namespace Sprk.Bff.Api.Models.Ai.PublicContracts;

/// <summary>
/// Request to <see cref="Services.Ai.PublicContracts.IInsightsAi.AnswerQuestionAsync"/> —
/// the synthesis path through the Insights Engine (D-P14 + D-P15).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B-importable DTO</b> per SPEC §3.5 — the request shape the D-P15 endpoint
/// constructs and passes through the facade. Carries the question identifier, the
/// subject of inquiry, free-form parameters for template substitution, and the two
/// fields needed by the D-P13 cache key (<see cref="TenantId"/> for telemetry
/// attribution + isolation; <see cref="AccessibleScopeHash"/> for within-tenant access
/// invalidation per DEP-3).
/// </para>
/// <para>
/// <b>What "Question" means here</b>: a published Insights-mode playbook identifier
/// (e.g., the Guid of <c>predict-matter-cost</c>). Per D-54 questions-as-playbooks —
/// there is no parallel question-catalog entity. The orchestrator resolves the
/// playbook by id and invokes the engine.
/// </para>
/// </remarks>
/// <param name="Question">Insights-mode playbook id (e.g., <c>predict-matter-cost</c>'s Guid).
/// The orchestrator resolves this to the playbook definition. Required.</param>
/// <param name="Subject">Scheme-prefixed subject identifier the question is being asked about
/// (e.g., <c>matter:M-1234</c>). Required.</param>
/// <param name="Parameters">Playbook parameters for template substitution. May be null or empty.</param>
/// <param name="TenantId">Tenant identifier (D-52 single-tenant). Required.</param>
/// <param name="AccessibleScopeHash">Hash of the caller's accessible-scope set per DEP-3.
/// Drives D-P13 cache-key invalidation when within-tenant access changes. Required.</param>
public sealed record InsightsAgentRequest(
    Guid Question,
    string Subject,
    IReadOnlyDictionary<string, string>? Parameters,
    string TenantId,
    string AccessibleScopeHash);
