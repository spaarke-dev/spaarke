// infrastructure/bicep/modules/app-service-slot.bicep
//
// Staging slot for an existing App Service - UAMI-ONLY identity (T5 fix).
//
// PURPOSE (customer-provisioning-orchestration-r1, task 029, spec.md FR-37 / T5)
//   Binds the SAME `userAssignedIdentityResourceId` to the staging slot that
//   the production App Service (`modules/app-service.bicep`) binds. Ensures
//   slot-swap does NOT rotate the effective identity - the KV references,
//   Dataverse App User, and Graph app-roles bound to the UAMI's stable
//   principalId remain valid across swaps.
//
// SPEC / ADR REFERENCES
//   - spec.md FR-37, § MUST rules ("MUST PATCH App Service keyVaultReferenceIdentity
//     to UAMI on both slots"), § New Components row 4.
//   - design.md §7 (per-customer UAMI), T5 trap catalog.
//   - ADR-028 MUST rules (UAMI-only server identity; no co-emitted SA-MI).
//
// OUT OF SCOPE (task 029)
//   - `keyVaultReferenceIdentity` PATCH on the slot's site config is H4 handler
//     work (spec.md § MUST rules). This module does NOT PATCH site config.
//   - Slot-sticky app settings are configured in `deployment-slot.bicep` (the
//     more comprehensive slot module) OR via post-deploy PATCH; this thin
//     variant declares only the resource + identity + basic siteConfig.
//
// COORDINATION
//   Passing a DIFFERENT `userAssignedIdentityResourceId` to this module than
//   the one bound to the parent App Service defeats the T5 fix. Callers MUST
//   pass the same UAMI resource ID (typically `uami.outputs.id` from the
//   customer's UAMI module invocation).
//
// BREAKING CHANGE (caller migration required)
//   This refactor ADDS a REQUIRED `userAssignedIdentityResourceId` param and
//   REMOVES the previous unconditional SystemAssigned identity. Existing
//   callers (`platform.bicep`) will FAIL `az bicep build` until migrated.
//   Follow-on task will migrate the callers atomically (see
//   `notes/task-029-deviations.md`).

@description('Name of the parent App Service')
param appServiceName string

@description('Name of the deployment slot')
param slotName string = 'staging'

@description('Location for the slot')
param location string = resourceGroup().location

@description('App Service Plan resource ID')
param appServicePlanId string

@description('Resource ID of the User-Assigned Managed Identity to bind to this slot. REQUIRED (T5 structural fix). MUST equal the `userAssignedIdentityResourceId` bound to the parent App Service (`modules/app-service.bicep`) so slot-swap does NOT rotate the effective identity. Sourced from `modules/uami.bicep` (task 028) as `uami.outputs.id`.')
param userAssignedIdentityResourceId string

@description('Health check path for the slot')
param healthCheckPath string = '/healthz'

@description('Tags for the resource')
param tags object = {}

resource appService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: appServiceName
}

// ============================================================================
// STAGING SLOT (SAME UAMI as parent - structural T5 fix)
// ============================================================================
//
// Identity binding MUST use the same `userAssignedIdentityResourceId` as the
// parent App Service. No SystemAssigned MI (co-emission is an ADR-028
// anti-pattern; per-slot SA-MI is the exact drift T5 eliminates).

resource slot 'Microsoft.Web/sites/slots@2023-01-01' = {
  parent: appService
  name: slotName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: healthCheckPath
    }
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================
//
// `slotPrincipalId` is intentionally REMOVED - the slot no longer has a
// SystemAssigned principalId. Downstream consumers that need the identity's
// principalId MUST read `uami.outputs.principalId` at the caller level
// (the SAME UAMI is bound to prod + staging by design; one principalId).

output slotName string = slot.name
output slotDefaultHostName string = slot.properties.defaultHostName
output slotUrl string = 'https://${slot.properties.defaultHostName}'
