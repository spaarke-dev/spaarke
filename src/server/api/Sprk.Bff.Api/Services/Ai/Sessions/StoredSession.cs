using System.Text.Json.Serialization;
using Sprk.Bff.Api.Models.Ai.Chat;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Cosmos DB document representing a durable AI chat session (ADR-015 Tier 3: Work History).
///
/// Partition key: <c>/tenantId</c> — enforces tenant isolation and supports GDPR erasure
/// by partition delete.
///
/// Container: <c>sessions</c> (Cosmos DB database configured via CosmosPersistence:DatabaseName).
/// Retention: 90 days default (container-level <c>DefaultTimeToLive = 7776000</c>, defined at
/// provisioning time — ADR-015). FR-D10 (task 033) overrides this PER DOCUMENT via <see cref="Ttl"/>
/// so a FILED analysis's session is retained indefinitely while unfiled sessions keep the 90-day
/// default (spike: <c>projects/spaarkeai-assistant-enhancements-r2/notes/d10-ttl-spike.md</c>).
/// </summary>
public class StoredSession
{
    /// <summary>
    /// FR-D10 (task 033) — Cosmos per-item <c>ttl</c> sentinel meaning "never expire". See <see cref="Ttl"/>.
    /// </summary>
    public const int NeverExpireTtl = -1;

    /// <summary>Cosmos DB document id — matches sessionId.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Unique session identifier.</summary>
    [JsonPropertyName("sessionId")]
    public string SessionId { get; set; } = string.Empty;

    /// <summary>
    /// FR-D4 (task 032) — stored, writable, human-readable session title. Seeded from a cheap
    /// grounded label (or a deterministic fallback derived from the first user message — see
    /// <see cref="Chat.ChatHistoryManager.AddMessageAsync"/>) at the session's first substantive
    /// exchange, and rewritable via <c>PATCH /api/ai/chat/sessions/{sessionId}</c>
    /// (<see cref="Api.Ai.ChatEndpoints"/>). Never a bare timestamp once set.
    ///
    /// Null for sessions that pre-date this field or that have not yet exchanged a first message
    /// (additive schema evolution per ADR-015; partition key unchanged) — the History list
    /// (<see cref="SessionPersistenceService.ListRecentSessionsAsync"/>) falls back to its prior
    /// read-computed heuristic (<c>BuildSessionTitle</c>) only in that case, so the persisted title
    /// is the ONE source of truth once it exists (no double-sourcing).
    /// </summary>
    [JsonPropertyName("title")]
    public string? Title { get; set; }

    /// <summary>
    /// Tenant identifier. Used as the partition key (/tenantId) for all Cosmos DB operations.
    /// Required — every document must be scoped to a tenant (ADR-015, NFR-09).
    /// </summary>
    [JsonPropertyName("tenantId")]
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Playbook that governs this session's agent behaviour. Nullable for knowledge-only sessions.</summary>
    [JsonPropertyName("playbookId")]
    public Guid? PlaybookId { get; set; }

    /// <summary>
    /// FR-D10 (task 033) — the session's host context (where it is embedded and, critically, whether
    /// it has been FILED to an Analysis: <c>HostContext.EntityType == "sprk_analysisoutput"</c>).
    ///
    /// Persisted through the warm tier so filed-state SURVIVES a Redis eviction + Cosmos reload — the
    /// same "must survive warm-store restore" class as <see cref="DocumentId"/> (ADR-040). Without
    /// this, a reopened filed session would restore with a null host context, and the next message
    /// turn's <see cref="Chat.ChatSessionManager"/>.<c>MapChatSessionToStoredSession</c> would
    /// re-derive <see cref="Ttl"/> = null and silently revert the doc to the 90-day default — the
    /// exact FR-D10 failure this feature prevents. Restored by
    /// <c>MapStoredSessionToChatSession</c> so re-derivation stays correct on every turn.
    ///
    /// Stored as the domain record directly (same reuse rationale as <see cref="ActiveDocument"/> /
    /// <see cref="AnchoredAnnotations"/> — no parallel Stored* shape where the record round-trips
    /// cleanly, root CLAUDE.md §11). Null for knowledge-only sessions or Cosmos documents that
    /// pre-date this field (additive per ADR-015; partition key unchanged). The
    /// <c>ChatHostContext.EntityType</c> normalizer is idempotent, so re-normalizing an
    /// already-normalized stored value on restore is a no-op.
    /// </summary>
    [JsonPropertyName("hostContext")]
    public ChatHostContext? HostContext { get; set; }

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
    /// FR-D10 (task 033) — Cosmos per-item time-to-live override (seconds). Controls THIS document's
    /// retention independently of the container-level <c>DefaultTimeToLive</c> (90 days / 7776000s):
    /// <list type="bullet">
    ///   <item><c>null</c> → the document rides the container's 90-day default (every UNFILED session;
    ///     unchanged from before this field). Note: Cosmos treats an absent OR explicitly-null
    ///     <c>ttl</c> identically — both defer to the container default — so this is safe regardless
    ///     of whether the serializer omits the property.</item>
    ///   <item><see cref="NeverExpireTtl"/> (<c>-1</c>) → the document NEVER expires (indefinite
    ///     retention), set when the session is FILED to an Analysis so a filed analysis's
    ///     transcript+tabs+redline survive past 90 days (FR-D10).</item>
    /// </list>
    /// DERIVED from the live filed-state on EVERY map-to-StoredSession
    /// (<see cref="Chat.ChatSessionManager"/>.<c>MapChatSessionToStoredSession</c>), so every
    /// write-through re-asserts it — a later message turn's upsert can never silently reset a filed
    /// document back to the 90-day default. The <see cref="SessionPersistenceService"/> read-modify-write
    /// paths (tabs / uploads / summary) load the existing document and round-trip this value, so they
    /// preserve it without needing the source <c>ChatSession</c>.
    ///
    /// Per-item overrides require the container's <c>DefaultTimeToLive</c> to be non-null; the
    /// <c>sessions</c> container is provisioned at 7776000s so overrides are enabled (spike:
    /// <c>notes/d10-ttl-spike.md</c>). Omitted-when-null on the System.Text.Json write path so an
    /// unfiled document carries no <c>ttl</c> at all; older documents deserialize cleanly to
    /// <c>null</c> (additive per ADR-015; partition key unchanged).
    /// </summary>
    [JsonPropertyName("ttl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Ttl { get; set; }

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

    // =========================================================================
    // Document references (ADR-040 / FR-P0-01 Cosmos file-reference fix)
    //
    // ADR-040 MUST: document references survive warm-store restore. Prior to
    // FR-P0-01 the ChatSessionManager Cosmos mapping dropped DocumentId,
    // AdditionalDocumentIds, and the UploadedFiles manifest on both the persist
    // and restore directions (the audited P2 violation). These fields carry
    // them; older documents deserialize cleanly (additive per ADR-015).
    // =========================================================================

    /// <summary>
    /// SPE document ID for document-context sessions (mirrors
    /// <c>ChatSession.DocumentId</c>). Null for knowledge-only sessions and for
    /// documents that pre-date this field.
    /// </summary>
    [JsonPropertyName("documentId")]
    public string? DocumentId { get; set; }

    /// <summary>
    /// Additional document IDs (max 5) pinned to the conversation (mirrors
    /// <c>ChatSession.AdditionalDocumentIds</c>). Empty for sessions with no
    /// pinned documents or documents that pre-date this field.
    /// </summary>
    [JsonPropertyName("additionalDocumentIds")]
    public List<string> AdditionalDocumentIds { get; set; } = [];

    // =========================================================================
    // Session ledger (ADR-040 / FR-P0-01) — Cosmos shapes of the typed entries.
    //
    // DARK at P0: persisted + restored, zero production readers. All four
    // default to empty lists so older Cosmos documents deserialize cleanly
    // (additive schema evolution per ADR-015; partition key unchanged).
    // Tier 3 user-owned / GDPR-erased with the session document.
    // =========================================================================

    /// <summary>Capability outputs, addressable by <c>{bindingId}@t{n}</c> key (ADR-040).</summary>
    [JsonPropertyName("outputs")]
    public List<StoredSessionOutput> Outputs { get; set; } = [];

    /// <summary>Per-turn tool-call audit chains (identifiers/filters/counts only — NFR-07).</summary>
    [JsonPropertyName("toolChains")]
    public List<StoredToolChain> ToolChains { get; set; } = [];

    /// <summary>Widget user-action events.</summary>
    [JsonPropertyName("widgetEvents")]
    public List<StoredWidgetEvent> WidgetEvents { get; set; } = [];

    /// <summary>Pending-confirmation / elicitation gate markers.</summary>
    [JsonPropertyName("gates")]
    public List<StoredGate> Gates { get; set; } = [];

    /// <summary>Per-turn ContextEnvelope fingerprints (identifiers/counts only — NFR-07; AIR2-038).</summary>
    [JsonPropertyName("contextFingerprints")]
    public List<StoredContextFingerprint> ContextFingerprints { get; set; } = [];

    // =========================================================================
    // R2 Compose-domain annotations (spaarkeai-compose-r2 FR-29 / task 102 gap 4.5 / DEF-05).
    //
    // The two MUTABLE Compose-domain session collections (design.md §8) — anchored annotations
    // and defined-terms tracking. Previously persisted through the Redis HOT tier only, so a
    // Redis eviction / cold restore silently dropped them (the DEF-05 failure). These fields
    // carry them through the Cosmos WARM tier so a reopen that outlives the Redis TTL still
    // restores prior annotations + defined terms.
    //
    // Stored as the domain records directly (they are plain System.Text.Json-serializable
    // records) rather than minting parallel Stored* shapes — the same reuse rationale as the
    // other Compose collections (Component Justification §11: no new DTO surface where the
    // domain record round-trips cleanly). Web camelCase serialization matches the rest of this
    // document. Both default to empty lists so older Cosmos documents deserialize cleanly
    // (additive schema evolution per ADR-015; partition key unchanged). Tier 3 user-owned /
    // GDPR-erased with the session document.
    //
    // NOTE: action history is NOT stored here — it is a read-only projection over
    // <see cref="Outputs"/> (ComposeService.GetActionHistory), which already round-trips through
    // the warm tier, so it survives automatically once Outputs do.
    // =========================================================================

    /// <summary>R2 anchored annotations (design.md §8) — MUTABLE Compose-domain positional UI state.</summary>
    [JsonPropertyName("anchoredAnnotations")]
    public List<AnchoredAnnotation> AnchoredAnnotations { get; set; } = [];

    /// <summary>R2 defined-terms tracking (design.md §8 / spec FR-11) — session-scoped detected terms.</summary>
    [JsonPropertyName("definedTermsTracking")]
    public List<DefinedTerm> DefinedTermsTracking { get; set; } = [];

    /// <summary>
    /// R2 session-scoped active-document pointer (spaarkeai-compose-r2 task 113) — the "which
    /// document is the user acting on?" fact for the chat↔Compose bridge. Stored as the domain
    /// record directly (same reuse rationale as <see cref="AnchoredAnnotations"/> — no parallel
    /// Stored* shape where the record round-trips cleanly, Component Justification §11). Null when
    /// no document is active or the Cosmos document pre-dates this field (additive per ADR-015;
    /// partition key unchanged). Carried through the warm tier so the active document is NOT
    /// dropped on a Redis eviction / cold restore (the ADR-040 document-reference-survival MUST).
    /// Tier 3 user-owned / GDPR-erased with the session document.
    /// </summary>
    [JsonPropertyName("activeDocument")]
    public ActiveDocumentIdentity? ActiveDocument { get; set; }

    /// <summary>
    /// R4.5 WS-4 (spaarkeai-compose-fidelity-r4.5 task 041, spec FR-17) — the persisted
    /// <c>paraId -&gt; legal-number</c> reference map. Stored as the domain record directly
    /// (same reuse rationale as <see cref="AnchoredAnnotations"/> — no parallel Stored* shape
    /// where the record round-trips cleanly, root CLAUDE.md §11). Carried through the warm
    /// tier so a Redis eviction / cold restore does not silently drop the reference map (the
    /// same ADR-040 document-reference-survival MUST the sibling fields above document).
    /// Empty for sessions with no reference map yet or for Cosmos documents that pre-date
    /// this field (additive schema evolution per ADR-015; partition key unchanged). Tier 3
    /// user-owned / GDPR-erased with the session document.
    /// </summary>
    [JsonPropertyName("referenceMap")]
    public List<ParaReferenceMapEntry> ReferenceMap { get; set; } = [];
}
