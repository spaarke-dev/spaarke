// =====================================================================
// main.bicep — Spaarke Insights Engine, single-tenant deployment unit
// =====================================================================
// Per D-52 (single-tenant deployment per customer): ONE customer = ONE
// parameter file = ONE deployment. This template orchestrates the
// per-tenant infra:
//   1. Per-tenant UAMI (auth boundary for Insights Function App)
//   2. Resolve EXISTING shared resources (App Insights, KV)
//   3. Function App shell (Flex Consumption, dotnet-isolated, no
//      functions deployed yet — task 050 ships those)
//   4. KV RBAC grant to UAMI ('Key Vault Secrets User')
//   5. spaarke-insights-index on the existing search service
//
// Per D-53 (revised): ONE derived index. The schema (artifactType
// discriminator) is loaded via loadJsonContent() so the index name and
// shape are tracked in source control as a single canonical file.
//
// Per D-08: 3072-dim contentVector matches text-embedding-3-large
// (deployed on spaarke-openai-dev in Spaarke Dev).
//
// Per D-27 / D-24: ZERO new SAS keys, ZERO new ClientSecretCredential.
// All auth is via UAMI + RBAC. (Storage AzureWebJobsStorage retains
// account-key bootstrap per current Flex Consumption requirement;
// task 050 may migrate to identity-based AzureWebJobsStorage if the
// SDK path supports it cleanly at that time.)
// =====================================================================

targetScope = 'resourceGroup'

// ---------------------------------------------------------------------
// Tenant + naming
// ---------------------------------------------------------------------
@description('Tenant short name (lowercase, 2-20 chars, alphanumeric + dashes only).')
@minLength(2)
@maxLength(20)
param tenantShortName string

@description('Customer-friendly display name (for tag value only — does not affect resource names).')
param tenantDisplayName string

@description('Azure region. Flex Consumption regional availability is narrower than Consumption — verify before changing.')
param location string = resourceGroup().location

@description('Environment label (dev/test/prod) used in tags.')
@allowed([ 'dev', 'test', 'prod' ])
param environmentName string = 'dev'

@description('Common resource name prefix.')
param resourcePrefix string = 'insights'

// ---------------------------------------------------------------------
// Existing shared-resource references (Spaarke Dev defaults)
// ---------------------------------------------------------------------
@description('Name of the existing Azure AI Search service to add spaarke-insights-index to.')
param searchServiceName string

@description('Resource group of the existing search service. Defaults to the deployment RG.')
param searchServiceResourceGroup string = resourceGroup().name

@description('Name of the existing Key Vault to grant the UAMI Secrets User access on.')
param keyVaultName string

@description('Name of the existing App Insights resource to share for correlated tracing.')
param appInsightsName string

@description('Name of the existing Log Analytics workspace App Insights writes into.')
param logAnalyticsWorkspaceName string

// ---------------------------------------------------------------------
// Function App sizing knobs
// ---------------------------------------------------------------------
@description('Memory per Flex Consumption instance, in MB.')
@allowed([ 512, 2048, 4096 ])
param functionInstanceMemoryMB int = 2048

@description('Maximum scale-out instance count for the Function App.')
@minValue(40)
@maxValue(1000)
param functionMaxInstanceCount int = 100

@description('Always-ready instance count. 1 = cold-start mitigation. 0 = pure consumption (cheaper, but cold start on first event of quiet day).')
@minValue(0)
@maxValue(20)
param functionAlwaysReadyInstanceCount int = 1

// ---------------------------------------------------------------------
// Tags (applied to every Insights-owned resource)
// ---------------------------------------------------------------------
var standardTags = {
  spaarkeProject: 'insights-engine'
  spaarkeTenant: tenantShortName
  spaarkeTenantDisplayName: tenantDisplayName
  spaarkeEnvironment: environmentName
  spaarkeDeliverable: 'D-P2'
  spaarkeManagedBy: 'bicep'
}

// ---------------------------------------------------------------------
// 1. Per-tenant Managed Identity (auth boundary)
// ---------------------------------------------------------------------
module mi 'modules/managed-identity.bicep' = {
  name: 'mod-managed-identity'
  params: {
    tenantShortName: tenantShortName
    location: location
    tags: standardTags
    prefix: resourcePrefix
  }
}

// ---------------------------------------------------------------------
// 2. Monitoring — resolve existing App Insights + Log Analytics
// ---------------------------------------------------------------------
module monitoring 'modules/monitoring.bicep' = {
  name: 'mod-monitoring'
  params: {
    appInsightsName: appInsightsName
    logAnalyticsWorkspaceName: logAnalyticsWorkspaceName
  }
}

// ---------------------------------------------------------------------
// 3. Function App shell (Flex Consumption, no functions deployed)
// ---------------------------------------------------------------------
module functionApp 'modules/function-app.bicep' = {
  name: 'mod-function-app'
  params: {
    tenantShortName: tenantShortName
    location: location
    tags: standardTags
    prefix: resourcePrefix
    uamiId: mi.outputs.uamiId
    uamiClientId: mi.outputs.uamiClientId
    appInsightsConnectionString: monitoring.outputs.appInsightsConnectionString
    instanceMemoryMB: functionInstanceMemoryMB
    maximumInstanceCount: functionMaxInstanceCount
    alwaysReadyInstanceCount: functionAlwaysReadyInstanceCount
  }
}

// ---------------------------------------------------------------------
// 4. Key Vault RBAC grant to per-tenant UAMI
// ---------------------------------------------------------------------
module kvAccess 'modules/keyvault-secrets.bicep' = {
  name: 'mod-keyvault-access'
  params: {
    keyVaultName: keyVaultName
    uamiPrincipalId: mi.outputs.uamiPrincipalId
  }
}

// ---------------------------------------------------------------------
// 5. spaarke-insights-index — created/updated on existing search service
//     Schema loaded from schemas/spaarke-insights-index.index.json so
//     the canonical shape lives in source control.
// ---------------------------------------------------------------------
var insightsIndexSchema = loadJsonContent('schemas/spaarke-insights-index.index.json')

module searchIndex 'modules/search-index.bicep' = {
  name: 'mod-search-index'
  params: {
    searchServiceName: searchServiceName
    searchServiceResourceGroup: searchServiceResourceGroup
    location: location
    tags: standardTags
    indexSchema: insightsIndexSchema
  }
}

// ---------------------------------------------------------------------
// Outputs (useful for downstream automation / task 050 deployment)
// ---------------------------------------------------------------------
output tenantShortName string = tenantShortName
output environmentName string = environmentName
output uamiId string = mi.outputs.uamiId
output uamiName string = mi.outputs.uamiName
output uamiClientId string = mi.outputs.uamiClientId
output uamiPrincipalId string = mi.outputs.uamiPrincipalId
output functionAppName string = functionApp.outputs.functionAppName
output functionAppHostName string = functionApp.outputs.functionAppDefaultHostName
output storageAccountName string = functionApp.outputs.storageAccountName
output keyVaultUri string = kvAccess.outputs.keyVaultUri
output appInsightsConnectionString string = monitoring.outputs.appInsightsConnectionString
output indexName string = searchIndex.outputs.indexName
output searchServiceEndpoint string = 'https://${searchServiceName}.search.windows.net'
