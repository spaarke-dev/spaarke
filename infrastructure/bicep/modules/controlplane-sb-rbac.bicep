// infrastructure/bicep/modules/controlplane-sb-rbac.bicep
//
// L2 CONTROL-PLANE Service Bus RBAC — Data Sender + Data Receiver for the
// fleet-scoped provisioning UAMI, at the sprk-provisioning-jobs namespace
// scope (task 110 / DS-5 C5.5).
//
// PURPOSE
//   Grants the control-plane UAMI (shared by both .Api and .Worker App
//   Services per DS-3 §3) the two built-in Service Bus data-plane roles it
//   needs against the fleet-scoped namespace:
//     - Azure Service Bus Data Sender   (.Api enqueues; .Worker's reconciler
//       re-enqueues on retry per §4C)
//     - Azure Service Bus Data Receiver (.Worker's dispatcher, task 102,
//       drains sprk-provisioning-jobs via ServiceBusSessionProcessor)
//
//   Prior to this task, DS-5's Category-5 audit found ZERO
//   Microsoft.Authorization/roleAssignments resources anywhere in
//   platform-controlplane.bicep — Sender was granted MANUALLY on dev
//   (yesterday's live-invocation attempt) and Receiver was granted NOWHERE,
//   meaning task 102's dispatcher would fail immediately at
//   ServiceBusSessionProcessor.StartProcessingAsync on first live deploy.
//   This module makes both grants Bicep-managed + idempotent. Because
//   role-assignment resource names are deterministic guid()s, deploying
//   this module against a scope/principal/role tuple that already has a
//   matching MANUAL role assignment is a no-op create (not a duplicate),
//   provided the manual assignment's role/scope/principal match exactly —
//   which they do for the Sender grant made yesterday.
//
// SCOPE — NAMESPACE, not queue
//   Grants are declared at the Service Bus NAMESPACE scope (not the
//   individual sprk-provisioning-jobs queue) — matches this task's own
//   acceptance criteria (`az role assignment list --scope
//   <namespace-resource-id>`) and is simpler than per-queue RBAC for a
//   namespace that currently hosts a single fleet queue; additional
//   control-plane queues added later inherit the same grant without a new
//   role-assignment resource.
//
// CROSS-RESOURCE-GROUP DEPLOYMENT (same BCP165 constraint as task 108's
// controlplane-sb-queue.bicep)
//   The fleet Service Bus namespace lives in a DIFFERENT resource group
//   (`SharePointEmbedded` on dev) than the L2 stamp (`rg-spaarke-platform-
//   {env}`). Bicep rejects a role assignment scoped to a resource outside
//   the file's ambient resource-group scope with BCP165 ("A resource's
//   computed scope must match that of the Bicep file... You must use
//   modules to deploy resources to a different scope.") unless declared via
//   a module invoked with an explicit `scope: resourceGroup(...)` at the
//   CALLER. This module is that module — platform-controlplane.bicep
//   invokes it with `scope: resourceGroup(serviceBusResourceGroupName)`,
//   mirroring the task 108 controlplane-sb-queue.bicep pattern exactly (see
//   that module's header for the full BCP165 rationale).
//
// DETERMINISTIC NAMING PATTERN (copied from modules/membership-topic.bicep
// bffSenderOnTopic / bffReceiverOnSubscription, lines 120-140)
//   Role-assignment resource names are `guid(scope.id, principalId,
//   roleDefinitionId)` — idempotent across repeated deploys, unique per
//   scope+principal+role tuple, and collision-free with any OTHER
//   role-assignment on the same namespace (a future differently-scoped SB
//   grant would hash to a different guid because at least one input
//   differs).
//
// SPEC / DESIGN REFERENCES (customer-provisioning-orchestration-r1)
//   - DS-5 §C5.5 (design-study-ds5-cat456-remediation.md): the finding this
//     task closes — Sender manual-only, Receiver granted nowhere;
//     "mechanically absolute for live receive" (DS-5 Ordering item 2).
//   - DS-3 §3: single shared control-plane UAMI for .Api + .Worker (v1) —
//     one principalId, two role grants (the two-UAMI least-privilege split
//     is the target shape, not required at v1).
//   - spec.md FR-22: enqueue path (Sender) is part of the 202-Accepted
//     contract; Receiver is task 102's dispatcher prerequisite.
// -----------------------------------------------------------------------------

@description('Name of the existing fleet-scoped Service Bus namespace to grant RBAC on. Not created by this module (ADR-036 reuse — matches modules/controlplane-sb-queue.bicep).')
param serviceBusNamespaceName string

@description('Principal ID of the control-plane UAMI (shared by .Api and .Worker per DS-3 §3 v1). Pass empty to skip both grants.')
param principalId string = ''

// ============================================================================
// VARIABLES — built-in Azure role definition IDs (same constants as
// modules/membership-topic.bicep)
// ============================================================================

// Azure Service Bus Data Sender — enqueue HandlerEnvelope messages onto sprk-provisioning-jobs
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-sender
var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'

// Azure Service Bus Data Receiver — drain sprk-provisioning-jobs (task 102's dispatcher)
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-receiver
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

// ============================================================================
// EXISTING SERVICE BUS NAMESPACE (fleet-scoped; provisioned by env infra,
// NOT this stack — see file header)
// ============================================================================

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

// ============================================================================
// RBAC — control-plane UAMI -> Data Sender + Data Receiver on the namespace
// ============================================================================

resource controlPlaneSenderOnNamespace 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  scope: serviceBusNamespace
  name: guid(serviceBusNamespace.id, principalId, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'L2 control-plane UAMI enqueues HandlerEnvelope messages onto sprk-provisioning-jobs (task 110 / DS-5 C5.5)'
  }
}

resource controlPlaneReceiverOnNamespace 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(principalId)) {
  scope: serviceBusNamespace
  name: guid(serviceBusNamespace.id, principalId, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: principalId
    principalType: 'ServicePrincipal'
    description: 'L2 control-plane UAMI (task 102 dispatcher, ServiceBusSessionProcessor) drains sprk-provisioning-jobs (task 110 / DS-5 C5.5)'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output namespaceId string = serviceBusNamespace.id
output namespaceName string = serviceBusNamespace.name
