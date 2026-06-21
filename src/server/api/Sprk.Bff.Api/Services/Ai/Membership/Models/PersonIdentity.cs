// R3 Part 1 — User-Record Membership Resolution (PersonIdentity model)
// Task 031 (2026-06-21): The full normalized identity record for a single
// systemuserid, resolved across the six identity-type paths defined by the
// design.md Part 1 § Identity normalization contract table:
//
//   Lookup → systemuser    → SystemUserId (always populated)
//   Lookup → contact       → ContactId (cross-ref via azureactivedirectoryobjectid per ADR-028)
//   Lookup → team          → TeamIds[] (expanded from teammembership)
//   Lookup → businessunit  → BusinessUnitId (from systemuser row)
//   Lookup → account       → AccountId (from primary contact's parentcustomerid)
//   Lookup → sprk_org      → OrganizationIds[] (via IIdentityOrganizationResolver
//                            implementations supplied by task 032)
//
// Each path is INDEPENDENT — a failure on one (e.g., user has no contact) does
// NOT fail the others. Cached in Redis with 10-min TTL per ADR-009.
//
// Task 032 published a placeholder (SystemUserId-only) record at this path so
// it could compile alongside IIdentityOrganizationResolver without racing
// task 031. This file (task 031) replaces that placeholder with the full
// shape; the additive-only contract is honored because the placeholder
// constructor (SystemUserId) is preserved as the first positional parameter.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.5, FR-1A.6;
//            projects/spaarke-platform-foundations-r3/design.md Part 1 §
//            Identity normalization contract; ADR-028 § Cross-reference rule;
//            ADR-034 (forthcoming) § Identity normalization.

namespace Sprk.Bff.Api.Services.Ai.Membership.Models;

/// <summary>
/// The normalized identity for a single Dataverse <c>systemuser</c> across the
/// six person/group identity-type paths defined by the membership-resolution
/// contract. Returned by <see cref="IIdentityNormalizationService"/>.
/// </summary>
/// <param name="SystemUserId">Always populated — the input <c>systemuserid</c>.</param>
/// <param name="ContactId">
/// The matching <c>contactid</c> if a contact exists whose
/// <c>azureactivedirectoryobjectid</c> equals the systemuser's
/// <c>azureactivedirectoryobjectid</c> (per ADR-028). <c>null</c> if the user
/// has no corresponding contact (perfectly valid — many BFF callers are users
/// without an external-contact record).
/// </param>
/// <param name="PrimaryEmail">
/// The systemuser's <c>internalemailaddress</c> (preferred — Dataverse-owned)
/// or <c>domainname</c> as fallback. <c>null</c> if neither is set.
/// </param>
/// <param name="TeamIds">
/// Distinct teamids resolved via the <c>teammembership</c> intersect entity.
/// Empty list if the user is not a member of any team (always non-null).
/// </param>
/// <param name="BusinessUnitId">
/// The systemuser's <c>businessunitid</c>. Almost always populated; <c>null</c>
/// only if the systemuser row cannot be retrieved.
/// </param>
/// <param name="AccountId">
/// The <c>accountid</c> from the matching contact's <c>parentcustomerid</c>
/// (only when <c>parentcustomerid</c> points to an account). <c>null</c> if the
/// user has no contact, the contact has no parent customer, or the parent
/// customer is a contact rather than an account.
/// </param>
/// <param name="OrganizationIds">
/// Distinct <c>sprk_organizationid</c> values for organizations the user is
/// associated with via the configured mapping mechanism. Sourced by merging
/// results from all registered <see cref="IIdentityOrganizationResolver"/>
/// implementations (task 032). Empty list if no resolver is registered or none
/// of the registered resolvers return matches (always non-null).
/// </param>
public sealed record PersonIdentity(
    Guid SystemUserId,
    Guid? ContactId = null,
    string? PrimaryEmail = null,
    IReadOnlyList<Guid>? TeamIds = null,
    Guid? BusinessUnitId = null,
    Guid? AccountId = null,
    IReadOnlyList<Guid>? OrganizationIds = null)
{
    /// <summary>
    /// Distinct teamids resolved via <c>teammembership</c>. Always non-null at
    /// the consumer surface — defaults to an empty list when no teams resolve.
    /// </summary>
    public IReadOnlyList<Guid> TeamIds { get; init; } = TeamIds ?? Array.Empty<Guid>();

    /// <summary>
    /// Distinct <c>sprk_organizationid</c> values. Always non-null at the
    /// consumer surface — defaults to an empty list when no resolver match.
    /// </summary>
    public IReadOnlyList<Guid> OrganizationIds { get; init; } = OrganizationIds ?? Array.Empty<Guid>();
}
