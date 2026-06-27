using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Result returned from <see cref="Services.Ai.PublicContracts.IInsightsAi.AssistantQueryAsync"/>
/// — the unified Spaarke Assistant tool-call response (Wave E3 task 042).
/// </summary>
/// <remarks>
/// <para>
/// <b>Shape per contract §4</b>: carries the path actually taken (playbook OR rag), a
/// uniform user-facing answer, a uniform citation array, plus the rich
/// <see cref="StructuredEnvelopeJson"/> envelope (artifact/decline/observation) that
/// the Assistant can render for power-user views. Diagnostics carry routing telemetry.
/// </para>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — primitives + JSON-as-string for the
/// structured envelope (avoids leaking domain types into the Zone B surface). The
/// endpoint serializes <see cref="StructuredEnvelopeJson"/> directly into the response
/// body without re-deserialising.
/// </para>
/// </remarks>
public sealed record AssistantQueryFacadeResult
{
    /// <summary>The dispatch path taken: <c>"playbook"</c> or <c>"rag"</c>.</summary>
    public required string Path { get; init; }

    /// <summary>User-facing answer text. On the playbook decline path this is the
    /// <c>Explanation</c>; on the artifact path this is a plain-text rendering of
    /// the inference; on the RAG path this is the LLM-synthesized grounded summary
    /// (with <c>[n]</c> tokens indexing into <see cref="Citations"/>).</summary>
    public required string Answer { get; init; }

    /// <summary>Unified citation array. On the RAG path: derived from
    /// <c>InsightsSearchFacadeResult.Results</c>. On the playbook path: derived from
    /// the playbook's <c>InsightArtifact.EvidenceRefs</c> when available; empty
    /// otherwise. Always 1-based via <see cref="AssistantQueryCitation.N"/>.</summary>
    public IReadOnlyList<AssistantQueryCitation> Citations { get; init; } = [];

    /// <summary>Confidence in the answer (0.0–1.0). RAG: top hit relevance score.
    /// Playbook artifact: 1.0. Playbook decline: <c>1 - ConfidenceInDecline</c>.</summary>
    public double Confidence { get; init; }

    /// <summary>Canonical playbook name (e.g., <c>predict-matter-cost@v1</c>) when
    /// <see cref="Path"/> = <c>"playbook"</c>; null on the RAG path.</summary>
    public string? PlaybookId { get; init; }

    /// <summary>Structured envelope kind: <c>"inference"</c>, <c>"decline"</c>, or
    /// <c>"observation"</c>. Lets the Assistant pick a renderer.</summary>
    public required string StructuredKind { get; init; }

    /// <summary>JSON serialization of the rich envelope (full <c>InsightArtifact</c>,
    /// <c>DeclineResponse</c>, or RAG <c>{results, summary}</c>). The endpoint emits
    /// this as a JSON sub-object without re-parsing. Empty <c>"{}"</c> when no
    /// envelope is available.</summary>
    public string StructuredEnvelopeJson { get; init; } = "{}";

    /// <summary>Routing-decision telemetry — <c>"classifier"</c>,
    /// <c>"forceMode"</c>, or <c>"classifier-fallback"</c> (classifier returned
    /// below-threshold, fell back to RAG).</summary>
    public required string IntentSource { get; init; }

    /// <summary>True when the classifier returned a path but <see cref="Confidence"/>
    /// was below the configured threshold and the facade fell back to RAG per
    /// FR-05 safety. False when classifier was not invoked or returned above
    /// threshold.</summary>
    public bool ClassifierBelowThreshold { get; init; }

    /// <summary>Total wall-clock processing time in milliseconds, measured by the
    /// facade from request acceptance through response production.</summary>
    public long DurationMs { get; init; }

    /// <summary>True when the playbook D-P13 cache served the answer; false on
    /// RAG path or playbook miss.</summary>
    public bool CacheHit { get; init; }

    /// <summary>Number of RAG retrievals when <see cref="Path"/> = <c>"rag"</c>;
    /// number of citations on the playbook path (0 typical for Phase 1.5).</summary>
    public int HitCount { get; init; }
}

/// <summary>
/// A single citation entry in <see cref="AssistantQueryFacadeResult.Citations"/>.
/// </summary>
/// <param name="N">1-based citation index — matches the <c>[n]</c> token in
/// <see cref="AssistantQueryFacadeResult.Answer"/>.</param>
/// <param name="Source">Display name of the source document/observation.</param>
/// <param name="Excerpt">Content snippet suitable for citation display (≤280 chars).</param>
/// <param name="ObservationId">Source document/observation id when linked to a
/// Dataverse record; null for orphan chunks or playbook citations without record link.</param>
/// <param name="ChunkId">Chunk identifier from the underlying index when available;
/// null on the playbook path when the citation source is a playbook-internal evidence ref.</param>
/// <param name="Href">Optional clickable URL for citation source preview (Wave F task 052
/// / contract v1.1 §3). When non-null, points to the existing BFF preview endpoint
/// (<c>GET /api/documents/{sprk_document-guid}/preview</c>) — auth enforced via OBO so
/// the URL itself returns 403 for users lacking ACL access (AIPU2-027 privilege filtering).
/// Null when the citation source cannot be addressed (orphan chunk; playbook evidence in
/// <c>spe://drive/X/item/Y</c> form not yet resolvable to sprk_document Guid in v1.1 —
/// deferred to v1.2; or <see cref="AssistantCitationHrefOptions.BffBaseUrl"/> unconfigured).
/// v1.0 clients ignore unknown fields per contract §3.5 back-compat.</param>
public sealed record AssistantQueryCitation(
    int N,
    string Source,
    string Excerpt,
    string? ObservationId,
    string? ChunkId,
    [property: JsonPropertyName("href")] string? Href = null);
