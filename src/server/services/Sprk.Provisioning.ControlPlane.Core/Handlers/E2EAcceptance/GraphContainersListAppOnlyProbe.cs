// -----------------------------------------------------------------------------
// GraphContainersListAppOnlyProbe.cs
//
// Production IT6GraphAppOnlyProbe (task 248 — replaces
// GraphContainerTypesListAppOnlyProbe). Issues, app-only as the container type's
// OWNING app (SpeConfidentialClientGraphFactory — the Worker UAMI's federated
// identity credential on the owning app):
//   GET /storage/fileStorage/containers?$filter=containerTypeId eq {id}
// following @odata.nextLink, and reports whether the run's container (H8's
// InterStepState.SpeContainerId) is listed.
//
// WHY THIS CALL (task 248): the previous probe called app-only
// GET /storage/fileStorage/containerTypes, which Microsoft documents as 403 for
// app-only callers — T6 could never pass. The containers listing is the app-only
// call the owning app is granted (FileStorageContainer.Selected + its `full`
// application grant on the registration); it succeeded live from the dev Worker
// identity on 2026-10-03 (T248 POML notes). Requiring the run's container in the
// result makes T6 assert the EFFECT (this stamp's container is reachable app-only
// as the owner), not merely that a token works.
//
// RESPONSE HANDLING mirrors GraphAppOnlyContainerVerifier.cs: linked timeout, 404 →
// ReplicationPending, delegated-token trap phrase → DelegatedTokenTrapDetected, any
// other error → InfraFault.
//
// PAGE CAP: at most MaxPages pages are followed; a listing still unfinished at the cap
// is "no verdict" (InfraFault), never ContainerAbsent — a truncated listing must not
// quarantine a run.
//
// TESTED (GraphContainersListAppOnlyProbeTests) through SpeConfidentialClientGraphFactory's
// internal seam: the real Graph SDK runs against a fake HttpMessageHandler (ADR-038).
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

namespace Sprk.Provisioning.ControlPlane.Handlers.E2EAcceptance;

/// <inheritdoc cref="IT6GraphAppOnlyProbe"/>
public sealed class GraphContainersListAppOnlyProbe : IT6GraphAppOnlyProbe
{
    /// <summary>Upper bound on listing pages followed (defends against a nextLink loop).</summary>
    internal const int MaxPages = 100;

    private readonly SpeConfidentialClientGraphFactory _graphFactory;
    private readonly H13AcceptanceOptions _options;
    private readonly ILogger<GraphContainersListAppOnlyProbe> _logger;

    /// <summary>Constructs the production probe over the owning-app Graph client factory.</summary>
    public GraphContainersListAppOnlyProbe(
        SpeConfidentialClientGraphFactory graphFactory,
        IOptions<H13AcceptanceOptions> options,
        ILogger<GraphContainersListAppOnlyProbe> logger)
    {
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _graphFactory = graphFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<T6GraphAppOnlyProbeResult> ProbeAsync(
        string tenantId, string ownerAppId, string containerTypeId, string containerId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        using var graph = _graphFactory.CreateGraphClient(tenantId, ownerAppId);

        _logger.LogInformation(
            "T6 Graph app-only probe starting: tenantId={TenantId} ownerAppId={OwnerAppId} containerTypeId={ContainerTypeId} " +
            "containerId={ContainerId}", tenantId, ownerAppId, containerTypeId, containerId);

        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_options.TrapVerifierTimeout);

            var listed = 0;
            var page = await graph.Storage.FileStorage.Containers
                .GetAsync(rc => rc.QueryParameters.Filter = $"containerTypeId eq {containerTypeId}",
                    cancellationToken: timeoutCts.Token)
                .ConfigureAwait(false);
            for (var pages = 1; page is not null; pages++)
            {
                foreach (FileStorageContainer container in page.Value ?? new List<FileStorageContainer>())
                {
                    listed++;
                    if (string.Equals(container.Id, containerId, StringComparison.Ordinal))
                    {
                        _logger.LogInformation(
                            "T6 Graph app-only probe cleared: container {ContainerId} listed under container type " +
                            "{ContainerTypeId} as the owning app.", containerId, containerTypeId);
                        return T6GraphAppOnlyProbeResults.Succeeded;
                    }
                }
                if (string.IsNullOrEmpty(page.OdataNextLink))
                {
                    break;
                }
                if (pages >= MaxPages)
                {
                    return new T6GraphAppOnlyProbeResult.InfraFaultResult(
                        $"Graph containers listing for container type '{containerTypeId}' still had more pages after " +
                        $"{MaxPages} pages ({listed} containers) without the run's container '{containerId}' — no verdict.");
                }
                page = await graph.Storage.FileStorage.Containers.WithUrl(page.OdataNextLink)
                    .GetAsync(cancellationToken: timeoutCts.Token).ConfigureAwait(false);
            }

            return new T6GraphAppOnlyProbeResult.ContainerAbsentResult(listed,
                $"The app-only containers listing for container type '{containerTypeId}' succeeded as the owning app " +
                $"'{ownerAppId}' ({listed} container(s)) but does not include the run's container '{containerId}'.");
        }
        catch (ODataError ex) when (ex.ResponseStatusCode == 404)
        {
            var diagnostic = $"Graph containers listing returned 404 Not Found " +
                             $"({ex.Error?.Code ?? "(no code)"} {ex.Error?.Message ?? ex.Message}).";
            _logger.LogInformation(
                "T6 Graph app-only probe: 404 -- SPE replication window signature. tenantId={TenantId}", tenantId);
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
                return new T6GraphAppOnlyProbeResult.DelegatedTokenTrapDetectedResult(
                    ex.ResponseStatusCode,
                    $"Graph error code={ex.Error?.Code ?? "(no code)"} message='{ex.Error?.Message ?? ex.Message}'. " +
                    "Regression signature: the deployed configuration is issuing a public/delegated token where an " +
                    "app-only owning-app token is required.");
            }
            return new T6GraphAppOnlyProbeResult.InfraFaultResult(
                $"Graph containers listing ODataError status={ex.ResponseStatusCode}: " +
                $"{ex.Error?.Code ?? "(no code)"} {ex.Error?.Message ?? ex.Message}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new T6GraphAppOnlyProbeResult.InfraFaultResult(
                $"Graph containers listing exceeded configured timeout {_options.TrapVerifierTimeout}. " +
                "Retry after transient resolves.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "T6 Graph app-only probe: unexpected {ExType}", ex.GetType().Name);
            return new T6GraphAppOnlyProbeResult.InfraFaultResult(
                $"Graph containers listing failed with unexpected {ex.GetType().Name}: {ex.Message}.");
        }
    }
}
