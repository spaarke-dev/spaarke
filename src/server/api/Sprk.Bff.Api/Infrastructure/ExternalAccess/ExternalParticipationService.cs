using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.Core;
using Sprk.Bff.Api.Infrastructure.Cache;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Queries sprk_externalrecordaccess for a Contact's active participations.
/// Results are cached in Redis with 60-second TTL per ADR-009.
///
/// Cache key: sdap:external:access:{contactId}
/// </summary>
public class ExternalParticipationService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(60);
    // Resource identifier for ITenantCache (FR-05). Cached value is per-Contact participation
    // data (project list), not an authorization decision per ADR-009.
    //
    // PUBLIC by design (task 073 #7): this service is the SINGLE SOURCE OF TRUTH for the participation
    // cache key. The write-side endpoints that INVALIDATE this cache — GrantExternalAccessEndpoint,
    // RevokeExternalAccessEndpoint, ProjectClosureEndpoint — MUST reference these constants rather than
    // re-declaring their own, so a version bump here automatically propagates to every invalidator. (A
    // prior local `CacheVersion = 1` in those endpoints silently missed the v2/v3 stored key — the exact
    // drift this shared constant removes.)
    public const string ExternalAccessResource = "external-access-grant";
    // CacheVersion 2 (task 028): the cached shape widened from project-only participations to the full
    // polymorphic grant set (projects + matters + work assignments). CacheVersion 3 (task 073 #7): the
    // cached grant set now ALSO includes records inherited via ORGANIZATION grants (Term 3 — org
    // memberships from sprk_contactorganization). The bump orphans any v2 entry (it expires on its 60s
    // TTL) so no stale pre-org-grant read can occur.
    // CacheVersion 4 (unified-access-control-r2 task 032 / FR-19): matter + work-assignment grants are
    // now cached as (id + LEVEL) instead of bare ids. The bump is LOAD-BEARING, not bookkeeping — a v3
    // entry deserializes into the v4 shape with no level, so every matter/WA would resolve to
    // AccessRights.None for one TTL after deploy: rights correct on a cache MISS, absent on a HIT, with
    // the unit suite green throughout because unit tests bypass the cache.
    public const int CacheVersion = 4;

    // ─────────────────────────────────────────────────────────────────────────
    // Grant-query construction (extracted by task 007 / FR-06, finding A-5)
    // ─────────────────────────────────────────────────────────────────────────
    //
    // These were inline string interpolations immediately before _httpClient.SendAsync, which is why
    // task 001 could not pin A-5 at all: the only way to observe the emitted $filter was to intercept
    // the transport, and Mock<HttpMessageHandler> is banned (ADR-038 §7 ban B1). Extracting them as
    // PURE members makes the predicate assertable directly — and the predicate is the whole fix, so
    // "does the query actually carry it" is the question that has to be answerable.
    //
    // internal + InternalsVisibleTo("Sprk.Bff.Api.Tests"), the convention already used across this
    // assembly. No reflection into privates (ban B8).

    /// <summary>Columns every grant read needs to partition a row into its root bucket.</summary>
    internal const string GrantRowSelect =
        "_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,sprk_accesslevel";

    /// <summary>
    /// The <c>$filter</c> selecting a Contact's own ACTIVE, UNEXPIRED grants.
    /// </summary>
    internal static string BuildContactGrantFilter(Guid contactId, DateOnly today)
        => $"_sprk_contact_value eq {contactId} and statecode eq 0 and {ExpiryPredicate(today)}";

    /// <summary>
    /// The <c>$filter</c> selecting ACTIVE, UNEXPIRED ORGANIZATION grants (contact empty — the
    /// org-grant marker) for any of the organizations a Contact actively belongs to.
    /// </summary>
    internal static string BuildOrganizationGrantFilter(IEnumerable<Guid> organizationIds, DateOnly today)
    {
        var orgFilter = string.Join(" or ", organizationIds.Select(id => $"_sprk_organization_value eq {id}"));
        return $"({orgFilter}) and _sprk_contact_value eq null and statecode eq 0 and {ExpiryPredicate(today)}";
    }

    /// <summary>
    /// Excludes grants whose expiry has passed — finding A-5 (spec FR-06).
    /// </summary>
    /// <remarks>
    /// <para><b>What was wrong.</b> <c>sprk_expiresdate</c> was written at grant time and read
    /// <i>nowhere</i>: it appeared in no <c>$filter</c> and no <c>$select</c> on any path, and there is
    /// no sweep job. A grant whose expiry had passed conferred full access forever, while the Manage
    /// Access UI presented expiry as a working control. A promise-shaped no-op.</para>
    ///
    /// <para><b>The null branch is load-bearing.</b> In OData, <c>field ge X</c> excludes nulls — so
    /// without <c>eq null</c> this predicate would silently revoke every grant that has NO expiry,
    /// which is most of them. That failure would look like a total outage of external access rather
    /// than an expiry bug.</para>
    ///
    /// <para><b>Why <c>ge</c> and not <c>gt</c>.</b> <c>sprk_expiresdate</c> is <b>Date Only</b>
    /// (verified against live Dataverse metadata, 2026-08-23 — the task's own escalation trigger
    /// required checking rather than trusting the docs). A date-only expiry of "30 June" means access
    /// works ON 30 June; <c>gt</c> would kill it at 00:00 that morning, silently shortening every
    /// grant in the system by a day. <c>ge</c> keeps the grant live through its expiry date and still
    /// satisfies FR-06, whose acceptance is about an expiry <i>in the past</i>.</para>
    ///
    /// <para><b>Server-side, deliberately.</b> Filtering after materialization would mean the rows
    /// crossed the wire and any later code path that forgot to re-filter would see them. The predicate
    /// belongs where the set is defined.</para>
    /// </remarks>
    internal static string ExpiryPredicate(DateOnly today)
        => $"(sprk_expiresdate eq null or sprk_expiresdate ge {today:yyyy-MM-dd})";

    /// <summary>Today in UTC — the reference date every expiry comparison uses.</summary>
    private static DateOnly TodayUtc => DateOnly.FromDateTime(DateTime.UtcNow);

    private readonly HttpClient _httpClient;
    private readonly ITenantCache _cache;
    private readonly IConfiguration _configuration;
    private readonly TokenCredential _credential;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ExternalParticipationService> _logger;
    private readonly SemaphoreSlim _tokenSemaphore = new(1, 1);
    private AccessToken? _currentToken;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExternalParticipationService(
        HttpClient httpClient,
        ITenantCache cache,
        IConfiguration configuration,
        TokenCredential credential,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ExternalParticipationService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _configuration = configuration;
        _credential = credential;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Gets active PROJECT participations for a Contact (id + access level). Retained for the CIAM
    /// <c>/me</c> per-project level mapping and every legacy project-scoped caller; it now projects the
    /// project slice of the full <see cref="GetGrantSetAsync"/> grant set so there is a single query +
    /// cache entry per Contact.
    /// </summary>
    public virtual async Task<IReadOnlyList<ExternalParticipation>> GetParticipationsAsync(
        Guid contactId,
        CancellationToken ct = default)
    {
        var grantSet = await GetGrantSetAsync(contactId, ct).ConfigureAwait(false);
        return grantSet.Projects;
    }

    /// <summary>
    /// Gets the FULL polymorphic grant set for a Contact — projects (with level) + matters + work
    /// assignments — from active <c>sprk_externalrecordaccess</c> rows (task 028). Checks Redis cache
    /// first (60s TTL, ADR-009), falls back to Dataverse. Outside-counsel access is grant-only: this set
    /// is exactly what a CIAM partner may see, and one of the union terms for an internal caller.
    /// </summary>
    public virtual async Task<ExternalGrantSet> GetGrantSetAsync(
        Guid contactId,
        CancellationToken ct = default)
    {
        var tenantId = ExtractTenantId();
        var idComponent = contactId.ToString();

        // Try cache first (only if tenantId is available — otherwise fall through to Dataverse)
        if (!string.IsNullOrEmpty(tenantId))
        {
            try
            {
                var cached = await _cache.GetAsync<CachedGrantSet>(
                    tenantId, ExternalAccessResource, idComponent, CacheVersion, ct: ct);
                if (cached != null)
                {
                    _logger.LogDebug(
                        "[EXT-ACCESS] Cache HIT for Contact {ContactId}: {Projects} project / {Matters} matter / {Was} work-assignment grants",
                        contactId, cached.Projects.Count, cached.MatterGrants.Count, cached.WorkAssignmentGrants.Count);
                    return cached.ToGrantSet();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EXT-ACCESS] Cache read error for Contact {ContactId}. Falling through to Dataverse.", contactId);
            }
        }

        // Cache miss — query Dataverse
        var grantSet = await QueryGrantSetAsync(contactId, ct);

        // Cache result (fire-and-forget — don't block response). Skip when no tenant claim.
        if (!string.IsNullOrEmpty(tenantId))
        {
            _ = CacheGrantSetAsync(tenantId, idComponent, grantSet);
        }

        return grantSet;
    }

    /// <summary>
    /// Invalidates the cached per-Contact participation DATA entry
    /// (<c>tenant:{tid}:external-access-grant:{contactId}:v1</c>, the tenant-scoped realization of the
    /// documented <c>sdap:external:access:{contactId}</c> key) so a subsequent accessible-set
    /// evaluation re-reads current state instead of serving up to 60 seconds of stale TTL.
    /// </summary>
    /// <remarks>
    /// teams-app-r1 task 051 — the standing-grant runtime union (design §5). When a contact's
    /// subject-level standing grant (<c>contact.sprk_standinggrant</c>) is toggled, the contact's
    /// accessible set widens or narrows. This clears the contact's cached participation DATA so the
    /// change reflects promptly. This is a DATA-cache invalidation only: it never caches — and never
    /// invalidates — an authorization DECISION (the yes/no record∈set outcome is recomputed live by
    /// <see cref="AccessibleRecordSetService"/> on every request per <c>.claude/constraints/auth.md</c>
    /// "MUST NOT cache authorization decisions"). The standing-grant flag itself is read live (never
    /// cached) by <see cref="ContactStandingGrantReader"/>, so this invalidation is the defensive
    /// belt-and-suspenders that also drops any co-cached per-contact grant data for the same subject.
    /// <para>
    /// <paramref name="tenantId"/> is explicit so an out-of-request caller (e.g. a future Dataverse
    /// change webhook on the standing-grant field) can invalidate without an ambient HttpContext; when
    /// null it falls back to the current request's <c>tid</c> claim. A no-tenant call is a logged
    /// no-op (the cache key is mandatorily tenant-scoped, so there is nothing to remove without one).
    /// </para>
    /// </remarks>
    public virtual async Task InvalidateAsync(
        Guid contactId,
        string? tenantId = null,
        CancellationToken ct = default)
    {
        var tenant = tenantId ?? ExtractTenantId();
        if (string.IsNullOrEmpty(tenant))
        {
            _logger.LogWarning(
                "[EXT-ACCESS] InvalidateAsync for Contact {ContactId} skipped: no tenant id available " +
                "(no explicit tenantId argument and no 'tid' claim on the current request). The cache " +
                "key is tenant-scoped, so there is nothing to remove.", contactId);
            return;
        }

        try
        {
            await _cache.RemoveAsync(
                tenant, ExternalAccessResource, contactId.ToString(), CacheVersion, ct: ct);
            _logger.LogInformation(
                "[EXT-ACCESS] Invalidated cached participation data for Contact {ContactId} (tenant {TenantId}) " +
                "— standing-grant change reflects on next evaluation.", contactId, tenant);
        }
        catch (Exception ex)
        {
            // Non-fatal: a failed invalidation degrades to the 60s TTL expiring on its own. The
            // authorization decision is never cached, so the worst case is a bounded staleness window,
            // not an incorrect grant/deny beyond that window.
            _logger.LogWarning(ex,
                "[EXT-ACCESS] Failed to invalidate participation cache for Contact {ContactId} (tenant {TenantId}). " +
                "Falling back to TTL expiry.", contactId, tenant);
        }
    }

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the current HttpContext.
    /// Returns null when no claim is present (in which case caching is skipped).
    /// </summary>
    private string? ExtractTenantId()
    {
        var user = _httpContextAccessor.HttpContext?.User;
        if (user is null) return null;
        return user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
    }

    /// <summary>
    /// Resolves the Dataverse Contact for an external (CIAM) caller by the stable <c>oid</c> claim
    /// (bound to <c>Contact.sprk_externalobjectid</c>) per ADR-028 Amendment A1. Email is used only as
    /// a <b>first-login</b> fallback that then binds the oid onto the Contact; once a Contact is bound
    /// to an oid, a mismatched email can neither redirect resolution nor grant access.
    ///
    /// Resolution order:
    ///   1. If <paramref name="oid"/> is present, look up the Contact by <c>sprk_externalobjectid</c>.
    ///      A hit is authoritative (email is not consulted).
    ///   2. Otherwise (no Contact bound to this oid yet), fall back to an <c>emailaddress1</c> match,
    ///      but ONLY bind the oid onto — and grant — a Contact that has no oid yet. A Contact already
    ///      bound to a <i>different</i> oid is NOT granted via email (prevents shared-email hijack).
    /// </summary>
    /// <param name="oid">The CIAM token's stable object id (immutable directory key). May be null on a
    /// non-CIAM/transitional email-only token.</param>
    /// <param name="email">The caller's email/UPN claim (first-login fallback). May be null.</param>
    public virtual async Task<Guid?> ResolveExternalContactAsync(string? oid, string? email, CancellationToken ct = default)
    {
        // 1. Stable-oid resolution — authoritative once bound.
        if (!string.IsNullOrEmpty(oid))
        {
            var byOid = await ResolveContactByOidAsync(oid, ct);
            if (byOid.HasValue)
            {
                _logger.LogDebug("[EXT-ACCESS] Resolved oid to Contact {ContactId} via sprk_externalobjectid", byOid.Value);
                return byOid;
            }
        }

        // 2. First-login email fallback (no Contact bound to this oid yet).
        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        var (contactId, existingOid) = await ResolveContactRowByEmailAsync(email, ct);
        if (!contactId.HasValue)
        {
            return null;
        }

        if (string.IsNullOrEmpty(existingOid))
        {
            // Unbound Contact — bind the incoming oid so subsequent logins resolve by the stable key.
            // A bind failure is non-fatal: this login still resolves, and the next login retries the bind.
            if (!string.IsNullOrEmpty(oid))
            {
                await BindOidToContactAsync(contactId.Value, oid!, ct);
            }
            return contactId;
        }

        // Contact is already bound to an oid. Only grant if it matches the incoming oid — never let an
        // email match override an existing (different) oid binding (Amendment A1: oid is authoritative).
        if (!string.IsNullOrEmpty(oid) && string.Equals(existingOid, oid, StringComparison.OrdinalIgnoreCase))
        {
            return contactId;
        }

        _logger.LogWarning(
            "[EXT-ACCESS] Email {Email} matches a Contact already bound to a different oid — access denied (no email hijack of a bound Contact).",
            email);
        return null;
    }

    /// <summary>
    /// Resolves a Contact GUID by the stable CIAM <c>oid</c> (Contact.sprk_externalobjectid).
    /// </summary>
    public async Task<Guid?> ResolveContactByOidAsync(string oid, CancellationToken ct = default)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            // OData string literal: double single quotes, then URL-encode.
            var encodedOid = Uri.EscapeDataString(oid.Replace("'", "''"));
            var query = $"{apiUrl}/contacts?$filter=sprk_externalobjectid eq '{encodedOid}'&$select=contactid&$top=1";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-ACCESS] Failed to resolve Contact by oid: {Status}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ContactRow>>(ct);
            return result?.Value?.FirstOrDefault()?.contactid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error resolving Contact by oid");
            return null;
        }
    }

    /// <summary>
    /// Resolves a Contact GUID by querying contacts.emailaddress1. Retained for the first-login
    /// fallback path; delegates to <see cref="ResolveContactRowByEmailAsync"/>.
    /// </summary>
    public async Task<Guid?> ResolveContactByEmailAsync(string email, CancellationToken ct = default)
    {
        var (contactId, _) = await ResolveContactRowByEmailAsync(email, ct);
        return contactId;
    }

    /// <summary>
    /// Queries a Contact by email, returning both the Contact id and its current oid binding
    /// (<c>sprk_externalobjectid</c>, null when unbound) so callers can enforce the no-hijack rule.
    /// </summary>
    private async Task<(Guid? ContactId, string? ExistingOid)> ResolveContactRowByEmailAsync(string email, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            // OData string literal: double single quotes, then URL-encode.
            var encodedEmail = Uri.EscapeDataString(email.Replace("'", "''"));
            var query = $"{apiUrl}/contacts?$filter=emailaddress1 eq '{encodedEmail}'&$select=contactid,sprk_externalobjectid&$top=1";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-ACCESS] Failed to resolve Contact by email {Email}: {Status}",
                    email, response.StatusCode);
                return (null, null);
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ContactRow>>(ct);
            var row = result?.Value?.FirstOrDefault();

            if (row?.contactid is not null)
                _logger.LogDebug("[EXT-ACCESS] Resolved email {Email} to Contact {ContactId} (oid bound: {Bound})",
                    email, row.contactid, !string.IsNullOrEmpty(row.sprk_externalobjectid));

            return (row?.contactid, row?.sprk_externalobjectid);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error resolving Contact by email {Email}", email);
            return (null, null);
        }
    }

    /// <summary>
    /// Binds the CIAM <c>oid</c> onto a Contact's <c>sprk_externalobjectid</c> at first login.
    /// Update-only (<c>If-Match: *</c>) so a missing Contact is never accidentally created.
    /// Non-fatal on failure — the caller still resolves this login and the bind is retried next time.
    /// </summary>
    private async Task BindOidToContactAsync(Guid contactId, string oid, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            var url = $"{apiUrl}/contacts({contactId})";
            using var request = new HttpRequestMessage(HttpMethod.Patch, url)
            {
                Content = JsonContent.Create(new Dictionary<string, string> { ["sprk_externalobjectid"] = oid })
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("If-Match", "*"); // update-only — do not upsert-create
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("[EXT-ACCESS] Bound oid to Contact {ContactId} (first-login).", contactId);
            }
            else
            {
                _logger.LogWarning("[EXT-ACCESS] Failed to bind oid to Contact {ContactId}: {Status}. Will retry next login.",
                    contactId, response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-ACCESS] Error binding oid to Contact {ContactId}. Non-fatal.", contactId);
        }
    }

    // ── Root-record veto flags (task 037 · FR-21 / FR-22) ────────────────────────────────────────

    /// <summary>
    /// Collection name + primary-key attribute for each root entity that carries the veto flags.
    /// <b>Verified against live Dataverse metadata 2026-09-04</b>: all three carry BOTH
    /// <c>sprk_issecure</c> (BIT) and <c>sprk_accesspermission</c> (CHOICE), with identical option sets.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, (string Collection, string IdAttribute)> RootFlagSources =
        new Dictionary<string, (string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_project"] = ("sprk_projects", "sprk_projectid"),
            ["sprk_matter"] = ("sprk_matters", "sprk_matterid"),
            ["sprk_workassignment"] = ("sprk_workassignments", "sprk_workassignmentid"),
        };

    /// <summary>
    /// The <c>sprk_accesspermission</c> option value meaning RESTRICTED.
    /// <b>Verified live 2026-09-04</b> on all three root entities (Standard 100000000 / Limited 100000001 /
    /// Restricted 100000002).
    /// </summary>
    /// <remarks>
    /// The task brief cited <c>TrackingFieldTrio/index.ts</c> for this number, but that file documents the
    /// <c>sprk_communication</c> option set and says so explicitly ("entity-specific: lives ONLY here …").
    /// The value happens to match on all three roots — established by querying metadata, not by trusting
    /// the citation.
    /// </remarks>
    internal const int AccessPermissionRestricted = 100000002;

    /// <summary>Ids per flag query. Bounded so a large candidate set cannot produce an over-length URL.</summary>
    private const int FlagQueryChunkSize = 50;

    /// <summary>
    /// Reads the veto flags for a batch of root records (NFR-02: batched — never a per-record round trip).
    /// </summary>
    /// <remarks>
    /// <b>Fail-closed, per NFR-01.</b> Every id the caller asked about is present in the returned map. An id
    /// the query did not return — deleted, filtered, or invisible to the app-only identity — is
    /// indistinguishable from a read that failed, so it comes back as <b>secure AND restricted</b>. That is
    /// the deny direction: unknown flags suppress derived terms and veto contact-sourced rights, rather than
    /// defaulting a record to open. A transport fault or non-success status does the same for the whole chunk.
    /// <para>
    /// An entity type with no flag columns returns an empty map, meaning "no vetoes apply" — that is a
    /// STATIC fact about the schema (verified above), not a failed read, so it is not a fail-closed case.
    /// </para>
    /// <para>Virtual for the same test seam the rest of this class uses (subclass + override).</para>
    /// </remarks>
    public virtual async Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetRootRecordFlagsAsync(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
    {
        if (recordIds is null || recordIds.Count == 0)
        {
            return new Dictionary<Guid, RootRecordFlags>();
        }

        if (!RootFlagSources.TryGetValue(entityType ?? string.Empty, out var source))
        {
            // Not a flag-bearing root type. No veto applies — see the remarks.
            return new Dictionary<Guid, RootRecordFlags>();
        }

        var distinct = recordIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, RootRecordFlags>();
        }

        var flags = new Dictionary<Guid, RootRecordFlags>();

        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            for (var offset = 0; offset < distinct.Count; offset += FlagQueryChunkSize)
            {
                var chunk = distinct.Skip(offset).Take(FlagQueryChunkSize).ToList();
                var idFilter = string.Join(" or ", chunk.Select(id => $"{source.IdAttribute} eq {id}"));
                var query = $"{apiUrl}/{source.Collection}" +
                            $"?$filter=({idFilter})" +
                            $"&$select={source.IdAttribute},sprk_issecure,sprk_accesspermission";

                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("OData-MaxVersion", "4.0");
                request.Headers.Add("OData-Version", "4.0");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "[EXT-ACCESS] Root-flag query FAILED for {EntityType} ({Count} ids): {Status}. "
                        + "Failing CLOSED — every id in this chunk is treated as secure AND restricted (NFR-01).",
                        entityType, chunk.Count, response.StatusCode);
                    foreach (var id in chunk)
                    {
                        flags[id] = RootRecordFlags.Unreadable;
                    }
                    continue;
                }

                var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<RootFlagRow>>(ct);
                var byId = (result?.Value ?? new List<RootFlagRow>())
                    .GroupBy(r => r.GetId(source.IdAttribute))
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var id in chunk)
                {
                    flags[id] = byId.TryGetValue(id, out var row)
                        ? new RootRecordFlags(
                            IsSecure: row.sprk_issecure == true,
                            IsRestricted: row.sprk_accesspermission == AccessPermissionRestricted)
                        // Asked about, not returned. Cannot be distinguished from an unreadable row.
                        : RootRecordFlags.Unreadable;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-ACCESS] Root-flag query threw for {EntityType}. Failing CLOSED — all {Count} ids "
                + "treated as secure AND restricted (NFR-01).", entityType, distinct.Count);
            foreach (var id in distinct)
            {
                flags[id] = RootRecordFlags.Unreadable;
            }
        }

        return flags;
    }

    /// <summary>Projection of the flag columns. Ids arrive as strings over OData.</summary>
    private sealed class RootFlagRow
    {
        public string? sprk_projectid { get; set; }
        public string? sprk_matterid { get; set; }
        public string? sprk_workassignmentid { get; set; }
        public bool? sprk_issecure { get; set; }
        public int? sprk_accesspermission { get; set; }

        public Guid GetId(string idAttribute)
        {
            var raw = idAttribute switch
            {
                "sprk_projectid" => sprk_projectid,
                "sprk_matterid" => sprk_matterid,
                "sprk_workassignmentid" => sprk_workassignmentid,
                _ => null,
            };
            return Guid.TryParse(raw, out var id) ? id : Guid.Empty;
        }
    }

    private async Task<ExternalGrantSet> QueryGrantSetAsync(Guid contactId, CancellationToken ct)
    {
        try
        {
            var token = await GetAppOnlyTokenAsync(ct);
            var apiUrl = GetDataverseApiUrl();

            // Query ALL active grants for this Contact across every root type (task 028 — polymorphic).
            // A grant row targets exactly ONE root via its typed lookup (verified live):
            //   _sprk_project_value / _sprk_matter_value / _sprk_workassignment_value.
            // (Dataverse projects lookups as _sprk_{name}_value; the contact FK is _sprk_contact_value —
            // verified against live Dataverse.) sprk_invoice grants are intentionally NOT read (design §6
            // — child access derives from an accessible root, not a direct child grant).
            // Expiry is enforced HERE, in the $filter (task 007 / FR-06) — see ExpiryPredicate.
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter={BuildContactGrantFilter(contactId, TodayUtc)}" +
                        $"&$select={GrantRowSelect}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[EXT-ACCESS] Dataverse query failed for Contact {ContactId}: {Status}",
                    contactId, response.StatusCode);
                return ExternalGrantSet.Empty;
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ExternalAccessRow>>(ct);
            var rows = result?.Value ?? new List<ExternalAccessRow>();

            // Partition each grant into its root bucket by which typed lookup is populated. A project
            // grant keeps its access level; matter/WA grants contribute an id only.
            // Task 037 (FR-22): DIRECT rows carry their level in BOTH slots. `DirectAccessLevel` is what
            // survives Secure suppression; `AccessLevel` stays the all-sources effective level.
            var projects = rows
                .Where(r => r._sprk_project_value.HasValue && r.sprk_accesslevel.HasValue)
                .Select(r => new ExternalParticipation
                {
                    ProjectId = r._sprk_project_value!.Value,
                    AccessLevel = (ExternalAccessLevel)r.sprk_accesslevel!.Value,
                    DirectAccessLevel = (ExternalAccessLevel)r.sprk_accesslevel!.Value
                })
                .ToList();
            // Task 032 (FR-19): matter/WA grants now KEEP the level that was already on the row —
            // GrantRowSelect has always $select'ed sprk_accesslevel; the partitioning simply discarded
            // it, which is why these root types had no level anywhere downstream (register A-8 / B-8).
            //
            // ⚠️ NOTE THE ASYMMETRY WITH `projects` ABOVE, WHICH IS DELIBERATE. The project filter
            // requires `sprk_accesslevel.HasValue` and drops rows without one. Copying that here would
            // read as tidy symmetry and would be a SILENT REVOCATION: a matter/WA row with a null level
            // grants access today, and would stop granting it. So the level is carried as NULLABLE and
            // the row is kept — set membership is unchanged, and a null level contributes
            // AccessRights.None, which the highest-wins max cannot widen.
            var matters = rows
                .Where(r => r._sprk_matter_value.HasValue)
                .Select(r => new ExternalRootGrant
                {
                    RecordId = r._sprk_matter_value!.Value,
                    AccessLevel = (ExternalAccessLevel?)r.sprk_accesslevel,
                    DirectAccessLevel = (ExternalAccessLevel?)r.sprk_accesslevel
                })
                .ToList();
            var workAssignments = rows
                .Where(r => r._sprk_workassignment_value.HasValue)
                .Select(r => new ExternalRootGrant
                {
                    RecordId = r._sprk_workassignment_value!.Value,
                    AccessLevel = (ExternalAccessLevel?)r.sprk_accesslevel,
                    DirectAccessLevel = (ExternalAccessLevel?)r.sprk_accesslevel
                })
                .ToList();

            // Term 3 (task 073 #7): union ORGANIZATION grants — records granted to any organization the
            // contact is an ACTIVE member of (sprk_contactorganization junction). This mirrors the
            // standing-grant runtime union: no per-contact rows exist, membership is resolved live, and
            // staleness is bounded by the 60s cache TTL. Fail-closed by construction — a junction or
            // org-grant read fault returns an empty list and contributes nothing, never 500s the authz path.
            var orgRows = await QueryOrganizationGrantRowsAsync(contactId, token, apiUrl, ct);
            if (orgRows.Count > 0)
            {
                // Task 037 (FR-22): ORG-INHERITED rows leave DirectAccessLevel NULL. That null is the
                // provenance marker Secure suppression reads — an org row contributes to the effective
                // level but never to the direct one.
                projects.AddRange(orgRows
                    .Where(r => r._sprk_project_value.HasValue && r.sprk_accesslevel.HasValue)
                    .Select(r => new ExternalParticipation
                    {
                        ProjectId = r._sprk_project_value!.Value,
                        AccessLevel = (ExternalAccessLevel)r.sprk_accesslevel!.Value,
                        DirectAccessLevel = null
                    }));
                foreach (var r in orgRows.Where(r => r._sprk_matter_value.HasValue))
                    matters.Add(new ExternalRootGrant
                    {
                        RecordId = r._sprk_matter_value!.Value,
                        AccessLevel = (ExternalAccessLevel?)r.sprk_accesslevel,
                        DirectAccessLevel = null
                    });
                foreach (var r in orgRows.Where(r => r._sprk_workassignment_value.HasValue))
                    workAssignments.Add(new ExternalRootGrant
                    {
                        RecordId = r._sprk_workassignment_value!.Value,
                        AccessLevel = (ExternalAccessLevel?)r.sprk_accesslevel,
                        DirectAccessLevel = null
                    });
            }

            // Dedupe project grants by id, keeping the HIGHEST access level — a contact may hold a direct
            // project grant AND inherit one via an org grant; the strongest level wins (the enum orders
            // ViewOnly < Collaborate < FullAccess).
            //
            // ⚠️ Task 037: the dedupe MUST carry both levels forward. Collapsing to a single max would
            // destroy exactly what Secure suppression needs — a ViewOnly DIRECT grant plus a Collaborate
            // ORG grant would become "Collaborate", and once the org term is suppressed there would be no
            // ViewOnly left to fall back to. `Max` over the nullable direct level skips org rows (null) and
            // yields null only when EVERY contributing row was org-inherited.
            projects = projects
                .GroupBy(p => p.ProjectId)
                .Select(g => new ExternalParticipation
                {
                    ProjectId = g.Key,
                    AccessLevel = g.Max(x => x.AccessLevel),
                    DirectAccessLevel = g.Max(x => x.DirectAccessLevel)
                })
                .ToList();

            // Task 032: the SAME highest-wins rule now applies to matters + work assignments, for the
            // same reason — the org-grant union above adds rows from a SECOND source, so one id can
            // arrive twice at different levels.
            //
            // This was invisible before: both were HashSet<Guid>, so duplicates silently collapsed and
            // no level could disagree. Duplicates are real, not theoretical — one dev contact holds FIVE
            // active grant rows on a single matter. Without this, once levels are carried the answer for
            // such an id would depend on ROW ORDER.
            //
            // `Max` over `ExternalAccessLevel?` ignores nulls and yields null only when EVERY row for
            // that id lacks a level, which maps to AccessRights.None — fail-closed, and never a level
            // invented for a row that had none.
            matters = DedupeByHighestLevel(matters);
            workAssignments = DedupeByHighestLevel(workAssignments);

            _logger.LogInformation(
                "[EXT-ACCESS] Loaded grants for Contact {ContactId}: {Projects} project / {Matters} matter / {Was} work-assignment (incl. {OrgRows} org-grant rows)",
                contactId, projects.Count, matters.Count, workAssignments.Count, orgRows.Count);

            return new ExternalGrantSet
            {
                Projects = projects,
                MatterGrants = matters,
                WorkAssignmentGrants = workAssignments,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying Dataverse for Contact {ContactId}", contactId);
            return ExternalGrantSet.Empty;
        }
    }

    /// <summary>
    /// Collapses repeated grants on one record id to a single grant at the HIGHEST level (task 032).
    /// The non-project generalization of the <c>projects</c> <c>GroupBy(...).Max(...)</c> rule above.
    /// </summary>
    private static List<ExternalRootGrant> DedupeByHighestLevel(IEnumerable<ExternalRootGrant> grants) =>
        grants
            .GroupBy(g => g.RecordId)
            .Select(g => new ExternalRootGrant
            {
                RecordId = g.Key,
                // Max over a nullable enum skips nulls; all-null yields null -> AccessRights.None.
                AccessLevel = g.Max(x => x.AccessLevel),
                // Task 037: same rule for the direct-only level — null iff every row was org-inherited.
                DirectAccessLevel = g.Max(x => x.DirectAccessLevel)
            })
            .ToList();

    /// <summary>
    /// Term 3 (task 073 #7) — the ORGANIZATION-grant rows a contact inherits: active org grants (contact
    /// empty) for every organization the contact is an ACTIVE member of (<c>sprk_contactorganization</c>).
    /// Two reads (memberships → org grants); fail-closed at every step (an empty list on any fault so an
    /// org-side read problem NEVER widens NOR 500s the authz decision). Returns the org-grant rows in the
    /// same shape as per-contact grants so the caller unions them identically.
    /// </summary>
    private async Task<List<ExternalAccessRow>> QueryOrganizationGrantRowsAsync(
        Guid contactId, string token, string apiUrl, CancellationToken ct)
    {
        var orgIds = await QueryActiveOrgIdsAsync(contactId, token, apiUrl, ct);
        if (orgIds.Count == 0)
            return new List<ExternalAccessRow>();

        try
        {
            // Active, UNEXPIRED org grants (sprk_Contact EMPTY — the org-grant marker) for any of the
            // contact's orgs. An org grant expires exactly like a person grant: leaving the predicate off
            // this second path would let every contact keep expired access simply by holding it through
            // their firm, which is the same finding wearing a different lookup.
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter={BuildOrganizationGrantFilter(orgIds, TodayUtc)}" +
                        $"&$select={GrantRowSelect}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[EXT-ACCESS] Org-grant query failed for Contact {ContactId} ({OrgCount} orgs): {Status}",
                    contactId, orgIds.Count, response.StatusCode);
                return new List<ExternalAccessRow>();
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ExternalAccessRow>>(ct);
            return result?.Value ?? new List<ExternalAccessRow>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying org grants for Contact {ContactId}", contactId);
            return new List<ExternalAccessRow>();
        }
    }

    /// <summary>
    /// The <c>sprk_organization</c> ids the contact is an ACTIVE member of, from the
    /// <c>sprk_contactorganization</c> junction (<c>statecode eq 0</c> = active membership; a former
    /// member is a deactivated row and is excluded, so leaving a firm drops inherited access). Fail-closed
    /// to an empty list. NOTE: assumes the junction's lookup logical names are <c>sprk_contact</c> /
    /// <c>sprk_organization</c> (→ <c>_sprk_contact_value</c> / <c>_sprk_organization_value</c>), matching
    /// the grant table's convention — confirm against the created junction schema.
    /// </summary>
    private async Task<List<Guid>> QueryActiveOrgIdsAsync(
        Guid contactId, string token, string apiUrl, CancellationToken ct)
    {
        try
        {
            var query = $"{apiUrl}/sprk_contactorganizations" +
                        $"?$filter=_sprk_contact_value eq {contactId} and statecode eq 0" +
                        $"&$select=_sprk_organization_value";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "[EXT-ACCESS] Contact-organization membership query failed for Contact {ContactId}: {Status}",
                    contactId, response.StatusCode);
                return new List<Guid>();
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ContactOrgRow>>(ct);
            return (result?.Value ?? new List<ContactOrgRow>())
                .Where(r => r._sprk_organization_value.HasValue)
                .Select(r => r._sprk_organization_value!.Value)
                .Distinct()
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying contact-organization memberships for Contact {ContactId}", contactId);
            return new List<Guid>();
        }
    }

    private async Task CacheGrantSetAsync(
        string tenantId,
        string idComponent,
        ExternalGrantSet grantSet)
    {
        try
        {
            var cached = new CachedGrantSet
            {
                Projects = grantSet.Projects
                    .Select(p => new CachedParticipation { ProjectId = p.ProjectId, AccessLevel = (int)p.AccessLevel })
                    .ToList(),
                // Task 032: persist matter/WA LEVELS, not just ids. Writing ids here (the prior shape)
                // is what would have made rights correct on a miss and None on a hit.
                MatterGrants = grantSet.MatterGrants
                    .Select(g => new CachedRootGrant { RecordId = g.RecordId, AccessLevel = (int?)g.AccessLevel })
                    .ToList(),
                WorkAssignmentGrants = grantSet.WorkAssignmentGrants
                    .Select(g => new CachedRootGrant { RecordId = g.RecordId, AccessLevel = (int?)g.AccessLevel })
                    .ToList(),
            };

            await _cache.SetAsync(
                tenantId, ExternalAccessResource, idComponent, CacheVersion,
                cached, CacheTtl);

            _logger.LogDebug(
                "[EXT-ACCESS] Cached grants for Contact {ContactId} (TTL: {Ttl}s): {Projects}p/{Matters}m/{Was}w",
                idComponent, CacheTtl.TotalSeconds, cached.Projects.Count, cached.MatterGrants.Count, cached.WorkAssignmentGrants.Count);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[EXT-ACCESS] Error caching grants for Contact {ContactId}. Non-critical.", idComponent);
        }
    }

    private async Task<string> GetAppOnlyTokenAsync(CancellationToken ct)
    {
        if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
            return _currentToken.Value.Token;

        if (!await _tokenSemaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new TimeoutException("Timed out waiting for Dataverse token");

        try
        {
            if (_currentToken != null && _currentToken.Value.ExpiresOn > DateTimeOffset.UtcNow.AddMinutes(5))
                return _currentToken.Value.Token;

            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
                ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");

            var scope = $"{dataverseUrl.TrimEnd('/')}/.default";
            _currentToken = await _credential.GetTokenAsync(new TokenRequestContext(new[] { scope }), ct);
            return _currentToken.Value.Token;
        }
        finally
        {
            _tokenSemaphore.Release();
        }
    }

    private string GetDataverseApiUrl()
    {
        var dataverseUrl = _configuration["Dataverse:ServiceUrl"]
            ?? throw new InvalidOperationException("Dataverse:ServiceUrl is required");
        return $"{dataverseUrl.TrimEnd('/')}/api/data/v9.2";
    }

    // DTO types for Dataverse OData responses

    private sealed class DataverseQueryResult<T>
    {
        [JsonPropertyName("value")]
        public List<T>? Value { get; set; }
    }

    private sealed class ExternalAccessRow
    {
        [JsonPropertyName("_sprk_project_value")]
        public Guid? _sprk_project_value { get; set; }

        [JsonPropertyName("_sprk_matter_value")]
        public Guid? _sprk_matter_value { get; set; }

        [JsonPropertyName("_sprk_workassignment_value")]
        public Guid? _sprk_workassignment_value { get; set; }

        [JsonPropertyName("sprk_accesslevel")]
        public int? sprk_accesslevel { get; set; }
    }

    /// <summary>A `sprk_contactorganization` junction row — projects the org lookup value (task 073 #7).</summary>
    private sealed class ContactOrgRow
    {
        [JsonPropertyName("_sprk_organization_value")]
        public Guid? _sprk_organization_value { get; set; }
    }

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")]
        public Guid? contactid { get; set; }

        [JsonPropertyName("sprk_externalobjectid")]
        public string? sprk_externalobjectid { get; set; }
    }

    private sealed class CachedParticipation
    {
        public Guid ProjectId { get; set; }
        public int AccessLevel { get; set; }

        public ExternalParticipation ToParticipation() => new()
        {
            ProjectId = ProjectId,
            AccessLevel = (ExternalAccessLevel)AccessLevel
        };
    }

    /// <summary>
    /// A cached non-project (matter / work-assignment) grant: id + level (task 032).
    /// <c>AccessLevel</c> is nullable for the same reason <see cref="ExternalRootGrant"/>'s is.
    /// </summary>
    private sealed class CachedRootGrant
    {
        public Guid RecordId { get; set; }
        public int? AccessLevel { get; set; }

        public ExternalRootGrant ToGrant() => new()
        {
            RecordId = RecordId,
            AccessLevel = (ExternalAccessLevel?)AccessLevel
        };
    }

    /// <summary>
    /// The cached grant-set shape.
    /// <para>
    /// 🔴 Task 032 fixed a defect that would otherwise have shipped GREEN. This type stored projects as
    /// (id + level) but matters/WAs as bare <c>List&lt;Guid&gt;</c>. Carrying levels only on the QUERY
    /// path would therefore have produced correct matter rights on a cache MISS and
    /// <c>AccessRights.None</c> on a cache HIT — i.e. for most of every 60-second TTL — while the unit
    /// suite stayed green, because unit tests bypass the cache entirely. Silent, intermittent, and
    /// invisible to CI.
    /// </para>
    /// <para>
    /// <b><see cref="CacheVersion"/> MUST be bumped whenever this shape changes</b> (3 → 4 here).
    /// Without the bump, entries written under the old shape deserialize into the new one with levels
    /// absent, reproducing exactly the bug above for one TTL after every deploy.
    /// </para>
    /// </summary>
    private sealed class CachedGrantSet
    {
        public List<CachedParticipation> Projects { get; set; } = new();
        public List<CachedRootGrant> MatterGrants { get; set; } = new();
        public List<CachedRootGrant> WorkAssignmentGrants { get; set; } = new();

        public ExternalGrantSet ToGrantSet() => new()
        {
            Projects = Projects.Select(p => p.ToParticipation()).ToList(),
            MatterGrants = MatterGrants.Select(g => g.ToGrant()).ToList(),
            WorkAssignmentGrants = WorkAssignmentGrants.Select(g => g.ToGrant()).ToList(),
        };
    }
}
