using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Workers.Office.Messages;

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
    private readonly IFileIndexingService _fileIndexingService;
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
        IFileIndexingService fileIndexingService,
        IOptions<ServiceBusOptions> serviceBusOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _jobStatusService = jobStatusService ?? throw new ArgumentNullException(nameof(jobStatusService));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _fileIndexingService = fileIndexingService ?? throw new ArgumentNullException(nameof(fileIndexingService));
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

            // Parse payload to get DocumentId and file details
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

            // Check if indexing is actually enabled
            if (!payload.RagIndex)
            {
                _logger.LogInformation(
                    "RAG indexing disabled for job {JobId}, skipping and completing",
                    message.JobId);
                await CompleteJobAsync(message.JobId, payload.DocumentId, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                return;
            }

            _logger.LogInformation(
                "Starting RAG indexing for job {JobId}, document {DocumentId}",
                message.JobId,
                payload.DocumentId);

            try
            {
                // Build FileIndexRequest using the payload details
                var indexRequest = new FileIndexRequest
                {
                    DriveId = payload.DriveId,
                    ItemId = payload.ItemId,
                    FileName = payload.FileName,
                    TenantId = payload.TenantId,
                    DocumentId = payload.DocumentId.ToString(),
                    Metadata = new Dictionary<string, string>
                    {
                        ["source"] = "OfficeAddIn",
                        ["jobId"] = message.JobId.ToString()
                    }
                };

                // Call FileIndexingService using app-only authentication
                var result = await _fileIndexingService.IndexFileAppOnlyAsync(indexRequest, args.CancellationToken);

                if (!result.Success)
                {
                    // Per spec, indexing failures are not fatal - document remains accessible
                    _logger.LogWarning(
                        "RAG indexing failed for job {JobId} (non-fatal): {Error}",
                        message.JobId,
                        result.ErrorMessage);

                    // Still complete the job - indexing failure doesn't fail the entire save
                    await CompleteJobAsync(message.JobId, payload.DocumentId, args.CancellationToken);
                    await args.CompleteMessageAsync(args.Message, args.CancellationToken);
                    return;
                }

                _logger.LogInformation(
                    "RAG indexing succeeded for job {JobId}: {ChunksIndexed} chunks indexed in {Duration}",
                    message.JobId,
                    result.ChunksIndexed,
                    result.Duration);

                await CompleteJobAsync(message.JobId, payload.DocumentId, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            catch (Exception ex)
            {
                // Per spec, indexing is optional - log error but don't fail the job
                _logger.LogError(
                    ex,
                    "Unhandled exception during indexing for job {JobId} (non-fatal)",
                    message.JobId);

                // Complete the job even though indexing failed
                await CompleteJobAsync(message.JobId, payload.DocumentId, args.CancellationToken);
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
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
    public string DriveId { get; init; } = string.Empty;
    public string ItemId { get; init; } = string.Empty;
    public string FileName { get; init; } = string.Empty;
    public string TenantId { get; init; } = string.Empty;
    public bool ProfileSummary { get; init; }
    public bool RagIndex { get; init; }
    public bool DeepAnalysis { get; init; }
}
