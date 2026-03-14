// infrastructure/bicep/platform.bicep
// Shared Spaarke Platform — deploys all shared infrastructure to rg-spaarke-platform-{env}
// This is the "deploy once" template for the shared platform that all customers share.
//
// Resources deployed:
//   1. App Service Plan (P1v3)
//   2. App Service (Sprk.Bff.Api) with staging slot, health check at /healthz
//   3. Azure OpenAI (GPT-4o, GPT-4o-mini, text-embedding-3-large)
//   4. AI Search (Standard2, 2 replicas)
//   5. Document Intelligence (S0)
//   6. App Insights + Log Analytics (180-day retention)
//   7. Platform Key Vault
//
// Usage:
//   az deployment sub create \
//     --location westus2 \
//     --template-file infrastructure/bicep/platform.bicep \
//     --parameters infrastructure/bicep/platform-prod.bicepparam

targetScope = 'subscription'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Environment name (dev, staging, prod)')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Primary Azure region')
param location string = 'westus2'

@description('Azure region for OpenAI deployment (may differ from primary location due to model availability)')
param openAiLocation string = 'westus3'

@description('App Service Plan SKU')
@allowed(['S1', 'S2', 'P1v3', 'P2v3', 'P3v3'])
param appServicePlanSku string = 'P1v3'

@description('Azure OpenAI model deployments configuration')
param openAiDeployments array = [
  {
    name: 'gpt-4o'
    model: 'gpt-4o'
    version: '2024-08-06'
    capacity: 80
  }
  {
    name: 'gpt-4o-mini'
    model: 'gpt-4o-mini'
    version: '2024-07-18'
    capacity: 120
  }
  {
    name: 'text-embedding-3-large'
    model: 'text-embedding-3-large'
    version: '1'
    capacity: 200
  }
]

@description('Azure AI Search SKU')
@allowed(['standard', 'standard2', 'standard3'])
param aiSearchSku string = 'standard2'

@description('Azure AI Search replica count (2+ for HA)')
@minValue(1)
@maxValue(12)
param aiSearchReplicaCount int = 2

@description('Log Analytics retention in days')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 180

@description('Tags applied to all resources')
param tags object = {
  environment: environmentName
  application: 'spaarke'
  layer: 'platform'
  managedBy: 'bicep'
}

// ============================================================================
// VARIABLES — Naming Convention (sprk_/spaarke- standard)
// ============================================================================

var resourceGroupName = 'rg-spaarke-platform-${environmentName}'

// Long-form names (spaarke- prefix) for resources with generous limits
var appServicePlanName = 'spaarke-bff-${environmentName}-plan'
var appServiceName = 'spaarke-bff-${environmentName}'
var openAiName = 'spaarke-openai-${environmentName}'
var aiSearchName = 'spaarke-search-${environmentName}'
var docIntelligenceName = 'spaarke-docintel-${environmentName}'
var appInsightsName = 'sprk-platform-${environmentName}-insights'
var logAnalyticsName = 'sprk-platform-${environmentName}-logs'

// Short-form names (sprk- prefix) for length-constrained resources (Key Vault: 24 chars)
var keyVaultName = 'sprk-platform-${environmentName}-kv'

// ============================================================================
// RESOURCE GROUP
// ============================================================================

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ============================================================================
// 1. MONITORING (Deploy first — other resources reference App Insights)
// ============================================================================

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'platform-monitoring'
  params: {
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
    location: location
    retentionInDays: logRetentionDays
    tags: tags
  }
}

// ============================================================================
// 2. PLATFORM KEY VAULT (Central secret management for shared platform)
// ============================================================================

module keyVault 'modules/key-vault.bicep' = {
  scope: rg
  name: 'platform-keyvault'
  params: {
    keyVaultName: keyVaultName
    location: location
    sku: 'standard'
    tags: tags
  }
}

// ============================================================================
// 3. APP SERVICE PLAN (Shared compute — P1v3 for production)
// ============================================================================

module appServicePlan 'modules/app-service-plan.bicep' = {
  scope: rg
  name: 'platform-appserviceplan'
  params: {
    planName: appServicePlanName
    location: location
    sku: appServicePlanSku
    os: 'Linux'
    tags: tags
  }
}

// ============================================================================
// 4. APP SERVICE — Sprk.Bff.Api (with staging slot + /healthz health check)
// ============================================================================

module bffApi 'modules/app-service.bicep' = {
  scope: rg
  name: 'platform-bffapi'
  params: {
    appServiceName: appServiceName
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    keyVaultName: keyVault.outputs.keyVaultName
    enableManagedIdentity: true
    appSettings: {
      // AI Services — endpoints only; keys via Key Vault references (FR-08)
      OPENAI_ENDPOINT: openAi.outputs.openAiEndpoint
      OPENAI_API_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=openai-api-key)'
      AI_SEARCH_ENDPOINT: aiSearch.outputs.searchServiceEndpoint
      AI_SEARCH_API_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=aisearch-admin-key)'
      DOC_INTELLIGENCE_ENDPOINT: docIntelligence.outputs.docIntelligenceEndpoint
      DOC_INTELLIGENCE_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=docintel-key)'

      // Monitoring
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.connectionString
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    }
    tags: tags
  }
}

// Override health check path to /healthz (module defaults to /health)
// and configure platform-specific site settings
module bffApiConfig 'modules/app-service-config.bicep' = {
  scope: rg
  name: 'platform-bffapi-config'
  params: {
    appServiceName: bffApi.outputs.appServiceName
    healthCheckPath: '/healthz'
  }
}

// Staging deployment slot for zero-downtime deployments (NFR-02)
module bffApiStagingSlot 'modules/app-service-slot.bicep' = {
  scope: rg
  name: 'platform-bffapi-staging'
  params: {
    appServiceName: bffApi.outputs.appServiceName
    slotName: 'staging'
    location: location
    appServicePlanId: appServicePlan.outputs.planId
    healthCheckPath: '/healthz'
    tags: tags
  }
}

// ============================================================================
// 5. AZURE OPENAI (GPT-4o, GPT-4o-mini, text-embedding-3-large)
// ============================================================================

module openAi 'modules/openai.bicep' = {
  scope: rg
  name: 'platform-openai'
  params: {
    openAiName: openAiName
    location: openAiLocation
    deployments: openAiDeployments
    tags: tags
  }
}

// ============================================================================
// 6. AI SEARCH (Standard2, 2 replicas for HA)
// ============================================================================

module aiSearch 'modules/ai-search.bicep' = {
  scope: rg
  name: 'platform-aisearch'
  params: {
    searchServiceName: aiSearchName
    location: location
    sku: aiSearchSku
    replicaCount: aiSearchReplicaCount
    partitionCount: 1
    semanticSearch: 'standard'
    tags: tags
  }
}

// ============================================================================
// 7. DOCUMENT INTELLIGENCE (S0)
// ============================================================================

module docIntelligence 'modules/doc-intelligence.bicep' = {
  scope: rg
  name: 'platform-docintelligence'
  params: {
    docIntelligenceName: docIntelligenceName
    location: location
    sku: 'S0'
    tags: tags
  }
}

// ============================================================================
// OUTPUTS — Exported for customer.bicep and deployment scripts
// ============================================================================

// Resource Group
output resourceGroupName string = rg.name
output location string = location
output environmentName string = environmentName

// App Service
output apiUrl string = bffApi.outputs.appServiceUrl
output apiDefaultHostName string = bffApi.outputs.appServiceDefaultHostName
output apiPrincipalId string = bffApi.outputs.appServicePrincipalId
output appServicePlanId string = appServicePlan.outputs.planId

// Key Vault
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri

// AI Services — endpoints only (no keys in outputs per FR-08)
output openAiEndpoint string = openAi.outputs.openAiEndpoint
output openAiName string = openAi.outputs.openAiName
output aiSearchEndpoint string = aiSearch.outputs.searchServiceEndpoint
output aiSearchName string = aiSearch.outputs.searchServiceName
output docIntelligenceEndpoint string = docIntelligence.outputs.docIntelligenceEndpoint
output docIntelligenceName string = docIntelligence.outputs.docIntelligenceName

// Monitoring
output appInsightsName string = monitoring.outputs.appInsightsName
output appInsightsConnectionString string = monitoring.outputs.connectionString
output logAnalyticsWorkspaceId string = monitoring.outputs.logAnalyticsWorkspaceId
