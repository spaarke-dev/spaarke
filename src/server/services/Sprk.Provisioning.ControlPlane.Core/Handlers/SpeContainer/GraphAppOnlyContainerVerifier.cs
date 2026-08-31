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
// NOT UNIT-TESTED IN THE CI SUITE (real Microsoft.Graph HTTP calls) — parity
// with GraphContainerProvisioner.cs. H8SpeContainerHandlerTests.cs substitutes
// a fake ISpeContainerVerifier as the real coverage surface for H8's
// orchestration + the WaitingOnGate classification logic itself.
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <inheritdoc cref="ISpeContainerVerifier"/>
public sealed class GraphAppOnlyContainerVerifier : ISpeContainerVerifier
{
    private readonly TokenCredential _sharedCredential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly SpeContainerOptions _options;
    private readonly ILogger<GraphAppOnlyContainerVerifier> _logger;

    /// <summary>Constructs the production verifier. <paramref name="sharedCredential"/> is used ONLY for the T6 cert read from KV; the Graph GET itself uses a per-request T6 ClientCertificateCredential.</summary>
    public GraphAppOnlyContainerVerifier(
        TokenCredential sharedCredential,
        IOptions<SpeContainerOptions> options,
        ILogger<GraphAppOnlyContainerVerifier> logger)
        : this(sharedCredential, clientOptions: null, options, logger)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/> for the KV cert read.</summary>
    internal GraphAppOnlyContainerVerifier(
        TokenCredential sharedCredential,
        SecretClientOptions? clientOptions,
        IOptions<SpeContainerOptions> options,
        ILogger<GraphAppOnlyContainerVerifier> logger)
    {
        ArgumentNullException.ThrowIfNull(sharedCredential);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _sharedCredential = sharedCredential;
        _clientOptions = clientOptions;
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
        ArgumentException.ThrowIfNullOrWhiteSpace(request.VaultName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CertSecretName);

        using var cert = await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            _sharedCredential, _clientOptions, request.VaultName, request.CertSecretName,
            _options.CertLoadTimeout, cancellationToken).ConfigureAwait(false);

        var graph = SpeConfidentialClientGraphFactory.BuildGraphClient(request.TenantId, request.OwningAppId, cert);

        _logger.LogInformation(
            "H8-B Graph SPE container app-only verification starting: containerId={ContainerId}",
            request.ContainerId);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.GraphRequestTimeout);

            var container = await graph.Storage.FileStorage.Containers[request.ContainerId]
                .GetAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            var status = container?.Status?.ToString() ?? "unknown";

            _logger.LogInformation(
                "H8-B container verified via app-only (confidential-client cert-based) token. " +
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
