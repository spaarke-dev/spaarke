// -----------------------------------------------------------------------------
// GraphAppOnlyContainerVerifier.cs
//
// Task 131 (Wave G-3) — production ISpeContainerVerifier. Ports the retired
// SpeContainerAppOnlyVerifier.cs (shelled out to scripts/Get-
// SpeContainerMetadata-AppOnly.ps1 — "123 lines of token ceremony around ONE
// GET" per DS-1b §1) to Microsoft.Graph 6.5.0 under ClientCertificateCredential
// (T6). This is the "dramatically simplified" collaborator the POML calls
// out: the PS script's entire purpose was manually building + signing an
// RS256 JWT client assertion around a single Graph GET; Azure.Identity's
// ClientCertificateCredential (shared with GraphContainerTypeProvisioner via
// SpeConfidentialClientGraphFactory — see that file's header for the KV
// cert-bootstrap + credential-construction recipe) does that ceremony
// internally. The production body of this file is a single Graph GET call —
// the surrounding structure (timeouts, T6-trap classification) is retained
// for parity with the handler's post-condition contract.
//
// 24h REPLICATION-LAG HANDLING (design.md §4.1 H8 row + DS-4 §2 + this
// project's CLAUDE.md MUST rules): a 404 Not Found on the app-only GET for a
// container H8 JUST created is the documented signature of SPE's up-to-24h
// container-type replication window — NOT a T6 trap, NOT a genuine failure.
// Mapped to SpeContainerVerificationResult.ReplicationPending, which
// H8SpeContainerTypeHandler classifies as RunStatus.WaitingOnGate (a
// session-free run-level pause) rather than Resumable/QuarantineRequired.
// Any OTHER Graph error (403, 400, 5xx) is a genuine NotVerified failure —
// 404-specifically is the only status this verifier treats as "not yet, not
// broken."
//
// NOT UNIT-TESTED IN THE CI SUITE (real Microsoft.Graph HTTP calls) — see
// GraphContainerTypeProvisioner.cs's file header for the full rationale
// (identical posture; H8SpeContainerTypeHandlerTests.cs substitutes a fake
// ISpeContainerVerifier as the real coverage surface for H8's orchestration +
// the WaitingOnGate classification logic itself).
// -----------------------------------------------------------------------------

using Azure.Core;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

/// <inheritdoc cref="ISpeContainerVerifier"/>
public sealed class GraphAppOnlyContainerVerifier : ISpeContainerVerifier
{
    private readonly TokenCredential _sharedCredential;
    private readonly SecretClientOptions? _clientOptions;
    private readonly SpeContainerTypeOptions _options;
    private readonly ILogger<GraphAppOnlyContainerVerifier> _logger;

    /// <summary>Constructs the production verifier. <paramref name="sharedCredential"/> is used ONLY for the T6 cert read from KV (see SpeConfidentialClientGraphFactory.cs); the Graph GET itself uses a per-request T6 ClientCertificateCredential.</summary>
    public GraphAppOnlyContainerVerifier(
        TokenCredential sharedCredential,
        IOptions<SpeContainerTypeOptions> options,
        ILogger<GraphAppOnlyContainerVerifier> logger)
        : this(sharedCredential, clientOptions: null, options, logger)
    {
    }

    /// <summary>Test seam constructor — injects a fake-transport <see cref="SecretClientOptions"/> for the KV cert read.</summary>
    internal GraphAppOnlyContainerVerifier(
        TokenCredential sharedCredential,
        SecretClientOptions? clientOptions,
        IOptions<SpeContainerTypeOptions> options,
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

        // Cert load — no external SPE side effect on failure; propagates as
        // an infra fault (handler maps to VerificationInfraFault, QuarantineRequired
        // per the existing post-creation classification — the container
        // already exists at this point).
        using var cert = await SpeConfidentialClientGraphFactory.LoadCertificateAsync(
            _sharedCredential, _clientOptions, request.VaultName, request.CertSecretName,
            _options.CertLoadTimeout, cancellationToken).ConfigureAwait(false);

        var graph = SpeConfidentialClientGraphFactory.BuildGraphClient(request.TenantId, request.OwningAppId, cert);

        _logger.LogInformation(
            "H8 Graph SPE container app-only verification starting: containerId={ContainerId}",
            request.ContainerId);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.GraphRequestTimeout);

            var container = await graph.Storage.FileStorage.Containers[request.ContainerId]
                .GetAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            var status = container?.Status?.ToString() ?? "unknown";

            _logger.LogInformation(
                "T6 cleared: container ID {ContainerId} verified via app-only (confidential-client cert-based) " +
                "token. Status: {Status}", request.ContainerId, status);

            return new SpeContainerVerificationResult.Verified(status);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            // Documented 24h SPE replication-lag signature — see file header.
            var diagnostic =
                $"App-only GET for container '{request.ContainerId}' returned 404 Not Found — consistent with " +
                "SPE's up-to-24h container-type replication window (design.md §4.1 H8 row). Not a failure; " +
                "not a T6 trap. Handler classifies this as RunStatus.WaitingOnGate.";
            _logger.LogInformation(
                "H8 SPE container verification pending replication: containerId={ContainerId}", request.ContainerId);
            return new SpeContainerVerificationResult.ReplicationPending(diagnostic);
        }
        catch (ODataError ex)
        {
            var isTrap = SpeConfidentialClientGraphFactory.IsDelegatedTokenTrapError(ex);
            _logger.LogError(ex,
                "H8 Graph SPE container app-only verification ODataError: containerId={ContainerId} " +
                "status={Status} isDelegatedTokenTrap={IsDelegatedTokenTrap}",
                request.ContainerId, ex.ResponseStatusCode, isTrap);
            var diagnostic = isTrap
                ? $"T6 silent-fail trap detected during verification: Graph ODataError {ex.ResponseStatusCode} " +
                  $"contains '{SpeConfidentialClientGraphFactory.DelegatedTokenTrapPhrase}' for containerId " +
                  $"'{request.ContainerId}'. {ex.Error?.Code} {ex.Error?.Message}"
                : $"Graph ODataError {ex.ResponseStatusCode} verifying containerId '{request.ContainerId}': " +
                  $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message}";
            return new SpeContainerVerificationResult.NotVerified(diagnostic, IsDelegatedTokenTrap: isTrap);
        }
    }
}
