// -----------------------------------------------------------------------------
// H12aAiSeedChainHandler.cs
//
// L2 CONTROL-PLANE H12a AI seed chain handler (task 070, wave C' — Phase C';
// runner swapped to a pure-.NET Dataverse Web API writer by task 150, wave
// G-5).
//
// PURPOSE:
//   Terminal AI-domain seeder for a newly-provisioned Spaarke customer
//   environment. Reads task 069's declarative seed manifest
//   (scripts/seed-data/manifest.yaml), invokes the seed-manifest runner
//   (task 150's DataverseWebApiSeedWriter — direct Dataverse Web API writes,
//   YamlDotNet manifest parse) against the target Dataverse env, and records
//   outcome to Cosmos ProvisioningRun state. Ends the seed chain at
//   `playbook consumers` per ADR-039 (single AI routing surface;
//   spaarke-playbook-embeddings retired; frozen multi-node engine
//   deliberately UNDEPLOYED).
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-15 (H12a
//     acceptance): all seed rows present; no duplicates; playbook consumers
//     resolve to shipped playbooks; sprk_aimodeldeployment placeholder rows
//     (H12c populates).
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-17 (handler
//     ownership boundary): H12a owns the AI seed chain; H12b owns app-config
//     seed; H12c owns runtime references. Cross-handler boundary MUST be
//     honored — the manifest declares aimodeldeployment as PLACEHOLDER +
//     deployerOwnedBy=H12c so the PS orchestrator emits a marker rather than
//     invoking a deployer.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H12a
//     row: idempotency key = h12a-{customerId}-{manifestHash}; manifestHash =
//     SHA-256 of manifest.yaml so any change forces a re-seed on next run.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback
//     taxonomy: Pre-write validation failures are Resumable (operator fixes
//     params + resumes). Partial seed (some rows exist with broken FK) is
//     Quarantine-required.
//   - ADR-004: idempotent handler contract.
//   - ADR-010: DI minimalism — collaborators registered in L2, seams justified
//     by ≥2 implementations from day 1 (production PS runner + test stub;
//     production file reader + test fake).
//   - ADR-036: 3-level idempotency stack (Service Bus MessageId dedup +
//     Redis idempotency at Level 2 (not yet in L2) + handler body durable
//     dedup at Level 3 via CompletedPhases scan).
//   - ADR-039: single AI routing surface. Retired-artifact defense-in-depth
//     check runs BEFORE the PS orchestrator invocation — even though task
//     069's orchestrator ALSO enforces retiredArtifacts, this handler runs
//     an independent literal-substring scan against the manifest content so
//     a hand-edit that bypasses the generator ALSO fails loudly.
//   - ADR-039 amendment 2026-07-05: MUST NOT land new capability on the
//     frozen node-graph engine. Manifest defense-in-depth pattern list
//     enumerates "multinode" for this reason.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌─────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                        │ §4C class                 │
//   ├─────────────────────────────────────┼───────────────────────────┤
//   │ Run not found in Cosmos partition   │ Resumable                 │
//   │ Missing tenantId (§4D I1)           │ Resumable                 │
//   │ Missing target Dataverse URL        │ Resumable                 │
//   │ (InterStepState.DataverseEnvUrl     │ (H5/H6 must run first —   │
//   │ null; upstream H5/H6 not run)       │ operator resumes after)   │
//   │ Manifest.yaml not found on disk     │ Resumable                 │
//   │ (deployment/config error)           │                           │
//   │ Manifest contains retired-artifact  │ QuarantineRequired        │
//   │ (defense-in-depth backstop for      │ (hand-edit bypassed the   │
//   │ ADR-039 amendment 2026-07-05)       │ generator — operator MUST │
//   │                                     │ regenerate + review)      │
//   │ Invoke-SeedManifest.ps1 exit != 0   │ QuarantineRequired        │
//   │                                     │ (partial seed = broken FK;│
//   │                                     │ operator inspects runNotes│
//   │                                     │ before decommission)      │
//   │ Concurrent Cosmos writer conflict   │ Resumable                 │
//   │ Run row deleted mid-flight          │ Resumable                 │
//   └─────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): the reconciler computes
//           deterministic MessageId per (HandlerId, RunId, CustomerId,
//           paramHash); SB duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (design.md
//           §4.1 preamble; parity with H2a / H0 / H0.5).
//   Level 3 (handler body durable dedup): this handler scans
//           ProvisioningRun.CompletedPhases for (Phase=="H12a",
//           IdempotencyKey==h12a-{customerId}-{manifestHash}). Match ⇒
//           Success no-op. manifestHash is a deterministic SHA-256 of
//           manifest.yaml — content change forces a new key + re-seed.
//
// DOWNSTREAM (wave note):
//   H12a does NOT enqueue a specific successor. Downstream H12b (task 071)
//   is DAG-parallel with H12a — no dependency ordering. H12c (task 072)
//   populates sprk_aimodeldeployment values from the customer-specific Bicep
//   output (declared as PLACEHOLDER + owned by H12c in the manifest — the PS
//   orchestrator emits a placeholder marker for H12a rather than invoking
//   any deployer). H14 (post-deploy integrations) depends on the FULL
//   H12a + H12b + H12c triangle being green. The wave-C5 reconciler owns
//   the fan-out; for this task H12a mutates Cosmos state (CurrentPhase +
//   CompletedPhases + ErrorDetail) and returns, letting the reconciler
//   decide which handler(s) to dispatch next.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.AiSeedChain;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H12aAiSeedChainHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H12a;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    private readonly IProvisioningRunRepository _repository;
    private readonly ISeedManifestReader _manifestReader;
    private readonly ISeedManifestRunner _seedRunner;
    private readonly ILogger<H12aAiSeedChainHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H12a AI seed chain handler. All collaborators are
    /// interface-abstracted so unit tests can substitute stubs for each seam
    /// (parity with H2a's constructor shape).
    /// </summary>
    public H12aAiSeedChainHandler(
        IProvisioningRunRepository repository,
        ISeedManifestReader manifestReader,
        ISeedManifestRunner seedRunner,
        ILogger<H12aAiSeedChainHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(manifestReader);
        ArgumentNullException.ThrowIfNull(seedRunner);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _manifestReader = manifestReader;
        _seedRunner = seedRunner;
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
            // mismatch here means a dispatch bug — fail loud rather than
            // silently mis-executing (parity with H0PreflightHandler + H2a).
            throw new InvalidOperationException(
                $"H12aAiSeedChainHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H12a AI seed chain starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H12a aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AiSeedChainRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var parameters = run.Parameters.NonSecret;

        // (2) §4D I1 tenant guard — H12a MUST NOT fall back to a default tenant.
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H12a (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H12a dispatches.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSeedChainRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (3) Target Dataverse URL guard (POML acceptance criterion 6 — MUST
        //     NOT invoke the seeder script when this is missing). Sourced
        //     from InterStepState.DataverseEnvUrl (written by H5/H6). If
        //     null, the customer's Dataverse env has not been created yet
        //     and H12a cannot proceed.
        var targetDataverseUrl = run.InterStepState.DataverseEnvUrl;
        if (string.IsNullOrWhiteSpace(targetDataverseUrl))
        {
            var diagnostic =
                "Target Dataverse URL not present on ProvisioningRun.interStepState.dataverseEnvUrl. " +
                "H5/H6 (Dataverse env creation + solution import) MUST complete before H12a dispatches. " +
                "Handler did NOT invoke the seeder script (POML acceptance criterion 6).";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSeedChainRejectionCodes.MissingDataverseUrl, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (4) Read manifest + compute idempotency key. Defense-in-depth
        //     retired-artifact check runs here so a hand-edit that bypasses
        //     task 069's Invoke-SeedManifest.ps1 orchestrator retired-check
        //     ALSO fails loudly. Order matters: manifest-not-found is
        //     Resumable; retired-artifact match is QuarantineRequired.
        var manifestReadResult = await _manifestReader.ReadAsync(cancellationToken).ConfigureAwait(false);
        if (manifestReadResult is SeedManifestReadResult.NotFound notFound)
        {
            var diagnostic =
                $"AI seed manifest not found at '{notFound.AttemptedPath}'. " +
                "Verify scripts/seed-data/manifest.yaml ships in the L2 publish output.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                AiSeedChainRejectionCodes.ManifestNotFound, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var manifestSuccess = (SeedManifestReadResult.Success)manifestReadResult;
        if (manifestSuccess.RetiredArtifactViolation is { } violation)
        {
            var diagnostic =
                $"Manifest defense-in-depth check FAILED: retired-artifact pattern '{violation.Pattern}' matched " +
                $"in an artifact declaration at line {violation.LineNumber}: \"{violation.LineExcerpt}\". " +
                "Handler did NOT invoke the seeder script. ADR-039 amendment 2026-07-05 forbids landing new " +
                "capability on the frozen node-graph engine; spaarke-playbook-embeddings + dispatcher are retired. " +
                "Regenerate the manifest via the task-069 generator + review changes before re-running H12a.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSeedChainRejectionCodes.ManifestContainsRetiredArtifact, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var manifestHash = manifestSuccess.ContentHash;
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId, manifestHash);

        // (5) Level-3 idempotency: durable no-op on duplicate. A change to
        //     manifest.yaml (any content byte) produces a new hash + key, so
        //     an upgrade re-seed fires correctly on next invocation.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H12a idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (6) Invoke the seed runner. Long-running (up to 20 min per manifest
        //     scope) — reconciler owns keeping the ambient Service Bus lock
        //     alive per FR-22 / R20.
        var request = new SeedManifestInvocationRequest(
            CustomerId: envelope.CustomerId,
            TenantId: tenantId,
            TargetDataverseUrl: targetDataverseUrl);

        SeedManifestInvocationOutcome outcome;
        try
        {
            outcome = await _seedRunner.InvokeAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "H12a seed-runner infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Seed manifest runner infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Partial seed state may exist — treated as Quarantine-required per §4C.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSeedChainRejectionCodes.SeedManifestInvocationFailed, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        if (outcome is SeedManifestInvocationOutcome.Failure runnerFailure)
        {
            // POML acceptance criterion 5: stderr captured in Cosmos runNotes.
            // The runner has already truncated + formatted stderr + stdout
            // into the Diagnostic — FailAsync writes it to
            // ProvisioningRun.ErrorDetail (the L2 "runNotes" surface, per
            // design.md §6.2 field mapping).
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                AiSeedChainRejectionCodes.SeedManifestInvocationFailed, runnerFailure.Diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        var success = (SeedManifestInvocationOutcome.Success)outcome;

        // (7) All post-conditions cleared — advance Cosmos state. Downstream
        //     reconciler (wave C5) fans out to H14 once H12a + H12b + H12c
        //     triangle is green.
        stopwatch.Stop();
        _logger.LogInformation(
            "H12a AI seed chain succeeded: runId={RunId} customerId={CustomerId} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(run, etag, idempotencyKey, success.StdoutSummary, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H12a idempotency key:
    /// <c>h12a-{customerId}-{manifestHash}</c>. Exposed internal so unit tests
    /// can construct expected keys without duplicating the format. Parity with
    /// <see cref="BicepInfraDeploy.H2aBicepInfraDeployHandler.BuildIdempotencyKey"/>.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string manifestHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);
        return $"h12a-{customerId}-{manifestHash}";
    }

    private static bool TryGetNonEmpty(
        IDictionary<string, string> parameters,
        string key,
        out string value)
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
        ProvisioningRun run,
        string etag,
        FailureClass failureClass,
        string rejectionCode,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        run.Status = failureClass == FailureClass.QuarantineRequired
            ? RunStatus.Quarantined
            : RunStatus.Failed;
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

        run.GateStates[$"h12a-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H12a failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H12a failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        string stdoutSummary,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out to H14 when H12a/b/c triangle is green.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        // Preserve seed summary in ErrorDetail slot (design.md §6.2 "runNotes"
        // surface). Prefix "SEED-SUMMARY" so operators + queries can
        // distinguish this from actual error entries. The runner already
        // truncated to <=800 chars.
        run.ErrorDetail = $"[SEED-SUMMARY] {stdoutSummary}";

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H12a success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AiSeedChainRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H12a read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H12a.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H12a success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AiSeedChainRejectionCodes.RunDeletedDuringSeed,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H12a was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
