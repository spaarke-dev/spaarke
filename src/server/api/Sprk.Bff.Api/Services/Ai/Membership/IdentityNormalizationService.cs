// R3 Part 1 — User-Record Membership Resolution (identity normalization implementation)
// Task 031 (2026-06-21): Resolves a systemuserid into the six identity-type
// components defined by design.md Part 1 § Identity normalization contract.
// Each path is independent (failing one does NOT fail others). Results cached
// in Redis (IDistributedCache) with a 10-minute TTL per ADR-009.
//
// Sub-queries executed in parallel via Task.WhenAll:
//   1. systemuser row     → BusinessUnitId, PrimaryEmail, azureactivedirectoryobjectid
//   2. contact cross-ref  → ContactId (via azureactivedirectoryobjectid match, ADR-028)
//   3. teammembership     → TeamIds[]
//
// Sequential after #1+#2 (depend on contact lookup):
//   4. account             → AccountId (from contact.parentcustomerid if account)
//   5. organizations       → OrganizationIds[] (delegated to IIdentityOrganizationResolver)
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.5, FR-1A.6;
//            projects/spaarke-platform-foundations-r3/design.md Part 1 §
//            Identity normalization contract; ADR-009, ADR-010, ADR-028, ADR-024.

using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Resolves a Dataverse <c>systemuserid</c> into a normalized
/// <see cref="PersonIdentity"/> by querying the six identity-type paths in
/// parallel and merging the results. Cached in Redis (<see cref="IDistributedCache"/>)
/// with a 10-minute TTL per ADR-009. Failure on a single identity-type path
/// produces a <c>null</c> / empty value for that field without failing the
/// other paths (per FR-1A.5 contract).
/// </summary>
public sealed class IdentityNormalizationService : IIdentityNormalizationService
{
    /// <summary>
    /// Cache resource label (per ITenantCache contract). The on-wire key becomes
    /// <c>tenant:{tenantId}:membership-identity:{systemUserId:D}:v1</c>
    /// (with the configured <c>InstanceName</c> prepended by StackExchangeRedisCache).
    /// </summary>
    /// <remarks>
    /// Phase 2 invalidation channel (FR-2P2.8) — a future per-user invalidation can
    /// target this resource label without affecting other Redis namespaces.
    /// </remarks>
    internal const string CacheResource = "membership-identity";

    /// <summary>Cache schema version per ADR-009.</summary>
    private const int CacheVersion = 1;

    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IDataverseService _dataverse;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly IEnumerable<IIdentityOrganizationResolver> _organizationResolvers;
    private readonly ILogger<IdentityNormalizationService> _logger;

    public IdentityNormalizationService(
        IDataverseService dataverse,
        ITenantCache cache,
        IEnumerable<IIdentityOrganizationResolver> organizationResolvers,
        IOptions<MembershipOptions> options,
        ILogger<IdentityNormalizationService> logger,
        IHttpContextAccessor? httpContextAccessor = null)
    {
        ArgumentNullException.ThrowIfNull(dataverse);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(organizationResolvers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _dataverse = dataverse;
        _cache = cache;
        _httpContextAccessor = httpContextAccessor;
        _organizationResolvers = organizationResolvers;
        _logger = logger;
        _ = options.Value; // Currently unused at runtime; reserved for future tuning
                           // (BU-descendant policy, additional identity tables, etc.)
                           // Resolving here surfaces binding errors at construction
                           // rather than first call.
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

    /// <inheritdoc/>
    public async Task<PersonIdentity> ResolveAsync(Guid systemUserId, CancellationToken ct)
    {
        if (systemUserId == Guid.Empty)
        {
            throw new ArgumentException(
                "systemUserId must not be Guid.Empty",
                nameof(systemUserId));
        }

        ct.ThrowIfCancellationRequested();

        // ── Cache lookup ────────────────────────────────────────────────────
        var tenantId = GetTenantId();
        var cacheId = systemUserId.ToString("D");
        var cached = await TryGetFromCacheAsync(tenantId, cacheId, ct).ConfigureAwait(false);
        if (cached is not null)
        {
            _logger.LogDebug(
                "IdentityNormalizationService cache HIT for systemUserId={SystemUserId}",
                systemUserId);
            return cached;
        }

        var sw = Stopwatch.StartNew();
        _logger.LogDebug(
            "IdentityNormalizationService cache MISS for systemUserId={SystemUserId} — resolving",
            systemUserId);

        // ── Parallel-fetch the three independent root paths ────────────────
        // SystemUser row provides: BusinessUnitId, PrimaryEmail, AADObjectId
        // (the AADObjectId then drives the contact cross-ref below).
        // Teams are independent of the systemuser row content.
        var systemUserTask = TryResolveSystemUserAsync(systemUserId, ct);
        var teamsTask = TryResolveTeamsAsync(systemUserId, ct);

        await Task.WhenAll(systemUserTask, teamsTask).ConfigureAwait(false);

        var systemUserData = await systemUserTask.ConfigureAwait(false);
        var teamIds = await teamsTask.ConfigureAwait(false);

        // ── Contact cross-ref (ADR-028) ─────────────────────────────────────
        // Requires the AAD object id from the systemuser row; therefore
        // depends on #1 completing. If AAD object id is unknown, ContactId
        // stays null (path failed independently, others unaffected).
        Guid? contactId = null;
        if (systemUserData.AzureAdObjectId is { } aadOid)
        {
            contactId = await TryResolveContactIdAsync(aadOid, ct).ConfigureAwait(false);
        }

        // ── Account via contact.parentcustomerid ───────────────────────────
        Guid? accountId = null;
        if (contactId is { } cid)
        {
            accountId = await TryResolveAccountIdAsync(cid, ct).ConfigureAwait(false);
        }

        // ── Organizations (delegated to task 032's resolver(s)) ────────────
        var organizationIds = await ResolveOrganizationIdsAsync(
            systemUserId,
            contactId,
            ct).ConfigureAwait(false);

        var identity = new PersonIdentity(
            SystemUserId: systemUserId,
            ContactId: contactId,
            PrimaryEmail: systemUserData.PrimaryEmail,
            TeamIds: teamIds,
            BusinessUnitId: systemUserData.BusinessUnitId,
            AccountId: accountId,
            OrganizationIds: organizationIds);

        await TrySetCacheAsync(tenantId, cacheId, identity, ct).ConfigureAwait(false);

        sw.Stop();
        _logger.LogInformation(
            "IdentityNormalizationService resolved systemUserId={SystemUserId} " +
            "in {ElapsedMs}ms (contactId={ContactId}, teams={TeamCount}, " +
            "bu={BusinessUnitId}, account={AccountId}, orgs={OrgCount})",
            systemUserId,
            sw.ElapsedMilliseconds,
            contactId,
            teamIds.Count,
            systemUserData.BusinessUnitId,
            accountId,
            organizationIds.Count);

        return identity;
    }

    // ── Path 1: systemuser row ─────────────────────────────────────────────
    private async Task<SystemUserData> TryResolveSystemUserAsync(
        Guid systemUserId,
        CancellationToken ct)
    {
        try
        {
            var entity = await _dataverse.RetrieveAsync(
                "systemuser",
                systemUserId,
                new[]
                {
                    "systemuserid",
                    "internalemailaddress",
                    "domainname",
                    "businessunitid",
                    "azureactivedirectoryobjectid"
                },
                ct).ConfigureAwait(false);

            var email = GetString(entity, "internalemailaddress")
                ?? GetString(entity, "domainname");

            var businessUnitId = GetEntityReferenceId(entity, "businessunitid");
            var aadOid = GetGuidLike(entity, "azureactivedirectoryobjectid");

            return new SystemUserData(email, businessUnitId, aadOid);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to resolve systemuser row for " +
                "systemUserId={SystemUserId}; BU/email/AAD-oid will be null",
                systemUserId);
            return SystemUserData.Empty;
        }
    }

    // ── Path 2: contact cross-ref via azureactivedirectoryobjectid ─────────
    // Per ADR-028 — the single source of truth for SystemUser↔Contact mapping.
    private async Task<Guid?> TryResolveContactIdAsync(
        Guid aadObjectId,
        CancellationToken ct)
    {
        try
        {
            var query = new QueryExpression("contact")
            {
                ColumnSet = new ColumnSet("contactid"),
                TopCount = 1,
                NoLock = true
            };
            query.Criteria.AddCondition(
                "azureactivedirectoryobjectid",
                ConditionOperator.Equal,
                aadObjectId);

            var results = await _dataverse
                .RetrieveMultipleAsync(query, ct)
                .ConfigureAwait(false);

            if (results.Entities.Count == 0)
            {
                return null;
            }

            var contactId = results.Entities[0].Id;
            return contactId == Guid.Empty ? null : contactId;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to cross-reference contact for " +
                "azureActiveDirectoryObjectId={AadObjectId}; ContactId will be null",
                aadObjectId);
            return null;
        }
    }

    // ── Path 3: teammembership → teamIds[] ─────────────────────────────────
    private async Task<IReadOnlyList<Guid>> TryResolveTeamsAsync(
        Guid systemUserId,
        CancellationToken ct)
    {
        try
        {
            // teammembership is the intersect entity. Filter by systemuserid,
            // project teamid only — no payload bloat.
            var query = new QueryExpression("teammembership")
            {
                ColumnSet = new ColumnSet("teamid"),
                NoLock = true
            };
            query.Criteria.AddCondition(
                "systemuserid",
                ConditionOperator.Equal,
                systemUserId);

            var results = await _dataverse
                .RetrieveMultipleAsync(query, ct)
                .ConfigureAwait(false);

            if (results.Entities.Count == 0)
            {
                return Array.Empty<Guid>();
            }

            var ids = new HashSet<Guid>();
            foreach (var row in results.Entities)
            {
                if (row.Contains("teamid") && row["teamid"] is Guid g && g != Guid.Empty)
                {
                    ids.Add(g);
                }
            }

            return ids.Count == 0 ? Array.Empty<Guid>() : ids.ToArray();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to resolve teammembership for " +
                "systemUserId={SystemUserId}; TeamIds will be empty",
                systemUserId);
            return Array.Empty<Guid>();
        }
    }

    // ── Path 4: contact → parentcustomerid → accountid (only if Account) ───
    private async Task<Guid?> TryResolveAccountIdAsync(
        Guid contactId,
        CancellationToken ct)
    {
        try
        {
            var entity = await _dataverse.RetrieveAsync(
                "contact",
                contactId,
                new[] { "contactid", "parentcustomerid" },
                ct).ConfigureAwait(false);

            if (!entity.Contains("parentcustomerid") ||
                entity["parentcustomerid"] is not EntityReference parentRef)
            {
                return null;
            }

            // parentcustomerid is polymorphic (contact OR account). We only
            // care about Account; ignore Contact-typed parents per design.
            return string.Equals(parentRef.LogicalName, "account", StringComparison.OrdinalIgnoreCase)
                ? parentRef.Id == Guid.Empty ? null : parentRef.Id
                : null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to resolve parentcustomerid → account for " +
                "contactId={ContactId}; AccountId will be null",
                contactId);
            return null;
        }
    }

    // ── Path 5: organizations via task 032's resolver(s) ───────────────────
    private async Task<IReadOnlyList<Guid>> ResolveOrganizationIdsAsync(
        Guid systemUserId,
        Guid? contactId,
        CancellationToken ct)
    {
        var resolvers = _organizationResolvers.ToList();
        if (resolvers.Count == 0)
        {
            return Array.Empty<Guid>();
        }

        var merged = new HashSet<Guid>();
        foreach (var resolver in resolvers)
        {
            try
            {
                var ids = await resolver
                    .ResolveOrganizationsAsync(systemUserId, contactId, ct)
                    .ConfigureAwait(false);

                if (ids is null)
                {
                    continue;
                }

                foreach (var id in ids)
                {
                    if (id != Guid.Empty)
                    {
                        merged.Add(id);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "IdentityNormalizationService: organization resolver {ResolverType} " +
                    "threw for systemUserId={SystemUserId}; skipping this resolver, " +
                    "other resolvers' results still merged",
                    resolver.GetType().FullName,
                    systemUserId);
            }
        }

        return merged.Count == 0 ? Array.Empty<Guid>() : merged.ToArray();
    }

    // ── Cache helpers ──────────────────────────────────────────────────────
    private async Task<PersonIdentity?> TryGetFromCacheAsync(
        string tenantId,
        string cacheId,
        CancellationToken ct)
    {
        try
        {
            return await _cache.GetAsync<PersonIdentity>(
                tenantId,
                CacheResource,
                cacheId,
                CacheVersion,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Cache failure must NOT break resolution — fall through to re-resolve.
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to read cache for tenant={TenantId} id={CacheId}; " +
                "falling through to live resolve",
                tenantId, cacheId);
            return null;
        }
    }

    private async Task TrySetCacheAsync(
        string tenantId,
        string cacheId,
        PersonIdentity identity,
        CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(
                tenantId,
                CacheResource,
                cacheId,
                CacheVersion,
                identity,
                CacheTtl,
                ct: ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "IdentityNormalizationService failed to write cache for tenant={TenantId} id={CacheId}; " +
                "next call will re-resolve (no functional impact)",
                tenantId, cacheId);
        }
    }

    // ── Entity helpers ─────────────────────────────────────────────────────
    private static string? GetString(Entity entity, string attribute)
        => entity.Contains(attribute) && entity[attribute] is string s && !string.IsNullOrWhiteSpace(s)
            ? s
            : null;

    private static Guid? GetEntityReferenceId(Entity entity, string attribute)
    {
        if (!entity.Contains(attribute) || entity[attribute] is not EntityReference er)
        {
            return null;
        }
        return er.Id == Guid.Empty ? null : er.Id;
    }

    /// <summary>
    /// Reads a value that may be stored as <see cref="Guid"/> or as a string
    /// containing a Guid representation. Dataverse <c>azureactivedirectoryobjectid</c>
    /// returns as a Guid via the SDK but as a string via the Web API — accept both.
    /// </summary>
    private static Guid? GetGuidLike(Entity entity, string attribute)
    {
        if (!entity.Contains(attribute))
        {
            return null;
        }

        var value = entity[attribute];
        return value switch
        {
            Guid g when g != Guid.Empty => g,
            string s when Guid.TryParse(s, out var parsed) && parsed != Guid.Empty => parsed,
            _ => null
        };
    }

    private readonly record struct SystemUserData(
        string? PrimaryEmail,
        Guid? BusinessUnitId,
        Guid? AzureAdObjectId)
    {
        public static SystemUserData Empty { get; } = new(null, null, null);
    }
}
