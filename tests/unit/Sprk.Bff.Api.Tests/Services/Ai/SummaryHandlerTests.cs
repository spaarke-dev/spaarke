using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for SummaryHandler.
/// Validates handler properties, validation logic, and execution behavior.
/// </summary>
public class SummaryHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ILogger<SummaryHandler>> _loggerMock;
    private readonly SummaryHandler _handler;

    public SummaryHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _loggerMock = new Mock<ILogger<SummaryHandler>>();
        _handler = new SummaryHandler(_openAiClientMock.Object, _loggerMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        Assert.Equal("SummaryHandler", _handler.HandlerId);
    }

    [Fact]
    public void SupportedToolTypes_ContainsSummary()
    {
        Assert.Contains(ToolType.Summary, _handler.SupportedToolTypes);
    }

    [Fact]
    public void SupportedToolTypes_ContainsExactlyOneType()
    {
        Assert.Single(_handler.SupportedToolTypes);
    }

    [Fact]
    public void Metadata_HasExpectedName()
    {
        Assert.Equal("Summary Generator", _handler.Metadata.Name);
    }

    [Fact]
    public void Metadata_HasExpectedVersion()
    {
        Assert.Equal("1.0.0", _handler.Metadata.Version);
    }

    [Fact]
    public void Metadata_SupportsMultipleInputTypes()
    {
        Assert.Contains("text/plain", _handler.Metadata.SupportedInputTypes);
        Assert.Contains("application/pdf", _handler.Metadata.SupportedInputTypes);
        Assert.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml.document", _handler.Metadata.SupportedInputTypes);
    }

    [Fact]
    public void Metadata_HasParameterDefinitions()
    {
        var parameters = _handler.Metadata.Parameters;
        Assert.NotEmpty(parameters);

        var maxLengthParam = parameters.FirstOrDefault(p => p.Name == "max_length");
        Assert.NotNull(maxLengthParam);
        Assert.Equal(ToolParameterType.Integer, maxLengthParam.Type);
        Assert.False(maxLengthParam.Required);
        Assert.Equal(500, maxLengthParam.DefaultValue);

        var formatParam = parameters.FirstOrDefault(p => p.Name == "format");
        Assert.NotNull(formatParam);
        Assert.Equal(ToolParameterType.String, formatParam.Type);
        Assert.Equal("structured", formatParam.DefaultValue);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Validate_WithNullDocument_ReturnsFailure()
    {
        var context = CreateContextWithNullDocument();
        var tool = CreateTool();

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("Document context is required.", result.Errors);
    }

    [Fact]
    public void Validate_WithEmptyExtractedText_ReturnsFailure()
    {
        var context = CreateValidContext(extractedText: "");
        var tool = CreateTool();

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("Document extracted text is required for summarization.", result.Errors);
    }

    [Fact]
    public void Validate_WithWhitespaceExtractedText_ReturnsFailure()
    {
        var context = CreateValidContext(extractedText: "   ");
        var tool = CreateTool();

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("Document extracted text is required for summarization.", result.Errors);
    }

    [Fact]
    public void Validate_WithNullTenantId_ReturnsFailure()
    {
        var context = CreateValidContext(tenantId: null!);
        var tool = CreateTool();

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("TenantId is required.", result.Errors);
    }

    [Fact]
    public void Validate_WithValidContext_ReturnsSuccess()
    {
        var context = CreateValidContext();
        var tool = CreateTool();

        var result = _handler.Validate(context, tool);

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithValidConfiguration_ReturnsSuccess()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxLength": 300, "format": "bullets"}""");

        var result = _handler.Validate(context, tool);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithMaxLengthTooSmall_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxLength": 10}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("max_length must be between 50 and 5000 words.", result.Errors);
    }

    [Fact]
    public void Validate_WithMaxLengthTooLarge_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxLength": 10000}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("max_length must be between 50 and 5000 words.", result.Errors);
    }

    [Fact]
    public void Validate_WithInvalidFormat_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"format": "unknown"}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("format must be 'paragraph', 'bullets', or 'structured'.", result.Errors);
    }

    [Theory]
    [InlineData("paragraph")]
    [InlineData("bullets")]
    [InlineData("structured")]
    public void Validate_WithValidFormats_ReturnsSuccess(string format)
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: $$$"""{"format": "{{{format}}}"}""");

        var result = _handler.Validate(context, tool);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: "not valid json");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Single(result.Errors);
        Assert.StartsWith("Invalid configuration JSON:", result.Errors.First());
    }

    [Fact]
    public void Validate_WithEmptyConfiguration_ReturnsSuccess()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: "");

        var result = _handler.Validate(context, tool);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithNullConfiguration_ReturnsSuccess()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: null);

        var result = _handler.Validate(context, tool);

        Assert.True(result.IsValid);
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithSmallDocument_ProcessesSingleChunk()
    {
        // Arrange
        var context = CreateValidContext(extractedText: "This is a short test document for summarization.");
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("## Executive Summary\n\nThis is the summary.");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("SummaryHandler", result.HandlerId);
        Assert.Equal(tool.Id, result.ToolId);
        Assert.Equal(tool.Name, result.ToolName);
        Assert.NotNull(result.Data);
        Assert.Equal("## Executive Summary\n\nThis is the summary.", result.Summary);
        Assert.Equal(0.9, result.Confidence);

        // Verify single model call
        Assert.Equal(1, result.Execution.ModelCalls);
        Assert.Equal("gpt-4o-mini", result.Execution.ModelName);
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeDocument_ProcessesMultipleChunks()
    {
        // Arrange - Create document larger than 8000 characters
        var longText = string.Join(" ", Enumerable.Repeat("This is a test sentence for the document.", 300));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        var callCount = 0;
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return $"Summary of chunk {callCount}";
            });

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Execution.ModelCalls > 1, "Large documents should require multiple model calls");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        using var cts = new CancellationTokenSource();

        // Mock the OpenAI client to throw when cancellation is requested
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((_, _, ct) => ct.ThrowIfCancellationRequested())
            .ThrowsAsync(new OperationCanceledException());

        cts.Cancel();

        // Act
        var result = await _handler.ExecuteAsync(context, tool, cts.Token);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Cancelled, result.ErrorCode);
        Assert.Contains("cancelled", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WhenOpenAiThrows_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("OpenAI service unavailable"));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InternalError, result.ErrorCode);
        Assert.Contains("OpenAI service unavailable", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_ReturnsExecutionMetadata()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Test summary response");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Execution);
        Assert.True(result.Execution.CompletedAt >= result.Execution.StartedAt);
        Assert.True(result.Execution.Duration >= TimeSpan.Zero);
        Assert.NotNull(result.Execution.InputTokens);
        Assert.NotNull(result.Execution.OutputTokens);
        Assert.True(result.Execution.InputTokens > 0);
        Assert.True(result.Execution.OutputTokens > 0);
    }

    [Fact]
    public async Task ExecuteAsync_WithStructuredFormat_ExtractsSections()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"format": "structured"}""");

        var structuredResponse = """
            ## Executive Summary
            This is the executive summary.

            ## Key Terms
            - Term 1: Definition 1
            - Term 2: Definition 2

            ## Obligations
            - Party A must do X
            - Party B must do Y

            ## Notable Provisions
            - Important clause 1
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(structuredResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);

        var data = result.GetData<SummaryResult>();
        Assert.NotNull(data);
        Assert.Equal("structured", data.Format);
        Assert.NotNull(data.Sections);
        Assert.True(data.Sections.Count > 0);
        Assert.True(data.Sections.ContainsKey("Executive Summary"));
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesWordCount()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = "This is a test summary with exactly eleven words in it.";
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<SummaryResult>();
        Assert.NotNull(data);
        Assert.Equal(11, data.WordCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithParagraphFormat_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"format": "paragraph"}""");

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is a paragraph summary.");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<SummaryResult>();
        Assert.NotNull(data);
        Assert.Equal("paragraph", data.Format);
    }

    [Fact]
    public async Task ExecuteAsync_WithBulletsFormat_ReturnsCorrectFormat()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"format": "bullets"}""");

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("- Point 1\n- Point 2\n- Point 3");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<SummaryResult>();
        Assert.NotNull(data);
        Assert.Equal("bullets", data.Format);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesPromptConfiguration()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxLength": 300, "usePlainLanguage": true, "highlightTimeSensitive": true}""");

        string capturedPrompt = "";
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
            .ReturnsAsync("Test summary");

        // Act
        await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.Contains("300", capturedPrompt);
        Assert.Contains("plain language", capturedPrompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("time-sensitive", capturedPrompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithKnowledgeContext_IncludesInProcessing()
    {
        // Arrange
        var context = CreateValidContext(knowledgeContext: "This is additional context from knowledge base.");
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary with context");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithUserContext_IncludesInProcessing()
    {
        // Arrange
        var context = CreateValidContext(userContext: "Focus on the financial terms and deadlines.");
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Focused summary");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    #endregion

    #region Multi-Chunk Processing Tests

    [Fact]
    public async Task ExecuteAsync_WithMultipleChunks_SynthesizesFinalSummary()
    {
        // Arrange - Create a very long document
        var longText = string.Join(". ", Enumerable.Range(1, 500).Select(i => $"This is sentence number {i} in the document"));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        var responses = new Queue<string>(new[]
        {
            "Summary of section 1",
            "Summary of section 2",
            "Final synthesized summary combining all sections"
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => responses.Count > 0 ? responses.Dequeue() : "Fallback summary");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        _openAiClientMock.Verify(
            x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleChunks_AccumulatesTokenCounts()
    {
        // Arrange
        var longText = string.Join(" ", Enumerable.Repeat("This is test content for chunking.", 400));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Chunk summary text");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Execution.InputTokens);
        Assert.NotNull(result.Execution.OutputTokens);
        Assert.True(result.Execution.InputTokens > 100, "Multi-chunk processing should accumulate tokens");
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellationDuringMultiChunk_StopsProcessing()
    {
        // Arrange - Create a very long document to ensure multiple chunks (>8000 chars per chunk)
        var longText = string.Join(" ", Enumerable.Repeat("This is a longer test sentence that will help create multiple chunks for the document.", 200));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        var callCount = 0;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<string, string?, CancellationToken>((_, _, _) =>
            {
                callCount++;
                if (callCount == 2)
                {
                    // Simulate cancellation during the second chunk
                    throw new OperationCanceledException();
                }
                return Task.FromResult("Chunk summary");
            });

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Cancelled, result.ErrorCode);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ExecuteAsync_WithMinimalConfiguration_UsesDefaults()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: "{}");

        string capturedPrompt = "";
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
            .ReturnsAsync("Default summary");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("500", capturedPrompt); // Default max length
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptySummaryResponse_ReturnsSuccess()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert - Empty response is still technically a success (AI returned something)
        Assert.True(result.Success);
        var data = result.GetData<SummaryResult>();
        Assert.NotNull(data);
        Assert.Equal(0, data.WordCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithSpecialCharactersInDocument_ProcessesSuccessfully()
    {
        // Arrange
        var textWithSpecialChars = "Document with special chars: $100,000 @ 5% interest. <script>alert('test')</script> & more...";
        var context = CreateValidContext(extractedText: textWithSpecialChars);
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary of document with special characters");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnicodeDocument_ProcessesSuccessfully()
    {
        // Arrange
        var unicodeText = "Document with unicode: 日本語 العربية 中文 🔒 📄";
        var context = CreateValidContext(extractedText: unicodeText);
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Summary of multilingual document");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(
        string extractedText = "This is a test document with content for summarization.",
        string tenantId = "test-tenant-id",
        string? knowledgeContext = null,
        string? userContext = null)
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = tenantId,
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Document",
                FileName = "test-document.pdf",
                ContentType = "application/pdf",
                ExtractedText = extractedText
            },
            KnowledgeContext = knowledgeContext,
            UserContext = userContext,
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

    private static ToolExecutionContext CreateContextWithNullDocument()
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "test-tenant-id",
            Document = null!
        };
    }

    private static AnalysisTool CreateTool(
        string name = "Test Summary Tool",
        string? configuration = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Test tool for summarization",
            Type = ToolType.Summary,
            Configuration = configuration
        };
    }

    #endregion
}
