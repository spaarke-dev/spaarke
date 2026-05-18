using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Graph;
using Sprk.Bff.Api.Infrastructure.Graph;

namespace Sprk.Bff.Api.Services.Ai.Security;

/// <summary>
/// Resolves Azure AD group memberships for the calling user.
/// Implements a two-strategy resolution chain with a 5-minute per-user memory cache.
/// </summary>
/// <remarks>
/// Strategy 1 — JWT claims: reads the "groups" claim from the bearer token.
///   Valid when the token contains the groups claim and there is no overage indicator
///   (_claim_names / _claim_sources populated by Entra ID when a user belongs to 200+ groups).
///
/// Strategy 2 — Graph fallback: when groups claim is absent or the token is in overage,
///   calls GET /me/memberOf via the OBO Graph client to resolve transitively.
///
/// Cache: IMemoryCache (ADR-009 exception for per-request short-lived data).
///   Key: "privilege-groups:{oid}"  TTL: 5 minutes (absolute).
///   Thread-safety: GetOrCreate is not atomic, but concurrent initialisation of the same
///   entry is benign (both branches are read-only Graph calls with the same expected result).
///
/// Fail-closed: any Graph exception is caught, logged, and an empty list is returned.
///   Callers (RagService) MUST return empty search results when they receive an empty list.
/// </remarks>
public sealed class PrivilegeGroupResolver : IPrivilegeGroupResolver
{
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly IMemoryCache _cache;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<PrivilegeGroupResolver> _logger;

    // Claim type names
    private const string GroupsClaimType = "groups";
    private const string OidClaimType = "oid";
    private const string ObjectIdClaimType = "http://schemas.microsoft.com/identity/claims/objectidentifier";

    // Overage indicator claim populated by Entra ID when user belongs to 200+ groups
    private const string ClaimNamesClaimType = "_claim_names";

    // Cache TTL — short enough to reflect group changes within a workday session
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    // Cache key prefix (tenant-safe: OID is globally unique in Entra ID)
    private const string CacheKeyPrefix = "privilege-groups:";

    public PrivilegeGroupResolver(
        IGraphClientFactory graphClientFactory,
        IMemoryCache cache,
        IHttpContextAccessor httpContextAccessor,
        ILogger<PrivilegeGroupResolver> logger)
    {
        _graphClientFactory = graphClientFactory ?? throw new ArgumentNullException(nameof(graphClientFactory));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ResolveGroupIdsAsync(
        ClaimsPrincipal user,
        CancellationToken ct = default)
    {
        var oid = GetObjectId(user);
        if (string.IsNullOrEmpty(oid))
        {
            _logger.LogWarning("PrivilegeGroupResolver: No OID claim found in principal — returning empty group list (fail-closed)");
            return Array.Empty<string>();
        }

        var cacheKey = CacheKeyPrefix + oid;

        if (_cache.TryGetValue<IReadOnlyList<string>>(cacheKey, out var cached) && cached != null)
        {
            _logger.LogDebug("PrivilegeGroupResolver: cache hit for OID {Oid}, {Count} groups", oid, cached.Count);
            return cached;
        }

        var groups = await ResolveGroupsUncachedAsync(user, oid, ct);

        _cache.Set(cacheKey, groups, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CacheTtl,
            Size = 1  // Required when IMemoryCache is configured with SizeLimit
        });

        _logger.LogDebug("PrivilegeGroupResolver: cached {Count} groups for OID {Oid} (TTL 5 min)", groups.Count, oid);
        return groups;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private async Task<IReadOnlyList<string>> ResolveGroupsUncachedAsync(
        ClaimsPrincipal user,
        string oid,
        CancellationToken ct)
    {
        // Strategy 1: JWT "groups" claim (fast path — no network call needed)
        if (!IsOverage(user) && TryGetGroupsFromClaims(user, out var claimGroups))
        {
            _logger.LogDebug(
                "PrivilegeGroupResolver: resolved {Count} groups from JWT claims for OID {Oid}",
                claimGroups!.Count, oid);
            return claimGroups!;
        }

        // Strategy 2: Graph fallback (overage or groups claim absent)
        _logger.LogInformation(
            "PrivilegeGroupResolver: groups claim absent or in overage for OID {Oid} — falling back to Graph /me/memberOf",
            oid);

        return await ResolveGroupsFromGraphAsync(oid, ct);
    }

    /// <summary>
    /// Returns true when Entra ID has populated the _claim_names claim, which signals token
    /// overage (user belongs to more groups than fit in the JWT — typically 200+).
    /// </summary>
    private static bool IsOverage(ClaimsPrincipal user)
    {
        return user.HasClaim(c => c.Type == ClaimNamesClaimType);
    }

    /// <summary>
    /// Attempts to read group GUIDs from the JWT "groups" claim.
    /// Returns false if the claim is absent or contains no values.
    /// </summary>
    private static bool TryGetGroupsFromClaims(
        ClaimsPrincipal user,
        out IReadOnlyList<string>? groups)
    {
        var groupClaims = user.Claims
            .Where(c => c.Type == GroupsClaimType)
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        if (groupClaims.Count == 0)
        {
            groups = null;
            return false;
        }

        groups = groupClaims;
        return true;
    }

    /// <summary>
    /// Calls Microsoft Graph GET /me/memberOf to enumerate all transitive group memberships.
    /// Returns an empty list on any error (fail-closed).
    /// </summary>
    private async Task<IReadOnlyList<string>> ResolveGroupsFromGraphAsync(string oid, CancellationToken ct)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            _logger.LogWarning(
                "PrivilegeGroupResolver: no HttpContext available for Graph call (OID {Oid}) — returning empty (fail-closed)",
                oid);
            return Array.Empty<string>();
        }

        try
        {
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, ct);

            // GET /me/memberOf — returns all directory objects (groups, directory roles, etc.)
            // We filter to only Group objects by checking the @odata.type.
            var memberOfResponse = await graphClient.Me.MemberOf.GetAsync(
                requestConfig =>
                {
                    // Request only the fields we need to minimise response payload
                    requestConfig.QueryParameters.Select = ["id", "displayName"];
                    requestConfig.QueryParameters.Top = 999;
                },
                ct);

            if (memberOfResponse?.Value == null)
            {
                _logger.LogWarning(
                    "PrivilegeGroupResolver: Graph /me/memberOf returned null for OID {Oid} — returning empty (fail-closed)",
                    oid);
                return Array.Empty<string>();
            }

            // Collect group IDs from the response (only Group objects, not roles)
            var groupIds = new List<string>();
            foreach (var directoryObject in memberOfResponse.Value)
            {
                if (directoryObject.Id != null)
                {
                    groupIds.Add(directoryObject.Id);
                }
            }

            // Handle pagination if there are more pages
            var pageIterator = PageIterator<Microsoft.Graph.Models.DirectoryObject,
                Microsoft.Graph.Models.DirectoryObjectCollectionResponse>.CreatePageIterator(
                graphClient,
                memberOfResponse,
                (directoryObject) =>
                {
                    if (directoryObject.Id != null)
                    {
                        groupIds.Add(directoryObject.Id);
                    }
                    return true; // continue iteration
                });

            await pageIterator.IterateAsync(ct);

            _logger.LogInformation(
                "PrivilegeGroupResolver: Graph resolved {Count} group memberships for OID {Oid}",
                groupIds.Count, oid);

            return groupIds;
        }
        catch (OperationCanceledException)
        {
            throw; // propagate cancellation
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "PrivilegeGroupResolver: Graph /me/memberOf failed for OID {Oid} — returning empty list (fail-closed). " +
                "User will receive zero AI Search results.",
                oid);
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// Extracts the Azure AD Object ID from the principal's claims.
    /// Handles both short-form ("oid") and long-form claim type URIs.
    /// </summary>
    private static string? GetObjectId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(OidClaimType)
            ?? user.FindFirstValue(ObjectIdClaimType);
    }
}
