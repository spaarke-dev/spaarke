using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Background worker that generates AI profile summaries for uploaded documents.
/// Handles the Profile stage of the Office document processing pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-001, this worker uses BackgroundService pattern (not Azure Functions).
/// Per ADR-004, handlers are idempotent using IdempotencyKey for duplicate detection.
/// </para>
/// <para>
/// Processing stages:
/// 1. Receive profile job message from office-profile queue
/// 2. Generate AI summary using existing AI services (TODO: implement)
/// 3. Update Document record with profile summary
/// 4. Queue indexing stage if RagIndex is enabled
/// 5. Update ProcessingJob status to completed
/// </para>
/// <para>
/// Current implementation is a stub that just completes the job.
/// AI profile generation will be added in a future task.
/// </para>
/// </remarks>
public class ProfileSummaryWorker : BackgroundService, IOfficeJobHandler
{
    private readonly ILogger<ProfileSummaryWorker> _logger;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IDistributedCache _cache;
    private readonly IDataverseService _dataverseService;
    private readonly IJobStatusService _jobStatusService;
    private readonly IAppOnlyAnalysisService _analysisService;
    private readonly ServiceBusOptions _serviceBusOptions;

    private const string QueueName = "office-profile";
    private const string IndexingQueueName = "office-indexing";
    private const int MaxConcurrentCalls = 5;
    private static readonly TimeSpan IdempotencyKeyTtl = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public OfficeJobType JobType => OfficeJobType.Profile;

    public ProfileSummaryWorker(
        ILogger<ProfileSummaryWorker> logger,
        ServiceBusClient serviceBusClient,
        IDistributedCache cache,
        IDataverseService dataverseService,
        IJobStatusService jobStatusService,
        IAppOnlyAnalysisService analysisService,
        IOptions<ServiceBusOptions> serviceBusOptions)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _jobStatusService = jobStatusService ?? throw new ArgumentNullException(nameof(jobStatusService));
        _analysisService = analysisService ?? throw new ArgumentNullException(nameof(analysisService));
        _serviceBusOptions = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ProfileSummaryWorker starting, listening on queue {QueueName}",
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
            _logger.LogInformation("ProfileSummaryWorker stopping gracefully");
        }
        finally
        {
            await processor.StopProcessingAsync();
            await processor.DisposeAsync();
        }
    }

    /// <inheritdoc />
    public async Task<JobOutcome> ProcessAsync(OfficeJobMessage message, CancellationToken cancellationToken)
    {
        using var scope = _logger.BeginScope(new Dictionary<string, object>
        {
            ["JobId"] = message.JobId,
            ["CorrelationId"] = message.CorrelationId,
            ["UserId"] = message.UserId
        });

        _logger.LogInformation(
            "Processing profile summary job {JobId}, attempt {Attempt}/{MaxAttempts}",
            message.JobId,
            message.Attempt,
            message.MaxAttempts);

        try
        {
            // Step 1: Check idempotency
            if (await IsAlreadyProcessedAsync(message.IdempotencyKey, cancellationToken))
            {
                _logger.LogInformation(
                    "Job {JobId} already processed (idempotency key: {IdempotencyKey}), skipping",
                    message.JobId,
                    message.IdempotencyKey);
                return JobOutcome.Success();
            }

            // Step 2: Deserialize payload
            var payload = DeserializePayload(message.Payload);
            if (payload == null)
            {
                return JobOutcome.Failure(
                    "OFFICE_INTERNAL",
                    "Failed to deserialize profile job payload",
                    retryable: false);
            }

            // Step 3: Update job status to profile processing
            await UpdateJobStatusAsync(
                message.JobId,
                JobStatus.Running,
                "ProfileGenerating",
                75,
                cancellationToken);

            // Step 4: Generate AI profile summary using IAppOnlyAnalysisService
            try
            {
                _logger.LogInformation(
                    "Starting AI profile generation for job {JobId}, document {DocumentId}",
                    message.JobId,
                    payload.DocumentId);

                // Call the analysis service with the "Document Profile" playbook
                var analysisResult = await _analysisService.AnalyzeDocumentAsync(
                    payload.DocumentId,
                    IAppOnlyAnalysisService.DefaultPlaybookName,
                    cancellationToken);

                if (!analysisResult.Success)
                {
                    // Per spec, AI processing failures are not fatal
                    _logger.LogWarning(
                        "AI profile generation failed for job {JobId} (non-fatal): {Error}",
                        message.JobId,
                        analysisResult.ErrorMessage);
                }
                else
                {
                    _logger.LogInformation(
                        "AI profile generated successfully for job {JobId}, document {DocumentId}",
                        message.JobId,
                        payload.DocumentId);
                }
            }
            catch (Exception ex)
            {
                // Per spec, AI processing is optional - log error but don't fail the job
                _logger.LogWarning(
                    ex,
                    "Exception during AI profile generation for job {JobId} (non-fatal)",
                    message.JobId);
            }

            // Step 5: Mark as processed (idempotency)
            await MarkAsProcessedAsync(message.IdempotencyKey, payload.DocumentId, cancellationToken);

            // Step 6: Check if RAG indexing is requested
            if (payload.RagIndex)
            {
                // Queue indexing stage
                await QueueIndexingAsync(message, payload, cancellationToken);

                await UpdateJobStatusAsync(
                    message.JobId,
                    JobStatus.Running,
                    "IndexingQueued",
                    85,
                    cancellationToken);
            }
            else
            {
                // No indexing needed - mark job as complete
                await CompleteJobAsync(message.JobId, payload.DocumentId, cancellationToken);
            }

            _logger.LogInformation(
                "Profile summary job completed for job {JobId}, document {DocumentId}",
                message.JobId,
                payload.DocumentId);

            return JobOutcome.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Profile summary job failed for job {JobId}",
                message.JobId);

            if (message.Attempt >= message.MaxAttempts)
            {
                await UpdateJobStatusAsync(
                    message.JobId,
                    JobStatus.Failed,
                    "Failed",
                    0,
                    cancellationToken,
                    errorCode: "OFFICE_013",
                    errorMessage: ex.Message);
            }

            return JobOutcome.Failure(
                "OFFICE_013",
                $"Profile summary failed: {ex.Message}",
                retryable: message.Attempt < message.MaxAttempts);
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

            // Verify this is the correct job type
            if (message.JobType != OfficeJobType.Profile)
            {
                _logger.LogWarning(
                    "Received message with unexpected job type {JobType}, expected Profile",
                    message.JobType);
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "WrongJobType",
                    deadLetterErrorDescription: $"Expected Profile, got {message.JobType}");
                return;
            }

            var outcome = await ProcessAsync(message, args.CancellationToken);

            if (outcome.IsSuccess)
            {
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            else if (outcome.Retryable && message.Attempt < message.MaxAttempts)
            {
                await RetryJobAsync(message, args, outcome);
            }
            else
            {
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: outcome.ErrorCode ?? "ProcessingFailed",
                    deadLetterErrorDescription: outcome.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Unhandled exception processing message for job {JobId}",
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

    private async Task RetryJobAsync(
        OfficeJobMessage message,
        ProcessMessageEventArgs args,
        JobOutcome outcome)
    {
        var delay = TimeSpan.FromSeconds(Math.Pow(2, message.Attempt) * 5);

        _logger.LogWarning(
            "Retrying job {JobId} in {Delay} seconds (attempt {Attempt}/{MaxAttempts}), reason: {ErrorMessage}",
            message.JobId,
            delay.TotalSeconds,
            message.Attempt + 1,
            message.MaxAttempts,
            outcome.ErrorMessage);

        var retryMessage = message with { Attempt = message.Attempt + 1 };
        var sender = _serviceBusClient.CreateSender(QueueName);

        var sbMessage = new ServiceBusMessage(JsonSerializer.Serialize(retryMessage, JsonOptions))
        {
            MessageId = $"{message.JobId}-{retryMessage.Attempt}",
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            Subject = message.JobType.ToString(),
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.Add(delay)
        };

        await sender.SendMessageAsync(sbMessage);
        await sender.DisposeAsync();
        await args.CompleteMessageAsync(args.Message);
    }

    private async Task<bool> IsAlreadyProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"office:profile:processed:{idempotencyKey}";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        return !string.IsNullOrEmpty(cached);
    }

    private async Task MarkAsProcessedAsync(
        string idempotencyKey,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"office:profile:processed:{idempotencyKey}";
        await _cache.SetStringAsync(
            cacheKey,
            documentId.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = IdempotencyKeyTtl },
            cancellationToken);
    }

    private static ProfileJobPayload? DeserializePayload(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<ProfileJobPayload>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task UpdateJobStatusAsync(
        Guid jobId,
        JobStatus status,
        string phase,
        int progress,
        CancellationToken cancellationToken,
        string? errorCode = null,
        string? errorMessage = null)
    {
        _logger.LogDebug(
            "Updating job status: JobId={JobId}, Status={Status}, Phase={Phase}, Progress={Progress}%",
            jobId,
            status,
            phase,
            progress);

        try
        {
            // Publish to Redis for SSE subscribers
            var updateType = status switch
            {
                JobStatus.Completed => JobStatusUpdateType.JobCompleted,
                JobStatus.Failed => JobStatusUpdateType.JobFailed,
                _ => JobStatusUpdateType.Progress
            };

            var update = new JobStatusUpdate
            {
                JobId = jobId,
                UpdateType = updateType,
                Status = status,
                Progress = progress,
                CurrentPhase = phase,
                Timestamp = DateTimeOffset.UtcNow
            };

            if (!string.IsNullOrEmpty(errorMessage))
            {
                update = update with
                {
                    Error = new JobError
                    {
                        Code = errorCode ?? "OFFICE_013",
                        Message = errorMessage
                    }
                };
            }

            await _jobStatusService.PublishStatusUpdateAsync(update, cancellationToken);

            // Also update Dataverse ProcessingJob record
            var dataverseStatus = status switch
            {
                JobStatus.Queued => 0,
                JobStatus.Running => 1,
                JobStatus.Completed => 2,
                JobStatus.Failed => 3,
                JobStatus.Cancelled => 4,
                _ => 1
            };

            var updateRequest = new
            {
                Status = dataverseStatus,
                Progress = progress,
                CurrentStage = phase,
                ErrorCode = errorCode,
                ErrorMessage = errorMessage,
                CompletedDate = status is JobStatus.Completed or JobStatus.Failed
                    ? DateTime.UtcNow
                    : (DateTime?)null
            };

            await _dataverseService.UpdateProcessingJobAsync(jobId, updateRequest, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to update job status for job {JobId}, continuing",
                jobId);
        }
    }

    private async Task CompleteJobAsync(Guid jobId, Guid documentId, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Completing job {JobId}, document {DocumentId}",
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

        // Update Dataverse
        var updateRequest = new
        {
            Status = 2, // Completed
            Progress = 100,
            CurrentStage = "Complete",
            CompletedDate = DateTime.UtcNow
        };

        await _dataverseService.UpdateProcessingJobAsync(jobId, updateRequest, cancellationToken);
    }

    private async Task QueueIndexingAsync(
        OfficeJobMessage originalMessage,
        ProfileJobPayload payload,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Queueing indexing stage for job {JobId}",
            originalMessage.JobId);

        var nextMessage = new OfficeJobMessage
        {
            JobId = originalMessage.JobId,
            JobType = OfficeJobType.Indexing,
            SubjectId = payload.DocumentId.ToString(),
            CorrelationId = originalMessage.CorrelationId,
            IdempotencyKey = $"{originalMessage.IdempotencyKey}-Indexing",
            Attempt = 1,
            MaxAttempts = 3,
            UserId = originalMessage.UserId,
            Payload = JsonSerializer.SerializeToElement(new
            {
                DocumentId = payload.DocumentId,
                payload.DriveId,
                payload.ItemId,
                payload.FileName,
                payload.TenantId,
                payload.ProfileSummary,
                payload.RagIndex,
                payload.DeepAnalysis
            })
        };

        var sender = _serviceBusClient.CreateSender(IndexingQueueName);

        var sbMessage = new ServiceBusMessage(JsonSerializer.Serialize(nextMessage, JsonOptions))
        {
            MessageId = $"{nextMessage.JobId}-Indexing",
            CorrelationId = nextMessage.CorrelationId,
            ContentType = "application/json",
            Subject = OfficeJobType.Indexing.ToString(),
            ApplicationProperties =
            {
                ["JobType"] = OfficeJobType.Indexing.ToString(),
                ["Attempt"] = 1,
                ["UserId"] = nextMessage.UserId,
                ["DocumentId"] = payload.DocumentId.ToString()
            }
        };

        await sender.SendMessageAsync(sbMessage, cancellationToken);
        await sender.DisposeAsync();

        _logger.LogInformation(
            "Queued indexing stage for job {JobId}, document {DocumentId}",
            originalMessage.JobId,
            payload.DocumentId);
    }
}

/// <summary>
/// Payload structure for profile summary jobs.
/// </summary>
public record ProfileJobPayload
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
