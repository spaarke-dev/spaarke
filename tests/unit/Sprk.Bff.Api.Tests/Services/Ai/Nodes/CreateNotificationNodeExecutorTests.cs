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

    #region FR-18: Visible vs Hidden Toast data.actions[] Tests (P3 — Native MDA bell deep-links)

    /// <summary>
    /// FR-18 (standard path, visible toast): When actionUrl is supplied and toasttype is the
    /// default (Timed = 200000000, visible), the appnotification.data payload MUST contain a
    /// single-entry actions array [{ title: "Open", data: { url: actionUrl } }] AND
    /// customData.actionUrl populated.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_StandardPath_VisibleToast_PopulatesDataActionsAndCustomData()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        const string actionUrl = "/main.aspx?pagetype=entityrecord&etn=sprk_document&id=" +
                                 "00000000-0000-0000-0000-000000000001";
        var config = JsonSerializer.Serialize(new
        {
            title = "New document uploaded",
            body = "Document summary",
            actionUrl,
            recipientId = recipientId.ToString()
            // toasttype omitted → defaults to DefaultToastType (200000000 = Timed, visible)
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        SetupCreateNotificationReturns(notificationId);
        var capturedPostBody = CapturePostBody();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var postedPayload = capturedPostBody.Value
            ?? throw new InvalidOperationException("POST body was not captured");
        using var doc = JsonDocument.Parse(postedPayload);
        var root = doc.RootElement;

        root.TryGetProperty("data", out var dataProperty).Should().BeTrue(
            "FR-18: visible-toast notifications populate appnotification.data");
        var dataString = dataProperty.GetString();
        dataString.Should().NotBeNullOrEmpty();

        using var dataDoc = JsonDocument.Parse(dataString!);
        var dataRoot = dataDoc.RootElement;

        // (a) data.actions[0].data.url == actionUrl
        dataRoot.TryGetProperty("actions", out var actionsArr).Should().BeTrue(
            "FR-18: visible-toast notifications populate data.actions");
        actionsArr.ValueKind.Should().Be(JsonValueKind.Array);
        actionsArr.GetArrayLength().Should().Be(1, "FR-18 specifies a single-entry actions array");
        var firstAction = actionsArr[0];
        firstAction.GetProperty("title").GetString().Should().Be("Open");
        firstAction.GetProperty("data").GetProperty("url").GetString().Should().Be(actionUrl);

        // (c) customData.actionUrl populated (regardless of toasttype)
        dataRoot.TryGetProperty("customData", out var customData).Should().BeTrue();
        customData.GetProperty("actionUrl").GetString().Should().Be(actionUrl);
    }

    /// <summary>
    /// FR-18 (standard path, hidden toast): When actionUrl is supplied but toasttype == Hidden
    /// (100000000), data.actions MUST be null/absent (no visible bell surface to render the
    /// "Open" action). customData.actionUrl MUST still be populated (consumed by the Daily
    /// Briefing UI, not the MDA bell).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_StandardPath_HiddenToast_OmitsDataActionsButKeepsCustomData()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        const string actionUrl = "/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=" +
                                 "00000000-0000-0000-0000-000000000002";
        var config = JsonSerializer.Serialize(new
        {
            title = "Hidden notification",
            body = "For Daily Briefing UI only — no MDA bell render",
            actionUrl,
            toasttype = 100_000_000, // Hidden
            recipientId = recipientId.ToString()
        });
        var context = CreateValidContext(config);

        SetupMockPassThrough();
        SetupCreateNotificationReturns(notificationId);
        var capturedPostBody = CapturePostBody();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();

        var postedPayload = capturedPostBody.Value
            ?? throw new InvalidOperationException("POST body was not captured");
        using var doc = JsonDocument.Parse(postedPayload);
        var root = doc.RootElement;

        // toasttype = Hidden propagated
        root.GetProperty("toasttype").GetInt32().Should().Be(100_000_000);

        root.TryGetProperty("data", out var dataProperty).Should().BeTrue(
            "FR-18: customData still serialized for hidden-toast notifications");
        var dataString = dataProperty.GetString();
        dataString.Should().NotBeNullOrEmpty();

        using var dataDoc = JsonDocument.Parse(dataString!);
        var dataRoot = dataDoc.RootElement;

        // (b) data.actions null/absent — hidden toasts skip actions[] (no MDA bell render surface)
        dataRoot.TryGetProperty("actions", out _).Should().BeFalse(
            "FR-18: hidden-toast notifications MUST NOT populate data.actions[]");

        // (c) customData.actionUrl populated (Daily Briefing UI consumer)
        dataRoot.TryGetProperty("customData", out var customData).Should().BeTrue();
        customData.GetProperty("actionUrl").GetString().Should().Be(actionUrl);
    }

    /// <summary>
    /// FR-18 (iterateItems path, visible toast): The same data.actions[] population rule MUST
    /// apply when notifications are created in iterate-items mode (one per upstream query item).
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_IterateItems_VisibleToast_PopulatesDataActionsAndCustomData()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var itemRegardingId = Guid.NewGuid();

        // Hand-build the iterate-items config: upstream "items" output + per-item template.
        // Note: actionUrl is rendered through the template engine (which is mocked pass-through).
        // Top-level title/body are required by Validate() (line 96-100) even though
        // ExecuteIterateItemsAsync ultimately renders from itemNotification.
        var iterateConfig = new
        {
            title = "(parent placeholder — iterate uses itemNotification)",
            body = "(parent placeholder)",
            iterateItems = true,
            itemNotification = new
            {
                title = "Item: {{item.name}}",
                body = "Body for item",
                actionUrl = "/main.aspx?pagetype=entityrecord&etn=sprk_document&id=" +
                            itemRegardingId.ToString(),
                recipientId = recipientId.ToString()
                // toasttype omitted → defaults to visible (DefaultToastType)
            }
        };
        var config = JsonSerializer.Serialize(iterateConfig);
        var context = CreateIterateContext(config, itemRegardingId);

        SetupMockPassThrough();
        SetupCreateNotificationReturns(notificationId);
        var capturedPostBody = CapturePostBody();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("Created 1 notifications");

        var postedPayload = capturedPostBody.Value
            ?? throw new InvalidOperationException("POST body was not captured");
        using var doc = JsonDocument.Parse(postedPayload);
        var root = doc.RootElement;

        root.TryGetProperty("data", out var dataProperty).Should().BeTrue();
        using var dataDoc = JsonDocument.Parse(dataProperty.GetString()!);
        var dataRoot = dataDoc.RootElement;

        // (a) iterateItems path: data.actions[0].data.url is the rendered actionUrl
        dataRoot.TryGetProperty("actions", out var actionsArr).Should().BeTrue(
            "FR-18: iterateItems visible-toast notifications populate data.actions");
        actionsArr.GetArrayLength().Should().Be(1);
        actionsArr[0].GetProperty("title").GetString().Should().Be("Open");
        actionsArr[0].GetProperty("data").GetProperty("url").GetString()
            .Should().Contain(itemRegardingId.ToString());

        // (c) customData.actionUrl populated on iterateItems path
        dataRoot.TryGetProperty("customData", out var customData).Should().BeTrue();
        customData.GetProperty("actionUrl").GetString().Should().Contain(itemRegardingId.ToString());
    }

    /// <summary>
    /// FR-18 (iterateItems path, hidden toast): For hidden-toast iterate-items notifications,
    /// data.actions MUST be null/absent and customData.actionUrl MUST still be populated.
    /// </summary>
    [Fact]
    public async Task ExecuteAsync_IterateItems_HiddenToast_OmitsDataActionsButKeepsCustomData()
    {
        // Arrange
        var recipientId = Guid.NewGuid();
        var notificationId = Guid.NewGuid();
        var itemRegardingId = Guid.NewGuid();

        var iterateConfig = new
        {
            title = "(parent placeholder)",
            body = "(parent placeholder)",
            iterateItems = true,
            itemNotification = new
            {
                title = "Item: {{item.name}}",
                body = "Body for item",
                actionUrl = "/main.aspx?pagetype=entityrecord&etn=sprk_matter&id=" +
                            itemRegardingId.ToString(),
                toasttype = 100_000_000, // Hidden
                recipientId = recipientId.ToString()
            }
        };
        var config = JsonSerializer.Serialize(iterateConfig);
        var context = CreateIterateContext(config, itemRegardingId);

        SetupMockPassThrough();
        SetupCreateNotificationReturns(notificationId);
        var capturedPostBody = CapturePostBody();

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("Created 1 notifications");

        var postedPayload = capturedPostBody.Value
            ?? throw new InvalidOperationException("POST body was not captured");
        using var doc = JsonDocument.Parse(postedPayload);
        var root = doc.RootElement;

        root.GetProperty("toasttype").GetInt32().Should().Be(100_000_000);

        root.TryGetProperty("data", out var dataProperty).Should().BeTrue();
        using var dataDoc = JsonDocument.Parse(dataProperty.GetString()!);
        var dataRoot = dataDoc.RootElement;

        // (b) iterateItems hidden-toast: actions MUST be absent
        dataRoot.TryGetProperty("actions", out _).Should().BeFalse(
            "FR-18: iterateItems hidden-toast notifications MUST NOT populate data.actions[]");

        // (c) customData.actionUrl populated
        dataRoot.TryGetProperty("customData", out var customData).Should().BeTrue();
        customData.GetProperty("actionUrl").GetString().Should().Contain(itemRegardingId.ToString());
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

    /// <summary>
    /// Builds a NodeExecutionContext for iterate-items mode by attaching a synthetic
    /// upstream PreviousOutput whose StructuredData contains an "items" array with a single
    /// item — sufficient to exercise the iterate path while keeping the test focused on
    /// FR-18's data.actions[] vs customData semantics.
    /// </summary>
    private static NodeExecutionContext CreateIterateContext(string configJson, Guid itemRegardingId)
    {
        var baseContext = CreateValidContext(configJson);

        // Synthesize an upstream query result: { items: [ { id, name } ] }
        var upstream = NodeOutput.Ok(
            nodeId: Guid.NewGuid(),
            outputVariable: "query",
            data: new
            {
                items = new[]
                {
                    new
                    {
                        id = itemRegardingId.ToString(),
                        name = "Test Item"
                    }
                }
            },
            textContent: "1 item");

        return baseContext with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput>
            {
                ["query"] = upstream
            }
        };
    }

    /// <summary>
    /// Wires up the idempotency-check GET (returns no duplicate) AND captures the body of
    /// the next POST to /appnotifications. Returns a single-value holder the test reads
    /// after Act. Avoids the brittleness of mock.Verify(...).Callback by exposing the
    /// captured JSON as plain text.
    /// </summary>
    private CapturedBody CapturePostBody()
    {
        SetupIdempotencyCheckReturnsNoDuplicate();

        var captured = new CapturedBody();
        _httpHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == HttpMethod.Post),
                ItExpr.IsAny<CancellationToken>())
            .Returns(async (HttpRequestMessage request, CancellationToken ct) =>
            {
                captured.Value = request.Content is not null
                    ? await request.Content.ReadAsStringAsync(ct)
                    : null;

                var response = new HttpResponseMessage(HttpStatusCode.NoContent);
                response.Headers.Add(
                    "OData-EntityId",
                    $"https://org.crm.dynamics.com/api/data/v9.2/appnotifications({Guid.NewGuid()})");
                return response;
            });

        return captured;
    }

    private sealed class CapturedBody
    {
        public string? Value { get; set; }
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
