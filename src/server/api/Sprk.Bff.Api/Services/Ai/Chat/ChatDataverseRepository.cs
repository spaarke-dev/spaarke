using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
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

    // sprk_aichatsummary.sprk_analysis lookup FK target entity (owner-created; verified
    // present by ai-advanced-capabilities-analysis-hub-r1 task 010 — Dataverse MCP `describe`
    // confirms: sprk_analysis LOOKUP (GUID) (Related table: sprk_analysis)). Bound at
    // CreateSessionAsync for Analysis-owned sessions (task 020, spec FR-05).
    private const string AnalysisFkAttribute = "sprk_analysis";
    private const string AnalysisEntityName = "sprk_analysis";

    // HostContext.EntityType value that identifies an Analysis-owned chat session (existing
    // convention — set at session-create time for sessions bound to an sprk_analysis record;
    // see AnalysisEndpoints.cs ForkAnalysis/PromoteSession, task 020/021/023). The sibling
    // per-turn sprk_chathistory write in ChatEndpoints.cs that used to reference this same
    // convention was removed by task 064 (provably dead once its sole reader was dropped).
    private const string AnalysisHostContextEntityType = "sprk_analysisoutput";

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

        // Analysis-owned session binding (task 020, spec FR-05): when the session's
        // HostContext identifies an analysis-scoped session (the existing
        // "sprk_analysisoutput" + analysis GUID convention), set the sprk_analysis lookup FK
        // alongside sprk_sessionid so "one Analysis → many sessions" is queryable
        // (GetSessionsByAnalysisAsync below). Loose (non-Analysis) sessions write no FK —
        // the record persists with only sprk_sessionid, matching pre-existing behavior.
        Guid? analysisId = null;
        if (session.HostContext?.EntityType == AnalysisHostContextEntityType &&
            Guid.TryParse(session.HostContext.EntityId, out var parsedAnalysisId))
        {
            analysisId = parsedAnalysisId;
            entity[AnalysisFkAttribute] = new EntityReference(AnalysisEntityName, parsedAnalysisId);
        }

        var id = await _genericEntityService.CreateAsync(entity, ct);

        _logger.LogInformation(
            "Created sprk_aichatsummary {RecordId} for session {SessionId} (tenant={TenantId}, analysis={AnalysisId})",
            id, session.SessionId, session.TenantId, analysisId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AnalysisSessionSummary>> GetSessionsByAnalysisAsync(
        string tenantId,
        Guid analysisId,
        CancellationToken ct = default)
    {
        var query = new QueryExpression(SummaryEntityName)
        {
            ColumnSet = new ColumnSet("sprk_sessionid", "sprk_messagecount", "sprk_isarchived", "createdon"),
            NoLock = true,
        };
        query.Criteria.AddCondition(AnalysisFkAttribute, ConditionOperator.Equal, analysisId);
        // Tenant-scoped (ADR-014/ADR-028): mirrors every sibling read on this interface — a
        // cross-tenant analysisId guess must not leak another tenant's sessions.
        query.Criteria.AddCondition("sprk_tenantid", ConditionOperator.Equal, tenantId);

        var results = await _genericEntityService.RetrieveMultipleAsync(query, ct);

        var summaries = results.Entities
            .Select(e => new AnalysisSessionSummary(
                SessionId: e.GetAttributeValue<string>("sprk_sessionid") ?? string.Empty,
                MessageCount: e.GetAttributeValue<int?>("sprk_messagecount") ?? 0,
                IsArchived: e.GetAttributeValue<bool?>("sprk_isarchived") ?? false,
                CreatedOn: e.Contains("createdon")
                    ? new DateTimeOffset(e.GetAttributeValue<DateTime>("createdon"), TimeSpan.Zero)
                    : null))
            .Where(s => !string.IsNullOrEmpty(s.SessionId))
            .ToList();

        _logger.LogInformation(
            "Retrieved {Count} session(s) bound to analysis {AnalysisId} for tenant {TenantId} (sprk_analysis FK, grouped by sprk_sessionid)",
            summaries.Count, analysisId, tenantId);

        return summaries;
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

        // AIPL-054 durable-archive (ai-advanced-capabilities-analysis-hub-r1 task 022, spec FR-06):
        // this method previously logged only — it lacked the sprk_aichatsummary record GUID needed
        // to flip sprk_isarchived (the "missing cached summary GUID" gap that deferred a durable
        // archive to task 022). Rather than a broad summary-GUID caching refactor across
        // ChatSessionManager, we close the gap in-place: sprk_sessionid is already a queryable key
        // on the summary row (CreateSessionAsync writes it), so ONE tenant-scoped query resolves the
        // GUID and a sparse UpdateAsync durably flips the flag.
        //
        // Tenant-scoped (ADR-014/ADR-028): mirrors every sibling read on this interface — a
        // cross-tenant sessionId guess must not flip another tenant's archive flag.
        //
        // ADR-040 Path A: this is a MARKER update only. The Cosmos transcript is the store-of-record
        // and is NEVER deleted here — the archived prior session stays retrievable / switchable-back
        // (no transcript data loss). No second transcript store is created.
        var query = new QueryExpression(SummaryEntityName)
        {
            ColumnSet = new ColumnSet("sprk_isarchived"),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition("sprk_sessionid", ConditionOperator.Equal, sessionId);
        query.Criteria.AddCondition("sprk_tenantid", ConditionOperator.Equal, tenantId);

        var results = await _genericEntityService.RetrieveMultipleAsync(query, ct);
        var record = results.Entities.FirstOrDefault();

        if (record is null)
        {
            // No sprk_aichatsummary anchor row (e.g. a session whose cold-tier create was skipped
            // or failed — CreateSessionAsync tolerates a Dataverse write failure and continues on
            // Redis only). There is no row to flip; the Cosmos transcript remains the store-of-record
            // (ADR-040 Path A) and is still retrievable. Non-fatal — matches the tolerant create path.
            _logger.LogWarning(
                "ArchiveSessionAsync: no sprk_aichatsummary row for session {SessionId} (tenant={TenantId}) — " +
                "sprk_isarchived not flipped. Cosmos transcript stays the store-of-record; no data lost.",
                sessionId, tenantId);
            return;
        }

        // Durable flip: sprk_isarchived = true (sparse update — only the changed field).
        await _genericEntityService.UpdateAsync(
            SummaryEntityName,
            record.Id,
            new Dictionary<string, object> { ["sprk_isarchived"] = true },
            ct);

        _logger.LogInformation(
            "Archived session {SessionId}: sprk_isarchived flipped true on sprk_aichatsummary {RecordId} " +
            "(tenant={TenantId}). Cosmos transcript preserved (ADR-040 Path A — retrievable/switchable-back).",
            sessionId, record.Id, tenantId);
    }

    /// <inheritdoc />
    public async Task<bool> BindSessionToAnalysisAsync(
        string tenantId,
        string sessionId,
        Guid analysisId,
        CancellationToken ct = default)
    {
        // Explicit promotion (task 023, spec FR-07): binds an EXISTING sprk_aichatsummary row to a
        // NEW sprk_analysis — a bind on the CURRENT row, never a new row. Distinct from
        // CreateSessionAsync's create-time FK write (which fires only for a session minted with an
        // Analysis-owned HostContext) — this is the after-the-fact path for a loose session that was
        // NOT Analysis-owned at create time. Same tenant-scoped query-then-update shape as
        // ArchiveSessionAsync (ADR-014/ADR-028: a cross-tenant sessionId guess must not bind another
        // tenant's row).
        _logger.LogInformation(
            "Binding session {SessionId} to analysis {AnalysisId} (tenant={TenantId}) — explicit promotion (task 023)",
            sessionId, analysisId, tenantId);

        var query = new QueryExpression(SummaryEntityName)
        {
            ColumnSet = new ColumnSet(AnalysisFkAttribute),
            TopCount = 1,
            NoLock = true,
        };
        query.Criteria.AddCondition("sprk_sessionid", ConditionOperator.Equal, sessionId);
        query.Criteria.AddCondition("sprk_tenantid", ConditionOperator.Equal, tenantId);

        var results = await _genericEntityService.RetrieveMultipleAsync(query, ct);
        var record = results.Entities.FirstOrDefault();

        if (record is null)
        {
            // No sprk_aichatsummary anchor row (e.g. a session whose cold-tier create was skipped or
            // failed — CreateSessionAsync tolerates a Dataverse write failure and continues on Redis
            // only). UNLIKE the tolerant archive path, PROMOTE cannot no-op here: the durable
            // sprk_analysis FK is promotion's ENTIRE deliverable — it is what makes the Analysis
            // queryable via GetSessionsByAnalysisAsync / visible in the hub grid. A silent no-op left
            // promote returning 201 with an orphaned Analysis (agreements-r1 Q2 / silent-FK gap,
            // 2026-07-31). So we CREATE the anchor row now WITH the FK (minimal columns — message
            // count / playbook backfill on the next cold write; the FK is the point). A real Dataverse
            // write failure throws and propagates to the caller's compensation (Analysis delete).
            var created = new Entity(SummaryEntityName)
            {
                ["sprk_sessionid"] = sessionId,
                ["sprk_tenantid"] = tenantId,
                ["sprk_messagecount"] = 0,
                ["sprk_isarchived"] = false,
                [AnalysisFkAttribute] = new EntityReference(AnalysisEntityName, analysisId),
            };
            var createdId = await _genericEntityService.CreateAsync(created, ct);

            _logger.LogInformation(
                "BindSessionToAnalysisAsync: no existing sprk_aichatsummary for session {SessionId} (tenant={TenantId}) — " +
                "created anchor row {RecordId} WITH the sprk_analysis {AnalysisId} FK (promote durable-bind).",
                sessionId, tenantId, createdId, analysisId);
            return true;
        }

        await _genericEntityService.UpdateAsync(
            SummaryEntityName,
            record.Id,
            new Dictionary<string, object> { [AnalysisFkAttribute] = new EntityReference(AnalysisEntityName, analysisId) },
            ct);

        _logger.LogInformation(
            "Bound session {SessionId} to analysis {AnalysisId}: sprk_analysis FK set on sprk_aichatsummary {RecordId} (tenant={TenantId})",
            sessionId, analysisId, record.Id, tenantId);

        return true;
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
