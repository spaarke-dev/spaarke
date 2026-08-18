// -----------------------------------------------------------------------------
// IQuarantineClearService.cs
//
// L2 CONTROL-PLANE clear-quarantine mutation seam. Owns the transition
// Quarantined -> Failed (post-quarantine) + QuarantineInfo.State = Cleared +
// ClearedBy/ClearedAt population + ETag-safe Cosmos write.
//
// SPEC / DESIGN references:
//   - spec.md FR-24: POST /api/runs/{id}/clear-quarantine REQUIRES a reason;
//                    audit-logged to App Insights; releases the same-customer
//                    serialization guard (via task 059 hook) so a new run can
//                    start against the same customerId.
//   - design.md §4C: Quarantined -> Cleared quarantine sub-state; run overall
//                    status transitions Quarantined -> Failed (operator may
//                    then POST /api/runs/{id}/resume, POST /api/runs/{id}/cancel,
//                    or invoke Decommission-Customer.ps1).
//
// SEPARATION OF CONCERNS:
//   The endpoint (Api/RunsEndpoints.cs) owns validation + audit-log emission +
//   HTTP status mapping. This service owns the Cosmos state transition +
//   ETag-race handling. The audit-log stays at the endpoint layer (per the
//   task 057 shape) so the same log record captures the actor claims populated
//   by UseAuthentication + the FR-24 required Reason.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <summary>
/// Encapsulates the Quarantined -> Failed transition for the
/// <c>POST /api/runs/{id}/clear-quarantine</c> endpoint (spec FR-24).
/// </summary>
public interface IQuarantineClearService
{
    /// <summary>
    /// Verifies the run exists in the customer's partition + is in
    /// <see cref="RunStatus.Quarantined"/> state, then transitions:
    /// <list type="bullet">
    ///   <item><see cref="ProvisioningRun.Status"/>     = <see cref="RunStatus.Failed"/> (post-quarantine)</item>
    ///   <item><see cref="QuarantineInfo.State"/>       = <see cref="QuarantineState.Cleared"/></item>
    ///   <item><see cref="QuarantineInfo.ClearedBy"/>   = <paramref name="actorObjectId"/> (may be null when the caller supplies no OID)</item>
    ///   <item><see cref="QuarantineInfo.ClearedAt"/>   = now (from <see cref="TimeProvider.GetUtcNow"/>)</item>
    /// </list>
    /// Persists via <see cref="Repositories.IProvisioningRunRepository.ReplaceRunAsync"/>
    /// with the ETag returned by <see cref="Repositories.IProvisioningRunRepository.ReadRunAsync"/>.
    /// </summary>
    /// <param name="customerId">Partition-key value. Required (§4D I3).</param>
    /// <param name="runId">Run document id.</param>
    /// <param name="reason">Operator-supplied justification (FR-24 required). Non-empty.</param>
    /// <param name="actorObjectId">JWT <c>oid</c> claim of the operator invoking clear-quarantine. Null when unauthenticated (test path). Persisted as <see cref="QuarantineInfo.ClearedBy"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// <see cref="QuarantineClearResult.Success"/> on happy path;
    /// <see cref="QuarantineClearResult.NotFound"/> if the run does not exist in the partition;
    /// <see cref="QuarantineClearResult.Conflict"/> if the run is not in <see cref="RunStatus.Quarantined"/>;
    /// <see cref="QuarantineClearResult.ConcurrencyConflict"/> if a concurrent writer advanced the document between read + write.
    /// </returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="customerId"/> / <paramref name="runId"/> / <paramref name="reason"/> is null or whitespace.</exception>
    Task<QuarantineClearResult> ClearAsync(
        string customerId,
        string runId,
        string reason,
        string? actorObjectId,
        CancellationToken cancellationToken);
}

/// <summary>
/// Discriminated outcome of <see cref="IQuarantineClearService.ClearAsync"/>.
/// Exhaustive: <see cref="Success"/> | <see cref="NotFound"/> |
/// <see cref="Conflict"/> | <see cref="ConcurrencyConflict"/>. Callers pattern-
/// match to decide the HTTP status the endpoint returns — the service never
/// throws on domain-outcome paths.
/// </summary>
public abstract record QuarantineClearResult
{
    private QuarantineClearResult() { }

    /// <summary>The transition succeeded. <paramref name="Run"/> is the freshly persisted run.</summary>
    public sealed record Success(ProvisioningRun Run) : QuarantineClearResult;

    /// <summary>No run with the given id exists in the customer's partition (404).</summary>
    public sealed record NotFound() : QuarantineClearResult;

    /// <summary>Run exists but is not in <see cref="RunStatus.Quarantined"/> (409 — wrong state).</summary>
    public sealed record Conflict(RunStatus CurrentStatus) : QuarantineClearResult;

    /// <summary>ETag mismatch — a concurrent writer advanced the document between read + write (409 — concurrent write).</summary>
    public sealed record ConcurrencyConflict(ProvisioningRun Current) : QuarantineClearResult;
}
