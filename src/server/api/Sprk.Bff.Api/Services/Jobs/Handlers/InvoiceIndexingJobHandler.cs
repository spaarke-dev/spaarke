using System.Diagnostics;
using System.Text.Json;
using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Models;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for invoice indexing into Azure AI Search.
/// Indexes invoices with embeddings for semantic search and filtering.
///
/// Follows ADR-013: AI via BFF API (not separate service).
/// Follows ADR-015: No content logging (IDs only).
/// </summary>
public class InvoiceIndexingJobHandler : IJobHandler
{
    private readonly SearchIndexClient _searchIndexClient;
    private readonly IOpenAiClient _openAiClient;
    private readonly IDataverseService _dataverseService;
    private readonly TextExtractorService _textExtractorService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceIndexingJobHandler> _logger;

    /// <summary>
    /// Job type constant - must match the JobType used when enqueuing.
    /// </summary>
    public const string JobTypeName = "InvoiceIndexing";

    /// <summary>
    /// Index name pattern: spaarke-invoices-{tenantId}
    /// For MVP, using a single index: spaarke-invoices-dev
    /// </summary>
    private const string IndexNamePattern = "spaarke-invoices-dev";

    public InvoiceIndexingJobHandler(
        SearchIndexClient searchIndexClient,
        IOpenAiClient openAiClient,
        IDataverseService dataverseService,
        TextExtractorService textExtractorService,
        FinanceTelemetry telemetry,
        ILogger<InvoiceIndexingJobHandler> logger)
    {
        _searchIndexClient = searchIndexClient ?? throw new ArgumentNullException(nameof(searchIndexClient));
        _openAiClient = openAiClient ?? throw new ArgumentNullException(nameof(openAiClient));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _textExtractorService = textExtractorService ?? throw new ArgumentNullException(nameof(textExtractorService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogInformation(
                "Processing invoice indexing job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload to get invoiceId and documentId
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.InvoiceId == Guid.Empty || payload.DocumentId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for invoice indexing job {JobId}", job.JobId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var invoiceId = payload.InvoiceId;
            var documentId = payload.DocumentId;

            _logger.LogDebug(
                "Indexing invoice {InvoiceId}, document {DocumentId}",
                invoiceId, documentId);

            // Step 1: Load invoice record from Dataverse
            var invoice = await LoadInvoiceRecordAsync(invoiceId, ct);
            if (invoice == null)
            {
                _logger.LogError("Invoice {InvoiceId} not found in Dataverse", invoiceId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Invoice record not found", job.Attempt, stopwatch.Elapsed);
            }

            // Step 2: Load document record from Dataverse
            var document = await LoadDocumentRecordAsync(documentId, ct);
            if (document == null)
            {
                _logger.LogError("Document {DocumentId} not found in Dataverse", documentId);
                return JobOutcome.Poisoned(job.JobId, JobType, "Document record not found", job.Attempt, stopwatch.Elapsed);
            }

            // Step 3: Get document text (from extraction results if available)
            string documentText;
            if (!string.IsNullOrWhiteSpace(document.ExtractedText))
            {
                _logger.LogDebug("Using cached extracted text for document {DocumentId}", documentId);
                documentText = document.ExtractedText;
            }
            else
            {
                _logger.LogWarning(
                    "Document {DocumentId} has no extracted text, indexing will proceed with empty content",
                    documentId);
                documentText = string.Empty;
            }

            // Step 4: Generate embedding for document text
            ReadOnlyMemory<float> embedding;
            if (!string.IsNullOrWhiteSpace(documentText))
            {
                try
                {
                    embedding = await _openAiClient.GenerateEmbeddingAsync(
                        documentText,
                        model: "text-embedding-3-large",
                        dimensions: 3072,
                        cancellationToken: ct);

                    _logger.LogDebug(
                        "Generated embedding for document {DocumentId}, dimensions: {Dimensions}",
                        documentId, embedding.Length);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to generate embedding for document {DocumentId}: {Error}. Indexing without vector.",
                        documentId, ex.Message);

                    // Continue without embedding - allow keyword search only
                    embedding = ReadOnlyMemory<float>.Empty;
                }
            }
            else
            {
                // No text to embed
                embedding = ReadOnlyMemory<float>.Empty;
            }

            // Step 5: Create search document
            var searchDocument = CreateSearchDocument(
                invoiceId,
                documentId,
                invoice,
                document,
                documentText,
                embedding);

            // Step 6: Index document into Azure AI Search
            var searchClient = _searchIndexClient.GetSearchClient(IndexNamePattern);

            try
            {
                var batch = new[]
                {
                    new IndexDocumentsAction<SearchDocument>(
                        IndexActionType.MergeOrUpload,
                        searchDocument)
                };

                var response = await searchClient.IndexDocumentsAsync(
                    IndexDocumentsBatch.Create(batch),
                    new IndexDocumentsOptions { ThrowOnAnyError = true },
                    cancellationToken: ct);

                var result = response.Value.Results.FirstOrDefault();
                if (result?.Succeeded != true)
                {
                    _logger.LogError(
                        "Failed to index invoice {InvoiceId}: {Error}",
                        invoiceId, result?.ErrorMessage ?? "Unknown error");

                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        result?.ErrorMessage ?? "Indexing failed",
                        job.Attempt, stopwatch.Elapsed);
                }

                _logger.LogInformation(
                    "Successfully indexed invoice {InvoiceId} into Azure AI Search in {Duration}ms",
                    invoiceId, stopwatch.ElapsedMilliseconds);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }
            catch (RequestFailedException ex)
            {
                _logger.LogError(ex,
                    "Azure AI Search request failed for invoice {InvoiceId}: {Error}",
                    invoiceId, ex.Message);

                // Check if this is a retryable error
                var isRetryable = ex.Status is 429 or 503; // Throttling or Service Unavailable
                if (isRetryable)
                {
                    return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
                }

                // Permanent failure
                return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invoice indexing job {JobId} failed: {Error}", job.JobId, ex.Message);

            // Determine if retryable
            var isRetryable = IsRetryableException(ex);
            if (isRetryable)
            {
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            // Permanent failure
            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private InvoiceIndexingPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<InvoiceIndexingPayload>(payload, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse invoice indexing job payload");
            return null;
        }
    }

    /// <summary>
    /// Load invoice record from Dataverse.
    /// Note: This is a placeholder - actual implementation needs proper Dataverse query.
    /// </summary>
    private async Task<InvoiceSearchRecord?> LoadInvoiceRecordAsync(Guid invoiceId, CancellationToken ct)
    {
        try
        {
            // TODO: Implement actual Dataverse query when sprk_invoice entity methods are added
            // For now, return minimal record to allow compilation
            // In production, this would query sprk_invoice entity with fields:
            // - sprk_invoicenumber, sprk_invoicedate, sprk_totalamount, sprk_currency
            // - sprk_matter (lookup), sprk_vendororg (lookup)
            // - sprk_extractionconfidence, sprk_reviewstatus

            _logger.LogWarning(
                "LoadInvoiceRecordAsync not fully implemented - using placeholder. Invoice {InvoiceId}",
                invoiceId);

            await Task.CompletedTask; // Suppress warning

            return new InvoiceSearchRecord
            {
                InvoiceId = invoiceId,
                InvoiceNumber = null,
                InvoiceDate = null,
                TotalAmount = null,
                Currency = null,
                MatterId = null,
                MatterNumber = null,
                MatterName = null,
                VendorOrgId = null,
                VendorName = null,
                Confidence = null,
                ReviewStatus = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load invoice {InvoiceId} from Dataverse", invoiceId);
            return null;
        }
    }

    /// <summary>
    /// Load document record from Dataverse to get extracted text and metadata.
    /// </summary>
    private async Task<DocumentSearchRecord?> LoadDocumentRecordAsync(Guid documentId, CancellationToken ct)
    {
        try
        {
            var document = await _dataverseService.GetDocumentAsync(documentId.ToString(), ct);
            if (document == null)
                return null;

            // Check if document has extracted text (from document intelligence or OCR)
            // This may be stored in a custom field like sprk_extractedtext
            // For now, we'll assume text extraction happens elsewhere and is cached

            return new DocumentSearchRecord
            {
                DocumentId = documentId,
                FileName = document.FileName ?? "unknown",
                ExtractedText = null, // TODO: Load from actual field when available
                TenantId = null, // TODO: Load from actual tenant context
                ProjectId = null // TODO: Load if document has project association
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load document {DocumentId} from Dataverse", documentId);
            return null;
        }
    }

    /// <summary>
    /// Create search document matching the invoice index schema.
    /// Schema from infrastructure/ai-search/invoice-index-schema.json
    /// </summary>
    private SearchDocument CreateSearchDocument(
        Guid invoiceId,
        Guid documentId,
        InvoiceSearchRecord invoice,
        DocumentSearchRecord document,
        string documentText,
        ReadOnlyMemory<float> embedding)
    {
        var searchDoc = new SearchDocument
        {
            // Primary key: composite of invoice and document IDs for uniqueness
            ["id"] = $"{invoiceId:N}_{documentId:N}",

            // Content fields
            ["content"] = documentText,

            // Vector field (3072 dimensions for text-embedding-3-large)
            ["contentVector"] = embedding.Length > 0 ? embedding.ToArray() : null,

            // Chunk index (always 0 for full invoice documents, not chunked)
            ["chunkIndex"] = 0,

            // Invoice identifiers
            ["invoiceId"] = invoiceId.ToString(),
            ["documentId"] = documentId.ToString(),
            ["matterId"] = invoice.MatterId?.ToString(),
            ["projectId"] = document.ProjectId?.ToString(),
            ["vendorOrgId"] = invoice.VendorOrgId?.ToString(),

            // Invoice metadata
            ["vendorName"] = invoice.VendorName,
            ["invoiceNumber"] = invoice.InvoiceNumber,
            ["invoiceDate"] = invoice.InvoiceDate,
            ["totalAmount"] = invoice.TotalAmount,
            ["currency"] = invoice.Currency,

            // Document metadata
            ["documentType"] = "invoice", // Static value for filtering

            // Tenant isolation
            ["tenantId"] = document.TenantId ?? "default",

            // Indexing timestamp
            ["indexedAt"] = DateTimeOffset.UtcNow
        };

        return searchDoc;
    }

    /// <summary>
    /// Determines if an exception represents a transient failure that should be retried.
    /// </summary>
    private static bool IsRetryableException(Exception ex)
    {
        // HTTP 429 (throttling), 503 (service unavailable), timeouts
        if (ex is HttpRequestException)
        {
            return true;
        }

        // RequestFailedException with retryable status codes
        if (ex is RequestFailedException rfe)
        {
            return rfe.Status is 429 or 503;
        }

        // Check for known transient exception types
        var exceptionName = ex.GetType().Name;
        return exceptionName.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Timeout", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Transient", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>
/// Payload structure for invoice indexing jobs.
/// </summary>
public class InvoiceIndexingPayload
{
    /// <summary>
    /// The Dataverse invoice ID to index.
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
internal record InvoiceSearchRecord
{
    public Guid InvoiceId { get; init; }
    public string? InvoiceNumber { get; init; }
    public DateTimeOffset? InvoiceDate { get; init; }
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
    public Guid? MatterId { get; init; }
    public string? MatterNumber { get; init; }
    public string? MatterName { get; init; }
    public Guid? VendorOrgId { get; init; }
    public string? VendorName { get; init; }
    public decimal? Confidence { get; init; }
    public int? ReviewStatus { get; init; }
}

/// <summary>
/// Internal record for document data loaded from Dataverse.
/// </summary>
internal record DocumentSearchRecord
{
    public Guid DocumentId { get; init; }
    public string FileName { get; init; } = null!;
    public string? ExtractedText { get; init; }
    public string? TenantId { get; init; }
    public Guid? ProjectId { get; init; }
}
