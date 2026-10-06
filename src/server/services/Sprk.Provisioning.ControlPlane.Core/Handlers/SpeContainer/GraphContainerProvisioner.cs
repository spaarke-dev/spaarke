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
// TASK 227e — REUSE: FindCustomerContainersAsync lists the type's containers (the call the T6 probe proved
// live as the owning app), keeps those named as H8 names the customer's container, and reads each one's
// `spaarkeCustomerId` custom property (GET ?$select=id,customProperties): another customer's marker excludes it.
// EnsureCustomerMarkerAsync writes that marker (PATCH .../customProperties — not settable at create) when absent;
// ActivateAsync activates a reused container an earlier attempt left inactive.
//
// TESTS: ProvisionAsync (CREATE + ACTIVATE) has no unit test here (H8SpeContainerHandlerTests
// substitutes a fake ISpeContainerProvisioner). The task-227e calls are tested by GraphContainerProvisionerReuseTests,
// and EnsureGrantsAsync (task 227b) by
// GraphContainerProvisionerGrantTests — the real Graph SDK against a scripted HttpMessageHandler
// through SpeConfidentialClientGraphFactory's internal seam. The owning-app credential itself is
// unit-tested via SpeConfidentialClientGraphFactoryTests.cs.
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;

namespace Sprk.Provisioning.ControlPlane.Handlers.SpeContainer;

/// <inheritdoc cref="ISpeContainerProvisioner"/>
public sealed class GraphContainerProvisioner : ISpeContainerProvisioner
{
    /// <summary>Upper bound on listing pages followed by <see cref="FindCustomerContainersAsync"/> (defends against a nextLink loop).</summary>
    internal const int MaxListingPages = 100;

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
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // A timed-out create may still have created the container; H8 classifies the thrown fault, and its next
            // attempt finds a container that did get created (task 227e lookup).
            throw new TimeoutException($"The container create exceeded {_options.GraphRequestTimeout}.");
        }

        // (2) Activate the container per topology doc §6 ("A container is not
        // usable until activated"). If this fails, we surface ActivateFailure
        // with the containerId so the handler classifies QuarantineRequired.
        return await ActivateCoreAsync(graph, containerId, cancellationToken).ConfigureAwait(false);
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
        return await ActivateCoreAsync(graph, request.ContainerId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<SpeContainerProvisionOutcome> ActivateCoreAsync(
        GraphServiceClient graph, string containerId, CancellationToken cancellationToken)
    {
        try
        {
            using var activateTimeout = LinkedTimeout(cancellationToken);
            await graph.Storage.FileStorage.Containers[containerId].Activate.PostAsync(
                cancellationToken: activateTimeout.Token).ConfigureAwait(false);

            _logger.LogInformation("H8-B container activated: containerId={ContainerId}", containerId);
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex,
                "H8-B Graph container ACTIVATE ODataError: containerId={ContainerId} status={Status}",
                containerId, ex.ResponseStatusCode);
            return new SpeContainerProvisionOutcome.ActivateFailure(
                containerId,
                $"Graph POST /storage/fileStorage/containers/{containerId}/activate failed with ODataError " +
                $"{ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}. The container " +
                $"exists but is unusable until activated (topology doc §6). QuarantineRequired.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Activating container '{containerId}' exceeded {_options.GraphRequestTimeout}.");
        }

        return new SpeContainerProvisionOutcome.Success(new SpeContainerProvisionOutputs(
            ContainerId: containerId));
    }

    /// <inheritdoc/>
    public async Task<SpeContainerLookupOutcome> FindCustomerContainersAsync(
        SpeContainerLookupRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Description);

        if (!Guid.TryParse(request.ContainerTypeId, out var containerTypeGuid))
        {
            // The id goes into an OData filter: only a GUID may reach it.
            return new SpeContainerLookupOutcome.Failure(
                $"Container type id '{request.ContainerTypeId}' is not a GUID — the listing was not attempted.");
        }

        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);
        var wantedName = request.DisplayName.Trim();
        var wantedDescription = request.Description.Trim();

        // (1) The container type's containers, by H8's display name — or by H8's description for this customer, which
        //     survives a rename when the listing returns descriptions. The type is shared by every Model 1 stamp (D28),
        //     so the listing holds other customers' containers too; only these candidates are read further.
        var named = new List<FileStorageContainer>();
        var listed = 0;
        try
        {
            FileStorageContainerCollectionResponse? page;
            using (var timeout = LinkedTimeout(cancellationToken))
            {
                page = await graph.Storage.FileStorage.Containers
                    .GetAsync(rc => rc.QueryParameters.Filter = $"containerTypeId eq {containerTypeGuid}",
                        cancellationToken: timeout.Token)
                    .ConfigureAwait(false);
            }
            for (var pages = 1; page is not null; pages++)
            {
                foreach (var container in page.Value ?? [])
                {
                    listed++;
                    if (!string.IsNullOrWhiteSpace(container.Id)
                        && (string.Equals(container.DisplayName?.Trim(), wantedName, StringComparison.OrdinalIgnoreCase)
                            || string.Equals(container.Description?.Trim(), wantedDescription, StringComparison.Ordinal)))
                    {
                        named.Add(container);
                    }
                }
                if (string.IsNullOrEmpty(page.OdataNextLink))
                {
                    break;
                }
                if (pages >= MaxListingPages)
                {
                    return new SpeContainerLookupOutcome.Failure(
                        $"The containers listing for container type '{request.ContainerTypeId}' still had more pages after " +
                        $"{MaxListingPages} pages ({listed} containers) — no verdict on whether customer " +
                        $"'{request.CustomerId}' already has a container, so none was created.");
                }
                using var nextTimeout = LinkedTimeout(cancellationToken);
                page = await graph.Storage.FileStorage.Containers.WithUrl(page.OdataNextLink)
                    .GetAsync(cancellationToken: nextTimeout.Token).ConfigureAwait(false);
            }
        }
        catch (ODataError ex)
        {
            _logger.LogError(ex, "H8 container lookup: listing ODataError status={Status}", ex.ResponseStatusCode);
            return new SpeContainerLookupOutcome.Failure(
                $"Graph GET /storage/fileStorage/containers?$filter=containerTypeId eq {request.ContainerTypeId} failed " +
                $"with HTTP {ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The containers listing exceeded {_options.GraphRequestTimeout}.");
        }

        // (2) Whose a candidate is. The marker decides when present: this customer's → match; another customer's →
        //     never returned. With no marker (a container from before the marker, or one whose marker write never
        //     happened) only H8's description for THIS customer proves it — a display name alone is operator-supplied
        //     intake and could name another customer's unmarked container.
        var matches = new List<SpeContainerMatch>();
        foreach (var container in named)
        {
            ContainerOwnership ownership;
            try
            {
                ownership = await ReadOwnershipAsync(graph, container.Id!, cancellationToken).ConfigureAwait(false);
            }
            catch (ODataError ex)
            {
                return new SpeContainerLookupOutcome.Failure(
                    $"Reading container '{container.Id}' ('{container.DisplayName}') failed with HTTP " +
                    $"{ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message} — no verdict on " +
                    "whether it is this customer's.");
            }

            if (ownership.MarkerUnreadable)
            {
                return new SpeContainerLookupOutcome.Failure(
                    $"Container '{container.Id}' ('{container.DisplayName}') has a {SpeContainerMarker.PropertyName} custom " +
                    "property H8 cannot read as a string — no verdict on whether it is this customer's.");
            }

            var mine = ownership.Marker is not null
                ? string.Equals(ownership.Marker, request.CustomerId, StringComparison.Ordinal)
                : string.Equals(ownership.Description?.Trim() ?? container.Description?.Trim(), wantedDescription, StringComparison.Ordinal);
            if (!mine)
            {
                _logger.LogInformation(
                    "H8 container lookup: candidate {ContainerId} is not customer {CustomerId}'s ({Reason}).",
                    container.Id, request.CustomerId,
                    ownership.Marker is not null ? "marked for a different customer" : "unmarked, and not H8's container for this customer");
                continue;
            }
            matches.Add(new SpeContainerMatch(container.Id!, container.DisplayName ?? string.Empty, ownership.Marker));
        }

        _logger.LogInformation(
            "H8 container lookup: containerTypeId={ContainerTypeId} listed={Listed} sameName={SameName} matches={Matches} " +
            "customerId={CustomerId}", request.ContainerTypeId, listed, named.Count, matches.Count, request.CustomerId);
        return new SpeContainerLookupOutcome.Found(matches);
    }

    /// <inheritdoc/>
    public async Task<SpeContainerMarkerOutcome> EnsureCustomerMarkerAsync(
        SpeContainerMarkerRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.CustomerId);

        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);

        ContainerOwnership ownership;
        try
        {
            ownership = await ReadOwnershipAsync(graph, request.ContainerId, cancellationToken).ConfigureAwait(false);
        }
        catch (ODataError ex)
        {
            return MarkerFailure("GET", request.ContainerId, ex);
        }

        if (ownership.MarkerUnreadable)
        {
            return new SpeContainerMarkerOutcome.Failure(
                $"Container '{request.ContainerId}' has a {SpeContainerMarker.PropertyName} custom property H8 cannot read as " +
                "a string; H8 wrote nothing. Inspect the container's custom properties, then resume.");
        }

        var marker = ownership.Marker;
        if (marker is not null)
        {
            return string.Equals(marker, request.CustomerId, StringComparison.Ordinal)
                ? new SpeContainerMarkerOutcome.Success(Written: false)
                : new SpeContainerMarkerOutcome.Failure(
                    $"Container '{request.ContainerId}' is marked for a different customer ({SpeContainerMarker.PropertyName}); " +
                    $"H8 never takes over another customer's container and wrote nothing. The run's container id must be " +
                    $"customer '{request.CustomerId}''s — check InterStepState.SpeContainerId.");
        }

        // Custom properties cannot be set in the create body; the sub-resource PATCH merges (other properties untouched).
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [SpeContainerMarker.PropertyName] = new Dictionary<string, object>
            {
                ["value"] = request.CustomerId,
                ["isSearchable"] = false,
            },
        });
        var patch = new RequestInformation
        {
            HttpMethod = Method.PATCH,
            URI = new Uri($"{BaseUrl(graph)}/storage/fileStorage/containers/{Uri.EscapeDataString(request.ContainerId)}/customProperties"),
        };
        patch.Headers.Add("Accept", "application/json");
        patch.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)), "application/json");

        try
        {
            using var timeout = LinkedTimeout(cancellationToken);
            await graph.RequestAdapter.SendNoContentAsync(patch, ODataErrorMapping, timeout.Token).ConfigureAwait(false);
        }
        catch (ODataError ex)
        {
            return MarkerFailure("PATCH", request.ContainerId, ex);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"The marker write on container '{request.ContainerId}' exceeded {_options.GraphRequestTimeout}.");
        }

        _logger.LogInformation(
            "H8 marked container {ContainerId} as customer {CustomerId}'s ({Property}).",
            request.ContainerId, request.CustomerId, SpeContainerMarker.PropertyName);
        return new SpeContainerMarkerOutcome.Success(Written: true);
    }

    /// <summary>
    /// What a container says about its owner: its <see cref="SpeContainerMarker.PropertyName"/> value (trimmed; null when
    /// absent or blank), whether that property is present but not a readable string, and its description.
    /// </summary>
    private sealed record ContainerOwnership(string? Marker, bool MarkerUnreadable, string? Description);

    private async Task<ContainerOwnership> ReadOwnershipAsync(GraphServiceClient graph, string containerId, CancellationToken cancellationToken)
    {
        FileStorageContainer? container;
        try
        {
            // customProperties is not in the default GET payload — select it (T227d research).
            using var timeout = LinkedTimeout(cancellationToken);
            container = await graph.Storage.FileStorage.Containers[containerId]
                .GetAsync(r => r.QueryParameters.Select = ["id", "description", "customProperties"], timeout.Token)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Reading container '{containerId}' exceeded {_options.GraphRequestTimeout}.");
        }

        foreach (var entry in container?.CustomProperties?.AdditionalData ?? new Dictionary<string, object>())
        {
            if (!string.Equals(entry.Key.Trim(), SpeContainerMarker.PropertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            if (entry.Value is UntypedObject obj && obj.GetValue().TryGetValue("value", out var node) && node is UntypedString s)
            {
                var value = s.GetValue();
                return new ContainerOwnership(string.IsNullOrWhiteSpace(value) ? null : value.Trim(), false, container?.Description);
            }
            return new ContainerOwnership(null, MarkerUnreadable: true, container?.Description);
        }
        return new ContainerOwnership(null, false, container?.Description);
    }

    private SpeContainerMarkerOutcome MarkerFailure(string verb, string containerId, ODataError ex)
    {
        _logger.LogError(ex, "H8 container marker {Verb} ODataError: containerId={ContainerId} status={Status}",
            verb, containerId, ex.ResponseStatusCode);
        return new SpeContainerMarkerOutcome.Failure(
            $"Graph {verb} /storage/fileStorage/containers/{containerId}/customProperties failed with HTTP " +
            $"{ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}.");
    }

    private static string BaseUrl(GraphServiceClient graph)
    {
        var baseUrl = graph.RequestAdapter?.BaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl) ? "https://graph.microsoft.com/v1.0" : baseUrl.TrimEnd('/');
    }

    /// <inheritdoc/>
    public async Task<SpeContainerTypeGrantOutcome> EnsureGrantsAsync(
        SpeContainerTypeGrantRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ContainerTypeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.OwningAppId);

        using var graph = _graphFactory.CreateGraphClient(request.TenantId, request.OwningAppId);
        var grants = graph.Storage.FileStorage.ContainerTypeRegistrations[request.ContainerTypeId].ApplicationPermissionGrants;
        var written = new List<string>();

        foreach (var grant in request.Grants)
        {
            var item = grants[grant.AppId];
            FileStorageContainerTypeAppPermissionGrant? existing;
            try
            {
                using var getTimeout = LinkedTimeout(cancellationToken);
                existing = await item.GetAsync(cancellationToken: getTimeout.Token).ConfigureAwait(false);
            }
            catch (ODataError ex) when (ex.ResponseStatusCode == 404)
            {
                existing = null;
            }
            catch (ODataError ex)
            {
                return GrantFailure(grant.AppId, "GET", request.ContainerTypeId, ex);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Container-type grant GET for app '{grant.AppId}' exceeded {_options.GraphRequestTimeout}.");
            }

            if (existing is not null
                && SamePermissions(existing.ApplicationPermissions, grant.ApplicationPermissions)
                && SamePermissions(existing.DelegatedPermissions, grant.DelegatedPermissions))
            {
                continue;
            }

            // The appId is the URL key only — Learn: "Don't include the appId in the body".
            var desired = new FileStorageContainerTypeAppPermissionGrant
            {
                ApplicationPermissions = ToGraph(grant.ApplicationPermissions),
                DelegatedPermissions = ToGraph(grant.DelegatedPermissions),
            };
            try
            {
                using var writeTimeout = LinkedTimeout(cancellationToken);
                if (existing is null)
                {
                    // Graph v1.0 creates a grant with PUT .../applicationPermissionGrants/{appId}; the SDK (6.5.0) has
                    // no typed PUT, so send the PATCH request it builds with the method switched.
                    var put = item.ToPatchRequestInformation(desired);
                    put.HttpMethod = Method.PUT;
                    await graph.RequestAdapter.SendAsync(put, FileStorageContainerTypeAppPermissionGrant.CreateFromDiscriminatorValue,
                        ODataErrorMapping, writeTimeout.Token).ConfigureAwait(false);
                }
                else
                {
                    await item.PatchAsync(desired, cancellationToken: writeTimeout.Token).ConfigureAwait(false);
                }
            }
            catch (ODataError ex)
            {
                return GrantFailure(grant.AppId, existing is null ? "PUT" : "PATCH", request.ContainerTypeId, ex);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Container-type grant write for app '{grant.AppId}' exceeded {_options.GraphRequestTimeout}.");
            }

            written.Add(grant.AppId);
            // An update overwrites both lists — log what was there for audit.
            _logger.LogInformation(
                "H8 container-type grant {Action}: containerTypeId={ContainerTypeId} appId={AppId} application=[{Application}] " +
                "delegated=[{Delegated}] previous application=[{PreviousApplication}] previous delegated=[{PreviousDelegated}]",
                existing is null ? "created" : "updated", request.ContainerTypeId, grant.AppId,
                string.Join(",", grant.ApplicationPermissions), string.Join(",", grant.DelegatedPermissions),
                Describe(existing?.ApplicationPermissions), Describe(existing?.DelegatedPermissions));
        }

        return new SpeContainerTypeGrantOutcome.Success(written);
    }

    private static readonly Dictionary<string, ParsableFactory<IParsable>> ODataErrorMapping = new()
    {
        ["XXX"] = ODataError.CreateFromDiscriminatorValue,
    };

    /// <summary>An empty set is sent as <c>["none"]</c> — the value Graph reports for it on GET.</summary>
    private static List<FileStorageContainerTypeAppPermission?> ToGraph(IReadOnlyList<string> permissions)
        => permissions.Count == 0
            ? [FileStorageContainerTypeAppPermission.None]
            : permissions.Select(p => (FileStorageContainerTypeAppPermission?)Enum.Parse<FileStorageContainerTypeAppPermission>(p, ignoreCase: true))
                .ToList();

    private static string Describe(IEnumerable<FileStorageContainerTypeAppPermission?>? permissions)
        => string.Join(",", (permissions ?? []).Select(p => p?.ToString() ?? "?"));

    /// <summary>Set comparison; <c>none</c> and an empty list are the same grant.</summary>
    private static bool SamePermissions(IEnumerable<FileStorageContainerTypeAppPermission?>? actual, IReadOnlyList<string> desired)
    {
        static HashSet<string> Normalise(IEnumerable<string> names) => names
            .Where(n => !string.Equals(n, nameof(FileStorageContainerTypeAppPermission.None), StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var actualNames = (actual ?? []).Where(p => p.HasValue).Select(p => p!.Value.ToString());
        return Normalise(actualNames).SetEquals(Normalise(desired));
    }

    private SpeContainerTypeGrantOutcome GrantFailure(string appId, string verb, string containerTypeId, ODataError ex)
    {
        _logger.LogError(ex, "H8 container-type grant {Verb} ODataError: appId={AppId} status={Status}", verb, appId, ex.ResponseStatusCode);
        return new SpeContainerTypeGrantOutcome.Failure(appId,
            $"Graph {verb} /storage/fileStorage/containerTypeRegistrations/{containerTypeId}/applicationPermissionGrants/{appId} " +
            $"failed with HTTP {ex.ResponseStatusCode}: {ex.Error?.Code} {ex.Error?.Message ?? ex.Message}.");
    }

    private CancellationTokenSource LinkedTimeout(CancellationToken ct)
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(_options.GraphRequestTimeout);
        return cts;
    }
}
