namespace Sprk.Bff.Api.Models.Ai.Chat;

/// <summary>
/// Represents an active or resumable chat session.
///
/// Lifecycle:
///   - Created by <see cref="Services.Ai.Chat.ChatSessionManager.CreateSessionAsync"/>.
///   - Hot copy cached in Redis at key <c>"chat:session:{TenantId}:{SessionId}"</c> (24h sliding TTL — NFR-07, ADR-009).
///   - Cold record stored in Dataverse as a <c>sprk_aichatsummary</c> entity (audit trail, session recovery).
///   - Messages are managed by <see cref="Services.Ai.Chat.ChatHistoryManager"/>:
///       - Summarisation triggers at 15 messages (NFR).
///       - Archive triggers at 50 messages (NFR-12).
/// </summary>
/// <param name="SessionId">
/// Unique session GUID.  Matches <c>sprk_sessionid</c> on <c>sprk_aichatsummary</c>.
/// Also the suffix of the Redis cache key.
/// </param>
/// <param name="TenantId">
/// Power Platform tenant ID.  Provides multi-tenant isolation.
/// Part of the Redis cache key and required on every Dataverse query.
/// </param>
/// <param name="DocumentId">
/// SPE document ID for document-context sessions.  Maps to <c>sprk_documentid</c>.
/// May be null for knowledge-only sessions.
/// </param>
/// <param name="PlaybookId">
/// Dataverse ID of the playbook governing this session's agent behaviour.
/// Maps to <c>sprk_playbookid</c> on <c>sprk_aichatsummary</c>.
/// </param>
/// <param name="CreatedAt">
/// UTC timestamp when the session was created.  Corresponds to <c>createdon</c>
/// on <c>sprk_aichatsummary</c>.
/// </param>
/// <param name="LastActivity">
/// UTC timestamp of the last message activity.  Used to determine whether the
/// 24-hour idle window (NFR-07) has elapsed when loading from cold storage.
/// </param>
/// <param name="Messages">
/// Ordered list of messages for this session (most recent up to the configured max).
/// This is the hot in-memory/Redis copy; Dataverse holds the authoritative audit trail.
/// </param>
public record ChatSession(
    string SessionId,
    string TenantId,
    string? DocumentId,
    Guid PlaybookId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity,
    IReadOnlyList<ChatMessage> Messages);
