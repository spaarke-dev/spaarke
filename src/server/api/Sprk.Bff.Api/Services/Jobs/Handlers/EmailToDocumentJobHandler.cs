using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
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
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly EmailProcessingOptions _options;
    private readonly EmailTelemetry _telemetry;
    private readonly ILogger<EmailToDocumentJobHandler> _logger;

    // Document type choice value for Email
    private const int DocumentTypeEmail = 100000006;

    // Document type choice value for Email Attachment
    private const int DocumentTypeEmailAttachment = 100000007;

    // Relationship type choice value for Email Attachment
    private const int RelationshipTypeEmailAttachment = 100000000;

    // Email processing status choice values (sprk_documentprocessingstatus on email entity)
    // These prevent infinite retry loops by marking emails as processed/failed
    private const int ProcessingStatusInProgress = 100000000;
    private const int ProcessingStatusCompleted = 100000001;
    private const int ProcessingStatusFailed = 100000002;

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
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
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
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string JobType => JobTypeName;

    public async Task<JobOutcome> ProcessAsync(JobContract job, CancellationToken ct)
    {
        var stopwatch = _telemetry.RecordJobStart();
        using var activity = _telemetry.StartActivity("EmailToDocument.ProcessJob", correlationId: job.CorrelationId);

        // Track emailId at method scope for circuit breaker status update in catch block
        var emailId = Guid.Empty;

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

            emailId = payload.EmailId;
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
                // Circuit breaker: Mark email as "InProgress" to prevent polling service from picking it up again
                // This is set on first attempt only - subsequent retries keep the status as InProgress
                if (job.Attempt == 1)
                {
                    await UpdateEmailProcessingStatusAsync(emailId, ProcessingStatusInProgress, null, ct);
                }

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

                // CRITICAL: Copy EML content to byte array immediately to avoid stream state issues
                // after upload. The upload operation consumes the stream, and we need a fresh
                // stream for attachment extraction.
                byte[] emlContent;
                using (var originalStream = emlResult.EmlStream)
                {
                    originalStream.Position = 0;
                    emlContent = new byte[originalStream.Length];
                    await originalStream.ReadExactlyAsync(emlContent, ct);
                }

                _logger.LogDebug(
                    "[AttachmentDebug] Copied EML content to byte array: {Size} bytes",
                    emlContent.Length);

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

                // Upload .eml file to SPE using a fresh stream from the byte array
                var uploadPath = $"/emails/{fileName}";
                FileHandleDto? fileHandle;
                await using (var uploadStream = new MemoryStream(emlContent, writable: false))
                {
                    fileHandle = await _speFileStore.UploadSmallAsync(driveId, uploadPath, uploadStream, ct);
                }

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

                // Enqueue RAG indexing for main email document (if enabled via AutoIndexToRag)
                // RAG indexing failures are non-blocking per owner clarification
                await EnqueueRagIndexingJobAsync(driveId, fileHandle.Id, documentId, fileName, ct);

                // Process attachments as child documents (FR-04)
                // Attachment failures should not fail the main job
                // Note: AI analysis is also enqueued for each attachment in ProcessSingleAttachmentAsync
                // Optimization: Pass fetched attachments directly instead of re-extracting from .eml
                // The attachment streams are still valid because BuildMimeMessage uses ToArray() which
                // doesn't consume the MemoryStream position.
                _logger.LogWarning(
                    "[AttachmentProcessDebug] Processing {AttachmentCount} attachments for document {DocumentId}",
                    emlResult.Attachments.Count, documentId);

                var attachmentResults = await ProcessAttachmentsAsync(
                    emlResult.Attachments,
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

                // Circuit breaker: Mark email as "Completed" - polling service will never pick it up again
                await UpdateEmailProcessingStatusAsync(emailId, ProcessingStatusCompleted, null, ct);

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

            // Circuit breaker: Check if this is the last retry attempt
            var isLastAttempt = job.Attempt >= job.MaxAttempts;

            if (isRetryable && !isLastAttempt)
            {
                // Will retry - keep status as InProgress
                _logger.LogWarning(
                    "Email {EmailId} processing failed (attempt {Attempt}/{MaxAttempts}), will retry: {Error}",
                    emailId, job.Attempt, job.MaxAttempts, ex.Message);
                return JobOutcome.Failure(job.JobId, JobType, ex.Message, job.Attempt, stopwatch.Elapsed);
            }

            // Circuit breaker: Mark email as "Failed" - stop all retries
            // This happens when:
            // 1. It's a permanent (non-retryable) failure, OR
            // 2. It's the last retry attempt (exhausted all retries)
            if (emailId != Guid.Empty)
            {
                var failureReason = isLastAttempt
                    ? $"Max retries ({job.MaxAttempts}) exhausted: {ex.Message}"
                    : $"Permanent failure: {ex.Message}";

                await UpdateEmailProcessingStatusAsync(emailId, ProcessingStatusFailed, failureReason, ct);

                _logger.LogError(
                    "Email {EmailId} processing permanently failed after {Attempts} attempts: {Error}",
                    emailId, job.Attempt, ex.Message);
            }

            // Permanent failure - no more retries
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
    /// Updates the email's sprk_documentprocessingstatus field to prevent infinite retry loops.
    /// This is the circuit breaker - once an email is marked as Completed or Failed, the polling
    /// service will stop picking it up (it queries for status eq null).
    /// </summary>
    /// <param name="emailId">The email activity ID</param>
    /// <param name="status">Processing status: InProgress (100000000), Completed (100000001), Failed (100000002)</param>
    /// <param name="errorMessage">Optional error message for failed status</param>
    /// <param name="ct">Cancellation token</param>
    private async Task UpdateEmailProcessingStatusAsync(
        Guid emailId,
        int status,
        string? errorMessage,
        CancellationToken ct)
    {
        try
        {
            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');
            if (string.IsNullOrEmpty(dataverseUrl))
            {
                _logger.LogWarning("Dataverse:ServiceUrl not configured, cannot update email processing status");
                return;
            }

            var accessToken = await GetDataverseAccessTokenAsync(ct);
            if (string.IsNullOrEmpty(accessToken))
            {
                _logger.LogWarning("Failed to get Dataverse access token for status update");
                return;
            }

            var client = _httpClientFactory.CreateClient("DataverseStatusUpdate");
            client.BaseAddress = new Uri($"{dataverseUrl}/api/data/v9.2/");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            client.DefaultRequestHeaders.Add("OData-MaxVersion", "4.0");
            client.DefaultRequestHeaders.Add("OData-Version", "4.0");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            // Build the PATCH payload - only update the processing status field
            var updatePayload = new Dictionary<string, object?>
            {
                ["sprk_documentprocessingstatus"] = status
            };

            // If there's an error message and status is Failed, we could store it in a notes field
            // For now, we just log it - the polling query filters by status, not error message

            var jsonPayload = JsonSerializer.Serialize(updatePayload);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            // PATCH to emails(emailId)
            var response = await client.PatchAsync($"emails({emailId})", content, ct);

            if (response.IsSuccessStatusCode)
            {
                var statusName = status switch
                {
                    ProcessingStatusInProgress => "InProgress",
                    ProcessingStatusCompleted => "Completed",
                    ProcessingStatusFailed => "Failed",
                    _ => status.ToString()
                };

                _logger.LogInformation(
                    "Updated email {EmailId} processing status to {Status}",
                    emailId, statusName);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync(ct);
                _logger.LogWarning(
                    "Failed to update email {EmailId} processing status: {StatusCode} - {Error}",
                    emailId, response.StatusCode, errorContent);
            }
        }
        catch (Exception ex)
        {
            // Don't fail the main job if status update fails - this is best-effort
            _logger.LogWarning(ex,
                "Error updating email {EmailId} processing status: {Error}",
                emailId, ex.Message);
        }
    }

    /// <summary>
    /// Gets an access token for Dataverse using client credentials flow.
    /// </summary>
    private async Task<string?> GetDataverseAccessTokenAsync(CancellationToken ct)
    {
        try
        {
            var tenantId = _configuration["AzureAd:TenantId"];
            var clientId = _configuration["AzureAd:ClientId"];
            var clientSecret = _configuration["AzureAd:ClientSecret"];
            var dataverseUrl = _configuration["Dataverse:ServiceUrl"]?.TrimEnd('/');

            if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(clientId) ||
                string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(dataverseUrl))
            {
                _logger.LogWarning("Missing Azure AD or Dataverse configuration for status update");
                return null;
            }

            var app = Microsoft.Identity.Client.ConfidentialClientApplicationBuilder
                .Create(clientId)
                .WithTenantId(tenantId)
                .WithClientSecret(clientSecret)
                .Build();

            var scopes = new[] { $"{dataverseUrl}/.default" };
            var result = await app.AcquireTokenForClient(scopes).ExecuteAsync(ct);

            return result.AccessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to acquire Dataverse access token for status update");
            return null;
        }
    }

    /// <summary>
    /// Process email attachments as child documents.
    /// Filters out noise (signatures, tracking pixels), uploads meaningful attachments to SPE,
    /// and creates child Document records.
    /// </summary>
    /// <remarks>
    /// Attachment failures are logged but do not fail the main job.
    /// Child documents are linked to the parent via sprk_ParentDocumentLookup.
    /// Attachments are passed directly from FetchAttachmentsAsync to avoid redundant re-extraction from .eml.
    /// </remarks>
    private async Task<AttachmentProcessingResult> ProcessAttachmentsAsync(
        IReadOnlyList<EmailAttachmentInfo> fetchedAttachments,
        Guid parentDocumentId,
        string parentFileName,
        string parentGraphItemId,
        string driveId,
        string containerId,
        Guid emailId,
        CancellationToken ct)
    {
        var result = new AttachmentProcessingResult();

        _logger.LogWarning(
            "[AttachmentProcessDebug] ProcessAttachmentsAsync: Processing {Count} attachments for parent document {ParentDoc}",
            fetchedAttachments.Count, parentDocumentId);

        try
        {
            // Use attachments directly from FetchAttachmentsAsync (already fetched during .eml conversion)
            // This avoids redundant re-extraction from the .eml file
            var allAttachments = fetchedAttachments.ToList();
            result.ExtractedCount = allAttachments.Count;

            _logger.LogDebug(
                "Processing {Count} attachments for parent {ParentDocumentId}",
                allAttachments.Count, parentDocumentId);

            if (allAttachments.Count == 0)
            {
                _logger.LogWarning(
                    "[AttachmentProcessDebug] No attachments to process for parent document {ParentDocumentId}",
                    parentDocumentId);
                return result;
            }

            // Filter out noise (signature images, tracking pixels, calendar files, etc.)
            var filteredAttachments = _attachmentFilterService.FilterAttachments(allAttachments);
            result.FilteredCount = allAttachments.Count - filteredAttachments.Count;

            _logger.LogWarning(
                "[AttachmentProcessDebug] Filtered attachments for parent {ParentDocumentId}: {RemainingCount} to process, {FilteredCount} filtered out. Remaining: [{FileList}]",
                parentDocumentId, filteredAttachments.Count, result.FilteredCount, string.Join(", ", filteredAttachments.Select(a => a.FileName)));

            if (filteredAttachments.Count == 0)
            {
                _logger.LogWarning(
                    "[AttachmentProcessDebug] All {AttachmentCount} attachments were filtered out for parent {ParentDocumentId}. Filtered: [{FileList}]",
                    allAttachments.Count, parentDocumentId, string.Join(", ", allAttachments.Select(a => a.FileName)));

                // Dispose streams for filtered attachments
                foreach (var att in allAttachments)
                {
                    att.Content?.Dispose();
                }

                return result;
            }

            _logger.LogInformation(
                "Processing {ProcessCount} of {TotalCount} attachments for parent document {ParentDocumentId}",
                filteredAttachments.Count, allAttachments.Count, parentDocumentId);

            // Process each attachment (sequential to avoid overwhelming SPE)
            foreach (var attachment in filteredAttachments)
            {
                _logger.LogWarning(
                    "[AttachmentProcessDebug] Processing attachment '{FileName}' ({Size} bytes, ContentNull={ContentNull}, ContentLength={ContentLength}) for parent {ParentDocumentId}",
                    attachment.FileName, attachment.SizeBytes, attachment.Content == null, attachment.Content?.Length ?? -1, parentDocumentId);

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

        // Reset stream position (stream was already read during .eml building via ToArray())
        if (attachment.Content.CanSeek)
            attachment.Content.Position = 0;

        // Upload attachment to SPE in a subfolder of the parent email
        var attachmentPath = $"/emails/attachments/{parentDocumentId:N}/{attachment.FileName}";

        var fileHandle = await _speFileStore.UploadSmallAsync(driveId, attachmentPath, attachment.Content, ct);

        if (fileHandle == null)
        {
            throw new InvalidOperationException($"Failed to upload attachment '{attachment.FileName}' to SPE");
        }

        _logger.LogWarning(
            "[AttachmentProcessDebug] Uploaded attachment '{AttachmentName}' to SPE: ItemId={ItemId}, WebUrl={WebUrl}",
            attachment.FileName, fileHandle.Id, fileHandle.WebUrl);

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

            // Parent relationship (ParentDocumentLookup sets the lookup via @odata.bind)
            ParentDocumentLookup = parentDocumentId,
            ParentFileName = parentFileName,
            ParentGraphItemId = parentGraphItemId,
            RelationshipType = RelationshipTypeEmailAttachment

            // Note: EmailLookup is NOT set for child documents because:
            // 1. The sprk_document entity has an alternate key on sprk_email (Email Activity Key)
            // 2. The parent .eml document already uses that email lookup
            // 3. Child attachments relate to the email through their ParentDocumentLookup
        };

        _logger.LogWarning(
            "[AttachmentProcessDebug] Updating child document {ChildDocumentId} with: GraphItemId={GraphItemId}, GraphDriveId={GraphDriveId}, FilePath={FilePath}, HasFile={HasFile}, ParentDocumentLookup={ParentDocumentLookup}",
            childDocumentIdStr, updateRequest.GraphItemId, updateRequest.GraphDriveId, updateRequest.FilePath, updateRequest.HasFile, updateRequest.ParentDocumentLookup);

        await _dataverseService.UpdateDocumentAsync(childDocumentIdStr, updateRequest, ct);

        _logger.LogWarning(
            "[AttachmentProcessDebug] Created child document {ChildDocumentId} for attachment '{AttachmentName}' (parent: {ParentDocumentId})",
            childDocumentId, attachment.FileName, parentDocumentId);

        // Enqueue AI analysis for attachment document (if enabled)
        await EnqueueAiAnalysisJobAsync(childDocumentId, "EmailAttachment", ct);

        // Enqueue RAG indexing for attachment document (if enabled via AutoIndexToRag)
        await EnqueueRagIndexingJobAsync(driveId, fileHandle.Id, childDocumentId, attachment.FileName, ct);
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

    /// <summary>
    /// Enqueues a RAG indexing job for a document if AutoIndexToRag is enabled.
    /// Uses try/catch to ensure enqueueing failures don't fail the main processing.
    /// RAG indexing is non-blocking per owner clarification - it's an enhancement, not critical path.
    /// </summary>
    /// <param name="driveId">The SPE drive ID where the file is stored</param>
    /// <param name="itemId">The SPE item ID of the file</param>
    /// <param name="documentId">The Dataverse document ID</param>
    /// <param name="fileName">The file name for logging and metadata</param>
    /// <param name="ct">Cancellation token</param>
    private async Task EnqueueRagIndexingJobAsync(
        string driveId,
        string itemId,
        Guid documentId,
        string fileName,
        CancellationToken ct)
    {
        if (!_options.AutoIndexToRag)
        {
            _telemetry.RecordRagJobSkipped("config_disabled");
            _logger.LogDebug(
                "AutoIndexToRag disabled, skipping RAG indexing job for document {DocumentId}",
                documentId);
            return;
        }

        try
        {
            var tenantId = _configuration["TENANT_ID"] ?? _configuration["AzureAd:TenantId"] ?? "";

            var ragJob = new JobContract
            {
                JobId = Guid.NewGuid(),
                JobType = RagIndexingJobHandler.JobTypeName,
                SubjectId = documentId.ToString(),
                CorrelationId = Activity.Current?.Id ?? Guid.NewGuid().ToString(),
                IdempotencyKey = $"rag-index-{driveId}-{itemId}",
                Attempt = 1,
                MaxAttempts = 3,
                CreatedAt = DateTimeOffset.UtcNow,
                Payload = JsonDocument.Parse(JsonSerializer.Serialize(new RagIndexingJobPayload
                {
                    TenantId = tenantId,
                    DriveId = driveId,
                    ItemId = itemId,
                    FileName = fileName,
                    DocumentId = documentId.ToString(),
                    Source = "EmailAttachment",
                    EnqueuedAt = DateTimeOffset.UtcNow
                }))
            };

            await _jobSubmissionService.SubmitJobAsync(ragJob, ct);

            _telemetry.RecordRagJobEnqueued();

            _logger.LogInformation(
                "Enqueued RAG indexing job {JobId} for document {DocumentId} (file: {FileName})",
                ragJob.JobId, documentId, fileName);
        }
        catch (Exception ex)
        {
            // Log but don't fail - RAG indexing is non-critical
            _telemetry.RecordRagJobEnqueueFailure("enqueue_error");

            _logger.LogWarning(ex,
                "Failed to enqueue RAG indexing job for document {DocumentId}: {Error}. Email processing will continue.",
                documentId, ex.Message);
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
