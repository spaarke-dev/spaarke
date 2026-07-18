using System.Text.Json.Serialization;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// FR-01a (task 026, E1 born-in-editor) — the paraId-keyed CONTENT MODEL a Compose client POSTs when it
/// saves a document that was BORN IN THE EDITOR (AI-drafted from a <c>initialHtml</c> seed, blank-new, or
/// browse-local) and therefore has NO retained load-time original to delta against. This model — NOT a
/// client <c>docx.js</c> reconstruction — is the authoring source for
/// <see cref="ComposeDocumentRenderer.SynthesizeDocument"/>, the server-side replacement for the removed
/// client exporter (task 027 makes the client stop authoring bytes and POST this instead).
/// </summary>
/// <remarks>
/// <para>
/// <b>Mirror-first (the client contract, task 027)</b>: the field names align with the TipTap / ProseMirror
/// node + mark surface the client serializes from — <c>paragraph</c> / <c>heading</c>(<c>attrs.level</c>) /
/// <c>bulletList</c>+<c>orderedList</c>&gt;<c>listItem</c> / <c>table</c>, inline <c>bold</c> / <c>italic</c>
/// / <c>underline</c> marks. The client's node→block mapper (027) FLATTENS TipTap's nested list structure
/// (<c>bulletList</c> &gt; <c>listItem</c> &gt; <c>paragraph</c>) into a linear sequence of
/// <see cref="ComposeBlockKind.ListItem"/> blocks carrying <see cref="ComposeBlock.Level"/> (nesting depth)
/// and <see cref="ComposeBlock.Ordered"/> — a legitimate projection, not a parallel schema. Each block
/// carries its <c>w14:paraId</c> (client-minted per task 011, or minted server-side by the renderer when
/// absent) so the rendered document is a first-class E2 substrate for the very next edit.
/// </para>
/// <para>
/// <b>Deterministic, not AI (ADR-039 non-conflict — design §11)</b>: this is a plain data contract consumed
/// by a deterministic OOXML authoring engine. It carries no AI reach, no routing type, no Graph type.
/// </para>
/// </remarks>
public sealed record ComposeContentModel
{
    /// <summary>The document body in document order. Empty renders a valid, empty <c>.docx</c>.</summary>
    public IReadOnlyList<ComposeBlock> Blocks { get; init; } = Array.Empty<ComposeBlock>();
}

/// <summary>The block kinds the renderer materializes into body content. Serialized as its STRING name
/// over the wire (the client posts <c>"heading"</c> etc.; the BFF has no global string-enum converter).</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeBlockKind
{
    /// <summary>A body paragraph (Normal style).</summary>
    Paragraph,

    /// <summary>A numbered clause heading — <see cref="ComposeBlock.Level"/> 1-6 → <c>Heading1..6</c> style
    /// (style-linked multi-level numbering; NO direct paragraph numId).</summary>
    Heading,

    /// <summary>A list item — <c>ListParagraph</c> style + a DIRECT <c>numPr</c> (ordered or bullet;
    /// <see cref="ComposeBlock.Level"/> = 0-based nesting depth).</summary>
    ListItem,

    /// <summary>A native table (<see cref="ComposeBlock.Table"/> carries rows/cells).</summary>
    Table,
}

/// <summary>Paragraph horizontal alignment (mirrors the TipTap <c>textAlign</c> attribute). Serialized as
/// its STRING name over the wire.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeParagraphAlignment
{
    /// <summary>No explicit alignment — inherit from the style.</summary>
    Default,
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>
/// One body block. The active fields depend on <see cref="Kind"/>: <see cref="Runs"/> for
/// Paragraph / Heading / ListItem; <see cref="Table"/> for Table.
/// </summary>
public sealed record ComposeBlock
{
    /// <summary>Which block this is (drives the style + numbering the renderer applies).</summary>
    public required ComposeBlockKind Kind { get; init; }

    /// <summary>
    /// The block's <c>w14:paraId</c> (E2 splice key). Client-minted (task 011) when present; the renderer
    /// mints an OOXML-valid one (per <see cref="ParaIdPreParser"/>'s scheme) when null/blank/invalid. Table
    /// blocks ignore this (their cell paragraphs carry their own ids); an empty table cell gets a minted id.
    /// </summary>
    public string? ParaId { get; init; }

    /// <summary>
    /// For <see cref="ComposeBlockKind.Heading"/>: the clause level 1-6 (→ <c>Heading1..6</c>). For
    /// <see cref="ComposeBlockKind.ListItem"/>: the 0-based list NESTING depth (0-8). Ignored otherwise.
    /// Out-of-range values are clamped by the renderer.
    /// </summary>
    public int Level { get; init; }

    /// <summary>
    /// <see cref="ComposeBlockKind.ListItem"/> only: <c>true</c> = an ORDERED (numbered) list item;
    /// <c>false</c> = a BULLET list item. Ignored for other kinds.
    /// </summary>
    public bool Ordered { get; init; }

    /// <summary>
    /// <see cref="ComposeBlockKind.ListItem"/> + <see cref="Ordered"/> only: <c>true</c> marks the FIRST
    /// item of a distinct ordered list that must RESTART numbering at 1 (a fresh <c>w:num</c> instance with
    /// a <c>startOverride</c>). <c>false</c> continues the current ordered list. Ignored otherwise.
    /// </summary>
    public bool StartsNewList { get; init; }

    /// <summary>Inline runs for Paragraph / Heading / ListItem (empty → an empty paragraph). Ignored for
    /// Table.</summary>
    public IReadOnlyList<ComposeInlineRun> Runs { get; init; } = Array.Empty<ComposeInlineRun>();

    /// <summary>Optional paragraph alignment for Paragraph / Heading / ListItem.</summary>
    public ComposeParagraphAlignment Alignment { get; init; } = ComposeParagraphAlignment.Default;

    /// <summary><see cref="ComposeBlockKind.Table"/> only: the table's rows/cells. Null for other kinds.</summary>
    public ComposeTable? Table { get; init; }
}

/// <summary>
/// One inline run — a span of <paramref name="Text"/> with optional character formatting. Mirrors the
/// TipTap <c>bold</c> / <c>italic</c> / <c>underline</c> marks the client serializes (→ <c>w:b</c> /
/// <c>w:i</c> / <c>w:u</c>).
/// </summary>
public sealed record ComposeInlineRun
{
    /// <summary>The run text (sanitized of XML-illegal control characters by the renderer).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Bold (<c>w:b</c>).</summary>
    public bool Bold { get; init; }

    /// <summary>Italic (<c>w:i</c>).</summary>
    public bool Italic { get; init; }

    /// <summary>Single underline (<c>w:u val="single"</c>).</summary>
    public bool Underline { get; init; }
}

/// <summary>A native table (<c>w:tbl</c>): an ordered list of rows.</summary>
public sealed record ComposeTable
{
    /// <summary>The table rows in order. An empty table is not rendered (Word requires ≥1 row).</summary>
    public IReadOnlyList<ComposeTableRow> Rows { get; init; } = Array.Empty<ComposeTableRow>();
}

/// <summary>A table row (<c>w:tr</c>): an ordered list of cells.</summary>
public sealed record ComposeTableRow
{
    /// <summary>The row cells in order.</summary>
    public IReadOnlyList<ComposeTableCell> Cells { get; init; } = Array.Empty<ComposeTableCell>();
}

/// <summary>
/// A table cell (<c>w:tc</c>): a nested block sequence (Word requires each cell to contain ≥1 paragraph —
/// the renderer emits an empty paragraph for an empty cell). Nested tables are supported by including a
/// <see cref="ComposeBlockKind.Table"/> block.
/// </summary>
public sealed record ComposeTableCell
{
    /// <summary>The cell's block content (each paragraph gets its own minted/carried <c>w14:paraId</c>).</summary>
    public IReadOnlyList<ComposeBlock> Blocks { get; init; } = Array.Empty<ComposeBlock>();

    /// <summary>Header cell — the renderer bolds its text (cosmetic; no <c>tblHeader</c> row property).</summary>
    public bool IsHeader { get; init; }
}
