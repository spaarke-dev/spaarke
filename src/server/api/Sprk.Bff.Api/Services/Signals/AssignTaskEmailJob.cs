using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Signals;

/// <summary>
/// The ADR-004 <see cref="IJobHandler"/> for Assign Task's system-mailbox email (task 168, D-194): the fallback
/// <see cref="AssignTaskNotifier.NotifyAsync"/> queues when the assigner's own send fails or times out. It sends from
/// <c>Communication:DefaultMailbox</c> through <see cref="AssignTaskNotifier.SendFromSystemMailboxAsync"/>.
/// </summary>
/// <remarks>
/// <para><b>Once per task.</b> Three layers, strongest last: an idempotency entry under the task's key (set after a send), a processing
/// lock owned by this job id (a second copy is refused while the first runs; a redelivery of this job takes over its own dead lock), and
/// the durable check in the notifier (an email whose <c>sprk_communication</c> carries the task's correlation id is not sent again). The
/// cache is check-then-set and fails open (#984), which is why the last layer is a Dataverse read.</para>
/// <para><b>Outcomes.</b> Sent, already sent and nothing to send (the task or contact is gone, or the contact has no address) are
/// <c>Success</c>. An unreadable payload is <c>Poisoned</c>. Any other fault (Graph, Dataverse, an unapproved or unset default mailbox)
/// is <c>Failure</c>: redelivered, then dead-lettered by <c>ServiceBusJobProcessor</c>.</para>
/// <para>The payload and every log line hold ids only. Placement (ADR-004, ADR-052): a queue consumer on the existing main-queue seam,
/// the <see cref="GateDecisionRecordJobHandler"/> shape. §11: see <see cref="AssignTaskNotifier"/>.</para>
/// </remarks>
public sealed class AssignTaskEmailJobHandler : IJobHandler
{
    /// <summary>How long a job holds the task's processing lock.</summary>
    internal static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(5);

    /// <summary>How long the "sent" entry is kept (beyond the broker's redelivery window).</summary>
    internal static readonly TimeSpan SentRetention = TimeSpan.FromDays(7);

    private static readonly JsonSerializerOptions PayloadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly AssignTaskNotifier _notifier;
    private readonly IIdempotencyService _idempotency;
    private readonly ILogger<AssignTaskEmailJobHandler> _logger;

    public AssignTaskEmailJobHandler(AssignTaskNotifier notifier, IIdempotencyService idempotency, ILogger<AssignTaskEmailJobHandler> logger)
    {
        _notifier = notifier ?? throw new ArgumentNullException(nameof(notifier));
        _idempotency = idempotency ?? throw new ArgumentNullException(nameof(idempotency));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => AssignTaskNotifier.JobTypeName;

    /// <summary>The idempotency key of one task's email.</summary>
    internal static string KeyFor(Guid taskId) => $"assign-task-email:{taskId:N}";

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(job);
        var stopwatch = Stopwatch.StartNew();

        if (!TryReadPayload(job, out var payload))
        {
            _logger.LogError(AssignTaskNotifierEvents.SystemEmailJob,
                "[ASSIGN-TASK-NOTIFY] job {JobId} has an unreadable payload; it is poisoned (a retry cannot fix it).", job.JobId);
            return JobOutcome.Poisoned(job.JobId, JobType, "Invalid assign-task email payload", job.Attempt, stopwatch.Elapsed);
        }

        var key = KeyFor(payload.TaskId);
        if (await _idempotency.IsEventProcessedAsync(key, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(AssignTaskNotifierEvents.SystemEmailJob,
                "[ASSIGN-TASK-NOTIFY] job {JobId}: the email for task {TaskId} was already sent.", job.JobId, payload.TaskId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        var owner = job.JobId.ToString("N", CultureInfo.InvariantCulture);
        if (!await _idempotency.TryAcquireProcessingLockAsync(key, owner, LockDuration, ct).ConfigureAwait(false))
        {
            _logger.LogInformation(AssignTaskNotifierEvents.SystemEmailJob,
                "[ASSIGN-TASK-NOTIFY] job {JobId} not run: another job holds the email for task {TaskId}.", job.JobId, payload.TaskId);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }

        try
        {
            var outcome = await _notifier.SendFromSystemMailboxAsync(payload, ct).ConfigureAwait(false);
            if (outcome is AssignTaskSystemEmailOutcome.Sent or AssignTaskSystemEmailOutcome.AlreadySent)
                await _idempotency.MarkEventAsProcessedAsync(key, SentRetention, CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation(AssignTaskNotifierEvents.SystemEmailJob,
                "[ASSIGN-TASK-NOTIFY] job {JobId} done: task {TaskId}, contact {ContactId}: {Outcome}.",
                job.JobId, payload.TaskId, payload.AssigneeContactId, outcome);
            return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            _logger.LogWarning(AssignTaskNotifierEvents.SystemEmailJob, ex,
                "[ASSIGN-TASK-NOTIFY] job {JobId}: the system-mailbox email for task {TaskId} failed ({ExceptionType}); it will be " +
                "redelivered (attempt {Attempt}, delivery {Delivery}).",
                job.JobId, payload.TaskId, ex.GetType().Name, job.Attempt, job.DeliveryCount);
            return JobOutcome.Failure(job.JobId, JobType, ex.GetType().Name, job.Attempt, stopwatch.Elapsed);
        }
        finally
        {
            // Not with ct: a cancelled token never reaches the cache (task 068, #1086).
            await _idempotency.ReleaseProcessingLockAsync(key, owner, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static bool TryReadPayload(JobContract job, out AssignTaskEmailPayload payload)
    {
        payload = null!;
        if (job.Payload is null) return false;

        AssignTaskEmailPayload? read;
        try
        {
            read = job.Payload.Deserialize<AssignTaskEmailPayload>(PayloadOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        if (read is null || read.TaskId == Guid.Empty || read.AssigneeContactId == Guid.Empty)
            return false;

        payload = read;
        return true;
    }
}
