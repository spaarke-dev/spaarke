using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Access;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Engine;
using Sprk.Bff.Api.Services.Communication.Models;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Services.Jobs.Handlers;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the OUTBOUND MESSAGE (ACS Chat) send path on <see cref="CommunicationService"/>
/// (task 051 / FR-04 / ADR-045 rule 3). These protect the load-bearing, correctness-critical properties that
/// would regress silently:
///   (a) a Message-typed send DISPATCHES through <see cref="CommunicationChannelDispatcher.ResolveSender"/>
///       (as the sending participant) and persists EXACTLY ONE <c>sprk_communication</c> (type=Message,
///       Direction=Outgoing) recording the ACS message id + thread id under the AS-BUILT schema names;
///   (b) ECHO-DEDUP: the outbound send MARKS the SAME idempotency key the inbound handler dedupes on
///       (<see cref="IncomingMessagingJobHandler.IdempotencyKeyFor"/>) — so the Event Grid echo of our own
///       message, delivered through the REAL inbound handler, is a no-op ⇒ exactly one persisted record;
///   (c) a DUPLICATE echo delivery still yields exactly one record;
///   (d) BEST-EFFORT: a thread-assignment or enrichment exception does NOT fail the send (NFR-02);
///   (e) NEGATIVE: no ACS token / admin capability is returned to the caller (NFR-04/NFR-05).
///
/// The send path is exercised end-to-end through the REAL dispatcher, REAL idempotency wiring
/// (<see cref="MarkOutboundEchoProcessedAsync"/> via a real <see cref="IServiceScopeFactory"/>), and — for the
/// echo tests — the REAL <see cref="IncomingMessagingJobHandler"/> + REAL <see cref="MessagingIngestor"/>.
/// Only the Dataverse persist boundary (<see cref="IGenericEntityService"/>), the ACS transmit (the messaging
/// sender is a recording double — the live ACS behavior is covered by MessagingChannelSenderTests), and the
/// systemuser lookup are doubled. The persisted-record count is <c>IGenericEntityService.CreateAsync</c> call count.
/// </summary>
public class CommunicationServiceMessageSendTests
{
    private const string AcsMessageId = "1784231661455"; // the task's canonical ACS message id (numeric string)
    private const string AcsThreadId = "19:acs:thread_00000000000000000000000000000009@thread.v2";
    private const string SenderEmail = "author@spaarke.com";
    private const string SenderOid = "11111111-1111-1111-1111-111111111111";
    private const string MintedChatToken = "SUPER-SECRET-ACS-CHAT-TOKEN"; // must NEVER surface to the caller

    /// <summary>Honest in-memory <see cref="IIdempotencyService"/> — real processed-set + lock semantics.</summary>
    private sealed class FakeIdempotencyService : IIdempotencyService
    {
        private readonly HashSet<string> _processed = new();
        private readonly HashSet<string> _locks = new();

        public bool IsProcessed(string key) => _processed.Contains(key);

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

    /// <summary>
    /// Recording messaging sender (<c>SupportedType = Message</c>): captures the <see cref="ChannelSendRequest"/>
    /// (so tests can assert the sender acts AS the participant) and returns a canned ACS message + thread id — the
    /// ACS transmit + token-minting behavior itself is covered by <c>MessagingChannelSenderTests</c>.
    /// </summary>
    private sealed class RecordingMessagingSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Message;
        public ChannelSendRequest? LastRequest { get; private set; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            // The sender mints a chat token internally to post AS the participant; ChannelSendResult carries only
            // ids — never the token (NFR-05). Returning the ids the way the real sender does.
            return Task.FromResult(new ChannelSendResult
            {
                FromAddress = "8:acs:res_" + SenderOid,
                ProviderMessageId = AcsMessageId,
                ProviderThreadId = AcsThreadId
            });
        }
    }

    private static CommunicationOptions MinimalOptions() => new()
    {
        ApprovedSenders = Array.Empty<ApprovedSenderConfig>(),
        DefaultMailbox = "noreply@contoso.com"
    };

    private static HttpContext AuthenticatedContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("oid", SenderOid),
            new Claim("preferred_username", SenderEmail),
            new Claim("name", "Author One"),
        }, authenticationType: "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static IServiceScopeFactory ScopeFactoryFor(IIdempotencyService idempotency)
    {
        var services = new ServiceCollection();
        services.AddSingleton(idempotency);
        return services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
    }

    private static CommunicationService BuildOutboundSut(
        ICommunicationChannelSender messagingSender,
        IGenericEntityService entityService,
        IIdempotencyService idempotency,
        IThreadResolver? threadResolver = null,
        ICommunicationEnrichmentService? enrichment = null,
        ICommunicationDataverseService? communicationDataverse = null,
        Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService? directThreadAccess = null)
    {
        var options = MinimalOptions();

        var comm = communicationDataverse ?? DataverseResolvingSystemUser();

        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            accountService,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var dispatcher = new CommunicationChannelDispatcher(
            new[] { messagingSender },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        return new CommunicationService(
            dispatcher,
            senderValidator,
            comm,
            entityService,
            Mock.Of<IDocumentDataverseService>(),
            null!,  // CommunicationAccountService — not exercised on the message path
            null!,  // JobSubmissionService — not exercised on the message path
            enrichment ?? Mock.Of<ICommunicationEnrichmentService>(),
            Options.Create(options),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            new Sprk.Bff.Api.Tests.TestInfrastructure.RecordOwnershipResolverDouble(),
            Mock.Of<ILogger<CommunicationService>>(),
            threadResolver,
            ScopeFactoryFor(idempotency),
            directThreadAccess);
    }

    private static ICommunicationDataverseService DataverseResolvingSystemUser()
    {
        var comm = new Mock<ICommunicationDataverseService>();
        comm.Setup(c => c.QuerySystemUserByAzureAdOidAsync(SenderOid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.Parse("22222222-2222-2222-2222-222222222222"));
        return comm.Object;
    }

    private static SendCommunicationRequest MessageRequest(string body = "hello there") => new()
    {
        To = new[] { "recipient@contoso.com" },
        Subject = "Kickoff",
        Body = body,
        BodyFormat = BodyFormat.PlainText,
        CommunicationType = CommunicationType.Message
    };

    private static Mock<IGenericEntityService> EntityServiceCreating(Guid? id = null)
    {
        var entity = new Mock<IGenericEntityService>();
        entity.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(id ?? Guid.NewGuid());
        return entity;
    }

    // ── (a) dispatch + persist-on-send: exactly one record with the ACS correlation ids ──
    [Fact]
    public async Task SendAsync_ForMessageType_DispatchesToMessagingSenderAndPersistsOneRecordWithAcsIds()
    {
        var sender = new RecordingMessagingSender();
        var entity = EntityServiceCreating();
        Entity? persisted = null;
        entity.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => persisted = e)
            .ReturnsAsync(Guid.NewGuid());

        var sut = BuildOutboundSut(sender, entity.Object, new FakeIdempotencyService());

        var response = await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        // Dispatched AS the participant through the seam (not a provider call).
        sender.LastRequest.Should().NotBeNull();
        sender.LastRequest!.SenderParticipant.Should().NotBeNull();
        sender.LastRequest.SenderParticipant!.EntityLogicalName.Should().Be("systemuser");

        // Persisted exactly once with the AS-BUILT names + Message type + Outgoing direction.
        entity.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        persisted.Should().NotBeNull();
        persisted!.GetAttributeValue<OptionSetValue>("sprk_communicationtype").Value.Should().Be((int)CommunicationType.Message);
        persisted.GetAttributeValue<OptionSetValue>("sprk_direction").Value.Should().Be((int)CommunicationDirection.Outgoing);
        persisted.GetAttributeValue<string>("sprk_acsmessageid").Should().Be(AcsMessageId);
        persisted.GetAttributeValue<string>("sprk_acsthreadid").Should().Be(AcsThreadId);

        response.CommunicationId.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send);
        response.From.Should().Be(SenderEmail);
    }

    // ── (b) echo-dedup mark: the SAME key the inbound handler dedupes on is marked ──
    [Fact]
    public async Task SendAsync_ForMessageType_MarksInboundEchoDedupKey()
    {
        var idempotency = new FakeIdempotencyService();
        var sut = BuildOutboundSut(new RecordingMessagingSender(), EntityServiceCreating().Object, idempotency);

        await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        idempotency.IsProcessed(IncomingMessagingJobHandler.IdempotencyKeyFor(AcsMessageId)).Should().BeTrue();
        idempotency.IsProcessed($"acs-msg:{AcsMessageId}").Should().BeTrue(); // exact contract shape
    }

    // ── (b) end-to-end exactly-once: send, then deliver the ACS echo through the REAL inbound handler ⇒ one record ──
    [Fact]
    public async Task SendThenAcsEcho_DeliveredThroughInboundHandler_YieldsExactlyOnePersistedRecord()
    {
        // Shared state across the outbound + inbound legs: one Dataverse boundary (persist count) + one idempotency store.
        var idempotency = new FakeIdempotencyService();
        var entity = EntityServiceCreating();

        var outboundSut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, idempotency);
        var inboundHandler = BuildInboundHandler(entity.Object, idempotency);

        // 1) Outbound persist-on-send (persist #1) + echo-dedup mark.
        await outboundSut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        // 2) ACS echoes our own message back via Event Grid → inbound handler sees the key already marked → no-op.
        var echo = await inboundHandler.ProcessAsync(EchoJob(), CancellationToken.None);

        echo.Status.Should().Be(JobStatus.Completed);
        entity.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once); // EXACTLY ONE
    }

    // ── (c) a duplicate echo delivery still yields exactly one record (idempotent) ──
    [Fact]
    public async Task SendThenDuplicateAcsEcho_YieldsExactlyOnePersistedRecord()
    {
        var idempotency = new FakeIdempotencyService();
        var entity = EntityServiceCreating();

        var outboundSut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, idempotency);
        var inboundHandler = BuildInboundHandler(entity.Object, idempotency);

        await outboundSut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);
        var echo1 = await inboundHandler.ProcessAsync(EchoJob(), CancellationToken.None);
        var echo2 = await inboundHandler.ProcessAsync(EchoJob(), CancellationToken.None);

        echo1.Status.Should().Be(JobStatus.Completed);
        echo2.Status.Should().Be(JobStatus.Completed);
        entity.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once); // still ONE
    }

    // ── (d) best-effort: a thread-assignment exception does NOT fail the send (NFR-02) ──
    [Fact]
    public async Task SendAsync_ForMessageType_WhenThreadResolverThrows_StillPersistsAndSucceeds()
    {
        var throwingResolver = new Mock<IThreadResolver>();
        throwingResolver
            .Setup(r => r.ResolveAndAssignAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("thread boom"));

        var idempotency = new FakeIdempotencyService();
        var entity = EntityServiceCreating();
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, idempotency, threadResolver: throwingResolver.Object);

        var response = await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        response.CommunicationId.Should().NotBeNull(); // send succeeded despite the thread failure
        entity.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
        idempotency.IsProcessed(IncomingMessagingJobHandler.IdempotencyKeyFor(AcsMessageId)).Should().BeTrue();
    }

    // ── (d) best-effort: an enrichment exception does NOT fail the send (NFR-02) ──
    [Fact]
    public async Task SendAsync_ForMessageType_WhenEnrichmentThrows_StillPersistsAndSucceeds()
    {
        var throwingEnrichment = new Mock<ICommunicationEnrichmentService>();
        throwingEnrichment
            .Setup(e => e.EnrichAsync(
                It.IsAny<Guid>(), It.IsAny<CommunicationDirection>(), It.IsAny<NormalizedMessage>(),
                It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("enrichment boom"));

        var entity = EntityServiceCreating();
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService(), enrichment: throwingEnrichment.Object);

        var response = await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        response.CommunicationId.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send);
        entity.Verify(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── (e) NEGATIVE (NFR-04/NFR-05): no ACS token / admin capability is returned to the caller ──
    [Fact]
    public async Task SendAsync_ForMessageType_DoesNotReturnAnyAcsTokenToTheCaller()
    {
        var sut = BuildOutboundSut(new RecordingMessagingSender(), EntityServiceCreating().Object, new FakeIdempotencyService());

        var response = await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        // The response carries only the tracking record id + human sender — never the minted chat token.
        var serialized = JsonSerializer.Serialize(response);
        serialized.Should().NotContain(MintedChatToken);
        response.From.Should().Be(SenderEmail).And.NotBe(MintedChatToken);
    }

    // ── missing sender identity → RFC 7807 ProblemDetails (a message needs a sending participant) ──
    [Fact]
    public async Task SendAsync_ForMessageType_WhenSystemUserUnresolved_ThrowsProblemDetails()
    {
        var comm = new Mock<ICommunicationDataverseService>();
        comm.Setup(c => c.QuerySystemUserByAzureAdOidAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var sut = BuildOutboundSut(
            new RecordingMessagingSender(), EntityServiceCreating().Object, new FakeIdempotencyService(),
            communicationDataverse: comm.Object);

        var act = () => sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        (await act.Should().ThrowAsync<Sprk.Bff.Api.Infrastructure.Exceptions.SdapProblemException>())
            .Which.Code.Should().Be("SENDER_NOT_RESOLVED");
    }

    // ── task 062 / FR-12 (A): explicit ThreadId stamps sprk_communicationthread directly, bypassing
    //    the find-or-create IThreadResolver entirely, and chains the task-043 message-access grant ──
    [Fact]
    public async Task SendAsync_ForMessageType_WithThreadId_StampsThreadLookupDirectlyAndGrantsAccess()
    {
        var explicitThreadId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var entity = EntityServiceCreating();
        Guid? updatedCommunicationId = null;
        Dictionary<string, object>? updatedFields = null;
        entity.Setup(s => s.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, id, fields, _) =>
            {
                updatedCommunicationId = id;
                updatedFields = fields;
            })
            .Returns(Task.CompletedTask);
        // #1629 F3: the thread the caller responds into is a "Direct" thread carrying a typed account lookup (a party:
        // no re-file). The grant must receive exactly the audience the join's read produced — the account's members —
        // so the assertion below pins the audience, not just the thread id.
        var accountId = Guid.Parse("acacacac-3333-3333-3333-333333333333");
        entity.Setup(s => s.RetrieveAsync(
                "sprk_communicationthread", explicitThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_communicationthread")
            {
                Id = explicitThreadId,
                ["sprk_threadtype"] = new OptionSetValue(100000001),
                ["sprk_regardingaccount"] = new EntityReference("account", accountId),
            });

        // Never touched — the explicit-target branch bypasses find-or-create entirely.
        var resolver = new Mock<IThreadResolver>(MockBehavior.Strict);

        var grantAccess = new Mock<Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService>();
        grantAccess
            .Setup(g => g.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = BuildOutboundSut(
            new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService(),
            threadResolver: resolver.Object, directThreadAccess: grantAccess.Object);

        var request = MessageRequest() with { ThreadId = explicitThreadId };
        var response = await sut.SendAsync(request, AuthenticatedContext(), CancellationToken.None);

        updatedFields.Should().NotBeNull();
        updatedFields!["sprk_communicationthread"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(explicitThreadId);
        updatedCommunicationId.Should().Be(response.CommunicationId);

        resolver.Invocations.Should().BeEmpty(); // find-or-create resolver never called
        grantAccess.Verify(
            g => g.GrantMessageAccessAsync(
                response.CommunicationId!.Value, ThreadAssignment.Anchored(explicitThreadId, "account", accountId), It.IsAny<CancellationToken>()),
            Times.Once);
        grantAccess.Verify(
            g => g.GrantMessageAccessAsync(It.IsAny<Guid>(), It.IsAny<ThreadAssignment>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static readonly Guid ResolvedMatter = Guid.Parse("4d4d4d4d-4444-4444-4444-444444444444");

    // ── task 062 / FR-12 (A): omitting ThreadId leaves the existing find-or-create resolver path
    //    unchanged (email-send parity — regression guard for the additive contract) ──
    [Fact]
    public async Task SendAsync_ForMessageType_WithoutThreadId_StillUsesFindOrCreateResolver()
    {
        var resolvedThreadId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var resolver = new Mock<IThreadResolver>();
        resolver
            .Setup(r => r.ResolveAndAssignAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ThreadAssignment.Anchored(resolvedThreadId, "sprk_matter", ResolvedMatter));
        var grantAccess = new Mock<Sprk.Bff.Api.Services.Communication.Access.IDirectThreadAccessService>();

        var entity = EntityServiceCreating();
        var sut = BuildOutboundSut(
            new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService(), threadResolver: resolver.Object,
            directThreadAccess: grantAccess.Object);

        var response = await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        response.CommunicationId.Should().NotBeNull();
        // #1629 F3: the grant receives the resolver's assignment unchanged (its audience and anchor).
        grantAccess.Verify(
            g => g.GrantMessageAccessAsync(
                response.CommunicationId!.Value, ThreadAssignment.Anchored(resolvedThreadId, "sprk_matter", ResolvedMatter), It.IsAny<CancellationToken>()),
            Times.Once);
        resolver.Verify(
            r => r.ResolveAndAssignAsync(
                It.Is<ThreadResolutionRequest>(req => req.ChannelType == CommunicationType.Message),
                It.IsAny<CancellationToken>()),
            Times.Once);
        entity.Verify(s => s.UpdateAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── task 062: InReplyToMessageId is now stamped on the Message channel's persisted record
    //    (previously only wired for Email) ──
    [Fact]
    public async Task SendAsync_ForMessageType_WithInReplyToMessageId_StampsSprkInReplyToOnPersistedRecord()
    {
        var entity = EntityServiceCreating();
        Entity? persisted = null;
        entity.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => persisted = e)
            .ReturnsAsync(Guid.NewGuid());

        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        var request = MessageRequest() with { InReplyToMessageId = "parent-comm-id-123" };
        await sut.SendAsync(request, AuthenticatedContext(), CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.GetAttributeValue<string>("sprk_inreplyto").Should().Be("parent-comm-id-123");
    }

    // ── task 065 (external-access r3, owner 2026-10-13): the persisted message carries an EXPLICIT internal-only
    //    flag and its sender, so the external read (which hides a row whose flag is unset — fail closed) shows it ──

    private static readonly Guid SenderSystemUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TargetThreadId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    /// <summary>A contact (external) caller of the external read, evaluated by the REAL access filter.</summary>
    private static CommunicationAccessDecision ExternalContactDecision(Entity persisted) =>
        new CommunicationAccessFilter(Mock.Of<ILogger<CommunicationAccessFilter>>())
            .EvaluateMessage(new CommunicationAccessContext(Guid.Empty, IsInternalUser: false, CallerContactId: Guid.NewGuid()), persisted);

    private static Mock<IGenericEntityService> EntityServiceCapturing(Action<Entity> capture)
    {
        var entity = EntityServiceCreating();
        entity.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => capture(e))
            .ReturnsAsync(Guid.NewGuid());
        return entity;
    }

    private static void ThreadWithPrivacy(Mock<IGenericEntityService> entity, int? privacyState)
    {
        var thread = new Entity("sprk_communicationthread") { Id = TargetThreadId };
        if (privacyState is { } state)
            thread["sprk_privacystate"] = new OptionSetValue(state);
        entity.Setup(s => s.RetrieveAsync(
                "sprk_communicationthread", TargetThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(thread);
    }

    [Fact]
    public async Task SendAsync_ForMessageType_WithoutIsInternalOnly_PersistsMessageVisibleToExternalContact()
    {
        Entity? persisted = null;
        var sut = BuildOutboundSut(
            new RecordingMessagingSender(), EntityServiceCapturing(e => persisted = e).Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        persisted.Should().NotBeNull();
        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
        ExternalContactDecision(persisted).IsVisible.Should().BeTrue(
            "a message the sender did not mark internal is visible to an external participant on the record");
    }

    [Fact]
    public async Task SendAsync_ForMessageType_StampsSentByWithTheSendingSystemUser()
    {
        Entity? persisted = null;
        var sut = BuildOutboundSut(
            new RecordingMessagingSender(), EntityServiceCapturing(e => persisted = e).Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest(), AuthenticatedContext(), CancellationToken.None);

        var sentBy = persisted!.GetAttributeValue<EntityReference>("sprk_sentby");
        sentBy.Should().NotBeNull();
        sentBy!.LogicalName.Should().Be("systemuser");
        sentBy.Id.Should().Be(SenderSystemUserId);
    }

    [Fact]
    public async Task SendAsync_ForMessageType_WithIsInternalOnlyTrue_PersistsMessageHiddenFromExternalContact()
    {
        Entity? persisted = null;
        var sut = BuildOutboundSut(
            new RecordingMessagingSender(), EntityServiceCapturing(e => persisted = e).Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest() with { IsInternalOnly = true }, AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
        ExternalContactDecision(persisted).IsVisible.Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ForMessageType_IntoPrivateThreadWithoutIsInternalOnly_PersistsInternalOnly()
    {
        Entity? persisted = null;
        var entity = EntityServiceCapturing(e => persisted = e);
        ThreadWithPrivacy(entity, privacyState: 100000001); // Private
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest() with { ThreadId = TargetThreadId }, AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue(
            "a message into a Private thread with no explicit choice fails closed to internal-only");
    }

    [Fact]
    public async Task SendAsync_ForMessageType_IntoOpenThreadWithoutIsInternalOnly_PersistsNotInternalOnly()
    {
        Entity? persisted = null;
        var entity = EntityServiceCapturing(e => persisted = e);
        ThreadWithPrivacy(entity, privacyState: 100000000); // Open
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest() with { ThreadId = TargetThreadId }, AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ForMessageType_IntoPrivateThreadWithIsInternalOnlyFalse_PersistsTheSendersChoice()
    {
        Entity? persisted = null;
        var entity = EntityServiceCapturing(e => persisted = e);
        ThreadWithPrivacy(entity, privacyState: 100000001); // Private
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        await sut.SendAsync(
            MessageRequest() with { ThreadId = TargetThreadId, IsInternalOnly = false }, AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeFalse();
    }

    [Fact]
    public async Task SendAsync_ForMessageType_IntoThreadWhosePrivacyIsUnreadable_PersistsInternalOnly()
    {
        Entity? persisted = null;
        var entity = EntityServiceCapturing(e => persisted = e);
        ThreadWithPrivacy(entity, privacyState: null); // privacy not readable → not confirmed Open
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest() with { ThreadId = TargetThreadId }, AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    [Fact]
    public async Task SendAsync_ForMessageType_IntoThreadWhosePrivacyReadThrows_PersistsInternalOnly()
    {
        Entity? persisted = null;
        var entity = EntityServiceCapturing(e => persisted = e);
        entity.Setup(s => s.RetrieveAsync(
                "sprk_communicationthread", TargetThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("dataverse unavailable"));
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest() with { ThreadId = TargetThreadId }, AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue(
            "a thread whose privacy cannot be read is not confirmed Open (fail closed)");
    }

    [Fact]
    public async Task SendAsync_ForMessageType_IntoThreadWhosePrivacyRowIsNull_PersistsInternalOnly()
    {
        Entity? persisted = null;
        var entity = EntityServiceCapturing(e => persisted = e);
        entity.Setup(s => s.RetrieveAsync(
                "sprk_communicationthread", TargetThreadId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        await sut.SendAsync(MessageRequest() with { ThreadId = TargetThreadId }, AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().BeTrue();
    }

    // Message sends share SendCommunicationRequest and ResolveOutboundOwnerAsync with email, so a Message reply that
    // names InheritRegardingFromCommunicationId follows the same quoted-parent rule (fix round 1, F1).
    private static readonly Guid ReplySourceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Theory]
    [InlineData(true, null, true)]     // source flagged internal-only
    [InlineData(null, null, true)]     // source flag unset
    [InlineData(false, null, false)]   // source explicitly not internal-only
    [InlineData(true, false, false)]   // explicit request value wins over a source flagged true
    [InlineData(false, true, true)]    // explicit request value wins over a source explicitly false
    public async Task SendAsync_ForMessageType_ReplyInheritingFromParent_FollowsTheParentsFlagUnlessExplicit(
        bool? sourceFlag, bool? explicitFlag, bool expected)
    {
        Entity? persisted = null;
        var entity = EntityServiceCapturing(e => persisted = e);
        var source = new Entity("sprk_communication", ReplySourceId);
        if (sourceFlag is { } flag)
            source["sprk_isinternalonly"] = flag;
        entity.Setup(s => s.RetrieveAsync("sprk_communication", ReplySourceId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(source);
        var sut = BuildOutboundSut(new RecordingMessagingSender(), entity.Object, new FakeIdempotencyService());

        await sut.SendAsync(
            MessageRequest() with { InheritRegardingFromCommunicationId = ReplySourceId, IsInternalOnly = explicitFlag },
            AuthenticatedContext(), CancellationToken.None);

        persisted!.GetAttributeValue<bool?>("sprk_isinternalonly").Should().Be(expected);
    }

    // ── Inbound-echo scaffolding: the REAL handler + REAL ingestor over the shared boundary ──
    private static IncomingMessagingJobHandler BuildInboundHandler(
        IGenericEntityService entityService, IIdempotencyService idempotency)
    {
        var ingestor = new MessagingIngestor(
            entityService, Mock.Of<ICommunicationEnrichmentService>(), Mock.Of<ILogger<MessagingIngestor>>());

        var dispatcher = new CommunicationChannelDispatcher(
            senders: Enumerable.Empty<ICommunicationChannelSender>(),
            archivers: Enumerable.Empty<ICommunicationArchiver>(),
            ingestors: new ICommunicationChannelIngestor[] { ingestor });

        return new IncomingMessagingJobHandler(
            new AcsEventNormalizer(), dispatcher, idempotency, Mock.Of<ILogger<IncomingMessagingJobHandler>>());
    }

    private static JobContract EchoJob()
    {
        const string eventType = "Microsoft.Communication.ChatMessageReceivedInThread";
        var data = new Dictionary<string, object?>
        {
            ["messageId"] = AcsMessageId,
            ["threadId"] = AcsThreadId,
            ["messageBody"] = "hello there",
            ["messageType"] = "Text",
            ["senderCommunicationIdentifier"] = new Dictionary<string, object?> { ["rawId"] = "8:acs:res_" + SenderOid },
            ["composeTime"] = "2026-07-16T12:00:01Z",
        };
        var evt = new Dictionary<string, object?>
        {
            ["id"] = "evt-echo",
            ["eventType"] = eventType,
            ["subject"] = $"thread/{AcsThreadId}",
            ["eventTime"] = "2026-07-16T12:00:02Z",
            ["data"] = data,
        };

        var payload = new IncomingMessagingJobPayload
        {
            AcsMessageId = AcsMessageId,
            AcsThreadId = AcsThreadId,
            EventType = eventType,
            EventId = "evt-echo",
            EventTime = DateTimeOffset.Parse("2026-07-16T12:00:02Z"),
            RawEvent = JsonSerializer.SerializeToElement(evt),
        };

        return new JobContract
        {
            JobType = IncomingMessagingJobHandler.JobTypeName,
            SubjectId = AcsMessageId,
            CorrelationId = "corr-051-echo",
            IdempotencyKey = $"Messaging:{eventType}:{AcsMessageId}",
            Attempt = 1,
            MaxAttempts = 3,
            Payload = JsonSerializer.SerializeToDocument(payload),
        };
    }
}
