// temp-blob-lifecycle.bicep
// Lifecycle management policy for test documents with 24-hour TTL
//
// This module configures Azure Blob Storage lifecycle management to automatically
// delete test documents after 24 hours, ensuring:
// - No accumulation of temporary test files
// - Cost optimization by removing unused data
// - Compliance with data retention policies

@description('Name of the storage account')
param storageAccountName string

@description('Name of the container for test documents')
param containerName string = 'test-documents'

@description('Number of days after which blobs are deleted')
param deleteAfterDays int = 1

@description('Enable lifecycle management policy')
param enableLifecyclePolicy bool = true

// Reference existing storage account
resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// Create the test-documents container
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource testDocumentsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
    metadata: {
      purpose: 'test-documents'
      ttlDays: string(deleteAfterDays)
    }
  }
}

// Lifecycle management policy for automatic cleanup
resource lifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2023-01-01' = if (enableLifecyclePolicy) {
  parent: storageAccount
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-test-documents'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${containerName}/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterCreationGreaterThan: deleteAfterDays
                }
              }
            }
          }
        }
        {
          name: 'cleanup-expired-test-docs'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: {
              blobTypes: [
                'blockBlob'
              ]
              prefixMatch: [
                '${containerName}/'
              ]
            }
            actions: {
              baseBlob: {
                delete: {
                  daysAfterLastAccessTimeGreaterThan: deleteAfterDays
                }
              }
            }
          }
        }
      ]
    }
  }
}

// Outputs
output containerName string = testDocumentsContainer.name
output lifecyclePolicyEnabled bool = enableLifecyclePolicy
output deleteAfterDays int = deleteAfterDays
