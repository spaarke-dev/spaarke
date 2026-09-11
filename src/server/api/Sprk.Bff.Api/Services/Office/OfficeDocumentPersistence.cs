using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Xrm.Sdk;
// `EntityReference` is ambiguous inside this namespace — Sprk.Bff.Api.Models.Office declares its own
// (the SaveRequest target-entity DTO). Alias the Dataverse one so both stay readable at their use sites.
using XrmEntityReference = Microsoft.Xrm.Sdk.EntityReference;
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
    // ── The shared content-identity contract (FR-C3 / NFR-08) ────────────────────────────────────
    // The SAME two columns ContentDedupDetector indexes and the shipped Compose create-on-save stamps.
    // The logical names are declared LOCALLY (exactly as CrossPathLink declares its own lookup name) so
    // the Office save path carries no code dependency on Compose internals — one contract, one
    // mechanism, no parallel detector and no second hash column (root CLAUDE.md §11).
    internal const string DocumentLogicalName = "sprk_document";
    internal const string DocumentIdAttribute = "sprk_documentid";
    internal const string GraphItemIdAttribute = "sprk_graphitemid";
    internal const string CanonicalHashAttribute = "sprk_canonicalhash";
    internal const string CanonicalDocumentAttribute = "sprk_canonicaldocument";

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
        // against the sprk_canonicalhash index. There are TWO dedup modes, and which one runs is keyed on
        // the HOST-NEUTRAL SaveContentType — never on which Office host called (F-h / NFR-08, task 028).
        // Word and Outlook both arrive at this same line; what matters is what the content IS:
        //
        //   IMMUTABLE (Email, Attachment) → SUPPRESS. An archival capture never diverges, so a
        //     byte-identical hit correctly resolves to the pre-existing canonical and creates NO second
        //     document; the detector has already NOTIFIED the uploader and the caller cleans up the
        //     transient blob. UNCHANGED — email-communication-intelligence-r2 Pillar C relies on it.
        //
        //   EDITABLE (Document) → LINK / GRADUATE. A Word document is a living draft: two genuinely
        //     DIFFERENT drafts that happen to be byte-identical right now are still two documents.
        //     Suppressing the second discarded a distinct draft's record with no error surfaced — silent
        //     data loss (spike-4 §3 D2; forbidden by DEDUP-AND-SAVE-BACK-IDENTITY.md §3 and NFR-08). So
        //     the editable path ALWAYS creates its own sprk_document and stamps sprk_canonicalhash; when
        //     the bytes match an existing canonical it records that fact as a LINK (sprk_canonicaldocument)
        //     plus a user notification, and severs the link on first divergence. This MIRRORS the shipped
        //     Compose implementation (ComposeCreateOnSavePromoter create branch +
        //     ComposeRecordResolution.GraduateLinkedCopyIfDivergedAsync) — same detector, same two
        //     columns, same semantics. It is not a second mechanism.
        //
        // Non-fatal on both modes (NFR-04): an unavailable hash or a failed lookup degrades to a normal
        // create, and the hash (when known) is stamped so future uploads dedup against THIS document.
        string? canonicalHash;
        Guid? linkedCanonicalId = null;

        if (IsEditableContent(request.ContentType))
        {
            canonicalHash = null;

            // Guarded for the same reason the Compose create branch is: the detector documents itself as
            // never-throwing, but an editable save must not become the ONE path where a dedup hiccup fails
            // a user's save. Worst case here is a document created without its dedup stamp — recoverable
            // on the next save; a lost draft is not.
            try
            {
                // The detector's EDITABLE seam: pure identity resolution — no notification, no suppression.
                // (ContentDedupDetector.ResolveContentIdentityAsync already excludes hash-linked copies from
                // the canonical lookup, so a link never points at a copy that is about to graduate.)
                var (liveHash, existingCanonicalId) = await _dedupDetector
                    .ResolveContentIdentityAsync(driveId, itemId, cancellationToken);
                canonicalHash = liveHash;
                linkedCanonicalId = existingCanonicalId;

                // Graduate-on-divergence, evaluated BEFORE the create so the pre-existing row's metadata is
                // honest regardless of what this save goes on to do. Reachable on the shipped Office path
                // today because the SPE upload uses ConflictBehavior.Replace — a re-save under the same name
                // lands on the SAME drive-item, so the row recorded for that item is exactly the one whose
                // content just changed. It is also the seam FR-11's version-save (task 023) calls once it
                // resolves an existing document row.
                //
                // Ordering, stated plainly: linkedCanonicalId was resolved BEFORE this graduation, so if the
                // row graduating here would itself have become a valid link target, this save links to the
                // older canonical (or to nothing) instead. That is deliberate — it is the CONSERVATIVE
                // direction (a missing link, never a wrong one), and the case only arises from the D1
                // same-drive-item collision that FR-11 (task 023) owns. Re-resolving after graduation would
                // cost a second SPE round-trip to chase a state this path is not meant to create.
                await GraduateLinkedCopyIfDivergedAsync(itemId, canonicalHash, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Editable content-dedup resolution failed (non-fatal) for DriveId={DriveId}, ItemId={ItemId}; " +
                    "creating the document without a dedup stamp.",
                    driveId, itemId);
                linkedCanonicalId = null;
            }
        }
        else
        {
            var dedup = await _dedupDetector.ReconcileAsync(driveId, itemId, userId, fileName, cancellationToken);
            if (dedup.IsDuplicate && dedup.CanonicalDocumentId is { } canonicalId)
            {
                _logger.LogInformation(
                    "Skipping duplicate document create for {FileName} (DriveId={DriveId}, ItemId={ItemId}); content matches canonical sprk_document {CanonicalId}. Caller skips finalization + cleans up the transient blob.",
                    fileName, driveId, itemId, canonicalId);
                return (canonicalId, true);
            }

            canonicalHash = dedup.CanonicalHash;
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
            CanonicalHash = canonicalHash  // FR-C3: stamp the content identity (null when unavailable)
        };

        // Set entity association lookup based on target entity
        if (request.TargetEntity != null)
        {
            // Shared map — see Spaarke.Dataverse.DocumentAssociationMap for why four copies of this
            // switch became one.
            if (!DocumentAssociationMap.TryApply(
                    updateRequest, request.TargetEntity.EntityType, request.TargetEntity.EntityId))
            {
                _logger.LogWarning(
                    "Target entity type {EntityType} has no sprk_document lookup — document will be " +
                    "created UNASSOCIATED. Known gaps: account, contact, sprk_todo (no column exists).",
                    request.TargetEntity.EntityType);
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
        // rows). Non-fatal / contract-first (NFR-04): the link is written via the generic seam onto the existing
        // sprk_relatedcommunication lookup — it never fails the save. The reverse order (upload-then-
        // capture) is linked from IncomingCommunicationProcessor when the communication is later created.
        await LinkDocumentToCanonicalCommunicationAsync(documentId, canonicalCommunicationId, cancellationToken);

        // ── NFR-08 (task 028): record the byte-identical EDITABLE copy as a hash-linked copy ──
        // No-op unless this was an editable save whose bytes matched an existing canonical. The document
        // itself already exists at this point — the link is metadata about how it came to be, never a
        // precondition for it (contrast the immutable branch above, which returns before the create).
        await LinkEditableCopyToCanonicalAsync(documentId, linkedCanonicalId, userId, fileName, cancellationToken);

        _logger.LogInformation(
            "Document record created: DocumentId={DocumentId}, DriveId={DriveId}, ItemId={ItemId}",
            documentId, driveId, itemId);

        return (documentId, false);
    }

    /// <summary>
    /// NFR-08 (task 028): classifies a save as EDITABLE (link/graduate dedup) or IMMUTABLE (suppress dedup).
    /// The axis is the content type — which is HOST-NEUTRAL by construction: <see cref="SaveContentType.Email"/>
    /// and <see cref="SaveContentType.Attachment"/> are archival captures that never diverge (Outlook today),
    /// while <see cref="SaveContentType.Document"/> is a living draft (Word today, any host tomorrow). Nothing
    /// here — and nothing on either dedup path — reads which Office host issued the save; host divergence
    /// belongs in the client host adapters, never in save or dedup semantics.
    /// </summary>
    /// <remarks>
    /// The default arm deliberately fails SAFE toward <c>true</c> (link/graduate). A future content type that is
    /// really immutable and lands here gains at worst a redundant row plus a link — recoverable. The opposite
    /// default would silently discard a distinct record, which is the exact defect this method exists to fix.
    /// </remarks>
    internal static bool IsEditableContent(SaveContentType contentType) => contentType switch
    {
        SaveContentType.Email => false,       // immutable capture — suppress is correct (unchanged)
        SaveContentType.Attachment => false,  // immutable capture — suppress is correct (unchanged)
        _ => true,                            // Document (+ fail-safe default) — link/graduate
    };

    /// <summary>
    /// NFR-08 link half (task 028), mirroring the Compose create-on-save branch: the just-created EDITABLE
    /// document is byte-identical to an existing canonical RIGHT NOW, so record that as a hash-linked copy
    /// (<c>sprk_canonicaldocument</c>) and NOTIFY the saver — never silent, and never a suppressed create.
    /// The link is severed the moment the copy's content diverges
    /// (<see cref="GraduateLinkedCopyIfDivergedAsync"/>), graduating it to its own canonical.
    /// </summary>
    /// <remarks>
    /// Written through the generic seam (as the FR-C4 communication link is) rather than through
    /// <see cref="UpdateDocumentRequest"/>, so no shared <c>Spaarke.Dataverse</c> contract changes for a
    /// best-effort link. Non-fatal by construction (NFR-04): a missing seam or a failed write leaves the
    /// document intact and unlinked — metadata is lost, a record never is.
    /// </remarks>
    private async Task LinkEditableCopyToCanonicalAsync(
        Guid documentId,
        Guid? canonicalDocumentId,
        string? ownerOid,
        string? fileName,
        CancellationToken ct)
    {
        // Not byte-identical to anything — this document simply IS its own canonical.
        if (canonicalDocumentId is not { } canonicalId || canonicalId == Guid.Empty)
            return;

        // Defensive: a document can never be a copy of itself.
        if (documentId == canonicalId)
            return;

        if (_genericEntityService is null)
        {
            _logger.LogWarning(
                "Editable save of {FileName} is byte-identical to canonical sprk_document {CanonicalId}, but the " +
                "generic entity seam is unavailable — the document was created WITHOUT its sprk_canonicaldocument " +
                "link. The record itself is intact (NFR-08 holds: nothing was suppressed); only the link is missing.",
                fileName, canonicalId);
            return;
        }

        try
        {
            await _genericEntityService.UpdateAsync(
                DocumentLogicalName,
                documentId,
                new Dictionary<string, object>
                {
                    [CanonicalDocumentAttribute] = new XrmEntityReference(DocumentLogicalName, canonicalId),
                },
                ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Editable content-dedup link failed (non-fatal) for document {DocumentId} → canonical {CanonicalId}; " +
                "the document stands unlinked as its own canonical.",
                documentId, canonicalId);
            return;
        }

        _logger.LogInformation(
            "Editable content dedup: sprk_document {DocumentId} is byte-identical to canonical {CanonicalId} and was " +
            "recorded as a hash-linked COPY (NFR-08 — linked, never suppressed). It graduates to its own canonical on " +
            "first divergence.",
            documentId, canonicalId);

        // NOTIFY (never silent) — the detector's editable-path notification, distinct from the suppressed-copy one.
        await _dedupDetector.NotifyLinkedCopyAsync(ownerOid, canonicalId, fileName, ct);
    }

    /// <summary>
    /// NFR-08 graduate half (task 028), mirroring <c>ComposeRecordResolution.GraduateLinkedCopyIfDivergedAsync</c>:
    /// when a save lands on an SPE drive-item that ALREADY has an <c>sprk_document</c> row, and that row is a
    /// hash-linked COPY whose live content no longer matches the hash it was linked at, sever the link
    /// (<c>sprk_canonicaldocument</c> cleared via the <see cref="DBNull"/> clear-sentinel) and stamp the new
    /// content hash — the copy graduates to its own canonical.
    /// </summary>
    /// <remarks>
    /// Resolution is by the <c>sprk_graphitemid_uk</c> alternate key — the same identity Compose graduates on,
    /// and the key NFR-07 forbids relaxing. Best-effort / non-fatal (NFR-04): every failure logs and leaves the
    /// row unchanged, to be re-evaluated on the next save; it never fails a save. No-op when the generic seam is
    /// absent (bare test constructor), no live hash could be read, no row exists for the item (the ordinary
    /// first-save case — the alternate key signals "not found" by throwing), or the row is a true canonical with
    /// no link to sever.
    /// </remarks>
    internal async Task GraduateLinkedCopyIfDivergedAsync(string itemId, string? liveHash, CancellationToken ct)
    {
        if (_genericEntityService is null
            || string.IsNullOrWhiteSpace(itemId)
            || string.IsNullOrWhiteSpace(liveHash))
        {
            return;
        }

        Entity? existing;
        try
        {
            existing = await _genericEntityService.RetrieveByAlternateKeyAsync(
                DocumentLogicalName,
                new KeyAttributeCollection { { GraphItemIdAttribute, itemId } },
                new[] { DocumentIdAttribute, CanonicalDocumentAttribute, CanonicalHashAttribute },
                ct);
        }
        catch (Exception ex)
        {
            // The alternate key reports "not found" by THROWING, and not-found is the ordinary case (a first
            // save of a brand-new drive-item). Debug, not warning: there is nothing to graduate either way.
            _logger.LogDebug(ex,
                "No existing sprk_document resolved for drive-item {ItemId}; nothing to graduate.", itemId);
            return;
        }

        if (existing is null)
            return;

        // Only a hash-linked COPY can graduate — a true canonical carries no sprk_canonicaldocument link.
        if (existing.GetAttributeValue<XrmEntityReference>(CanonicalDocumentAttribute) is null)
            return;

        // Still byte-identical to the content it was linked at → not diverged; the link stands.
        var linkedHash = existing.GetAttributeValue<string>(CanonicalHashAttribute);
        if (string.Equals(liveHash, linkedHash, StringComparison.Ordinal))
            return;

        try
        {
            await _genericEntityService.UpdateAsync(
                DocumentLogicalName,
                existing.Id,
                new Dictionary<string, object>
                {
                    [CanonicalDocumentAttribute] = DBNull.Value, // sever the link (DBNull clear-sentinel)
                    [CanonicalHashAttribute] = liveHash!,        // stamp the diverged content's own identity
                },
                ct);

            _logger.LogInformation(
                "Editable content dedup: sprk_document {DocumentId} diverged from its linked canonical; graduated to " +
                "its own canonical (NFR-08).",
                existing.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Editable content-dedup graduation failed (non-fatal) for document {DocumentId}; leaving the link intact.",
                existing.Id);
        }
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
    /// writes the existing <c>sprk_relatedcommunication</c> lookup; it never fails the save.
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
