// infrastructure/bicep/stacks/model2-full.bicep
// Complete Spaarke deployment for customer-hosted environments (Model 2)
// Deploys all Azure resources needed for a standalone Spaarke installation

targetScope = 'subscription'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Customer identifier (lowercase, alphanumeric only)')
@minLength(3)
@maxLength(10)
param customerId string

@description('Environment name')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'prod'

@description('Primary Azure region')
param location string = 'eastus'

@description('Dataverse environment URL')
param dataverseUrl string

@description('SPE ContainerType ID (created via script)')
param containerTypeId string = ''

@description('App Service Plan SKU')
@allowed(['B1', 'B2', 'S1', 'S2', 'P1v3'])
param appServiceSku string = 'S1'

@description('Azure AI Search SKU')
@allowed(['basic', 'standard', 'standard2'])
param aiSearchSku string = 'standard'

@description('SPE container/drive ID for communication email archival')
param communicationArchiveContainerId string = ''

@description('Default mailbox email address for communication')
param communicationDefaultMailbox string = ''

@description('Display name for the default communication mailbox')
param communicationDefaultDisplayName string = ''

@description('Tags applied to all resources')
param tags object = {
  customer: customerId
  environment: environment
  application: 'spaarke'
  deploymentModel: 'model2'
  managedBy: 'bicep'
}

// ============================================================================
// VARIABLES
// ============================================================================

var resourceGroupName = 'rg-spaarke-${customerId}-${environment}'
var baseName = 'sprk${customerId}${environment}'

// Ensure storage account name is valid (lowercase, no hyphens, max 24 chars)
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
// MONITORING (Deploy first - other resources reference it)
// ============================================================================

module monitoring '../modules/monitoring.bicep' = {
  scope: rg
  name: 'monitoring-${baseName}'
  params: {
    appInsightsName: '${baseName}-insights'
    logAnalyticsName: '${baseName}-logs'
    location: location
    tags: tags
  }
}

// ============================================================================
// KEY VAULT (Central secret management)
// ============================================================================

module keyVault '../modules/key-vault.bicep' = {
  scope: rg
  name: 'keyVault-${baseName}'
  params: {
    keyVaultName: '${baseName}-kv'
    location: location
    enableNetworkRestrictions: enableVnet
    allowedSubnetIds: enableVnet ? [vnet.outputs.snetAppId] : []
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsId
    tags: tags
  }
}

// ============================================================================
// REDIS CACHE (Token caching, session state)
// ============================================================================

module redis '../modules/redis.bicep' = {
  scope: rg
  name: 'redis-${baseName}'
  dependsOn: enableVnet ? [vnet] : []
  params: {
    redisName: '${baseName}-redis'
    location: location
    sku: enableVnet ? 'Premium' : 'Standard'
    capacity: 1
    subnetId: enableVnet ? vnet.outputs.snetRedisId : ''
    enableRdbPersistence: enableVnet  // RDB persistence when Premium (VNet-injected)
    tags: tags
  }
}

// ============================================================================
// SERVICE BUS (Job queue)
// ============================================================================

module serviceBus '../modules/service-bus.bicep' = {
  scope: rg
  name: 'serviceBus-${baseName}'
  params: {
    serviceBusName: '${baseName}-sb'
    location: location
    sku: 'Standard'
    queueNames: ['sdap-jobs', 'document-indexing', 'ai-indexing', 'sdap-communication']
    tags: tags
  }
}

// ============================================================================
// STORAGE ACCOUNT (Temp files, document processing)
// ============================================================================

module storage '../modules/storage-account.bicep' = {
  scope: rg
  name: 'storage-${baseName}'
  params: {
    storageAccountName: storageAccountName
    location: location
    sku: 'Standard_LRS'
    containers: ['temp-files', 'document-processing', 'ai-chunks']
    disableSharedKeyAccess: enableVnet  // Only disable shared keys when VNet is active (RBAC ready)
    allowedSubnetId: enableVnet ? vnet.outputs.snetAppId : ''
    appServicePrincipalId: bffApi.outputs.appServicePrincipalId
    tags: tags
  }
}

// ============================================================================
// APP SERVICE (Sprk.Bff.Api)
// ============================================================================

module appServicePlan '../modules/app-service-plan.bicep' = {
  scope: rg
  name: 'appServicePlan-${baseName}'
  params: {
    planName: '${baseName}-plan'
    location: location
    sku: appServiceSku
    os: 'Linux'
    tags: tags
  }
}

module bffApi '../modules/app-service.bicep' = {
  scope: rg
  name: 'bffApi-${baseName}'
  params: {
    appServiceName: '${baseName}-api'
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    keyVaultName: keyVault.outputs.keyVaultName
    enableManagedIdentity: true
    vnetIntegrationSubnetId: enableVnet ? vnet.outputs.snetAppId : ''
    appSettings: {
      // Dataverse configuration
      DATAVERSE_URL: dataverseUrl

      // SPE configuration
      SPE_CONTAINER_TYPE_ID: containerTypeId

      // Redis (Key Vault reference)
      Redis__Enabled: 'true'
      Redis__ConnectionString: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=redis-connection-string)'

      // Service Bus (Key Vault reference)
      ConnectionStrings__ServiceBus: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=servicebus-connection-string)'

      // Storage (Key Vault reference)
      ConnectionStrings__Storage: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=storage-connection-string)'

      // AI Services (Key Vault references)
      OPENAI_ENDPOINT: openAi.outputs.openAiEndpoint
      OPENAI_API_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=openai-api-key)'
      AI_SEARCH_ENDPOINT: aiSearch.outputs.searchServiceEndpoint
      AI_SEARCH_API_KEY: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=aisearch-admin-key)'

      // Monitoring
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.connectionString
      ApplicationInsightsAgent_EXTENSION_VERSION: '~3'

      // Communication (email processing)
      Communication__ArchiveContainerId: communicationArchiveContainerId
      Communication__DefaultMailbox: communicationDefaultMailbox
      Communication__WebhookNotificationUrl: 'https://${baseName}-api.azurewebsites.net/api/communications/incoming-webhook'
      Communication__WebhookClientState: '@Microsoft.KeyVault(VaultName=${keyVault.outputs.keyVaultName};SecretName=communication-webhook-secret)'
      Communication__ApprovedSenders__0__Email: communicationDefaultMailbox
      Communication__ApprovedSenders__0__DisplayName: communicationDefaultDisplayName
      Communication__ApprovedSenders__0__IsDefault: 'true'

      // Service Bus queue name for communication jobs
      ServiceBus__CommunicationQueueName: 'sdap-communication'
    }
    tags: tags
  }
}

// ============================================================================
// KEY VAULT RBAC — App Service Secrets Access
// Deployed after App Service so managed identity principal ID is available.
// Grants "Key Vault Secrets User" (read-only) for Key Vault reference resolution.
// ============================================================================

var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

module kvRbacAppService '../modules/role-assignment-keyvault.bicep' = {
  scope: rg
  name: 'kvRbac-${baseName}'
  params: {
    keyVaultName: keyVault.outputs.keyVaultName
    principalId: bffApi.outputs.appServicePrincipalId
    roleDefinitionId: keyVaultSecretsUserRoleId
    principalType: 'ServicePrincipal'
  }
}

// ============================================================================
// AUTOSCALING (App Service Plan)
// ============================================================================

@description('Enable autoscaling for the App Service Plan')
param enableAutoscaling bool = true

@description('Minimum instance count for autoscaling')
@minValue(1)
@maxValue(20)
param autoscaleMinInstances int = 2

@description('Maximum instance count for autoscaling')
@minValue(1)
@maxValue(20)
param autoscaleMaxInstances int = 10

module autoscale '../modules/autoscale.bicep' = if (enableAutoscaling) {
  scope: rg
  name: 'autoscale-${baseName}'
  params: {
    autoscaleSettingName: '${baseName}-autoscale'
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    minInstanceCount: autoscaleMinInstances
    maxInstanceCount: autoscaleMaxInstances
    defaultInstanceCount: autoscaleMinInstances
    tags: tags
  }
}

// ============================================================================
// AI SERVICES
// ============================================================================

module openAi '../modules/openai.bicep' = {
  scope: rg
  name: 'openAi-${baseName}'
  params: {
    openAiName: '${baseName}-openai'
    location: location
    // Network hardening: disable public access when VNet + private endpoints are active
    disablePublicNetworkAccess: enableVnet
    deployments: [
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
        capacity: 200 // >= 200 TPM required for beta scale
      }
      {
        name: 'text-embedding-3-large'
        model: 'text-embedding-3-large'
        version: '1'
        capacity: 200
      }
      // text-embedding-3-small removed (deprecated, replaced by text-embedding-3-large)
    ]
    tags: tags
  }
}

module aiSearch '../modules/ai-search.bicep' = {
  scope: rg
  name: 'aiSearch-${baseName}'
  params: {
    searchServiceName: '${baseName}-search'
    location: location
    sku: aiSearchSku
    replicaCount: 2
    partitionCount: 1
    semanticSearch: 'standard'
    tags: tags
  }
}

module docIntelligence '../modules/doc-intelligence.bicep' = {
  scope: rg
  name: 'docIntelligence-${baseName}'
  params: {
    docIntelligenceName: '${baseName}-docintel'
    location: location
    sku: 'S0'
    tags: tags
  }
}

// ============================================================================
// AI FOUNDRY (Optional - for Analysis feature Prompt Flow orchestration)
// ============================================================================

@description('Enable AI Foundry Hub and Project deployment')
param enableAiFoundry bool = false

@description('SKU for AI Foundry workspace')
@allowed(['Basic', 'Standard'])
param aiFoundrySku string = 'Basic'

@description('Enable VNet and private endpoints for network isolation')
param enableVnet bool = false

@description('VNet address space CIDR')
param vnetAddressPrefix string = '10.0.0.0/16'

@description('Enable AI Monitoring Dashboard deployment')
param enableMonitoringDashboard bool = true

@description('Alert notification email address')
param alertNotificationEmail string = ''

module aiFoundry '../modules/ai-foundry-hub.bicep' = if (enableAiFoundry) {
  scope: rg
  name: 'aiFoundry-${baseName}'
  params: {
    hubName: '${baseName}-aif-hub'
    projectName: '${baseName}-aif-proj'
    location: location
    storageAccountId: storage.outputs.storageAccountId
    keyVaultId: keyVault.outputs.keyVaultId
    appInsightsId: monitoring.outputs.appInsightsId
    openAiResourceId: openAi.outputs.openAiId
    aiSearchResourceId: aiSearch.outputs.searchServiceId
    sku: aiFoundrySku
    publicNetworkAccess: 'Enabled'
    tags: tags
  }
}

// ============================================================================
// VNET + PRIVATE ENDPOINTS (Optional — deploy alongside public first, test,
// then disable public access on individual services)
// ============================================================================

module vnet '../modules/vnet.bicep' = if (enableVnet) {
  scope: rg
  name: 'vnet-${baseName}'
  params: {
    vnetName: '${baseName}-vnet'
    location: location
    addressPrefix: vnetAddressPrefix
    tags: tags
  }
}

module privateEndpoints '../modules/private-endpoints.bicep' = if (enableVnet) {
  scope: rg
  name: 'privateEndpoints-${baseName}'
  params: {
    baseName: baseName
    location: location
    vnetId: enableVnet ? vnet.outputs.vnetId : ''
    privateEndpointSubnetId: enableVnet ? vnet.outputs.snetPeId : ''
    keyVaultId: keyVault.outputs.keyVaultId
    storageAccountId: storage.outputs.storageAccountId
    serviceBusId: serviceBus.outputs.serviceBusId
    openAiId: openAi.outputs.openAiId
    aiSearchId: aiSearch.outputs.searchServiceId
    docIntelligenceId: docIntelligence.outputs.docIntelligenceId
    tags: tags
  }
}

// ============================================================================
// MONITORING DASHBOARD & ALERTS
// ============================================================================

module aiDashboard '../modules/dashboard.bicep' = if (enableMonitoringDashboard) {
  scope: rg
  name: 'dashboard-${baseName}'
  params: {
    dashboardName: '${baseName}-ai-dashboard'
    location: location
    appInsightsId: monitoring.outputs.appInsightsId
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsId
    tags: tags
  }
}

module aiAlerts '../modules/alerts.bicep' = if (enableMonitoringDashboard && !empty(alertNotificationEmail)) {
  scope: rg
  name: 'alerts-${baseName}'
  params: {
    alertNamePrefix: baseName
    location: location
    appInsightsId: monitoring.outputs.appInsightsId
    actionGroupId: '' // Will create default action group
    tags: tags
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output resourceGroupName string = rg.name
output location string = location

// API
output apiUrl string = bffApi.outputs.appServiceUrl
output apiPrincipalId string = bffApi.outputs.appServicePrincipalId

// Key Vault
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri

// AI Services
output openAiEndpoint string = openAi.outputs.openAiEndpoint
output aiSearchEndpoint string = aiSearch.outputs.searchServiceEndpoint
output docIntelligenceEndpoint string = docIntelligence.outputs.docIntelligenceEndpoint

// Monitoring
output appInsightsName string = monitoring.outputs.appInsightsName
output appInsightsConnectionString string = monitoring.outputs.connectionString

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

// AI Foundry (when enabled)
output aiFoundryEnabled bool = enableAiFoundry
output aiFoundryHubId string = enableAiFoundry ? aiFoundry.outputs.hubId : ''
output aiFoundryProjectId string = enableAiFoundry ? aiFoundry.outputs.projectId : ''
output promptFlowEndpoint string = enableAiFoundry ? aiFoundry.outputs.promptFlowEndpoint : ''
output aiFoundryPortalUrl string = enableAiFoundry ? aiFoundry.outputs.aiFoundryPortalUrl : ''

// Dashboard
output dashboardId string = enableMonitoringDashboard ? aiDashboard.outputs.dashboardId : ''
output dashboardName string = enableMonitoringDashboard ? aiDashboard.outputs.dashboardName : ''

// Autoscaling
output autoscaleEnabled bool = enableAutoscaling
output autoscaleSettingId string = enableAutoscaling ? autoscale.outputs.autoscaleSettingId : ''

// VNet + Private Endpoints (when enabled)
output vnetEnabled bool = enableVnet
output vnetId string = enableVnet ? vnet.outputs.vnetId : ''
output snetAppId string = enableVnet ? vnet.outputs.snetAppId : ''
output snetRedisId string = enableVnet ? vnet.outputs.snetRedisId : ''
output snetPeId string = enableVnet ? vnet.outputs.snetPeId : ''
