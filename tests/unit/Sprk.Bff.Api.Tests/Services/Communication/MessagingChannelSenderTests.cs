using Azure;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Communication.Channels;
using Sprk.Bff.Api.Services.Communication.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Behavior of <see cref="MessagingChannelSender"/> — the SECOND ADR-045 channel sender (task 020 / FR-01).
/// These protect the branched, security-relevant contract that would regress silently: (1) the sender acts
/// AS the participant (resolve identity → mint the sender's chat token → post on it), (2) the ACS message id
/// is returned as <see cref="ChannelSendResult.ProviderMessageId"/> — the echo-dedup key tasks 031/051 depend
/// on, (3) an absent thread is created via the task-011 thread plane (created/looked-up), and (4) an ACS
/// transmit failure maps to <c>CHANNEL_SEND_FAILED</c> (the ACS analog of email's <c>GRAPH_SEND_FAILED</c>).
/// The ACS SDK is mocked via the <c>TransmitAsync</c> test seam and the identity/thread planes are mocked
/// because no live ACS resource is provisioned (the task's explicit constraint) — the branches have no other
/// observable surface.
/// </summary>
public class MessagingChannelSenderTests
{
    private const string Mri = "8:acs:res_00000000-0000-0000-0000-000000000042";
    private const string ThreadId = "19:acs:thread_00000000000000000000000000000009@thread.v2";
    private const string AcsMessageId = "1700000000123";

    /// <summary>
    /// Overrides the raw-ACS test seam so no live ACS call is made; captures the args SendAsync passes and
    /// returns a canned message id (or throws to exercise the error-mapping branch).
    /// </summary>
    private sealed class TestableMessagingChannelSender : MessagingChannelSender
    {
        private readonly Func<string, string, string, string?, Task<string>> _transmit;

        public string? CapturedThreadId { get; private set; }
        public string? CapturedToken { get; private set; }
        public string? CapturedContent { get; private set; }
        public string? CapturedDisplayName { get; private set; }

        public TestableMessagingChannelSender(
            IAcsIdentityService identityService,
            IAcsThreadService threadService,
            Func<string, string, string, string?, Task<string>> transmit)
            : base(
                identityService,
                threadService,
                Options.Create(new AcsOptions { Endpoint = "https://unit.communication.azure.com" }),
                Mock.Of<ILogger<MessagingChannelSender>>())
        {
            _transmit = transmit;
        }

        protected override Task<string> TransmitAsync(
            string chatThreadId, string senderChatToken, string content, string? senderDisplayName, CancellationToken cancellationToken)
        {
            CapturedThreadId = chatThreadId;
            CapturedToken = senderChatToken;
            CapturedContent = content;
            CapturedDisplayName = senderDisplayName;
            return _transmit(chatThreadId, senderChatToken, content, senderDisplayName);
        }
    }

    private static Mock<IAcsIdentityService> IdentityReturning(string communicationUserId, string token)
    {
        var identity = new Mock<IAcsIdentityService>();
        identity
            .Setup(s => s.EnsureIdentityAsync(It.IsAny<ParticipantReference>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ParticipantReference p, CancellationToken _) => new AcsIdentityMapping
            {
                CommunicationUserId = communicationUserId,
                Participant = p,
                WasCreated = false
            });
        identity
            .Setup(s => s.MintChatTokenAsync(communicationUserId, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AcsChatToken
            {
                CommunicationUserId = communicationUserId,
                Token = token,
                ExpiresOn = DateTimeOffset.UtcNow.AddHours(24)
            });
        return identity;
    }

    private static ChannelSendRequest MessageRequest(string? acsThreadId, string subject = "Kickoff", string body = "hello there") =>
        new()
        {
            Communication = new SendCommunicationRequest
            {
                To = new[] { "recipient@contoso.com" },
                Subject = subject,
                Body = body,
                CommunicationType = CommunicationType.Message
            },
            FromAddress = "sender@spaarke.com",
            FromDisplayName = "Sender One",
            UserMode = false,
            CorrelationId = "corr-020",
            SenderParticipant = ParticipantReference.SystemUser(Guid.NewGuid()),
            AcsThreadId = acsThreadId
        };

    [Fact]
    public async Task SendAsync_WithResolvedThread_MintsSenderTokenAndReturnsAcsMessageIdAsProviderMessageId()
    {
        var identity = IdentityReturning(Mri, "sender-chat-token");
        var thread = new Mock<IAcsThreadService>(MockBehavior.Strict); // no thread create when one is supplied

        var sut = new TestableMessagingChannelSender(
            identity.Object, thread.Object,
            (_, _, _, _) => Task.FromResult(AcsMessageId));

        var result = await sut.SendAsync(MessageRequest(acsThreadId: ThreadId));

        // The ACS message id is the echo-dedup key (FR-04) surfaced as ProviderMessageId; thread flows back too.
        result.ProviderMessageId.Should().Be(AcsMessageId);
        result.ProviderThreadId.Should().Be(ThreadId);
        result.FromAddress.Should().Be(Mri);

        // Posted AS the sender: on the sender's minted token, into the supplied thread, with the body.
        sut.CapturedThreadId.Should().Be(ThreadId);
        sut.CapturedToken.Should().Be("sender-chat-token");
        sut.CapturedContent.Should().Be("hello there");
        sut.CapturedDisplayName.Should().Be("Sender One");
        identity.Verify(s => s.EnsureIdentityAsync(It.IsAny<ParticipantReference>(), It.IsAny<CancellationToken>()), Times.Once);
        identity.Verify(s => s.MintChatTokenAsync(Mri, It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithNoThread_CreatesThreadViaThreadPlaneSeededWithSenderAndSendsIntoIt()
    {
        var identity = IdentityReturning(Mri, "tok");
        var thread = new Mock<IAcsThreadService>();
        IEnumerable<string>? seededParticipants = null;
        string? capturedTopic = null;
        thread
            .Setup(t => t.CreateThreadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
            .Callback<string, IEnumerable<string>, TimeSpan?, CancellationToken>((topic, parts, _, _) =>
            {
                capturedTopic = topic;
                seededParticipants = parts.ToList();
            })
            .ReturnsAsync(new AcsThreadCreateResult { ChatThreadId = ThreadId, RetentionDays = 30 });

        var sut = new TestableMessagingChannelSender(
            identity.Object, thread.Object,
            (_, _, _, _) => Task.FromResult(AcsMessageId));

        var result = await sut.SendAsync(MessageRequest(acsThreadId: null, subject: "Matter 42"));

        thread.Verify(t => t.CreateThreadAsync(It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()), Times.Once);
        capturedTopic.Should().Be("Matter 42");
        seededParticipants.Should().ContainSingle().Which.Should().Be(Mri);
        result.ProviderThreadId.Should().Be(ThreadId);
        sut.CapturedThreadId.Should().Be(ThreadId);
    }

    [Fact]
    public async Task SendAsync_WithoutSenderParticipant_ThrowsChannelSendFailed()
    {
        var identity = new Mock<IAcsIdentityService>(MockBehavior.Strict);
        var thread = new Mock<IAcsThreadService>(MockBehavior.Strict);
        var sut = new TestableMessagingChannelSender(
            identity.Object, thread.Object,
            (_, _, _, _) => Task.FromResult(AcsMessageId));

        var request = MessageRequest(acsThreadId: ThreadId) with { SenderParticipant = null };

        var act = () => sut.SendAsync(request);

        (await act.Should().ThrowAsync<SdapProblemException>())
            .Which.Code.Should().Be("CHANNEL_SEND_FAILED");
    }

    [Fact]
    public async Task SendAsync_WhenAcsTransmitFails_MapsRequestFailedToChannelSendFailed()
    {
        var identity = IdentityReturning(Mri, "tok");
        var thread = new Mock<IAcsThreadService>();

        var sut = new TestableMessagingChannelSender(
            identity.Object, thread.Object,
            (_, _, _, _) => throw new RequestFailedException(502, "ACS unavailable"));

        var act = () => sut.SendAsync(MessageRequest(acsThreadId: ThreadId));

        var problem = (await act.Should().ThrowAsync<SdapProblemException>()).Which;
        problem.Code.Should().Be("CHANNEL_SEND_FAILED");
        problem.StatusCode.Should().Be(502);
    }
}
