// infrastructure/bicep/modules/customer-l2-bff-rbac.bicep
//
// PER-CUSTOMER -- L2 CONTROL-PLANE UAMI roles on the customer stamp:
//   - Website Contributor on the per-customer BFF App Service (task 203b), and
//   - Search Service Contributor + Search Index Data Reader on the stamp's
//     AI Search service (task 244, plan G16).
//
// PURPOSE
//   The L2 Worker's H4b handler (Sprk.Provisioning.ControlPlane.Core/Handlers/
//   BulkAppSettings/KuduContainerLogFetcher.cs, task 201) fetches Kudu docker
//   logs from the per-customer BFF App Service via SCM /api/logs/docker under
//   an ARM-scoped bearer token; the H9 handler (BFF zip-deploy, task 132)
//   deploys to the same site. Both operations require the L2 UAMI to hold
//   Website Contributor on the target App Service.
//
//   H2b (Handlers/AiSearchIndex, DefaultAzureCredential) creates and verifies the
//   stamp's search indexes. The service has local auth disabled (task 244), so
//   without these two Search roles every H2b call returns 403. Service Contributor
//   covers index definitions (H2b PUT + the verifier's GET /indexes/{name});
//   Index Data Reader covers H13's live /docs/search tenant-filter probe
//   (E2EAcceptance/AiSearchTenantFilterInvariantProbe). L2 writes no documents,
//   so a Reader — not Contributor — data-plane role is the least privilege.
//   The file keeps its name so the deployment name (l2-bff-rbac-*) is unchanged.
//
// AUDIT REFERENCE (customer-provisioning-orchestration-r1 task 203b,
//                  punch list row A21 / task 201 "Deferred #1")
//   customer.bicep already provisions the per-customer BFF App Service (bffApi
//   module) but never grants any RBAC to the L2 UAMI. This module grants it on
//   Model 1 stamps: H2a sends the principal for Model 1 only (task 249, owner
//   decision 2026-10-02). A Model 2 stamp is in the customer's tenant, where a role
//   assignment cannot name a principal from Spaarke's tenant (the deployment would
//   fail); L2 reaches it through its Lighthouse delegation. Its former Model 1 shared
//   parallel (model1-shared-l2-rbac.bicep) was retired with that stack by task 225a.
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

@description('Principal ID of the fleet-scoped L2 control-plane UAMI (sprk-controlplane-{env}-uami, provisioned by infrastructure/bicep/platform-controlplane.bicep). Sent by H2a for Model 1 stamps only (task 249); empty (Model 2) skips the grant.')
param controlPlaneUamiPrincipalId string = ''

@description('Name of the per-customer BFF App Service (Microsoft.Web/sites) provisioned by the parent customer.bicep.')
param bffAppServiceName string

@description('Name of the per-customer AI Search service (Microsoft.Search/searchServices) provisioned by the parent customer.bicep. The L2 UAMI gets Search Service Contributor (H2b) + Search Index Data Reader (H13 probe) on it (task 244).')
param searchServiceName string

// ============================================================================
// VARIABLES -- built-in Azure role definition IDs
// ============================================================================

// Website Contributor -- Kudu docker-log fetch + zip-deploy on Microsoft.Web/sites
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/web-and-mobile#website-contributor
var websiteContributorRoleId = 'de139f84-1756-47ae-9be6-808fbbe84772'

// Search Service Contributor -- index / indexer definitions (H2b create)
var searchServiceContributorRoleId = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'

// Search Index Data Reader -- index data plane, query only (H13 /docs/search probe)
var searchIndexDataReaderRoleId = '1407120a-92aa-4202-b7e9-c0e197c71c8f'

// ============================================================================
// EXISTING PER-CUSTOMER BFF APP SERVICE
// ============================================================================

resource bffAppService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: bffAppServiceName
}

resource searchService 'Microsoft.Search/searchServices@2023-11-01' existing = {
  name: searchServiceName
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
// RBAC -- L2 UAMI Search roles on the per-customer AI Search service (task 244, G16)
// ============================================================================

resource l2SearchServiceContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: searchService
  name: guid(searchService.id, controlPlaneUamiPrincipalId, searchServiceContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H2b creates the stamp search indexes over Entra (local auth disabled; task 244, G16)'
  }
}

resource l2SearchIndexDataReader 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: searchService
  name: guid(searchService.id, controlPlaneUamiPrincipalId, searchIndexDataReaderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataReaderRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H13 queries the stamp search indexes (tenant-filter probe) over Entra (local auth disabled; task 244, G16)'
  }
}

// ============================================================================
// OUTPUTS -- consumed by parent for post-deploy `az role assignment list` verification.
// ============================================================================

output websiteContributorRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(bffAppService.id, controlPlaneUamiPrincipalId, websiteContributorRoleId) : ''
output searchServiceContributorRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(searchService.id, controlPlaneUamiPrincipalId, searchServiceContributorRoleId) : ''
output searchIndexDataReaderRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(searchService.id, controlPlaneUamiPrincipalId, searchIndexDataReaderRoleId) : ''
