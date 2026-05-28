using System.Diagnostics;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.Insights;

/// <summary>
/// Phase 1 Zone A implementation of <see cref="IInsightsAi"/> — orchestrates the
/// synthesis path (<see cref="AnswerQuestionAsync"/> via D-P13 cache +
/// <see cref="IPlaybookExecutionEngine"/>), the ingest path
/// (<see cref="RunIngestAsync"/> via the universal ingest playbook), and embedding
/// generation (<see cref="EmbedTextAsync"/> via <see cref="IOpenAiClient"/>) for
/// Zone B callers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone A placement</b>: lives under <c>Services/Ai/Insights/</c> and freely imports
/// AI internals (<see cref="IPlaybookExecutionEngine"/>, <see cref="IOpenAiClient"/>,
/// <see cref="IInsightsPlaybookExecutionCache"/>). Zone B callers receive
/// <see cref="IInsightsAi"/> via DI and have no visibility into any of these types.
/// </para>
/// <para>
/// <b>Phase 1 implementation status</b>:
/// <list type="bullet">
///   <item><see cref="EmbedTextAsync"/> — <b>complete</b> (thin delegation to
///   <see cref="IOpenAiClient.GenerateEmbeddingAsync"/>); unblocks task 041 Step 3.</item>
///   <item><see cref="AnswerQuestionAsync"/> — <b>scaffold</b>: cache + engine wiring is
///   in place, but the full D-P15 wiring (subject → <c>DocumentIds[]</c> mapping for
///   <see cref="PlaybookRunRequest"/>, <c>NotImplementedException</c> on decline path,
///   D-P14 playbook id resolution) lands with task 061 (D-P15 endpoint) and task 060
///   (D-P14 synthesis playbook). The implementation here invokes the cache directly so
///   that as soon as a D-P14 playbook id is supplied as <see cref="InsightsAgentRequest.Question"/>
///   the wiring is honest.</item>
///   <item><see cref="RunIngestAsync"/> — <b>scaffold</b>: throws
///   <see cref="NotImplementedException"/> with a "task 040 (D-P7)" message until the
///   universal ingest playbook lands and the orchestrator can resolve it by convention.
///   The D-P8 consumer (task 050) is the first caller that will exercise this path.</item>
/// </list>
/// </para>
/// <para>
/// <b>ADR-013 + ADR-009 + ADR-010 compliance</b>:
/// <list type="bullet">
///   <item>ADR-013: Zone A; single facade implementation behind the IInsightsAi seam.</item>
///   <item>ADR-009: synthesis path goes through the D-P13 Redis cache
///   (<see cref="IInsightsPlaybookExecutionCache"/>); no direct
///   <see cref="IDistributedCache"/> usage here.</item>
///   <item>ADR-010: single concrete impl behind the interface; registered Singleton
///   (all dependencies are themselves Singleton or thread-safe Scoped-resolved per call).</item>
/// </list>
/// </para>
/// </remarks>
public sealed class InsightsOrchestrator : IInsightsAi
{
    private readonly IPlaybookExecutionEngine _engine;
    private readonly IInsightsPlaybookExecutionCache _cache;
    private readonly IOpenAiClient _openAi;
    private readonly ILogger<InsightsOrchestrator> _logger;

    public InsightsOrchestrator(
        IPlaybookExecutionEngine engine,
        IInsightsPlaybookExecutionCache cache,
        IOpenAiClient openAi,
        ILogger<InsightsOrchestrator> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InsightsAgentResult> AnswerQuestionAsync(
        InsightsAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.AccessibleScopeHash);

        var sw = Stopwatch.StartNew();

        var cacheRequest = new InsightsPlaybookExecutionRequest(
            PlaybookId: request.Question,
            Subject: request.Subject,
            Parameters: request.Parameters,
            AccessibleScopeHash: request.AccessibleScopeHash,
            TenantId: request.TenantId,
            Ttl: null); // Defer to InsightsPlaybookExecutionCache.DefaultTtl (D-P13: 5 min).

        // Cache wraps the engine. On HIT the engine factory is never invoked.
        // On MISS the cache invokes the factory which drives ExecuteBatchAsync and
        // drains the stream looking for the ReturnInsightArtifactNode output (D-P12).
        var preCacheElapsed = sw.ElapsedMilliseconds;
        bool factoryWasCalled = false;

        var artifact = await _cache.GetOrExecuteAsync(
            cacheRequest,
            engineInvocation: ct =>
            {
                factoryWasCalled = true;
                return _engine.ExecuteBatchAsync(
                    new PlaybookRunRequest
                    {
                        PlaybookId = request.Question,
                        // Phase 1 scaffold note: D-P15 endpoint (task 061) is responsible
                        // for resolving subject → relevant document ids before calling the
                        // facade. The synthesis playbook (D-P14) uses LiveFactNode +
                        // IndexRetrieveNode for cohort retrieval; it does NOT process
                        // ad-hoc DocumentIds. We pass an empty array to satisfy the
                        // engine's required[] contract; the playbook ignores it.
                        DocumentIds = Array.Empty<Guid>(),
                        Parameters = request.Parameters
                    },
                    ct);
            },
            cancellationToken);

        sw.Stop();
        var elapsedMs = sw.ElapsedMilliseconds;
        var cacheHit = !factoryWasCalled;

        if (artifact is not null)
        {
            _logger.LogDebug(
                "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced artifact (cacheHit={CacheHit}, elapsedMs={ElapsedMs})",
                request.Question, request.Subject, cacheHit, elapsedMs);
            return InsightsAgentResult.Success(artifact, cacheHit, elapsedMs);
        }

        // No artifact emitted by the playbook. Phase 1 scaffold: surface as a stub
        // decline so the contract honors the "exactly one of" invariant. Full decline
        // wiring (real DeclineToFindNode output extraction from the engine stream;
        // structured MinimumEvidenceNeeded propagation) lands with task 061 (D-P15 endpoint)
        // once a real D-P14 playbook with EvidenceSufficiencyNode + DeclineToFindNode
        // is registered and the orchestrator can distinguish "no artifact, took decline path"
        // from "no artifact, engine errored".
        _logger.LogDebug(
            "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced no artifact; returning scaffold decline (cacheHit={CacheHit}, elapsedMs={ElapsedMs})",
            request.Question, request.Subject, cacheHit, elapsedMs);

        return InsightsAgentResult.Declined(
            new DeclineResponse
            {
                Reason = "no-artifact-produced",
                Explanation = "The playbook completed without emitting an InsightArtifact. Phase 1 scaffold response — task 061 (D-P15 endpoint) wires structured decline extraction from the engine stream.",
                MinimumEvidenceNeeded = new Dictionary<string, object>(),
                SuggestedActions = Array.Empty<string>(),
                ConfidenceInDecline = 0.0 // Not a real decline verdict — scaffold; consumers should check elapsedMs/cacheHit.
            },
            cacheHit,
            elapsedMs);
    }

    /// <inheritdoc />
    public Task<InsightsIngestResult> RunIngestAsync(
        InsightsIngestRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.DocumentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.MatterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);

        // Phase 1 scaffold: full implementation lands with task 040 (D-P7 universal
        // ingest playbook) — the orchestrator will resolve the playbook by convention
        // (e.g., "insights:ingest:universal@v1"), invoke ExecuteBatchAsync with the
        // single document id, and read structured aggregate counts from the playbook
        // run output. The D-P8 SPE-upload consumer (task 050) is the first real
        // caller that will exercise this path.
        throw new NotImplementedException(
            "RunIngestAsync — Phase 1 scaffold. Full universal ingest playbook " +
            "(D-P7) lands with task 040; this method becomes operational then. " +
            "The D-P8 SPE-upload consumer (task 050) is the first caller that will " +
            "exercise the live path.");
    }

    /// <inheritdoc />
    public Task<ReadOnlyMemory<float>> EmbedTextAsync(
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        // Thin delegation to IOpenAiClient. Model + dimensions are left at the
        // client's configured defaults (text-embedding-3-large, 3072 dims) per
        // SPEC §3.4 / D-P2 — the spaarke-insights-index schema is fixed at 3072
        // and the facade is opinionated for substrate consistency. Callers
        // (e.g., D-P4 PrecedentProjectionSync per task 041) do not choose.
        return _openAi.GenerateEmbeddingAsync(
            text,
            model: null,
            dimensions: null,
            cancellationToken: cancellationToken);
    }
}
