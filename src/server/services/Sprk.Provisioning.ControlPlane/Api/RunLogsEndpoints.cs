// -----------------------------------------------------------------------------
// RunLogsEndpoints.cs
//
// L2 CONTROL-PLANE REST API — /api/runs/{id}/phases/{phaseId}/logs surface
// (task 057, Wave C5, 8th of 8 L2 endpoints per spec §4.2).
//
// PLACEMENT (why separate from RunsEndpoints):
//   Kept in its own file so /swagger's tag grouping cleanly splits the "Runs"
//   surface (7 endpoints under /api/runs) from the "RunLogs" surface (this
//   one endpoint) — parity with the BFF's per-tag file organization
//   (RegistrationEndpoints vs PermissionsEndpoints etc.). Also keeps the
//   sole read-only endpoint from creating log noise in RunsEndpoints' large
//   MapGroup composition.
//
// SPEC / DESIGN references:
//   - spec.md FR-21:  All 9 L2 REST endpoints per §4.2 exposed — this file
//                     ships the 8th (the 9th is the BFF-side H0.5 consent
//                     callback, D18).
//   - spec.md FR-20:  Reader app-role required for GET endpoints.
//   - spec.md §4D I3 / FR-30: Cosmos partition-key predicate discipline —
//                     the ?customerId= query parameter is REQUIRED.
//   - design.md §4.2 endpoint table row 6: "GET /api/runs/{id}/phases/{phaseId}/logs — Reader
//                     Return logs/output for a specific phase".
//
// SCOPE (this task):
//   - GET /api/runs/{id}/phases/{phaseId}/logs
//
//   Implementation reads the ProvisioningRun.CompletedPhases collection from
//   Cosmos and returns the CompletedPhase record for the given phase id
//   (H0, H0.5, H1, H2a, H2b, H3, H4, H5, H6, H7, H8, H10, H11, H12a, H12b,
//   H12c, H13, H14, H14a, H14b, H14c per design.md §4.1 catalog). A phase in
//   flight OR not yet reached returns 404. Rich per-handler execution logs
//   (stdout / stderr from the Bicep runner, Package Deployer output, etc.)
//   live in App Insights `traces` and are queryable by RunId + Phase; this
//   endpoint surfaces only the structured summary Cosmos owns (start/complete
//   timestamps, idempotency key, job id) — the App Insights link is where
//   operators pivot for the full log stream.
//
// ADR references:
//   - ADR-004 (Path A): reads the L2 orchestration state store.
//   - ADR-010:          extension-method registration; no new DI here.
//   - ADR-032:          unconditional — no feature-gate branches.
//
// STATE-TRANSITION SCOPE (this task):
//   Read-only — this endpoint never mutates. If a phase is IN FLIGHT
//   (CurrentPhase == phaseId but no matching CompletedPhases entry), the
//   response is 404 with a body indicating in-flight status. Operators use
//   GET /api/runs/{id} for the aggregate view + this endpoint for per-phase
//   deep-dive.
// -----------------------------------------------------------------------------

using System.Text.Json.Serialization;
using Sprk.Provisioning.ControlPlane.Modules;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Api;

/// <summary>
/// L2 REST API endpoint for phase-scoped log/output retrieval. The
/// counterpart to <see cref="RunsEndpoints"/>; separated for /swagger tag
/// clarity + single-purpose file discipline.
/// </summary>
public static class RunLogsEndpoints
{
    /// <summary>Maps the phase-logs endpoint onto the application.</summary>
    public static IEndpointRouteBuilder MapRunLogsEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/api/runs").WithTags("RunLogs");

        group.MapGet("/{id}/phases/{phaseId}/logs", GetPhaseLogs)
            .RequireAuthorization(AuthModule.Policies.Reader)
            .WithName("GetPhaseLogs")
            .WithSummary("Return the completed-phase record for a specific handler execution")
            .WithDescription(
                "Reads ProvisioningRun.CompletedPhases from Cosmos and returns " +
                "the entry for the given phaseId (H0, H2a, H12b, ...). REQUIRES " +
                "?customerId= query parameter — §4D I3 forbids cross-partition " +
                "reads. Returns 404 when the run does not exist OR the phase " +
                "has not completed yet. Rich per-handler execution logs live " +
                "in App Insights `traces` keyed by RunId + Phase.")
            .Produces<PhaseLogResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound);

        return app;
    }

    private static async Task<IResult> GetPhaseLogs(
        string id,
        string phaseId,
        string? customerId,
        IProvisioningRunRepository repository,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(id))
        {
            return BadRequest(httpContext, "runId is required.");
        }
        if (string.IsNullOrWhiteSpace(phaseId))
        {
            return BadRequest(httpContext, "phaseId is required.");
        }
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return BadRequest(httpContext,
                "customerId query parameter is required (§4D I3 forbids cross-partition reads).");
        }

        // §4D I3: partition-key predicate enforced by construction — the
        // repository takes customerId as its first parameter.
        var read = await repository.ReadRunAsync(customerId, id, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: $"ProvisioningRun '{id}' not found in customer partition '{customerId}'.",
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        // Ordinal match — HandlerIdentifier constants (H0, H2a, H12b, ...) are
        // case-sensitive in envelope routing; we honour that at the read side.
        var entry = read.Run.CompletedPhases
            .FirstOrDefault(cp => string.Equals(cp.Phase, phaseId, StringComparison.Ordinal));
        if (entry is null)
        {
            var detail = string.Equals(read.Run.CurrentPhase, phaseId, StringComparison.Ordinal)
                ? $"Phase '{phaseId}' is currently in flight — no completed-phase record yet. Poll GET /api/runs/{id} for status."
                : $"Phase '{phaseId}' has not completed for run '{id}'.";
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Not Found",
                detail: detail,
                extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });
        }

        return Results.Ok(new PhaseLogResponse
        {
            RunId = read.Run.RunId,
            CustomerId = read.Run.CustomerId,
            Phase = entry.Phase,
            StartedAt = entry.StartedAt,
            CompletedAt = entry.CompletedAt,
            IdempotencyKey = entry.IdempotencyKey,
            JobId = entry.JobId,
            AppInsightsQueryHint =
                $"traces | where operation_ParentId startswith \"{entry.JobId}\" or " +
                $"customDimensions.RunId == \"{read.Run.RunId}\" and " +
                $"customDimensions.HandlerId == \"{entry.Phase}\"",
        });
    }

    private static IResult BadRequest(HttpContext httpContext, string detail) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Bad Request",
            detail: detail,
            type: "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            extensions: new Dictionary<string, object?> { ["correlationId"] = httpContext.TraceIdentifier });

    /// <summary>
    /// Response body for GET /api/runs/{id}/phases/{phaseId}/logs. Wraps the
    /// underlying <see cref="Models.CompletedPhase"/> record with a Kusto
    /// query hint operators paste into App Insights for the full log stream.
    /// </summary>
    public sealed record PhaseLogResponse
    {
        [JsonPropertyName("runId")]
        public string RunId { get; init; } = string.Empty;

        [JsonPropertyName("customerId")]
        public string CustomerId { get; init; } = string.Empty;

        [JsonPropertyName("phase")]
        public string Phase { get; init; } = string.Empty;

        [JsonPropertyName("startedAt")]
        public DateTimeOffset StartedAt { get; init; }

        [JsonPropertyName("completedAt")]
        public DateTimeOffset CompletedAt { get; init; }

        [JsonPropertyName("idempotencyKey")]
        public string IdempotencyKey { get; init; } = string.Empty;

        [JsonPropertyName("jobId")]
        public string JobId { get; init; } = string.Empty;

        /// <summary>
        /// Suggested Azure Monitor Kusto query fragment operators paste into
        /// the App Insights UI to see the full per-handler log stream. The
        /// L2 host writes structured properties via AuditLogMiddleware +
        /// per-handler ILogger; the query pivots on both.
        /// </summary>
        [JsonPropertyName("appInsightsQueryHint")]
        public string AppInsightsQueryHint { get; init; } = string.Empty;
    }
}
