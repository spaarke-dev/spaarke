// -----------------------------------------------------------------------------
// GraphContainerTypesListAppOnlyProbe.cs
//
// Task 175 (Wave G-7, pipelined with H8 / task 131) -- production
// IT6GraphAppOnlyProbe. Issues:
//   GET /storage/fileStorage/containerTypes
// under a per-request ClientCertificateCredential built via task 131's
// SpeConfidentialClientGraphFactory.BuildGraphClient (T6-compliant
// confidential-client cert-based auth model).
//
// SDK SURFACE (Microsoft.Graph 6.5.0): graph.Storage.FileStorage.ContainerTypes
// .GetAsync() -- the v1.0 GA endpoint identical to what task 131's
// GraphContainerTypeProvisioner POSTs to. See that file's GOTCHA 1 section.
//
// TIMEOUT + RESPONSE HANDLING mirrors GraphAppOnlyContainerVerifier.cs
// (task 131) -- same LinkedTimeout pattern, same ODataError catches, same
// delegated-token-trap-phrase detection. Path is the LIST endpoint (empty
// list still returns 200 for a tenant that has never provisioned an SPE
// container-type) so the probe answers "does the credential authenticate?"
// not "does a specific container exist?".
//
// NOT UNIT-TESTED IN THE CI SUITE (real Microsoft.Graph HTTP calls) -- parity
// with the established project precedent. T6SpeConfidentialClientTrapProbe
// tests substitute a fake IT6GraphAppOnlyProbe. Phase F acceptance (task 186)
// covers the live Graph path.
// -----------------------------------------------------------------------------

using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.Options;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainerType;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IT6GraphAppOnlyProbe"/>
public sealed class GraphContainerTypesListAppOnlyProbe : IT6GraphAppOnlyProbe
{
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<GraphContainerTypesListAppOnlyProbe> _logger;

    public GraphContainerTypesListAppOnlyProbe(
        IOptions<H13AcceptanceOptions> options,
        ILogger<GraphContainerTypesListAppOnlyProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<T6GraphAppOnlyProbeResult> ProbeAsync(
        string tenantId, string clientAppId, X509Certificate2 cert,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(clientAppId);
        ArgumentNullException.ThrowIfNull(cert);

        var graph = SpeConfidentialClientGraphFactory.BuildGraphClient(tenantId, clientAppId, cert);

        _logger.LogInformation(
            "T6 Graph app-only probe starting: tenantId={TenantId} clientAppId={ClientAppId} " +
            "certThumbprint={CertThumbprint}", tenantId, clientAppId, cert.Thumbprint);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.TrapVerifierTimeout);

            var response = await graph.Storage.FileStorage.ContainerTypes
                .GetAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);

            var count = response?.Value?.Count ?? 0;
            _logger.LogInformation(
                "T6 Graph app-only probe cleared: containerTypes GET returned {Count} entry(ies) " +
                "under confidential-client cert-based auth.", count);

            return T6GraphAppOnlyProbeResults.Succeeded;
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            var diagnostic = $"Graph containerTypes GET returned 404 Not Found " +
                             $"({ex.Error?.Code ?? "(no code)"} {ex.Error?.Message ?? ex.Message}).";
            _logger.LogInformation(
                "T6 Graph app-only probe: 404 -- SPE replication window signature. tenantId={TenantId}",
                tenantId);
            return new T6GraphAppOnlyProbeResult.ReplicationPendingResult(diagnostic);
        }
        catch (ODataError ex)
        {
            var isTrap = SpeConfidentialClientGraphFactory.IsDelegatedTokenTrapError(ex);
            _logger.LogWarning(ex,
                "T6 Graph app-only probe: ODataError status={Status} isDelegatedTokenTrap={IsTrap}",
                ex.ResponseStatusCode, isTrap);
            if (isTrap)
            {
                var diagnostic =
                    $"Graph error code={ex.Error?.Code ?? "(no code)"} message='{ex.Error?.Message ?? ex.Message}'. " +
                    $"Regression signature: the deployed configuration is issuing a public/delegated " +
                    $"token where an app-only cert-based token is required.";
                return new T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult(
                    ex.ResponseStatusCode, diagnostic);
            }
            return new T6GraphAppOnlyProbeResult.InfraFaultResult(
                $"Graph containerTypes GET ODataError status={ex.ResponseStatusCode}: " +
                $"{ex.Error?.Code ?? "(no code)"} {ex.Error?.Message ?? ex.Message}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new T6GraphAppOnlyProbeResult.InfraFaultResult(
                $"Graph containerTypes GET exceeded configured timeout " +
                $"{_options.TrapVerifierTimeout}. Retry after transient resolves.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex,
                "T6 Graph app-only probe: unexpected {ExType}", ex.GetType().Name);
            return new T6GraphAppOnlyProbeResult.InfraFaultResult(
                $"Graph containerTypes GET failed with unexpected {ex.GetType().Name}: {ex.Message}.");
        }
    }
}
