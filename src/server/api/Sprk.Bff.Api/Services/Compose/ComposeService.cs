using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Services.Jobs;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Canonical orchestration implementation of <see cref="IComposeService"/> for the Compose
/// drafting workspace. Load/Save/Promote against SPE + Dataverse.
/// </summary>
/// <remarks>
/// <para>
/// Consumes <see cref="ISpeFileOperations"/> for SPE plumbing (Graph OBO Load/Save),
/// <see cref="ChatSessionManager"/> for ChatSession binding, and
/// <see cref="IGenericEntityService"/> for the FR-06 first-Save promotion.
/// </para>
/// <para>
/// FR-06 idempotent promotion: <see cref="PromoteIfEphemeralAsync"/> looks up an existing
/// <c>sprk_document</c> row by SPE drive-item id (alternate key <c>sprk_graphitemid_uk</c>)
/// BEFORE attempting create. If a row is found, the existing id is returned. Concurrent
/// callers resolve via Dataverse alternate-key uniqueness — the second create surfaces as
/// InvalidOperationException, caught + re-resolved via alternate-key lookup.
/// </para>
/// <para>
/// FR-07 ChatSession rebind: on promotion, the session's <c>DocumentId</c> is rebound
/// from the SPE drive-item id to the new <c>sprk_documentid</c> via
/// <see cref="ChatSessionManager.UpdateSessionCacheAsync"/>.
/// </para>
/// </remarks>
public class ComposeService : IComposeService
{
    private const string DocumentLogicalName = "sprk_document";
    private const string DocumentIdAttribute = "sprk_documentid";
    private const string GraphItemIdAttribute = "sprk_graphitemid";
    private const string DisplayNameAttribute = "sprk_documentname";
    private const string FileNameAttribute = "sprk_filename";

    // FR-05 create-on-save backbone — the consumer-declared ordered step set the
    // JobAwareCompletionStateProjector projects (container → record → profile-analysis → indexing).
    // These string keys are the Compose contract the future OutcomeCard renders; keep stable.
    internal const string StepContainer = "container";
    internal const string StepRecord = "record";
    internal const string StepProfileAnalysis = "profile-analysis";
    internal const string StepIndexing = "indexing";

    private const string ComposeCreateOnSaveJobType = "compose-create-on-save";
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly ISpeFileOperations _spe;
    private readonly ChatSessionManager _sessions;
    private readonly IGenericEntityService _dataverse;
    private readonly DocxAnnotationWriter _annotationWriter;
    private readonly IPostUploadIndexingEnqueuer _indexing;
    private readonly ILogger<ComposeService> _logger;

    public ComposeService(
        ISpeFileOperations spe,
        ChatSessionManager sessions,
        IGenericEntityService dataverse,
        DocxAnnotationWriter annotationWriter,
        IPostUploadIndexingEnqueuer indexing,
        ILogger<ComposeService> logger)
    {
        _spe = spe;
        _sessions = sessions;
        _dataverse = dataverse;
        _annotationWriter = annotationWriter;
        _indexing = indexing;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<UploadComposeDocumentResult> UploadAsync(
        UploadComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException(
            "Compose upload routes through the existing Assistant upload pipeline in R1; " +
            "see spec §10.5 Placement Justification. Use LoadAsync with the resulting " +
            "SPE drive-item id. This method is reserved for R2+ inline upload.");
    }

    /// <inheritdoc />
    public async Task<LoadComposeDocumentResult> LoadAsync(
        LoadComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DriveId))
            throw new ArgumentException("DriveId is required for SPE drive-item access.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));

        _logger.LogInformation(
            "Compose load: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} record={DocumentRecordId}",
            request.TenantId, request.DriveId, request.DocumentSpeId, request.DocumentRecordId);

        // 1) Fetch metadata (name/size/etag). Missing → NotFound.
        var metadata = await _spe.GetFileMetadataAsUserAsync(httpContext, request.DriveId, request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);
        if (metadata is null)
        {
            throw new InvalidOperationException(
                $"SPE drive-item not found: drive={request.DriveId} item={request.DocumentSpeId}");
        }

        // 2) Fetch content stream. Graph returns non-seekable HttpBaseStream → buffer to
        //    MemoryStream so Length/Seek work for downstream consumers.
        var stream = await _spe.DownloadFileAsUserAsync(httpContext, request.DriveId, request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"SPE drive-item content unavailable: drive={request.DriveId} item={request.DocumentSpeId}");
        }

        ReadOnlyMemory<byte> content;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream(capacity: (int)Math.Min(metadata.Size ?? 0, int.MaxValue));
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            content = buffer.ToArray();
        }

        // 3) Ensure a ChatSession bound to the document. For Path A (Document row present),
        //    bind to sprk_documentid; for Path B continuation, bind to the SPE drive-item id.
        var bindingId = request.DocumentRecordId.HasValue
            ? request.DocumentRecordId.Value.ToString()
            : request.DocumentSpeId;

        // FR-29 (R2, design.md §8): if the caller supplies a known prior SessionId bound to
        // THIS SAME document identity, RESUME it instead of minting a new one — this is what
        // carries AnchoredAnnotations/DefinedTermsTracking forward across a document re-open
        // (the annotations are keyed to document identity, not to a DOCX version — design.md
        // §8 "Cross-version persistence"). A mismatched or missing session falls back to the
        // R1 mint-new behavior unchanged (purely additive).
        ChatSession? session = null;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var candidate = await _sessions.GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
                .ConfigureAwait(false);
            if (candidate is not null && string.Equals(candidate.DocumentId, bindingId, StringComparison.Ordinal))
            {
                session = candidate;
                _logger.LogDebug(
                    "Compose load: resumed existing session {SessionId} bound to document={BindingId} (tenant={TenantId}) — restoring {AnnotationCount} annotation(s), {DefinedTermCount} defined term(s)",
                    session.SessionId, bindingId, request.TenantId,
                    session.AnchoredAnnotations?.Count ?? 0, session.DefinedTermsTracking?.Count ?? 0);
            }
        }

        session ??= await _sessions.CreateSessionAsync(
                tenantId: request.TenantId,
                documentId: bindingId,
                playbookId: null,
                hostContext: null,
                ct: cancellationToken)
            .ConfigureAwait(false);

        return new LoadComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId,
            DriveId = request.DriveId,
            SessionId = session.SessionId,
            DocumentRecordId = request.DocumentRecordId,
            Content = content,
            ETag = metadata.ETag,
            FileName = metadata.Name,
            Size = metadata.Size,
            AnchoredAnnotations = session.AnchoredAnnotations ?? Array.Empty<AnchoredAnnotation>(),
            DefinedTermsTracking = session.DefinedTermsTracking ?? Array.Empty<DefinedTerm>(),
        };
    }

    /// <inheritdoc />
    public async Task<SaveComposeDocumentResult> SaveAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("SessionId is required for first-Save promotion rebind.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));
        if (request.Content.IsEmpty)
            throw new ArgumentException("Content is required and must be non-empty.", nameof(request));

        var observedAt = DateTimeOffset.UtcNow;
        var isTransientCreate = string.IsNullOrWhiteSpace(request.DocumentSpeId);

        _logger.LogInformation(
            "Compose save: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} container={ContainerId} transientCreate={IsTransientCreate} session={SessionId} record={DocumentRecordId} size={SizeBytes}",
            request.TenantId, request.DriveId, request.DocumentSpeId, request.ContainerId,
            isTransientCreate, request.SessionId, request.DocumentRecordId, request.Content.Length);

        // ────────────────────────────────────────────────────────────────────────────
        // STEP 1 — container (FR-05, Fork A + Fork B).
        //   Transient draft (no DocumentSpeId): the container id is CLIENT-SUPPLIED (Fork A —
        //   no server-side BU→container resolver); create the SPE drive-item in it under OBO
        //   (Fork B). A missing container FAILS the container step honestly — never a success.
        //   Existing item (DocumentSpeId present): replace the drive-item's content (R1 behavior).
        // ────────────────────────────────────────────────────────────────────────────
        string effectiveSpeId;
        string? effectiveDriveId;
        FileHandleDto saved;
        var fileName = ResolveFileName(request.DisplayName);

        if (isTransientCreate)
        {
            if (string.IsNullOrWhiteSpace(request.ContainerId))
            {
                _logger.LogWarning(
                    "Compose create-on-save: transient draft with no client-supplied ContainerId — failing the '{Step}' step honestly (session={SessionId}). No server-side BU→container resolver (multi-container INV-7).",
                    StepContainer, request.SessionId);
                return BuildContainerFailedResult(request, observedAt);
            }

            // Fork B: mint the SPE drive-item in the supplied container under the user's OBO
            // identity (the Compose user holds the file ACL; MI does not — same constraint that
            // deferred profile). Idempotency: this branch only runs when DocumentSpeId is absent;
            // once created, the client re-Saves with the returned id → the replace path below, so
            // the drive-item is never double-created.
            var driveId = await _spe.ResolveDriveIdAsync(request.ContainerId, cancellationToken).ConfigureAwait(false);
            using var createStream = new MemoryStream(request.Content.ToArray(), writable: false);
            var created = await _spe.UploadSmallAsUserAsync(
                    httpContext, driveId, fileName, createStream, cancellationToken)
                .ConfigureAwait(false);

            if (created is null || string.IsNullOrEmpty(created.Id))
            {
                _logger.LogError(
                    "Compose create-on-save: SPE drive-item creation returned null/empty for container={ContainerId} — failing the '{Step}' step (session={SessionId}).",
                    request.ContainerId, StepContainer, request.SessionId);
                return BuildContainerFailedResult(request, observedAt);
            }

            saved = created;
            effectiveSpeId = created.Id;
            effectiveDriveId = created.DriveId ?? driveId;
            fileName = created.Name ?? fileName;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.DriveId))
                throw new ArgumentException("DriveId is required for SPE drive-item access when DocumentSpeId is supplied.", nameof(request));

            using var contentStream = new MemoryStream(request.Content.ToArray(), writable: false);
            var replaced = await _spe.ReplaceFileContentAsUserAsync(
                    httpContext, request.DriveId, request.DocumentSpeId!, contentStream, cancellationToken)
                .ConfigureAwait(false);

            if (replaced is null || string.IsNullOrEmpty(replaced.Id))
            {
                throw new InvalidOperationException(
                    $"SPE save failed: drive-item not found or version not returned. drive={request.DriveId} item={request.DocumentSpeId}");
            }

            saved = replaced;
            effectiveSpeId = request.DocumentSpeId!;
            effectiveDriveId = request.DriveId;
            fileName = replaced.Name ?? fileName;
        }

        // ────────────────────────────────────────────────────────────────────────────
        // STEP 2 — record (FR-06 idempotent promotion). Repeated saves see the existing row.
        // ────────────────────────────────────────────────────────────────────────────
        var promoteRequest = new PromoteComposeDocumentRequest
        {
            DocumentSpeId = effectiveSpeId,
            SessionId = request.SessionId,
            TenantId = request.TenantId,
            DisplayName = request.DisplayName,
        };

        var promotion = await PromoteIfEphemeralAsync(promoteRequest, httpContext, cancellationToken)
            .ConfigureAwait(false);

        // ────────────────────────────────────────────────────────────────────────────
        // STEP 4 — indexing (sync-OBO). The Compose user wrote the file, so indexing MUST run
        // inline in the OBO request scope (Pattern 4) — a Service Bus job under MI would 403 on
        // the download. IPostUploadIndexingEnqueuer is the always-registered, ADR-013-safe seam;
        // it swallows its own failures (returns a result), so we read the result for the step
        // state rather than relying on an exception.
        // ────────────────────────────────────────────────────────────────────────────
        var indexingResult = await _indexing.EnqueueIfApplicableAsync(
                new PostUploadIndexingRequest(
                    TenantId: request.TenantId,
                    DriveId: effectiveDriveId ?? string.Empty,
                    ItemId: effectiveSpeId,
                    FileName: fileName,
                    FileSizeBytes: saved.Size ?? request.Content.Length,
                    ContentType: DocxContentType,
                    DocumentId: promotion.DocumentRecordId?.ToString(),
                    ParentEntity: null,          // parent association is task 014 — standalone Document is valid
                    SearchIndexName: null,       // resolver cascade runs downstream
                    Source: "ComposeCreateOnSave",
                    CorrelationId: httpContext.TraceIdentifier),
                httpContext,
                cancellationToken)
            .ConfigureAwait(false);

        // ────────────────────────────────────────────────────────────────────────────
        // Project per-step states (container → record → profile-analysis[deferred] → indexing)
        // through the shared JobAwareCompletionStateProjector. A fileless/unindexed record can
        // never be a success (aggregate Failed/Partial); profile is deferred to core (Fork C).
        // ────────────────────────────────────────────────────────────────────────────
        var completion = ProjectCreateOnSaveState(
            subjectId: effectiveSpeId,
            correlationId: httpContext.TraceIdentifier,
            containerSignal: CompletedSignal(StepContainer),
            recordSignal: CompletedSignal(StepRecord),
            indexingSignal: IndexingSignal(indexingResult),
            observedAt: observedAt);

        return new SaveComposeDocumentResult
        {
            DocumentSpeId = effectiveSpeId,
            DriveId = effectiveDriveId,
            SessionId = promotion.SessionId,
            DocumentRecordId = promotion.DocumentRecordId,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            WasPromotedThisSave = promotion.WasCreated,
            CompletionState = completion,
        };
    }

    /// <inheritdoc />
    public async Task<PromoteComposeDocumentResult> PromoteIfEphemeralAsync(
        PromoteComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("SessionId is required for the ephemeral→promoted rebind.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));

        // 1) Idempotency check by SPE drive-item id (alt key sprk_graphitemid_uk).
        var existingId = await TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);

        if (existingId.HasValue)
        {
            _logger.LogDebug(
                "Compose promote: existing sprk_document {DocumentRecordId} found for driveItem={DocumentSpeId} — idempotent no-op",
                existingId.Value, request.DocumentSpeId);

            await RebindSessionDocumentIdAsync(
                    tenantId: request.TenantId,
                    sessionId: request.SessionId,
                    currentDocumentId: request.DocumentSpeId,
                    newDocumentId: existingId.Value.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);

            return new PromoteComposeDocumentResult
            {
                DocumentSpeId = request.DocumentSpeId,
                SessionId = request.SessionId,
                DocumentRecordId = existingId.Value,
                WasCreated = false,
            };
        }

        // 2) Create the sprk_document row.
        var entity = new Entity(DocumentLogicalName);
        entity[GraphItemIdAttribute] = request.DocumentSpeId;
        var effectiveDisplayName = !string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.DisplayName!
            : $"Compose document ({request.DocumentSpeId})";
        entity[DisplayNameAttribute] = effectiveDisplayName;
        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            entity[FileNameAttribute] = request.DisplayName!;
        }

        Guid newId;
        try
        {
            newId = await _dataverse.CreateAsync(entity, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Compose promote: created sprk_document {DocumentRecordId} for driveItem={DocumentSpeId}",
                newId, request.DocumentSpeId);
        }
        catch (InvalidOperationException ex)
        {
            // Narrow race — concurrent Save promoted first. Re-resolve.
            _logger.LogWarning(ex,
                "Compose promote: create failed for driveItem={DocumentSpeId} — likely concurrent promotion. Re-resolving via alternate key.",
                request.DocumentSpeId);

            var raceWinnerId = await TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false);

            if (!raceWinnerId.HasValue)
            {
                throw;
            }

            newId = raceWinnerId.Value;
        }

        // 3) Rebind the ChatSession DocumentId from SPE id → new sprk_documentid (FR-07).
        await RebindSessionDocumentIdAsync(
                tenantId: request.TenantId,
                sessionId: request.SessionId,
                currentDocumentId: request.DocumentSpeId,
                newDocumentId: newId.ToString(),
                cancellationToken)
            .ConfigureAwait(false);

        return new PromoteComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId,
            SessionId = request.SessionId,
            DocumentRecordId = newId,
            WasCreated = true,
        };
    }

    // =========================================================================
    // FR-05 create-on-save backbone — helpers (per-step job-aware projection).
    //
    // The four steps container → record → profile-analysis → indexing are projected through the
    // shared JobAwareCompletionStateProjector (store-before-render, ADR-040). profile-analysis is
    // DEFERRED: the only profile seam (IAppOnlyAnalysisService) trips the ADR-013 NetArchTest
    // facade rule AND runs under MI (which 403s on the OBO-written file). Core owns the
    // Services/Ai/PublicContracts/IDocumentProfileAi facade (redesign-r2, notes/HANDOFF-…). Until
    // it ships, profile emits a non-terminal deferred state and the aggregate stays Partial on the
    // happy path — record exists + indexed, downstream profile pending (the ingestion-parity signal).
    // =========================================================================

    /// <summary>
    /// The interim R5-E success bar for FR-05 create-on-save (documented exception, 2026-07-09):
    /// a record is interim-successful ONLY when the <c>container</c>, <c>record</c>, AND
    /// <c>indexing</c> steps all reached terminal success — a record with no SPE file OR no index
    /// is NEVER a success. <c>profile-analysis</c> is excluded (deferred to core's
    /// <c>IDocumentProfileAi</c>); the FULL R5-E bar (aggregate == <see cref="JobAwareState.Completed"/>,
    /// which requires profile too) is restored when core ships the facade.
    /// </summary>
    public static bool IsInterimCreateOnSaveSuccess(JobAwareCompletionState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        bool Completed(string stepName) =>
            state.Steps.Any(s => string.Equals(s.StepName, stepName, StringComparison.Ordinal)
                && s.State == JobAwareState.Completed);
        return Completed(StepContainer) && Completed(StepRecord) && Completed(StepIndexing);
    }

    /// <summary>Resolves the created drive-item's file name from the caller display name,
    /// defaulting to a unique <c>compose-draft-…docx</c> and ensuring a <c>.docx</c> extension.</summary>
    private static string ResolveFileName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return $"compose-draft-{Guid.NewGuid():N}.docx";
        return displayName.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)
            ? displayName
            : displayName + ".docx";
    }

    /// <summary>A stored terminal-success signal for a step that this request completed inline.</summary>
    private static StoredStepSignal CompletedSignal(string stepName) => new()
    {
        StepName = stepName,
        StoredStatus = JobStatus.Completed,
        Started = true,
    };

    /// <summary>
    /// The DEFERRED profile-analysis signal (Fork C, core-owned). No stored outcome, not started —
    /// projects to <see cref="JobAwareState.Queued"/> (the honest "nothing persisted yet" state,
    /// since the contract has no dedicated Deferred member) with a <c>deferred</c> diagnostic so
    /// the OutcomeCard and a future re-profile pass can distinguish it from a genuinely-queued step.
    /// </summary>
    private static StoredStepSignal DeferredProfileSignal() => new()
    {
        StepName = StepProfileAnalysis,
        StoredStatus = null,
        Started = false,
        Detail = "deferred: core-owned Services/Ai/PublicContracts/IDocumentProfileAi (Fork C) — not implemented in this task",
    };

    /// <summary>Maps the indexing enqueue outcome to a stored step signal: submitted (sync-OBO ran)
    /// → Completed; failed → terminal Failed (single attempt, so never RetryPending); skipped →
    /// non-terminal (no stored outcome) so the record is never a success without an index.</summary>
    private static StoredStepSignal IndexingSignal(PostUploadIndexingResult result)
    {
        if (result.JobSubmitted)
        {
            return new StoredStepSignal { StepName = StepIndexing, StoredStatus = JobStatus.Completed, Started = true };
        }

        if (result.FailureReason is not null)
        {
            return new StoredStepSignal
            {
                StepName = StepIndexing,
                StoredStatus = JobStatus.Failed,
                Started = true,
                Attempt = 1,
                MaxAttempts = 1,   // no retry budget → terminal Failed, not RetryPending
                Detail = $"indexing failed: {result.FailureReason}",
            };
        }

        // Skipped (feature flag off / non-indexable / empty / missing tenant): not indexed →
        // not a terminal success. Keep it non-terminal so the aggregate never reads Completed.
        return new StoredStepSignal
        {
            StepName = StepIndexing,
            StoredStatus = null,
            Started = false,
            Detail = $"indexing skipped: {result.SkipReason}",
        };
    }

    /// <summary>Projects the four create-on-save steps (with profile-analysis deferred) through the
    /// shared <see cref="JobAwareCompletionStateProjector"/>.</summary>
    private static JobAwareCompletionState ProjectCreateOnSaveState(
        string subjectId,
        string correlationId,
        StoredStepSignal containerSignal,
        StoredStepSignal recordSignal,
        StoredStepSignal indexingSignal,
        DateTimeOffset observedAt)
    {
        var job = new JobContract
        {
            JobType = ComposeCreateOnSaveJobType,
            SubjectId = subjectId,
            CorrelationId = correlationId,
            IdempotencyKey = $"compose-create-on-save-{subjectId}",
        };

        var steps = new List<StoredStepSignal>
        {
            containerSignal,
            recordSignal,
            DeferredProfileSignal(),
            indexingSignal,
        };

        return JobAwareCompletionStateProjector.Project(job, steps, observedAt);
    }

    /// <summary>Builds the create-on-save result for a FAILED container step (missing client-supplied
    /// container, or SPE creation returned null): no record, no version, aggregate Failed — never a
    /// success. record/indexing project as non-terminal since they never ran.</summary>
    private SaveComposeDocumentResult BuildContainerFailedResult(
        SaveComposeDocumentRequest request,
        DateTimeOffset observedAt)
    {
        var containerFailed = new StoredStepSignal
        {
            StepName = StepContainer,
            StoredStatus = JobStatus.Failed,
            Started = true,
            Attempt = 1,
            MaxAttempts = 1,
            Detail = "container step failed: no client-supplied ContainerId for a transient draft, or SPE drive-item creation failed",
        };

        var completion = ProjectCreateOnSaveState(
            subjectId: request.DocumentSpeId ?? string.Empty,
            correlationId: request.SessionId,
            containerSignal: containerFailed,
            recordSignal: new StoredStepSignal { StepName = StepRecord, StoredStatus = null, Started = false },
            indexingSignal: new StoredStepSignal { StepName = StepIndexing, StoredStatus = null, Started = false },
            observedAt: observedAt);

        return new SaveComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId ?? string.Empty,
            DriveId = request.DriveId,
            SessionId = request.SessionId,
            DocumentRecordId = null,
            VersionId = string.Empty,
            ETag = null,
            Size = null,
            WasPromotedThisSave = false,
            CompletionState = completion,
        };
    }

    /// <inheritdoc />
    public async Task<PushAnnotationsResult> PushAnnotationsAsync(
        PushAnnotationsRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.DriveId))
            throw new ArgumentException("DriveId is required for SPE drive-item access.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.IfMatch))
            throw new ArgumentException("IfMatch (load-time ETag) is required — a blind overwrite is not offered on the push-annotations path.", nameof(request));
        if (request.Annotations is null || request.Annotations.Count == 0)
            throw new ArgumentException("At least one annotation is required.", nameof(request));

        _logger.LogInformation(
            "Compose push-annotations: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} annotations={AnnotationCount}",
            request.TenantId, request.DriveId, request.DocumentSpeId, request.Annotations.Count);

        // 1) Download the CURRENT bytes via the facade (ADR-007 — no Graph type here).
        var stream = await _spe.DownloadFileAsUserAsync(httpContext, request.DriveId, request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"SPE drive-item not found or unreadable: drive={request.DriveId} item={request.DocumentSpeId}");
        }

        byte[] sourceBytes;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            sourceBytes = buffer.ToArray();
        }

        // 2) Render annotations into native OOXML markup. Pure — no I/O, no AI (ADR-013).
        //    DocxAnnotationException (malformed / target-not-found) propagates to the endpoint,
        //    which maps it to 400 / 422 ProblemDetails. This runs BEFORE the write, so a bad
        //    annotation batch never leaves a partial SPE version.
        var annotatedBytes = _annotationWriter.Annotate(sourceBytes, request.Annotations);

        // 3) Persist with optimistic concurrency (If-Match). A drive-item that moved under the
        //    caller (Word autosave) surfaces as EtagPreconditionFailedException (412); an open
        //    Word co-authoring session surfaces as DocumentLockedByWordException (423). Both
        //    propagate to the endpoint. Nothing partially writes.
        using var annotatedStream = new MemoryStream(annotatedBytes, writable: false);
        var saved = await _spe.ReplaceFileContentAsUserAsync(
                httpContext, request.DriveId, request.DocumentSpeId, annotatedStream, request.IfMatch, cancellationToken)
            .ConfigureAwait(false);

        if (saved is null || string.IsNullOrEmpty(saved.Id))
        {
            throw new InvalidOperationException(
                $"SPE annotated-write failed: drive-item not found or version not returned. drive={request.DriveId} item={request.DocumentSpeId}");
        }

        return new PushAnnotationsResult
        {
            DocumentSpeId = request.DocumentSpeId,
            DriveId = request.DriveId,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            AnnotationCount = request.Annotations.Count,
        };
    }

    // =========================================================================
    // FR-29 anchored annotations (task 060). See design.md §8 + ChatSession.cs
    // class-level remarks on AnchoredAnnotation for the Path-A deviation note.
    // These two methods are the ONLY read/write surface for the two Compose-domain
    // session collections — mutable partial-replace, NOT ledger writes.
    // =========================================================================

    /// <summary>ADR-040 <c>{bindingId}@t{n}</c> ledger-ref shape validator (mirrors <see cref="Ai.PublicContracts.OutcomeCard"/>'s own ledger-key validation intent).</summary>
    private static readonly System.Text.RegularExpressions.Regex LedgerRefPattern =
        new(@"^.+@t\d+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <inheritdoc />
    public async Task<ComposeAnnotationsState> GetComposeAnnotationsAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(sessionId))
            throw new ArgumentException("SessionId is required.", nameof(sessionId));

        var session = await _sessions.GetSessionAsync(tenantId, sessionId, cancellationToken).ConfigureAwait(false);
        return new ComposeAnnotationsState
        {
            AnchoredAnnotations = session?.AnchoredAnnotations ?? Array.Empty<AnchoredAnnotation>(),
            DefinedTermsTracking = session?.DefinedTermsTracking ?? Array.Empty<DefinedTerm>(),
        };
    }

    /// <inheritdoc />
    public async Task<ComposeAnnotationsState> SaveComposeAnnotationsAsync(
        SaveComposeAnnotationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.SessionId))
            throw new ArgumentException("SessionId is required.", nameof(request));

        ValidateLedgerRefs(request.AnchoredAnnotations, request.DefinedTermsTracking);

        var session = await _sessions.GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        if (session is null)
        {
            throw new InvalidOperationException(
                $"Compose session not found: session={request.SessionId} tenant={request.TenantId}. " +
                "Annotations can only be saved onto an existing session (create one via LoadAsync first).");
        }

        // Partial-replace: a null collection on the request leaves the stored collection
        // unchanged; a non-null (possibly empty) collection replaces it wholesale. Mutable
        // by design (accept/reject/edit) — NOT an append to the append-only ledger.
        var updated = session with
        {
            AnchoredAnnotations = request.AnchoredAnnotations ?? session.AnchoredAnnotations,
            DefinedTermsTracking = request.DefinedTermsTracking ?? session.DefinedTermsTracking,
            LastActivity = DateTimeOffset.UtcNow,
        };

        await _sessions.UpdateSessionCacheAsync(updated, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Compose annotations saved: tenant={TenantId} session={SessionId} annotations={AnnotationCount} definedTerms={DefinedTermCount}",
            request.TenantId, request.SessionId,
            updated.AnchoredAnnotations?.Count ?? 0, updated.DefinedTermsTracking?.Count ?? 0);

        return new ComposeAnnotationsState
        {
            AnchoredAnnotations = updated.AnchoredAnnotations ?? Array.Empty<AnchoredAnnotation>(),
            DefinedTermsTracking = updated.DefinedTermsTracking ?? Array.Empty<DefinedTerm>(),
        };
    }

    /// <summary>
    /// Validates that every supplied <see cref="AnchoredAnnotation.Provenance"/> /
    /// <see cref="DefinedTerm.Provenance"/> ledger ref is in ADR-040 <c>{bindingId}@t{n}</c>
    /// form BEFORE anything persists (fail fast — no partial writes).
    /// </summary>
    private static void ValidateLedgerRefs(
        IReadOnlyList<AnchoredAnnotation>? annotations,
        IReadOnlyList<DefinedTerm>? definedTerms)
    {
        if (annotations is not null)
        {
            foreach (var a in annotations)
            {
                if (a.Provenance is not null && !LedgerRefPattern.IsMatch(a.Provenance.LedgerRef))
                {
                    throw new ArgumentException(
                        $"AnchoredAnnotation '{a.Id}' provenance.ledgerRef '{a.Provenance.LedgerRef}' " +
                        "does not match the ADR-040 {bindingId}@t{n} format.",
                        nameof(annotations));
                }
            }
        }

        if (definedTerms is not null)
        {
            foreach (var t in definedTerms)
            {
                if (t.Provenance is not null && !LedgerRefPattern.IsMatch(t.Provenance.LedgerRef))
                {
                    throw new ArgumentException(
                        $"DefinedTerm '{t.Term}' provenance.ledgerRef '{t.Provenance.LedgerRef}' " +
                        "does not match the ADR-040 {bindingId}@t{n} format.",
                        nameof(definedTerms));
                }
            }
        }
    }

    /// <summary>
    /// FR-07 idempotent rebind of a ChatSession's DocumentId. Handles three cases:
    /// (a) current==new (no-op), (b) session missing (returns null), (c) stored already at
    /// target (no-op), (d) rebind applied via ChatSessionManager's cache-write path.
    /// </summary>
    private async Task<ChatSession?> RebindSessionDocumentIdAsync(
        string tenantId,
        string sessionId,
        string currentDocumentId,
        string newDocumentId,
        CancellationToken ct)
    {
        // (a) Caller asked for a no-op.
        if (string.Equals(currentDocumentId, newDocumentId, StringComparison.Ordinal))
        {
            return await _sessions.GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        }

        var session = await _sessions.GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        if (session is null)
        {
            _logger.LogWarning(
                "Compose: rebind called for non-existent session {SessionId} (tenant={TenantId})",
                sessionId, tenantId);
            return null;
        }

        // (c) Stored binding already at target.
        if (string.Equals(session.DocumentId, newDocumentId, StringComparison.Ordinal))
        {
            return session;
        }

        // Out-of-order race: caller-asserted currentDocumentId differs from stored.
        // Proceed with new-value-wins semantics but emit a Warning for operator visibility.
        if (!string.IsNullOrWhiteSpace(currentDocumentId) &&
            !string.Equals(session.DocumentId, currentDocumentId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Compose rebind: caller-asserted currentDocumentId ({CallerCurrent}) differs from stored DocumentId ({StoredCurrent}) for session {SessionId} (tenant={TenantId}); proceeding with rebind to {NewDocumentId} (new-value-wins).",
                currentDocumentId, session.DocumentId, sessionId, tenantId, newDocumentId);
        }

        _logger.LogInformation(
            "Compose: rebinding session {SessionId} DocumentId {From} -> {To} (tenant={TenantId})",
            sessionId, session.DocumentId, newDocumentId, tenantId);

        var rebound = session with
        {
            DocumentId = newDocumentId,
            LastActivity = DateTimeOffset.UtcNow,
        };

        await _sessions.UpdateSessionCacheAsync(rebound, ct).ConfigureAwait(false);
        return rebound;
    }

    /// <summary>
    /// Looks up an existing <c>sprk_document</c> row by SPE drive-item id via the
    /// <c>sprk_graphitemid_uk</c> alternate key. Returns the <c>sprk_documentid</c> or
    /// <c>null</c> when no row exists.
    /// </summary>
    private async Task<Guid?> TryFindDocumentByGraphItemIdAsync(
        string driveItemId,
        CancellationToken cancellationToken)
    {
        var key = new KeyAttributeCollection
        {
            { GraphItemIdAttribute, driveItemId },
        };

        try
        {
            var entity = await _dataverse.RetrieveByAlternateKeyAsync(
                DocumentLogicalName,
                key,
                new[] { DocumentIdAttribute },
                cancellationToken).ConfigureAwait(false);

            return entity?.Id;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "Compose promote alt-key lookup threw InvalidOperationException for driveItem={DocumentSpeId} — treating as not-found",
                driveItemId);
            return null;
        }
    }

    // =========================================================================
    // FR-31 action history — READ-ONLY ledger query (task 061). See design.md §8:
    // the 2026-07-03 draft's `actionLog: ComposeAction[]` stored structure is DELETED —
    // "it IS the session ledger". This adds no new stored surface; it projects the
    // existing ChatSession.Outputs (SessionOutput) + ChatSession.ToolChains
    // (SessionToolChain) ledger collections into a Compose action-history view.
    // =========================================================================

    /// <summary>
    /// FR-31 read-only action-history query: projects Compose's prior actions for a session
    /// directly from the session ledger — <see cref="ChatSession.Outputs"/> (<see cref="SessionOutput"/>
    /// entries, addressable by <c>{bindingId}@t{n}</c>) correlated with
    /// <see cref="ChatSession.ToolChains"/> (<see cref="SessionToolChain"/> entries) for a
    /// best-effort args summary. This is a QUERY over the existing ledger — never a second
    /// stored structure (ADR-040 / FR-31 / design.md §8: the action log IS the session ledger).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Supersession (ADR-040)</b>: within the same <see cref="SessionOutput.BindingId"/>, the
    /// highest-<see cref="SessionOutput.Turn"/> entry is CURRENT; earlier same-binding entries are
    /// SUPERSEDED (<see cref="ComposeActionHistoryEntry.IsSuperseded"/> = <c>true</c>). This
    /// generalizes the compose-disposition undo/replace pattern already established by
    /// <see cref="Ai.PublicContracts.ComposeDisposition.ResolveCurrent"/> (which resolves the
    /// current <c>compose</c>-disposition output for one binding) to EVERY disposition — any
    /// Binding's output can be re-produced within a session (retries, refinements), and this
    /// query always reflects CURRENT ledger state, never a stale copy of it (ADR-040 constraint;
    /// spec FR-31 acceptance criterion 3).
    /// </para>
    /// <para>
    /// <b>Args (best-effort)</b>: <see cref="SessionToolCall.ArgsSummary"/> values are correlated
    /// to an output by matching <see cref="SessionToolChain.Turn"/> to
    /// <see cref="SessionOutput.Turn"/>. This is a best-effort correlation, NOT a guaranteed 1:1
    /// link — the two ledger collections use independently-allocated per-session ordinals (see
    /// <see cref="Ai.OutputRouter"/> remarks on Turn numbering) — so <see cref="ComposeActionHistoryEntry.Args"/>
    /// is <c>null</c> when no ToolChain entry shares the output's turn (e.g. a loop-native output).
    /// </para>
    /// <para>
    /// <b>ADR-013 facade boundary</b>: pure projection over <see cref="ChatSession"/> data already
    /// in hand — no AI executor/routing types, no DI, no I/O. Callers obtain the session via the
    /// existing <see cref="Ai.Chat.ChatSessionManager.GetSessionAsync"/> seam; this method never
    /// reaches into AI internals.
    /// </para>
    /// <para>
    /// <b>ADR-015</b>: no new retention policy — entries live and expire with the session's
    /// existing Tier 3 ledger lifetime (ADR-015 / ADR-040). This method only reads what is
    /// already persisted; it persists nothing itself.
    /// </para>
    /// </remarks>
    /// <param name="session">The Compose session whose ledger to query.</param>
    /// <param name="bindingId">
    /// Optional filter to one Binding's action history. Null (default) returns every binding's
    /// action history recorded in the session.
    /// </param>
    /// <returns>Action-history entries ordered oldest-first by <see cref="SessionOutput.Turn"/>.</returns>
    public static IReadOnlyList<ComposeActionHistoryEntry> GetActionHistory(
        ChatSession session,
        string? bindingId = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        IEnumerable<SessionOutput> outputs = session.Outputs ?? Array.Empty<SessionOutput>();
        if (!string.IsNullOrWhiteSpace(bindingId))
        {
            outputs = outputs.Where(o => string.Equals(o.BindingId, bindingId, StringComparison.Ordinal));
        }

        var materializedOutputs = outputs.ToList();

        // ADR-040 supersession: the highest-Turn entry per BindingId is CURRENT; every
        // earlier same-binding entry is superseded. Computed over ALL outputs for the
        // binding (not just the filtered set) would require the unfiltered collection —
        // but since a bindingId filter already narrows to one binding, computing over
        // materializedOutputs is equivalent whether filtered or not.
        var currentTurnByBinding = materializedOutputs
            .GroupBy(o => o.BindingId, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Max(o => o.Turn), StringComparer.Ordinal);

        var toolChainsByTurn = (session.ToolChains ?? Array.Empty<SessionToolChain>())
            .ToLookup(tc => tc.Turn);

        var entries = new List<ComposeActionHistoryEntry>(materializedOutputs.Count);
        foreach (var output in materializedOutputs)
        {
            var argsSummary = toolChainsByTurn[output.Turn]
                .SelectMany(tc => tc.Calls)
                .Select(c => c.ArgsSummary)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .ToList();

            var isCurrent = currentTurnByBinding.TryGetValue(output.BindingId, out var maxTurn)
                && maxTurn == output.Turn;

            entries.Add(new ComposeActionHistoryEntry
            {
                OutputRef = output.Key,
                BindingId = output.BindingId,
                UcId = output.UcId,
                Disposition = output.Disposition,
                Turn = output.Turn,
                Args = argsSummary.Count > 0 ? argsSummary : null,
                CreatedAt = output.CreatedAt,
                IsSuperseded = !isCurrent,
            });
        }

        return entries
            .OrderBy(e => e.Turn)
            .ToList();
    }
}

/// <summary>
/// FR-31 read-only projection of one ledger action (a <see cref="SessionOutput"/> entry,
/// optionally correlated with a <see cref="SessionToolChain"/> entry's args) for Compose's
/// action-history view. This is a QUERY RESULT, never a stored structure — produced by
/// <see cref="ComposeService.GetActionHistory"/>. There is no persisted <c>actionLog</c> or
/// <c>derivedInsight</c> type anywhere in this codebase (design.md §8 / ADR-040) — this record
/// is transient, constructed fresh from the ledger on every call.
/// </summary>
public sealed record ComposeActionHistoryEntry
{
    /// <summary>Addressable ledger key (<c>{bindingId}@t{n}</c>) of the underlying <see cref="SessionOutput"/>.</summary>
    public required string OutputRef { get; init; }

    /// <summary>Binding (<c>sprk_playbookconsumer</c>) id that produced the output.</summary>
    public required string BindingId { get; init; }

    /// <summary>Stable use-case vocabulary id (<see cref="SessionOutput.UcId"/>).</summary>
    public required string UcId { get; init; }

    /// <summary>
    /// Rendering-contract disposition the output was routed under (<c>informational</c> |
    /// <c>work_product</c> | <c>overlay</c> | <c>email</c> | <c>record</c> | <c>notification</c> |
    /// <c>compose</c>) — see <see cref="SessionOutput.Disposition"/>.
    /// </summary>
    public required string Disposition { get; init; }

    /// <summary>1-based session turn (output ordinal) the action was produced on.</summary>
    public required int Turn { get; init; }

    /// <summary>
    /// Best-effort args summary correlated from a <see cref="SessionToolChain"/> entry sharing
    /// the same Turn (see <see cref="ComposeService.GetActionHistory"/> remarks). Null when no
    /// ToolChain entry correlates with this action's turn.
    /// </summary>
    public IReadOnlyList<string>? Args { get; init; }

    /// <summary>UTC timestamp the underlying output was written to the ledger.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// True when a later-turn <see cref="SessionOutput"/> for the SAME <see cref="BindingId"/>
    /// exists in the session ledger — i.e., this action has been superseded (ADR-040 undo/replace
    /// semantics). The highest-turn entry per binding is CURRENT and authoritative
    /// (<c>IsSuperseded == false</c>).
    /// </summary>
    public required bool IsSuperseded { get; init; }
}
