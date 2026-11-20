namespace Sprk.Bff.Api.Services.Finance.Submission;

/// <summary>
/// The real <see cref="IEbillingNotificationSink"/> (ebilling-r1 task 132; replaces <see cref="NoOpEbillingNotificationSink"/>'s registration): it turns the
/// pipeline's decided-submission report into the notification job, once the decision is committed (049, 105).
/// </summary>
/// <remarks>
/// <para><b>Mapping.</b> Accepted becomes <see cref="EbillingNotificationEvent.InvoiceAccepted"/> (approvers, FR-35; subject: the invoice).
/// Rejected becomes <see cref="EbillingNotificationEvent.SubmissionRejected"/> (subject: the submission), EXCEPT a blocked internal submission
/// (<see cref="SubmissionReasonCodes.Blocked"/>): the pipeline reports it as Rejected, but it is HELD for review, not an outcome the firm is told
/// (its way out is 105's Override or Return). Returned becomes <see cref="EbillingNotificationEvent.ReturnedToFirm"/> (subject: the invoice) with NO
/// confirmed notice: 105's Return request carries none, so by EB-D-258 no firm email is sent for it (the job logs ids and says so); the firm still sees
/// the outcome in the SPA feed.</para>
/// <para><b>Contract</b> (<see cref="IEbillingNotificationSink"/>): never throws for a failure to enqueue (the enqueuer swallows and logs it), so the
/// decision stands and the pipeline is not retried; idempotent per (event, subject), by the job's idempotency key and marker. Ids and codes only in the log.</para>
/// </remarks>
public sealed class EbillingNotificationSink : IEbillingNotificationSink
{
    private readonly EbillingNotificationEnqueuer _enqueuer;
    private readonly ILogger<EbillingNotificationSink> _logger;

    public EbillingNotificationSink(EbillingNotificationEnqueuer enqueuer, ILogger<EbillingNotificationSink> logger)
    {
        _enqueuer = enqueuer ?? throw new ArgumentNullException(nameof(enqueuer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task SubmissionDecidedAsync(SubmissionDecidedNotification decision, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(decision);

        switch (decision.Outcome)
        {
            case SubmissionDecisionOutcome.Accepted:
                if (decision.InvoiceId is not { } acceptedInvoice || acceptedInvoice == Guid.Empty)
                {
                    _logger.LogWarning("Submission {SubmissionId} was accepted but names no invoice; the approvers are not notified", decision.SubmissionId);
                    return;
                }

                await _enqueuer.EnqueueAsync(
                    EbillingNotificationEvent.InvoiceAccepted, EbillingNotificationSubject.Invoice, acceptedInvoice).ConfigureAwait(false);
                return;

            case SubmissionDecisionOutcome.Rejected:
                if (string.Equals(decision.ReasonCode, SubmissionReasonCodes.Blocked, StringComparison.Ordinal))
                {
                    _logger.LogInformation("Submission {SubmissionId} is held for review, not rejected; the firm is not notified", decision.SubmissionId);
                    return;
                }

                await _enqueuer.EnqueueAsync(
                    EbillingNotificationEvent.SubmissionRejected, EbillingNotificationSubject.Submission, decision.SubmissionId).ConfigureAwait(false);
                return;

            case SubmissionDecisionOutcome.Returned:
                if (decision.InvoiceId is not { } returnedInvoice || returnedInvoice == Guid.Empty)
                {
                    _logger.LogWarning("Submission {SubmissionId} was returned but names no invoice; no notification job is enqueued", decision.SubmissionId);
                    return;
                }

                await _enqueuer.EnqueueAsync(
                    EbillingNotificationEvent.ReturnedToFirm, EbillingNotificationSubject.Invoice, returnedInvoice).ConfigureAwait(false);
                return;

            default:
                _logger.LogWarning("Submission {SubmissionId}: unknown decision outcome {Outcome}; no notification job is enqueued", decision.SubmissionId, decision.Outcome);
                return;
        }
    }
}
