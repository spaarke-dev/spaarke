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

    /// <summary>
    /// Task 024: the document's COMMENTS (from <c>word/comments.xml</c>), keyed by <see cref="ComposeComment.Id"/>
    /// — the same ids the body's <see cref="ComposeInlineRun.CommentAnchor"/> markers reference. In CARRIER
    /// mode the source comments part is preserved byte-identically and only the body ANCHORS are re-authored
    /// (this list is projection output / read model there); in blank-package synthesize the renderer authors
    /// a comments part from this list. Server-set; the client mapper never sets it.
    /// </summary>
    public IReadOnlyList<ComposeComment> Comments { get; init; } = Array.Empty<ComposeComment>();
}

/// <summary>One document comment (task 024) — the near-term-tier projection of a <c>w:comment</c>: identity,
/// attribution, and plain text (paragraphs joined by <c>\n</c>; rich comment content flattens).</summary>
public sealed record ComposeComment
{
    /// <summary>The OOXML comment id (decimal, matches the body anchors' <see cref="ComposeCommentAnchor.Id"/>).</summary>
    public required int Id { get; init; }

    public string Author { get; init; } = string.Empty;

    public string? Initials { get; init; }

    /// <summary>ISO-8601 timestamp as authored, or null.</summary>
    public string? Date { get; init; }

    /// <summary>The comment's plain text (paragraphs joined by <c>\n</c>).</summary>
    public string Text { get; init; } = string.Empty;
}

/// <summary>
/// A comment range anchor carried as a dedicated marker run (task 024 — same mechanism as
/// <see cref="ComposeInlineRun.IsPageBreak"/>): <see cref="Kind"/> Start emits <c>w:commentRangeStart</c>;
/// Kind End emits <c>w:commentRangeEnd</c> IMMEDIATELY followed by the <c>w:commentReference</c> run (the
/// reference is folded into the End marker — Word's canonical adjacency). A POINT comment (bare reference,
/// no range) projects as an adjacent Start+End pair.
/// </summary>
public sealed record ComposeCommentAnchor
{
    public required ComposeCommentAnchorKind Kind { get; init; }

    /// <summary>The comment id this anchor belongs to (see <see cref="ComposeComment.Id"/>).</summary>
    public required int Id { get; init; }
}

/// <summary>Anchor kind for <see cref="ComposeCommentAnchor"/>. Serialized as its STRING name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeCommentAnchorKind
{
    /// <summary>Range start (<c>w:commentRangeStart</c>).</summary>
    Start,

    /// <summary>Range end (<c>w:commentRangeEnd</c> + the folded <c>w:commentReference</c> run).</summary>
    End,
}

/// <summary>Revision kind for <see cref="ComposeRevision"/> (task 025). Serialized as its STRING name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeRevisionKind
{
    /// <summary>A tracked INSERTION (<c>w:ins</c> at run level; <c>w:rPr/w:ins</c> on a paragraph mark).</summary>
    Inserted,

    /// <summary>A tracked DELETION (<c>w:del</c> at run level — content authored as <c>w:delText</c>;
    /// <c>w:rPr/w:del</c> on a paragraph mark — the mark is pending-deleted, accepting merges the
    /// paragraph with its successor).</summary>
    Deleted,
}

/// <summary>
/// One tracked-change identity (task 025) — the WHO/WHEN of a <c>w:ins</c>/<c>w:del</c> revision. The model
/// carries attribution only; the revision <c>w:id</c> is ALWAYS server-minted at render (monotonic, seeded
/// above the carrier's existing ids) and never client-controlled. The renderer SANITIZES <see cref="Author"/>
/// (XML-illegal control chars stripped, length-clamped, empty → <c>"Unknown"</c> — <c>@w:author</c> is
/// schema-required) and emits <see cref="Date"/> only when it parses as a round-trip timestamp
/// (client junk is dropped; <c>@w:date</c> is schema-optional).
/// </summary>
public sealed record ComposeRevision
{
    public required ComposeRevisionKind Kind { get; init; }

    /// <summary>Revision author as captured from the source (<c>@w:author</c>).</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Revision timestamp as authored (<c>@w:date</c>, xsd:dateTime), or null.</summary>
    public string? Date { get; init; }
}

/// <summary>
/// A tracked FORMATTING-change record (task 025) — <c>w:pPrChange</c> (paragraph) / <c>w:rPrChange</c>
/// (run): the revision identity plus the PREVIOUS properties Word restores on reject.
/// <see cref="PreviousPropertiesXml"/> is a SERVER-SET opaque carry of the change's child element
/// (<c>w:pPr</c> / <c>w:rPr</c>) exactly as captured by the projection — the previous formatting can
/// contain properties far outside the thin model (indentation, spacing, tabs …), so a typed carry would
/// mis-state the reject target. The renderer NEVER string-injects it: the XML is parsed through the typed
/// OpenXML SDK class, validated for element name + namespace, and the whole change record is dropped when
/// parsing fails (client junk cannot reach the package). Clients preserve this untouched on re-post.
/// </summary>
public sealed record ComposeFormatChange
{
    /// <summary>Revision author (<c>@w:author</c>; sanitized at render like <see cref="ComposeRevision.Author"/>).</summary>
    public string Author { get; init; } = string.Empty;

    /// <summary>Revision timestamp as authored, or null.</summary>
    public string? Date { get; init; }

    /// <summary>The change's previous-properties child (<c>w:pPr</c> for a paragraph change, <c>w:rPr</c>
    /// for a run change) as OuterXml. Server-set; SDK-parse-gated at render; null/invalid → the change
    /// record is not emitted (the CURRENT formatting stands — equivalent to accepting the change).</summary>
    public string? PreviousPropertiesXml { get; init; }
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
    /// a <c>startOverride</c>). <c>false</c> continues the current ordered list — honored ACROSS intervening
    /// non-list blocks (task 021 / review 020-R1: the renderer keeps the current ordered instance open until
    /// a <c>StartsNewList</c> item or a table-cell boundary, mirroring Word's per-<c>numId</c> counters).
    /// Ignored when <see cref="NumId"/> is set (source identity is authoritative) and for other kinds.
    /// NOTE (Step-9.5 F6): when set by the SERVER projection this flag is scoped PER CONTAINER ("first
    /// appearance of the numId in this body/cell walk") while <see cref="NumId"/> continuity is
    /// DOCUMENT-scoped — a numId seen in the body and again inside a table cell is flagged
    /// <c>StartsNewList=true</c> in the cell yet renders as (and Word displays) a CONTINUATION of the same
    /// instance. Consumers must key continuity decisions on <see cref="NumId"/>, not this flag.
    /// </summary>
    public bool StartsNewList { get; init; }

    /// <summary>
    /// <see cref="ComposeBlockKind.ListItem"/> only (task 021): the SOURCE document's numbering-instance
    /// identity — the <c>w:numPr/w:numId</c> the paragraph carried when the server-side projection
    /// (<c>ComposeDocxProjectionBuilder.BuildContentModel</c>) captured it. The model carries only the
    /// IDENTITY; the numbering SCHEME (<c>w:abstractNum</c> levels, <c>numFmt</c>/<c>lvlText</c>) stays in
    /// the retained source package ("carrier"). On render, an item whose <c>NumId</c> exists in the carrier
    /// references it DIRECTLY — Word's per-instance counters then reproduce the source labels exactly
    /// (golden-label parity by construction; interruption-continuity included). A <c>NumId</c> unknown to
    /// the render target (blank-package synthesize, or a foreign carrier) maps per-distinct-source-id to an
    /// allocated instance, preserving list identity/continuity under the renderer's own scheme. Null =
    /// born-in-editor item (the client mapper never sets this; <see cref="StartsNewList"/> governs).
    /// </summary>
    public int? NumId { get; init; }

    /// <summary>Inline runs for Paragraph / Heading / ListItem (empty → an empty paragraph). Ignored for
    /// Table.</summary>
    public IReadOnlyList<ComposeInlineRun> Runs { get; init; } = Array.Empty<ComposeInlineRun>();

    /// <summary>Optional paragraph alignment for Paragraph / Heading / ListItem.</summary>
    public ComposeParagraphAlignment Alignment { get; init; } = ComposeParagraphAlignment.Default;

    /// <summary>Task 023: <c>w:pPr/w:pageBreakBefore</c> — the paragraph starts on a new page. Carried for
    /// Paragraph / Heading / ListItem; captured by the server projection (mirrors Word's paragraph-level
    /// page-break idiom, distinct from the run-level <see cref="ComposeInlineRun.IsPageBreak"/>).
    /// ACCEPTED DEGRADATION (review 023-F2): an EXPLICIT <c>w:val="false"</c> override — turning OFF a
    /// carrier STYLE's page-break-before on one paragraph — collapses to absent here (bool, not tri-state),
    /// so such a paragraph regains the style's break on save; consistent with the model's bold/italic
    /// OnOff collapse, rare in practice, widening is 026-shaped if it surfaces.</summary>
    public bool PageBreakBefore { get; init; }

    /// <summary><see cref="ComposeBlockKind.Table"/> only: the table's rows/cells. Null for other kinds.</summary>
    public ComposeTable? Table { get; init; }

    /// <summary>
    /// Task 025: a tracked revision on this paragraph's MARK (<c>w:pPr/w:rPr/w:ins</c> or <c>w:del</c>).
    /// Kind Deleted = the paragraph mark is pending-deleted (accepting merges this paragraph with the
    /// next); Kind Inserted = the paragraph was created while tracking. Carried for Paragraph / Heading /
    /// ListItem; ignored for Table. Server-set; preserve untouched on re-post.
    /// </summary>
    public ComposeRevision? MarkRevision { get; init; }

    /// <summary>
    /// Task 025: a tracked paragraph-FORMATTING change (<c>w:pPr/w:pPrChange</c> — rejecting restores the
    /// previous paragraph properties). See <see cref="ComposeFormatChange"/> for the opaque-carry +
    /// SDK-parse-gate contract. Server-set; preserve untouched on re-post.
    /// </summary>
    public ComposeFormatChange? PropertiesChange { get; init; }
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

    /// <summary>
    /// G5 (FR-05, task 033): optional hyperlink target. When non-null/non-empty the renderer wraps this
    /// run in a clean <c>w:hyperlink</c> pointing at an EXTERNAL relationship (added to the
    /// MainDocumentPart's <c>.rels</c> with <c>TargetMode="External"</c>) — the authored (clean) path's
    /// hyperlink representation (mirrors the TipTap <c>link</c> mark's <c>href</c>). Null for a plain run.
    /// </summary>
    public string? Href { get; init; }

    /// <summary>
    /// Task 023: this run IS a manual page break (<c>w:br w:type="page"</c>). When true the renderer emits
    /// exactly that break run and every other field on this run (<see cref="Text"/>, marks,
    /// <see cref="Href"/>) is ignored. Captured by the server projection at the break's exact inline
    /// position (splitting the surrounding text into separate runs); soft line/column breaks remain the
    /// counted <c>line-break-flattened</c> degradation.
    /// </summary>
    public bool IsPageBreak { get; init; }

    /// <summary>
    /// Task 024: this run IS a comment range anchor (see <see cref="ComposeCommentAnchor"/>). When non-null
    /// the renderer emits the anchor markup and every other field on this run is ignored (marker-run
    /// contract, like <see cref="IsPageBreak"/>). Mutually exclusive with <see cref="IsPageBreak"/> by
    /// construction. Server-set; preserve untouched on re-post.
    /// </summary>
    public ComposeCommentAnchor? CommentAnchor { get; init; }

    /// <summary>
    /// Task 025: the tracked revision this run belongs to (<c>w:ins</c> / <c>w:del</c>). The renderer
    /// GROUPS consecutive runs carrying the same revision identity (kind + author + date, after
    /// normalization) into ONE wrapper element with a freshly minted <c>w:id</c>; a Deleted run's text is
    /// authored as <c>w:delText</c>. Page-break marker runs participate (a break can be part of an
    /// insertion/deletion); comment-anchor marker runs never carry this (anchors are emitted at paragraph
    /// level outside revision wrappers). Null = settled prose. Server-set; preserve untouched on re-post.
    /// </summary>
    public ComposeRevision? Revision { get; init; }

    /// <summary>
    /// Task 025: a tracked run-FORMATTING change (<c>w:rPr/w:rPrChange</c> — rejecting restores the
    /// previous run properties). Attached to the FIRST model run projected from the source run when a
    /// source run splits (page-break markers). See <see cref="ComposeFormatChange"/> for the opaque-carry +
    /// SDK-parse-gate contract. Server-set; preserve untouched on re-post.
    /// </summary>
    public ComposeFormatChange? FormatChange { get; init; }
}

/// <summary>A native table (<c>w:tbl</c>): an ordered list of rows plus the STRUCTURAL facts the render-on-save
/// path must reproduce (task 022 widening — spans/merges/widths/borders/style identity; visual chrome beyond
/// this closed set flattens LOUDLY via the <c>table-formatting-flattened</c> projection warning).</summary>
public sealed record ComposeTable
{
    /// <summary>The table rows in order. An empty table is not rendered (Word requires ≥1 row).</summary>
    public IReadOnlyList<ComposeTableRow> Rows { get; init; } = Array.Empty<ComposeTableRow>();

    /// <summary>Task 022: the source table STYLE identity (<c>w:tblPr/w:tblStyle</c>). In carrier mode the
    /// preserved styles part keeps the definition, so a styled table keeps its look; a dangling reference
    /// (blank-package synthesize) falls back to Normal Table — the same accepted posture as <c>pStyle</c>.
    /// Null for born-in-editor tables.</summary>
    public string? StyleId { get; init; }

    /// <summary>Task 022: the table width (<c>w:tblPr/w:tblW</c>). Null = not specified (Word auto layout)
    /// for source-faithful tables; born-in-editor tables get the renderer's 100%-pct default.</summary>
    public ComposeTableWidth? Width { get; init; }

    /// <summary>
    /// Task 022: the table borders (<c>w:tblPr/w:tblBorders</c>). SEMANTICS ARE TRI-STATE and load-bearing:
    /// <c>null</c> = born-in-editor table → the renderer applies its own single-border chrome (bit-stable
    /// legacy look); NON-null = source-faithful mode — only the edges present are emitted, and an instance
    /// with ALL edges null reproduces a BORDERLESS table (legal signature-block layout tables must not grow
    /// borders on save). The PROJECTOR always sets this (possibly empty) for projected tables.
    /// NOTE (022-F5): this field is ALSO the mode discriminator — <see cref="StyleId"/>, <see cref="Width"/>
    /// and <see cref="LookHex"/> are honored only in source-faithful mode (Borders non-null); in legacy mode
    /// the chrome replaces them wholesale. Cell/row facts (spans, merges, header rows, grid widths) are
    /// honored in both modes.
    /// </summary>
    public ComposeTableBorders? Borders { get; init; }

    /// <summary>Task 022: explicit column widths in twips (<c>w:tblGrid/w:gridCol/@w:w</c>, document order).
    /// Null when the source grid carries no explicit widths — the renderer then emits width-less columns
    /// sized to the widest row's total span.</summary>
    public IReadOnlyList<string>? GridColumnWidthsTwips { get; init; }

    /// <summary>Task 022: the <c>w:tblLook/@w:val</c> hex (conditional-formatting flags consumed by
    /// <see cref="StyleId"/>'s banding). Null for born-in-editor tables (renderer default "04A0").</summary>
    public string? LookHex { get; init; }
}

/// <summary>A width value (<c>w:tblW</c> / <c>w:tcW</c>): <see cref="Type"/> is the OOXML
/// <c>ST_TblWidth</c> name (<c>auto</c> / <c>dxa</c> / <c>pct</c> / <c>nil</c>), <see cref="Value"/> the
/// numeric string exactly as authored.</summary>
public sealed record ComposeTableWidth
{
    public string Type { get; init; } = "auto";
    public string Value { get; init; } = "0";
}

/// <summary>The six table border edges (task 022). Each null edge is simply not emitted — see
/// <see cref="ComposeTable.Borders"/> for the tri-state contract.</summary>
public sealed record ComposeTableBorders
{
    public ComposeTableBorderEdge? Top { get; init; }
    public ComposeTableBorderEdge? Left { get; init; }
    public ComposeTableBorderEdge? Bottom { get; init; }
    public ComposeTableBorderEdge? Right { get; init; }
    public ComposeTableBorderEdge? InsideHorizontal { get; init; }
    public ComposeTableBorderEdge? InsideVertical { get; init; }
}

/// <summary>One border edge: <see cref="Val"/> is the OOXML <c>ST_Border</c> name (<c>single</c>,
/// <c>none</c>, <c>dotted</c>, …), <see cref="Size"/> eighths-of-a-point, <see cref="Color"/> hex or
/// <c>auto</c>.</summary>
public sealed record ComposeTableBorderEdge
{
    public string Val { get; init; } = "single";
    public uint? Size { get; init; }
    public string? Color { get; init; }
}

/// <summary>Vertical-merge state of a cell (<c>w:tcPr/w:vMerge</c>, task 022). Serialized as its STRING
/// name over the wire.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ComposeVerticalMerge
{
    /// <summary>Not merged vertically.</summary>
    None,

    /// <summary>Starts a vertical merge (<c>w:vMerge w:val="restart"</c>).</summary>
    Restart,

    /// <summary>Continues the merge from the cell above (<c>w:vMerge</c> with no/continue val).</summary>
    Continue,
}

/// <summary>A table row (<c>w:tr</c>): an ordered list of cells.</summary>
public sealed record ComposeTableRow
{
    /// <summary>The row cells in order.</summary>
    public IReadOnlyList<ComposeTableCell> Cells { get; init; } = Array.Empty<ComposeTableCell>();

    /// <summary>Task 022: <c>w:trPr/w:tblHeader</c> — the row repeats as a header at the top of every page
    /// the table spans. Distinct from <see cref="ComposeTableCell.IsHeader"/> (a cosmetic bolding hint).</summary>
    public bool RepeatAsHeaderRow { get; init; }
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

    /// <summary>Header cell — the renderer bolds its text (cosmetic; no <c>tblHeader</c> row property —
    /// that is <see cref="ComposeTableRow.RepeatAsHeaderRow"/>).</summary>
    public bool IsHeader { get; init; }

    /// <summary>Task 022: horizontal span (<c>w:tcPr/w:gridSpan</c>) — how many grid columns this cell
    /// occupies. Values &lt; 1 are treated as 1.</summary>
    public int GridSpan { get; init; } = 1;

    /// <summary>Task 022: vertical-merge state (<c>w:tcPr/w:vMerge</c>).</summary>
    public ComposeVerticalMerge VMerge { get; init; } = ComposeVerticalMerge.None;

    /// <summary>Task 022: the cell width (<c>w:tcPr/w:tcW</c>). Null = not specified.</summary>
    public ComposeTableWidth? Width { get; init; }

    /// <summary>Task 022: vertical alignment (<c>w:tcPr/w:vAlign</c>: <c>top</c> / <c>center</c> /
    /// <c>bottom</c>). Null = the cell carries no DIRECT vAlign — in the renderer's source-faithful mode
    /// (table <see cref="ComposeTable.Borders"/> non-null) nothing is emitted so the TABLE-STYLE chain
    /// governs exactly as in the source (review 022-F4: stamping a default here would override a styled
    /// table's center/bottom); in born-in-editor mode (Borders null) the legacy <c>center</c> chrome
    /// applies.</summary>
    public string? VerticalAlignment { get; init; }
}
