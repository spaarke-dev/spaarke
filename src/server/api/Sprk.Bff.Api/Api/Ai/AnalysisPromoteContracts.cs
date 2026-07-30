namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Request body for <c>POST /api/ai/analysis/promote</c> (ai-advanced-capabilities-analysis-hub-r1
/// task 023, spec FR-07 — two-tier session model). Explicitly promotes an EXISTING loose session
/// into a new, named <c>sprk_analysis</c> — a bind on the session's existing <c>sprk_aichatsummary</c>
/// row (task-020 FK), NOT a new session mint (that is <c>AnalysisEndpoints.ForkAnalysis</c>, task 021).
/// </summary>
/// <param name="SessionId">
/// The <c>sprk_sessionid</c> of the loose session to promote. Required. The SAME session id
/// continues after promotion — no new session is created.
/// </param>
/// <param name="Name">Display name for the new <c>sprk_analysis</c> record. Required.</param>
/// <param name="DocumentId">
/// Optional GUID of the source <c>sprk_document</c> for the new Analysis. When omitted, the
/// session's own <see cref="Models.Ai.Chat.ChatSession.DocumentId"/> is used (falls back to a 400
/// when neither is available — <c>sprk_analysis</c> is document-anchored, mirroring
/// <see cref="CreateAnalysisRequest"/> / <see cref="AnalysisForkRequest"/>).
/// </param>
/// <param name="PlaybookId">
/// Optional playbook GUID applied to the new Analysis. When omitted, the session's own
/// <see cref="Models.Ai.Chat.ChatSession.PlaybookId"/> is used.
/// </param>
public record AnalysisPromoteRequest(
    string SessionId,
    string Name,
    Guid? DocumentId = null,
    Guid? PlaybookId = null);

/// <summary>
/// Response body for <c>POST /api/ai/analysis/promote</c> (201 Created). The client keeps using
/// <see cref="SessionId"/> unchanged — promotion binds the existing session, it does not replace it.
/// </summary>
/// <param name="AnalysisId">GUID of the newly created <c>sprk_analysis</c> record.</param>
/// <param name="SessionId">The (unchanged) session id that was promoted/bound.</param>
public record AnalysisPromoteResponse(
    Guid AnalysisId,
    string SessionId);
