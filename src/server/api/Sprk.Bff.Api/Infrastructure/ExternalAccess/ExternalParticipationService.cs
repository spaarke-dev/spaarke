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
    // CacheVersion 5 (unified-access-control-r2 task 131 / defect C3): every cached grant now carries
    // DirectAccessLevel as well — the field task 037 (FR-22) added to the grant types WITHOUT a cache
    // change or a bump. Secure suppression reads that field, so a v4 entry (no direct level) turned a
    // legitimate DIRECT grant on a secure root into AccessRights.None on every cache HIT. The same
    // miss/hit split as v4, a third time; GrantCacheRoundTripSeamTests now drives the real cache so the
    // fourth one fails CI instead of production.
    public const int CacheVersion = 5;

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

    /// <summary>
    /// Columns every grant read needs to partition a row into its root bucket — plus the row's
    /// organization lookup, which <see cref="GrantOrganizationConfers"/> needs to tell "no organization"
    /// (unaffected) from "an organization whose state did not come back" (confers nothing). Task 109.
    /// </summary>
    internal const string GrantRowSelect =
        "_sprk_project_value,_sprk_matter_value,_sprk_workassignment_value,sprk_accesslevel,_sprk_organization_value";

    /// <summary>
    /// The <c>$expand</c> that brings the parent <c>sprk_organization</c>'s OWN <c>statecode</c> back with a
    /// row, in the SAME read (task 109 · ISS-026 / #1006 read half). Used by the junction read and by both
    /// grant reads.
    /// </summary>
    /// <remarks>
    /// The navigation property is <c>sprk_Organization</c> (PascalCase) on BOTH tables — live-verified
    /// 2026-09-30 (<c>ManyToOneRelationships.ReferencingEntityNavigationPropertyName</c> on
    /// <c>sprk_contactorganization</c>; the grant writer already binds <c>sprk_Organization@odata.bind</c>).
    /// An expand, not a second query: the parent's state is a column of the row's own read, so the
    /// single-read budget (task 043 / NFR-02) is untouched.
    /// </remarks>
    internal const string OrganizationStateExpand = "sprk_Organization($select=statecode)";

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
    /// Excludes grants whose expiry has passed, AND excludes grants with NO expiry at all — finding
    /// A-5 (spec FR-06); the null-exclusion half is ISS-009 (#974) option A, owner decision D-1
    /// (2026-09-19, <c>projects/unified-access-control-r2/notes/owner-decision-brief-2026-09-18.md</c>).
    /// </summary>
    /// <remarks>
    /// <para><b>What was wrong (task 007).</b> <c>sprk_expiresdate</c> was written at grant time and read
    /// <i>nowhere</i>: it appeared in no <c>$filter</c> and no <c>$select</c> on any path, and there is
    /// no sweep job. A grant whose expiry had passed conferred full access forever, while the Manage
    /// Access UI presented expiry as a working control. A promise-shaped no-op.</para>
    ///
    /// <para><b>The null branch is now EXCLUSIONARY — inverted from task 007 (ISS-009 / D-1).</b> Task
    /// 007 added an explicit <c>eq null</c> branch because, at the time, the BFF was every writer of this
    /// column and always supplied a date only later (the branch existed to avoid a false outage on
    /// existing undated rows during that transition). Since task 097 the BFF itself never writes a grant
    /// without <c>sprk_expiresdate</c> — but the column is optional in Dataverse and users hold Create on
    /// <c>sprk_externalrecordaccess</c>, so a row created outside the BFF (a form, the Web API, a flow or
    /// an import) can still omit it, and the old rule let that row confer access FOREVER. The owner
    /// ruled out every Dataverse-side fix (D-1: "we do not use Dataverse plugins") and closed the gap
    /// from the read side instead: a null <c>sprk_expiresdate</c> now confers NOTHING. Undated moves from
    /// fail-OPEN to fail-CLOSED (ADR-003) — in OData, <c>field ge X</c> already excludes nulls, so
    /// dropping the <c>eq null</c> disjunct is the entire change.</para>
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
        => $"sprk_expiresdate ge {today:yyyy-MM-dd}";

    /// <summary>
    /// The IN-MEMORY mirror of <see cref="ExpiryPredicate"/>: does a grant carrying
    /// <paramref name="expiresDate"/> still confer access on <paramref name="today"/>?
    /// </summary>
    /// <remarks>
    /// <para><b>Deliberately adjacent to the OData form, and the only in-memory expiry test in the
    /// external-access write path</b> (task 106). The write path must answer the same question the read
    /// filter answers — "does this row confer access" — but it holds materialized rows rather than a
    /// <c>$filter</c>, so it cannot reuse the string. Two independent definitions of "expired" is
    /// precisely the drift that would let <c>/grant</c> report an outcome the reader contradicts, which is
    /// finding A-5's shape. So this sits next to the predicate it mirrors, and
    /// <c>GrantExpiryCharacterizationTests</c> pins it to the same semantics: <c>null</c> confers NOTHING
    /// (task 107, ISS-009 / D-1, inverted from task 007's original "null never expires"), and the expiry
    /// date ITSELF still confers (<c>ge</c>, not <c>gt</c> — task 007's Date Only rule).</para>
    ///
    /// <para>Review finding F3/W8 found the original "the only in-memory copy" claim was false:
    /// <c>SetRecordShareExpiryEndpoint</c> already compared expiry in memory for a log count. Rather than
    /// narrow the claim, that call site was routed through here, so the claim is true by construction.
    /// (<c>GrantExpiryReminderJob</c>'s days-until-expiry arithmetic is NOT a mirror — it answers a
    /// different question.) The nullable parameter mirrors the nullable column so a caller holding a raw
    /// row can use it; the grant path itself always passes a resolved value.</para>
    /// </remarks>
    internal static bool ConfersAccessOn(DateOnly? expiresDate, DateOnly today)
        => expiresDate is not null && expiresDate.Value >= today;

    // ─────────────────────────────────────────────────────────────────────────
    // The ONE organization-membership read (task 109 — ISS-019 #998, ISS-020 #999, ISS-026 #1006 read
    // half, owner decisions D-2 and D-10)
    // ─────────────────────────────────────────────────────────────────────────
    //
    // One junction read feeds consumers whose safe failure directions are OPPOSITE — the additive
    // org terms (over-inclusion = over-GRANT) and the FR-23 deny-veto subject (over-inclusion = a
    // stricter wall). That was only ever a dilemma because the read projected bare ids. It is one
    // READ, not one FILTER: the $filter below is the WALL's (statecode only), and the conferring set is
    // narrowed from the same rows IN MEMORY. A second query for either consumer would re-introduce the
    // two-snapshot hazard task 043 removed.

    /// <summary>
    /// The junction <c>$filter</c>: the contact's memberships whose own <c>statecode</c> is ACTIVE — and
    /// NOTHING else.
    /// </summary>
    /// <remarks>
    /// <para><b>🔴 No date term, deliberately, and none may be added here.</b> This filter defines the
    /// FR-23 wall-subject set as well as the superset the conferring set is narrowed from. A date bound in
    /// the <c>$filter</c> would narrow the WALL too — a fail-OPEN change to a veto, which owner decision
    /// D-2 part 2 (end date) and D-10 (start date) both forbid: an org-keyed ethical wall keeps binding a
    /// former member, and a not-yet-started one. The date bounds live in
    /// <see cref="MembershipConfersOn"/>, applied to the conferring set only.</para>
    /// <para><b>Null <c>statecode</c> is ACTIVE</b> — <c>ExternalGrantRow.IsActive</c>'s semantics, and
    /// task 117's reconciliation scan's. A bare <c>statecode eq 0</c> excludes nulls in OData, so the
    /// disjunction is explicit.</para>
    /// </remarks>
    internal static string BuildOrganizationMembershipFilter(Guid contactId)
        => $"_sprk_contact_value eq {contactId} and (statecode eq 0 or statecode eq null)";

    /// <summary>
    /// Columns of the junction read: the organization, BOTH date bounds (task 109 / D-2 + D-10), and the
    /// row's own state (re-decided in code — see <see cref="ProjectOrganizationMemberships"/>).
    /// </summary>
    internal const string OrganizationMembershipSelect =
        "_sprk_organization_value,sprk_startdate,sprk_enddate,statecode";

    /// <summary>
    /// The IN-MEMORY conferring bound on a membership's dates: does a membership dated
    /// <paramref name="startDate"/>..<paramref name="endDate"/> confer access on <paramref name="today"/>?
    /// </summary>
    /// <remarks>
    /// <para><b>Mirrors <see cref="ExpiryPredicate"/>'s MECHANICS, not task 107's null inversion.</b> Both
    /// columns are <b>Date Only</b> with <c>DateOnly</c> behaviour (live-verified 2026-09-30:
    /// <c>DateTimeAttributeMetadata.Format = DateOnly</c>, <c>DateTimeBehavior = DateOnly</c>), so both
    /// boundaries are INCLUSIVE — access holds ON the start date (owner D-10: "confers as of the access
    /// date") and THROUGH the end date (D-2 part 1; the same <c>ge</c> boundary <c>ExpiryPredicate</c>
    /// uses, and the complement of task 117's strict <c>lt</c> deactivation rule, so the reader and the
    /// writer agree on which day a membership ends).</para>
    /// <para>🔴 <b>A NULL bound means UNBOUNDED, and both null branches are load-bearing.</b> Task 107
    /// inverted the null branch for a GRANT's <c>sprk_expiresdate</c>, so an undated grant confers nothing
    /// — correct for a grant, which is a privilege that must be bounded. A membership is not a grant: no
    /// start date and no end date is simply an ordinary current membership. "Being consistent" across the
    /// date columns here would silently revoke every open-ended membership in the system.</para>
    /// <para>Applied to the CONFERRING set only — never to the wall (see
    /// <see cref="BuildOrganizationMembershipFilter"/>).</para>
    /// </remarks>
    internal static bool MembershipConfersOn(DateOnly? startDate, DateOnly? endDate, DateOnly today)
        => (startDate is null || startDate.Value <= today)
           && (endDate is null || endDate.Value >= today);

    /// <summary>Active means <c>statecode</c> 0 OR null — <c>ExternalGrantRow.IsActive</c>'s semantics.</summary>
    internal static bool IsActiveState(int? stateCode) => stateCode is null or 0;

    /// <summary>
    /// Is the parent <c>sprk_organization</c>, as expanded onto a row, ACTIVE (task 109 · ISS-026)?
    /// </summary>
    /// <remarks>
    /// <para>An expanded organization whose <c>statecode</c> is null is ACTIVE
    /// (<see cref="IsActiveState"/>). An organization that did not come back at all — the lookup is set but
    /// the expand is absent — is NOT: its state is unknown, and on a CONFERRING path unknown confers
    /// nothing (ADR-003 fail-closed; the same rule <see cref="RootRecordFlags.Unreadable"/> applies to an
    /// id the flag read did not return).</para>
    /// <para>Deliberately the opposite of task 117's WRITER, which deactivates only on a CONFIRMED
    /// <c>statecode = 1</c>. A writer removing access on an unknown would be the fail-open direction for a
    /// write; a reader granting on one would be the fail-open direction for a read.</para>
    /// </remarks>
    internal static bool OrganizationIsActive(OrganizationStateRow? organization)
        => organization is not null && IsActiveState(organization.StateCode);

    /// <summary>
    /// The ISS-026 read guard for a GRANT row: does the row's organization association let it confer?
    /// </summary>
    /// <remarks>
    /// A row with NO organization lookup is unaffected — that is every plain contact-keyed grant. A row
    /// WITH one confers only while that organization is active: an organization grant (contact empty)
    /// obviously, and also a contact-keyed grant carrying its grantee's firm (the <c>/grant</c> writer's
    /// "firm/org association", <c>GrantExternalAccessEndpoint</c>). The second is the set task 117's R2
    /// deactivates (it selects every active grant whose <c>sprk_organization</c> is inactive, contact or
    /// not), so the read guard covers exactly what the writer will later make permanent — the guard is
    /// belt-and-braces for the writer, never a different rule.
    /// </remarks>
    internal static bool GrantOrganizationConfers(Guid? organizationId, OrganizationStateRow? organization)
        => organizationId is null || organizationId.Value == Guid.Empty || OrganizationIsActive(organization);

    /// <summary>
    /// Projects the junction rows of ONE read into the two NAMED sets (task 109).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item><b>Wall subject</b> — every organization with an ACTIVE junction row. <c>statecode</c> ONLY:
    /// no date bound at either end and no organization-state bound. FR-23 over-matches by design, and every
    /// one of those bounds would make the wall match FEWER subjects (owner D-2 part 2, D-10).</item>
    /// <item><b>Conferring</b> — the wall set, narrowed to memberships that are current on
    /// <paramref name="today"/> (<see cref="MembershipConfersOn"/>) under an ACTIVE organization
    /// (<see cref="OrganizationIsActive"/>). Every additive org term reads this set and nothing else.</item>
    /// </list>
    /// <para>The junction <c>statecode</c> is re-decided here even though the <c>$filter</c> already
    /// bounded it — task 117's discipline: the filter bounds what comes back, the code decides what it
    /// means, so an over-broad filter is a cost rather than an access change.</para>
    /// <para>One organization reached by two rows (one ended, one current) confers: any current
    /// membership is a membership.</para>
    /// </remarks>
    internal static ActiveOrgMemberships ProjectOrganizationMemberships(
        IEnumerable<ContactOrgRow> rows, DateOnly today)
    {
        var wall = new HashSet<Guid>();
        var conferring = new HashSet<Guid>();

        foreach (var row in rows)
        {
            if (row.OrganizationId is not { } organizationId || organizationId == Guid.Empty)
            {
                continue;
            }

            if (!IsActiveState(row.StateCode))
            {
                continue;
            }

            wall.Add(organizationId);

            if (MembershipConfersOn(row.StartDate, row.EndDate, today) && OrganizationIsActive(row.Organization))
            {
                conferring.Add(organizationId);
            }
        }

        return new ActiveOrgMemberships(conferring.ToList(), wall.ToList(), Unreadable: false);
    }

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
    /// (<c>tenant:{tid}:external-access-grant:{contactId}:v{CacheVersion}</c>, the tenant-scoped realization of the
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
    /// cached) by <see cref="SubjectStandingGrantReader"/>, so this invalidation is the defensive
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

    // ── Referenced-organization resolution (task 039 · FR-23) ────────────────────────────────────

    /// <summary>
    /// Org-typed lookup columns per root entity — the record side of the FR-23 deny-list's ethical-wall
    /// match. Enumerated from LIVE metadata (task 039 step 1, spaarkedev1, 2026-09-04) and recorded in
    /// projects/unified-access-control-r2/notes/task-039-org-reference-inventory.md.
    /// </summary>
    /// <remarks>
    /// All three roots are uniform TODAY (both carry exactly <c>sprk_assignedlawfirm1</c> +
    /// <c>sprk_assignedlawfirm2</c> → <c>sprk_organization</c>) — but per the inventory notes this must
    /// NOT be assumed forward. A future root (e.g. <c>sprk_servicerequest</c>) needs its own VERIFIED
    /// entry here, never an inferred one.
    /// </remarks>
    private static readonly IReadOnlyDictionary<string, IReadOnlyList<string>> OrganizationLookupAttributes =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["sprk_project"] = new[] { "sprk_assignedlawfirm1", "sprk_assignedlawfirm2" },
            ["sprk_matter"] = new[] { "sprk_assignedlawfirm1", "sprk_assignedlawfirm2" },
            ["sprk_workassignment"] = new[] { "sprk_assignedlawfirm1", "sprk_assignedlawfirm2" },
        };

    /// <summary>
    /// The <c>$select</c> fragment for a batch of org-typed lookups — <c>_{attribute}_value</c> per
    /// column. Extracted as a PURE member (task 007 / A-5 precedent) so the over-match property (every
    /// registered lookup is selected unconditionally — no narrowing to a conferring subset) is directly
    /// assertable without an HTTP stack.
    /// </summary>
    internal static string BuildOrganizationReferenceSelect(IReadOnlyCollection<string> orgAttributes)
        => string.Join(",", orgAttributes.Select(a => $"_{a}_value"));

    /// <summary>
    /// Resolves EVERY organization each candidate record references (task 039 / FR-23) — deliberately
    /// ANY org-typed lookup, not narrowed to task 041's access-conferring registry (denial over-matches
    /// on purpose; register B-10).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mirrors <see cref="GetRootRecordFlagsAsync"/>'s batched-read shape (same
    /// <see cref="FlagQueryChunkSize"/>, same per-root-entity-type source lookup) but answers a
    /// DIFFERENT question — WHICH organizations a record references, not whether it is
    /// secure/restricted — so it is a separate method rather than a widened <see cref="RootRecordFlags"/>
    /// read: conflating the two would give one method two unrelated reasons to change.
    /// </para>
    /// <para>
    /// <b>Fail-closed toward UNRESOLVED — the worst case for THIS read, mirroring
    /// <see cref="GetRootRecordFlagsAsync"/>'s Unreadable contract.</b> A record whose org-reference
    /// read faults must not silently resolve to "references no organizations": the caller (task 039's
    /// veto wiring) would then have no way to know it missed a possible ethical-wall match — exactly
    /// the "skipped record is an unevaluated wall" case this task's escalation trigger names. Every id
    /// in a faulted chunk comes back with <see cref="ReferencedOrganizations.Unresolved"/>
    /// (<c>Unreadable = true</c>); the caller treats that as a forced deny, independent of whatever the
    /// deny-list reader itself would say.
    /// </para>
    /// <para>
    /// An entity type with NO org-typed lookups returns a totally EMPTY map — a static SCHEMA fact, not
    /// a failed read, mirroring <see cref="GetRootRecordFlagsAsync"/>'s "not a flag-bearing type"
    /// branch. Callers must not confuse absence-because-no-columns with Unreadable; the two are only
    /// comparable once the entity type is known to be org-bearing (i.e. once ANY id is present in the
    /// returned map).
    /// </para>
    /// <para>Virtual for the same test seam the rest of this class uses (subclass + override).</para>
    /// </remarks>
    public virtual async Task<IReadOnlyDictionary<Guid, ReferencedOrganizations>> GetReferencedOrganizationIdsAsync(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
    {
        if (recordIds is null || recordIds.Count == 0)
        {
            return new Dictionary<Guid, ReferencedOrganizations>();
        }

        if (!RootFlagSources.TryGetValue(entityType ?? string.Empty, out var source) ||
            !OrganizationLookupAttributes.TryGetValue(entityType ?? string.Empty, out var orgAttributes) ||
            orgAttributes.Count == 0)
        {
            // Not an org-bearing root type. No organization reference is possible — a static schema
            // fact, not a failed read (see remarks).
            return new Dictionary<Guid, ReferencedOrganizations>();
        }

        var distinct = recordIds.Where(id => id != Guid.Empty).Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, ReferencedOrganizations>();
        }

        var result = new Dictionary<Guid, ReferencedOrganizations>();
        var selectClause = BuildOrganizationReferenceSelect(orgAttributes);

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
                            $"&$select={source.IdAttribute},{selectClause}";

                using var request = new HttpRequestMessage(HttpMethod.Get, query);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Headers.Add("OData-MaxVersion", "4.0");
                request.Headers.Add("OData-Version", "4.0");
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                var response = await _httpClient.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError(
                        "[EXT-ACCESS] Org-reference query FAILED for {EntityType} ({Count} ids): {Status}. "
                        + "Failing CLOSED — every id in this chunk is UNRESOLVED; task 039's caller denies them.",
                        entityType, chunk.Count, response.StatusCode);
                    foreach (var id in chunk)
                    {
                        result[id] = ReferencedOrganizations.Unresolved;
                    }
                    continue;
                }

                var payload = await response.Content.ReadFromJsonAsync<DataverseQueryResult<OrganizationReferenceRow>>(ct);
                var byId = (payload?.Value ?? new List<OrganizationReferenceRow>())
                    .GroupBy(r => r.GetId(source.IdAttribute))
                    .ToDictionary(g => g.Key, g => g.First());

                foreach (var id in chunk)
                {
                    result[id] = byId.TryGetValue(id, out var row)
                        ? new ReferencedOrganizations(row.ReferencedOrganizationIds(), Unreadable: false)
                        // Asked about, not returned. Cannot be distinguished from an unreadable row.
                        : ReferencedOrganizations.Unresolved;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-ACCESS] Org-reference query threw for {EntityType}. Failing CLOSED — all {Count} "
                + "ids are UNRESOLVED; task 039's caller denies them.", entityType, distinct.Count);
            foreach (var id in distinct)
            {
                result[id] = ReferencedOrganizations.Unresolved;
            }
        }

        return result;
    }

    /// <summary>Projection of the org-typed lookup columns (task 039). Ids arrive as strings over OData.</summary>
    private sealed class OrganizationReferenceRow
    {
        public string? sprk_projectid { get; set; }
        public string? sprk_matterid { get; set; }
        public string? sprk_workassignmentid { get; set; }

        [JsonPropertyName("_sprk_assignedlawfirm1_value")]
        public Guid? sprk_assignedlawfirm1 { get; set; }

        [JsonPropertyName("_sprk_assignedlawfirm2_value")]
        public Guid? sprk_assignedlawfirm2 { get; set; }

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

        /// <summary>Every populated org-lookup slot on this row, in declaration order (task 039 over-match: both slots, unconditionally).</summary>
        public IReadOnlyCollection<Guid> ReferencedOrganizationIds()
        {
            var ids = new List<Guid>(2);
            if (sprk_assignedlawfirm1 is { } id1) ids.Add(id1);
            if (sprk_assignedlawfirm2 is { } id2) ids.Add(id2);
            return ids;
        }
    }

    /// <summary>
    /// The Dataverse read behind a grant-set cache MISS: the contact's own grant rows plus the
    /// organization-inherited ones, partitioned and deduped.
    /// </summary>
    /// <remarks>
    /// <c>internal virtual</c> as a test seam (task 131), the convention <c>NoAccessListReader.QueryChunkAsync</c>
    /// already uses: a test double overrides THIS read so <see cref="GetGrantSetAsync"/> — the method
    /// that owns the cache read, the miss fallback and the cache write — runs unmodified. Every earlier
    /// double overrode <see cref="GetGrantSetAsync"/> itself, which is why no test ever exercised the
    /// cache and defect C3 shipped green. Behaviour is unchanged by the modifier.
    /// </remarks>
    internal virtual async Task<ExternalGrantSet> QueryGrantSetAsync(Guid contactId, CancellationToken ct)
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
            // The organization's own state comes back in the SAME read (task 109 · ISS-026) — see
            // GrantOrganizationConfers, applied immediately below before any row is partitioned.
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter={BuildContactGrantFilter(contactId, TodayUtc)}" +
                        $"&$select={GrantRowSelect}" +
                        $"&$expand={OrganizationStateExpand}";

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
            var rows = WithoutInactiveOrganizations(result?.Value ?? new List<ExternalAccessRow>(), contactId);

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
            // contact CURRENTLY and ACTIVELY belongs to (the junction's CONFERRING set — task 109: date-
            // bounded at both ends, under an active organization). This mirrors the standing-grant runtime
            // union: no per-contact rows exist, membership is resolved live, and staleness is bounded by
            // the 60s cache TTL. Fail-closed by construction — a junction or org-grant read fault
            // contributes nothing, never 500s the authz path.
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
    /// empty) for every organization in the contact's CONFERRING membership set
    /// (<c>sprk_contactorganization</c>, task 109). Two reads (memberships → org grants); fail-closed at
    /// every step (nothing on any fault, so an org-side read problem NEVER widens NOR 500s the authz
    /// decision). Returns the org-grant rows in the same shape as per-contact grants so the caller unions
    /// them identically.
    /// </summary>
    /// <remarks>
    /// An ADDITIVE caller, so it reads <see cref="ActiveOrgMemberships.ConferringOrganizationIds"/> and
    /// nothing else: a date-ended or not-yet-started membership, or one under an inactive organization,
    /// contributes no org grant (owner D-2 part 1, D-10, ISS-026). On an unreadable junction that set is
    /// empty, so the fault grants nothing — the additive half of the outcome's two fail directions.
    /// <para>⚠️ The <see cref="ActiveOrgMemberships.Unreadable"/> signal is logged here but not yet carried
    /// on the returned grant set, so a grant set built over a faulted junction read is cached for one TTL
    /// like any other. Classifying that set as faulted (and not caching it) is task 132's, which consumes
    /// this outcome by design rather than re-reading the junction.</para>
    /// </remarks>
    private async Task<List<ExternalAccessRow>> QueryOrganizationGrantRowsAsync(
        Guid contactId, string token, string apiUrl, CancellationToken ct)
    {
        var memberships = await QueryOrganizationMembershipsAsync(contactId, token, apiUrl, ct);
        var orgIds = memberships.ConferringOrganizationIds;
        if (orgIds.Count == 0)
        {
            if (memberships.Unreadable)
            {
                _logger.LogWarning(
                    "[EXT-ACCESS] Org-grant term for Contact {ContactId} contributes NOTHING: the membership " +
                    "junction was unreadable. A fault must not grant (ADR-003).", contactId);
            }

            return new List<ExternalAccessRow>();
        }

        try
        {
            // Active, UNEXPIRED org grants (sprk_Contact EMPTY — the org-grant marker) for any of the
            // contact's orgs. An org grant expires exactly like a person grant: leaving the predicate off
            // this second path would let every contact keep expired access simply by holding it through
            // their firm, which is the same finding wearing a different lookup.
            var query = $"{apiUrl}/sprk_externalrecordaccesses" +
                        $"?$filter={BuildOrganizationGrantFilter(orgIds, TodayUtc)}" +
                        $"&$select={GrantRowSelect}" +
                        $"&$expand={OrganizationStateExpand}";

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

            // Belt-and-braces for ISS-026: the conferring set already excluded inactive organizations,
            // so a row here under an inactive one means its organization changed state between the two
            // reads. The guard is the same one the contact-grant read applies.
            return WithoutInactiveOrganizations(result?.Value ?? new List<ExternalAccessRow>(), contactId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying org grants for Contact {ContactId}", contactId);
            return new List<ExternalAccessRow>();
        }
    }

    /// <summary>
    /// Drops every grant row whose organization association is not active
    /// (<see cref="GrantOrganizationConfers"/>) — the ISS-026 read guard, applied before any row is
    /// partitioned, cached or returned.
    /// </summary>
    /// <remarks>
    /// In memory rather than in the <c>$filter</c>: the organization's state arrives by
    /// <see cref="OrganizationStateExpand"/>, and an OR across a parent column and a navigation column is
    /// not a predicate whose null semantics Dataverse documents. The rows never leave this private method
    /// unfiltered, so no later code path can see one.
    /// </remarks>
    private List<ExternalAccessRow> WithoutInactiveOrganizations(List<ExternalAccessRow> rows, Guid contactId)
    {
        var kept = rows.Where(r => GrantOrganizationConfers(r._sprk_organization_value, r.Organization)).ToList();
        if (kept.Count != rows.Count)
        {
            _logger.LogInformation(
                "[EXT-ACCESS] {Dropped} grant row(s) for Contact {ContactId} confer NOTHING: their organization " +
                "is inactive or its state did not come back (ISS-026 read guard).",
                rows.Count - kept.Count, contactId);
        }

        return kept;
    }

    /// <summary>
    /// The evaluator's entry onto the ONE organization-membership read (task 109): the subject's
    /// CONFERRING set (additive org terms) and WALL-SUBJECT set (the FR-23 deny veto, task 039), plus
    /// whether the read could be completed at all — from a single junction query.
    /// </summary>
    /// <remarks>
    /// <para><b>Every fault now reaches the caller as a fault.</b> A query-level failure (non-success status,
    /// timeout, transport or parse error) comes back as <see cref="ActiveOrgMemberships.Failed"/> from
    /// <see cref="QueryOrganizationMembershipsAsync"/>. Token/API-url acquisition is still deliberately NOT
    /// guarded here: it propagates to <c>AccessibleRecordSetService.ReadActiveOrgMembershipsAsync</c>,
    /// which maps it to the SAME <see cref="ActiveOrgMemberships.Failed"/>. Before task 109 those two faults
    /// resolved differently — the token fault denied, the query fault was swallowed into an empty list that
    /// the veto read as "belongs to no organization" (ISS-019, #998), so the wall's organization axis
    /// silently stopped matching.</para>
    /// <para><b>The two fail directions are NOT unified — they are now both reachable.</b> The outcome is
    /// one value; each consumer applies its own direction to it. The additive terms read
    /// <see cref="ActiveOrgMemberships.ConferringOrganizationIds"/>, which is empty on a fault, so a fault
    /// grants nothing; the veto reads <see cref="ActiveOrgMemberships.Unreadable"/> first and denies every
    /// queried candidate. What was removed is only the case where a fault was indistinguishable from
    /// absence.</para>
    /// <para><c>internal virtual</c> for the test seam the rest of this class uses (subclass + override);
    /// internal because <see cref="ActiveOrgMemberships"/> is.</para>
    /// </remarks>
    internal virtual async Task<ActiveOrgMemberships> ReadOrganizationMembershipsAsync(
        Guid contactId, CancellationToken ct = default)
    {
        var token = await GetAppOnlyTokenAsync(ct).ConfigureAwait(false);
        var apiUrl = GetDataverseApiUrl();
        var memberships = await QueryOrganizationMembershipsAsync(contactId, token, apiUrl, ct).ConfigureAwait(false);

        // The query reports the caller's own cancellation as Failed (see its catch); here, on the evaluator's
        // path, a cancelled request propagates instead of composing a deny-all answer nobody is waiting for.
        ct.ThrowIfCancellationRequested();
        return memberships;
    }

    /// <summary>
    /// The <c>sprk_contactorganization</c> junction query — the ONE read behind both named membership sets
    /// (<see cref="ProjectOrganizationMemberships"/>), shared by the evaluator entry above and the org-grant
    /// term (<see cref="QueryOrganizationGrantRowsAsync"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Reports a fault instead of swallowing it</b> (task 109 · ISS-019): a non-success status, a
    /// timeout or any other exception returns <see cref="ActiveOrgMemberships.Failed"/> — never an empty
    /// "successful" read. An HTTP timeout surfaces as an <see cref="OperationCanceledException"/> whose token
    /// is not the caller's, and that is a fault. The caller's OWN cancellation is reported as Failed here too
    /// and rethrown by <see cref="ReadOrganizationMembershipsAsync"/> — see the catch for why it must not
    /// propagate from this method.</para>
    /// <para><b>Schema</b>, live-verified 2026-08-26 (task 020, recorded at
    /// <c>RevokeExternalAccessEndpoint.cs</c>'s <c>ExternalOrganizationMembership</c>) and again 2026-09-30
    /// (task 109): collection <c>sprk_contactorganizations</c>; lookups <c>_sprk_contact_value</c> /
    /// <c>_sprk_organization_value</c>; <c>statecode</c> Active(0)/Inactive(1); <c>sprk_startdate</c> and
    /// <c>sprk_enddate</c> Date Only with <c>DateOnly</c> behaviour; navigation property
    /// <c>sprk_Organization</c>.</para>
    /// <para><b>A former member is NOT necessarily a deactivated row.</b> Nothing in the repo deactivated
    /// these maker-authored rows until task 117's reconciliation job (registered disabled), so a membership
    /// ended by DATE can still be <c>statecode</c>-active. That is why the conferring set is date-bounded in
    /// memory and does not rely on <c>statecode</c> alone — and why the wall, which deliberately does, keeps
    /// binding a former member (owner D-2).</para>
    /// </remarks>
    private async Task<ActiveOrgMemberships> QueryOrganizationMembershipsAsync(
        Guid contactId, string token, string apiUrl, CancellationToken ct)
    {
        try
        {
            var query = $"{apiUrl}/sprk_contactorganizations" +
                        $"?$filter={BuildOrganizationMembershipFilter(contactId)}" +
                        $"&$select={OrganizationMembershipSelect}" +
                        $"&$expand={OrganizationStateExpand}";

            using var request = new HttpRequestMessage(HttpMethod.Get, query);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[EXT-ACCESS] Contact-organization membership query FAILED for Contact {ContactId}: {Status}. " +
                    "Reporting the read as UNREADABLE — the org terms contribute nothing and the deny veto " +
                    "denies every queried candidate (ISS-019).",
                    contactId, response.StatusCode);
                return ActiveOrgMemberships.Failed;
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ContactOrgRow>>(ct);
            var memberships = ProjectOrganizationMemberships(result?.Value ?? new List<ContactOrgRow>(), TodayUtc);

            if (memberships.ConferringOrganizationIds.Count != memberships.WallSubjectOrganizationIds.Count)
            {
                // Debug, not Information: this runs on every composition (every authorization check) and
                // describes a steady state, not an event.
                _logger.LogDebug(
                    "[EXT-ACCESS] Contact {ContactId}: {Wall} active organization membership(s), of which " +
                    "{Conferring} confer access — the rest are ended by date, not yet started, or under an " +
                    "inactive organization. They still bind the No Access wall (owner D-2 / D-10).",
                    contactId, memberships.WallSubjectOrganizationIds.Count, memberships.ConferringOrganizationIds.Count);
            }

            return memberships;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The CALLER cancelled. Reported as Failed, not rethrown, on purpose: this query also runs inside
            // QueryGrantSetAsync, whose catch-all would turn a propagated cancellation into an EMPTY grant set
            // (direct grants included) and cache it — a wider loss than the org-grant term alone. The
            // evaluator's entry (ReadOrganizationMembershipsAsync) rethrows the cancellation itself.
            // Not caching fault-derived grant sets at all is task 132's.
            return ActiveOrgMemberships.Failed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-ACCESS] Error querying contact-organization memberships for Contact {ContactId}. " +
                "Reporting the read as UNREADABLE (ISS-019).", contactId);
            return ActiveOrgMemberships.Failed;
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
                // Task 131 (C3): BOTH levels are written, for every root type. DirectAccessLevel is what
                // Secure suppression reads; dropping it here made a direct grant on a secure root None on
                // every cache hit. A null direct level (org-inherited only) is written AS null.
                Projects = grantSet.Projects
                    .Select(p => new CachedParticipation
                    {
                        ProjectId = p.ProjectId,
                        AccessLevel = (int)p.AccessLevel,
                        DirectAccessLevel = (int?)p.DirectAccessLevel
                    })
                    .ToList(),
                // Task 032: persist matter/WA LEVELS, not just ids. Writing ids here (the prior shape)
                // is what would have made rights correct on a miss and None on a hit.
                MatterGrants = grantSet.MatterGrants
                    .Select(CachedRootGrant.From)
                    .ToList(),
                WorkAssignmentGrants = grantSet.WorkAssignmentGrants
                    .Select(CachedRootGrant.From)
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

        /// <summary>The row's organization association — an org grant's grantee, or a contact grant's firm (task 109).</summary>
        [JsonPropertyName("_sprk_organization_value")]
        public Guid? _sprk_organization_value { get; set; }

        /// <summary>The organization's own state, expanded in the same read (<see cref="OrganizationStateExpand"/>).</summary>
        [JsonPropertyName("sprk_Organization")]
        public OrganizationStateRow? Organization { get; set; }
    }

    /// <summary>
    /// A <c>sprk_contactorganization</c> junction row (task 073 #7; widened by task 109 to carry both date
    /// bounds, its own state and its organization's state, so ONE read serves both named sets).
    /// </summary>
    /// <remarks><c>internal</c> so <see cref="ProjectOrganizationMemberships"/> — a pure member carrying the
    /// conferring/wall contract — is assertable without a transport (ADR-038 A2).</remarks>
    internal sealed class ContactOrgRow
    {
        [JsonPropertyName("_sprk_organization_value")]
        public Guid? OrganizationId { get; set; }

        /// <summary>Date Only. Null = no start bound (an ordinary current membership).</summary>
        [JsonPropertyName("sprk_startdate")]
        public DateOnly? StartDate { get; set; }

        /// <summary>Date Only. Null = no end bound (an ordinary current membership).</summary>
        [JsonPropertyName("sprk_enddate")]
        public DateOnly? EndDate { get; set; }

        /// <summary>The junction row's own state. Null is ACTIVE (<see cref="IsActiveState"/>).</summary>
        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }

        /// <summary>The parent organization's state; null when the expand did not come back.</summary>
        [JsonPropertyName("sprk_Organization")]
        public OrganizationStateRow? Organization { get; set; }
    }

    /// <summary>The expanded parent <c>sprk_organization</c> — its state only (task 109 · ISS-026).</summary>
    internal sealed class OrganizationStateRow
    {
        /// <summary>Active(0) / Inactive(1), live-verified 2026-09-30. Null is ACTIVE (<see cref="IsActiveState"/>).</summary>
        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }
    }

    private sealed class ContactRow
    {
        [JsonPropertyName("contactid")]
        public Guid? contactid { get; set; }

        [JsonPropertyName("sprk_externalobjectid")]
        public string? sprk_externalobjectid { get; set; }
    }

    /// <summary>
    /// A cached project grant: id + effective level + DIRECT level (task 131 / C3).
    /// </summary>
    /// <remarks>
    /// ⚠️ <c>DirectAccessLevel</c> restores null AS null — never defaulted to <c>AccessLevel</c>, never
    /// inferred. Null means "every contributing row was org-inherited", and on a secure root that must
    /// compose to <see cref="AccessRights.None"/> on a hit exactly as it does on a miss. Defaulting it
    /// upward would be an over-grant on every secure record reached only through an organization.
    /// </remarks>
    private sealed class CachedParticipation
    {
        public Guid ProjectId { get; set; }
        public int AccessLevel { get; set; }
        public int? DirectAccessLevel { get; set; }

        public ExternalParticipation ToParticipation() => new()
        {
            ProjectId = ProjectId,
            AccessLevel = (ExternalAccessLevel)AccessLevel,
            DirectAccessLevel = (ExternalAccessLevel?)DirectAccessLevel
        };
    }

    /// <summary>
    /// A cached non-project (matter / work-assignment) grant: id + effective level (task 032) + DIRECT
    /// level (task 131 / C3). Both levels are nullable for the same reasons <see cref="ExternalRootGrant"/>'s
    /// are, and a null direct level restores as null — see <see cref="CachedParticipation"/>.
    /// </summary>
    private sealed class CachedRootGrant
    {
        public Guid RecordId { get; set; }
        public int? AccessLevel { get; set; }
        public int? DirectAccessLevel { get; set; }

        public static CachedRootGrant From(ExternalRootGrant grant) => new()
        {
            RecordId = grant.RecordId,
            AccessLevel = (int?)grant.AccessLevel,
            DirectAccessLevel = (int?)grant.DirectAccessLevel
        };

        public ExternalRootGrant ToGrant() => new()
        {
            RecordId = RecordId,
            AccessLevel = (ExternalAccessLevel?)AccessLevel,
            DirectAccessLevel = (ExternalAccessLevel?)DirectAccessLevel
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
    /// <para>
    /// 🔴 <b>It happened again (4 → 5, task 131 / defect C3).</b> Task 037 added
    /// <c>DirectAccessLevel</c> to <see cref="ExternalParticipation"/> and <see cref="ExternalRootGrant"/>
    /// — the field Secure suppression reads — but not to this shape and without a bump. A DIRECT grant
    /// on a secure root therefore composed correctly on a miss and to <c>AccessRights.None</c> on every
    /// hit; the suite stayed green because every test double overrode <c>GetGrantSetAsync</c>. The rule
    /// above was not enough on its own: it binds whoever edits THIS type, and task 037 edited the grant
    /// TYPES. So the obligation is now enforced from the other side — <c>GrantCacheRoundTripSeamTests</c>
    /// enumerates every public settable property of the three grant types and fails when one does not
    /// survive a round trip through the production <c>TenantCache</c>. Adding a property there means
    /// carrying it here and bumping <see cref="CacheVersion"/>.
    /// </para>
    /// <para>
    /// ⚠️ It holds NO expiry dates — expiry is applied by the read <c>$filter</c> when an entry is built. The
    /// write paths rely on that: <c>/set-record-share-expiry</c> (task 098) and <c>/grant</c> treat a failed
    /// invalidation as a freshness issue only, because a changed date that is today or later cannot change
    /// today's answer. If a date is ever cached here, a failed invalidation after a SHORTENING keeps the old
    /// date for one TTL — revisit those invalidators when you change this shape.
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
