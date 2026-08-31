// infrastructure/bicep/modules/model1-shared-l2-rbac.bicep
//
// MODEL 1 SHARED-TIER -- L2 CONTROL-PLANE UAMI RBAC on shared source services.
//
// PURPOSE
//   Grants the fleet-scoped L2 control-plane UAMI (sprk-controlplane-{env}-uami,
//   provisioned by infrastructure/bicep/platform-controlplane.bicep) the seven
//   data-plane / management-plane role assignments it needs on the Model 1
//   SHARED source services:
//
//     - A20.1 Cognitive Services User    on shared OpenAI account
//     - A20.2 Cognitive Services User    on shared Doc Intelligence account
//     - A20.3 Search Service Contributor on shared AI Search service
//     - A20.4 Azure Service Bus Data Owner on shared SB namespace
//     - A20.5 Storage Account Contributor  on shared Storage account
//     - A20.6 Redis Cache Contributor    on shared Redis
//     - A21   Website Contributor        on shared BFF App Service
//
// AUDIT REFERENCE (customer-provisioning-orchestration-r1 task 203b,
//                  punch list rows A20 + A21)
//   A20: task-200-completion-notes.md "Deferred #1" -- H4-shared handler
//        (Sprk.Provisioning.ControlPlane.Core/Handlers/KvSecretsPopulation/
//        SdkSourceServiceKeyExtractor.cs) reads current API keys / connection
//        strings from the shared source services via SDK GetKeys() /
//        listKeys() calls; each of the 6 branches requires the corresponding
//        data-plane role on the source resource.
//   A21: task-201-completion-notes.md "Deferred #1" -- H4b handler
//        (BulkAppSettings, KuduContainerLogFetcher.cs) fetches Kudu container
//        logs from the BFF App Service via SCM /api/logs/docker under an
//        ARM-scoped bearer token; H9 handler zip-deploys BFF artifacts to
//        the same site. Website Contributor covers both.
//
// WHY A MODULE (BCP139 forces the split)
//   model1-shared.bicep uses `targetScope = 'subscription'`. A role assignment
//   whose `scope` symbol resolves to a RG-nested resource (Microsoft.Web/sites,
//   Microsoft.Cache/redis, ...) cannot be declared inline at subscription
//   scope -- Bicep rejects with BCP139 ("A resource's scope must match the
//   scope of the Bicep file for it to be deployable. You must use modules to
//   deploy resources to a different scope."). Same mechanism-forced module
//   split as controlplane-sb-rbac.bicep (that one is cross-RG via BCP165;
//   this one is subscription -> RG via BCP139). The parent stack invokes
//   this module with `scope: sharedRg` (see model1-shared.bicep call site).
//
// SCOPE
//   Resource-group scoped -- invoked from model1-shared.bicep with
//   `scope: sharedRg` (rg-spaarke-shared-{env}). All 7 target resources live
//   in that RG (provisioned by the sibling shared modules).
//
// IDEMPOTENCY
//   Deterministic guid(scope, principalId, roleId) names -- safe over pre-
//   existing manual grants (same principal+role+scope tuple yields the same
//   guid; re-deploy is a no-op create).

@description('Principal ID of the fleet-scoped L2 control-plane UAMI. Empty skips ALL grants (what-if isolation only).')
param controlPlaneUamiPrincipalId string = ''

@description('Name of the shared Azure OpenAI account (Microsoft.CognitiveServices/accounts).')
param sharedOpenAiName string

@description('Name of the shared Doc Intelligence account (Microsoft.CognitiveServices/accounts).')
param sharedDocIntelligenceName string

@description('Name of the shared Azure AI Search service (Microsoft.Search/searchServices).')
param sharedAiSearchName string

@description('Name of the shared Service Bus namespace (Microsoft.ServiceBus/namespaces).')
param sharedServiceBusName string

@description('Name of the shared Storage account (Microsoft.Storage/storageAccounts).')
param sharedStorageAccountName string

@description('Name of the shared Redis Cache (Microsoft.Cache/redis).')
param sharedRedisName string

@description('Name of the shared BFF App Service (Microsoft.Web/sites).')
param sharedBffAppServiceName string

// ============================================================================
// VARIABLES -- built-in Azure role definition IDs
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles
// ============================================================================

var cognitiveServicesUserRoleId = 'a97b65f3-24c7-4388-baec-2e87135dc908'
var searchServiceContributorRoleId = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'
var serviceBusDataOwnerRoleId = '090c5cfd-751d-490a-894a-3ce6f1109419'
var storageAccountContributorRoleId = '17d1049b-9a84-46fb-8f53-869881c3d3ab'
var redisCacheContributorRoleId = 'e0f68234-74aa-48ed-b826-c38b57376e17'
var websiteContributorRoleId = 'de139f84-1756-47ae-9be6-808fbbe84772'

// ============================================================================
// EXISTING SOURCE RESOURCES
// ============================================================================

resource sharedOpenAi 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: sharedOpenAiName
}

resource sharedDocIntel 'Microsoft.CognitiveServices/accounts@2023-05-01' existing = {
  name: sharedDocIntelligenceName
}

resource sharedAiSearch 'Microsoft.Search/searchServices@2023-11-01' existing = {
  name: sharedAiSearchName
}

resource sharedServiceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: sharedServiceBusName
}

resource sharedStorage 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: sharedStorageAccountName
}

resource sharedRedis 'Microsoft.Cache/redis@2023-08-01' existing = {
  name: sharedRedisName
}

resource sharedBffAppService 'Microsoft.Web/sites@2023-01-01' existing = {
  name: sharedBffAppServiceName
}

// ============================================================================
// RBAC -- A20.1 Cognitive Services User on OpenAI
// ============================================================================

resource l2OpenAiCsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: sharedOpenAi
  name: guid(sharedOpenAi.id, controlPlaneUamiPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4-shared reads OpenAI key via SDK (task 203b, punch list A20)'
  }
}

// ============================================================================
// RBAC -- A20.2 Cognitive Services User on Doc Intelligence
// ============================================================================

resource l2DocIntelCsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: sharedDocIntel
  name: guid(sharedDocIntel.id, controlPlaneUamiPrincipalId, cognitiveServicesUserRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', cognitiveServicesUserRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4-shared reads DocIntel key via SDK (task 203b, punch list A20)'
  }
}

// ============================================================================
// RBAC -- A20.3 Search Service Contributor on AI Search
// ============================================================================

resource l2SearchContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: sharedAiSearch
  name: guid(sharedAiSearch.id, controlPlaneUamiPrincipalId, searchServiceContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4-shared reads AI Search admin key via SDK (task 203b, punch list A20)'
  }
}

// ============================================================================
// RBAC -- A20.4 Azure Service Bus Data Owner on shared SB namespace
// ============================================================================

resource l2SbDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: sharedServiceBus
  name: guid(sharedServiceBus.id, controlPlaneUamiPrincipalId, serviceBusDataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataOwnerRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4-shared reads SB SAS keys via namespace-management API (task 203b, punch list A20)'
  }
}

// ============================================================================
// RBAC -- A20.5 Storage Account Contributor on shared Storage
// ============================================================================

resource l2StorageContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: sharedStorage
  name: guid(sharedStorage.id, controlPlaneUamiPrincipalId, storageAccountContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageAccountContributorRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4-shared reads Storage keys via SDK (task 203b, punch list A20)'
  }
}

// ============================================================================
// RBAC -- A20.6 Redis Cache Contributor on shared Redis
// ============================================================================

resource l2RedisContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: sharedRedis
  name: guid(sharedRedis.id, controlPlaneUamiPrincipalId, redisCacheContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', redisCacheContributorRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4-shared reads Redis keys via SDK (task 203b, punch list A20)'
  }
}

// ============================================================================
// RBAC -- A21 Website Contributor on shared BFF App Service
// ============================================================================

resource l2SharedBffWebsiteContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(controlPlaneUamiPrincipalId)) {
  scope: sharedBffAppService
  name: guid(sharedBffAppService.id, controlPlaneUamiPrincipalId, websiteContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', websiteContributorRoleId)
    principalId: controlPlaneUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'L2 UAMI -- H4b Kudu docker-log fetch + H9 BFF zip-deploy on shared BFF App Service (task 203b, punch list A21 / task 201 Deferred #1)'
  }
}

// ============================================================================
// OUTPUTS -- consumed by the parent stack's own outputs for verification-cli scripts.
// ============================================================================

output openAiCsUserRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(sharedOpenAi.id, controlPlaneUamiPrincipalId, cognitiveServicesUserRoleId) : ''
output docIntelCsUserRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(sharedDocIntel.id, controlPlaneUamiPrincipalId, cognitiveServicesUserRoleId) : ''
output searchContributorRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(sharedAiSearch.id, controlPlaneUamiPrincipalId, searchServiceContributorRoleId) : ''
output sbDataOwnerRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(sharedServiceBus.id, controlPlaneUamiPrincipalId, serviceBusDataOwnerRoleId) : ''
output storageContributorRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(sharedStorage.id, controlPlaneUamiPrincipalId, storageAccountContributorRoleId) : ''
output redisContributorRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(sharedRedis.id, controlPlaneUamiPrincipalId, redisCacheContributorRoleId) : ''
output sharedBffWebsiteContributorRoleAssignmentName string = !empty(controlPlaneUamiPrincipalId) ? guid(sharedBffAppService.id, controlPlaneUamiPrincipalId, websiteContributorRoleId) : ''
