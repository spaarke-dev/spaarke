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
//     Search, ARM Reader, Graph app-roles): task 110 (SB RBAC,
//     modules/controlplane-sb-rbac.bicep) + Grant-ControlPlaneIdentity.ps1
//     (task 111, landed) grant the FULL scope onto the shared control-plane
//     UAMI this module binds to.
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
//
// DS-5 C5.1 FOLLOW-ON FIX (task 110, applied here)
//   DS-5's C5.1 finding scoped ONLY modules/controlplane-app-service.bicep
//   (task 109 fixed it there) because this .Worker module did not exist yet
//   when DS-5 was authored — task 101 added it afterward carrying the
//   IDENTICAL key-shape bugs: `Cosmos__Endpoint`/`__Database`/
//   `__RunsContainer` (code reads `Cosmos:AccountEndpoint`/`:DatabaseName`/
//   `:ContainerName` per Sprk.Provisioning.ControlPlane.Core's
//   CosmosModule.cs:98,109-110 — SHARED by both .Api and .Worker) and a
//   KV-referenced `ServiceBus__ConnectionString` app setting that
//   ServiceBusModule.cs:53 documents is IGNORED (the code always resolves
//   `ServiceBus:FullyQualifiedNamespace` via the bound UAMI's token
//   credential per ADR-028 MI-outbound). Fixed below mirroring task 109's
//   exact fix shape: renamed Cosmos__* keys, ServiceBus__ConnectionString
//   replaced with ServiceBus__FullyQualifiedNamespace + ServiceBus__QueueName,
//   and ManagedIdentity__ClientId added (CosmosModule.cs:125,
//   ServiceBusModule.cs:157 read this app-owned config key to pin
//   DefaultAzureCredential to the bound UAMI — AZURE_CLIENT_ID alone is the
//   Azure-native convention but belt-and-braces per task 109 precedent).

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

@description('Name of the fleet-scoped Service Bus namespace (task 108 / DS-5 C5.4) used to construct the fully-qualified-namespace app-setting the code reads (DS-5 C5.1 key-rename fix, applied here by task 110 -- MI-only send/receive, no connection string per ServiceBusModule.cs:53). SAME value passed to the .Api module.')
param serviceBusNamespaceName string

@description('Name of the fleet-scoped Service Bus queue this App Service enqueues onto / receives from (DS-5 C5.1). Defaults to the canonical queue declared by task 108.')
param serviceBusQueueName string = 'sprk-provisioning-jobs'

@description('Name of the KV secret holding the Dataverse S2S ClientSecret. Handlers H5/H6/H7/H10 (registry + solution-import + env-var writes) run in the WORKER post-split (task 100) -- this setting moved here from .Api, which no longer needs it (the .Api host has no handler DI; task 100 Program.cs header).')
param dataverseClientSecretName string

@description('Admin Dataverse environment URL (e.g. https://spaarkedev1.crm.dynamics.com) hosting the sprk_dataverseenvironment registry table. Consumed by DataverseEnvironmentRegistryOptions.AdminEnvironmentUrl (Sprk.Provisioning.ControlPlane.Core/Registry/DataverseEnvironmentRegistryClient.cs, task 112 -- Path X MI-native, DefaultAzureCredential pinned to this module\'s UAMI via ManagedIdentity__ClientId below; NO client secret). REQUIRED as of task 122 (Wave G-2): the Worker\'s composition root now registers the REAL client unconditionally (NullDataverseEnvironmentRegistryClient placeholder removed from DI) and DataverseEnvironmentRegistryOptions.Validate() fails fast at boot if this is unset (NFR-05) -- no kill-switch/Enabled flag exists for this seam by design (DS-8 mandates Path X real from day one).')
param adminDataverseEnvironmentUrl string

@description('Name of the platform Key Vault secret holding the shared BFF app-registration client secret (canonical name "BFF-API-ClientSecret" per scripts/canonical-secret-catalog/manifest.yaml -- BINDING never-delete). Consumed by EnvVarValuesOptions.ClientSecret (Sprk.Provisioning.ControlPlane.Core/Handlers/EnvVarValues/EnvVarValuesOptions.cs, task 142 -- H7 authenticates to each customer\'s target Dataverse environment via confidential-client credentials against this SAME shared multitenant BFF app-reg, the identity spec.md §9.1 v3 mandates for Model 1; H6 uses the identical pattern for solution import). REQUIRED as of task 142 (Wave G-4): EnvVarValuesOptions.Validate() fails fast at boot if the resolved value is unset (NFR-05) -- no kill-switch/Enabled flag exists for this seam by design (parity with adminDataverseEnvironmentUrl above).')
param bffApiClientSecretName string = 'BFF-API-ClientSecret'

@description('Name of the platform Key Vault secret holding the shared-platform Azure OpenAI resource endpoint (canonical name "AzureOpenAI-Endpoint" per scripts/canonical-secret-catalog/manifest.yaml -- the SAME secret the .Api site already resolves as AzureOpenAI__Endpoint / DocumentIntelligence__OpenAiEndpoint; single source of truth, not a second copy). Consumed by RuntimeReferencesOptions.SharedPlatformOpenAiEndpoint (Sprk.Provisioning.ControlPlane.Core/Handlers/RuntimeReferences/RuntimeReferencesOptions.cs, task 153 -- H12c writes this endpoint into every Model1Shared customer\'s sprk_aimodeldeployment rows; Model2Dedicated customers instead read InterStepState.OpenAiEndpoint from H2a\'s Bicep output and never consult this setting). Unlike adminDataverseEnvironmentUrl / bffApiClientSecretName, this field is CONDITIONALLY required (Model1Shared branch only) -- RuntimeReferencesOptions.Validate() deliberately does NOT fail-fast at boot on this being unset (task 153); the existing per-run runtime guard (H12cRuntimeReferencesHandler.cs) classifies a missing value as a Resumable failure on the affected run only, not a Worker-wide boot crash.')
param azureOpenAiEndpointSecretName string = 'AzureOpenAI-Endpoint'

@description('Name of the platform Key Vault secret holding the per-environment Redis connection string (canonical name "Redis-ConnectionString" per scripts/canonical-secret-catalog/manifest.yaml). Consumed by DispatchModule.cs:154-199 (Level-2 dispatch-idempotency IDistributedCache backing store, task 105 / DS-2 §4-L2): the code reads ConnectionStrings:Redis first, then Redis:ConnectionString, and THROWS at composition time (NFR-05 fail-fast) when neither is set and ASPNETCORE_ENVIRONMENT is not Development/Testing -- App Service defaults to Production, so omitting this app setting is a guaranteed Worker crash-loop (G-8 audit defect #6). The referenced Redis is the REAL per-environment instance (spaarke-bff-redis-{env}, provisioned by scripts/Deploy-RedisCache.ps1 via modules/redis.bicep -- platform-controlplane.bicep deliberately does not declare its own Redis); the secret must be seeded into THIS module\'s platform KV (sprk-controlplane-{env}-kv) by Seed-PlatformKeyVault.ps1 (G-8 Batch 4, defect #9) -- same seeding contract as bffApiClientSecretName / azureOpenAiEndpointSecretName above. We deliberately do NOT set ASPNETCORE_ENVIRONMENT=Development to bypass the gate: the fail-fast exists to prevent silent same-instance-only duplicate suppression in deployed multi-instance environments.')
param redisConnectionStringSecretName string = 'Redis-ConnectionString'

@description('HTTPS URI of the provisioning-artifacts blob CONTAINER (e.g. https://{account}.blob.core.windows.net/provisioning-artifacts) -- output of modules/controlplane-artifacts-storage.bicep (G-8 Batch 2, audit defect #5). Threaded into the three handler option sections that each REQUIRE it at boot per NFR-05 (G-8 audit defect #7): BicepInfraDeployOptions (H2a), BffDeployOptions (H9), SolutionImportOptions (H6) -- Program.cs binds each via GetSection(nameof(...Options)), so the app-setting keys below carry the literal "...Options" section names. All three Validate() throw on empty, so this param is REQUIRED (no default) -- platform-controlplane.bicep MUST pass the artifacts-storage module\'s container URI output here (wiring owned by G-8 Batch 2). The Worker\'s UAMI reads blobs via DefaultAzureCredential (Storage Blob Data Reader grant -- audit defect #3); no account key or SAS in config.')
param artifactsStorageContainerUri string

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
        // Cosmos (task 024 wiring -- endpoint only; MI resolves credentials).
        // Keys renamed per DS-5 C5.1 (task 110 follow-on fix -- see file
        // header) to match CosmosModule.cs:98-110 (Cosmos:AccountEndpoint /
        // :DatabaseName / :ContainerName) -- SHARED by .Api and .Worker via
        // Sprk.Provisioning.ControlPlane.Core. The OLD keys
        // (Cosmos__Endpoint/__Database/__RunsContainer) were never read by
        // the code.
        // ---------------------------------------------------------------
        { name: 'Cosmos__AccountEndpoint', value: cosmosAccountEndpoint }
        { name: 'Cosmos__DatabaseName', value: cosmosDatabaseName }
        { name: 'Cosmos__ContainerName', value: cosmosRunsContainerName }

        // ---------------------------------------------------------------
        // Service Bus (DS-5 C5.1 fix, task 110 follow-on): MI-only FQNS +
        // queue name, NOT a connection string. ServiceBusModule.cs:53
        // documents that any connection-string setting is IGNORED -- the
        // code always resolves ServiceBus:FullyQualifiedNamespace + uses
        // the bound UAMI's token credential (ADR-028). The Worker's
        // dispatcher (task 102) drains this queue with Receive rights;
        // .Api only Sends. Least-privilege is enforced via RBAC role
        // (Sender vs Receiver) on the shared UAMI (task 110 RBAC module),
        // not via distinct connection strings -- both hosts resolve the
        // SAME FQNS/queue app settings.
        // ---------------------------------------------------------------
        { name: 'ServiceBus__FullyQualifiedNamespace', value: '${serviceBusNamespaceName}.servicebus.windows.net' }
        { name: 'ServiceBus__QueueName', value: serviceBusQueueName }

        // ---------------------------------------------------------------
        // Dataverse S2S (H5/H6/H7/H10 registry + env-var writes -- these
        // handlers live in the WORKER post-split; see param description).
        // ---------------------------------------------------------------
        {
          name: 'Dataverse__ClientSecret'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${dataverseClientSecretName})'
        }

        // ---------------------------------------------------------------
        // Task 122 (Wave G-2): DataverseEnvironmentRegistry -- Path X
        // MI-native admin-env registry client (task 112). REQUIRED --
        // DataverseEnvironmentRegistryOptions.Validate() fails fast at boot
        // (NFR-05) if this is missing; no ClientSecret needed (auth is via
        // ManagedIdentity__ClientId below, pinning DefaultAzureCredential to
        // this site's UAMI, which task 111's Grant-ControlPlaneIdentity.ps1
        // registers as a Dataverse Application User on this same admin env).
        // ---------------------------------------------------------------
        { name: 'DataverseEnvironmentRegistry__AdminEnvironmentUrl', value: adminDataverseEnvironmentUrl }

        // ---------------------------------------------------------------
        // Task 142 (Wave G-4): EnvVarValues -- H7's Dataverse Web API
        // writer collaborator authenticates to each customer's Dataverse
        // env using the SAME shared multitenant BFF app-reg credential H6
        // uses for solution import (the MI-Dataverse App User from H10 does
        // not exist yet at H7's point in the DAG). REQUIRED --
        // EnvVarValuesOptions.Validate() fails fast at boot (NFR-05) if
        // this is missing; sourced from the platform KV's canonical
        // never-delete BFF-API-ClientSecret secret (task 126 real-value
        // population; same secret the .Api site resolves as
        // AzureAd__ClientSecret / Graph__ClientSecret).
        // ---------------------------------------------------------------
        {
          name: 'EnvVarValues__ClientSecret'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${bffApiClientSecretName})'
        }

        // ---------------------------------------------------------------
        // Task 153 (Wave G-5): RuntimeReferences -- H12c's shared-platform
        // Azure OpenAI endpoint for Model1Shared customers. Sourced from the
        // SAME canonical "AzureOpenAI-Endpoint" KV secret the .Api site
        // already resolves (AzureOpenAI__Endpoint / DocumentIntelligence__
        // OpenAiEndpoint) -- single source of truth for this environment's
        // shared platform OpenAI resource, not a duplicate. CONDITIONALLY
        // required (Model1Shared branch only) -- RuntimeReferencesOptions.
        // Validate() does NOT fail-fast at boot on this being unset (unlike
        // EnvVarValues__ClientSecret above); a missing value fails the
        // affected Model1Shared run Resumable, not the whole Worker boot.
        // ---------------------------------------------------------------
        {
          name: 'RuntimeReferences__SharedPlatformOpenAiEndpoint'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${azureOpenAiEndpointSecretName})'
        }

        // ---------------------------------------------------------------
        // G-8 Batch 3 (audit defect #6): Level-2 dispatch-idempotency Redis
        // (DispatchModule.cs:154-199, task 105 / DS-2 §4-L2). The code reads
        // GetConnectionString("Redis") FIRST, then Redis:ConnectionString --
        // ConnectionStrings__Redis is used here for exact parity with the
        // BFF cutover shape (Deploy-RedisCache.ps1 -CutoverBffSettings).
        // Without this setting the Worker THROWS at composition time under
        // the App Service default ASPNETCORE_ENVIRONMENT=Production
        // (deliberate NFR-05 fail-fast; we provide a REAL connection string
        // rather than bypass the gate with an environment override -- see
        // the redisConnectionStringSecretName param description).
        // ---------------------------------------------------------------
        {
          name: 'ConnectionStrings__Redis'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${redisConnectionStringSecretName})'
        }

        // ---------------------------------------------------------------
        // G-8 Batch 3 (audit defect #7): provisioning-artifacts container
        // URI for the three artifact-consuming handler option sections.
        // Program.cs binds each via GetSection(nameof(...Options)) -- the
        // section names are the LITERAL class names incl. the "Options"
        // suffix (BicepInfraDeployOptions / BffDeployOptions /
        // SolutionImportOptions), matching the fixture keys in
        // HandlerRegistrationCompletenessTests.cs:195,204,220. All three
        // Validate() throw on empty at boot (NFR-05) -- H2a (Bicep infra
        // deploy), H9 (BFF zip-deploy), H6 (solution import) each download
        // artifacts from this container via the bound UAMI (Storage Blob
        // Data Reader; no key/SAS). Same URI for all three by design --
        // one artifacts container per environment (audit defect #5 module,
        // modules/controlplane-artifacts-storage.bicep, G-8 Batch 2).
        // ---------------------------------------------------------------
        { name: 'BicepInfraDeployOptions__ProvisioningArtifactsContainerUri', value: artifactsStorageContainerUri }
        { name: 'BffDeployOptions__ProvisioningArtifactsContainerUri', value: artifactsStorageContainerUri }
        { name: 'SolutionImportOptions__ProvisioningArtifactsContainerUri', value: artifactsStorageContainerUri }

        // ---------------------------------------------------------------
        // Managed-identity discovery (pin DefaultAzureCredential to bound
        // UAMI). AZURE_CLIENT_ID is the Azure-native env var
        // DefaultAzureCredential honors natively; ManagedIdentity__ClientId
        // is ADDED per DS-5 C5.1 (task 110 follow-on, mirroring task 109's
        // .Api fix) because CosmosModule.cs:125 and ServiceBusModule.cs:157
        // read the app's own ManagedIdentity:ClientId config key (not the
        // Azure env-var convention) to pin their per-module TokenCredential
        // to the bound UAMI. Both kept (belt-and-braces; harmless
        // duplication).
        // ---------------------------------------------------------------
        { name: 'AZURE_CLIENT_ID', value: uamiClientId }
        { name: 'ManagedIdentity__ClientId', value: uamiClientId }

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
