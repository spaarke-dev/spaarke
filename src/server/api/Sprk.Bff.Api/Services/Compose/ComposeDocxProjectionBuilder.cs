using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

/// <summary>
/// Phase-1 server-authoritative DOCX → editor projection (design
/// <c>notes/design-server-side-docx-html-conversion.md</c>). Walks the source <c>.docx</c> ONCE and emits
/// paraId-tagged, TipTap-shaped HTML — replacing the client-side mammoth convert + position-based paraId
/// stamping that caused the recurring save-abort bug class.
/// </summary>
/// <remarks>
/// <para>
/// <b>The single-walk invariant (the whole point).</b> The recurring
/// <c>"w14:paraId matches no paragraph in the retained original"</c> save failures came from TWO engines
/// walking the same document (server <see cref="ParaIdPreParser"/> vs. client mammoth) and joining their
/// outputs by ordinal paragraph index. This builder is the single engine: it assigns each paragraph's
/// <c>w14:paraId</c> and emits that paragraph's editor block from the SAME <see cref="Paragraph"/> instance.
/// The id is looked up by object identity (<see cref="Dictionary{TKey,TValue}"/> keyed on the paragraph
/// reference), NEVER by <c>map[index]</c> — so drift, which requires positional correspondence, is
/// structurally impossible.
/// </para>
/// <para>
/// <b>Reader alignment.</b> The <see cref="ParaIdMap"/> is produced in <c>body.Descendants&lt;Paragraph&gt;()</c>
/// document order — the SAME order <see cref="DocxAnnotationReader"/> uses for its <c>ParagraphHint</c> — so
/// <see cref="ComposeService"/>'s imported-revision/comment paraId resolution keeps working unchanged.
/// </para>
/// <para>
/// <b>Revision flattening (F-02, normative — independent of mammoth).</b> Native tracked changes are emitted
/// as settled prose with all text present and wrappers stripped: <c>w:ins</c> runs → plain text; <c>w:del</c>
/// runs (<c>w:delText</c>) → plain text (present, so the client deletion overlay can anchor); a
/// paragraph-mark-deleted <c>&lt;w:p&gt;</c> is still emitted with its <c>data-paraid</c> (empty content) so
/// the count/id sequence never breaks.
/// </para>
/// <para>
/// <b>Fail-closed (F-04 / GPT §11).</b> A malformed/unreadable source returns <see cref="ComposeProjectionStatus.Failed"/>
/// with an empty HTML and <c>CanEdit=false</c> — the client must not mount a blank editable doc over a
/// non-empty baseline. This never throws (Load still returns the source bytes).
/// </para>
/// <para>
/// <b>Zero package delta (NFR-01).</b> <c>DocumentFormat.OpenXml</c> is already referenced; no SkiaSharp, no
/// OpenXmlPowerTools. <b>Pure</b> — bytes in / record out; no I/O, no Graph, no AI types (Tier-1, mirrors
/// <see cref="ParaIdPreParser"/>). <b>Privacy</b>: produces Tier-3 content — never logged; warnings carry
/// codes/counts only. Thread-safe stateless singleton (ADR-010).
/// </para>
/// </remarks>
public sealed class ComposeDocxProjectionBuilder
{
    // ST_LongHexNumber: 0 < x < 0x80000000, 8-hex uppercase — mirrors ParaIdPreParser / ComposeDocumentRenderer.
    private const uint MaxParaId = 0x80000000u;
    private const int MintRetryLimit = 1000;

    // Resource caps (GPT §13, scoped to an OBO-fetched tenant document — sane guards, not anonymous-upload hardening).
    private const int MaxParagraphs = 100_000;
    private const int MaxOutputChars = 16_000_000;

    // Heading style ids Heading1..Heading6 (mirrors ComposeDocumentRenderer's MaxHeadingLevel=6).
    private const int MaxHeadingLevel = 6;

    private readonly Func<uint> _mint;

    /// <summary>Production constructor — mints ids from a cryptographic RNG.</summary>
    public ComposeDocxProjectionBuilder() : this(DefaultMint) { }

    /// <summary>Test seam: inject a deterministic id generator (forced-collision fixtures). Internal via InternalsVisibleTo.</summary>
    internal ComposeDocxProjectionBuilder(Func<uint> mint) => _mint = mint;

    private static uint DefaultMint() => (uint)RandomNumberGenerator.GetInt32(1, int.MaxValue);

    /// <summary>
    /// Projects <paramref name="docx"/> to a paraId-tagged HTML editor representation + ordered paraId map.
    /// Never throws — an unreadable source degrades to <see cref="ComposeProjectionStatus.Failed"/>.
    /// </summary>
    public ComposeDocxProjection Build(ReadOnlyMemory<byte> docx, CancellationToken cancellationToken = default)
    {
        if (docx.IsEmpty)
        {
            return ComposeDocxProjection.Failed("empty-source");
        }

        WordprocessingDocument doc;
        MemoryStream buffer;
        try
        {
            buffer = new MemoryStream(docx.Length);
            buffer.Write(docx.Span);
            buffer.Position = 0;
            doc = WordprocessingDocument.Open(buffer, isEditable: false);
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or FileFormatException or InvalidDataException or ArgumentOutOfRangeException)
        {
            return ComposeDocxProjection.Failed("unreadable-source");
        }

        try
        {
            using (doc)
            using (buffer)
            {
                var mainPart = doc.MainDocumentPart;
                var body = mainPart?.Document?.Body;
                if (mainPart is null || body is null)
                {
                    // A body-less package is a legitimately empty document — editable-empty, not a failure.
                    return new ComposeDocxProjection { Status = ComposeProjectionStatus.Success, CanEdit = true, Html = string.Empty };
                }

                // Pass 1 (identity): assign every body paragraph a w14:paraId in Descendants order (reader-aligned),
                // keyed by INSTANCE so the render pass looks it up by identity, never by ordinal index.
                var paragraphs = body.Descendants<Paragraph>().ToList();
                if (paragraphs.Count > MaxParagraphs)
                {
                    return ComposeDocxProjection.Failed("resource-limit-paragraphs");
                }

                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in paragraphs)
                {
                    var id = p.ParagraphId?.Value;
                    if (!string.IsNullOrEmpty(id)) seen.Add(id!);
                }

                var map = new List<ParaIdMapEntry>(paragraphs.Count);
                var idByParagraph = new Dictionary<Paragraph, string>(ReferenceEqualityComparer.Instance);
                for (var i = 0; i < paragraphs.Count; i++)
                {
                    var existing = paragraphs[i].ParagraphId?.Value;
                    string id;
                    bool minted;
                    if (!string.IsNullOrEmpty(existing))
                    {
                        id = existing!.ToUpperInvariant();
                        minted = false;
                    }
                    else
                    {
                        id = MintUnique(seen);
                        seen.Add(id);
                        minted = true;
                    }
                    map.Add(new ParaIdMapEntry(i, id, minted));
                    idByParagraph[paragraphs[i]] = id;
                }

                // Pass 2 (render): ONE structural tree walk emits HTML, pulling each paragraph's id by instance.
                var ctx = new BuildContext(mainPart, idByParagraph, cancellationToken);
                RenderBlockChildren(body, ctx);
                ctx.CloseOpenList();

                // Runtime alignment guard (F-03): emitted blocks vs. id map. A shortfall means some enumerated
                // paragraph (e.g. text-box / unsupported container) was not rendered — degrade to Partial + warn,
                // never silently. Counts only (privacy).
                if (ctx.EmittedParagraphCount != map.Count)
                {
                    ctx.AddWarning("unrendered-paragraphs", Math.Abs(map.Count - ctx.EmittedParagraphCount));
                }

                var warnings = ctx.Warnings;
                var status = warnings.Count == 0 ? ComposeProjectionStatus.Success : ComposeProjectionStatus.Partial;

                return new ComposeDocxProjection
                {
                    Status = status,
                    CanEdit = true, // Partial is still editable (save is paraId-keyed delta onto the retained original).
                    Html = ctx.Html,
                    ParaIdMap = map,
                    Warnings = warnings,
                };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Defensive: any unexpected projection error fails closed rather than throwing out of Load.
            return ComposeDocxProjection.Failed("projection-error");
        }
    }

    // ── structural walk ────────────────────────────────────────────────────────────────────────────

    private void RenderBlockChildren(OpenXmlElement container, BuildContext ctx)
    {
        foreach (var child in container.Elements())
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            switch (child)
            {
                case Paragraph p:
                    RenderParagraph(p, ctx);
                    break;
                case Table t:
                    ctx.CloseOpenList();
                    RenderTable(t, ctx);
                    break;
                case SdtBlock sdt:
                    // Content control wrapping block content: descend so wrapped paragraphs are still projected.
                    ctx.AddWarning("content-control", 1);
                    var sdtContent = sdt.GetFirstChild<SdtContentBlock>();
                    if (sdtContent is not null) RenderBlockChildren(sdtContent, ctx);
                    break;
                default:
                    // sectPr, bookmarks, and other non-block markup: no editable block. (Paragraphs nested in
                    // text boxes/drawings are reached only via a Run, so they are intentionally not rendered as
                    // top-level blocks; the F-03 guard reports the count shortfall.)
                    break;
            }
        }
    }

    private void RenderParagraph(Paragraph p, BuildContext ctx)
    {
        if (!ctx.TryGetParaId(p, out var paraId))
        {
            // A paragraph not in the identity map is a nested (e.g. text-box) paragraph reached structurally —
            // do not emit it as a top-level block; the guard accounts for it.
            return;
        }

        var headingLevel = HeadingLevel(p, ctx);
        var listInfo = headingLevel is null ? ListInfo(p, ctx) : null;

        if (listInfo is not null)
        {
            ctx.EnsureList(listInfo.Ordered);
            if (listInfo.Level > 0) ctx.AddWarning("multi-level-numbering", 1);
            ctx.Append("<li><p");
            ctx.AppendParaIdAttr(paraId);
            AppendAlignment(p, ctx);
            ctx.Append(">");
            RenderInline(p, ctx);
            ctx.Append("</p></li>");
            ctx.EmittedParagraphCount++;
            return;
        }

        ctx.CloseOpenList();

        var tag = headingLevel is int lvl ? $"h{lvl}" : "p";
        ctx.Append($"<{tag}");
        ctx.AppendParaIdAttr(paraId);
        AppendAlignment(p, ctx);
        ctx.Append(">");
        RenderInline(p, ctx);
        ctx.Append($"</{tag}>");
        ctx.EmittedParagraphCount++;
    }

    private void RenderTable(Table table, BuildContext ctx)
    {
        ctx.Append("<table><tbody>");
        foreach (var row in table.Elements<TableRow>())
        {
            ctx.Append("<tr>");
            foreach (var cell in row.Elements<TableCell>())
            {
                ctx.Append("<td>");
                RenderBlockChildren(cell, ctx); // cell paragraphs continue the same id sequence
                ctx.CloseOpenList();            // a list must not leak past the cell boundary
                ctx.Append("</td>");
            }
            ctx.Append("</tr>");
        }
        ctx.Append("</tbody></table>");
    }

    // ── inline (runs / marks / hyperlinks / revision flattening) ─────────────────────────────────────

    private void RenderInline(OpenXmlElement container, BuildContext ctx)
    {
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run r:
                    RenderRun(r, ctx);
                    break;
                case Hyperlink h:
                    RenderHyperlink(h, ctx);
                    break;
                case InsertedRun ins:
                    RenderInline(ins, ctx); // F-02: emit inserted text, wrapper stripped
                    break;
                case DeletedRun del:
                    RenderInline(del, ctx); // F-02: emit deleted text (present) so the overlay can anchor
                    break;
                case SdtRun sdtRun:
                    var sdtContent = sdtRun.GetFirstChild<SdtContentRun>();
                    if (sdtContent is not null) RenderInline(sdtContent, ctx);
                    break;
                default:
                    // ParagraphProperties, bookmarks, proofErr, etc. — no inline text.
                    break;
            }
        }
    }

    private void RenderRun(Run run, BuildContext ctx)
    {
        var rPr = run.RunProperties;
        var bold = IsOn(rPr?.Bold);
        var italic = IsOn(rPr?.Italic);
        var underline = rPr?.Underline is { Val: not null } u && u.Val!.Value != UnderlineValues.None;
        var strike = IsOn(rPr?.Strike);

        if (bold) ctx.Append("<strong>");
        if (italic) ctx.Append("<em>");
        if (underline) ctx.Append("<u>");
        if (strike) ctx.Append("<s>");

        foreach (var child in run.Elements())
        {
            switch (child)
            {
                case Text t:
                    ctx.AppendEscaped(t.Text);
                    break;
                case DeletedText dt:
                    ctx.AppendEscaped(dt.Text); // F-02: deleted text present as plain text
                    break;
                case TabChar:
                    // Non-collapsing tab representation (GPT §9.1) — never a bare "\t".
                    ctx.Append("<span class=\"compose-tab\"> </span>");
                    break;
                case Break:
                    ctx.Append("<br>");
                    break;
                case NoBreakHyphen:
                    ctx.Append("‑");
                    break;
                default:
                    break;
            }
        }

        if (strike) ctx.Append("</s>");
        if (underline) ctx.Append("</u>");
        if (italic) ctx.Append("</em>");
        if (bold) ctx.Append("</strong>");
    }

    private void RenderHyperlink(Hyperlink h, BuildContext ctx)
    {
        var href = ResolveHyperlinkHref(h, ctx);
        if (href is null)
        {
            RenderInline(h, ctx); // unsafe/unknown target → emit the text without a link
            return;
        }
        ctx.Append("<a href=\"");
        ctx.AppendEscapedAttr(href);
        ctx.Append("\">");
        RenderInline(h, ctx);
        ctx.Append("</a>");
    }

    private static string? ResolveHyperlinkHref(Hyperlink h, BuildContext ctx)
    {
        // Internal anchor (bookmark) — safe, no relationship needed.
        if (!string.IsNullOrEmpty(h.Anchor?.Value))
        {
            return "#" + h.Anchor!.Value;
        }
        var rid = h.Id?.Value;
        if (string.IsNullOrEmpty(rid)) return null;
        try
        {
            var rel = ctx.MainPart.HyperlinkRelationships.FirstOrDefault(r => r.Id == rid);
            var uri = rel?.Uri?.ToString();
            if (string.IsNullOrEmpty(uri)) return null;
            // Protocol allowlist (GPT §13): http/https/mailto only. Never resolve/fetch external relationships.
            if (uri.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || uri.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }
            return null; // javascript:/data:/file:/custom → neutralized
        }
        catch
        {
            return null;
        }
    }

    // ── classification helpers ───────────────────────────────────────────────────────────────────────

    private static int? HeadingLevel(Paragraph p, BuildContext ctx)
    {
        var styleId = p.ParagraphProperties?.ParagraphStyleId?.Val?.Value;
        if (string.IsNullOrEmpty(styleId)) return null;
        // Heading1..Heading6 (and "Heading 1" tolerant) → h1..h6.
        var digits = new string(styleId!.Where(char.IsDigit).ToArray());
        if (styleId.StartsWith("Heading", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lvl)
            && lvl is >= 1 and <= MaxHeadingLevel)
        {
            return lvl;
        }
        return null;
    }

    private sealed record ListItemInfo(bool Ordered, int Level);

    private static ListItemInfo? ListInfo(Paragraph p, BuildContext ctx)
    {
        // A list item carries a DIRECT paragraph w:numPr. Style-linked heading numbering (numPr on the STYLE)
        // is intentionally NOT treated as a list — it is a heading (mirrors ComposeDocumentRenderer's model).
        var numPr = p.ParagraphProperties?.NumberingProperties;
        var numId = numPr?.NumberingId?.Val;
        if (numId is null) return null;
        var ilvl = numPr!.NumberingLevelReference?.Val?.Value ?? 0;
        var ordered = ResolveOrdered(numId.Value, ilvl, ctx);
        return new ListItemInfo(ordered, ilvl);
    }

    private static bool ResolveOrdered(int numId, int ilvl, BuildContext ctx)
    {
        try
        {
            var numbering = ctx.MainPart.NumberingDefinitionsPart?.Numbering;
            if (numbering is null) { ctx.AddWarning("numbering-unresolved", 1); return true; }

            var instance = numbering.Elements<NumberingInstance>().FirstOrDefault(n => n.NumberID?.Value == numId);
            var abstractNumId = instance?.AbstractNumId?.Val?.Value;
            if (abstractNumId is null) { ctx.AddWarning("numbering-unresolved", 1); return true; }

            var abstractNum = numbering.Elements<AbstractNum>().FirstOrDefault(a => a.AbstractNumberId?.Value == abstractNumId);
            var level = abstractNum?.Elements<Level>().FirstOrDefault(l => (l.LevelIndex?.Value ?? 0) == ilvl)
                        ?? abstractNum?.Elements<Level>().FirstOrDefault();
            var fmt = level?.NumberingFormat?.Val?.Value;
            // Bullet → unordered; anything else (decimal, lowerLetter, lowerRoman, …) → ordered.
            return fmt != NumberFormatValues.Bullet;
        }
        catch
        {
            ctx.AddWarning("numbering-unresolved", 1);
            return true;
        }
    }

    private static void AppendAlignment(Paragraph p, BuildContext ctx)
    {
        var just = p.ParagraphProperties?.Justification?.Val?.Value;
        if (just is null) return;
        string? css = null;
        if (just.Value == JustificationValues.Center) css = "center";
        else if (just.Value == JustificationValues.Right) css = "right";
        else if (just.Value == JustificationValues.Both) css = "justify";
        if (css is not null)
        {
            ctx.Append(" style=\"text-align:");
            ctx.Append(css);
            ctx.Append("\"");
        }
    }

    private static bool IsOn(OnOffType? toggle)
    {
        if (toggle is null) return false;
        // OnOffType.Val null ⇒ present-means-on; explicit false ⇒ off.
        return toggle.Val is null || toggle.Val.Value;
    }

    private string MintUnique(HashSet<string> seen)
    {
        for (var attempt = 0; attempt < MintRetryLimit; attempt++)
        {
            var candidate = _mint();
            if (candidate == 0 || candidate >= MaxParaId) continue;
            var hex = candidate.ToString("X8");
            if (!seen.Contains(hex)) return hex;
        }
        throw new InvalidOperationException($"Unable to mint a unique w14:paraId after {MintRetryLimit} attempts.");
    }

    // ── build context (per-call render state) ────────────────────────────────────────────────────────

    private sealed class BuildContext
    {
        private readonly StringBuilder _sb = new(4096);
        private readonly Dictionary<Paragraph, string> _idByParagraph;
        private readonly Dictionary<string, int> _warnings = new(StringComparer.Ordinal);
        private bool _listOpen;
        private bool _listOrdered;

        public BuildContext(MainDocumentPart mainPart, Dictionary<Paragraph, string> idByParagraph, CancellationToken ct)
        {
            MainPart = mainPart;
            _idByParagraph = idByParagraph;
            CancellationToken = ct;
        }

        public MainDocumentPart MainPart { get; }
        public CancellationToken CancellationToken { get; }
        public int EmittedParagraphCount { get; set; }
        public string Html => _sb.ToString();

        public IReadOnlyList<ComposeProjectionWarning> Warnings =>
            _warnings.Select(kv => new ComposeProjectionWarning(kv.Key, kv.Value)).ToList();

        public bool TryGetParaId(Paragraph p, out string paraId) => _idByParagraph.TryGetValue(p, out paraId!);

        public void AddWarning(string code, int count)
        {
            _warnings.TryGetValue(code, out var existing);
            _warnings[code] = existing + count;
        }

        public void Append(string s)
        {
            if (_sb.Length + s.Length > MaxOutputChars)
            {
                throw new InvalidOperationException("Compose projection exceeded the maximum output size.");
            }
            _sb.Append(s);
        }

        public void AppendParaIdAttr(string paraId)
        {
            Append(" data-paraid=\"");
            AppendEscapedAttr(paraId);
            Append("\"");
        }

        public void AppendEscaped(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '&': Append("&amp;"); break;
                    case '<': Append("&lt;"); break;
                    case '>': Append("&gt;"); break;
                    default: _sb.Append(ch); break;
                }
            }
        }

        public void AppendEscapedAttr(string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var ch in text)
            {
                switch (ch)
                {
                    case '&': Append("&amp;"); break;
                    case '<': Append("&lt;"); break;
                    case '>': Append("&gt;"); break;
                    case '"': Append("&quot;"); break;
                    default: _sb.Append(ch); break;
                }
            }
        }

        public void EnsureList(bool ordered)
        {
            if (_listOpen && _listOrdered != ordered) CloseOpenList();
            if (!_listOpen)
            {
                Append(ordered ? "<ol>" : "<ul>");
                _listOpen = true;
                _listOrdered = ordered;
            }
        }

        public void CloseOpenList()
        {
            if (!_listOpen) return;
            Append(_listOrdered ? "</ol>" : "</ul>");
            _listOpen = false;
        }
    }
}
