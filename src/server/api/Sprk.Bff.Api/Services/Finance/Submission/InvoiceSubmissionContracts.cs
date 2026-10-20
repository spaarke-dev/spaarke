using Sprk.Bff.Api.Infrastructure.ExternalAccess;
using Sprk.Bff.Api.Services.Dataverse;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The channel a submission arrived through (<c>sprk_invoicesubmission.sprk_channel</c>, task 020). R1 uses SPA and Internal; Mailbox, Outlook
/// and API are reserved option values with no writer.
/// </summary>
public enum InvoiceSubmissionChannel
{
    /// <summary>The external SPA route (<c>/api/v1/external/...</c>), a firm's contact.</summary>
    Spa = 100000000,

    /// <summary>The internal route (<c>/api/finance/...</c>), a Dataverse user submitting on a firm's behalf (D-24).</summary>
    Internal = 100000001,
}

/// <summary><c>sprk_invoicesubmission.sprk_processingstatus</c> (task 020). Intake writes only <see cref="Received"/>.</summary>
public enum InvoiceSubmissionStatus
{
    Received = 100000000,
    Processing = 100000001,
    Accepted = 100000002,
    Rejected = 100000003,
    Failed = 100000004,
}

/// <summary>The kind of core record a submission is filed against (FR-05: exactly one).</summary>
public enum InvoiceCoreRecordKind
{
    Matter,
    Project,
    WorkAssignment,
}

/// <summary>
/// Whether a firm is linked to a core record (ebilling-r1 D-106, refined by D-110): "linked" means the record's assigned law firms
/// (<c>sprk_assignedlawfirm1/2</c>) and nothing else. An organization-level access grant does not link a firm.
/// </summary>
public enum InvoiceFirmLink
{
    /// <summary>The firm is one of the record's assigned law firms.</summary>
    Linked,

    /// <summary>The record's links were read and the firm is none of them.</summary>
    NotLinked,

    /// <summary>The record's links could not be read (a fault, a missing row, an incomplete answer). Never "linked" and never a final "not linked": 503.</summary>
    Unreadable,
}

/// <summary>A core record (matter, project or work assignment) named by a route.</summary>
public sealed record InvoiceCoreRecord(InvoiceCoreRecordKind Kind, Guid RecordId)
{
    /// <summary>The table's logical name.</summary>
    public string EntityLogicalName => Kind switch
    {
        InvoiceCoreRecordKind.Matter => "sprk_matter",
        InvoiceCoreRecordKind.Project => "sprk_project",
        _ => "sprk_workassignment",
    };

    /// <summary>The submission's lookup that names this record (task 013, the <c>sprk_regarding{core}</c> trio).</summary>
    public string SubmissionColumn => Kind switch
    {
        InvoiceCoreRecordKind.Matter => "sprk_regardingmatter",
        InvoiceCoreRecordKind.Project => "sprk_regardingproject",
        _ => "sprk_regardingworkassignment",
    };

    /// <summary>The external grant root of the same record.</summary>
    internal ExternalGrantRootType GrantRootType => Kind switch
    {
        InvoiceCoreRecordKind.Matter => ExternalGrantRootType.Matter,
        InvoiceCoreRecordKind.Project => ExternalGrantRootType.Project,
        _ => ExternalGrantRootType.WorkAssignment,
    };

    /// <summary>The external document parent of the same record.</summary>
    internal ExternalDataService.DocumentParentKind DocumentParentKind => Kind switch
    {
        InvoiceCoreRecordKind.Matter => ExternalDataService.DocumentParentKind.Matter,
        InvoiceCoreRecordKind.Project => ExternalDataService.DocumentParentKind.Project,
        _ => ExternalDataService.DocumentParentKind.WorkAssignment,
    };
}

/// <summary>The uploaded file as the route received it. The bytes are already buffered (the size limit is checked before).</summary>
/// <param name="FileName">The client's file name; sanitized to one flat segment before it reaches SPE.</param>
/// <param name="ContentType">The multipart part's content type.</param>
/// <param name="Content">The file bytes.</param>
public sealed record InvoiceSubmissionFile(string? FileName, string? ContentType, byte[] Content);

/// <summary>
/// One submission (FR-17). Built by a route AFTER its authorization filter decided the caller, the firm and the core record; the service
/// never authorizes, it validates and writes.
/// </summary>
public sealed record InvoiceSubmissionRequest
{
    /// <summary>
    /// The firm the invoice is from (NFR-10): the external caller's own organization, or the firm an internal caller chose. The route decides
    /// who may name it; <see cref="InvoiceSubmissionService.SubmitAsync"/> then checks it is active and LINKED to <see cref="CoreRecord"/>
    /// (D-106 / D-110), whichever channel the request came through.
    /// </summary>
    public required Guid FirmId { get; init; }

    /// <summary>The core record the invoice is filed against.</summary>
    public required InvoiceCoreRecord CoreRecord { get; init; }

    /// <summary>The channel; decides the submitter and the document's creator stamp.</summary>
    public required InvoiceSubmissionChannel Channel { get; init; }

    /// <summary>The firm invoice number the submitter typed. Required (D-91); validated by the service.</summary>
    public string? FirmInvoiceNumber { get; init; }

    /// <summary>Optional PO number (at most 100 characters).</summary>
    public string? PoNumber { get; init; }

    /// <summary>The PDF; null when the request carried none. Always null for <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>.</summary>
    public InvoiceSubmissionFile? File { get; init; }

    /// <summary>
    /// ebilling-r1 task 050a (D-134): the <c>sprk_document</c> already holding the PDF (an email attachment a reviewer confirmed as an invoice).
    /// Set only for <see cref="InvoiceSubmissionService.SubmitExistingDocumentAsync"/>, which uploads nothing and creates no document: the
    /// submission points at this one. Null for an upload (<see cref="InvoiceSubmissionService.SubmitAsync"/>).
    /// </summary>
    public Guid? ExistingDocumentId { get; init; }

    /// <summary>SPA channel: the resolved external principal (the submitter contact and the document's creator stamp).</summary>
    public CallerPrincipal? ExternalCaller { get; init; }

    /// <summary>Internal channel: the caller's Entra object id (the creator person of the rows the application writes).</summary>
    public Guid? CallerObjectId { get; init; }

    /// <summary>The request's correlation id, carried to the job.</summary>
    public string? CorrelationId { get; init; }
}

/// <summary>A submission that was received: the row exists with its tracking number and the processing job is enqueued.</summary>
public sealed record InvoiceSubmissionReceipt(Guid SubmissionId, Guid JobId, string TrackingNumber, Guid DocumentId);

/// <summary>How a submission ended.</summary>
public enum InvoiceSubmissionOutcomeKind
{
    /// <summary>Received; <see cref="InvoiceSubmissionOutcome.Receipt"/> is set.</summary>
    Received,

    /// <summary>Refused with a stable code; nothing is left behind.</summary>
    Refused,

    /// <summary>No owner team could be resolved for the new rows; nothing was written.</summary>
    OwnerRefused,
}

/// <summary>The result of <see cref="InvoiceSubmissionService.SubmitAsync"/>.</summary>
public sealed record InvoiceSubmissionOutcome
{
    public required InvoiceSubmissionOutcomeKind Kind { get; init; }

    public InvoiceSubmissionReceipt? Receipt { get; init; }

    /// <summary>HTTP status of a refusal.</summary>
    public int StatusCode { get; init; }

    /// <summary>Stable error code of a refusal (ADR-019).</summary>
    public string? ErrorCode { get; init; }

    /// <summary>Human-readable detail of a refusal; never echoes business data.</summary>
    public string? Detail { get; init; }

    public RecordOwnerResolution? OwnerRefusal { get; init; }

    internal static InvoiceSubmissionOutcome Received(InvoiceSubmissionReceipt receipt) =>
        new() { Kind = InvoiceSubmissionOutcomeKind.Received, Receipt = receipt, StatusCode = StatusCodes.Status202Accepted };

    internal static InvoiceSubmissionOutcome Refused(int statusCode, string errorCode, string detail) =>
        new() { Kind = InvoiceSubmissionOutcomeKind.Refused, StatusCode = statusCode, ErrorCode = errorCode, Detail = detail };

    internal static InvoiceSubmissionOutcome OwnerRefused(RecordOwnerResolution resolution) =>
        new() { Kind = InvoiceSubmissionOutcomeKind.OwnerRefused, OwnerRefusal = resolution, StatusCode = StatusCodes.Status409Conflict };
}

/// <summary>A stored submission as the status routes return it. Firm-visible: carries only facts about this submission (NFR-10).</summary>
public sealed record InvoiceSubmissionView
{
    public required Guid SubmissionId { get; init; }

    public string? TrackingNumber { get; init; }

    public string? FirmInvoiceNumber { get; init; }

    public string? PoNumber { get; init; }

    /// <summary>Received, Processing, Accepted, Rejected or Failed; null when the stored value is unknown.</summary>
    public string? Status { get; init; }

    /// <summary>SPA or Internal (or a reserved channel's name).</summary>
    public string? Channel { get; init; }

    public DateTime? ReceivedOn { get; init; }

    /// <summary>The stable reason code of a terminal outcome, when the job set one.</summary>
    public string? OutcomeReasonCode { get; init; }

    public Guid? DocumentId { get; init; }

    /// <summary>The firm. Used by the routes to scope; returned to the caller (it is their own firm).</summary>
    public Guid? FirmId { get; init; }

    /// <summary>The core record the submission is filed against; null when the stored row names none.</summary>
    public InvoiceCoreRecord? CoreRecord { get; init; }

    public DateTime? CreatedOn { get; init; }
}

/// <summary>Stable error codes of the submission operation (ADR-019). The strings are a contract with the SPA (051) and the review UI.</summary>
public static class InvoiceSubmissionErrors
{
    public const string FileRequired = "ebilling.submission.file_required";
    public const string NotPdf = "ebilling.submission.not_pdf";
    public const string TooLarge = "ebilling.submission.too_large";
    public const string FirmForbidden = "ebilling.submission.firm_forbidden";
    public const string RecordForbidden = "ebilling.submission.record_forbidden";

    /// <summary>D-91: the firm invoice number is required; missing or blank is refused before any upload or write.</summary>
    public const string FirmInvoiceNumberRequired = "ebilling.submission.firm_invoice_number_required";

    /// <summary>The firm invoice number is longer than the tracking number column allows (<see cref="TrackingNumberAllocator.MaxFirmInvoiceNumberLength"/>).</summary>
    public const string FirmInvoiceNumberTooLong = "ebilling.submission.firm_invoice_number_too_long";

    public const string PoNumberTooLong = "ebilling.submission.po_number_too_long";

    /// <summary>The firm id is malformed, does not exist or is not an active organization.</summary>
    public const string FirmInvalid = "ebilling.submission.firm_invalid";

    /// <summary>The caller belongs to several firms and the request named none; the server never picks one.</summary>
    public const string FirmRequired = "ebilling.submission.firm_required";

    /// <summary>The caller's firm memberships could not be read (a fault, never "no firm"): 503, retry.</summary>
    public const string FirmUnverifiable = "ebilling.submission.firm_unverifiable";

    /// <summary>
    /// ebilling-r1 task 051 (D-106, D-110): the chosen firm is not linked to the chosen core record (it is none of the record's assigned law
    /// firms). 403, on every channel; the reason is this code and nothing else, so no record or firm detail leaves. A record whose links could
    /// not be read is NOT this code: it is 503 <see cref="FirmUnverifiable"/>.
    /// </summary>
    public const string FirmNotLinked = "ebilling.submission.firm_not_linked";

    /// <summary>
    /// ebilling-r1 task 051: a workforce systemuser called an EXTERNAL submission route. 403 before any write; internal users submit through
    /// the internal route (as a systemuser is sent to Manage Access on the contact-side grant routes).
    /// </summary>
    public const string UseInternalRoute = "ebilling.submission.use_internal_route";

    /// <summary>ebilling-r1 task 050a (D-134): the existing document named by a confirm does not exist (404). Nothing was written.</summary>
    public const string DocumentNotFound = "ebilling.submission.document_not_found";

    /// <summary>
    /// ebilling-r1 task 050a (D-134): the existing document is already linked to an invoice (<c>sprk_document.sprk_invoice</c>), so a submission
    /// for it would make a second invoice for the same PDF (409). The invoice is never named.
    /// </summary>
    public const string DocumentAlreadyInvoiced = "ebilling.submission.document_already_invoiced";

    /// <summary>
    /// ebilling-r1 task 050a (D-134): the document already has its confirm submission, for another firm, record or firm invoice number (409). A
    /// retry with the same firm and record gets that submission back instead; this one names nothing about it.
    /// </summary>
    public const string DocumentAlreadySubmitted = "ebilling.submission.document_already_submitted";

    /// <summary>ebilling-r1 task 050a (D-134): the existing document has no stored file (no drive or item pointer), so nothing could be read (422).</summary>
    public const string DocumentHasNoFile = "ebilling.submission.document_has_no_file";

    /// <summary>
    /// ebilling-r1 050a review #3: the existing document is filed under a core record (its <c>sprk_matter</c>, <c>sprk_project</c> or
    /// <c>sprk_workassignment</c>) other than the one the confirm names (409). Nothing was written; the other record is never named. An UNFILED
    /// document (no core lookup) may be confirmed (owner 2026-10-20, D-139).
    /// </summary>
    public const string DocumentCoreRecordMismatch = "ebilling.submission.document_core_record_mismatch";

    /// <summary>
    /// ebilling-r1 050a review #4: the document's confirm submission already ended Failed or Rejected, so the same confirm cannot answer with it
    /// (409; the detail names the terminal status). Nothing was written. A new attempt after a terminal status is a later feature (owner
    /// 2026-10-20, D-139: a new submission with a new tracking .n under a generation key).
    /// </summary>
    public const string SubmissionAlreadyEnded = "ebilling.submission.submission_already_ended";

    public const string StorageNotConfigured = "ebilling.submission.storage_not_configured";
    public const string CoreRecordUnavailable = "ebilling.submission.record_unavailable";
    public const string Failed = "ebilling.submission.failed";
}
