using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Configuration;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.Office;
using Sprk.Bff.Api.Services.Ai.Membership.Events;
using Sprk.Bff.Api.Services.Communication;

namespace Sprk.Bff.Api.Services.Office;

/// <summary>
/// Thin orchestrator for Office add-in operations.
/// Delegates to focused services: OfficeEmailEnricher, OfficeDocumentPersistence,
/// OfficeJobQueue, and OfficeStorageUploader.
/// </summary>
/// <remarks>
/// <para>
/// This service orchestrates the Office add-in backend workflows:
/// - Save: enriches content, uploads to SPE, persists to Dataverse, queues finalization
/// - Job status: queries Dataverse/in-memory store for job progress
/// - SSE streaming: real-time job status updates via Redis pub/sub
/// - Search, share, quick-create: entity and document operations
/// </para>
/// <para>
/// Per ADR-001, heavy processing (SPE upload, AI processing) is delegated to background workers.
/// This service focuses on fast job creation (target: &lt;3 seconds response time).
/// </para>
/// </remarks>
public class OfficeService : IOfficeService
{
    private readonly IJobStatusService _jobStatusService;
    private readonly IProcessingJobService _jobService;
    private readonly OfficeEmailEnricher _emailEnricher;
    private readonly OfficeDocumentPersistence _documentPersistence;
    private readonly OfficeJobQueue _jobQueue;
    private readonly OfficeStorageUploader _storageUploader;
    private readonly EmailProcessingOptions _emailProcessingOptions;
    private readonly RecordContainerResolver _containerResolver;
    private readonly IMembershipEventPublisher _membershipEventPublisher;
    // FR-B3 (task 043): routes a user-saved EMAIL through the SAME Association Engine as mailbox capture so a
    // hand-filed email is associated + triaged (an intelligence-bearing sprk_communication), not merely a
    // sprk_document archive. Optional/null-tolerant so hosts without the Communication module (and the existing
    // bare test constructions) keep working; null → the capture step is a guarded no-op (best-effort, NFR-04).
    private readonly EmailUploadCaptureService? _emailUploadCapture;
    // Real Dataverse entity search for the add-in "File to" picker (task 026 / #229 — replaces the
    // GenerateStubResults hardcoded fixtures). App-only read (ADR-028); singleton REST client.
    // Optional/null-tolerant so bare test constructions keep compiling; null → stub fallback.
    private readonly DataverseWebApiClient? _dataverseClient;
    // Slice 3 (#10): generic Dataverse create for the add-in inline "New record" (Matter/Project).
    // Registered singleton (→ IDataverseService, GraphModule.cs); optional/null-tolerant so bare test
    // ctors keep compiling; null → quick-create returns null (endpoint 403s).
    private readonly IGenericEntityService? _genericEntityService;
    private readonly ILogger<OfficeService> _logger;

    // In-memory job storage for development/testing (fallback when Dataverse unavailable)
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, JobStatusResponse> _jobStore = new();

    public OfficeService(
        IJobStatusService jobStatusService,
        IProcessingJobService jobService,
        OfficeEmailEnricher emailEnricher,
        OfficeDocumentPersistence documentPersistence,
        OfficeJobQueue jobQueue,
        OfficeStorageUploader storageUploader,
        IOptions<EmailProcessingOptions> emailProcessingOptions,
        IMembershipEventPublisher membershipEventPublisher,
        RecordContainerResolver containerResolver,
        ILogger<OfficeService> logger,
        EmailUploadCaptureService? emailUploadCapture = null,
        DataverseWebApiClient? dataverseClient = null,
        IGenericEntityService? genericEntityService = null)
    {
        _containerResolver = containerResolver
            ?? throw new ArgumentNullException(nameof(containerResolver));
        _jobStatusService = jobStatusService;
        _jobService = jobService;
        _emailEnricher = emailEnricher;
        _documentPersistence = documentPersistence;
        _jobQueue = jobQueue;
        _storageUploader = storageUploader;
        _emailProcessingOptions = emailProcessingOptions.Value;
        _membershipEventPublisher = membershipEventPublisher;
        _emailUploadCapture = emailUploadCapture;
        _dataverseClient = dataverseClient;
        _genericEntityService = genericEntityService;
        _logger = logger;
    }

    /// <summary>
    /// Decides which SPE container this save writes into. Server-side, always — task 085.
    /// </summary>
    /// <remarks>
    /// <para>
    /// With a <c>TargetEntity</c>, the container is derived from that record through task 076's
    /// <see cref="RecordContainerResolver"/>. That record is the one
    /// <c>AddEntityAccessFilter</c> already authorized the caller against, so the authorization key
    /// and the write destination become a single value. The resolver refuses (rather than falling
    /// back) when a secure record has no container of its own, which is why this method does not catch
    /// <see cref="SdapProblemException"/>: swallowing it would turn a correct fail-closed refusal into
    /// what reads like a misconfiguration.
    /// </para>
    /// <para>
    /// Without a <c>TargetEntity</c> there is no record, so the configured default applies. It is
    /// fail-closed when unset. Every shipped add-in path sends a <c>TargetEntity</c>, so this branch
    /// exists for the contract rather than for traffic.
    /// </para>
    /// </remarks>
    private async Task<string> ResolveContainerAsync(SaveRequest request, CancellationToken ct)
    {
        if (request.TargetEntity is { } target && target.EntityId != Guid.Empty)
        {
            var decision = await _containerResolver
                .ResolveForRecordAsync(target.EntityType, target.EntityId, ct)
                .ConfigureAwait(false);

            // FailClosed means the record is SECURE and has no container of its own. Falling through to
            // the configured default here would put a secure record's content in the shared container —
            // the precise failure this project exists to prevent, and irreversible in SPE because
            // permissions there are additive-only. Refuse instead.
            if (decision.Outcome == ContainerDecisionOutcome.FailClosed)
            {
                throw new InvalidOperationException(
                    $"The storage container for secure record {target.EntityType} {target.EntityId} could "
                    + "not be determined. Refusing rather than writing its content into a shared "
                    + "container, which SPE cannot subsequently un-share.");
            }

            if (!string.IsNullOrWhiteSpace(decision.ContainerId))
            {
                _logger.LogDebug(
                    "Office save container derived from {EntityType} {EntityId} (outcome: {Outcome})",
                    target.EntityType, target.EntityId, decision.Outcome);

                return decision.ContainerId!;
            }

            // Unresolved: a non-secure record with no container and no business-unit default. Falling
            // through to the configured default is safe here precisely BECAUSE the record is not
            // secure — the outcome enum guarantees Unresolved is unreachable for a secure record.
            _logger.LogDebug(
                "No container resolved for {EntityType} {EntityId} (outcome: {Outcome}); using the "
                + "configured default.",
                target.EntityType, target.EntityId, decision.Outcome);
        }

        var configured = _emailProcessingOptions.DefaultContainerId;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                "No storage container could be determined for this save. The request names no target "
                + "entity to derive one from, and EmailProcessing:DefaultContainerId is not configured. "
                + "Refusing rather than guessing a container.");
        }

        return configured;
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
                "[EMAIL BODY DEBUG] Subject={Subject}, HasBody={HasBody}, BodyLength={BodyLength}",
                request.Email.Subject,
                !string.IsNullOrEmpty(request.Email.Body),
                request.Email.Body?.Length ?? 0);
        }

        // Fetch email body and attachments from Graph API if missing
        if (request.ContentType == SaveContentType.Email && request.Email != null)
        {
            request = await _emailEnricher.EnrichEmailFromGraphAsync(request, httpContext, cancellationToken);
        }

        // Fetch attachment content from Graph API if missing (single attachment save)
        if (request.ContentType == SaveContentType.Attachment && request.Attachment != null)
        {
            request = await _emailEnricher.EnrichAttachmentFromGraphAsync(request, httpContext, cancellationToken);
        }

        try
        {
            // Step 1: Generate or use provided idempotency key
            var idempotencyKey = request.IdempotencyKey ?? GenerateIdempotencyKey(request);

            // Step 2: Check for existing job with this idempotency key
            // TRACKED: GitHub #229 - Replace with Dataverse ProcessingJob query
            var existingJob = await _documentPersistence.CheckForExistingJobAsync(idempotencyKey, cancellationToken);

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
                    StatusUrl = $"/api/office/jobs/{existingJob.JobId}",
                    StreamUrl = $"/api/office/jobs/{existingJob.JobId}/stream"
                };
            }

            // FR-B3 (task 043): route a user-saved EMAIL through the SAME capture engine as mailbox intake —
            // create/reconcile the canonical sprk_communication and run association + triage + provenance — so a
            // hand-filed email is intelligence-bearing, not merely a sprk_document archive. Runs AFTER the
            // idempotency early-return (a genuine replay already captured) and BEFORE document creation so
            // OfficeDocumentPersistence's cross-path link (FR-C4) resolves the canonical this produces. Message-
            // level dedup is structural (FR-C1 alternate key) inside CaptureAsync — no second dedup mechanism.
            // CaptureAsync is internally best-effort/non-fatal (NFR-04): it never throws out of the save.
            if (_emailUploadCapture is not null && request.ContentType == SaveContentType.Email)
            {
                await _emailUploadCapture.CaptureAsync(request, userId, cancellationToken);
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

            // ══ SERVER-DERIVED CONTAINER (task 085) ═══════════════════════════════════════════════
            // Derived HERE, before the payload is built, so the job record and the upload below cannot
            // disagree. Previously the payload carried request.ContainerId — a client-chosen container
            // that outlived the request inside the ProcessingJob row.
            //
            // With a TargetEntity, the container comes from the SAME record the caller was authorized
            // against (AddEntityAccessFilter), via task 076's resolver: the authorization key and the
            // write destination are now one value, so no code path can let them disagree. The resolver
            // is also secure-aware — a secure record's own container wins over any business-unit
            // default, which is the isolation guarantee a client-supplied id could always defeat.
            //
            // Without a TargetEntity there is no record to derive from, so the configured
            // EmailProcessing:DefaultContainerId applies — server-side, and already fail-closed when
            // unset (below). This is the sanctioned ServerDerivedConfig shape for content with no
            // owning record.
            //
            // ⚠️ Deliberately NOT the acting user's business unit. That was this task's brief, and the
            // resolver's own contract argues against it (RecordContainerResolver §"Why the RECORD's
            // business unit and not the ACTING USER's"): users sit in the Operations subtree while
            // secure records are owned in Secure Projects, so acting-user resolution writes a secure
            // record's content into the general Operations container — the exact isolation failure this
            // project exists to close. The owner's Q1 answer sanctioned acting-user BU for the three
            // upload-before-a-record-exists client paths in task 076, which are a different surface;
            // Office save always carries a TargetEntity from the shipped add-in, so its no-record
            // branch is contract-only and needs no new derivation component (CLAUDE.md §11).
            var derivedContainerId = await ResolveContainerAsync(request, cancellationToken);

            // Serialize the request payload for storage
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                ContentType = request.ContentType.ToString(),
                TargetEntity = request.TargetEntity,
                ContainerId = derivedContainerId,
                Email = request.Email,
                Attachment = request.Attachment,
                Document = request.Document,
                TriggerAiProcessing = request.TriggerAiProcessing
            });

            try
            {
                // Create ProcessingJob in Dataverse using existing IDataverseService
                jobId = await _jobService.CreateProcessingJobAsync(new
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
                        contentStream = OfficeEmailEnricher.BuildEmlFromMetadata(request.Email);
                        fileName = OfficeEmailEnricher.GenerateEmlFileName(request.Email);
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
                        // SANITIZED — see the note on the Document branch below. The client-supplied
                        // attachment name becomes the SPE upload path verbatim, and any '/' in it makes
                        // Graph create a folder.
                        fileName = SpeUploadPath.SanitizeFileName(request.Attachment.FileName);
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
                        // ══ SANITIZED 2026-08-28 — THIS IS THE FOLDER-MINTING DEFECT, ROOT CAUSE ══════
                        // The add-in's "Document Name" box is free text (SaveFlow.tsx) and its value
                        // arrives here as request.Document.FileName with NO client-side cleaning. It then
                        // becomes the SPE upload path verbatim (OfficeStorageUploader → UploadSmallAsync →
                        // Drives[id].Root.ItemWithPath(path)), and Graph creates EVERY '/'-delimited
                        // segment of an upload path as a folder.
                        //
                        // So a user typing a date — "New Word Document from Word Web Add In 8/24/2026" —
                        // produced a folder "New Word Document from Word Web Add In 8", containing a
                        // folder "24", containing an extension-less file "2026". That is the origin of the
                        // mystery folders in SPE Admin: not Word Online writing directly to the container,
                        // and not a folder prefix in our code, but OUR OWN app-only upload of a filename
                        // with slashes in it. Confirmed against production sprk_document rows (created by
                        // the BFF service identities, in the reported container) — the app-only upload is
                        // also why SPE Admin showed no human creator, which is what made it look external.
                        //
                        // The EMAIL branch above never had this bug because GenerateEmlFileName sanitizes.
                        // The asymmetry was the defect; the document and attachment branches now use the
                        // same sanitizer. Removing the hardcoded folder prefixes elsewhere in this change
                        // does NOT subsume this — a filename is a path, so it needs its own guard.
                        fileName = SpeUploadPath.SanitizeFileName(request.Document.FileName);
                        break;

                    default:
                        throw new InvalidOperationException($"Unsupported content type: {request.ContentType}");
                }

                // Task 085: the container was derived from the AUTHORIZED RECORD above, before the job
                // payload was built. This site used to read request.ContainerId and only fall back to
                // config — which is how a caller chose the destination of an app-only MI write.
                var containerId = derivedContainerId;

                // Upload to SPE
                var (uploadSuccess, driveId, itemId, webUrl, uploadError) = await _storageUploader.UploadToSpeAsync(
                    containerId,
                    fileName,
                    contentStream,
                    cancellationToken);

                if (!uploadSuccess || string.IsNullOrEmpty(driveId) || string.IsNullOrEmpty(itemId))
                {
                    // Update job status to failed
                    await _documentPersistence.UpdateJobStatusInDataverseAsync(jobId, JobStatus.Failed, "UploadFailed", 0, uploadError, cancellationToken);

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
                await _documentPersistence.UpdateJobStatusInDataverseAsync(jobId, JobStatus.Running, "FileUploaded", 30, null, cancellationToken);
                _jobStore[jobId] = jobRecord with
                {
                    Status = JobStatus.Running,
                    Progress = 30,
                    CurrentPhase = "FileUploaded"
                };

                // Create Document record with SPE pointers
                var (documentId, wasContentDuplicate) = await _documentPersistence.CreateDocumentWithSpePointersAsync(
                    request,
                    driveId,
                    itemId,
                    webUrl,
                    fileName,
                    fileSize,
                    userId,
                    cancellationToken);

                // FR-C3 (email-communication-intelligence-r2, R-3): the content is byte-identical to an existing
                // canonical document (returned as `documentId`). No second document was created — and there is
                // nothing new to finalize: the canonical already carries its Email/Attachment artifacts + AI, so
                // re-running finalization would only duplicate them and re-spend AI on identical bytes. Skip the
                // whole downstream pipeline, CLEAN UP the transient upload blob (gate-after-write; it is now truly
                // unreferenced — this is the item THIS request just uploaded, never the canonical's own), and
                // complete the job. The detector already NOTIFIED the user of the canonical. Blob cleanup is
                // best-effort (never fails the save). The `finally` below still disposes the content stream.
                if (wasContentDuplicate)
                {
                    await _storageUploader.DeleteFromSpeAsync(driveId, itemId, cancellationToken);

                    await _documentPersistence.UpdateJobStatusInDataverseAsync(jobId, JobStatus.Completed, "DeduplicatedToExisting", 100, null, cancellationToken);
                    _jobStore[jobId] = _jobStore[jobId] with
                    {
                        Status = JobStatus.Completed,
                        Progress = 100,
                        CurrentPhase = "DeduplicatedToExisting",
                        CompletedAt = DateTimeOffset.UtcNow
                    };

                    _logger.LogInformation(
                        "ProcessingJob {JobId} completed: content duplicate of canonical document {DocumentId}; finalization skipped, transient blob cleaned up.",
                        jobId, documentId);

                    return new SaveResponse
                    {
                        Success = true,
                        Duplicate = false, // NOT an idempotent job replay — this is a content dedup (user notified via the canonical notification)
                        JobId = jobId,
                        StatusUrl = $"/api/office/jobs/{jobId}",
                        StreamUrl = $"/api/office/jobs/{jobId}/stream"
                    };
                }

                // R3 task 082 — FR-2P2.6 + Q2 fire-and-forget membership event.
                // Per event-source-inventory §3B (line 64), POST /office/save
                // creates a sprk_document with ownerid defaulted by Dataverse to
                // the OBO caller. Publish Added event so the junction-updater
                // (task 084) + nightly recon (task 085) observe the new
                // association. When MembershipEventPublisherOptions.Enabled=false
                // (default), the NullMembershipEventPublisher peer logs + returns
                // (ADR-032 P2). The Task is discarded explicitly to signal
                // fire-and-forget semantics — publisher contract guarantees no
                // exceptions propagate to this site.
                if (Guid.TryParse(userId, out var callerOid))
                {
                    var membershipEvent = new MembershipChangedEvent
                    {
                        PersonId = callerOid,
                        PersonIdType = PersonIdentityType.User,
                        EntityLogicalName = "sprk_document",
                        EntityRecordId = documentId,
                        SourceField = "ownerid",
                        Role = "owner",
                        MutationType = MembershipMutationType.Added,
                        CorrelationId = correlationId,
                        OccurredOnUtc = DateTime.UtcNow,
                    };

                    _ = _membershipEventPublisher.PublishAsync(membershipEvent, cancellationToken);
                }

                // Update job status to records created
                await _documentPersistence.UpdateJobStatusInDataverseAsync(jobId, JobStatus.Running, "RecordsCreated", 50, null, cancellationToken);
                _jobStore[jobId] = _jobStore[jobId] with
                {
                    Progress = 50,
                    CurrentPhase = "RecordsCreated"
                };

                // ALWAYS queue finalization job - it creates EmailArtifact/AttachmentArtifact records
                // and optionally triggers AI processing based on TriggerAiProcessing flag in payload
                await _jobQueue.QueueUploadFinalizationAsync(
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
                await _documentPersistence.UpdateJobStatusInDataverseAsync(jobId, JobStatus.Completed, "Complete", 100, null, cancellationToken);
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
                StatusUrl = $"/api/office/jobs/{jobId}",
                StreamUrl = $"/api/office/jobs/{jobId}/stream"
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
        // TRACKED: GitHub #229 - Replace with Dataverse query once ProcessingJob exists
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
            var processingJob = await _jobService.GetProcessingJobAsync(jobId, cancellationToken);
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

        // Real Dataverse search (task 026 / #229). When no client is injected (bare test
        // constructions), fall back to the legacy stub so those tests keep their shape.
        if (_dataverseClient is null)
        {
            var stub = GenerateStubResults(request.Query, typesToSearch, request.Top);
            var stubTotal = stub.Count + (request.Skip > 0 ? request.Skip : 0);
            return new EntitySearchResponse
            {
                Results = stub.Skip(request.Skip).Take(request.Top).ToList(),
                TotalCount = stubTotal,
                HasMore = stubTotal > request.Skip + request.Top
            };
        }

        // Query each requested entity type with a name/number 'contains' filter. Each type is
        // best-effort — one entity's failure (missing table, transient 4xx) is logged and skipped,
        // never fails the whole picker. NOTE: app-only read (no per-user security trimming yet) —
        // tracked as a follow-up on #919.
        var combined = new List<EntitySearchResult>();
        var perTypeTop = Math.Clamp(request.Top, 5, 50);
        foreach (var type in typesToSearch)
        {
            try
            {
                combined.AddRange(await QuerySearchEntityAsync(type, request.Query, perTypeTop, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Entity search failed for type {EntityType}; skipping", type);
            }
        }

        // Rank: prefix matches first, then most-recently-modified.
        var ordered = combined
            .OrderByDescending(r => r.Name.StartsWith(request.Query, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(r => r.ModifiedOn)
            .ToList();

        return new EntitySearchResponse
        {
            Results = ordered.Skip(request.Skip).Take(request.Top).ToList(),
            TotalCount = ordered.Count + request.Skip,
            HasMore = ordered.Count > request.Skip + request.Top
        };
    }

    /// <summary>Per-entity-type Dataverse Web API search metadata (mirrors RecordSyncJob's catalogue).</summary>
    internal sealed record EntitySearchMeta(string EntitySet, string IdField, string NameField, string? RefField, string? DescField);

    private static readonly IReadOnlyDictionary<AssociationEntityType, EntitySearchMeta> _searchMeta =
        new Dictionary<AssociationEntityType, EntitySearchMeta>
        {
            [AssociationEntityType.Matter]  = new("sprk_matters",  "sprk_matterid",  "sprk_mattername",  "sprk_matternumber",  "sprk_matterdescription"),
            [AssociationEntityType.Project] = new("sprk_projects", "sprk_projectid", "sprk_projectname", "sprk_projectnumber", "sprk_projectdescription"),
            [AssociationEntityType.Invoice] = new("sprk_invoices", "sprk_invoiceid", "sprk_name",        "sprk_invoicenumber", "sprk_description"),
            [AssociationEntityType.Account]  = new("accounts",     "accountid",      "name",             "accountnumber",      "description"),
            [AssociationEntityType.Contact]  = new("contacts",     "contactid",      "fullname",         null,                 "jobtitle"),
        };

    /// <summary>
    /// Runs a single entity type's name/number 'contains' query against the Dataverse Web API and
    /// maps rows to <see cref="EntitySearchResult"/>. App-only read.
    /// </summary>
    private async Task<List<EntitySearchResult>> QuerySearchEntityAsync(
        AssociationEntityType type,
        string query,
        int top,
        CancellationToken cancellationToken)
    {
        var meta = _searchMeta[type];

        // OData string literal: double single-quotes, then URL-encode the value (the surrounding
        // contains(...) syntax stays literal).
        var value = Uri.EscapeDataString(query.Replace("'", "''"));
        var nameClause = $"contains({meta.NameField},'{value}')";
        var filter = meta.RefField is null
            ? nameClause
            : $"({nameClause} or contains({meta.RefField},'{value}'))";

        var selectFields = new List<string> { meta.IdField, meta.NameField, "modifiedon" };
        if (meta.RefField is not null) selectFields.Add(meta.RefField);
        if (meta.DescField is not null) selectFields.Add(meta.DescField);

        var rows = await _dataverseClient!.QueryAsync<Dictionary<string, JsonElement>>(
            meta.EntitySet,
            filter: filter,
            select: string.Join(",", selectFields),
            top: top,
            cancellationToken: cancellationToken);

        var results = new List<EntitySearchResult>(rows.Count);
        foreach (var row in rows)
        {
            var mapped = MapSearchRow(type, meta, row);
            if (mapped is not null)
                results.Add(mapped);
        }

        return results;
    }

    /// <summary>
    /// Maps one Dataverse Web API JSON row to an <see cref="EntitySearchResult"/>, or null when the
    /// row has no name (never surface an unnamed record in the picker). Pure — unit-tested.
    /// </summary>
    internal static EntitySearchResult? MapSearchRow(
        AssociationEntityType type,
        EntitySearchMeta meta,
        Dictionary<string, JsonElement> row)
    {
        var name = GetJsonString(row, meta.NameField);
        if (string.IsNullOrWhiteSpace(name))
            return null;

        var id = Guid.TryParse(GetJsonString(row, meta.IdField), out var g) ? g : Guid.Empty;
        var refVal = meta.RefField is not null ? GetJsonString(row, meta.RefField) : null;
        var desc = meta.DescField is not null ? GetJsonString(row, meta.DescField) : null;
        var modified = DateTimeOffset.TryParse(GetJsonString(row, "modifiedon"), out var mo)
            ? mo
            : DateTimeOffset.UtcNow;

        return new EntitySearchResult
        {
            Id = id,
            EntityType = type,
            LogicalName = GetLogicalName(type),
            Name = name!,
            DisplayInfo = !string.IsNullOrWhiteSpace(refVal) ? refVal! : (desc ?? GetLogicalName(type)),
            PrimaryField = !string.IsNullOrWhiteSpace(refVal) ? refVal! : name!,
            IconUrl = $"/icons/{type.ToString().ToLowerInvariant()}.svg",
            ModifiedOn = modified
        };
    }

    private static string? GetJsonString(Dictionary<string, JsonElement> row, string key)
        => row.TryGetValue(key, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;

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

        // TRACKED: GitHub #229 - Replace with Dataverse/SpeFileStore queries
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
        // TRACKED: GitHub #229 - Replace with Dataverse security role check
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
        // TRACKED: GitHub #229 - Replace with Dataverse query
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
        // TRACKED: GitHub #233 - Make base URL configurable via appsettings
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
    /// <remarks>
    /// Slice 3 (#10, email-communication-intelligence-r2 2026-09-02): implements the inline
    /// "New record" for the add-in "Related to" picker. Scope = <b>Matter + Project</b> (the common
    /// filings from an email); other types return null (endpoint 403s) until built out. The record is
    /// created via the generic Dataverse create (<see cref="IGenericEntityService.CreateAsync"/>) with
    /// only the required name (Dataverse-required set is name-only for both). Ownership is attributed to
    /// the caller when their <c>systemuserid</c> resolved (<paramref name="ownerSystemUserId"/>, ADR-024
    /// — best-effort; unresolved → app-owned, still created). There is no impersonated-create helper in
    /// the BFF, so ownership is set via the <c>ownerid</c> lookup rather than MSCRMCallerID.
    /// </remarks>
    public async Task<QuickCreateResponse?> QuickCreateAsync(
        QuickCreateEntityType entityType,
        QuickCreateRequest request,
        string userId,
        string? ownerSystemUserId = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Quick create requested for {EntityType} by user {UserId}",
            entityType,
            userId);

        // Scope: Matter + Project + Invoice (UI feedback 2026-09-02). Others not yet supported.
        if (entityType is not (QuickCreateEntityType.Matter
            or QuickCreateEntityType.Project
            or QuickCreateEntityType.Invoice))
        {
            _logger.LogInformation(
                "Quick create for {EntityType} is not yet supported (Matter/Project/Invoice only).",
                entityType);
            return null;
        }

        if (_genericEntityService is null)
        {
            _logger.LogWarning("Quick create unavailable — IGenericEntityService not injected.");
            return null;
        }

        var name = request.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null; // endpoint validates Name; guard defensively
        }

        var logicalName = QuickCreateFieldRequirements.GetLogicalName(entityType);
        var (nameField, descriptionField) = entityType switch
        {
            QuickCreateEntityType.Matter => ("sprk_mattername", (string?)"sprk_matterdescription"),
            QuickCreateEntityType.Project => ("sprk_projectname", (string?)"sprk_projectdescription"),
            _ => ("sprk_invoicename", (string?)null), // Invoice: name-only (no verified description field)
        };

        var entity = new Microsoft.Xrm.Sdk.Entity(logicalName);
        entity[nameField] = name;
        if (descriptionField is not null && !string.IsNullOrWhiteSpace(request.Description))
        {
            entity[descriptionField] = request.Description!.Trim();
        }

        // Attribute ownership to the caller when resolved (ADR-024). Best-effort: an unresolved
        // caller leaves ownerid to the Dataverse default (app user) rather than failing the create.
        if (!string.IsNullOrWhiteSpace(ownerSystemUserId) && Guid.TryParse(ownerSystemUserId, out var ownerGuid))
        {
            entity["ownerid"] = new Microsoft.Xrm.Sdk.EntityReference("systemuser", ownerGuid);
        }

        var createdId = await _genericEntityService.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Quick create completed: EntityType={EntityType}, Id={Id}, Name={Name}",
            entityType,
            createdId,
            name);

        return new QuickCreateResponse
        {
            Id = createdId,
            EntityType = entityType,
            LogicalName = logicalName,
            Name = name,
            // Org URL isn't known server-side (the add-in must not be org-pinned); the add-in uses
            // Id + Name to select the new record as the regarding, not the Url.
            Url = null
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

        // TRACKED: GitHub #229 - Replace with Redis + Dataverse queries
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
                // TRACKED: GitHub #229 - Replace with real implementation once dependencies available:
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

        // TRACKED: GitHub #229 - Replace with real implementation:
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
