// infrastructure/bicep/modules/deployment-slot.bicep
//
// Staging deployment slot for zero-downtime deployments - UAMI-ONLY identity
// (T5 fix; comprehensive variant with warm-up + slot-sticky settings).
//
// PURPOSE (customer-provisioning-orchestration-r1, task 029, spec.md FR-37 / T5)
//   Comprehensive staging-slot module (vs the thin `app-service-slot.bicep`):
//   adds swap warm-up settings, slot-sticky config, and a separate site config
//   sub-resource for staging diagnostics. Binds the SAME
//   `userAssignedIdentityResourceId` to the slot that the parent App Service
//   (`modules/app-service.bicep`) binds - slot-swap does NOT rotate the
//   effective identity so KV references, Dataverse App User assignment, and
//   Graph app-roles remain valid across swaps.
//
// DEPLOYMENT FLOW
//   1. Deploy code to staging slot (identity is the shared UAMI)
//   2. Azure hits warm-up endpoints (applicationInitialization + WEBSITE_SWAP_WARMUP_PING_*)
//   3. Health check passes on /healthz
//   4. CI/CD gate or manual approval triggers swap
//   5. Swap promotes staging => production atomically (identity does NOT change)
//   Constraint: Auto-swap disabled - require manual approval or CI/CD gate.
//
// SPEC / ADR REFERENCES
//   - spec.md FR-37, § MUST rules, § New Components row 4.
//   - design.md §7 (per-customer UAMI), T5 trap catalog.
//   - ADR-028 MUST rules (UAMI-only server identity; no co-emitted SA-MI).
//
// OUT OF SCOPE (task 029)
//   - `keyVaultReferenceIdentity` PATCH on the slot's site config is H4 handler
//     work (spec.md § MUST rules). This module does NOT PATCH site config.
//
// USAGE NOTE
//   As of task 029 authoring, this module has NO in-tree callers - the active
//   staging-slot invocation in `platform.bicep` uses the thinner
//   `app-service-slot.bicep`. This module is retained for future use (per its
//   original intent: comprehensive slot with warm-up + sticky settings) and
//   refactored here for consistency: any future caller MUST pass a UAMI.
//
// BREAKING CHANGE (any future caller migration required)
//   REMOVED unconditional SystemAssigned identity; ADDED REQUIRED
//   `userAssignedIdentityResourceId` param. No current caller impact (no
//   in-tree caller); documented for symmetry with `app-service-slot.bicep`.

@description('Name of the parent App Service')
param appServiceName string

@description('Location for the deployment slot')
param location string = resourceGroup().location

@description('Slot name')
param slotName string = 'staging'

@description('Runtime stack')
param runtimeStack string = 'DOTNETCORE|10.0'

@description('Health check path for the staging slot')
param healthCheckPath string = '/healthz'

@description('App settings for the staging slot (merged with swap warm-up settings)')
param appSettings object = {}

@description('Slot-sticky setting names (not swapped to production)')
param slotSettingNames array = [
  'ASPNETCORE_ENVIRONMENT'
  'APPLICATIONINSIGHTS_CONNECTION_STRING'
  'ApplicationInsightsAgent_EXTENSION_VERSION'
  'WEBSITE_SWAP_WARMUP_PING_PATH'
  'WEBSITE_SWAP_WARMUP_PING_STATUSES'
]

@description('Resource ID of the User-Assigned Managed Identity to bind to this slot. REQUIRED (T5 structural fix). MUST equal the `userAssignedIdentityResourceId` bound to the parent App Service (`modules/app-service.bicep`) so slot-swap does NOT rotate the effective identity. Sourced from `modules/uami.bicep` (task 028) as `uami.outputs.id`.')
param userAssignedIdentityResourceId string

@description('Tags for the resource')
param tags object = {}

// ============================================================================
// PARENT APP SERVICE (must already exist)
// ============================================================================

resource appService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: appServiceName
}

// ============================================================================
// STAGING DEPLOYMENT SLOT (SAME UAMI as parent - structural T5 fix)
// ============================================================================

// Merge caller-provided app settings with swap warm-up settings
var swapWarmUpSettings = {
  WEBSITE_SWAP_WARMUP_PING_PATH: healthCheckPath
  WEBSITE_SWAP_WARMUP_PING_STATUSES: '200'
}
var mergedSettings = union(appSettings, swapWarmUpSettings)

// Identity binding MUST use the same `userAssignedIdentityResourceId` as the
// parent App Service. No SystemAssigned MI (per-slot SA-MI is exactly the
// drift T5 eliminates; co-emission with UAMI is an ADR-028 anti-pattern).
resource stagingSlot 'Microsoft.Web/sites/slots@2023-01-01' = {
  parent: appService
  name: slotName
  location: location
  tags: union(tags, {
    slot: slotName
    purpose: 'zero-downtime-deployment'
  })
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: appService.properties.serverFarmId
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: runtimeStack
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: healthCheckPath

      // Auto-swap disabled — require manual approval or CI/CD gate
      autoSwapSlotName: ''

      appSettings: [for setting in items(mergedSettings): {
        name: setting.key
        value: setting.value
      }]
    }
  }
}

// ============================================================================
// WARM-UP CONFIGURATION
// ============================================================================

// Warm-up for Linux App Service is configured via app settings:
//   WEBSITE_SWAP_WARMUP_PING_PATH  — endpoint Azure hits before completing swap
//   WEBSITE_SWAP_WARMUP_PING_STATUSES — expected HTTP status (200)
// Combined with healthCheckPath on the slot, this ensures the .NET runtime,
// DI container, and caches are fully warm before traffic is routed.
//
// Additional slot web config for diagnostics during staging validation:
resource stagingSlotWebConfig 'Microsoft.Web/sites/slots/config@2023-01-01' = {
  parent: stagingSlot
  name: 'web'
  properties: {
    autoHealEnabled: false
    detailedErrorLoggingEnabled: false
    httpLoggingEnabled: true
    requestTracingEnabled: true
  }
}

// ============================================================================
// SLOT-STICKY SETTINGS
// ============================================================================

// Configure slot-sticky settings on the parent App Service
// These settings stay with their respective slot and are NOT swapped
// e.g., ASPNETCORE_ENVIRONMENT=Staging stays on staging even after swap
resource slotConfigNames 'Microsoft.Web/sites/config@2023-01-01' = {
  parent: appService
  name: 'slotConfigNames'
  properties: {
    appSettingNames: slotSettingNames
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

output slotId string = stagingSlot.id
output slotName string = stagingSlot.name
output slotDefaultHostName string = stagingSlot.properties.defaultHostName
output slotUrl string = 'https://${stagingSlot.properties.defaultHostName}'
