using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Microsoft.Kiota.Abstractions;
using Microsoft.Kiota.Abstractions.Serialization;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;

namespace Sprk.Bff.Api.Infrastructure.Graph;

/// <summary>
/// The ONLY way BFF code gets an app-only Graph client for SharePoint Embedded work. It refuses any
/// container this stamp does not own, before Graph is called (owner D28 / D29, task 227d).
/// </summary>
/// <remarks>
/// <para><b>Why this exists.</b> Every Model 1 stamp shares one container type, and each stamp's managed
/// identity holds application <c>full</c> on it (T227b). An app-only token reaches EVERY container of the
/// type — Microsoft has no per-container app scoping (researcher 2026-10-06). So the boundary between
/// customers on app-only calls is this class, not Entra. Delegated (OBO) calls are out of scope: Graph
/// isolates them by container membership.</para>
///
/// <para><b>What "own" means.</b> A container is this stamp's when either</para>
/// <list type="bullet">
///   <item>its id is one of the stamp's configured containers, handed in at construction
///   (<see cref="Sprk.Bff.Api.Infrastructure.DI.GraphModule.StampContainerSettingKeys"/>). Those are App
///   Service settings written by provisioning (H4b, from H8) or an operator, never by the customer. They are
///   read by the DI module, not here, so this Graph-layer class originates no container id (the
///   plumbing-layer rule in <c>SpeWriteSinkContainerProvenanceGuardTests</c>); or</item>
///   <item>its custom property <see cref="MarkerPropertyName"/> equals this BFF's <c>Customer:Id</c>.
///   The BFF writes it when it creates a container (<see cref="MarkOwnedAsync"/>) — a stamp also owns
///   one container per secure record.</item>
/// </list>
/// <para>Container ids reach the BFF through Dataverse fields the customer can write
/// (<c>sprk_graphdriveid</c>, <c>sprk_containerid</c>, <c>sprk_specontainerid</c>), so the id alone
/// proves nothing; the container's own marker does. Another customer's container carries that
/// customer's marker, and nothing in this stamp can change it: this class refuses before any write.</para>
///
/// <para><b>What the marker does NOT stop.</b> Any app holding Write or Full on the container type can
/// rewrite a marker — that includes every other stamp's identity. The marker defends against a
/// misrouted or forged container id reaching THIS stamp's code (the D28 threat), not against another
/// stamp's code being compromised.</para>
///
/// <para><b>Fail closed.</b> A blank id, a missing marker, or a 400/403/404 from Graph mean "not owned".
/// Any other Graph failure propagates as <see cref="SpaarkeStorageException"/>, and an unresolved
/// customer identity throws <see cref="InvalidOperationException"/> — no decision is made without an
/// answer. A refusal is <see cref="SdapProblemException"/> 404
/// <c>spe_container_not_owned</c>, never an empty result — the same answer as for a container that does
/// not exist, so a refusal reveals nothing about other customers' containers (tenant-isolation category,
/// <c>tests/integration/tenant/README.md</c>).</para>
///
/// <para><b>Caching.</b> What is cached is the container's marker VALUE (data), never the decision
/// (ADR-009: authorization decisions are not cached); every call compares it with <c>Customer:Id</c>
/// afresh. A marker naming another customer is cached for 60 minutes (it does not change); this stamp's
/// marker, or none, for 10.</para>
///
/// <para><b>One client, many containers.</b> The client handed out is the BFF's single app-only client:
/// nothing in its type ties it to the container that was checked. Every caller passes the same id to the
/// check and to the Graph call that follows, and the ArchTest confines who may obtain a client at all.</para>
///
/// <para><b>Enforced structurally.</b> <c>tests/Spaarke.ArchTests/TenantIsolation/SpeAppOnlyContainerGuardTests.cs</c>
/// fails the build when <c>ForApp()</c> appears outside this class and a fixed list of non-SPE files,
/// when one of those files touches an SPE Graph path, or when <see cref="ForTypeWideOperation"/> is
/// called outside the three facade classes allowed to use it.</para>
///
/// <para><see cref="IsOwnedAsync"/> is virtual only so tests can supply a permissive subclass; no
/// subclass may exist in <c>src/</c> (same ArchTest).</para>
/// </remarks>
public class SpeContainerOwnershipGuard
{
    /// <summary>The container custom property that names the owning customer.</summary>
    public const string MarkerPropertyName = "spaarkeCustomerId";

    /// <summary>Stable error code for a refused container.</summary>
    public const string NotOwnedErrorCode = "spe_container_not_owned";

    private static readonly TimeSpan OwnOrMissingMarkerTtl = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan OtherCustomersMarkerTtl = TimeSpan.FromMinutes(60);

    private readonly IGraphClientFactory _factory;
    private readonly HashSet<string> _stampContainerIds;
    private readonly CustomerIdentity _customer;
    private readonly GraphMetadataCache? _cache;
    private readonly ILogger<SpeContainerOwnershipGuard> _logger;

    /// <param name="factory">The BFF's Graph client factory — the app-only client comes from here.</param>
    /// <param name="stampContainerIds">This stamp's configured containers (blank entries ignored).</param>
    /// <param name="customer">This BFF's customer — the value a container's marker must carry.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="cache">Optional ownership cache (Redis).</param>
    public SpeContainerOwnershipGuard(
        IGraphClientFactory factory,
        IEnumerable<string?> stampContainerIds,
        CustomerIdentity customer,
        ILogger<SpeContainerOwnershipGuard> logger,
        GraphMetadataCache? cache = null)
    {
        _factory = factory ?? throw new ArgumentNullException(nameof(factory));
        ArgumentNullException.ThrowIfNull(stampContainerIds);
        _stampContainerIds = stampContainerIds
            .Select(id => id?.Trim())
            .Where(id => !string.IsNullOrEmpty(id))
            .Select(id => id!)
            .ToHashSet(StringComparer.Ordinal);
        _customer = customer ?? throw new ArgumentNullException(nameof(customer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cache = cache;
    }

    /// <summary>
    /// The app-only Graph client for work on ONE container (or its drive — the ids are the same
    /// <c>b!…</c> string). Throws 404 <c>spe_container_not_owned</c> unless this stamp owns it.
    /// </summary>
    public async Task<GraphServiceClient> ForOwnedContainerAsync(string containerOrDriveId, CancellationToken ct = default)
    {
        await EnsureOwnedAsync(containerOrDriveId, ct).ConfigureAwait(false);
        return _factory.ForApp();
    }

    /// <summary>
    /// The app-only Graph client for work that targets no single container: creating one, listing or
    /// searching across the type, renewing or deleting a subscription this stamp created.
    /// </summary>
    /// <remarks>
    /// Every result that names a container MUST be filtered with <see cref="IsOwnedAsync"/> before it
    /// leaves the facade, and a created container MUST be marked with <see cref="MarkOwnedAsync"/>.
    /// The ArchTest allows this call only in <c>ContainerOperations</c>, <c>SpeAdminGraphService</c>
    /// and <c>SpeFileStore</c>.
    /// </remarks>
    public GraphServiceClient ForTypeWideOperation() => _factory.ForApp();

    /// <summary>Throws 404 <c>spe_container_not_owned</c> unless this stamp owns the container.</summary>
    public async Task EnsureOwnedAsync(string containerOrDriveId, CancellationToken ct = default)
    {
        if (!await IsOwnedAsync(containerOrDriveId, ct).ConfigureAwait(false))
        {
            Refuse(containerOrDriveId);
        }
    }

    /// <summary>
    /// As <see cref="EnsureOwnedAsync"/>, for a container in the deleted-containers bin (restore, permanent
    /// delete). A plain container read answers 404 for those, so the marker is read from the bin.
    /// </summary>
    public async Task EnsureDeletedContainerOwnedAsync(string containerId, CancellationToken ct = default)
    {
        if (!await IsDeletedContainerOwnedAsync(containerId, ct).ConfigureAwait(false))
        {
            Refuse(containerId);
        }
    }

    private void Refuse(string containerOrDriveId)
    {
        _logger.LogWarning(
            "Refused an app-only SharePoint Embedded call on container {ContainerId}: it is not one of this " +
            "stamp's containers (owner D28/D29).",
            containerOrDriveId);

        throw new SdapProblemException(
            NotOwnedErrorCode,
            "Not Found",
            "SharePoint Embedded container not found.",
            StatusCodes.Status404NotFound);
    }

    /// <summary>
    /// Whether this stamp owns the container. Configured stamp containers answer without a Graph call;
    /// otherwise the container's <see cref="MarkerPropertyName"/> is read app-only (its value cached for 10
    /// minutes) and compared with <c>Customer:Id</c>.
    /// </summary>
    public virtual Task<bool> IsOwnedAsync(string containerOrDriveId, CancellationToken ct = default)
        => IsOwnedCoreAsync(containerOrDriveId, deleted: false, ct);

    /// <summary>
    /// <see cref="IsOwnedAsync"/> for a container in the deleted-containers bin: the marker is read from
    /// <c>/storage/fileStorage/deletedContainers/{id}</c>. Cached separately from live containers.
    /// </summary>
    public virtual Task<bool> IsDeletedContainerOwnedAsync(string containerId, CancellationToken ct = default)
        => IsOwnedCoreAsync(containerId, deleted: true, ct);

    private async Task<bool> IsOwnedCoreAsync(string containerOrDriveId, bool deleted, CancellationToken ct)
    {
        var id = containerOrDriveId?.Trim();
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        if (_stampContainerIds.Contains(id))
        {
            return true;
        }

        // Throws when the customer identity is unresolved: no decision without knowing who we are.
        var customerId = _customer.Id;

        string? marker;
        var cached = _cache is null ? null : await _cache.GetContainerMarkerAsync(id, deleted).ConfigureAwait(false);
        if (cached is not null)
        {
            marker = cached.Marker;
        }
        else
        {
            marker = await ReadMarkerAsync(id, deleted, ct).ConfigureAwait(false);
            if (_cache is not null)
            {
                var ttl = marker is not null && !string.Equals(marker, customerId, StringComparison.Ordinal)
                    ? OtherCustomersMarkerTtl
                    : OwnOrMissingMarkerTtl;
                await _cache.SetContainerMarkerAsync(id, deleted, marker, ttl).ConfigureAwait(false);
            }
        }

        var owned = marker is not null && string.Equals(marker, customerId, StringComparison.Ordinal);
        if (!owned)
        {
            _logger.LogInformation(
                "Container {ContainerId} is not this stamp's: marker {Marker} (expected {CustomerId}).",
                id, marker ?? "(none)", customerId);
        }

        return owned;
    }

    /// <summary>
    /// This BFF's customer id — the marker value. Throws when the identity is unresolved. Container creators
    /// call it BEFORE creating, so an unresolvable identity cannot leave an unmarked container behind.
    /// </summary>
    public string RequireCustomerId() => _customer.Id;

    /// <summary>True when <paramref name="markerValue"/> is this stamp's own marker value.</summary>
    public bool IsThisStampsMarker(string? markerValue)
        => markerValue is not null && string.Equals(markerValue.Trim(), _customer.Id, StringComparison.Ordinal);

    /// <summary>Drops the cached marker of a container (after a restore, so a stale "missing" is not served).</summary>
    public async Task ForgetContainerAsync(string containerId)
    {
        if (_cache is not null && !string.IsNullOrWhiteSpace(containerId))
        {
            await _cache.RemoveContainerMarkerAsync(containerId.Trim()).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Writes this stamp's marker on a container the BFF has JUST created. Custom properties cannot be set
    /// in the create call, so this is a PATCH on the <c>customProperties</c> sub-resource (it merges; other
    /// properties are untouched). When Graph refuses it the new container is deleted (best effort) and the
    /// creation fails: an unmarked container would be unreachable through this guard and orphaned.
    /// </summary>
    public async Task MarkOwnedAsync(string containerId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerId);
        containerId = containerId.Trim();
        var customerId = _customer.Id;
        var graph = _factory.ForApp();

        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            [MarkerPropertyName] = new Dictionary<string, object>
            {
                ["value"] = customerId,
                ["isSearchable"] = false,
            },
        });

        var request = new RequestInformation
        {
            HttpMethod = Method.PATCH,
            URI = new Uri($"{BaseUrl(graph)}/storage/fileStorage/containers/{Uri.EscapeDataString(containerId)}/customProperties"),
        };
        request.Headers.Add("Accept", "application/json");
        request.SetStreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)), "application/json");

        try
        {
            await graph.RequestAdapter.SendNoContentAsync(request, ErrorMapping, ct).ConfigureAwait(false);
        }
        catch (ODataError ex)
        {
            var removed = await TryDeleteUnmarkedAsync(graph, containerId).ConfigureAwait(false);
            throw new InvalidOperationException(
                $"Created SharePoint Embedded container '{containerId}' but could not write its ownership marker " +
                $"'{MarkerPropertyName}' (Graph {ex.ResponseStatusCode}: {ex.Error?.Message ?? ex.Message}). " +
                (removed
                    ? "The new container was deleted again."
                    : "Deleting it failed too: it is unreachable from this environment until it carries the marker."),
                ex);
        }

        if (_cache is not null)
        {
            await _cache.SetContainerMarkerAsync(containerId, deleted: false, customerId, OwnOrMissingMarkerTtl).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Marked container {ContainerId} as owned by customer {CustomerId}.", containerId, customerId);
    }

    private async Task<bool> TryDeleteUnmarkedAsync(GraphServiceClient graph, string containerId)
    {
        try
        {
            await graph.Storage.FileStorage.Containers[containerId].DeleteAsync().ConfigureAwait(false);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not delete unmarked container {ContainerId} after marking failed.", containerId);
            return false;
        }
    }

    /// <summary>True when <paramref name="propertyName"/> is the ownership marker (reserved — callers may not edit it).</summary>
    public static bool IsMarkerProperty(string? propertyName)
        => string.Equals(propertyName?.Trim(), MarkerPropertyName, StringComparison.OrdinalIgnoreCase);

    /// <summary>The marker value, or null when the container has none or Graph will not show it (404/403).</summary>
    private async Task<string?> ReadMarkerAsync(string id, bool deleted, CancellationToken ct)
    {
        var graph = _factory.ForApp();
        var select = new[] { "id", "customProperties" };
        Microsoft.Graph.Models.FileStorageContainer? container;
        try
        {
            container = deleted
                ? await graph.Storage.FileStorage.DeletedContainers[id]
                    .GetAsync(r => r.QueryParameters.Select = select, ct)
                    .ConfigureAwait(false)
                : await graph.Storage.FileStorage.Containers[id]
                    .GetAsync(r => r.QueryParameters.Select = select, ct)
                    .ConfigureAwait(false);
        }
        catch (ODataError ex) when (ex.ResponseStatusCode is (int)HttpStatusCode.NotFound
                                        or (int)HttpStatusCode.Forbidden
                                        or (int)HttpStatusCode.BadRequest)
        {
            // 400: not a container id at all (e.g. a malformed or legacy GUID-form value).
            _logger.LogInformation(
                "Ownership read for container {ContainerId} returned {Status}; treating it as not owned.",
                id, ex.ResponseStatusCode);
            return null;
        }
        catch (ODataError ex)
        {
            // No answer (throttled, 5xx): no decision. Translated so no Graph type leaves the facade (ADR-007).
            throw ex.ToSpaarkeStorageException($"ownership read of container {id}");
        }

        var entries = container?.CustomProperties?.AdditionalData;
        if (entries is null)
        {
            return null;
        }

        foreach (var entry in entries)
        {
            if (!IsMarkerProperty(entry.Key))
            {
                continue;
            }

            return entry.Value switch
            {
                UntypedObject obj when obj.GetValue().TryGetValue("value", out var node) && node is UntypedString s
                    => s.GetValue(),
                _ => null,
            };
        }

        return null;
    }

    private static string BaseUrl(GraphServiceClient graph)
    {
        var baseUrl = graph.RequestAdapter?.BaseUrl;
        return string.IsNullOrWhiteSpace(baseUrl) ? "https://graph.microsoft.com/v1.0" : baseUrl.TrimEnd('/');
    }

    private static readonly Dictionary<string, ParsableFactory<IParsable>> ErrorMapping = new()
    {
        { "XXX", ODataError.CreateFromDiscriminatorValue },
    };
}
