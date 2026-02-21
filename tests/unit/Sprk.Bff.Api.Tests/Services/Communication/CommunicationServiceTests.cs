using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models.ODataErrors;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Communication.Models;
using System.Net;
using System.Text;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Communication;

public class CommunicationServiceTests
{
    #region Test Infrastructure

    private readonly Mock<IGraphClientFactory> _graphClientFactoryMock;
    private readonly Mock<ILogger<CommunicationService>> _loggerMock;
    private readonly CommunicationService _sut;

    /// <summary>
    /// Default test setup: one approved sender configured, Graph client returns 202 Accepted.
    /// </summary>
    public CommunicationServiceTests()
    {
        _graphClientFactoryMock = new Mock<IGraphClientFactory>();
        _loggerMock = new Mock<ILogger<CommunicationService>>();

        var options = CreateDefaultOptions();
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        // Default: ForApp() returns a mock Graph client that succeeds (202 Accepted)
        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient());

        _sut = new CommunicationService(
            _graphClientFactoryMock.Object,
            senderValidator,
            Mock.Of<IDataverseService>(),
            null!, // EmlGenerationService — not tested here
            null!, // SpeFileStore — not tested here
            Options.Create(options),
            _loggerMock.Object);
    }

    #endregion

    #region Test Data Builders

    private static CommunicationOptions CreateDefaultOptions() => new()
    {
        ApprovedSenders = new[]
        {
            new ApprovedSenderConfig
            {
                Email = "noreply@contoso.com",
                DisplayName = "Contoso Notifications",
                IsDefault = true
            },
            new ApprovedSenderConfig
            {
                Email = "support@contoso.com",
                DisplayName = "Contoso Support"
            }
        },
        DefaultMailbox = "noreply@contoso.com"
    };

    private static SendCommunicationRequest CreateValidRequest(
        string[]? to = null,
        string subject = "Test Subject",
        string body = "<p>Test body</p>",
        string? fromMailbox = null,
        string? correlationId = null) => new()
    {
        To = to ?? new[] { "recipient@example.com" },
        Subject = subject,
        Body = body,
        BodyFormat = BodyFormat.HTML,
        FromMailbox = fromMailbox,
        CommunicationType = CommunicationType.Email,
        CorrelationId = correlationId
    };

    private static GraphServiceClient CreateMockGraphClient(
        HttpStatusCode responseCode = HttpStatusCode.Accepted)
    {
        var handler = new MockHttpMessageHandler(responseCode);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
    }

    private static GraphServiceClient CreateFailingGraphClient()
    {
        var errorJson = "{\"error\":{\"code\":\"Authorization_RequestDenied\",\"message\":\"Access denied\"}}";
        var handler = new MockHttpMessageHandler(HttpStatusCode.Forbidden, errorJson);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
    }

    /// <summary>
    /// Creates a CommunicationService with a custom options configuration (e.g., no senders).
    /// </summary>
    private CommunicationService CreateServiceWithOptions(CommunicationOptions options)
    {
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());
        return new CommunicationService(
            _graphClientFactoryMock.Object,
            senderValidator,
            Mock.Of<IDataverseService>(),
            null!, // EmlGenerationService — not tested here
            null!, // SpeFileStore — not tested here
            Options.Create(options),
            _loggerMock.Object);
    }

    /// <summary>
    /// Mock HTTP handler that returns a configurable response for Graph SDK testing.
    /// </summary>
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

    #region Validation Errors

    [Fact]
    public async Task SendAsync_WithNoRecipients_ThrowsValidationError()
    {
        // Arrange
        var request = CreateValidRequest(to: Array.Empty<string>());

        // Act
        var act = () => _sut.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("VALIDATION_ERROR");
        ex.Which.Detail.Should().Contain("recipient");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SendAsync_WithBlankSubject_ThrowsValidationError()
    {
        // Arrange
        var request = CreateValidRequest(subject: "   ");

        // Act
        var act = () => _sut.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("VALIDATION_ERROR");
        ex.Which.Detail.Should().Contain("Subject");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SendAsync_WithBlankBody_ThrowsValidationError()
    {
        // Arrange
        var request = CreateValidRequest(body: "");

        // Act
        var act = () => _sut.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("VALIDATION_ERROR");
        ex.Which.Detail.Should().Contain("Body");
        ex.Which.StatusCode.Should().Be(400);
    }

    #endregion

    #region Sender Resolution Errors

    [Fact]
    public async Task SendAsync_WithInvalidSender_ThrowsInvalidSenderError()
    {
        // Arrange
        var request = CreateValidRequest(fromMailbox: "unauthorized@evil.com");

        // Act
        var act = () => _sut.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("INVALID_SENDER");
        ex.Which.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task SendAsync_WithNoDefaultSender_ThrowsNoDefaultSenderError()
    {
        // Arrange — service with no approved senders configured
        var emptyOptions = new CommunicationOptions
        {
            ApprovedSenders = Array.Empty<ApprovedSenderConfig>()
        };
        var service = CreateServiceWithOptions(emptyOptions);
        var request = CreateValidRequest(fromMailbox: null);

        // Act
        var act = () => service.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("NO_DEFAULT_SENDER");
        ex.Which.StatusCode.Should().Be(400);
    }

    #endregion

    #region Graph Client Errors

    [Fact]
    public async Task SendAsync_WhenGraphClientThrows_ThrowsGraphSendFailedError()
    {
        // Arrange — Graph client returns 403 Forbidden with OData error body
        _graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateFailingGraphClient());

        var request = CreateValidRequest();

        // Act
        var act = () => _sut.SendAsync(request);

        // Assert
        var ex = await act.Should().ThrowAsync<SdapProblemException>();
        ex.Which.Code.Should().Be("GRAPH_SEND_FAILED");
        ex.Which.Title.Should().Be("Email Send Failed");
    }

    #endregion

    #region Successful Send

    [Fact]
    public async Task SendAsync_WithValidRequest_ReturnsSuccessResponse()
    {
        // Arrange
        var request = CreateValidRequest(
            to: new[] { "user@example.com" },
            subject: "Hello",
            body: "<p>World</p>",
            correlationId: "test-corr-123");

        // Act
        var response = await _sut.SendAsync(request);

        // Assert
        response.Should().NotBeNull();
        response.Status.Should().Be(CommunicationStatus.Send);
        response.From.Should().Be("noreply@contoso.com");
        response.CorrelationId.Should().Be("test-corr-123");
        response.GraphMessageId.Should().Be("test-corr-123");
        response.SentAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        response.CommunicationId.Should().BeNull("Phase 1 does not create Dataverse records");
    }

    #endregion

    #region Correlation ID Handling

    [Fact]
    public async Task SendAsync_SetsCorrectCorrelationId_WhenProvided()
    {
        // Arrange
        var request = CreateValidRequest(correlationId: "my-custom-correlation-id");

        // Act
        var response = await _sut.SendAsync(request);

        // Assert
        response.CorrelationId.Should().Be("my-custom-correlation-id");
    }

    [Fact]
    public async Task SendAsync_GeneratesCorrelationId_WhenNotProvided()
    {
        // Arrange
        var request = CreateValidRequest(correlationId: null);

        // Act
        var response = await _sut.SendAsync(request);

        // Assert
        response.CorrelationId.Should().NotBeNullOrWhiteSpace();
        response.CorrelationId!.Length.Should().Be(32, "auto-generated correlation IDs use Guid.ToString(\"N\") which produces 32 hex chars");
    }

    #endregion
}
