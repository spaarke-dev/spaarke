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
using Sprk.Provisioning.ControlPlane.Handlers.ConsentCapture;
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
