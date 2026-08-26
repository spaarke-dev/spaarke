// infrastructure/bicep/modules/controlplane-artifacts-storage.bicep
//
// L2 CONTROL-PLANE provisioning-artifacts Storage Account (fleet-scoped)
// + provisioning-artifacts blob container + data-plane RBAC.
//
// PURPOSE
//   Emits the fleet-scoped StorageV2 account that hosts the
//   `provisioning-artifacts` blob container -- the artifact hand-off point
//   between CI and the L2 Worker's handlers:
//     - CI (tasks 116/117: publish-provisioning-arm-artifacts.yml +
//       bff-artifact CI) UPLOADS compiled ARM JSON manifests/templates,
//       BFF zip-deploy artifacts, and solution zips into the container
//       (Storage Blob Data Contributor via the GitHub Actions OIDC
//       principal, when provided).
//     - The L2 Worker's H2a (BicepInfraDeployOptions), H6
//       (SolutionImportOptions) and H9 (BffDeployOptions) handlers
//       DOWNLOAD those artifacts at dispatch time via the shared
//       control-plane UAMI (Storage Blob Data Reader -- read-only; the
//       Worker never writes artifacts). All three options classes carry a
//       `ProvisioningArtifactsContainerUri` field whose Validate() fails
//       fast (NFR-05) when unset -- this module is the resource that URI
//       points at.
//
// AUDIT REFERENCE (post-authoring-audit-2026-08-20.md, Wave G-8 Batch 2)
//   - Defect #5: this storage account existed NOWHERE in infrastructure/**
//     (no Microsoft.Storage module in the control-plane stack) -- H2a/H6/H9
//     were a hard blocker on first live dispatch.
//   - Defect #3 (closed here): Storage Blob Data Reader for the L2 UAMI was
//     never granted anywhere.
//
// SECURITY SHAPE (ADR-028 MI-first)
//   - allowSharedKeyAccess: false -- data-plane access is AAD-RBAC only
//     (Worker via UAMI DefaultAzureCredential; CI via azure/login OIDC with
//     `--auth-mode login`). No account keys, no SAS.
//   - allowBlobPublicAccess: false; HTTPS-only; TLS 1.2 minimum.
//
// WHY A NEW MODULE (vs modules/storage-account.bicep)
//   storage-account.bicep is the per-customer BFF temp-file account
//   (temp-files/document-processing/test-documents containers, lifecycle
//   policies, legacy SystemAssigned interim param, key-based connection
//   string output). The control-plane artifacts store has a different
//   security posture (shared-key OFF from birth, Reader-not-Contributor for
//   its runtime identity) and a different lifecycle (fleet-scoped, one per
//   env, CI-writer/Worker-reader). Parameterizing all that into the legacy
//   module would blur two distinct shapes -- same rationale as
//   controlplane-app-service.bicep vs app-service.bicep.
//
// SCOPE
//   Resource-group scoped -- invoked from platform-controlplane.bicep with
//   `scope: rg` (rg-spaarke-platform-{env}), alongside the other fleet
//   control-plane resources.

@description('Name of the provisioning-artifacts Storage Account (lowercase alphanumeric, 3-24 chars, globally unique; typically sprkcpartifacts{env} per AZURE-RESOURCE-NAMING-CONVENTION.md no-hyphen storage rule).')
@minLength(3)
@maxLength(24)
param storageAccountName string

@description('Location for the Storage Account.')
param location string = resourceGroup().location

@description('SKU. Standard_LRS is sufficient for dev/staging (artifacts are CI-reproducible from a git sha, so durability requirements are low); Standard_ZRS recommended for prod (region-resilient reads keep provisioning runs alive through a zonal outage).')
@allowed(['Standard_LRS', 'Standard_ZRS', 'Standard_GRS'])
param sku string = 'Standard_LRS'

@description('Name of the blob container CI publishes provisioning artifacts into and H2a/H6/H9 read from. Default matches the container segment every *Options.ProvisioningArtifactsContainerUri test/CI convention already uses.')
param containerName string = 'provisioning-artifacts'

@description('Principal ID of the fleet-scoped control-plane UAMI (from modules/uami.bicep) granted Storage Blob Data Reader (read-only -- the Worker downloads artifacts, never writes them). Empty skips the grant (not expected in real deploys; kept optional for what-if isolation).')
param controlPlaneUamiPrincipalId string = ''

@description('Principal ID of the GitHub Actions OIDC service principal granted Storage Blob Data Contributor (CI uploads compiled ARM JSON / BFF artifacts / solution zips). Empty (default) skips the grant -- supply once the CI OIDC app-reg principal is known for this environment.')
param githubActionsOidcPrincipalId string = ''

@description('Tags for the resource.')
param tags object = {}

// ============================================================================
// VARIABLES -- built-in Azure role definition IDs
// ============================================================================

// Storage Blob Data Reader -- Worker-side artifact downloads (H2a/H6/H9)
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/storage#storage-blob-data-reader
var storageBlobDataReaderRoleId = '2a2b9908-6ea1-4ae2-8e65-a410df84e7d1'

// Storage Blob Data Contributor -- CI-side artifact uploads (tasks 116/117)
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/storage#storage-blob-data-contributor
var storageBlobDataContributorRoleId = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'

// ============================================================================
// STORAGE ACCOUNT (StorageV2, AAD-RBAC-only data plane)
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
    // MI/OIDC-only data plane per ADR-028 -- no account keys, no SAS.
    allowSharedKeyAccess: false
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ============================================================================
// BLOB SERVICE + provisioning-artifacts CONTAINER
// ============================================================================

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2023-01-01' = {
  parent: storageAccount
  name: 'default'
  properties: {
    // Soft-delete safety net: CI overwrites manifest blobs in place
    // (provisioning-arm-latest.json); 7 days of undelete covers an
    // accidental bad publish without any lifecycle-cost impact.
    deleteRetentionPolicy: {
      enabled: true
      days: 7
    }
  }
}

resource artifactsContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  parent: blobService
  name: containerName
  properties: {
    publicAccess: 'None'
  }
}

// ============================================================================
// RBAC -- Reader for the Worker's UAMI, Contributor for the CI OIDC principal.
// Deterministic guid() names (idempotent, no-op over matching manual grants)
// -- same pattern as modules/controlplane-sb-rbac.bicep.
// ============================================================================

resource uamiBlobReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: storageAccount
  name: guid(storageAccount.id, controlPlaneUamiPrincipalId, storageBlobDataReaderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataReaderRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 control-plane UAMI downloads provisioning artifacts (H2a/H6/H9) -- Wave G-8 Batch 2, audit defects #3/#5'
  }
}

resource ciBlobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(githubActionsOidcPrincipalId)) {
  scope: storageAccount
  name: guid(storageAccount.id, githubActionsOidcPrincipalId, storageBlobDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributorRoleId)
    principalId: githubActionsOidcPrincipalId
    principalType: 'ServicePrincipal'
    description: 'GitHub Actions OIDC principal uploads provisioning artifacts (tasks 116/117 CI) -- Wave G-8 Batch 2, audit defect #5'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output resourceId string = storageAccount.id
output accountName string = storageAccount.name

// Container-scoped blob URI -- the EXACT value the Worker's three
// *Options__ProvisioningArtifactsContainerUri app settings must carry
// (BicepInfraDeployOptions / BffDeployOptions / SolutionImportOptions).
// primaryEndpoints.blob already ends with '/', so this concatenation yields
// https://{account}.blob.core.windows.net/{container}.
output blobUri string = '${storageAccount.properties.primaryEndpoints.blob}${containerName}'
