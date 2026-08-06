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
    public AccessRights GetEffectiveRights(Guid projectId)
    {
        var level = GetAccessLevel(projectId);
        return level switch
        {
            ExternalAccessLevel.ViewOnly => AccessRights.Read,
            ExternalAccessLevel.Collaborate => AccessRights.Read | AccessRights.Create | AccessRights.Write,
            ExternalAccessLevel.FullAccess => AccessRights.Read | AccessRights.Create | AccessRights.Write | AccessRights.Delete,
            _ => AccessRights.None
        };
    }

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

    /// <summary>Constructs a systemuser outcome (systemuserId + derived contactId).</summary>
    public static WorkforcePrincipalResolution ForSystemUser(
        Guid systemUserId, Guid? derivedContactId, string oid, string tenantId)
        => Resolved(new WorkforcePrincipal
        {
            Kind = WorkforcePrincipalKind.SystemUser,
            SystemUserId = systemUserId,
            ContactId = derivedContactId,
            Oid = oid,
            TenantId = tenantId
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
