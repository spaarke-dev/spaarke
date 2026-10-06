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
// BUSINESS-UNIT STAMP + RESUME (unified-access-control-r2 task 165, owner rounds
// 35 / 41 / 49 — re-applied to H8-B at the batch-4 integration):
//   - BindRootContainerAsync / BindNewContainerAsync stamp the container with the
//     customer environment's ROOT business unit (custom property
//     Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName — the one
//     C# constant the BFF shares), read it back, and DELETE the container when the
//     stamp did not land. H8SpeContainerHandler calls it after verification (the
//     ArchTest SpeAdminContainerBindingGuardTests lists this file as a deferred
//     binder and checks that order).
//   - EVERY fault after the container POST was sent is returned, never thrown: an
//     ODataError is Graph's own answer (nothing created); anything else — a client
//     timeout (LinkedTimeout's OperationCanceledException), a dropped connection, the
//     caller's cancellation, a 2xx without an id — is CreateFailure(ContainerInDoubt)
//     so H8 records that a container may exist. Only the owning-app token exchange
//     (before any request is sent) still throws. The /activate call likewise
//     returns ActivateFailure (NoAnswer) for a fault with no answer.
//   - ActivateAsync re-activates a container H8 recorded whose /activate failed, so
//     a resume never creates a second container.
//
// TESTS: GraphContainerProvisionerBindTests drives this class through
// SpeConfidentialClientGraphFactory's test seam (a hand-written fake Graph
// transport — never Mock<HttpMessageHandler>): the stamp body, the read-back, the
// removal, and the in-doubt classification of create faults.
// H8SpeContainerHandlerTests substitutes a fake ISpeContainerProvisioner for the
// handler's orchestration. The owning-app credential itself is unit-tested via
// SpeConfidentialClientGraphFactoryTests.cs.
// -----------------------------------------------------------------------------

using Azure.Identity;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
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
        // (AuthenticationFailedException / CredentialUnavailableException) happens BEFORE any
        // request is sent — no external SPE side effect — and is rethrown so it reaches
        // H8SpeContainerHandler's provisioner-infra-fault catch (Resumable).
        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);
        return await CreateInTypeAsync(graph, request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The Graph part of <see cref="ProvisionAsync"/>: create the container in the pre-existing type, then activate it.
    /// Every fault after the container POST was sent is RETURNED (owner round 49 item 2): an <see cref="ODataError"/> is
    /// Graph's answer (nothing created); anything else leaves a container in doubt.
    /// </summary>
    private async Task<SpeContainerProvisionOutcome> CreateInTypeAsync(
        GraphServiceClient graph,
        SpeContainerProvisionRequest request,
        CancellationToken cancellationToken)
    {
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
                // Graph answered 2xx — a container may well exist, but no one can name it.
                return new SpeContainerProvisionOutcome.CreateFailure(
                    $"Graph POST /storage/fileStorage/containers returned no usable Id for customerId " +
                    $"'{request.CustomerId}' (containerTypeId '{request.ContainerTypeId}') — a container may have been created.",
                    ContainerInDoubt: true);
            }

            containerId = container.Id;
            _logger.LogInformation(
                "H8-B container created: containerId={ContainerId} customerId={CustomerId}. " +
                "Proceeding to /activate.", containerId, request.CustomerId);
        }
        catch (ODataError ex)
        {
            // Graph's own answer: the POST did NOT create a container (nothing is in doubt).
            _logger.LogError(ex,
                "H8-B Graph container CREATE ODataError: customerId={CustomerId} status={Status}",
                request.CustomerId, ex.ResponseStatusCode);
            return new SpeContainerProvisionOutcome.CreateFailure(
                $"Graph POST /storage/fileStorage/containers failed with ODataError {ex.ResponseStatusCode}: " +
                $"{ex.Error?.Code} {ex.Error?.Message ?? ex.Message} (customerId '{request.CustomerId}', " +
                $"containerTypeId '{request.ContainerTypeId}').");
        }
        catch (Exception ex) when (ex is AuthenticationFailedException or CredentialUnavailableException)
        {
            // The owning-app token exchange failed — no request was sent, nothing exists.
            throw;
        }
        catch (Exception ex)
        {
            // NO authoritative answer (owner round 49 item 2): a dropped connection, a client-side timeout, the caller's
            // cancellation, an unreadable response. The POST may have created a container — UNBOUND. Never thrown: the
            // handler must record that, or a resume creates a second container and leaves the first one orphaned.
            _logger.LogError(ex,
                "H8-B Graph container CREATE got no answer: customerId={CustomerId} containerTypeId={ContainerTypeId}",
                request.CustomerId, request.ContainerTypeId);
            return new SpeContainerProvisionOutcome.CreateFailure(
                $"Graph POST /storage/fileStorage/containers got no answer: {ex.GetType().Name}: {ex.Message} " +
                $"(customerId '{request.CustomerId}', containerTypeId '{request.ContainerTypeId}') — a container may have " +
                "been created.",
                ContainerInDoubt: true);
        }

        // (2) Activate the container per topology doc §6 ("A container is not usable until activated").
        return await ActivateCoreAsync(graph, containerId, request.CustomerId, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<SpeContainerProvisionOutcome> ActivateAsync(
        SpeContainerActivationRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerId);

        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);
        return await ActivateCoreAsync(graph, request.ContainerId, request.CustomerId, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// <c>POST …/containers/{id}/activate</c>. A failure — Graph's answer or none — is an
    /// <see cref="SpeContainerProvisionOutcome.ActivateFailure"/> carrying the container id, so H8 keeps it on record.
    /// </summary>
    private async Task<SpeContainerProvisionOutcome> ActivateCoreAsync(
        GraphServiceClient graph, string containerId, string? customerId, CancellationToken cancellationToken)
    {
        try
        {
            using var activateTimeout = LinkedTimeout(cancellationToken);
            await graph.Storage.FileStorage.Containers[containerId].Activate.PostAsync(
                cancellationToken: activateTimeout.Token).ConfigureAwait(false);

            _logger.LogInformation(
                "H8-B container activated: containerId={ContainerId} customerId={CustomerId}",
                containerId, customerId);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "H8-B Graph container ACTIVATE ODataError: customerId={CustomerId} containerId={ContainerId} " +
                "status={Status}",
                customerId, containerId, ex.ResponseStatusCode);
            return new SpeContainerProvisionOutcome.ActivateFailure(
                containerId,
                $"Graph POST /storage/fileStorage/containers/{containerId}/activate failed with ODataError " +
                $"{ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. Container was " +
                $"created but is unusable until activated (topology doc §6). QuarantineRequired.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "H8-B Graph container ACTIVATE got no answer: customerId={CustomerId} containerId={ContainerId}",
                customerId, containerId);
            return new SpeContainerProvisionOutcome.ActivateFailure(
                containerId,
                $"Graph POST /storage/fileStorage/containers/{containerId}/activate got no answer: {ex.GetType().Name}: " +
                $"{ex.Message}. The container exists; its activation status is unknown. QuarantineRequired.",
                NoAnswer: true);
        }

        return new SpeContainerProvisionOutcome.Success(new SpeContainerProvisionOutputs(
            ContainerId: containerId));
    }

    /// <inheritdoc/>
    public async Task<SpeContainerBindOutcome> BindRootContainerAsync(
        SpeContainerBindRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerId);

        // The same owning-app identity that created the container (task 248, MI-FIC).
        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);
        return await BindNewContainerAsync(graph, request.ContainerId, request.BusinessUnitId, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// THE bind step for a container this handler created (unified-access-control-r2 task 165, owner round 35 item 1 —
    /// the same rule the BFF's creation paths follow): <c>PATCH /storage/fileStorage/containers/{id}/customProperties</c>
    /// with the property map as the BODY ROOT (Graph merges, so nothing else is touched), a single-container read-back
    /// (<c>$select=id,customProperties</c> — the collection drops custom properties), and — when the stamp did not land,
    /// for ANY reason — <c>DELETE /storage/fileStorage/containers/{id}</c>, so no unbound container is left behind.
    /// </summary>
    internal async Task<SpeContainerBindOutcome> BindNewContainerAsync(
        GraphServiceClient graph,
        string containerId,
        Guid businessUnitId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);

        string failure;
        try
        {
            if (businessUnitId == Guid.Empty)
            {
                throw new InvalidOperationException("No owning business unit was resolved (Guid.Empty) — the container cannot be bound.");
            }

            await WriteStampAsync(graph, containerId, businessUnitId, cancellationToken).ConfigureAwait(false);

            using var readTimeout = LinkedTimeout(cancellationToken);
            var container = await graph.Storage.FileStorage.Containers[containerId]
                .GetAsync(config => config.QueryParameters.Select = new[] { "id", "customProperties" }, readTimeout.Token)
                .ConfigureAwait(false);

            var stamped = ReadStamp(container);
            if (stamped == businessUnitId)
            {
                _logger.LogInformation(
                    "H8 container {ContainerId} bound to business unit {BusinessUnitId} (stamp read back).",
                    containerId, businessUnitId);
                return new SpeContainerBindOutcome.Bound();
            }

            failure = $"The business-unit stamp did not read back on container '{containerId}' (read: " +
                      $"'{stamped?.ToString() ?? "none"}', expected '{businessUnitId}').";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            failure = ex is ODataError odata
                ? $"Graph ODataError {odata.ResponseStatusCode} binding container '{containerId}': {odata.Error?.Code} {odata.Error?.Message ?? odata.Message}"
                : $"Binding container '{containerId}' failed: {ex.GetType().Name}: {ex.Message}";
        }

        _logger.LogError(
            "H8 container {ContainerId} could not be bound to business unit {BusinessUnitId} — removing it. {Failure}",
            containerId, businessUnitId, failure);

        try
        {
            using var deleteTimeout = LinkedTimeout(CancellationToken.None);
            await graph.Storage.FileStorage.Containers[containerId].DeleteAsync(cancellationToken: deleteTimeout.Token)
                .ConfigureAwait(false);
            return new SpeContainerBindOutcome.NotBound(failure + " The container was removed.", Removed: true);
        }
        catch (ODataError deleteEx) when (deleteEx.ResponseStatusCode == 404)
        {
            // Already gone (an earlier removal, or an operator's) — nothing unbound remains, and a resume must not keep
            // trying to bind a container that no longer exists.
            return new SpeContainerBindOutcome.NotBound(failure + " The container no longer exists.", Removed: true);
        }
        catch (Exception deleteEx)
        {
            _logger.LogCritical(deleteEx,
                "H8 container {ContainerId} is UNBOUND and could not be removed; no SPE admin route reaches it until " +
                "H8 resumes and binds it, the backfill binds it (-Bind), or an operator removes it.", containerId);
            return new SpeContainerBindOutcome.NotBound(
                failure + $" Removing it ALSO failed ({deleteEx.GetType().Name}: {deleteEx.Message}); container " +
                $"'{containerId}' is left unbound.", Removed: false);
        }
    }

    /// <summary>
    /// <c>PATCH …/containers/{id}/customProperties</c> with <c>{"&lt;stamp&gt;": {"value": "&lt;unit&gt;", "isSearchable": false}}</c>
    /// as the body root — the shape the BFF sends (<c>SpeAdminGraphService.WriteBusinessUnitStampAsync</c>), sent through
    /// the SDK's request adapter so Graph failures arrive as <see cref="ODataError"/>.
    /// </summary>
    private async Task WriteStampAsync(
        GraphServiceClient graph, string containerId, Guid businessUnitId, CancellationToken cancellationToken)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName] = new Dictionary<string, object>
            {
                ["value"] = businessUnitId.ToString("D"),
                ["isSearchable"] = false,
            },
        });

        var baseUrl = (graph.RequestAdapter.BaseUrl ?? "https://graph.microsoft.com/v1.0").TrimEnd('/');
        var requestInfo = new Microsoft.Kiota.Abstractions.RequestInformation
        {
            HttpMethod = Microsoft.Kiota.Abstractions.Method.PATCH,
            URI = new Uri($"{baseUrl}/storage/fileStorage/containers/{Uri.EscapeDataString(containerId)}/customProperties"),
        };
        requestInfo.Headers.Add("Accept", "application/json");
        requestInfo.SetStreamContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(payload)), "application/json");

        var errorMapping = new Dictionary<string, Microsoft.Kiota.Abstractions.Serialization.ParsableFactory<Microsoft.Kiota.Abstractions.Serialization.IParsable>>
        {
            { "XXX", ODataError.CreateFromDiscriminatorValue },
        };

        using var timeout = LinkedTimeout(cancellationToken);
        await graph.RequestAdapter.SendNoContentAsync(requestInfo, errorMapping, timeout.Token).ConfigureAwait(false);
    }

    /// <summary>The business unit the container's stamp names, or null when it carries none (or not one GUID).</summary>
    private static Guid? ReadStamp(FileStorageContainer? container)
    {
        var properties = container?.CustomProperties?.AdditionalData;
        if (properties is null)
        {
            return null;
        }

        foreach (var (name, raw) in properties)
        {
            if (!string.Equals(name?.Trim(), Spaarke.Contracts.Spe.SpeContainerBusinessUnitBinding.PropertyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (raw is Microsoft.Kiota.Abstractions.Serialization.UntypedObject node
                && node.GetValue().TryGetValue("value", out var valueNode)
                && valueNode is Microsoft.Kiota.Abstractions.Serialization.UntypedString text
                && Guid.TryParse(text.GetValue()?.Trim(), out var unit))
            {
                return unit;
            }

            return null;
        }

        return null;
    }

    private CancellationTokenSource LinkedTimeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.GraphRequestTimeout);
        return cts;
    }
}
