using System.Globalization;
using System.Text.Json;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Services.Dataverse;
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
/// Confirm workflow (rewritten by unified-access-control-r2 task 130 against the LIVE schema, owner decision
/// G5 2026-10-01 — the caller's rights are checked by the route's filter; this service writes app-only):
/// 1. Create sprk_invoice, OWNED BY THE MATTER'S TEAM (RecordOwnershipResolver, record-first), with its
///    matter + vendor lookups bound by navigation property
/// 2. Link the document to it: sprk_document.sprk_invoice (the live link runs document → invoice)
/// 3. LAST: mark the document ConfirmedInvoice (review timestamp, reviewer notes)
/// 4. Enqueue InvoiceExtraction job: triggers Playbook B (full AI extraction) asynchronously
/// No partial write: a failed link deletes the invoice it just created; a failed final step names the invoice
/// and a retry resumes from the document's existing link instead of creating a second invoice.
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

    /// <summary>Optional reviewer notes — written to the document's <c>sprk_invoicereviewnotes</c>.</summary>
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

/// <summary>Why a confirm was refused or did not complete. Each value's message says what was saved.</summary>
public enum InvoiceReviewFailure
{
    /// <summary>The document does not exist (deleted after the authorization check). Nothing was written.</summary>
    DocumentNotFound,

    /// <summary>The document is already linked to an invoice for a different matter or vendor. Nothing was written.</summary>
    DocumentLinkedToAnotherInvoice,

    /// <summary>No owning team resolved from the matter, so no invoice was created (never user- or app-owned).</summary>
    OwnerTeamUnresolved,

    /// <summary>The invoice was created but linking the document failed; the invoice was deleted again.</summary>
    LinkFailed,

    /// <summary>Linking failed AND removing the just-created invoice failed — the named invoice is orphaned.</summary>
    LinkFailedInvoiceNotRemoved,

    /// <summary>The invoice exists and is linked, but the document's review status was not set. Retry resumes.</summary>
    StatusNotUpdated,

    /// <summary>Everything was saved but the extraction job was not queued. Retry resumes and re-queues.</summary>
    ExtractionNotQueued,
}

/// <summary>
/// A confirm that was refused before any write, or that stopped part-way with its partial state undone or named.
/// </summary>
public sealed class InvoiceReviewException : Exception
{
    public InvoiceReviewException(InvoiceReviewFailure failure, string message, Guid? invoiceId = null, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        InvoiceId = invoiceId;
    }

    public InvoiceReviewFailure Failure { get; }

    /// <summary>The invoice that exists (or existed) when the failure happened; null when none was created.</summary>
    public Guid? InvoiceId { get; }

    /// <summary>Machine-readable reason (ADR-019), e.g. <c>sdap.finance.invoice_review.link_failed</c>.</summary>
    public string ReasonCode => Failure switch
    {
        InvoiceReviewFailure.DocumentNotFound => "sdap.finance.invoice_review.document_not_found",
        InvoiceReviewFailure.DocumentLinkedToAnotherInvoice => "sdap.finance.invoice_review.document_linked_elsewhere",
        InvoiceReviewFailure.OwnerTeamUnresolved => "sdap.finance.invoice_review.owner_unresolved",
        InvoiceReviewFailure.LinkFailed => "sdap.finance.invoice_review.link_failed",
        InvoiceReviewFailure.LinkFailedInvoiceNotRemoved => "sdap.finance.invoice_review.link_failed_invoice_orphaned",
        InvoiceReviewFailure.StatusNotUpdated => "sdap.finance.invoice_review.status_not_updated",
        _ => "sdap.finance.invoice_review.extraction_not_queued",
    };
}

/// <summary>
/// Implements invoice review confirmation workflow:
/// create the team-owned invoice, link the document to it, mark the document confirmed, enqueue extraction.
/// </summary>
public class InvoiceReviewService : IInvoiceReviewService
{
    private readonly IFieldMappingDataverseService _records;
    private readonly IGenericEntityService _entities;
    private readonly IRecordOwnershipResolver _ownership;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly FinanceTelemetry _telemetry;
    private readonly ILogger<InvoiceReviewService> _logger;

    // ═══════════════════════════════════════════════════════════════════════════
    // Dataverse Schema Constants — every name below read from LIVE metadata (spaarkedev1, read-only
    // EntityDefinitions / ManyToOneRelationships, 2026-10-01). Task 130 §6 recorded that the previous names
    // (sprk_invoice.sprk_document, sprk_reviewernotes, sprk_createdon, sprk_invoicerejectionnotes, and the
    // unwritable _x_value lookup keys) do not exist, so every confirm failed and every reject-with-notes 400'd.
    // ═══════════════════════════════════════════════════════════════════════════

    // Entities and their SET names (never pluralized — live EntitySetName).
    internal const string DocumentEntity = "sprk_document";
    internal const string InvoiceEntity = "sprk_invoice";
    internal const string OrganizationEntity = "sprk_organization";
    internal const string MatterEntity = "sprk_matter";
    private const string InvoiceEntitySet = "sprk_invoices";
    private const string MatterEntitySet = "sprk_matters";
    private const string OrganizationEntitySet = "sprk_organizations";
    private const string TeamEntitySet = "teams";

    // Document fields. The link column is sprk_document.sprk_invoice (relationship sprk_document_Invoice_n1,
    // navigation property sprk_Invoice); there is no lookup from the invoice to the document.
    internal const string DocInvoiceReviewStatus = "sprk_invoicereviewstatus";
    internal const string DocInvoiceReviewedOn = "sprk_invoicereviewedon";
    internal const string DocInvoiceReviewNotes = "sprk_invoicereviewnotes";
    internal const string DocInvoiceLinkBind = "sprk_Invoice@odata.bind";
    internal const string DocInvoiceLinkValue = "_sprk_invoice_value";

    // Invoice review status option set values (live: ToReview 100000000, ConfirmedInvoice 100000001,
    // RejectedNotInvoice 100000002).
    internal const int ReviewStatusConfirmedInvoice = 100000001;
    internal const int ReviewStatusRejectedNotInvoice = 100000002;

    // Invoice fields. Lookups are written by NAVIGATION PROPERTY (@odata.bind), never as _x_value keys, which
    // the Web API does not accept as a write.
    internal const string InvOwnerBind = "ownerid@odata.bind";
    internal const string InvMatterBind = "sprk_Matter@odata.bind";
    internal const string InvVendorOrgBind = "sprk_vendororg@odata.bind";
    internal const string InvMatterValue = "_sprk_matter_value";
    internal const string InvVendorOrgValue = "_sprk_vendororg_value";
    internal const string InvName = "sprk_name";
    internal const string InvInvoiceStatus = "sprk_invoicestatus";
    internal const string InvExtractionStatus = "sprk_extractionstatus";
    internal const string InvInvoiceNumber = "sprk_invoicenumber";
    internal const string InvInvoiceDate = "sprk_invoicedate";
    internal const string InvTotalAmount = "sprk_totalamount";

    private const string OrganizationName = "sprk_organizationname";

    /// <summary>sprk_invoice.sprk_name MaxLength (live: 850).</summary>
    internal const int InvoiceNameMaxLength = 850;

    // Invoice status option set values (live: ToReview 100000000, Reviewed 100000001).
    internal const int InvoiceStatusToReview = 100000000;

    // Extraction status option set values (live: NotRun 100000000, Extracted, Failed).
    internal const int ExtractionStatusNotRun = 100000000;

    // Job type for extraction
    private const string JobTypeInvoiceExtraction = "InvoiceExtraction";

    public InvoiceReviewService(
        IFieldMappingDataverseService records,
        IGenericEntityService entities,
        IRecordOwnershipResolver ownership,
        JobSubmissionService jobSubmissionService,
        FinanceTelemetry telemetry,
        ILogger<InvoiceReviewService> logger)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <exception cref="InvoiceReviewException">Refused before any write, or stopped with its partial state
    /// undone (link failure) or named (status / queue failure). See <see cref="InvoiceReviewFailure"/>.</exception>
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

        // Resume point: a retry after a failed final step finds the document already linked to the invoice the
        // first attempt created, and continues from there instead of creating a second one (idempotent retry).
        var invoiceId = await FindResumableInvoiceAsync(request, ct);

        if (invoiceId is null)
        {
            // Step 1: create the invoice, owned by the MATTER's team. Nothing has been written before this.
            var created = await CreateInvoiceRecordAsync(request, ct);

            // Step 2: link the document to it. On failure, undo step 1 so no orphan invoice is left behind.
            await LinkDocumentOrCompensateAsync(request.DocumentId, created, ct);
            invoiceId = created;
        }

        // Step 3 (LAST write): mark the document confirmed. The invoice exists and is linked; a failure here
        // leaves a state a retry completes, so the error names the invoice rather than undoing anything.
        try
        {
            await UpdateDocumentReviewStatusAsync(request.DocumentId, ReviewStatusConfirmedInvoice, request.Notes, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new InvoiceReviewException(
                InvoiceReviewFailure.StatusNotUpdated,
                $"Invoice {invoiceId} was created and linked to the document, but the document could not be marked " +
                "as a confirmed invoice. Retry the confirmation; it will reuse this invoice.",
                invoiceId, ex);
        }

        // Step 4: enqueue extraction. Its idempotency key is per invoice, so a retry cannot queue it twice.
        var jobId = Guid.NewGuid();
        try
        {
            await EnqueueExtractionJobAsync(jobId, invoiceId.Value, request.DocumentId, correlationId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new InvoiceReviewException(
                InvoiceReviewFailure.ExtractionNotQueued,
                $"Invoice {invoiceId} was confirmed, but its extraction could not be queued. Retry the confirmation; " +
                "it will reuse this invoice.",
                invoiceId, ex);
        }

        _logger.LogInformation(
            "Invoice review confirmation completed. DocumentId={DocumentId}, InvoiceId={InvoiceId}, " +
            "JobId={JobId}, CorrelationId={CorrelationId}",
            request.DocumentId, invoiceId, jobId, correlationId);

        return new InvoiceReviewResult
        {
            JobId = jobId,
            InvoiceId = invoiceId.Value,
            StatusUrl = $"/api/finance/jobs/{jobId}/status"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Resume (idempotent retry)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// The invoice an earlier attempt already linked this document to, when it is the SAME invoice this request
    /// would create (same matter and vendor) — or null when there is none. A link to a different matter's or
    /// vendor's invoice is refused rather than silently re-pointed or reused.
    /// </summary>
    private async Task<Guid?> FindResumableInvoiceAsync(InvoiceReviewConfirmRequest request, CancellationToken ct)
    {
        var document = await _records.RetrieveRecordFieldsAsync(
            DocumentEntity, request.DocumentId, new[] { DocInvoiceLinkValue }, ct);

        // RetrieveRecordFieldsAsync answers an EMPTY dictionary for a missing row (and a key per requested field,
        // null-valued, for a row whose column is empty).
        if (document.Count == 0)
        {
            throw new InvoiceReviewException(
                InvoiceReviewFailure.DocumentNotFound, "The document no longer exists. Nothing was saved.");
        }

        if (!TryReadGuid(document, DocInvoiceLinkValue, out var linkedInvoiceId))
        {
            return null;
        }

        var invoice = await _records.RetrieveRecordFieldsAsync(
            InvoiceEntity, linkedInvoiceId, new[] { InvMatterValue, InvVendorOrgValue }, ct);

        if (invoice.Count == 0)
        {
            // A dangling link to a deleted invoice — nothing to resume; the new link overwrites it.
            return null;
        }

        var sameMatter = TryReadGuid(invoice, InvMatterValue, out var matterId) && matterId == request.MatterId;
        var sameVendor = TryReadGuid(invoice, InvVendorOrgValue, out var vendorId) && vendorId == request.VendorOrgId;

        if (sameMatter && sameVendor)
        {
            _logger.LogInformation(
                "Document {DocumentId} is already linked to invoice {InvoiceId} for the same matter and vendor; " +
                "resuming the confirmation with it (no second invoice is created).",
                request.DocumentId, linkedInvoiceId);
            return linkedInvoiceId;
        }

        throw new InvoiceReviewException(
            InvoiceReviewFailure.DocumentLinkedToAnotherInvoice,
            $"The document is already linked to invoice {linkedInvoiceId}, which belongs to a different matter or " +
            "vendor. Nothing was saved.",
            linkedInvoiceId);
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 1: Create Invoice Record (app-only, team-owned)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Create a new sprk_invoice OWNED BY THE MATTER'S TEAM — the matter's business-unit default owner team, which
    /// for a secure matter is the Secure Record business unit's owner team (RecordOwnershipResolver, record-first).
    /// Never user-owned, never app-owned: an unresolved team refuses before anything is written (owner G5:
    /// "the same rule that assigned to the bu/team not individual").
    /// </summary>
    private async Task<Guid> CreateInvoiceRecordAsync(InvoiceReviewConfirmRequest request, CancellationToken ct)
    {
        var ownerTeamId = await _ownership.ResolveOwningTeamAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = MatterEntity,
                TargetRecordId = request.MatterId,
            },
            ct);

        if (ownerTeamId is null || ownerTeamId == Guid.Empty)
        {
            throw new InvoiceReviewException(
                InvoiceReviewFailure.OwnerTeamUnresolved,
                "The invoice could not be assigned to the matter's team, so it was not created and nothing was " +
                "saved. Ask an administrator to check that the matter's business unit has its default team.");
        }

        var vendorName = await ReadVendorNameAsync(request.VendorOrgId, ct);
        var fields = BuildInvoiceCreateFields(request, ownerTeamId.Value, vendorName);

        // A fresh GUID PATCHed without If-Match is a CREATE (Dataverse upserts) — the pattern this service has
        // always used, and the reason UpdateRecordFieldsAsync must keep its upsert semantics.
        var invoiceId = Guid.NewGuid();
        await _records.UpdateRecordFieldsAsync(InvoiceEntity, invoiceId, fields, ct);

        _logger.LogInformation(
            "Created invoice record {InvoiceId} for matter {MatterId}, owned by team {TeamId}",
            invoiceId, request.MatterId, ownerTeamId);

        return invoiceId;
    }

    /// <summary>
    /// The create payload. Lookups bound by navigation property (<c>@odata.bind</c>) against live set names;
    /// <c>sprk_name</c> (ApplicationRequired) derived from vendor + invoice number/date. Pure, so the exact wire
    /// shape is pinned by a test.
    /// </summary>
    internal static Dictionary<string, object?> BuildInvoiceCreateFields(
        InvoiceReviewConfirmRequest request, Guid ownerTeamId, string? vendorName)
    {
        var fields = new Dictionary<string, object?>
        {
            [InvOwnerBind] = $"/{TeamEntitySet}({ownerTeamId})",
            [InvMatterBind] = $"/{MatterEntitySet}({request.MatterId})",
            [InvVendorOrgBind] = $"/{OrganizationEntitySet}({request.VendorOrgId})",
            [InvName] = BuildInvoiceName(vendorName, request.InvoiceNumber, request.InvoiceDate),
            [InvInvoiceStatus] = InvoiceStatusToReview,
            [InvExtractionStatus] = ExtractionStatusNotRun,
        };

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

        return fields;
    }

    /// <summary>
    /// "{vendor} - {invoice number}", else "{vendor} - {invoice date yyyy-MM-dd}", else "{vendor} - Invoice";
    /// "Invoice" stands in for a vendor with no name. Capped at the column's live MaxLength.
    /// </summary>
    internal static string BuildInvoiceName(string? vendorName, string? invoiceNumber, DateTime? invoiceDate)
    {
        var vendor = string.IsNullOrWhiteSpace(vendorName) ? null : vendorName.Trim();
        var qualifier = !string.IsNullOrWhiteSpace(invoiceNumber)
            ? invoiceNumber.Trim()
            : invoiceDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        var name = (vendor, qualifier) switch
        {
            (not null, not null) => $"{vendor} - {qualifier}",
            (not null, null) => $"{vendor} - Invoice",
            (null, not null) => $"Invoice {qualifier}",
            _ => "Invoice",
        };

        return name.Length <= InvoiceNameMaxLength ? name : name[..InvoiceNameMaxLength];
    }

    private async Task<string?> ReadVendorNameAsync(Guid vendorOrgId, CancellationToken ct)
    {
        var vendor = await _records.RetrieveRecordFieldsAsync(OrganizationEntity, vendorOrgId, new[] { OrganizationName }, ct);
        return vendor.TryGetValue(OrganizationName, out var name) ? name?.ToString() : null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 2: Link the document (compensating)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// PATCHes the document's own lookup (<c>sprk_Invoice@odata.bind</c>) update-only. If that fails, the invoice
    /// created in step 1 is deleted again so no unlinked invoice is left behind; if THAT fails too, the error
    /// names the orphaned invoice.
    /// </summary>
    private async Task LinkDocumentOrCompensateAsync(Guid documentId, Guid invoiceId, CancellationToken ct)
    {
        try
        {
            await _records.UpdateExistingRecordFieldsAsync(
                DocumentEntity,
                documentId,
                new Dictionary<string, object?> { [DocInvoiceLinkBind] = $"/{InvoiceEntitySet}({invoiceId})" },
                ct);
        }
        catch (Exception linkFailure)
        {
            _logger.LogError(linkFailure,
                "Linking document {DocumentId} to invoice {InvoiceId} failed; deleting the invoice (compensation)",
                documentId, invoiceId);

            try
            {
                // CancellationToken.None: the undo must run even when the request that caused it was cancelled.
                await _entities.DeleteAsync(InvoiceEntity, invoiceId, CancellationToken.None);
            }
            catch (Exception compensationFailure)
            {
                _logger.LogError(compensationFailure,
                    "Compensation FAILED: invoice {InvoiceId} was created but is not linked to document {DocumentId} " +
                    "and could not be deleted. It must be removed manually.", invoiceId, documentId);

                throw new InvoiceReviewException(
                    InvoiceReviewFailure.LinkFailedInvoiceNotRemoved,
                    $"The document could not be linked to the new invoice, and invoice {invoiceId} could not be " +
                    "removed again. Report this invoice id to an administrator; the document was not changed.",
                    invoiceId, linkFailure);
            }

            throw new InvoiceReviewException(
                InvoiceReviewFailure.LinkFailed,
                "The document could not be linked to the new invoice, so the invoice was removed again. Nothing " +
                "was saved; retry the confirmation.",
                inner: linkFailure);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 3: Update Document Review Status
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Set the document's review status (with timestamp and optional reviewer notes) — update-only, so a document
    /// deleted after the check is never recreated. Shared by confirm and reject.
    /// </summary>
    private async Task UpdateDocumentReviewStatusAsync(Guid documentId, int status, string? notes, CancellationToken ct)
    {
        await _records.UpdateExistingRecordFieldsAsync(
            DocumentEntity, documentId, BuildReviewStatusFields(status, notes, DateTime.UtcNow), ct);

        _logger.LogDebug("Updated document {DocumentId} review status to {Status}", documentId, status);
    }

    /// <summary>The review-status PATCH body. Notes go to the LIVE column <c>sprk_invoicereviewnotes</c>.</summary>
    internal static Dictionary<string, object?> BuildReviewStatusFields(int status, string? notes, DateTime reviewedOnUtc)
    {
        var fields = new Dictionary<string, object?>
        {
            [DocInvoiceReviewStatus] = status,
            [DocInvoiceReviewedOn] = reviewedOnUtc
            // TRACKED: GitHub #233 - Add sprk_invoicereviewedby when user context available
        };

        if (!string.IsNullOrWhiteSpace(notes))
        {
            fields[DocInvoiceReviewNotes] = notes;
        }

        return fields;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 4: Enqueue Extraction Job
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
    /// <exception cref="KeyNotFoundException">The document no longer exists; nothing was written.</exception>
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

        // Mark RejectedNotInvoice; notes go to the live sprk_invoicereviewnotes column.
        await UpdateDocumentReviewStatusAsync(request.DocumentId, ReviewStatusRejectedNotInvoice, request.Notes, ct);

        _logger.LogInformation(
            "Invoice review rejection completed. DocumentId={DocumentId}, CorrelationId={CorrelationId}",
            request.DocumentId, correlationId);

        return new InvoiceReviewRejectResult
        {
            DocumentId = request.DocumentId,
            Status = "Rejected"
        };
    }

    private static bool TryReadGuid(Dictionary<string, object?> row, string column, out Guid value)
    {
        value = Guid.Empty;
        return row.TryGetValue(column, out var raw)
            && raw is not null
            && Guid.TryParse(raw.ToString(), out value)
            && value != Guid.Empty;
    }
}
