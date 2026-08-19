// -----------------------------------------------------------------------------
// H0PreflightHandler.cs
//
// The first handler in the provisioning pipeline (task 041, wave C4). Validates
// run parameters + queries the four quota / readiness sources — Azure OpenAI
// regional TPM, Dataverse env-creation rate, subscription vCPU, SPE cert-
// bootstrap — and BLOCKS the run before H1 starts if any headroom is
// insufficient.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-01 acceptance:
//       run blocked with clear error IF any headroom insufficient for +1
//       provisioning target; blocks before H1 starts. Idempotency key:
//       preflight-{customerId}-{paramHash}.
//   - projects/customer-provisioning-orchestration-r1/spec.md NFR-12:
//       Regional TPM headroom check MUST fail run before H1 if projected +1
//       capacity exceeds the 150+200+30+350 sum per-model regional quota.
//   - projects/customer-provisioning-orchestration-r1/spec.md § 4D I1:
//       MUST NOT hardcode default tenant — accept tenantId from run
//       parameters and pass explicitly to every probe.
//   - projects/customer-provisioning-orchestration-r1/design.md § 4.1 H0 row +
//       § 4C rollback: H0 preflight failures are Resumable class (operator
//       resolves external precondition + POST /api/runs/{id}/resume).
//   - projects/customer-provisioning-orchestration-r1/design.md § 15 north-star:
//       lead-time items surfaced by H0 BEFORE the 30-min Bicep step, not
//       after.
//   - scripts/preflight/README.md § Purpose: 4 checks + shared return
//       contract; H0 orchestrates all four in parallel.
//
// ROLLBACK CLASSIFICATION (§ 4C):
//   H0 writes NO external side effect — the four probes are pure read-only
//   quota queries. On any failure, the ProvisioningRun transitions to
//   Status = Failed with a distinct RejectionCode per failing check, but no
//   quarantine is warranted. Operator addresses the missing precondition
//   (quota bump / cert bootstrap) then invokes POST /api/runs/{id}/resume,
//   which re-dispatches H0 with the same envelope.
//
// DOWNSTREAM ENQUEUE (WAVE C4 TEMPORARY BRIDGE):
//   The IHandlerEnqueuer contract note (Enqueue/IHandlerEnqueuer.cs § NOT
//   CONSUMED BY) states that handler bodies should NOT re-enqueue via L2 —
//   handler-to-handler chaining is the wave-C5 reconciler's job. In wave C4
//   the reconciler does not exist yet, so H0 enqueues H0.5 directly here
//   as a TEMPORARY BRIDGE. When the wave-C5 reconciler ships, this
//   handler's enqueue call moves out to the reconciler + the enqueuer
//   contract note applies unchanged. Documented in the H0 report-back for
//   task 041 + tracked in project notes for wave C5 follow-up.
//
// IDEMPOTENCY:
//   Level 1 (wire): ServiceBusHandlerEnqueuer sets a deterministic
//                   MessageId per (HandlerId, RunId, CustomerId, paramHash);
//                   SB duplicate-detection drops re-enqueues within its
//                   dedup window.
//   Level 2 (Redis): NOT YET IMPLEMENTED in L2 (this project has no Redis
//                    dependency); Wave-C5 reconciler may add if latency
//                    profiling warrants.
//   Level 3 (durable): This handler scans ProvisioningRun.CompletedPhases
//                      for an entry with (Phase=="H0", IdempotencyKey ==
//                      preflight-{customerId}-{paramHash}) — returns
//                      HandlerResult.Success no-op on hit. Cosmos writes
//                      are ETag-guarded via ReplaceRunAsync (FR-23 I5).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.Preflight;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H0PreflightHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H0;

    /// <summary>Handler identifier of the downstream handler H0 chains to on success.</summary>
    public const string DownstreamHandlerId = "H0.5";

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§ 4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    private static readonly JsonSerializerOptions ParameterHashSerializerOptions = new()
    {
        // Canonical JSON for hashing — sorted keys + no whitespace + no
        // enum-name variance. Deterministic across runtimes so paramHash
        // matches whether the handler runs in-process or under the future
        // reconciler.
        WriteIndented = false,
    };

    private readonly IProvisioningRunRepository _repository;
    private readonly IHandlerEnqueuer _enqueuer;
    private readonly IEnumerable<IPreflightQuotaProbe> _probes;
    private readonly ILogger<H0PreflightHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H0 preflight handler.
    /// </summary>
    /// <param name="repository">Cosmos-backed run state store (task 037).</param>
    /// <param name="enqueuer">Service Bus enqueuer used to dispatch H0.5 on success (wave-C4 temporary bridge — see file header).</param>
    /// <param name="probes">The four preflight probes. Registration order does not matter; the handler orchestrates all four in parallel and aggregates results.</param>
    /// <param name="logger">Structured logger.</param>
    public H0PreflightHandler(
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        IEnumerable<IPreflightQuotaProbe> probes,
        ILogger<H0PreflightHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _enqueuer = enqueuer;
        _probes = probes;
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
                $"H0PreflightHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H0 preflight starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H0 preflight aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: "run-not-found",
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var idempotencyKey = ComputeIdempotencyKey(envelope.CustomerId, run.Parameters);

        // (2) Level-3 idempotency: durable no-op on duplicate.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H0 preflight idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey}",
                envelope.RunId, idempotencyKey);
            return new HandlerResult.Success(idempotencyKey);
        }

        // (3) § 4D I1 tenant guard — probes need tenantId; fail BEFORE any
        // probe fires so the operator sees the specific rejection code and
        // no Azure API call happens with a default tenant.
        if (!run.Parameters.NonSecret.TryGetValue(TenantIdParameterKey, out var tenantId)
            || string.IsNullOrWhiteSpace(tenantId))
        {
            const string rejectionCode = "missing-tenant-id";
            const string diagnostic =
                "Run parameter 'tenantId' is required by H0 preflight (spec § 4D I1 no-hardcoded-tenant). " +
                "Model 1 = Spaarke tenant; Model 2 = customer tenant captured via H0.5 consent-callback.";
            await MarkFailedAsync(run, etag, rejectionCode, diagnostic, evidence: null, cancellationToken)
                .ConfigureAwait(false);
            return new HandlerResult.Failure(FailureClass.Resumable, rejectionCode, diagnostic);
        }

        // (4) Execute all probes in parallel. Each probe is independent per
        // scripts/preflight/README.md constraint: "no cross-module state".
        // Task.WhenAll respects the cancellation token via the individual
        // CheckAsync calls.
        // Copy into a fresh Dictionary so the PreflightProbeInput's
        // IReadOnlyDictionary parameter binds (RunParameters.NonSecret is
        // typed IDictionary; the two interfaces are distinct in .NET). This
        // is a defensive-copy anyway — probes MUST NOT mutate this map.
        // NOTE (coord with task 042): call-site fix applied while H0.5 handler
        // was being added; if RunParameters is later re-typed to Dictionary
        // (implements both interfaces), this copy can be dropped.
        var input = new PreflightProbeInput(
            envelope.CustomerId,
            tenantId,
            new Dictionary<string, string>(run.Parameters.NonSecret));
        PreflightCheckResult[] results;
        try
        {
            results = await Task.WhenAll(_probes.Select(p => p.CheckAsync(input, cancellationToken)))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Infrastructure error inside a probe (e.g. pwsh missing, script
            // parse failure). Domain-check failures do NOT throw — they
            // return Passed=false with a diagnostic. Anything reaching here
            // is an unexpected runtime fault; classify as Resumable so the
            // operator can fix the infra + resume without quarantine.
            _logger.LogError(
                ex,
                "H0 preflight probe threw unexpected exception: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"Preflight probe infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Verify pwsh is on PATH and scripts/preflight/ ships in the L2 publish output (spec.md NFR-05).";
            await MarkFailedAsync(run, etag, "probe-infrastructure-error", diagnostic, evidence: null, cancellationToken)
                .ConfigureAwait(false);
            return new HandlerResult.Failure(FailureClass.Resumable, "probe-infrastructure-error", diagnostic);
        }

        // (5) Aggregate — H0 blocks on ANY failure (POML constraint: "no advisory quota results").
        var failed = results.FirstOrDefault(r => !r.Passed);
        if (failed is not null)
        {
            var rejectionCode = BuildRejectionCode(failed.CheckName);
            _logger.LogWarning(
                "H0 preflight failed: runId={RunId} customerId={CustomerId} check={CheckName} rejectionCode={RejectionCode}",
                envelope.RunId, envelope.CustomerId, failed.CheckName, rejectionCode);

            await MarkFailedAsync(run, etag, rejectionCode, failed.Diagnostic, failed.Headroom, cancellationToken)
                .ConfigureAwait(false);
            return new HandlerResult.Failure(FailureClass.Resumable, rejectionCode, failed.Diagnostic);
        }

        // (6) All probes passed — mark H0 complete + enqueue H0.5 (wave-C4 bridge — see file header).
        stopwatch.Stop();
        _logger.LogInformation(
            "H0 preflight succeeded: runId={RunId} customerId={CustomerId} durationMs={DurationMs} probeCount={ProbeCount}",
            envelope.RunId, envelope.CustomerId, stopwatch.ElapsedMilliseconds, results.Length);

        return await MarkCompleteAndAdvanceAsync(
            run, etag, idempotencyKey, envelope, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H0 idempotency key: <c>preflight-{customerId}-{paramHash}</c>.
    /// Exposed as internal so unit tests can construct expected keys without duplicating the
    /// canonical hash algorithm.
    /// </summary>
    internal static string ComputeIdempotencyKey(string customerId, RunParameters parameters)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        ArgumentNullException.ThrowIfNull(parameters);

        var paramHash = ComputeParametersHash(parameters);
        return $"preflight-{customerId}-{paramHash}";
    }

    private static string ComputeParametersHash(RunParameters parameters)
    {
        // Canonical JSON: sorted keys inside dictionaries, no whitespace.
        // Rebuild both dictionaries as SortedDictionary so the hash is
        // insensitive to insertion order (Cosmos may round-trip in any
        // order + fresh runs may build the dictionary differently).
        var canonical = new
        {
            nonSecret = new SortedDictionary<string, string>(parameters.NonSecret, StringComparer.Ordinal),
            secrets = new SortedDictionary<string, KeyVaultSecretRef>(parameters.Secrets, StringComparer.Ordinal),
        };
        var json = JsonSerializer.Serialize(canonical, ParameterHashSerializerOptions);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(hash);
    }

    private static string BuildRejectionCode(string checkName) => checkName switch
    {
        PreflightCheckNames.AzureOpenAiTpmHeadroom     => "quota-openai-tpm",
        PreflightCheckNames.DataverseEnvCreationRate   => "quota-dataverse-env-rate",
        PreflightCheckNames.SubscriptionVCpuQuota      => "quota-subscription-vcpu",
        PreflightCheckNames.SpeCertBootstrap           => "spe-cert-bootstrap-missing",
        _                                              => $"preflight-{checkName.ToLowerInvariant()}",
    };

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

        // Gate state — one entry per H0 preflight run outcome. Verifier is
        // H0; state is Pending (external precondition to resolve). Evidence
        // captures the probe's headroom hashtable so the operator has the
        // raw diagnostic without pulling logs.
        var gateEntry = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
            Evidence = evidence,
        };
        run.GateStates[$"preflight-{rejectionCode}"] = gateEntry;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            // Concurrent writer advanced the run between our read + write.
            // Log the winning state — a concurrent H0 invocation may have
            // already recorded a different failure OR the operator manually
            // updated the row. Do NOT retry the ETag write; the reconciler
            // observes the winning state.
            _logger.LogWarning(
                "H0 preflight failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H0 preflight failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }
    }

    private async Task<HandlerResult> MarkCompleteAndAdvanceAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = DateTimeOffset.UtcNow;
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1); // Approximate; probe start not tracked separately.

        run.Status = RunStatus.Running;
        run.CurrentPhase = DownstreamHandlerId; // The reconciler observes this + knows to dispatch H0.5 next.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId, // H0 has no separate BFF job row; use RunId for correlation.
        });
        run.ErrorDetail = null;

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            // Race with another concurrent writer — abort quietly. The
            // reconciler will observe the winning state + decide.
            _logger.LogWarning(
                "H0 preflight success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: "concurrent-write-conflict",
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H0 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H0.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H0 preflight success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: "run-deleted-during-preflight",
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H0 preflight was in flight.");
        }

        // Enqueue H0.5 — TEMPORARY WAVE-C4 BRIDGE (see file header). When
        // the wave-C5 reconciler ships, this call moves to the reconciler
        // + the enqueuer contract note (Enqueue/IHandlerEnqueuer.cs § NOT
        // CONSUMED BY) applies unchanged.
        var downstreamEnvelope = new HandlerEnvelope
        {
            HandlerId = DownstreamHandlerId,
            RunId = envelope.RunId,
            CustomerId = envelope.CustomerId,
            ParametersJson = "{}", // H0.5 reads its own parameters from Cosmos.
            EnqueuedAt = completedAt,
        };
        try
        {
            await _enqueuer.EnqueueAsync(downstreamEnvelope, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // The Cosmos state already records H0 complete + CurrentPhase =
            // H0.5. Enqueue failure is recoverable — the reconciler's
            // background scan (wave C5, design.md § crash recovery I6) will
            // re-emit the H0.5 job. Log + return success so the handler
            // outcome accurately reflects the H0 completion; the enqueue
            // failure is a downstream concern.
            _logger.LogError(
                ex,
                "H0 succeeded but H0.5 downstream enqueue failed — reconciler scan will re-emit: " +
                "runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
