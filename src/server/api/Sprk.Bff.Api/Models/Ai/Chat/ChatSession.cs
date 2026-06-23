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
    /// Document type label produced by <c>FileClassificationService</c>
    /// (chat-routing-redesign-r1 task 067). Examples: "NDA", "patent", "invoice",
    /// "contract", "memo". Null until classification completes.
    /// </summary>
    public string? ClassifiedDocType { get; init; }

    /// <summary>
    /// Classifier confidence in <see cref="ClassifiedDocType"/> on the closed interval [0, 1].
    /// Null until classification completes. Downstream consumers SHOULD treat values
    /// below ~0.6 as "unclassified" per architecture §6.1 trust-frame rules.
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
