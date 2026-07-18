using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Communication.Threads;
using Xunit;
using DataverseEntity = Microsoft.Xrm.Sdk.Entity;

namespace Sprk.Bff.Api.Tests.Seam.Communication;

/// <summary>
/// Vertical-slice seam for the task-040 <see cref="IThreadResolver"/> (FR-06): real <see cref="ThreadResolver"/>
/// + real <see cref="EmailThreadKeyStrategy"/> + real <see cref="MessagingThreadKeyStrategy"/>, mocking only
/// the Dataverse module boundary (<see cref="IGenericEntityService"/> / <see cref="ICommunicationDataverseService"/>).
/// Pins the direction-symmetric find-or-create behavior both inbound and outbound, for email + chat, plus the
/// NFR-02 non-fatal contract and the ADR-024 anchor-reuse (no second regarding mechanism).
/// </summary>
public class ThreadResolverSeamTests
{
    private const int ThreadTypeRecordAnchored = 100000000;
    private const int ThreadTypeDirect = 100000001;
    private const int MessageChannel = 100000004;

    private readonly Mock<IGenericEntityService> _entity = new(MockBehavior.Strict);
    private readonly Mock<ICommunicationDataverseService> _comm = new(MockBehavior.Loose);

    private ThreadResolver CreateResolver() =>
        new(
            new IThreadKeyStrategy[]
            {
                new EmailThreadKeyStrategy(_comm.Object, _entity.Object),
                new MessagingThreadKeyStrategy(_entity.Object),
            },
            _entity.Object,
            NullLogger<ThreadResolver>.Instance);

    private static DataverseEntity Communication(Guid id, params (string field, object value)[] attrs)
    {
        var e = new DataverseEntity("sprk_communication") { Id = id };
        foreach (var (field, value) in attrs)
            e[field] = value;
        return e;
    }

    private void SetupRetrieve(string logicalName, Guid id, DataverseEntity result) =>
        _entity.Setup(s => s.RetrieveAsync(logicalName, id, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);

    private List<Dictionary<string, object>> CaptureUpdates(string logicalName)
    {
        var captured = new List<Dictionary<string, object>>();
        _entity.Setup(s => s.UpdateAsync(logicalName, It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, f, _) => captured.Add(f))
            .Returns(Task.CompletedTask);
        return captured;
    }

    // ── Email ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAndAssign_EmailReplyWithAncestorThread_JoinsParentThread()
    {
        var commId = Guid.NewGuid();
        var parentId = Guid.NewGuid();
        var existingThreadId = Guid.NewGuid();

        _comm.Setup(c => c.GetCommunicationByInternetMessageIdAsync("<parent@x.com>", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Communication(parentId));
        // Parent's thread lookup (the ancestry query does NOT project it, so it is read explicitly).
        SetupRetrieve("sprk_communication", parentId,
            Communication(parentId, ("sprk_communicationthread", new EntityReference("sprk_communicationthread", existingThreadId))));
        var updates = CaptureUpdates("sprk_communication");

        var result = await CreateResolver().ResolveAndAssignThreadAsync(new ThreadResolutionRequest
        {
            CommunicationId = commId,
            ChannelType = CommunicationType.Email,
            Direction = CommunicationDirection.Incoming,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming, InReplyTo = "<parent@x.com>" },
        });

        result.Should().Be(existingThreadId);
        updates.Should().ContainSingle();
        updates[0]["sprk_communicationthread"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(existingThreadId);
        // JOIN path — no new thread created.
        _entity.Verify(s => s.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAndAssign_FreshOutboundEmailWithRegarding_CreatesRecordAnchoredThread()
    {
        var commId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var matterId = Guid.NewGuid();

        // Anchor read: the send path mapped the regarding onto the record (denormalized ADR-024 family).
        SetupRetrieve("sprk_communication", commId, Communication(commId,
            ("sprk_regardingrecordid", matterId.ToString()),
            ("sprk_regardingrecordtype", "sprk_matter"),
            ("sprk_regardingrecordname", "Acme v. Widgets")));

        DataverseEntity? createdThread = null;
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => createdThread = e)
            .ReturnsAsync(threadId);
        var updates = CaptureUpdates("sprk_communication");

        var result = await CreateResolver().ResolveAndAssignThreadAsync(new ThreadResolutionRequest
        {
            CommunicationId = commId,
            ChannelType = CommunicationType.Email,
            Direction = CommunicationDirection.Outgoing,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Outgoing, Subject = "Re: Acme" }, // fresh — no InReplyTo
        });

        result.Should().Be(threadId);
        createdThread.Should().NotBeNull();
        createdThread!.GetAttributeValue<string>("sprk_name").Should().Be("Re: Acme");
        createdThread.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeRecordAnchored);
        // Anchor REUSES the ADR-024 regarding family — NOT a second mechanism / no sprk_anchor* fields.
        createdThread.GetAttributeValue<string>("sprk_regardingrecordid").Should().Be(matterId.ToString());
        createdThread.GetAttributeValue<string>("sprk_regardingrecordtype").Should().Be("sprk_matter");
        createdThread.Attributes.Keys.Should().NotContain(k => k.StartsWith("sprk_anchor"));
        updates.Should().ContainSingle();
        updates[0]["sprk_communicationthread"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(threadId);
    }

    // ── Chat (Message) ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAndAssign_ChatMessageWithExistingAcsThread_JoinsThreadViaChannelRef()
    {
        var commId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        const string acsThreadId = "19:acs-thread-abc@thread.v2";

        _entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection(new List<DataverseEntity>
            {
                new("sprk_communicationchannelref") { ["sprk_thread"] = new EntityReference("sprk_communicationthread", threadId) },
            }));
        var updates = CaptureUpdates("sprk_communication");

        var result = await CreateResolver().ResolveAndAssignThreadAsync(new ThreadResolutionRequest
        {
            CommunicationId = commId,
            ChannelType = CommunicationType.Message,
            Direction = CommunicationDirection.Incoming,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming },
            AcsThreadId = acsThreadId,
        });

        result.Should().Be(threadId);
        updates.Should().ContainSingle();
        updates[0]["sprk_communicationthread"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(threadId);
        _entity.Verify(s => s.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ResolveAndAssign_FreshChatMessage_CreatesThreadAndChannelRefRow()
    {
        var commId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        var channelRefId = Guid.NewGuid();
        const string acsThreadId = "19:acs-thread-new@thread.v2";

        _entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection()); // no existing channel-ref → create
        // Chat is unassociated → anchor read yields no regarding.
        SetupRetrieve("sprk_communication", commId, Communication(commId));

        DataverseEntity? createdThread = null;
        DataverseEntity? createdChannelRef = null;
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => createdThread = e)
            .ReturnsAsync(threadId);
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .Callback<DataverseEntity, CancellationToken>((e, _) => createdChannelRef = e)
            .ReturnsAsync(channelRefId);
        var updates = CaptureUpdates("sprk_communication");

        var result = await CreateResolver().ResolveAndAssignThreadAsync(new ThreadResolutionRequest
        {
            CommunicationId = commId,
            ChannelType = CommunicationType.Message,
            Direction = CommunicationDirection.Incoming,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "Chat" },
            AcsThreadId = acsThreadId,
        });

        result.Should().Be(threadId);
        // Unassociated chat → Direct 1:1 thread type.
        createdThread!.GetAttributeValue<OptionSetValue>("sprk_threadtype").Value.Should().Be(ThreadTypeDirect);
        // Channel-ref maps the ACS ChatThreadId → the new thread.
        createdChannelRef.Should().NotBeNull();
        createdChannelRef!.GetAttributeValue<string>("sprk_externalref").Should().Be(acsThreadId);
        createdChannelRef.GetAttributeValue<OptionSetValue>("sprk_channeltype").Value.Should().Be(MessageChannel);
        createdChannelRef.GetAttributeValue<EntityReference>("sprk_thread").Id.Should().Be(threadId);
        updates.Should().ContainSingle();
        updates[0]["sprk_communicationthread"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(threadId);
    }

    [Fact]
    public async Task ResolveAndAssign_ChatMessageWithoutAcsThreadId_SkipsWithoutCreatingOrphanThread()
    {
        var result = await CreateResolver().ResolveAndAssignThreadAsync(new ThreadResolutionRequest
        {
            CommunicationId = Guid.NewGuid(),
            ChannelType = CommunicationType.Message,
            Direction = CommunicationDirection.Outgoing,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Outgoing },
            AcsThreadId = null, // outbound R1 — assigned later by the inbound echo path
        });

        result.Should().BeNull();
        _entity.Verify(s => s.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()), Times.Never);
        _entity.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── NFR-02 non-fatal ────────────────────────────────────────────────────────

    [Fact]
    public async Task ResolveAndAssign_WhenDataverseCreateThrows_ReturnsNullAndDoesNotThrow()
    {
        var commId = Guid.NewGuid();
        SetupRetrieve("sprk_communication", commId, Communication(commId,
            ("sprk_regardingrecordid", Guid.NewGuid().ToString()),
            ("sprk_regardingrecordtype", "sprk_matter")));
        _entity.Setup(s => s.CreateAsync(It.IsAny<DataverseEntity>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Dataverse unavailable"));

        var act = async () => await CreateResolver().ResolveAndAssignThreadAsync(new ThreadResolutionRequest
        {
            CommunicationId = commId,
            ChannelType = CommunicationType.Email,
            Direction = CommunicationDirection.Outgoing,
            Message = new NormalizedMessage { Direction = CommunicationDirection.Outgoing, Subject = "x" },
        });

        (await act.Should().NotThrowAsync()).Which.Should().BeNull();
    }

    // ── Call-site wiring: chat inbound (MessagingIngestor → resolver) ─────────────

    [Fact]
    public async Task MessagingIngestor_InboundChatMessage_PersistsThenStampsResolvedThread()
    {
        var commId = Guid.NewGuid();
        var threadId = Guid.NewGuid();
        const string acsThreadId = "19:acs-ingest@thread.v2";

        // Persist returns the new communication id; the resolver then creates the thread + channel-ref.
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communication"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(commId);
        _entity.Setup(s => s.RetrieveMultipleAsync(
                It.Is<QueryExpression>(q => q.EntityName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EntityCollection());
        SetupRetrieve("sprk_communication", commId, Communication(commId));
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationthread"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(threadId);
        _entity.Setup(s => s.CreateAsync(It.Is<DataverseEntity>(e => e.LogicalName == "sprk_communicationchannelref"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var updates = CaptureUpdates("sprk_communication");

        var enrichment = new Mock<ICommunicationEnrichmentService>(MockBehavior.Loose);
        var ingestor = new MessagingIngestor(
            _entity.Object, enrichment.Object, NullLogger<MessagingIngestor>.Instance, CreateResolver());

        var result = await ingestor.IngestAsync(new ChannelIngestRequest
        {
            Message = new NormalizedMessage { Direction = CommunicationDirection.Incoming, Subject = "Hi" },
            ProviderMessageId = "acs-msg-1",
            ProviderThreadId = acsThreadId,
            CorrelationId = Guid.NewGuid().ToString("N"),
        });

        result.CommunicationId.Should().Be(commId);
        // The inbound capture path invoked the resolver, which stamped the thread lookup on the persisted record.
        updates.Should().ContainSingle();
        updates[0]["sprk_communicationthread"].Should().BeOfType<EntityReference>().Which.Id.Should().Be(threadId);
    }
}
