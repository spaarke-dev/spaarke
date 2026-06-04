using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Models.Insights;

/// <summary>
/// Wire request body for <c>POST /api/insights/assistant/query</c> (Wave E3 task 042 /
/// FR-05) — the unified Spaarke Assistant tool-call entry point.
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B POCO per SPEC §3.5</b> — pure record with DataAnnotations validation, no
/// AI-internals imports. The endpoint parses <see cref="Subject"/> via
/// <c>ISubjectParser</c> (Wave D5) and constructs
/// <c>AssistantQueryFacadeRequest</c> for the facade call.
/// </para>
/// <para>
/// <b>Contract anchor</b>: <c>projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md</c> §3.
/// </para>
/// </remarks>
/// <param name="Query">Natural-language search query. Required, 1..500 chars.</param>
/// <param name="Subject">Scheme-prefixed subject ref. Required.</param>
/// <param name="ForceMode">Optional Assistant-supplied intent override per contract §3.2.
/// Accepted: <c>"playbook"</c> | <c>"rag"</c> | null. When null the BFF invokes the
/// classifier. When set the BFF skips the classifier and routes directly.</param>
/// <param name="ConversationContext">Optional Assistant-side conversation context. Phase 1.5
/// telemetry only.</param>
public sealed record InsightsAssistantQueryRequest(
    [Required, StringLength(500, MinimumLength = 1)] string? Query,
    [Required, StringLength(256, MinimumLength = 1)] string? Subject,
    [StringLength(16)] string? ForceMode = null,
    InsightsAssistantConversationContext? ConversationContext = null);

/// <summary>
/// Optional conversation-context envelope per contract §3.1. Phase 1.5 fields are
/// telemetry/correlation only; they do NOT alter BFF state.
/// </summary>
/// <param name="ConversationId">Assistant-side opaque conversation identifier.
/// Surfaced in BFF logs for cross-service correlation.</param>
/// <param name="PreviousTurnSummary">Optional ≤2000-char summary of prior conversation
/// turns. Phase 1.5: telemetry only. Phase 2: may inform classifier prompt or RAG
/// augmentation per contract §11.</param>
public sealed record InsightsAssistantConversationContext(
    [StringLength(128)] string? ConversationId,
    [StringLength(2000)] string? PreviousTurnSummary);

/// <summary>
/// Wire response body for <c>POST /api/insights/assistant/query</c>. Mirrors contract §4.
/// </summary>
/// <remarks>
/// <para>
/// <b>Uniform shape across paths</b>: regardless of whether the BFF dispatched to the
/// playbook or RAG path, the response has the same top-level fields. The Assistant
/// inspects <see cref="Path"/> + <see cref="StructuredResult"/>.Kind when it needs to
/// branch its rendering; the basic answer + citations are always uniformly addressable.
/// </para>
/// <para>
/// <b>Observability headers</b> the endpoint sets alongside this body:
/// <list type="bullet">
///   <item><c>X-Insights-Elapsed-Ms: N</c></item>
///   <item><c>X-Insights-Path: playbook | rag</c></item>
///   <item><c>X-Insights-Intent-Source: classifier | forceMode | classifier-fallback</c></item>
///   <item><c>X-Insights-Cache: true | false</c></item>
///   <item><c>X-Insights-Hit-Count: N</c> (RAG path)</item>
/// </list>
/// </para>
/// </remarks>
public sealed record InsightsAssistantQueryResponse(
    string Path,
    string Answer,
    IReadOnlyList<InsightsAssistantCitation> Citations,
    double Confidence,
    string? PlaybookId,
    InsightsAssistantStructuredResult StructuredResult,
    InsightsAssistantDiagnostics Diagnostics);

/// <summary>
/// A single citation entry in <see cref="InsightsAssistantQueryResponse.Citations"/>.
/// </summary>
/// <param name="N">1-based citation index — matches the <c>[n]</c> token in
/// <see cref="InsightsAssistantQueryResponse.Answer"/>.</param>
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
/// deferred to v1.2; or BffBaseUrl unconfigured for the environment).
/// v1.0 clients ignore unknown fields per contract §3.5 back-compat.</param>
public sealed record InsightsAssistantCitation(
    int N,
    string Source,
    string Excerpt,
    string? ObservationId,
    string? ChunkId,
    [property: JsonPropertyName("href")] string? Href = null);

/// <summary>
/// Rich structured envelope wrapper carried in
/// <see cref="InsightsAssistantQueryResponse.StructuredResult"/>. The
/// <see cref="Envelope"/> shape is path-specific — <c>inference</c> / <c>decline</c> for
/// the playbook path; <c>observation</c> (RAG results + summary) for the RAG path.
/// </summary>
/// <remarks>
/// <b>Envelope shape</b>: opaque <see cref="System.Text.Json.JsonElement"/> so the
/// Assistant can deserialise the appropriate concrete shape per <see cref="Kind"/>:
/// <list type="bullet">
///   <item><c>"inference"</c>: full <c>InsightArtifact</c> JSON (polymorphic with
///   <c>type</c> discriminator)</item>
///   <item><c>"decline"</c>: full <c>DeclineResponse</c> JSON</item>
///   <item><c>"observation"</c>: <c>{ results: [...], summary: "..." }</c> JSON</item>
/// </list>
/// </remarks>
public sealed record InsightsAssistantStructuredResult(
    string Kind,
    [property: JsonPropertyName("envelope")] System.Text.Json.JsonElement Envelope);

/// <summary>
/// Routing-decision telemetry per contract §4 field <c>diagnostics</c>.
/// </summary>
/// <param name="IntentSource">How routing was decided: <c>"classifier"</c>,
/// <c>"forceMode"</c>, or <c>"classifier-fallback"</c>.</param>
/// <param name="ClassifierBelowThreshold">True when classifier was invoked, returned
/// below-threshold, and the handler fell back to RAG per FR-05.</param>
/// <param name="ElapsedMs">Total BFF wall time including classifier + path execution.</param>
/// <param name="CacheHit">True when the playbook D-P13 cache served the answer; false on
/// RAG path or playbook miss.</param>
public sealed record InsightsAssistantDiagnostics(
    string IntentSource,
    bool ClassifierBelowThreshold,
    long ElapsedMs,
    bool CacheHit);
