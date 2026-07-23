using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;

namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Real <see cref="IThreadExplicitParticipantReader"/> for the Direct 1:1 topology (task 043 / FR-09).
/// Replaces the ADR-032 <see cref="NullThreadExplicitParticipantReader"/> task 041 registered as the
/// interim default. For a Direct thread it returns the thread's two explicit participants (owner +
/// POA-shared principal, per <see cref="IDirectThreadAccessService"/>) as
/// <see cref="ThreadExplicitParticipantKind.DirectParticipant"/>; for any non-Direct thread it returns
/// EMPTY, so Open/record-anchored threads are unaffected — <see cref="ThreadMembershipDerivationService"/>
/// composes this reader's output with the ADR-034 reverse-membership set, and for a Direct thread the
/// reverse-membership half is naturally empty (no anchor), so the derived authorized set is EXACTLY the
/// two participants.
/// </summary>
public sealed class DirectThreadExplicitParticipantReader : IThreadExplicitParticipantReader
{
    private readonly IDirectThreadAccessService _directThreadAccess;

    public DirectThreadExplicitParticipantReader(IDirectThreadAccessService directThreadAccess)
    {
        _directThreadAccess = directThreadAccess ?? throw new ArgumentNullException(nameof(directThreadAccess));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ThreadExplicitParticipant>> GetExplicitParticipantsAsync(
        Guid threadId, CancellationToken ct = default)
    {
        // GetParticipantSystemUserIdsAsync is itself topology-gated (empty for non-Direct) — this reader is
        // a thin adapter, not a second topology check, so there is exactly one place the Direct-vs-not
        // branch lives (IDirectThreadAccessService).
        var participantIds = await _directThreadAccess.GetParticipantSystemUserIdsAsync(threadId, ct);
        if (participantIds.Count == 0)
            return Array.Empty<ThreadExplicitParticipant>();

        return participantIds
            .Select(id => new ThreadExplicitParticipant
            {
                Participant = ParticipantReference.SystemUser(id),
                Kind = ThreadExplicitParticipantKind.DirectParticipant,
            })
            .ToList();
    }
}
