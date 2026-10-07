using System.Data;
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
/// and a retry resumes from the document's existing link instead of creating a second invoice. The link is
/// conditional on the document version read by the resume check (If-Match), so two concurrent confirms cannot
/// both link: the loser deletes its own invoice and answers with the winner's WITHOUT queuing a second
/// extraction, or is refused (409). An unrelated write between the read and the link is retried (bounded).
/// Every review-status write — confirm's and reject's — is conditional on the version it read, and reject refuses
/// a document that is linked to an invoice, so neither decision silently overwrites the other (task 130 round 3).
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
    /// <returns>Result with the invoice ID and the enqueued job ID — or, when a concurrent confirmation linked the
    /// document first, that confirmation's invoice with no job ID and <see cref="InvoiceReviewResult.ExtractionAlreadyQueued"/>.</returns>
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
    /// <exception cref="KeyNotFoundException">The document no longer exists; nothing was written.</exception>
    /// <exception cref="InvoiceReviewException">The document is linked to an invoice
    /// (<see cref="InvoiceReviewFailure.DocumentLinkedToInvoice"/>), or kept changing
    /// (<see cref="InvoiceReviewFailure.DocumentChangedConcurrently"/>); nothing was written.</exception>
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

    /// <summary>
    /// unified-access-control-r2 task 146 c1-r1 (owner round 13 item 9): the reviewer confirming the invoice — set by the
    /// endpoint from the signed-in caller, recorded as the app-created invoice's creator person. 🔒 Never bound from the
    /// request body: a client never chooses who created a record.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public RecordRequester? RequestedBy { get; init; }
}

/// <summary>
/// Result of confirming an invoice review.
/// </summary>
public record InvoiceReviewResult
{
    /// <summary>
    /// The extraction job THIS request enqueued; null when it enqueued none because
    /// <see cref="ExtractionAlreadyQueued"/> (never a fabricated id).
    /// </summary>
    public Guid? JobId { get; init; }

    /// <summary>The invoice the document is linked to (created by this request, or by the one that linked first).</summary>
    public Guid InvoiceId { get; init; }

    /// <summary>URL for polling the job this request enqueued; null when it enqueued none.</summary>
    public string? StatusUrl { get; init; }

    /// <summary>
    /// True when a concurrent confirmation linked this document to an invoice for the same matter and vendor
    /// first: this request deleted the invoice it had created, answers with that one, and queued NO extraction —
    /// the confirmation that linked the document queues it (or reports, naming the invoice, that it could not).
    /// The <c>sdap-jobs</c> queue has no duplicate detection, so a second submission would run the job twice.
    /// </summary>
    public bool ExtractionAlreadyQueued { get; init; }
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

    /// <summary>
    /// Linking failed AND the just-created invoice is still there after the attempt to remove it (read back), or
    /// its removal could not be confirmed — the named invoice is orphaned. A delete whose response was lost but
    /// which did remove the row is NOT reported as this; the clean failure is reported instead.
    /// </summary>
    LinkFailedInvoiceNotRemoved,

    /// <summary>The invoice exists and is linked, but the document's review status was not set. Retry resumes.</summary>
    StatusNotUpdated,

    /// <summary>Everything was saved but the extraction job was not queued. Retry resumes and re-queues.</summary>
    ExtractionNotQueued,

    /// <summary>
    /// The document kept changing: every one of the bounded conditional attempts (link for confirm, status for
    /// reject) found a newer version, while the document stayed unlinked and its review decision unchanged. A
    /// confirm deleted the invoice it created. Nothing was saved; retry.
    /// </summary>
    DocumentChangedConcurrently,

    /// <summary>
    /// Confirm only: the document's review decision changed (e.g. a concurrent reject) between this confirm's read
    /// and its link. The invoice this request created was deleted again; the other decision stands. Nothing was saved.
    /// </summary>
    ReviewDecisionChanged,

    /// <summary>
    /// Reject only: the document is linked to an invoice (it was confirmed), so it cannot be rejected as not an
    /// invoice. Nothing was written; the invoice is never named (the caller was not authorized against it).
    /// </summary>
    DocumentLinkedToInvoice,

    /// <summary>
    /// The invoice create did not report success, and the named invoice id could not be confirmed absent or
    /// removed — it may exist unlinked. The document was not changed.
    /// </summary>
    CreateFailedInvoiceMayRemain,
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

    /// <summary>The document no longer exists — shared by confirm and by reject's 404 (ADR-019).</summary>
    public const string DocumentNotFoundReasonCode = "sdap.finance.invoice_review.document_not_found";

    /// <summary>Machine-readable reason (ADR-019), e.g. <c>sdap.finance.invoice_review.link_failed</c>.</summary>
    public string ReasonCode => Failure switch
    {
        InvoiceReviewFailure.DocumentNotFound => DocumentNotFoundReasonCode,
        InvoiceReviewFailure.DocumentLinkedToAnotherInvoice => "sdap.finance.invoice_review.document_linked_elsewhere",
        InvoiceReviewFailure.OwnerTeamUnresolved => "sdap.finance.invoice_review.owner_unresolved",
        InvoiceReviewFailure.LinkFailed => "sdap.finance.invoice_review.link_failed",
        InvoiceReviewFailure.LinkFailedInvoiceNotRemoved => "sdap.finance.invoice_review.link_failed_invoice_orphaned",
        InvoiceReviewFailure.StatusNotUpdated => "sdap.finance.invoice_review.status_not_updated",
        InvoiceReviewFailure.DocumentChangedConcurrently => "sdap.finance.invoice_review.document_changed",
        InvoiceReviewFailure.CreateFailedInvoiceMayRemain => "sdap.finance.invoice_review.create_failed_invoice_may_remain",
        InvoiceReviewFailure.ReviewDecisionChanged => "sdap.finance.invoice_review.review_decision_changed",
        InvoiceReviewFailure.DocumentLinkedToInvoice => "sdap.finance.invoice_review.document_linked_to_invoice",
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
    private readonly ICommunicationDataverseService _recordTypes;
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
    private const string RecordTypeRefEntitySet = "sprk_recordtype_refs";

    /// <summary>Every Dataverse row's version; its ETag is <c>W/"versionnumber"</c> (verified live 2026-10-01).</summary>
    internal const string VersionNumber = "versionnumber";

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

    // sprk_invoice.sprk_regardingrecordtype (ApplicationRequired; lookup → sprk_recordtype_ref, navigation property
    // sprk_regardingrecordtype — live metadata 2026-10-01). Existing typed invoices point it at the MATTER's
    // record-type row (sprk_recordlogicalname = 'sprk_matter'); its id differs per environment, so it is resolved.
    internal const string InvRegardingRecordTypeBind = "sprk_regardingrecordtype@odata.bind";

    // The other ADR-024 resolver fields, populated with the type exactly as TodoRegardingBuilder /
    // IncomingAssociationResolver populate them (id = lowercase "D" GUID, name = the matter's sprk_mattername,
    // number = its sprk_matternumber, url = the relative model-driven record URL). Live lengths (2026-10-01):
    // id 100, name 100, number 1000, url 250.
    internal const string InvRegardingRecordId = "sprk_regardingrecordid";
    internal const string InvRegardingRecordName = "sprk_regardingrecordname";
    internal const string InvRegardingRecordNumber = "sprk_regardingrecordnumber";
    internal const string InvRegardingRecordUrl = "sprk_regardingrecordurl";
    internal const int RegardingRecordNameMaxLength = 100;
    internal const int RegardingRecordNumberMaxLength = 1000;
    internal const string MatterName = "sprk_mattername";
    internal const string MatterNumber = "sprk_matternumber";

    /// <summary>
    /// Attempts, in total, of each conditional document write (confirm's link, confirm's status, reject's status)
    /// when the document keeps changing under it without the change being a decision about it.
    /// </summary>
    internal const int MaxConditionalWriteAttempts = 3;
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
        ICommunicationDataverseService recordTypes,
        JobSubmissionService jobSubmissionService,
        FinanceTelemetry telemetry,
        ILogger<InvoiceReviewService> logger)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _entities = entities ?? throw new ArgumentNullException(nameof(entities));
        _ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
        _recordTypes = recordTypes ?? throw new ArgumentNullException(nameof(recordTypes));
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
        // It also reads the document's VERSION, which the link below is conditional on, and its review decision.
        var first = await ReadDocumentStateAsync(request, ct);
        if (!first.Exists)
        {
            throw DocumentGone();
        }

        if (first.Link == LinkState.Elsewhere)
        {
            throw LinkedElsewhere();
        }

        Guid invoiceId;
        long? statusVersion;
        var reconcileOwnerAfterStatus = false;

        if (first.Link == LinkState.SameMatterAndVendor)
        {
            _logger.LogInformation(
                "Document {DocumentId} is already linked to invoice {InvoiceId} for the same matter and vendor; " +
                "resuming the confirmation with it (no second invoice is created).",
                request.DocumentId, first.LinkedInvoiceId);
            invoiceId = first.LinkedInvoiceId!.Value;
            statusVersion = first.Version; // nothing written since the read: the status write is conditional on it
            reconcileOwnerAfterStatus = true; // task 146 — see after step 3
        }
        else
        {
            // Task 146: linking FILES the document under the invoice — a reparent. Its owner is re-derived over every
            // parent it will have (secure-if-any; the invoice's securability is its matter's, so the matter stands in
            // for the not-yet-created invoice) BEFORE anything is written. A refusal writes nothing at all — no
            // invoice, no link. Steps 1-2 run inside the reparent, which then reassigns the document (read back).
            Guid created = Guid.Empty;
            LinkOutcome? linkOutcome = null;
            try
            {
                await RefileDocumentUnderInvoiceAsync(
                    request,
                    async token =>
                    {
                        // Step 1: create the invoice, owned by the MATTER's team.
                        created = await CreateInvoiceRecordAsync(request, token);

                        // Step 2: link the document to it — only if the document is still at the version read above. On
                        // failure, undo step 1 so no orphan invoice is left behind. A concurrent confirm that linked
                        // first makes this answer with ITS invoice (same matter and vendor) or refuse (409).
                        linkOutcome = await LinkDocumentOrCompensateAsync(request, created, first, token);
                    },
                    ct);
            }
            catch (Exception refileFailure) when (refileFailure is not InvoiceReviewException
                                                  && created != Guid.Empty
                                                  && linkOutcome is { ConcurrentInvoiceId: null })
            {
                // Task 146 b2: the invoice was created and the document linked to it, but the document's owner could not
                // then be moved under the invoice (the owner assignment the reparent makes after the change failed). The
                // link is THIS flow's change, so this flow undoes it: deleting the invoice it created also removes the
                // link (sprk_document_Invoice_n1 cascades Delete as RemoveLink, live metadata 2026-10-01). The document
                // is left as it was — never filed under a (possibly secure) invoice while owned elsewhere — and no
                // orphan invoice remains. A lost-race link (another confirmation's invoice) is that confirmation's.
                _logger.LogError(refileFailure,
                    "Document {DocumentId} was linked to new invoice {InvoiceId}, but its owner could not be moved under the "
                    + "invoice; deleting the invoice (which removes the link). CorrelationId={CorrelationId}",
                    request.DocumentId, created, correlationId);
                await DeleteCreatedInvoiceOrThrowAsync(request.DocumentId, created, refileFailure);
                throw new InvoiceReviewException(
                    InvoiceReviewFailure.LinkFailed,
                    "The document could not be filed under the new invoice because its owner could not be moved with it, "
                    + "so the invoice was removed again. Nothing was saved; retry the confirmation.",
                    inner: refileFailure);
            }
            var link = linkOutcome!.Value; // set by the change, which ran (a refusal threw above)

            if (link.ConcurrentInvoiceId is { } winner)
            {
                // Lost the race to a confirmation for the same matter and vendor. That confirmation owns the review
                // status and the extraction job: queuing here too would run the job twice (sdap-jobs has no
                // duplicate detection, so a same-MessageId submission is NOT suppressed). No job id is invented.
                _logger.LogInformation(
                    "Confirmation of document {DocumentId} lost the race to a concurrent confirmation that linked " +
                    "invoice {InvoiceId}; answering with it, without queuing a second extraction. CorrelationId={CorrelationId}",
                    request.DocumentId, winner, correlationId);

                return new InvoiceReviewResult
                {
                    InvoiceId = winner,
                    JobId = null,
                    StatusUrl = null,
                    ExtractionAlreadyQueued = true,
                };
            }

            invoiceId = created;
            statusVersion = null; // the link changed the version; the status write re-reads it
        }

        // Step 3 (LAST write): mark the document confirmed — conditional on the version it read, so it never
        // overwrites a write it did not see. The invoice exists and is linked; a failure here leaves a state a
        // retry completes, so the error names the invoice rather than undoing anything.
        try
        {
            await MarkDocumentConfirmedAsync(request, invoiceId, statusVersion, ct);
        }
        catch (InvoiceReviewException)
        {
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            throw new InvoiceReviewException(
                InvoiceReviewFailure.StatusNotUpdated,
                $"Invoice {invoiceId} was created and linked to the document, but the document could not be marked " +
                "as a confirmed invoice. Retry the confirmation; it will reuse this invoice.",
                invoiceId, ex);
        }

        // Task 146 — a RESUMED confirmation: the first attempt linked the document but may have failed before the
        // document's owner moved under the invoice. Completed here, after the status write so that write keeps its
        // version condition (an already-owned document is left as it is — the common case is a no-op).
        if (reconcileOwnerAfterStatus)
        {
            await RefileDocumentUnderInvoiceAsync(request, applyChange: _ => Task.CompletedTask, ct);
        }

        // Step 4: enqueue extraction. Its idempotency key is per invoice and becomes the Service Bus MessageId, but
        // sdap-jobs has duplicate detection OFF (verified live 2026-10-01; immutable on an existing queue), so the
        // key does NOT suppress a second submission — which is why the race loser above never submits.
        var jobId = Guid.NewGuid();
        try
        {
            await EnqueueExtractionJobAsync(jobId, invoiceId, request.DocumentId, correlationId, ct);
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
            InvoiceId = invoiceId,
            StatusUrl = $"/api/finance/jobs/{jobId}/status"
        };
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Resume (idempotent retry)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Where the document's invoice link points, relative to this request's matter and vendor.</summary>
    private enum LinkState
    {
        /// <summary>No link, or a dangling link to a deleted invoice (nothing to resume; a new link replaces it).</summary>
        None,

        /// <summary>Linked to an invoice for the SAME matter and vendor — the invoice this request would create.</summary>
        SameMatterAndVendor,

        /// <summary>Linked to an invoice of another matter or vendor, which the caller was never authorized against.</summary>
        Elsewhere,
    }

    /// <summary>
    /// What one read of the document found: existence, version, review decision and where its link points.
    /// <see cref="Decision"/> is the decision's full identity — status, reviewed-on stamp and notes — because every
    /// confirm and reject writes all three: a SECOND reject of an already-rejected document leaves the status
    /// unchanged but restamps <c>sprk_invoicereviewedon</c>, and only the stamp tells it apart from an unrelated write.
    /// </summary>
    private readonly record struct DocumentState(
        bool Exists, long Version, int? ReviewStatus, LinkState Link, Guid? LinkedInvoiceId, ReviewDecision Decision);

    /// <summary>A review decision as written by <see cref="BuildReviewStatusFields"/>; equal only if no decision was written in between.</summary>
    private readonly record struct ReviewDecision(int? Status, DateTime? ReviewedOnUtc, string? Notes);

    /// <summary>
    /// Reads the document's link, <c>versionnumber</c> (which every conditional write is made on) and review
    /// decision, and classifies the link against the request's matter and vendor. A missing document or a foreign
    /// link is REPORTED, not thrown — after a create the caller must remove its own invoice first. A link to another
    /// matter's invoice is logged with its id, which is NEVER returned (task 130 item 5).
    /// </summary>
    private async Task<DocumentState> ReadDocumentStateAsync(InvoiceReviewConfirmRequest request, CancellationToken ct)
    {
        var document = await _records.RetrieveRecordFieldsAsync(
            DocumentEntity, request.DocumentId,
            new[] { DocInvoiceLinkValue, VersionNumber, DocInvoiceReviewStatus, DocInvoiceReviewedOn, DocInvoiceReviewNotes }, ct);

        // RetrieveRecordFieldsAsync answers an EMPTY dictionary for a missing row (and a key per requested field,
        // null-valued, for a row whose column is empty).
        if (document.Count == 0)
        {
            return new DocumentState(false, 0, null, LinkState.None, null, default);
        }

        var version = ReadVersion(document, "the confirmation cannot link it safely");
        var reviewStatus = TryReadInt(document, DocInvoiceReviewStatus);
        var decision = new ReviewDecision(
            reviewStatus,
            TryReadUtc(document, DocInvoiceReviewedOn),
            document.TryGetValue(DocInvoiceReviewNotes, out var notes) ? notes?.ToString() : null);

        if (!TryReadGuid(document, DocInvoiceLinkValue, out var linkedInvoiceId))
        {
            return new DocumentState(true, version, reviewStatus, LinkState.None, null, decision);
        }

        var invoice = await _records.RetrieveRecordFieldsAsync(
            InvoiceEntity, linkedInvoiceId, new[] { InvMatterValue, InvVendorOrgValue }, ct);

        if (invoice.Count == 0)
        {
            // A dangling link to a deleted invoice — nothing to resume; the new link overwrites it.
            return new DocumentState(true, version, reviewStatus, LinkState.None, null, decision);
        }

        var sameMatter = TryReadGuid(invoice, InvMatterValue, out var matterId) && matterId == request.MatterId;
        var sameVendor = TryReadGuid(invoice, InvVendorOrgValue, out var vendorId) && vendorId == request.VendorOrgId;

        if (sameMatter && sameVendor)
        {
            return new DocumentState(true, version, reviewStatus, LinkState.SameMatterAndVendor, linkedInvoiceId, decision);
        }

        // The other invoice belongs to a matter or vendor the request did not name — and the caller was never
        // authorized against it. Its id is logged for support and NEVER returned (task 130 item 5).
        _logger.LogWarning(
            "Document {DocumentId} is linked to invoice {LinkedInvoiceId} of a different matter or vendor; the " +
            "confirmation is refused (409). The linked invoice id is not returned to the caller.",
            request.DocumentId, linkedInvoiceId);

        return new DocumentState(true, version, reviewStatus, LinkState.Elsewhere, null, decision);
    }

    /// <summary>
    /// Every Dataverse row has a version. Without one a write cannot be made conditional, so refuse before writing
    /// anything rather than fall back to an unconditional write that can overwrite a change it never saw.
    /// </summary>
    private static long ReadVersion(Dictionary<string, object?> document, string consequence)
    {
        if (!document.TryGetValue(VersionNumber, out var rawVersion)
            || rawVersion is null
            || !long.TryParse(rawVersion.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var version))
        {
            throw new InvalidOperationException(
                $"The document's {VersionNumber} could not be read, so {consequence}. Nothing was saved.");
        }

        return version;
    }

    private static InvoiceReviewException DocumentGone() =>
        new(InvoiceReviewFailure.DocumentNotFound, "The document no longer exists. Nothing was saved.");

    private static InvoiceReviewException LinkedElsewhere() =>
        new(InvoiceReviewFailure.DocumentLinkedToAnotherInvoice,
            "The document is already linked to an invoice for a different matter or vendor. Nothing was saved.");

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 1: Create Invoice Record (app-only, team-owned)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Task 146: files the document under the invoice as a REPARENT through the one owner resolver. The document's
    /// owner is re-derived over every parent it carries plus the confirmed matter (the invoice's own securability is
    /// its matter's — the invoice is owned by the matter's team) BEFORE <paramref name="applyChange"/> runs; a refusal
    /// throws <see cref="InvoiceReviewFailure.OwnerTeamUnresolved"/> with nothing written. After the change the
    /// document is reassigned (a separate owner write, read back) when its owner moves — into the named Secure team
    /// for a secure matter. A Dataverse fault propagates.
    /// </summary>
    private async Task RefileDocumentUnderInvoiceAsync(
        InvoiceReviewConfirmRequest request, Func<CancellationToken, Task> applyChange, CancellationToken ct)
    {
        var reparent = await _ownership.ReparentAsync(
            new RecordReparent
            {
                EntityLogicalName = DocumentEntity,
                RecordId = request.DocumentId,
                ParentChanges = new Dictionary<string, Microsoft.Xrm.Sdk.EntityReference?>(),
                InheritedParents = new[] { new RecordOwnershipParent(MatterEntity, request.MatterId) },
            },
            applyChange,
            ct);

        if (reparent.IsRefused)
        {
            throw new InvoiceReviewException(
                InvoiceReviewFailure.OwnerTeamUnresolved,
                $"The document could not be filed under the invoice because its owner could not be resolved "
                + $"({reparent.RefusalCode}: {reparent.Reason}). Nothing was saved.");
        }
    }

    /// <summary>
    /// Create a new sprk_invoice OWNED BY THE MATTER'S TEAM — the matter's business-unit default owner team, which
    /// for a secure matter is the Secure Record business unit's owner team (RecordOwnershipResolver, record-first).
    /// Never user-owned, never app-owned: an unresolved team refuses before anything is written (owner G5:
    /// "the same rule that assigned to the bu/team not individual").
    /// </summary>
    private async Task<Guid> CreateInvoiceRecordAsync(InvoiceReviewConfirmRequest request, CancellationToken ct)
    {
        var invoiceOwner = await _ownership.ResolveOwnerAsync(
            new RecordOwnershipContext
            {
                TargetEntityLogicalName = MatterEntity,
                TargetRecordId = request.MatterId,
                RequestedBy = request.RequestedBy, // task 146 c1-r1 — the reviewer, recorded on the app-created invoice
            },
            ct);
        var ownerTeamId = invoiceOwner.IsOwned ? invoiceOwner.OwningTeamId : null;

        if (ownerTeamId is null || ownerTeamId == Guid.Empty)
        {
            throw new InvoiceReviewException(
                InvoiceReviewFailure.OwnerTeamUnresolved,
                "The invoice could not be assigned to the matter's team, so it was not created and nothing was " +
                "saved. Ask an administrator to check that the matter's business unit has its default team.");
        }

        var vendorName = await ReadVendorNameAsync(request.VendorOrgId, ct);
        var matterRecordTypeId = await ResolveMatterRecordTypeIdAsync(ct);
        var (matterName, matterNumber) = await ReadMatterNameAndNumberAsync(request.MatterId, ct);
        var fields = BuildInvoiceCreateFields(
            request, ownerTeamId.Value, vendorName, matterRecordTypeId, matterName, matterNumber);
        invoiceOwner.StampCreatorOn(fields, InvoiceEntity);

        // A fresh GUID PATCHed without If-Match is a CREATE (Dataverse upserts) — the pattern this service has
        // always used, and the reason UpdateRecordFieldsAsync must keep its upsert semantics.
        var invoiceId = Guid.NewGuid();
        try
        {
            await _records.UpdateRecordFieldsAsync(InvoiceEntity, invoiceId, fields, ct);
        }
        catch (Exception createFailure)
        {
            // The create did not REPORT success — but a lost response (timeout, dropped connection, cancellation
            // after send) can leave the row created. The id is ours, so look it up and remove it if it is there.
            await RemoveInvoiceIfCreatedAsync(invoiceId, request.DocumentId, createFailure);
            throw;
        }

        _logger.LogInformation(
            "Created invoice record {InvoiceId} for matter {MatterId}, owned by team {TeamId}",
            invoiceId, request.MatterId, ownerTeamId);

        return invoiceId;
    }

    /// <summary>
    /// After a create that did not report success: deletes the invoice if it exists after all, and returns so the
    /// original failure is rethrown ("nothing was saved"). If its existence cannot be ruled out, or it exists and
    /// cannot be removed, throws <see cref="InvoiceReviewFailure.CreateFailedInvoiceMayRemain"/> naming it.
    /// </summary>
    private async Task RemoveInvoiceIfCreatedAsync(Guid invoiceId, Guid documentId, Exception createFailure)
    {
        try
        {
            // CancellationToken.None: the clean-up must run even when the request that caused it was cancelled.
            var row = await _records.RetrieveRecordFieldsAsync(
                InvoiceEntity, invoiceId, new[] { InvMatterValue }, CancellationToken.None);
            if (row.Count == 0)
            {
                return; // never created — the original failure stands, nothing was saved
            }

            _logger.LogWarning(
                "Invoice create for document {DocumentId} reported a failure but invoice {InvoiceId} exists (lost " +
                "response); deleting it", documentId, invoiceId);
            if (await DeleteInvoiceAsync(invoiceId, documentId) == InvoiceRemoval.Removed)
            {
                return;
            }

            throw new InvalidOperationException($"Invoice {invoiceId} could not be confirmed removed.");
        }
        catch (Exception cleanupFailure)
        {
            _logger.LogError(cleanupFailure,
                "Invoice create for document {DocumentId} failed and invoice {InvoiceId} could not be confirmed " +
                "absent or removed. It may exist unlinked and must be checked manually.", documentId, invoiceId);

            throw new InvoiceReviewException(
                InvoiceReviewFailure.CreateFailedInvoiceMayRemain,
                $"The invoice could not be created, and invoice {invoiceId} may exist unlinked — it could not be " +
                "confirmed absent or removed. Report this invoice id to an administrator; the document was not changed.",
                invoiceId, createFailure);
        }
    }

    /// <summary>
    /// The id of the Matter row in <c>sprk_recordtype_ref</c>, for <c>sprk_invoice.sprk_regardingrecordtype</c> —
    /// or null when it cannot be resolved. Non-fatal (the <c>TodoRegardingBuilder</c> precedent): the column is
    /// ApplicationRequired (form-level), not enforced by the platform, and live invoices exist without it.
    /// </summary>
    private async Task<Guid?> ResolveMatterRecordTypeIdAsync(CancellationToken ct)
    {
        try
        {
            var row = await _recordTypes.QueryRecordTypeRefAsync(MatterEntity, ct);
            if (row is not null && row.Id != Guid.Empty)
            {
                return row.Id;
            }

            _logger.LogWarning(
                "No active sprk_recordtype_ref row for {Entity}; the invoice's sprk_regardingrecordtype is left unset.",
                MatterEntity);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Resolving the sprk_recordtype_ref row for {Entity} failed; the invoice's sprk_regardingrecordtype is " +
                "left unset.", MatterEntity);
        }

        return null;
    }

    /// <summary>
    /// The matter's display name and number, for the invoice's <c>sprk_regardingrecordname</c> /
    /// <c>sprk_regardingrecordnumber</c> (the columns <c>IncomingAssociationResolver</c> reads for a matter:
    /// <c>RegardingNameFields.PrimaryNameField</c> and its reference-number map). Read before the create, so a
    /// failure here saves nothing.
    /// </summary>
    private async Task<(string? Name, string? Number)> ReadMatterNameAndNumberAsync(Guid matterId, CancellationToken ct)
    {
        var matter = await _records.RetrieveRecordFieldsAsync(MatterEntity, matterId, new[] { MatterName, MatterNumber }, ct);
        return (
            matter.TryGetValue(MatterName, out var name) ? name?.ToString() : null,
            matter.TryGetValue(MatterNumber, out var number) ? number?.ToString() : null);
    }

    /// <summary>
    /// The create payload. Lookups bound by navigation property (<c>@odata.bind</c>) against live set names;
    /// <c>sprk_name</c> (ApplicationRequired) derived from vendor + invoice number/date. The ADR-024 resolver fields
    /// describe the MATTER the invoice regards, populated together as <c>TodoRegardingBuilder</c> does: id
    /// (lowercase "D"), name (empty when unknown), relative record URL, and — when resolved — the record type
    /// and the matter number (<c>IncomingAssociationResolver</c>). Pure, so the exact wire shape is pinned by a test.
    /// </summary>
    internal static Dictionary<string, object?> BuildInvoiceCreateFields(
        InvoiceReviewConfirmRequest request,
        Guid ownerTeamId,
        string? vendorName,
        Guid? matterRecordTypeId = null,
        string? matterName = null,
        string? matterNumber = null)
    {
        var matterId = request.MatterId.ToString("D").ToLowerInvariant();
        var fields = new Dictionary<string, object?>
        {
            [InvOwnerBind] = $"/{TeamEntitySet}({ownerTeamId})",
            [InvMatterBind] = $"/{MatterEntitySet}({request.MatterId})",
            [InvVendorOrgBind] = $"/{OrganizationEntitySet}({request.VendorOrgId})",
            [InvName] = BuildInvoiceName(vendorName, request.InvoiceNumber, request.InvoiceDate),
            [InvInvoiceStatus] = InvoiceStatusToReview,
            [InvExtractionStatus] = ExtractionStatusNotRun,
            [InvRegardingRecordId] = matterId,
            [InvRegardingRecordName] = Cap(matterName?.Trim() ?? string.Empty, RegardingRecordNameMaxLength),
            [InvRegardingRecordUrl] = $"/main.aspx?pagetype=entityrecord&etn={MatterEntity}&id={matterId}",
        };

        if (!string.IsNullOrWhiteSpace(matterNumber))
        {
            fields[InvRegardingRecordNumber] = Cap(matterNumber.Trim(), RegardingRecordNumberMaxLength);
        }

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

        if (matterRecordTypeId is { } recordTypeId && recordTypeId != Guid.Empty)
        {
            fields[InvRegardingRecordTypeBind] = $"/{RecordTypeRefEntitySet}({recordTypeId})";
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

        return Cap(name, InvoiceNameMaxLength);
    }

    private static string Cap(string value, int maxLength) => value.Length <= maxLength ? value : value[..maxLength];

    private async Task<string?> ReadVendorNameAsync(Guid vendorOrgId, CancellationToken ct)
    {
        var vendor = await _records.RetrieveRecordFieldsAsync(OrganizationEntity, vendorOrgId, new[] { OrganizationName }, ct);
        return vendor.TryGetValue(OrganizationName, out var name) ? name?.ToString() : null;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 2: Link the document (compensating)
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// How step 2 ended: the document is linked to this request's invoice (<see cref="ConcurrentInvoiceId"/> null),
    /// or a concurrent confirmation for the same matter and vendor linked it first — this request's invoice was
    /// deleted and the confirmation answers with <see cref="ConcurrentInvoiceId"/>, queuing nothing.
    /// </summary>
    private readonly record struct LinkOutcome(Guid? ConcurrentInvoiceId);

    /// <summary>
    /// PATCHes the document's own lookup (<c>sprk_Invoice@odata.bind</c>) ONLY IF the document is still at the
    /// version last read (<c>If-Match: W/"version"</c>).
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    ///   <item><b>Linked</b> → this request continues with <paramref name="invoiceId"/>.</item>
    ///   <item><b>The document changed</b> (412): it is read again, and
    ///   <list type="bullet">
    ///     <item>linked to an invoice for the same matter and vendor (a concurrent confirm won) → this request's
    ///     invoice is deleted and the winner's is answered with — still exactly one invoice, one extraction job;</item>
    ///     <item>linked to another matter's or vendor's invoice → invoice deleted, 409
    ///     <see cref="InvoiceReviewFailure.DocumentLinkedToAnotherInvoice"/>;</item>
    ///     <item>gone → invoice deleted, <see cref="InvoiceReviewFailure.DocumentNotFound"/>;</item>
    ///     <item>still unlinked but a review decision was written in between (a concurrent reject — including a
    ///     second reject of an already-rejected document, which changes only the reviewed-on stamp) → invoice deleted, 409
    ///     <see cref="InvoiceReviewFailure.ReviewDecisionChanged"/> — the reject stands;</item>
    ///     <item>still unlinked, decision unchanged (an unrelated write, e.g. a profiling status) → the link is
    ///     retried with the fresh version, up to <see cref="MaxConditionalWriteAttempts"/> attempts in total, then
    ///     invoice deleted, 409 <see cref="InvoiceReviewFailure.DocumentChangedConcurrently"/>.</item>
    ///   </list></item>
    ///   <item><b>Any other failure</b>: if the response was lost but the link landed (the document now points at
    ///   <paramref name="invoiceId"/>), the confirmation continues; otherwise the invoice is deleted again.</item>
    /// </list>
    /// If a needed delete leaves the invoice in place, the error names the orphaned invoice. Every clean-up read and
    /// delete runs on <see cref="CancellationToken.None"/>, so a cancelled request is still undone.
    /// </remarks>
    private async Task<LinkOutcome> LinkDocumentOrCompensateAsync(
        InvoiceReviewConfirmRequest request, Guid invoiceId, DocumentState first, CancellationToken ct)
    {
        var documentId = request.DocumentId;
        var version = first.Version;

        for (var attempt = 1; ; attempt++)
        {
            DBConcurrencyException changed;
            try
            {
                await _records.UpdateRecordFieldsIfUnchangedAsync(
                    DocumentEntity,
                    documentId,
                    new Dictionary<string, object?> { [DocInvoiceLinkBind] = $"/{InvoiceEntitySet}({invoiceId})" },
                    version,
                    ct);
                return new LinkOutcome(null);
            }
            catch (DBConcurrencyException concurrency)
            {
                changed = concurrency;
            }
            catch (Exception linkFailure)
            {
                if (await LinkLandedAsync(documentId, invoiceId))
                {
                    _logger.LogWarning(linkFailure,
                        "Linking document {DocumentId} to invoice {InvoiceId} reported a failure, but the link is in " +
                        "place (lost response); continuing", documentId, invoiceId);
                    return new LinkOutcome(null);
                }

                _logger.LogError(linkFailure,
                    "Linking document {DocumentId} to invoice {InvoiceId} failed; deleting the invoice (compensation)",
                    documentId, invoiceId);

                await DeleteCreatedInvoiceOrThrowAsync(documentId, invoiceId, linkFailure);
                throw LinkFailed(linkFailure);
            }

            _logger.LogWarning(changed,
                "Document {DocumentId} changed after it was read (version {Version}, link attempt {Attempt} of " +
                "{MaxAttempts}); re-reading it", documentId, version, attempt, MaxConditionalWriteAttempts);

            DocumentState now;
            try
            {
                now = await ReadDocumentStateAsync(request, ct);
            }
            catch (Exception readFailure)
            {
                await DeleteCreatedInvoiceOrThrowAsync(documentId, invoiceId, readFailure);
                throw LinkFailed(readFailure);
            }

            if (now.Exists && now.Link == LinkState.SameMatterAndVendor && now.LinkedInvoiceId == invoiceId)
            {
                return new LinkOutcome(null); // defensive: our own link is in place
            }

            InvoiceReviewException? refusal = null;
            if (!now.Exists)
            {
                refusal = DocumentGone();
            }
            else if (now.Link == LinkState.SameMatterAndVendor)
            {
                await DeleteCreatedInvoiceOrThrowAsync(documentId, invoiceId, changed);
                return new LinkOutcome(now.LinkedInvoiceId);
            }
            else if (now.Link == LinkState.Elsewhere)
            {
                refusal = LinkedElsewhere();
            }
            else if (now.Decision != first.Decision)
            {
                refusal = new InvoiceReviewException(
                    InvoiceReviewFailure.ReviewDecisionChanged,
                    "The document's review decision was changed by someone else (for example, it was rejected as not " +
                    "an invoice) while it was being confirmed, so the new invoice was removed again and that decision " +
                    "stands. Nothing was saved; reload the document before confirming it again.",
                    inner: changed);
            }
            else if (attempt >= MaxConditionalWriteAttempts)
            {
                refusal = new InvoiceReviewException(
                    InvoiceReviewFailure.DocumentChangedConcurrently,
                    "The document kept being changed by someone else while the invoice was being linked, so the new " +
                    "invoice was removed again. Nothing was saved; retry the confirmation.",
                    inner: changed);
            }

            if (refusal is not null)
            {
                await DeleteCreatedInvoiceOrThrowAsync(documentId, invoiceId, changed);
                throw refusal;
            }

            // Still unlinked and undecided: an unrelated write moved the version. Link again on the fresh one.
            version = now.Version;
        }
    }

    private static InvoiceReviewException LinkFailed(Exception cause) =>
        new(InvoiceReviewFailure.LinkFailed,
            "The document could not be linked to the new invoice, so the invoice was removed again. Nothing " +
            "was saved; retry the confirmation.",
            inner: cause);

    /// <summary>Whether the document now points at <paramref name="invoiceId"/>; false when that cannot be read.</summary>
    private async Task<bool> LinkLandedAsync(Guid documentId, Guid invoiceId)
    {
        try
        {
            var document = await _records.RetrieveRecordFieldsAsync(
                DocumentEntity, documentId, new[] { DocInvoiceLinkValue }, CancellationToken.None);
            return TryReadGuid(document, DocInvoiceLinkValue, out var linked) && linked == invoiceId;
        }
        catch (Exception probeFailure)
        {
            // Unknown → treat as not linked and undo. If the link DID land, deleting the invoice clears it
            // (sprk_document_Invoice_n1 cascades Delete as RemoveLink — live metadata 2026-10-01).
            _logger.LogWarning(probeFailure,
                "Could not re-read document {DocumentId} after a failed link; treating it as not linked", documentId);
            return false;
        }
    }

    /// <summary>
    /// Deletes the invoice this request created. Returns when it is gone — including when the delete reported a
    /// failure but a read-back finds no row (a lost response), so the caller reports its clean failure. Throws
    /// <see cref="InvoiceReviewFailure.LinkFailedInvoiceNotRemoved"/>, naming the invoice, only when the read-back
    /// finds it still there or cannot tell.
    /// </summary>
    private async Task DeleteCreatedInvoiceOrThrowAsync(Guid documentId, Guid invoiceId, Exception cause)
    {
        var removal = await DeleteInvoiceAsync(invoiceId, documentId);
        if (removal == InvoiceRemoval.Removed)
        {
            return;
        }

        _logger.LogError(
            "Compensation FAILED: invoice {InvoiceId} was created but is not linked to document {DocumentId}, and " +
            "{Outcome} after the delete failed. It must be removed manually.",
            invoiceId, documentId, removal == InvoiceRemoval.StillExists ? "it still exists" : "its removal could not be confirmed");

        var state = removal == InvoiceRemoval.StillExists
            ? "could not be removed again"
            : "may not have been removed again (its removal could not be confirmed)";

        throw new InvoiceReviewException(
            InvoiceReviewFailure.LinkFailedInvoiceNotRemoved,
            $"The document could not be linked to the new invoice, and invoice {invoiceId} {state}. Report this " +
            "invoice id to an administrator; the document was not changed.",
            invoiceId, cause);
    }

    /// <summary>What a delete of an invoice this request created left behind.</summary>
    private enum InvoiceRemoval
    {
        /// <summary>Deleted — or the delete reported a failure but a read-back finds no row (lost response).</summary>
        Removed,

        /// <summary>The delete failed and a read-back finds the row still there.</summary>
        StillExists,

        /// <summary>The delete failed and the read-back failed too.</summary>
        Unknown,
    }

    /// <summary>
    /// Deletes the invoice; when the delete reports a failure, reads the row back before concluding anything, so a
    /// delete whose response was lost is not reported as an orphan (task 130 round 3, residual b). Both calls run on
    /// <see cref="CancellationToken.None"/>: the undo must run even when the request that caused it was cancelled.
    /// </summary>
    private async Task<InvoiceRemoval> DeleteInvoiceAsync(Guid invoiceId, Guid documentId)
    {
        try
        {
            await _entities.DeleteAsync(InvoiceEntity, invoiceId, CancellationToken.None);
            return InvoiceRemoval.Removed;
        }
        catch (Exception deleteFailure)
        {
            try
            {
                var row = await _records.RetrieveRecordFieldsAsync(
                    InvoiceEntity, invoiceId, new[] { InvMatterValue }, CancellationToken.None);
                if (row.Count == 0)
                {
                    _logger.LogWarning(deleteFailure,
                        "Deleting invoice {InvoiceId} (document {DocumentId}) reported a failure, but the invoice is " +
                        "gone (lost response)", invoiceId, documentId);
                    return InvoiceRemoval.Removed;
                }

                _logger.LogError(deleteFailure,
                    "Deleting invoice {InvoiceId} (document {DocumentId}) failed; it still exists", invoiceId, documentId);
                return InvoiceRemoval.StillExists;
            }
            catch (Exception readBackFailure)
            {
                _logger.LogError(readBackFailure,
                    "Deleting invoice {InvoiceId} (document {DocumentId}) failed ({DeleteFailure}), and reading it back " +
                    "failed too", invoiceId, documentId, deleteFailure.GetType().Name);
                return InvoiceRemoval.Unknown;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // Step 3: Update Document Review Status
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Mark the document ConfirmedInvoice (with timestamp and optional reviewer notes) — CONDITIONAL on the version
    /// read (<c>If-Match</c>; a deleted document is a 404, never recreated), and only while the document is still
    /// linked to <paramref name="invoiceId"/>. <paramref name="knownVersion"/> is the version of a read with no write
    /// since (the resume path); otherwise the document is read first. A 412 from an unrelated write is retried on a
    /// fresh read, up to <see cref="MaxConditionalWriteAttempts"/> attempts in total.
    /// </summary>
    /// <exception cref="InvoiceReviewException"><see cref="InvoiceReviewFailure.StatusNotUpdated"/> when the
    /// document is no longer linked to the invoice (someone re-pointed it).</exception>
    private async Task MarkDocumentConfirmedAsync(
        InvoiceReviewConfirmRequest request, Guid invoiceId, long? knownVersion, CancellationToken ct)
    {
        var fields = BuildReviewStatusFields(ReviewStatusConfirmedInvoice, request.Notes, DateTime.UtcNow);
        var version = knownVersion;

        for (var attempt = 1; ; attempt++)
        {
            if (version is null)
            {
                var document = await _records.RetrieveRecordFieldsAsync(
                    DocumentEntity, request.DocumentId, new[] { DocInvoiceLinkValue, VersionNumber }, ct);
                if (document.Count == 0)
                {
                    throw new KeyNotFoundException($"{DocumentEntity} record was not found.");
                }

                if (!TryReadGuid(document, DocInvoiceLinkValue, out var linked) || linked != invoiceId)
                {
                    _logger.LogWarning(
                        "Document {DocumentId} is no longer linked to invoice {InvoiceId} (now {LinkedInvoiceId}); " +
                        "not marking it confirmed", request.DocumentId, invoiceId, linked);

                    throw new InvoiceReviewException(
                        InvoiceReviewFailure.StatusNotUpdated,
                        $"Invoice {invoiceId} was created and linked to the document, but the document was then changed " +
                        "by someone else and is no longer linked to it, so it was not marked as a confirmed invoice. " +
                        "Report this invoice id to an administrator.",
                        invoiceId);
                }

                version = ReadVersion(document, "the confirmation cannot mark it safely");
            }

            try
            {
                await _records.UpdateRecordFieldsIfUnchangedAsync(DocumentEntity, request.DocumentId, fields, version.Value, ct);
                _logger.LogDebug("Marked document {DocumentId} ConfirmedInvoice (version {Version})", request.DocumentId, version);
                return;
            }
            catch (DBConcurrencyException changed) when (attempt < MaxConditionalWriteAttempts)
            {
                _logger.LogWarning(changed,
                    "Document {DocumentId} changed before its confirmed status was written (attempt {Attempt} of " +
                    "{MaxAttempts}); re-reading it", request.DocumentId, attempt, MaxConditionalWriteAttempts);
                version = null;
            }
        }
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
    /// <remarks>
    /// The status write is CONDITIONAL on the version read with the link (<c>If-Match</c>), and a linked document is
    /// refused (409 <see cref="InvoiceReviewFailure.DocumentLinkedToInvoice"/>): a confirm that links after this
    /// read makes the write 412, the re-read sees the link, and the reject refuses instead of overwriting
    /// ConfirmedInvoice. An unrelated write is retried, up to <see cref="MaxConditionalWriteAttempts"/> in total.
    /// </remarks>
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
        var fields = BuildReviewStatusFields(ReviewStatusRejectedNotInvoice, request.Notes, DateTime.UtcNow);
        for (var attempt = 1; ; attempt++)
        {
            var document = await _records.RetrieveRecordFieldsAsync(
                DocumentEntity, request.DocumentId, new[] { DocInvoiceLinkValue, VersionNumber }, ct);
            if (document.Count == 0)
            {
                throw new KeyNotFoundException($"{DocumentEntity} record was not found.");
            }

            if (TryReadGuid(document, DocInvoiceLinkValue, out var linkedInvoiceId))
            {
                // The caller was authorized against the document only: the invoice id is logged, never returned.
                _logger.LogWarning(
                    "Document {DocumentId} is linked to invoice {InvoiceId}; refusing to reject it as not an invoice " +
                    "(409). CorrelationId={CorrelationId}", request.DocumentId, linkedInvoiceId, correlationId);

                throw new InvoiceReviewException(
                    InvoiceReviewFailure.DocumentLinkedToInvoice,
                    "The document has been confirmed as an invoice and is linked to it, so it cannot be rejected as " +
                    "not an invoice. Nothing was saved.");
            }

            var version = ReadVersion(document, "the rejection cannot be written safely");
            try
            {
                await _records.UpdateRecordFieldsIfUnchangedAsync(DocumentEntity, request.DocumentId, fields, version, ct);
                break;
            }
            catch (DBConcurrencyException changed) when (attempt < MaxConditionalWriteAttempts)
            {
                _logger.LogWarning(changed,
                    "Document {DocumentId} changed before its rejection was written (attempt {Attempt} of " +
                    "{MaxAttempts}); re-reading it", request.DocumentId, attempt, MaxConditionalWriteAttempts);
            }
            catch (DBConcurrencyException changed)
            {
                throw new InvoiceReviewException(
                    InvoiceReviewFailure.DocumentChangedConcurrently,
                    "The document kept being changed by someone else while it was being rejected. Nothing was saved; " +
                    "retry the rejection.",
                    inner: changed);
            }
        }

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

    /// <summary>
    /// A date-time column as UTC, whichever shape the reader returns: the Web API answers an ISO string, the
    /// ServiceClient a <see cref="DateTime"/>. Null when absent or unreadable.
    /// </summary>
    private static DateTime? TryReadUtc(Dictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var raw) ? raw switch
        {
            DateTime dt => dt.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dt, DateTimeKind.Utc) : dt.ToUniversalTime(),
            DateTimeOffset dto => dto.UtcDateTime,
            string text when DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed) => parsed.UtcDateTime,
            _ => null,
        } : null;

    private static int? TryReadInt(Dictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var raw)
        && raw is not null
        && int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}
