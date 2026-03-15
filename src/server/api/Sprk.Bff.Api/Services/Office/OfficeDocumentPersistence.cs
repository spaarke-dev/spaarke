using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Handles Dataverse CRUD operations for Office documents, processing jobs, and related records.
/// Extracted from OfficeService to enforce single responsibility.
/// </summary>
public class OfficeDocumentPersistence
{
    private readonly IDocumentDataverseService _documentService;
    private readonly IProcessingJobService _jobService;
    private readonly ILogger<OfficeDocumentPersistence> _logger;

    public OfficeDocumentPersistence(
        IDocumentDataverseService documentService,
        IProcessingJobService jobService,
        ILogger<OfficeDocumentPersistence> logger)
    {
        _documentService = documentService;
        _jobService = jobService;
        _logger = logger;
    }

    /// <summary>
    /// Creates a Document record in Dataverse with SPE pointers.
    /// </summary>
    public async Task<Guid> CreateDocumentWithSpePointersAsync(
        SaveRequest request,
        string driveId,
        string itemId,
        string? webUrl,
        string fileName,
        long fileSize,
        string userId,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Creating Document record with SPE pointers: DriveId={DriveId}, ItemId={ItemId}",
            driveId, itemId);

        // Create base document record
        var createRequest = new CreateDocumentRequest
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

        var documentIdString = await _documentService.CreateDocumentAsync(createRequest, cancellationToken);
        var documentId = Guid.Parse(documentIdString);

        // Update with SPE pointers and additional metadata
        var updateRequest = new UpdateDocumentRequest
        {
            GraphDriveId = driveId,
            GraphItemId = itemId,
            FileName = fileName,
            FileSize = fileSize,
            MimeType = OfficeJobQueue.GetMimeType(request),
            HasFile = true,
            FilePath = webUrl  // SharePoint Embedded web URL (maps to sprk_filepath in Dataverse)
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

        await _documentService.UpdateDocumentAsync(documentIdString, updateRequest, cancellationToken);

        _logger.LogInformation(
            "Document record created: DocumentId={DocumentId}, DriveId={DriveId}, ItemId={ItemId}",
            documentId, driveId, itemId);

        return documentId;
    }

    /// <summary>
    /// Updates ProcessingJob status in Dataverse.
    /// </summary>
    public async Task UpdateJobStatusInDataverseAsync(
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

            await _jobService.UpdateProcessingJobAsync(jobId, new
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
    /// Checks for an existing ProcessingJob with the given idempotency key.
    /// Uses IDataverseService to query for existing jobs.
    /// </summary>
    public async Task<JobStatusResponse?> CheckForExistingJobAsync(
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking for existing job with idempotency key");

        try
        {
            var existingJob = await _jobService.GetProcessingJobByIdempotencyKeyAsync(
                idempotencyKey,
                cancellationToken);

            if (existingJob == null)
            {
                return null;
            }

            // Map the dynamic result to JobStatusResponse
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
    /// Generates a Dataverse URL for a document record.
    /// </summary>
    public static string GenerateDataverseUrl(Guid documentId)
    {
        const string dataverseBaseUrl = "https://spaarkedev1.crm.dynamics.com";
        const string appId = "729afe6d-ca73-f011-b4cb-6045bdd8b757";
        return $"{dataverseBaseUrl}/main.aspx?appid={appId}&pagetype=entityrecord&etn=sprk_document&id={documentId}";
    }

    /// <summary>
    /// Maps Dataverse ProcessingJob status option set value to JobStatus enum.
    /// </summary>
    public static JobStatus MapDataverseStatusToJobStatus(int? statusValue)
    {
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
    public static JobType MapDataverseJobTypeToJobType(int? jobTypeValue)
    {
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
}
