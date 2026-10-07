// R3 Part 1 — User-Record Membership Resolution (orchestration contract)
// Task 033 (2026-06-21): Public contract for the top-level membership resolver.
// Combines IMembershipFieldDiscoveryService (task 030), IIdentityNormalizationService
// (task 031), IOrganizationMembershipResolver (task 032 — consumed transitively via
// task 031), and a per-user Redis cache (2-min TTL since unified-access-control-r2 task 132; was 5) to build
// + execute a single OR-joined FetchXML query against the target entity and group
// matching rows by role.
//
// Per ADR-010 the interface exists as a testing seam — consumers
// (MembershipEndpoints — task 035, LookupUserMembershipNodeExecutor — task 041)
// get the concrete via DI; unit tests substitute a mock.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.5 through
//            FR-1A.9; design.md Part 1 § "Endpoint contract" response shape.

using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Top-level orchestration for user-record membership resolution. Combines
/// field discovery (per-entity metadata scan), identity normalization (systemuser
/// → 6-path PersonIdentity), and a per-user FetchXML query against the target
/// entity to return matching row ids grouped by role. Results cached in Redis
/// for 2 minutes (5 before unified-access-control-r2 task 132; a response built
/// over a faulted read is never cached); evicted by the BFF's own team / BU /
/// owner writes (task 132) and by the Phase 2 pub/sub invalidation (FR-2P2.8).
/// </summary>
public interface IMembershipResolverService
{
    /// <summary>
    /// Resolves the set of <paramref name="entityType"/> rows the given
    /// systemuser is a member of (via any discovered membership-bearing lookup),
    /// grouped by role. Returns a non-null <see cref="MembershipResponse"/>.
    /// </summary>
    /// <param name="systemUserId">
    /// Dataverse <c>systemuserid</c>. MUST NOT be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="entityType">
    /// Target entity logical name (e.g., <c>sprk_matter</c>). MUST NOT be
    /// null/empty/whitespace.
    /// </param>
    /// <param name="options">
    /// Optional filters + paging. <c>null</c> means: all discovered roles, all
    /// configured identity types, no transitive expansion, default limit (500),
    /// no continuation.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="MembershipResponse"/> with <c>ids[]</c> (sorted, deduplicated),
    /// <c>byRole</c> map (role → ids), <c>count</c>, <c>cacheExpiresAt</c>,
    /// and the <c>PersonIdentity</c> the resolver used. Always non-null —
    /// users with zero memberships return an empty <c>ids</c> + <c>count: 0</c>.
    /// </returns>
    Task<MembershipResponse> ResolveAsync(
        Guid systemUserId,
        string entityType,
        MembershipResolveOptions? options,
        CancellationToken ct);

    /// <summary>
    /// Contact-anchored membership entry point (teams-app-r1 task 021, ADR-034
    /// Path C additive reuse). Given a bare <paramref name="contactId"/> — with
    /// NO systemuser — builds a contact-only <see cref="PersonIdentity"/> and
    /// resolves the same membership record set the systemuser path returns, but
    /// <b>filtered to the access-conferring column registry</b> (spec.md NFR-05):
    /// only <c>contact</c>- or <c>sprk_organization</c>-target lookups that appear
    /// as an explicit entry for this entity in
    /// <c>MembershipOptions.AccessConferringRoles</c>, with the entry's declared
    /// identity type matching what live metadata discovery resolved.
    /// <para>
    /// ⚠️ <b>Corrected 2026-09-17 (task 043).</b> This paragraph previously described
    /// the retired <c>sprk_assigned*</c> <i>naming convention</i> plus an exclusion
    /// list. Task 041 (ADR-034 Amendment A1 / FR-24) DELETED that prefix check — it
    /// is not layered under the registry, it is gone — so a reader trusting this
    /// doc would have concluded that naming a new column <c>sprk_assigned*</c> still
    /// confers access. It does not; only a reviewed registry entry does, which is
    /// the entire point of FR-24.
    /// </para>
    /// Adverse/informational contact fields
    /// (opposing-counsel lookups) and polymorphic <c>sprk_regardingrecord*</c>
    /// fields NEVER confer access. Reuses the same discovery + FetchXml engine as
    /// <see cref="ResolveAsync"/> — it does not fork the resolution pipeline.
    /// <para>
    /// This entry point exists for non-systemuser workforce users (Owner
    /// Clarifications "Option B") whose principal (task 020) carries a contactId
    /// but no systemuserid. Transitive expansion (<c>includeRelated</c>) is not
    /// applied on this path; <see cref="MembershipResponse.RelatedByRole"/> is
    /// always <c>null</c>.
    /// </para>
    /// </summary>
    /// <param name="contactId">
    /// Dataverse <c>contactid</c>. MUST NOT be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="entityType">
    /// Target entity logical name (e.g., <c>sprk_project</c>). MUST NOT be
    /// null/empty/whitespace.
    /// </param>
    /// <param name="options">
    /// Optional role/identity-type filters + paging, applied AFTER the
    /// access-conferring allowlist filter. <c>null</c> means: all allowlisted
    /// contact roles, default limit, no continuation.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// A non-null <see cref="MembershipResponse"/> whose <c>PersonIdentity</c>
    /// carries the supplied <c>ContactId</c> and an empty <c>SystemUserId</c>.
    /// Contacts with no allowlisted role membership return empty <c>ids</c> +
    /// <c>count: 0</c>.
    /// </returns>
    /// <exception cref="ArgumentException">
    /// <paramref name="contactId"/> is <see cref="Guid.Empty"/>, or
    /// <paramref name="entityType"/> is null/empty/whitespace.
    /// </exception>
    Task<MembershipResponse> ResolveByContactAsync(
        Guid contactId,
        string entityType,
        MembershipResolveOptions? options,
        CancellationToken ct);
}

/// <summary>
/// Query-time filters + paging for <see cref="IMembershipResolverService.ResolveAsync"/>.
/// All fields optional. Mirrors the query parameters of the
/// <c>GET /api/users/me/memberships/{entityType}</c> endpoint contract per
/// design.md Part 1 § "Endpoint contract".
/// </summary>
/// <param name="Roles">
/// Narrows the descriptors considered to those whose <c>Role</c> matches one
/// of the supplied values (case-insensitive). <c>null</c> or empty → use all
/// discovered roles for the entity.
/// </param>
/// <param name="IdentityTypes">
/// Narrows the descriptors considered to those whose <c>IdentityType</c>
/// matches one of the supplied values (case-insensitive). <c>null</c> or empty
/// → use all configured identity types.
/// </param>
/// <param name="IncludeRelated">
/// Phase 1D — transitive memberships (e.g., expand to related documents/events).
/// Currently ACCEPTED-BUT-IGNORED — task 054 implements the expansion. Phase 1A
/// callers SHOULD pass <c>null</c>.
/// </param>
/// <param name="Limit">
/// Maximum row ids returned in <c>ids[]</c>. Default 500. Hard-capped at
/// <see cref="MaxLimit"/> server-side. When matches exceed the limit, a
/// continuation token is returned + ids[] is truncated.
/// </param>
/// <param name="ContinuationToken">
/// Opaque pagination cursor. Pass the value returned from a prior
/// <see cref="MembershipResponse.ContinuationToken"/> to fetch the next page.
/// </param>
/// <param name="AccessConferringOnly">
/// ADR-034 Amendment A1 / spec FR-24 (unified-access-control-r2 task 041). When <c>true</c> on
/// <see cref="IMembershipResolverService.ResolveAsync"/> (the systemuser plane), the resolver applies
/// the SAME access-conferring column registry filter <see cref="IMembershipResolverService.ResolveByContactAsync"/>
/// always applies — narrowing discovered descriptors to only those registered in
/// <c>MembershipOptions.AccessConferringRoles</c> for the target entity, covering both Contact- and
/// Organization-typed lookups. Default <c>false</c> leaves <see cref="ResolveAsync"/>'s existing
/// unfiltered (all-discovered-descriptors) scoping behavior byte-identical — this option is an explicit
/// opt-in for callers making an ACCESS decision (e.g. the accessible-record-set composer), never a
/// change to the AI-scoping default. Ignored by <see cref="IMembershipResolverService.ResolveByContactAsync"/>,
/// which already applies the registry filter unconditionally regardless of this flag's value.
/// </param>
/// <param name="OrganizationIds">
/// Design §4.5 term 4 — ORG EXPANSION (unified-access-control-r2 task 043 / spec FR-24 + FR-25). The
/// <c>sprk_organization</c> ids to bind into the resolved
/// <see cref="PersonIdentity.OrganizationIds"/> on the CONTACT plane
/// (<see cref="IMembershipResolverService.ResolveByContactAsync"/>), so that registry-listed
/// <b>org-typed</b> descriptors emit conditions instead of resolving to nothing.
/// <para>
/// ⚠️ <b>Supplied by the caller, deliberately not read here.</b> The ids come from the
/// <c>sprk_contactorganization</c> junction, which lives behind
/// <c>Infrastructure/ExternalAccess/ExternalParticipationService.ReadOrganizationMembershipsAsync</c> — a
/// read the accessible-record-set composer ALREADY performs for the FR-23 deny veto (since task 109 it
/// passes the read's CONFERRING set here — date-bounded, active organizations only — and keeps the
/// statecode-only WALL set for the veto). Resolving them here instead
/// would invert the layering (<c>Services/Ai/Membership</c> depending on
/// <c>Infrastructure/ExternalAccess</c>) and add a SECOND, separately-cached junction read that could
/// disagree with the first mid-composition. The pre-existing
/// <c>IIdentityOrganizationResolver</c> seam is not a substitute: its implementation resolves via the
/// configured <c>sprk_organization</c>→<c>systemuser</c> lookup
/// (<c>MembershipOptions.OrganizationLookup.UserLookupField</c>, default empty), which is a different
/// mechanism from the junction and returns nothing for a contact.
/// </para>
/// <para>
/// <c>null</c> or empty (the default) leaves the contact plane byte-identical to its pre-task-043
/// behaviour: the identity carries no org ids, so every org-typed descriptor emits zero conditions.
/// Callers wanting ONLY the org-derived records should pair this with
/// <c>IdentityTypes: ["Organization"]</c> — binding org ids alone does not stop the always-present
/// <c>ContactId</c> from binding, and a contact-derived record credited at an ORGANIZATION's baseline
/// is an over-grant that the returned id list cannot reveal.
/// </para>
/// </param>
/// <param name="PeopleTargeting">
/// ADR-034 Amendment A3 — the PEOPLE-TARGETING consumption surface (unified-access-control-r2 task 152; owner
/// decisions round 2 item 9 + Q8, round 3 D1). The third surface beside AI scoping (the default) and authorization
/// (<see cref="AccessConferringOnly"/>). It answers "which records are FOR this person" — the Daily Briefing, the
/// Workspace top-priority matter and every notification playbook's LookupUserMembership node — and it binds ONLY
/// person terms:
/// <list type="bullet">
/// <item><c>createdby</c> = the caller, and only when the caller is a HUMAN systemuser (<c>applicationid</c> null).
/// The BFF application user never receives "everything it created" through this surface. Admitted on this surface
/// only — the global <c>createdby</c> exclusion stays for AI scoping and authorization.</item>
/// <item>The user-valued owner: <c>ownerid</c> / <c>owninguser</c> = the caller. On a team-owned row <c>ownerid</c>
/// holds the team's id, so the condition never matches it.</item>
/// <item>The registry-listed CONTACT-typed "Assigned *" columns = the caller's LINKED contact
/// (<see cref="Models.PersonIdentity.ContactId"/>, task 141). No contact → the term binds nothing; never an email,
/// UPN or name match.</item>
/// </list>
/// <c>owningteam</c>, <c>owningbusinessunit</c> and every Team-, BusinessUnit-, Organization- or Account-typed
/// descriptor select NOTHING here: team and BU ownership are ACCESS facts, not attention facts — a team-owned record
/// never fans out to the team's members. This surface selects; it does not authorize. Callers that show the rows
/// MUST read them under the caller's Dataverse security (owner D1: Created By decides who a record is FOR, never who
/// can open it).
/// <para>
/// Mutually exclusive with <see cref="AccessConferringOnly"/>: requesting both throws
/// <see cref="ArgumentException"/> — the two surfaces answer different questions and are never merged. Default
/// <c>false</c> leaves every existing caller byte-identical (descriptors, FetchXML, cache key). Ignored by
/// <see cref="IMembershipResolverService.ResolveByContactAsync"/>, whose contact plane is always registry-filtered
/// and has no Created By (a contact never creates a Dataverse row).
/// </para>
/// </param>
public sealed record MembershipResolveOptions(
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<string>? IdentityTypes = null,
    IReadOnlyList<string>? IncludeRelated = null,
    int Limit = MembershipResolveOptions.DefaultLimit,
    string? ContinuationToken = null,
    bool AccessConferringOnly = false,
    IReadOnlyList<Guid>? OrganizationIds = null,
    bool PeopleTargeting = false)
{
    /// <summary>Default per-page row limit when not specified by caller.</summary>
    public const int DefaultLimit = 500;

    /// <summary>
    /// The people-targeting surface (ADR-034 A3) with every other option at its default. The one value the
    /// briefing, the top-priority matter and LookupUserMembership <c>targeting: "people"</c> pass.
    /// </summary>
    public static MembershipResolveOptions People { get; } = new(PeopleTargeting: true);

    /// <summary>
    /// Hard ceiling enforced server-side regardless of caller request.
    /// Protects against runaway queries on misconfigured FetchXml.
    /// </summary>
    public const int MaxLimit = 5000;
}
