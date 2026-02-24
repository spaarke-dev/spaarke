using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Tools;
using Sprk.Bff.Api.Telemetry;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Finance.Tools;

public class InvoiceExtractionToolHandlerTests
{
    private readonly IInvoiceAnalysisService _invoiceAnalysisService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceExtractionToolHandler> _logger;
    private readonly InvoiceExtractionToolHandler _handler;

    public InvoiceExtractionToolHandlerTests()
    {
        _invoiceAnalysisService = Substitute.For<IInvoiceAnalysisService>();
        _telemetry = Substitute.For<FinanceTelemetry>();
        _logger = Substitute.For<ILogger<InvoiceExtractionToolHandler>>();
        _handler = new InvoiceExtractionToolHandler(_invoiceAnalysisService, _telemetry, _logger);
    }

    [Fact]
    public void ToolName_ShouldReturnInvoiceExtraction()
    {
        // Act
        var toolName = _handler.ToolName;

        // Assert
        toolName.Should().Be("InvoiceExtraction");
    }

    [Fact]
    public async Task ExecuteAsync_ValidDocumentText_ExtractsInvoiceFacts()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var documentText = "INVOICE #12345\nDate: 2024-01-15\nAmount: $15,000.00";

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = documentText,
            ["invoiceId"] = invoiceId,
            ["matterId"] = matterId
        });

        var extractionResult = new ExtractionResult
        {
            Header = new InvoiceHeader
            {
                InvoiceNumber = "12345",
                InvoiceDate = "2024-01-15",
                TotalAmount = 15000m,
                Currency = "USD",
                VendorName = "Acme Corp"
            },
            LineItems =
            [
                new BillingEventLine
                {
                    Description = "Legal services - January",
                    Amount = 15000m,
                    CostType = "Fee",
                    Currency = "USD"
                }
            ],
            ExtractionConfidence = 0.95m
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            documentText,
            Arg.Any<InvoiceHints?>(),
            Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        result.Data.Should().NotBeNull();

        var data = result.Data as dynamic;
        var invoiceIdValue = (Guid)data!.InvoiceId;
        var aiSummary = (string)data.AiSummary;
        var extractedJson = (string)data.ExtractedJson;

        invoiceIdValue.Should().Be(invoiceId);
        aiSummary.Should().NotBeNullOrEmpty();
        aiSummary.Should().Contain("12345");
        aiSummary.Should().Contain("Acme Corp");
        extractedJson.Should().NotBeNullOrEmpty();

        await _invoiceAnalysisService.Received(1).ExtractInvoiceFactsAsync(
            documentText,
            Arg.Any<InvoiceHints?>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ServiceReturnsNullHeader_ReturnsSuccessWithNoSummary()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var documentText = "Partial invoice text";

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = documentText,
            ["invoiceId"] = invoiceId
        });

        // ExtractionResult with no header (edge case — extraction returned partial data)
        var extractionResult = new ExtractionResult
        {
            Header = null!,
            LineItems = [],
            ExtractionConfidence = 0m
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            documentText,
            Arg.Any<InvoiceHints?>(),
            Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.Data as dynamic;
        var aiSummary = (string)data!.AiSummary;
        aiSummary.Should().Contain("no facts extracted");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyDocumentText_ReturnsError()
    {
        // Arrange
        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = string.Empty,
            ["invoiceId"] = Guid.NewGuid()
        });

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Document text is required");
    }

    [Fact]
    public async Task ExecuteAsync_WithoutMatterId_StillSucceeds()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var documentText = "INVOICE #12345";

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = documentText,
            ["invoiceId"] = invoiceId
            // matterId omitted
        });

        var extractionResult = new ExtractionResult
        {
            Header = new InvoiceHeader
            {
                InvoiceNumber = "12345",
                TotalAmount = 10000m,
                Currency = "USD",
                VendorName = "Test Vendor",
                InvoiceDate = "2024-01-01"
            },
            LineItems = [],
            ExtractionConfidence = 0.9m
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            Arg.Any<string>(),
            Arg.Any<InvoiceHints?>(),
            Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ExecuteAsync_AiSummaryTruncated_WhenFactsExceedLimit()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var documentText = "Large invoice with many items";

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = documentText,
            ["invoiceId"] = invoiceId
        });

        // Create invoice with many line items to exceed 5000 char limit
        var lineItems = new List<BillingEventLine>();
        for (int i = 0; i < 100; i++)
        {
            lineItems.Add(new BillingEventLine
            {
                Description = $"Line item {i} - This is a very long description that will contribute to exceeding the 5000 character limit for the AI summary field in Dataverse",
                Amount = 100m,
                CostType = "Fee",
                Currency = "USD"
            });
        }

        var extractionResult = new ExtractionResult
        {
            Header = new InvoiceHeader
            {
                InvoiceNumber = "12345",
                TotalAmount = 10000m,
                Currency = "USD",
                VendorName = "Test Vendor",
                InvoiceDate = "2024-01-01"
            },
            LineItems = lineItems.ToArray(),
            ExtractionConfidence = 0.9m
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            Arg.Any<string>(),
            Arg.Any<InvoiceHints?>(),
            Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.Data as dynamic;
        var aiSummary = (string)data!.AiSummary;

        // Should be truncated to 5000 characters
        aiSummary.Should().NotBeNullOrEmpty();
        aiSummary.Length.Should().BeLessOrEqualTo(5000);
        aiSummary.Should().EndWith("...");
    }

    [Fact]
    public async Task ExecuteAsync_ServiceThrowsException_ReturnsError()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var documentText = "Invoice text";

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = documentText,
            ["invoiceId"] = invoiceId
        });

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            Arg.Any<string>(),
            Arg.Any<InvoiceHints?>(),
            Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("AI service unavailable"));

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Extraction failed");
        result.Error.Should().Contain("AI service unavailable");
    }

    [Fact]
    public async Task ExecuteAsync_ExtractedJsonSerializedCorrectly()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var documentText = "INVOICE #12345";

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = documentText,
            ["invoiceId"] = invoiceId
        });

        var extractionResult = new ExtractionResult
        {
            Header = new InvoiceHeader
            {
                InvoiceNumber = "12345",
                InvoiceDate = "2024-01-15",
                TotalAmount = 15000m,
                Currency = "USD",
                VendorName = "Test Corp"
            },
            LineItems =
            [
                new BillingEventLine
                {
                    Description = "Legal services",
                    Amount = 15000m,
                    CostType = "Fee",
                    Currency = "USD"
                }
            ],
            ExtractionConfidence = 0.95m
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            Arg.Any<string>(),
            Arg.Any<InvoiceHints?>(),
            Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeTrue();
        var data = result.Data as dynamic;
        var extractedJson = (string)data!.ExtractedJson;

        extractedJson.Should().NotBeNullOrEmpty();
        extractedJson.Should().Contain("\"invoiceNumber\""); // camelCase
        extractedJson.Should().Contain("12345");
        extractedJson.Should().Contain("\"lineItems\"");
        extractedJson.Should().Contain("\"description\"");
    }
}
