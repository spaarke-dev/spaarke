using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Services.Email;
using Sprk.Bff.Api.Telemetry;

namespace Sprk.Bff.Api.Services.Jobs.Handlers;

/// <summary>
/// Job handler for email-to-document conversion jobs.
/// Processes emails from both webhook and polling triggers, converting them to .eml documents
/// stored in SharePoint Embedded with Dataverse metadata records.
///
/// Follows ADR-004 for job contract patterns and idempotency requirements.
/// Follows ADR-007 for SPE operations via SpeFileStore facade.
/// </summary>
public class EmailToDocumentJobHandler : IJobHandler
{
    private readonly IEmailToEmlConverter _emlConverter;
    private readonly AttachmentFilterService _attachmentFilterService;
    private readonly SpeFileStore _speFileStore;
    private readonly IDataverseService _dataverseService;
    private readonly IIdempotencyService _idempotencyService;
    private readonly JobSubmissionService _jobSubmissionService;
    private readonly EmailProcessingOptions _options;
    private readonly EmailTelemetry _telemetry;
    private readonly ILogger<EmailToDocumentJobHandler> _logger;

    // Document type choice value for Email
    private const int DocumentTypeEmail = 100000006;

    // Document type choice value for Email Attachment
    private const int DocumentTypeEmailAttachment = 100000007;

    // Relationship type choice value for Email Attachment
    private const int RelationshipTypeEmailAttachment = 100000000;

    /// <summary>
    /// Job type constant - must match the JobType used by webhook and polling triggers.
    /// </summary>
    public const string JobTypeName = "ProcessEmailToDocument";

    public EmailToDocumentJobHandler(
        IEmailToEmlConverter emlConverter,
        AttachmentFilterService attachmentFilterService,
        SpeFileStore speFileStore,
        IDataverseService dataverseService,
        IIdempotencyService idempotencyService,
        JobSubmissionService jobSubmissionService,
        IOptions<EmailProcessingOptions> options,
        EmailTelemetry telemetry,
        ILogger<EmailToDocumentJobHandler> logger)
    {
        _emlConverter = emlConverter ?? throw new ArgumentNullException(nameof(emlConverter));
        _attachmentFilterService = attachmentFilterService ?? throw new ArgumentNullException(nameof(attachmentFilterService));
        _speFileStore = speFileStore ?? throw new ArgumentNullException(nameof(speFileStore));
        _dataverseService = dataverseService ?? throw new ArgumentNullException(nameof(dataverseService));
        _idempotencyService = idempotencyService ?? throw new ArgumentNullException(nameof(idempotencyService));
        _jobSubmissionService = jobSubmissionService ?? throw new ArgumentNullException(nameof(jobSubmissionService));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = _telemetry.RecordJobStart();
        using var activity = _telemetry.StartActivity("EmailToDocument.ProcessJob", correlationId: job.CorrelationId);

        try
        {
            _logger.LogInformation(
                "Processing email-to-document job {JobId} for subject {SubjectId}, Attempt {Attempt}, CorrelationId {CorrelationId}",
                job.JobId, job.SubjectId, job.Attempt, job.CorrelationId);

            // Parse payload to get emailId and trigger source
            var payload = ParsePayload(job.Payload);
            if (payload == null || payload.EmailId == Guid.Empty)
            {
                _logger.LogError("Invalid payload for email-to-document job {JobId}", job.JobId);
                _telemetry.RecordJobFailure(stopwatch, "invalid_payload");
                return JobOutcome.Poisoned(job.JobId, JobType, "Invalid job payload", job.Attempt, stopwatch.Elapsed);
            }

            var emailId = payload.EmailId;
            var triggerSource = payload.TriggerSource ?? "Unknown";

            _logger.LogDebug(
                "Processing email {EmailId} from trigger {TriggerSource}",
                emailId, triggerSource);

            // Check idempotency - prevent duplicate processing
            var idempotencyKey = job.IdempotencyKey;
            if (string.IsNullOrEmpty(idempotencyKey))
            {
                idempotencyKey = $"Email:{emailId}:Archive";
            }

            if (await _idempotencyService.IsEventProcessedAsync(idempotencyKey, ct))
            {
                _logger.LogInformation(
                    "Email {EmailId} already processed (idempotency key: {IdempotencyKey})",
                    emailId, idempotencyKey);

                _telemetry.RecordJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            // Try to acquire processing lock
            if (!await _idempotencyService.TryAcquireProcessingLockAsync(idempotencyKey, TimeSpan.FromMinutes(5), ct))
            {
                _logger.LogWarning(
                    "Could not acquire processing lock for email {EmailId} (idempotency key: {IdempotencyKey})",
                    emailId, idempotencyKey);

                // Return success to prevent retry - another instance is processing
                _telemetry.RecordJobSkippedDuplicate();
                stopwatch.Stop();
                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }

            try
            {
                // Convert email to .eml format
                var emlResult = await _emlConverter.ConvertToEmlAsync(emailId, includeAttachments: true, ct);

                if (!emlResult.Success || emlResult.EmlStream == null || emlResult.Metadata == null)
                {
                    _logger.LogError(
                        "Failed to convert email {EmailId} to .eml: {Error}",
                        emailId, emlResult.ErrorMessage);

                    // Permanent failure - don't retry
                    _telemetry.RecordJobFailure(stopwatch, "conversion_failed");
                    return JobOutcome.Poisoned(
                        job.JobId, JobType,
                        $"Email conversion failed: {emlResult.ErrorMessage}",
                        job.Attempt, stopwatch.Elapsed);
                }

                var metadata = emlResult.Metadata;
                var emlStream = emlResult.EmlStream;

                // Generate filename for the .eml file
                var fileName = await _emlConverter.GenerateEmlFileNameAsync(emailId, ct);

                _logger.LogDebug(
                    "Email {EmailId} converted to .eml ({Size} bytes), filename: {FileName}",
                    emailId, emlResult.FileSizeBytes, fileName);

                // Get the container ID for email documents
                var containerId = _options.DefaultContainerId;
                if (string.IsNullOrEmpty(containerId))
                {
                    _logger.LogError("DefaultContainerId not configured for email processing");
                    _telemetry.RecordJobFailure(stopwatch, "config_error");
                    return JobOutcome.Poisoned(
                        job.JobId, JobType,
                        "DefaultContainerId not configured",
                        job.Attempt, stopwatch.Elapsed);
                }

                // Resolve container to drive ID
                var driveId = await _speFileStore.ResolveDriveIdAsync(containerId, ct);

                // Upload .eml file to SPE
                var uploadPath = $"/emails/{fileName}";
                var fileHandle = await _speFileStore.UploadSmallAsync(driveId, uploadPath, emlStream, ct);

                if (fileHandle == null)
                {
                    _logger.LogError("Failed to upload .eml file to SPE for email {EmailId}", emailId);
                    _telemetry.RecordJobFailure(stopwatch, "upload_failed");
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        "Failed to upload .eml file to SPE",
                        job.Attempt, stopwatch.Elapsed);
                }

                _logger.LogInformation(
                    "Uploaded .eml file for email {EmailId} to SPE: ItemId={ItemId}",
                    emailId, fileHandle.Id);

                // Create Dataverse document record (minimal required fields)
                var createRequest = new CreateDocumentRequest
                {
                    Name = metadata.Subject.Length > 0 ? metadata.Subject : fileName,
                    ContainerId = containerId,
                    Description = $"Email archived via {triggerSource} on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC"
                };

                var documentIdStr = await _dataverseService.CreateDocumentAsync(createRequest, ct);

                if (!Guid.TryParse(documentIdStr, out var documentId))
                {
                    _logger.LogError("Failed to create Dataverse document record for email {EmailId}", emailId);
                    _telemetry.RecordJobFailure(stopwatch, "dataverse_create_failed");
                    return JobOutcome.Failure(
                        job.JobId, JobType,
                        "Failed to create Dataverse document record",
                        job.Attempt, stopwatch.Elapsed);
                }

                _logger.LogInformation(
                    "Created Dataverse document record {DocumentId} for email {EmailId}",
                    documentId, emailId);

                // Update document with file info and email metadata
                var updateRequest = new UpdateDocumentRequest
                {
                    FileName = fileName,
                    FileSize = emlResult.FileSizeBytes,
                    MimeType = "message/rfc822",
                    GraphItemId = fileHandle.Id,
                    GraphDriveId = driveId,
                    FilePath = fileHandle.WebUrl, // SharePoint URL for "Open in SharePoint" links
                    HasFile = true,
                    DocumentType = DocumentTypeEmail,
                    IsEmailArchive = true,

                    // Email metadata
                    EmailSubject = metadata.Subject,
                    EmailFrom = metadata.From,
                    EmailTo = metadata.To,
                    EmailCc = metadata.Cc,
                    EmailDate = metadata.EmailDate,
                    EmailBody = metadata.Body,
                    EmailMessageId = metadata.MessageId,
                    EmailDirection = metadata.Direction,
                    EmailTrackingToken = metadata.TrackingToken,
                    EmailConversationIndex = metadata.ConversationIndex,

                    // Link to original email activity
                    EmailLookup = emailId
                };

                await _dataverseService.UpdateDocumentAsync(documentIdStr, updateRequest, ct);

                _logger.LogInformation(
                    "Updated document {DocumentId} with email metadata for email {EmailId}",
                    documentId, emailId);

                // Enqueue AI analysis for main email document (if enabled via AutoEnqueueAi)
                // Uses AppOnlyAnalysisService which runs without user context
                await EnqueueAiAnalysisJobAsync(documentId, "Email", ct);

                // Process attachments as child documents (FR-04)
                // Attachment failures should not fail the main job
                // Note: AI analysis is also enqueued for each attachment in ProcessSingleAttachmentAsync
                var attachmentResults = await ProcessAttachmentsAsync(
                    emlStream,
                    documentId,
                    fileName,
                    fileHandle.Id,
                    driveId,
                    containerId,
                    emailId,
                    ct);

                // Mark as processed
                await _idempotencyService.MarkEventAsProcessedAsync(
                    idempotencyKey,
                    TimeSpan.FromDays(7), // Keep record for 7 days
                    ct);

                // Record telemetry for successful job (including attachment counts)
                _telemetry.RecordJobSuccess(stopwatch, emlResult.FileSizeBytes, emlResult.Attachments.Count);
                _telemetry.RecordAttachmentProcessing(
                    attachmentResults.ExtractedCount,
                    attachmentResults.FilteredCount,
                    attachmentResults.UploadedCount,
                    attachmentResults.FailedCount);

                _logger.LogInformation(
                    "Email-to-document job {JobId} completed in {Duration}ms. Email {EmailId} -> Document {DocumentId}",
                    job.JobId, stopwatch.ElapsedMilliseconds, emailId, documentId);

                return JobOutcome.Success(job.JobId, JobType, stopwatch.Elapsed);
            }
            finally
            {
                // Always release the lock
                await _idempotencyService.ReleaseProcessingLockAsync(idempotencyKey, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Email-to-document job {JobId} failed: {Error}", job.JobId, ex.Message);

            // Check for retryable vs permanent failures
            var isRetryable = IsRetryableException(ex);
            _telemetry.RecordJobFailure(stopwatch, isRetryable ? "transient_error" : "permanent_error");

            if (isRetryable)
            {
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            // Permanent failure
            return JobOutcome.Poisoned(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
        }
    }

    private EmailToDocumentPayload? ParsePayload(JsonDocument? payload)
    {
        if (payload == null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<EmailToDocumentPayload>(payload, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse email-to-document job payload");
            return null;
        }
    }


    private static bool IsRetryableException(Exception ex)
    {
        // HTTP 429 (throttling), 503 (service unavailable), etc.
        if (ex is HttpRequestException)
        {
            return true;
        }

        // Check for known throttling exception types
        var exceptionName = ex.GetType().Name;
        return exceptionName.Contains("Throttling", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("ServiceUnavailable", StringComparison.OrdinalIgnoreCase) ||
               exceptionName.Contains("Timeout", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Process email attachments as child documents.
    /// Extracts attachments from the .eml file, filters out noise (signatures, tracking pixels),
    /// uploads meaningful attachments to SPE, and creates child Document records.
    /// </summary>
    /// <remarks>
    /// Attachment failures are logged but do not fail the main job.
    /// Child documents are linked to the parent via sprk_ParentDocumentLookup.
    /// </remarks>
    private async Task<AttachmentProcessingResult> ProcessAttachmentsAsync(
        Stream emlStream,
        Guid parentDocumentId,
        string parentFileName,
        string parentGraphItemId,
        string driveId,
        string containerId,
        Guid emailId,
        CancellationToken ct)
    {
        var result = new AttachmentProcessingResult();

        try
        {
            // Reset stream position for extraction
            if (emlStream.CanSeek)
                emlStream.Position = 0;

            // Extract all attachments from the .eml file
            var allAttachments = _emlConverter.ExtractAttachments(emlStream);
            result.ExtractedCount = allAttachments.Count;

            if (allAttachments.Count == 0)
            {
                _logger.LogDebug("No attachments found in email for parent document {ParentDocumentId}", parentDocumentId);
                return result;
            }

            _logger.LogDebug(
                "Extracted {AttachmentCount} attachments from email for parent document {ParentDocumentId}",
                allAttachments.Count, parentDocumentId);

            // Filter out noise (signature images, tracking pixels, calendar files, etc.)
            var filteredAttachments = _attachmentFilterService.FilterAttachments(allAttachments);
            result.FilteredCount = allAttachments.Count - filteredAttachments.Count;

            if (filteredAttachments.Count == 0)
            {
                _logger.LogDebug(
                    "All {AttachmentCount} attachments were filtered out for parent document {ParentDocumentId}",
                    allAttachments.Count, parentDocumentId);

                // Dispose all attachment streams
                foreach (var att in allAttachments)
                    att.Content?.Dispose();

                return result;
            }

            _logger.LogInformation(
                "Processing {ProcessCount} of {TotalCount} attachments for parent document {ParentDocumentId} ({FilteredCount} filtered)",
                filteredAttachments.Count, allAttachments.Count, parentDocumentId, result.FilteredCount);

            // Process each attachment (sequential to avoid overwhelming SPE)
            foreach (var attachment in filteredAttachments)
            {
                try
                {
                    await ProcessSingleAttachmentAsync(
                        attachment,
                        parentDocumentId,
                        parentFileName,
                        parentGraphItemId,
                        driveId,
                        containerId,
                        emailId,
                        ct);

                    result.UploadedCount++;
                }
                catch (Exception ex)
                {
                    result.FailedCount++;
                    _logger.LogWarning(ex,
                        "Failed to process attachment '{AttachmentName}' for parent document {ParentDocumentId}: {Error}",
                        attachment.FileName, parentDocumentId, ex.Message);
                }
                finally
                {
                    // Dispose the attachment stream
                    attachment.Content?.Dispose();
                }
            }

            // Dispose filtered-out attachment streams
            foreach (var att in allAttachments.Except(filteredAttachments))
                att.Content?.Dispose();

            _logger.LogInformation(
                "Attachment processing complete for parent document {ParentDocumentId}: {UploadedCount} uploaded, {FailedCount} failed",
                parentDocumentId, result.UploadedCount, result.FailedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error processing attachments for parent document {ParentDocumentId}: {Error}",
                parentDocumentId, ex.Message);
        }

        return result;
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
        Guid emailId,
        CancellationToken ct)
    {
        if (attachment.Content == null || attachment.Content.Length == 0)
        {
            _logger.LogWarning("Attachment '{AttachmentName}' has no content, skipping", attachment.FileName);
            return;
        }

        // Reset stream position
        if (attachment.Content.CanSeek)
            attachment.Content.Position = 0;

        // Upload attachment to SPE in a subfolder of the parent email
        var attachmentPath = $"/emails/attachments/{parentDocumentId:N}/{attachment.FileName}";
        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, attachmentPath, attachment.Content, ct);

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

        var childDocumentIdStr = await _dataverseService.CreateDocumentAsync(createRequest, ct);

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
            ParentDocumentId = parentDocumentId.ToString(),
            ParentFileName = parentFileName,
            ParentGraphItemId = parentGraphItemId,
            RelationshipType = RelationshipTypeEmailAttachment,

            // Link to original email activity
            EmailLookup = emailId
        };

        await _dataverseService.UpdateDocumentAsync(childDocumentIdStr, updateRequest, ct);

        _logger.LogInformation(
            "Created child document {ChildDocumentId} for attachment '{AttachmentName}' (parent: {ParentDocumentId})",
            childDocumentId, attachment.FileName, parentDocumentId);

        // Enqueue AI analysis for attachment document (if enabled)
        await EnqueueAiAnalysisJobAsync(childDocumentId, "EmailAttachment", ct);
    }

    /// <summary>
    /// Enqueues an AI analysis job for a document if AutoEnqueueAi is enabled.
    /// Uses try/catch to ensure enqueueing failures don't fail the main processing.
    /// </summary>
    /// <param name="documentId">The document ID to analyze</param>
    /// <param name="source">Source type: "Email" or "EmailAttachment"</param>
    /// <param name="ct">Cancellation token</param>
    private async Task EnqueueAiAnalysisJobAsync(Guid documentId, string source, CancellationToken ct)
    {
        if (!_options.AutoEnqueueAi)
        {
            _logger.LogDebug(
                "AutoEnqueueAi disabled, skipping AI analysis job for document {DocumentId}",
                documentId);
            return;
        }

        try
        {
            var analysisJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = AppOnlyDocumentAnalysisJobHandler.JobTypeName,
                SubjectId = documentId.ToString(),
                CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString(),
                IdempotencyKey = $"analysis-{documentId}-documentprofile",
                Attempt = 1,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new
                {
                    DocumentId = documentId,
                    Source = source,
                    EnqueuedAt = DateTimeOffset.UtcNow
                }))
            };

            await _jobSubmissionService.SubmitJobAsync(analysisJob, ct);

            var documentType = source == "EmailAttachment" ? "attachment" : "email";
            _telemetry.RecordAiJobEnqueued(documentType);

            _logger.LogInformation(
                "Enqueued AI analysis job {JobId} for {Source} document {DocumentId}",
                analysisJob.JobId, source, documentId);
        }
        catch (Exception ex)
        {
            // Log but don't fail - AI analysis is non-critical
            var documentType = source == "EmailAttachment" ? "attachment" : "email";
            _telemetry.RecordAiJobEnqueueFailure(documentType, "enqueue_error");

            _logger.LogWarning(ex,
                "Failed to enqueue AI analysis job for {Source} document {DocumentId}: {Error}. Email processing will continue.",
                source, documentId, ex.Message);
        }
    }
}

/// <summary>
/// Payload structure for email-to-document jobs.
/// </summary>
public class EmailToDocumentPayload
{
    /// <summary>
    /// The Dataverse email activity ID to process.
    /// </summary>
    public Guid EmailId { get; set; }

    /// <summary>
    /// Source of the trigger: "Webhook", "PollingBackup", "Manual", etc.
    /// </summary>
    public string? TriggerSource { get; set; }

    /// <summary>
    /// When the job was triggered.
    /// </summary>
    public string? TriggeredAt { get; set; }

    /// <summary>
    /// Dataverse message name (for webhook triggers): "Create", "Update", etc.
    /// </summary>
    public string? MessageName { get; set; }

    /// <summary>
    /// Webhook correlation ID from Dataverse (for tracing).
    /// </summary>
    public Guid? WebhookCorrelationId { get; set; }
}

/// <summary>
/// Result of attachment processing for telemetry and logging.
/// </summary>
public class AttachmentProcessingResult
{
    /// <summary>
    /// Number of attachments extracted from the .eml file.
    /// </summary>
    public int ExtractedCount { get; set; }

    /// <summary>
    /// Number of attachments filtered out (signatures, tracking pixels, etc.).
    /// </summary>
    public int FilteredCount { get; set; }

    /// <summary>
    /// Number of attachments successfully uploaded and documented.
    /// </summary>
    public int UploadedCount { get; set; }

    /// <summary>
    /// Number of attachments that failed to process.
    /// </summary>
    public int FailedCount { get; set; }
}
