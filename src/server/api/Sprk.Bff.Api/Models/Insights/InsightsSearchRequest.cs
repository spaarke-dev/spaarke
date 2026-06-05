using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Insights;

/// <summary>
/// Wire request body for <c>POST /api/insights/search</c> (D-P15-06 / FR-04, Wave E
/// task 040).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B POCO per SPEC §3.5</b> — pure record with DataAnnotations validation, no
/// AI-internals imports. The endpoint handler parses <see cref="Subject"/> via
/// <c>ISubjectParser</c> (Wave D5) into scheme + GUID, then constructs
/// <c>InsightsSearchFacadeRequest</c> for the <see cref="IInsightsAi.SearchAsync"/>
/// call. <see cref="TenantId"/> is NOT on the wire — it is always derived from the
/// authenticated principal's <c>tid</c> claim inside the handler so a caller cannot
/// spoof it.
/// </para>
/// <para>
/// <b>Subject scheme support</b>: unlike the <c>/api/insights/ask</c> endpoint (Phase 1
/// matter-only), <c>/api/insights/search</c> supports the full Wave D5 scheme set
/// (<c>matter:</c>, <c>project:</c>, <c>invoice:</c>). The endpoint uses
/// <c>SubjectParser</c>'s config-driven catalog so future schemes added in
/// <c>Insights:Subject:Schemes</c> work without an endpoint code change.
/// </para>
/// </remarks>
/// <param name="Query">Natural-language search query. Required, 1–500 chars.</param>
/// <param name="Subject">Scheme-prefixed subject ref (e.g., <c>matter:{guid}</c>,
/// <c>project:{guid}</c>, <c>invoice:{guid}</c>). Required.</param>
/// <param name="Top">Optional max ranked results to return. Clamped to [1, 20] by the
/// endpoint; default 10. Null treated as default.</param>
/// <param name="Filter">Optional filter sub-object — artifact type and predicate
/// constraints. Null when no filtering requested.</param>
/// <param name="ForceMode">Optional Wave E2 (FR-05) caller-side intent override. Accepted
/// values: <c>"playbook"</c> | <c>"rag"</c> | null. When set to <c>"rag"</c> on this
/// endpoint, the endpoint proceeds as today (this is the canonical RAG dispatch endpoint;
/// the field is accepted for symmetric API ergonomics with <c>/api/insights/ask</c> and
/// for Wave E3 Spaarke Assistant routing). When set to <c>"playbook"</c> on this endpoint,
/// the endpoint returns 400 ProblemDetails — callers MUST switch endpoints to invoke a
/// playbook (E3 Assistant handles the cross-endpoint dispatch on the caller's behalf). The
/// field exists in the Wave E2 wire surface for forward-compat; the classifier itself is
/// not yet invoked from this endpoint in E2 (reserved for E3 Assistant integration).</param>
public sealed record InsightsSearchRequest(
    [Required, StringLength(500, MinimumLength = 1)] string? Query,
    [Required, StringLength(256, MinimumLength = 1)] string? Subject,
    int? Top,
    InsightsSearchFilter? Filter,
    [StringLength(16)] string? ForceMode = null);

/// <summary>
/// Optional filter clause on <see cref="InsightsSearchRequest"/>.
/// </summary>
/// <param name="ArtifactType">Optional artifact-type filter (maps to index
/// <c>documentType</c> field). Examples: <c>contract</c>, <c>policy</c>,
/// <c>observation</c>.</param>
/// <param name="Predicate">Optional predicate filter — restricts to chunks tagged with
/// the given Insights predicate (e.g., <c>predictedCost</c>, <c>governingLaw</c>).
/// Implemented as a RequiredTags constraint on the index <c>tags</c> field.</param>
public sealed record InsightsSearchFilter(
    [StringLength(128)] string? ArtifactType,
    [StringLength(128)] string? Predicate);

/// <summary>
/// Wire response body for <c>POST /api/insights/search</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Citation contract</b>: <see cref="Summary"/> contains <c>[n]</c> citation tokens
/// where <c>n</c> is the 1-based index into <see cref="Results"/>. The UI renders these
/// as clickable references to the corresponding result item.
/// </para>
/// <para>
/// <b>Empty-results behavior</b>: when retrieval returns zero hits (post privilege +
/// subject filtering), <see cref="Results"/> is empty and <see cref="Summary"/> is the
/// empty string. The endpoint returns 200 OK so the caller can render a "no results
/// found" message client-side; the orchestrator deliberately does NOT fabricate a
/// summary without grounding.
/// </para>
/// <para>
/// <b>Observability headers</b> the endpoint sets alongside this body:
/// <list type="bullet">
///   <item><c>X-Insights-Elapsed-Ms: N</c> — orchestrator-measured wall time</item>
///   <item><c>X-Insights-Hit-Count: N</c> — result count for quick log scanning</item>
/// </list>
/// </para>
/// </remarks>
public sealed record InsightsSearchResponse(
    string Query,
    IReadOnlyList<InsightsSearchResultItem> Results,
    string Summary,
    long DurationMs);

/// <summary>
/// A single ranked retrieval result in <see cref="InsightsSearchResponse.Results"/>.
/// </summary>
/// <param name="ChunkId">Chunk identifier from <c>spaarke-insights-index</c>.</param>
/// <param name="ObservationId">Source observation / document id when the chunk is linked
/// to a Dataverse record; null for orphan chunks.</param>
/// <param name="DocumentName">Display name of the source document/observation.</param>
/// <param name="Snippet">Content snippet (or semantic-caption highlight) for citation
/// display.</param>
/// <param name="Predicate">Insights predicate tag (e.g., <c>predictedCost</c>) when
/// available; null otherwise.</param>
/// <param name="Confidence">Combined relevance score (0.0–1.0).</param>
public sealed record InsightsSearchResultItem(
    string ChunkId,
    string? ObservationId,
    string DocumentName,
    string Snippet,
    string? Predicate,
    double Confidence);
