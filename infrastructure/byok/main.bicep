// infrastructure/byok/main.bicep
// BYOK (Bring Your Own Key) Deployment Template for Spaarke AI Stack
// Deploys the complete Spaarke AI platform into a customer's Azure subscription.
//
// This template provides full data sovereignty — all AI resources, secrets,
// caching, and telemetry remain within the customer's tenant boundary.
//
// Resources deployed:
//   - App Service Plan + App Service (BFF API — Linux, .NET 8)
//   - Azure OpenAI (customer-controlled models and data boundary)
//   - Azure AI Search (physical data isolation for vector search)
//   - Azure Bot Service (M365 Copilot agent with Teams channel)
//   - Azure Cache for Redis (session/token caching per ADR-009)
//   - Azure Key Vault (secrets management with RBAC)
//   - Application Insights + Log Analytics (telemetry)
//
// Usage:
//   az deployment group create \
//     --resource-group rg-spaarke-byok-prod \
//     --template-file infrastructure/byok/main.bicep \
//     --parameters infrastructure/byok/parameters.template.json \
//     --parameters environmentName=prod location=westus2

targetScope = 'resourceGroup'

// ============================================================================
// PARAMETERS — Core
// ============================================================================

@description('Environment name — drives naming conventions and default SKU sizing')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Primary Azure region for all resources')
param location string = resourceGroup().location

@description('Customer\'s Dataverse environment URL (e.g., https://contoso.crm.dynamics.com)')
param dataverseUrl string

@description('Microsoft App ID from the customer\'s Entra app registration for the Bot Service')
param botAppId string

// ============================================================================
// PARAMETERS — Compute
// ============================================================================

@description('App Service Plan SKU')
@allowed(['B1', 'B2', 'B3', 'S1', 'S2', 'S3', 'P1v3', 'P2v3', 'P3v3'])
param appServicePlanSku string = 'S1'

// ============================================================================
// PARAMETERS — AI
// ============================================================================

@description('Azure OpenAI model deployments. Each entry specifies model name, version, and capacity (TPM in thousands).')
param openAiModelDeployments array = [
  {
    name: 'gpt-4o'
    model: 'gpt-4o'
    version: '2024-08-06'
    capacity: 150
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

@description('Azure AI Search SKU')
@allowed(['basic', 'standard', 'standard2', 'standard3'])
param aiSearchSku string = 'standard'

@description('Azure AI Search replica count (2+ for production HA)')
@minValue(1)
@maxValue(12)
param aiSearchReplicaCount int = 1

// ============================================================================
// PARAMETERS — Caching
// ============================================================================

@description('Redis Cache SKU')
@allowed(['Basic', 'Standard', 'Premium'])
param redisSku string = 'Standard'

@description('Redis Cache capacity (C-family size for Basic/Standard: 0-6, P-family for Premium: 1-5)')
param redisCapacity int = 1

// ============================================================================
// PARAMETERS — Bot Service
// ============================================================================

@description('Bot Service SKU')
@allowed(['F0', 'S1'])
param botServiceSku string = 'F0'

@description('Bot display name shown in Teams and M365 Copilot')
param botDisplayName string = 'Spaarke AI Assistant'

// ============================================================================
// PARAMETERS — Monitoring
// ============================================================================

@description('Log Analytics retention period in days')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 90

// ============================================================================
// PARAMETERS — Tags
// ============================================================================

@description('Tags applied to all resources for cost tracking and governance')
param tags object = {
  environment: environmentName
  application: 'spaarke'
  deploymentModel: 'byok'
  managedBy: 'bicep'
  createdDate: utcNow('yyyy-MM-dd')
}

// ============================================================================
// VARIABLES
// ============================================================================

// Base name for resource naming: sprk-byok-{env}
var baseName = 'sprk-byok-${environmentName}'

// Unique suffix derived from resource group ID for globally unique names
var uniqueSuffix = uniqueString(resourceGroup().id)

// Resource names following Spaarke naming conventions
var appServicePlanName = '${baseName}-plan'
var appServiceName = '${baseName}-api-${take(uniqueSuffix, 6)}'
var openAiName = '${baseName}-openai'
var aiSearchName = '${baseName}-search'
var botServiceName = '${baseName}-bot'
var redisName = '${baseName}-cache'
var keyVaultName = take('sprk-byok-${environmentName}-kv', 24)
var appInsightsName = '${baseName}-insights'
var logAnalyticsName = '${baseName}-logs'

// Redis SKU family mapping
var redisSkuFamilies = {
  Basic: 'C'
  Standard: 'C'
  Premium: 'P'
}

// App Service Plan tier mapping
var appServicePlanTiers = {
  B1: 'Basic'
  B2: 'Basic'
  B3: 'Basic'
  S1: 'Standard'
  S2: 'Standard'
  S3: 'Standard'
  P1v3: 'PremiumV3'
  P2v3: 'PremiumV3'
  P3v3: 'PremiumV3'
}

// ============================================================================
// MONITORING (Deploy first — other resources reference Log Analytics)
// ============================================================================

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: logRetentionDays
    features: {
      enableLogAccessUsingOnlyResourcePermissions: true
    }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: logAnalytics.id
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery: 'Enabled'
  }
}

// ============================================================================
// KEY VAULT (Deploy early — App Service stores secret references here)
// ============================================================================

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: 'standard'
    }
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
    enableRbacAuthorization: true
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// Key Vault diagnostic settings — audit logging
resource keyVaultDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: '${keyVaultName}-diagnostics'
  scope: keyVault
  properties: {
    workspaceId: logAnalytics.id
    logs: [
      {
        categoryGroup: 'audit'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 90
        }
      }
    ]
    metrics: [
      {
        category: 'AllMetrics'
        enabled: true
        retentionPolicy: {
          enabled: true
          days: 30
        }
      }
    ]
  }
}

// ============================================================================
// AZURE OPENAI (Customer-controlled AI models and data boundary)
// ============================================================================

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: location
  tags: tags
  kind: 'OpenAI'
  sku: {
    name: 'S0'
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
    }
  }
}

@batchSize(1)
resource openAiDeployments 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = [for deployment in openAiModelDeployments: {
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
    raiPolicyName: 'Microsoft.Default'
  }
}]

// ============================================================================
// AZURE AI SEARCH (Customer's physical data isolation for vector search)
// ============================================================================

resource aiSearch 'Microsoft.Search/searchServices@2023-11-01' = {
  name: aiSearchName
  location: location
  tags: tags
  sku: {
    name: aiSearchSku
  }
  properties: {
    replicaCount: aiSearchReplicaCount
    partitionCount: 1
    hostingMode: 'default'
    semanticSearch: 'standard'
    publicNetworkAccess: 'enabled'
  }
}

// ============================================================================
// REDIS CACHE (Session/token caching per ADR-009)
// ============================================================================

resource redisCache 'Microsoft.Cache/redis@2023-08-01' = {
  name: redisName
  location: location
  tags: tags
  properties: {
    sku: {
      name: redisSku
      family: redisSkuFamilies[redisSku]
      capacity: redisCapacity
    }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    redisConfiguration: {
      'maxmemory-policy': 'allkeys-lru'
    }
  }
}

// ============================================================================
// APP SERVICE PLAN + APP SERVICE (BFF API — Linux, .NET 8)
// ============================================================================

resource appServicePlan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: appServicePlanName
  location: location
  tags: tags
  kind: 'linux'
  sku: {
    name: appServicePlanSku
    tier: appServicePlanTiers[appServicePlanSku]
  }
  properties: {
    reserved: true // Required for Linux
  }
}

resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: appServicePlan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|8.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsights.properties.ConnectionString
        }
        {
          name: 'ASPNETCORE_ENVIRONMENT'
          value: environmentName == 'prod' ? 'Production' : (environmentName == 'staging' ? 'Staging' : 'Development')
        }
        {
          name: 'KeyVault__VaultUri'
          value: keyVault.properties.vaultUri
        }
        {
          name: 'AzureOpenAI__Endpoint'
          value: openAi.properties.endpoint
        }
        {
          name: 'AzureOpenAI__DeploymentName'
          value: 'gpt-4o'
        }
        {
          name: 'AzureAISearch__Endpoint'
          value: 'https://${aiSearch.name}.search.windows.net'
        }
        {
          name: 'Redis__ConnectionString'
          value: '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'
        }
        {
          name: 'Dataverse__Url'
          value: dataverseUrl
        }
        {
          name: 'Bot__AppId'
          value: botAppId
        }
      ]
    }
  }
}

// Grant App Service managed identity "Key Vault Secrets User" role
var keyVaultSecretsUserRoleId = '4633458b-17de-408a-b874-0445c86b69e6'

resource appServiceKeyVaultRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, appService.id, keyVaultSecretsUserRoleId)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUserRoleId)
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant App Service managed identity "Cognitive Services OpenAI User" role on OpenAI
var cognitiveServicesOpenAiUserRoleId = '5e0bd9bd-7b93-4f28-af87-19fc36ad61bd'

resource appServiceOpenAiRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(openAi.id, appService.id, cognitiveServicesOpenAiUserRoleId)
  scope: openAi
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesOpenAiUserRoleId)
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// Grant App Service managed identity "Search Index Data Contributor" role on AI Search
var searchIndexDataContributorRoleId = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'

resource appServiceSearchRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(aiSearch.id, appService.id, searchIndexDataContributorRoleId)
  scope: aiSearch
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleId)
    principalId: appService.identity.principalId
    principalType: 'ServicePrincipal'
  }
}

// ============================================================================
// BOT SERVICE (M365 Copilot agent with Teams and DirectLine channels)
// ============================================================================

resource botService 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botServiceName
  location: location
  kind: 'azurebot'
  tags: tags
  sku: {
    name: botServiceSku
  }
  properties: {
    displayName: botDisplayName
    description: 'Spaarke AI-powered document assistant for M365 Copilot integration'
    endpoint: 'https://${appService.properties.defaultHostName}/api/messages'
    msaAppId: botAppId
    msaAppType: 'SingleTenant'
    msaAppTenantId: tenant().tenantId
    schemaTransformationVersion: '1.3'
    disableLocalAuth: true
    isStreamingSupported: true
  }
}

resource teamsChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: botService
  name: 'MsTeamsChannel'
  location: location
  properties: {
    channelName: 'MsTeamsChannel'
    properties: {
      isEnabled: true
      enableCalling: false
      acceptedTerms: true
    }
  }
}

resource directLineChannel 'Microsoft.BotService/botServices/channels@2022-09-15' = {
  parent: botService
  name: 'DirectLineChannel'
  location: location
  properties: {
    channelName: 'DirectLineChannel'
    properties: {
      extensionKey1: ''
      extensionKey2: ''
      sites: [
        {
          siteName: 'Default'
          isEnabled: true
          isV1Enabled: false
          isV3Enabled: true
          isSecureSiteEnabled: true
          trustedOrigins: [
            'https://${appService.properties.defaultHostName}'
          ]
        }
      ]
    }
  }
}

// ============================================================================
// KEY VAULT SECRETS (Store connection strings and keys)
// ============================================================================

resource secretOpenAiKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'openai-api-key'
  properties: {
    value: openAi.listKeys().key1
  }
}

resource secretSearchAdminKey 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'aisearch-admin-key'
  properties: {
    value: aiSearch.listAdminKeys().primaryKey
  }
}

resource secretRedisConnectionString 'Microsoft.KeyVault/vaults/secrets@2023-07-01' = {
  parent: keyVault
  name: 'redis-connection-string'
  properties: {
    value: '${redisCache.properties.hostName}:${redisCache.properties.sslPort},password=${redisCache.listKeys().primaryKey},ssl=True,abortConnect=False'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

// --- Resource Group ---
output resourceGroupName string = resourceGroup().name
output location string = location
output environmentName string = environmentName

// --- App Service ---
output appServiceName string = appService.name
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output appServicePrincipalId string = appService.identity.principalId

// --- Azure OpenAI ---
output openAiName string = openAi.name
output openAiEndpoint string = openAi.properties.endpoint

// --- Azure AI Search ---
output aiSearchName string = aiSearch.name
output aiSearchEndpoint string = 'https://${aiSearch.name}.search.windows.net'

// --- Bot Service ---
output botServiceName string = botService.name
output botMessagingEndpoint string = botService.properties.endpoint
output botAppId string = botService.properties.msaAppId

// --- Redis ---
output redisHostName string = redisCache.properties.hostName

// --- Key Vault ---
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri

// --- Monitoring ---
output appInsightsName string = appInsights.name
output logAnalyticsName string = logAnalytics.name
output appInsightsConnectionString string = appInsights.properties.ConnectionString
