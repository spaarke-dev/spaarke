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

using Sprk.Provisioning.ControlPlane.Endpoints;
using Sprk.Provisioning.ControlPlane.Handlers.AiSearchIndex;
using Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;
using Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;
using Sprk.Provisioning.ControlPlane.Handlers.BicepInfraDeploy;
using Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;
using Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
using Sprk.Provisioning.ControlPlane.Handlers.EnvVarValues;
using Sprk.Provisioning.ControlPlane.Handlers.KvSecretsPopulation;
using Sprk.Provisioning.ControlPlane.Handlers.SolutionImport;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;
using Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;
using Sprk.Provisioning.ControlPlane.Middleware;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Registry;

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

app.Run();

// Expose Program for future WebApplicationFactory-based integration tests
// (parity with BFF Program.cs; test project is not yet created).
public partial class Program
{
}
