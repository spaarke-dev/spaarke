using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Finance.Coding;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The outcome reason codes the InvoiceSubmissionProcessing job (ebilling-r1 task 049) writes to
/// <c>sprk_invoicesubmission.sprk_outcomereasoncode</c>, and the pipeline finding codes it records. One definition: the job, the
/// spend predicate (<see cref="FinanceBaseValues.BlockedReasonCode"/>) and the review flows (104 / 105) read these, never a literal.
/// </summary>
/// <remarks>
/// Codes owned by another component are referenced, not re-spelled: the <c>invoice.read.*</c> codes are 043's
/// (<see cref="InvoiceAiException"/>), the reader's "not configured" code is <see cref="InvoiceAi.ReadNotConfiguredCode"/>, and the
/// coding code is 047's (<see cref="LineCodingException.NoCodeSet"/>). Two kill-switch codes are spelled here because their owners
/// keep them private or outside PublicContracts: <see cref="AiInvoiceDisabled"/> (<c>NullInvoiceAi</c>) and
/// <see cref="AiLinearConsumersDisabled"/> (<c>NullActionResolver</c>); tests pin both to the owners' values.
/// </remarks>
public static class SubmissionReasonCodes
{
    // ── Held (D-101; notes 049 section 2) ──

    /// <summary>
    /// A blocked internal submission: the decider (073) said Rejected on an Internal-channel submission. The invoice is created In
    /// Review and held from approval until Override / Return (105); the submission is Rejected with this code.
    /// </summary>
    public const string Blocked = "ebilling.submission.blocked";

    // ── Decided by a person (ebilling-r1 task 105; D-24, D-73) ──

    /// <summary>
    /// A blocked internal submission a person with the eBilling Override privilege accepted (task 105, <c>SubmissionOverrideService</c>). The
    /// submission is Accepted with this code, so it leaves the held set (<see cref="Blocked"/> no longer matches) and the invoice, already In
    /// Review, is released to approval. The code is how a repeat override is told from a submission that processing accepted.
    /// </summary>
    public const string Overridden = "ebilling.submission.overridden";

    /// <summary>
    /// A blocked internal submission a person returned to the firm (task 105, D-73). The submission stays Rejected with this code and its
    /// invoice is Returned to Firm, so it also leaves the held set.
    /// </summary>
    public const string Returned = "ebilling.submission.returned";

    // ── Failed: no invoice is created ──

    /// <summary>ADR-018: <c>Ebilling:Enabled</c> is false.</summary>
    public const string EbillingDisabled = "ebilling.disabled";

    /// <summary>ADR-032: the AI kill switch (<c>NullInvoiceAi</c>'s <c>FeatureDisabledException.ErrorCode</c>).</summary>
    public const string AiInvoiceDisabled = "ai.invoice.disabled";

    /// <summary>ADR-032: the linear-consumer kill switch, raised by line coding (<c>NullActionResolver.ErrorCode</c>).</summary>
    public const string AiLinearConsumersDisabled = "ai.linear-consumers.disabled";

    /// <summary>043: no Content Understanding endpoint is configured.</summary>
    public const string ReadNotConfigured = InvoiceAi.ReadNotConfiguredCode;

    /// <summary>D-87: the submission carries no tracking number (<c>sprk_submissionsequence</c> / <c>sprk_trackingnumber</c>). The job never allocates one.</summary>
    public const string TrackingMissing = "ebilling.submission.tracking_missing";

    /// <summary>The submission has no <c>createdon</c>, so the date its rules are judged on (072 <c>ReceivedOn</c>) is unknown. Never defaulted.</summary>
    public const string ReceivedOnMissing = "ebilling.submission.received_on_missing";

    /// <summary>The submission names no firm (<c>sprk_vendororg</c>).</summary>
    public const string FirmMissing = "ebilling.submission.firm_missing";

    /// <summary>FR-05: the submission names no core record, or more than one.</summary>
    public const string CoreRecordMissing = "ebilling.submission.core_record_missing";

    /// <summary>D-91: the submission has no typed firm invoice number (intake refuses that, so this is a defect, not a case to repair).</summary>
    public const string FirmInvoiceNumberMissing = InvoiceSubmissionErrors.FirmInvoiceNumberRequired;

    /// <summary>The submission's channel is not SPA or Internal, so a rejection could not be classified as rejected or blocked.</summary>
    public const string ChannelUnsupported = "ebilling.submission.channel_unsupported";

    /// <summary>
    /// The submission names no document (<c>sprk_document</c>). Not written since ebilling-r1 task 092 (an upload names no document: its PDF
    /// is in the file store); kept so a stored value still reads as what it meant.
    /// </summary>
    public const string DocumentMissing = "ebilling.submission.document_missing";

    /// <summary>047: no code set resolved for the submission's core record. Since D-126 not a failure: the cause recorded on
    /// <see cref="CodingNoCodeSetHold"/>.</summary>
    public const string NoCodeSet = LineCodingException.NoCodeSet;

    /// <summary>
    /// Part E: the submission's document row is gone or has no SharePoint Embedded file pointer. Permanent. Since ebilling-r1 task 092 also: the
    /// uploaded PDF staged in the file store is missing or its bytes do not match the staged hash, or the submission names a document that is
    /// not an existing-document confirm (the retired intake shape).
    /// </summary>
    public const string DocumentUnavailable = "ebilling.submission.document_unavailable";

    /// <summary>Part E: the document pointer's container could not be verified (<c>RecordContainerResolver</c>'s check, fail closed). Permanent.</summary>
    public const string DocumentStorageUnverified = "ebilling.submission.document_storage_unverified";

    /// <summary>Part E: a transient fault outlived the job's last delivery and no invoice was committed.</summary>
    public const string ProcessingFailed = "ebilling.submission.processing_failed";

    /// <summary>
    /// ebilling-r1 task 092 (procedure-and-file-model 9.1 steps 5-6): the invoice's file could not be made servable. Before the commit its
    /// staged blob is gone or unusable, so no invoice is created. After the commit the flip of a Stored File row failed definitively (blob
    /// missing, hash mismatch, row not flippable): since task 094 the commit is UNDONE (<see cref="SubmissionCommitGate"/>: the file rows
    /// and blobs, the approval, the lines and the invoice deleted, the commit's columns cleared) and the submission is Failed with this
    /// code, on every channel. Until the undo completes the submission is Failed with its file Not yet servable (held).
    /// </summary>
    public const string FileUnservable = "ebilling.submission.file_unservable";

    /// <summary>
    /// ebilling-r1 task 094 (procedure-and-file-model 9.1 step 5; the code task 095's contract pins): the re-run after the commit found
    /// the Submit Invoice source filed under another core record, its secure roots not covered by the copy's, or it could not be read (any
    /// fault is a failure, uac-r2 condition 1). The commit was undone (<see cref="SubmissionCommitGate"/>) and the submission is Failed
    /// with this code and an administrator note; the communication's filing by step A is kept.
    /// </summary>
    public const string SourceCheckFailed = "ebilling.submission.source_check_failed";

    /// <summary>
    /// ebilling-r1 task 094: a job outcome only, never stored: the undo of a commit could not finish (a delete faulted), so the submission
    /// is Failed (with <see cref="SourceCheckFailed"/> or <see cref="FileUnservable"/>) and its file stays Not yet servable until a later
    /// delivery completes the compensation (uac-r2 condition 3, "complete or held").
    /// </summary>
    public const string UndoHeld = "ebilling.submission.undo_held";

    /// <summary>
    /// ebilling-r1 task 094 (9.1 step 4): the Submit Invoice source changed since the job staged its copy, and it changed again after one
    /// re-stage, or its bytes differ from the bytes the job evaluated. Transient: nothing was committed; a redelivery evaluates the source
    /// as it is then.
    /// </summary>
    public const string SourceChanged = "ebilling.submission.source_changed";

    /// <summary>ebilling-r1 task 092: the uploaded invoice PDF's malware scan said Malicious; it is never read. Permanent; no invoice.</summary>
    public const string FileMalicious = "ebilling.submission.file_malicious";

    // ── Rejected, not read: no invoice (FR-21, D-101) ──

    /// <summary>043: the document could not be opened.</summary>
    public const string ReadUnreadable = InvoiceAiException.ReadUnreadable;

    /// <summary>043: the document has too many pages.</summary>
    public const string ReadPageLimit = InvoiceAiException.ReadPageLimit;

    /// <summary>043: the document holds no invoice.</summary>
    public const string ReadNoInvoice = InvoiceAiException.ReadNoInvoice;

    /// <summary>043: the read service failed. Retryable: the job retries, then marks the submission Failed with this code.</summary>
    public const string ReadServiceFailed = InvoiceAiException.ReadServiceFailed;

    // ── Pipeline findings (hold for review, never reject) ──

    /// <summary>D-91: the firm invoice number read from the PDF differs from the typed one (normalised for case, whitespace and punctuation).</summary>
    public const string InvoiceNumberMismatch = "ebilling.submission.invoice_number_mismatch";

    /// <summary>
    /// D-126: the invoice was read but no code set is configured for its core record (<see cref="NoCodeSet"/>), so its lines are stored
    /// uncoded and it is held for review. Value <c>cause</c> is 047's code. Part D writes it as the SubmissionIntegrity reason
    /// <c>coding.no_code_set</c>.
    /// </summary>
    public const string CodingNoCodeSetHold = "ebilling.submission.coding_no_code_set";

    /// <summary>
    /// D-126: the invoice was read but line coding could not run (a kill switch, e.g. <see cref="AiLinearConsumersDisabled"/>), so its
    /// lines are stored uncoded and it is held for review. Value <c>cause</c> is the switch's code. Part D writes it as the
    /// SubmissionIntegrity reason <c>coding.unavailable</c>.
    /// </summary>
    public const string CodingUnavailableHold = "ebilling.submission.coding_unavailable";

    // ── After-commit steps (part D; notes 049 section 13a) ──

    /// <summary>
    /// The persister's result names a terminal status or reason code other than the one the STORED findings JSON (kind and root reason)
    /// maps to: the caller evaluated again and got a different answer than the one committed. Nothing is written and the stored state wins.
    /// It cannot occur on the handler path (review fix R2: every outcome that finds the submission already linked - Resumed, or a totals Retry
    /// that carries the invoice id - goes to <see cref="IInvoiceSubmissionAfterSteps.ResumeAsync"/>, which reads the stored state alone, and
    /// <c>RunAsync</c> is only called on Committed, a run that committed in this delivery), so it signals a bug; the handler leaves the linked
    /// submission Processing and flags it for the administrator, and nothing marks it Failed.
    /// </summary>
    public const string StoredStateMismatch = "ebilling.submission.stored_state_mismatch";

    /// <summary>
    /// The document-level read failures (FR-21): the submission is Rejected with the code and no invoice is created. Every other read
    /// failure is retried.
    /// </summary>
    public static bool IsDocumentLevelReadFailure(string? reasonCode) =>
        reasonCode is ReadUnreadable or ReadPageLimit or ReadNoInvoice;
}
