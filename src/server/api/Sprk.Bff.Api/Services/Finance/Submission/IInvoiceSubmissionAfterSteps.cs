namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The after-commit steps of the Processing Pipeline (ebilling-r1 task 049 part D; map rows 7, 7a, 8, 9; D-122, D-125, D-130 a): the
/// Signals, the automatic rejection's Decision Record, the indexing job, the notification and, LAST, the submission's terminal status.
/// Notes: <c>projects/ebilling-r1/notes/049-submission-processing-job.md</c>, section 13.
/// </summary>
public interface IInvoiceSubmissionAfterSteps
{
    /// <summary>
    /// Runs the after-commit steps for a persisted submission, from its STORED state (<c>sprk_findingsjson</c>), so the first run and a
    /// resume behave the same and nothing re-runs AI. Every step is idempotent: a re-delivery after a failure at any step completes the
    /// rest without a second Signal episode, Decision Record or terminal write of a different value; the notification is guarded by a
    /// marker (at-least-once only in the window between the sink returning and the marker being set, or when the cache fails open).
    /// </summary>
    /// <param name="persisted">The persister's result: <see cref="SubmissionPersistStatus.Committed"/>, <see cref="SubmissionPersistStatus.Resumed"/>,
    /// or a totals <see cref="SubmissionPersistStatus.Retry"/> that carries an invoice id (then only the Signals and the Decision Record
    /// run, and the result is a Retry so the redelivery re-runs the totals).</param>
    /// <param name="correlationId">The job's correlation id (indexing job); null uses the submission's.</param>
    /// <param name="ct">Cancellation propagates.</param>
    /// <returns>See <see cref="SubmissionAfterStepsStatus"/>. Only <see cref="SubmissionAfterStepsStatus.Completed"/> wrote the terminal status.</returns>
    /// <exception cref="ArgumentException"><paramref name="persisted"/> carries no committed invoice.</exception>
    Task<SubmissionAfterStepsResult> RunAsync(SubmissionPersistResult persisted, string? correlationId, CancellationToken ct = default);

    /// <summary>
    /// Record-before-mutate (074 K-3; notes section 7 "Preflight before the transaction"): for an
    /// <see cref="SubmissionEvaluationKind.AutoRejected"/> outcome, builds the automatic rejection's Decision Record exactly as
    /// <see cref="RunAsync"/> will (from the findings JSON the persister will store) and runs the recorder's I/O-free checks. Part E calls
    /// it BEFORE <see cref="IInvoiceSubmissionPersister.PersistAsync"/>, so a decision the recorder would refuse never leaves a Rejected
    /// invoice without its record. Does nothing for any other kind. Reads and writes nothing.
    /// </summary>
    /// <exception cref="Signals.DecisionRecordRefusedException">The record would be refused.</exception>
    void PreflightRejection(SubmissionEvaluationOutcome outcome);
}

/// <summary>What <see cref="IInvoiceSubmissionAfterSteps.RunAsync"/> did.</summary>
public enum SubmissionAfterStepsStatus
{
    /// <summary>Every step ran and the terminal status was written last (D-130 a): the submission is Accepted or Rejected, i.e. complete.</summary>
    Completed,

    /// <summary>
    /// A step did not complete (a Signal write the writer skipped, an open Signal that changed while it was being superseded, a transient
    /// Decision Record refusal, an indexing enqueue fault, or the persister's totals Retry; a notification fault is logged and does not
    /// retry, per <see cref="IEbillingNotificationSink"/>'s contract). The submission stays Processing with
    /// its invoice link, so a re-delivery resumes here (part E's step 0). <see cref="SubmissionAfterStepsResult.ReasonCode"/> says which.
    /// </summary>
    Retry,

    /// <summary>A retry cannot succeed (the stored findings are unreadable or do not match the link, or the recorder refused the decision
    /// for its content). Nothing after the failing step ran; part E marks the submission Failed with the code.</summary>
    Refused,

    /// <summary>The submission row no longer exists (044 K4): nothing was written.</summary>
    SubmissionMissing,
}

/// <summary>The outcome of the after-commit steps for one submission.</summary>
public sealed record SubmissionAfterStepsResult
{
    public required SubmissionAfterStepsStatus Status { get; init; }

    public required Guid SubmissionId { get; init; }

    /// <summary>The linked invoice.</summary>
    public Guid? InvoiceId { get; init; }

    /// <summary>A stable code for <see cref="SubmissionAfterStepsStatus.Retry"/> and <see cref="SubmissionAfterStepsStatus.Refused"/>.</summary>
    public string? ReasonCode { get; init; }

    /// <summary>The terminal status written (only when <see cref="SubmissionAfterStepsStatus.Completed"/>).</summary>
    public InvoiceSubmissionStatus? TerminalStatus { get; init; }

    /// <summary>The automatic rejection's Decision Record (written now or found from an earlier run); null for every other kind.</summary>
    public Guid? DecisionRecordId { get; init; }

    /// <summary>The Signal requests sent to the writer this run (created or refreshed), skips of resolved Signals excluded.</summary>
    public int SignalsWritten { get; init; }
}
