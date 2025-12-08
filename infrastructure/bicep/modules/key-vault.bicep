// infrastructure/bicep/modules/key-vault.bicep
// Azure Key Vault module for Spaarke infrastructure

@description('Name of the Key Vault')
param keyVaultName string

@description('Location for the Key Vault')
param location string = resourceGroup().location

@description('Enable soft delete')
param enableSoftDelete bool = true

@description('Soft delete retention days')
param softDeleteRetentionDays int = 90

@description('Enable purge protection')
param enablePurgeProtection bool = true

@description('SKU for Key Vault')
@allowed(['standard', 'premium'])
param sku string = 'standard'

@description('Principal IDs to grant Key Vault access')
param accessPolicies array = []

@description('Tags for the resource')
param tags object = {}

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: {
      family: 'A'
      name: sku
    }
    enableSoftDelete: enableSoftDelete
    softDeleteRetentionInDays: softDeleteRetentionDays
    enablePurgeProtection: enablePurgeProtection
    enableRbacAuthorization: true
    accessPolicies: [for policy in accessPolicies: {
      tenantId: subscription().tenantId
      objectId: policy.objectId
      permissions: {
        secrets: policy.?secretPermissions ?? ['get', 'list']
        certificates: policy.?certificatePermissions ?? ['get', 'list']
        keys: policy.?keyPermissions ?? ['get', 'list']
      }
    }]
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

output keyVaultId string = keyVault.id
output keyVaultName string = keyVault.name
output keyVaultUri string = keyVault.properties.vaultUri
