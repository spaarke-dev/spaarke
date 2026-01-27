using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Workers.Office.Messages;
using Spaarke.Dataverse;

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
    private readonly GraphOptions _graphOptions;
    private readonly IDataverseService _dataverseService;
    private readonly IEmailToEmlConverter _emlConverter;
    private readonly AttachmentFilterService _attachmentFilterService;

    private const string QueueName = "office-upload-finalization";
    private const string ProfileQueueName = "office-profile";
    private const string IndexingQueueName = "office-indexing";
    private const int MaxConcurrentCalls = 5;
    private static readonly TimeSpan IdempotencyKeyTtl = TimeSpan.FromDays(7);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    // Document type choice values (match EmailToDocumentJobHandler)
    private const int DocumentTypeEmailAttachment = 100000007;
    private const int RelationshipTypeEmailAttachment = 100000000;
    private const int SourceTypeEmailAttachment = 659490004;

    /// <inheritdoc />
    public OfficeJobType JobType => OfficeJobType.UploadFinalization;

    public UploadFinalizationWorker(
        ILogger<UploadFinalizationWorker> logger,
        SpeFileStore speFileStore,
        IDistributedCache cache,
        ServiceBusClient serviceBusClient,
        IServiceScopeFactory scopeFactory,
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<GraphOptions> graphOptions,
        IDataverseService dataverseService,
        IEmailToEmlConverter emlConverter,
        AttachmentFilterService attachmentFilterService)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _serviceBusClient = serviceBusClient ?? throw new ArgumentNullException(nameof(serviceBusClient));
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _serviceBusOptions = serviceBusOptions?.Value ?? throw new ArgumentNullException(nameof(serviceBusOptions));
        _graphOptions = graphOptions?.Value ?? throw new ArgumentNullException(nameof(graphOptions));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _emlConverter = emlConverter ?? throw new ArgumentNullException(nameof(emlConverter));
        _attachmentFilterService = attachmentFilterService ?? throw new ArgumentNullException(nameof(attachmentFilterService));
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

            // Step 3: Check if file is already uploaded to SPE (new direct flow)
            // TempFileLocation format: spe://{driveId}/{itemId} means file is already in SPE
            var isAlreadyInSpe = payload.TempFileLocation?.StartsWith("spe://", StringComparison.OrdinalIgnoreCase) ?? false;

            string driveId;
            string itemId;
            Guid documentId;

            if (isAlreadyInSpe)
            {
                // File already uploaded to SPE and Document record created by SaveAsync
                // Parse the SPE reference: spe://{driveId}/{itemId}
                var speRef = payload.TempFileLocation![6..]; // Remove "spe://" prefix
                var parts = speRef.Split('/', 2);
                if (parts.Length != 2)
                {
                    return JobOutcome.Failure(
                        "OFFICE_INTERNAL",
                        $"Invalid SPE reference format: {payload.TempFileLocation}",
                        retryable: false);
                }

                driveId = parts[0];
                itemId = parts[1];

                _logger.LogInformation(
                    "File already uploaded to SPE, skipping upload. DriveId={DriveId}, ItemId={ItemId}",
                    driveId, itemId);

                await UpdateJobStatusAsync(
                    message.JobId,
                    JobStatus.Running,
                    "FileAlreadyUploaded",
                    50,
                    cancellationToken);

                // Document record was already created by SaveAsync - use the passed DocumentId
                if (payload.DocumentId.HasValue && payload.DocumentId.Value != Guid.Empty)
                {
                    documentId = payload.DocumentId.Value;
                    _logger.LogInformation(
                        "Using Document ID from payload: {DocumentId}",
                        documentId);
                }
                else
                {
                    // Fallback: should not happen in production, log warning
                    _logger.LogWarning(
                        "DocumentId not provided in payload for already-uploaded file, creating new Document record");
                    documentId = await CreateDocumentRecordAsync(
                        payload,
                        driveId,
                        itemId,
                        message.UserId,
                        cancellationToken);
                }
            }
            else
            {
                // Traditional flow: download temp file and upload to SPE
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

                driveId = uploadResult.DriveId!;
                itemId = uploadResult.ItemId!;

                await UpdateJobStatusAsync(
                    message.JobId,
                    JobStatus.Running,
                    "RecordsCreating",
                    40,
                    cancellationToken);

                // Step 6: Create Document record in Dataverse
                documentId = await CreateDocumentRecordAsync(
                    payload,
                    driveId,
                    itemId,
                    message.UserId,
                    cancellationToken);
            }

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

            // Step 7.5: Process email attachments as child documents (if email with attachments)
            if (payload.ContentType == SaveContentType.Email && payload.EmailMetadata?.HasAttachments == true)
            {
                await ProcessEmailAttachmentsAsync(
                    driveId,
                    itemId,
                    documentId,
                    payload,
                    cancellationToken);

                await UpdateJobStatusAsync(
                    message.JobId,
                    JobStatus.Running,
                    "AttachmentsProcessed",
                    65,
                    cancellationToken);
            }

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
                await QueueNextStageAsync(message, documentId, payload, driveId, itemId, cancellationToken);
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
        var hasAssociation = !string.IsNullOrEmpty(payload.AssociationType) && payload.AssociationId.HasValue;
        _logger.LogDebug(
            hasAssociation
                ? "Creating Document record with association {AssociationType}:{AssociationId}"
                : "Creating Document record without association (document-only save)",
            payload.AssociationType ?? "none",
            payload.AssociationId);

        // Step 1: Create the base document record using IDataverseService
        var createRequest = new Spaarke.Dataverse.CreateDocumentRequest
        {
            Name = payload.FileName ?? "Untitled Document",
            ContainerId = payload.ContainerId,
            Description = payload.ContentType == SaveContentType.Email
                ? payload.EmailMetadata?.Subject
                : payload.AttachmentMetadata?.OriginalFileName
        };

        var documentIdString = await _dataverseService.CreateDocumentAsync(createRequest, cancellationToken);
        var documentId = Guid.Parse(documentIdString);

        _logger.LogInformation(
            "Document record created in Dataverse: DocumentId={DocumentId}",
            documentId);

        // Step 2: Update the document with SPE pointers and association lookups
        var updateRequest = new Spaarke.Dataverse.UpdateDocumentRequest
        {
            GraphDriveId = driveId,
            GraphItemId = itemId,
            FileName = payload.FileName,
            FileSize = payload.FileSize,
            MimeType = payload.MimeType,
            HasFile = true
        };

        // Set entity association lookup based on AssociationType
        if (hasAssociation && payload.AssociationId.HasValue)
        {
            switch (payload.AssociationType?.ToLowerInvariant())
            {
                case "matter":
                    updateRequest.MatterLookup = payload.AssociationId.Value;
                    break;
                case "project":
                    updateRequest.ProjectLookup = payload.AssociationId.Value;
                    break;
                case "invoice":
                    updateRequest.InvoiceLookup = payload.AssociationId.Value;
                    break;
                // Account and Contact associations would need additional lookup fields
                // added to UpdateDocumentRequest if needed
                default:
                    _logger.LogWarning(
                        "Unknown association type {AssociationType}, skipping association",
                        payload.AssociationType);
                    break;
            }
        }

        // Set email-specific fields if this is an email save
        if (payload.ContentType == SaveContentType.Email && payload.EmailMetadata != null)
        {
            updateRequest.EmailSubject = payload.EmailMetadata.Subject;
            updateRequest.EmailFrom = payload.EmailMetadata.SenderEmail;
            updateRequest.EmailTo = payload.EmailMetadata.RecipientsJson;
            updateRequest.EmailDate = payload.EmailMetadata.SentDate?.DateTime; // Convert DateTimeOffset to DateTime
            updateRequest.EmailBody = payload.EmailMetadata.BodyPreview;
            updateRequest.EmailMessageId = payload.EmailMetadata.InternetMessageId;
            updateRequest.EmailConversationIndex = payload.EmailMetadata.ConversationId;
            updateRequest.IsEmailArchive = true;
        }

        await _dataverseService.UpdateDocumentAsync(documentIdString, updateRequest, cancellationToken);

        _logger.LogInformation(
            "Document record updated with SPE pointers and metadata: DocumentId={DocumentId}, DriveId={DriveId}, ItemId={ItemId}",
            documentId,
            driveId,
            itemId);

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

        // Map importance (0=Low, 1=Normal, 2=High) to Dataverse priority option values
        var priorityValue = metadata.Importance switch
        {
            0 => 192350003, // Low
            1 => 192350002, // Medium (Normal)
            2 => 192350001, // Important (High)
            _ => 192350002  // Default to Medium
        };

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
            Priority = priorityValue, // Changed from Importance to Priority per Dataverse schema
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

        try
        {
            // Map JobStatus enum to Dataverse option set value
            var dataverseStatus = status switch
            {
                JobStatus.Queued => 0,
                JobStatus.Running => 1,
                JobStatus.Completed => 2,
                JobStatus.Failed => 3,
                JobStatus.Cancelled => 4,
                _ => 1 // Default to Running
            };

            // Update ProcessingJob record in Dataverse
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

            _logger.LogInformation(
                "ProcessingJob {JobId} updated in Dataverse: Status={Status}, Progress={Progress}%",
                jobId,
                status,
                progress);
        }
        catch (Exception ex)
        {
            // Log but don't fail - job status update is not critical path
            _logger.LogWarning(
                ex,
                "Failed to update ProcessingJob {JobId} in Dataverse, continuing",
                jobId);
        }
    }

    private async Task QueueNextStageAsync(
        OfficeJobMessage originalMessage,
        Guid documentId,
        UploadFinalizationPayload payload,
        string driveId,
        string itemId,
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

        // Get tenant ID from Graph configuration (customer-specific value from App Service settings)
        var tenantId = _graphOptions.TenantId;

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
                DriveId = driveId,
                ItemId = itemId,
                FileName = payload.FileName ?? "unknown",
                TenantId = tenantId,
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
    /// Processes email attachments as child documents.
    /// Downloads .eml from SPE, extracts attachments, uploads each to SPE,
    /// and creates child Document records with parent-child relationships.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Follows the same pattern as EmailToDocumentJobHandler.ProcessAttachmentsAsync.
    /// Attachment failures are logged but do not fail the main job.
    /// </para>
    /// <para>
    /// Child documents are linked to the parent via sprk_ParentDocumentLookup.
    /// Uses AttachmentFilterService to filter out noise (signatures, tracking pixels).
    /// </para>
    /// </remarks>
    private async Task ProcessEmailAttachmentsAsync(
        string driveId,
        string itemId,
        Guid parentDocumentId,
        UploadFinalizationPayload payload,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Processing email attachments for document {DocumentId}",
            parentDocumentId);

        try
        {
            // Download the .eml file from SPE
            var emlStream = await _speFileStore.DownloadFileAsync(driveId, itemId, cancellationToken);
            if (emlStream == null)
            {
                _logger.LogWarning(
                    "Failed to download .eml from SPE for attachment extraction. DriveId={DriveId}, ItemId={ItemId}",
                    driveId, itemId);
                return;
            }

            // Extract attachments using IEmailToEmlConverter
            IReadOnlyList<EmailAttachmentInfo> attachments;
            using (emlStream)
            {
                attachments = _emlConverter.ExtractAttachments(emlStream);
            }

            if (attachments.Count == 0)
            {
                _logger.LogDebug("No attachments found in .eml for document {DocumentId}", parentDocumentId);
                return;
            }

            _logger.LogInformation(
                "Extracted {Count} attachments from .eml for document {DocumentId}",
                attachments.Count, parentDocumentId);

            // Filter out noise (signatures, tracking pixels, etc.)
            var filteredAttachments = _attachmentFilterService.FilterAttachments(attachments);
            var filteredCount = attachments.Count - filteredAttachments.Count;

            _logger.LogInformation(
                "Filtered attachments for document {DocumentId}: {RemainingCount} to process, {FilteredCount} filtered out",
                parentDocumentId, filteredAttachments.Count, filteredCount);

            if (filteredAttachments.Count == 0)
            {
                _logger.LogDebug("All attachments were filtered out for document {DocumentId}", parentDocumentId);
                return;
            }

            // Get container ID from drive ID
            var containerId = driveId;

            // Process each attachment (sequential to avoid overwhelming SPE)
            var uploadedCount = 0;
            var failedCount = 0;

            foreach (var attachment in filteredAttachments)
            {
                try
                {
                    await ProcessSingleAttachmentAsync(
                        attachment,
                        parentDocumentId,
                        payload.FileName,
                        itemId,
                        driveId,
                        containerId,
                        payload.EmailMetadata?.ConversationId,
                        cancellationToken);

                    uploadedCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    _logger.LogWarning(ex,
                        "Failed to process attachment '{AttachmentName}' for document {DocumentId}",
                        attachment.FileName, parentDocumentId);
                }
                finally
                {
                    // Dispose the attachment stream
                    attachment.Content?.Dispose();
                }
            }

            // Dispose filtered-out attachment streams
            foreach (var att in attachments.Except(filteredAttachments))
            {
                att.Content?.Dispose();
            }

            _logger.LogInformation(
                "Attachment processing complete for document {DocumentId}: {UploadedCount} uploaded, {FailedCount} failed",
                parentDocumentId, uploadedCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing attachments for document {DocumentId}",
                parentDocumentId);
            // Don't rethrow - attachment failures should not fail the main job
        }
    }

    /// <summary>
    /// Process a single attachment: upload to SPE and create child Document record.
    /// </summary>
    private async Task ProcessSingleAttachmentAsync(
        EmailAttachmentInfo attachment,
        Guid parentDocumentId,
        string parentFileName,
        string parentGraphItemId,
        string driveId,
        string containerId,
        string? conversationIndex,
        CancellationToken cancellationToken)
    {
        if (attachment.Content == null || attachment.Content.Length == 0)
        {
            _logger.LogWarning("Attachment '{AttachmentName}' has no content, skipping", attachment.FileName);
            return;
        }

        // Reset stream position
        if (attachment.Content.CanSeek)
        {
            attachment.Content.Position = 0;
        }

        // Upload attachment to SPE in a subfolder of the parent email
        var attachmentPath = $"/emails/attachments/{parentDocumentId:N}/{attachment.FileName}";

        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, attachmentPath, attachment.Content, cancellationToken);

        if (fileHandle == null)
        {
            throw new InvalidOperationException($"Failed to upload attachment '{attachment.FileName}' to SPE");
        }

        _logger.LogDebug(
            "Uploaded attachment '{AttachmentName}' to SPE: ItemId={ItemId}",
            attachment.FileName, fileHandle.Id);

        // Create child Document record in Dataverse
        var createRequest = new CreateDocumentRequest
        {
            Name = attachment.FileName,
            ContainerId = containerId,
            Description = $"Email attachment from {parentFileName}"
        };

        var childDocumentIdStr = await _dataverseService.CreateDocumentAsync(createRequest, cancellationToken);

        if (!Guid.TryParse(childDocumentIdStr, out var childDocumentId))
        {
            throw new InvalidOperationException($"Failed to create Dataverse document record for attachment '{attachment.FileName}'");
        }

        // Update child document with file info and parent relationship
        var updateRequest = new UpdateDocumentRequest
        {
            FileName = attachment.FileName,
            FileSize = attachment.SizeBytes,
            MimeType = attachment.MimeType,
            GraphItemId = fileHandle.Id,
            GraphDriveId = driveId,
            FilePath = fileHandle.WebUrl,
            HasFile = true,
            DocumentType = DocumentTypeEmailAttachment,

            // Parent relationship
            ParentDocumentLookup = parentDocumentId,
            ParentFileName = parentFileName,
            ParentGraphItemId = parentGraphItemId,
            RelationshipType = RelationshipTypeEmailAttachment,

            // Source type for relationship queries
            SourceType = SourceTypeEmailAttachment,

            // Copy ConversationIndex from parent email to enable same_thread queries
            EmailConversationIndex = conversationIndex
        };

        await _dataverseService.UpdateDocumentAsync(childDocumentIdStr, updateRequest, cancellationToken);

        _logger.LogInformation(
            "Created child document {ChildDocumentId} for attachment '{AttachmentName}' (parent: {ParentDocumentId})",
            childDocumentId, attachment.FileName, parentDocumentId);
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
