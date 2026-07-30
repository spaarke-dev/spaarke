using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Api.Ai;

/// <summary>
/// Request body for <c>POST /api/ai/analysis/fork</c> (ai-advanced-capabilities-analysis-hub-r1
/// task 021 / UQ-1 Option B). Forks a running chat into a new Analysis + a new session bound to
/// that Analysis, archiving the prior session.
/// </summary>
/// <param name="PriorSessionId">
/// The <c>sprk_sessionid</c> of the session being forked from. It is snapshotted (its transcript
/// preserved in the Cosmos store-of-record) and archived. Required.
/// </param>
/// <param name="DocumentId">
/// GUID of the source <c>sprk_document</c> for the new Analysis (the new session inherits it).
/// Required and must be non-empty (mirrors <see cref="CreateAnalysisRequest"/>).
/// </param>
/// <param name="Name">Display name for the new <c>sprk_analysis</c> record. Required.</param>
/// <param name="PlaybookId">
/// Optional playbook GUID applied to BOTH the new Analysis and the new bound session.
/// </param>
/// <param name="HostContext">
/// Optional additional host context (workspace/page telemetry). The <c>EntityType</c>/<c>EntityId</c>
/// are IGNORED and overwritten by the fork so the new session binds to the newly-created Analysis
/// (<c>sprk_analysisoutput</c> + the new analysisId) — the server owns the FK binding, not the client.
/// </param>
public record AnalysisForkRequest(
    string PriorSessionId,
    Guid DocumentId,
    string Name,
    Guid? PlaybookId = null,
    ChatHostContext? HostContext = null);

/// <summary>
/// Response body for <c>POST /api/ai/analysis/fork</c> (201 Created). The client's only job is to
/// store <see cref="NewSessionId"/> as the active session and surface the fork warning.
/// </summary>
/// <param name="AnalysisId">GUID of the newly created <c>sprk_analysis</c> record.</param>
/// <param name="NewSessionId">The new server-minted session bound to <see cref="AnalysisId"/>.</param>
/// <param name="ArchivedSessionId">The prior session id that was snapshotted + archived.</param>
public record AnalysisForkResponse(
    Guid AnalysisId,
    string NewSessionId,
    string ArchivedSessionId);
