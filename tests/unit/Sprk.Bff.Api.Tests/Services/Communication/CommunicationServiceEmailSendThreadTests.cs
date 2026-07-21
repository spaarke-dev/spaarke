using FluentAssertions;
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
/// Task-001 characterization baseline (plan Phase 1 gate / spec NFR-08) for the OUTBOUND EMAIL send path's
/// thread resolution (<c>ResolveOutboundThreadAsync</c>, task 040 / FR-06), authored BEFORE any Phase-1
/// (FR-16..19) edit. Pins the CURRENT, load-bearing contract that <see cref="CommunicationServiceMessageSendTests"/>
/// already pins for the sibling Message channel: unlike the Message send path's explicit-target branch
/// (<c>ResolveOutboundMessageThreadAsync</c> / <c>AssignExplicitThreadAsync</c>, task 062 / FR-12), the EMAIL
/// path's <c>ResolveOutboundThreadAsync</c> does NOT branch on <see cref="SendCommunicationRequest.ThreadId"/> at
/// all — it ALWAYS runs the find-or-create <see cref="IThreadResolver"/> ladder, regardless of whether
/// <c>ThreadId</c> is supplied. This is documented on the field itself ("Ignored for Email sends") and is the
/// PRE-FR-19 behavior the FR-19 task will deliberately change — this baseline makes that change a reviewable,
/// intentional diff rather than a silent regression.
///
/// <para><b>Boundary doubles only (ADR-038):</b> a hand-written <see cref="ICommunicationChannelSender"/> recording
/// double (mirrors <c>RecordingMessagingSender</c> in <see cref="CommunicationServiceMessageSendTests"/> — a
/// module-boundary test double, NOT <c>Mock&lt;HttpMessageHandler&gt;</c>) stands in for the Graph transmit;
/// <see cref="IGenericEntityService"/> (the Dataverse persist boundary) and <see cref="IThreadResolver"/> (the
/// task-040 ladder) are mocked. Everything else — <see cref="CommunicationService"/>, the REAL
/// <see cref="CommunicationChannelDispatcher"/>, the REAL <see cref="ApprovedSenderValidator"/> — is production
/// code, unmocked.</para>
/// </summary>
public class CommunicationServiceEmailSendThreadTests
{
    private const string SenderEmail = "noreply@contoso.com";
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
            null!, // SpeFileStore — not exercised (ArchiveToSpe = false, the default)
            null!, // CommunicationAccountService — not exercised (ArchiveToSpe = false; see CommunicationServiceTests precedent)
            null!, // JobSubmissionService — not exercised (ArchiveToSpe = false)
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
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

    // ── baseline (a): no ThreadId ⇒ the find-or-create resolver runs (unchanged expectation) ──
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
        // The email path has no explicit-target stamp branch at all (unlike Message) — never an UpdateAsync
        // that sets sprk_communicationthread directly.
        entity.Verify(
            s => s.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_communicationthread")),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── baseline (b) — THE load-bearing pre-FR-19 pin: WithThreadId STILL uses find-or-create; ThreadId is
    //    currently silently ignored on the email path (see SendCommunicationRequest.ThreadId doc comment:
    //    "Ignored for Email sends"). Mirrors CommunicationServiceMessageSendTests' explicit-target coverage for
    //    the Message channel, which behaves DIFFERENTLY (stamps directly via AssignExplicitThreadAsync). ──
    [Fact]
    public async Task SendAsync_ForEmailType_WithThreadId_StillUsesFindOrCreateResolver_ThreadIdCurrentlyIgnored()
    {
        var explicitThreadId = Guid.NewGuid();
        var entity = EntityServiceCreating();
        var threadResolver = new Mock<IThreadResolver>();
        threadResolver
            .Setup(r => r.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());

        var sut = BuildOutboundEmailSut(new RecordingEmailSender(), entity.Object, threadResolver.Object);

        var request = EmailRequest() with { ThreadId = explicitThreadId };
        var response = await sut.SendAsync(request, httpContext: null, CancellationToken.None);

        response.CommunicationId.Should().NotBeNull();

        // PRE-FR-19 BASELINE: ResolveOutboundThreadAsync (email) never reads request.ThreadId — the
        // find-or-create resolver runs exactly as it would with no ThreadId supplied at all.
        threadResolver.Verify(
            r => r.ResolveAndAssignThreadAsync(It.IsAny<ThreadResolutionRequest>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "the email path currently has no explicit-target branch — it always runs the find-or-create ladder");

        // No direct sprk_communicationthread stamp — the explicit-target path (AssignExplicitThreadAsync) is
        // Message-channel-only today; email's ThreadId value is accepted on the wire but has no effect.
        entity.Verify(
            s => s.UpdateAsync(
                "sprk_communication", It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(f => f.ContainsKey("sprk_communicationthread")),
                It.IsAny<CancellationToken>()),
            Times.Never,
            "email currently has no explicit-target stamp path — request.ThreadId is silently ignored (pre-FR-19)");
    }
}
