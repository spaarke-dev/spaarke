using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
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

        var extractionResult = new InvoiceExtractionResult
        {
            Success = true,
            Facts = new InvoiceFacts
            {
                InvoiceNumber = "12345",
                InvoiceDate = new DateTime(2024, 1, 15),
                TotalAmount = 15000m,
                Currency = "USD",
                VendorName = "Acme Corp",
                LineItems = new List<InvoiceLineItem>
                {
                    new()
                    {
                        Description = "Legal services - January",
                        Quantity = 1,
                        UnitPrice = 15000m,
                        Amount = 15000m
                    }
                }
            }
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            documentText,
            Arg.Any<string>(),
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
        var facts = data.Facts as InvoiceFacts;

        invoiceIdValue.Should().Be(invoiceId);
        aiSummary.Should().NotBeNullOrEmpty();
        aiSummary.Should().Contain("12345");
        aiSummary.Should().Contain("Acme Corp");
        extractedJson.Should().NotBeNullOrEmpty();
        facts.Should().NotBeNull();
        facts!.InvoiceNumber.Should().Be("12345");
        facts.TotalAmount.Should().Be(15000m);

        await _invoiceAnalysisService.Received(1).ExtractInvoiceFactsAsync(
            documentText,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_ExtractionFails_ReturnsError()
    {
        // Arrange
        var invoiceId = Guid.NewGuid();
        var documentText = "Invalid invoice text";

        var parameters = new ToolParameters(new Dictionary<string, object>
        {
            ["documentText"] = documentText,
            ["invoiceId"] = invoiceId
        });

        var extractionResult = new InvoiceExtractionResult
        {
            Success = false,
            Error = "Unable to extract invoice facts"
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(
            documentText,
            Arg.Any<string>(),
            Arg.Any<CancellationToken>())
            .Returns(extractionResult);

        // Act
        var result = await _handler.ExecuteAsync(parameters, CancellationToken.None);

        // Assert
        result.Success.Should().BeFalse();
        result.Error.Should().Contain("Extraction failed");
        result.Error.Should().Contain("Unable to extract invoice facts");
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

        var extractionResult = new InvoiceExtractionResult
        {
            Success = true,
            Facts = new InvoiceFacts
            {
                InvoiceNumber = "12345",
                TotalAmount = 10000m
            }
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
        var lineItems = new List<InvoiceLineItem>();
        for (int i = 0; i < 100; i++)
        {
            lineItems.Add(new InvoiceLineItem
            {
                Description = $"Line item {i} - This is a very long description that will contribute to exceeding the 5000 character limit for the AI summary field in Dataverse",
                Quantity = 1,
                UnitPrice = 100m,
                Amount = 100m
            });
        }

        var extractionResult = new InvoiceExtractionResult
        {
            Success = true,
            Facts = new InvoiceFacts
            {
                InvoiceNumber = "12345",
                TotalAmount = 10000m,
                LineItems = lineItems
            }
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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

        var extractionResult = new InvoiceExtractionResult
        {
            Success = true,
            Facts = new InvoiceFacts
            {
                InvoiceNumber = "12345",
                InvoiceDate = new DateTime(2024, 1, 15),
                TotalAmount = 15000m,
                Currency = "USD",
                LineItems = new List<InvoiceLineItem>
                {
                    new()
                    {
                        Description = "Legal services",
                        Amount = 15000m
                    }
                }
            }
        };

        _invoiceAnalysisService.ExtractInvoiceFactsAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
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
