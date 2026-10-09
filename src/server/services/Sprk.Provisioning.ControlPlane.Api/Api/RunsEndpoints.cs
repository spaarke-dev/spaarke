// -----------------------------------------------------------------------------
// RunsEndpoints.cs
//
// L2 CONTROL-PLANE REST API — /api/runs surface (task 057, Wave C5).
//
// SPEC / DESIGN references:
//   - spec.md FR-21:  All 9 L2 REST endpoints per §4.2 must be exposed;
//                     OpenAPI at /swagger enumerates them.
//   - spec.md FR-22:  Handler-invoking endpoints ENQUEUE via Service Bus +
//                     return 202 Accepted in <100ms — no synchronous handler
//                     execution in the HTTP request path. App Service 230s
//                     HTTP timeout vs 30-min handlers.
//   - spec.md FR-20:  JWT bearer + audience api://spaarke-provisioning-controlplane-{env}
//                     + Operator/Reader app-roles.
//   - spec.md FR-24 / §4C: POST /api/runs/{id}/clear-quarantine REQUIRES a
//                     `reason` parameter and MUST audit-log the action + actor
//                     tid to App Insights.
//   - spec.md §4D I3 / FR-30: EVERY Cosmos read/write MUST include an explicit
//                     /customerId partition-key predicate — never a cross-
//                     partition query.
//   - design.md §4.2 endpoint table: authoritative surface.
//   - design.md §4.2 handler execution model (v3.2 Fable M-9): fire-and-forget
//                     via Service Bus + state-reconciler BackgroundService in L2.
//
// ADR references:
//   - ADR-004 (Path A per CLAUDE.md §6.5 + spec.md ADR Tensions row 1):
//                     L2 orchestration is documented custom state machine.
//                     Handlers themselves are IJobHandler-shape (§5.1).
//   - ADR-010:        Endpoints register in L2 DI (this project), NOT BFF.
//                     No feature-module DI added here — the endpoint mapping
//                     is composed in Program.cs via the extension method
//                     .MapRunsEndpoints() at the SAME layer as the health
//                     placeholder + module composition.
//   - ADR-032:        No conditional service branches — all endpoints map
//                     unconditionally. Feature-gating (if any) is per-handler
//                     via the Null-Object kill-switch pattern, never here.
//   - ADR-036:        Enqueue uses the shared IHandlerEnqueuer abstraction
//                     (task 038). Do NOT invent a new queuing abstraction.
//   - ADR-028 (via AuthModule task 036): JWT bearer auth is composed via
//                     Microsoft.Identity.Web AddMicrosoftIdentityWebApi;
//                     policies Operator + Reader are enforced by name.
//
// SCOPE (task 057):
//   Maps 7 of 8 L2 endpoints under /api/runs:
//     1. POST   /api/runs                                     Operator
//     2. POST   /api/runs/{id}/preflight                      Operator
//     3. GET    /api/runs/{id}                                Reader
//     4. POST   /api/runs/{id}/gates/{gateId}/advance         Operator
//     5. POST   /api/runs/{id}/resume                         Operator
//     6. POST   /api/runs/{id}/cancel                         Operator
//     7. POST   /api/runs/{id}/clear-quarantine               Operator
//   The 8th L2 endpoint — GET /api/runs/{id}/phases/{phaseId}/logs — is in the
//   sibling file Api/RunLogsEndpoints.cs (separated for readability + so
//   /swagger's tag grouping cleanly splits "Runs" from "RunLogs").
//   The 9th endpoint per §4.2 — POST /api/onboarding/consent-callback — is
//   BFF-side (D18 Anonymous+HMAC redirect from Microsoft admin-consent flow)
//   and is NOT part of this L2 project's surface. See design.md §4.3a.2
//   "Model 2 self-service exception".
//
// PARTITION-KEY DISCIPLINE (§4D I3 / FR-30):
//   Every endpoint that identifies a run by {id} REQUIRES the customerId
//   query parameter (or, for POST /api/runs, the JSON body). All Cosmos reads
//   route through IProvisioningRunRepository which requires customerId as its
//   first parameter — cross-partition access is a compile-time impossibility.
//
// AUTH POLICY BOUNDARY:
//   - Operator: mutating endpoints (POST create/init/advance/resume/cancel/
//               preflight/clear-quarantine). Operator implicitly satisfies Reader.
//   - Reader:   read-only endpoints (GET). Reader alone cannot invoke mutating.
//
// ENQUEUE PATH (POST endpoints, FR-22 / R20):
//   All handler-invoking POST endpoints:
//     (a) validate the request shape (400 on bad body/missing required field)
//     (b) verify the run exists in the caller-supplied customerId partition
//         (404 on miss; §4D I3 enforced by construction)
//     (c) enqueue a HandlerEnvelope via IHandlerEnqueuer — MessageId is
//         deterministic per (HandlerId, RunId, CustomerId, paramHash), so
//         duplicate submissions are dedup'd at the Service Bus wire (FR-22
//         level-1 idempotency).
//     (d) return 202 Accepted with Location header pointing at GET /api/runs/{id}.
//   Latency target: <100ms end-to-end (excluding first-hit Cosmos client
//   handshake). Measured in tests via 202-latency spot-check.
//
// AUDIT-LOG (POST /api/runs/{id}/clear-quarantine, spec FR-24):
//   The clear-quarantine endpoint REQUIRES a `reason` query parameter (400
//   otherwise) and emits a structured `ILogger.LogInformation` record with
//   message prefix "QuarantineCleared:" carrying actor tid + oid + runId +
//   customerId + reason. The record flows through the OpenTelemetry -> Azure
//   Monitor pipeline (task 039 TelemetryModule) into App Insights `traces`
//   with structured customDimensions. The general AuditLogMiddleware (task
//   039) ALSO emits an AuditableAction record for every mutating request;
//   the QuarantineCleared record is IN ADDITION per spec FR-24's explicit
//   requirement for a purpose-built event (Kusto queries can pivot on either).
//
// STATE-TRANSITION SCOPE (this task):
//   This task's endpoints WRITE new runs (POST /api/runs) and READ existing
//   runs (GET /api/runs/{id}). Everything else ENQUEUES a handler — actual
//   state transitions (Running -> Failed, Quarantined -> Cleared, etc.) are
//   owned by:
//     - Task 058 (state-reconciler) — DAG advancement, retry dispatch.
//     - Task 059 (I5 concurrency guard) — same-customer serialization.
//     - Task 060 (I6 crash recovery) — orphaned-run scan.
//     - Task 061 (§4C rollback semantics) — Quarantined transitions, clear-
//                                            quarantine state mutation.
//   The endpoints here are the intake surface; the state machine advances
//   asynchronously. This separation is deliberate per the Fable M-9
//   resolution (design.md §4.2 v3.2).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;
using Sprk.Provisioning.ControlPlane.Handlers.Preflight;
using Sprk.Provisioning.ControlPlane.Handlers.SecureRecordSetup;
using Sprk.Provisioning.ControlPlane.Handlers.UserProvisioning;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Repositories;
using Sprk.Provisioning.ControlPlane.Rollback;

namespace Sprk.Provisioning.ControlPlane.Api;

/// <summary>
/// L2 REST API endpoints under <c>/api/runs</c>. Maps 7 of the 8 L2 endpoints
/// per spec §4.2; the read-only phase-logs endpoint lives in the sibling
/// <see cref="RunLogsEndpoints"/> file.
/// </summary>
public static class RunsEndpoints
{
    /// <summary>Stable log-event prefix for the FR-24 clear-quarantine audit record.</summary>
    public const string QuarantineClearedEventName = "QuarantineCleared";

    /// <summary>
    /// COMP-03 (customer-provisioning-orchestration-r1 SESSION 17 pre-dispatch
    /// remediation, 2026-08-27): centralised profile-enum constants + design
    /// call = REJECT unknown profile (mirrors intake.schema.json profile enum
    /// exactly). Rationale for the reject design (vs warn+accept):
    ///   1. intake.schema.json declares profile as a strict enum
    ///      — batch mode already fails schema validation on any drift.
    ///   2. Interactive mode passes the operator-typed value straight through;
    ///      accepting an unknown profile silently produces cryptic downstream
    ///      failures (H5 tier derivation, H11 user provisioning gate).
    ///   3. The 4th "unknown" branch never has a defensible behavior — every
    ///      handler hard-casts. Rejecting at intake with a clear diagnostic
    ///      is strictly better than any warn+accept behavior.
    ///
    /// Enforced by <see cref="TryValidateTenancyProfilePair"/> at the endpoint
    /// layer and mirror-checked by a contract-parity test in the sibling
    /// <c>Sprk.Provisioning.ControlPlane.Tests</c> project that reads
    /// <c>scripts/provisioning-prereqs/intake.schema.json</c> at test time and
    /// asserts the enum values match this class's constants exactly (fails the
    /// build the moment either surface drifts).
    ///
    /// Task 225b (D-12, G6): <c>spaarke-hosted-model1-trial</c> (the retired shared
    /// trial/SMB tier) is no longer a known profile — a request carrying it is
    /// refused as an unknown profile. The profile names predate the D-12
    /// renumbering: <c>spaarke-hosted-model2</c> is the Model 1 profile.
    /// </summary>
    public static class KnownProfiles
    {
        /// <summary>Model 1 (D-12): dedicated stamp hosted in Spaarke's tenant and Azure subscription. Pairs only with tenancyModel <c>Model1</c>.</summary>
        public const string SpaarkeHostedModel2 = "spaarke-hosted-model2";

        /// <summary>Model 2 (D-12): dedicated stamp in the customer's own tenant and Azure subscription. Pairs only with tenancyModel <c>Model2</c>.</summary>
        public const string CustomerOwnedModel2 = "customer-owned-model2";

        /// <summary>All legal profile values — the authoritative L2-side enum.</summary>
        public static readonly IReadOnlyList<string> All = new[]
        {
            SpaarkeHostedModel2,
            CustomerOwnedModel2,
        };
    }

    /// <summary>
    /// COMP-03 (customer-provisioning-orchestration-r1 SESSION 17): centralised
    /// tenancy-model enum constants — same reject-unknown design as
    /// <see cref="KnownProfiles"/>. Mirrors intake.schema.json tenancyModel enum.
    /// Task 223 (D-12, 2026-09-29): const strings + <c>All</c> now derive from the
    /// shared <see cref="Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel"/>
    /// enum via <c>nameof</c> / <c>Enum.GetNames</c>. The runtime string values are
    /// unchanged (BINDING: H12c idempotency-key format preservation) — this class
    /// is now the enum's string view for the HTTP surface.
    /// </summary>
    public static class KnownTenancyModels
    {
        /// <summary>Model 1 — Spaarke-hosted dedicated stamp per D-12 (post-T224 rename; pre-T224 was <c>Model1Shared</c>).</summary>
        public const string Model1 = nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1);

        /// <summary>Model 2 — customer-hosted dedicated stamp per D-12 (post-T224 rename; pre-T224 was <c>Model2Dedicated</c>).</summary>
        public const string Model2 = nameof(Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2);

        /// <summary>All legal tenancyModel string values, derived from the enum.</summary>
        public static readonly IReadOnlyList<string> All = Enum.GetNames<Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel>();
    }

    /// <summary>
    /// Downstream handler dispatched by <c>POST /api/runs</c> on run creation
    /// and by <c>POST /api/runs/{id}/preflight</c>. H0 is the preflight-quota
    /// probe per design.md §4.1 catalog.
    /// </summary>
    private const string InitialHandlerId = "H0";

    /// <summary>
    /// Handler identifier the resume + cancel + advance + clear-quarantine
    /// endpoints use as their <c>HandlerId</c> tag on the enqueued envelope.
    /// The state-reconciler (task 058) inspects the envelope's ApplicationProperties
    /// to route to the correct in-flight handler; a stable per-action tag keeps
    /// the dispatch loop readable.
    /// </summary>
    private const string ResumeActionId = "Resume";

    private const string CancelActionId = "Cancel";
    private const string GateAdvanceActionId = "GateAdvance";
    private const string ClearQuarantineActionId = "ClearQuarantine";

    /// <summary>
    /// Serializer for the opaque <see cref="HandlerEnvelope.ParametersJson"/>
    /// payload. camelCase parity with ServiceBusHandlerEnqueuer + Cosmos wire.
    /// </summary>
    private static readonly JsonSerializerOptions ParametersJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    /// <summary>Maps all seven <c>/api/runs</c> endpoints onto the application.</summary>
    public static IEndpointRouteBuilder MapRunsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/runs").WithTags("Runs");

        // (1) POST /api/runs — create the run + enqueue H0 preflight.
        group.MapPost("/", CreateRun)
            .RequireAuthorization(AuthModule.Policies.Operator)
            .WithName("CreateRun")
            .WithSummary("Initialize a provisioning run against an environment record")
            .WithDescription(
                "Creates a new ProvisioningRun in Cosmos (partition /customerId, " +
                "status=NotStarted) and enqueues H0 preflight via Service Bus. " +
                "Returns 202 Accepted with Location header pointing at GET /api/runs/{id}.")
            .Produces<CreateRunResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status500InternalServerError);

        // (2) POST /api/runs/{id}/preflight — re-run H0 preflight for an existing run.
        group.MapPost("/{id}/preflight", RunPreflight)
            .RequireAuthorization(AuthModule.Policies.Operator)
            .WithName("RunPreflight")
            .WithSummary("Enqueue H0 preflight for the specified run")
            .WithDescription(
                "Re-dispatches H0 preflight quota-probe against the existing " +
                "ProvisioningRun. Idempotent — repeat calls are wire-level dedup'd " +
                "on the deterministic Service Bus MessageId.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // (3) GET /api/runs/{id} — read current run state.
        group.MapGet("/{id}", GetRun)
            .RequireAuthorization(AuthModule.Policies.Reader)
            .WithName("GetRun")
            .WithSummary("Return current phase, completed phases, gate states, and quarantine info")
            .WithDescription(
                "Point-reads the ProvisioningRun from Cosmos. REQUIRES ?customerId=" +
                " query parameter — §4D I3 forbids cross-partition reads.")
            .Produces<ProvisioningRun>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // (4) POST /api/runs/{id}/gates/{gateId}/advance — operator advances a manual gate.
        group.MapPost("/{id}/gates/{gateId}/advance", AdvanceGate)
            .RequireAuthorization(AuthModule.Policies.Operator)
            .WithName("AdvanceGate")
            .WithSummary("Mark a manual gate as cleared (operator override)")
            .WithDescription(
                "Enqueues a gate-advance dispatch for the specified gate. The " +
                "reconciler (task 058) picks up the envelope and applies the " +
                "Verified transition to the ProvisioningRun's gateStates map.")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // (5) POST /api/runs/{id}/resume — resume a failed run from failure point.
        group.MapPost("/{id}/resume", ResumeRun)
            .RequireAuthorization(AuthModule.Policies.Operator)
            .WithName("ResumeRun")
            .WithSummary("Resume a Failed run from its current failure point")
            .WithDescription(
                "Enqueues a resume-dispatch envelope; the reconciler picks up " +
                "and re-runs the current phase. Idempotency handles duplicate " +
                "resumes (3-level per ADR-004).")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // (6) POST /api/runs/{id}/cancel — operator explicitly cancels a run.
        group.MapPost("/{id}/cancel", CancelRun)
            .RequireAuthorization(AuthModule.Policies.Operator)
            .WithName("CancelRun")
            .WithSummary("Cancel an in-progress run")
            .WithDescription(
                "Enqueues a cancel-dispatch envelope; the reconciler transitions " +
                "the run to Cancelled and clears sprk_currentrunid on the " +
                "registry row (task 059 concurrency guard).")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        // (7) POST /api/runs/{id}/clear-quarantine — v3.2 §4C reversible clear.
        // Task 061 wired the QuarantineClearService — endpoint returns 409 when
        // the run is not in Quarantined state (wrong-state) OR when a
        // concurrent writer advanced the Cosmos ETag.
        group.MapPost("/{id}/clear-quarantine", ClearQuarantine)
            .RequireAuthorization(AuthModule.Policies.Operator)
            .WithName("ClearQuarantine")
            .WithSummary("Clear a Quarantined run (v3.2 §4C) — REQUIRES ?reason= parameter")
            .WithDescription(
                "Explicitly releases a Quarantined run so a new run can start " +
                "against the same customerId. Transitions Cosmos state " +
                "Quarantined -> Failed + QuarantineInfo.State = Cleared + " +
                "populates ClearedBy/ClearedAt. REQUIRES ?reason= parameter " +
                "(400 otherwise), returns 404 if the run does not exist, 409 " +
                "if the run is not in Quarantined state (wrong-state) OR on a " +
                "concurrent-write ETag conflict, and audit-logs the action + " +
                "actor tid + oid to App Insights `traces` with message prefix " +
                "'QuarantineCleared:' (spec FR-24 acceptance).")
            .Produces(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);

        return app;
    }

    // -------------------------------------------------------------------------
    // Endpoint handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// POST /api/runs. Acquires the per-customer I5 concurrency guard (task
    /// 059), creates a new ProvisioningRun in Cosmos + enqueues H0 preflight
    /// via Service Bus. Returns 202 Accepted with Location header on success,
    /// 409 Conflict with the winning run id on same-customer contention, 502
    /// on a transient guard failure.
    /// </summary>
    private static async Task<IResult> CreateRun(
        CreateRunRequest request,
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        ICustomerRunGuard runGuard,
        Sprk.Provisioning.ControlPlane.Registry.IDataverseEnvironmentRegistryClient registryClient,
        HttpContext httpContext,
        ILogger<RunsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(runGuard);
        ArgumentNullException.ThrowIfNull(registryClient);

        if (request is null)
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.RequestBodyRequired, "Request body is required.");
        }
        if (string.IsNullOrWhiteSpace(request.CustomerId))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.CustomerIdRequired, "customerId is required.");
        }
        // T237 (owner D10 / INCOMING-CUSTOMERID-STANDARD §3.1): the customerId standard is enforced
        // HERE — ARM has no @pattern and the Dataverse column has no regex, so nothing later can catch
        // a hyphenated id (silent storage-account collision) or an over-long one (customer.bicep's Key
        // Vault name ends in a hyphen → H2a fails). Reject, never repair; checked before the registry
        // lookup, the run guard, any Cosmos write and any enqueue.
        if (!CustomerIdStandard.IsValid(request.CustomerId))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.CustomerIdNonStandard,
                $"customerId '{request.CustomerId}' does not match the customerId standard {CustomerIdStandard.Pattern}: " +
                $"{CustomerIdStandard.Description}. Abbreviate longer customer names at intake (northwind -> nwind) " +
                "and record the full name as the registry row's display name.");
        }
        if (CustomerIdStandard.IsReserved(request.CustomerId))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.CustomerIdReserved,
                $"customerId '{request.CustomerId}' is reserved: it names a non-customer resource group " +
                $"(rg-spaarke-{request.CustomerId}-{{env}}). Reserved ids: {string.Join(", ", CustomerIdStandard.ReservedIds)}.");
        }
        if (string.IsNullOrWhiteSpace(request.EnvironmentId))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.EnvironmentIdRequired, "environmentId is required.");
        }
        if (string.IsNullOrWhiteSpace(request.TenancyModel))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.TenancyModelRequired, "tenancyModel is required.");
        }
        if (string.IsNullOrWhiteSpace(request.Profile))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.ProfileRequired, "profile is required.");
        }

        // ISH-11 (customer-provisioning-orchestration-r1 Wave 5 punchlist,
        // 2026-08-27): enforce tenancyModel × profile cross-field invariant,
        // mirroring intake.schema.json's allOf logic. Without this check, a
        // direct-API caller (test harness, retry script) supplying an invalid
        // pair (e.g., Model1 + customer-owned-model2) succeeds at
        // CreateRun; handlers that read tenancyModel then misbehave (H5 tier
        // derivation, H11 user provisioning gate). Downstream failures are
        // cryptic — surfacing "invalid tenancy/profile pair" at intake is
        // the only place the operator gets a clear signal.
        //
        // Rules (mirrors intake.schema.json allOf; task 225b / D-12 pairing):
        //   Model1 → profile MUST be 'spaarke-hosted-model2' (Spaarke-hosted dedicated stamp)
        //   Model2 → profile MUST be 'customer-owned-model2' (customer-hosted dedicated stamp)
        //   Any other profile value (incl. the retired 'spaarke-hosted-model1-trial')
        //   or tenancyModel value → 400 (enum check).
        if (!TryValidateTenancyProfilePair(request.TenancyModel, request.Profile, out var pairError))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.TenancyProfileInvalid, pairError);
        }

        // Task 245a (G25 — run-context contract): nonSecretParameters is the ONLY writer of
        // run.Parameters.NonSecret, so it may carry intake values only — the closed set in
        // IntakeParameterCatalog. An unknown key is a typo (`tenant_id`) or a value some handler
        // produces (which belongs in InterStepState); either way no handler would ever read it as
        // intended, so refuse it here instead of letting the run fail deep in the DAG.
        if (request.NonSecretParameters is not null)
        {
            var unknownKeys = IntakeParameterCatalog.UnknownKeys(request.NonSecretParameters.Keys);
            if (unknownKeys.Count > 0)
            {
                // Echo at most a few rejected keys (the body is attacker-controlled); the full accepted set
                // goes back in `acceptedKeys` so the caller can correct the payload without reading code.
                const int maxEchoed = 5;
                var echoed = string.Join(", ", unknownKeys.Take(maxEchoed).Select(k => $"'{k}'"))
                    + (unknownKeys.Count > maxEchoed ? $" (+{unknownKeys.Count - maxEchoed} more)" : string.Empty);
                return ControlPlaneProblems.Create(
                    httpContext,
                    StatusCodes.Status400BadRequest,
                    ControlPlaneErrorCodes.IntakeUnknownKey,
                    $"nonSecretParameters contains keys that are not accepted intake values: {echoed}. Keys are " +
                    "case-sensitive; the accepted set is listed in `acceptedKeys`. A value one handler produces for " +
                    "another is never a run parameter.",
                    new Dictionary<string, object?>
                    {
                        ["acceptedKeys"] = IntakeParameterCatalog.All.Keys.Order(StringComparer.Ordinal).ToArray(),
                    });
            }

            if (request.NonSecretParameters.TryGetValue(IntakeParameterCatalog.EnvironmentName, out var environmentNameValue)
                && !IntakeParameterCatalog.AllowedEnvironmentNames.Contains(environmentNameValue ?? string.Empty))
            {
                return BadRequest(httpContext, ControlPlaneErrorCodes.IntakeInvalidEnvironmentName,
                    $"nonSecretParameters['{IntakeParameterCatalog.EnvironmentName}'] is '{environmentNameValue}'; " +
                    $"allowed values are {string.Join(" | ", IntakeParameterCatalog.AllowedEnvironmentNames.Order(StringComparer.Ordinal))} " +
                    $"(customer.bicep environmentName). Omit it for '{IntakeParameterCatalog.DefaultEnvironmentName}'.");
            }

            // T218b: managed by default, unmanaged only on explicit instruction (ADR-027 §3, owner D8).
            if (request.NonSecretParameters.TryGetValue(IntakeParameterCatalog.SolutionPackageType, out var packageTypeValue)
                && !IntakeParameterCatalog.AllowedSolutionPackageTypes.Contains(packageTypeValue ?? string.Empty))
            {
                return BadRequest(httpContext, ControlPlaneErrorCodes.IntakeInvalidSolutionPackageType,
                    $"nonSecretParameters['{IntakeParameterCatalog.SolutionPackageType}'] is '{packageTypeValue}'; " +
                    $"allowed values are {IntakeParameterCatalog.ManagedSolutionPackage} | {IntakeParameterCatalog.UnmanagedSolutionPackage} " +
                    $"(exact case). Omit it for '{IntakeParameterCatalog.ManagedSolutionPackage}'.");
            }

            // T256: H7b's dry-run flag — the handler's own rule (SecureRecordSetupIntake) and its own rejection code.
            if (!SecureRecordSetupIntake.TryReadDryRun(request.NonSecretParameters, out _))
            {
                return BadRequest(httpContext, SecureRecordSetupRejectionCodes.DryRunInvalid,
                    $"nonSecretParameters['{IntakeParameterCatalog.SecureRecordSetupDryRun}'] must be 'true' or 'false' " +
                    "(exact, lower case). Omit it to apply the Secure Record setup (H7b).");
            }
        }

        // ISH-01 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
        // 2026-08-27, Wave 0 Decision 1): validate that tenantId is present in
        // nonSecretParameters. tenantId is the CANONICAL propagation path per
        // Wave 0 Decision 1 — every downstream handler reads
        // run.Parameters.NonSecret["tenantId"] to satisfy §4D I1 (no
        // hardcoded-tenant); a missing value causes the very first handler to
        // fail Resumable with missing-tenant-id, wasting the whole H0 dispatch.
        // Fail-fast at intake with a clear 400 instead of surfacing the same
        // error deep inside the DAG.
        if (request.NonSecretParameters is null
            || !request.NonSecretParameters.TryGetValue(IntakeParameterCatalog.TenantId, out var tenantIdValue)
            || string.IsNullOrWhiteSpace(tenantIdValue))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.TenantIdRequired,
                "nonSecretParameters['tenantId'] is required (§4D I1 tenant-isolation invariant). " +
                "Every downstream handler reads run.Parameters.NonSecret['tenantId']; a missing value " +
                "would fail the H0 preflight envelope with missing-tenant-id — surface at intake instead.");
        }

        // T228 (owner D4 / Q1; ADR-027 one subscription per customer): the operator creates the customer's subscription
        // and Dataverse environment; L2 creates neither and defaults neither — for EVERY tenancy model. (ISH-02 used to
        // exempt Model 1, and the skill then sent the operator's current `az account` subscription.) Intake is fixed here
        // (there is no add-parameter endpoint), so each value is checked now, before the run guard, the registry lookup,
        // any Cosmos write or enqueue.
        if (!request.NonSecretParameters.TryGetValue(IntakeParameterCatalog.SubscriptionId, out var subscriptionIdValue)
            || !Guid.TryParse(subscriptionIdValue, out var subscriptionGuid) || subscriptionGuid == Guid.Empty)
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.SubscriptionIdRequired,
                "nonSecretParameters['subscriptionId'] is required and must be the GUID of the customer's own Azure " +
                "subscription, created by the operator (ADR-027: one subscription per customer; T228). No subscription " +
                "is defaulted or shared for any tenancy model.");
        }

        // G19: H0, H4b and H8 each need the container type and would otherwise fail one by one deep in the DAG.
        if (!request.NonSecretParameters.TryGetValue(IntakeParameterCatalog.ContainerTypeId, out var containerTypeIdValue)
            || !Guid.TryParse(containerTypeIdValue, out var containerTypeGuid) || containerTypeGuid == Guid.Empty)
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.ContainerTypeIdRequired,
                "nonSecretParameters['containerTypeId'] is required and must be the GUID of the model's SPE container type " +
                "(spaarke-constants.yaml) — H0, H4b and H8 read it (G19).");
        }

        // The environment the operator created — and only one named for THIS customer (DataverseEnvironmentUrlRule:
        // all Model 1 environments share Spaarke's tenant, so a typo could otherwise name another customer's).
        request.NonSecretParameters.TryGetValue(IntakeParameterCatalog.DataverseEnvUrl, out var dataverseEnvUrlValue);
        if (!Sprk.Provisioning.ControlPlane.Core.Models.DataverseEnvironmentUrlRule.TryNormalize(
                dataverseEnvUrlValue,
                request.CustomerId,
                IntakeParameterCatalog.ResolveEnvironmentName(request.NonSecretParameters),
                out var normalizedDataverseEnvUrl,
                out var dataverseEnvUrlError))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.DataverseEnvUrlInvalid,
                $"nonSecretParameters['{IntakeParameterCatalog.DataverseEnvUrl}'] {dataverseEnvUrlError}");
        }

        // Task 245c (G25): the operator-owned values H11, H14 and H4 need, checked with the handlers' own rules.
        // Intake is fixed here (there is no add-parameter endpoint), so a value a handler would refuse must be
        // refused now — before the run guard, the registry lookup, any Cosmos write or enqueue — not after
        // H0–H10 have built the stamp.
        if (ValidateOperatorIntake(request.TenancyModel, request.NonSecretParameters) is { } intakeViolation)
        {
            return BadRequest(httpContext, intakeViolation.ErrorCode, intakeViolation.Detail);
        }

        var runId = Guid.NewGuid().ToString("D").ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;

        // Task 059 (I5 concurrency guard, spec.md FR-23): acquire BEFORE
        // writing to Cosmos so a conflict does not litter the runs container
        // with an orphaned NotStarted document. The guard is idempotent for
        // the same runId; a Conflict means a DIFFERENT run holds the guard.
        var acquire = await runGuard.TryAcquireAsync(
            request.CustomerId, runId, cancellationToken).ConfigureAwait(false);
        switch (acquire)
        {
            case AcquireResult.Conflict conflict:
                logger.LogInformation(
                    "CreateRun: 409 — same-customer serialization (I5). " +
                    "CustomerId={CustomerId} AttemptedRunId={RunId} " +
                    "WinningRunId={WinningRunId} ReasonCode={ReasonCode}",
                    request.CustomerId, runId, conflict.WinningRunId, conflict.ReasonCode);
                return ControlPlaneProblems.Create(
                    httpContext,
                    StatusCodes.Status409Conflict,
                    ControlPlaneErrorCodes.CustomerRunInFlight,
                    $"A provisioning run for customer '{request.CustomerId}' is already " +
                    $"in flight (winning runId '{conflict.WinningRunId}', reason '{conflict.ReasonCode}'). " +
                    "Cross-customer runs are unaffected — this is per-customer serialization only (spec.md §4D I5 / FR-23).",
                    new Dictionary<string, object?>
                    {
                        ["winningRunId"] = conflict.WinningRunId,
                        ["reasonCode"] = conflict.ReasonCode,
                    });

            case AcquireResult.TransientFailure txf:
                logger.LogWarning(
                    "CreateRun: 502 — CustomerRunGuard transient failure. " +
                    "CustomerId={CustomerId} AttemptedRunId={RunId} Diagnostic={Diagnostic}",
                    request.CustomerId, runId, txf.Diagnostic);
                return ControlPlaneProblems.Create(
                    httpContext,
                    StatusCodes.Status502BadGateway,
                    ControlPlaneErrorCodes.RunGuardUnavailable,
                    $"Concurrency guard could not be evaluated for customer '{request.CustomerId}': " +
                    $"{txf.Diagnostic}");
        }

        // REG-07 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
        // 2026-08-27): validate the operator-supplied environmentId against the
        // registry BEFORE writing to Cosmos. Prevents:
        //   - Unknown environmentId (typo, or Step-1f partially failed and
        //     returned a stale GUID) → H1–H12 run to completion, H13 PATCHes
        //     the wrong row (§4D I1 cross-customer bleed) or 404s with no
        //     recovery path (H13 marks Resumable but Cosmos still carries the
        //     wrong environmentId).
        //   - CustomerId mismatch → same cross-customer bleed risk.
        //   - SetupStatus != InProgress → row is already finalized (Ready)
        //     or in a rollback state; a second run should never overwrite it.
        //
        // Best-effort: a registry lookup infra fault (client throws) is treated
        // as inconclusive — CreateRun proceeds so the operator isn't blocked
        // by a transient registry outage. The concurrency guard already gates
        // dual-dispatch; H13's own row-id write will catch a wrong-row PATCH
        // as NotFound. Silent fallback lets operators complete provisioning
        // when the registry is degraded — Cosmos write + audit trail are the
        // fallback source of truth. Null-Object registry (P2 fallback per
        // ADR-032) returns null → the strict check is skipped (WARN in logs).
        try
        {
            var snapshot = await registryClient
                .LookupByEnvironmentIdAsync(request.EnvironmentId, cancellationToken)
                .ConfigureAwait(false);
            if (snapshot is not null)
            {
                if (!string.Equals(snapshot.CustomerId, request.CustomerId, StringComparison.OrdinalIgnoreCase))
                {
                    // Best-effort release the guard we just acquired so the operator
                    // can retry with the correct customerId without waiting for
                    // an idle-timeout / reconciler pass.
                    _ = await runGuard.ReleaseAsync(request.CustomerId, runId, cancellationToken).ConfigureAwait(false);
                    logger.LogWarning(
                        "CreateRun: 400 — REG-07 customerId mismatch. RequestedCustomerId={RequestedCustomerId} " +
                        "RegistryCustomerId={RegistryCustomerId} EnvironmentId={EnvironmentId}",
                        request.CustomerId, snapshot.CustomerId, request.EnvironmentId);
                    return BadRequest(httpContext, ControlPlaneErrorCodes.RegistryCustomerMismatch,
                        $"REG-07: environmentId '{request.EnvironmentId}' belongs to customer " +
                        $"'{snapshot.CustomerId}', not requested customer '{request.CustomerId}' " +
                        "(§4D I1 cross-customer bleed guard).");
                }
                if (!string.Equals(snapshot.SetupStatus, "InProgress", StringComparison.OrdinalIgnoreCase))
                {
                    _ = await runGuard.ReleaseAsync(request.CustomerId, runId, cancellationToken).ConfigureAwait(false);
                    logger.LogWarning(
                        "CreateRun: 400 — REG-07 setupStatus mismatch. RequestedCustomerId={CustomerId} " +
                        "EnvironmentId={EnvironmentId} SetupStatus={SetupStatus}",
                        request.CustomerId, request.EnvironmentId, snapshot.SetupStatus);
                    return BadRequest(httpContext, ControlPlaneErrorCodes.RegistrySetupStatusNotInProgress,
                        $"REG-07: environmentId '{request.EnvironmentId}' has setupStatus='{snapshot.SetupStatus}' " +
                        "(expected 'InProgress'). Row is already finalized or in a rollback state; " +
                        "a new run cannot overwrite it. Use clear-quarantine or an operator-side " +
                        "registry reset before retrying.");
                }
            }
            else
            {
                // Null snapshot from the real client means the row does not
                // exist (or the client is the Null-Object fallback). We only
                // hard-fail on absence when the registry lookup returned a
                // definitive 'row not found' — since LookupByEnvironmentIdAsync
                // and NullDataverseEnvironmentRegistryClient both return null,
                // we cannot distinguish here without extra flavor on the
                // outcome type. Rather than block CreateRun on the ambiguity,
                // log at Warning and let H13's own row-id PATCH catch the
                // truly-missing row (NotFound → Resumable, operator can retry
                // after Step-1f re-runs).
                logger.LogWarning(
                    "CreateRun: REG-07 registry lookup returned null for environmentId={EnvironmentId} " +
                    "(row missing OR Null-Object registry). Proceeding — H13 will fail Resumable if the row " +
                    "is truly missing.",
                    request.EnvironmentId);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Registry-lookup infra fault — do NOT block CreateRun. Cosmos +
            // audit trail are the fallback source of truth; the concurrency
            // guard prevents dual-dispatch even if the strict check was
            // skipped.
            logger.LogWarning(ex,
                "CreateRun: REG-07 registry lookup infra fault for environmentId={EnvironmentId} — proceeding without strict check.",
                request.EnvironmentId);
        }

        var run = new ProvisioningRun
        {
            RunId = runId,
            CustomerId = request.CustomerId,
            EnvironmentId = request.EnvironmentId,
            TenancyModel = request.TenancyModel,
            Profile = request.Profile,
            Status = RunStatus.NotStarted,
            CreatedOn = now,
            Parameters = new RunParameters(),
        };

        // Copy non-secret parameters into the run. Cleartext secrets are
        // structurally impossible on this endpoint — CreateRunRequest exposes
        // NonSecret (Dictionary<string,string>) only. The KeyVaultSecretRef
        // channel (run.Parameters.Secrets) has NO writer since task 245a: the
        // manifest entries that still expect one are pinned gaps in
        // RunContextContractTests, each owned by a follow-up task.
        if (request.NonSecretParameters is not null)
        {
            foreach (var kvp in request.NonSecretParameters)
            {
                run.Parameters.NonSecret[kvp.Key] = kvp.Value;
            }
        }

        // Task 245a: one stamp environment for every handler. Before, H2a/H2b defaulted a missing
        // value to "prod" on their own while H4b required it — the same run could see two answers.
        if (!run.Parameters.NonSecret.ContainsKey(IntakeParameterCatalog.EnvironmentName))
        {
            run.Parameters.NonSecret[IntakeParameterCatalog.EnvironmentName] = IntakeParameterCatalog.DefaultEnvironmentName;
        }

        // T218b: store the package type actually used, so H6 and the registry row (H13) agree with the intake.
        if (!run.Parameters.NonSecret.ContainsKey(IntakeParameterCatalog.SolutionPackageType))
        {
            run.Parameters.NonSecret[IntakeParameterCatalog.SolutionPackageType] = IntakeParameterCatalog.ManagedSolutionPackage;
        }

        // T228: canonical forms — the URL H5 compares against and hands on (https://{host}/), and the two GUIDs in the
        // bare lowercase "D" form ARM resource ids and the ContainerTypeOwners lookup expect (ADR-044). Guid.TryParse
        // above also accepts braced / N-format / padded spellings.
        run.Parameters.NonSecret[IntakeParameterCatalog.DataverseEnvUrl] = normalizedDataverseEnvUrl;
        run.Parameters.NonSecret[IntakeParameterCatalog.SubscriptionId] = subscriptionGuid.ToString("D");
        run.Parameters.NonSecret[IntakeParameterCatalog.ContainerTypeId] = containerTypeGuid.ToString("D");

        try
        {
            // §4D I3: repository requires customerId as first parameter — the
            // shape prevents a cross-partition write by construction.
            await repository.CreateRunAsync(run, cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            // 409 Conflict — a run with this id already exists in the partition.
            // Extremely unlikely given the NewGuid — surfaces only on determined
            // collision or replay attack. Best-effort release the guard we
            // just acquired so the customer can start a new run without
            // waiting for the reconciler / a terminal transition.
            logger.LogWarning(ex,
                "CreateRun: id collision (customerId={CustomerId}, runId={RunId})",
                run.CustomerId, run.RunId);
            _ = await runGuard.ReleaseAsync(run.CustomerId, run.RunId, cancellationToken).ConfigureAwait(false);
            return Conflict(httpContext, ControlPlaneErrorCodes.RunIdCollision, $"A run with id '{runId}' already exists.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Any other run-store failure (throttling, an item over Cosmos's size limit, an outage) left the I5 guard
            // held, blocking every new run for this customer until the guard went stale (found in the T245c review).
            // Release it, then let the failure surface as before.
            logger.LogError(ex,
                "CreateRun: run-store write failed — releasing the run guard (customerId={CustomerId}, runId={RunId})",
                run.CustomerId, run.RunId);
            _ = await runGuard.ReleaseAsync(run.CustomerId, run.RunId, CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        // Enqueue H0 preflight. Deterministic MessageId (FR-22 level-1) dedup's
        // duplicate submissions on the wire.
        var envelope = new HandlerEnvelope
        {
            HandlerId = InitialHandlerId,
            RunId = runId,
            CustomerId = request.CustomerId,
            ParametersJson = SerializeEnqueueParameters(new EnqueuePayload
            {
                CustomerId = request.CustomerId,
                RunId = runId,
                Action = "create-run",
            }),
            EnqueuedAt = now,
        };
        await enqueuer.EnqueueAsync(envelope, cancellationToken).ConfigureAwait(false);

        var location = $"/api/runs/{runId}?customerId={Uri.EscapeDataString(request.CustomerId)}";
        return Results.Accepted(location, new CreateRunResponse
        {
            RunId = runId,
            CustomerId = request.CustomerId,
            Status = RunStatus.NotStarted.ToString(),
            Location = location,
        });
    }

    /// <summary>
    /// POST /api/runs/{id}/preflight. Enqueues H0 preflight for an existing run.
    /// </summary>
    private static async Task<IResult> RunPreflight(
        string id,
        string? customerId,
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRouteAndPartition(id, customerId, httpContext, out var validationResult))
        {
            return validationResult;
        }

        // Verify the run exists — 404 if not.
        var read = await repository.ReadRunAsync(customerId!, id, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return NotFound(httpContext, id, customerId!);
        }

        await enqueuer.EnqueueAsync(
            new HandlerEnvelope
            {
                HandlerId = InitialHandlerId,
                RunId = id,
                CustomerId = customerId!,
                ParametersJson = SerializeEnqueueParameters(new EnqueuePayload
                {
                    CustomerId = customerId!,
                    RunId = id,
                    Action = "preflight",
                }),
                EnqueuedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"/api/runs/{id}?customerId={Uri.EscapeDataString(customerId!)}");
    }

    /// <summary>
    /// GET /api/runs/{id}. Point-reads the run from Cosmos.
    /// </summary>
    private static async Task<IResult> GetRun(
        string id,
        string? customerId,
        IProvisioningRunRepository repository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRouteAndPartition(id, customerId, httpContext, out var validationResult))
        {
            return validationResult;
        }

        // §4D I3: partition-key predicate enforced by construction.
        var read = await repository.ReadRunAsync(customerId!, id, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return NotFound(httpContext, id, customerId!);
        }
        return Results.Ok(read.Run);
    }

    /// <summary>
    /// POST /api/runs/{id}/gates/{gateId}/advance.
    /// </summary>
    private static async Task<IResult> AdvanceGate(
        string id,
        string gateId,
        string? customerId,
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRouteAndPartition(id, customerId, httpContext, out var validationResult))
        {
            return validationResult;
        }
        if (string.IsNullOrWhiteSpace(gateId))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.GateIdRequired, "gateId is required.");
        }

        var read = await repository.ReadRunAsync(customerId!, id, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return NotFound(httpContext, id, customerId!);
        }

        await enqueuer.EnqueueAsync(
            new HandlerEnvelope
            {
                HandlerId = GateAdvanceActionId,
                RunId = id,
                CustomerId = customerId!,
                ParametersJson = SerializeEnqueueParameters(new EnqueuePayload
                {
                    CustomerId = customerId!,
                    RunId = id,
                    Action = "gate-advance",
                    GateId = gateId,
                }),
                EnqueuedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"/api/runs/{id}?customerId={Uri.EscapeDataString(customerId!)}");
    }

    /// <summary>
    /// POST /api/runs/{id}/resume.
    /// </summary>
    private static async Task<IResult> ResumeRun(
        string id,
        string? customerId,
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!TryValidateRouteAndPartition(id, customerId, httpContext, out var validationResult))
        {
            return validationResult;
        }

        var read = await repository.ReadRunAsync(customerId!, id, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return NotFound(httpContext, id, customerId!);
        }

        await enqueuer.EnqueueAsync(
            new HandlerEnvelope
            {
                HandlerId = ResumeActionId,
                RunId = id,
                CustomerId = customerId!,
                ParametersJson = SerializeEnqueueParameters(new EnqueuePayload
                {
                    CustomerId = customerId!,
                    RunId = id,
                    Action = "resume",
                    CurrentPhase = read.Run.CurrentPhase,
                }),
                EnqueuedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);

        return Results.Accepted($"/api/runs/{id}?customerId={Uri.EscapeDataString(customerId!)}");
    }

    /// <summary>
    /// POST /api/runs/{id}/cancel.
    /// </summary>
    private static async Task<IResult> CancelRun(
        string id,
        string? customerId,
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        ICustomerRunGuard runGuard,
        HttpContext httpContext,
        ILogger<RunsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runGuard);

        if (!TryValidateRouteAndPartition(id, customerId, httpContext, out var validationResult))
        {
            return validationResult;
        }

        var read = await repository.ReadRunAsync(customerId!, id, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return NotFound(httpContext, id, customerId!);
        }

        await enqueuer.EnqueueAsync(
            new HandlerEnvelope
            {
                HandlerId = CancelActionId,
                RunId = id,
                CustomerId = customerId!,
                ParametersJson = SerializeEnqueueParameters(new EnqueuePayload
                {
                    CustomerId = customerId!,
                    RunId = id,
                    Action = "cancel",
                }),
                EnqueuedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);

        // Task 059 §4C: on operator-initiated cancel, best-effort release the
        // I5 guard so a fresh run can start immediately. ReleaseAsync is a
        // no-op if the guard's current value doesn't match (safe against
        // races with an already-completed cancel handler). Task 061 explicitly
        // owns the Quarantined path — see design.md §4C ("cross-customer
        // serialization on quarantine" note): a Quarantined run's guard stays
        // set until clear-quarantine, NOT released here. Our release is a
        // mismatch no-op in that case because the run's Status is Quarantined,
        // not the Cancel target.
        var release = await runGuard.ReleaseAsync(customerId!, id, cancellationToken).ConfigureAwait(false);
        if (release is ReleaseResult.TransientFailure txf)
        {
            // Not fatal to the request — the cancel envelope is already
            // enqueued. Log for observability; the operator sees 202 either
            // way and the reconciler (task 058) or a task 061-owned handler
            // will eventually converge.
            logger.LogWarning(
                "CancelRun: CustomerRunGuard release transient failure — " +
                "CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                customerId, id, txf.Diagnostic);
        }

        return Results.Accepted($"/api/runs/{id}?customerId={Uri.EscapeDataString(customerId!)}");
    }

    /// <summary>
    /// POST /api/runs/{id}/clear-quarantine. Reason parameter REQUIRED; the
    /// clearing action is audit-logged to App Insights with actor tid + oid.
    /// Task 061: wired to <see cref="IQuarantineClearService"/> which
    /// transitions Cosmos state Quarantined -> Failed +
    /// QuarantineInfo.State = Cleared + populates ClearedBy/ClearedAt (ETag-
    /// safe). The audit-log fires ONLY on a successful transition — 400 (no
    /// reason), 404 (not found), 409 (wrong-state OR concurrent-write) paths
    /// do NOT emit the QuarantineCleared record (spec FR-24 acceptance).
    /// </summary>
    private static async Task<IResult> ClearQuarantine(
        string id,
        string? customerId,
        string? reason,
        IQuarantineClearService clearService,
        IHandlerEnqueuer enqueuer,
        ICustomerRunGuard runGuard,
        HttpContext httpContext,
        ILogger<RunsMarker> logger,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runGuard);
        if (!TryValidateRouteAndPartition(id, customerId, httpContext, out var validationResult))
        {
            return validationResult;
        }
        // FR-24 acceptance: reason MUST be present + non-empty; audit-log MUST
        // NOT emit on the 400 path (only on the enqueue-successful path).
        if (string.IsNullOrWhiteSpace(reason))
        {
            return BadRequest(httpContext, ControlPlaneErrorCodes.ReasonRequired, "reason is required (spec FR-24).");
        }

        // Task 061: single source of truth for the Quarantined -> Failed
        // transition. Service does the point-read + Quarantined guard + ETag-
        // safe write; endpoint interprets the discriminated result into HTTP
        // status per POML acceptance criterion "wrong-state, missing-reason,
        // unauthorized". Missing-reason handled above; unauthorized handled by
        // AuthModule.Policies.Operator (401/403).
        var actorTid = ExtractTenantId(httpContext.User);
        var actorOid = ExtractObjectId(httpContext.User);

        var result = await clearService
            .ClearAsync(customerId!, id, reason, actorOid, cancellationToken)
            .ConfigureAwait(false);

        switch (result)
        {
            case QuarantineClearResult.NotFound:
                return NotFound(httpContext, id, customerId!);

            case QuarantineClearResult.Conflict wrongState:
                return Conflict(
                    httpContext,
                    ControlPlaneErrorCodes.RunNotQuarantined,
                    $"Run '{id}' is not in Quarantined state (current status: {wrongState.CurrentStatus}). " +
                    "clear-quarantine requires the run to be in Quarantined state (spec FR-24).");

            case QuarantineClearResult.ConcurrencyConflict concurrent:
                return Conflict(
                    httpContext,
                    ControlPlaneErrorCodes.RunConcurrentlyModified,
                    $"Run '{id}' was modified by a concurrent writer (current status: {concurrent.Current.Status}). " +
                    "Retry the clear-quarantine after re-reading the run state.");

            case QuarantineClearResult.Success:
                break; // Fall through to enqueue + audit-log below.

            default:
                throw new UnreachableException(
                    $"QuarantineClearResult exhaustive union changed: {result.GetType().FullName}");
        }

        // REG-03 (customer-provisioning-orchestration-r1 Wave 2 B24 punchlist,
        // 2026-08-27): mirror the CancelRun ReleaseAsync semantics so a fresh
        // POST /api/runs can start immediately after a clear-quarantine.
        // Without this call, sprk_currentrunid on the registry row stays
        // pointing at the (now-cleared) runId; the next POST /api/runs for
        // this customer reads the stale value, DetermineConflictReasonAsync
        // sees Failed status (not Quarantined), returns AlreadyInFlight
        // fallback, and the operator hits 409 indefinitely — with no
        // documented operator-side recovery. ReleaseAsync's stale-value guard
        // (only clears when current value matches this runId) keeps the
        // operation safe against concurrent races (parity with CancelRun).
        var release = await runGuard.ReleaseAsync(customerId!, id, cancellationToken).ConfigureAwait(false);
        if (release is ReleaseResult.TransientFailure txf)
        {
            // Not fatal to the request — the Quarantined→Failed transition
            // has already landed via clearService above; the FR-24 audit-log
            // + envelope enqueue must still fire so operators see the
            // clear-quarantine action. Log for observability so a repeated
            // failure to release surfaces before the next-run 409 loop.
            logger.LogWarning(
                "ClearQuarantine: CustomerRunGuard release transient failure (REG-03) — " +
                "CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                customerId, id, txf.Diagnostic);
        }

        // Fire-and-forget dispatch envelope so downstream consumers (log
        // subscribers, potential future audit-cleanup workers) see the action
        // on the wire. The Cosmos state transition already landed via
        // clearService.ClearAsync above; the envelope is observability-only.
        await enqueuer.EnqueueAsync(
            new HandlerEnvelope
            {
                HandlerId = ClearQuarantineActionId,
                RunId = id,
                CustomerId = customerId!,
                ParametersJson = SerializeEnqueueParameters(new EnqueuePayload
                {
                    CustomerId = customerId!,
                    RunId = id,
                    Action = "clear-quarantine",
                    Reason = reason,
                }),
                EnqueuedAt = DateTimeOffset.UtcNow,
            },
            cancellationToken).ConfigureAwait(false);

        // FR-24 audit-log — structured record with stable prefix so a Kusto
        // query can pivot on 'QuarantineCleared' independently of the general
        // AuditableAction stream. The general AuditLogMiddleware (task 039)
        // ALSO fires for this request; the two records complement (this one
        // carries the reason + a distinct event name). Only fired on success.
        logger.LogInformation(
            QuarantineClearedEventName + ": RunId={RunId} CustomerId={CustomerId} " +
            "Reason={Reason} ActorTid={ActorTid} ActorOid={ActorOid} RequestId={RequestId}",
            id,
            customerId,
            reason,
            actorTid ?? string.Empty,
            actorOid ?? string.Empty,
            httpContext.TraceIdentifier);

        return Results.Accepted($"/api/runs/{id}?customerId={Uri.EscapeDataString(customerId!)}");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// ISH-11 (customer-provisioning-orchestration-r1 Wave 5 punchlist,
    /// 2026-08-27): mirrors the intake.schema.json allOf logic — validates
    /// the tenancyModel × profile pair. Exposed <c>internal</c> so unit tests
    /// can cover the matrix directly without going through the HTTP surface.
    /// Returns true on a valid pair; returns false with a filled diagnostic
    /// string on an unknown profile, an unknown tenancyModel or a mis-paired
    /// combination (all surfaced as <c>tenancy-profile-invalid</c>).
    /// </summary>
    internal static bool TryValidateTenancyProfilePair(
        string tenancyModel,
        string profile,
        out string error)
    {
        // COMP-03: unknown-profile reject-first — before tenancy-model
        // matching so a garbage profile string surfaces its own diagnostic
        // instead of falling through to the pair-mismatch branch and blaming
        // the tenancy-model. Mirrors intake.schema.json profile enum exactly.
        if (!KnownProfiles.All.Any(p => string.Equals(p, profile, StringComparison.OrdinalIgnoreCase)))
        {
            error =
                $"Invalid profile '{profile}': must be one of [{string.Join(", ", KnownProfiles.All)}] " +
                "(intake.schema.json profile enum). Handlers hard-cast on this value; unknown profiles " +
                "are silent no-ops that surface deep in the DAG as cryptic H5/H11 failures.";
            return false;
        }

        // Task 223 (D-12, 2026-09-29): parse tenancyModel via the shared parser rather than
        // ad-hoc case-insensitive string.Equals. TryParse is CASE-SENSITIVE (H12c
        // idempotency-key format preservation constraint); pre-D-12 the API accepted
        // "model1shared" and coerced downstream, whereas H3/H1 rejected the same string.
        // Tightening at the HTTP edge closes that asymmetry — a caller sending wrong-case
        // now gets a specific 400 rather than a cryptic downstream handler failure.
        if (!Sprk.Provisioning.ControlPlane.Core.Models.TenancyModelParser.TryParse(tenancyModel, out var parsedTenancyModel))
        {
            error =
                $"Invalid tenancyModel '{tenancyModel}': must be one of [{string.Join(", ", KnownTenancyModels.All)}] " +
                "(intake.schema.json enum, case-sensitive). Handlers hard-cast on this value; unknown values " +
                "are silent no-ops with cost blow-up potential.";
            return false;
        }

        // Task 225b (D-12, G6): each tenancy model has exactly one legal profile —
        // Model1 (Spaarke-hosted dedicated stamp) ↔ spaarke-hosted-model2,
        // Model2 (customer-hosted dedicated stamp) ↔ customer-owned-model2.
        var requiredProfile = parsedTenancyModel switch
        {
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model1 => KnownProfiles.SpaarkeHostedModel2,
            Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel.Model2 => KnownProfiles.CustomerOwnedModel2,
            _ => throw new InvalidOperationException(
                $"Unhandled TenancyModel '{parsedTenancyModel}' in RunsEndpoints.ValidateTenancyProfilePair. " +
                "Add a switch arm here when the enum grows (Task 224 / Item 3 territory)."),
        };

        if (!string.Equals(profile, requiredProfile, StringComparison.OrdinalIgnoreCase))
        {
            error =
                $"Invalid tenancyModel × profile pair: '{parsedTenancyModel}' MUST pair with " +
                $"'{requiredProfile}' (received profile='{profile}'). Mirrors the intake.schema.json " +
                "tenancyModel × profile allOf invariant. Downstream handlers (H5 tier derivation, H11 user " +
                "provisioning gate) misbehave on invalid pairs — fail-fast at intake.";
            return false;
        }

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// Validates the route id + customerId query parameter. Returns true when
    /// both are non-empty; otherwise sets <paramref name="failure"/> to a 400
    /// ProblemDetails result and returns false.
    /// </summary>
    private static bool TryValidateRouteAndPartition(
        string id,
        string? customerId,
        HttpContext httpContext,
        out IResult failure)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            failure = BadRequest(httpContext, ControlPlaneErrorCodes.RunIdRequired, "runId is required.");
            return false;
        }
        if (string.IsNullOrWhiteSpace(customerId))
        {
            failure = BadRequest(httpContext, ControlPlaneErrorCodes.CustomerIdQueryRequired,
                "customerId query parameter is required (§4D I3 forbids cross-partition reads).");
            return false;
        }
        failure = Results.Empty;
        return true;
    }

    /// <summary>
    /// Task 245c: H11's identity preset + user list (<see cref="UserProvisioningIntake"/> — the code H11 itself
    /// runs; T232: Model1 takes only B2BGuest, and B2BGuest needs the environment security group), H14's Exchange scope group and "at least one Graph resource" (H14a / H14b's rules and codes), and
    /// H4's Communication default mailbox, and (task 229) H0's cost tier + estimate (<see cref="CostEnvelopeIntake"/>).
    /// <c>null</c> when the values are usable.
    /// </summary>
    internal static (string ErrorCode, string Detail)? ValidateOperatorIntake(
        string? tenancyModel, IDictionary<string, string> parameters)
    {
        parameters.TryGetValue(IntakeParameterCatalog.IdentityPreset, out var identityPreset);
        parameters.TryGetValue(IntakeParameterCatalog.UsersJson, out var usersJson);
        parameters.TryGetValue(IntakeParameterCatalog.EnvironmentSecurityGroupId, out var securityGroupId);
        if (UserProvisioningIntake.Validate(tenancyModel, identityPreset, usersJson, securityGroupId)
            is UserProvisioningIntakeOutcome.Invalid users)
        {
            return (users.RejectionCode, $"nonSecretParameters: {users.Diagnostic}");
        }

        if (IsBlank(parameters, IntakeParameterCatalog.ExchangePolicyScopeGroupId))
        {
            return (H14aRejections.MissingPolicyScopeGroupId,
                $"nonSecretParameters['{IntakeParameterCatalog.ExchangePolicyScopeGroupId}'] is required — the " +
                "mail-enabled security group that scopes the Exchange RBAC for Applications role assignments H14a creates. The " +
                "Exchange admin of the stamp's tenant (the customer's for Model 2, Spaarke's for Model 1) creates it " +
                "before the run (prerequisite PRQ-C-08).");
        }

        if (IsBlank(parameters, IntakeParameterCatalog.CommunicationGraphResource)
            && IsBlank(parameters, IntakeParameterCatalog.EmailGraphResource))
        {
            return (H14bRejections.NoWebhookTargetsConfigured,
                $"nonSecretParameters needs at least one of '{IntakeParameterCatalog.CommunicationGraphResource}' " +
                $"and '{IntakeParameterCatalog.EmailGraphResource}' — the Graph subscription resources H14b subscribes to.");
        }

        parameters.TryGetValue(IntakeParameterCatalog.CommunicationDefaultMailbox, out var mailbox);
        if (!IntakeParameterCatalog.IsMailboxAddress(mailbox))
        {
            return (ControlPlaneErrorCodes.CommunicationDefaultMailboxInvalid,
                $"nonSecretParameters['{IntakeParameterCatalog.CommunicationDefaultMailbox}'] is required and must be " +
                $"a mailbox address (local@domain.tld, at most {IntakeParameterCatalog.MaxMailboxAddressLength} characters) — " +
                "H4 writes it to the customer vault as Communication-DefaultMailbox.");
        }

        // T229: H0's cost-envelope inputs, same rules as H0 (CostEnvelopeIntake) — required for every model.
        parameters.TryGetValue(CostEnvelopeIntake.TierParameterKey, out var tier);
        parameters.TryGetValue(CostEnvelopeIntake.EstimatedMonthlyUsdParameterKey, out var estimatedMonthlyUsd);
        if (CostEnvelopeIntake.Validate(tier, estimatedMonthlyUsd) is CostEnvelopeIntakeOutcome.Invalid cost)
        {
            return (cost.RejectionCode, $"nonSecretParameters: {cost.Diagnostic}");
        }

        // T254: the OPTIONAL OpenAI spend limit (G37) — absent = no limit; present must be a usable limit, because H4b
        // writes it verbatim and the BFF would read a bad value as no limit.
        parameters.TryGetValue(IntakeParameterCatalog.OpenAiMonthlyLimitUsd, out var openAiLimit);
        if (Sprk.Provisioning.ControlPlane.Core.Models.OpenAiMonthlyLimitRule.Validate(openAiLimit)
            is Sprk.Provisioning.ControlPlane.Core.Models.OpenAiMonthlyLimitOutcome.Invalid limit)
        {
            return (limit.RejectionCode, $"nonSecretParameters: {limit.Diagnostic}");
        }

        return null;

        static bool IsBlank(IDictionary<string, string> values, string key)
            => !values.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value);
    }

    private static IResult BadRequest(HttpContext httpContext, string errorCode, string detail) =>
        ControlPlaneProblems.BadRequest(httpContext, errorCode, detail);

    private static IResult NotFound(HttpContext httpContext, string runId, string customerId) =>
        ControlPlaneProblems.NotFound(httpContext, ControlPlaneErrorCodes.RunNotFound,
            $"ProvisioningRun '{runId}' not found in customer partition '{customerId}'.");

    private static IResult Conflict(HttpContext httpContext, string errorCode, string detail) =>
        ControlPlaneProblems.Conflict(httpContext, errorCode, detail);

    private static string SerializeEnqueueParameters(EnqueuePayload payload) =>
        JsonSerializer.Serialize(payload, ParametersJsonOptions);

    // Claim extraction — mirrors AuditLogMiddleware.ExtractTenantId/ExtractObjectId
    // so the QuarantineCleared record + the general AuditableAction record use
    // identical actor identities without cross-class coupling.
    internal static string? ExtractTenantId(ClaimsPrincipal? user) =>
        user?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? user?.FindFirst("tid")?.Value;

    internal static string? ExtractObjectId(ClaimsPrincipal? user) =>
        user?.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
            ?? user?.FindFirst("oid")?.Value;

    /// <summary>
    /// DTO for the POST /api/runs request body. Non-secret parameters only —
    /// cleartext secrets have no field here, and the run's KeyVaultSecretRef
    /// channel (run.Parameters.Secrets) is never written from the intake body.
    /// </summary>
    public sealed record CreateRunRequest
    {
        [JsonPropertyName("customerId")]
        public string CustomerId { get; init; } = string.Empty;

        [JsonPropertyName("environmentId")]
        public string EnvironmentId { get; init; } = string.Empty;

        /// <summary>Values: the string members of <see cref="Sprk.Provisioning.ControlPlane.Core.Models.TenancyModel"/> (case-sensitive per Task 223 D-12) — post-T224 rename: <c>Model1</c> | <c>Model2</c>. Serialized string on <see cref="ProvisioningRun.TenancyModel"/>.</summary>
        [JsonPropertyName("tenancyModel")]
        public string TenancyModel { get; init; } = string.Empty;

        /// <summary>Values: <c>spaarke-hosted-model2</c> (pairs with <c>Model1</c>) | <c>customer-owned-model2</c> (pairs with <c>Model2</c>).</summary>
        [JsonPropertyName("profile")]
        public string Profile { get; init; } = string.Empty;

        /// <summary>
        /// Intake values for the run — keys must be in <see cref="IntakeParameterCatalog"/> (closed set,
        /// case-sensitive; anything else is a 400 <c>intake-unknown-key</c>). <c>tenantId</c>, <c>subscriptionId</c>,
        /// <c>containerTypeId</c> and <c>dataverseEnvUrl</c> are required for every model (T228).
        /// </summary>
        [JsonPropertyName("nonSecretParameters")]
        public IDictionary<string, string>? NonSecretParameters { get; init; }
    }

    /// <summary>Response body for POST /api/runs — the 202 Accepted payload.</summary>
    public sealed record CreateRunResponse
    {
        [JsonPropertyName("runId")]
        public string RunId { get; init; } = string.Empty;

        [JsonPropertyName("customerId")]
        public string CustomerId { get; init; } = string.Empty;

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("location")]
        public string Location { get; init; } = string.Empty;
    }

    /// <summary>
    /// Serialized shape of the ParametersJson body enqueued via IHandlerEnqueuer.
    /// The reconciler (task 058) + downstream handlers deserialize this. Optional
    /// fields are omitted when null (see <see cref="ParametersJsonOptions"/>).
    /// </summary>
    internal sealed record EnqueuePayload
    {
        [JsonPropertyName("customerId")]
        public string CustomerId { get; init; } = string.Empty;

        [JsonPropertyName("runId")]
        public string RunId { get; init; } = string.Empty;

        [JsonPropertyName("action")]
        public string Action { get; init; } = string.Empty;

        [JsonPropertyName("gateId")]
        public string? GateId { get; init; }

        [JsonPropertyName("currentPhase")]
        public string? CurrentPhase { get; init; }

        [JsonPropertyName("reason")]
        public string? Reason { get; init; }
    }
}

/// <summary>
/// Marker type used solely so <see cref="ILogger{TCategoryName}"/> resolves
/// with a stable category name for RunsEndpoints — <c>ILogger&lt;RunsEndpoints&gt;</c>
/// would work but <see cref="RunsEndpoints"/> is a static class (illegal as a
/// generic argument in some analyzer configurations). Kept internal.
/// </summary>
internal sealed class RunsMarker { }
