// infrastructure/bicep/modules/service-bus.bicep
// Azure Service Bus module

@description('Name of the Service Bus namespace')
param serviceBusName string

@description('Location for the Service Bus')
param location string = resourceGroup().location

@description('SKU for Service Bus')
@allowed(['Basic', 'Standard', 'Premium'])
param sku string = 'Standard'

@description('Queue names to create')
param queueNames array = ['sdap-jobs', 'document-indexing']

@description('Tags for the resource')
param tags object = {}

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: serviceBusName
  location: location
  tags: tags
  sku: {
    name: sku
    tier: sku
  }
  properties: {
    minimumTlsVersion: '1.2'
    // Keyless (owner D13, task 244): SAS is rejected; the stamp BFF sends and receives with its UAMI
    // (Azure Service Bus Data Sender + Data Receiver, modules/bff-runtime-rbac.bicep), configured by
    // namespace (`ServiceBus__FullyQualifiedNamespace`).
    disableLocalAuth: true
  }
}

resource queues 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = [for queueName in queueNames: {
  parent: serviceBusNamespace
  name: queueName
  properties: {
    lockDuration: 'PT5M'
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    requiresSession: false
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    maxDeliveryCount: 10
    enablePartitioning: false
  }
}]

// No SAS authorization rule and no connection-string output (task 244): local auth is disabled.

output serviceBusId string = serviceBusNamespace.id
output serviceBusName string = serviceBusNamespace.name
output serviceBusEndpoint string = serviceBusNamespace.properties.serviceBusEndpoint
