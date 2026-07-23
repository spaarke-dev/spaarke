using Microsoft.Xrm.Sdk;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Chat;

/// <summary>
/// Dataverse persistence implementation for chat sessions and message history.
///
/// Entities (schema defined in AIPL-001, notes/design/dataverse-chat-schema.md):
///   - <c>sprk_aichatsummary</c> — session metadata (SessionId, TenantId, PlaybookId, MessageCount, Summary)
///   - <c>sprk_aichatmessage</c> — individual messages (SessionId, Role, Content, TokenCount, SequenceNumber)
///
/// Constraint (ADR-002): No AI processing here — pure data persistence.
/// Constraint (ADR-010): Registered as scoped (one per request); narrow interfaces are singletons.
/// </summary>
public sealed class ChatDataverseRepository : IChatDataverseRepository
{
    // Dataverse entity names (from dataverse-chat-schema.md)
    private const string SummaryEntityName = "sprk_aichatsummary";
    private const string MessageEntityName = "sprk_aichatmessage";

    private readonly IGenericEntityService _genericEntityService;
    private readonly ILogger<ChatDataverseRepository> _logger;

    public ChatDataverseRepository(
        IGenericEntityService genericEntityService,
        ILogger<ChatDataverseRepository> logger)
    {
        _genericEntityService = genericEntityService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task CreateSessionAsync(ChatSession session, CancellationToken ct = default)
    {
        var entity = new Entity(SummaryEntityName)
        {
            ["sprk_sessionid"] = session.SessionId,
            ["sprk_tenantid"] = session.TenantId,
            ["sprk_playbookid"] = session.PlaybookId?.ToString(),
            ["sprk_documentid"] = session.DocumentId,
            ["sprk_messagecount"] = 0,
            ["sprk_isarchived"] = false
        };

        var id = await _genericEntityService.CreateAsync(entity, ct);

        _logger.LogInformation(
            "Created sprk_aichatsummary {RecordId} for session {SessionId} (tenant={TenantId})",
            id, session.SessionId, session.TenantId);
    }

    /// <inheritdoc />
    public async Task<ChatSession?> GetSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        // Query sprk_aichatsummary by sessionId + tenantId (ADR-014: tenant-scoped)
        // Using RetrieveMultiple via RetrieveByAlternateKeyAsync is not available for this pattern.
        // We use the generic RetrieveAsync with a known query pattern via IDataverseService.
        // The actual query is performed by a FetchXML-capable method; using RetrieveAsync with
        // a filter approach requires direct SDK access. For now we delegate to a helper that
        // wraps the FetchXML query to avoid adding a new method to IDataverseService.
        //
        // Implementation note: IDataverseService does not expose a FetchXML query method directly.
        // This method constructs the session from available data using the message records.
        // In a full implementation, a QueryExpression or FetchXML approach would be used.
        // For testability (unit tests with mock IChatDataverseRepository), the logic here is
        // encapsulated in this repository class.

        _logger.LogDebug(
            "Loading session {SessionId} from Dataverse (tenant={TenantId})",
            sessionId, tenantId);

        // Load messages to reconstruct session
        var messages = await GetMessagesAsync(sessionId, maxMessages: 50, ct);

        // If no messages exist we still need the summary record to know the session exists.
        // For now, assume existence is validated by the ChatSessionManager on create.
        // Return null if no messages to indicate the session is not found.
        // In Phase D (evaluation), a dedicated GetSummaryAsync could be added.
        if (messages.Count == 0)
        {
            // Session exists but has no messages yet — this happens on CreateSession before
            // any messages are added. We reconstruct a minimal session.
            // Note: we cannot reconstruct full metadata (PlaybookId, DocumentId) from messages alone.
            // Callers should rely on the Redis hot path; Dataverse cold path is for resumed sessions.
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        return new ChatSession(
            SessionId: sessionId,
            TenantId: tenantId,
            DocumentId: null, // Will be populated once AIPL-054 adds FetchXML query support
            PlaybookId: null, // Will be populated from sprk_aichatsummary in Phase D
            CreatedAt: messages[0].CreatedAt,
            LastActivity: messages[^1].CreatedAt,
            Messages: messages);
    }

    /// <inheritdoc />
    public async Task ArchiveSessionAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Archiving session {SessionId} in Dataverse (tenant={TenantId})",
            sessionId, tenantId);

        // Mark the session summary record as archived
        // Note: We need the Dataverse record GUID to update. In Phase D, this would be cached.
        // For now, we log and proceed — the session will simply expire from Redis.
        // The AddMessageAsync path already has the GUID; ArchiveSession would need a lookup.
        // This is a known limitation addressed in AIPL-054 (ChatEndpoints) via summary GUID caching.
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task AddMessageAsync(ChatMessage message, CancellationToken ct = default)
    {
        // Map ChatMessageRole enum (matches Dataverse option set values)
        var roleValue = (int)message.Role;

        var entity = new Entity(MessageEntityName)
        {
            ["sprk_sessionid"] = message.SessionId,
            ["sprk_role"] = new OptionSetValue(roleValue),
            ["sprk_content"] = message.Content,
            ["sprk_tokencount"] = message.TokenCount,
            ["sprk_sequencenumber"] = message.SequenceNumber
        };

        var id = await _genericEntityService.CreateAsync(entity, ct);

        _logger.LogDebug(
            "Persisted message {MessageId} to Dataverse (session={SessionId}, role={Role}, seqNum={SeqNum})",
            id, message.SessionId, message.Role, message.SequenceNumber);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        string sessionId,
        int maxMessages,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Loading up to {MaxMessages} messages for session {SessionId} from Dataverse",
            maxMessages, sessionId);

        // The Dataverse cold read path has never reconstructed real messages: a text-field
        // (sprk_sessionid) query API was deferred (AIPL-054). The Redis hot path + Cosmos warm
        // path are the authoritative session stores; this cold path only reports "no messages"
        // (which resolves to a null session in GetSessionAsync → the endpoint returns 404).
        //
        // BUGFIX (spaarkeai-compose-r2): the prior implementation still called
        // IFieldMappingDataverseService.QueryChildRecordIdsAsync purely to DISCARD its result
        // (the method already returned an empty list unconditionally). Because
        // IFieldMappingDataverseService resolves to DataverseWebApiService (GraphModule DI), that
        // dead call reached the unimplemented metadata helper GetEntitySetNameAsync and threw
        // NotImplementedException — surfacing as an HTTP 500 on
        // GET /api/ai/chat/sessions/{id}/compose-outputs for any client-minted document session
        // that misses Redis + Cosmos and falls through to this cold path (the two-session model:
        // the document session is never server-persisted). A not-found session MUST resolve to
        // null (→ clean 404), never throw. The dead call is removed so the cold path returns
        // "no messages" cleanly. Genuine errors are not masked — there is no swallowed exception;
        // the path simply no longer invokes an unimplemented stub it never needed.
        return Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());
    }

    /// <inheritdoc />
    public async Task<int> GetMessageCountAsync(
        string tenantId,
        string sessionId,
        CancellationToken ct = default)
    {
        // Returns 0 until IDataverseService supports FetchXML count queries.
        // The ChatHistoryManager maintains the count in Redis (via ChatSession.Messages.Count).
        await Task.CompletedTask;
        return 0;
    }

    /// <inheritdoc />
    public async Task UpdateSessionActivityAsync(
        string tenantId,
        string sessionId,
        int messageCount,
        DateTimeOffset lastActivity,
        CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Updating session activity for {SessionId}: messageCount={MessageCount} (tenant={TenantId})",
            sessionId, messageCount, tenantId);

        // Note: Without the Dataverse record GUID, UpdateRecordFieldsAsync cannot be called directly.
        // The summary GUID caching strategy is deferred to AIPL-054 (ChatEndpoints).
        // Logging is sufficient for Phase 1 — the Redis hot path has the authoritative state.
        await Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task UpdateSessionSummaryAsync(
        string tenantId,
        string sessionId,
        string summary,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Persisting conversation summary for session {SessionId} (tenant={TenantId}, summaryLen={Len})",
            sessionId, tenantId, summary.Length);

        // Note: Same GUID-caching limitation as UpdateSessionActivityAsync.
        // Deferred to AIPL-054. The summary text is stored in the ChatSession record in Redis.
        await Task.CompletedTask;
    }
}
