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
//                     audience api://spaarke-provisioning-controlplane-{env};
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
//     --parameters environmentName=dev serviceBusKeyVaultSecretName=servicebus-connection-string \
//                  serviceBusNamespaceName=spaarke-servicebus-dev serviceBusResourceGroupName=SharePointEmbedded

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

@description('Name of the Key Vault secret holding the Service Bus connection string for the fleet-scoped SB namespace (per ADR-036 reuse - no new SB is created here). The App Service resolves this via @Microsoft.KeyVault reference at runtime.')
param serviceBusKeyVaultSecretName string = 'servicebus-connection-string'

@description('Name of the fleet-scoped Service Bus namespace that hosts the sprk-provisioning-jobs queue (task 108 / DS-5 C5.4). Empty defaults to spaarke-servicebus-{environmentName} (the legacy-but-canonical name per AZURE-RESOURCE-NAMING-CONVENTION.md; verified live value for dev is spaarke-servicebus-dev). This stack does NOT create the namespace - it only declares the queue as its child via an `existing` reference.')
param serviceBusNamespaceName string = ''

@description('Name of the resource group that hosts the fleet-scoped Service Bus namespace (task 108 / DS-5 C5.4). Defaults to SharePointEmbedded - the verified live dev value; this is a legacy pre-per-env-model resource group name, NOT the canonical rg-spaarke-{env} shape, so staging/prod deploys MUST override this parameter once the shared Service Bus resource group name for those environments is known.')
param serviceBusResourceGroupName string = 'SharePointEmbedded'

@description('Name of the Key Vault secret holding the Dataverse App User (Spaarke S2S) client secret used by handlers H5/H6/H10 to write registry rows. Resolved via @Microsoft.KeyVault reference in appSettings.')
param dataverseClientSecretName string = 'Dataverse-ClientSecret'

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

// App Service NAME must match the FR-20 audience shape - the resource name is
// the deterministic default hostname; keeping it aligned with the audience
// (api://spaarke-provisioning-controlplane-{env}) reduces operator cognitive
// load + makes tenant-config sanity checks trivial.
var appServiceName = 'spaarke-provisioning-controlplane-${environmentName}'

// FR-20 acceptance: audience MUST be api://spaarke-provisioning-controlplane-{env}.
// The `api://` scheme is Azure AD's canonical audience URI form.
var jwtAudience = 'api://spaarke-provisioning-controlplane-${environmentName}'

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
    keyVaultName: keyVault.outputs.keyVaultName
    serviceBusKeyVaultSecretName: serviceBusKeyVaultSecretName
    dataverseClientSecretName: dataverseClientSecretName
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
// (namespace/queue scope for role assignments) + task 113's deploy script
// (post-deploy property verification).
output fleetServiceBusNamespaceId string = fleetServiceBusQueue.outputs.namespaceId
output fleetServiceBusNamespaceName string = fleetServiceBusQueue.outputs.namespaceName
output fleetServiceBusResourceGroupName string = serviceBusResourceGroupName
output provisioningJobsQueueId string = fleetServiceBusQueue.outputs.queueId
output provisioningJobsQueueName string = fleetServiceBusQueue.outputs.queueName
