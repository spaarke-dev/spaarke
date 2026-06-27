using System.Security.Claims;

namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Request to <see cref="Services.Ai.PublicContracts.IInsightsAi.AssistantQueryAsync"/> —
/// the unified Spaarke Assistant tool-call entry point (D-P15-06 / FR-05, Wave E3 task 042).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this is separate from <see cref="InsightsAgentRequest"/> + <see cref="InsightsSearchFacadeRequest"/></b>:
/// the Assistant tool surface is unified — the BFF makes the routing decision (classifier
/// OR <see cref="ForceMode"/> override) internally and routes to playbook OR RAG. This
/// request shape carries the raw NL query + subject + optional override; the facade
/// does the routing.
/// </para>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — composed entirely of primitives /
/// well-known framework types (<see cref="ClaimsPrincipal"/>). Used by the
/// <c>POST /api/insights/assistant/query</c> endpoint (Zone B) when constructing the
/// facade call.
/// </para>
/// <para>
/// <b>Contract anchor</b>: `projects/ai-spaarke-insights-engine-r2/design-e3-tool-call-contract.md` §3.
/// </para>
/// </remarks>
/// <param name="Query">Natural-language search query. Required, non-whitespace.</param>
/// <param name="ParentEntityType">Subject scheme parsed from the wire <c>subject</c>
/// field (e.g., <c>matter</c>, <c>project</c>, <c>invoice</c>). Required.</param>
/// <param name="ParentEntityId">Subject id (GUID string) parsed from the wire
/// <c>subject</c> field. Required.</param>
/// <param name="Subject">The original scheme-prefixed subject (e.g.,
/// <c>"matter:{guid}"</c>). Passed through to the playbook path which needs it for
/// template substitution. Required.</param>
/// <param name="ForceMode">Optional Assistant-supplied intent override per contract §3.2.
/// Values: <c>"playbook"</c> | <c>"rag"</c> | null. When null the facade invokes the
/// classifier; when set the facade skips the classifier and dispatches directly.</param>
/// <param name="ConversationId">Optional Assistant conversation identifier — Phase 1.5
/// telemetry only; not used for state. May be null.</param>
/// <param name="PreviousTurnSummary">Optional ≤2000-char summary of prior conversation
/// turns — Phase 1.5 telemetry + classifier prompt hint only. May be null.</param>
/// <param name="TenantId">Tenant identifier from the caller's <c>tid</c> claim per
/// ADR-014. Required.</param>
/// <param name="CallerOid">Caller user's object id from the <c>oid</c> claim. Used for
/// playbook cache key derivation (AccessibleScopeHash). Required.</param>
/// <param name="CallerPrincipal">The caller's <see cref="ClaimsPrincipal"/> forwarded
/// to RAG search for AIPU2-027 privilege-group filtering. Required for RAG path; null
/// is rejected by the underlying RAG service.</param>
public sealed record AssistantQueryFacadeRequest(
    string Query,
    string ParentEntityType,
    string ParentEntityId,
    string Subject,
    string? ForceMode,
    string? ConversationId,
    string? PreviousTurnSummary,
    string TenantId,
    string CallerOid,
    ClaimsPrincipal? CallerPrincipal);
