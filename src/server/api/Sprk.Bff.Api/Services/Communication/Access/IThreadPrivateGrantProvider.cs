namespace Sprk.Bff.Api.Services.Communication.Access;

/// <summary>
/// Supplies the ACTIVE per-record overlay grants that let a named participant read a PRIVATE thread's messages
/// (option B, owner-approved 2026-07-16 — <c>notes/spikes/002-private-grant-decision.md</c> §OWNER DECISION).
/// This is the SAME overlay-grant PATTERN as <c>sprk_externalrecordaccess</c> / <c>ExternalParticipationService</c>
/// (soft-delete revoke, <c>grantedBy</c>/<c>grantedDate</c>, app-only read) applied to the thread entity — NOT a
/// new authorization engine (design §5, root §11). It is the ONE seam the private-thread dimension depends on;
/// the open-thread dimension stays on <see cref="Sprk.Bff.Api.Services.Ai.Membership.IMembershipResolverService"/>
/// (ADR-034). Task 041's ACS reconcile enumerates the SAME grants to project ACS membership — keep the two in agreement.
/// </summary>
public interface IThreadPrivateGrantProvider
{
    /// <summary>
    /// Returns the caller's ACTIVE grants for the given thread (empty when none). "Active" = not soft-deleted and
    /// not expired. MUST fail closed: on any error the implementation returns an EMPTY list (deny), never throws
    /// open access. Each grant's <see cref="ThreadPrivateGrant.EffectiveFrom"/> gates private messages point-forward.
    /// </summary>
    Task<IReadOnlyList<ThreadPrivateGrant>> GetActiveGrantsAsync(
        Guid threadId,
        CommunicationAccessContext caller,
        CancellationToken ct = default);
}

/// <summary>
/// A single active overlay grant for a (thread, principal) pair. Mirrors the <c>sprk_externalrecordaccess</c> row
/// shape (grantedBy/grantedDate/expiry captured by the future Dataverse-backed provider) reduced to the two fields
/// the read filter needs: WHO the grant is for and the point-forward <see cref="EffectiveFrom"/> boundary.
/// </summary>
/// <param name="ThreadId">The private thread the grant applies to.</param>
/// <param name="PrincipalId">The granted principal (a <c>systemuserid</c> in R1; a <c>contactid</c> in R2).</param>
/// <param name="EffectiveFrom">
/// The point-forward boundary (D-04): the caller may read a private message only when
/// <c>message.createdon &gt;= EffectiveFrom</c>. Prior private messages stay restricted — there is no retroactive open.
/// </param>
/// <param name="ExpiresAt">Optional expiry; a grant past its expiry is NOT active and MUST be excluded by the provider.</param>
public sealed record ThreadPrivateGrant(
    Guid ThreadId,
    Guid PrincipalId,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? ExpiresAt = null);

/// <summary>
/// The FAIL-CLOSED default grant provider (ADR-032 Null-Object). It grants NOTHING, so every private-thread message
/// is invisible until a real Dataverse-backed provider is wired.
/// <para>
/// <b>Why this is the default in R1:</b> the thread-scoped overlay-grant SCHEMA does not exist yet — task 004 created
/// <c>sprk_communicationthread</c> + <c>sprk_communicationchannelref</c> only, and the existing
/// <c>sprk_externalrecordaccess</c> table is PROJECT-scoped (no thread lookup, no <c>effectiveFrom</c>), so it cannot
/// carry thread grants without new schema. Task 042 does not create schema (per its infra constraints). Registering
/// deny-all as the default keeps the composition filter's private dimension DEFAULT-DENY (NFR-06) and correct: the
/// point-forward + grant logic is fully implemented and unit-tested against a mock provider; production private-thread
/// access lights up when the overlay schema lands and a Dataverse-backed <see cref="IThreadPrivateGrantProvider"/>
/// replaces this registration (following the <c>sprk_externalrecordaccess</c> / <c>ExternalParticipationService</c>
/// precedent). See the task-042 decisions note + the shared-access-contract note for tasks 041/050.
/// </para>
/// </summary>
public sealed class DenyAllThreadPrivateGrantProvider : IThreadPrivateGrantProvider
{
    public Task<IReadOnlyList<ThreadPrivateGrant>> GetActiveGrantsAsync(
        Guid threadId,
        CommunicationAccessContext caller,
        CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ThreadPrivateGrant>>(Array.Empty<ThreadPrivateGrant>());
}
