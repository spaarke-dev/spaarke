// -----------------------------------------------------------------------------
// GraphContainerProvisioner.cs
//
// Task 214 (H8-B rewrite, 2026-08-30) — production ISpeContainerProvisioner.
// SUPERSEDES GraphContainerTypeProvisioner (deleted 2026-08-30). The old
// implementation attempted container-TYPE creation + owning-app permission
// registration + container creation in one shot; that flow is architecturally
// broken under L2's app-only runtime credential per topology doc §R5 (verified
// 403 accessDenied 2026-08-30 — runs/h8-live-test-2026-08-30.md).
//
// H8-B RESPONSIBILITY (per topology doc §6 + task 214 POML):
//   Two Graph calls, both app-only, as the container type's owning app (task 248:
//   the Worker UAMI's federated identity credential on the owning app —
//   SpeConfidentialClientGraphFactory):
//     (1) POST /storage/fileStorage/containers with { displayName, description,
//         containerTypeId } — creates the container inside the PRE-EXISTING
//         container-type (topology doc §R1: containerTypeId is a permanent
//         operator prereq, not per-customer). Response .Id is the new container
//         GUID.
//     (2) POST /storage/fileStorage/containers/{id}/activate — REQUIRED per
//         topology doc §6 ("A container is not usable until activated"). If
//         this fails after (1) succeeded, we surface an ActivateFailure so the
//         handler can classify QuarantineRequired (created-but-not-activated
//         container exists as data on the SPE side).
//
// NAMESPACE CHANGE: this file moved from Handlers/SpeContainerType/ to
// Handlers/SpeContainer/ + the namespace changed to reflect H8's new,
// narrower scope (container CREATION, not container-TYPE creation).
//
// GRAPH SDK SHAPES (Microsoft.Graph 6.5.0):
//   - Storage.FileStorage.Containers.PostAsync(FileStorageContainer)
//   - Storage.FileStorage.Containers[{id}].Activate.PostAsync()
//   The Beta-vs-v1.0 endpoint choice is opaque to the caller — the installed
//   Microsoft.Graph package exposes both under GraphServiceClient's default
//   surface (no separate Microsoft.Graph.Beta package). The GA models match
//   the beta JSON body shape for these two calls.
//
// TOKEN AUTH: app-only as the container type's owning app (T6 posture), via the
// Worker UAMI's federated identity credential on it (task 248 — no certificate,
// no secret). This is the CONTAINER-CREATION token, which
// per topology doc §6 IS app-only-capable — unlike CONTAINER-TYPE-CREATION
// per §R5, which requires delegated. The distinction is critical.
//
// NOT UNIT-TESTED IN THE CI SUITE (real Microsoft.Graph HTTP calls) — parity
// with the established project precedent (H8SpeContainerHandlerTests.cs
// substitutes a fake ISpeContainerProvisioner). The owning-app credential itself
// IS unit-tested via SpeConfidentialClientGraphFactoryTests.cs.
// -----------------------------------------------------------------------------

using Microsoft.Extensions.Options;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <inheritdoc cref="ISpeContainerProvisioner"/>
public sealed class GraphContainerProvisioner : ISpeContainerProvisioner
{
    private readonly SpeConfidentialClientGraphFactory _graphFactory;
    private readonly SpeContainerOptions _options;
    private readonly ILogger<GraphContainerProvisioner> _logger;

    /// <summary>Constructs the production provisioner over the owning-app Graph client factory (task 248).</summary>
    public GraphContainerProvisioner(
        SpeConfidentialClientGraphFactory graphFactory,
        IOptions<SpeContainerOptions> options,
        ILogger<GraphContainerProvisioner> logger)
    {
        ArgumentNullException.ThrowIfNull(graphFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        _graphFactory = graphFactory;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<SpeContainerProvisionOutcome> ProvisionAsync(
        SpeContainerProvisionRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);

        // The owning-app token is acquired on the first Graph call. A failed exchange
        // (AuthenticationFailedException) happens BEFORE the container exists — no
        // external SPE side effect — and is left uncaught so it reaches
        // H8SpeContainerHandler's provisioner-infra-fault catch (Resumable).
        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);

        _logger.LogInformation(
            "H8-B Graph SPE container creation starting: customerId={CustomerId} tenantId={TenantId} " +
            "containerTypeId={ContainerTypeId} owningAppId={OwningAppId}",
            request.CustomerId, request.TenantId, request.ContainerTypeId, request.OwningAppId);

        // (1) Create the container per topology doc §6.
        string containerId;
        try
        {
            using var createTimeout = LinkedTimeout(cancellationToken);
            var container = await graph.Storage.FileStorage.Containers.PostAsync(
                new FileStorageContainer
                {
                    DisplayName = request.DisplayName,
                    Description = request.Description,
                    ContainerTypeId = Guid.Parse(request.ContainerTypeId),
                },
                cancellationToken: createTimeout.Token).ConfigureAwait(false);

            if (container is null || string.IsNullOrWhiteSpace(container.Id))
            {
                return new SpeContainerProvisionOutcome.CreateFailure(
                    $"Graph POST /storage/fileStorage/containers returned no usable Id for customerId " +
                    $"'{request.CustomerId}' (containerTypeId '{request.ContainerTypeId}').");
            }

            containerId = container.Id;
            _logger.LogInformation(
                "H8-B container created: containerId={ContainerId} customerId={CustomerId}. " +
                "Proceeding to /activate.", containerId, request.CustomerId);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "H8-B Graph container CREATE ODataError: customerId={CustomerId} status={Status}",
                request.CustomerId, ex.ResponseStatusCode);
            return new SpeContainerProvisionOutcome.CreateFailure(
                $"Graph POST /storage/fileStorage/containers failed with ODataError {ex.ResponseStatusCode}: " +
                $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message} (customerId '{request.CustomerId}', " +
                $"containerTypeId '{request.ContainerTypeId}').");
        }

        // (2) Activate the container per topology doc §6 ("A container is not
        // usable until activated"). If this fails, we surface ActivateFailure
        // with the containerId so the handler classifies QuarantineRequired.
        try
        {
            using var activateTimeout = LinkedTimeout(cancellationToken);
            await graph.Storage.FileStorage.Containers[containerId].Activate.PostAsync(
                cancellationToken: activateTimeout.Token).ConfigureAwait(false);

            _logger.LogInformation(
                "H8-B container activated: containerId={ContainerId} customerId={CustomerId}",
                containerId, request.CustomerId);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "H8-B Graph container ACTIVATE ODataError: customerId={CustomerId} containerId={ContainerId} " +
                "status={Status}",
                request.CustomerId, containerId, ex.ResponseStatusCode);
            return new SpeContainerProvisionOutcome.ActivateFailure(
                containerId,
                $"Graph POST /storage/fileStorage/containers/{containerId}/activate failed with ODataError " +
                $"{ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. Container was " +
                $"created but is unusable until activated (topology doc §6). QuarantineRequired.");
        }

        return new SpeContainerProvisionOutcome.Success(new SpeContainerProvisionOutputs(
            ContainerId: containerId));
    }

    private CancellationTokenSource LinkedTimeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.GraphRequestTimeout);
        return cts;
    }
}
