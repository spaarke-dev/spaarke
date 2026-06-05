using System.Diagnostics;
using System.Text.Json;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Dataverse.Privileges;

/// <summary>
/// Caches the Dataverse <c>RetrieveUserPrivileges</c> result for a calling user.
/// </summary>
/// <remarks>
/// <para>
/// Per task 010 design (Q1 resolution, recorded 2026-06-01):
/// </para>
/// <list type="bullet">
///   <item><description>Uses app-only <see cref="ServiceClient"/> exposed via <see cref="DataverseServiceClientImpl.OrganizationService"/>.</description></item>
///   <item><description>Sets <see cref="ServiceClient.CallerId"/> to impersonate the caller (after looking up systemuserid from the Azure AD <c>oid</c>).</description></item>
///   <item><description>Calls <see cref="RetrieveUserPrivilegesRequest"/> to fetch the user's role-privilege set in a single round-trip, then resolves each <c>PrivilegeId</c> to its entity logical name via a batched FetchXML against the <c>privilege</c> entity.</description></item>
///   <item><description>Caches the projected set of Read-allowed entity logical names per user with a 6-hour sliding TTL and 24-hour absolute maximum.</description></item>
///   <item><description>Fails closed on Dataverse errors (returns empty set) and logs a warning. Cache failures are graceful.</description></item>
/// </list>
/// <para>
/// Cache backend: shared <see cref="IDistributedCache"/> (Redis in production), matching the
/// <c>GraphMetadataCache</c> precedent. Key shape:
/// <c>sdap:dv:privilege:{userOid}:set</c>. Value: JSON-serialised <see cref="HashSet{T}"/> of strings.
/// </para>
/// </remarks>
internal sealed class UserPrivilegeChecker : IDataversePrivilegeChecker
{
    private readonly IDataverseService _dataverseService;
    private readonly IDistributedCache _cache;
    private readonly ILogger<UserPrivilegeChecker> _logger;

    // Per task 010 §6: 6-hour sliding TTL, 24-hour absolute maximum.
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(6);
    private static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromHours(24);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public UserPrivilegeChecker(
        IDataverseService dataverseService,
        IDistributedCache cache,
        ILogger<UserPrivilegeChecker> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> HasReadPrivilegeAsync(Guid userOid, string entityLogicalName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entityLogicalName))
        {
            return false;
        }

        var readable = await GetReadableEntitiesAsync(userOid, ct);
        return readable.Contains(entityLogicalName);
    }

    /// <inheritdoc />
    public async Task<IReadOnlySet<string>> GetReadableEntitiesAsync(Guid userOid, CancellationToken ct)
    {
        if (userOid == Guid.Empty)
        {
            _logger.LogWarning("GetReadableEntitiesAsync called with empty userOid; failing closed");
            return EmptySet();
        }

        var cacheKey = $"sdap:dv:privilege:{userOid:D}:set";

        // Cache read (graceful — failures fall through to Dataverse).
        var cached = await TryGetFromCacheAsync(cacheKey, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Privilege cache HIT for user {UserOid} ({Count} readable entities)", userOid, cached.Count);
            return cached;
        }

        _logger.LogDebug("Privilege cache MISS for user {UserOid}; fetching from Dataverse", userOid);

        var fetched = await FetchReadablePrivilegesAsync(userOid, ct);

        // Cache write (graceful — failures don't break the request).
        await TrySetInCacheAsync(cacheKey, fetched, ct);

        return fetched;
    }

    /// <summary>
    /// Fetches the user's effective Read-privilege entity set from Dataverse.
    /// Returns an empty set on any error (fail-closed).
    /// </summary>
    private async Task<IReadOnlySet<string>> FetchReadablePrivilegesAsync(Guid userOid, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Resolve the ServiceClient from the composite IDataverseService.
        if (_dataverseService is not DataverseServiceClientImpl impl)
        {
            _logger.LogError(
                "FetchReadablePrivilegesAsync: IDataverseService is not DataverseServiceClientImpl (actual: {Type}); cannot access ServiceClient",
                _dataverseService.GetType().FullName);
            return EmptySet();
        }

        var serviceClient = impl.OrganizationService;
        if (serviceClient is null || !serviceClient.IsReady)
        {
            _logger.LogError("FetchReadablePrivilegesAsync: ServiceClient is not ready");
            return EmptySet();
        }

        // Step 1: Map Azure AD oid → Dataverse systemuserid.
        var systemUserId = await LookupSystemUserIdAsync(serviceClient, userOid, ct);
        if (systemUserId == Guid.Empty)
        {
            _logger.LogWarning(
                "Could not map Azure AD oid {UserOid} to a Dataverse systemuserid; failing closed",
                userOid);
            return EmptySet();
        }

        // Step 2: Clone ServiceClient to safely impersonate without mutating the singleton CallerId.
        ServiceClient? impersonated = null;
        try
        {
            impersonated = serviceClient.Clone();
            impersonated.CallerId = systemUserId;

            // Step 2a: Retrieve role-privileges for the user.
            var request = new RetrieveUserPrivilegesRequest { UserId = systemUserId };
            var response = (RetrieveUserPrivilegesResponse)await impersonated.ExecuteAsync(request, ct);

            // The response contains a flat list of RolePrivilege entries. Each carries a PrivilegeId
            // (Guid) but NOT the privilege Name or entity logical name. We resolve these via a
            // single batched query against the `privilege` entity below.
            var privilegeIds = response.RolePrivileges
                .Select(rp => rp.PrivilegeId)
                .Where(id => id != Guid.Empty)
                .Distinct()
                .ToList();

            if (privilegeIds.Count == 0)
            {
                _logger.LogWarning(
                    "User {UserOid} (systemuserid={SystemUserId}) has zero role-privileges per Dataverse",
                    userOid, systemUserId);
                return EmptySet();
            }

            // Step 2b: Resolve privilege IDs → entity logical names where the privilege is a Read privilege.
            var readableEntities = await ResolveReadEntitiesAsync(impersonated, privilegeIds, ct);

            sw.Stop();
            _logger.LogInformation(
                "Fetched privilege set for user {UserOid} (systemuserid={SystemUserId}): {ReadableCount} entities with Read in {ElapsedMs}ms",
                userOid, systemUserId, readableEntities.Count, sw.ElapsedMilliseconds);

            return new HashSet<string>(readableEntities, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogError(
                ex,
                "Failed to fetch user privileges for user {UserOid} in {ElapsedMs}ms; failing closed (returning empty set)",
                userOid, sw.ElapsedMilliseconds);
            return EmptySet();
        }
        finally
        {
            impersonated?.Dispose();
        }
    }

    /// <summary>
    /// Looks up the Dataverse systemuserid from the Azure AD object id via a direct ServiceClient query.
    /// Returns <see cref="Guid.Empty"/> if not found.
    /// </summary>
    private async Task<Guid> LookupSystemUserIdAsync(ServiceClient client, Guid userOid, CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid"),
                TopCount = 1,
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression(
                            "azureactivedirectoryobjectid",
                            ConditionOperator.Equal,
                            userOid)
                    }
                }
            };

            var result = await client.RetrieveMultipleAsync(query, ct);
            if (result.Entities.Count == 0)
            {
                return Guid.Empty;
            }

            return result.Entities[0].Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error looking up systemuserid for Azure AD oid {UserOid}", userOid);
            return Guid.Empty;
        }
    }

    /// <summary>
    /// Resolves a batch of privilege IDs to the set of entity logical names that the user has Read on.
    /// </summary>
    /// <remarks>
    /// Queries the <c>privilege</c> entity in a single round-trip. Filters to privileges whose
    /// <c>name</c> begins with <c>prvRead</c> (the canonical Dataverse naming convention for entity
    /// Read privileges). The associated entity logical name is parsed from the trailing portion of
    /// the privilege name (e.g., <c>prvReadAccount</c> → <c>account</c>).
    /// </remarks>
    private async Task<List<string>> ResolveReadEntitiesAsync(
        ServiceClient client,
        List<Guid> privilegeIds,
        CancellationToken ct)
    {
        try
        {
            // Use a paged ConditionOperator.In query against the privilege entity.
            // Dataverse caps IN to 500 values per condition; chunk if necessary.
            var entities = new List<string>();

            foreach (var chunk in Chunk(privilegeIds, 500))
            {
                var query = new QueryExpression("privilege")
                {
                    ColumnSet = new ColumnSet("name", "accessright"),
                    Criteria = new FilterExpression(LogicalOperator.And)
                    {
                        Conditions =
                        {
                            new ConditionExpression("privilegeid", ConditionOperator.In, chunk.Cast<object>().ToArray()),
                            new ConditionExpression("name", ConditionOperator.BeginsWith, "prvRead")
                        }
                    }
                };

                var result = await client.RetrieveMultipleAsync(query, ct);
                foreach (var entity in result.Entities)
                {
                    var name = entity.GetAttributeValue<string>("name");
                    if (string.IsNullOrEmpty(name) || !name.StartsWith("prvRead", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    // Convention: prv{AccessRight}{EntitySchemaName} (e.g., prvReadAccount → account).
                    // The privilege Name uses the schema name (PascalCase); the logical name is the
                    // lower-case form. We lower-case the trailing portion.
                    var schemaName = name.Substring("prvRead".Length);
                    if (string.IsNullOrEmpty(schemaName))
                    {
                        continue;
                    }

                    entities.Add(schemaName.ToLowerInvariant());
                }
            }

            return entities;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error resolving privilege IDs to entity logical names; failing closed");
            return new List<string>();
        }
    }

    private static IEnumerable<List<T>> Chunk<T>(IList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    private async Task<IReadOnlySet<string>?> TryGetFromCacheAsync(string cacheKey, CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetStringAsync(cacheKey, ct);
            if (cached is null)
            {
                return null;
            }

            var deserialised = JsonSerializer.Deserialize<HashSet<string>>(cached, JsonOptions);
            if (deserialised is null)
            {
                return null;
            }

            // Rebuild with case-insensitive comparer (JSON deserialisation drops the comparer).
            return new HashSet<string>(deserialised, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read privilege cache for key {Key}; falling back to Dataverse", cacheKey);
            return null;
        }
    }

    private async Task TrySetInCacheAsync(string cacheKey, IReadOnlySet<string> value, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(value, JsonOptions);
            var options = new DistributedCacheEntryOptions
            {
                SlidingExpiration = SlidingExpiration,
                AbsoluteExpirationRelativeToNow = AbsoluteExpiration
            };

            await _cache.SetStringAsync(cacheKey, json, options, ct);
            _logger.LogDebug(
                "Cached privilege set for key {Key} (sliding={Sliding}, absolute={Absolute})",
                cacheKey, SlidingExpiration, AbsoluteExpiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write privilege cache for key {Key}; continuing without cache", cacheKey);
        }
    }

    private static IReadOnlySet<string> EmptySet() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
