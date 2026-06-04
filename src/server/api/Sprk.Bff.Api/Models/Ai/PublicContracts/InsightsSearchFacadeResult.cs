namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Result returned from <see cref="Services.Ai.PublicContracts.IInsightsAi.SearchAsync"/>
/// — the hybrid RAG retrieval + LLM synthesis output (D-P15-06 / FR-04, Wave E task 040).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — composed entirely of primitives so the
/// <c>POST /api/insights/search</c> endpoint can serialise it without importing any
/// AI-internal types.
/// </para>
/// <para>
/// <b>Shape</b>: <see cref="Results"/> carries the ranked retrieval hits (one per index
/// chunk); <see cref="Summary"/> carries the LLM-synthesized answer with grounded
/// <c>[n]</c> citations whose <c>n</c> indexes into <see cref="Results"/> (1-based);
/// <see cref="DurationMs"/> reports total wall time for client-side observability
/// matching the <c>X-Insights-Elapsed-Ms</c> header convention from the
/// <c>/api/insights/ask</c> endpoint.
/// </para>
/// </remarks>
public sealed record InsightsSearchFacadeResult
{
    /// <summary>The original natural-language query.</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Ranked retrieval hits from <c>spaarke-insights-index</c>, after subject +
    /// optional artifact-type + predicate filtering, ordered by relevance score.</summary>
    public IReadOnlyList<InsightsSearchHit> Results { get; init; } = [];

    /// <summary>LLM-synthesized summary answering <see cref="Query"/> grounded in
    /// <see cref="Results"/>. Citations appear as <c>[n]</c> tokens where <c>n</c> is the
    /// 1-based index into <see cref="Results"/>. Empty string when <see cref="Results"/>
    /// is empty (no retrievals to ground against — endpoint surfaces this case explicitly
    /// rather than fabricating a summary).</summary>
    public string Summary { get; init; } = string.Empty;

    /// <summary>Total wall-clock processing time in milliseconds, measured by the
    /// orchestrator from request acceptance through summary production. Includes RAG
    /// search + LLM synthesis.</summary>
    public long DurationMs { get; init; }
}

/// <summary>
/// A single retrieval hit returned by <see cref="InsightsSearchFacadeResult.Results"/>.
/// </summary>
/// <param name="ChunkId">Chunk identifier from <c>spaarke-insights-index</c> (the index
/// document id). Use as a stable client-side key.</param>
/// <param name="ObservationId">The source document/observation id when the chunk is
/// linked to a Dataverse <c>sprk_document</c>; null for orphan chunks.</param>
/// <param name="DocumentName">Display name of the source document or observation.</param>
/// <param name="Snippet">Content snippet (or semantic-caption highlight) suitable for
/// citation display in the UI.</param>
/// <param name="Predicate">Insights predicate tag from the index when available (e.g.,
/// <c>predictedCost</c>, <c>governingLaw</c>). Maps from the chunk's first matching tag.
/// Null when no Insights-predicate tag is present.</param>
/// <param name="Confidence">Combined relevance score (0.0–1.0). Higher is more
/// relevant. Uses the semantic-ranking score when available, otherwise the hybrid
/// search score.</param>
public sealed record InsightsSearchHit(
    string ChunkId,
    string? ObservationId,
    string DocumentName,
    string Snippet,
    string? Predicate,
    double Confidence);
