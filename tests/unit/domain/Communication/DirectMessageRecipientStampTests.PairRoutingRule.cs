using FluentAssertions;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Task 054a piece 2b: which sends the outbound Direct pair routing considers at all
/// (<see cref="CommunicationService.IsOutboundDirectSend"/>). Only a Message send naming no thread whose prepared row carries
/// no typed regarding lookup; an email send, a respond and a record-filed send never reach the routing.
/// </summary>
public partial class DirectMessageRecipientStampTests
{
    [Fact]
    public void IsOutboundDirectSend_OnlyForAMessage_NamingNoThread_FiledUnderNoRecord()
    {
        var unfiled = new Entity("sprk_communication");
        var filed = new Entity("sprk_communication") { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.NewGuid()) };
        var filedToNothing = new Entity("sprk_communication") { ["sprk_regardingmatter"] = new EntityReference("sprk_matter", Guid.Empty) };

        CommunicationService.IsOutboundDirectSend(MessageRequest(), unfiled).Should().BeTrue();
        CommunicationService.IsOutboundDirectSend(MessageRequest(), filedToNothing).Should().BeTrue("an empty id files under nothing");
        CommunicationService.IsOutboundDirectSend(MessageRequest(), filed).Should().BeFalse("a record-filed message is not Direct");
        CommunicationService.IsOutboundDirectSend(MessageRequest() with { ThreadId = DirectThread }, unfiled)
            .Should().BeFalse("a respond names its thread");
        CommunicationService.IsOutboundDirectSend(MessageRequest() with { CommunicationType = CommunicationType.Email }, unfiled)
            .Should().BeFalse("an email is never Direct");
    }

    // ── The one Direct-pair shape rule (DirectMessageRecipient.ReadStrictPairShapeAsync) ───────────────────────────────

    private Task<DirectPairShape> Shape() =>
        DirectMessageRecipient.ReadStrictPairShapeAsync(_entities.Object, _shares.Object, DirectThread, CancellationToken.None);

    [Fact]
    public async Task Shape_OfAStrictPairThread_IsAPair_NamingTheOwnerAndTheOther()
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: Sender);
        _threadShares = new() { UserShare(Other, ReadMask | AppendMask) };

        var shape = await Shape();

        shape.IsStrictPair.Should().BeTrue();
        shape.OwnerId.Should().Be(Sender);
        shape.OtherId.Should().Be(Other);
        shape.Reason.Should().BeNull();
    }

    private const int AppendMask = 4;

    public static TheoryData<string> ExtraPrincipalCases =>
        new() { "user-zero-mask", "user-append-only", "user-read-and-team", "team-zero-mask", "owner-own-share" };

    /// <summary>
    /// Fix round 2 (F2-1): beside the other participant's Read share, ANY other share row refuses — a systemuser at mask 0 or
    /// Append only, a team at any mask, a row naming the owner itself.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExtraPrincipalCases))]
    public async Task Shape_OfAThreadWithAnyOtherShareBesideTheOthersRead_IsExtraPrincipal(string extra)
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: Sender);
        _threadShares = new() { UserShare(Other, ReadMask) };
        _threadShares.Add(extra switch
        {
            "user-zero-mask" => UserShare(Third, 0),
            "user-append-only" => UserShare(Third, AppendMask),
            "user-read-and-team" => new DataversePrincipalAccess(DataversePrincipalRef.Team(ATeam), ReadMask, DateTimeOffset.UtcNow),
            "team-zero-mask" => new DataversePrincipalAccess(DataversePrincipalRef.Team(ATeam), 0, DateTimeOffset.UtcNow),
            _ => UserShare(Sender, ReadMask),
        });

        (await Shape()).Should().Match<DirectPairShape>(s =>
            !s.IsStrictPair && s.Reason == DirectPairShapeReason.ExtraPrincipal && s.OwnerId == null && s.OtherId == null);
    }

    [Fact]
    public async Task Shape_OfANonDirectThread_IsNotDirect()
    {
        _threadRow = ThreadRow(ThreadTypeRecordAnchored, owner: Sender);

        (await Shape()).Should().Match<DirectPairShape>(s => !s.IsStrictPair && s.Reason == DirectPairShapeReason.NotDirect);
    }

    [Fact]
    public async Task Shape_OfAMissingThread_IsNotDirect()
    {
        _threadRow = null!;

        (await Shape()).Should().Match<DirectPairShape>(s => !s.IsStrictPair && s.Reason == DirectPairShapeReason.NotDirect);
    }

    [Fact]
    public async Task Shape_OfADirectThreadCarryingATypedRegarding_IsTypedRegarding()
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: Sender, matter: Guid.NewGuid());

        (await Shape()).Should().Match<DirectPairShape>(s => !s.IsStrictPair && s.Reason == DirectPairShapeReason.TypedRegarding);
    }

    [Theory]
    [InlineData(100000001)]
    [InlineData(null)]
    public async Task Shape_OfAThreadNotConfirmedOpen_IsNotOpen(int? privacy)
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: Sender);
        if (privacy is { } p)
            _threadRow["sprk_privacystate"] = new OptionSetValue(p);
        else
            _threadRow.Attributes.Remove("sprk_privacystate");

        (await Shape()).Should().Match<DirectPairShape>(s => !s.IsStrictPair && s.Reason == DirectPairShapeReason.NotOpen);
    }

    public static TheoryData<string> NotTwoReadParticipantsCases => new() { "no-read", "three", "team-owner", "alone" };

    [Theory]
    [MemberData(nameof(NotTwoReadParticipantsCases))]
    public async Task Shape_OfAThreadWithoutExactlyTwoReadParticipants_IsNotTwoReadParticipants(string shape)
    {
        switch (shape)
        {
            case "no-read":
                _threadShares = new() { UserShare(Other, 0) };
                break;
            case "three":
                _threadShares = new() { UserShare(Other, ReadMask), UserShare(Third, ReadMask) };
                break;
            case "team-owner":
                _threadRow = ThreadRow(ThreadTypeDirect, owner: null);
                _threadShares = new() { UserShare(Sender, ReadMask), UserShare(Other, ReadMask) };
                break;
            case "alone":
                _threadShares = new() { UserShare(Sender, ReadMask) };
                break;
        }

        (await Shape()).Should().Match<DirectPairShape>(s =>
            !s.IsStrictPair && s.Reason == DirectPairShapeReason.NotTwoReadParticipants && s.OwnerId == null && s.OtherId == null);
    }

    [Fact]
    public async Task Shape_WhenTheShareReadFaults_IsAParticipantReadFault_NeverAPair()
    {
        var fault = new HttpRequestException("simulated 503 from Dataverse");
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .ThrowsAsync(fault);

        var shape = await Shape();

        shape.IsStrictPair.Should().BeFalse();
        shape.Reason.Should().Be(DirectPairShapeReason.ParticipantReadFault);
        shape.Fault.Should().BeSameAs(fault);
    }

    [Fact]
    public async Task Shape_WhenTheThreadReadFaults_IsAParticipantReadFault()
    {
        _entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", DirectThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated read fault"));

        (await Shape()).Should().Match<DirectPairShape>(s => !s.IsStrictPair && s.Reason == DirectPairShapeReason.ParticipantReadFault);
    }

    [Fact]
    public async Task Shape_PropagatesTheCallersCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        _entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", DirectThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var read = () => DirectMessageRecipient.ReadStrictPairShapeAsync(_entities.Object, _shares.Object, DirectThread, cts.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void ShapeReasonCodes_AreTheSixNamedCodes()
    {
        new[]
        {
            DirectPairShapeReason.NotDirect, DirectPairShapeReason.TypedRegarding, DirectPairShapeReason.NotOpen,
            DirectPairShapeReason.NotTwoReadParticipants, DirectPairShapeReason.ExtraPrincipal, DirectPairShapeReason.ParticipantReadFault,
        }.Should().Equal(
            "not_direct", "typed_regarding", "not_open", "not_two_read_participants", "extra_principal", "participant_read_fault");
        DirectPairFilingRefusedException.NotThePair.Should().Be("not_the_pair");
    }
}
