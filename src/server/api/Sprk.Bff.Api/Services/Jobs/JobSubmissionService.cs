using System.Text.Json;
using Azure.Messaging.ServiceBus;

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

    public JobSubmissionService(
        IConfiguration configuration,
        ILogger<JobSubmissionService> logger,
        ServiceBusClient serviceBusClient)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _queueName = configuration["Jobs:ServiceBus:QueueName"] ?? "sdap-jobs";

        _logger.LogInformation("Job submission configured with Service Bus (Queue: {Queue})", _queueName);
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

        await SubmitToServiceBusAsync(job, ct);
    }

    private async Task SubmitToServiceBusAsync(JobContract job, CancellationToken ct)
    {
        try
        {
            var sender = _serviceBusClient!.CreateSender(_queueName);

            // Serialize job to JSON
            var messageBody = JsonSerializer.Serialize(job, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            var message = new ServiceBusMessage(messageBody)
            {
                MessageId = job.JobId.ToString(),
                CorrelationId = job.IdempotencyKey,
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
                job.JobId, job.JobType, _queueName, job.IdempotencyKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit job {JobId} ({JobType}) to Service Bus queue {QueueName}",
                job.JobId, job.JobType, _queueName);
            throw;
        }
    }

}
