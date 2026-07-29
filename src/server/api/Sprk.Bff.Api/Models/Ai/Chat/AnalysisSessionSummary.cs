namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Lightweight session-summary projection for the "one Analysis → many sessions" query
/// (ai-advanced-capabilities-analysis-hub-r1 task 020, spec FR-05).
///
/// Returned by
/// <see cref="Services.Ai.Chat.IChatDataverseRepository.GetSessionsByAnalysisAsync"/> when
/// listing every chat session bound to a given <c>sprk_analysis</c> record via the
/// <c>sprk_aichatsummary.sprk_analysis</c> lookup FK (owner-created; verified present by
/// task 010). The grouping key is <see cref="SessionId"/> (== <c>sprk_sessionid</c>) — each
/// returned row already represents exactly one session, since
/// <see cref="Services.Ai.Chat.ChatDataverseRepository.CreateSessionAsync"/> writes exactly
/// one <c>sprk_aichatsummary</c> row per session at create time.
/// </summary>
/// <param name="SessionId">The session's <c>sprk_sessionid</c> — matches <see cref="ChatSession.SessionId"/>.</param>
/// <param name="MessageCount">The session's <c>sprk_messagecount</c> at last Dataverse sync.</param>
/// <param name="IsArchived">The session's <c>sprk_isarchived</c> flag.</param>
/// <param name="CreatedOn">The Dataverse <c>createdon</c> timestamp for the summary record, when present.</param>
public sealed record AnalysisSessionSummary(
    string SessionId,
    int MessageCount,
    bool IsArchived,
    DateTimeOffset? CreatedOn);
