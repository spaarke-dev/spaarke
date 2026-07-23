using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Task-080 composed vertical-slice seam (ADR-038 "DoD for dispatch-spine changes") for the FRESH INBOUND
/// path: a raw ACS <c>ChatMessageReceivedInThread</c> Event Grid job → the REAL
/// <see cref="AcsEventNormalizer"/> → the REAL <see cref="CommunicationChannelDispatcher"/> → the REAL
/// <see cref="MessagingIngestor"/> → the REAL <see cref="ThreadResolver"/> (with a real
/// <see cref="MessagingThreadKeyStrategy"/>) → persist. Only the Dataverse boundary
/// (<see cref="IGenericEntityService"/>) and enrichment are doubled.
///
/// <para><b>Why this is additive, not a clone.</b>
/// <see cref="Sprk.Bff.Api.Tests.Services.Communication.IncomingMessagingJobHandlerTests"/> already composes
/// job → normalizer → dispatcher → ingestor, but its harness constructs <see cref="MessagingIngestor"/>
/// WITHOUT a thread resolver (the resolver argument is omitted, so it is <c>null</c> and thread resolution
/// never runs) — it proves idempotency/DLQ, not thread assignment.
/// <see cref="Sprk.Bff.Api.Tests.Seam.Communication.ThreadResolverSeamTests"/> composes ingestor → resolver
/// (its <c>MessagingIngestor_InboundChatMessage_PersistsThenStampsResolvedThread</c> test), but it hand-builds
/// a <see cref="NormalizedMessage"/> directly, skipping <see cref="AcsEventNormalizer"/> entirely. Neither
/// existing test exercises the full chain — raw ACS event → normalizer → ingest → resolve → persisted +
/// thread-stamped record — which is exactly how <see cref="IncomingMessagingJobHandler"/> is wired in
/// production DI (<c>CommunicationModule</c> injects the real <c>IThreadResolver</c> into
/// <c>MessagingIngestor</c>). This file closes that composition gap with two scenarios (fresh thread vs.
/// join existing thread) rather than re-testing normalizer mapping or resolver internals in isolation
/// (already covered by <c>AcsEventNormalizerTests</c> and <c>ThreadResolverSeamTests</c>).</para>
/// </summary>
public class MessagingSpineSeamTests
{
    private const int ThreadTypeDirect = 100000001;
    private const int MessageChannel = 100000004;
    private const string AcsMessageId = "1784231661500";

    /// <summary>Honest in-memory <see cref="IIdempotencyService"/> — mirrors the sibling job-handler tests.</summary>
    private sealed class FakeIdempotencyService : IIdempotencyService
    {
        private readonly HashSet<string> _processed = new();
        private readonly HashSet<string> _locks = new();

        public Task<bool> IsEventProcessedAsync(string eventId, CancellationToken ct = default)
            => Task.FromResult(_processed.Contains(eventId));

        public Task MarkEventAsProcessedAsync(string eventId, TimeSpan? expiration = null, CancellationToken ct = default)
        {
            _processed.Add(eventId);
            return Task.CompletedTask;
        }

        public Task<bool> TryAcquireProcessingLockAsync(string eventId, TimeSpan? lockDuration = null, CancellationToken ct = default)
            => Task.FromResult(_locks.Add(eventId));

        public Task ReleaseProcessingLockAsync(string eventId, CancellationToken ct = default)
        {
            _locks.Remove(eventId);
            return Task.CompletedTask;
        }
    }

    private readonly Mock<IGenericEntityService> _entity = new(MockBehavior.Strict);

    private IncomingMessagingJobHandler CreateHandler(IThreadResolver resolver)
    {
        var ingestor = new MessagingIngestor(
            _entity.Object,
            Mock.Of<ICommunicationEnrichmentService>(MockBehavior.Loose),
            NullLogger<MessagingIngestor>.Instance,
            resolver);

        var dispatcher = new CommunicationChannelDispatcher(
            senders: Enumerable.Empty<ICommunicationChannelSender>(),
            archivers: Enumerable.Empty<ICommunicationArchiver>(),
            ingestors: new ICommunicationChannelIngestor[] { ingestor });

        return new IncomingMessagingJobHandler(
            new AcsEventNormalizer(), dispatcher, new FakeIdempotencyService(), NullLogger<IncomingMessagingJobHandler>.Instance);
    }

    private static ThreadResolver CreateRealResolver(Mock<IGenericEntityService> entity) =>
        new(
            new IThreadKeyStrategy[] { new MessagingThreadKeyStrategy(entity.Object) },
            entity.Object,
            NullLogger<ThreadResolver>.Instance);

    private static DataverseEntity Communication(Guid id) => new("sprk_communication") { Id = id };

    private List<Dictionary<string, object>> CaptureUpdates()
    {
        var captured = new List<Dictionary<string, object>>();
        _entity.Setup(s => s.UpdateAsync("sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => captured.Add(f))
            .Returns(Task.CompletedTask);
        return captured;
    }

    private static JobContract RawInboundJob(string acsThreadId, string messageId = AcsMessageId)
    {
        const string eventType = "Microsoft.Communication.ChatMessageReceivedInThread";
        var data = new Dictionary<string, object?>
        {
            ["messageId"] = messageId,
            ["threadId"] = acsThreadId,
            ["messageBody"] = "Kickoff — see you at 3pm",
            ["messageType"] = "Text",
            ["senderDisplayName"] = "Alice Attorney",
            ["senderCommunicationIdentifier"] = new Dictionary<string, object?> { ["rawId"] = "8:acs:res_alice" },
            ["composeTime"] = "2026-07-17T15:00:00Z",
        };
        var evt = new Dictionary<string, object?>
        {
            ["id"] = "evt-spine-1",
            ["eventType"] = eventType,
            ["subject"] = $"thread/{acsThreadId}",
            ["eventTime"] = "2026-07-17T15:00:01Z",
            ["data"] = data,
        };

        var payload = new IncomingMessagingJobPayload
        {
            AcsMessageId = messageId,
            AcsThreadId = acsThreadId,
            EventType = eventType,
            EventId = "evt-spine-1",
            EventTime = DateTimeOffset.Parse("2026-07-17T15:00:01Z"),
            RawEvent = JsonSerializer.SerializeToElement(evt),
        };

        return new JobContract
        {
            JobType = IncomingMessagingJobHandler.JobTypeName,
            SubjectId = messageId,
            CorrelationId = "corr-080-spine",
            IdempotencyKey = $"Messaging:{eventType}:{messageId}",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonSerializer.SerializeToDocument(payload),
        };
    }

    [Fact]
    public async Task ProcessAsync_FreshChatMessage_NormalizesIngestsResolvesAndPersistsThreadStampedRecord()
    {
        const string acsThreadId = "19:acs:thread_spine_fresh@thread.v2";
        var commId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var channelRefId = Guid.NewGuid();

        DataverseEntity? persistedCommunication = null;
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => persistedCommunication = e)
            .ReturnsAsync(commId);
        // No channel-ref yet for this ACS thread ⇒ fresh create.
        _entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        // Unassociated chat ⇒ anchor read yields no regarding (Direct thread).
        _entity.Setup(s => s.RetrieveAsync("sprk_communication", commId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Communication(commId));
        DataverseEntity? createdThread = null;
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => createdThread = e)
            .ReturnsAsync(threadId);
        DataverseEntity? createdChannelRef = null;
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => createdChannelRef = e)
            .ReturnsAsync(channelRefId);
        var updates = CaptureUpdates();

        var handler = CreateHandler(CreateRealResolver(_entity));

        var outcome = await handler.ProcessAsync(RawInboundJob(acsThreadId), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);

        // Persisted EXACTLY ONE sprk_communication, carrying the raw event's ids/body through the normalizer.
        _entity.Verify(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communication"), It.IsAny<CancellationToken>()), Times.Once);
        persistedCommunication.Should().NotBeNull();
        persistedCommunication!.GetAttributeValue<string>("sprk_acsmessageid").Should().Be(AcsMessageId);
        persistedCommunication.GetAttributeValue<string>("sprk_acsthreadid").Should().Be(acsThreadId);
        persistedCommunication.GetAttributeValue<string>("sprk_body").Should().Be("Kickoff — see you at 3pm");
        persistedCommunication.GetAttributeValue<string>("sprk_from").Should().Be("Alice Attorney");

        // Resolver created a fresh Direct thread + channel-ref row, keyed to the ACS thread id.
        createdThread.Should().NotBeNull();
        createdThread!.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeDirect);
        createdChannelRef.Should().NotBeNull();
        createdChannelRef!.GetAttributeValue<string>("sprk_externalref").Should().Be(acsThreadId);
        createdChannelRef.GetAttributeValue<OptionSetValue>("sprk_channeltype").Value.Should().Be(MessageChannel);
        createdChannelRef.GetAttributeValue<EntityReference>("sprk_thread").Id.Should().Be(threadId);

        // The persisted record was stamped with the newly created thread — the end-to-end contract.
        updates.Should().ContainSingle();
        updates[0]["sprk_communicationthread"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(threadId);
    }

    [Fact]
    public async Task ProcessAsync_ChatMessageOnKnownAcsThread_JoinsExistingThreadWithoutCreatingDuplicate()
    {
        const string acsThreadId = "19:acs:thread_spine_join@thread.v2";
        var commId = Guid.NewGuid();
        var existingThreadId = Guid.NewGuid();

        DataverseEntity? persistedCommunication = null;
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => persistedCommunication = e)
            .ReturnsAsync(commId);
        // A channel-ref already maps this ACS thread to an existing sprk_communicationthread ⇒ JOIN.
        _entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                new("sprk_communicationchannelref") { ["sprk_thread"] = new EntityReference("sprk_communicationthread", existingThreadId) },
            }));
        var updates = CaptureUpdates();

        var handler = CreateHandler(CreateRealResolver(_entity));

        var outcome = await handler.ProcessAsync(RawInboundJob(acsThreadId, messageId: "1784231661501"), CancellationToken.None);

        outcome.Status.Should().Be(JobStatus.Completed);
        _entity.Verify(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communication"), It.IsAny<CancellationToken>()), Times.Once);
        persistedCommunication.Should().NotBeNull();
        persistedCommunication!.GetAttributeValue<string>("sprk_acsthreadid").Should().Be(acsThreadId);

        // JOIN path — no new thread or channel-ref created; the existing thread id is stamped directly.
        _entity.Verify(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()), Times.Never);
        _entity.Verify(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()), Times.Never);
        updates.Should().ContainSingle();
        updates[0]["sprk_communicationthread"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(existingThreadId);
    }
}
