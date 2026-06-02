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
}
