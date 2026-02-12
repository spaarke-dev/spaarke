using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Handles human-in-the-loop invoice review confirmation and rejection.
/// After AI classification flags a document as an invoice candidate, a human reviewer confirms
/// the identification and optionally corrects extracted hints (invoice number, date, amount),
/// or rejects the document as not an invoice.
/// </summary>
/// <remarks>
/// Workflow:
/// 1. Update sprk_document: mark as ConfirmedInvoice with review timestamp
/// 2. Create sprk_invoice: link to document, matter, vendor org; set initial statuses
/// 3. Enqueue InvoiceExtraction job: triggers Playbook B (full AI extraction) asynchronously
/// 4. Return job + invoice IDs for polling
///
/// Per ADR-013: Extends BFF API (not a separate service).
/// Per ADR-015: NEVER log document content or PII. Only IDs, statuses, and timings.
/// </remarks>
public interface IInvoiceReviewService
{
    /// <summary>
    /// Confirm a document as an invoice and enqueue extraction.
    /// </summary>
    /// <param name="request">The confirmation request with document, matter, vendor, and optional corrected hints.</param>
    /// <param name="correlationId">Correlation ID for distributed tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with the enqueued job ID and created invoice ID.</returns>
    Task<InvoiceReviewResult> ConfirmInvoiceAsync(
        InvoiceReviewConfirmRequest request,
        string correlationId,
        CancellationToken ct = default);

    /// <summary>
    /// Reject a document as not an invoice.
    /// </summary>
    /// <param name="request">The rejection request with document ID and optional notes.</param>
    /// <param name="correlationId">Correlation ID for distributed tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Result with the document ID and rejection status.</returns>
    Task<InvoiceReviewRejectResult> RejectInvoiceAsync(
        InvoiceReviewRejectRequest request,
        string correlationId,
        CancellationToken ct = default);
}

/// <summary>
/// Request to confirm a document as an invoice.
/// </summary>
public record InvoiceReviewConfirmRequest
{
    /// <summary>The document to confirm as an invoice (required).</summary>
    public Guid DocumentId { get; init; }

    /// <summary>The matter this invoice relates to (required).</summary>
    public Guid MatterId { get; init; }

    /// <summary>The vendor organization (required).</summary>
    public Guid VendorOrgId { get; init; }

    /// <summary>Optional corrected invoice number from reviewer.</summary>
    public string? InvoiceNumber { get; init; }

    /// <summary>Optional corrected invoice date from reviewer.</summary>
    public DateTime? InvoiceDate { get; init; }

    /// <summary>Optional corrected total amount from reviewer.</summary>
    public decimal? TotalAmount { get; init; }

    /// <summary>Optional reviewer notes.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Result of confirming an invoice review.
/// </summary>
public record InvoiceReviewResult
{
    /// <summary>The enqueued extraction job ID.</summary>
    public Guid JobId { get; init; }

    /// <summary>The created invoice record ID.</summary>
    public Guid InvoiceId { get; init; }

    /// <summary>URL for polling job status.</summary>
    public string StatusUrl { get; init; } = string.Empty;
}

/// <summary>
/// Request to reject a document as not an invoice.
/// </summary>
public record InvoiceReviewRejectRequest
{
    /// <summary>The document to reject (required).</summary>
    public Guid DocumentId { get; init; }

    /// <summary>Optional rejection notes from reviewer.</summary>
    public string? Notes { get; init; }
}

/// <summary>
/// Result of rejecting an invoice review.
/// </summary>
public record InvoiceReviewRejectResult
{
    /// <summary>The rejected document ID.</summary>
    public Guid DocumentId { get; init; }

    /// <summary>The rejection status.</summary>
    public string Status { get; init; } = "Rejected";
}

/// <summary>
/// Implements invoice review confirmation workflow:
/// update document status, create invoice record, enqueue extraction job.
/// </summary>
public class InvoiceReviewService : IInvoiceReviewService
{
    private readonly IDataverseService _dataverseService;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceReviewService> _logger;

    // ═══════════════════════════════════════════════════════════════════════════
    // Dataverse Schema Constants
    // ═══════════════════════════════════════════════════════════════════════════

    // Entity names
    private const string DocumentEntity = "sprk_document";
    private const string InvoiceEntity = "sprk_invoice";

    // Document fields
    private const string DocInvoiceReviewStatus = "sprk_invoicereviewstatus";
    private const string DocInvoiceReviewedOn = "sprk_invoicereviewedon";
    private const string DocInvoiceReviewedBy = "sprk_invoicereviewedby";
    private const string DocInvoiceRejectionNotes = "sprk_invoicerejectionnotes";

    // Invoice review status option set values
    private const int ReviewStatusConfirmedInvoice = 100000001;
    private const int ReviewStatusRejectedNotInvoice = 100000002;

    // Invoice fields
    private const string InvDocumentLookup = "sprk_document";
    private const string InvMatterLookup = "sprk_matter";
    private const string InvVendorOrgLookup = "sprk_vendororg";
    private const string InvInvoiceStatus = "sprk_invoicestatus";
    private const string InvExtractionStatus = "sprk_extractionstatus";
    private const string InvInvoiceNumber = "sprk_invoicenumber";
    private const string InvInvoiceDate = "sprk_invoicedate";
    private const string InvTotalAmount = "sprk_totalamount";
    private const string InvReviewerNotes = "sprk_reviewernotes";
    private const string InvCreatedOn = "sprk_createdon";

    // Invoice status option set values
    private const int InvoiceStatusToReview = 100000000;

    // Extraction status option set values
    private const int ExtractionStatusNotRun = 100000000;

    // Job type for extraction
    private const string JobTypeInvoiceExtraction = "InvoiceExtraction";

    public InvoiceReviewService(
        IDataverseService dataverseService,
        JobSubmissionService jobSubmissionService,
        FinanceTelemetry telemetry,
        ILogger<InvoiceReviewService> logger)
    {
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InvoiceReviewResult> ConfirmInvoiceAsync(
        InvoiceReviewConfirmRequest request,
        string correlationId,
        CancellationToken ct = default)
    {
        using var activity = _telemetry.StartActivity(
            "InvoiceReview.Confirm",
            request.DocumentId.ToString(),
            correlationId);

        _logger.LogInformation(
            "Starting invoice review confirmation. DocumentId={DocumentId}, MatterId={MatterId}, " +
            "VendorOrgId={VendorOrgId}, CorrelationId={CorrelationId}",
            request.DocumentId, request.MatterId, request.VendorOrgId, correlationId);

        // Step 1: Update document — mark as ConfirmedInvoice
        await UpdateDocumentReviewStatusAsync(request.DocumentId, ct);

        // Step 2: Create invoice record linked to document, matter, and vendor org
        var invoiceId = await CreateInvoiceRecordAsync(request, ct);

        // Step 3: Enqueue InvoiceExtraction job
        var jobId = Guid.NewGuid();
        await EnqueueExtractionJobAsync(jobId, invoiceId, request.DocumentId, correlationId, ct);

        _logger.LogInformation(
            "Invoice review confirmation completed. DocumentId={DocumentId}, InvoiceId={InvoiceId}, " +
            "JobId={JobId}, CorrelationId={CorrelationId}",
            request.DocumentId, invoiceId, jobId, correlationId);

        return new InvoiceReviewResult
        {
            JobId = jobId,
            InvoiceId = invoiceId,
            StatusUrl = $"/api/finance/jobs/{jobId}/status"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 1: Update Document Review Status
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mark the document as ConfirmedInvoice with the current review timestamp.
    /// </summary>
    private async Task UpdateDocumentReviewStatusAsync(Guid documentId, CancellationToken ct)
    {
        var fields = new Dictionary<string, object?>
        {
            [DocInvoiceReviewStatus] = ReviewStatusConfirmedInvoice,
            [DocInvoiceReviewedOn] = DateTime.UtcNow
        };

        await _dataverseService.UpdateDocumentFieldsAsync(
            documentId.ToString(),
            fields,
            ct);

        _logger.LogDebug(
            "Updated document {DocumentId} review status to ConfirmedInvoice",
            documentId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 2: Create Invoice Record
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a new sprk_invoice record linked to the document, matter, and vendor org.
    /// Sets initial statuses (ToReview, NotRun) and any corrected hints from the reviewer.
    /// </summary>
    private async Task<Guid> CreateInvoiceRecordAsync(
        InvoiceReviewConfirmRequest request,
        CancellationToken ct)
    {
        var fields = new Dictionary<string, object?>
        {
            // Lookup bindings (Web API format: _fieldname_value)
            [$"_{InvDocumentLookup}_value"] = request.DocumentId,
            [$"_{InvMatterLookup}_value"] = request.MatterId,
            [$"_{InvVendorOrgLookup}_value"] = request.VendorOrgId,

            // Status option sets
            [InvInvoiceStatus] = InvoiceStatusToReview,
            [InvExtractionStatus] = ExtractionStatusNotRun,

            // Timestamp
            [InvCreatedOn] = DateTime.UtcNow
        };

        // Add optional corrected hints from reviewer
        if (!string.IsNullOrWhiteSpace(request.InvoiceNumber))
        {
            fields[InvInvoiceNumber] = request.InvoiceNumber;
        }

        if (request.InvoiceDate.HasValue)
        {
            fields[InvInvoiceDate] = request.InvoiceDate.Value;
        }

        if (request.TotalAmount.HasValue)
        {
            fields[InvTotalAmount] = request.TotalAmount.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            fields[InvReviewerNotes] = request.Notes;
        }

        // Create via UpdateRecordFieldsAsync with a new GUID (upsert pattern)
        var invoiceId = Guid.NewGuid();
        await _dataverseService.UpdateRecordFieldsAsync(
            InvoiceEntity,
            invoiceId,
            fields,
            ct);

        _logger.LogInformation(
            "Created invoice record {InvoiceId} for document {DocumentId}, matter {MatterId}",
            invoiceId, request.DocumentId, request.MatterId);

        return invoiceId;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 3: Enqueue Extraction Job
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Submit an InvoiceExtraction job to the background processing queue.
    /// The handler will run Playbook B (full AI extraction) on the document.
    /// </summary>
    private async Task EnqueueExtractionJobAsync(
        Guid jobId,
        Guid invoiceId,
        Guid documentId,
        string correlationId,
        CancellationToken ct)
    {
        var payload = JsonSerializer.SerializeToDocument(new
        {
            invoiceId,
            documentId
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var job = new JobContract
        {
            JobId = jobId,
            JobType = JobTypeInvoiceExtraction,
            SubjectId = documentId.ToString(),
            CorrelationId = correlationId,
            IdempotencyKey = $"invoice-extraction-{invoiceId}",
            Payload = payload
        };

        await _jobSubmissionService.SubmitJobAsync(job, ct);

        _logger.LogInformation(
            "Enqueued {JobType} job {JobId} for invoice {InvoiceId}, document {DocumentId}",
            JobTypeInvoiceExtraction, jobId, invoiceId, documentId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Reject Invoice Review
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    public async Task<InvoiceReviewRejectResult> RejectInvoiceAsync(
        InvoiceReviewRejectRequest request,
        string correlationId,
        CancellationToken ct = default)
    {
        if (request.DocumentId == Guid.Empty)
        {
            throw new ArgumentException("DocumentId is required and must be a valid non-empty GUID.", nameof(request));
        }

        using var activity = _telemetry.StartActivity(
            "InvoiceReview.Reject",
            request.DocumentId.ToString(),
            correlationId);

        _logger.LogInformation(
            "Starting invoice review rejection. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
            request.DocumentId, correlationId);

        // Update document — mark as RejectedNotInvoice
        await UpdateDocumentRejectionStatusAsync(request.DocumentId, request.Notes, ct);

        _logger.LogInformation(
            "Invoice review rejection completed. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
            request.DocumentId, correlationId);

        return new InvoiceReviewRejectResult
        {
            DocumentId = request.DocumentId,
            Status = "Rejected"
        };
    }

    /// <summary>
    /// Mark the document as RejectedNotInvoice with the current review timestamp and optional notes.
    /// </summary>
    private async Task UpdateDocumentRejectionStatusAsync(Guid documentId, string? notes, CancellationToken ct)
    {
        var fields = new Dictionary<string, object?>
        {
            [DocInvoiceReviewStatus] = ReviewStatusRejectedNotInvoice,
            [DocInvoiceReviewedOn] = DateTime.UtcNow
            // TODO: Add DocInvoiceReviewedBy when user context is available
            // [DocInvoiceReviewedBy] = currentUserId
        };

        if (!string.IsNullOrWhiteSpace(notes))
        {
            fields[DocInvoiceRejectionNotes] = notes;
        }

        await _dataverseService.UpdateDocumentFieldsAsync(
            documentId.ToString(),
            fields,
            ct);

        _logger.LogDebug(
            "Updated document {DocumentId} review status to RejectedNotInvoice",
            documentId);
    }
}
