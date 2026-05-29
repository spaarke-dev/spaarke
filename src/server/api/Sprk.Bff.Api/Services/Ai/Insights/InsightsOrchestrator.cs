using System.Diagnostics;
using Sprk.Bff.Api.Models.Ai.PublicContracts;
using Sprk.Bff.Api.Models.Insights;
using Sprk.Bff.Api.Services.Ai.Insights.Ingest;
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
///   <item><see cref="RunIngestAsync"/> — <b>complete</b> (task 040): delegates to
///   <see cref="IIngestOrchestrator"/> which composes Sanitizer → Layer 1 → conditional
///   Layer 2 → GroundingVerifier → ConfidenceThreshold → ObservationEmitter →
///   IndexUpsert → Mirror. D-P11 mirror is wired as <see cref="Mirror.IObservationMirror"/>
///   seam; Phase 1 ships <see cref="Mirror.NoOpObservationMirror"/>; task 051 swaps in
///   the real Dataverse impl.</item>
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
    private readonly IIngestOrchestrator _ingestOrchestrator;
    private readonly ILogger<InsightsOrchestrator> _logger;

    public InsightsOrchestrator(
        IPlaybookExecutionEngine engine,
        IInsightsPlaybookExecutionCache cache,
        IOpenAiClient openAi,
        IIngestOrchestrator ingestOrchestrator,
        ILogger<InsightsOrchestrator> logger)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _openAi = openAi ?? throw new ArgumentNullException(nameof(openAi));
        _ingestOrchestrator = ingestOrchestrator ?? throw new ArgumentNullException(nameof(ingestOrchestrator));
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
        // drains the stream looking for BOTH the ReturnInsightArtifactNode output (D-P12)
        // and the DeclineToFindNode output (task 071). The result carries whichever path
        // the playbook took.
        bool factoryWasCalled = false;

        var runResult = await _cache.GetOrExecuteAsync(
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

        // Path 1: artifact produced (sufficient-evidence path). Defensive "both populated"
        // case: prefer Artifact per InsightsEngineRunResult contract ("sufficient path wins").
        if (runResult.HasArtifact)
        {
            _logger.LogDebug(
                "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced artifact (cacheHit={CacheHit}, elapsedMs={ElapsedMs})",
                request.Question, request.Subject, cacheHit, elapsedMs);
            return InsightsAgentResult.Success(runResult.Artifact!, cacheHit, elapsedMs);
        }

        // Path 2: real decline produced (insufficient-evidence path). Per task 071, the
        // cache now extracts DeclineToFindNode output from the engine stream and surfaces
        // it here with structured MinimumEvidenceNeeded gap analysis from upstream
        // EvidenceSufficiencyNode — no more scaffold "no-artifact-produced" for this case.
        // CacheHit is ALWAYS false on the decline path because declines are never cached
        // (evidence sufficiency depends on the current state of the index).
        if (runResult.HasDecline)
        {
            _logger.LogDebug(
                "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced decline (reason={Reason}, gaps={GapCount}, elapsedMs={ElapsedMs})",
                request.Question, request.Subject, runResult.Decline!.Reason,
                runResult.Decline.MinimumEvidenceNeeded.Count, elapsedMs);
            return InsightsAgentResult.Declined(runResult.Decline, cacheHit: false, elapsedMs);
        }

        // Path 3 (defensive): engine produced neither artifact nor decline. This should not
        // happen with a well-formed playbook (EvidenceSufficiencyNode's branch routing
        // guarantees exactly one terminal node fires). If it does — malformed playbook,
        // engine error masked, branch routing bug — log Warning and emit a scaffold decline
        // so the facade contract's "exactly one of artifact/decline" invariant holds for
        // Zone B callers. The scaffold's ConfidenceInDecline = 0.0 signals "this is not a
        // real decline verdict" to observability tooling.
        _logger.LogWarning(
            "InsightsOrchestrator AnswerQuestionAsync: playbook {PlaybookId} subject {Subject} produced NO artifact AND NO decline; engine may be misconfigured. Returning scaffold decline. (cacheHit={CacheHit}, elapsedMs={ElapsedMs})",
            request.Question, request.Subject, cacheHit, elapsedMs);

        return InsightsAgentResult.Declined(
            new DeclineResponse
            {
                Reason = "no-artifact-produced",
                Explanation = "The playbook completed without emitting an InsightArtifact or DeclineResponse. This indicates a malformed playbook (missing terminal node), an engine error masked by node validation, or a branch-routing bug. Operations: inspect the playbook's terminal nodes (ReturnInsightArtifactNode + DeclineToFindNode) and EvidenceSufficiencyNode branch wiring.",
                MinimumEvidenceNeeded = new Dictionary<string, object>(),
                SuggestedActions = Array.Empty<string>(),
                ConfidenceInDecline = 0.0 // Sentinel: not a real decline verdict
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

        // Task 040 (D-P7) delegates to IIngestOrchestrator, which composes the universal
        // ingest pipeline (Sanitizer → Layer 1 → conditional Layer 2 → mechanical gates →
        // emission → substrate write → mirror). The facade stays thin so Zone B callers
        // (D-P8 SPE-upload consumer per task 050) see a stable IInsightsAi contract while
        // the Zone A pipeline composition evolves freely.
        return _ingestOrchestrator.RunAsync(request, cancellationToken);
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
