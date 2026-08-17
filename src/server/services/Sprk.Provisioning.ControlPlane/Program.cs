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
using Sprk.Provisioning.ControlPlane.Handlers.EntraAppReg;
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
