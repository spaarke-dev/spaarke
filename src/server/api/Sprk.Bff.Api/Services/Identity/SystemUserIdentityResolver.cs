using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Identity;

/// <summary>
/// Shared, cached, bidirectional resolver for the Dataverse <c>systemuserid</c> ⇄ Azure AD <c>oid</c>
/// mapping (the <c>systemuser.azureactivedirectoryobjectid</c> cross-reference, ADR-028).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists (CLAUDE.md §11 consolidation point).</b> The <c>oid ⇄ systemuserid</c> lookup is
/// currently re-implemented in ≥5 ad-hoc private copies across the BFF (BriefingService,
/// DocumentCheckoutService, DataverseAccessDataSource, AnalysisEndpoints, UserPrivilegeChecker,
/// RegistrationDataverseService). The notification spine is the first surface that needs BOTH directions
/// from a shared, injectable service: producers hold the outbox <c>OwnerId</c> (a Dataverse
/// <c>systemuserid</c>) and must resolve it to an <c>oid</c> to target a SignalR connection
/// (<see cref="ResolveOidAsync"/>); the poll endpoint holds the caller's JWT <c>oid</c> and must resolve
/// it to a <c>systemuserid</c> to query the outbox (<see cref="ResolveSystemUserIdAsync"/>). This is the
/// third+ independent consumer <c>BriefingService.cs</c> flagged for extraction, so it earns a concrete
/// class + interface. The existing ad-hoc copies are NOT migrated here (high blast radius); see
/// <c>notes/identity-resolver-consolidation-defer.md</c>.
/// </para>
/// <para>
/// The mapping is stable (populated once by the AAD sync), so BOTH directions are cached in
/// <see cref="IDistributedCache"/> (Redis-first per ADR-009) with a bounded TTL, mirroring
/// <c>BriefingService.ResolveSystemUserIdAsync</c>'s caching approach. Reads/writes fail open — a cache
/// error falls through to a live Dataverse resolve rather than failing the caller.
/// </para>
/// </remarks>
public interface ISystemUserIdentityResolver
{
    /// <summary>
    /// Resolves the Azure AD <c>oid</c> for a Dataverse <paramref name="systemUserId"/> via the
    /// <c>systemuser.azureactivedirectoryobjectid</c> column (ADR-028). Returns the oid string ("D" GUID
    /// format), or <c>null</c> when the systemuser row is missing or has no AAD mapping.
    /// </summary>
    Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default);

    /// <summary>
    /// Resolves the Dataverse <c>systemuserid</c> for an Azure AD <paramref name="oid"/> by querying the
    /// <c>systemuser</c> entity's <c>azureactivedirectoryobjectid</c> column (ADR-028; disabled users
    /// excluded). Returns the <c>systemuserid</c>, or <c>null</c> when the oid is blank/non-GUID or has no
    /// matching enabled systemuser row.
    /// </summary>
    Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default);

    /// <summary>
    /// The authoritative internal-vs-external determination for a Dataverse user, read from the
    /// <c>systemuser.sprk_isexternal</c> two-option flag. Returns <c>true</c> when the user is EXTERNAL
    /// (a licensed-but-external systemuser), <c>false</c> when INTERNAL.
    /// <para>
    /// <b>Why a flag, not "is a systemuser".</b> A person we treat as external for internal-only-message
    /// purposes CAN be a licensed <c>systemuser</c> (owner confirmation 2026-07-21). Inferring internal from
    /// record type (systemuser⇒internal) therefore leaks internal-only content to external licensed users.
    /// This is the single authoritative source for the <c>CommunicationAccessContext.IsInternalUser</c> bit
    /// (callers pass <c>IsInternalUser: !await IsExternalAsync(id)</c>) — used by BOTH the notification
    /// fan-out and (via a coordinated messaging-r3 change) the timeline read path.
    /// </para>
    /// <para>
    /// <b>Fail-closed:</b> an empty id, a missing systemuser row, or an unreadable flag returns <c>true</c>
    /// (external) — the safe posture for internal-only exclusion (under-deliver, never leak). A present
    /// <c>false</c> is honored as internal (the column default is 'No'/internal). Cached like the other
    /// directions.
    /// </para>
    /// </summary>
    Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default);
}

/// <summary>
/// Default <see cref="ISystemUserIdentityResolver"/> — see the interface remarks for the Component and
/// Placement justification. Wraps the singleton <see cref="IDataverseService"/> facade (same Dataverse
/// access path the ad-hoc copies use) and caches both directions in <see cref="IDistributedCache"/>.
/// </summary>
public sealed class SystemUserIdentityResolver : ISystemUserIdentityResolver
{
    private const string SystemUserEntity = "systemuser";
    private const string AadObjectIdColumn = "azureactivedirectoryobjectid";
    private const string SystemUserIdColumn = "systemuserid";
    private const string IsExternalColumn = "sprk_isexternal";

    private const string SystemUserToOidCacheKeyPrefix = "identity:sysuser-to-oid:";
    private const string OidToSystemUserCacheKeyPrefix = "identity:oid-to-sysuser:";
    private const string ExternalityCacheKeyPrefix = "identity:sysuser-isexternal:";

    /// <summary>
    /// TTL for both cache directions. Mirrors <c>BriefingService.CurrentUserCacheTtl</c> (10 minutes) so a
    /// re-mapped/disabled user surfaces within at most 10 minutes.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private readonly IDataverseService _dataverse;
    private readonly IDistributedCache _cache;
    private readonly ILogger<SystemUserIdentityResolver> _logger;

    public SystemUserIdentityResolver(
        IDataverseService dataverse,
        IDistributedCache cache,
        ILogger<SystemUserIdentityResolver> logger)
    {
        _dataverse = dataverse ?? throw new ArgumentNullException(nameof(dataverse));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string?> ResolveOidAsync(Guid systemUserId, CancellationToken ct = default)
    {
        if (systemUserId == Guid.Empty)
        {
            return null;
        }

        var cacheKey = SystemUserToOidCacheKeyPrefix + systemUserId.ToString("D");

        // Cache lookup — fail-open on read errors.
        try
        {
            var cached = await _cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is { Length: > 0 })
            {
                return Encoding.UTF8.GetString(cached);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemUserIdentityResolver: sysuser→oid cache read failed for {CacheKey}; falling through to live resolve.",
                cacheKey);
        }

        // Live cross-reference: read systemuser.azureactivedirectoryobjectid for this systemuserid (ADR-028).
        var query = new QueryExpression(SystemUserEntity)
        {
            ColumnSet = new ColumnSet(AadObjectIdColumn),
            TopCount = 1,
            NoLock = true
        };
        query.Criteria.AddCondition(SystemUserIdColumn, ConditionOperator.Equal, systemUserId);

        var results = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (results.Entities.Count == 0)
        {
            return null;
        }

        // The column is populated by the AAD sync; read defensively (it surfaces as Guid or string).
        var entity = results.Entities[0];
        object? raw = entity.Contains(AadObjectIdColumn) ? entity[AadObjectIdColumn] : null;
        var oid = raw switch
        {
            Guid g when g != Guid.Empty => g.ToString("D"),
            string s when !string.IsNullOrWhiteSpace(s) => s,
            _ => null
        };

        if (string.IsNullOrWhiteSpace(oid))
        {
            // Row exists but has no AAD mapping — return null (delivery becomes a quiet no-op).
            return null;
        }

        await CacheStringAsync(cacheKey, oid, ct).ConfigureAwait(false);
        return oid;
    }

    /// <inheritdoc />
    public async Task<Guid?> ResolveSystemUserIdAsync(string oid, CancellationToken ct = default)
    {
        if (!Guid.TryParse(oid, out var oidGuid) || oidGuid == Guid.Empty)
        {
            _logger.LogDebug(
                "SystemUserIdentityResolver: oid→sysuser called with a blank/non-GUID oid; returning null.");
            return null;
        }

        var cacheKey = OidToSystemUserCacheKeyPrefix + oidGuid.ToString("D");

        // Cache lookup — fail-open on read errors.
        try
        {
            var cached = await _cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is { Length: 16 })
            {
                var cachedGuid = new Guid(cached);
                if (cachedGuid != Guid.Empty)
                {
                    return cachedGuid;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemUserIdentityResolver: oid→sysuser cache read failed for {CacheKey}; falling through to live resolve.",
                cacheKey);
        }

        // Live cross-reference via systemuser.azureactivedirectoryobjectid (ADR-028), disabled users excluded.
        var query = new QueryExpression(SystemUserEntity)
        {
            ColumnSet = new ColumnSet(SystemUserIdColumn),
            TopCount = 1,
            NoLock = true
        };
        query.Criteria.AddCondition(AadObjectIdColumn, ConditionOperator.Equal, oidGuid);
        query.Criteria.AddCondition("isdisabled", ConditionOperator.Equal, false);

        var results = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);
        if (results.Entities.Count == 0)
        {
            return null;
        }

        var systemUserId = results.Entities[0].Id;
        if (systemUserId == Guid.Empty)
        {
            return null;
        }

        // Cache write — fail-open on write errors.
        try
        {
            await _cache.SetAsync(
                cacheKey,
                systemUserId.ToByteArray(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemUserIdentityResolver: oid→sysuser cache write failed for {CacheKey}; next call will re-resolve.",
                cacheKey);
        }

        return systemUserId;
    }

    /// <inheritdoc />
    public async Task<bool> IsExternalAsync(Guid systemUserId, CancellationToken ct = default)
    {
        // Fail-closed: an unresolved user is treated as external for internal-only exclusion.
        if (systemUserId == Guid.Empty)
        {
            return true;
        }

        var cacheKey = ExternalityCacheKeyPrefix + systemUserId.ToString("D");

        // Cache lookup — fail-open on read errors (a cache miss/error falls through to a live resolve).
        try
        {
            var cached = await _cache.GetAsync(cacheKey, ct).ConfigureAwait(false);
            if (cached is { Length: 1 })
            {
                return cached[0] != 0;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemUserIdentityResolver: isexternal cache read failed for {CacheKey}; falling through to live resolve.",
                cacheKey);
        }

        // Live read of systemuser.sprk_isexternal (two-option, column default 'No'/internal).
        var query = new QueryExpression(SystemUserEntity)
        {
            ColumnSet = new ColumnSet(IsExternalColumn),
            TopCount = 1,
            NoLock = true
        };
        query.Criteria.AddCondition(SystemUserIdColumn, ConditionOperator.Equal, systemUserId);

        var results = await _dataverse.RetrieveMultipleAsync(query, ct).ConfigureAwait(false);

        // Fail-closed: a missing row or an unreadable/absent flag → external (never leak an internal-only
        // message to a user whose posture we cannot confirm internal). A present `false` is honored as internal.
        bool isExternal;
        if (results.Entities.Count == 0)
        {
            _logger.LogWarning(
                "SystemUserIdentityResolver: systemuser {SystemUserId} not found reading '{Column}' — treating as EXTERNAL (fail closed).",
                systemUserId, IsExternalColumn);
            isExternal = true;
        }
        else
        {
            var flag = results.Entities[0].GetAttributeValue<bool?>(IsExternalColumn);
            if (flag is null)
            {
                _logger.LogWarning(
                    "SystemUserIdentityResolver: systemuser {SystemUserId} has no readable '{Column}' value — treating as EXTERNAL (fail closed).",
                    systemUserId, IsExternalColumn);
                isExternal = true;
            }
            else
            {
                isExternal = flag.Value;
            }
        }

        // Cache write — fail-open on write errors.
        try
        {
            await _cache.SetAsync(
                cacheKey,
                new[] { (byte)(isExternal ? 1 : 0) },
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemUserIdentityResolver: isexternal cache write failed for {CacheKey}; next call will re-resolve.",
                cacheKey);
        }

        return isExternal;
    }

    private async Task CacheStringAsync(string cacheKey, string value, CancellationToken ct)
    {
        try
        {
            await _cache.SetAsync(
                cacheKey,
                Encoding.UTF8.GetBytes(value),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SystemUserIdentityResolver: sysuser→oid cache write failed for {CacheKey}; next call will re-resolve.",
                cacheKey);
        }
    }
}
