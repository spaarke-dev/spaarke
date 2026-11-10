using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Task 054a piece 2b, fix round 1. The thread <c>FindOrCreateDirectThreadAsync</c> answers is joined only when the Direct-pair
/// shape rule finds it a strict 1:1 Direct thread (typed Direct, no typed regarding, privacy Open, exactly two Read
/// participants, one of them its user owner) AND those two are the sender and the mapped recipient. Otherwise the message
/// takes today's resolver path and the skip event names the reason; nothing is written onto, or granted from, that thread.
/// The stamp is tied to the mapped recipient: when the thread's other participant at the filing is someone else, no recipient
/// is stamped. And the A9 read's <c>domainname</c> match maps the address like <c>internalemailaddress</c> does.
/// </summary>
public partial class DirectMessageRecipientStampTests
{
    /// <summary>A Direct thread already stored for the pair, with a shape the test sets.</summary>
    private static readonly Guid ExistingPairThread = Guid.Parse("5a5a5a5a-5a5a-5a5a-5a5a-5a5a5a5a5a5a");

    private const int PrivacyOpen = 100000000;
    private const int PrivacyPrivate = 100000001;
    private const string RoutedCode = "direct_message.pair_routed";
    private const string PairThreadUnreadCode = "direct_message.pair_thread_unread";
    private const string RecipientNotExpectedCode = "direct_message.recipient_not_expected";

    // ── F1-a: the found thread is verified before the join ─────────────────────────────────────────────────────────────

    /// <summary>(A) Owned by the recipient, shared with Read to the sender AND to a third user: three participants.</summary>
    [Fact]
    public async Task AFoundThreadWithAThirdReadParticipant_IsNotJoined()
    {
        FindOrCreateAnswersAStoredThread(
            StoredThread(owner: Other), new() { UserShare(Sender, ReadMask), UserShare(Third, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_not_two_read_participants", pairThreadAsked: true);
    }

    /// <summary>(B) Typed Direct, but carrying a typed regarding lookup: a record thread, never a pair.</summary>
    [Fact]
    public async Task AFoundDirectThreadCarryingATypedRegarding_IsNotJoined()
    {
        FindOrCreateAnswersAStoredThread(
            StoredThread(owner: Sender, matter: Guid.NewGuid()), new() { UserShare(Other, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_typed_regarding", pairThreadAsked: true);
    }

    /// <summary>
    /// (C) Owned by the sender; the recipient's share has no Read (mask 0) and a third user holds Read: its pair is
    /// {sender, third}, not {sender, recipient}.
    /// </summary>
    [Fact]
    public async Task AFoundThreadWhosePairIsTheSenderAndAThirdUser_IsNotJoined()
    {
        FindOrCreateAnswersAStoredThread(
            StoredThread(owner: Sender), new() { UserShare(Other, 0), UserShare(Third, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_not_the_pair", pairThreadAsked: true);
    }

    /// <summary>(D) The right pair, but the thread is not Open.</summary>
    [Fact]
    public async Task AFoundThreadThatIsNotOpen_IsNotJoined()
    {
        FindOrCreateAnswersAStoredThread(
            StoredThread(owner: Sender, privacy: PrivacyPrivate), new() { UserShare(Other, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_not_open", pairThreadAsked: true);
    }

    [Fact]
    public async Task AFoundThreadWithNoPrivacyValue_IsNotJoined()
    {
        FindOrCreateAnswersAStoredThread(
            StoredThread(owner: Sender, privacy: null), new() { UserShare(Other, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_not_open", pairThreadAsked: true);
    }

    /// <summary>A fault reading the found thread's shares: one Warning, today's path, the send succeeds.</summary>
    [Fact]
    public async Task AFaultReadingTheFoundThread_LogsAWarning_AndTakesTodaysPath()
    {
        FindOrCreateAnswersAStoredThread(StoredThread(owner: Sender), new() { UserShare(Other, ReadMask) });
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ExistingPairThread, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated 429 from Dataverse"));

        var response = await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        response.CommunicationId.Should().Be(MessageId);
        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient(skipReason: null, pairThreadAsked: true);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == PairThreadUnreadCode).Which.Level.Should().Be(LogLevel.Warning);
    }

    /// <summary>A clean stored pair thread found in the other orientation (owned by the recipient) is joined.</summary>
    [Fact]
    public async Task AFoundStrictPairThreadOwnedByTheRecipient_IsJoined_AndStampsTheRecipient()
    {
        FindOrCreateAnswersAStoredThread(StoredThread(owner: Other), new() { UserShare(Sender, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        var filing = _messageUpdates.Should().ContainSingle().Which;
        ((EntityReference)filing[ThreadLookup]).Id.Should().Be(ExistingPairThread);
        Recipient(filing).Should().Be(Other);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == RoutedCode).Which.Level.Should().Be(LogLevel.Information);
        _serviceLog.Entries.Should().Contain(e => e.Message.Contains("(outbound Direct pair thread)"));
        _serviceLog.Entries.Should().NotContain(e => e.Message.Contains("(respond-into-thread)"));
    }

    // ── F1-b: the stamp writes only the mapped recipient ───────────────────────────────────────────────────────────────

    /// <summary>
    /// The thread's pair changes between the shape check and the filing (the share read answers the recipient first, then a
    /// third user): the filing writes the thread lookup and no recipient.
    /// </summary>
    [Fact]
    public async Task WhenThePairChangesBetweenTheCheckAndTheFiling_NoRecipientIsStamped()
    {
        NoThreadStoredYet();
        var service = RoutingService();
        var shareReads = 0;
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++shareReads == 1
                ? new List<DataversePrincipalAccess> { UserShare(Other, ReadMask) }
                : new List<DataversePrincipalAccess> { UserShare(Third, ReadMask) });

        await service.SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        var filing = _messageUpdates.Should().ContainSingle().Which;
        ((EntityReference)filing[ThreadLookup]).Id.Should().Be(DirectThread);
        filing.Should().NotContainKey(RecipientColumn);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == RecipientNotExpectedCode)
            .Which.Level.Should().Be(LogLevel.Information);
    }

    // ── The A9 read's domainname match ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnAddressMatchingAUsersDomainName_MapsToThatUser_AndIsRouted()
    {
        NoThreadStoredYet();
        _directory.RemoveAll(u => u.Id == Other);
        _directory.Add((Other, "o.ther@contoso.com", false, "Other.User@contoso.onmicrosoft.com"));

        await RoutingService().SendAsync(MessageTo("other.user@CONTOSO.onmicrosoft.com"), Caller(), CancellationToken.None);

        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(Sender, Other, It.IsAny<CancellationToken>()), Times.Once);
        Recipient(_messageUpdates.Single()).Should().Be(Other);
    }

    // ── Rig ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The composed service with today's resolver and the email read; the pair-thread double is set by the test.</summary>
    private CommunicationService ShapeCheckedService() =>
        Service(new StubStrategy(ThreadKeyResolution.CreateNew), Directory().Object);

    /// <summary>The pair's thread already exists, as stored: <c>FindOrCreateDirectThreadAsync</c> answers it as it is.</summary>
    private void FindOrCreateAnswersAStoredThread(Entity row, List<DataversePrincipalAccess> shares)
    {
        _entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", ExistingPairThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(row);
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ExistingPairThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => shares.ToList());
        _shares
            .Setup(s => s.GetPrincipalAccessAsync("sprk_communicationthread", ExistingPairThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => shares.ToList());
        _directAccess
            .Setup(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ExistingPairThread);
    }

    private static Entity StoredThread(Guid owner, int? privacy = PrivacyOpen, Guid? matter = null)
    {
        var row = new Entity("sprk_communicationthread", ExistingPairThread)
        {
            ["sprk_threadtype"] = new OptionSetValue(ThreadTypeDirect),
            ["ownerid"] = new EntityReference("systemuser", owner),
        };
        if (privacy is { } p)
            row["sprk_privacystate"] = new OptionSetValue(p);
        if (matter is { } m)
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
        return row;
    }

    /// <summary>No filing write names the found thread, and no grant was made from it.</summary>
    private void AssertTheFoundThreadIsUntouched()
    {
        _messageUpdateAttempts
            .Where(u => u.TryGetValue(ThreadLookup, out var t) && ((EntityReference)t).Id == ExistingPairThread)
            .Should().BeEmpty("the found thread is never joined");
        _directAccess.Verify(
            d => d.GrantMessageAccessAsync(It.IsAny<Guid>(), It.Is<ThreadAssignment>(a => a.ThreadId == ExistingPairThread), It.IsAny<CancellationToken>()),
            Times.Never);
        _serviceLog.Entries.Should().NotContain(e => e.EventId.Name == RoutedCode);
    }
}
