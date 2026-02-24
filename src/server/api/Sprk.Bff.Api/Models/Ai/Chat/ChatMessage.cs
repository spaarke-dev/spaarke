namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Role of the author of a chat message.
/// Corresponds to the Dataverse global option set <c>sprk_aichatrole</c>
/// (option values: 726490000=User, 726490001=Assistant, 726490002=System).
/// </summary>
public enum ChatMessageRole
{
    /// <summary>Message authored by the end user.</summary>
    User = 726490000,

    /// <summary>Message authored by the AI agent (SprkChatAgent).</summary>
    Assistant = 726490001,

    /// <summary>System prompt message (typically one per session, at start).</summary>
    System = 726490002
}

/// <summary>
/// An individual message within a chat session.
///
/// Persisted to Dataverse as a <c>sprk_aichatmessage</c> record (cold storage / audit trail).
/// The hot copy is stored inside <see cref="ChatSession.Messages"/> in Redis.
/// </summary>
/// <param name="MessageId">
/// Unique identifier for this message.  Matches <c>sprk_name</c> (auto-number, e.g. "MSG-000001")
/// when persisted to Dataverse.
/// </param>
/// <param name="SessionId">
/// Foreign key to the owning session.  Matches <c>sprk_sessionid</c> on
/// <c>sprk_aichatmessage</c> and <c>sprk_aichatsummary</c>.
/// </param>
/// <param name="Role">Who authored this message (User / Assistant / System).</param>
/// <param name="Content">
/// The message text.  For assistant messages this is the fully-aggregated streaming response.
/// Maps to <c>sprk_content</c> (max 10 000 chars in Dataverse).
/// </param>
/// <param name="TokenCount">
/// Approximate token count for cost-tracking.  Optional — may be 0 when not measured.
/// Maps to <c>sprk_tokencount</c>.
/// </param>
/// <param name="CreatedAt">
/// UTC timestamp of message creation.  Corresponds to <c>createdon</c> in Dataverse
/// (auto-populated); stored here so the hot Redis copy retains the same value.
/// </param>
/// <param name="SequenceNumber">
/// Message order within the session.  Used to reconstruct ordered history when reloading
/// from Dataverse (<c>sprk_sequencenumber asc</c>).
/// </param>
public record ChatMessage(
    string MessageId,
    string SessionId,
    ChatMessageRole Role,
    string Content,
    int TokenCount,
    DateTimeOffset CreatedAt,
    int SequenceNumber = 0);
