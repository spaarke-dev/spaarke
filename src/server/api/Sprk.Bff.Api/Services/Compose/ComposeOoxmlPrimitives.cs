// Task 071 (Track D) — OOXML reading primitives shared by BOTH projection pipelines.
//
// WHY THIS EXISTS. `ComposeDocxProjectionBuilder` contains two independent output pipelines —
// `Build()` (paraId-tagged HTML for the editor) and `BuildContentModel()` (the canonical model the
// renderer consumes). They share nothing except a handful of predicates that answer "what does this
// OOXML construct actually say?", independent of either output shape. Those predicates change for a
// third reason — OOXML semantics — so they live here rather than being owned by one pipeline and
// borrowed by the other.

using System.Globalization;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Sprk.Bff.Api.Services.Compose;

internal static class ComposeOoxmlPrimitives
{
    // Heading style ids Heading1..Heading6 (mirrors ComposeDocumentRenderer's MaxHeadingLevel=6).
    private const int MaxHeadingLevel = 6;

    /// <summary>
    /// OOXML toggle semantics: an ABSENT element is off, but a PRESENT element with no `w:val` is ON
    /// (present-means-on) — only an explicit `w:val="false"` turns it off. Getting this backwards
    /// silently drops bold/italic/strike from every run that used the shorthand form, which is how
    /// Word actually writes them.
    /// </summary>
    internal static bool IsOn(OnOffType? toggle)
    {
        if (toggle is null) return false;
        // OnOffType.Val null ⇒ present-means-on; explicit false ⇒ off.
        return toggle.Val is null || toggle.Val.Value;
    }

    internal enum FieldPhase { None, Code, Result }

    /// <summary>
    /// Per-container scan state for a <c>w:fldChar</c> begin/instrText/separate/result/end run sequence.
    /// <see cref="Depth"/> tracks NESTING (e.g. <c>{ IF {PAGE} = 1 ... }</c>) so only the OUTERMOST
    /// <c>end</c> closes the atom; an inner field's own separate/result folds into the outer atom's display
    /// text rather than being modeled precisely (documented simplification — not exercised by the corpus).
    /// Each of <see cref="CollectRunBoundaries"/> and <see cref="RenderInline"/> keeps its OWN instance, so
    /// the two mirrored walks stay independent but deterministic over the same input (this file's existing
    /// parallel-walk pattern — see <see cref="RunEditorLength"/> vs <see cref="RenderRun"/>).
    /// </summary>
    internal sealed class FieldScanState
    {
        public int Depth;
        public FieldPhase Phase = FieldPhase.None;
        public readonly List<Run> ResultRuns = new();

        /// <summary>
        /// Task 049: the field INSTRUCTION accumulated from the code phase's <c>w:instrText</c> /
        /// <c>w:delInstrText</c> children. Swallowing these was correct while a field could only ever be
        /// flattened to its display text; carrying the field needs the half that says what it IS.
        /// </summary>
        public readonly StringBuilder Instruction = new();

        /// <summary>
        /// Task 049: the deepest nesting reached in this span. The INSTRUCTION carry is available only at 1
        /// — at 2 the inner field's own <c>w:instrText</c> has been folded into this accumulation, so the
        /// string is a concatenation that would author a DIFFERENT field. Recorded rather than inferred from
        /// <see cref="Depth"/>, which is back at 0 by the time the caller decides. (Task 058: at 2 the field
        /// is still carried — by <see cref="SpanRuns"/>, which never takes the instruction apart.)
        /// </summary>
        public int MaxDepth;

        /// <summary>
        /// Task 058: every run this span consumed, in document order — the <c>begin</c>, the code phase, any
        /// nested field's own runs, the <c>separate</c>, the result runs and the <c>end</c>.
        /// </summary>
        /// <remarks>
        /// This is the whole of the nested carry. <see cref="Instruction"/> is a lossy summary of the code
        /// phase and cannot describe a tree; this is the tree, unread. Accumulated unconditionally rather
        /// than only when nesting appears, because the decision is made at the close and the runs are gone
        /// by then — and because a conditional accumulation would be a second rule to keep in step with the
        /// first. Costs one list of references per field span.
        /// </remarks>
        public readonly List<Run> SpanRuns = new();

        /// <summary><c>w:fldLock</c> on the outermost <c>begin</c> — the author froze this field.</summary>
        public bool Locked;

        /// <summary><c>w:dirty</c> on the outermost <c>begin</c> — re-evaluate on next open.</summary>
        public bool Dirty;

        public void Reset()
        {
            Depth = 0;
            Phase = FieldPhase.None;
            ResultRuns.Clear();
            Instruction.Clear();
            SpanRuns.Clear();
            MaxDepth = 0;
            Locked = false;
            Dirty = false;
        }
    }

    /// <summary>
    /// Advances <paramref name="field"/> for one candidate <paramref name="run"/>. Returns <c>true</c> when
    /// <paramref name="run"/> was consumed as part of a field (control markup, a swallowed field-code run,
    /// or an accumulated result run); <paramref name="closed"/> distinguishes "the outermost end just
    /// closed — emit the atom now" from "still mid-field, nothing to emit yet". A run NOT participating in
    /// a field returns <c>false</c> and MUST be handled as ordinary content by the caller. Never throws — a
    /// stray separate/end with no matching begin fails open (returns <c>false</c>, treated as ordinary
    /// empty content) rather than corrupting the scan state (F-04 fail-closed philosophy).
    /// </summary>
    internal static bool TryAdvanceFieldScan(Run run, FieldScanState field, out bool closed)
    {
        closed = false;
        var fldChar = run.GetFirstChild<FieldChar>();
        if (fldChar is not null)
        {
            var type = fldChar.FieldCharType?.Value;
            if (type == FieldCharValues.Begin)
            {
                if (field.Depth == 0)
                {
                    field.Phase = FieldPhase.Code;
                    field.ResultRuns.Clear();
                    field.Instruction.Clear();
                    field.SpanRuns.Clear();
                    // Task 049: fldLock / dirty are declared on the OUTERMOST begin and govern the whole
                    // field, so they are read here and not overwritten by a nested begin below.
                    field.Locked = fldChar.FieldLock?.Value == true;
                    field.Dirty = fldChar.Dirty?.Value == true;
                }
                field.Depth++;
                if (field.Depth > field.MaxDepth) field.MaxDepth = field.Depth;
                field.SpanRuns.Add(run);
                return true;
            }
            if (type == FieldCharValues.Separate)
            {
                if (field.Depth == 0) return false; // stray separate outside any field — fail open
                if (field.Phase == FieldPhase.Code) field.Phase = FieldPhase.Result;
                field.SpanRuns.Add(run);
                return true;
            }
            if (type == FieldCharValues.End)
            {
                if (field.Depth == 0) return false; // stray end outside any field — fail open
                field.Depth--;
                if (field.Depth == 0) closed = true;
                field.SpanRuns.Add(run);
                return true;
            }
            return false; // unrecognized/absent FieldCharType — treat as ordinary (empty) content
        }

        if (field.Depth > 0)
        {
            // Task 058: recorded on EVERY consumed-run path, control markup included. The span carry needs
            // the whole sequence, and a run recorded on some paths but not others would be a hole that only
            // shows up as a mangled field in a saved document.
            field.SpanRuns.Add(run);
            if (field.Phase == FieldPhase.Result)
            {
                field.ResultRuns.Add(run); // cached result content — becomes the atom's display text
            }
            else
            {
                // Phase == Code: field-code runs are never editor-VISIBLE, and still are not — the atom's
                // display text is unchanged. Task 049 accumulates them anyway, because the instruction is
                // what makes the construct a field rather than the number it printed last time.
                foreach (var child in run.ChildElements)
                {
                    switch (child)
                    {
                        case FieldCode code: field.Instruction.Append(code.Text); break;
                        case DeletedFieldCode deleted: field.Instruction.Append(deleted.Text); break;
                        default: break;
                    }
                }
            }
            return true;
        }

        return false;
    }

    /// <summary>A run carrying a complex/floating object — DrawingML (<c>w:drawing</c>, image/shape), an
    /// OLE embed (<c>w:object</c>), or a legacy VML fallback picture (<c>w:pict</c>, task 022 WS-2
    /// construct audit: previously fell through <see cref="RenderRun"/>'s default case with zero HTML,
    /// zero offset-table length, and no warning — a genuine silent drop per F-1; now the same non-editable
    /// atom placeholder as <c>w:drawing</c>/<c>w:object</c>, since it is equally an opaque image/shape
    /// construct, never opened for display (I-4)).</summary>
    internal static bool IsComplexObjectRun(Run run) =>
        run.GetFirstChild<Drawing>() is not null
        || run.GetFirstChild<EmbeddedObject>() is not null
        || run.GetFirstChild<Picture>() is not null;

    /// <summary>
    /// The editor-visible display text an opaque atom shows — the SAME text/glyph counting convention as
    /// <see cref="RunEditorLength"/> (so an atom's offset-table length always equals the exact character
    /// count the HTML render emits inside its placeholder — the two mirrored walks stay consistent by
    /// construction, not by sharing state). Never returns document formatting, only text content.
    /// </summary>
    internal static string ExtractRunsDisplayText(IEnumerable<Run> runs)
    {
        var sb = new StringBuilder();
        foreach (var run in runs)
        {
            foreach (var child in run.Elements())
            {
                switch (child)
                {
                    case Text t: sb.Append(t.Text); break;
                    case DeletedText dt: sb.Append(dt.Text); break;
                    // Review 023-F3: this is FLATTENED DISPLAY TEXT for a field result / SDT atom — a page
                    // break inside it stays a space (covered by that construct's own field-flattened-to-text
                    // / hard-tier warning), deliberately NOT an IsPageBreak model run; 026 owns that surface.
                    case Break or TabChar or NoBreakHyphen or CarriageReturn or PositionalTab: sb.Append(' '); break;
                    case Ruby ruby:
                        // Task 022 WS-2 construct audit: mirrors RenderRun's Ruby case — the base text is
                        // real editor-visible prose (not the phonetic guide), so this atom-display-text path
                        // must include it too, or a field/SDT wrapping a ruby run would silently drop it.
                        sb.Append(ExtractRunsDisplayText(RubyBaseRuns(ruby)));
                        break;
                    case SymbolChar sym:
                        // FR-06: represent (mapped glyph or placeholder) rather than drop — same resolver
                        // RenderRun uses. This atom-display-text path has no BuildContext to raise a
                        // warning through (documented limitation, same shape as this file's other
                        // "not exercised by the corpus" simplifications, e.g. FieldScanState remarks); an
                        // unmapped w:sym inside a field RESULT/SDT display text is not present in the
                        // corpus today.
                        sb.Append(ResolveSymbolGlyph(sym, out _));
                        break;
                    default: break;
                }
            }
        }
        return sb.ToString();
    }

    /// <summary>Convenience overload of <see cref="ExtractRunsDisplayText(IEnumerable{Run})"/> over every
    /// <see cref="Run"/> descendant of <paramref name="container"/> (a <see cref="SimpleField"/> or a
    /// special <see cref="SdtRun"/>) — used for BOTH the atom's offset-table length and its HTML display
    /// text, so the two can never diverge.</summary>
    internal static string ExtractAtomDisplayText(OpenXmlElement container) =>
        ExtractRunsDisplayText(container.Descendants<Run>());

    /// <summary>
    /// FR-02 (task 012) escalation-boundary decision: an SDT/content-control becomes a whole-construct
    /// opaque atom ONLY when its declared type is genuinely non-text (date, dropdown list, combo box,
    /// picture, doc-part gallery, equation, citation, bibliography, group) — content that cannot be
    /// faithfully shown as editable prose. A plain-text or rich-text control (or one with no declared type
    /// at all, the OOXML default) wraps ordinary editable paragraphs, so its shell stays TRANSPARENT (see
    /// <see cref="RenderBlockChildren"/>'s <c>SdtBlock</c> case and <see cref="CollectRunBoundaries"/>'s
    /// <c>SdtRun</c> case) — treating every SDT as opaque would silently regress real, currently-editable
    /// content, and no corpus construct forces that tradeoff (the escalation trigger this task's POML names
    /// is resolved by this structural rule rather than applied silently — see task notes for the writeup).
    /// </summary>
    internal static bool IsSpecialSdtControl(SdtProperties? props)
    {
        if (props is null) return false;
        return props.GetFirstChild<SdtContentDate>() is not null
            || props.GetFirstChild<SdtContentDropDownList>() is not null
            || props.GetFirstChild<SdtContentComboBox>() is not null
            || props.GetFirstChild<SdtContentPicture>() is not null
            || props.GetFirstChild<SdtContentDocPartObject>() is not null
            || props.GetFirstChild<SdtContentDocPartList>() is not null
            || props.GetFirstChild<SdtContentEquation>() is not null
            || props.GetFirstChild<SdtContentCitation>() is not null
            || props.GetFirstChild<SdtContentBibliography>() is not null
            || props.GetFirstChild<SdtContentGroup>() is not null;
    }

    /// <summary>The <c>w:ruby</c> base-text runs (<c>w:rubyBase</c>'s direct <see cref="Run"/> children) —
    /// the real editor-visible prose. The phonetic guide (<c>w:rt</c>) is deliberately excluded: it is a
    /// supplementary pronunciation annotation, not the document's own text (task 022 WS-2 construct
    /// audit).</summary>
    internal static IEnumerable<Run> RubyBaseRuns(Ruby ruby) =>
        ruby.GetFirstChild<RubyBase>()?.Elements<Run>() ?? Enumerable.Empty<Run>();

    /// <summary>
    /// FR-06 symbol-font → Unicode mapping (verified, HAND-CURATED, deliberately small). Today covers
    /// only the corpus's confirmed case — Symbol-font PUA code point <c>F0A7</c> maps to § (U+00A7, the
    /// legal section mark; corpus-manifest.md row 12). Deliberately does NOT include an algorithmic
    /// "subtract 0xF000 and treat the remainder as a Latin-1/legacy-Symbol-charset code point" fallback:
    /// that heuristic holds for SOME Symbol-font glyphs but is unverified across the font, and a WRONG
    /// mapped glyph in a legal document is worse than an honest, warned placeholder (F-1 / this task's
    /// escalation trigger — a wrong § is a wrong legal document, same failure class as a missing one).
    /// Extend this table only with entries verified against a real corpus/owner document. Mirrors the
    /// identically-scoped map in <c>ComposeReadFidelityHarnessSeamTests.KnownSymbolGlyphMap</c> — keep
    /// the two in sync if either changes.
    /// </summary>
    private static readonly Dictionary<(string Font, string Char), string> KnownSymbolGlyphMap = new()
    {
        [("Symbol", "F0A7")] = "§",
    };

    /// <summary>
    /// Resolves a <c>w:sym</c> run child to its display glyph. <paramref name="mapped"/> is <c>true</c>
    /// when <see cref="KnownSymbolGlyphMap"/> has a verified entry for the run's <c>(font, char)</c> pair
    /// (the returned glyph IS the correct Unicode target); <c>false</c> means no verified mapping exists
    /// and the caller must treat the returned replacement character as a placeholder, never as content —
    /// FR-06/FR-10 require the caller to also raise a warning in that branch.
    /// </summary>
    internal static string ResolveSymbolGlyph(SymbolChar sym, out bool mapped)
    {
        var font = sym.Font?.Value;
        var code = sym.Char?.Value;
        if (font is not null && code is not null && KnownSymbolGlyphMap.TryGetValue((font, code), out var glyph))
        {
            mapped = true;
            return glyph;
        }

        mapped = false;
        return "�"; // REPLACEMENT CHARACTER — visible placeholder, never a silent drop (FR-06).
    }

    // Task 020 (r6): takes MainDocumentPart (not BuildContext) so the canonical-model projection walk
    // (BuildContentModel) shares this ONE resolver with the HTML render walk — same protocol allowlist,
    // never two divergent implementations.
    internal static string? ResolveHyperlinkHref(Hyperlink h, MainDocumentPart mainPart)
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
            var rel = mainPart.HyperlinkRelationships.FirstOrDefault(r => r.Id == rid);
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

    // Note (task 040, WS-4/FR-16): the `ctx` parameter was unused in the body (heading-level classification
    // is a pure style-id read) — dropped so this helper is callable from the Pass-1 loop (below), which runs
    // BEFORE `BuildContext` exists (`ctx` is constructed after Pass-1, at the Pass-2 render handoff).
    internal static int? HeadingLevel(Paragraph p)
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
}
