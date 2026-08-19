// infrastructure/bicep/modules/controlplane-worker-app-service.bicep
//
// L2 CONTROL-PLANE .WORKER App Service (slotless) + Exchange sidecar
// sitecontainer, with UAMI-only identity binding.
//
// PURPOSE
//   Emits the Sprk.Provisioning.ControlPlane.Worker .NET 10 App Service --
//   the background-processing host (C1.1 session-serialized dispatcher +
//   state-reconciler + crash-recovery + the 20-handler fleet, task 100/102)
//   -- on the SAME App Service Plan as .Api ($0 marginal Azure cost per
//   DS-3 Option 2). Also emits the DS-1b Exchange ApplicationAccessPolicy
//   sidecar as a Microsoft.Web/sites/sitecontainers child resource, moving
//   the Exchange-admin-capable container off the internet-facing .Api site.
//
// SPEC / DESIGN REFERENCES (customer-provisioning-orchestration-r1)
//   - DS-3 Section 3 Option 2 (owner-locked): .Worker is a NEW slotless App
//     Service on the SAME P1v3 plan; deploy = stop -> zip-deploy -> start
//     (the honest drain story -- crash-recovery/I6 + SB redelivery + L3
//     dedup + Section 4C already make this safe under EVERY topology).
//   - DS-3 Section 5: the DS-1b Exchange sidecar attaches to the WORKER
//     site, not .Api -- removes the Exchange-admin-capable container from
//     the public-facing surface.
//   - design.md Section 4.2a: main site(s) are stock DOTNETCORE|10.0
//     code-based deploys -- zero custom container image on the main site;
//     the EXO sidecar (H14a only) is the one designed exception.
//   - DS-1b Section 3: sitecontainer message contract -- localhost:8091,
//     POST /apply-policy with X-Sidecar-Auth per-boot shared secret from
//     platform KV; sidecar fetches the Exchange cert from KV at call time
//     via the SAME UAMI (App Service MSI endpoint reachable from
//     sitecontainers -- shared network namespace).
//   - ADR-028: UAMI-only identity; DefaultAzureCredential; NEVER
//     SystemAssigned.
//
// WHY A NEW MODULE (vs extending modules/controlplane-app-service.bicep)
//   controlplane-app-service.bicep hosts .Api ONLY -- it declares a staging
//   slot (the Worker deliberately has none; DS-3 Section 3) and JWT-bearer
//   audience app settings (the Worker has NO auth surface -- only
//   /healthz + /ping; task 100 Program.cs). Parameterizing those away inside
//   the existing module would re-open the shadow-worker defect task 100 was
//   created to close (an always-on staging slot silently draining the fleet
//   queue against production Cosmos/Service Bus with old/new code -- see
//   notes/design-study-ds3-api-worker-split.md Section 1.3). A dedicated
//   module keeps the .Api and .Worker hosting shapes independently legible.
//
// OUT OF SCOPE FOR THIS MODULE
//   - RBAC role assignments (Cosmos, Service Bus Receiver/Sender, AI
//     Search, ARM Reader, Graph app-roles): task 110 (SB RBAC) +
//     Grant-ControlPlaneIdentity.ps1 (task 111, landed) grant the FULL
//     scope onto the shared control-plane UAMI this module binds to.
//   - Path X Dataverse App User registration: task 111's script / H10
//     handler, not a Bicep concern.
//   - keyVaultReferenceIdentity PATCH: applied post-deploy by the H4
//     handler on the Worker site (parity with .Api's T1/T5 handling).
//   - The real ACR image for the Exchange sidecar: task 114 built the
//     Dockerfile/image; task 115 wires the CI build+push to the platform
//     ACR. Until task 115 lands, `acrImageTag` defaults to a documented
//     public placeholder (see param description + the POML's own
//     escalation-trigger guidance) so this module's shape can be authored
//     and validated independently of the CI pipeline landing first.

@description('Name of the L2 control-plane Worker App Service (typically spaarke-provisioning-controlplane-worker-{env}).')
param appServiceName string

@description('App Service Plan resource ID -- MUST be the SAME plan resource id .Api uses (DS-3 Section 3: $0 marginal cost, one plan hosts multiple apps). This module does NOT declare a new server-farm (App Service Plan) resource -- it only references an existing plan id via this parameter.')
param appServicePlanId string

@description('Location for the App Service.')
param location string = resourceGroup().location

@description('Resource ID of the fleet-scoped control-plane UAMI (from modules/uami.bicep). v1 ships on the SAME shared UAMI as .Api with the FULL grant set (Cosmos + SB Sender+Receiver + KV + AI Search + ARM + Graph + Path X Dataverse App User via task 111 script) -- DS-3 Section 3 notes the two-UAMI least-privilege split as the target shape, cheap to introduce later; not required for v1.')
param userAssignedIdentityResourceId string

@description('AZURE_CLIENT_ID pins DefaultAzureCredential to the bound UAMI (per ADR-028). Pass the UAMI clientId from uami.bicep outputs -- SAME value passed to the .Api module.')
param uamiClientId string

@description('Cosmos DB account endpoint (from cosmos-provisioning.bicep outputs). Same Cosmos database as .Api -- the Worker reconciler + crash-recovery + all 20 handlers read/write ProvisioningRun docs here.')
param cosmosAccountEndpoint string

@description('Cosmos database name (spaarke-provisioning per task 024 spec).')
param cosmosDatabaseName string

@description('Cosmos runs container name (runs per task 024 spec).')
param cosmosRunsContainerName string

@description('Key Vault name (for @Microsoft.KeyVault references in appSettings + sitecontainer environmentVariables).')
param keyVaultName string

@description('Key Vault URI (https://{name}.vault.azure.net/) -- passed to the sitecontainer as PLATFORM_KV_URI so the sidecar can fetch the Exchange cert via App Service MSI at call time (DS-1b Section 3).')
param keyVaultUri string

@description('Name of the KV secret holding the Service Bus connection string (ADR-036 reuse -- no new SB created here).')
param serviceBusKeyVaultSecretName string

@description('Name of the KV secret holding the Dataverse S2S ClientSecret. Handlers H5/H6/H7/H10 (registry + solution-import + env-var writes) run in the WORKER post-split (task 100) -- this setting moved here from .Api, which no longer needs it (the .Api host has no handler DI; task 100 Program.cs header).')
param dataverseClientSecretName string

@description('App Insights connection string (from monitoring.bicep outputs). Same App Insights workspace as .Api -- distinct cloud_RoleName distinguishes the two hosts (DS-3 Section 3 observability note).')
param appInsightsConnectionString string

@description('Container image reference for the DS-1b Exchange ApplicationAccessPolicy sidecar (task 114 built the Dockerfile; task 115 wires CI build+push to the platform ACR). Defaults to a public placeholder per this task POML escalation-trigger guidance -- REPLACE with the real ACR tag (e.g. {acrLoginServer}/sprk-provisioning-sidecar:{tag}) once task 115 lands; do not leave the placeholder in a live deploy.')
param acrImageTag string = 'mcr.microsoft.com/appsvc/staticsite:latest'

@description('ACR authentication mode for the sitecontainer pull. Anonymous is correct ONLY for the public MCR placeholder default above. Switch to UserAssigned (with userManagedIdentityClientId = uamiClientId) once acrImageTag points at the platform ACR (task 115) -- the UAMI needs AcrPull RBAC on that registry, granted alongside task 110 and task 111 other RBAC grants.')
param sidecarAuthType string = 'Anonymous'

@description('Name of the KV secret holding the per-boot shared secret the Worker site injects into the sidecar as SIDECAR_SHARED_SECRET (DS-1b Section 3 main-to-sidecar auth leg).')
param sidecarSharedSecretKvSecretName string = 'Sidecar-Shared-Secret'

@description('Name of the KV secret holding the Exchange Online connect certificate (PFX) the sidecar fetches at call time via the App Service MSI endpoint. This is the SECRET NAME passed as EXCHANGE_CERT_SECRET_NAME -- not the certificate value itself (DS-1b Section 3 sidecar-to-Exchange auth leg).')
param exchangeCertKvSecretName string = 'Exchange-Connect-Cert'

@description('Client (application) ID of the Exchange Online connect app registration the sidecar authenticates as (app-only Connect-ExchangeOnline). Not a secret -- passed as a plain sitecontainer environment variable. Empty default is valid at author time; the H3 Entra app-reg handler output supplies the real value at customer/platform onboarding.')
param exchangeConnectAppId string = ''

@description('Tags for the resource.')
param tags object = {}

// ============================================================================
// APP SERVICE (WORKER -- slotless per DS-3 Section 3; UAMI-only per ADR-028)
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
        // NOTE: no AzureAd__* settings here -- the Worker has NO auth
        // surface (task 100 Program.cs: only anonymous /healthz + /ping).
        // ---------------------------------------------------------------

        // ---------------------------------------------------------------
        // Cosmos (task 024 wiring -- endpoint only; MI resolves credentials)
        // ---------------------------------------------------------------
        { name: 'Cosmos__Endpoint', value: cosmosAccountEndpoint }
        { name: 'Cosmos__Database', value: cosmosDatabaseName }
        { name: 'Cosmos__RunsContainer', value: cosmosRunsContainerName }

        // ---------------------------------------------------------------
        // Service Bus (ADR-036 reuse -- KV reference; NS not created here).
        // The Worker's dispatcher (task 102) drains this queue with
        // Receive rights; .Api only Sends. Both resolve the SAME connection
        // string setting -- least-privilege is enforced via RBAC scope on
        // the shared UAMI (task 110), not via distinct connection strings.
        // ---------------------------------------------------------------
        {
          name: 'ServiceBus__ConnectionString'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${serviceBusKeyVaultSecretName})'
        }

        // ---------------------------------------------------------------
        // Dataverse S2S (H5/H6/H7/H10 registry + env-var writes -- these
        // handlers live in the WORKER post-split; see param description).
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
// EXCHANGE APPLICATIONACCESSPOLICY SIDECAR (DS-1b Section 3 / design.md
// Section 4.2a) -- Microsoft.Web/sites/sitecontainers child resource.
// Shares the Worker site's network namespace (localhost-only, not publicly
// routed) and the Worker's UAMI (sitecontainers can reach the App Service
// MSI endpoint -- IDENTITY_ENDPOINT / IDENTITY_HEADER are injected
// automatically by the platform for any identity-bound site; NOT set here).
// ============================================================================

resource exchangePolicySidecar 'Microsoft.Web/sites/sitecontainers@2024-04-01' = {
  parent: appService
  name: 'exchange-policy-sidecar'
  properties: {
    image: acrImageTag
    targetPort: '8091'
    isMain: false
    authType: sidecarAuthType
    environmentVariables: [
      // PLATFORM_KV_URI + EXCHANGE_CERT_SECRET_NAME + EXCHANGE_CONNECT_APP_ID
      // are plain (non-secret) values per Listener.ps1's documented
      // .ENVIRONMENT contract (task 114).
      { name: 'PLATFORM_KV_URI', value: keyVaultUri }
      { name: 'EXCHANGE_CERT_SECRET_NAME', value: exchangeCertKvSecretName }
      { name: 'EXCHANGE_CONNECT_APP_ID', value: exchangeConnectAppId }

      // SIDECAR_SHARED_SECRET is the main-to-sidecar auth leg (DS-1b
      // Section 3) -- KV-reference syntax, same convention as every other
      // secret-bearing appSetting in this module. Requires the Worker's
      // keyVaultReferenceIdentity PATCH (H4, post-deploy) to resolve at
      // runtime -- same T1 pattern as the main site's Cosmos/SB/Dataverse
      // settings above.
      {
        name: 'SIDECAR_SHARED_SECRET'
        value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${sidecarSharedSecretKvSecretName})'
      }
    ]
  }
}

// ============================================================================
// OUTPUTS
// ============================================================================

output appServiceId string = appService.id
output appServiceName string = appService.name
output appServiceDefaultHostName string = appService.properties.defaultHostName
output appServiceUrl string = 'https://${appService.properties.defaultHostName}'
output sidecarName string = exchangePolicySidecar.name
