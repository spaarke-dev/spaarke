using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

public class ClauseAnalyzerHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ILogger<ClauseAnalyzerHandler>> _loggerMock;
    private readonly ClauseAnalyzerHandler _handler;

    public ClauseAnalyzerHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _loggerMock = new Mock<ILogger<ClauseAnalyzerHandler>>();
        _handler = new ClauseAnalyzerHandler(_openAiClientMock.Object, _loggerMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        Assert.Equal("ClauseAnalyzerHandler", _handler.HandlerId);
    }

    [Fact]
    public void SupportedToolTypes_ContainsClauseAnalyzer()
    {
        Assert.Contains(ToolType.ClauseAnalyzer, _handler.SupportedToolTypes);
    }

    [Fact]
    public void Metadata_HasCorrectName()
    {
        Assert.Equal("Clause Analyzer", _handler.Metadata.Name);
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
        Assert.Contains("clauseTypes", paramNames);
        Assert.Contains("includeRiskAssessment", paramNames);
        Assert.Contains("includeStandardComparison", paramNames);
        Assert.Contains("detectMissingClauses", paramNames);
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
        var context = CreateValidContext(extractedText: "The Supplier shall indemnify and hold harmless the Buyer against all claims.");
        var tool = CreateTool();
        var aiResponse = """
            {
              "clauses": [
                {
                  "type": "Indemnification",
                  "text": "The Supplier shall indemnify and hold harmless the Buyer against all claims.",
                  "summary": "Supplier indemnifies buyer against claims",
                  "confidence": 0.95,
                  "riskLevel": "Medium",
                  "riskReason": "Standard indemnification",
                  "deviatesFromStandard": false,
                  "deviationNotes": null
                }
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
        Assert.Contains("Found 1 clause", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoClauses_ReturnsEmptyResult()
    {
        // Arrange
        var context = CreateValidContext(extractedText: "This is not a contract document.");
        var tool = CreateTool();
        var aiResponse = """{"clauses": []}""";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Empty(data.Clauses);
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
        var aiResponse = """{"clauses": [{"type": "Indemnification", "text": "Test", "summary": "Test", "confidence": 0.9}]}""";

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
    public async Task ExecuteAsync_DetectsMissingClauses()
    {
        // Arrange
        var context = CreateValidContext(extractedText: "This contract has no standard clauses.");
        var tool = CreateTool(configuration: """{"detectMissingClauses": true}""");
        var aiResponse = """{"clauses": []}""";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.NotEmpty(data.MissingClauses);
        // Should detect that standard clause types are missing
        Assert.Contains(data.MissingClauses, m => m.Type == ClauseTypes.Indemnification);
    }

    [Fact]
    public async Task ExecuteAsync_WithDisabledMissingClauseDetection_ReturnsEmptyMissing()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"detectMissingClauses": false}""");
        var aiResponse = """{"clauses": []}""";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(aiResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Empty(data.MissingClauses);
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesDuplicateClauses()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """
            {
              "clauses": [
                {"type": "Indemnification", "text": "The Supplier shall indemnify...", "summary": "Indemnification", "confidence": 0.9},
                {"type": "Indemnification", "text": "The Supplier shall indemnify...", "summary": "Indemnification", "confidence": 0.85}
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
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Single(data.Clauses); // Deduplicated
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
              "clauses": [
                {"type": "Termination", "text": "Either party may terminate...", "summary": "Termination rights", "confidence": 0.95}
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
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Single(data.Clauses);
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
        Assert.True(result.Success); // Should succeed but with empty clauses
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Empty(data.Clauses);
    }

    [Fact]
    public async Task ExecuteAsync_SetsExecutionMetadata()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """{"clauses": []}""";

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
    public async Task ExecuteAsync_WithRiskAssessment_IncludesRiskInResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"includeRiskAssessment": true}""");
        var aiResponse = """
            {
              "clauses": [
                {
                  "type": "Indemnification",
                  "text": "Uncapped indemnification clause...",
                  "summary": "Broad indemnification",
                  "confidence": 0.9,
                  "riskLevel": "High",
                  "riskReason": "No liability cap specified"
                }
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
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Single(data.Clauses);
        Assert.Equal(RiskLevel.High, data.Clauses[0].RiskLevel);
        Assert.NotNull(data.Clauses[0].RiskReason);
    }

    [Fact]
    public async Task ExecuteAsync_GroupsClausesByType()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        var aiResponse = """
            {
              "clauses": [
                {"type": "Indemnification", "text": "First indemnification...", "summary": "Test", "confidence": 0.9},
                {"type": "Indemnification", "text": "Second indemnification...", "summary": "Test", "confidence": 0.85},
                {"type": "Termination", "text": "Termination clause...", "summary": "Test", "confidence": 0.95}
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
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Equal(3, data.TotalClausesFound);
        Assert.Equal(2, data.ClausesByType["Indemnification"]);
        Assert.Equal(1, data.ClausesByType["Termination"]);
    }

    [Fact]
    public async Task ExecuteAsync_WithStandardComparison_IncludesDeviationInfo()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"includeStandardComparison": true}""");
        var aiResponse = """
            {
              "clauses": [
                {
                  "type": "LimitationOfLiability",
                  "text": "No liability caps apply...",
                  "summary": "Unlimited liability",
                  "confidence": 0.9,
                  "deviatesFromStandard": true,
                  "deviationNotes": "Standard contracts typically include liability caps"
                }
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
        var data = result.GetData<ClauseAnalysisResult>();
        Assert.NotNull(data);
        Assert.Single(data.Clauses);
        Assert.True(data.Clauses[0].DeviatesFromStandard);
        Assert.NotNull(data.Clauses[0].DeviationNotes);
    }

    #endregion

    #region ClauseTypes Tests

    [Fact]
    public void ClauseTypes_StandardTypes_ContainsExpectedValues()
    {
        var standardTypes = ClauseTypes.StandardTypes;

        Assert.Contains("Indemnification", standardTypes);
        Assert.Contains("LimitationOfLiability", standardTypes);
        Assert.Contains("Termination", standardTypes);
        Assert.Contains("Confidentiality", standardTypes);
        Assert.Contains("DisputeResolution", standardTypes);
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(string extractedText = "This Agreement contains indemnification and liability provisions...")
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "tenant-456",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Contract",
                ExtractedText = extractedText,
                FileName = "contract.pdf"
            }
        };
    }

    private static AnalysisTool CreateTool(string? configuration = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "Clause Analyzer",
            Type = ToolType.ClauseAnalyzer,
            Configuration = configuration
        };
    }

    #endregion
}
