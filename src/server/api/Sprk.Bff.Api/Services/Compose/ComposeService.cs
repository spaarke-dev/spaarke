using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Models.Ai;
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
    // SPE-pointer + file-metadata columns — logical names mirrored from the canonical
    // OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync write (Services/Office),
    // which maps through Spaarke.Dataverse UpdateDocumentRequest → DataverseWebApiService.
    // WITHOUT these, every downstream reader (open-links, preview) validates the SPE pointer,
    // finds drive-id empty + sprk_hasfile false, and 409s "No file is attached to this document".
    private const string GraphDriveIdAttribute = "sprk_graphdriveid";
    private const string HasFileAttribute = "sprk_hasfile";
    private const string FileSizeAttribute = "sprk_filesize";
    private const string MimeTypeAttribute = "sprk_mimetype";
    private const string FilePathAttribute = "sprk_filepath";

    // FR-05 create-on-save backbone — the consumer-declared ordered step set the
    // JobAwareCompletionStateProjector projects (container → record → profile-analysis → indexing).
    // These string keys are the Compose contract the future OutcomeCard renders; keep stable.
    internal const string StepContainer = "container";
    internal const string StepRecord = "record";
    internal const string StepProfileAnalysis = "profile-analysis";
    internal const string StepIndexing = "indexing";

    // FR-28 push/save pipeline (task 055) — the consumer-declared step set for the
    // push-annotations orchestration: push (native OOXML render) → save (SPE write) →
    // version (new version id confirmed). Mirrors the FR-05 step-naming convention above
    // (stable string keys a future JobAwareCompletionState OutcomeCard renders).
    internal const string StepPush = "push";
    internal const string StepSave = "save";
    internal const string StepVersion = "version";

    private const string ComposeCreateOnSaveJobType = "compose-create-on-save";
    private const string ComposePushSaveJobType = "compose-push-save";
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly ISpeFileOperations _spe;
    private readonly ChatSessionManager _sessions;
    private readonly IGenericEntityService _dataverse;
    private readonly DocxAnnotationWriter _annotationWriter;
    private readonly IPostUploadIndexingEnqueuer _indexing;
    private readonly ILogger<ComposeService> _logger;
    private readonly ComposePushSaveStatusStore? _pushSaveStatusStore;
    // FR-05 Fork C (compose-r2, UAT #7b): the ADR-013-safe profile seam is the OBO-capable
    // IDocumentProfileAi facade (Services/Ai/PublicContracts). ComposeService injects ONLY this
    // facade — NOT IAppOnlyAnalysisService / IOpenAiClient / IPlaybookService / IConsumerRoutingService
    // (ADR013_ComposeFacadeTests). The facade downloads the user-OBO-written SPE file UNDER OBO
    // (the same identity + SPE call the RAG indexing step already uses — MI 403s on it) and reuses
    // the existing extract → classify → summarize → field-map → UpdateDocumentAsync pipeline
    // unchanged. It REPLACES the round-6 AppOnlyDocumentAnalysis Service-Bus enqueue, which silently
    // 403'd on the user-written file (the bug UAT #7b reported).
    //
    // FIRE-AND-FORGET / BACKGROUND (owner-approved, compose-r2): the profile leg is NO LONGER awaited
    // in the create-on-save response path. The full extract → classify → summarize → field-map LLM
    // pipeline runs ~15-40 s; awaiting it blocked the HTTP response for that entire time. SaveAsync now
    // DISPATCHES the profile onto a detached DI scope (IServiceScopeFactory) and returns immediately;
    // the 7 sprk_document profile fields populate shortly AFTER the save returns. OBO is preserved by
    // capturing the caller's bearer token + claims BEFORE returning and threading them into a synthetic
    // HttpContext resolved against the fresh scope (the profile path reads ONLY the Authorization header
    // + User claims from HttpContext — see TokenHelper.ExtractBearerToken / GraphClientFactory.ForUserAsync;
    // the user access token stays valid long enough to exchange after the response). Best-effort: the
    // background task swallows + logs every failure, so a profile miss can never crash the process or
    // affect the already-returned save. Optional + defaults null (same rationale as _pushSaveStatusStore
    // below) so existing 6-arg test constructors keep compiling; the field is now the availability GATE
    // (null → nothing to dispatch), while the actual run resolves a FRESH facade from the detached scope.
    private readonly IDocumentProfileAi? _documentProfileAi;
    // compose-r2 FR-30 (#629): durable Record-scope memory-capture facade (ADR-013). Optional + defaults
    // null so existing test constructors keep compiling; DI resolves the real ComposeMemoryCapture in every
    // non-test host. Null → the STEP 5 capture below is a clean no-op (best-effort availability gate).
    private readonly IComposeMemoryCapture? _memoryCapture;
    // Fire-and-forget profile dispatch (compose-r2): a NEW DI scope is created per background profile so
    // the profile facade + its scoped deps never touch the disposing request scope. Optional + defaults
    // null so existing test constructors compile; DI always resolves it in every non-test host.
    private readonly IServiceScopeFactory? _scopeFactory;
    // App-shutdown token for the detached profile task — NEVER the request CancellationToken (which
    // cancels when the response completes). Optional; null → CancellationToken.None.
    private readonly IHostApplicationLifetime? _appLifetime;
    // FR-08 (task 010, E2): load-time w14:paraId pre-parse. Optional + defaults to a fresh instance so
    // existing test constructors compile unchanged; DI resolves the registered singleton in every host.
    // Stateless + thread-safe — a default construction is functionally identical to the DI singleton.
    private readonly ParaIdPreParser _paraIdPreParser;

    public ComposeService(
        ISpeFileOperations spe,
        ChatSessionManager sessions,
        IGenericEntityService dataverse,
        DocxAnnotationWriter annotationWriter,
        IPostUploadIndexingEnqueuer indexing,
        ILogger<ComposeService> logger,
        IDistributedCache? cache = null,
        IDocumentProfileAi? documentProfileAi = null,
        IServiceScopeFactory? scopeFactory = null,
        IHostApplicationLifetime? appLifetime = null,
        IComposeMemoryCapture? memoryCapture = null,
        ParaIdPreParser? paraIdPreParser = null)
    {
        _spe = spe;
        _sessions = sessions;
        _dataverse = dataverse;
        _annotationWriter = annotationWriter;
        _indexing = indexing;
        _logger = logger;
        _documentProfileAi = documentProfileAi;
        _scopeFactory = scopeFactory;
        _paraIdPreParser = paraIdPreParser ?? new ParaIdPreParser();
        _appLifetime = appLifetime;
        _memoryCapture = memoryCapture;
        // ADR-009: cross-request push/save job state lives in Redis, never IMemoryCache.
        // Optional + defaults null so existing 6-arg test constructors (sibling task suites
        // 013/050/060/062/102) keep compiling unchanged; DI (AddScoped<IComposeService,
        // ComposeService>) resolves the real IDistributedCache registered via
        // AddStackExchangeRedisCache in every non-test host, so production always persists.
        _pushSaveStatusStore = cache is not null ? new ComposePushSaveStatusStore(cache) : null;
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

        // FR-08 (E2, task 010): pre-parse the load-time bytes for the ordered w14:paraId map alongside
        // the (client-side) mammoth convert, which discards paraId. Single Open XML pass (NFR-08).
        // Best-effort — a malformed/unreadable source must NOT fail Load (which historically did no
        // server-side parse of the content); the client degrades to fuzzy-only anchoring (task 012)
        // when the map is empty.
        IReadOnlyList<ParaIdMapEntry> paraIdMap;
        try
        {
            paraIdMap = _paraIdPreParser.Parse(content).Entries;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: paraId pre-parse failed for drive={DriveId} item={DocumentSpeId}; returning empty map",
                request.DriveId, request.DocumentSpeId);
            paraIdMap = Array.Empty<ParaIdMapEntry>();
        }

        // 3) Ensure a ChatSession bound to the document. For Path A (Document row present),
        //    bind to sprk_documentid; for Path B continuation, bind to the SPE drive-item id.
        var bindingId = request.DocumentRecordId.HasValue
            ? request.DocumentRecordId.Value.ToString()
            : request.DocumentSpeId;

        // FR-29 (R2, design.md §8) + FR-33 (task 062): if the caller supplies a known prior
        // SessionId bound to THIS SAME cross-version key — DocumentId (bindingId, already
        // version-independent: sprk_documentid or the SPE drive-item id, NEVER a DOCX version
        // identifier) AND, when supplied, MatterId — RESUME it instead of minting a new one.
        // This is what carries AnchoredAnnotations/DefinedTermsTracking/action-history forward
        // across a document re-open (design.md §8 "Cross-version persistence": bound to
        // `DocumentId + MatterId`, NOT to a specific DOCX version — a Word save that produces a
        // new version never changes this key). A mismatched or missing session falls back to
        // the R1 mint-new behavior unchanged (purely additive; see
        // <see cref="IsSameCrossVersionBinding"/>).
        ChatSession? session = null;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var candidate = await _sessions.GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
                .ConfigureAwait(false);
            if (candidate is not null && IsSameCrossVersionBinding(candidate, bindingId, request.MatterId))
            {
                session = candidate;
                _logger.LogDebug(
                    "Compose load: resumed existing session {SessionId} bound to document={BindingId} matter={MatterId} (tenant={TenantId}) — restoring {AnnotationCount} annotation(s), {DefinedTermCount} defined term(s), {OutputCount} ledger output(s)",
                    session.SessionId, bindingId, request.MatterId, request.TenantId,
                    session.AnchoredAnnotations?.Count ?? 0, session.DefinedTermsTracking?.Count ?? 0,
                    session.Outputs?.Count ?? 0);
            }
        }

        session ??= await _sessions.CreateSessionAsync(
                tenantId: request.TenantId,
                documentId: bindingId,
                playbookId: null,
                hostContext: BuildMatterHostContext(request.MatterId),
                ct: cancellationToken)
            .ConfigureAwait(false);

        // FR-33 (task 062, design.md §8): restore prior decisions from the ledger alongside the
        // FR-29 annotations — task 061's read-only GetActionHistory query over the resumed
        // session's Outputs/ToolChains. No new stored structure (ADR-040); a freshly-minted
        // session naturally has an empty ledger.
        var actionHistory = GetActionHistory(session);

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
            ActionHistory = actionHistory,
            ParaIdMap = paraIdMap,
        };
    }

    /// <summary>
    /// FR-33 (design.md §8) cross-version session-binding predicate: a resumed session must
    /// match the SAME <c>DocumentId</c> binding (<paramref name="bindingId"/> — version
    /// independent by construction; see <see cref="LoadAsync"/> remarks) AND, when the caller
    /// supplies a <paramref name="matterId"/>, the SAME Matter — read from the candidate
    /// session's <see cref="ChatHostContext"/> (canonical <c>EntityType == "matter"</c> per
    /// <see cref="Models.Ai.Chat.EntityTypeNormalizer"/>, <c>EntityId == matterId</c>). A
    /// <c>null</c>/whitespace <paramref name="matterId"/> preserves the FR-29 DocumentId-only
    /// match (backward compatible with callers that predate FR-33). This augments the EXISTING
    /// caller-supplied-SessionId resume path — no new lookup index, no parallel session cache
    /// (ADR-040).
    /// </summary>
    private static bool IsSameCrossVersionBinding(ChatSession candidate, string bindingId, string? matterId)
    {
        if (!string.Equals(candidate.DocumentId, bindingId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(matterId))
        {
            return true;
        }

        return candidate.HostContext is { } hostContext
            && string.Equals(hostContext.EntityType, ParentEntityContext.EntityTypes.Matter, StringComparison.Ordinal)
            && string.Equals(hostContext.EntityId, matterId, StringComparison.Ordinal);
    }

    /// <summary>
    /// FR-33 (design.md §8): seeds a new Compose session's <see cref="ChatHostContext"/> with
    /// the Matter binding when the caller supplies one, so the NEXT <see cref="LoadAsync"/>
    /// call for the same document + matter can resume via
    /// <see cref="IsSameCrossVersionBinding"/>. Returns <c>null</c> (R1 behavior, unchanged)
    /// when no <paramref name="matterId"/> is supplied.
    /// </summary>
    private static ChatHostContext? BuildMatterHostContext(string? matterId) =>
        string.IsNullOrWhiteSpace(matterId)
            ? null
            : new ChatHostContext(EntityType: ParentEntityContext.EntityTypes.Matter, EntityId: matterId);

    /// <inheritdoc />
    public async Task<SaveComposeDocumentResult> SaveAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        // SessionId is OPTIONAL (task 110): the Browse/local-file first Save legitimately has no
        // chat session. The FR-07 rebind this would drive is skipped below when SessionId is
        // absent (empty/whitespace). TenantId + Content remain hard preconditions.
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));
        if (request.Content.IsEmpty)
            throw new ArgumentException("Content is required and must be non-empty.", nameof(request));

        // ────────────────────────────────────────────────────────────────────────────
        // FR-06a upload fidelity (task 015) + REDLINE/COMMENT fidelity (UAT-R7 #2/#3/#4):
        // the choice between the pristine ORIGINAL upload bytes and the tipTapToDocxBytes-
        // regenerated bytes is made by the CALLER (ComposeWorkspace.tsx triggerSave), keyed off
        // the editor's own dirty flag — an unedited mount sends the retained original bytes
        // byte-identical; an edited mount sends the regenerated .docx. SaveAsync treats
        // request.Content as an OPAQUE, already-decided BASELINE byte payload and MUST NOT
        // re-encode/re-wrap it.
        //
        // The ONE deliberate transform (UAT-R7 #2/#3/#4, owner-approved): when request.Annotations
        // is non-empty, the caller's Content is a clean BASELINE (pending redlines reduced to their
        // reject-state text; accepted edits already baked in) and this method re-applies the pending
        // track-changes + comments as NATIVE OOXML (w:ins/w:del/w:comment) via the SAME proven
        // DocxAnnotationWriter the push path uses (PushAnnotationsAsync) — BEFORE any SPE write. This
        // fixes the flatten-to-plain-text defect where the client docx writer, which has no
        // track-change branch, wrote both original + AI text as plain body text. An EMPTY/null
        // annotation list persists the baseline byte-identical (a plain no-redline Save is unchanged;
        // the FR-06a byte-identity invariant still holds — see ComposeServiceUploadFidelityTests.cs).
        // The render is pure/in-memory; a bad batch (malformed / target-not-found) throws
        // DocxAnnotationException BEFORE the write, so no partial SPE version can land.
        // ────────────────────────────────────────────────────────────────────────────
        var observedAt = DateTimeOffset.UtcNow;
        var isTransientCreate = string.IsNullOrWhiteSpace(request.DocumentSpeId);

        byte[] contentToPersist = request.Content.ToArray();
        if (request.Annotations is { Count: > 0 })
        {
            _logger.LogInformation(
                "Compose save: applying {AnnotationCount} redline/comment annotation(s) to the baseline before persist (session={SessionId}).",
                request.Annotations.Count, request.SessionId);
            contentToPersist = _annotationWriter.Annotate(contentToPersist, request.Annotations);
        }

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
            using var createStream = new MemoryStream(contentToPersist, writable: false);
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

            using var contentStream = new MemoryStream(contentToPersist, writable: false);
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
            // Thread the already-computed SPE pointer + file metadata into the record write so
            // the created sprk_document is COMPLETE (drive-id + has-file + size/mime/filepath),
            // mirroring OfficeDocumentPersistence. The SPE upload above already produced these;
            // this only carries them forward — no new upload logic.
            GraphDriveId = effectiveDriveId,
            FileName = fileName,
            FileSize = saved.Size ?? contentToPersist.Length,
            MimeType = DocxContentType,
            FilePath = saved.WebUrl,
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
                    FileSizeBytes: saved.Size ?? contentToPersist.Length,
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
        // STEP 3 — profile-analysis (FR-05 Fork C, compose-r2, UAT #7b — now FIRE-AND-FORGET).
        // The Compose user wrote the file, so profiling MUST run UNDER OBO (Pattern 4) — a background
        // AppOnlyDocumentAnalysis (MI) job would 403 on the download (that was the round-6 bug: profile
        // fields silently never populated). But awaiting the full extract → classify → summarize →
        // field-map LLM pipeline (~15-40 s) INLINE blocked this HTTP response for that entire time.
        //
        // So the profile is now DISPATCHED to a detached DI scope and NOT awaited: SaveAsync returns
        // immediately, and the OBO-capable IDocumentProfileAi facade runs the SAME pipeline in the
        // background, writing the 7 sprk_document profile fields shortly AFTER this save returns. OBO is
        // preserved by capturing the caller's bearer token + claims before returning (see
        // DispatchBackgroundProfile). Best-effort: the background task swallows + logs every failure, so
        // it can never fail or block the save. Because the outcome is not known synchronously, the
        // returned profile step is a non-terminal "dispatched" (Running) signal — the record is a valid
        // interim success (container + record + indexing) with the profile still in flight.
        // ────────────────────────────────────────────────────────────────────────────
        var profileSignal = promotion.DocumentRecordId.HasValue
            ? DispatchBackgroundProfile(promotion.DocumentRecordId.Value, httpContext)
            : ProfileNotAttemptedSignal("no sprk_document record id resolved — profile not attempted");

        // ────────────────────────────────────────────────────────────────────────────
        // STEP 5 — durable memory capture (FR-30, compose-r2, deferral #629). Best-effort: distil the
        // session's durable insights (defined terms today) into Record-scope MemoryItems keyed by the
        // newly-saved sprk_document, via the ADR-013 IComposeMemoryCapture facade (shared IMemoryItemStore,
        // no forked store). Runs ONLY when a sprk_document id was resolved. The untrusted-origin gate is
        // DEFERRED to the memory-governance project (#629); TrustLevel is carried inert. This NEVER affects
        // the returned Save result or the completion-state projection — a capture miss is silently logged.
        // ────────────────────────────────────────────────────────────────────────────
        if (promotion.DocumentRecordId.HasValue)
        {
            await CaptureDocumentMemoryAsync(
                    promotion.DocumentRecordId.Value, request.TenantId, request.SessionId, cancellationToken)
                .ConfigureAwait(false);
        }

        // ────────────────────────────────────────────────────────────────────────────
        // Project per-step states (container → record → profile-analysis → indexing)
        // through the shared JobAwareCompletionStateProjector. A fileless/unindexed record can
        // never be a success (aggregate Failed/Partial). The profile step is DISPATCHED to the
        // background above (fire-and-forget, not awaited): in the returned response it is a non-terminal
        // "dispatched"/Running signal, so the synchronous aggregate reads Partial (record + index exist,
        // profile pending) and never demotes to Failed on a best-effort profile (Fork C, compose-r2).
        // ────────────────────────────────────────────────────────────────────────────
        var completion = ProjectCreateOnSaveState(
            subjectId: effectiveSpeId,
            correlationId: httpContext.TraceIdentifier,
            containerSignal: CompletedSignal(StepContainer),
            recordSignal: CompletedSignal(StepRecord),
            profileSignal: profileSignal,
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

    /// <summary>
    /// STEP 5 (FR-30, compose-r2, #629) — best-effort durable memory CAPTURE. Distils the bound session's
    /// durable insights (defined terms today) into Record-scope memory keyed by the saved
    /// <c>sprk_document</c>, via the ADR-013 <see cref="IComposeMemoryCapture"/> facade. The whole body is
    /// guarded so a memory-capture failure NEVER throws — a Save must never be blocked or failed by it. A
    /// no-op when the facade is unregistered (null gate) or no session is bound.
    /// </summary>
    private async Task CaptureDocumentMemoryAsync(
        Guid documentId,
        string tenantId,
        string? sessionId,
        CancellationToken ct)
    {
        // Availability gate + precondition: no facade (AI/persistence off in this host) or no bound
        // session → nothing to capture. Both are clean no-ops, not failures.
        if (_memoryCapture is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            var session = await _sessions.GetSessionAsync(tenantId, sessionId, ct).ConfigureAwait(false);
            var definedTerms = session?.DefinedTermsTracking;
            if (definedTerms is null || definedTerms.Count == 0)
            {
                return;
            }

            // DISTILL: a defined term is durable knowledge only when it carries BOTH a non-empty label
            // (the fact Key) AND a definition (the fact Value). Guarding Term too avoids persisting an
            // empty-key fact (two of which would collide on the store's hashed empty key).
            var insights = new List<ComposeInsight>(definedTerms.Count);
            foreach (var term in definedTerms)
            {
                if (string.IsNullOrWhiteSpace(term.Term) || string.IsNullOrWhiteSpace(term.Definition))
                {
                    continue;
                }

                insights.Add(new ComposeInsight(
                    FactType: "defined-term",
                    Key: term.Term,
                    Value: term.Definition!,
                    Origin: string.Equals(term.Source, "ai", StringComparison.OrdinalIgnoreCase)
                        ? MemoryOrigin.AiDerived
                        : MemoryOrigin.User,
                    // Confidence null → the store default (1.0), which keeps AI-extracted terms above the
                    // recall confidence gate so they DO surface in later sessions (the FR-30 point).
                    // ConfirmedByUser=false (set in the facade) is the honest "unverified" marker; DefinedTerm
                    // carries no per-term confidence signal to thread here.
                    Confidence: null,
                    BindingId: term.Provenance?.BindingId,
                    LedgerRef: term.Provenance?.LedgerRef));
            }

            if (insights.Count == 0)
            {
                return;
            }

            var outcome = await _memoryCapture.CaptureRecordInsightsAsync(
                    subjectType: "sprk_document",
                    subjectId: documentId.ToString(),
                    insights: insights,
                    provenance: new ComposeMemoryProvenance
                    {
                        TenantId = tenantId,
                        SessionId = sessionId,
                        CreatedBy = null,   // caller identity not threaded here; envelope stays honest (null)
                    },
                    ct)
                .ConfigureAwait(false);

            _logger.LogDebug(
                "Compose memory-capture (FR-30): document {DocumentId} — {Status} {Count} insight(s) (session={SessionId}).",
                documentId, outcome.Status, outcome.Count, sessionId);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Honour cancellation quietly — the Save has already returned its result on its own terms.
        }
        catch (Exception ex)
        {
            // Best-effort: a memory-capture failure must never fail or block a Save (swallow + log).
            _logger.LogWarning(ex,
                "Compose memory-capture (FR-30): threw while capturing memory for document {DocumentId} — best-effort, Save unaffected.",
                documentId);
        }
    }

    /// <inheritdoc />
    public async Task<PromoteComposeDocumentResult> PromoteIfEphemeralAsync(
        PromoteComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DocumentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(request));
        // SessionId is OPTIONAL (task 110): the ephemeral→promoted rebind is skipped when no
        // session is bound (transient Browse/local-file first Save). See the conditional rebinds below.
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

            // FR-07 rebind is OPTIONAL (task 110): skip entirely when no session is bound
            // (transient Browse/local-file first Save). RebindSessionDocumentIdAsync is already
            // null-tolerant, but skipping avoids an empty-session lookup + a misleading warn.
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                await RebindSessionDocumentIdAsync(
                        tenantId: request.TenantId,
                        sessionId: request.SessionId,
                        currentDocumentId: request.DocumentSpeId,
                        newDocumentId: existingId.Value.ToString(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            return new PromoteComposeDocumentResult
            {
                DocumentSpeId = request.DocumentSpeId,
                SessionId = request.SessionId,
                DocumentRecordId = existingId.Value,
                WasCreated = false,
            };
        }

        // 2) Create the sprk_document row.
        //    The record MUST carry the full SPE pointer + file metadata (drive-id + has-file +
        //    size/mime/filepath), NOT just the item-id — otherwise downstream readers (open-links,
        //    preview) validate the pointer, find drive-id empty + sprk_hasfile false, and 409
        //    "No file is attached to this document yet." Field set mirrors the canonical
        //    OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync write.
        var entity = new Entity(DocumentLogicalName);
        entity[GraphItemIdAttribute] = request.DocumentSpeId;
        var effectiveDisplayName = !string.IsNullOrWhiteSpace(request.DisplayName)
            ? request.DisplayName!
            : $"Compose document ({request.DocumentSpeId})";
        entity[DisplayNameAttribute] = effectiveDisplayName;

        // Prefer the resolved file name (carries the .docx extension); fall back to the display
        // name for standalone promote callers that supply neither.
        var effectiveFileName = !string.IsNullOrWhiteSpace(request.FileName)
            ? request.FileName!
            : request.DisplayName;
        if (!string.IsNullOrWhiteSpace(effectiveFileName))
        {
            entity[FileNameAttribute] = effectiveFileName!;
        }

        // SPE drive pointer — the field whose absence is the root cause of the 409s.
        if (!string.IsNullOrWhiteSpace(request.GraphDriveId))
        {
            entity[GraphDriveIdAttribute] = request.GraphDriveId!;
        }

        // A promoted Compose document always has an SPE file behind it (the drive-item id is a
        // hard precondition of this method). Mark it so downstream readers stop rejecting it.
        entity[HasFileAttribute] = true;

        if (request.FileSize.HasValue)
        {
            // sprk_filesize is a Whole Number (int) column; the OrganizationService write path is
            // strict about CLR type, so cast (same as OfficeDocumentPersistence / DataverseServiceClientImpl).
            entity[FileSizeAttribute] = (int)request.FileSize.Value;
        }
        if (!string.IsNullOrWhiteSpace(request.MimeType))
        {
            entity[MimeTypeAttribute] = request.MimeType!;
        }
        if (!string.IsNullOrWhiteSpace(request.FilePath))
        {
            entity[FilePathAttribute] = request.FilePath!;
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
        //    OPTIONAL (task 110): skip when no session is bound (transient Browse/local-file
        //    first Save). The sprk_document create above already completed without a session.
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            await RebindSessionDocumentIdAsync(
                    tenantId: request.TenantId,
                    sessionId: request.SessionId,
                    currentDocumentId: request.DocumentSpeId,
                    newDocumentId: newId.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
        }

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
    // DISPATCHED FIRE-AND-FORGET under OBO via the ADR-013-safe IDocumentProfileAi facade (compose-r2) —
    // captured OBO token + fresh DI scope, because a background MI job 403s on the user-OBO-written file.
    // In the synchronous response the profile step is a non-terminal "dispatched" (Running) signal, so
    // the aggregate reads Partial (record + index exist, profile pending) and never reads Failed on a
    // best-effort profile miss (which happens off-thread and is only logged).
    // =========================================================================

    /// <summary>
    /// The interim R5-E success bar for FR-05 create-on-save (documented exception, 2026-07-09):
    /// a record is interim-successful when the <c>container</c>, <c>record</c>, AND <c>indexing</c>
    /// steps all reached terminal success — a record with no SPE file OR no index is NEVER a success.
    /// <c>profile-analysis</c> is intentionally EXCLUDED from this bar so a best-effort profile miss
    /// never demotes an otherwise-good save. Since the profile now runs FIRE-AND-FORGET in the
    /// background (<see cref="DispatchBackgroundProfile"/>), the synchronous create-on-save response
    /// carries a non-terminal "dispatched" profile step — so the interim bar (container + record +
    /// indexing) is the operative success bar for the returned aggregate; the profile fields land
    /// shortly after, off the response path.
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
    /// FR-05 Fork C (compose-r2): DISPATCHES a best-effort OBO document-profile onto a detached DI
    /// scope (fire-and-forget) for a newly-created (or idempotently-resolved) <c>sprk_document</c>, and
    /// returns the non-terminal "dispatched" profile-analysis step signal WITHOUT awaiting the profile.
    /// The background task (<see cref="RunBackgroundProfileAsync"/>) downloads the user-OBO-written SPE
    /// file as the user (a background MI job would 403 — the round-6 bug) and reuses the existing
    /// extract → classify → summarize → field-map → UpdateDocumentAsync pipeline; its 7 profile fields
    /// land shortly AFTER the save returns.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Why a detached scope (constraint #1):</b> the request DI scope disposes when the HTTP
    /// response returns, so the background profile MUST resolve the facade from a FRESH
    /// <see cref="IServiceScopeFactory.CreateScope"/> — never a service captured from the request scope.
    /// </para>
    /// <para>
    /// <b>How OBO survives (constraint #2):</b> the profile path reads ONLY the <c>Authorization</c>
    /// header (the OBO user assertion) + <c>User</c> claims + <c>TraceIdentifier</c> from
    /// <see cref="HttpContext"/> (see <c>TokenHelper.ExtractBearerToken</c> /
    /// <c>GraphClientFactory.ForUserAsync</c>). Those three are captured HERE, before the request scope
    /// disposes, and threaded into a synthetic <see cref="DefaultHttpContext"/> in the background task —
    /// the user access token stays valid long enough to exchange after the response. If the save request
    /// carried no bearer token, a background OBO download would 401, so we DO NOT dispatch a doomed task:
    /// we skip cleanly with a non-terminal not-attempted signal instead.
    /// </para>
    /// <para>
    /// Best-effort by design: a null facade / scope factory (test host, AI-gate off) or a missing bearer
    /// token returns a NON-terminal signal, and any background failure is swallowed + logged
    /// (<see cref="RunBackgroundProfileAsync"/>) — the synchronous save is never blocked, and its
    /// aggregate is never demoted to Failed by a profile miss. ADR-013: injects NO AI-internal type.
    /// </para>
    /// </remarks>
    private StoredStepSignal DispatchBackgroundProfile(Guid documentId, HttpContext httpContext)
    {
        // Availability gate: no scope factory (unit-test host) or no facade registered (compound AI gate
        // off) → nothing to dispatch. Report non-terminal not-attempted synchronously.
        if (_scopeFactory is null || _documentProfileAi is null)
        {
            return ProfileNotAttemptedSignal(
                "profile facade unavailable (no IDocumentProfileAi / IServiceScopeFactory) — profile not dispatched");
        }

        // Capture the OBO user assertion (raw Authorization header) BEFORE the request scope disposes.
        // Non-throwing (unlike TokenHelper.ExtractBearerToken) so a token-less save degrades cleanly.
        var authorizationHeader = httpContext.Request?.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(authorizationHeader)
            || !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            // No user token to detach — a background OBO download would 401. Never ship a broken detach.
            _logger.LogWarning(
                "Compose create-on-save: no bearer Authorization header on the save request for document {DocumentId} — background OBO profile not dispatched (best-effort skip).",
                documentId);
            return ProfileNotAttemptedSignal(
                "no bearer Authorization header on the save request — background OBO profile not dispatched");
        }

        // Capture the remaining request-scoped context the profile facade reads. A ClaimsPrincipal is a
        // plain object with no request-tied lifetime, so retaining the reference across the response is safe.
        var user = httpContext.User;
        var correlationId = httpContext.TraceIdentifier;

        // Fire-and-forget: detach from the request scope AND the request CancellationToken (constraint #3).
        // The task is unobserved by design; RunBackgroundProfileAsync owns its own try/catch so nothing
        // faults the finalizer thread. The discard makes the intent explicit to reviewers + analyzers.
        _ = Task.Run(() => RunBackgroundProfileAsync(documentId, authorizationHeader, user, correlationId));

        return ProfileDispatchedSignal();
    }

    /// <summary>
    /// The detached (fire-and-forget) body of the best-effort OBO document-profile. Creates a NEW DI
    /// scope, rebuilds a minimal <see cref="HttpContext"/> from the captured OBO token + claims, resolves
    /// the <see cref="IDocumentProfileAi"/> facade FROM THAT SCOPE, and runs the profile. NEVER throws —
    /// a background profiling failure must not crash the process (unobserved-exception safe, constraint #4).
    /// </summary>
    private async Task RunBackgroundProfileAsync(
        Guid documentId,
        string authorizationHeader,
        ClaimsPrincipal? user,
        string correlationId)
    {
        // Constraint #3: use the app-shutdown token, NOT the (already-completed) request token. A profile
        // in flight when the host stops is cut off cleanly; otherwise it runs to completion.
        var ct = _appLifetime?.ApplicationStopping ?? CancellationToken.None;

        try
        {
            await using var scope = _scopeFactory!.CreateAsyncScope();

            // Rebuild a minimal HttpContext carrying ONLY what the profile path reads: the OBO user
            // assertion (Authorization header), the User claims (runContext.TenantId + tenant-cache
            // scoping), and the correlation id. Backed by the fresh scope's provider so any
            // RequestServices lookup resolves against the detached (non-disposed) scope.
            var detachedContext = new DefaultHttpContext
            {
                RequestServices = scope.ServiceProvider,
                TraceIdentifier = correlationId,
            };
            detachedContext.Request.Headers.Authorization = authorizationHeader;
            if (user is not null)
            {
                detachedContext.User = user;
            }

            // Keep AnalysisDocumentLoader's IHttpContextAccessor-based tenant-cache scoping coherent
            // (else it degrades to the documented "system" sentinel — acceptable, but this is tidier).
            var accessor = scope.ServiceProvider.GetService<IHttpContextAccessor>();
            if (accessor is not null)
            {
                accessor.HttpContext = detachedContext;
            }

            // Constraint #1: resolve the facade from the NEW scope — never the request-scope service.
            var facade = scope.ServiceProvider.GetService<IDocumentProfileAi>();
            if (facade is null)
            {
                _logger.LogWarning(
                    "Compose background profile: IDocumentProfileAi did not resolve from the detached scope for document {DocumentId} (correlation={CorrelationId}) — skipped.",
                    documentId, correlationId);
                return;
            }

            _logger.LogInformation(
                "Compose background profile: starting best-effort OBO profile for document {DocumentId} (correlation={CorrelationId}).",
                documentId, correlationId);

            var result = await facade.ProfileDocumentAsUserAsync(documentId, detachedContext, ct)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Compose background profile: document {DocumentId} — success={Success} (failure={Failure} skip={Skip}) (correlation={CorrelationId}). Profile fields populate on the record now.",
                documentId, result.Success, result.FailureReason ?? "(none)", result.SkipReason ?? "(none)", correlationId);
        }
        catch (Exception ex)
        {
            // Constraint #4: unobserved-exception safe. The save already returned on its own terms; a
            // best-effort background profile failure is logged and swallowed, never rethrown.
            _logger.LogError(ex,
                "Compose background profile: threw while profiling document {DocumentId} (correlation={CorrelationId}) — best-effort, the save is unaffected.",
                documentId, correlationId);
        }
    }

    /// <summary>Non-terminal (Running) profile-analysis signal for the fire-and-forget path: the profile
    /// was DISPATCHED to a background scope and runs best-effort AFTER the save returns (the 7
    /// sprk_document profile fields populate shortly after). <c>Started=true</c> + no stored status →
    /// projects to <see cref="JobAwareState.Running"/>, so the returned aggregate reads Partial (record +
    /// index exist, profile pending), never Completed-on-claim and never Failed.</summary>
    private static StoredStepSignal ProfileDispatchedSignal() => new()
    {
        StepName = StepProfileAnalysis,
        StoredStatus = null,
        Started = true,
        Detail = "profile-analysis dispatched to background (best-effort); the profile fields populate shortly after the save returns",
    };

    /// <summary>A non-terminal profile-analysis signal for when the profile was NOT attempted
    /// (container/record step never produced a record, or no facade injected). Non-terminal so it
    /// never poisons the create-on-save aggregate.</summary>
    private static StoredStepSignal ProfileNotAttemptedSignal(string detail) => new()
    {
        StepName = StepProfileAnalysis,
        StoredStatus = null,
        Started = false,
        Detail = detail,
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
        StoredStepSignal profileSignal,
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
            profileSignal,
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
            // Container failed → no record → nothing to profile. Non-terminal so the aggregate stays
            // Failed (driven by the container step), not double-counted.
            profileSignal: ProfileNotAttemptedSignal("profile not attempted: container step failed"),
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

        var observedAt = DateTimeOffset.UtcNow;

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

        // ────────────────────────────────────────────────────────────────────────────
        // FR-28 (task 055) — push/save pipeline with per-step JobAwareCompletionState
        // projection (push → save → version). A failure at either step aborts the pipeline
        // with NO partial write: the SPE write only happens after the pure Annotate render
        // fully succeeds (an in-memory transform — nothing is sent to SPE until step 3), and
        // ReplaceFileContentAsUserAsync's If-Match is atomic at the Graph boundary (the whole
        // new version lands or the whole call is rejected — never a half-applied version).
        // Every branch below persists the resulting per-step state to Redis (ADR-009) before
        // propagating/returning, so a future job-aware OutcomeCard has real state to read
        // regardless of outcome.
        // ────────────────────────────────────────────────────────────────────────────

        // 2) Render annotations into native OOXML markup. Pure — no I/O, no AI (ADR-013).
        //    DocxAnnotationException (malformed / target-not-found) propagates to the endpoint,
        //    which maps it to 400 / 422 ProblemDetails. This runs BEFORE the write, so a bad
        //    annotation batch never leaves a partial SPE version.
        byte[] annotatedBytes;
        try
        {
            annotatedBytes = _annotationWriter.Annotate(sourceBytes, request.Annotations);
        }
        catch (Exception ex)
        {
            await PersistPushSaveStatusAsync(
                    request.DocumentSpeId,
                    FailedSignal(StepPush, $"push failed: {ex.Message}"),
                    NotStartedSignal(StepSave),
                    NotStartedSignal(StepVersion),
                    observedAt,
                    cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        // 3) Persist with optimistic concurrency (If-Match). A drive-item that moved under the
        //    caller (Word autosave) surfaces as EtagPreconditionFailedException (412); an open
        //    Word co-authoring session surfaces as DocumentLockedByWordException (423). Both
        //    propagate to the endpoint. Nothing partially writes.
        using var annotatedStream = new MemoryStream(annotatedBytes, writable: false);
        FileHandleDto? saved;
        try
        {
            saved = await _spe.ReplaceFileContentAsUserAsync(
                    httpContext, request.DriveId, request.DocumentSpeId, annotatedStream, request.IfMatch, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await PersistPushSaveStatusAsync(
                    request.DocumentSpeId,
                    CompletedSignal(StepPush),
                    FailedSignal(StepSave, $"save failed: {ex.Message}"),
                    NotStartedSignal(StepVersion),
                    observedAt,
                    cancellationToken)
                .ConfigureAwait(false);
            throw;
        }

        if (saved is null || string.IsNullOrEmpty(saved.Id))
        {
            await PersistPushSaveStatusAsync(
                    request.DocumentSpeId,
                    CompletedSignal(StepPush),
                    FailedSignal(StepSave, "SPE annotated-write returned no version id"),
                    NotStartedSignal(StepVersion),
                    observedAt,
                    cancellationToken)
                .ConfigureAwait(false);
            throw new InvalidOperationException(
                $"SPE annotated-write failed: drive-item not found or version not returned. drive={request.DriveId} item={request.DocumentSpeId}");
        }

        // 4) All three steps landed — persist the terminal-success state.
        var completion = await PersistPushSaveStatusAsync(
                request.DocumentSpeId,
                CompletedSignal(StepPush),
                CompletedSignal(StepSave),
                CompletedSignal(StepVersion),
                observedAt,
                cancellationToken)
            .ConfigureAwait(false);

        // 5) Tier-2c preview echo (design.md §2.4 / HANDOFF §4): comment/track-change counts +
        //    the Word-vs-Compose split, attached as post-write completion evidence. The SAME
        //    calculator backs the pre-confirm PreviewPushAnnotationsAsync path below.
        var composeOnlyCount = await ResolveComposeOnlyCountAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        var preview = ComposePushSavePreviewCalculator.Compute(request.Annotations, composeOnlyCount);

        return new PushAnnotationsResult
        {
            DocumentSpeId = request.DocumentSpeId,
            DriveId = request.DriveId,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            AnnotationCount = request.Annotations.Count,
            Preview = preview,
            CompletionState = completion,
        };
    }

    /// <inheritdoc />
    public async Task<ComposePushSavePreview> PreviewPushAnnotationsAsync(
        PreviewPushAnnotationsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));
        if (request.Annotations is null || request.Annotations.Count == 0)
            throw new ArgumentException("At least one annotation is required.", nameof(request));

        var composeOnlyCount = await ResolveComposeOnlyCountAsync(request.TenantId, request.SessionId, cancellationToken)
            .ConfigureAwait(false);
        return ComposePushSavePreviewCalculator.Compute(request.Annotations, composeOnlyCount);
    }

    // =========================================================================
    // FR-28 push/save pipeline — helpers (per-step job-aware projection + Redis persistence).
    // Mirrors the FR-05 create-on-save helpers above (CompletedSignal / ProjectCreateOnSaveState)
    // — same JobAwareCompletionStateProjector, a different consumer-declared step set.
    // =========================================================================

    /// <summary>
    /// FR-28: resolves the "stays in Compose only" count for the Tier-2c preview from the
    /// session's <c>DefinedTermsTracking</c> (FR-29) — the one Compose-domain collection with no
    /// Word-native OOXML representation (contrast anchored annotations, which map onto the
    /// <c>DocxAnnotation</c> push payload). Returns 0 (graceful degrade, not a failure) when no
    /// session id is supplied — callers that predate this optional field still get a valid
    /// preview, just with <c>ComposeOnlyCount</c> = 0.
    /// </summary>
    private async Task<int> ResolveComposeOnlyCountAsync(string tenantId, string? sessionId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return 0;
        }

        var state = await GetComposeAnnotationsAsync(tenantId, sessionId, ct).ConfigureAwait(false);
        return state.DefinedTermsTracking.Count;
    }

    /// <summary>
    /// FR-28: projects the push/save per-step signals through the shared
    /// <see cref="JobAwareCompletionStateProjector"/> and persists the result to Redis (ADR-009)
    /// via <see cref="ComposePushSaveStatusStore"/> — best-effort: a Redis outage is logged and
    /// swallowed here so it never masks or rolls back a document write/failure that has already
    /// happened on its own terms (see <see cref="ComposePushSaveStatusStore"/> remarks). The
    /// projected state is always returned/used for the in-flight HTTP response regardless of
    /// whether the Redis write succeeded.
    /// </summary>
    private async Task<JobAwareCompletionState> PersistPushSaveStatusAsync(
        string documentSpeId,
        StoredStepSignal pushSignal,
        StoredStepSignal saveSignal,
        StoredStepSignal versionSignal,
        DateTimeOffset observedAt,
        CancellationToken ct)
    {
        var job = new JobContract
        {
            JobType = ComposePushSaveJobType,
            SubjectId = documentSpeId,
            CorrelationId = documentSpeId,
            IdempotencyKey = $"compose-push-save-{documentSpeId}-{observedAt.Ticks}",
        };

        var completion = JobAwareCompletionStateProjector.Project(
            job,
            new List<StoredStepSignal> { pushSignal, saveSignal, versionSignal },
            observedAt);

        if (_pushSaveStatusStore is not null)
        {
            try
            {
                await _pushSaveStatusStore.SaveAsync(documentSpeId, completion, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose push-save: failed to persist cross-request completion state to Redis for driveItem={DocumentSpeId} — the in-flight response is unaffected.",
                    documentSpeId);
            }
        }

        return completion;
    }

    /// <summary>A stored terminal-failure signal for a step (single attempt, so never
    /// RetryPending — the push/save pipeline does not auto-retry within one HTTP call).</summary>
    private static StoredStepSignal FailedSignal(string stepName, string detail) => new()
    {
        StepName = stepName,
        StoredStatus = JobStatus.Failed,
        Started = true,
        Attempt = 1,
        MaxAttempts = 1,
        Detail = detail,
    };

    /// <summary>A stored non-terminal "never started" signal for a step that never ran because an
    /// earlier step in the SAME pipeline invocation failed first (pipeline-abort semantics — no
    /// partial write, no partial step).</summary>
    private static StoredStepSignal NotStartedSignal(string stepName) => new()
    {
        StepName = stepName,
        StoredStatus = null,
        Started = false,
    };

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
