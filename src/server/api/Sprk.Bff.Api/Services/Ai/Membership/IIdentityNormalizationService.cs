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

    /// <summary>
    /// Resolves a workforce-authenticated caller (who has <b>no</b> systemuser row) to a
    /// Dataverse <c>contact</c>, for the contact-only branch of the workforce→principal
    /// resolver (ADR-028 Amendment A2 · teams-app-r1 FR-04). Reuses the existing AAD-oid→contact
    /// cross-reference (<c>contact.azureactivedirectoryobjectid == oid</c>) and, when that field is
    /// unpopulated for the caller, falls back to an email match on <c>contact.emailaddress1</c>.
    ///
    /// <para>⚠️ <b>CORRECTED 2026-08-27 (task 013 / finding A-18).</b> This comment previously read:
    /// <i>"The email is taken from the already-validated workforce token (tenant-verified by Entra),
    /// so no oid-binding / first-login hijack protection is required here (that concern is specific to
    /// the CIAM external path)."</i> <b>That is the exact assumption A-18 disproved, and it was load-bearing
    /// — it is why this path shipped without the protection the CIAM path has.</b></para>
    ///
    /// <para>Two things make it false. (1) The email is NOT verified by anything in this codebase:
    /// <c>WorkforcePrincipalResolver.ExtractVerifiedEmail</c> performs no verification — despite its name
    /// — it returns the first present claim of <c>email</c> → <c>preferred_username</c> → <c>upn</c> →
    /// <c>ClaimTypes.Email</c>. <c>preferred_username</c> and <c>upn</c> are mutable and Microsoft
    /// documents them as unsuitable for authorization. (2) "Tenant-verified by Entra" does not hold for a
    /// <b>multitenant</b> registration, and multitenant workforce sign-in is REQUIRED here, not incidental
    /// — it is the Type 2 door (design.md §Principal types), prerequisite E-6, and ADR-028 Amendment A2
    /// for the Teams host.</para>
    ///
    /// <para>Task 013 added the oid-binding guard to this method
    /// (<c>DecideWorkforceEmailMatch</c>: deny when the contact is bound to a different oid, when the
    /// email is ambiguous, when the caller oid is unusable, or when the binding is unreadable).
    /// <b>Unbound contacts still resolve, per FR-12</b> — and because this plane never WRITES a binding
    /// (unlike CIAM, which binds on first login and thereby closes its own window), the A-18 exposure
    /// remains open indefinitely for any contact holding grants but no workforce oid. That is an open
    /// escalation, not a closed finding: <b>do not mark A-18 closed on the strength of task 013 alone.</b></para>
    ///
    /// <para>🔴 This guard does NOT cover every caller of the email→contact path. See
    /// <c>AccessibleRecordSetService</c>, which calls <c>ResolveExternalContactAsync(oid: null, …)</c> —
    /// a different method that bypasses this guard by construction.</para>
    /// </summary>
    /// <param name="aadObjectId">The workforce token's AAD object id (<c>oid</c> claim). Primary key.</param>
    /// <param name="verifiedEmail">
    /// The caller's tenant-verified email/UPN from the workforce token; used only when the oid
    /// cross-reference returns no contact. May be <c>null</c> (then only the oid path is attempted).
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The resolved <c>contactid</c>, or <c>null</c> when the caller matches neither a contact by
    /// oid nor by verified email. A single-path failure (query error) is isolated and yields
    /// <c>null</c>, never an exception.
    /// </returns>
    Task<Guid?> TryResolveContactByWorkforceIdentityAsync(
        Guid aadObjectId,
        string? verifiedEmail,
        CancellationToken ct);
}
