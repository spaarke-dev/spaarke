using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// Metadata-derived implementation of <see cref="ISecurableEntityRegistry"/>: one
/// <see cref="RetrieveMetadataChangesRequest"/> filtered to the <c>sprk_issecure</c> attribute, projecting
/// logical names only.
///
/// <para><b>Why a metadata-changes query rather than per-entity retrieves.</b> The question is "which
/// entities carry this attribute?", which is an attribute-first query. Answering it with
/// <c>RetrieveEntityRequest</c> would require a candidate list of entities to interrogate — i.e. exactly the
/// hard-coded list the task forbids — or interrogating every entity in the org. The attribute-filtered
/// metadata query asks the question directly in one round trip.</para>
///
/// <para><b>Caching (ADR-009).</b> The projected name set is cached for 6h in the shared
/// <see cref="IDistributedCache"/> under <c>sdap:dv:securable-entities</c>, mirroring
/// <c>Services.Dataverse.MetadataService</c>. The key is allow-listed as
/// <see cref="Infrastructure.Cache.SystemCacheKeys.DataverseSecurableEntities"/> with its
/// SYSTEM-LEVEL EXCEPTION (NFR-08) justification: this is org-wide SCHEMA (which entities can be marked
/// secure), identical for every caller, so tenant-scoping would defeat the cache without changing any
/// answer. Cache <i>failures</i> are graceful — an unreachable Redis falls through to a live query, matching
/// the MetadataService precedent — but metadata failures are NOT: they propagate, per the interface
/// contract. The 6h staleness window means a newly-added securable entity is picked up within 6h of a
/// solution import; the same window the metadata endpoint already accepts.</para>
///
/// <para><b>An empty result is never cached.</b> An empty set is indistinguishable from a failed query or an
/// under-privileged identity, and caching it would make every record read as non-secure — resolving to a
/// shared container — for the full TTL. See <see cref="GetSecurableEntitiesAsync"/>.</para>
/// </summary>
public sealed class SecurableEntityRegistry : ISecurableEntityRegistry
{
    /// <summary>The attribute whose presence makes an entity securable.</summary>
    public const string SecureFlagAttribute = "sprk_issecure";

    // SYSTEM-LEVEL EXCEPTION (NFR-08): org-wide schema, not per-tenant data — allow-listed as
    // SystemCacheKeys.DataverseSecurableEntities. See the class remarks for the justification.
    internal const string CacheKey = "sdap:dv:" + Infrastructure.Cache.SystemCacheKeys.DataverseSecurableEntities;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private readonly IDataverseService _dataverseService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SecurableEntityRegistry> _logger;

    public SecurableEntityRegistry(
        IDataverseService dataverseService,
        IDistributedCache cache,
        ILogger<SecurableEntityRegistry> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<bool> IsSecurableAsync(string entityLogicalName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return false;
        }

        var securable = await GetSecurableEntitiesAsync(ct).ConfigureAwait(false);
        return securable.Contains(entityLogicalName.Trim().ToLowerInvariant());
    }

    public async Task<IReadOnlySet<string>> GetSecurableEntitiesAsync(CancellationToken ct = default)
    {
        var cached = await TryGetFromCacheAsync(ct).ConfigureAwait(false);
        if (cached is not null)
        {
            return cached;
        }

        // NOT wrapped in a try/catch that returns an empty set. A metadata failure must propagate: an
        // empty set would silently classify every record as non-secure, and that is the isolation failure
        // this whole wave exists to remove.
        var names = await QueryMetadataAsync(ct).ConfigureAwait(false);

        if (names.Count == 0)
        {
            // Legitimate in an org where the field has never been added — but it is ALSO what a broken query
            // or an under-privileged identity looks like, and the consequence is that every record reads as
            // non-secure. Under the build plan's rule 1 ("any error, null, or missing config denies"), an
            // answer that cannot be distinguished from a failure MUST NOT be persisted: caching it would
            // extend a possible metadata fault into a 6-hour window during which every upload silently
            // resolves to a shared container. So this returns the empty set WITHOUT caching it — callers on
            // the isolation path (CommunicationContainerResolver) refuse on it — and it is logged at Error
            // rather than Warning. The live end-to-end assertion is task 047.
            _logger.LogError(
                "[SECURABLE-ENTITIES] Dataverse metadata reports NO entity carrying '{Attribute}'. This is "
                + "expected ONLY in an environment where secure records do not exist; otherwise it indicates "
                + "a failed metadata query or an under-privileged identity. NOT CACHED — the next call "
                + "re-queries, so a transient fault cannot become a 6-hour isolation gap.",
                SecureFlagAttribute);

            return names;
        }

        _logger.LogInformation(
            "[SECURABLE-ENTITIES] Derived {Count} securable entity/entities from live metadata: {Entities}",
            names.Count, string.Join(", ", names.OrderBy(n => n, StringComparer.Ordinal)));

        await TrySetInCacheAsync(names, ct).ConfigureAwait(false);

        return names;
    }

    private async Task<IReadOnlySet<string>> QueryMetadataAsync(CancellationToken ct)
    {
        var serviceClient = _dataverseService.UnwrapServiceClient(nameof(SecurableEntityRegistry));

        var query = new EntityQueryExpression
        {
            // Only the entity's logical name and the (filtered) attribute collection — keeps the payload
            // small even across a full-org enumeration.
            Properties = new MetadataPropertiesExpression("LogicalName", "Attributes"),
            AttributeQuery = new AttributeQueryExpression
            {
                Criteria = new MetadataFilterExpression(Microsoft.Xrm.Sdk.Query.LogicalOperator.And)
                {
                    Conditions =
                    {
                        new MetadataConditionExpression(
                            "LogicalName", MetadataConditionOperator.Equals, SecureFlagAttribute)
                    }
                },
                Properties = new MetadataPropertiesExpression("LogicalName")
            }
        };

        var request = new RetrieveMetadataChangesRequest
        {
            Query = query,
            // No client version stamp: always a full answer. An incremental stamp would return only the
            // DELTA since a previous call, which for a fail-closed security list is the wrong default —
            // an empty delta is indistinguishable from an empty world.
            ClientVersionStamp = null,
            DeletedMetadataFilters = DeletedMetadataFilters.All
        };

        // ServiceClient.Execute is synchronous; wrap with the cancellation token, matching
        // MetadataService.FetchEntityMetadataAsync.
        var response = (RetrieveMetadataChangesResponse)await Task
            .Run(() => serviceClient.Execute(request), ct)
            .ConfigureAwait(false);

        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in response.EntityMetadata ?? [])
        {
            // The entity query is unfiltered, so entities WITHOUT the attribute come back with an empty
            // attribute collection. Presence of the attribute is the signal.
            if (entity?.LogicalName is null || entity.Attributes is null || entity.Attributes.Length == 0)
            {
                continue;
            }

            var carriesFlag = entity.Attributes.Any(a =>
                string.Equals(a?.LogicalName, SecureFlagAttribute, StringComparison.OrdinalIgnoreCase));

            if (carriesFlag)
            {
                names.Add(entity.LogicalName.ToLowerInvariant());
            }
        }

        return names;
    }

    private async Task<IReadOnlySet<string>?> TryGetFromCacheAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetAsync(CacheKey, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            var names = JsonSerializer.Deserialize<string[]>(Encoding.UTF8.GetString(bytes));
            return names is null ? null : new HashSet<string>(names, StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            // Graceful: a cache miss-shaped failure just means a live query. Never fail the request on it.
            _logger.LogWarning(ex,
                "[SECURABLE-ENTITIES] Cache read failed; falling back to a live metadata query.");
            return null;
        }
    }

    private async Task TrySetInCacheAsync(IReadOnlySet<string> names, CancellationToken ct)
    {
        try
        {
            var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(names.ToArray()));
            await _cache.SetAsync(
                CacheKey,
                bytes,
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[SECURABLE-ENTITIES] Cache write failed; the next call will re-query metadata.");
        }
    }
}
