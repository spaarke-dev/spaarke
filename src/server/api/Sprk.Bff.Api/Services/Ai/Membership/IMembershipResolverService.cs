// R3 Part 1 — User-Record Membership Resolution (orchestration contract)
// Task 033 (2026-06-21): Public contract for the top-level membership resolver.
// Combines IMembershipFieldDiscoveryService (task 030), IIdentityNormalizationService
// (task 031), IOrganizationMembershipResolver (task 032 — consumed transitively via
// task 031), and a per-user Redis cache (5-min TTL Phase 1A per FR-1A.8) to build
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
/// for 5 minutes (Phase 1A; Phase 2 task 086 extends TTL + adds pub/sub
/// invalidation per FR-2P2.8).
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
    /// <b>filtered to the access-conferring role allowlist</b> (spec.md NFR-05):
    /// only <c>contact</c>-target lookups whose logical name matches the
    /// configured convention (default <c>sprk_assigned*</c>, via
    /// <see cref="IMembershipFieldDiscoveryService"/> metadata discovery) minus a
    /// config/data-driven exclusion list. Adverse/informational contact fields
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
public sealed record MembershipResolveOptions(
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<string>? IdentityTypes = null,
    IReadOnlyList<string>? IncludeRelated = null,
    int Limit = MembershipResolveOptions.DefaultLimit,
    string? ContinuationToken = null,
    bool AccessConferringOnly = false)
{
    /// <summary>Default per-page row limit when not specified by caller.</summary>
    public const int DefaultLimit = 500;

    /// <summary>
    /// Hard ceiling enforced server-side regardless of caller request.
    /// Protects against runaway queries on misconfigured FetchXml.
    /// </summary>
    public const int MaxLimit = 5000;
}
