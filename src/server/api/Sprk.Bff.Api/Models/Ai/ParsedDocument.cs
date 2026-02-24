namespace Sprk.Bff.Api.Models.Ai;

/// <summary>
/// Represents the output of document parsing, whether via Azure Document Intelligence
/// or the LlamaParse API. Callers of <c>DocumentParserRouter</c> receive this type
/// regardless of which parser was used.
/// </summary>
public record ParsedDocument
{
    /// <summary>
    /// Full text content extracted from the document. Never null on success; empty string
    /// indicates the document had no extractable text (e.g., blank scanned pages).
    /// </summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>
    /// Number of pages detected by the parser. Zero when the document format does not
    /// expose a page count (e.g., plain text files).
    /// </summary>
    public int Pages { get; init; }

    /// <summary>
    /// Structured table data extracted from the document. Each entry is a list of rows;
    /// each row is a list of cell strings. Empty when no tables were detected or when the
    /// parser does not support table extraction.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<IReadOnlyList<string>>> Tables { get; init; }
        = Array.Empty<IReadOnlyList<IReadOnlyList<string>>>();

    /// <summary>
    /// UTC timestamp at which parsing completed.
    /// </summary>
    public DateTimeOffset ExtractedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Identifies which parser produced this result. Useful for telemetry, evaluation,
    /// and debugging routing decisions.
    /// </summary>
    public DocumentParser ParserUsed { get; init; }
}

/// <summary>
/// Identifies the parser that produced a <see cref="ParsedDocument"/>.
/// </summary>
public enum DocumentParser
{
    /// <summary>
    /// Azure Document Intelligence (primary/fallback parser).
    /// </summary>
    DocumentIntelligence,

    /// <summary>
    /// LlamaParse API (enhancement for complex legal documents).
    /// </summary>
    LlamaParse
}
