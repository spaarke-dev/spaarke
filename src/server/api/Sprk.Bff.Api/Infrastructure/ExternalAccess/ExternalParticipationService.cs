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
        => $"_sprk_contact_value eq {contactId} and {WallMembershipStateClause}";

    /// <summary>
    /// The SAME wall-set predicate keyed the other way — every ACTIVE membership OF an organization (task 143: the
    /// enforcer expands an organization-subject No Access entry to its members). It shares
    /// <see cref="WallMembershipStateClause"/>, so the wall can never be bounded differently in the two directions.
    /// </summary>
    internal static string BuildOrganizationMembershipFilterForOrganization(Guid organizationId)
        => $"_sprk_organization_value eq {organizationId} and {WallMembershipStateClause}";

    /// <summary>
    /// The ONLY bound on a WALL membership: its own <c>statecode</c> (owner D-2 part 2 / D-10) — never a date. Shared by
    /// both junction filters.
    /// </summary>
    internal const string WallMembershipStateClause = "(statecode eq 0 or statecode eq null)";

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

    /// <summary>
    /// Task 174 (owner round 84): the app-only reader the ONE filing walk reads through
    /// (<see cref="GetEffectiveRootRecordFlagsAsync"/>). Registered unconditionally (GraphModule), so the typed-client factory
    /// always supplies it. A null reader (a test double that passes none) makes every work assignment or project read as
    /// unverifiable — secure AND Restricted (fail closed), never as unfiled.
    /// </summary>
    private readonly Spaarke.Dataverse.IGenericEntityService? _filing;

    public ExternalParticipationService(
        HttpClient httpClient,
        ITenantCache cache,
        IConfiguration configuration,
        TokenCredential credential,
        IHttpContextAccessor httpContextAccessor,
        ILogger<ExternalParticipationService> logger,
        Spaarke.Dataverse.IGenericEntityService? filing)
    {
        _httpClient = httpClient;
        _cache = cache;
        _configuration = configuration;
        _credential = credential;
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
        _filing = filing;
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
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Task 132: the caller cancelled — propagate; a cancelled read is not a cache fault.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[EXT-ACCESS] Cache read error for Contact {ContactId}. Falling through to Dataverse.", contactId);
            }
        }

        // Cache miss — query Dataverse
        var grantSet = await QueryGrantSetAsync(contactId, ct);

        // THE ONE cache write for grant sets (task 137 left this the single write path; task 132 · C12 gates it).
        // A FAULTED set — built over a failed grant, organization-grant or junction read — is returned to this request
        // (fail closed: it holds only what was read successfully) and NEVER stored: storing it is what turned one 429
        // into 60 seconds of "no grants". A successful read that found nothing is an answer and IS stored.
        // Fire-and-forget (don't block the response). Skip when no tenant claim.
        if (grantSet.Faulted)
        {
            _logger.LogWarning(
                "[EXT-ACCESS] Grant set for Contact {ContactId} was built over a FAULTED read; returned to this request " +
                "only and NOT cached (task 132). The next request re-reads.", contactId);
        }
        else if (!string.IsNullOrEmpty(tenantId))
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
    /// change webhook on the standing-grant field) can invalidate without an ambient HttpContext. Since task 137
    /// it is ADDED to the tenants <see cref="InvalidateGrantSetsAsync"/> removes under (the request's, the CIAM
    /// tenant and every configured workforce tenant); with none of those available the call is a logged no-op.
    /// </para>
    /// </remarks>
    public virtual Task InvalidateAsync(
        Guid contactId,
        string? tenantId = null,
        CancellationToken ct = default)
        => InvalidateGrantSetsAsync(new[] { contactId }, Array.Empty<Guid>(), ct, tenantId);

    // ── THE ONE grant-cache invalidation routine (task 137 · defect C5) ───────────────────────────
    //
    // Every grant-write path calls this — /grant, /invite-and-grant (through the grant core), /revoke,
    // /close-project, /set-record-share-expiry — instead of carrying its own cache.RemoveAsync copy. Two holes
    // it closes:
    //   1. THE TENANT. The cache key is tenant:{tid}:…, where tid is the READING request's tenant: a CIAM
    //      caller's entry lives under the CIAM tenant, a workforce caller's under theirs. Every writer runs on the
    //      workforce admin group and removed only its own tid, so a revoked CIAM contact kept its cached grants for
    //      the 60-second TTL. The routine removes under EVERY tenant a grant set can be cached under (path C — no
    //      ITenantCache contract change, ADR-009 tenant scoping intact).
    //   2. ORGANIZATION GRANTS. A grant to an organization names no contact, so /grant, /revoke and
    //      /close-project invalidated nobody. The routine expands every organization to its ACTIVE members, paged
    //      to completion — no silent cap.

    /// <summary>Junction rows per page when expanding an organization to its members (Dataverse's own maximum is 5000).</summary>
    internal const int OrganizationMemberPageSize = 500;

    /// <summary>
    /// Page backstop for one organization's member walk: 200 pages × 500 = 100,000 members. Past it the walk stops
    /// and says so at warning — the remaining members' entries expire on the 60-second TTL.
    /// </summary>
    internal const int MaxOrganizationMemberPages = 200;

    /// <summary>Concurrent cache removals per invalidation (each is one independent Redis DEL).</summary>
    private const int MaxConcurrentRemovals = 16;

    /// <summary>
    /// Removes every cached grant set that can hold the affected contacts' grants: the given contacts, plus every
    /// ACTIVE member of the given organizations, under every tenant id a grant set can be cached under
    /// (<see cref="GrantCacheTenantIds"/>).
    /// </summary>
    /// <remarks>
    /// <para><b>Never throws and never fails the write</b> (owner 2026-09-10). Each removal is independent; a
    /// failure is counted and logged, and the entry it missed expires on the 60-second TTL. The caller has
    /// already committed its write, so it may pass <see cref="CancellationToken.None"/> to finish the clean-up
    /// even if its client disconnects.</para>
    /// <para><b>Data, not decisions.</b> The grant set is participation DATA (ADR-009); the authorization
    /// decision is recomputed live per request and never cached (auth.md). A read already in flight can still
    /// re-populate an entry after its removal (the cache write is fire-and-forget), so the bound on staleness
    /// remains the TTL — what this routine removes is the TTL as the NORMAL case.</para>
    /// </remarks>
    /// <param name="contactIds">Contacts whose own grants changed (a person grant, or a contact-keyed share).</param>
    /// <param name="organizationIds">Organizations whose organization-wide grants changed.</param>
    /// <param name="explicitTenantId">An extra tenant id (an out-of-request caller's), added to the set.</param>
    public virtual async Task<GrantCacheInvalidation> InvalidateGrantSetsAsync(
        IEnumerable<Guid> contactIds,
        IEnumerable<Guid> organizationIds,
        CancellationToken ct = default,
        string? explicitTenantId = null)
    {
        try
        {
            return await InvalidateGrantSetsCoreAsync(contactIds, organizationIds, ct, explicitTenantId)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            // Belt-and-braces for the "never throws" contract: every step inside already catches its own faults.
            _logger.LogWarning(ex,
                "[EXT-ACCESS] Grant-cache invalidation failed unexpectedly. Affected entries expire on the " +
                "{Ttl}-second TTL; the write that called it is unaffected.", CacheTtl.TotalSeconds);
            return new GrantCacheInvalidation(0, 0, 0, Array.Empty<Guid>());
        }
    }

    private async Task<GrantCacheInvalidation> InvalidateGrantSetsCoreAsync(
        IEnumerable<Guid> contactIds,
        IEnumerable<Guid> organizationIds,
        CancellationToken ct,
        string? explicitTenantId)
    {
        var contacts = new HashSet<Guid>((contactIds ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty));
        var unexpanded = new List<Guid>();

        foreach (var organizationId in (organizationIds ?? Array.Empty<Guid>()).Where(id => id != Guid.Empty).Distinct())
        {
            var members = await EnumerateActiveOrganizationMembersAsync(organizationId, ct).ConfigureAwait(false);
            contacts.UnionWith(members.ContactIds);
            if (!members.Complete)
            {
                unexpanded.Add(organizationId);
            }
        }

        var tenants = GrantCacheTenantIds(explicitTenantId);
        if (tenants.Count == 0)
        {
            _logger.LogWarning(
                "[EXT-ACCESS] Grant-cache invalidation for {Count} contact(s) skipped: no tenant id is available (no " +
                "'tid' claim, no explicit tenant, no Ciam:TenantId / AzureAd:TenantId / WorkforceIdentity:CustomerTenantIds " +
                "configured). Their entries expire on the {Ttl}-second TTL.", contacts.Count, CacheTtl.TotalSeconds);
            return new GrantCacheInvalidation(contacts.Count, 0, 0, unexpanded);
        }

        // Bounded parallelism: a large organization × several tenants is thousands of independent key removals,
        // and run one at a time they would hold the write's response for seconds. Each removal stands alone.
        var removed = 0;
        var failed = 0;
        var removals = contacts.SelectMany(contactId => tenants.Select(tenant => (contactId, tenant)));
        await Parallel.ForEachAsync(
                removals,
                new ParallelOptions { MaxDegreeOfParallelism = MaxConcurrentRemovals, CancellationToken = ct },
                async (removal, token) =>
                {
                    try
                    {
                        await _cache.RemoveAsync(removal.tenant, ExternalAccessResource, removal.contactId.ToString(),
                            CacheVersion, ct: token).ConfigureAwait(false);
                        Interlocked.Increment(ref removed);
                    }
                    catch (Exception ex) when (!token.IsCancellationRequested)
                    {
                        // Non-fatal: the entry expires on its own TTL. Never turns a committed write into an error.
                        Interlocked.Increment(ref failed);
                        _logger.LogWarning(ex,
                            "[EXT-ACCESS] Failed to invalidate the grant cache for Contact {ContactId} under tenant " +
                            "{TenantId}. It expires on the {Ttl}-second TTL.",
                            removal.contactId, removal.tenant, CacheTtl.TotalSeconds);
                    }
                })
            .ConfigureAwait(false);

        _logger.LogInformation(
            "[EXT-ACCESS] Invalidated the grant cache of {Contacts} contact(s) under {Tenants} tenant id(s): {Removed} " +
            "removal(s), {Failed} failed; {Unexpanded} organization(s) not fully expanded.",
            contacts.Count, tenants.Count, removed, failed, unexpanded.Count);

        return new GrantCacheInvalidation(contacts.Count, removed, failed, unexpanded);
    }

    /// <summary>
    /// Every tenant id a contact's grant set can be cached under: the current request's <c>tid</c>, an explicit
    /// one, the CIAM tenant (<c>Ciam:TenantId</c>), the workforce app's own tenant (<c>AzureAd:TenantId</c>) and
    /// every configured customer workforce tenant (<c>WorkforceIdentity:CustomerTenantIds</c>, task 141 — under
    /// Model 1 it differs from <c>AzureAd:TenantId</c>).
    /// </summary>
    /// <remarks>
    /// The cache key is built from the raw <c>tid</c> claim, which Entra issues as a lower-case "D" GUID. A
    /// configured value may differ in case, so each GUID is added in BOTH its configured spelling and its canonical
    /// lower-case form; an extra removal of a key that does not exist costs one cache round trip and nothing else.
    /// </remarks>
    internal IReadOnlyList<string> GrantCacheTenantIds(string? explicitTenantId = null)
    {
        var raw = new List<string?> { ExtractTenantId(), explicitTenantId };
        if (_configuration is not null)
        {
            raw.Add(_configuration["Ciam:TenantId"]);
            raw.Add(_configuration["AzureAd:TenantId"]);
            raw.AddRange(_configuration.GetSection("WorkforceIdentity:CustomerTenantIds").GetChildren().Select(c => c.Value));
        }

        var tenants = new List<string>();
        foreach (var value in raw)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var trimmed = value.Trim();
            if (!tenants.Contains(trimmed, StringComparer.Ordinal))
            {
                tenants.Add(trimmed);
            }

            if (Guid.TryParse(trimmed, out var guid))
            {
                var canonical = guid.ToString("D");
                if (!tenants.Contains(canonical, StringComparer.Ordinal))
                {
                    tenants.Add(canonical);
                }
            }
        }

        return tenants;
    }

    /// <summary>
    /// The ACTIVE member contacts of one organization, following the server's paging to the end (no silent cap).
    /// A failed page, or the page backstop, stops the walk: the members already read are returned and the result
    /// says it is incomplete, with a warning naming the count and the TTL bound.
    /// </summary>
    internal async Task<OrganizationMemberWalk> EnumerateActiveOrganizationMembersAsync(Guid organizationId, CancellationToken ct)
    {
        var members = new HashSet<Guid>();
        string? next = null;
        var pages = 0;

        try
        {
            do
            {
                var page = await ReadOrganizationMemberPageAsync(organizationId, next, ct).ConfigureAwait(false);
                pages++;
                members.UnionWith(page.ContactIds.Where(id => id != Guid.Empty));
                next = page.NextLink;

                if (next is not null && pages >= MaxOrganizationMemberPages)
                {
                    _logger.LogWarning(
                        "[EXT-ACCESS] Organization {OrganizationId} has more active members than {Pages} page(s) of " +
                        "{PageSize}: {Read} member(s) invalidated, the rest are NOT — their cached grant sets expire on " +
                        "the {Ttl}-second TTL.",
                        organizationId, pages, OrganizationMemberPageSize, members.Count, CacheTtl.TotalSeconds);
                    return new OrganizationMemberWalk(members.ToList(), Complete: false);
                }
            }
            while (next is not null);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "[EXT-ACCESS] Could not read all active members of Organization {OrganizationId} (stopped after {Pages} " +
                "page(s), {Read} member(s) read). Members not read are NOT invalidated — their cached grant sets expire " +
                "on the {Ttl}-second TTL.", organizationId, pages, members.Count, CacheTtl.TotalSeconds);
            return new OrganizationMemberWalk(members.ToList(), Complete: false);
        }

        return new OrganizationMemberWalk(members.ToList(), Complete: true);
    }

    /// <summary>
    /// One page of an organization's ACTIVE junction rows — the same <c>$filter</c> the revoke path's SPE sweep
    /// uses (<see cref="ExternalOrganizationMembership.ActiveMembersFilter"/>) — and the server's
    /// <c>@odata.nextLink</c> for the next one. Throws on any failure (a fault is never an empty page).
    /// </summary>
    /// <remarks><c>internal virtual</c>: the test seam this class uses for every read (subclass + override; no
    /// HTTP double, ADR-038 B1). <c>DataverseWebApiClient.QueryAsync</c> is not used because it discards
    /// <c>@odata.nextLink</c> — the silent truncation the 200-member bound on the revoke path exists to detect.</remarks>
    internal virtual async Task<OrganizationMemberPage> ReadOrganizationMemberPageAsync(
        Guid organizationId, string? nextLink, CancellationToken ct)
    {
        var token = await GetAppOnlyTokenAsync(ct).ConfigureAwait(false);
        var url = nextLink
                  ?? $"{GetDataverseApiUrl()}/{ExternalOrganizationMembership.EntitySet}" +
                     $"?$filter={ExternalOrganizationMembership.ActiveMembersFilter(organizationId)}" +
                     $"&$select={ExternalOrganizationMembership.MemberSelect}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Add("Prefer", $"odata.maxpagesize={OrganizationMemberPageSize}");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var page = await response.Content.ReadFromJsonAsync<MemberPageResult>(ct).ConfigureAwait(false);
        var ids = (page?.Value ?? new List<ExternalOrganizationMembership.ContactOrganizationRow>())
            .Where(r => r.ContactId is { } id && id != Guid.Empty)
            .Select(r => r.ContactId!.Value)
            .ToList();
        return new OrganizationMemberPage(ids, page?.NextLink);
    }

    private sealed class MemberPageResult
    {
        [JsonPropertyName("value")]
        public List<ExternalOrganizationMembership.ContactOrganizationRow>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
    }

    // ── The LIVE contact-state read (task 137 · defect C5) ─────────────────────────────────────────

    /// <summary>
    /// Whether <paramref name="contactId"/> is an ACTIVE contact, read LIVE — never from the 60-second grant cache
    /// or the identity cache (2 minutes since task 132; 10 before). The evaluator consults it on every composition
    /// that could contribute contact-sourced access, so a contact deactivated after sign-in loses that access on its
    /// next request.
    /// </summary>
    /// <remarks>
    /// <para><b>Fail closed.</b> Anything but a successfully read <c>statecode</c> of 0 is not Active: a missing
    /// row is <see cref="ContactRecordState.Inactive"/>, a fault is <see cref="ContactRecordState.Unreadable"/>,
    /// and the evaluator treats both as "confers nothing" (ADR-003, NFR-01).</para>
    /// <para><b>Once per request.</b> A request that composes several entity types (the CIAM principal composes
    /// three) reads the row once: an Active or Inactive answer is remembered in <c>HttpContext.Items</c> for the
    /// rest of that request only — the next request reads again. A fault is not remembered.</para>
    /// <para><b>Why its own read.</b> No existing live read covers every plane: the standing-grant reader reads
    /// the contact row only on the workforce contact plane (and task 142 escalation (d) may retire that term), the
    /// junction read returns nothing for a contact with no membership, and the grant read is cached.</para>
    /// </remarks>
    internal async Task<ContactRecordState> ReadContactStateAsync(Guid contactId, CancellationToken ct)
    {
        var items = _httpContextAccessor?.HttpContext?.Items;
        var key = ContactStateItemKey + contactId.ToString("N");
        if (items is not null && items.TryGetValue(key, out var remembered) && remembered is ContactRecordState known)
        {
            return known;
        }

        ContactRecordState state;
        try
        {
            state = await QueryContactStateAsync(contactId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-ACCESS] The state of Contact {ContactId} could not be read. Failing CLOSED — its grants confer " +
                "nothing on this request (ADR-003).", contactId);
            return ContactRecordState.Unreadable;
        }

        if (items is not null && state != ContactRecordState.Unreadable)
        {
            items[key] = state;
        }

        return state;
    }

    private const string ContactStateItemKey = "uac:contact-state:";

    /// <summary>
    /// The live read behind <see cref="ReadContactStateAsync"/>: <c>contacts({id})?$select=statecode</c>, app-only.
    /// 404 is <see cref="ContactRecordState.Inactive"/> (no row confers nothing); any other non-success status, or a
    /// row without a <c>statecode</c>, is <see cref="ContactRecordState.Unreadable"/>.
    /// </summary>
    /// <remarks><c>internal virtual</c> — the test seam every grant-data double overrides (subclass + override).</remarks>
    internal virtual async Task<ContactRecordState> QueryContactStateAsync(Guid contactId, CancellationToken ct)
    {
        var token = await GetAppOnlyTokenAsync(ct).ConfigureAwait(false);
        var url = $"{GetDataverseApiUrl()}/contacts({contactId:D})?$select=statecode";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ContactRecordState.Inactive;
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "[EXT-ACCESS] Contact-state read FAILED for Contact {ContactId}: {Status}. Failing CLOSED.",
                contactId, response.StatusCode);
            return ContactRecordState.Unreadable;
        }

        var row = await response.Content.ReadFromJsonAsync<ContactStateRow>(ct).ConfigureAwait(false);
        return ContactStateFrom(row?.StateCode);
    }

    /// <summary>The state one successfully read row carries: 0 is Active, any other value Inactive, null Unreadable.</summary>
    internal static ContactRecordState ContactStateFrom(int? stateCode) => stateCode switch
    {
        0 => ContactRecordState.Active,
        null => ContactRecordState.Unreadable,
        _ => ContactRecordState.Inactive,
    };

    private sealed class ContactStateRow
    {
        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }
    }

    /// <summary>
    /// Extracts the Azure AD tenant ID ('tid' claim) from the current HttpContext.
    /// Returns null when no claim is present (in which case caching is skipped).
    /// </summary>
    private string? ExtractTenantId()
    {
        var user = _httpContextAccessor?.HttpContext?.User;
        if (user is null) return null;
        return user.FindFirst("tid")?.Value
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/tenantid")?.Value;
    }

    // ── CIAM contact resolution — MOVED (task 141) ─────────────────────────────────────────────────
    //
    // ResolveExternalContactAsync, ResolveContactByOidAsync, ResolveContactByEmailAsync and BindOidToContactAsync
    // used to live here. They are DELETED, not deprecated, because each carried a defect the binding rule forbids:
    //   • both lookups read $top=1, so two contacts carrying one oid or one email resolved to whichever came
    //     first instead of denying the ambiguity;
    //   • a failed oid read returned null, indistinguishable from "no contact", and fell through to the email
    //     fallback — which could then bind the same oid onto a SECOND contact;
    //   • the oid was compared as a string, and neither query filtered statecode;
    //   • BindOidToContactAsync wrote any oid onto any unbound contact, including an employee's;
    //   • ResolveContactByEmailAsync was public, unguarded and had no callers.
    // CIAM resolution now runs the ONE binding decision both planes share: ContactIdentityBinder.ResolveCiamCallerAsync
    // (Infrastructure/ExternalAccess/ContactIdentityBinder.cs). This class is the grant-DATA reader only.

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
    /// Restricted 100000002), and again 2026-09-30 (session 27).
    /// </summary>
    /// <remarks>
    /// These are the ROOT tables' values, read from root metadata — not from any communication option set.
    /// The same option set backs <c>sprk_accesspermission</c> on To Do, Event, Communication and Document, where it is
    /// a DISPLAY copy of the parent's value (task 173, owner round 81; the communication column was to be retired by
    /// task 138, round 81 keeps it). A child's access comes from its parent root (owner Q6): no access decision reads
    /// a child table's own copy.
    /// </remarks>
    internal const int AccessPermissionRestricted = 100000002;

    /// <summary>
    /// The <c>sprk_accesspermission</c> option value meaning LIMITED (task 138): named, direct contact grants
    /// only. Same live verification as <see cref="AccessPermissionRestricted"/>. A null or Standard
    /// (100000000) value is Standard — today's behaviour, unchanged.
    /// </summary>
    internal const int AccessPermissionLimited = 100000001;

    /// <summary>Ids per flag query. Bounded so a large candidate set cannot produce an over-length URL.</summary>
    private const int FlagQueryChunkSize = 50;

    /// <summary>
    /// Reads the veto flags for a batch of root records (NFR-02: batched — never a per-record round trip).
    /// </summary>
    /// <remarks>
    /// <b>Fail-closed, per NFR-01.</b> Every id the caller asked about is present in the returned map. An id
    /// the query did not return — deleted, filtered, or invisible to the app-only identity — is
    /// indistinguishable from a read that failed, so it comes back as <see cref="RootRecordFlags.Unreadable"/>:
    /// <b>secure, limited AND restricted</b>, with the explicit unreadable marker (task 138). That is the deny
    /// direction: unknown flags suppress derived terms and veto contact-sourced rights, rather than defaulting a
    /// record to open. A transport fault or non-success status does the same for the whole chunk.
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
                            $"&$select={source.IdAttribute},{RootFlagColumns}";

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
                        ? FlagsFrom(row.sprk_issecure, row.sprk_accesspermission, row.statecode)
                        // Asked about, not returned. Cannot be distinguished from an unreadable row.
                        : RootRecordFlags.Unreadable;

                    if (row is not null && row.sprk_issecure is null)
                    {
                        _logger.LogError(
                            "[EXT-ACCESS] sprk_issecure came back EMPTY on {EntityType} {RecordId}. Failing CLOSED — "
                            + "treated as unreadable (secure AND restricted). This service has likely lost its "
                            + "field-level-security Read on the column (scripts/Set-SecureFlagFieldSecurity.ps1 -Verify), "
                            + "or the row predates the backfill (scripts/Repair-SecureFlagNulls.ps1).",
                            entityType, id);
                    }
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

    /// <summary>
    /// Task 174 (owner round 84; #1442): the EFFECTIVE flags of a batch of root records — the most restrictive of each
    /// record's own flags (<see cref="GetRootRecordFlagsAsync"/>) and those of every record it is filed under, through the
    /// ONE filing walk (<see cref="EffectiveRootFlags"/>). Every access decision asks this, not the own-row read: the
    /// read-time cancellation and Restricted veto fold the same walk themselves (<c>AccessibleRecordSetService</c>); the
    /// grant policy, the grantor ceiling, the share-link refusal, the internal-user Restricted bar, the No Access enforcer,
    /// the Restricted share remover and the Assigned-To materializer read it here.
    /// </summary>
    /// <remarks>
    /// <para>The same answer shape and fail-closed contract as <see cref="GetRootRecordFlagsAsync"/> (every asked id of a
    /// flag-bearing type is present; an unreadable one is <see cref="RootRecordFlags.Unreadable"/>), plus: an ancestry that
    /// cannot be decided is <see cref="RootRecordFlags.Unreadable"/> too.</para>
    /// <para><b>Cost.</b> A matter reads exactly what the own-row read reads. A work assignment or project adds the walk —
    /// batched per 200 rows per level (one point read per row for a single record) — except for a record whose own flags
    /// are already unreadable or already Secure AND Restricted, which no ancestor can make stricter.</para>
    /// </remarks>
    public async Task<IReadOnlyDictionary<Guid, RootRecordFlags>> GetEffectiveRootRecordFlagsAsync(
        string entityType, IReadOnlyCollection<Guid> recordIds, CancellationToken ct = default)
    {
        var own = await GetRootRecordFlagsAsync(entityType, recordIds, ct).ConfigureAwait(false);
        return await FoldEffectiveAsync(entityType, own, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Task 174: folds flags the caller already read (<see cref="GetRootRecordFlagsAsync"/>) with what each record is filed
    /// under — for a caller that needs BOTH the record's own flags and its effective ones (the Assigned-To materializer's S5
    /// rule follows the stored ownership, its other rules the effective values). A record whose own flags are unreadable, or
    /// already Secure AND Restricted, is not walked: nothing above it can make it stricter.
    /// </summary>
    public async Task<IReadOnlyDictionary<Guid, RootRecordFlags>> FoldEffectiveAsync(
        string entityType, IReadOnlyDictionary<Guid, RootRecordFlags> own, CancellationToken ct = default)
    {
        var walk = own.Where(kv => !kv.Value.IsUnreadable && !(kv.Value.IsSecure && kv.Value.IsRestricted))
            .Select(kv => kv.Key).ToList();
        if (walk.Count == 0)
        {
            return own;
        }

        var ancestry = await EffectiveRootFlags.ReadAncestryAsync(_filing, _logger, entityType, walk, ct).ConfigureAwait(false);
        var folded = EffectiveRootFlags.Fold(walk.ToDictionary(id => id, id => own[id]), ancestry);
        return own.ToDictionary(kv => kv.Key, kv => folded.TryGetValue(kv.Key, out var f) ? f : kv.Value);
    }

    /// <summary>
    /// Task 174 (task 067's amendment): ONE record's effective access for display — the effective flags and the record they
    /// are inherited from (<see cref="EffectiveRootFlags.InheritedFrom"/>). An id the own-row read did not return is
    /// <see cref="RootRecordFlags.Unreadable"/>, as at write time.
    /// </summary>
    public async Task<EffectiveRootAccess> GetEffectiveRootAccessAsync(string entityType, Guid recordId, CancellationToken ct = default)
    {
        var own = await GetRootRecordFlagsAsync(entityType, new[] { recordId }, ct).ConfigureAwait(false);
        var flags = own.TryGetValue(recordId, out var f) ? f : RootRecordFlags.Unreadable;
        if (flags.IsUnreadable)
        {
            return new EffectiveRootAccess(flags, null);
        }

        var ancestry = await EffectiveRootFlags.ReadAncestryAsync(_filing, _logger, entityType, new[] { recordId }, ct)
            .ConfigureAwait(false);
        var answer = ancestry?.GetValueOrDefault(recordId);
        return new EffectiveRootAccess(EffectiveRootFlags.Fold(flags, answer), EffectiveRootFlags.InheritedFrom(flags, answer));
    }

    /// <summary>
    /// The flags one SUCCESSFULLY read row carries (task 138 — extracted so the column-to-flag mapping is
    /// asserted directly, without an HTTP stack).
    /// </summary>
    /// <remarks>
    /// <para><b>An EMPTY <c>sprk_issecure</c> is <see cref="RootRecordFlags.Unreadable"/></b> (task 150, round 17
    /// item 3) — exactly as <c>RecordContainerResolver</c> refuses it (<c>secure_flag_unreadable</c>). Since task 150
    /// every row holds true or false (the one-time backfill <c>scripts/Repair-SecureFlagNulls.ps1</c>; the column
    /// defaults to No) and the column is field-secured, so an empty value means the app identity has lost its
    /// field-level Read and the TRUE value was masked. Reading that as "not secure" would let a derived-member,
    /// standing-grant or org-expansion term reach a record that may be secure; the fail-closed answer is the same one
    /// a failed read gets, with its unreadable marker, so the write-time grant policy reports "could not be read".
    /// Every consumer of this reader inherits it (the read-time evaluator, the grant policy, the internal user-share
    /// last-reader rule).</para>
    /// <para>A null <c>sprk_accesspermission</c> is Standard — today's behaviour, unchanged (task 138).</para>
    /// <para><b>Task 137 · defect C5 — the root's own state.</b> Only <c>statecode</c> 0 is active. A non-zero OR
    /// NULL state is INACTIVE (fail closed, NFR-01): Dataverse never writes a null <c>statecode</c>, so a row
    /// without one is a row whose state was not read. No default for the parameter — every caller says what it
    /// read.</para>
    /// </remarks>
    internal static RootRecordFlags FlagsFrom(bool? isSecure, int? accessPermission, int? stateCode)
        => isSecure is null
            ? RootRecordFlags.Unreadable
            : new(
                IsSecure: isSecure == true,
                IsRestricted: accessPermission == AccessPermissionRestricted,
                IsLimited: accessPermission == AccessPermissionLimited,
                IsInactive: stateCode != 0);

    /// <summary>
    /// The flag read's columns besides the id: the two policy flags (tasks 037, 138) and the row's own
    /// <c>statecode</c> (task 137 · C5) — in the SAME batched read, so the inactive-root rule costs no round trip.
    /// </summary>
    internal const string RootFlagColumns = "sprk_issecure,sprk_accesspermission,statecode";

    /// <summary>
    /// Whether <paramref name="entityType"/> (a LOGICAL name, e.g. <c>sprk_project</c>) is a key of the flag
    /// sources — i.e. whether <see cref="GetRootRecordFlagsAsync"/> reads anything for it at all.
    /// </summary>
    /// <remarks>
    /// Exists for the write-time grant policy (task 138). For any other type the flag read returns an EMPTY
    /// map, which the read path treats as "no veto"; at write time an absent id must instead mean
    /// "unreadable". A test pins that every grant root type's logical name answers <c>true</c> here, so a
    /// renamed key or a wrong name cannot silently turn the policy off.
    /// </remarks>
    internal static bool IsFlagBearingRootType(string? entityType)
        => entityType is not null && RootFlagSources.ContainsKey(entityType);

    /// <summary>Projection of the flag columns. Ids arrive as strings over OData.</summary>
    private sealed class RootFlagRow
    {
        public string? sprk_projectid { get; set; }
        public string? sprk_matterid { get; set; }
        public string? sprk_workassignmentid { get; set; }
        public bool? sprk_issecure { get; set; }
        public int? sprk_accesspermission { get; set; }

        /// <summary>The root's own state (task 137 · C5): Active(0) / Inactive(1). Null reads as inactive.</summary>
        public int? statecode { get; set; }

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
    /// The org-typed lookups of <paramref name="entityType"/> that a No Access entry's organization reaches it through (the
    /// same registry <see cref="GetReferencedOrganizationIdsAsync"/> reads) — empty for a type that has none. Task 158 r1:
    /// the No Access list of a secure record that is ABOUT to be created is read from these columns of its create payload.
    /// </summary>
    internal static IReadOnlyList<string> OrganizationLookupAttributesOf(string entityType) =>
        OrganizationLookupAttributes.TryGetValue(entityType ?? string.Empty, out var attributes)
            ? attributes
            : Array.Empty<string>();

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

    // ── Reverse reads for the No Access enforcer (task 143) ──────────────────────────────────────

    /// <summary>
    /// The SECURE root records of <paramref name="entityType"/> that reference <paramref name="organizationId"/> in
    /// ANY org-typed lookup — the record side of an organization-object No Access entry, read in reverse (task 143).
    /// </summary>
    /// <remarks>
    /// <para>The same column registry as <see cref="GetReferencedOrganizationIdsAsync"/> (ANY reference, the B-10
    /// over-match), so "which records does this wall cover" and "which walls cover this record" cannot disagree.</para>
    /// <para><b>Throws on any fault</b> — the enforcer records a failed run, never "nothing covered". At most
    /// <paramref name="maxRows"/> ids; one more row than that reports <c>Truncated</c>, never a silent prefix.</para>
    /// <para>An entity type with no org-typed lookups covers nothing: a static schema fact, not a fault.</para>
    /// </remarks>
    public virtual Task<(IReadOnlyList<Guid> RecordIds, bool Truncated)> FindSecureRootsReferencingOrganizationAsync(
        string entityType, Guid organizationId, int maxRows, CancellationToken ct = default)
        => FindRootsReferencingOrganizationAsync(entityType, organizationId, maxRows, "sprk_issecure eq true", ct);

    /// <summary>
    /// Task 174 (verifier pass 2 F1): the root records of <paramref name="entityType"/> that reference
    /// <paramref name="organizationId"/> and are NOT flagged secure — the candidates an organization-object No Access entry
    /// still covers when they are secure through what they are filed under (owner round 84). The enforcer folds them with
    /// the filing walk; this read decides nothing on its own. Same registry, cap and fault contract as
    /// <see cref="FindSecureRootsReferencingOrganizationAsync"/>.
    /// </summary>
    public virtual Task<(IReadOnlyList<Guid> RecordIds, bool Truncated)> FindUnflaggedRootsReferencingOrganizationAsync(
        string entityType, Guid organizationId, int maxRows, CancellationToken ct = default)
        => FindRootsReferencingOrganizationAsync(entityType, organizationId, maxRows, UnflaggedRootFilter, ct);

    /// <summary>
    /// The flag half of <see cref="FindUnflaggedRootsReferencingOrganizationAsync"/>'s <c>$filter</c>: not flagged secure,
    /// a BLANK flag included. Dataverse's <c>ne</c> excludes nulls (SQL <c>&lt;&gt;</c>), so <c>ne true</c> alone would skip
    /// a record whose <c>sprk_issecure</c> is empty — and leave its share in place (fail open). Extracted so the test double
    /// evaluates the very filter production sends.
    /// </summary>
    internal const string UnflaggedRootFilter = "(sprk_issecure ne true or sprk_issecure eq null)";

    private async Task<(IReadOnlyList<Guid> RecordIds, bool Truncated)> FindRootsReferencingOrganizationAsync(
        string entityType, Guid organizationId, int maxRows, string flagFilter, CancellationToken ct)
    {
        if (organizationId == Guid.Empty ||
            !RootFlagSources.TryGetValue(entityType ?? string.Empty, out var source) ||
            !OrganizationLookupAttributes.TryGetValue(entityType ?? string.Empty, out var orgAttributes) ||
            orgAttributes.Count == 0)
        {
            return (Array.Empty<Guid>(), false);
        }

        var token = await GetAppOnlyTokenAsync(ct);
        var apiUrl = GetDataverseApiUrl();
        var orgFilter = string.Join(" or ", orgAttributes.Select(a => $"_{a}_value eq {organizationId}"));
        var query = $"{apiUrl}/{source.Collection}" +
                    $"?$filter={flagFilter} and ({orgFilter})" +
                    $"&$select={source.IdAttribute}&$top={maxRows + 1}";

        var ids = await ReadIdColumnAsync(query, token, source.IdAttribute, ct);
        return ids.Count > maxRows ? (ids.Take(maxRows).ToList(), true) : (ids, false);
    }

    /// <summary>
    /// The contacts an organization-subject No Access entry walls off (task 143): every contact with an ACTIVE
    /// <c>sprk_contactorganization</c> row for <paramref name="organizationId"/> — bounded on <c>statecode</c> ONLY,
    /// the same WALL set the deny veto reads (owner D-2 part 2 / D-10), never the date-bounded conferring set.
    /// </summary>
    /// <remarks>Throws on any fault; one row beyond <paramref name="maxRows"/> reports <c>Truncated</c>.</remarks>
    public virtual async Task<(IReadOnlyList<Guid> ContactIds, bool Truncated)> FindWallMemberContactsAsync(
        Guid organizationId, int maxRows, CancellationToken ct = default)
    {
        if (organizationId == Guid.Empty)
        {
            return (Array.Empty<Guid>(), false);
        }

        var token = await GetAppOnlyTokenAsync(ct);
        var apiUrl = GetDataverseApiUrl();
        var query = $"{apiUrl}/sprk_contactorganizations" +
                    $"?$filter={BuildOrganizationMembershipFilterForOrganization(organizationId)}" +
                    $"&$select=_sprk_contact_value&$top={maxRows + 1}";

        var rows = await ReadIdColumnAsync(query, token, "_sprk_contact_value", ct);
        var truncated = rows.Count > maxRows;
        return ((truncated ? rows.Take(maxRows) : rows).Distinct().ToList(), truncated);
    }

    /// <summary>One GET; the named GUID column of every row. Any non-success status throws.</summary>
    private async Task<List<Guid>> ReadIdColumnAsync(string query, string token, string column, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, query);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("OData-MaxVersion", "4.0");
        request.Headers.Add("OData-Version", "4.0");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var ids = new List<Guid>();
        if (doc.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in value.EnumerateArray())
            {
                if (row.TryGetProperty(column, out var cell) && cell.ValueKind == JsonValueKind.String &&
                    Guid.TryParse(cell.GetString(), out var id) && id != Guid.Empty)
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
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
                // Task 132 (C12): a non-2xx — a 429 throttle and a 5xx included — is a FAULT, not "no grants". The
                // request still composes no grant-derived access; the set is marked so it is never cached.
                _logger.LogWarning("[EXT-ACCESS] Dataverse query failed for Contact {ContactId}: {Status} (faulted, not cached)",
                    contactId, response.StatusCode);
                return ExternalGrantSet.Unreadable;
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ExternalAccessRow>>(ct);
            if (result?.Value is null)
            {
                // A 2xx without a value array is not a Dataverse answer.
                _logger.LogWarning("[EXT-ACCESS] Grant query for Contact {ContactId} returned no value array (faulted, not cached)",
                    contactId);
                return ExternalGrantSet.Unreadable;
            }

            var rows = WithoutInactiveOrganizations(result.Value, contactId);

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
            // ⚠️ NOTE THE ASYMMETRY WITH `projects` ABOVE. The project filter requires
            // `sprk_accesslevel.HasValue` and drops rows without one; here the level is carried as NULLABLE
            // and the row is kept, so this read stays a faithful copy of the rows. A null level contributes
            // AccessRights.None, which the highest-wins max cannot widen.
            //
            // OWNER DECISION (2026-09-30), applied by task 136 (defect C2): NO LEVEL = NOT GRANTED. This
            // comment used to call dropping such a row a "SILENT REVOCATION" and keep its id as a key so set
            // membership stayed unchanged — but a key with no rights still admitted every presence-gated read.
            // The owner's rule is that a contact gets only granted records at the granted level, so the
            // evaluator now removes any record without Read at the end of every composition
            // (AccessibleRecordSetService.RemoveEntriesWithoutRead). The grant query itself is unchanged here
            // on purpose (C12 and task 137 own this file's other changes). Dev before-state, 2026-10-01: 0 rows
            // with a null level in any state — notes/task-136-rights-based-read-gates.md §2.
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
            //
            // Task 132 (C12): ...and marks the set FAULTED, so the direct grants read above are returned for THIS
            // request but nothing is cached — a set missing every organization grant is not stored for 60 s. The
            // junction fault comes from task 109's outcome (ActiveOrgMemberships.Unreadable), not a second read.
            var (orgRows, orgTermFaulted) = await QueryOrganizationGrantRowsAsync(contactId, token, apiUrl, ct);
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
                "[EXT-ACCESS] Loaded grants for Contact {ContactId}: {Projects} project / {Matters} matter / {Was} work-assignment (incl. {OrgRows} org-grant rows; org term faulted: {OrgTermFaulted})",
                contactId, projects.Count, matters.Count, workAssignments.Count, orgRows.Count, orgTermFaulted);

            return new ExternalGrantSet
            {
                Projects = projects,
                MatterGrants = matters,
                WorkAssignmentGrants = workAssignments,
                Faulted = orgTermFaulted,
            };
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Task 132 (C12): the CALLER cancelled (a client abort). Propagate — swallowing it here returned an empty
            // set that was then cached for 60 s. An HttpClient TIMEOUT also arrives as OperationCanceledException,
            // with the caller's token NOT cancelled: it falls through to the fault arm below.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying Dataverse for Contact {ContactId} (faulted, not cached)", contactId);
            return ExternalGrantSet.Unreadable;
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
    /// <para><b>Faulted is carried, not just logged</b> (task 132 · C12). The second value is true when the
    /// junction read reported <see cref="ActiveOrgMemberships.Unreadable"/> (task 109's outcome, consumed as is —
    /// no second read) or the organization-grant read failed. <see cref="QueryGrantSetAsync"/> marks the whole set
    /// faulted, so it is returned for this request and never cached.</para>
    /// </remarks>
    private async Task<(List<ExternalAccessRow> Rows, bool Faulted)> QueryOrganizationGrantRowsAsync(
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
                    "junction was unreadable. A fault must not grant (ADR-003), and the grant set is not cached " +
                    "(task 132).", contactId);
            }

            return (new List<ExternalAccessRow>(), memberships.Unreadable);
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
                    "[EXT-ACCESS] Org-grant query failed for Contact {ContactId} ({OrgCount} orgs): {Status} (faulted, not cached)",
                    contactId, orgIds.Count, response.StatusCode);
                return (new List<ExternalAccessRow>(), true);
            }

            var result = await response.Content.ReadFromJsonAsync<DataverseQueryResult<ExternalAccessRow>>(ct);
            if (result?.Value is null)
            {
                _logger.LogWarning(
                    "[EXT-ACCESS] Org-grant query for Contact {ContactId} returned no value array (faulted, not cached)",
                    contactId);
                return (new List<ExternalAccessRow>(), true);
            }

            // Belt-and-braces for ISS-026: the conferring set already excluded inactive organizations,
            // so a row here under an inactive one means its organization changed state between the two
            // reads. The guard is the same one the contact-grant read applies.
            return (WithoutInactiveOrganizations(result.Value, contactId), false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Task 132: the caller cancelled — propagate (QueryGrantSetAsync rethrows it and caches nothing). A
            // timeout (the caller's token NOT cancelled) is a fault, below.
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[EXT-ACCESS] Error querying org grants for Contact {ContactId} (faulted, not cached)", contactId);
            return (new List<ExternalAccessRow>(), true);
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

        // The query rethrows the caller's cancellation itself (task 132). This check also covers a cancellation that
        // lands just after the read completed, so a cancelled request never composes an answer nobody is waiting for.
        ct.ThrowIfCancellationRequested();
        return memberships;
    }

    // ── Colleagues by email (task 140: contact-side Grant Access, owner Q2) ─────────────────────────────

    /// <summary>
    /// How many organizations one colleague-by-email query names. The grantor's conferring organizations are few; the
    /// bound keeps the <c>$filter</c> disjunction (and the URL) inside Dataverse's limits for any count, and every chunk
    /// is read, so no organization is ever left out (the round-18 rule: no deterministic "too many" state).
    /// </summary>
    internal const int ColleagueOrganizationChunkSize = 25;

    /// <summary>
    /// The ACTIVE contacts whose <c>emailaddress1</c> is <paramref name="email"/> AND who hold a CONFERRING membership
    /// in at least one of <paramref name="organizationIds"/> — "by email among active org members" (task 140 POML step
    /// 4), never a system-wide email lookup.
    /// </summary>
    /// <remarks>
    /// <para><b>Why scoped in the query and not after it.</b> A system-wide lookup answers about people outside the
    /// grantor's organizations: a colleague sharing an email with an outsider read as "ambiguous", and the refusal told the
    /// grantor a non-colleague with that address exists (task 140 verifier r1, items 3 and 11). Here only memberships of
    /// the grantor's own organizations are read, so nobody else can be counted, matched or disclosed.</para>
    /// <para><b>One rule for "member".</b> The junction rows are judged by <see cref="ProjectOrganizationMemberships"/>
    /// — the same conferring rule (statecode, start/end dates, active organization) the grantee's own membership read
    /// applies (task 109) — so the email path and the contact-id path can never disagree about who is a colleague.</para>
    /// <para><b>Faults.</b> A read that could not be completed in ANY chunk answers <see cref="ColleagueEmailMatch.Failed"/>
    /// — never "nobody": the caller reports a fault (503), not "not a member". The caller's own cancellation
    /// propagates.</para>
    /// </remarks>
    internal async Task<ColleagueEmailMatch> FindConferringMembersByEmailAsync(
        IReadOnlyCollection<Guid> organizationIds, string email, CancellationToken ct = default)
    {
        var organizations = organizationIds.Where(id => id != Guid.Empty).Distinct().ToList();
        var wanted = email?.Trim();
        if (organizations.Count == 0 || string.IsNullOrEmpty(wanted))
        {
            return ColleagueEmailMatch.None;
        }

        var rows = new List<ColleagueMembershipRow>();
        try
        {
            for (var i = 0; i < organizations.Count; i += ColleagueOrganizationChunkSize)
            {
                var chunk = organizations.Skip(i).Take(ColleagueOrganizationChunkSize).ToList();
                rows.AddRange(await ReadColleagueMembershipRowsAsync(chunk, wanted, ct).ConfigureAwait(false));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[EXT-ACCESS] The colleague-by-email membership read failed for {Count} organization(s); reporting it as " +
                "UNREADABLE (never as 'no colleague').", organizations.Count);
            return ColleagueEmailMatch.Failed;
        }

        return new ColleagueEmailMatch(ProjectColleaguesByEmail(rows, organizations, wanted, TodayUtc), Unreadable: false);
    }

    /// <summary>
    /// Pure: which contacts the junction rows of one colleague-by-email read make colleagues — an expanded, ACTIVE contact
    /// whose email equals <paramref name="email"/> (trimmed, case-insensitive — Dataverse's own comparison), holding a
    /// CONFERRING membership (<see cref="ProjectOrganizationMemberships"/>) in one of <paramref name="organizationIds"/>.
    /// </summary>
    /// <remarks>The <c>$filter</c> bounds what comes back; this decides what it means (task 117's discipline), so a filter
    /// that grew too broad costs a read and never adds a colleague. A row whose contact did not come back expanded names
    /// nobody (fail closed on an additive decision).</remarks>
    internal static IReadOnlyList<Guid> ProjectColleaguesByEmail(
        IEnumerable<ColleagueMembershipRow> rows, IReadOnlyCollection<Guid> organizationIds, string email, DateOnly today)
    {
        var wanted = email.Trim();
        return rows
            .Where(r => r.ContactId is { } id && id != Guid.Empty
                        && r.OrganizationId is { } org && organizationIds.Contains(org)
                        && r.Contact is { } contact
                        && IsActiveState(contact.StateCode)
                        && string.Equals(contact.Email?.Trim(), wanted, StringComparison.OrdinalIgnoreCase))
            .GroupBy(r => r.ContactId!.Value)
            .Where(g => ProjectOrganizationMemberships(g.Select(r => r.ToContactOrgRow()), today)
                .ConferringOrganizationIds.Any(organizationIds.Contains))
            .Select(g => g.Key)
            .OrderBy(id => id)
            .ToList();
    }

    /// <summary>
    /// The <c>$filter</c> of one colleague-by-email chunk: memberships of these organizations under the WALL state clause
    /// (the dates and the organization's state are decided in code), whose contact is active and carries the email.
    /// The email is an OData string literal — quotes doubled, then URL-encoded (<see cref="DataverseContactIdentityStore.Literal"/>)
    /// — so no caller value can change the query (session 27 round 16 item 3).
    /// </summary>
    internal static string BuildColleagueByEmailFilter(IReadOnlyCollection<Guid> organizationIds, string email)
        => $"({string.Join(" or ", organizationIds.Select(id => $"_sprk_organization_value eq {id:D}"))})" +
           $" and {WallMembershipStateClause}" +
           $" and sprk_Contact/emailaddress1 eq '{DataverseContactIdentityStore.Literal(email.Trim())}'" +
           " and sprk_Contact/statecode eq 0";

    /// <summary>
    /// Columns and expansions of the colleague-by-email read: the membership columns the conferring rule needs, the
    /// parent organization's state (ISS-026) and the contact's state and email (re-decided in code). Navigation
    /// properties <c>sprk_Contact</c> / <c>sprk_Organization</c> live-verified 2026-10-04 (read-only metadata GET).
    /// </summary>
    internal const string ColleagueByEmailSelect =
        "_sprk_contact_value," + OrganizationMembershipSelect;

    internal const string ColleagueByEmailExpand =
        OrganizationStateExpand + ",sprk_Contact($select=contactid,statecode,emailaddress1)";

    /// <summary>
    /// One colleague-by-email chunk, following <c>@odata.nextLink</c> to the end. Throws on any failure (a fault is never
    /// an empty answer).
    /// </summary>
    /// <remarks><c>internal virtual</c>: the test seam this class uses for every read (subclass + override; no HTTP double,
    /// ADR-038 B1).</remarks>
    internal virtual async Task<IReadOnlyList<ColleagueMembershipRow>> ReadColleagueMembershipRowsAsync(
        IReadOnlyCollection<Guid> organizationIds, string email, CancellationToken ct)
    {
        var token = await GetAppOnlyTokenAsync(ct).ConfigureAwait(false);
        string? next = $"{GetDataverseApiUrl()}/sprk_contactorganizations" +
                       $"?$filter={BuildColleagueByEmailFilter(organizationIds, email)}" +
                       $"&$select={ColleagueByEmailSelect}" +
                       $"&$expand={ColleagueByEmailExpand}";

        var rows = new List<ColleagueMembershipRow>();
        var pages = 0;
        while (next is not null)
        {
            if (++pages > MaxOrganizationMemberPages)
            {
                throw new InvalidOperationException(
                    $"The colleague-by-email read did not finish within {MaxOrganizationMemberPages} pages.");
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, next);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add("OData-MaxVersion", "4.0");
            request.Headers.Add("OData-Version", "4.0");
            request.Headers.Add("Prefer", $"odata.maxpagesize={OrganizationMemberPageSize}");
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync<ColleagueMembershipPage>(ct).ConfigureAwait(false);
            rows.AddRange(page?.Value ?? new List<ColleagueMembershipRow>());
            next = page?.NextLink;
        }

        return rows;
    }

    private sealed class ColleagueMembershipPage
    {
        [JsonPropertyName("value")]
        public List<ColleagueMembershipRow>? Value { get; set; }

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; set; }
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
    /// is not the caller's, and that is a fault. The caller's OWN cancellation propagates (task 132 — see the
    /// catch for why task 109's "report it as Failed" no longer applies).</para>
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
            // The CALLER cancelled (a client abort) — propagate, on BOTH paths that run this query (task 132 · C12,
            // criterion 3). Task 109 reported it as Failed instead, because QueryGrantSetAsync's catch-all then
            // turned an escaping cancellation into an EMPTY grant set and cached it. Task 132 made that catch rethrow
            // the caller's cancellation and gated the cache write, so the reason is gone: reporting it as Failed now
            // only made GetGrantSetAsync RETURN a faulted set to a request nobody is waiting for. An HttpClient
            // TIMEOUT (the caller's token NOT cancelled) is still a fault, in the catch below.
            throw;
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

    /// <summary>
    /// One junction row of the colleague-by-email read (task 140): a <see cref="ContactOrgRow"/> plus the member contact
    /// and its expanded state and email.
    /// </summary>
    internal sealed class ColleagueMembershipRow
    {
        [JsonPropertyName("_sprk_contact_value")]
        public Guid? ContactId { get; set; }

        [JsonPropertyName("_sprk_organization_value")]
        public Guid? OrganizationId { get; set; }

        [JsonPropertyName("sprk_startdate")]
        public DateOnly? StartDate { get; set; }

        [JsonPropertyName("sprk_enddate")]
        public DateOnly? EndDate { get; set; }

        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }

        [JsonPropertyName("sprk_Organization")]
        public OrganizationStateRow? Organization { get; set; }

        [JsonPropertyName("sprk_Contact")]
        public ColleagueContactRow? Contact { get; set; }

        /// <summary>The membership part, for <see cref="ProjectOrganizationMemberships"/>.</summary>
        internal ContactOrgRow ToContactOrgRow() => new()
        {
            OrganizationId = OrganizationId,
            StartDate = StartDate,
            EndDate = EndDate,
            StateCode = StateCode,
            Organization = Organization,
        };
    }

    /// <summary>The expanded member contact of a <see cref="ColleagueMembershipRow"/>: its state and email only.</summary>
    internal sealed class ColleagueContactRow
    {
        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }

        [JsonPropertyName("emailaddress1")]
        public string? Email { get; set; }
    }

    /// <summary>
    /// The answer of <see cref="FindConferringMembersByEmailAsync"/>: the colleagues the email names (zero, one, or
    /// several), or <see cref="Unreadable"/> when the read could not be completed.
    /// </summary>
    internal sealed record ColleagueEmailMatch(IReadOnlyList<Guid> ContactIds, bool Unreadable)
    {
        public static ColleagueEmailMatch None { get; } = new(Array.Empty<Guid>(), Unreadable: false);

        public static ColleagueEmailMatch Failed { get; } = new(Array.Empty<Guid>(), Unreadable: true);
    }

    /// <summary>The expanded parent <c>sprk_organization</c> — its state only (task 109 · ISS-026).</summary>
    internal sealed class OrganizationStateRow
    {
        /// <summary>Active(0) / Inactive(1), live-verified 2026-09-30. Null is ACTIVE (<see cref="IsActiveState"/>).</summary>
        [JsonPropertyName("statecode")]
        public int? StateCode { get; set; }
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

/// <summary>
/// What one call of <see cref="ExternalParticipationService.InvalidateGrantSetsAsync"/> did (task 137): how many
/// contacts were affected, how many key removals landed or failed, and which organizations could not be expanded to
/// every member. Informational — a write endpoint never changes its answer because of it.
/// </summary>
public sealed record GrantCacheInvalidation(
    int ContactCount,
    int RemovalsSucceeded,
    int RemovalsFailed,
    IReadOnlyList<Guid> OrganizationsNotFullyExpanded);

/// <summary>An organization's active members, and whether the walk read them all (task 137).</summary>
internal sealed record OrganizationMemberWalk(IReadOnlyList<Guid> ContactIds, bool Complete);

/// <summary>One page of an organization's active members and the server's next-page link (task 137).</summary>
internal sealed record OrganizationMemberPage(IReadOnlyList<Guid> ContactIds, string? NextLink);

/// <summary>
/// A contact's own state as the evaluator sees it (task 137 · defect C5). The DEFAULT value is
/// <see cref="Unreadable"/> on purpose: an uninitialized or defaulted answer fails closed.
/// </summary>
internal enum ContactRecordState
{
    /// <summary>The state could not be read. Confers nothing (fail closed).</summary>
    Unreadable = 0,

    /// <summary><c>statecode</c> 0 — the only state whose grants confer access.</summary>
    Active = 1,

    /// <summary>Deactivated, or no such row. Confers nothing.</summary>
    Inactive = 2,
}
