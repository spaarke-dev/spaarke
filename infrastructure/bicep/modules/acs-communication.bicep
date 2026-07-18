// infrastructure/bicep/modules/acs-communication.bicep
// Per-customer-boundary Azure Communication Services resource + Event Grid system topic
// + chat-event subscription -> BFF webhook + dead-letter Storage.
// (messaging-communication-app-r1, task 012, FR-18)
//
// EXTENSION of the ADR-027 per-customer provisioning orchestrator (customer.bicep) — mirrors
// the existing membership-topic module (topic + subscription attached per customer boundary).
// This is NOT a parallel provisioning path (root CLAUDE.md §11): it reuses the customer RG,
// the customer Storage account (dead-letter), and the same per-boundary isolation ADR-027 owns.
//
// Residency (design §8.7 / decision D-01): ACS `dataLocation` is IMMUTABLE at create time, so
// data residency is achieved by provisioning a SEPARATE ACS resource per boundary with the data
// location chosen here. Choose acsDataLocation deliberately at onboarding — it cannot be changed
// later without recreating the resource.
//
// Event capture (design §8.3): the subscription wires the ACS chat events to the BFF inbound
// webhook (task 030 ingress) and configures a dead-letter Storage destination FROM DAY ONE.
// Event Grid delivery is at-least-once / unordered / may duplicate — handler idempotency is
// task 031's concern, not this module's.

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Name of the per-boundary ACS resource (e.g. sprk-{customerId}-acs).')
param acsResourceName string

@description('ACS data location. IMMUTABLE at create time (D-01) — the residency mechanism. e.g. UnitedStates, Europe, Australia, UK.')
param acsDataLocation string = 'UnitedStates'

@description('Control-plane location for the ACS resource and Event Grid system topic (ACS + system topic are always global).')
param resourceLocation string = 'global'

@description('BFF inbound webhook URL the Event Grid subscription delivers chat events to (task 030 ingress).')
param webhookEndpointUrl string

@description('ACS chat event types subscribed from day one (design §8.3).')
param includedEventTypes array = [
  'Microsoft.Communication.ChatMessageReceivedInThread'
  'Microsoft.Communication.ChatMessageEditedInThread'
  'Microsoft.Communication.ChatMessageDeletedInThread'
  'Microsoft.Communication.ParticipantAddedToThread'
  'Microsoft.Communication.ParticipantRemovedFromThread'
]

@description('Resource id of the Storage account used for Event Grid dead-lettering (reuses the customer Storage account).')
param deadLetterStorageAccountResourceId string

@description('Blob container that receives dead-lettered Event Grid events.')
param deadLetterContainerName string = 'acs-eventgrid-deadletter'

@description('Name of the Event Grid system topic on the ACS resource.')
param systemTopicName string = '${acsResourceName}-egt'

@description('Name of the event subscription (chat events -> BFF webhook).')
param eventSubscriptionName string = 'chat-events-to-bff'

@description('Tags applied to all resources.')
param tags object = {}

// ============================================================================
// ACS RESOURCE (per boundary; data location IMMUTABLE at create — D-01)
// ============================================================================

resource acs 'Microsoft.Communication/communicationServices@2023-04-01' = {
  name: acsResourceName
  location: resourceLocation
  tags: tags
  properties: {
    dataLocation: acsDataLocation
  }
}

// ============================================================================
// EVENT GRID SYSTEM TOPIC (on the ACS resource)
// ============================================================================

resource systemTopic 'Microsoft.EventGrid/systemTopics@2023-12-15-preview' = {
  name: systemTopicName
  location: resourceLocation
  tags: tags
  properties: {
    source: acs.id
    topicType: 'Microsoft.Communication.CommunicationServices'
  }
}

// ============================================================================
// EVENT SUBSCRIPTION — chat events -> BFF webhook + dead-letter Storage (day one)
// ============================================================================
//   destination:            WebHook -> BFF inbound (task 030). ACS sends a one-time
//                           SubscriptionValidationEvent the webhook must echo (task 030 handshake).
//   deadLetterDestination:  StorageBlob on the customer Storage account, FROM DAY ONE (§8.3),
//                           so undeliverable events are captured for operator inspection.
//   retryPolicy:            30 attempts / 24h TTL (Event Grid defaults; at-least-once semantics
//                           — handler dedupe on ACS message id is task 031, NFR-03).
//
resource chatEventSubscription 'Microsoft.EventGrid/systemTopics/eventSubscriptions@2023-12-15-preview' = {
  parent: systemTopic
  name: eventSubscriptionName
  properties: {
    destination: {
      endpointType: 'WebHook'
      properties: {
        endpointUrl: webhookEndpointUrl
        maxEventsPerBatch: 1
        preferredBatchSizeInKilobytes: 64
      }
    }
    filter: {
      includedEventTypes: includedEventTypes
    }
    deadLetterDestination: {
      endpointType: 'StorageBlob'
      properties: {
        resourceId: deadLetterStorageAccountResourceId
        blobContainerName: deadLetterContainerName
      }
    }
    retryPolicy: {
      maxDeliveryAttempts: 30
      eventTimeToLiveInMinutes: 1440
    }
    eventDeliverySchema: 'EventGridSchema'
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output acsResourceId string = acs.id
output acsHostName string = acs.properties.hostName
output acsDataLocation string = acsDataLocation
output systemTopicName string = systemTopic.name
output eventSubscriptionName string = chatEventSubscription.name
