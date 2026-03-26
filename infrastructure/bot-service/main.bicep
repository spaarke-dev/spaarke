// infrastructure/bot-service/main.bicep
// Azure Bot Service for Spaarke M365 Copilot Integration
// Deploys Bot Service with Teams and DirectLine channels, managed identity auth,
// and Key Vault reference for secrets.
//
// Usage:
//   az deployment group create \
//     --resource-group spe-infrastructure-westus2 \
//     --template-file infrastructure/bot-service/main.bicep \
//     --parameters infrastructure/bot-service/parameters.dev.json

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Name of the Azure Bot Service resource')
param botServiceName string = 'spaarke-bot-dev'

@description('Messaging endpoint URL for the bot (BFF API agent message route)')
param messagingEndpoint string

@description('Microsoft App ID (Entra app registration client ID) for bot authentication')
param appId string

@description('Primary Azure region')
param location string = 'westus2'

@description('Bot display name shown in Teams and Copilot')
param botDisplayName string = 'Spaarke AI Assistant'

@description('Bot description')
param botDescription string = 'Spaarke AI-powered document assistant for M365 Copilot integration'

@description('Key Vault name for secret references')
param keyVaultName string = 'spaarke-spekvcert'

@description('SKU for the Bot Service')
@allowed(['F0', 'S1'])
param sku string = 'F0'

@description('Enable managed identity for the Bot Service')
param enableManagedIdentity bool = true

@description('Tags applied to all resources')
param tags object = {
  environment: 'dev'
  application: 'spaarke'
  layer: 'platform'
  managedBy: 'bicep'
  component: 'm365-copilot-integration'
}

// ============================================================================
// VARIABLES
// ============================================================================

var botServiceKind = 'azurebot'

// ============================================================================
// RESOURCES
// ============================================================================

// Azure Bot Service
resource botService 'Microsoft.BotService/botServices@2022-09-15' = {
  name: botServiceName
  location: location
  kind: botServiceKind
  tags: tags
  sku: {
    name: sku
  }
  identity: enableManagedIdentity ? {
    type: 'SystemAssigned'
  } : null
  properties: {
    displayName: botDisplayName
    description: botDescription
    endpoint: messagingEndpoint
    msaAppId: appId
    msaAppType: 'SingleTenant'
    msaAppMSIResourceId: enableManagedIdentity ? botService.id : null
    msaAppTenantId: tenant().tenantId
    schemaTransformationVersion: '1.3'
    disableLocalAuth: true
    isStreamingSupported: true
  }
}

// Microsoft Teams Channel
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

// DirectLine Channel (used by Copilot and web integrations)
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
            'https://spe-api-dev-67e2xz.azurewebsites.net'
          ]
        }
      ]
    }
  }
}

// Key Vault access policy — grant Bot Service managed identity read access to secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = if (keyVaultName != '' && enableManagedIdentity) {
  name: keyVaultName
}

resource keyVaultAccessPolicy 'Microsoft.KeyVault/vaults/accessPolicies@2023-07-01' = if (keyVaultName != '' && enableManagedIdentity) {
  parent: keyVault
  name: 'add'
  properties: {
    accessPolicies: [
      {
        objectId: enableManagedIdentity ? botService.identity.principalId : ''
        tenantId: tenant().tenantId
        permissions: {
          secrets: [
            'get'
            'list'
          ]
        }
      }
    ]
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

@description('Bot Service resource ID')
output botServiceId string = botService.id

@description('Bot Service name')
output botServiceName string = botService.name

@description('Bot Service messaging endpoint')
output messagingEndpoint string = botService.properties.endpoint

@description('Bot Service managed identity principal ID')
output botPrincipalId string = enableManagedIdentity ? botService.identity.principalId : ''

@description('Bot Service Microsoft App ID')
output msaAppId string = botService.properties.msaAppId
