// infrastructure/bicep/modules/controlplane-acr.bicep
//
// L2 CONTROL-PLANE Azure Container Registry (fleet-scoped) + pull/push RBAC.
//
// PURPOSE
//   Emits the platform ACR that hosts the DS-1b Exchange
//   ApplicationAccessPolicy sidecar image (sprk-provisioning-sidecar --
//   Dockerfile authored by task 114, CI build+push wired by task 115's
//   publish workflow). The Worker App Service's sitecontainer
//   (modules/controlplane-worker-app-service.bicep, exchange-policy-sidecar)
//   pulls from this registry via the shared control-plane UAMI once
//   `acrImageTag` points here and `sidecarAuthType` is 'UserAssigned'.
//
// AUDIT REFERENCE (post-authoring-audit-2026-08-20.md, Wave G-8 Batch 2)
//   - Defect #10: NO Microsoft.ContainerRegistry resource existed anywhere
//     in infrastructure/** (the sidecar CI header falsely claimed task 101
//     authored it) -- H14a's /apply-policy would 404 forever against the
//     static-site placeholder image.
//   - Defect #4 (closed here): AcrPull for the L2 UAMI was never granted
//     anywhere -- a UserAssigned-auth sitecontainer pull would 401.
//
// SECURITY SHAPE (ADR-028 MI-first)
//   - adminUserEnabled: false -- no admin username/password; pulls are
//     UAMI-token-authenticated (AcrPull), pushes are CI-OIDC-authenticated
//     (AcrPush). No registry credentials ever land in KV or app settings.
//
// SCOPE
//   Resource-group scoped -- invoked from platform-controlplane.bicep with
//   `scope: rg` (rg-spaarke-platform-{env}), alongside the other fleet
//   control-plane resources.

@description('Name of the container registry (alphanumeric only, 5-50 chars, globally unique; typically sprkcontrolplane{env}acr -- ACR names cannot contain hyphens).')
@minLength(5)
@maxLength(50)
param acrName string

@description('Location for the registry.')
param location string = resourceGroup().location

@description('SKU. Basic is sufficient for dev (single small sidecar image, low pull volume); Standard recommended for prod (higher storage/throughput headroom + webhooks). Premium only if geo-replication or private endpoints become requirements.')
@allowed(['Basic', 'Standard', 'Premium'])
param sku string = 'Basic'

@description('Principal ID of the fleet-scoped control-plane UAMI (from modules/uami.bicep) granted AcrPull -- the Worker sitecontainer pulls the sidecar image with this identity (sidecarAuthType=UserAssigned). Empty skips the grant (not expected in real deploys; kept optional for what-if isolation).')
param controlPlaneUamiPrincipalId string = ''

@description('Principal ID of the GitHub Actions OIDC service principal granted AcrPush (task 115 sidecar CI build+push). Empty (default) skips the grant -- supply once the CI OIDC app-reg principal is known for this environment.')
param githubActionsOidcPrincipalId string = ''

@description('Tags for the resource.')
param tags object = {}

// ============================================================================
// VARIABLES -- built-in Azure role definition IDs
// ============================================================================

// AcrPull -- Worker sitecontainer image pull via UAMI
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/containers#acrpull
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

// AcrPush -- CI image push (includes pull)
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/containers#acrpush
var acrPushRoleId = '8311e382-0749-46cb-b1d6-9b7aacc770eb'

// ============================================================================
// CONTAINER REGISTRY (token-auth only; no admin user per ADR-028)
// ============================================================================

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: acrName
  location: location
  tags: tags
  sku: {
    name: sku
  }
  properties: {
    adminUserEnabled: false
    publicNetworkAccess: 'Enabled'
  }
}

// ============================================================================
// RBAC -- AcrPull for the Worker's UAMI, AcrPush for the CI OIDC principal.
// Deterministic guid() names (idempotent, no-op over matching manual grants)
// -- same pattern as modules/controlplane-sb-rbac.bicep.
// ============================================================================

resource uamiAcrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: containerRegistry
  name: guid(containerRegistry.id, controlPlaneUamiPrincipalId, acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 control-plane UAMI pulls the Exchange sidecar image for the Worker sitecontainer -- Wave G-8 Batch 2, audit defect #4'
  }
}

resource ciAcrPush 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(githubActionsOidcPrincipalId)) {
  scope: containerRegistry
  name: guid(containerRegistry.id, githubActionsOidcPrincipalId, acrPushRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPushRoleId)
    principalId: githubActionsOidcPrincipalId
    principalType: 'ServicePrincipal'
    description: 'GitHub Actions OIDC principal pushes the Exchange sidecar image (task 115 CI) -- Wave G-8 Batch 2, audit defect #10'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output resourceId string = containerRegistry.id
output acrName string = containerRegistry.name

// e.g. sprkcontrolplanedevacr.azurecr.io -- prefix for acrImageTag once
// task 115's CI pushes the real sidecar image
// ({loginServer}/sprk-provisioning-sidecar:{tag}).
output loginServer string = containerRegistry.properties.loginServer
