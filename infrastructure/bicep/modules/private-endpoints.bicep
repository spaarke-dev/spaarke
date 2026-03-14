// infrastructure/bicep/modules/private-endpoints.bicep
// Private endpoint module for Spaarke production network isolation
// Deploys private endpoints for Key Vault, Storage, Service Bus,
// OpenAI, AI Search, and Document Intelligence into snet-pe subnet.
// NOTE: Deploy alongside public access first, test, then disable public access.

@description('Location for the private endpoints')
param location string = resourceGroup().location

@description('Subnet ID for private endpoints (snet-pe)')
param privateEndpointSubnetId string

@description('Base name for private endpoints')
param baseName string

// ============================================================================
// SERVICE RESOURCE IDs (pass from parent stack)
// ============================================================================

@description('Key Vault resource ID')
param keyVaultId string

@description('Storage Account resource ID')
param storageAccountId string

@description('Service Bus namespace resource ID')
param serviceBusId string

@description('Azure OpenAI resource ID')
param openAiId string

@description('AI Search resource ID')
param aiSearchId string

@description('Document Intelligence resource ID')
param docIntelligenceId string

@description('Tags for resources')
param tags object = {}

// ============================================================================
// PRIVATE DNS ZONES
// ============================================================================

// Use environment() to avoid hardcoded URLs across sovereign clouds
var blobSuffix = environment().suffixes.storage

var privateDnsZones = [
  {
    name: 'privatelink.vaultcore.azure.net'
    label: 'keyvault'
  }
  {
    name: 'privatelink.blob.${blobSuffix}'
    label: 'blob'
  }
  {
    name: 'privatelink.servicebus.windows.net'
    label: 'servicebus'
  }
  {
    name: 'privatelink.openai.azure.com'
    label: 'openai'
  }
  {
    name: 'privatelink.search.windows.net'
    label: 'search'
  }
  {
    name: 'privatelink.cognitiveservices.azure.com'
    label: 'docintel'
  }
]

resource dnsZones 'Microsoft.Network/privateDnsZones@2020-06-01' = [for zone in privateDnsZones: {
  name: zone.name
  location: 'global'
  tags: tags
}]

// ============================================================================
// VNET LINKS (link DNS zones to VNet for resolution)
// ============================================================================

@description('VNet resource ID for DNS zone linking')
param vnetId string

resource dnsZoneVnetLinks 'Microsoft.Network/privateDnsZones/virtualNetworkLinks@2020-06-01' = [for (zone, i) in privateDnsZones: {
  parent: dnsZones[i]
  name: '${zone.label}-vnet-link'
  location: 'global'
  tags: tags
  properties: {
    virtualNetwork: {
      id: vnetId
    }
    registrationEnabled: false
  }
}]

// ============================================================================
// PRIVATE ENDPOINTS
// ============================================================================

// --- Key Vault ---
resource peKeyVault 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${baseName}-pe-kv'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${baseName}-psc-kv'
        properties: {
          privateLinkServiceId: keyVaultId
          groupIds: ['vault']
        }
      }
    ]
  }
}

resource peKeyVaultDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: peKeyVault
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'keyvault-config'
        properties: {
          privateDnsZoneId: dnsZones[0].id
        }
      }
    ]
  }
}

// --- Storage Account (Blob) ---
resource peStorage 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${baseName}-pe-sa'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${baseName}-psc-sa'
        properties: {
          privateLinkServiceId: storageAccountId
          groupIds: ['blob']
        }
      }
    ]
  }
}

resource peStorageDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: peStorage
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'blob-config'
        properties: {
          privateDnsZoneId: dnsZones[1].id
        }
      }
    ]
  }
}

// --- Service Bus ---
resource peServiceBus 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${baseName}-pe-sb'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${baseName}-psc-sb'
        properties: {
          privateLinkServiceId: serviceBusId
          groupIds: ['namespace']
        }
      }
    ]
  }
}

resource peServiceBusDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: peServiceBus
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'servicebus-config'
        properties: {
          privateDnsZoneId: dnsZones[2].id
        }
      }
    ]
  }
}

// --- Azure OpenAI ---
resource peOpenAi 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${baseName}-pe-openai'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${baseName}-psc-openai'
        properties: {
          privateLinkServiceId: openAiId
          groupIds: ['account']
        }
      }
    ]
  }
}

resource peOpenAiDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: peOpenAi
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'openai-config'
        properties: {
          privateDnsZoneId: dnsZones[3].id
        }
      }
    ]
  }
}

// --- AI Search ---
resource peAiSearch 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${baseName}-pe-search'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${baseName}-psc-search'
        properties: {
          privateLinkServiceId: aiSearchId
          groupIds: ['searchService']
        }
      }
    ]
  }
}

resource peAiSearchDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: peAiSearch
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'search-config'
        properties: {
          privateDnsZoneId: dnsZones[4].id
        }
      }
    ]
  }
}

// --- Document Intelligence ---
resource peDocIntel 'Microsoft.Network/privateEndpoints@2023-09-01' = {
  name: '${baseName}-pe-docintel'
  location: location
  tags: tags
  properties: {
    subnet: {
      id: privateEndpointSubnetId
    }
    privateLinkServiceConnections: [
      {
        name: '${baseName}-psc-docintel'
        properties: {
          privateLinkServiceId: docIntelligenceId
          groupIds: ['account']
        }
      }
    ]
  }
}

resource peDocIntelDnsGroup 'Microsoft.Network/privateEndpoints/privateDnsZoneGroups@2023-09-01' = {
  parent: peDocIntel
  name: 'default'
  properties: {
    privateDnsZoneConfigs: [
      {
        name: 'docintel-config'
        properties: {
          privateDnsZoneId: dnsZones[5].id
        }
      }
    ]
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output keyVaultPrivateEndpointId string = peKeyVault.id
output storagePrivateEndpointId string = peStorage.id
output serviceBusPrivateEndpointId string = peServiceBus.id
output openAiPrivateEndpointId string = peOpenAi.id
output aiSearchPrivateEndpointId string = peAiSearch.id
output docIntelligencePrivateEndpointId string = peDocIntel.id

output privateDnsZoneIds array = [for (zone, i) in privateDnsZones: dnsZones[i].id]
