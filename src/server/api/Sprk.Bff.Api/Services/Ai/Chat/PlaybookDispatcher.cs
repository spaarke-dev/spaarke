using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Sprk.Bff.Api.Api.Ai;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Models.Ai.Chat;
using Sprk.Bff.Api.Services.Ai.Nodes;
using Sprk.Bff.Api.Services.Ai.PlaybookEmbedding;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Two-stage playbook intent matching: vector similarity search + LLM refinement.
///
/// <para>
/// <b>Stage 1 — Vector Similarity Search</b> (1.5s budget):
/// Embeds the user message via <see cref="PlaybookEmbeddingService.SearchPlaybooksAsync"/> and
/// queries the <c>playbook-embeddings</c> AI Search index. Pre-filters by <c>recordType</c>
/// from <see cref="ChatHostContext"/> when available. Returns top 5 candidates.
/// If a single candidate scores &gt;= 0.85, Stage 2 is skipped.
/// </para>
///
/// <para>
/// <b>Stage 2 — LLM Refinement + Parameter Extraction</b> (0.5s budget):
/// Sends the top candidates + user message to the execution <see cref="IChatClient"/>.
/// Extracts: best match, confidence, and parameter values as a dictionary.
/// </para>
///
/// <para>
/// <b>Output enrichment</b>: Once matched, the dispatcher queries the playbook's
/// DeliverOutput node to populate <see cref="DispatchResult.OutputType"/>,
/// <see cref="DispatchResult.RequiresConfirmation"/>, and <see cref="DispatchResult.TargetPage"/>
/// from the JPS definition (spec FR-18 — NOT hardcoded).
/// </para>
///
/// <para>
/// <b>Caching</b> (ADR-014): Final dispatch results are cached in Redis with a version key
/// derived from the playbook catalog version. Cache is tenant-scoped. Individual user messages
/// are NOT cached (each message is unique).
/// </para>
///
/// <b>Not registered in DI</b> (ADR-010). Factory-instantiated by <see cref="SprkChatAgentFactory"/>.
/// </summary>
public sealed class PlaybookDispatcher
{
    /// <summary>
    /// Confidence threshold above which a single-candidate Stage 1 result is accepted
    /// without Stage 2 LLM refinement.
    /// </summary>
    private const double HighConfidenceThreshold = 0.85;

    /// <summary>
    /// Maximum number of playbook candidates from Stage 1 vector search.
    /// </summary>
    private const int MaxCandidates = 5;

    /// <summary>
    /// Stage 1 timeout: vector similarity search budget.
    /// </summary>
    private static readonly TimeSpan Stage1Timeout = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Stage 2 timeout: LLM refinement budget.
    /// </summary>
    private static readonly TimeSpan Stage2Timeout = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Total NFR-04 budget: both stages combined.
    /// </summary>
    private static readonly TimeSpan TotalTimeout = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Cache TTL for dispatch results. Short because playbook catalog can change.
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Concurrency limiter for AI calls (ADR-016: bound concurrency).
    /// Shared across all instances within the process to prevent AI Search / LLM overload.
    /// </summary>
    private static readonly SemaphoreSlim AiConcurrencyLimiter = new(maxCount: 10, initialCount: 10);

    private readonly PlaybookEmbeddingService _embeddingService;
    private readonly IChatClient _executionClient;
    private readonly INodeService _nodeService;
    private readonly IDistributedCache _cache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger _logger;
    private readonly string _tenantId;

    /// <summary>
    /// Lazy-initialised default <see cref="IMemoryCache"/> for Phase B per-file
    /// candidate caching when callers do not supply one. Mirrors the precedent set
    /// by <see cref="PublicContracts.ConsumerRoutingService"/> and
    /// <see cref="PlaybookLookupService"/>: an in-process 5-min TTL cache for
    /// query-result memoisation (ADR-014). Single shared instance avoids each
    /// per-tenant dispatcher allocating its own bag.
    /// </summary>
    private static readonly Lazy<IMemoryCache> DefaultMemoryCache =
        new(() => new MemoryCache(new MemoryCacheOptions()));

    /// <summary>
    /// Phase B per-file top-K cache TTL (chat-routing-redesign-r1 FR-17 v2, ADR-014).
    /// </summary>
    private static readonly TimeSpan PhaseBCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Maximum number of characters of <see cref="ChatMessageAttachment.TextContent"/>
    /// included in the per-file query for the manifest-absent vector path. Long enough
    /// to bias the embedder toward the file's content, short enough to keep total
    /// embed-input under text-embedding-3-large's 8K-token ceiling.
    /// </summary>
    private const int PhaseBTextPrefixCharLimit = 2_000;

    /// <summary>
    /// Initializes a new instance of <see cref="PlaybookDispatcher"/>.
    /// </summary>
    /// <param name="embeddingService">Playbook embedding service for vector similarity search.</param>
    /// <param name="executionClient">IChatClient for Stage 2 LLM refinement (fast model).</param>
    /// <param name="nodeService">Node service for querying playbook DeliverOutput nodes (JPS).</param>
    /// <param name="cache">Distributed cache for result caching (ADR-009, ADR-014).</param>
    /// <param name="tenantId">Tenant ID for cache key scoping (ADR-014).</param>
    /// <param name="logger">Logger instance.</param>
    /// <remarks>
    /// <para>
    /// <b>Factory instantiation rationale</b>: this class is factory-instantiated by
    /// <c>SprkChatAgentFactory</c> (NOT DI-registered) BECAUSE the <c>tenantId</c> ctor
    /// parameter must be resolved per-request from the calling agent's session context.
    /// DI registration would have to choose a single lifetime — Singleton would pin the
    /// first-seen tenant into shared state (tenant-leak risk), and Scoped would still
    /// require a per-request factory to supply <c>tenantId</c>, defeating the registration.
    /// Factory instantiation makes the per-tenant binding explicit at the call site.
    /// </para>
    /// <para>
    /// ADR-010 DI minimalism is a secondary benefit (avoids inflating the DI graph for a
    /// type that needs runtime-resolved arguments), but the LOAD-BEARING reason is the
    /// per-request tenantId binding above.
    /// </para>
    /// <para>
    /// Amended 2026-06-05 by <c>bff-ai-architecture-audit-r1</c> Migration PR #3 per
    /// W1 Cat 4 §9 SprkChat row.
    /// </para>
    /// </remarks>
    public PlaybookDispatcher(
        PlaybookEmbeddingService embeddingService,
        IChatClient executionClient,
        INodeService nodeService,
        IDistributedCache cache,
        string tenantId,
        ILogger<PlaybookDispatcher> logger,
        IMemoryCache? memoryCache = null)
    {
        _embeddingService = embeddingService ?? throw new ArgumentNullException(nameof(embeddingService));
        _executionClient = executionClient ?? throw new ArgumentNullException(nameof(executionClient));
        _nodeService = nodeService ?? throw new ArgumentNullException(nameof(nodeService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _tenantId = tenantId ?? throw new ArgumentNullException(nameof(tenantId));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _memoryCache = memoryCache ?? DefaultMemoryCache.Value;
    }

    /// <summary>
    /// Dispatches a user message through the two-stage playbook matching pipeline.
    /// </summary>
    /// <param name="userMessage">The user's natural language message to match against playbooks.</param>
    /// <param name="hostContext">
    /// Optional host context describing where SprkChat is embedded.
    /// When provided, <see cref="ChatHostContext.EntityType"/> is used to pre-filter
    /// the vector search by <c>recordType</c>.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="attachments">
    /// Optional per-turn chat-message attachments (FR-15, Phase 5R Wave 5-A foundation).
    /// When <c>null</c> or empty, dispatch behaves identically to the pre-FR-15 message-only
    /// path (backward-compatibility invariant). Downstream tasks 111R/112/113R/114R will
    /// extend this signature with the Hybrid C file-aware Phase A/B/C classification flow;
    /// task 110 only carries the parameter so the wiring is in place ahead of those tasks.
    /// </param>
    /// <returns>
    /// A <see cref="DispatchResult"/> with the matched playbook and extracted parameters,
    /// or <see cref="DispatchResult.NoMatch"/> if no playbook matches the user message.
    /// Returns null when the AI Search service is overloaded (ADR-016: 503 backpressure).
    /// </returns>
    /// <remarks>
    /// <b>Backward-compat invariant (FR-15)</b>: callers that do not pass <paramref name="attachments"/>
    /// (or pass <c>null</c> / an empty list) MUST observe behavior identical to the pre-task-110
    /// signature. This is enforced by the early-return guard at the top of the method body and
    /// covered by <c>PlaybookDispatcherAttachmentsTests</c>.
    /// </remarks>
    public async Task<DispatchResult?> DispatchAsync(
        string userMessage,
        ChatHostContext? hostContext,
        CancellationToken cancellationToken = default,
        IReadOnlyList<ChatMessageAttachment>? attachments = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage, nameof(userMessage));

        var totalStopwatch = Stopwatch.StartNew();

        // FR-15 backward-compat guard (task 110): when no attachments are present, this
        // dispatcher behaves exactly as the pre-FR-15 message-only path. The variable below
        // is the explicit no-attachments check; flagged for tasks 111R-114R which will
        // branch above this point into the Hybrid C Phase A/B/C classification flow.
        var hasAttachments = attachments is { Count: > 0 };

        _logger.LogDebug(
            "PlaybookDispatcher: starting dispatch for message length={MessageLength}, entityType={EntityType}, attachmentCount={AttachmentCount}",
            userMessage.Length, hostContext?.EntityType ?? "(none)", attachments?.Count ?? 0);

        // Phase 5R Wave 5-A entry point. Task 110 retains today's message-only behavior for
        // all paths (with and without attachments). Tasks 111R-114R will branch on
        // `hasAttachments` to invoke the file-aware classification pipeline; until then the
        // attachments parameter is accepted for caller-readiness but does not alter dispatch.
        _ = hasAttachments;

        // ADR-016: Acquire concurrency permit with total timeout as deadline.
        if (!await AiConcurrencyLimiter.WaitAsync(TotalTimeout, cancellationToken))
        {
            _logger.LogWarning(
                "PlaybookDispatcher: concurrency limit exceeded, returning 503 backpressure");
            return null; // Caller should return 503
        }

        try
        {
            // === Stage 1: Vector Similarity Search ===
            var stage1Stopwatch = Stopwatch.StartNew();
            PlaybookSearchResult[] candidates;

            try
            {
                using var stage1Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stage1Cts.CancelAfter(Stage1Timeout);

                // Pre-filter by recordType from host context (ADR-013: entity scoping)
                var recordTypeFilter = hostContext?.EntityType is { Length: > 0 }
                    ? MapEntityTypeToRecordType(hostContext.EntityType)
                    : null;

                candidates = await _embeddingService.SearchPlaybooksAsync(
                    userMessage,
                    recordTypeFilter: recordTypeFilter,
                    topK: MaxCandidates,
                    cancellationToken: stage1Cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "PlaybookDispatcher: Stage 1 timed out after {ElapsedMs}ms (budget={BudgetMs}ms)",
                    stage1Stopwatch.ElapsedMilliseconds, Stage1Timeout.TotalMilliseconds);
                return DispatchResult.NoMatch;
            }

            stage1Stopwatch.Stop();
            _logger.LogDebug(
                "PlaybookDispatcher: Stage 1 completed in {ElapsedMs}ms — {CandidateCount} candidates",
                stage1Stopwatch.ElapsedMilliseconds, candidates.Length);

            // No candidates → no match
            if (candidates.Length == 0)
            {
                _logger.LogDebug("PlaybookDispatcher: no candidates found, returning NoMatch");
                LogTotalDuration(totalStopwatch, "NoMatch (0 candidates)");
                return DispatchResult.NoMatch;
            }

            // === Stage 2 bypass: single high-confidence candidate ===
            if (candidates.Length == 1 && candidates[0].Score >= HighConfidenceThreshold)
            {
                _logger.LogDebug(
                    "PlaybookDispatcher: single high-confidence candidate ({Score:F3} >= {Threshold}), skipping Stage 2",
                    candidates[0].Score, HighConfidenceThreshold);

                var directResult = await BuildResultFromCandidate(
                    candidates[0], candidates[0].Score, new Dictionary<string, string>(), cancellationToken);

                LogTotalDuration(totalStopwatch, $"DirectMatch (score={candidates[0].Score:F3})");
                return directResult;
            }

            // === Stage 2: LLM Refinement + Parameter Extraction ===
            var stage2Stopwatch = Stopwatch.StartNew();
            LlmRefinementOutput? refinement;

            try
            {
                using var stage2Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                stage2Cts.CancelAfter(Stage2Timeout);

                refinement = await RefineWithLlmAsync(userMessage, candidates, stage2Cts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "PlaybookDispatcher: Stage 2 timed out after {ElapsedMs}ms (budget={BudgetMs}ms). " +
                    "Falling back to top Stage 1 candidate.",
                    stage2Stopwatch.ElapsedMilliseconds, Stage2Timeout.TotalMilliseconds);

                // Fallback: use top Stage 1 candidate if Stage 2 times out
                var fallbackResult = await BuildResultFromCandidate(
                    candidates[0], candidates[0].Score, new Dictionary<string, string>(), cancellationToken);

                LogTotalDuration(totalStopwatch, "FallbackMatch (Stage 2 timeout)");
                return fallbackResult;
            }

            stage2Stopwatch.Stop();
            _logger.LogDebug(
                "PlaybookDispatcher: Stage 2 completed in {ElapsedMs}ms",
                stage2Stopwatch.ElapsedMilliseconds);

            // LLM said "none" or returned null
            if (refinement is null || refinement.PlaybookId is null or "none")
            {
                _logger.LogDebug("PlaybookDispatcher: LLM refinement returned no match");
                LogTotalDuration(totalStopwatch, "NoMatch (LLM refinement)");
                return DispatchResult.NoMatch;
            }

            // Find the matching candidate from Stage 1 results
            var matchedCandidate = Array.Find(candidates,
                c => c.PlaybookId.Equals(refinement.PlaybookId, StringComparison.OrdinalIgnoreCase));

            if (matchedCandidate is null)
            {
                _logger.LogWarning(
                    "PlaybookDispatcher: LLM selected playbookId={PlaybookId} not in candidate list",
                    refinement.PlaybookId);
                LogTotalDuration(totalStopwatch, "NoMatch (LLM selected unknown candidate)");
                return DispatchResult.NoMatch;
            }

            var result = await BuildResultFromCandidate(
                matchedCandidate,
                refinement.Confidence,
                refinement.Parameters ?? new Dictionary<string, string>(),
                cancellationToken);

            LogTotalDuration(totalStopwatch, $"Match (playbook={matchedCandidate.PlaybookName}, confidence={refinement.Confidence:F3})");
            return result;
        }
        finally
        {
            AiConcurrencyLimiter.Release();
        }
    }

    #region Phase B — Per-file Vector Match (FR-17 v2 / task 112)

    /// <summary>
    /// Per-file Hybrid C Phase B classifier (chat-routing-redesign-r1 FR-17 v2, task 112).
    /// Performs a per-attachment vector match against the <c>playbook-embeddings</c>
    /// index in parallel, returning the top-K candidate playbooks for each file
    /// independently. The caller (task 113R top-N selector) reconciles cross-file
    /// disagreements.
    /// </summary>
    /// <param name="userMessage">The user's natural-language turn message. Composed
    /// into the per-file query for the manifest-absent path so the embedding picks
    /// up user intent in addition to file content.</param>
    /// <param name="attachments">Per-turn attachment list (must be non-empty — empty
    /// or null returns an empty result array).</param>
    /// <param name="sessionFiles">
    /// Optional matching <see cref="ChatSessionFile"/> entries (one per attachment,
    /// aligned by index OR by <c>FileId</c>). When an entry's
    /// <see cref="ChatSessionFile.ClassifiedDocType"/> is non-null, that file uses
    /// the <b>manifest-present</b> path: a structured <c>documentTypes</c> pre-filter
    /// against <c>playbook-embeddings</c>. When the entry is null or the doc-type is
    /// null, the file falls through to the <b>manifest-absent</b> path: a per-file
    /// composed query <c>"{userMessage} | Document: {filename} | Type hint: {contentType} | Content: {textPrefix}"</c>.
    /// MVP scope: Phase 4b classification is deferred so the manifest-absent path is
    /// the production path; the manifest-present path is forward-compat scaffolding.
    /// </param>
    /// <param name="topK">Per-file top-K (defaults to 5). The caller picks final N.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of per-file results, ordered to match <paramref name="attachments"/>.</returns>
    /// <remarks>
    /// <para>
    /// <b>Caching (ADR-014)</b>: results are memoised in <see cref="IMemoryCache"/>
    /// with a 5-min absolute TTL. Cache key is tenant-scoped:
    /// <c>$"phaseb:{_tenantId}:mfpresent:{hash(message)}:{classifiedDocType}"</c>
    /// for the manifest-present path and
    /// <c>$"phaseb:{_tenantId}:mfabsent:{hash(message|filename|contentType|prefix)}"</c>
    /// for the manifest-absent path. Hash is SHA-256 over UTF-8 bytes (deterministic
    /// across processes) — first 32 hex chars only (collision-safe within tenant scope
    /// and over a 5-min window).
    /// </para>
    /// <para>
    /// <b>Telemetry (ADR-015 tier 1)</b>: per-file latency, total latency, file count,
    /// manifest-present flag, and per-file top-K count are logged at Information level.
    /// Query text, file content, embedding values, and classifier confidence are NEVER
    /// logged.
    /// </para>
    /// <para>
    /// <b>Performance budgets (FR-17 v2)</b>: manifest-present path p95 ≤100ms (filter
    /// overhead only — single embedding call + pre-filtered search); manifest-absent
    /// path ≤300ms for 3 files (parallel fan-out — bounded by slowest embedding+search).
    /// Total dispatch budget remains 2s per FR-19 (<see cref="TotalTimeout"/>).
    /// </para>
    /// </remarks>
    public async Task<PhaseBPerFileResult[]> RunPhaseBVectorMatchAsync(
        string userMessage,
        IReadOnlyList<ChatMessageAttachment> attachments,
        IReadOnlyList<ChatSessionFile?>? sessionFiles = null,
        int topK = 5,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userMessage, nameof(userMessage));
        ArgumentNullException.ThrowIfNull(attachments);

        if (attachments.Count == 0)
        {
            return Array.Empty<PhaseBPerFileResult>();
        }

        var totalStopwatch = Stopwatch.StartNew();

        // Pair each attachment with its matching session-file entry (by index — the
        // attachments are turn-scoped and arrive in caller-supplied order; session
        // files are the corresponding upload manifest entries).
        var pairings = new List<(ChatMessageAttachment Attachment, ChatSessionFile? SessionFile)>(attachments.Count);
        for (var i = 0; i < attachments.Count; i++)
        {
            var sessionFile = sessionFiles is not null && i < sessionFiles.Count
                ? sessionFiles[i]
                : null;
            pairings.Add((attachments[i], sessionFile));
        }

        // Parallel fan-out per ADR-016 (no per-file semaphore — the AI concurrency
        // limiter on DispatchAsync is the upstream bound; Phase B is invoked under
        // that umbrella by the caller in task 113R).
        var manifestPresentCount = 0;
        var tasks = pairings.Select(async pair =>
        {
            var fileStopwatch = Stopwatch.StartNew();
            try
            {
                if (!string.IsNullOrWhiteSpace(pair.SessionFile?.ClassifiedDocType))
                {
                    Interlocked.Increment(ref manifestPresentCount);
                    var candidates = await RunPhaseBManifestPresentAsync(
                        userMessage,
                        pair.SessionFile.ClassifiedDocType,
                        topK,
                        cancellationToken);
                    fileStopwatch.Stop();
                    return new PhaseBPerFileResult(
                        FileId: pair.SessionFile.FileId,
                        Filename: pair.Attachment.Filename,
                        ManifestPresent: true,
                        Candidates: candidates,
                        LatencyMs: fileStopwatch.ElapsedMilliseconds);
                }
                else
                {
                    var candidates = await RunPhaseBManifestAbsentAsync(
                        userMessage,
                        pair.Attachment,
                        topK,
                        cancellationToken);
                    fileStopwatch.Stop();
                    return new PhaseBPerFileResult(
                        FileId: pair.SessionFile?.FileId,
                        Filename: pair.Attachment.Filename,
                        ManifestPresent: false,
                        Candidates: candidates,
                        LatencyMs: fileStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Per-file failure must not poison the whole fan-out; log + return
                // an empty-candidates entry so the caller (113R) can decide on
                // graceful degradation.
                fileStopwatch.Stop();
                _logger.LogWarning(ex,
                    "PlaybookDispatcher Phase B: per-file vector match failed " +
                    "(fileId={FileId}, contentType={ContentType}, elapsedMs={ElapsedMs})",
                    pair.SessionFile?.FileId ?? "(no-manifest)",
                    pair.Attachment.ContentType,
                    fileStopwatch.ElapsedMilliseconds);
                return new PhaseBPerFileResult(
                    FileId: pair.SessionFile?.FileId,
                    Filename: pair.Attachment.Filename,
                    ManifestPresent: pair.SessionFile?.ClassifiedDocType is not null,
                    Candidates: Array.Empty<PlaybookSearchResult>(),
                    LatencyMs: fileStopwatch.ElapsedMilliseconds);
            }
        });

        var results = await Task.WhenAll(tasks);

        totalStopwatch.Stop();

        // ADR-015 tier-1 telemetry: counts + latency only. No query content, file
        // content, embedding values, or classifier confidence ever leaves the process
        // via the log pipeline.
        var perFileLatencies = string.Join(",", results.Select(r => r.LatencyMs));
        var topKCount = results.Sum(r => r.Candidates.Count);
        _logger.LogInformation(
            "PlaybookDispatcher Phase B: filesCount={FilesCount} manifestPresent={ManifestPresent} " +
            "perFileLatencyMs=[{PerFileLatencyMs}] totalLatencyMs={TotalLatencyMs} topKCount={TopKCount}",
            results.Length,
            manifestPresentCount > 0,
            perFileLatencies,
            totalStopwatch.ElapsedMilliseconds,
            topKCount);

        return results;
    }

    /// <summary>
    /// Manifest-present per-file path: classified doc type drives a structured pre-filter
    /// (<c>documentTypes/any(t: search.in(t, 'NDA'))</c>) on the playbook-embeddings index.
    /// Per FR-17 v2 budget ≤100ms (single embed + filtered search; no extra LLM call).
    /// Results are cached for 5 min on <c>(tenantId, classifiedDocType, normalizedMessage)</c>.
    /// </summary>
    private async Task<IReadOnlyList<PlaybookSearchResult>> RunPhaseBManifestPresentAsync(
        string userMessage,
        string classifiedDocType,
        int topK,
        CancellationToken cancellationToken)
    {
        // Cache key includes a normalised hash of the user message so semantically
        // identical queries hit the same entry; classified doc type is the structural
        // discriminator.
        var normalizedMessage = NormalizeForCacheKey(userMessage);
        var messageHash = Sha256HexPrefix(normalizedMessage);
        var classifiedHash = EscapeForCacheKey(classifiedDocType);
        var cacheKey = $"phaseb:{_tenantId}:mfpresent:{messageHash}:{classifiedHash}";

        if (_memoryCache.TryGetValue<IReadOnlyList<PlaybookSearchResult>>(cacheKey, out var cached) &&
            cached is not null)
        {
            return cached;
        }

        var candidates = await _embeddingService.SearchPlaybooksAsync(
            query: userMessage,
            recordTypeFilter: null,
            documentTypeFilter: classifiedDocType,
            topK: topK,
            cancellationToken: cancellationToken);

        var snapshot = (IReadOnlyList<PlaybookSearchResult>)candidates;
        _memoryCache.Set(cacheKey, snapshot, PhaseBCacheTtl);
        return snapshot;
    }

    /// <summary>
    /// Manifest-absent per-file path: per-file query composition + vector search.
    /// Composes <c>"{userMessage} | Document: {filename} | Type hint: {contentType} | Content: {textPrefix}"</c>
    /// and embeds it as a single query against the unfiltered playbook-embeddings index.
    /// Per FR-17 v2 budget ≤300ms for 3 files (parallel fan-out — bounded by slowest
    /// embed + search). Results cached 5 min on <c>(tenantId, normalizedQueryText)</c>.
    /// </summary>
    private async Task<IReadOnlyList<PlaybookSearchResult>> RunPhaseBManifestAbsentAsync(
        string userMessage,
        ChatMessageAttachment attachment,
        int topK,
        CancellationToken cancellationToken)
    {
        var textPrefix = attachment.TextContent.Length > PhaseBTextPrefixCharLimit
            ? attachment.TextContent[..PhaseBTextPrefixCharLimit]
            : attachment.TextContent;

        // FR-17 v2 query composition — order is stable for cache-key determinism.
        var query = $"{userMessage} | Document: {attachment.Filename} | Type hint: {attachment.ContentType} | Content: {textPrefix}";

        var normalizedQuery = NormalizeForCacheKey(query);
        var cacheKey = $"phaseb:{_tenantId}:mfabsent:{Sha256HexPrefix(normalizedQuery)}";

        if (_memoryCache.TryGetValue<IReadOnlyList<PlaybookSearchResult>>(cacheKey, out var cached) &&
            cached is not null)
        {
            return cached;
        }

        var candidates = await _embeddingService.SearchPlaybooksAsync(
            query: query,
            recordTypeFilter: null,
            documentTypeFilter: null,
            topK: topK,
            cancellationToken: cancellationToken);

        var snapshot = (IReadOnlyList<PlaybookSearchResult>)candidates;
        _memoryCache.Set(cacheKey, snapshot, PhaseBCacheTtl);
        return snapshot;
    }

    /// <summary>
    /// Lower-cases + collapses runs of whitespace so semantically-identical messages
    /// produce the same cache key. Deliberately not trimming punctuation: chat
    /// messages with slightly different punctuation are NOT cache-equivalent (the
    /// embedder will produce slightly different vectors).
    /// </summary>
    private static string NormalizeForCacheKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var lowered = s.Trim().ToLowerInvariant();
        // Collapse internal whitespace runs to a single space without LINQ
        // allocations.
        var sb = new StringBuilder(lowered.Length);
        var lastWasSpace = false;
        foreach (var c in lowered)
        {
            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    sb.Append(' ');
                    lastWasSpace = true;
                }
            }
            else
            {
                sb.Append(c);
                lastWasSpace = false;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// SHA-256 hex truncated to 32 chars — deterministic across processes (vs
    /// <see cref="string.GetHashCode()"/>) and collision-safe within the tenant-scoped,
    /// 5-min-TTL cache window the dispatcher operates in. ADR-015 compliant: input
    /// content is hashed away before contributing to the (potentially logged) cache
    /// key — only the hash prefix is observable.
    /// </summary>
    private static string Sha256HexPrefix(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input ?? string.Empty));
        return Convert.ToHexString(bytes)[..32].ToLowerInvariant();
    }

    /// <summary>
    /// Sanitises a classifier doc-type label for inclusion in a cache key — strips
    /// whitespace and colons so the <c>"a:b:c"</c> key structure stays unambiguous.
    /// </summary>
    private static string EscapeForCacheKey(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        return s.Trim().Replace(':', '_').Replace(' ', '_').ToLowerInvariant();
    }

    /// <summary>
    /// Per-file Phase B result (chat-routing-redesign-r1 task 112). Carries the
    /// top-K vector-match candidates plus per-file telemetry. The caller
    /// (task 113R top-N selector) reconciles N per-file lists into the final
    /// dispatch decision.
    /// </summary>
    /// <param name="FileId">
    /// Stable session-scoped file ID when the caller provided a matching
    /// <see cref="ChatSessionFile"/>; null when no session-file entry was supplied
    /// (e.g. transient turn-only attachments without an upload manifest).
    /// </param>
    /// <param name="Filename">
    /// Display filename (from <see cref="ChatMessageAttachment.Filename"/>) for the
    /// chat link-buttons UX in task 5-C.
    /// </param>
    /// <param name="ManifestPresent">
    /// <c>true</c> when the per-file path used the manifest-present structured
    /// pre-filter (Phase 4b classifier output drove the route); <c>false</c> for
    /// the manifest-absent parallel vector path (MVP production path while Phase 4b
    /// is deferred). Surfaced for telemetry and 113R reconciliation logic.
    /// </param>
    /// <param name="Candidates">
    /// Ordered top-K candidates (highest score first). May be empty when the search
    /// returned no matches (e.g. manifest-present filter narrowed to zero playbooks
    /// before Phase 4b backfill, OR per-file failure trapped by the fan-out
    /// catch-all).
    /// </param>
    /// <param name="LatencyMs">Per-file path latency in milliseconds.</param>
    public sealed record PhaseBPerFileResult(
        string? FileId,
        string Filename,
        bool ManifestPresent,
        IReadOnlyList<PlaybookSearchResult> Candidates,
        long LatencyMs);

    #endregion

    #region Stage 2 — LLM Refinement

    /// <summary>
    /// Sends candidates + user message to the execution IChatClient for refined selection
    /// and parameter extraction.
    /// </summary>
    private async Task<LlmRefinementOutput?> RefineWithLlmAsync(
        string userMessage,
        PlaybookSearchResult[] candidates,
        CancellationToken cancellationToken)
    {
        // Build a compact candidate list for the prompt (keep under ~500 tokens total)
        var candidateList = string.Join("\n", candidates.Select((c, i) =>
            $"{i + 1}. id=\"{c.PlaybookId}\" name=\"{c.PlaybookName}\" tags=[{string.Join(",", c.Tags)}]"));

        var systemPrompt = """
            You are a playbook matcher. Given a user message and a numbered list of playbook candidates,
            select the best matching playbook or respond "none" if no playbook fits.
            Extract any parameter values mentioned in the user message (e.g., recipient name, date, subject).
            Respond with JSON only: {"playbookId":"...","confidence":0.0-1.0,"parameters":{"key":"value"}}
            If no match: {"playbookId":"none","confidence":0,"parameters":{}}
            """;

        var userPrompt = $"User message: \"{userMessage}\"\n\nCandidates:\n{candidateList}";

        var messages = new List<Microsoft.Extensions.AI.ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, userPrompt)
        };

        var options = new ChatOptions
        {
            MaxOutputTokens = 200
        };

        try
        {
            var response = await _executionClient.GetResponseAsync(messages, options, cancellationToken);

            var responseText = response.Text?.Trim();
            if (string.IsNullOrWhiteSpace(responseText))
            {
                _logger.LogDebug("PlaybookDispatcher: LLM returned empty response");
                return null;
            }

            // Parse JSON response
            var result = JsonSerializer.Deserialize<LlmRefinementOutput>(responseText, LlmJsonOptions);
            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "PlaybookDispatcher: failed to parse LLM refinement output");
            return null;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "PlaybookDispatcher: LLM refinement call failed");
            return null;
        }
    }

    /// <summary>
    /// JSON deserialization options for the LLM refinement output.
    /// </summary>
    private static readonly JsonSerializerOptions LlmJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    /// <summary>
    /// Internal model for LLM refinement structured output.
    /// </summary>
    private sealed record LlmRefinementOutput
    {
        public string? PlaybookId { get; init; }
        public double Confidence { get; init; }
        public Dictionary<string, string>? Parameters { get; init; }
    }

    #endregion

    #region Result Building

    /// <summary>
    /// Builds a <see cref="DispatchResult"/> from a matched search candidate.
    /// Enriches with OutputType, RequiresConfirmation, and TargetPage from the playbook's
    /// JPS DeliverOutput node (spec FR-18).
    /// </summary>
    private async Task<DispatchResult> BuildResultFromCandidate(
        PlaybookSearchResult candidate,
        double confidence,
        Dictionary<string, string> extractedParameters,
        CancellationToken cancellationToken)
    {
        // Attempt to read output node metadata from cache or Dataverse.
        // Returns the primary DeliverOutput node's OutputType / RequiresConfirmation / TargetPage
        // (existing R2 surface) plus the new R6 per-playbook routing info
        // (NodeDestination + WidgetType) parsed from that same node's sprk_configjson.
        //
        // Spec FR-14c: matched playbook returns DispatchResult populated with NodeDestination
        // + WidgetType reflecting the matched playbook's primary DeliverOutput node's routing.
        //
        // R6 FR-26 convergence invariant: the chat-summarize playbook
        // (sprk_playbookid = 44285d15-...) has no `destination` property in its node's
        // sprk_configjson, so NodeRoutingConfig.Parse(null|empty|missing) returns
        // `{ Destination = Chat, WidgetType = null }` — preserving pre-R6 behavior without
        // any playbook-specific branching here. The default is the mechanism.
        var (outputType, requiresConfirmation, targetPage, nodeDestination, widgetType) =
            await GetOutputNodeMetadataAsync(candidate.PlaybookId, cancellationToken);

        return new DispatchResult(
            Matched: true,
            PlaybookId: candidate.PlaybookId,
            PlaybookName: candidate.PlaybookName,
            Confidence: confidence,
            OutputType: outputType,
            RequiresConfirmation: requiresConfirmation,
            ExtractedParameters: extractedParameters,
            TargetPage: targetPage,
            NodeDestination: nodeDestination,
            WidgetType: widgetType);
    }

    /// <summary>
    /// Retrieves OutputType, RequiresConfirmation, TargetPage, NodeDestination, and WidgetType
    /// from the playbook's DeliverOutput node. Cached per playbook (ADR-014: version-keyed,
    /// tenant-scoped).
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Primary-node selection heuristic</b> (spec FR-14c, task 047):
    /// The "primary AI/DeliverOutput node" is identified as the FIRST node in the playbook
    /// whose <see cref="PlaybookNodeDto.NodeType"/> equals <see cref="NodeType.Output"/>.
    /// <see cref="INodeService.GetNodesAsync"/> returns nodes ordered by
    /// <c>sprk_executionorder</c>, so this picks the terminal DeliverOutput node — the one
    /// that drives <c>PlaybookOutputHandler</c> rendering. This is the same heuristic the
    /// pre-R6 OutputType lookup used; we reuse it here to extract the routing config from
    /// the same node, avoiding a separate Dataverse round-trip and respecting ADR-014 by
    /// piggy-backing on the existing per-playbook cache.
    /// </para>
    /// <para>
    /// <b>R6 routing fields</b>: <see cref="PlaybookNodeDto.ConfigJson"/>
    /// (<c>sprk_playbooknode.sprk_configjson</c>) is parsed by
    /// <see cref="NodeRoutingConfig.Parse"/> which is null/empty/malformed-safe and
    /// returns <c>{ Destination = Chat, WidgetType = null }</c> by default — preserving
    /// pre-R6 behavior and the R6 FR-26 chat-summarize convergence invariant without any
    /// per-playbook branching.
    /// </para>
    /// </remarks>
    private async Task<(OutputType outputType, bool requiresConfirmation, string? targetPage,
        NodeDestination nodeDestination, string? widgetType)>
        GetOutputNodeMetadataAsync(string playbookId, CancellationToken cancellationToken)
    {
        // ADR-014: Cache key scoped by tenant and playbook
        var cacheKey = $"dispatch:output:{_tenantId}:{playbookId}";

        try
        {
            // Check cache first
            var cached = await _cache.GetStringAsync(cacheKey, cancellationToken);
            if (cached is not null)
            {
                var cachedMeta = JsonSerializer.Deserialize<OutputNodeMetadata>(cached);
                if (cachedMeta is not null)
                {
                    _logger.LogDebug(
                        "PlaybookDispatcher: output node metadata cache hit for playbook {PlaybookId}",
                        playbookId);
                    return (cachedMeta.OutputType, cachedMeta.RequiresConfirmation,
                        cachedMeta.TargetPage, cachedMeta.NodeDestination, cachedMeta.WidgetType);
                }
            }

            // Query Dataverse for playbook nodes
            if (!Guid.TryParse(playbookId, out var playbookGuid))
            {
                _logger.LogWarning("PlaybookDispatcher: invalid playbook ID format: {PlaybookId}", playbookId);
                return (OutputType.Text, false, null, NodeDestination.Chat, null);
            }

            var nodes = await _nodeService.GetNodesAsync(playbookGuid, cancellationToken);

            // Primary-node selection: first Output-type node by execution order
            // (INodeService.GetNodesAsync returns nodes ordered by sprk_executionorder).
            // This is the terminal DeliverOutput node that drives PlaybookOutputHandler.
            var outputNode = Array.Find(nodes, n => n.NodeType == NodeType.Output);

            OutputType outputType;
            bool requiresConfirmation;
            string? targetPage;
            NodeDestination nodeDestination;
            string? widgetType;

            if (outputNode is not null)
            {
                outputType = outputNode.OutputType ?? OutputType.Text;
                // Default: dialog/navigation require confirmation; text does not
                requiresConfirmation = outputNode.RequiresConfirmation
                    ?? (outputType is OutputType.Dialog or OutputType.Navigation);
                targetPage = outputNode.TargetPage;

                // R6 FR-14c / FR-27 routing: parse the node's sprk_configjson for
                // per-playbook routing destination + widget type. Parse() handles
                // null/empty/malformed gracefully (FR-14f): defaults to
                // { Destination = Chat, WidgetType = null }. This default IS the
                // chat-summarize convergence invariant (R6 FR-26) — no special case
                // needed.
                var routing = NodeRoutingConfig.Parse(outputNode.ConfigJson);
                nodeDestination = routing.Destination;
                widgetType = routing.WidgetType;
            }
            else
            {
                // No output node found — default to text with no confirmation,
                // and chat destination (pre-R6 backward compatibility)
                _logger.LogDebug(
                    "PlaybookDispatcher: no DeliverOutput node found for playbook {PlaybookId}, defaulting to text",
                    playbookId);
                outputType = OutputType.Text;
                requiresConfirmation = false;
                targetPage = null;
                nodeDestination = NodeDestination.Chat;
                widgetType = null;
            }

            // Cache the result (ADR-014)
            var metadata = new OutputNodeMetadata(
                outputType, requiresConfirmation, targetPage, nodeDestination, widgetType);
            await _cache.SetStringAsync(
                cacheKey,
                JsonSerializer.Serialize(metadata),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = CacheTtl },
                cancellationToken);

            return (outputType, requiresConfirmation, targetPage, nodeDestination, widgetType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "PlaybookDispatcher: failed to load output node metadata for playbook {PlaybookId}; " +
                "defaulting to text output + chat destination",
                playbookId);
            return (OutputType.Text, false, null, NodeDestination.Chat, null);
        }
    }

    /// <summary>
    /// Cached output node metadata record.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Cache compatibility (R6 FR-14c — task 047)</b>: the addition of
    /// <see cref="NodeDestination"/> and <see cref="WidgetType"/> changes the JSON shape.
    /// Older cache entries (pre-R6, without the new properties) deserialize with the C#
    /// init-default values — <c>NodeDestination.Chat</c> + <c>null</c> WidgetType — which
    /// is the backward-compatible behavior. No cache flush is required for the rolling
    /// deploy. Cache TTL is 5 minutes (see <see cref="CacheTtl"/>), so any stale shape
    /// would naturally roll over within minutes.
    /// </para>
    /// </remarks>
    private sealed record OutputNodeMetadata(
        OutputType OutputType,
        bool RequiresConfirmation,
        string? TargetPage,
        NodeDestination NodeDestination = NodeDestination.Chat,
        string? WidgetType = null);

    #endregion

    #region Helpers

    /// <summary>
    /// Maps ChatHostContext entity types to Dataverse record type logical names.
    /// </summary>
    private static string? MapEntityTypeToRecordType(string entityType)
    {
        return entityType.ToLowerInvariant() switch
        {
            "matter" => "sprk_matter",
            "project" => "sprk_project",
            "invoice" => "sprk_invoice",
            "account" => "account",
            "contact" => "contact",
            _ => null // Unknown entity type — don't filter
        };
    }

    /// <summary>
    /// Logs total dispatch duration at Information level for NFR-04 tracking.
    /// </summary>
    private void LogTotalDuration(Stopwatch stopwatch, string outcome)
    {
        stopwatch.Stop();
        var elapsed = stopwatch.ElapsedMilliseconds;
        var level = elapsed > TotalTimeout.TotalMilliseconds ? LogLevel.Warning : LogLevel.Information;

        _logger.Log(level,
            "PlaybookDispatcher: dispatch completed in {ElapsedMs}ms — {Outcome} (NFR-04 budget={BudgetMs}ms)",
            elapsed, outcome, TotalTimeout.TotalMilliseconds);
    }

    #endregion
}
