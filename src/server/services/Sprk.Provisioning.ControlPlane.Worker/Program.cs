// -----------------------------------------------------------------------------
// Program.cs — Sprk.Provisioning.ControlPlane.Worker
//
// L2 CONTROL-PLANE BACKGROUND-PROCESSING HOST.
//
// CREATED BY (task 100, DS-3 § 3 Option 2 owner-lock, 2026-08-19):
//   New composition root. Hosts the 20-handler fleet DI + reconciler +
//   crash-recovery + customer-run guard + rollback module + Cosmos +
//   Service Bus + OTel. The session-serialized Service Bus dispatcher
//   itself (ServiceBusSessionProcessor that drains the fleet-scoped queue
//   and invokes handlers by HandlerId) is authored by task 102 — this file
//   registers the surface the dispatcher will resolve against.
//
// SURFACE (minimal per POML step 3 constraint):
//   - GET /healthz — anonymous, returns 200 "ok" for App Service warm-up
//                    + operator availability probe.
//   - GET /ping    — anonymous, returns 200 "ok" (parity with .Api's
//                    smoke-test endpoint so operator runbooks work against
//                    both hosts).
//   NO Auth, NO Swagger, NO REST endpoints, NO audit middleware. The
//   Worker never receives operator-authored HTTP requests — it drains the
//   Service Bus fleet queue.
//
// HANDLER REGISTRATIONS PRESERVED (task 100 folder-move / DS-3 § 7 timing):
//   All 20 IProvisioningHandler registrations (H0, H0.5, H1, H2a, H2b, H3,
//   H4, H5, H6, H7, H8, H10, H11, H12a, H12b, H12c, H13, H14) + their
//   collaborator seams + the reconciler + crash-recovery + customer-run
//   guard + rollback module registrations moved verbatim from the pre-split
//   Sprk.Provisioning.ControlPlane/Program.cs. Every ADR-tension citation +
//   Placement Justification + § 4C rollback classification per handler was
//   preserved in-line; do NOT elide or shorten these blocks — they are the
//   code-review evidence trail for CLAUDE.md § 6.5 / § 10 / § 11 compliance.
//
// PLACEMENT: L2 is a PEER service to Sprk.Bff.Api, not a BFF extension
// (ADR-010 DI minimalism; project MUST NOT rule — no reference to
// Sprk.Bff.Api assemblies from here).
// -----------------------------------------------------------------------------

using Azure.Core;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Dispatch;
using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Sprk.Provisioning.ControlPlane.Handlers.BffDeploy;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseAppUserGraphParity;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;
using Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Handlers.RuntimeReferences;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;
using Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Registry;
using Sprk.Provisioning.ControlPlane.Rollback;
using Sprk.Provisioning.ControlPlane.Worker.Dispatch;

var builder = WebApplication.CreateBuilder(args);

// ---- Shared infrastructure (parity with .Api host) ----
// Cosmos client + IProvisioningRunRepository. Reconciler + crash-recovery
// + customer-run guard + every handler consumes this repository; without it
// the Worker composition graph is unresolvable. Same UAMI-pinned
// DefaultAzureCredential + disableLocalAuth: true wiring as .Api.
builder.Services.AddCosmosModule(builder.Configuration);

// Service Bus client + IHandlerEnqueuer over the fleet-scoped queue. The
// dispatcher (task 102) will register a ServiceBusSessionProcessor against
// this same queue; the reconciler + crash-recovery use IHandlerEnqueuer for
// re-enqueue; every handler that spawns downstream work does so through
// IHandlerEnqueuer. SessionId = CustomerId + MessageId dedup (level-1
// idempotency FR-22) are configured inside ServiceBusModule.
builder.Services.AddServiceBusModule(builder.Configuration);

// OpenTelemetry -> Azure Monitor exporter. The Worker's ILogger calls flow
// through the OTel Logs pipeline into App Insights `traces` alongside the
// .Api host's audit records (both hosts share one App Insights workspace;
// distinct `cloud_RoleName` distinguishes them). Wired behind
// AzureMonitorGuard so a deployed Worker App Service missing
// APPLICATIONINSIGHTS_CONNECTION_STRING throws at startup (NFR-05) while
// Development / Testing envs skip silently.
builder.Services.AddTelemetryModule(builder.Configuration, builder.Environment.EnvironmentName);

// ---- 20-handler fleet DI (moved from pre-split Program.cs by task 100) ----

// Task 041: Provisioning handler surface — H0 preflight handler + the four
// IPreflightQuotaProbe registrations (one per script under
// scripts/preflight/*.ps1). H0 blocks the pipeline BEFORE H1 starts on any
// insufficient headroom (spec.md FR-01 + NFR-12; design.md § 15 north-star:
// surface lead-time items UP-FRONT, not after the 30-min Bicep step). The
// dispatcher (task 102) resolves H0 by HandlerId; today they resolve via
// IProvisioningHandler for unit tests + the temporary H0-enqueues-H0.5
// bridge documented in H0PreflightHandler.
builder.Services.AddProvisioningHandlers(builder.Configuration);

// Task 042: H0.5 consent-capture handler. Task 112 (Wave G-1 C1.4) built
// the real Path X (MI-native) DataverseEnvironmentRegistryClient; task 122
// (Wave G-2, THIS registration) swaps the NullDataverseEnvironmentRegistryClient
// placeholder for it via DataverseEnvironmentRegistryModule.AddDataverseEnvironmentRegistry.
// This is a GLOBAL swap — the same IDataverseEnvironmentRegistryClient
// registration also serves H13's idempotency-short-circuit registry
// Ready-check READ (AddH13E2EAcceptanceGateHandler below); H13's WRITE path
// (IRegistrySetupStatusUpdater) is a SEPARATE seam swapped independently by
// task 184. NullDataverseEnvironmentRegistryClient is NOT deleted — it
// remains in Sprk.Provisioning.ControlPlane.Core/Registry/ as the documented
// ADR-032 P2 Null-Object shape (its own unit tests in
// DataverseEnvironmentRegistryClientTests.cs continue to exercise its
// contract) but is no longer registered anywhere in this composition root.
// Registration UNCONDITIONAL — no feature-gate branch; DS-8 mandates Path X
// be real from day one (no environment-based kill-switch for this seam).
// NFR-05: DataverseEnvironmentRegistryOptions.Validate() fails fast at boot
// if DataverseEnvironmentRegistry:AdminEnvironmentUrl is unset — see
// infrastructure/bicep/modules/controlplane-worker-app-service.bicep for the
// corresponding app-setting (added alongside this swap so a live deploy
// does not crash-loop on the newly-unconditional requirement).
// Placement Justification (CLAUDE.md §10): the handler lives in L2 (not
// BFF) per spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types
// (ADR-013 forcing-function rule — no IActionResolver, IActionRunner,
// IOpenAiClient, IPlaybookService injection).
builder.Services.AddDataverseEnvironmentRegistry(builder.Configuration);
builder.Services.AddScoped<H05ConsentCaptureHandler>();

// Task 043 / task 121: H1 subscription-readiness handler + real ARM readiness
// probe. Task 121 (Wave G-2) replaced the Wave-C4 NullSubscriptionReadinessProbe
// placeholder (which returned Passed=true unconditionally, no ARM call — DS-4
// §3's classified PLACEHOLDER) with ArmSubscriptionReadinessProbe — a real
// Azure.ResourceManager-SDK-backed impl performing two ARM calls:
//   (1) ArmClient.GetSubscriptionResource(...).GetAsync() for reachability
//       (equivalent to `az account show`).
//   (2) ArmClient.GetManagedServicesRegistrationAssignments(...).GetAllAsync()
//       for Lighthouse delegation (CustomerOwned tenancy branch only, gated
//       by the H1 handler — not this probe).
// The probe is constructed via a factory lambda that wraps the TokenCredential
// singleton already registered by AddCosmosModule (UAMI-pinned via
// ManagedIdentity:ClientId, ADR-028 MI-outbound) in a probe-local ArmClient —
// no second credential chain, no shared ArmClient DI registration (keeps this
// registration self-contained against sibling Wave-G-2 handler ports that may
// also construct their own ArmClient instances for other resource types).
// Both registrations UNCONDITIONAL per ADR-032 — no feature-gate branches.
// Placement Justification (CLAUDE.md §10): H1 lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types (ADR-013).
// H1 uses IProvisioningRunRepository (task 037) + IHandlerEnqueuer (task
// 038); no BFF-facade dependencies. Downstream H2a is owned by sibling
// task 044.
builder.Services.AddSingleton<ISubscriptionReadinessProbe>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var logger = sp.GetRequiredService<ILogger<ArmSubscriptionReadinessProbe>>();
    return new ArmSubscriptionReadinessProbe(armClient, logger);
});
builder.Services.AddScoped<H1SubscriptionReadinessHandler>();

// Task 044 / task 123: H2a Bicep infra-deploy handler + four collaborator
// seams. Task 123 (Wave G-2, Option D hybrid) replaced the three shell-out
// collaborators (ProvisionCustomerScriptBicepDeployRunner /
// AzCliArmKeyVaultRefProbe / AzCliUpgradeDriftDetector — all RETIRED, kept
// on disk unregistered per the retirement banners in their file headers)
// with pure Azure.ResourceManager SDK ports: ArmDeploymentRunner
// (SubscriptionResource.GetArmDeployments().CreateOrUpdateAsync() against
// the CI-precompiled ARM JSON artifact task 117 publishes),
// ArmKeyVaultRefProbe (WebSiteResource/WebSiteSlotResource.Data.KeyVaultReferenceIdentity),
// and ArmWhatIfDriftDetector (ArmDeploymentResource.WhatIfAsync() — typed
// WhatIfChange[] results, not stdout-parsed JSON). IBicepTemplateInspector
// (on-disk infrastructure/bicep/ structural pre-flight) is UNCHANGED —
// out of task 123's scope (it does not shell out; it reads local files
// shipped in the publish output). All registrations UNCONDITIONAL per
// ADR-032 — SignalR is the feature-gated resource, not the handler; the
// handler passes through the SignalREnabled parameter to the runner
// unconditionally (Null-Object kill-switch applies to the RESOURCE, not the
// DI branch — spec MUST rule + design.md §7.2 row 13).
//
// ArmClient + BlobContainerClient are constructed via factory lambdas that
// reuse the shared UAMI-pinned TokenCredential singleton already registered
// by AddCosmosModule (ADR-028 MI-outbound) — NO shared ArmClient/
// BlobContainerClient DI singleton registration, so this stays
// self-contained against sibling Wave-G-2 handler ports that construct
// their OWN ArmClient/Blob clients for other resource types (parity with
// task 121's ArmSubscriptionReadinessProbe registration comment above).
//
// Placement Justification (CLAUDE.md §10): H2a lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types (ADR-013
// forcing-function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H2a owns silent-fail trap T1 verification
// per POML acceptance §4B — this is the sole reason H2a exists as a handler
// wrapping the deploy (deploy alone leaves T1 as a runtime null-KV-ref
// timebomb; handler adds ARM read post-condition).
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-027 Path A: Model 1 shared-tier is documented exception —
//     TenancyModel drives stack selection (Model1Shared → stacks/model1-shared.bicep;
//     Model2Dedicated → customer.bicep). Full rationale: project spec.md § ADR Tensions.
//   - ADR-028 UAMI outbound: ArmDeploymentRunner / ArmKeyVaultRefProbe /
//     ArmWhatIfDriftDetector all use DefaultAzureCredential pinned to the L2
//     UAMI (via the shared TokenCredential singleton) — no account keys, no
//     operator `az login` chain (task 123 REMOVES the last three `az` CLI
//     shell-outs from H2a's collaborator set).
//   - §4C rollback: partial Bicep deploys are QuarantineRequired (orphaned
//     resources per design.md §4C example); §4C classification is inline in
//     H2aBicepInfraDeployHandler file header + the FailAsync helper.
builder.Services.Configure<BicepInfraDeployOptions>(
    builder.Configuration.GetSection(nameof(BicepInfraDeployOptions)));
builder.Services.PostConfigure<BicepInfraDeployOptions>(o => o.Validate());
builder.Services.AddSingleton<IBicepDeployRunner>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var options = sp.GetRequiredService<IOptions<BicepInfraDeployOptions>>();
    var artifactsContainer = new Azure.Storage.Blobs.BlobContainerClient(
        new Uri(options.Value.ProvisioningArtifactsContainerUri), credential);
    var logger = sp.GetRequiredService<ILogger<ArmDeploymentRunner>>();
    return new ArmDeploymentRunner(armClient, artifactsContainer, options, logger);
});
builder.Services.AddSingleton<IArmKeyVaultRefProbe>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var logger = sp.GetRequiredService<ILogger<ArmKeyVaultRefProbe>>();
    return new ArmKeyVaultRefProbe(armClient, logger);
});
builder.Services.AddSingleton<IUpgradeDriftDetector>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var options = sp.GetRequiredService<IOptions<BicepInfraDeployOptions>>();
    var artifactsContainer = new Azure.Storage.Blobs.BlobContainerClient(
        new Uri(options.Value.ProvisioningArtifactsContainerUri), credential);
    var logger = sp.GetRequiredService<ILogger<ArmWhatIfDriftDetector>>();
    return new ArmWhatIfDriftDetector(armClient, artifactsContainer, options, logger);
});
builder.Services.AddSingleton<IBicepTemplateInspector, FileBicepTemplateInspector>();
builder.Services.AddScoped<H2aBicepInfraDeployHandler>();

// Task 045: H2b AI Search index-provisioning handler + collaborator seams
// (ICanonicalIndexCatalog is the retired-lineage guard; IAiSearchIndexProvisioner
// = SearchIndexClientProvisioner (task 124, Wave G-2 — Azure.Search.Documents.
// Indexes.SearchIndexClient under UAMI RBAC for Model 2, REPLACING the retired
// script-shelling DeployAllIndexesScriptProvisioner); IAiSearchIndexVerifier
// calls the AI Search REST API for presence + invariants on both branches;
// ITenantFilterTemplateStore + IAiSearchTenantFilterTemplateProvisioner
// (task 124 — Cosmos-backed AiSearchTenantFilterTemplateProvisioner, REPLACING
// the wave-C4 logging-only StubAiSearchTenantFilterTemplateProvisioner) enforce
// §4D I2 / FR-29 at Model 1 onboarding for REAL. All registrations
// UNCONDITIONAL per ADR-032 — no feature-gate branches. The verifier is
// registered via AddHttpClient (typed) so DefaultAzureCredential's token
// cache is shared across handler invocations (ADR-028 UAMI-outbound MUST
// rule); SearchIndexClientProvisioner reuses the SAME shared TokenCredential
// singleton (registered by AddCosmosModule above) via constructor injection
// — zero admin-key handling anywhere in H2b's collaborator graph. The
// tenant-filter template store reuses the SAME shared CosmosClient singleton
// against a NEW, TTL-less `tenantFilterTemplates` container (see
// AiSearchTenantFilterTemplateProvisioner.cs's header for the container
// design rationale).
//
// Placement Justification (CLAUDE.md §10): H2b lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types (ADR-013
// forcing-function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H2b owns the §4D I2 (FR-29) enforcement at
// Model 1 tenant onboarding time — the per-tenant filter template is the
// PROVISIONING-time half of the tenantId eq filter invariant; the runtime
// half is enforced by BFF services + the Wave-C6 ArchTest (task 173's I2
// acceptance probe closes the loop with a live sample-query check).
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-039 (compliance path C — pivot): retired `spaarke-playbook-embeddings`
//     is rejected structurally by ICanonicalIndexCatalog.RetiredIndexNames +
//     H2b's pre-check guard. Full retired lineage per task 002 audit § 2.
//   - ADR-027 Path A: Model 1 shared-tier is documented exception —
//     TenancyModel drives branch selection (Model1Shared → verifier +
//     template; Model2Dedicated → provisioner + verifier). Full rationale:
//     project spec.md § ADR Tensions.
//   - ADR-028 UAMI outbound: REST verifier + SearchIndexClientProvisioner +
//     the Cosmos-backed template store ALL use the shared UAMI-pinned
//     TokenCredential/CosmosClient — zero admin-key, zero operator `az`
//     chain anywhere in H2b's collaborator graph (task 124, Wave G-2).
//   - §4C rollback: retired-index / provisioner-failure / invariant-violation
//     / shared-index-missing are QuarantineRequired; parameter-missing /
//     endpoint-missing / template-provisioner-failure are Resumable. Full
//     mapping inline in H2bAiSearchIndexHandler file header.
builder.Services.Configure<AiSearchIndexOptions>(
    builder.Configuration.GetSection(nameof(AiSearchIndexOptions)));
builder.Services.AddSingleton<ICanonicalIndexCatalog, CanonicalIndexCatalog>();
builder.Services.AddSingleton<IAiSearchIndexProvisioner, SearchIndexClientProvisioner>();
builder.Services.AddHttpClient<IAiSearchIndexVerifier, RestApiAiSearchIndexVerifier>();
builder.Services.AddSingleton<ITenantFilterTemplateStore>(sp => new CosmosTenantFilterTemplateStore(
    sp.GetRequiredService<Microsoft.Azure.Cosmos.CosmosClient>(),
    builder.Configuration[$"{CosmosModule.ConfigSection}:DatabaseName"] ?? CosmosModule.DefaultDatabaseName,
    sp.GetRequiredService<ILogger<CosmosTenantFilterTemplateStore>>()));
builder.Services.AddSingleton<IAiSearchTenantFilterTemplateProvisioner, AiSearchTenantFilterTemplateProvisioner>();
builder.Services.AddScoped<H2bAiSearchIndexHandler>();

// Task 046 / task 130: H3 Entra app-registration handler + two collaborator
// seams. Task 130 (Wave G-3, xhigh, Option D hybrid) REPLACED the shell-out
// scaffold (RegisterEntraAppRegScriptProvisioner + NullAdminConsentVerifier —
// both RETIRED, kept on disk unregistered per their retirement banners) with
// pure Microsoft.Graph 6.5.0 SDK ports: GraphAppRegistrationProvisioner
// (Applications/ServicePrincipals/FederatedIdentityCredentials/AddPassword —
// Model 2 ensure/create + FIC trusting the shared BFF UAMI per auth-v4 §3.1)
// and GraphAdminConsentVerifier (a REAL oauth2PermissionGrants query —
// closes DS-4 §3's "consent gate can advance on fiction" defect finding).
// Task 130 also added the Model 1 vs Model 2 tenancy-model runtime branch
// (I6-enforced, design.md §4D / spec.md FR-40 — no default/fallback) inside
// H3EntraAppRegHandler itself. All registrations UNCONDITIONAL per ADR-032 —
// no feature-gate branches.
//
// Both Graph collaborators construct a FRESH per-tenant DefaultAzureCredential
// per call (parity with H10's GraphRestAppRoleGranter — §4D I5 explicit
// per-tenant scope) rather than reusing the shared UAMI-pinned TokenCredential
// singleton (that credential is only used for GraphAppRegistrationProvisioner's
// KV writes, which target the customer's own vault under L2's own platform
// UAMI's RBAC grant — see that file's header for the full Graph-vs-KV
// credential split rationale + the 2 SDK gotchas ground-truthed via reflection
// before authoring).
//
// Placement Justification (CLAUDE.md §10): H3 lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013). H3
// uses IProvisioningRunRepository (task 037) + two dedicated seams; no BFF-
// facade dependencies. Downstream H4 (task 047, Batch 3D) reads bffAppRegId
// from interStepState AND the BFF-API-ClientId/Audience/ClientSecret KV
// references H3 now writes to RunParameters.Secrets (task 129's manifest.yaml
// reclassification of the first two entries to from-run-parameter, owner E3);
// H3 does NOT enqueue H4 directly — the reconciler owns fan-out.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-028 UAMI-outbound + KV-secret-ref: client secret stored in KV as
//     BFF-API-ClientSecret and referenced downstream as
//     @Microsoft.KeyVault(SecretUri=...) — cleartext NEVER traverses Cosmos
//     parameters/interStepState (handler leak-guard enforces); KV writes are
//     DEFERRED (PendingKvSecretWrite, in-memory only) until admin-consent is
//     verified, per DS-4 §3's binding recipe ordering.
//   - spec.md MUST rule (Dataverse S2S drop per r3 task 060): NO S2sAppRegId
//     field on EntraAppRegOutputs (compile-time guard); NO code path to
//     invoke a script that would create one.
//   - §4C rollback: provisioner failures + missing precondition are Resumable;
//     cleartext-secret-leak + S2S-forbidden + deferred-KV-commit-failure are
//     QuarantineRequired.
//   - Admin-consent WaitingOnGate is NOT a failure per design.md §4.1 H3 row —
//     envelope is processed correctly; the gate is external.
//   - CLAUDE.md §11 Path C (documented in H3EntraAppRegHandler.cs "SCOPE
//     DEVIATION" notes): H3 does NOT grant the 14 GraphAppRoles.cs app-only
//     roles (H10 already owns that, correctly targeting the UAMI SP) and does
//     NOT perform its own Dataverse-app-user assignment (H10 already performs
//     this for BOTH the BFF app-reg and the UAMI, using H3's own
//     InterStepState.BffAppRegId output).
builder.Services.Configure<EntraAppRegOptions>(
    builder.Configuration.GetSection(nameof(EntraAppRegOptions)));
builder.Services.AddSingleton<IEntraAppRegProvisioner>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var options = sp.GetRequiredService<IOptions<EntraAppRegOptions>>();
    var logger = sp.GetRequiredService<ILogger<GraphAppRegistrationProvisioner>>();
    return new GraphAppRegistrationProvisioner(credential, options, logger);
});
builder.Services.AddSingleton<IAdminConsentVerifier, GraphAdminConsentVerifier>();
builder.Services.AddScoped<H3EntraAppRegHandler>();

// Task 048 / task 140: H5 Dataverse env creation handler + 2 collaborator
// seams (IDataverseEnvCreator creates + polls via the BAP admin REST API —
// see BapRestEnvironmentCreator.cs file header for the ground-truthed
// endpoint/audience port of Provision-Customer.ps1 STEP 5/6, replacing the
// retired `pac admin create-environment` shell-out (PacAdminDataverseEnvCreator
// — kept on disk unregistered per Wave G-2/G-3 retirement convention);
// IDataverseHealthProbe polls Web API `WhoAmI` via DefaultAzureCredential
// until Reachable — implements the Pending→Verified gate for the long-
// running Dataverse env-creation flow).
builder.Services.Configure<DataverseEnvCreationOptions>(
    builder.Configuration.GetSection(nameof(DataverseEnvCreationOptions)));
// Typed HttpClient (BapRestEnvironmentCreator's public ctor takes HttpClient
// directly, matching BapRestEnvironmentRateProbe's established pattern —
// parity with the other raw-HttpClient BAP-REST collaborator in Handlers/**).
builder.Services.AddHttpClient<IDataverseEnvCreator, BapRestEnvironmentCreator>();
// NAMED HttpClient (task 103 fix): DataverseWebApiHealthProbe takes
// IHttpClientFactory + calls _httpClientFactory.CreateClient(HttpClientName)
// itself — it is NOT a typed client (no HttpClient-accepting constructor).
// The previous AddHttpClient<IDataverseHealthProbe, DataverseWebApiHealthProbe>()
// typed-client registration could never construct this type (ActivatorUtilities
// requires an HttpClient ctor param for typed clients), so IDataverseHealthProbe
// — and therefore H5DataverseEnvCreationHandler — was NOT resolvable via DI.
// HandlerRegistrationCompletenessTests (task 103) surfaced this pre-existing
// defect the first time anything actually built the real container down to H5.
builder.Services.AddHttpClient(DataverseWebApiHealthProbe.HttpClientName);
builder.Services.AddScoped<IDataverseHealthProbe, DataverseWebApiHealthProbe>();
builder.Services.AddScoped<H5DataverseEnvCreationHandler>();

// Task 047 / task 125: H4 KV secrets-population handler + FOUR collaborator
// seams. Task 125 (Wave G-2, Option D hybrid) replaced the three shell-out
// collaborators (AzCliKvSecretsWriter / AzCliAppServiceIdentityPatcher /
// AzCliSlotIdentityRoleGranter — all RETIRED, kept on disk unregistered per
// the retirement banners in their file headers) with pure Azure SDK ports:
// SecretClientKvWriter (Azure.Security.KeyVault.Secrets.SecretClient —
// SetSecretAsync/GetSecretAsync/StartDeleteSecretAsync; the "az account show"
// prerequisite probe becomes ArmClient.GetDefaultSubscriptionAsync()),
// ArmAppServiceIdentityPatcher (Azure.ResourceManager.AppService —
// WebSiteResource/WebSiteSlotResource.UpdateAsync(SitePatchInfo) on BOTH
// slots — T1 trap owner), and ArmSlotIdentityRoleGranter
// (Azure.ResourceManager.Authorization — RoleAssignmentCollection
// .CreateOrUpdateAsync against slot System-Assigned MIs — T5 interim trap
// owner). IKvSecretManifest reads task 084's real canonical secret-catalog
// manifest (task 126 C2.2 DI-swap — see the registration comment below;
// UNCHANGED by task 125 — manifest reading was always a separate seam from
// KV writing). H4 also REUSES
// IArmKeyVaultRefProbe from H2a (task 044/123) for T1 post-condition verify —
// single source of truth for the T1 trap. All registrations UNCONDITIONAL
// per ADR-032 — no feature-gate branches. The T5 granter's
// NoSlotSystemAssignedIdentity outcome is a domain SUCCESS (post-Phase-C
// UAMI-only steady state), NOT a null-object kill-switch.
//
// ArmClient instances are constructed via factory lambdas that reuse the
// shared UAMI-pinned TokenCredential singleton already registered by
// AddCosmosModule (ADR-028 MI-outbound) — NO shared ArmClient DI singleton
// registration, parity with task 121/123's registration-comment precedent.
// SecretClientKvWriter additionally takes the raw TokenCredential (SecretClient
// is constructed per-vault-per-call, matching KeyVaultCertBootstrapProbe's
// posture from task 120).
//
// spec.md MUST rule (BINDING pre-check per r3 handoff): H4
// BindingNeverDeleteSecrets = { Dataverse-ClientSecret, BFF-API-ClientSecret };
// handler refuses any manifest with a Delete op on those two names + fails
// QuarantineRequired BEFORE any external write. Full ADR-tension citations
// preserved in H4KvSecretsPopulationHandler.cs file header.
//
// FR-39 pluggability (auth-v4 MI-FIC coordination, spec.md FR-39): none of
// the three new collaborators special-case `BFF-API-ClientSecret` by name —
// every manifest entry (including that one) flows through the SAME generic
// Upsert/Delete/rotation-safe path, so the writer stays agnostic to whichever
// credential-creation path (secret vs FIC) produced the value it is asked to
// persist. Task 126 (H4 real-values correctness gate) owns the
// value-provenance branching (IKvSecretValueResolver, registered below), not
// this registration.
//
// Task 126 (Wave G-2 Batch G-2C) C2.2 manifest DI-swap: the interim 7-entry
// placeholder reader is replaced by FileKvSecretManifest (task 084's real
// 26-entry canonical manifest, embedded from scripts/canonical-secret-catalog/
// manifest.yaml). H4 handler + tests are UNCHANGED by this swap (parity with
// H1's Null-probe -> real-ARM-probe transition) — only the DI registration
// target changed.
builder.Services.Configure<KvSecretsPopulationOptions>(
    builder.Configuration.GetSection(nameof(KvSecretsPopulationOptions)));
builder.Services.AddSingleton<IKvSecretManifest, FileKvSecretManifest>();
builder.Services.AddSingleton<IKvSecretValueResolver>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    return new KvSecretValueResolver(credential);
});
builder.Services.AddSingleton<IKvSecretsWriter>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var resolver = sp.GetRequiredService<IKvSecretValueResolver>();
    var options = sp.GetRequiredService<IOptions<KvSecretsPopulationOptions>>();
    var logger = sp.GetRequiredService<ILogger<SecretClientKvWriter>>();
    return new SecretClientKvWriter(credential, armClient, resolver, options, logger);
});
builder.Services.AddSingleton<IAppServiceIdentityPatcher>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var options = sp.GetRequiredService<IOptions<KvSecretsPopulationOptions>>();
    var logger = sp.GetRequiredService<ILogger<ArmAppServiceIdentityPatcher>>();
    return new ArmAppServiceIdentityPatcher(armClient, options, logger);
});
builder.Services.AddSingleton<ISlotIdentityRoleGranter>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var logger = sp.GetRequiredService<ILogger<ArmSlotIdentityRoleGranter>>();
    return new ArmSlotIdentityRoleGranter(armClient, logger);
});
builder.Services.AddScoped<H4KvSecretsPopulationHandler>();

// Task 070: H12a AI seed chain handler + two collaborator seams
// (ISeedManifestReader = on-disk read + SHA-256 hash + defense-in-depth
// retired-artifact scan; ISeedManifestRunner = pwsh shell-out to task-069's
// scripts/seed-data/Invoke-SeedManifest.ps1 -Live). All registrations
// UNCONDITIONAL per ADR-032 — no feature-gate branches.
//
// Placement Justification (CLAUDE.md §10): H12a lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-
// function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H12a is the terminal AI-domain seeder per
// spec.md FR-15 — it wraps the task-069 PS orchestrator with Cosmos state
// management + idempotency (h12a-{customerId}-{SHA256(manifest.yaml)}) +
// defense-in-depth retired-artifact check that layers on top of the
// orchestrator's own retiredArtifacts enforcement.
builder.Services.Configure<AiSeedChainOptions>(
    builder.Configuration.GetSection(nameof(AiSeedChainOptions)));
builder.Services.AddSingleton<ISeedManifestReader, FileSeedManifestReader>();
builder.Services.AddSingleton<ISeedManifestRunner, InvokeSeedManifestScriptRunner>();
builder.Services.AddScoped<H12aAiSeedChainHandler>();

// Task 071: H12b app-config seed handler + four IAppConfigSeeder registrations.
// Single AddH12bAppConfigSeedHandler() extension method replaces 6 raw
// registrations (ADR-010 god-class-ratchet). DAG-parallel with H12a; no
// cross-dependency; both handlers can fire post-H7.
builder.Services.AddH12bAppConfigSeedHandler(builder.Configuration);

// Task 049: H6 Package Deployer solution-import handler + 3 collaborator seams
// (ISolutionCatalog = C#-side mirror of Deploy-DataverseSolutions.ps1's
// $SolutionImportOrder per task 008 R5 binding; ISolutionImporter shells out
// to the wave-0 hardened Deploy-DataverseSolutions.ps1 for the 8 authoritative
// solutions per §11.1a; ISolutionVerifier shells out to `pac solution list`
// post-import to build the Cosmos interStepState.ImportedSolutions manifest
// with per-solution version + solutionId).
builder.Services.Configure<SolutionImportOptions>(
    builder.Configuration.GetSection(nameof(SolutionImportOptions)));
builder.Services.AddSingleton<ISolutionCatalog, CanonicalSolutionCatalog>();
builder.Services.AddSingleton<ISolutionImporter, DeployDataverseSolutionsScriptImporter>();
builder.Services.AddSingleton<ISolutionVerifier, PacCliSolutionVerifier>();
builder.Services.AddScoped<H6SolutionImportHandler>();

// Task 050: H7 Dataverse env-var values handler + 1 collaborator seam
// (IEnvVarValuesWriter issues direct Dataverse Web API REST calls — find
// environmentvariabledefinition by schema name, then PATCH-if-exists /
// POST-if-not on environmentvariablevalues — replicating
// scripts/Provision-Customer.ps1 Step 8's sequence in C#). Registration is
// UNCONDITIONAL per ADR-032 — no feature-gate branches. Auth is confidential-
// client (BFF app-reg client-credentials via Azure.Identity.ClientSecretCredential),
// the SAME identity + pattern H6 uses — the MI-Dataverse App User (H10) has
// not yet been created at H7's point in the DAG (H10 runs AFTER H7 per
// design.md §4.1: "H5 → H6 (solutions) → H7 → H10 (app-user) → H11").
//
// NAMED HttpClient (task 103 fix, applied 2026-08-19 per g1-task-103 report):
// DataverseWebApiEnvVarValuesWriter takes IHttpClientFactory (not HttpClient)
// and resolves its own named client via DataverseWebApiEnvVarValuesWriter.HttpClientName
// — parity with DataverseWebApiHealthProbe's (H5) identical fix immediately
// above. The previous AddHttpClient<IEnvVarValuesWriter, DataverseWebApiEnvVarValuesWriter>()
// typed-client registration failed DI resolution at runtime (Microsoft.Extensions.Http
// requires an HttpClient ctor param for typed clients), so H7DataverseEnvVarValuesHandler
// was NOT actually resolvable — surfaced by task 103's HandlerRegistrationCompletenessTests.
//
// Task 142 (Wave G-4): EnvVarValuesOptions.ClientSecret is now UNCONDITIONALLY
// wired via modules/controlplane-worker-app-service.bicep's EnvVarValues__ClientSecret
// KV-reference app setting (sourced from the platform KV's canonical
// BFF-API-ClientSecret, the shared multitenant BFF app-reg secret — same
// identity H7 authenticates to customer Dataverse envs with). AddOptions +
// Validate + ValidateOnStart fails fast at Worker boot (NFR-05 parity with
// DataverseEnvironmentRegistryModule.AddDataverseEnvironmentRegistry, task 122)
// if a deployed Worker is missing this setting — replaces the plain
// Configure<T>() call so a config-gap fails loud at startup instead of only
// surfacing on H7's first dispatch (the handler's own runtime
// MissingClientSecret guard stays as defense-in-depth).
builder.Services.AddOptions<EnvVarValuesOptions>()
    .Bind(builder.Configuration.GetSection(EnvVarValuesOptions.SectionName))
    .Validate(o =>
    {
        o.Validate();
        return true;
    }, "EnvVarValues options failed validation — see inner exception (Validate throws).")
    .ValidateOnStart();
builder.Services.AddHttpClient(DataverseWebApiEnvVarValuesWriter.HttpClientName);
builder.Services.AddScoped<IEnvVarValuesWriter, DataverseWebApiEnvVarValuesWriter>();
builder.Services.AddScoped<H7DataverseEnvVarValuesHandler>();

// Task 051 (Batch 3E) -> task 131 (Wave G-3) Graph SDK port: H8 SPE
// container-type + root-container handler + THREE collaborator seams, now
// Microsoft.Graph 6.5.0 under ClientCertificateCredential (T6) instead of the
// retired shell-out scripts (CreateNewContainerTypeScriptProvisioner.cs /
// SpeContainerAppOnlyVerifier.cs / AzCliSpeContainerIdKvWriter.cs — kept on
// disk, UNREGISTERED, per this project's retirement pattern):
//   - ISpeContainerTypeProvisioner -> GraphContainerTypeProvisioner: POST
//     /storage/fileStorage/containerTypes (v1.0 GA) + POST
//     /storage/fileStorage/containerTypeRegistrations (owning-app FULL
//     permission grant — replaces the retired script's separate SharePoint
//     REST applicationPermissions PUT under a different token audience) +
//     POST /storage/fileStorage/containers (root container).
//   - ISpeContainerVerifier -> GraphAppOnlyContainerVerifier: single GET
//     /storage/fileStorage/containers/{id} — dramatically simplified vs the
//     retired script's "123 lines of token ceremony around ONE GET"
//     (Azure.Identity.ClientCertificateCredential owns the JWT client-
//     assertion ceremony the script hand-rolled). Also owns the NEW 24h
//     SPE-replication-lag classification (404 -> ReplicationPending ->
//     handler sets RunStatus.WaitingOnGate, never Resumable/QuarantineRequired
//     — DS-4 §2 / this project's CLAUDE.md MUST rules).
//   - ISpeContainerIdKvWriter -> SecretClientSpeContainerIdKvWriter: reuses
//     task 125's SecretClient idiom (single-secret, narrower than H4's
//     manifest-driven writer — see that file's header for the justification).
// Both Graph collaborators load the T6 cert from KV via SecretClient (NOT
// CertificateClient — see SpeConfidentialClientGraphFactory.cs's header for
// why: the private key is only obtainable via the paired Secret, never via
// CertificateClient's public-cert-only DownloadCertificateAsync).
//
// spec.md MUST rule (T6, FR-33): confidential-client (app-only) cert-based
// token is the ONLY auth path (ClientCertificateCredential, NEVER
// ClientSecretCredential) — enforced in BOTH the provisioner (creation) and
// the verifier (post-condition GET), each independently detecting a
// delegated-token trap signature ("public client not allowed") and
// classifying QuarantineRequired + TrapT6DelegatedTokenDetected rather than a
// routine Resumable failure.
builder.Services.Configure<SpeContainerTypeOptions>(
    builder.Configuration.GetSection(nameof(SpeContainerTypeOptions)));
builder.Services.AddSingleton<ISpeContainerTypeProvisioner, GraphContainerTypeProvisioner>();
builder.Services.AddSingleton<ISpeContainerVerifier, GraphAppOnlyContainerVerifier>();
builder.Services.AddSingleton<ISpeContainerIdKvWriter, SecretClientSpeContainerIdKvWriter>();
builder.Services.AddScoped<H8SpeContainerTypeHandler>();

// Task 053 (Batch 3E): H10 Dataverse App User + Graph app-role parity handler
// (T2 + T3 silent-fail trap owner) + FIVE collaborator seams
// (IGraphAppRolesRegistry = L2GraphAppRolesRegistry, a compiled mirror of
// Sprk.Bff.Api.Infrastructure.Auth.GraphAppRoles — L2 cannot reference the BFF
// assembly per ADR-010 / project MUST rule, so the 14-role catalog is
// duplicated as its own DI-registered source of truth; IDataverseAppUserCreator
// = DataverseWebApiAppUserCreator issues real Dataverse Web API systemusers
// upsert + role-association calls for BOTH the BFF app-reg and the UAMI;
// IDataverseAppUserVerifier = DataverseWebApiAppUserVerifier is the INDEPENDENT
// T2 post-registration re-query; IGraphAppRoleGranter = GraphRestAppRoleGranter
// grants the 14 roles onto the UAMI SP via raw Graph REST calls;
// IGraphAppRoleParityVerifier = GraphRestAppRoleParityVerifier is the INDEPENDENT
// T3 post-grant re-query).
builder.Services.Configure<H10DataverseAppUserGraphParityOptions>(
    builder.Configuration.GetSection(nameof(H10DataverseAppUserGraphParityOptions)));
builder.Services.AddSingleton<IGraphAppRolesRegistry, L2GraphAppRolesRegistry>();
builder.Services.AddHttpClient<IDataverseAppUserCreator, DataverseWebApiAppUserCreator>();
builder.Services.AddHttpClient<IDataverseAppUserVerifier, DataverseWebApiAppUserVerifier>();
builder.Services.AddHttpClient<IGraphAppRoleGranter, GraphRestAppRoleGranter>();
builder.Services.AddHttpClient<IGraphAppRoleParityVerifier, GraphRestAppRoleParityVerifier>();
builder.Services.AddScoped<H10DataverseAppUserGraphParityHandler>();

// Task 054 (Batch 3F): H11 user-provisioning handler (D6 identity-preset
// branch) + THREE collaborator seams (IGraphUserProvisioner =
// GraphRestUserProvisioner issues real Graph REST /users + assignLicense
// calls — NativeAccount branch; IB2BInvitationClient =
// GraphRestB2BInvitationClient issues real Graph REST /invitations calls —
// B2BGuest branch; IB2BConsentVerifier = GraphRestB2BConsentVerifier is the
// consent-verification gate — independent GET /users/{id}?$select=
// externalUserState re-query per invited guest, parity with H3's
// IAdminConsentVerifier gate shape).
builder.Services.Configure<H11UserProvisioningOptions>(
    builder.Configuration.GetSection(nameof(H11UserProvisioningOptions)));
builder.Services.AddHttpClient<IGraphUserProvisioner, GraphRestUserProvisioner>();
builder.Services.AddHttpClient<IB2BInvitationClient, GraphRestB2BInvitationClient>();
builder.Services.AddHttpClient<IB2BConsentVerifier, GraphRestB2BConsentVerifier>();
builder.Services.AddScoped<H11UserProvisioningHandler>();

// Task 072 (Batch 3F): H12c runtime references handler + ONE collaborator
// seam (IModelDeploymentReferenceWriter = DataverseWebApiModelDeployment
// ReferenceWriter issues real Dataverse Web API upserts against
// sprk_aimodeldeployment — find-by-sprk_name, PATCH if found, POST if not).
// H12c is the DAG-join point requiring BOTH H12a (task 070) + H12b (task
// 071) complete before it dispatches (design.md §4.1 DAG: "H12c — needs both
// H12a + H12b + H2a OpenAI"). Registered via a single AddH12cRuntimeReferences
// Handler() extension method (RuntimeReferencesModule.cs) — parity with
// H12b's AddH12bAppConfigSeedHandler() god-class-ratchet pattern.
builder.Services.AddH12cRuntimeReferencesHandler(builder.Configuration);

// Task 073 (Batch 3F): H14 post-deploy integration wiring handler (parent) +
// its 3 DAG-parallel sub-handlers (H14a Exchange ApplicationAccessPolicy —
// T4 silent-fail trap owner; H14b Graph webhook subscriptions; H14c Dataverse
// service-endpoint webhook) + FOUR collaborator seams. Registered via a
// single AddH14IntegrationWiringHandler() extension method
// (IntegrationWiringModule.cs) — parity with H12b/H12c's god-class-ratchet
// pattern.
builder.Services.AddH14IntegrationWiringHandler(builder.Configuration);

// Task 052 (Batch 4B) / task 132 (Wave G-3, Option D hybrid, DS-4 §5
// re-scope): H9 BFF-deploy handler + SIX collaborator seams. Task 132
// replaced the two shell-out collaborators (DotnetR3GateVerifier /
// DeployBffApiScriptRunner — both RETIRED, kept on disk unregistered per
// their retirement banners) AND the ARM-adjacent-but-CLI AzCliAppServiceSlotSwapper
// (also RETIRED) with pure SDK/REST ports: IArtifactManifestVerifier =
// ArtifactManifestVerifier (pure C# metadata check — downloads + parses
// task 116's latest.json manifest via a shared BlobContainerClient; hard-
// blocks on missing/red gates — the r3-era gates now run in CI, not here),
// IBffArtifactDownloader = BlobArtifactDownloader (Azure.Storage.Blobs
// BlobClient.DownloadToAsync — UAMI RBAC, no stored key), IKuduZipDeployer =
// KuduZipDeployer (typed HttpClient POST to the Kudu SCM zip-deploy route —
// no ARM SDK zip-deploy primitive exists; MI-acquired ARM-scope bearer
// token), and IAppServiceSlotSwapper = ArmSlotSwapper
// (Azure.ResourceManager.AppService WebSiteSlotResource.SwapSlotAsync — a
// proper awaited LRO, replacing the CLI's fire-and-parse). IHealthProbe =
// HttpHealthProbe (UNCHANGED — already real, reused unmodified per DS-4 §5
// item 3) issues HttpClient GETs against BFF /healthz with retry parity to
// Deploy-BffApi.ps1's Test-HealthCheck; IBffPublishSizeReporter =
// FileBffPublishSizeReporter (UNCHANGED) measures the DOWNLOADED artifact
// zip + computes NFR-01 delta vs the configured baseline.
//
// ArmClient + BlobContainerClient are constructed via factory lambdas that
// reuse the shared UAMI-pinned TokenCredential singleton (ADR-028
// MI-outbound) — parity with task 123's H2a ArmDeploymentRunner registration
// comment above (self-contained against sibling handler ports).
//
// LIVE-CEREMONY DEPENDENCY: BffDeployOptions:ProvisioningArtifactsContainerUri
// points at the SAME `provisioning-artifacts` storage account task 116/117
// publish to, which does NOT YET EXIST (Wave G-1 live-ceremony backlog item
// #4, project current-task.md). PostConfigure.Validate() fails fast at boot
// if the app-setting is blank — this handler is fully buildable/unit-testable
// today; a real end-to-end run additionally requires that live-ceremony item.
builder.Services.Configure<BffDeployOptions>(
    builder.Configuration.GetSection(nameof(BffDeployOptions)));
builder.Services.PostConfigure<BffDeployOptions>(o => o.Validate());
builder.Services.AddSingleton<IArtifactManifestVerifier>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var options = sp.GetRequiredService<IOptions<BffDeployOptions>>();
    var artifactsContainer = new Azure.Storage.Blobs.BlobContainerClient(
        new Uri(options.Value.ProvisioningArtifactsContainerUri), credential);
    var logger = sp.GetRequiredService<ILogger<ArtifactManifestVerifier>>();
    return new ArtifactManifestVerifier(artifactsContainer, options, logger);
});
builder.Services.AddSingleton<IBffArtifactDownloader>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var options = sp.GetRequiredService<IOptions<BffDeployOptions>>();
    var artifactsContainer = new Azure.Storage.Blobs.BlobContainerClient(
        new Uri(options.Value.ProvisioningArtifactsContainerUri), credential);
    var logger = sp.GetRequiredService<ILogger<BlobArtifactDownloader>>();
    return new BlobArtifactDownloader(artifactsContainer, options, logger);
});
builder.Services.AddHttpClient<IKuduZipDeployer, KuduZipDeployer>();
builder.Services.AddSingleton<IAppServiceSlotSwapper>(sp =>
{
    var credential = sp.GetRequiredService<TokenCredential>();
    var armClient = new Azure.ResourceManager.ArmClient(credential);
    var options = sp.GetRequiredService<IOptions<BffDeployOptions>>();
    var logger = sp.GetRequiredService<ILogger<ArmSlotSwapper>>();
    return new ArmSlotSwapper(armClient, options, logger);
});
builder.Services.AddHttpClient<IHealthProbe, HttpHealthProbe>();
builder.Services.AddSingleton<IBffPublishSizeReporter, FileBffPublishSizeReporter>();
builder.Services.AddScoped<H9BffDeployHandler>();

// Task 055 (Batch 4E): H13 E2E acceptance-gate handler + SIX collaborator seams
// (IE2EValidationRunner = ValidateDeployedEnvironmentScriptRunner wraps the
// Phase-B extended scripts/Validate-DeployedEnvironment.ps1 for SC #5 sample
// checks; IE2ETrapVerifier = PlaceholderTrapVerifier — Wave-C4 stub that
// returns InfraFault for every T1–T6; IE2EInvariantVerifier =
// PlaceholderInvariantVerifier — parity stub for I1–I5; INamingConformanceChecker
// = NamingConformanceScriptRunner shells out to r3 task 063's
// scripts/naming-conformance-check.ps1; ICostEnvelopeChecker =
// AzCliCostEnvelopeChecker shells out to `az costmanagement query` per
// subscription + compares against §15 #14 envelopes;
// IRegistrySetupStatusUpdater = DataverseRegistrySetupStatusUpdater is a
// Wave-C4 placeholder returning Success WITHOUT a real Dataverse write —
// Wave-C5 swaps for a real Web API PATCH once the L2 Dataverse client
// wiring lands). H13 also REUSES IDataverseEnvironmentRegistryClient
// (task 042) for the idempotency-short-circuit registry Ready-check lookup.
builder.Services.AddH13E2EAcceptanceGateHandler(builder.Configuration);

// ---- Reconciler + crash-recovery + concurrency + rollback (Wave C5) ----

// Task 058 (Wave C5): state-reconciler BackgroundService — polls Cosmos every
// 5s (configurable via ReconcilerOptions.PollInterval) for runs with status ∈
// {Running, WaitingOnGate}, computes DAG advancement per design.md §4.1
// handler dependencies via IDagAdvancer, and enqueues each ready-to-dispatch
// handler via IHandlerEnqueuer (task 038 Service Bus wire). Registers:
//   - ReconcilerOptions (bound + validated; PollInterval >= 1s enforced)
//   - TimeProvider.System (once, via TryAddSingleton — production clock)
//   - IDagAdvancer -> DagAdvancer (Singleton, pure function)
//   - IActiveRunScanner -> CosmosActiveRunScanner (Scoped)
//   - IHandlerOutcomeApplier -> HandlerOutcomeApplier (Scoped — task 104,
//     Phase C'' Wave G-1: extracted from StateReconcilerService's own
//     ApplyHandlerOutcomeAsync per DS-2 §5 / gap C2.1. This is the SAME
//     registration task 102's ProvisioningHandlerDispatcher resolves
//     IHandlerOutcomeApplier from — no separate Worker-level DI line needed;
//     AddReconcilerModule below is this composition root's single source for
//     the seam per ADR-010 DI minimalism.)
//   - StateReconcilerService (HostedService)
//
// ADR-004 (Path A at L2 scope per spec.md ADR Tensions row 1): the state-
// reconciler is orchestration infrastructure, NOT itself an IJobHandler.
// Custom state machine + Cosmos-backed run doc + Service Bus enqueue is
// the documented L2 execution model per design.md §4.2 (Fable M-9 resolution).
//
// §4D I3 waiver: CosmosActiveRunScanner.QueryActiveRunsAsync is annotated
// with [AllowCrossPartitionScan] — the ONE deliberate cross-partition read
// in L2, per design intent.
builder.Services.AddReconcilerModule(builder.Configuration);

// Task 059 (Wave C5): I5 same-customer concurrency guard — optimistic upsert
// of sprk_dataverseenvironment.sprk_currentrunid (null -> newRunId). Registered
// via extension method (parity with ReconcilerModule so Program.cs stays
// god-class-ratchet-clean per ADR-010). All registrations UNCONDITIONAL per
// ADR-032; the kill-switch lives on CustomerRunGuardOptions.Enabled.
//
// spec.md §4D I5 / FR-23 / FR-32: same-customer serialization via
// optimistic upsert; cross-customer runs unaffected.
builder.Services.AddCustomerRunGuard(builder.Configuration);

// Task 060 (Wave C5): I6 crash-recovery startup scan — CrashRecoveryStartupService
// is an IHostedService that on Worker boot scans Cosmos (via task 058's shared
// IActiveRunScanner) for status ∈ {Running, WaitingOnGate} runs whose last-
// activity age exceeds MAX(2× CrashRecovery:MedianHandlerDuration,
// CrashRecovery:FloorAge) and re-enqueues run.CurrentPhase via the SAME
// IHandlerEnqueuer (task 038) the reconciler uses. Reuses TimeProvider.System
// already registered by AddReconcilerModule (TryAddSingleton) so this does
// NOT double-register the production clock.
builder.Services.Configure<CrashRecoveryOptions>(
    builder.Configuration.GetSection(CrashRecoveryOptions.SectionName));
builder.Services.PostConfigure<CrashRecoveryOptions>(o => o.Validate());
builder.Services.AddHostedService<CrashRecoveryStartupService>();

// Task 061 (Wave C5): §4C rollback surface — IFailureClassifier + IQuarantineClearService.
// Provides the exhaustive FailureClass -> RunStatus mapping (RollbackTransitions)
// + the Quarantined -> Failed transition invoked by the .Api's
// POST /api/runs/{id}/clear-quarantine endpoint (spec FR-24). Even though
// the CLEAR endpoint lives on .Api, the service that mutates Cosmos on
// clear-quarantine lives in Core and is resolved via the .Api's DI container
// per-request. Registering the rollback module in the Worker mirrors the
// pre-split composition (Wave-C5 assumed one process); if a future task
// separates the clear-quarantine cosmos-write path into the Worker only,
// the .Api can drop this registration. All registrations UNCONDITIONAL per
// ADR-032 — no feature-gate branches; §4C is domain-authoritative (not a
// feature flag).
builder.Services.AddRollbackModule();

// Task 102 (Phase C'' Wave G-1): THE load-bearing execution engine --
// ProvisioningHandlerDispatcher BackgroundService (ServiceBusSessionProcessor
// against sprk-provisioning-jobs). Mirror-and-diverge of the BFF's
// ServiceBusJobProcessor per DS-2 §1.5 (session-aware; keyed-DI resolution;
// 65-min lock renewal; §4C retry authority via IHandlerOutcomeApplier instead
// of SB Abandon-loop). Resolves handlers via GetKeyedService<IProvisioningHandler>
// (task 103), applies outcomes via IHandlerOutcomeApplier (task 104), and
// gates the dequeue path via IDispatchIdempotencyService (Level 2 -- NoOp
// placeholder today, Redis-backed impl in task 105). The
// AddHostedService<ProvisioningHandlerDispatcher> line is registered
// DIRECTLY here (not inside AddDispatchModule) because the dispatcher lives
// in the .Worker project and .Core's DispatchModule extension cannot
// reference a .Worker type -- exact parity with CrashRecoveryStartupService's
// two-line registration above.
//
// spec.md MUST rule (DS-2b R3 forcing function):
// ServiceBusSessionProcessorOptions.MaxConcurrentCallsPerSession is HARD-CODED
// to 1 inside the dispatcher (not surfaced on DispatcherOptions), and
// ProvisioningHandlerDispatcherInvariantTests protects the invariant from
// config drift + a static contract test asserts .Core + .Worker have ZERO
// compile references to Sprk.Bff.Api's IJobHandler.
//
// Placement Justification (CLAUDE.md §10 / §11):
//   Existing -- BFF's ServiceBusJobProcessor is BFF-scoped (ADR-010 forbids
//     L2 handlers registering in BFF) and NOT session-aware.
//   Extension -- cannot extend a non-session processor into a session
//     processor without rewriting the core loop; DS-2 §1.5 documents every
//     divergence explicitly.
//   Cost-of-doing-nothing -- without this class, zero handlers execute
//     end-to-end (THE load-bearing gap FR-18/SC#5 identifies + the current-
//     state finding of the 2026-08-18 Fable gap analysis).
//   Owner sign-off (DS-2 §7.1 + DS-2b §9): session-serialized execution
//     adopted as designed.
builder.Services.AddDispatchModule(builder.Configuration, builder.Environment);
builder.Services.AddHostedService<ProvisioningHandlerDispatcher>();

var app = builder.Build();

// ---- Middleware pipeline (Worker: no auth, no swagger, no audit) ----
// The Worker never receives operator-authored HTTP requests. Its only HTTP
// surface is the /healthz + /ping smoke tests below. Adding Authentication /
// Authorization / Audit middleware here would be surface expansion without
// justification (CLAUDE.md §11); explicitly omitted.

// ---- Endpoints ----
// Only anonymous /healthz + /ping per POML step 3 constraint. NO Map*Endpoints
// call — HealthEndpoints.MapHealthEndpoints in .Api is the API host's
// smoke-test route; the Worker inlines minimal probes to keep this host free
// of any dependency on .Api-side extension methods.
app.MapGet("/healthz", () => Results.Text("ok", contentType: "text/plain"))
    .AllowAnonymous()
    .WithName("WorkerHealthz")
    .WithTags("Health");

app.MapGet("/ping", () => Results.Text("ok", contentType: "text/plain"))
    .AllowAnonymous()
    .WithName("WorkerPing")
    .WithTags("Health");

app.Run();

// Expose Program for WebApplicationFactory-based tests if a Worker-side
// integration test is added in a future task (parity with .Api's Program
// exposure). No test in the current 41-file suite consumes this yet.
public partial class Program
{
}
