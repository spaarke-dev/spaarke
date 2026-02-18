using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Finance;
using Sprk.Bff.Api.Services.Finance.Models;
using Sprk.Bff.Api.Services.Finance.Tools;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for invoice extraction jobs.
/// Extracts invoice facts via AI and updates Dataverse records via OutputOrchestrator.
/// MVP: Stores extraction as JSON on invoice, updates matter totals (no BillingEvents).
///
/// Architecture: Uses OutputOrchestrator pattern for declarative playbook-driven updates.
/// Follows ADR-013: AI via BFF API (not separate service).
/// Follows ADR-014: Playbook-based prompts (FinanceExtraction).
/// Follows ADR-015: No content logging (IDs only).
/// </summary>
public class InvoiceExtractionJobHandler : IJobHandler
{
    private readonly IInvoiceAnalysisService _invoiceAnalysisService;
    private readonly ISpeFileOperations _speFileOperations;
    private readonly TextExtractorService _textExtractorService;
    private readonly IDataverseService _dataverseService;
    private readonly IOutputOrchestratorService _outputOrchestrator;
    private readonly IPlaybookLookupService _playbookLookup;
    private readonly FinancialCalculationToolHandler _financialCalculationTool;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceExtractionJobHandler> _logger;

    // Extraction status choice values
    private const int ExtractionStatusExtracted = 100000001;
    private const int ExtractionStatusFailed = 100000002;

    /// <summary>
    /// Job type constant - must match the JobType used by InvoiceReviewService.
    /// </summary>
    public const string JobTypeName = "InvoiceExtraction";

    public InvoiceExtractionJobHandler(
        IInvoiceAnalysisService invoiceAnalysisService,
        ISpeFileOperations speFileOperations,
        TextExtractorService textExtractorService,
        IDataverseService dataverseService,
        IOutputOrchestratorService outputOrchestrator,
        IPlaybookLookupService playbookLookup,
        FinancialCalculationToolHandler financialCalculationTool,
        JobSubmissionService jobSubmissionService,
        FinanceTelemetry telemetry,
        ILogger<InvoiceExtractionJobHandler> logger)
    {
        _invoiceAnalysisService = invoiceAnalysisService ?? throw new ArgumentNullException(nameof(invoiceAnalysisService));
        _speFileOperations = speFileOperations ?? throw new ArgumentNullException(nameof(speFileOperations));
        _textExtractorService = textExtractorService ?? throw new ArgumentNullException(nameof(textExtractorService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _outputOrchestrator = outputOrchestrator ?? throw new ArgumentNullException(nameof(outputOrchestrator));
        _playbookLookup = playbookLookup ?? throw new ArgumentNullException(nameof(playbookLookup));
        _financialCalculationTool = financialCalculationTool ?? throw new ArgumentNullException(nameof(financialCalculationTool));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();
        using var activity = _telemetry.StartActivity("InvoiceExtraction.ProcessJob", correlationId: job.CorrelationId);

        var invoiceIdStr = string.Empty;
        var documentIdStr = string.Empty;

        try
        {
            _logger.LogInformation(
                "Processing invoice extraction job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload to get invoiceId and documentId
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.InvoiceId == Guid.Empty || payload.DocumentId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for invoice extraction job {JobId}", job.JobId);
                _telemetry.RecordExtractionFailure(stopwatch, job.SubjectId, "invalid_payload");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var invoiceId = payload.InvoiceId;
            var documentId = payload.DocumentId;
            invoiceIdStr = invoiceId.ToString();
            documentIdStr = documentId.ToString();

            _logger.LogDebug(
                "Processing invoice {InvoiceId}, document {DocumentId}",
                invoiceId, documentId);

            // Start extraction telemetry
            var extractionStopwatch = _telemetry.RecordExtractionStart(documentIdStr);

            // Load sprk_invoice record from Dataverse to get reviewer corrections
            var invoice = await LoadInvoiceRecordAsync(invoiceId, ct);
            if (invoice == null)
            {
                _logger.LogError("Invoice {InvoiceId} not found in Dataverse", invoiceId);
                _telemetry.RecordExtractionFailure(extractionStopwatch, documentIdStr, "invoice_not_found");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invoice record not found", job.Attempt, stopwatch.Elapsed);
            }

            // Load sprk_document record from Dataverse to get SPE file info
            var document = await LoadDocumentRecordAsync(documentId, ct);
            if (document == null)
            {
                _logger.LogError("Document {DocumentId} not found in Dataverse", documentId);
                _telemetry.RecordExtractionFailure(extractionStopwatch, documentIdStr, "document_not_found");
                return JobOutcome.Poisoned(job.JobId, JobType, "Document record not found", job.Attempt, stopwatch.Elapsed);
            }

            var driveId = document.GraphDriveId;
            var itemId = document.GraphItemId;
            var fileName = document.FileName;

            if (string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId))
            {
                _logger.LogError(
                    "Document {DocumentId} missing SPE file info (driveId: {DriveId}, itemId: {ItemId})",
                    documentId, driveId, itemId);
                _telemetry.RecordExtractionFailure(extractionStopwatch, documentIdStr, "missing_file_info");
                return JobOutcome.Poisoned(job.JobId, JobType, "Document missing SPE file info", job.Attempt, stopwatch.Elapsed);
            }

            // Download document from SPE
            var fileStream = await _speFileOperations.DownloadFileAsync(driveId, itemId, ct);
            if (fileStream == null)
            {
                _logger.LogError("Failed to download document {DocumentId} from SPE", documentId);
                _telemetry.RecordExtractionFailure(extractionStopwatch, documentIdStr, "download_failed");

                // Mark invoice as Failed and return Success to prevent retry
                await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Extract text from document
            string documentText;
            try
            {
                using (fileStream)
                {
                    var textExtractionResult = await _textExtractorService.ExtractAsync(fileStream, fileName, ct);

                    if (!textExtractionResult.Success || string.IsNullOrWhiteSpace(textExtractionResult.Text))
                    {
                        _logger.LogError(
                            "Text extraction failed for document {DocumentId}: {ErrorMessage}",
                            documentId, textExtractionResult.ErrorMessage);
                        _telemetry.RecordExtractionFailure(extractionStopwatch, documentIdStr, "text_extraction_failed");

                        // Mark invoice as Failed and return Success to prevent retry
                        await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                        return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
                    }

                    documentText = textExtractionResult.Text;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from document {DocumentId}: {Error}", documentId, ex.Message);
                _telemetry.RecordExtractionFailure(extractionStopwatch, documentIdStr, "text_extraction_error");

                // Mark invoice as Failed and return Success to prevent retry
                await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            _logger.LogDebug(
                "Extracted {TextLength} characters from document {DocumentId}",
                documentText.Length, documentId);

            // Prepare reviewer hints from invoice record
            var reviewerHints = new InvoiceHints
            {
                VendorName = null, // Vendor lookup resolved separately
                InvoiceNumber = null, // Not stored on invoice record yet
                InvoiceDate = null, // Not stored on invoice record yet
                TotalAmount = null, // Not stored on invoice record yet
                Currency = null, // Not stored on invoice record yet
                MatterReference = null // Matter lookup resolved separately
            };

            // Call AI extraction service
            ExtractionResult aiExtractionResult;
            try
            {
                aiExtractionResult = await _invoiceAnalysisService.ExtractInvoiceFactsAsync(
                    documentText,
                    reviewerHints,
                    ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AI extraction failed for document {DocumentId}: {Error}", documentId, ex.Message);
                _telemetry.RecordExtractionFailure(extractionStopwatch, documentIdStr, "ai_extraction_failed");

                // Mark invoice as Failed and return Success to prevent retry
                await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            _logger.LogInformation(
                "AI extraction completed for document {DocumentId}: {LineItemCount} line items, confidence {Confidence}",
                documentId, aiExtractionResult.LineItems.Length, aiExtractionResult.ExtractionConfidence);

            // Build PlaybookExecutionContext with variables for OutputOrchestrator
            var context = new PlaybookExecutionContext();

            // context.* variables (from job payload)
            context.SetVariable("context.invoiceId", invoiceId.ToString());
            context.SetVariable("context.documentId", documentId.ToString());
            context.SetVariable("context.matterId", (invoice.MatterId ?? Guid.Empty).ToString());

            // extraction.* variables (from AI extraction result)
            context.SetVariable("extraction.aiSummary", GenerateAiSummary(aiExtractionResult));
            context.SetVariable("extraction.extractedJson", JsonSerializer.Serialize(aiExtractionResult, new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
            context.SetVariable("extraction.totalAmount", aiExtractionResult.Header?.TotalAmount ?? 0);
            context.SetVariable("extraction.invoiceNumber", aiExtractionResult.Header?.InvoiceNumber ?? string.Empty);
            context.SetVariable("extraction.invoiceDate", aiExtractionResult.Header?.InvoiceDate ?? string.Empty);
            context.SetVariable("extraction.currency", aiExtractionResult.Header?.Currency ?? "USD");

            // calculation.* variables from FinancialCalculationToolHandler (TL-011)
            // Calculates matter-level aggregates: total spend, invoice count, budget, remaining budget
            var calculationParams = new ToolParameters(new Dictionary<string, object>
            {
                ["matterId"] = invoice.MatterId ?? Guid.Empty
            });

            var calculationResult = await _financialCalculationTool.ExecuteAsync(calculationParams, ct);
            if (calculationResult.Success && calculationResult.Data != null)
            {
                try
                {
                    var dataString = calculationResult.Data.ToString();
                    if (!string.IsNullOrEmpty(dataString))
                    {
                        var calculationData = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                            dataString,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                        if (calculationData != null)
                        {
                            context.SetVariable("calculation.totalSpend",
                                calculationData.ContainsKey("totalSpend") ? calculationData["totalSpend"].GetDecimal() : 0);
                            context.SetVariable("calculation.invoiceCount",
                                calculationData.ContainsKey("invoiceCount") ? calculationData["invoiceCount"].GetInt32() : 0);
                            context.SetVariable("calculation.remainingBudget",
                                calculationData.ContainsKey("remainingBudget") ? calculationData["remainingBudget"].GetDecimal() : 0);
                            context.SetVariable("calculation.budgetUtilization",
                                calculationData.ContainsKey("budgetUtilization") ? calculationData["budgetUtilization"].GetDecimal() : 0);

                            _logger.LogDebug(
                                "Financial calculation succeeded for matter {MatterId}: TotalSpend={TotalSpend}, " +
                                "InvoiceCount={InvoiceCount}, RemainingBudget={RemainingBudget}",
                                invoice.MatterId,
                                calculationData.ContainsKey("totalSpend") ? calculationData["totalSpend"].GetDecimal() : 0,
                                calculationData.ContainsKey("invoiceCount") ? calculationData["invoiceCount"].GetInt32() : 0,
                                calculationData.ContainsKey("remainingBudget") ? calculationData["remainingBudget"].GetDecimal() : 0);
                        }
                    }
                }
                catch (JsonException ex)
                {
                    _logger.LogWarning(ex, "Failed to parse financial calculation result, using defaults");
                    // Fallback to defaults
                    context.SetVariable("calculation.totalSpend", 0);
                    context.SetVariable("calculation.invoiceCount", 0);
                    context.SetVariable("calculation.remainingBudget", 0);
                }
            }
            else
            {
                _logger.LogWarning(
                    "Financial calculation failed for matter {MatterId}: {Error}, using defaults",
                    invoice.MatterId, calculationResult.Error ?? "Unknown error");

                // Fallback to defaults
                context.SetVariable("calculation.totalSpend", 0);
                context.SetVariable("calculation.invoiceCount", 0);
                context.SetVariable("calculation.remainingBudget", 0);
            }

            _logger.LogDebug(
                "PlaybookExecutionContext built with {VariableCount} variables for invoice {InvoiceId}",
                context.Variables.Count, invoiceId);

            // Apply outputMapping via OutputOrchestrator
            // Look up playbook by portable code (works in all environments - DEV/QA/PROD)
            // Uses alternate key "sprk_playbookcode" = "PB-013" instead of environment-specific GUID
            // Result is cached for 1 hour to minimize Dataverse queries
            var playbook = await _playbookLookup.GetByCodeAsync("PB-013", ct);
            var outputResult = await _outputOrchestrator.ApplyOutputMappingAsync(playbook.Id, context, ct);

            if (!outputResult.Success)
            {
                _logger.LogError(
                    "OutputOrchestrator failed for invoice {InvoiceId}: {ErrorMessage}",
                    invoiceId, outputResult.ErrorMessage);

                // Mark invoice as Failed and return Success to prevent retry
                await UpdateInvoiceExtractionStatusAsync(invoiceId, ExtractionStatusFailed, ct);
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            _logger.LogInformation(
                "OutputOrchestrator succeeded for invoice {InvoiceId}: {UpdateCount} entities updated",
                invoiceId, outputResult.Updates.Count);

            // Enqueue InvoiceIndexing job
            await EnqueueInvoiceIndexingJobAsync(invoiceId, documentId, job.CorrelationId, ct);

            _telemetry.RecordExtractionSuccess(extractionStopwatch, documentIdStr);

            _logger.LogInformation(
                "Invoice extraction job {JobId} completed in {Duration}ms. Invoice {InvoiceId} extracted with {LineItemCount} line items and updated via OutputOrchestrator",
                job.JobId, stopwatch.ElapsedMilliseconds, invoiceId, aiExtractionResult.LineItems?.Length ?? 0);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice extraction job {JobId} failed: {Error}", job.JobId, ex.Message);

            // Determine if retryable
            var isRetryable = IsRetryableException(ex);
            var isLastAttempt = job.Attempt >= job.MaxAttempts;

            if (isRetryable && !isLastAttempt)
            {
                _logger.LogWarning(
                    "Invoice extraction job {JobId} failed (attempt {Attempt}/{MaxAttempts}), will retry: {Error}",
                    job.JobId, job.Attempt, job.MaxAttempts, ex.Message);
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            // Permanent failure - mark invoice as Failed
            // Note: We extract invoiceId from payload again since we may have failed before loading it
            var payload = ParsePayload(job.Payload);
            if (payload != null && payload.InvoiceId != Guid.Empty)
            {
                await UpdateInvoiceExtractionStatusAsync(payload.InvoiceId, ExtractionStatusFailed, ct);
            }

            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
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

    private InvoiceExtractionPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<InvoiceExtractionPayload>(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse invoice extraction job payload");
            return null;
        }
    }

    /// <summary>
    /// Load sprk_invoice record from Dataverse to get reviewer corrections (matterId, vendorOrgId).
    /// </summary>
    private Task<InvoiceRecord?> LoadInvoiceRecordAsync(Guid invoiceId, CancellationToken ct)
    {
        try
        {
            // Use GetDocumentAsync pattern - need to check if there's a similar method for invoices
            // For now, construct a simple record - this would need actual Dataverse query
            // This is a placeholder that would be replaced with actual query

            // NOTE: IDataverseService doesn't have generic retrieve methods for custom entities
            // We need to use UpdateDocumentFieldsAsync pattern for updates, but for reads
            // we may need to add methods or use direct DataverseServiceClientImpl

            // For this implementation, we'll assume matter and vendor are set via UpdateDocumentFieldsAsync
            // when the invoice is confirmed, so we can query the document record

            // TODO: This needs actual implementation when sprk_invoice entity methods are added
            return Task.FromResult<InvoiceRecord?>(new InvoiceRecord
            {
                InvoiceId = invoiceId,
                MatterId = null, // Would be loaded from actual query
                VendorOrgId = null // Would be loaded from actual query
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load invoice {InvoiceId} from Dataverse", invoiceId);
            return Task.FromResult<InvoiceRecord?>(null);
        }
    }

    /// <summary>
    /// Load sprk_document record from Dataverse to get SPE file info.
    /// </summary>
    private async Task<DocumentRecord?> LoadDocumentRecordAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            var document = await _dataverseService.GetDocumentAsync(documentId.ToString(), ct);
            if (document == null)
                return null;

            return new DocumentRecord
            {
                DocumentId = documentId,
                GraphDriveId = document.GraphDriveId ?? string.Empty,
                GraphItemId = document.GraphItemId ?? string.Empty,
                FileName = document.FileName ?? "unknown"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load document {DocumentId} from Dataverse", documentId);
            return null;
        }
    }


    /// <summary>
    /// Update invoice extraction status only (for failure cases).
    /// </summary>
    private async Task UpdateInvoiceExtractionStatusAsync(
        Guid invoiceId,
        int extractionStatus,
        CancellationToken ct)
    {
        try
        {
            var fields = new Dictionary<string, object?>
            {
                ["sprk_extractionstatus"] = extractionStatus
            };

            await _dataverseService.UpdateDocumentFieldsAsync(invoiceId.ToString(), fields, ct);

            _logger.LogInformation(
                "Updated invoice {InvoiceId} extraction status to Failed",
                invoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to update invoice {InvoiceId} extraction status: {Error}",
                invoiceId, ex.Message);
        }
    }


    /// <summary>
    /// Enqueue InvoiceIndexing job with invoiceId and documentId payload.
    /// </summary>
    private async Task EnqueueInvoiceIndexingJobAsync(
        Guid invoiceId,
        Guid documentId,
        string? correlationId,
        CancellationToken ct)
    {
        try
        {
            var indexingJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = "InvoiceIndexing",
                SubjectId = invoiceId.ToString(),
                CorrelationId = correlationId ?? Activity.Current?.Id ?? Guid.NewGuid().ToString(),
                IdempotencyKey = $"invoice-index-{invoiceId}",
                Attempt = 1,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    InvoiceId = invoiceId,
                    DocumentId = documentId,
                    EnqueuedAt = DateTimeOffset.UtcNow
                }))
            };

            await _jobSubmissionService.SubmitJobAsync(indexingJob, ct);

            _logger.LogInformation(
                "Enqueued InvoiceIndexing job {JobId} for invoice {InvoiceId}",
                indexingJob.JobId, invoiceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to enqueue InvoiceIndexing job for invoice {InvoiceId}: {Error}. Extraction will continue.",
                invoiceId, ex.Message);
        }
    }

    private static bool IsRetryableException(Exception ex)
    {
        // HTTP 429 (throttling), 503 (service unavailable), etc.
        if (ex is HttpRequestException)
        {
            return true;
        }

        // Check for known throttling exception types
        var exceptionName = ex.GetType().Name;
        return exceptionName.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Timeout", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Payload structure for invoice extraction jobs.
/// </summary>
public class InvoiceExtractionPayload
{
    /// <summary>
    /// The Dataverse invoice ID to extract.
    /// </summary>
    public Guid InvoiceId { get; set; }

    /// <summary>
    /// The Dataverse document ID containing the invoice file.
    /// </summary>
    public Guid DocumentId { get; set; }
}

/// <summary>
/// Internal record for invoice data loaded from Dataverse.
/// </summary>
internal record InvoiceRecord
{
    public Guid InvoiceId { get; init; }
    public Guid? MatterId { get; init; }
    public Guid? VendorOrgId { get; init; }
}

/// <summary>
/// Internal record for document data loaded from Dataverse.
/// </summary>
internal record DocumentRecord
{
    public Guid DocumentId { get; init; }
    public string GraphDriveId { get; init; } = null!;
    public string GraphItemId { get; init; } = null!;
    public string FileName { get; init; } = null!;
}
