using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for ClauseComparisonHandler.
/// Tests comparison logic, deviation detection, and severity classification.
/// </summary>
public class ClauseComparisonHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ITextChunkingService> _textChunkingServiceMock;
    private readonly Mock<ILogger<ClauseComparisonHandler>> _loggerMock;
    private readonly ClauseComparisonHandler _handler;

    public ClauseComparisonHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _textChunkingServiceMock = new Mock<ITextChunkingService>();
        _loggerMock = new Mock<ILogger<ClauseComparisonHandler>>();
        _handler = new ClauseComparisonHandler(_openAiClientMock.Object, _textChunkingServiceMock.Object, _loggerMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        Assert.Equal("ClauseComparisonHandler", _handler.HandlerId);
    }

    [Fact]
    public void SupportedToolTypes_ContainsClauseComparison()
    {
        Assert.Contains(ToolType.ClauseComparison, _handler.SupportedToolTypes);
    }

    [Fact]
    public void SupportedToolTypes_ContainsExactlyOneType()
    {
        Assert.Single(_handler.SupportedToolTypes);
    }

    [Fact]
    public void Metadata_HasExpectedName()
    {
        Assert.Equal("Clause Comparison", _handler.Metadata.Name);
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
    }

    [Fact]
    public void Metadata_HasParameterDefinitions()
    {
        var parameters = _handler.Metadata.Parameters;
        Assert.NotEmpty(parameters);

        var thresholdParam = parameters.FirstOrDefault(p => p.Name == "deviation_threshold");
        Assert.NotNull(thresholdParam);
        Assert.Equal(ToolParameterType.String, thresholdParam.Type);
        Assert.Equal("minor", thresholdParam.DefaultValue);

        var maxDeviationsParam = parameters.FirstOrDefault(p => p.Name == "max_deviations");
        Assert.NotNull(maxDeviationsParam);
        Assert.Equal(ToolParameterType.Integer, maxDeviationsParam.Type);
        Assert.Equal(30, maxDeviationsParam.DefaultValue);
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
        Assert.Contains("Document extracted text is required for clause comparison.", result.Errors);
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
    public void Validate_WithMaxDeviationsTooSmall_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxDeviations": 0}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("max_deviations must be between 1 and 100.", result.Errors);
    }

    [Fact]
    public void Validate_WithMaxDeviationsTooLarge_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxDeviations": 150}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("max_deviations must be between 1 and 100.", result.Errors);
    }

    [Fact]
    public void Validate_WithInvalidThreshold_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"deviationThreshold": "critical"}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("deviation_threshold must be 'minor', 'moderate', or 'significant'.", result.Errors);
    }

    [Theory]
    [InlineData("minor")]
    [InlineData("moderate")]
    [InlineData("significant")]
    public void Validate_WithValidThresholds_ReturnsSuccess(string threshold)
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: $$$"""{"deviationThreshold": "{{{threshold}}}"}""");

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
        Assert.StartsWith("Invalid configuration JSON:", result.Errors.First());
    }

    #endregion

    #region ExecuteAsync Tests

    [Fact]
    public async Task ExecuteAsync_WithSmallDocument_ProcessesSingleChunk()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = CreateDeviationResponse(new[]
        {
            ("LimitationOfLiability", "Significant", "No liability cap", "Mutual cap expected", 0.95)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("ClauseComparisonHandler", result.HandlerId);
        Assert.Equal(1, result.Execution.ModelCalls);
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeDocument_ProcessesMultipleChunks()
    {
        // Arrange
        var longText = string.Join(" ", Enumerable.Repeat("This contract contains various clauses for comparison analysis.", 200));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateDeviationResponse(new[] { ("General", "Minor", "Test", "Test", 0.8) }));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Execution.ModelCalls > 1);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsSignificantDeviations()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = CreateDeviationResponse(new[]
        {
            ("LimitationOfLiability", "Significant", "Unlimited liability", "Cap expected", 0.95),
            ("Indemnification", "Significant", "One-sided indemnity", "Mutual expected", 0.90)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.SignificantCount);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsMixedSeverityDeviations()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = CreateDeviationResponse(new[]
        {
            ("LimitationOfLiability", "Significant", "High risk", "Standard", 0.95),
            ("PaymentTerms", "Moderate", "Medium risk", "Standard", 0.85),
            ("Notices", "Minor", "Low risk", "Standard", 0.75)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.Equal(3, data.TotalDeviationsFound);
        Assert.Equal(1, data.SignificantCount);
        Assert.Equal(1, data.ModerateCount);
        Assert.Equal(1, data.MinorCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoDeviations_ReturnsCleanResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"deviations": [], "clausesAnalyzed": [{"clauseType": "LimitationOfLiability", "found": true}]}""");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.Equal(0, data.TotalDeviationsFound);
        Assert.Contains("No deviations", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();
        using var cts = new CancellationTokenSource();

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
    }

    [Fact]
    public async Task ExecuteAsync_WhenOpenAiThrows_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service unavailable"));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InternalError, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesRecommendations()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"includeRecommendations": true}""");

        var response = CreateDeviationResponseWithRecommendations(new[]
        {
            ("LimitationOfLiability", "Significant", "No cap", "Cap expected", 0.9, "Negotiate liability cap")
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.NotEmpty(data.Deviations);
        Assert.NotNull(data.Deviations[0].Recommendation);
    }

    #endregion

    #region Severity Filtering Tests

    [Fact]
    public async Task ExecuteAsync_WithModerateThreshold_FiltersMinorDeviations()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"deviationThreshold": "moderate"}""");

        var response = CreateDeviationResponse(new[]
        {
            ("LimitationOfLiability", "Significant", "High", "Standard", 0.95),
            ("PaymentTerms", "Moderate", "Medium", "Standard", 0.85),
            ("Notices", "Minor", "Low", "Standard", 0.75)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.TotalDeviationsFound); // Significant + Moderate only
        Assert.Equal(0, data.MinorCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithSignificantThreshold_FiltersOthers()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"deviationThreshold": "significant"}""");

        var response = CreateDeviationResponse(new[]
        {
            ("LimitationOfLiability", "Significant", "High", "Standard", 0.95),
            ("PaymentTerms", "Moderate", "Medium", "Standard", 0.85),
            ("Notices", "Minor", "Low", "Standard", 0.75)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.Equal(1, data.TotalDeviationsFound); // Significant only
    }

    #endregion

    #region Max Deviations Limit Tests

    [Fact]
    public async Task ExecuteAsync_RespectsMaxDeviationsLimit()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxDeviations": 2}""");

        var response = CreateDeviationResponse(new[]
        {
            ("Type1", "Significant", "Dev1", "Std", 0.95),
            ("Type2", "Significant", "Dev2", "Std", 0.90),
            ("Type3", "Moderate", "Dev3", "Std", 0.85),
            ("Type4", "Minor", "Dev4", "Std", 0.80)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.TotalDeviationsFound);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ExecuteAsync_WithMalformedResponse_HandlesGracefully()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Not valid JSON response");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.Empty(data.Deviations);
    }

    [Fact]
    public async Task ExecuteAsync_WithKnowledgeContext_IncludesInPrompt()
    {
        // Arrange
        var context = CreateValidContext(knowledgeContext: "Our standard indemnification requires mutual protection.");
        var tool = CreateTool();

        string capturedPrompt = "";
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
            .ReturnsAsync("""{"deviations": [], "clausesAnalyzed": []}""");

        // Act
        await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.Contains("Our standard indemnification", capturedPrompt);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsConfidenceScores()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = """
            {
              "deviations": [
                {"clauseType": "Test", "severity": "Minor", "documentText": "Test", "deviationDescription": "Test", "impact": "Test", "confidence": 1.5}
              ],
              "clausesAnalyzed": []
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<ClauseComparisonResult>();
        Assert.NotNull(data);
        Assert.All(data.Deviations, d => Assert.InRange(d.Confidence, 0.0, 1.0));
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesSummaryWithSignificantDeviations()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = CreateDeviationResponse(new[]
        {
            ("LimitationOfLiability", "Significant", "Unlimited liability clause", "Standard", 0.95),
            ("Indemnification", "Significant", "One-sided indemnity", "Standard", 0.90)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Contains("2 deviation(s)", result.Summary);
        Assert.Contains("significant", result.Summary.ToLower());
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(
        string extractedText = "This contract contains limitation of liability and indemnification clauses.",
        string tenantId = "test-tenant-id",
        string? knowledgeContext = null)
    {
        return new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = tenantId,
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test Contract",
                FileName = "test-contract.pdf",
                ContentType = "application/pdf",
                ExtractedText = extractedText
            },
            KnowledgeContext = knowledgeContext,
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
        string name = "Test Clause Comparison Tool",
        string? configuration = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Test tool for clause comparison",
            Type = ToolType.ClauseComparison,
            Configuration = configuration
        };
    }

    private static string CreateDeviationResponse(
        (string clauseType, string severity, string description, string standard, double confidence)[] deviations)
    {
        var items = deviations.Select(d => $$"""
            {
              "clauseType": "{{d.clauseType}}",
              "severity": "{{d.severity}}",
              "documentText": "...document text...",
              "standardExpectation": "{{d.standard}}",
              "deviationDescription": "{{d.description}}",
              "impact": "Potential risk",
              "confidence": {{d.confidence}}
            }
            """);

        return $$"""
            {
              "deviations": [
                {{string.Join(",\n    ", items)}}
              ],
              "clausesAnalyzed": [{"clauseType": "{{deviations.FirstOrDefault().clauseType ?? "General"}}", "found": true}]
            }
            """;
    }

    private static string CreateDeviationResponseWithRecommendations(
        (string clauseType, string severity, string description, string standard, double confidence, string recommendation)[] deviations)
    {
        var items = deviations.Select(d => $$"""
            {
              "clauseType": "{{d.clauseType}}",
              "severity": "{{d.severity}}",
              "documentText": "...document text...",
              "standardExpectation": "{{d.standard}}",
              "deviationDescription": "{{d.description}}",
              "impact": "Potential risk",
              "confidence": {{d.confidence}},
              "recommendation": "{{d.recommendation}}"
            }
            """);

        return $$"""
            {
              "deviations": [
                {{string.Join(",\n    ", items)}}
              ],
              "clausesAnalyzed": []
            }
            """;
    }

    #endregion
}
