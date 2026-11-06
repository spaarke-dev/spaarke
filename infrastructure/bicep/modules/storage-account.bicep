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

@description('Disable shared key access (requires RBAC for all access). Default true (task 244, owner D13): the only caller, customer.bicep, is keyless.')
param disableSharedKeyAccess bool = true

@description('Subnet ID for VNet network rule (empty = allow all)')
param allowedSubnetId string = ''

@description('Principal ID of the per-customer User-Assigned Managed Identity (from `modules/uami.bicep`, task 028) granted Storage Blob Data Contributor (built-in role `ba92f5b4-2d11-453d-a403-e96b0029c9fe`) at CONTAINER scope on each container in `userAssignedIdentityContainerNames` — never on the account (owner 2026-10-21, ebilling-r1 re-plan Q5). Empty default skips the grants (caller-side wiring). Emitted assignments set principalType=ServicePrincipal.')
param userAssignedIdentityPrincipalId string = ''

@description('Containers (a subset of `containers`) on which `userAssignedIdentityPrincipalId` is granted Storage Blob Data Contributor, one assignment per container at the scope of that container. A name not in `containers` gets no grant, so the caller must also list it in `containers`. Default empty: no grant. Every blob consumer the identity runs needs its container here, or its calls fail 403 (ebilling-r1 notes/091-storage-provisioning.md §2).')
param userAssignedIdentityContainerNames array = []

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
// RBAC: Storage Blob Data Contributor for the stamp UAMI — CONTAINER scope only
// Owner 2026-10-21 (ebilling-r1 re-plan Q5): the identity holds no data role at
// account scope; it is granted only on the containers it uses, so a container it
// does not list (another workload's, or the eBilling file store's) is not readable.
// Same pattern as acs-communication.bicep's dead-letter writer.
// ============================================================================

// Storage Blob Data Contributor built-in role ID
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// ADR-028 UAMI-first data-plane access; stable across App Service slot swaps (T5).
// principalType=ServicePrincipal avoids Azure Graph propagation-delay "principal not
// found" deploy failures. Idempotent guid(container id, principal, role) name.
resource uamiContainerBlobRbac 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (container, i) in containers: if (!empty(userAssignedIdentityPrincipalId) && contains(userAssignedIdentityContainerNames, container)) {
  name: guid(blobContainers[i].id, userAssignedIdentityPrincipalId, storageBlobDataContributorRoleId)
  scope: blobContainers[i]
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: userAssignedIdentityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Stamp UAMI blob data access on this container only (container scope, ebilling-r1 re-plan Q5)'
  }
}]

// ============================================================================
// OUTPUTS
// ============================================================================

output storageAccountId string = storageAccount.id
output storageAccountName string = storageAccount.name
output primaryEndpoint string = storageAccount.properties.primaryEndpoints.blob

// No connection-string output (task 244): callers use the blob endpoint above with their managed
// identity (Storage Blob Data Contributor on its containers, above). customer.bicep disables shared key access.
