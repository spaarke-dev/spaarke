using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
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
using Sprk.Bff.Api.Services.Compose.Operations;
using Sprk.Bff.Api.Services.Documents;
using Sprk.Bff.Api.Services.Jobs;
using Sprk.Bff.Api.Infrastructure.Authentication;

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
    // G1 (FR-01, task 020): the durable cross-session authored-vs-imported origin marker (owner-created
    // choice field; notes/g1-origin-field-asbuilt.md). Written ONLY at create-on-save
    // (PromoteIfEphemeralAsync) and read on Path A loads (LoadAsync) — see ComposeOrigin remarks for the
    // AS-BUILT integer values + BINDING null-handling contract.
    private const string ComposeOriginAttribute = "sprk_composeorigin";
    // G7 (FR-06, task 022): the client-minted transient dedup key (owner-created Single-line-text column +
    // single-column alt-key sprk_composetransientkey_uk; notes/g7-transient-key-schema.md). Stamped ONLY at
    // create-on-save (PromoteIfEphemeralAsync). Resolved via the alt-key in TryFindDocumentByTransientKeyAsync
    // BEFORE minting a transient SPE item, so repeated create-on-save calls with the same key replace one
    // record in place instead of minting duplicates (the 8-duplicate defect). Resolve by KEY, never by
    // content (I-7/NFR-02).
    private const string ComposeTransientKeyAttribute = "sprk_composetransientkey";
    // FR-C3 (email-communication-intelligence-r2, graduate-on-divergence): the SPE content identity
    // (quickXorHash, task 023 indexed column) + the self-referential canonical link. A create-on-save
    // stamps sprk_canonicalhash; on a byte-identical hit it also LINKS via sprk_canonicaldocument (this
    // editable copy is byte-identical NOW). The link is CLEARED the moment content diverges (first edit),
    // graduating the copy to its own canonical — see the create + idempotent branches of
    // PromoteIfEphemeralAsync. Distinct from sprk_parentdocument (attachment→parent-email).
    private const string CanonicalHashAttribute = "sprk_canonicalhash";
    private const string CanonicalDocumentAttribute = "sprk_canonicaldocument";

    // Task 041 B-MED-3 (option C): the sprk_document record-link lookup vocabulary (ADR-024 — the
    // SAME closed set AttachmentDocumentAssociationRung follows, type-agnostic by design). A
    // PDF-sourced create-on-save copies every non-empty lookup from the source PDF's record onto the
    // new Word document's record so the two file side-by-side under the same matter/project/….
    private static readonly string[] DocumentAssociationLookupAttributes =
    {
        "sprk_matter",
        "sprk_relatedmatter",
        "sprk_project",
        "sprk_relatedproject",
        "sprk_invoice",
        "sprk_workassignment",
    };

    // FR-05 create-on-save backbone — the consumer-declared ordered step set the
    // JobAwareCompletionStateProjector projects (container → record → profile-analysis → indexing).
    // These string keys are the Compose contract the future OutcomeCard renders; keep stable.
    internal const string StepContainer = "container";
    internal const string StepRecord = "record";

    /// <summary>
    /// FR-S09 item 7 (r8 task 016): the save landed but the <c>sprk_document</c> row's file metadata
    /// (<c>sprk_filesize</c> / <c>sprk_filepath</c>) could not be brought up to date with it.
    /// </summary>
    internal const string DocumentMetadataStaleCode = "document-metadata-stale";
    internal const string StepProfileAnalysis = "profile-analysis";
    internal const string StepIndexing = "indexing";

    private const string ComposeCreateOnSaveJobType = "compose-create-on-save";
    private const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly ISpeFileOperations _spe;
    private readonly ChatSessionManager _sessions;
    private readonly IGenericEntityService _dataverse;
    private readonly IPostUploadIndexingEnqueuer _indexing;
    private readonly ILogger<ComposeService> _logger;
    // FR-C3 (email-communication-intelligence-r2): SPE content-dedup detector for the create-on-save
    // graduate-on-divergence hook. Optional + defaults null so the single bare test constructor
    // (ComposeServiceCreateOnSaveTests) + any legacy construction keep compiling; DI resolves the real
    // scoped ContentDedupDetector in every non-test host. Null → the dedup hook is a guarded no-op
    // (behavior identical to pre-R2 create-on-save). Best-effort/non-fatal by construction (NFR-04).
    private readonly ContentDedupDetector? _dedupDetector;
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
    // affect the already-returned save. Optional + defaults null (same availability-gate rationale as
    // _cache below) so existing 6-arg test constructors keep compiling; the field is now the availability GATE
    // (null → nothing to dispatch), while the actual run resolves a FRESH facade from the detached scope.
    private readonly IDocumentProfileAi? _documentProfileAi;
    // compose-r2 FR-30 (#629): durable Record-scope memory-capture facade (ADR-013). Optional + defaults
    // null so existing test constructors keep compiling; DI resolves the real ComposeMemoryCapture in every
    // non-test host. Null → the STEP 5 capture below is a clean no-op (best-effort availability gate).
    private readonly IComposeMemoryCapture? _memoryCapture;

    /// <summary>Cluster 7 (task 070): the document-memory distillation policy, extracted.</summary>
    private readonly ComposeMemoryCapturer _memoryCapturer;

    /// <summary>Cluster 6 (task 070): the session-annotations contract, extracted.</summary>
    private readonly ComposeAnnotationStore _annotations;

    /// <summary>Cluster 5b (task 070): background profile dispatch + the step signals, extracted.</summary>
    private readonly ComposeProfileDispatcher _profileDispatcher;
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
    // FR-06 (task 032, the write-path cutover — supersedes the retired R3 paragraph-diff synthesizer, Path B /
    // ADR-049): the SINGLE unified byte-author. On a dirty save it applies the client's ordered, rebased
    // task-003 operation log (+ any (paraId,range)-anchored comments) surgically onto the resolved baseline as
    // native w:ins/w:del/w:comment — ID-anchored, ZERO write-path text-search (I-7), preserving every untouched
    // paragraph + all structure. REPLACES both ComposeParagraphRedlineSynthesizer AND DocxAnnotationWriter,
    // both now fully retired (task 036 removed the last text-search byte-author — the push-annotations surface).
    // It is the SOLE byte-author (I-5). Optional +
    // defaults to a fresh instance (stateless, thread-safe) so existing test constructors compile unchanged; DI
    // resolves the registered singleton (ComposeModule) in every host.
    private readonly ComposeShadowPatchEngine _patchEngine;
    // FR-01a (task 026, E1 born-in-editor): the from-scratch high-fidelity OOXML AUTHORING engine. On a
    // born-in-editor save (request.ContentModel present — an AI-drafted/blank/browse-local doc with no
    // retained original), it renders the .docx server-side (real styles + style-linked multi-level numbering
    // + tables + minted paraId) — the deterministic replacement for the removed client docx.js exporter.
    // Optional + defaults to a fresh instance (stateless, thread-safe) so existing test constructors compile
    // unchanged; DI resolves the registered singleton (ComposeModule) in every host.
    private readonly ComposeDocumentRenderer _documentRenderer;
    // FR-24 (task 050, import round-trip): the EXISTING read-direction OOXML annotation parser, REUSED
    // VERBATIM. On Load it recovers every native w:ins/w:del (any authorship) so the client can render them
    // as first-class tracked changes instead of the mammoth-flattened prose (design §7). Pure byte[]-in /
    // record-out — no Microsoft.Graph type (ADR-007), no AI-internal type (ADR-013); the SPE download stays
    // behind the SpeFileStore facade. Optional + defaults to a fresh instance (stateless, thread-safe) so
    // existing test constructors compile unchanged; DI resolves the registered singleton in every host.
    private readonly DocxAnnotationReader _annotationReader;
    // C2 fix (UAT 2026-07-20): stamps the client's minted paraIds physically onto the resolved baseline's
    // id-less paragraphs BEFORE the synthesizer resolves — completing the "apply physically" step
    // ParaIdPreParser mints but never wrote into the bytes. Pure/stateless; optional + defaults to a
    // fresh instance so existing test constructors compile unchanged.
    private readonly ComposeBaselineParaIdStamper _baselineParaIdStamper;
    // Phase-1 mammoth removal (design notes/design-server-side-docx-html-conversion.md): the single-walk
    // server-side DOCX→editor projection. On Load it emits paraId-tagged HTML AND the ordered paraId map
    // from ONE traversal (the client no longer runs mammoth or position-stamps ids) — eliminating the
    // two-engine drift that produced the recurring save-abort bug class. The map it returns is the load
    // path's single paraId authority (also feeds imported-revision/comment paraId resolution). Pure/
    // stateless; optional + defaults to a fresh instance so existing test constructors compile unchanged.
    private readonly ComposeDocxProjectionBuilder _projectionBuilder;
    // Task 032 (spaarkeai-compose-r6, FR-05): the 030 house-style chrome engine — merges the document's
    // BODY into a firm/matter .dotx template's chrome (styles/numbering/theme/headers/footers/sectPr from
    // the TEMPLATE). Consumed ONLY by ApplyTemplateAsync; pure OOXML byte[]-in/byte[]-out (ADR-007/013).
    // Optional + defaults to a fresh instance (stateless, thread-safe) so existing test constructors
    // compile unchanged; DI resolves the registered singleton in every host (ComposeModule.cs).
    private readonly ComposeTemplatePartMergeEngine _partMergeEngine;
    // Task 040 (spaarkeai-compose-r6, FR-06): the PublicContracts PDF-intake facade (ADR-013 — Compose
    // consumes the facade, never Services/Ai internals) + the pure DocumentLayout→canonical-model
    // projector. The facade is optional + defaults null (bare test ctor / compound-OFF hosts resolve the
    // NullComposePdfIntakeSource peer via DI); null here means a PDF load fails LOUDLY with a clear
    // "PDF intake unavailable" message — never a silent empty mount. The projector mirrors the
    // _patchEngine idiom (stateless singleton-shaped; fresh instance ≡ DI-registered one).
    private readonly Sprk.Bff.Api.Services.Ai.PublicContracts.IComposePdfIntakeSource? _pdfIntakeSource;
    private readonly ComposePdfModelProjector _pdfModelProjector;
    // FR-08 (task 050): the raw distributed cache handle for the save-path version stamp (Redis, ADR-009 —
    // NOT IMemoryCache). Optional + defaults null so existing test constructors keep compiling; DI resolves
    // the real IDistributedCache (AddStackExchangeRedisCache) in every non-test host. Null in a bare test
    // constructor is the availability gate for the Redis-backed save-path features below.
    private readonly IDistributedCache? _cache;
    // FR-08 (task 050): the KEEP-asset fuzzy re-anchor engine (bands + ambiguity guard + never-silently-drop),
    // REUSED verbatim as the stale-base / cross-Word-session fallback for the save-path operation log — see
    // ReanchorStaleSaveAsync. Constructed only when a cache is available (the _cache availability gate
    // above); null in a test host with no cache means staleness is asserted but never
    // re-anchored (the save proceeds on the resolved baseline unchanged, R1-equivalent behavior).
    private readonly AnnotationReanchorService? _reanchorService;

    public ComposeService(
        ISpeFileOperations spe,
        ChatSessionManager sessions,
        IGenericEntityService dataverse,
        IPostUploadIndexingEnqueuer indexing,
        ILogger<ComposeService> logger,
        IDistributedCache? cache = null,
        IDocumentProfileAi? documentProfileAi = null,
        IServiceScopeFactory? scopeFactory = null,
        IHostApplicationLifetime? appLifetime = null,
        IComposeMemoryCapture? memoryCapture = null,
        ParaIdPreParser? paraIdPreParser = null,
        ComposeShadowPatchEngine? patchEngine = null,
        ComposeDocumentRenderer? documentRenderer = null,
        DocxAnnotationReader? annotationReader = null,
        ComposeBaselineParaIdStamper? baselineParaIdStamper = null,
        ComposeDocxProjectionBuilder? projectionBuilder = null,
        ComposeTemplatePartMergeEngine? partMergeEngine = null,
        Sprk.Bff.Api.Services.Ai.PublicContracts.IComposePdfIntakeSource? pdfIntakeSource = null,
        ComposePdfModelProjector? pdfModelProjector = null,
        // FR-C3 (email-communication-intelligence-r2, merged from master 2026-08-07): content-dedup
        // graduate-on-divergence detector — union of both branches' trailing optional params.
        ContentDedupDetector? dedupDetector = null)
    {
        _spe = spe;
        _sessions = sessions;
        _dataverse = dataverse;
        _indexing = indexing;
        _logger = logger;
        _documentProfileAi = documentProfileAi;
        _scopeFactory = scopeFactory;
        _paraIdPreParser = paraIdPreParser ?? new ParaIdPreParser();
        _patchEngine = patchEngine ?? new ComposeShadowPatchEngine();
        _documentRenderer = documentRenderer ?? new ComposeDocumentRenderer();
        // FR-24 (task 050): reuse the existing reader verbatim — stateless singleton-shaped, so a fresh
        // instance is functionally identical to the DI-registered one (mirrors _paraIdPreParser above).
        _annotationReader = annotationReader ?? new DocxAnnotationReader();
        _baselineParaIdStamper = baselineParaIdStamper ?? new ComposeBaselineParaIdStamper();
        _projectionBuilder = projectionBuilder ?? new ComposeDocxProjectionBuilder();
        // Task 032 (FR-05): mirrors the _patchEngine idiom — stateless singleton-shaped, so a fresh
        // instance is functionally identical to the DI-registered one.
        _partMergeEngine = partMergeEngine ?? new ComposeTemplatePartMergeEngine();
        // Task 040 (FR-06): facade stays null in a bare test ctor (PDF loads then fail loudly);
        // the projector mirrors the stateless-singleton idiom above.
        _pdfIntakeSource = pdfIntakeSource;
        _pdfModelProjector = pdfModelProjector ?? new ComposePdfModelProjector();
        _appLifetime = appLifetime;
        _memoryCapture = memoryCapture;
        _memoryCapturer = new ComposeMemoryCapturer(memoryCapture, _sessions, _logger);
        _annotations = new ComposeAnnotationStore(_sessions, _logger);
        _profileDispatcher = new ComposeProfileDispatcher(_scopeFactory, _documentProfileAi, _appLifetime, _logger);
        // FR-C3 (email-communication-intelligence-r2): null in a bare test constructor (dedup hook = no-op),
        // the real scoped detector in every non-test host.
        _dedupDetector = dedupDetector;
        // FR-08 (task 050): ADR-009 Redis when present in every non-test host, null (no staleness
        // re-anchor) in a bare test constructor.
        _cache = cache;
        _reanchorService = cache is not null ? new AnnotationReanchorService(cache) : null;
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
    // FR-01 (task 010, spaarkeai-compose-fidelity-r4.5): reuses the SAME _projectionBuilder instance
    // LoadAsync builds the Load-path projection from — one builder, one shape, both doorways (F-2).
    public ComposeDocxProjection ProjectDocument(ReadOnlyMemory<byte> content, CancellationToken cancellationToken = default)
        => _projectionBuilder.Build(content, cancellationToken);

    /// <inheritdoc />
    // Task 012 (the client cutover): mint FIRST, then build BOTH projections from the SAME minted
    // bytes. The builder mints ids for id-less paragraphs from a cryptographic RNG per walk, so two
    // independent walks over unminted bytes would DISAGREE on those paragraphs' ids — the retained
    // model's block ids must match the editor's node ids or the client's imported-save merge mapper
    // (keyed by paraId) cannot pair them. MintAndPersist is the ingest-time stamp (fill-gaps-only,
    // idempotent, fail-open — NOT the save-path count-gate this project retires), the same pass
    // LoadAsync applies before its own projection build.
    public async Task<ComposeMountProjection> ProjectForMount(
        ReadOnlyMemory<byte> content,
        string? fileName = null,
        CancellationToken cancellationToken = default,
        string? sessionId = null)
    {
        // Task 050 (spaarkeai-compose-r7, FR-06 — PDF import parity, NFR-04 / ADR Tensions path A): give
        // the mount doors (Browse-project + Assistant-upload) the SAME PDF fork LoadAsync has (@502) so a
        // PDF opened via those doors becomes an editable Compose document, not a fail-closed read-only
        // mount. Detection is bytes-first (IsPdfSource: %PDF- magic OR .pdf extension — a mis-named PDF
        // still lands here), and on a PDF the source projects through the ONE ProjectPdfToDocxAsync intake
        // leg (Azure DI prebuilt-layout → ComposePdfModelProjector → SynthesizeDocument), after which the
        // synthesized .docx replaces `content` and the ENTIRE mint/HTML/canonical pipeline below runs
        // UNCHANGED — "PDF projects into the same model docx projects into" holds by construction.
        //
        // This is why ProjectForMount is now async — a documented, project-scoped ADR-007/ADR-013 contract
        // change: it was deliberately synchronous / no-I/O, and the DOCX path STAYS synchronous-fast (the
        // await below is reached ONLY on the PDF branch; a native .docx mount does zero added I/O and never
        // touches the intake source). Unavailability / parse failure throws a CLEAR ComposePdfIntakeException
        // (the endpoints map it to 503/422), never a silent empty mount over a non-empty PDF.
        IReadOnlyList<ComposeProjectionWarning>? pdfIntakeWarnings = null;
        string? sourceFormat = null;
        if (IsPdfSource(fileName, content.Span))
        {
            sourceFormat = "pdf";
            (content, pdfIntakeWarnings) = await ProjectPdfToDocxAsync(
                    content, fileName ?? "(compose-mount)", driveId: "(mount)", documentSpeId: "(mount)", cancellationToken)
                .ConfigureAwait(false);

            // FR-A08 (task 044): the same server-determined "this was a PDF" carry LoadAsync makes, for the
            // mount doors. There are no SOURCE drive-item coordinates here — an uploaded or browsed file has
            // no SPE item to re-open — so this marker serves the STAMP only; the FR-A09 derived-document
            // mapping needs a durable source pointer and is correctly skipped. Session-less callers (the
            // stateless Browse door) record nothing, which is the documented residual.
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                await SetPdfSourceMarkerAsync(sessionId!, driveId: string.Empty, speId: string.Empty, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        var stamp = _baselineParaIdStamper.MintAndPersist(content);
        var bytes = stamp.Mutated ? stamp.Bytes : content;

        var projection = _projectionBuilder.Build(bytes, cancellationToken);
        var canonical = _projectionBuilder.BuildContentModel(bytes, cancellationToken);

        var mountModel = canonical.Status == ComposeProjectionStatus.Failed ? null : canonical.Model;
        // Task 013 (012-review F7): flatten warnings ride to the client with the model.
        var contentModelWarnings = mountModel is not null && canonical.Warnings.Count > 0 ? canonical.Warnings : null;
        // Task 050 (FR-06): the PDF intake's counted degradations ride WITH the model warnings — intake
        // facts FIRST (source-level: fixed-layout reflow, page chrome, list/table approximation), mirroring
        // LoadAsync@563. Merged UNCONDITIONALLY so the intake facts reach the client even when the
        // synthesized-docx re-projection itself failed (mountModel null / op-log fallback).
        if (pdfIntakeWarnings is { Count: > 0 })
        {
            contentModelWarnings = contentModelWarnings is null
                ? pdfIntakeWarnings
                : pdfIntakeWarnings.Concat(contentModelWarnings).ToList();
        }
        return new ComposeMountProjection
        {
            Content = bytes,
            Minted = stamp.Mutated,
            Projection = projection,
            ContentModel = mountModel,
            ContentModelWarnings = contentModelWarnings,
            SourceFormat = sourceFormat,
        };
    }

    /// <inheritdoc />
    // Task 032 (spaarkeai-compose-r6, FR-05) — the apply-template orchestration: download the PERSISTED
    // bytes (mirror LoadAsync's fetch idiom), merge via the ONE 030 engine (never re-implemented),
    // persist via the existing ReplaceFileContentAsUserAsync idiom (new SPE version — the prior version
    // stays retrievable, FR-07 safety net), and re-project the persisted bytes (mirror the post-save
    // re-projection) so the client re-mounts on the response. No Graph type crosses this method
    // (ADR-007); no AI internals (ADR-013 — the resolved template bytes arrive from the endpoint's
    // PublicContracts facade call); no AI dispatch (ADR-039).
    public async Task<ApplyComposeTemplateResult> ApplyTemplateAsync(
        HttpContext httpContext,
        string driveId,
        string documentSpeId,
        byte[] resolvedTemplateBytes,
        string templateName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(driveId))
            throw new ArgumentException("DriveId is required for SPE drive-item access.", nameof(driveId));
        if (string.IsNullOrWhiteSpace(documentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(documentSpeId));
        ArgumentNullException.ThrowIfNull(resolvedTemplateBytes);
        if (resolvedTemplateBytes.Length == 0)
            throw new ArgumentException("Resolved template bytes must not be empty.", nameof(resolvedTemplateBytes));

        _logger.LogInformation(
            "Compose apply-template: drive={DriveId} driveItem={DocumentSpeId} template={TemplateName}",
            driveId, documentSpeId, templateName);

        // 1) Download the CURRENT persisted bytes (the merge applies to the SAVED document — the client
        //    guards apply on a non-dirty, non-transient mount). Mirrors LoadAsync's buffered fetch.
        var stream = await _spe.DownloadFileAsUserAsync(httpContext, driveId, documentSpeId, cancellationToken)
            .ConfigureAwait(false);
        if (stream is null)
        {
            throw new InvalidOperationException(
                $"SPE drive-item not found: drive={driveId} item={documentSpeId}");
        }

        byte[] currentBytes;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            currentBytes = buffer.ToArray();
        }

        // Step-9.5 A-MEDIUM-1 (task 041 review): apply-template merges the item's CURRENT bytes —
        // for a PDF item (a PDF-sourced Compose mount that has not saved yet) that would hand %PDF-
        // bytes to the OOXML part-merge and die deep in the stack as a generic 500. Refuse with the
        // typed 422 and the honest instruction instead (the client also disables the affordance while
        // sourceFormat === 'pdf').
        if (currentBytes.Length >= 5
            && currentBytes[0] == (byte)'%' && currentBytes[1] == (byte)'P' && currentBytes[2] == (byte)'D'
            && currentBytes[3] == (byte)'F' && currentBytes[4] == (byte)'-')
        {
            throw new ComposePdfIntakeException(
                "Apply template: this document is a PDF. Save it as a Word document first " +
                "(a PDF opened in Compose saves as a new Word document), then apply the template.",
                unavailable: false);
        }

        // 2) The ONE 030 part-merge: template chrome (styles/numbering/theme/headers/footers/sectPr)
        //    + document body. Degradations are collected loudly (template-merge-* codes), never silent.
        var mergeWarnings = new List<ComposeProjectionWarning>();
        var merged = _partMergeEngine.Merge(currentBytes, resolvedTemplateBytes, mergeWarnings);

        // 3) Ingest-stamp paraIds into the merged package (fill-gaps-only, idempotent, fail-open —
        //    the SAME MintAndPersist pass LoadAsync/ProjectForMount apply) so the re-projection below
        //    and the client's next Load agree on every paragraph id.
        var stamp = _baselineParaIdStamper.MintAndPersist(merged);
        var finalBytes = stamp.Mutated ? stamp.Bytes : merged;

        // 4) Persist as a NEW SPE version via the existing replace idiom (the prior version remains
        //    retrievable through SPE version history — FR-07 safety net).
        FileHandleDto? replaced;
        using (var replaceStream = new MemoryStream(finalBytes, writable: false))
        {
            replaced = await _spe.ReplaceFileContentAsUserAsync(
                    httpContext, driveId, documentSpeId, replaceStream, cancellationToken)
                .ConfigureAwait(false);
        }

        if (replaced is null || string.IsNullOrEmpty(replaced.Id))
        {
            throw new InvalidOperationException(
                $"SPE apply-template failed: drive-item not found or version not returned. drive={driveId} item={documentSpeId}");
        }

        // FR-08 alignment: stamp the just-written version as the next save's staleness assert-baseline
        // (best-effort — mirrors SaveAsync's post-write stamp; a Redis miss never fails the merge).
        await SetSaveVersionStampAsync(documentSpeId, replaced.ETag, DateTimeOffset.UtcNow, cancellationToken)
            .ConfigureAwait(false);

        // 5) Re-project the PERSISTED bytes into the canonical model (mirror the post-save
        //    re-projection) so the client can adopt/re-mount from the response. Best-effort: a failed
        //    projection returns null — the merge itself already succeeded and persisted.
        var savedProjection = _projectionBuilder.BuildContentModel(finalBytes, cancellationToken);
        var contentModel = savedProjection.Status == ComposeProjectionStatus.Failed ? null : savedProjection.Model;
        var contentModelWarnings = contentModel is not null && savedProjection.Warnings.Count > 0
            ? savedProjection.Warnings
            : null;

        _logger.LogInformation(
            "Compose apply-template: merged + persisted drive={DriveId} driveItem={DocumentSpeId} template={TemplateName} newVersion={VersionId} mergeWarnings={WarningCount}",
            driveId, documentSpeId, templateName, replaced.Id, mergeWarnings.Count);

        return new ApplyComposeTemplateResult
        {
            DocumentSpeId = documentSpeId,
            DriveId = replaced.DriveId ?? driveId,
            VersionId = replaced.Id,
            ETag = replaced.ETag,
            Size = replaced.Size,
            TemplateName = templateName,
            MergeWarnings = mergeWarnings.Count > 0 ? mergeWarnings : null,
            ContentModel = contentModel,
            ContentModelWarnings = contentModelWarnings,
        };
    }

    /// <inheritdoc />
    public Task<LoadComposeDocumentResult> LoadAsync(
        LoadComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
        => LoadAsync(request, httpContext, allowPdfRedirect: true, cancellationToken);

    /// <param name="allowPdfRedirect">FR-A09 (task 044): true on the caller-facing entry point, false on
    /// the ONE re-entry this method makes into itself when a PDF resolves to the Word document it already
    /// became. The re-entry targets a <c>.docx</c>, so <see cref="IsPdfSource"/> is false there and the
    /// redirect branch is unreachable by construction — this flag makes that non-recursion explicit rather
    /// than relying on the reader to derive it.</param>
    private async Task<LoadComposeDocumentResult> LoadAsync(
        LoadComposeDocumentRequest request,
        HttpContext httpContext,
        bool allowPdfRedirect,
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

        // Task 040 (spaarkeai-compose-r6, FR-06 — PDF intake): a PDF source projects through the SAME
        // canonical hub as docx, then IS a docx from here on. Detection is extension OR magic-bytes
        // (a mis-named file lands on the branch its BYTES belong to). The branch: PublicContracts
        // intake facade (Azure DI prebuilt-layout via the existing parse stack — ADR-013, no
        // Services/Ai fork) → ComposePdfModelProjector (DocumentLayout → ComposeContentModel, counted
        // pdf-intake-* degradations, never a construct hard-fail) → SynthesizeDocument (the ONE
        // renderer) → the synthesized .docx replaces `content`, and the ENTIRE existing pipeline below
        // (paraId mint, HTML projection, canonical re-projection, annotation read) runs UNCHANGED —
        // "PDF projects into the same model docx projects into" holds by construction. Unavailability
        // (compound gate OFF / parse failure / nothing projectable) throws a CLEAR message — never a
        // silent empty mount over a non-empty PDF (honest-lossiness principle).
        IReadOnlyList<ComposeProjectionWarning>? pdfIntakeWarnings = null;
        string? sourceFormat = null;
        if (IsPdfSource(metadata.Name, content.Span))
        {
            // FR-A09 (task 044): this PDF may ALREADY have become a Word document. A PDF-sourced first
            // save mints a NEW .docx item and the client re-targets onto it — but ONLY in that browser
            // session. A refresh destroys every client-held coordinate (retained bytes, re-targeted
            // documentRef, and the per-mount transientKey, which composeIdentity.ts deliberately never
            // persists), so the client re-mounts against the only durable pointer it has: this PDF. Re-
            // projecting it here would show the user the PDF again — their saved work invisible — and
            // their next save would mint a DUPLICATE document. So: resume on the document that exists.
            //
            // This is the load half of the FR-A09 pair. It is what makes the second save ordinary: the
            // resumed .docx has real version coordinates (unlike a .pdf item, whose version id is
            // deliberately suppressed below), so the save resolves a baseline and CLONES untouched blocks
            // exactly like any imported document, with no PDF-shaped special case anywhere in the save path.
            //
            // Best-effort in both directions — a cache miss, a Redis failure, or a derived document that
            // has since been deleted all fall through to the intake below (today's behavior), never a
            // failed Load.
            if (allowPdfRedirect)
            {
                var derived = await ResolvePdfDerivedDocumentAsync(
                        request.DriveId, request.DocumentSpeId, httpContext, cancellationToken)
                    .ConfigureAwait(false);

                if (derived is not null)
                {
                    _logger.LogInformation(
                        "Compose load: PDF drive={DriveId} item={DocumentSpeId} already became Word document " +
                        "drive={DerivedDriveId} item={DerivedSpeId} — resuming on it instead of re-projecting the PDF (FR-A09).",
                        request.DriveId, request.DocumentSpeId, derived.DriveId, derived.SpeId);

                    return await LoadAsync(
                            request with
                            {
                                DriveId = derived.DriveId,
                                DocumentSpeId = derived.SpeId,
                                // NOT `?? request.DocumentRecordId`. When the derived document has no known
                                // row (its promotion failed), falling back to the PDF's row would read the
                                // PDF record's sprk_composeorigin and attribute it to the .docx, and would
                                // re-trigger the profile against the wrong record. Null is the honest answer
                                // — the load degrades to Path B, where origin is null and the binding
                                // contract already treats that as Imported.
                                DocumentRecordId = derived.RecordId,
                            },
                            httpContext,
                            allowPdfRedirect: false,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            sourceFormat = "pdf";
            (content, pdfIntakeWarnings) = await ProjectPdfToDocxAsync(
                    content, metadata.Name ?? request.DocumentSpeId, request.DriveId, request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false);
        }

        // FR-01 (task 010, ingest — design §5, I-1/I-3): mint + PERSIST a w14:paraId into the retained
        // package's DOM for every editable paragraph that lacks one — durable across a load → save →
        // reload round-trip, not merely carried in the projection map below. Idempotent
        // (ComposeBaselineParaIdStamper.MintAndPersist): a document whose paragraphs already all carry
        // ids returns byte-identical (no-op). Running this BEFORE the projection build means the builder
        // — which reads but never writes existing ids — sees a body with no gaps, so its own ParaIdMap is
        // automatically consistent with what is now physically in `content`. That same mutated `content`
        // is what Load returns as Content and what the client echoes back as the same-session Save
        // fast-path baseline (ResolveSaveBaselineAsync), so the persisted ids survive that round-trip by
        // construction — same-session durable minting; a Word-regenerated cross-session id collision is
        // AnnotationReanchorService's fuzzy-fallback boundary, not this step's concern.
        var ingestParaIdStamp = _baselineParaIdStamper.MintAndPersist(content);
        if (ingestParaIdStamp.Mutated)
        {
            content = ingestParaIdStamp.Bytes;
            _logger.LogInformation(
                "Compose load: minted + persisted {MintedCount} w14:paraId(s) into the retained package for drive={DriveId} item={DocumentSpeId}.",
                ingestParaIdStamp.ParaIdMap.Count(e => e.IsMinted), request.DriveId, request.DocumentSpeId);
        }

        // Phase-1 mammoth removal (design notes/design-server-side-docx-html-conversion.md): the single-walk
        // projection builder produces BOTH the paraId-tagged editor HTML AND the ordered w14:paraId map from
        // ONE traversal — replacing the client-side mammoth convert + position-based paraId stamping (the
        // two-engine drift that caused the "matches no paragraph in the retained original" save failures).
        // The builder's map is the load path's single paraId authority; it is produced in
        // Descendants<Paragraph>() order (reader-aligned) so the imported-revision/comment ParagraphHint
        // resolution below keeps working. Fail-closed + best-effort: an unreadable source yields
        // Status=Failed/empty map (NEVER throws) and Load still returns the source bytes (§3.3). The builder
        // supersedes the ParaIdPreParser single-pass on the Load path (one paragraph-enumeration authority).
        var projection = _projectionBuilder.Build(content, cancellationToken);
        IReadOnlyList<ParaIdMapEntry> paraIdMap = projection.ParaIdMap;

        // Task 012 (the client cutover): the CANONICAL content model, built from the SAME minted bytes
        // the HTML projection above walks (ids already persisted by MintAndPersist — the two walks agree
        // by construction). The client retains it as the loaded model and re-posts it (merged with editor
        // state, every server-set field preserved) on an imported dirty save — the render-on-save (a1)
        // shape. Best-effort: a Failed canonical projection degrades to null (the client falls back to
        // the transitional op-log shape); never fails Load. Counts-only logging (privacy — no text).
        var canonicalProjection = _projectionBuilder.BuildContentModel(content, cancellationToken);
        var contentModel = canonicalProjection.Status == ComposeProjectionStatus.Failed
            ? null
            : canonicalProjection.Model;
        // Task 013 (012-review F7): surface the flatten warnings to the client (folded into the FIRST
        // model-path save's degradation banner - the save is when the loss materializes).
        var contentModelWarnings = contentModel is not null && canonicalProjection.Warnings.Count > 0
            ? canonicalProjection.Warnings
            : null;
        // Task 040 (FR-06): the PDF intake's counted degradations ride WITH the model warnings —
        // intake facts first (source-level: fixed-layout reflow, page chrome, list/table
        // approximation), then whatever the synthesized-docx re-projection added. Same client
        // surface as the docx flatten warnings (the 041 honest-lossiness banner reads these).
        // Step-9.5 LOW-7: merged UNCONDITIONALLY — even if the synthesized-docx re-projection
        // failed (contentModel null, op-log fallback), the intake facts must still reach the client.
        if (pdfIntakeWarnings is { Count: > 0 })
        {
            contentModelWarnings = contentModelWarnings is null
                ? pdfIntakeWarnings
                : pdfIntakeWarnings.Concat(contentModelWarnings).ToList();
        }
        if (canonicalProjection.Status == ComposeProjectionStatus.Failed)
        {
            _logger.LogWarning(
                "Compose load: canonical-model projection failed for drive={DriveId} item={DocumentSpeId} (code={Code}); imported saves will use the transitional op-log shape",
                request.DriveId, request.DocumentSpeId, canonicalProjection.Warnings.FirstOrDefault()?.Code);
        }
        else if (canonicalProjection.Warnings.Count > 0)
        {
            _logger.LogInformation(
                "Compose load: canonical-model projection partial for drive={DriveId} item={DocumentSpeId}; warnings={Warnings}",
                request.DriveId, request.DocumentSpeId,
                string.Join(",", canonicalProjection.Warnings.Select(w => $"{w.Code}:{w.Count}")));
        }
        if (projection.Status == ComposeProjectionStatus.Failed)
        {
            _logger.LogWarning(
                "Compose load: DOCX projection failed for drive={DriveId} item={DocumentSpeId} (code={Code}); client will fail closed (read-only / Open in Word)",
                request.DriveId, request.DocumentSpeId, projection.Warnings.FirstOrDefault()?.Code);
        }
        else if (projection.Warnings.Count > 0)
        {
            // Counts-only (privacy — no document content). Surfaces fidelity gaps to engineering (F-03).
            _logger.LogInformation(
                "Compose load: DOCX projection partial for drive={DriveId} item={DocumentSpeId}; warnings={Warnings}",
                request.DriveId, request.DocumentSpeId,
                string.Join(",", projection.Warnings.Select(w => $"{w.Code}:{w.Count}")));
        }

        // FR-24/FR-25 (task 050 + task 051, import round-trip): run the EXISTING DocxAnnotationReader ONCE
        // on the SAME load-time bytes, alongside the paraId pre-parse + the (client-side) mammoth convert —
        // which FLATTENS w:ins/w:del to prose AND drops comment anchors before the editor sees them
        // (docxBridge.ts). A single Read() call (NFR-08 — same single-pass rationale as the paraId pre-parse
        // above) projects BOTH RecoveredRevision (FR-24) and RecoveredComment (FR-25) onto the Load response,
        // each WITH the E2 w14:paraId of its containing paragraph (resolved from the paraIdMap above by the
        // reader's document-order ParagraphHint — both walk body.Descendants<Paragraph>() so the indices
        // align). Revisions render as first-class accept/reject-able insertion/deletion marks; comments group
        // by shared anchorText into FR-23 comment threads (design §7). REUSE ONLY — the reader is unmodified.
        // Best-effort + empty (NOT null): a malformed/unreadable source degrades to no imported
        // revisions/comments and NEVER fails Load, matching the paraId pre-parse contract above (the client
        // still edits the doc).
        IReadOnlyList<ImportedRevision> importedRevisions;
        IReadOnlyList<ImportedComment> importedComments;
        // UAT-12 (2026-08-18, honest/safe): track whether the annotation read actually FAILED (threw) vs
        // genuinely returned nothing. An empty result on the failure path must NOT be presented as a clean
        // document — the client surfaces an honest banner when this is true.
        bool annotationReadFailed = false;
        try
        {
            var recovered = _annotationReader.Read(content.ToArray());

            importedRevisions = recovered.Revisions.Count == 0
                ? Array.Empty<ImportedRevision>()
                : recovered.Revisions
                    .Select(r => new ImportedRevision(
                        Kind: r.Kind,
                        Id: r.Id,
                        Author: r.Author,
                        Date: r.Date,
                        Text: r.Text,
                        AnchorText: r.AnchorText,
                        ParagraphHint: r.ParagraphHint,
                        ParaId: ComposeReferenceMapping.ResolveParaIdForHint(paraIdMap, r.ParagraphHint)))
                    .ToList();

            importedComments = recovered.Comments.Count == 0
                ? Array.Empty<ImportedComment>()
                : recovered.Comments
                    .Select(c => new ImportedComment(
                        Id: c.Id,
                        Author: c.Author,
                        Date: c.Date,
                        CommentText: c.CommentText,
                        AnchorText: c.AnchorText,
                        ParagraphHint: c.ParagraphHint,
                        ParaId: ComposeReferenceMapping.ResolveParaIdForHint(paraIdMap, c.ParagraphHint)))
                    .ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: existing-annotation read failed for drive={DriveId} item={DocumentSpeId}; returning no imported revisions/comments",
                request.DriveId, request.DocumentSpeId);
            importedRevisions = Array.Empty<ImportedRevision>();
            importedComments = Array.Empty<ImportedComment>();
            annotationReadFailed = true; // UAT-12: signal the client so it never shows this as clean
        }

        // FR-06 (E1, task 027): capture the LOAD-TIME SPE version id so a later dirty save that no longer
        // holds the client bytes (e.g. after a page refresh) can re-fetch THIS baseline by versionId
        // (DownloadFileVersionAsUserAsync). Best-effort behind the SpeFileStore facade (ADR-007) — a null
        // (no version history / lookup unavailable) never fails Load; the client then relies on the
        // retained-bytes Content fast-path.
        string? versionId = null;
        // Step-9.5 MEDIUM-3 (task 040): a PDF-sourced load returns SYNTHESIZED docx Content — the
        // `.pdf` item's version id would be a booby trap (a post-refresh save re-fetching it as the
        // "retained docx baseline" would hand %PDF- bytes to the OOXML engine). Leave it null so the
        // save path uses the retained-bytes fast-path or the client's create-on-save routing (041);
        // ResolveSaveBaselineAsync additionally sniff-guards every resolved baseline (HIGH-2).
        if (sourceFormat is null)
        {
            try
            {
                versionId = await _spe.GetCurrentVersionIdAsUserAsync(httpContext, request.DriveId, request.DocumentSpeId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose load: current-version-id lookup failed for drive={DriveId} item={DocumentSpeId}; the save path will use the retained-bytes fast-path",
                    request.DriveId, request.DocumentSpeId);
            }
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
        // Issue #863 — resolved from the PRINCIPAL, never from the request. Compose Load is a
        // body-scoped session route (the session id arrives in the payload, not the URL), so
        // SessionOwnershipFilter does not cover it and the check lives here instead; it is
        // enumerated as such in SessionOwnershipGuardTests.BodyScopedSessionRoutes. Adding an
        // ownerOid FIELD to the request would recreate exactly the defect task 059 removed —
        // a caller naming its own identity.
        var callerOid = CallerResolution.ResolveObjectId(httpContext.User);
        if (string.IsNullOrEmpty(callerOid))
        {
            // Unreachable on a RequireAuthorization() route with an Entra principal. Thrown rather
            // than defaulted because both alternatives are worse: minting an unowned session makes
            // the document permanently unopenable for its own author, and resuming without an
            // identity is the gap itself.
            throw new UnauthorizedAccessException(
                "Compose load: the caller carries no Entra oid, so session ownership cannot be established.");
        }

        ChatSession? session = null;
        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            var candidate = await _sessions.GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
                .ConfigureAwait(false);

            // Issue #863 — a resume is an ACCESS to an existing session, so it takes the same
            // ownership test as any session-scoped route. Without this, supplying someone else's
            // SessionId in the Load body resumes their conversation, annotations, defined terms and
            // action history. Unowned (pre-#863) candidates fail closed and fall through to a fresh
            // session, which is the graceful outcome: the user gets a working document, not an error.
            if (candidate is not null
                && !string.Equals(candidate.OwnerOid, callerOid, StringComparison.Ordinal))
            {
                _logger.LogWarning(
                    "Compose load: session {SessionId} (tenant={TenantId}) is not owned by the caller — " +
                    "ignoring the supplied SessionId and minting a new session.",
                    request.SessionId, request.TenantId);
                candidate = null;
            }

            if (candidate is not null && ComposeReferenceMapping.IsSameCrossVersionBinding(candidate, bindingId, request.MatterId))
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
                ownerOid: callerOid,
                documentId: bindingId,
                playbookId: null,
                hostContext: BuildMatterHostContext(request.MatterId),
                ct: cancellationToken)
            .ConfigureAwait(false);

        // FR-17 (task 041, design.md §4 WS-4, owner clarification "WS-4 store" = BOTH stores):
        // persist the paraId -> legal-number map into the R4 session ledger (ADR-040) alongside
        // AnchoredAnnotations/DefinedTermsTracking/ActiveDocument — the SAME three-tier ChatSession
        // stack (Redis hot / Cosmos warm), no new store. The map already rides on the projection
        // payload returned below (ParaIdMap, task 040); this mirrors the SAME per-paragraph
        // reference fields onto the session so a reload — or a consumer that reads the session
        // directly without re-running the projection (e.g. task 042's citation resolver) —
        // resolves paraId -> number without a recompute divergence. Reassigned on EVERY load from
        // the freshest Build() output: unchanged paragraphs keep the SAME entry because R4's
        // AnnotationReanchorService/ComposeBaselineParaIdStamper keep a paragraph's physical
        // w14:paraId stable across edits; new/split paragraphs simply appear as new map entries
        // (R4 re-anchor — this task does not reconcile or diff the two snapshots).
        var referenceMap = ComposeReferenceMapping.BuildReferenceMap(paraIdMap);
        session = session with { ReferenceMap = referenceMap };
        await _sessions.UpdateSessionCacheAsync(session, cancellationToken).ConfigureAwait(false);

        // FR-A08/FR-A09 (task 044): carry the SERVER-DETERMINED "this was a PDF" fact forward on the
        // session, rather than re-deriving it at save time or trusting the client to say so. The
        // discriminant is `IsPdfSource` above — bytes-first, ours, decided here (project CLAUDE.md
        // invariant 7: deterministic information available at capture time MUST be carried, not
        // re-derived). The save reads it back by the session id IT minted, and uses it for exactly two
        // things: stamping the new record Authored (a PDF projection has no original .docx it could be a
        // lossy view of) and recording what this PDF became. Best-effort — a miss degrades to today's
        // behavior on both counts, never a failed Load.
        //
        // The else-branch matters more than the if. The marker's ONLY dangerous direction is a stale one
        // surviving onto a document that is genuinely imported — that would stamp a real .docx Authored
        // and put its later saves on the clean-apply branch, which is the SEV-1 shape (redlines dropped).
        // A resumed session that is now serving a NON-PDF document therefore clears it, so the marker
        // always describes what this session currently holds rather than what it once held. Missing the
        // marker costs a false warning; a stale one costs redlines, and those are not the same stakes.
        if (sourceFormat == "pdf")
        {
            await SetPdfSourceMarkerAsync(
                    session.SessionId, request.DriveId, request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await ClearPdfSourceMarkerAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
        }

        // FR-33 (task 062, design.md §8): restore prior decisions from the ledger alongside the
        // FR-29 annotations — task 061's read-only GetActionHistory query over the resumed
        // session's Outputs/ToolChains. No new stored structure (ADR-040); a freshly-minted
        // session naturally has an empty ledger.
        var actionHistory = GetActionHistory(session);

        // G1 (FR-01, task 020): read the persisted sprk_composeorigin marker for Path A loads (an
        // existing sprk_document record) so the client can route a reopened Authored doc onto the
        // clean payload instead of the op-log/tracked path (NFR-02 — never inferred from SPE-id or
        // content). Path B continuation (no DocumentRecordId — the doc is not yet promoted) has no
        // row to read; Origin stays null. Best-effort: a read failure OR a legacy row with no stored
        // value degrades to Origin=null — NEVER fails Load. The BINDING null-handling contract (see
        // ComposeOrigin remarks) is the CALLER's obligation: null MUST be treated as Imported, never
        // strict-equal to Authored.
        ComposeOrigin? origin = request.DocumentRecordId.HasValue
            ? await ReadPersistedOriginAsync(request.DocumentRecordId.Value, cancellationToken).ConfigureAwait(false)
            : null;

        // G10 (FR-09, task 040): reload/onload re-trigger of the Document Profile — storm-safe (fires only
        // when the doc changed since Compose last profiled it). Path A only (an existing sprk_document to
        // profile). Best-effort — never blocks or fails Load.
        if (request.DocumentRecordId is { } reloadRecordId && !string.IsNullOrWhiteSpace(metadata.ETag))
        {
            await MaybeRetriggerProfileOnLoadAsync(
                reloadRecordId, request.DocumentSpeId, metadata.ETag!, httpContext, cancellationToken)
                .ConfigureAwait(false);
        }

        return new LoadComposeDocumentResult
        {
            DocumentSpeId = request.DocumentSpeId,
            DriveId = request.DriveId,
            SessionId = session.SessionId,
            DocumentRecordId = request.DocumentRecordId,
            Content = content,
            ETag = metadata.ETag,
            VersionId = versionId,
            FileName = metadata.Name,
            Size = metadata.Size,
            AnchoredAnnotations = session.AnchoredAnnotations ?? Array.Empty<AnchoredAnnotation>(),
            DefinedTermsTracking = session.DefinedTermsTracking ?? Array.Empty<DefinedTerm>(),
            ActionHistory = actionHistory,
            ParaIdMap = paraIdMap,
            Projection = projection,
            ImportedRevisions = importedRevisions,
            ImportedComments = importedComments,
            AnnotationReadFailed = annotationReadFailed,
            ContentModel = contentModel,
            ContentModelWarnings = contentModelWarnings,
            Origin = origin,
            SourceFormat = sourceFormat,
        };
    }

    /// <summary>
    /// Task 040 (FR-06): PDF source detection — BYTES FIRST (Step-9.5 MEDIUM-5), extension as
    /// tiebreak, so a mis-named file lands on the branch its bytes belong to: a docx (PK-zip) named
    /// <c>.pdf</c> takes the native full-fidelity OOXML path (NOT the lossy reflow), and a PDF named
    /// <c>.docx</c> takes the intake path (it would otherwise fail-closed on the OOXML projection).
    /// Only when the bytes are neither signature does the extension decide.
    /// </summary>
    private static bool IsPdfSource(string? fileName, ReadOnlySpan<byte> content)
    {
        // %PDF- → PDF regardless of name.
        if (content.Length >= 5
            && content[0] == (byte)'%' && content[1] == (byte)'P' && content[2] == (byte)'D'
            && content[3] == (byte)'F' && content[4] == (byte)'-')
        {
            return true;
        }

        // PK\x03\x04 (OOXML zip container) → NOT a PDF regardless of name.
        if (content.Length >= 4
            && content[0] == 0x50 && content[1] == 0x4B && content[2] == 0x03 && content[3] == 0x04)
        {
            return false;
        }

        return fileName?.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase) == true;
    }

    /// <summary>
    /// Task 040 (FR-06): the PDF → canonical model → synthesized-docx intake leg. Throws
    /// <see cref="InvalidOperationException"/> with a clear, user-presentable reason on
    /// unavailability/failure — the Compose load endpoint surfaces it as a ProblemDetails failure,
    /// never a silent empty mount. Counts-only logging (privacy — no document text).
    /// </summary>
    private async Task<(ReadOnlyMemory<byte> DocxBytes, IReadOnlyList<ComposeProjectionWarning> IntakeWarnings)> ProjectPdfToDocxAsync(
        ReadOnlyMemory<byte> pdfBytes,
        string fileName,
        string driveId,
        string documentSpeId,
        CancellationToken cancellationToken)
    {
        if (_pdfIntakeSource is null)
        {
            // Step-9.5 HIGH-1: TYPED throw — the load endpoint maps Unavailable=true to a 503
            // ProblemDetails carrying this exact message (never the generic catch-all 500).
            throw new ComposePdfIntakeException(
                "PDF intake is unavailable: AI document parsing is disabled on this host " +
                "(Analysis:Enabled + DocumentIntelligence:Enabled required).",
                unavailable: true);
        }

        // Task 050 / FR-11 (spaarkeai-compose-r7): consume the CAUSE-DISCRIMINATED intake result (task 073's
        // ParseWithDiagnosticsAsync, now on the IComposePdfIntakeSource facade) so the user sees the SPECIFIC
        // reason — circuit-breaker-open / timeout / corrupt-file / disabled — instead of one collapsed
        // "corrupt or unavailable". This became a clean, downcast-free facade consumption (no ADR-013 breach)
        // once the facade's prior sole owner (spaarke-ai-architecture-redesign-r2) closed and R7 took ownership.
        var intake = await _pdfIntakeSource.ParseWithDiagnosticsAsync(pdfBytes.ToArray(), fileName, cancellationToken)
            .ConfigureAwait(false);
        if (!intake.Succeeded)
        {
            // 503 (retryable) for service-side / transient causes — circuit-open, timeout, unknown, and the
            // ADR-032 gate-off "disabled" (which rides Unknown); 422 (not retryable — the document itself is
            // the problem) ONLY for Corrupt. Mirrors the load endpoint's own 503-vs-422 split. The message is
            // the facade's cause-specific text (honest-lossiness: the real reason crosses the wire).
            var unavailable = intake.FailureCause != PdfIntakeFailureCause.Corrupt;
            throw new ComposePdfIntakeException(
                intake.FailureMessage
                    ?? $"PDF intake failed: the document layout could not be extracted from '{fileName}'.",
                unavailable);
        }
        var layout = intake.Layout!;

        var projection = _pdfModelProjector.Project(layout);
        if (projection.Status == ComposeProjectionStatus.Failed)
        {
            // The projector's only Failed outcome is "nothing projectable" — mounting an empty
            // editor over a non-empty PDF would be a silent lie (projection contract). 422 — the
            // document itself is the problem; retrying won't change the outcome.
            throw new ComposePdfIntakeException(
                $"PDF intake failed: no editable content could be projected from '{fileName}'.",
                unavailable: false);
        }

        // Render the model through the ONE renderer (render-on-save hub) — the synthesized docx is a
        // first-class imported carrier for everything downstream (paraIds minted by the renderer).
        var intakeWarnings = new List<ComposeProjectionWarning>(projection.Warnings);
        var docxBytes = _documentRenderer.SynthesizeDocument(projection.Model, author: "Spaarke Compose", intakeWarnings);

        _logger.LogInformation(
            "Compose load: PDF intake projected drive={DriveId} item={DocumentSpeId} into the canonical model " +
            "({Pages} source pages, {Blocks} blocks); degradations={Warnings}",
            driveId, documentSpeId, layout.PageCount, projection.Model.Blocks.Count,
            string.Join(",", intakeWarnings.Select(w => $"{w.Code}:{w.Count}")));

        return (docxBytes, intakeWarnings);
    }

    /// <summary>
    /// G1/G2 (FR-01/FR-02, tasks 020/021): reads the durable <c>sprk_composeorigin</c> marker for an
    /// existing <c>sprk_document</c>. Server-authoritative — the origin decision is the persisted marker,
    /// never inferred from SPE-id presence or document content (NFR-02 / I-7). Best-effort: a read failure
    /// OR a legacy row with no stored value returns <c>null</c> and NEVER throws — callers apply the BINDING
    /// null-handling contract (treat <c>null</c> as <see cref="ComposeOrigin.Imported"/>, never strict-equal
    /// to <see cref="ComposeOrigin.Authored"/>). Consumed by <see cref="LoadAsync"/> (returns it to the
    /// client) and <see cref="SaveAsync"/> (selects the engine's clean-vs-tracked apply mode).
    /// </summary>
    private async Task<ComposeOrigin?> ReadPersistedOriginAsync(Guid documentRecordId, CancellationToken cancellationToken)
    {
        try
        {
            var documentEntity = await _dataverse.RetrieveAsync(
                    DocumentLogicalName,
                    documentRecordId,
                    new[] { ComposeOriginAttribute },
                    cancellationToken)
                .ConfigureAwait(false);

            if (documentEntity is not null
                && documentEntity.Contains(ComposeOriginAttribute)
                && documentEntity[ComposeOriginAttribute] is OptionSetValue originOptionSet)
            {
                return (ComposeOrigin)originOptionSet.Value;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose: sprk_composeorigin lookup failed for record={DocumentRecordId} — origin treated as null (callers apply the BINDING null-handling contract: null = Imported).",
                documentRecordId);
        }

        return null;
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
        // absent (empty/whitespace). TenantId remains a hard precondition; the baseline must be
        // RESOLVABLE (see ResolveSaveBaselineAsync) — it need not arrive as Content bytes.
        if (string.IsNullOrWhiteSpace(request.TenantId))
            throw new ArgumentException("TenantId is required for ADR-015 Tier 3 isolation.", nameof(request));

        // ────────────────────────────────────────────────────────────────────────────
        // E1 KEYSTONE (FR-01/FR-06, task 022, Option C — design §4.2, §8): the persisted document is a
        // DELTA onto the retained LOAD-TIME ORIGINAL OOXML, never a TipTap reconstruction (docx.js is
        // dropped from the client export path). Three-part derivation, in order:
        //
        //   1. Resolve the BASELINE = the retained original bytes (FR-06):
        //        (a) request.Content — the same-session fast-path (the client still holds the pristine
        //            mount payload state.docxBytes; it is the ORIGINAL, not a reconstruction), else
        //        (b) re-fetch the load-time SPE version by request.BaselineVersionId (task 002 —
        //            DownloadFileVersionAsUserAsync, behind the SpeFileStore facade; ADR-007). A save
        //            after a page refresh (client bytes gone) still lands the delta on the correct version.
        //      A create-on-save (no DocumentSpeId) always supplies Content as the document bytes.
        //
        //   2. Apply the client's ordered, rebased task-003 OPERATION LOG (request.OperationLog) + any
        //      (paraId,range)-anchored comments (request.Comments) surgically ONTO the baseline via the SINGLE
        //      ComposeShadowPatchEngine (FR-06 cutover, task 032) — emitting native w:ins/w:del/w:comment,
        //      ID-anchored, ZERO write-path text-search (I-7), preserving every untouched paragraph + all
        //      structure by construction. This REPLACES both now-fully-retired writers
        //      (ComposeParagraphRedlineSynthesizer paragraph-diff + DocxAnnotationWriter, whose last use — the
        //      push-annotations surface — was retired by task 036). An empty log + no comments is a clean
        //      Save → the baseline stays byte-identical.
        //
        // The transform is pure/in-memory BEFORE any SPE write, so a refusal (unresolved paraId/anchor,
        // unsupported schema version, opaque-atom/structural refusal → ComposePatchException) throws before the
        // write — no partial or wrong SPE version can land. A no-op Save persists the baseline byte-identical
        // (FR-06a byte-identity preserved — see ComposeServiceUploadFidelityTests.cs).
        // ────────────────────────────────────────────────────────────────────────────
        var observedAt = DateTimeOffset.UtcNow;
        var isTransientCreate = string.IsNullOrWhiteSpace(request.DocumentSpeId);

        (byte[] contentToPersist, var renderDegradationWarnings) = await ResolveSaveBaselineAsync(request, httpContext, cancellationToken)
            .ConfigureAwait(false);

        // FR-A08 (task 044): remember WHICH warnings came from the render call. Everything appended below
        // this point is a save-outcome warning (op-log-ignored, comment anchoring, concurrency, stale
        // metadata) and must reach an authored document's author untouched; these are the fidelity family.
        // Captured by identity rather than by code so the distinction cannot drift as codes are added.
        var renderProvenanceWarnings = renderDegradationWarnings;

        // FR-06 (task 032, the write-path cutover): apply the client's ordered, rebased task-003 operation log
        // (+ any (paraId,range)-anchored comments) surgically onto the resolved baseline via the SINGLE
        // ComposeShadowPatchEngine. This REPLACES the retired ComposeParagraphRedlineSynthesizer (paragraph-diff)
        // AND DocxAnnotationWriter (fully retired by task 036 — its last use was the push-annotations surface). Skipped on
        // the born-in-editor path (ContentModel present → the renderer authored the whole doc, minting ids into
        // its own bytes; there is no baseline to patch and the client sends no op-log there).
        var hasOperations = request.OperationLog is { Operations.Count: > 0 };
        var hasComments = request.Comments is { Count: > 0 };

        // G1 (FR-01, task 020): the durable origin discriminator — mirrors the SAME baseline-source
        // signal ResolveSaveBaselineAsync's routing keys off. ContentModel present WITHOUT any baseline
        // source → born-in-editor render (a0, Authored); ContentModel WITH a baseline source → imported
        // render-on-save (a1 carrier render, Imported — task 010); ContentModel absent → the save carries
        // retained SPE bytes, whether a delta (op-log) or a byte-identical replace (Imported).
        // Resolved ONLY from this server-side discriminant — NEVER from SPE-id presence or a content/text
        // match (NFR-02, I-7). Computed on every save (not only create-on-save) so it can ride the returned
        // SaveComposeDocumentResult for immediate client/consumer use (e.g. task 021's clean-apply engine
        // mode selection) — but it is PERSISTED onto sprk_document ONLY at create-on-save (see the
        // PromoteComposeDocumentRequest.Origin wiring below); a replace-path save of an already-promoted
        // document never mutates the stored value.
        // UAT #1A hardening (task 050) + task 010 cutover: Authored requires a ContentModel AND no baseline
        // SOURCE at all — no retained original bytes AND no version-fetch coordinates. A save carrying a
        // baseline source is IMPORTED even when a ContentModel is present: post-cutover that combination IS
        // the designed imported render-on-save shape (model + carrier via RenderIntoCarrier), and pre-cutover
        // it was the erroneous-routing case UAT #1A already labeled Imported — either way the label is
        // Imported, so a routing slip can never durably mis-stamp an imported doc Authored (which would force
        // every later save onto the clean branch and silently drop redlines — the SEV-1 UAT regression).
        // ContentModel absent → Imported. Still resolved ONLY from server-side request shape — never from
        // SPE-id presence or a content/text match (NFR-02, I-7).
        var origin = request.ContentModel is not null
            && request.Content.IsEmpty
            && !HasBaselineVersionCoordinates(request)
            ? ComposeOrigin.Authored
            : ComposeOrigin.Imported;

        // Task 010: on the render-on-save path an op-log cannot apply (the model IS the document state);
        // a client that sends both is on a mixed contract — the ops are ignored LOUDLY, never
        // half-applied: logged server-side AND surfaced on the wire as an `op-log-ignored` degradation
        // warning (Step-9.5 F1 — observable to clients/tests, not just operators).
        if (request.ContentModel is not null && hasOperations)
        {
            _logger.LogWarning(
                "Compose save: request carries BOTH a ContentModel and an operation log ({OpCount} op(s)) — the render-on-save path ignores the op-log (the model is the authoritative document state). session={SessionId}",
                request.OperationLog!.Operations.Count, request.SessionId);
            var combinedWarnings = new List<ComposeProjectionWarning>(
                renderDegradationWarnings ?? (IReadOnlyList<ComposeProjectionWarning>)Array.Empty<ComposeProjectionWarning>())
            {
                new("op-log-ignored", request.OperationLog!.Operations.Count),
            };
            renderDegradationWarnings = combinedWarnings;
        }

        // G2 (FR-02, task 021 / R5-D2 Candidate A): the CLEAN-APPLY decision for the op-log/engine path.
        // A reopened AUTHORED doc sends an op-log (ContentModel null) — the ContentModel discriminant above
        // would mislabel it Imported, so the DURABLE sprk_composeorigin marker is authoritative here (read
        // server-side, never inferred from SPE-id/content — NFR-02/I-7). When the marker says Authored, the
        // engine applies edits CLEAN (plain runs, physical deletes, no w:pPrChange) so an authored doc's own
        // cross-session edits carry NO redlines (REQ-1); Imported/legacy-null stay on the tracked default
        // (REQ-2 not regressed). Best-effort: a marker read failure degrades to tracked (safe — worst case an
        // authored doc shows redlines, never data loss). ContentModel-present saves take the renderer path
        // (no engine Apply) and are already clean by construction.
        // FR-A08 (task 044) — THE STAMPING HALF. The routing `origin` above is deliberately
        // Imported-biased: it sees the synthesized carrier bytes a PDF-sourced save carries and calls the
        // save Imported, which is CORRECT for routing (it can never mis-stamp a genuinely imported doc
        // Authored and force it onto the clean-apply branch — the SEV-1 UAT regression). But it is the
        // WRONG thing to write down. What gets PERSISTED is what the document IS, and a PDF projection is
        // our file: the content model IS the document, not a lossy view of some prior .docx. Stamped
        // Imported, such a row makes the FR-A08 warning suppression unreachable for the very class the
        // requirement names first — and warns the user about losing formatting relative to an original
        // that never existed.
        //
        // The two values are therefore separated: `origin` continues to drive routing + the returned
        // result untouched, and `originToPersist` is what the promotion writes. The discriminant is the
        // SERVER's own bytes-first PDF detection at load, carried on the session (never a client claim,
        // never a content match — NFR-02/I-7), so it cannot fire for a .docx load and the SEV-1 vector
        // stays closed. Its downstream effect on a later save is also correct: reading Authored back puts
        // a PDF-sourced document's own edits on the clean-apply branch, which is right — there are no
        // redlines to drop, because there was never an original to redline against.
        var pdfSource = await GetPdfSourceMarkerAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
        var originToPersist = pdfSource is not null ? ComposeOrigin.Authored : origin;

        var cleanApply = false;
        if (request.ContentModel is null && request.DocumentRecordId is { } originRecordId)
        {
            var persistedOrigin = await ReadPersistedOriginAsync(originRecordId, cancellationToken).ConfigureAwait(false);
            if (persistedOrigin == ComposeOrigin.Authored)
            {
                origin = ComposeOrigin.Authored;
                cleanApply = true;
            }
        }

        // ────────────────────────────────────────────────────────────────────────────
        // FR-08 (task 050, design §5 "Save + concurrency"): version-stamp + assert-before-apply +
        // re-anchor-on-stale. Only meaningful for an EXISTING item (a transient create has no prior base
        // that could have moved). Every save of an existing item fetches the LIVE SPE eTag (never a
        // client-supplied precondition — the client cannot assert its own currency), and asserts it
        // against the version stamp THIS SAME save path persisted after its own last write (ADR-009
        // IDistributedCache, never IMemoryCache). No stamp yet (first Compose save of a pre-existing item)
        // means nothing to assert against — proceeds unstaled, R1-equivalent. A genuine mismatch means the
        // base moved under the client since it loaded (another writer landed a new version): re-anchor the
        // operation log via AnnotationReanchorService (the KEEP asset, reused verbatim — never reimplemented)
        // INSTEAD of blindly overwriting or throwing an eTag 500. AUTO (exact paraId match) re-anchors apply;
        // REVIEW/ORPHAN surface for the user — never silently applied, never silently dropped.
        // ────────────────────────────────────────────────────────────────────────────
        string? preWriteETag = null;
        ReanchorSummary? reanchorSummary = null;
        PartialApplySummary? partialApplySummary = null; // prong 1 (task 055) — set iff best-effort recovery ran

        if (!isTransientCreate && !string.IsNullOrWhiteSpace(request.DriveId))
        {
            var currentMetadata = await _spe.GetFileMetadataAsUserAsync(
                    httpContext, request.DriveId!, request.DocumentSpeId!, cancellationToken)
                .ConfigureAwait(false);
            preWriteETag = currentMetadata?.ETag;

            // Step-9.5 A-HIGH-1 (task 041 review): the replace-path save must NEVER write onto a
            // `.pdf` drive item. GuardBaselineIsNotPdf covers the BASELINE bytes, but under version
            // skew (new BFF + a Compose client predating 041's create-on-save routing) the client
            // sends a perfectly valid SYNTHESIZED-docx baseline while the replace TARGET is still the
            // PDF — the write would corrupt the item for every non-Compose consumer (preview,
            // download, Word). The metadata is already in hand at this choke point (zero extra Graph
            // calls); refuse with the typed 422 the endpoints map honestly. Extension comparison via
            // Path.GetExtension + string.Equals — a METADATA check, phrased to keep the I-7 write-path
            // text-search audit's lexical ban list satisfied (no string-search API in this slice).
            if (string.Equals(Path.GetExtension(currentMetadata?.Name), ".pdf", StringComparison.OrdinalIgnoreCase))
            {
                throw new ComposePdfIntakeException(
                    "Compose save: the target document is a PDF. A document opened from a PDF saves " +
                    "as a NEW Word document (create-on-save) — it cannot be replaced in place. " +
                    "Reload the document in Compose and save again.",
                    unavailable: false);
            }

            // UAT-25/26 (2026-08-18, honest/safe concurrency): compute the stale-base signal ONCE for both
            // save paths. The effective baseline is the Compose save-version STAMP if this session already
            // saved (Compose's own last write), else the client's LOAD-TIME ETag (request.BaselineETag) — the
            // latter closes the first-Compose-save no-stamp gap (UAT-26). A mismatch vs the live SPE ETag means
            // an EXTERNAL writer (Word / another tab) landed a new version since the client loaded.
            var saveStamp = await GetSaveVersionStampAsync(request.DocumentSpeId!, cancellationToken)
                .ConfigureAwait(false);
            var effectiveBaselineETag = saveStamp?.ETag ?? request.BaselineETag;
            var baseMoved = preWriteETag is not null
                && !string.IsNullOrEmpty(effectiveBaselineETag)
                && !string.Equals(effectiveBaselineETag, preWriteETag, StringComparison.Ordinal);

            // FR-S02 (r8 task 011) — CONCURRENCY IS LAST-WRITER-WINS WITH A WARNING. Owner decision
            // 2026-08-19, superseding the 412 refusal that shipped 2026-08-18.
            //
            // The whole-body ContentModel re-author cannot re-anchor another writer's changes (unlike the
            // op-log path below), so a moved base here DOES mean this save supersedes theirs. That is
            // acceptable and it is not data loss: Compose versions every save, so the other writer's
            // content is the PREVIOUS version, recoverable from version history. The refusal it replaces
            // was worse in exactly the way that matters — it left the user with unsaved work in a browser
            // tab and no way forward, and its client-side recovery handler was dead code the day it
            // shipped (task 010).
            //
            // So: proceed, and TELL THE USER. The warning rides the existing degradation-warning channel
            // (no new wire field, no new client surface) and names version history as the recovery. The
            // write below additionally carries `If-Match: preWriteETag`, so "last writer wins" is enforced
            // at the storage boundary rather than merely hoped for — a writer landing between our read and
            // our PUT is rejected by Graph and retried against the fresh version, never silently lost.
            if (request.ContentModel is not null && baseMoved)
            {
                _logger.LogWarning(
                    "Compose save: base moved on the ContentModel (whole-body) path for driveItem={DocumentSpeId} " +
                    "(baseline eTag={BaselineETag} [{BaselineSource}], live eTag={CurrentETag}) — proceeding " +
                    "last-writer-wins and warning the user; the superseded content remains in version history.",
                    request.DocumentSpeId, effectiveBaselineETag, saveStamp is not null ? "stamp" : "load-time", preWriteETag);

                renderDegradationWarnings = new List<ComposeProjectionWarning>(
                    renderDegradationWarnings ?? (IReadOnlyList<ComposeProjectionWarning>)Array.Empty<ComposeProjectionWarning>())
                {
                    new(ConcurrentExternalChangeCode, 1),
                };
            }

            if (request.ContentModel is null && (hasOperations || hasComments) && baseMoved)
            {
                var storedStamp = saveStamp;
                {
                    var (patchedBytes, summary) = await ReanchorStaleSaveAsync(
                            request, contentToPersist, httpContext, observedAt, trackChanges: !cleanApply, cancellationToken)
                        .ConfigureAwait(false);

                    contentToPersist = patchedBytes;
                    reanchorSummary = summary;

                    _logger.LogWarning(
                        "Compose save: stale base for driveItem={DocumentSpeId} (stamped eTag={StoredETag}, live eTag={CurrentETag}) — re-anchored via AnnotationReanchorService: auto={Auto} review={Review} orphan={Orphan} of {Total} op(s)/comment(s). AUTO re-anchors applied; REVIEW/ORPHAN surfaced (never silently applied, never silently dropped).",
                        request.DocumentSpeId, storedStamp?.ETag, preWriteETag,
                        summary.AutoCount, summary.ReviewCount, summary.OrphanCount, summary.Total);

                    // ReanchorStaleSaveAsync already applied the AUTO-band ops/comments through the Patch
                    // Engine onto the freshly-fetched current bytes — skip the normal apply block below
                    // (it would otherwise re-apply the FULL unfiltered log onto a now-stale baseline).
                    hasOperations = false;
                    hasComments = false;
                }
            }
        }

        if (request.ContentModel is null && (hasOperations || hasComments))
        {
            // C2 fix (UAT 2026-07-20): stamp the client's minted paraIds physically onto the baseline's id-less
            // paragraphs BEFORE the engine resolves each op's (paraId, runIndex, run-local-offset) anchor against
            // it. Without it, an edit on an originally-id-less paragraph (or any paragraph of an uploaded doc,
            // whose ids are all client-minted) would refuse with ParagraphNotFound. Text-verified + fill-gaps-only
            // + count-gated + fail-open (see ComposeBaselineParaIdStamper) — a no-op when the map is absent
            // (older client) or nothing qualifies, so a doc whose paragraphs already carry ids is unchanged.
            if (request.ParaIdMap is { Count: > 0 })
            {
                contentToPersist = _baselineParaIdStamper.Stamp(contentToPersist, request.ParaIdMap);
            }

            // Task 012: this op-log/engine path is the TRANSITIONAL save shape — the ADR-049 R6
            // amendment's sole permitted ComposeShadowPatchEngine caller (reopened-authored clean-apply
            // + pre-cutover clients / legacy in-flight sessions). Post-cutover clients send a
            // ContentModel + baseline source for every imported dirty save and never reach this block.
            // Logged at Warning for retirement telemetry (tasks 013/090 own the eventual removal).
            _logger.LogWarning(
                "Compose save: TRANSITIONAL op-log save shape — applying {OpCount} op(s) + {CommentCount} comment(s) via the Patch Engine onto the retained baseline (cleanApply={CleanApply}, session={SessionId}).",
                request.OperationLog?.Operations.Count ?? 0, request.Comments?.Count ?? 0, cleanApply, request.SessionId);

            // Pure/in-memory BEFORE any SPE write: a refusal (unresolved paraId/anchor, unsupported schema
            // version, opaque-atom/structural refusal) throws ComposePatchException here — mapped to a
            // ProblemDetails by the endpoint — so no partial or wrong SPE version can land.
            var revisionAuthor = ResolveRevisionAuthor(httpContext);
            var editLog = request.OperationLog ?? new ComposeOperationLog();
            try
            {
                contentToPersist = _patchEngine.Apply(
                    contentToPersist,
                    editLog,
                    request.Comments,
                    revisionAuthor,
                    observedAt,
                    trackChanges: !cleanApply);
            }
            catch (ComposePatchException ex) when (!IsBatchLevelPatchRefusal(ex.Kind))
            {
                // Prong 1 (task 055 — keep-edits graceful degradation). An OP-LEVEL anchoring refusal (a
                // single op whose paraId/anchor can't resolve) no longer loses the WHOLE editing session:
                // best-effort apply the resolvable paragraph-units and SURFACE the unresolvable ops so the
                // client prompts the user to redo just those edits. Never silently applies a wrong edit (the
                // paragraph is the atomic unit under the engine's intra-paragraph sequential rebasing) and
                // never silently drops one. Prong 2's paraOffset anchor already fixed the common root cause;
                // this is the residual safety net. BATCH-level refusals (malformed docx / schema skew) are
                // filtered OUT by the `when` guard and still throw hard → the endpoint's ProblemDetails.
                _logger.LogWarning(ex,
                    "Compose save: batch patch refusal ({Kind}) on the loaded-doc path — entering best-effort per-paragraph recovery (session={SessionId}).",
                    ex.Kind, request.SessionId);

                var bestEffortBytes = ApplyBestEffortByParagraph(
                    contentToPersist, editLog, request.Comments, revisionAuthor, observedAt,
                    trackChanges: !cleanApply, out var partial);

                if (partial.AppliedCount == 0)
                {
                    // Nothing resolved — no op applied AND no advisory comment baked (agreements-r1 UAT #4:
                    // AppliedCount now folds in baked comments, so a comments-only review save where at least
                    // one note anchored preserves that partial success instead of re-throwing). There is no
                    // partial success to preserve here. Fail HARD exactly as before prong 1 (a typed
                    // ProblemDetails via the endpoint), rather than persist a no-op version + a partial-apply
                    // banner. A wholly-unanchorable batch stays a hard refusal (the op-log + notes survive
                    // client-side for a retry). Re-throws the ORIGINAL op-level refusal.
                    _logger.LogWarning(
                        "Compose save: best-effort recovery resolved ZERO of {Total} op(s) — re-throwing the original refusal ({Kind}) as a hard failure (session={SessionId}).",
                        partial.Total, ex.Kind, request.SessionId);
                    throw;
                }

                contentToPersist = bestEffortBytes;
                partialApplySummary = partial;

                _logger.LogWarning(
                    "Compose save: best-effort recovery applied {Applied}/{Total} op(s); {Unresolved} surfaced as unresolvable (session={SessionId}).",
                    partial.AppliedCount, partial.Total, partial.UnresolvedCount, request.SessionId);
            }
        }
        // Task 012 (the cutover — retires the UAT round-3 comment BAKE, the LAST ComposeShadowPatchEngine
        // caller reachable with a ContentModel; 010 adr-check residual): on the render path comments ride
        // the MODEL itself (ComposeContentModel.Comments + Start/End anchor marker runs, folded in by the
        // client mapper per the task-024 shapes) — the renderer authors them into the blank package or
        // APPENDS the new ones to the carrier's comments part. The server never anchors a (paraId,
        // run-range) comment against rendered bytes again (that was anchor reconciliation — the retired
        // bug class). A request that still carries the SEPARATE comments field alongside a ContentModel is
        // a pre-cutover client shape: the comments are ignored LOUDLY (server log + wire-visible
        // degradation warning), never half-anchored and never silently dropped.
        else if (request.ContentModel is not null && hasComments)
        {
            _logger.LogWarning(
                "Compose save: request carries BOTH a ContentModel and {CommentCount} separate anchored comment(s) — the render-on-save path ignores the separate comments field (comments ride the model itself; pre-cutover client shape). session={SessionId}",
                request.Comments!.Count, request.SessionId);
            var combinedCommentWarnings = new List<ComposeProjectionWarning>(
                renderDegradationWarnings ?? (IReadOnlyList<ComposeProjectionWarning>)Array.Empty<ComposeProjectionWarning>())
            {
                new("comments-ignored", request.Comments!.Count),
            };
            renderDegradationWarnings = combinedCommentWarnings;
        }

        // Task 041 (Phase 4, NDA-REVIEW Summary Page): when the caller supplies the ledgered NDA-REVIEW
        // result, append the Summary Page (TL;DR + flagged-section overview + recommendations) as a
        // page-broken, non-tracked section at the END of the document — AFTER any edit-log/comment
        // application above, so the summary always lands as the true tail of what gets persisted. Pure,
        // deterministic, no second LLM call (ComposeSummaryPageGenerator); no new package (ADR-049 — reuses
        // ComposeDocumentRenderer, never the retired DocxAnnotationWriter).
        if (request.SummaryPage is not null)
        {
            var summaryBlocks = ComposeSummaryPageGenerator.Build(request.SummaryPage);
            contentToPersist = _documentRenderer.AppendSection(contentToPersist, summaryBlocks);

            _logger.LogInformation(
                "Compose save: appended NDA-REVIEW Summary Page ({FindingCount} flagged section(s), overallRisk={OverallRisk}) to the document (session={SessionId}).",
                request.SummaryPage.FlaggedSections.Count, request.SummaryPage.OverallRisk, request.SessionId);
        }

        // Task 012 (the client cutover): on a render-path save, project the FINAL persisted bytes back
        // into the canonical model and return it — the client adopts it as its new retained loaded model
        // and re-baselines its edit snapshot, so the NEXT dirty save merges against the just-persisted
        // state (without this, the next save would re-diff against the stale load-time baseline and
        // re-emit the same revisions). Includes any Summary Page appended above. Best-effort: a failed
        // projection returns null and the client keeps the model it posted as its merge base.
        ComposeContentModel? savedContentModel = null;
        if (request.ContentModel is not null)
        {
            var savedProjection = _projectionBuilder.BuildContentModel(contentToPersist, cancellationToken);
            if (savedProjection.Status != ComposeProjectionStatus.Failed)
            {
                savedContentModel = savedProjection.Model;
            }
        }

        _logger.LogInformation(
            "Compose save: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} container={ContainerId} transientCreate={IsTransientCreate} contentModel={HasContentModel} comments={CommentCount} session={SessionId} record={DocumentRecordId} size={SizeBytes}",
            request.TenantId, request.DriveId, request.DocumentSpeId, request.ContainerId,
            isTransientCreate, request.ContentModel is not null, request.Comments?.Count ?? 0,
            request.SessionId, request.DocumentRecordId, request.Content.Length);

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
            // ─── G7 (FR-06, task 022): transient-key dedup — the 8-duplicate fix ───
            // A transient draft has no SPE id until its first save mints one; this branch minted a NEW SPE
            // item on EVERY create-on-save call, so a lost/raced round-trip (concurrent saves, a re-created
            // mount, a new tab) produced another item → another sprk_document row. The client mints a stable
            // transient key once at mount and sends it on every create-on-save; BEFORE minting, resolve it
            // against the durable sprk_composetransientkey_uk alt-key. A hit REUSES the existing record's SPE
            // item (replace in place, no new mint, no new row). Save-New (ForkNew) deliberately SKIPS the
            // dedup to fork a fresh record. Resolves by KEY, never by content (I-7/NFR-02).
            TransientKeyMatch? dedupMatch = null;
            if (!request.ForkNew && !string.IsNullOrWhiteSpace(request.TransientKey))
            {
                dedupMatch = await TryFindDocumentByTransientKeyAsync(request.TransientKey!, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (dedupMatch is { } match
                && !string.IsNullOrWhiteSpace(match.SpeId)
                && !string.IsNullOrWhiteSpace(match.DriveId))
            {
                // Dedup hit: the transient key already resolved to a record with a live SPE item — replace
                // that item's content in place. No new mint, no new row (the promote step below finds the
                // existing record by sprk_graphitemid → idempotent no-op).
                using var replaceStream = new MemoryStream(contentToPersist, writable: false);
                var replaced = await _spe.ReplaceFileContentAsUserAsync(
                        httpContext, match.DriveId!, match.SpeId!, replaceStream, cancellationToken)
                    .ConfigureAwait(false);

                if (replaced is null || string.IsNullOrEmpty(replaced.Id))
                {
                    throw new InvalidOperationException(
                        $"Compose transient-key dedup: SPE replace failed for existing item drive={match.DriveId} item={match.SpeId} (transientKey matched record {match.RecordId}).");
                }

                saved = replaced;
                effectiveSpeId = match.SpeId!;
                effectiveDriveId = match.DriveId;
                fileName = replaced.Name ?? fileName;

                _logger.LogInformation(
                    "Compose create-on-save: transientKey matched existing sprk_document {DocumentRecordId} (driveItem={DocumentSpeId}) — replaced in place, no duplicate mint (session={SessionId}).",
                    match.RecordId, match.SpeId, request.SessionId);
            }
            else
            {
                if (string.IsNullOrWhiteSpace(request.ContainerId))
                {
                    _logger.LogWarning(
                        "Compose create-on-save: transient draft with no client-supplied ContainerId — failing the '{Step}' step honestly (session={SessionId}). No server-side BU→container resolver (multi-container INV-7).",
                        StepContainer, request.SessionId);
                    return BuildContainerFailedResult(request, observedAt);
                }

                // Fork B: mint the SPE drive-item in the supplied container under the user's OBO identity
                // (the Compose user holds the file ACL; MI does not — same constraint that deferred profile).
                // First save of this transient key (or a deliberate Save-New fork): once created, the record
                // is stamped with the transient key (promote step below) so the NEXT create-on-save with the
                // same key takes the dedup replace path above — never a double mint.
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
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.DriveId))
                throw new ArgumentException("DriveId is required for SPE drive-item access when DocumentSpeId is supplied.", nameof(request));

            // FR-S02 (r8 task 011): the write now carries `If-Match` — the "further-hardening candidate"
            // task 050 deferred. Without it, last-writer-wins is a hope: between reading `preWriteETag`
            // above and this PUT there is a check-then-act window in which another writer can land a
            // version that this blind write would erase with no version of theirs ever recorded.
            //
            // The precondition value is `preWriteETag` — the LIVE version this save's baseline was
            // resolved against, which is exactly what the POML requires and is correct on both paths:
            // the non-stale path merged against it because it equals the load-time baseline, and the
            // stale path merged against it because ReanchorStaleSaveAsync re-downloaded those very bytes.
            // Deliberately NOT the client's load-time ETag — sending that would re-create the refusal
            // this task removed, since a concurrent writer would fail the precondition every time.
            //
            // Null preWriteETag (no metadata read — a drive-less or transient path) degrades to the R1
            // blind PUT, unchanged. `ReplaceFileContentAsUserAsync` already accepts the value and maps a
            // Graph 412 to a typed EtagPreconditionFailedException (ADR-007: the Graph type never
            // crosses the facade); the ETag itself crosses as a plain string.
            var replaced = await ReplaceWithPreconditionAsync(
                    httpContext, request.DriveId, request.DocumentSpeId!, contentToPersist, preWriteETag, cancellationToken)
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

        // FR-08 (task 050/051): version-stamp the save that just landed — the POST-WRITE eTag returned by
        // THIS write (create or replace, whichever branch ran) becomes the assert-baseline for the NEXT
        // save of this same item (never a pre-write/pre-create precondition). Best-effort: a Redis miss
        // here never fails an already-successful save; it only means the next save's staleness assert
        // degrades to "no stamp = not stale" (R1-equivalent), never a false negative that blocks a save.
        await SetSaveVersionStampAsync(effectiveSpeId, saved.ETag, observedAt, cancellationToken).ConfigureAwait(false);

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
            // G1 (FR-01, task 020): persisted onto sprk_composeorigin ONLY when this call actually
            // creates the row (PromoteIfEphemeralAsync's idempotent existing-row branch ignores it).
            // FR-A08 (task 044): the PERSISTED value, which is what the document IS — not the
            // Imported-biased routing value. See the originToPersist derivation above.
            Origin = originToPersist,
            // G7 (FR-06, task 022): stamped onto sprk_composetransientkey ONLY when this call creates the
            // row, so the next create-on-save with the same key dedups via the alt-key (see the transient
            // branch above). Null on the replace path / older clients — no dedup identity, unchanged behavior.
            TransientKey = request.TransientKey,
            // Task 041 B-MED-3 (option C): the source PDF's record — the new row inherits its record links.
            SourceDocumentRecordId = request.SourceDocumentRecordId,
        };

        // FR-S09 item 5 (r8 task 016): the SPE write ABOVE has already landed and is durable. If the
        // record step now throws, "not saved" is a lie — the bytes are in storage, only the identity row
        // is missing — and it is the lie that costs the most, because the user retypes work that exists.
        //
        // Two exception classes, deliberately handled differently:
        //   • The Dataverse identity-key faults (inactive alternate key / duplicate rows) are RETHROWN.
        //     The endpoint already maps those to an honest 409/503 with administrator-actionable copy and
        //     `partially-recorded` telemetry. Swallowing them here would dead-code that handler — and
        //     dead handlers are this project's entire subject.
        //   • Everything else (Dataverse unavailable, timeout, transient auth) becomes a RETURNED
        //     terminal result carrying `partially-recorded`, the same shape the container-failure path
        //     uses. The save is over; a retry completes the promotion idempotently.
        PromoteComposeDocumentResult promotion;
        try
        {
            promotion = await PromoteIfEphemeralAsync(promoteRequest, httpContext, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOperationException ex) when (IsDataverseIdentityKeyFault(ex))
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Compose save: the SPE write succeeded but the sprk_document record step FAILED for " +
                "driveItem={DocumentSpeId} (session={SessionId}). The document bytes are durable; the " +
                "identity record is not. Reporting partially-recorded.",
                effectiveSpeId, request.SessionId);

            return BuildRecordFailedResult(
                request, effectiveSpeId, effectiveDriveId, saved, origin, observedAt,
                detail: $"record step failed: {ex.GetType().Name}: {ex.Message}");
        }

        // FR-A09 (task 044) — THE SAVE HALF. Record what this PDF became, so the next open of the PDF
        // resumes on the Word document instead of projecting the PDF a second time (the load half is in
        // LoadAsync's IsPdfSource branch). Written after the promotion so the record id is known, and on
        // EVERY PDF-sourced save rather than only the mint, so a re-save that dedups onto an existing
        // record refreshes a mapping that may have expired.
        //
        // The mapping stores the POINTER — drive + item + record — and deliberately NOT a version id.
        // The requirement says "track the version coordinates", and this is how they get tracked: the
        // resumed load re-reads the CURRENT version through the ordinary path, which is strictly better
        // than replaying one captured at creation. A stored version id would be read-never and stale the
        // moment anyone edits the document in Word — pointing the recovery path at a version that is no
        // longer the document. Storing what we would not read is how stale state becomes a bug.
        if (pdfSource is not null
            && !string.IsNullOrWhiteSpace(pdfSource.DriveId)
            && !string.IsNullOrWhiteSpace(pdfSource.SpeId)
            && !string.IsNullOrWhiteSpace(effectiveDriveId))
        {
            await SetPdfDerivedDocumentAsync(
                    pdfSource, effectiveDriveId!, effectiveSpeId, promotion.DocumentRecordId, observedAt, cancellationToken)
                .ConfigureAwait(false);
        }

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
            ? _profileDispatcher.Dispatch(promotion.DocumentRecordId.Value, httpContext)
            : ComposeProfileDispatcher.ProfileNotAttempted("no sprk_document record id resolved — profile not attempted");

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
            await _memoryCapturer.CaptureDocumentMemoryAsync(
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
            // FR-S09 item 5(a) (r8 task 016): derived, not asserted. This was a hardcoded
            // CompletedSignal — the record step reported success even when promotion resolved no record
            // id at all, which is the same class of claim-without-evidence as a 200 that means nothing
            // was written. The very next statement already branches on `DocumentRecordId.HasValue` for
            // the profile step, so the two lines used to contradict each other three lines apart.
            recordSignal: promotion.DocumentRecordId.HasValue
                ? CompletedSignal(StepRecord)
                : RecordNotResolvedSignal(),
            profileSignal: profileSignal,
            indexingSignal: ComposeProfileDispatcher.Indexing(indexingResult),
            observedAt: observedAt);

        // FR-S06 (task 013): the ONE success-path outcome decision. Ordered most-severe first so a save
        // that both partially applied AND warned reports the more consequential state — `partially-recorded`
        // means the user has work to redo, which must not be masked by the softer warning member.
        // FR-S09 item 5(b) (r8 task 016): the record step is part of the decision. Before this, the
        // outcome read the partial-apply summary and the warning list and NOTHING else — so a save whose
        // completion aggregate was Failed (no sprk_document row) still reported `persisted`, which is
        // "indistinguishable from full success" exactly as FR-S09 describes. The projection was computed
        // three lines above and then ignored.
        var recordResolved = promotion.DocumentRecordId.HasValue;

        // FR-S09 item 7 (r8 task 016): a failed metadata refresh is a warning, not a failure — the
        // document is saved and complete; only the columns describing it are stale.
        if (promotion.MetadataRefreshFailed)
        {
            renderDegradationWarnings = new List<ComposeProjectionWarning>(
                renderDegradationWarnings ?? (IReadOnlyList<ComposeProjectionWarning>)Array.Empty<ComposeProjectionWarning>())
            {
                new(DocumentMetadataStaleCode, 1),
            };
        }

        // ════════════════════════════════════════════════════════════════════════════════════════
        // FR-A08 (task 044) — an AUTHORED document has NO original to lose against.
        //
        // Born-in-editor, AI-drafted and PDF-sourced documents are OUR file: the content model IS the
        // document, not a lossy view of some prior .docx. "Some formatting was simplified when saving" on
        // one of those describes no loss, because there is nothing it could be a loss RELATIVE TO — and a
        // warning that cannot be acted on is the noise that made R7's honest-signal layer feel like a
        // nuisance rather than information.
        //
        // Applied HERE, not at the origin decision, because the document record is only resolved by the
        // promotion step above — and the routing `origin` deliberately labels ANY save carrying a carrier
        // Imported (it must never mis-stamp an imported doc Authored and force it onto the clean-apply
        // branch, the SEV-1 UAT regression). The durable `sprk_composeorigin` marker is what the document
        // actually IS; it decides the WARNING only, never `cleanApply`.
        //
        // Suppression is scoped by PROVENANCE, not by a code list: only the warnings the render call
        // produced are dropped. A code list would need maintaining as codes are added, and either
        // direction of omission is a defect — a missed fidelity code shows a false warning, a missed
        // outcome code SILENCES a real one.
        var warningRecordId = promotion.DocumentRecordId ?? request.DocumentRecordId;
        if (renderProvenanceWarnings is { Count: > 0 }
            && renderDegradationWarnings is { Count: > 0 }
            && warningRecordId is { } authoredCheckId
            && await ReadPersistedOriginAsync(authoredCheckId, cancellationToken).ConfigureAwait(false)
                == ComposeOrigin.Authored)
        {
            // Set difference by REFERENCE, not by value: two warnings can legitimately carry the same code
            // and count (one from the render call, one appended after it), and only the render one is a
            // fidelity warning. `Except` is used rather than a membership test because the I-7 source audit
            // bans that membership call anywhere in this slice — a blunt token scan over the source text
            // (comments included, which is how the first draft of THIS comment tripped it) guarding "no
            // text-search in the write path". The rule is right even where this particular use was not what
            // it was aimed at, so the code moves rather than the guard.
            var kept = renderDegradationWarnings
                .Except(renderProvenanceWarnings, (IEqualityComparer<ComposeProjectionWarning>)ReferenceEqualityComparer.Instance)
                .ToList();

            _logger.LogDebug(
                "Compose save: suppressed {Dropped} render-path degradation warning(s) for an AUTHORED " +
                "document (no original to lose against; FR-A08); {Kept} save-outcome warning(s) retained. " +
                "routingOrigin={Origin} session={SessionId}",
                renderDegradationWarnings.Count - kept.Count, kept.Count, origin, request.SessionId);

            renderDegradationWarnings = kept.Count > 0 ? kept : null;
        }

        var outcome =
            !recordResolved ? ComposeSaveOutcome.PartiallyRecorded
            : partialApplySummary is { UnresolvedCount: > 0 } ? ComposeSaveOutcome.PartiallyRecorded
            : (renderDegradationWarnings is { Count: > 0 } || reanchorSummary is not null) ? ComposeSaveOutcome.PersistedWithWarnings
            : ComposeSaveOutcome.Persisted;

        return new SaveComposeDocumentResult
        {
            Outcome = outcome,
            DocumentSpeId = effectiveSpeId,
            DriveId = effectiveDriveId,
            SessionId = promotion.SessionId,
            DocumentRecordId = promotion.DocumentRecordId,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            WasPromotedThisSave = promotion.WasCreated,
            CompletionState = completion,
            ReanchorSummary = reanchorSummary,
            PartialApply = partialApplySummary,
            Origin = origin,
            // Task 026 (FR-04): success-with-warnings — render-side degradations surfaced, never a 422.
            DegradationWarnings = renderDegradationWarnings,
            // Task 012: the post-save canonical model (render-path saves only) — the client's new merge base.
            ContentModel = savedContentModel,
        };
    }

    /// <summary>
    /// E1 baseline resolution (FR-06, task 022, Option C — design §4.3): returns the retained LOAD-TIME
    /// ORIGINAL bytes the save delta applies onto. Resolution order:
    /// <list type="number">
    /// <item><b>Same-session fast-path</b> — <see cref="SaveComposeDocumentRequest.Content"/> when present:
    /// the client still holds the pristine mount payload (<c>state.docxBytes</c>, the ORIGINAL — never a
    /// reconstruction). Also the create-on-save document bytes.</item>
    /// <item><b>FR-06 primary</b> — re-fetch the load-time SPE version by
    /// <see cref="SaveComposeDocumentRequest.BaselineVersionId"/> via
    /// <c>ISpeFileOperations.DownloadFileVersionAsUserAsync</c> (task 002; behind the <c>SpeFileStore</c>
    /// facade — ADR-007). Covers a save after the client lost its in-memory bytes (page refresh); the
    /// load-time version stays addressable even after later dirty saves advance the CURRENT version.</item>
    /// </list>
    /// A dirty save NEVER falls back to a client reconstruction (FR-01) — an unresolvable baseline is a
    /// clear error, not a lossy rebuild.
    /// <para>
    /// <b>Tier-3 Redis fallback (design §4.3, deferred — §6.5 Path-A scoping)</b>: the size-capped Redis
    /// cache of the load-time original is an OPTIMIZATION to avoid the SPE re-fetch, not a correctness
    /// requirement — the <see cref="SaveComposeDocumentRequest.BaselineVersionId"/> fetch already
    /// discharges FR-06 baseline retrieval. Populating it requires a Load-path write (out of task-022's
    /// file scope; Load is task 010/024). Deferred to keep this cutover to the SaveAsync inversion; the
    /// fast-path + versionId cover every real save case.
    /// </para>
    /// </summary>
    private async Task<(byte[] Bytes, IReadOnlyList<ComposeProjectionWarning>? RenderDegradations)> ResolveSaveBaselineAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (request.ContentModel is not null)
        {
            // Task 026 (FR-04): the render degradation sink — dropped anchors / format-change records /
            // hrefs surface as SUCCESS-WITH-WARNINGS, never a 422 and never silent.
            var renderDegradations = new List<ComposeProjectionWarning>();
            var revisionAuthor = ResolveRevisionAuthor(httpContext);

            // (a1) IMPORTED RENDER-ON-SAVE (task 010 — the cutover; spec FR-01/FR-02, ADR-049 Path-B
            //      amendment): a ContentModel WITH a resolvable retained baseline renders INTO that
            //      carrier (RenderIntoCarrier) — the model (projected at load through the 020-026
            //      canonical hub, edited in TipTap, re-posted with every server-set fact preserved) is
            //      the authoring source, and the carrier contributes the parts the thin model cannot
            //      carry (styles / numbering / headers / footers / theme / comments part). NO surgical
            //      byte-patch and NO count-gate on this path — the anchor-reconciliation 422 class
            //      (the NDA) is unreachable by construction. Hard-tier constructs accept-flattened at
            //      projection (026) render as degraded prose; the prior version stays retrievable via
            //      SPE version history (FR-07 safety net). Two boundary notes: (1) a born-in-editor doc
            //      must keep OMITTING baselineVersionId on its re-saves (its retained versionId is the
            //      drive-ITEM id, not a real SPE version — echoing it here would 404 the fetch; the
            //      client's bornInEditor branch sends contentModel only); (2) the FR-08 stale-base
            //      re-anchor deliberately does not run on this path — the model is full document state,
            //      so a concurrent out-of-band writer resolves last-writer-wins with version history as
            //      the net (design-accepted; the eTag stamp still updates post-save for the next op-log
            //      save's assert).
            if (!request.Content.IsEmpty)
            {
                var carrierContent = request.Content.ToArray();
                GuardBaselineIsNotPdf(carrierContent);
                var carrierRendered = _documentRenderer.RenderIntoCarrier(
                    carrierContent, request.ContentModel, revisionAuthor, renderDegradations);
                return (carrierRendered, renderDegradations.Count > 0 ? renderDegradations : null);
            }

            if (HasBaselineVersionCoordinates(request))
            {
                var carrierBytes = await FetchBaselineVersionBytesAsync(request, httpContext, cancellationToken)
                    .ConfigureAwait(false);
                GuardBaselineIsNotPdf(carrierBytes);
                var carrierRendered = _documentRenderer.RenderIntoCarrier(
                    carrierBytes, request.ContentModel, revisionAuthor, renderDegradations);
                return (carrierRendered, renderDegradations.Count > 0 ? renderDegradations : null);
            }

            // (a0) BORN-IN-EDITOR (FR-01a, task 026): no retained original at all (AI-drafted / blank) —
            //      the model is the WHOLE document; render the high-fidelity .docx from a blank package
            //      (real styles + style-linked multi-level numbering + native tables + minted
            //      w14:paraId). Deterministic authoring, NOT an AI dispatch (ADR-039 — design §11).
            var synthesized = _documentRenderer.SynthesizeDocument(
                request.ContentModel, revisionAuthor, renderDegradations);
            return (synthesized, renderDegradations.Count > 0 ? renderDegradations : null);
        }

        // (a) Same-session fast-path: the client still holds the retained ORIGINAL bytes.
        if (!request.Content.IsEmpty)
        {
            var retained = request.Content.ToArray();
            GuardBaselineIsNotPdf(retained);
            return (retained, null);
        }

        // (b) FR-06 primary: re-fetch the LOAD-TIME SPE version by versionId (task 002), behind the
        //     SpeFileStore facade (ADR-007 — no Microsoft.Graph type crosses into Services/Compose).
        if (HasBaselineVersionCoordinates(request))
        {
            var baseline = await FetchBaselineVersionBytesAsync(request, httpContext, cancellationToken)
                .ConfigureAwait(false);
            GuardBaselineIsNotPdf(baseline);
            return (baseline, null);
        }

        // No baseline resolvable. A dirty save NEVER falls back to a client reconstruction (FR-01).
        throw new ArgumentException(
            "Compose save: no baseline could be resolved — supply the retained original bytes (Content) for " +
            "a same-session save, or a BaselineVersionId (+ DriveId + DocumentSpeId) to re-fetch the " +
            "load-time version (FR-06). A docx.js reconstruction is not a valid baseline (FR-01).",
            nameof(request));
    }

    /// <summary>
    /// Task 040 Step-9.5 fix (HIGH-2): every resolved SAVE BASELINE must be an OOXML package, never a
    /// PDF. Before 040, a PDF could not reach a save (Load fail-closed on the OOXML projection); now a
    /// PDF load succeeds with SYNTHESIZED docx Content and a rogue/stale caller could hand the engine
    /// %PDF- bytes (a re-fetched PDF-item version, or the raw PDF echoed as "retained bytes") — which
    /// would either throw deep inside the OOXML stack as a generic 500, or worse, write docx bytes
    /// over the .pdf item. Sniff once here (the single choke point every baseline passes through) and
    /// refuse LOUDLY with the honest instruction (the 041 client saves PDFs via create-on-save; the
    /// endpoint maps this to 422).
    /// </summary>
    private static void GuardBaselineIsNotPdf(ReadOnlySpan<byte> baseline)
    {
        if (baseline.Length >= 5
            && baseline[0] == (byte)'%' && baseline[1] == (byte)'P' && baseline[2] == (byte)'D'
            && baseline[3] == (byte)'F' && baseline[4] == (byte)'-')
        {
            throw new ComposePdfIntakeException(
                "Compose save: the save baseline resolved to PDF bytes. A document opened from a PDF " +
                "saves as a NEW Word document (create-on-save) — it cannot replace the PDF in place. " +
                "Re-open the document and save again.",
                unavailable: false);
        }
    }

    /// <summary>
    /// FR-S02 (r8 task 011): replace the drive-item's content under an `If-Match` precondition, retrying
    /// ONCE against the freshly-read version if a writer landed inside the check-then-act window.
    /// </summary>
    /// <remarks>
    /// The retry is the deliberate resolution of the POML's step-5 question ("retry once, or report
    /// storage-failed?"). Retrying is correct here because the precondition failure carries no information
    /// the user could act on — it means only that our read was microseconds stale, and the save's own
    /// semantics are already last-writer-wins, so re-issuing against the fresh version produces exactly the
    /// outcome the user asked for. Retrying UNBOUNDED would be wrong (a hot document could spin), and
    /// failing immediately would resurrect the dead-end this task exists to remove — so: exactly one retry,
    /// then an honest typed failure the endpoint maps to a defined outcome.
    ///
    /// The second attempt re-reads metadata rather than reusing the failed ETag: reusing it would fail
    /// identically, and the point of the retry is to rebase onto whatever landed.
    /// </remarks>
    private async Task<FileHandleDto?> ReplaceWithPreconditionAsync(
        HttpContext httpContext,
        string driveId,
        string itemId,
        byte[] content,
        string? ifMatch,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(ifMatch))
        {
            // No resolved version to assert against (no metadata read happened — a drive-less or
            // transient path). Nothing to precondition on, so this stays the unchanged R1 blind PUT via
            // the etag-less overload rather than passing an explicit null through the If-Match one.
            using var blindStream = new MemoryStream(content, writable: false);
            return await _spe.ReplaceFileContentAsUserAsync(httpContext, driveId, itemId, blindStream, cancellationToken)
                .ConfigureAwait(false);
        }

        try
        {
            using var stream = new MemoryStream(content, writable: false);
            return await _spe.ReplaceFileContentAsUserAsync(httpContext, driveId, itemId, stream, ifMatch, cancellationToken)
                .ConfigureAwait(false);
        }
        // Only reachable with a non-empty `ifMatch` — the guard above returns for the blind-PUT case, so
        // this catch cannot fire on a request that never carried a precondition.
        catch (EtagPreconditionFailedException)
        {
            var fresh = await _spe.GetFileMetadataAsUserAsync(httpContext, driveId, itemId, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "Compose save: If-Match precondition failed for driveItem={DocumentSpeId} (sent eTag={SentETag}, " +
                "live eTag={FreshETag}) — a writer landed inside the read-to-write window. Retrying ONCE against " +
                "the fresh version (last-writer-wins).",
                itemId, ifMatch, fresh?.ETag);

            using var retryStream = new MemoryStream(content, writable: false);
            return await _spe.ReplaceFileContentAsUserAsync(
                    httpContext, driveId, itemId, retryStream, fresh?.ETag, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// FR-S02 (r8 task 011): the degradation-warning code carried when a save superseded a version another
    /// writer landed while the document was open. Concurrency is last-writer-wins with a warning; this code
    /// IS the warning, and the client renders it naming version history as the recovery path.
    /// Mirrored client-side by <c>CONCURRENT_EXTERNAL_CHANGE_CODE</c> in <c>ComposeBannerStack.tsx</c>.
    /// </summary>
    internal const string ConcurrentExternalChangeCode = "concurrent-external-change";

    /// <summary>Whether the request carries the full coordinate set for an FR-06 load-time-version
    /// re-fetch (versionId + driveId + speId).</summary>
    private static bool HasBaselineVersionCoordinates(SaveComposeDocumentRequest request) =>
        !string.IsNullOrWhiteSpace(request.BaselineVersionId)
        && !string.IsNullOrWhiteSpace(request.DriveId)
        && !string.IsNullOrWhiteSpace(request.DocumentSpeId);

    /// <summary>FR-06: downloads the load-time SPE version's exact bytes (the retained baseline / render
    /// carrier) behind the SpeFileStore facade. Throws when the version is gone — a dirty save never
    /// falls back to a reconstruction (FR-01).</summary>
    private async Task<byte[]> FetchBaselineVersionBytesAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var stream = await _spe.DownloadFileVersionAsUserAsync(
                httpContext, request.DriveId!, request.DocumentSpeId!, request.BaselineVersionId!, cancellationToken)
            .ConfigureAwait(false);

        if (stream is not null)
        {
            await using (stream.ConfigureAwait(false))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }
        }

        throw new InvalidOperationException(
            $"Compose save: the load-time baseline version was not found (drive={request.DriveId} " +
            $"item={request.DocumentSpeId} version={request.BaselineVersionId}). A dirty save must apply " +
            "onto the load-time original — it will not fall back to a reconstruction (FR-01/FR-06).");
    }

    // =========================================================================
    // FR-08 (task 050) — save-path version stamp + stale-base re-anchor (design §5 "Save + concurrency",
    // NFR-08). The stamp (SPE eTag + operation-schema version) is persisted via IDistributedCache (ADR-009)
    // after every save of an existing item and asserted against the LIVE eTag at the top of the NEXT save.
    // A mismatch re-anchors the operation log via AnnotationReanchorService — REUSED verbatim, never
    // reimplemented (CLAUDE.md §11 / task constraint).
    // =========================================================================

    private const string SaveVersionStampKeyPrefix = "sdap:compose:save-stamp:";
    private static readonly JsonSerializerOptions SaveStampJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>The save-path version stamp persisted per <c>documentSpeId</c> (ADR-009 Redis) — the SPE
    /// eTag + operation-schema version this service last wrote, asserted against the live eTag at the top
    /// of the next save of the same item.</summary>
    private sealed record ComposeSaveVersionStamp(
        [property: JsonPropertyName("eTag")] string ETag,
        [property: JsonPropertyName("schemaVersion")] string SchemaVersion,
        [property: JsonPropertyName("savedAtUtc")] DateTimeOffset SavedAtUtc);

    /// <summary>Reads the persisted version stamp for <paramref name="documentSpeId"/> (null when absent, no
    /// cache configured, or a Redis read fails — all three degrade to "not stale", never a false-positive
    /// re-anchor and never a blocked save).</summary>
    private async Task<ComposeSaveVersionStamp?> GetSaveVersionStampAsync(string documentSpeId, CancellationToken ct)
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            var json = await _cache.GetStringAsync(SaveVersionStampKeyPrefix + documentSpeId, ct).ConfigureAwait(false);
            return json is null ? null : JsonSerializer.Deserialize<ComposeSaveVersionStamp>(json, SaveStampJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to read the version stamp for driveItem={DocumentSpeId} — treating as no prior stamp (not stale).",
                documentSpeId);
            return null;
        }
    }

    /// <summary>Persists the version stamp for <paramref name="documentSpeId"/> after a successful write
    /// (create or replace). Best-effort: a Redis write failure here never fails the already-successful save
    /// — it only means the NEXT save's staleness assert degrades to "no stamp" (not stale), same as a
    /// freshly-onboarded item that has never been stamped.</summary>
    private async Task SetSaveVersionStampAsync(string documentSpeId, string? eTag, DateTimeOffset savedAtUtc, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrEmpty(eTag))
        {
            return;
        }

        try
        {
            var stamp = new ComposeSaveVersionStamp(eTag, ComposeOperationSchema.Version, savedAtUtc);
            await _cache.SetStringAsync(SaveVersionStampKeyPrefix + documentSpeId, JsonSerializer.Serialize(stamp, SaveStampJsonOptions), ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to persist the version stamp for driveItem={DocumentSpeId} — the save itself succeeded; a future assert may miss this save.",
                documentSpeId);
        }
    }

    // =========================================================================
    // FR-A08/FR-A09 (r8 task 044) — PDF provenance: what a session was opened FROM, and what that PDF
    // BECAME. Two keys because they answer two different questions and neither substitutes for the other:
    //
    //   pdf-session:{sessionId}        -> the source PDF's coordinates. Written at load, read at save.
    //                                    Carries the server's own bytes-first PDF determination forward
    //                                    so the save neither re-derives it nor takes the client's word.
    //   pdf-derived:{driveId}:{speId}  -> the Word document that PDF became. Written at save, read at the
    //                                    NEXT load of that PDF. This is what survives a page refresh.
    //
    // IDistributedCache throughout (ADR-009 — never IMemoryCache): the refresh case is a DIFFERENT request
    // on a possibly different instance, which is exactly the cross-request boundary the ADR is about.
    //
    // Every operation is best-effort and swallows its own failures. Losing either key degrades to the
    // pre-044 behavior (re-project the PDF, stamp by routing origin) — worse, but never wrong in a way the
    // user cannot see, and never a failed Load or Save. That asymmetry is deliberate: this is a recovery
    // aid, and a recovery aid must not become a new way to fail.
    // =========================================================================

    private const string PdfSourceMarkerKeyPrefix = "sdap:compose:pdf-session:";
    private const string PdfDerivedDocumentKeyPrefix = "sdap:compose:pdf-derived:";

    /// <summary>How long a PDF keeps pointing at the document it became. Long enough to cover working on a
    /// document across days; bounded so a PDF that is deleted and replaced at the same drive-item id cannot
    /// redirect indefinitely. On expiry the behavior degrades to a fresh projection, never to an error.</summary>
    private static readonly DistributedCacheEntryOptions PdfProvenanceCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30),
    };

    /// <summary>The source PDF a Compose session was opened from (FR-A08/FR-A09).</summary>
    private sealed record ComposePdfSourceMarker(
        [property: JsonPropertyName("driveId")] string DriveId,
        [property: JsonPropertyName("speId")] string SpeId);

    /// <summary>The Word document a PDF became (FR-A09). Pointer only — see the SetPdfDerivedDocumentAsync
    /// call site for why no version id is stored.</summary>
    private sealed record ComposePdfDerivedDocument(
        [property: JsonPropertyName("driveId")] string DriveId,
        [property: JsonPropertyName("speId")] string SpeId,
        [property: JsonPropertyName("recordId")] Guid? RecordId,
        [property: JsonPropertyName("derivedAtUtc")] DateTimeOffset DerivedAtUtc);

    private async Task SetPdfSourceMarkerAsync(string sessionId, string driveId, string speId, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            await _cache.SetStringAsync(
                    PdfSourceMarkerKeyPrefix + sessionId,
                    JsonSerializer.Serialize(new ComposePdfSourceMarker(driveId, speId), SaveStampJsonOptions),
                    PdfProvenanceCacheOptions,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: failed to record the PDF source marker for session={SessionId} (drive={DriveId} item={SpeId}) — " +
                "a save on this session will stamp by routing origin and will not record what this PDF became (FR-A08/FR-A09 degrade).",
                sessionId, driveId, speId);
        }
    }

    private async Task ClearPdfSourceMarkerAsync(string sessionId, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        try
        {
            await _cache.RemoveAsync(PdfSourceMarkerKeyPrefix + sessionId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: failed to clear the PDF source marker for session={SessionId} — a save on this session could " +
                "stamp a non-PDF document Authored. Logged loudly because this is the marker's one unsafe direction.",
                sessionId);
        }
    }

    private async Task<ComposePdfSourceMarker?> GetPdfSourceMarkerAsync(string? sessionId, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        try
        {
            var json = await _cache.GetStringAsync(PdfSourceMarkerKeyPrefix + sessionId, ct).ConfigureAwait(false);
            return json is null ? null : JsonSerializer.Deserialize<ComposePdfSourceMarker>(json, SaveStampJsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to read the PDF source marker for session={SessionId} — treating the save as not PDF-sourced " +
                "(stamps by routing origin; records no derived-document mapping).",
                sessionId);
            return null;
        }
    }

    private async Task SetPdfDerivedDocumentAsync(
        ComposePdfSourceMarker source,
        string derivedDriveId,
        string derivedSpeId,
        Guid? derivedRecordId,
        DateTimeOffset derivedAtUtc,
        CancellationToken ct)
    {
        if (_cache is null)
        {
            return;
        }

        try
        {
            var derived = new ComposePdfDerivedDocument(derivedDriveId, derivedSpeId, derivedRecordId, derivedAtUtc);
            await _cache.SetStringAsync(
                    PdfDerivedDocumentKey(source.DriveId, source.SpeId),
                    JsonSerializer.Serialize(derived, SaveStampJsonOptions),
                    PdfProvenanceCacheOptions,
                    ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: failed to record that PDF drive={SourceDriveId} item={SourceSpeId} became drive={DerivedDriveId} " +
                "item={DerivedSpeId} — the save itself succeeded; a re-open of the PDF will project it afresh (FR-A09 degrades).",
                source.DriveId, source.SpeId, derivedDriveId, derivedSpeId);
        }
    }

    /// <summary>
    /// FR-A09: resolves the Word document a PDF already became, or null to project the PDF afresh.
    /// <para>
    /// A mapping is only honored when the derived document is actually reachable by this caller. Someone who
    /// deletes the Word document is entitled to re-open the PDF and start over, and a dangling mapping would
    /// otherwise fail their load with a 404 on an item they never asked for.
    /// </para>
    /// <para>
    /// The entry is deliberately NOT evicted on a miss — see the probe comment below. The reachability
    /// signal is per-caller; the mapping is not.
    /// </para>
    /// </summary>
    private async Task<ComposePdfDerivedDocument?> ResolvePdfDerivedDocumentAsync(
        string driveId,
        string speId,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (_cache is null)
        {
            return null;
        }

        try
        {
            var json = await _cache.GetStringAsync(PdfDerivedDocumentKey(driveId, speId), ct).ConfigureAwait(false);
            if (json is null)
            {
                return null;
            }

            var derived = JsonSerializer.Deserialize<ComposePdfDerivedDocument>(json, SaveStampJsonOptions);
            if (derived is null || string.IsNullOrWhiteSpace(derived.DriveId) || string.IsNullOrWhiteSpace(derived.SpeId))
            {
                return null;
            }

            // The probe runs under the CALLER's identity (OBO), so a null here means "this user cannot see
            // it" — which is NOT the same as "it is gone". Deleting the mapping on that signal would let one
            // user without access destroy the recovery path for everyone else on a tenant-scoped, per-item
            // mapping. So: fall through for this caller and leave the entry alone. It expires on its own TTL,
            // and a genuinely deleted document simply falls through for every caller until it does.
            var visibleToCaller = await _spe.GetFileMetadataAsUserAsync(httpContext, derived.DriveId, derived.SpeId, ct)
                .ConfigureAwait(false);
            if (visibleToCaller is not null)
            {
                return derived;
            }

            _logger.LogInformation(
                "Compose load: PDF drive={DriveId} item={SpeId} maps to drive={DerivedDriveId} item={DerivedSpeId}, which this " +
                "caller cannot see (deleted, or no access) — projecting the PDF afresh for them; the mapping is left intact " +
                "for other callers (FR-A09).",
                driveId, speId, derived.DriveId, derived.SpeId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose load: failed to resolve the derived-document mapping for PDF drive={DriveId} item={SpeId} — projecting the " +
                "PDF afresh (FR-A09 degrades; never a failed Load).",
                driveId, speId);
            return null;
        }
    }

    private static string PdfDerivedDocumentKey(string driveId, string speId) =>
        PdfDerivedDocumentKeyPrefix + driveId + ":" + speId;

    // =========================================================================
    // G10 (FR-09, task 040) — Document Profile re-run: reload/onload re-trigger (storm-safe) + the shared
    // manual "Refresh Profile" leg. Both reuse the EXISTING fire-and-forget DispatchBackgroundProfile
    // pipeline (never a second trigger). The storm guard is a DEDICATED per-doc "profiled-at eTag" stamp
    // (IDistributedCache, ADR-009) — INTENTIONALLY separate from the FR-08 save-version stamp so it never
    // perturbs the save-path staleness/re-anchor semantics. A reopen re-profiles ONLY when the live eTag
    // differs from the last-profiled eTag (an external Word edit, or a doc Compose never profiled); an
    // unchanged reopen matches the stamp → skip (no profiling storm on repeated reopens).
    // =========================================================================

    private const string ProfiledETagKeyPrefix = "sdap:compose:profiled-etag:";

    private async Task<string?> GetProfiledETagAsync(string documentSpeId, CancellationToken ct)
    {
        if (_cache is null)
        {
            return null;
        }
        try
        {
            return await _cache.GetStringAsync(ProfiledETagKeyPrefix + documentSpeId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose profile (G10): failed to read the profiled-eTag stamp for driveItem={DocumentSpeId} — treating as never-profiled (may re-trigger once).",
                documentSpeId);
            return null;
        }
    }

    private async Task SetProfiledETagAsync(string documentSpeId, string eTag, CancellationToken ct)
    {
        if (_cache is null || string.IsNullOrEmpty(eTag))
        {
            return;
        }
        try
        {
            await _cache.SetStringAsync(ProfiledETagKeyPrefix + documentSpeId, eTag, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose profile (G10): failed to persist the profiled-eTag stamp for driveItem={DocumentSpeId} — a future reopen may re-trigger once more (best-effort, never a storm).",
                documentSpeId);
        }
    }

    /// <summary>
    /// G10 (FR-09, task 040): the reload/onload re-trigger. On a Path A reopen (an existing
    /// <c>sprk_document</c>), re-dispatch the fire-and-forget Document Profile ONLY when the doc CHANGED
    /// since Compose last profiled it (live eTag ≠ the profiled-eTag stamp) — then stamp the current eTag so
    /// a subsequent unchanged reopen skips (the storm guard closes the loop). Best-effort: never blocks or
    /// fails Load; a null <c>_documentProfileAi</c>/cache simply no-ops.
    /// </summary>
    private async Task MaybeRetriggerProfileOnLoadAsync(
        Guid documentRecordId, string documentSpeId, string liveETag, HttpContext httpContext, CancellationToken ct)
    {
        try
        {
            var profiledETag = await GetProfiledETagAsync(documentSpeId, ct).ConfigureAwait(false);
            if (string.Equals(profiledETag, liveETag, StringComparison.Ordinal))
            {
                return; // unchanged since the last profile — skip (no storm)
            }

            _profileDispatcher.Dispatch(documentRecordId, httpContext);
            await SetProfiledETagAsync(documentSpeId, liveETag, ct).ConfigureAwait(false);

            _logger.LogInformation(
                "Compose reload profile re-trigger (G10): document {DocumentRecordId} (driveItem={DocumentSpeId}) changed since last profile (profiledETag={ProfiledETag}, liveETag={LiveETag}) — profile re-dispatched fire-and-forget.",
                documentRecordId, documentSpeId, profiledETag, liveETag);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose reload profile re-trigger (G10): failed for document {DocumentRecordId} — best-effort, Load unaffected.",
                documentRecordId);
        }
    }

    /// <inheritdoc />
    public async Task<bool> RefreshProfileAsync(
        RefreshComposeProfileRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.DocumentRecordId == Guid.Empty)
        {
            throw new ArgumentException("A DocumentRecordId is required to refresh a Compose document's profile.", nameof(request));
        }

        // G10 manual leg: a user-initiated on-demand re-run. UNCONDITIONAL (unlike the reload guard) — the
        // user explicitly asked to refresh — but still fire-and-forget + best-effort. Stamp the current eTag
        // (when known) so an immediately-following reopen does not redundantly re-trigger.
        _profileDispatcher.Dispatch(request.DocumentRecordId, httpContext);
        if (!string.IsNullOrWhiteSpace(request.DocumentSpeId) && !string.IsNullOrWhiteSpace(request.ETag))
        {
            await SetProfiledETagAsync(request.DocumentSpeId!, request.ETag!, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Compose manual profile refresh (G10): document {DocumentRecordId} — profile re-dispatched fire-and-forget on user request.",
            request.DocumentRecordId);
        return true;
    }

    /// <summary>
    /// FR-08 (task 050): the base moved under the client since it was loaded (the persisted version stamp's
    /// eTag no longer matches the live SPE eTag). Re-anchors <paramref name="request"/>'s operation log +
    /// comments against the FRESHLY re-downloaded current bytes via <see cref="AnnotationReanchorService"/>
    /// (the KEEP asset, reused verbatim), applies ONLY the exact-paraId AUTO band through the Patch Engine,
    /// and returns the patched bytes alongside the full band summary. REVIEW/ORPHAN ops/comments are
    /// deliberately NOT applied — an op's anchor is never rewritten (I-7, no write-path text-search), so a
    /// fuzzy (non-exact-id) match is not safe to auto-apply; it surfaces in the summary instead, never
    /// silently applied and never silently dropped.
    /// </summary>
    private async Task<(byte[] PatchedBytes, ReanchorSummary Summary)> ReanchorStaleSaveAsync(
        SaveComposeDocumentRequest request,
        byte[] originalBaseline,
        HttpContext httpContext,
        DateTimeOffset observedAt,
        bool trackChanges,
        CancellationToken cancellationToken)
    {
        // Re-download the CURRENT (live) bytes — the base the op log must now be checked against.
        //
        // FR-S07 (r8 task 014): a download miss REFUSES THE SAVE. It previously returned `originalBaseline`
        // — the LOAD-TIME bytes — and let the caller persist them. This method is only ever reached when
        // the base has already been observed to MOVE, so those bytes are by definition older than the
        // version they were about to replace: the fallback silently overwrote a newer document with
        // pre-edit content and reported HTTP 200. It was the only data-destroying path in Track S.
        //
        // Its comment claimed it "fails closed" because every op surfaced as ORPHAN — and that was true of
        // the OPS. It was the BYTES that were wrong. Surfacing the ops honestly while writing a stale
        // document is precisely the Half-A/Half-B confusion this project exists to remove.
        //
        // Deleted rather than guarded: a re-anchor with no current bytes cannot produce a correct save
        // under any condition, so there is no version of this fallback worth keeping. The throw is caught
        // at the endpoint and reported as `refused-stale` (FR-S06) — a defined terminal outcome, never an
        // HTTP 422 content refusal (ADR-049).
        Stream? stream;
        try
        {
            stream = await _spe.DownloadFileAsUserAsync(httpContext, request.DriveId!, request.DocumentSpeId!, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose save: stale-base re-anchor could not re-download the current bytes for driveItem={DocumentSpeId} — REFUSING the save; nothing written, stored version untouched.",
                request.DocumentSpeId);
            throw new ComposeStaleBaselineUnavailableException(request.DocumentSpeId!, "download-faulted", ex);
        }

        if (stream is null)
        {
            _logger.LogWarning(
                "Compose save: stale-base re-anchor got no content stream for driveItem={DocumentSpeId} — REFUSING the save; nothing written, stored version untouched.",
                request.DocumentSpeId);
            throw new ComposeStaleBaselineUnavailableException(request.DocumentSpeId!, "download-empty");
        }

        byte[] currentBytes;
        await using (stream.ConfigureAwait(false))
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            currentBytes = buffer.ToArray();
        }

        // Seam A (UAT round-2 item #4 — advisory comments must survive a STALE save and bake as native
        // w:comment). The client's minted paraIds live only in request.ParaIdMap — physically absent from
        // BOTH the retained baseline AND the freshly-fetched current bytes. Without them, an advisory
        // comment's client-minted ParaId resolves to nothing in currentParaIds below → 0.0 re-anchor score
        // → ORPHAN band → surfaced-but-never-baked (the exact "comments don't survive Save to Word" bug the
        // non-stale path does NOT have, because it stamps first — see the sibling Stamp call in SaveAsync).
        // Stamp both corpora with the SAME fail-open, count-gated, text-verified stamper: where the version
        // bump was benign (unchanged paragraph structure + text — a metadata touch or an eTag-counter move
        // that did not really edit content), the client paraId becomes physically present in currentBytes →
        // ResolveByParaId hits confidence 1.0 → AUTO → the exact-paraId auto-apply gate below bakes the
        // comment. Where the current bytes genuinely diverged (different paragraph count, or the anchored
        // text changed), the stamper no-ops (count gate / text-verify) and the comment correctly stays
        // ORPHAN — never a wrong-paragraph stamp. Stamping originalBaseline too repopulates each comment's
        // TextPattern (via the IndexOfParaId hint below) so a text-drifted clause surfaces as REVIEW rather
        // than a blind ORPHAN in the re-anchor banner.
        if (request.ParaIdMap is { Count: > 0 })
        {
            originalBaseline = _baselineParaIdStamper.Stamp(originalBaseline, request.ParaIdMap);
            currentBytes = _baselineParaIdStamper.Stamp(currentBytes, request.ParaIdMap);
        }

        IReadOnlyList<string> oldParagraphs;
        IReadOnlyList<string?> oldParaIds;
        IReadOnlyList<string> currentParagraphs;
        IReadOnlyList<string?> currentParaIds;
        try
        {
            oldParagraphs = AnnotationReanchorService.ExtractParagraphTexts(originalBaseline);
            oldParaIds = AnnotationReanchorService.ExtractParaIds(originalBaseline);
            currentParagraphs = AnnotationReanchorService.ExtractParagraphTexts(currentBytes);
            currentParaIds = AnnotationReanchorService.ExtractParaIds(currentBytes);
        }
        catch (Exception ex) when (ex is DocxAnnotationException or ArgumentException)
        {
            _logger.LogWarning(ex,
                "Compose save: stale-base re-anchor could not read the paragraph corpus for driveItem={DocumentSpeId} — every op/comment surfaces as ORPHAN.",
                request.DocumentSpeId);
            return (currentBytes, BuildAllOrphanSummary(request, observedAt));
        }

        var ops = request.OperationLog?.Operations ?? Array.Empty<ComposeOperation>();
        var comments = request.Comments ?? Array.Empty<ComposeAnchoredComment>();

        var priorAnchors = new List<PriorAnchor>(ops.Count + comments.Count);
        for (var i = 0; i < ops.Count; i++)
        {
            var op = ops[i];
            var hint = IndexOfParaId(oldParaIds, op.ParaId);
            priorAnchors.Add(new PriorAnchor(
                Id: $"op-{i}",
                Type: op.GetType().Name,
                TextPattern: hint >= 0 && hint < oldParagraphs.Count ? oldParagraphs[hint] : string.Empty,
                ParagraphHint: hint,
                Preview: null,
                ParaId: op.ParaId));
        }

        for (var i = 0; i < comments.Count; i++)
        {
            var c = comments[i];
            var hint = IndexOfParaId(oldParaIds, c.ParaId);
            priorAnchors.Add(new PriorAnchor(
                Id: $"comment-{i}",
                Type: "comment",
                TextPattern: hint >= 0 && hint < oldParagraphs.Count ? oldParagraphs[hint] : string.Empty,
                ParagraphHint: hint,
                Preview: c.CommentText,
                ParaId: c.ParaId));
        }

        var summary = AnnotationReanchorService.Reanchor(priorAnchors, currentParagraphs, request.DocumentSpeId, observedAt, currentParaIds);

        // Only an EXACT paraId match (confidence 1.0 — the paragraph's w14:paraId is still present,
        // unchanged, in the current document) is safe to auto-apply verbatim: the op's anchor is never
        // rewritten, so a fuzzy AUTO (a different paraId that merely scored well on content) would apply
        // the op against the WRONG paragraph id and fail to resolve (or worse, silently mis-anchor). Fuzzy
        // AUTO/REVIEW/ORPHAN all surface for review — never silently applied.
        var autoIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in summary.Annotations)
        {
            if (r.Band == ReanchorBand.Auto && r.Confidence >= 1.0)
            {
                autoIds.Add(r.Id);
            }
        }

        var autoOps = new List<ComposeOperation>();
        for (var i = 0; i < ops.Count; i++)
        {
            if (autoIds.TryGetValue($"op-{i}", out _))
            {
                autoOps.Add(ops[i]);
            }
        }

        var autoComments = new List<ComposeAnchoredComment>();
        for (var i = 0; i < comments.Count; i++)
        {
            if (autoIds.TryGetValue($"comment-{i}", out _))
            {
                autoComments.Add(comments[i]);
            }
        }

        byte[] patched;
        try
        {
            patched = (autoOps.Count == 0 && autoComments.Count == 0)
                ? currentBytes
                : _patchEngine.Apply(
                    currentBytes,
                    new ComposeOperationLog { SchemaVersion = request.OperationLog?.SchemaVersion ?? ComposeOperationSchema.Version, Operations = autoOps },
                    autoComments,
                    ResolveRevisionAuthor(httpContext),
                    observedAt,
                    trackChanges: trackChanges);
        }
        catch (ComposePatchException ex)
        {
            // An AUTO-band op that still fails to resolve at patch time (an edge case beyond the reanchor's
            // own exact-paraId check) is never silently applied — degrade the whole batch to ORPHAN rather
            // than guess a partial apply that could mis-place bytes.
            _logger.LogWarning(ex,
                "Compose save: stale-base re-anchor's AUTO band failed to apply for driveItem={DocumentSpeId} ({Kind}) — degrading the whole batch to ORPHAN.",
                request.DocumentSpeId, ex.Kind);
            return (currentBytes, BuildAllOrphanSummary(request, observedAt));
        }

        if (_reanchorService is not null)
        {
            try
            {
                await _reanchorService.SaveSummaryAsync(request.DocumentSpeId!, summary, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose save: failed to persist the stale-base re-anchor summary for driveItem={DocumentSpeId} — the save itself succeeded.",
                    request.DocumentSpeId);
            }
        }

        return (patched, summary);
    }

    // ─────────────────────────────────────────────────────────────────────────────────────────────────────
    // Prong 1 (task 055) — best-effort per-paragraph recovery for an OP-LEVEL patch refusal on the loaded-doc
    // save path. Distinct from ReanchorStaleSaveAsync above (which handles a STALE BASE — an eTag mismatch —
    // by content-similarity re-anchoring across old→current paragraphs). Here the base is CURRENT; a single op
    // just fails to anchor. Rather than lose the whole editing session (the pre-prong-1 behavior: any refusal
    // → 422), apply the resolvable paragraph-units and surface the unresolvable ops.
    // ─────────────────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A <see cref="ComposePatchErrorKind"/> that condemns the WHOLE batch (the docx itself is unreadable, or
    /// the op-log schema version is unsupported) rather than a single op's anchor — for these, best-effort
    /// partial apply is meaningless, so the save must still fail hard (mapped to a ProblemDetails by the
    /// endpoint). Every OTHER kind is an op-level anchoring/resolution refusal eligible for prong-1 recovery.
    /// </summary>
    private static bool IsBatchLevelPatchRefusal(ComposePatchErrorKind kind) =>
        kind is ComposePatchErrorKind.MalformedDocument or ComposePatchErrorKind.UnsupportedSchemaVersion;

    /// <summary>
    /// True for a STRUCTURAL / whole-document op — one that splits/merges/inserts/deletes paragraphs, edits a
    /// table's structure, or reconciles EVERY tracked revision (scope=All). These span or renumber paragraphs
    /// (and mint child paraIds later ops depend on), so under the engine's intra-paragraph sequential rebasing
    /// they are NOT safe to apply piecemeal: prong-1 groups them into ONE all-or-nothing unit applied LAST
    /// (mirroring the engine's own structural-last pass). Inline ops (text/mark/setBlockAttr/single-revision)
    /// are grouped by paraId instead — the paragraph being the safe atomic unit.
    /// </summary>
    private static bool IsStructuralOrGlobalOp(ComposeOperation op) => op switch
    {
        SplitParagraphOperation or MergeParagraphOperation or InsertParagraphOperation
            or DeleteParagraphOperation or TableOperation => true,
        AcceptRevisionOperation { Scope: ComposeRevisionScope.All } => true,
        RejectRevisionOperation { Scope: ComposeRevisionScope.All } => true,
        _ => false,
    };

    /// <summary>
    /// Prong 1 (task 055). Applies <paramref name="log"/> onto <paramref name="baseline"/> in the LARGEST
    /// units provably safe under the engine's intra-paragraph sequential rebasing, applying every unit that
    /// resolves and surfacing every unit that refuses (never a wrong edit, never a silent drop):
    /// <list type="bullet">
    /// <item>Inline ops (text/mark/setBlockAttr/single-revision) grouped by <c>paraId</c> — the paragraph is
    ///   atomic (dropping one op would leave later same-paragraph ops mis-anchored), applied in first-seen
    ///   order; each paragraph's anchored comments ride its unit so the engine's comments-first-per-Apply
    ///   ordering (EDGE-1) is preserved per paragraph.</item>
    /// <item>Structural / All-revision ops as ONE all-or-nothing unit applied LAST (keeps minted-paraId
    ///   lineage intact).</item>
    /// </list>
    /// Each unit runs through the SAME <see cref="ComposeShadowPatchEngine.Apply"/> onto the cumulative bytes,
    /// so the byte result for the resolvable paragraphs matches the clean-batch path. A unit throwing a
    /// batch-level refusal (a malformed cumulative package — not expected mid-recovery) propagates.
    /// </summary>
    private byte[] ApplyBestEffortByParagraph(
        byte[] baseline,
        ComposeOperationLog log,
        IReadOnlyList<ComposeAnchoredComment>? comments,
        string author,
        DateTimeOffset observedAt,
        bool trackChanges,
        out PartialApplySummary summary)
    {
        var ops = log.Operations ?? Array.Empty<ComposeOperation>();
        var schemaVersion = log.SchemaVersion;

        // Inline ops grouped by paraId (first-seen order preserved) + a single structural/global unit.
        var inlineOrder = new List<string>();
        var inlineOps = new Dictionary<string, List<ComposeOperation>>(StringComparer.OrdinalIgnoreCase);
        var structural = new List<ComposeOperation>();
        foreach (var op in ops)
        {
            if (IsStructuralOrGlobalOp(op))
            {
                structural.Add(op);
                continue;
            }
            if (!inlineOps.TryGetValue(op.ParaId, out var list))
            {
                list = new List<ComposeOperation>();
                inlineOps[op.ParaId] = list;
                inlineOrder.Add(op.ParaId);
            }
            list.Add(op);
        }

        // Distribute anchored comments into their paraId's unit; a comment whose paraId carries no inline op
        // gets its own comment-only unit (still applied in the inline pass, so it lands before any structural
        // change to that paragraph — mirrors the engine's comments-first ordering).
        var commentsByPara = new Dictionary<string, List<ComposeAnchoredComment>>(StringComparer.OrdinalIgnoreCase);
        var unbakeableComments = 0;
        foreach (var c in comments ?? Array.Empty<ComposeAnchoredComment>())
        {
            var key = c.ParaId ?? string.Empty;
            if (!commentsByPara.TryGetValue(key, out var list))
            {
                list = new List<ComposeAnchoredComment>();
                commentsByPara[key] = list;
                if (!inlineOps.ContainsKey(key))
                {
                    inlineOrder.Add(key); // comment-only paragraph unit
                    inlineOps[key] = new List<ComposeOperation>();
                }
            }
            list.Add(c);
        }

        var unresolved = new List<UnresolvedComposeOp>();
        var appliedCount = 0;
        // agreements-r1 UAT round-1 #4: a COMMENT is a NON-DESTRUCTIVE change, so a comments-only review
        // save (empty op-log — a review placed advisory notes but made no text edits) must degrade
        // gracefully, not lose the WHOLE save when one note can't anchor. Pre-fix the "did anything apply?"
        // signal counted only OPS, so a fully-bakeable comment contributed 0 and the caller's
        // `appliedCount == 0` guard re-threw the anchor refusal → 422, discarding even the notes that DID
        // anchor. Count baked comments as applied work (folded into the summary below) so the guard stays
        // correct AND surface the unbakeable notes on `unresolved` (skip-with-report, never a silent drop).
        // A failing TEXT op still lands in `unresolved` exactly as before — its honest contract is unchanged.
        var bakedCommentCount = 0;
        var current = baseline;

        // Inline paragraph units first, then the structural unit LAST.
        foreach (var paraId in inlineOrder)
        {
            var unitOps = inlineOps[paraId];
            commentsByPara.TryGetValue(paraId, out var unitComments);
            var (bytes, refusal) = TryApplyPatchUnit(current, schemaVersion, unitOps, unitComments, author, observedAt, trackChanges);
            if (refusal is null)
            {
                current = bytes;
                appliedCount += unitOps.Count;
                if (unitComments is not null)
                    bakedCommentCount += unitComments.Count;
            }
            else
            {
                foreach (var op in unitOps)
                    unresolved.Add(new UnresolvedComposeOp(op.ParaId, op.GetType().Name, refusal.Kind.ToString(), refusal.Message));
                if (unitComments is not null)
                {
                    unbakeableComments += unitComments.Count;
                    // Surface each un-anchorable advisory note so the client reports it (skip-with-report).
                    // OpType "AdvisoryComment" distinguishes a non-destructive note from a lost edit op.
                    foreach (var c in unitComments)
                        unresolved.Add(new UnresolvedComposeOp(c.ParaId ?? string.Empty, "AdvisoryComment", refusal.Kind.ToString(), refusal.Message));
                }
            }
        }

        if (structural.Count > 0)
        {
            var (bytes, refusal) = TryApplyPatchUnit(current, schemaVersion, structural, null, author, observedAt, trackChanges);
            if (refusal is null)
            {
                current = bytes;
                appliedCount += structural.Count;
            }
            else
            {
                foreach (var op in structural)
                    unresolved.Add(new UnresolvedComposeOp(op.ParaId, op.GetType().Name, refusal.Kind.ToString(), refusal.Message));
            }
        }

        if (unbakeableComments > 0)
        {
            _logger.LogWarning(
                "Compose save: best-effort recovery could not bake {UnbakeableComments} advisory comment(s) whose paragraph unit refused.",
                unbakeableComments);
        }

        // Comments are first-class items in the partial-apply accounting alongside ops: Total counts every
        // op + comment, AppliedCount counts applied ops + baked comments, and the invariant
        // Total == AppliedCount + UnresolvedCount holds. An op-only batch (the existing seam cases) has no
        // comments, so these reduce to ops.Count / appliedCount exactly as before (no behavior change).
        summary = new PartialApplySummary(
            Total: ops.Count + (comments?.Count ?? 0),
            AppliedCount: appliedCount + bakedCommentCount,
            UnresolvedCount: unresolved.Count,
            Unresolved: unresolved,
            ComputedAtUtc: observedAt);
        return current;
    }

    /// <summary>
    /// Applies ONE prong-1 unit through the patch engine, returning the patched bytes on success or the
    /// ORIGINAL bytes + the op-level <see cref="ComposePatchException"/> on refusal. A batch-level refusal
    /// (malformed / schema — see <see cref="IsBatchLevelPatchRefusal"/>) is rethrown (the cumulative bytes are
    /// unusable). A unit with no ops and no comments is a byte-identical no-op (the engine's passthrough).
    /// </summary>
    private (byte[] Bytes, ComposePatchException? Refusal) TryApplyPatchUnit(
        byte[] input,
        string schemaVersion,
        IReadOnlyList<ComposeOperation> unitOps,
        IReadOnlyList<ComposeAnchoredComment>? unitComments,
        string author,
        DateTimeOffset observedAt,
        bool trackChanges)
    {
        try
        {
            var bytes = _patchEngine.Apply(
                input,
                new ComposeOperationLog { SchemaVersion = schemaVersion, Operations = unitOps },
                unitComments,
                author,
                observedAt,
                trackChanges: trackChanges);
            return (bytes, null);
        }
        catch (ComposePatchException ex) when (!IsBatchLevelPatchRefusal(ex.Kind))
        {
            return (input, ex);
        }
    }

    /// <summary>0-based index of the FIRST current paraId equal to <paramref name="paraId"/> (case-sensitive
    /// — <see cref="AnnotationReanchorService.ExtractParaIds"/> already upper-cases every id), or -1 when
    /// absent/null.</summary>
    private static int IndexOfParaId(IReadOnlyList<string?> paraIds, string? paraId)
    {
        if (string.IsNullOrEmpty(paraId))
        {
            return -1;
        }

        for (var i = 0; i < paraIds.Count; i++)
        {
            if (string.Equals(paraIds[i], paraId, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>Fail-closed summary: every op/comment in <paramref name="request"/> surfaces as ORPHAN
    /// (never silently applied, never silently dropped) when the current base could not be re-downloaded or
    /// read.</summary>
    private static ReanchorSummary BuildAllOrphanSummary(SaveComposeDocumentRequest request, DateTimeOffset observedAt)
    {
        var opsCount = request.OperationLog?.Operations.Count ?? 0;
        var commentsCount = request.Comments?.Count ?? 0;
        var total = opsCount + commentsCount;

        var annotations = new List<ReanchoredAnnotation>(total);
        for (var i = 0; i < opsCount; i++)
        {
            annotations.Add(new ReanchoredAnnotation(
                Id: $"op-{i}", Type: request.OperationLog!.Operations[i].GetType().Name, Preview: null,
                Band: ReanchorBand.Orphan, Confidence: 0.0, MatchedParagraphIndex: -1,
                ContentSimilarity: 0.0, StructuralProximity: 0.0, Ambiguous: false, MatchedParagraphPreview: null));
        }
        for (var i = 0; i < commentsCount; i++)
        {
            annotations.Add(new ReanchoredAnnotation(
                Id: $"comment-{i}", Type: "comment", Preview: request.Comments![i].CommentText,
                Band: ReanchorBand.Orphan, Confidence: 0.0, MatchedParagraphIndex: -1,
                ContentSimilarity: 0.0, StructuralProximity: 0.0, Ambiguous: false, MatchedParagraphPreview: null));
        }

        return new ReanchorSummary(
            DocumentSpeId: request.DocumentSpeId,
            Total: total,
            AutoCount: 0,
            ReviewCount: 0,
            OrphanCount: total,
            Annotations: annotations,
            ComputedAtUtc: observedAt);
    }

    /// <summary>
    /// Resolves the tracked-change revision AUTHOR for a synthesized redline (task 022) from the caller's
    /// OBO identity — the acting user's display name (<c>name</c> / <see cref="ClaimTypes.Name"/> /
    /// <c>preferred_username</c>), so Word attributes the user's own direct-typing edits to the user.
    /// Falls back to a stable product label when no name claim is present (never empty — the synthesizer
    /// requires a non-whitespace author).
    /// </summary>
    private static string ResolveRevisionAuthor(HttpContext httpContext)
    {
        var user = httpContext.User;
        var name = user?.FindFirst("name")?.Value
            ?? user?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? user?.FindFirst("preferred_username")?.Value
            ?? user?.Identity?.Name;

        return string.IsNullOrWhiteSpace(name) ? "Spaarke Compose" : name!.Trim();
    }

    /// <summary>
    /// STEP 5 (FR-30, compose-r2, #629) — best-effort durable memory CAPTURE. Distils the bound session's
    /// durable insights (defined terms today) into Record-scope memory keyed by the saved
    /// <c>sprk_document</c>, via the ADR-013 <see cref="IComposeMemoryCapture"/> facade. The whole body is
    /// guarded so a memory-capture failure NEVER throws — a Save must never be blocked or failed by it. A
    /// no-op when the facade is unregistered (null gate) or no session is bound.
    /// </summary>
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

        // 1) Idempotency check by SPE drive-item id (alt key sprk_graphitemid_uk). The lookup also carries the
        //    FR-C3 dedup columns so graduate-on-divergence needs no extra round-trip.
        var existingRow = await TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
            .ConfigureAwait(false);

        if (existingRow is not null)
        {
            var existingId = existingRow.Id;
            _logger.LogDebug(
                "Compose promote: existing sprk_document {DocumentRecordId} found for driveItem={DocumentSpeId} — idempotent no-op",
                existingId, request.DocumentSpeId);

            // FR-07 rebind is OPTIONAL (task 110): skip entirely when no session is bound
            // (transient Browse/local-file first Save). RebindSessionDocumentIdAsync is already
            // null-tolerant, but skipping avoids an empty-session lookup + a misleading warn.
            if (!string.IsNullOrWhiteSpace(request.SessionId))
            {
                await RebindSessionDocumentIdAsync(
                        tenantId: request.TenantId,
                        sessionId: request.SessionId,
                        currentDocumentId: request.DocumentSpeId,
                        newDocumentId: existingId.ToString(),
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            // FR-C3 graduate-on-divergence: if this existing row is a hash-linked COPY whose content has now
            // diverged from the canonical it was linked at, sever the link so it becomes its own canonical.
            await GraduateLinkedCopyIfDivergedAsync(existingRow, request, cancellationToken)
                .ConfigureAwait(false);

            // FR-S09 item 7 (r8 task 016): refresh the file metadata this save just changed.
            //
            // This branch is the REPLACE path — every save after the first lands here. It wrote a new
            // version to SPE (new byte length, and a new web URL whenever the file was renamed or moved)
            // and then returned without touching the row, so `sprk_filesize` and `sprk_filepath` kept
            // describing the FIRST version forever. Downstream readers trust those columns: the
            // Documents grid shows the size, "Open in SharePoint" follows the path. Both quietly drifted.
            //
            // Only these two columns, and only when the caller supplied them: the create branch owns the
            // fields that define IDENTITY (origin, transient key, canonical link) and those must never be
            // mutated by a later save — the existing-row branch's whole contract is idempotence.
            var metadataRefreshFailed = false;
            var refreshFields = new Dictionary<string, object>();
            if (request.FileSize.HasValue)
            {
                // Whole Number (int) column — same cast the create branch uses; the OrganizationService
                // write path is strict about CLR type.
                refreshFields[FileSizeAttribute] = (int)request.FileSize.Value;
            }
            if (!string.IsNullOrWhiteSpace(request.FilePath))
            {
                refreshFields[FilePathAttribute] = request.FilePath!;
            }
            if (refreshFields.Count > 0)
            {
                try
                {
                    await _dataverse.UpdateAsync(DocumentLogicalName, existingId, refreshFields, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Never fails the save — the document IS stored. But it is not silent either: the
                    // flag rides back to SaveAsync, which turns it into a `document-metadata-stale`
                    // degradation warning on a `persisted-with-warnings` outcome.
                    metadataRefreshFailed = true;
                    _logger.LogWarning(ex,
                        "Compose promote: file-metadata refresh failed for sprk_document {DocumentRecordId} " +
                        "(driveItem={DocumentSpeId}). The save itself is unaffected; sprk_filesize/sprk_filepath " +
                        "are now stale for this row.",
                        existingId, request.DocumentSpeId);
                }
            }

            return new PromoteComposeDocumentResult
            {
                DocumentSpeId = request.DocumentSpeId,
                SessionId = request.SessionId,
                DocumentRecordId = existingId,
                WasCreated = false,
                MetadataRefreshFailed = metadataRefreshFailed,
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

        // G1 (FR-01, task 020): persist the durable origin marker ONLY at create-on-save (this branch —
        // the idempotent existing-row branch above never reaches here, so a subsequent replace-path save
        // never mutates an already-persisted origin). Defaults to Imported (the Dataverse field's own
        // default) when the caller supplies none (e.g. a standalone /promote call that predates G1) —
        // never left unset, so a fresh row is never silently null-origin.
        entity[ComposeOriginAttribute] = new OptionSetValue((int)(request.Origin ?? ComposeOrigin.Imported));

        // G7 (FR-06, task 022): stamp the client-minted transient dedup key ONLY at create (this branch;
        // the idempotent existing-row branch above never reaches here). The single-column alt-key
        // sprk_composetransientkey_uk makes this the durable dedup identity for repeated create-on-save
        // calls (see TryFindDocumentByTransientKeyAsync + the SaveAsync transient branch). Omitted for a
        // replace-path save or an older client that predates G7 (nulls are not enforced-unique).
        if (!string.IsNullOrWhiteSpace(request.TransientKey))
        {
            entity[ComposeTransientKeyAttribute] = request.TransientKey!;
        }

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

        // Task 041 B-MED-3 (operator resolution 2026-08-07, option C): a PDF-sourced create-on-save
        // INHERITS the source PDF record's link lookups so the new Word document files ALONGSIDE the
        // PDF (same matter/project/… — containers are BU-level, so placement is already shared; the
        // RECORD association is what was missing). The copied set is the ADR-024 sprk_document link
        // vocabulary (mirrors AttachmentDocumentAssociationRung's map). Best-effort: a failed source
        // read logs LOUDLY and the create proceeds unassociated (mirrors the source having no links —
        // never fails the save); the idempotent existing-row branch above never reaches here, so an
        // existing record's links are never mutated.
        if (request.SourceDocumentRecordId is { } sourceRecordId)
        {
            try
            {
                var sourceEntity = await _dataverse.RetrieveAsync(
                        DocumentLogicalName,
                        sourceRecordId,
                        DocumentAssociationLookupAttributes,
                        cancellationToken)
                    .ConfigureAwait(false);

                var inherited = 0;
                if (sourceEntity is not null)
                {
                    foreach (var lookup in DocumentAssociationLookupAttributes)
                    {
                        var reference = sourceEntity.GetAttributeValue<EntityReference>(lookup);
                        if (reference is null || reference.Id == Guid.Empty)
                        {
                            continue;
                        }

                        entity[lookup] = new EntityReference(reference.LogicalName, reference.Id);
                        inherited++;
                    }
                }

                if (inherited > 0)
                {
                    _logger.LogInformation(
                        "Compose promote: inherited {Count} record link(s) from source document {SourceRecordId} (PDF-sourced create — filed alongside the source).",
                        inherited, sourceRecordId);
                }
                else
                {
                    _logger.LogInformation(
                        "Compose promote: source document {SourceRecordId} carries no record links to inherit — the new document is created unassociated (mirrors the source).",
                        sourceRecordId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose promote: link inheritance from source document {SourceRecordId} failed — creating the new document UNASSOCIATED (the save itself is not affected). Associate manually or re-file from the Documents surface.",
                    sourceRecordId);
            }
        }

        // ── FR-C3 content-dedup, graduate-on-divergence (CREATE branch) ─────────────────────────────
        // (email-communication-intelligence-r2, merged from master 2026-08-07 — runs AFTER the B-MED-3
        // link inheritance above; the two blocks stamp disjoint attribute sets on the same new entity.)
        // Read the just-uploaded item's content identity (quickXorHash) and record it. On a byte-identical
        // hit against an existing CANONICAL, LINK this editable copy (sprk_canonicaldocument) rather than
        // suppressing it: a Compose document is a living document that diverges on first edit — the idempotent
        // branch above graduates it then. NOTIFY (never silent). Best-effort/non-fatal (NFR-04): any failure →
        // create proceeds unstamped. No-op when the detector is absent (bare test ctor) or the drive id is
        // unknown. Suppression is deliberately NOT used here (that is the immutable email-attachment path's
        // behavior; suppressing an editable copy would cross-wire the session onto a foreign drive-item).
        if (_dedupDetector is not null && !string.IsNullOrWhiteSpace(request.GraphDriveId))
        {
            try
            {
                var (contentHash, canonicalId) = await _dedupDetector
                    .ResolveContentIdentityAsync(request.GraphDriveId!, request.DocumentSpeId, cancellationToken)
                    .ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(contentHash))
                    entity[CanonicalHashAttribute] = contentHash!;
                if (canonicalId is { } canonical)
                {
                    entity[CanonicalDocumentAttribute] = new EntityReference(DocumentLogicalName, canonical);
                    // Was `FindFirst("oid")` with no schema form: under inbound claim mapping the short
                    // claim does not exist, so this resolved NULL and NotifyLinkedCopyAsync bailed with
                    // "no resolvable uploader oid" — the linked-copy notification was never delivered.
                    var ownerOid = CallerResolution.ResolveObjectId(httpContext.User);
                    await _dedupDetector
                        .NotifyLinkedCopyAsync(ownerOid, canonical, effectiveFileName, cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Compose content-dedup (create) failed (non-fatal) for driveItem={DocumentSpeId}; creating without dedup stamp.",
                    request.DocumentSpeId);
            }
        }

        // FR-07(d) (task 013): atomic UPSERT on the sprk_graphitemid_uk alternate key — replaces the
        // read-then-CreateAsync sequence so two concurrent first-saves of the SAME minted SPE item can
        // never each insert a row (Dataverse resolves the target server-side; the second UPDATES the
        // first's row → exactly one sprk_document, no TOCTOU window). The key uses the RAW DocumentSpeId
        // string, identical to the read above (TryFindDocumentByGraphItemIdAsync): sprk_graphitemid is an
        // opaque SPE drive-item id (a STRING, not a GUID), so the match is exact-string and ADR-044 GUID
        // canonicalization does NOT apply (verified — the alt-key lookup keys on the raw string).
        entity.KeyAttributes[GraphItemIdAttribute] = request.DocumentSpeId;

        Guid newId;
        bool rowCreatedThisCall;
        try
        {
            (newId, rowCreatedThisCall) = await _dataverse.UpsertAsync(entity, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Compose promote: upserted sprk_document {DocumentRecordId} for driveItem={DocumentSpeId} (created={Created})",
                newId, request.DocumentSpeId, rowCreatedThisCall);
        }
        catch (InvalidOperationException ex)
        {
            // The graphItemId upsert is atomic, so the classic same-SPE-item race is already closed. This
            // catch now handles the SECONDARY race the upsert CANNOT: two truly-concurrent FIRST saves of
            // the same transient draft each mint their OWN SPE item (DIFFERENT graphitemid) but carry the
            // SAME transient key — the loser's upsert-create then fails the sprk_composetransientkey_uk
            // unique constraint. Re-resolve by graphItemId (defensive) then transientKey to land the loser
            // on the winner's record → ONE record (the loser's minted item is orphaned, an acceptable rare
            // edge — never a duplicate ROW).
            _logger.LogWarning(ex,
                "Compose promote: upsert failed for driveItem={DocumentSpeId} — likely a concurrent same-transientKey first-save. Re-resolving via alternate key (graphItemId, then transientKey).",
                request.DocumentSpeId);

            Guid? raceWinnerId = (await TryFindDocumentByGraphItemIdAsync(request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false))?.Id;

            if (!raceWinnerId.HasValue && !string.IsNullOrWhiteSpace(request.TransientKey))
            {
                var transientKeyWinner = await TryFindDocumentByTransientKeyAsync(request.TransientKey!, cancellationToken)
                    .ConfigureAwait(false);
                raceWinnerId = transientKeyWinner?.RecordId;
            }

            if (!raceWinnerId.HasValue)
            {
                throw;
            }

            newId = raceWinnerId.Value;
            rowCreatedThisCall = false; // the winner created the row; this call resolved onto it
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
            // FR-07(d) (task 013): honest create-vs-update signal from the atomic upsert (false when a
            // concurrent winner created the row and this call updated/resolved onto it).
            WasCreated = rowCreatedThisCall,
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
    /// FR-S09 item 5 (r8 task 016): the record step ran and resolved no <c>sprk_document</c> id.
    /// Terminal Failed (there is no retry budget on this path), so the aggregate can never read a
    /// success for a save that produced no identity record.
    /// </summary>
    private static StoredStepSignal RecordNotResolvedSignal() => new()
    {
        StepName = StepRecord,
        StoredStatus = JobStatus.Failed,
        Started = true,
        Attempt = 1,
        MaxAttempts = 1,
        Detail = "record step resolved no sprk_document id",
    };

    /// <summary>
    /// FR-S09 item 5 (r8 task 016): does this <see cref="InvalidOperationException"/> describe one of the
    /// two Dataverse identity-key faults that <c>ComposeEndpoints.ExecuteSaveAsync</c> maps to an honest,
    /// administrator-actionable 409/503?
    /// </summary>
    /// <remarks>
    /// The predicate is duplicated from that catch filter ON PURPOSE, and the duplication is the point:
    /// the promote guard must let exactly those exceptions through so the endpoint handler stays live.
    /// If either side changes, the other must change with it — a single shared helper would be tidier
    /// but would hide that coupling behind an abstraction, and an endpoint handler that quietly stops
    /// being reachable is the defect this whole task exists to remove. Keep them in step.
    /// </remarks>
    private static bool IsDataverseIdentityKeyFault(InvalidOperationException ex) =>
        ex.Message.Contains("Found multiple records", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("not defined as keys", StringComparison.OrdinalIgnoreCase)
        || (ex.Message.Contains("sprk_graphitemid", StringComparison.OrdinalIgnoreCase)
            && ex.Message.Contains("Not Active", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// FR-S09 item 5 (r8 task 016): the terminal result for "the bytes are durable, the record is not".
    /// </summary>
    /// <remarks>
    /// Mirrors <c>BuildContainerFailedResult</c>'s shape — a RETURNED non-success outcome rather than a
    /// throw — because the two are the same kind of event: a save that reached a defined, reportable end
    /// state that is not success. <c>partially-recorded</c> rather than <c>storage-failed</c>: storage
    /// succeeded. Telling the user their document is gone when it is provably stored would be its own
    /// dishonest outcome, and it would invite them to retype work that already exists.
    /// </remarks>
    private static SaveComposeDocumentResult BuildRecordFailedResult(
        SaveComposeDocumentRequest request,
        string effectiveSpeId,
        string? effectiveDriveId,
        FileHandleDto saved,
        ComposeOrigin origin,
        DateTimeOffset observedAt,
        string detail)
    {
        var completion = ProjectCreateOnSaveState(
            subjectId: effectiveSpeId,
            correlationId: request.SessionId,
            containerSignal: CompletedSignal(StepContainer),
            recordSignal: new StoredStepSignal
            {
                StepName = StepRecord,
                StoredStatus = JobStatus.Failed,
                Started = true,
                Attempt = 1,
                MaxAttempts = 1,
                Detail = detail,
            },
            profileSignal: ComposeProfileDispatcher.ProfileNotAttempted("profile not attempted: record step failed"),
            indexingSignal: new StoredStepSignal { StepName = StepIndexing, StoredStatus = null, Started = false },
            observedAt: observedAt);

        return new SaveComposeDocumentResult
        {
            Outcome = ComposeSaveOutcome.PartiallyRecorded,
            DocumentSpeId = effectiveSpeId,
            DriveId = effectiveDriveId,
            SessionId = request.SessionId,
            DocumentRecordId = null,
            VersionId = saved.Id,
            ETag = saved.ETag,
            Size = saved.Size,
            WasPromotedThisSave = false,
            CompletionState = completion,
            Origin = origin,
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
            profileSignal: ComposeProfileDispatcher.ProfileNotAttempted("profile not attempted: container step failed"),
            indexingSignal: new StoredStepSignal { StepName = StepIndexing, StoredStatus = null, Started = false },
            observedAt: observedAt);

        return new SaveComposeDocumentResult
        {
            // FR-S06 (task 013): THE defect this contract exists to remove. This path RETURNS (it does
            // not throw), so the endpoint wraps it in Results.Ok — a save that wrote nothing at all
            // presented as HTTP 200, which the client rendered as "Saved ✓". The status stays 200 (the
            // create-on-save step-projection contract rides on this body), but the body now says plainly
            // that nothing was stored, and the client keys off THIS field rather than the status.
            Outcome = ComposeSaveOutcome.StorageFailed,
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

    // =========================================================================
    // FR-29 anchored annotations (task 060). See design.md §8 + ChatSession.cs
    // class-level remarks on AnchoredAnnotation for the Path-A deviation note.
    // These two methods are the ONLY read/write surface for the two Compose-domain
    // session collections — mutable partial-replace, NOT ledger writes.
    // =========================================================================

    /// <inheritdoc />
    /// <remarks>
    /// The interface member stays here and the implementation lives in
    /// <see cref="ComposeAnnotationStore"/> (task 070 cluster 6): the CONTRACT is the service's to
    /// keep, only the annotation policy moves.
    /// </remarks>
    public Task<ComposeAnnotationsState> GetComposeAnnotationsAsync(
        string tenantId,
        string sessionId,
        CancellationToken cancellationToken = default)
        => _annotations.GetAsync(tenantId, sessionId, cancellationToken);

    /// <inheritdoc />
    public Task<ComposeAnnotationsState> SaveComposeAnnotationsAsync(
        SaveComposeAnnotationsRequest request,
        CancellationToken cancellationToken = default)
        => _annotations.SaveAsync(request, cancellationToken);

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
    /// <summary>
    /// FR-C3 graduate-on-divergence (email-communication-intelligence-r2): when a subsequent Compose save
    /// routes through <see cref="PromoteIfEphemeralAsync"/>'s idempotent existing-row branch, check whether the
    /// row is a hash-linked COPY (<c>sprk_canonicaldocument</c> set) whose LIVE content has diverged from the
    /// hash it was linked at (<c>sprk_canonicalhash</c>). If so, sever the link (clear
    /// <c>sprk_canonicaldocument</c> via the <see cref="DBNull"/> clear-sentinel) and stamp the new content hash
    /// — the copy graduates to its own canonical. The row's dedup columns are already in hand from the idempotent
    /// alt-key lookup (no extra retrieve). Best-effort / non-fatal (NFR-04): every failure logs and leaves the
    /// row unchanged (re-evaluated on the next save); never fails the save. No-op when the detector is absent
    /// (bare test ctor), the drive id is unknown, or the row is a true canonical (no link to sever).
    /// </summary>
    private async Task GraduateLinkedCopyIfDivergedAsync(
        Entity existingRow,
        PromoteComposeDocumentRequest request,
        CancellationToken cancellationToken)
    {
        if (_dedupDetector is null || string.IsNullOrWhiteSpace(request.GraphDriveId))
            return;

        // Only a hash-linked COPY can graduate — a true canonical has no sprk_canonicaldocument link.
        if (existingRow.GetAttributeValue<EntityReference>(CanonicalDocumentAttribute) is null)
            return;

        try
        {
            var linkedHash = existingRow.GetAttributeValue<string>(CanonicalHashAttribute);
            var (liveHash, _) = await _dedupDetector
                .ResolveContentIdentityAsync(request.GraphDriveId!, request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false);

            // No live hash (unavailable) OR still identical → not diverged; leave the link intact.
            if (string.IsNullOrWhiteSpace(liveHash) || string.Equals(liveHash, linkedHash, StringComparison.Ordinal))
                return;

            await _dataverse.UpdateAsync(
                    DocumentLogicalName,
                    existingRow.Id,
                    new Dictionary<string, object>
                    {
                        [CanonicalDocumentAttribute] = DBNull.Value, // sever the link (DBNull clear-sentinel)
                        [CanonicalHashAttribute] = liveHash!,        // stamp the diverged content's own identity
                    },
                    cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Compose content-dedup: sprk_document {DocumentId} diverged from its linked canonical; graduated to its own document.",
                existingRow.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Compose content-dedup (graduate) failed (non-fatal) for document {DocumentId}; leaving link intact.",
                existingRow.Id);
        }
    }

    private async Task<Entity?> TryFindDocumentByGraphItemIdAsync(
        string driveItemId,
        CancellationToken cancellationToken)
    {
        var key = new KeyAttributeCollection
        {
            { GraphItemIdAttribute, driveItemId },
        };

        try
        {
            // Fetch the FR-C3 dedup columns alongside the id so the idempotent branch can evaluate
            // graduate-on-divergence WITHOUT a second Dataverse round-trip on the save hot path.
            var entity = await _dataverse.RetrieveByAlternateKeyAsync(
                DocumentLogicalName,
                key,
                new[] { DocumentIdAttribute, CanonicalDocumentAttribute, CanonicalHashAttribute },
                cancellationToken).ConfigureAwait(false);

            return entity;
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "Compose promote alt-key lookup threw InvalidOperationException for driveItem={DocumentSpeId} — treating as not-found",
                driveItemId);
            return null;
        }
    }

    /// <summary>
    /// G7 (FR-06, task 022): the resolved dedup identity for a transient key — the <c>sprk_document</c> row id
    /// plus the SPE pointer (<c>sprk_graphitemid</c> + <c>sprk_graphdriveid</c>) needed to REPLACE its content
    /// in place instead of minting a duplicate. <see cref="SpeId"/>/<see cref="DriveId"/> are <c>null</c> only
    /// for a row that somehow carries a transient key but no SPE pointer (a G7-created row always has both) —
    /// the caller then falls back to minting.
    /// </summary>
    private sealed record TransientKeyMatch(Guid RecordId, string? SpeId, string? DriveId);

    /// <summary>
    /// G7 (FR-06, task 022): looks up an existing <c>sprk_document</c> row by the client-minted transient key
    /// via the <c>sprk_composetransientkey_uk</c> alternate key, returning its id + SPE pointer (so the caller
    /// can replace in place). Returns <c>null</c> when no row carries the key (the first save of this draft,
    /// or a Save-New fork). Resolves by KEY, never by content (I-7/NFR-02). Mirrors
    /// <see cref="TryFindDocumentByGraphItemIdAsync"/> exactly (same best-effort not-found on a thrown
    /// InvalidOperationException).
    /// </summary>
    private async Task<TransientKeyMatch?> TryFindDocumentByTransientKeyAsync(
        string transientKey,
        CancellationToken cancellationToken)
    {
        var key = new KeyAttributeCollection
        {
            { ComposeTransientKeyAttribute, transientKey },
        };

        try
        {
            var entity = await _dataverse.RetrieveByAlternateKeyAsync(
                DocumentLogicalName,
                key,
                new[] { DocumentIdAttribute, GraphItemIdAttribute, GraphDriveIdAttribute },
                cancellationToken).ConfigureAwait(false);

            if (entity is null)
            {
                return null;
            }

            var speId = entity.Contains(GraphItemIdAttribute) ? entity[GraphItemIdAttribute] as string : null;
            var driveId = entity.Contains(GraphDriveIdAttribute) ? entity[GraphDriveIdAttribute] as string : null;
            return new TransientKeyMatch(entity.Id, speId, driveId);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogDebug(ex,
                "Compose transient-key alt-key lookup threw InvalidOperationException for transientKey — treating as not-found");
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
