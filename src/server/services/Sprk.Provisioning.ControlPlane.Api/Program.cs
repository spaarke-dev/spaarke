// -----------------------------------------------------------------------------
// Program.cs — Sprk.Provisioning.ControlPlane.Api
//
// L2 CONTROL-PLANE HTTP INTAKE HOST.
//
// SPLIT LINEAGE (task 100, DS-3 § 3 Option 2 owner-lock, 2026-08-19):
//   Before task 100 this file was a 1,178-line composition root inside the
//   single Sprk.Provisioning.ControlPlane project. Task 100 moved the
//   20-handler fleet DI + reconciler + crash-recovery + customer-run guard
//   + rollback registrations into Sprk.Provisioning.ControlPlane.Worker,
//   and moved the shared infrastructure + handler types into
//   Sprk.Provisioning.ControlPlane.Core. This host now composes only what
//   the HTTP intake surface needs: JWT bearer + Swagger + audit middleware +
//   Cosmos + ServiceBus + Telemetry + endpoint mappings. Every downstream
//   Wave-G task's ADR-tension citations + Placement Justification for the
//   individual handler registrations are preserved in the .Worker Program.cs
//   header — do NOT restore handler DI here.
//
// COMPOSITION (post-split):
//   - Minimal API WebApplication builder.
//   - Module composition (AddAuthModule + AddSwaggerModule + AddCosmosModule
//     + AddServiceBusModule + AddTelemetryModule) — parity with the BFF
//     *Module.cs pattern; respects NFR-07 god-class ratchet.
//   - Auth pipeline: UseAuthentication() -> UseMiddleware<AuditLogMiddleware>()
//     -> UseAuthorization(). AuditLogMiddleware wraps downstream so
//     Authorization short-circuits (401/403) still audit-log.
//   - Swagger UI at /swagger.
//   - Endpoints: /ping (anon) + the 8 L2 REST endpoints under /api/runs
//     (RunsEndpoints + RunLogsEndpoints, task 057 Wave C5).
//   - NO handler DI. NO BackgroundService that touches handler execution.
//     Those live in Sprk.Provisioning.ControlPlane.Worker (task 102+).
//
// PLACEMENT: L2 is a PEER service to Sprk.Bff.Api, not a BFF extension
// (ADR-010 DI minimalism; project MUST NOT rule — no reference to
// Sprk.Bff.Api assemblies from here).
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Api;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Endpoints;
using Sprk.Provisioning.ControlPlane.Middleware;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Registry;
using Sprk.Provisioning.ControlPlane.Rollback;

var builder = WebApplication.CreateBuilder(args);

// ---- Services registration (module composition per ADR-010) ----
builder.Services.AddAuthModule(builder.Configuration);
builder.Services.AddSwaggerModule();

// Cosmos client (UAMI-pinned DefaultAzureCredential; account
// disableLocalAuth: true) + IProvisioningRunRepository over the
// spaarke-provisioning/runs container. Every read/write includes the
// /customerId partition key by construction (§4D I3 / FR-30); replace
// uses ETag optimistic concurrency (FR-23 I5). The intake endpoints
// consume IProvisioningRunRepository to persist ProvisioningRun docs
// on POST /api/runs and read them on GET /api/runs/{id}.
builder.Services.AddCosmosModule(builder.Configuration);

// Service Bus client + IHandlerEnqueuer over the fleet-scoped queue the
// Worker's dispatcher (task 102) will drain. Deterministic MessageId =
// SHA256(HandlerId|RunId|CustomerId|paramHash) implements FR-22 level-1
// idempotency; SessionId = CustomerId enables per-customer FIFO ordering
// (§4D I5). DefaultAzureCredential (UAMI) — never account-key per ADR-028.
// The intake endpoints call IHandlerEnqueuer on every mutating request to
// enqueue a HandlerEnvelope + return 202 Accepted <100ms (FR-22 / R20).
builder.Services.AddServiceBusModule(builder.Configuration);

// OpenTelemetry -> Azure Monitor exporter, wired behind AzureMonitorGuard
// so a deployed L2 App Service missing APPLICATIONINSIGHTS_CONNECTION_STRING
// throws at startup (NFR-05) while Development / Testing envs skip silently.
// Without this, AuditLogMiddleware's structured-log emissions never reach
// App Insights and NFR-11's audit trail is dead — invisible failure. Feeds
// the OTel Logs pipeline; the audit records land in Azure Monitor `traces`
// with structured properties in customDimensions (Kusto: `traces | where
// message startswith "AuditableAction"`).
builder.Services.AddTelemetryModule(builder.Configuration, builder.Environment.EnvironmentName);

// ---- Dual-hosted services (registered on BOTH .Api and .Worker) ----
//
// ICustomerRunGuard (task 059, spec §4D I5 / FR-23 / FR-32) and
// IQuarantineClearService / IFailureClassifier (task 061, spec FR-24 / §4C)
// are consumed by BOTH hosts:
//   - .Api endpoints (this project): POST /api/runs and POST /api/runs/{id}/cancel
//     inject ICustomerRunGuard for I5 acquire/release around the intake
//     transaction. POST /api/runs/{id}/clear-quarantine injects
//     IQuarantineClearService for the Quarantined -> Failed transition.
//   - .Worker (Sprk.Provisioning.ControlPlane.Worker Program.cs): reconciler +
//     rollback classifier consume them for post-execution state transitions.
// Registering the same seams in both composition roots is deliberate —
// pre-split the single Program.cs held one graph; post-split, the intake
// surface and the execution surface each need their own graph but MUST use
// the SAME underlying types (identical Dataverse row keys, identical §4C
// mapping). Do NOT drop these from either host without an ADR-tracked
// contract change.
builder.Services.AddCustomerRunGuard(builder.Configuration);

// REG-07 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
// 2026-08-27): the CreateRun endpoint uses IDataverseEnvironmentRegistryClient
// to sanity-check the operator-supplied environmentId against the registry
// (customerId match + setupStatus==InProgress) before writing to Cosmos.
// The Api project registers the real Path X impl via
// DataverseEnvironmentRegistryModule.AddDataverseEnvironmentRegistry (parity
// with the Worker Program.cs task-122 wiring). When
// DataverseEnvironmentRegistry:AdminEnvironmentUrl is unset the module's
// PostConfigure fails fast — but the Api test host overrides the endpoint
// gracefully because the endpoint catches lookup faults + proceeds without
// the strict check (REG-07 fault-tolerance branch).
builder.Services.AddDataverseEnvironmentRegistry(builder.Configuration);

builder.Services.AddRollbackModule();

var app = builder.Build();

// ---- Middleware pipeline ----
// Swagger UI is enabled in ALL environments for the scaffold — the L2
// surface is Spaarke-internal and the OpenAPI schema is a required output
// (FR-21). A later task may gate this behind IsDevelopment() if operator
// policy changes; for now discoverability trumps hiding.
app.UseSwagger();
app.UseSwaggerUI(o =>
{
    o.SwaggerEndpoint("/swagger/v1/swagger.json", "Sprk.Provisioning.ControlPlane v1");
    o.RoutePrefix = "swagger";
});

app.UseAuthentication();

// AuditLogMiddleware wraps the downstream pipeline (Authorization +
// endpoints) so mutating requests (POST/PUT/PATCH/DELETE) get an
// AuditableAction record emitted regardless of outcome — 200/201/202 success,
// 401/403 auth-failure short-circuits, or 500-class handler faults. Actor
// claims (tid/oid/roles) are read from HttpContext.User which
// UseAuthentication() has already populated at this point (null for
// unauthenticated requests — that's OK per FR-20 acceptance). The record
// carries no body content; only method + path + status + trace id + actor
// claims (spec NFR-11).
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
// State-transition scope: this project's endpoints WRITE new runs (POST) +
// READ runs (GET); everything else ENQUEUES. Actual state-machine
// transitions (Running -> Failed, Quarantined -> Cleared, DAG advancement)
// are owned by the Worker (Sprk.Provisioning.ControlPlane.Worker) —
// task 058 reconciler / task 059 I5 concurrency / task 060 I6 crash
// recovery / task 061 §4C rollback / task 102 dispatcher.
app.MapRunsEndpoints();
app.MapRunLogsEndpoints();

app.Run();

// Expose Program for WebApplicationFactory-based integration tests
// (parity with BFF Program.cs; consumed by Api/RunsEndpointsTests.cs in
// Sprk.Provisioning.ControlPlane.Tests per task 057).
public partial class Program
{
}
