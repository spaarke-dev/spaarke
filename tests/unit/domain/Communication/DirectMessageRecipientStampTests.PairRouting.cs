using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;
using EA = Sprk.Bff.Api.Infrastructure.ExternalAccess;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Task 054a piece 2b (owner 2026-11-10, option a): an <b>outbound Direct send</b> — a Message send that names no thread and
/// is filed under no record — is filed into the Direct thread of its pair (sender, recipient) when its addresses
/// (To ∪ Cc ∪ Bcc, trimmed, case-insensitive, de-duplicated, empty entries dropped) are exactly one, that address maps to
/// exactly one enabled systemuser through the internal-user email read, and that systemuser is not the sender, reads enabled
/// and is not an application user. The pair's thread comes from <c>FindOrCreateDirectThreadAsync(sender, recipient)</c>, and
/// the message is filed through the respond path's explicit join, so the Direct filing stamp writes the recipient in the
/// filing update and the grant follows. Otherwise the send takes the thread resolver's path unchanged, one Information event
/// <c>direct_message.pair_routing_skipped</c> names the reason, and no recipient is stamped. A fault in the step never fails
/// the send.
/// </summary>
/// <remarks>
/// The internal-user email read is doubled as the directory it reads: enabled systemusers whose address matches,
/// case-insensitively, two rows at most (a disabled user's row is not returned). The pair-thread double is the thread as
/// stored: the first call for a pair creates it, owned by that caller and shared with Read to the other party; every later
/// call for the same pair, in either orientation, answers the same thread and changes nothing. "Today's path" is the
/// resolver CREATE (the key strategy answers CreateNew for a fresh ACS thread): an application-owned, unshared Direct thread.
/// </remarks>
public partial class DirectMessageRecipientStampTests
{
    private const string SkippedCode = "direct_message.pair_routing_skipped";
    private const string UnresolvedCode = "direct_message.pair_routing_unresolved";
    private const string PairThreadFailedCode = "direct_message.pair_thread_failed";

    private const string SenderAddress = "sender@contoso.com";
    private const string OtherAddress = "other@contoso.com";
    private const string ThirdAddress = "third@contoso.com";
    private const string AppUserAddress = "integration@contoso.com";

    /// <summary>The systemusers the internal-user email read can see: (id, address, disabled).</summary>
    private readonly List<(Guid Id, string Address, bool Disabled)> _directory = new()
    {
        (Sender, SenderAddress, false),
        (Other, OtherAddress, false),
        (Third, ThirdAddress, false),
        (AppUser, AppUserAddress, false),
    };

    /// <summary>Every address the internal-user email read was asked for.</summary>
    private readonly List<string> _emailReads = new();

    /// <summary>Every (caller, other) pair the pair-thread double was asked for.</summary>
    private readonly List<(Guid Caller, Guid Other)> _pairCalls = new();

    private Guid? _pairThreadOwner;
    private Guid? _pairThreadSharedTo;
    private int _pairThreadsCreated;

    // ── Must fire ──────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OutboundDirectSend_ToOneInternalUser_IsFiledIntoThePairsDirectThread_InOneFilingUpdateCarryingTheRecipient_AndGranted()
    {
        NoThreadStoredYet();

        var response = await RoutingService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        response.CommunicationId.Should().Be(MessageId);
        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(Sender, Other, It.IsAny<CancellationToken>()), Times.Once);
        _pairThreadsCreated.Should().Be(1);

        var filing = _messageUpdates.Should().ContainSingle("the message is filed in ONE update").Which;
        ((EntityReference)filing[ThreadLookup]).Id.Should().Be(DirectThread);
        Recipient(filing).Should().Be(Other);
        filing.Should().NotContainKey(SenderColumn, "the sent row already carries its sender, which is kept as it is");

        _directAccess.Verify(
            d => d.GrantMessageAccessAsync(
                MessageId,
                It.Is<ThreadAssignment>(a => a.ThreadId == DirectThread && a.Audience == ThreadGrantAudience.DirectParticipants),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _created.Should().NotContain(e => e.LogicalName == "sprk_communicationthread", "the resolver created no thread");
        _serviceLog.Entries.Should().NotContain(e => e.EventId.Name == SkippedCode || e.EventId.Name == UnresolvedCode);
    }

    [Fact]
    public async Task SendsBetweenTheSamePair_InBothOrientations_ReuseTheOneDirectThread_AndEachStampsTheOtherParty()
    {
        NoThreadStoredYet();

        await RoutingService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);
        Recipient(_messageUpdates.Single()).Should().Be(Other);
        _messageUpdates.Clear();

        await RoutingService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);
        Recipient(_messageUpdates.Single()).Should().Be(Other);
        _messageUpdates.Clear();

        _messageRow = MessageRow(sentBy: Other);
        await RoutingService().SendAsync(MessageTo(SenderAddress), Caller(OtherOid, OtherAddress), CancellationToken.None);
        var reply = _messageUpdates.Single();
        ((EntityReference)reply[ThreadLookup]).Id.Should().Be(DirectThread);
        Recipient(reply).Should().Be(Sender);

        _pairCalls.Should().Equal((Sender, Other), (Sender, Other), (Other, Sender));
        _pairThreadsCreated.Should().Be(1, "the pair's thread is created once and reused in both orientations");
        _threadRow.GetAttributeValue<EntityReference>("ownerid")!.Id.Should().Be(Sender, "the first sender owns the thread");
        _created.Should().NotContain(e => e.LogicalName == "sprk_communicationthread");
    }

    [Fact]
    public async Task AddressMatching_IsCaseAndWhitespaceInsensitive_AndTheSameAddressInToAndCcCountsOnce()
    {
        NoThreadStoredYet();
        var request = MessageTo("  Other@Contoso.COM ") with { Cc = new[] { "other@contoso.com", " ", "" } };

        await RoutingService().SendAsync(request, Caller(), CancellationToken.None);

        _emailReads.Should().Equal(OtherAddress);
        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(Sender, Other, It.IsAny<CancellationToken>()), Times.Once);
        Recipient(_messageUpdates.Single()).Should().Be(Other);
    }

    // ── Must not fire: an outbound Direct send that does not resolve ──────────────────────────────────────────────────

    [Fact]
    public async Task TwoAddresses_AreNotRouted()
    {
        await RoutingService().SendAsync(MessageTo(OtherAddress, ThirdAddress), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("not_one_address");
        _emailReads.Should().BeEmpty("the count is decided before any address is mapped");
    }

    [Fact]
    public async Task OneAddressAndACcOfAnother_AreNotRouted()
    {
        await RoutingService().SendAsync(MessageTo(OtherAddress) with { Cc = new[] { ThirdAddress } }, Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("not_one_address");
    }

    [Fact]
    public async Task OneRecipientAndTheSendersOwnAddressInCc_CountAsTwo_AndAreNotRouted()
    {
        await RoutingService().SendAsync(MessageTo(OtherAddress) with { Cc = new[] { SenderAddress } }, Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("not_one_address");
    }

    [Fact]
    public async Task OneAddressInBccOnly_IsTheOneAddress_ButAnExternalAddressIsNotRouted()
    {
        await RoutingService().SendAsync(MessageTo() with { Bcc = new[] { "partner@outside.test" } }, Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("no_internal_user");
        _emailReads.Should().Equal("partner@outside.test");
    }

    [Fact]
    public async Task AnExternalAddress_IsNotRouted()
    {
        await RoutingService().SendAsync(MessageTo("partner@outside.test"), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("no_internal_user");
    }

    [Fact]
    public async Task AnAddressTwoSystemUsersHold_IsNotRouted()
    {
        _directory.Add((Guid.Parse("45454545-4545-4545-4545-454545454545"), OtherAddress, false));

        await RoutingService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("ambiguous_address");
    }

    /// <summary>The internal-user email read returns enabled users only, so a disabled user's address maps to nobody.</summary>
    [Fact]
    public async Task ADisabledUsersAddress_IsNotRouted()
    {
        _directory.RemoveAll(u => u.Id == Other);
        _directory.Add((Other, OtherAddress, true));
        _disabled.Add(Other);

        await RoutingService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("no_internal_user");
    }

    /// <summary>The one mapped user reads disabled at the eligibility read (disabled after the email read).</summary>
    [Fact]
    public async Task AMappedUserWhoReadsDisabled_IsNotRouted()
    {
        _disabled.Add(Other);

        await RoutingService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("recipient_disabled");
    }

    [Fact]
    public async Task AnApplicationUser_IsNotRouted()
    {
        await RoutingService().SendAsync(MessageTo(AppUserAddress), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("recipient_application_user");
    }

    [Fact]
    public async Task TheSendersOwnAddress_IsNotRouted()
    {
        await RoutingService().SendAsync(MessageTo(SenderAddress), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("recipient_is_sender");
    }

    [Fact]
    public async Task WithoutTheDirectThreadAccessService_NothingIsRouted()
    {
        await Service(new StubStrategy(ThreadKeyResolution.CreateNew), Directory().Object, directAccess: false)
            .SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        AssertTodaysPathWithNoRecipient("not_configured", granted: false);
        _emailReads.Should().BeEmpty();
    }

    /// <summary>
    /// A refused send writes exactly what a service without the routing writes: the same thread create and the same filing
    /// update, with no recipient and no share.
    /// </summary>
    [Fact]
    public async Task ARefusedSend_WritesExactlyWhatTodaysPathWrites()
    {
        await Service(new StubStrategy(ThreadKeyResolution.CreateNew)).SendAsync(MessageTo(OtherAddress, ThirdAddress), Caller(), CancellationToken.None);
        var todaysThread = Comparable(_created.Single(e => e.LogicalName == "sprk_communicationthread").Attributes);
        var todaysFilings = _messageUpdates.Select(Comparable).ToList();
        _created.Clear();
        _messageUpdates.Clear();
        _serviceLog.Entries.Clear(); // the baseline service logs its own "not_configured" skip

        await RoutingService().SendAsync(MessageTo(OtherAddress, ThirdAddress), Caller(), CancellationToken.None);

        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        Comparable(_created.Single(e => e.LogicalName == "sprk_communicationthread").Attributes).Should().BeEquivalentTo(todaysThread);
        _messageUpdates.Select(Comparable).Should().BeEquivalentTo(todaysFilings);
        AllFilingColumns().Should().NotContain(RecipientColumn);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == SkippedCode)
            .Which.Message.Should().Contain("(not_one_address)");
    }

    /// <summary>A write's columns and values, without the clock (a timestamp differs between two runs).</summary>
    private static Dictionary<string, string?> Comparable(IEnumerable<KeyValuePair<string, object>> columns) =>
        columns
            .Where(c => c.Value is not DateTime and not DateTimeOffset)
            .ToDictionary(c => c.Key, c => c.Value switch
            {
                EntityReference r => $"{r.LogicalName}:{r.Id}",
                OptionSetValue o => o.Value.ToString(),
                _ => c.Value?.ToString(),
            });

    // ── Must not fire: not an outbound Direct send (no routing event) ─────────────────────────────────────────────────

    [Fact]
    public async Task ARecordFiledSend_IsNotRouted_AndLogsNoRoutingEvent()
    {
        var matter = Guid.NewGuid();
        _messageRow = MessageRow(sentBy: Sender, regardingMatter: matter);
        var request = MessageTo(OtherAddress) with
        {
            Associations = new[] { new CommunicationAssociation { EntityType = "sprk_matter", EntityId = matter } },
        };

        await RoutingService().SendAsync(request, Caller(), CancellationToken.None);

        AssertNotRoutedAndNoRoutingEvent();
        _created.Single(e => e.LogicalName == "sprk_communication")
            .GetAttributeValue<EntityReference>("sprk_regardingmatter")!.Id.Should().Be(matter);
    }

    [Fact]
    public async Task ASendNamingAThread_TakesTheRespondPathUnchanged_AndLogsNoRoutingEvent()
    {
        await RoutingService().SendAsync(MessageTo(ThirdAddress) with { ThreadId = DirectThread }, Caller(), CancellationToken.None);

        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailReads.Should().BeEmpty();
        _serviceLog.Entries.Should().NotContain(e => e.EventId.Name == SkippedCode);
        // The named thread (owned by the sender, shared with Read to the other party) gives its own pair, as before.
        var filing = TheFilingWrite();
        ((EntityReference)filing[ThreadLookup]).Id.Should().Be(DirectThread);
        Recipient(filing).Should().Be(Other);
    }

    [Fact]
    public async Task TheExternalThreadPost_IsNotRouted_AndLogsNoRoutingEvent()
    {
        var matter = Guid.NewGuid();
        _messageRow = MessageRow(sentBy: Sender, regardingMatter: matter);
        var author = new EA.CallerPrincipal
        {
            Plane = EA.CallerPrincipalPlane.Workforce,
            ContactId = Guid.NewGuid(),
            SystemUserId = Sender,
            Email = OtherAddress,
            ProjectAccess = Array.Empty<EA.CallerProjectAccess>(),
        };

        var result = await RoutingService().PostToRecordThreadAsync("sprk_matter", matter, "hello", author, CancellationToken.None);

        result.Should().NotBeNull();
        AssertNotRoutedAndNoRoutingEvent();
    }

    // ── Faults ─────────────────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AFaultResolvingTheRecipient_LogsAWarning_TakesTodaysPath_AndTheSendSucceeds()
    {
        var store = Directory();
        store.Setup(s => s.FindEnabledSystemUsersByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated 503 from Dataverse"));

        var response = await Service(new StubStrategy(ThreadKeyResolution.CreateNew), store.Object)
            .SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        response.CommunicationId.Should().Be(MessageId);
        AssertTodaysPathWithNoRecipient(skipReason: null);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == UnresolvedCode).Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task AnEmailReadThatDoesNotComplete_IsAResolveFault_NotNoMatch()
    {
        var store = Directory();
        store.Setup(s => s.FindEnabledSystemUsersByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EA.SystemUserEmailLookup.Failed);

        var response = await Service(new StubStrategy(ThreadKeyResolution.CreateNew), store.Object)
            .SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        response.CommunicationId.Should().Be(MessageId);
        AssertTodaysPathWithNoRecipient(skipReason: null);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == UnresolvedCode).Which.Level.Should().Be(LogLevel.Warning);
    }

    [Fact]
    public async Task AFaultFindingOrCreatingThePairThread_LogsAWarning_TakesTodaysPath_AndStampsNothing()
    {
        _directAccess
            .Setup(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated 429 from Dataverse"));

        var response = await Service(new StubStrategy(ThreadKeyResolution.CreateNew), Directory().Object)
            .SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        response.CommunicationId.Should().Be(MessageId);
        _created.Should().ContainSingle(e => e.LogicalName == "sprk_communicationthread", "the resolver created today's thread");
        TheFilingWrite().Should().ContainKey(ThreadLookup);
        AllFilingColumns().Should().NotContain(RecipientColumn);
        _serviceLog.Entries.Should().ContainSingle(e => e.EventId.Name == PairThreadFailedCode).Which.Level.Should().Be(LogLevel.Warning);
        _serviceLog.Entries.Should().NotContain(e => e.EventId.Name == SkippedCode);
    }

    /// <summary>
    /// The pair's thread is found or created, then the filing write faults: the explicit join's best-effort catch keeps the
    /// message as it was — no owner change, no recipient, no grant — and the send still succeeds. The empty Direct thread is
    /// left behind.
    /// </summary>
    [Fact]
    public async Task WhenTheFilingFaultsAfterThePairThreadIsFound_TheMessageKeepsItsOwner_AndGetsNoRecipient()
    {
        NoThreadStoredYet();
        _messageUpdateFault = fields => fields.ContainsKey(ThreadLookup) ? new HttpRequestException("simulated 500 from Dataverse") : null;

        var response = await RoutingService().SendAsync(MessageTo(OtherAddress), Caller(), CancellationToken.None);

        response.CommunicationId.Should().Be(MessageId);
        _pairThreadsCreated.Should().Be(1);
        _messageUpdates.Should().BeEmpty("the filing write did not land");
        _messageUpdateAttempts.SelectMany(u => u.Keys).Should().NotContain("ownerid").And.NotContain("owningteam");
        _entities.Verify(
            e => e.UpdateAsync("sprk_communication", MessageId, It.Is<Dictionary<string, object>>(f => f.ContainsKey("ownerid")), It.IsAny<CancellationToken>()),
            Times.Never);
        _directAccess.Verify(d => d.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()), Times.Never);
        _created.Should().NotContain(e => e.LogicalName == "sprk_communicationthread", "the resolver path does not run after the explicit join");
        _serviceLog.Entries.Should().Contain(e => e.Message.Contains("Explicit thread stamp failed") && e.Level == LogLevel.Warning);
    }

    // ── Rig ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>The routing service as composed: today's resolver (CREATE for a fresh ACS thread) plus the email read.</summary>
    private CommunicationService RoutingService()
    {
        PairThreadsAsStored();
        return Service(new StubStrategy(ThreadKeyResolution.CreateNew), Directory().Object);
    }

    private static SendCommunicationRequest MessageTo(params string[] to) => MessageRequest() with { To = to };

    /// <summary>No Direct thread exists for the pair yet: only the pair-thread double can put one in the store.</summary>
    private void NoThreadStoredYet()
    {
        _threadRow = null!;
        _threadShares = new();
    }

    private Mock<EA.IContactIdentityStore> Directory()
    {
        var store = new Mock<EA.IContactIdentityStore>(MockBehavior.Strict);
        store
            .Setup(s => s.FindEnabledSystemUsersByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string email, CancellationToken _) =>
            {
                _emailReads.Add(email);
                return EA.SystemUserEmailLookup.Of(_directory
                    .Where(u => !u.Disabled && string.Equals(u.Address, email, StringComparison.OrdinalIgnoreCase))
                    .Take(2)
                    .Select(u => new EA.SystemUserEmailRow(u.Id, false, u.Address, null))
                    .ToArray());
            });
        return store;
    }

    /// <summary>
    /// The pair's Direct thread as stored: the first call for a pair creates it, owned by that caller and shared with Read to
    /// the other party; a later call for the same pair, in either orientation, answers it unchanged.
    /// </summary>
    private void PairThreadsAsStored()
    {
        _directAccess
            .Setup(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid caller, Guid other, CancellationToken _) =>
            {
                _pairCalls.Add((caller, other));
                var stored = (_pairThreadOwner == caller && _pairThreadSharedTo == other)
                             || (_pairThreadOwner == other && _pairThreadSharedTo == caller);
                if (!stored)
                {
                    _pairThreadOwner = caller;
                    _pairThreadSharedTo = other;
                    _pairThreadsCreated++;
                    _threadRow = ThreadRow(ThreadTypeDirect, owner: caller);
                    _threadShares = new() { UserShare(other, ReadMask) };
                }

                return DirectThread;
            });
    }

    /// <summary>
    /// Today's path: no pair thread was asked for, the resolver created an application-owned Direct thread that is shared to
    /// nobody, the message was filed into it with no recipient, and (when <paramref name="skipReason"/> is given) ONE
    /// Information <c>pair_routing_skipped</c> event carries the reason and the message id, and no address.
    /// </summary>
    private void AssertTodaysPathWithNoRecipient(string? skipReason, bool granted = true)
    {
        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);

        var thread = _created.Should().ContainSingle(e => e.LogicalName == "sprk_communicationthread").Which;
        thread.Contains("ownerid").Should().BeFalse("today's create names no owner");
        _shares.Verify(
            s => s.GrantAccessAsync("sprk_communicationthreads", It.IsAny<Guid>(), It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var filing = TheFilingWrite();
        ((EntityReference)filing[ThreadLookup]).Id.Should().Be(DirectThread);
        filing.Should().NotContainKey(RecipientColumn);
        AllFilingColumns().Should().NotContain(RecipientColumn);

        _directAccess.Verify(
            d => d.GrantMessageAccessAsync(MessageId, It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()),
            granted ? Times.Once() : Times.Never());

        var skipped = _serviceLog.Entries.Where(e => e.EventId.Name == SkippedCode).ToList();
        if (skipReason is null)
        {
            skipped.Should().BeEmpty();
            return;
        }

        skipped.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Information);
        skipped[0].Message.Should().Contain($"({skipReason})").And.Contain(MessageId.ToString()).And.NotContain("@");
    }

    private void AssertNotRoutedAndNoRoutingEvent()
    {
        _directAccess.Verify(d => d.FindOrCreateDirectThreadAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _emailReads.Should().BeEmpty();
        _serviceLog.Entries.Should().NotContain(e => e.EventId.Name == SkippedCode || e.EventId.Name == UnresolvedCode);
        AllFilingColumns().Should().NotContain(RecipientColumn);
        _messageUpdates.Should().Contain(u => u.ContainsKey(ThreadLookup), "the message is still filed by the resolver");
    }
}
