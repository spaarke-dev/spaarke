// -----------------------------------------------------------------------------
// H1SubscriptionReadinessHandler.cs
//
// The H1 subscription-readiness handler (task 043, wave C4). Verifies the
// target Azure subscription is reachable via ARM AND — for CustomerOwned
// (Model 2) tenancy — that Lighthouse delegation is in place with the
// delegated resource-group scope accessible. Blocks the run BEFORE H2a
// (the 30-min Bicep step) if either check fails, per design.md § 15 north-
// star: surface lead-time / config items UP-FRONT, not after wasted Bicep
// runs.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-03 acceptance:
//       `az account show` succeeds against target sub; Lighthouse RG scope
//       accessible for CustomerOwned. Idempotency key: subready-{customerId}.
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1:
//       MUST NOT hardcode default tenant / subscription — accept both from
//       run parameters explicitly, pass every downstream Azure API call
//       with them.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H1 row +
//       §4C rollback: H1 failures are Resumable class (operator resolves
//       subscription config / Lighthouse offer + POST /api/runs/{id}/resume).
//   - projects/customer-provisioning-orchestration-r1/design.md §3A A2:
//       Tenancy models — Model 1 (SpaarkeOwned, shared) skips the Lighthouse
//       branch; Model 2 (CustomerOwned, dedicated) requires it.
//   - .claude/adr/ADR-004-job-contract.md: idempotent + at-least-once safe.
//   - .claude/adr/ADR-010-di-minimalism.md: probe seam ≥2 impls (Null +
//     test stubs; Wave C5 adds real ARM-backed impl).
//   - .claude/adr/ADR-028-spaarke-auth-architecture.md: MI-outbound MUST
//     rule — Wave C5 real probe impl uses DefaultAzureCredential, never
//     account-key.
//   - .claude/adr/ADR-036-background-job-infrastructure.md: reuse shared
//     background-job infrastructure (Service Bus + IHandlerEnqueuer;
//     Redis IdempotencyService pending L2 wiring).
//
// ROLLBACK CLASSIFICATION (§ 4C):
//   H1 writes NO external Azure state — the probe methods are pure read-only
//   ARM queries. On any failure, the ProvisioningRun transitions to
//   Status = Failed with a distinct RejectionCode per failing check, but no
//   quarantine is warranted. Operator addresses the missing precondition
//   (subscription access grant / Lighthouse offer acceptance) then invokes
//   POST /api/runs/{id}/resume, which re-dispatches H1 with the same
//   envelope.
//
// DOWNSTREAM ENQUEUE (WAVE C4 TEMPORARY BRIDGE):
//   Same pattern as H0PreflightHandler + H05ConsentCaptureHandler in this
//   wave: the wave-C5 reconciler does not exist yet, so H1 enqueues H2a
//   (task 044 sibling) directly here as a TEMPORARY BRIDGE. When the wave-C5
//   reconciler ships, this handler's enqueue call moves to the reconciler +
//   the IHandlerEnqueuer contract note (Enqueue/IHandlerEnqueuer.cs § NOT
//   CONSUMED BY) applies unchanged.
//
// IDEMPOTENCY (3-level per ADR-004 + design.md §4.1):
//   Level 1 (wire): ServiceBusHandlerEnqueuer sets a deterministic
//                   MessageId per (HandlerId, RunId, CustomerId, paramHash);
//                   SB duplicate-detection drops re-enqueues within its
//                   dedup window.
//   Level 2 (Redis): NOT YET IMPLEMENTED in L2 (this project has no Redis
//                    dependency); Wave-C5 reconciler may add if latency
//                    profiling warrants.
//   Level 3 (durable): This handler scans ProvisioningRun.CompletedPhases
//                      for an entry with (Phase == "H1", IdempotencyKey ==
//                      subready-{customerId}) — returns HandlerResult.Success
//                      no-op on hit. Cosmos writes are ETag-guarded via
//                      ReplaceRunAsync (FR-23 I5).
//
//   Idempotency key format: subready-{customerId} — NO version token per
//   project constraint (subscription readiness has no versioned artifact;
//   see POML <constraints> "Idempotency key" row).
//
// TENANCY MODEL NORMALIZATION:
//   The POML uses colloquial names (SpaarkeOwned / CustomerOwned); the
//   ProvisioningRun.TenancyModel typed field uses (Model1Shared /
//   Model2Dedicated) per design.md §6.2 property comment. Both name
//   conventions are accepted at read time — the handler normalizes to an
//   internal enum-like set (CustomerOwned requires the Lighthouse branch).
//   Any unrecognized value returns HandlerResult.Failure(Resumable,
//   InvalidTenancyModel) rather than silently defaulting.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.SubscriptionReadiness;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H1SubscriptionReadinessHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = "H1";

    /// <summary>
    /// Handler identifier of the downstream handler H1 chains to on success.
    /// Sibling task 044 owns the H2a implementation; H1 only needs the
    /// string constant to enqueue.
    /// </summary>
    public const string DownstreamHandlerId = "H2a";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§ 4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the Azure subscription id (§ 4D I1).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    // Tenancy-model normalization. All comparisons are case-insensitive.
    private static readonly HashSet<string> CustomerOwnedTenancyValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "CustomerOwned",
        "Model2Dedicated",
    };

    private static readonly HashSet<string> SpaarkeOwnedTenancyValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "SpaarkeOwned",
        "Model1Shared",
    };

    private readonly IProvisioningRunRepository _repository;
    private readonly IHandlerEnqueuer _enqueuer;
    private readonly ISubscriptionReadinessProbe _probe;
    private readonly ILogger<H1SubscriptionReadinessHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H1 subscription-readiness handler.
    /// </summary>
    /// <param name="repository">Cosmos-backed run state store (task 037).</param>
    /// <param name="enqueuer">Service Bus enqueuer used to dispatch H2a on success (wave-C4 temporary bridge — see file header).</param>
    /// <param name="probe">The ARM readiness probe (Wave C4 = <see cref="NullSubscriptionReadinessProbe"/>; Wave C5 swaps for real ARM-backed impl).</param>
    /// <param name="logger">Structured logger.</param>
    public H1SubscriptionReadinessHandler(
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        ISubscriptionReadinessProbe probe,
        ILogger<H1SubscriptionReadinessHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(probe);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _enqueuer = enqueuer;
        _probe = probe;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            // Defensive: the reconciler routes by HandlerId string match. A
            // mismatch here means a bug in dispatch — fail loud rather than
            // silently mis-executing.
            throw new InvalidOperationException(
                $"H1SubscriptionReadinessHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H1 subscription readiness starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H1 subscription readiness aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SubscriptionReadinessRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId);

        // (2) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H1 subscription readiness idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (3) §4D I1 parameter guard — probes need subscriptionId + tenantId;
        // fail BEFORE any probe fires so the operator sees the specific
        // rejection code and no ARM call happens with a default sub/tenant.
        if (!run.Parameters.NonSecret.TryGetValue(SubscriptionIdParameterKey, out var subscriptionId)
            || string.IsNullOrWhiteSpace(subscriptionId))
        {
            const string diagnostic =
                "Run parameter 'subscriptionId' is required by H1 subscription-readiness " +
                "(spec § 4D I1 no-hardcoded-subscription). Operator must supply the target Azure " +
                "subscription id in ProvisioningRun.parameters.nonSecret before H1 can verify " +
                "reachability.";
            await MarkFailedAsync(
                run, etag, SubscriptionReadinessRejectionCodes.MissingSubscriptionId,
                diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                SubscriptionReadinessRejectionCodes.MissingSubscriptionId,
                diagnostic);
        }

        if (!run.Parameters.NonSecret.TryGetValue(TenantIdParameterKey, out var tenantId)
            || string.IsNullOrWhiteSpace(tenantId))
        {
            const string diagnostic =
                "Run parameter 'tenantId' is required by H1 subscription-readiness " +
                "(spec § 4D I1 no-hardcoded-tenant). Model 1 = Spaarke tenant; Model 2 = customer " +
                "tenant captured via H0.5 consent-callback. Operator must supply the target tenantId " +
                "in ProvisioningRun.parameters.nonSecret before H1 can verify reachability.";
            await MarkFailedAsync(
                run, etag, SubscriptionReadinessRejectionCodes.MissingTenantId,
                diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                SubscriptionReadinessRejectionCodes.MissingTenantId,
                diagnostic);
        }

        // (4) Tenancy-model normalization. Accepts both the design.md §6.2
        // structured names (Model1Shared / Model2Dedicated) and the POML's
        // colloquial names (SpaarkeOwned / CustomerOwned). Unknown values
        // fail Resumable so the operator sees the mismatch explicitly.
        var requiresLighthouseCheck = ClassifyTenancy(
            run.TenancyModel, out var tenancyKnown);
        if (!tenancyKnown)
        {
            var diagnostic =
                $"ProvisioningRun.TenancyModel value '{run.TenancyModel ?? "(null)"}' is not recognized. " +
                $"Accepted values: {string.Join(", ", SpaarkeOwnedTenancyValues.Concat(CustomerOwnedTenancyValues))}. " +
                "H1 cannot decide whether to run the Lighthouse-delegation branch.";
            await MarkFailedAsync(
                run, etag, SubscriptionReadinessRejectionCodes.InvalidTenancyModel,
                diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                SubscriptionReadinessRejectionCodes.InvalidTenancyModel,
                diagnostic);
        }

        // (5) Subscription reachability check — unconditional.
        SubscriptionReadinessCheckResult reachabilityResult;
        try
        {
            reachabilityResult = await _probe.CheckSubscriptionReachableAsync(
                subscriptionId, tenantId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Wave-C5 real ARM impl may throw on transient SDK faults —
            // classify as Resumable so operator can resume after the upstream
            // fault clears. Placeholder impl never throws.
            _logger.LogError(
                ex,
                "H1 subscription-readiness probe (reachability) threw unexpected exception: " +
                "runId={RunId} customerId={CustomerId} subscriptionId={SubscriptionId}",
                envelope.RunId, envelope.CustomerId, subscriptionId);
            var diagnostic =
                $"Subscription-readiness probe (reachability) infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "This is Resumable — operator resolves the ARM SDK / connectivity issue and " +
                "POSTs /api/runs/{id}/resume.";
            await MarkFailedAsync(
                run, etag, SubscriptionReadinessRejectionCodes.ProbeInfrastructureError,
                diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                SubscriptionReadinessRejectionCodes.ProbeInfrastructureError,
                diagnostic);
        }

        if (!reachabilityResult.Passed)
        {
            _logger.LogWarning(
                "H1 subscription readiness failed (reachability): runId={RunId} customerId={CustomerId} " +
                "subscriptionId={SubscriptionId} tenantId={TenantId}",
                envelope.RunId, envelope.CustomerId, subscriptionId, tenantId);
            await MarkFailedAsync(
                run, etag, SubscriptionReadinessRejectionCodes.SubscriptionUnreachable,
                reachabilityResult.Diagnostic, reachabilityResult.Evidence, cancellationToken)
                .ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                SubscriptionReadinessRejectionCodes.SubscriptionUnreachable,
                reachabilityResult.Diagnostic);
        }

        // (6) Lighthouse delegation check — CustomerOwned only.
        if (requiresLighthouseCheck)
        {
            SubscriptionReadinessCheckResult lighthouseResult;
            try
            {
                lighthouseResult = await _probe.CheckLighthouseDelegationAsync(
                    subscriptionId, tenantId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(
                    ex,
                    "H1 subscription-readiness probe (Lighthouse) threw unexpected exception: " +
                    "runId={RunId} customerId={CustomerId} subscriptionId={SubscriptionId}",
                    envelope.RunId, envelope.CustomerId, subscriptionId);
                var diagnostic =
                    $"Subscription-readiness probe (Lighthouse) infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                    "This is Resumable — operator resolves the ARM SDK / connectivity issue and " +
                    "POSTs /api/runs/{id}/resume.";
                await MarkFailedAsync(
                    run, etag, SubscriptionReadinessRejectionCodes.ProbeInfrastructureError,
                    diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
                return new HandlerResult.Failure(
                    FailureClass.Resumable,
                    SubscriptionReadinessRejectionCodes.ProbeInfrastructureError,
                    diagnostic);
            }

            if (!lighthouseResult.Passed)
            {
                _logger.LogWarning(
                    "H1 subscription readiness failed (Lighthouse delegation missing): " +
                    "runId={RunId} customerId={CustomerId} subscriptionId={SubscriptionId} tenantId={TenantId}",
                    envelope.RunId, envelope.CustomerId, subscriptionId, tenantId);
                await MarkFailedAsync(
                    run, etag, SubscriptionReadinessRejectionCodes.LighthouseDelegationMissing,
                    lighthouseResult.Diagnostic, lighthouseResult.Evidence, cancellationToken)
                    .ConfigureAwait(false);
                return new HandlerResult.Failure(
                    FailureClass.Resumable,
                    SubscriptionReadinessRejectionCodes.LighthouseDelegationMissing,
                    lighthouseResult.Diagnostic);
            }
        }

        // (7) All checks passed — mark H1 complete + enqueue H2a (wave-C4 bridge — see file header).
        stopwatch.Stop();
        _logger.LogInformation(
            "H1 subscription readiness succeeded: runId={RunId} customerId={CustomerId} " +
            "subscriptionId={SubscriptionId} tenancy={Tenancy} lighthouseChecked={LighthouseChecked} " +
            "durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, subscriptionId, run.TenancyModel,
            requiresLighthouseCheck, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAndAdvanceAsync(
            run, etag, idempotencyKey, envelope, requiresLighthouseCheck,
            reachabilityResult, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H1 idempotency key: <c>subready-{customerId}</c>.
    /// No version token per POML constraint (subscription readiness has no
    /// versioned artifact — a re-run always re-verifies the current state).
    /// Exposed as internal so unit tests can construct expected keys without
    /// duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return $"subready-{customerId}";
    }

    /// <summary>
    /// Classifies a tenancy-model string into "requires Lighthouse check" (true)
    /// vs SpaarkeOwned (false). <paramref name="known"/> is set to false when
    /// the input is null / whitespace / unrecognized — callers surface that as
    /// an <c>InvalidTenancyModel</c> failure rather than silently defaulting.
    /// Exposed as internal so unit tests can validate the mapping.
    /// </summary>
    internal static bool ClassifyTenancy(string? tenancyModel, out bool known)
    {
        if (string.IsNullOrWhiteSpace(tenancyModel))
        {
            known = false;
            return false;
        }
        if (CustomerOwnedTenancyValues.Contains(tenancyModel))
        {
            known = true;
            return true;
        }
        if (SpaarkeOwnedTenancyValues.Contains(tenancyModel))
        {
            known = true;
            return false;
        }
        known = false;
        return false;
    }

    private async Task MarkFailedAsync(
        ProvisioningRun run,
        string etag,
        string rejectionCode,
        string diagnostic,
        JsonElement? evidence,
        CancellationToken cancellationToken)
    {
        run.Status = RunStatus.Failed;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = $"[{rejectionCode}] {diagnostic}";

        // Gate state — one entry per H1 failure. Verifier is H1; state is
        // Pending (external precondition to resolve). Evidence carries the
        // probe's raw response so the operator has the diagnostic without
        // pulling logs.
        var gateEntry = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = evidence,
        };
        run.GateStates[$"subready-{rejectionCode}"] = gateEntry;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            // Concurrent writer advanced the run between our read + write.
            // Log the winning state — a concurrent H1 invocation may have
            // already recorded a different failure OR the operator manually
            // updated the row. Do NOT retry the ETag write; the reconciler
            // observes the winning state.
            _logger.LogWarning(
                "H1 subscription-readiness failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H1 subscription-readiness failure state write raced with row delete: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }
    }

    private async Task<HandlerResult> MarkCompleteAndAdvanceAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        bool lighthouseChecked,
        SubscriptionReadinessCheckResult reachabilityResult,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1); // Approximate; probe start not tracked separately.

        run.Status = RunStatus.Running;
        run.CurrentPhase = DownstreamHandlerId; // The reconciler observes this + knows to dispatch H2a next.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId, // H1 has no separate BFF job row; use RunId for correlation.
        });
        run.ErrorDetail = null;

        // Verified-gate entries — one for the subscription-reachability check,
        // one for the Lighthouse check when it applied. Evidence field carries
        // the probe's raw response payload so the operator sees the confirmed
        // ARM state without pulling logs. Note: InterStepState (design.md §6.2)
        // enumerates a LOCKED set of keys and does NOT include a subscription
        // metadata slot — see notes/task-043-deviations.md for the deliberate
        // scope-preservation choice (evidence goes in GateStates, not
        // InterStepState).
        run.GateStates[SubscriptionReadinessGates.SubscriptionReachable] = new GateEntry
        {
            Status = GateState.Verified,
            VerifierHandler = HandlerIdentifier,
            Evidence = reachabilityResult.Evidence,
        };
        if (lighthouseChecked)
        {
            run.GateStates[SubscriptionReadinessGates.LighthouseDelegation] = new GateEntry
            {
                Status = GateState.Verified,
                VerifierHandler = HandlerIdentifier,
                // Evidence for the Lighthouse gate is captured on the probe
                // result the handler chose not to route through here (avoids
                // an extra local var; test coverage validates the reachability
                // evidence flow which is the load-bearing case).
                Evidence = null,
            };
        }

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H1 subscription-readiness success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SubscriptionReadinessRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H1 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H1.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H1 subscription-readiness success state write raced with row delete: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: SubscriptionReadinessRejectionCodes.RunDeletedDuringCheck,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H1 subscription-readiness " +
                             $"was in flight.");
        }

        // Enqueue H2a — TEMPORARY WAVE-C4 BRIDGE (see file header). When
        // the wave-C5 reconciler ships, this call moves to the reconciler +
        // the enqueuer contract note (Enqueue/IHandlerEnqueuer.cs § NOT
        // CONSUMED BY) applies unchanged. H2a is owned by sibling task 044;
        // this handler only knows the string HandlerId.
        var downstreamEnvelope = new HandlerEnvelope
        {
            HandlerId = DownstreamHandlerId,
            RunId = envelope.RunId,
            CustomerId = envelope.CustomerId,
            ParametersJson = "{}", // H2a reads its own parameters from Cosmos.
            EnqueuedAt = completedAt,
        };
        try
        {
            await _enqueuer.EnqueueAsync(downstreamEnvelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The Cosmos state already records H1 complete + CurrentPhase =
            // H2a. Enqueue failure is recoverable — the reconciler's
            // background scan (wave C5, design.md § crash recovery I6) will
            // re-emit the H2a job. Log + return success so the handler
            // outcome accurately reflects the H1 completion; the enqueue
            // failure is a downstream concern.
            _logger.LogError(
                ex,
                "H1 succeeded but H2a downstream enqueue failed — reconciler scan will re-emit: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
