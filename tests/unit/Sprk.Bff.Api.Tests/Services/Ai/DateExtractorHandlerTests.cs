using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for DateExtractorHandler.
/// </summary>
public sealed class DateExtractorHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ITextChunkingService> _textChunkingServiceMock;
    private readonly Mock<ILogger<DateExtractorHandler>> _loggerMock;
    private readonly DateExtractorHandler _handler;

    public DateExtractorHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _textChunkingServiceMock = new Mock<ITextChunkingService>();
        _loggerMock = new Mock<ILogger<DateExtractorHandler>>();

        // Default mock: return the input text as a single chunk
        _textChunkingServiceMock
            .Setup(x => x.ChunkTextAsync(It.IsAny<string?>(), It.IsAny<ChunkingOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string? text, ChunkingOptions? _, CancellationToken _) =>
                string.IsNullOrEmpty(text)
                    ? Array.Empty<TextChunk>()
                    : new List<TextChunk> { new() { Content = text, Index = 0, StartPosition = 0, EndPosition = text.Length } });

        _handler = new DateExtractorHandler(_openAiClientMock.Object, _textChunkingServiceMock.Object, _loggerMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        // Assert
        Assert.Equal("DateExtractorHandler", _handler.HandlerId);
    }

    [Fact]
    public void Metadata_HasCorrectName()
    {
        // Assert
        Assert.Equal("Date Extractor", _handler.Metadata.Name);
    }

    [Fact]
    public void Metadata_HasCorrectVersion()
    {
        // Assert
        Assert.Equal("1.0.0", _handler.Metadata.Version);
    }

    [Fact]
    public void Metadata_SupportsExpectedInputTypes()
    {
        // Assert
        Assert.Contains("text/plain", _handler.Metadata.SupportedInputTypes);
        Assert.Contains("application/pdf", _handler.Metadata.SupportedInputTypes);
        Assert.Contains("application/vnd.openxmlformats-officedocument.wordprocessingml.document", _handler.Metadata.SupportedInputTypes);
    }

    [Fact]
    public void Metadata_HasExpectedParameters()
    {
        // Assert
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "date_types");
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "include_relative_dates");
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "max_dates");
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "include_context");
    }

    [Fact]
    public void SupportedToolTypes_ContainsDateExtractor()
    {
        // Assert
        Assert.Contains(ToolType.DateExtractor, _handler.SupportedToolTypes);
    }

    #endregion

    #region Validation Tests

    [Fact]
    public void Validate_WithValidContext_ReturnsSuccess()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Validate_WithNullDocument_ReturnsError()
    {
        // Arrange
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "test-tenant-id",
            Document = null!
        };
        var tool = CreateValidTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Document context"));
    }

    [Fact]
    public void Validate_WithEmptyExtractedText_ReturnsError()
    {
        // Arrange
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "test-tenant-id",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test",
                FileName = "test.pdf",
                ExtractedText = ""
            }
        };
        var tool = CreateValidTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("extracted text"));
    }

    [Fact]
    public void Validate_WithMissingTenantId_ReturnsError()
    {
        // Arrange
        var context = new ToolExecutionContext
        {
            AnalysisId = Guid.NewGuid(),
            TenantId = "",
            Document = new DocumentContext
            {
                DocumentId = Guid.NewGuid(),
                Name = "Test",
                FileName = "test.pdf",
                ExtractedText = "Some text"
            }
        };
        var tool = CreateValidTool();

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("TenantId"));
    }

    [Fact]
    public void Validate_WithInvalidMaxDates_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"maxDates": 300}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("max_dates"));
    }

    [Fact]
    public void Validate_WithZeroMaxDates_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"maxDates": 0}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("max_dates"));
    }

    [Fact]
    public void Validate_WithInvalidJson_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: "not valid json");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Invalid configuration JSON"));
    }

    [Fact]
    public void Validate_WithValidConfiguration_ReturnsSuccess()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"maxDates": 25, "includeRelativeDates": true}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.True(result.IsValid);
    }

    #endregion

    #region Execution Tests

    [Fact]
    public async Task ExecuteAsync_WithValidInput_ReturnsSuccessResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("ExpirationDate", "December 31, 2025", "2025-12-31", false, null, 0.90)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.NotNull(result.Summary);
        Assert.Contains("2 date(s)", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoDatesFound_ReturnsSuccessWithEmptyResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = "{\"dates\": []}";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Contains("No dates found", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_WithRelativeDates_IncludesThemByDefault()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("Deadline", "30 days after signing", null, true, "Signing Date", 0.85)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.Dates.Count);
        Assert.Contains(data.Dates, d => d.IsRelative);
    }

    [Fact]
    public async Task ExecuteAsync_WithExcludeRelativeDates_FiltersThemOut()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"includeRelativeDates": false}""");

        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("Deadline", "30 days after signing", null, true, "Signing Date", 0.85)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.Single(data.Dates);
        Assert.False(data.Dates.First().IsRelative);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesEarliestAndLatestDates()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("ExpirationDate", "December 31, 2025", "2025-12-31", false, null, 0.90),
            ("SignatureDate", "November 15, 2024", "2024-11-15", false, null, 0.92)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.NotNull(data.EarliestDate);
        Assert.NotNull(data.LatestDate);
        Assert.Equal(new DateTime(2024, 11, 15), data.EarliestDate);
        Assert.Equal(new DateTime(2025, 12, 31), data.LatestDate);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesDatesByType()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("Deadline", "February 15, 2025", "2025-02-15", false, null, 0.90),
            ("Deadline", "March 1, 2025", "2025-03-01", false, null, 0.85)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.DatesByType["Deadline"]);
        Assert.Equal(1, data.DatesByType["EffectiveDate"]);
    }

    [Fact]
    public async Task ExecuteAsync_DeduplicatesDates()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        // Same date text appears twice
        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("EffectiveDate", "january 1, 2025", "2025-01-01", false, null, 0.90)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.Single(data.Dates); // Deduplicated to one
        Assert.Equal(0.95, data.Dates.First().Confidence, 5); // Keeps highest confidence
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxDatesConfig_LimitsResults()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"maxDates": 2}""");

        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("ExpirationDate", "December 31, 2025", "2025-12-31", false, null, 0.90),
            ("SignatureDate", "November 15, 2024", "2024-11-15", false, null, 0.85)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.Dates.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsConfidenceValues()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = @"{
            ""dates"": [
                { ""dateType"": ""EffectiveDate"", ""originalText"": ""Jan 1"", ""normalizedDate"": ""2025-01-01"", ""isRelative"": false, ""confidence"": 1.5 },
                { ""dateType"": ""ExpirationDate"", ""originalText"": ""Dec 31"", ""normalizedDate"": ""2025-12-31"", ""isRelative"": false, ""confidence"": -0.5 }
            ]
        }";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.All(data.Dates, d => Assert.InRange(d.Confidence, 0.0, 1.0));
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellation_ReturnsCancelledResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
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
    public async Task ExecuteAsync_WithException_ReturnsErrorResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("API error"));

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ToolErrorCodes.InternalError, result.ErrorCode);
        Assert.NotNull(result.ErrorMessage);
        Assert.Contains("API error", result.ErrorMessage);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidJson_HandlesGracefully()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("This is not valid JSON");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<DateExtractionResult>();
        Assert.NotNull(data);
        Assert.Empty(data.Dates);
    }

    [Fact]
    public async Task ExecuteAsync_SetsExecutionMetadata()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(("EffectiveDate", "Jan 1, 2025", "2025-01-01", false, null, 0.95));

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

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
    public async Task ExecuteAsync_SetsConfidenceToAverage()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(
            ("EffectiveDate", "Jan 1", "2025-01-01", false, null, 0.90),
            ("ExpirationDate", "Dec 31", "2025-12-31", false, null, 0.80)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Confidence);
        Assert.Equal(0.85, result.Confidence.Value, 5); // Average of 0.90 and 0.80
    }

    [Fact]
    public async Task ExecuteAsync_WithNoDates_SetsConfidenceToZero()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"dates\": []}");

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Confidence);
        Assert.Equal(0.0, result.Confidence.Value, 5);
    }

    #endregion

    #region Multi-Chunk Tests

    [Fact]
    public async Task ExecuteAsync_WithLargeDocument_ProcessesMultipleChunks()
    {
        // Arrange
        var largeText = new string('x', 20000); // Force multiple chunks
        var context = CreateValidContext(largeText);
        var tool = CreateValidTool();

        var response = CreateDateResponse(("EffectiveDate", "Jan 1, 2025", "2025-01-01", false, null, 0.95));

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.True(result.Execution.ModelCalls > 1);
    }

    #endregion

    #region Summary Generation Tests

    [Fact]
    public async Task ExecuteAsync_SummaryIncludesDateRange()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(
            ("EffectiveDate", "January 1, 2025", "2025-01-01", false, null, 0.95),
            ("ExpirationDate", "December 31, 2025", "2025-12-31", false, null, 0.90)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Summary);
        Assert.Contains("Date range:", result.Summary);
        Assert.Contains("2025-01-01", result.Summary);
        Assert.Contains("2025-12-31", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_SummaryIndicatesRelativeDates()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateDateResponse(
            ("Deadline", "30 days after signing", null, true, "Signing Date", 0.85)
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Summary);
        Assert.Contains("relative date expressions", result.Summary);
    }

    #endregion

    #region DateTypes Static Class Tests

    [Fact]
    public void DateTypes_StandardTypes_ContainsExpectedTypes()
    {
        // Assert
        Assert.Contains(DateTypes.EffectiveDate, DateTypes.StandardTypes);
        Assert.Contains(DateTypes.ExpirationDate, DateTypes.StandardTypes);
        Assert.Contains(DateTypes.SignatureDate, DateTypes.StandardTypes);
        Assert.Contains(DateTypes.Deadline, DateTypes.StandardTypes);
        Assert.Contains(DateTypes.RenewalDate, DateTypes.StandardTypes);
        Assert.Contains(DateTypes.TerminationDate, DateTypes.StandardTypes);
        Assert.Contains(DateTypes.PaymentDue, DateTypes.StandardTypes);
        Assert.Contains(DateTypes.NoticeDate, DateTypes.StandardTypes);
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(
        string extractedText = "This agreement is effective January 1, 2025 and expires December 31, 2025.",
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
                FileName = "contract.pdf",
                ExtractedText = extractedText,
                ContentType = "application/pdf"
            }
        };
    }

    private static AnalysisTool CreateValidTool(string? configuration = null)
    {
        return new AnalysisTool
        {
            Id = Guid.NewGuid(),
            Name = "Date Extractor",
            Type = ToolType.DateExtractor,
            Configuration = configuration
        };
    }

    private static string CreateDateResponse(
        params (string dateType, string originalText, string? normalizedDate, bool isRelative, string? relativeBase, double confidence)[] dates)
    {
        var dateItems = dates.Select(d =>
            $@"{{
                ""dateType"": ""{d.dateType}"",
                ""originalText"": ""{d.originalText}"",
                ""normalizedDate"": {(d.normalizedDate != null ? $"\"{d.normalizedDate}\"" : "null")},
                ""isRelative"": {d.isRelative.ToString().ToLower()},
                ""relativeBase"": {(d.relativeBase != null ? $"\"{d.relativeBase}\"" : "null")},
                ""confidence"": {d.confidence},
                ""context"": ""Sample context for {d.originalText}""
            }}");

        return $@"{{
            ""dates"": [{string.Join(",", dateItems)}]
        }}";
    }

    #endregion
}
