using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Tools;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Ai;

/// <summary>
/// Unit tests for FinancialCalculatorHandler.
/// </summary>
public sealed class FinancialCalculatorHandlerTests
{
    private readonly Mock<IOpenAiClient> _openAiClientMock;
    private readonly Mock<ILogger<FinancialCalculatorHandler>> _loggerMock;
    private readonly FinancialCalculatorHandler _handler;

    public FinancialCalculatorHandlerTests()
    {
        _openAiClientMock = new Mock<IOpenAiClient>();
        _loggerMock = new Mock<ILogger<FinancialCalculatorHandler>>();
        _handler = new FinancialCalculatorHandler(_openAiClientMock.Object, _loggerMock.Object);
    }

    #region Handler Properties Tests

    [Fact]
    public void HandlerId_ReturnsExpectedValue()
    {
        // Assert
        Assert.Equal("FinancialCalculatorHandler", _handler.HandlerId);
    }

    [Fact]
    public void Metadata_HasCorrectName()
    {
        // Assert
        Assert.Equal("Financial Calculator", _handler.Metadata.Name);
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
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "currencies");
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "include_payment_terms");
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "include_totals");
        Assert.Contains(_handler.Metadata.Parameters, p => p.Name == "max_items");
    }

    [Fact]
    public void SupportedToolTypes_ContainsFinancialCalculator()
    {
        // Assert
        Assert.Contains(ToolType.FinancialCalculator, _handler.SupportedToolTypes);
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
    public void Validate_WithInvalidMaxItems_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"maxItems": 600}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("max_items"));
    }

    [Fact]
    public void Validate_WithZeroMaxItems_ReturnsError()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"maxItems": 0}""");

        // Act
        var result = _handler.Validate(context, tool);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("max_items"));
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
        var tool = CreateValidTool(configuration: """{"maxItems": 50, "includePaymentTerms": true}""");

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
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "Annual license fee", 50000.00m, "USD", "Annual", false, 0.95),
                ("Payment", "Monthly service charge", 5000.00m, "USD", "Monthly", false, 0.90)
            },
            paymentTerms: new (string, string, string?, decimal?)[] { ("PaymentDue", "Payment due within 30 days", "30 days", null) }
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
        Assert.Contains("2 financial item(s)", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_WithNoItemsFound_ReturnsSuccessWithEmptyResult()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = "{\"items\": [], \"paymentTerms\": []}";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.NotNull(result.Summary);
        Assert.Contains("No financial values found", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesTotalsByCurrency()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "License fee", 50000.00m, "USD", "Annual", false, 0.95),
                ("Fee", "Support fee", 10000.00m, "USD", "Annual", false, 0.90),
                ("Fee", "European license", 30000.00m, "EUR", "Annual", false, 0.92)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Equal(60000.00m, data.TotalsByCurrency["USD"].Total);
        Assert.Equal(30000.00m, data.TotalsByCurrency["EUR"].Total);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesTotalsByCategory()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "License fee", 50000.00m, "USD", "Annual", false, 0.95),
                ("Fee", "Support fee", 10000.00m, "USD", "Annual", false, 0.90),
                ("Penalty", "Late payment penalty", 5000.00m, "USD", "OneTime", false, 0.85)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Equal(60000.00m, data.TotalsByCategory["Fee"]);
        Assert.Equal(5000.00m, data.TotalsByCategory["Penalty"]);
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesGrandTotalUsd()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "USD fee", 10000.00m, "USD", "Annual", false, 0.95),
                ("Fee", "EUR fee", 10000.00m, "EUR", "Annual", false, 0.90) // ~11000 USD
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.True(data.GrandTotalUsd > 10000m); // USD + converted EUR
    }

    [Fact]
    public async Task ExecuteAsync_IncludesPaymentTermsByDefault()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[] { ("Fee", "License fee", 50000.00m, "USD", "Annual", false, 0.95) },
            paymentTerms: new (string, string, string?, decimal?)[] { ("PaymentDue", "Net 30", "30 days", null) }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Single(data.PaymentTerms);
    }

    [Fact]
    public async Task ExecuteAsync_WithExcludePaymentTerms_FiltersThemOut()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"includePaymentTerms": false}""");

        var response = CreateFinancialResponse(
            items: new[] { ("Fee", "License fee", 50000.00m, "USD", "Annual", false, 0.95) },
            paymentTerms: new (string, string, string?, decimal?)[] { ("PaymentDue", "Net 30", "30 days", null) }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Empty(data.PaymentTerms);
    }

    [Fact]
    public async Task ExecuteAsync_DeduplicatesItems()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        // Same item appears twice with different confidence
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "License fee", 50000.00m, "USD", "Annual", false, 0.95),
                ("Fee", "License fee", 50000.00m, "USD", "Annual", false, 0.85)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Single(data.Items); // Deduplicated to one
        Assert.Equal(0.95, data.Items.First().Confidence, 5); // Keeps highest confidence
    }

    [Fact]
    public async Task ExecuteAsync_WithMaxItemsConfig_LimitsResults()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool(configuration: """{"maxItems": 2}""");

        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "Fee 1", 50000.00m, "USD", "Annual", false, 0.95),
                ("Fee", "Fee 2", 40000.00m, "USD", "Annual", false, 0.90),
                ("Fee", "Fee 3", 30000.00m, "USD", "Annual", false, 0.85)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Equal(2, data.Items.Count);
    }

    [Fact]
    public async Task ExecuteAsync_ClampsConfidenceValues()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = @"{
            ""items"": [
                { ""category"": ""Fee"", ""description"": ""High conf"", ""amount"": 1000, ""currency"": ""USD"", ""frequency"": ""OneTime"", ""isEstimate"": false, ""confidence"": 1.5 },
                { ""category"": ""Fee"", ""description"": ""Low conf"", ""amount"": 2000, ""currency"": ""USD"", ""frequency"": ""OneTime"", ""isEstimate"": false, ""confidence"": -0.5 }
            ],
            ""paymentTerms"": []
        }";

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.All(data.Items, i => Assert.InRange(i.Confidence, 0.0, 1.0));
    }

    [Fact]
    public async Task ExecuteAsync_CalculatesOneTimeVsRecurringTotals()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "Setup fee", 10000.00m, "USD", "OneTime", false, 0.95),
                ("Fee", "Monthly fee", 5000.00m, "USD", "Monthly", false, 0.90),
                ("Fee", "Annual fee", 50000.00m, "USD", "Annual", false, 0.92)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        var usdTotal = data.TotalsByCurrency["USD"];
        Assert.Equal(10000.00m, usdTotal.OneTimeTotal);
        Assert.Equal(55000.00m, usdTotal.RecurringTotal); // Monthly + Annual
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
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Empty(data.Items);
    }

    [Fact]
    public async Task ExecuteAsync_SetsExecutionMetadata()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[] { ("Fee", "License", 50000m, "USD", "Annual", false, 0.95) }
        );

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
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "Fee 1", 10000m, "USD", "OneTime", false, 0.90),
                ("Fee", "Fee 2", 20000m, "USD", "OneTime", false, 0.80)
            }
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
    public async Task ExecuteAsync_WithNoItems_SetsConfidenceToZero()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("{\"items\": [], \"paymentTerms\": []}");

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

        var response = CreateFinancialResponse(
            items: new[] { ("Fee", "License", 50000m, "USD", "Annual", false, 0.95) }
        );

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
    public async Task ExecuteAsync_SummaryIncludesTotals()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "License", 50000.00m, "USD", "Annual", false, 0.95),
                ("Fee", "Support", 10000.00m, "EUR", "Annual", false, 0.90)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Summary);
        Assert.Contains("Totals:", result.Summary);
        Assert.Contains("USD", result.Summary);
        Assert.Contains("EUR", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_SummaryIncludesGrandTotalUsd()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[] { ("Fee", "License", 50000.00m, "USD", "Annual", false, 0.95) }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Summary);
        Assert.Contains("Grand total", result.Summary);
        Assert.Contains("USD", result.Summary);
    }

    [Fact]
    public async Task ExecuteAsync_SummaryIndicatesPaymentTermsCount()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[] { ("Fee", "License", 50000.00m, "USD", "Annual", false, 0.95) },
            paymentTerms: new (string, string, string?, decimal?)[]
            {
                ("PaymentDue", "Net 30", "30 days", null),
                ("LateFee", "2% penalty", null, 2.0m)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.NotNull(result.Summary);
        Assert.Contains("2 payment term(s)", result.Summary);
    }

    #endregion

    #region Currency Conversion Tests

    [Theory]
    [InlineData("USD", 10000, 10000)]
    [InlineData("EUR", 10000, 11000)] // 1.10 rate
    [InlineData("GBP", 10000, 12700)] // 1.27 rate
    [InlineData("CAD", 10000, 7400)]  // 0.74 rate
    [InlineData("AUD", 10000, 6500)]  // 0.65 rate
    [InlineData("JPY", 10000, 10000)] // Unknown = assume USD
    public async Task ExecuteAsync_ConvertsCurrencyToUsd(string currency, decimal amount, decimal expectedUsd)
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[] { ("Fee", "Test", amount, currency, "OneTime", false, 0.95) }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Equal(expectedUsd, data.GrandTotalUsd);
    }

    #endregion

    #region Currencies Found Tests

    [Fact]
    public async Task ExecuteAsync_TracksAllCurrenciesFound()
    {
        // Arrange
        var context = CreateValidContext();
        var tool = CreateValidTool();
        var response = CreateFinancialResponse(
            items: new[]
            {
                ("Fee", "USD fee", 10000m, "USD", "OneTime", false, 0.95),
                ("Fee", "EUR fee", 10000m, "EUR", "OneTime", false, 0.90),
                ("Fee", "GBP fee", 10000m, "GBP", "OneTime", false, 0.85)
            }
        );

        _openAiClientMock
            .Setup(x => x.GetCompletionAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _handler.ExecuteAsync(context, tool, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        var data = result.GetData<FinancialAnalysisResult>();
        Assert.NotNull(data);
        Assert.Contains("USD", data.CurrenciesFound);
        Assert.Contains("EUR", data.CurrenciesFound);
        Assert.Contains("GBP", data.CurrenciesFound);
    }

    #endregion

    #region Helper Methods

    private static ToolExecutionContext CreateValidContext(
        string extractedText = "The annual license fee is $50,000 USD. Payment due within 30 days.",
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
            Name = "Financial Calculator",
            Type = ToolType.FinancialCalculator,
            Configuration = configuration
        };
    }

    private static string CreateFinancialResponse(
        (string category, string description, decimal amount, string currency, string frequency, bool isEstimate, double confidence)[]? items = null,
        (string termType, string description, string? daysOrPeriod, decimal? percentage)[]? paymentTerms = null)
    {
        items ??= Array.Empty<(string, string, decimal, string, string, bool, double)>();
        paymentTerms ??= Array.Empty<(string, string, string?, decimal?)>();

        var itemsJson = items.Select(i =>
            $@"{{
                ""category"": ""{i.category}"",
                ""description"": ""{i.description}"",
                ""amount"": {i.amount},
                ""currency"": ""{i.currency}"",
                ""frequency"": ""{i.frequency}"",
                ""isEstimate"": {i.isEstimate.ToString().ToLower()},
                ""confidence"": {i.confidence}
            }}");

        var termsJson = paymentTerms.Select(t =>
            $@"{{
                ""termType"": ""{t.termType}"",
                ""description"": ""{t.description}"",
                ""daysOrPeriod"": {(t.daysOrPeriod != null ? $"\"{t.daysOrPeriod}\"" : "null")},
                ""percentage"": {(t.percentage.HasValue ? t.percentage.Value.ToString() : "null")}
            }}");

        return $@"{{
            ""items"": [{string.Join(",", itemsJson)}],
            ""paymentTerms"": [{string.Join(",", termsJson)}]
        }}";
    }

    #endregion
}
