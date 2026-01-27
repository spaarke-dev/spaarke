using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Background service that processes the office-indexing queue.
/// Wraps IndexingWorker to provide the queue consumer infrastructure.
/// </summary>
/// <remarks>
/// <para>
/// This hosted service:
/// 1. Listens on the office-indexing Service Bus queue
/// 2. Deserializes OfficeJobMessage
/// 3. Marks job as complete (actual indexing is optional per spec)
/// </para>
/// <para>
/// Per spec.md, RAG indexing is optional - if it fails or is skipped, the document
/// remains accessible but won't appear in AI search results.
/// </para>
/// <para>
/// TODO: Integrate with actual IndexingWorker.ProcessAsync() when FileIndexingService
/// is fully configured. For now, this stub completes the job immediately.
/// </para>
/// </remarks>
public class IndexingWorkerHostedService : BackgroundService
{
    private readonly ILogger<IndexingWorkerHostedService> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IJobStatusService _jobStatusService;
    private readonly IDataverseService _dataverseService;
    private readonly ServiceBusOptions _serviceBusOptions;

    private const string QueueName = "office-indexing";
    private const int MaxConcurrentCalls = 5;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public IndexingWorkerHostedService(
        ILogger<IndexingWorkerHostedService> logger,
        ServiceBusClient serviceBusClient,
        IJobStatusService jobStatusService,
        IDataverseService dataverseService,
        IOptions<ServiceBusOptions> serviceBusOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _jobStatusService = jobStatusService ?? throw new ArgumentNullException(nameof(jobStatusService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _serviceBusOptions = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "IndexingWorkerHostedService starting, listening on queue {QueueName}",
            QueueName);

        var processor = _serviceBusClient.CreateProcessor(QueueName, new ServiceBusProcessorOptions
        {
            MaxConcurrentCalls = MaxConcurrentCalls,
            AutoCompleteMessages = false,
            MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10)
        });

        processor.ProcessMessageAsync += ProcessMessageAsync;
        processor.ProcessErrorAsync += ProcessErrorAsync;

        try
        {
            await processor.StartProcessingAsync(stoppingToken);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("IndexingWorkerHostedService stopping gracefully");
        }
        finally
        {
            await processor.StopProcessingAsync();
            await processor.DisposeAsync();
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var messageBody = args.Message.Body.ToString();
        OfficeJobMessage? message = null;

        try
        {
            message = JsonSerializer.Deserialize<OfficeJobMessage>(messageBody, JsonOptions);
            if (message == null)
            {
                _logger.LogError("Failed to deserialize message, dead-lettering");
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "InvalidMessage",
                    deadLetterErrorDescription: "Failed to deserialize message payload");
                return;
            }

            _logger.LogInformation(
                "Processing indexing job {JobId}, attempt {Attempt}/{MaxAttempts}",
                message.JobId,
                message.Attempt,
                message.MaxAttempts);

            // Parse payload to get DocumentId
            var payload = ParsePayload(message.Payload);
            if (payload == null)
            {
                _logger.LogError("Invalid payload for indexing job {JobId}", message.JobId);
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "InvalidPayload",
                    deadLetterErrorDescription: "Failed to deserialize indexing payload");
                return;
            }

            // TODO: Integrate with actual IndexingWorker.ProcessAsync() when ready
            // For now, mark job as complete immediately (indexing is optional per spec)
            _logger.LogInformation(
                "Indexing skipped for job {JobId} (stub implementation), marking complete",
                message.JobId);

            await CompleteJobAsync(message.JobId, payload.DocumentId, args.CancellationToken);

            await args.CompleteMessageAsync(args.Message, args.CancellationToken);

            _logger.LogInformation(
                "Indexing job {JobId} completed (stub - actual indexing not implemented)",
                message.JobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception processing indexing message for job {JobId}",
                message?.JobId);

            await args.DeadLetterMessageAsync(
                args.Message,
                deadLetterReason: "UnhandledException",
                deadLetterErrorDescription: ex.Message);
        }
    }

    private Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        _logger.LogError(
            args.Exception,
            "Service Bus error on queue {QueueName}: {ErrorSource}",
            QueueName,
            args.ErrorSource);

        return Task.CompletedTask;
    }

    private static IndexingPayload? ParsePayload(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<IndexingPayload>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task CompleteJobAsync(Guid jobId, Guid documentId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Completing indexing job {JobId}, document {DocumentId}",
            jobId,
            documentId);

        // Publish job completed via Redis for SSE
        var update = new JobStatusUpdate
        {
            JobId = jobId,
            UpdateType = JobStatusUpdateType.JobCompleted,
            Status = JobStatus.Completed,
            Progress = 100,
            CurrentPhase = "Complete",
            Result = new JobResult
            {
                Artifact = new CreatedArtifact
                {
                    Type = ArtifactType.Document,
                    Id = documentId
                }
            },
            Timestamp = DateTimeOffset.UtcNow
        };

        await _jobStatusService.PublishStatusUpdateAsync(update, cancellationToken);

        // Update Dataverse ProcessingJob record
        try
        {
            var updateRequest = new
            {
                Status = 2, // Completed
                Progress = 100,
                CurrentStage = "Complete",
                CompletedDate = DateTime.UtcNow
            };

            await _dataverseService.UpdateProcessingJobAsync(jobId, updateRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to update ProcessingJob {JobId} in Dataverse, continuing",
                jobId);
        }
    }
}

/// <summary>
/// Payload structure for indexing jobs.
/// </summary>
public record IndexingPayload
{
    public Guid DocumentId { get; init; }
    public bool ProfileSummary { get; init; }
    public bool RagIndex { get; init; }
    public bool DeepAnalysis { get; init; }
}
