// =====================================================================
// managed-identity.bicep
// Per-tenant User-Assigned Managed Identity for the Insights Function App.
// Per knowledge/azure-functions-isv/README.md §3: UAMI (not system-assigned)
// so identity survives Function App recreate. Per ADR-024/D-27: no
// ClientSecretCredential; this UAMI is the auth boundary for the
// tenant's Insights workload.
// =====================================================================

@description('Tenant short name (lowercase, used in resource naming).')
@minLength(2)
@maxLength(32)
param tenantShortName string

@description('Azure region.')
param location string = resourceGroup().location

@description('Common resource tags.')
param tags object = {}

@description('Resource name prefix (e.g. "insights").')
param prefix string = 'insights'

// Build name: insights-<tenant>-uami (per-tenant UAMI)
var uamiName = '${prefix}-${tenantShortName}-uami'

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: uamiName
  location: location
  tags: tags
}

output uamiId string = uami.id
output uamiName string = uami.name
output uamiPrincipalId string = uami.properties.principalId
output uamiClientId string = uami.properties.clientId
