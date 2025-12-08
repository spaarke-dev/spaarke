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

// Authorization rule for application access
resource sendListenRule 'Microsoft.ServiceBus/namespaces/AuthorizationRules@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'SpaarkeAppAccess'
  properties: {
    rights: ['Send', 'Listen']
  }
}

output serviceBusId string = serviceBusNamespace.id
output serviceBusName string = serviceBusNamespace.name
output serviceBusEndpoint string = serviceBusNamespace.properties.serviceBusEndpoint
#disable-next-line outputs-should-not-contain-secrets
output serviceBusConnectionString string = sendListenRule.listKeys().primaryConnectionString
