# Production Deployment Guide

> **Last Updated**: March 2026
> **Audience**: Operators deploying Spaarke to production environments
> **Estimated Time**: 2-4 hours (first deployment), 30-60 minutes (subsequent)

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Architecture Overview](#2-architecture-overview)
3. [Phase 1: Deploy Shared Platform](#3-phase-1-deploy-shared-platform)
4. [Phase 2: Configure Entra ID App Registrations](#4-phase-2-configure-entra-id-app-registrations)
5. [Phase 3: Deploy BFF API](#5-phase-3-deploy-bff-api)
6. [Phase 4: Configure Custom Domain and SSL](#6-phase-4-configure-custom-domain-and-ssl)
7. [Phase 5: Provision First Customer](#7-phase-5-provision-first-customer)
8. [Phase 6: Verify Deployment](#8-phase-6-verify-deployment)
9. [Rollback Procedures](#9-rollback-procedures)
10. [Troubleshooting](#10-troubleshooting)
11. [Maintenance Operations](#11-maintenance-operations)
12. [Reference: Resource Inventory](#12-reference-resource-inventory)

---

## 1. Prerequisites

### Required Tools

| Tool | Minimum Version | Install Command |
|------|----------------|-----------------|
| Azure CLI | 2.60+ | `winget install Microsoft.AzureCLI` |
| PowerShell | 7.4+ | `winget install Microsoft.PowerShell` |
| .NET SDK | 8.0+ | `winget install Microsoft.DotNet.SDK.8` |
| PAC CLI | Latest | `dotnet tool install --global Microsoft.PowerApps.CLI.Tool` |
| Git | 2.40+ | `winget install Git.Git` |

### Required Access

| Resource | Access Level | How to Verify |
|----------|-------------|---------------|
| Azure Subscription | Contributor + User Access Admin | `az account show` |
| Entra ID Tenant | Application Administrator | Azure Portal > Entra ID > Roles |
| Power Platform | Global Admin or Dynamics 365 Admin | Power Platform Admin Center |
| GitHub Repository | Write access | `gh auth status` |
| DNS Management | Record creation for `api.spaarke.com` | Varies by provider |

### Required Azure Quotas

Verify quotas in the target region before starting:

| Resource | Required | Check Command |
|----------|----------|---------------|
| App Service Plan (P1v3) | 1 | `az appservice plan list` |
| Azure OpenAI (GPT-4o) | 80K TPM | Azure Portal > OpenAI > Quotas |
| AI Search (Standard2) | 1 service, 2 replicas | Azure Portal > AI Search |
| Document Intelligence (S0) | 1 | Azure Portal > Cognitive Services |

### Authentication Setup

Log in to all required services before starting:

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

Spaarke uses a hybrid architecture with shared platform resources and per-customer isolated resources.

```
Shared Platform (rg-spaarke-platform-{env})
  ├── App Service Plan (P1v3)
  │   └── App Service: spaarke-bff-{env}
  │       └── Staging Slot (zero-downtime deploy)
  ├── Azure OpenAI (GPT-4o, GPT-4o-mini, text-embedding-3-large)
  ├── AI Search (Standard2, 2 replicas)
  ├── Document Intelligence (S0)
  ├── App Insights + Log Analytics
  └── Platform Key Vault (sprk-platform-{env}-kv)

Per-Customer (rg-spaarke-{customerId}-{env})
  ├── Storage Account (SPE containers)
  ├── Customer Key Vault (sprk-{customerId}-{env}-kv)
  ├── Service Bus Namespace
  ├── Redis Cache
  └── Dataverse Environment (with managed solutions)
```

### Naming Convention

All resources follow the adopted naming standard:

| Pattern | Example |
|---------|---------|
| `spaarke-{purpose}-{env}` | `spaarke-bff-prod` |
| `sprk-{purpose}-{env}-kv` | `sprk-platform-prod-kv` |
| `rg-spaarke-{scope}-{env}` | `rg-spaarke-platform-prod` |
| `sprk-{customer}-{env}-kv` | `sprk-demo-prod-kv` |
| `sprk{customer}{env}sa` | `sprkdemoprodsa` |

See `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` for the full naming matrix.

---

## 3. Phase 1: Deploy Shared Platform

The shared platform provides infrastructure used by all customers. Deploy this first.

### 3.1 Review Parameter File

Before deploying, review the Bicep parameter file for your environment:

```powershell
# File: infrastructure/bicep/platform-prod.bicepparam
# Verify: environment name, region, SKUs, model deployments
```

Key parameters to verify:
- `environmentName`: Must be `prod` for production
- `location`: Region for all resources (default: `westus2`)
- App Service Plan SKU: `P1v3` for production
- AI Search: `standard2` with 2 replicas
- OpenAI model deployments: GPT-4o, GPT-4o-mini, text-embedding-3-large

### 3.2 Preview Deployment (What-If)

Always run a what-if preview before deploying:

```powershell
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod -WhatIf
```

Review the output. Verify:
- Resource group `rg-spaarke-platform-prod` will be created
- All expected resources are listed (App Service, OpenAI, AI Search, Doc Intel, Key Vault, App Insights)
- No unexpected deletions

### 3.3 Deploy Platform

```powershell
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod
```

The script performs:
1. Pre-flight checks (Azure CLI logged in, Bicep template exists, parameter file exists)
2. Subscription-scoped Bicep deployment (10-20 minutes)
3. Validates deployment outputs (resource group, API URL, Key Vault, AI endpoints)
4. Checks resource health (App Service, Key Vault, OpenAI, AI Search)

**Expected duration**: 10-20 minutes for a full deployment.

### 3.4 Verify Platform Deployment

After the script completes, verify manually:

```powershell
# Resource group exists
az group show --name rg-spaarke-platform-prod --query "{name:name, location:location}" -o table

# Key Vault accessible
az keyvault show --name sprk-platform-prod-kv --query "{name:name, location:location}" -o table

# App Service running
az webapp show --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --query "{state:state, defaultHostName:defaultHostName}" -o table

# OpenAI provisioned
az cognitiveservices account show --name spaarke-openai-prod --resource-group rg-spaarke-platform-prod --query "{name:name, provisioningState:properties.provisioningState}" -o table
```

---

## 4. Phase 2: Configure Entra ID App Registrations

Create app registrations for the BFF API to authenticate with Microsoft Graph, SharePoint Embedded, and Dataverse.

### 4.1 Create BFF API App Registration

In Azure Portal > Entra ID > App registrations:

1. **New registration**:
   - Name: `spaarke-bff-api-prod`
   - Supported account types: Single tenant
   - Redirect URI: (none needed for API)

2. **API permissions** (add and grant admin consent):
   - Microsoft Graph:
     - `Files.Read.All` (Application)
     - `FileStorageContainer.Selected` (Application)
     - `Sites.Read.All` (Application)
   - Dynamics CRM:
     - `user_impersonation` (Delegated)

3. **Certificates & secrets**:
   - Create a client secret (recommended: 24-month expiry)
   - Record the secret value immediately (shown only once)

4. **Expose an API**:
   - Application ID URI: `api://{client-id}`
   - Add scope: `user_impersonation` (Admins and users)

5. **Store credentials in Key Vault**:

```powershell
# Store the client secret
az keyvault secret set --vault-name sprk-platform-prod-kv --name "AzureAd--ClientSecret" --value "<client-secret>"

# Store the client ID (for reference)
az keyvault secret set --vault-name sprk-platform-prod-kv --name "AzureAd--ClientId" --value "<client-id>"

# Store the tenant ID
az keyvault secret set --vault-name sprk-platform-prod-kv --name "AzureAd--TenantId" --value "<tenant-id>"
```

### 4.2 Create Dataverse S2S App Registration

For server-to-server Dataverse communication:

1. **New registration**:
   - Name: `spaarke-dataverse-s2s-prod`
   - Supported account types: Single tenant

2. **API permissions**:
   - Dynamics CRM:
     - `user_impersonation` (Delegated)

3. **Register as Dataverse application user**:
   - In Power Platform Admin Center > Environments > (your env) > Settings > Users
   - Add Application User with the app's client ID
   - Assign System Administrator security role

4. **Store credentials in Key Vault**:

```powershell
az keyvault secret set --vault-name sprk-platform-prod-kv --name "Dataverse--ClientId" --value "<client-id>"
az keyvault secret set --vault-name sprk-platform-prod-kv --name "Dataverse--ClientSecret" --value "<client-secret>"
```

### 4.3 Grant App Service Managed Identity Access

The App Service uses a system-assigned managed identity to read Key Vault secrets:

```powershell
# Get the App Service managed identity principal ID
$principalId = az webapp identity show --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --query principalId -o tsv

# Grant Key Vault Secrets User role
az role assignment create --role "Key Vault Secrets User" --assignee-object-id $principalId --scope "/subscriptions/<sub-id>/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.KeyVault/vaults/sprk-platform-prod-kv"

# Also grant to staging slot (different managed identity)
$stagingPrincipalId = az webapp identity show --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --slot staging --query principalId -o tsv

az role assignment create --role "Key Vault Secrets User" --assignee-object-id $stagingPrincipalId --scope "/subscriptions/<sub-id>/resourceGroups/rg-spaarke-platform-prod/providers/Microsoft.KeyVault/vaults/sprk-platform-prod-kv"
```

---

## 5. Phase 3: Deploy BFF API

### 5.1 Configure App Settings

App settings use Key Vault references so no secrets appear in plaintext. Ensure these are configured in the App Service:

| Setting | Value Pattern |
|---------|--------------|
| `AzureAd__TenantId` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureAd--TenantId)` |
| `AzureAd__ClientId` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureAd--ClientId)` |
| `AzureAd__ClientSecret` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureAd--ClientSecret)` |
| `AzureOpenAI__Endpoint` | `https://spaarke-openai-prod.openai.azure.com/` |
| `AzureOpenAI__ApiKey` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AzureOpenAI--ApiKey)` |
| `AiSearch__Endpoint` | `https://spaarke-search-prod.search.windows.net/` |
| `AiSearch__ApiKey` | `@Microsoft.KeyVault(VaultName=sprk-platform-prod-kv;SecretName=AiSearch--ApiKey)` |
| `DocumentIntelligence__Endpoint` | Cognitive Services endpoint |
| `DocumentIntelligence__ApiKey` | `@Microsoft.KeyVault(...)` |

For the full list, see `src/server/api/Sprk.Bff.Api/appsettings.Production.json` for all configuration keys (non-secret values are in the JSON; secret values are injected via app settings with Key Vault references).

### 5.2 Build and Deploy

**Production deployment** uses staging slot with zero-downtime swap:

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy
```

The script performs:
1. Builds the API (`dotnet publish -c Release`)
2. Creates a zip package (~65 MB)
3. Deploys to the `staging` slot
4. Verifies health check on staging (`/healthz`)
5. Swaps staging to production (zero-downtime)
6. Verifies health check on production
7. Rolls back automatically if production health check fails

**Expected duration**: 3-5 minutes.

### 5.3 Skip Build (Redeploy Existing Package)

If you need to redeploy without rebuilding:

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy `
    -SkipBuild
```

### 5.4 Verify BFF API

```powershell
# Health check
curl https://spaarke-bff-prod.azurewebsites.net/healthz

# If custom domain is configured:
curl https://api.spaarke.com/healthz

# Detailed health (includes Dataverse, AI services):
curl https://api.spaarke.com/healthz/dataverse
```

Expected response: `Healthy` (HTTP 200).

---

## 6. Phase 4: Configure Custom Domain and SSL

### 6.1 Create DNS Record

In your DNS provider, create the record:

| Type | Name | Value | TTL |
|------|------|-------|-----|
| CNAME | `api.spaarke.com` | `spaarke-bff-prod.azurewebsites.net` | 3600 |

Alternatively, for apex domains, use an A record pointing to the App Service IP:

```powershell
# Get the App Service IP
az webapp show --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --query "inboundIpAddress" -o tsv
```

### 6.2 Add Custom Domain to App Service

```powershell
az webapp config hostname add --resource-group rg-spaarke-platform-prod --webapp-name spaarke-bff-prod --hostname api.spaarke.com
```

### 6.3 Configure SSL Certificate

Use Azure-managed free certificate (auto-renewing):

```powershell
# Create managed certificate
az webapp config ssl create --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --hostname api.spaarke.com

# Bind the certificate (get the thumbprint from the previous command output)
az webapp config ssl bind --resource-group rg-spaarke-platform-prod --name spaarke-bff-prod --certificate-thumbprint "<thumbprint>" --ssl-type SNI
```

### 6.4 Verify Custom Domain

```powershell
# Should return Healthy over HTTPS
curl https://api.spaarke.com/healthz

# Verify SSL certificate
openssl s_client -connect api.spaarke.com:443 -servername api.spaarke.com < /dev/null 2>/dev/null | openssl x509 -noout -subject -dates
```

---

## 7. Phase 5: Provision First Customer

The demo customer validates the entire provisioning pipeline. It uses the same scripts as real customers (FR-06).

### 7.1 Provision Demo Customer

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<service-principal-client-id>" `
    -ClientSecret "<service-principal-secret>"
```

The script executes these 10 steps:
1. Validates inputs and prerequisites (Azure CLI, PAC CLI, Bicep template)
2. Creates resource group `rg-spaarke-demo-prod`
3. Deploys `customer.bicep` (Storage, Key Vault, Service Bus, Redis)
4. Populates customer Key Vault with secrets
5. Creates Dataverse environment via Power Platform Admin API
6. Waits for Dataverse environment provisioning (5-15 minutes)
7. Imports managed solutions via `Deploy-DataverseSolutions.ps1`
8. Provisions SPE containers
9. Registers customer in BFF API tenant registry
10. Runs smoke tests via `Test-Deployment.ps1`

**Expected duration**: 20-30 minutes (Dataverse provisioning is the longest step).

### 7.2 Resume from a Specific Step

If provisioning fails partway through, resume from the last successful step:

```powershell
# Example: resume from step 5 (Dataverse creation)
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>" `
    -ResumeFromStep 5
```

### 7.3 Skip Dataverse (If Already Created)

If the Dataverse environment was created manually:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>" `
    -SkipDataverse
```

### 7.4 Preview Without Executing

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "demo" `
    -DisplayName "Spaarke Demo" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>" `
    -WhatIf
```

---

## 8. Phase 6: Verify Deployment

### 8.1 Run Smoke Tests

```powershell
.\scripts\Test-Deployment.ps1 -EnvironmentName prod
```

The smoke test suite validates:
- BFF API health (`/healthz` returns 200)
- Dataverse connectivity
- SPE container operations
- AI services (OpenAI, AI Search, Document Intelligence)
- Service Bus connectivity
- Redis connectivity

### 8.2 Manual Verification Checklist

| Check | Command | Expected Result |
|-------|---------|-----------------|
| API health | `curl https://api.spaarke.com/healthz` | `Healthy` (200) |
| Dataverse health | `curl https://api.spaarke.com/healthz/dataverse` | `healthy` (200) |
| SSL certificate | `curl -vI https://api.spaarke.com 2>&1 \| grep "subject"` | Valid cert for `api.spaarke.com` |
| App Service running | `az webapp show -g rg-spaarke-platform-prod -n spaarke-bff-prod --query state` | `Running` |
| Key Vault accessible | `az keyvault secret list --vault-name sprk-platform-prod-kv --query "[].name"` | List of secrets |
| Resource group (platform) | `az group show -n rg-spaarke-platform-prod` | Exists |
| Resource group (customer) | `az group show -n rg-spaarke-demo-prod` | Exists |

### 8.3 Verify Dataverse Solutions

```powershell
# Authenticate PAC CLI
pac auth create --environment "https://<demo-org>.crm.dynamics.com"

# List installed solutions
pac solution list
```

Verify all 10 Spaarke managed solutions are present and in a healthy state.

---

## 9. Rollback Procedures

### 9.1 BFF API Rollback

The `Deploy-BffApi.ps1` script includes automatic rollback when `-UseSlotDeploy` is used. If a swap has already completed and issues are found later:

```powershell
# Swap back to previous version (previous version is in the staging slot after a swap)
az webapp deployment slot swap `
    --resource-group rg-spaarke-platform-prod `
    --name spaarke-bff-prod `
    --slot staging `
    --target-slot production
```

Verify after rollback:

```powershell
curl https://api.spaarke.com/healthz
```

### 9.2 Platform Infrastructure Rollback

Bicep deployments are idempotent. To rollback:

1. Check the Git log for the previous working Bicep version
2. Check out that version
3. Redeploy:

```powershell
git checkout <previous-commit> -- infrastructure/bicep/
.\scripts\Deploy-Platform.ps1 -EnvironmentName prod
```

### 9.3 Dataverse Solution Rollback

Managed solutions support version rollback in the Power Platform Admin Center:

1. Navigate to the Dataverse environment > Solutions
2. Select the solution to rollback
3. Use "Solution history" to find the previous version
4. Import the previous managed solution version

---

## 10. Troubleshooting

### Common Issues

#### App Service returns 503 after deployment

**Symptoms**: `/healthz` returns 503 or times out after deployment.

**Causes and fixes**:
1. **Key Vault references not resolving**: Verify managed identity has `Key Vault Secrets User` role
   ```powershell
   az role assignment list --assignee <principal-id> --scope <keyvault-resource-id> -o table
   ```
2. **Missing app settings**: Compare required settings against configured settings
   ```powershell
   az webapp config appsettings list -g rg-spaarke-platform-prod -n spaarke-bff-prod --query "[].name" -o tsv | sort
   ```
3. **App not started**: Restart the App Service
   ```powershell
   az webapp restart -g rg-spaarke-platform-prod -n spaarke-bff-prod
   ```

#### Staging slot health check fails

**Symptoms**: Deployment to staging succeeds but health check fails.

**Cause**: Staging slot uses a different managed identity than production. It needs its own Key Vault access.

**Fix**:
```powershell
$stagingPrincipalId = az webapp identity show -g rg-spaarke-platform-prod -n spaarke-bff-prod --slot staging --query principalId -o tsv

az role assignment create --role "Key Vault Secrets User" --assignee-object-id $stagingPrincipalId --scope "<keyvault-resource-id>"
```

#### Bicep deployment fails with quota error

**Symptoms**: `Deploy-Platform.ps1` fails with "QuotaExceeded" or "SkuNotAvailable".

**Fix**: Request quota increases in the Azure Portal under Subscription > Usage + quotas. Common quotas to check:
- App Service Plan cores
- Azure OpenAI TPM (tokens per minute)
- AI Search service units

#### Dataverse environment creation times out

**Symptoms**: `Provision-Customer.ps1` hangs at step 5/6 (Dataverse provisioning).

**Cause**: Power Platform Admin API environment creation can take 5-15 minutes.

**Fix**:
1. Check status in Power Platform Admin Center
2. If the environment was created but the script timed out, resume from step 7:
   ```powershell
   .\scripts\Provision-Customer.ps1 -CustomerId "demo" ... -ResumeFromStep 7
   ```

#### Dataverse solution import fails

**Symptoms**: `Deploy-DataverseSolutions.ps1` fails with import errors.

**Common causes**:
1. **Missing dependency**: Solutions must import in order (SpaarkeCore first). Re-run the script, which handles order.
2. **Version conflict**: Unmanaged customizations conflict with managed solution. In the target environment, remove conflicting unmanaged components.
3. **Missing prerequisite solution**: Ensure all upstream solutions were imported successfully.

#### Redis connection refused

**Symptoms**: BFF API health check fails with Redis connection error.

**Fix**: If Redis is not yet provisioned, disable it temporarily:
```powershell
az webapp config appsettings set -g rg-spaarke-platform-prod -n spaarke-bff-prod --settings Redis__Enabled=false
```

Enable it once Redis is provisioned and the connection string is in Key Vault.

#### Custom domain SSL binding fails

**Symptoms**: `az webapp config ssl create` fails.

**Cause**: DNS record not yet propagated, or hostname not validated.

**Fix**:
1. Verify DNS is resolving: `nslookup api.spaarke.com`
2. Check hostname binding: `az webapp config hostname list -g rg-spaarke-platform-prod -n spaarke-bff-prod`
3. Wait for DNS propagation (up to 24 hours for new records, typically 5-30 minutes)

---

## 11. Maintenance Operations

### Secret Rotation

Use the dedicated rotation script:

```powershell
.\scripts\Rotate-Secrets.ps1 -EnvironmentName prod
```

See `docs/guides/SECRET-ROTATION-PROCEDURES.md` for the full secret rotation runbook.

### Customer Decommissioning

To cleanly remove a customer and all their resources:

```powershell
.\scripts\Decommission-Customer.ps1 `
    -CustomerId "<customer-id>" `
    -EnvironmentName prod `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

This removes:
- Azure resource group (`rg-spaarke-{customerId}-prod`)
- Dataverse environment
- SPE containers
- Tenant registry entry in BFF API

### Subsequent BFF API Deployments

After the initial setup, deploying new API versions requires only:

```powershell
.\scripts\Deploy-BffApi.ps1 `
    -Environment production `
    -ResourceGroupName "rg-spaarke-platform-prod" `
    -AppServiceName "spaarke-bff-prod" `
    -UseSlotDeploy
```

### Viewing Logs

```powershell
# Live log stream
az webapp log tail -g rg-spaarke-platform-prod -n spaarke-bff-prod

# Staging slot logs
az webapp log tail -g rg-spaarke-platform-prod -n spaarke-bff-prod --slot staging

# App Insights queries (via Azure Portal)
# Navigate to: App Insights > Logs
# KQL: requests | where timestamp > ago(1h) | summarize count() by resultCode
```

### Provisioning Additional Customers

Onboard new customers using the same script:

```powershell
.\scripts\Provision-Customer.ps1 `
    -CustomerId "acme" `
    -DisplayName "Acme Legal" `
    -TenantId "<tenant-id>" `
    -ClientId "<client-id>" `
    -ClientSecret "<secret>"
```

See `docs/guides/CUSTOMER-DEPLOYMENT-GUIDE.md` for the complete customer onboarding runbook.

---

## 12. Reference: Resource Inventory

### Production Platform Resources

| Resource | Name | Resource Group |
|----------|------|----------------|
| App Service Plan | `spaarke-bff-prod-plan` | `rg-spaarke-platform-prod` |
| App Service | `spaarke-bff-prod` | `rg-spaarke-platform-prod` |
| App Service (staging slot) | `spaarke-bff-prod/staging` | `rg-spaarke-platform-prod` |
| Azure OpenAI | `spaarke-openai-prod` | `rg-spaarke-platform-prod` |
| AI Search | `spaarke-search-prod` | `rg-spaarke-platform-prod` |
| Document Intelligence | `spaarke-docintel-prod` | `rg-spaarke-platform-prod` |
| Key Vault | `sprk-platform-prod-kv` | `rg-spaarke-platform-prod` |
| App Insights | `spaarke-appinsights-prod` | `rg-spaarke-platform-prod` |
| Log Analytics | `spaarke-logs-prod` | `rg-spaarke-platform-prod` |

### Per-Customer Resources (Template)

| Resource | Name Pattern | Resource Group |
|----------|-------------|----------------|
| Storage Account | `sprk{customerId}{env}sa` | `rg-spaarke-{customerId}-{env}` |
| Key Vault | `sprk-{customerId}-{env}-kv` | `rg-spaarke-{customerId}-{env}` |
| Service Bus | `sprk-{customerId}-{env}-sb` | `rg-spaarke-{customerId}-{env}` |
| Redis Cache | `sprk-{customerId}-{env}-redis` | `rg-spaarke-{customerId}-{env}` |
| Dataverse Env | `spaarke-{customerId}` | (Power Platform) |

### Key Endpoints

| Service | URL |
|---------|-----|
| BFF API (production) | `https://api.spaarke.com` |
| BFF API (direct) | `https://spaarke-bff-prod.azurewebsites.net` |
| Health check | `https://api.spaarke.com/healthz` |
| OpenAI endpoint | `https://spaarke-openai-prod.openai.azure.com/` |
| AI Search endpoint | `https://spaarke-search-prod.search.windows.net/` |

### Entra ID App Registrations

| App Name | Purpose |
|----------|---------|
| `spaarke-bff-api-prod` | BFF API identity (Graph, SPE, Dataverse) |
| `spaarke-dataverse-s2s-prod` | Server-to-server Dataverse access |

---

## Quick Reference: Deployment Sequence

For a complete fresh deployment, execute in this order:

```
1. Deploy-Platform.ps1 -EnvironmentName prod -WhatIf    (preview)
2. Deploy-Platform.ps1 -EnvironmentName prod             (deploy shared infra)
3. Create Entra ID app registrations                      (manual in portal)
4. Store secrets in Key Vault                             (az keyvault secret set)
5. Grant managed identity Key Vault access                (az role assignment create)
6. Configure App Service app settings                     (Key Vault references)
7. Deploy-BffApi.ps1 -Environment production -UseSlotDeploy  (deploy API)
8. Configure custom domain + SSL                          (DNS + az webapp config)
9. Provision-Customer.ps1 -CustomerId demo                (first customer)
10. Test-Deployment.ps1 -EnvironmentName prod             (validate everything)
```

---

*This guide was written based on the production deployment completed in March 2026. All scripts referenced are in the `scripts/` directory of the Spaarke repository.*
