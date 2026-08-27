// -----------------------------------------------------------------------------
// QuarantineClearService.cs
//
// Default <see cref="IQuarantineClearService"/> impl over
// <see cref="IProvisioningRunRepository"/> + <see cref="ICustomerRunGuard"/>.
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
//   5. COMP-06 / ROLLBACK-1 (customer-provisioning-orchestration-r1 SESSION 17,
//      2026-08-27, Wave 0 Decision 9 REG-04 credential seam): on SUCCESS ONLY,
//      release the customer's I5 concurrency guard so a fresh POST /api/runs
//      can start immediately. The guard's ReleaseAsync is stale-value-safe
//      (only clears when current value matches this runId) so a repeat call
//      from the endpoint layer is a documented Mismatched no-op — the release
//      is idempotent. TransientFailure at release time is logged BUT NOT
//      propagated: the Cosmos state transition has already landed and the
//      audit trail exists at the endpoint layer; a transient Dataverse
//      registry outage is fixable independently. Prior to this SESSION 17
//      fix the release lived ONLY at the endpoint (REG-03) — any future
//      non-endpoint caller of ClearAsync (state-reconciler background job,
//      admin CLI) would silently leak sprk_currentrunid and put operators
//      into a permanent 409 loop with no documented recovery path.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Logging;
using Sprk.Provisioning.ControlPlane.Concurrency;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Rollback;

/// <inheritdoc/>
public sealed class QuarantineClearService : IQuarantineClearService
{
    private readonly IProvisioningRunRepository _repository;
    private readonly ICustomerRunGuard _customerRunGuard;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QuarantineClearService> _logger;

    /// <summary>
    /// Constructs the service. All collaborators required — no optional
    /// dependencies (ADR-010 DI minimalism). <paramref name="customerRunGuard"/>
    /// is added by COMP-06 (SESSION 17) so the Quarantined→Failed transition
    /// atomically releases the I5 concurrency guard as part of the SAME
    /// service call — see file header §5.
    /// </summary>
    public QuarantineClearService(
        IProvisioningRunRepository repository,
        ICustomerRunGuard customerRunGuard,
        TimeProvider timeProvider,
        ILogger<QuarantineClearService> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(customerRunGuard);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        _repository = repository;
        _customerRunGuard = customerRunGuard;
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

        switch (replace)
        {
            case ReplaceRunResult.Success s:
                // Step 5 — COMP-06 / ROLLBACK-1 (SESSION 17, Wave 0 Decision 9
                // REG-04 credential seam): release the customer's I5 guard so a
                // fresh POST /api/runs can start immediately. Idempotent-safe:
                // ReleaseAsync clears sprk_currentrunid ONLY IF the current
                // value matches this runId, so a second call from the endpoint
                // layer (belt-and-suspenders) or a stale terminal-state
                // handler is a documented Mismatched no-op. TransientFailure
                // is LOG-ONLY: the Cosmos state transition has already landed
                // — a transient Dataverse registry outage at release time is
                // not a rollback trigger; the operator observes the release
                // failure via the log and can invoke a maintenance workflow.
                var release = await _customerRunGuard
                    .ReleaseAsync(customerId, runId, cancellationToken)
                    .ConfigureAwait(false);
                switch (release)
                {
                    case ReleaseResult.Released:
                        _logger.LogInformation(
                            "QuarantineClear: sprk_currentrunid released (COMP-06). CustomerId={CustomerId} RunId={RunId}",
                            customerId, runId);
                        break;
                    case ReleaseResult.Mismatched mismatched:
                        _logger.LogInformation(
                            "QuarantineClear: sprk_currentrunid release was no-op (COMP-06 idempotent path). " +
                            "CustomerId={CustomerId} RunId={RunId} CurrentValue={CurrentValue}",
                            customerId, runId, mismatched.CurrentValue ?? "<null>");
                        break;
                    case ReleaseResult.NotFound:
                        _logger.LogWarning(
                            "QuarantineClear: registry row missing during release (COMP-06). " +
                            "CustomerId={CustomerId} RunId={RunId}",
                            customerId, runId);
                        break;
                    case ReleaseResult.TransientFailure txf:
                        // Log-only per file header §5. The Cosmos transition
                        // already landed; a repeated failure here surfaces as
                        // a stale sprk_currentrunid that operators can clear
                        // via a maintenance workflow, and the endpoint's
                        // belt-and-suspenders release will retry on the
                        // NEXT operator invocation.
                        _logger.LogWarning(
                            "QuarantineClear: sprk_currentrunid release transient failure (COMP-06 log-only). " +
                            "CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                            customerId, runId, txf.Diagnostic);
                        break;
                }
                return new QuarantineClearResult.Success(s.Run);

            case ReplaceRunResult.Conflict c:
                return new QuarantineClearResult.ConcurrencyConflict(c.Current.Run);

            case ReplaceRunResult.NotFound:
                return new QuarantineClearResult.NotFound();

            default:
                throw new System.Diagnostics.UnreachableException(
                    "ReplaceRunResult exhaustive union changed — update QuarantineClearService.");
        }
    }
}
