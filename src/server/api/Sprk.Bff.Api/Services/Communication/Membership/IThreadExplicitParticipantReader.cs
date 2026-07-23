namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Reads the EXPLICIT participant/grant rows declared on a thread — the Direct-thread two-participant list
/// (task 043) and the Private-thread overlay grants (spike 002 option B; task 042). Kept behind a seam
/// (ADR-010) because the persisted grant-row shape is owned by tasks 042/043, which land in parallel with
/// this task on the shared tree. Task 041's derivation consumes this contract so the WRITE side (ACS
/// reconcile) and the READ side (task 042's query-filter) compute the SAME authorized set (design §5).
/// </summary>
/// <remarks>
/// <para><b>Shared authorized-set contract.</b> The derivation is
/// <c>authorized = MembershipResolver(anchor) ∪ activeExplicitGrants</c>. This reader supplies the
/// <c>activeExplicitGrants</c> half; ADR-034 reverse-membership supplies the other half. When task 042
/// finalizes the overlay-grant schema binding, it registers the real implementation behind this seam and
/// both sides agree by construction.</para>
/// <para><b>Default is the ADR-032 Null-Object</b> (<see cref="NullThreadExplicitParticipantReader"/>):
/// until the grant-row binding lands, a Private/Direct thread contributes no explicit participants, so the
/// reconcile projects only the ADR-034-derived open set. The projection-never-exceeds invariant holds under
/// either implementation because the reconcile only ever projects what derivation returns.</para>
/// </remarks>
public interface IThreadExplicitParticipantReader
{
    /// <summary>
    /// Returns the active explicit participants/grants for the thread (Direct list + Private overlay grants).
    /// MUST return a non-null (possibly empty) list. MUST NOT throw for a thread with no grants — return empty.
    /// </summary>
    Task<IReadOnlyList<ThreadExplicitParticipant>> GetExplicitParticipantsAsync(Guid threadId, CancellationToken ct = default);
}

/// <summary>
/// ADR-032 Null-Object default: no explicit participants/grants. Registered by
/// <c>CommunicationModule</c> until task 042 wires the concrete overlay-grant reader. With this default the
/// reconcile projects only the ADR-034-derived open membership — a Private thread's overlay grants become
/// effective the moment task 042's reader is registered, with NO change to task 041's derivation or reconcile.
/// </summary>
public sealed class NullThreadExplicitParticipantReader : IThreadExplicitParticipantReader
{
    public Task<IReadOnlyList<ThreadExplicitParticipant>> GetExplicitParticipantsAsync(Guid threadId, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<ThreadExplicitParticipant>>(Array.Empty<ThreadExplicitParticipant>());
}
