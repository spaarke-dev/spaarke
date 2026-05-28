// =====================================================================
// function-app.bicep
// Per-tenant Function App SHELL on Azure Functions Flex Consumption
// (FC1) for the Insights Engine SPE-upload consumer (D-P8 / task 050).
//
// Phase 1 (this task / D-P2): SHELL ONLY. No functions are deployed.
// The runtime, identity, networking, app settings, and storage are all
// wired so a `func azure functionapp publish ...` from task 050 works
// without infra changes.
//
// Per ADR-001 (updated 2026-05-19): Functions are permitted ONLY for
// out-of-band integration work (SPE-upload event ingest is exactly
// that). Per knowledge/azure-functions-isv/README.md §1: Flex
// Consumption is the recommended 2026 default for ISV serverless.
// Per §3: per-tenant UAMI is the auth boundary.
//
// Storage: required by Flex Consumption for deployment artifacts +
// runtime state. Created here (one per tenant) — small SKU, no
// blob/file lifecycle policies needed for the shell.
// =====================================================================

@description('Tenant short name (lowercase, used in resource naming).')
@minLength(2)
@maxLength(20)
param tenantShortName string

@description('Azure region. Flex Consumption is not available in every region — check before changing.')
param location string = resourceGroup().location

@description('Common resource tags.')
param tags object = {}

@description('Resource name prefix (e.g. "insights").')
param prefix string = 'insights'

@description('Resource ID of the per-tenant User-Assigned Managed Identity.')
param uamiId string

@description('Client ID of the per-tenant UAMI (set as AZURE_CLIENT_ID so DefaultAzureCredential prefers it).')
param uamiClientId string

@description('App Insights connection string (for correlated tracing with the BFF).')
@secure()
param appInsightsConnectionString string

@description('Memory per Flex Consumption instance, in MB. 2048 is the sweet spot for .NET-isolated workloads per knowledge guide.')
@allowed([
  512
  2048
  4096
])
param instanceMemoryMB int = 2048

@description('Maximum scale-out instance count.')
@minValue(40)
@maxValue(1000)
param maximumInstanceCount int = 100

@description('Always-ready instance count for cold-start mitigation. 1 = warm baseline; 0 = pure consumption.')
@minValue(0)
@maxValue(20)
param alwaysReadyInstanceCount int = 1

// ---------------------------------------------------------------------
// Naming
// ---------------------------------------------------------------------
// Storage account names: 3-24 lowercase alphanumeric, no dashes.
var storageName = toLower(replace(replace('${prefix}${tenantShortName}stg', '-', ''), '_', ''))
var hostingPlanName = '${prefix}-${tenantShortName}-plan'
var functionAppName = '${prefix}-${tenantShortName}-func'

// ---------------------------------------------------------------------
// Storage (required by Flex Consumption)
// ---------------------------------------------------------------------
resource storage 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: storageName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier: 'Hot'
    allowBlobPublicAccess: false
    allowSharedKeyAccess: true // Flex Consumption deployment container requires connection string at provision time; identity-based access can be enabled post-shell when functions ship.
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    publicNetworkAccess: 'Enabled'
  }
}

// Deployment container — Flex Consumption stores function package blobs here.
resource deploymentBlobService 'Microsoft.Storage/storageAccounts/blobServices@2023-05-01' = {
  parent: storage
  name: 'default'
}

resource deploymentContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-05-01' = {
  parent: deploymentBlobService
  name: 'function-app-package'
  properties: {
    publicAccess: 'None'
  }
}

// Grant the per-tenant UAMI 'Storage Blob Data Owner' on the storage account so
// the Function App can pull its deployment package and manage runtime blobs
// via Managed Identity (no SAS, no connection-string sharing post-bootstrap).
var storageBlobDataOwnerRoleId = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'

resource uamiStorageBlobDataOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  scope: storage
  name: guid(storage.id, uamiId, storageBlobDataOwnerRoleId)
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwnerRoleId)
    principalId: reference(uamiId, '2023-01-31').principalId
    principalType: 'ServicePrincipal'
  }
}

// ---------------------------------------------------------------------
// Flex Consumption hosting plan
// ---------------------------------------------------------------------
resource hostingPlan 'Microsoft.Web/serverfarms@2024-04-01' = {
  name: hostingPlanName
  location: location
  tags: tags
  sku: {
    name: 'FC1'
    tier: 'FlexConsumption'
  }
  kind: 'functionapp'
  properties: {
    reserved: true // Linux
  }
}

// ---------------------------------------------------------------------
// Function App (shell) — no functions deployed; only configuration
// ---------------------------------------------------------------------
resource functionApp 'Microsoft.Web/sites@2024-04-01' = {
  name: functionAppName
  location: location
  tags: tags
  kind: 'functionapp,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uamiId}': {}
    }
  }
  properties: {
    serverFarmId: hostingPlan.id
    httpsOnly: true
    publicNetworkAccess: 'Enabled' // Phase 1 shell — VNet integration deferred until D-P8 ships
    functionAppConfig: {
      deployment: {
        storage: {
          type: 'blobContainer'
          value: '${storage.properties.primaryEndpoints.blob}${deploymentContainer.name}'
          authentication: {
            type: 'StorageAccountConnectionString'
            storageAccountConnectionStringName: 'AzureWebJobsStorage'
          }
        }
      }
      scaleAndConcurrency: {
        alwaysReady: alwaysReadyInstanceCount > 0 ? [
          {
            name: 'function:default'
            instanceCount: alwaysReadyInstanceCount
          }
        ] : []
        instanceMemoryMB: instanceMemoryMB
        maximumInstanceCount: maximumInstanceCount
      }
      runtime: {
        name: 'dotnet-isolated'
        version: '8.0'
      }
    }
    siteConfig: {
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [
        {
          name: 'AzureWebJobsStorage'
          value: 'DefaultEndpointsProtocol=https;AccountName=${storage.name};EndpointSuffix=${environment().suffixes.storage};AccountKey=${storage.listKeys().keys[0].value}'
        }
        {
          name: 'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnectionString
        }
        {
          name: 'AZURE_CLIENT_ID'
          value: uamiClientId
        }
        // NOTE: FUNCTIONS_WORKER_RUNTIME is NOT allowed on Flex Consumption —
        // the runtime is set via functionAppConfig.runtime.{name,version} above.
        // The platform rejects FUNCTIONS_WORKER_RUNTIME in siteConfig.appSettings.
      ]
    }
  }
  dependsOn: [
    uamiStorageBlobDataOwner
  ]
}

output functionAppName string = functionApp.name
output functionAppId string = functionApp.id
output functionAppDefaultHostName string = functionApp.properties.defaultHostName
output storageAccountName string = storage.name
output storageAccountId string = storage.id
output hostingPlanId string = hostingPlan.id
