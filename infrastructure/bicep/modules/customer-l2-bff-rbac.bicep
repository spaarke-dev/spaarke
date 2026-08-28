// infrastructure/bicep/modules/customer-l2-bff-rbac.bicep
//
// PER-CUSTOMER (MODEL 2 DEDICATED) -- L2 CONTROL-PLANE UAMI Website Contributor
// on the per-customer BFF App Service.
//
// PURPOSE
//   The L2 Worker's H4b handler (Sprk.Provisioning.ControlPlane.Core/Handlers/
//   BulkAppSettings/KuduContainerLogFetcher.cs, task 201) fetches Kudu docker
//   logs from the per-customer BFF App Service via SCM /api/logs/docker under
//   an ARM-scoped bearer token; the H9 handler (BFF zip-deploy, task 132)
//   deploys to the same site. Both operations require the L2 UAMI to hold
//   Website Contributor on the target App Service.
//
// AUDIT REFERENCE (customer-provisioning-orchestration-r1 task 203b,
//                  punch list row A21 / task 201 "Deferred #1")
//   customer.bicep already provisions the per-customer BFF App Service (bffApi
//   module) but never grants any RBAC to the L2 UAMI. Same "Deferred #1"
//   surface as the Model 1 shared BFF (model1-shared-l2-rbac.bicep A21) --
//   this module is the per-customer parallel for Model 2 Dedicated stamps.
//
// WHY A MODULE (BCP139 forces the split)
//   customer.bicep uses `targetScope = 'subscription'`. A role assignment
//   whose `scope` symbol resolves to a RG-nested resource
//   (Microsoft.Web/sites) cannot be declared inline at subscription scope --
//   Bicep rejects with BCP139 ("A resource's scope must match the scope of
//   the Bicep file for it to be deployable."). Parent stack invokes this
//   module with `scope: rg` (the per-customer RG).
//
// SCOPE
//   Resource-group scoped (per-customer RG rg-spaarke-{customerId}-{env}).
//
// IDEMPOTENCY
//   Deterministic guid() name -- safe over a pre-existing manual grant
//   (same principal+role+scope tuple yields the same guid; re-deploy is a
//   no-op create).

@description('Principal ID of the fleet-scoped L2 control-plane UAMI (sprk-controlplane-{env}-uami, provisioned by infrastructure/bicep/platform-controlplane.bicep). Empty skips the grant (what-if isolation only -- a real per-customer deploy always needs it for H4b + H9 to function).')
param controlPlaneUamiPrincipalId string = ''

@description('Name of the per-customer BFF App Service (Microsoft.Web/sites) provisioned by the parent customer.bicep.')
param bffAppServiceName string

// ============================================================================
// VARIABLES -- built-in Azure role definition IDs
// ============================================================================

// Website Contributor -- Kudu docker-log fetch + zip-deploy on Microsoft.Web/sites
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/web-and-mobile#website-contributor
var websiteContributorRoleId = 'de139f84-1756-47ae-9be6-808fbbe84772'

// ============================================================================
// EXISTING PER-CUSTOMER BFF APP SERVICE
// ============================================================================

resource bffAppService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: bffAppServiceName
}

// ============================================================================
// RBAC -- L2 UAMI Website Contributor on per-customer BFF App Service
// ============================================================================

resource l2WebsiteContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: bffAppService
  name: guid(bffAppService.id, controlPlaneUamiPrincipalId, websiteContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', websiteContributorRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4b Kudu docker-log fetch + H9 BFF zip-deploy on per-customer BFF App Service (task 203b, punch list A21 / task 201 Deferred #1)'
  }
}

// ============================================================================
// OUTPUTS -- consumed by parent for post-deploy `az role assignment list` verification.
// ============================================================================

output websiteContributorRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(bffAppService.id, controlPlaneUamiPrincipalId, websiteContributorRoleId) : ''
