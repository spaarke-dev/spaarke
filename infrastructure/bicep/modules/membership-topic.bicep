// infrastructure/bicep/modules/membership-topic.bicep
// Service Bus topic + subscription for membership-change events (R3 Phase 2)
//
// Provisioned per D3 decision (resolved 2026-06-20, spec.md FR-2P2.3):
//   - Topic `sprk-membership-changes` (NOT a queue; NOT reusing ServiceBusJobProcessor `sdap-jobs` queue)
//   - One subscription `recon-junction-updater` in R3
//   - Future consumers (cache warmers, downstream indexers, Teams notify, VIP cache invalidator)
//     can add subscriptions without infra migration
//
// Authentication:
//   - BFF App Service Managed Identity is granted Azure Service Bus Data Sender on the topic
//     (publish MembershipChangedEvent during mutation endpoints)
//   - BFF App Service Managed Identity is granted Azure Service Bus Data Receiver on the
//     subscription (MembershipJunctionUpdater handler reads + acks messages)
//   - Per ADR-028 — all outbound auth via DefaultAzureCredential / Managed Identity
//
// This module is additive to the existing service-bus.bicep (queue-centric); it does NOT
// modify the namespace itself, only attaches the topic + subscription + RBAC.

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Name of the existing Service Bus namespace to attach the topic to')
param serviceBusNamespaceName string

@description('Name of the topic for membership-change events')
param topicName string = 'sprk-membership-changes'

@description('Name of the subscription consumed by MembershipJunctionUpdater')
param subscriptionName string = 'recon-junction-updater'

@description('Principal ID of the BFF App Service Managed Identity (granted Sender on topic + Receiver on subscription). Pass empty to skip RBAC assignment.')
param bffPrincipalId string = ''

// ============================================================================
// VARIABLES — built-in Azure role definition IDs
// ============================================================================

// Azure Service Bus Data Sender — publish messages to topic
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-sender
var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'

// Azure Service Bus Data Receiver — receive + complete messages from subscription
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-receiver
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

// ============================================================================
// EXISTING SERVICE BUS NAMESPACE (assumed already provisioned by service-bus.bicep)
// ============================================================================

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

// ============================================================================
// TOPIC — sprk-membership-changes
// ============================================================================
//   maxSizeInMegabytes:            1024 (1 GB — default; ample headroom at expected volume)
//   defaultMessageTimeToLive:      P14D (14 days — long enough for nightly recon to handle
//                                        a Friday-night outage covering the weekend)
//   enablePartitioning:            false (single partition; expected volume well under
//                                        partition split threshold; simpler ordering semantics)
//   enableBatchedOperations:       true  (publishers batch sends for throughput)
//   requiresDuplicateDetection:    false (idempotency handled at consumer side per FR-2P2.4:
//                                        MembershipJunctionUpdater is keyed on
//                                        {personId, entityRecordId, sourceField})
//
resource membershipTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: topicName
  properties: {
    maxSizeInMegabytes: 1024
    defaultMessageTimeToLive: 'P14D'
    enablePartitioning: false
    enableBatchedOperations: true
    requiresDuplicateDetection: false
    supportOrdering: true
  }
}

// ============================================================================
// SUBSCRIPTION — recon-junction-updater
// ============================================================================
//   maxDeliveryCount:                          10 (10 attempts before DLQ — covers
//                                                 transient outages; matches existing
//                                                 sdap-jobs queue policy)
//   lockDuration:                              PT5M (5-min lock — handler is upsert/delete
//                                                   on Dataverse, typical < 1s; lock allows
//                                                   for transient Dataverse throttling)
//   defaultMessageTimeToLive:                  P14D (matches topic TTL)
//   deadLetteringOnMessageExpiration:          true (move expired messages to DLQ for
//                                                   operator inspection; expiration in
//                                                   normal operation indicates a stuck
//                                                   consumer that needs attention)
//   enableBatchedOperations:                   true
//   requiresSession:                           false (no per-key ordering required; junction
//                                                   updater handles each event independently
//                                                   via per-row idempotency key)
//
resource reconJunctionUpdaterSubscription 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: membershipTopic
  name: subscriptionName
  properties: {
    maxDeliveryCount: 10
    lockDuration: 'PT5M'
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    enableBatchedOperations: true
    requiresSession: false
  }
}

// ============================================================================
// RBAC — BFF Managed Identity → Sender on topic + Receiver on subscription
// ============================================================================
// Pattern matches cosmos-db.bicep + key-vault.bicep: conditional assignment via
// `if (!empty(...))`; idempotent guid() based on scope + principal + role.

resource bffSenderOnTopic 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bffPrincipalId)) {
  scope: membershipTopic
  name: guid(membershipTopic.id, bffPrincipalId, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: bffPrincipalId
    principalType: 'ServicePrincipal'
    description: 'BFF App Service MI publishes MembershipChangedEvent to topic (R3 Phase 2)'
  }
}

resource bffReceiverOnSubscription 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (!empty(bffPrincipalId)) {
  scope: reconJunctionUpdaterSubscription
  name: guid(reconJunctionUpdaterSubscription.id, bffPrincipalId, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: bffPrincipalId
    principalType: 'ServicePrincipal'
    description: 'BFF App Service MI consumes recon-junction-updater subscription (MembershipJunctionUpdater handler) (R3 Phase 2)'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output topicId string = membershipTopic.id
output topicName string = membershipTopic.name
output subscriptionId string = reconJunctionUpdaterSubscription.id
output subscriptionName string = reconJunctionUpdaterSubscription.name
