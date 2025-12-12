// infrastructure/bicep/modules/ai-foundry-hub.bicep
// Azure AI Foundry Hub and Project for Prompt Flow orchestration
// Supports multi-tenant deployment for Spaarke Document Intelligence

@description('Name of the AI Foundry Hub')
param hubName string

@description('Name of the AI Foundry Project')
param projectName string

@description('Azure region for deployment')
param location string

@description('Resource ID of the Storage Account for AI Foundry')
param storageAccountId string

@description('Resource ID of the Key Vault for AI Foundry')
param keyVaultId string

@description('Resource ID of Application Insights for monitoring')
param appInsightsId string

@description('Resource ID of the existing Azure OpenAI resource to connect')
param openAiResourceId string = ''

@description('Resource ID of the existing AI Search resource to connect')
param aiSearchResourceId string = ''

@description('Tags applied to all resources')
param tags object = {}

@description('SKU for the AI Foundry workspace')
@allowed(['Basic', 'Standard'])
param sku string = 'Basic'

@description('Enable public network access')
param publicNetworkAccess string = 'Enabled'

// ============================================================================
// AI FOUNDRY HUB
// ============================================================================

resource aiHub 'Microsoft.MachineLearningServices/workspaces@2024-04-01' = {
  name: hubName
  location: location
  tags: union(tags, {
    component: 'ai-foundry-hub'
  })
  kind: 'Hub'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    friendlyName: 'Spaarke AI Foundry Hub'
    description: 'Central AI Hub for Spaarke Document Intelligence - Prompt Flow orchestration, evaluation, and monitoring'
    storageAccount: storageAccountId
    keyVault: keyVaultId
    applicationInsights: appInsightsId
    publicNetworkAccess: publicNetworkAccess
    // Hub-specific settings
    managedNetwork: {
      isolationMode: 'Disabled' // Use 'AllowInternetOutbound' or 'AllowOnlyApprovedOutbound' for stricter control
    }
  }
}

// ============================================================================
// AI FOUNDRY PROJECT
// ============================================================================

resource aiProject 'Microsoft.MachineLearningServices/workspaces@2024-04-01' = {
  name: projectName
  location: location
  tags: union(tags, {
    component: 'ai-foundry-project'
  })
  kind: 'Project'
  identity: {
    type: 'SystemAssigned'
  }
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    friendlyName: 'Spaarke Document Intelligence'
    description: 'AI Project for Analysis feature - Prompt Flows, RAG, and evaluation pipelines'
    hubResourceId: aiHub.id
    publicNetworkAccess: publicNetworkAccess
  }
}

// ============================================================================
// CONNECTIONS (Optional - connect existing resources)
// ============================================================================

// Azure OpenAI Connection
// Note: Uses ManagedIdentity auth for secure connection to existing OpenAI resource
resource openAiConnection 'Microsoft.MachineLearningServices/workspaces/connections@2024-04-01' = if (!empty(openAiResourceId)) {
  parent: aiHub
  name: 'azure-openai-connection'
  properties: {
    category: 'AzureOpenAI'
    target: '${environment().resourceManager}${skip(openAiResourceId, 1)}'
    authType: 'ManagedIdentity'
    isSharedToAll: true
    metadata: {
      ApiType: 'Azure'
    }
  }
}

// AI Search Connection
// Note: Uses ManagedIdentity auth for secure connection to existing AI Search resource
resource aiSearchConnection 'Microsoft.MachineLearningServices/workspaces/connections@2024-04-01' = if (!empty(aiSearchResourceId)) {
  parent: aiHub
  name: 'ai-search-connection'
  properties: {
    category: 'CognitiveSearch'
    target: '${environment().resourceManager}${skip(aiSearchResourceId, 1)}'
    authType: 'ManagedIdentity'
    isSharedToAll: true
    metadata: {}
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output hubId string = aiHub.id
output hubName string = aiHub.name
output hubPrincipalId string = aiHub.identity.principalId

output projectId string = aiProject.id
output projectName string = aiProject.name
output projectPrincipalId string = aiProject.identity.principalId

// Endpoint for Prompt Flow invocations
output promptFlowEndpoint string = 'https://${projectName}.${location}.inference.ml.azure.com'

// Discovery URL for AI Foundry portal
output aiFoundryPortalUrl string = 'https://ai.azure.com/build/overview?wsid=${aiProject.id}'
