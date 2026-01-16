using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class EntityExtractorHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ITextChunkingService> _textChunkingServiceMock;
    private readonly Mock<ILogger<EntityExtractorHandler>> _loggerMock;
    private readonly EntityExtractorHandler _handler;

    public EntityExtractorHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _textChunkingServiceMock = new Mock<ITextChunkingService>();
        _loggerMock = new Mock<ILogger<EntityExtractorHandler>>();
        _handler = new EntityExtractorHandler(_openAiClientMock.Object, _textChunkingServiceMock.Object, _loggerMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        Assert.Equal("EntityExtractorHandler", _handler.HandlerId);
    }

    [Fact]
    public void SupportedToolTypes_ContainsEntityExtractor()
    {
        Assert.Contains(ToolType.EntityExtractor, _handler.SupportedToolTypes);
    }

    [Fact]
    public void Metadata_HasCorrectName()
    {
        Assert.Equal("Entity Extractor", _handler.Metadata.Name);
    }

    [Fact]
    public void Metadata_HasCorrectVersion()
    {
        Assert.Equal("1.0.0", _handler.Metadata.Version);
    }

    [Fact]
    public void Metadata_SupportsExpectedInputTypes()
    {
        Assert.Contains("text/plain", _handler.Metadata.SupportedInputTypes);
        Assert.Contains("application/pdf", _handler.Metadata.SupportedInputTypes);
    }

    [Fact]
    public void Metadata_HasExpectedParameters()
    {
        var paramNames = _handler.Metadata.Parameters.Select(p => p.Name).ToList();
        Assert.Contains("entityTypes", paramNames);
        Assert.Contains("minConfidence", paramNames);
        Assert.Contains("chunkSize", paramNames);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Validate_WithNullDocument_ReturnsFailure()
    {
        // Arrange
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Document = null!
        };
        var tool = CreateTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Document context is required", result.Errors.First());
    }

    [Fact]
    public void Validate_WithEmptyExtractedText_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext(extractedText: "");
        var tool = CreateTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("extracted text is required", result.Errors.First());
    }

    [Fact]
    public void Validate_WithMissingTenantId_ReturnsFailure()
    {
        // Arrange
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Document",
                ExtractedText = "Sample text"
            }
        };
        var tool = CreateTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("TenantId is required", result.Errors.First());
    }

    [Fact]
    public void Validate_WithValidContext_ReturnsSuccess()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithInvalidMinConfidence_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"minConfidence": 1.5}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("minConfidence must be between", result.Errors.First());
    }

    [Fact]
    public void Validate_WithTooSmallChunkSize_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"chunkSize": 100}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("chunkSize must be at least 500", result.Errors.First());
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: "not-valid-json");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("Invalid configuration JSON", result.Errors.First());
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithSmallDocument_ProcessesSingleChunk()
    {
        // Arrange
        var context = CreateValidContext(extractedText: "John Smith works at Acme Corp.");
        var tool = CreateTool();
        var aiResponse = """
            {
              "entities": [
                {"value": "John Smith", "type": "Person", "confidence": 0.95, "context": "John Smith works at..."},
                {"value": "Acme Corp", "type": "Organization", "confidence": 0.9, "context": "...works at Acme Corp"}
              ]
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains("Found 2 entities", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoEntities_ReturnsEmptyResult()
    {
        // Arrange
        var context = CreateValidContext(extractedText: "No named entities here.");
        var tool = CreateTool();
        var aiResponse = """{"entities": []}""";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("No entities were found", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var cts = new CancellationTokenSource();
        cts.Cancel();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        // Act
        var result = await _handler.ExecuteAsync(context, tool, cts.Token);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithAiError_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("AI service unavailable"));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InternalError, result.ErrorCode);
        Assert.Contains("AI service unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeDocument_ProcessesMultipleChunks()
    {
        // Arrange
        var largeText = new string('A', 20000); // Exceeds default chunk size
        var context = CreateValidContext(extractedText: largeText);
        var tool = CreateTool();
        var aiResponse = """{"entities": [{"value": "Test", "type": "Person", "confidence": 0.9, "context": "test context"}]}""";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        // Should have called AI multiple times for multiple chunks
        _openAiClientMock.Verify(
            x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_FiltersEntitiesByMinConfidence()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"minConfidence": 0.8}""");
        var aiResponse = """
            {
              "entities": [
                {"value": "High Confidence", "type": "Person", "confidence": 0.95, "context": "test"},
                {"value": "Low Confidence", "type": "Person", "confidence": 0.5, "context": "test"}
              ]
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<EntityExtractionResult>();
        Assert.NotNull(data);
        Assert.Single(data.Entities);
        Assert.Equal("High Confidence", data.Entities.First().Value);
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesDuplicateEntities()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """
            {
              "entities": [
                {"value": "John Smith", "type": "Person", "confidence": 0.9, "context": "first mention"},
                {"value": "john smith", "type": "Person", "confidence": 0.85, "context": "second mention"}
              ]
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<EntityExtractionResult>();
        Assert.NotNull(data);
        Assert.Single(data.Entities); // Deduplicated
        Assert.Equal(2, data.Entities.First().Occurrences); // Aggregated count
    }

    [Fact]
    public async Task ExecuteAsync_HandlesMarkdownWrappedJson()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """
            ```json
            {
              "entities": [
                {"value": "Test Entity", "type": "Person", "confidence": 0.95, "context": "test"}
              ]
            }
            ```
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<EntityExtractionResult>();
        Assert.NotNull(data);
        Assert.Single(data.Entities);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesInvalidAiResponse()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = "This is not valid JSON at all";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success); // Should succeed but with empty entities
        var data = result.GetData<EntityExtractionResult>();
        Assert.NotNull(data);
        Assert.Empty(data.Entities);
    }

    [Fact]
    public async Task ExecuteAsync_SetsExecutionMetadata()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """{"entities": []}""";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Execution.StartedAt > DateTimeOffset.MinValue);
        Assert.True(result.Execution.CompletedAt >= result.Execution.StartedAt);
        Assert.Equal(1, result.Execution.ModelCalls);
        Assert.Equal("gpt-4o-mini", result.Execution.ModelName);
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(string extractedText = "Sample document text with John Smith from Acme Corp.")
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "tenant-456",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Document",
                ExtractedText = extractedText,
                FileName = "test.pdf"
            }
        };
    }

    private static AnalysisTool CreateTool(string? configuration = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "Entity Extractor",
            Type = ToolType.EntityExtractor,
            Configuration = configuration
        };
    }

    #endregion
}
