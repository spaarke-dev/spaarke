// infrastructure/bicep/modules/intake-staging-storage.bicep
//
// INTAKE ENGINE STAGING CONTAINER -- a stamp-private blob container with a 7-day lifecycle delete rule,
// over an EXISTING stamp storage account (spaarke-intake-pipeline-r1 task 002, FR-01, NFR-03).
//
// WHAT IT HOLDS
//   The claim-check staging area of the NDA engine pipeline (ADR-004 A2 section 3): the BFF run job stages
//   the source .docx and the edit plan here, the engine Job (intake-engine-job.bicep) reads them and writes
//   its outputs (redline, external and clean .docx, check report) to deterministic paths here, and queue
//   messages carry ids only. The approved outputs are filed as sprk_document records, so these blobs are
//   never the record and are deleted after `deleteAfterDays`.
//
// SHAPE (copied from the existing modules, not invented)
//   - Lifecycle rule: modules/temp-blob-lifecycle.bicep (delete rule on a container prefix); 7 days, and
//     only the creation-time rule (the last-access variant there needs access-time tracking, which the stamp
//     account does not enable).
//   - Container-scoped role assignments: modules/ebilling-file-store.bicep (never the account or the group;
//     owner 2026-10-21, ebilling-r1 re-plan Q5, pinned by CustomerStampKeylessTemplateTests).
//
// SECURITY (ADR-015, ADR-028)
//   Private container (publicAccess None). Storage Blob Data Contributor, built-in, at CONTAINER scope for
//   the engine job identity and the BFF identity only. No key, connection string or SAS; the only outputs
//   are names and the blob endpoint.
//
// ONE POLICY PER ACCOUNT -- READ BEFORE ADDING ANOTHER LIFECYCLE RULE
//   `managementPolicies/default` is a singleton on a storage account: whoever deploys it last replaces the
//   whole rule set. customer.bicep passes enableTestDocumentLifecycle=false to storage-account.bicep, so
//   today this module owns the stamp account's only policy. A second lifecycle rule on the stamp account
//   (another module, or enabling storage-account.bicep's own policy) must be merged into this rules list or
//   it will silently replace it and the 7-day TTL on staged NDA content would stop applying.

@description('Name of the existing stamp storage account, in the resource group this module deploys into.')
param storageAccountName string

@description('Name of the private staging container (the engine pipeline contract: Sprk.Intake.EngineJob and the BFF run job read it from configuration).')
param containerName string = 'intake-staging'

@description('Days after creation after which staged blobs are deleted. 7 = past any Service Bus retry or dead-letter window (design section 5; spec NFR-03).')
@minValue(1)
@maxValue(30)
param deleteAfterDays int = 7

@description('Principal (object) id of the engine job identity. Granted Storage Blob Data Contributor on this container only. On a stamp: the Job\'s own identity (customer.bicep uamiIntakeEngine), never the stamp UAMI.')
@minLength(36)
param jobPrincipalId string

@description('Principal (object) id of the BFF identity that stages inputs and reads outputs. Granted Storage Blob Data Contributor on this container only. On a stamp: the stamp UAMI. The two ids are merged (union), so passing the same principal twice makes one assignment, not two with the same name.')
@minLength(36)
param bffPrincipalId string

// Storage Blob Data Contributor (built-in)
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/storage#storage-blob-data-contributor
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

var containerPrincipalIds = union([jobPrincipalId], [bffPrincipalId])

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// The blob service is declared by storage-account.bicep (soft delete); referenced only here.
resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' existing = {
  parent: storageAccount
  name: 'default'
}

resource stagingContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
    metadata: {
      purpose: 'intake-engine-staging'
      ttlDays: string(deleteAfterDays)
    }
  }
}

resource lifecyclePolicy 'Microsoft.Storage/storageAccounts/managementPolicies@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'delete-intake-staging'
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
      ]
    }
  }
}

// Container scope only (never the account). Deterministic guid() names make a re-deploy a no-op.
resource containerBlobRbac 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for principalId in containerPrincipalIds: {
  name: guid(stagingContainer.id, principalId, storageBlobDataContributorRoleId)
  scope: stagingContainer
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'Intake engine staging container only (container scope; spaarke-intake-pipeline-r1 task 002)'
  }
}]

output containerName string = stagingContainer.name
output deleteAfterDays int = deleteAfterDays
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
