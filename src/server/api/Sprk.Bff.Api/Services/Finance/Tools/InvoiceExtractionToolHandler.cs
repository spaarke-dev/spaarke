using System.Text.Json;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance.Tools;

/// <summary>
/// Tool handler that extracts structured invoice data using AI.
/// Wraps InvoiceAnalysisService and generates human-readable AI summaries.
/// Called by playbooks during invoice processing workflow.
/// </summary>
public class InvoiceExtractionToolHandler : IAiToolHandler
{
    private readonly IInvoiceAnalysisService _invoiceAnalysisService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceExtractionToolHandler> _logger;

    public const string ToolNameConst = "InvoiceExtraction";
    public string ToolName => ToolNameConst;

    public InvoiceExtractionToolHandler(
        IInvoiceAnalysisService invoiceAnalysisService,
        FinanceTelemetry telemetry,
        ILogger<InvoiceExtractionToolHandler> logger)
    {
        _invoiceAnalysisService = invoiceAnalysisService ?? throw new ArgumentNullException(nameof(invoiceAnalysisService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Extracts invoice data from document text.
    /// Expected parameters:
    /// - documentText: string (OCR text from invoice document)
    /// - invoiceId: Guid (ID of invoice record)
    /// - matterId: Guid (optional, ID of matter/project for context)
    /// </summary>
    public async Task<PlaybookToolResult> ExecuteAsync(ToolParameters parameters, CancellationToken ct)
    {
        using var activity = _telemetry.StartActivity("InvoiceExtraction.Execute");

        try
        {
            var documentText = parameters.GetString("documentText");
            var invoiceId = parameters.GetGuid("invoiceId");
            var matterId = parameters.TryGetGuid("matterId", out var matterIdValue) ? matterIdValue : Guid.Empty;

            if (string.IsNullOrWhiteSpace(documentText))
            {
                return PlaybookToolResult.CreateError("Document text is required");
            }

            _logger.LogInformation(
                "Extracting invoice data for invoice {InvoiceId}, matter {MatterId}",
                invoiceId, matterId);

            // Call existing InvoiceAnalysisService (returns ExtractionResult directly)
            var extractionResult = await _invoiceAnalysisService.ExtractInvoiceFactsAsync(
                documentText,
                reviewerHints: null,
                ct);

            // Generate AI summary from extracted data
            var aiSummary = GenerateAiSummary(extractionResult);

            // Serialize extraction result to JSON for storage
            var extractedJson = JsonSerializer.Serialize(extractionResult, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            _logger.LogInformation(
                "Successfully extracted invoice data for invoice {InvoiceId}. Line items: {LineItemCount}, Total: {TotalAmount}",
                invoiceId,
                extractionResult.LineItems?.Length ?? 0,
                extractionResult.Header?.TotalAmount ?? 0);

            return PlaybookToolResult.CreateSuccess(new
            {
                InvoiceId = invoiceId,
                AiSummary = aiSummary,
                ExtractedJson = extractedJson,
                Facts = extractionResult
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice extraction failed");
            return PlaybookToolResult.CreateError($"Extraction failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Generates a human-readable AI summary from extracted invoice facts.
    /// Maximum 5000 characters to fit in sprk_aisummary field.
    /// </summary>
    private static string GenerateAiSummary(ExtractionResult extractionResult)
    {
        if (extractionResult == null || extractionResult.Header == null)
        {
            return "Unable to generate summary - no facts extracted.";
        }

        var header = extractionResult.Header;
        var summary = new System.Text.StringBuilder();

        // Invoice header
        summary.AppendLine($"Invoice #{header.InvoiceNumber ?? "N/A"} from {header.VendorName ?? "Unknown Vendor"}");

        if (!string.IsNullOrEmpty(header.InvoiceDate))
        {
            summary.AppendLine($"Date: {header.InvoiceDate}");
        }

        summary.AppendLine($"Total: {header.Currency ?? "USD"} {header.TotalAmount:N2}");

        if (!string.IsNullOrEmpty(header.PaymentTerms))
        {
            summary.AppendLine($"Payment Terms: {header.PaymentTerms}");
        }

        summary.AppendLine();

        // Line items summary
        if (extractionResult.LineItems != null && extractionResult.LineItems.Length > 0)
        {
            summary.AppendLine($"Line Items ({extractionResult.LineItems.Length}):");

            foreach (var item in extractionResult.LineItems.Take(10)) // Limit to first 10 items
            {
                var description = item.Description?.Length > 60
                    ? item.Description.Substring(0, 57) + "..."
                    : item.Description ?? "N/A";

                var hours = item.Hours.HasValue ? $"{item.Hours.Value:N2}" : "N/A";
                var rate = item.Rate.HasValue ? $"{item.Rate.Value:N2}" : "N/A";
                var amount = $"{item.Amount:N2}";
                var costType = item.CostType ?? "N/A";

                summary.AppendLine($"  - {description} | Type: {costType} | Hours: {hours} | Rate: {rate} | Amt: {amount}");
            }

            if (extractionResult.LineItems.Length > 10)
            {
                summary.AppendLine($"  ... and {extractionResult.LineItems.Length - 10} more items");
            }
        }
        else
        {
            summary.AppendLine("No line items extracted.");
        }

        // Truncate if exceeds field limit (5000 chars)
        var result = summary.ToString();
        if (result.Length > 5000)
        {
            result = result.Substring(0, 4997) + "...";
        }

        return result;
    }
}
