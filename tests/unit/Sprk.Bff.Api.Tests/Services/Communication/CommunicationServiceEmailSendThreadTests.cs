using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of the OUTBOUND EMAIL send path's thread resolution (<c>ResolveOutboundThreadAsync</c>,
/// task 040 / FR-06) AFTER FR-19 (task 005). FR-19 brings the email branch to parity with the sibling Message
/// channel (<see cref="CommunicationServiceMessageSendTests"/>): when <see cref="SendCommunicationRequest.ThreadId"/>
/// is supplied ("respond into the current thread"), the email's <c>sprk_communicationthread</c> is stamped DIRECTLY
/// to that thread via the SAME <c>AssignExplicitThreadAsync</c> helper the Message path uses (no new send/assignment
/// path — ADR-045), bypassing the find-or-create <see cref="IThreadResolver"/> ladder. When <c>ThreadId</c> is
/// absent, the pre-FR-19 find-or-create behavior is preserved byte-for-byte (regression guard). Both paths are
/// best-effort / non-fatal (NFR-02): a stamp failure MUST NOT fail an already-sent + persisted email.
///
/// <para>This file supersedes the task-001 PRE-FR-19 characterization baseline (which pinned the now-flipped
/// "email silently ignores ThreadId" contract); the FR-19 flip is the reviewable, intentional diff task 001
/// was authored to surface.</para>
///
/// <para>Both email call sites route through the identical private <c>ResolveOutboundThreadAsync</c>: the
/// shared-mailbox branch (<c>SendAsync</c>) and the OBO branch (<c>SendAsUserAsync</c>, <c>SendMode.User</c>).
/// Both are covered below.</para>
///
/// <para><b>Boundary doubles only (ADR-038):</b> a hand-written <see cref="ICommunicationChannelSender"/> recording
/// double (mirrors <c>RecordingMessagingSender</c> — a module-boundary test double, NOT <c>Mock&lt;HttpMessageHandler&gt;</c>)
/// stands in for the Graph transmit; <see cref="IGenericEntityService"/> (the Dataverse persist boundary) and
/// <see cref="IThreadResolver"/> (the task-040 ladder) are mocked. Everything else — <see cref="CommunicationService"/>,
/// the REAL <see cref="CommunicationChannelDispatcher"/>, the REAL <see cref="ApprovedSenderValidator"/> — is
/// production code, unmocked.</para>
/// </summary>
public class CommunicationServiceEmailSendThreadTests
{
    private const string SenderEmail = "noreply@contoso.com";
    private const string UserEmail = "user@contoso.com";
    private const string ProviderMessageId = "AAMkAGI2-test-internet-message-id@example.com";

    /// <summary>Recording email sender — captures the request; returns a canned provider message id (email has
    /// no provider-thread concept, so <see cref="ChannelSendResult.ProviderThreadId"/> is always null).</summary>
    private sealed class RecordingEmailSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;
        public ChannelSendRequest? LastRequest { get; private set; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChannelSendResult
            {
                FromAddress = request.FromAddress,
                ProviderMessageId = ProviderMessageId,
                ProviderThreadId = null,
            });
        }
    }

    private static CommunicationOptions MinimalOptions() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig { Email = SenderEmail, DisplayName = "Contoso Notifications", IsDefault = true },
        },
        DefaultMailbox = SenderEmail,
    };

    private static CommunicationService BuildOutboundEmailSut(
        ICommunicationChannelSender emailSender,
        IGenericEntityService entityService,
        IThreadResolver threadResolver)
    {
        var options = MinimalOptions();
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            null!, // CommunicationAccountService — daily-limit lookup is best-effort/try-catch on the send path (see CommunicationServiceTests precedent for null! here)
            Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var dispatcher = new CommunicationChannelDispatcher(
            new[] { emailSender },
            new ICommunicationArchiver[]
            {
                new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())),
            });

        return new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService,
            Mock.Of<IDocumentDataverseService>(),
            null!, // CommunicationAccountService — not exercised (ArchiveToSpe = false; see CommunicationServiceTests precedent)
            null!, // JobSubmissionService — not exercised (ArchiveToSpe = false)
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            Sprk.Bff.Api.Tests.TestInfrastructure.CoreAncestorResolverFixtures.Inert(),
            Mock.Of<ILogger<CommunicationService>>(),
            threadResolver);
    }

    private static SendCommunicationRequest EmailRequest() => new()
    {
        To = new[] { "recipient@contoso.com" },
        Subject = "Kickoff",
        Body = "hello there",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
    };

    /// <summary>Authenticated OBO context carrying the email + oid claims <c>SendAsUserAsync</c> reads.</summary>
    private static HttpContext AuthenticatedUserContext()
    {
        var identity = new ClaimsIdentity(new[]
        {
            new Claim("preferred_username", UserEmail),
            new Claim("oid", "55555555-5555-5555-5555-555555555555"),
            new Claim("name", "User One"),
        }, authenticationType: "test");
        return new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
    }

    private static Mock<IGenericEntityService> EntityServiceCreating()
    {
        var entity = new Mock<IGenericEntityService>();
        entity.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        entity.Setup(s => s.UpdateAsync(
                It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return entity;
    }

    // ── (b regression) no ThreadId ⇒ the find-or-create resolver runs and there is NO direct thread stamp
    //    (pre-FR-19 behavior preserved byte-for-byte) ──
    [Fact]
    public async Task SendAsync_ForEmailType_WithNoThreadId_UsesFindOrCreateThreadResolver()
    {
        var entity = EntityServiceCreating();
        var threadResolver = new Mock<IThreadResolver>();
        threadResolver
            .Setup(r => r.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var sut = BuildOutboundEmailSut(new RecordingEmailSender(), entity.Object, threadResolver.Object);

        var response = await sut.SendAsync(EmailRequest(), httpContext: null, CancellationToken.None);

        response.CommunicationId.Should().NotBeNull();
        threadResolver.Verify(
            r => r.ResolveAndAssignThreadAsync(
                It.Is<ThreadResolutionRequest>(req => req.Direction == CommunicationDirection.Outgoing),
                It.IsAny<CancellationToken>()),
            Times.Once);
        // Without ThreadId the explicit-target branch is not taken — never a direct sprk_communicationthread stamp.
        entity.Verify(
            s => s.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_communicationthread")),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── (a) FR-19 explicit target — shared-mailbox call site: WithThreadId stamps sprk_communicationthread
    //    DIRECTLY to T and NEVER calls the find-or-create resolver (parity with the Message channel) ──
    [Fact]
    public async Task SendAsync_ForEmailType_WithThreadId_StampsThreadLookupDirectly_NoFindOrCreate()
    {
        var explicitThreadId = Guid.NewGuid();
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

        // Strict: the explicit-target branch must bypass find-or-create entirely (any resolver call fails the test).
        var threadResolver = new Mock<IThreadResolver>(MockBehavior.Strict);

        var sut = BuildOutboundEmailSut(new RecordingEmailSender(), entity.Object, threadResolver.Object);

        var request = EmailRequest() with { ThreadId = explicitThreadId };
        var response = await sut.SendAsync(request, httpContext: null, CancellationToken.None);

        response.CommunicationId.Should().NotBeNull();
        updatedFields.Should().NotBeNull();
        updatedFields!["sprk_communicationthread"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(explicitThreadId);
        updatedCommunicationId.Should().Be(response.CommunicationId);

        threadResolver.Invocations.Should().BeEmpty(); // find-or-create resolver never called
    }

    // ── (both call sites) FR-19 explicit target — OBO (SendMode.User) call site routes through the SAME
    //    ResolveOutboundThreadAsync, so it also stamps the explicit thread directly ──
    [Fact]
    public async Task SendAsUser_ForEmailType_WithThreadId_StampsThreadLookupDirectly_NoFindOrCreate()
    {
        var explicitThreadId = Guid.NewGuid();
        var entity = EntityServiceCreating();
        Dictionary<string, object>? updatedFields = null;
        entity.Setup(s => s.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, _, fields, _) => updatedFields = fields)
            .Returns(Task.CompletedTask);

        var threadResolver = new Mock<IThreadResolver>(MockBehavior.Strict);

        var sut = BuildOutboundEmailSut(new RecordingEmailSender(), entity.Object, threadResolver.Object);

        var request = EmailRequest() with { SendMode = SendMode.User, ThreadId = explicitThreadId };
        var response = await sut.SendAsync(request, AuthenticatedUserContext(), CancellationToken.None);

        response.CommunicationId.Should().NotBeNull();
        updatedFields.Should().NotBeNull();
        updatedFields!["sprk_communicationthread"].Should().BeOfType<EntityReference>()
            .Which.Id.Should().Be(explicitThreadId);

        threadResolver.Invocations.Should().BeEmpty(); // OBO call site also bypasses find-or-create
    }

    // ── (c) best-effort / non-fatal (NFR-02): a thread-stamp write failure does NOT fail the already-sent +
    //    persisted email ──
    [Fact]
    public async Task SendAsync_ForEmailType_WithThreadId_WhenStampWriteFails_StillSucceeds()
    {
        var explicitThreadId = Guid.NewGuid();
        var entity = new Mock<IGenericEntityService>();
        entity.Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        entity.Setup(s => s.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_communicationthread")),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("thread stamp boom"));

        var threadResolver = new Mock<IThreadResolver>(MockBehavior.Strict);

        var sut = BuildOutboundEmailSut(new RecordingEmailSender(), entity.Object, threadResolver.Object);

        var request = EmailRequest() with { ThreadId = explicitThreadId };
        var response = await sut.SendAsync(request, httpContext: null, CancellationToken.None);

        // Send + persist succeeded despite the thread-stamp failure (best-effort swallowed inside AssignExplicitThreadAsync).
        response.CommunicationId.Should().NotBeNull();
        threadResolver.Invocations.Should().BeEmpty(); // explicit branch was taken; find-or-create still bypassed
    }
}
