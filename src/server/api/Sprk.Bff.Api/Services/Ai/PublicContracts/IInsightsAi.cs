using Sprk.Bff.Api.Models.Ai.PublicContracts;

namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Public facade for the Spaarke Insights Engine. The ONLY Zone-A surface Zone B code
/// is permitted to import per <c>SPEC §3.5</c> facade boundary.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists</b>: <c>SPEC §3.5.4</c> forbids Zone B paths
/// (<c>Services/Insights/**</c>, <c>Api/Insights/**</c>, <c>Models/Insights/**</c>,
/// <c>Services/Jobs/SpeUploadConsumer*</c>) from importing AI internals like
/// <see cref="IOpenAiClient"/>, <see cref="IPlaybookExecutionEngine"/>,
/// <c>Microsoft.Extensions.AI.*</c>, <c>Microsoft.SemanticKernel.*</c>,
/// <c>OpenAI.*</c>, <c>Azure.AI.OpenAI.*</c>, or any type under
/// <c>Services/Ai/Nodes/</c>, <c>Services/Ai/Chat/</c>, <c>Services/Ai/Insights/</c>
/// (except this <c>PublicContracts</c> sub-namespace). This interface is the bridge:
/// Zone B injects <see cref="IInsightsAi"/>, the implementation
/// (<c>InsightsOrchestrator</c>) lives in Zone A and is free to use AI internals.
/// </para>
/// <para>
/// <b>Phase 1 consumers</b> (verified via SPEC §3.5.4 grep at every PR):
/// <list type="bullet">
///   <item>D-P8 SPE-upload consumer (Zone B at dispatch boundary) — calls
///   <see cref="RunIngestAsync"/></item>
///   <item>D-P15 <c>POST /api/insights/ask</c> endpoint (Zone B API) — calls
///   <see cref="AnswerQuestionAsync"/></item>
///   <item>D-P4 Precedent projection sync (Zone B substrate) — calls
///   <see cref="EmbedTextAsync"/> to vectorize Precedent pattern statements before
///   write to <c>spaarke-insights-index</c>, without taking a hard dependency on
///   <see cref="IOpenAiClient"/></item>
/// </list>
/// </para>
/// <para>
/// <b>Naming convention</b>: methods are named for the <em>domain need</em>, not the
/// AI mechanism (per task 042 §constraint: <c>AnswerQuestionAsync</c>, NOT
/// <c>InvokePlaybookAsync</c>; <c>RunIngestAsync</c>, NOT <c>ExecuteIngestPlaybookAsync</c>;
/// <c>EmbedTextAsync</c>, NOT <c>GenerateEmbeddingAsync</c>). This keeps the facade stable
/// as the AI mechanism evolves underneath (e.g., switching from Azure OpenAI to a Foundry
/// agent for synthesis would not change the Zone B-visible signature).
/// </para>
/// <para>
/// <b>Mirrors the canonical facade pattern</b> from ADR-007 (<c>SpeFileStore</c>) and
/// <see cref="IBriefingAi"/>: narrow surface (only what real consumers call today),
/// SDAP-domain DTOs (no <c>ChatMessage</c> / OpenAI types leaked), single concrete
/// implementation behind the interface.
/// </para>
/// </remarks>
public interface IInsightsAi
{
    /// <summary>
    /// Answer an Insights-mode synthesis question (D-P14 + D-P15). Resolves the
    /// playbook by id, executes it (with the D-P13 cache wrapping
    /// <see cref="IPlaybookExecutionEngine.ExecuteBatchAsync"/>), and returns either
    /// the synthesized <see cref="Models.Insights.InsightArtifact"/> or a structured
    /// <see cref="Models.Insights.DeclineResponse"/> per D-49 when evidence is insufficient.
    /// </summary>
    /// <param name="request">Question identifier + subject + parameters + tenant + scope
    /// hash. See <see cref="InsightsAgentRequest"/> for field semantics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="InsightsAgentResult"/> carrying exactly one of
    /// <c>Artifact</c> or <c>Decline</c> (never both, never neither), plus diagnostic
    /// fields (<c>CacheHit</c>, <c>ProcessingTimeMs</c>) the endpoint may surface
    /// as response headers.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">When required string fields on
    /// <paramref name="request"/> are null/whitespace.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// is signalled before the result is produced.</exception>
    Task<InsightsAgentResult> AnswerQuestionAsync(
        InsightsAgentRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Run the universal ingest playbook (D-P7) for a single SPE-uploaded document.
    /// Layer 1 classifies; conditional Layer 2 extracts outcome fields; mechanical
    /// gates (D-P9 GroundingVerifier + D-P10 per-field confidence threshold) filter;
    /// per-field Observations persist to <c>spaarke-insights-index</c> and mirror to
    /// <c>sprk_analysis</c> for the review surface.
    /// </summary>
    /// <param name="request">Document + matter + tenant identifiers. The playbook
    /// reads document content from <c>spaarke-files-index</c> (already chunked); it
    /// does NOT re-fetch from SPE.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="InsightsIngestResult"/> reporting Observations emitted,
    /// the Layer 1 classification, and whether Layer 2 was triggered.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">When required string fields on
    /// <paramref name="request"/> are null/whitespace.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// is signalled before the playbook completes.</exception>
    Task<InsightsIngestResult> RunIngestAsync(
        InsightsIngestRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Generate a vector embedding for a string. Provided on the facade so Zone B
    /// projection-sync code (D-P4 Precedent projection per task 041) can vectorize
    /// pattern statements before writing to <c>spaarke-insights-index</c> WITHOUT
    /// taking a forbidden Zone B → <see cref="IOpenAiClient"/> dependency.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Design note (task 042 resolution of task 041 Step 3)</b>: task 041's
    /// <c>PrecedentProjectionSync</c> lives in Zone B and must vectorize
    /// Precedent pattern statements before write. Direct injection of
    /// <see cref="IOpenAiClient"/> is forbidden by SPEC §3.5.4. Without a facade
    /// path, the only options were (a) extract embedding to a separate Zone A
    /// service (extra interface + DI + module — overkill for one call site), or
    /// (b) restructure D-P4 to push embedding into a Zone A node (semantic mismatch —
    /// projection sync is a substrate write, not a playbook). Adding this third
    /// method to the facade is the lowest-friction resolution: a single signature
    /// that delegates to the existing <see cref="IOpenAiClient.GenerateEmbeddingAsync"/>,
    /// preserving the Zone-A → AI-internal call path while keeping Zone B clean.
    /// </para>
    /// <para>
    /// <b>Model + dimensions</b>: the implementation uses the configured
    /// <c>text-embedding-3-large</c> model at 3072 dimensions per the
    /// <c>spaarke-insights-index</c> schema (SPEC §3.4 / D-P2). Callers do not
    /// choose model/dimensions because the facade is opinionated for Insights Engine
    /// substrate consistency.
    /// </para>
    /// </remarks>
    /// <param name="text">Text to embed. Required, non-whitespace.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The embedding vector as a <see cref="ReadOnlyMemory{T}"/> of
    /// <see cref="float"/>. Length is the configured embedding dimensions (3072 for
    /// <c>spaarke-insights-index</c>).</returns>
    /// <exception cref="ArgumentException">When <paramref name="text"/> is null/whitespace.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// is signalled before the embedding is produced.</exception>
    Task<ReadOnlyMemory<float>> EmbedTextAsync(
        string text,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Hybrid RAG retrieval + LLM synthesis path through the Insights Engine (D-P15-06 /
    /// FR-04, Wave E task 040). Runs semantic + vector search over
    /// <c>spaarke-insights-index</c> filtered by subject scope (and optional artifact
    /// type / predicate), then LLM-synthesizes a grounded summary citing the top
    /// retrievals.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>How this differs from <see cref="AnswerQuestionAsync"/></b>: <c>AnswerQuestion</c>
    /// invokes a <em>pre-authored</em> JPS playbook (structured output, evidence-sufficiency
    /// gates, structured Decline). <c>Search</c> answers <em>open-ended</em> natural-language
    /// queries via generic RAG over the same substrate. The Spaarke Assistant intent
    /// classifier (Wave E2 task 041) routes between the two paths.
    /// </para>
    /// <para>
    /// <b>Zone B → Zone A boundary</b>: the endpoint
    /// (<c>POST /api/insights/search</c>) lives in <c>Api/Insights/</c> (Zone B per
    /// SPEC §3.5.4). It MUST NOT import <c>IRagService</c> directly because
    /// <c>Services/Ai/*</c> is forbidden in Zone B. This facade method is the bridge:
    /// the orchestrator (Zone A) holds <c>IRagService</c> + <c>IOpenAiClient</c> and
    /// performs both retrieval and synthesis.
    /// </para>
    /// <para>
    /// <b>Kill-switch behavior</b>: when the AI kill-switch is OFF, <c>NullRagService</c>
    /// (ADR-032 P3) throws <see cref="Configuration.FeatureDisabledException"/> with
    /// <c>ErrorCode = "ai.rag.disabled"</c>. The orchestrator does NOT catch this — it
    /// propagates to the endpoint which converts to 503 ProblemDetails via the shared
    /// <c>AsFeatureDisabled503()</c> helper. See <c>NullRagService</c> + <c>ADR-032</c>.
    /// </para>
    /// <para>
    /// <b>Empty-result behavior</b>: when retrieval returns zero hits (after privilege
    /// + subject filtering), the result's <c>Results</c> list is empty and
    /// <c>Summary</c> is the empty string — the orchestrator does NOT fabricate a
    /// summary without grounding. The endpoint surfaces this as a 200 OK with an empty
    /// envelope so the caller can render a "no results found" message client-side.
    /// </para>
    /// </remarks>
    /// <param name="request">Search request — query + subject + optional filters +
    /// caller principal. See <see cref="InsightsSearchFacadeRequest"/> for field
    /// semantics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>An <see cref="InsightsSearchFacadeResult"/> carrying the ranked hits,
    /// the LLM-synthesized summary with grounded <c>[n]</c> citations, and total
    /// wall-clock duration.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">When required string fields on
    /// <paramref name="request"/> are null/whitespace.</exception>
    /// <exception cref="Configuration.FeatureDisabledException">When the AI kill-switch
    /// is OFF (propagated unchanged from <c>NullRagService</c> per ADR-032 P3); the
    /// endpoint converts this to 503 ProblemDetails.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// is signalled before the result is produced.</exception>
    Task<InsightsSearchFacadeResult> SearchAsync(
        InsightsSearchFacadeRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unified Spaarke Assistant tool-call entry point (Wave E3 task 042 / FR-05). Takes a
    /// natural-language query + subject + optional forceMode override and routes through
    /// the Wave E2 intent classifier OR directly to the playbook / RAG path depending on
    /// caller-declared intent. Returns a uniform <see cref="AssistantQueryFacadeResult"/>
    /// the Assistant can render without knowing which underlying path executed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Contract anchor</b>: <c>projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md</c>
    /// — the canonical Spaarke Assistant ↔ Insights tool-call contract. This facade method
    /// is the binding BFF-side implementation surface for that contract; the wire-level
    /// endpoint (<c>POST /api/insights/assistant/query</c>) is a thin shell over this call.
    /// </para>
    /// <para>
    /// <b>Why this is on the facade (not the endpoint directly)</b>: the Assistant tool-call
    /// orchestration is Zone A logic — it consumes the intent classifier
    /// (<c>IInsightsIntentClassifier</c>), the playbook path (<c>AnswerQuestionAsync</c>),
    /// and the RAG path (<c>SearchAsync</c>), all of which are AI internals. The Zone B
    /// endpoint sees ONLY this facade method per SPEC §3.5.4.
    /// </para>
    /// <para>
    /// <b>Routing decision</b> (per contract §3.2):
    /// <list type="bullet">
    ///   <item><c>ForceMode == null</c>: invoke classifier; if BelowThreshold → RAG fallback;
    ///   else dispatch per classifier's <c>Path</c>.</item>
    ///   <item><c>ForceMode == "playbook"</c>: skip classifier; resolve playbook id via
    ///   classifier hint OR <c>Insights:Playbooks:DefaultName</c>; invoke playbook path.</item>
    ///   <item><c>ForceMode == "rag"</c>: skip classifier; invoke RAG path directly.</item>
    /// </list>
    /// </para>
    /// <para>
    /// <b>Kill-switch behavior</b> (per contract §7 matrix): the implementation does NOT
    /// catch <see cref="Configuration.FeatureDisabledException"/> — it propagates unchanged
    /// from the underlying classifier / playbook / RAG service. The endpoint converts to
    /// 503 ProblemDetails via <c>AsFeatureDisabled503()</c> with the correct <c>errorCode</c>
    /// (<c>ai.insights.disabled</c>, <c>ai.rag.disabled</c>, or <c>ai.intent-classification.disabled</c>).
    /// </para>
    /// </remarks>
    /// <param name="request">Assistant tool-call request — query + subject + optional
    /// forceMode override + tenant + caller principal. See
    /// <see cref="AssistantQueryFacadeRequest"/> for field semantics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="AssistantQueryFacadeResult"/> carrying the chosen path,
    /// uniform answer + citations, the rich structured envelope (JSON), and routing
    /// diagnostics.</returns>
    /// <exception cref="ArgumentNullException">When <paramref name="request"/> is null.</exception>
    /// <exception cref="ArgumentException">When required string fields on
    /// <paramref name="request"/> are null/whitespace.</exception>
    /// <exception cref="Configuration.FeatureDisabledException">When a required kill-switch
    /// is OFF (propagated from underlying services per ADR-032 P3); the endpoint converts
    /// this to 503 ProblemDetails.</exception>
    /// <exception cref="InvalidOperationException">When <c>ForceMode == "playbook"</c> AND
    /// no default playbook is configured (deployment misconfiguration). Endpoint converts
    /// to 503 with <c>errorCode = "ai.assistant-default-playbook.unconfigured"</c>.</exception>
    /// <exception cref="OperationCanceledException">When <paramref name="cancellationToken"/>
    /// is signalled.</exception>
    Task<AssistantQueryFacadeResult> AssistantQueryAsync(
        AssistantQueryFacadeRequest request,
        CancellationToken cancellationToken = default);
}
