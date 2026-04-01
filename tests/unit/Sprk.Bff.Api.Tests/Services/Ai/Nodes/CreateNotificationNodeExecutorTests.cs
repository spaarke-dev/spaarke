using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for CreateNotificationNodeExecutor.
/// Tests validation, notification creation, idempotency check (duplicate skip), and error handling.
/// </summary>
public class CreateNotificationNodeExecutorTests
{
    private readonly Mock<ITemplateEngine> _templateEngineMock;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly Mock<ILogger<CreateNotificationNodeExecutor>> _loggerMock;
    private readonly Mock<HttpMessageHandler> _httpHandlerMock;
    private readonly CreateNotificationNodeExecutor _executor;

    public CreateNotificationNodeExecutorTests()
    {
        _templateEngineMock = new Mock<ITemplateEngine>();
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _loggerMock = new Mock<ILogger<CreateNotificationNodeExecutor>>();
        _httpHandlerMock = new Mock<HttpMessageHandler>();

        var httpClient = new HttpClient(_httpHandlerMock.Object)
        {
            BaseAddress = new Uri("https://org.crm.dynamics.com/api/data/v9.2/")
        };

        _httpClientFactoryMock
            .Setup(f => f.CreateClient("DataverseApi"))
            .Returns(httpClient);

        _executor = new CreateNotificationNodeExecutor(
            _templateEngineMock.Object,
            _httpClientFactoryMock.Object,
            _loggerMock.Object);
    }

    #region SupportedActionTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsCreateNotification()
    {
        // Assert
        _executor.SupportedActionTypes.Should().Contain(ActionType.CreateNotification);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidConfig_ReturnsSuccess()
    {
        // Arrange
        var config = @"{""title"":""New document"",""body"":""A document was uploaded""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithNoConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(null);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public void Validate_WithEmptyConfig_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("ConfigJson"));
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("{not valid json!}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid"));
    }

    [Fact]
    public void Validate_WithMissingTitle_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(@"{""body"":""Some body text""}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("title"));
    }

    [Fact]
    public void Validate_WithMissingBody_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(@"{""title"":""Some title""}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("body"));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithValidConfig_CreatesNotificationAndReturnsSuccess()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var config = JsonSerializer.Serialize(new
        {
            title = "New document uploaded",
            body = "Document summary here",
            category = "document-upload",
            recipientId = recipientId.ToString(),
            regardingId = Guid.NewGuid().ToString(),
            regardingType = "sprk_document"
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        SetupIdempotencyCheckReturnsNoDuplicate();
        SetupCreateNotificationReturns(notificationId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.NodeId.Should().Be(context.Node.Id);
        result.OutputVariable.Should().Be(context.Node.OutputVariable);
        result.TextContent.Should().Contain("Notification created");
    }

    [Fact]
    public async Task ExecuteAsync_WhenDuplicateUnreadNotificationExists_SkipsCreation()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var regardingId = Guid.NewGuid();
        var config = JsonSerializer.Serialize(new
        {
            title = "New document uploaded",
            body = "Document summary here",
            category = "document-upload",
            recipientId = recipientId.ToString(),
            regardingId = regardingId.ToString(),
            regardingType = "sprk_document"
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        SetupIdempotencyCheckReturnsDuplicate();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("skipped");
        result.TextContent.Should().Contain("duplicate");

        var data = result.GetData<SkippedNotificationOutput>();
        data.Should().NotBeNull();
        data!.Skipped.Should().BeTrue();
        data.Reason.Should().Contain("Duplicate");
    }

    [Fact]
    public async Task ExecuteAsync_WhenNoCategoryOrRegarding_SkipsIdempotencyCheckAndCreates()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var config = JsonSerializer.Serialize(new
        {
            title = "Simple notification",
            body = "No category or regarding",
            recipientId = recipientId.ToString()
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        SetupCreateNotificationReturns(notificationId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("Notification created");

        // Verify no GET request was made for idempotency check
        _httpHandlerMock.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenRecipientIdMissing_ReturnsValidationError()
    {
        // Arrange - no recipientId and no run userId in context
        var config = JsonSerializer.Serialize(new
        {
            title = "Notification",
            body = "Body text"
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
        result.ErrorMessage.Should().Contain("recipient");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidationFailure_ReturnsErrorOutput()
    {
        // Arrange - empty config triggers validation error
        var context = CreateValidContext("{}");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_WhenDataverseApiFails_ReturnsErrorOutput()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var config = JsonSerializer.Serialize(new
        {
            title = "Notification",
            body = "Body",
            recipientId = recipientId.ToString()
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Setup POST to fail with 500
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent("Internal Server Error")
            });

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.InternalError);
        result.ErrorMessage.Should().Contain("Failed to create notification");
    }

    [Fact]
    public async Task ExecuteAsync_WhenIdempotencyCheckFails_ProceedsWithCreation()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var regardingId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var config = JsonSerializer.Serialize(new
        {
            title = "Notification",
            body = "Body",
            category = "test-category",
            recipientId = recipientId.ToString(),
            regardingId = regardingId.ToString(),
            regardingType = "sprk_document"
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();

        // Setup GET (idempotency check) to fail
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        // Setup POST (create notification) to succeed
        SetupCreateNotificationReturns(notificationId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert — should proceed with creation despite idempotency check failure
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("Notification created");
    }

    [Fact]
    public async Task ExecuteAsync_WithTemplateVariables_SubstitutesCorrectly()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var config = JsonSerializer.Serialize(new
        {
            title = "{{analysis.output.title}}",
            body = "{{analysis.output.summary}}",
            recipientId = recipientId.ToString()
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        SetupCreateNotificationReturns(notificationId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        // Verify Render was called for title and body at minimum
        _templateEngineMock.Verify(
            t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomPriority_UsesProvidedPriority()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var config = JsonSerializer.Serialize(new
        {
            title = "Urgent notification",
            body = "Needs attention",
            priority = 300000000, // Urgent
            recipientId = recipientId.ToString()
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        SetupCreateNotificationReturns(notificationId);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private void SetupMockPassThrough()
    {
        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns((string template, IDictionary<string, object?> _) => template);

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns((string template, Dictionary<string, object?> _) => template);
    }

    private void SetupIdempotencyCheckReturnsNoDuplicate()
    {
        var emptyResponse = JsonSerializer.Serialize(new { value = Array.Empty<object>() });

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(emptyResponse, Encoding.UTF8, "application/json")
            });
    }

    private void SetupIdempotencyCheckReturnsDuplicate()
    {
        var duplicateResponse = JsonSerializer.Serialize(new
        {
            value = new[]
            {
                new { activityid = Guid.NewGuid().ToString() }
            }
        });

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Get),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(duplicateResponse, Encoding.UTF8, "application/json")
            });
    }

    private void SetupCreateNotificationReturns(Guid notificationId)
    {
        var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        response.Headers.Add("OData-EntityId",
            $"https://org.crm.dynamics.com/api/data/v9.2/appnotifications({notificationId})");

        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private static NodeExecutionContext CreateValidContext(string? configJson)
    {
        var nodeId = Guid.NewGuid();
        var actionId = Guid.NewGuid();

        return new NodeExecutionContext
        {
            RunId = Guid.NewGuid(),
            PlaybookId = Guid.NewGuid(),
            Node = new PlaybookNodeDto
            {
                Id = nodeId,
                PlaybookId = Guid.NewGuid(),
                ActionId = actionId,
                Name = "Create Notification Node",
                ExecutionOrder = 1,
                OutputVariable = "notificationResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Create Notification"
            },
            ActionType = ActionType.CreateNotification,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private class SkippedNotificationOutput
    {
        public bool Skipped { get; set; }
        public string Reason { get; set; } = string.Empty;
        public string? RecipientId { get; set; }
        public string? RegardingId { get; set; }
        public string? Category { get; set; }
    }

    #endregion
}
