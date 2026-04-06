# Production Deployment Guide

> **Last Updated**: 2026-04-05
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Audience**: Claude Code AI (primary executor) + Human developers (assistant/approver)
> **Estimated Time**: 2-4 hours (first deployment), 30-60 minutes (subsequent)
> **Related Guides**: [Customer Onboarding Runbook](./CUSTOMER-ONBOARDING-RUNBOOK.md) | [Incident Response](./INCIDENT-RESPONSE.md) | [Secret Rotation](./SECRET-ROTATION-PROCEDURES.md) | [Monitoring](./MONITORING-AND-ALERTING-GUIDE.md)

---

## How to Use This Guide

This guide is written for **dual execution**: Claude Code handles automated steps, human developers handle manual steps (portal actions, DNS, approvals).

### Legend

| Icon | Meaning |
|------|---------|
| **[AI]** | Claude Code executes this step autonomously via script/CLI |
| **[HUMAN]** | Human developer must perform this step (portal, DNS, approval) |
| **[AI+HUMAN]** | Claude Code runs the script; human verifies/approves output |
| **[DECISION]** | Decision point — Claude Code presents options, human chooses |

### Claude Code Execution Pattern

When Claude Code is asked to "deploy to production" or "set up production environment", it should:

1. Read this guide to determine which phase to execute
2. Check prerequisites (tools, access, quotas)
3. Execute **[AI]** steps using the scripts in `scripts/`
4. Pause at **[HUMAN]** steps and clearly tell the developer what to do
5. Resume after human confirmation

**Trigger phrases** that invoke this guide:
- "deploy to production"
- "set up production environment"
- "fresh production deployment"
- "deploy platform infrastructure"
- "provision customer for production"

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Architecture Overview](#2-architecture-overview)
   - [Build Once, Deploy Anywhere](#build-once-deploy-anywhere)
   - [Dataverse Environment Variables Reference](#dataverse-environment-variables-reference)
3. [Phase 1: Deploy Shared Platform](#3-phase-1-deploy-shared-platform)
4. [Phase 2: Entra ID App Registrations](#4-phase-2-entra-id-app-registrations)
5. [Phase 3: Configure Secrets and App Settings](#5-phase-3-configure-secrets-and-app-settings)
6. [Phase 4: Deploy BFF API](#6-phase-4-deploy-bff-api)
7. [Phase 5: Custom Domain and SSL](#7-phase-5-custom-domain-and-ssl)
8. [Phase 6: Provision First Customer](#8-phase-6-provision-first-customer)
   - [Set Dataverse Environment Variables (Step 8 of provisioning)](#86-manual-environment-variable-setup-alternative)
   - [Validate Deployed Environment (Step 13 of provisioning)](#85-validate-deployed-environment)
9. [Phase 7: Verify Deployment](#9-phase-7-verify-deployment)
10. [Day-2 Operations](#10-day-2-operations)
11. [Rollback Procedures](#11-rollback-procedures)
12. [Troubleshooting](#12-troubleshooting)
13. [Known Issues and Lessons Learned](#13-known-issues-and-lessons-learned)
14. [Reference: Resource Inventory](#14-reference-resource-inventory)
15. [Subscription and Resource Group Strategy](#15-subscription-and-resource-group-strategy)
16. [Dataverse Solution Lifecycle](#16-dataverse-solution-lifecycle)
17. [Managed vs Unmanaged Solutions](#17-managed-vs-unmanaged-solutions)
18. [Dataverse CI/CD and Deployment Automation](#18-dataverse-cicd-and-deployment-automation)
19. [SharePoint Embedded (SPE) Setup](#19-sharepoint-embedded-spe-setup)
20. [CI/CD Pipelines (Azure)](#20-cicd-pipelines-azure)

---

## 1. Prerequisites

### Required Tools

**[AI]** Claude Code verifies these automatically before any deployment step:

```powershell
# Claude Code runs these checks — no human action needed
az --version          # Azure CLI 2.60+
pwsh --version        # PowerShell 7.4+
dotnet --version      # .NET SDK 8.0+
pac --version         # PAC CLI (latest)
gh --version          # GitHub CLI 2.40+
```

| Tool | Minimum Version | Install Command |
|------|----------------|-----------------|
| Azure CLI | 2.60+ | `winget install Microsoft.AzureCLI` |
| PowerShell | 7.4+ | `winget install Microsoft.PowerShell` |
| .NET SDK | 8.0+ | `winget install Microsoft.DotNet.SDK.8` |
| PAC CLI | Latest | `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` |
| GitHub CLI | 2.40+ | `winget install GitHub.cli` |

> **PAC CLI on Windows**: `pac` is a `.cmd` wrapper. Scripts use `cmd /c pac` for output capture. Claude Code handles this automatically.

### Required Access

**[HUMAN]** Verify these before starting. Claude Code cannot check portal-level roles:

| Resource | Access Level | How to Verify |
|----------|-------------|---------------|
| Azure Subscription | Contributor + User Access Admin | `az account show` **[AI]** |
| Entra ID Tenant | Application Administrator | Azure Portal > Entra ID > Roles **[HUMAN]** |
| Power Platform | Global Admin or Dynamics 365 Admin | Power Platform Admin Center **[HUMAN]** |
| GitHub Repository | Write access | `gh auth status` **[AI]** |
| DNS Management | Record creation for `api.spaarke.com` | Varies by provider **[HUMAN]** |

### Required Azure Quotas

**[DECISION]** Before deploying, verify quotas. If quotas are insufficient, request increases (1-3 business days). Azure OpenAI regional availability changes frequently — confirm westus3 capacity for GPT-4o before starting provisioning.

| Resource | Required | Region | Notes |
|----------|----------|--------|-------|
| App Service Plan (P1v3) | 1 | westus2 | Production workload |
| Azure OpenAI (GPT-4o) | 50K+ TPM | **westus3** | Not available in westus2 — see [Known Issues](#13-known-issues-and-lessons-learned) |
| Azure OpenAI (GPT-4o-mini) | 100K TPM | **westus3** | Same region as GPT-4o |
| Azure OpenAI (text-embedding-3-large) | 100K TPM | **westus3** | Same region |
| AI Search (Standard) | 1 service | westus2 | Standard SKU (not Standard2 — see Known Issues) |
| Document Intelligence (S0) | 1 | westus2 | |

### Authentication Setup

**[AI]** Claude Code runs these commands. Human provides credentials if not already cached:

```powershell
# Azure CLI
az login
az account set --subscription "<subscription-name>"
az account show  # Verify correct subscription

# PAC CLI (for Dataverse operations)
pac auth create --environment "https://<org>.crm.dynamics.com"
pac auth list   # Verify connection
```

---

## 2. Architecture Overview

```
┌─────────────────────────────────────────────────────────────────┐
│ Shared Platform  (rg-spaarke-platform-prod)                     │
│                                                                 │
│  ┌─────────────────┐  ┌──────────────────┐  ┌───────────────┐  │
│  │ App Service Plan │  │ Azure OpenAI     │  │ AI Search     │  │
│  │ (P1v3)          │  │ (westus3)        │  │ (Standard)    │  │
│  │  ├─ BFF API      │  │  ├─ gpt-4o       │  │ (westus2)     │  │
│  │  └─ Staging Slot │  │  ├─ gpt-4o-mini  │  │               │  │
│  └─────────────────┘  │  └─ embed-3-large │  └───────────────┘  │
│                        └──────────────────┘                     │
│  ┌─────────────────┐  ┌──────────────────┐  ┌───────────────┐  │
│  │ Doc Intelligence│  │ App Insights +   │  │ Platform      │  │
│  │ (S0, westus2)   │  │ Log Analytics    │  │ Key Vault     │  │
│  └─────────────────┘  └──────────────────┘  └───────────────┘  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│ Per-Customer  (rg-spaarke-{customerId}-prod)                    │
│                                                                 │
│  ┌──────────┐  ┌──────────┐  ┌──────────────┐  ┌────────────┐  │
│  │ Storage  │  │ Customer │  │ Service Bus  │  │ Redis      │  │
│  │ Account  │  │ Key Vault│  │ Namespace    │  │ Cache      │  │
│  └──────────┘  └──────────┘  └──────────────┘  └────────────┘  │
│                                                                 │
│  + Dataverse Environment (Power Platform)                       │
│  + SPE Containers (SharePoint Embedded)                         │
└─────────────────────────────────────────────────────────────────┘
```

### Naming Convention

All resources follow `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` (current: v3.0, production deployed under v2 patterns):

| Pattern | Example |
|---------|---------|
| `rg-spaarke-{scope}-{env}` | `rg-spaarke-platform-prod` |
| `spaarke-{purpose}-{env}` | `spaarke-bff-prod` |
| `sprk-{purpose}-{env}-kv` | `sprk-platform-prod-kv` |
| `sprk-{customer}-{env}-kv` | `sprk-demo-prod-kv` |
| `sprk{customer}{env}sa` | `sprkdemoprodsa` |
| `sprk-{customer}-{env}-sbus` | `sprk-demo-prod-sbus` |

> **Note**: Service Bus uses `-sbus` suffix, NOT `-sb` (reserved by Azure).

### Build Once, Deploy Anywhere

Spaarke uses an **environment-agnostic build** strategy. All client-side components (PCF controls, Code Pages, legacy JS webresources, Office Add-ins) resolve configuration at runtime from **Dataverse Environment Variables** — no environment-specific values are baked into any build artifact.

**How it works:**

1. **Build artifacts are identical** across dev, UAT, staging, and production. The same compiled PCF controls, Code Pages, and solution ZIPs are promoted through environments without rebuilding.
2. **Runtime configuration resolution** — At startup, client components call `resolveRuntimeConfig()` (from `@spaarke/auth`) which queries Dataverse Environment Variables via the Web API and caches the result.
3. **Seven canonical environment variables** define the complete runtime configuration per environment (see [Dataverse Environment Variables Reference](#dataverse-environment-variables-reference) below).
4. **No dev defaults** — If an environment variable is missing, components fail loudly with a clear error message rather than silently falling back to dev values.

**Benefits:**

- Same solution ZIP deploys to every environment — no per-environment build pipelines
- Configuration is visible and auditable in the Dataverse UI (Environment Variables section)
- `Provision-Customer.ps1` sets all variables automatically during customer onboarding
- `Validate-DeployedEnvironment.ps1` verifies correct configuration post-deployment

### Dataverse Environment Variables Reference

These 7 Dataverse Environment Variables are defined in the Spaarke solution and must be set for every deployment target:

| Variable | Schema Name | Purpose | Example Value |
|----------|-------------|---------|---------------|
| BFF API Base URL | `sprk_BffApiBaseUrl` | Base URL for all BFF API calls from client components | `https://api.spaarke.com/api` |
| BFF API App ID | `sprk_BffApiAppId` | Azure AD App Registration ID for BFF API (used as OAuth scope audience) | `api://bff-api-prod-app-id` |
| MSAL Client ID | `sprk_MsalClientId` | MSAL Client ID for Dataverse-hosted SPAs (Code Pages, External SPA) | `12345678-1234-1234-1234-123456789012` |
| Tenant ID | `sprk_TenantId` | Azure AD Tenant ID for authentication | `a221a95e-6abc-4434-aecc-e48338a1b2f2` |
| Azure OpenAI Endpoint | `sprk_AzureOpenAiEndpoint` | Azure OpenAI service endpoint for AI features | `https://spaarke-openai-prod.openai.azure.com/` |
| Share Link Base URL | `sprk_ShareLinkBaseUrl` | Base URL for generating shareable document links | `https://app.spaarke.com/share` |
| SPE Container ID | `sprk_SharePointEmbeddedContainerId` | SharePoint Embedded container ID for document storage | `b1c2d3e4-f5a6-7890-abcd-ef1234567890` |

> **Important**: These variables are defined as part of the Spaarke Dataverse solution (solution XML). After solution import, values must be set per environment — either via `Provision-Customer.ps1` (automated) or via the Power Platform Admin Center (manual).

---

## 3. Phase 1: Deploy Shared Platform

### 3.1 Review Parameter File

**[AI]** Claude Code reads and validates the parameter file:

```powershell
# Parameter file location
infrastructure/bicep/parameters/platform-prod.bicepparam

# Key parameters to verify:
# - environmentName = 'prod'
# - location = 'westus2'
# - openAiLocation = 'westus3'           ← CRITICAL: OpenAI not available in westus2
# - appServicePlanSku = 'P1v3'
# - aiSearchSku = 'standard'             ← NOT standard2 (see Known Issues)
# - aiSearchReplicaCount = 2
```

### 3.2 Preview Deployment (What-If)

**[AI+HUMAN]** Claude Code runs the preview; human reviews output:

```powershell
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod -WhatIf
```

The script performs:
1. Pre-flight checks (Azure CLI logged in, Bicep template exists, parameter file exists)
2. Subscription-scoped `az deployment sub what-if`
3. Outputs expected resource changes

**Human reviews**: Verify no unexpected deletions, all expected resources listed.

### 3.3 Deploy Platform

**[AI]** Claude Code executes:

```powershell
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod
```

**What happens** (10-20 minutes):
1. Pre-flight checks pass
2. Subscription-scoped Bicep deployment creates `rg-spaarke-platform-prod`
3. Deploys: App Service Plan, App Service + staging slot, Azure OpenAI (westus3), AI Search (westus2), Document Intelligence, App Insights, Log Analytics, Platform Key Vault
4. Post-deployment validation checks resource health

**Expected outputs**: Resource group name, API URL, Key Vault name, AI endpoints.

### 3.4 Verify Platform

**[AI]** Claude Code runs verification:

```powershell
# Resource group exists
az group show --name rg-spaarke-platform-prod --query "{name:name, location:location}" -o table

# App Service running
az webapp show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query "{state:state, defaultHostName:defaultHostName}" -o table

# Key Vault accessible
az keyvault show --name sprk-platform-prod-kv --query "{name:name, location:location}" -o table
```

---

## 4. Phase 2: Entra ID App Registrations

### 4.1 Create App Registrations (Automated)

**[AI]** Claude Code runs the registration script:

```powershell
# Preview first
.\scripts\Register-EntraAppRegistrations.ps1 -DryRun

# Create registrations
.\scripts\Register-EntraAppRegistrations.ps1
```

This creates two registrations in tenant `a221a95e-6abc-4434-aecc-e48338a1b2f2`:

| App Name | Purpose | API Permissions |
|----------|---------|-----------------|
| `spaarke-bff-api-prod` | BFF API identity | Graph: Files.Read.All, FileStorageContainer.Selected, Sites.Read.All; Dynamics CRM: user_impersonation |
| `spaarke-dataverse-s2s-prod` | Dataverse S2S | Dynamics CRM: user_impersonation |

The script:
- Creates app registrations with correct permissions
- Generates 24-month client secrets
- Stores secrets in `sprk-platform-prod-kv` (Key Vault)
- Configures redirect URIs and exposed API scopes

### 4.2 Grant Admin Consent

**[HUMAN]** Admin consent must be granted in the Azure Portal:

1. Navigate to **Azure Portal > Entra ID > App registrations**
2. Select `spaarke-bff-api-prod`
3. Go to **API permissions** > Click **Grant admin consent for [tenant]**
4. Repeat for `spaarke-dataverse-s2s-prod`

### 4.3 Register Dataverse Application User

**[HUMAN]** Must be done in Power Platform Admin Center:

1. Navigate to **Power Platform Admin Center > Environments > [prod env] > Settings > Users**
2. Click **+ New app user**
3. Select the `spaarke-dataverse-s2s-prod` app registration
4. Assign **System Administrator** security role

### 4.4 Verify Registrations

**[AI]** Claude Code verifies:

```powershell
.\scripts\Test-EntraAppRegistrations.ps1
```

This checks:
- Both app registrations exist with correct permissions
- Secrets are stored in Key Vault
- Token acquisition succeeds for both apps

### 4.5 Grant Managed Identity Key Vault Access

**[AI]** Claude Code runs:

```powershell
# Get App Service managed identity
$principalId = az webapp identity show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query principalId -o tsv

# Grant Key Vault Secrets User role (production slot)
az role assignment create `
    --role "Key Vault Secrets User" `
    --assignee-object-id $principalId `
    --scope "/subscriptions/<sub-id>/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.KeyVault/vaults/sprk-platform-prod-kv"

# CRITICAL: Also grant to staging slot (different managed identity!)
$stagingPrincipalId = az webapp identity show -g rg-spaarke-platform-prod -n spaarke-bff-prod --slot staging --query principalId -o tsv

az role assignment create `
    --role "Key Vault Secrets User" `
    --assignee-object-id $stagingPrincipalId `
    --scope "/subscriptions/<sub-id>/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.KeyVault/vaults/sprk-platform-prod-kv"
```

> **CRITICAL**: The staging slot has a **different managed identity** than production. Both need Key Vault access or deployments to the staging slot will fail with 503.

---

## 5. Phase 3: Configure Secrets and App Settings

### 5.1 Seed Key Vault Secrets

**[AI]** Claude Code runs the seeding script (if available):

```powershell
.\scripts\Seed-ProductionKeyVault.ps1
```

Or manually set critical secrets:

```powershell
$kvName = "sprk-platform-prod-kv"

# Azure AD (from Register-EntraAppRegistrations.ps1 — already stored if script ran)
az keyvault secret set --vault-name $kvName --name "AzureAd--TenantId" --value "<tenant-id>"
az keyvault secret set --vault-name $kvName --name "AzureAd--ClientId" --value "<bff-api-client-id>"
az keyvault secret set --vault-name $kvName --name "AzureAd--ClientSecret" --value "<bff-api-secret>"

# Dataverse S2S
az keyvault secret set --vault-name $kvName --name "Dataverse--ClientId" --value "<s2s-client-id>"
az keyvault secret set --vault-name $kvName --name "Dataverse--ClientSecret" --value "<s2s-secret>"

# Azure OpenAI (from deployment outputs)
$openaiKey = az cognitiveservices account keys list -g rg-spaarke-platform-prod -n spaarke-openai-prod --query "key1" -o tsv
az keyvault secret set --vault-name $kvName --name "AzureOpenAI--ApiKey" --value "$openaiKey"

# AI Search
$searchKey = az search admin-key show -g rg-spaarke-platform-prod --service-name spaarke-search-prod --query "primaryKey" -o tsv
az keyvault secret set --vault-name $kvName --name "AiSearch--ApiKey" --value "$searchKey"

# Document Intelligence
$docIntelKey = az cognitiveservices account keys list -g rg-spaarke-platform-prod -n spaarke-docintel-prod --query "key1" -o tsv
az keyvault secret set --vault-name $kvName --name "DocumentIntelligence--ApiKey" --value "$docIntelKey"
```

### 5.2 Configure App Settings with Key Vault References

**[AI]** Claude Code runs:

```powershell
.\scripts\Configure-ProductionAppSettings.ps1
```

Or set individually. App settings use the `@Microsoft.KeyVault(...)` pattern — **no plaintext secrets in app settings**:

| Setting | Value |
|---------|-------|
| `AzureAd__TenantId` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureAd--TenantId)` |
| `AzureAd__ClientId` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureAd--ClientId)` |
| `AzureAd__ClientSecret` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureAd--ClientSecret)` |
| `AzureOpenAI__Endpoint` | `https://spaarke-openai-prod.openai.azure.com/` |
| `AzureOpenAI__ApiKey` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureOpenAI--ApiKey)` |
| `AiSearch__Endpoint` | `https://spaarke-search-prod.search.windows.net/` |
| `AiSearch__ApiKey` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AiSearch--ApiKey)` |
| `DocumentIntelligence__Endpoint` | `https://westus2.api.cognitive.microsoft.com/` |
| `DocumentIntelligence__ApiKey` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=DocumentIntelligence--ApiKey)` |
| `Dataverse__OrgUrl` | `https://<prod-org>.crm.dynamics.com` |
| `Dataverse__ClientId` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=Dataverse--ClientId)` |
| `Dataverse__ClientSecret` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=Dataverse--ClientSecret)` |
| `Redis__Enabled` | `false` (until per-customer Redis provisioned) |

Full non-secret configuration is in `src/server/api/Sprk.Bff.Api/appsettings.Production.json.template` (30 Key Vault references, zero plaintext secrets).

---

## 6. Phase 4: Deploy BFF API

### 6.1 Build and Deploy with Zero-Downtime

**[AI]** Claude Code executes the production deployment:

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy
```

**What happens** (3-5 minutes):
1. `dotnet publish -c Release` builds the API (~65 MB zip)
2. Deploys zip to `staging` slot
3. Waits for staging health check (`/healthz` → 200)
4. Swaps staging → production (zero-downtime)
5. Verifies production health check
6. **Auto-rollback** if production health check fails after swap

### 6.2 Skip Build (Redeploy Existing)

**[AI]** For redeployments without code changes:

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy `
    -SkipBuild
```

### 6.3 Verify

**[AI]** Claude Code checks:

```powershell
# Direct URL
curl https://spaarke-bff-prod.azurewebsites.net/healthz
# Expected: Healthy (200)
```

---

## 7. Phase 5: Custom Domain and SSL

### 7.1 Get DNS Configuration Instructions

**[AI]** Claude Code runs:

```powershell
.\scripts\Configure-CustomDomain.ps1 -ShowDnsInstructions
```

This outputs the **two required DNS records**.

### 7.2 Create DNS Records

**[HUMAN]** In your DNS provider, create **BOTH** records:

| Type | Name | Value | TTL |
|------|------|-------|-----|
| **CNAME** | `api.spaarke.com` | `spaarke-bff-prod.azurewebsites.net` | 3600 |
| **TXT** | `asuid.api.spaarke.com` | `<verification-id from Step 7.1>` | 3600 |

> **CRITICAL**: Both CNAME and TXT records are **required** by Azure App Service. The TXT record is the domain verification token. Without it, the custom domain binding will fail.

To get the verification ID manually:

```powershell
az webapp show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query "customDomainVerificationId" -o tsv
```

### 7.3 Wait for DNS Propagation

**[HUMAN]** Verify DNS propagation before proceeding:

```powershell
nslookup api.spaarke.com
# Should resolve to spaarke-bff-prod.azurewebsites.net

nslookup -type=TXT asuid.api.spaarke.com
# Should return the verification ID
```

Propagation typically takes 5-30 minutes, but can take up to 24 hours.

### 7.4 Configure Custom Domain and SSL

**[AI]** Once DNS is confirmed, Claude Code runs:

```powershell
.\scripts\Configure-CustomDomain.ps1
```

The script performs:
1. Validates DNS records (CNAME + TXT) resolve correctly
2. Adds custom domain hostname binding to App Service
3. Creates Azure-managed SSL certificate (auto-renewal)
4. Binds SSL with SNI
5. Enforces HTTPS-only (HTTP → HTTPS redirect)
6. Configures CORS for production origins

### 7.5 Verify Custom Domain

**[AI]** Claude Code verifies:

```powershell
.\scripts\Test-CustomDomain.ps1
```

Or manually:

```powershell
curl https://api.spaarke.com/healthz
# Expected: Healthy (200)
```

---

## 8. Phase 6: Provision First Customer

### 8.1 Preview Provisioning

**[AI+HUMAN]** Preview what will be created:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<service-principal-client-id>" `
    -ClientSecret "<service-principal-secret>" `
    -WhatIf
```

### 8.2 Execute Provisioning

**[AI]** Claude Code runs:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

**10-step pipeline** (20-30 minutes):

| Step | Description | Duration | Can Claude Execute? |
|------|-------------|----------|---------------------|
| 1 | Validate inputs and prerequisites | 10s | Yes |
| 2 | Create resource group `rg-spaarke-demo-prod` | 15s | Yes |
| 3 | Deploy customer.bicep (Storage, Key Vault, Service Bus, Redis) | 5-8 min | Yes |
| 4 | Populate customer Key Vault with secrets | 30s | Yes |
| 5 | Create Dataverse environment via Admin API | 30s | Yes |
| 6 | Wait for Dataverse provisioning | 5-15 min | Yes (waits) |
| 7 | Import managed solutions (10 solutions in dependency order) | 5-10 min | Yes |
| 8 | Set Dataverse Environment Variables (7 required variables) | 30s | Yes |
| 9 | Generate `environment-config.json` | 5s | Yes |
| 10 | Provision SPE containers | 1-2 min | Yes |
| 11 | Register in BFF API tenant registry | 15s | Yes |
| 12 | Run smoke tests (`Test-Deployment.ps1`) | 1-2 min | Yes |
| 13 | Validate deployed environment (`Validate-DeployedEnvironment.ps1`) | 30s | Yes |

> **Step 8** sets all 7 Dataverse Environment Variables listed in the [Dataverse Environment Variables Reference](#dataverse-environment-variables-reference). Step 9 writes `environment-config.json` as the canonical configuration reference for the customer. Step 13 runs the full validation suite to confirm no configuration issues remain.

The script is **idempotent and resumable**. If it fails at any step, re-run with `-ResumeFromStep`:

```powershell
# Resume from step 5 (Dataverse creation)
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>" `
    -ResumeFromStep 5
```

### 8.3 Load Sample Data (Demo Only)

**[AI]** For the demo customer:

```powershell
.\scripts\Load-DemoSampleData.ps1
```

### 8.4 Configure Demo User Access (Demo Only)

**[AI]** Invite demo users via B2B guest invitations:

```powershell
.\scripts\Invite-DemoUsers.ps1
```

### 8.5 Validate Deployed Environment

**[AI]** After provisioning completes (or after any manual configuration changes), run the validation script to confirm the environment is correctly configured:

```powershell
.\scripts\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://contoso.crm.dynamics.com"
```

Or with an explicit BFF API URL:

```powershell
.\scripts\Validate-DeployedEnvironment.ps1 `
    -DataverseUrl "https://contoso.crm.dynamics.com" `
    -BffApiUrl "https://api.spaarke.com/api"
```

**What it checks (4 categories):**

| Category | Checks Performed | Pass Criteria |
|----------|-----------------|---------------|
| **Env Vars** | All 7 Dataverse Environment Variables exist and have non-empty values | All 7 variables present with values |
| **BFF API** | `GET /healthz` and `GET /ping` return HTTP 200 | Both endpoints healthy |
| **CORS** | Preflight OPTIONS request includes Dataverse origin in `Access-Control-Allow-Origin` | Dataverse URL allowed |
| **Dev Leakage** | Scans env var values for dev-only identifiers (`spaarkedev1`, `spe-api-dev`, `67e2xz`, known dev GUIDs) | No dev values detected |

**Expected output:**

```
====================================================================
  RESULTS SUMMARY
====================================================================

  [PASS] Env Vars            Pass: 7  Fail: 0  Warn: 0
  [PASS] BFF API             Pass: 2  Fail: 0  Warn: 0
  [PASS] CORS                Pass: 1  Fail: 0  Warn: 0
  [PASS] Dev Leakage         Pass: 6  Fail: 0  Warn: 0

  Total:  16 checks
  Pass:   16

  VERDICT: PASSED - All checks successful. Environment is correctly configured.
```

> **When to run**: After initial provisioning, after solution upgrades, after manual env var changes, and as part of any deployment verification workflow.

### 8.6 Manual Environment Variable Setup (Alternative)

If not using `Provision-Customer.ps1`, set environment variables manually:

**Via Power Platform Admin Center (UI):**

1. Open **Power Platform Admin Center** (https://admin.powerplatform.microsoft.com)
2. Navigate to **Environments** > Select your environment > **Solutions**
3. Open the **Spaarke** solution > **Environment Variables**
4. Set all 7 variables per the [Dataverse Environment Variables Reference](#dataverse-environment-variables-reference)

**Via PAC CLI:**

```powershell
pac env var set --name sprk_BffApiBaseUrl --value "https://api.spaarke.com/api"
pac env var set --name sprk_BffApiAppId --value "api://bff-api-prod-app-id"
pac env var set --name sprk_MsalClientId --value "<msal-client-id>"
pac env var set --name sprk_TenantId --value "<tenant-id>"
pac env var set --name sprk_AzureOpenAiEndpoint --value "https://spaarke-openai-prod.openai.azure.com/"
pac env var set --name sprk_ShareLinkBaseUrl --value "https://app.spaarke.com/share"
pac env var set --name sprk_SharePointEmbeddedContainerId --value "<spe-container-id>"
```

After setting variables manually, always run the validation script (Section 8.5) to confirm correctness.

---

## 9. Phase 7: Verify Deployment

### 9.1 Run Smoke Test Suite

**[AI]** Claude Code runs:

```powershell
.\scripts\Test-Deployment.ps1 -EnvironmentName prod
```

**17 tests across 6 groups**:

| Group | Tests | What It Checks |
|-------|-------|----------------|
| BFF API Health | 3 | `/healthz`, `/ping`, response headers |
| Dataverse | 3 | Connection, solution presence, WhoAmI |
| SPE | 2 | Container exists, file operations |
| AI Services | 4 | OpenAI chat, embeddings, AI Search query, Doc Intelligence |
| Service Bus | 2 | Connection, send/receive |
| Redis | 3 | Connection, set/get, TTL |

### 9.2 Manual Verification Checklist

**[AI+HUMAN]** Claude Code runs the commands; human confirms expectations:

| Check | Command | Expected |
|-------|---------|----------|
| API health | `curl https://api.spaarke.com/healthz` | `Healthy` (200) |
| SSL cert valid | `curl -vI https://api.spaarke.com 2>&1 \| grep "subject"` | Valid cert |
| App running | `az webapp show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query state` | `Running` |
| Key Vault | `az keyvault secret list --vault-name sprk-platform-prod-kv --query "[].name"` | 10+ secrets |
| Platform RG | `az group show -n rg-spaarke-platform-prod` | Exists |
| Customer RG | `az group show -n rg-spaarke-demo-prod` | Exists |
| Solutions | `pac solution list` (after `pac auth create`) | 10 managed solutions |

---

## 10. Day-2 Operations

### Subsequent BFF API Deployments

**[AI]** After initial setup, deploying new API versions:

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy
```

### Provisioning Additional Customers

**[AI]** Same script, different CustomerId:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -DisplayName "Acme Legal" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

See [Customer Onboarding Runbook](./CUSTOMER-ONBOARDING-RUNBOOK.md) for the complete lifecycle.

### Customer Decommissioning

**[AI+HUMAN]** Preview first, then execute with confirmation:

```powershell
# Preview what would be deleted
.\scripts\Decommission-Customer.ps1 -CustomerId "test" -Environment prod -DryRun

# Execute (will prompt for confirmation)
.\scripts\Decommission-Customer.ps1 `
    -CustomerId "test" `
    -Environment prod `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

Removes: Azure resource group, Dataverse environment, SPE containers, tenant registry entry.

> **Safety**: Platform resource groups (`rg-spaarke-platform-*`) are explicitly blocked from deletion.

### Secret Rotation

**[AI]** Regular rotation (recommended every 90 days):

```powershell
# Preview
.\scripts\Rotate-Secrets.ps1 -Scope Platform -SecretType All -DryRun

# Execute platform secrets
.\scripts\Rotate-Secrets.ps1 -Scope Platform -SecretType All

# Execute customer secrets
.\scripts\Rotate-Secrets.ps1 -Scope Customer -CustomerId "demo" -SecretType All
```

Supported types: `StorageKey`, `ServiceBus`, `Redis`, `EntraId`, `All`.

See [Secret Rotation Procedures](./SECRET-ROTATION-PROCEDURES.md) for the full runbook.

### Viewing Logs

**[AI]** Claude Code runs:

```powershell
# Live log stream
az webapp log tail -g rg-spaarke-platform-prod -n spaarke-bff-prod

# Staging slot logs
az webapp log tail -g rg-spaarke-platform-prod -n spaarke-bff-prod --slot staging
```

For App Insights KQL queries, see [Monitoring and Alerting Guide](./MONITORING-AND-ALERTING-GUIDE.md).

---

## 11. Rollback Procedures

### BFF API Rollback

**[AI]** Automatic rollback is built into `Deploy-BffApi.ps1` when `-UseSlotDeploy` is used.

For manual rollback after a completed swap:

```powershell
# Previous version is in the staging slot after a swap — swap back
az webapp deployment slot swap `
    -g rg-spaarke-platform-prod `
    -n spaarke-bff-prod `
    --slot staging `
    --target-slot production
```

Verify: `curl https://api.spaarke.com/healthz`

### Platform Infrastructure Rollback

**[AI]** Bicep is idempotent. Redeploy previous version:

```powershell
git checkout <previous-commit> -- infrastructure/bicep/
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod
```

### Dataverse Solution Rollback

**[HUMAN]** Managed solutions support version rollback in Power Platform Admin Center:
1. Environment > Solutions > Select solution > Solution history
2. Import the previous managed solution version

---

## 12. Troubleshooting

### Decision Tree for Claude Code

When troubleshooting, Claude Code should follow this decision tree:

```
API returns 503 after deployment?
  → Check staging slot Key Vault RBAC (Section 4.5)
  → Check managed identity principal ID for BOTH slots
  → Restart: az webapp restart -g rg-spaarke-platform-prod -n spaarke-bff-prod

Health check fails on staging slot?
  → Staging slot has DIFFERENT managed identity than production
  → Grant Key Vault Secrets User to staging slot's principal ID

Bicep deployment fails with QuotaExceeded?
  → Check Known Issues section for OpenAI region and AI Search SKU
  → Request quota increases in Azure Portal > Subscription > Usage + quotas

Custom domain binding fails?
  → Verify BOTH DNS records exist: CNAME + TXT (asuid.*)
  → Get verification ID: az webapp show ... --query customDomainVerificationId
  → Wait for DNS propagation (nslookup to verify)

Dataverse environment creation times out?
  → Check Power Platform Admin Center for status
  → If created but script timed out: -ResumeFromStep 7

PAC CLI output capture fails?
  → PAC is a .cmd wrapper on Windows
  → Use: cmd /c pac <command>

Redis connection refused?
  → Set Redis__Enabled=false until per-customer Redis provisioned
  → az webapp config appsettings set -g ... --settings Redis__Enabled=false
```

### Common Issues Reference

| Symptom | Cause | Fix |
|---------|-------|-----|
| 503 after deploy | Key Vault refs not resolving | Grant managed identity `Key Vault Secrets User` role |
| Staging slot 503 | Different managed identity | Grant staging slot separate RBAC |
| `QuotaExceeded` on OpenAI | Region capacity | Deploy to westus3 with `openAiLocation` parameter |
| `SkuNotAvailable` on AI Search | Standard2 unavailable | Use `standard` SKU instead |
| DNS TXT record error | Missing verification record | Add `asuid.api.spaarke.com` TXT record |
| PAC CLI hangs | .cmd wrapper issue | Use `cmd /c pac` for output capture |
| Redis timeout | Not provisioned yet | Set `Redis__Enabled=false` |
| Service Bus name conflict | `-sb` suffix reserved | Use `-sbus` suffix |
| Solution import fails | Dependency order wrong | `Deploy-DataverseSolutions.ps1` handles ordering |

---

## 13. Known Issues and Lessons Learned

These were discovered during the initial March 2026 production deployment and are critical for future deployments.

### Azure OpenAI Not Available in westus2

**Issue**: Azure OpenAI GPT-4o and GPT-4o-mini are NOT available in westus2.

**Resolution**: Deploy OpenAI to `westus3` using the `openAiLocation` parameter in `platform-prod.bicepparam`. All other resources remain in westus2.

```bicep
// in platform-prod.bicepparam
param openAiLocation = 'westus3'
```

**Impact**: GPT-4o capacity limited to 50K TPM in westus3 (lower than westus2 would have offered).

### AI Search Standard2 Not Available

**Issue**: AI Search `standard2` SKU provisioning fails in westus2 with insufficient capacity.

**Resolution**: Downgraded to `standard` SKU. Sufficient for current workload. Monitor and upgrade when capacity is available.

### Service Bus Naming — `-sb` Is Reserved

**Issue**: Azure rejects Service Bus names ending in `-sb`.

**Resolution**: Customer Bicep template uses `-sbus` suffix: `sprk-{customerId}-{env}-sbus`.

### Custom Domain Requires BOTH CNAME and TXT Records

**Issue**: Azure App Service custom domain binding requires TWO DNS records, not just CNAME. The TXT record (`asuid.api.spaarke.com`) contains a domain verification token.

**Resolution**: `Configure-CustomDomain.ps1` now prominently shows both required records. The `-ShowDnsInstructions` flag outputs exact values.

### Staging Slot Has Different Managed Identity

**Issue**: After deploying to staging slot, health check fails with 503 because Key Vault references can't resolve.

**Resolution**: The staging slot's managed identity needs its own `Key Vault Secrets User` role assignment. This is a separate principal ID from the production slot.

### PAC CLI on Windows Is a .cmd Wrapper

**Issue**: `pac` commands fail to capture output in PowerShell because `pac` is a `.cmd` file, not a native executable.

**Resolution**: Use `cmd /c pac <args>` for output capture in scripts. The `Test-Deployment.ps1` script handles this automatically.

### Document Intelligence API Version

**Issue**: The legacy `formrecognizer/info` endpoint no longer works.

**Resolution**: Use `formrecognizer/documentModels` with GA API version `2024-11-30`. `Test-Deployment.ps1` uses the correct endpoint.

---

## 14. Reference: Resource Inventory

### Production Platform Resources

| Resource | Name | Resource Group | Region |
|----------|------|----------------|--------|
| App Service Plan | `spaarke-bff-prod-plan` | `rg-spaarke-platform-prod` | westus2 |
| App Service | `spaarke-bff-prod` | `rg-spaarke-platform-prod` | westus2 |
| Staging Slot | `spaarke-bff-prod/staging` | `rg-spaarke-platform-prod` | westus2 |
| Azure OpenAI | `spaarke-openai-prod` | `rg-spaarke-platform-prod` | **westus3** |
| AI Search | `spaarke-search-prod` | `rg-spaarke-platform-prod` | westus2 |
| Document Intelligence | `spaarke-docintel-prod` | `rg-spaarke-platform-prod` | westus2 |
| Key Vault | `sprk-platform-prod-kv` | `rg-spaarke-platform-prod` | westus2 |
| App Insights | `spaarke-appinsights-prod` | `rg-spaarke-platform-prod` | westus2 |
| Log Analytics | `spaarke-logs-prod` | `rg-spaarke-platform-prod` | westus2 |

### Per-Customer Resources (Template)

| Resource | Name Pattern | Resource Group |
|----------|-------------|----------------|
| Storage Account | `sprk{customerId}{env}sa` | `rg-spaarke-{customerId}-{env}` |
| Key Vault | `sprk-{customerId}-{env}-kv` | `rg-spaarke-{customerId}-{env}` |
| Service Bus | `sprk-{customerId}-{env}-sbus` | `rg-spaarke-{customerId}-{env}` |
| Redis Cache | `sprk-{customerId}-{env}-redis` | `rg-spaarke-{customerId}-{env}` |
| Dataverse Env | `spaarke-{customerId}` | (Power Platform) |

### Key Endpoints

| Service | URL |
|---------|-----|
| BFF API (production) | `https://api.spaarke.com` |
| BFF API (direct) | `https://spaarke-bff-prod.azurewebsites.net` |
| Health check | `https://api.spaarke.com/healthz` |
| OpenAI | `https://spaarke-openai-prod.openai.azure.com/` |
| AI Search | `https://spaarke-search-prod.search.windows.net/` |
| Doc Intelligence | `https://westus2.api.cognitive.microsoft.com/` |

### Entra ID App Registrations

| App Name | Purpose |
|----------|---------|
| `spaarke-bff-api-prod` | BFF API identity (Graph, SPE, Dataverse) |
| `spaarke-dataverse-s2s-prod` | Server-to-server Dataverse access |

---

## 15. Subscription and Resource Group Strategy

> **ADR**: See [ADR-027: Subscription Isolation and Dataverse Solution Management](../../docs/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md)

### Resource Group Model

Spaarke uses **explicit resource groups** for isolation. These are created automatically by Bicep templates (subscription-scoped deployments):

| Scope | Resource Group | Created By | Contains |
|-------|---------------|------------|----------|
| Shared platform | `rg-spaarke-platform-{env}` | `platform.bicep` via `Deploy-Platform.ps1` | App Service, OpenAI, AI Search, Doc Intel, Key Vault, Monitoring |
| Per-customer | `rg-spaarke-{customerId}-{env}` | `customer.bicep` via `Provision-Customer.ps1` | Storage, Key Vault, Service Bus, Redis |

Both Bicep templates use `targetScope = 'subscription'`, which means the resource group is **created as part of the deployment** — no manual `az group create` needed.

```bicep
// platform.bicep — subscription-scoped
targetScope = 'subscription'

resource resourceGroup 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: 'rg-spaarke-platform-${environmentName}'
  location: location
}
```

### Subscription Strategy

**[DECISION]** Choose a subscription model based on organizational requirements:

| Model | Description | When to Use | Current Status |
|-------|-------------|-------------|----------------|
| **A: Single subscription** | Dev + Prod in same subscription, isolated by resource groups | Small teams, startups, single-operator | **Current** |
| **B: Env-separated subscriptions** | Separate subscriptions for Dev and Prod | **Recommended for production** — prevents blast radius, enables RBAC isolation | Planned |
| **C: Customer-separated subscriptions** | Each customer gets their own subscription | Enterprise customers requiring billing isolation or data sovereignty | Future consideration |

#### Model B: Environment-Separated Subscriptions (Recommended)

```
┌─────────────────────────────────────────────┐
│ Management Group: Spaarke                    │
│                                              │
│  ┌───────────────────┐  ┌─────────────────┐ │
│  │ Dev Subscription   │  │ Prod Subscription│ │
│  │                    │  │                  │ │
│  │ rg-spaarke-        │  │ rg-spaarke-      │ │
│  │   platform-dev     │  │   platform-prod  │ │
│  │ rg-spaarke-        │  │ rg-spaarke-      │ │
│  │   demo-dev         │  │   demo-prod      │ │
│  └───────────────────┘  │ rg-spaarke-      │ │
│                          │   acme-prod      │ │
│                          └─────────────────┘ │
└─────────────────────────────────────────────┘
```

**To implement Model B:**
1. Create a second Azure subscription for production
2. Update `Deploy-Platform.ps1` and `Provision-Customer.ps1` to accept a `-SubscriptionId` parameter
3. Add `az account set --subscription` at the start of each script
4. Use separate service principals per subscription (RBAC isolation)
5. GitHub Actions workflows use different `AZURE_SUBSCRIPTION_ID` secrets per environment

**Impact on existing scripts**: Minimal — all scripts already parameterize resource group names and environment names. The subscription is the only new parameter.

#### Model C: Customer-Separated Subscriptions

For enterprise customers requiring subscription-level isolation:
- `customer.bicep` deploys to the customer's subscription
- Service principal needs `Contributor` on the customer's subscription
- Cross-subscription networking (VNet peering) may be needed for BFF API → customer resources
- Significant architectural change — not currently supported

### What Claude Code Should Do

When deploying, Claude Code should:
1. Verify the correct subscription is active: `az account show`
2. If Model B is adopted, switch subscriptions before each phase
3. Never deploy platform resources into a customer resource group or vice versa
4. Use `Deploy-Platform.ps1 -WhatIf` to verify resource group targeting before actual deployment

---

## 16. Dataverse Solution Lifecycle

### What's in the Solutions

Spaarke Dataverse solutions contain all platform customizations:

| Component Type | Examples | Solution |
|----------------|----------|----------|
| Tables (entities) | `sprk_matter`, `sprk_project`, `sprk_event`, `sprk_document` | SpaarkeCore |
| Columns (fields) | Custom fields on all Spaarke entities | SpaarkeCore |
| Views | List views, quick-find views | SpaarkeCore |
| Forms | Main forms, quick-create forms | SpaarkeCore |
| Option sets | Priority, Status, Event Type | SpaarkeCore |
| Security roles | Spaarke User, Spaarke Admin | SpaarkeCore |
| Web resources (JS) | Ribbon commands, form scripts | SpaarkeWebResources |
| Web resources (HTML) | Code pages (React 18 apps) | Feature solutions |
| Web resources (SVG/PNG) | Icons, images | SpaarkeWebResources |
| Model-driven apps | Spaarke legal workspace app | LegalWorkspace |
| Sitemaps | Navigation structure | LegalWorkspace |
| Ribbons/Command bars | Custom buttons and actions | Feature solutions |
| Dashboards | Analytics views | Feature solutions |

### Solution Dependency Tree

```
SpaarkeCore (Tier 1 — base entities, option sets, security roles)
  └── SpaarkeWebResources (Tier 2 — JS/icons used by all features)
        ├── AnalysisBuilder (Tier 3)
        ├── CalendarSidePane (Tier 3)
        ├── DocumentUploadWizard (Tier 3)
        ├── EventCommands (Tier 3)
        ├── EventDetailSidePane (Tier 3)
        ├── EventsPage (Tier 3)
        ├── LegalWorkspace (Tier 3)
        └── TodoDetailSidePane (Tier 3)
```

All 10 solutions are imported by `Deploy-DataverseSolutions.ps1` in this dependency order.

### Dev → Production Pipeline

**[AI+HUMAN]** The current process for moving solutions from dev to production:

```
Step 1: Build components in Dev (unmanaged)
  ├── PCF controls → npm run build → pac pcf push (dev)
  ├── Code pages → npm run build → deploy script → upload web resource (dev)
  ├── Schema changes → Power Apps maker portal (dev)
  └── Form/view customizations → Power Apps maker portal (dev)

Step 2: Export from Dev as MANAGED solution ZIPs
  [AI] pac solution export \
    --environment https://spaarkedev1.crm.dynamics.com \
    --path ./exports/SpaarkeCore_managed.zip \
    --name SpaarkeCore \
    --managed true

Step 3: Store ZIPs in repository or artifact store
  [AI] Copy exported ZIPs to src/solutions/ or a release artifact location

Step 4: Import to Production
  [AI] .\scripts\Deploy-DataverseSolutions.ps1 \
    -EnvironmentUrl "https://spaarke-prod.crm.dynamics.com" \
    -TenantId "..." -ClientId "..." -ClientSecret "..." \
    -SolutionPath "./exports/"
```

**Current gap**: Steps 2-3 are manual. There is no automated pipeline that exports from dev and imports to prod. See [Section 18](#18-dataverse-cicd-and-deployment-automation) for automation options.

### Version Management

Solutions should be version-bumped before export:

```powershell
# Bump solution version in dev before export
pac solution version --solution-name SpaarkeCore --strategy solution --value 1.2.0.0
```

Version format: `{major}.{minor}.{patch}.{revision}`

| When to Bump | Version Part | Example |
|-------------|-------------|---------|
| Breaking schema changes | Major | 1.0 → 2.0 |
| New features, new entities | Minor | 1.1 → 1.2 |
| Bug fixes, form tweaks | Patch | 1.1.1 → 1.1.2 |
| Build/CI increments | Revision | 1.1.1.0 → 1.1.1.1 |

---

## 17. Managed vs Unmanaged Solutions

### Key Differences

| Aspect | Unmanaged | Managed |
|--------|-----------|---------|
| **Purpose** | Development — active editing | Production — locked-down delivery |
| **Customizable** | Yes — full editing in maker tools | No — components are read-only |
| **Deletable** | Manual component-by-component | Clean uninstall removes all components |
| **Layering** | Base layer | Top layer, overrides unmanaged |
| **Rollback** | No clean rollback mechanism | Uninstall → previous version restored |
| **Schema conflicts** | Merge on import | Fail on conflict (safer) |
| **Recommended for** | Dev environments | Staging, Demo, Production |

### Current State and Migration Path

**Current state**: Dev environment uses **unmanaged** solutions (standard development pattern).

**Target state**:

| Environment | Solution Type | Rationale |
|-------------|--------------|-----------|
| Dev (`spaarkedev1`) | Unmanaged | Active development — developers need to edit |
| Demo-Production | **Managed** | Clean uninstall, prevents ad-hoc changes |
| Customer-Production | **Managed** | Clean uninstall, version control, rollback support |

### Implications of Moving to Managed

**Benefits:**
- Clean uninstall: removing a managed solution removes ALL its components cleanly
- Version rollback: import a previous version to roll back
- No accidental customizations in production
- Clear component ownership (which solution owns which component)

**Risks and considerations:**
- Cannot make ad-hoc fixes in production — all changes must go through dev → export → import
- **Unmanaged-to-managed migration**: If a production environment already has unmanaged components that overlap with the managed solution, import will fail. Must remove unmanaged customizations first.
- **Demo environment**: If demo currently has unmanaged components, a one-time cleanup is needed before importing managed versions
- **Hotfix process**: Emergency fixes require fast-tracking through dev → managed export → prod import (cannot edit in-place)

### Migration Steps (Demo Environment)

**[AI+HUMAN]** One-time migration from unmanaged to managed:

```powershell
# 1. In dev: Ensure all components are in solutions (not "default solution")
# 2. Export each solution as managed
pac solution export --name SpaarkeCore --managed true --path ./exports/

# 3. In demo: Remove existing unmanaged solutions (CAUTION: back up first)
#    This is the riskiest step — may need manual cleanup of overlapping components

# 4. Import managed solutions in dependency order
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-demo.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..."
```

**[HUMAN]** Before migration:
1. Document all unmanaged customizations in the target environment
2. Back up the environment (Power Platform Admin Center > Environments > Back up)
3. Test the full managed import in a throwaway environment first
4. Plan for a maintenance window — solution import can take 10-30 minutes

---

## 18. Dataverse CI/CD and Deployment Automation

### Current State

| What | Automated? | Tool |
|------|-----------|------|
| PCF control build | Partial | `npm run build` + `pac pcf push` (manual) |
| Code page build | Partial | Build scripts exist, manual deployment |
| Solution export from dev | **No** | Manual `pac solution export` |
| Solution import to prod | **Yes** | `Deploy-DataverseSolutions.ps1` |
| Solution version bumping | **No** | Manual `pac solution version` |
| Schema changes | **No** | Manual in maker portal |

### Recommended Automation Architecture

```
┌──────────────────────────────────────────────────────────┐
│ GitHub Actions: deploy-dataverse.yml                      │
│                                                          │
│  Trigger: Manual dispatch (workflow_dispatch)             │
│  Inputs: target_environment, solutions_to_deploy          │
│                                                          │
│  Job 1: export-solutions                                  │
│    ├── pac auth create (service principal)                │
│    ├── pac solution export --managed (each solution)      │
│    └── Upload ZIPs as build artifacts                    │
│                                                          │
│  Job 2: validate (depends on Job 1)                      │
│    ├── Download artifacts                                 │
│    ├── pac solution check (solution checker)              │
│    └── Report warnings/errors                            │
│                                                          │
│  Job 3: deploy-staging (depends on Job 2)                │
│    ├── pac auth create (staging env)                      │
│    ├── pac solution import (managed, staging)             │
│    └── Verify import success                             │
│                                                          │
│  Job 4: deploy-production (depends on Job 3, approval)   │
│    ├── Environment protection: require reviewer           │
│    ├── pac auth create (prod env)                        │
│    ├── pac solution import (managed, prod)                │
│    └── Verify import success                             │
└──────────────────────────────────────────────────────────┘
```

### GitHub Actions: PAC CLI Authentication

PAC CLI can authenticate via service principal in CI/CD:

```yaml
- name: Authenticate PAC CLI
  run: |
    pac auth create \
      --environment ${{ vars.DATAVERSE_URL }} \
      --tenant ${{ secrets.AZURE_TENANT_ID }} \
      --applicationId ${{ secrets.DATAVERSE_CLIENT_ID }} \
      --clientSecret ${{ secrets.DATAVERSE_CLIENT_SECRET }}
```

**Required GitHub secrets for Dataverse CI/CD:**

| Secret | Purpose |
|--------|---------|
| `DATAVERSE_CLIENT_ID` | `spaarke-dataverse-s2s-{env}` app registration |
| `DATAVERSE_CLIENT_SECRET` | Client secret for the S2S app |
| `AZURE_TENANT_ID` | Entra ID tenant (same as Azure) |

**Required GitHub variables:**

| Variable | Purpose | Example |
|----------|---------|---------|
| `DATAVERSE_URL_DEV` | Dev environment URL | `https://spaarkedev1.crm.dynamics.com` |
| `DATAVERSE_URL_PROD` | Prod environment URL | `https://spaarke-prod.crm.dynamics.com` |

### Microsoft Power Platform GitHub Actions (Alternative)

Microsoft provides official GitHub Actions for Power Platform deployments:

```yaml
- uses: microsoft/powerplatform-actions/export-solution@v1
  with:
    environment-url: ${{ vars.DATAVERSE_URL_DEV }}
    solution-name: SpaarkeCore
    managed: true
    solution-output-file: exports/SpaarkeCore_managed.zip
    app-id: ${{ secrets.DATAVERSE_CLIENT_ID }}
    client-secret: ${{ secrets.DATAVERSE_CLIENT_SECRET }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}

- uses: microsoft/powerplatform-actions/import-solution@v1
  with:
    environment-url: ${{ vars.DATAVERSE_URL_PROD }}
    solution-file: exports/SpaarkeCore_managed.zip
    app-id: ${{ secrets.DATAVERSE_CLIENT_ID }}
    client-secret: ${{ secrets.DATAVERSE_CLIENT_SECRET }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
```

### Solution Packager (Source Control)

For advanced teams, solutions can be **unpacked to source control** and **repacked for deployment**:

```powershell
# Unpack solution to source-controllable files
pac solution unpack --solution-zip SpaarkeCore_managed.zip --folder src/solutions/SpaarkeCore/

# Repack from source files
pac solution pack --folder src/solutions/SpaarkeCore/ --zip-file SpaarkeCore_managed.zip --type managed
```

**Pros**: Full git history of schema changes, code review on entity/form changes, merge support.
**Cons**: Adds complexity, unpack/pack can drift from actual environment state.

**Current recommendation**: Start with managed solution export/import via `Deploy-DataverseSolutions.ps1`. Add Solution Packager when the team grows and needs schema change review.

### Claude Code Execution Pattern for Dataverse Deployments

When asked to "deploy to Dataverse" or "update Dataverse solutions":

1. **Build components first**: PCF controls (`npm run build`), code pages (webpack/vite build)
2. **Deploy to dev**: `pac pcf push`, deploy scripts for web resources
3. **Export as managed**: `pac solution export --managed true`
4. **Import to prod**: `.\scripts\Deploy-DataverseSolutions.ps1`

Claude Code can automate steps 1, 2, and 4. Step 3 (export) requires the dev environment to be in a clean state, which Claude Code can verify but the developer should confirm.

---

## 19. SharePoint Embedded (SPE) Setup

SPE provides document storage for Spaarke. Each production environment needs a **Container Type** (the blueprint) and the BFF API registered as the **owning application**.

> **Reference**: See [HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md](./HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md) for the full SPE setup guide with troubleshooting.

### Architecture

```
┌───────────────────────────────────────────────────────────┐
│ Container Type (one per production environment)            │
│   - Owned by: spaarke-bff-api-prod app registration       │
│   - Container Type ID: stored in Key Vault                │
│   - Registration: full delegated + full appOnly           │
│                                                           │
│   Containers (one per Business Unit):                      │
│   ├── Root BU → Container (sprk_containerid on BU record) │
│   ├── Child BU → Container (sprk_containerid on BU record)│
│   └── ...                                                 │
└───────────────────────────────────────────────────────────┘
```

### Ownership Model

The **BFF API app** is the owning application for the container type. It has full permissions to create containers, manage files, and perform all SPE operations server-side using client credentials (client secret via Key Vault).

### Step 1: Create Production Container Type

**[HUMAN]** Requires SharePoint Administrator or Global Administrator. One-time operation per environment.

```powershell
# Option A: PowerShell (recommended)
.\scripts\Create-ContainerType-PowerShell.ps1 `
    -ContainerTypeName "Spaarke Document Storage" `
    -OwningAppId "<spaarke-bff-api-prod-client-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"

# Option B: Graph API + registration in one step
.\scripts\Create-NewContainerType.ps1 `
    -OwningAppId "<spaarke-bff-api-prod-client-id>" `
    -OwningAppSecret "<secret-from-key-vault>" `
    -TenantId "<tenant-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"
```

**Output**: A new `ContainerTypeId` (GUID). Save this — it's needed for all subsequent steps.

### Step 2: Register Owning App with Container Type

**[AI]** Register the BFF API as owning app with full permissions:

```powershell
.\scripts\Register-BffApiWithContainerType.ps1 `
    -ContainerTypeId "<container-type-id>" `
    -OwningAppId "<spaarke-bff-api-prod-client-id>" `
    -OwningAppSecret "<secret-from-key-vault>" `
    -TenantId "<tenant-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"
```

This calls `PUT /_api/v2.1/storageContainerTypes/{id}/applicationPermissions` with `full` delegated and `full` appOnly permissions.

### Step 3: Store ContainerTypeId in Key Vault

**[AI]**

```powershell
az keyvault secret set `
    --vault-name <platform-key-vault> `
    --name "Spe--ContainerTypeId" `
    --value "<container-type-id>"
```

### Step 4: Configure BFF API App Setting

**[AI]** Add Key Vault reference to App Service:

```powershell
az webapp config appsettings set `
    -g <resource-group> `
    -n <app-service-name> `
    --settings "Spe__ContainerTypeId=@Microsoft.KeyVault(VaultName=<vault>;SecretName=Spe--ContainerTypeId)"
```

### Step 5: Verify SPE Setup

**[AI]**

```powershell
# Check container type registration
.\scripts\Check-ContainerType-Registration.ps1 `
    -ContainerTypeId "<container-type-id>" `
    -SharePointDomain "<tenant>.sharepoint.com" `
    -OwningAppId "<bff-api-client-id>"

# Test token and API access
.\scripts\Test-SharePointToken.ps1 `
    -ClientId "<bff-api-client-id>" `
    -ClientSecret "<secret>" `
    -TenantId "<tenant-id>" `
    -ContainerTypeId "<container-type-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"

# Restart BFF API to pick up config
az webapp restart --name <app-service-name> --resource-group <resource-group>
```

### Entra ID Permissions for SPE

The BFF API app registration needs these permissions:

| Permission | Type | Purpose |
|-----------|------|---------|
| `FileStorageContainer.Selected` | Application | Create/access SPE containers |
| `Container.Selected` | Application (SharePoint) | Register with container types |

These should already be configured by `Register-EntraAppRegistrations.ps1` (Phase 2). Verify with `.\scripts\Test-EntraAppRegistrations.ps1`.

### Container Provisioning

Containers are created **per business unit** — each BU gets one SPE container stored in `businessunit.sprk_containerid`.

**During initial provisioning:** `Provision-Customer.ps1` Step 8 creates the SPE container for the root business unit via Graph API and sets `sprk_containerid` on the BU record.

**When adding new business units:** Run the standalone script:

```powershell
.\scripts\New-BusinessUnitContainer.ps1 `
    -BusinessUnitId "<bu-guid>" `
    -BusinessUnitName "New Business Unit" `
    -ContainerTypeId "<container-type-id>" `
    -DataverseUrl "https://<env>.crm.dynamics.com"
```

**Automation options** (ADR-002 prohibits Dataverse plugins):
- Power Automate flow triggered on BU creation
- BFF API endpoint called from ribbon button
- Manual script execution during onboarding

---

## 20. CI/CD Pipelines (Azure)

Three GitHub Actions workflows automate deployment after initial setup:

### deploy-platform.yml

**Trigger**: Manual dispatch (`workflow_dispatch`)

```yaml
# 3-job pipeline:
# 1. what-if: Preview changes (always runs)
# 2. deploy: Apply changes (requires environment approval)
# 3. verify: Run smoke tests
```

### deploy-bff-api.yml

**Trigger**: Push to `master` (path filter: `src/server/api/**`)

```yaml
# 8-job pipeline with zero-downtime:
# 1. build → 2. test → 3. deploy-staging → 4. health-check-staging
# → 5. swap → 6. health-check-production → 7. rollback (on failure)
# → 8. notify
```

### provision-customer.yml

**Trigger**: Manual dispatch with customer parameters

```yaml
# 4-job pipeline:
# 1. validate-inputs → 2. provision → 3. verify → 4. audit-trail
```

### GitHub Environment Protection

| Environment | Protection Rules |
|-------------|-----------------|
| staging | Auto-approve, wait timer: 0 |
| production | Required reviewer, wait timer: 5 minutes |

See [GitHub Environment Protection](./GITHUB-ENVIRONMENT-PROTECTION.md) for setup details.

### Required GitHub Secrets

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC federated credential (not a password) |
| `AZURE_TENANT_ID` | Entra ID tenant |
| `AZURE_SUBSCRIPTION_ID` | Target subscription |

> **Note**: GitHub Actions use OIDC (federated credentials), not service principal secrets.

---

## Quick Reference: Complete Deployment Sequence

For a fresh production deployment, execute phases in order:

```
PHASE 1: SHARED PLATFORM                                    [AI]
  1. Deploy-Platform.ps1 -EnvironmentName prod -WhatIf      (preview)
  2. Deploy-Platform.ps1 -EnvironmentName prod               (deploy, 10-20 min)

PHASE 2: ENTRA ID                                           [AI + HUMAN]
  3. Register-EntraAppRegistrations.ps1                      (create apps)
  4. Azure Portal: Grant admin consent                       [HUMAN]
  5. Power Platform: Create application user                 [HUMAN]
  6. Test-EntraAppRegistrations.ps1                          (verify)
  7. Grant managed identity Key Vault RBAC (both slots!)     [AI]

PHASE 3: SPE (SHAREPOINT EMBEDDED)                           [HUMAN + AI]
  8. Create SPE container type (SharePoint Admin Center)     [HUMAN]
  9. Register BFF API app with container type                [AI]
  10. Store ContainerTypeId in Key Vault                     [AI]

PHASE 4: SECRETS & SETTINGS                                  [AI]
  11. Seed-ProductionKeyVault.ps1                            (seed secrets)
  12. Configure-ProductionAppSettings.ps1                    (KV references)

PHASE 5: BFF API                                             [AI]
  13. Deploy-BffApi.ps1 -Environment production -UseSlotDeploy (deploy, 3-5 min)

PHASE 6: CUSTOM DOMAIN                                       [AI + HUMAN]
  14. Configure-CustomDomain.ps1 -ShowDnsInstructions        (get DNS values)
  15. Create CNAME + TXT DNS records                         [HUMAN]
  16. Wait for DNS propagation                               [HUMAN]
  17. Configure-CustomDomain.ps1                             (bind domain + SSL)
  18. Test-CustomDomain.ps1                                  (verify)

PHASE 7: DATAVERSE SOLUTIONS                                  [AI]
  19. Deploy-DataverseSolutions.ps1 -Environment prod         (import managed, 10-30 min)

PHASE 8: FIRST CUSTOMER                                      [AI]
  20. Provision-Customer.ps1 -CustomerId demo -WhatIf        (preview)
  21. Provision-Customer.ps1 -CustomerId demo                (provision, 20-30 min)
      - Sets all 7 Dataverse Environment Variables (Step 8)
      - Generates environment-config.json (Step 9)
      - Runs Validate-DeployedEnvironment.ps1 (Step 13)
  22. Load-DemoSampleData.ps1                                (sample data)
  23. Invite-DemoUsers.ps1                                   (demo access)

PHASE 9: VERIFY                                              [AI]
  24. Test-Deployment.ps1 -EnvironmentName prod              (17 smoke tests)
  25. Validate-DeployedEnvironment.ps1 -DataverseUrl <url>   (env vars, CORS, leakage)
```

**Total estimated time**: 3-5 hours (first deployment). Steps 1-2, 9-13, 17-18, 19-24 are fully automatable by Claude Code.

---

## Script Reference

All scripts are in the `scripts/` directory:

| Script | Purpose | Key Parameters |
|--------|---------|----------------|
| `Deploy-Platform.ps1` | Deploy shared platform Bicep | `-EnvironmentName`, `-WhatIf` |
| `Deploy-BffApi.ps1` | Deploy BFF API with zero-downtime | `-Environment`, `-UseSlotDeploy`, `-SkipBuild` |
| `Deploy-DataverseSolutions.ps1` | Import 10 managed solutions in order | `-Environment` |
| `Register-EntraAppRegistrations.ps1` | Create Entra ID app registrations | `-DryRun`, `-SkipBffApi`, `-SkipDataverseS2S` |
| `Test-EntraAppRegistrations.ps1` | Verify app registrations | (none) |
| `Configure-CustomDomain.ps1` | Custom domain + SSL setup | `-ShowDnsInstructions`, `-SkipDnsCheck`, `-SkipSsl` |
| `Test-CustomDomain.ps1` | Verify custom domain | (none) |
| `Seed-ProductionKeyVault.ps1` | Populate Key Vault with secrets | (none) |
| `Configure-ProductionAppSettings.ps1` | Set app settings with KV refs | (none) |
| `Provision-Customer.ps1` | End-to-end customer provisioning | `-CustomerId`, `-ResumeFromStep`, `-WhatIf`, `-SkipDataverse` |
| `Decommission-Customer.ps1` | Customer teardown | `-CustomerId`, `-DryRun`, `-Force` |
| `Load-DemoSampleData.ps1` | Load sample data for demo | (none) |
| `Invite-DemoUsers.ps1` | B2B guest invitations for demo | (none) |
| `Test-Deployment.ps1` | Smoke tests (17 tests, 6 groups) | `-EnvironmentName` |
| `Validate-DeployedEnvironment.ps1` | Post-deployment validation (env vars, API health, CORS, dev leakage) | `-DataverseUrl`, `-BffApiUrl` (optional) |
| `Rotate-Secrets.ps1` | Zero-downtime secret rotation | `-Scope`, `-SecretType`, `-CustomerId`, `-DryRun` |

---

*This guide was written based on the production deployment completed in March 2026. Updated March 19, 2026 to document environment-agnostic "Build Once, Deploy Anywhere" architecture, Dataverse Environment Variables reference, and Validate-DeployedEnvironment.ps1 validation script. All scripts are in `scripts/`. All lessons learned are from actual deployment execution.*
