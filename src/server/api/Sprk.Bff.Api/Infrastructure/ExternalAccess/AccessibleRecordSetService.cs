// teams-app-r1 Task 022 (2026-08-04) — Accessible-record-set composition (the core authz gate).
//
// design.md §5 / spec FR-06 — authorization is uniform ("is this record in the principal's
// accessible-record set?"), but the SET is composed per principal plane:
//
//     accessible(principal) =
//         systemuser  → ADR-034 membership (auto — trusted internal staff, Dataverse-governed)
//       ∪ contact     → sprk_externalrecordaccess grants (per-record, materialized)
//       ∪ contact     → standing-grant runtime membership (IFF contact.sprk_standinggrant is set)
//
// EXACT composition rules honored here (design §5 per-principal table):
//   • systemuser principal  = ADR-034 membership ONLY (automatic). No grants/standing term.
//   • contact-only principal = sprk_externalrecordaccess grants  ∪  (standing-grant membership
//                              IFF the contact holds a standing grant). NEVER automatic membership
//                              without an explicit grant OR an explicit standing-grant policy flag.
//
// This GENERALIZES the CIAM-only ExternalCallerContext.HasProjectAccess record∈set check (which
// modeled the contact-grant plane only) to compose all three sources for EVERY principal type task
// 020 resolves. It is the single place the authorization boundary is composed + audited, and is the
// authz-before-stream gate task 030 (broker document access) depends on.
//
// Broker-only (ADR-028 A2 NFR-02): reads membership/grant/flag data APP-ONLY against the already-
// resolved principal (task 020). No caller-token exchange (no OBO), no Graph SDK types, no
// AI-internal types.

using Sprk.Bff.Api.Services.Ai.Membership;

namespace Sprk.Bff.Api.Infrastructure.ExternalAccess;

/// <summary>
/// Composes and evaluates the accessible-record set for a resolved <see cref="WorkforcePrincipal"/>
/// per design.md §5 / spec FR-06. The single enforcement primitive: given a principal + entity type
/// + record id, decide membership in the composed set (deny anything outside it).
/// </summary>
/// <remarks>
/// ADR-010 testing seam: the interface lets the endpoint filter (and task 030) be exercised against
/// a substitute composer, and lets the composition itself be unit-tested per principal plane.
/// </remarks>
public interface IAccessibleRecordSetService
{
    /// <summary>
    /// Composes the full accessible-record set of the given <paramref name="entityType"/> for the
    /// principal, unioning the design §5 sources that apply to the principal's plane.
    /// </summary>
    Task<AccessibleRecordSet> ComposeAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct);

    /// <summary>
    /// The enforcement decision: is <paramref name="recordId"/> in the principal's composed
    /// accessible set for <paramref name="entityType"/>? A <c>false</c> result MUST be enforced as a
    /// DENY (not merely an omission) by the caller.
    /// </summary>
    Task<bool> IsRecordAccessibleAsync(
        WorkforcePrincipal principal, string entityType, Guid recordId, CancellationToken ct);
}

/// <summary>
/// The composed accessible-record set for one principal + entity type, with source provenance for
/// auditability. <see cref="Contains"/> is the enforcement check.
/// </summary>
public sealed class AccessibleRecordSet
{
    public required WorkforcePrincipalKind PrincipalKind { get; init; }
    public required string EntityType { get; init; }

    /// <summary>The de-duplicated record ids the principal may access for this entity type.</summary>
    public required IReadOnlySet<Guid> RecordIds { get; init; }

    /// <summary>Which design §5 union terms contributed to this set (audit + test introspection).</summary>
    public required AccessibleRecordSetSources Sources { get; init; }

    public int Count => RecordIds.Count;

    /// <summary>The enforcement check: <c>true</c> iff the record is in the composed set.</summary>
    public bool Contains(Guid recordId) => RecordIds.Contains(recordId);
}

/// <summary>
/// Flags for which design §5 union terms contributed to an <see cref="AccessibleRecordSet"/>.
/// </summary>
/// <param name="SystemUserMembership">systemuser → ADR-034 membership (automatic) term applied.</param>
/// <param name="ContactGrants">contact → sprk_externalrecordaccess grants term applied.</param>
/// <param name="StandingGrantMembership">contact → standing-grant runtime membership term applied
/// (only when the contact held a standing grant).</param>
public readonly record struct AccessibleRecordSetSources(
    bool SystemUserMembership,
    bool ContactGrants,
    bool StandingGrantMembership);

/// <inheritdoc />
public sealed class AccessibleRecordSetService : IAccessibleRecordSetService
{
    /// <summary>
    /// The root entity types <c>sprk_externalrecordaccess</c> grants can target (task 028 — closes the
    /// R1 design §5 known-gap #2: grants are no longer project-only). Each grant row carries exactly one
    /// typed root FK (project / matter / work assignment — verified live). Membership (ADR-034) and
    /// standing-grant membership span all entities; grants now span these three root types.
    /// </summary>
    internal const string ProjectEntity = "sprk_project";
    internal const string MatterEntity = "sprk_matter";
    internal const string WorkAssignmentEntity = "sprk_workassignment";

    private static readonly HashSet<string> GrantSupportedRootEntities =
        new(StringComparer.OrdinalIgnoreCase) { ProjectEntity, MatterEntity, WorkAssignmentEntity };

    /// <summary>Whether <c>sprk_externalrecordaccess</c> grants apply to the given entity type.</summary>
    private static bool IsGrantSupported(string entityType) =>
        GrantSupportedRootEntities.Contains(entityType);

    /// <summary>The granted record ids of <paramref name="entityType"/> within a grant set.</summary>
    private static IEnumerable<Guid> GrantedIdsFor(ExternalGrantSet grants, string entityType)
    {
        if (string.Equals(entityType, ProjectEntity, StringComparison.OrdinalIgnoreCase))
            return grants.Projects.Select(p => p.ProjectId);
        if (string.Equals(entityType, MatterEntity, StringComparison.OrdinalIgnoreCase))
            return grants.Matters;
        if (string.Equals(entityType, WorkAssignmentEntity, StringComparison.OrdinalIgnoreCase))
            return grants.WorkAssignments;
        return Enumerable.Empty<Guid>();
    }

    private readonly IMembershipResolverService _membership;
    private readonly ExternalParticipationService _participations;
    private readonly IContactStandingGrantReader _standingGrant;
    private readonly ILogger<AccessibleRecordSetService> _logger;

    public AccessibleRecordSetService(
        IMembershipResolverService membership,
        ExternalParticipationService participations,
        IContactStandingGrantReader standingGrant,
        ILogger<AccessibleRecordSetService> logger)
    {
        ArgumentNullException.ThrowIfNull(membership);
        ArgumentNullException.ThrowIfNull(participations);
        ArgumentNullException.ThrowIfNull(standingGrant);
        ArgumentNullException.ThrowIfNull(logger);
        _membership = membership;
        _participations = participations;
        _standingGrant = standingGrant;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<AccessibleRecordSet> ComposeAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(principal);
        if (string.IsNullOrWhiteSpace(entityType))
        {
            throw new ArgumentException("entityType must not be null/empty/whitespace.", nameof(entityType));
        }

        return principal.Kind switch
        {
            WorkforcePrincipalKind.SystemUser => await ComposeForSystemUserAsync(principal, entityType, ct)
                .ConfigureAwait(false),
            WorkforcePrincipalKind.ContactOnly => await ComposeForContactAsync(principal, entityType, ct)
                .ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(
                nameof(principal), principal.Kind, "Unknown workforce principal kind."),
        };
    }

    /// <inheritdoc />
    public async Task<bool> IsRecordAccessibleAsync(
        WorkforcePrincipal principal, string entityType, Guid recordId, CancellationToken ct)
    {
        if (recordId == Guid.Empty)
        {
            // No record to evaluate — cannot prove access; deny (fail-closed).
            return false;
        }

        var set = await ComposeAsync(principal, entityType, ct).ConfigureAwait(false);
        return set.Contains(recordId);
    }

    // ── systemuser plane: ADR-034 membership ∪ the caller's own contact grants ───────────────────
    // (§6.5 Path-B amendment of design §5, spaarke-SPA-external-access-platform-r2 UAT 2026-08-07,
    //  owner directive — "parallel workforce/contact access"): an internal system-user who is ALSO a
    //  granted contact sees BOTH their ADR-034 membership AND their contact's project grants, so
    //  internal staff can sign in to the external SPA to "see what's there" and shepherd external
    //  users. Still strictly the person's OWN access on both planes — never "all projects" (NFR-08).
    private async Task<AccessibleRecordSet> ComposeForSystemUserAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct)
    {
        // A systemuser principal always carries a systemuserid (task 020 invariant).
        var systemUserId = principal.SystemUserId
            ?? throw new InvalidOperationException(
                "A SystemUser principal must carry a SystemUserId (task 020 invariant).");

        var membership = await _membership
            .ResolveAsync(systemUserId, entityType, options: null, ct)
            .ConfigureAwait(false);

        var ids = new HashSet<Guid>(membership.Ids);

        // Contact-grants union — grants now span project / matter / work-assignment root types
        // (task 028, closing R1 gap #2), so this term applies for any grant-supported entity. Prefer the
        // derived contact (sprk_primarycontact); fall back to a verified-email match when the systemuser
        // has no linked contact.
        var contactGrantsApplied = false;
        if (IsGrantSupported(entityType))
        {
            Guid? grantContactId =
                principal.ContactId is { } cid && cid != Guid.Empty ? cid : null;

            if (grantContactId is null && !string.IsNullOrWhiteSpace(principal.Email))
            {
                grantContactId = await _participations
                    .ResolveExternalContactAsync(oid: null, email: principal.Email, ct)
                    .ConfigureAwait(false);
            }

            if (grantContactId is { } resolved && resolved != Guid.Empty)
            {
                var grants = await _participations
                    .GetGrantSetAsync(resolved, ct)
                    .ConfigureAwait(false);
                foreach (var id in GrantedIdsFor(grants, entityType))
                {
                    ids.Add(id);
                }
                contactGrantsApplied = true;
            }
        }

        _logger.LogInformation(
            "[WF-AUTHZ] Composed accessible set for systemuser {SystemUserId} on {EntityType}: " +
            "{Count} records (ADR-034 membership; contact-grants union applied: {ContactGrants}).",
            systemUserId, entityType, ids.Count, contactGrantsApplied);

        return new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.SystemUser,
            EntityType = entityType,
            RecordIds = ids,
            Sources = new AccessibleRecordSetSources(
                SystemUserMembership: true, ContactGrants: contactGrantsApplied, StandingGrantMembership: false),
        };
    }

    // ── contact plane: grants ∪ (standing-grant membership IFF flag set) ─────────────────────────
    private async Task<AccessibleRecordSet> ComposeForContactAsync(
        WorkforcePrincipal principal, string entityType, CancellationToken ct)
    {
        // A contact-only principal always carries a contactId anchor (task 020 invariant).
        var contactId = principal.ContactId
            ?? throw new InvalidOperationException(
                "A ContactOnly principal must carry a ContactId anchor (task 020 invariant).");

        var ids = new HashSet<Guid>();

        // Term 1 — explicit sprk_externalrecordaccess grants (per-record, materialized). Grants now span
        // project / matter / work-assignment root types (task 028, closing R1 gap #2): they contribute
        // for any grant-supported entity, restricted to that entity's granted-id slice.
        var grantsApplied = false;
        if (IsGrantSupported(entityType))
        {
            var grants = await _participations
                .GetGrantSetAsync(contactId, ct)
                .ConfigureAwait(false);
            foreach (var id in GrantedIdsFor(grants, entityType))
            {
                ids.Add(id);
            }
            grantsApplied = true;
        }

        // Term 2 — standing-grant runtime membership, GATED on the subject-level policy flag. The
        // negative case is load-bearing: a contact WITHOUT a standing grant gets ONLY the explicit
        // grants above — NEVER automatic membership. (task-051 seam: IContactStandingGrantReader.)
        var standingApplied = false;
        if (await _standingGrant.HasStandingGrantAsync(contactId, ct).ConfigureAwait(false))
        {
            var membership = await _membership
                .ResolveByContactAsync(contactId, entityType, options: null, ct)
                .ConfigureAwait(false);
            foreach (var id in membership.Ids)
            {
                ids.Add(id);
            }
            standingApplied = true;
        }

        _logger.LogInformation(
            "[WF-AUTHZ] Composed accessible set for contact {ContactId} on {EntityType}: {Count} records " +
            "(grants: {Grants}, standing-grant membership: {Standing}).",
            contactId, entityType, ids.Count, grantsApplied, standingApplied);

        return new AccessibleRecordSet
        {
            PrincipalKind = WorkforcePrincipalKind.ContactOnly,
            EntityType = entityType,
            RecordIds = ids,
            Sources = new AccessibleRecordSetSources(
                SystemUserMembership: false,
                ContactGrants: grantsApplied,
                StandingGrantMembership: standingApplied),
        };
    }
}
