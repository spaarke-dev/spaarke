// infrastructure/bicep/modules/storage-account.bicep
// Azure Storage Account module for temporary files and logs
// Hardened: shared key access disabled, VNet restricted, lifecycle policies

@description('Name of the Storage Account')
param storageAccountName string

@description('Location for the Storage Account')
param location string = resourceGroup().location

@description('SKU for the Storage Account')
@allowed(['Standard_LRS', 'Standard_GRS', 'Standard_ZRS', 'Premium_LRS'])
param sku string = 'Standard_LRS'

@description('Blob containers to create')
param containers array = ['temp-files', 'document-processing', 'test-documents']

@description('Enable lifecycle policy for test-documents container (24hr TTL)')
param enableTestDocumentLifecycle bool = true

@description('Disable shared key access (requires RBAC for all access)')
param disableSharedKeyAccess bool = false

@description('Subnet ID for VNet network rule (empty = allow all)')
param allowedSubnetId string = ''

@description('App Service principal ID for Storage Blob Data Contributor RBAC role')
param appServicePrincipalId string = ''

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// STORAGE ACCOUNT
// ============================================================================

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: storageAccountName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    supportsHttpsTrafficOnly: true
    minimumTlsVersion: 'TLS1_2'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: !disableSharedKeyAccess
    networkAcls: !empty(allowedSubnetId) ? {
      defaultAction: 'Deny'
      bypass: 'AzureServices'
      virtualNetworkRules: [
        {
          id: allowedSubnetId
          action: 'Allow'
        }
      ]
      ipRules: []
    } : {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ============================================================================
// BLOB SERVICE + CONTAINERS
// ============================================================================

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource blobContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = [for container in containers: {
  parent: blobService
  name: container
  properties: {
    publicAccess: 'None'
  }
}]

// ============================================================================
// LIFECYCLE POLICIES
// ============================================================================

// Combined lifecycle policy: test-documents (24hr delete) + ai-chunks (Cool after 30d)
resource lifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2023-01-01' = if (enableTestDocumentLifecycle) {
  parent: storageAccount
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-test-documents-after-24hrs'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: ['blockBlob']
              prefixMatch: ['test-documents/']
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterCreationGreaterThan: 1
                }
              }
            }
          }
        }
        {
          name: 'tier-ai-chunks-to-cool-after-30d'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: ['blockBlob']
              prefixMatch: ['ai-chunks/']
            }
            actions: {
              baseBlob: {
                tierToCool: {
                  daysAfterModificationGreaterThan: 30
                }
              }
            }
          }
        }
      ]
    }
  }
}

// ============================================================================
// RBAC: Storage Blob Data Contributor for App Service managed identity
// Required BEFORE disabling shared key access so the app can still read/write blobs
// ============================================================================

// Storage Blob Data Contributor built-in role ID
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

resource storageBlobRbac 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(appServicePrincipalId)) {
  // Use a deterministic GUID based on storage + principal + role to avoid conflicts
  name: guid(storageAccount.id, appServicePrincipalId, storageBlobDataContributorRoleId)
  scope: storageAccount
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
output primaryEndpoint string = storageAccount.properties.primaryEndpoints.blob

// When shared key access is disabled, connection strings using account keys won't work.
// Use managed identity (DefaultAzureCredential) with the blob endpoint instead.
// For backward compatibility, output key-based connection string only when shared keys are enabled.
#disable-next-line outputs-should-not-contain-secrets
output connectionString string = disableSharedKeyAccess ? 'BlobEndpoint=${storageAccount.properties.primaryEndpoints.blob}' : 'DefaultEndpointsProtocol=https;AccountName=${storageAccount.name};AccountKey=${storageAccount.listKeys().keys[0].value};EndpointSuffix=core.windows.net'
