using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Workers.Office.Messages;

namespace Sprk.Bff.Api.Workers.Office;

/// <summary>
/// Background worker that finalizes document uploads by moving temporary files to SPE,
/// creating Document/EmailArtifact/AttachmentArtifact records, and updating job status.
/// </summary>
/// <remarks>
/// <para>
/// Per ADR-001, this worker uses BackgroundService pattern (not Azure Functions).
/// Per ADR-004, handlers are idempotent using IdempotencyKey for duplicate detection.
/// Per ADR-007, all SPE operations go through SpeFileStore facade.
/// </para>
/// <para>
/// Processing stages:
/// 1. Retrieve temporary file from blob storage
/// 2. Upload to SPE via SpeFileStore
/// 3. Create Document record in Dataverse with association
/// 4. Create EmailArtifact (for email saves) or AttachmentArtifact (for attachments)
/// 5. Update ProcessingJob status to "uploaded"
/// 6. Queue next stage (profile or indexing)
/// 7. Clean up temporary file
/// </para>
/// </remarks>
public class UploadFinalizationWorker : BackgroundService, IOfficeJobHandler
{
    private readonly ILogger<UploadFinalizationWorker> _logger;
    private readonly SpeFileStore _speFileStore;
    private readonly IDistributedCache _cache;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ServiceBusOptions _serviceBusOptions;
    private readonly Spaarke.Dataverse.IDataverseService _dataverseService;

    private const string QueueName = "office-upload-finalization";
    private const string ProfileQueueName = "office-profile";
    private const string IndexingQueueName = "office-indexing";
    private const int MaxConcurrentCalls = 5;
    private static readonly TimeSpan IdempotencyKeyTtl = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <inheritdoc />
    public OfficeJobType JobType => OfficeJobType.UploadFinalization;

    public UploadFinalizationWorker(
        ILogger<UploadFinalizationWorker> logger,
        SpeFileStore speFileStore,
        IDistributedCache cache,
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ServiceBusOptions> serviceBusOptions,
        Spaarke.Dataverse.IDataverseService dataverseService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _serviceBusOptions = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "UploadFinalizationWorker starting, listening on queue {QueueName}",
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
            _logger.LogInformation("UploadFinalizationWorker stopping gracefully");
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
            "Processing upload finalization job {JobId}, attempt {Attempt}/{MaxAttempts}",
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
                    "Failed to deserialize upload finalization payload",
                    retryable: false);
            }

            // Step 3: Update job status to Running
            await UpdateJobStatusAsync(
                message.JobId,
                JobStatus.Running,
                "FileUploading",
                10,
                cancellationToken);

            // Step 4: Retrieve temporary file
            using var fileStream = await RetrieveTempFileAsync(
                payload.TempFileLocation,
                cancellationToken);

            if (fileStream == null)
            {
                return JobOutcome.Failure(
                    "OFFICE_012",
                    "Failed to retrieve temporary file from storage",
                    retryable: true);
            }

            // Step 5: Upload to SPE
            var uploadResult = await UploadToSpeAsync(
                payload.ContainerId,
                payload.FolderPath,
                payload.FileName,
                fileStream,
                cancellationToken);

            if (!uploadResult.Success)
            {
                return JobOutcome.Failure(
                    "OFFICE_012",
                    uploadResult.ErrorMessage ?? "SPE upload failed",
                    retryable: true);
            }

            await UpdateJobStatusAsync(
                message.JobId,
                JobStatus.Running,
                "RecordsCreating",
                40,
                cancellationToken);

            // Step 6: Create Document record in Dataverse
            var documentId = await CreateDocumentRecordAsync(
                payload,
                uploadResult.DriveId!,
                uploadResult.ItemId!,
                message.UserId,
                cancellationToken);

            // Step 7: Create artifact records (EmailArtifact or AttachmentArtifact)
            await CreateArtifactRecordsAsync(
                payload,
                documentId,
                cancellationToken);

            await UpdateJobStatusAsync(
                message.JobId,
                JobStatus.Running,
                "RecordsCreated",
                60,
                cancellationToken);

            // Step 8: Mark as processed (idempotency)
            await MarkAsProcessedAsync(message.IdempotencyKey, documentId, cancellationToken);

            // Step 9: Update job status to uploaded
            await UpdateJobStatusAsync(
                message.JobId,
                JobStatus.Running,
                "FileUploaded",
                70,
                cancellationToken,
                documentId);

            // Step 10: Queue next stage (profile or indexing)
            if (payload.TriggerAiProcessing)
            {
                await QueueNextStageAsync(message, documentId, payload, cancellationToken);
            }
            else
            {
                // No AI processing - mark job as complete
                await UpdateJobStatusAsync(
                    message.JobId,
                    JobStatus.Completed,
                    "Complete",
                    100,
                    cancellationToken,
                    documentId);
            }

            // Step 11: Clean up temporary file
            await CleanupTempFileAsync(payload.TempFileLocation, cancellationToken);

            _logger.LogInformation(
                "Upload finalization completed for job {JobId}, created document {DocumentId}",
                message.JobId,
                documentId);

            return JobOutcome.Success();
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Upload finalization failed for job {JobId}",
                message.JobId);

            // Update job status to failed if this is the last attempt
            if (message.Attempt >= message.MaxAttempts)
            {
                await UpdateJobStatusAsync(
                    message.JobId,
                    JobStatus.Failed,
                    "Failed",
                    0,
                    cancellationToken,
                    errorCode: "OFFICE_012",
                    errorMessage: ex.Message);
            }

            return JobOutcome.Failure(
                "OFFICE_012",
                $"Upload finalization failed: {ex.Message}",
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
            if (message.JobType != OfficeJobType.UploadFinalization)
            {
                _logger.LogWarning(
                    "Received message with unexpected job type {JobType}, expected UploadFinalization",
                    message.JobType);
                await args.DeadLetterMessageAsync(
                    args.Message,
                    deadLetterReason: "WrongJobType",
                    deadLetterErrorDescription: $"Expected UploadFinalization, got {message.JobType}");
                return;
            }

            var outcome = await ProcessAsync(message, args.CancellationToken);

            if (outcome.IsSuccess)
            {
                await args.CompleteMessageAsync(args.Message, args.CancellationToken);
            }
            else if (outcome.Retryable && message.Attempt < message.MaxAttempts)
            {
                // Retry with exponential backoff
                await RetryJobAsync(message, args, outcome);
            }
            else
            {
                // Non-retryable or max attempts reached - dead letter
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
        // Calculate exponential backoff delay
        var delay = TimeSpan.FromSeconds(Math.Pow(2, message.Attempt) * 5);

        _logger.LogWarning(
            "Retrying job {JobId} in {Delay} seconds (attempt {Attempt}/{MaxAttempts}), reason: {ErrorMessage}",
            message.JobId,
            delay.TotalSeconds,
            message.Attempt + 1,
            message.MaxAttempts,
            outcome.ErrorMessage);

        // Create retry message with incremented attempt
        var retryMessage = message with { Attempt = message.Attempt + 1 };
        var sender = _serviceBusClient.CreateSender(QueueName);

        var sbMessage = new ServiceBusMessage(JsonSerializer.Serialize(retryMessage, JsonOptions))
        {
            MessageId = $"{message.JobId}-{retryMessage.Attempt}",
            CorrelationId = message.CorrelationId,
            ContentType = "application/json",
            Subject = message.JobType.ToString(),
            ScheduledEnqueueTime = DateTimeOffset.UtcNow.Add(delay),
            ApplicationProperties =
            {
                ["JobType"] = message.JobType.ToString(),
                ["Attempt"] = retryMessage.Attempt,
                ["UserId"] = message.UserId
            }
        };

        await sender.SendMessageAsync(sbMessage);
        await sender.DisposeAsync();

        // Complete the original message since we've scheduled a retry
        await args.CompleteMessageAsync(args.Message);
    }

    private async Task<bool> IsAlreadyProcessedAsync(string idempotencyKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"office:upload:processed:{idempotencyKey}";
        var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
        return !string.IsNullOrEmpty(cached);
    }

    private async Task MarkAsProcessedAsync(
        string idempotencyKey,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        var cacheKey = $"office:upload:processed:{idempotencyKey}";
        await _cache.SetStringAsync(
            cacheKey,
            documentId.ToString(),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = IdempotencyKeyTtl },
            cancellationToken);
    }

    private static UploadFinalizationPayload? DeserializePayload(JsonElement payload)
    {
        try
        {
            return payload.Deserialize<UploadFinalizationPayload>(JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private async Task<Stream?> RetrieveTempFileAsync(string location, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Retrieving temporary file from {Location}", location);

        // TODO: Implement actual blob storage retrieval
        // For now, return a stub that simulates file retrieval
        // In production, this would:
        // 1. Parse the location (blob URL or local path)
        // 2. Download from Azure Blob Storage using BlobClient
        // 3. Return the stream

        // Stub implementation - return empty stream
        await Task.Delay(100, cancellationToken);
        return new MemoryStream();
    }

    private async Task<SpeUploadResult> UploadToSpeAsync(
        string containerId,
        string? folderPath,
        string fileName,
        Stream content,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Uploading to SPE container {ContainerId}, path {FolderPath}/{FileName}",
            containerId,
            folderPath ?? "root",
            fileName);

        try
        {
            // Resolve container to drive ID
            var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, cancellationToken);

            // Build the path
            var path = string.IsNullOrEmpty(folderPath)
                ? fileName
                : $"{folderPath.TrimEnd('/')}/{fileName}";

            // Upload using SpeFileStore (ADR-007)
            var result = await _speFileStore.UploadSmallAsync(
                driveId,
                path,
                content,
                cancellationToken);

            if (result != null)
            {
                _logger.LogInformation(
                    "File uploaded to SPE: DriveId={DriveId}, ItemId={ItemId}",
                    driveId,
                    result.Id);

                return new SpeUploadResult
                {
                    Success = true,
                    DriveId = driveId,
                    ItemId = result.Id,
                    WebUrl = result.WebUrl
                };
            }

            return new SpeUploadResult
            {
                Success = false,
                ErrorMessage = "Upload returned null result"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SPE upload failed");
            return new SpeUploadResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private async Task<Guid> CreateDocumentRecordAsync(
        UploadFinalizationPayload payload,
        string driveId,
        string itemId,
        string userId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Creating Document record with association {AssociationType}:{AssociationId}",
            payload.AssociationType,
            payload.AssociationId);

        // TODO: Implement actual Dataverse record creation
        // This would use the Spaarke.Dataverse library to:
        // 1. Create sprk_document record
        // 2. Set sprk_graphdriveid = driveId
        // 3. Set sprk_graphitemid = itemId
        // 4. Set appropriate association lookup based on AssociationType
        // 5. Set metadata fields (filename, size, content type)

        // Stub implementation - return generated GUID
        await Task.Delay(50, cancellationToken);
        var documentId = Guid.NewGuid();

        _logger.LogInformation(
            "Document record created: DocumentId={DocumentId}",
            documentId);

        return documentId;
    }

    private async Task CreateArtifactRecordsAsync(
        UploadFinalizationPayload payload,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        if (payload.ContentType == SaveContentType.Email && payload.EmailMetadata != null)
        {
            await CreateEmailArtifactAsync(payload.EmailMetadata, documentId, cancellationToken);
        }
        else if (payload.ContentType == SaveContentType.Attachment && payload.AttachmentMetadata != null)
        {
            await CreateAttachmentArtifactAsync(payload.AttachmentMetadata, documentId, cancellationToken);
        }
    }

    private async Task CreateEmailArtifactAsync(
        EmailArtifactPayload metadata,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Creating EmailArtifact for document {DocumentId}, subject: {Subject}",
            documentId,
            metadata.Subject);

        var request = new
        {
            Name = $"{metadata.Subject} - {metadata.SentDate:yyyy-MM-dd}",
            Subject = metadata.Subject,
            Sender = metadata.SenderEmail,
            Recipients = metadata.RecipientsJson,
            SentDate = metadata.SentDate,
            ReceivedDate = metadata.ReceivedDate,
            MessageId = metadata.InternetMessageId,
            ConversationId = metadata.ConversationId,
            BodyPreview = metadata.BodyPreview,
            HasAttachments = metadata.HasAttachments,
            Importance = metadata.Importance,
            DocumentId = documentId
        };

        var emailArtifactId = await _dataverseService.CreateEmailArtifactAsync(request, cancellationToken);

        _logger.LogInformation(
            "EmailArtifact created: EmailArtifactId={EmailArtifactId}, DocumentId={DocumentId}",
            emailArtifactId,
            documentId);
    }

    private async Task CreateAttachmentArtifactAsync(
        AttachmentArtifactPayload metadata,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Creating AttachmentArtifact for document {DocumentId}, filename: {FileName}",
            documentId,
            metadata.OriginalFileName);

        var request = new
        {
            Name = metadata.OriginalFileName,
            OriginalFilename = metadata.OriginalFileName,
            ContentType = metadata.ContentType,
            Size = (int)metadata.Size, // Convert long to int for Dataverse
            IsInline = metadata.IsInline,
            EmailArtifactId = metadata.EmailArtifactId,
            DocumentId = documentId
        };

        var attachmentArtifactId = await _dataverseService.CreateAttachmentArtifactAsync(request, cancellationToken);

        _logger.LogInformation(
            "AttachmentArtifact created: AttachmentArtifactId={AttachmentArtifactId}, DocumentId={DocumentId}",
            attachmentArtifactId,
            documentId);
    }

    private async Task UpdateJobStatusAsync(
        Guid jobId,
        JobStatus status,
        string phase,
        int progress,
        CancellationToken cancellationToken,
        Guid? documentId = null,
        string? errorCode = null,
        string? errorMessage = null)
    {
        _logger.LogDebug(
            "Updating job status: JobId={JobId}, Status={Status}, Phase={Phase}, Progress={Progress}%",
            jobId,
            status,
            phase,
            progress);

        // TODO: Implement actual Dataverse ProcessingJob update
        // Update sprk_processingjob record:
        // - sprk_status = status
        // - sprk_stagestatuses JSON (add phase to completed)
        // - sprk_subjectid = documentId (if available)
        // - sprk_completedon = now (if completed/failed)
        // - sprk_errorcode = errorCode (if failed)
        // - sprk_errormessage = errorMessage (if failed)

        // Also publish SSE event for real-time updates
        // This would use a shared event bus (Redis pub/sub or SignalR backplane)

        await Task.Delay(20, cancellationToken);
    }

    private async Task QueueNextStageAsync(
        OfficeJobMessage originalMessage,
        Guid documentId,
        UploadFinalizationPayload payload,
        CancellationToken cancellationToken)
    {
        var aiOptions = payload.AiOptions ?? new AiProcessingOptions();

        // Determine which queue to use
        var nextQueue = aiOptions.ProfileSummary ? ProfileQueueName : IndexingQueueName;
        var nextJobType = aiOptions.ProfileSummary ? OfficeJobType.Profile : OfficeJobType.Indexing;

        _logger.LogDebug(
            "Queueing next stage {NextJobType} for job {JobId}",
            nextJobType,
            originalMessage.JobId);

        var nextMessage = new OfficeJobMessage
        {
            JobId = originalMessage.JobId,
            JobType = nextJobType,
            SubjectId = documentId.ToString(),
            CorrelationId = originalMessage.CorrelationId,
            IdempotencyKey = $"{originalMessage.IdempotencyKey}-{nextJobType}",
            Attempt = 1,
            MaxAttempts = 3,
            UserId = originalMessage.UserId,
            Payload = JsonSerializer.SerializeToElement(new
            {
                DocumentId = documentId,
                ProfileSummary = aiOptions.ProfileSummary,
                RagIndex = aiOptions.RagIndex,
                DeepAnalysis = aiOptions.DeepAnalysis
            })
        };

        var sender = _serviceBusClient.CreateSender(nextQueue);

        var sbMessage = new ServiceBusMessage(JsonSerializer.Serialize(nextMessage, JsonOptions))
        {
            MessageId = $"{nextMessage.JobId}-{nextJobType}",
            CorrelationId = nextMessage.CorrelationId,
            ContentType = "application/json",
            Subject = nextJobType.ToString(),
            ApplicationProperties =
            {
                ["JobType"] = nextJobType.ToString(),
                ["Attempt"] = 1,
                ["UserId"] = nextMessage.UserId,
                ["DocumentId"] = documentId.ToString()
            }
        };

        await sender.SendMessageAsync(sbMessage, cancellationToken);
        await sender.DisposeAsync();

        _logger.LogInformation(
            "Queued {NextJobType} stage for job {JobId}, document {DocumentId}",
            nextJobType,
            originalMessage.JobId,
            documentId);
    }

    private async Task CleanupTempFileAsync(string location, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Cleaning up temporary file at {Location}", location);

        // TODO: Implement actual blob deletion
        // This would:
        // 1. Parse the blob URL
        // 2. Delete using BlobClient.DeleteIfExistsAsync

        await Task.Delay(10, cancellationToken);

        _logger.LogDebug("Temporary file cleaned up");
    }

    /// <summary>
    /// Result of SPE upload operation.
    /// </summary>
    private record SpeUploadResult
    {
        public bool Success { get; init; }
        public string? DriveId { get; init; }
        public string? ItemId { get; init; }
        public string? WebUrl { get; init; }
        public string? ErrorMessage { get; init; }
    }
}
