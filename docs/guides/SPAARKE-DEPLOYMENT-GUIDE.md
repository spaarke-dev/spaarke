# Spaarke Deployment Guide

> **Version**: 1.0 (consolidated)
> **Last Updated**: 2026-04-24
> **Status**: Authoritative — supersedes `ENVIRONMENT-DEPLOYMENT-GUIDE.md` and `PRODUCTION-DEPLOYMENT-GUIDE.md`
> **Audience**: Claude Code AI (primary executor) + Human developers (approver/operator)
> **Applies To**: Deploying Spaarke to any environment — dev, UAT, demo, production

---

## How to Use This Guide

This is the single source of truth for Spaarke platform deployments. It covers infrastructure provisioning, Entra ID setup, Dataverse solutions, SharePoint Embedded, BFF API deployment, customer provisioning, validation, and Day-2 operations.

### Execution Legend

| Icon | Meaning |
|------|---------|
| **[AI]** | Claude Code executes this step autonomously via script/CLI |
| **[HUMAN]** | Human developer must perform this step (portal, DNS, approval) |
| **[AI+HUMAN]** | Claude Code runs the script; human verifies/approves output |
| **[DECISION]** | Decision point — Claude Code presents options, human chooses |

### Trigger Phrases for Claude Code

When asked any of the following, Claude Code reads this guide to determine which phase(s) to execute:

- "deploy to production" / "deploy to dev" / "deploy to demo"
- "set up production environment" / "provision new environment"
- "fresh production deployment"
- "deploy platform infrastructure"
- "provision customer"
- "deploy Spaarke to {env}"

---

## Table of Contents

1. [Overview](#1-overview)
2. [Prerequisites](#2-prerequisites)
3. [Deployment Models](#3-deployment-models)
4. [Phase 1: Azure Infrastructure](#4-phase-1-azure-infrastructure)
5. [Phase 2: Entra ID App Registrations](#5-phase-2-entra-id-app-registrations)
6. [Phase 3: Key Vault + App Settings](#6-phase-3-key-vault--app-settings)
7. [Phase 4: Dataverse Solutions](#7-phase-4-dataverse-solutions)
8. [Phase 5: Dataverse Environment Variables](#8-phase-5-dataverse-environment-variables)
9. [Phase 6: SharePoint Embedded](#9-phase-6-sharepoint-embedded)
10. [Phase 7: BFF API Deployment](#10-phase-7-bff-api-deployment)
11. [Phase 8: Custom Domain + SSL (Production)](#11-phase-8-custom-domain--ssl-production)
12. [Phase 9: Customer Provisioning](#12-phase-9-customer-provisioning)
13. [Phase 10: Validation](#13-phase-10-validation)
14. [Day-2 Operations](#14-day-2-operations)
15. [Rollback Procedures](#15-rollback-procedures)
16. [Troubleshooting](#16-troubleshooting)
17. [CI/CD Integration (Azure)](#17-cicd-integration-azure)
18. [CI/CD for Dataverse](#18-cicd-for-dataverse)
- [Appendix A: Known Issues and Workarounds](#appendix-a-known-issues-and-workarounds)
- [Appendix B: Complete App Settings Reference](#appendix-b-complete-app-settings-reference)
- [Appendix C: Resource Inventory](#appendix-c-resource-inventory)
- [Appendix D: Script Reference](#appendix-d-script-reference)

---

## 1. Overview

### Platform Components

Each Spaarke deployment consists of:

| Component | Scope | Purpose |
|-----------|-------|---------|
| Shared Azure platform | Per environment | App Service, OpenAI, AI Search, Doc Intelligence, Key Vault |
| Per-customer Azure resources | Per customer | Storage, Key Vault, Service Bus, Redis |
| Entra ID app registrations | Per environment | BFF API identity + Dataverse S2S |
| Dataverse environment | Per customer | Tables, plugins, security roles, solutions |
| SharePoint Embedded | Per environment | Container type + containers |
| BFF API deployment | Per environment | .NET 8 Minimal API code |

### Build Once, Deploy Anywhere

Spaarke uses an **environment-agnostic build** strategy. All client-side components (PCF controls, Code Pages, legacy JS webresources, Office Add-ins) resolve configuration at runtime from **Dataverse Environment Variables** — no environment-specific values are baked into any build artifact.

**How it works:**

1. **Build artifacts are identical** across dev, UAT, demo, and production. The same compiled PCF controls, Code Pages, and solution ZIPs are promoted through environments without rebuilding.
2. **Runtime configuration resolution** — At startup, client components call `resolveRuntimeConfig()` (from `@spaarke/auth`) which queries Dataverse Environment Variables via the Web API and caches the result.
3. **Seven canonical environment variables** define the complete runtime configuration per environment (see [§8 Dataverse Environment Variables](#8-phase-5-dataverse-environment-variables)).
4. **No dev defaults** — If an environment variable is missing, components fail loudly with a clear error message rather than silently falling back to dev values.

### Dual Execution Model

This guide is written for **dual execution**: Claude Code handles automated steps, human developers handle manual steps (portal actions, DNS, approvals). Each step is marked with **[AI]**, **[HUMAN]**, **[AI+HUMAN]**, or **[DECISION]**.

### Estimated Time

| Scenario | Duration |
|----------|----------|
| First-time production deployment | 4-6 hours |
| Subsequent environment deployment | 2-3 hours |
| Adding a customer to existing platform | 20-30 minutes |
| Subsequent BFF API redeploy (slot swap) | 3-5 minutes |

### Related Guides

- [Customer Onboarding Runbook](./CUSTOMER-ONBOARDING-RUNBOOK.md) — customer-facing onboarding process
- [Customer Deployment Guide](./CUSTOMER-DEPLOYMENT-GUIDE.md) — customer-specific deployment details
- [Incident Response](./INCIDENT-RESPONSE.md)
- [Secret Rotation Procedures](./SECRET-ROTATION-PROCEDURES.md)
- [Monitoring and Alerting](./MONITORING-AND-ALERTING-GUIDE.md)
- [How to Set Up Container Types and Containers](./HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md)

---

## 2. Prerequisites

### Required Tools

**[AI]** Claude Code verifies these automatically before any deployment step:

```powershell
az --version       # Azure CLI 2.60+
pwsh --version     # PowerShell 7.4+
dotnet --version   # .NET SDK 8.0+
pac --version      # PAC CLI (latest)
gh --version       # GitHub CLI 2.40+
```

| Tool | Minimum Version | Install Command |
|------|----------------|-----------------|
| Azure CLI | 2.60+ | `winget install Microsoft.AzureCLI` |
| PowerShell | 7.4+ | `winget install Microsoft.PowerShell` |
| .NET SDK | 8.0+ | `winget install Microsoft.DotNet.SDK.8` |
| PAC CLI | Latest | `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` |
| GitHub CLI | 2.40+ | `winget install GitHub.cli` |
| SharePoint Online Management Shell | Latest | See §9 (container type creation requires Windows PowerShell 5.1) |

> **PAC CLI on Windows**: `pac` is a `.cmd` wrapper. Scripts use `cmd /c pac` for output capture. Claude Code handles this automatically.

### Required Access

**[HUMAN]** Verify these before starting. Claude Code cannot check portal-level roles:

| Resource | Access Level | How to Verify |
|----------|-------------|---------------|
| Azure Subscription | Contributor + User Access Admin | `az account show` **[AI]** |
| Entra ID Tenant | Application Administrator | Azure Portal > Entra ID > Roles **[HUMAN]** |
| Power Platform | Global Admin or Dynamics 365 Admin | Power Platform Admin Center **[HUMAN]** |
| SharePoint Tenant | SharePoint Administrator | Required for container type creation |
| GitHub Repository | Write access | `gh auth status` **[AI]** |
| DNS Management | Record creation for `api.spaarke.com` | Varies by provider **[HUMAN]** |

### Required Azure Quotas

**[DECISION]** Before deploying, verify quotas. If quotas are insufficient, request increases (1-3 business days).

| Resource | Required | Region | Notes |
|----------|----------|--------|-------|
| App Service Plan (P1v3) | 1 | westus2 | Production workload |
| Azure OpenAI (GPT-4o) | 50K+ TPM | **westus3** | NOT available in westus2 — see [Appendix A](#appendix-a-known-issues-and-workarounds) |
| Azure OpenAI (GPT-4o-mini) | 100K TPM | **westus3** | Same region as GPT-4o |
| Azure OpenAI (text-embedding-3-large) | 100K TPM | **westus3** | Same region |
| AI Search (Standard) | 1 service | westus2 | Standard SKU (not Standard2 — see Appendix A) |
| Document Intelligence (S0) | 1 | westus2 | |

### Authentication Setup

**[AI]** Claude Code runs these commands. Human provides credentials if not already cached:

```powershell
# Azure CLI
az login
az account set --subscription "<subscription-name>"
az account show

# PAC CLI
pac auth create --environment "https://<org>.crm.dynamics.com"
pac auth list
```

### Information to Collect

| Item | Example | Where to Find |
|------|---------|---------------|
| Target subscription ID | `2ff9ee48-...` | Azure Portal > Subscriptions |
| Target Dataverse URL | `https://spaarke-demo.crm.dynamics.com` | Power Platform Admin Center |
| Tenant ID | `a221a95e-...` | Entra ID > Overview |
| Environment name | `demo` | Your naming convention |

---

## 3. Deployment Models

### Resource Group Strategy

Spaarke uses **explicit resource groups** for isolation, created automatically by subscription-scoped Bicep templates:

| Scope | Resource Group | Created By | Contains |
|-------|---------------|------------|----------|
| Shared platform | `rg-spaarke-platform-{env}` | `platform.bicep` via `Deploy-Platform.ps1` | App Service, OpenAI, AI Search, Doc Intel, Key Vault, Monitoring |
| Per-customer | `rg-spaarke-{customerId}-{env}` | `customer.bicep` via `Provision-Customer.ps1` | Storage, Key Vault, Service Bus, Redis |

### Subscription Models

**[DECISION]** Choose based on organizational requirements:

| Model | Description | When to Use | Current Status |
|-------|-------------|-------------|----------------|
| **A: Single subscription** | Dev + Prod in same subscription, isolated by RGs | Small teams, startups, single-operator | **Current** |
| **B: Env-separated subscriptions** | Separate subscriptions for Dev and Prod | **Recommended for production** — prevents blast radius, enables RBAC isolation | Planned |
| **C: Customer-separated subscriptions** | Each customer gets their own subscription | Enterprise customers requiring billing isolation or data sovereignty | Future consideration |

> **ADR**: See [ADR-027: Subscription Isolation and Dataverse Solution Management](../adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md)

### Naming Convention

All resources follow [AZURE-RESOURCE-NAMING-CONVENTION.md](../architecture/AZURE-RESOURCE-NAMING-CONVENTION.md):

| Pattern | Example |
|---------|---------|
| `rg-spaarke-{scope}-{env}` | `rg-spaarke-platform-prod` |
| `spaarke-{purpose}-{env}` | `spaarke-bff-prod` |
| `sprk-{purpose}-{env}-kv` | `sprk-platform-prod-kv` |
| `sprk-{customer}-{env}-kv` | `sprk-demo-prod-kv` |
| `sprk{customer}{env}sa` | `sprkdemoprodsa` |
| `sprk-{customer}-{env}-sbus` | `sprk-demo-prod-sbus` |

> **Note**: Service Bus uses `-sbus` suffix, NOT `-sb` (reserved by Azure).

---

## 4. Phase 1: Azure Infrastructure

### 4.1 Subscription Setup

**[HUMAN]** Create subscription (one-time per environment):

1. Azure Portal > **Cost Management + Billing** > **Billing scopes** > select corporate billing account
2. Create subscription: `Spaarke {Environment} Environment`
3. Add tags: `environment={env}`, `managedBy=spaarke-ops`

### 4.2 Register Resource Providers

**[AI]** New subscriptions need resource providers registered before resources can be created:

```bash
az account set --subscription "{subscription-id}"

for provider in Microsoft.KeyVault Microsoft.Storage Microsoft.ServiceBus \
  Microsoft.Cache Microsoft.CognitiveServices Microsoft.Search \
  Microsoft.Web Microsoft.Insights Microsoft.OperationalInsights Microsoft.Syntex; do
  az provider register --namespace "$provider"
done

# Verify (can take 2-5 minutes):
az provider show --namespace Microsoft.KeyVault --query "registrationState"
az provider show --namespace Microsoft.Syntex --query "registrationState"
```

> **Microsoft.Syntex** is required for SharePoint Embedded billing — register early to avoid delays in §9.

### 4.3 Deploy Platform (Bicep — Preferred)

**[AI+HUMAN]** Preview first, then deploy:

```powershell
# Preview (always review what-if output before applying)
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod -WhatIf

# Deploy (10-20 minutes)
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod
```

**What this deploys** (subscription-scoped Bicep):
- Resource Group: `rg-spaarke-platform-{env}`
- App Service Plan (P1v3) + App Service + staging slot
- Azure OpenAI (westus3) with gpt-4o, gpt-4o-mini, text-embedding-3-large deployments
- Azure AI Search (westus2, Standard SKU)
- Azure Document Intelligence (S0)
- Application Insights + Log Analytics
- Platform Key Vault (RBAC-enabled)
- Managed identity assigned to App Service and staging slot

**Parameter file**: `infrastructure/bicep/parameters/platform-{env}.bicepparam`

Key parameters to verify:
```
environmentName = 'prod'
location = 'westus2'
openAiLocation = 'westus3'         // CRITICAL: OpenAI not in westus2
appServicePlanSku = 'P1v3'
aiSearchSku = 'standard'           // NOT standard2 (see Appendix A)
aiSearchReplicaCount = 2
```

### 4.4 Azure CLI Reference (Fallback)

**[AI]** Use only if Bicep is not available or for one-off resource additions. The Bicep modules cover all of the following:

<details>
<summary>Click to expand CLI commands for all resources</summary>

```bash
ENV=demo  # Change per environment

# 1. Resource Group
az group create --name rg-spaarke-platform-$ENV --location westus2 \
  --tags environment=$ENV managedBy=bicep application=spaarke

# 2. Log Analytics + App Insights
az monitor log-analytics workspace create \
  --resource-group rg-spaarke-platform-$ENV --workspace-name spaarke-$ENV-logs \
  --location westus2 --retention-time 90

WORKSPACE_ID=$(az monitor log-analytics workspace show \
  --resource-group rg-spaarke-platform-$ENV --workspace-name spaarke-$ENV-logs \
  --query id --output tsv)

MSYS_NO_PATHCONV=1 az monitor app-insights component create \
  --app spaarke-$ENV-insights --location westus2 \
  --resource-group rg-spaarke-platform-$ENV --workspace "$WORKSPACE_ID"

# 3. Key Vault (RBAC)
az keyvault create --name sprk-$ENV-kv --resource-group rg-spaarke-platform-$ENV \
  --location westus2 --enable-rbac-authorization true

# 4. Azure OpenAI (westus3!)
az cognitiveservices account create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-platform-$ENV --location westus3 \
  --kind OpenAI --sku S0 --custom-domain spaarke-openai-$ENV

az cognitiveservices account deployment create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-platform-$ENV --deployment-name gpt-4o \
  --model-name gpt-4o --model-version "2024-08-06" \
  --model-format OpenAI --sku-name Standard --sku-capacity 50

az cognitiveservices account deployment create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-platform-$ENV --deployment-name gpt-4o-mini \
  --model-name gpt-4o-mini --model-version "2024-07-18" \
  --model-format OpenAI --sku-name Standard --sku-capacity 50

az cognitiveservices account deployment create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-platform-$ENV --deployment-name text-embedding-3-large \
  --model-name text-embedding-3-large --model-version "1" \
  --model-format OpenAI --sku-name Standard --sku-capacity 50

# 5. Document Intelligence
az cognitiveservices account create --name spaarke-docintel-$ENV \
  --resource-group rg-spaarke-platform-$ENV --location westus2 \
  --kind FormRecognizer --sku S0

# 6. AI Search
az search service create --name spaarke-search-$ENV \
  --resource-group rg-spaarke-platform-$ENV --location westus2 \
  --sku standard --replica-count 1 --partition-count 1

# 7. App Service Plan + App Service
az appservice plan create --name spaarke-$ENV-plan \
  --resource-group rg-spaarke-platform-$ENV --location westus2 \
  --sku P1v3 --is-linux

MSYS_NO_PATHCONV=1 az webapp create --name spaarke-bff-$ENV \
  --resource-group rg-spaarke-platform-$ENV --plan spaarke-$ENV-plan \
  --runtime "DOTNETCORE:8.0"

# 8. Staging slot (CRITICAL: different managed identity than production)
az webapp deployment slot create --name spaarke-bff-$ENV \
  --resource-group rg-spaarke-platform-$ENV --slot staging

# 9. Managed identity for both slots
MSYS_NO_PATHCONV=1 az webapp identity assign \
  --name spaarke-bff-$ENV --resource-group rg-spaarke-platform-$ENV

MSYS_NO_PATHCONV=1 az webapp identity assign \
  --name spaarke-bff-$ENV --resource-group rg-spaarke-platform-$ENV --slot staging
```

</details>

### 4.5 Per-Customer Resources

Per-customer Azure resources are created by `Provision-Customer.ps1` (§12). The Bicep template is `infrastructure/bicep/customer.bicep` and creates:

| Resource | Name Pattern |
|----------|-------------|
| Storage Account | `sprk{customerId}{env}sa` |
| Key Vault | `sprk-{customerId}-{env}-kv` |
| Service Bus | `sprk-{customerId}-{env}-sbus` |
| Redis Cache | `sprk-{customerId}-{env}-redis` |

### 4.6 Verify Platform

```powershell
az group show --name rg-spaarke-platform-prod --query "{name:name, location:location}" -o table
az webapp show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query "{state:state, defaultHostName:defaultHostName}" -o table
az keyvault show --name sprk-platform-prod-kv --query "{name:name, location:location}" -o table
```

### 4.7 Increase Dataverse Max Upload Size

**[AI]** Before importing Dataverse solutions, increase max upload file size to 32MB (default 5MB is too small for PCF control bundles):

```powershell
$token = az account get-access-token --resource "{dataverse-url}" --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; "OData-Version" = "4.0"; "Content-Type" = "application/json"; "If-Match" = "*" }
$orgId = (Invoke-RestMethod -Uri "{dataverse-url}/api/data/v9.2/organizations?`$select=organizationid" -Headers @{Authorization="Bearer $token";"OData-Version"="4.0"}).value[0].organizationid
Invoke-RestMethod -Uri "{dataverse-url}/api/data/v9.2/organizations($orgId)" -Method Patch -Headers $headers -Body '{"maxuploadfilesize":33554432}'
```

---

## 5. Phase 2: Entra ID App Registrations

### 5.1 Create App Registrations (Automated)

**[AI]** Run the registration script:

```powershell
# Preview first
.\scripts\Register-EntraAppRegistrations.ps1 -DryRun

# Create registrations
.\scripts\Register-EntraAppRegistrations.ps1
```

This creates two registrations in your tenant:

| App Name | Purpose | API Permissions |
|----------|---------|-----------------|
| `spaarke-bff-api-{env}` | BFF API identity | Graph: Sites.Read.All, User.Read.All, FileStorageContainer.Selected, FileStorageContainer.ReadWrite.All; SharePoint: Container.Selected, Sites.ReadWrite.All; Dynamics CRM: user_impersonation |
| `spaarke-dataverse-s2s-{env}` | Dataverse S2S | Dynamics CRM: user_impersonation |

The script:
- Creates app registrations with correct permissions
- Generates 24-month client secrets
- Stores secrets in `sprk-platform-{env}-kv` (Key Vault)
- Configures redirect URIs and exposed API scopes
- Creates service principals

### 5.2 Manual App Registration Details (Reference)

<details>
<summary>Click to expand manual commands if scripts are unavailable</summary>

#### BFF API App

```bash
az ad app create --display-name "Spaarke BFF API - {Env}" --sign-in-audience AzureADMyOrg
BFF_APP_ID="{appId-from-output}"

# Client secret
az ad app credential reset --id "$BFF_APP_ID" --append \
  --display-name "{Env} BFF API Secret" --years 2

# Service principal
az ad sp create --id "$BFF_APP_ID"
BFF_SP_ID="{sp-id-from-output}"

# Redirect URIs
MSYS_NO_PATHCONV=1 az ad app update --id "$BFF_APP_ID" \
  --web-redirect-uris "https://localhost" \
    "https://spaarke-bff-{env}.azurewebsites.net" \
    "https://spaarke-bff-{env}.azurewebsites.net/.auth/login/aad/callback"

# Graph permissions
GRAPH_SP_ID=$(MSYS_NO_PATHCONV=1 az ad sp show --id "00000003-0000-0000-c000-000000000000" --query id --output tsv)

for role_id in \
  "332a536c-c7ef-4017-ab91-336970924f0d" \
  "df021288-bdef-4463-88db-98f22de89214" \
  "75359482-378d-4052-8f01-80520e7db3cd" \
  "4437522e-9a86-4a41-a7da-e380edd4a97d"; do
  MSYS_NO_PATHCONV=1 az rest --method POST \
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$BFF_SP_ID/appRoleAssignments" \
    --body "{\"principalId\":\"$BFF_SP_ID\",\"resourceId\":\"$GRAPH_SP_ID\",\"appRoleId\":\"$role_id\"}" \
    --output none
done

# SharePoint permissions
SP_RESOURCE_ID=$(MSYS_NO_PATHCONV=1 az ad sp show --id "00000003-0000-0ff1-ce00-000000000000" --query id --output tsv)

for role_id in "19766c1b-905b-43af-8756-06526ab42875" "20d37865-089c-4dee-8c41-6967602d4ac8"; do
  MSYS_NO_PATHCONV=1 az rest --method POST \
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$BFF_SP_ID/appRoleAssignments" \
    --body "{\"principalId\":\"$BFF_SP_ID\",\"resourceId\":\"$SP_RESOURCE_ID\",\"appRoleId\":\"$role_id\"}" \
    --output none
done
```

**Required Graph permissions (Application)**:

| Permission | GUID | Purpose |
|-----------|------|---------|
| Sites.Read.All | `332a536c-c7ef-4017-ab91-336970924f0d` | SharePoint site access |
| User.Read.All | `df021288-bdef-4463-88db-98f22de89214` | User profile resolution |
| FileStorageContainer.Selected | `75359482-378d-4052-8f01-80520e7db3cd` | SPE container access |
| FileStorageContainer.ReadWrite.All | `4437522e-9a86-4a41-a7da-e380edd4a97d` | SPE container creation |

**Required SharePoint permissions (Application)**:

| Permission | GUID | Purpose |
|-----------|------|---------|
| Container.Selected | `19766c1b-905b-43af-8756-06526ab42875` | SPE container type operations |
| Sites.ReadWrite.All | `20d37865-089c-4dee-8c41-6967602d4ac8` | SPE registration |

> **Note**: Permission GUIDs `19766c1b` and `20d37865` are SharePoint-specific and not visible in the Azure Portal API permissions picker. They must be granted via Graph API `appRoleAssignments`.

#### UI SPA App

```bash
az ad app create --display-name "Spaarke UI - {Env}" --sign-in-audience AzureADMyOrg \
  --enable-access-token-issuance true --enable-id-token-issuance true

UI_APP_ID="{appId-from-output}"

# SPA redirect URIs via Graph API
MSYS_NO_PATHCONV=1 az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/{object-id}" \
  --body "{\"spa\":{\"redirectUris\":[\"https://spaarke-{env}.crm.dynamics.com\",\"https://spaarke-{env}.api.crm.dynamics.com\",\"http://localhost\"]}}"
```

</details>

### 5.3 Grant Admin Consent

**[HUMAN]** Required — Claude Code cannot grant admin consent:

1. Navigate to **Azure Portal > Entra ID > App registrations**
2. Select `spaarke-bff-api-{env}`
3. Go to **API permissions** > Click **Grant admin consent for [tenant]**
4. Repeat for `spaarke-dataverse-s2s-{env}`

### 5.4 Register Dataverse Application User

**[HUMAN]** Required — must be done in Power Platform Admin Center:

1. Navigate to **Power Platform Admin Center > Environments > [env] > Settings > Users + permissions > Application users**
2. Click **+ New app user**
3. Select the `spaarke-bff-api-{env}` app registration
4. Business unit: Root
5. Assign **System Administrator** security role
6. Repeat for `spaarke-dataverse-s2s-{env}`

> **Without this step**, the BFF API crashes on startup with: `DataverseConnectionException: The user is not a member of the organization`

### 5.5 Verify Registrations

**[AI]**

```powershell
.\scripts\Test-EntraAppRegistrations.ps1
```

This checks:
- Both app registrations exist with correct permissions
- Secrets are stored in Key Vault
- Token acquisition succeeds for both apps

### 5.6 Grant Managed Identity Key Vault Access (⚠️ Both Slots)

**[AI]** CRITICAL: The staging slot has a **different managed identity** than production. Both need Key Vault access.

```powershell
# Production slot
$principalId = az webapp identity show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query principalId -o tsv

az role assignment create `
    --role "Key Vault Secrets User" `
    --assignee-object-id $principalId `
    --scope "/subscriptions/<sub-id>/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.KeyVault/vaults/sprk-platform-prod-kv"

# Staging slot (CRITICAL — different principal ID!)
$stagingPrincipalId = az webapp identity show -g rg-spaarke-platform-prod -n spaarke-bff-prod --slot staging --query principalId -o tsv

az role assignment create `
    --role "Key Vault Secrets User" `
    --assignee-object-id $stagingPrincipalId `
    --scope "/subscriptions/<sub-id>/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.KeyVault/vaults/sprk-platform-prod-kv"
```

---

## 6. Phase 3: Key Vault + App Settings

### 6.1 Seed Key Vault Secrets

**[AI]** Run the automated seeding script (recommended):

```powershell
.\scripts\Seed-ProductionKeyVault.ps1
```

Or manually set secrets (if script unavailable):

```powershell
$kvName = "sprk-platform-{env}-kv"

# Gather values
$openaiKey = az cognitiveservices account keys list -g rg-spaarke-platform-{env} -n spaarke-openai-{env} --query "key1" -o tsv
$docIntelKey = az cognitiveservices account keys list -g rg-spaarke-platform-{env} -n spaarke-docintel-{env} --query "key1" -o tsv
$searchKey = az search admin-key show -g rg-spaarke-platform-{env} --service-name spaarke-search-{env} --query "primaryKey" -o tsv
$sbusConn = az servicebus namespace authorization-rule keys list --namespace-name spaarke-{env}-sbus --resource-group rg-spaarke-platform-{env} --name RootManageSharedAccessKey --query primaryConnectionString --output tsv
$insightsConn = az monitor app-insights component show --app spaarke-{env}-insights --resource-group rg-spaarke-platform-{env} --query connectionString --output tsv

# Identity / Auth
az keyvault secret set --vault-name $kvName --name "TenantId" --value "<tenant-id>"
az keyvault secret set --vault-name $kvName --name "BFF-API-ClientId" --value "<bff-app-id>"
az keyvault secret set --vault-name $kvName --name "BFF-API-ClientSecret" --value "<bff-secret>"
az keyvault secret set --vault-name $kvName --name "BFF-API-Audience" --value "api://<bff-app-id>"
az keyvault secret set --vault-name $kvName --name "Dataverse-ServiceUrl" --value "https://spaarke-{env}.crm.dynamics.com"

# AI Services
az keyvault secret set --vault-name $kvName --name "ai-openai-endpoint" --value "https://spaarke-openai-{env}.openai.azure.com/"
az keyvault secret set --vault-name $kvName --name "ai-openai-key" --value "$openaiKey"
az keyvault secret set --vault-name $kvName --name "ai-docintel-endpoint" --value "https://spaarke-docintel-{env}.cognitiveservices.azure.com/"
az keyvault secret set --vault-name $kvName --name "ai-docintel-key" --value "$docIntelKey"
az keyvault secret set --vault-name $kvName --name "ai-search-endpoint" --value "https://spaarke-search-{env}.search.windows.net"
az keyvault secret set --vault-name $kvName --name "ai-search-key" --value "$searchKey"

# Infra
az keyvault secret set --vault-name $kvName --name "ServiceBus-ConnectionString" --value "$sbusConn"
az keyvault secret set --vault-name $kvName --name "AppInsights-ConnectionString" --value "$insightsConn"

# SPE (set after §9)
az keyvault secret set --vault-name $kvName --name "SPE-ContainerTypeId" --value "<container-type-id>"
az keyvault secret set --vault-name $kvName --name "SPE-DefaultContainerId" --value "<container-id>"
```

### 6.2 Configure App Settings with Key Vault References

**[AI]** Run the automated script:

```powershell
.\scripts\Configure-ProductionAppSettings.ps1
```

App settings use the `@Microsoft.KeyVault(...)` pattern — **no plaintext secrets in app settings**.

See [Appendix B](#appendix-b-complete-app-settings-reference) for the complete settings list.

> **Important CORS note**: The base `appsettings.json` contains localhost origins. In Production mode, non-HTTPS origins cause a startup crash. The script overrides CORS indices 0-4 with HTTPS-only values to replace ALL base origins.

---

## 7. Phase 4: Dataverse Solutions

### 7.1 Source: Where Solutions Come From

Dataverse solutions are **developed in dev** (unmanaged), **exported as managed**, and **imported to target environments**.

| Environment | Solution Type | Rationale |
|-------------|--------------|-----------|
| Dev (`spaarkedev1`) | Unmanaged | Active development — developers need to edit |
| Demo, Staging, Production | Managed | Clean uninstall, prevents ad-hoc changes, version rollback |

### 7.2 Export from Dev

**[AI]** Export the current state:

```bash
pac auth select --index {dev-profile-index}
pac solution export --name SpaarkeCore --path ./exports/SpaarkeCore.zip --overwrite
pac solution export --name SpaarkeFeatures --path ./exports/SpaarkeFeatures.zip --overwrite
# ... repeat for all 10 solutions
```

For **managed** export (production target):

```bash
pac solution export --name SpaarkeCore --managed true --path ./exports/SpaarkeCore_managed.zip --overwrite
```

### 7.3 Fix Pipeline (CRITICAL — Required Before Import)

The exported `SpaarkeCore.zip` typically contains issues that must be fixed before importing to a new environment. Run this pipeline:

```bash
# 1. Unpack
pac solution unpack --zipfile exports/SpaarkeCore.zip --folder exports/SC_unpacked --allowDelete true --allowWrite true

# 2. Remove stale PCF static parameters from ALL form XMLs
find exports/SC_unpacked -name "*.xml" -path "*/FormXml/*" \
  -exec sed -i 's/<tenantId type="[^"]*" static="true">[^<]*<\/tenantId>//g' {} \;
find exports/SC_unpacked -name "*.xml" -path "*/FormXml/*" \
  -exec sed -i 's/<apiBaseUrl type="[^"]*" static="true">[^<]*<\/apiBaseUrl>//g' {} \;
find exports/SC_unpacked -name "*.xml" -path "*/FormXml/*" \
  -exec sed -i 's/<bffApiUrl type="[^"]*" static="true">[^<]*<\/bffApiUrl>//g' {} \;

# 3. Fix SpeDocumentViewer manifest (tenantId required→false, remove dev URL)
sed -i 's/name="tenantId"\([^/]*\)required="true"/name="tenantId"\1required="false"/g' \
  exports/SC_unpacked/Controls/sprk_Spaarke.SpeDocumentViewer/ControlManifest.xml
sed -i 's/default-value="https:\/\/spe-api-dev-67e2xz.azurewebsites.net"//g' \
  exports/SC_unpacked/Controls/sprk_Spaarke.SpeDocumentViewer/ControlManifest.xml

# 4. Remove empty sitemaps (zero areas/groups cause import failure)
rm -rf exports/SC_unpacked/AppModuleSiteMaps/sprk_DocumentManagement
rm -rf exports/SC_unpacked/AppModuleSiteMaps/sprk_LawFirmCaseManagement
sed -i '/DocumentManagement/d; /LawFirmCaseManagement/d' exports/SC_unpacked/Other/Solution.xml

# 5. Remove canvas app components (type 300) — legacy, replaced by code pages
rm -rf exports/SC_unpacked/CanvasApps
sed -i '/type="300"/d' exports/SC_unpacked/Other/Solution.xml

# 6. Remove canvas app dependency references
sed -i '/AnalysisBuilder/d; /AnalysisWorkspace/d; /PlaybookBuilderHost/d' \
  exports/SC_unpacked/Other/Solution.xml

# 7. Remove app module + sitemaps if they reference canvas apps
rm -rf exports/SC_unpacked/AppModules/sprk_MatterManagement
rm -rf exports/SC_unpacked/AppModuleSiteMaps/sprk_MatterManagement
rm -rf exports/SC_unpacked/AppModuleSiteMaps/sprk_CorporateMatterManagement
sed -i '/MatterManagement/d; /CorporateMatterManagement/d' exports/SC_unpacked/Other/Solution.xml

# 8. Repack
pac solution pack --zipfile exports/SpaarkeCore_fixed.zip --folder exports/SC_unpacked
```

> **Why these fixes are needed**: See [Appendix A, Issues 1-4](#appendix-a-known-issues-and-workarounds) for full rationale.

### 7.4 Import Order (CRITICAL)

Import solutions in this strict order — **SpaarkeFeatures MUST be imported BEFORE SpaarkeCore** because Core entities reference web resources in their forms and ribbons:

```
Tier 1: SpaarkeCore           (base entities, option sets, security roles)
Tier 2: SpaarkeWebResources   (JS, icons)
Tier 3: Feature solutions      (AnalysisBuilder, CalendarSidePane, DocumentUploadWizard,
                                EventCommands, EventDetailSidePane, EventsPage,
                                LegalWorkspace, TodoDetailSidePane)
```

### 7.5 Import Solutions

**[AI]** Use the automated script (handles dependency order):

```powershell
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-{env}.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..." `
    -SolutionPath "./exports/"
```

Or manually:

```bash
pac auth select --index {target-profile-index}

# Import SpaarkeFeatures FIRST (web resources)
pac solution import --path ./exports/SpaarkeFeatures.zip --publish-changes --async

# Wait for completion, then import SpaarkeCore (entities)
pac solution import --path ./exports/SpaarkeCore_fixed.zip --publish-changes --async

# Then remaining 8 feature solutions in any order
```

### 7.6 Version Management

Bump solution version in dev **before** export:

```powershell
pac solution version --solution-name SpaarkeCore --strategy solution --value 1.2.0.0
```

| When to Bump | Version Part | Example |
|-------------|-------------|---------|
| Breaking schema changes | Major | 1.0 → 2.0 |
| New features, new entities | Minor | 1.1 → 1.2 |
| Bug fixes, form tweaks | Patch | 1.1.1 → 1.1.2 |
| Build/CI increments | Revision | 1.1.1.0 → 1.1.1.1 |

---

## 8. Phase 5: Dataverse Environment Variables

### 8.1 The 7 Canonical Variables

These variables are defined in the SpaarkeCore solution and must be set per environment:

| Variable | Schema Name | Type | Purpose | Example |
|----------|-------------|------|---------|---------|
| BFF API Base URL | `sprk_BffApiBaseUrl` | String | Base URL for BFF API calls | `https://api.spaarke.com/api` |
| BFF API App ID | `sprk_BffApiAppId` | String | OAuth scope audience | `api://<bff-app-id>` |
| MSAL Client ID | `sprk_MsalClientId` | String | UI MSAL Client ID | `<ui-spa-app-id>` |
| Tenant ID | `sprk_TenantId` | String | Azure AD Tenant ID | `a221a95e-6abc-4434-...` |
| Azure OpenAI Endpoint | `sprk_AzureOpenAiEndpoint` | String | Azure OpenAI service endpoint | `https://spaarke-openai-prod.openai.azure.com/` |
| Share Link Base URL | `sprk_ShareLinkBaseUrl` | String | Base URL for Spaarke-hosted landing page (future — GitHub #233). **Not the BFF API URL.** | `https://app.spaarke.com/doc` or *(empty)* |
| SPE Container ID | `sprk_SharePointEmbeddedContainerId` | String | **Default/fallback** SPE container. Actual container resolved per-record from Business Unit. | `b!...` |

> **⚠️ Data Type Warning**: All variables must be `String` (Text). Dataverse does **not allow** changing the data type of an existing env var definition — subsequent solution imports silently keep the wrong type. If you find a variable with the wrong type (e.g., "Decimal Number"), you must **delete the definition** and **re-import** the solution (or manually recreate with correct type if unmanaged).
>
> See [Appendix A, Issue 14](#appendix-a-known-issues-and-workarounds) for the full recovery procedure.

> **About `sprk_ShareLinkBaseUrl`**: Intended for a future Spaarke-hosted landing page that wraps SPE with Spaarke's authorization checks. Currently hardcoded in `OfficeService.GenerateShareLinkUrl` to `https://spaarke.app/doc/{documentId}` — the env var is **not read at runtime** until GitHub #233 lands. Leave empty or set to your intended landing page domain.

> **About `sprk_SharePointEmbeddedContainerId`**: This is a **default/fallback** only. The actual container used for a given document is resolved from the user's Business Unit (`sprk_containerid` field on the BU record). Setting this variable alone is not sufficient — BU-level containers must be provisioned via `Provision-Customer.ps1` Step 10.

### 8.2 Automated Setup (Preferred)

**[AI]** `Provision-Customer.ps1` Step 8 sets all 7 variables automatically during customer provisioning. See §12.

### 8.3 Manual Setup

**Via PAC CLI:**

```powershell
pac env variable set --name sprk_BffApiBaseUrl --value "https://api.spaarke.com/api"
pac env variable set --name sprk_BffApiAppId --value "api://<bff-app-id>"
pac env variable set --name sprk_MsalClientId --value "<ui-spa-app-id>"
pac env variable set --name sprk_TenantId --value "<tenant-id>"
pac env variable set --name sprk_AzureOpenAiEndpoint --value "https://spaarke-openai-{env}.openai.azure.com/"
pac env variable set --name sprk_ShareLinkBaseUrl --value ""  # See note above
pac env variable set --name sprk_SharePointEmbeddedContainerId --value "<container-id>"
```

**Via Power Platform Admin Center:**

1. **Power Platform Admin Center** > **Environments** > select env > **Solutions**
2. Open the **Spaarke** solution > **Environment Variables**
3. Set all 7 variables per the table above
4. Save

After setting manually, always run the validation script (§13).

### 8.4 Recovering from Wrong Data Type

If a variable shows the wrong data type (e.g., "Decimal Number" instead of "Text"):

**If SpaarkeCore is unmanaged:**
1. In the maker portal, navigate to the Spaarke solution > Environment Variables
2. Delete the value (Current Value → Delete)
3. Delete the definition (right-click variable → Delete from solution AND delete)
4. Re-import SpaarkeCore solution (which has `<Type>String</Type>` in source XML)
5. Set the value

**If SpaarkeCore is managed:**
1. Create a temporary unmanaged solution
2. Add the env var definition to it
3. Delete it via the temporary solution
4. Re-import the full SpaarkeCore solution
5. Set the value

See [Appendix A, Issue 14](#appendix-a-known-issues-and-workarounds) for detail.

---

## 9. Phase 6: SharePoint Embedded

SPE provides document storage. Each environment needs a **Container Type** (the blueprint) and the BFF API registered as the **owning application**.

> **Reference**: [HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md](./HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md) for detailed SPE setup with troubleshooting.

### 9.1 Ownership Architecture

```
Container Type (one per environment)
  - Owned by: spaarke-bff-api-{env} app registration
  - Container Type ID: stored in Key Vault + Dataverse env var
  - Registration: full delegated + full appOnly permissions

Containers (one per Business Unit):
  - Root BU → Container (sprk_containerid on BU record)
  - Child BU → Container (sprk_containerid on BU record)
  ...
```

The **BFF API app** has full permissions to create containers, manage files, and perform all SPE operations server-side using client credentials.

### 9.2 Create Container Type (SharePoint Admin Required)

**[HUMAN]** Must use **SPO Management Shell** (Windows PowerShell 5.1) — the Graph API returns 403 for container type creation even with all permissions.

```powershell
# Install if needed (Windows PowerShell 5.1 — NOT PowerShell 7)
# Install-Module -Name Microsoft.Online.SharePoint.PowerShell -Force

Connect-SPOService -Url "https://{tenant}-admin.sharepoint.com"

New-SPOContainerType `
    -ContainerTypeName "Spaarke {Env} Documents" `
    -OwningApplicationId "{bff-app-id}"

# Note the ContainerTypeId from output
```

Alternatively, use the automated scripts:

```powershell
.\scripts\Create-ContainerType-PowerShell.ps1 `
    -ContainerTypeName "Spaarke Document Storage" `
    -OwningAppId "<spaarke-bff-api-{env}-client-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"
```

### 9.3 Add Billing (Standard Container Types)

**[HUMAN]**

```powershell
Add-SPOContainerTypeBilling `
    -ContainerTypeId "{container-type-id}" `
    -AzureSubscriptionId "{subscription-id}" `
    -ResourceGroup "rg-spaarke-platform-{env}" `
    -Region "westus"    # NOTE: Must be Syntex-compatible region
```

> **⚠️ Region gotcha**: `-Region` must be a [Syntex-supported region](#syntex-supported-regions). **`westus2` is NOT supported** — use `westus`, `eastus`, `westeurope`, etc.

> **Syntex provider**: `Microsoft.Syntex` must be registered on the subscription first (done in §4.2). The `Add-SPOContainerTypeBilling` command triggers registration automatically but may fail if still registering. Wait 2-5 minutes and retry.

#### Syntex Supported Regions

`westus`, `eastus`, `eastus2`, `centralus`, `southcentralus`, `northcentralus`, `westeurope`, `northeurope`, `uksouth`, `ukwest`, `australiaeast`, `japaneast`, `canadacentral`, `brazilsouth`, `southeastasia`, `centralindia`, `koreacentral`, `francecentral`, `switzerlandnorth`, `norwayeast`, `uaenorth`, `southafricanorth`

**NOT supported**: `westus2`, `westus3`

### 9.4 Register Container Type (Graph API)

**[AI]** Register the BFF API as owning app with full permissions:

```powershell
.\scripts\Register-BffApiWithContainerType.ps1 `
    -ContainerTypeId "<container-type-id>" `
    -OwningAppId "<spaarke-bff-api-{env}-client-id>" `
    -OwningAppSecret "<secret-from-key-vault>" `
    -TenantId "<tenant-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"
```

Manual equivalent:

```powershell
$t = Invoke-RestMethod -Uri "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token" -Method Post -Body @{
    client_id="{bff-app-id}"; client_secret="{bff-secret}"; scope="https://graph.microsoft.com/.default"; grant_type="client_credentials"
}

$body = @{
    applicationPermissionGrants = @(@{
        appId = "{bff-app-id}"
        delegatedPermissions = @("full")
        applicationPermissions = @("full")
    })
} | ConvertTo-Json -Depth 3

Invoke-RestMethod -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containerTypeRegistrations/{container-type-id}" `
    -Method Put -Headers @{Authorization="Bearer $($t.access_token)";"Content-Type"="application/json"} `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($body))
```

### 9.5 Create Root Container

> **⚠️ Wait period**: Allow 10-30 minutes after registration for permissions to propagate into the Graph token. The `FileStorageContainer.Selected` role must appear in the token claims before container creation succeeds.

```powershell
$body = @{
    displayName = "{Env} Root Documents"
    containerTypeId = "{container-type-id}"
} | ConvertTo-Json

$container = Invoke-RestMethod -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containers" `
    -Method Post -Headers @{Authorization="Bearer $($t.access_token)";"Content-Type"="application/json"} `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($body))

# Save container.id — you'll need it

# Activate
Invoke-RestMethod -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containers/$($container.id)/activate" `
    -Method Post -Headers @{Authorization="Bearer $($t.access_token)";"Content-Type"="application/json"}
```

### 9.6 Store Container IDs

```powershell
az keyvault secret set --vault-name sprk-platform-{env}-kv --name SPE-ContainerTypeId --value "<container-type-id>"
az keyvault secret set --vault-name sprk-platform-{env}-kv --name SPE-DefaultContainerId --value "<container-id>"
pac env variable set --name sprk_SharePointEmbeddedContainerId --value "<container-id>"
```

Also update the BFF API app setting:

```powershell
az webapp config appsettings set `
    -g rg-spaarke-platform-{env} -n spaarke-bff-{env} `
    --settings "Spe__ContainerTypeId=@Microsoft.KeyVault(VaultName=sprk-platform-{env}-kv;SecretName=SPE-ContainerTypeId)"
```

### 9.7 Per-BU Container Provisioning

Containers are created **per business unit** — each BU gets one SPE container stored in `businessunit.sprk_containerid`.

**During initial provisioning**: `Provision-Customer.ps1` Step 10 creates the SPE container for the root BU and sets `sprk_containerid` on the BU record.

**Adding new BUs later**:

```powershell
.\scripts\New-BusinessUnitContainer.ps1 `
    -BusinessUnitId "<bu-guid>" `
    -BusinessUnitName "New Business Unit" `
    -ContainerTypeId "<container-type-id>" `
    -DataverseUrl "https://<env>.crm.dynamics.com"
```

### 9.8 Verify SPE Setup

```powershell
.\scripts\Check-ContainerType-Registration.ps1 `
    -ContainerTypeId "<container-type-id>" `
    -SharePointDomain "<tenant>.sharepoint.com" `
    -OwningAppId "<bff-api-client-id>"

.\scripts\Test-SharePointToken.ps1 `
    -ClientId "<bff-api-client-id>" `
    -ClientSecret "<secret>" `
    -TenantId "<tenant-id>" `
    -ContainerTypeId "<container-type-id>" `
    -SharePointDomain "<tenant>.sharepoint.com"

az webapp restart --name spaarke-bff-{env} --resource-group rg-spaarke-platform-{env}
```

---

## 10. Phase 7: BFF API Deployment

### 10.1 Production: Slot Deploy (Zero Downtime)

**[AI]**

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

### 10.2 Non-Production: Direct Deploy

**[AI]**

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment dev `
    -ResourceGroupName "rg-spaarke-platform-dev" `
    -AppServiceName "spaarke-bff-dev"
```

### 10.3 Redeploy Without Rebuild

For config-only changes:

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy `
    -SkipBuild
```

### 10.4 Manual Deployment (Fallback)

```bash
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o ./publish/bff-api
pwsh -Command "Compress-Archive -Path 'publish/bff-api/*' -DestinationPath 'exports/bff-api.zip' -Force"
MSYS_NO_PATHCONV=1 az webapp deploy --resource-group rg-spaarke-platform-{env} \
  --name spaarke-bff-{env} --src-path exports/bff-api.zip --type zip
```

### 10.5 Verify

```bash
curl https://spaarke-bff-{env}.azurewebsites.net/healthz
# Expected: "Healthy" (HTTP 200)
```

### 10.6 Startup Troubleshooting

If the BFF API fails to start, follow this diagnostic process:

```bash
# 1. Enable verbose logging
MSYS_NO_PATHCONV=1 az webapp log config --name spaarke-bff-{env} \
  --resource-group rg-spaarke-platform-{env} \
  --docker-container-logging filesystem --application-logging filesystem --level verbose

# 2. Restart
az webapp restart --name spaarke-bff-{env} --resource-group rg-spaarke-platform-{env}

# 3. Wait 2 minutes, download logs
az webapp log download --name spaarke-bff-{env} --resource-group rg-spaarke-platform-{env} --log-file logs.zip

# 4. Check the DEFAULT docker log (NOT *_docker.log which is lifecycle only)
grep "Unhandled" LogFiles/*default_docker.log
```

| Error Pattern | Fix |
|--------------|-----|
| `SpeAdmin:KeyVaultUri configuration is required` | Add `KeyVaultUri` and `SpeAdmin__KeyVaultUri` app settings |
| `CORS: Non-HTTPS origin not allowed in Production` | Override `Cors__AllowedOrigins__0-4` with HTTPS-only origins |
| `The user is not a member of the organization` | Create Dataverse application user in PP Admin Center (§5.4) |
| `ServiceBus:QueueName is required` | Add `ServiceBus__QueueName=sdap-jobs` |
| `Failure to infer one or more parameters` (chatClient UNKNOWN) | Add `AzureOpenAI__ChatModelName=gpt-4o` |
| `ConnectionStrings:ServiceBus is required` | Set `ConnectionStrings__ServiceBus` via Key Vault ref |
| Key Vault reference not resolving | Verify managed identity has `Key Vault Secrets User` role (§5.6) — **check BOTH slots** |

---

## 11. Phase 8: Custom Domain + SSL (Production)

### 11.1 Get DNS Instructions

**[AI]**

```powershell
.\scripts\Configure-CustomDomain.ps1 -ShowDnsInstructions
```

### 11.2 Create DNS Records (BOTH Required!)

**[HUMAN]** In your DNS provider, create **both** records:

| Type | Name | Value | TTL |
|------|------|-------|-----|
| **CNAME** | `api.spaarke.com` | `spaarke-bff-prod.azurewebsites.net` | 3600 |
| **TXT** | `asuid.api.spaarke.com` | `<verification-id from Step 11.1>` | 3600 |

> **⚠️ CRITICAL**: Both CNAME and TXT records are **required** by Azure App Service. The TXT record is the domain verification token. Without it, the custom domain binding will fail.

To get the verification ID:

```powershell
az webapp show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query "customDomainVerificationId" -o tsv
```

### 11.3 Wait for DNS Propagation

**[HUMAN]**

```powershell
nslookup api.spaarke.com
# Should resolve to spaarke-bff-prod.azurewebsites.net

nslookup -type=TXT asuid.api.spaarke.com
# Should return the verification ID
```

Propagation typically takes 5-30 minutes, can take up to 24 hours.

### 11.4 Bind Domain and SSL

**[AI]**

```powershell
.\scripts\Configure-CustomDomain.ps1
```

The script:
1. Validates DNS records resolve correctly
2. Adds custom domain hostname binding to App Service
3. Creates Azure-managed SSL certificate (auto-renewal)
4. Binds SSL with SNI
5. Enforces HTTPS-only (HTTP → HTTPS redirect)
6. Configures CORS for production origins

### 11.5 Verify

```powershell
.\scripts\Test-CustomDomain.ps1

# Or manually:
curl https://api.spaarke.com/healthz
```

---

## 12. Phase 9: Customer Provisioning

### 12.1 Preview Provisioning

**[AI+HUMAN]**

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<service-principal-client-id>" `
    -ClientSecret "<service-principal-secret>" `
    -WhatIf
```

### 12.2 Execute Provisioning

**[AI]**

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

### 12.3 13-Step Pipeline

| Step | Description | Duration | Type |
|------|-------------|----------|------|
| 1 | Validate inputs and prerequisites | 10s | [AI] |
| 2 | Create resource group `rg-spaarke-{customerId}-{env}` | 15s | [AI] |
| 3 | Deploy `customer.bicep` (Storage, Key Vault, Service Bus, Redis) | 5-8 min | [AI] |
| 4 | Populate customer Key Vault with secrets | 30s | [AI] |
| 5 | Create Dataverse environment via Admin API | 30s | [AI] |
| 6 | Wait for Dataverse provisioning | 5-15 min | [AI] |
| 7 | Import managed solutions (10 solutions in dependency order) | 5-10 min | [AI] |
| 8 | Set Dataverse Environment Variables (7 required) | 30s | [AI] |
| 9 | Generate `environment-config.json` | 5s | [AI] |
| 10 | Provision SPE containers (root BU) | 1-2 min | [AI] |
| 11 | Register in BFF API tenant registry | 15s | [AI] |
| 12 | Run smoke tests (`Test-Deployment.ps1`) | 1-2 min | [AI] |
| 13 | Validate environment (`Validate-DeployedEnvironment.ps1`) | 30s | [AI] |

**Total**: 20-30 minutes

### 12.4 Resume From Step (Idempotent)

If the script fails at any step:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>" `
    -ResumeFromStep 5
```

State is persisted between runs.

### 12.5 Demo-Specific Steps

For the demo customer only:

```powershell
# Load sample data
.\scripts\Load-DemoSampleData.ps1

# Invite demo users (B2B guest invitations)
.\scripts\Invite-DemoUsers.ps1
```

---

## 13. Phase 10: Validation

### 13.1 Validate Deployed Environment

**[AI]**

```powershell
.\scripts\Validate-DeployedEnvironment.ps1 `
    -DataverseUrl "https://spaarke-{env}.crm.dynamics.com" `
    -BffApiUrl "https://api.spaarke.com/api"
```

**What it checks (4 categories):**

| Category | Checks | Pass Criteria |
|----------|--------|---------------|
| **Env Vars** | All 7 Dataverse env vars exist and have non-empty values | All 7 present with values |
| **BFF API** | `GET /healthz` and `GET /ping` return HTTP 200 | Both healthy |
| **CORS** | Preflight OPTIONS request includes Dataverse origin in `Access-Control-Allow-Origin` | Dataverse URL allowed |
| **Dev Leakage** | Scans env var values for dev-only identifiers (`spaarkedev1`, `spe-api-dev`, `67e2xz`, known dev GUIDs) | No dev values detected |

Expected output:

```
  [PASS] Env Vars            Pass: 7  Fail: 0  Warn: 0
  [PASS] BFF API             Pass: 2  Fail: 0  Warn: 0
  [PASS] CORS                Pass: 1  Fail: 0  Warn: 0
  [PASS] Dev Leakage         Pass: 6  Fail: 0  Warn: 0
  VERDICT: PASSED
```

### 13.2 Run Smoke Tests (17 Tests)

**[AI]**

```powershell
.\scripts\Test-Deployment.ps1 -EnvironmentName prod
```

| Group | Tests | Checks |
|-------|-------|--------|
| BFF API Health | 3 | `/healthz`, `/ping`, response headers |
| Dataverse | 3 | Connection, solution presence, WhoAmI |
| SPE | 2 | Container exists, file operations |
| AI Services | 4 | OpenAI chat, embeddings, AI Search query, Doc Intelligence |
| Service Bus | 2 | Connection, send/receive |
| Redis | 3 | Connection, set/get, TTL |

### 13.3 Manual Verification Checklist

| Check | Command | Expected |
|-------|---------|----------|
| API health (custom domain) | `curl https://api.spaarke.com/healthz` | `Healthy` (200) |
| API health (direct) | `curl https://spaarke-bff-prod.azurewebsites.net/healthz` | `Healthy` (200) |
| SSL cert valid | `curl -vI https://api.spaarke.com 2>&1 \| grep "subject"` | Valid cert |
| App running | `az webapp show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query state` | `Running` |
| Key Vault | `az keyvault secret list --vault-name sprk-platform-prod-kv --query "[].name"` | 10+ secrets |
| Platform RG | `az group show -n rg-spaarke-platform-prod` | Exists |
| Customer RG | `az group show -n rg-spaarke-demo-prod` | Exists |
| Solutions | `pac solution list` | 10 managed solutions |

---

## 14. Day-2 Operations

### 14.1 Subsequent BFF API Deployments

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy
```

### 14.2 Provisioning Additional Customers

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -DisplayName "Acme Legal" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

See [Customer Onboarding Runbook](./CUSTOMER-ONBOARDING-RUNBOOK.md) for the complete lifecycle.

### 14.3 Customer Decommissioning

**[AI+HUMAN]** Preview first, then execute:

```powershell
# Preview
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

### 14.4 Secret Rotation (90-Day Recommended)

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

### 14.5 Viewing Logs

```powershell
# Live log stream
az webapp log tail -g rg-spaarke-platform-prod -n spaarke-bff-prod

# Staging slot logs
az webapp log tail -g rg-spaarke-platform-prod -n spaarke-bff-prod --slot staging
```

For App Insights KQL queries, see [Monitoring and Alerting Guide](./MONITORING-AND-ALERTING-GUIDE.md).

---

## 15. Rollback Procedures

### 15.1 BFF API

Automatic rollback is built into `Deploy-BffApi.ps1` when `-UseSlotDeploy` is used.

For manual rollback after a completed swap:

```powershell
# Previous version is in staging slot after a swap — swap back
az webapp deployment slot swap `
    -g rg-spaarke-platform-prod `
    -n spaarke-bff-prod `
    --slot staging `
    --target-slot production
```

Verify: `curl https://api.spaarke.com/healthz`

### 15.2 Platform Infrastructure

Bicep is idempotent. Redeploy previous version:

```powershell
git checkout <previous-commit> -- infrastructure/bicep/
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod
```

### 15.3 Dataverse Solutions

**[HUMAN]** Managed solutions support version rollback in Power Platform Admin Center:
1. Environment > Solutions > select solution > Solution history
2. Import the previous managed solution version

---

## 16. Troubleshooting

### 16.1 Decision Tree for Claude Code

```
API returns 503 after deployment?
  → Check staging slot Key Vault RBAC (§5.6)
  → Check managed identity principal ID for BOTH slots
  → Restart: az webapp restart -g rg-spaarke-platform-prod -n spaarke-bff-prod

Health check fails on staging slot?
  → Staging slot has DIFFERENT managed identity than production
  → Grant Key Vault Secrets User to staging slot's principal ID

Bicep deployment fails with QuotaExceeded?
  → Check Appendix A for OpenAI region and AI Search SKU
  → Request quota increases in Azure Portal > Subscription > Usage + quotas

Custom domain binding fails?
  → Verify BOTH DNS records exist: CNAME + TXT (asuid.*)
  → Get verification ID: az webapp show ... --query customDomainVerificationId
  → Wait for DNS propagation (nslookup to verify)

Dataverse environment creation times out?
  → Check Power Platform Admin Center for status
  → If created but script timed out: -ResumeFromStep 7

Solution import fails?
  → Check Appendix A Issues 1-7 for known causes
  → Max upload size, PCF manifest, canvas apps, empty sitemaps, import order

Env var shows wrong data type?
  → Delete definition + re-import solution (see §8.4 and Appendix A Issue 14)

PAC CLI output capture fails?
  → PAC is a .cmd wrapper on Windows
  → Use: cmd /c pac <command>

Redis connection refused?
  → Set Redis__Enabled=false until per-customer Redis provisioned
  → az webapp config appsettings set -g ... --settings Redis__Enabled=false
```

### 16.2 Common Issues Quick Reference

| Symptom | Cause | Fix |
|---------|-------|-----|
| 503 after deploy | Key Vault refs not resolving | Grant managed identity `Key Vault Secrets User` role |
| Staging slot 503 | Different managed identity | Grant staging slot separate RBAC |
| `QuotaExceeded` on OpenAI | Region capacity | Deploy to westus3 with `openAiLocation` parameter |
| `SkuNotAvailable` on AI Search | Standard2 unavailable | Use `standard` SKU |
| DNS TXT record error | Missing verification record | Add `asuid.*` TXT record |
| PAC CLI hangs | .cmd wrapper issue | Use `cmd /c pac` |
| Redis timeout | Not provisioned yet | Set `Redis__Enabled=false` |
| Service Bus name conflict | `-sb` suffix reserved | Use `-sbus` suffix |
| Solution import fails | Dependency order wrong | `Deploy-DataverseSolutions.ps1` handles ordering |
| Env var wrong data type | Type immutable in Dataverse | Delete + re-import (§8.4) |

### 16.3 Known Issues Catalog

See [Appendix A](#appendix-a-known-issues-and-workarounds) for the complete catalog of 14 documented issues with symptoms, causes, and fixes.

---

## 17. CI/CD Integration (Azure)

Three GitHub Actions workflows automate deployment after initial setup:

### 17.1 deploy-platform.yml

**Trigger**: Manual dispatch (`workflow_dispatch`)

```yaml
# 3-job pipeline:
# 1. what-if: Preview changes (always runs)
# 2. deploy: Apply changes (requires environment approval)
# 3. verify: Run smoke tests
```

### 17.2 deploy-bff-api.yml

**Trigger**: Push to `master` (path filter: `src/server/api/**`)

```yaml
# 8-job pipeline with zero-downtime:
# 1. build → 2. test → 3. deploy-staging → 4. health-check-staging
# → 5. swap → 6. health-check-production → 7. rollback (on failure)
# → 8. notify
```

### 17.3 provision-customer.yml

**Trigger**: Manual dispatch with customer parameters

```yaml
# 4-job pipeline:
# 1. validate-inputs → 2. provision → 3. verify → 4. audit-trail
```

### 17.4 GitHub Environment Protection

| Environment | Protection Rules |
|-------------|-----------------|
| staging | Auto-approve, wait timer: 0 |
| production | Required reviewer, wait timer: 5 minutes |

See [GitHub Environment Protection](./GITHUB-ENVIRONMENT-PROTECTION.md) for setup details.

### 17.5 Required GitHub Secrets

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC federated credential (not a password) |
| `AZURE_TENANT_ID` | Entra ID tenant |
| `AZURE_SUBSCRIPTION_ID` | Target subscription |

> **Note**: GitHub Actions use OIDC (federated credentials), not service principal secrets.

---

## 18. CI/CD for Dataverse

### 18.1 Current State

| Step | Automated? | Tool |
|------|-----------|------|
| PCF control build | Partial | `npm run build` + `pac pcf push` (manual) |
| Code page build | Partial | Build scripts exist, manual deployment |
| Solution export from dev | **No** | Manual `pac solution export` |
| Solution import to prod | **Yes** | `Deploy-DataverseSolutions.ps1` |
| Solution version bumping | **No** | Manual `pac solution version` |
| Schema changes | **No** | Manual in maker portal |

### 18.2 Recommended Architecture

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
│    ├── pac solution check                                │
│    └── Report warnings/errors                            │
│                                                          │
│  Job 3: deploy-staging (depends on Job 2)                │
│    ├── pac auth create (staging env)                      │
│    └── pac solution import (managed, staging)             │
│                                                          │
│  Job 4: deploy-production (depends on Job 3, approval)   │
│    ├── Required reviewer                                  │
│    └── pac solution import (managed, prod)                │
└──────────────────────────────────────────────────────────┘
```

### 18.3 PAC CLI Service Principal Auth

```yaml
- name: Authenticate PAC CLI
  run: |
    pac auth create \
      --environment ${{ vars.DATAVERSE_URL }} \
      --tenant ${{ secrets.AZURE_TENANT_ID }} \
      --applicationId ${{ secrets.DATAVERSE_CLIENT_ID }} \
      --clientSecret ${{ secrets.DATAVERSE_CLIENT_SECRET }}
```

Required secrets: `DATAVERSE_CLIENT_ID`, `DATAVERSE_CLIENT_SECRET`, `AZURE_TENANT_ID`
Required vars: `DATAVERSE_URL_DEV`, `DATAVERSE_URL_PROD`

### 18.4 Microsoft Power Platform GitHub Actions

Microsoft provides official actions:

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

### 18.5 Solution Packager (Advanced)

For schema-change code review:

```powershell
# Unpack to source
pac solution unpack --solution-zip SpaarkeCore_managed.zip --folder src/solutions/SpaarkeCore/

# Repack for deployment
pac solution pack --folder src/solutions/SpaarkeCore/ --zip-file SpaarkeCore_managed.zip --type managed
```

**Pros**: Full git history of schema changes, code review on form/entity changes, merge support.
**Cons**: Adds complexity, unpack/pack can drift from environment state.

**Recommendation**: Start with managed export/import. Add Solution Packager when team needs schema-change review.

---

## Quick Reference: Complete Deployment Sequence

For a fresh production deployment:

```
PHASE 1: AZURE INFRASTRUCTURE                               [AI]
  1. Deploy-Platform.ps1 -EnvironmentName prod -WhatIf       (preview)
  2. Deploy-Platform.ps1 -EnvironmentName prod                (deploy, 10-20 min)

PHASE 2: ENTRA ID                                           [AI + HUMAN]
  3. Register-EntraAppRegistrations.ps1                       (create apps)
  4. Azure Portal: Grant admin consent                        [HUMAN]
  5. Power Platform: Create application user                  [HUMAN]
  6. Test-EntraAppRegistrations.ps1                           (verify)
  7. Grant managed identity Key Vault RBAC (BOTH slots!)      [AI]

PHASE 3: SPE (SHAREPOINT EMBEDDED)                          [HUMAN + AI]
  8. Create SPE container type (SPO Mgmt Shell)               [HUMAN]
  9. Add billing (westus region!)                             [HUMAN]
  10. Register-BffApiWithContainerType.ps1                    [AI]
  11. Create root container                                   [AI]
  12. Store IDs in Key Vault + env var                        [AI]

PHASE 4: SECRETS & SETTINGS                                 [AI]
  13. Seed-ProductionKeyVault.ps1                             (seed secrets)
  14. Configure-ProductionAppSettings.ps1                     (KV references)

PHASE 5: BFF API                                            [AI]
  15. Deploy-BffApi.ps1 -UseSlotDeploy                        (deploy, 3-5 min)

PHASE 6: CUSTOM DOMAIN                                      [AI + HUMAN]
  16. Configure-CustomDomain.ps1 -ShowDnsInstructions         (get DNS)
  17. Create CNAME + TXT DNS records                          [HUMAN]
  18. Wait for DNS propagation                                [HUMAN]
  19. Configure-CustomDomain.ps1                              (bind + SSL)

PHASE 7: DATAVERSE SOLUTIONS                                [AI]
  20. Deploy-DataverseSolutions.ps1                           (import, 10-30 min)

PHASE 8: FIRST CUSTOMER                                     [AI]
  21. Provision-Customer.ps1 -WhatIf                          (preview)
  22. Provision-Customer.ps1                                  (provision, 20-30 min)

PHASE 9: VERIFY                                             [AI]
  23. Test-Deployment.ps1                                     (17 smoke tests)
  24. Validate-DeployedEnvironment.ps1                        (env vars, CORS, leakage)
```

**Total**: 3-5 hours first time. Steps 1-2, 9-15, 19-24 are fully automatable.

---

## Appendix A: Known Issues and Workarounds

Discovered during multiple deployments (March-April 2026) and critical for future deployments.

### Issue 1: PCF Manifest Mismatch (Form XML)

**Symptom**: Import fails with `Property tenantId is not declared in the control manifest`

**Cause**: R2 migration removed `tenantId`/`apiBaseUrl` from PCF control manifests but form XML still has static values referencing these properties.

**Fix**: Remove stale static params from form XMLs in the unpacked solution (§7.3 step 2).

### Issue 2: SpeDocumentViewer Required Properties

**Symptom**: Import fails with `Property tenantId is required, but the declaration is missing`

**Cause**: SpeDocumentViewer manifest has `tenantId` as `required="true"` with hardcoded dev URL.

**Fix**: Change to `required="false"` and remove dev URL default in the unpacked solution (§7.3 step 3).

### Issue 3: Empty Sitemaps

**Symptom**: Import fails with `SiteMap needs to have a non-empty Area with a non-empty Group`

**Cause**: DocumentManagement and LawFirmCaseManagement sitemaps have zero areas/groups.

**Fix**: Remove from unpacked solution (§7.3 step 4).

### Issue 4: Canvas App Dependencies

**Symptom**: Import fails with `Some dependencies are missing` referencing type 66 (CustomControl) for AnalysisBuilder, AnalysisWorkspace, PlaybookBuilderHost.

**Cause**: Legacy canvas apps (custom pages) pulled into SpaarkeCore by `--AddRequiredComponents`. These depend on PCF controls that may not be in the solution.

**Fix**: Remove all canvas apps (type 300) and their dependency references from Solution.xml (§7.3 steps 5-7).

### Issue 5: Solution Import Order

**Rule**: SpaarkeFeatures MUST be imported BEFORE SpaarkeCore.

**Cause**: SpaarkeCore entities reference web resources (icons, JS files) in forms and ribbons. These web resources are in SpaarkeFeatures.

### Issue 6: Dataverse Max Upload Size

**Symptom**: Import fails with `Webresource content size is too big`

**Cause**: Default 5MB limit is too small for PCF control bundles (VisualHost is ~10MB).

**Fix**: Increase `maxuploadfilesize` to 32MB via Dataverse API before import (§4.7).

### Issue 7: CORS Localhost Origins

**Symptom**: BFF API crashes on startup with `CORS: Non-HTTPS origin 'http://127.0.0.1:3000' is not allowed in Production environment`

**Cause**: Base `appsettings.json` has localhost origins. App Service env var array settings **merge** with base config. Production CORS validation rejects non-HTTPS origins.

**Fix**: Override CORS indices 0-4 via app settings to replace ALL base origins.

### Issue 8: SPE Container Type Creation

**Requirement**: Use `New-SPOContainerType` (SPO Management Shell), NOT the Graph API.

**Cause**: Graph API returns 403 for container type creation even with all permissions. Container type creation requires SharePoint Admin access via SPO Mgmt Shell.

### Issue 9: SPE Billing Region

**Requirement**: Use `westus` (not `westus2`) for the `-Region` parameter in `Add-SPOContainerTypeBilling`.

**Cause**: Microsoft.Syntex doesn't support `westus2` or `westus3`. See [§9.3 Syntex Supported Regions](#syntex-supported-regions).

### Issue 10: Missing AzureOpenAI ChatModelName

**Symptom**: BFF API crashes on startup with `Failure to infer one or more parameters` for `chatClient` (UNKNOWN source)

**Cause**: `AzureOpenAI:ChatModelName` not configured. Without it, `IChatClient` is not registered in DI (registration is conditional on both `Endpoint` and `ChatModelName` being set).

**Fix**: Add `AzureOpenAI__ChatModelName=gpt-4o` (must match deployed model name).

### Issue 11: Dataverse Application User Required

**Symptom**: BFF API crashes with `DataverseConnectionException: The user is not a member of the organization`

**Cause**: BFF API app registration is not registered as an application user in the target Dataverse environment.

**Fix**: Power Platform Admin Center → Environments → {env} → Settings → Users + permissions → Application users → + New app user → select BFF API app → assign System Administrator. **Cannot be done via CLI or API.** (§5.4)

### Issue 12: Graph Token Propagation

**Symptom**: SPE container creation returns 403 immediately after granting permissions.

**Cause**: Newly granted app role assignments take 10-30 minutes to appear in the Graph client credentials token.

**Fix**: Wait for token cache to expire, or generate a new client secret to force a new token.

### Issue 13: Staging Slot Managed Identity

**Symptom**: After deploying to staging slot, health check fails with 503 — Key Vault references can't resolve.

**Cause**: The staging slot's managed identity is a **separate principal** from production slot. `Key Vault Secrets User` role must be granted to both.

**Fix**: §5.6 — grant role to staging slot's `principalId` separately.

### Issue 14: Environment Variable Data Type Immutable

**Symptom**: Dataverse env var shows wrong data type (e.g., "Decimal Number" when should be "Text" / String).

**Cause**: Once an env var definition is created in Dataverse, **its data type cannot be changed** via solution upgrade — subsequent solution imports silently keep the existing (wrong) type.

**Fix**:
- **If unmanaged**: Delete the value → Delete the definition → Re-import solution (creates definition fresh with correct type from XML) → Set the value
- **If managed**: Create a temporary unmanaged solution containing the env var → Delete via the temporary solution → Re-import full solution → Set the value
- Schema names are case-sensitive for solution tracking — use exact case from source XML

See §8.4 for the full recovery procedure.

### Issue 15: Azure OpenAI Not in westus2

**Issue**: Azure OpenAI GPT-4o and GPT-4o-mini are NOT available in westus2.

**Resolution**: Deploy OpenAI to `westus3` using the `openAiLocation` parameter in `platform-{env}.bicepparam`. All other resources remain in westus2.

**Impact**: GPT-4o capacity limited to 50K TPM in westus3 (lower than westus2 would offer).

### Issue 16: AI Search Standard2 Not Available

**Issue**: AI Search `standard2` SKU provisioning fails in westus2 with insufficient capacity.

**Resolution**: Use `standard` SKU. Sufficient for current workload. Monitor and upgrade when capacity available.

### Issue 17: Service Bus `-sb` Is Reserved

**Issue**: Azure rejects Service Bus names ending in `-sb`.

**Resolution**: Use `-sbus` suffix: `sprk-{customerId}-{env}-sbus`.

### Issue 18: Custom Domain Requires BOTH CNAME and TXT

**Issue**: Azure App Service custom domain binding requires TWO DNS records, not just CNAME. TXT record (`asuid.api.spaarke.com`) contains the domain verification token.

**Resolution**: `Configure-CustomDomain.ps1 -ShowDnsInstructions` outputs exact values for both.

### Issue 19: PAC CLI on Windows

**Issue**: `pac` commands fail to capture output in PowerShell because `pac` is a `.cmd` file, not a native executable.

**Resolution**: Use `cmd /c pac <args>` for output capture in scripts. `Test-Deployment.ps1` handles this automatically.

### Issue 20: Document Intelligence API Version

**Issue**: The legacy `formrecognizer/info` endpoint no longer works.

**Resolution**: Use `formrecognizer/documentModels` with GA API version `2024-11-30`.

---

## Appendix B: Complete App Settings Reference

All settings required for the BFF API App Service. Values use Key Vault references (`@Microsoft.KeyVault(VaultName=sprk-platform-{env}-kv;SecretName=...)`) for secrets.

| Setting | Value | Source |
|---------|-------|--------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Static |
| `TENANT_ID` | KV: `TenantId` | Key Vault |
| `API_APP_ID` | KV: `BFF-API-ClientId` | Key Vault |
| `API_CLIENT_SECRET` | KV: `BFF-API-ClientSecret` | Key Vault |
| `DEFAULT_CT_ID` | KV: `SPE-ContainerTypeId` | Key Vault |
| `KeyVaultUri` | `https://sprk-platform-{env}-kv.vault.azure.net/` | Static |
| `SpeAdmin__KeyVaultUri` | `https://sprk-platform-{env}-kv.vault.azure.net/` | Static |
| `AzureAd__TenantId` | KV: `TenantId` | Key Vault |
| `AzureAd__ClientId` | KV: `BFF-API-ClientId` | Key Vault |
| `AzureAd__ClientSecret` | KV: `BFF-API-ClientSecret` | Key Vault |
| `AzureAd__Audience` | KV: `BFF-API-Audience` | Key Vault |
| `Graph__TenantId` | KV: `TenantId` | Key Vault |
| `Graph__ClientId` | KV: `BFF-API-ClientId` | Key Vault |
| `Graph__ClientSecret` | KV: `BFF-API-ClientSecret` | Key Vault |
| `Graph__Scopes__0` | `https://graph.microsoft.com/.default` | Static |
| `Dataverse__TenantId` | KV: `TenantId` | Key Vault |
| `Dataverse__ServiceUrl` | KV: `Dataverse-ServiceUrl` | Key Vault |
| `Dataverse__EnvironmentUrl` | KV: `Dataverse-ServiceUrl` | Key Vault |
| `Dataverse__ClientId` | KV: `BFF-API-ClientId` | Key Vault |
| `Dataverse__ClientSecret` | KV: `BFF-API-ClientSecret` | Key Vault |
| `AzureOpenAI__Endpoint` | KV: `ai-openai-endpoint` | Key Vault |
| `AzureOpenAI__ApiKey` | KV: `ai-openai-key` | Key Vault |
| `AzureOpenAI__ChatModelName` | `gpt-4o` | Static (must match deployed model) |
| `DocumentIntelligence__OpenAiEndpoint` | KV: `ai-openai-endpoint` | Key Vault |
| `DocumentIntelligence__OpenAiKey` | KV: `ai-openai-key` | Key Vault |
| `DocumentIntelligence__DocIntelEndpoint` | KV: `ai-docintel-endpoint` | Key Vault |
| `DocumentIntelligence__DocIntelKey` | KV: `ai-docintel-key` | Key Vault |
| `DocumentIntelligence__AiSearchEndpoint` | KV: `ai-search-endpoint` | Key Vault |
| `DocumentIntelligence__AiSearchKey` | KV: `ai-search-key` | Key Vault |
| `AiSearch__Endpoint` | KV: `ai-search-endpoint` | Key Vault |
| `AiSearch__ApiKey` | KV: `ai-search-key` | Key Vault |
| `ConnectionStrings__ServiceBus` | KV: `ServiceBus-ConnectionString` | Key Vault |
| `ServiceBus__ConnectionString` | KV: `ServiceBus-ConnectionString` | Key Vault |
| `ServiceBus__QueueName` | `sdap-jobs` | Static |
| `ApplicationInsights__ConnectionString` | KV: `AppInsights-ConnectionString` | Key Vault |
| `ApplicationInsightsAgent_EXTENSION_VERSION` | `~3` | Static |
| `Redis__Enabled` | `false` | Static (set `true` when Redis configured) |
| `ScheduledRagIndexing__TenantId` | KV: `TenantId` | Key Vault |
| `Analysis__KeyVaultUrl` | `https://sprk-platform-{env}-kv.vault.azure.net/` | Static |
| `Analysis__PromptFlowEndpoint` | `https://placeholder.api.azureml.ms` | Placeholder |
| `Analysis__PromptFlowKey` | `placeholder` | Placeholder |
| `Communication__ArchiveContainerId` | `placeholder` | Placeholder |
| `Communication__DefaultMailbox` | `placeholder` | Placeholder |
| `Communication__WebhookClientState` | `placeholder` | Placeholder |
| `Communication__WebhookNotificationUrl` | `placeholder` | Placeholder |
| `Email__DefaultContainerId` | KV: `SPE-DefaultContainerId` | Key Vault |
| `Email__WebhookSecret` | `placeholder` | Placeholder |
| `Cors__AllowedOrigins__0` | `https://spaarke-{env}.crm.dynamics.com` | Static |
| `Cors__AllowedOrigins__1` | `https://spaarke-{env}.api.crm.dynamics.com` | Static |
| `Cors__AllowedOrigins__2-4` | Same as `__0` (override base config) | Static |

---

## Appendix C: Resource Inventory

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

### Per-Customer Resources

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
| `spaarke-bff-api-{env}` | BFF API identity (Graph, SPE, Dataverse) |
| `spaarke-dataverse-s2s-{env}` | Server-to-server Dataverse access |
| `spaarke-ui-{env}` | UI MSAL (public SPA client) |

---

## Appendix D: Script Reference

All scripts are in the `scripts/` directory:

| Script | Purpose | Key Parameters |
|--------|---------|----------------|
| `Deploy-Platform.ps1` | Deploy shared platform Bicep | `-EnvironmentName`, `-WhatIf` |
| `Deploy-BffApi.ps1` | Deploy BFF API with zero-downtime | `-Environment`, `-UseSlotDeploy`, `-SkipBuild` |
| `Deploy-DataverseSolutions.ps1` | Import 10 managed solutions in order | `-EnvironmentUrl`, `-TenantId`, `-ClientId`, `-ClientSecret`, `-SolutionPath` |
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
| `Validate-DeployedEnvironment.ps1` | Post-deployment validation | `-DataverseUrl`, `-BffApiUrl` (optional) |
| `Rotate-Secrets.ps1` | Zero-downtime secret rotation | `-Scope`, `-SecretType`, `-CustomerId`, `-DryRun` |
| `Create-ContainerType-PowerShell.ps1` | Create SPE container type (SPO Mgmt Shell) | `-ContainerTypeName`, `-OwningAppId`, `-SharePointDomain` |
| `Create-NewContainerType.ps1` | Create + register SPE container type (Graph API) | `-OwningAppId`, `-OwningAppSecret`, `-TenantId`, `-SharePointDomain` |
| `Register-BffApiWithContainerType.ps1` | Register BFF API as owning app | `-ContainerTypeId`, `-OwningAppId`, `-OwningAppSecret`, `-TenantId`, `-SharePointDomain` |
| `Check-ContainerType-Registration.ps1` | Verify SPE registration | `-ContainerTypeId`, `-SharePointDomain`, `-OwningAppId` |
| `Test-SharePointToken.ps1` | Verify SPE token acquisition | `-ClientId`, `-ClientSecret`, `-TenantId`, `-ContainerTypeId`, `-SharePointDomain` |
| `New-BusinessUnitContainer.ps1` | Create SPE container for additional BU | `-BusinessUnitId`, `-BusinessUnitName`, `-ContainerTypeId`, `-DataverseUrl` |
| `Audit-DataverseComponents.ps1` | Verify solution completeness before export | `-EnvironmentUrl` |

---

*Consolidated from `ENVIRONMENT-DEPLOYMENT-GUIDE.md` (v1.1) and `PRODUCTION-DEPLOYMENT-GUIDE.md` (March 2026). All lessons learned preserved from actual deployment execution across dev, demo, and production environments.*
