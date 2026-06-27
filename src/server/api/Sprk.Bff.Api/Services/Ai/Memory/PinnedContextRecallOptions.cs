using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Services.Ai.Memory;

/// <summary>
/// Configuration options for <see cref="PinnedContextRecallService"/> — the R6 Pillar 7
/// (task 066, D-C-19) selective recall service that ranks the user's pinned-context items
/// by embedding-cosine similarity to the current user message and returns the top-K most
/// relevant items for injection into the LLM system prompt.
/// </summary>
/// <remarks>
/// <para>
/// Bound from appsettings section <c>PinnedContextRecall</c> via
/// <c>services.AddOptions&lt;PinnedContextRecallOptions&gt;().BindConfiguration(...)</c>
/// inside <see cref="Sprk.Bff.Api.Infrastructure.DI.AnalysisServicesModule"/>.
/// </para>
/// <para>
/// <b>B-G11 hardening pattern</b> (per CLAUDE.md §10 + <see cref="SummarizationCompressionOptions"/>
/// precedent): fields conditional on <see cref="Enabled"/> are NOT decorated with
/// <c>[Required]</c>. Validation happens at use-site (inside
/// <see cref="PinnedContextRecallService.RecallAsync"/>) so the app starts cleanly when
/// <c>Enabled=false</c> and no recall-specific config is present.
/// </para>
/// <para>
/// <b>NFR-10 invariant</b>: the 8K total system-prompt budget is the binding ceiling.
/// This service's contribution to that budget is bounded by <see cref="TopK"/> (default 5)
/// times the per-pin cap (1000 chars enforced by <see cref="PinnedContextRepository.MaxContentLength"/>);
/// the caller (task 067 hierarchical memory composition) is responsible for the final
/// budget arithmetic.
/// </para>
/// </remarks>
public sealed class PinnedContextRecallOptions
{
    /// <summary>Configuration section name used for binding.</summary>
    public const string SectionName = "PinnedContextRecall";

    /// <summary>
    /// Kill switch. When <c>false</c>, <see cref="PinnedContextRecallService.RecallAsync"/>
    /// returns an empty list immediately without computing any embeddings — the caller
    /// (task 067) short-circuits selective recall and falls back to unranked pin
    /// retrieval. Default: <c>true</c> (selective recall on by default since FR-43 binds
    /// embedding-based recall as the canonical mechanism for budget-constrained pin
    /// selection).
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Maximum number of pins to return from <see cref="PinnedContextRecallService.RecallAsync"/>.
    /// The caller can override per-call; this option is the default ceiling. NFR-10 bounds
    /// the total system-prompt budget — task 067 (memory composition) sums TopK pins +
    /// summarized turns + recent verbatim window and stays ≤8K. Range [1, 20]; values
    /// outside this band are rejected by use-site clamping.
    /// </summary>
    [Range(1, 20)]
    public int TopK { get; init; } = 5;

    /// <summary>
    /// Cosine-similarity threshold below which a pin is filtered out (treated as "not
    /// relevant enough to surface"). Range [0.0, 1.0]; default 0.0 means "include any
    /// pin that has a computable embedding" (the natural default for v1 where the
    /// TopK cut-off is the primary relevance gate). Tighten to 0.6-0.75 once empirical
    /// recall quality is measurable.
    /// </summary>
    [Range(0.0, 1.0)]
    public double SimilarityThreshold { get; init; } = 0.0;

    /// <summary>
    /// Optional embedding-model deployment override (Azure OpenAI). When <c>null</c>, the
    /// service passes <c>null</c> to <see cref="IOpenAiClient.GenerateEmbeddingAsync"/>,
    /// which falls back to the configured default embedding deployment.
    /// </summary>
    public string? EmbeddingModelOverride { get; init; }

    /// <summary>
    /// Maximum number of pins the service will consider per <see cref="PinnedContextRecallService.RecallAsync"/>
    /// call. Defensive cap on the per-pin embedding cost — a pathological matter with
    /// thousands of pins would otherwise trigger thousands of cache-miss embedding calls.
    /// Range [10, 500]; default 100. When the matter has more than this many pins, the
    /// service ranks only the first <see cref="MaxPinsToRank"/> (insertion order from the
    /// repository) — task 067 may choose a smarter pre-filter in a follow-up.
    /// </summary>
    [Range(10, 500)]
    public int MaxPinsToRank { get; init; } = 100;
}
