using System.Diagnostics;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Infrastructure.Caching;

/// <summary>
/// Decorator over <see cref="IAccessDataSource"/> that caches authorization DATA in Redis
/// while ensuring authorization DECISIONS are always computed fresh per-request.
///
/// ADR-003: Cache data, NOT decisions. Decisions are computed by AuthorizationService/OperationAccessRule.
/// ADR-009: Redis-first through <see cref="ITenantCache"/>; short TTLs for security-sensitive data.
///
/// Cache key scheme (unified-access-control-r2 task 132 · defect C12 — moved onto <see cref="ITenantCache"/>,
/// ADR-009 path C; on-wire keys carry the configured <c>InstanceName</c> in front):
/// - Document access: <c>tenant:{tid}:auth-access:{authMode}:{userOid}:{documentId}:v2</c>       TTL 60 s
/// - Record access:   <c>tenant:{tid}:auth-record-access:{entitySet}:{userOid}:{recordId}:v2</c> TTL 60 s
///
/// Both are evicted for EVERY user of a record by <c>IMembershipCacheInvalidator</c> on the BFF's owner changes
/// (<c>InvalidateRecordOwnerChangeAsync</c>) and share changes (<c>InvalidateRecordShareChangeAsync</c>, called through
/// <c>IRecordShareWriteObserver</c> by <c>DataverseWebApiService</c> on every grant / modify / revoke it makes, round 55)
/// — task 132. A table re-owned silently by an Assign cascade is never cached (<see cref="CachesRecordEntitySet"/>).
///
/// The former user-level role and team keys (<c>sdap:</c>-prefixed, 2-minute) are GONE: they were written on every
/// miss and read by nothing in the repo (task 132 removed them with their TTLs).
///
/// Performance target: Authorization overhead drops from 50-200ms (Dataverse) to &lt;10ms on cache hit.
/// Security: Fail-open to inner data source on cache errors (cache is optimization, not requirement).
///
/// <para><b>A fault is never stored (task 132 · C12).</b> The inner source marks a snapshot
/// <see cref="AccessSnapshot.Faulted"/> when it is not a complete Dataverse answer — a failed read, a 429/5xx/timeout,
/// a failed user lookup, a failed probe or team/role sub-read, or a DEGRADED probe-derived Read after
/// <c>RetrievePrincipalAccess</c> gave no answer. Such a snapshot is returned to the request unchanged and NOT
/// cached, so a transient fault denies one request instead of 60 seconds of them, and a Write holder is never pinned
/// at Read. A legitimate None (RPA answered with no rights, a 403/404 probe, a user lookup that found no systemuser)
/// IS cached.</para>
///
/// <para><b>Tenant segment (ADR-009).</b> The caller's <c>tid</c> claim. Every caller of this decorator runs inside an
/// HTTP request carrying a validated Entra token (AuthorizationService denies without a caller token;
/// AiAuthorizationService takes the HttpContext), and every Entra token carries <c>tid</c>. A request with no
/// <c>tid</c> is not cached at all — never keyed under a sentinel tenant.</para>
///
/// Auth-mode key segment (finding A-19 / spec FR-13, fixed by task 014): the document-access key
/// includes an <c>{authMode}</c> segment ("sp" when <c>userAccessToken</c> is null/empty, "obo"
/// otherwise) so an app-only (service-principal) snapshot can never be served to a subsequent OBO
/// caller, or vice versa, within the 60s TTL. The discriminator is a mode flag, never the raw token —
/// the raw token MUST NOT appear in a cache key or a log line (project constraint on task 014).
///
/// <b>Updated by task 006 (2026-08-21).</b> When task 014 wrote this comment,
/// <c>Spaarke.Core.Auth.AuthorizationService</c> always called with <c>userAccessToken: null</c>
/// (app-only) and <c>AiAuthorizationService</c> was the only OBO caller. Task 004 (FR-02) made
/// <c>AuthorizationService</c> caller-scoped, so BOTH now pass the caller's bearer token and the "sp"
/// branch is reached only by a path that genuinely has no caller credential. The mode flag is still
/// required: it is what prevents such a path's snapshot from being served to an OBO caller.
///
/// A boolean mode flag is sufficient here (no further per-identity hash needed) because <c>userId</c>
/// (the first parameter of <see cref="GetUserAccessAsync"/>) is already the caller's stable 'oid'
/// claim — both call sites (<c>AuthorizationService.cs:49</c>, <c>AiAuthorizationService.cs:176-178</c>
/// via <c>CheckDocumentAccessAsync</c>) extract <c>userId</c> and <c>userAccessToken</c> from the SAME
/// validated <c>ClaimsPrincipal</c> for a single request, so two different OBO callers already produce
/// two different <c>userId</c> values and therefore two different keys — the mode flag only needs to
/// separate SP-mode from OBO-mode for the SAME oid, not disambiguate between distinct OBO identities
/// that happen to share an oid (there are none: oid IS the identity).
/// </summary>
public class CachedAccessDataSource : IAccessDataSource
{
    /// <summary>Cache resource for the document-scoped snapshot (<see cref="GetUserAccessAsync"/>).</summary>
    internal const string DocumentAccessResource = "auth-access";

    /// <summary>
    /// Cache resource for the entity-agnostic record snapshot (<see cref="GetRecordAccessAsync"/>). A DISTINCT
    /// resource from <see cref="DocumentAccessResource"/>, and the entity set is part of the id — see the remarks in
    /// <see cref="GetRecordAccessAsync"/>.
    /// </summary>
    internal const string RecordAccessResource = "auth-record-access";

    /// <summary>Cache schema version (ADR-009). v1 of the tenant-scoped keys; the old <c>sdap:</c> keys are orphaned.</summary>
    /// <remarks>
    /// <b>Bumped 1 → 2</b> (task 132 integration residual): the document id segment is now normalised
    /// (<see cref="DocumentIdSegment"/>), so the share/owner-change eviction can address it. A v1 entry may carry a
    /// differently-cased id the eviction pattern would not match; the bump makes every v1 entry unreachable instead.
    /// </remarks>
    internal const int CacheVersion = 2;

    /// <summary>TTL for per-resource access cache (most sensitive, shortest TTL).</summary>
    internal static readonly TimeSpan ResourceAccessTtl = TimeSpan.FromSeconds(60);

    private readonly IAccessDataSource _inner;
    private readonly ITenantCache _cache;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<CachedAccessDataSource> _logger;

    public CachedAccessDataSource(
        IAccessDataSource inner,
        ITenantCache cache,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<CachedAccessDataSource> logger)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _httpContextAccessor = httpContextAccessor;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// The cache id of a document-access snapshot — the reader's ONLY id builder, also used to build the
    /// eviction pattern (task 132), so a removal addresses exactly the key a read wrote.
    /// </summary>
    internal static string DocumentAccessCacheId(string authMode, string userId, string resourceId)
        => $"{authMode}:{userId}:{resourceId}";

    /// <summary>
    /// The cache id of a record-access snapshot — the reader's ONLY id builder, also used by the per-record eviction
    /// pattern (<c>MembershipCacheInvalidator.RecordOwnerChangePatterns</c>, task 132) with <c>*</c> for the user.
    /// </summary>
    internal static string RecordAccessCacheId(string entitySetName, string userId, string recordId)
        => $"{entitySetName}:{userId}:{recordId}";

    /// <summary>The record id segment, in the one format both the reader and the eviction pattern use.</summary>
    internal static string RecordIdSegment(Guid recordId) => recordId.ToString("D");

    /// <summary>
    /// The document id segment of a document-access key: a GUID in the one format <see cref="RecordIdSegment"/> uses
    /// (callers pass route text, whose casing and braces vary), anything else verbatim. Shared with the eviction
    /// pattern (<c>MembershipCacheInvalidator</c>, task 132), so an owner or share change on a document reaches every
    /// snapshot of it however the id was spelled on the request that cached it.
    /// </summary>
    internal static string DocumentIdSegment(string resourceId)
        => Guid.TryParse(resourceId, out var id) ? RecordIdSegment(id) : resourceId;

    /// <summary>
    /// Whether <paramref name="entitySetName"/> is the set the document-scoped <see cref="GetUserAccessAsync"/> answers for
    /// — its snapshot key carries no set, so the eviction must add the document-access pattern for such a record.
    /// </summary>
    internal static bool IsDocumentEntitySet(string entitySetName)
        => string.Equals(entitySetName?.Trim(), DataverseAccessDataSource.DocumentEntitySetName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a record snapshot of <paramref name="entitySetName"/> is cached at all — the ONE predicate this reader and
    /// the eviction hook share. False only for a table Dataverse re-owns as a side effect of a secure root's Assign
    /// cascade (<c>AssignCascadeChildOwners.IsReownedByCascadeEntitySet</c>, task 132 integration residual): no BFF
    /// write can evict after those owner changes, so such a snapshot is always read live.
    /// </summary>
    internal static bool CachesRecordEntitySet(string entitySetName)
        => !Sprk.Bff.Api.Infrastructure.Dataverse.AssignCascadeChildOwners.IsReownedByCascadeEntitySet(entitySetName);

    /// <summary>The request's tenant (<c>tid</c>), or null — in which case nothing is read from or written to the cache.</summary>
    internal static string? TenantFor(ClaimsPrincipal? user)
        => user?.FindFirst("tid")?.Value
            ?? user?.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;

    /// <inheritdoc />
    public async Task<AccessSnapshot> GetUserAccessAsync(
        string userId,
        string resourceId,
        string? userAccessToken = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId, nameof(resourceId));

        // Auth-mode discriminator (A-19 / FR-13, task 014): "sp" (service-principal / app-only,
        // AuthorizationService's userAccessToken:null path) vs "obo" (on-behalf-of, AiAuthorizationService's
        // caller-bearer path). Fail-closed toward separation: any non-empty token is treated as OBO, so an
        // SP-mode snapshot is never returned for a request that presented a token. The raw token itself is
        // NEVER used as (or embedded in) the key — only this two-value mode flag.
        var authMode = string.IsNullOrEmpty(userAccessToken) ? "sp" : "obo";

        var tenantId = TenantFor(_httpContextAccessor?.HttpContext?.User);
        if (tenantId is null)
        {
            // No tid: not cached (ADR-009 forbids an untenanted key; a sentinel tenant would pool callers).
            return await _inner.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);
        }

        var cacheId = DocumentAccessCacheId(authMode, userId, DocumentIdSegment(resourceId));
        var cached = await TryGetAsync(tenantId, DocumentAccessResource, cacheId, ct);
        if (cached is not null)
        {
            return cached;
        }

        // Cache miss or error - fetch from Dataverse
        var snapshot = await _inner.GetUserAccessAsync(userId, resourceId, userAccessToken, ct);

        // Cache the result (fire-and-forget style, don't block the response) — unless it is a fault (task 132).
        CacheUnlessFaulted(tenantId, DocumentAccessResource, cacheId, snapshot);

        return snapshot;
    }

    /// <inheritdoc />
    public async Task<AccessSnapshot> GetRecordAccessAsync(
        string userId,
        string entitySetName,
        Guid recordId,
        string? userAccessToken,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId, nameof(userId));
        ArgumentException.ThrowIfNullOrWhiteSpace(entitySetName, nameof(entitySetName));

        // Do NOT cache a denial caused by a missing token: it is a property of the REQUEST, not of the
        // caller's access, and caching it would deny a later well-formed request for the whole TTL.
        // Delegate straight through — the inner implementation is the one that fails closed.
        if (recordId == Guid.Empty || string.IsNullOrWhiteSpace(userAccessToken))
        {
            return await _inner.GetRecordAccessAsync(userId, entitySetName, recordId, userAccessToken, ct);
        }

        var tenantId = TenantFor(_httpContextAccessor?.HttpContext?.User);
        if (tenantId is null || !CachesRecordEntitySet(entitySetName))
        {
            // No tid (ADR-009), or a table re-owned silently by an Assign cascade (task 132): read live, never cached.
            return await _inner.GetRecordAccessAsync(userId, entitySetName, recordId, userAccessToken, ct);
        }

        // DISTINCT resource ("auth-record-access", not "auth-access") AND the entity set is part of the id.
        //
        // This matters: GetUserAccessAsync's id is `{authMode}:{userId}:{resourceId}` with no entity type, because
        // that path is document-only by contract. Reusing that shape for arbitrary entities would make the key
        // ambiguous about WHICH RECORD was asked about — a snapshot for one table answering for another table's
        // record of the same id. Separate resource + entity set makes that structurally impossible rather than
        // merely unlikely.
        //
        // No authMode discriminator: this method denies without a caller token, so every cached entry is
        // an OBO answer by construction.
        var cacheId = RecordAccessCacheId(entitySetName, userId, RecordIdSegment(recordId));
        var cached = await TryGetAsync(tenantId, RecordAccessResource, cacheId, ct);
        if (cached is not null)
        {
            return cached;
        }

        var snapshot = await _inner.GetRecordAccessAsync(userId, entitySetName, recordId, userAccessToken, ct);

        CacheUnlessFaulted(tenantId, RecordAccessResource, cacheId, snapshot);

        return snapshot;
    }

    private async Task<AccessSnapshot?> TryGetAsync(string tenantId, string resource, string cacheId, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var cached = await _cache.GetAsync<CachedAccessSnapshot>(tenantId, resource, cacheId, CacheVersion, ct: ct);
            sw.Stop();

            if (cached is not null)
            {
                _logger.LogDebug(
                    "[AUTH-CACHE] HIT {Resource}: Id={CacheId}, Latency={LatencyMs}ms",
                    resource, cacheId, sw.ElapsedMilliseconds);
                CacheMetrics.RecordHit(sw.Elapsed.TotalMilliseconds, "auth-access");
                return cached.ToAccessSnapshot();
            }

            _logger.LogDebug(
                "[AUTH-CACHE] MISS {Resource}: Id={CacheId}, Latency={LatencyMs}ms",
                resource, cacheId, sw.ElapsedMilliseconds);
            CacheMetrics.RecordMiss(sw.Elapsed.TotalMilliseconds, "auth-access");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            sw.Stop();
            _logger.LogWarning(ex,
                "[AUTH-CACHE] Error reading {Resource} cache: Id={CacheId}. Falling through to Dataverse.",
                resource, cacheId);
            CacheMetrics.RecordMiss(sw.Elapsed.TotalMilliseconds, "auth-access");
        }

        return null;
    }

    /// <summary>
    /// Writes the snapshot unless it is <see cref="AccessSnapshot.Faulted"/> — the ONE cache gate of this class
    /// (task 132 · C12). Fire-and-forget: the response never waits for the cache.
    /// </summary>
    private void CacheUnlessFaulted(string tenantId, string resource, string cacheId, AccessSnapshot snapshot)
    {
        if (snapshot.Faulted)
        {
            _logger.LogInformation(
                "[AUTH-CACHE] NOT caching {Resource} {CacheId}: the snapshot is faulted or degraded (task 132 — a fault " +
                "is returned to this request only, never stored). Rights for this request: {Rights}.",
                resource, cacheId, snapshot.AccessRights);
            return;
        }

        _ = CacheSnapshotAsync(tenantId, resource, cacheId, snapshot);
    }

    /// <summary>
    /// Caches the full access snapshot for a user+resource combination.
    /// </summary>
    private async Task CacheSnapshotAsync(string tenantId, string resource, string cacheId, AccessSnapshot snapshot)
    {
        try
        {
            await _cache.SetAsync(
                tenantId, resource, cacheId, CacheVersion,
                CachedAccessSnapshot.FromAccessSnapshot(snapshot), ResourceAccessTtl);

            _logger.LogDebug(
                "[AUTH-CACHE] Cached {Resource}: UserId={UserId}, ResourceId={ResourceId}, TTL={TtlSeconds}s",
                resource, snapshot.UserId, snapshot.ResourceId, ResourceAccessTtl.TotalSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "[AUTH-CACHE] Error caching {Resource} for UserId={UserId}, ResourceId={ResourceId}. Non-critical.",
                resource, snapshot.UserId, snapshot.ResourceId);
            // Don't throw - caching is optimization, not requirement
        }
    }

    /// <summary>
    /// DTO for serializing AccessSnapshot to/from Redis.
    /// Avoids serializing the full AccessSnapshot which has enum flags and DateTimeOffset.
    /// </summary>
    /// <remarks>
    /// Carries no fault flag on purpose (task 132): a faulted snapshot is never written, so every hit is a complete
    /// answer and restores with <see cref="AccessSnapshot.Faulted"/> false.
    /// </remarks>
    internal sealed class CachedAccessSnapshot
    {
        public string UserId { get; set; } = string.Empty;
        public string ResourceId { get; set; } = string.Empty;
        public int AccessRightsValue { get; set; }
        public List<string> TeamMemberships { get; set; } = new();
        public List<string> Roles { get; set; } = new();
        public DateTimeOffset CachedAt { get; set; }

        public static CachedAccessSnapshot FromAccessSnapshot(AccessSnapshot snapshot)
        {
            return new CachedAccessSnapshot
            {
                UserId = snapshot.UserId,
                ResourceId = snapshot.ResourceId,
                AccessRightsValue = (int)snapshot.AccessRights,
                TeamMemberships = snapshot.TeamMemberships.ToList(),
                Roles = snapshot.Roles.ToList(),
                CachedAt = DateTimeOffset.UtcNow
            };
        }

        public AccessSnapshot ToAccessSnapshot()
        {
            return new AccessSnapshot
            {
                UserId = UserId,
                ResourceId = ResourceId,
                AccessRights = (AccessRights)AccessRightsValue,
                TeamMemberships = TeamMemberships,
                Roles = Roles,
                CachedAt = CachedAt
            };
        }
    }
}
