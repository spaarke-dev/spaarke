using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Cosmos DB document representing a durable AI chat session (ADR-015 Tier 3: Work History).
///
/// Partition key: <c>/tenantId</c> — enforces tenant isolation and supports GDPR erasure
/// by partition delete.
///
/// Container: <c>sessions</c> (Cosmos DB database configured via CosmosPersistence:DatabaseName).
/// Retention: 90 days default (defined at container provisioning time — ADR-015).
/// </summary>
public class StoredSession
{
    /// <summary>Cosmos DB document id — matches sessionId.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Unique session identifier.</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// Tenant identifier. Used as the partition key (/tenantId) for all Cosmos DB operations.
    /// Required — every document must be scoped to a tenant (ADR-015, NFR-09).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Playbook that governs this session's agent behaviour. Nullable for knowledge-only sessions.</summary>
    [JsonPropertyName("playbookId")]
    public Guid? PlaybookId { get; set; }

    /// <summary>Ordered message history for the session.</summary>
    [JsonPropertyName("messages")]
    public List<SessionMessage> Messages { get; set; } = [];

    /// <summary>
    /// Widget state dictionary keyed by widget instance ID.
    /// Stores serialised widget payloads so the three-pane UI can restore state on resume.
    /// </summary>
    [JsonPropertyName("widgetStates")]
    public Dictionary<string, string> WidgetStates { get; set; } = [];

    /// <summary>
    /// Workspace tabs (non-Home only) persisted via write-through (NFR-09).
    /// Home tab is recreated by ensureHomeTab() on every WorkspacePane mount.
    /// Tab order is preserved. Empty list for sessions with no non-Home tabs open.
    /// Older Cosmos documents that pre-date this field deserialize cleanly to an empty list
    /// (additive schema evolution per ADR-015).
    /// </summary>
    [JsonPropertyName("tabs")]
    public List<StoredWorkspaceTab> Tabs { get; set; } = [];

    /// <summary>
    /// Active tab id at the time of the last save. May be the Home tab id ("home") or one of
    /// the ids in <see cref="Tabs"/>. Used by the client on restore to set the active selection.
    /// Null for sessions that pre-date this field.
    /// </summary>
    [JsonPropertyName("activeTabId")]
    public string? ActiveTabId { get; set; }

    /// <summary>UTC timestamp when the session was first created.</summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>UTC timestamp of the most recent message or state update.</summary>
    [JsonPropertyName("lastActivity")]
    public DateTimeOffset LastActivity { get; set; }

    /// <summary>
    /// Dataverse entity references in scope when the session was saved.
    /// Each entry carries a saved ETag so <see cref="ISessionRestoreService"/> can detect
    /// whether the entity has changed since the session was persisted (ADR-015 D-08).
    /// Empty for sessions that have no entity context (e.g., knowledge-only sessions).
    /// </summary>
    [JsonPropertyName("entityRefs")]
    public List<SessionEntityRef> EntityRefs { get; set; } = [];

    /// <summary>
    /// LLM-generated conversation summary produced when the message count exceeds the
    /// summarisation threshold. Used by <see cref="ISessionRestoreService"/> as the base
    /// context string when reconstructing the context window on restore.
    /// Null if no summary has been generated yet (short sessions use verbatim messages only).
    /// </summary>
    [JsonPropertyName("conversationSummary")]
    public string? ConversationSummary { get; set; }

    /// <summary>
    /// Structured session summary produced by <see cref="ISessionSummarizationService"/> (AIPU2-032).
    ///
    /// Written by <see cref="SessionPersistenceService.PersistSummaryAsync"/> after the session
    /// reaches the 25-message or 8,000-token threshold. Null until first summarization occurs.
    ///
    /// Contains both a free-text narrative and a structured list of key legal conclusions.
    /// The full verbatim message history is always preserved in <see cref="Messages"/>;
    /// this field is an addition to the document, never a replacement.
    /// </summary>
    [JsonPropertyName("summary")]
    public SessionSummary? Summary { get; set; }

    /// <summary>
    /// Per-file manifest of files uploaded into this session by the end user, enriched with
    /// classify / summarize / manifest-extraction outputs from the upload pipeline
    /// (chat-routing-redesign-r1 architecture §6.1, task 072).
    ///
    /// Written by <see cref="SessionPersistenceService.UpdateUploadedFilesAsync"/> after the
    /// parallel enrichment paths complete. Empty list for sessions with no uploads or for
    /// Cosmos documents that pre-date this field (additive schema evolution per ADR-015 —
    /// older docs deserialize cleanly to an empty list).
    ///
    /// Cap: hard-limited to 20 per session by the writing service
    /// (mirrors <c>ChatSession.MaxUploadedFiles</c>). Manifest order is preserved.
    ///
    /// The shape mirrors <c>Sprk.Bff.Api.Models.Ai.Chat.ChatSessionFile</c> (6 R5 fields +
    /// 8 chat-routing-redesign-r1 enrichment fields). camelCase wire format matches the rest
    /// of this document.
    /// </summary>
    [JsonPropertyName("uploadedFiles")]
    public List<StoredUploadedFile> UploadedFiles { get; set; } = [];
}
