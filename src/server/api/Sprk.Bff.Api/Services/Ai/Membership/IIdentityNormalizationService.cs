// R3 Part 1 — User-Record Membership Resolution (identity normalization contract)
// Task 031 (2026-06-21): Public contract for resolving a systemuserid into the
// six identity components described in design.md Part 1 § Identity normalization
// contract. The interface is intentionally small — the heavy lifting
// (parallel sub-queries, per-path failure isolation, Redis caching) lives in
// the implementation. Per ADR-010, the interface exists as a testing seam —
// consumers (MembershipResolverService in task 033) get the concrete via DI,
// but unit tests substitute a mock implementation.
//
// unified-access-control-r2 task 141: the workforce contact-only resolution that used to live here
// (TryResolveContactByWorkforceIdentityAsync — an email match against a column that does not exist in dev)
// moved to Infrastructure/ExternalAccess/ContactIdentityBinder, which binds by Entra oid. This service stays
// READ-ONLY; InvalidateAsync lets a writer make a link effective on the same request.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.5;
//            ADR-010 (DI minimalism — interface allowed when testing seam needed).

using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Resolves a Dataverse <c>systemuserid</c> into a fully-populated
/// <see cref="PersonIdentity"/>. Implementations cache results in Redis with a
/// 2-minute TTL per ADR-009 (task 132; was 10). Each of the six identity-type paths
/// (systemuser, contact, team, businessunit, account, organization) is resolved
/// independently — failure on one path produces a <c>null</c> / empty value for
/// that field, never an exception — and, since task 132, a FAILED path is recorded on the identity
/// (internal <c>PersonIdentity.Faults</c>) and such an identity is never cached.
/// </summary>
public interface IIdentityNormalizationService
{
    /// <summary>
    /// Returns the normalized <see cref="PersonIdentity"/> for the given
    /// systemuserid. Always returns a non-null record — fields are <c>null</c>
    /// or empty when the corresponding identity type does not apply to this user.
    /// </summary>
    /// <param name="systemUserId">
    /// The Dataverse <c>systemuserid</c> primary key. MUST NOT be <see cref="Guid.Empty"/>.
    /// </param>
    /// <param name="ct">Cancellation token; honored across all internal sub-queries.</param>
    /// <returns>
    /// A populated <see cref="PersonIdentity"/>. <see cref="PersonIdentity.SystemUserId"/>
    /// always equals the input parameter; other fields populate based on what
    /// Dataverse returns. Cached for 2 minutes per ADR-009 — unless a sub-read faulted (task 132). <see cref="PersonIdentity.ContactId"/> comes from
    /// the user's <c>sprk_primarycontact</c> link, else from the contact bound to the user's Entra oid
    /// (<c>contact.sprk_externalobjectid</c>) — never from an email.
    /// </returns>
    Task<PersonIdentity> ResolveAsync(Guid systemUserId, CancellationToken ct);

    /// <summary>
    /// Drops the cached identity for <paramref name="systemUserId"/> (current tenant), so a link written during
    /// this request takes effect on this request instead of after the TTL (task 141). Never throws
    /// for a cache fault — the entry then expires on its own.
    /// </summary>
    Task InvalidateAsync(Guid systemUserId, CancellationToken ct);
}
