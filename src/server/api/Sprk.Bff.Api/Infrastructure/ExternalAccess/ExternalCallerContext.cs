using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Represents the resolved context for an authenticated external caller (Power Pages Contact).
/// Set on HttpContext.Items by ExternalCallerAuthorizationFilter and consumed by downstream handlers.
/// </summary>
public sealed class ExternalCallerContext
{
    public static readonly object HttpContextItemsKey = new();

    /// <summary>
    /// The Dataverse Contact ID for the authenticated external user.
    /// </summary>
    public required Guid ContactId { get; init; }

    /// <summary>
    /// The external user's email / UPN (from token claims). May be empty for an oid-resolved
    /// CIAM caller whose token carries no email claim.
    /// </summary>
    public required string Email { get; init; }

    /// <summary>
    /// The stable CIAM object id ('oid') the caller was resolved by (Contact.sprk_externalobjectid),
    /// per ADR-028 Amendment A1. Null on a transitional email-only resolution.
    /// </summary>
    public string? Oid { get; init; }

    /// <summary>
    /// List of active project participations for this Contact.
    /// </summary>
    public required IReadOnlyList<ExternalParticipation> Participations { get; init; }

    /// <summary>
    /// Whether this context was loaded from Redis cache.
    /// </summary>
    public bool FromCache { get; init; }

    /// <summary>
    /// Whether the Contact holds <see cref="AccessRights.Read"/> on the specified project.
    /// </summary>
    /// <remarks>
    /// unified-access-control-r2 task 136 (defect C2): was "a participation for the id exists", the same
    /// presence shape as <c>CallerPrincipal.HasProjectAccess</c>. A participation always carries a level today,
    /// so the answer does not change; the shape is aligned so no copy of the presence gate survives to be
    /// imitated. No production handler reads this class any more — the /api/v1/external routes read
    /// <c>CallerPrincipal</c>.
    /// </remarks>
    public bool HasProjectAccess(Guid projectId) =>
        GetEffectiveRights(projectId).HasFlag(AccessRights.Read);

    /// <summary>
    /// Gets the access level for the specified project, or null if no access.
    /// </summary>
    public ExternalAccessLevel? GetAccessLevel(Guid projectId) =>
        Participations.FirstOrDefault(p => p.ProjectId == projectId)?.AccessLevel;

    /// <summary>
    /// Gets the effective AccessRights for the specified project based on access level.
    /// </summary>
    public AccessRights GetEffectiveRights(Guid projectId) =>
        ExternalAccessLevels.ToAccessRights(GetAccessLevel(projectId));

    /// <summary>
    /// Gets all project IDs the Contact can READ (for AI search filter construction). Read-gated by task 136.
    /// </summary>
    public IEnumerable<Guid> GetAccessibleProjectIds() =>
        Participations.Where(p => HasProjectAccess(p.ProjectId)).Select(p => p.ProjectId);
}

/// <summary>
/// A single external access grant for a Contact → Project relationship.
/// </summary>
public sealed class ExternalParticipation
{
    public required Guid ProjectId { get; init; }

    /// <summary>The effective granted level — the highest across ALL sources (direct + org-inherited).</summary>
    public required ExternalAccessLevel AccessLevel { get; init; }

    /// <summary>
    /// The highest level from the caller's <b>own</b> grant rows only, excluding anything inherited via an
    /// organization grant. <c>null</c> when every contributing row was org-inherited (task 037 / FR-22).
    /// </summary>
    /// <remarks>
    /// <b>Additive on purpose.</b> Secure records suppress org-inherited access but keep direct personal
    /// grants, and that decision needs both numbers for the SAME record — so the dedupe must not be allowed
    /// to collapse them into one.
    /// <para>
    /// The alternative — deduping by <c>(recordId, provenance)</c> and keeping two entries — was rejected:
    /// duplicate ids would reach <c>CallerPrincipal.ProjectAccess</c>, whose <c>GetEffectiveRights</c> does
    /// <c>FirstOrDefault</c> and would silently return whichever entry happened to come first. A wrong
    /// authorization answer that depends on list order is strictly worse than an extra field.
    /// </para>
    /// </remarks>
    public ExternalAccessLevel? DirectAccessLevel { get; init; }
}

/// <summary>
/// A single <c>sprk_externalrecordaccess</c> grant against a NON-project root (matter or work
/// assignment): the granted record id plus the level the grant row carries.
/// <para>
/// unified-access-control-r2 task 032 (FR-19). Before this, matter/WA grants were reduced to a bare
/// <c>Guid</c> at partitioning while the level sat unread on the row — <c>GrantRowSelect</c> has always
/// requested <c>sprk_accesslevel</c> for every row. That is the structural reason matters and work
/// assignments had no access level anywhere in the pipeline (register A-8 / B-8).
/// </para>
/// <para>
/// ⚠️ <b>Nullable by design.</b> The level is carried as read, null included: a null maps to
/// <see cref="AccessRights.None"/>, which the highest-wins max cannot widen. The row is kept here (not
/// filtered like the PROJECT partition's <c>&amp;&amp; r.sprk_accesslevel.HasValue</c>) only so the grant
/// read stays a faithful copy of the rows; it no longer grants anything.
/// </para>
/// <para>
/// <b>No level = not granted (owner, 2026-09-30; unified-access-control-r2 task 136 · defect C2).</b> This
/// paragraph used to argue that dropping a level-less row would be a "silent REVOCATION", and the id was kept
/// as a key so set membership stayed unchanged. That key had no rights, yet every presence-gated read admitted
/// it — the module /fetch and /record for the matter or work assignment and its documents and invoices. The
/// owner's rule is that a contact gets only the records it is granted, at the granted level, so the evaluator
/// now removes the record at the end of every composition. Measured before the change (2026-10-01, dev): 0 grant
/// rows with a null level in any state, of 31 active — so nothing live was revoked. Only rows written outside
/// the BFF can lack a level; the BFF always writes one.
/// </para>
/// </summary>
public sealed class ExternalRootGrant
{
    public required Guid RecordId { get; init; }

    /// <summary>The effective granted level — the highest across ALL sources (direct + org-inherited).</summary>
    public required ExternalAccessLevel? AccessLevel { get; init; }

    /// <summary>
    /// The highest level from the caller's <b>own</b> grant rows only, excluding org-inherited ones.
    /// <c>null</c> when every contributing row was org-inherited (task 037 / FR-22) — see
    /// <see cref="ExternalParticipation.DirectAccessLevel"/> for why this is additive rather than a
    /// second entry per record.
    /// </summary>
    public ExternalAccessLevel? DirectAccessLevel { get; init; }
}

/// <summary>
/// The access-policy flags on a root record (task 037 · FR-21 / FR-22; task 138 · Limited).
/// </summary>
/// <param name="IsSecure"><c>sprk_issecure</c> — makes the record DIRECT-ONLY for contacts (see
/// <see cref="IsDirectOnly"/>).</param>
/// <param name="IsRestricted"><c>sprk_accesspermission == Restricted</c> — removes every
/// contact-sourced contribution AFTER the max.</param>
/// <param name="IsLimited"><c>sprk_accesspermission == Limited</c> — makes the record DIRECT-ONLY for
/// contacts, exactly as Secure does (task 138; teams-app-r1 FR-14 "Option A": named grants only).</param>
/// <param name="IsUnreadable">
/// <c>true</c> only when the flags could NOT be read (a fault, a non-success status, or an id the query did
/// not return). The read path never needs it — the other three fields already carry the most restrictive
/// combination — but the WRITE-time grant policy does: it must tell an operator "the record's settings could
/// not be read" rather than "the record is Restricted", which would be false (task 138).
/// </param>
/// <param name="IsInactive">
/// The root record's own <c>statecode</c> is not Active (task 137 · defect C5). An inactive project, matter or
/// work assignment confers NOTHING contact-sourced — removed after the max exactly as Restricted removes it, with
/// the same survivor rule (a systemuser's own membership term stays). A read-time rule, not a grant write, so
/// reactivating the record restores access with no data change.
/// </param>
/// <remarks>
/// The flags are independent, so a record can carry any combination. They are carried together because they
/// come from the same row and the same read. <see cref="IsLimited"/>, <see cref="IsUnreadable"/> and
/// <see cref="IsInactive"/> are optional so a value built with only the first two (every pre-138 call site)
/// means what it always meant.
/// </remarks>
public readonly record struct RootRecordFlags(
    bool IsSecure, bool IsRestricted, bool IsLimited = false, bool IsUnreadable = false, bool IsInactive = false)
{
    /// <summary>
    /// Whether the post-max veto removes every CONTACT-SOURCED contribution on this record: Restricted (task 037 ·
    /// FR-21) or an inactive record (task 137 · C5). Both keep a non-contact-sourced survivor — the systemuser
    /// plane's ADR-034 membership term (<c>AccessibleRecordSetService.ApplyVetoPipeline</c>).
    /// </summary>
    public bool RemovesContactSourcedAccess => IsRestricted || IsInactive;

    /// <summary>
    /// The ONE pre-max suppression predicate (ADR-003 item 8 as amended by task 138 · FR-22): on a
    /// direct-only record a contact's access comes ONLY from its own named grant rows. Organization-inherited
    /// grants, standing-grant membership and organization expansion contribute nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>Secure OR Limited.</b> The owner's model (round 2, 2026-09-30): Limited means named, direct
    /// grants only, which is exactly what FR-22 Secure suppression already does — and a Secure record is
    /// Limited for contacts. So this is the same predicate widened, not a second one. Every term that used to
    /// ask "is it Secure?" asks this instead.</para>
    /// <para><b>Restricted is not part of it.</b> Restricted is a POST-max veto that removes every
    /// contact-sourced contribution, direct grants included (<c>AccessibleRecordSetService.ApplyVetoPipeline</c>).
    /// Where Restricted and direct-only are both present, Restricted wins because it runs last.</para>
    /// </remarks>
    public bool IsDirectOnly => IsSecure || IsLimited;

    /// <summary>
    /// What an unreadable or unreturned record resolves to: <b>every restriction active</b> (spec NFR-01),
    /// plus the <see cref="IsUnreadable"/> marker.
    /// <para>
    /// This is the fail-closed direction, and it is deliberately the MOST restrictive combination — not
    /// merely "restricted". Treating an unknown record as non-secure would let a derived-member or
    /// org-expansion term contribute access to a record nobody could confirm is safe to share.
    /// </para>
    /// </summary>
    public static RootRecordFlags Unreadable =>
        new(IsSecure: true, IsRestricted: true, IsLimited: true, IsUnreadable: true, IsInactive: true);

    /// <summary>No restriction applies. Only ever produced by a SUCCESSFUL read of a Standard, non-secure row.</summary>
    public static RootRecordFlags None => new(IsSecure: false, IsRestricted: false);
}

/// <summary>
/// The organizations ONE record references, via any org-typed lookup (task 039 · FR-23) — the record
/// side of the deny-list's ethical-wall match.
/// </summary>
/// <param name="OrganizationIds">
/// Every organization the record references, from EVERY org-typed lookup — deliberately not narrowed
/// to task 041's access-conferring registry (denial over-matches on purpose; register B-10). Empty when
/// a SUCCESSFUL read found no populated org lookup on the row.
/// </param>
/// <param name="Unreadable">
/// <c>true</c> when this record's own org-reference read could not be completed (fault, non-success
/// status, or an id the query did not return at all). The fail-closed direction for THIS read is toward
/// denial, not toward "references nothing" — see <see cref="Unresolved"/>.
/// </param>
/// <remarks>
/// Unlike <see cref="RootRecordFlags"/>, there is no single "worst case" combination of the two vetoes
/// to fold an unreadable row into — an org id set has no analogous most-restrictive value. So this type
/// carries the unreadable signal as its OWN field rather than encoding it into
/// <see cref="OrganizationIds"/> (e.g. via a sentinel guid), which would be silently defeated by any
/// downstream code that copies or re-wraps the collection.
/// </remarks>
public readonly record struct ReferencedOrganizations(IReadOnlyCollection<Guid> OrganizationIds, bool Unreadable)
{
    /// <summary>
    /// What an unreadable or unreturned record resolves to (spec NFR-01 applied to this read). The
    /// caller (task 039's deny-veto wiring) treats this as a forced deny of the record, independent of
    /// what the deny-list reader itself would say — see
    /// <c>AccessibleRecordSetService.ResolveDenyVetoAsync</c>.
    /// </summary>
    public static ReferencedOrganizations Unresolved => new(Array.Empty<Guid>(), Unreadable: true);

    /// <summary>No organization reference. Only ever produced by a SUCCESSFUL read of a row with no populated org lookup.</summary>
    public static ReferencedOrganizations None => new(Array.Empty<Guid>(), Unreadable: false);
}

/// <summary>
/// The ONE <see cref="ExternalAccessLevel"/> → <see cref="AccessRights"/> mapping (task 032; root
/// CLAUDE.md §11 — reuse, do not fork).
/// </summary>
/// <remarks>
/// Extracted from <c>ExternalCallerContext.GetEffectiveRights</c>, which is now a caller. It was the
/// only implementation of this table, but it was an INSTANCE method that resolved a level from
/// project-only <c>Participations</c> before mapping it, so no other root type could reach it. Task
/// 032's step list says "add the mapping"; adding a second copy would have put a divergence in the one
/// function where drift silently changes rights.
/// </remarks>
public static class ExternalAccessLevels
{
    /// <summary>
    /// Maps a grant level to rights. Fails CLOSED: <c>null</c> and any value outside the enum yield
    /// <see cref="AccessRights.None"/> (spec NFR-01) — an unrecognised level must never widen access.
    /// </summary>
    public static AccessRights ToAccessRights(ExternalAccessLevel? level) => level switch
    {
        ExternalAccessLevel.ViewOnly => AccessRights.Read,
        ExternalAccessLevel.Collaborate => AccessRights.Read | AccessRights.Create | AccessRights.Write,
        ExternalAccessLevel.FullAccess => AccessRights.Read | AccessRights.Create | AccessRights.Write | AccessRights.Delete,
        _ => AccessRights.None
    };

    /// <summary>
    /// The reverse projection: the coarse level to SHOW for a set of rights (task 033 / FR-19).
    /// Returns the highest level whose rights are FULLY CONTAINED in <paramref name="rights"/>, or
    /// <c>null</c> when even Read is absent.
    /// </summary>
    /// <remarks>
    /// ⚠️ <b>Display only. Never authorize on this.</b> The projection is deliberately LOSSY, because
    /// rights are a flags set and levels are three fixed points: <c>Read|Write</c> (no Create) reports as
    /// <c>ViewOnly</c>, under-stating a real Write. Authorization must therefore read
    /// <c>AccessRights</c> — which is why <c>CallerProjectAccess</c> stores rights and derives the level,
    /// not the reverse.
    /// <para>
    /// It is not currently possible to hit the lossy case: every term contributes either
    /// <c>ToAccessRights(level)</c> or <c>MembershipTermRights</c> (== Collaborate), and a union of
    /// those is always exactly one of the three points. The containment test is written for the general
    /// case anyway, so that a future term contributing an off-grid combination degrades to an
    /// UNDER-statement rather than silently reporting a level the caller does not hold.
    /// </para>
    /// </remarks>
    public static ExternalAccessLevel? ToDisplayLevel(AccessRights rights)
    {
        if (rights.HasFlag(ToAccessRights(ExternalAccessLevel.FullAccess)))
            return ExternalAccessLevel.FullAccess;
        if (rights.HasFlag(ToAccessRights(ExternalAccessLevel.Collaborate)))
            return ExternalAccessLevel.Collaborate;
        if (rights.HasFlag(ToAccessRights(ExternalAccessLevel.ViewOnly)))
            return ExternalAccessLevel.ViewOnly;
        return null;
    }

    /// <summary>
    /// The highest level a grantor holding <paramref name="grantorRights"/> on a record may GRANT on it — the ONE
    /// grantor ceiling (unified-access-control-r2 task 139; owner Q1, round 2, confirmed round 3b: "cap every grant
    /// at the grantor's own level"). <c>null</c> when the grantor may grant nothing.
    /// </summary>
    /// <remarks>
    /// <para><b>The table.</b> Full Access iff Read + Write + Delete; Collaborate iff Read + Write; View Only iff Read;
    /// otherwise none. Create, Append, AppendTo and Share are deliberately NOT consulted: the input comes from
    /// <c>RetrievePrincipalAccess</c> on an EXISTING record (<c>CallerRecordAccessProbe</c>), which is not guaranteed
    /// to report <c>CreateAccess</c>, so a test including Create would under-state a Deep-role user as View Only —
    /// and Append/AppendTo/Share do not change what a grant level confers on a contact.</para>
    /// <para><b>Not <see cref="ToDisplayLevel"/></b>, whose containment test includes Create and whose remark forbids
    /// authorizing on it. This method IS an authorization input: it bounds what a grant route writes.</para>
    /// <para>Shared by <c>/grant</c> and <c>/invite-and-grant</c> (task 139) and task 140's contact-side route, through
    /// <c>GrantCeiling.FromGrantorRights</c>; never computed anywhere else.</para>
    /// </remarks>
    public static ExternalAccessLevel? GrantCeilingFor(AccessRights grantorRights)
    {
        const AccessRights read = AccessRights.Read;
        const AccessRights readWrite = AccessRights.Read | AccessRights.Write;
        const AccessRights readWriteDelete = AccessRights.Read | AccessRights.Write | AccessRights.Delete;

        if ((grantorRights & readWriteDelete) == readWriteDelete)
            return ExternalAccessLevel.FullAccess;
        if ((grantorRights & readWrite) == readWrite)
            return ExternalAccessLevel.Collaborate;
        if ((grantorRights & read) == read)
            return ExternalAccessLevel.ViewOnly;
        return null;
    }
}

/// <summary>
/// The FULL set of a Contact's active <c>sprk_externalrecordaccess</c> grants, partitioned by the
/// grant's typed root lookup (spaarke-SPA-external-access-platform-r2 task 028 — polymorphic Tier-2
/// scoping). A grant row targets exactly ONE root via its typed lookup (<c>sprk_project</c> /
/// <c>sprk_matter</c> / <c>sprk_workassignment</c> — verified live), so the row falls into exactly one
/// bucket here.
/// <para>
/// <b>All three root types carry their access level</b> as of unified-access-control-r2 task 032
/// (FR-19). This paragraph previously read "matters and work assignments are id sets … within-root
/// rights are not level-differentiated for those types yet" — that is no longer true, and leaving it
/// would have been exactly the failure mode where a stale comment becomes the constraint the next
/// reader honours (FAILURE-MODES AP-12). <see cref="Matters"/> / <see cref="WorkAssignments"/> remain
/// available as DERIVED id views for the read-scoping callers that only need ids.
/// </para>
/// </summary>
/// <remarks>
/// Direct document/invoice-level grants are intentionally OUT OF SCOPE (design §6 — access to a child
/// derives from an accessible ROOT), so the grant table's <c>sprk_invoice</c> lookup is not read here.
/// </remarks>
public sealed class ExternalGrantSet
{
    /// <summary>Project grants (id + level) — level preserved for the CIAM <c>/me</c> mapping.</summary>
    public required IReadOnlyList<ExternalParticipation> Projects { get; init; }

    /// <summary>
    /// Matter grants (id + level), task 032. The SOURCE OF TRUTH for matter access;
    /// <see cref="Matters"/> is a derived view over it.
    /// </summary>
    public required IReadOnlyList<ExternalRootGrant> MatterGrants { get; init; }

    /// <summary>
    /// Work-assignment grants (id + level), task 032. The SOURCE OF TRUTH;
    /// <see cref="WorkAssignments"/> is a derived view over it.
    /// </summary>
    public required IReadOnlyList<ExternalRootGrant> WorkAssignmentGrants { get; init; }

    private IReadOnlySet<Guid>? _matterIds;
    private IReadOnlySet<Guid>? _workAssignmentIds;

    /// <summary>
    /// Matter grant ids (<c>sprk_matter</c>). A DERIVED VIEW over <see cref="MatterGrants"/> as of task
    /// 032 — deliberately not a second stored collection, so ids and levels cannot disagree.
    /// <para>
    /// Kept at this exact shape (<c>IReadOnlySet&lt;Guid&gt;</c>) because
    /// <c>CallerPrincipalResolver</c> assigns it straight into <c>AccessibleMatterIds</c>, and that file
    /// belongs to task 033 — widening the property type here would have forced an edit outside this
    /// task's envelope.
    /// </para>
    /// </summary>
    public IReadOnlySet<Guid> Matters =>
        _matterIds ??= MatterGrants.Select(g => g.RecordId).ToHashSet();

    /// <summary>Work-assignment grant ids. A derived view over <see cref="WorkAssignmentGrants"/> — see <see cref="Matters"/>.</summary>
    public IReadOnlySet<Guid> WorkAssignments =>
        _workAssignmentIds ??= WorkAssignmentGrants.Select(g => g.RecordId).ToHashSet();

    /// <summary>
    /// <c>true</c> when this set was built over a read that could not be completed — the grant query itself (a
    /// non-2xx, a 429, a timeout, a parse failure, any exception), the organization-grant read, or the membership
    /// junction read (task 109's <see cref="ActiveOrgMemberships.Unreadable"/>). Unified-access-control-r2 task 132
    /// (defect C12).
    /// </summary>
    /// <remarks>
    /// <para><b>The set itself is still the fail-closed answer for THIS request</b>: whatever was read successfully
    /// (e.g. the direct grants when only the organization term faulted), and nothing a failed read would have added.
    /// What the flag changes is only whether the set may be STORED — <c>GetGrantSetAsync</c> never caches a faulted
    /// set, so a 429 costs one request its grants instead of 60 seconds of requests.</para>
    /// <para><b>Never cached, so always false on a cache hit.</b> It is deliberately absent from the cached shape
    /// (<c>CachedGrantSet</c>) and from <c>GrantCacheRoundTripSeamTests</c>' carried set (named in its
    /// <c>NotCarriedByDesign</c>). Internal init: only the grant read sets it. Not serialized.</para>
    /// </remarks>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Faulted { get; internal init; }

    /// <summary>The empty grant set (no grants of any root type).</summary>
    public static ExternalGrantSet Empty { get; } = new()
    {
        Projects = Array.Empty<ExternalParticipation>(),
        MatterGrants = Array.Empty<ExternalRootGrant>(),
        WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
    };

    /// <summary>
    /// The empty, FAULTED grant set: the read failed, so nothing is granted for this request and nothing is cached
    /// (task 132). Never use <see cref="Empty"/> for a failed read — that is an answer ("no grants") and IS cached.
    /// </summary>
    internal static ExternalGrantSet Unreadable { get; } = new()
    {
        Projects = Array.Empty<ExternalParticipation>(),
        MatterGrants = Array.Empty<ExternalRootGrant>(),
        WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
        Faulted = true,
    };
}

/// <summary>
/// Access level values for external participation (matches sprk_accesslevel choice field).
/// </summary>
public enum ExternalAccessLevel
{
    ViewOnly = 100000000,
    Collaborate = 100000001,
    FullAccess = 100000002
}

// =============================================================================
// Workforce collaboration principal (ADR-028 Amendment A2 · teams-app-r1 FR-04)
// =============================================================================
// The output shape of the workforce-token→principal resolver. Distinct from
// ExternalCallerContext (which models a CIAM contact + its sprk_externalrecordaccess
// participations): a workforce principal is resolved from a workforce Entra token to
// EITHER a Dataverse systemuser (→ ADR-034 membership, task 021/022) OR a contact-only
// principal (→ contact-anchored membership, task 021). Set on HttpContext.Items by
// WorkforceCallerAuthorizationFilter and consumed by downstream collaboration handlers +
// the accessible-record-set enforcement (task 022). Tasks 021/022 compose on this shape —
// do NOT change it without re-opening both.

/// <summary>
/// Which identity plane a workforce-authenticated caller resolved to.
/// </summary>
public enum WorkforcePrincipalKind
{
    /// <summary>Caller has a Dataverse <c>systemuser</c> row (AAD oid → systemuser).
    /// Accessible set = ADR-034 membership (automatic).</summary>
    SystemUser,

    /// <summary>Caller has no systemuser but resolves to a <c>contact</c> BOUND to their Entra oid
    /// (<c>sprk_externalobjectid</c>, task 141). Accessible set = contact-anchored membership / grants (task 021).</summary>
    ContactOnly
}

/// <summary>
/// A resolved workforce collaboration principal (exactly one of systemuser / contact-only).
/// Produced by <c>IWorkforcePrincipalResolver</c>; there is no "unscoped" or "anonymous"
/// principal — an unresolvable caller is denied, never represented here.
/// </summary>
public sealed class WorkforcePrincipal
{
    /// <summary>HttpContext.Items key under which the resolved principal is stored.</summary>
    public static readonly object HttpContextItemsKey = new();

    /// <summary>The identity plane this caller resolved to.</summary>
    public required WorkforcePrincipalKind Kind { get; init; }

    /// <summary>The Dataverse <c>systemuserid</c>. Non-null iff <see cref="Kind"/> is
    /// <see cref="WorkforcePrincipalKind.SystemUser"/>.</summary>
    public Guid? SystemUserId { get; init; }

    /// <summary>The Dataverse <c>contactid</c>. For a <see cref="WorkforcePrincipalKind.ContactOnly"/>
    /// principal this is the required anchor (always non-null). For a
    /// <see cref="WorkforcePrincipalKind.SystemUser"/> principal this is the <b>derived</b> contact
    /// (its <c>sprk_primarycontact</c> link, else the contact bound to its oid — task 141) and MAY be null
    /// when the systemuser has neither.</summary>
    public Guid? ContactId { get; init; }

    /// <summary>
    /// <c>true</c> when, on a <see cref="WorkforcePrincipalKind.SystemUser"/> principal, <see cref="ContactId"/> is
    /// <c>null</c> because the reads that decide it FAILED (the systemuser row or the oid-binding lookup) — not because
    /// the user genuinely has no linked contact. Unified-access-control-r2 task 132 (defect C12).
    /// </summary>
    /// <remarks>
    /// The deny veto (FR-23) uses the linked contact as its subject on the systemuser plane. With the contact
    /// UNKNOWN the veto has no subject and checks nothing, so a No Access entry naming that person stops applying to
    /// their membership-term access — the same "unreadable looks like absent" shape as ISS-019. The evaluator
    /// therefore denies every candidate when this is set, mirroring <see cref="ActiveOrgMemberships.Failed"/> — on the
    /// grant-supported root types, the only ones where the contact is the veto subject (elsewhere a known contact is
    /// not checked either, so an unknown one changes nothing). A successfully read user with no linked contact leaves
    /// this false and composes exactly as before.
    /// </remarks>
    public bool ContactUnreadable { get; init; }

    /// <summary>The workforce AAD object id (<c>oid</c> claim) the caller was resolved by.</summary>
    public required string Oid { get; init; }

    /// <summary>The workforce tenant id (<c>tid</c> claim).</summary>
    public required string TenantId { get; init; }

    /// <summary>The caller's email/UPN from token claims — UNVERIFIED (Microsoft documents these claims as
    /// mutable). Display and audit only: since task 141 it is NEVER used to resolve a contact or its grants
    /// (the email fallback in <c>AccessibleRecordSetService</c> was removed — a licensed user's contact comes
    /// only from the systemuser↔contact link). May be empty.</summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>True when this is a systemuser principal (ADR-034 membership plane).</summary>
    public bool IsSystemUser => Kind == WorkforcePrincipalKind.SystemUser;

    /// <summary>True when this is a contact-only principal (contact-anchored plane).</summary>
    public bool IsContactOnly => Kind == WorkforcePrincipalKind.ContactOnly;
}

/// <summary>
/// Why a workforce caller was denied by the resolver. Drives the HTTP status the endpoint
/// filter returns: <see cref="MissingIdentityClaims"/> → 401 (we cannot identify the caller);
/// <see cref="PrincipalNotResolved"/> → 403 (we identified the caller but they map to neither a
/// systemuser nor a contact — a known identity that is not authorized/provisioned here).
/// </summary>
public enum WorkforceDenyReason
{
    /// <summary>The token carried no usable AAD object id (<c>oid</c>) claim → 401.</summary>
    MissingIdentityClaims,

    /// <summary>The caller matched neither a systemuser nor a contact → 403.</summary>
    PrincipalNotResolved
}

/// <summary>
/// The result of resolving a workforce token to a principal: exactly one of a resolved
/// <see cref="WorkforcePrincipal"/> (systemuser or contact-only) or an explicit deny. There is
/// no silent fallback to an unscoped principal.
/// </summary>
public sealed class WorkforcePrincipalResolution
{
    /// <summary>The resolved principal on success; <c>null</c> on deny.</summary>
    public WorkforcePrincipal? Principal { get; private init; }

    /// <summary>The deny reason on failure; <c>null</c> on success.</summary>
    public WorkforceDenyReason? DenyReason { get; private init; }

    /// <summary>Machine-readable deny code (per auth.md <c>{domain}.{area}.{action}.{reason}</c>)
    /// on failure; <c>null</c> on success.</summary>
    public string? DenyCode { get; private init; }

    /// <summary>True when a principal was resolved.</summary>
    public bool IsResolved => Principal is not null;

    /// <summary>Constructs a resolved outcome from an already-built principal.</summary>
    public static WorkforcePrincipalResolution Resolved(WorkforcePrincipal principal)
        => new() { Principal = principal ?? throw new ArgumentNullException(nameof(principal)) };

    /// <summary>Constructs a systemuser outcome (systemuserId + derived contactId + token email).
    /// <paramref name="email"/> is display/audit only (see <see cref="WorkforcePrincipal.Email"/>).
    /// <paramref name="contactUnreadable"/>: the derived contact could not be read (task 132 — see
    /// <see cref="WorkforcePrincipal.ContactUnreadable"/>); ignored when a contact was derived.</summary>
    public static WorkforcePrincipalResolution ForSystemUser(
        Guid systemUserId, Guid? derivedContactId, string oid, string tenantId, string? email = null,
        bool contactUnreadable = false)
        => Resolved(new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = systemUserId,
            ContactId = derivedContactId,
            ContactUnreadable = derivedContactId is null && contactUnreadable,
            Oid = oid,
            TenantId = tenantId,
            Email = email ?? string.Empty
        });

    /// <summary>Constructs a contact-only outcome (contactId anchor).</summary>
    public static WorkforcePrincipalResolution ForContact(
        Guid contactId, string oid, string tenantId)
        => Resolved(new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.ContactOnly,
            ContactId = contactId,
            Oid = oid,
            TenantId = tenantId
        });

    /// <summary>Constructs an explicit deny outcome with a reason + machine-readable code.</summary>
    public static WorkforcePrincipalResolution Denied(WorkforceDenyReason reason, string denyCode)
        => new() { DenyReason = reason, DenyCode = denyCode };
}
