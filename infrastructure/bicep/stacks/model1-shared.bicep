// infrastructure/bicep/stacks/model1-shared.bicep
// Shared Spaarke infrastructure for multi-tenant hosted environments (Model 1)
// Deploys shared Azure resources that serve multiple customers

targetScope = 'subscription'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Environment name')
@allowed(['dev', 'staging', 'prod'])
param environment string

@description('Primary Azure region')
param location string = 'eastus'

@description('App Service Plan SKU (shared across customers)')
@allowed(['S1', 'S2', 'S3', 'P1v3', 'P2v3', 'P3v3'])
param appServiceSku string = 'S1'

@description('Azure AI Search SKU (shared, multi-index)')
@allowed(['standard', 'standard2', 'standard3'])
param aiSearchSku string = 'standard'

@description('Redis Cache SKU (shared)')
@allowed(['Standard', 'Premium'])
param redisSku string = 'Standard'

@description('Tags applied to all resources')
param tags object = {
  environment: environment
  application: 'spaarke'
  deploymentModel: 'model1'
  managedBy: 'bicep'
}

// ============================================================================
// VARIABLES
// ============================================================================

var resourceGroupName = 'rg-spaarke-shared-${environment}'
var baseName = 'sprkshared${environment}'

// Ensure storage account name is valid
var storageAccountName = take(toLower(replace('${baseName}sa', '-', '')), 24)

// ============================================================================
// RESOURCE GROUP
// ============================================================================

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ============================================================================
// MONITORING (Shared - all customers)
// ============================================================================

module monitoring '../modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring-shared'
  params: {
    appInsightsName: '${baseName}-insights'
    logAnalyticsName: '${baseName}-logs'
    location: location
    retentionInDays: 180  // Longer retention for multi-tenant
    tags: tags
  }
}

// ============================================================================
// KEY VAULT (Shared secrets + per-customer secrets)
// ============================================================================

module keyVault '../modules/key-vault.bicep' = {
  scope: rg
  name: 'keyVault-shared'
  params: {
    keyVaultName: '${baseName}-kv'
    location: location
    sku: 'standard'
    tags: tags
  }
}

// ============================================================================
// REDIS CACHE (Shared - partitioned by customer prefix)
// ============================================================================

module redis '../modules/redis.bicep' = {
  scope: rg
  name: 'redis-shared'
  params: {
    redisName: '${baseName}-redis'
    location: location
    sku: redisSku
    capacity: redisSku == 'Premium' ? 1 : 2  // C2 for Standard, P1 for Premium
    tags: tags
  }
}

// ============================================================================
// SERVICE BUS (Shared namespace)
// ============================================================================

module serviceBus '../modules/service-bus.bicep' = {
  scope: rg
  name: 'serviceBus-shared'
  params: {
    serviceBusName: '${baseName}-sb'
    location: location
    sku: 'Standard'
    queueNames: [
      'sdap-jobs'
      'document-indexing'
      'ai-indexing'
      'customer-onboarding'
    ]
    tags: tags
  }
}

// ============================================================================
// STORAGE ACCOUNT (Shared - partitioned by container)
// ============================================================================

module storage '../modules/storage-account.bicep' = {
  scope: rg
  name: 'storage-shared'
  params: {
    storageAccountName: storageAccountName
    location: location
    sku: 'Standard_GRS'  // Geo-redundant for production
    containers: [
      'temp-files'
      'document-processing'
      'ai-chunks'
      'customer-exports'
    ]
    tags: tags
  }
}

// ============================================================================
// APP SERVICE PLAN (Shared compute)
// ============================================================================

module appServicePlan '../modules/app-service-plan.bicep' = {
  scope: rg
  name: 'appServicePlan-shared'
  params: {
    planName: '${baseName}-plan'
    location: location
    sku: appServiceSku
    os: 'Linux'
    tags: tags
  }
}

// ============================================================================
// SPRK.BFF.API (Single deployment serving all customers)
// ============================================================================

module bffApi '../modules/app-service.bicep' = {
  scope: rg
  name: 'bffApi-shared'
  params: {
    appServiceName: '${baseName}-api'
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    keyVaultName: keyVault.outputs.keyVaultName
    enableManagedIdentity: true
    appSettings: {
      // Multi-tenant mode
      MULTI_TENANT_MODE: 'true'

      // Redis (shared)
      Redis__Enabled: 'true'
      Redis__ConnectionString: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=redis-connection-string)'
      Redis__InstanceName: 'spaarke:'  // Prefix for key isolation

      // Service Bus
      ConnectionStrings__ServiceBus: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=servicebus-connection-string)'

      // Storage
      ConnectionStrings__Storage: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=storage-connection-string)'

      // AI Services (shared)
      OPENAI_ENDPOINT: openAi.outputs.openAiEndpoint
      OPENAI_API_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=openai-api-key)'
      AI_SEARCH_ENDPOINT: aiSearch.outputs.searchServiceEndpoint
      AI_SEARCH_API_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=aisearch-admin-key)'

      // Document Intelligence
      DOC_INTELLIGENCE_ENDPOINT: docIntelligence.outputs.docIntelligenceEndpoint
      DOC_INTELLIGENCE_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=docintel-key)'

      // Monitoring
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.connectionString
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'
    }
    tags: tags
  }
}

// ============================================================================
// AI SERVICES (Shared - multi-tenant)
// ============================================================================

module openAi '../modules/openai.bicep' = {
  scope: rg
  name: 'openAi-shared'
  params: {
    openAiName: '${baseName}-openai'
    location: location
    deployments: [
      {
        name: 'gpt-4o'
        model: 'gpt-4o'
        version: '2024-08-06'
        capacity: 150  // Higher capacity for multi-tenant
      }
      {
        name: 'gpt-4o-mini'
        model: 'gpt-4o-mini'
        version: '2024-07-18'
        capacity: 200
      }
      {
        name: 'text-embedding-3-large'
        model: 'text-embedding-3-large'
        version: '1'
        capacity: 350
      }
    ]
    tags: tags
  }
}

module aiSearch '../modules/ai-search.bicep' = {
  scope: rg
  name: 'aiSearch-shared'
  params: {
    searchServiceName: '${baseName}-search'
    location: location
    sku: aiSearchSku
    replicaCount: environment == 'prod' ? 2 : 1  // HA for production
    partitionCount: 1
    semanticSearch: 'standard'
    tags: tags
  }
}

module docIntelligence '../modules/doc-intelligence.bicep' = {
  scope: rg
  name: 'docIntelligence-shared'
  params: {
    docIntelligenceName: '${baseName}-docintel'
    location: location
    sku: 'S0'
    tags: tags
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output resourceGroupName string = rg.name
output location string = location
output environment string = environment

// API
output apiUrl string = bffApi.outputs.appServiceUrl
output apiPrincipalId string = bffApi.outputs.appServicePrincipalId
output appServicePlanId string = appServicePlan.outputs.planId

// Key Vault
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri

// AI Services
output openAiEndpoint string = openAi.outputs.openAiEndpoint
output aiSearchEndpoint string = aiSearch.outputs.searchServiceEndpoint
output docIntelligenceEndpoint string = docIntelligence.outputs.docIntelligenceEndpoint

// Monitoring
output appInsightsName string = monitoring.outputs.appInsightsName
output logAnalyticsWorkspaceId string = monitoring.outputs.logAnalyticsWorkspaceId

// Connection strings (store in Key Vault via post-deployment script)
#disable-next-line outputs-should-not-contain-secrets
output redisConnectionString string = redis.outputs.redisConnectionString
#disable-next-line outputs-should-not-contain-secrets
output serviceBusConnectionString string = serviceBus.outputs.serviceBusConnectionString
#disable-next-line outputs-should-not-contain-secrets
output storageConnectionString string = storage.outputs.connectionString
#disable-next-line outputs-should-not-contain-secrets
output openAiKey string = openAi.outputs.openAiKey
#disable-next-line outputs-should-not-contain-secrets
output aiSearchAdminKey string = aiSearch.outputs.searchServiceAdminKey
#disable-next-line outputs-should-not-contain-secrets
output docIntelligenceKey string = docIntelligence.outputs.docIntelligenceKey
