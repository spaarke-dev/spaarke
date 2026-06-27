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
public sealed record MembershipResolveOptions(
    IReadOnlyList<string>? Roles = null,
    IReadOnlyList<string>? IdentityTypes = null,
    IReadOnlyList<string>? IncludeRelated = null,
    int Limit = MembershipResolveOptions.DefaultLimit,
    string? ContinuationToken = null)
{
    /// <summary>Default per-page row limit when not specified by caller.</summary>
    public const int DefaultLimit = 500;

    /// <summary>
    /// Hard ceiling enforced server-side regardless of caller request.
    /// Protects against runaway queries on misconfigured FetchXml.
    /// </summary>
    public const int MaxLimit = 5000;
}
