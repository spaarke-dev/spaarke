using System.Data;
using System.Globalization;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Services.Dataverse;
using Sprk.Bff.Api.Services.Finance.Submission;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Finance;

/// <summary>
/// Handles human-in-the-loop invoice review confirmation and rejection.
/// After AI classification flags a document as an invoice candidate, a human reviewer confirms
/// the identification (with the firm's invoice number), or rejects the document as not an invoice.
/// </summary>
/// <remarks>
/// <para><b>Confirm (ebilling-r1 task 050a, owner decision D-134, refining D-71).</b> Confirm creates NO invoice and queues no
/// extraction: it submits the document through the one invoice intake, <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>
/// (the same firm, firm-link, core-record and owner checks as an upload; no upload, no second document; one submission per document). No
/// confirm path creates an invoice: the submission's processing job creates it (ADR-002, D-19) and, since ebilling-r1 task 094, COPIES the
/// document's bytes into the invoice's Stored File (procedure-and-file-model 9.1; the document is never re-filed, re-owned or linked to the
/// invoice: <see cref="Submission.SubmissionCommitGate"/>). The firm invoice number is required (D-91). The document's review
/// decision and version are read BEFORE the intake (a document already rejected is refused, 409
/// <see cref="InvoiceReviewFailure.ReviewDecisionChanged"/>); when the submission is received, the document is marked ConfirmedInvoice,
/// conditional on that version (an unrelated change is re-read and retried; a reject that landed in between is 409
/// <see cref="InvoiceReviewFailure.ReviewDecisionChanged"/> and the job refuses the submission). A failure there names the submission, and a
/// retry of the confirm returns the same submission and completes the mark.</para>
/// <para><b>Reject</b> is conditional on the version it read and refuses a document that is linked to an invoice or that has a confirm
/// submission, so neither decision silently overwrites the other.</para>
/// Per ADR-013: Extends BFF API (not a separate service).
/// Per ADR-015: NEVER log document content or PII. Only IDs, statuses, and timings.
/// </remarks>
public interface IInvoiceReviewService
{
    /// <summary>
    /// Confirm a document as an invoice: submit it through the invoice intake (D-134) and mark it ConfirmedInvoice.
    /// </summary>
    /// <param name="request">The confirmation request with document, matter, vendor (the firm), and the firm invoice number.</param>
    /// <param name="correlationId">Correlation ID for distributed tracing.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The intake's outcome: Received with the submission's receipt, or its refusal (never thrown).</returns>
    /// <exception cref="InvoiceReviewException">The document was rejected before or during the confirm
    /// (<see cref="InvoiceReviewFailure.ReviewDecisionChanged"/>; the submission is named when it exists), it was deleted after its submission
    /// (<see cref="InvoiceReviewFailure.DocumentNotFound"/>, naming the submission), or the submission was received but the document's review
    /// status could not be set (<see cref="InvoiceReviewFailure.StatusNotUpdated"/>, naming the submission; a retry completes it), or the
    /// document's review state could not be read before anything was saved (<see cref="InvoiceReviewFailure.ReviewStateUnreadable"/>, 503).</exception>
    Task<InvoiceSubmissionOutcome> ConfirmInvoiceAsync(
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
    /// <exception cref="InvoiceReviewException">The document is linked to an invoice or was confirmed
    /// (<see cref="InvoiceReviewFailure.DocumentLinkedToInvoice"/>), kept changing
    /// (<see cref="InvoiceReviewFailure.DocumentChangedConcurrently"/>), or its confirm could not be read
    /// (<see cref="InvoiceReviewFailure.ConfirmStateUnreadable"/>); nothing was written.</exception>
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

    /// <summary>The matter this invoice relates to (required): the submission's core record.</summary>
    public Guid MatterId { get; init; }

    /// <summary>The vendor organization (required): the submitting law firm, which must be one of the matter's assigned law firms (D-110).</summary>
    public Guid VendorOrgId { get; init; }

    /// <summary>The firm's invoice number (required, D-91; never defaulted from the classifier's hint).</summary>
    public string? InvoiceNumber { get; init; }

    /// <summary>Not used since 050a (D-134): the submission job reads the invoice date from the document. Accepted for compatibility.</summary>
    public DateTime? InvoiceDate { get; init; }

    /// <summary>Not used since 050a (D-134): the submission job reads the total from the document. Accepted for compatibility.</summary>
    public decimal? TotalAmount { get; init; }

    /// <summary>Optional reviewer notes — written to the document's <c>sprk_invoicereviewnotes</c>.</summary>
    public string? Notes { get; init; }

    /// <summary>
    /// unified-access-control-r2 task 146 c1-r1 (owner round 13 item 9): the reviewer confirming the invoice — set by the
    /// endpoint from the signed-in caller, passed to the intake as the requester of the records the app creates. 🔒 Never bound
    /// from the request body: a client never chooses who created a record.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public RecordRequester? RequestedBy { get; init; }

    /// <summary>
    /// ebilling-r1 task 094 fix round (F1-b): the route's step A (file an unfiled source communication under the matter, as the caller),
    /// handed to the intake, which runs it only after every read-only refusal (<see cref="InvoiceSubmissionRequest.FileSourceBeforeWrite"/>).
    /// Never bound from the request body.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<CancellationToken, Task<bool>>? FileSourceBeforeWrite { get; init; }
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

/// <summary>Why a review decision was refused or did not complete. Each value's message says what was saved.</summary>
public enum InvoiceReviewFailure
{
    /// <summary>
    /// The document does not exist (deleted after the authorization check). Reject: nothing was written. Confirm (050a review #6): the document
    /// was deleted after its submission was received; the submission is named, and no retry can complete the mark.
    /// </summary>
    DocumentNotFound,

    /// <summary>Confirm only: the submission was received and queued, but the document's review status was not set. Retry completes it.</summary>
    StatusNotUpdated,

    /// <summary>Reject only: every one of the bounded conditional attempts found a newer version. Nothing was saved; retry.</summary>
    DocumentChangedConcurrently,

    /// <summary>
    /// Reject only: the document is linked to an invoice or has a confirm submission (it was confirmed), so it cannot be rejected as not
    /// an invoice. Nothing was written; neither the invoice nor the submission is named.
    /// </summary>
    DocumentLinkedToInvoice,

    /// <summary>Reject only: whether the document has a confirm submission could not be read. Nothing was written; transient (503).</summary>
    ConfirmStateUnreadable,

    /// <summary>
    /// Confirm only (050a review #2): the document is rejected as not an invoice: already when the confirm read it (nothing was saved), or by
    /// a reject that landed between the confirm's read and its mark (the submission is named; the job refuses it, so no invoice is created).
    /// The reject stands (409).
    /// </summary>
    ReviewDecisionChanged,

    /// <summary>
    /// Confirm only (050a review round 2, item 5): the document's review decision and version, read before the intake, could not be read.
    /// Nothing was saved; transient (503), retry.
    /// </summary>
    ReviewStateUnreadable,
}

/// <summary>
/// A review decision refused before any write, or a confirm whose submission was received but whose document mark did not land.
/// </summary>
public sealed class InvoiceReviewException : Exception
{
    public InvoiceReviewException(InvoiceReviewFailure failure, string message, Guid? submissionId = null, Exception? inner = null)
        : base(message, inner)
    {
        Failure = failure;
        SubmissionId = submissionId;
    }

    public InvoiceReviewFailure Failure { get; }

    /// <summary>The confirm's submission when it exists (<see cref="InvoiceReviewFailure.StatusNotUpdated"/>); null otherwise.</summary>
    public Guid? SubmissionId { get; }

    /// <summary>The document no longer exists — reject's 404 (ADR-019).</summary>
    public const string DocumentNotFoundReasonCode = "sdap.finance.invoice_review.document_not_found";

    /// <summary>Machine-readable reason (ADR-019), e.g. <c>sdap.finance.invoice_review.status_not_updated</c>.</summary>
    public string ReasonCode => Failure switch
    {
        InvoiceReviewFailure.DocumentNotFound => DocumentNotFoundReasonCode,
        InvoiceReviewFailure.StatusNotUpdated => "sdap.finance.invoice_review.status_not_updated",
        InvoiceReviewFailure.DocumentChangedConcurrently => "sdap.finance.invoice_review.document_changed",
        InvoiceReviewFailure.DocumentLinkedToInvoice => "sdap.finance.invoice_review.document_linked_to_invoice",
        InvoiceReviewFailure.ReviewDecisionChanged => "sdap.finance.invoice_review.review_decision_changed",
        InvoiceReviewFailure.ReviewStateUnreadable => "sdap.finance.invoice_review.review_state_unreadable",
        _ => "sdap.finance.invoice_review.confirm_state_unreadable",
    };
}

/// <summary>
/// The review decisions on a classified document: confirm (submit it through the invoice intake, then mark it ConfirmedInvoice) and
/// reject (mark it RejectedNotInvoice). It creates no invoice (ebilling-r1 050a, D-134).
/// </summary>
public class InvoiceReviewService : IInvoiceReviewService
{
    private readonly IFieldMappingDataverseService _records;
    private readonly InvoiceSubmissionService _submissions;
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

    // Invoice fields the submission job's persister writes (InvoiceSubmissionPersister) and the read services read.
    /// <summary>
    /// Attempts, in total, of each conditional document write (confirm's link, confirm's status, reject's status)
    /// when the document keeps changing under it without the change being a decision about it.
    /// </summary>
    internal const int MaxConditionalWriteAttempts = 3;
    internal const string InvMatterValue = "_sprk_matter_value";
    internal const string InvVendorOrgValue = "_sprk_vendororg_value";
    internal const string InvName = "sprk_name";
    internal const string InvExtractionStatus = "sprk_extractionstatus";
    internal const string InvInvoiceNumber = "sprk_invoicenumber";

    /// <summary>
    /// <c>sprk_invoice.sprk_trackingnumber</c> (ebilling-r1 task 049 part A, D-124): the D-87 tracking number <c>{firm invoice number}.{n}</c>,
    /// copied from the submission when the invoice is created and never changed. The firm's own number stays in <see cref="InvInvoiceNumber"/>.
    /// Same logical name as the submission's column (<c>TrackingNumberAllocator.TrackingNumberColumn</c>), by design.
    /// </summary>
    internal const string InvTrackingNumber = "sprk_trackingnumber";

    /// <summary>
    /// <c>sprk_invoice.sprk_trackingnumber</c> MaxLength (100, as the submission's): the allocator caps the firm invoice number at 89
    /// characters (<c>TrackingNumberAllocator.MaxFirmInvoiceNumberLength</c>) and adds a dot and an n of up to 10 digits.
    /// </summary>
    internal const int InvTrackingNumberMaxLength = 100;
    internal const string InvInvoiceDate = "sprk_invoicedate";
    internal const string InvTotalAmount = "sprk_totalamount";

    private const string OrganizationName = "sprk_organizationname";

    /// <summary>sprk_invoice.sprk_name MaxLength (live: 850).</summary>
    internal const int InvoiceNameMaxLength = 850;

    public InvoiceReviewService(
        IFieldMappingDataverseService records,
        InvoiceSubmissionService submissions,
        FinanceTelemetry telemetry,
        ILogger<InvoiceReviewService> logger)
    {
        _records = records ?? throw new ArgumentNullException(nameof(records));
        _submissions = submissions ?? throw new ArgumentNullException(nameof(submissions));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InvoiceSubmissionOutcome> ConfirmInvoiceAsync(
        InvoiceReviewConfirmRequest request,
        string correlationId,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var activity = _telemetry.StartActivity(
            "InvoiceReview.Confirm",
            request.DocumentId.ToString(),
            correlationId);

        _logger.LogInformation(
            "Starting invoice review confirmation. DocumentId={DocumentId}, MatterId={MatterId}, " +
            "VendorOrgId={VendorOrgId}, CorrelationId={CorrelationId}",
            request.DocumentId, request.MatterId, request.VendorOrgId, correlationId);

        // 050a review #2: the review decision and version BEFORE the intake. The mark is conditional on this version, so a reject that lands
        // between the submission's create and the mark is seen, never overwritten. A document already rejected is not confirmed (nothing saved).
        // A missing document reads as null: the intake answers it (404 document_not_found).
        ReviewState? before;
        try
        {
            before = await ReadReviewStateAsync(request.DocumentId, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // 050a review round 2, item 5: a fault of this read happens before anything is written; transient (503), never a 500.
            _logger.LogWarning(ex,
                "Invoice review confirmation refused: the review state of document {DocumentId} could not be read (503). CorrelationId={CorrelationId}",
                request.DocumentId, correlationId);
            throw new InvoiceReviewException(
                InvoiceReviewFailure.ReviewStateUnreadable,
                "The document's review state could not be read right now. Nothing was saved; retry the confirmation.",
                inner: ex);
        }

        if (before is { Decision.Status: ReviewStatusRejectedNotInvoice })
        {
            _logger.LogInformation(
                "Invoice review confirmation refused: document {DocumentId} is rejected as not an invoice (409). CorrelationId={CorrelationId}",
                request.DocumentId, correlationId);
            throw new InvoiceReviewException(
                InvoiceReviewFailure.ReviewDecisionChanged,
                "The document has been rejected as not an invoice, so it cannot be confirmed. Nothing was saved; reload the document.");
        }

        // D-134: the one intake, its existing-document entry. The vendor is the submitting firm; the matter the core record. The number is
        // passed as the reviewer typed it (D-91: the intake refuses a missing one; nothing here defaults it).
        var outcome = await _submissions.SubmitExistingDocumentAsync(new InvoiceSubmissionRequest
        {
            FirmId = request.VendorOrgId,
            CoreRecord = new InvoiceCoreRecord(InvoiceCoreRecordKind.Matter, request.MatterId),
            Channel = InvoiceSubmissionChannel.Internal,
            FirmInvoiceNumber = request.InvoiceNumber,
            ExistingDocumentId = request.DocumentId,
            CallerObjectId = request.RequestedBy?.ObjectId,
            CorrelationId = correlationId,
            FileSourceBeforeWrite = request.FileSourceBeforeWrite,
        }, ct);

        if (outcome.Kind != InvoiceSubmissionOutcomeKind.Received)
        {
            _logger.LogInformation(
                "Invoice review confirmation refused by the intake ({Kind}, {StatusCode}, {ErrorCode}). DocumentId={DocumentId}, " +
                "CorrelationId={CorrelationId}", outcome.Kind, outcome.StatusCode, outcome.ErrorCode, request.DocumentId, correlationId);
            return outcome;
        }

        var receipt = outcome.Receipt!;
        await MarkDocumentConfirmedAsync(request.DocumentId, request.Notes, receipt.SubmissionId, before);

        _logger.LogInformation(
            "Invoice review confirmed. DocumentId={DocumentId}, SubmissionId={SubmissionId}, JobId={JobId}, CorrelationId={CorrelationId}",
            request.DocumentId, receipt.SubmissionId, receipt.JobId, correlationId);
        return outcome;
    }

    /// <summary>
    /// Marks the document ConfirmedInvoice once its submission is received. The first write is conditional on the version read BEFORE the
    /// intake (<paramref name="before"/>, 050a review #2). On a newer version the document is read again: a review decision changed to
    /// RejectedNotInvoice is 409 <see cref="InvoiceReviewFailure.ReviewDecisionChanged"/> naming the submission (the reject stands; the job
    /// refuses the submission); anything else is an unrelated write and is retried on the new version, <see cref="MaxConditionalWriteAttempts"/>
    /// in total. It runs under <see cref="CancellationToken.None"/>: the submission is already saved and queued, so a caller that goes away does
    /// not leave the mark half-done. A deleted document is <see cref="InvoiceReviewFailure.DocumentNotFound"/> naming the submission (review
    /// #6); any other failure is <see cref="InvoiceReviewFailure.StatusNotUpdated"/> naming the submission, and a retry of the confirm returns
    /// that submission and marks again.
    /// </summary>
    private async Task MarkDocumentConfirmedAsync(Guid documentId, string? notes, Guid submissionId, ReviewState? before)
    {
        var fields = BuildReviewStatusFields(ReviewStatusConfirmedInvoice, notes, DateTime.UtcNow);
        try
        {
            var state = before ?? await ReadReviewStateAsync(documentId, CancellationToken.None)
                ?? throw new KeyNotFoundException($"{DocumentEntity} record was not found.");
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    await _records.UpdateRecordFieldsIfUnchangedAsync(DocumentEntity, documentId, fields, state.Version, CancellationToken.None);
                    _logger.LogDebug("Marked document {DocumentId} ConfirmedInvoice (version {Version})", documentId, state.Version);
                    return;
                }
                catch (DBConcurrencyException changed) when (attempt < MaxConditionalWriteAttempts)
                {
                    var now = await ReadReviewStateAsync(documentId, CancellationToken.None)
                        ?? throw new KeyNotFoundException($"{DocumentEntity} record was not found.");
                    if (now.Decision.Status == ReviewStatusRejectedNotInvoice && now.Decision != before?.Decision)
                    {
                        _logger.LogWarning(changed,
                            "Document {DocumentId} was rejected as not an invoice after submission {SubmissionId} was received; the reject " +
                            "stands (409) and the job refuses the submission", documentId, submissionId);
                        throw new InvoiceReviewException(
                            InvoiceReviewFailure.ReviewDecisionChanged,
                            $"The document was rejected as not an invoice by someone else while it was being confirmed (submission {submissionId}). " +
                            "The rejection stands: that submission will not create an invoice, and the document was not marked as confirmed.",
                            submissionId,
                            changed);
                    }

                    _logger.LogWarning(changed,
                        "Document {DocumentId} changed before its confirmed status was written (attempt {Attempt} of " +
                        "{MaxAttempts}); re-reading it", documentId, attempt, MaxConditionalWriteAttempts);
                    state = now;
                }
            }
        }
        catch (InvoiceReviewException)
        {
            throw;
        }
        catch (KeyNotFoundException gone)
        {
            _logger.LogWarning(gone, "Document {DocumentId}: submission {SubmissionId} was received but the document no longer exists",
                documentId, submissionId);
            throw new InvoiceReviewException(
                InvoiceReviewFailure.DocumentNotFound,
                $"The invoice was submitted for processing (submission {submissionId}), but the document no longer exists, so it could not " +
                "be marked as a confirmed invoice. A retry cannot complete it.",
                submissionId,
                gone);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Document {DocumentId}: submission {SubmissionId} was received but the document was not marked ConfirmedInvoice",
                documentId, submissionId);
            throw new InvoiceReviewException(
                InvoiceReviewFailure.StatusNotUpdated,
                $"The invoice was submitted for processing (submission {submissionId}), but the document could not be marked as a " +
                "confirmed invoice. Retry the confirmation; it returns the same submission and completes the mark.",
                submissionId,
                ex);
        }
    }

    /// <summary>
    /// The document's review decision (status, reviewed-on stamp and notes: every decision writes all three, so a second reject of an
    /// already-rejected document is told apart from an unrelated write by its stamp) and the version a conditional write is made on.
    /// </summary>
    internal sealed record ReviewState(long Version, ReviewDecision Decision);

    /// <summary>A review decision as <see cref="BuildReviewStatusFields"/> writes it; equal only if no decision was written in between.</summary>
    internal readonly record struct ReviewDecision(int? Status, DateTime? ReviewedOnUtc, string? Notes);

    /// <summary>The document's <see cref="ReviewState"/>, or null when the document does not exist.</summary>
    private async Task<ReviewState?> ReadReviewStateAsync(Guid documentId, CancellationToken ct)
    {
        var document = await _records.RetrieveRecordFieldsAsync(
            DocumentEntity, documentId, new[] { VersionNumber, DocInvoiceReviewStatus, DocInvoiceReviewedOn, DocInvoiceReviewNotes }, ct);
        if (document.Count == 0)
        {
            return null;
        }

        return new ReviewState(
            ReadVersion(document, "the confirmation cannot mark it safely"),
            new ReviewDecision(
                TryReadInt(document, DocInvoiceReviewStatus),
                TryReadUtc(document, DocInvoiceReviewedOn),
                document.TryGetValue(DocInvoiceReviewNotes, out var reviewNotes) ? reviewNotes?.ToString() : null));
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
    // Reject Invoice Review
    // ═══════════════════════════════════════════════════════════════════════════

    /// <inheritdoc />
    /// <remarks>
    /// The status write is CONDITIONAL on the version read with the link (<c>If-Match</c>). A document linked to an invoice, or with a
    /// confirm submission (050a: the confirm's submission exists before the job links the document), is refused (409
    /// <see cref="InvoiceReviewFailure.DocumentLinkedToInvoice"/>): a confirm that lands after this read marks the document, the write
    /// is 412, the re-read sees the submission, and the reject refuses instead of overwriting ConfirmedInvoice. An unrelated write is
    /// retried, up to <see cref="MaxConditionalWriteAttempts"/> in total.
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
        var confirmSubmissionId = InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(request.DocumentId);
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

                throw AlreadyConfirmed();
            }

            // 050a: the confirm's submission exists from the moment the confirm is received, before its job links the document.
            InvoiceSubmissionView? confirmed;
            try
            {
                confirmed = await _submissions.GetAsync(confirmSubmissionId, ct);
            }
            catch (SdapProblemException unreadable)
            {
                throw new InvoiceReviewException(
                    InvoiceReviewFailure.ConfirmStateUnreadable,
                    "Whether the document was already confirmed as an invoice could not be read right now. Nothing was saved; " +
                    "retry the rejection.",
                    inner: unreadable);
            }

            if (confirmed is not null)
            {
                _logger.LogWarning(
                    "Document {DocumentId} has confirm submission {SubmissionId}; refusing to reject it as not an invoice (409). " +
                    "CorrelationId={CorrelationId}", request.DocumentId, confirmed.SubmissionId, correlationId);

                throw AlreadyConfirmed();
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

    private static InvoiceReviewException AlreadyConfirmed() =>
        new(InvoiceReviewFailure.DocumentLinkedToInvoice,
            "The document has been confirmed as an invoice, so it cannot be rejected as not an invoice. Nothing was saved.");

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
