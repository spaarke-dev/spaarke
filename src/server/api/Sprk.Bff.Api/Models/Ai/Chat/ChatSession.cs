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
/// <param name="AdditionalDocumentIds">
/// Optional list of additional document IDs (max 5) pinned to the conversation for
/// cross-referencing, comparison, or comprehensive analysis across multiple documents.
/// Persisted to Redis cache and Dataverse for session recovery.
/// </param>
/// <param name="UploadedFiles">
/// Optional manifest of files uploaded into THIS session by the end user (distinct from
/// <see cref="AdditionalDocumentIds"/>, which pins pre-existing SPE documents for
/// cross-reference). Each entry tracks a file the user actively uploaded for the
/// chat agent to ground on — see <see cref="ChatSessionFile"/> for the per-file shape.
///
/// Cap: hard-limited to <see cref="MaxUploadedFiles"/> (= 20) per session — uploads
/// beyond the cap MUST be rejected by the caller (spec NFR-02, project CLAUDE.md §3.8).
///
/// Lifecycle:
///   - Populated by chat-pane uploads (R5 task 020 frontend → upload endpoint).
///   - Consumed by session-scoped RAG retrieval (R5 task 002) and the Summarize
///     orchestrator (R5 task 014) for chat-driven summarization.
///   - Cleaned up aggressively on session end by the session-files cleanup
///     <c>IHostedService</c> (R5 task 007) — does NOT wait for the scheduled sweep.
///
/// Persistence: rides the existing triple-tier flow (Redis hot via
/// <see cref="System.Text.Json.JsonSerializer"/>; Cosmos warm intentionally drops the
/// manifest per the aggressive cleanup-on-session-end contract; Dataverse cold-tier
/// audit intentionally omits the manifest for the same reason).
///
/// Default: <c>null</c> for backward compatibility — pre-R5 sessions, persisted records,
/// and call sites omitting the parameter are semantically equivalent to "no files
/// uploaded into this session".
/// </param>
public record ChatSession(
    string SessionId,
    string TenantId,
    string? DocumentId,
    Guid? PlaybookId,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivity,
    IReadOnlyList<ChatMessage> Messages,
    ChatHostContext? HostContext = null,
    IReadOnlyList<string>? AdditionalDocumentIds = null,
    IReadOnlyList<ChatSessionFile>? UploadedFiles = null)
{
    /// <summary>
    /// Hard cap on the number of files a single chat session may carry in
    /// <see cref="UploadedFiles"/> (spec NFR-02, project CLAUDE.md §3.8).
    ///
    /// Callers MUST reject the 21st upload with a 4xx ProblemDetails error
    /// (R5 endpoint task own the error mapping per ADR-019).
    /// </summary>
    public const int MaxUploadedFiles = 20;
}

/// <summary>
/// Per-file manifest entry on <see cref="ChatSession.UploadedFiles"/>.
///
/// Carries the data needed by:
///   - The session-scoped RAG retrieval path (R5 task 002 — filters
///     <c>spaarke-session-files</c> AI Search index by <see cref="ChatSession.SessionId"/>).
///   - The session-files cleanup <c>IHostedService</c> (R5 task 007 — uses
///     <see cref="SearchDocumentIdsCsv"/> to identify abandoned-session leftovers).
///   - The chat-pane file-chip UX (R5 task 020).
///
/// Tenant isolation: <see cref="ChatSession.TenantId"/> on the parent session is the
/// authoritative tenant boundary (ADR-014). This record intentionally does NOT carry a
/// redundant <c>TenantId</c>.
///
/// Shape: matches design.md §4.4 verbatim (six fields). Do NOT add fields without
/// updating design.md §4.4 + spec NFR-02 first.
/// </summary>
/// <param name="FileId">
/// Stable session-scoped file ID. Used as the foreign key on
/// <c>spaarke-session-files</c> AI Search index documents.
/// </param>
/// <param name="FileName">Original upload file name (display only).</param>
/// <param name="ContentType">MIME content type as reported on upload.</param>
/// <param name="SizeBytes">Original (uncompressed) file size in bytes.</param>
/// <param name="SearchDocumentIdsCsv">
/// Comma-separated list of AI Search document IDs (in the
/// <c>spaarke-session-files</c> index) that hold the chunks for this file.
///
/// CSV format (not array) is intentional: it keeps the Redis hot-tier blob flat
/// and Cosmos JSON compact (no nested arrays per session-file). Cleanup logic
/// (R5 task 007) splits on <c>,</c> to enumerate index documents for deletion.
///
/// Single-chunk files: per spec NFR-02 / project CLAUDE.md §3.8, files smaller than
/// 500 tokens skip chunking and are indexed as a single document — in that case this
/// field contains exactly one document ID with no comma.
/// </param>
/// <param name="UploadedAt">
/// UTC timestamp the file was uploaded into the session. Used by the cleanup job
/// (R5 task 007) and by chat-pane UX for "uploaded N minutes ago" rendering.
/// </param>
public record ChatSessionFile(
    string FileId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string SearchDocumentIdsCsv,
    DateTimeOffset UploadedAt);
