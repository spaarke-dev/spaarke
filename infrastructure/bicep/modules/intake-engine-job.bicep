// infrastructure/bicep/modules/intake-engine-job.bicep
//
// INTAKE ENGINE JOB -- a Container Apps managed environment plus ONE event-triggered Container Apps Job that
// runs the NDA redline engines, and the engine request queue that triggers it (spaarke-intake-pipeline-r1
// task 002, FR-01; design section 5; ADR-052 "Container Apps Jobs", ADR-004 Amendment A2).
// This is the FIRST Container Apps resource in the repo: there is no module to extend.
//
// HOW IT RUNS
//   The BFF run job stages the source .docx and edit plan in blob storage (intake-staging-storage.bicep) and
//   sends an ids-only Job Contract envelope to `requestQueueName`. A KEDA `azure-servicebus` scale rule sees
//   the message and starts one execution; the execution runs the engines, writes outputs to deterministic
//   staging paths and sends a completion Job Contract to the BFF's EXISTING jobs queue (`jobsQueueName`,
//   ServiceBusJobProcessor consumes one queue only; no completion queue is created here). Service Bus
//   redelivery is the retry: replicaRetryLimit is 0 and the 10-minute replicaTimeout is a hard stop.
//
// IDENTITY (ADR-028) -- NO KEYS ANYWHERE
//   The Job runs as the stamp's EXISTING user-assigned managed identity (modules/uami.bicep, passed in; no
//   identity is created here). The KEDA scaler authenticates with that identity (`identity` on the scale rule,
//   so the scaler needs no secret or connection string), the registry pull uses it, and the containers
//   reach Service Bus and blob storage with it (DefaultAzureCredential + AZURE_CLIENT_ID). No listKeys(),
//   no SAS, no connection string, no secret output (CustomerStampKeylessTemplateTests). The managed
//   environment sends logs to Log Analytics through a diagnostic setting (destination azure-monitor), NOT the
//   `customerId` + shared-key pair that appLogsConfiguration.logAnalyticsConfiguration takes -- that pair
//   needs a listKeys() call.
//   Exception, deliberate and not a credential: APPLICATIONINSIGHTS_CONNECTION_STRING, the same value the
//   BFF receives as an app setting (customer.bicep, monitoring.outputs.connectionString). It is held as a Job
//   secret and read with secretRef, not placed in the plain configuration.
//
// RBAC -- exactly these, each at QUEUE (or registry) scope, nothing namespace-wide
//   - Azure Service Bus Data Receiver on the request queue      (consume)
//   - Azure Service Bus Data Sender   on the request queue      (retry copy: a scheduled copy carrying
//                                                                PriorDeliveryCount, like ServiceBusJobProcessor)
//   - Azure Service Bus Data Sender   on the BFF jobs queue     (completion message only)
//   - AcrPull on the registry                                   (optional grant, see registry note below)
//   Blob access is granted in intake-staging-storage.bicep (container scope).
//   NOTE: the stamp UAMI ALSO holds namespace-wide Service Bus Sender + Receiver for the BFF
//   (modules/bff-runtime-rbac.bicep), so on a stamp the queue-scoped grants here are a documented contract,
//   not a narrowing; real least privilege needs a dedicated job identity, which this module accepts unchanged
//   (it takes the identity as parameters). Task brief and design say "the stamp's managed identity", so the
//   stamp UAMI is what customer.bicep passes.
//
// REGISTRY (escalation trigger 2 in the task): this module never CREATES a registry. `registryName` names an
//   EXISTING registry in this resource group; with `grantAcrPull` true (default) the identity gets AcrPull on
//   it. For a registry elsewhere (the platform registry from controlplane-acr.bicep), pass grantAcrPull=false
//   and grant the identity AcrPull there (controlplane-acr.bicep `controlPlaneUamiPrincipalId`). Which
//   registry a stamp uses is owed by customer-provisioning-orchestration-r1 and the owner.
//
// QUEUE
//   Created here (not in the shared stamp queue list) because service-bus.bicep gives every queue
//   maxDeliveryCount 10 and no per-queue settings; the engine queue needs 5 (JobContract.MaxDeliveryCount).
//   Duplicate detection is OFF, like every BFF queue (JobQueueDuplicateDetectionOffGuardTests); idempotency is
//   the engine's atomic blob marker (ADR-004 A2).
//
// WHY A QUEUE LOCK OF 5 MINUTES -- the maximum Service Bus allows. An engine run can take up to 10 minutes, so
//   the entrypoint renews the lock (ADR-004 A2); the namespace lock duration is not the run budget.

@description('Container Apps environment name.')
param environmentName string

@description('Container Apps Job name.')
param jobName string

@description('Location for the environment and job.')
param location string = resourceGroup().location

@description('Resource id of the stamp Log Analytics workspace (monitoring.bicep logAnalyticsId). The environment sends its console and system logs there through a diagnostic setting.')
param logAnalyticsWorkspaceId string

@description('Existing stamp Service Bus namespace name (service-bus.bicep output). Not created here.')
param serviceBusNamespaceName string

@description('Engine request queue created here. The engine pipeline contract: the BFF run job sends to it, the Job consumes it.')
param requestQueueName string = 'intake-engine-requests'

@description('The BFF jobs queue the completion message goes to (the one ServiceBusJobProcessor consumes). Existing; the identity gets Data Sender on it only.')
param jobsQueueName string = 'sdap-jobs'

@description('Resource id of the stamp user-assigned managed identity (uami.outputs.id). The Job runs as it.')
param identityResourceId string

@description('Principal (object) id of the same identity (uami.outputs.principalId) for the RBAC grants.')
@minLength(36)
param identityPrincipalId string

@description('Client id of the same identity (uami.outputs.clientId), exposed to the container as AZURE_CLIENT_ID so DefaultAzureCredential picks the user-assigned identity.')
@minLength(36)
param identityClientId string

@description('Name of an EXISTING Azure Container Registry in this resource group that holds the engine image. Never created here.')
@minLength(5)
param registryName string

@description('Grant the identity AcrPull on registryName. Set false when the registry is elsewhere and the grant is made there.')
param grantAcrPull bool = true

@description('Full image reference, for example {registry}.azurecr.io/sprk-intake-engine:{tag}. The tag comes from the image build (task 003).')
param image string

@description('Stamp storage account name; the staging container lives in it (intake-staging-storage.bicep).')
param stagingStorageAccountName string

@description('Staging container name (intake-staging-storage.bicep).')
param stagingContainerName string = 'intake-staging'

@description('Application Insights connection string (monitoring.outputs.connectionString), the same value the BFF gets. Held as a Job secret.')
@secure()
param appInsightsConnectionString string

@description('Hard stop per execution, in seconds. 600 = the 10-minute job timeout (spec NFR-05); the platform allows more, this module does not.')
@minValue(60)
@maxValue(600)
param replicaTimeoutSeconds int = 600

@description('Maximum parallel executions. The spec assumes at most 200 requests per day per stamp, so a small cap is enough.')
@minValue(1)
@maxValue(10)
param maxExecutions int = 5

@description('KEDA polling interval, in seconds.')
param pollingIntervalSeconds int = 30

@description('Container CPU cores. Python 3.11 + lxml over one .docx.')
param cpu string = '1.0'

@description('Container memory.')
param memory string = '2Gi'

@description('Tags for all resources.')
param tags object = {}

// ============================================================================
// VARIABLES -- built-in role definition ids
// ============================================================================

// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-sender
var serviceBusDataSenderRoleId = '69a216fc-b8fb-44d8-bc22-1f3c2cd27a39'
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/integration#azure-service-bus-data-receiver
var serviceBusDataReceiverRoleId = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'
// https://learn.microsoft.com/azure/role-based-access-control/built-in-roles/containers#acrpull
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

// ============================================================================
// EXISTING RESOURCES (referenced only)
// ============================================================================

resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
}

resource jobsQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' existing = {
  parent: serviceBusNamespace
  name: jobsQueueName
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: registryName
}

// ============================================================================
// ENGINE REQUEST QUEUE -- shape copied from service-bus.bicep, with per-queue settings
// ============================================================================

resource requestQueue 'Microsoft.ServiceBus/namespaces/queues@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: requestQueueName
  properties: {
    lockDuration: 'PT5M'
    maxSizeInMegabytes: 1024
    requiresDuplicateDetection: false
    requiresSession: false
    defaultMessageTimeToLive: 'P14D'
    deadLetteringOnMessageExpiration: true
    // JobContract.MaxDeliveryCount (ADR-004): the 5th failed delivery dead-letters.
    maxDeliveryCount: 5
    enablePartitioning: false
  }
}

// ============================================================================
// RBAC -- queue scope, deterministic guid() names
// ============================================================================

resource receiverOnRequestQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: requestQueue
  name: guid(requestQueue.id, identityPrincipalId, serviceBusDataReceiverRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataReceiverRoleId)
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Intake engine Job consumes the engine request queue (spaarke-intake-pipeline-r1 task 002)'
  }
}

resource senderOnRequestQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: requestQueue
  name: guid(requestQueue.id, identityPrincipalId, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Intake engine Job sends the scheduled retry copy (PriorDeliveryCount) to the engine request queue, then completes the original, like ServiceBusJobProcessor (task 002)'
  }
}

resource senderOnJobsQueue 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: jobsQueue
  name: guid(jobsQueue.id, identityPrincipalId, serviceBusDataSenderRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', serviceBusDataSenderRoleId)
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Intake engine Job sends the completion Job Contract to the BFF jobs queue only (task 002)'
  }
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (grantAcrPull) {
  scope: registry
  name: guid(registry.id, identityPrincipalId, acrPullRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: identityPrincipalId
    principalType: 'ServicePrincipal'
    description: 'Intake engine Job pulls the engine image (task 002)'
  }
}

// ============================================================================
// MANAGED ENVIRONMENT -- Consumption only; logs through a diagnostic setting (no shared key)
// ============================================================================

resource managedEnvironment 'Microsoft.App/managedEnvironments@2025-01-01' = {
  name: environmentName
  location: location
  tags: tags
  properties: {
    appLogsConfiguration: {
      destination: 'azure-monitor'
    }
    workloadProfiles: [
      {
        name: 'Consumption'
        workloadProfileType: 'Consumption'
      }
    ]
    zoneRedundant: false
  }
}

resource environmentDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  scope: managedEnvironment
  name: 'to-log-analytics'
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      {
        category: 'ContainerAppConsoleLogs'
        enabled: true
      }
      {
        category: 'ContainerAppSystemLogs'
        enabled: true
      }
    ]
  }
}

// ============================================================================
// CONTAINER APPS JOB -- event trigger, KEDA azure-servicebus on the request queue, identity auth
// ============================================================================

resource engineJob 'Microsoft.App/jobs@2025-01-01' = {
  name: jobName
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${identityResourceId}': {}
    }
  }
  properties: {
    environmentId: managedEnvironment.id
    workloadProfileName: 'Consumption'
    configuration: {
      triggerType: 'Event'
      replicaTimeout: replicaTimeoutSeconds
      // Service Bus redelivery (MaxDeliveryCount 5, then the DLQ) is the retry; a platform retry would run a
      // second engine against a message the first may already have completed.
      replicaRetryLimit: 0
      // No `ingress` block: the Job exposes nothing (ADR-052).
      eventTriggerConfig: {
        parallelism: 1
        replicaCompletionCount: 1
        scale: {
          minExecutions: 0
          maxExecutions: maxExecutions
          pollingInterval: pollingIntervalSeconds
          rules: [
            {
              name: 'intake-engine-requests'
              type: 'azure-servicebus'
              metadata: {
                queueName: requestQueueName
                // The namespace NAME (not the FQDN) for identity-authenticated KEDA.
                namespace: serviceBusNamespaceName
                messageCount: '1'
              }
              // Managed-identity auth: the scaler uses this identity, no secret or connection string.
              identity: identityResourceId
            }
          ]
        }
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: identityResourceId
        }
      ]
      secrets: [
        {
          name: 'appinsights-connection-string'
          value: appInsightsConnectionString
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'intake-engine'
          image: image
          resources: {
            cpu: json(cpu)
            memory: memory
          }
          env: [
            // DefaultAzureCredential selects the stamp UAMI.
            { name: 'AZURE_CLIENT_ID', value: identityClientId }
            { name: 'INTAKE_SERVICEBUS_FQDN', value: '${serviceBusNamespaceName}.servicebus.windows.net' }
            { name: 'INTAKE_REQUEST_QUEUE', value: requestQueueName }
            { name: 'INTAKE_COMPLETION_QUEUE', value: jobsQueueName }
            { name: 'INTAKE_STAGING_ACCOUNT_URL', value: 'https://${stagingStorageAccountName}.blob.${environment().suffixes.storage}' }
            { name: 'INTAKE_STAGING_CONTAINER', value: stagingContainerName }
            { name: 'INTAKE_MAX_DELIVERY_COUNT', value: '5' }
            { name: 'INTAKE_JOB_TIMEOUT_SECONDS', value: string(replicaTimeoutSeconds) }
            { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', secretRef: 'appinsights-connection-string' }
          ]
        }
      ]
    }
  }
  dependsOn: [
    // The registry pull and the scaler both use the identity: its grants must exist before the first execution.
    receiverOnRequestQueue
    senderOnRequestQueue
    senderOnJobsQueue
    acrPull
  ]
}

output environmentName string = managedEnvironment.name
output environmentId string = managedEnvironment.id
output jobName string = engineJob.name
output jobId string = engineJob.id
output requestQueueName string = requestQueue.name
output completionQueueName string = jobsQueue.name
