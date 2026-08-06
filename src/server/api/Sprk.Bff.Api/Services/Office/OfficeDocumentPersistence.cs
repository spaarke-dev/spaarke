using System.Text.Json;
using Microsoft.Extensions.Logging;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Communication;
using Sprk.Bff.Api.Services.Documents;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Handles Dataverse CRUD operations for Office documents, processing jobs, and related records.
/// Extracted from OfficeService to enforce single responsibility.
/// </summary>
public class OfficeDocumentPersistence
{
    private readonly IDocumentDataverseService _documentService;
    private readonly IProcessingJobService _jobService;
    private readonly ContentDedupDetector _dedupDetector;
    private readonly ILogger<OfficeDocumentPersistence> _logger;
    // FR-C2 (task 022): the seams for the office-upload half — record the SAVING USER on the canonical
    // sprk_communication (if the email was also captured inbound). Optional/null-tolerant so the existing bare
    // test constructor keeps compiling; DI resolves both singletons in every host. Null → the uploader merge is
    // a guarded no-op (best-effort by construction, NFR-04).
    private readonly ICommunicationDataverseService? _communicationService;
    private readonly IGenericEntityService? _genericEntityService;

    public OfficeDocumentPersistence(
        IDocumentDataverseService documentService,
        IProcessingJobService jobService,
        ContentDedupDetector dedupDetector,
        ILogger<OfficeDocumentPersistence> logger,
        ICommunicationDataverseService? communicationService = null,
        IGenericEntityService? genericEntityService = null)
    {
        _documentService = documentService;
        _jobService = jobService;
        _dedupDetector = dedupDetector;
        _logger = logger;
        _communicationService = communicationService;
        _genericEntityService = genericEntityService;
    }

    /// <summary>
    /// Creates a Document record in Dataverse with SPE pointers. Returns the document id AND whether the content
    /// was a byte-identical DUPLICATE (FR-C3): when <c>WasContentDuplicate</c> is true the returned
    /// <c>DocumentId</c> is the existing CANONICAL (no second document was created) — the caller MUST skip
    /// finalization (no redundant artifacts / AI) and clean up the transient upload blob.
    /// </summary>
    public async Task<(Guid DocumentId, bool WasContentDuplicate)> CreateDocumentWithSpePointersAsync(
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

        // ── FR-C2 (task 022) + FR-C4 (task 025): resolve the canonical communication ONCE ────
        // If this is an email save AND the same email was captured inbound, a canonical sprk_communication exists
        // for its internet-message-id. Resolve it once here and reuse it for BOTH: (a) FR-C2 — record THIS user as
        // a saver on the canonical row (the "M uploaders" fact); and (b) FR-C4 — link the document created below to
        // that canonical so capture + upload resolve to ONE email (done after the create, when the document id is
        // known). Null when this is not an email, there is no message-id, or the email was never captured.
        var canonicalCommunicationId =
            await MergeUploaderAndResolveCanonicalAsync(request, userId, cancellationToken);

        // ── FR-C3 content de-dup (gate-after-write, Tier-1 exact quickXorHash) ──────────────
        // The blob is already in SPE (upload happened upstream). Read its content identity and reconcile
        // against the sprk_canonicalhash index: on a byte-identical hit, DO NOT create a second canonical
        // document — the detector has already NOTIFIED the uploader; return the existing canonical id so the
        // caller opens/links it. Non-fatal (NFR-04): a null/no-dup decision proceeds to a normal create, and
        // its hash (when known) is stamped so future uploads dedup against THIS document.
        var dedup = await _dedupDetector.ReconcileAsync(driveId, itemId, userId, fileName, cancellationToken);
        if (dedup.IsDuplicate && dedup.CanonicalDocumentId is { } canonicalId)
        {
            _logger.LogInformation(
                "Skipping duplicate document create for {FileName} (DriveId={DriveId}, ItemId={ItemId}); content matches canonical sprk_document {CanonicalId}. Caller skips finalization + cleans up the transient blob.",
                fileName, driveId, itemId, canonicalId);
            return (canonicalId, true);
        }

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
            FilePath = webUrl,  // SharePoint Embedded web URL (maps to sprk_filepath in Dataverse)
            CanonicalHash = dedup.CanonicalHash  // FR-C3: stamp the content identity (null when unavailable)
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

        // ── FR-C4 (task 025): link this email-archive document to its captured communication ──
        // Capture-then-upload order: the canonical communication was resolved above; link the just-created document
        // to it so the reconciliation surface shows ONE email (not the captured communication + this archive as two
        // rows). Non-fatal / contract-first (NFR-04): the link is written via the generic seam, degrading until the
        // gated sprk_linkedcommunication column exists — it never fails the save. The reverse order (upload-then-
        // capture) is linked from IncomingCommunicationProcessor when the communication is later created.
        await LinkDocumentToCanonicalCommunicationAsync(documentId, canonicalCommunicationId, cancellationToken);

        _logger.LogInformation(
            "Document record created: DocumentId={DocumentId}, DriveId={DriveId}, ItemId={ItemId}",
            documentId, driveId, itemId);

        return (documentId, false);
    }

    /// <summary>
    /// FR-C2 (task 022) office-upload half + FR-C4 (task 025) resolve: when an email is saved and the SAME email was
    /// also captured inbound (a canonical <c>sprk_communication</c> exists for its internet-message-id), record the
    /// saving user on that canonical row's <see cref="DeliveryContextMerge.SavedByUsersAttribute"/> set — so no "who
    /// saved it" fact is lost — AND return the canonical's id so the caller can cross-path-link the document it
    /// creates (FR-C4). Returns <c>null</c> when the seams are unavailable (bare test ctor), the save is not an email,
    /// there is no internet-message-id, or no canonical communication exists (the email was never captured).
    /// Best-effort / non-fatal (NFR-04): never throws out of the save.
    /// </summary>
    private async Task<Guid?> MergeUploaderAndResolveCanonicalAsync(
        SaveRequest request, string userId, CancellationToken ct)
    {
        if (_communicationService is null || _genericEntityService is null)
            return null;
        if (request.ContentType != SaveContentType.Email)
            return null;

        var internetMessageId = request.Email?.InternetMessageId;
        if (string.IsNullOrWhiteSpace(internetMessageId))
            return null;

        try
        {
            var canonical = await _communicationService
                .GetCommunicationByInternetMessageIdAsync(internetMessageId, ct);
            if (canonical is null)
                return null; // email not captured inbound → no canonical communication

            // FR-C2: record the saver (skipped when userId is absent, e.g. a system save).
            if (!string.IsNullOrWhiteSpace(userId))
            {
                await DeliveryContextMerge.MergeAsync(
                    _genericEntityService, canonical.Id,
                    DeliveryContextMerge.SavedByUsersAttribute, userId, _logger, ct);
            }

            return canonical.Id; // FR-C4: the caller links the document it creates to this canonical.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "FR-C2/C4 canonical resolve failed (non-fatal) for message {MessageId}.", internetMessageId);
            return null;
        }
    }

    /// <summary>
    /// FR-C4 (task 025): links a just-created email-archive document to the canonical <c>sprk_communication</c> that
    /// captured the same email (resolved by <see cref="MergeUploaderAndResolveCanonicalAsync"/>). No-op when the
    /// generic seam is unavailable (bare test ctor) or no canonical was found (upload-then-capture — the reverse path
    /// links later from capture). Best-effort / non-fatal (NFR-04): the link is written via the generic seam so it
    /// degrades until the gated <c>sprk_linkedcommunication</c> column exists; it never fails the save.
    /// </summary>
    private async Task LinkDocumentToCanonicalCommunicationAsync(
        Guid documentId, Guid? canonicalCommunicationId, CancellationToken ct)
    {
        if (_genericEntityService is null || canonicalCommunicationId is not { } communicationId)
            return;

        await CrossPathLink.LinkDocumentToCommunicationAsync(
            _genericEntityService, documentId, communicationId, _logger, ct);
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
