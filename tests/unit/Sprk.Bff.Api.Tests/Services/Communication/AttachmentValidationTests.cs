using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using System.Net;
using System.Text;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

/// <summary>
/// Tests for attachment validation in CommunicationService.
/// Uses the same constructor pattern as CommunicationServiceTests
/// with null! for EmlGenerationService and SpeFileStore when not testing those features.
/// </summary>
public class AttachmentValidationTests
{
    #region Test Infrastructure

    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<ILogger<CommunicationService>> _loggerMock;

    public AttachmentValidationTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _loggerMock = new Mock<ILogger<CommunicationService>>();

        // Default: ForApp() returns a mock Graph client that succeeds (202 Accepted)
        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient());
    }

    private static CommunicationOptions CreateDefaultOptions() => new()
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

    private CommunicationService CreateService(CommunicationOptions? options = null)
    {
        var opts = options ?? CreateDefaultOptions();
        var accountService = new CommunicationAccountService(
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<CommunicationAccountService>>());
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(opts),
            accountService,
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        return new CommunicationService(
            _graphClientFactoryMock.Object,
            senderValidator,
            Mock.Of<IDataverseService>(),
            null!, // EmlGenerationService — not tested here
            null!, // SpeFileStore — not tested here
            Options.Create(opts),
            _loggerMock.Object);
    }

    private static SendCommunicationRequest CreateValidRequest(
        string[]? attachmentDocumentIds = null) => new()
    {
        To = new[] { "recipient@example.com" },
        Subject = "Test Subject",
        Body = "<p>Test body</p>",
        BodyFormat = BodyFormat.HTML,
        CommunicationType = CommunicationType.Email,
        AttachmentDocumentIds = attachmentDocumentIds
    };

    private static GraphServiceClient CreateMockGraphClient(
        HttpStatusCode responseCode = HttpStatusCode.Accepted)
    {
        var handler = new MockHttpMessageHandler(responseCode);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
    }

    private class MockHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string? _responseContent;

        public MockHttpMessageHandler(HttpStatusCode statusCode, string? responseContent = null)
        {
            _statusCode = statusCode;
            _responseContent = responseContent;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(_statusCode);
            if (_responseContent is not null)
            {
                response.Content = new StringContent(_responseContent, Encoding.UTF8, "application/json");
            }
            return Task.FromResult(response);
        }
    }

    #endregion

    #region Null and Empty AttachmentDocumentIds

    [Fact]
    public async Task SendAsync_WithNullAttachmentDocumentIds_SucceedsNormally()
    {
        // Arrange
        var sut = CreateService();
        var request = CreateValidRequest(attachmentDocumentIds: null);

        // Act
        var response = await sut.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send);
        response.AttachmentCount.Should().Be(0);
    }

    [Fact]
    public async Task SendAsync_WithEmptyAttachmentDocumentIds_SucceedsNormally()
    {
        // Arrange
        var sut = CreateService();
        var request = CreateValidRequest(attachmentDocumentIds: Array.Empty<string>());

        // Act
        var response = await sut.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send);
        response.AttachmentCount.Should().Be(0);
    }

    #endregion
}
