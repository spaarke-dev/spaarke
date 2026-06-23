// R3 Part 1 — User-Record Membership Resolution (org-membership contract)
// Task 032 (2026-06-21): Defines the contract for resolving a Dataverse
// systemuser → set of sprk_organization GUIDs the user belongs to. Used by:
//   - IdentityNormalizationService (task 031): populates PersonIdentity.
//     OrganizationIds so MembershipResolverService can match
//     `Lookup → sprk_organization` columns (per design.md Part 1 row 6 of the
//     identity-normalization contract; spec.md FR-1A.6; Q4 owner clarification).
//   - MembershipResolverService (task 033): supplies the GUID set used in the
//     IN-list for FetchXML predicates against `Lookup → sprk_organization`
//     fields such as `sprk_matter.sprk_assignedlawfirm1/2`.
//
// Mechanism (decided in task 032 — see notes/sprk-organization-mapping-decision.md):
//   Option (b) — config-driven lookup field on sprk_organization. Operators
//   point a Lookup field on sprk_organization at a systemuser (typical
//   examples: sprk_owneruser, sprk_relationshipowner) and configure that
//   field's logical name via MembershipOptions.OrganizationLookup.
//
// If no mapping is configured (or no organizations link to the user), the
// implementation returns an empty list — fail-soft so downstream services
// degrade gracefully (a user simply matches 0 `Lookup → sprk_organization`
// records, which is the correct outcome).
//
// Operator alternatives (deferred — not implemented in BFF code):
//   (a) Dataverse N:N between systemuser and sprk_organization. Future
//       implementation swaps this resolver for a metadata-driven N:N
//       traversal; the interface signature is unchanged.
//   (c) Team-per-organization. Future implementation derives membership from
//       team memberships; the interface signature is unchanged.
//
// Reference: projects/spaarke-platform-foundations-r3/design.md Part 1 §
// Identity normalization contract (row 6 `Lookup → sprk_organization`);
// spec.md FR-1A.6 + Q4 Owner Clarification; ADR-034 (forthcoming, task 037).

using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Resolves the set of <c>sprk_organization</c> GUIDs a Dataverse
/// <c>systemuser</c> belongs to, per the mechanism configured in
/// <see cref="MembershipOptions.OrganizationLookup"/>.
/// </summary>
/// <remarks>
/// <para>
/// Implementations MUST fail-soft (return empty list, log info) when no
/// mapping is configured, when the lookup field does not exist, or when no
/// organizations link to the user. Throwing for these conditions would
/// cascade-fail the entire membership-resolution pipeline for users that
/// simply have no organizational affiliations (the common case).
/// </para>
/// <para>
/// Consumed by <c>IdentityNormalizationService</c> (task 031) when populating
/// <see cref="PersonIdentity"/>. Registered as a singleton concrete per ADR-010
/// in <c>MembershipModule</c>.
/// </para>
/// </remarks>
public interface IOrganizationMembershipResolver
{
    /// <summary>
    /// Returns the set of <c>sprk_organization</c> GUIDs the specified
    /// systemuser is mapped to. Returns an empty list (not <c>null</c>) when
    /// no mapping exists or the configuration is incomplete.
    /// </summary>
    /// <param name="systemUserId">The Dataverse <c>systemuserid</c> GUID.</param>
    /// <param name="identityContext">
    /// Optional already-resolved identity context (passed by
    /// <c>IdentityNormalizationService</c>) so the resolver can avoid
    /// duplicating cross-reference lookups. Implementations MUST tolerate
    /// <c>null</c>: the resolver is also called outside the normalization
    /// pipeline (e.g. directly by playbook nodes in future tasks).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<Guid>> GetOrganizationIdsAsync(
        Guid systemUserId,
        PersonIdentity? identityContext,
        CancellationToken ct);
}
