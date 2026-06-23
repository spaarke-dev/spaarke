// R3 Part 1 — User-Record Membership Resolution (identity normalization contract)
// Task 031 (2026-06-21): Public contract for resolving a systemuserid into the
// six identity components described in design.md Part 1 § Identity normalization
// contract. The interface is intentionally tiny (one method) — the heavy lifting
// (parallel sub-queries, per-path failure isolation, Redis caching) lives in
// the implementation. Per ADR-010, the interface exists as a testing seam —
// consumers (MembershipResolverService in task 033) get the concrete via DI,
// but unit tests substitute a mock implementation.
//
// Reference: projects/spaarke-platform-foundations-r3/spec.md FR-1A.5;
//            ADR-010 (DI minimalism — interface allowed when testing seam needed).

using Sprk.Bff.Api.Services.Ai.Membership.Models;

namespace Sprk.Bff.Api.Services.Ai.Membership;

/// <summary>
/// Resolves a Dataverse <c>systemuserid</c> into a fully-populated
/// <see cref="PersonIdentity"/>. Implementations cache results in Redis with a
/// 10-minute TTL per ADR-009. Each of the six identity-type paths
/// (systemuser, contact, team, businessunit, account, organization) is resolved
/// independently — failure on one path produces a <c>null</c> / empty value for
/// that field, never an exception.
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
    /// Dataverse returns. Cached for 10 minutes per ADR-009.
    /// </returns>
    Task<PersonIdentity> ResolveAsync(Guid systemUserId, CancellationToken ct);
}
