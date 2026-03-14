// infrastructure/bicep/modules/vnet.bicep
// Virtual Network module for Spaarke production network isolation
// Deploys VNet with 3 subnets: snet-app (App Service integration),
// snet-redis (Redis VNet injection), snet-pe (private endpoints)

@description('Name of the Virtual Network')
param vnetName string

@description('Location for the VNet')
param location string = resourceGroup().location

@description('VNet address space')
param addressPrefix string = '10.0.0.0/16'

@description('App Service integration subnet CIDR')
param snetAppPrefix string = '10.0.1.0/24'

@description('Redis VNet injection subnet CIDR')
param snetRedisPrefix string = '10.0.2.0/24'

@description('Private endpoints subnet CIDR')
param snetPePrefix string = '10.0.3.0/24'

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// NETWORK SECURITY GROUPS
// ============================================================================

resource nsgApp 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: '${vnetName}-nsg-app'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowHttpsOutbound'
        properties: {
          priority: 100
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowRedisOutbound'
        properties: {
          priority: 110
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '6380'
          sourceAddressPrefix: snetAppPrefix
          destinationAddressPrefix: snetRedisPrefix
        }
      }
      {
        name: 'AllowPrivateEndpointsOutbound'
        properties: {
          priority: 120
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: snetAppPrefix
          destinationAddressPrefix: snetPePrefix
        }
      }
    ]
  }
}

resource nsgRedis 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: '${vnetName}-nsg-redis'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowRedisInboundFromApp'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '6380'
          sourceAddressPrefix: snetAppPrefix
          destinationAddressPrefix: snetRedisPrefix
        }
      }
      {
        name: 'AllowRedisInternalInbound'
        properties: {
          priority: 110
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: snetRedisPrefix
          destinationAddressPrefix: snetRedisPrefix
        }
      }
      {
        name: 'AllowAzureLoadBalancerInbound'
        properties: {
          priority: 120
          direction: 'Inbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: 'AzureLoadBalancer'
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowRedisOutbound'
        properties: {
          priority: 100
          direction: 'Outbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRanges: ['443', '6379', '6380', '8443', '10221-10231', '20226']
          sourceAddressPrefix: snetRedisPrefix
          destinationAddressPrefix: '*'
        }
      }
      {
        name: 'AllowDnsOutbound'
        properties: {
          priority: 110
          direction: 'Outbound'
          access: 'Allow'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '53'
          sourceAddressPrefix: snetRedisPrefix
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

resource nsgPe 'Microsoft.Network/networkSecurityGroups@2023-09-01' = {
  name: '${vnetName}-nsg-pe'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowVnetInbound'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourcePortRange: '*'
          destinationPortRange: '443'
          sourceAddressPrefix: 'VirtualNetwork'
          destinationAddressPrefix: snetPePrefix
        }
      }
      {
        name: 'DenyAllInbound'
        properties: {
          priority: 4096
          direction: 'Inbound'
          access: 'Deny'
          protocol: '*'
          sourcePortRange: '*'
          destinationPortRange: '*'
          sourceAddressPrefix: '*'
          destinationAddressPrefix: '*'
        }
      }
    ]
  }
}

// ============================================================================
// VIRTUAL NETWORK
// ============================================================================

resource vnet 'Microsoft.Network/virtualNetworks@2023-09-01' = {
  name: vnetName
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [addressPrefix]
    }
    subnets: [
      {
        name: 'snet-app'
        properties: {
          addressPrefix: snetAppPrefix
          networkSecurityGroup: {
            id: nsgApp.id
          }
          delegations: [
            {
              name: 'appServiceDelegation'
              properties: {
                serviceName: 'Microsoft.Web/serverFarms'
              }
            }
          ]
          serviceEndpoints: [
            {
              service: 'Microsoft.KeyVault'
              locations: ['*']
            }
          ]
        }
      }
      {
        name: 'snet-redis'
        properties: {
          addressPrefix: snetRedisPrefix
          networkSecurityGroup: {
            id: nsgRedis.id
          }
        }
      }
      {
        name: 'snet-pe'
        properties: {
          addressPrefix: snetPePrefix
          networkSecurityGroup: {
            id: nsgPe.id
          }
          privateEndpointNetworkPolicies: 'Disabled'
        }
      }
    ]
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output vnetId string = vnet.id
output vnetName string = vnet.name
output snetAppId string = vnet.properties.subnets[0].id
output snetAppName string = vnet.properties.subnets[0].name
output snetRedisId string = vnet.properties.subnets[1].id
output snetRedisName string = vnet.properties.subnets[1].name
output snetPeId string = vnet.properties.subnets[2].id
output snetPeName string = vnet.properties.subnets[2].name
