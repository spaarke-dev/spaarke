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
/// participant: the thread's two-party list must be exactly two distinct, non-empty, enabled systemusers, one of them the
/// sender; otherwise no recipient is stamped. Non-Direct messages never get one.
/// </summary>
/// <remarks>
/// Every case runs through a real entry point — <see cref="CommunicationService.SendAsync"/> for the send, respond and
/// new-thread paths, and <see cref="IncomingMessagingJobHandler"/> over a raw ACS event for the inbound join — with the real
/// <see cref="ThreadResolver"/>. Only the Dataverse boundary (<see cref="IGenericEntityService"/>), the Direct thread's
/// two-party list (<see cref="IDirectThreadAccessService.GetParticipantSystemUserIdsAsync"/>), the ACS transmit and the
/// caller's systemuser lookup are doubled.
/// </remarks>
public class DirectMessageRecipientStampTests
{
    private const string RecipientColumn = "sprk_directrecipient";
    private const string SenderColumn = "sprk_sentby";
    private const string ThreadLookup = "sprk_communicationthread";
    private const string UnmappedCode = "direct_message.recipient_unmapped";
    private const int ThreadTypeDirect = 100000001;
    private const int ThreadTypeRecordAnchored = 100000000;

    private const string SenderOid = "11111111-1111-1111-1111-111111111111";
    private const string SenderChatId = "8:acs:sender-identity";
    private const string AcsThreadId = "19:acs:thread_direct@thread.v2";

    private static readonly Guid Sender = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid Other = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Third = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid DirectThread = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid MessageId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private readonly Mock<IGenericEntityService> _entities = new();
    private readonly Mock<IDirectThreadAccessService> _directAccess = new();
    private readonly RecordingLogger<ThreadResolver> _resolverLog = new();
    private readonly List<Entity> _created = new();
    private readonly List<Dictionary<string, object>> _messageUpdates = new();
    private readonly HashSet<Guid> _disabled = new();
    private Entity _messageRow = MessageRow(sentBy: Sender);
    private Entity _threadRow = ThreadRow(ThreadTypeDirect);

    public DirectMessageRecipientStampTests()
    {
        _entities
            .Setup(e => e.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((entity, _) => _created.Add(entity))
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
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) => _messageUpdates.Add(fields))
            .Returns(Task.CompletedTask);

        // systemuser reads: the chat-identity map (sprk_communicationuserid) and the enabled check (isdisabled).
        _entities
            .Setup(e => e.RetrieveMultipleAsync(It.Is<QueryExpression>(q => q.EntityName == "systemuser"), It.IsAny<CancellationToken>()))
            .ReturnsAsync((QueryExpression q, CancellationToken _) => SystemUsers(q));

        _directAccess
            .Setup(a => a.GetParticipantSystemUserIdsAsync(DirectThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Sender, Other });
    }

    // ── Must fire: one per Direct path ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Send: a message whose ACS thread maps to an existing Direct thread (the resolver's join).</summary>
    [Fact]
    public async Task Send_JoiningADirectThread_FilesItWithTheOtherParticipantAsRecipient_AndTheSenderAsSentBy()
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

    /// <summary>The first message of a new Direct thread: the resolver creates a Direct thread (no regarding) for it.</summary>
    [Fact]
    public async Task FirstMessageOfANewDirectThread_IsFiledWithTheOtherParticipantAsRecipient()
    {
        await Service(new StubStrategy(ThreadKeyResolution.CreateNew)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _created.Should().ContainSingle(e => e.LogicalName == "sprk_communicationthread")
            .Which.GetAttributeValue<OptionSetValue>("sprk_threadtype")!.Value.Should().Be(ThreadTypeDirect);
        SentMessageCreate().GetAttributeValue<EntityReference>(SenderColumn)!.Id.Should().Be(Sender);
        Recipient(TheFilingWrite()).Should().Be(Other);
    }

    /// <summary>
    /// Inbound join: a chat message arriving into a Direct thread. Its row has no sender, so the sender's chat identity is
    /// mapped to a systemuser and stamped with the recipient, in the filing write.
    /// </summary>
    [Fact]
    public async Task InboundJoin_IntoADirectThread_StampsTheMappedSender_AndTheOtherParticipantAsRecipient()
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

    [Fact]
    public async Task ARecordThread_GetsNoRecipient()
    {
        _threadRow = ThreadRow(ThreadTypeRecordAnchored, matter: Guid.NewGuid());

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        AllFilingColumns().Should().NotContain(RecipientColumn);
        _directAccess.Verify(a => a.GetParticipantSystemUserIdsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ADirectTypedThreadCarryingATypedLookup_IsARecordThread_AndGetsNoRecipient()
    {
        _threadRow = ThreadRow(ThreadTypeDirect, matter: Guid.NewGuid());

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

    public static TheoryData<Guid[]> NotExactlyTwoParticipantsWithTheSender => new()
    {
        new[] { Sender },                 // one participant
        new[] { Sender, Other, Third },   // three participants
        new[] { Sender, Sender },         // two equal ids
        new[] { Other, Third },           // two, but not the sender
        new[] { Sender, Guid.Empty },     // an empty id
        Array.Empty<Guid>(),              // none
    };

    [Theory]
    [MemberData(nameof(NotExactlyTwoParticipantsWithTheSender))]
    public async Task ADirectThreadWithoutExactlyTwoDistinctParticipantsIncludingTheSender_GetsNoRecipient(Guid[] participants)
    {
        _directAccess
            .Setup(a => a.GetParticipantSystemUserIdsAsync(DirectThread, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participants);

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle("the message is still filed into the thread");
        AllFilingColumns().Should().NotContain(RecipientColumn);
    }

    [Fact]
    public async Task ADisabledParticipant_GetsNoRecipient()
    {
        _disabled.Add(Other);

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle();
        AllFilingColumns().Should().NotContain(RecipientColumn);
    }

    [Fact]
    public async Task AParticipantReadFault_StampsNothing_AndStillFilesTheMessage()
    {
        _directAccess
            .Setup(a => a.GetParticipantSystemUserIdsAsync(DirectThread, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated Dataverse fault"));

        await Service(Join(DirectThread)).SendAsync(MessageRequest(), Caller(), CancellationToken.None);

        _messageUpdates.Should().ContainSingle().Which.Should().ContainKey(ThreadLookup);
        AllFilingColumns().Should().NotContain(RecipientColumn);
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
            _resolverLog, _directAccess.Object);

    private CommunicationService Service(IThreadKeyStrategy? resolver)
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
            NullLogger<CommunicationService>.Instance,
            resolver is null ? null : Resolver(resolver),
            scopeFactory: null,
            directThreadAccess: _directAccess.Object);
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

    private static HttpContext Caller() => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(
            new[] { new Claim("oid", SenderOid), new Claim("preferred_username", "sender@contoso.com") }, "test")),
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

    private static Entity ThreadRow(int threadType, Guid? matter = null)
    {
        var row = new Entity("sprk_communicationthread", DirectThread)
        {
            ["sprk_threadtype"] = new OptionSetValue(threadType),
            ["sprk_privacystate"] = new OptionSetValue(100000000),
        };
        if (matter is { } m)
            row["sprk_regardingmatter"] = new EntityReference("sprk_matter", m);
        return row;
    }

    private EntityCollection SystemUsers(QueryExpression query)
    {
        var identity = query.Criteria.Conditions.FirstOrDefault(c => c.AttributeName == "sprk_communicationuserid");
        if (identity is not null)
        {
            return identity.Values.Single() as string == SenderChatId
                ? new EntityCollection(new List<Entity> { new("systemuser", Sender) })
                : new EntityCollection();
        }

        var ids = query.Criteria.Conditions.Single(c => c.AttributeName == "systemuserid").Values.Cast<Guid>();
        return new EntityCollection(ids
            .Select(id => new Entity("systemuser", id) { ["isdisabled"] = _disabled.Contains(id) })
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
