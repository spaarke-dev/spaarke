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
//   DS-3 Option 2). Also emits the H14a Exchange sidecar (RBAC for
//   Applications since task 251) as a Microsoft.Web/sites/sitecontainers
//   child resource, moving the Exchange-admin-capable container off the
//   internet-facing .Api site.
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
//   - DS-1b Section 3 (task 251): sitecontainer message contract --
//     localhost:8091, POST /apply-mailbox-access + /read-mailbox-access with
//     X-Sidecar-Auth (per-boot shared secret, platform KV) and
//     X-Exchange-Access-Token (the Worker signs in as 'Spaarke Exchange Admin'
//     through its UAMI's federated credential; the sidecar holds NO
//     credential and reads no Key Vault -- owner D24).
//   - SITECONTAINER SETTINGS CONTRACT (task 251, G30): a sitecontainer
//     environmentVariables value is the NAME of an app setting on this site,
//     never a literal -- App Service resolves it at start and passes an empty
//     string when the setting does not exist. Literals here are why the first
//     sidecar (2026-10-03) started with every variable empty.
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
//   - The Exchange sidecar image: built by .github/workflows/build-provisioning-sidecar.yml
//     into the platform ACR; `acrImageTag` selects it (dev bicepparam pins the ACR tag).
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

@description('Name of the fleet-scoped Service Bus namespace (task 108 / DS-5 C5.4) used to construct the fully-qualified-namespace app-setting the code reads (DS-5 C5.1 key-rename fix, applied here by task 110 -- MI-only send/receive, no connection string per ServiceBusModule.cs:53). SAME value passed to the .Api module.')
param serviceBusNamespaceName string

@description('Name of the fleet-scoped Service Bus queue this App Service enqueues onto / receives from (DS-5 C5.1). Defaults to the canonical queue declared by task 108.')
param serviceBusQueueName string = 'sprk-provisioning-jobs'

@description('Admin Dataverse environment URL (e.g. https://spaarkedev1.crm.dynamics.com) hosting the sprk_dataverseenvironment registry table. Consumed by DataverseEnvironmentRegistryOptions.AdminEnvironmentUrl (Sprk.Provisioning.ControlPlane.Core/Registry/DataverseEnvironmentRegistryClient.cs, task 112 -- Path X MI-native, DefaultAzureCredential pinned to this module\'s UAMI via ManagedIdentity__ClientId below; NO client secret). REQUIRED as of task 122 (Wave G-2): the Worker\'s composition root now registers the REAL client unconditionally (NullDataverseEnvironmentRegistryClient placeholder removed from DI) and DataverseEnvironmentRegistryOptions.Validate() fails fast at boot if this is unset (NFR-05) -- no kill-switch/Enabled flag exists for this seam by design (DS-8 mandates Path X real from day one).')
param adminDataverseEnvironmentUrl string

@description('LEGACY credential chain only (requireSecretFreeIdentity=false). Name of the platform Key Vault secret the EnvVarValues__ClientSecret and SolutionImportOptions__ClientSecret Key Vault references resolve. NOT referenced when requireSecretFreeIdentity=true (the default since task 252): H6, H7 and H7b then sign in through the FR-39 chain (ManagedIdentityFederated only) and no app setting names this secret. H6, H7 and H7b sign in as the customer BFF app registration H3 creates for that customer (D-13: one BFF app registration per customer, both models), so one shared platform secret cannot authenticate as it; the legacy chain survives only for the ADR-028 A4 prong-3 exception (unmigrated environments, sunset 2026-11-23). Never create, seed or restore this secret in a secret-free environment (provisioning.md KV credential lifecycle rule 1); Seed-PlatformKeyVault.ps1 does not seed it.')
param bffApiClientSecretName string = 'BFF-API-ClientSecret'


@description('Endpoint (host:port) of the per-environment Azure Managed Redis (spaarke-bff-redis-{env}, modules/redis.bicep output `redisEndpoint`), emitted as Redis__Endpoint. Consumed by DispatchModule (Level-2 dispatch-idempotency IDistributedCache, task 105 / DS-2 §4-L2): with Redis__Endpoint set the Worker authenticates with its user-assigned identity (ManagedIdentity__ClientId) over RESP3 -- the cache has access keys disabled (task 242, owner D12/D13), so there is no connection string and no Key Vault secret. The Worker UAMI must hold an access-policy assignment on the cache (parameters/redis-{env}.bicepparam). Required: outside Development/Testing the Worker refuses to start without it (NFR-05 fail-fast; we do not set ASPNETCORE_ENVIRONMENT=Development to bypass it -- the gate prevents silent same-instance-only duplicate suppression in multi-instance environments). Not a secret.')
@minLength(1)
param redisEndpoint string

@description('HTTPS URI of the provisioning-artifacts blob CONTAINER (e.g. https://{account}.blob.core.windows.net/provisioning-artifacts) -- output of modules/controlplane-artifacts-storage.bicep (G-8 Batch 2, audit defect #5). Threaded into the three handler option sections that each REQUIRE it at boot per NFR-05 (G-8 audit defect #7): BicepInfraDeployOptions (H2a), BffDeployOptions (H9), SolutionImportOptions (H6) -- Program.cs binds each via GetSection(nameof(...Options)), so the app-setting keys below carry the literal "...Options" section names. All three Validate() throw on empty, so this param is REQUIRED (no default) -- platform-controlplane.bicep MUST pass the artifacts-storage module\'s container URI output here (wiring owned by G-8 Batch 2). The Worker\'s UAMI reads blobs via DefaultAzureCredential (Storage Blob Data Reader grant -- audit defect #3); no account key or SAS in config.')
param artifactsStorageContainerUri string

@description('App Insights connection string (from monitoring.bicep outputs). Same App Insights workspace as .Api -- distinct cloud_RoleName distinguishes the two hosts (DS-3 Section 3 observability note).')
param appInsightsConnectionString string

@description('Container image of the Exchange sidecar (H14a apply + H13 T4 read, RBAC for Applications -- task 251), e.g. {acrLoginServer}/provisioning-sidecar:{tag}, built by .github/workflows/build-provisioning-sidecar.yml or az acr build. The default is a public placeholder that serves none of the sidecar routes -- never deploy it.')
param acrImageTag string = 'mcr.microsoft.com/appsvc/staticsite:latest'

@description('ACR authentication mode for the sitecontainer pull. Anonymous is correct ONLY for the public MCR placeholder default above. Switch to UserAssigned (with userManagedIdentityClientId = uamiClientId) once acrImageTag points at the platform ACR (task 115) -- the UAMI needs AcrPull RBAC on that registry, granted alongside task 110 and task 111 other RBAC grants.')
param sidecarAuthType string = 'Anonymous'

@description('Name of the platform KV secret holding the per-boot shared secret between the Worker and the Exchange sidecar (DS-1b Section 3). The Worker reads it from Key Vault (IntegrationWiring__SidecarSharedSecret*); the sidecar receives it through the ExchangeSidecar__SharedSecret Key Vault reference app setting.')
param sidecarSharedSecretKvSecretName string = 'Sidecar-Shared-Secret'

@description('Client id of the \'Spaarke Exchange Admin\' app registration (task 251, owner D24). The Worker signs in as it through the federated identity credential that trusts this module\'s UAMI and hands the Exchange Online token to the sidecar per request -- no certificate, no secret. Emitted as IntegrationWiring__ExchangeAdminAppId. Empty: the Worker and sidecar start, and H14a / H13 T4 report "ExchangeAdminAppId is not configured" on first use.')
param exchangeAdminAppId string = ''

@description('Entra tenant ID of the ADMIN Dataverse environment for the CustomerRunGuard concurrency guard (Sprk.Provisioning.ControlPlane.Core/Concurrency/CustomerRunGuardOptions.cs). Emitted as the CustomerRunGuard__TenantId app-setting. Diagnostics only since the guard authenticates as the UAMI (not validated, not used for token issuance — CustomerRunGuardOptions; task 242b restored REG-02). Task 203b, punch list row A27 / r1-gap-analysis c5-6.')
param customerRunGuardTenantId string = ''


@description('Kill-switch for the CustomerRunGuard (Sprk.Provisioning.ControlPlane.Core/Concurrency/CustomerRunGuardOptions.cs Enabled). Emitted as the CustomerRunGuard__Enabled app-setting. Default false keeps the null-object return-Success path per ADR-032 -- flip to true once the bound UAMI is a Dataverse Application User on the admin env (customerRunGuardTenantId is diagnostics-only) (it authenticates as the UAMI since 2026-08-27; no client secret is involved). MUST equal the Api module\'s value (the Api acquires, the Worker releases). Production deployments MUST set true once I5 same-customer serialization becomes load-bearing (spec.md §4D I5 / FR-32; customer-provisioning-orchestration-r1 task 203b, punch list row A27).')
param customerRunGuardEnabled bool = false

@description('A44.5 (customer-provisioning-orchestration-r1 task 205i, 2026-08-25; restored by task 245b -- the 2026-09-28 master merge 92b480500 had taken master\'s pre-A44.5 copy of this module). When TRUE this Worker deploys on the SECRET-FREE identity contract (ADR-028 Amendment A4 / auth-v4 SS10.2): the BFF-API-ClientSecret KV-reference app settings (EnvVarValues__ClientSecret, SolutionImportOptions__ClientSecret) are OMITTED -- omission is the signal, NEVER a sentinel (auth-v4 SS9.1: an unresolvable KV-ref reaches the app as a literal string, which the credential path fails on opaquely with AADSTS7000215) -- and the FR-39 ordered-credential chain settings are emitted instead (EnvVarValues__Credentials__Order__0=ManagedIdentityFederated + __RequireSecretFreeIdentity=true, same pair for SolutionImportOptions). A secret-free control plane must carry ZERO references to the secret. Default TRUE since task 252 (2026-10-09): every control-plane environment is secret-free unless a prong-3 unmigrated environment explicitly passes false (SS6.5 resolution record), so a new environment never gets a Key Vault reference to a secret the binding rule forbids creating. (The CustomerRunGuard authenticates as the bound UAMI and carries no secret in either mode.)')
param requireSecretFreeIdentity bool = true

// ============================================================================
// A44.5 -- BFF-app-reg credential app settings (exactly ONE of these two sets
// is appended to the base appSettings below via concat + ternary):
//   - secret-free (requireSecretFreeIdentity=true, the DEFAULT since task
//     252): FR-39 ordered-credential chain settings consumed by
//     WorkerCredentialSelectionOptions
//     (Sprk.Provisioning.ControlPlane.Core/Handlers/Credentials/**, task
//     205i) -- MI-FIC via the SAME UAMI this module binds. No app setting
//     references BFF-API-ClientSecret.
//   - legacy (requireSecretFreeIdentity=false, prong-3 opt-in only): the two
//     KV-refs exactly as tasks 142 / 204a wired them. With no Credentials
//     section the Worker resolves the legacy [ClientSecret] chain and reads
//     them.
// Dataverse-ClientSecret is referenced by NO control-plane app setting in
// either mode (FR-38 deleted the last one, Wave G-8 Batch 2).
// ============================================================================
var legacyClientSecretAppSettings = [
  {
    name: 'EnvVarValues__ClientSecret'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${bffApiClientSecretName})'
  }
  {
    name: 'SolutionImportOptions__ClientSecret'
    value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${bffApiClientSecretName})'
  }
]

var secretFreeCredentialAppSettings = [
  { name: 'EnvVarValues__Credentials__Order__0', value: 'ManagedIdentityFederated' }
  { name: 'EnvVarValues__Credentials__RequireSecretFreeIdentity', value: 'true' }
  { name: 'SolutionImportOptions__Credentials__Order__0', value: 'ManagedIdentityFederated' }
  { name: 'SolutionImportOptions__Credentials__RequireSecretFreeIdentity', value: 'true' }
]

@description('Object id of the L2 control plane\'s own identity (the Worker UAMI this module binds). Emitted as ControlPlaneIdentity__PrincipalObjectId (task 249 -- one setting for two handlers): the principal H4 grants Key Vault Secrets Officer on each customer vault before writing its secrets (customer-provisioning-orchestration-r1 task 245b, owner-approved 2026-10-01; it previously granted the customer stamp\'s BFF UAMI instead), and the principal H2a sends as customer.bicep\'s controlPlaneUamiPrincipalId on Model 1 stamps (Website Contributor on the stamp BFF). H3 also makes it the subject of the federated credential spaarke-l2-worker on each customer BFF app registration (ISS-015), through which H6/H7/H7b sign in as that registration secret-free -- the principalId, never the clientId (AADSTS700213). REQUIRED: ControlPlaneIdentityOptions.Validate() fails Worker startup on a blank or non-GUID value. platform-controlplane.bicep passes uami.outputs.principalId -- the same value its Cosmos RBAC takes as controlPlanePrincipalId.')
param controlPlanePrincipalId string


// Task 245b. Deliberately `array`, not a user-defined type: a `type` makes
// Bicep emit languageVersion 2.0 (symbolic-name resources) for this module AND
// for platform-controlplane.bicep, which imports it -- a template-wide change to
// a live-what-if-verified deployment for a shape check that
// SpeContainerOptions.Validate() already does more strictly at Worker startup.
@description('SPE container types this L2 deployment provisions into, each with its OWNING app: [{ containerTypeId, ownerAppId }]. containerTypeId = the SPE container type GUID, matched against the run\'s intake containerTypeId; ownerAppId = the owning app registration\'s client id -- never the customer BFF app (topology section 3A). L2 signs in as the owning app through the federated identity credential on it whose subject is this Worker\'s UAMI (task 248, ADR-028 A4) -- no certificate or secret is configured or stored. Emitted as SpeContainerOptions__ContainerTypeOwners__{i}__ContainerTypeId / __OwnerAppId -- read by H0\'s SpeOwnerCredential probe, H8 (container creation) and H13\'s T6 probe. Empty (default) boots the Worker; H0 then rejects every run (spe-owner-not-configured) until the topology runbook (docs/guides/SPAARKE-SPE-TOPOLOGY-SETUP-RUNBOOK.md) has created a container type + owning app and its entry is added here. SpeContainerOptions.Validate() fails Worker startup on a non-GUID id, a duplicate container type or an owning app listed twice.')
param speContainerTypeOwners array = []

@description('Client apps H3 pre-authorizes on every customer BFF app registration for user_impersonation, so they get a token for that BFF without a consent prompt (T240a). Platform-wide, never per customer. Default: the PRODUCTION Office add-in client (Spaarke Office Add-in (Production), served from addins.spaarke.com) -- customer stamps are production. The dev add-in client c1258e2d talks only to the dev BFF and is never listed here. The Teams client joins when it exists (T240c).')
param preAuthorizedClientAppIds array = [
  '1958aec2-0218-495e-8e3c-37133e9b8357'
]

@description('Client id of the shared Spaarke Copilot Agent app (T257; secret-free public client, PKCE). When set it is appended to preAuthorizedClientAppIds, so H3 pre-authorizes it on every customer BFF app for user_impersonation. Empty (default): not added -- the app does not exist until the operator creates it. Must be a GUID: H3 rejects every run on a malformed entry (appreg-preauthorized-client-invalid).')
param copilotAgentClientAppId string = ''

@description('Entra External ID (CIAM) tenant id(s) Spaarke operates for external contacts. Emitted with the deployment tenant as ReservedTenants__CiamTenantIds__N / ReservedTenants__SpaarkeTenantId (task 255): H4b and H13 refuse either as a customer workforce tenant (CustomerWorkforceTenantsRule). REQUIRED, at least one: ReservedTenantsOptions.Validate() fails Worker startup without it. Same value the Api module receives.')
@minLength(1)
param ciamTenantIds array

@description('Tags for the resource.')
param tags object = {}

// Task 255: the tenants that are never a customer's workforce tenant — Spaarke's own (the control plane is deployed in
// it; the same value as EntraAppRegOptions__SpaarkeTenantId) and the CIAM tenant(s).
var reservedTenantSettings = concat([
  { name: 'ReservedTenants__SpaarkeTenantId', value: tenant().tenantId }
], map(range(0, length(ciamTenantIds)), i => {
  name: 'ReservedTenants__CiamTenantIds__${i}'
  value: ciamTenantIds[i]
}))

// Task 245b: flatten speContainerTypeOwners into indexed app settings (the .NET
// configuration binder's list syntax: SpeContainerOptions__ContainerTypeOwners__0__ContainerTypeId ...).
var speContainerTypeOwnerSettings = flatten(map(range(0, length(speContainerTypeOwners)), i => [
  { name: 'SpeContainerOptions__ContainerTypeOwners__${i}__ContainerTypeId', value: speContainerTypeOwners[i].containerTypeId }
  { name: 'SpeContainerOptions__ContainerTypeOwners__${i}__OwnerAppId', value: speContainerTypeOwners[i].ownerAppId }
]))

// T257: the shared Copilot agent client joins the platform's pre-authorized clients once the operator has created it.
var effectivePreAuthorizedClientAppIds = concat(preAuthorizedClientAppIds, empty(copilotAgentClientAppId) ? [] : [copilotAgentClientAppId])

// T240a: H3's platform settings. SpaarkeTenantId is the federated-credential issuer for Model 1 stamps
// (profile spaarke-hosted-model2): without it every Model 1 run fails at H3's FIC step. The control plane
// is deployed in Spaarke's own tenant, so the deployment's tenant is that value.
var entraAppRegSettings = concat([
  { name: 'EntraAppRegOptions__SpaarkeTenantId', value: tenant().tenantId }
], map(range(0, length(effectivePreAuthorizedClientAppIds)), i => {
  name: 'EntraAppRegOptions__PreAuthorizedClientAppIds__${i}'
  value: effectivePreAuthorizedClientAppIds[i]
}))

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
      appSettings: concat([
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
        // writer (and H7b, which binds the same section) signs in to each
        // customer's Dataverse env as that customer's OWN BFF app
        // registration (H3 output InterStepState.BffAppRegId; D-13 -- one
        // BFF app registration per customer), the same identity H6 uses for
        // solution import.
        //
        // A44.5 (task 205i; restored by task 245b; default flipped by task
        // 252): the credential comes from the FR-39 chain settings in
        // secretFreeCredentialAppSettings (appended via the concat + ternary
        // at the bottom of this array). The EnvVarValues__ClientSecret KV-ref
        // exists only in legacyClientSecretAppSettings (prong-3 opt-in,
        // requireSecretFreeIdentity=false); on the secret-free chain
        // EnvVarValuesOptions.Validate() accepts the empty slot.
        // ---------------------------------------------------------------

        // ---------------------------------------------------------------
        // Task 204a (Wave G-8 Class-B follow-on to task 142): SolutionImport
        // -- H6's Dataverse Web API solution importer (task 141) signs in as
        // the same per-customer BFF app registration H7 uses (H3 output;
        // D-13).
        //
        // NOTE: unlike EnvVarValuesOptions.Validate() (which fails fast at
        // boot on a missing ClientSecret under the legacy chain),
        // SolutionImportOptions.Validate() only asserts
        // ProvisioningArtifactsContainerUri + SolutionArtifactManifestBlobName
        // -- under the legacy chain a missing ClientSecret is a runtime
        // Resumable failure (SolutionImportRejectionCodes.MissingClientSecret).
        //
        // A44.5 (task 205i; restored by task 245b; default flipped by task
        // 252): the SolutionImportOptions__ClientSecret KV-ref lives in
        // legacyClientSecretAppSettings (prong-3 opt-in only); on the default
        // secret-free chain H6 selects MI-FIC via the FR-39 chain settings.
        // ---------------------------------------------------------------

        // ---------------------------------------------------------------
        // G-8 Batch 3 (audit defect #6): Level-2 dispatch-idempotency Redis
        // (DispatchModule, task 105 / DS-2 §4-L2). Task 242b: Azure Managed
        // Redis, Microsoft Entra only -- the endpoint is a plain setting and
        // the Worker signs in with its UAMI (ManagedIdentity__ClientId below);
        // the former ConnectionStrings__Redis Key Vault reference is gone.
        // Without Redis__Endpoint the Worker THROWS at composition time under
        // ASPNETCORE_ENVIRONMENT=Production (deliberate NFR-05 fail-fast).
        // ---------------------------------------------------------------
        {
          name: 'Redis__Endpoint'
          value: redisEndpoint
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
        // CustomerRunGuard (task 203b, punch list row A27 / r1-gap-analysis
        // c5-6): I5 same-customer serialization guard (spec.md §4D I5 /
        // FR-32). Bound from IConfiguration section "CustomerRunGuard" via
        // AddCustomerRunGuard() in Worker/Program.cs (Sprk.Provisioning.
        // ControlPlane.Core/Concurrency/CustomerRunGuardModule.cs +
        // CustomerRunGuardOptions.cs). Uses the ADMIN Dataverse env
        // (adminDataverseEnvironmentUrl above -- SAME registry env the
        // DataverseEnvironmentRegistry client talks to) and signs in as the
        // bound UAMI (no client secret -- see the MIGRATED note below).
        //
        // Enabled=false by default per null-object kill-switch pattern
        // (ADR-032): a fresh L2 deployment without the UAMI registered as a
        // Dataverse Application User stays boot-safe (the null-object returns
        // Success unconditionally, WARN-log on each acquire). Flip to true
        // once the bound UAMI is an Application User on the admin env --
        // CustomerRunGuardOptions.Validate() fails fast at boot on missing
        // fields when Enabled=true.
        // ---------------------------------------------------------------
        { name: 'CustomerRunGuard__TargetDataverseUrl', value: adminDataverseEnvironmentUrl }
        { name: 'CustomerRunGuard__TenantId', value: customerRunGuardTenantId }
        // MIGRATED 2026-08-27: CustomerRunGuard__ClientId + __ClientSecret removed. The guard
        // authenticated as the BFF app-reg with a client secret (ADR-028 A4 violation; E-3 CLOSED
        // 2026-08-24) because the L2 UAMI was not a Dataverse Application User on the admin env.
        // It is one now -- '# sprk-controlplane-dev-uami', Spaarke Provisioning Registry role --
        // so the store uses DefaultAzureCredential pinned to the bound UAMI, exactly as
        // DataverseEnvironmentRegistryClient already did against the same environment.
        { name: 'CustomerRunGuard__ManagedIdentityClientId', value: uamiClientId }
        { name: 'CustomerRunGuard__Enabled', value: string(customerRunGuardEnabled) }

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

        // ---------------------------------------------------------------
        // Task 245b (G25): L2-owned run inputs, validated at Worker startup.
        // ControlPlaneIdentity__PrincipalObjectId (task 249 — one setting shared
        // by two handlers): H4's KV RBAC bootstrap grants THIS principal (L2's
        // own identity) Secrets Officer on each customer vault, and H2a sends it
        // as customer.bicep's controlPlaneUamiPrincipalId on Model 1 stamps
        // (Website Contributor on the stamp BFF); H3 makes it the subject of
        // each customer BFF registration's spaarke-l2-worker federated
        // credential (ISS-015). SPE owning-app credentials
        // are appended below (speContainerTypeOwnerSettings). Task 225b (D18)
        // removed the vendor-key platform vault setting (no Spaarke-shared
        // vendor key remains in the customer catalog).
        //
        // Task 225b (G21): every new stamp is secret-free — H4 omits
        // BFF-API-ClientSecret and Dataverse-ClientSecret (BINDING
        // credential-lifecycle rule; no sentinel). Stated explicitly so the
        // deployed Worker never depends on the code default.
        // ---------------------------------------------------------------
        { name: 'ControlPlaneIdentity__PrincipalObjectId', value: controlPlanePrincipalId }
        { name: 'KvSecretsPopulationOptions__RequireSecretFreeIdentity', value: 'true' }

        // ---------------------------------------------------------------
        // Task 251 (G30): H14a's Exchange sidecar. The Worker reads the shared
        // secret from Key Vault (all three settings, else H14a fails loudly at
        // first call) and signs in to Exchange as 'Spaarke Exchange Admin'.
        // ExchangeSidecar__SharedSecret exists only so the sitecontainer can
        // name it (see the sitecontainer below).
        // ---------------------------------------------------------------
        { name: 'IntegrationWiring__SidecarSharedSecretVaultName', value: keyVaultName }
        { name: 'IntegrationWiring__SidecarSharedSecretSubscriptionId', value: subscription().subscriptionId }
        { name: 'IntegrationWiring__SidecarSharedSecretName', value: sidecarSharedSecretKvSecretName }
        { name: 'IntegrationWiring__ExchangeAdminAppId', value: exchangeAdminAppId }
        {
          name: 'ExchangeSidecar__SharedSecret'
          value: '@Microsoft.KeyVault(VaultName=${keyVaultName};SecretName=${sidecarSharedSecretKvSecretName})'
        }
      ], requireSecretFreeIdentity ? secretFreeCredentialAppSettings : legacyClientSecretAppSettings, speContainerTypeOwnerSettings, entraAppRegSettings, reservedTenantSettings)
    }
  }
}

// ============================================================================
// H14a EXCHANGE SIDECAR (DS-1b Section 3 / design.md Section 4.2a; task 251)
// -- Microsoft.Web/sites/sitecontainers child resource. Shares the Worker
// site's network namespace (localhost-only, not publicly routed). Holds no
// credential: the Worker sends the Exchange token with each request.
// Each environmentVariables value NAMES an app setting above (Microsoft's
// sitecontainers contract) -- never a literal. The sidecar binds its port
// even when a setting is missing, so it can never hold the Worker site down.
// ============================================================================

resource exchangePolicySidecar 'Microsoft.Web/sites/sitecontainers@2024-04-01' = {
  parent: appService
  name: 'exchange-policy-sidecar'
  properties: {
    image: acrImageTag
    targetPort: '8091'
    isMain: false
    authType: sidecarAuthType
    // Required when authType == 'UserAssigned' (customer-provisioning-orchestration-r1
    // Wave H-3 fix-at-discovery 2026-08-21): ACR pull 401s without this when the tag
    // points to the platform ACR. Ignored by the platform when authType == 'Anonymous'
    // (the MCR-placeholder default), so unconditionally setting it is safe.
    userManagedIdentityClientId: uamiClientId
    environmentVariables: [
      // The Worker -> sidecar shared secret (DS-1b Section 3), through the
      // ExchangeSidecar__SharedSecret Key Vault reference app setting -- which
      // resolves with the site's keyVaultReferenceIdentity (Deploy-ControlPlane.ps1).
      { name: 'SIDECAR_SHARED_SECRET', value: 'ExchangeSidecar__SharedSecret' }
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
