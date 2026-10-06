// -----------------------------------------------------------------------------
// H5DataverseEnvAdoptionHandler.cs
//
// L2 CONTROL-PLANE H5 — adopt the customer's Dataverse environment (task 048; rewritten by T228).
//
// PURPOSE (T228, owner D4 / Q1 2026-09-30): the OPERATOR creates the customer's Dataverse environment (prereqs.yaml
//   PRQ-C-09) and gives its URL at intake (`dataverseEnvUrl`); H5 adopts it and never creates one. Until T228 H5 created
//   the environment through the BAP admin API (BapRestEnvironmentCreator, removed with its interface, the retired pac
//   creator and H0's environment-creation-rate probe). The folder and namespace keep their historical name.
//
// WHAT H5 PROVES before any later handler acts on the environment:
//   1. The URL is THIS customer's (DataverseEnvironmentUrlRule — the same rule POST /api/runs applies; re-checked here
//      for a run document that predates it). Every Model 1 environment lives in Spaarke's tenant, so a mistyped URL
//      could otherwise name another customer's environment, into which H6 would import, H7 write and H10 add users.
//   2. The L2 Worker identity can use it: GET /WhoAmI as DefaultAzureCredential(TenantId) — the identity H8 (root
//      business unit, recorded container) and H10 (application users) act as. 401/403 → Resumable
//      `worker-not-app-user`: the operator adds the Worker identity as a System Administrator application user
//      (PRQ-C-09) and resumes. A creator was System Administrator implicitly; an adopter must be made one.
//   Then it hands the canonical URL on through InterStepState.DataverseEnvUrl (H4b, H6, H7, H8, H10, H12a-c, H13, H14).
//
// IDEMPOTENCY: `dvenv-{customerId}` — a completed H5 is a durable no-op. H5 writes nothing outside the run document.
//
// FAILURE CLASSES: all Resumable — nothing is created, so nothing can be left half-made.
// -----------------------------------------------------------------------------

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Sprk.Provisioning.ControlPlane.Core.Models;
using Sprk.Provisioning.ControlPlane.Enqueue;
using Sprk.Provisioning.ControlPlane.Models;
using Sprk.Provisioning.ControlPlane.Repositories;

namespace Sprk.Provisioning.ControlPlane.Handlers.DataverseEnvCreation;

/// <inheritdoc cref="IProvisioningHandler"/>
public sealed class H5DataverseEnvAdoptionHandler : IProvisioningHandler
{
    /// <summary>Handler identifier — matches design.md § 4.1 catalog verbatim.</summary>
    public const string HandlerIdentifier = HandlerIds.H5;

    /// <summary>Non-secret parameter key carrying the Entra tenant id (§4D I1).</summary>
    public const string TenantIdParameterKey = IntakeParameterCatalog.TenantId;

    /// <summary>Verified gate identifier for the H5 post-condition (the environment is adopted and usable).</summary>
    public const string EnvProvisionedGateId = "h5-dataverse-env-provisioned";

    private readonly IProvisioningRunRepository _repository;
    private readonly IDataverseHealthProbe _healthProbe;
    private readonly DataverseEnvAdoptionOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<H5DataverseEnvAdoptionHandler> _logger;

    /// <inheritdoc/>
    public string HandlerId => HandlerIdentifier;

    /// <summary>Constructs H5. <see cref="TimeProvider"/> drives the WhoAmI polling loop (TEST-ARCHITECTURE.md).</summary>
    public H5DataverseEnvAdoptionHandler(
        IProvisioningRunRepository repository,
        IDataverseHealthProbe healthProbe,
        IOptions<DataverseEnvAdoptionOptions> options,
        TimeProvider timeProvider,
        ILogger<H5DataverseEnvAdoptionHandler> logger)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(healthProbe);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _repository = repository;
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
            throw new InvalidOperationException(
                $"H5DataverseEnvAdoptionHandler invoked with mismatched HandlerId '{envelope.HandlerId}' " +
                $"(expected '{HandlerIdentifier}').");
        }

        var stopwatch = Stopwatch.StartNew();
        var read = await _repository.ReadRunAsync(
            envelope.CustomerId, envelope.RunId, cancellationToken).ConfigureAwait(false);
        if (read is null)
        {
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: DataverseEnvAdoptionRejectionCodes.RunNotFound,
                Diagnostic: $"ProvisioningRun '{envelope.RunId}' not found in customer partition '{envelope.CustomerId}'.");
        }

        var run = read.Run;
        var etag = read.ETag;
        var idempotencyKey = BuildIdempotencyKey(envelope.CustomerId);
        if (run.CompletedPhases.Any(cp =>
                string.Equals(cp.Phase, HandlerIdentifier, StringComparison.Ordinal)
                && string.Equals(cp.IdempotencyKey, idempotencyKey, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "H5 idempotent no-op: runId={RunId} idempotencyKey={IdempotencyKey} envUrl={EnvUrl}",
                envelope.RunId, idempotencyKey, run.InterStepState.DataverseEnvUrl ?? "(unset)");
            return new HandlerResult.Success(idempotencyKey);
        }

        // (1) §4D I1: an explicit tenant, never a default.
        var parameters = run.Parameters.NonSecret;
        if (!parameters.TryGetValue(TenantIdParameterKey, out var tenantId) || string.IsNullOrWhiteSpace(tenantId))
        {
            return await FailAsync(run, etag, DataverseEnvAdoptionRejectionCodes.MissingTenantId,
                "Run parameter 'tenantId' is required by H5 (§4D I1) — POST /api/runs requires it, so this run predates that rule.",
                cancellationToken).ConfigureAwait(false);
        }

        // (2) The environment is this customer's — the rule POST /api/runs applies.
        parameters.TryGetValue(IntakeParameterCatalog.DataverseEnvUrl, out var suppliedUrl);
        if (!DataverseEnvironmentUrlRule.TryNormalize(
                suppliedUrl,
                envelope.CustomerId,
                IntakeParameterCatalog.ResolveEnvironmentName(parameters),
                out var environmentUrl,
                out var urlError))
        {
            return await FailAsync(run, etag, DataverseEnvAdoptionRejectionCodes.EnvUrlInvalid,
                $"Run parameter '{IntakeParameterCatalog.DataverseEnvUrl}' {urlError} H5 adopts only the environment the " +
                "operator created for this customer; nothing was written.",
                cancellationToken).ConfigureAwait(false);
        }

        // (3) The Worker identity can use it.
        var health = await PollHealthAsync(environmentUrl, tenantId, envelope, cancellationToken).ConfigureAwait(false);
        switch (health)
        {
            case DataverseHealthProbeResult.AccessDenied denied:
                return await FailAsync(run, etag, DataverseEnvAdoptionRejectionCodes.WorkerNotAppUser,
                    $"The L2 Worker identity cannot use '{environmentUrl}': {denied.Diagnostic}. Add the Worker's managed " +
                    "identity to the environment as an application user with the System Administrator role " +
                    "(prereqs.yaml PRQ-C-09), then resume. H5 never creates or changes an environment.",
                    cancellationToken).ConfigureAwait(false);

            case DataverseHealthProbeResult.Unreachable unreachable:
                return await FailAsync(run, etag, DataverseEnvAdoptionRejectionCodes.EnvHealthCheckFailed,
                    $"Dataverse Web API check failed for '{environmentUrl}': {unreachable.Diagnostic}. Confirm the environment " +
                    "exists, has a Dataverse database and is enabled in the Power Platform admin center, then resume.",
                    cancellationToken).ConfigureAwait(false);

            case DataverseHealthProbeResult.InProgress inProgress:
                return await FailAsync(run, etag, DataverseEnvAdoptionRejectionCodes.EnvHealthCheckFailed,
                    $"Dataverse Web API did not answer within {_options.HealthProbeTotalTimeout} for '{environmentUrl}' " +
                    $"(last: {inProgress.Diagnostic}). An environment still being prepared answers once it is ready — resume then.",
                    cancellationToken).ConfigureAwait(false);
        }

        stopwatch.Stop();
        _logger.LogInformation(
            "H5 adopted the Dataverse environment: runId={RunId} customerId={CustomerId} envUrl={EnvUrl} durationMs={DurationMs}",
            envelope.RunId, envelope.CustomerId, environmentUrl, stopwatch.ElapsedMilliseconds);

        return await MarkCompleteAsync(run, etag, idempotencyKey, environmentUrl, envelope, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>The deterministic H5 idempotency key: <c>dvenv-{customerId}</c>.</summary>
    internal static string BuildIdempotencyKey(string customerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(customerId);
        return $"dvenv-{customerId}";
    }

    /// <summary>
    /// Polls <see cref="IDataverseHealthProbe.CheckHealthAsync"/> until a terminal answer (Reachable, AccessDenied,
    /// Unreachable) or the total timeout. Returns the last result.
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
                _logger.LogWarning(ex,
                    "H5 WhoAmI probe threw on attempt {Attempt}: runId={RunId} customerId={CustomerId}",
                    attempt, envelope.RunId, envelope.CustomerId);
                return new DataverseHealthProbeResult.Unreachable(
                    $"Health probe infrastructure error: {ex.GetType().Name}: {ex.Message}");
            }

            if (lastResult is not DataverseHealthProbeResult.InProgress)
            {
                return lastResult;
            }

            var remaining = deadline - _timeProvider.GetUtcNow();
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }
            var delay = _options.HealthProbeInterval < remaining ? _options.HealthProbeInterval : remaining;
            await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
        }

        return lastResult;
    }

    private async Task<HandlerResult> FailAsync(
        ProvisioningRun run,
        string etag,
        string rejectionCode,
        string diagnostic,
        CancellationToken cancellationToken)
    {
        run.Status = RunStatus.Failed;
        run.CurrentPhase = HandlerIdentifier;
        run.ErrorDetail = $"[{rejectionCode}] {diagnostic}";
        run.GateStates[$"h5-{rejectionCode}"] = new GateEntry
        {
            Status = GateState.Pending,
            VerifierHandler = HandlerIdentifier,
        };

        var replace = await _repository.ReplaceRunAsync(run, etag, cancellationToken).ConfigureAwait(false);
        if (replace is ReplaceRunResult.Conflict conflict)
        {
            _logger.LogWarning(
                "H5 failure state write LOST optimistic-concurrency race: runId={RunId} customerId={CustomerId} winningStatus={WinningStatus}",
                run.RunId, run.CustomerId, conflict.Current.Run.Status);
        }
        else if (replace is ReplaceRunResult.NotFound)
        {
            _logger.LogWarning(
                "H5 failure state write raced with row delete: runId={RunId} customerId={CustomerId}",
                run.RunId, run.CustomerId);
        }

        return new HandlerResult.Failure(FailureClass.Resumable, rejectionCode, diagnostic);
    }

    private async Task<HandlerResult> MarkCompleteAsync(
        ProvisioningRun run,
        string etag,
        string idempotencyKey,
        string environmentUrl,
        HandlerEnvelope envelope,
        CancellationToken cancellationToken)
    {
        var completedAt = _timeProvider.GetUtcNow();

        run.Status = RunStatus.Running;
        run.CurrentPhase = HandlerIdentifier; // Reconciler observes + fans out.
        run.CompletedPhases.Add(new CompletedPhase
        {
            Phase = HandlerIdentifier,
            StartedAt = completedAt - TimeSpan.FromMilliseconds(1),
            CompletedAt = completedAt,
            IdempotencyKey = idempotencyKey,
            JobId = envelope.RunId,
        });
        run.ErrorDetail = null;

        // The single H5-owned slot (design.md §6.2), in canonical form.
        run.InterStepState.DataverseEnvUrl = environmentUrl;

        var evidence = JsonDocument.Parse(JsonSerializer.Serialize(new { environmentUrl, adopted = true }))
            .RootElement.Clone();
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
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: DataverseEnvAdoptionRejectionCodes.ConcurrentWriteConflict,
                Diagnostic: $"Concurrent write advanced run '{run.RunId}' between H5 read + write. Winning status: " +
                            $"{conflict.Current.Run.Status}. Resume re-runs H5, which short-circuits on idempotency.");
        }
        if (replace is ReplaceRunResult.NotFound)
        {
            return new HandlerResult.Failure(
                Class: FailureClass.Resumable,
                RejectionCode: DataverseEnvAdoptionRejectionCodes.RunDeletedDuringAdoption,
                Diagnostic: $"ProvisioningRun '{run.RunId}' was deleted while H5 was in flight.");
        }

        return new HandlerResult.Success(idempotencyKey);
    }
}
