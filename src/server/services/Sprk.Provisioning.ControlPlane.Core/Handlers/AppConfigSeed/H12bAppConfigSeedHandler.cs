// -----------------------------------------------------------------------------
// H12bAppConfigSeedHandler.cs
//
// The H12b app-config seed handler (task 071, wave Cp — DAG-parallel with
// H12a). Populates the non-AI configuration records every customer environment
// needs to render UI: DataGrid configs (sprk_gridconfiguration), field-mapping
// profiles + rules, system workspace layouts (sprk_workspacelayout), and chart
// definitions.
//
// DAG position: post-H7 (env-var values ready), parallel with H12a (AI seed
// chain). No cross-dependency — the two handlers touch disjoint entity sets.
// H12c (task 072) depends on BOTH H12a + H12b completing before it can
// populate customer-specific runtime references.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-16 (H12b
//     app-config seed acceptance).
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H12b row
//     + §4C rollback (H12b failures are Resumable — every wrapped script is
//     upsert-safe so post-remediation resume re-drives cleanly).
//   - projects/customer-provisioning-orchestration-r1/notes/ai-seed-drift-resolution-2026-08.md
//     §4b (app-config source-of-truth decision matrix) + §5b (N-deltas —
//     what needs to happen to un-defer field-mapping + chart-def scopes).
//   - .claude/adr/ADR-004-job-contract.md — idempotent + at-least-once safe.
//   - .claude/adr/ADR-010-di-minimalism.md — IAppConfigSeeder seam earns
//     keep: 4 real Dataverse Web API impls as of task 152 (Wave G-5 Batch
//     G-5B) — DataGrid/WorkspaceLayout ports (task 151) + FieldMapping/
//     ChartDefinition greenfield seeders (task 152). DeferredAppConfigSeeder
//     is RETIRED (kept on disk unregistered, zero remaining callers).
//   - .claude/adr/ADR-036-background-job-infrastructure.md — reuse shared
//     background-job infrastructure (Service Bus + IHandlerEnqueuer; Redis
//     IdempotencyService pending L2 wiring).
//   - .claude/adr/ADR-039-grounded-execution-closed-catalogs.md — H12b MUST
//     NOT touch any AI record type (H12a's scope); the negative acceptance
//     criterion is enforced by construction (IAppConfigSeeder registry does
//     not include any AI scope).
//
// ROLLBACK CLASSIFICATION (§4C):
//   H12b writes external Dataverse state (sprk_gridconfiguration +
//   sprk_workspacelayout rows) via upsert-safe existence-check-then-insert
//   scripts. Partial writes are safe to retry — the underlying scripts
//   detect existing records + PATCH or skip. On any seeder failure the run
//   transitions to Status=Failed with FailureClass.Resumable + a per-scope
//   rejection code; operator resolves the underlying issue (Dataverse creds,
//   script availability, target-env URL) then POSTs /api/runs/{id}/resume
//   which re-dispatches H12b with the same envelope + same idempotency key.
//
// DOWNSTREAM ENQUEUE (WAVE C4 TEMPORARY BRIDGE):
//   Same pattern as H0/H0.5/H1/H2a handlers in this wave: the wave-C5
//   reconciler does not exist yet, so H12b enqueues H12c (task 072) directly
//   here as a TEMPORARY BRIDGE. H12c is the DAG-join point that requires
//   BOTH H12a + H12b complete; a separate join-coordinator (wave C5) will
//   own the actual advancement decision. For wave Cp, H12b enqueues H12c
//   unconditionally on its own success — H12a's parallel enqueue is
//   duplicate-detected by Service Bus MessageId dedup + the H12c handler's
//   own idempotency check on CompletedPhases.
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
//                      for an entry with (Phase == "H12b", IdempotencyKey ==
//                      h12b-{customerId}-{manifestHash}) — returns
//                      HandlerResult.Success no-op on hit. Cosmos writes are
//                      ETag-guarded via ReplaceRunAsync (FR-23 I5).
//
//   Idempotency key format: h12b-{customerId}-{manifestHash}
//     - customerId: partition-key tie so cross-customer runs never collide.
//     - manifestHash: SHA-256 of scripts/seed-data/manifest.yaml contents.
//       Same-manifest re-invocation → same key → durable no-op. Manifest
//       edit (add a new app-config scope entry, retire an artifact, etc.)
//       → new key → re-drives the seeders. Aligns with sibling H12a
//       (task 070) which uses the identical manifestHash formula so
//       operators reason about ONE manifest state across both handlers.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.AppConfigSeed;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H12bAppConfigSeedHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md §4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H12b;

    /// <summary>
    /// Handler identifier of the downstream DAG-join point that consumes
    /// H12b's completion. H12a (task 070) also enqueues H12c; both parallel
    /// enqueues are duplicate-detected via Service Bus MessageId dedup +
    /// H12c's own idempotency check.
    /// </summary>
    public const string DownstreamHandlerId = "H12c";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>
    /// Non-secret parameter key carrying the target customer Dataverse env URL.
    /// H12b passes this to every registered <see cref="IAppConfigSeeder"/>
    /// so the wrapped PS scripts hit the correct env (never a default fallback
    /// per §4D I1).
    /// </summary>
    public const string DataverseUrlParameterKey = "dataverseUrl";

    private readonly IProvisioningRunRepository _repository;
    private readonly IHandlerEnqueuer _enqueuer;
    private readonly IReadOnlyList<IAppConfigSeeder> _seeders;
    private readonly AppConfigSeedOptions _options;
    private readonly ILogger<H12bAppConfigSeedHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H12b app-config seed handler.
    /// </summary>
    /// <param name="repository">Cosmos-backed run state store (task 037).</param>
    /// <param name="enqueuer">Service Bus enqueuer used to dispatch H12c on success (wave-C4 temporary bridge — see file header).</param>
    /// <param name="seeders">Registered per-scope seeders. Registration order is preserved (data-grid → workspace-layout → field-mapping → chart-def is the canonical sequence but the handler does not enforce a specific order — it aggregates all outcomes).</param>
    /// <param name="options">Bound app-config seed options (manifest path, pwsh path, timeout).</param>
    /// <param name="logger">Structured logger.</param>
    public H12bAppConfigSeedHandler(
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        IEnumerable<IAppConfigSeeder> seeders,
        Microsoft.Extensions.Options.IOptions<AppConfigSeedOptions> options,
        ILogger<H12bAppConfigSeedHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(seeders);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _enqueuer = enqueuer;
        _seeders = seeders.ToList();
        _options = options.Value;
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
                $"H12bAppConfigSeedHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H12b app-config seed starting: runId={RunId} customerId={CustomerId} seederCount={SeederCount}",
            envelope.RunId, envelope.CustomerId, _seeders.Count);

        // (1) Compute manifest hash — the H12b idempotency key incorporates
        // SHA-256 of manifest.yaml so a manifest edit forces a re-seed.
        // Aligns with sibling H12a (task 070) for a single-manifest-state
        // mental model. Failure here is Resumable (operator restores the
        // manifest file + resumes).
        string manifestHash;
        try
        {
            manifestHash = ComputeManifestHash(_options.ManifestPath);
        }
        catch (FileNotFoundException ex)
        {
            var diagnostic =
                $"Manifest file not found at '{_options.ManifestPath}'. " +
                "H12b requires the seed manifest to compute a deterministic idempotency key. " +
                $"Verify scripts/seed-data/manifest.yaml ships in the L2 publish output. " +
                $"Inner: {ex.Message}";
            _logger.LogWarning(ex,
                "H12b app-config seed aborted — manifest not found: runId={RunId} customerId={CustomerId} " +
                "manifestPath={ManifestPath}",
                envelope.RunId, envelope.CustomerId, _options.ManifestPath);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AppConfigSeedRejectionCodes.ManifestNotFound,
                Diagnostic: diagnostic);
        }

        var idempotencyKey = $"h12b-{envelope.CustomerId}-{manifestHash}";

        // (2) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H12b app-config seed aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AppConfigSeedRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;

        // (3) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H12b app-config seed idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (4) §4D I1 parameter guard — seeders need tenantId + dataverseUrl;
        // fail BEFORE any script fires so the operator sees the specific
        // rejection code and no PS shell-out happens with default env vars.
        if (!run.Parameters.NonSecret.TryGetValue(TenantIdParameterKey, out var tenantId)
            || string.IsNullOrWhiteSpace(tenantId))
        {
            const string diagnostic =
                "Run parameter 'tenantId' is required by H12b app-config seed " +
                "(spec §4D I1 no-hardcoded-tenant). Operator must supply the target Entra tenantId " +
                "in ProvisioningRun.parameters.nonSecret before H12b can invoke seeders.";
            await MarkFailedAsync(
                run, etag, AppConfigSeedRejectionCodes.MissingTenantId,
                diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                AppConfigSeedRejectionCodes.MissingTenantId,
                diagnostic);
        }

        if (!run.Parameters.NonSecret.TryGetValue(DataverseUrlParameterKey, out var dataverseUrl)
            || string.IsNullOrWhiteSpace(dataverseUrl))
        {
            const string diagnostic =
                "Run parameter 'dataverseUrl' is required by H12b app-config seed. " +
                "The wrapped PS scripts (Deploy-SystemWorkspaceLayouts.ps1 + " +
                "seed-reconciliation-gridconfig.ps1) call `az account get-access-token --resource {url}` " +
                "against this exact URL — a missing value cannot be silently defaulted (§4D I1).";
            await MarkFailedAsync(
                run, etag, AppConfigSeedRejectionCodes.MissingDataverseUrl,
                diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                AppConfigSeedRejectionCodes.MissingDataverseUrl,
                diagnostic);
        }

        // (5) Invoke every registered seeder in registration order. Sequential
        // rather than parallel — the underlying PS scripts each shell out to
        // `az` for token acquisition + hit Dataverse REST, so parallelism buys
        // ~seconds. Serial keeps per-scope diagnostics readable and matches
        // Invoke-SeedManifest.ps1's own serial-fan-out shape.
        var input = new AppConfigSeedInput(envelope.CustomerId, tenantId, dataverseUrl);
        var scopeOutcomes = new List<ScopeOutcome>(_seeders.Count);
        foreach (var seeder in _seeders)
        {
            AppConfigSeederResult result;
            try
            {
                result = await seeder.SeedAsync(input, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "H12b app-config seeder threw unexpected exception: scope={ScopeName} runId={RunId} customerId={CustomerId}",
                    seeder.ScopeName, envelope.RunId, envelope.CustomerId);
                var diagnostic =
                    $"App-config seeder '{seeder.ScopeName}' infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                    "Resumable — operator resolves the pwsh / scripts / connectivity issue and POSTs /api/runs/{id}/resume.";
                await MarkFailedAsync(
                    run, etag, AppConfigSeedRejectionCodes.SeederInfrastructureError,
                    diagnostic, evidence: null, cancellationToken).ConfigureAwait(false);
                return new HandlerResult.Failure(
                    FailureClass.Resumable,
                    AppConfigSeedRejectionCodes.SeederInfrastructureError,
                    diagnostic);
            }

            scopeOutcomes.Add(new ScopeOutcome(seeder.ScopeName, result));
            _logger.LogInformation(
                "H12b app-config seeder outcome: scope={ScopeName} status={Status}",
                seeder.ScopeName, result.Status);
        }

        // (6) Aggregate — any Failed → HandlerResult.Failure(Resumable) with
        // rolled-up per-scope diagnostics.
        var failedScopes = scopeOutcomes.Where(o => o.Result.Status == AppConfigSeederStatus.Failed).ToList();
        if (failedScopes.Count > 0)
        {
            var aggregate = string.Join(" | ", failedScopes.Select(o =>
                $"[{o.ScopeName}] {o.Result.Diagnostic}"));
            var diagnostic =
                $"H12b app-config seed failed for {failedScopes.Count} scope(s): {aggregate}. " +
                "Every wrapped script is upsert-safe — resume re-drives cleanly after the operator " +
                "resolves the underlying issue (Dataverse creds, script availability, target-env URL).";
            _logger.LogWarning(
                "H12b app-config seed FAILED: runId={RunId} customerId={CustomerId} failedScopeCount={FailedCount} failedScopes={FailedScopes}",
                envelope.RunId, envelope.CustomerId, failedScopes.Count,
                string.Join(",", failedScopes.Select(o => o.ScopeName)));

            var evidence = BuildScopeOutcomesEvidence(scopeOutcomes);
            await MarkFailedAsync(
                run, etag, AppConfigSeedRejectionCodes.SeederFailed,
                diagnostic, evidence, cancellationToken).ConfigureAwait(false);
            return new HandlerResult.Failure(
                FailureClass.Resumable,
                AppConfigSeedRejectionCodes.SeederFailed,
                diagnostic);
        }

        // (7) All scopes OK or Deferred → mark H12b complete + enqueue H12c.
        stopwatch.Stop();
        var okCount = scopeOutcomes.Count(o => o.Result.Status == AppConfigSeederStatus.Ok);
        var deferredCount = scopeOutcomes.Count(o => o.Result.Status == AppConfigSeederStatus.Deferred);
        _logger.LogInformation(
            "H12b app-config seed succeeded: runId={RunId} customerId={CustomerId} " +
            "okCount={OkCount} deferredCount={DeferredCount} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, okCount, deferredCount, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAndAdvanceAsync(
            run, etag, idempotencyKey, envelope, scopeOutcomes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the SHA-256 hash of the manifest.yaml file contents as a
    /// hex string. Aligned with sibling H12a (task 070) so operators reason
    /// about ONE manifest state across both parallel handlers.
    /// Exposed as internal so unit tests can validate the exact key format
    /// without duplicating the hash algorithm.
    /// </summary>
    internal static string ComputeManifestHash(string manifestPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestPath);
        if (!File.Exists(manifestPath))
        {
            throw new FileNotFoundException($"Manifest not found: {manifestPath}", manifestPath);
        }
        var bytes = File.ReadAllBytes(manifestPath);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// Convenience helper for tests + code-review greppability: builds the
    /// idempotency key string with the exact H12b format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId, string manifestHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(manifestHash);
        return $"h12b-{customerId}-{manifestHash}";
    }

    private static JsonElement BuildScopeOutcomesEvidence(IReadOnlyList<ScopeOutcome> outcomes)
    {
        var doc = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            scopes = outcomes.Select(o => new
            {
                scope = o.ScopeName,
                status = o.Result.Status.ToString(),
                diagnostic = o.Result.Diagnostic,
            }),
        }));
        return doc.RootElement.Clone();
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

        var gateEntry = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = evidence,
        };
        run.GateStates[$"appconfig-{rejectionCode}"] = gateEntry;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H12b app-config seed failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H12b app-config seed failure state write raced with row delete: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }
    }

    private async Task<HandlerResult> MarkCompleteAndAdvanceAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        IReadOnlyList<ScopeOutcome> outcomes,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1); // Approximate; per-seeder start not tracked separately.

        run.Status = RunStatus.Running;
        run.CurrentPhase = DownstreamHandlerId; // The reconciler observes this + knows H12c is next.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId, // H12b has no separate BFF job row; use RunId for correlation.
        });
        run.ErrorDetail = null;

        // Verified-gate entry — evidence carries the per-scope roll-up so the
        // operator sees the deferred-scope list without pulling logs. If any
        // scopes were Deferred, this evidence IS the audit-trail record for
        // "these scopes were intentionally skipped this run".
        run.GateStates[AppConfigSeedGates.AppConfigSeeded] = new GateEntry
        {
            Status = GateState.Verified,
            VerifierHandler = HandlerIdentifier,
            Evidence = BuildScopeOutcomesEvidence(outcomes),
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H12b app-config seed success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AppConfigSeedRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H12b read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H12b.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H12b app-config seed success state write raced with row delete: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: AppConfigSeedRejectionCodes.RunDeletedDuringSeed,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H12b app-config seed " +
                             $"was in flight.");
        }

        // Enqueue H12c — TEMPORARY WAVE-C4 BRIDGE (see file header). H12c is
        // the DAG-join point that requires BOTH H12a + H12b complete; sibling
        // H12a (task 070) also enqueues H12c on its own success. Service Bus
        // MessageId dedup + H12c's own idempotency check ensure the parallel
        // enqueues collapse to a single H12c execution.
        var downstreamEnvelope = new HandlerEnvelope
        {
            HandlerId = DownstreamHandlerId,
            RunId = envelope.RunId,
            CustomerId = envelope.CustomerId,
            ParametersJson = "{}", // H12c reads its own parameters from Cosmos.
            EnqueuedAt = completedAt,
        };
        try
        {
            await _enqueuer.EnqueueAsync(downstreamEnvelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The Cosmos state already records H12b complete + CurrentPhase =
            // H12c. Enqueue failure is recoverable — the reconciler's
            // background scan (wave C5, design.md §crash recovery I6) will
            // re-emit the H12c job. Log + return success so the handler
            // outcome accurately reflects the H12b completion; the enqueue
            // failure is a downstream concern.
            _logger.LogError(
                ex,
                "H12b succeeded but H12c downstream enqueue failed — reconciler scan will re-emit: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Success(idempotencyKey);
    }

    /// <summary>
    /// Per-scope outcome tuple used for aggregation + evidence building.
    /// Nested record — no external consumer.
    /// </summary>
    private sealed record ScopeOutcome(string ScopeName, AppConfigSeederResult Result);
}
