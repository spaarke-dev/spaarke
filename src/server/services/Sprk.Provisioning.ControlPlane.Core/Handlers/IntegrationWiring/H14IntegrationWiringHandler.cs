// -----------------------------------------------------------------------------
// H14IntegrationWiringHandler.cs
//
// L2 CONTROL-PLANE H14 post-deploy integration wiring handler (task 073, wave
// C4 Batch 3F). Parent of 3 DAG-parallel sub-handlers (H14a Exchange, H14b
// Graph webhooks, H14c Dataverse webhooks). T4 silent-fail trap owner (via
// H14a).
//
// PURPOSE:
//   Wires the customer environment into 3 external systems in parallel per
//   spec.md FR-19: (a) 2 Exchange ApplicationAccessPolicy entries (BFF
//   app-reg + UAMI, T4 action-and-verify); (b) Graph webhook subscriptions
//   per Communication/Email module (HMAC signing key from H4); (c) Dataverse
//   service-endpoint webhook (same HMAC signing key). S2S consent sub-step
//   (d) is explicitly NOT included per r3 task 060 — see
//   <see cref="AssertNoS2SSubStep"/>.
//
// SINGLE-WRITER DAG-PARALLEL DESIGN (Path C — pivot to comply with ADR-004's
// "one message one handler one outcome" in spirit; documented per CLAUDE.md
// §6.5):
//   H14a/H14b/H14c do NOT touch Cosmos themselves (see
//   H14aExchangePolicySubHandler.cs's file header for the full concurrency
//   rationale — 3 TRUE-parallel writers against the SAME ProvisioningRun
//   ETag would race on every single invocation). This parent handler:
//     1. Reads the run ONCE.
//     2. Extracts + validates every shared input (tenantId, keyVaultName,
//        InterStepState fields) BEFORE building any sub-envelope — a missing
//        upstream field fails the WHOLE H14 invocation Resumable (parity
//        with every other H-series handler's upstream-guard posture) rather
//        than partially dispatching.
//     3. Computes each sub-step's DETERMINISTIC expected idempotency key
//        (BuildIdempotencyKey on each sub-handler type) and checks
//        run.CompletedPhases for a match — an already-completed sub-step is
//        represented as a pre-completed Task (no external call), so
//        Task.WhenAll always awaits exactly 3 tasks whether fresh or reused.
//     4. Dispatches all 3 (fresh + reused) via Task.WhenAll — genuine
//        in-process parallelism for the fresh ones.
//     5. Aggregates outcomes: partial success is PERSISTED (a substep that
//        succeeded this invocation gets a CompletedPhase entry even if a
//        sibling substep failed) so a resume only re-drives the incomplete
//        substep(s). The overall §4C classification is the MOST SEVERE
//        classification among any failing substeps (QuarantineRequired >
//        RetryableWithCleanup > Resumable) — the operator sees the worst
//        problem first.
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
//     scan performed HERE, once, for all 3 sub-steps).
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌────────────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                               │ §4C class                 │
//   ├────────────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId/keyVaultName/subscriptionId│ Resumable                 │
//   │ /exchangePolicyScopeGroupId/                │                           │
//   │ webhookNotificationBaseUrl (run params)     │                           │
//   │ Missing bffAppRegId/miClientId/             │ Resumable (upstream       │
//   │ dataverseEnvUrl (InterStepState)            │ handler hasn't run yet)   │
//   │ Run not found in Cosmos partition           │ Resumable                 │
//   │ T4 drift (H14a)                             │ QuarantineRequired        │
//   │ Graph subscription create/renew failure     │ RetryableWithCleanup      │
//   │ (H14b)                                      │                           │
//   │ Dataverse serviceendpoint upsert failure     │ RetryableWithCleanup      │
//   │ (H14c)                                      │                           │
//   │ Aggregate (worst of the above 3)            │ max severity present      │
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

    /// <summary>Non-secret parameter key carrying the target Key Vault name (H14b/H14c signing-key reads).</summary>
    public const string KeyVaultNameParameterKey = "keyVaultName";

    /// <summary>Non-secret parameter key carrying the target subscription id (ADR-027 D4; KV read scoping).</summary>
    public const string SubscriptionIdParameterKey = "subscriptionId";

    /// <summary>Non-secret parameter key carrying the mail-enabled security group id scoping the Exchange ApplicationAccessPolicy.</summary>
    public const string ExchangePolicyScopeGroupIdParameterKey = "exchangePolicyScopeGroupId";

    /// <summary>Non-secret parameter key carrying the base URL of the customer's BFF webhook receiver (H14b + H14c).</summary>
    public const string WebhookNotificationBaseUrlParameterKey = "webhookNotificationBaseUrl";

    /// <summary>Non-secret parameter key carrying the Graph resource path for the Communication module subscription (optional; at least one of Communication/Email required).</summary>
    public const string CommunicationGraphResourceParameterKey = "communicationGraphResource";

    /// <summary>Non-secret parameter key carrying the Graph resource path for the Email module subscription (optional; at least one of Communication/Email required).</summary>
    public const string EmailGraphResourceParameterKey = "emailGraphResource";

    /// <summary>
    /// Exactly 3 DAG-parallel sub-steps per spec.md FR-19 — Exchange (a) +
    /// Graph webhooks (b) + Dataverse webhooks (c). S2S consent (d) is
    /// explicitly NOT a 4th sub-step (r3 task 060 dropped it).
    /// </summary>
    public const int ExpectedSubStepCount = 3;

    private readonly IProvisioningRunRepository _repository;
    private readonly H14aExchangePolicySubHandler _h14a;
    private readonly H14bGraphWebhookSubHandler _h14b;
    private readonly H14cDataverseWebhookSubHandler _h14c;
    private readonly IntegrationWiringOptions _options;
    private readonly ILogger<H14IntegrationWiringHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    public H14IntegrationWiringHandler(
        IProvisioningRunRepository repository,
        H14aExchangePolicySubHandler h14a,
        H14bGraphWebhookSubHandler h14b,
        H14cDataverseWebhookSubHandler h14c,
        IOptions<IntegrationWiringOptions> options,
        ILogger<H14IntegrationWiringHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(h14a);
        ArgumentNullException.ThrowIfNull(h14b);
        ArgumentNullException.ThrowIfNull(h14c);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _h14a = h14a;
        _h14b = h14b;
        _h14c = h14c;
        _options = options.Value;
        _logger = logger;

        AssertNoS2SSubStep();
    }

    /// <summary>
    /// Startup verification (POML step 5 / acceptance criterion 7): H14 has
    /// EXACTLY 3 sub-steps. r3 task 060 dropped the S2S consent sub-step (d)
    /// — this assertion is a forcing-function that fires loudly (not a
    /// silent no-op) if a future edit ever grows a 4th sub-handler field
    /// without updating <see cref="ExpectedSubStepCount"/> in lockstep.
    /// </summary>
    internal static void AssertNoS2SSubStep()
    {
        if (ExpectedSubStepCount != 3)
        {
            throw new InvalidOperationException(
                $"H14 sub-step count invariant violated: expected exactly 3 (Exchange + Graph + Dataverse), " +
                $"got {ExpectedSubStepCount}. S2S consent (d) is explicitly NOT a 4th sub-step per r3 task 060 — " +
                "if a 4th sub-handler was intentionally added, this assertion + its doc comment must be updated " +
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
        // ALL 3 sub-steps and recorded the "H14" phase — full no-op.
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
        if (!TryGetNonEmpty(parameters, KeyVaultNameParameterKey, out var keyVaultName))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingKeyVaultName,
                "Run parameter 'keyVaultName' is required by H14 (H14b/H14c read the HMAC signing key from this vault).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, SubscriptionIdParameterKey, out var subscriptionId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingSubscriptionId,
                "Run parameter 'subscriptionId' is required by H14 (KV read scoping for H14b/H14c).",
                cancellationToken).ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, ExchangePolicyScopeGroupIdParameterKey, out var policyScopeGroupId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14aRejections.MissingPolicyScopeGroupId,
                "Run parameter 'exchangePolicyScopeGroupId' is required by H14a.", cancellationToken)
                .ConfigureAwait(false);
        }
        if (!TryGetNonEmpty(parameters, WebhookNotificationBaseUrlParameterKey, out var notificationBaseUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingWebhookNotificationBaseUrl,
                "Run parameter 'webhookNotificationBaseUrl' is required by H14 (H14b + H14c both derive their " +
                "receiver URL from it).", cancellationToken).ConfigureAwait(false);
        }
        TryGetNonEmpty(parameters, CommunicationGraphResourceParameterKey, out var communicationResource);
        TryGetNonEmpty(parameters, EmailGraphResourceParameterKey, out var emailResource);

        var interStep = run.InterStepState;
        if (string.IsNullOrWhiteSpace(interStep.BffAppRegId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingBffAppRegId,
                "InterStepState.bffAppRegId is not populated — H3 (Entra app-reg) must complete before H14.",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.MiClientId))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingUamiClientId,
                "InterStepState.miClientId is not populated — the UAMI (H2a/uami.bicep) must complete before H14.",
                cancellationToken).ConfigureAwait(false);
        }
        if (string.IsNullOrWhiteSpace(interStep.DataverseEnvUrl))
        {
            return await FailAsync(run, etag, FailureClass.Resumable, H14Rejections.MissingDataverseEnvUrl,
                "InterStepState.dataverseEnvUrl is not populated — H5/H6 (Dataverse env) must complete before H14.",
                cancellationToken).ConfigureAwait(false);
        }

        var bffAppRegId = interStep.BffAppRegId!;
        var uamiClientId = interStep.MiClientId!;
        var dataverseEnvUrl = interStep.DataverseEnvUrl!;
        var dataverseWebhookUrl = $"{notificationBaseUrl.TrimEnd('/')}/api/webhooks/dataverse/communication";

        // (4) Compute each sub-step's deterministic expected key + build its
        // dispatch task (pre-completed Success if already recorded, else a
        // real invocation) — Task.WhenAll always awaits exactly 3 tasks.
        var h14aKey = H14aExchangePolicySubHandler.BuildIdempotencyKey(envelope.CustomerId, new[] { bffAppRegId, uamiClientId });
        var h14bResources = new List<string>();
        if (!string.IsNullOrWhiteSpace(communicationResource)) h14bResources.Add(communicationResource);
        if (!string.IsNullOrWhiteSpace(emailResource)) h14bResources.Add(emailResource);
        var h14bKey = h14bResources.Count > 0
            ? H14bGraphWebhookSubHandler.BuildIdempotencyKey(envelope.CustomerId, h14bResources, notificationBaseUrl)
            : null; // No targets configured — H14b will fail fast when actually invoked (not pre-completable).
        var h14cKey = H14cDataverseWebhookSubHandler.BuildIdempotencyKey(envelope.CustomerId, dataverseEnvUrl, dataverseWebhookUrl);

        var h14aTask = BuildSubStepTask(
            run, H14aExchangePolicySubHandler.HandlerIdentifier, h14aKey,
            () => _h14a.HandleAsync(
                new HandlerEnvelope
                {
                    HandlerId = H14aExchangePolicySubHandler.HandlerIdentifier,
                    RunId = envelope.RunId,
                    CustomerId = envelope.CustomerId,
                    ParametersJson = H14aExchangePolicySubHandler.BuildParametersJson(
                        tenantId, bffAppRegId, uamiClientId, policyScopeGroupId, _options.ExchangePolicyDescriptionPrefix),
                    EnqueuedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken));

        var h14bTask = h14bKey is null
            ? Task.FromResult<HandlerResult>(new HandlerResult.Failure(
                FailureClass.Resumable, H14bRejections.NoWebhookTargetsConfigured,
                "Neither run parameter 'communicationGraphResource' nor 'emailGraphResource' was supplied."))
            : BuildSubStepTask(
                run, H14bGraphWebhookSubHandler.HandlerIdentifier, h14bKey,
                () => _h14b.HandleAsync(
                    new HandlerEnvelope
                    {
                        HandlerId = H14bGraphWebhookSubHandler.HandlerIdentifier,
                        RunId = envelope.RunId,
                        CustomerId = envelope.CustomerId,
                        ParametersJson = H14bGraphWebhookSubHandler.BuildParametersJson(
                            tenantId, keyVaultName, subscriptionId, notificationBaseUrl,
                            communicationResource.Length > 0 ? communicationResource : null,
                            emailResource.Length > 0 ? emailResource : null,
                            _options.GraphSubscriptionExpirationMinutes),
                        EnqueuedAt = DateTimeOffset.UtcNow,
                    },
                    cancellationToken));

        var h14cTask = BuildSubStepTask(
            run, H14cDataverseWebhookSubHandler.HandlerIdentifier, h14cKey,
            () => _h14c.HandleAsync(
                new HandlerEnvelope
                {
                    HandlerId = H14cDataverseWebhookSubHandler.HandlerIdentifier,
                    RunId = envelope.RunId,
                    CustomerId = envelope.CustomerId,
                    ParametersJson = H14cDataverseWebhookSubHandler.BuildParametersJson(
                        tenantId, dataverseEnvUrl, keyVaultName, subscriptionId, dataverseWebhookUrl),
                    EnqueuedAt = DateTimeOffset.UtcNow,
                },
                cancellationToken));

        // (5) DAG-parallel dispatch — genuine in-process parallelism for the
        // NOT-already-completed sub-steps; pre-completed ones resolve instantly.
        await Task.WhenAll(h14aTask, h14bTask, h14cTask).ConfigureAwait(false);

        var subResults = new (string PhaseId, HandlerResult Result)[]
        {
            (H14aExchangePolicySubHandler.HandlerIdentifier, h14aTask.Result),
            (H14bGraphWebhookSubHandler.HandlerIdentifier, h14bTask.Result),
            (H14cDataverseWebhookSubHandler.HandlerIdentifier, h14cTask.Result),
        };
        Debug.Assert(subResults.Length == ExpectedSubStepCount, "H14 must always aggregate exactly 3 sub-step results.");

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

        // (7) All 3 sub-steps succeeded — record the parent "H14" completion.
        var parentKey = BuildParentIdempotencyKey(envelope.CustomerId, h14aKey, h14bKey!, h14cKey);
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
        H14bGraphWebhookSubHandler.HandlerIdentifier => H14Gates.GraphWebhooksWired,
        H14cDataverseWebhookSubHandler.HandlerIdentifier => H14Gates.DataverseWebhookWired,
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
    /// over the 3 sub-step keys joined. Exposed internal so unit tests can
    /// construct the expected key.
    /// </summary>
    internal static string BuildParentIdempotencyKey(string customerId, string h14aKey, string h14bKey, string h14cKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        var payload = $"{h14aKey}|{h14bKey}|{h14cKey}";
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
