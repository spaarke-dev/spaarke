# Phase 1 Deployment Guide - AI Document Intelligence

> **Version**: 1.0
> **Date**: December 12, 2025
> **Status**: Production Ready
> **Audience**: System Administrators, DevOps Engineers

---

## Executive Summary

This guide provides step-by-step instructions for deploying the AI Document Intelligence feature. Two deployment models are supported:

| Model | Description | Managed By | Best For |
|-------|-------------|------------|----------|
| **Model 1** | Spaarke Hosted, Customer Dedicated Environment | Spaarke (Azure) / Shared (Power Platform) | SMB, cost-sensitive, rapid deployment |
| **Model 2** | Customer Hosted | Customer (all infrastructure) | Enterprise, compliance, data residency |

**Phase 1 Scope:**
- Multi-tenant parameterization (Environment Variables)
- Azure AI Foundry infrastructure
- Dataverse entities and security roles
- BFF API endpoints
- Integration tests

---

## Table of Contents

1. [Prerequisites](#1-prerequisites)
2. [Model 1: Spaarke Hosted, Customer Dedicated Environment](#2-model-1-spaarke-hosted-customer-dedicated-environment)
3. [Model 2: Customer Hosted](#3-model-2-customer-hosted)
4. [Post-Deployment Validation](#4-post-deployment-validation)
5. [Environment Variables Reference](#5-environment-variables-reference)
6. [Troubleshooting](#6-troubleshooting)
7. [Support](#7-support)

---

## 1. Prerequisites

### 1.1 Common Prerequisites (Both Models)

| Requirement | Description |
|-------------|-------------|
| **Power Platform License** | Dynamics 365 or Power Apps per-user/per-app license |
| **Azure AD Tenant** | Entra ID tenant for authentication |
| **User Permissions** | System Administrator role in Power Platform environment |

### 1.2 Model 1 Prerequisites (Spaarke Hosted)

| Requirement | Description |
|-------------|-------------|
| **Spaarke Subscription** | Active Spaarke AI subscription |
| **Customer ID** | Unique identifier assigned by Spaarke |
| **Environment URL** | Provided Dataverse environment URL |
| **BFF API URL** | Provided BFF API endpoint |

### 1.3 Model 2 Prerequisites (Customer Hosted)

| Requirement | Description |
|-------------|-------------|
| **Azure Subscription** | Azure subscription with Contributor access |
| **Azure CLI** | Version 2.50+ with Bicep extension |
| **Power Platform Environment** | Dedicated environment with Dataverse enabled |
| **Resource Quotas** | Ensure quotas for: Azure OpenAI, AI Search, App Service |
| **PAC CLI** | Power Platform CLI for solution deployment |

**Azure Resource Provider Registration:**
```bash
# Required resource providers
az provider register --namespace Microsoft.CognitiveServices
az provider register --namespace Microsoft.Search
az provider register --namespace Microsoft.MachineLearningServices
az provider register --namespace Microsoft.Web
az provider register --namespace Microsoft.KeyVault
```

---

## 2. Model 1: Spaarke Hosted, Customer Dedicated Environment

### 2.1 Overview

In Model 1, Spaarke manages the Azure infrastructure while the customer receives a dedicated Power Platform environment. This model offers:

- **Rapid Deployment** - Hours instead of days
- **Managed Infrastructure** - Spaarke handles Azure operations
- **Cost Efficiency** - Shared infrastructure resources
- **Simplified Updates** - Automatic feature updates

```
┌─────────────────────────────────────────────────────────────────┐
│                    SPAARKE AZURE TENANT                         │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │              Shared Azure Resources                       │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────────────┐   │  │
│  │  │ Azure      │ │ AI Search  │ │ AI Foundry         │   │  │
│  │  │ OpenAI     │ │ (shared    │ │ Hub + Project      │   │  │
│  │  │            │ │ index)     │ │                    │   │  │
│  │  └────────────┘ └────────────┘ └────────────────────┘   │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────────────┐   │  │
│  │  │ BFF API    │ │ Key Vault  │ │ App Insights       │   │  │
│  │  │ (multi-    │ │ (secrets)  │ │ (monitoring)       │   │  │
│  │  │ tenant)    │ │            │ │                    │   │  │
│  │  └────────────┘ └────────────┘ └────────────────────┘   │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              │ HTTPS (Entra ID Auth)
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│               CUSTOMER POWER PLATFORM ENVIRONMENT               │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │              Dataverse (Customer Dedicated)               │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────────────┐   │  │
│  │  │ sprk_      │ │ Environment│ │ Security Roles     │   │  │
│  │  │ analysis   │ │ Variables  │ │ (customer-managed) │   │  │
│  │  │ entities   │ │ (configured│ │                    │   │  │
│  │  │            │ │ by Spaarke)│ │                    │   │  │
│  │  └────────────┘ └────────────┘ └────────────────────┘   │  │
│  │  ┌────────────┐ ┌────────────┐                          │  │
│  │  │ PCF        │ │ Model-     │                          │  │
│  │  │ Controls   │ │ Driven App │                          │  │
│  │  │            │ │            │                          │  │
│  │  └────────────┘ └────────────┘                          │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 2.2 Deployment Steps

#### Step 1: Receive Customer Package from Spaarke

Spaarke provides a deployment package containing:

| Item | Description |
|------|-------------|
| `Spaarke_DocumentIntelligence_managed.zip` | Dataverse solution package |
| `customer-config.json` | Pre-populated configuration values |
| Customer ID | Unique identifier (e.g., `contoso`) |
| BFF API URL | `https://spe-api-prod.azurewebsites.net` |

#### Step 2: Import Dataverse Solution

```powershell
# Authenticate to Power Platform
pac auth create --url https://your-environment.crm.dynamics.com

# Import the solution
pac solution import --path ./Spaarke_DocumentIntelligence_managed.zip --async

# Verify import
pac solution list
```

**Expected Output:**
```
Unique Name                     Version      Managed
──────────────────────────────────────────────────────────────
Spaarke_DocumentIntelligence    1.0.0.0      Yes
```

#### Step 3: Configure Environment Variables

Navigate to **Power Apps Maker Portal** → **Solutions** → **Spaarke Document Intelligence** → **Environment Variables**.

| Variable | Value (from customer-config.json) |
|----------|-----------------------------------|
| `sprk_BffApiBaseUrl` | `https://spe-api-prod.azurewebsites.net` |
| `sprk_AzureOpenAiEndpoint` | `https://spaarke-openai-prod.openai.azure.com/` |
| `sprk_AzureOpenAiKey` | *(provided by Spaarke via secure channel)* |
| `sprk_DocumentIntelligenceEndpoint` | `https://westus2.api.cognitive.microsoft.com/` |
| `sprk_DocumentIntelligenceKey` | *(provided by Spaarke via secure channel)* |
| `sprk_AzureAiSearchEndpoint` | `https://spaarke-search-prod.search.windows.net` |
| `sprk_AzureAiSearchKey` | *(provided by Spaarke via secure channel)* |
| `sprk_KeyVaultUrl` | `https://spaarke-prod-kv.vault.azure.net/` |
| `sprk_EnableAiFeatures` | `Yes` |
| `sprk_EnableMultiDocumentAnalysis` | `No` |
| `sprk_DeploymentEnvironment` | `Production` |
| `sprk_CustomerTenantId` | *(your Azure AD tenant ID)* |

> **Note:** For secret-type variables (API keys), Spaarke may provide Key Vault references instead of raw values for enhanced security.

#### Step 4: Assign Security Roles

Assign these security roles to appropriate users:

| Role | Purpose | Assign To |
|------|---------|-----------|
| **Spaarke AI User** | Execute analyses, view results | All AI users |
| **Spaarke AI Administrator** | Manage playbooks, configure scopes | AI admins |
| **System Administrator** | Solution management, environment variables | IT admins |

**Via Power Platform Admin Center:**
1. Navigate to **Environments** → **Your Environment** → **Settings**
2. Go to **Users + permissions** → **Security roles**
3. Assign roles to users/teams

#### Step 5: Validate Deployment

Run the validation checklist:

- [ ] Solution imported successfully
- [ ] All environment variables have values
- [ ] Security roles assigned
- [ ] BFF API health check passes: `GET https://spe-api-prod.azurewebsites.net/healthz`
- [ ] Test user can access AI features in Model-Driven App

### 2.3 Ongoing Operations (Model 1)

| Operation | Responsibility | Process |
|-----------|---------------|---------|
| Azure infrastructure | Spaarke | Automatic maintenance |
| Solution updates | Spaarke | Notification + managed solution upgrade |
| Security role management | Customer | Power Platform Admin Center |
| User management | Customer | Azure AD / Entra ID |
| Monitoring | Shared | Spaarke dashboards + customer access |

---

## 3. Model 2: Customer Hosted

### 3.1 Overview

In Model 2, the customer deploys and manages all infrastructure. This model offers:

- **Full Control** - Customer owns all resources
- **Data Residency** - Data stays in customer's Azure tenant
- **Compliance** - Meet specific regulatory requirements
- **Customization** - Modify infrastructure as needed

```
┌─────────────────────────────────────────────────────────────────┐
│                   CUSTOMER AZURE TENANT                         │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │           Customer-Owned Azure Resources                  │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────────────┐   │  │
│  │  │ Azure      │ │ AI Search  │ │ AI Foundry         │   │  │
│  │  │ OpenAI     │ │ (dedicated │ │ Hub + Project      │   │  │
│  │  │ (dedicated)│ │ index)     │ │                    │   │  │
│  │  └────────────┘ └────────────┘ └────────────────────┘   │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────────────┐   │  │
│  │  │ BFF API    │ │ Key Vault  │ │ App Insights       │   │  │
│  │  │ (dedicated)│ │ (secrets)  │ │ (monitoring)       │   │  │
│  │  └────────────┘ └────────────┘ └────────────────────┘   │  │
│  └──────────────────────────────────────────────────────────┘  │
│                                                                 │
│  ┌──────────────────────────────────────────────────────────┐  │
│  │          Customer Power Platform Environment              │  │
│  │  ┌────────────┐ ┌────────────┐ ┌────────────────────┐   │  │
│  │  │ Dataverse  │ │ PCF        │ │ Model-Driven App   │   │  │
│  │  │ Solution   │ │ Controls   │ │                    │   │  │
│  │  └────────────┘ └────────────┘ └────────────────────┘   │  │
│  └──────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### 3.2 Deployment Steps

#### Step 1: Prepare Azure Subscription

```bash
# Login to Azure
az login

# Set subscription
az account set --subscription "Your Subscription Name"

# Verify access
az account show
```

#### Step 2: Deploy Azure Infrastructure

**Option A: Deploy via Bicep (Recommended)**

```bash
# Clone or obtain the Spaarke infrastructure repository
cd infrastructure/bicep

# Copy and customize parameter file
cp parameters/customer-template.bicepparam parameters/your-company.bicepparam
```

Edit `your-company.bicepparam`:

```bicep
using './stacks/model2-full.bicep'

// Customer-specific parameters
param customerId = 'yourcompany'
param environment = 'prod'
param location = 'eastus'

// Resource naming (follows convention: sprk{customer}{env}-{resource})
param resourceGroupName = 'rg-yourcompany-spaarke-prod'

// Networking (optional)
param allowedIpRanges = ['203.0.113.0/24']  // Your corporate IP range
param vnetIntegration = false
param privateEndpoints = false

// AI Configuration
param openAiSkuName = 'S0'
param openAiDeployments = [
  {
    name: 'gpt-4o-mini'
    model: 'gpt-4o-mini'
    version: '2024-07-18'
    capacity: 30
  }
]

// Feature flags
param enableAiFeatures = true
param enableMultiDocumentAnalysis = false

// Tags for cost tracking
param costCenter = 'IT-Legal'
param owner = 'admin@yourcompany.com'
```

Deploy:

```bash
# Create resource group
az group create \
  --name rg-yourcompany-spaarke-prod \
  --location eastus

# Deploy infrastructure
az deployment group create \
  --resource-group rg-yourcompany-spaarke-prod \
  --template-file stacks/model2-full.bicep \
  --parameters parameters/your-company.bicepparam

# Capture outputs
az deployment group show \
  --resource-group rg-yourcompany-spaarke-prod \
  --name model2-full \
  --query properties.outputs -o json > deployment-outputs.json
```

**Option B: Deploy via Azure Portal**

Use the Azure Marketplace managed application (coming soon):
1. Search for "Spaarke Document Intelligence" in Azure Marketplace
2. Follow the deployment wizard
3. Provide required parameters

#### Step 3: Store Secrets in Key Vault

```bash
# Get the Key Vault name from deployment outputs
KV_NAME="sprkyourcompanyprod-kv"

# Store Azure OpenAI key
az keyvault secret set \
  --vault-name $KV_NAME \
  --name openai-api-key \
  --value "$(az cognitiveservices account keys list \
    --name sprkyourcompanyprod-openai \
    --resource-group rg-yourcompany-spaarke-prod \
    --query key1 -o tsv)"

# Store Document Intelligence key
az keyvault secret set \
  --vault-name $KV_NAME \
  --name docintel-api-key \
  --value "$(az cognitiveservices account keys list \
    --name sprkyourcompanyprod-docintel \
    --resource-group rg-yourcompany-spaarke-prod \
    --query key1 -o tsv)"

# Store AI Search admin key
az keyvault secret set \
  --vault-name $KV_NAME \
  --name search-admin-key \
  --value "$(az search admin-key show \
    --service-name sprkyourcompanyprod-search \
    --resource-group rg-yourcompany-spaarke-prod \
    --query primaryKey -o tsv)"

# Store App Insights key
az keyvault secret set \
  --vault-name $KV_NAME \
  --name appinsights-key \
  --value "$(az monitor app-insights component show \
    --app sprkyourcompanyprod-insights \
    --resource-group rg-yourcompany-spaarke-prod \
    --query instrumentationKey -o tsv)"
```

#### Step 4: Deploy BFF API

```bash
# Build the API
cd src/server/api/Sprk.Bff.Api
dotnet publish -c Release -o ./publish

# Create deployment package
cd publish
zip -r Sprk.Bff.Api.zip .

# Deploy to App Service
az webapp deploy \
  --resource-group rg-yourcompany-spaarke-prod \
  --name sprkyourcompanyprod-api \
  --src-path ./Sprk.Bff.Api.zip \
  --type zip

# Verify deployment
curl https://sprkyourcompanyprod-api.azurewebsites.net/healthz
```

#### Step 5: Configure App Service Settings

```bash
APP_NAME="sprkyourcompanyprod-api"
RG_NAME="rg-yourcompany-spaarke-prod"
KV_URI="https://sprkyourcompanyprod-kv.vault.azure.net/"

# Set application settings
az webapp config appsettings set \
  --resource-group $RG_NAME \
  --name $APP_NAME \
  --settings \
    "ASPNETCORE_ENVIRONMENT=Production" \
    "Dataverse__Url=https://yourcompany.crm.dynamics.com" \
    "AzureOpenAi__Endpoint=https://sprkyourcompanyprod-openai.openai.azure.com/" \
    "AzureOpenAi__DeploymentName=gpt-4o-mini" \
    "AiSearch__Endpoint=https://sprkyourcompanyprod-search.search.windows.net" \
    "KeyVault__Url=$KV_URI"

# Configure Key Vault references for secrets
az webapp config appsettings set \
  --resource-group $RG_NAME \
  --name $APP_NAME \
  --settings \
    "AzureOpenAi__ApiKey=@Microsoft.KeyVault(SecretUri=${KV_URI}secrets/openai-api-key/)" \
    "AiSearch__AdminKey=@Microsoft.KeyVault(SecretUri=${KV_URI}secrets/search-admin-key/)" \
    "ApplicationInsights__InstrumentationKey=@Microsoft.KeyVault(SecretUri=${KV_URI}secrets/appinsights-key/)"
```

#### Step 6: Deploy AI Foundry (Optional)

If using AI Foundry for prompt flow orchestration:

```bash
# Deploy AI Foundry Hub
az deployment group create \
  --resource-group rg-yourcompany-spaarke-prod \
  --template-file infrastructure/bicep/modules/ai-foundry-hub.bicep \
  --parameters \
    customerId=yourcompany \
    environment=prod \
    location=eastus

# Create connections
az ml connection create \
  --file infrastructure/ai-foundry/connections/azure-openai-connection.yaml \
  --workspace-name sprkyourcompanyprod-aif-hub \
  --resource-group rg-yourcompany-spaarke-prod
```

#### Step 7: Import Dataverse Solution

```powershell
# Authenticate to Power Platform
pac auth create --url https://yourcompany.crm.dynamics.com

# Import the solution (unmanaged for Model 2)
pac solution import --path ./Spaarke_DocumentIntelligence.zip --async

# Publish customizations
pac solution publish
```

#### Step 8: Configure Environment Variables

Navigate to **Power Apps Maker Portal** → **Solutions** → **Spaarke Document Intelligence** → **Environment Variables**.

| Variable | Value |
|----------|-------|
| `sprk_BffApiBaseUrl` | `https://sprkyourcompanyprod-api.azurewebsites.net` |
| `sprk_AzureOpenAiEndpoint` | `https://sprkyourcompanyprod-openai.openai.azure.com/` |
| `sprk_AzureOpenAiKey` | `@Microsoft.KeyVault(SecretUri=https://sprkyourcompanyprod-kv.vault.azure.net/secrets/openai-api-key/)` |
| `sprk_DocumentIntelligenceEndpoint` | `https://eastus.api.cognitive.microsoft.com/` |
| `sprk_DocumentIntelligenceKey` | `@Microsoft.KeyVault(SecretUri=https://sprkyourcompanyprod-kv.vault.azure.net/secrets/docintel-api-key/)` |
| `sprk_AzureAiSearchEndpoint` | `https://sprkyourcompanyprod-search.search.windows.net` |
| `sprk_AzureAiSearchKey` | `@Microsoft.KeyVault(SecretUri=https://sprkyourcompanyprod-kv.vault.azure.net/secrets/search-admin-key/)` |
| `sprk_PromptFlowEndpoint` | `https://sprkyourcompanyprod-aif-proj.eastus.inference.ml.azure.com/score` |
| `sprk_KeyVaultUrl` | `https://sprkyourcompanyprod-kv.vault.azure.net/` |
| `sprk_EnableAiFeatures` | `Yes` |
| `sprk_EnableMultiDocumentAnalysis` | `No` |
| `sprk_DeploymentEnvironment` | `Production` |
| `sprk_CustomerTenantId` | *(leave empty - you're the hosting tenant)* |

#### Step 9: Create Security Roles

Security roles must be manually created or imported for Model 2:

**Option A: Import from Solution**
```powershell
pac solution import --path ./Spaarke_SecurityRoles.zip
```

**Option B: Create Manually**

Navigate to **Power Platform Admin Center** → **Environments** → **Settings** → **Security roles**:

**Spaarke AI User Role:**
- `sprk_analysis`: Read, Create, Append, Append To
- `sprk_analysisplaybook`: Read
- `sprk_analysisaction`: Read
- `sprk_analysisskill`: Read
- `sprk_analysisknowledge`: Read

**Spaarke AI Administrator Role:**
- All `sprk_*` entities: Full access
- Environment Variables: Read, Write

#### Step 10: Grant Key Vault Access to Power Platform

For Key Vault references to work, grant access:

```bash
# Get Power Platform service principal
# (This varies by region - check Microsoft docs for your region's principal)
POWER_PLATFORM_PRINCIPAL="00000007-0000-0000-c000-000000000000"

# Grant Key Vault access
az keyvault set-policy \
  --name sprkyourcompanyprod-kv \
  --spn $POWER_PLATFORM_PRINCIPAL \
  --secret-permissions get list
```

### 3.3 Ongoing Operations (Model 2)

| Operation | Responsibility | Process |
|-----------|---------------|---------|
| Azure infrastructure | Customer | Azure Portal / CLI / IaC |
| Solution updates | Customer | Import new solution versions from Spaarke |
| Secret rotation | Customer | Update Key Vault secrets, restart App Service |
| Monitoring | Customer | Azure Monitor, App Insights dashboards |
| Backup | Customer | Azure Backup for Key Vault, Dataverse backup |

---

## 4. Post-Deployment Validation

### 4.1 Health Check Validation

```bash
# BFF API health check
curl -s https://YOUR-API-URL/healthz | jq .

# Expected response:
{
  "status": "Healthy",
  "checks": {
    "dataverse": "Healthy",
    "openai": "Healthy",
    "aiSearch": "Healthy"
  }
}
```

### 4.2 End-to-End Validation Checklist

| Test | Steps | Expected Result |
|------|-------|-----------------|
| **BFF API Health** | `GET /healthz` | Status: 200, Healthy |
| **Authentication** | Open MDA, sign in | Redirects to Entra ID, successful login |
| **Entity Access** | Navigate to Analysis entity | Grid loads, no errors |
| **AI Features** | Create new Analysis | AI panel displays, streaming works |
| **Environment Variables** | Check PCF console logs | No "undefined" or "null" API URLs |

### 4.3 Automated Validation Script

```powershell
# validation-script.ps1
param(
    [Parameter(Mandatory=$true)]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory=$true)]
    [string]$DataverseUrl
)

Write-Host "=== Spaarke AI Document Intelligence Validation ===" -ForegroundColor Cyan

# Test 1: BFF API Health
Write-Host "`n[Test 1] BFF API Health Check..." -ForegroundColor Yellow
try {
    $health = Invoke-RestMethod -Uri "$ApiBaseUrl/healthz" -Method Get
    if ($health.status -eq "Healthy") {
        Write-Host "  PASS: API is healthy" -ForegroundColor Green
    } else {
        Write-Host "  FAIL: API unhealthy - $($health.status)" -ForegroundColor Red
    }
} catch {
    Write-Host "  FAIL: API unreachable - $_" -ForegroundColor Red
}

# Test 2: OpenAPI Endpoint
Write-Host "`n[Test 2] OpenAPI Documentation..." -ForegroundColor Yellow
try {
    $swagger = Invoke-WebRequest -Uri "$ApiBaseUrl/swagger/v1/swagger.json" -Method Get
    if ($swagger.StatusCode -eq 200) {
        Write-Host "  PASS: OpenAPI endpoint accessible" -ForegroundColor Green
    }
} catch {
    Write-Host "  FAIL: OpenAPI endpoint error - $_" -ForegroundColor Red
}

# Test 3: Dataverse Connection (requires token)
Write-Host "`n[Test 3] Dataverse WhoAmI..." -ForegroundColor Yellow
Write-Host "  SKIP: Requires interactive authentication" -ForegroundColor Gray

Write-Host "`n=== Validation Complete ===" -ForegroundColor Cyan
```

---

## 5. Environment Variables Reference

### 5.1 Complete Variable List

| # | Variable | Type | Required | Model 1 | Model 2 |
|---|----------|------|----------|---------|---------|
| 1 | `sprk_BffApiBaseUrl` | Text | Yes | Spaarke provides | Customer deploys |
| 2 | `sprk_AzureOpenAiEndpoint` | Text | Yes | Shared | Dedicated |
| 3 | `sprk_AzureOpenAiKey` | Secret | Yes | Shared secret | Key Vault ref |
| 4 | `sprk_DocumentIntelligenceEndpoint` | Text | Yes | Shared | Dedicated |
| 5 | `sprk_DocumentIntelligenceKey` | Secret | Yes | Shared secret | Key Vault ref |
| 6 | `sprk_AzureAiSearchEndpoint` | Text | Yes | Shared | Dedicated |
| 7 | `sprk_AzureAiSearchKey` | Secret | Yes | Shared secret | Key Vault ref |
| 8 | `sprk_PromptFlowEndpoint` | Text | No | Shared | Dedicated |
| 9 | `sprk_EnableAiFeatures` | Choice | Yes | Yes | Yes |
| 10 | `sprk_EnableMultiDocumentAnalysis` | Choice | Yes | No | No |
| 11 | `sprk_RedisConnectionString` | Secret | No | N/A | Key Vault ref |
| 12 | `sprk_ApplicationInsightsKey` | Secret | No | Shared | Key Vault ref |
| 13 | `sprk_KeyVaultUrl` | Text | Yes | Spaarke KV | Customer KV |
| 14 | `sprk_CustomerTenantId` | Text | No | Customer's AAD | Empty |
| 15 | `sprk_DeploymentEnvironment` | Text | Yes | Production | Production |

### 5.2 Key Vault Reference Format

For secret-type variables, use this format:
```
@Microsoft.KeyVault(SecretUri=https://{vault-name}.vault.azure.net/secrets/{secret-name}/)
```

Example:
```
@Microsoft.KeyVault(SecretUri=https://sprkyourcompanyprod-kv.vault.azure.net/secrets/openai-api-key/)
```

---

## 6. Troubleshooting

### 6.1 Common Issues

| Issue | Symptom | Solution |
|-------|---------|----------|
| **API Unreachable** | PCF shows "Network Error" | Verify `sprk_BffApiBaseUrl` is correct and HTTPS |
| **Authentication Failed** | 401 errors in browser console | Check App Registration, API permissions |
| **Key Vault Access Denied** | Environment variables show as empty | Grant Power Platform access to Key Vault |
| **AI Features Disabled** | No AI options in UI | Set `sprk_EnableAiFeatures` to `Yes` |
| **OpenAI Rate Limited** | 429 errors | Check quota in Azure Portal, increase capacity |
| **Solution Import Failed** | Error during import | Check dependencies, ensure managed solution for Model 1 |

### 6.2 Diagnostic Commands

```bash
# Check App Service logs
az webapp log tail \
  --name YOUR-APP-NAME \
  --resource-group YOUR-RG

# Check Key Vault access
az keyvault secret list --vault-name YOUR-KV-NAME

# Check OpenAI deployment status
az cognitiveservices account deployment list \
  --name YOUR-OPENAI-NAME \
  --resource-group YOUR-RG

# Check AI Search index
az search index list \
  --service-name YOUR-SEARCH-NAME \
  --resource-group YOUR-RG
```

### 6.3 Support Escalation Path

| Level | Contact | When |
|-------|---------|------|
| **L1** | Documentation, this guide | First attempt |
| **L2** | Spaarke Support Portal | Config issues, known issues |
| **L3** | Spaarke Engineering | Critical bugs, feature requests |

---

## 7. Support

### 7.1 Resources

| Resource | URL |
|----------|-----|
| Spaarke Support Portal | https://support.spaarke.com |
| Documentation | https://docs.spaarke.com |
| Status Page | https://status.spaarke.com |
| Community Forum | https://community.spaarke.com |

### 7.2 Contact Information

- **Email**: support@spaarke.com
- **Phone**: +1-800-SPAARKE (Model 1 customers with Premium support)
- **Hours**: 24/7 for critical issues, business hours for general inquiries

---

## Appendix A: Resource Naming Convention

| Resource Type | Pattern | Example |
|---------------|---------|---------|
| Resource Group | `rg-{customer}-spaarke-{env}` | `rg-contoso-spaarke-prod` |
| App Service | `sprk{customer}{env}-api` | `sprkcontosoprod-api` |
| Key Vault | `sprk{customer}{env}-kv` | `sprkcontosoprod-kv` |
| Azure OpenAI | `sprk{customer}{env}-openai` | `sprkcontosoprod-openai` |
| AI Search | `sprk{customer}{env}-search` | `sprkcontosoprod-search` |
| AI Foundry Hub | `sprk{customer}{env}-aif-hub` | `sprkcontosoprod-aif-hub` |
| AI Foundry Project | `sprk{customer}{env}-aif-proj` | `sprkcontosoprod-aif-proj` |
| App Insights | `sprk{customer}{env}-insights` | `sprkcontosoprod-insights` |

---

## Appendix B: Deployment Timeline

| Phase | Model 1 Duration | Model 2 Duration |
|-------|------------------|------------------|
| Prerequisites | 30 min | 2-4 hours |
| Infrastructure Deployment | N/A (Spaarke) | 1-2 hours |
| Solution Import | 15 min | 15 min |
| Environment Variables | 30 min | 1 hour |
| Security Configuration | 30 min | 1 hour |
| Validation | 30 min | 1 hour |
| **Total** | **~2 hours** | **~6-9 hours** |

---

*Last updated: December 12, 2025*
