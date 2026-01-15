using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai.Nodes;

/// <summary>
/// Unit tests for DeliverOutputNodeExecutor.
/// Tests validation, template rendering, and multiple output formats.
/// </summary>
public class DeliverOutputNodeExecutorTests
{
    private readonly Mock<ITemplateEngine> _templateEngineMock;
    private readonly Mock<ILogger<DeliverOutputNodeExecutor>> _loggerMock;
    private readonly DeliverOutputNodeExecutor _executor;

    public DeliverOutputNodeExecutorTests()
    {
        _templateEngineMock = new Mock<ITemplateEngine>();
        _loggerMock = new Mock<ILogger<DeliverOutputNodeExecutor>>();
        _executor = new DeliverOutputNodeExecutor(
            _templateEngineMock.Object,
            _loggerMock.Object);
    }

    #region SupportedActionTypes Tests

    [Fact]
    public void SupportedActionTypes_ContainsDeliverOutput()
    {
        // Assert
        _executor.SupportedActionTypes.Should().Contain(ActionType.DeliverOutput);
        _executor.SupportedActionTypes.Should().HaveCount(1);
    }

    #endregion

    #region Validate Tests

    [Fact]
    public void Validate_WithValidJsonConfig_ReturnsSuccess()
    {
        // Arrange - JSON type doesn't require template
        var config = @"{""deliveryType"":""json""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Validate_WithValidTextConfig_ReturnsSuccess()
    {
        // Arrange
        var config = @"{""deliveryType"":""text"",""template"":""Hello {{name}}""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithValidMarkdownConfig_ReturnsSuccess()
    {
        // Arrange
        var config = @"{""deliveryType"":""markdown"",""template"":""# {{title}}""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WithValidHtmlConfig_ReturnsSuccess()
    {
        // Arrange
        var config = @"{""deliveryType"":""html"",""template"":""<h1>{{title}}</h1>""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeTrue();
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
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext("{invalid json}");

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid"));
    }

    [Fact]
    public void Validate_WithMissingDeliveryType_ReturnsFailure()
    {
        // Arrange
        var config = @"{""template"":""Hello {{name}}""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Delivery type") || e.Contains("deliveryType"));
    }

    [Fact]
    public void Validate_WithInvalidDeliveryType_ReturnsFailure()
    {
        // Arrange
        var config = @"{""deliveryType"":""pdf"",""template"":""Content""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid delivery type"));
    }

    [Fact]
    public void Validate_WithTextTypeAndMissingTemplate_ReturnsFailure()
    {
        // Arrange
        var config = @"{""deliveryType"":""text""}";
        var context = CreateValidContext(config);

        // Act
        var result = _executor.Validate(context);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Template"));
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithJsonType_ReturnsStructuredOutput()
    {
        // Arrange
        var config = @"{""deliveryType"":""json""}";
        var previousOutput = NodeOutput.Ok(
            Guid.NewGuid(),
            "analysis",
            new { summary = "Document summary", count = 3 });

        var context = CreateValidContext(config);
        context = context with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput>
            {
                ["analysis"] = previousOutput
            }
        };

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_WithTextType_RendersTemplate()
    {
        // Arrange
        var config = @"{""deliveryType"":""text"",""template"":""Summary: {{analysis.output.summary}}""}";
        var context = CreateValidContext(config);

        SetupMockWithResponse("Summary: Document analyzed successfully");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Be("Summary: Document analyzed successfully");

        var data = result.GetData<DeliveryOutput>();
        data.Should().NotBeNull();
        data!.Format.Should().Be("text");
    }

    [Fact]
    public async Task ExecuteAsync_WithMarkdownType_RendersTemplate()
    {
        // Arrange
        var config = @"{""deliveryType"":""markdown"",""template"":""# {{title}}\n\n{{content}}""}";
        var context = CreateValidContext(config);

        SetupMockWithResponse("# Analysis Report\n\nThis is the content.");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("# Analysis Report");

        var data = result.GetData<DeliveryOutput>();
        data!.Format.Should().Be("markdown");
    }

    [Fact]
    public async Task ExecuteAsync_WithHtmlType_RendersTemplate()
    {
        // Arrange
        var config = @"{""deliveryType"":""html"",""template"":""<h1>{{title}}</h1>""}";
        var context = CreateValidContext(config);

        SetupMockWithResponse("<h1>Analysis Complete</h1>");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("<h1>");

        var data = result.GetData<DeliveryOutput>();
        data!.Format.Should().Be("html");
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxLength_TruncatesOutput()
    {
        // Arrange
        var config = @"{
            ""deliveryType"":""text"",
            ""template"":""{{longContent}}"",
            ""outputFormat"":{""maxLength"":20}
        }";
        var context = CreateValidContext(config);

        SetupMockWithResponse("This is a very long text that should be truncated");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().HaveLength(34); // 20 + "...(truncated)".Length
        result.TextContent.Should().EndWith("...(truncated)");
    }

    [Fact]
    public async Task ExecuteAsync_WithIncludeMetadata_AddsMetadata()
    {
        // Arrange
        var config = @"{
            ""deliveryType"":""json"",
            ""outputFormat"":{""includeMetadata"":true}
        }";
        var context = CreateValidContext(config);

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.TextContent.Should().Contain("_metadata");
    }

    [Fact]
    public async Task ExecuteAsync_WithValidationFailure_ReturnsErrorOutput()
    {
        // Arrange
        var context = CreateValidContext("{}");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(NodeErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task ExecuteAsync_WithPreviousOutputs_BuildsTemplateContext()
    {
        // Arrange
        var config = @"{
            ""deliveryType"":""text"",
            ""template"":""Summary: {{analysis.output.summary}}\nEntities: {{entities.output.count}}""
        }";
        var analysisOutput = NodeOutput.Ok(
            Guid.NewGuid(),
            "analysis",
            new { summary = "Contract reviewed" });
        var entitiesOutput = NodeOutput.Ok(
            Guid.NewGuid(),
            "entities",
            new { count = 5 });

        var context = CreateValidContext(config);
        context = context with
        {
            PreviousOutputs = new Dictionary<string, NodeOutput>
            {
                ["analysis"] = analysisOutput,
                ["entities"] = entitiesOutput
            }
        };

        SetupMockWithResponse("Summary: Contract reviewed\nEntities: 5");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_WithJsonTypeAndTemplate_RendersCustomJson()
    {
        // Arrange
        var config = "{\"deliveryType\":\"json\",\"template\":\"{\\\"summary\\\": \\\"{{analysis.output.summary}}\\\"}\"}";
        var context = CreateValidContext(config);

        SetupMockWithResponse("{\"summary\": \"Test summary\"}");

        // Act
        var result = await _executor.ExecuteAsync(context, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private void SetupMockWithResponse(string response)
    {
        // Mock both overloads - compiler chooses generic version for Dictionary<string, object?>
        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<IDictionary<string, object?>>()))
            .Returns(response);

        _templateEngineMock
            .Setup(t => t.Render(It.IsAny<string>(), It.IsAny<Dictionary<string, object?>>()))
            .Returns(response);
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
                Name = "Deliver Output Node",
                ExecutionOrder = 1,
                OutputVariable = "deliveryResult",
                ConfigJson = configJson,
                IsActive = true
            },
            Action = new AnalysisAction
            {
                Id = actionId,
                Name = "Deliver Output"
            },
            ActionType = ActionType.DeliverOutput,
            Scopes = new ResolvedScopes([], [], []),
            TenantId = "test-tenant"
        };
    }

    private class DeliveryOutput
    {
        public string Content { get; set; } = string.Empty;
        public string Format { get; set; } = string.Empty;
    }

    #endregion
}
