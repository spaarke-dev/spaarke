using System.ComponentModel.DataAnnotations;

namespace Sprk.Bff.Api.Models.Insights;

/// <summary>
/// Wire request body for <c>POST /api/insights/ask</c> (D-P15, task 061).
/// </summary>
/// <remarks>
/// <para>
/// <b>Zone B POCO per SPEC §3.5</b> — pure record with DataAnnotations validation, no
/// AI-internals imports. The endpoint handler maps this onto
/// <c>Models/Ai/PublicContracts/InsightsAgentRequest</c> (the facade DTO) before
/// invoking <c>IInsightsAi.AnswerQuestionAsync</c>. The two-DTO split keeps the wire
/// shape intentionally simpler than the internal facade DTO:
/// <list type="bullet">
///   <item>Wire DTO supplies <c>question</c> as a parseable string — the handler
///   parses it to <see cref="System.Guid"/> for the facade.</item>
///   <item>Wire DTO has NO <c>tenantId</c> / <c>accessibleScopeHash</c> — both are
///   derived from the authenticated principal inside the handler (caller cannot
///   spoof them).</item>
/// </list>
/// </para>
/// <para>
/// <b>SPEC §3.4.3 illustrative example</b> shows
/// <c>{question:"predict-matter-cost", subject:"matter:M-NEW-0042"}</c>. The literal
/// string "predict-matter-cost" is illustrative — the canonical Phase 1 contract
/// requires <see cref="Question"/> to parse as a <see cref="System.Guid"/> (the
/// published Insights-mode playbook id, per task 042 facade contract +
/// <see cref="Models.Ai.PublicContracts.InsightsAgentRequest.Question"/>). A
/// question-name → playbook-id catalog is a Phase 1.5 enhancement; Phase 1 callers
/// pass the Guid directly. Invalid input returns 400 ProblemDetails per ADR-019.
/// </para>
/// <para>
/// <b>Subject</b> is a scheme-prefixed identifier; Phase 1 accepts
/// <c>matter:{guid|loose-id}</c>. Strict parsing reject anything else with 400.
/// </para>
/// </remarks>
/// <param name="Question">Insights-mode playbook id as a Guid string. Required.
/// In Phase 1 callers MUST supply the published playbook's Guid id. Surface naming
/// (e.g., "predict-matter-cost") is deferred to a Phase 1.5 question-name catalog.</param>
/// <param name="Subject">Scheme-prefixed subject ref the question is being asked about
/// (e.g., <c>matter:M-1234</c>, <c>matter:{guid}</c>). Required. Phase 1 accepts only
/// the <c>matter:</c> scheme.</param>
/// <param name="Parameters">Optional template parameters passed through to the playbook
/// for prompt substitution. Each value is rendered as a string at the prompt layer.</param>
public sealed record InsightAskRequest(
    [Required, StringLength(64, MinimumLength = 1)] string? Question,
    [Required, StringLength(256, MinimumLength = 1)] string? Subject,
    IReadOnlyDictionary<string, string>? Parameters
);

/// <summary>
/// Wire response body for <c>POST /api/insights/ask</c> (D-P15, task 061).
/// </summary>
/// <remarks>
/// <para>
/// Carries exactly one of <see cref="Artifact"/> (success path) or
/// <see cref="Decline"/> (D-49 structured insufficient-evidence path); never both,
/// never neither. Both branches return HTTP 200 OK because a decline is the
/// successful production of a structured "I can't defensibly answer this" — not an
/// error. ADR-019 ProblemDetails is reserved for true failures (validation, auth,
/// rate limit, internal error).
/// </para>
/// <para>
/// <b>Observability headers</b> the endpoint sets alongside this body:
/// <list type="bullet">
///   <item><c>X-Insights-Cache: true|false</c> — D-P13 cache outcome</item>
///   <item><c>X-Insights-Elapsed-Ms: N</c> — orchestrator-measured wall time</item>
/// </list>
/// </para>
/// </remarks>
/// <param name="Artifact">The synthesized <see cref="InsightArtifact"/> on the
/// success path. Null on decline.</param>
/// <param name="Decline">The structured <see cref="DeclineResponse"/> on the decline
/// path. Null on success.</param>
public sealed record InsightAskResponse(
    InsightArtifact? Artifact,
    DeclineResponse? Decline);
