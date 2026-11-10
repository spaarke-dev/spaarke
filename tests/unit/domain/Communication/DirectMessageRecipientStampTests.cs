using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Access;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Task 054 (field-mapping-server-write-path-r1, owner 2026-11-09, option A): a Direct message carries its own two
/// participants — <c>sprk_sentby</c> and <c>sprk_directrecipient</c> — written by the BFF in the update that files the message
/// into a Direct thread (the thread lookup and both participants in one write). The recipient is the Direct thread's other
/// participant: the thread's two-party list (its user owner and the systemusers holding a Read share on it) must be exactly
/// two distinct, non-empty systemusers, one of them the sender, both enabled and neither an application user; otherwise no
/// recipient is stamped. Non-Direct messages never get one.
/// </summary>
/// <remarks>
/// <para>Every case runs through a real entry point — <see cref="CommunicationService.SendAsync"/> for the send, respond and
/// new-thread paths, and <see cref="IncomingMessagingJobHandler"/> over a raw ACS event for the inbound join — with the real
/// <see cref="ThreadResolver"/>. Only the Dataverse boundary (<see cref="IGenericEntityService"/>), the POA share seam
/// (<see cref="IDataverseRecordShareService"/>), the ACS transmit and the caller's systemuser lookup are doubled.</para>
/// <para>The thread double is the thread as stored: its owner is the <c>ownerid</c> on the row (a thread the resolver
/// creates has none in its payload, so it is owned by the creating application user), and its shares are the share rows
/// the test seeds plus every share granted on it during the run. The default thread has the shape
/// <c>POST /threads/direct</c> creates: owned by one party, shared with Read to the other.</para>
/// </remarks>
public partial class DirectMessageRecipientStampTests
{
    private const string RecipientColumn = "sprk_directrecipient";
    private const string SenderColumn = "sprk_sentby";
    private const string ThreadLookup = "sprk_communicationthread";
    private const string UnmappedCode = "direct_message.recipient_unmapped";
    private const string NotTwoParticipantsCode = "direct_message.recipient_not_two_participants";
    private const string NotEligibleCode = "direct_message.recipient_participant_not_eligible";
    private const string UnreadCode = "direct_message.recipient_participants_unread";
    private const int ThreadTypeDirect = 100000001;
    private const int ThreadTypeRecordAnchored = 100000000;

    private const string SenderOid = "11111111-1111-1111-1111-111111111111";
    private const string OtherOid = "12121212-1212-1212-1212-121212121212";
    private const string SenderChatId = "8:acs:sender-identity";
    private const string AcsThreadId = "19:acs:thread_direct@thread.v2";

    private static readonly Guid Sender = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Other = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Third = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DirectThread = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid MessageId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    /// <summary>The BFF's application user: the owner of every thread the resolver creates (no <c>ownerid</c> in its payload).</summary>
    private static readonly Guid AppUser = Guid.Parse("77777777-7777-7777-7777-777777777777");
    private static readonly Guid ATeam = Guid.Parse("88888888-8888-8888-8888-888888888888");

    private const int ReadMask = 1;

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IDirectThreadAccessService> _directAccess = new();
    private readonly Mock<IDataverseRecordShareService> _shares = new();
    private readonly RecordingLogger<ThreadResolver> _resolverLog = new();
    private readonly RecordingLogger<CommunicationService> _serviceLog = new();
    private readonly List<Entity> _created = new();

    /// <summary>Every update the message received that landed.</summary>
    private readonly List<Dictionary<string, object>> _messageUpdates = new();

    /// <summary>Every update sent for the message, landed or faulted, in order.</summary>
    private readonly List<Dictionary<string, object>> _messageUpdateAttempts = new();

    /// <summary>The fault an update of the message answers with, by its columns; <c>null</c> lands it.</summary>
    private Func<Dictionary<string, object>, Exception?> _messageUpdateFault = static _ => null;

    /// <summary>When set, a thread the run creates is stored owned by the sender and shared with Read to the other party.</summary>
    private bool _createdThreadHoldsThePair;
    private readonly HashSet<Guid> _disabled = new();
    private Entity _messageRow = MessageRow(sentBy: Sender);

    // The thread as `POST /threads/direct` creates it: owned by the caller, shared with Read to the other party.
    private Entity _threadRow = ThreadRow(ThreadTypeDirect, owner: Sender);
    private List<DataversePrincipalAccess> _threadShares = new() { UserShare(Other, ReadMask) };

    public DirectMessageRecipientStampTests()
    {
        _entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) =>
            {
                _created.Add(entity);
                if (entity.LogicalName == "sprk_communicationthread")
                    StoreCreatedThread(entity);
            })
            .ReturnsAsync((Entity entity, CancellationToken _) =>
                entity.LogicalName == "sprk_communication" ? MessageId : DirectThread);

        _entities
            .Setup(e => e.RetrieveAsync("sprk_communication", MessageId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _messageRow);

        _entities
            .Setup(e => e.RetrieveAsync("sprk_communicationthread", DirectThread, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _threadRow);

        _entities
            .Setup(e => e.UpdateAsync("sprk_communication", MessageId, It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) =>
            {
                _messageUpdateAttempts.Add(fields);
                if (_messageUpdateFault(fields) is { } fault)
                    return Task.FromException(fault);
                _messageUpdates.Add(fields);
                return Task.CompletedTask;
            });

        // systemuser reads: the chat-identity map (sprk_communicationuserid) and the enabled check (isdisabled).
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => SystemUsers(q));

        // The POA seam over the thread's shares as stored. Both reads answer the same rows; a share granted on the thread
        // during the run becomes one of them.
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _threadShares.ToList());
        _shares
            .Setup(s => s.GetPrincipalAccessAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _threadShares.ToList());
        _shares
            .Setup(s => s.GrantAccessAsync(It.IsAny<string>(), DirectThread, It.IsAny<DataversePrincipalRef>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, DataversePrincipalRef, string, CancellationToken>((set, _, principal, _, _) =>
            {
                if (set == "sprk_communicationthreads")
                    _threadShares.Add(new DataversePrincipalAccess(principal, ReadMask, DateTimeOffset.UtcNow));
            })
            .Returns(Task.CompletedTask);
    }

    // ── Must fire: the Direct paths that can reach a thread holding a real pair ────────────────────────────────────────

    /// <summary>
    /// Send JOIN into a thread created by <c>POST /threads/direct</c> (owned by one party, shared with Read to the other):
    /// the resolver's join of an existing thread. Reachable only for such a thread; a thread the resolver itself creates
    /// holds no pair (see the CREATE case below).
    /// </summary>
    [Fact]
    public async Task Send_JoiningAThreadCreatedByThreadsDirect_FilesItWithTheOtherParticipantAsRecipient_AndTheSenderAsSentBy()
    {
        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        SentMessageCreate().GetAttributeValue<EntityReference>(SenderColumn)!.Id.Should().Be(Sender);
        var filing = TheFilingWrite();
        Recipient(filing).Should().Be(Other);
        filing.Should().NotContainKey(SenderColumn, "the row already carries its sender, which is kept as it is");
    }

    /// <summary>Respond: a send naming the Direct thread it answers in (the explicit respond-into-thread join).</summary>
    [Fact]
    public async Task Respond_IntoADirectThread_FilesItWithTheOtherParticipantAsRecipient_AndTheSenderAsSentBy()
    {
        await Service(resolver: null).SendAsync(MessageRequest() with { ThreadId = DirectThread }, Caller(), CancellationToken.None);

        SentMessageCreate().GetAttributeValue<EntityReference>(SenderColumn)!.Id.Should().Be(Sender);
        var filing = TheFilingWrite();
        Recipient(filing).Should().Be(Other);
    }

    /// <summary>
    /// Respond into a pair thread whose owner is the OTHER party: the sender is the shared participant, and the owner is the
    /// recipient.
    /// </summary>
    [Fact]
    public async Task Respond_IntoAPairThreadOwnedByTheOtherParty_StampsTheOwnerAsRecipient()
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: Other);
        _threadShares = new() { UserShare(Sender, ReadMask) };

        await Service(resolver: null).SendAsync(MessageRequest() with { ThreadId = DirectThread }, Caller(), CancellationToken.None);

        Recipient(TheFilingWrite()).Should().Be(Other);
    }

    /// <summary>
    /// Inbound JOIN into a thread created by <c>POST /threads/direct</c>: a chat message arriving into such a thread. Its row
    /// has no sender, so the sender's chat identity is mapped to a systemuser and stamped with the recipient, in the filing
    /// write. Reachable only for a thread of that shape (owned by one party, shared with Read to the other).
    /// </summary>
    [Fact]
    public async Task InboundJoin_IntoAThreadCreatedByThreadsDirect_StampsTheMappedSender_AndTheOtherParticipantAsRecipient()
    {
        _messageRow = MessageRow(sentBy: null);

        var outcome = await InboundHandler(Join(DirectThread)).ProcessAsync(InboundJob(SenderChatId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        _created.Single(e => e.LogicalName == "sprk_communication").Contains(SenderColumn)
            .Should().BeFalse("an inbound chat row is created without a sender; the Direct filing write stamps it");
        var filing = TheFilingWrite();
        ((EntityReference)filing[SenderColumn]).Id.Should().Be(Sender);
        Recipient(filing).Should().Be(Other);
    }

    // ── Must not fire ──────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// The first message of a new Direct thread: the resolver CREATEs a Direct thread (no regarding) for it. That thread is
    /// owned by the BFF application user and shared to nobody, so it holds no sender/recipient pair and no recipient is
    /// stamped (the sender stays as it is on the row).
    /// </summary>
    [Fact]
    public async Task FirstMessageOfAThreadTheResolverCreates_GetsNoRecipient_BecauseTheThreadIsAppOwnedAndUnshared()
    {
        await Service(new StubStrategy(ThreadKeyResolution.CreateNew)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        var thread = _created.Should().ContainSingle(e => e.LogicalName == "sprk_communicationthread").Which;
        thread.GetAttributeValue<OptionSetValue>("sprk_threadtype")!.Value.Should().Be(ThreadTypeDirect);
        thread.Contains("ownerid").Should().BeFalse("the resolver's create names no owner, so the creating application user owns it");
        SentMessageCreate().GetAttributeValue<EntityReference>(SenderColumn)!.Id.Should().Be(Sender);
        TheFilingWrite().Should().ContainKey(ThreadLookup, "the message is still filed into the new thread");
        AllFilingColumns().Should().NotContain(RecipientColumn);
    }

    /// <summary>A share row without Read (mask 0: access inherited from a related record) is not a participant.</summary>
    [Fact]
    public async Task AZeroMaskShareRow_IsNotAParticipant_AndGetsNoRecipient()
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: Sender);
        _threadShares = new() { UserShare(Other, 0) };

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle("the message is still filed into the thread");
        AllFilingColumns().Should().NotContain(RecipientColumn);
        _resolverLog.Entries.Should().Contain(e => e.EventId.Name == NotTwoParticipantsCode && e.Level == LogLevel.Information);
    }

    /// <summary>A thread owned by an application user and shared to the sender: an application user is never a participant.</summary>
    [Fact]
    public async Task AThreadOwnedByAnApplicationUser_SharedToTheSender_GetsNoRecipient()
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: AppUser);
        _threadShares = new() { UserShare(Sender, ReadMask) };

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle("the message is still filed into the thread");
        AllFilingColumns().Should().NotContain(RecipientColumn);
        _resolverLog.Entries.Should().Contain(e => e.EventId.Name == NotEligibleCode && e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task ARecordThread_GetsNoRecipient()
    {
        _threadRow = ThreadRow(ThreadTypeRecordAnchored, owner: Sender, matter: Guid.NewGuid());

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        AllFilingColumns().Should().NotContain(RecipientColumn);
        _shares.Verify(s => s.GetPrincipalAccessOrThrowAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADirectTypedThreadCarryingATypedLookup_IsARecordThread_AndGetsNoRecipient()
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: Sender, matter: Guid.NewGuid());

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        AllFilingColumns().Should().NotContain(RecipientColumn);
    }

    [Fact]
    public async Task ARecordFiledMessage_InADirectThread_GetsNoRecipient()
    {
        _messageRow = MessageRow(sentBy: Sender, regardingMatter: Guid.NewGuid());

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        AllFilingColumns().Should().NotContain(RecipientColumn).And.NotContain(SenderColumn);
    }

    [Fact]
    public async Task AnEmail_InADirectThread_GetsNoRecipient()
    {
        _messageRow = MessageRow(sentBy: Sender, type: CommunicationType.Email);

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        AllFilingColumns().Should().NotContain(RecipientColumn);
    }

    [Fact]
    public async Task ThePerUserMasterThread_GetsNoRecipient()
    {
        // No groupable key and no regarding: the per-user master (Tier 3), which grants nobody.
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        _messageRow = MessageRow(sentBy: Sender, owner: Sender);

        await Service(new StubStrategy(ThreadKeyResolution.Skip)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().NotBeEmpty("the message is filed into the master thread");
        AllFilingColumns().Should().NotContain(RecipientColumn);
    }

    /// <summary>
    /// (user owner, or <c>null</c> for a team owner; systemusers shared with Read; teams shared with Read) — none of them holds
    /// exactly two distinct participants including the sender.
    /// </summary>
    public static TheoryData<Guid?, Guid[], Guid[]> NotExactlyTwoParticipantsWithTheSender => new()
    {
        { Sender, Array.Empty<Guid>(), Array.Empty<Guid>() },          // one participant: the owner, shared to nobody
        { Sender, new[] { Other, Third }, Array.Empty<Guid>() },       // three participants
        { Sender, new[] { Sender }, Array.Empty<Guid>() },             // the owner shared to itself: one distinct id
        { Other, new[] { Third }, Array.Empty<Guid>() },               // two, but not the sender
        { Sender, new[] { Guid.Empty }, Array.Empty<Guid>() },         // an empty id
        { null, new[] { Sender }, Array.Empty<Guid>() },               // a team owner is not a participant
        { Sender, Array.Empty<Guid>(), new[] { ATeam } },              // a team share is not a participant
        { null, Array.Empty<Guid>(), Array.Empty<Guid>() },            // none
    };

    [Theory]
    [MemberData(nameof(NotExactlyTwoParticipantsWithTheSender))]
    public async Task ADirectThreadWithoutExactlyTwoDistinctParticipantsIncludingTheSender_GetsNoRecipient(
        Guid? userOwner, Guid[] userShares, Guid[] teamShares)
    {
        _threadRow = ThreadRow(ThreadTypeDirect, owner: userOwner);
        _threadShares = userShares.Select(u => UserShare(u, ReadMask))
            .Concat(teamShares.Select(t => new DataversePrincipalAccess(DataversePrincipalRef.Team(t), ReadMask, DateTimeOffset.UtcNow)))
            .ToList();

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle("the message is still filed into the thread");
        AllFilingColumns().Should().NotContain(RecipientColumn);
        _resolverLog.Entries.Should().Contain(e => e.EventId.Name == NotTwoParticipantsCode && e.Level == LogLevel.Information);
    }

    [Fact]
    public async Task ADisabledParticipant_GetsNoRecipient()
    {
        _disabled.Add(Other);

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle();
        AllFilingColumns().Should().NotContain(RecipientColumn);
        _resolverLog.Entries.Should().Contain(e => e.EventId.Name == NotEligibleCode && e.Level == LogLevel.Information);
    }

    /// <summary>
    /// The share read faults (a throttle or a server error): the strict read throws, so nothing is stamped, one Warning
    /// with its own code is logged, and the message is still filed into the thread. The soft read's empty answer is never
    /// taken as "no shares".
    /// </summary>
    [Fact]
    public async Task AShareReadFault_StampsNothing_LogsTheUnreadWarning_AndStillFilesTheMessage()
    {
        _shares
            .Setup(s => s.GetPrincipalAccessOrThrowAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("simulated 429 from Dataverse"));
        _shares
            .Setup(s => s.GetPrincipalAccessAsync("sprk_communicationthread", DirectThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<DataversePrincipalAccess>()); // what the soft read answers on the same fault

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle().Which.Should().ContainKey(ThreadLookup);
        AllFilingColumns().Should().NotContain(RecipientColumn);
        var warnings = _resolverLog.Entries.Where(e => e.EventId.Name == UnreadCode).ToList();
        warnings.Should().ContainSingle().Which.Level.Should().Be(LogLevel.Warning);
        _resolverLog.Entries.Should().NotContain(e => e.EventId.Name == NotTwoParticipantsCode);
    }

    [Fact]
    public async Task AnUnmappableInboundSender_GetsNoRecipient_LogsOneErrorWithTheCode_AndTheMessageIsStillCreated()
    {
        _messageRow = MessageRow(sentBy: null);

        var outcome = await InboundHandler(Join(DirectThread))
            .ProcessAsync(InboundJob("8:acs:identity-nobody-holds"), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed, "the message is never failed or dropped");
        _created.Should().ContainSingle(e => e.LogicalName == "sprk_communication");
        TheFilingWrite().Should().ContainKey(ThreadLookup, "the message is still filed into the thread");
        AllFilingColumns().Should().NotContain(RecipientColumn).And.NotContain(SenderColumn);

        var errors = _resolverLog.Entries.Where(e => e.Level == LogLevel.Error).ToList();
        errors.Should().ContainSingle();
        errors[0].EventId.Name.Should().Be(UnmappedCode);
        errors[0].Message.Should().Contain(UnmappedCode).And.Contain(MessageId.ToString());
    }

    [Fact]
    public async Task AnInboundMessage_IntoARecordThread_LogsNoErrorAndStampsNothing()
    {
        _messageRow = MessageRow(sentBy: null);
        _threadRow = ThreadRow(ThreadTypeRecordAnchored, matter: Guid.NewGuid());

        await InboundHandler(Join(DirectThread)).ProcessAsync(InboundJob("8:acs:identity-nobody-holds"), CancellationToken.None);

        AllFilingColumns().Should().NotContain(RecipientColumn).And.NotContain(SenderColumn);
        _resolverLog.Entries.Should().NotContain(e => e.EventId.Name == UnmappedCode);
    }

    // ── The rule on its own ────────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OtherParticipant_IsTheParticipantThatIsNotTheSender_OnlyForExactlyTwoDistinctNonEmptyIds()
    {
        DirectMessageRecipient.OtherParticipant(Sender, new[] { Sender, Other }).Should().Be(Other);
        DirectMessageRecipient.OtherParticipant(Sender, new[] { Other, Sender }).Should().Be(Other);
        DirectMessageRecipient.OtherParticipant(Guid.Empty, new[] { Guid.Empty, Other }).Should().BeNull();
        DirectMessageRecipient.OtherParticipant(Sender, null).Should().BeNull();
        DirectMessageRecipient.OtherParticipant(Sender, new[] { Sender, Sender }).Should().BeNull();
        DirectMessageRecipient.OtherParticipant(Sender, new[] { Other, Third }).Should().BeNull();
        DirectMessageRecipient.OtherParticipant(Sender, new[] { Sender, Other, Third }).Should().BeNull();
    }

    // ── Rig ────────────────────────────────────────────────────────────────────────────────────────────────────────────

    private static StubStrategy Join(Guid threadId) => new(ThreadKeyResolution.Join(threadId));

    private ThreadResolver Resolver(IThreadKeyStrategy strategy) =>
        new(new[] { strategy }, _entities.Object, new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            _resolverLog, _directAccess.Object, recordShares: _shares.Object);

    /// <param name="identities">The internal-user email read the outbound Direct pair routing maps with (task 054a piece 2b);
    /// <c>null</c> routes nothing.</param>
    /// <param name="directAccess"><c>false</c> composes the service without the Direct thread access service.</param>
    private CommunicationService Service(
        IThreadKeyStrategy? resolver,
        Sprk.Bff.Api.Infrastructure.ExternalAccess.IContactIdentityStore? identities = null,
        bool directAccess = true)
    {
        var options = new CommunicationOptions { ApprovedSenders = Array.Empty<ApprovedSenderConfig>(), DefaultMailbox = "noreply@contoso.com" };
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            new CommunicationAccountService(
                Mock.Of<IDataverseService>(), Mock.Of<IDataverseService>(), Mock.Of<IDistributedCache>(),
                NullLogger<CommunicationAccountService>.Instance),
            Mock.Of<IDistributedCache>(),
            NullLogger<ApprovedSenderValidator>.Instance);
        var dispatcher = new CommunicationChannelDispatcher(
            new ICommunicationChannelSender[] { new CannedMessagingSender() },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(NullLogger<EmlGenerationService>.Instance)) });
        var comm = new Mock<ICommunicationDataverseService>();
        comm.Setup(c => c.QuerySystemUserByAzureAdOidAsync(SenderOid, It.IsAny<CancellationToken>())).ReturnsAsync(Sender);
        comm.Setup(c => c.QuerySystemUserByAzureAdOidAsync(OtherOid, It.IsAny<CancellationToken>())).ReturnsAsync(Other);

        return new CommunicationService(
            dispatcher,
            senderValidator,
            comm.Object,
            _entities.Object,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService — not reached on the message path
            null!, // JobSubmissionService — not reached on the message path
            Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(options),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            _serviceLog,
            resolver is null ? null : Resolver(resolver),
            scopeFactory: null,
            directThreadAccess: directAccess ? _directAccess.Object : null,
            recordShares: _shares.Object,
            contactIdentities: identities);
    }

    private IncomingMessagingJobHandler InboundHandler(IThreadKeyStrategy strategy)
    {
        var ingestor = new MessagingIngestor(
            _entities.Object, Mock.Of<ICommunicationEnrichmentService>(), NullLogger<MessagingIngestor>.Instance,
            Resolver(strategy), _directAccess.Object);
        var dispatcher = new CommunicationChannelDispatcher(
            senders: Enumerable.Empty<ICommunicationChannelSender>(),
            archivers: Enumerable.Empty<ICommunicationArchiver>(),
            ingestors: new ICommunicationChannelIngestor[] { ingestor });
        return new IncomingMessagingJobHandler(
            new AcsEventNormalizer(), dispatcher, new InMemoryIdempotency(), NullLogger<IncomingMessagingJobHandler>.Instance);
    }

    private static SendCommunicationRequest MessageRequest() => new()
    {
        To = new[] { "someone@contoso.com" },
        Subject = "Direct",
        Body = "hello",
        BodyFormat = BodyFormat.PlainText,
        CommunicationType = CommunicationType.Message,
    };

    private static HttpContext Caller() => Caller(SenderOid, "sender@contoso.com");

    private static HttpContext Caller(string oid, string username) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", oid), new Claim("preferred_username", username) }, "test")),
    };

    private static Entity MessageRow(
        Guid? sentBy, CommunicationType type = CommunicationType.Message, Guid? regardingMatter = null, Guid? owner = null)
    {
        var row = new Entity("sprk_communication", MessageId)
        {
            ["sprk_communicationtype"] = new OptionSetValue((int)type),
        };
        if (sentBy is { } s)
            row[SenderColumn] = new EntityReference("systemuser", s);
        if (regardingMatter is { } m)
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
        if (owner is { } o)
            row["ownerid"] = new EntityReference("systemuser", o);
        return row;
    }

    /// <summary>A thread row as stored. <paramref name="owner"/> <c>null</c>: owned by a team.</summary>
    private static Entity ThreadRow(int threadType, Guid? owner = null, Guid? matter = null)
    {
        var row = new Entity("sprk_communicationthread", DirectThread)
        {
            ["sprk_threadtype"] = new OptionSetValue(threadType),
            ["sprk_privacystate"] = new OptionSetValue(100000000),
            ["ownerid"] = owner is { } o ? new EntityReference("systemuser", o) : new EntityReference("team", ATeam),
        };
        if (matter is { } m)
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
        return row;
    }

    /// <summary>
    /// A thread the run creates becomes the stored thread: its own columns, owned by the identity that created it (the
    /// application user, unless the payload names an owner), with no share rows.
    /// </summary>
    private void StoreCreatedThread(Entity created)
    {
        var stored = new Entity("sprk_communicationthread", DirectThread);
        foreach (var attribute in created.Attributes)
            stored[attribute.Key] = attribute.Value;
        if (!stored.Contains("ownerid"))
            stored["ownerid"] = new EntityReference("systemuser", _createdThreadHoldsThePair ? Sender : AppUser);
        _threadRow = stored;
        _threadShares = _createdThreadHoldsThePair ? new() { UserShare(Other, ReadMask) } : new();
    }

    private static DataversePrincipalAccess UserShare(Guid systemUserId, int mask) =>
        new(DataversePrincipalRef.User(systemUserId), mask, DateTimeOffset.UtcNow);

    private EntityCollection SystemUsers(QueryExpression query)
    {
        var identity = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_communicationuserid");
        if (identity is not null)
        {
            return identity.Values.Single() as string == SenderChatId
                ? new EntityCollection(new List<Entity> { new("systemuser", Sender) })
                : new EntityCollection();
        }

        // The application user is enabled, and carries an applicationid; people carry none.
        var ids = query.Criteria.Conditions.Single(c => c.AttributeName == "systemuserid").Values.Cast<Guid>();
        return new EntityCollection(ids
            .Select(id =>
            {
                var user = new Entity("systemuser", id) { ["isdisabled"] = _disabled.Contains(id) };
                if (id == AppUser)
                    user["applicationid"] = Guid.Parse("99999999-9999-9999-9999-999999999999");
                return user;
            })
            .ToList());
    }

    private Entity SentMessageCreate() => _created.Single(e => e.LogicalName == "sprk_communication");

    private Dictionary<string, object> TheFilingWrite() =>
        _messageUpdates.Should().ContainSingle(u => u.ContainsKey(ThreadLookup)).Which;

    private IEnumerable<string> AllFilingColumns() => _messageUpdates.SelectMany(u => u.Keys);

    private static Guid? Recipient(IReadOnlyDictionary<string, object> filing) =>
        filing.TryGetValue(RecipientColumn, out var value) ? ((EntityReference)value).Id : null;

    private static JobContract InboundJob(string senderRawId)
    {
        const string eventType = "Microsoft.Communication.ChatMessageReceivedInThread";
        var evt = new Dictionary<string, object?>
        {
            ["id"] = "evt-inbound",
            ["eventType"] = eventType,
            ["subject"] = $"thread/{AcsThreadId}",
            ["eventTime"] = "2026-11-09T12:00:02Z",
            ["data"] = new Dictionary<string, object?>
            {
                ["messageId"] = "1790000000001",
                ["threadId"] = AcsThreadId,
                ["messageBody"] = "hello back",
                ["messageType"] = "Text",
                ["senderDisplayName"] = "Display Name",
                ["senderCommunicationIdentifier"] = new Dictionary<string, object?> { ["rawId"] = senderRawId },
                ["composeTime"] = "2026-11-09T12:00:01Z",
            },
        };
        var payload = new IncomingMessagingJobPayload
        {
            AcsMessageId = "1790000000001",
            AcsThreadId = AcsThreadId,
            EventType = eventType,
            EventId = "evt-inbound",
            EventTime = DateTimeOffset.Parse("2026-11-09T12:00:02Z"),
            RawEvent = JsonSerializer.SerializeToElement(evt),
        };
        return new JobContract
        {
            JobType = IncomingMessagingJobHandler.JobTypeName,
            SubjectId = "1790000000001",
            CorrelationId = "corr-054-inbound",
            IdempotencyKey = $"Messaging:{eventType}:1790000000001",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonSerializer.SerializeToDocument(payload),
        };
    }

    private sealed class StubStrategy(ThreadKeyResolution resolution) : IThreadKeyStrategy
    {
        public CommunicationType SupportedType => CommunicationType.Message;

        public Task<ThreadKeyResolution> ResolveAsync(ThreadResolutionRequest request, CancellationToken ct) => Task.FromResult(resolution);

        public Task OnThreadCreatedAsync(Guid threadId, ThreadResolutionRequest request, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CannedMessagingSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Message;

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChannelSendResult
            {
                FromAddress = SenderChatId,
                ProviderMessageId = "1790000000000",
                ProviderThreadId = AcsThreadId,
            });
    }

    private sealed class InMemoryIdempotency : IIdempotencyService
    {
        private readonly HashSet<string> _processed = new();

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken ct = default) => Task.FromResult(_processed.Contains(eventId));

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken ct = default)
        {
            _processed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken ct = default) =>
            Task.FromResult(true);

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, eventId, formatter(state, exception)));
    }
}
