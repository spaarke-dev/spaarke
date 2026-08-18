// infrastructure/bicep/modules/controlplane-app-service.bicep
//
// L2 CONTROL-PLANE App Service + staging slot with UAMI-only identity binding.
//
// PURPOSE
//   Emits the Sprk.Provisioning.ControlPlane .NET 10 App Service and its
//   staging slot with the User-Assigned Managed Identity from birth (T1/T5
//   structural fix). Only invoked from platform-controlplane.bicep - not a
//   general-purpose App Service module (the existing modules/app-service.bicep
//   is BFF-oriented + still uses SystemAssigned; task 029 refactor deferred).
//
// SPEC REFERENCES (customer-provisioning-orchestration-r1)
//   - spec.md FR-20:  audience api://spaarke-provisioning-controlplane-{env};
//                     .NET 10 App Service; JWT bearer.
//   - design.md §4.2 B2:  App Service parity with BFF.
//   - design.md T1/T5:  UAMI binds BOTH slots; keyVaultReferenceIdentity PATCH
//                       is applied by H4 handler post-deploy (NOT this module).
//   - ADR-028:  UAMI-only identity; DefaultAzureCredential; NEVER SystemAssigned.
//
// WHY A NEW MODULE (vs invoking modules/app-service.bicep)
//   modules/app-service.bicep at this write time emits SystemAssigned MI only
//   and has no UAMI parameter. Co-mixed SystemAssigned+UserAssigned identity is
//   an ADR-028 anti-pattern. When task 029 refactors app-service.bicep for UAMI,
//   this module could be retired in favor of two module invocations - the
//   topology would be equivalent.
//
// OUT OF SCOPE FOR THIS MODULE
//   - keyVaultReferenceIdentity PATCH:  H4 handler applies post-deploy on both
//                                       slots (spec.md MUST rule / design.md T1).
//   - Slot-sticky config:  Applied via post-deploy PATCH by ops scripts (env
//                          overrides that must not swap).

@description('Name of the L2 control-plane App Service (typically spaarke-provisioning-controlplane-{env} to match the FR-20 audience).')
param appServiceName string

@description('App Service Plan resource ID.')
param appServicePlanId string

@description('Location for the App Service + slot.')
param location string = resourceGroup().location

@description('Resource ID of the fleet-scoped control-plane UAMI (from modules/uami.bicep). Bound to BOTH prod + staging slots (T1/T5 structural fix).')
param userAssignedIdentityResourceId string

@description('AAD bearer audience for the L2 REST API - MUST equal api://spaarke-provisioning-controlplane-{env} per FR-20 acceptance.')
param jwtAudience string

@description('AAD tenant ID for JWT bearer authority validation. Single-issuer per spec.md §4.2.')
param jwtTenantId string

@description('Client ID of the L2 control-plane app-reg (used for informational logging + config surface).')
param controlPlaneAppRegClientId string = ''

@description('AZURE_CLIENT_ID pins DefaultAzureCredential to the bound UAMI (per ADR-028). Pass the UAMI clientId from uami.bicep outputs.')
param uamiClientId string

@description('Cosmos DB account endpoint (from cosmos-provisioning.bicep outputs).')
param cosmosAccountEndpoint string

@description('Cosmos database name (spaarke-provisioning per task 024 spec).')
param cosmosDatabaseName string

@description('Cosmos runs container name (runs per task 024 spec).')
param cosmosRunsContainerName string

@description('Key Vault name (for @Microsoft.KeyVault references in appSettings).')
param keyVaultName string

@description('Name of the KV secret holding the Service Bus connection string (ADR-036 reuse - no new SB created here).')
param serviceBusKeyVaultSecretName string

@description('Name of the KV secret holding the Dataverse S2S ClientSecret used by H5/H6/H10.')
param dataverseClientSecretName string

@description('App Insights connection string (from monitoring.bicep outputs).')
param appInsightsConnectionString string

@description('AAD authority login endpoint (typically environment().authentication.loginEndpoint to remain cloud-portable).')
param aadLoginEndpoint string = environment().authentication.loginEndpoint

@description('Tags for the resource.')
param tags object = {}

// ============================================================================
// APP SERVICE (UAMI-only from birth per ADR-028 + T1/T5)
// ============================================================================

resource appService 'Microsoft.Web/sites@2023-01-01' = {
  name: appServiceName
  location: location
  tags: tags
  kind: 'app,linux'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
      appSettings: [
        // ---------------------------------------------------------------
        // Bearer / audience (FR-20 acceptance)
        // ---------------------------------------------------------------
        { name: 'AzureAd__Audience', value: jwtAudience }
        { name: 'AzureAd__TenantId', value: jwtTenantId }
        { name: 'AzureAd__ClientId', value: controlPlaneAppRegClientId }
        { name: 'AzureAd__Instance', value: aadLoginEndpoint }

        // ---------------------------------------------------------------
        // Cosmos (task 024 wiring - endpoint only; MI resolves credentials)
        // ---------------------------------------------------------------
        { name: 'Cosmos__Endpoint', value: cosmosAccountEndpoint }
        { name: 'Cosmos__Database', value: cosmosDatabaseName }
        { name: 'Cosmos__RunsContainer', value: cosmosRunsContainerName }

        // ---------------------------------------------------------------
        // Service Bus (ADR-036 reuse - KV reference; NS not created here)
        // ---------------------------------------------------------------
        {
          name: 'ServiceBus__ConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${serviceBusKeyVaultSecretName})'
        }

        // ---------------------------------------------------------------
        // Dataverse S2S (H5/H6/H10 registry writes)
        // ---------------------------------------------------------------
        {
          name: 'Dataverse__ClientSecret'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${dataverseClientSecretName})'
        }

        // ---------------------------------------------------------------
        // Managed-identity discovery (pin DefaultAzureCredential to bound UAMI)
        // ---------------------------------------------------------------
        { name: 'AZURE_CLIENT_ID', value: uamiClientId }

        // ---------------------------------------------------------------
        // App Insights (connection string is not a secret per Azure guidance)
        // ---------------------------------------------------------------
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
      ]
    }
  }
}

// ============================================================================
// STAGING SLOT (SAME UAMI as prod slot => structural T5/T1 fix - identity does
//              NOT drift on slot swap so KV RBAC + Dataverse App User + Graph
//              app-roles remain valid across swaps).
// ============================================================================

resource stagingSlot 'Microsoft.Web/sites/slots@2023-01-01' = {
  parent: appService
  name: 'staging'
  location: location
  tags: tags
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${userAssignedIdentityResourceId}': {}
    }
  }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    clientAffinityEnabled: false
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: true
      http20Enabled: true
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      healthCheckPath: '/healthz'
      // App settings inherit from prod slot at swap time by default (no slot-
      // sticky settings declared here). Operational overrides applied via
      // post-deploy PATCH.
    }
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output appServiceId string = appService.id
output appServiceName string = appService.name
output appServiceDefaultHostName string = appService.properties.defaultHostName
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output stagingSlotName string = stagingSlot.name
output stagingSlotDefaultHostName string = stagingSlot.properties.defaultHostName
output stagingSlotUrl string = 'https://${stagingSlot.properties.defaultHostName}'
