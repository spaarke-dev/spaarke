namespace Sprk.Bff.Api.Services.Ai.PublicContracts;

/// <summary>
/// Task 040 (spaarkeai-compose-r6, FR-06) — the STRUCTURED document layout the AI intake path
/// (Azure Document Intelligence <c>prebuilt-layout</c> via the existing
/// <c>ITextExtractor</c>/<c>DocumentIntelligenceService</c>/<c>DocumentParserRouter</c> stack) exposes
/// through the PublicContracts facade. Deliberately NEUTRAL: paragraphs-with-roles + tables in document
/// order — no Azure SDK type, no AI-internal type — so facade consumers (the Compose PDF intake is the
/// first) can project it without importing <c>Services/Ai</c> internals (ADR-013).
/// </summary>
/// <remarks>
/// This is a PARSE product, not an AI-dispatch product (ADR-039 — no engine, no routing). Fixed-layout
/// facts that a flow-document consumer cannot represent faithfully (absolute positioning, page chrome,
/// per-page pagination) are surfaced as data here (roles, page numbers) and degraded LOUDLY by the
/// consumer's projector — never silently dropped in this contract.
/// </remarks>
public sealed record DocumentLayout
{
    /// <summary>Pages detected by the layout analysis (1+ for any real document).</summary>
    public int PageCount { get; init; }

    /// <summary>The document content in document order: paragraph and table blocks interleaved
    /// exactly as they appear in the source (span-ordered). Paragraphs that are table-cell content
    /// appear ONLY inside their table block, never duplicated as loose paragraphs.</summary>
    public IReadOnlyList<DocumentLayoutBlock> Blocks { get; init; } = Array.Empty<DocumentLayoutBlock>();
}

/// <summary>One document-order block: exactly one of <see cref="Paragraph"/> / <see cref="Table"/> is
/// non-null (closed two-case union — mirrors the layout model's block vocabulary).</summary>
public sealed record DocumentLayoutBlock
{
    public DocumentLayoutParagraph? Paragraph { get; init; }

    public DocumentLayoutTable? Table { get; init; }
}

/// <summary>The layout paragraph roles surfaced by the analysis (mirror of the Azure DI
/// <c>ParagraphRole</c> vocabulary, plus <see cref="Body"/> for role-less prose).</summary>
public enum DocumentLayoutParagraphRole
{
    /// <summary>Role-less body prose (the overwhelmingly common case).</summary>
    Body,

    /// <summary>Document title.</summary>
    Title,

    /// <summary>Section heading.</summary>
    SectionHeading,

    /// <summary>Running page header (page chrome — repeats per page).</summary>
    PageHeader,

    /// <summary>Running page footer (page chrome — repeats per page).</summary>
    PageFooter,

    /// <summary>Bare page number (page chrome).</summary>
    PageNumber,

    /// <summary>Footnote text.</summary>
    Footnote,

    /// <summary>Formula block.</summary>
    Formula,
}

/// <summary>One layout paragraph: plain text + role + the 1-based page it starts on (0 when the
/// analysis reported no bounding region).</summary>
public sealed record DocumentLayoutParagraph(string Text, DocumentLayoutParagraphRole Role, int PageNumber);

/// <summary>One layout table. <see cref="Cells"/> carries ONLY the anchor cells the analysis reports —
/// grid positions covered by a <see cref="DocumentLayoutTableCell.RowSpan"/>/<see
/// cref="DocumentLayoutTableCell.ColumnSpan"/> have no entry (the consumer reconstructs them).</summary>
public sealed record DocumentLayoutTable(
    int RowCount,
    int ColumnCount,
    IReadOnlyList<DocumentLayoutTableCell> Cells,
    int PageNumber);

/// <summary>One table anchor cell (0-based grid position; spans ≥ 1; <see cref="IsHeader"/> from the
/// analysis' column/row-header cell kinds).</summary>
public sealed record DocumentLayoutTableCell(
    int RowIndex,
    int ColumnIndex,
    int RowSpan,
    int ColumnSpan,
    string Text,
    bool IsHeader);
