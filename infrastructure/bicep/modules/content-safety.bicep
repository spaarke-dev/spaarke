// infrastructure/bicep/modules/content-safety.bicep
// Azure AI Content Safety module for Spaarke R2 safety perimeter
//
// Capabilities enabled:
//   - Prompt Shields: jailbreak detection + indirect (document) attack detection
//   - Groundedness Detection: RAG response validation against source content
//
// Regional note:
//   Prompt Shields and Groundedness Detection API require West US 2 or East US 2
//   (as of 2026). Do not change location without verifying feature availability.
//
// Usage (one-off provisioning):
//   az deployment group create \
//     --resource-group spe-infrastructure-westus2 \
//     --template-file infrastructure/bicep/modules/content-safety.bicep \
//     --parameters contentSafetyName=spaarke-contentsafety-dev

@description('Name of the Azure AI Content Safety resource')
param contentSafetyName string

@description('Location for the resource — West US 2 required for Prompt Shields and Groundedness Detection API')
param location string = 'westus2'

@description('SKU for Content Safety. S0 is the only paid tier; F0 is free (limited TPS).')
@allowed(['F0', 'S0'])
param sku string = 'S0'

@description('Disable public network access. Set true after confirming private endpoint connectivity.')
param disablePublicNetworkAccess bool = false

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// CONTENT SAFETY ACCOUNT
// ============================================================================

resource contentSafety 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: contentSafetyName
  location: location
  tags: tags
  kind: 'ContentSafety'
  sku: {
    name: sku
  }
  properties: {
    customSubDomainName: contentSafetyName
    publicNetworkAccess: disablePublicNetworkAccess ? 'Disabled' : 'Enabled'
    networkAcls: {
      defaultAction: disablePublicNetworkAccess ? 'Deny' : 'Allow'
    }
    // Local key auth is required for Prompt Shields and Groundedness API
    // until managed-identity support reaches GA for ContentSafety kind.
    // Track: https://learn.microsoft.com/azure/ai-services/content-safety/
    disableLocalAuth: false
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output contentSafetyId string = contentSafety.id
output contentSafetyName string = contentSafety.name
output contentSafetyEndpoint string = contentSafety.properties.endpoint
#disable-next-line outputs-should-not-contain-secrets
output contentSafetyKey string = contentSafety.listKeys().key1
