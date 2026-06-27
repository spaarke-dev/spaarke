using System.Security.Claims;

namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Request to <see cref="Services.Ai.PublicContracts.IInsightsAi.SearchAsync"/> — the
/// hybrid RAG retrieval + LLM synthesis path through the Insights Engine (D-P15-06 /
/// FR-04, Wave E task 040). Companion to <see cref="InsightsAgentRequest"/> (playbook
/// synthesis path).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — the shape the
/// <c>POST /api/insights/search</c> endpoint constructs and passes through the facade.
/// Carries the natural-language query, the subject of inquiry, optional artifact-type +
/// predicate filters, and the auth context the underlying <c>IRagService</c> needs for
/// AIPU2-027 privilege filtering and ADR-014 tenant scoping.
/// </para>
/// <para>
/// <b>Why this is separate from <see cref="InsightsAgentRequest"/></b>: the playbook
/// path resolves <see cref="InsightsAgentRequest.Question"/> as a pre-authored
/// <c>sprk_analysisplaybook</c> Guid. The RAG path takes a free-form NL string and runs
/// generic retrieval over <c>spaarke-insights-index</c> — there is no playbook to
/// resolve. Keeping them distinct preserves wire clarity (no overloaded "question"
/// field) and lets the orchestrator branch implementations cleanly.
/// </para>
/// <para>
/// <b>Subject scoping</b>: <see cref="ParentEntityType"/> + <see cref="ParentEntityId"/>
/// map directly to <c>RagSearchOptions.ParentEntityType</c> + <c>ParentEntityId</c>
/// already shipped in the RAG facade. The endpoint parses the wire-side
/// <c>subject: "matter:&lt;guid&gt;"</c> form (via <c>ISubjectParser</c>) into the
/// scheme + GUID before constructing this request.
/// </para>
/// </remarks>
/// <param name="Query">Natural-language search query. Required, non-whitespace.</param>
/// <param name="ParentEntityType">Subject scheme (e.g., <c>matter</c>, <c>project</c>,
/// <c>invoice</c>) — drives the index <c>parentEntityType</c> filter. Required.</param>
/// <param name="ParentEntityId">Subject id (GUID string) — drives the index
/// <c>parentEntityId</c> filter. Required.</param>
/// <param name="ArtifactType">Optional artifact-type filter, mapped to the index
/// <c>documentType</c> field (e.g., <c>contract</c>, <c>policy</c>).</param>
/// <param name="Predicate">Optional predicate filter; maps to a single required tag on
/// the index <c>tags</c> field so callers can scope to e.g. <c>predictedCost</c>
/// Observations.</param>
/// <param name="TopK">Max ranked results to return after semantic-ranking. Clamped to
/// [1, 20] by the endpoint; default 10 per FR-04.</param>
/// <param name="TenantId">Tenant identifier from the caller's <c>tid</c> claim per
/// ADR-014. Required.</param>
/// <param name="CallerPrincipal">The caller's <see cref="ClaimsPrincipal"/> from
/// <c>HttpContext.User</c>, forwarded to the underlying <c>IRagService</c> for AIPU2-027
/// privilege-group filtering. Required — the endpoint MUST supply this; null is allowed
/// only for background/system calls (which currently do not use the search facade).</param>
/// <param name="ForceMode">Wave E2 (FR-05) caller-side intent override forwarded from the
/// <c>/api/insights/search</c> wire DTO. Accepted values: <c>"playbook"</c> | <c>"rag"</c>
/// | null. Today (Wave E2) this field is forward-compat only — the orchestrator's
/// <c>SearchAsync</c> implementation does NOT branch on it because the endpoint dispatch
/// already chose the RAG path. The future Wave E3 Spaarke Assistant integration uses this
/// field to bypass intent classification when the caller has already declared intent.</param>
public sealed record InsightsSearchFacadeRequest(
    string Query,
    string ParentEntityType,
    string ParentEntityId,
    string? ArtifactType,
    string? Predicate,
    int TopK,
    string TenantId,
    ClaimsPrincipal? CallerPrincipal,
    string? ForceMode = null);
