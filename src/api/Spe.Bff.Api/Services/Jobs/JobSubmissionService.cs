using Azure.Messaging.ServiceBus;
using Spe.Bff.Api.Services.BackgroundServices;
using System.Text.Json;

namespace Spe.Bff.Api.Services.Jobs;

/// <summary>
/// Unified job submission service that routes jobs to the appropriate processor.
/// Routes to Service Bus (production) or in-memory queue (development) based on configuration.
/// Implements ADR-004 job contract for all job submissions.
/// </summary>
public class JobSubmissionService
{
    private readonly ServiceBusClient? _serviceBusClient;
    private readonly JobProcessor? _inMemoryProcessor;
    private readonly ILogger<JobSubmissionService> _logger;
    private readonly IConfiguration _configuration;
    private readonly bool _useServiceBus;
    private readonly string _queueName;

    public JobSubmissionService(
        IConfiguration configuration,
        ILogger<JobSubmissionService> logger,
        ServiceBusClient? serviceBusClient = null,
        JobProcessor? inMemoryProcessor = null)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceBusClient = serviceBusClient;
        _inMemoryProcessor = inMemoryProcessor;

        _useServiceBus = configuration.GetValue<bool>("Jobs:UseServiceBus", true);
        _queueName = configuration["Jobs:ServiceBus:QueueName"] ?? "sdap-jobs";

        // Fail-fast validation
        if (_useServiceBus && _serviceBusClient == null)
        {
            throw new InvalidOperationException(
                "Service Bus is enabled (Jobs:UseServiceBus=true) but ServiceBusClient is not registered. " +
                "Configure ServiceBus:ConnectionString or set Jobs:UseServiceBus=false for development.");
        }

        if (!_useServiceBus && _inMemoryProcessor == null)
        {
            throw new InvalidOperationException(
                "In-memory job processing is enabled (Jobs:UseServiceBus=false) but JobProcessor is not registered. " +
                "Register JobProcessor as a singleton or enable Service Bus.");
        }

        _logger.LogInformation("Job submission configured with {Mode} mode (Queue: {Queue})",
            _useServiceBus ? "Service Bus" : "In-Memory",
            _useServiceBus ? _queueName : "ConcurrentQueue");
    }

    /// <summary>
    /// Submits a job for asynchronous processing.
    /// Routes to Service Bus (production) or in-memory queue (development).
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

        if (_useServiceBus)
        {
            await SubmitToServiceBusAsync(job, ct);
        }
        else
        {
            SubmitToInMemoryQueue(job);
        }
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

    private void SubmitToInMemoryQueue(JobContract job)
    {
        try
        {
            _inMemoryProcessor!.EnqueueJob(job);

            _logger.LogInformation(
                "Job {JobId} ({JobType}) submitted to in-memory queue with idempotency key {IdempotencyKey}",
                job.JobId, job.JobType, job.IdempotencyKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to submit job {JobId} ({JobType}) to in-memory queue",
                job.JobId, job.JobType);
            throw;
        }
    }

    /// <summary>
    /// Gets the current queue depth (for monitoring and health checks).
    /// Only works with in-memory queue. Returns null for Service Bus mode.
    /// </summary>
    public int? GetQueueDepth()
    {
        if (!_useServiceBus && _inMemoryProcessor != null)
        {
            return _inMemoryProcessor.QueueDepth;
        }
        return null;
    }

    /// <summary>
    /// Indicates whether Service Bus mode is active.
    /// </summary>
    public bool IsUsingServiceBus => _useServiceBus;
}
