// infrastructure/bicep/platform-controlplane.bicep
//
// L2 CONTROL-PLANE INFRASTRUCTURE (fleet-scoped orchestrator home).
//
// PURPOSE
//   Composes the infrastructure that hosts the L2 orchestration service
//   (Sprk.Provisioning.ControlPlane) - the single fleet-scoped .NET 10 App
//   Service that sequences all 19 IJobHandler dispatches for customer
//   environment provisioning. One deployment per environment (dev/staging/prod)
//   into rg-spaarke-platform-{env} (parity with BFF per spec.md §4.2 B2).
//
// SPEC REFERENCES (customer-provisioning-orchestration-r1)
//   - spec.md FR-20:  L2 hosted on .NET 10 App Service in rg-spaarke-platform-{env};
//                     audience api://spaarke.com/provisioning-controlplane-{env}
//                     (tenant-policy-forced verifier-domain form; DS-5 C5.2);
//                     Operator/Reader app-roles.
//   - spec.md § "New Components" row 6:  NEW platform-controlplane.bicep.
//   - design.md §4.2 (v3):  App Service hosting (B2), REST + AAD (B1),
//                            fire-and-forget + state-reconciler execution model.
//   - design.md §5.3 D13:  separate stores per concern (Cosmos for run state).
//   - design.md §7:  resource inventory line 12 (App Insights + Log Analytics).
//
// COMPOSITION (five resource families, subscription-scope entry)
//   1. Resource Group:  rg-spaarke-platform-{env}
//   2. Monitoring:      App Insights + Log Analytics workspace (module invocation)
//   3. UAMI:            Control-plane User-Assigned Managed Identity (module invocation)
//                       - Distinct from per-customer UAMIs (task 028's module).
//                       - Fleet-scoped; binds to L2 App Service on BOTH slots.
//   4. Platform KV:     sprk-controlplane-{env}-kv (module invocation)
//                       - Holds control-plane secrets (Dataverse S2S, Graph app-only,
//                         Service Bus SAS, Cosmos endpoint refs, etc.).
//                       - Grants Key Vault Secrets User to the UAMI.
//   5. App Service:     spaarke-provisioning-controlplane-{env}
//                       + staging slot (via modules/controlplane-app-service.bicep;
//                       a dedicated UAMI-only module, distinct from the
//                       BFF-oriented modules/app-service.bicep - see deviation
//                       note below).
//   6. Cosmos:          Invokes task 024's modules/cosmos-provisioning.bicep
//                       (spaarke-provisioning DB + runs container, /customerId
//                       partition, RBAC-only, Continuous7Days backup, TTL 365d).
//   7. Fleet SB queue:  sprk-provisioning-jobs (task 108 / DS-5 C5.4/C4.6) -
//                       declared as a child of an `existing`, cross-resource-
//                       group reference to the fleet Service Bus namespace.
//                       See "DELIBERATELY OUT OF SCOPE > Service Bus" below.
//   8. .Worker App Service + Exchange sidecar (task 101 / DS-3 §3 Option 2):
//                       spaarke-provisioning-controlplane-worker-{env}
//                       (via modules/controlplane-worker-app-service.bicep).
//                       Slotless (deploy = stop -> zip-deploy -> start; no
//                       staging-slot shadow-worker per DS-3 §1.3), SAME
//                       plan as .Api ($0 marginal cost), SAME shared UAMI
//                       (v1 - two-UAMI least-privilege split is the DS-3
//                       target shape, not required at v1). Hosts the
//                       DS-1b Exchange ApplicationAccessPolicy sidecar as a
//                       Microsoft.Web/sites/sitecontainers child resource.
//   9. Artifacts SA:    sprkcpartifacts{env} (Wave G-8 Batch 2 / audit
//                       defects #3+#5) -- StorageV2 + provisioning-artifacts
//                       container; CI (OIDC, Blob Data Contributor) uploads
//                       compiled ARM JSON / BFF zips / solution zips; the
//                       Worker's H2a/H6/H9 handlers download them via the
//                       shared UAMI (Blob Data Reader). Its blobUri output
//                       is the value the Worker's three
//                       *Options__ProvisioningArtifactsContainerUri app
//                       settings must carry (wired by the worker module).
//   10. Platform ACR:   sprkcontrolplane{env}acr (Wave G-8 Batch 2 / audit
//                       defects #4+#10) -- hosts the task 114/115 Exchange
//                       sidecar image; AcrPull to the shared UAMI, AcrPush
//                       to the CI OIDC principal.
//   11. Subscription RBAC: Contributor for the shared UAMI at the deploying
//                       subscription's scope (Wave G-8 Batch 2 / audit
//                       defect #2) -- H2a's ArmDeploymentRunner needs it for
//                       customer RG-ensure + subscription-scope ARM deploys
//                       (via modules/controlplane-subscription-rbac.bicep;
//                       BCP120 forces the module split -- see its header).
//
// DELIBERATELY OUT OF SCOPE
//   - Service Bus:      Per ADR-036 (background-job infrastructure) the L2
//                       control-plane REUSES the environment-scope Service Bus
//                       already provisioned by env infra. This stack takes the
//                       Service Bus KV-secret NAME as a parameter and wires an
//                       @Microsoft.KeyVault reference into App Service settings;
//                       it never creates a new Service Bus namespace.
//                       Task 108 (DS-5 C5.4/C4.6) ADDS a Bicep-managed CHILD
//                       QUEUE resource (`sprk-provisioning-jobs`) as an
//                       `existing`-scoped reference to that fleet namespace
//                       -- still not a namespace create. The namespace lives
//                       in a DIFFERENT resource group (`SharePointEmbedded`
//                       on dev -- a legacy pre-per-env-model artifact, not the
//                       canonical `rg-spaarke-{env}` shape; see
//                       docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md
//                       "Dev Environment (DO NOT RENAME)"), so the queue
//                       resource below carries an explicit cross-RG `scope:`.
//   - Per-customer AI:  OpenAI / AI Search / Doc Intelligence / per-customer
//                       Cosmos live in customer.bicep per D3/D12. This stack
//                       MUST NOT declare any of them.
//   - keyVaultReferenceIdentity PATCH:  Bicep provisions the resource shape
//                       + UAMI binding on identity.userAssignedIdentities; the
//                       keyVaultReferenceIdentity PATCH to the UAMI resourceId
//                       is applied by handler H4 (post-deploy) on BOTH slots per
//                       spec.md MUST rule + design.md T1.
//   - Dataverse App User registration:  Handled by handler H10 (uses UAMI
//                       clientId as the application ID). Not a Bicep concern.
//
// DEVIATION FROM POML STEP 4 (documented per CLAUDE.md §6.5 path A)
//   POML step 4 calls for invoking modules/app-service.bicep (task 029
//   UAMI-refactored). At author time (2026-08-17) task 029 has NOT shipped -
//   modules/app-service.bicep still emits SystemAssigned MI only + carries no
//   UAMI parameter. Rather than block Wave C2 batch 2 on the sibling refactor
//   OR emit a co-mixed SystemAssigned+UserAssigned identity block (an anti-
//   pattern per ADR-028 MUST rules), this stack invokes a NEW dedicated
//   module: modules/controlplane-app-service.bicep - which emits the L2 App
//   Service with UAMI-only identity from birth. Rationale documented in
//   projects/customer-provisioning-orchestration-r1/notes/task-033-deviations.md.
//   When task 029 lands, this file can OPTIONALLY be refactored to invoke the
//   general-purpose module + retire the dedicated one - the topology is
//   equivalent.
//
// USAGE (representative - actual invocation via ops scripts / Phase F pipeline)
//   az deployment sub create \
//     --location westus2 \
//     --template-file infrastructure/bicep/platform-controlplane.bicep \
//     --parameters environmentName=dev \
//                  serviceBusNamespaceName=spaarke-servicebus-dev serviceBusResourceGroupName=SharePointEmbedded \
//                  adminDataverseEnvironmentUrl=https://spaarkedev1.crm.dynamics.com

targetScope = 'subscription'

// ============================================================================
// PARAMETERS
// ============================================================================

@description('Environment name (dev, staging, prod). Drives naming + tag envelope.')
@allowed(['dev', 'staging', 'prod'])
param environmentName string

@description('Primary Azure region for all fleet-scoped control-plane resources.')
param location string = 'westus2'

@description('App Service Plan SKU. Must be P1v3 or better per spec.md §4.2 B2 (parity with BFF, always-on for state-reconciler BackgroundService, sufficient headroom for 19-handler DAG traversal + Cosmos reads at expected cadence). Basic/Standard SKUs disallowed - alwaysOn semantics + slot support + memory ceiling matter.')
@allowed(['P1v3', 'P2v3', 'P3v3'])
param appServicePlanSku string = 'P1v3'

@description('Log Analytics retention (days). 180 default matches BFF platform (rg-spaarke-platform-{env}). NFR-11 requires auditable operator action; 180 days covers audit retention window.')
@minValue(30)
@maxValue(730)
param logRetentionDays int = 180

@description('Name of the fleet-scoped Service Bus namespace that hosts the sprk-provisioning-jobs queue (task 108 / DS-5 C5.4). Empty defaults to spaarke-servicebus-{environmentName} (the legacy-but-canonical name per AZURE-RESOURCE-NAMING-CONVENTION.md; verified live value for dev is spaarke-servicebus-dev). This stack does NOT create the namespace - it only declares the queue as its child via an `existing` reference.')
param serviceBusNamespaceName string = ''

@description('Name of the resource group that hosts the fleet-scoped Service Bus namespace (task 108 / DS-5 C5.4). Defaults to SharePointEmbedded - the verified live dev value; this is a legacy pre-per-env-model resource group name, NOT the canonical rg-spaarke-{env} shape, so staging/prod deploys MUST override this parameter once the shared Service Bus resource group name for those environments is known.')
param serviceBusResourceGroupName string = 'SharePointEmbedded'

// NOTE (FR-38 / Wave G-8 Batch 2, audit defect #20): the former
// `dataverseClientSecretName` param + its `Dataverse__ClientSecret`
// app-setting emissions were DELETED here and in both controlplane app-service
// modules. Task 112's Path X migration made the runtime code path MI-native
// (DataverseEnvironmentRegistryClient via DefaultAzureCredential pinned to the
// UAMI); FR-38's acceptance criterion explicitly requires the Bicep residue's
// absence. The `Dataverse-ClientSecret` KV SECRET itself is untouched
// (BINDING never-delete per scripts/canonical-secret-catalog/manifest.yaml).

@description('Admin Dataverse environment URL (e.g. https://spaarkedev1.crm.dynamics.com) hosting the sprk_dataverseenvironment registry table -- passed through to modules/controlplane-worker-app-service.bicep as DataverseEnvironmentRegistry__AdminEnvironmentUrl (task 122 / task 112 Path X MI-native client). REQUIRED: DataverseEnvironmentRegistryOptions.Validate() fails fast at Worker boot (NFR-05) if this is missing -- no default is supplied here deliberately (dev/staging/prod each target a distinct admin Dataverse environment; a default would risk silently pointing a non-dev deploy at the dev org).')
param adminDataverseEnvironmentUrl string

@description('Name of the platform Key Vault secret holding the shared BFF app-registration client secret (canonical name "BFF-API-ClientSecret" -- BINDING never-delete per scripts/canonical-secret-catalog/manifest.yaml). Passed through to modules/controlplane-worker-app-service.bicep as the EnvVarValues__ClientSecret KV-reference source (task 142, Wave G-4 -- H7 credential provisioning). REQUIRED: EnvVarValuesOptions.Validate() fails fast at Worker boot (NFR-05) if the resolved secret value is missing. Same secret name every environment resolves (the shared multitenant BFF app-reg is Spaarke-tenant-scoped per spec.md §9.1 v3, not per-customer), so a stable default is safe here (contrast with adminDataverseEnvironmentUrl above, which is deliberately env-specific with no default).')
param bffApiClientSecretName string = 'BFF-API-ClientSecret'

@description('Name of the platform Key Vault secret holding the shared-platform Azure OpenAI resource endpoint (canonical name "AzureOpenAI-Endpoint" per scripts/canonical-secret-catalog/manifest.yaml -- the SAME secret the .Api site already resolves as AzureOpenAI__Endpoint / DocumentIntelligence__OpenAiEndpoint). Passed through to modules/controlplane-worker-app-service.bicep as the RuntimeReferences__SharedPlatformOpenAiEndpoint KV-reference source (task 153, Wave G-5 -- H12c credential-config confirmation). CONDITIONALLY required: only H12c\'s Model1Shared branch consults it; RuntimeReferencesOptions.Validate() does NOT fail-fast at boot on this being unset (contrast with adminDataverseEnvironmentUrl / bffApiClientSecretName above, both of which every run needs).')
param azureOpenAiEndpointSecretName string = 'AzureOpenAI-Endpoint'

@description('Principal ID of the GitHub Actions OIDC service principal for CI artifact publishing (Wave G-8 Batch 2). When provided, grants Storage Blob Data Contributor on the provisioning-artifacts storage account (tasks 116/117 upload compiled ARM JSON / BFF zips / solution zips) and AcrPush on the platform ACR (task 115 sidecar image push). Empty (default) skips BOTH grants -- supply once the CI OIDC app-reg principal is known for this environment.')
param githubActionsOidcPrincipalId string = ''

@description('Container image reference for the DS-1b Exchange sidecar, threaded through to modules/controlplane-worker-app-service.bicep (Wave G-8 Batch 2 / audit defect #11 -- the worker module always supported this but nothing plumbed it to the top level, so a real sidecar image could never be deployed). Default remains the documented public MCR placeholder until task 115 CI pushes the real image; then override with {acrLoginServer output}/sprk-provisioning-sidecar:{tag}. Do not leave the placeholder in a live deploy.')
param acrImageTag string = 'mcr.microsoft.com/appsvc/staticsite:latest'

@description('ACR authentication mode for the sidecar sitecontainer pull, threaded through to modules/controlplane-worker-app-service.bicep (Wave G-8 Batch 2 / audit defect #11). Default is COMPUTED from acrImageTag so the default parameter pair stays coherent: the public MCR placeholder needs Anonymous; any other (platform-ACR) image defaults to UserAssigned, backed by the AcrPull grant this stack now makes on the platform ACR (defect #4). Override explicitly if needed.')
param sidecarAuthType string = startsWith(acrImageTag, 'mcr.microsoft.com/') ? 'Anonymous' : 'UserAssigned'

@description('Client (application) ID of the Exchange Online connect app registration the sidecar authenticates as (app-only Connect-ExchangeOnline). Threaded through to modules/controlplane-worker-app-service.bicep as the EXCHANGE_CONNECT_APP_ID sitecontainer environment variable (customer-provisioning-orchestration-r1 Wave H-3 fix-at-discovery 2026-08-21 — the worker module declared this param with default \'\' but the platform stack never plumbed it, so the sidecar always got an empty value and exited 1 at Listener.ps1 startup fail-fast). All-zero GUID default lets the sidecar START without a real EXO app-reg (Verify-Sidecar-Live.ps1 explicitly accommodates this: "all-zero GUIDs reach sidecar but Set-ExchangeApplicationAccessPolicy.ps1 rejects at Connect-ExchangeOnline before any real Exchange mutation"). Override with the real EXO connect app-reg client ID once H3 Entra app-reg handler output supplies it at customer/platform onboarding.')
param exchangeConnectAppId string = '00000000-0000-0000-0000-000000000000'

@description('BFF Entra app-registration client (application) ID used by the CustomerRunGuard concurrency guard to auth against the admin Dataverse env (customer-provisioning-orchestration-r1 task 203b, punch list row A27). Threaded through to modules/controlplane-worker-app-service.bicep as CustomerRunGuard__ClientId. SAME app-reg H6/H7 use (bffApiClientSecretName above provides the client-secret side). Empty default keeps CustomerRunGuard__Enabled=false safe -- supply when flipping the guard on.')
param customerRunGuardClientId string = ''

@description('Kill-switch for the CustomerRunGuard (customer-provisioning-orchestration-r1 task 203b, punch list row A27). Threaded through to modules/controlplane-worker-app-service.bicep as CustomerRunGuard__Enabled. Default false per ADR-032 null-object kill-switch -- flip true once customerRunGuardClientId + the platform-KV BFF-API-ClientSecret are in place; then CustomerRunGuardOptions.Validate() fails fast at Worker boot on any missing field. spec.md §4D I5 / FR-32 requires this true in production.')
param customerRunGuardEnabled bool = false

@description('Tenant ID for JWT bearer authority validation on the L2 REST API. Empty defaults to subscription tenant ID (single-issuer per spec.md §4.2 - the control plane is Spaarke-internal, never customer-tenant).')
param jwtTenantId string = ''

@description('Client ID (app registration application ID) of the L2 control-plane app-reg. Used only for logging + config surface; the AAD bearer audience is derived from environmentName.')
param controlPlaneAppRegClientId string = ''

@description('Tag envelope applied to every resource. Do NOT hardcode - callers may override for cost-attribution or naming exceptions.')
param tags object = {
  environment: environmentName
  application: 'spaarke'
  layer: 'platform-controlplane'
  managedBy: 'bicep'
  purpose: 'l2-orchestrator'
}

// ============================================================================
// VARIABLES - Naming (canonical per docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)
// ============================================================================

// Resource group parity with BFF platform (both live in rg-spaarke-platform-{env}
// per spec.md §4.2 B2). BFF has its own App Service + AI resources; L2 adds
// its own App Service + Cosmos + KV alongside.
var resourceGroupName = 'rg-spaarke-platform-${environmentName}'

// UAMI: fleet-scoped, distinct from per-customer UAMIs (task 028's module names
// per-customer UAMIs sprk-{env}-{customerId}-uami). This one is the ORCHESTRATOR
// identity - it binds to the L2 App Service and holds every RBAC grant the
// orchestrator needs (Cosmos data-contributor, Service Bus sender, KV secrets
// user, Dataverse-app-user application ID via H10, Graph app-roles via H10).
var controlPlaneUamiName = 'sprk-controlplane-${environmentName}-uami'

// App Service Plan (Linux, P1v3 default). Prefix mirrors L2's controlplane
// scope (distinct from spaarke-bff-{env}-plan). Ops scripts / autoscale rules
// key off this name.
var appServicePlanName = 'spaarke-controlplane-${environmentName}-plan'

// App Service NAME mirrors the FR-20 audience's meaningful segment - the
// resource name is the deterministic default hostname; keeping it aligned
// with the audience's provisioning-controlplane-{env} segment (full audience
// per DS-5 C5.2 is api://spaarke.com/provisioning-controlplane-{env} - the
// spaarke.com/ prefix is the tenant-policy-forced verified-domain segment,
// not part of the resource name) reduces operator cognitive load + makes
// tenant-config sanity checks trivial.
var appServiceName = 'spaarke-provisioning-controlplane-${environmentName}'

// .Worker App Service NAME (task 101 / DS-3 Section 3 Option 2). No audience
// implication - the Worker has no auth surface (only /healthz + /ping) - so
// the name just needs to be deterministic + collision-free alongside the
// .Api site on the SAME plan.
var workerAppServiceName = 'spaarke-provisioning-controlplane-worker-${environmentName}'

// FR-20 acceptance: audience MUST be api://spaarke.com/provisioning-controlplane-{env}
// (DS-5 C5.2) -- the tenant's AAD verified-domain policy forces the
// `api://{verified-domain}/...` audience shape; the prior form (scheme +
// resource segment only, no verified-domain segment) does not satisfy that
// policy. This matches the live L2 app-reg's identifier URI and the
// already-corrected SKILL.md.
var jwtAudience = 'api://spaarke.com/provisioning-controlplane-${environmentName}'

// Cosmos DB account name. Convention: cosmos-{purpose}-{env}. Cosmos account
// names are globally unique + max 44 chars + lowercase alphanumeric+hyphens;
// 'cosmos-spaarke-platform-{env}' fits all envs.
var cosmosAccountName = 'cosmos-spaarke-platform-${environmentName}'

// Platform KV for L2. Canonical convention (AZURE-RESOURCE-NAMING-CONVENTION.md
// R3): sprk-{scope}-{env}-kv. Keeping controlplane in the scope segment because
// L2 secrets are DIFFERENT from BFF's (sprk-{env}-kv holds BFF-specific secrets;
// L2 has its own audience/appreg/ServiceBus/Dataverse-orchestration credentials).
var keyVaultName = 'sprk-controlplane-${environmentName}-kv'

var appInsightsName = 'sprk-controlplane-${environmentName}-insights'
var logAnalyticsName = 'sprk-controlplane-${environmentName}-logs'

// Provisioning-artifacts storage account (Wave G-8 Batch 2 / audit defect #5).
// Storage names: lowercase alphanumeric only, <=24 chars, globally unique
// (AZURE-RESOURCE-NAMING-CONVENTION.md no-hyphen storage rule).
// sprkcpartifactsstaging = 22 chars -- longest env fits.
var artifactsStorageAccountName = 'sprkcpartifacts${environmentName}'

// Platform ACR (Wave G-8 Batch 2 / audit defect #10). ACR names: alphanumeric
// only (no hyphens), 5-50 chars, globally unique.
var acrName = 'sprkcontrolplane${environmentName}acr'

// SKU escalation for prod: region-resilient artifact reads (ZRS) + ACR
// storage/throughput headroom (Standard). Dev/staging stay on the cheap tier
// -- artifacts are CI-reproducible from a git sha.
var artifactsStorageSku = environmentName == 'prod' ? 'Standard_ZRS' : 'Standard_LRS'
var acrSku = environmentName == 'prod' ? 'Standard' : 'Basic'

// Effective JWT tenant - default to subscription tenant if not overridden.
// Single-issuer per spec.md §4.2 (control plane is Spaarke-internal).
var effectiveJwtTenantId = empty(jwtTenantId) ? subscription().tenantId : jwtTenantId

// Effective fleet Service Bus namespace name - default to the
// spaarke-servicebus-{env} legacy-but-canonical shape if not overridden
// (task 108 / DS-5 C5.4; verified live value for dev is spaarke-servicebus-dev
// via `az resource list --name spaarke-servicebus-dev`).
var effectiveServiceBusNamespaceName = empty(serviceBusNamespaceName) ? 'spaarke-servicebus-${environmentName}' : serviceBusNamespaceName

// ============================================================================
// RESOURCE GROUP
// ============================================================================

resource rg 'Microsoft.Resources/resourceGroups@2023-07-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// ============================================================================
// 1. MONITORING - Deploy FIRST (KV diagnosticSettings + App Service instrumentation
//                                reference these outputs).
// ============================================================================

module monitoring 'modules/monitoring.bicep' = {
  scope: rg
  name: 'controlplane-monitoring'
  params: {
    appInsightsName: appInsightsName
    logAnalyticsName: logAnalyticsName
    location: location
    retentionInDays: logRetentionDays
    tags: tags
  }
}

// ============================================================================
// 2. CONTROL-PLANE UAMI - Fleet-scoped, single identity for the L2 App Service.
//    Binds to BOTH slots (see App Service resource below). Every downstream RBAC
//    grant references this UAMI's principalId; every Dataverse App User row +
//    Graph app-role assignment references its clientId (via H10 handler).
// ============================================================================

module uami 'modules/uami.bicep' = {
  scope: rg
  name: 'controlplane-uami'
  params: {
    name: controlPlaneUamiName
    location: location
    tags: tags
  }
}

// ============================================================================
// 3. PLATFORM KV - Control-plane secret store. Grants Key Vault Secrets User to
//    the UAMI so the App Service (bound to that UAMI) can resolve
//    @Microsoft.KeyVault references at startup + runtime.
//
//    NOTE: The keyVaultReferenceIdentity PATCH (T1 fix) is APPLIED BY H4 handler
//    post-deploy against BOTH prod + staging slots; Bicep cannot set the App
//    Service's keyVaultReferenceIdentity in the same deployment that creates
//    both the App Service and the KV role assignment because App Service reads
//    the setting during startup + it must reference an ALREADY-GRANTED UAMI.
// ============================================================================

module keyVault 'modules/key-vault.bicep' = {
  scope: rg
  name: 'controlplane-keyvault'
  params: {
    keyVaultName: keyVaultName
    location: location
    sku: 'standard'
    // Grant the UAMI Key Vault Secrets User (read secrets) at deploy time so H4
    // PATCH has a valid target from the start.
    appServicePrincipalId: uami.outputs.principalId
    // Wire audit diagnostics into the workspace shared with L2 telemetry
    // (NFR-11: auditable operator action).
    logAnalyticsWorkspaceId: monitoring.outputs.logAnalyticsId
    tags: tags
  }
}

// ============================================================================
// 4. COSMOS DB - Fleet-scoped orchestration state store.
//    Invokes task 024's cosmos-provisioning.bicep which provisions the account
//    + spaarke-provisioning database + runs container (/customerId partition,
//    composite indexes, disableLocalAuth:true, Continuous7Days backup) AND
//    grants the L2 MI Cosmos DB Built-in Data Contributor at account scope.
//    This is the CANONICAL fleet-scoped Cosmos - do NOT re-declare here.
// ============================================================================

module cosmos 'modules/cosmos-provisioning.bicep' = {
  scope: rg
  name: 'controlplane-cosmos-provisioning'
  params: {
    accountName: cosmosAccountName
    location: location
    // databaseName + containerName + defaultTtlSeconds default to spec values.
    controlPlanePrincipalId: uami.outputs.principalId
    tags: tags
  }
}

// ============================================================================
// 5. APP SERVICE PLAN (Linux, P1v3 default per B2 parity with BFF).
// ============================================================================

module appServicePlan 'modules/app-service-plan.bicep' = {
  scope: rg
  name: 'controlplane-app-service-plan'
  params: {
    planName: appServicePlanName
    location: location
    sku: appServicePlanSku
    os: 'Linux'
    tags: tags
  }
}

// ============================================================================
// 6. L2 APP SERVICE + STAGING SLOT (UAMI-only, dedicated module)
//
//    modules/controlplane-app-service.bicep emits the App Service + staging
//    slot with the UAMI bound from birth (see file-header deviation note).
//    Both slots share the SAME UAMI => KV RBAC + Dataverse App User + Graph
//    app-roles do NOT drift on slot swap (T1/T5 structural fix).
// ============================================================================

module appService 'modules/controlplane-app-service.bicep' = {
  scope: rg
  name: 'controlplane-app-service'
  params: {
    appServiceName: appServiceName
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    userAssignedIdentityResourceId: uami.outputs.id
    uamiClientId: uami.outputs.clientId
    jwtAudience: jwtAudience
    jwtTenantId: effectiveJwtTenantId
    controlPlaneAppRegClientId: controlPlaneAppRegClientId
    cosmosAccountEndpoint: cosmos.outputs.accountEndpoint
    cosmosDatabaseName: cosmos.outputs.databaseName
    cosmosRunsContainerName: cosmos.outputs.containerName
    // (keyVaultName arg removed -- Wave G-8 Batch 2: the .Api module's only
    // KV-ref app setting was the deleted Path-Y Dataverse__ClientSecret.)
    // DS-5 C5.1: MI-only FQNS + queue name, NOT a KV-ref connection string.
    serviceBusNamespaceName: effectiveServiceBusNamespaceName
    serviceBusQueueName: 'sprk-provisioning-jobs'
    appInsightsConnectionString: monitoring.outputs.connectionString
    tags: tags
  }
}

// ============================================================================
// 6a. .WORKER APP SERVICE + EXCHANGE SIDECAR (task 101 / DS-3 Section 3
//    Option 2, task 100's .Api/.Worker split) -- slotless, SAME P1v3 plan as
//    .Api ($0 marginal cost), SAME shared control-plane UAMI (v1 -- the
//    two-UAMI least-privilege split from DS-3 Section 3 is the target shape,
//    not required at v1). Hosts the task 102 session-serialized dispatcher +
//    state-reconciler + crash-recovery + the 20-handler fleet, plus the
//    DS-1b Exchange ApplicationAccessPolicy sidecar as a sitecontainer child
//    resource (moves the Exchange-admin-capable container off the
//    internet-facing .Api site -- DS-3 Section 5).
// ============================================================================

module workerAppService 'modules/controlplane-worker-app-service.bicep' = {
  scope: rg
  name: 'controlplane-worker-app-service'
  params: {
    appServiceName: workerAppServiceName
    appServicePlanId: appServicePlan.outputs.planId
    location: location
    userAssignedIdentityResourceId: uami.outputs.id
    uamiClientId: uami.outputs.clientId
    cosmosAccountEndpoint: cosmos.outputs.accountEndpoint
    cosmosDatabaseName: cosmos.outputs.databaseName
    cosmosRunsContainerName: cosmos.outputs.containerName
    keyVaultName: keyVault.outputs.keyVaultName
    keyVaultUri: keyVault.outputs.keyVaultUri
    // DS-5 C5.1 follow-on fix (task 110): MI-only FQNS + queue name, NOT a
    // KV-ref connection string -- same fix shape as .Api above, applied to
    // .Worker (task 101 added this module after DS-5 was authored, carrying
    // the identical drift; see modules/controlplane-worker-app-service.bicep
    // header "DS-5 C5.1 FOLLOW-ON FIX" for the full rationale).
    serviceBusNamespaceName: effectiveServiceBusNamespaceName
    serviceBusQueueName: 'sprk-provisioning-jobs'
    adminDataverseEnvironmentUrl: adminDataverseEnvironmentUrl
    bffApiClientSecretName: bffApiClientSecretName
    azureOpenAiEndpointSecretName: azureOpenAiEndpointSecretName
    // Wave G-8 Batch 2 (audit defect #11): sidecar image + pull-auth plumbed
    // from top-level params (previously the worker module's defaults were
    // unreachable from this stack). The sitecontainer's UserAssigned
    // pull-identity wiring (userManagedIdentityClientId) is the worker
    // module's concern (Batch 3).
    acrImageTag: acrImageTag
    sidecarAuthType: sidecarAuthType
    // Wave H-3 fix-at-discovery 2026-08-21: worker module always had this
    // param but nothing plumbed it here; empty value caused sidecar Listener.ps1
    // fail-fast (exit 1) → App Service killed whole site startup. See top-level
    // exchangeConnectAppId param description for full rationale.
    exchangeConnectAppId: exchangeConnectAppId
    // A27 (customer-provisioning-orchestration-r1 task 203b, punch list row A27
    // / r1-gap-analysis c5-6): CustomerRunGuard I5 same-customer serialization
    // guard config. Same shared BFF app-reg identity H6/H7/H4 use -- reuses
    // adminDataverseEnvironmentUrl (target) + bffApiClientSecretName (secret);
    // adds tenantId + clientId + kill-switch here. Enabled=false by default
    // per ADR-032 null-object kill-switch (see worker module param docstring).
    customerRunGuardTenantId: effectiveJwtTenantId
    customerRunGuardClientId: customerRunGuardClientId
    customerRunGuardEnabled: customerRunGuardEnabled
    // Wave G-8 Batch 2 (audit defects #5/#7 hand-off): container-scoped blob
    // URI of the provisioning-artifacts store (module 9 below). Batch 3's
    // worker module emits it as the three
    // *Options__ProvisioningArtifactsContainerUri app settings H2a/H6/H9
    // Validate() against (NFR-05).
    artifactsStorageContainerUri: artifactsStorage.outputs.blobUri
    appInsightsConnectionString: monitoring.outputs.connectionString
    tags: tags
  }
}

// ============================================================================
// 7. FLEET SERVICE BUS QUEUE - sprk-provisioning-jobs (task 108 / DS-5 C5.4/C4.6)
//
//    Does NOT create the namespace (per file-header "DELIBERATELY OUT OF
//    SCOPE" - the fleet-scoped Service Bus namespace is env infra, reused
//    per ADR-036). Declares the queue as a Bicep-managed CHILD resource of
//    an `existing` cross-resource-group reference to that namespace.
//
//    requiresDuplicateDetection + requiresSession are CREATE-TIME-ONLY
//    properties in Azure Service Bus - they cannot be applied to the live
//    queue (created via bare `az servicebus queue create` defaults: both
//    OFF) by an in-place `az deployment`. Landing this Bicep declaration is
//    necessary but NOT sufficient - the live queue must be deleted and
//    recreated once, per the runbook at
//    projects/customer-provisioning-orchestration-r1/notes/queue-recreate-runbook-2026-08.md.
//    That live delete+recreate is a separate, human-run ceremony - this
//    Bicep file only declares the desired end state.
//
//    requiresSession: true              - DS-2/DS-2b session-serialized
//                                          per-customer dispatch decision
//                                          (task 102's ServiceBusSessionProcessor;
//                                          SessionId = CustomerId already set
//                                          on every enqueue per
//                                          ServiceBusHandlerEnqueuer.cs header).
//    requiresDuplicateDetection: true   - FR-22 Level-1 idempotency (wire-level
//                                          MessageId dedup; level 1 of 3 -
//                                          see ServiceBusHandlerEnqueuer.cs).
//    duplicateDetectionHistoryTimeWindow: PT1H - must exceed the longest
//                                          reconciler retry re-enqueue window
//                                          for the same paramHash; handlers
//                                          run <=30-60 min per DS-2b §1.2, so
//                                          PT1H is the documented safe floor.
//                                          NOTE: task 107 (attempt field in
//                                          HandlerEnvelope) MUST land before
//                                          or alongside this queue's live
//                                          recreation, or every §4C
//                                          RetryableWithCleanup auto-retry
//                                          within the PT1H window is SILENTLY
//                                          dropped by SB dedup (identical
//                                          MessageId as the original attempt).
//                                          See the runbook's "PT1H dedup
//                                          window vs §4C retry" section.
//    lockDuration: PT5M                 - matches service-bus.bicep's
//                                          existing queues (sdap-jobs,
//                                          document-indexing) +
//                                          membership-topic.bicep's
//                                          subscription; handler dispatch is
//                                          typically well under 5 min.
//    maxDeliveryCount: 10               - matches the repo-wide convention
//                                          (service-bus.bicep, membership-topic.bicep).
//    deadLetteringOnMessageExpiration: true - move expired/exhausted messages
//                                          to DLQ for operator inspection
//                                          rather than silent loss.
//
//    DEVIATION FROM POML STEP 1 (documented per CLAUDE.md §6.5 path C -
//    pivot to comply): the POML/DS-5 prose describes this as a direct
//    `resource sbNamespace ... existing = {...}` + child `queues` resource
//    declared inline in this file. `az bicep build` rejects that shape with
//    BCP165 ("A resource's computed scope must match that of the Bicep
//    file... You must use modules to deploy resources to a different
//    scope.") because this file's ambient scope is the L2 stamp's own
//    resource group (via the `rg` resource + module `scope: rg` pattern
//    used everywhere else in this file), not the fleet namespace's resource
//    group. The queue declaration is therefore a MODULE
//    (modules/controlplane-sb-queue.bicep) invoked with an explicit
//    `scope: resourceGroup(serviceBusResourceGroupName)` - functionally
//    identical to the POML's intent (Bicep-managed, deterministic, not a
//    runbook `az` command), just via the mechanism Bicep actually requires
//    for cross-resource-group declarations. Task 110 (SB RBAC) hits the
//    same BCP165 constraint for the same reason - see its POML's own
//    cross-RG module guidance.
// ============================================================================

module fleetServiceBusQueue 'modules/controlplane-sb-queue.bicep' = {
  scope: resourceGroup(serviceBusResourceGroupName)
  name: 'controlplane-sb-queue'
  params: {
    serviceBusNamespaceName: effectiveServiceBusNamespaceName
    queueName: 'sprk-provisioning-jobs'
  }
}

// ============================================================================
// 8. FLEET SERVICE BUS RBAC - Data Sender + Data Receiver for the shared
//    control-plane UAMI (task 110 / DS-5 C5.5)
//
//    Grants the SAME cross-RG BCP165 treatment as the queue module above
//    (module invoked with an explicit `scope: resourceGroup(...)` pointing
//    at the namespace's resource group, NOT this file's ambient `rg`).
//
//    Prior to this task, every live RBAC grant on the dev stamp was a
//    manual `az role assignment create` (Sender only) - Receiver was
//    granted nowhere, a hard blocker for task 102's dispatcher
//    (ServiceBusSessionProcessor.StartProcessingAsync fails immediately
//    without it). Both grants land on the ONE shared control-plane UAMI
//    (DS-3 Section 3 v1 - .Api and .Worker share an identity; the
//    two-UAMI least-privilege split is a documented future refinement,
//    not required here).
// ============================================================================

module fleetServiceBusRbac 'modules/controlplane-sb-rbac.bicep' = {
  scope: resourceGroup(serviceBusResourceGroupName)
  name: 'controlplane-sb-rbac'
  params: {
    serviceBusNamespaceName: effectiveServiceBusNamespaceName
    principalId: uami.outputs.principalId
  }
}

// ============================================================================
// 9. PROVISIONING-ARTIFACTS STORAGE (Wave G-8 Batch 2 / audit defects #3+#5)
//
//    The CI -> Worker artifact hand-off store: tasks 116/117 CI uploads
//    compiled ARM JSON manifests/templates + BFF zip-deploy artifacts +
//    solution zips into the provisioning-artifacts container (Blob Data
//    Contributor via the CI OIDC principal, when provided); the Worker's
//    H2a/H6/H9 handlers download them via the shared control-plane UAMI
//    (Blob Data Reader). Before this module existed, all three handlers
//    were a hard blocker on first live dispatch -- their *Options.Validate()
//    fails fast (NFR-05) on a missing ProvisioningArtifactsContainerUri,
//    and no storage account backed that URI anywhere in infrastructure/**.
//
//    The blobUri OUTPUT below is the exact value the Worker's three
//    *Options__ProvisioningArtifactsContainerUri app settings must carry
//    (BicepInfraDeployOptions / BffDeployOptions / SolutionImportOptions) --
//    app-setting wiring lands in modules/controlplane-worker-app-service.bicep
//    (Batch 3), consuming artifactsStorage.outputs.blobUri via a new module
//    param on the worker invocation above.
// ============================================================================

module artifactsStorage 'modules/controlplane-artifacts-storage.bicep' = {
  scope: rg
  name: 'controlplane-artifacts-storage'
  params: {
    storageAccountName: artifactsStorageAccountName
    location: location
    sku: artifactsStorageSku
    controlPlaneUamiPrincipalId: uami.outputs.principalId
    githubActionsOidcPrincipalId: githubActionsOidcPrincipalId
    tags: tags
  }
}

// ============================================================================
// 10. PLATFORM ACR (Wave G-8 Batch 2 / audit defects #4+#10)
//
//    Hosts the task 114/115 Exchange ApplicationAccessPolicy sidecar image.
//    AcrPull to the shared control-plane UAMI (the Worker sitecontainer's
//    pull identity once sidecarAuthType=UserAssigned); AcrPush to the CI
//    OIDC principal (task 115 build+push workflow), when provided. Before
//    this module existed, no Microsoft.ContainerRegistry resource existed
//    anywhere in infrastructure/** despite the sidecar CI header claiming
//    otherwise.
// ============================================================================

module acr 'modules/controlplane-acr.bicep' = {
  scope: rg
  name: 'controlplane-acr'
  params: {
    acrName: acrName
    location: location
    sku: acrSku
    controlPlaneUamiPrincipalId: uami.outputs.principalId
    githubActionsOidcPrincipalId: githubActionsOidcPrincipalId
    tags: tags
  }
}

// ============================================================================
// 11. SUBSCRIPTION-SCOPE RBAC -- Contributor for the shared control-plane
//     UAMI (Wave G-8 Batch 2 / audit defect #2)
//
//    H2a's ArmDeploymentRunner requires Contributor at subscription scope
//    for customer RG-ensure + subscription-scope ARM deployments (its own
//    error guidance, ArmDeploymentRunner.cs:162, says to verify exactly this
//    grant -- but nothing ever made it). Declared via a dedicated
//    subscription-scope module rather than inline because the role
//    assignment's guid() NAME must be calculable at deployment start and the
//    UAMI principalId is a runtime module output here (BCP120) -- inside the
//    module it is a param, which is legal. Covers the DEPLOYING subscription;
//    Model 2 stamps in foreign customer subscriptions need their own grant
//    (see module header).
// ============================================================================

module subscriptionRbac 'modules/controlplane-subscription-rbac.bicep' = {
  name: 'controlplane-subscription-rbac'
  params: {
    principalId: uami.outputs.principalId
  }
}

// ============================================================================
// OUTPUTS - Consumed by:
//   - Phase D deploy scripts (L2 app service URL + resource IDs)
//   - H4 handler (KV name + UAMI resourceId for keyVaultReferenceIdentity PATCH)
//   - H10 handler (UAMI clientId for Dataverse App User application ID)
//   - Ops scripts (Cosmos endpoint + KV URI for parameter-store lookups)
// ============================================================================

// Resource Group
output resourceGroupName string = rg.name
output location string = location
output environmentName string = environmentName

// UAMI (H4, H10, RBAC hooks)
output controlPlaneUamiId string = uami.outputs.id
output controlPlaneUamiName string = uami.outputs.name
output controlPlaneUamiPrincipalId string = uami.outputs.principalId
output controlPlaneUamiClientId string = uami.outputs.clientId

// App Service
output appServiceName string = appService.outputs.appServiceName
output appServiceId string = appService.outputs.appServiceId
output appServiceDefaultHostName string = appService.outputs.appServiceDefaultHostName
output appServiceUrl string = appService.outputs.appServiceUrl
output appServiceStagingSlotName string = appService.outputs.stagingSlotName
output appServiceStagingSlotHostName string = appService.outputs.stagingSlotDefaultHostName
output appServiceStagingSlotUrl string = appService.outputs.stagingSlotUrl
output appServicePlanId string = appServicePlan.outputs.planId

// .Worker App Service (task 101 / DS-3 Section 3) - consumed by task 102's
// dispatcher deploy target, task 110's SB Receiver RBAC, task 113's deploy
// script, and H4's post-deploy keyVaultReferenceIdentity PATCH.
output workerAppServiceName string = workerAppService.outputs.appServiceName
output workerAppServiceId string = workerAppService.outputs.appServiceId
output workerAppServiceDefaultHostName string = workerAppService.outputs.appServiceDefaultHostName
output workerAppServiceUrl string = workerAppService.outputs.appServiceUrl
output workerExchangeSidecarName string = workerAppService.outputs.sidecarName

// L2 REST API bearer audience (FR-20 acceptance)
output jwtAudience string = jwtAudience
output jwtTenantId string = effectiveJwtTenantId

// Key Vault (H4 PATCH target)
output keyVaultName string = keyVault.outputs.keyVaultName
output keyVaultUri string = keyVault.outputs.keyVaultUri
output keyVaultId string = keyVault.outputs.keyVaultId

// Cosmos DB (task 024 wiring)
output cosmosAccountName string = cosmos.outputs.accountName
output cosmosAccountId string = cosmos.outputs.accountId
output cosmosAccountEndpoint string = cosmos.outputs.accountEndpoint
output cosmosDatabaseName string = cosmos.outputs.databaseName
output cosmosRunsContainerName string = cosmos.outputs.containerName

// Monitoring
output appInsightsName string = monitoring.outputs.appInsightsName
output appInsightsConnectionString string = monitoring.outputs.connectionString
output logAnalyticsWorkspaceId string = monitoring.outputs.logAnalyticsId

// Fleet Service Bus queue (task 108) - consumed by task 110's RBAC module
// (namespace scope for role assignments) + task 113's deploy script
// (post-deploy property verification).
output fleetServiceBusNamespaceId string = fleetServiceBusQueue.outputs.namespaceId
output fleetServiceBusNamespaceName string = fleetServiceBusQueue.outputs.namespaceName
output fleetServiceBusResourceGroupName string = serviceBusResourceGroupName
output provisioningJobsQueueId string = fleetServiceBusQueue.outputs.queueId
output provisioningJobsQueueName string = fleetServiceBusQueue.outputs.queueName

// Fleet Service Bus RBAC (task 110 / DS-5 C5.5) - Data Sender + Data
// Receiver both granted to the shared control-plane UAMI at namespace
// scope. Consumed by task 113's deploy script (post-deploy `az role
// assignment list` verification).
output fleetServiceBusRbacNamespaceId string = fleetServiceBusRbac.outputs.namespaceId

// Provisioning-artifacts storage (Wave G-8 Batch 2 / audit defects #3+#5).
// artifactsBlobUri is the exact value for the Worker's three
// *Options__ProvisioningArtifactsContainerUri app settings (Batch 3 wiring)
// AND the CI upload target (tasks 116/117).
output artifactsStorageAccountName string = artifactsStorage.outputs.accountName
output artifactsStorageAccountId string = artifactsStorage.outputs.resourceId
output artifactsBlobUri string = artifactsStorage.outputs.blobUri

// Platform ACR (Wave G-8 Batch 2 / audit defects #4+#10). loginServer is the
// prefix for the real sidecar acrImageTag once task 115's CI pushes
// ({loginServer}/sprk-provisioning-sidecar:{tag}).
output acrName string = acr.outputs.acrName
output acrId string = acr.outputs.resourceId
output acrLoginServer string = acr.outputs.loginServer

// Subscription-scope Contributor for the control-plane UAMI (Wave G-8
// Batch 2 / audit defect #2) -- consumed by deploy-script post-deploy
// `az role assignment list` verification.
output subscriptionContributorRoleAssignmentName string = subscriptionRbac.outputs.contributorRoleAssignmentName
