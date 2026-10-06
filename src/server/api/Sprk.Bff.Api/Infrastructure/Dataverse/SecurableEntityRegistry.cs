using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Metadata.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.Dataverse;

/// <summary>
/// Metadata-derived implementation of <see cref="ISecurableEntityRegistry"/>: one
/// <see cref="RetrieveMetadataChangesRequest"/> whose ATTRIBUTE query is filtered to <c>sprk_issecure</c>,
/// projecting logical names only.
///
/// <para><b>Why a metadata-changes query rather than per-entity retrieves.</b> The question is "which
/// entities carry this attribute?", which is an attribute-first query. Answering it with
/// <c>RetrieveEntityRequest</c> would require a candidate list of entities to interrogate — i.e. exactly the
/// hard-coded list the task forbids — or interrogating every entity in the org. The attribute-filtered
/// metadata query asks the question directly in one round trip.</para>
///
/// <para><b>One query, two answers (task 151, #1038).</b> The ENTITY query is unfiltered, so the response
/// enumerates every entity in the org — those without <c>sprk_issecure</c> come back with an empty attribute
/// collection. Until task 151 the entity names were read and thrown away; they are now retained as the
/// KNOWN-ENTITY set, which is what lets <see cref="RecordContainerResolver"/> refuse a name that is not an
/// entity at all instead of reading it as "a real entity that cannot be secure". No second query, no second
/// metadata service, no hard-coded list. The premise "the unfiltered entity query returns every entity" is
/// proved by the task 151 manual live gate, because no unit test can prove a Dataverse behaviour. Both answers
/// are served by ONE call (<see cref="ClassifyEntityAsync"/>) reading ONE catalog value, so a cache miss costs
/// one metadata round trip per question, not one per set.</para>
///
/// <para><b>Caching (ADR-009).</b> Both sets are cached together for 6h in the shared
/// <see cref="IDistributedCache"/> under <see cref="CacheKey"/>, mirroring
/// <c>Services.Dataverse.MetadataService</c>. The key is allow-listed as
/// <see cref="Infrastructure.Cache.SystemCacheKeys.DataverseSecurableEntities"/> with its
/// SYSTEM-LEVEL EXCEPTION (NFR-08) justification: this is org-wide SCHEMA (which entities exist and which can
/// be marked secure), identical for every caller, so tenant-scoping would defeat the cache without changing
/// any answer. Cache <i>failures</i> are graceful — an unreachable Redis falls through to a live query,
/// matching the MetadataService precedent — but metadata failures are NOT: they propagate, per the interface
/// contract. The 6h staleness window means a newly-added entity is picked up within 6h of a solution import;
/// the same window the metadata endpoint already accepts.</para>
///
/// <para><b>The key is VERSIONED (<c>:v2</c>), and the value is shape-checked on read.</b> The previous build
/// wrote a bare JSON array of SECURABLE names to the unversioned key. Read as the known-entity set, that
/// value would make every non-securable entity "unknown" — refusing every ordinary upload for up to 6h after
/// deploy — and read the other way it would be a securable list with nothing to check names against. The new
/// key means the old value is never even fetched; the shape check (format version, both sets non-empty,
/// securable ⊆ known) means a value that is not exactly this format is treated as a miss and re-queried,
/// never interpreted.</para>
///
/// <para><b>An empty result is never cached.</b> An empty set is indistinguishable from a failed query or an
/// under-privileged identity, and caching it would make every record read as non-secure — resolving to a
/// shared container — for the full TTL. See <see cref="GetCatalogAsync"/>.</para>
/// </summary>
public sealed class SecurableEntityRegistry : ISecurableEntityRegistry
{
    /// <summary>
    /// The attribute whose presence makes an entity securable — except on the entities in
    /// <see cref="FlagIsNotASecurityInput"/>.
    /// </summary>
    public const string SecureFlagAttribute = "sprk_issecure";

    /// <summary>
    /// Entities that CARRY <see cref="SecureFlagAttribute"/> but whose value is NOT a security input, so they are
    /// never securable (unified-access-control-r2 task 150; owner round 10 item 11, 2026-10-03: "invoices follow
    /// their matter").
    /// </summary>
    /// <remarks>
    /// <para><b><c>sprk_invoice</c>.</b> Live dev metadata reports the column on the invoice table (the task 151 live
    /// gate) and, until task 150, that made an invoice a securable entity in its own right: an invoice flagged
    /// <c>true</c> under an ORDINARY matter refused every upload (no container of its own), and one flagged
    /// <c>false</c> was read for its flag before its matter was. The owner's rule is that an invoice is secure exactly
    /// when the record it is filed under is: <see cref="RecordContainerResolver"/>'s ancestor walk decides, through
    /// the invoice's typed matter / project lookups (<c>RecordContainerResolver.ChildAncestorLinks</c>). The entity
    /// stays KNOWN (<see cref="EntitySecurability.NotSecurable"/>), so its row is still read for those links. The
    /// column itself is field-secured like the three roots' (<c>scripts/Set-SecureFlagFieldSecurity.ps1</c>), so no
    /// user can change a value nothing reads any more.</para>
    ///
    /// <para><b>Why a named exception in a metadata-derived set.</b> The set is derived so a NEW carrier of the column
    /// is secured without a code change (fail closed). This list is the opposite direction and therefore only ever
    /// holds an entity the OWNER has ruled is not secured by its own flag; adding one is an owner decision, not a
    /// cleanup. It is applied to the live answer AND to a cached value, so a catalog cached by a build from before this
    /// rule (6h TTL) is not read with the invoice still securable.</para>
    /// </remarks>
    internal static readonly IReadOnlySet<string> FlagIsNotASecurityInput =
        new HashSet<string>(StringComparer.Ordinal) { "sprk_invoice" };

    /// <summary>
    /// The cached value's format version. Bump it AND the <see cref="CacheKey"/> suffix together whenever
    /// the cached shape changes, so a value written by a previous build is never read as the current one.
    /// </summary>
    private const int CacheFormatVersion = 2;

    // SYSTEM-LEVEL EXCEPTION (NFR-08): org-wide schema, not per-tenant data — allow-listed as
    // SystemCacheKeys.DataverseSecurableEntities. See the class remarks for the justification.
    //
    // ":v2" (task 151): the unversioned key holds the previous build's bare securable-names array, which
    // must never be read as the known-entity set. Changing the key is what guarantees that across a deploy.
    internal const string CacheKey =
        "sdap:dv:" + Infrastructure.Cache.SystemCacheKeys.DataverseSecurableEntities + ":v2";

    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(6);

    private readonly Func<CancellationToken, Task<IReadOnlyCollection<EntityMetadata>>> _fetchEntityMetadata;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SecurableEntityRegistry> _logger;

    /// <summary>The valid catalog already obtained in this scope, if any. See <see cref="GetCatalogAsync"/>.</summary>
    private EntityCatalog? _scopeCatalog;

    public SecurableEntityRegistry(
        IDataverseService dataverseService,
        IDistributedCache cache,
        ILogger<SecurableEntityRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(dataverseService);
        _fetchEntityMetadata = ct => FetchEntityMetadataAsync(dataverseService, ct);
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Test seam: substitutes ONLY the metadata round trip (a <c>ServiceClient</c> cannot be stood up in a
    /// test host), so the catalog building, the empty-never-cached rule and the cache format/versioning all
    /// run as production code. Internal — DI only sees the public constructor.
    /// </summary>
    internal SecurableEntityRegistry(
        Func<CancellationToken, Task<IReadOnlyCollection<EntityMetadata>>> fetchEntityMetadata,
        IDistributedCache cache,
        ILogger<SecurableEntityRegistry> logger)
    {
        _fetchEntityMetadata = fetchEntityMetadata ?? throw new ArgumentNullException(nameof(fetchEntityMetadata));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<EntitySecurability> ClassifyEntityAsync(string entityLogicalName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return EntitySecurability.NotAnEntity;
        }

        var name = entityLogicalName.Trim().ToLowerInvariant();

        // The ONE catalog lookup for this question (task 151 review). Both answers below come from this single
        // value, so a cache miss costs at most ONE metadata round trip per classification. Do not split this
        // into two lookups — that is exactly the defect this method replaced (an IsSecurableAsync +
        // IsKnownEntityAsync pair that each fetched, doubling the full-org metadata cost of every ordinary
        // non-securable upload whenever Redis was unavailable).
        var catalog = await GetCatalogAsync(ct).ConfigureAwait(false);

        // Securable ⊆ known (both sets come from one query), so a securable hit needs no known-set check.
        if (catalog.Securable.Contains(name))
        {
            return EntitySecurability.Securable;
        }

        if (catalog.Known.Count == 0)
        {
            // Every org has entities (systemuser, businessunit, contact, …), so "none" is not an answer — it is
            // what a broken metadata query or an under-privileged identity looks like. Answering NotAnEntity here
            // would report a metadata fault to the caller as "you named something that is not an entity" (a
            // 400 the client cannot fix), so this THROWS, matching the interface's fail-closed contract.
            throw new InvalidOperationException(
                "Dataverse metadata reported no entities at all, so it cannot be determined whether "
                + "the requested entity exists. Refusing rather than answering.");
        }

        return catalog.Known.Contains(name) ? EntitySecurability.NotSecurable : EntitySecurability.NotAnEntity;
    }

    public async Task<IReadOnlySet<string>> GetSecurableEntitiesAsync(CancellationToken ct = default)
        => (await GetCatalogAsync(ct).ConfigureAwait(false)).Securable;

    /// <summary>
    /// Both answers from ONE metadata round trip: every entity logical name in the org, and the subset
    /// carrying <see cref="SecureFlagAttribute"/>.
    /// </summary>
    private async Task<EntityCatalog> GetCatalogAsync(CancellationToken ct)
    {
        // SCOPE MEMO (task 155). Registered Scoped, so this lives for one request / one job scope. A child record's
        // resolution asks a SECOND question (is its root's entity securable?) after the record's own
        // classification; without the memo that second question is another full-org metadata round trip whenever
        // the distributed cache is down — the very cost the task 151 review removed. Only a VALID catalog is
        // memoized (the same rule as the distributed cache), so an empty answer still re-queries on the next call.
        if (_scopeCatalog is not null)
        {
            return _scopeCatalog;
        }

        var cached = await TryGetFromCacheAsync(ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _scopeCatalog = cached;
            return cached;
        }

        // NOT wrapped in a try/catch that returns an empty set. A metadata failure must propagate: an
        // empty set would silently classify every record as non-secure, and that is the isolation failure
        // this whole wave exists to remove.
        var catalog = BuildCatalog(await _fetchEntityMetadata(ct).ConfigureAwait(false));

        if (catalog.Known.Count == 0 || catalog.Securable.Count == 0)
        {
            // Legitimate in an org where the field has never been added — but it is ALSO what a broken query
            // or an under-privileged identity looks like, and the consequence is that every record reads as
            // non-secure. Under the build plan's rule 1 ("any error, null, or missing config denies"), an
            // answer that cannot be distinguished from a failure MUST NOT be persisted: caching it would
            // extend a possible metadata fault into a 6-hour window during which every upload silently
            // resolves to a shared container. So this returns the answer WITHOUT caching it — callers on
            // the isolation path (CommunicationContainerResolver) refuse on an empty securable set, and
            // ClassifyEntityAsync throws on an empty known set — and it is logged at Error rather than
            // Warning. The live end-to-end assertion is task 047.
            _logger.LogError(
                "[SECURABLE-ENTITIES] Dataverse metadata reports {KnownCount} entity/entities and {SecurableCount} "
                + "carrying '{Attribute}'. An empty answer is expected ONLY in an environment where secure "
                + "records do not exist; otherwise it indicates a failed metadata query or an under-privileged "
                + "identity. NOT CACHED — the next call re-queries, so a transient fault cannot become a "
                + "6-hour isolation gap.",
                catalog.Known.Count, catalog.Securable.Count, SecureFlagAttribute);

            return catalog;
        }

        _logger.LogInformation(
            "[SECURABLE-ENTITIES] Derived {Count} securable entity/entities out of {KnownCount} known entities "
            + "from live metadata: {Entities}",
            catalog.Securable.Count,
            catalog.Known.Count,
            string.Join(", ", catalog.Securable.OrderBy(n => n, StringComparer.Ordinal)));

        await TrySetInCacheAsync(catalog, ct).ConfigureAwait(false);

        _scopeCatalog = catalog;
        return catalog;
    }

    private static async Task<IReadOnlyCollection<EntityMetadata>> FetchEntityMetadataAsync(
        IDataverseService dataverseService,
        CancellationToken ct)
    {
        var serviceClient = dataverseService.UnwrapServiceClient(nameof(SecurableEntityRegistry));

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

        return response.EntityMetadata is { } entities ? entities : [];
    }

    /// <summary>
    /// Project the metadata response into the two name sets. Every entity the (unfiltered) entity query
    /// returned is KNOWN; those whose (attribute-filtered) attribute collection carries
    /// <see cref="SecureFlagAttribute"/> are also SECURABLE.
    /// </summary>
    private static EntityCatalog BuildCatalog(IReadOnlyCollection<EntityMetadata> entities)
    {
        var known = new HashSet<string>(StringComparer.Ordinal);
        var securable = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entity in entities)
        {
            if (string.IsNullOrWhiteSpace(entity?.LogicalName))
            {
                continue;
            }

            var logicalName = entity.LogicalName.ToLowerInvariant();

            // The entity query is unfiltered, so EVERY entity comes back — this is the known-entity set
            // (task 151). Recorded before the attribute check, which only decides securability.
            known.Add(logicalName);

            // Entities WITHOUT the attribute come back with an empty attribute collection. Presence of the
            // attribute is the securability signal — except where the owner ruled its value is not a security input
            // (FlagIsNotASecurityInput, task 150: an invoice follows its matter).
            if (entity.Attributes is null || entity.Attributes.Length == 0)
            {
                continue;
            }

            var carriesFlag = entity.Attributes.Any(a =>
                string.Equals(a?.LogicalName, SecureFlagAttribute, StringComparison.OrdinalIgnoreCase));

            if (carriesFlag && !FlagIsNotASecurityInput.Contains(logicalName))
            {
                securable.Add(logicalName);
            }
        }

        return new EntityCatalog(known, securable);
    }

    private async Task<EntityCatalog?> TryGetFromCacheAsync(CancellationToken ct)
    {
        try
        {
            var bytes = await _cache.GetAsync(CacheKey, ct).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
            {
                return null;
            }

            var catalog = TryParseCacheEntry(bytes);

            if (catalog is null)
            {
                // Not this build's format (or an internally inconsistent value). Re-query rather than guess
                // what it means: a mis-read known-entity set either refuses every ordinary upload or stops
                // refusing names that are not entities.
                _logger.LogWarning(
                    "[SECURABLE-ENTITIES] Cached entry at '{CacheKey}' is not a valid format-v{Version} entity "
                    + "catalog; ignoring it and re-querying metadata.",
                    CacheKey, CacheFormatVersion);
            }

            return catalog;
        }
        catch (Exception ex)
        {
            // Graceful: a cache miss-shaped failure just means a live query. Never fail the request on it.
            _logger.LogWarning(ex,
                "[SECURABLE-ENTITIES] Cache read failed; falling back to a live metadata query.");
            return null;
        }
    }

    private async Task TrySetInCacheAsync(EntityCatalog catalog, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(
                CacheKey,
                SerializeCacheEntry(catalog.Known, catalog.Securable),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[SECURABLE-ENTITIES] Cache write failed; the next call will re-query metadata.");
        }
    }

    /// <summary>
    /// The cached value's wire form. <c>internal</c> so test hosts that seed this cache (rather than run a
    /// live metadata query) write THIS format through THIS code — a test that hand-builds the JSON is a
    /// second copy of the format that would silently keep seeding the old shape.
    /// </summary>
    internal static byte[] SerializeCacheEntry(
        IEnumerable<string> knownEntities,
        IEnumerable<string> securableEntities)
        => JsonSerializer.SerializeToUtf8Bytes(new CacheEntry(
            CacheFormatVersion,
            knownEntities.Select(n => n.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray(),
            securableEntities.Select(n => n.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToArray()));

    /// <summary>
    /// Parse a cached value, or return <see langword="null"/> when it is not EXACTLY a valid current-format
    /// catalog. Validity: a JSON object (the previous build wrote a bare array), the current format version,
    /// both sets non-empty (an empty set is never written, so one on read is not ours), and every securable
    /// name also known (both come from one query, so a securable name missing from the known set means the
    /// value was not written by this code).
    /// </summary>
    internal static EntityCatalog? TryParseCacheEntry(byte[] bytes)
    {
        CacheEntry? entry;
        try
        {
            entry = JsonSerializer.Deserialize<CacheEntry>(bytes);
        }
        catch (JsonException)
        {
            return null;
        }

        if (entry?.Known is null || entry.Securable is null || entry.FormatVersion != CacheFormatVersion)
        {
            return null;
        }

        var known = new HashSet<string>(entry.Known.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);
        var securable = new HashSet<string>(entry.Securable.Where(n => !string.IsNullOrWhiteSpace(n)), StringComparer.Ordinal);

        // Task 150: a value cached before the owner's invoice rule (or by an older build still running beside this one)
        // lists sprk_invoice as securable. The rule applies to it too, rather than waiting out the TTL.
        securable.ExceptWith(FlagIsNotASecurityInput);

        // A non-empty securable set that is a subset of the known set implies the known set is non-empty too,
        // so these two conditions cover all three rules.
        if (securable.Count == 0 || !securable.IsSubsetOf(known))
        {
            return null;
        }

        return new EntityCatalog(known, securable);
    }

    /// <summary>The org's entity logical names, and the subset carrying <see cref="SecureFlagAttribute"/>.</summary>
    internal sealed record EntityCatalog(IReadOnlySet<string> Known, IReadOnlySet<string> Securable);

    private sealed record CacheEntry(
        [property: JsonPropertyName("v")] int FormatVersion,
        [property: JsonPropertyName("known")] string[]? Known,
        [property: JsonPropertyName("securable")] string[]? Securable);
}
