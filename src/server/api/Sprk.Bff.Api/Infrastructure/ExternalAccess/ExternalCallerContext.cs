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
    /// Checks if the Contact has access to the specified project.
    /// </summary>
    public bool HasProjectAccess(Guid projectId) =>
        Participations.Any(p => p.ProjectId == projectId);

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
    /// Gets all project IDs the Contact can access (for AI search filter construction).
    /// </summary>
    public IEnumerable<Guid> GetAccessibleProjectIds() =>
        Participations.Select(p => p.ProjectId);
}

/// <summary>
/// A single external access grant for a Contact → Project relationship.
/// </summary>
public sealed class ExternalParticipation
{
    public required Guid ProjectId { get; init; }
    public required ExternalAccessLevel AccessLevel { get; init; }
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
/// ⚠️ <b>Nullable by design.</b> The PROJECT partition drops rows whose level is null
/// (<c>&amp;&amp; r.sprk_accesslevel.HasValue</c>); this shape deliberately does NOT, because applying
/// that filter to matters/WAs would turn a level-less row from "grants access" into "grants nothing" —
/// a silent REVOCATION on the security boundary. A null level keeps its id (set membership unchanged)
/// and contributes <see cref="AccessRights.None"/>, which the highest-wins max cannot widen. Verified
/// 2026-09-04: every active grant row in dev carries a level on all three root types, so this is a
/// safety property for other tenants rather than a live case.
/// </para>
/// </summary>
public sealed class ExternalRootGrant
{
    public required Guid RecordId { get; init; }
    public required ExternalAccessLevel? AccessLevel { get; init; }
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

    /// <summary>The empty grant set (no grants of any root type).</summary>
    public static ExternalGrantSet Empty { get; } = new()
    {
        Projects = Array.Empty<ExternalParticipation>(),
        MatterGrants = Array.Empty<ExternalRootGrant>(),
        WorkAssignmentGrants = Array.Empty<ExternalRootGrant>(),
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

    /// <summary>Caller has no systemuser but resolves to a <c>contact</c> (by AAD oid or
    /// verified email). Accessible set = contact-anchored membership / grants (task 021).</summary>
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
    /// (via <c>sprk_primarycontact</c> / AAD cross-ref) and MAY be null when the systemuser has no
    /// linked contact.</summary>
    public Guid? ContactId { get; init; }

    /// <summary>The workforce AAD object id (<c>oid</c> claim) the caller was resolved by.</summary>
    public required string Oid { get; init; }

    /// <summary>The workforce tenant id (<c>tid</c> claim).</summary>
    public required string TenantId { get; init; }

    /// <summary>The caller's tenant-verified email/UPN (from token claims). Used as the fallback key to
    /// find the caller's contact-grants when a <see cref="WorkforcePrincipalKind.SystemUser"/> has no
    /// derived <see cref="ContactId"/> (no <c>sprk_primarycontact</c> link). May be empty.</summary>
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

    /// <summary>Constructs a systemuser outcome (systemuserId + derived contactId + verified email).
    /// <paramref name="email"/> is the fallback key for the caller's contact-grants when the systemuser
    /// has no derived contact (see <see cref="WorkforcePrincipal.Email"/>).</summary>
    public static WorkforcePrincipalResolution ForSystemUser(
        Guid systemUserId, Guid? derivedContactId, string oid, string tenantId, string? email = null)
        => Resolved(new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = systemUserId,
            ContactId = derivedContactId,
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
