// infrastructure/bicep/modules/controlplane-sb-queue.bicep
//
// Fleet Service Bus queue for the L2 control-plane dispatcher (task 108 /
// DS-5 C5.4 / C4.6).
//
// PURPOSE
//   Declares the sprk-provisioning-jobs queue as a Bicep-managed CHILD
//   resource of the fleet-scoped Service Bus namespace, WITHOUT creating the
//   namespace itself (per ADR-036 reuse - the namespace is env infra,
//   already provisioned by service-bus.bicep for a different queue set).
//
//   This is a dedicated module (not inline in platform-controlplane.bicep)
//   because the fleet namespace lives in a DIFFERENT resource group than the
//   L2 stamp (`SharePointEmbedded` on dev vs `rg-spaarke-platform-{env}`),
//   and Bicep rejects cross-resource-group child-resource declarations at
//   the file's ambient scope with BCP165 ("A resource's computed scope must
//   match that of the Bicep file... You must use modules to deploy resources
//   to a different scope."). The caller (platform-controlplane.bicep)
//   invokes this module with an explicit `scope: resourceGroup(...)`
//   pointing at the namespace's resource group.
//
//   modules/service-bus.bicep is NOT reused here: it applies UNIFORM queue
//   properties (dedup off, sessions off, 1024 MB) across its own managed
//   queue list (`sdap-jobs`, `document-indexing`) - the wrong shape for this
//   queue's DS-2/DS-2b session-serialized + FR-22 dedup requirements.
//   modules/membership-topic.bicep is the closer structural precedent
//   (existing-namespace reference + new child resource, additive, does not
//   modify the namespace) though it targets a topic/subscription, not a
//   queue.
//
// CREATE-TIME-ONLY PROPERTIES (Azure Service Bus)
//   requiresDuplicateDetection and requiresSession CANNOT be changed on an
//   existing queue via `az deployment` / ARM PATCH - Azure silently ignores
//   (or, depending on API version, rejects) an attempt to flip them on a
//   queue that already exists with different values. Landing this module is
//   necessary but NOT sufficient to turn dedup/sessions on for the LIVE dev
//   queue (created via bare `az servicebus queue create` defaults - both
//   OFF today). The live queue must be deleted and recreated once; see the
//   runbook: projects/customer-provisioning-orchestration-r1/notes/queue-recreate-runbook-2026-08.md.
//
// SPEC REFERENCES (customer-provisioning-orchestration-r1)
//   - spec.md FR-22:  Level-1 (wire-level) idempotency via SB duplicate
//                     detection; 3-level idempotency stack overall.
//   - spec.md §4D I5 / DS-2 / DS-2b: session-serialized per-customer
//                     dispatch (SessionId = CustomerId).
//   - DS-5 §C5.4/§C4.6 (design-study-ds5-cat456-remediation.md): the
//                     verified defect + exact property block this module
//                     implements.
// -----------------------------------------------------------------------------

@description('Name of the existing fleet-scoped Service Bus namespace to attach the queue to. Not created by this module.')
param serviceBusNamespaceName string

@description('Name of the queue. Defaults to the canonical fleet dispatch queue name (ServiceBusModule.cs DefaultQueueName).')
param queueName string = 'sprk-provisioning-jobs'

// ============================================================================
// EXISTING SERVICE BUS NAMESPACE (assumed already provisioned by env infra -
// service-bus.bicep or equivalent; this module never creates a namespace)
// ============================================================================

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

// ============================================================================
// QUEUE — sprk-provisioning-jobs
// ============================================================================
//   requiresSession:                     true  - DS-2/DS-2b session-serialized
//                                                 per-customer dispatch decision;
//                                                 task 102's ServiceBusSessionProcessor
//                                                 requires this; SessionId = CustomerId
//                                                 already set on every enqueue
//                                                 (ServiceBusHandlerEnqueuer.cs header).
//   requiresDuplicateDetection:          true  - FR-22 Level-1 (wire-level) idempotency.
//   duplicateDetectionHistoryTimeWindow: PT1H  - must exceed the longest reconciler
//                                                 retry re-enqueue window for the same
//                                                 paramHash; handlers run <=30-60 min
//                                                 per DS-2b §1.2, so PT1H is the
//                                                 documented safe floor. Task 107 (the
//                                                 HandlerEnvelope `attempt` field) MUST
//                                                 land before/alongside this queue's
//                                                 live recreation, or every §4C
//                                                 RetryableWithCleanup auto-retry within
//                                                 the PT1H window is silently dropped by
//                                                 SB dedup (identical MessageId as the
//                                                 original attempt).
//   lockDuration:                        PT5M  - matches service-bus.bicep's existing
//                                                 queues + membership-topic.bicep's
//                                                 subscription.
//   maxDeliveryCount:                    10    - matches the repo-wide convention.
//   deadLetteringOnMessageExpiration:    true  - expired/exhausted messages go to DLQ
//                                                 for operator inspection, not silent loss.
//
resource provisioningJobsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: queueName
  properties: {
    lockDuration: 'PT5M'
    maxDeliveryCount: 10
    deadLetteringOnMessageExpiration: true
    requiresDuplicateDetection: true
    duplicateDetectionHistoryTimeWindow: 'PT1H'
    requiresSession: true
  }
}

// ============================================================================
// OUTPUTS — consumed by platform-controlplane.bicep (re-exported), task 110's
// RBAC module (namespace/queue scope), task 113's deploy script.
// ============================================================================

output namespaceId string = serviceBusNamespace.id
output namespaceName string = serviceBusNamespace.name
output queueId string = provisioningJobsQueue.id
output queueName string = provisioningJobsQueue.name
