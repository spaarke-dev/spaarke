// infrastructure/bicep/modules/app-service.bicep
//
// BFF Sprk.Bff.Api App Service module - UAMI-ONLY identity (T5 structural fix).
//
// PURPOSE (customer-provisioning-orchestration-r1, task 029, spec.md FR-37 / T5)
//   Emits the BFF App Service with a User-Assigned Managed Identity that is the
//   SAME identity bound to both the production App Service AND its staging slot
//   (staging slot binding is `app-service-slot.bicep` / `deployment-slot.bicep`,
//   also refactored in this task). This is the T5 structural fix:
//
//     T5 (pre-Phase-C anti-pattern): SystemAssigned MI has a different objectId
//     on the production slot vs the staging slot AND rotates on any App Service
//     delete/recreate. Every Key Vault reference, Dataverse App User assignment,
//     and Graph app-role grant is bound to the wrong-slot principalId after a
//     slot-swap => silent 503 window until manually re-granted.
//
//     T5 (post-Phase-C fix - this module + task 030 + H4): ONE UAMI (from
//     `modules/uami.bicep`, task 028) bound to BOTH slots via
//     `identity.userAssignedIdentities`; downstream RBAC/App-User/Graph grants
//     bind to the UAMI's stable principalId. Slot-swap no longer rotates the
//     effective identity, KV references stay valid, no 503 window.
//
// SPEC REFERENCES
//   - spec.md FR-37, § MUST rules ("MUST PATCH App Service keyVaultReferenceIdentity
//     to UAMI on both slots"), § New Components row 4.
//   - design.md §7 (per-customer UAMI), T5 trap catalog.
//   - ADR-028 MUST rules ("MUST use DefaultAzureCredential (managed identity)
//     for all server outbound"); anti-pattern is emitting SystemAssigned + UAMI
//     together (`type: 'SystemAssigned, UserAssigned'`) - MUST NOT.
//
// OUT OF SCOPE FOR THIS MODULE (task 029)
//   - RBAC assignments (Key Vault Secrets User, Storage Blob Data Contributor,
//     Cognitive Services User, Cosmos DB Data Contributor, etc.) belong to
//     task 030 (`role-assignment-*.bicep` modules). This module intentionally
//     removes the previous SA-MI-tied `keyVaultAccessPolicy` resource block:
//     the SA-MI principal it granted access to no longer exists, and the
//     UAMI-based replacement is task 030's responsibility (grant is added at
//     the caller level using `uami.outputs.principalId`).
//   - `keyVaultReferenceIdentity` PATCH is the H4 handler's post-deploy job
//     (spec.md § MUST rules); this module does NOT PATCH the site config.
//
// COORDINATION NOTE (task 033 predecessor)
//   Task 033 created a dedicated `modules/controlplane-app-service.bicep`
//   (UAMI-only from birth) as a Path A documented exception because THIS task
//   had not yet shipped. After this task lands, `controlplane-app-service.bicep`
//   becomes topology-equivalent to `<this module> + <slot module>`; deletion
//   decision belongs to a future consolidation task (do NOT delete here).
//
// BREAKING CHANGE (caller migration required)
//   This refactor REMOVES `enableManagedIdentity` (SA-MI toggle) and
//   `keyVaultName` (SA-MI-tied KV access policy) parameters. Existing callers
//   (`platform.bicep`, `stacks/model1-shared.bicep`, `stacks/model2-full.bicep`)
//   still pass these params and will FAIL `az bicep build` at the stack level
//   until they are migrated to pass `userAssignedIdentityResourceId` instead.
//   Follow-on task will migrate the callers atomically (see
//   `notes/task-029-deviations.md` for the recommendation).
//   Module-level `az bicep build modules/app-service.bicep` succeeds; stack-
//   level builds fail by design (semantic break of T5 fix).

@description('Name of the App Service')
param appServiceName string

@description('App Service Plan resource ID')
param appServicePlanId string

@description('Location for the App Service')
param location string = resourceGroup().location

@description('Runtime stack')
param runtimeStack string = 'DOTNETCORE|10.0'

@description('App settings as key-value object')
param appSettings object = {}

@description('Resource ID of the User-Assigned Managed Identity to bind to BOTH production and staging slots. REQUIRED (T5 structural fix - no BFF App Service may be deployed without an explicit UAMI). Sourced from `modules/uami.bicep` (task 028); consumed at the caller as `uami.outputs.id`. The same value MUST be passed to the corresponding `app-service-slot.bicep` / `deployment-slot.bicep` invocation so the slot binds the SAME identity.')
param userAssignedIdentityResourceId string

@description('Subnet ID for VNet integration (outbound traffic). Leave empty to disable.')
param vnetIntegrationSubnetId string = ''

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// APP SERVICE (UAMI-only per ADR-028 + T5 structural fix)
// ============================================================================
//
// Identity block: `type: 'UserAssigned'` with the caller-supplied UAMI as the
// single entry in `userAssignedIdentities`. MUST NOT co-emit SystemAssigned
// (`type: 'SystemAssigned, UserAssigned'`) - that is the anti-pattern spec.md
// § New Components row 4 + ADR-028 MUST rules explicitly eliminate.
//
// The staging slot (in `app-service-slot.bicep` / `deployment-slot.bicep`)
// binds the SAME `userAssignedIdentityResourceId` value, so a slot-swap does
// not rotate the effective principal.

resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    virtualNetworkSubnetId: !empty(vnetIntegrationSubnetId) ? vnetIntegrationSubnetId : null
    vnetRouteAllEnabled: !empty(vnetIntegrationSubnetId)
    siteConfig: {
      linuxFxVersion: runtimeStack
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/health'
      appSettings: [for setting in items(appSettings): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

// ============================================================================
// RBAC (out of scope for this module - task 030 owns UAMI role assignments)
// ============================================================================
//
// The previous version of this module emitted a
// `Microsoft.KeyVault/vaults/accessPolicies@2023-07-01` resource that granted
// the SystemAssigned MI (`appService.identity.principalId`) `get`/`list` on
// secrets + certificates. That block has been REMOVED with the SA-MI:
//   - The SA-MI principalId no longer exists on this module's App Service.
//   - The UAMI's principalId is discoverable at the CALLER via
//     `uami.outputs.principalId` (task 028), not from the App Service resource.
//   - Task 030 (`role-assignment-keyvault.bicep`) grants "Key Vault Secrets User"
//     to the UAMI at the caller level, replacing the removed access-policy grant
//     with modern RBAC (aligned with vault-level RBAC per key-vault.bicep).
//
// Any KV RBAC in an intermediate deployment state (between this task landing
// and task 030 landing) is the operator's/H4 handler's responsibility per
// spec.md § MUST rules + design.md T5 interim mitigation notes.

// ============================================================================
// OUTPUTS
// ============================================================================
//
// `appServicePrincipalId` is intentionally REMOVED - the App Service resource
// no longer has a SystemAssigned principalId. Downstream consumers that need
// the identity's principalId MUST read `uami.outputs.principalId` at the
// caller level (task 028 output).

output appServiceId string = appService.id
output appServiceName string = appService.name
output appServiceDefaultHostName string = appService.properties.defaultHostName
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
