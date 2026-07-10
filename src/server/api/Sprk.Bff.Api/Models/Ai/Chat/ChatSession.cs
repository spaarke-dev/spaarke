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
/// <see cref="System.Text.Json.JsonSerializer"/>; Cosmos warm carries the manifest as
/// <c>StoredSession.UploadedFiles</c> and it is restored on Cosmos fallback — the prior
/// mapping that dropped file references on restore was a P2 violation fixed per ADR-040
/// / FR-P0-01; Dataverse cold-tier audit intentionally omits the manifest).
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

    // =========================================================================
    // Session ledger (ADR-040 / FR-P0-01) — append-only typed entries.
    //
    // DARK LANDING (P0): these collections are persisted through the Redis +
    // Cosmos pipeline but have ZERO production readers. Writers arrive at P1
    // (FR-P1-02 universal ledger-write-before-render); ToolChain writers at P2;
    // ledger-referencing capability inputs at P3. Do NOT add readers before the
    // corresponding phase lands.
    //
    // All four default to null so pre-ledger Redis/Cosmos payloads (and call
    // sites that never touch the ledger) deserialize/construct cleanly — null is
    // semantically identical to "no ledger entries yet" (same convention as
    // UploadedFiles / AdditionalDocumentIds).
    //
    // Governance: Tier 3 user-owned, GDPR-erasable with the session (ADR-015 /
    // NFR-07). ToolChains carry identifiers/filters/counts only — never content.
    // =========================================================================

    /// <summary>
    /// Capability outputs, addressable by <see cref="SessionOutput.Key"/>
    /// (<c>{bindingId}@t{n}</c> — build via <see cref="SessionLedger.BuildOutputKey"/>).
    /// Append-only; the composition carrier for cross-capability references (ADR-040).
    /// </summary>
    public IReadOnlyList<SessionOutput>? Outputs { get; init; }

    /// <summary>
    /// Per-turn tool-call audit chains (identifiers/filters/counts/citations only —
    /// never verbatim content, per NFR-07). Append-only.
    /// </summary>
    public IReadOnlyList<SessionToolChain>? ToolChains { get; init; }

    /// <summary>
    /// Widget user-actions (selection, highlight, edit) recorded as consumable
    /// session events. Append-only.
    /// </summary>
    public IReadOnlyList<SessionWidgetEvent>? WidgetEvents { get; init; }

    /// <summary>
    /// Pending-confirmation and in-flight-elicitation markers. Append-only —
    /// resolutions are new entries correlated by <see cref="SessionGate.GateId"/>.
    /// </summary>
    public IReadOnlyList<SessionGate>? Gates { get; init; }

    /// <summary>
    /// Per-turn ContextEnvelope fingerprints — the context-selection leg of the
    /// decision-traceability trace (AIR2-038 / FR-A1-09). Identifiers/counts only
    /// (fingerprint id + slice count) — never slice content (NFR-07). Append-only.
    /// Null (like the sibling ledger collections) means "no fingerprints yet".
    /// </summary>
    public IReadOnlyList<SessionContextFingerprint>? ContextFingerprints { get; init; }

    // =========================================================================
    // R2 Compose-domain annotations (spaarkeai-compose-r2 FR-29, design.md §8).
    //
    // UNLIKE the append-only ledger collections above, these two are MUTABLE —
    // accept/reject/edit REPLACE the stored collection wholesale (see
    // ComposeService.SaveComposeAnnotationsAsync). They are NOT ledger entries and
    // NOT MemoryItems: this is the ARGUED Path-A exception to the core memory
    // charter §3.4 "no local MemoryItem variants" (design.md §8 "Explicit deviation
    // note"). AnchoredAnnotation is document-adjacent positional UI state (like a
    // cursor or a fold) — it MUST NOT be written via memory.*, MUST NOT be
    // retrieved by the Context Binder, and MUST NOT be surfaced in the user's
    // memory review/delete view. Persistence rides this SAME three-tier ChatSession
    // stack (Redis hot / Cosmos warm / Dataverse cold) — no new entity. Bound to
    // the session's DocumentId (document identity), not to a DOCX version, so it
    // is intended to survive Word handoffs (design.md §8 "Cross-version
    // persistence"). Governance: ADR-015 Tier 3 (user-owned, GDPR-erasable with
    // the session); tenant-scoped via TenantId; document-scoped via DocumentId.
    //
    // Null (like the sibling collections above) means "none yet" — pre-R2 Redis/
    // Cosmos payloads deserialize cleanly.
    // =========================================================================

    /// <summary>
    /// R2 anchored annotations (design.md §8) — Compose-domain document-adjacent
    /// positional UI state: comments, insertion/deletion suggestions, and
    /// explanations anchored to a position in the open document. See the
    /// class-level remarks above for the Path-A deviation note.
    /// </summary>
    public IReadOnlyList<AnchoredAnnotation>? AnchoredAnnotations { get; init; }

    /// <summary>
    /// R2 defined-terms tracking (design.md §8; spec FR-11 <c>compose-defined-terms</c>
    /// extraction capability) — session-scoped tracking of detected defined terms +
    /// inconsistent-usage flags, rendered read-only in the Context pane. Same
    /// governance + mutability notes as <see cref="AnchoredAnnotations"/>.
    /// </summary>
    public IReadOnlyList<DefinedTerm>? DefinedTermsTracking { get; init; }

    // =========================================================================
    // R2 session-scoped ACTIVE-DOCUMENT identity (spaarkeai-compose-r2 task 113;
    // UAT-R4/R5 defects 4/5/6/7 — the "which document is the user acting on?" fact).
    //
    // A document lives in four disjoint identity spaces (chat-uploaded ChatSessionFile;
    // stored sprk_document; Compose client-only Browse bytes; Compose transient
    // upload-mount). The only connective tissue before this field was the LLM guessing
    // between documentId / sessionFileId on send_workspace_artifact. This single MUTABLE
    // nullable field is the shared "which document is active" fact both surfaces
    // read/write deterministically:
    //   - WRITTEN by the compose-direct/upload mount registration
    //     (ComposeEndpoints.RegisterActiveDocument, POST /api/compose/active-document).
    //   - READ by SendWorkspaceArtifactHandler when the LLM supplies no explicit current
    //     pointer, so "edit in Compose" mounts the just-uploaded file, not a stale one.
    //
    // Like AnchoredAnnotations / DefinedTermsTracking above it is MUTABLE (registration
    // REPLACES it wholesale — it is a pointer, not an append-only ledger entry), rides the
    // SAME three-tier ChatSession stack (Redis hot / Cosmos warm / Dataverse cold), and is
    // NOT a MemoryItem: it is transient document-selection state (like a cursor), never
    // retrieved by the Context Binder, never surfaced in the memory review/delete view.
    // Governance: ADR-015 Tier 3 (user-owned, GDPR-erasable with the session); tenant-scoped
    // via TenantId. Null (like the sibling collections) means "no active document yet" —
    // pre-task-113 Redis/Cosmos payloads deserialize cleanly (backward compatible).
    // =========================================================================

    /// <summary>
    /// R2 session-scoped active-document identity (task 113) — the document the user is
    /// currently acting on across the chat↔Compose bridge. Null when no document is active.
    /// See the class-level remarks above for the mutability + governance notes.
    /// </summary>
    public ActiveDocumentIdentity? ActiveDocument { get; init; }
}

/// <summary>
/// R2 session-scoped active-document identity (spaarkeai-compose-r2 task 113) — the shared
/// "which document is the user acting on?" fact that both the chat surface and the Compose
/// surface read/write deterministically, fixing the chat↔Compose bridge defects (UAT-R4/R5
/// #4/#5/#6/#7) as a class.
///
/// <para>
/// It is a POINTER, not content: it carries deterministic identifiers only (ADR-015 Tier 3)
/// and at most one of a stored-document pointer (<see cref="SprkDocumentId"/>) or a
/// session-upload pointer (<see cref="SessionFileId"/>), discriminated by <see cref="Source"/>.
/// The optional SPE pointer fields are populated only when already known (stored documents);
/// they are never resolved here (that resolution stays behind the SpeFileStore facade — ADR-007).
/// </para>
/// </summary>
/// <param name="Source">
/// Origin discriminant — one of <see cref="SourceStored"/> (a stored <c>sprk_document</c>),
/// <see cref="SourceSessionUpload"/> (a chat-uploaded <see cref="ChatSessionFile"/>), or
/// <see cref="SourceComposeDirect"/> (a file Browse-mounted directly in Compose then landed
/// server-side as a <see cref="ChatSessionFile"/>). Session-upload and compose-direct both
/// resolve through <see cref="SessionFileId"/>; the discriminant preserves provenance for
/// telemetry + future policy.
/// </param>
/// <param name="SessionFileId">
/// The <see cref="ChatSessionFile.FileId"/> for a session-upload / compose-direct active
/// document. Null for a stored-document active document. This is the pointer
/// <c>SendWorkspaceArtifactHandler</c> resolves into a transient Compose upload-mount.
/// </param>
/// <param name="SprkDocumentId">
/// The stored <c>sprk_document</c> GUID (D form) for a stored active document. Null for a
/// session-upload / compose-direct active document. Resolved into a Compose pre-seed (SPE
/// pointer) by <c>SendWorkspaceArtifactHandler.ResolveComposeSeedAsync</c> under OBO.
/// </param>
/// <param name="SpeDriveItemId">Optional SPE drive-item id when already known (stored documents). Never resolved here.</param>
/// <param name="SpeDriveId">Optional SPE drive id when already known (stored documents).</param>
/// <param name="FileName">Optional display file name (best-effort; display only).</param>
/// <param name="RegisteredAt">UTC timestamp the document became active (observability / most-recent-wins).</param>
public sealed record ActiveDocumentIdentity(
    string Source,
    string? SessionFileId = null,
    string? SprkDocumentId = null,
    string? SpeDriveItemId = null,
    string? SpeDriveId = null,
    string? FileName = null,
    DateTimeOffset? RegisteredAt = null)
{
    /// <summary>Source discriminant: a stored <c>sprk_document</c> row.</summary>
    public const string SourceStored = "stored";

    /// <summary>Source discriminant: a file the user uploaded into the chat session.</summary>
    public const string SourceSessionUpload = "session-upload";

    /// <summary>Source discriminant: a file Browse-mounted directly in Compose, landed server-side as a <see cref="ChatSessionFile"/>.</summary>
    public const string SourceComposeDirect = "compose-direct";
}

/// <summary>
/// R2 anchored annotation (design.md §8) — Compose-domain, document-adjacent
/// positional UI state (comment, insertion/deletion suggestion, or explanation)
/// anchored to a position in the open document via <see cref="Anchor"/>.
///
/// <para>
/// <b>Path-A deviation (charter §3.4, design.md §8)</b>: this is NOT a
/// <c>MemoryItem</c> variant. It is argued as document-adjacent UI state — like a
/// cursor or a fold — never retrieved as "memory" by the Context Binder, never
/// written via <c>memory.*</c>, and never surfaced in the user's memory
/// review/delete view. If that argument is ever rejected at review, the fallback
/// is a <c>MemoryItem</c> sub-type negotiated WITH the core — never a silent
/// local variant.
/// </para>
/// <para>
/// <b>Mutability</b>: unlike the append-only ledger collections on
/// <see cref="ChatSession"/>, annotations are mutable — accept/reject/edit REPLACE
/// the stored collection (see <c>ComposeService.SaveComposeAnnotationsAsync</c>).
/// </para>
/// </summary>
public sealed record AnchoredAnnotation
{
    /// <summary>Stable annotation id (client-generated).</summary>
    public required string Id { get; init; }

    /// <summary>
    /// <c>"comment"</c> | <c>"insertion-suggestion"</c> | <c>"deletion-suggestion"</c> |
    /// <c>"explanation"</c> (design.md §8 verbatim).
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Drift-resistant anchor (content-match + structural hint + editor span id) locating the annotation in the document.</summary>
    public required AnchoredAnnotationAnchor Anchor { get; init; }

    /// <summary>Annotation body text (comment text, suggested replacement text, or explanation).</summary>
    public required string Body { get; init; }

    /// <summary>Author display name or identifier (human user, or the producing AI capability).</summary>
    public required string Author { get; init; }

    /// <summary>UTC timestamp the annotation was created.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary><c>"human"</c> | <c>"ai"</c> — who originated the annotation (design.md §8 verbatim).</summary>
    public required string Source { get; init; }

    /// <summary>
    /// AI-sourced annotations carry the producing Binding + the ADR-040 ledger ref.
    /// References the ledger entry — NEVER duplicates the <c>SessionOutput</c> payload
    /// as a second source of truth.
    /// </summary>
    public AnchoredAnnotationProvenance? Provenance { get; init; }
}

/// <summary>Drift-resistant anchor for an <see cref="AnchoredAnnotation"/> or a <see cref="DefinedTerm"/> first-usage location (design.md §8 verbatim shape).</summary>
public sealed record AnchoredAnnotationAnchor
{
    /// <summary>Content-match pattern (surrounding text) used to re-locate the anchor after document edits.</summary>
    public required string TextPattern { get; init; }

    /// <summary>Structural hint — paragraph index at anchor-creation time (best-effort; re-anchoring tolerates drift).</summary>
    public required int ParagraphHint { get; init; }

    /// <summary>Editor-native span id (TipTap/ProseMirror) — stable for in-editor session lifetime; not guaranteed stable across a Word round-trip (content-match + paragraph hint carry re-anchoring there).</summary>
    public required string SpanId { get; init; }
}

/// <summary>
/// AI-sourced provenance for an <see cref="AnchoredAnnotation"/> or <see cref="DefinedTerm"/> —
/// the ADR-040 <c>{bindingId}@t{n}</c> ledger reference form
/// (<see cref="SessionLedger.BuildOutputKey"/>). References the ledger entry; never
/// duplicates the underlying <c>SessionOutput</c> payload.
/// </summary>
public sealed record AnchoredAnnotationProvenance
{
    /// <summary>The <c>sprk_playbookconsumer</c> Binding id that produced the annotation.</summary>
    public required string BindingId { get; init; }

    /// <summary>Addressable ledger key in <c>{bindingId}@t{n}</c> form (ADR-040).</summary>
    public required string LedgerRef { get; init; }
}

/// <summary>
/// R2 defined-term tracking entry (design.md §8; spec FR-11 <c>compose-defined-terms</c>
/// extraction capability) — a term detected by the extraction capability, tracked for
/// the session and rendered read-only in the Context pane (Context pane is audit-only,
/// never an interactive input surface). Same Path-A deviation note and mutability
/// contract as <see cref="AnchoredAnnotation"/> (see its remarks).
/// </summary>
public sealed record DefinedTerm
{
    /// <summary>The detected term text (e.g., "Confidential Information").</summary>
    public required string Term { get; init; }

    /// <summary>The term's definition as stated in the document, when the extraction capability located one.</summary>
    public string? Definition { get; init; }

    /// <summary>Drift-resistant anchor to the term's first (defining) usage in the document.</summary>
    public AnchoredAnnotationAnchor? FirstUsageAnchor { get; init; }

    /// <summary>Human-readable descriptions of usages flagged as inconsistent with the definition (spec FR-11 <c>inconsistencies</c>). Empty/absent when no inconsistency was flagged.</summary>
    public IReadOnlyList<string>? InconsistentUsages { get; init; }

    /// <summary><c>"human"</c> | <c>"ai"</c> — who originated the tracking entry (human-added term vs. AI-extracted).</summary>
    public required string Source { get; init; }

    /// <summary>AI-sourced entries carry the producing Binding + the ADR-040 ledger ref. References the ledger entry — never duplicates the <c>SessionOutput</c> payload.</summary>
    public AnchoredAnnotationProvenance? Provenance { get; init; }
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
///   - The 6-Tier Memory Subsystem upload pipeline (chat-routing-redesign-r1 task 071+):
///     enriched fields (<see cref="SummaryText"/>, <see cref="ClassifiedDocType"/>,
///     <see cref="Sections"/>, <see cref="TableMetadata"/>, <see cref="Citations"/>,
///     <see cref="PageCount"/>, <see cref="Language"/>, <see cref="ClassifiedConfidence"/>)
///     populated by <c>SessionFileEnrichmentService</c> and consumed by
///     <c>LayeredContextCardBuilder</c> + recall tools.
///
/// Tenant isolation: <see cref="ChatSession.TenantId"/> on the parent session is the
/// authoritative tenant boundary (ADR-014). This record intentionally does NOT carry a
/// redundant <c>TenantId</c>.
///
/// Shape (chat-routing-redesign-r1, task 071): the original 6 R5 fields are preserved
/// verbatim; the 8 enriched fields are additive per stateful-chat-architecture.md §11.2.
/// All 8 enriched fields are nullable / default-initialised so older Cosmos and Redis
/// payloads (pre-enrichment) deserialize cleanly. Do NOT remove or rename fields without
/// updating stateful-chat-architecture.md §11.2 + spec NFR-02 first.
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
    DateTimeOffset UploadedAt)
{
    /// <summary>
    /// Full extracted text of the uploaded file (added R7 Wave 12.3 Phase 12.3a UAT fix
    /// 2026-07-03). At upload time <c>ChatDocumentEndpoints</c> parses the file via
    /// <c>IDocumentTextExtractor</c> to feed the RAG indexing pipeline; that same text is
    /// now persisted here so <c>SessionFileTextSource</c> can read it directly for the
    /// chat-summarize Linear consumer without paying the Azure AI Search index-catchup
    /// race.
    /// <para>
    /// Pattern mirrors the workspace-file summarize path
    /// (<c>WorkspaceFileEndpoints.ExecuteFileSummarizeAsync</c>) which has read text
    /// directly since inception. Prior chat-summarize paths (playbook engine + my initial
    /// Linear implementation) went through RAG and hit the race whenever a user typed
    /// "summarize this document" within a few seconds of upload.
    /// </para>
    /// <para>
    /// Null on pre-persistence session files (older Redis / Cosmos payloads deserialize
    /// cleanly). <c>SessionFileTextSource</c> falls back to RAG when null so existing
    /// sessions do not break mid-flight.
    /// </para>
    /// </summary>
    public string? ExtractedText { get; init; }

    /// <summary>
    /// Precomputed 1-paragraph summary produced by <c>FileSummarizationService</c>
    /// (gpt-4o-mini, chat-routing-redesign-r1 task 068). Marked "NOT authoritative"
    /// by <c>TrustFrameInstructionInjector</c> in the static-prefix trust frame —
    /// downstream agents MUST verify via recall before citing.
    ///
    /// Null until the upload-pipeline enrichment completes for this file
    /// (architecture §6.1). Length target: ≤120 chars per architecture §6.1.
    /// </summary>
    public string? SummaryText { get; init; }

    /// <summary>
    /// Document type label. Populated by the Event path's classify member
    /// (<c>EventRulesService</c> — the <c>document_uploaded → chat-classify(1)</c>
    /// rule, FR-P1-03 / spaarke-ai-architecture-redesign-r1 task 022; field
    /// originally added for chat-routing-redesign-r1 task 067, whose
    /// <c>FileClassificationService</c> never shipped). Examples: "nda", "patent",
    /// "invoice", "contract", "memo". Null until classification completes.
    /// </summary>
    public string? ClassifiedDocType { get; init; }

    /// <summary>
    /// Classifier confidence in <see cref="ClassifiedDocType"/> on the closed interval [0, 1].
    /// Null until classification completes. The Event path's M4 policy gates on this value
    /// (below <c>EventRules:ClassifyConfidenceThreshold</c> ⇒ confirmation turn before
    /// auto-summarize — FR-P1-03); downstream trust-frame consumers SHOULD treat values
    /// below ~0.6 as "unclassified" per architecture §6.1 rules.
    /// </summary>
    public double? ClassifiedConfidence { get; init; }

    /// <summary>
    /// Detected section structure produced by <c>FileManifestExtractor</c>
    /// (chat-routing-redesign-r1 task 069). Empty list when the extractor has not
    /// yet run or no sections were detected (e.g., a flat-text upload). Consumed by
    /// <c>LayeredContextCardBuilder</c> + <c>get_file_manifest</c> tool.
    /// </summary>
    public IReadOnlyList<SectionInfo> Sections { get; init; } = Array.Empty<SectionInfo>();

    /// <summary>
    /// Detected table metadata produced by <c>FileManifestExtractor</c>
    /// (chat-routing-redesign-r1 task 069). Empty list when the extractor has not
    /// yet run or no tables were detected. The name <c>TableMetadata</c> (vs <c>Tables</c>)
    /// is intentional: peer R5 sibling fields use noun phrases, and "Tables" alone is
    /// ambiguous when log lines mention table-rendering.
    /// </summary>
    public IReadOnlyList<TableInfo> TableMetadata { get; init; } = Array.Empty<TableInfo>();

    /// <summary>
    /// Citation references this file has produced or had attributed to it during the
    /// session (chat-routing-redesign-r1 task 071/076). Empty list when no citation
    /// activity has occurred for this file. Populated incrementally by recall tools
    /// (e.g., <c>recall_session_file</c>) as they emit citations.
    /// </summary>
    public IReadOnlyList<CitationReference> Citations { get; init; } = Array.Empty<CitationReference>();

    /// <summary>
    /// Total page count produced by <c>FileManifestExtractor</c>
    /// (chat-routing-redesign-r1 task 069). Null until extraction completes.
    /// For non-paginated formats (plain text, markdown), the extractor falls back to
    /// <c>Math.Max(1, textContent.Length / 3000)</c> per task 069 design.
    /// </summary>
    public int? PageCount { get; init; }

    /// <summary>
    /// Detected language as an ISO-639-1 code (e.g., "en", "fr", "de") produced by
    /// <c>FileManifestExtractor</c> (chat-routing-redesign-r1 task 069). Null until
    /// detection completes. Intentionally NOT defaulted to "en": an explicit
    /// <c>null</c> signals "not yet detected", which downstream tooling can
    /// distinguish from a confident English detection. Downstream consumers MAY
    /// default to "en" at the point of use.
    /// </summary>
    public string? Language { get; init; }
}

/// <summary>
/// Detected section heading in an uploaded file's text content.
///
/// Produced by <c>FileManifestExtractor</c> (chat-routing-redesign-r1 task 069) using
/// deterministic regex heuristics (no LLM call). Persisted on
/// <see cref="ChatSessionFile.Sections"/> for Redis hot-tier + Cosmos warm-tier read
/// by <c>LayeredContextCardBuilder</c> and the <c>get_file_manifest</c> tool.
///
/// Page fields are nullable because formats without page delimiters (plain text,
/// markdown) cannot report a meaningful page range — only character offsets.
/// </summary>
/// <param name="Name">The detected section heading text (display only).</param>
/// <param name="StartCharOffset">
/// Character offset (inclusive) of the heading within the original extracted text content.
/// Used by downstream tooling to slice the source for full-section retrieval.
/// </param>
/// <param name="EndCharOffset">
/// Character offset (exclusive) of the end of the section within the original extracted
/// text content. May equal <see cref="StartCharOffset"/> when extraction cannot
/// determine the section boundary.
/// </param>
/// <param name="StartPage">
/// 1-based start page number, or null when the source format is not paginated
/// (e.g., extracted from plain text / markdown without form-feed delimiters).
/// </param>
/// <param name="EndPage">
/// 1-based end page number (inclusive), or null when the source format is not paginated.
/// </param>
public sealed record SectionInfo(
    string Name,
    int StartCharOffset,
    int EndCharOffset,
    int? StartPage = null,
    int? EndPage = null);

/// <summary>
/// Detected table metadata in an uploaded file's text content.
///
/// Produced by <c>FileManifestExtractor</c> (chat-routing-redesign-r1 task 069) using
/// deterministic heuristics (3+ consecutive pipe/tab-delimited rows or markdown table
/// headers — no LLM call). Persisted on <see cref="ChatSessionFile.TableMetadata"/>
/// for Redis hot-tier + Cosmos warm-tier read by <c>LayeredContextCardBuilder</c> and
/// the <c>get_file_manifest</c> tool.
/// </summary>
/// <param name="Name">
/// Display label for the table (e.g., preceding heading line, or "Table 1" when no
/// label is detectable).
/// </param>
/// <param name="StartCharOffset">
/// Character offset (inclusive) of the table within the original extracted text content.
/// </param>
/// <param name="Page">
/// 1-based page number the table appears on, or null when the source format is not
/// paginated.
/// </param>
public sealed record TableInfo(
    string Name,
    int StartCharOffset,
    int? Page = null);

/// <summary>
/// Citation reference attributed to an uploaded file during the session.
///
/// Populated by recall tools (e.g., <c>recall_session_file</c>) as they emit citations
/// during their response shaping (chat-routing-redesign-r1 task 071 + downstream
/// recall handlers). Persisted on <see cref="ChatSessionFile.Citations"/> so the
/// layered context card and audit pipeline can report "how this file has been used"
/// across the session.
/// </summary>
/// <param name="SourceId">
/// The AI Search document ID (within <c>spaarke-session-files</c>) the citation
/// points at, OR a free-form symbolic source ID for non-search-backed citations.
/// </param>
/// <param name="Quote">
/// Optional verbatim quote text the citation surfaces. May be null when the citation
/// is structural (e.g., "Section 4.2" with no inline quote).
/// </param>
/// <param name="Page">
/// 1-based page number the citation points at, or null for non-paginated sources or
/// section-level citations.
/// </param>
public sealed record CitationReference(
    string SourceId,
    string? Quote = null,
    int? Page = null);
