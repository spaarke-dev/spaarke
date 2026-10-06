// R3 Part 1 — User-Record Membership Resolution (PersonIdentity model)
// Task 031 (2026-06-21): The full normalized identity record for a single
// systemuserid, resolved across the six identity-type paths defined by the
// design.md Part 1 § Identity normalization contract table:
//
//   Lookup → systemuser    → SystemUserId (always populated)
//   Lookup → contact       → ContactId (systemuser.sprk_primarycontact, else the contact bound to the
//                            user's Entra oid via contact.sprk_externalobjectid — task 141)
//   Lookup → team          → TeamIds[] (expanded from teammembership)
//   Lookup → businessunit  → BusinessUnitId (from systemuser row)
//   Lookup → account       → AccountId (from primary contact's parentcustomerid)
//   Lookup → sprk_org      → OrganizationIds[] (via IIdentityOrganizationResolver
//                            implementations supplied by task 032)
//
// Each path is INDEPENDENT — a failure on one (e.g., user has no contact) does
// NOT fail the others. Cached in Redis with a 2-min TTL per ADR-009 (task 132; was 10) — and never when a
// path FAILED (Faults, task 132 / C12).
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

using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Membership.Models;

/// <summary>
/// The normalized identity for a single Dataverse <c>systemuser</c> across the
/// six person/group identity-type paths defined by the membership-resolution
/// contract. Returned by <see cref="IIdentityNormalizationService"/>.
/// </summary>
/// <param name="SystemUserId">Always populated — the input <c>systemuserid</c>.</param>
/// <param name="ContactId">
/// The user's contact: the systemuser's <c>sprk_primarycontact</c> link, else the
/// ACTIVE contact whose <c>sprk_externalobjectid</c> is the systemuser's Entra oid
/// (task 141). <c>null</c> if the user has neither (valid — the identity-link
/// reconciliation job links such users, or flags a collision).
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
    [property: JsonPropertyName("systemUserId")] Guid SystemUserId,
    [property: JsonPropertyName("contactId")] Guid? ContactId = null,
    [property: JsonPropertyName("primaryEmail")] string? PrimaryEmail = null,
    IReadOnlyList<Guid>? TeamIds = null,
    [property: JsonPropertyName("businessUnitId")] Guid? BusinessUnitId = null,
    [property: JsonPropertyName("accountId")] Guid? AccountId = null,
    IReadOnlyList<Guid>? OrganizationIds = null)
{
    /// <summary>
    /// Distinct teamids resolved via <c>teammembership</c>. Always non-null at
    /// the consumer surface — defaults to an empty list when no teams resolve.
    /// </summary>
    [JsonPropertyName("teamIds")]
    public IReadOnlyList<Guid> TeamIds { get; init; } = TeamIds ?? Array.Empty<Guid>();

    /// <summary>
    /// Distinct <c>sprk_organizationid</c> values. Always non-null at the
    /// consumer surface — defaults to an empty list when no resolver match.
    /// </summary>
    [JsonPropertyName("organizationIds")]
    public IReadOnlyList<Guid> OrganizationIds { get; init; } = OrganizationIds ?? Array.Empty<Guid>();

    /// <summary>
    /// Which identity sub-reads FAILED while this identity was resolved, as opposed to answering "none"
    /// (unified-access-control-r2 task 132 · defect C12). <see cref="IdentityReadFaults.None"/> for a complete
    /// resolution.
    /// </summary>
    /// <remarks>
    /// <para><b>Internal, and not serialized</b> — deliberately not part of the
    /// <c>GET /api/users/me/memberships</c> contract (this record is returned inside <c>MembershipResponse</c>).
    /// It never needs to survive the cache either: an identity with any fault is never cached, so a cache hit always
    /// reads back <see cref="IdentityReadFaults.None"/>, which is the truth for it.</para>
    /// <para>A faulted field still holds its fail-soft value (<c>null</c> / empty) for the current request; this
    /// flag only tells a consumer that the value is UNKNOWN rather than absent — see
    /// <see cref="ContactUnreadable"/>.</para>
    /// </remarks>
    [JsonIgnore]
    internal IdentityReadFaults Faults { get; init; }

    /// <summary>True when any identity sub-read failed (task 132).</summary>
    [JsonIgnore]
    internal bool IsFaulted => Faults != IdentityReadFaults.None;

    /// <summary>
    /// True when <see cref="ContactId"/> is <c>null</c> because the reads that decide it FAILED — the systemuser
    /// row (which carries <c>sprk_primarycontact</c> and the oid) or the oid-binding lookup — rather than because
    /// the user genuinely has no linked contact (task 132). A deny-veto subject that cannot be read must deny.
    /// </summary>
    [JsonIgnore]
    internal bool ContactUnreadable =>
        ContactId is null && (Faults & (IdentityReadFaults.SystemUser | IdentityReadFaults.ContactBinding)) != 0;
}

/// <summary>
/// The identity sub-reads of <c>IdentityNormalizationService</c> that can fail independently (task 132 · C12).
/// A flag is set only for a read that could not be COMPLETED (an exception, a timeout); a read that succeeded and
/// found nothing sets nothing.
/// </summary>
[Flags]
internal enum IdentityReadFaults
{
    None = 0,

    /// <summary>The <c>systemuser</c> row (business unit, email, oid, <c>sprk_primarycontact</c>).</summary>
    SystemUser = 1 << 0,

    /// <summary>The <c>teammembership</c> read.</summary>
    Teams = 1 << 1,

    /// <summary>The contact bound to the user's oid (<c>contact.sprk_externalobjectid</c>, task 141).</summary>
    ContactBinding = 1 << 2,

    /// <summary>The contact's <c>parentcustomerid</c> → account read.</summary>
    Account = 1 << 3,

    /// <summary>An organization resolver.</summary>
    Organizations = 1 << 4,
}
