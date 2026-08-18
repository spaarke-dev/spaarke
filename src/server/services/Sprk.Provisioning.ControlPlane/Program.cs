// -----------------------------------------------------------------------------
// Program.cs
//
// L2 CONTROL-PLANE ENTRY POINT (Sprk.Provisioning.ControlPlane).
//
// Composition (customer-provisioning-orchestration-r1):
//   - Minimal API WebApplication builder.
//   - Module composition (AddAuthModule + AddSwaggerModule + AddCosmosModule
//     + AddServiceBusModule + AddTelemetryModule) — extension methods per
//     feature area to avoid god-class DI (parity with the BFF *Module.cs
//     pattern; respects NFR-07 god-class ratchet).
//   - Auth pipeline: UseAuthentication() → UseMiddleware<AuditLogMiddleware>()
//     → UseAuthorization(). AuditLogMiddleware wraps downstream so
//     Authorization short-circuits (401/403) still audit-log.
//   - Swagger UI at /swagger.
//   - Health endpoints: GET /ping (anon, 200 "ok") + POST /api/runs
//     placeholder (Operator policy required; returns 501 Not Implemented).
//
// WAVE-BY-WAVE LAYERING (do NOT wire beyond your task's scope):
//   - Task 036 (scaffold):    Auth + Swagger + Health placeholder.
//   - Task 037:               Cosmos client + IProvisioningRunRepository
//                             over `spaarke-provisioning/runs` container
//                             (partition /customerId; ETag concurrency).
//   - Task 038:               Service Bus client + IHandlerEnqueuer
//                             (fleet-scoped queue; DefaultAzureCredential;
//                             deterministic MessageId for FR-22 level-1
//                             idempotency; SessionId = CustomerId).
//   - Task 039:               OpenTelemetry -> Azure Monitor exporter behind
//                             AzureMonitorGuard + AuditLogMiddleware
//                             (NFR-11 auditable operator action; every
//                             mutating endpoint audit-logs actor tid/oid/roles
//                             + method/path/status/traceId via ILogger which
//                             flows through OTel Logs into App Insights
//                             `traces`). Placed AFTER UseAuthentication (so
//                             claims are populated) and BEFORE UseAuthorization
//                             (so 401/403 short-circuits are captured per POML
//                             acceptance #4).
//   - Task 041 (this task):   H0 preflight handler + four preflight quota
//                             probes (Azure OpenAI TPM, Dataverse env-rate,
//                             subscription vCPU, SPE cert-bootstrap) wired
//                             behind IProvisioningHandler in DI via
//                             AddProvisioningHandlers. First handler in the
//                             wave C4 catalog (spec.md FR-01 + NFR-12).
//   - Wave C5:                Real POST /api/runs handler (replaces the 501
//                             placeholder) + reconciler background service
//                             (consumes IHandlerEnqueuer).
//
// PLACEMENT: L2 is a PEER service to Sprk.Bff.Api, not a BFF extension
// (ADR-010 DI minimalism; project MUST NOT rule — no reference to
// Sprk.Bff.Api assemblies from here).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Api;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Endpoints;
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
using Sprk.Provisioning.ControlPlane.Middleware;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Reconciler;
using Sprk.Provisioning.ControlPlane.Registry;
using Sprk.Provisioning.ControlPlane.Rollback;

var builder = WebApplication.CreateBuilder(args);

// ---- Services registration (module composition per ADR-010) ----
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddSwaggerModule();
// Task 037: Cosmos client (UAMI-pinned DefaultAzureCredential; account
// `disableLocalAuth: true`) + IProvisioningRunRepository over the
// `spaarke-provisioning/runs` container. Every read/write includes the
// /customerId partition key by construction (§4D I3 / FR-30); replace
// uses ETag optimistic concurrency (FR-23 I5).
builder.Services.AddCosmosModule(builder.Configuration);
// Task 038: Service Bus client + IHandlerEnqueuer over the fleet-scoped
// queue the BFF's ServiceBusJobProcessor drains. Deterministic MessageId
// = SHA256(HandlerId|RunId|CustomerId|paramHash) implements FR-22 level-1
// idempotency; SessionId = CustomerId enables per-customer FIFO ordering
// (§4D I5). DefaultAzureCredential (UAMI) — never account-key per ADR-028.
builder.Services.AddServiceBusModule(builder.Configuration);
// Task 039: OpenTelemetry -> Azure Monitor exporter, wired behind
// AzureMonitorGuard so a deployed L2 App Service missing
// APPLICATIONINSIGHTS_CONNECTION_STRING throws at startup (NFR-05) while
// Development / Testing envs skip silently. Without this, AuditLogMiddleware's
// structured-log emissions never reach App Insights and NFR-11's audit trail
// is dead — invisible failure. Feeds the OTel Logs pipeline; the audit
// records land in Azure Monitor `traces` with structured properties in
// customDimensions (Kusto: `traces | where message startswith "AuditableAction"`).
builder.Services.AddTelemetryModule(builder.Configuration, builder.Environment.EnvironmentName);
// Task 041: Provisioning handler surface — H0 preflight handler + the four
// IPreflightQuotaProbe registrations (one per script under
// scripts/preflight/*.ps1). H0 blocks the pipeline BEFORE H1 starts on any
// insufficient headroom (spec.md FR-01 + NFR-12; design.md § 15 north-star:
// surface lead-time items UP-FRONT, not after the 30-min Bicep step). Wave
// C5 adds the reconciler background service that dispatches these handlers
// off the Service Bus queue; today they resolve via IProvisioningHandler
// for unit tests + the temporary H0-enqueues-H0.5 bridge documented in
// H0PreflightHandler.
builder.Services.AddProvisioningHandlers(builder.Configuration);

// Task 042: H0.5 consent-capture handler + registry lookup placeholder.
// Wave C5 replaces NullDataverseEnvironmentRegistryClient with a real
// Dataverse-backed impl once the L2 Dataverse client wiring lands.
// Both registrations UNCONDITIONAL per ADR-032 — no feature-gate branches.
// Placement Justification (CLAUDE.md §10): the handler lives in L2 (not
// BFF) per spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types
// (ADR-013 forcing-function rule — no IActionResolver, IActionRunner,
// IOpenAiClient, IPlaybookService injection).
builder.Services.AddSingleton<IDataverseEnvironmentRegistryClient, NullDataverseEnvironmentRegistryClient>();
builder.Services.AddScoped<H05ConsentCaptureHandler>();

// Task 043: H1 subscription-readiness handler + ARM readiness probe
// placeholder. Wave C5 replaces NullSubscriptionReadinessProbe with a real
// ARM-backed impl (Azure.ResourceManager SDK OR `az` shell-out) once the L2
// App Service UAMI has Reader RBAC granted on each customer subscription.
// Both registrations UNCONDITIONAL per ADR-032 — no feature-gate branches.
// Placement Justification (CLAUDE.md §10): H1 lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types (ADR-013).
// H1 uses IProvisioningRunRepository (task 037) + IHandlerEnqueuer (task
// 038); no BFF-facade dependencies. Downstream H2a is owned by sibling
// task 044.
builder.Services.AddSingleton<ISubscriptionReadinessProbe, NullSubscriptionReadinessProbe>();
builder.Services.AddScoped<H1SubscriptionReadinessHandler>();

// Task 044: H2a Bicep infra-deploy handler + four collaborator seams
// (IBicepDeployRunner shells out to scripts/Provision-Customer.ps1;
// IArmKeyVaultRefProbe + IUpgradeDriftDetector shell out to `az` CLI;
// IBicepTemplateInspector reads infrastructure/bicep/ on disk). All
// registrations UNCONDITIONAL per ADR-032 — SignalR is the feature-gated
// resource, not the handler; the handler passes through the SignalREnabled
// parameter to the runner unconditionally (Null-Object kill-switch applies
// to the RESOURCE, not the DI branch — spec MUST rule + design.md §7.2 row 13).
//
// Placement Justification (CLAUDE.md §10): H2a lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types (ADR-013
// forcing-function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H2a owns silent-fail trap T1 verification
// per POML acceptance §4B — this is the sole reason H2a exists as a handler
// wrapping the PS script (script alone leaves T1 as a runtime null-KV-ref
// timebomb; handler adds ARM read post-condition).
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-027 Path A: Model 1 shared-tier is documented exception —
//     TenancyModel drives stack selection (Model1Shared → stacks/model1-shared.bicep;
//     Model2Dedicated → customer.bicep). Full rationale: project spec.md § ADR Tensions.
//   - ADR-028 UAMI outbound: all four collaborators use `az` CLI's operator
//     auth chain (DefaultAzureCredential via `az login`); no account keys.
//   - §4C rollback: partial Bicep deploys are QuarantineRequired (orphaned
//     resources per design.md §4C example); §4C classification is inline in
//     H2aBicepInfraDeployHandler file header + the FailAsync helper.
builder.Services.Configure<BicepInfraDeployOptions>(
    builder.Configuration.GetSection(nameof(BicepInfraDeployOptions)));
builder.Services.AddSingleton<IBicepDeployRunner, ProvisionCustomerScriptBicepDeployRunner>();
builder.Services.AddSingleton<IArmKeyVaultRefProbe, AzCliArmKeyVaultRefProbe>();
builder.Services.AddSingleton<IUpgradeDriftDetector, AzCliUpgradeDriftDetector>();
builder.Services.AddSingleton<IBicepTemplateInspector, FileBicepTemplateInspector>();
builder.Services.AddScoped<H2aBicepInfraDeployHandler>();

// Task 045: H2b AI Search index-provisioning handler + four collaborator
// seams (ICanonicalIndexCatalog is the retired-lineage guard;
// IAiSearchIndexProvisioner wraps scripts/ai-search/Deploy-AllIndexes.ps1
// for Model 2; IAiSearchIndexVerifier calls the AI Search REST API for
// presence + invariants on both branches; IAiSearchTenantFilterTemplateProvisioner
// enforces §4D I2 / FR-29 at Model 1 onboarding). All registrations
// UNCONDITIONAL per ADR-032 — no feature-gate branches. The verifier is
// registered via AddHttpClient (typed) so DefaultAzureCredential's token
// cache is shared across handler invocations (ADR-028 UAMI-outbound MUST
// rule); the wave-C4 stub template provisioner logs the intended template
// contents + returns Success — swap to a real impl in Wave C5+ without
// touching H2b or its tests.
//
// Placement Justification (CLAUDE.md §10): H2b lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types (ADR-013
// forcing-function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H2b owns the §4D I2 (FR-29) enforcement at
// Model 1 tenant onboarding time — the per-tenant filter template is the
// PROVISIONING-time half of the tenantId eq filter invariant; the runtime
// half is enforced by BFF services + the Wave-C6 ArchTest.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-039 (compliance path C — pivot): retired `spaarke-playbook-embeddings`
//     is rejected structurally by ICanonicalIndexCatalog.RetiredIndexNames +
//     H2b's pre-check guard. Full retired lineage per task 002 audit § 2.
//   - ADR-027 Path A: Model 1 shared-tier is documented exception —
//     TenancyModel drives branch selection (Model1Shared → verifier +
//     template; Model2Dedicated → provisioner + verifier). Full rationale:
//     project spec.md § ADR Tensions.
//   - ADR-028 UAMI outbound: REST verifier + real template store impl use
//     DefaultAzureCredential; script wrapper delegates to operator `az` chain.
//   - §4C rollback: retired-index / provisioner-failure / invariant-violation
//     / shared-index-missing are QuarantineRequired; parameter-missing /
//     endpoint-missing / template-provisioner-failure are Resumable. Full
//     mapping inline in H2bAiSearchIndexHandler file header.
builder.Services.Configure<AiSearchIndexOptions>(
    builder.Configuration.GetSection(nameof(AiSearchIndexOptions)));
builder.Services.AddSingleton<ICanonicalIndexCatalog, CanonicalIndexCatalog>();
builder.Services.AddSingleton<IAiSearchIndexProvisioner, DeployAllIndexesScriptProvisioner>();
builder.Services.AddHttpClient<IAiSearchIndexVerifier, RestApiAiSearchIndexVerifier>();
builder.Services.AddSingleton<IAiSearchTenantFilterTemplateProvisioner, StubAiSearchTenantFilterTemplateProvisioner>();
builder.Services.AddScoped<H2bAiSearchIndexHandler>();

// Task 046: H3 Entra app-registration handler + two collaborator seams
// (IEntraAppRegProvisioner shells out to hardened
// scripts/Register-EntraAppRegistrations.ps1 per r1 task 010 commit fea66c023;
// IAdminConsentVerifier queries Graph oauth2PermissionGrants — Wave C4 uses
// NullAdminConsentVerifier (always Verified) as scaffold, Wave C5 swaps for
// Microsoft.Graph SDK v6 impl with DefaultAzureCredential per ADR-028). All
// registrations UNCONDITIONAL per ADR-032 — no feature-gate branches.
//
// Placement Justification (CLAUDE.md §10): H3 lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013). H3
// uses IProvisioningRunRepository (task 037) + two dedicated seams; no BFF-
// facade dependencies. Downstream H4 (task 047, Batch 3D) reads bffAppRegId
// from interStepState; H3 does NOT enqueue H4 directly — Wave C5 reconciler
// owns fan-out (parity with H2a's post-deploy branching model).
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-028 UAMI-outbound + KV-secret-ref: script uses az operator auth;
//     client secret stored in KV as BFF-API-ClientSecret and referenced
//     downstream as @Microsoft.KeyVault(SecretUri=...) — cleartext NEVER
//     traverses Cosmos parameters/interStepState (handler leak-guard enforces).
//   - spec.md MUST rule (Dataverse S2S drop per r3 task 060): NO S2sAppRegId
//     field on EntraAppRegOutputs (compile-time guard); NO code path to
//     invoke a script that would create one.
//   - §4C rollback: provisioner failures + missing precondition are Resumable;
//     cleartext-secret-leak + S2S-forbidden are QuarantineRequired.
//   - Admin-consent WaitingOnGate is NOT a failure per design.md §4.1 H3 row —
//     envelope is processed correctly; the gate is external.
builder.Services.Configure<EntraAppRegOptions>(
    builder.Configuration.GetSection(nameof(EntraAppRegOptions)));
builder.Services.AddSingleton<IEntraAppRegProvisioner, RegisterEntraAppRegScriptProvisioner>();
builder.Services.AddSingleton<IAdminConsentVerifier, NullAdminConsentVerifier>();
builder.Services.AddScoped<H3EntraAppRegHandler>();


// Task 048: H5 Dataverse env creation handler + 2 collaborator seams
// (IDataverseEnvCreator wraps `pac admin create-environment` interim per
// design.md § 4.1 H5 row + M-10 TF Power Platform deferral;
// IDataverseHealthProbe polls Web API `WhoAmI` via DefaultAzureCredential
// until Reachable — implements the Pending→Verified gate for the long-
// running Dataverse env-creation flow).
//
// DISPATCHER-INSERTED (task 048 agent completed without committing due to
// Batch 3C shared-worktree Program.cs write race; task 071's git-checkout
// HEAD -- Program.cs discarded 048's staged edits. Files on disk are correct;
// this DI block re-adds the registration per H2b/H3/H12a pattern).
builder.Services.Configure<DataverseEnvCreationOptions>(
    builder.Configuration.GetSection(nameof(DataverseEnvCreationOptions)));
builder.Services.AddSingleton<IDataverseEnvCreator, PacAdminDataverseEnvCreator>();
builder.Services.AddHttpClient<IDataverseHealthProbe, DataverseWebApiHealthProbe>();
builder.Services.AddScoped<H5DataverseEnvCreationHandler>();


// Task 047 (Batch 3D): H4 KV secrets-population handler + FOUR collaborator
// seams (IKvSecretManifest = interim StaticKvSecretManifest pending Phase H
// task 084's canonical secret-catalog manifest generator; IKvSecretsWriter =
// az CLI shell-out to `az keyvault secret set/show/delete`;
// IAppServiceIdentityPatcher = az CLI shell-out to `az webapp update --set
// keyVaultReferenceIdentity=<UAMI-RID>` on BOTH slots — T1 trap owner;
// ISlotIdentityRoleGranter = az CLI shell-out to `az role assignment create`
// against slot System-Assigned MIs — T5 interim trap owner). H4 also REUSES
// IArmKeyVaultRefProbe from H2a (task 044) for T1 post-condition verify —
// single source of truth for the T1 trap. All registrations UNCONDITIONAL
// per ADR-032 — no feature-gate branches. The T5 granter's
// NoSlotSystemAssignedIdentity outcome is a domain SUCCESS (post-Phase-C
// UAMI-only steady state), NOT a null-object kill-switch.
//
// Placement Justification (CLAUDE.md §10): H4 lives in L2 (not BFF) per spec
// §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-
// function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H4 uses IProvisioningRunRepository (task 037)
// + the four dedicated seams + reuses IArmKeyVaultRefProbe (task 044); no
// BFF-facade dependencies. Downstream H7 (env-var population) reads no state
// from H4 (H4 only mutates App Service + KV, not interStepState); the wave-C5
// reconciler owns fan-out.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-028 (Path C — comply): all KV writes flow through az CLI's operator
//     auth chain; T1 PATCH sets keyVaultReferenceIdentity to UAMI on both
//     prod + staging slots; cleartext secrets NEVER touch handler code (only
//     pass through IKvSecretsWriter's process boundary). The 21-MUST
//     keyVaultReferenceIdentity rule is the H4 raison d'être.
//   - ADR-004 idempotency: 3-level; kv-{customerId}-{secretsVer} is the
//     Level-3 durable key. Content change to manifest = new secretsVer =
//     new key = re-seed. Rotation-safe upgrade is the DEFAULT per spec.md
//     FR-34 H4 row.
//   - spec.md MUST rule (BINDING pre-check per r3 handoff): H4
//     BindingNeverDeleteSecrets = { Dataverse-ClientSecret, BFF-API-ClientSecret };
//     handler refuses any manifest with a Delete op on those two names + fails
//     QuarantineRequired BEFORE any external write. Fleet-wide OBO + shared-lib
//     Dataverse still depend on these secrets (#3b credential migration is
//     r1's task 011, not r1's H4). Writer carries a belt-and-braces guard
//     (AzCliKvSecretsWriter) so a direct writer call from a future refactor
//     still refuses the destructive op.
//   - §7.9 canonical naming (Phase G/H per r3 task 063 handoff): interim
//     StaticKvSecretManifest returns entries at canonical names (env-agnostic,
//     one canonical casing). Real Phase H manifest (task 084) swaps via DI
//     registration change only — H4 handler + tests unchanged.
//   - §4C rollback: parameter guards + manifest failure = Resumable; BINDING
//     violation + partial-KV-write + T1 patch/verify failure + cleartext
//     leak = QuarantineRequired; T5 grant failure = Resumable (INTERIM).
//     NoSlotSystemAssignedIdentity = SUCCESS (post-Phase-C UAMI structural
//     steady state).
//
// Batch 3D concurrent-write note: task 049 (H6 solution import) committed
// its own DI block + `using SolutionImport;` at 6b8698461 while this task's
// Step 9.5 quality gates ran; the H4 block below was re-applied to the
// post-049 Program.cs snapshot. No functional overlap; both handlers register
// disjoint seams.
builder.Services.Configure<KvSecretsPopulationOptions>(
    builder.Configuration.GetSection(nameof(KvSecretsPopulationOptions)));
builder.Services.AddSingleton<IKvSecretManifest, StaticKvSecretManifest>();
builder.Services.AddSingleton<IKvSecretsWriter, AzCliKvSecretsWriter>();
builder.Services.AddSingleton<IAppServiceIdentityPatcher, AzCliAppServiceIdentityPatcher>();
builder.Services.AddSingleton<ISlotIdentityRoleGranter, AzCliSlotIdentityRoleGranter>();
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
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-039 (single AI routing surface): manifest defense-in-depth scan
//     rejects any artifact declaration referencing spaarke-playbook-embeddings,
//     multinode, or dispatcher — even if task 069's Invoke-SeedManifest.ps1
//     is hand-edited to bypass its own retired-check. This is redundant with
//     the orchestrator but intentional; ADR-039 amendment 2026-07-05 raises
//     the bar for "MUST NOT land new capability on the frozen node-graph
//     engine" enough to warrant two independent gates.
//   - ADR-004 idempotency: the handler is Path C (comply) — CompletedPhases
//     scan is the Level-3 durable dedup per design.md §4.1 preamble; a
//     content change to manifest.yaml (any byte) produces a new SHA-256 +
//     new key + re-seed on next invocation (upgrade path).
//   - ADR-028 UAMI outbound: the PS orchestrator + downstream per-artifact
//     seeders authenticate via `az login` (operator auth chain via
//     DefaultAzureCredential in-script); no account keys pass through the
//     runner.
builder.Services.Configure<AiSeedChainOptions>(
    builder.Configuration.GetSection(nameof(AiSeedChainOptions)));
builder.Services.AddSingleton<ISeedManifestReader, FileSeedManifestReader>();
builder.Services.AddSingleton<ISeedManifestRunner, InvokeSeedManifestScriptRunner>();
builder.Services.AddScoped<H12aAiSeedChainHandler>();

// Task 071: H12b app-config seed handler + four IAppConfigSeeder registrations
// (DataGrid + workspace-layout via PowerShellAppConfigSeeder wrapping the
// existing shipping scripts per task 004 sec 4b decision matrix; field-mapping
// + chart-def via DeferredAppConfigSeeder pending Wave-C5 mirror authoring per
// task 004 sec 5b deltas N3 + N5). All registrations UNCONDITIONAL per ADR-032
// - no feature-gate branches (DeferredAppConfigSeeder is INTERIM behavior, not
// a feature-gate). DAG-parallel with H12a (task 070) - no cross-dependency;
// both handlers can fire post-H7.
//
// Placement Justification (CLAUDE.md sec 10): H12b lives in L2 (not BFF) per
// spec sec 5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013
// forcing-function rule - no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H12b uses IProvisioningRunRepository (task 037)
// + IHandlerEnqueuer (task 038) + the local IAppConfigSeeder seam; no
// BFF-facade dependencies. Idempotency key h12b-{customerId}-{SHA256(manifest.yaml)}
// deliberately mirrors H12a's manifestHash formula so operators reason about
// ONE manifest state across both parallel handlers.
//
// ADR Tension citations for PR description (per CLAUDE.md sec 6.5):
//   - ADR-004 idempotency: Path C (comply) - CompletedPhases scan is the
//     Level-3 durable dedup per design.md sec 4.1 preamble; a content change to
//     manifest.yaml (any byte) produces a new SHA-256 + new key + re-seed on
//     next invocation. Same shape as sibling H12a (task 070).
//   - ADR-010 DI minimalism: single AddH12bAppConfigSeedHandler() extension
//     replaces 6 raw registrations, holding Program.cs god-class-ratchet
//     margin.
//   - ADR-028 UAMI outbound: DataGrid + workspace-layout scripts authenticate
//     via `az login` (operator auth chain via DefaultAzureCredential in-script);
//     no account keys pass through the PowerShellAppConfigSeeder invocation.
//   - ADR-032 kill-switch: DeferredAppConfigSeeder is NOT the null-object
//     kill-switch - it is the intentional interim seeder for scopes without
//     a repo source (task 004 sec 4b rows 11, 13). When Wave-C5 authors the
//     field-mapping mirror + consolidated chart-def mirror, the DI
//     registration lines flip from DeferredAppConfigSeeder to
//     PowerShellAppConfigSeeder without touching the handler.
//   - sec 4C rollback: all four scopes classify as Resumable - every wrapped
//     script is upsert-safe (existence-check-then-insert or PATCH on the
//     stable per-row id/name key) so post-remediation resume re-drives cleanly.
//     Full mapping table inline in H12bAppConfigSeedHandler.cs file header.
builder.Services.AddH12bAppConfigSeedHandler(builder.Configuration);

// Task 049: H6 Package Deployer solution-import handler + 3 collaborator seams
// (ISolutionCatalog = C#-side mirror of Deploy-DataverseSolutions.ps1's
// $SolutionImportOrder per task 008 R5 binding; ISolutionImporter shells out
// to the wave-0 hardened Deploy-DataverseSolutions.ps1 for the 8 authoritative
// solutions per §11.1a; ISolutionVerifier shells out to `pac solution list`
// post-import to build the Cosmos interStepState.ImportedSolutions manifest
// with per-solution version + solutionId). All registrations UNCONDITIONAL
// per ADR-032 - no feature-gate branches.
//
// Placement Justification (CLAUDE.md §10): H6 lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-
// function rule - no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H6 uses IProvisioningRunRepository (task 037)
// + the local 3-seam surface; no BFF-facade dependencies. Idempotency key
// solimport-{customerId}-{catalogHash} follows the H2a bicepVer + H12a
// manifestHash version-suffix pattern so operators reason about ONE catalog
// state across re-runs. Downstream H7 (env-var values, task 050 wave 3E)
// reads from InterStepState.ImportedSolutions - Wave C5 reconciler owns
// H6 -> H7 fan-out (parity with H5 -> H6 pattern).
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-039 (compliance path C - pivot): retired dispatcher / embeddings
//     surface is rejected structurally by CanonicalSolutionCatalog.RetiredSolutionUniqueNames
//     + H6's FindRetiredMatch pre-check. Defense-in-depth against future
//     accidents; the 8 authoritative solutions do not currently overlap.
//   - ADR-028 UAMI outbound: PS script uses `pac auth create --clientSecret`
//     which requires an explicit client secret - Wave C4 reads it from
//     SolutionImportOptions:ClientSecret; Wave C5 wires the option-binding
//     to a Key Vault reference (@Microsoft.KeyVault(SecretUri=...)). Cleartext
//     secret NEVER traverses Cosmos parameters/interStepState (handler passes
//     via env var to pwsh child process only).
//   - §4C rollback: auth / rate-limit / quota / timeout / missing-zips /
//     unknown-invocation → Resumable (no side effect OR PS idempotent on
//     retry). Partial-import (Tier N failure after Tier N-1) / verification-
//     failure / retired-artifact-reintroduction → QuarantineRequired (Package
//     Deployer stage-and-upgrade may leave a holding solution behind on
//     mid-flight failure; ADR violation surfaces a code-review breakdown).
//     Full mapping table inline in H6SolutionImportHandler.cs file header.
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
// Placement Justification (CLAUDE.md §10): H7 lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-
// function rule - no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H7 uses IProvisioningRunRepository (task 037)
// + the single IEnvVarValuesWriter seam; no BFF-facade dependencies.
// Idempotency key envvars-{customerId}-{configVer} follows the H6 catalogHash
// / H4 secretsVer version-suffix pattern — configVer is a SHA-256 hash of the
// 7 resolved (schemaName, value) pairs so any upstream-state or operator-
// parameter change forces a re-write. Downstream H10 (app user, task 053)
// consumes no state from H7 (H7 only mutates the target Dataverse env's
// environmentvariablevalue records, not interStepState) - the wave-C5
// reconciler owns H7 -> H10 fan-out.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-028 (Path C - comply): confidential-client credentials flow through
//     EnvVarValuesOptions:ClientSecret (wave-C5 KV wiring); cleartext secret
//     NEVER traverses Cosmos parameters/interStepState (options-bound only,
//     parity with SolutionImportOptions:ClientSecret).
//   - ADR-004 idempotency: Path C (comply) - CompletedPhases scan is the
//     Level-3 durable dedup; configVer content-hash forces re-write on any
//     resolved-value change (parity with H6's catalogHash).
//   - §4C rollback: EVERY H7 failure mode classifies Resumable - every
//     environmentvariablevalue write is a natural upsert (PATCH-if-exists /
//     POST-if-not), so a full handler retry after ANY failure is always safe
//     with no cleanup required. No QuarantineRequired path exists for H7.
//     Full mapping table inline in H7DataverseEnvVarValuesHandler.cs file header.
builder.Services.Configure<EnvVarValuesOptions>(
    builder.Configuration.GetSection(nameof(EnvVarValuesOptions)));
builder.Services.AddHttpClient<IEnvVarValuesWriter, DataverseWebApiEnvVarValuesWriter>();
builder.Services.AddScoped<H7DataverseEnvVarValuesHandler>();

// Task 051 (Batch 3E): H8 SPE container-type + root-container handler + THREE
// collaborator seams (ISpeContainerTypeProvisioner shells out to the task-011
// T6-hardened scripts/Create-NewContainerType.ps1 -CreateTestContainer — the
// -CreateTestContainer switch creates the container-type AND a root container
// in one Graph-only invocation, avoiding a chicken-and-egg dependency on H5/H6
// Dataverse business-unit rows that don't exist yet at H8's point in the DAG;
// ISpeContainerVerifier shells out to the NEW scripts/Get-SpeContainerMetadata-AppOnly.ps1
// — a T6-compliant app-only GET (the existing Get-ContainerMetadata.ps1 uses a
// DELEGATED `az account get-access-token` and would defeat the T6 post-condition
// check); ISpeContainerIdKvWriter persists the real container-type id to the
// customer KV `SPE-ContainerTypeId` slot H4's manifest pre-creates with a
// placeholder). All registrations UNCONDITIONAL per ADR-032 — no feature-gate
// branches.
//
// Placement Justification (CLAUDE.md §10): H8 lives in L2 (not BFF) per spec
// §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-function
// rule — no IActionResolver, IActionRunner, IOpenAiClient, IPlaybookService
// injection). H8 uses IProvisioningRunRepository (task 037) + the three
// dedicated seams; owning-app id is read from InterStepState.BffAppRegId (H3
// output) rather than a run parameter — H8 owns no fallback path to create the
// app registration itself. Idempotency key spe-{customerId} is deliberately
// customerId-ONLY (version-independent, unlike H4/H6/H12a's content-hash
// suffix) — design.md line 1158 marks the SPE container-type "Never rotate;
// container = data": a repeat/upgrade run for the same customer MUST NOT
// re-create a container-type.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - spec.md MUST rule (T6, FR-33): confidential-client (app-only) cert-based
//     token is the ONLY auth path — enforced in BOTH the provisioner (creation)
//     and the verifier (post-condition GET), each independently detecting a
//     delegated-token trap signature ("public client not allowed") or missing
//     "T6 cleared" evidence markers and classifying QuarantineRequired +
//     TrapT6DelegatedTokenDetected rather than a routine Resumable failure.
//   - CLAUDE.md §11 component justification (Path C — extend where possible):
//     ISpeContainerIdKvWriter is a NEW narrow seam rather than reusing H4's
//     IKvSecretsWriter directly — AzCliKvSecretsWriter's production value
//     resolution is an INTERIM Phase-H placeholder with no caller-supplied-
//     value code path (see ISpeContainerIdKvWriter.cs header for the full
//     three-question justification). H8 writes to the SAME canonical secret
//     NAME (`SPE-ContainerTypeId`) H4's manifest already reserves.
//   - §4C rollback: parameter guards + missing H3 owning-app-id + non-T6
//     provisioning failure + provisioner infra fault + incomplete outputs =
//     Resumable (no confirmed external side effect, or side effect not yet
//     attempted). T6 trap (either stage) + non-T6 verification failure +
//     verifier infra fault + KV write failure/infra fault = QuarantineRequired
//     (external SPE resource created; post-condition unconfirmed or unpersisted).
//     Full mapping table inline in H8SpeContainerTypeHandler.cs file header.
//
// Deviations from the task POML's literal wording (Path C pivot-to-comply,
// documented per CLAUDE.md §6.5) are recorded in
// projects/customer-provisioning-orchestration-r1/notes/task-051-h8-deviations.md.
builder.Services.Configure<SpeContainerTypeOptions>(
    builder.Configuration.GetSection(nameof(SpeContainerTypeOptions)));
builder.Services.AddSingleton<ISpeContainerTypeProvisioner, CreateNewContainerTypeScriptProvisioner>();
builder.Services.AddSingleton<ISpeContainerVerifier, SpeContainerAppOnlyVerifier>();
builder.Services.AddSingleton<ISpeContainerIdKvWriter, AzCliSpeContainerIdKvWriter>();
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
// grants the 14 roles onto the UAMI SP via raw Graph REST calls (same endpoint
// shapes as scripts/Grant-GraphAppRoles.ps1, task 015); IGraphAppRoleParityVerifier
// = GraphRestAppRoleParityVerifier is the INDEPENDENT T3 post-grant re-query).
// All registrations UNCONDITIONAL per ADR-032 — no feature-gate branches.
//
// Placement Justification (CLAUDE.md §10): H10 lives in L2 (not BFF) per spec
// §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-function
// rule — no IActionResolver, IActionRunner, IOpenAiClient, IPlaybookService
// injection). H10 uses IProvisioningRunRepository (task 037) + the five
// dedicated seams; reads bffAppRegId/miClientId/miObjectId/dataverseEnvUrl from
// InterStepState (H3/H2a/H5-H6 outputs) rather than run parameters — H10 owns
// no fallback path to (re)create any of those upstream resources. Idempotency
// key appuser-{customerId} is deliberately customerId-ONLY (version-independent,
// parity with H5/H8) — App User registration + Graph role parity are per-
// customer steady state, not a versioned artifact.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - NFR-09 (Path C — pivot to comply in spirit, NOT a literal SDK dependency):
//     see H10DataverseAppUserGraphParityHandler.cs file-header "NFR-09
//     IMPLEMENTATION NOTE" — raw HttpClient + DefaultAzureCredential (parity
//     with every other Wave-C4 Graph/Dataverse collaborator) surfaces the same
//     HTTP-status + response-body diagnostic information NFR-09 asks the BFF's
//     Microsoft.Graph SDK ODataError catch to carry, without adding a new SDK
//     dependency to L2 for a 2-GET/1-POST REST surface already validated by
//     task 015's script.
//   - ADR-028 UAMI-outbound + §4D I5 explicit-tenant: every Dataverse + Graph
//     token acquisition uses DefaultAzureCredential with TenantId = the run's
//     explicit tenantId parameter — never an ambient default-tenant credential.
//   - §4C rollback: T2 + T3 post-condition MISMATCHES are QuarantineRequired
//     (silent-fail traps — the whole reason these two independent re-queries
//     exist); a raw Graph GRANT-CALL failure is RetryableWithCleanup (grants
//     are individually idempotent — a full re-run safely completes partial
//     state) — the two failure modes are DELIBERATELY different classes.
//     Full mapping table inline in H10DataverseAppUserGraphParityHandler.cs
//     file header.
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
// IAdminConsentVerifier gate shape). All registrations UNCONDITIONAL per
// ADR-032 — no feature-gate branches.
//
// Placement Justification (CLAUDE.md §10): H11 lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013
// forcing-function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H11 uses IProvisioningRunRepository (task
// 037) + the three dedicated seams; no BFF-facade dependencies. Idempotency
// key users-{customerId} is deliberately customerId-ONLY per POML constraint
// — user provisioning is per-customer; per-user idempotency is delegated to
// the Graph UPN alt-key check inside GraphRestUserProvisioner.CreateUserAsync.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - NFR-09 (Path C — pivot to comply in spirit, NOT a literal SDK
//     dependency): parity with H10's file-header "NFR-09 IMPLEMENTATION
//     NOTE" — raw HttpClient + DefaultAzureCredential surfaces the same
//     HTTP-status + response-body diagnostic NFR-09 asks the BFF's
//     Microsoft.Graph SDK ODataError catch to carry, without adding a new
//     SDK dependency to L2.
//   - ADR-028 UAMI-outbound + §4D I5 explicit-tenant: every Graph token
//     acquisition uses DefaultAzureCredential with TenantId = the run's
//     explicit tenantId parameter — never an ambient default-tenant credential.
//   - §4C rollback: user-creation + B2B-invitation failures are Resumable
//     (POST /users + POST /invitations are each individually idempotent per
//     their own existing-record short-circuit); a license-assignment
//     failure is RetryableWithCleanup (user account already exists;
//     assignLicense is itself idempotent) — DELIBERATELY distinct from the
//     user-creation failure class, parity with H10's T3-vs-grant-failure
//     distinction.
//   - B2B consent-pending is NOT a failure per design.md §4.1 H11 row (same
//     WaitingOnGate shape as H3's admin-consent gate) — envelope is
//     processed correctly; the gate is external (the customer-tenant guest
//     accepting the invitation).
//
// Branch-scope resolution note (per root CLAUDE.md §8.5 directional-steps
// guidance): see H11UserProvisioningHandler.cs file header "BRANCH
// SEMANTICS" for how the POML's <goal> vs <steps>/<acceptance-criteria>
// ambiguity (whether B2BGuest ALSO runs CreateUser+AssignLicense) was
// resolved — full note also in
// projects/customer-provisioning-orchestration-r1/notes/task-054-deviations.md.
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
// H12b's AddH12bAppConfigSeedHandler() god-class-ratchet pattern. All
// registrations UNCONDITIONAL per ADR-032 — no feature-gate branches.
//
// Placement Justification (CLAUDE.md §10): H12c lives in L2 (not BFF) per
// spec §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-
// function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H12c uses IProvisioningRunRepository (task
// 037) + IHandlerEnqueuer (task 038) + the local IModelDeploymentReference
// Writer seam; no BFF-facade dependencies. Idempotency key
// h12c-{customerId}-{tenancyModel}-{endpointHash} follows the POML-literal
// format (endpointHash = SHA-256 of the RESOLVED Azure OpenAI endpoint URI)
// so a tenancy migration or endpoint rotation forces a re-write.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - Path C (pivot to comply): the live sprk_aimodeldeployment Dataverse
//     schema (verified via mcp__dataverse__describe) carries NO tenantId
//     column, though the POML's literal step 4 asks for a "metering-
//     attribution tenantId field" on Model 1 rows. H12c does NOT add a new
//     column to a live table from inside a handler task (CLAUDE.md §11 —
//     schema changes are their own task); instead the tenantId attribution
//     is written as a human-readable note in sprk_description, and the
//     QUERYABLE metering mechanism is left to task 077's scope. Full
//     rationale in H12cRuntimeReferencesHandler.cs file header +
//     projects/customer-provisioning-orchestration-r1/notes/task-072-h12c-deviations.md.
//   - ADR-020: written rows are exactly the 3 pinned models (gpt-4o
//     2024-08-06, gpt-4o-mini 2024-07-18, text-embedding-3-large 1) per
//     PinnedModelCatalog.cs — no ad-hoc or unpinned entries.
//   - ADR-028 UAMI outbound + §4D I5 explicit-tenant: the writer's Dataverse
//     token acquisition uses DefaultAzureCredential with TenantId = the
//     run's explicit tenantId parameter — never an ambient default-tenant
//     credential.
//   - §4C rollback: EVERY H12c failure mode classifies Resumable — the
//     find-by-sprk_name/PATCH-or-POST upsert is retry-safe in full on any
//     partial-batch failure. No QuarantineRequired path exists for H12c
//     (parity with H7's reasoning). Full mapping table inline in
//     H12cRuntimeReferencesHandler.cs file header.
builder.Services.AddH12cRuntimeReferencesHandler(builder.Configuration);

// Task 073 (Batch 3F): H14 post-deploy integration wiring handler (parent) +
// its 3 DAG-parallel sub-handlers (H14a Exchange ApplicationAccessPolicy —
// T4 silent-fail trap owner; H14b Graph webhook subscriptions; H14c Dataverse
// service-endpoint webhook) + FOUR collaborator seams (IExchangePolicyApplier
// = ExchangePolicyScriptApplier shells out to the new
// scripts/Set-ExchangeApplicationAccessPolicy.ps1 — Exchange Online has no
// REST surface for ApplicationAccessPolicy management; IKvSecretReader =
// AzCliKvSecretReader reads back the H4-provisioned Communication-Webhook-
// SigningKey HMAC secret [a NEW narrow read-only seam — IKvSecretsWriter
// (H4) is write-only; see IKvSecretReader.cs for the full CLAUDE.md §11
// component-justification]; IGraphSubscriptionCreator = GraphRestSubscription
// Creator issues real Graph REST /subscriptions calls (list-then-create-or-
// renew, idempotent); IServiceEndpointWebhookRegistrar = DataverseWebApi
// ServiceEndpointWebhookRegistrar issues real Dataverse Web API upserts
// against serviceendpoints). Registered via a single
// AddH14IntegrationWiringHandler() extension method (IntegrationWiringModule.cs)
// — parity with H12b/H12c's god-class-ratchet pattern + this batch's
// dispatcher note that Program.cs is a SHARED file across 3 parallel Batch 3F
// siblings (054 H11, 072 H12c, 073 H14). All registrations UNCONDITIONAL per
// ADR-032 — no feature-gate branches.
//
// Placement Justification (CLAUDE.md §10): H14 lives in L2 (not BFF) per spec
// §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-
// function rule — no IActionResolver, IActionRunner, IOpenAiClient,
// IPlaybookService injection). H14 uses IProvisioningRunRepository (task 037)
// + the four dedicated seams; no BFF-facade dependencies. H14a/H14b/H14c do
// NOT touch Cosmos themselves — H14 (parent) owns the single read + single
// write to avoid a guaranteed ETag race across 3 TRUE-parallel in-process
// sub-invocations against the same ProvisioningRun document (full rationale
// in H14aExchangePolicySubHandler.cs file header "PARENT-OWNS-COSMOS DESIGN").
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - NFR-09 (Path C — pivot to comply in spirit, NOT a literal SDK
//     dependency): H14b's GraphRestSubscriptionCreator parity with H10's
//     file-header "NFR-09 IMPLEMENTATION NOTE" — raw HttpClient +
//     DefaultAzureCredential surfaces the same HTTP-status + response-body
//     diagnostic NFR-09 asks the BFF's Microsoft.Graph SDK ODataError catch
//     to carry, without adding a new SDK dependency to L2.
//   - ADR-004 (Path C — pivot to comply IN SPIRIT): H14 is ONE IJobHandler-
//     shape impl that internally dispatches 3 sub-steps via Task.WhenAll —
//     each sub-step still returns its OWN HandlerResult + has its OWN
//     deterministic idempotency key (h14-{customerId}-{subStep}-{hash} per
//     POML constraint); the PARENT owns the single Cosmos write that
//     aggregates all 3 outcomes. Full rationale in
//     H14IntegrationWiringHandler.cs file header "SINGLE-WRITER DAG-PARALLEL
//     DESIGN".
//   - CLAUDE.md §11 component justification (Path C — new narrow seam):
//     IKvSecretReader is a NEW read-only seam rather than widening H4's
//     IKvSecretsWriter (write-only manifest contract) with an unrelated read
//     concern only H14 needs. Full 3-question justification in
//     IKvSecretReader.cs file header.
//   - T4 (spec.md FR-33): H14a's IExchangePolicyApplier.ApplyAsync owns the
//     FULL action-and-verify sequence — 0/1 present creates the missing
//     entries; 2+ present VERIFIES parity and reports Drift (QuarantineRequired)
//     rather than silently overwriting. Post-condition verification happens
//     via the SAME Get-ApplicationAccessPolicy re-query the script uses to
//     decide the outcome — no separate out-of-band verify step exists to drift
//     out of sync with the action step.
//   - §4C rollback: parameter guards + missing upstream InterStepState fields
//     = Resumable; T4 drift (H14a) = QuarantineRequired (no silent overwrite);
//     Graph subscription / Dataverse serviceendpoint create-or-renew failures
//     = RetryableWithCleanup (both seams are individually idempotent — a full
//     re-run safely completes partial state). Full mapping table inline in
//     H14IntegrationWiringHandler.cs file header.
//
// Deviations from the task POML's literal wording (Path C pivot-to-comply,
// documented per CLAUDE.md §6.5) are recorded in
// projects/customer-provisioning-orchestration-r1/notes/task-073-h14-deviations.md.
builder.Services.AddH14IntegrationWiringHandler(builder.Configuration);

// Task 052 (Batch 4B): H9 BFF-deploy handler + FIVE collaborator seams
// (IR3GateVerifier = DotnetR3GateVerifier — shells to dotnet CLI + pwsh for
// the five r3-era gates: analyzers-as-errors (r3 task 041) + god-class ratchet
// (r3 task 040) + 4 tenant-isolation ArchTests (r1 task 064 pending) +
// naming-conformance (r3 task 063 pending) + Graph app-role parity (r3 task
// 062 / r1 task 067 pending) — gates whose artifacts are not yet in the tree
// are reported as Skipped rather than Failed so pending-artifact tasks don't
// pre-emptively block H9; IBffDeployRunner = DeployBffApiScriptRunner shells
// out to the shipped scripts/Deploy-BffApi.ps1 for build + zip + staging-slot
// deploy + staging-slot health check (script's steps 1-3); IAppServiceSlotSwapper
// = AzCliAppServiceSlotSwapper shells out to `az webapp deployment slot swap`
// for both the initial staging=>production swap AND the rollback re-swap;
// IHealthProbe = HttpHealthProbe issues HttpClient GETs against BFF /healthz
// with retry parity to Deploy-BffApi.ps1's Test-HealthCheck (24 x 5s) — NFR-05
// proof-of-KV-ref-resolution because BFF ValidateOnStart (r3 task 061) throws
// at boot on missing Tier-1 KV refs, so /healthz returning 200 IS the resolution
// evidence; IBffPublishSizeReporter = FileBffPublishSizeReporter measures the
// compressed publish zip + computes NFR-01 delta vs 44.96 MB baseline +
// flags threshold-tripped conditions). All registrations UNCONDITIONAL per
// ADR-032 - no feature-gate branches. H9's idempotency key is
// bff-{customerId}-{buildId} per POML constraint (buildId = BFF CI build
// number — a new build = new key = re-deploys; same build = level-3 no-op).
//
// Placement Justification (CLAUDE.md §10): H9 lives in L2 (not BFF) per spec
// §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-function
// rule - no IActionResolver, IActionRunner, IOpenAiClient, IPlaybookService
// injection). H9 uses IProvisioningRunRepository (task 037) + the five
// dedicated seams; no BFF-facade dependencies. The L2 App Service's UAMI
// authenticates every outbound Azure call inside AzCliAppServiceSlotSwapper's
// operator az CLI chain; the BFF's OWN keyVaultReferenceIdentity=UAMI wiring
// (task 047 H4) is what the /healthz smoke test verifies post-swap.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - Path C (comply): r3-era gate verification is a POML-mandated pre-swap
//     gate — the five gates block slot-swap with distinct rejection codes
//     per failing gate (POML criterion 2). Where gate artifacts have not yet
//     landed (r1 tasks 064 / 067 + r3 task 063 script), the verifier reports
//     Skipped rather than Failed. This is an INTERIM posture, NOT a
//     silent-bypass — once the pending artifacts land, the verifier auto-
//     picks them up (filesystem-based discovery, not compile-time-bound).
//     Full rationale + per-gate status inline in IR3GateVerifier.cs +
//     DotnetR3GateVerifier.cs file headers.
//   - Path C (comply): the shipped Deploy-BffApi.ps1 does NOT expose a
//     -SkipSwap switch, so the runner invokes the script against the STAGING
//     App Service (targets `{AppServiceName}-{SlotName}` directly) rather
//     than passing -UseSlotDeploy. This confines the script to its
//     build + zip + deploy + verify-staging-health scope (steps 1-3) while
//     the handler owns the swap + prod smoke test + rollback in-process for
//     tight Cosmos state coupling. Full rationale inline in
//     DeployBffApiScriptRunner.cs file header.
//   - Path C (comply — Gap 2 assertion per POML criterion 5): H9 performs a
//     defense-in-depth spaarkedev1-literal scan on Deploy-Release.ps1 before
//     the swap. Task 013 hardened Phase 4 to be customerId-driven with
//     -CustomerId Mandatory (no spaarkedev1 fallback); the handler-side scan
//     is the secondary belt-and-braces guard against a regression. A hit
//     fails the deploy QuarantineRequired with Spaarkedev1HardcodeDetected;
//     script-not-found is tolerated (task 013's hardening is the primary
//     defense; the handler-side scan is opportunistic).
//   - ADR-028 UAMI outbound: az CLI slot-swap ARM call flows through the
//     operator az CLI auth chain (L2 App Service's own UAMI via
//     DefaultAzureCredential); the BFF's OWN keyVaultReferenceIdentity=UAMI
//     wiring is what the post-swap /healthz smoke test verifies (BFF cannot
//     boot to 200 without every Tier-1 KV ref resolving).
//   - spec.md NFR-01 (publish-size ≤60 MB ceiling; ≥+5 MB per-task delta =
//     explicit justification): H9 REJECTS deploy BEFORE swap when either
//     threshold is exceeded (QuarantineRequired + PublishSizeDeltaExceeded).
//     Baseline 44.96 MB (2026-08-13 net10 fw-dependent linux-x64); overridable
//     via BffDeployOptions.BaselinePublishSizeBytes as newer baselines land.
//   - §4C rollback: parameter guards + run-not-found = Resumable; spaarkedev1
//     hardcode + publish-size threshold + smoke-test-failure-AND-rollback-
//     failure = QuarantineRequired; r3-gate failure + deploy runner failure
//     + deploy runner infra fault + publish-size reporter infra fault =
//     Resumable (fix + resume); slot-swap failure/infra-fault + smoke-test-
//     failure-with-successful-rollback = RetryableWithCleanup. Full mapping
//     table inline in H9BffDeployHandler.cs file header.
builder.Services.Configure<BffDeployOptions>(
    builder.Configuration.GetSection(nameof(BffDeployOptions)));
builder.Services.AddSingleton<IR3GateVerifier, DotnetR3GateVerifier>();
builder.Services.AddSingleton<IBffDeployRunner, DeployBffApiScriptRunner>();
builder.Services.AddSingleton<IAppServiceSlotSwapper, AzCliAppServiceSlotSwapper>();
builder.Services.AddHttpClient<IHealthProbe, HttpHealthProbe>();
builder.Services.AddSingleton<IBffPublishSizeReporter, FileBffPublishSizeReporter>();
builder.Services.AddScoped<H9BffDeployHandler>();

// Task 055 (Batch 4E): H13 E2E acceptance-gate handler + SIX collaborator seams
// (IE2EValidationRunner = ValidateDeployedEnvironmentScriptRunner wraps the
// Phase-B extended scripts/Validate-DeployedEnvironment.ps1 for SC #5 sample
// checks; IE2ETrapVerifier = PlaceholderTrapVerifier — Wave-C4 stub that
// returns InfraFault for every T1–T6 so H13 classifies Resumable when
// invoked against a live customer stamp without the full probe wiring in
// place [swap to real per-trap live-probe impl in Phase F task 089];
// IE2EInvariantVerifier = PlaceholderInvariantVerifier — parity stub for
// I1–I5; INamingConformanceChecker = NamingConformanceScriptRunner shells
// out to r3 task 063's scripts/naming-conformance-check.ps1 INDEPENDENTLY of
// the wrapped call inside Validate-DeployedEnvironment.ps1 so SC #17 is a
// distinct pass/fail boundary; ICostEnvelopeChecker = AzCliCostEnvelopeChecker
// shells out to `az costmanagement query` per subscription + compares against
// §15 #14 envelopes; IRegistrySetupStatusUpdater = DataverseRegistrySetupStatusUpdater
// is a Wave-C4 placeholder returning Success WITHOUT a real Dataverse write —
// Wave-C5 swaps for a real Web API PATCH once the L2 Dataverse client wiring
// lands, parity with H0.5's IDataverseEnvironmentRegistryClient placeholder-
// then-real evolution). H13 also REUSES IDataverseEnvironmentRegistryClient
// (task 042) for the idempotency-short-circuit registry Ready-check lookup.
// All registrations UNCONDITIONAL per ADR-032 — no feature-gate branches.
//
// H13's idempotency key is validate-{customerId}-{buildId} per POML constraint
// (buildId = BFF CI build number — a new build = new key = re-verifies; same
// build = level-3 no-op).
//
// Placement Justification (CLAUDE.md §10): H13 lives in L2 (not BFF) per spec
// §5.2 / D3 / D8 / D12; consumes NO AI-internal types (ADR-013 forcing-function
// rule — no IActionResolver, IActionRunner, IOpenAiClient, IPlaybookService
// injection). H13 uses IProvisioningRunRepository (task 037) + 6 dedicated
// seams; no BFF-facade dependencies. H13 is the SOLE authority for the
// Dataverse registry `sprk_setupstatus → Ready` transition — the r1
// acceptance floor per spec.md FR-18.
//
// Component Justification (CLAUDE.md §11):
//   - Existing: scripts/Validate-DeployedEnvironment.ps1 (operator-invocable
//     capstone) + scripts/naming-conformance-check.ps1 (r3 task 063). Neither
//     extends the automated per-run Cosmos state transition + registry-status
//     transition + §4C Quarantine semantics H13 needs.
//   - Extension: no — H13 is the SOLE authoritative gate. An IProvisioningHandler
//     is REQUIRED because only handlers own Cosmos state transitions +
//     §4C Quarantined semantics.
//   - Cost-of-doing-nothing: registry rows could transition to Ready with
//     silent trap failures still present (T1 keyVaultReferenceIdentity
//     mismatch → BFF boots with null KV refs → customer-visible runtime
//     failures the moment the operator hands the env over).
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-004 (Path A at L2 scope per spec.md ADR Tensions row 1): H13 is a
//     single IProvisioningHandler-shape impl that aggregates 6 collaborator
//     outcomes into ONE §4C classification decision. Same posture as H14's
//     parent handler (task 073) — the "one message one handler one outcome"
//     spirit is preserved; the fan-out is an INTERNAL orchestration detail.
//   - ADR-036 (Path C — comply): 3-level idempotency intact. Level 1 = SB
//     MessageId dedup (reconciler-owned); Level 3 = ProvisioningRun.
//     CompletedPhases scan for (Phase="H13", key==validate-{customerId}-
//     {buildId}). Level 2 (Redis) is NOT YET IMPLEMENTED in L2 (parity with
//     all sibling wave-C4 handlers).
//   - ADR-028 UAMI-outbound + §4D I5 explicit-tenant: every collaborator
//     seam uses the run's explicit tenantId parameter for token acquisition —
//     never an ambient default-tenant credential. IRegistrySetupStatusUpdater's
//     real Wave-C5 Dataverse Web API impl will follow the same DefaultAzureCredential
//     posture as H10/H11/H12c.
//   - Cost drift semantic (per POML deviation note, spec.md Unresolved Questions):
//     default posture is ADVISORY-WARN (Ready still transitions if all else
//     green; the drift is attached to the run's ErrorDetail advisory suffix +
//     gate-state). Opt-in fail-run via E2EAcceptance:CostDriftFailsRun=true.
//     Full rationale in projects/customer-provisioning-orchestration-r1/notes/task-055-deviations.md.
//   - §4C rollback: trap/invariant Failed + naming Failed + extended-validate
//     Failed + cost-drift-with-CostDriftFailsRun = QuarantineRequired;
//     trap/invariant InfraFault + validate-script infra fault + naming infra
//     fault + cost-query infra fault + registry-update failure = Resumable.
//     Full mapping table inline in H13E2EAcceptanceGateHandler.cs file header.
//
// Deviations from the task POML's literal wording (Path C pivot-to-comply
// per CLAUDE.md §6.5) are recorded in
// projects/customer-provisioning-orchestration-r1/notes/task-055-deviations.md.
builder.Services.AddH13E2EAcceptanceGateHandler(builder.Configuration);

// Task 058 (Wave C5): state-reconciler BackgroundService — polls Cosmos every
// 5s (configurable via ReconcilerOptions.PollInterval) for runs with status ∈
// {Running, WaitingOnGate}, computes DAG advancement per design.md §4.1
// handler dependencies via IDagAdvancer, and enqueues each ready-to-dispatch
// handler via IHandlerEnqueuer (task 038 Service Bus wire). Registers:
//   - ReconcilerOptions (bound + validated; PollInterval >= 1s enforced)
//   - TimeProvider.System (once, via TryAddSingleton — production clock)
//   - IDagAdvancer -> DagAdvancer (Singleton, pure function)
//   - IActiveRunScanner -> CosmosActiveRunScanner (Scoped; reuses the
//     CosmosClient singleton from CosmosModule + the same database/container
//     names — no duplicate client instantiation)
//   - StateReconcilerService (HostedService)
//
// PLACEMENT (CLAUDE.md §10 / §11): L2-only; no BFF references; no AI-internal
// injection (ADR-013 forcing-function rule). The reconciler is orchestration
// infrastructure — the ADR-004 Path A exception at L2 scope applies (spec.md
// ADR Tensions row 1 / CLAUDE.md §6.5).
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-004 (Path A at L2 scope): the state-reconciler is orchestration
//     infrastructure, NOT itself an IJobHandler. Custom state machine +
//     Cosmos-backed run doc + Service Bus enqueue is the documented L2
//     execution model per design.md §4.2 (Fable M-9 resolution).
//   - ADR-036 (Path C - comply): 3-level idempotency contract intact —
//     Level 1 is Service Bus MessageId dedup (deterministic per (HandlerId,
//     RunId, CustomerId, paramHash) via ServiceBusHandlerEnqueuer.ComputeMessageId);
//     Level 2 is BFF-side Redis IdempotencyService; Level 3 is Dataverse
//     alt-key upsert / Cosmos ETag on run doc. The reconciler does NOT
//     write to Cosmos itself — handlers own state-transition writes —
//     which keeps the reconciler ETag-race-free under N-instance concurrent
//     execution.
//   - ADR-032: all registrations UNCONDITIONAL - no feature-gate branches.
//     Kill-switch is via Reconciler:Enabled config flag (test-only escape
//     hatch; production leaves it true) which suppresses the ExecuteAsync
//     loop cleanly rather than skipping DI registration.
//   - §4D I3 waiver: CosmosActiveRunScanner.QueryActiveRunsAsync is
//     annotated with [AllowCrossPartitionScan("design.md §4.2 ...")] — the
//     ONE deliberate cross-partition read in L2, per design intent. The
//     attribute is a same-named local declaration (not a Spaarke.Core ref)
//     to avoid pulling Spaarke.Dataverse's Dataverse SDK transitively into
//     the L2 publish (~several MB).
builder.Services.AddReconcilerModule(builder.Configuration);

// Task 059 (Wave C5): I5 same-customer concurrency guard — optimistic upsert
// of sprk_dataverseenvironment.sprk_currentrunid (null -> newRunId). Registered
// via extension method (parity with ReconcilerModule so Program.cs stays
// god-class-ratchet-clean per ADR-010). All registrations UNCONDITIONAL per
// ADR-032; the kill-switch lives on CustomerRunGuardOptions.Enabled (defaults
// false — production deployments MUST set true after wiring the
// CustomerRunGuard:TargetDataverseUrl + TenantId + ClientId + ClientSecret KV
// references).
//
// Placement Justification (CLAUDE.md §10 / §11): L2-only. Consumes NO
// AI-internal types (ADR-013 forcing-function rule). The guard is
// orchestration infrastructure (Path A exception at L2 scope per spec.md
// ADR Tensions row 1) — NOT itself an IJobHandler. Reuses IHttpClientFactory
// (already registered by other handler modules) and IProvisioningRunRepository
// (task 037 CosmosModule); no duplicate client instantiation.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-044 (Path C — comply): sprk_currentrunid stores canonical GUID
//     form (bare, lowercase). CustomerRunGuard.CanonicalizeRunId normalizes
//     at the guard's ingress boundary; RunIdEquals normalizes both sides
//     defensively against a legacy non-canonical write.
//   - ADR-032 (Path C — comply): registered UNCONDITIONALLY. Kill-switch
//     lives INSIDE the guard (Enabled=false -> Success-always, WARN-logged)
//     so the DI graph is a stable shape across staged rollout.
//   - ADR-004 (Path A at L2 scope): the guard is orchestration infrastructure,
//     NOT itself an IJobHandler. Same reasoning as StateReconcilerService
//     (task 058) + CrashRecoveryStartupService (task 060).
//   - spec.md §4D I5 / FR-23 / FR-32: same-customer serialization via
//     optimistic upsert; cross-customer runs unaffected. Task 064's I5
//     ArchTest will recognize this guard as the sanctioned pattern.
//   - spec.md FR-24 integration hook: when the winning run's Cosmos status
//     is Quarantined, TryAcquireAsync returns
//     AcquireConflictReasonCodes.Quarantined so the operator sees a distinct
//     signal from a routine "already in flight" (matches task 061's Rollback
//     module: Quarantined runs KEEP sprk_currentrunid set until clear-quarantine).
builder.Services.AddCustomerRunGuard(builder.Configuration);

// Task 060 (Wave C5): I6 crash-recovery startup scan — CrashRecoveryStartupService
// is an IHostedService that on L2 boot scans Cosmos (via task 058's shared
// IActiveRunScanner) for status ∈ {Running, WaitingOnGate} runs whose last-
// activity age exceeds MAX(2× CrashRecovery:MedianHandlerDuration,
// CrashRecovery:FloorAge) and re-enqueues run.CurrentPhase via the SAME
// IHandlerEnqueuer (task 038) the reconciler uses. Registers:
//   - CrashRecoveryOptions (bound + validated; FloorAge >= 30s, MedianHandlerDuration >= 1s)
//   - CrashRecoveryStartupService (IHostedService — one-shot on StartAsync)
// Reuses TimeProvider.System already registered by AddReconcilerModule
// (TryAddSingleton) so this does NOT double-register the production clock.
//
// PLACEMENT (CLAUDE.md §10 / §11): L2-only; consumes NO AI-internal types
// (ADR-013 forcing-function rule). The scan is orchestration infrastructure —
// the ADR-004 Path A exception at L2 scope applies (spec.md ADR Tensions row 1
// / CLAUDE.md §6.5). Registered AFTER AddReconcilerModule so IActiveRunScanner
// + TimeProvider + IHandlerEnqueuer scoped registrations are all in place.
// Once task 059's guard lands as an IHandlerEnqueuer decorator (planned in a
// follow-on iteration), crash-recovery re-enqueues transitively inherit the
// same-customer serialization contract without a direct guard dependency here.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-004 (Path A at L2 scope): the crash-recovery service is orchestration
//     infrastructure, NOT itself an IJobHandler. Same reasoning as
//     StateReconcilerService (task 058).
//   - ADR-036 (Path C — comply): 3-level idempotency intact —
//     Level 1 is Service Bus MessageId dedup (crash-recovery re-enqueues use
//     byte-identical ParametersJson to what the reconciler produces, so
//     ComputeMessageId collapses them at the SB wire);
//     Level 2 is BFF-side Redis IdempotencyService;
//     Level 3 is Cosmos ETag + Dataverse alt-key upsert. Re-enqueueing
//     currentPhase is safe under ALL crash timings.
//   - ADR-032: registration UNCONDITIONAL — the kill-switch is a config flag
//     (CrashRecovery:Enabled) that suppresses the SCAN, not the DI graph.
//   - spec.md FR-23 SCOPE: the startup scan pairs with the reconciler's 5s
//     tick loop — the scan is the FAST-path resume that catches orphans
//     BEFORE the first reconciler tick fires; the reconciler is the ongoing
//     DAG advancer. Redundant dispatches collapse at SB dedup (level-1).
builder.Services.Configure<CrashRecoveryOptions>(
    builder.Configuration.GetSection(CrashRecoveryOptions.SectionName));
builder.Services.PostConfigure<CrashRecoveryOptions>(o => o.Validate());
builder.Services.AddHostedService<CrashRecoveryStartupService>();

// Task 061 (Wave C5): §4C rollback surface — IFailureClassifier + IQuarantineClearService.
// Provides the exhaustive FailureClass -> RunStatus mapping (RollbackTransitions)
// + the Quarantined -> Failed transition invoked by
// POST /api/runs/{id}/clear-quarantine (spec FR-24). All registrations
// UNCONDITIONAL per ADR-032 — no feature-gate branches; §4C is domain-
// authoritative (not a feature flag). Extension method keeps Program.cs
// god-class-ratchet-clean per ADR-010.
//
// Placement Justification (CLAUDE.md §10 / §11): L2-only (parity with the
// reconciler / crash-recovery / customer-run-guard); no BFF references; no
// AI-internal injection (ADR-013 forcing-function rule). The QuarantineClearService
// encapsulates the Quarantined -> Failed Cosmos transition (ETag-safe, one-
// shot semantic) so the endpoint layer stays intake-shaped.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - Path C (comply): FailureClass enum stays in Handlers/HandlerResult.cs
//     (57 files already reference the Handlers namespace); moving it would
//     churn 57 files for zero behavioral gain. RollbackTransitions consumes
//     Handlers.FailureClass by using-directive — no duplicate enum.
//     Deviation logged in notes/task-061-deviations.md.
//   - §4C rollback: RollbackTransitions.MapToRunStatus + ShouldReEnqueue +
//     ShouldReleaseCustomerGuard encode the §4C table as three exhaustive
//     switch expressions; UnreachableException at the default guard forces
//     any new FailureClass value to be handled at BUILD time (CS8524 warning-
//     as-error via TreatWarningsAsErrors inherited from Directory.Build.props).
//   - spec FR-24 SCOPE: QuarantineRequired keeps the customer-run-guard held
//     (ShouldReleaseCustomerGuard returns false) — new runs against the same
//     customerId are BLOCKED until clear-quarantine explicitly runs. This is
//     the hook task 059's ICustomerRunGuard.TryAcquireAsync reads to return
//     AcquireConflictReasonCodes.Quarantined; the state on the run doc is the
//     source of truth (no coupling between task 061 + task 059 code paths).
builder.Services.AddRollbackModule();

var app = builder.Build();

// ---- Middleware pipeline ----
// Swagger UI is enabled in ALL environments for the scaffold — the L2
// surface is Spaarke-internal and the OpenAPI schema is a required output
// (FR-21). Task 039 may gate this behind IsDevelopment() if operator policy
// changes; for now discoverability trumps hiding.
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Sprk.Provisioning.ControlPlane v1");
    o.RoutePrefix = "swagger";
});

app.UseAuthentication();

// Task 039: AuditLogMiddleware wraps the downstream pipeline (Authorization +
// endpoints) so mutating requests (POST/PUT/PATCH/DELETE) get an
// AuditableAction record emitted regardless of outcome — 200/201/202 success,
// 401/403 auth-failure short-circuits, or 500-class handler faults. Actor
// claims (tid/oid/roles) are read from HttpContext.User which
// UseAuthentication() has already populated at this point (null for
// unauthenticated requests — that's OK per POML acceptance #4). The record
// carries no body content per the POML constraint; only method + path +
// status + trace id + actor claims (spec NFR-11).
app.UseMiddleware<AuditLogMiddleware>();

app.UseAuthorization();

// ---- Endpoints ----
app.MapHealthEndpoints();

// Task 057 (Wave C5): the real L2 REST surface — 8 of the 9 endpoints per
// spec.md §4.2 (POST /api/runs, POST /api/runs/{id}/preflight, GET /api/runs/{id},
// POST /api/runs/{id}/gates/{gateId}/advance, POST /api/runs/{id}/resume,
// POST /api/runs/{id}/cancel, POST /api/runs/{id}/clear-quarantine, and
// GET /api/runs/{id}/phases/{phaseId}/logs). The 9th endpoint —
// POST /api/onboarding/consent-callback — is BFF-side (D18 Anonymous+HMAC)
// and is not part of this L2 project's surface.
//
// Placement Justification (CLAUDE.md §10): the endpoint layer lives in L2
// (not BFF) per spec §5.2 / D3 / D8 / D12; it consumes NO AI-internal types
// (ADR-013 forcing-function rule — no IActionResolver, IActionRunner,
// IOpenAiClient, IPlaybookService injection). Uses IProvisioningRunRepository
// (task 037) + IHandlerEnqueuer (task 038); no BFF-facade dependencies.
//
// ADR Tension citations for PR description (per CLAUDE.md §6.5):
//   - ADR-004 (Path A per spec.md ADR Tensions row 1): the L2 endpoint layer
//     is part of the documented custom state machine — mutating endpoints
//     enqueue via Service Bus + return 202 <100ms; handlers themselves remain
//     IJobHandler-shape (proven with the 19 handler impls in Handlers/).
//   - ADR-028 UAMI outbound: JWT bearer via Microsoft.Identity.Web (AuthModule),
//     bound to audience api://spaarke-provisioning-controlplane-{env};
//     Operator + Reader app-role policies from configuration.
//   - §4D I3 partition-key discipline: every /api/runs/{id}/* endpoint that
//     identifies a run REQUIRES the ?customerId= query parameter (or, for
//     POST /api/runs, the JSON body); IProvisioningRunRepository enforces the
//     partition-key predicate by construction (interface shape).
//   - spec FR-24: POST /api/runs/{id}/clear-quarantine requires ?reason=
//     (400 if missing); emits a structured "QuarantineCleared:" audit record
//     with actor tid + oid + reason + runId to App Insights `traces` via
//     ILogger + the OpenTelemetry -> Azure Monitor pipeline (task 039).
//
// State-transition scope: this task's endpoints WRITE new runs (POST) + READ
// runs (GET); everything else ENQUEUES. Actual state-machine transitions
// (Running -> Failed, Quarantined -> Cleared, DAG advancement) are owned by
// tasks 058 (reconciler) / 059 (I5 concurrency) / 060 (I6 crash recovery) /
// 061 (§4C rollback).
app.MapRunsEndpoints();
app.MapRunLogsEndpoints();

app.Run();

// Expose Program for future WebApplicationFactory-based integration tests
// (parity with BFF Program.cs; test project is not yet created).
public partial class Program
{
}
