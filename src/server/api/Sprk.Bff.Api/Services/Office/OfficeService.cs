using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using MimeKit;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Workers.Office;
using Sprk.Bff.Api.Workers.Office.Messages;
using Spaarke.Dataverse;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Implementation of <see cref="IOfficeService"/> for Office add-in operations.
/// </summary>
/// <remarks>
/// <para>
/// This service implements the Office add-in backend workflows:
/// - Task 021: Save endpoint (implemented) - creates ProcessingJob and returns tracking URLs
/// - Task 022: Job status endpoint (stub) - query Dataverse for job progress
/// - Task 023: SSE streaming (pending) - real-time job status updates
/// </para>
/// <para>
/// Per ADR-001, heavy processing (SPE upload, AI processing) is delegated to background workers.
/// This service focuses on fast job creation (target: &lt;3 seconds response time).
/// </para>
/// </remarks>
public class OfficeService : IOfficeService
{
    private readonly IJobStatusService _jobStatusService;
    private readonly IDataverseService _dataverseService;
    private readonly SpeFileStore _speFileStore;
    private readonly IGraphClientFactory _graphClientFactory;
    private readonly ServiceBusClient _serviceBusClient;
    private readonly ServiceBusOptions _serviceBusOptions;
    private readonly EmailProcessingOptions _emailProcessingOptions;
    private readonly ILogger<OfficeService> _logger;

    // In-memory job storage for development/testing (fallback when Dataverse unavailable)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, JobStatusResponse> _jobStore = new();

    // Queue names for Office workers
    private const string UploadFinalizationQueueName = "office-upload-finalization";

    public OfficeService(
        IJobStatusService jobStatusService,
        IDataverseService dataverseService,
        SpeFileStore speFileStore,
        IGraphClientFactory graphClientFactory,
        ServiceBusClient serviceBusClient,
        IOptions<ServiceBusOptions> serviceBusOptions,
        IOptions<EmailProcessingOptions> emailProcessingOptions,
        ILogger<OfficeService> logger)
    {
        _jobStatusService = jobStatusService;
        _dataverseService = dataverseService;
        _speFileStore = speFileStore;
        _graphClientFactory = graphClientFactory;
        _serviceBusClient = serviceBusClient;
        _serviceBusOptions = serviceBusOptions.Value;
        _emailProcessingOptions = emailProcessingOptions.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SaveResponse> SaveAsync(
        SaveRequest request,
        string userId,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Save requested for {ContentType} by user {UserId}",
            request.ContentType,
            userId);

        // DEBUG: Log email body info
        if (request.ContentType == SaveContentType.Email && request.Email != null)
        {
            _logger.LogInformation(
                "[EMAIL BODY DEBUG] Subject={Subject}, HasBody={HasBody}, BodyLength={BodyLength}, BodyPreview={BodyPreview}",
                request.Email.Subject,
                !string.IsNullOrEmpty(request.Email.Body),
                request.Email.Body?.Length ?? 0,
                request.Email.Body?.Substring(0, Math.Min(50, request.Email.Body?.Length ?? 0)) ?? "(empty)");
        }

        // Fetch email body and attachments from Graph API if missing
        if (request.ContentType == SaveContentType.Email && request.Email != null)
        {
            request = await EnrichEmailFromGraphAsync(request, httpContext, cancellationToken);
        }

        try
        {
            // Step 1: Generate or use provided idempotency key
            var idempotencyKey = request.IdempotencyKey ?? GenerateIdempotencyKey(request);

            // Step 2: Check for existing job with this idempotency key
            // TODO: Replace with actual Dataverse query for ProcessingJob by IdempotencyKey
            var existingJob = await CheckForExistingJobAsync(idempotencyKey, cancellationToken);

            if (existingJob is not null)
            {
                _logger.LogInformation(
                    "Duplicate save detected, returning existing job {JobId}",
                    existingJob.JobId);

                return new SaveResponse
                {
                    Success = true,
                    Duplicate = true,
                    JobId = existingJob.JobId,
                    StatusUrl = $"/office/jobs/{existingJob.JobId}",
                    StreamUrl = $"/office/jobs/{existingJob.JobId}/stream"
                };
            }

            // Step 3: Determine job type based on content type
            var jobType = request.ContentType switch
            {
                SaveContentType.Email => JobType.EmailSave,
                SaveContentType.Attachment => JobType.AttachmentSave,
                SaveContentType.Document => JobType.DocumentSave,
                _ => throw new ArgumentOutOfRangeException(nameof(request.ContentType))
            };

            // Step 4: Create a new ProcessingJob record in Dataverse
            var jobId = Guid.Empty;

            _logger.LogInformation(
                "Creating ProcessingJob for {ContentType} save with association {AssociationType}:{AssociationId}",
                request.ContentType,
                request.TargetEntity?.EntityType,
                request.TargetEntity?.EntityId);

            // Serialize the request payload for storage
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                ContentType = request.ContentType.ToString(),
                TargetEntity = request.TargetEntity,
                ContainerId = request.ContainerId,
                FolderPath = request.FolderPath,
                Email = request.Email,
                Attachment = request.Attachment,
                Document = request.Document,
                TriggerAiProcessing = request.TriggerAiProcessing
            });

            try
            {
                // Create ProcessingJob in Dataverse using existing IDataverseService
                jobId = await _dataverseService.CreateProcessingJobAsync(new
                {
                    Name = $"{request.ContentType} Save - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                    JobType = (int)jobType,
                    Status = 0, // Queued
                    Progress = 0,
                    IdempotencyKey = idempotencyKey,
                    CorrelationId = Guid.NewGuid().ToString(),
                    Payload = payload
                }, cancellationToken);

                _logger.LogInformation(
                    "ProcessingJob {JobId} created in Dataverse for {ContentType}",
                    jobId,
                    request.ContentType);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to create ProcessingJob in Dataverse, falling back to in-memory storage");

                // Fallback to in-memory for development/testing when Dataverse is unavailable
                jobId = Guid.NewGuid();
            }

            // Also store in-memory for job status polling (fast access)
            var correlationId = Guid.NewGuid().ToString();
            var jobRecord = new JobStatusResponse
            {
                JobId = jobId,
                Status = JobStatus.Queued,
                JobType = jobType,
                Progress = 0,
                CurrentPhase = "Queued",
                CompletedPhases = new List<CompletedPhase>(),
                CreatedAt = DateTimeOffset.UtcNow,
                CreatedBy = userId
            };
            _jobStore[jobId] = jobRecord;

            // Step 5: Upload content to SPE and queue finalization job
            // Following existing document flow per architecture docs:
            // 1. Create Document → 2. Upload to SPE → 3. Associate Document to SPE → 4. Trigger AI
            Stream? contentStream = null;
            string fileName;
            long fileSize = 0;

            try
            {
                // Build file content based on content type
                switch (request.ContentType)
                {
                    case SaveContentType.Email when request.Email != null:
                        // Build .eml file from email metadata using MimeKit
                        contentStream = BuildEmlFromMetadata(request.Email);
                        fileName = GenerateEmlFileName(request.Email);
                        fileSize = contentStream.Length;
                        break;

                    case SaveContentType.Attachment when request.Attachment != null:
                        // Decode base64 attachment content
                        if (!string.IsNullOrEmpty(request.Attachment.ContentBase64))
                        {
                            var bytes = Convert.FromBase64String(request.Attachment.ContentBase64);
                            contentStream = new MemoryStream(bytes);
                            fileSize = bytes.Length;
                        }
                        else
                        {
                            throw new InvalidOperationException("Attachment content is required for attachment saves");
                        }
                        fileName = request.Attachment.FileName;
                        break;

                    case SaveContentType.Document when request.Document != null:
                        // Decode base64 document content
                        if (!string.IsNullOrEmpty(request.Document.ContentBase64))
                        {
                            var bytes = Convert.FromBase64String(request.Document.ContentBase64);
                            contentStream = new MemoryStream(bytes);
                            fileSize = bytes.Length;
                        }
                        else
                        {
                            throw new InvalidOperationException("Document content is required for document saves");
                        }
                        fileName = request.Document.FileName;
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported content type: {request.ContentType}");
                }

                // Get the container ID (use provided or default from configuration)
                var containerId = request.ContainerId;
                if (string.IsNullOrEmpty(containerId))
                {
                    containerId = _emailProcessingOptions.DefaultContainerId;
                    if (string.IsNullOrEmpty(containerId))
                    {
                        throw new InvalidOperationException(
                            "ContainerId is required for save operations. Either provide ContainerId in request or configure EmailProcessing:DefaultContainerId.");
                    }
                    _logger.LogDebug("Using default container ID from configuration: {ContainerId}", containerId);
                }

                // Upload to SPE
                var (uploadSuccess, driveId, itemId, webUrl, uploadError) = await UploadToSpeAsync(
                    containerId,
                    request.FolderPath,
                    fileName,
                    contentStream,
                    cancellationToken);

                if (!uploadSuccess || string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId))
                {
                    // Update job status to failed
                    await UpdateJobStatusInDataverseAsync(jobId, JobStatus.Failed, "UploadFailed", 0, uploadError, cancellationToken);

                    return new SaveResponse
                    {
                        Success = false,
                        Error = new SaveError
                        {
                            Code = "OFFICE_012",
                            Message = "Failed to upload file to storage",
                            Details = uploadError,
                            Retryable = true
                        }
                    };
                }

                // Update job status to uploading complete
                await UpdateJobStatusInDataverseAsync(jobId, JobStatus.Running, "FileUploaded", 30, null, cancellationToken);
                _jobStore[jobId] = jobRecord with
                {
                    Status = JobStatus.Running,
                    Progress = 30,
                    CurrentPhase = "FileUploaded"
                };

                // Create Document record with SPE pointers
                var documentId = await CreateDocumentWithSpePointersAsync(
                    request,
                    driveId,
                    itemId,
                    fileName,
                    fileSize,
                    userId,
                    cancellationToken);

                // Update job status to records created
                await UpdateJobStatusInDataverseAsync(jobId, JobStatus.Running, "RecordsCreated", 50, null, cancellationToken);
                _jobStore[jobId] = _jobStore[jobId] with
                {
                    Progress = 50,
                    CurrentPhase = "RecordsCreated"
                };

                // ALWAYS queue finalization job - it creates EmailArtifact/AttachmentArtifact records
                // and optionally triggers AI processing based on TriggerAiProcessing flag in payload
                await QueueUploadFinalizationAsync(
                    jobId,
                    idempotencyKey,
                    correlationId,
                    userId,
                    request,
                    driveId,
                    itemId,
                    fileName,
                    fileSize,
                    documentId,
                    cancellationToken);

                // Mark job as complete - background workers will process asynchronously
                // User sees immediate success while AI processing continues in background
                await UpdateJobStatusInDataverseAsync(jobId, JobStatus.Completed, "Complete", 100, null, cancellationToken);
                _jobStore[jobId] = _jobStore[jobId] with
                {
                    Status = JobStatus.Completed,
                    Progress = 100,
                    CurrentPhase = "Complete",
                    CompletedAt = DateTimeOffset.UtcNow
                };

                _logger.LogInformation(
                    "ProcessingJob {JobId} completed, file uploaded to SPE, document {DocumentId} created. Background workers queued for finalization.",
                    jobId,
                    documentId);
            }
            finally
            {
                contentStream?.Dispose();
            }

            // Step 6: Return success response with job tracking URLs
            return new SaveResponse
            {
                Success = true,
                Duplicate = false,
                JobId = jobId,
                StatusUrl = $"/office/jobs/{jobId}",
                StreamUrl = $"/office/jobs/{jobId}/stream"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to create save job for {ContentType} by user {UserId}: {ErrorMessage}",
                request.ContentType,
                userId,
                ex.Message);

            // Include the actual exception message to aid debugging
            // In production, consider returning a generic message and logging details server-side only
            return new SaveResponse
            {
                Success = false,
                Error = new SaveError
                {
                    Code = "OFFICE_INTERNAL",
                    Message = $"Save failed: {ex.Message}",
                    Details = ex.ToString(), // Full stack trace for debugging
                    Retryable = true
                }
            };
        }
    }

    /// <summary>
    /// DEPRECATED: Simulates job progress for testing/development when Service Bus is unavailable.
    /// Production flow now uses actual SPE upload and Service Bus queueing via SaveAsync.
    /// Only use this for local development without Service Bus configured.
    /// </summary>
    [Obsolete("Use SaveAsync with Service Bus flow instead. This is only for local development without Service Bus.")]
    private async Task SimulateJobProgressAsync(Guid jobId, CancellationToken cancellationToken)
    {
        try
        {
            // Wait briefly then start "Running"
            await Task.Delay(500, cancellationToken);
            if (!_jobStore.TryGetValue(jobId, out var job)) return;

            job = job with
            {
                Status = JobStatus.Running,
                Progress = 10,
                CurrentPhase = "RecordsCreated",
                StartedAt = DateTimeOffset.UtcNow
            };
            _jobStore[jobId] = job;

            // Simulate RecordsCreated phase
            await Task.Delay(1000, cancellationToken);
            if (!_jobStore.TryGetValue(jobId, out job)) return;

            var completedPhases = new List<CompletedPhase>(job.CompletedPhases ?? new List<CompletedPhase>())
            {
                new CompletedPhase
                {
                    Name = "RecordsCreated",
                    CompletedAt = DateTimeOffset.UtcNow,
                    DurationMs = 1000
                }
            };

            job = job with
            {
                Progress = 30,
                CurrentPhase = "FileUploaded",
                CompletedPhases = completedPhases
            };
            _jobStore[jobId] = job;

            // Simulate FileUploaded phase
            await Task.Delay(1500, cancellationToken);
            if (!_jobStore.TryGetValue(jobId, out job)) return;

            completedPhases = new List<CompletedPhase>(job.CompletedPhases ?? new List<CompletedPhase>())
            {
                new CompletedPhase
                {
                    Name = "FileUploaded",
                    CompletedAt = DateTimeOffset.UtcNow,
                    DurationMs = 1500
                }
            };

            // Create actual Document record in Dataverse
            // In production, this would be done by the background worker after SPE upload
            Guid documentId;
            string documentUrl;

            try
            {
                var createRequest = new Spaarke.Dataverse.CreateDocumentRequest
                {
                    Name = $"Simulated Document - {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}",
                    ContainerId = "simulation", // Placeholder - no actual container
                    Description = "Document created by simulation (no actual file uploaded)"
                };

                var documentIdString = await _dataverseService.CreateDocumentAsync(createRequest, cancellationToken);
                documentId = Guid.Parse(documentIdString);

                _logger.LogInformation(
                    "Simulation created actual Document in Dataverse: {DocumentId}",
                    documentId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Simulation failed to create Document in Dataverse, using fake ID");
                documentId = Guid.NewGuid();
            }

            // Use correct Dataverse URL format for the document
            // AppId for Spaarke app in dev environment
            const string dataverseBaseUrl = "https://spaarkedev1.crm.dynamics.com";
            const string appId = "729afe6d-ca73-f011-b4cb-6045bdd8b757";
            documentUrl = $"{dataverseBaseUrl}/main.aspx?appid={appId}&pagetype=entityrecord&etn=sprk_document&id={documentId}";

            job = job with
            {
                Status = JobStatus.Completed,
                Progress = 100,
                CurrentPhase = "Complete",
                CompletedPhases = completedPhases,
                CompletedAt = DateTimeOffset.UtcNow,
                Result = new JobResult
                {
                    Artifact = new CreatedArtifact
                    {
                        Type = ArtifactType.Document,
                        Id = documentId,
                        WebUrl = documentUrl
                    }
                }
            };
            _jobStore[jobId] = job;

            _logger.LogInformation(
                "Simulated job {JobId} completed with document {DocumentId}",
                jobId,
                documentId);
        }
        catch (OperationCanceledException)
        {
            // Job was cancelled
            _logger.LogDebug("Simulated job {JobId} was cancelled", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error simulating job progress for {JobId}", jobId);

            // Mark job as failed
            if (_jobStore.TryGetValue(jobId, out var job))
            {
                job = job with
                {
                    Status = JobStatus.Failed,
                    Error = new JobError
                    {
                        Code = "OFFICE_INTERNAL",
                        Message = "Internal error during processing",
                        Retryable = true
                    }
                };
                _jobStore[jobId] = job;
            }
        }
    }

    /// <summary>
    /// Generates an idempotency key based on the request content.
    /// Uses SHA256 hash of the canonical payload.
    /// </summary>
    private static string GenerateIdempotencyKey(SaveRequest request)
    {
        // Create a canonical representation of the request for hashing
        var canonical = $"{request.ContentType}|" +
                       $"{request.TargetEntity?.EntityType}|" +
                       $"{request.TargetEntity?.EntityId}|" +
                       $"{request.Email?.InternetMessageId ?? request.Email?.Subject}|" +
                       $"{request.Attachment?.AttachmentId}|" +
                       $"{request.Document?.FileName}|" +
                       $"{request.Document?.ExistingDocumentId}";

        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hashBytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(canonical));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Builds an RFC 5322 compliant .eml file from Office add-in email metadata.
    /// Uses MimeKit for proper MIME message construction.
    /// </summary>
    private static Stream BuildEmlFromMetadata(EmailMetadata metadata)
    {
        var message = new MimeMessage();

        // Set sender
        message.From.Add(new MailboxAddress(metadata.SenderName ?? "", metadata.SenderEmail));

        // Set recipients
        if (metadata.Recipients != null)
        {
            foreach (var recipient in metadata.Recipients)
            {
                var mailbox = new MailboxAddress(recipient.Name ?? "", recipient.Email);
                switch (recipient.Type)
                {
                    case RecipientType.To:
                        message.To.Add(mailbox);
                        break;
                    case RecipientType.Cc:
                        message.Cc.Add(mailbox);
                        break;
                    case RecipientType.Bcc:
                        message.Bcc.Add(mailbox);
                        break;
                }
            }
        }

        // Set subject
        message.Subject = metadata.Subject;

        // Set dates
        if (metadata.SentDate.HasValue)
        {
            message.Date = metadata.SentDate.Value;
        }

        // Set message ID if it's a valid RFC 2822 Message-ID format
        // Note: Exchange item IDs (AAMkA...) are NOT valid Message-IDs
        // Valid format: <something@something> or just something@something
        if (!string.IsNullOrEmpty(metadata.InternetMessageId))
        {
            // Check if it looks like an RFC 2822 Message-ID (contains @ and doesn't look like base64)
            var msgId = metadata.InternetMessageId;
            if (msgId.Contains('@') && !msgId.StartsWith("AAMk", StringComparison.OrdinalIgnoreCase))
            {
                // Strip angle brackets if present, MimeKit will add them
                if (msgId.StartsWith("<") && msgId.EndsWith(">"))
                {
                    msgId = msgId[1..^1];
                }
                message.MessageId = msgId;
            }
            // If it's an Exchange item ID, store it in a custom header for reference
            else
            {
                message.Headers.Add("X-Exchange-Item-Id", metadata.InternetMessageId);
            }
        }

        // Build body with attachments
        var bodyBuilder = new BodyBuilder();
        if (metadata.IsBodyHtml && !string.IsNullOrEmpty(metadata.Body))
        {
            bodyBuilder.HtmlBody = metadata.Body;
        }
        else if (!string.IsNullOrEmpty(metadata.Body))
        {
            bodyBuilder.TextBody = metadata.Body;
        }

        // Add attachments from client-side content
        if (metadata.Attachments != null)
        {
            foreach (var attachment in metadata.Attachments)
            {
                if (string.IsNullOrEmpty(attachment.ContentBase64))
                {
                    continue; // Skip attachments without content
                }

                try
                {
                    var contentBytes = Convert.FromBase64String(attachment.ContentBase64);
                    var contentType = MimeKit.ContentType.Parse(attachment.ContentType ?? "application/octet-stream");

                    if (attachment.IsInline && !string.IsNullOrEmpty(attachment.ContentId))
                    {
                        // Inline attachment (embedded image in HTML body)
                        var linkedResource = bodyBuilder.LinkedResources.Add(
                            attachment.FileName,
                            contentBytes,
                            contentType);
                        linkedResource.ContentId = attachment.ContentId;
                    }
                    else
                    {
                        // Regular attachment
                        bodyBuilder.Attachments.Add(
                            attachment.FileName,
                            contentBytes,
                            contentType);
                    }
                }
                catch (FormatException)
                {
                    // Skip invalid base64 content
                }
            }
        }

        message.Body = bodyBuilder.ToMessageBody();

        // Write to stream
        var stream = new MemoryStream();
        message.WriteTo(stream);
        stream.Position = 0;
        return stream;
    }

    /// <summary>
    /// Generates a sanitized filename for the .eml file.
    /// Format: YYYY-MM-DD_Subject.eml (max 100 chars, special chars removed)
    /// </summary>
    private static string GenerateEmlFileName(EmailMetadata metadata)
    {
        var datePrefix = metadata.SentDate?.ToString("yyyy-MM-dd") ?? DateTime.UtcNow.ToString("yyyy-MM-dd");
        var sanitizedSubject = SanitizeFileName(metadata.Subject);

        // Limit subject to 80 chars to leave room for date and extension
        if (sanitizedSubject.Length > 80)
        {
            sanitizedSubject = sanitizedSubject[..80];
        }

        return $"{datePrefix}_{sanitizedSubject}.eml";
    }

    /// <summary>
    /// Sanitizes a string for use as a filename.
    /// </summary>
    private static string SanitizeFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "untitled";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "untitled" : sanitized.Trim();
    }

    /// <summary>
    /// Uploads content to SPE and returns the DriveId and ItemId.
    /// </summary>
    private async Task<(bool Success, string? DriveId, string? ItemId, string? WebUrl, string? Error)> UploadToSpeAsync(
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

            // Build the full path
            var path = string.IsNullOrEmpty(folderPath)
                ? fileName
                : $"{folderPath.TrimEnd('/')}/{fileName}";

            // Upload using SpeFileStore (ADR-007)
            var result = await _speFileStore.UploadSmallAsync(driveId, path, content, cancellationToken);

            if (result != null)
            {
                _logger.LogInformation(
                    "File uploaded to SPE: DriveId={DriveId}, ItemId={ItemId}",
                    driveId,
                    result.Id);

                return (true, driveId, result.Id, result.WebUrl, null);
            }

            return (false, null, null, null, "Upload returned null result");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SPE upload failed for {FileName}", fileName);
            return (false, null, null, null, ex.Message);
        }
    }

    /// <summary>
    /// Enriches email metadata by fetching body and attachments from Graph API when missing.
    /// Uses OBO authentication to access user's mailbox via Microsoft Graph.
    /// </summary>
    /// <param name="request">Save request to enrich with email content.</param>
    /// <param name="httpContext">HTTP context for OBO authentication.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated save request with email body and attachments from Graph API.</returns>
    private async Task<SaveRequest> EnrichEmailFromGraphAsync(
        SaveRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        // Only fetch if body is missing and we have an internet message ID
        if (!string.IsNullOrEmpty(request.Email?.Body) || string.IsNullOrEmpty(request.Email?.InternetMessageId))
        {
            return request; // Body already present or no message ID
        }

        try
        {
            _logger.LogInformation(
                "Fetching email content from Graph API for message {MessageId}",
                request.Email.InternetMessageId);

            // Get Graph client with OBO auth
            var graphClient = await _graphClientFactory.ForUserAsync(httpContext, cancellationToken);

            // Fetch message with body and attachments
            var message = await graphClient.Me.Messages[request.Email.InternetMessageId]
                .GetAsync(requestConfig =>
                {
                    requestConfig.QueryParameters.Select = new[]
                    {
                        "body",
                        "subject",
                        "from",
                        "toRecipients",
                        "ccRecipients",
                        "bccRecipients",
                        "hasAttachments",
                        "internetMessageId",
                        "sentDateTime"
                    };
                    requestConfig.QueryParameters.Expand = new[] { "attachments" };
                }, cancellationToken);

            if (message == null)
            {
                _logger.LogWarning(
                    "Graph API returned null message for {MessageId}",
                    request.Email.InternetMessageId);
                return request; // Graph API returned null, return original request
            }

            // Extract body content
            string? bodyContent = null;
            bool isBodyHtml = false;
            if (message.Body != null && !string.IsNullOrEmpty(message.Body.Content))
            {
                bodyContent = message.Body.Content;
                isBodyHtml = message.Body.ContentType == Microsoft.Graph.Models.BodyType.Html;

                _logger.LogInformation(
                    "Retrieved email body from Graph API: Length={BodyLength}, IsHtml={IsHtml}",
                    bodyContent.Length,
                    isBodyHtml);
            }

            // Extract attachments if present
            List<Models.Office.AttachmentReference>? attachmentReferences = null;
            if (message.HasAttachments == true && message.Attachments?.Any() == true)
            {
                attachmentReferences = new List<Models.Office.AttachmentReference>();

                foreach (var attachment in message.Attachments)
                {
                    if (attachment is FileAttachment fileAttachment && fileAttachment.ContentBytes != null)
                    {
                        var contentBase64 = Convert.ToBase64String(fileAttachment.ContentBytes);

                        attachmentReferences.Add(new Models.Office.AttachmentReference
                        {
                            AttachmentId = attachment.Id ?? Guid.NewGuid().ToString(),
                            FileName = fileAttachment.Name ?? "attachment",
                            Size = fileAttachment.Size,
                            ContentType = fileAttachment.ContentType ?? "application/octet-stream",
                            ContentBase64 = contentBase64,
                            IsInline = fileAttachment.IsInline ?? false,
                            ContentId = fileAttachment.ContentId
                        });
                    }
                }

                _logger.LogInformation(
                    "Retrieved {AttachmentCount} attachments from Graph API for message {MessageId}",
                    attachmentReferences.Count,
                    request.Email.InternetMessageId);
            }

            // Create updated email metadata with Graph API content
            // EmailMetadata is a record with init-only properties, so we need to create a new instance
            if (bodyContent != null || attachmentReferences != null)
            {
                return request with
                {
                    Email = request.Email with
                    {
                        Body = bodyContent ?? request.Email.Body,
                        IsBodyHtml = bodyContent != null ? isBodyHtml : request.Email.IsBodyHtml,
                        Attachments = attachmentReferences ?? request.Email.Attachments
                    }
                };
            }

            return request; // No updates needed
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to fetch email content from Graph API for message {MessageId}",
                request.Email?.InternetMessageId);

            // Don't throw - continue with whatever content we have from the client
            // This allows fallback to client-provided data if Graph API fails
            return request;
        }
    }

    /// <summary>
    /// Queues a job to the Service Bus for background processing.
    /// </summary>
    private async Task QueueUploadFinalizationAsync(
        Guid jobId,
        string idempotencyKey,
        string correlationId,
        string userId,
        SaveRequest request,
        string driveId,
        string itemId,
        string fileName,
        long fileSize,
        Guid documentId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Queueing upload finalization job {JobId} for {ContentType}",
            jobId,
            request.ContentType);

        // Build the payload for the worker
        var payload = new UploadFinalizationPayload
        {
            ContentType = request.ContentType,
            AssociationType = request.TargetEntity?.EntityType,
            AssociationId = request.TargetEntity?.EntityId,
            ContainerId = driveId, // Use resolved drive ID
            FolderPath = request.FolderPath,
            TempFileLocation = $"spe://{driveId}/{itemId}", // Reference the already-uploaded SPE file
            FileName = fileName,
            FileSize = fileSize,
            MimeType = GetMimeType(request),
            TriggerAiProcessing = request.TriggerAiProcessing,
            EmailMetadata = request.ContentType == SaveContentType.Email && request.Email != null
                ? new EmailArtifactPayload
                {
                    InternetMessageId = request.Email.InternetMessageId,
                    ConversationId = request.Email.ConversationId,
                    Subject = request.Email.Subject,
                    SenderEmail = request.Email.SenderEmail,
                    SenderName = request.Email.SenderName,
                    RecipientsJson = request.Email.Recipients != null
                        ? JsonSerializer.Serialize(request.Email.Recipients)
                        : null,
                    SentDate = request.Email.SentDate,
                    ReceivedDate = request.Email.ReceivedDate,
                    BodyPreview = request.Email.Body?[..Math.Min(request.Email.Body.Length, 500)],
                    HasAttachments = request.Email.Attachments?.Count > 0,
                    Importance = 1, // Normal
                    SelectedAttachmentFileNames = request.Email.SelectedAttachmentFileNames
                }
                : null,
            AttachmentMetadata = request.ContentType == SaveContentType.Attachment && request.Attachment != null
                ? new AttachmentArtifactPayload
                {
                    OutlookAttachmentId = request.Attachment.AttachmentId,
                    OriginalFileName = request.Attachment.FileName,
                    ContentType = request.Attachment.ContentType,
                    Size = request.Attachment.Size ?? 0,
                    IsInline = false
                }
                : null,
            AiOptions = request.AiOptions != null
                ? new AiProcessingOptions
                {
                    ProfileSummary = request.AiOptions.ProfileSummary,
                    RagIndex = request.AiOptions.RagIndex,
                    DeepAnalysis = request.AiOptions.DeepAnalysis
                }
                : new AiProcessingOptions
                {
                    ProfileSummary = request.TriggerAiProcessing,
                    RagIndex = request.TriggerAiProcessing,
                    DeepAnalysis = false
                },
            DocumentId = documentId
        };

        // Create the job message
        var message = new OfficeJobMessage
        {
            JobId = jobId,
            JobType = OfficeJobType.UploadFinalization,
            SubjectId = itemId,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            Attempt = 1,
            MaxAttempts = 3,
            UserId = userId,
            Payload = JsonSerializer.SerializeToElement(payload)
        };

        // Send to Service Bus
        var sender = _serviceBusClient.CreateSender(UploadFinalizationQueueName);

        var sbMessage = new ServiceBusMessage(JsonSerializer.Serialize(message))
        {
            MessageId = jobId.ToString(),
            CorrelationId = correlationId,
            ContentType = "application/json",
            Subject = OfficeJobType.UploadFinalization.ToString(),
            ApplicationProperties =
            {
                ["JobType"] = OfficeJobType.UploadFinalization.ToString(),
                ["Attempt"] = 1,
                ["UserId"] = userId,
                ["ContentType"] = request.ContentType.ToString()
            }
        };

        await sender.SendMessageAsync(sbMessage, cancellationToken);
        await sender.DisposeAsync();

        _logger.LogInformation(
            "Upload finalization job {JobId} queued to Service Bus for {ContentType}",
            jobId,
            request.ContentType);
    }

    /// <summary>
    /// Gets the MIME type for the save request content.
    /// </summary>
    private static string GetMimeType(SaveRequest request) => request.ContentType switch
    {
        SaveContentType.Email => "message/rfc822",
        SaveContentType.Attachment => request.Attachment?.ContentType ?? "application/octet-stream",
        SaveContentType.Document => request.Document?.ContentType ?? "application/octet-stream",
        _ => "application/octet-stream"
    };

    /// <summary>
    /// Updates ProcessingJob status in Dataverse.
    /// </summary>
    private async Task UpdateJobStatusInDataverseAsync(
        Guid jobId,
        JobStatus status,
        string phase,
        int progress,
        string? errorMessage,
        CancellationToken cancellationToken)
    {
        try
        {
            var dataverseStatus = status switch
            {
                JobStatus.Queued => 0,
                JobStatus.Running => 1,
                JobStatus.Completed => 2,
                JobStatus.Failed => 3,
                JobStatus.Cancelled => 4,
                _ => 1
            };

            await _dataverseService.UpdateProcessingJobAsync(jobId, new
            {
                Status = dataverseStatus,
                Progress = progress,
                CurrentStage = phase,
                ErrorMessage = errorMessage,
                CompletedDate = status is JobStatus.Completed or JobStatus.Failed
                    ? DateTime.UtcNow
                    : (DateTime?)null
            }, cancellationToken);

            _logger.LogDebug(
                "ProcessingJob {JobId} status updated: {Status}, {Phase}, {Progress}%",
                jobId, status, phase, progress);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to update ProcessingJob {JobId} status in Dataverse", jobId);
        }
    }

    /// <summary>
    /// Creates a Document record in Dataverse with SPE pointers.
    /// </summary>
    private async Task<Guid> CreateDocumentWithSpePointersAsync(
        SaveRequest request,
        string driveId,
        string itemId,
        string fileName,
        long fileSize,
        string userId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Creating Document record with SPE pointers: DriveId={DriveId}, ItemId={ItemId}",
            driveId, itemId);

        // Create base document record
        var createRequest = new Spaarke.Dataverse.CreateDocumentRequest
        {
            Name = fileName,
            ContainerId = driveId,
            Description = request.ContentType switch
            {
                SaveContentType.Email => request.Email?.Subject,
                SaveContentType.Attachment => $"Attachment: {request.Attachment?.FileName}",
                SaveContentType.Document => request.Document?.Title ?? request.Document?.FileName,
                _ => null
            }
        };

        var documentIdString = await _dataverseService.CreateDocumentAsync(createRequest, cancellationToken);
        var documentId = Guid.Parse(documentIdString);

        // Update with SPE pointers and additional metadata
        var updateRequest = new Spaarke.Dataverse.UpdateDocumentRequest
        {
            GraphDriveId = driveId,
            GraphItemId = itemId,
            FileName = fileName,
            FileSize = fileSize,
            MimeType = GetMimeType(request),
            HasFile = true,
            FilePath = request.FolderPath  // Maps to sprk_filepath in Dataverse
        };

        // Set entity association lookup based on target entity
        if (request.TargetEntity != null)
        {
            switch (request.TargetEntity.EntityType?.ToLowerInvariant())
            {
                case "matter":
                case "sprk_matter":
                    updateRequest.MatterLookup = request.TargetEntity.EntityId;
                    break;
                case "project":
                case "sprk_project":
                    updateRequest.ProjectLookup = request.TargetEntity.EntityId;
                    break;
                case "invoice":
                case "sprk_invoice":
                    updateRequest.InvoiceLookup = request.TargetEntity.EntityId;
                    break;
                default:
                    _logger.LogWarning(
                        "Unknown target entity type {EntityType}, skipping association",
                        request.TargetEntity.EntityType);
                    break;
            }
        }

        // Set email-specific fields
        if (request.ContentType == SaveContentType.Email && request.Email != null)
        {
            updateRequest.EmailSubject = request.Email.Subject;
            updateRequest.EmailFrom = request.Email.SenderEmail;
            updateRequest.EmailTo = request.Email.Recipients != null
                ? JsonSerializer.Serialize(request.Email.Recipients)
                : null;
            updateRequest.EmailDate = request.Email.SentDate?.DateTime;
            updateRequest.EmailBody = request.Email.Body?[..Math.Min(request.Email.Body?.Length ?? 0, 2000)];
            updateRequest.EmailMessageId = request.Email.InternetMessageId;
            updateRequest.EmailConversationIndex = request.Email.ConversationId;
            updateRequest.IsEmailArchive = true;
        }

        await _dataverseService.UpdateDocumentAsync(documentIdString, updateRequest, cancellationToken);

        _logger.LogInformation(
            "Document record created: DocumentId={DocumentId}, DriveId={DriveId}, ItemId={ItemId}",
            documentId, driveId, itemId);

        return documentId;
    }

    /// <summary>
    /// Generates a Dataverse URL for a document record.
    /// </summary>
    private static string GenerateDataverseUrl(Guid documentId)
    {
        const string dataverseBaseUrl = "https://spaarkedev1.crm.dynamics.com";
        const string appId = "729afe6d-ca73-f011-b4cb-6045bdd8b757";
        return $"{dataverseBaseUrl}/main.aspx?appid={appId}&pagetype=entityrecord&etn=sprk_document&id={documentId}";
    }

    /// <summary>
    /// Checks for an existing ProcessingJob with the given idempotency key.
    /// Uses IDataverseService to query for existing jobs.
    /// </summary>
    private async Task<JobStatusResponse?> CheckForExistingJobAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking for existing job with idempotency key");

        try
        {
            var existingJob = await _dataverseService.GetProcessingJobByIdempotencyKeyAsync(
                idempotencyKey,
                cancellationToken);

            if (existingJob == null)
            {
                return null;
            }

            // Map the dynamic result to JobStatusResponse
            // The Dataverse service returns an anonymous type with: Id, Name, JobType, Status, Progress, IdempotencyKey, CorrelationId
            dynamic job = existingJob;

            var status = MapDataverseStatusToJobStatus((int?)job.Status);
            var jobType = MapDataverseJobTypeToJobType((int?)job.JobType);

            _logger.LogInformation(
                "Found existing job {JobId} with idempotency key, status: {Status}",
                (Guid)job.Id,
                status);

            return new JobStatusResponse
            {
                JobId = (Guid)job.Id,
                Status = status,
                JobType = jobType,
                Progress = (int?)job.Progress ?? 0,
                CurrentPhase = null, // Not stored in ProcessingJob
                CompletedPhases = new List<CompletedPhase>(),
                CreatedAt = DateTimeOffset.UtcNow, // Not returned by query
                CreatedBy = null
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error checking for existing job by idempotency key, treating as no duplicate");
            return null;
        }
    }

    /// <summary>
    /// Maps Dataverse ProcessingJob status option set value to JobStatus enum.
    /// </summary>
    private static JobStatus MapDataverseStatusToJobStatus(int? statusValue)
    {
        // ProcessingJob status option set values:
        // 0 = Pending/Queued, 1 = InProgress/Running, 2 = Completed, 3 = Failed
        return statusValue switch
        {
            0 => JobStatus.Queued,
            1 => JobStatus.Running,
            2 => JobStatus.Completed,
            3 => JobStatus.Failed,
            4 => JobStatus.Cancelled,
            _ => JobStatus.Queued
        };
    }

    /// <summary>
    /// Maps Dataverse ProcessingJob job type option set value to JobType enum.
    /// </summary>
    private static JobType MapDataverseJobTypeToJobType(int? jobTypeValue)
    {
        // ProcessingJob job type option set values:
        // 0 = DocumentSave, 1 = EmailSave, 2 = AttachmentSave, 3 = AiProcessing, 4 = Indexing
        return jobTypeValue switch
        {
            0 => JobType.DocumentSave,
            1 => JobType.EmailSave,
            2 => JobType.AttachmentSave,
            3 => JobType.AiProcessing,
            4 => JobType.Indexing,
            _ => JobType.DocumentSave
        };
    }

    /// <inheritdoc />
    public async Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Job status requested for {JobId} by user {UserId}",
            jobId,
            userId);

        // Look up job in in-memory store
        // TODO: Replace with actual Dataverse query once ProcessingJob table exists
        if (_jobStore.TryGetValue(jobId, out var job))
        {
            _logger.LogDebug(
                "Job {JobId} found in store: Status={Status}, Progress={Progress}",
                jobId,
                job.Status,
                job.Progress);

            // Optionally verify ownership (if userId is provided)
            if (userId is not null && job.CreatedBy is not null && job.CreatedBy != userId)
            {
                _logger.LogWarning(
                    "Job {JobId} ownership mismatch: Expected {ExpectedUser}, Got {ActualUser}",
                    jobId,
                    job.CreatedBy,
                    userId);
                return null; // Treat as not found for security
            }

            await Task.CompletedTask; // Keep method async for future Dataverse calls
            return job;
        }

        // Also check for hardcoded test job ID for backwards compatibility
        var testJobId = Guid.Parse("00000000-0000-0000-0000-000000000001");
        if (jobId == testJobId)
        {
            return new JobStatusResponse
            {
                JobId = jobId,
                Status = JobStatus.Running,
                JobType = JobType.EmailSave,
                Progress = 50,
                CurrentPhase = "FileUploaded",
                CompletedPhases = new List<CompletedPhase>
                {
                    new CompletedPhase
                    {
                        Name = "RecordsCreated",
                        CompletedAt = DateTimeOffset.UtcNow.AddSeconds(-5),
                        DurationMs = 250
                    },
                    new CompletedPhase
                    {
                        Name = "FileUploaded",
                        CompletedAt = DateTimeOffset.UtcNow.AddSeconds(-2),
                        DurationMs = 1500
                    }
                },
                CreatedAt = DateTimeOffset.UtcNow.AddSeconds(-10),
                CreatedBy = userId,
                StartedAt = DateTimeOffset.UtcNow.AddSeconds(-8)
            };
        }

        // Job not found in memory - query Dataverse
        _logger.LogDebug("Job {JobId} not found in memory store, querying Dataverse", jobId);

        try
        {
            var processingJob = await _dataverseService.GetProcessingJobAsync(jobId, cancellationToken);
            if (processingJob != null)
            {
                // Map Dataverse ProcessingJob to JobStatusResponse
                // ProcessingJob fields: Id, Name, JobType, Status, Progress, IdempotencyKey, CorrelationId
                dynamic dvJob = processingJob;

                // Map Dataverse status values to JobStatus enum
                // Dataverse: 1 = Running, 2 = Completed, 3 = Failed, 4 = Cancelled
                var dvStatus = (int?)dvJob.Status ?? 1;
                var status = dvStatus switch
                {
                    1 => JobStatus.Running,
                    2 => JobStatus.Completed,
                    3 => JobStatus.Failed,
                    4 => JobStatus.Cancelled,
                    _ => JobStatus.Running
                };

                var isCompleted = status == JobStatus.Completed;
                var response = new JobStatusResponse
                {
                    JobId = jobId,
                    Status = status,
                    JobType = JobType.EmailSave, // Default to EmailSave for Office jobs
                    Progress = isCompleted ? 100 : ((int?)dvJob.Progress ?? 0),
                    CurrentPhase = isCompleted ? "Complete" : "Processing",
                    CreatedAt = DateTimeOffset.UtcNow, // Not stored in Dataverse yet
                    CompletedAt = isCompleted ? DateTimeOffset.UtcNow : null
                };

                _logger.LogInformation(
                    "Job {JobId} found in Dataverse: Status={Status}, Progress={Progress}",
                    jobId,
                    response.Status,
                    response.Progress);

                return response;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to query Dataverse for job {JobId}, returning not found",
                jobId);
        }

        // Job not found in Dataverse either
        _logger.LogDebug("Job {JobId} not found in Dataverse", jobId);
        return null;
    }

    /// <inheritdoc />
    public Task<JobStatusResponse?> GetJobStatusAsync(
        Guid jobId,
        CancellationToken cancellationToken = default)
    {
        // Delegate to the main method without ownership validation
        // This overload is used by authorization filters to verify job existence
        return GetJobStatusAsync(jobId, userId: null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
    {
        // Basic health check - always returns true for now
        // Will be expanded to check dependencies (Dataverse, SPE, etc.)
        return Task.FromResult(true);
    }

    /// <inheritdoc />
    public async Task<EntitySearchResponse> SearchEntitiesAsync(
        EntitySearchRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Entity search requested: Query='{Query}', Types={EntityTypes}, Skip={Skip}, Top={Top}, User={UserId}",
            request.Query,
            request.EntityTypes != null ? string.Join(",", request.EntityTypes) : "all",
            request.Skip,
            request.Top,
            userId);

        // Determine which entity types to search
        var typesToSearch = GetEntityTypesToSearch(request.EntityTypes);

        // TODO: Replace with actual Dataverse queries once tables exist
        // The implementation should:
        // 1. Build FetchXML queries for each entity type with 'contains' filter on name fields
        // 2. Execute queries in parallel for performance
        // 3. Combine and sort results by relevance + recency
        // 4. Apply pagination (skip/top) to combined results
        // 5. Filter by user permissions (Dataverse handles this via security roles)

        // For now, return stub data for testing the endpoint structure
        var results = GenerateStubResults(request.Query, typesToSearch, request.Top);
        var totalCount = results.Count + (request.Skip > 0 ? request.Skip : 0);

        await Task.CompletedTask; // Simulate async operation

        return new EntitySearchResponse
        {
            Results = results.Skip(request.Skip).Take(request.Top).ToList(),
            TotalCount = totalCount,
            HasMore = totalCount > request.Skip + request.Top
        };
    }

    /// <summary>
    /// Determines which entity types to search based on the request.
    /// </summary>
    private static HashSet<AssociationEntityType> GetEntityTypesToSearch(string[]? requestedTypes)
    {
        // If no types specified, search all
        if (requestedTypes == null || requestedTypes.Length == 0)
        {
            return new HashSet<AssociationEntityType>(Enum.GetValues<AssociationEntityType>());
        }

        var typesToSearch = new HashSet<AssociationEntityType>();
        foreach (var typeStr in requestedTypes)
        {
            if (Enum.TryParse<AssociationEntityType>(typeStr, ignoreCase: true, out var entityType))
            {
                typesToSearch.Add(entityType);
            }
        }

        // If no valid types were specified, search all
        return typesToSearch.Count > 0
            ? typesToSearch
            : new HashSet<AssociationEntityType>(Enum.GetValues<AssociationEntityType>());
    }

    /// <summary>
    /// Generates stub results for testing. Will be replaced with actual Dataverse queries.
    /// </summary>
    private static List<EntitySearchResult> GenerateStubResults(
        string query,
        HashSet<AssociationEntityType> entityTypes,
        int maxResults)
    {
        var results = new List<EntitySearchResult>();
        var queryLower = query.ToLowerInvariant();

        // Generate test data that matches the query
        var testData = new[]
        {
            new { Type = AssociationEntityType.Matter, Name = "Smith vs Jones Matter", Info = "Client: Acme Corp | Status: Active", Primary = "SMJ-2024-001" },
            new { Type = AssociationEntityType.Matter, Name = "Acme Contract Dispute", Info = "Client: Acme Corp | Status: Open", Primary = "ACD-2024-002" },
            new { Type = AssociationEntityType.Project, Name = "Acme Implementation Project", Info = "Phase: Development | Due: 2026-06-01", Primary = "PROJ-001" },
            new { Type = AssociationEntityType.Project, Name = "Smith Foundation Audit", Info = "Phase: Planning | Due: 2026-03-15", Primary = "PROJ-002" },
            new { Type = AssociationEntityType.Invoice, Name = "INV-2024-0001", Info = "Amount: $15,000 | Status: Pending", Primary = "Acme Corp" },
            new { Type = AssociationEntityType.Invoice, Name = "INV-2024-0002", Info = "Amount: $8,500 | Status: Paid", Primary = "Smith Foundation" },
            new { Type = AssociationEntityType.Account, Name = "Acme Corporation", Info = "Industry: Manufacturing | City: Chicago", Primary = "acme@acmecorp.com" },
            new { Type = AssociationEntityType.Account, Name = "Smith Foundation", Info = "Industry: Non-Profit | City: Boston", Primary = "info@smithfoundation.org" },
            new { Type = AssociationEntityType.Contact, Name = "John Smith", Info = "Company: Acme Corp | Title: CEO", Primary = "john.smith@acmecorp.com" },
            new { Type = AssociationEntityType.Contact, Name = "Jane Acme", Info = "Company: Acme Corp | Title: CFO", Primary = "jane.acme@acmecorp.com" }
        };

        foreach (var item in testData)
        {
            // Only include if type is requested
            if (!entityTypes.Contains(item.Type))
                continue;

            // Only include if query matches name, info, or primary field
            var matchesQuery = item.Name.ToLowerInvariant().Contains(queryLower) ||
                               item.Info.ToLowerInvariant().Contains(queryLower) ||
                               item.Primary.ToLowerInvariant().Contains(queryLower);

            if (!matchesQuery)
                continue;

            results.Add(new EntitySearchResult
            {
                Id = Guid.NewGuid(),
                EntityType = item.Type,
                LogicalName = GetLogicalName(item.Type),
                Name = item.Name,
                DisplayInfo = item.Info,
                PrimaryField = item.Primary,
                IconUrl = $"/icons/{item.Type.ToString().ToLowerInvariant()}.svg",
                ModifiedOn = DateTimeOffset.UtcNow.AddDays(-Random.Shared.Next(1, 30))
            });

            if (results.Count >= maxResults)
                break;
        }

        // Sort by relevance (exact match first) then by recency
        return results
            .OrderByDescending(r => r.Name.ToLowerInvariant().StartsWith(queryLower))
            .ThenByDescending(r => r.ModifiedOn)
            .ToList();
    }

    /// <summary>
    /// Gets the Dataverse logical name for an entity type.
    /// </summary>
    private static string GetLogicalName(AssociationEntityType entityType) => entityType switch
    {
        AssociationEntityType.Matter => "sprk_matter",
        AssociationEntityType.Project => "sprk_project",
        AssociationEntityType.Invoice => "sprk_invoice",
        AssociationEntityType.Account => "account",
        AssociationEntityType.Contact => "contact",
        _ => throw new ArgumentOutOfRangeException(nameof(entityType))
    };

    /// <inheritdoc />
    public async Task<DocumentSearchResponse> SearchDocumentsAsync(
        DocumentSearchRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Document search requested: Query='{Query}', EntityType={EntityType}, EntityId={EntityId}, ContentType={ContentType}, Skip={Skip}, Top={Top}, User={UserId}",
            request.Query,
            request.EntityType?.ToString() ?? "any",
            request.EntityId?.ToString() ?? "none",
            request.ContentType ?? "any",
            request.Skip,
            request.Top,
            userId);

        // TODO: Replace with actual Dataverse/SpeFileStore queries once document entity exists
        // The implementation should:
        // 1. Build FetchXML query for sprk_document with filters:
        //    - Name/filename contains query (case-insensitive)
        //    - sprk_matter/sprk_project/etc. filter by EntityType + EntityId
        //    - sprk_contenttype contains ContentType if specified
        //    - modifiedon date range if ModifiedAfter/ModifiedBefore specified
        //    - sprk_graphdriveid = ContainerId if specified
        // 2. Check user permissions via Dataverse security roles
        // 3. Get thumbnail URLs from SPE via SpeFileStore (batch Graph API call)
        // 4. Determine CanShare for each document based on user's permissions
        // 5. Map to DocumentSearchResult DTOs

        // Generate stub data for testing the endpoint structure
        var results = GenerateStubDocumentResults(request);
        var totalCount = results.Count + (request.Skip > 0 ? request.Skip : 0);

        await Task.CompletedTask; // Simulate async operation

        return new DocumentSearchResponse
        {
            Results = results.Skip(request.Skip).Take(request.Top).ToList(),
            TotalCount = totalCount,
            HasMore = totalCount > request.Skip + request.Top
        };
    }

    /// <summary>
    /// Generates stub document results for testing. Will be replaced with actual Dataverse/SPE queries.
    /// </summary>
    private static List<DocumentSearchResult> GenerateStubDocumentResults(DocumentSearchRequest request)
    {
        var results = new List<DocumentSearchResult>();
        var queryLower = request.Query.ToLowerInvariant();

        // Generate test data that matches the query
        var testDocuments = new[]
        {
            new
            {
                Name = "Contract Agreement v2",
                FileName = "Contract Agreement v2.docx",
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Size = 245678L,
                AssocType = AssociationEntityType.Matter,
                AssocName = "Smith vs Jones",
                Description = "Final version of the service contract",
                ModifiedBy = "John Doe"
            },
            new
            {
                Name = "Financial Report Q4",
                FileName = "Financial Report Q4 2025.xlsx",
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                Size = 1024567L,
                AssocType = AssociationEntityType.Account,
                AssocName = "Acme Corporation",
                Description = "Q4 2025 financial summary",
                ModifiedBy = "Jane Smith"
            },
            new
            {
                Name = "Project Proposal",
                FileName = "Project Proposal - Acme.pdf",
                ContentType = "application/pdf",
                Size = 512000L,
                AssocType = AssociationEntityType.Project,
                AssocName = "Acme Implementation Project",
                Description = "Initial project proposal document",
                ModifiedBy = "Bob Wilson"
            },
            new
            {
                Name = "Invoice INV-2024-0001",
                FileName = "Invoice INV-2024-0001.pdf",
                ContentType = "application/pdf",
                Size = 89000L,
                AssocType = AssociationEntityType.Invoice,
                AssocName = "INV-2024-0001",
                Description = "Invoice for consulting services",
                ModifiedBy = "Jane Smith"
            },
            new
            {
                Name = "Meeting Notes",
                FileName = "Meeting Notes 2026-01-15.docx",
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                Size = 45000L,
                AssocType = AssociationEntityType.Contact,
                AssocName = "John Smith",
                Description = "Notes from client meeting",
                ModifiedBy = "John Doe"
            }
        };

        var baseDate = DateTimeOffset.UtcNow;
        var index = 0;

        foreach (var doc in testDocuments)
        {
            // Apply query filter - search name, filename, description
            var matchesQuery = doc.Name.ToLowerInvariant().Contains(queryLower) ||
                               doc.FileName.ToLowerInvariant().Contains(queryLower) ||
                               doc.Description.ToLowerInvariant().Contains(queryLower);

            if (!matchesQuery)
                continue;

            // Apply EntityType filter
            if (request.EntityType.HasValue && doc.AssocType != request.EntityType.Value)
                continue;

            // Apply ContentType filter (partial match)
            if (!string.IsNullOrEmpty(request.ContentType) &&
                !doc.ContentType.Contains(request.ContentType, StringComparison.OrdinalIgnoreCase))
                continue;

            var documentId = Guid.NewGuid();
            var modifiedDate = baseDate.AddDays(-index - 1);

            // Apply date range filters
            if (request.ModifiedAfter.HasValue && modifiedDate < request.ModifiedAfter.Value)
                continue;

            if (request.ModifiedBefore.HasValue && modifiedDate > request.ModifiedBefore.Value)
                continue;

            results.Add(new DocumentSearchResult
            {
                Id = documentId,
                Name = doc.Name,
                FileName = doc.FileName,
                WebUrl = $"https://spaarke.com/documents/{documentId}",
                ContentType = doc.ContentType,
                Size = doc.Size,
                ModifiedDate = modifiedDate,
                ModifiedBy = doc.ModifiedBy,
                ThumbnailUrl = null, // Thumbnails would be fetched from SPE in real implementation
                AssociationType = doc.AssocType,
                AssociationId = Guid.NewGuid(),
                AssociationName = doc.AssocName,
                ContainerId = Guid.NewGuid(),
                Description = doc.Description,
                CanShare = true // In real implementation, check user permissions
            });

            index++;
        }

        // Sort by modification date (most recent first)
        return results.OrderByDescending(r => r.ModifiedDate).ToList();
    }

    /// <inheritdoc />
    public async Task<ShareLinksResponse> CreateShareLinksAsync(
        ShareLinksRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Share links requested for {DocumentCount} documents by user {UserId}",
            request.DocumentIds.Count,
            userId);

        var links = new List<DocumentLink>();
        var errors = new List<ShareLinkError>();
        var invitations = new List<ShareInvitation>();

        // Generate share links for each document
        foreach (var documentId in request.DocumentIds)
        {
            // Simulate permission check - in real implementation, query Dataverse
            var hasSharePermission = await SimulateSharePermissionCheckAsync(documentId, userId, cancellationToken);

            if (!hasSharePermission)
            {
                errors.Add(new ShareLinkError
                {
                    DocumentId = documentId,
                    Code = "OFFICE_009",
                    Message = "Access denied. User lacks share permission for this document."
                });
                continue;
            }

            // Get document metadata - in real implementation, from Dataverse query
            var documentMetadata = await GetDocumentMetadataForLinkAsync(documentId, cancellationToken);

            if (documentMetadata == null)
            {
                errors.Add(new ShareLinkError
                {
                    DocumentId = documentId,
                    Code = "OFFICE_007",
                    Message = "Document not found."
                });
                continue;
            }

            // Generate shareable URL
            var shareUrl = GenerateShareLinkUrl(documentId);

            links.Add(new DocumentLink
            {
                DocumentId = documentId,
                Url = shareUrl,
                DisplayName = documentMetadata.DisplayName,
                FileName = documentMetadata.FileName,
                ContentType = documentMetadata.ContentType,
                Size = documentMetadata.Size,
                IconUrl = GetDocumentIconUrl(documentMetadata.ContentType)
            });
        }

        // Process invitations if grantAccess is true and recipients are provided
        if (request.GrantAccess && request.Recipients?.Count > 0)
        {
            invitations = await ProcessShareInvitationsAsync(
                request.Recipients,
                request.DocumentIds,
                request.Role,
                userId,
                cancellationToken);
        }

        return new ShareLinksResponse
        {
            Links = links,
            Invitations = invitations.Count > 0 ? invitations : null,
            Errors = errors.Count > 0 ? errors : null,
            CorrelationId = Guid.NewGuid().ToString()
        };
    }

    /// <summary>
    /// Simulates permission check for share access.
    /// </summary>
    private Task<bool> SimulateSharePermissionCheckAsync(
        Guid documentId,
        string userId,
        CancellationToken cancellationToken)
    {
        // TODO: Replace with actual Dataverse security role check
        _logger.LogDebug(
            "Permission check for document {DocumentId} by user {UserId}",
            documentId,
            userId);

        return Task.FromResult(true);
    }

    /// <summary>
    /// Gets document metadata for link generation.
    /// </summary>
    private Task<ShareLinkDocumentMetadata?> GetDocumentMetadataForLinkAsync(
        Guid documentId,
        CancellationToken cancellationToken)
    {
        // TODO: Replace with actual Dataverse query
        var shortId = documentId.ToString("N").Substring(0, 8);
        return Task.FromResult<ShareLinkDocumentMetadata?>(new ShareLinkDocumentMetadata
        {
            DisplayName = $"Document {shortId}",
            FileName = $"document-{shortId}.docx",
            ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            Size = 245678
        });
    }

    /// <summary>
    /// Generates a shareable URL for a document.
    /// </summary>
    private static string GenerateShareLinkUrl(Guid documentId)
    {
        // TODO: Make base URL configurable via appsettings
        const string baseUrl = "https://spaarke.app/doc";
        return $"{baseUrl}/{documentId}";
    }

    /// <summary>
    /// Gets an icon URL based on content type.
    /// </summary>
    private static string? GetDocumentIconUrl(string? contentType)
    {
        if (string.IsNullOrEmpty(contentType))
            return "/icons/document.svg";

        return contentType switch
        {
            var t when t.Contains("word") => "/icons/word.svg",
            var t when t.Contains("excel") || t.Contains("spreadsheet") => "/icons/excel.svg",
            var t when t.Contains("powerpoint") || t.Contains("presentation") => "/icons/powerpoint.svg",
            var t when t.Contains("pdf") => "/icons/pdf.svg",
            var t when t.StartsWith("image/") => "/icons/image.svg",
            var t when t.StartsWith("video/") => "/icons/video.svg",
            var t when t.StartsWith("audio/") => "/icons/audio.svg",
            var t when t.StartsWith("text/") => "/icons/text.svg",
            _ => "/icons/document.svg"
        };
    }

    /// <summary>
    /// Processes external sharing invitations.
    /// </summary>
    private async Task<List<ShareInvitation>> ProcessShareInvitationsAsync(
        IReadOnlyList<string> recipients,
        IReadOnlyList<Guid> documentIds,
        ShareLinkRole role,
        string userId,
        CancellationToken cancellationToken)
    {
        var invitations = new List<ShareInvitation>();

        foreach (var email in recipients)
        {
            var isExternal = !email.EndsWith("@spaarke.com", StringComparison.OrdinalIgnoreCase);

            if (!isExternal)
            {
                invitations.Add(new ShareInvitation
                {
                    Email = email,
                    Status = InvitationStatus.AlreadyHasAccess
                });
                continue;
            }

            _logger.LogInformation(
                "Creating invitation for external user {Email} to share {DocumentCount} documents with role {Role}",
                email,
                documentIds.Count,
                role);

            invitations.Add(new ShareInvitation
            {
                Email = email,
                Status = InvitationStatus.Created,
                InvitationId = Guid.NewGuid()
            });
        }

        await Task.CompletedTask;
        return invitations;
    }

    /// <summary>
    /// Internal record for document metadata used in link generation.
    /// </summary>
    private record ShareLinkDocumentMetadata
    {
        public required string DisplayName { get; init; }
        public required string FileName { get; init; }
        public string? ContentType { get; init; }
        public long? Size { get; init; }
    }

    /// <inheritdoc />
    public async Task<QuickCreateResponse?> QuickCreateAsync(
        QuickCreateEntityType entityType,
        QuickCreateRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Quick create requested for {EntityType} by user {UserId}",
            entityType,
            userId);

        // Get the display name for the created entity
        var displayName = entityType == QuickCreateEntityType.Contact
            ? $"{request.FirstName} {request.LastName}".Trim()
            : request.Name ?? "Unnamed";

        // TODO: Implement actual Dataverse record creation
        // The implementation should:
        // 1. Verify user has create permission for the entity type
        // 2. Build the entity record with appropriate fields based on entity type:
        //    - Matter: sprk_name, sprk_description, sprk_account (lookup)
        //    - Project: sprk_name, sprk_description, sprk_account (lookup)
        //    - Invoice: sprk_name, sprk_description, sprk_account (lookup)
        //    - Account: name, description, industrycode, address1_city
        //    - Contact: firstname, lastname, emailaddress1, parentcustomerid (lookup)
        // 3. Create the record via Dataverse SDK
        // 4. Return the created record ID and URL

        // Simulate async Dataverse operation
        await Task.Delay(100, cancellationToken);

        // Generate stub response for testing
        var createdId = Guid.NewGuid();
        var logicalName = QuickCreateFieldRequirements.GetLogicalName(entityType);

        _logger.LogInformation(
            "Quick create completed: EntityType={EntityType}, Id={Id}, Name={Name}",
            entityType,
            createdId,
            displayName);

        return new QuickCreateResponse
        {
            Id = createdId,
            EntityType = entityType,
            LogicalName = logicalName,
            Name = displayName,
            Url = $"https://spaarkedev1.crm.dynamics.com/main.aspx?etn={logicalName}&id={createdId}&pagetype=entityrecord"
        };
    }

    /// <inheritdoc />
    public async Task<RecentDocumentsResponse> GetRecentDocumentsAsync(
        string userId,
        int top = 10,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Recent items requested by user {UserId} with limit {Top}",
            userId,
            top);

        // TODO: Replace with actual Redis + Dataverse queries
        // The full implementation should:
        // 1. Query Redis sorted set for recent associations: "recent:associations:{userId}"
        // 2. Query Redis sorted set for recent documents: "recent:documents:{userId}"
        // 3. Query Dataverse for user favorites (sprk_userfavorite table)
        // 4. Validate user still has access to each item (parallel Dataverse permission checks)
        // 5. Filter out items user no longer has access to
        // 6. Return top N items per category sorted by most recently used

        // For now, return stub data for testing the endpoint structure
        var recentAssociations = GenerateStubRecentAssociations(top);
        var recentDocuments = GenerateStubRecentDocuments(top);
        var favorites = GenerateStubFavorites(top);

        await Task.CompletedTask; // Simulate async operation

        return new RecentDocumentsResponse
        {
            RecentAssociations = recentAssociations,
            RecentDocuments = recentDocuments,
            Favorites = favorites
        };
    }

    /// <summary>
    /// Generates stub recent associations for testing. Will be replaced with Redis queries.
    /// </summary>
    private static List<RecentAssociation> GenerateStubRecentAssociations(int top)
    {
        var associations = new List<RecentAssociation>
        {
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Matter,
                LogicalName = "sprk_matter",
                Name = "Smith vs Jones Matter",
                DisplayInfo = "Client: Acme Corp | Status: Active",
                LastUsed = DateTimeOffset.UtcNow.AddHours(-2),
                UseCount = 15
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Project,
                LogicalName = "sprk_project",
                Name = "Acme Implementation Project",
                DisplayInfo = "Phase: Development | Due: 2026-06-01",
                LastUsed = DateTimeOffset.UtcNow.AddHours(-5),
                UseCount = 8
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Account,
                LogicalName = "account",
                Name = "Acme Corporation",
                DisplayInfo = "Industry: Manufacturing | City: Chicago",
                LastUsed = DateTimeOffset.UtcNow.AddDays(-1),
                UseCount = 23
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Contact,
                LogicalName = "contact",
                Name = "John Smith",
                DisplayInfo = "Company: Acme Corp | Title: CEO",
                LastUsed = DateTimeOffset.UtcNow.AddDays(-2),
                UseCount = 5
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Invoice,
                LogicalName = "sprk_invoice",
                Name = "INV-2024-0001",
                DisplayInfo = "Amount: $15,000 | Status: Pending",
                LastUsed = DateTimeOffset.UtcNow.AddDays(-3),
                UseCount = 3
            }
        };

        return associations
            .OrderByDescending(a => a.LastUsed)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Generates stub recent documents for testing. Will be replaced with Redis + Dataverse queries.
    /// </summary>
    private static List<RecentDocument> GenerateStubRecentDocuments(int top)
    {
        var documents = new List<RecentDocument>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Contract Agreement v2.docx",
                WebUrl = "https://spaarke.com/documents/contract-agreement-v2",
                ModifiedDate = DateTimeOffset.UtcNow.AddHours(-1),
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = 245678,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Matter,
                    LogicalName = "sprk_matter",
                    Name = "Smith vs Jones Matter"
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Financial Report Q4 2025.xlsx",
                WebUrl = "https://spaarke.com/documents/financial-report-q4",
                ModifiedDate = DateTimeOffset.UtcNow.AddHours(-3),
                ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                FileSize = 1024567,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Account,
                    LogicalName = "account",
                    Name = "Acme Corporation"
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Project Proposal - Acme.pdf",
                WebUrl = "https://spaarke.com/documents/project-proposal-acme",
                ModifiedDate = DateTimeOffset.UtcNow.AddDays(-1),
                ContentType = "application/pdf",
                FileSize = 512000,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Project,
                    LogicalName = "sprk_project",
                    Name = "Acme Implementation Project"
                }
            },
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Meeting Notes 2026-01-15.docx",
                WebUrl = "https://spaarke.com/documents/meeting-notes-20260115",
                ModifiedDate = DateTimeOffset.UtcNow.AddDays(-5),
                ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                FileSize = 45000,
                EntityReference = new EntityReference
                {
                    Id = Guid.NewGuid(),
                    EntityType = AssociationType.Contact,
                    LogicalName = "contact",
                    Name = "John Smith"
                }
            }
        };

        return documents
            .OrderByDescending(d => d.ModifiedDate)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Generates stub favorites for testing. Will be replaced with Dataverse queries.
    /// </summary>
    private static List<FavoriteEntity> GenerateStubFavorites(int top)
    {
        var favorites = new List<FavoriteEntity>
        {
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Matter,
                LogicalName = "sprk_matter",
                Name = "Smith vs Jones Matter",
                FavoritedAt = DateTimeOffset.UtcNow.AddDays(-30)
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Account,
                LogicalName = "account",
                Name = "Acme Corporation",
                FavoritedAt = DateTimeOffset.UtcNow.AddDays(-25)
            },
            new()
            {
                Id = Guid.NewGuid(),
                EntityType = AssociationType.Project,
                LogicalName = "sprk_project",
                Name = "Acme Implementation Project",
                FavoritedAt = DateTimeOffset.UtcNow.AddDays(-15)
            }
        };

        return favorites
            .OrderByDescending(f => f.FavoritedAt)
            .Take(top)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<ShareAttachResponse> GetAttachmentsAsync(
        ShareAttachRequest request,
        string userId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Attachment packaging requested for {DocumentCount} documents by user {UserId}, DeliveryMode={DeliveryMode}, CorrelationId={CorrelationId}",
            request.DocumentIds.Length,
            userId,
            request.DeliveryMode,
            correlationId);

        var attachments = new List<AttachmentPackage>();
        var errors = new List<AttachmentError>();
        long totalSize = 0;

        // Process each document
        foreach (var documentId in request.DocumentIds)
        {
            try
            {
                // TODO: Replace with actual implementation once dependencies are available:
                // 1. Get document from Dataverse via IDataverseService
                // 2. Verify user has share permission via UAC
                // 3. Check size limits (25MB per file, 100MB total)
                // 4. For URL mode: Generate pre-signed download URL
                // 5. For Base64 mode: Download content from SPE and encode

                // Generate stub attachment for testing
                var attachment = await PackageAttachmentAsync(
                    documentId,
                    request.DeliveryMode,
                    totalSize,
                    cancellationToken);

                if (attachment != null)
                {
                    // Check if adding this file would exceed total size limit (100MB per spec NFR-03)
                    const long maxTotalAttachmentSizeBytes = 100 * 1024 * 1024; // 100MB
                    if (totalSize + attachment.Size > maxTotalAttachmentSizeBytes)
                    {
                        _logger.LogWarning(
                            "Total attachment size would exceed limit. DocumentId={DocumentId}, CurrentTotal={CurrentTotal}, FileSize={FileSize}, Limit={Limit}",
                            documentId,
                            totalSize,
                            attachment.Size,
                            maxTotalAttachmentSizeBytes);

                        errors.Add(new AttachmentError
                        {
                            DocumentId = documentId,
                            ErrorCode = "OFFICE_005",
                            Message = $"Adding this file ({attachment.Size / (1024 * 1024):F1}MB) would exceed the total attachment limit of {maxTotalAttachmentSizeBytes / (1024 * 1024)}MB."
                        });
                        continue;
                    }

                    attachments.Add(attachment);
                    totalSize += attachment.Size;

                    _logger.LogDebug(
                        "Packaged attachment: DocumentId={DocumentId}, FileName={FileName}, Size={Size}",
                        documentId,
                        attachment.FileName,
                        attachment.Size);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to package attachment for DocumentId={DocumentId}, CorrelationId={CorrelationId}",
                    documentId,
                    correlationId);

                errors.Add(new AttachmentError
                {
                    DocumentId = documentId,
                    ErrorCode = "OFFICE_012",
                    Message = "Failed to retrieve document from storage."
                });
            }
        }

        _logger.LogInformation(
            "Attachment packaging completed: {SuccessCount} succeeded, {ErrorCount} failed, TotalSize={TotalSize} bytes, CorrelationId={CorrelationId}",
            attachments.Count,
            errors.Count,
            totalSize,
            correlationId);

        return new ShareAttachResponse
        {
            Attachments = attachments.ToArray(),
            Errors = errors.Count > 0 ? errors.ToArray() : null,
            CorrelationId = correlationId,
            TotalSize = totalSize
        };
    }

    /// <summary>
    /// Packages a single document for attachment.
    /// Stub implementation - will be replaced with actual SPE and Dataverse calls.
    /// </summary>
    /// <remarks>
    /// Size limits per spec NFR-03:
    /// - Single file: 25MB max
    /// - Total attachments: 100MB max
    /// - Recommended base64 threshold: 1MB (URL preferred for larger files)
    /// </remarks>
    private async Task<AttachmentPackage?> PackageAttachmentAsync(
        Guid documentId,
        AttachmentDeliveryMode deliveryMode,
        long currentTotalSize,
        CancellationToken cancellationToken)
    {
        const long maxAttachmentSizeBytes = 25 * 1024 * 1024; // 25MB per file
        const long recommendedBase64ThresholdBytes = 1 * 1024 * 1024; // 1MB

        // TODO: Replace with actual implementation:
        // 1. Look up document in Dataverse by ID
        // 2. Verify SPE pointers exist (GraphDriveId, GraphItemId)
        // 3. Check user share permission
        // 4. Validate size constraints
        // 5. Generate download URL or base64 content based on delivery mode

        // Simulate async operation
        await Task.Delay(10, cancellationToken);

        // Generate stub data for testing
        // Use document ID to generate consistent test data
        var hash = documentId.GetHashCode();
        var testFiles = new[]
        {
            ("Contract.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 245678L),
            ("Report.pdf", "application/pdf", 512000L),
            ("Data.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", 1024567L),
            ("Presentation.pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation", 3145728L),
            ("Notes.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", 45000L)
        };

        var (filename, contentType, size) = testFiles[Math.Abs(hash) % testFiles.Length];

        // Check single file size limit (25MB per file per spec NFR-03)
        if (size > maxAttachmentSizeBytes)
        {
            _logger.LogWarning(
                "Attachment exceeds size limit: DocumentId={DocumentId}, Size={Size}, Limit={Limit}",
                documentId,
                size,
                maxAttachmentSizeBytes);

            // In real implementation, this would throw or return an error
            // For stub, we'll just return a smaller file
            size = 1024000; // 1MB
        }

        // URL expiry - 5 minutes per spec
        var urlExpiry = DateTimeOffset.UtcNow.AddMinutes(5);

        // Generate pre-signed download URL (always required)
        // In real implementation, this would generate a cryptographic token
        var downloadToken = Convert.ToBase64String(
            System.Text.Encoding.UTF8.GetBytes($"{documentId}:{urlExpiry:o}"));
        var downloadUrl = $"/office/share/attach/{Uri.EscapeDataString(downloadToken)}";

        // For base64 mode, include base64 content for small files
        string? contentBase64 = null;
        if (deliveryMode == AttachmentDeliveryMode.Base64)
        {
            if (size > recommendedBase64ThresholdBytes)
            {
                _logger.LogWarning(
                    "File exceeds recommended base64 threshold: DocumentId={DocumentId}, Size={Size}, Threshold={Threshold}",
                    documentId,
                    size,
                    recommendedBase64ThresholdBytes);
            }

            // Generate stub base64 content (placeholder - real implementation would encode actual file)
            contentBase64 = Convert.ToBase64String(
                System.Text.Encoding.UTF8.GetBytes($"Stub content for {documentId}"));
        }

        return new AttachmentPackage
        {
            DocumentId = documentId,
            FileName = filename,
            ContentType = contentType,
            Size = size,
            DownloadUrl = downloadUrl,
            UrlExpiry = urlExpiry,
            ContentBase64 = contentBase64
        };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<byte[]> StreamJobStatusAsync(
        Guid jobId,
        string? lastEventId,
        CancellationToken cancellationToken = default)
    {
        // Use Channel to produce events - avoids yield-inside-try-catch limitation
        var channel = System.Threading.Channels.Channel.CreateUnbounded<byte[]>(
            new System.Threading.Channels.UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = true
            });

        // Start the producer task
        _ = ProduceJobStatusEventsAsync(jobId, lastEventId, channel.Writer, cancellationToken);

        return channel.Reader.ReadAllAsync(cancellationToken);
    }

    /// <summary>
    /// Produces SSE events for job status streaming and writes them to the channel.
    /// </summary>
    private async Task ProduceJobStatusEventsAsync(
        Guid jobId,
        string? lastEventId,
        System.Threading.Channels.ChannelWriter<byte[]> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation(
                "SSE stream started for job {JobId}, LastEventId={LastEventId}",
                jobId,
                lastEventId ?? "none");

            // Parse last event ID for reconnection support
            long startSequence = 0;
            if (SseHelper.TryParseLastEventId(lastEventId, out var parsedJobId, out var parsedSequence))
            {
                if (parsedJobId == jobId)
                {
                    startSequence = parsedSequence;
                    _logger.LogInformation(
                        "SSE reconnection detected for job {JobId}, resuming from sequence {Sequence}",
                        jobId,
                        startSequence);
                }
            }

            long sequence = startSequence;
            var heartbeatInterval = TimeSpan.FromSeconds(15); // Per spec.md
            var pollInterval = TimeSpan.FromMilliseconds(500); // Internal poll frequency
            var lastHeartbeat = DateTimeOffset.UtcNow;

            // Send initial connected event
            sequence++;
            var eventId = SseHelper.GenerateEventId(jobId, sequence);
            await writer.WriteAsync(SseHelper.FormatConnected(jobId, eventId), cancellationToken);

            // Get initial job status and send it
            var currentStatus = await GetJobStatusAsync(jobId, cancellationToken);
            if (currentStatus is null)
            {
                // Job not found - send error and close
                _logger.LogWarning("SSE stream: Job {JobId} not found", jobId);
                await writer.WriteAsync(SseHelper.FormatError(
                    "OFFICE_008",
                    "Job not found or has expired",
                    jobId.ToString()), cancellationToken);
                return;
            }

            // Send initial status
            sequence++;
            eventId = SseHelper.GenerateEventId(jobId, sequence);
            await writer.WriteAsync(SseHelper.FormatProgress(
                currentStatus.Progress,
                currentStatus.CurrentPhase,
                eventId), cancellationToken);

            // Send completed phases if any
            if (currentStatus.CompletedPhases?.Count > 0)
            {
                foreach (var phase in currentStatus.CompletedPhases)
                {
                    // Only send phases after the reconnection point
                    sequence++;
                    if (sequence <= startSequence)
                        continue;

                    eventId = SseHelper.GenerateEventId(jobId, sequence);
                    await writer.WriteAsync(SseHelper.FormatStageUpdate(
                        phase.Name,
                        "Completed",
                        phase.CompletedAt,
                        eventId), cancellationToken);
                }
            }

            // Check if job is already in terminal state
            if (currentStatus.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
            {
                sequence++;
                eventId = SseHelper.GenerateEventId(jobId, sequence);

                if (currentStatus.Status == JobStatus.Completed)
                {
                    _logger.LogInformation("SSE stream: Job {JobId} already completed", jobId);
                    await writer.WriteAsync(SseHelper.FormatJobComplete(
                        jobId,
                        currentStatus.Result?.Artifact?.Id,
                        currentStatus.Result?.Artifact?.WebUrl,
                        eventId), cancellationToken);
                }
                else
                {
                    _logger.LogInformation("SSE stream: Job {JobId} already failed/cancelled", jobId);
                    await writer.WriteAsync(SseHelper.FormatJobFailed(
                        jobId,
                        currentStatus.Error?.Code ?? "OFFICE_INTERNAL",
                        currentStatus.Error?.Message ?? "Job failed",
                        currentStatus.Error?.Retryable ?? false,
                        eventId), cancellationToken);
                }

                return;
            }

            // Main streaming loop using Redis pub/sub via JobStatusService
            // Falls back to polling if Redis subscription fails
            var useRedisSubscription = await _jobStatusService.IsHealthyAsync(cancellationToken);

            if (useRedisSubscription)
            {
                _logger.LogInformation(
                    "SSE stream using Redis pub/sub for job {JobId}",
                    jobId);

                // Start heartbeat task
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var heartbeatTask = SendHeartbeatsAsync(
                    jobId,
                    writer,
                    heartbeatInterval,
                    heartbeatCts.Token,
                    () => sequence);

                try
                {
                    // Subscribe to job status updates via Redis pub/sub
                    await foreach (var update in _jobStatusService.SubscribeToJobAsync(jobId, cancellationToken))
                    {
                        // Skip updates we've already sent (based on sequence)
                        if (update.Sequence <= startSequence)
                        {
                            _logger.LogDebug(
                                "SSE stream: Skipping update with sequence {Sequence} (already sent) for job {JobId}",
                                update.Sequence,
                                jobId);
                            continue;
                        }

                        // Update our sequence tracker
                        sequence = Math.Max(sequence, update.Sequence);
                        eventId = SseHelper.GenerateEventId(jobId, sequence);

                        // Format and send the SSE event based on update type
                        var sseEvent = update.UpdateType switch
                        {
                            JobStatusUpdateType.Progress => SseHelper.FormatProgress(
                                update.Progress,
                                update.CurrentPhase,
                                eventId),

                            JobStatusUpdateType.StageComplete when update.CompletedPhase is not null =>
                                SseHelper.FormatStageUpdate(
                                    update.CompletedPhase.Name,
                                    "Completed",
                                    update.CompletedPhase.CompletedAt,
                                    eventId),

                            JobStatusUpdateType.StageStarted when update.CurrentPhase is not null =>
                                SseHelper.FormatStageUpdate(
                                    update.CurrentPhase,
                                    "Running",
                                    update.Timestamp,
                                    eventId),

                            JobStatusUpdateType.JobCompleted => SseHelper.FormatJobComplete(
                                jobId,
                                update.Result?.Artifact?.Id,
                                update.Result?.Artifact?.WebUrl,
                                eventId),

                            JobStatusUpdateType.JobFailed or JobStatusUpdateType.JobCancelled =>
                                SseHelper.FormatJobFailed(
                                    jobId,
                                    update.Error?.Code ?? "OFFICE_INTERNAL",
                                    update.Error?.Message ?? "Job failed",
                                    update.Error?.Retryable ?? false,
                                    eventId),

                            _ => SseHelper.FormatProgress(update.Progress, update.CurrentPhase, eventId)
                        };

                        await writer.WriteAsync(sseEvent, cancellationToken);

                        _logger.LogDebug(
                            "SSE event sent for job {JobId}: Type={UpdateType}, Progress={Progress}",
                            jobId,
                            update.UpdateType,
                            update.Progress);

                        // Terminal states end the stream
                        if (update.UpdateType is JobStatusUpdateType.JobCompleted
                            or JobStatusUpdateType.JobFailed
                            or JobStatusUpdateType.JobCancelled)
                        {
                            _logger.LogInformation(
                                "SSE stream ending for job {JobId} due to terminal state {State}",
                                jobId,
                                update.UpdateType);
                            return;
                        }
                    }
                }
                finally
                {
                    // Cancel heartbeat task
                    heartbeatCts.Cancel();
                    try { await heartbeatTask; } catch (OperationCanceledException) { }
                }
            }
            else
            {
                // Fallback to polling when Redis is unavailable
                _logger.LogWarning(
                    "SSE stream falling back to polling for job {JobId} (Redis unavailable)",
                    jobId);

                var fallbackPollInterval = TimeSpan.FromMilliseconds(500);
                var previousStatus = currentStatus.Status;
                var previousProgress = currentStatus.Progress;
                var previousPhase = currentStatus.CurrentPhase;
                var previousCompletedPhaseCount = currentStatus.CompletedPhases?.Count ?? 0;
                var fallbackLastHeartbeat = DateTimeOffset.UtcNow;

                while (!cancellationToken.IsCancellationRequested)
                {
                    // Check if heartbeat is needed
                    var now = DateTimeOffset.UtcNow;
                    if (now - fallbackLastHeartbeat >= heartbeatInterval)
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);
                        await writer.WriteAsync(SseHelper.FormatHeartbeat(now, eventId), cancellationToken);
                        fallbackLastHeartbeat = now;
                        _logger.LogDebug("SSE heartbeat sent for job {JobId}", jobId);
                    }

                    await Task.Delay(fallbackPollInterval, cancellationToken);

                    currentStatus = await GetJobStatusAsync(jobId, cancellationToken);
                    if (currentStatus is null)
                    {
                        _logger.LogWarning("SSE stream: Job {JobId} was deleted during streaming", jobId);
                        await writer.WriteAsync(SseHelper.FormatError(
                            "OFFICE_008",
                            "Job no longer exists",
                            jobId.ToString()), cancellationToken);
                        return;
                    }

                    // Send progress updates
                    if (currentStatus.Progress != previousProgress)
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);
                        await writer.WriteAsync(SseHelper.FormatProgress(
                            currentStatus.Progress,
                            currentStatus.CurrentPhase,
                            eventId), cancellationToken);
                        previousProgress = currentStatus.Progress;
                    }

                    // Send completed phase updates
                    var currentCompletedPhaseCount = currentStatus.CompletedPhases?.Count ?? 0;
                    if (currentCompletedPhaseCount > previousCompletedPhaseCount)
                    {
                        for (var i = previousCompletedPhaseCount; i < currentCompletedPhaseCount; i++)
                        {
                            var phase = currentStatus.CompletedPhases![i];
                            sequence++;
                            eventId = SseHelper.GenerateEventId(jobId, sequence);
                            await writer.WriteAsync(SseHelper.FormatStageUpdate(
                                phase.Name,
                                "Completed",
                                phase.CompletedAt,
                                eventId), cancellationToken);
                        }
                        previousCompletedPhaseCount = currentCompletedPhaseCount;
                    }

                    // Send current phase change
                    if (currentStatus.CurrentPhase != previousPhase && !string.IsNullOrEmpty(currentStatus.CurrentPhase))
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);
                        await writer.WriteAsync(SseHelper.FormatStageUpdate(
                            currentStatus.CurrentPhase,
                            "Running",
                            DateTimeOffset.UtcNow,
                            eventId), cancellationToken);
                        previousPhase = currentStatus.CurrentPhase;
                    }

                    // Check for terminal state
                    if (currentStatus.Status != previousStatus &&
                        currentStatus.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled)
                    {
                        sequence++;
                        eventId = SseHelper.GenerateEventId(jobId, sequence);

                        if (currentStatus.Status == JobStatus.Completed)
                        {
                            await writer.WriteAsync(SseHelper.FormatJobComplete(
                                jobId,
                                currentStatus.Result?.Artifact?.Id,
                                currentStatus.Result?.Artifact?.WebUrl,
                                eventId), cancellationToken);
                        }
                        else
                        {
                            await writer.WriteAsync(SseHelper.FormatJobFailed(
                                jobId,
                                currentStatus.Error?.Code ?? "OFFICE_INTERNAL",
                                currentStatus.Error?.Message ?? $"Job {currentStatus.Status.ToString().ToLowerInvariant()}",
                                currentStatus.Error?.Retryable ?? false,
                                eventId), cancellationToken);
                        }
                        return;
                    }
                    previousStatus = currentStatus.Status;
                }
            }

            _logger.LogInformation(
                "SSE stream ended for job {JobId} (cancellation requested)",
                jobId);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation(
                "SSE stream cancelled for job {JobId} (client disconnected)",
                jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "SSE stream error for job {JobId}",
                jobId);

            // Send terminal error event per ADR-019
            try
            {
                await writer.WriteAsync(SseHelper.FormatError(
                    "OFFICE_INTERNAL",
                    "Internal server error during job status streaming",
                    jobId.ToString()), CancellationToken.None);
            }
            catch
            {
                // Ignore errors when writing final error event
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    /// <summary>
    /// Sends heartbeat events at regular intervals to keep the SSE connection alive.
    /// </summary>
    private async Task SendHeartbeatsAsync(
        Guid jobId,
        System.Threading.Channels.ChannelWriter<byte[]> writer,
        TimeSpan interval,
        CancellationToken cancellationToken,
        Func<long> getCurrentSequence)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(interval, cancellationToken);

                var sequence = getCurrentSequence() + 1;
                var eventId = SseHelper.GenerateEventId(jobId, sequence);
                var heartbeatEvent = SseHelper.FormatHeartbeat(DateTimeOffset.UtcNow, eventId);

                await writer.WriteAsync(heartbeatEvent, cancellationToken);

                _logger.LogDebug("SSE heartbeat sent for job {JobId}", jobId);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when cancellation is requested
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Error sending heartbeat for job {JobId}",
                jobId);
        }
    }
}
