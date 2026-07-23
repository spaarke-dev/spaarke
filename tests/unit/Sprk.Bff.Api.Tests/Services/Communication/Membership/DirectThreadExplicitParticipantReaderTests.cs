using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Membership;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication.Membership;

/// <summary>
/// Behavior of <see cref="DirectThreadExplicitParticipantReader"/> — the REAL
/// <see cref="IThreadExplicitParticipantReader"/> task 043 registers in place of task 041's ADR-032
/// <see cref="NullThreadExplicitParticipantReader"/> default. Protects the two contract properties
/// <see cref="ThreadMembershipDerivationService"/> (task 041) depends on: for a Direct thread it returns
/// EXACTLY the two participants as <see cref="ThreadExplicitParticipantKind.DirectParticipant"/>; for any
/// non-Direct thread it returns EMPTY (Open/record-anchored threads are unaffected).
/// </summary>
public class DirectThreadExplicitParticipantReaderTests
{
    private static readonly Guid ThreadId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid ParticipantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ParticipantB = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly Mock<IDirectThreadAccessService> _directThreadAccess = new();

    private DirectThreadExplicitParticipantReader BuildSut() => new(_directThreadAccess.Object);

    [Fact]
    public async Task GetExplicitParticipantsAsync_DirectThread_ReturnsBothParticipantsAsDirectParticipantKind()
    {
        _directThreadAccess
            .Setup(s => s.GetParticipantSystemUserIdsAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { ParticipantA, ParticipantB });

        var result = await BuildSut().GetExplicitParticipantsAsync(ThreadId);

        result.Select(p => p.Participant.RecordId).Should().BeEquivalentTo(new[] { ParticipantA, ParticipantB });
        result.Should().OnlyContain(p => p.Kind == ThreadExplicitParticipantKind.DirectParticipant);
        result.Should().OnlyContain(p => p.Participant.EntityLogicalName == "systemuser");
    }

    [Fact]
    public async Task GetExplicitParticipantsAsync_NonDirectThread_ReturnsEmpty()
    {
        // IDirectThreadAccessService itself is topology-gated — the reader is a thin adapter.
        _directThreadAccess
            .Setup(s => s.GetParticipantSystemUserIdsAsync(ThreadId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<Guid>());

        var result = await BuildSut().GetExplicitParticipantsAsync(ThreadId);

        result.Should().BeEmpty();
    }
}
