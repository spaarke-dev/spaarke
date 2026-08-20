// -----------------------------------------------------------------------------
// H5DataverseEnvCreationHandler.cs
//
// L2 CONTROL-PLANE H5 Dataverse env-creation handler (task 048, wave C4).
//
// PURPOSE:
//   Provisions the per-customer Dataverse environment via the
//   IDataverseEnvCreator seam. Task 140 (Wave G-4, Option D hybrid) replaced
//   the interim `pac admin create-environment` shell-out
//   (PacAdminDataverseEnvCreator — retired, kept on disk unregistered) with
//   BapRestEnvironmentCreator: a pure HttpClient + DefaultAzureCredential
//   port of Provision-Customer.ps1 STEP 5/6's BAP admin REST create +
//   async-operation-polling sequence (design.md § 4.1 H5 row). After
//   creation, verifies
//   Web API reachability via `GET /api/data/v9.2/WhoAmI` and writes the
//   env URL to `ProvisioningRun.InterStepState.DataverseEnvUrl` so H6
//   (managed solution import), H7 (env-var values), and H10 (app user)
//   can consume it.
//
// SPEC / DESIGN references:
//   - projects/customer-provisioning-orchestration-r1/spec.md FR-08:
//       "Dataverse environment created. Interim: `pac admin create-environment` +
//       gate verified. Design target (deferred per M-10): TF `powerplatform_environment`
//       resource. Acceptance: `sprk_dataverseenvironment.sprk_dataverseurl`
//       populated + env is accessible."
//   - projects/customer-provisioning-orchestration-r1/spec.md §4D I1:
//       No hardcoded default tenant — MUST flow tenantId through to `pac
//       admin -TenantId` explicitly.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.1 H5:
//       Interim `pac admin` + gate; idempotency key `dvenv-{customerId}`.
//   - projects/customer-provisioning-orchestration-r1/design.md §4.2 (v3.2):
//       Fire-and-forget handler execution model — L2 REST endpoints enqueue
//       + return 202; reconciler owns advancement. H5 is 15–30 min per row
//       418 of design.md.
//   - projects/customer-provisioning-orchestration-r1/design.md §4C rollback:
//       Rate-limit / auth failure → Resumable; partial provisioning →
//       QuarantineRequired (orphaned env row); timeout → distinct Resumable
//       signal (EnvCreationTimeout).
//   - projects/customer-provisioning-orchestration-r1/design.md §6.2
//       interStepState field: `dataverseEnvUrl` is the H5-owned slot.
//   - .claude/adr/ADR-004-job-contract.md: idempotent + at-least-once safe.
//   - .claude/adr/ADR-010-di-minimalism.md: 3 seams (creator + probe +
//       repository) — ≥2 impls each, no NIH abstraction.
//   - .claude/adr/ADR-036-background-job-infrastructure.md: reuse Service
//       Bus + IJobHandler infrastructure (spec FR-22 / R20).
//   - .claude/adr/ADR-044-dataverse-guid-canonicalization.md: env URL is
//       NOT a GUID — canonicalization does not apply; URL stored verbatim.
//
// ROLLBACK CLASSIFICATION (§4C mapping — declared at code level):
//   ┌─────────────────────────────────────┬───────────────────────────┐
//   │ Failure mode                        │ §4C class                 │
//   ├─────────────────────────────────────┼───────────────────────────┤
//   │ Missing tenantId (§4D I1)           │ Resumable                 │
//   │ Missing region / tier               │ Resumable                 │
//   │ Run not found in Cosmos partition   │ Resumable                 │
//   │ pac auth failure                    │ Resumable                 │
//   │ Rate-limited                        │ Resumable                 │
//   │ Quota exhausted                     │ Resumable                 │
//   │ pac timeout (creation window)       │ Resumable (EnvCreationTimeout — distinct code) │
//   │ Partial provisioning                │ QuarantineRequired        │
//   │ Web API health check fails          │ Resumable (EnvHealthCheckFailed) │
//   │ Concurrent Cosmos writer conflict   │ Resumable                 │
//   │ Run row deleted mid-flight          │ Resumable                 │
//   └─────────────────────────────────────┴───────────────────────────┘
//
// IDEMPOTENCY (3-level per ADR-004 / design.md §4.1):
//   Level 1 (Service Bus MessageId dedup): the future reconciler computes
//           deterministic MessageId per (HandlerId, RunId, CustomerId,
//           paramHash); SB duplicate-detection collapses re-enqueues.
//   Level 2 (Redis IdempotencyService): NOT YET IMPLEMENTED in L2 (design.md
//           §4.1 preamble; parity with H0 / H0.5 / H1 / H2a).
//   Level 3 (handler body durable dedup): scans
//           ProvisioningRun.CompletedPhases for (Phase == "H5",
//           IdempotencyKey == dvenv-{customerId}). Match → Success no-op
//           (no pac invocation, no health probe). POML acceptance #4:
//           existing env for customerId returns Success with prior URL.
//           Idempotency key format is customer-only (no version token) per
//           design.md § 4.1 H5 row — Dataverse env creation is per-customer
//           + version-independent (unlike H2a bicepVer or H6 solutionVer).
//
// DOWNSTREAM ENQUEUE (WAVE C4 NOTE):
//   H5's successor in the DAG (design.md §4.1) is H6 (managed solution
//   import) — single-successor (unlike H2a's 3-way fan-out to H2b/H4/H5).
//   Wave C4 does NOT enqueue H6 from H5 for two reasons:
//     1. Parity with H2a: post-H2a fan-out is reconciler-owned; keeping H5
//        aligned means the reconciler owns all fan-out decisions uniformly.
//     2. H6 handler class (task 049, Wave C4 Batch 3D) is not guaranteed
//        landed when this handler ships; enqueueing by string constant
//        would risk emitting messages the ServiceBusJobProcessor cannot
//        route yet.
//   The Wave C5 reconciler observes CurrentPhase == "H5" +
//   CompletedPhases + fans out to H6. This handler mutates Cosmos state
//   (advancing CurrentPhase + CompletedPhases + InterStepState) and returns
//   Success.
//
// REGISTRY WRITE-BACK (WAVE C4 NOTE):
//   Dispatcher summary references "registers env URL back into
//   sprk_dataverseenvironment registry" but the L2 registry client
//   (IDataverseEnvironmentRegistryClient — task 042) is currently a
//   read-only interface with a Null placeholder impl pending Wave C5's
//   real Dataverse-backed impl. The authoritative surface for downstream
//   handlers in Wave C4 IS the Cosmos InterStepState.DataverseEnvUrl —
//   H6/H7/H10 read from Cosmos, not from Dataverse. Registry write-back
//   is deferred to when the real registry client lands with a
//   write-capable extension (its own change, not H5's).
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H5DataverseEnvCreationHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H5;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = "tenantId";

    /// <summary>Non-secret parameter key carrying the Dataverse region code (e.g. <c>unitedstates</c>).</summary>
    public const string RegionParameterKey = "region";

    /// <summary>Non-secret parameter key carrying the env tier (<c>Sandbox</c> / <c>Production</c> / <c>Trial</c>).</summary>
    public const string TierParameterKey = "tier";

    /// <summary>Non-secret parameter key carrying an optional friendly display name for the env.</summary>
    public const string DisplayNameParameterKey = "dataverseDisplayName";

    /// <summary>Verified gate identifier for the H5 env-provisioning post-condition.</summary>
    public const string EnvProvisionedGateId = "h5-dataverse-env-provisioned";

    private readonly IProvisioningRunRepository _repository;
    private readonly IDataverseEnvCreator _creator;
    private readonly IDataverseHealthProbe _healthProbe;
    private readonly DataverseEnvCreationOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<H5DataverseEnvCreationHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>
    /// Constructs the H5 Dataverse env-creation handler. All collaborators
    /// are interface-abstracted so unit tests can substitute stubs for each
    /// seam. <see cref="TimeProvider"/> is injected (per
    /// docs/standards/TEST-ARCHITECTURE.md — "TimeProvider over Stopwatch")
    /// so the polling loop is deterministically testable.
    /// </summary>
    public H5DataverseEnvCreationHandler(
        IProvisioningRunRepository repository,
        IDataverseEnvCreator creator,
        IDataverseHealthProbe healthProbe,
        IOptions<DataverseEnvCreationOptions> options,
        TimeProvider timeProvider,
        ILogger<H5DataverseEnvCreationHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(creator);
        ArgumentNullException.ThrowIfNull(healthProbe);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
        _creator = creator;
        _healthProbe = healthProbe;
        _options = options.Value;
        _timeProvider = timeProvider;
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
            // silently mis-executing (parity with H0/H1/H2a).
            throw new InvalidOperationException(
                $"H5DataverseEnvCreationHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        _logger.LogInformation(
            "H5 Dataverse env-creation starting: runId={RunId} customerId={CustomerId}",
            envelope.RunId, envelope.CustomerId);

        // (1) Load the ProvisioningRun. §4D I3: partition-key predicate
        // required by construction (repository shape enforces it).
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            _logger.LogWarning(
                "H5 aborted — ProvisioningRun not found: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: DataverseEnvCreationRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId);

        // (2) Level-3 idempotency: durable no-op on duplicate. POML
        // acceptance #4 requires that an existing env for the customer
        // returns Success WITHOUT re-creating. The prior URL, if any, is
        // read from InterStepState (H5's own write) and included in the
        // Success outcome so downstream is unaware of the re-run.
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H5 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey} existingEnvUrl={EnvUrl}",
                envelope.RunId, idempotencyKey, run.InterStepState.DataverseEnvUrl ?? "(unset)");
            return new HandlerResult.Success(idempotencyKey);
        }

        // (3) §4D I1 tenant guard — H5 MUST NOT fall back to a default tenant.
        //     The MissingTenantId failure fires BEFORE any pac invocation, so
        //     acceptance criterion #5 ("no pac admin call fired") is met by
        //     construction — the creator seam is never invoked.
        var parameters = run.Parameters.NonSecret;
        if (!TryGetNonEmpty(parameters, TenantIdParameterKey, out var tenantId))
        {
            var diagnostic =
                "Run parameter 'tenantId' is required by H5 (§4D I1 no-hardcoded-tenant). " +
                "Upstream handler (H0.5 for Model 2, L2 endpoint for Model 1) MUST populate this before H5 dispatches. " +
                "pac admin create-environment -TenantId requires an explicit tenant — H5 refuses to guess.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                DataverseEnvCreationRejectionCodes.MissingTenantId, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (4) Region guard — pac admin create-environment requires --region.
        if (!TryGetNonEmpty(parameters, RegionParameterKey, out var region))
        {
            var diagnostic =
                "Run parameter 'region' is required by H5 (pac admin create-environment --region). " +
                "Operator MUST supply the Dataverse region (e.g. 'unitedstates', 'europe') — H5 has no safe default.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                DataverseEnvCreationRejectionCodes.MissingRegion, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // (5) Tier guard — pac admin create-environment requires --type.
        //     Note per design.md D14 R15 rationale: SPs cannot create
        //     Developer-type envs. Callers filter that upstream; H5 accepts
        //     whatever tier the operator supplied and lets pac CLI reject
        //     invalid values (its diagnostic is more accurate than any
        //     enum we could hardcode here).
        if (!TryGetNonEmpty(parameters, TierParameterKey, out var tier))
        {
            var diagnostic =
                "Run parameter 'tier' is required by H5 (pac admin create-environment --type — Sandbox/Production/Trial). " +
                "Operator MUST supply the Dataverse env type — H5 has no safe default. Note: SPs cannot create Developer-type envs per design.md R15.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                DataverseEnvCreationRejectionCodes.MissingTier, diagnostic, cancellationToken).ConfigureAwait(false);
        }

        // Optional display name.
        var displayName = TryGetNonEmpty(parameters, DisplayNameParameterKey, out var dn)
            ? dn
            : null;

        var request = new DataverseEnvCreationRequest(
            CustomerId: envelope.CustomerId,
            TenantId: tenantId,
            Region: region,
            Tier: tier,
            DisplayName: displayName);

        // (6) Invoke the env-creation seam. This is the long-running (15–30 min)
        //     step per design.md § 4.2 / FR-22 R20; the caller (reconciler)
        //     owns keeping the ambient Service Bus lock alive. Infrastructure
        //     exceptions from the seam are classified Resumable (except
        //     OperationCanceledException which propagates for shutdown).
        DataverseEnvCreationOutcome outcome;
        try
        {
            outcome = await _creator.CreateEnvironmentAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H5 pac-admin creator infrastructure fault: runId={RunId} customerId={CustomerId}",
                envelope.RunId, envelope.CustomerId);
            var diagnostic =
                $"pac CLI infrastructure error: {ex.GetType().Name}: {ex.Message}. " +
                "Verify pac CLI is on PATH + operator has 'Power Platform Admin' role.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                DataverseEnvCreationRejectionCodes.PacInvocationFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        if (outcome is DataverseEnvCreationOutcome.Failure creatorFailure)
        {
            var (rejectionCode, failureClass) = MapCreatorFailure(creatorFailure.FailureKind);
            return await FailAsync(run, etag, failureClass, rejectionCode,
                creatorFailure.Diagnostic, cancellationToken).ConfigureAwait(false);
        }

        var successOutcome = (DataverseEnvCreationOutcome.Success)outcome;
        var environmentUrl = successOutcome.EnvironmentUrl;
        if (string.IsNullOrWhiteSpace(environmentUrl))
        {
            var diagnostic =
                "pac admin create-environment reported success but did not return an environment URL. " +
                "Env row may exist in tenant but its Web API endpoint could not be discovered — treat as partial provisioning.";
            return await FailAsync(run, etag, FailureClass.QuarantineRequired,
                DataverseEnvCreationRejectionCodes.PartialProvisioning, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (7) Post-create Web API health probe — POML acceptance criterion #3.
        //     Polls GET /WhoAmI on the returned URL until Reachable OR the
        //     health-probe total-timeout expires. InProgress signals from the
        //     probe extend the loop; Unreachable signals terminate with
        //     EnvHealthCheckFailed. Uses TimeProvider for deterministic
        //     test behavior (per docs/standards/TEST-ARCHITECTURE.md).
        var healthOutcome = await PollHealthAsync(
            environmentUrl, tenantId, envelope, cancellationToken).ConfigureAwait(false);
        if (healthOutcome is DataverseHealthProbeResult.Unreachable unreachable)
        {
            var diagnostic =
                $"Dataverse Web API health check failed for '{environmentUrl}': {unreachable.Diagnostic}. " +
                "Env row exists (pac reported success) but WhoAmI is not returning 200 within the health-probe window. " +
                "Operator inspects env state in Power Platform Admin Center + POSTs /api/runs/{id}/resume once accessible.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                DataverseEnvCreationRejectionCodes.EnvHealthCheckFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        if (healthOutcome is DataverseHealthProbeResult.InProgress inProgress)
        {
            // Polling exited on total-timeout while last probe was still
            // InProgress — treat as a health-check timeout (distinct from
            // creation timeout). Same rejection code as Unreachable so
            // operator dashboards branch on health-check vs create-timeout.
            var diagnostic =
                $"Dataverse Web API health check exceeded {_options.HealthProbeTotalTimeout} for '{environmentUrl}'. " +
                $"Last probe response: {inProgress.Diagnostic}. " +
                "Env may still be replicating — operator inspects PPAC and resumes once /WhoAmI returns 200.";
            return await FailAsync(run, etag, FailureClass.Resumable,
                DataverseEnvCreationRejectionCodes.EnvHealthCheckFailed, diagnostic, cancellationToken)
                .ConfigureAwait(false);
        }

        // (8) All post-conditions cleared — write dataverseEnvUrl to
        //     InterStepState + advance Cosmos state. Reconciler (Wave C5)
        //     fans out to H6 on the CurrentPhase == "H5" observation.
        stopwatch.Stop();
        _logger.LogInformation(
            "H5 Dataverse env-creation succeeded: runId={RunId} customerId={CustomerId} " +
            "envUrl={EnvUrl} region={Region} tier={Tier} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, environmentUrl, region, tier,
            stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(
            run, etag, idempotencyKey, environmentUrl, region, tier, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Computes the deterministic H5 idempotency key: <c>dvenv-{customerId}</c>.
    /// No version token per POML constraint + design.md § 4.1 H5 row —
    /// Dataverse env creation is per-customer + version-independent (unlike
    /// H2a's <c>infra-{customerId}-{bicepVer}</c> or H6's
    /// <c>solimport-{customerId}-{solutionVer}</c>). Exposed as internal so
    /// unit tests can construct expected keys without duplicating the format.
    /// </summary>
    internal static string BuildIdempotencyKey(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return $"dvenv-{customerId}";
    }

    /// <summary>
    /// Maps <see cref="DataverseEnvCreationFailureKind"/> to the pair
    /// (rejectionCode, §4C failureClass). Exposed as internal so tests can
    /// assert the mapping without depending on handler internals.
    /// </summary>
    internal static (string RejectionCode, FailureClass Class) MapCreatorFailure(
        DataverseEnvCreationFailureKind kind) => kind switch
        {
            DataverseEnvCreationFailureKind.AuthFailure =>
                (DataverseEnvCreationRejectionCodes.PacAuthFailure, FailureClass.Resumable),
            DataverseEnvCreationFailureKind.RateLimited =>
                (DataverseEnvCreationRejectionCodes.RateLimited, FailureClass.Resumable),
            DataverseEnvCreationFailureKind.QuotaExhausted =>
                (DataverseEnvCreationRejectionCodes.QuotaExhausted, FailureClass.Resumable),
            DataverseEnvCreationFailureKind.PartialProvisioning =>
                (DataverseEnvCreationRejectionCodes.PartialProvisioning, FailureClass.QuarantineRequired),
            DataverseEnvCreationFailureKind.Timeout =>
                (DataverseEnvCreationRejectionCodes.EnvCreationTimeout, FailureClass.Resumable),
            DataverseEnvCreationFailureKind.UnknownInvocationFailure =>
                (DataverseEnvCreationRejectionCodes.PacInvocationFailed, FailureClass.Resumable),
            DataverseEnvCreationFailureKind.DomainAlreadyExists =>
                (DataverseEnvCreationRejectionCodes.DomainAlreadyExists, FailureClass.Resumable),
            DataverseEnvCreationFailureKind.ProvisioningFailed =>
                (DataverseEnvCreationRejectionCodes.EnvProvisioningFailed, FailureClass.Resumable),
            _ =>
                (DataverseEnvCreationRejectionCodes.PacInvocationFailed, FailureClass.Resumable),
        };

    /// <summary>
    /// Polls <see cref="IDataverseHealthProbe.CheckHealthAsync"/> until
    /// Reachable OR the total-timeout expires. Returns the LAST probe result
    /// (Reachable / InProgress / Unreachable). Uses <see cref="TimeProvider"/>
    /// for deterministic testability — tests inject <see cref="FakeTimeProvider"/>
    /// (or the concrete TimeProvider.System in production).
    /// </summary>
    private async Task<DataverseHealthProbeResult> PollHealthAsync(
        string environmentUrl,
        string tenantId,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var deadline = _timeProvider.GetUtcNow() + _options.HealthProbeTotalTimeout;
        DataverseHealthProbeResult lastResult = new DataverseHealthProbeResult.InProgress("polling-not-yet-started");
        var attempt = 0;

        while (_timeProvider.GetUtcNow() < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            attempt++;
            try
            {
                lastResult = await _healthProbe.CheckHealthAsync(
                    environmentUrl, tenantId, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Infrastructure fault mid-probe — treat as Unreachable so
                // the handler surfaces EnvHealthCheckFailed. Log the
                // exception separately since Unreachable doesn't carry stack.
                _logger.LogWarning(ex,
                    "H5 health probe threw on attempt {Attempt}: runId={RunId} customerId={CustomerId}",
                    attempt, envelope.RunId, envelope.CustomerId);
                lastResult = new DataverseHealthProbeResult.Unreachable(
                    $"Health probe infrastructure error: {ex.GetType().Name}: {ex.Message}");
                break;
            }

            if (lastResult is DataverseHealthProbeResult.Reachable
                || lastResult is DataverseHealthProbeResult.Unreachable)
            {
                break;
            }

            // InProgress — wait then retry.
            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }
            var delay = _options.HealthProbeInterval < remaining
                ? _options.HealthProbeInterval
                : remaining;
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return lastResult;
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
                QuarantinedAt = _timeProvider.GetUtcNow(),
            };
        }

        // Gate entry — one per failure. Verifier is H5; state is Pending
        // (external precondition to resolve). Parity with H2a's FailAsync.
        run.GateStates[$"h5-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H5 failure state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H5 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(failureClass, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        string environmentUrl,
        string region,
        string tier,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = _timeProvider.GetUtcNow();
        var startedAt = completedAt - TimeSpan.FromMilliseconds(1);

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out to H6.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // Populate interStepState.DataverseEnvUrl (design.md §6.2) — the
        // single H5-owned slot. H6/H7/H10 consume from here.
        run.InterStepState.DataverseEnvUrl = environmentUrl;

        // Verified-gate entry — one for the env-provisioned post-condition.
        // Evidence captures env URL + region + tier so an operator sees the
        // parameters without pulling logs.
        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            environmentUrl,
            region,
            tier,
        })).RootElement.Clone();
        run.GateStates[EnvProvisionedGateId] = new GateEntry
        {
            Status = GateState.Verified,
            VerifierHandler = HandlerIdentifier,
            VerifiedAt = completedAt,
            Evidence = evidence,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H5 success state write LOST optimistic-concurrency race: " +
                "runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: DataverseEnvCreationRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H5 read + write. " +
                             $"Winning status: {conflict.Current.Run.Status}. Resume will re-run H5 " +
                             "which will short-circuit on idempotency.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H5 success state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: DataverseEnvCreationRejectionCodes.RunDeletedDuringCreation,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H5 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
