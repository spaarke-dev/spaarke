using System.Text.Json;
using System.Text.Json.Serialization;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Finance;

// ═══════════════════════════════════════════════════════════════════════════════════════════════
// eBilling notifications (ebilling-r1 task 132; FR-39, FR-35, FR-50, D-45, ADR-004, ADR-047). The job message and the one place that enqueues it.
// ═══════════════════════════════════════════════════════════════════════════════════════════════

/// <summary>The events <see cref="Sprk.Bff.Api.Services.Jobs.Handlers.EbillingNotificationJobHandler"/> handles. Closed.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EbillingNotificationEvent
{
    /// <summary>A submission was rejected (the system's decision). A firm email, the composed draft unchanged (FR-50 "in-session").</summary>
    SubmissionRejected,

    /// <summary>A reviewer returned an invoice (104) or a blocked submission (105). A firm email ONLY with the reviewer-confirmed notice (EB-D-258).</summary>
    ReturnedToFirm,

    /// <summary>A reviewer rejected the whole invoice (104). A firm email ONLY with the reviewer-confirmed notice (EB-D-258).</summary>
    InvoiceRejected,

    /// <summary>
    /// An invoice was approved (103). Enqueued on EVERY approval: whether it carries adjustments is decided once, by the notice composer's
    /// applicability check over the 130 projection (the one definition: a Reduced, Disallowed or invoice-level adjustment item). Without
    /// adjustments the composer says NotApplicable and no email is sent.
    /// </summary>
    ApprovedWithAdjustments,

    /// <summary>A submission was accepted: its invoice is In Review. Every eligible approver gets one in-app notification (FR-35).</summary>
    InvoiceAccepted,
}

/// <summary>Whether a job's subject id is a submission or an invoice.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum EbillingNotificationSubject
{
    Submission,
    Invoice,
}

/// <summary>
/// The job payload. The subject id is also <see cref="JobContract.SubjectId"/>. <paramref name="NoticeSubject"/> and <paramref name="NoticeProse"/> are the
/// reviewer-confirmed notice of a Return or Reject (EB-D-258), both set or both null; they never appear in a log (ADR-015).
/// </summary>
public sealed record EbillingNotificationPayload(
    EbillingNotificationEvent Event,
    EbillingNotificationSubject Subject,
    string? NoticeSubject = null,
    string? NoticeProse = null)
{
    /// <summary>True when the reviewer's confirmed subject and prose are both present.</summary>
    [JsonIgnore]
    public bool HasConfirmedNotice => !string.IsNullOrWhiteSpace(NoticeSubject) && !string.IsNullOrWhiteSpace(NoticeProse);
}

/// <summary>
/// Enqueues the eBilling notification job once a decision is PERSISTED (task 132, ADR-004: one idempotent <c>IJobHandler</c> per (subject id, event kind)). Callers: the
/// approve route (103), the reject and return routes (104) and the submission pipeline's notification seam
/// (<see cref="Submission.EbillingNotificationSink"/>, 049 / 105).
/// </summary>
/// <remarks>
/// <para><b>Never fails the decision.</b> The decision is already persisted; a fault here is a Warning with ids and the exception type, and the
/// call returns. The send is bounded by <see cref="Timeout"/> and is NOT tied to the request's token: a client that disconnects after the decision
/// must not drop the notice.</para>
/// <para><b>Service Bus duplicate detection.</b> The idempotency key (<see cref="IdempotencyKeyFor"/>) is the message id, so a second enqueue of the same
/// (event, subject) inside the duplicate-detection window is dropped by the broker; the handler's at-most-once marker covers redelivery beyond it.</para>
/// <para><b>Notice caps.</b> A confirmed notice longer than the screen's own limits is never queued (a 256 KB message limit must not be reachable by a request
/// body): the job is enqueued without it, which sends no email, and the reason is logged. A notice with only one of subject and prose counts as none.</para>
/// </remarks>
public sealed class EbillingNotificationEnqueuer
{
    /// <summary>The job type on the queue.</summary>
    public const string JobTypeName = "EbillingNotification";

    /// <summary>How long enqueuing may take before it is given up.</summary>
    internal static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    /// <summary>The longest confirmed subject queued: the screen's cap for the words, plus room for the code-written tracking suffix.</summary>
    internal const int MaxQueuedSubjectLength = FirmNoticeProse.MaxSubjectLength + 160;

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly JobSubmissionService _jobs;
    private readonly ILogger<EbillingNotificationEnqueuer> _logger;

    public EbillingNotificationEnqueuer(JobSubmissionService jobs, ILogger<EbillingNotificationEnqueuer> logger)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>The Service Bus message id and the job's idempotency key: one per (event, subject).</summary>
    public static string IdempotencyKeyFor(EbillingNotificationEvent evt, Guid subjectId) => $"ebilling-notify:{evt}:{subjectId:N}";

    /// <summary>The at-most-once marker the handler writes BEFORE it sends: one per (event, subject).</summary>
    public static string MarkerFor(EbillingNotificationEvent evt, Guid subjectId) => $"ebilling-notified:{evt}:{subjectId:N}";

    /// <summary>Parses a payload; null for a missing, malformed or incomplete one.</summary>
    public static EbillingNotificationPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
        {
            return null;
        }

        try
        {
            var parsed = payload.RootElement.Deserialize<EbillingNotificationPayload>(PayloadOptions);
            return parsed is not null
                && Enum.IsDefined(parsed.Event)
                && Enum.IsDefined(parsed.Subject)
                && (parsed.NoticeSubject is null) == (parsed.NoticeProse is null)
                    ? parsed
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Enqueues the job for (<paramref name="evt"/>, <paramref name="subjectId"/>). <paramref name="noticeSubject"/> and <paramref name="noticeProse"/> are
    /// the reviewer-confirmed notice of a Return or Reject (EB-D-258); pass null for every other event. Never throws.
    /// </summary>
    public async Task EnqueueAsync(
        EbillingNotificationEvent evt,
        EbillingNotificationSubject subject,
        Guid subjectId,
        string? noticeSubject = null,
        string? noticeProse = null,
        string? correlationId = null)
    {
        try
        {
            var (confirmedSubject, confirmedProse) = ConfirmedNoticeOf(evt, subjectId, noticeSubject, noticeProse);
            var payload = new EbillingNotificationPayload(evt, subject, confirmedSubject, confirmedProse);
            var job = new JobContract
            {
                JobType = JobTypeName,
                SubjectId = subjectId.ToString("D"),
                CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId,
                IdempotencyKey = IdempotencyKeyFor(evt, subjectId),
                Payload = JsonSerializer.SerializeToDocument(payload, PayloadOptions),
            };

            using var bound = new CancellationTokenSource(Timeout);
            await _jobs.SubmitJobAsync(job, bound.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Includes the time bound: the decision is persisted and must not depend on this.
            _logger.LogWarning(
                "eBilling notification not enqueued | Event {Event}, Subject {SubjectId}, ExceptionType {ExceptionType}; the decision stands",
                evt, subjectId, ex.GetType().Name);
        }
    }

    private (string? Subject, string? Prose) ConfirmedNoticeOf(EbillingNotificationEvent evt, Guid subjectId, string? subject, string? prose)
    {
        var hasSubject = !string.IsNullOrWhiteSpace(subject);
        var hasProse = !string.IsNullOrWhiteSpace(prose);
        if (!hasSubject && !hasProse)
        {
            return (null, null);
        }

        if (evt is not (EbillingNotificationEvent.ReturnedToFirm or EbillingNotificationEvent.InvoiceRejected))
        {
            // A confirmed notice belongs to a Return or Reject only.
            _logger.LogWarning("eBilling notification: a confirmed notice was supplied for {Event} and is dropped | Subject {SubjectId}", evt, subjectId);
            return (null, null);
        }

        if (hasSubject != hasProse)
        {
            _logger.LogWarning(
                "eBilling notification: the confirmed notice is incomplete (subject and prose are both needed); no firm email will be sent | Event {Event}, Subject {SubjectId}",
                evt, subjectId);
            return (null, null);
        }

        if (subject!.Length > MaxQueuedSubjectLength || prose!.Length > FirmNoticeProse.MaxProseLength)
        {
            _logger.LogWarning(
                "eBilling notification: the confirmed notice is longer than a notice may be; no firm email will be sent | Event {Event}, Subject {SubjectId}, Reason {Reason}",
                evt, subjectId, FirmNoticeFallbackReasons.ProseTooLong);
            return (null, null);
        }

        return (subject, prose);
    }
}
