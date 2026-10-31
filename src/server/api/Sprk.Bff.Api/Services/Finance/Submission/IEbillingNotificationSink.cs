namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>The decision of the submission pipeline that a notification reports (ebilling-r1 task 049; the events are task 132's).</summary>
public enum SubmissionDecisionOutcome
{
    /// <summary>Processing accepted the submission: an invoice exists In Review.</summary>
    Accepted,

    /// <summary>Processing rejected the submission: the document was not an invoice that could be read, or the rejection rule rejected it.</summary>
    Rejected,

    /// <summary>A reviewer returned a blocked internal submission's invoice to the firm (task 105 Return; D-143: the firm is notified, FR-39 Return to Firm).</summary>
    Returned,
}

/// <summary>
/// What the pipeline tells the notification seam about one decided submission. Identifiers and stable codes only (ADR-015): no extracted value,
/// narrative or finding text. The receiver reads whatever else it needs (recipients, adjustments) from Dataverse by these ids, so a retry or a
/// late delivery sees current state.
/// </summary>
/// <param name="SubmissionId">The decided <c>sprk_invoicesubmission</c>.</param>
/// <param name="Outcome">Accepted, Rejected or Returned. A Failed submission is not a decision and is not reported here.</param>
/// <param name="Channel">The channel the submission arrived through: who the submitter is (a firm contact on the SPA, an internal user).</param>
/// <param name="InvoiceId">The invoice the pipeline created; null when none exists (a document that could not be read, D-101).</param>
/// <param name="ReasonCode">The stable reason code of a rejection or return (<c>sprk_outcomereasoncode</c>); null for <see cref="SubmissionDecisionOutcome.Accepted"/>.</param>
public sealed record SubmissionDecidedNotification(
    Guid SubmissionId,
    SubmissionDecisionOutcome Outcome,
    InvoiceSubmissionChannel Channel,
    Guid? InvoiceId,
    string? ReasonCode);

/// <summary>
/// The seam between the submission pipeline and eBilling notifications (task 049, Processing Pipeline step 9). The pipeline calls it once
/// per decided submission, after the decision is committed; task 132 replaces the no-op implementation with one that enqueues its
/// notification job (email, SPA feed, approver in-app).
/// </summary>
/// <remarks>
/// <para><b>Contract for implementations.</b> Idempotent per <see cref="SubmissionDecidedNotification.SubmissionId"/> and outcome: the pipeline
/// resumes after a crash and may call again. A failure here must not undo the decision, so the caller logs and does not retry the pipeline
/// for it (the decision is already persisted).</para>
/// </remarks>
public interface IEbillingNotificationSink
{
    /// <summary>Reports an Accepted, Rejected or Returned submission.</summary>
    Task SubmissionDecidedAsync(SubmissionDecidedNotification decision, CancellationToken ct = default);
}

/// <summary>
/// The default <see cref="IEbillingNotificationSink"/> until task 132: logs the decision at Debug (submission id and outcome only, ADR-015)
/// and does nothing else.
/// </summary>
public sealed class NoOpEbillingNotificationSink : IEbillingNotificationSink
{
    private readonly ILogger<NoOpEbillingNotificationSink> _logger;

    public NoOpEbillingNotificationSink(ILogger<NoOpEbillingNotificationSink> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task SubmissionDecidedAsync(SubmissionDecidedNotification decision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);
        _logger.LogDebug(
            "Submission {SubmissionId} decided {Outcome}; no notification sink is configured (task 132)", decision.SubmissionId, decision.Outcome);
        return Task.CompletedTask;
    }
}
