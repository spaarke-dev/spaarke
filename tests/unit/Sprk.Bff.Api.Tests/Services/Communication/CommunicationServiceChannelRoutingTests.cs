using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
/// Verifies that <see cref="CommunicationService"/> routes the send through the
/// <see cref="ICommunicationChannelSender"/> seam (dispatched by <see cref="CommunicationType"/>) rather
/// than talking to a provider directly (NFR-04), and that the provider message id the sender returns is
/// persisted to the tracking record (FR-06). Uses a recording fake sender — no Graph.
/// </summary>
public class CommunicationServiceChannelRoutingTests
{
    private static CommunicationOptions Options() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig
            {
                Email = "noreply@contoso.com",
                DisplayName = "Contoso Notifications",
                IsDefault = true
            }
        },
        DefaultMailbox = "noreply@contoso.com"
    };

    private static CommunicationService BuildSut(
        ICommunicationChannelSender sender,
        IGenericEntityService entityService)
    {
        var options = Options();
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Microsoft.Extensions.Options.Options.Create(options),
            accountService,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var dispatcher = new CommunicationChannelDispatcher(
            new[] { sender },
            new ICommunicationArchiver[] { new EmailArchiver(new EmlGenerationService(Mock.Of<ILogger<EmlGenerationService>>())) });

        return new CommunicationService(
            dispatcher,
            senderValidator,
            Mock.Of<ICommunicationDataverseService>(),
            entityService,
            Mock.Of<IDocumentDataverseService>(),
            null!, // SpeFileStore — archival not exercised
            null!, // CommunicationAccountService — not exercised
            null!, // JobSubmissionService — not exercised
            Mock.Of<ICommunicationEnrichmentService>(),
            Microsoft.Extensions.Options.Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>());
    }

    [Fact]
    public async Task SendAsync_SharedMailbox_RoutesThroughChannelSenderSeam()
    {
        // Arrange
        var fakeSender = new RecordingChannelSender();
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var sut = BuildSut(fakeSender, entityService.Object);

        var request = new SendCommunicationRequest
        {
            To = new[] { "user@example.com" },
            Subject = "Hello",
            Body = "<p>Body</p>",
            BodyFormat = BodyFormat.HTML,
            CommunicationType = CommunicationType.Email
        };

        // Act
        var response = await sut.SendAsync(request);

        // Assert — the send was dispatched to the email sender with the resolved shared-mailbox identity
        fakeSender.LastRequest.Should().NotBeNull();
        fakeSender.LastRequest!.FromAddress.Should().Be("noreply@contoso.com");
        fakeSender.LastRequest.UserMode.Should().BeFalse();
        fakeSender.LastRequest.Communication.To.Should().ContainSingle().Which.Should().Be("user@example.com");
        response.Status.Should().Be(CommunicationStatus.Send);
        response.From.Should().Be("noreply@contoso.com");
    }

    [Fact]
    public async Task SendAsync_WhenSenderReturnsProviderMessageId_StampsInternetMessageIdOnRecord()
    {
        // Arrange
        var fakeSender = new RecordingChannelSender { ProviderMessageId = "<abc@contoso.com>" };
        var entityService = new Mock<IGenericEntityService>();
        entityService
            .Setup(s => s.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Guid.NewGuid());
        var sut = BuildSut(fakeSender, entityService.Object);

        var request = new SendCommunicationRequest
        {
            To = new[] { "user@example.com" },
            Subject = "Hello",
            Body = "<p>Body</p>",
            BodyFormat = BodyFormat.HTML,
            CommunicationType = CommunicationType.Email
        };

        // Act
        await sut.SendAsync(request);

        // Assert — the provider-captured id flows to the sprk_communication tracking record (FR-06)
        entityService.Verify(
            s => s.UpdateAsync(
                "sprk_communication",
                It.IsAny<Guid>(),
                It.Is<Dictionary<string, object>>(d => d.ContainsKey("sprk_internetmessageid")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private sealed class RecordingChannelSender : ICommunicationChannelSender
    {
        public CommunicationType SupportedType => CommunicationType.Email;
        public string? ProviderMessageId { get; init; }
        public ChannelSendRequest? LastRequest { get; private set; }

        public Task<ChannelSendResult> SendAsync(ChannelSendRequest request, CancellationToken cancellationToken = default)
        {
            LastRequest = request;
            return Task.FromResult(new ChannelSendResult
            {
                FromAddress = request.FromAddress,
                ProviderMessageId = ProviderMessageId
            });
        }
    }
}
