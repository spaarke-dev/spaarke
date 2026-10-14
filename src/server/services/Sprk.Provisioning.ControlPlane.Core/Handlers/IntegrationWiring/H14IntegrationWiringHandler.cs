// -----------------------------------------------------------------------------
// H14IntegrationWiringHandler.cs
//
// L2 CONTROL-PLANE H14 post-deploy integration wiring handler (task 073, wave
// C4 Batch 3F). Parent of TWO in-process sub-handlers, run in order: H14a Exchange
// RBAC for Applications (T4 silent-fail trap owner), then H14m — the customer's
// Spaarke-tenant shared mailbox and its verified sprk_communicationaccount row
// (task 263, owner decision 2026-10-10 #1562). H14m needs H14a's grants for its
// authorization test, so it is not attempted when H14a fails. H14b (Graph webhook subscriptions)
// and H14c (Dataverse service-endpoint webhook) were REMOVED under ISS-019 /
// #1560: they registered webhooks at /api/webhooks/graph/{module} and
// /api/webhooks/dataverse/communication, routes the BFF never mapped, and the
// BFF's own GraphSubscriptionManager already creates and renews the per-mailbox
// subscriptions (owner directive "needed -> build, else remove").
//
// PURPOSE:
//   Wires the customer environment's mail access per spec.md FR-19 (a): the
//   stamp UAMI's group-scoped "Application Mail.*" roles (RBAC for
//   Applications, task 251; T4 action-and-verify) and the customer mailbox those
//   roles reach (H14m, task 263). S2S consent sub-step (d) is
//   explicitly NOT included per r3 task 060 -- see
//   <see cref="AssertNoS2SSubStep"/>.
//
// SINGLE-WRITER DESIGN (Path C -- documented per CLAUDE.md §6.5):
//   H14a does NOT touch Cosmos itself (see H14aExchangePolicySubHandler.cs's
//   file header). This parent handler:
//     1. Reads the run ONCE.
//     2. Extracts + validates every input (tenantId + other intake parameters,
//        InterStepState fields) BEFORE building the sub-envelope -- a missing
//        upstream field fails the whole H14 invocation Resumable.
//     3. Computes H14a's deterministic expected idempotency key and checks
//        run.CompletedPhases for a match (level-3 idempotency).
//     4. Dispatches H14a, then H14m (or reuses a recorded completion).
//     5. Persists each sub-step's CompletedPhase on success and classifies a
//        failure per §4C.
//     6. Performs ONE ReplaceRunAsync call with the etag from step 1.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-19 (H14
//     acceptance) + FR-33 T4.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H14 row
//     + §4B T4 trap catalog + §4C rollback taxonomy.
//   - .claude/adr/ADR-004: IJobHandler-shape contract.
//   - .claude/adr/ADR-010: registers in L2, NOT BFF.
//   - .claude/adr/ADR-036: 3-level idempotency (level 3 = the CompletedPhases
//     scan performed HERE, once).
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                               │ §4C class                 │
//   ├────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId/exchangePolicyScopeGroupId │ Resumable                 │
//   │ /displayName/communicationDefaultMailbox    │                           │
//   │ (run params)                                │                           │
//   │ Missing miClientId/miObjectId               │ Resumable (upstream       │
//   │ (InterStepState)                            │ handler hasn't run yet)   │
//   │ Run not found in Cosmos partition           │ Resumable                 │
//   │ T4 drift (H14a)                             │ QuarantineRequired        │
//   │ Foreign / out-of-scope mailbox, row conflict│ QuarantineRequired        │
//   │ (H14m)                                      │                           │
//   │ Concurrent Cosmos writer conflict           │ Resumable                 │
//   │ Run row deleted mid-flight                  │ Resumable                 │
//   └────────────────────────────────────────────┴───────────────────────────┘
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.IntegrationWiring;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H14IntegrationWiringHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H14;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the mail-enabled security group H14a scopes the stamp identity's Exchange mailbox roles to.</summary>
    public const string ExchangePolicyScopeGroupIdParameterKey = "exchangePolicyScopeGroupId";

    /// <summary>
    /// Exactly 2 in-process sub-steps -- H14a Exchange roles, then H14m the customer mailbox (task 263). H14b
    /// (Graph webhooks) and H14c (Dataverse webhooks) were removed (ISS-019); S2S consent (d) is explicitly NOT a
    /// sub-step (r3 task 060 dropped it).
    /// </summary>
    public const int ExpectedSubStepCount = 2;

    private readonly IProvisioningRunRepository _repository;
    private readonly H14aExchangePolicySubHandler _h14a;
    private readonly H14mCustomerMailboxSubHandler _h14m;
    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<H14IntegrationWiringHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H14IntegrationWiringHandler(
        IProvisioningRunRepository repository,
        H14aExchangePolicySubHandler h14a,
        H14mCustomerMailboxSubHandler h14m,
        IOptions<IntegrationWiringOptions> options,
        ILogger<H14IntegrationWiringHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(h14a);
        ArgumentNullException.ThrowIfNull(h14m);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _h14a = h14a;
        _h14m = h14m;
        _options = options.Value;
        _logger = logger;

        AssertNoS2SSubStep();
    }

    /// <summary>
    /// Startup verification (POML step 5 / acceptance criterion 7): H14 has
    /// EXACTLY 2 sub-steps (H14a, H14m). r3 task 060 dropped the S2S consent sub-step (d)
    /// — this assertion is a forcing-function that fires loudly (not a
    /// silent no-op) if a future edit ever grows a 4th sub-handler field
    /// without updating <see cref="ExpectedSubStepCount"/> in lockstep.
    /// </summary>
    internal static void AssertNoS2SSubStep()
    {
        if (ExpectedSubStepCount != 2)
        {
            throw new InvalidOperationException(
                $"H14 sub-step count invariant violated: expected exactly 2 (H14a Exchange roles, H14m customer mailbox), " +
                $"got {ExpectedSubStepCount}. S2S consent (d) is explicitly NOT a sub-step per r3 task 060 — " +
                "if another sub-handler was intentionally added, this assertion + its doc comment must be updated " +
                "together, and the addition must NOT be the S2S consent step.");
        }
    }

    /// <inheritdoc/>
    public async Task<HandlerResult> HandleAsync(HandlerEnvelope envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.RunId);
        ArgumentException.ThrowIfNullOrWhiteSpace(envelope.CustomerId);

        if (!string.Equals(envelope.HandlerId, HandlerIdentifier, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"H14IntegrationWiringHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H14 post-deploy integration wiring starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun ONCE. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H14 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14Rejections.RunNotFound,
                $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;

        // (2) Parent-level idempotency: a prior invocation already completed
        // sub-steps and recorded the "H14" phase — full no-op.
        var existingParentPhase = run.CompletedPhases.FirstOrDefault(cp =>
            string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal));
        if (existingParentPhase is not null)
        {
            _logger.LogInformation(
                "H14 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, existingParentPhase.IdempotencyKey);
            return new HandlerResult.Success(existingParentPhase.IdempotencyKey);
        }

        // (3) Shared parameter guards — BEFORE building any sub-envelope.
        var parameters = run.Parameters.NonSecret;
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingTenantId,
                "Run parameter 'tenantId' is required by H14 (§4D I1 no-hardcoded-tenant).", cancellationToken)
                .ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, ExchangePolicyScopeGroupIdParameterKey, out var policyScopeGroupId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14aRejections.MissingPolicyScopeGroupId,
                "Run parameter 'exchangePolicyScopeGroupId' is required by H14a.", cancellationToken)
                .ConfigureAwait(false);
        }
        var interStep = run.InterStepState;
        if (string.IsNullOrWhiteSpace(interStep.MiClientId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingUamiClientId,
                "InterStepState.miClientId is not populated — the UAMI (H2a/uami.bicep) must complete before H14.",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.MiObjectId))
        {
            // H14a registers the stamp identity in Exchange by its Entra service-principal object id (task 251).
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingUamiObjectId,
                "InterStepState.miObjectId is not populated — the UAMI (H2a/uami.bicep) must complete before H14.",
                cancellationToken).ConfigureAwait(false);
        }
        // H14m (task 263): the shared mailbox's display name + address (intake, one producer each) and the stamp
        // Dataverse + identity its sprk_communicationaccount row is written with (the H7/H7b identity).
        if (!TryGetNonEmpty(parameters, IntakeParameterCatalog.DisplayName, out var displayName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingDisplayName,
                "Run parameter 'displayName' is required by H14m (the customer mailbox's display name).", cancellationToken)
                .ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, IntakeParameterCatalog.CommunicationDefaultMailbox, out var mailboxAddress))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingMailboxAddress,
                "Run parameter 'communicationDefaultMailbox' is required by H14m (the customer mailbox's address).", cancellationToken)
                .ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.DataverseEnvUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingDataverseEnvUrl,
                "InterStepState.dataverseEnvUrl is not populated — H5 must complete before H14 (H14m writes the account row there).",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.BffAppRegId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingBffAppRegId,
                "InterStepState.bffAppRegId is not populated — H3 must complete before H14 (H14m signs in to Dataverse as it).",
                cancellationToken).ConfigureAwait(false);
        }
        var uamiClientId = interStep.MiClientId!;
        var uamiObjectId = interStep.MiObjectId!;
        var dataverseUrl = interStep.DataverseEnvUrl!;
        var bffAppRegId = interStep.BffAppRegId!;

        // (4) Compute each sub-step's deterministic expected key + build its
        // dispatch task (pre-completed Success if already recorded, else a
        // real invocation).
        var h14aKey = _h14a.ExpectedIdempotencyKey(envelope.CustomerId, uamiClientId, policyScopeGroupId, _options.ExchangeAssignmentNamePrefix);
        var h14mKey = H14mCustomerMailboxSubHandler.ExpectedIdempotencyKey(
            envelope.CustomerId, uamiClientId, policyScopeGroupId, mailboxAddress, dataverseUrl);

        var h14aTask = BuildSubStepTask(
            run, H14aExchangePolicySubHandler.HandlerIdentifier, h14aKey,
            () => _h14a.HandleAsync(
                new HandlerEnvelope
                {
                    HandlerId = H14aExchangePolicySubHandler.HandlerIdentifier,
                    RunId = envelope.RunId,
                    CustomerId = envelope.CustomerId,
                    ParametersJson = H14aExchangePolicySubHandler.BuildParametersJson(
                        tenantId, uamiClientId, uamiObjectId, policyScopeGroupId, _options.ExchangeAssignmentNamePrefix),
                    EnqueuedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken));

        // (5) Dispatch in order (pre-completed reuse resolves instantly). H14m only after H14a succeeded: its
        // authorization test needs H14a's group-scoped assignments.
        var h14aResult = await h14aTask.ConfigureAwait(false);
        var h14mResult = h14aResult is HandlerResult.Success
            ? await BuildSubStepTask(
                run, H14mCustomerMailboxSubHandler.HandlerIdentifier, h14mKey,
                () => _h14m.HandleAsync(
                    new HandlerEnvelope
                    {
                        HandlerId = H14mCustomerMailboxSubHandler.HandlerIdentifier,
                        RunId = envelope.RunId,
                        CustomerId = envelope.CustomerId,
                        ParametersJson = H14mCustomerMailboxSubHandler.BuildParametersJson(
                            tenantId, uamiClientId, policyScopeGroupId, displayName, mailboxAddress, dataverseUrl, bffAppRegId),
                        EnqueuedAt = DateTimeOffset.UtcNow,
                    },
                    cancellationToken)).ConfigureAwait(false)
            : new HandlerResult.Failure(FailureClass.Resumable, H14Rejections.SubStepFailed,
                "Not attempted: H14a did not succeed, and H14m's authorization test needs H14a's assignments.");

        var subResults = new (string PhaseId, HandlerResult Result)[]
        {
            (H14aExchangePolicySubHandler.HandlerIdentifier, h14aResult),
            (H14mCustomerMailboxSubHandler.HandlerIdentifier, h14mResult),
        };
        Debug.Assert(subResults.Length == ExpectedSubStepCount, "H14 must always aggregate exactly 2 sub-step results.");

        // (6) Persist partial success: any substep that succeeded THIS
        // invocation (or was already-completed) gets its CompletedPhase
        // entry recorded, regardless of sibling failures.
        var completedAt = DateTimeOffset.UtcNow;
        foreach (var (phaseId, result) in subResults)
        {
            if (result is HandlerResult.Success success
                && !run.CompletedPhases.Any(cp => string.Equals(cp.Phase, phaseId, StringComparison.Ordinal)
                    && string.Equals(cp.IdempotencyKey, success.IdempotencyKey, StringComparison.Ordinal)))
            {
                run.CompletedPhases.Add(new CompletedPhase
                {
                    Phase = phaseId,
                    StartedAt = completedAt - TimeSpan.FromMilliseconds(1),
                    CompletedAt = completedAt,
                    IdempotencyKey = success.IdempotencyKey,
                    JobId = envelope.RunId,
                });
                run.GateStates[GateForPhase(phaseId)] = new GateEntry
                {
                    Status = GateState.Verified,
                    VerifiedAt = completedAt,
                    VerifierHandler = phaseId,
                };
            }
        }

        var failures = subResults.Where(r => r.Result is HandlerResult.Failure).ToList();

        if (failures.Count > 0)
        {
            var worstFailureClass = failures
                .Select(f => ((HandlerResult.Failure)f.Result).Class)
                .OrderByDescending(Severity)
                .First();
            var diagnostic = string.Join(" | ", failures.Select(f =>
            {
                var failure = (HandlerResult.Failure)f.Result;
                return $"{f.PhaseId}[{failure.RejectionCode}]: {failure.Diagnostic}";
            }));

            run.Status = worstFailureClass == FailureClass.QuarantineRequired ? RunStatus.Quarantined : RunStatus.Failed;
            run.CurrentPhase = HandlerIdentifier;
            run.ErrorDetail = $"[{H14Rejections.SubStepFailed}] {diagnostic}";
            if (worstFailureClass == FailureClass.QuarantineRequired)
            {
                run.Quarantine = new QuarantineInfo
                {
                    State = QuarantineState.Quarantined,
                    Reason = diagnostic,
                    QuarantinedByHandler = HandlerIdentifier,
                    QuarantinedAt = completedAt,
                };
            }

            var replaceOnFailure = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
            LogReplaceOutcome(replaceOnFailure, envelope);

            return new HandlerResult.Failure(worstFailureClass, H14Rejections.SubStepFailed, diagnostic);
        }

        // (7) All sub-steps succeeded — record the parent "H14" completion.
        var parentKey = BuildParentIdempotencyKey(envelope.CustomerId, h14aKey, h14mKey);
        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier;
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = completedAt - TimeSpan.FromMilliseconds(1),
            CompletedAt = completedAt,
            IdempotencyKey = parentKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();

        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H14 success state write LOST optimistic-concurrency race: runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14Rejections.ConcurrentWriteConflict,
                $"Concurrent write advanced run '{run.RunId}' between H14 read + write. Winning status: " +
                $"{conflict.Current.Run.Status}. Resume will re-run H14 which will short-circuit already-completed sub-steps.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H14 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                FailureClass.Resumable, H14Rejections.RunDeletedDuringWiring,
                $"ProvisioningRun '{run.RunId}' was deleted while H14 was in flight.");
        }

        _logger.LogInformation(
            "H14 succeeded: runId={RunId} customerId={CustomerId} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, stopwatch.ElapsedMilliseconds);

        return new HandlerResult.Success(parentKey);
    }

    /// <summary>
    /// Builds a sub-step's dispatch task: a pre-completed
    /// <see cref="HandlerResult.Success"/> if <paramref name="expectedKey"/>
    /// already matches a recorded <see cref="CompletedPhase"/> (level-3
    /// idempotency, checked ONCE by the parent before any sub-handler is
    /// invoked), else the real invocation.
    /// </summary>
    private static Task<HandlerResult> BuildSubStepTask(
        ProvisioningRun run, string phaseId, string expectedKey, Func<Task<HandlerResult>> invoke)
    {
        var alreadyDone = run.CompletedPhases.Any(cp =>
            string.Equals(cp.Phase, phaseId, StringComparison.Ordinal)
            && string.Equals(cp.IdempotencyKey, expectedKey, StringComparison.Ordinal));
        return alreadyDone
            ? Task.FromResult<HandlerResult>(new HandlerResult.Success(expectedKey))
            : invoke();
    }

    private static string GateForPhase(string phaseId) => phaseId switch
    {
        H14aExchangePolicySubHandler.HandlerIdentifier => H14Gates.ExchangePolicyApplied,
        H14mCustomerMailboxSubHandler.HandlerIdentifier => H14Gates.CustomerMailboxVerified,
        _ => throw new InvalidOperationException($"Unknown H14 sub-step phase id '{phaseId}'."),
    };

    private static int Severity(FailureClass failureClass) => failureClass switch
    {
        FailureClass.QuarantineRequired => 3,
        FailureClass.RetryableWithCleanup => 2,
        FailureClass.Resumable => 1,
        _ => 0,
    };

    /// <summary>
    /// Computes the deterministic H14 (parent) idempotency key:
    /// <c>h14-{customerId}-{combinedHash}</c> where combinedHash is SHA-256
    /// over the sub-step keys in order. Exposed internal so unit tests can
    /// construct the expected key.
    /// </summary>
    internal static string BuildParentIdempotencyKey(string customerId, string h14aKey, string h14mKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        var payload = h14aKey + "|" + h14mKey;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
        return $"h14-{customerId}-{hash}";
    }

    private static bool TryGetNonEmpty(IDictionary<string, string> parameters, string key, out string value)
    {
        if (parameters.TryGetValue(key, out var raw) && !string.IsNullOrWhiteSpace(raw))
        {
            value = raw;
            return true;
        }
        value = string.Empty;
        return false;
    }

    private async Task<HandlerResult> FailAsync(
        ProvisioningRun run, string etag, FailureClass failureClass, string rejectionCode, string diagnostic,
        CancellationToken cancellationToken)
    {
        run.Status = failureClass == FailureClass.QuarantineRequired ? RunStatus.Quarantined : RunStatus.Failed;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = $"[{rejectionCode}] {diagnostic}";
        if (failureClass == FailureClass.QuarantineRequired)
        {
            run.Quarantine = new QuarantineInfo
            {
                State = QuarantineState.Quarantined,
                Reason = diagnostic,
                QuarantinedByHandler = HandlerIdentifier,
                QuarantinedAt = DateTimeOffset.UtcNow,
            };
        }

        run.GateStates[$"h14-{rejectionCode}"] = new GateEntry { Status = GateState.Pending, VerifierHandler = HandlerIdentifier };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        LogReplaceOutcome(replace, new HandlerEnvelope
        {
            HandlerId = HandlerIdentifier,
            RunId = run.RunId,
            CustomerId = run.CustomerId,
            ParametersJson = "{}",
            EnqueuedAt = DateTimeOffset.UtcNow,
        });

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private void LogReplaceOutcome(ReplaceRunResult replace, HandlerEnvelope envelope)
    {
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H14 state write LOST optimistic-concurrency race: runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                envelope.RunId, envelope.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H14 state write raced with row delete: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
        }
    }
}
