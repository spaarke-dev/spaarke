using System.Diagnostics;
using System.Text.Json;
using Sprk.Bff.Api.Services.Communication.Acs;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Communication.Membership;

/// <summary>
/// Job handler that reconciles ACS thread membership to the Dataverse-derived authorized set
/// (messaging-communication-app-r1 task 041 / FR-07). Registered as an <see cref="IJobHandler"/> on the
/// EXISTING shared <c>sdap-jobs</c> queue (ADR-004 / ADR-036) — it reuses that contract's idempotency,
/// retry-with-backoff, and dead-letter machinery rather than standing up a second pipeline (root §10).
/// </summary>
/// <remarks>
/// <para><b>Non-fatal (NFR-02).</b> Reconcile is background work: this handler runs off the queue, never
/// inline in the send/capture path. A transient failure returns <see cref="JobStatus.Failed"/> (retried);
/// a malformed payload or repeated failure returns <see cref="JobStatus.Poisoned"/> so the processor
/// dead-letters it (never silently dropped). Either way, a reconcile failure cannot fail a send or a capture;
/// the periodic sweep repairs residual drift.</para>
/// <para><b>Idempotent.</b> Re-running on an already-consistent thread computes empty add/remove sets and is a
/// no-op — so at-least-once redelivery + the sweep overlapping an event-driven run are both safe.</para>
/// </remarks>
public sealed class MembershipReconcileJob : IJobHandler
{
    private readonly IMembershipReconciler _reconciler;
    private readonly ILogger<MembershipReconcileJob> _logger;

    public MembershipReconcileJob(IMembershipReconciler reconciler, ILogger<MembershipReconcileJob> logger)
    {
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => MembershipReconcileJobPayload.JobType;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = Stopwatch.StartNew();

        MembershipReconcileJobPayload? payload;
        try
        {
            payload = ParsePayload(job.Payload);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Invalid MembershipReconcile payload for job {JobId} — dead-lettering.", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType,
                "Invalid job payload: could not deserialize MembershipReconcileJobPayload", job.Attempt, stopwatch.Elapsed);
        }

        if (payload is null || payload.ThreadId == Guid.Empty)
        {
            _logger.LogError("Null/empty MembershipReconcile payload for job {JobId} — dead-lettering.", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType,
                "Invalid job payload: null or empty ThreadId", job.Attempt, stopwatch.Elapsed);
        }

        var correlationId = string.IsNullOrEmpty(job.CorrelationId) ? job.JobId.ToString() : job.CorrelationId;

        try
        {
            var result = await _reconciler.ReconcileAsync(
                payload.ThreadId, payload.Trigger, payload.Actor, correlationId, ct);

            _logger.LogInformation(
                "MembershipReconcile job {JobId} thread {ThreadId} done (added={Added}, removed={Removed}, noAcsThread={NoAcs}) in {Duration}ms.",
                job.JobId, payload.ThreadId, result.Added, result.Removed, result.SkippedNoAcsThread, stopwatch.ElapsedMilliseconds);

            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "MembershipReconcile job {JobId} failed on attempt {Attempt} for thread {ThreadId}: {Error}",
                job.JobId, job.Attempt, payload.ThreadId, ex.Message);

            // Retry transient failures (ACS 429 / network / timeout / Dataverse transient) until max attempts,
            // then poison so the processor dead-letters it. The sweep repairs drift regardless (NFR-02).
            return (IsRetryable(ex) && !job.IsAtMaxAttempts)
                ? JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed)
                : JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private static MembershipReconcileJobPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload is null)
            return null;

        return JsonSerializer.Deserialize<MembershipReconcileJobPayload>(
            payload, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    private static bool IsRetryable(Exception ex)
    {
        if (ex is AcsRateLimitException or HttpRequestException or TaskCanceledException or TimeoutException)
            return true;

        var typeName = ex.GetType().Name;
        if (typeName.Contains("ServiceBus", StringComparison.OrdinalIgnoreCase))
            return true;

        var message = ex.Message;
        return message.Contains("429", StringComparison.OrdinalIgnoreCase)
            || message.Contains("503", StringComparison.OrdinalIgnoreCase)
            || message.Contains("504", StringComparison.OrdinalIgnoreCase)
            || message.Contains("timeout", StringComparison.OrdinalIgnoreCase);
    }
}
