// -----------------------------------------------------------------------------
// CustomerRunGuard.cs
//
// L2 CONTROL-PLANE production impl of ICustomerRunGuard (task 059, Wave C5).
//
// DECISION LAYER — this class owns every I5 semantic:
//   - Idempotent re-acquire (same runId already present -> Success).
//   - Conflict detection with winning-runId + reason-code payload.
//   - Cross-check of winning-run status in Cosmos to disambiguate
//     AlreadyInFlight vs Quarantined (task 061 §4C integration).
//   - ETag-race retry (bounded loop; one re-read on 412 Precondition Failed).
//   - Release-mismatch protection (only clear when current value matches).
//   - Canonicalization of the runId at ingress (ADR-044).
//   - Kill-switch short-circuit when the guard is disabled (per
//     CustomerRunGuardOptions.Enabled false -> Success-always).
//
// The Dataverse Web API mechanics live in IRegistryConcurrencyStore
// (see IRegistryConcurrencyStore.cs for the seam justification).
//
// ETAG RACE POLICY:
//   The retry loop is bounded to 2 attempts total. Rationale: the guard sits
//   in the hot path of POST /api/runs and cross-instance contention is
//   the RARE case (single-digit runs/day per design.md §4.2). An unbounded
//   retry could pathologically thrash under a stuck-writer scenario; 2 attempts
//   is enough to absorb the typical "another L2 instance's simultaneous ACQUIRE
//   raced our ETag" case + surface persistent contention as a Conflict.
//
// LOGGING:
//   Every OUTCOME (Success, Conflict, TransientFailure) emits a structured
//   log record with stable event names so App Insights Kusto queries can
//   pivot on invariant violations vs routine conflicts. Correlation ids
//   (customerId, runId, winningRunId) are structured properties, never
//   string-interpolated.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Concurrency;

/// <summary>
/// Production <see cref="ICustomerRunGuard"/> implementation over
/// <see cref="IRegistryConcurrencyStore"/>. Owns all I5 policy; the store
/// owns Dataverse mechanics.
/// </summary>
public sealed class CustomerRunGuard : ICustomerRunGuard
{
    /// <summary>Log-event prefix for the acquire-success record.</summary>
    public const string AcquireSuccessEventName = "CustomerRunGuardAcquired";

    /// <summary>Log-event prefix for the acquire-conflict record.</summary>
    public const string AcquireConflictEventName = "CustomerRunGuardConflict";

    /// <summary>Log-event prefix for the acquire-transient-failure record.</summary>
    public const string AcquireTransientEventName = "CustomerRunGuardAcquireTransient";

    /// <summary>Log-event prefix for the release-success record.</summary>
    public const string ReleaseSuccessEventName = "CustomerRunGuardReleased";

    /// <summary>Log-event prefix for the release-mismatch record.</summary>
    public const string ReleaseMismatchEventName = "CustomerRunGuardReleaseMismatch";

    /// <summary>
    /// Bounded ETag-race retry attempts. See file header ETAG RACE POLICY.
    /// </summary>
    private const int MaxAcquireAttempts = 2;

    private readonly IRegistryConcurrencyStore _store;
    private readonly IProvisioningRunRepository _runRepository;
    private readonly CustomerRunGuardOptions _options;
    private readonly ILogger<CustomerRunGuard> _logger;

    /// <summary>
    /// Constructs the guard bound to the store + run repository + options + logger.
    /// The run repository is used ONLY to cross-check a winning run's status
    /// so <see cref="AcquireConflictReasonCodes.Quarantined"/> can be returned
    /// when the winner is Quarantined (task 061 §4C integration hook).
    /// </summary>
    public CustomerRunGuard(
        IRegistryConcurrencyStore store,
        IProvisioningRunRepository runRepository,
        IOptions<CustomerRunGuardOptions> options,
        ILogger<CustomerRunGuard> logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runRepository);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _store = store;
        _runRepository = runRepository;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<AcquireResult> TryAcquireAsync(
        string customerId,
        string runId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var canonicalRunId = CanonicalizeRunId(runId);

        // Kill-switch (ADR-032): when the guard is disabled, return Success
        // unconditionally so the endpoint layer stays hot without a hard
        // dependency on the admin Dataverse env. Logged at WARN so operators
        // notice a production deployment left the guard disabled.
        if (!_options.Enabled)
        {
            _logger.LogWarning(
                "CustomerRunGuard disabled (CustomerRunGuard:Enabled=false) — I5 concurrency guard is NOT enforced. " +
                "CustomerId={CustomerId} RunId={RunId}. Set Enabled=true after configuring Dataverse credentials.",
                customerId, canonicalRunId);
            return new AcquireResult.Success(customerId, canonicalRunId);
        }

        for (var attempt = 1; attempt <= MaxAcquireAttempts; attempt++)
        {
            // (1) Read current state + ETag.
            var lookup = await _store.LookupAsync(customerId, cancellationToken).ConfigureAwait(false);
            switch (lookup)
            {
                case LookupOutcome.NotFound:
                    var missingDiagnostic =
                        $"sprk_dataverseenvironment row not found for customerId='{customerId}'. " +
                        "Registry row must exist before a provisioning run is created.";
                    _logger.LogWarning(
                        AcquireTransientEventName +
                        ": CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                        customerId, canonicalRunId, missingDiagnostic);
                    return new AcquireResult.TransientFailure(customerId, canonicalRunId, missingDiagnostic);

                case LookupOutcome.TransientFailure tf:
                    _logger.LogWarning(
                        AcquireTransientEventName +
                        ": CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                        customerId, canonicalRunId, tf.Diagnostic);
                    return new AcquireResult.TransientFailure(customerId, canonicalRunId, tf.Diagnostic);

                case LookupOutcome.Found found:
                    // (2a) Idempotent re-acquire — same runId already present.
                    if (!string.IsNullOrWhiteSpace(found.CurrentRunId) &&
                        RunIdEquals(found.CurrentRunId!, canonicalRunId))
                    {
                        _logger.LogInformation(
                            AcquireSuccessEventName + ": Idempotent re-acquire — " +
                            "CustomerId={CustomerId} RunId={RunId}",
                            customerId, canonicalRunId);
                        return new AcquireResult.Success(customerId, canonicalRunId);
                    }

                    // (2b) Conflict — a different run holds the guard.
                    if (!string.IsNullOrWhiteSpace(found.CurrentRunId))
                    {
                        var winning = CanonicalizeRunId(found.CurrentRunId!);
                        var reasonCode = await DetermineConflictReasonAsync(
                            customerId, winning, cancellationToken).ConfigureAwait(false);
                        _logger.LogInformation(
                            AcquireConflictEventName +
                            ": CustomerId={CustomerId} AttemptedRunId={RunId} " +
                            "WinningRunId={WinningRunId} ReasonCode={ReasonCode}",
                            customerId, canonicalRunId, winning, reasonCode);
                        return new AcquireResult.Conflict(customerId, winning, reasonCode);
                    }

                    // (2c) Column is null — attempt the claim.
                    var write = await _store.TrySetIfNullAsync(
                        found.EnvironmentRowId, canonicalRunId, found.ETag, cancellationToken).ConfigureAwait(false);

                    switch (write)
                    {
                        case WriteOutcome.Success:
                            _logger.LogInformation(
                                AcquireSuccessEventName +
                                ": CustomerId={CustomerId} RunId={RunId}",
                                customerId, canonicalRunId);
                            return new AcquireResult.Success(customerId, canonicalRunId);

                        case WriteOutcome.PreconditionFailed:
                            // Another instance won the race between our read + write.
                            // Loop back to re-lookup. Bounded by MaxAcquireAttempts.
                            _logger.LogInformation(
                                "CustomerRunGuard acquire ETag race — re-lookup (attempt {Attempt}/{Max}). " +
                                "CustomerId={CustomerId} RunId={RunId}",
                                attempt, MaxAcquireAttempts, customerId, canonicalRunId);
                            continue;

                        case WriteOutcome.NotFound:
                            var noRow =
                                $"sprk_dataverseenvironment row {found.EnvironmentRowId} disappeared between lookup and PATCH.";
                            _logger.LogWarning(
                                AcquireTransientEventName +
                                ": CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                                customerId, canonicalRunId, noRow);
                            return new AcquireResult.TransientFailure(customerId, canonicalRunId, noRow);

                        case WriteOutcome.TransientFailure txf:
                            _logger.LogWarning(
                                AcquireTransientEventName +
                                ": CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                                customerId, canonicalRunId, txf.Diagnostic);
                            return new AcquireResult.TransientFailure(customerId, canonicalRunId, txf.Diagnostic);
                    }
                    break;
            }
        }

        // Exhausted retries on ETag races — return a Conflict pointing to
        // whoever won. Best-effort re-lookup to name the winner.
        var finalLookup = await _store.LookupAsync(customerId, cancellationToken).ConfigureAwait(false);
        if (finalLookup is LookupOutcome.Found finalFound &&
            !string.IsNullOrWhiteSpace(finalFound.CurrentRunId))
        {
            var winner = CanonicalizeRunId(finalFound.CurrentRunId!);
            var reasonCode = await DetermineConflictReasonAsync(
                customerId, winner, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                AcquireConflictEventName +
                ": CustomerId={CustomerId} AttemptedRunId={RunId} " +
                "WinningRunId={WinningRunId} ReasonCode={ReasonCode} (post-retry-exhaustion)",
                customerId, canonicalRunId, winner, reasonCode);
            return new AcquireResult.Conflict(customerId, winner, reasonCode);
        }

        // Retries exhausted AND no winner visible — surface as transient so
        // the endpoint returns 502 (not 409) and the operator investigates.
        var exhaustedDiagnostic =
            $"CustomerRunGuard exhausted {MaxAcquireAttempts} ETag-race attempts without a definitive outcome " +
            $"for customerId='{customerId}' runId='{canonicalRunId}'.";
        _logger.LogWarning(
            AcquireTransientEventName +
            ": CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
            customerId, canonicalRunId, exhaustedDiagnostic);
        return new AcquireResult.TransientFailure(customerId, canonicalRunId, exhaustedDiagnostic);
    }

    /// <inheritdoc/>
    public async Task<ReleaseResult> ReleaseAsync(
        string customerId,
        string runId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(runId);

        var canonicalRunId = CanonicalizeRunId(runId);

        // Kill-switch parity with TryAcquireAsync.
        if (!_options.Enabled)
        {
            _logger.LogWarning(
                "CustomerRunGuard disabled — ReleaseAsync is a no-op. " +
                "CustomerId={CustomerId} RunId={RunId}.",
                customerId, canonicalRunId);
            return new ReleaseResult.Released(customerId, canonicalRunId);
        }

        var lookup = await _store.LookupAsync(customerId, cancellationToken).ConfigureAwait(false);
        switch (lookup)
        {
            case LookupOutcome.NotFound:
                _logger.LogInformation(
                    ReleaseMismatchEventName + ": Registry row not found — " +
                    "CustomerId={CustomerId} RunId={RunId}",
                    customerId, canonicalRunId);
                return new ReleaseResult.NotFound(customerId, canonicalRunId);

            case LookupOutcome.TransientFailure tf:
                _logger.LogWarning(
                    "CustomerRunGuard release lookup transient failure — " +
                    "CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                    customerId, canonicalRunId, tf.Diagnostic);
                return new ReleaseResult.TransientFailure(customerId, canonicalRunId, tf.Diagnostic);

            case LookupOutcome.Found found:
                // Protect against stale caller clearing a different run.
                if (string.IsNullOrWhiteSpace(found.CurrentRunId) ||
                    !RunIdEquals(found.CurrentRunId!, canonicalRunId))
                {
                    _logger.LogInformation(
                        ReleaseMismatchEventName +
                        ": CustomerId={CustomerId} RunId={RunId} CurrentValue={CurrentValue}",
                        customerId, canonicalRunId, found.CurrentRunId ?? "(null)");
                    return new ReleaseResult.Mismatched(customerId, canonicalRunId, found.CurrentRunId);
                }

                var write = await _store.TryClearAsync(
                    found.EnvironmentRowId, found.ETag, cancellationToken).ConfigureAwait(false);
                switch (write)
                {
                    case WriteOutcome.Success:
                        _logger.LogInformation(
                            ReleaseSuccessEventName +
                            ": CustomerId={CustomerId} RunId={RunId}",
                            customerId, canonicalRunId);
                        return new ReleaseResult.Released(customerId, canonicalRunId);

                    case WriteOutcome.PreconditionFailed:
                        // Someone changed the row between our read + clear.
                        // Re-read to surface the current state.
                        var recheck = await _store.LookupAsync(customerId, cancellationToken).ConfigureAwait(false);
                        var currentValue = recheck is LookupOutcome.Found refresh ? refresh.CurrentRunId : null;
                        _logger.LogInformation(
                            ReleaseMismatchEventName +
                            ": ETag race — CustomerId={CustomerId} RunId={RunId} CurrentValue={CurrentValue}",
                            customerId, canonicalRunId, currentValue ?? "(null)");
                        return new ReleaseResult.Mismatched(customerId, canonicalRunId, currentValue);

                    case WriteOutcome.NotFound:
                        return new ReleaseResult.NotFound(customerId, canonicalRunId);

                    case WriteOutcome.TransientFailure txf:
                        _logger.LogWarning(
                            "CustomerRunGuard release write transient failure — " +
                            "CustomerId={CustomerId} RunId={RunId} Diagnostic={Diagnostic}",
                            customerId, canonicalRunId, txf.Diagnostic);
                        return new ReleaseResult.TransientFailure(customerId, canonicalRunId, txf.Diagnostic);
                }
                break;
        }

        // Defensive default — unreachable if store impls honor the union.
        return new ReleaseResult.TransientFailure(customerId, canonicalRunId,
            "Unexpected store outcome shape (CustomerRunGuard.ReleaseAsync fell through).");
    }

    /// <summary>
    /// Cross-checks the winning run's Cosmos status to disambiguate
    /// <see cref="AcquireConflictReasonCodes.AlreadyInFlight"/> from
    /// <see cref="AcquireConflictReasonCodes.Quarantined"/>. Task 061 §4C
    /// integration point — the winning run's Status is the source of truth
    /// (design.md §4C: "sprk_currentrunid stays set while status is
    /// Quarantined — blocks new runs against the same customer until the
    /// operator explicitly clears").
    ///
    /// A read failure or missing run doc is not fatal here — we degrade to
    /// AlreadyInFlight (the default) so a Cosmos hiccup during conflict
    /// resolution doesn't mask the concurrency signal. The endpoint still
    /// returns 409 either way; the reason code is UX-only for the operator.
    /// </summary>
    private async Task<string> DetermineConflictReasonAsync(
        string customerId,
        string winningRunId,
        CancellationToken cancellationToken)
    {
        try
        {
            var read = await _runRepository.ReadRunAsync(
                customerId, winningRunId, cancellationToken).ConfigureAwait(false);
            if (read is not null && read.Run.Status == RunStatus.Quarantined)
            {
                return AcquireConflictReasonCodes.Quarantined;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CustomerRunGuard failed to read winning run doc for Quarantine cross-check — " +
                "degrading to AlreadyInFlight. CustomerId={CustomerId} WinningRunId={WinningRunId}",
                customerId, winningRunId);
        }
        return AcquireConflictReasonCodes.AlreadyInFlight;
    }

    /// <summary>
    /// ADR-044 GUID canonicalization at the guard's ingress boundary. Strips
    /// braces + trims + lowercases so a Xrm-registry-format GUID from a caller
    /// still compares equal to the store's canonical value.
    /// </summary>
    internal static string CanonicalizeRunId(string runId)
    {
        return runId.Trim().Trim('{', '}').ToLowerInvariant();
    }

    /// <summary>
    /// Compares two runIds under the canonical form. Both sides are canonicalized
    /// so a stored non-canonical value doesn't silently mismatch a canonicalized
    /// caller (defense-in-depth against a legacy write).
    /// </summary>
    internal static bool RunIdEquals(string a, string b)
    {
        return string.Equals(CanonicalizeRunId(a), CanonicalizeRunId(b), StringComparison.Ordinal);
    }
}
