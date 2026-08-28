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
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-34 (Wave G-8
//       Batch 10 — upgrade-mode version-compat gate): when `provisionedOn` is
//       populated (upgrade of an already-provisioned customer), H0 queries the
//       version-compat matrix (docs/deployment/version-compatibility-matrix.md,
//       runtime mirror version-compat-matrix.json) BEFORE any quota probe
//       fires. Red → block fast (Resumable, `upgrade-compat-red`); Yellow →
//       warn + record a Pending `upgrade-compat-yellow` gate entry (operator
//       ACK per matrix doc §5) but allow the run to proceed; Green → proceed.
//       Upgrade-mode detection + version inputs follow the established
//       registry-mirrored run-parameter convention (parity with H2a/H4/H8's
//       ProvisionedOnParameterKey — H0 stays Dataverse-client-free): the L2
//       intake mirrors `sprk_provisionedon` / `sprk_bffversion` /
//       `sprk_solutionversion` into run parameters, and the release manifest
//       supplies the target versions.
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
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
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

    /// <summary>
    /// Non-secret parameter mirroring the registry's <c>sprk_provisionedon</c>.
    /// Non-empty ⇒ upgrade mode (spec.md FR-34). Naming parity with
    /// H2a/H4/H8's <c>ProvisionedOnParameterKey</c>.
    /// </summary>
    public const string ProvisionedOnParameterKey = "provisionedOn";

    /// <summary>Upgrade mode: current BFF version (registry mirror of <c>sprk_bffversion</c>).</summary>
    public const string CurrentBffVersionParameterKey = "currentBffVersion";

    /// <summary>Upgrade mode: current Solution-set version (registry mirror of <c>sprk_solutionversion</c>).</summary>
    public const string CurrentSolutionVersionParameterKey = "currentSolutionVersion";

    /// <summary>Upgrade mode: incoming release's BFF version (release manifest).</summary>
    public const string TargetBffVersionParameterKey = "targetBffVersion";

    /// <summary>Upgrade mode: incoming release's Solution-set version (release manifest).</summary>
    public const string TargetSolutionVersionParameterKey = "targetSolutionVersion";

    /// <summary>Gate id recorded on a Yellow verdict (operator manual-step ACK per matrix doc §5).</summary>
    public const string UpgradeCompatYellowGateId = "upgrade-compat-yellow";

    /// <summary>
    /// COMP-10 (SESSION 17): non-secret parameter carrying the operator-declared
    /// cost tier (matches intake.schema.json `tier` enum:
    /// <c>shared-trial</c>|<c>smb</c>|<c>enterprise</c>|<c>dedicated</c>).
    /// Used to look up the monthly cost ceiling in <see cref="H0Options.GetCeilingUsd"/>.
    /// </summary>
    public const string TierParameterKey = "tier";

    /// <summary>
    /// COMP-10 (SESSION 17): non-secret parameter carrying the run's projected
    /// monthly cost in USD as an invariant-culture decimal string (e.g.,
    /// <c>"425"</c>, <c>"1200.50"</c>). Populated by the SKILL.md Step 2
    /// BAT-10 client-side estimator OR by a direct-API caller passing its
    /// own estimate. Missing/blank → the H0 cost gate LOG-ONLY skips
    /// (no fail); values that fail invariant-culture decimal parse → same
    /// skip with a WARN log line so operators notice.
    /// </summary>
    public const string EstimatedMonthlyUsdParameterKey = "estimatedMonthlyUsd";

    /// <summary>
    /// COMP-10 (SESSION 17): non-secret parameter carrying the operator's
    /// cost-envelope policy — mirrors intake.schema.json `costEnvelopePolicy`
    /// enum (<c>abortOnOverrun</c>|<c>warnAndProceed</c>). Any value other
    /// than the exact literal <c>warnAndProceed</c> is treated as
    /// <c>abortOnOverrun</c> (default-strict per COMP-10 binding). The
    /// SKILL.md Step 1.0 batch loader rejects a Model2Dedicated intake that
    /// pairs <c>warnAndProceed</c> — H0 does NOT re-enforce that pair
    /// invariant (already caught at the boundary), only the overrun-vs-policy
    /// decision.
    /// </summary>
    public const string CostEnvelopePolicyParameterKey = "costEnvelopePolicy";

    /// <summary>Literal value of <see cref="CostEnvelopePolicyParameterKey"/> that skips the abort branch.</summary>
    public const string CostEnvelopePolicyWarnAndProceed = "warnAndProceed";

    /// <summary>Machine-stable rejection code emitted when COMP-10 aborts H0.</summary>
    public const string CostOverrunRejectionCode = "quota-cost-overrun";

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
    private readonly IVersionCompatMatrix _versionCompatMatrix;
    private readonly H0Options _options;
    private readonly ILogger<H0PreflightHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H0 preflight handler.
    /// </summary>
    /// <param name="repository">Cosmos-backed run state store (task 037).</param>
    /// <param name="enqueuer">Service Bus enqueuer used to dispatch H0.5 on success (wave-C4 temporary bridge — see file header).</param>
    /// <param name="probes">The four preflight probes. Registration order does not matter; the handler orchestrates all four in parallel and aggregates results.</param>
    /// <param name="versionCompatMatrix">Version-compat matrix queried in upgrade mode ONLY (spec.md FR-34; Wave G-8 Batch 10).</param>
    /// <param name="options">COMP-10 (SESSION 17): H0 options — cost-envelope gate configuration. Optional (defaults to <see cref="H0Options"/> defaults when null so the handler stays constructible in ad-hoc tests without an Options.Create wrapper).</param>
    /// <param name="logger">Structured logger.</param>
    public H0PreflightHandler(
        IProvisioningRunRepository repository,
        IHandlerEnqueuer enqueuer,
        IEnumerable<IPreflightQuotaProbe> probes,
        IVersionCompatMatrix versionCompatMatrix,
        IOptions<H0Options>? options,
        ILogger<H0PreflightHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(enqueuer);
        ArgumentNullException.ThrowIfNull(probes);
        ArgumentNullException.ThrowIfNull(versionCompatMatrix);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _enqueuer = enqueuer;
        _probes = probes;
        _versionCompatMatrix = versionCompatMatrix;
        // options may be null (test scaffolding) — fall back to defaults so
        // the gate stays ON (fail-safe) rather than silently disabled.
        _options = options?.Value ?? new H0Options();
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

        // (3.3) COMP-10 (SESSION 17) — cost-envelope gate. Fires BEFORE any
        // Azure probe so an over-budget run blocks fast with zero API calls.
        // Skipped entirely when `Options.CostEnvelopeAbortsPreflight = false`
        // OR when the required nonSecret parameters (tier + estimatedMonthlyUsd)
        // are absent/unparseable (LOG-ONLY skip so operators notice missing
        // plumbing without a hard-fail on runs that predate the schema
        // addition). See CheckCostEnvelopeAsync for the full decision matrix.
        var costFailure = await CheckCostEnvelopeAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (costFailure is not null)
        {
            return costFailure;
        }

        // (3.5) FR-34 upgrade-mode version-compat gate (Wave G-8 Batch 10).
        // Fires BEFORE any quota probe so an incompatible pair blocks fast
        // with zero Azure API calls. Skipped entirely for first-install runs
        // (no `provisionedOn` parameter).
        if (run.Parameters.NonSecret.TryGetValue(ProvisionedOnParameterKey, out var provisionedOnRaw)
            && !string.IsNullOrWhiteSpace(provisionedOnRaw))
        {
            var compatFailure = await CheckUpgradeCompatAsync(run, etag, cancellationToken).ConfigureAwait(false);
            if (compatFailure is not null)
            {
                return compatFailure;
            }
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

    /// <summary>
    /// COMP-10 (SESSION 17) cost-envelope gate. Returns a terminal
    /// <see cref="HandlerResult.Failure"/> when the run's projected monthly
    /// cost exceeds the tier ceiling AND the run's costEnvelopePolicy is not
    /// <see cref="CostEnvelopePolicyWarnAndProceed"/> AND
    /// <see cref="H0Options.CostEnvelopeAbortsPreflight"/> is true. Returns
    /// <c>null</c> in every other case (gate disabled / missing parameters /
    /// unknown tier / under-budget / warnAndProceed policy).
    ///
    /// Decision matrix:
    ///   CostEnvelopeAbortsPreflight = false            → null (log-only skip)
    ///   tier missing or unknown                        → null (WARN log)
    ///   estimatedMonthlyUsd missing or unparseable     → null (WARN log)
    ///   estimatedMonthlyUsd ≤ ceiling                  → null (INFO log)
    ///   estimatedMonthlyUsd > ceiling + warnAndProceed → null (WARN log)
    ///   estimatedMonthlyUsd > ceiling + abortOnOverrun → Failure(Resumable, quota-cost-overrun)
    ///
    /// Skipped/log-only branches never mutate Cosmos state — H0's own tenant
    /// guard already wrote to Cosmos if needed; the cost gate is additive.
    /// </summary>
    private async Task<HandlerResult?> CheckCostEnvelopeAsync(
        ProvisioningRun run,
        string etag,
        CancellationToken cancellationToken)
    {
        if (!_options.CostEnvelopeAbortsPreflight)
        {
            _logger.LogInformation(
                "H0 cost-envelope gate DISABLED via H0Options.CostEnvelopeAbortsPreflight=false — skipping " +
                "for runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return null;
        }

        if (!TryGetNonEmpty(run.Parameters.NonSecret, TierParameterKey, out var tier))
        {
            _logger.LogWarning(
                "H0 cost-envelope gate SKIPPED — nonSecret['{Key}'] absent for runId={RunId} customerId={CustomerId}. " +
                "Populate `tier` via the intake schema so H0 can enforce the tier ceiling.",
                TierParameterKey, run.RunId, run.CustomerId);
            return null;
        }

        var ceiling = _options.GetCeilingUsd(tier);
        if (ceiling is null)
        {
            _logger.LogWarning(
                "H0 cost-envelope gate SKIPPED — tier '{Tier}' has no ceiling in H0Options.TierMonthlyCostCeilingsUsd " +
                "AND no built-in default (runId={RunId} customerId={CustomerId}). Add the tier to the intake schema " +
                "enum + configure a ceiling, or accept the skip as intentional for an out-of-band tier.",
                tier, run.RunId, run.CustomerId);
            return null;
        }

        if (!TryGetNonEmpty(run.Parameters.NonSecret, EstimatedMonthlyUsdParameterKey, out var estimatedRaw))
        {
            _logger.LogWarning(
                "H0 cost-envelope gate SKIPPED — nonSecret['{Key}'] absent for runId={RunId} customerId={CustomerId} " +
                "(tier='{Tier}', ceiling=${Ceiling}). Populate `estimatedMonthlyUsd` via the intake schema so H0 can " +
                "enforce the cost envelope.",
                EstimatedMonthlyUsdParameterKey, run.RunId, run.CustomerId, tier, ceiling);
            return null;
        }

        if (!decimal.TryParse(estimatedRaw, NumberStyles.Number, CultureInfo.InvariantCulture, out var estimatedMonthlyUsd))
        {
            _logger.LogWarning(
                "H0 cost-envelope gate SKIPPED — nonSecret['{Key}']='{Raw}' is not a valid invariant-culture decimal " +
                "for runId={RunId} customerId={CustomerId}. Fix the value in the intake (e.g., '425' or '1200.50').",
                EstimatedMonthlyUsdParameterKey, estimatedRaw, run.RunId, run.CustomerId);
            return null;
        }

        if (estimatedMonthlyUsd <= ceiling.Value)
        {
            _logger.LogInformation(
                "H0 cost-envelope gate PASS — estimated ${Estimated}/mo ≤ tier '{Tier}' ceiling ${Ceiling}/mo " +
                "(runId={RunId} customerId={CustomerId})",
                estimatedMonthlyUsd, tier, ceiling, run.RunId, run.CustomerId);
            return null;
        }

        // Overrun path — decision hinges on policy.
        var policy = run.Parameters.NonSecret.TryGetValue(CostEnvelopePolicyParameterKey, out var policyRaw)
            ? policyRaw
            : null;

        // Bucket B HIGH#12 SESSION 18 (customer-provisioning-orchestration-r1
        // adversarial e2e verify workflow wepdcb8we): the intake schema
        // (scripts/provisioning-prereqs/intake.schema.json costEnvelopePolicy
        // description) declares "warnAndProceed MUST reject for Model2Dedicated";
        // the SKILL.md batch loader enforces it at Step 1.0 line 533-535 for
        // skill-dispatched runs. But direct-API callers (retry scripts / ad-hoc
        // curl / future non-skill orchestrators) bypass that check by POSTing
        // directly to /api/runs. The server-side gate MUST close the
        // asymmetry. When tenancyModel=Model2Dedicated is combined with
        // warnAndProceed, force the fall-through to abortOnOverrun — a
        // dedicated stamp running uncapped-budget contradicts the schema
        // invariant regardless of who POSTed the request.
        //
        // ControlPlaneEnv=prod is the second dimension the schema names
        // ("Model 1 shared-trial ONLY"). Not enforced HERE today because
        // controlPlaneEnv is NOT currently plumbed into run.Parameters.NonSecret
        // (SKILL.md Step 4.0 nonSecretParameters block lists tenantId /
        // subscriptionId / openAiLocation / confirmationAcknowledgment /
        // intakeFileSha256 / region / tier / estimatedMonthlyUsd /
        // costEnvelopePolicy / operatorUpn — but NOT controlPlaneEnv). Adding
        // the field here as a defensive-null-check would be dead code; when a
        // future change plumbs controlPlaneEnv, extend the OR-clause below.
        var isModel2Dedicated = string.Equals(run.TenancyModel, "Model2Dedicated", StringComparison.Ordinal);
        if (isModel2Dedicated
            && string.Equals(policy, CostEnvelopePolicyWarnAndProceed, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "H0 cost-envelope gate WARN-AND-PROCEED REJECTED (Bucket B HIGH#12 SESSION 18): " +
                "tenancyModel=Model2Dedicated MUST NOT permit warnAndProceed per intake.schema.json " +
                "costEnvelopePolicy description (Model2Dedicated dedicated-stamp runs bar uncapped budget). " +
                "Forcing fall-through to abortOnOverrun. runId={RunId} customerId={CustomerId} " +
                "estimated=${Estimated}/mo tier='{Tier}' ceiling=${Ceiling}/mo.",
                run.RunId, run.CustomerId, estimatedMonthlyUsd, tier, ceiling);
            policy = null; // Force the abortOnOverrun default branch below.
        }
        else if (string.Equals(policy, CostEnvelopePolicyWarnAndProceed, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "H0 cost-envelope gate WARN-AND-PROCEED — estimated ${Estimated}/mo > tier '{Tier}' ceiling " +
                "${Ceiling}/mo but nonSecret['{PolicyKey}']='{Policy}' explicitly requests proceed " +
                "(runId={RunId} customerId={CustomerId}). Only permitted for Model1Shared per intake.schema.json " +
                "costEnvelopePolicy description; Model2Dedicated + warnAndProceed is rejected above per Bucket B " +
                "HIGH#12 SESSION 18.",
                estimatedMonthlyUsd, tier, ceiling, CostEnvelopePolicyParameterKey, policy,
                run.RunId, run.CustomerId);
            return null;
        }

        // Default (abortOnOverrun or unspecified) → fail Resumable.
        var diagnostic =
            $"Cost-envelope overrun: estimated ${estimatedMonthlyUsd:F2}/mo exceeds tier '{tier}' ceiling " +
            $"${ceiling:F2}/mo (COMP-10). Set nonSecret['{CostEnvelopePolicyParameterKey}']='{CostEnvelopePolicyWarnAndProceed}' " +
            "on Model 1 shared-trial runs to warn-and-proceed OR reduce the projected cost + resume. Batch-mode " +
            "operators fix the estimate in the intake JSON; interactive operators re-invoke the skill.";
        _logger.LogWarning(
            "H0 cost-envelope gate ABORT — estimated ${Estimated}/mo > tier '{Tier}' ceiling ${Ceiling}/mo " +
            "(runId={RunId} customerId={CustomerId} policy={Policy}) — rejecting with {RejectionCode}.",
            estimatedMonthlyUsd, tier, ceiling, run.RunId, run.CustomerId, policy ?? "<absent>",
            CostOverrunRejectionCode);
        await MarkFailedAsync(
            run, etag, CostOverrunRejectionCode, diagnostic,
            evidence: BuildCostOverrunEvidence(tier, estimatedMonthlyUsd, ceiling.Value, policy),
            cancellationToken).ConfigureAwait(false);
        return new HandlerResult.Failure(FailureClass.Resumable, CostOverrunRejectionCode, diagnostic);
    }

    private static JsonElement BuildCostOverrunEvidence(
        string tier,
        decimal estimatedMonthlyUsd,
        decimal ceilingUsd,
        string? policy)
        => JsonSerializer.SerializeToElement(new
        {
            tier,
            estimatedMonthlyUsd,
            ceilingUsd,
            overageUsd = estimatedMonthlyUsd - ceilingUsd,
            costEnvelopePolicy = policy ?? "abortOnOverrun (default)",
            rejectionCode = CostOverrunRejectionCode,
        });

    /// <summary>
    /// FR-34 upgrade-mode version-compat gate. Returns a terminal
    /// <see cref="HandlerResult.Failure"/> when the run must block (missing
    /// version parameters / matrix unavailable / Red pair), or <c>null</c>
    /// when the run may proceed (Green, or Yellow — which records a Pending
    /// <see cref="UpgradeCompatYellowGateId"/> gate entry on the in-memory
    /// run so it persists with the next Cosmos write, per matrix doc §5
    /// operator-ACK guidance, without blocking H0 itself).
    /// </summary>
    private async Task<HandlerResult?> CheckUpgradeCompatAsync(
        ProvisioningRun run,
        string etag,
        CancellationToken cancellationToken)
    {
        var parameters = run.Parameters.NonSecret;

        var missing = new List<string>(4);
        if (!TryGetNonEmpty(parameters, CurrentBffVersionParameterKey, out var currentBff)) { missing.Add(CurrentBffVersionParameterKey); }
        if (!TryGetNonEmpty(parameters, CurrentSolutionVersionParameterKey, out var currentSolutions)) { missing.Add(CurrentSolutionVersionParameterKey); }
        if (!TryGetNonEmpty(parameters, TargetBffVersionParameterKey, out var targetBff)) { missing.Add(TargetBffVersionParameterKey); }
        if (!TryGetNonEmpty(parameters, TargetSolutionVersionParameterKey, out var targetSolutions)) { missing.Add(TargetSolutionVersionParameterKey); }
        if (missing.Count > 0)
        {
            const string rejectionCode = "upgrade-compat-missing-versions";
            var diagnostic =
                $"Upgrade mode (non-empty '{ProvisionedOnParameterKey}') requires version parameters " +
                $"[{string.Join(", ", missing)}] for the FR-34 version-compat matrix query. The L2 intake mirrors " +
                "currentBffVersion/currentSolutionVersion from the customer's sprk_dataverseenvironment registry row " +
                "(sprk_bffversion / sprk_solutionversion) and targetBffVersion/targetSolutionVersion from the " +
                "release manifest. Populate the parameters + resume.";
            await MarkFailedAsync(run, etag, rejectionCode, diagnostic, evidence: null, cancellationToken)
                .ConfigureAwait(false);
            return new HandlerResult.Failure(FailureClass.Resumable, rejectionCode, diagnostic);
        }

        VersionCompatCheckResult compat;
        try
        {
            compat = await _versionCompatMatrix.CheckPairAsync(
                new VersionPair(currentBff, currentSolutions),
                new VersionPair(targetBff, targetSolutions),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Matrix-source infrastructure fault (missing/corrupt matrix file).
            // Resumable: operator repairs the matrix deployment + resumes; no
            // external side effect has occurred.
            _logger.LogError(
                ex,
                "H0 version-compat matrix query failed: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            const string rejectionCode = "upgrade-compat-matrix-unavailable";
            var diagnostic =
                $"Version-compat matrix unavailable: {ex.GetType().Name}: {ex.Message}. Upgrade runs MUST NOT " +
                "proceed without the FR-34 compatibility verdict — repair the matrix source " +
                "(version-compat-matrix.json / Preflight:VersionCompatMatrixPath) and resume.";
            await MarkFailedAsync(run, etag, rejectionCode, diagnostic, evidence: null, cancellationToken)
                .ConfigureAwait(false);
            return new HandlerResult.Failure(FailureClass.Resumable, rejectionCode, diagnostic);
        }

        switch (compat.Verdict)
        {
            case VersionCompatVerdict.Red:
                {
                    const string rejectionCode = "upgrade-compat-red";
                    _logger.LogWarning(
                        "H0 upgrade blocked by version-compat matrix (Red): runId={RunId} customerId={CustomerId} " +
                        "currentBff={CurrentBff} currentSolutions={CurrentSolutions} targetBff={TargetBff} targetSolutions={TargetSolutions}",
                        run.RunId, run.CustomerId, currentBff, currentSolutions, targetBff, targetSolutions);
                    await MarkFailedAsync(
                        run, etag, rejectionCode, compat.Diagnostic,
                        evidence: BuildCompatEvidence(compat, currentBff, currentSolutions, targetBff, targetSolutions),
                        cancellationToken).ConfigureAwait(false);
                    return new HandlerResult.Failure(FailureClass.Resumable, rejectionCode, compat.Diagnostic);
                }

            case VersionCompatVerdict.Yellow:
                // Warn-but-allow: the run proceeds, but the operator manual-step
                // obligation (matrix doc §5 U-CB-N remediation + ACK before
                // H2a/H6/H9) is recorded as a Pending gate entry. Persisted by
                // the NEXT ReplaceRunAsync write on this in-flight run (success
                // advance or a later probe failure) — H0 does not spend an
                // extra Cosmos round trip on it.
                _logger.LogWarning(
                    "H0 version-compat verdict Yellow (proceeding with operator-ACK gate recorded): runId={RunId} " +
                    "customerId={CustomerId} ucbClasses={UcbClasses} diagnostic={Diagnostic}",
                    run.RunId, run.CustomerId, string.Join(",", compat.UcbClasses), compat.Diagnostic);
                run.GateStates[UpgradeCompatYellowGateId] = new GateEntry
                {
                    Status = GateState.Pending,
                    VerifierHandler = HandlerIdentifier,
                    Evidence = BuildCompatEvidence(compat, currentBff, currentSolutions, targetBff, targetSolutions),
                };
                return null;

            default:
                _logger.LogInformation(
                    "H0 version-compat verdict Green: runId={RunId} customerId={CustomerId} targetBff={TargetBff} targetSolutions={TargetSolutions}",
                    run.RunId, run.CustomerId, targetBff, targetSolutions);
                return null;
        }
    }

    private static JsonElement BuildCompatEvidence(
        VersionCompatCheckResult compat,
        string currentBff,
        string currentSolutions,
        string targetBff,
        string targetSolutions)
        => JsonSerializer.SerializeToElement(new
        {
            verdict = compat.Verdict.ToString(),
            currentBffVersion = currentBff,
            currentSolutionVersion = currentSolutions,
            targetBffVersion = targetBff,
            targetSolutionVersion = targetSolutions,
            ucbClasses = compat.UcbClasses,
            diagnostic = compat.Diagnostic,
        });

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

    private static string BuildRejectionCode(string checkName) => checkName switch
    {
        PreflightCheckNames.AzureOpenAiTpmHeadroom => "quota-openai-tpm",
        PreflightCheckNames.DataverseEnvCreationRate => "quota-dataverse-env-rate",
        PreflightCheckNames.SubscriptionVCpuQuota => "quota-subscription-vcpu",
        PreflightCheckNames.SpeCertBootstrap => "spe-cert-bootstrap-missing",
        // HANDLER-03 (pre-dispatch audit 2026-08-27) — F1 verbatim rejection
        // code the punchlist mandates so operators can filter for the
        // specific fast-fail without string-matching the diagnostic.
        PreflightCheckNames.OpenAiPinFreshness => "quota-openai-pin-stale",
        _ => $"preflight-{checkName.ToLowerInvariant()}",
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
