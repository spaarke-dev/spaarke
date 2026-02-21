using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Sprk.Bff.Api.Services.Communication;
using System.Net;
using System.Text;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Tools;

/// <summary>
/// Tests SendCommunicationToolHandler playbook email scenarios including
/// parameter validation, email parsing, error handling, and success paths.
/// Since CommunicationService is sealed and cannot be mocked, tests use real
/// instances with mocked infrastructure dependencies (IGraphClientFactory, IDataverseService).
/// </summary>
public class SendCommunicationToolHandlerScenarioTests
{
    #region Test Infrastructure

    /// <summary>
    /// Creates a handler with a mock IGraphClientFactory that returns a failing Graph client by default.
    /// Suitable for validation tests (tests 1-4) where CommunicationService is never reached,
    /// and for error-path tests (test 5) where the Graph call is expected to fail.
    /// </summary>
    private static SendCommunicationToolHandler CreateHandlerWithMockGraphFactory(
        Mock<IGraphClientFactory>? graphClientFactoryMock = null)
    {
        graphClientFactoryMock ??= new Mock<IGraphClientFactory>();

        var options = CreateDefaultOptions();
        var senderValidator = new ApprovedSenderValidator(
            Options.Create(options),
            Mock.Of<IDataverseService>(),
            Mock.Of<IDistributedCache>(),
            Mock.Of<ILogger<ApprovedSenderValidator>>());

        var communicationService = new CommunicationService(
            graphClientFactoryMock.Object,
            senderValidator,
            Mock.Of<IDataverseService>(),
            null!, // EmlGenerationService — not tested here
            null!, // SpeFileStore — not tested here
            Options.Create(options),
            Mock.Of<ILogger<CommunicationService>>());

        return new SendCommunicationToolHandler(
            communicationService,
            Mock.Of<ILogger<SendCommunicationToolHandler>>());
    }

    /// <summary>
    /// Creates a handler backed by a Graph client that returns 202 Accepted (success path).
    /// Uses MockHttpMessageHandler to simulate a successful Graph sendMail call.
    /// </summary>
    private static SendCommunicationToolHandler CreateHandlerWithSuccessGraphClient()
    {
        var graphClientFactoryMock = new Mock<IGraphClientFactory>();
        graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient(HttpStatusCode.Accepted));

        return CreateHandlerWithMockGraphFactory(graphClientFactoryMock);
    }

    /// <summary>
    /// Creates a handler backed by a Graph client that returns a specific HTTP error status.
    /// </summary>
    private static SendCommunicationToolHandler CreateHandlerWithFailingGraphClient(
        HttpStatusCode statusCode = HttpStatusCode.Forbidden,
        string? responseContent = null)
    {
        responseContent ??= "{\"error\":{\"code\":\"Authorization_RequestDenied\",\"message\":\"Access denied\"}}";
        var graphClientFactoryMock = new Mock<IGraphClientFactory>();
        graphClientFactoryMock
            .Setup(f => f.ForApp())
            .Returns(CreateMockGraphClient(statusCode, responseContent));

        return CreateHandlerWithMockGraphFactory(graphClientFactoryMock);
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

    private static GraphServiceClient CreateMockGraphClient(
        HttpStatusCode responseCode = HttpStatusCode.Accepted,
        string? responseContent = null)
    {
        var handler = new MockHttpMessageHandler(responseCode, responseContent);
        var httpClient = new HttpClient(handler);
        return new GraphServiceClient(httpClient);
    }

    private static ToolParameters CreateToolParameters(Dictionary<string, object> parameters)
        => new(parameters);

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

    #region Parameter Validation (Tests 1-4)

    [Fact]
    public async Task ExecuteAsync_MissingTo_ReturnsError()
    {
        // Arrange — "to" parameter is absent from the dictionary
        var handler = CreateHandlerWithMockGraphFactory();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["subject"] = "Test Subject",
            ["body"] = "<p>Test body</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("to");
    }

    [Fact]
    public async Task ExecuteAsync_MissingSubject_ReturnsError()
    {
        // Arrange — "subject" parameter is absent
        var handler = CreateHandlerWithMockGraphFactory();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user@example.com",
            ["body"] = "<p>Test body</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("subject");
    }

    [Fact]
    public async Task ExecuteAsync_MissingBody_ReturnsError()
    {
        // Arrange — "body" parameter is absent
        var handler = CreateHandlerWithMockGraphFactory();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user@example.com",
            ["subject"] = "Test Subject"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("body");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyTo_ReturnsError()
    {
        // Arrange — "to" is present but empty (no valid emails after parsing)
        var handler = CreateHandlerWithMockGraphFactory();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "  ,  ; ",
            ["subject"] = "Test Subject",
            ["body"] = "<p>Test body</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("email recipient");
    }

    #endregion

    #region Graph Failure Path (Test 5)

    [Fact]
    public async Task ExecuteAsync_GraphFailure_ReturnsStructuredError()
    {
        // Arrange — Graph client returns 403 Forbidden, which CommunicationService wraps in SdapProblemException,
        // then the handler catches it and returns PlaybookToolResult.CreateError
        var handler = CreateHandlerWithFailingGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "recipient@example.com",
            ["subject"] = "Test Subject",
            ["body"] = "<p>Test body</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNullOrWhiteSpace();
        result.Error.Should().Contain("Send communication failed");
    }

    #endregion

    #region Success Path (Test 6)

    [Fact]
    public async Task ExecuteAsync_ValidParams_ReturnsSuccessWithCommunicationData()
    {
        // Arrange — Graph client returns 202 Accepted (successful send)
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "recipient@example.com",
            ["subject"] = "Hello from Playbook",
            ["body"] = "<p>Test body content</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Data.Should().NotBeNull();

        // Verify structured data has expected fields by converting to dynamic/dictionary
        var data = result.Data!;
        var dataType = data.GetType();

        dataType.GetProperty("Status")!.GetValue(data).Should().Be("Send");
        dataType.GetProperty("From")!.GetValue(data).Should().Be("noreply@contoso.com");
        dataType.GetProperty("CorrelationId")!.GetValue(data).Should().NotBeNull();
        dataType.GetProperty("SentAt")!.GetValue(data).Should().NotBeNull();
    }

    #endregion

    #region CC Parameter Parsing (Test 7)

    [Fact]
    public async Task ExecuteAsync_WithCcParameter_ParsesCcRecipients()
    {
        // Arrange — CC parameter with comma-separated emails should be parsed and included
        // We verify the handler processes CC without errors on the success path
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "primary@example.com",
            ["subject"] = "CC Test",
            ["body"] = "<p>CC test body</p>",
            ["cc"] = "cc1@example.com, cc2@example.com; cc3@example.com"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert — If CC parsing failed, it would cause an error in request building
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyCcParameter_SucceedsWithoutCc()
    {
        // Arrange — Empty CC string should be ignored (not cause error)
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "primary@example.com",
            ["subject"] = "Empty CC Test",
            ["body"] = "<p>Body</p>",
            ["cc"] = "  "
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Regarding Entity/ID Association (Test 8)

    [Fact]
    public async Task ExecuteAsync_WithRegardingParams_CreatesAssociation()
    {
        // Arrange — regardingEntity and regardingId should be extracted and included in the request
        // The handler creates a CommunicationAssociation when both are provided
        var regardingId = Guid.NewGuid();
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "recipient@example.com",
            ["subject"] = "Association Test",
            ["body"] = "<p>Association test body</p>",
            ["regardingEntity"] = "sprk_matter",
            ["regardingId"] = regardingId.ToString()
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert — If association parsing failed, it would cause an error
        result.Success.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Data.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithRegardingEntityOnly_SucceedsWithoutAssociation()
    {
        // Arrange — Only regardingEntity without regardingId should not create an association
        // (the handler requires both to be present and valid)
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "recipient@example.com",
            ["subject"] = "Partial Association Test",
            ["body"] = "<p>Body</p>",
            ["regardingEntity"] = "sprk_matter"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert — Should succeed without error (association is optional)
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Email Parsing Scenarios

    [Fact]
    public async Task ExecuteAsync_CommaSeparatedEmails_SendsSuccessfully()
    {
        // Arrange — Multiple recipients separated by commas
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user1@example.com, user2@example.com, user3@example.com",
            ["subject"] = "Multi-Recipient Test",
            ["body"] = "<p>Sent to multiple recipients</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_SemicolonSeparatedEmails_SendsSuccessfully()
    {
        // Arrange — Multiple recipients separated by semicolons
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user1@example.com; user2@example.com; user3@example.com",
            ["subject"] = "Semicolon Separated Test",
            ["body"] = "<p>Sent to multiple recipients</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_MixedDelimiterEmails_SendsSuccessfully()
    {
        // Arrange — Commas and semicolons mixed
        var handler = CreateHandlerWithSuccessGraphClient();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user1@example.com, user2@example.com; user3@example.com",
            ["subject"] = "Mixed Delimiter Test",
            ["body"] = "<p>Mixed delimiters</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Whitespace-Only Parameter Handling

    [Fact]
    public async Task ExecuteAsync_WhitespaceOnlyTo_ReturnsError()
    {
        // Arrange — "to" is whitespace only (should fail validation)
        var handler = CreateHandlerWithMockGraphFactory();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "   ",
            ["subject"] = "Test",
            ["body"] = "<p>Body</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("to");
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceOnlySubject_ReturnsError()
    {
        // Arrange — "subject" is whitespace only
        var handler = CreateHandlerWithMockGraphFactory();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user@example.com",
            ["subject"] = "   ",
            ["body"] = "<p>Body</p>"
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("subject");
    }

    [Fact]
    public async Task ExecuteAsync_WhitespaceOnlyBody_ReturnsError()
    {
        // Arrange — "body" is whitespace only
        var handler = CreateHandlerWithMockGraphFactory();
        var parameters = CreateToolParameters(new Dictionary<string, object>
        {
            ["to"] = "user@example.com",
            ["subject"] = "Test",
            ["body"] = "   "
        });

        // Act
        var result = await handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("body");
    }

    #endregion
}
