using Microsoft.Xrm.Sdk;

namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// Processing Pipeline step 6 (ebilling-r1 task 049 part C; spec FR-20, D-101, D-126): turns a <see cref="SubmissionEvaluationOutcome"/> into
/// the invoice, its lines, the approval portion, the document link and the submission's update, in ONE Dataverse transaction, then
/// recalculates the invoice totals (016). Notes: <c>projects/ebilling-r1/notes/049-submission-processing-job.md</c>, Part C.
/// </summary>
public interface IInvoiceSubmissionPersister
{
    /// <summary>
    /// Persists <paramref name="outcome"/> all-or-nothing. Only <see cref="SubmissionEvaluationKind.Accepted"/>,
    /// <see cref="SubmissionEvaluationKind.AutoRejected"/>, <see cref="SubmissionEvaluationKind.Blocked"/> and
    /// <see cref="SubmissionEvaluationKind.AcceptedUncoded"/> create rows; any other kind is a caller error.
    /// </summary>
    /// <param name="outcome">What steps 1-5 produced for the submission.</param>
    /// <param name="correlationId">The job's correlation id, written to the invoice and its lines; null keeps the submission's own.</param>
    /// <param name="ct">Cancellation propagates unwrapped.</param>
    /// <returns>See <see cref="SubmissionPersistStatus"/>. A submission read fault or cancellation propagates (the job retries).</returns>
    /// <exception cref="ArgumentException"><paramref name="outcome"/> is not a persistable kind, or misses a member its kind requires.</exception>
    Task<SubmissionPersistResult> PersistAsync(SubmissionEvaluationOutcome outcome, string? correlationId, CancellationToken ct = default);
}

/// <summary>What <see cref="IInvoiceSubmissionPersister.PersistAsync"/> did.</summary>
public enum SubmissionPersistStatus
{
    /// <summary>The transaction committed (or a commit of unknown outcome was proved by the submission's link) and the totals ran.</summary>
    Committed,

    /// <summary>
    /// The submission already links an invoice: nothing was written and nothing was recalculated (review fix R2: a totals fault here would
    /// have become a <see cref="Retry"/> that carries the invoice id, which the caller must read as "committed by this delivery"). The ids
    /// are the stored ones. The caller answers it from the STORED state (<see cref="IInvoiceSubmissionAfterSteps.ResumeAsync"/>, which
    /// recalculates the totals itself), never <see cref="IInvoiceSubmissionAfterSteps.RunAsync"/>.
    /// </summary>
    Resumed,

    /// <summary>
    /// The submission's core record differs from the one the outcome was judged under (072 part L): nothing was written. The caller
    /// re-runs the processor on <see cref="SubmissionPersistResult.CurrentSubmission"/>.
    /// </summary>
    ReEvaluate,

    /// <summary>
    /// A transient fault. When <see cref="SubmissionPersistResult.InvoiceId"/> is null nothing was persisted; when set, the transaction
    /// committed (in THIS call; a submission found already linked is <see cref="Resumed"/>, never a Retry) and only the after-commit totals
    /// failed. Either way the submission is left as it is for the job to retry, and a retry
    /// never creates a second invoice (the link is the commit marker, and the ids are deterministic).
    /// </summary>
    Retry,

    /// <summary>
    /// A retry will not change the result (<see cref="SubmissionPersistResult.ReasonCode"/>): the job marks the submission Failed. Nothing was
    /// written.
    /// </summary>
    Refused,

    /// <summary>The submission row no longer exists (044 K4): nothing was written.</summary>
    SubmissionMissing,

    /// <summary>
    /// ebilling-r1 task 094 (procedure-and-file-model 9.1 step 4): the Submit Invoice source changed since the job staged its copy (its live
    /// row version is not the staged one, or the commit's guard on it failed). Nothing was committed and the staged copy was deleted; the
    /// caller stages the new version and persists once more (<see cref="SubmissionEvaluationOutcome.StagedFiles"/>).
    /// </summary>
    SourceChanged,

    /// <summary>
    /// ebilling-r1 task 094 (9.1 step 5): the transaction committed but the commit gate (<see cref="SubmissionCommitGate"/>) failed, so the
    /// commit was undone and the submission is Failed with <see cref="SubmissionPersistResult.ReasonCode"/>
    /// (<see cref="SubmissionReasonCodes.SourceCheckFailed"/> or <see cref="SubmissionReasonCodes.FileUnservable"/>). No totals and no
    /// after-step ran. <see cref="SubmissionPersistResult.UndoHeld"/> says a delete of the undo faulted (a later delivery finishes it).
    /// </summary>
    Undone,
}

/// <summary>The outcome of persisting one submission (task 049 part C), as part D consumes it.</summary>
public sealed record SubmissionPersistResult
{
    public required SubmissionPersistStatus Status { get; init; }

    public required Guid SubmissionId { get; init; }

    /// <summary>The invoice created (or found linked); null when nothing was committed.</summary>
    public Guid? InvoiceId { get; init; }

    /// <summary>Each line's row id keyed by line index (the 043 list position, <see cref="Signals.Billing.BillingSubject.LineIndex"/>).</summary>
    public IReadOnlyDictionary<int, Guid> LineIds { get; init; } = new Dictionary<int, Guid>();

    /// <summary>The approval portion created (Accepted, Blocked, AcceptedUncoded); null otherwise and on a resume.</summary>
    public Guid? ApprovalId { get; init; }

    /// <summary>
    /// D-130 (a): the status part D writes to <c>sprk_processingstatus</c> as the LAST step, after Signals, the Decision Record and the
    /// notification. The transaction leaves the submission Processing, so "Accepted / Rejected" means complete. Set on
    /// <see cref="SubmissionPersistStatus.Committed"/> and <see cref="SubmissionPersistStatus.Resumed"/> (and on a totals
    /// <see cref="SubmissionPersistStatus.Retry"/>, which keeps the invoice id); null when nothing was committed. Blocked is Rejected with
    /// <see cref="TerminalReasonCode"/> = <see cref="SubmissionReasonCodes.Blocked"/>.
    /// </summary>
    public InvoiceSubmissionStatus? TerminalStatus { get; init; }

    /// <summary>
    /// The <c>sprk_outcomereasoncode</c> that goes with <see cref="TerminalStatus"/>: null for Accepted; the coding code for an uncoded
    /// Accepted (D-130 b); the first deciding finding's code for AutoRejected; <see cref="SubmissionReasonCodes.Blocked"/> for Blocked.
    /// The transaction already wrote it; part D writes it again with the status so the pair is consistent.
    /// </summary>
    public string? TerminalReasonCode { get; init; }

    /// <summary>A stable code for <see cref="SubmissionPersistStatus.Retry"/> and <see cref="SubmissionPersistStatus.Refused"/>.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>
    /// The 016 recalculation after the commit. A refused calculation (<see cref="InvoiceTotalsCalculation.IsRefused"/>) leaves the totals
    /// unset and does not undo the invoice (map row 6c); part D records it in the findings.
    /// </summary>
    public InvoiceTotalsCalculation? Totals { get; init; }

    /// <summary>The submission as re-read, for <see cref="SubmissionPersistStatus.ReEvaluate"/> (pass it to the processor).</summary>
    public Entity? CurrentSubmission { get; init; }

    /// <summary>Task 094: on <see cref="SubmissionPersistStatus.Undone"/>, true when the undo is held (not every row of the commit is gone yet).</summary>
    public bool UndoHeld { get; init; }
}
