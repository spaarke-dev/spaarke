using Sprk.Bff.Api.Models.Memory;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// R6 Pillar 7 (task 066, D-C-19) — selective recall contract.
///
/// <para>
/// At chat-turn build time, when the user's matter has more pinned-context items than fit
/// inside the NFR-10 8K system-prompt budget, the caller (task 067 hierarchical memory
/// composition) asks this service to rank the available pins by relevance to the current
/// user message and return the top-K most similar pins.
/// </para>
///
/// <para>
/// Ranking strategy: cosine similarity between the embedding of the current user message
/// and the embedding of each pin's <see cref="PinnedContextItem.Content"/>. Embeddings
/// are computed via the existing <see cref="IEmbeddingCache"/> + <see cref="IOpenAiClient"/>
/// pipeline (per the spec FR-43 "use the existing IEmbeddingCache infrastructure" rule —
/// no new embedding service is introduced).
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <b>Boundary</b>: this service is internal to the chat memory pipeline. CRUD-side code
/// MUST NOT depend on it (ADR-013 §A.4 — no PublicContracts facade required because the
/// service is consumed only by AI-internal callers — i.e., task 067 hierarchical memory
/// composition). It lives under <see cref="Sprk.Bff.Api.Services.Ai.Memory"/> per the
/// Memory tree convention.
/// </para>
/// <para>
/// <b>ADR-014 invariant</b>: <paramref name="tenantId"/> is the partition key on every
/// downstream <see cref="IPinnedContextRepository"/> call. Cross-tenant reads are
/// structurally impossible because the underlying Cosmos query is partition-scoped.
/// Embedding cache keys are content-hashed and tenant-agnostic by design (the same
/// content produces the same vector regardless of tenant), which is safe because no PII
/// is encoded in the hash itself.
/// </para>
/// <para>
/// <b>ADR-015 invariant</b>: pin content is user-authored memory; this service does NOT
/// log content bodies — only deterministic identifiers (tenantId, matterId, pinId) and
/// counts appear in telemetry.
/// </para>
/// <para>
/// <b>Soft-failure contract</b>: the service returns an EMPTY list (NOT a thrown
/// exception) when (a) the kill switch is off, (b) no pins exist for the matter,
/// (c) the user-message embedding cannot be computed (LLM circuit broken). Per-pin
/// embedding failures are logged but do not fail the whole call — the affected pin is
/// simply omitted from the ranking. The caller (task 067) treats an empty result as
/// "no selective recall available — proceed with the unranked pin set or skip recall".
/// </para>
/// <para>
/// <b>NFR-10 budget interaction</b>: <paramref name="topK"/> bounds the output cardinality;
/// the caller (task 067) sums TopK pins + summarized turns + recent verbatim window when
/// composing the system prompt and keeps the total ≤8K tokens.
/// </para>
/// </remarks>
public interface IPinnedContextRecallService
{
    /// <summary>
    /// Returns the top-<paramref name="topK"/> pinned-context items for the matter, ranked
    /// by cosine similarity of their content embedding against the current user message
    /// embedding (descending — most similar first).
    /// </summary>
    /// <param name="tenantId">
    /// Tenant identifier — the Cosmos partition key (ADR-014 binding). Must be non-empty.
    /// </param>
    /// <param name="matterId">
    /// Dataverse <c>sprk_matter</c> id. Scopes the candidate pin set to pins bound to this
    /// matter (i.e., <see cref="IPinnedContextRepository.GetByMatterAsync"/> semantics).
    /// Must be non-empty.
    /// </param>
    /// <param name="userMessage">
    /// The current user message text whose embedding drives the similarity ranking. An
    /// empty / whitespace-only message returns an empty list (nothing to compare against).
    /// </param>
    /// <param name="topK">
    /// Maximum number of pins to return. Clamped to the bounds defined in
    /// <see cref="PinnedContextRecallOptions.TopK"/> (range [1, 20]). When the matter has
    /// fewer than <paramref name="topK"/> pins above the similarity threshold, fewer items
    /// are returned.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An ordered (most-similar-first) list of up to <paramref name="topK"/>
    /// <see cref="PinnedContextItem"/> objects whose cosine similarity to the user message
    /// embedding equals or exceeds <see cref="PinnedContextRecallOptions.SimilarityThreshold"/>.
    /// Returns an empty list when recall is disabled, no pins exist, or the user-message
    /// embedding could not be computed.
    /// </returns>
    Task<IReadOnlyList<PinnedContextItem>> RecallAsync(
        string tenantId,
        string matterId,
        string userMessage,
        int topK,
        CancellationToken cancellationToken = default);
}
