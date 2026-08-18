// -----------------------------------------------------------------------------
// IQuarantineClearService.cs
//
// TASK-061 PLACEHOLDER STUB — created by task 059 dispatcher to unblock the
// L2 project build while task 061 (§4C rollback semantics) is mid-flight.
//
// STATE (2026-08-18):
//   - Api/RunsEndpoints.cs's ClearQuarantine handler was pre-wired by task
//     061's agent to inject IQuarantineClearService + switch on QuarantineClearResult.
//   - Task 061's real Rollback/IQuarantineClearService.cs + QuarantineClearService.cs
//     + FailureClassifier.cs + RollbackModule.cs never landed on disk.
//   - Task 059's build (this file's owning task) needs the shape resolved.
//
// TASK 061 OBLIGATION:
//   When task 061 completes, REPLACE this file with the real interface + DU
//   contracts (probably including the actor-oid + ETag semantics documented in
//   the file header of the extant Rollback/QuarantineClearService.cs from the
//   parallel worktree). Sibling stubs to replace: none — this file is the
//   only member of the placeholder set.
//
// SCOPE:
//   Minimum surface to compile RunsEndpoints.cs ClearQuarantine call site:
//     - IQuarantineClearService.ClearAsync(customerId, runId, reason, actorOid, ct)
//     - QuarantineClearResult discriminated union: NotFound / Conflict(CurrentStatus)
//       / ConcurrencyConflict(Current) / Success.
//   No implementation — the interface has no default implementation and no
//   consumer beyond the endpoint's compile-time reference.
// -----------------------------------------------------------------------------

using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <summary>
/// PLACEHOLDER — task 061 replaces with the real interface. Owns the Cosmos
/// Quarantined -&gt; Failed transition invoked by
/// <c>POST /api/runs/{id}/clear-quarantine</c>.
/// </summary>
public interface IQuarantineClearService
{
    /// <summary>
    /// Clears the quarantine on the specified run, transitioning
    /// <see cref="RunStatus.Quarantined"/> -&gt; <see cref="RunStatus.Failed"/>
    /// (per design.md §4C). Idempotent via ETag on the run document.
    /// </summary>
    Task<QuarantineClearResult> ClearAsync(
        string customerId,
        string runId,
        string reason,
        string? actorObjectId,
        CancellationToken cancellationToken);
}

/// <summary>
/// PLACEHOLDER discriminated result — task 061 owns the final semantics.
/// </summary>
public abstract record QuarantineClearResult
{
    private QuarantineClearResult() { }

    /// <summary>Run doc not found in the customer partition.</summary>
    public sealed record NotFound(string CustomerId, string RunId) : QuarantineClearResult;

    /// <summary>Run is not in Quarantined state — cannot clear.</summary>
    public sealed record Conflict(RunStatus CurrentStatus) : QuarantineClearResult;

    /// <summary>Concurrent write raced our ETag — caller may retry.</summary>
    public sealed record ConcurrencyConflict(ProvisioningRun Current) : QuarantineClearResult;

    /// <summary>Transition landed successfully.</summary>
    public sealed record Success(ProvisioningRun Run) : QuarantineClearResult;
}
