# Spaarke Environment Deployment Guide

> **Version**: 1.0
> **Last Updated**: 2026-03-25
> **Status**: Production Ready (validated during demo environment deployment)
> **Applies To**: Deploying Spaarke to any new Dataverse + Azure environment

---

## Overview

This guide covers the complete process of deploying the Spaarke platform to a new environment. It was validated during the demo environment deployment (March 2026) and captures all lessons learned, including undocumented requirements and workarounds.

**Estimated Duration**: 4-6 hours (first time), 2-3 hours (subsequent environments)

**Components Deployed**:
- Azure infrastructure (11 resources)
- Entra ID app registrations (2 apps)
- Key Vault secrets (14+ secrets)
- Dataverse solutions (SpaarkeCore + SpaarkeFeatures)
- SharePoint Embedded container type + containers
- BFF API code deployment
- Dataverse environment variables (7 variables)

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Azure Subscription Setup](#2-azure-subscription-setup)
3. [Azure Resource Creation](#3-azure-resource-creation)
4. [Entra ID App Registrations](#4-entra-id-app-registrations)
5. [Key Vault Secret Population](#5-key-vault-secret-population)
6. [Dataverse Solution Export and Fix Pipeline](#6-dataverse-solution-export-and-fix-pipeline)
7. [Dataverse Solution Import](#7-dataverse-solution-import)
8. [Dataverse Environment Variables](#8-dataverse-environment-variables)
9. [SharePoint Embedded Setup](#9-sharepoint-embedded-setup)
10. [BFF API Deployment](#10-bff-api-deployment)
11. [Dataverse Application User](#11-dataverse-application-user)
12. [Validation](#12-validation)
13. [Known Issues and Workarounds](#13-known-issues-and-workarounds)
14. [Appendix: Complete App Settings Reference](#14-appendix-complete-app-settings-reference)

---

## 1. Prerequisites

### Required Tools

| Tool | Version | Purpose |
|------|---------|---------|
| Azure CLI (`az`) | 2.55+ | Azure resource management |
| PAC CLI (`pac`) | 1.46+ | Dataverse solution management |
| PowerShell 7+ | 7.0+ | Script execution |
| .NET SDK | 8.0+ | BFF API build |
| SharePoint Online Management Shell | Latest | SPE container type creation |

### Required Access

| Permission | Scope | Purpose |
|-----------|-------|---------|
| Azure Contributor | Target subscription | Create resources |
| Entra ID Application Administrator | Tenant | Create app registrations |
| Power Platform System Administrator | Target Dataverse environment | Import solutions |
| SharePoint Administrator | Tenant | Create SPE container types |

### Information to Collect

| Item | Example | Where to Find |
|------|---------|---------------|
| Target subscription ID | `2ff9ee48-...` | Azure Portal > Subscriptions |
| Target Dataverse URL | `https://spaarke-demo.crm.dynamics.com` | Power Platform Admin Center |
| Tenant ID | `a221a95e-...` | Entra ID > Overview |
| Environment name | `demo` | Your naming convention |

---

## 2. Azure Subscription Setup

### Create Subscription

1. Azure Portal > **Cost Management + Billing** > **Billing scopes** > select corporate billing account
2. Create subscription with name following convention: `Spaarke {Environment} Environment`
3. Add tags: `environment={env}`, `managedBy=spaarke-ops`

### Register Resource Providers

New subscriptions need resource providers registered before resources can be created:

```bash
az account set --subscription "{subscription-id}"

for provider in Microsoft.KeyVault Microsoft.Storage Microsoft.ServiceBus \
  Microsoft.Cache Microsoft.CognitiveServices Microsoft.Search \
  Microsoft.Web Microsoft.Insights Microsoft.OperationalInsights; do
  az provider register --namespace "$provider"
done

# Wait for registration (can take 2-5 minutes)
# Verify with:
az provider show --namespace Microsoft.KeyVault --query "registrationState"
```

### Register Syntex Provider (for SPE)

```bash
az provider register --namespace Microsoft.Syntex
# This can take several minutes — verify before SPE billing setup
az provider show --namespace Microsoft.Syntex --query "registrationState"
```

---

## 3. Azure Resource Creation

All resources follow the [naming convention v3](../architecture/AZURE-RESOURCE-NAMING-CONVENTION.md). Each environment gets one resource group with all resources.

### Resource Group

```bash
az group create --name rg-spaarke-{env} --location westus2 \
  --tags environment={env} managedBy=bicep application=spaarke
```

### All Resources

Create in this order (some depend on others):

```bash
ENV=demo  # Change for each environment

# 1. Log Analytics + App Insights
az monitor log-analytics workspace create \
  --resource-group rg-spaarke-$ENV --workspace-name spaarke-$ENV-logs \
  --location westus2 --retention-time 90 \
  --tags environment=$ENV component=monitoring

WORKSPACE_ID=$(az monitor log-analytics workspace show \
  --resource-group rg-spaarke-$ENV --workspace-name spaarke-$ENV-logs \
  --query id --output tsv)

MSYS_NO_PATHCONV=1 az monitor app-insights component create \
  --app spaarke-$ENV-insights --location westus2 \
  --resource-group rg-spaarke-$ENV --workspace "$WORKSPACE_ID" \
  --tags environment=$ENV component=monitoring

# 2. Key Vault (RBAC-enabled)
az keyvault create --name sprk-$ENV-kv --resource-group rg-spaarke-$ENV \
  --location westus2 --enable-rbac-authorization true \
  --tags environment=$ENV component=secrets

# 3. Storage Account
az storage account create --name sprk${ENV}sa --resource-group rg-spaarke-$ENV \
  --location westus2 --sku Standard_LRS --kind StorageV2 \
  --tags environment=$ENV component=storage

# 4. Service Bus
az servicebus namespace create --name spaarke-$ENV-sbus \
  --resource-group rg-spaarke-$ENV --location westus2 --sku Standard \
  --tags environment=$ENV component=messaging

for queue in document-processing document-indexing ai-indexing sdap-communication sdap-jobs; do
  az servicebus queue create --namespace-name spaarke-$ENV-sbus \
    --resource-group rg-spaarke-$ENV --name "$queue" \
    --max-size 1024 --default-message-time-to-live P14D
done

# 5. Redis Cache (Basic for non-prod, Standard for prod)
az redis create --name spaarke-$ENV-cache --resource-group rg-spaarke-$ENV \
  --location westus2 --sku Basic --vm-size c0 \
  --tags environment=$ENV component=cache

# 6. Azure OpenAI (NOTE: westus3 for model availability)
az cognitiveservices account create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-$ENV --location westus3 \
  --kind OpenAI --sku S0 --custom-domain spaarke-openai-$ENV \
  --tags environment=$ENV component=ai

# Deploy models (adjust capacity to subscription quota)
az cognitiveservices account deployment create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-$ENV --deployment-name gpt-4o \
  --model-name gpt-4o --model-version "2024-08-06" \
  --model-format OpenAI --sku-name Standard --sku-capacity 50

az cognitiveservices account deployment create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-$ENV --deployment-name gpt-4o-mini \
  --model-name gpt-4o-mini --model-version "2024-07-18" \
  --model-format OpenAI --sku-name Standard --sku-capacity 50

az cognitiveservices account deployment create --name spaarke-openai-$ENV \
  --resource-group rg-spaarke-$ENV --deployment-name text-embedding-3-large \
  --model-name text-embedding-3-large --model-version "1" \
  --model-format OpenAI --sku-name Standard --sku-capacity 50

# 7. Document Intelligence
az cognitiveservices account create --name spaarke-docintel-$ENV \
  --resource-group rg-spaarke-$ENV --location westus2 \
  --kind FormRecognizer --sku S0 \
  --tags environment=$ENV component=ai

# 8. AI Search
az search service create --name spaarke-search-$ENV \
  --resource-group rg-spaarke-$ENV --location westus2 \
  --sku standard --replica-count 1 --partition-count 1 \
  --tags environment=$ENV component=ai

# 9. App Service Plan + BFF API
az appservice plan create --name spaarke-$ENV-plan \
  --resource-group rg-spaarke-$ENV --location westus2 \
  --sku B1 --is-linux \
  --tags environment=$ENV component=bff-api

MSYS_NO_PATHCONV=1 az webapp create --name spaarke-bff-$ENV \
  --resource-group rg-spaarke-$ENV --plan spaarke-$ENV-plan \
  --runtime "DOTNETCORE:8.0" \
  --tags environment=$ENV component=bff-api

# 10. Enable managed identity + grant Key Vault access
MSYS_NO_PATHCONV=1 az webapp identity assign \
  --name spaarke-bff-$ENV --resource-group rg-spaarke-$ENV

PRINCIPAL_ID=$(MSYS_NO_PATHCONV=1 az webapp identity show \
  --name spaarke-bff-$ENV --resource-group rg-spaarke-$ENV \
  --query principalId --output tsv)

MSYS_NO_PATHCONV=1 az role assignment create --assignee "$PRINCIPAL_ID" \
  --role "Key Vault Secrets User" \
  --scope "/subscriptions/{sub-id}/resourceGroups/rg-spaarke-$ENV/providers/Microsoft.KeyVault/vaults/sprk-$ENV-kv"

# 11. Increase Dataverse max upload size (for PCF control bundles)
# Run via PowerShell against the target Dataverse environment:
# $token = az account get-access-token --resource "{dataverse-url}" --query accessToken -o tsv
# Invoke-RestMethod -Uri "{dataverse-url}/api/data/v9.2/organizations({org-id})" \
#   -Method Patch -Body '{"maxuploadfilesize":33554432}' ...
```

---

## 4. Entra ID App Registrations

Each environment gets its own app registrations.

### BFF API App

```bash
# Create app
az ad app create --display-name "Spaarke BFF API - {Env}" --sign-in-audience AzureADMyOrg

# Note the appId from output, then:
BFF_APP_ID="{appId-from-output}"

# Create client secret
az ad app credential reset --id "$BFF_APP_ID" --append \
  --display-name "{Env} BFF API Secret" --years 2

# Create service principal
az ad sp create --id "$BFF_APP_ID"
BFF_SP_ID="{sp-id-from-output}"

# Add redirect URIs
MSYS_NO_PATHCONV=1 az ad app update --id "$BFF_APP_ID" \
  --web-redirect-uris "https://localhost" \
    "https://spaarke-bff-{env}.azurewebsites.net" \
    "https://spaarke-bff-{env}.azurewebsites.net/.auth/login/aad/callback"

# Expose user_impersonation scope
# (Use Graph API to set identifierUris and oauth2PermissionScopes)

# Grant Graph permissions
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

# Grant SharePoint permissions
SP_RESOURCE_ID=$(MSYS_NO_PATHCONV=1 az ad sp show --id "00000003-0000-0ff1-ce00-000000000000" --query id --output tsv)

for role_id in "19766c1b-905b-43af-8756-06526ab42875" "20d37865-089c-4dee-8c41-6967602d4ac8"; do
  MSYS_NO_PATHCONV=1 az rest --method POST \
    --uri "https://graph.microsoft.com/v1.0/servicePrincipals/$BFF_SP_ID/appRoleAssignments" \
    --body "{\"principalId\":\"$BFF_SP_ID\",\"resourceId\":\"$SP_RESOURCE_ID\",\"appRoleId\":\"$role_id\"}" \
    --output none
done
```

**Required Graph Permissions (Application)**:

| Permission | GUID | Purpose |
|-----------|------|---------|
| Sites.Read.All | `332a536c-c7ef-4017-ab91-336970924f0d` | SharePoint site access |
| User.Read.All | `df021288-bdef-4463-88db-98f22de89214` | User profile resolution |
| FileStorageContainer.Selected | `75359482-378d-4052-8f01-80520e7db3cd` | SPE container access |
| FileStorageContainer.ReadWrite.All | `4437522e-9a86-4a41-a7da-e380edd4a97d` | SPE container creation |

**Required SharePoint Permissions (Application)**:

| Permission | GUID | Purpose |
|-----------|------|---------|
| Container.Selected | `19766c1b-905b-43af-8756-06526ab42875` | SPE container type operations |
| Sites.ReadWrite.All | `20d37865-089c-4dee-8c41-6967602d4ac8` | SPE registration |

> **Note**: Permission GUIDs `19766c1b` and `20d37865` are SharePoint-specific and not visible in the Azure Portal API permissions picker. They must be granted via Graph API `appRoleAssignments`.

### UI SPA App (Public Client)

```bash
az ad app create --display-name "Spaarke UI - {Env}" --sign-in-audience AzureADMyOrg \
  --enable-access-token-issuance true --enable-id-token-issuance true

UI_APP_ID="{appId-from-output}"

# Add SPA redirect URIs via Graph API
MSYS_NO_PATHCONV=1 az rest --method PATCH \
  --uri "https://graph.microsoft.com/v1.0/applications/{object-id}" \
  --body "{\"spa\":{\"redirectUris\":[\"https://spaarke-{env}.crm.dynamics.com\",\"https://spaarke-{env}.api.crm.dynamics.com\",\"http://localhost\"]}}"
```

> **Important**: Also add the target Dataverse URL to the **legacy dev app** (`170c98e1`) SPA redirect URIs until all PCF controls are migrated to use `sprk_MsalClientId` env var.

---

## 5. Key Vault Secret Population

```bash
# Gather values
OPENAI_KEY=$(az cognitiveservices account keys list --name spaarke-openai-$ENV --resource-group rg-spaarke-$ENV --query key1 --output tsv)
DOCINTEL_KEY=$(az cognitiveservices account keys list --name spaarke-docintel-$ENV --resource-group rg-spaarke-$ENV --query key1 --output tsv)
SEARCH_KEY=$(az search admin-key show --service-name spaarke-search-$ENV --resource-group rg-spaarke-$ENV --query primaryKey --output tsv)
SBUS_CONN=$(az servicebus namespace authorization-rule keys list --namespace-name spaarke-$ENV-sbus --resource-group rg-spaarke-$ENV --name RootManageSharedAccessKey --query primaryConnectionString --output tsv)
INSIGHTS_CONN=$(az monitor app-insights component show --app spaarke-$ENV-insights --resource-group rg-spaarke-$ENV --query connectionString --output tsv)

# Store secrets
az keyvault secret set --vault-name sprk-$ENV-kv --name TenantId --value "{tenant-id}"
az keyvault secret set --vault-name sprk-$ENV-kv --name BFF-API-ClientId --value "{bff-app-id}"
az keyvault secret set --vault-name sprk-$ENV-kv --name BFF-API-ClientSecret --value "{bff-secret}"
az keyvault secret set --vault-name sprk-$ENV-kv --name BFF-API-Audience --value "api://{bff-app-id}"
az keyvault secret set --vault-name sprk-$ENV-kv --name Dataverse-ServiceUrl --value "https://spaarke-{env}.crm.dynamics.com"
az keyvault secret set --vault-name sprk-$ENV-kv --name ai-openai-endpoint --value "https://spaarke-openai-$ENV.openai.azure.com/"
az keyvault secret set --vault-name sprk-$ENV-kv --name ai-openai-key --value "$OPENAI_KEY"
az keyvault secret set --vault-name sprk-$ENV-kv --name ai-docintel-endpoint --value "https://spaarke-docintel-$ENV.cognitiveservices.azure.com/"
az keyvault secret set --vault-name sprk-$ENV-kv --name ai-docintel-key --value "$DOCINTEL_KEY"
az keyvault secret set --vault-name sprk-$ENV-kv --name ai-search-endpoint --value "https://spaarke-search-$ENV.search.windows.net"
az keyvault secret set --vault-name sprk-$ENV-kv --name ai-search-key --value "$SEARCH_KEY"
az keyvault secret set --vault-name sprk-$ENV-kv --name ServiceBus-ConnectionString --value "$SBUS_CONN"
az keyvault secret set --vault-name sprk-$ENV-kv --name AppInsights-ConnectionString --value "$INSIGHTS_CONN"
az keyvault secret set --vault-name sprk-$ENV-kv --name SPE-ContainerTypeId --value "{container-type-id}"  # Set after SPE setup
az keyvault secret set --vault-name sprk-$ENV-kv --name SPE-DefaultContainerId --value "{container-id}"    # Set after container creation
```

---

## 6. Dataverse Solution Export and Fix Pipeline

### Prerequisites

1. PAC CLI authenticated to the **source** environment (dev): `pac auth create --url https://spaarkedev1.crm.dynamics.com`
2. Run `scripts/Audit-DataverseComponents.ps1` to verify solution completeness

### Export

```bash
pac auth select --index {dev-profile-index}
pac solution export --name SpaarkeCore --path ./exports/SpaarkeCore.zip --overwrite
pac solution export --name SpaarkeFeatures --path ./exports/SpaarkeFeatures.zip --overwrite
```

### Fix Pipeline (REQUIRED before import)

The exported solution contains issues that must be fixed before importing to a new environment:

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

> **Why these fixes are needed**: See [Known Issues](#13-known-issues-and-workarounds) for full explanation of each issue.

---

## 7. Dataverse Solution Import

**Critical: Import order matters.** SpaarkeFeatures (web resources) MUST be imported BEFORE SpaarkeCore (entities reference web resources in forms and ribbons).

```bash
pac auth select --index {target-profile-index}

# Step 1: Import SpaarkeFeatures FIRST (web resources)
pac solution import --path ./exports/SpaarkeFeatures.zip --publish-changes --async

# Step 2: Import SpaarkeCore (entities, forms, security roles, etc.)
pac solution import --path ./exports/SpaarkeCore_fixed.zip --publish-changes --async
```

### Increase Max Upload Size

Before importing, increase the Dataverse max upload file size to 32MB (default 5MB is too small for PCF control bundles):

```powershell
$token = az account get-access-token --resource "{dataverse-url}" --query accessToken -o tsv
$headers = @{ Authorization = "Bearer $token"; "OData-Version" = "4.0"; "Content-Type" = "application/json"; "If-Match" = "*" }
$orgId = (Invoke-RestMethod -Uri "{dataverse-url}/api/data/v9.2/organizations?`$select=organizationid" -Headers @{Authorization="Bearer $token";"OData-Version"="4.0"}).value[0].organizationid
Invoke-RestMethod -Uri "{dataverse-url}/api/data/v9.2/organizations($orgId)" -Method Patch -Headers $headers -Body '{"maxuploadfilesize":33554432}'
```

---

## 8. Dataverse Environment Variables

Set all 7 required environment variables after solution import:

```bash
pac env variable set --name sprk_BffApiBaseUrl --value "https://spaarke-bff-{env}.azurewebsites.net/api"
pac env variable set --name sprk_BffApiAppId --value "api://{bff-app-id}"
pac env variable set --name sprk_MsalClientId --value "{ui-spa-app-id}"
pac env variable set --name sprk_TenantId --value "{tenant-id}"
pac env variable set --name sprk_AzureOpenAiEndpoint --value "https://spaarke-openai-{env}.openai.azure.com/"
pac env variable set --name sprk_ShareLinkBaseUrl --value "https://spaarke-bff-{env}.azurewebsites.net/share"
pac env variable set --name sprk_SharePointEmbeddedContainerId --value "{container-id}"  # Set after SPE container creation
```

---

## 9. SharePoint Embedded Setup

### Step 1: Create Container Type (SharePoint Admin Required)

**Must use SPO Management Shell** — the Graph API returns 403 for container type creation even with all permissions.

```powershell
# Install if needed (Windows PowerShell 5.1 — NOT PowerShell 7)
# Install-Module -Name Microsoft.Online.SharePoint.PowerShell -Force

Connect-SPOService -Url "https://{tenant}-admin.sharepoint.com"

New-SPOContainerType `
    -ContainerTypeName "Spaarke {Env} Documents" `
    -OwningApplicationId "{bff-app-id}"

# Note the ContainerTypeId from output
```

### Step 2: Add Billing (Standard Container Types)

```powershell
Add-SPOContainerTypeBilling `
    -ContainerTypeId "{container-type-id}" `
    -AzureSubscriptionId "{subscription-id}" `
    -ResourceGroup "rg-spaarke-{env}" `
    -Region "westus"    # NOTE: Must be Syntex-compatible region, NOT westus2
```

> **Important**: The `-Region` must be a [Syntex-supported region](#syntex-supported-regions). `westus2` is NOT supported — use `westus`, `eastus`, `westeurope`, etc.

> **Important**: `Microsoft.Syntex` must be registered on the subscription first. The `Add-SPOContainerTypeBilling` command triggers registration automatically but may fail if it's still registering. Wait 2-5 minutes and retry.

### Step 3: Register Container Type (Graph API)

```powershell
# Get Graph token for the BFF API app
$t = Invoke-RestMethod -Uri "https://login.microsoftonline.com/{tenant-id}/oauth2/v2.0/token" -Method Post -Body @{
    client_id="{bff-app-id}"; client_secret="{bff-secret}"; scope="https://graph.microsoft.com/.default"; grant_type="client_credentials"
}

# Register with full permissions
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

### Step 4: Create Root Container

> **Note**: Allow 10-30 minutes after registration for permissions to propagate into the Graph token. The `FileStorageContainer.Selected` role must appear in the token claims.

```powershell
$body = @{
    displayName = "{Env} Root Documents"
    containerTypeId = "{container-type-id}"
} | ConvertTo-Json

$container = Invoke-RestMethod -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containers" `
    -Method Post -Headers @{Authorization="Bearer $($t.access_token)";"Content-Type"="application/json"} `
    -Body ([System.Text.Encoding]::UTF8.GetBytes($body))

# Container ID: $container.id

# Activate the container
Invoke-RestMethod -Uri "https://graph.microsoft.com/beta/storage/fileStorage/containers/$($container.id)/activate" `
    -Method Post -Headers @{Authorization="Bearer $($t.access_token)";"Content-Type"="application/json"}
```

### Step 5: Store Container IDs

```bash
az keyvault secret set --vault-name sprk-{env}-kv --name SPE-ContainerTypeId --value "{container-type-id}"
az keyvault secret set --vault-name sprk-{env}-kv --name SPE-DefaultContainerId --value "{container-id}"
pac env variable set --name sprk_SharePointEmbeddedContainerId --value "{container-id}"
```

### Syntex Supported Regions

`westus`, `eastus`, `eastus2`, `centralus`, `southcentralus`, `northcentralus`, `westeurope`, `northeurope`, `uksouth`, `ukwest`, `australiaeast`, `japaneast`, `canadacentral`, `brazilsouth`, `southeastasia`, `centralindia`, `koreacentral`, `francecentral`, `switzerlandnorth`, `norwayeast`, `uaenorth`, `southafricanorth`

**NOT supported**: `westus2`, `westus3`

---

## 10. BFF API Deployment

### Configure App Settings

See [Appendix: Complete App Settings Reference](#14-appendix-complete-app-settings-reference) for the full list.

Key settings:

```bash
MSYS_NO_PATHCONV=1 az webapp config appsettings set \
  --name spaarke-bff-{env} --resource-group rg-spaarke-{env} \
  --settings \
    ASPNETCORE_ENVIRONMENT="Production" \
    TENANT_ID="@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=TenantId)" \
    API_APP_ID="@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=BFF-API-ClientId)" \
    API_CLIENT_SECRET="@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=BFF-API-ClientSecret)" \
    # ... (see appendix for full list)
    KeyVaultUri="https://sprk-{env}-kv.vault.azure.net/" \
    SpeAdmin__KeyVaultUri="https://sprk-{env}-kv.vault.azure.net/" \
    ServiceBus__QueueName="sdap-jobs" \
    Cors__AllowedOrigins__0="https://spaarke-{env}.crm.dynamics.com" \
    Cors__AllowedOrigins__1="https://spaarke-{env}.api.crm.dynamics.com" \
    Cors__AllowedOrigins__2="https://spaarke-{env}.crm.dynamics.com" \
    Cors__AllowedOrigins__3="https://spaarke-{env}.crm.dynamics.com" \
    Cors__AllowedOrigins__4="https://spaarke-{env}.crm.dynamics.com"
```

> **Important CORS note**: The base `appsettings.json` contains localhost origins. In Production mode, non-HTTPS origins cause a startup crash. Override indices 0-4 to replace ALL base origins with HTTPS-only values.

### Build and Deploy

```bash
dotnet publish src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -c Release -o ./publish/bff-api
pwsh -Command "Compress-Archive -Path 'publish/bff-api/*' -DestinationPath 'exports/bff-api.zip' -Force"
MSYS_NO_PATHCONV=1 az webapp deploy --resource-group rg-spaarke-{env} \
  --name spaarke-bff-{env} --src-path exports/bff-api.zip --type zip
```

---

## 11. Dataverse Application User

**Must be done via Power Platform Admin Center** (not CLI or API).

1. Go to https://admin.powerplatform.microsoft.com
2. **Environments** → **spaarke-{env}** → **Settings**
3. **Users + permissions** → **Application users**
4. **+ New app user**
5. Select the BFF API app (`Spaarke BFF API - {Env}`)
6. Business unit: Root
7. Security roles: **System Administrator**
8. Create

Without this, the BFF API crashes with: `The user is not a member of the organization`

---

## 12. Validation

```bash
# Health check
curl https://spaarke-bff-{env}.azurewebsites.net/healthz
# Expected: "Healthy" (HTTP 200)

# Run validation script
.\scripts\Validate-DeployedEnvironment.ps1 -DataverseUrl "https://spaarke-{env}.crm.dynamics.com" -BffApiUrl "https://spaarke-bff-{env}.azurewebsites.net"

# Verify solutions
pac solution list
# Expected: SpaarkeCore, SpaarkeFeatures
```

---

## 13. Known Issues and Workarounds

### Issue 1: PCF Manifest Mismatch (Form XML)

**Symptom**: Import fails with `Property tenantId is not declared in the control manifest`

**Cause**: R2 migration removed `tenantId`/`apiBaseUrl` from PCF control manifests but the form XML still has static values referencing these properties.

**Fix**: Remove stale static params from form XMLs in the unpacked solution (see Fix Pipeline step 2).

### Issue 2: SpeDocumentViewer Required Properties

**Symptom**: Import fails with `Property tenantId is required, but the declaration is missing`

**Cause**: SpeDocumentViewer manifest still has `tenantId` as `required="true"` with hardcoded dev URL.

**Fix**: Change to `required="false"` and remove dev URL default in the unpacked solution (see Fix Pipeline step 3).

### Issue 3: Empty Sitemaps

**Symptom**: Import fails with `SiteMap needs to have a non-empty Area with a non-empty Group`

**Cause**: DocumentManagement and LawFirmCaseManagement sitemaps have zero areas/groups.

**Fix**: Remove from unpacked solution (see Fix Pipeline step 4).

### Issue 4: Canvas App Dependencies

**Symptom**: Import fails with `Some dependencies are missing` referencing type 66 (CustomControl) for AnalysisBuilder, AnalysisWorkspace, PlaybookBuilderHost

**Cause**: Legacy canvas apps (custom pages) were pulled into SpaarkeCore by `--AddRequiredComponents`. These canvas apps depend on PCF controls that may not be in the solution.

**Fix**: Remove all canvas apps (type 300) and their dependency references from Solution.xml (see Fix Pipeline steps 5-7).

### Issue 5: Solution Import Order

**Rule**: SpaarkeFeatures MUST be imported BEFORE SpaarkeCore.

**Cause**: SpaarkeCore entities reference web resources (icons, JS files) in their forms and ribbons. These web resources are in SpaarkeFeatures.

### Issue 6: Dataverse Max Upload Size

**Symptom**: Import fails with `Webresource content size is too big`

**Cause**: Default 5MB limit is too small for PCF control bundles (VisualHost is ~10MB).

**Fix**: Increase `maxuploadfilesize` to 32MB via Dataverse API before import.

### Issue 7: CORS Localhost Origins

**Symptom**: BFF API crashes on startup with `CORS: Non-HTTPS origin 'http://127.0.0.1:3000' is not allowed in Production environment`

**Cause**: Base `appsettings.json` has localhost origins. App Service env var array settings **merge** with base config (don't replace). Production CORS validation rejects non-HTTPS origins.

**Fix**: Override CORS indices 0-4 via app settings to replace ALL base origins.

### Issue 8: SPE Container Type Creation

**Requirement**: Use `New-SPOContainerType` (SPO Management Shell), NOT the Graph API.

**Cause**: Graph API returns 403 for container type creation even with all permissions. Container type creation requires SharePoint Admin access.

### Issue 9: SPE Billing Region

**Requirement**: Use `westus` (not `westus2`) for the `-Region` parameter in `Add-SPOContainerTypeBilling`.

**Cause**: Microsoft.Syntex doesn't support `westus2`. See [Syntex Supported Regions](#syntex-supported-regions).

### Issue 10: Graph Token Propagation

**Symptom**: SPE container creation returns 403 immediately after granting permissions.

**Cause**: Newly granted app role assignments take 10-30 minutes to appear in the Graph client credentials token.

**Fix**: Wait for token cache to expire, or generate a new client secret to force a new token.

---

## 14. Appendix: Complete App Settings Reference

All settings required for the BFF API App Service. Values use Key Vault references (`@Microsoft.KeyVault(VaultName=sprk-{env}-kv;SecretName=...)`) for secrets.

| Setting | Value | Source |
|---------|-------|--------|
| `ASPNETCORE_ENVIRONMENT` | `Production` | Static |
| `TENANT_ID` | KV: `TenantId` | Key Vault |
| `API_APP_ID` | KV: `BFF-API-ClientId` | Key Vault |
| `API_CLIENT_SECRET` | KV: `BFF-API-ClientSecret` | Key Vault |
| `DEFAULT_CT_ID` | KV: `SPE-ContainerTypeId` | Key Vault |
| `KeyVaultUri` | `https://sprk-{env}-kv.vault.azure.net/` | Static |
| `SpeAdmin__KeyVaultUri` | `https://sprk-{env}-kv.vault.azure.net/` | Static |
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
| `DocumentIntelligence__OpenAiEndpoint` | KV: `ai-openai-endpoint` | Key Vault |
| `DocumentIntelligence__OpenAiKey` | KV: `ai-openai-key` | Key Vault |
| `DocumentIntelligence__DocIntelEndpoint` | KV: `ai-docintel-endpoint` | Key Vault |
| `DocumentIntelligence__DocIntelKey` | KV: `ai-docintel-key` | Key Vault |
| `DocumentIntelligence__AiSearchEndpoint` | KV: `ai-search-endpoint` | Key Vault |
| `DocumentIntelligence__AiSearchKey` | KV: `ai-search-key` | Key Vault |
| `AiSearch__Endpoint` | KV: `ai-search-endpoint` | Key Vault |
| `ConnectionStrings__ServiceBus` | KV: `ServiceBus-ConnectionString` | Key Vault |
| `ServiceBus__ConnectionString` | KV: `ServiceBus-ConnectionString` | Key Vault |
| `ServiceBus__QueueName` | `sdap-jobs` | Static |
| `ApplicationInsights__ConnectionString` | KV: `AppInsights-ConnectionString` | Key Vault |
| `ApplicationInsightsAgent_EXTENSION_VERSION` | `~3` | Static |
| `Redis__Enabled` | `false` | Static (enable when Redis configured) |
| `ScheduledRagIndexing__TenantId` | KV: `TenantId` | Key Vault |
| `Analysis__KeyVaultUrl` | `https://sprk-{env}-kv.vault.azure.net/` | Static |
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
| `Cors__AllowedOrigins__2-4` | Same as __0 (override base config) | Static |

---

*Environment Deployment Guide v1.0 — Validated March 2026 during demo environment deployment.*
