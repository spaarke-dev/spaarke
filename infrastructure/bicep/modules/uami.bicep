// infrastructure/bicep/modules/uami.bicep
// Per-customer User-Assigned Managed Identity module.
//
// Purpose (customer-provisioning-orchestration-r1, task 028, FR-37,
// spec.md § New Components row 4, design.md §7):
//   ONE long-lived identity per customer environment that survives App Service
//   redeploys AND binds to BOTH production + staging slots of the per-customer
//   BFF App Service. This is the structural pre-requisite for eliminating T5
//   (slot-swap 503 window): today's app-service.bicep emits a System-Assigned MI
//   whose objectId differs between slots + rotates on delete/recreate, so KV
//   RBAC grants + Dataverse App User registration + Graph app-role assignments
//   all drift silently on every swap. A single UAMI attached to both slots
//   makes all downstream RBAC + app-registration binding stable.
//
// Downstream consumers (bound in later tasks, NOT this one):
//   - task 029: app-service.bicep + app-service-slot.bicep bind this UAMI to
//               both slots via `identity.userAssignedIdentities` and PATCH
//               `keyVaultReferenceIdentity` to `resourceId` on both slots (T1).
//   - task 030: role-assignment-keyvault.bicep + storage/cognitive/cosmos role
//               modules assign `principalId` (Key Vault Secrets User, etc.).
//   - H4 handler: PATCH `keyVaultReferenceIdentity` uses `resourceId`.
//   - H10 handler: registers the Dataverse App User with `clientId` as the
//               application ID; grants Graph app-roles onto this UAMI's SP
//               (from `GraphAppRoles.cs`, T3 parity).
//   - Task 011 #3b (BFF shared-lib ClientSecret -> MI migration): unblocked
//               once this identity outlives App Service instances.
//
// Scope of THIS module (per task 028 constraints):
//   Author the identity object + outputs ONLY. No RBAC assignments here
//   (task 030). No App Service binding here (task 029). Do NOT co-emit a
//   System-Assigned MI on the App Service alongside this UAMI - per ADR-028
//   MUST rules ("MUST use DefaultAzureCredential (managed identity) for all
//   server outbound") the two co-existing is the anti-pattern this module
//   exists to eliminate.

@description('Name of the User-Assigned Managed Identity (per-customer). Recommended pattern: sprk-{env}-{customerId}-uami. Case-insensitive; 3-128 chars.')
@minLength(3)
@maxLength(128)
param name string

@description('Location for the UAMI. Defaults to resource group location.')
param location string = resourceGroup().location

@description('Tag envelope applied to the UAMI. Spaarke standard: env, customerId, owner. Do NOT hardcode - always pass from caller.')
param tags object = {}

// ============================================================================
// USER-ASSIGNED MANAGED IDENTITY
// ============================================================================
//
// API version 2023-01-31: current stable GA release of
// Microsoft.ManagedIdentity/userAssignedIdentities. This version is used by
// Azure Verified Modules + is the latest non-preview stable at 2026-08-17.
// (Selection rationale documented in notes/task-028-deviations.md.)

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: name
  location: location
  tags: tags
}

// ============================================================================
// OUTPUTS
// ============================================================================
//
// All four outputs are load-bearing for downstream consumption (see
// header comment). Do NOT rename without updating tasks 029, 030, H4, H10.

@description('Resource ID of the UAMI. Consumed by App Service identity.userAssignedIdentities map key (task 029) and by App Service keyVaultReferenceIdentity PATCH (task 029 + H4). Also POML step 2 "resourceId".')
output id string = uami.id

@description('Name of the UAMI resource. Convenience for downstream logging + Dataverse env-var writes.')
output name string = uami.name

@description('Principal (object) ID of the UAMI service principal. Consumed by ALL role assignments in task 030 (Key Vault Secrets User, Storage Blob Data Contributor, Cognitive Services User, Cosmos DB Data Contributor, etc.) + Graph app-role grants (H10).')
output principalId string = uami.properties.principalId

@description('Client (application) ID of the UAMI. Consumed by Graph token acquisition scope (H10 app-only Graph client) + Dataverse App User application ID field (H10 registration) + BFF shared-lib MI migration (task 011 #3b).')
output clientId string = uami.properties.clientId
