// -----------------------------------------------------------------------------
// QuarantineClearService.cs
//
// Default <see cref="IQuarantineClearService"/> impl over
// <see cref="IProvisioningRunRepository"/>.
//
// FLOW:
//   1. Point-read the run in the customer partition (§4D I3 enforced by the
//      repository interface shape). null -> NotFound.
//   2. Verify run.Status == Quarantined. Otherwise -> Conflict(CurrentStatus).
//      Idempotent-clear semantics: a run already in Quarantine.State=Cleared
//      but still Status=Quarantined is treated as a fresh clear (re-write with
//      new ClearedBy/ClearedAt); the endpoint may distinguish via audit-log
//      timestamp. A run whose Status transitioned Quarantined -> Failed by
//      prior clear returns Conflict(RunStatus.Failed) — one-shot semantic.
//   3. Mutate in-memory: Status=Failed, Quarantine.State=Cleared,
//      Quarantine.ClearedBy=actorOid, Quarantine.ClearedAt=now.
//   4. ReplaceRunAsync with ETag. NotFound -> NotFound; Conflict ->
//      ConcurrencyConflict(current); Success -> Success(run).
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <inheritdoc/>
public sealed class QuarantineClearService : IQuarantineClearService
{
    private readonly IProvisioningRunRepository _repository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QuarantineClearService> _logger;

    /// <summary>
    /// Constructs the service. All collaborators required — no optional
    /// dependencies (ADR-010 DI minimalism).
    /// </summary>
    public QuarantineClearService(
        IProvisioningRunRepository repository,
        TimeProvider timeProvider,
        ILogger<QuarantineClearService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<QuarantineClearResult> ClearAsync(
        string customerId,
        string runId,
        string reason,
        string? actorObjectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        // Step 1 — point-read (§4D I3: partition-key first per repository shape).
        var read = await _repository.ReadRunAsync(customerId, runId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogInformation(
                "QuarantineClear: run not found. CustomerId={CustomerId} RunId={RunId}",
                customerId, runId);
            return new QuarantineClearResult.NotFound();
        }

        // Step 2 — verify Quarantined state.
        if (read.Run.Status != RunStatus.Quarantined)
        {
            _logger.LogInformation(
                "QuarantineClear: wrong state. CustomerId={CustomerId} RunId={RunId} CurrentStatus={CurrentStatus}",
                customerId, runId, read.Run.Status);
            return new QuarantineClearResult.Conflict(read.Run.Status);
        }

        // Step 3 — mutate in-memory. Preserve existing QuarantineInfo fields
        // (Reason, QuarantinedByHandler, QuarantinedAt) so the audit trail
        // retains WHY the quarantine originally fired + WHO cleared it.
        var now = _timeProvider.GetUtcNow();
        read.Run.Status = RunStatus.Failed;
        read.Run.CompletedOn = now;
        if (read.Run.Quarantine is not null)
        {
            read.Run.Quarantine.State = QuarantineState.Cleared;
            read.Run.Quarantine.ClearedBy = actorObjectId;
            read.Run.Quarantine.ClearedAt = now;
        }
        else
        {
            // Defensive: run.Status = Quarantined but Quarantine metadata
            // missing — synthesize a minimal record so downstream tooling
            // (audit queries, Kusto dashboards) sees non-null ClearedBy/At.
            read.Run.Quarantine = new QuarantineInfo
            {
                State = QuarantineState.Cleared,
                Reason = reason,
                QuarantinedAt = now,
                ClearedBy = actorObjectId,
                ClearedAt = now,
            };
        }

        // Step 4 — persist with ETag optimistic concurrency (FR-23 I5).
        var replace = await _repository
            .ReplaceRunAsync(read.Run, read.ETag, cancellationToken)
            .ConfigureAwait(false);

        return replace switch
        {
            ReplaceRunResult.Success s => new QuarantineClearResult.Success(s.Run),
            ReplaceRunResult.Conflict c => new QuarantineClearResult.ConcurrencyConflict(c.Current.Run),
            ReplaceRunResult.NotFound  => new QuarantineClearResult.NotFound(),
            _ => throw new System.Diagnostics.UnreachableException(
                "ReplaceRunResult exhaustive union changed — update QuarantineClearService."),
        };
    }
}
