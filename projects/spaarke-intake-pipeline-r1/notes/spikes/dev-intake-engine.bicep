// projects/spaarke-intake-pipeline-r1/notes/spikes/dev-intake-engine.bicep
//
// DEV-ONLY wrapper for the task 008 spike (spaarke-intake-pipeline-r1). NOT a stamp template.
//
// Why a wrapper and not customer.bicep: the shared dev environment was never deployed from customer.bicep. It is a
// hand-built set of resource groups (BFF `spaarke-bff-dev` in rg-spaarke-dev, BFF identity `mi-bff-api-dev` and the
// Log Analytics workspace in spe-infrastructure-westus2, Service Bus `spaarke-servicebus-dev` in SharePointEmbedded).
// customer.bicep at subscription scope would create a whole stamp, so this file calls ONLY the three task 002 modules
// (uami, intake-staging-storage, intake-engine-job) against the existing dev resources. Same modules, same names
// inside them, no copy of their content.
//
// Placement on dev (recorded in notes/spikes/phase0-spike-report.md):
//   - engine identity: rg-spaarke-dev (next to the BFF and mi-ontology-writer-dev)
//   - staging container + lifecycle policy: sprkspaarkedevebillsa (rg-spaarke-dev), the BFF's only keyless storage
//     account on dev; it had NO management policy before this deploy, so the singleton policy holds only the intake rule
//   - queues, Container Apps environment and Job: SharePointEmbedded, because the job module creates the queues on an
//     `existing` namespace in its own resource group. Location westus2 (next to the ACR and storage).
//
// Three-step deploy (POML constraint, #3210): (1) image empty; (2) AcrPull on sprkcontrolplanedevacr to output
// intakeEngineIdentityObjectId; (3) redeploy with image = the digest-pinned `image` from latest.json.

targetScope = 'subscription'

@description('Digest-pinned engine image. Empty = step 1 (no Job).')
param intakeEngineImage string = ''

@description('Platform ACR login server.')
param intakeEngineRegistryLoginServer string = 'sprkcontrolplanedevacr.azurecr.io'

@description('The dev BFF APPLICATIONINSIGHTS_CONNECTION_STRING (same value the BFF has).')
@secure()
param appInsightsConnectionString string

@description('Principal id of the dev BFF identity mi-bff-api-dev.')
param bffPrincipalId string = '9fd47efb-7962-492b-ac44-e5ccd0268ebb'

param location string = 'westus2'

var tags = {
  application: 'spaarke'
  environment: 'dev'
  project: 'spaarke-intake-pipeline-r1'
  managedBy: 'bicep'
}

resource rgBff 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: 'rg-spaarke-dev'
}

resource rgSpe 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: 'SharePointEmbedded'
}

resource rgInfra 'Microsoft.Resources/resourceGroups@2024-03-01' existing = {
  name: 'spe-infrastructure-westus2'
}

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  scope: rgInfra
  name: 'spe-logs-dev-67e2xz'
}

module uamiIntakeEngine '../../../../infrastructure/bicep/modules/uami.bicep' = {
  scope: rgBff
  name: 'dev-uami-intake-engine'
  params: {
    name: 'mi-intake-engine-dev'
    location: location
    tags: tags
  }
}

module intakeStagingStorage '../../../../infrastructure/bicep/modules/intake-staging-storage.bicep' = {
  scope: rgBff
  name: 'dev-intake-staging'
  params: {
    storageAccountName: 'sprkspaarkedevebillsa'
    jobPrincipalId: uamiIntakeEngine.outputs.principalId
    bffPrincipalId: bffPrincipalId
  }
}

module intakeEngineJob '../../../../infrastructure/bicep/modules/intake-engine-job.bicep' = {
  scope: rgSpe
  name: 'dev-intake-engine'
  params: {
    environmentName: 'spaarke-intake-cae-dev'
    jobName: 'spaarke-intake-job-dev'
    location: location
    logAnalyticsWorkspaceId: logs.id
    serviceBusNamespaceName: 'spaarke-servicebus-dev'
    identityResourceId: uamiIntakeEngine.outputs.id
    identityPrincipalId: uamiIntakeEngine.outputs.principalId
    identityClientId: uamiIntakeEngine.outputs.clientId
    bffPrincipalId: bffPrincipalId
    registryLoginServer: intakeEngineRegistryLoginServer
    grantAcrPull: false
    image: intakeEngineImage
    stagingStorageAccountName: 'sprkspaarkedevebillsa'
    stagingContainerName: intakeStagingStorage.outputs.containerName
    appInsightsConnectionString: appInsightsConnectionString
    tags: tags
  }
}

output intakeEngineIdentityObjectId string = uamiIntakeEngine.outputs.principalId
output intakeEngineIdentityClientId string = uamiIntakeEngine.outputs.clientId
output intakeEngineIdentityId string = uamiIntakeEngine.outputs.id
output intakeEngineEnvironmentId string = intakeEngineJob.outputs.environmentId
output intakeEngineJobName string = intakeEngineJob.outputs.jobName
output intakeEngineJobId string = intakeEngineJob.outputs.jobId
output intakeEngineRequestQueueName string = intakeEngineJob.outputs.requestQueueName
output intakeEngineCompletionQueueName string = intakeEngineJob.outputs.completionQueueName
output intakeStagingContainerName string = intakeStagingStorage.outputs.containerName
