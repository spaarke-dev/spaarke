using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Crm.Sdk.Messages;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;

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
/// Cache backend: <see cref="ITenantCache"/> wrapping Redis (FR-05 / NFR-08).
/// On-wire key shape: <c>tenant:{tenantId}:privileges:{userOid:D}:v1</c>.
/// Value: JSON-serialised <see cref="HashSet{T}"/> of strings.
/// </para>
/// </remarks>
internal sealed class UserPrivilegeChecker : IDataversePrivilegeChecker
{
    /// <summary>
    /// Cache resource label (per ITenantCache contract). On-wire key:
    /// <c>tenant:{tenantId}:privileges:{userOid:D}:v1</c>.
    /// </summary>
    internal const string CacheResource = "privileges";

    /// <summary>Cache schema version per ADR-009.</summary>
    private const int CacheVersion = 1;

    /// <summary>
    /// Task 099b review round 2: the per-TEAM cache resource. On-wire key: <c>tenant:{tenantId}:team-privileges:{teamId:D}:v1</c>.
    /// Value: the team's Read-allowed entity logical names. Only an ANSWER is stored; a fault is never cached.
    /// </summary>
    internal const string TeamCacheResource = "team-privileges";

    /// <summary>
    /// Task 099b review round 2: short, because the answer gates a write the moment an administrator fixes the role (the
    /// Secure Record Owner role gaining <c>prvReadsprk_ExternalRecordAccess</c> is seen within this window).
    /// </summary>
    internal static readonly TimeSpan TeamPrivilegeTtl = TimeSpan.FromMinutes(5);

    private readonly IDataverseService _dataverseService;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<UserPrivilegeChecker> _logger;

    // Task 099b review round 2: how a team's Read set is fetched on a cache miss: RetrieveTeamPrivileges (below) in the host;
    // a test supplies its own through the internal constructor, since a ServiceClient cannot be faked. Throws on a fault.
    private readonly Func<Guid, CancellationToken, Task<IReadOnlySet<string>>> _fetchTeamReadable;

    // Per task 010 §6: 6-hour sliding TTL, 24-hour absolute maximum.
    // ITenantCache supports only absolute TTL; preserve the 24-hour bound.
    private static readonly TimeSpan SlidingExpiration = TimeSpan.FromHours(6);
    private static readonly TimeSpan AbsoluteExpiration = TimeSpan.FromHours(24);

    public UserPrivilegeChecker(
        IDataverseService dataverseService,
        ITenantCache cache,
        ILogger<UserPrivilegeChecker> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpContextAccessor = httpContextAccessor;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fetchTeamReadable = FetchTeamReadablePrivilegesAsync;
    }

    /// <summary>Test seam (task 099b review round 2): the team fetch is given; everything else is the host's code.</summary>
    internal UserPrivilegeChecker(
        IDataverseService dataverseService,
        ITenantCache cache,
        ILogger<UserPrivilegeChecker> logger,
        Func<Guid, CancellationToken, Task<IReadOnlySet<string>>> fetchTeamReadable,
        IHttpContextAccessor? httpContextAccessor = null)
        : this(dataverseService, cache, logger, httpContextAccessor)
    {
        _fetchTeamReadable = fetchTeamReadable ?? throw new ArgumentNullException(nameof(fetchTeamReadable));
    }

    /// <summary>
    /// Resolves the tenant ID for tenant-scoped cache keys (FR-05).
    /// Reads the AAD <c>tid</c> claim from the current HttpContext per ADR-028;
    /// falls back to <c>"anonymous"</c> when no HttpContext is available.
    /// </summary>
    private string GetTenantId()
        => _httpContextAccessor?.HttpContext?.User?.FindFirst("tid")?.Value
            ?? _httpContextAccessor?.HttpContext?.User?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value
            ?? "anonymous";

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

        var tenantId = GetTenantId();
        var cacheId = userOid.ToString("D");

        // Cache read (graceful — failures fall through to Dataverse).
        var cached = await TryGetFromCacheAsync(tenantId, cacheId, ct);
        if (cached is not null)
        {
            _logger.LogDebug("Privilege cache HIT for user {UserOid} ({Count} readable entities)", userOid, cached.Count);
            return cached;
        }

        _logger.LogDebug("Privilege cache MISS for user {UserOid}; fetching from Dataverse", userOid);

        var fetched = await FetchReadablePrivilegesAsync(userOid, ct);

        // Cache write (graceful — failures don't break the request).
        await TrySetInCacheAsync(tenantId, cacheId, fetched, ct);

        return fetched;
    }

    /// <inheritdoc />
    public async Task<bool> TeamHasReadPrivilegeAsync(Guid teamId, string entityLogicalName, CancellationToken ct)
    {
        if (teamId == Guid.Empty)
            throw new ArgumentException("A team id is required.", nameof(teamId));
        ArgumentException.ThrowIfNullOrWhiteSpace(entityLogicalName);

        var tenantId = GetTenantId();
        var cacheId = teamId.ToString("D");

        IReadOnlySet<string>? readable = null;
        try
        {
            var cached = await _cache.GetAsync<HashSet<string>>(tenantId, TeamCacheResource, cacheId, CacheVersion, ct: ct);
            if (cached is not null)
                readable = new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Failed to read the team privilege cache for team {TeamId}; reading Dataverse", teamId);
        }

        if (readable is null)
        {
            // A fault propagates from here: never answered false, never cached. The caller refuses 503.
            readable = await _fetchTeamReadable(teamId, ct);
            try
            {
                await _cache.SetAsync(
                    tenantId, TeamCacheResource, cacheId, CacheVersion,
                    new HashSet<string>(readable, StringComparer.Ordinal), TeamPrivilegeTtl, ct: ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogWarning(ex, "Failed to write the team privilege cache for team {TeamId}; continuing", teamId);
            }
        }

        return readable.Contains(entityLogicalName);
    }

    /// <summary>
    /// Task 099b review round 2: the entity logical names the TEAM's own roles grant Read on, through
    /// <c>RetrieveTeamPrivileges</c> on the application <see cref="ServiceClient"/> (no impersonation: the team is the
    /// principal asked about). THROWS on any fault, unlike the user path: "could not be read" is not "lacks it".
    /// </summary>
    private async Task<IReadOnlySet<string>> FetchTeamReadablePrivilegesAsync(Guid teamId, CancellationToken ct)
    {
        var serviceClient = _dataverseService.TryUnwrapServiceClient(nameof(FetchTeamReadablePrivilegesAsync), _logger)
            ?? throw new InvalidOperationException(
                "The Dataverse ServiceClient is unavailable, so the team's privileges could not be read.");

        var response = (RetrieveTeamPrivilegesResponse)await serviceClient.ExecuteAsync(
            new RetrieveTeamPrivilegesRequest { TeamId = teamId }, ct);
        var privilegeIds = response.RolePrivileges
            .Select(rp => rp.PrivilegeId)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();

        var readable = privilegeIds.Count == 0
            ? new List<string>()
            : await QueryReadEntitiesAsync(serviceClient, privilegeIds, ct);
        _logger.LogInformation(
            "Fetched privilege set for team {TeamId}: {PrivilegeCount} privileges, {ReadableCount} entities with Read",
            teamId, privilegeIds.Count, readable.Count);
        return new HashSet<string>(readable, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Fetches the user's effective Read-privilege entity set from Dataverse.
    /// Returns an empty set on any error (fail-closed).
    /// </summary>
    private async Task<IReadOnlySet<string>> FetchReadablePrivilegesAsync(Guid userOid, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Resolve the ServiceClient from the composite IDataverseService (fail-closed: any
        // resolution failure logs at Error and returns an empty privilege set).
        var serviceClient = _dataverseService.TryUnwrapServiceClient(nameof(FetchReadablePrivilegesAsync), _logger);
        if (serviceClient is null)
        {
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
                            userOid),
                        // Issue #1576: a disabled systemuser resolves to Guid.Empty (empty privilege set) here,
                        // explicitly, instead of relying on the impersonated read failing.
                        new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
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
            return await QueryReadEntitiesAsync(client, privilegeIds, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error resolving privilege IDs to entity logical names; failing closed");
            return new List<string>();
        }
    }

    /// <summary>
    /// The <see cref="ResolveReadEntitiesAsync"/> query without its fail-closed catch (task 099b review round 2): the team
    /// path needs a fault to propagate. One code path for both, so the <c>prvRead</c> to logical-name rule is not copied.
    /// </summary>
    private static async Task<List<string>> QueryReadEntitiesAsync(
        ServiceClient client,
        List<Guid> privilegeIds,
        CancellationToken ct)
    {
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
    }

    private static IEnumerable<List<T>> Chunk<T>(IList<T> source, int size)
    {
        for (var i = 0; i < source.Count; i += size)
        {
            yield return source.Skip(i).Take(size).ToList();
        }
    }

    private async Task<IReadOnlySet<string>?> TryGetFromCacheAsync(string tenantId, string cacheId, CancellationToken ct)
    {
        try
        {
            var cached = await _cache.GetAsync<HashSet<string>>(
                tenantId,
                CacheResource,
                cacheId,
                CacheVersion,
                ct: ct);
            if (cached is null)
            {
                return null;
            }

            // Rebuild with case-insensitive comparer (JSON deserialisation drops the comparer).
            return new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read privilege cache for tenant={TenantId} id={CacheId}; falling back to Dataverse", tenantId, cacheId);
            return null;
        }
    }

    private async Task TrySetInCacheAsync(string tenantId, string cacheId, IReadOnlySet<string> value, CancellationToken ct)
    {
        try
        {
            // ITenantCache supports only absolute TTL (no SlidingExpiration); preserve
            // the 24-hour absolute bound from task 010 §6. SlidingExpiration retained
            // as a named field for future reference; not enforced by ITenantCache today.
            _ = SlidingExpiration;
            // Serialize as a plain HashSet — case-insensitive comparer is rebuilt on read.
            var payload = new HashSet<string>(value, StringComparer.Ordinal);

            await _cache.SetAsync(
                tenantId,
                CacheResource,
                cacheId,
                CacheVersion,
                payload,
                AbsoluteExpiration,
                ct: ct);
            _logger.LogDebug(
                "Cached privilege set for tenant={TenantId} id={CacheId} (absolute={Absolute})",
                tenantId, cacheId, AbsoluteExpiration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write privilege cache for tenant={TenantId} id={CacheId}; continuing without cache", tenantId, cacheId);
        }
    }

    private static IReadOnlySet<string> EmptySet() => new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
