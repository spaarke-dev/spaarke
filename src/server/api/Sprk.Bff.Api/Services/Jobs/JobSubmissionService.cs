using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;

namespace Sprk.Bff.Api.Services.Jobs;

/// <summary>
/// Service for submitting background jobs to Azure Service Bus.
/// Implements ADR-004 job contract for all job submissions.
/// </summary>
public class JobSubmissionService
{
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ILogger<JobSubmissionService> _logger;
    private readonly string _queueName;
    private readonly string _communicationQueueName;

    public JobSubmissionService(
        IOptions<ServiceBusOptions> serviceBusOptions,
        ILogger<JobSubmissionService> logger,
        ServiceBusClient serviceBusClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));

        var options = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _queueName = options.QueueName;
        _communicationQueueName = options.CommunicationQueueName;

        _logger.LogInformation(
            "Job submission configured with Service Bus (Queue: {Queue}, CommunicationQueue: {CommQueue})",
            _queueName, _communicationQueueName);
    }

    /// <summary>
    /// Submits a job for asynchronous processing via Azure Service Bus.
    /// </summary>
    /// <param name="job">The job to submit (must follow ADR-004 contract)</param>
    /// <param name="ct">Cancellation token</param>
    /// <remarks>
    /// Virtual to enable unit-test overriding via <c>Mock&lt;JobSubmissionService&gt;</c>.
    /// </remarks>
    public virtual async Task SubmitJobAsync(JobContract job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.JobId == Guid.Empty)
        {
            throw new ArgumentException("Job.JobId cannot be empty", nameof(job));
        }

        if (string.IsNullOrWhiteSpace(job.JobType))
        {
            throw new ArgumentException("Job.JobType cannot be null or empty", nameof(job));
        }

        await SubmitToQueueAsync(job, _queueName, ct);
    }

    /// <summary>
    /// Submits a communication job to the dedicated communication queue.
    /// Isolates email processing from the shared job queue to prevent cross-domain failures.
    /// </summary>
    /// <remarks>
    /// Virtual to enable unit-test overriding via <c>Mock&lt;JobSubmissionService&gt;</c>
    /// (mirrors <see cref="SubmitJobAsync"/>).
    /// </remarks>
    public virtual async Task SubmitCommunicationJobAsync(JobContract job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.JobId == Guid.Empty)
            throw new ArgumentException("Job.JobId cannot be empty", nameof(job));

        if (string.IsNullOrWhiteSpace(job.JobType))
            throw new ArgumentException("Job.JobType cannot be null or empty", nameof(job));

        await SubmitToQueueAsync(job, _communicationQueueName, ct);
    }

    /// <summary>
    /// Sends a job to a queue OTHER than the BFF's own, named by the caller: the intake engine's request queue
    /// (spaarke-intake-pipeline-r1 task 014, ADR-004 A2). The envelope is the same Job Contract JSON; the queue name is a setting
    /// (<c>IntakePipeline:Engine:RequestQueueName</c>), not a <see cref="ServiceBusOptions"/> field, because the queue exists only
    /// on stamps that deploy the engine.
    /// </summary>
    /// <remarks>Virtual to enable unit-test overriding via <c>Mock&lt;JobSubmissionService&gt;</c>.</remarks>
    public virtual async Task SubmitToNamedQueueAsync(JobContract job, string queueName, CancellationToken ct = default)
    {
        ValidateJob(job);
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);

        await SubmitToQueueAsync(job, queueName, ct);
    }

    /// <summary>
    /// Schedules a job on the BFF's own jobs queue to become available at <paramref name="enqueueAt"/> (a Service Bus scheduled
    /// message). The intake engine's hard timeout is one such message (task 014, ADR-052: a scheduled message handled by an
    /// job handler, no timer service). Same envelope and MessageId rule as <see cref="SubmitJobAsync"/>.
    /// </summary>
    /// <remarks>Virtual to enable unit-test overriding via <c>Mock&lt;JobSubmissionService&gt;</c>.</remarks>
    public virtual async Task ScheduleJobAsync(JobContract job, DateTimeOffset enqueueAt, CancellationToken ct = default)
    {
        ValidateJob(job);

        await SubmitToQueueAsync(job, _queueName, ct, enqueueAt);
    }

    private static void ValidateJob(JobContract job)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.JobId == Guid.Empty)
        {
            throw new ArgumentException("Job.JobId cannot be empty", nameof(job));
        }

        if (string.IsNullOrWhiteSpace(job.JobType))
        {
            throw new ArgumentException("Job.JobType cannot be null or empty", nameof(job));
        }
    }

    private async Task SubmitToQueueAsync(JobContract job, string queueName, CancellationToken ct, DateTimeOffset? enqueueAt = null)
    {
        try
        {
            var sender = _serviceBusClient!.CreateSender(queueName);

            // Serialize job to JSON
            var messageBody = JsonSerializer.Serialize(job, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            // Use IdempotencyKey as MessageId when available (ADR-004 A1 §5). Duplicate detection is OFF on the
            // BFF queues (service-bus.bicep; a create-time property), so the broker does NOT de-duplicate on it:
            // receive-side idempotency in each handler is the guarantee. The processors' retry copy reuses this
            // MessageId (ISS-025), which depends on duplicate detection staying off — with it on, the broker would
            // silently drop the copy inside the detection window.
            // Enforced by: JobQueueDuplicateDetectionOffGuardTests (tests/Spaarke.ArchTests; scans the bicep queue resources).
            // Falls back to JobId for jobs without an idempotency key.
            // Service Bus MessageId has a 128-character limit; hash if needed.
            var sbMessageId = !string.IsNullOrWhiteSpace(job.IdempotencyKey)
                ? ToSafeMessageId(job.IdempotencyKey)
                : job.JobId.ToString();

            var message = new ServiceBusMessage(messageBody)
            {
                MessageId = sbMessageId,
                CorrelationId = job.CorrelationId,
                ContentType = "application/json",
                Subject = job.JobType,
                ApplicationProperties =
                {
                    ["JobType"] = job.JobType,
                    ["Attempt"] = job.Attempt,
                    ["CreatedAt"] = job.CreatedAt.ToString("o")
                }
            };

            if (enqueueAt is { } due)
            {
                await sender.ScheduleMessageAsync(message, due, ct);
                _logger.LogInformation(
                    "Job {JobId} ({JobType}) scheduled on Service Bus queue {QueueName} for {EnqueueAt:o} with idempotency key {IdempotencyKey}",
                    job.JobId, job.JobType, queueName, due, job.IdempotencyKey);
                return;
            }

            await sender.SendMessageAsync(message, ct);

            _logger.LogInformation(
                "Job {JobId} ({JobType}) submitted to Service Bus queue {QueueName} with idempotency key {IdempotencyKey}",
                job.JobId, job.JobType, queueName, job.IdempotencyKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit job {JobId} ({JobType}) to Service Bus queue {QueueName}",
                job.JobId, job.JobType, queueName);
            throw;
        }
    }

    /// <summary>
    /// Converts an idempotency key to a safe Service Bus MessageId (max 128 chars).
    /// If the key fits, use it directly. Otherwise, SHA-256 hash it.
    /// </summary>
    private static string ToSafeMessageId(string idempotencyKey)
    {
        if (idempotencyKey.Length <= 128)
            return idempotencyKey;

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
        return Convert.ToHexString(hash); // 64-char hex string
    }
}
