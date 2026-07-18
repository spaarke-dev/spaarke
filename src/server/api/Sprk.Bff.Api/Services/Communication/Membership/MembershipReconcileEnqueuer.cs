using System.Text.Json;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Enqueues a <c>MembershipReconcile</c> job on the EXISTING shared <c>sdap-jobs</c> queue (ADR-004). The
/// single entry point for BOTH reconcile triggers (FR-07):
/// <list type="bullet">
/// <item><b>Event-driven</b> — a Dataverse record-access change, a privacy switch (task 042), or a
/// participant edit (task 043) calls <see cref="EnqueueAsync"/> so ACS membership converges quickly.</item>
/// <item><b>Periodic sweep</b> — <see cref="MembershipReconcileSweepService"/> calls it per messaging thread
/// as the eventual-consistency safety net (design §8.4).</item>
/// </list>
/// </summary>
/// <remarks>
/// Best-effort (NFR-02): enqueue failures are logged and swallowed — a failed enqueue never fails the caller's
/// mutation (privacy flip / participant edit / send). The sweep repairs any missed reconcile. The job's
/// idempotency key is <c>membership-reconcile:{threadId}</c> so a burst of triggers for one thread collapses
/// (Service Bus duplicate detection) — safe because each reconcile recomputes the FULL desired set, not a delta.
/// </remarks>
public class MembershipReconcileEnqueuer
{
    private readonly JobSubmissionService _jobSubmission;
    private readonly ILogger<MembershipReconcileEnqueuer> _logger;

    public MembershipReconcileEnqueuer(JobSubmissionService jobSubmission, ILogger<MembershipReconcileEnqueuer> logger)
    {
        _jobSubmission = jobSubmission ?? throw new ArgumentNullException(nameof(jobSubmission));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Enqueues a reconcile for the thread. Virtual so event-driven callers (042/043) can be unit-tested with a
    /// mock. Best-effort — never throws to the caller (NFR-02).
    /// </summary>
    public virtual async Task EnqueueAsync(
        Guid threadId,
        MembershipReconcileTrigger trigger,
        string actor = "system",
        string? correlationId = null,
        CancellationToken ct = default)
    {
        if (threadId == Guid.Empty)
        {
            _logger.LogWarning("MembershipReconcile enqueue skipped: empty thread id (trigger={Trigger}).", trigger);
            return;
        }

        try
        {
            var payload = new MembershipReconcileJobPayload
            {
                ThreadId = threadId,
                Trigger = trigger,
                Actor = string.IsNullOrWhiteSpace(actor) ? "system" : actor,
            };

            var job = new JobContract
            {
                JobType = MembershipReconcileJobPayload.JobType,
                SubjectId = threadId.ToString(),
                CorrelationId = correlationId ?? Guid.NewGuid().ToString(),
                IdempotencyKey = $"membership-reconcile:{threadId}",
                Payload = JsonSerializer.SerializeToDocument(payload),
            };

            await _jobSubmission.SubmitJobAsync(job, ct);

            _logger.LogInformation(
                "Enqueued MembershipReconcile for thread {ThreadId} (trigger={Trigger}, actor={Actor}).",
                threadId, trigger, actor);
        }
        catch (Exception ex)
        {
            // NFR-02: enqueue is best-effort — the sweep repairs a missed reconcile.
            _logger.LogWarning(ex, "Failed to enqueue MembershipReconcile for thread {ThreadId} (trigger={Trigger}).", threadId, trigger);
        }
    }
}
