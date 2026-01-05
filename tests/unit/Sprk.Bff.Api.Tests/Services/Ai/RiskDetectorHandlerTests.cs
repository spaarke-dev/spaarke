using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for RiskDetectorHandler.
/// Validates handler properties, validation logic, execution behavior,
/// severity classification, and confidence score handling.
/// </summary>
public class RiskDetectorHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ILogger<RiskDetectorHandler>> _loggerMock;
    private readonly RiskDetectorHandler _handler;

    public RiskDetectorHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _loggerMock = new Mock<ILogger<RiskDetectorHandler>>();
        _handler = new RiskDetectorHandler(_openAiClientMock.Object, _loggerMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        Assert.Equal("RiskDetectorHandler", _handler.HandlerId);
    }

    [Fact]
    public void SupportedToolTypes_ContainsRiskDetector()
    {
        Assert.Contains(ToolType.RiskDetector, _handler.SupportedToolTypes);
    }

    [Fact]
    public void SupportedToolTypes_ContainsExactlyOneType()
    {
        Assert.Single(_handler.SupportedToolTypes);
    }

    [Fact]
    public void Metadata_HasExpectedName()
    {
        Assert.Equal("Risk Detector", _handler.Metadata.Name);
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

        var categoriesParam = parameters.FirstOrDefault(p => p.Name == "categories");
        Assert.NotNull(categoriesParam);
        Assert.Equal(ToolParameterType.Array, categoriesParam.Type);
        Assert.False(categoriesParam.Required);

        var severityParam = parameters.FirstOrDefault(p => p.Name == "severity_threshold");
        Assert.NotNull(severityParam);
        Assert.Equal(ToolParameterType.String, severityParam.Type);
        Assert.Equal("low", severityParam.DefaultValue);

        var maxRisksParam = parameters.FirstOrDefault(p => p.Name == "max_risks");
        Assert.NotNull(maxRisksParam);
        Assert.Equal(ToolParameterType.Integer, maxRisksParam.Type);
        Assert.Equal(20, maxRisksParam.DefaultValue);

        var includeRecommendationsParam = parameters.FirstOrDefault(p => p.Name == "include_recommendations");
        Assert.NotNull(includeRecommendationsParam);
        Assert.Equal(ToolParameterType.Boolean, includeRecommendationsParam.Type);
        Assert.Equal(true, includeRecommendationsParam.DefaultValue);
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
        Assert.Contains("Document extracted text is required for risk detection.", result.Errors);
    }

    [Fact]
    public void Validate_WithWhitespaceExtractedText_ReturnsFailure()
    {
        var context = CreateValidContext(extractedText: "   ");
        var tool = CreateTool();

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("Document extracted text is required for risk detection.", result.Errors);
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
        var tool = CreateTool(configuration: """{"severityThreshold": "medium", "maxRisks": 10}""");

        var result = _handler.Validate(context, tool);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Validate_WithMaxRisksTooSmall_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxRisks": 0}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("max_risks must be between 1 and 100.", result.Errors);
    }

    [Fact]
    public void Validate_WithMaxRisksTooLarge_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxRisks": 150}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("max_risks must be between 1 and 100.", result.Errors);
    }

    [Fact]
    public void Validate_WithInvalidSeverity_ReturnsFailure()
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"severityThreshold": "critical"}""");

        var result = _handler.Validate(context, tool);

        Assert.False(result.IsValid);
        Assert.Contains("severity_threshold must be 'low', 'medium', or 'high'.", result.Errors);
    }

    [Theory]
    [InlineData("low")]
    [InlineData("medium")]
    [InlineData("high")]
    public void Validate_WithValidSeverityThresholds_ReturnsSuccess(string severity)
    {
        var context = CreateValidContext();
        var tool = CreateTool(configuration: $$$"""{"severityThreshold": "{{{severity}}}"}""");

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
        var context = CreateValidContext(extractedText: "This contract has an unlimited liability clause that could expose the company.");
        var tool = CreateTool();

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "Unlimited Liability", "Unlimited liability clause found", 0.95)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("RiskDetectorHandler", result.HandlerId);
        Assert.Equal(tool.Id, result.ToolId);
        Assert.Equal(tool.Name, result.ToolName);
        Assert.NotNull(result.Data);

        // Verify single model call
        Assert.Equal(1, result.Execution.ModelCalls);
        Assert.Equal("gpt-4o-mini", result.Execution.ModelName);
    }

    [Fact]
    public async Task ExecuteAsync_WithLargeDocument_ProcessesMultipleChunks()
    {
        // Arrange - Create document larger than 8000 characters
        var longText = string.Join(" ", Enumerable.Repeat("This contract includes various clauses that may expose the parties to legal and financial risks.", 200));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        var callCount = 0;
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                return CreateRiskResponse(new[]
                {
                    ("legal", "Medium", $"Risk in chunk {callCount}", $"Found in chunk {callCount}", 0.8)
                });
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
            .ReturnsAsync(CreateRiskResponse(new[] { ("legal", "Low", "Minor Risk", "Test", 0.5) }));

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
    public async Task ExecuteAsync_DetectsHighSeverityRisks()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "Unlimited Liability", "No liability cap", 0.95),
            ("financial", "High", "Penalty Clause", "Excessive penalties", 0.90)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.HighSeverityCount);
        Assert.Equal(0, data.MediumSeverityCount);
        Assert.Equal(0, data.LowSeverityCount);
    }

    [Fact]
    public async Task ExecuteAsync_DetectsMixedSeverityRisks()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "Critical Risk", "Description 1", 0.95),
            ("financial", "Medium", "Moderate Risk", "Description 2", 0.80),
            ("operational", "Low", "Minor Risk", "Description 3", 0.60)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal(3, data.TotalRisksFound);
        Assert.Equal(1, data.HighSeverityCount);
        Assert.Equal(1, data.MediumSeverityCount);
        Assert.Equal(1, data.LowSeverityCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoRisks_ReturnsEmptyResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("""{"risks": []}""");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal(0, data.TotalRisksFound);
        Assert.Contains("No risks detected", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesAverageConfidence()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "Risk 1", "Description", 0.90),
            ("financial", "Medium", "Risk 2", "Description", 0.80)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Confidence);
        Assert.Equal(0.85, result.Confidence.Value, precision: 5); // Average of 0.90 and 0.80
    }

    [Fact]
    public async Task ExecuteAsync_GeneratesSummaryWithHighRisks()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "Unlimited Liability", "Description", 0.95),
            ("financial", "High", "No Cap Clause", "Description", 0.90),
            ("operational", "Medium", "Process Gap", "Description", 0.75)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Contains("3 risk(s)", result.Summary);
        Assert.Contains("2 high", result.Summary);
        Assert.Contains("1 medium", result.Summary);
        Assert.Contains("High severity risks:", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_IncludesRecommendationsWhenConfigured()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"includeRecommendations": true}""");

        var riskResponse = CreateRiskResponseWithRecommendations(new[]
        {
            ("legal", "High", "Risk", "Description", 0.9, "Add liability cap")
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.NotEmpty(data.Risks);
        Assert.NotNull(data.Risks[0].Recommendation);
    }

    #endregion

    #region Severity Filtering Tests

    [Fact]
    public async Task ExecuteAsync_WithMediumThreshold_FiltersLowRisks()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"severityThreshold": "medium"}""");

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "High Risk", "Description", 0.95),
            ("financial", "Medium", "Medium Risk", "Description", 0.80),
            ("operational", "Low", "Low Risk", "Description", 0.60)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.TotalRisksFound); // Only High and Medium
        Assert.Equal(0, data.LowSeverityCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithHighThreshold_FiltersLowAndMediumRisks()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"severityThreshold": "high"}""");

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "High Risk", "Description", 0.95),
            ("financial", "Medium", "Medium Risk", "Description", 0.80),
            ("operational", "Low", "Low Risk", "Description", 0.60)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal(1, data.TotalRisksFound); // Only High
        Assert.Equal(1, data.HighSeverityCount);
        Assert.Equal(0, data.MediumSeverityCount);
        Assert.Equal(0, data.LowSeverityCount);
    }

    #endregion

    #region Max Risks Limit Tests

    [Fact]
    public async Task ExecuteAsync_WithMaxRisksLimit_RespectsLimit()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxRisks": 2}""");

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "High", "Risk 1", "Description", 0.95),
            ("financial", "High", "Risk 2", "Description", 0.90),
            ("operational", "Medium", "Risk 3", "Description", 0.80),
            ("compliance", "Low", "Risk 4", "Description", 0.70)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.TotalRisksFound);
    }

    [Fact]
    public async Task ExecuteAsync_PrioritizesHighSeverityWhenLimited()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"maxRisks": 1}""");

        var riskResponse = CreateRiskResponse(new[]
        {
            ("legal", "Low", "Low Risk", "Description", 0.95),
            ("financial", "High", "High Risk", "Description", 0.90)
        });

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(riskResponse);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal(1, data.TotalRisksFound);
        Assert.Equal(1, data.HighSeverityCount); // High severity should be kept
    }

    #endregion

    #region Multi-Chunk Processing Tests

    [Fact]
    public async Task ExecuteAsync_WithMultipleChunks_CombinesRisks()
    {
        // Arrange - Create a very long document
        var longText = string.Join(". ", Enumerable.Range(1, 500).Select(i => $"This is sentence number {i} in the contract document"));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        var responseCount = 0;
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                responseCount++;
                return CreateRiskResponse(new[]
                {
                    ("legal", "High", $"Risk from chunk {responseCount}", "Description", 0.9)
                });
            });

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
        var longText = string.Join(" ", Enumerable.Repeat("This contract document contains risk clauses.", 400));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRiskResponse(new[] { ("legal", "Low", "Risk", "Desc", 0.5) }));

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
        // Arrange - Create a very long document to ensure multiple chunks
        var longText = string.Join(" ", Enumerable.Repeat("This is a longer test sentence for a contract document with potential risks that will be analyzed.", 200));
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
                    throw new OperationCanceledException();
                }
                return Task.FromResult(CreateRiskResponse(new[] { ("legal", "Low", "Risk", "Desc", 0.5) }));
            });

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.Cancelled, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteAsync_DeduplicatesRisksAcrossChunks()
    {
        // Arrange - Document that will be split into chunks
        var longText = string.Join(" ", Enumerable.Repeat("Contract with unlimited liability clause that exposes the company to significant financial risk.", 200));
        var context = CreateValidContext(extractedText: longText);
        var tool = CreateTool();

        // Both chunks detect the same risk
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRiskResponse(new[]
            {
                ("legal", "High", "Unlimited Liability", "Found liability clause", 0.90)
            }));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        // After deduplication, should only have one "Unlimited Liability" risk
        var liabilityRisks = data.Risks.Where(r => r.Title.Contains("Unlimited Liability", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(liabilityRisks);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public async Task ExecuteAsync_WithMinimalConfiguration_UsesDefaults()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: "{}");

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRiskResponse(new[] { ("legal", "Low", "Risk", "Desc", 0.5) }));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.NotEmpty(data.CategoriesAnalyzed); // Default categories used
    }

    [Fact]
    public async Task ExecuteAsync_WithMalformedJsonResponse_HandlesGracefully()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not valid JSON");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert - Should still succeed but with no risks
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Empty(data.Risks);
    }

    [Fact]
    public async Task ExecuteAsync_WithSpecialCharactersInDocument_ProcessesSuccessfully()
    {
        // Arrange
        var textWithSpecialChars = "Contract with special chars: $100,000 @ 5% interest. <liability>unlimited</liability> & more...";
        var context = CreateValidContext(extractedText: textWithSpecialChars);
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRiskResponse(new[] { ("financial", "Medium", "Interest Rate Risk", "5% interest", 0.7) }));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithUnicodeDocument_ProcessesSuccessfully()
    {
        // Arrange
        var unicodeText = "Contract with unicode: 日本語 العربية 中文 🔒 📄";
        var context = CreateValidContext(extractedText: unicodeText);
        var tool = CreateTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CreateRiskResponse(new[] { ("confidentiality", "Low", "Language Risk", "Multi-language", 0.5) }));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsConfidenceScores()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        // Response with out-of-range confidence
        var response = """
            {
              "risks": [
                {
                  "category": "legal",
                  "severity": "High",
                  "title": "Over-confident Risk",
                  "description": "Test",
                  "location": "Test",
                  "confidence": 1.5
                },
                {
                  "category": "financial",
                  "severity": "Low",
                  "title": "Negative Confidence Risk",
                  "description": "Test",
                  "location": "Test",
                  "confidence": -0.5
                }
              ]
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.All(data.Risks, r => Assert.InRange(r.Confidence, 0.0, 1.0));
    }

    [Fact]
    public async Task ExecuteAsync_WithCustomCategories_UsesThem()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool(configuration: """{"categories": ["custom1", "custom2"]}""");

        string capturedPrompt = "";
        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<string, string?, CancellationToken>((prompt, _, _) => capturedPrompt = prompt)
            .ReturnsAsync(CreateRiskResponse(new[] { ("custom1", "Low", "Test", "Desc", 0.5) }));

        // Act
        await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.Contains("custom1", capturedPrompt);
        Assert.Contains("custom2", capturedPrompt);
    }

    [Fact]
    public async Task ExecuteAsync_NormalizesCategoriesToLowerCase()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = """
            {
              "risks": [
                {
                  "category": "LEGAL",
                  "severity": "High",
                  "title": "Test",
                  "description": "Test",
                  "location": "Test",
                  "confidence": 0.9
                }
              ]
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal("legal", data.Risks[0].Category);
    }

    [Fact]
    public async Task ExecuteAsync_HandlesNullCategoryInResponse()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateTool();

        var response = """
            {
              "risks": [
                {
                  "category": null,
                  "severity": "High",
                  "title": "Test",
                  "description": "Test",
                  "location": "Test",
                  "confidence": 0.9
                }
              ]
            }
            """;

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<RiskDetectionResult>();
        Assert.NotNull(data);
        Assert.Equal("general", data.Risks[0].Category); // Defaults to "general"
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(
        string extractedText = "This is a test contract document for risk analysis.",
        string tenantId = "test-tenant-id")
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
        string name = "Test Risk Detector Tool",
        string? configuration = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = name,
            Description = "Test tool for risk detection",
            Type = ToolType.RiskDetector,
            Configuration = configuration
        };
    }

    private static string CreateRiskResponse(
        (string category, string severity, string title, string description, double confidence)[] risks)
    {
        var riskItems = risks.Select(r => $$"""
            {
              "category": "{{r.category}}",
              "severity": "{{r.severity}}",
              "title": "{{r.title}}",
              "description": "{{r.description}}",
              "location": "...relevant text...",
              "confidence": {{r.confidence}}
            }
            """);

        return $$"""
            {
              "risks": [
                {{string.Join(",\n    ", riskItems)}}
              ]
            }
            """;
    }

    private static string CreateRiskResponseWithRecommendations(
        (string category, string severity, string title, string description, double confidence, string recommendation)[] risks)
    {
        var riskItems = risks.Select(r => $$"""
            {
              "category": "{{r.category}}",
              "severity": "{{r.severity}}",
              "title": "{{r.title}}",
              "description": "{{r.description}}",
              "location": "...relevant text...",
              "confidence": {{r.confidence}},
              "recommendation": "{{r.recommendation}}"
            }
            """);

        return $$"""
            {
              "risks": [
                {{string.Join(",\n    ", riskItems)}}
              ]
            }
            """;
    }

    #endregion
}
