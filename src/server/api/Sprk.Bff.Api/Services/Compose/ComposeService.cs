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
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Auth;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Exceptions;
using Sprk.Bff.Api.Infrastructure.ExternalAccess;
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
    internal const string DocumentLogicalName = "sprk_document";
    internal const string DocumentIdAttribute = "sprk_documentid";
    internal const string GraphItemIdAttribute = "sprk_graphitemid";
    internal const string DisplayNameAttribute = "sprk_documentname";
    internal const string FileNameAttribute = "sprk_filename";
    // SPE-pointer + file-metadata columns — logical names mirrored from the canonical
    // OfficeDocumentPersistence.CreateDocumentWithSpePointersAsync write (Services/Office),
    // which maps through Spaarke.Dataverse UpdateDocumentRequest → DataverseWebApiService.
    // WITHOUT these, every downstream reader (open-links, preview) validates the SPE pointer,
    // finds drive-id empty + sprk_hasfile false, and 409s "No file is attached to this document".
    internal const string GraphDriveIdAttribute = "sprk_graphdriveid";
    internal const string HasFileAttribute = "sprk_hasfile";
    internal const string FileSizeAttribute = "sprk_filesize";
    internal const string MimeTypeAttribute = "sprk_mimetype";
    internal const string FilePathAttribute = "sprk_filepath";
    // G1 (FR-01, task 020): the durable cross-session authored-vs-imported origin marker (owner-created
    // choice field; notes/g1-origin-field-asbuilt.md). Written ONLY at create-on-save
    // (PromoteIfEphemeralAsync) and read on Path A loads (LoadAsync) — see ComposeOrigin remarks for the
    // AS-BUILT integer values + BINDING null-handling contract.
    internal const string ComposeOriginAttribute = "sprk_composeorigin";
    // G7 (FR-06, task 022): the client-minted transient dedup key (owner-created Single-line-text column +
    // single-column alt-key sprk_composetransientkey_uk; notes/g7-transient-key-schema.md). Stamped ONLY at
    // create-on-save (PromoteIfEphemeralAsync). Resolved via the alt-key in TryFindDocumentByTransientKeyAsync
    // BEFORE minting a transient SPE item, so repeated create-on-save calls with the same key replace one
    // record in place instead of minting duplicates (the 8-duplicate defect). Resolve by KEY, never by
    // content (I-7/NFR-02).
    internal const string ComposeTransientKeyAttribute = "sprk_composetransientkey";
    // FR-C3 (email-communication-intelligence-r2, graduate-on-divergence): the SPE content identity
    // (quickXorHash, task 023 indexed column) + the self-referential canonical link. A create-on-save
    // stamps sprk_canonicalhash; on a byte-identical hit it also LINKS via sprk_canonicaldocument (this
    // editable copy is byte-identical NOW). The link is CLEARED the moment content diverges (first edit),
    // graduating the copy to its own canonical — see the create + idempotent branches of
    // PromoteIfEphemeralAsync. Distinct from sprk_parentdocument (attachment→parent-email).
    internal const string CanonicalHashAttribute = "sprk_canonicalhash";
    internal const string CanonicalDocumentAttribute = "sprk_canonicaldocument";

    // Task 041 B-MED-3 (option C): the sprk_document record-link lookup vocabulary (ADR-024 — the
    // SAME closed set AttachmentDocumentAssociationRung follows, type-agnostic by design). A
    // PDF-sourced create-on-save copies every non-empty lookup from the source PDF's record onto the
    // new Word document's record so the two file side-by-side under the same matter/project/….
    internal static readonly string[] DocumentAssociationLookupAttributes =
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

    internal const string ComposeCreateOnSaveJobType = "compose-create-on-save";
    internal const string DocxContentType =
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    private readonly ISpeFileOperations _spe;
    private readonly ChatSessionManager _sessions;
    private readonly IGenericEntityService _dataverse;
    private readonly IPostUploadIndexingEnqueuer _indexing;
    private readonly ILogger<ComposeService> _logger;

    /// <summary>
    /// Task 076/085's container resolver — the ONE place a storage container is chosen (issue #858).
    /// </summary>
    private readonly RecordContainerResolver _containerResolver;

    /// <summary>
    /// Per-record caller rights, OBO, fail-closed (issue #858). The create-on-save path had no
    /// per-resource authorization at all: the Compose route group carries a bare
    /// <c>RequireAuthorization()</c>, which asks only "are you anyone?".
    /// </summary>
    private readonly CallerRecordAccessProbe _accessProbe;
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

    /// <summary>Task 070 cluster 5a — the G10 re-profiling policy (storm guard + manual leg).</summary>
    private readonly ComposeProfileRetriggerGuard _profileRetrigger;
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
    // Task 070 cluster 1: mis-anchored-save recovery (stale-base re-anchor + prong-1 per-paragraph
    // best-effort). SaveAsync decides WHETHER recovery is needed; this decides what recovery does.
    private readonly ComposeReanchorCoordinator _reanchorCoordinator;
    // Task 070 cluster 3: the storage boundary of a save — which bytes it starts from, under what
    // precondition it writes, and the version stamp that makes the NEXT save's staleness detectable.
    private readonly ComposeSaveStorageCoordinator _saveStorage;

    /// <summary>Task 070 cluster 2b — which `sprk_document` row an external identifier denotes.</summary>
    private readonly ComposeRecordResolution _recordResolution;

    /// <summary>Task 070 cluster 2a — draft-to-record promotion + create-on-save outcome shaping.</summary>
    private readonly ComposeCreateOnSavePromoter _createOnSave;
    // Task 070 cluster 4: how a PDF becomes an editable document, and how that origin is remembered.
    private readonly ComposePdfIntakeCoordinator _pdfIntake;

    public ComposeService(
        ISpeFileOperations spe,
        ChatSessionManager sessions,
        IGenericEntityService dataverse,
        IPostUploadIndexingEnqueuer indexing,
        ILogger<ComposeService> logger,
        RecordContainerResolver containerResolver,
        CallerRecordAccessProbe accessProbe,
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
        _containerResolver = containerResolver ?? throw new ArgumentNullException(nameof(containerResolver));
        _accessProbe = accessProbe ?? throw new ArgumentNullException(nameof(accessProbe));
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
        // Cluster 5a wraps 5b: the guard decides WHETHER to re-profile, the dispatcher does it.
        _profileRetrigger = new ComposeProfileRetriggerGuard(cache, _profileDispatcher, _logger);
        // FR-C3 (email-communication-intelligence-r2): null in a bare test constructor (dedup hook = no-op),
        // the real scoped detector in every non-test host.
        _dedupDetector = dedupDetector;
        // Task 070 cluster 2b — record RESOLUTION. Constructed after _dedupDetector because it takes it.
        _recordResolution = new ComposeRecordResolution(_sessions, _dataverse, _logger, _dedupDetector);
        // Cluster 2a takes 2b: the promotion path resolves an existing row before creating one.
        _createOnSave = new ComposeCreateOnSavePromoter(_dataverse, _logger, _dedupDetector, _recordResolution);
        // FR-08 (task 050): ADR-009 Redis when present in every non-test host, null (no staleness
        // re-anchor) in a bare test constructor.
        _cache = cache;
        _reanchorService = cache is not null ? new AnnotationReanchorService(cache) : null;
        // Constructed LAST of the collaborators: it needs _reanchorService, which is itself gated on
        // the cache assigned immediately above.
        _reanchorCoordinator = new ComposeReanchorCoordinator(
            _spe, _patchEngine, _baselineParaIdStamper, _reanchorService, _logger);
        _saveStorage = new ComposeSaveStorageCoordinator(_spe, _documentRenderer, _cache, _logger);
        _pdfIntake = new ComposePdfIntakeCoordinator(
            _pdfIntakeSource, _pdfModelProjector, _documentRenderer, _spe, _cache, _logger);
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
        if (ComposePdfIntakeCoordinator.IsPdfSource(fileName, content.Span))
        {
            sourceFormat = "pdf";
            (content, pdfIntakeWarnings) = await _pdfIntake.ProjectPdfToDocxAsync(
                    content, fileName ?? "(compose-mount)", driveId: "(mount)", documentSpeId: "(mount)", cancellationToken)
                .ConfigureAwait(false);

            // FR-A08 (task 044): the same server-determined "this was a PDF" carry LoadAsync makes, for the
            // mount doors. There are no SOURCE drive-item coordinates here — an uploaded or browsed file has
            // no SPE item to re-open — so this marker serves the STAMP only; the FR-A09 derived-document
            // mapping needs a durable source pointer and is correctly skipped. Session-less callers (the
            // stateless Browse door) record nothing, which is the documented residual.
            if (!string.IsNullOrWhiteSpace(sessionId))
            {
                await _pdfIntake.SetPdfSourceMarkerAsync(sessionId!, driveId: string.Empty, speId: string.Empty, cancellationToken)
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

    /// <summary>
    /// DRIVE PROVENANCE (#858 family, 2026-09-01): resolves the drive a write into an EXISTING drive item
    /// must target — the drive RECORDED on the <c>sprk_document</c> row, not the one the caller named.
    /// </summary>
    /// <remarks>
    /// <para><b>What was wrong.</b> Both write paths into an existing item (<c>ApplyTemplateAsync</c>'s route
    /// parameter and <c>SaveAsync</c>'s <c>request.DriveId</c>) took the drive from the CALLER while the
    /// authorized row already held <c>sprk_graphdriveid</c>. The server never consulted it, so the record
    /// could claim one location while the bytes went to another.</para>
    /// <para><b>What this is NOT.</b> Not the app-only container hole this codebase's other
    /// <c>ClientSupplied</c> sinks describe. Compose writes are OBO — SPE authorizes them as the acting
    /// user, so no caller reaches a drive they could not already reach. The defect is that the record and
    /// the bytes could DIVERGE; the audit trail, not the ACL, is what was unsound.</para>
    /// <para><b>The fallback is deliberate, and it is not a half-measure.</b> When the row has no drive id
    /// the caller's value is used, logged. Legacy rows predating the full-SPE-pointer stamp exist — see
    /// <c>PromoteIfEphemeralAsync</c>, which documents that a row without the pointer makes downstream
    /// readers 409 "No file is attached" — so a hard fail-closed here would break saves on real documents
    /// to close a hole that OBO already closes. An attacker cannot make a row's drive id DISAPPEAR, so the
    /// fallback covers legacy data, not an attack path. When the row DOES have a drive id it wins
    /// unconditionally and a divergence is logged at Warning: that divergence is the signal this method
    /// exists to produce.</para>
    /// <para><b>Cost.</b> One keyed Dataverse retrieve per replace-path write, on a path that already does a
    /// Graph metadata read and a cache read. The save's promote step resolves the same row AFTER the write;
    /// this is not folded into that call because the value is needed BEFORE it — the point is to write to
    /// the right place, which is a decision that cannot be made after the write.</para>
    /// </remarks>
    private async Task<string?> ResolveAuthoritativeDriveIdAsync(
        string documentSpeId,
        string? requestedDriveId,
        string operation,
        CancellationToken cancellationToken)
    {
        string? recorded;
        try
        {
            recorded = await _recordResolution.TryResolveRecordedDriveIdAsync(documentSpeId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best-effort: a provenance READ must never be the reason a user's save fails. Degrading to
            // the caller's value reproduces the pre-fix behaviour exactly, loudly.
            _logger.LogWarning(ex,
                "Compose {Operation}: drive-provenance lookup failed for driveItem={DocumentSpeId}; " +
                "falling back to the caller-supplied drive={RequestedDriveId}.",
                operation, documentSpeId, requestedDriveId);
            return requestedDriveId;
        }

        if (string.IsNullOrWhiteSpace(recorded))
        {
            _logger.LogDebug(
                "Compose {Operation}: no sprk_document row records a drive for driveItem={DocumentSpeId}; " +
                "using the caller-supplied drive={RequestedDriveId}.",
                operation, documentSpeId, requestedDriveId);
            return requestedDriveId;
        }

        if (!string.IsNullOrWhiteSpace(requestedDriveId)
            && !string.Equals(recorded, requestedDriveId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Compose {Operation}: the caller named drive={RequestedDriveId} for driveItem={DocumentSpeId} " +
                "but sprk_document records drive={RecordedDriveId}. Writing to the RECORDED drive — the " +
                "record is the authority on where its own bytes live.",
                operation, requestedDriveId, documentSpeId, recorded);
        }

        return recorded;
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
        string requestedDriveId,
        string documentSpeId,
        byte[] resolvedTemplateBytes,
        string templateName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestedDriveId))
            throw new ArgumentException("DriveId is required for SPE drive-item access.", nameof(requestedDriveId));
        if (string.IsNullOrWhiteSpace(documentSpeId))
            throw new ArgumentException("DocumentSpeId (drive-item id) is required.", nameof(documentSpeId));
        ArgumentNullException.ThrowIfNull(resolvedTemplateBytes);
        if (resolvedTemplateBytes.Length == 0)
            throw new ArgumentException("Resolved template bytes must not be empty.", nameof(resolvedTemplateBytes));

        // DRIVE PROVENANCE (#858 family): the route names a drive; the sprk_document row KNOWS one. From
        // here down `driveId` is the authoritative value, so the read (metadata + download) and the write
        // (the preconditioned replace) all address the same drive the record claims — apply-template is a
        // read-merge-write, and reading from one drive while writing to another is the sharpest form of
        // the divergence this closes. The parameter is renamed rather than shadowed so no later edit can
        // reach the caller's claim by accident.
        var driveId = await ResolveAuthoritativeDriveIdAsync(
                documentSpeId, requestedDriveId, "apply-template", cancellationToken)
            .ConfigureAwait(false) ?? requestedDriveId;

        _logger.LogInformation(
            "Compose apply-template: drive={DriveId} driveItem={DocumentSpeId} template={TemplateName}",
            driveId, documentSpeId, templateName);

        // 1) Read the CURRENT version stamp BEFORE the download — this is T1, the version the merge
        //    below is computed against, and it is what the write at step 4 asserts (#776).
        //
        //    WHY IT IS CAPTURED HERE AND NOT JUST BEFORE THE WRITE. Apply-template is a
        //    read-merge-write over bytes we downloaded: if another writer lands a version between T1 and
        //    T2, our merged output was computed WITHOUT their change and writing it would erase them at
        //    the head version. Reading the eTag immediately before the write would assert against that
        //    NEWER version and succeed — clobbering silently, which is exactly the defect. The
        //    precondition is only meaningful as of the bytes we actually merged.
        //
        //    This is NOT the client's load-time eTag. Sending that would refuse on every stale mount and
        //    re-create the 422 treadmill R4 removed (see the SaveAsync note on `preWriteETag`). The
        //    window asserted here is OUR OWN read→write span, so a refusal means a genuine concurrent
        //    writer, not a stale client.
        //
        //    A null stamp (metadata unavailable) degrades to the pre-#776 blind PUT rather than blocking
        //    the merge — best-effort, same convention as the save path.
        var preMergeMetadata = await _spe.GetFileMetadataAsUserAsync(httpContext, driveId, documentSpeId, cancellationToken)
            .ConfigureAwait(false);
        var preMergeETag = preMergeMetadata?.ETag;

        // Download the CURRENT persisted bytes (the merge applies to the SAVED document — the client
        // guards apply on a non-dirty, non-transient mount). Mirrors LoadAsync's buffered fetch.
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

        // 4) Persist as a NEW SPE version, ASSERTING the T1 version captured at step 1 (#776). The prior
        //    version remains retrievable through SPE version history (FR-07 safety net).
        //
        //    Reuses the save path's `ReplaceWithPreconditionAsync` rather than adding a second
        //    precondition idiom (root §11): it already maps a Graph 412 to the typed
        //    EtagPreconditionFailedException, so the Graph type never crosses the facade (ADR-007), and
        //    it already degrades to a blind PUT on a null stamp. A 412 here means a sibling tab saved
        //    while this merge was in flight — the caller is told to re-apply, which is honest and
        //    actionable. The alternative was writing anyway and discarding their save with no way to
        //    reconcile it, since the merged bytes never contained their change.
        //    `rebaseOnConflict: false` is load-bearing, not a stylistic choice. The default retries once
        //    against the fresh version (last-writer-wins), which is sound on the SAVE path only because
        //    the edits were rebased onto those bytes first. Nothing rebases the merge here, so retrying
        //    would write a payload that never contained the other writer's change — the If-Match would
        //    be decorative and the defect would survive the fix.
        var replaced = await _saveStorage.ReplaceWithPreconditionAsync(
                httpContext, driveId, documentSpeId, finalBytes, preMergeETag, cancellationToken,
                rebaseOnConflict: false)
            .ConfigureAwait(false);

        if (replaced is null || string.IsNullOrEmpty(replaced.Id))
        {
            throw new InvalidOperationException(
                $"SPE apply-template failed: drive-item not found or version not returned. drive={driveId} item={documentSpeId}");
        }

        // FR-08 alignment: stamp the just-written version as the next save's staleness assert-baseline
        // (best-effort — mirrors SaveAsync's post-write stamp; a Redis miss never fails the merge).
        await _saveStorage.SetSaveVersionStampAsync(documentSpeId, replaced.ETag, DateTimeOffset.UtcNow, cancellationToken)
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
        if (ComposePdfIntakeCoordinator.IsPdfSource(metadata.Name, content.Span))
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
                var derived = await _pdfIntake.ResolvePdfDerivedDocumentAsync(
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
            (content, pdfIntakeWarnings) = await _pdfIntake.ProjectPdfToDocxAsync(
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
            await _pdfIntake.SetPdfSourceMarkerAsync(
                    session.SessionId, request.DriveId, request.DocumentSpeId, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            await _pdfIntake.ClearPdfSourceMarkerAsync(session.SessionId, cancellationToken).ConfigureAwait(false);
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
            await _profileRetrigger.MaybeRetriggerProfileOnLoadAsync(
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

    /// <summary>
    /// The Dataverse LOGICAL name of the only entity a Compose session can be bound to.
    /// </summary>
    /// <remarks>
    /// <see cref="BuildMatterHostContext"/> is the ONLY producer of a Compose session's
    /// <see cref="ChatHostContext"/> and it hard-codes
    /// <c>ParentEntityContext.EntityTypes.Matter</c> ("matter"), so the reachable set is exactly one
    /// entity. That is why <see cref="ResolveCreateOnSaveContainerAsync"/> maps the short name to a
    /// logical name with a single constant instead of a lookup table: the codebase already carries three
    /// short/logical → entity-set maps and CLAUDE.md §11 puts a fourth over the line. A table of one row
    /// for types that cannot occur would be that fourth map. A host context of any OTHER type is refused
    /// rather than guessed, which is what makes a future project-bound session visible instead of silent.
    /// </remarks>
    private const string ComposeHostEntityLogicalName = "sprk_matter";

    /// <summary>
    /// Issue #858 — choose the storage container for a create-on-save draft, SERVER-SIDE, and authorize
    /// the caller against the record that choice comes from.
    /// </summary>
    /// <remarks>
    /// <para><b>What this replaces.</b> The transient-create branch used
    /// <c>request.ContainerId</c> — an SPE container id supplied in the request BODY. The server wrote
    /// bytes into whatever container the caller named, having authorized the caller against nothing at
    /// all: the <c>/api/compose</c> group carries a bare <c>RequireAuthorization()</c>, which asks only
    /// "are you anyone?". Same defect class as task 073 (deleted), 076 (converted) and 085 (Office
    /// save).</para>
    ///
    /// <para><b>Why the session and not the request.</b> Threading the owning record through the SAVE
    /// request — which is what issue #858 proposed — would have relocated the defect rather than removed
    /// it: the caller would name a matter instead of a container and the server would resolve it. The
    /// record identity is taken from SERVER-SIDE session state instead, and then authorized, so the
    /// authorization key and the write destination are one value by construction.</para>
    ///
    /// <para><b>Session ownership is checked first</b>, mirroring <see cref="LoadAsync"/>'s issue #863
    /// test. Without it, supplying someone else's <c>SessionId</c> would let a caller borrow their
    /// matter binding — and the whole point of reading identity from the session is that the session is
    /// trustworthy.</para>
    ///
    /// <para><b>No host context → the acting user's business unit</b>, server-derived (task 2a). A
    /// matter-less draft is a DESIGNED flow, not an edge case: <c>composeEditor.registration.ts</c>
    /// opens the workspace on its empty state when no document context is supplied. Refusing it would
    /// break a shipped capability, and keeping <c>ContainerId</c> "just for that path" is option (B)
    /// through the back door — the client decides whether a matter is bound, so "omit the matter" would
    /// have become a supported route to naming your own container.</para>
    ///
    /// <para>🔴 <b>Residual, unchanged by this patch</b>: a draft that starts matter-less lands in a
    /// business-unit container, and if it is LATER associated to a secure record the bytes are already
    /// there — SPE permissions are additive-only. See
    /// <c>notes/finding-secure-transition-container-migration.md</c>.</para>
    /// </remarks>
    /// <returns>
    /// The resolved container id, or <see langword="null"/> when no container is CONFIGURED for the
    /// caller / record.
    /// </returns>
    /// <remarks>
    /// <para><b>Null vs throw is a deliberate split.</b> A missing container is a CONFIGURATION state
    /// (a business unit with no <c>sprk_containerid</c>, which is common — three of six verified live),
    /// and the caller turns it into <c>BuildContainerFailedResult</c> so the client keeps the per-step
    /// projection it already renders. An authorization DENIAL, an unsupported host entity, or an
    /// unattributable caller throw instead: those are answers about the caller, not about
    /// configuration, and they must reach the client as 403/409 rather than as a save step that
    /// "didn't work". Both outcomes write nothing.</para>
    /// </remarks>
    private async Task<string?> ResolveCreateOnSaveContainerAsync(
        SaveComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var callerOid = CallerResolution.ResolveObjectId(httpContext.User);

        if (string.IsNullOrWhiteSpace(callerOid))
        {
            // Mirrors LoadAsync: a caller with no Entra oid cannot be attributed, and an unattributable
            // caller must not pick storage.
            throw new UnauthorizedAccessException(
                "Compose create-on-save: the caller carries no Entra oid, so the storage container cannot "
                + "be attributed to a principal.");
        }

        // SessionId is OPTIONAL on the create-on-save path (task 110): a Browse/local-file first Save
        // has no chat session and the endpoint forwards SessionId = "". The session store's id guard
        // (TenantCache) throws ArgumentException("Id must be a non-empty string") on an empty id, which
        // the save route maps to a 400 — i.e. an unconditional lookup here turned the DESIGNED
        // session-less flow into a request rejection. Verified on the wire 2026-09-01 (8 seam/contract
        // tests, e.g. CreateOnSave_WithEmptySessionId_Returns200AndPersistsDocumentWithoutRebind). No
        // session means no host context, so the acting-user branch below is the correct — and only —
        // derivation for it.
        var session = string.IsNullOrWhiteSpace(request.SessionId)
            ? null
            : await _sessions
                .GetSessionAsync(request.TenantId, request.SessionId, cancellationToken)
                .ConfigureAwait(false);

        // Issue #863's ownership test, applied here for the same reason: an unowned or foreign session is
        // not a trustworthy source of the record identity this method is about to authorize against.
        var sessionIsOwnedByCaller =
            session is not null
            && string.Equals(session.OwnerOid, callerOid, StringComparison.Ordinal);

        var hostContext = sessionIsOwnedByCaller ? session!.HostContext : null;

        if (session is not null && !sessionIsOwnedByCaller)
        {
            _logger.LogWarning(
                "Compose create-on-save: session {SessionId} (tenant={TenantId}) is not owned by the "
                + "caller — its host context is IGNORED for container selection, falling back to the "
                + "caller's own business unit.",
                request.SessionId, request.TenantId);
        }

        if (hostContext is null || string.IsNullOrWhiteSpace(hostContext.EntityId))
        {
            // Matter-less draft — the designed empty-state flow. Server-derived from the caller.
            var actingUserDecision = await _containerResolver
                .ResolveForActingUserAsync(callerOid, cancellationToken)
                .ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(actingUserDecision.ContainerId))
            {
                _logger.LogWarning(
                    "Compose create-on-save: no matter bound to session {SessionId} and the acting user's "
                    + "business unit has no container stamped — failing the '{Step}' step honestly.",
                    request.SessionId, StepContainer);
                return null;
            }

            _logger.LogInformation(
                "Compose create-on-save: no matter bound to session {SessionId}; container derived from "
                + "the acting user's business unit (outcome={Outcome}).",
                request.SessionId, actingUserDecision.Outcome);

            return actingUserDecision.ContainerId!;
        }

        // A host context of an unexpected type is refused, not guessed — see
        // ComposeHostEntityLogicalName's remarks.
        if (!string.Equals(
                hostContext.EntityType, ParentEntityContext.EntityTypes.Matter, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError(
                "Compose create-on-save: session {SessionId} is bound to host entity type "
                + "'{EntityType}', which this path cannot authorize. Refusing.",
                request.SessionId, hostContext.EntityType);

            throw new SdapProblemException(
                code: "compose_host_entity_unsupported",
                title: "Cannot determine where to save this document",
                detail: $"This draft is bound to a '{hostContext.EntityType}', which is not a supported "
                        + "save target. Refusing rather than choosing a storage location that has not been "
                        + "authorized.",
                statusCode: 409);
        }

        if (!Guid.TryParse(hostContext.EntityId, out var recordId) || recordId == Guid.Empty)
        {
            throw new SdapProblemException(
                code: "compose_host_record_invalid",
                title: "Cannot determine where to save this document",
                detail: "The matter this draft is bound to could not be identified, so its storage "
                        + "location cannot be resolved. Refusing rather than using a shared container.",
                statusCode: 409);
        }

        // ── AUTHORIZE the record the container will come from ──────────────────────────────────────
        // Without this the patch would only MOVE the primitive: the matter id reaches the session from
        // LoadComposeDocumentRequest.MatterId, which is client-supplied and never authorized, so a caller
        // could bind their own session to any matter and receive that matter's container.
        if (!EntityAccessFilter.TryResolveEntitySet(ComposeHostEntityLogicalName, out var entitySet))
        {
            throw new SdapProblemException(
                code: "compose_host_entity_unsupported",
                title: "Cannot determine where to save this document",
                detail: "This draft's owning record type cannot be authorized, so the save is refused.",
                statusCode: 409);
        }

        var rights = await _accessProbe
            .GetCallerRightsAsync(
                TokenHelper.ExtractBearerTokenOrNull(httpContext), entitySet, recordId, cancellationToken)
            .ConfigureAwait(false);

        // The SAME operation key the Office save path uses for the same act — attaching a document to a
        // record. One policy decides what that costs (Dataverse AppendTo); this adds no second vocabulary.
        if (!OperationAccessPolicy.HasRequiredRights(rights, "entity.associate_document"))
        {
            _logger.LogWarning(
                "Compose create-on-save DENIED: caller cannot attach documents to {EntitySet}({RecordId}). "
                + "Holds {Rights}; requires {Required}. (session={SessionId})",
                entitySet, recordId, rights,
                OperationAccessPolicy.GetRequiredRights("entity.associate_document"), request.SessionId);

            throw new SdapProblemException(
                code: "compose_record_access_denied",
                title: "You cannot save a document to this matter",
                detail: "You do not have permission to file documents against this matter. Filing a "
                        + "document to a record requires the \"Append To\" permission on it, and your "
                        + "security role does not currently grant that. Ask an administrator to grant "
                        + "Append To for this record type, or ask the matter's owner to share it with you.",
                statusCode: 403);
        }

        var decision = await _containerResolver
            .ResolveForRecordAsync(ComposeHostEntityLogicalName, recordId, cancellationToken)
            .ConfigureAwait(false);

        if (decision.Outcome == ContainerDecisionOutcome.FailClosed)
        {
            // The resolver already threw for this case; kept as a defensive branch so a future resolver
            // change that RETURNS FailClosed cannot silently fall through to a shared container.
            throw new SdapProblemException(
                code: "secure_record_container_missing",
                title: "Secure matter has no storage container",
                detail: "This matter is marked secure but has no container of its own, so its content "
                        + "cannot be stored in a shared container. Provision the matter's container first.",
                statusCode: 409);
        }

        if (string.IsNullOrWhiteSpace(decision.ContainerId))
        {
            // Configuration, not authorization — the caller HAS access to the matter; the matter's
            // business unit simply has no container. Structured step failure, same as above.
            _logger.LogWarning(
                "Compose create-on-save: matter {RecordId} resolved to no container (outcome={Outcome}) "
                + "— failing the '{Step}' step honestly.",
                recordId, decision.Outcome, StepContainer);
            return null;
        }

        _logger.LogInformation(
            "Compose create-on-save: container derived from the AUTHORIZED matter {RecordId} "
            + "(outcome={Outcome}, session={SessionId}).",
            recordId, decision.Outcome, request.SessionId);

        return decision.ContainerId!;
    }

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

        // ────────────────────────────────────────────────────────────────────────────
        // DRIVE PROVENANCE (#858 family, 2026-09-01) — resolved ONCE, HERE, and folded back onto the
        // request so every downstream consumer inherits it: the baseline re-fetch below, the pre-write
        // metadata read + PDF guard, the stale-base re-anchor's own download, and the preconditioned
        // replace. Rewriting the request rather than threading a second drive parameter through five
        // collaborators is deliberate — a threaded parameter is a site a future edit can forget, and the
        // one property that has to hold is that NO site on this path can still reach the caller's claim.
        //
        // The transient-create branch is untouched by design: it has no drive item yet and its drive comes
        // from the SERVER-derived container (#858), which is already provenance-correct.
        // ────────────────────────────────────────────────────────────────────────────
        if (!isTransientCreate)
        {
            var authoritativeDriveId = await ResolveAuthoritativeDriveIdAsync(
                    request.DocumentSpeId!, request.DriveId, "save", cancellationToken)
                .ConfigureAwait(false);

            if (!string.Equals(authoritativeDriveId, request.DriveId, StringComparison.Ordinal))
            {
                request = request with { DriveId = authoritativeDriveId };
            }
        }

        (byte[] contentToPersist, var renderDegradationWarnings) = await _saveStorage.ResolveSaveBaselineAsync(request, httpContext, cancellationToken)
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
            && !ComposeSaveStorageCoordinator.HasBaselineVersionCoordinates(request)
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
        var pdfSource = await _pdfIntake.GetPdfSourceMarkerAsync(request.SessionId, cancellationToken).ConfigureAwait(false);
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
            var saveStamp = await _saveStorage.GetSaveVersionStampAsync(request.DocumentSpeId!, cancellationToken)
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
                    var (patchedBytes, summary) = await _reanchorCoordinator.ReanchorStaleSaveAsync(
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
            catch (ComposePatchException ex) when (!ComposeReanchorCoordinator.IsBatchLevelPatchRefusal(ex.Kind))
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

                var bestEffortBytes = _reanchorCoordinator.ApplyBestEffortByParagraph(
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

        // spaarkeai-compose-r8 (UAT item 8): the "Include document revision report" appendix — the
        // plain-language "we made these edits, here is what they do" memo, appended as real body content
        // so it prints and survives to PDF (metadata would not). Same shipped AppendSection path as the
        // Summary Page above and placed immediately after it, so when a save carries both, the ordering is
        // deterministic rather than incidental. Pure + deterministic, no second LLM call.
        //
        // The generator returns EMPTY when there is nothing to report, and appending then would leave a
        // heading with nothing under it — the document-shaped version of the phantom change the client
        // producer refuses to dispatch. So the emptiness is checked here, not assumed away.
        if (request.RevisionReport is not null)
        {
            var reportBlocks = ComposeRevisionReportGenerator.Build(request.RevisionReport);
            if (reportBlocks.Count > 0)
            {
                contentToPersist = _documentRenderer.AppendSection(contentToPersist, reportBlocks);

                _logger.LogInformation(
                    "Compose save: appended Document Revision Report ({ChangeCount} itemised change(s)) to the document (session={SessionId}).",
                    request.RevisionReport.Changes?.Count ?? 0, request.SessionId);
            }
            else
            {
                _logger.LogInformation(
                    "Compose save: Document Revision Report requested but the ledgered result carried nothing to report — nothing appended (session={SessionId}).",
                    request.SessionId);
            }
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
            // container= dropped with SaveComposeDocumentRequest.ContainerId (issue #858). The container
            // is no longer an INPUT to log; the chosen one is logged by ResolveCreateOnSaveContainerAsync
            // at the point of the decision, alongside which record it was derived from.
            "Compose save: tenant={TenantId} drive={DriveId} driveItem={DocumentSpeId} transientCreate={IsTransientCreate} contentModel={HasContentModel} comments={CommentCount} session={SessionId} record={DocumentRecordId} size={SizeBytes}",
            request.TenantId, request.DriveId, request.DocumentSpeId,
            isTransientCreate, request.ContentModel is not null, request.Comments?.Count ?? 0,
            request.SessionId, request.DocumentRecordId, request.Content.Length);

        // ────────────────────────────────────────────────────────────────────────────
        // STEP 1 — container (FR-05, Fork A + Fork B).
        //   Transient draft (no DocumentSpeId): the container id is SERVER-DERIVED by
        //   ResolveCreateOnSaveContainerAsync (#858 — the caller cannot name it; the session-bound
        //   matter is authorized first, else the acting user's BU supplies it); create the SPE
        //   drive-item in it under OBO (Fork B). An UNRESOLVABLE container FAILS the container step
        //   honestly — never a success, and never a guessed container.
        //   Existing item (DocumentSpeId present): replace the drive-item's content (R1 behavior).
        // ────────────────────────────────────────────────────────────────────────────
        string effectiveSpeId;
        string? effectiveDriveId;
        FileHandleDto saved;
        var fileName = ComposeCreateOnSavePromoter.ResolveFileName(request.DisplayName);

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
            ComposeRecordResolution.TransientKeyMatch? dedupMatch = null;
            if (!request.ForkNew && !string.IsNullOrWhiteSpace(request.TransientKey))
            {
                dedupMatch = await _recordResolution.TryFindDocumentByTransientKeyAsync(request.TransientKey!, cancellationToken)
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
                // ══ SERVER-DERIVED CONTAINER (issue #858) ═══════════════════════════════════════════
                // Was: `request.ContainerId`, an SPE container id supplied in the request BODY, written
                // into with no per-resource authorization of any kind. Now the container comes from the
                // matter bound to this session — SERVER-SIDE state — and only after the caller has been
                // authorized against that matter. The authorization key and the write destination are one
                // value by construction.
                //
                // The old guard here logged "No server-side BU→container resolver (multi-container
                // INV-7)" and failed the container step. BOTH halves of that were false by the time it
                // was read: RecordContainerResolver exists (task 075/076) with nine consumers, and INV-7
                // PRESCRIBES server-side resolution (record's own field → parent's BU → tenant default)
                // rather than forbidding it — the citation was inverted, and this project corrected the
                // same misreading in its own design.md. A matter-less draft is now resolved from the
                // acting user's business unit, server-side, instead of being refused.
                var resolvedContainerId = await ResolveCreateOnSaveContainerAsync(
                    request, httpContext, cancellationToken).ConfigureAwait(false);

                // Null means NO CONTAINER IS CONFIGURED (not "access denied" — that threw). Same honest
                // container-step failure the old client-supplied-ContainerId guard produced, so the
                // client's per-step projection is unchanged and nothing is ever written speculatively.
                if (string.IsNullOrWhiteSpace(resolvedContainerId))
                {
                    // #858 (unified-access-control-r2): the "no client-supplied ContainerId" warning that
                    // stood here is GONE with the premise — the client no longer supplies a container at
                    // all, so there is nothing about the request to report. The honest step failure below
                    // is the whole signal. Callee moved by task 070 cluster 2a.
                    return _createOnSave.BuildContainerFailedResult(request, observedAt);
                }

                // Fork B: mint the SPE drive-item in the RESOLVED container under the user's OBO identity
                // (the Compose user holds the file ACL; MI does not — same constraint that deferred profile).
                // First save of this transient key (or a deliberate Save-New fork): once created, the record
                // is stamped with the transient key (promote step below) so the NEXT create-on-save with the
                // same key takes the dedup replace path above — never a double mint.
                var driveId = await _spe.ResolveDriveIdAsync(resolvedContainerId, cancellationToken).ConfigureAwait(false);
                using var createStream = new MemoryStream(contentToPersist, writable: false);

                // The value handed to the sink is named as what it IS — the whole upload path — and is
                // sanitized AT the call rather than only in ResolveFileName far above. Redundant on today's
                // control flow and deliberately so: `fileName` is reassigned three times in this method
                // (from replaced.Name / created.Name), so "it was sanitized when it was created" is not a
                // property a future edit preserves. Sanitizing is idempotent. Enforced by
                // tests/Spaarke.ArchTests/SpeUploadPathIsFlatGuardTests.cs.
                var uploadPath = SpeUploadPath.SanitizeFileName(fileName);

                var created = await _spe.UploadSmallAsUserAsync(
                        httpContext, driveId, uploadPath, createStream, cancellationToken)
                    .ConfigureAwait(false);

                if (created is null || string.IsNullOrEmpty(created.Id))
                {
                    _logger.LogError(
                        "Compose create-on-save: SPE drive-item creation returned null/empty for container={ContainerId} — failing the '{Step}' step (session={SessionId}).",
                        resolvedContainerId, StepContainer, request.SessionId);
                    return _createOnSave.BuildContainerFailedResult(request, observedAt);
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
            var replaced = await _saveStorage.ReplaceWithPreconditionAsync(
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
        await _saveStorage.SetSaveVersionStampAsync(effectiveSpeId, saved.ETag, observedAt, cancellationToken).ConfigureAwait(false);

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
        catch (InvalidOperationException ex) when (ComposeCreateOnSavePromoter.IsDataverseIdentityKeyFault(ex))
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

            return ComposeCreateOnSavePromoter.BuildRecordFailedResult(
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
            await _pdfIntake.SetPdfDerivedDocumentAsync(
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
        var completion = ComposeCreateOnSavePromoter.ProjectCreateOnSaveState(
            subjectId: effectiveSpeId,
            correlationId: httpContext.TraceIdentifier,
            containerSignal: ComposeCreateOnSavePromoter.CompletedSignal(StepContainer),
            // FR-S09 item 5(a) (r8 task 016): derived, not asserted. This was a hardcoded
            // CompletedSignal — the record step reported success even when promotion resolved no record
            // id at all, which is the same class of claim-without-evidence as a 200 that means nothing
            // was written. The very next statement already branches on `DocumentRecordId.HasValue` for
            // the profile step, so the two lines used to contradict each other three lines apart.
            recordSignal: promotion.DocumentRecordId.HasValue
                ? ComposeCreateOnSavePromoter.CompletedSignal(StepRecord)
                : ComposeCreateOnSavePromoter.RecordNotResolvedSignal(),
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
    /// FR-S02 (r8 task 011): the degradation-warning code carried when a save superseded a version another
    /// writer landed while the document was open. Concurrency is last-writer-wins with a warning; this code
    /// IS the warning, and the client renders it naming version history as the recovery path.
    /// Mirrored client-side by <c>CONCURRENT_EXTERNAL_CHANGE_CODE</c> in <c>ComposeBannerStack.tsx</c>.
    /// </summary>
    internal const string ConcurrentExternalChangeCode = "concurrent-external-change";

    // =========================================================================
    // G10 (FR-09, task 040) — Document Profile re-run: reload/onload re-trigger (storm-safe) + the shared
    // manual "Refresh Profile" leg. Both reuse the EXISTING fire-and-forget DispatchBackgroundProfile
    // pipeline (never a second trigger). The storm guard is a DEDICATED per-doc "profiled-at eTag" stamp
    // (IDistributedCache, ADR-009) — INTENTIONALLY separate from the FR-08 save-version stamp so it never
    // perturbs the save-path staleness/re-anchor semantics. A reopen re-profiles ONLY when the live eTag
    // differs from the last-profiled eTag (an external Word edit, or a doc Compose never profiled); an
    // unchanged reopen matches the stamp → skip (no profiling storm on repeated reopens).
    // =========================================================================

    /// <inheritdoc />
    /// <remarks>
    /// The interface member stays here and the implementation lives in
    /// <see cref="ComposeProfileRetriggerGuard"/> (task 070 cluster 5a): the CONTRACT is the service's
    /// to keep, only the re-profiling policy moves.
    /// </remarks>
    public Task<bool> RefreshProfileAsync(
        RefreshComposeProfileRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
        => _profileRetrigger.RefreshProfileAsync(request, httpContext, cancellationToken);

    /// <summary>
    /// Resolves the tracked-change revision AUTHOR for a synthesized redline (task 022) from the caller's
    /// OBO identity — the acting user's display name (<c>name</c> / <see cref="ClaimTypes.Name"/> /
    /// <c>preferred_username</c>), so Word attributes the user's own direct-typing edits to the user.
    /// Falls back to a stable product label when no name claim is present (never empty — the synthesizer
    /// requires a non-whitespace author).
    /// </summary>
    /// <remarks>
    /// <c>internal</c> rather than private so <see cref="ComposeReanchorCoordinator"/> can reuse it
    /// (task 070 cluster 1). It stays HERE because its reason-to-change is the save path's identity
    /// handling and two of its three callers are on this class; it is a pure function of its argument,
    /// so sharing it is a helper reference, not a dependency cycle.
    /// </remarks>
    internal static string ResolveRevisionAuthor(HttpContext httpContext)
    {
        var user = httpContext.User;
        var name = user?.FindFirst("name")?.Value
            ?? user?.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value
            ?? user?.FindFirst("preferred_username")?.Value
            ?? user?.Identity?.Name;

        return string.IsNullOrWhiteSpace(name) ? "Spaarke Compose" : name!.Trim();
    }

    /// <inheritdoc />
    /// <remarks>
    /// The interface member stays here and the implementation lives in
    /// <see cref="ComposeCreateOnSavePromoter"/> (task 070 cluster 2a): the CONTRACT is the service's
    /// to keep, only the promotion policy moves. Same split as cluster 6's annotation store.
    /// </remarks>
    public Task<PromoteComposeDocumentResult> PromoteIfEphemeralAsync(
        PromoteComposeDocumentRequest request,
        HttpContext httpContext,
        CancellationToken cancellationToken = default)
        => _createOnSave.PromoteIfEphemeralAsync(request, httpContext, cancellationToken);

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
