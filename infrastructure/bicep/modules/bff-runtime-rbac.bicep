// infrastructure/bicep/modules/bff-runtime-rbac.bicep
//
// BFF RUNTIME UAMI -- DATA-PLANE RBAC on Service Bus + AI Search.
//
// PURPOSE (auth-v4 PROVISIONING-CHANGE-REQUEST §10.1 Δ1 + Δ2 + §10.3)
//   auth-v4 retires admin-key / connection-string auth for the BFF's outbound
//   Service Bus + AI Search access; new contract has the BFF authenticate to
//   both services using its RUNTIME UAMI. This module binds the four data-plane
//   / service-plane role assignments the BFF UAMI needs to satisfy that contract:
//
//     A36 (§10.1 Δ1) Service Bus:
//       - Azure Service Bus Data Sender    (69a216fc-b8fb-44d8-bc22-1f3c2cd27a39)
//       - Azure Service Bus Data Receiver  (4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0)
//
//     A37 (§10.1 Δ2) AI Search:
//       - Search Index Data Contributor    (8ebe5a00-799e-43f5-93ac-243d3dce84a7)
//       - Search Service Contributor       (7ca78c08-252a-4471-8644-bb5ff32d4ba0)
//
// CRITICAL DISTINCTION — which principal
//   This module grants the stamp's *BFF RUNTIME UAMI* (mi-spaarke-{customerId}-{env})
//   roles for RUNTIME queue enqueue/dequeue + index CRUD from BFF request handlers.
//   The *L2 PROVISIONING UAMI* (sprk-controlplane-{env}-uami) is granted by
//   customer-l2-bff-rbac.bicep (A21: Website Contributor on the BFF App Service).
//   The retired A20 module (model1-shared-l2-rbac.bicep) granted the L2 UAMI the
//   same `Search Service Contributor` role on the old shared services — which is
//   why the role-assignment description below still cites A20. That description
//   is a DEPLOYED property: it is kept verbatim so an existing stamp's upgrade
//   what-if shows no Modify (ArmWhatIfDriftDetector treats Modify as drift).
//
//   ONE principal per stamp, in both tenancy models (D-12, 2026-09-28): there is
//   no shared Model 1 BFF identity. The former `sprk-{env}-shared-bff-uami`
//   caller (stacks/model1-shared.bicep) and the A20 module were retired by
//   task 225a (2026-10-01).
//
// SCOPE
//   Resource-group scoped -- invoked from customer.bicep (every customer stamp,
//   Model 1 and Model 2) with `scope: <rg>`. Both target resources live in the
//   stamp's RG.
//
// WHY A MODULE (BCP139 forces the split)
//   customer.bicep uses `targetScope = 'subscription'`. A role assignment whose
//   `scope` symbol resolves to a RG-nested resource (Microsoft.ServiceBus/
//   namespaces, Microsoft.Search/searchServices) cannot be declared inline at
//   subscription scope -- Bicep rejects with BCP139. Same mechanism-forced
//   pattern as customer-l2-bff-rbac.bicep (A21).
//
// §11 COMPONENT JUSTIFICATION (root CLAUDE.md, three-question test)
//   (Recorded 2026-08-25, when the module was added; A20 has since been retired by task 225a.)
//   1. Existing:  model1-shared-l2-rbac.bicep (A20) + customer-l2-bff-rbac.bicep
//                 granted the L2 control-plane UAMI; NO module granted
//                 the *BFF runtime UAMI* Service Bus or Search data-plane
//                 roles (grep-verified 2026-08-25).
//   2. Extension: The A20 module was L2-UAMI-only by its type discriminator
//                 (`controlPlaneUamiPrincipalId` param + descriptions cited
//                 "H4-shared handler"). Adding a second principal to that
//                 module would have violated its single-purpose design (one UAMI /
//                 one lifecycle owner per module) and destabilized the audit
//                 trail (A20 rows would have taken on Δ1/Δ2 semantics). A new
//                 sibling module was the clean split.
//   3. Cost-of-doing-nothing (concrete failure modes):
//        - BFF running under `AiSearch__ManagedIdentity__Enabled=true`
//          (A39 setting) gets 403 on every search query -> matter-search /
//          knowledge features silently broken at runtime.
//        - BFF running under `ServiceBus__FullyQualifiedNamespace` MI-only
//          (A39 setting) gets 401 on every message enqueue -> outbound Service
//          Bus jobs (SPE ingestion, agent execution, communications) silently
//          drop.
//
// IDEMPOTENCY
//   Deterministic guid(scope, principalId, roleId) names -- safe over pre-
//   existing manual grants (same principal+role+scope tuple yields the same
//   guid; re-deploy is a no-op create).

@description('Principal ID of the stamp\'s BFF RUNTIME UAMI (customer.bicep: uami.outputs.principalId — the only principal, both tenancy models). Empty skips ALL grants (what-if isolation only -- a real deploy always needs it for BFF MI-only Service Bus + AI Search to function).')
param bffUamiPrincipalId string = ''

@description('Name of the stamp\'s Service Bus namespace (Microsoft.ServiceBus/namespaces) the BFF sends to and receives from.')
param serviceBusNamespaceName string

@description('Name of the stamp\'s AI Search service (Microsoft.Search/searchServices) the BFF queries + writes index docs to.')
param searchServiceName string

// ============================================================================
// VARIABLES -- built-in Azure role definition IDs
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles
// ============================================================================

// -- A36 Service Bus data-plane roles --
// Azure Service Bus Data Sender:   send messages to queues/topics
// Azure Service Bus Data Receiver: receive + complete messages from queues/subscriptions
var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

// -- A37 AI Search data-plane / service roles --
// Search Index Data Contributor: index document CRUD (data-plane)
// Search Service Contributor:    index / indexer / datasource definitions + service-level admin
var searchIndexDataContributorRoleId = '8ebe5a00-799e-43f5-93ac-243d3dce84a7'
var searchServiceContributorRoleId = '7ca78c08-252a-4471-8644-bb5ff32d4ba0'

// ============================================================================
// EXISTING TARGET RESOURCES
// ============================================================================

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

resource searchService 'Microsoft.Search/searchServices@2023-11-01' existing = {
  name: searchServiceName
}

// ============================================================================
// -- A36 Service Bus roles (BFF runtime UAMI) --
// PROVISIONING-CHANGE-REQUEST §10.1 Δ1 + §10.3
// ============================================================================

resource bffSbDataSender 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bffUamiPrincipalId)) {
  scope: serviceBusNamespace
  name: guid(serviceBusNamespace.id, bffUamiPrincipalId, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: bffUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'BFF runtime UAMI -- Service Bus Data Sender for MI-only enqueue (auth-v4 §10.1 Δ1 + §10.3; punch row A36)'
  }
}

resource bffSbDataReceiver 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bffUamiPrincipalId)) {
  scope: serviceBusNamespace
  name: guid(serviceBusNamespace.id, bffUamiPrincipalId, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: bffUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'BFF runtime UAMI -- Service Bus Data Receiver for MI-only dequeue (auth-v4 §10.1 Δ1 + §10.3; punch row A36)'
  }
}

// ============================================================================
// -- A37 AI Search data-plane roles (BFF runtime UAMI) --
// PROVISIONING-CHANGE-REQUEST §10.1 Δ2 + §10.3
// ============================================================================

resource bffSearchIndexDataContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bffUamiPrincipalId)) {
  scope: searchService
  name: guid(searchService.id, bffUamiPrincipalId, searchIndexDataContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchIndexDataContributorRoleId)
    principalId: bffUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'BFF runtime UAMI -- Search Index Data Contributor for MI-only index doc CRUD (auth-v4 §10.1 Δ2 + §10.3; punch row A37)'
  }
}

resource bffSearchServiceContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bffUamiPrincipalId)) {
  scope: searchService
  name: guid(searchService.id, bffUamiPrincipalId, searchServiceContributorRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', searchServiceContributorRoleId)
    principalId: bffUamiPrincipalId
    principalType: 'ServicePrincipal'
    description: 'BFF runtime UAMI -- Search Service Contributor for MI-only index/indexer definitions + schema evolution (auth-v4 §10.1 Δ2 + §10.3; punch row A37). Different principal from A20 (L2 provisioning UAMI same role, different SP).'
  }
}

// ============================================================================
// OUTPUTS -- consumed by parent stacks for post-deploy `az role assignment list`
// verification (matches existing rbac-module output pattern).
// ============================================================================

output bffSbDataSenderRoleAssignmentName string = !empty(bffUamiPrincipalId) ? guid(serviceBusNamespace.id, bffUamiPrincipalId, serviceBusDataSenderRoleId) : ''
output bffSbDataReceiverRoleAssignmentName string = !empty(bffUamiPrincipalId) ? guid(serviceBusNamespace.id, bffUamiPrincipalId, serviceBusDataReceiverRoleId) : ''
output bffSearchIndexDataContributorRoleAssignmentName string = !empty(bffUamiPrincipalId) ? guid(searchService.id, bffUamiPrincipalId, searchIndexDataContributorRoleId) : ''
output bffSearchServiceContributorRoleAssignmentName string = !empty(bffUamiPrincipalId) ? guid(searchService.id, bffUamiPrincipalId, searchServiceContributorRoleId) : ''
