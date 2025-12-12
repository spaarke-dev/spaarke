// infrastructure/bicep/stacks/ai-foundry-stack.bicep
// Standalone AI Foundry deployment for Spaarke Document Intelligence
// Can be deployed independently or as part of model2-full.bicep

targetScope = 'resourceGroup'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Customer identifier (lowercase, alphanumeric only)')
@minLength(3)
@maxLength(10)
param customerId string

@description('Environment name')
@allowed(['dev', 'staging', 'prod'])
param environment string = 'dev'

@description('Primary Azure region')
param location string = resourceGroup().location

@description('SKU for AI Foundry workspace')
@allowed(['Basic', 'Standard'])
param aiFoundrySku string = 'Basic'

@description('Enable public network access')
param publicNetworkAccess string = 'Enabled'

@description('Existing Azure OpenAI resource ID to connect (optional)')
param openAiResourceId string = ''

@description('Existing AI Search resource ID to connect (optional)')
param aiSearchResourceId string = ''

@description('Tags applied to all resources')
param tags object = {
  customer: customerId
  environment: environment
  application: 'spaarke'
  component: 'ai-foundry'
  managedBy: 'bicep'
}

// ============================================================================
// VARIABLES
// ============================================================================

var baseName = 'sprk${customerId}${environment}'
var storageAccountName = take(toLower(replace('${baseName}aifsa', '-', '')), 24)
var keyVaultName = '${baseName}-aif-kv'
var appInsightsName = '${baseName}-aif-insights'
var logAnalyticsName = '${baseName}-aif-logs'
var hubName = '${baseName}-aif-hub'
var projectName = '${baseName}-aif-proj'

// ============================================================================
// DEPENDENCIES - Storage, Key Vault, App Insights
// ============================================================================

// Storage Account for AI Foundry
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: {
    name: 'Standard_LRS'
  }
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
  }
}

// Key Vault for AI Foundry secrets
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
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 7
    publicNetworkAccess: 'Enabled'
  }
}

// Log Analytics Workspace
resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      name: 'PerGB2018'
    }
    retentionInDays: 30
  }
}

// Application Insights
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
// AI FOUNDRY HUB AND PROJECT
// ============================================================================

module aiFoundry '../modules/ai-foundry-hub.bicep' = {
  name: 'ai-foundry-${baseName}'
  params: {
    hubName: hubName
    projectName: projectName
    location: location
    storageAccountId: storageAccount.id
    keyVaultId: keyVault.id
    appInsightsId: appInsights.id
    openAiResourceId: openAiResourceId
    aiSearchResourceId: aiSearchResourceId
    sku: aiFoundrySku
    publicNetworkAccess: publicNetworkAccess
    tags: tags
  }
}

// ============================================================================
// ROLE ASSIGNMENTS
// ============================================================================

// Grant AI Foundry Hub access to Storage Account
// Note: Using hubName (deployment-time value) for deterministic guid generation
resource storageRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storageAccount.id, hubName, 'Storage Blob Data Contributor')
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', 'ba92f5b4-2d11-453d-a403-e96b0029c9fe') // Storage Blob Data Contributor
    principalId: aiFoundry.outputs.hubPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Grant AI Foundry Hub access to Key Vault
resource keyVaultRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, hubName, 'Key Vault Secrets User')
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6') // Key Vault Secrets User
    principalId: aiFoundry.outputs.hubPrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output hubId string = aiFoundry.outputs.hubId
output hubName string = aiFoundry.outputs.hubName
output projectId string = aiFoundry.outputs.projectId
output projectName string = aiFoundry.outputs.projectName
output promptFlowEndpoint string = aiFoundry.outputs.promptFlowEndpoint
output aiFoundryPortalUrl string = aiFoundry.outputs.aiFoundryPortalUrl

// Dependency resource IDs for reference
output storageAccountId string = storageAccount.id
output keyVaultId string = keyVault.id
output appInsightsId string = appInsights.id
