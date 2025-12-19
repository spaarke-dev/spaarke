# Spaarke Infrastructure Packaging Strategy

> **Version**: 1.0  
> **Date**: December 4, 2025  
> **Status**: Draft  
> **Purpose**: Define how Azure resources and Power Platform components are packaged for both deployment models

---

## Executive Summary

Spaarke needs a **modular, repeatable infrastructure packaging** approach that supports:

1. **Model 1 (Spaarke-Hosted)**: Multi-tenant with dedicated resources per customer
2. **Model 2 (Customer-Hosted)**: Complete deployment package for customer tenants

This document defines the Infrastructure-as-Code (IaC) strategy using **Bicep** for Azure resources and **Power Platform solutions** for Dataverse/model-driven apps.

---

## Table of Contents

1. [Current State Analysis](#1-current-state-analysis)
2. [Target Architecture](#2-target-architecture)
3. [Resource Inventory](#3-resource-inventory)
4. [Packaging Strategy](#4-packaging-strategy)
5. [Bicep Module Structure](#5-bicep-module-structure)
6. [Deployment Scripts](#6-deployment-scripts)
7. [Configuration Management](#7-configuration-management)
8. [Model-Specific Deployments](#8-model-specific-deployments)
9. [Implementation Roadmap](#9-implementation-roadmap)

---

## 1. Current State Analysis

### 1.1 What We Have Today

| Item | Location | Status |
|------|----------|--------|
| Local config files | `config/*.local.json` | ✅ Working for dev |
| PowerShell scripts | `scripts/*.ps1` | ⚠️ Ad-hoc, not parameterized |
| Docker Compose | `docker-compose.yml` | ✅ Local dev only |
| Bicep templates | None | ❌ Missing |
| ARM templates | None | ❌ Missing |

### 1.2 Current Config Files (Reference)

```
config/
├── app-registrations.local.json  # Entra ID app registrations
├── azure-config.local.json       # Azure subscription/tenant
├── dataverse-config.local.json   # Dataverse environment URLs
├── keyvault-config.local.json    # Key Vault configuration
└── sharepoint-config.local.json  # SPE ContainerType config
```

### 1.3 Pain Points

| Issue | Impact |
|-------|--------|
| No IaC | Manual Azure resource creation |
| Hardcoded GUIDs | Can't deploy to new environments |
| No environment promotion | Dev → Staging → Prod manual |
| No customer provisioning | Model 1 onboarding is manual |
| No deployment package | Model 2 requires documentation |

---

## 2. Target Architecture

### 2.1 Infrastructure Layers

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         INFRASTRUCTURE LAYERS                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  LAYER 1: FOUNDATION (Per-Tenant, One-Time)                                │
│  ──────────────────────────────────────────                                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                        │
│  │ Entra ID    │  │ Azure       │  │ Resource    │                        │
│  │ Tenant      │  │ Subscription│  │ Providers   │                        │
│  └─────────────┘  └─────────────┘  └─────────────┘                        │
│                                                                             │
│  LAYER 2: SHARED SERVICES (Per-Tenant, Shared by Customers)                │
│  ──────────────────────────────────────────────────────────                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                        │
│  │ Key Vault   │  │ Log         │  │ App Insights│                        │
│  │ (Shared)    │  │ Analytics   │  │ (Shared)    │                        │
│  └─────────────┘  └─────────────┘  └─────────────┘                        │
│                                                                             │
│  LAYER 3: PLATFORM SERVICES (Per-Environment)                              │
│  ─────────────────────────────────────────────                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐      │
│  │ App Service │  │ Redis       │  │ Service     │  │ Storage     │      │
│  │ Plan        │  │ Cache       │  │ Bus         │  │ Account     │      │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────┘      │
│                                                                             │
│  LAYER 4: APPLICATION (Sprk.Bff.Api)                                       │
│  ───────────────────────────────────                                       │
│  ┌─────────────┐  ┌─────────────┐                                         │
│  │ App         │  │ App         │                                         │
│  │ Registration│  │ Service     │                                         │
│  └─────────────┘  └─────────────┘                                         │
│                                                                             │
│  LAYER 5: CUSTOMER RESOURCES (Per-Customer)                                │
│  ──────────────────────────────────────────                                │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐      │
│  │ SPE         │  │ AI Search   │  │ Dataverse   │  │ Customer    │      │
│  │ Container   │  │ Index       │  │ Environment │  │ Key Vault   │      │
│  └─────────────┘  └─────────────┘  └─────────────┘  └─────────────┘      │
│                                                                             │
│  LAYER 6: AI SERVICES (Per-Tenant or Per-Customer)                         │
│  ─────────────────────────────────────────────────                         │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                        │
│  │ Azure       │  │ Azure AI    │  │ Document    │                        │
│  │ OpenAI      │  │ Search      │  │ Intelligence│                        │
│  └─────────────┘  └─────────────┘  └─────────────┘                        │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 2.2 Model 1 vs Model 2 Resource Ownership

| Layer | Model 1 (Spaarke-Hosted) | Model 2 (Customer-Hosted) |
|-------|--------------------------|---------------------------|
| **Foundation** | Spaarke tenant | Customer tenant |
| **Shared Services** | Spaarke-owned | Customer-owned |
| **Platform Services** | Spaarke-owned (shared) | Customer-owned |
| **Application** | Spaarke-owned | Customer-deployed |
| **Customer Resources** | Spaarke-owned (isolated) | Customer-owned |
| **AI Services** | Spaarke-owned (shared) | Customer-owned (BYOK) |

---

## 3. Resource Inventory

### 3.1 Entra ID Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **BFF API App Registration** | Sprk.Bff.Api authentication | Spaarke tenant | Customer tenant |
| **PCF/UI App Registration** | Client-side auth (MSAL) | Spaarke tenant | Customer tenant |
| **SPE App Registration** | SharePoint Embedded access | Spaarke tenant | Customer tenant |
| **Application Users** | Service accounts in Dataverse | Per-environment | Per-environment |

### 3.2 Azure Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **Resource Group** | Container for resources | `rg-spaarke-{env}` | `rg-spaarke` |
| **App Service Plan** | Compute for BFF API | Shared (S1/P1) | Dedicated |
| **App Service** | Sprk.Bff.Api hosting | Multi-tenant slots | Single |
| **Key Vault** | Secrets, certificates | Shared + per-customer | Dedicated |
| **Redis Cache** | Token caching, sessions | Shared (Premium) | Dedicated |
| **Service Bus** | Job queue | Shared namespace | Dedicated |
| **Storage Account** | Temp files, logs | Shared | Dedicated |
| **App Insights** | Telemetry | Shared | Dedicated |
| **Log Analytics** | Centralized logs | Shared | Dedicated |

### 3.3 AI Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **Azure OpenAI** | LLM inference | Shared (metered) | Customer BYOK |
| **Azure AI Search** | Vector search | Shared (multi-index) | Dedicated |
| **Document Intelligence** | OCR/extraction | Shared | Dedicated |

### 3.4 Power Platform Resources

| Resource | Purpose | Packaging |
|----------|---------|-----------|
| **Dataverse Solution** | Entities, forms, views | Managed solution `.zip` |
| **Model-Driven App** | Spaarke UI | Part of solution |
| **PCF Controls** | React components | Part of solution |
| **Security Roles** | RBAC | Part of solution |
| **Environment Variables** | Runtime config | Part of solution |

### 3.5 SharePoint Embedded Resources

| Resource | Purpose | Model 1 | Model 2 |
|----------|---------|---------|---------|
| **ContainerType** | SPE storage definition | Spaarke-owned | Customer-owned |
| **Containers** | Per-customer storage | Per-customer | Per-customer |

---

## 4. Packaging Strategy

### 4.1 Package Types

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         PACKAGE STRUCTURE                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  infrastructure/                                                            │
│  ├── bicep/                                                                │
│  │   ├── modules/                    # Reusable Bicep modules              │
│  │   │   ├── app-registration.bicep  # Entra ID app reg (via Graph)       │
│  │   │   ├── app-service.bicep       # Web app                            │
│  │   │   ├── key-vault.bicep         # Key Vault + secrets                │
│  │   │   ├── redis.bicep             # Redis Cache                        │
│  │   │   ├── service-bus.bicep       # Service Bus namespace              │
│  │   │   ├── ai-search.bicep         # Azure AI Search                    │
│  │   │   ├── openai.bicep            # Azure OpenAI                       │
│  │   │   ├── doc-intelligence.bicep  # Document Intelligence              │
│  │   │   └── monitoring.bicep        # App Insights + Log Analytics       │
│  │   │                                                                     │
│  │   ├── stacks/                     # Composed deployments               │
│  │   │   ├── model1-shared.bicep     # Model 1: Shared services          │
│  │   │   ├── model1-customer.bicep   # Model 1: Per-customer resources   │
│  │   │   ├── model2-full.bicep       # Model 2: Complete deployment      │
│  │   │   └── ai-services.bicep       # AI resources (both models)        │
│  │   │                                                                     │
│  │   └── parameters/                 # Environment-specific params        │
│  │       ├── dev.bicepparam                                               │
│  │       ├── staging.bicepparam                                           │
│  │       ├── prod.bicepparam                                              │
│  │       └── customer-template.bicepparam                                 │
│  │                                                                         │
│  ├── scripts/                        # Deployment automation              │
│  │   ├── Deploy-Model1-Shared.ps1    # Deploy shared infrastructure      │
│  │   ├── Deploy-Model1-Customer.ps1  # Onboard new customer              │
│  │   ├── Deploy-Model2-Full.ps1      # Full customer deployment          │
│  │   ├── Register-AppRegistrations.ps1                                   │
│  │   ├── Setup-SPE-ContainerType.ps1                                     │
│  │   └── Configure-Dataverse.ps1                                         │
│  │                                                                         │
│  └── docs/                           # Deployment documentation           │
│      ├── MODEL1-DEPLOYMENT-GUIDE.md                                       │
│      ├── MODEL2-DEPLOYMENT-GUIDE.md                                       │
│      ├── CUSTOMER-ONBOARDING.md                                           │
│      └── PREREQUISITES.md                                                  │
│                                                                             │
│  power-platform/                                                           │
│  ├── solutions/                                                            │
│  │   ├── SpaarkeCore/                # Core entities, forms, views        │
│  │   │   └── SpaarkeCore_managed.zip                                      │
│  │   ├── SpaarkePCF/                 # PCF controls                       │
│  │   │   └── SpaarkePCF_managed.zip                                       │
│  │   └── SpaarkeAI/                  # AI configuration entities          │
│  │       └── SpaarkeAI_managed.zip                                        │
│  │                                                                         │
│  └── setup/                                                                │
│      ├── Create-ApplicationUser.ps1                                       │
│      ├── Import-Solutions.ps1                                             │
│      └── Configure-SecurityRoles.ps1                                      │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Model 1: Spaarke-Hosted Deployment

**One-time setup (per environment):**
```
Deploy-Model1-Shared.ps1 -Environment "prod"
├── Creates shared Resource Group
├── Deploys App Service Plan
├── Deploys Sprk.Bff.Api App Service
├── Deploys shared Key Vault
├── Deploys shared Redis Cache
├── Deploys shared Service Bus
├── Deploys shared Azure OpenAI
├── Deploys shared Azure AI Search
├── Configures App Insights
└── Creates ContainerType (SPE)
```

**Per-customer onboarding:**
```
Deploy-Model1-Customer.ps1 -CustomerId "contoso" -Environment "prod"
├── Creates customer-specific AI Search index
├── Creates SPE Container for customer
├── Creates Dataverse environment (if needed)
├── Creates customer record in Dataverse
├── Configures customer Key Vault secrets (optional)
└── Outputs onboarding credentials
```

### 4.3 Model 2: Customer-Hosted Deployment

**Complete deployment package:**
```
Deploy-Model2-Full.ps1 -CustomerTenantId "xxx" -SubscriptionId "yyy"
├── Creates Resource Group
├── Creates all App Registrations (via Graph API)
├── Deploys all Azure resources
├── Creates ContainerType (SPE)
├── Creates SPE Container
├── Imports Power Platform solutions
├── Creates Application User
├── Configures environment variables
└── Outputs configuration summary
```

---

## 5. Bicep Module Structure

### 5.1 Core Module: App Service

```bicep
// infrastructure/bicep/modules/app-service.bicep

@description('Name of the App Service')
param appServiceName string

@description('App Service Plan resource ID')
param appServicePlanId string

@description('Location for the App Service')
param location string = resourceGroup().location

@description('Runtime stack (e.g., DOTNETCORE|8.0)')
param runtimeStack string = 'DOTNETCORE|8.0'

@description('App settings')
param appSettings object = {}

@description('Key Vault reference for secrets')
param keyVaultName string = ''

resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: appServiceName
  location: location
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: runtimeStack
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      appSettings: [for setting in items(appSettings): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

output appServiceId string = appService.id
output appServicePrincipalId string = appService.identity.principalId
output appServiceDefaultHostName string = appService.properties.defaultHostName
```

### 5.2 Core Module: Azure OpenAI

```bicep
// infrastructure/bicep/modules/openai.bicep

@description('Name of the Azure OpenAI resource')
param openAiName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('SKU for Azure OpenAI')
param sku string = 'S0'

@description('Model deployments to create')
param deployments array = [
  {
    name: 'gpt-4o'
    model: 'gpt-4o'
    version: '2024-08-06'
    capacity: 150
  }
  {
    name: 'text-embedding-3-large'
    model: 'text-embedding-3-large'
    version: '1'
    capacity: 350
  }
]

resource openAi 'Microsoft.CognitiveServices/accounts@2023-10-01-preview' = {
  name: openAiName
  location: location
  kind: 'OpenAI'
  sku: {
    name: sku
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'
  }
}

resource modelDeployments 'Microsoft.CognitiveServices/accounts/deployments@2023-10-01-preview' = [for deployment in deployments: {
  parent: openAi
  name: deployment.name
  sku: {
    name: 'Standard'
    capacity: deployment.capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: deployment.model
      version: deployment.version
    }
  }
}]

output openAiEndpoint string = openAi.properties.endpoint
output openAiId string = openAi.id
output openAiName string = openAi.name
```

### 5.3 Core Module: Azure AI Search

```bicep
// infrastructure/bicep/modules/ai-search.bicep

@description('Name of the Azure AI Search resource')
param searchServiceName string

@description('Location for the resource')
param location string = resourceGroup().location

@description('SKU for Azure AI Search')
@allowed(['basic', 'standard', 'standard2', 'standard3'])
param sku string = 'standard'

@description('Number of replicas')
param replicaCount int = 1

@description('Number of partitions')
param partitionCount int = 1

resource searchService 'Microsoft.Search/searchServices@2023-11-01' = {
  name: searchServiceName
  location: location
  sku: {
    name: sku
  }
  properties: {
    replicaCount: replicaCount
    partitionCount: partitionCount
    hostingMode: 'default'
    semanticSearch: 'standard'
  }
}

output searchServiceId string = searchService.id
output searchServiceEndpoint string = 'https://${searchServiceName}.search.windows.net'
output searchServiceAdminKey string = searchService.listAdminKeys().primaryKey
```

### 5.4 Stack: Model 2 Full Deployment

```bicep
// infrastructure/bicep/stacks/model2-full.bicep

targetScope = 'subscription'

@description('Customer identifier (lowercase, no spaces)')
param customerId string

@description('Environment name')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'prod'

@description('Primary Azure region')
param location string = 'eastus'

@description('Dataverse environment URL')
param dataverseUrl string

@description('SPE ContainerType ID (created separately)')
param containerTypeId string

// Variables
var resourceGroupName = 'rg-spaarke-${customerId}-${environment}'
var baseName = 'sprk${customerId}${environment}'

// Resource Group
resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: {
    customer: customerId
    environment: environment
    application: 'spaarke'
    deploymentModel: 'model2'
  }
}

// Key Vault
module keyVault '../modules/key-vault.bicep' = {
  scope: rg
  name: 'keyVault'
  params: {
    keyVaultName: '${baseName}-kv'
    location: location
  }
}

// Redis Cache
module redis '../modules/redis.bicep' = {
  scope: rg
  name: 'redis'
  params: {
    redisName: '${baseName}-redis'
    location: location
    sku: 'Basic'
  }
}

// Service Bus
module serviceBus '../modules/service-bus.bicep' = {
  scope: rg
  name: 'serviceBus'
  params: {
    serviceBusName: '${baseName}-sb'
    location: location
  }
}

// App Service Plan
module appServicePlan '../modules/app-service-plan.bicep' = {
  scope: rg
  name: 'appServicePlan'
  params: {
    planName: '${baseName}-plan'
    location: location
    sku: 'B1'  // Basic for Model 2
  }
}

// Sprk.Bff.Api
module bffApi '../modules/app-service.bicep' = {
  scope: rg
  name: 'bffApi'
  params: {
    appServiceName: '${baseName}-api'
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    keyVaultName: keyVault.outputs.keyVaultName
    appSettings: {
      DATAVERSE_URL: dataverseUrl
      SPE_CONTAINER_TYPE_ID: containerTypeId
      REDIS_ENABLED: 'true'
      REDIS_CONNECTION_STRING: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=redis-connection-string)'
    }
  }
}

// Azure OpenAI (Customer BYOK)
module openAi '../modules/openai.bicep' = {
  scope: rg
  name: 'openAi'
  params: {
    openAiName: '${baseName}-openai'
    location: location
  }
}

// Azure AI Search
module aiSearch '../modules/ai-search.bicep' = {
  scope: rg
  name: 'aiSearch'
  params: {
    searchServiceName: '${baseName}-search'
    location: location
    sku: 'basic'  // Basic for Model 2 single customer
  }
}

// Monitoring
module monitoring '../modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring'
  params: {
    appInsightsName: '${baseName}-insights'
    logAnalyticsName: '${baseName}-logs'
    location: location
  }
}

// Outputs for configuration
output resourceGroupName string = rg.name
output apiUrl string = 'https://${bffApi.outputs.appServiceDefaultHostName}'
output keyVaultUri string = keyVault.outputs.keyVaultUri
output openAiEndpoint string = openAi.outputs.openAiEndpoint
output searchEndpoint string = aiSearch.outputs.searchServiceEndpoint
output appInsightsKey string = monitoring.outputs.instrumentationKey
```

---

## 6. Deployment Scripts

### 6.1 Model 2 Full Deployment Script

```powershell
# infrastructure/scripts/Deploy-Model2-Full.ps1

<#
.SYNOPSIS
    Deploys complete Spaarke infrastructure to a customer tenant (Model 2)

.DESCRIPTION
    This script deploys all Azure resources, creates app registrations,
    and configures the Spaarke solution for a customer-hosted deployment.

.PARAMETER CustomerId
    Unique customer identifier (lowercase, no spaces)

.PARAMETER SubscriptionId
    Azure subscription ID for deployment

.PARAMETER Location
    Azure region for deployment (default: eastus)

.PARAMETER DataverseUrl
    Customer's Dataverse environment URL

.EXAMPLE
    .\Deploy-Model2-Full.ps1 -CustomerId "contoso" -SubscriptionId "xxx-xxx" -DataverseUrl "https://contoso.crm.dynamics.com"
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^[a-z0-9]+$')]
    [string]$CustomerId,

    [Parameter(Mandatory)]
    [string]$SubscriptionId,

    [Parameter()]
    [string]$Location = "eastus",

    [Parameter(Mandatory)]
    [string]$DataverseUrl,

    [Parameter()]
    [ValidateSet('dev', 'staging', 'prod')]
    [string]$Environment = "prod"
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Spaarke Model 2 Deployment" -ForegroundColor Cyan
Write-Host "Customer: $CustomerId" -ForegroundColor Cyan
Write-Host "Environment: $Environment" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# 1. Set Azure context
Write-Host "`n[1/8] Setting Azure context..." -ForegroundColor Yellow
az account set --subscription $SubscriptionId
$tenantId = (az account show --query tenantId -o tsv)
Write-Host "Tenant ID: $tenantId" -ForegroundColor Green

# 2. Create App Registrations
Write-Host "`n[2/8] Creating App Registrations..." -ForegroundColor Yellow
$appRegResult = & "$PSScriptRoot/Register-AppRegistrations.ps1" `
    -CustomerId $CustomerId `
    -TenantId $tenantId
$bffAppId = $appRegResult.BffApiAppId
$bffAppSecret = $appRegResult.BffApiSecret
Write-Host "BFF API App ID: $bffAppId" -ForegroundColor Green

# 3. Deploy Azure Infrastructure (Bicep)
Write-Host "`n[3/8] Deploying Azure infrastructure..." -ForegroundColor Yellow
$deploymentName = "spaarke-$CustomerId-$(Get-Date -Format 'yyyyMMddHHmmss')"
$bicepOutput = az deployment sub create `
    --name $deploymentName `
    --location $Location `
    --template-file "$PSScriptRoot/../bicep/stacks/model2-full.bicep" `
    --parameters customerId=$CustomerId `
                 environment=$Environment `
                 location=$Location `
                 dataverseUrl=$DataverseUrl `
    --query properties.outputs -o json | ConvertFrom-Json

$resourceGroupName = $bicepOutput.resourceGroupName.value
$apiUrl = $bicepOutput.apiUrl.value
$keyVaultUri = $bicepOutput.keyVaultUri.value
$openAiEndpoint = $bicepOutput.openAiEndpoint.value
$searchEndpoint = $bicepOutput.searchEndpoint.value

Write-Host "Resource Group: $resourceGroupName" -ForegroundColor Green
Write-Host "API URL: $apiUrl" -ForegroundColor Green

# 4. Store secrets in Key Vault
Write-Host "`n[4/8] Storing secrets in Key Vault..." -ForegroundColor Yellow
$keyVaultName = "sprk$CustomerId$Environment-kv"
az keyvault secret set --vault-name $keyVaultName --name "bff-api-client-secret" --value $bffAppSecret
az keyvault secret set --vault-name $keyVaultName --name "tenant-id" --value $tenantId
az keyvault secret set --vault-name $keyVaultName --name "bff-api-client-id" --value $bffAppId
Write-Host "Secrets stored in Key Vault" -ForegroundColor Green

# 5. Create SPE ContainerType
Write-Host "`n[5/8] Creating SPE ContainerType..." -ForegroundColor Yellow
$containerTypeResult = & "$PSScriptRoot/Setup-SPE-ContainerType.ps1" `
    -CustomerId $CustomerId `
    -OwningAppId $bffAppId `
    -SubscriptionId $SubscriptionId `
    -ResourceGroup $resourceGroupName
$containerTypeId = $containerTypeResult.ContainerTypeId
Write-Host "ContainerType ID: $containerTypeId" -ForegroundColor Green

# 6. Configure App Service with secrets
Write-Host "`n[6/8] Configuring App Service..." -ForegroundColor Yellow
$appServiceName = "sprk$CustomerId$Environment-api"
az webapp config appsettings set `
    --resource-group $resourceGroupName `
    --name $appServiceName `
    --settings `
        "TENANT_ID=@Microsoft.KeyVault(VaultName=$keyVaultName;SecretName=tenant-id)" `
        "API_APP_ID=@Microsoft.KeyVault(VaultName=$keyVaultName;SecretName=bff-api-client-id)" `
        "API_CLIENT_SECRET=@Microsoft.KeyVault(VaultName=$keyVaultName;SecretName=bff-api-client-secret)" `
        "SPE_CONTAINER_TYPE_ID=$containerTypeId" `
        "OPENAI_ENDPOINT=$openAiEndpoint" `
        "AI_SEARCH_ENDPOINT=$searchEndpoint"
Write-Host "App Service configured" -ForegroundColor Green

# 7. Import Power Platform solutions
Write-Host "`n[7/8] Importing Power Platform solutions..." -ForegroundColor Yellow
& "$PSScriptRoot/../power-platform/setup/Import-Solutions.ps1" `
    -DataverseUrl $DataverseUrl `
    -SolutionPath "$PSScriptRoot/../power-platform/solutions"
Write-Host "Solutions imported" -ForegroundColor Green

# 8. Create Application User in Dataverse
Write-Host "`n[8/8] Creating Application User in Dataverse..." -ForegroundColor Yellow
& "$PSScriptRoot/../power-platform/setup/Create-ApplicationUser.ps1" `
    -DataverseUrl $DataverseUrl `
    -AppId $bffAppId
Write-Host "Application User created" -ForegroundColor Green

# Output summary
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "DEPLOYMENT COMPLETE" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host @"

Customer ID:        $CustomerId
Resource Group:     $resourceGroupName
API URL:            $apiUrl
Key Vault:          $keyVaultUri
OpenAI Endpoint:    $openAiEndpoint
AI Search:          $searchEndpoint
ContainerType ID:   $containerTypeId

Next Steps:
1. Deploy application code to App Service
2. Configure DNS (optional)
3. Run health checks: $apiUrl/health
4. Test Dataverse connectivity

"@ -ForegroundColor White

# Return deployment info
return @{
    CustomerId = $CustomerId
    ResourceGroup = $resourceGroupName
    ApiUrl = $apiUrl
    KeyVaultUri = $keyVaultUri
    OpenAiEndpoint = $openAiEndpoint
    SearchEndpoint = $searchEndpoint
    ContainerTypeId = $containerTypeId
    BffAppId = $bffAppId
}
```

---

## 7. Configuration Management

### 7.1 Environment Variables Strategy

| Variable | Source | Model 1 | Model 2 |
|----------|--------|---------|---------|
| `TENANT_ID` | Key Vault | Spaarke tenant | Customer tenant |
| `API_APP_ID` | Key Vault | Shared app | Customer app |
| `API_CLIENT_SECRET` | Key Vault | Per-env secret | Customer secret |
| `DATAVERSE_URL` | App Settings | Per-customer | Customer env |
| `SPE_CONTAINER_TYPE_ID` | App Settings | Shared | Customer-owned |
| `REDIS_CONNECTION_STRING` | Key Vault | Shared Redis | Customer Redis |
| `SERVICEBUS_CONNECTION` | Key Vault | Shared | Customer |
| `OPENAI_ENDPOINT` | App Settings | Shared | Customer BYOK |
| `AI_SEARCH_ENDPOINT` | App Settings | Shared | Customer |

### 7.2 Customer-Specific Configuration (Model 1)

For Model 1, customer-specific configuration is stored in Dataverse:

```
sprk_CustomerConfiguration entity:
├── sprk_customerid (lookup to Account)
├── sprk_specontainerid (SPE Container ID)
├── sprk_aisearchindex (Customer-specific index name)
├── sprk_aifeaturesenabled (JSON: enabled AI features)
└── sprk_usagetier (AI usage tier/limits)
```

---

## 8. Model-Specific Deployments

### 8.1 Model 1: Customer Onboarding

```powershell
# infrastructure/scripts/Deploy-Model1-Customer.ps1

# Creates:
# - SPE Container (in shared ContainerType)
# - AI Search Index (in shared AI Search)
# - Dataverse customer configuration record
# - Customer admin account (Guest user)
```

### 8.2 Model 2: Complete Deployment Package

The Model 2 package includes:

```
model2-deployment-package/
├── README.md                    # Step-by-step deployment guide
├── PREREQUISITES.md             # Required permissions, licenses
├── Deploy-Model2-Full.ps1       # Main deployment script
├── bicep/                       # All Bicep templates
├── power-platform/              # Solution files
├── validation/                  # Post-deployment tests
│   ├── Test-AzureResources.ps1
│   ├── Test-DataverseConnection.ps1
│   └── Test-SPEContainer.ps1
└── config/
    └── customer-template.json   # Config template to fill out
```

---

## 9. Implementation Roadmap

### Phase 1: Foundation (Week 1-2)

| Task | Priority | Effort |
|------|----------|--------|
| Create Bicep module structure | High | 2 days |
| Implement core modules (KV, Redis, SB) | High | 3 days |
| Implement App Service module | High | 1 day |
| Create Model 2 full stack Bicep | High | 2 days |
| Test deployment to dev subscription | High | 2 days |

### Phase 2: AI Resources (Week 3)

| Task | Priority | Effort |
|------|----------|--------|
| Implement OpenAI Bicep module | High | 1 day |
| Implement AI Search Bicep module | High | 1 day |
| Implement Document Intelligence module | Medium | 0.5 day |
| Add AI resources to stacks | High | 1 day |
| Test AI resource deployment | High | 1 day |

### Phase 3: Automation (Week 4)

| Task | Priority | Effort |
|------|----------|--------|
| Create App Registration script | High | 2 days |
| Create SPE ContainerType script | High | 1 day |
| Create Model 1 customer onboarding script | High | 2 days |
| Create Model 2 full deployment script | High | 2 days |
| Create validation/test scripts | Medium | 1 day |

### Phase 4: Documentation & Polish (Week 5)

| Task | Priority | Effort |
|------|----------|--------|
| Write Model 1 deployment guide | High | 1 day |
| Write Model 2 deployment guide | High | 1 day |
| Write customer onboarding guide | High | 1 day |
| Create deployment checklist | Medium | 0.5 day |
| Package Model 2 deployment kit | High | 1 day |

---

## Appendix A: Related ADRs

| ADR | Status | Description |
|-----|--------|-------------|
| ADR-012 | Proposed | Infrastructure Packaging Strategy |
| ADR-013 | Proposed | AI Provider Abstraction |
| ADR-014 | Proposed | Multi-Tenant Resource Isolation |

---

## Appendix B: References

- [Bicep Documentation](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/)
- [Azure Deployment Stacks](https://learn.microsoft.com/en-us/azure/azure-resource-manager/bicep/deployment-stacks)
- [Power Platform ALM](https://learn.microsoft.com/en-us/power-platform/alm/)
- [SharePoint Embedded Provisioning](https://learn.microsoft.com/en-us/sharepoint/dev/embedded/concepts/admin-exp/cta)

---

*Document Owner: Spaarke Engineering*  
*Review Cycle: Quarterly*
