using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Ai.Sessions;

/// <summary>
/// Persisted snapshot of a single end-user-uploaded session file, enriched with the outputs
/// of the upload pipeline (chat-routing-redesign-r1 architecture §6.1, task 072).
///
/// Wire shape (System.Text.Json camelCase) — mirrors
/// <c>Sprk.Bff.Api.Models.Ai.Chat.ChatSessionFile</c> (6 R5 fields + 8 chat-routing-redesign-r1
/// enrichment fields). All 8 enrichment fields are nullable / default-initialised so older
/// Cosmos and Redis payloads (pre-enrichment) deserialize cleanly (additive schema evolution
/// per ADR-015 — partition key <c>/tenantId</c> unchanged).
///
/// <para><b>Why a parallel Cosmos-shape record rather than reusing
/// <c>Sprk.Bff.Api.Models.Ai.Chat.ChatSessionFile</c> directly</b>: <c>ChatSessionFile</c> and
/// its nested records (<c>SectionInfo</c>, <c>TableInfo</c>, <c>CitationReference</c>) use
/// PascalCase property names with no <c>[JsonPropertyName]</c> attributes, while every other
/// property on <see cref="StoredSession"/> uses camelCase. Mixing the two casings within a
/// single Cosmos document would produce an inconsistent wire format. Matches the precedent
/// of <see cref="StoredWorkspaceTab"/> (task 065) — a parallel Cosmos shape adjacent to the
/// in-process domain type.</para>
///
/// <para><b>Placement (CLAUDE.md §10 / ADR-013)</b>: in-process DTO on the existing
/// <see cref="SessionPersistenceService"/> pipeline. No new DI module, no new service, no new
/// NuGet packages — purely additive to <see cref="StoredSession"/>.</para>
/// </summary>
public class StoredUploadedFile
{
    // -------------------------------------------------------------------------
    // Original 6 R5 fields (preserved verbatim from ChatSessionFile)
    // -------------------------------------------------------------------------

    /// <summary>Stable session-scoped file ID.</summary>
    [JsonPropertyName("fileId")]
    public string FileId { get; set; } = string.Empty;

    /// <summary>Original upload file name (display only).</summary>
    [JsonPropertyName("fileName")]
    public string FileName { get; set; } = string.Empty;

    /// <summary>MIME content type as reported on upload.</summary>
    [JsonPropertyName("contentType")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>Original (uncompressed) file size in bytes.</summary>
    [JsonPropertyName("sizeBytes")]
    public long SizeBytes { get; set; }

    /// <summary>
    /// Comma-separated list of AI Search document IDs in <c>spaarke-session-files</c>.
    /// Mirrors <c>ChatSessionFile.SearchDocumentIdsCsv</c>.
    /// </summary>
    [JsonPropertyName("searchDocumentIdsCsv")]
    public string SearchDocumentIdsCsv { get; set; } = string.Empty;

    /// <summary>UTC timestamp the file was uploaded into the session.</summary>
    [JsonPropertyName("uploadedAt")]
    public DateTimeOffset UploadedAt { get; set; }

    // -------------------------------------------------------------------------
    // 8 chat-routing-redesign-r1 enrichment fields (additive — all nullable / default-init)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Precomputed 1-paragraph summary (≤120 chars) produced by
    /// <c>FileSummarizationService</c> (task 068). Marked "NOT authoritative" in the static
    /// trust frame — downstream agents MUST verify via recall before citing.
    /// </summary>
    [JsonPropertyName("summaryText")]
    public string? SummaryText { get; set; }

    /// <summary>
    /// Document type label produced by <c>FileClassificationService</c> (task 067).
    /// Examples: "NDA", "patent", "invoice", "contract", "memo".
    /// </summary>
    [JsonPropertyName("classifiedDocType")]
    public string? ClassifiedDocType { get; set; }

    /// <summary>
    /// Classifier confidence on the closed interval [0, 1]. Consumers SHOULD treat values
    /// below ~0.6 as "unclassified" per architecture §6.1 trust-frame rules.
    /// </summary>
    [JsonPropertyName("classifiedConfidence")]
    public double? ClassifiedConfidence { get; set; }

    /// <summary>
    /// Detected section structure produced by <c>FileManifestExtractor</c> (task 069).
    /// Empty list when extraction has not yet run or no sections were detected.
    /// </summary>
    [JsonPropertyName("sections")]
    public List<StoredSectionInfo> Sections { get; set; } = [];

    /// <summary>
    /// Detected table metadata produced by <c>FileManifestExtractor</c> (task 069).
    /// Empty list when extraction has not yet run or no tables were detected.
    /// </summary>
    [JsonPropertyName("tableMetadata")]
    public List<StoredTableInfo> TableMetadata { get; set; } = [];

    /// <summary>
    /// Citation references this file has produced or had attributed to it during the session.
    /// Populated incrementally by recall tools as they emit citations.
    /// </summary>
    [JsonPropertyName("citations")]
    public List<StoredCitationReference> Citations { get; set; } = [];

    /// <summary>Total page count produced by <c>FileManifestExtractor</c> (task 069).</summary>
    [JsonPropertyName("pageCount")]
    public int? PageCount { get; set; }

    /// <summary>
    /// Detected language as an ISO-639-1 code (e.g., "en", "fr", "de"). Null until detection
    /// completes. Intentionally NOT defaulted to "en": an explicit null signals "not yet
    /// detected", which downstream tooling can distinguish from a confident detection.
    /// </summary>
    [JsonPropertyName("language")]
    public string? Language { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.SectionInfo</c>.
/// camelCase wire format for consistency with the rest of <see cref="StoredSession"/>.
/// </summary>
public class StoredSectionInfo
{
    /// <summary>The detected section heading text (display only).</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Character offset (inclusive) of the heading within the extracted text.</summary>
    [JsonPropertyName("startCharOffset")]
    public int StartCharOffset { get; set; }

    /// <summary>Character offset (exclusive) of the end of the section.</summary>
    [JsonPropertyName("endCharOffset")]
    public int EndCharOffset { get; set; }

    /// <summary>1-based start page number; null when source format is not paginated.</summary>
    [JsonPropertyName("startPage")]
    public int? StartPage { get; set; }

    /// <summary>1-based end page number (inclusive); null when source format is not paginated.</summary>
    [JsonPropertyName("endPage")]
    public int? EndPage { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.TableInfo</c>.
/// </summary>
public class StoredTableInfo
{
    /// <summary>Display label for the table.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Character offset (inclusive) of the table within the extracted text.</summary>
    [JsonPropertyName("startCharOffset")]
    public int StartCharOffset { get; set; }

    /// <summary>1-based page number the table appears on; null for non-paginated sources.</summary>
    [JsonPropertyName("page")]
    public int? Page { get; set; }
}

/// <summary>
/// Cosmos-shape parallel of <c>Sprk.Bff.Api.Models.Ai.Chat.CitationReference</c>.
/// </summary>
public class StoredCitationReference
{
    /// <summary>
    /// The AI Search document ID (within <c>spaarke-session-files</c>) the citation points at,
    /// or a free-form symbolic source ID for non-search-backed citations.
    /// </summary>
    [JsonPropertyName("sourceId")]
    public string SourceId { get; set; } = string.Empty;

    /// <summary>Optional verbatim quote text the citation surfaces.</summary>
    [JsonPropertyName("quote")]
    public string? Quote { get; set; }

    /// <summary>1-based page number; null for non-paginated sources or section-level citations.</summary>
    [JsonPropertyName("page")]
    public int? Page { get; set; }
}
