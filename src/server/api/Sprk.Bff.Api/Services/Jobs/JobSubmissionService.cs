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
    public async Task SubmitJobAsync(JobContract job, CancellationToken ct = default)
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
    public async Task SubmitCommunicationJobAsync(JobContract job, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(job);

        if (job.JobId == Guid.Empty)
            throw new ArgumentException("Job.JobId cannot be empty", nameof(job));

        if (string.IsNullOrWhiteSpace(job.JobType))
            throw new ArgumentException("Job.JobType cannot be null or empty", nameof(job));

        await SubmitToQueueAsync(job, _communicationQueueName, ct);
    }

    private async Task SubmitToQueueAsync(JobContract job, string queueName, CancellationToken ct)
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

            // Use IdempotencyKey as MessageId when available so that Service Bus
            // duplicate detection rejects messages for the same logical operation
            // (e.g., same email processed by multiple webhook subscriptions or instances).
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
