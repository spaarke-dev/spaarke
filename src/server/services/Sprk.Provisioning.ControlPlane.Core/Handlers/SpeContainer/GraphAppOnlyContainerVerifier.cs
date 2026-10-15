// -----------------------------------------------------------------------------
// GraphAppOnlyContainerVerifier.cs
//
// Task 214 (H8-B rewrite, 2026-08-30) — production ISpeContainerVerifier.
// SUPERSEDES the previous version at Handlers/SpeContainerType/GraphAppOnlyContainerVerifier.cs
// (deleted). Logic is unchanged from the pre-rewrite implementation — the
// verifier's job is the same (GET a container via app-only, classify 404 as
// ReplicationPending, anything else as NotVerified/Verified). Namespace changed
// to Sprk.Provisioning.ControlPlane.Handlers.SpeContainer and the
// SpeContainerOptions type reference reflects the renamed options class.
//
// Also removed the T6-trap classification branch: H8-B does not participate in
// T6-trap detection (task 214.4 Option A — H13's T6SpeConfidentialClientTrapProbe
// owns the T6 acceptance gate). A delegated-token error surfaces as an ordinary
// NotVerified with the raw error text in the diagnostic; the handler classifies
// QuarantineRequired regardless.
//
// 24h REPLICATION-LAG HANDLING (design.md §4.1 H8 row + DS-4 §2 + this
// project's CLAUDE.md MUST rules): a 404 Not Found on the app-only GET for a
// container H8 JUST created + activated is the documented signature of SPE's
// up-to-24h container-type replication window — NOT a genuine failure. Mapped
// to SpeContainerVerificationResult.ReplicationPending, which
// H8SpeContainerHandler classifies as RunStatus.WaitingOnGate (a session-free
// run-level pause) rather than Resumable/QuarantineRequired.
//
// TRANSIENT FAULTS (T186 audit of H8): a 429, a 5xx, a per-attempt timeout or a
// dropped connection is retried (SpeContainerOptions.VerifyTransientRetryCount /
// ...Delay); if it persists the result is TransientFailure (handler: Resumable),
// never NotVerified/QuarantineRequired. Tests: GraphAppOnlyContainerVerifierTests.
// H8SpeContainerHandlerTests.cs substitutes a fake ISpeContainerVerifier as the
// coverage surface for H8's orchestration + the WaitingOnGate classification itself.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <inheritdoc cref="ISpeContainerVerifier"/>
public sealed class GraphAppOnlyContainerVerifier : ISpeContainerVerifier
{
    private readonly SpeConfidentialClientGraphFactory _graphFactory;
    private readonly SpeContainerOptions _options;
    private readonly ILogger<GraphAppOnlyContainerVerifier> _logger;

    /// <summary>Constructs the production verifier over the owning-app Graph client factory (task 248).</summary>
    public GraphAppOnlyContainerVerifier(
        SpeConfidentialClientGraphFactory graphFactory,
        IOptions<SpeContainerOptions> options,
        ILogger<GraphAppOnlyContainerVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _graphFactory = graphFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SpeContainerVerificationResult> VerifyAsync(
        SpeContainerVerificationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);

        _logger.LogInformation(
            "H8-B Graph SPE container app-only verification starting: containerId={ContainerId}",
            request.ContainerId);

        try
        {
            // T186 audit: a 429, 5xx, per-attempt timeout or dropped connection says nothing about the container —
            // retry it (bounded). 404 (replication gate) and every other answer are not retried here.
            var container = await SpeGraphRetry.RetryWhileAsync(
                async attemptCt =>
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(attemptCt);
                    timeoutCts.CancelAfter(_options.GraphRequestTimeout);
                    return await graph.Storage.FileStorage.Containers[request.ContainerId]
                        .GetAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);
                },
                ex => SpeGraphRetry.IsTransientServiceFault(ex, cancellationToken),
                _options.VerifyTransientRetryCount, _options.VerifyTransientRetryDelay,
                _logger, "container verification GET", cancellationToken).ConfigureAwait(false);

            var status = container?.Status?.ToString() ?? "unknown";

            _logger.LogInformation(
                "H8-B container verified via the owning app's app-only (MI-FIC) token. " +
                "containerId={ContainerId} status={Status}", request.ContainerId, status);

            return new SpeContainerVerificationResult.Verified(status);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            var diagnostic =
                $"App-only GET for container '{request.ContainerId}' returned 404 Not Found — consistent with " +
                "SPE's up-to-24h container-type replication window (design.md §4.1 H8 row). Not a failure. " +
                "Handler classifies this as RunStatus.WaitingOnGate.";
            _logger.LogInformation(
                "H8-B SPE container verification pending replication: containerId={ContainerId}", request.ContainerId);
            return new SpeContainerVerificationResult.ReplicationPending(diagnostic);
        }
        catch (Exception ex) when (SpeGraphRetry.IsTransientServiceFault(ex, cancellationToken))
        {
            // Still failing after the bounded retries — a transient service fault, not a verdict on the container.
            // Resumable at the handler (never QuarantineRequired); the container stays on the run's record.
            _logger.LogWarning(ex,
                "H8-B Graph SPE container app-only verification kept failing transiently: containerId={ContainerId}",
                request.ContainerId);
            return new SpeContainerVerificationResult.TransientFailure(
                $"App-only GET for container '{request.ContainerId}' kept failing transiently after " +
                $"{_options.VerifyTransientRetryCount} attempt(s): {SpeGraphRetry.Describe(ex)}. This is not a verdict on the container.");
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "H8-B Graph SPE container app-only verification ODataError: containerId={ContainerId} " +
                "status={Status}",
                request.ContainerId, ex.ResponseStatusCode);
            var diagnostic =
                $"Graph ODataError {ex.ResponseStatusCode} verifying containerId '{request.ContainerId}': " +
                $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message}";
            return new SpeContainerVerificationResult.NotVerified(diagnostic);
        }
    }
}
