// infrastructure/bicep/modules/ebilling-file-store.bicep
//
// eBilling FILE STORE -- blob containers, a least-privilege custom role, container-scoped grants
// and the Defender for Storage malware-scan setting, over an EXISTING storage account.
// (ebilling-r1 task 091; model projects/ebilling-r1/notes/procedure-and-file-model.md section 4.2.)
//
// WHY A CUSTOM ROLE (owner 2026-10-21, option A; unified-access-control-r2 conditions)
//   The file store (task 090) serves a file only when Defender's malware-scan blob index tag says
//   "No threats found". Reading that tag needs .../containers/blobs/tags/read, which built-in
//   Storage Blob Data Contributor lacks, and every built-in role that has it also has
//   .../blobs/tags/write -- so the BFF could forge a clean verdict. The role below is exactly
//   Storage Blob Data Contributor's actions and dataActions, listed one by one, plus tags/read:
//   no wildcard, no tags/write, empty notActions/notDataActions, assignable only in this resource
//   group. tests/Spaarke.ArchTests/CustomerStampKeylessTemplateTests.cs pins the exact set on the
//   compiled ebilling-file-store.json (recompile it after any edit here:
//   az bicep build --file ebilling-file-store.bicep --outfile ebilling-file-store.json).
//
// SCOPE
//   Resource-group scoped: deploy into the storage account's resource group. The role definition is
//   created here (assignableScopes = this resource group) and assigned once per container at that
//   container's scope -- never on the account or the resource group (K-3 mitigation, re-plan Q5).
//   The account itself is only referenced: no network-rule or key change. The one account-level setting
//   this module owns is the blob service's blob soft delete (deleteRetentionPolicy, default 30 days;
//   ebilling-r1 DEF-038, owner decision 2026-10-22: every environment including dev). Versioning, change
//   feed and container soft delete are deliberately NOT set here.
//
// KEYLESS (root CLAUDE.md section 9, ADR-028, task 244)
//   No key, connection string or SAS anywhere; the only output is the blob endpoint. The BFF's
//   managed identity is the only data-plane principal granted. No Key Vault resource.
//
// SOFT DELETE (ebilling-r1 DEF-038)
//   blobServices/default is declared (not just referenced) so the module can set deleteRetentionPolicy.
//   That PUT carries ONLY deleteRetentionPolicy: no CORS, versioning, change feed, restore policy,
//   container soft delete or default service version is written. Read the full blob-service properties
//   before the first apply (notes/091-storage-provisioning.md section 8.8).
//
// NOT HERE (hand-off to customer-provisioning-orchestration-r1, notes/091-storage-provisioning.md section 6)
//   Wiring into customer.bicep for the stamp UAMI; the per-environment scanning choice.

@description('Name of the existing storage account, in the resource group this module deploys into.')
param storageAccountName string

@description('Object (principal) id of the BFF managed identity granted the file-store role on each container. On a stamp: the stamp UAMI (modules/uami.bicep). On dev: mi-bff-api-dev.')
@minLength(36)
param principalId string

@description('The file-store containers (model 4.2): Procedure sources, invoice PDFs and supporting files, export files. One role assignment per container, at container scope.')
@minLength(1)
param containerNames array = [
  'procedures'
  'invoice-files'
  'billing-exports'
]

@description('Configure Defender for Storage on-upload malware scanning on the account. Required, no default: each environment states its choice. false = the explicit "Scanning not enabled" setting, which the BFF must also be told (FileStore MalwareScanningEnabled=false, task 090).')
param enableMalwareScanning bool

@description('Monthly cap, in GB scanned, for on-upload malware scanning. Used only when enableMalwareScanning is true.')
@minValue(1)
param malwareScanningCapGBPerMonth int = 5000

@description('Blob soft delete retention, in days, for the file store account (ebilling-r1 DEF-038). Every environment gets 30 unless it overrides (customer-provisioning passes it per environment).')
@minValue(1)
@maxValue(365)
param blobSoftDeleteRetentionDays int = 30

// ============================================================================
// EXISTING ACCOUNT (referenced only)
// ============================================================================

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: storageAccountName
}

// ============================================================================
// BLOB SERVICE -- blob soft delete only (ebilling-r1 DEF-038); nothing else on the service is written
// ============================================================================

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    deleteRetentionPolicy: {
      enabled: true
      days: blobSoftDeleteRetentionDays
    }
  }
}

// ============================================================================
// CONTAINERS -- private, no immutability policy (model 4.2: write-once is enforced by the BFF)
// ============================================================================

resource fileStoreContainers 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = [for name in containerNames: {
  parent: blobService
  name: name
  properties: {
    publicAccess: 'None'
  }
}]

// ============================================================================
// CUSTOM ROLE -- Storage Blob Data Contributor + blobs/tags/read, nothing else
// ============================================================================

resource fileStoreRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(resourceGroup().id, 'spaarke-ebilling-file-store-blob-role')
  properties: {
    roleName: 'Spaarke eBilling File Store Blob Data (${uniqueString(resourceGroup().id)})'
    description: 'Storage Blob Data Contributor plus reading blob index tags (Defender malware-scan verdict). Cannot write tags. Assigned per eBilling container only (ebilling-r1 task 091).'
    type: 'customRole'
    permissions: [
      {
        actions: [
          'Microsoft.Storage/storageAccounts/blobServices/containers/delete'
          'Microsoft.Storage/storageAccounts/blobServices/containers/read'
          'Microsoft.Storage/storageAccounts/blobServices/containers/write'
          'Microsoft.Storage/storageAccounts/blobServices/generateUserDelegationKey/action'
        ]
        notActions: []
        dataActions: [
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/delete'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/read'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/write'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/move/action'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/add/action'
          'Microsoft.Storage/storageAccounts/blobServices/containers/blobs/tags/read'
        ]
        notDataActions: []
      }
    ]
    assignableScopes: [
      resourceGroup().id
    ]
  }
}

// ============================================================================
// GRANTS -- one per container, at that container's scope
// ============================================================================

resource fileStoreGrants 'Microsoft.Authorization/roleAssignments@2022-04-01' = [for (name, i) in containerNames: {
  name: guid(fileStoreContainers[i].id, principalId, fileStoreRole.id)
  scope: fileStoreContainers[i]
  properties: {
    // The definition is deployed at resource-group scope; an assignment names it by its subscription-level id.
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', fileStoreRole.name)
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'BFF file-store access on this container only (custom role: blob data + tag read, no tag write; ebilling-r1 task 091)'
  }
}]

// ============================================================================
// DEFENDER FOR STORAGE -- on-upload malware scanning (writes the scan-result index tag)
// ============================================================================

resource malwareScanning 'Microsoft.Security/defenderForStorageSettings@2022-12-01-preview' = if (enableMalwareScanning) {
  name: 'current'
  scope: storageAccount
  properties: {
    isEnabled: true
    malwareScanning: {
      onUpload: {
        isEnabled: true
        capGBPerMonth: malwareScanningCapGBPerMonth
      }
    }
    sensitiveDataDiscovery: {
      isEnabled: false
    }
    overrideSubscriptionLevelSettings: true
  }
}

// ============================================================================
// OUTPUTS -- names and the endpoint only; no key, connection string or SAS
// ============================================================================

output containerNames array = [for (name, i) in containerNames: fileStoreContainers[i].name]
output blobEndpoint string = storageAccount.properties.primaryEndpoints.blob
