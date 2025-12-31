using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class DocumentClassifierHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<IRagService> _ragServiceMock;
    private readonly Mock<ILogger<DocumentClassifierHandler>> _loggerMock;
    private readonly DocumentClassifierHandler _handler;
    private readonly DocumentClassifierHandler _handlerWithRag;

    public DocumentClassifierHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _ragServiceMock = new Mock<IRagService>();
        _loggerMock = new Mock<ILogger<DocumentClassifierHandler>>();

        // Handler without RAG
        _handler = new DocumentClassifierHandler(_openAiClientMock.Object, _loggerMock.Object);

        // Handler with RAG
        _handlerWithRag = new DocumentClassifierHandler(_openAiClientMock.Object, _loggerMock.Object, _ragServiceMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        Assert.Equal("DocumentClassifierHandler", _handler.HandlerId);
    }

    [Fact]
    public void SupportedToolTypes_ContainsDocumentClassifier()
    {
        Assert.Contains(ToolType.DocumentClassifier, _handler.SupportedToolTypes);
    }

    [Fact]
    public void Metadata_HasCorrectName()
    {
        Assert.Equal("Document Classifier", _handler.Metadata.Name);
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
        Assert.Contains("categories", paramNames);
        Assert.Contains("useRagExamples", paramNames);
        Assert.Contains("ragExampleCount", paramNames);
        Assert.Contains("minConfidence", paramNames);
        Assert.Contains("includeSecondaryClassifications", paramNames);
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
    public void Validate_WithInvalidRagExampleCount_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"ragExampleCount": 10}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("ragExampleCount must be between", result.Errors.First());
    }

    [Fact]
    public void Validate_WithRagRequestedButNoService_ReturnsFailure()
    {
        // Arrange - handler without RAG service
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"useRagExamples": true}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("IRagService is not available", result.Errors.First());
    }

    [Fact]
    public void Validate_WithRagRequestedAndServiceAvailable_ReturnsSuccess()
    {
        // Arrange - handler WITH RAG service
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"useRagExamples": true}""");

        // Act
        var result = _handlerWithRag.Validate(context, tool);

        // Assert
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithEmptyCategories_ReturnsFailure()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"categories": []}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains("At least one category", result.Errors.First());
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
    public async Task ExecuteAsync_ClassifiesDocumentSuccessfully()
    {
        // Arrange
        var context = CreateValidContext(extractedText: "This Non-Disclosure Agreement is entered into between...");
        var tool = CreateTool();
        var aiResponse = """
            {
              "category": "NDA",
              "confidence": 0.95,
              "description": "Contains confidentiality obligations",
              "documentSummary": "Non-disclosure agreement between two parties",
              "secondaryClassifications": [
                {"category": "Contract", "confidence": 0.85}
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
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal("NDA", data.PrimaryCategory);
        Assert.Equal(0.95, data.PrimaryConfidence);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesSecondaryClassifications()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"includeSecondaryClassifications": true}""");
        var aiResponse = """
            {
              "category": "MSA",
              "confidence": 0.85,
              "description": "Master service agreement template",
              "secondaryClassifications": [
                {"category": "SOW", "confidence": 0.65},
                {"category": "Contract", "confidence": 0.55}
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
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.SecondaryClassifications.Count);
    }

    [Fact]
    public async Task ExecuteAsync_FiltersSecondaryByMinConfidence()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"minConfidence": 0.6}""");
        var aiResponse = """
            {
              "category": "Invoice",
              "confidence": 0.9,
              "description": "Payment document",
              "secondaryClassifications": [
                {"category": "Report", "confidence": 0.7},
                {"category": "Memo", "confidence": 0.4}
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
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Single(data.SecondaryClassifications); // Only Report (0.7) passes threshold
        Assert.Equal("Report", data.SecondaryClassifications[0].Category);
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
    public async Task ExecuteAsync_HandlesMarkdownWrappedJson()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """
            ```json
            {
              "category": "Proposal",
              "confidence": 0.88,
              "description": "Business proposal document"
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
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal("Proposal", data.PrimaryCategory);
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
        Assert.True(result.Success); // Should succeed but with Unknown classification
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal("Unknown", data.PrimaryCategory);
        Assert.Equal(0, data.PrimaryConfidence);
    }

    [Fact]
    public async Task ExecuteAsync_SetsExecutionMetadata()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """{"category": "Report", "confidence": 0.8}""";

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

    [Fact]
    public async Task ExecuteAsync_WithCustomCategories_UsesThemForClassification()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"categories": ["Legal", "Financial", "Technical", "HR"]}""");
        var aiResponse = """
            {
              "category": "Financial",
              "confidence": 0.92,
              "description": "Contains financial data and projections"
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal("Financial", data.PrimaryCategory);
    }

    [Fact]
    public async Task ExecuteAsync_WithRagExamples_IncludesExampleCount()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"useRagExamples": true, "ragExampleCount": 2}""");
        var aiResponse = """
            {
              "category": "NDA",
              "confidence": 0.95,
              "description": "Based on similar NDA documents"
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        _ragServiceMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RagSearchResponse
            {
                Results = new List<RagSearchResult>
                {
                    new RagSearchResult { Content = "Example NDA 1...", Score = 0.9, Metadata = """{"category": "NDA"}""" },
                    new RagSearchResult { Content = "Example NDA 2...", Score = 0.85, Metadata = """{"category": "NDA"}""" }
                },
                TotalCount = 2
            });

        // Act
        var result = await _handlerWithRag.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.RagExamplesUsed);
        Assert.Equal(2, result.Execution.ModelCalls); // RAG query + classification
    }

    [Fact]
    public async Task ExecuteAsync_WithRagFailure_ContinuesWithoutExamples()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"useRagExamples": true}""");
        var aiResponse = """
            {
              "category": "Report",
              "confidence": 0.75,
              "description": "General business report"
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        _ragServiceMock
            .Setup(x => x.SearchAsync(It.IsAny<string>(), It.IsAny<RagSearchOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("RAG service unavailable"));

        // Act
        var result = await _handlerWithRag.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success); // Should succeed without RAG examples
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal(0, data.RagExamplesUsed);
    }

    [Fact]
    public async Task ExecuteAsync_TruncatesLongDocuments()
    {
        // Arrange
        var longText = new string('A', 20000); // Exceeds max length
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();
        var aiResponse = """{"category": "Report", "confidence": 0.7}""";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        // Verify the prompt was called with truncated text
        _openAiClientMock.Verify(x => x.GetCompletionAsync(
            It.Is<string>(s => s.Contains("[... document truncated ...]")),
            It.IsAny<string?>(),
            It.IsAny<CancellationToken>()));
    }

    [Fact]
    public async Task ExecuteAsync_ClampsConfidenceToValidRange()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """{"category": "NDA", "confidence": 1.5}"""; // Invalid confidence

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Equal(1.0, data.PrimaryConfidence); // Clamped to max
    }

    [Fact]
    public async Task ExecuteAsync_IncludesDocumentSummary()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """
            {
              "category": "SOW",
              "confidence": 0.9,
              "description": "Statement of work for project",
              "documentSummary": "Statement of work defining deliverables for Phase 2 development"
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DocumentClassificationResult>();
        Assert.NotNull(data);
        Assert.Contains("Phase 2", data.DocumentSummary);
    }

    #endregion

    #region DocumentCategories Tests

    [Fact]
    public void DocumentCategories_ContractTypes_ContainsExpectedValues()
    {
        var contractTypes = DocumentCategories.ContractTypes;

        Assert.Contains("NDA", contractTypes);
        Assert.Contains("MSA", contractTypes);
        Assert.Contains("SOW", contractTypes);
        Assert.Contains("Amendment", contractTypes);
        Assert.Contains("SLA", contractTypes);
    }

    [Fact]
    public void DocumentCategories_BusinessTypes_ContainsExpectedValues()
    {
        var businessTypes = DocumentCategories.BusinessTypes;

        Assert.Contains("Invoice", businessTypes);
        Assert.Contains("Proposal", businessTypes);
        Assert.Contains("Report", businessTypes);
        Assert.Contains("Memo", businessTypes);
    }

    [Fact]
    public void DocumentCategories_AllStandardCategories_ContainsBothTypes()
    {
        var allCategories = DocumentCategories.AllStandardCategories;

        // Contract types
        Assert.Contains("NDA", allCategories);
        Assert.Contains("MSA", allCategories);

        // Business types
        Assert.Contains("Invoice", allCategories);
        Assert.Contains("Proposal", allCategories);
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(string extractedText = "This is a sample business document for classification testing...")
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "tenant-789",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Document",
                ExtractedText = extractedText,
                FileName = "document.pdf"
            }
        };
    }

    private static AnalysisTool CreateTool(string? configuration = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "Document Classifier",
            Type = ToolType.DocumentClassifier,
            Configuration = configuration
        };
    }

    #endregion
}
