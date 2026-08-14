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
    public const int CacheVersion = 3;

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
                        contactId, cached.Projects.Count, cached.Matters.Count, cached.WorkAssignments.Count);
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
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter=_sprk_contact_value eq {contactId} and statecode eq 0" +
                        $"&$select=_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,sprk_accesslevel";

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
            var projects = rows
                .Where(r => r._sprk_project_value.HasValue && r.sprk_accesslevel.HasValue)
                .Select(r => new ExternalParticipation
                {
                    ProjectId = r._sprk_project_value!.Value,
                    AccessLevel = (ExternalAccessLevel)r.sprk_accesslevel!.Value
                })
                .ToList();
            var matters = rows
                .Where(r => r._sprk_matter_value.HasValue)
                .Select(r => r._sprk_matter_value!.Value)
                .ToHashSet();
            var workAssignments = rows
                .Where(r => r._sprk_workassignment_value.HasValue)
                .Select(r => r._sprk_workassignment_value!.Value)
                .ToHashSet();

            // Term 3 (task 073 #7): union ORGANIZATION grants — records granted to any organization the
            // contact is an ACTIVE member of (sprk_contactorganization junction). This mirrors the
            // standing-grant runtime union: no per-contact rows exist, membership is resolved live, and
            // staleness is bounded by the 60s cache TTL. Fail-closed by construction — a junction or
            // org-grant read fault returns an empty list and contributes nothing, never 500s the authz path.
            var orgRows = await QueryOrganizationGrantRowsAsync(contactId, token, apiUrl, ct);
            if (orgRows.Count > 0)
            {
                projects.AddRange(orgRows
                    .Where(r => r._sprk_project_value.HasValue && r.sprk_accesslevel.HasValue)
                    .Select(r => new ExternalParticipation
                    {
                        ProjectId = r._sprk_project_value!.Value,
                        AccessLevel = (ExternalAccessLevel)r.sprk_accesslevel!.Value
                    }));
                foreach (var r in orgRows.Where(r => r._sprk_matter_value.HasValue))
                    matters.Add(r._sprk_matter_value!.Value);
                foreach (var r in orgRows.Where(r => r._sprk_workassignment_value.HasValue))
                    workAssignments.Add(r._sprk_workassignment_value!.Value);
            }

            // Dedupe project grants by id, keeping the HIGHEST access level — a contact may hold a direct
            // project grant AND inherit one via an org grant; the strongest level wins (the enum orders
            // ViewOnly < Collaborate < FullAccess).
            projects = projects
                .GroupBy(p => p.ProjectId)
                .Select(g => new ExternalParticipation { ProjectId = g.Key, AccessLevel = g.Max(x => x.AccessLevel) })
                .ToList();

            _logger.LogInformation(
                "[EXT-ACCESS] Loaded grants for Contact {ContactId}: {Projects} project / {Matters} matter / {Was} work-assignment (incl. {OrgRows} org-grant rows)",
                contactId, projects.Count, matters.Count, workAssignments.Count, orgRows.Count);

            return new ExternalGrantSet
            {
                Projects = projects,
                Matters = matters,
                WorkAssignments = workAssignments,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying Dataverse for Contact {ContactId}", contactId);
            return ExternalGrantSet.Empty;
        }
    }

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
            // Active org grants (sprk_Contact EMPTY — the org-grant marker) for any of the contact's orgs.
            var orgFilter = string.Join(" or ", orgIds.Select(id => $"_sprk_organization_value eq {id}"));
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter=({orgFilter}) and _sprk_contact_value eq null and statecode eq 0" +
                        $"&$select=_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,sprk_accesslevel";

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
                Matters = grantSet.Matters.ToList(),
                WorkAssignments = grantSet.WorkAssignments.ToList(),
            };

            await _cache.SetAsync(
                tenantId, ExternalAccessResource, idComponent, CacheVersion,
                cached, CacheTtl);

            _logger.LogDebug(
                "[EXT-ACCESS] Cached grants for Contact {ContactId} (TTL: {Ttl}s): {Projects}p/{Matters}m/{Was}w",
                idComponent, CacheTtl.TotalSeconds, cached.Projects.Count, cached.Matters.Count, cached.WorkAssignments.Count);
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

    private sealed class CachedGrantSet
    {
        public List<CachedParticipation> Projects { get; set; } = new();
        public List<Guid> Matters { get; set; } = new();
        public List<Guid> WorkAssignments { get; set; } = new();

        public ExternalGrantSet ToGrantSet() => new()
        {
            Projects = Projects.Select(p => p.ToParticipation()).ToList(),
            Matters = Matters.ToHashSet(),
            WorkAssignments = WorkAssignments.ToHashSet(),
        };
    }
}
