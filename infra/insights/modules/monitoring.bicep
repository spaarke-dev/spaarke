// =====================================================================
// monitoring.bicep
// Resolves EXISTING App Insights + Log Analytics resources (does NOT
// create new ones — Spaarke Dev already has both per azure-inventory.md).
// Outputs the connection strings the Function App needs so its traces
// correlate with the BFF's.
// Per knowledge/azure-functions-isv/README.md §4: shared App Insights
// = end-to-end distributed traces across BFF -> Function -> AI Search.
// =====================================================================

@description('Name of existing App Insights resource to reference.')
param appInsightsName string

@description('Name of existing Log Analytics workspace to reference.')
param logAnalyticsWorkspaceName string

resource appInsights 'Microsoft.Insights/components@2020-02-02' existing = {
  name: appInsightsName
}

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2023-09-01' existing = {
  name: logAnalyticsWorkspaceName
}

output appInsightsId string = appInsights.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsInstrumentationKey string = appInsights.properties.InstrumentationKey
output logAnalyticsId string = logAnalytics.id
output logAnalyticsCustomerId string = logAnalytics.properties.customerId
