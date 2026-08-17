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
//
// DELIBERATELY OUT OF SCOPE
//   - Service Bus:      Per ADR-036 (background-job infrastructure) the L2
//                       control-plane REUSES the environment-scope Service Bus
//                       already provisioned by env infra. This stack takes the
//                       Service Bus KV-secret NAME as a parameter and wires an
//                       @Microsoft.KeyVault reference into App Service settings;
//                       it never creates a new Service Bus namespace.
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
//     --parameters environmentName=dev serviceBusKeyVaultSecretName=servicebus-connection-string

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
