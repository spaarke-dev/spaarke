using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;
using EA = Sprk.Bff.Api.Infrastructure.ExternalAccess;

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
    private const string PairFilingRefusedCode = "direct_message.pair_filing_refused";

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
    /// (C) Owned by the sender; the recipient's share has no Read (mask 0) and a third user holds Read: a share row besides the
    /// one Read share, so not a strict pair.
    /// </summary>
    [Fact]
    public async Task AFoundThreadWithTheRecipientAtMaskZeroAndAThirdUserAtRead_IsNotJoined()
    {
        FindOrCreateAnswersAStoredThread(
            StoredThread(owner: Sender), new() { UserShare(Other, 0), UserShare(Third, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_extra_principal", pairThreadAsked: true);
    }

    /// <summary>A strict pair of the sender and a third user, not the recipient.</summary>
    [Fact]
    public async Task AFoundThreadWhosePairIsTheSenderAndAThirdUser_IsNotJoined()
    {
        FindOrCreateAnswersAStoredThread(StoredThread(owner: Sender), new() { UserShare(Third, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_not_the_pair", pairThreadAsked: true);
    }

    public static TheoryData<string> ExtraShareCases => new() { "user-zero-mask", "user-append-only", "team" };

    /// <summary>
    /// Fix round 2 (F2-1): the recipient holds Read and one more principal holds any share — a third user at mask 0 or Append
    /// only, or a team. The thread is not joined, so nothing is filed or granted from it.
    /// </summary>
    [Theory]
    [MemberData(nameof(ExtraShareCases))]
    public async Task AFoundThreadWithAnotherShareBesideTheRecipientsRead_IsNotJoined(string extra)
    {
        var shares = new List<DataversePrincipalAccess>
        {
            UserShare(Other, ReadMask),
            extra switch
            {
                "user-zero-mask" => UserShare(Third, 0),
                "user-append-only" => UserShare(Third, 4),
                _ => new DataversePrincipalAccess(DataversePrincipalRef.Team(ATeam), ReadMask, DateTimeOffset.UtcNow),
            },
        };
        FindOrCreateAnswersAStoredThread(StoredThread(owner: Sender), shares);

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTheFoundThreadIsUntouched();
        AssertTodaysPathWithNoRecipient("pair_thread_extra_principal", pairThreadAsked: true);
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

    // ── F1-b / F2-2: the filing is decided before any write, against the routed pair ────────────────────────────────────

    /// <summary>
    /// The thread's pair changes between the shape check and the filing (the share read answers the recipient first, then a
    /// third user): nothing is written onto the pair's thread and nothing is granted from it; the message takes today's path.
    /// </summary>
    [Fact]
    public async Task WhenThePairChangesBetweenTheCheckAndTheFiling_NothingIsFiledOrGrantedThere_AndTodaysPathRuns()
    {
        FindOrCreateAnswersAThreadThatChanges(
            StoredThread(owner: Sender), new() { UserShare(Other, ReadMask) },
            StoredThread(owner: Sender), new() { UserShare(Third, ReadMask) });

        var response = await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        response.CommunicationId.Should().Be(MessageId);
        AssertRefusedAtTheFiling("not_the_pair");
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == RecipientNotExpectedCode)
            .Which.Level.Should().Be(LogLevel.Information);
    }

    /// <summary>A typed regarding appears on the thread between the shape check and the join: no reparent, no owner write.</summary>
    [Fact]
    public async Task WhenATypedRegardingAppearsBetweenTheCheckAndTheFiling_NothingIsFiledOrGrantedThere_AndTodaysPathRuns()
    {
        FindOrCreateAnswersAThreadThatChanges(
            StoredThread(owner: Sender), new() { UserShare(Other, ReadMask) },
            StoredThread(owner: Sender, matter: Guid.NewGuid()), new() { UserShare(Other, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertRefusedAtTheFiling("typed_regarding");
        _messageUpdateAttempts.Should().NotContain(u => u.ContainsKey("ownerid"), "the refused join reparents nothing");
    }

    /// <summary>The thread turns Private between the shape check and the join.</summary>
    [Fact]
    public async Task WhenTheThreadTurnsPrivateBetweenTheCheckAndTheFiling_NothingIsFiledOrGrantedThere_AndTodaysPathRuns()
    {
        FindOrCreateAnswersAThreadThatChanges(
            StoredThread(owner: Sender), new() { UserShare(Other, ReadMask) },
            StoredThread(owner: Sender, privacy: PrivacyPrivate), new() { UserShare(Other, ReadMask) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertRefusedAtTheFiling("not_open");
    }

    /// <summary>Another share appears beside the recipient's Read between the shape check and the filing (mask 0).</summary>
    [Fact]
    public async Task WhenAnotherShareAppearsBetweenTheCheckAndTheFiling_NothingIsFiledOrGrantedThere_AndTodaysPathRuns()
    {
        FindOrCreateAnswersAThreadThatChanges(
            StoredThread(owner: Sender), new() { UserShare(Other, ReadMask) },
            StoredThread(owner: Sender), new() { UserShare(Other, ReadMask), UserShare(Third, 0) });

        await ShapeCheckedService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertRefusedAtTheFiling("not_the_pair");
    }

    // ── F2-5: the caller's cancellation ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The caller cancels while the pair's thread is found: the routing ends with no Warning (the message is already sent and
    /// persisted, and the send still succeeds).
    /// </summary>
    [Fact]
    public async Task WhenTheCallerCancelsDuringTheRouting_NoRoutingWarningIsLogged_AndTheSendSucceeds()
    {
        using var cts = new CancellationTokenSource();
        NoThreadStoredYet();
        var service = Service(new StubStrategy(ThreadKeyResolution.CreateNew), Directory().Object);
        _directAccess
            .Setup(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<Guid>(new OperationCanceledException(cts.Token));
            });

        var response = await service.SendAsync(MessageTo(OtherAddress), Caller(), cts.Token);

        response.CommunicationId.Should().Be(MessageId);
        _serviceLog.Entries.Should().NotContain(e =>
            e.EventId.Name == PairThreadFailedCode || e.EventId.Name == UnresolvedCode || e.EventId.Name == PairThreadUnreadCode);
        AllFilingColumns().Should().NotContain(RecipientColumn);
    }

    [Fact]
    public async Task WhenTheCallerCancelsDuringTheRecipientRead_NoRoutingWarningIsLogged_AndTheSendSucceeds()
    {
        using var cts = new CancellationTokenSource();
        NoThreadStoredYet();
        var directory = new Mock<EA.IContactIdentityStore>();
        directory
            .Setup(s => s.FindEnabledSystemUsersByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                cts.Cancel();
                return Task.FromException<EA.SystemUserEmailLookup>(new OperationCanceledException(cts.Token));
            });

        var response = await Service(new StubStrategy(ThreadKeyResolution.CreateNew), directory.Object)
            .SendAsync(MessageTo(OtherAddress), Caller(), cts.Token);

        response.CommunicationId.Should().Be(MessageId);
        _serviceLog.Entries.Should().NotContain(e => e.EventId.Name == UnresolvedCode || e.EventId.Name == PairThreadFailedCode);
        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    /// <summary>A timeout that is not the caller's cancellation is still a fault: one Warning, today's path.</summary>
    [Fact]
    public async Task ACancellationThatIsNotTheCallers_IsAFault_LogsAWarning_AndTakesTodaysPath()
    {
        NoThreadStoredYet();
        var service = Service(new StubStrategy(ThreadKeyResolution.CreateNew), Directory().Object);
        _directAccess
            .Setup(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TaskCanceledException("simulated HTTP timeout"));

        await service.SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == PairThreadFailedCode).Which.Level.Should().Be(LogLevel.Warning);
        AssertTodaysPathWithNoRecipient(skipReason: null, pairThreadAsked: true);
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

    /// <summary>
    /// The pair's thread exists and changes after the shape check: the first read of the row and of its shares answers
    /// <paramref name="atCheck"/> / <paramref name="sharesAtCheck"/>, every later read <paramref name="afterCheck"/> /
    /// <paramref name="sharesAfterCheck"/>.
    /// </summary>
    private void FindOrCreateAnswersAThreadThatChanges(
        Entity atCheck, List<DataversePrincipalAccess> sharesAtCheck, Entity afterCheck, List<DataversePrincipalAccess> sharesAfterCheck)
    {
        FindOrCreateAnswersAStoredThread(atCheck, sharesAtCheck);
        var rowReads = 0;
        var shareReads = 0;
        _entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", ExistingPairThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++rowReads == 1 ? atCheck : afterCheck);
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", ExistingPairThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => (++shareReads == 1 ? sharesAtCheck : sharesAfterCheck).ToList());
        _shares
            .Setup(s => s.GetPrincipalAccessAsync("sprk_communicationthread", ExistingPairThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => sharesAfterCheck.ToList());
    }

    /// <summary>
    /// The routing passed the shape check, and the join refused before any write: no filing write names the pair's thread, no
    /// grant is made from it, one Information <c>pair_filing_refused</c> carries <paramref name="reason"/>, and the message took
    /// today's path (the resolver's create, filed with no recipient, its own grant).
    /// </summary>
    private void AssertRefusedAtTheFiling(string reason)
    {
        _messageUpdateAttempts
            .Where(u => u.TryGetValue(ThreadLookup, out var t) && ((EntityReference)t).Id == ExistingPairThread)
            .Should().BeEmpty("the refused thread is never written onto");
        _directAccess.Verify(
            d => d.GrantMessageAccessAsync(It.IsAny<Guid>(), It.Is<ThreadAssignment>(a => a.ThreadId == ExistingPairThread), It.IsAny<CancellationToken>()),
            Times.Never);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == RoutedCode);
        var refused = _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == PairFilingRefusedCode).Which;
        refused.Level.Should().Be(LogLevel.Information);
        refused.Message.Should().Contain($"({reason})").And.Contain(MessageId.ToString()).And.NotContain("@");
        _serviceLog.Entries.Should().NotContain(e => e.Message.Contains("Explicit thread stamp failed"));
        AssertTodaysPathWithNoRecipient(skipReason: null, pairThreadAsked: true);
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
