// Task 002 (spaarkeai-compose-fidelity-r4.5, NFR-01/NFR-02) — the read-fidelity harness this
// project's flagship (WS-3 numbering) and hardening (WS-2 read fixes) work is measured against.
// Extends the R4 byte-diff/seam harness (ComposeNoOpRoundTripByteDiffSeamTests,
// ComposeCorpusFixtureLocator) with the TWO golden assertions spec success criteria 2+3 require and
// currently have NO measurement for (design §5: "success is measured, not asserted"):
//
//   (a) TEXT-EXACTNESS (NFR-01) — for every corpus doc, per paragraph, the projected text equals the
//       source run text CHARACTER-FOR-CHARACTER, with ONLY lossless HTML-structural encoding
//       (&amp;/&lt;/&gt;) reversed. No trimming/collapsing/smart-quote normalization anywhere in this
//       comparison — those would mask exactly the drops WS-2 must fix.
//
//   (b) NUMBERING-EXACTNESS (NFR-02) — the projection does not yet compute a per-paragraph displayed
//       number at all (WS-3, tasks 031/032, has not landed — ParaIdMapEntry today carries only
//       Index/ParaId/IsMinted; see ParaIdPreParser.cs:185). A literal "computed label == golden label"
//       assertion therefore cannot be authored against a real field without inventing one — doing so
//       would be scope creep into WS-3's territory (root CLAUDE.md §11: this task's justification is
//       "extend the read-fidelity harness", NOT "add a numbering field"). Instead this file:
//         - wires the FULL target-shape Theory (golden label per numbered paragraph, across the five
//           R4.5 legal-numbering exemplars — corpus-manifest.md §1.5), gated behind
//           GetCurrentComputedNumber (a one-line stub returning null today) and marked
//           [Theory(Skip=...)] referencing WS-3 tasks 031/032 — flipping that one stub to read the
//           real field WS-3 adds is what turns every ❌ in this Theory into a ✅;
//         - AND live (non-skipped) characterization Facts that pin the CURRENT, structurally
//           observable defect per exemplar (the naive browser <ol> auto-count collapsing at every
//           interruption, dropped heading numbers, multi-level flattening) so a silent behavior
//           change here — for better or worse — is a visible diff, per this project's CLAUDE.md
//           "characterization baseline before fix" pattern.
//
// Headless invocation (task 002 step 1, escalation trigger check): CONFIRMED —
// ComposeDocxProjectionBuilder.Build(ReadOnlyMemory<byte>) is a pure, public, synchronous method
// (bytes-in/record-out, no I/O — see its class remarks "Zero package delta / Pure"); it is driven
// directly here with no WebApplicationFactory, exactly like the existing
// ComposeDocxProjectionBuilderTests.cs unit suite. No harness gap — the escalation trigger does not
// fire.
//
// KEEP-path classification (ADR-038 §"vertical-slice-seam" + tests/CLAUDE.md): tests/integration/
// seam/**. Drives the REAL ComposeDocxProjectionBuilder over REAL corpus fixtures (task 001) and
// asserts on the real projection output — no Mock<HttpMessageHandler>, no DI-registration test, no
// ctor-null test anywhere in this file (ADR-038 bans).
//
// Corpus: EVERY `.docx` under tests/fixtures/compose-corpus/, glob-enumerated via
// ComposeCorpusFixtureLocator (task 004's precedent) — not a hardcoded filename list, so an
// owner-supplied worst-offender dropped in later is automatically covered by the text-exactness
// Theory without a code change.

using System.Text;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using Xunit.Abstractions;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeReadFidelityHarnessSeamTests
{
    private readonly ITestOutputHelper _output;

    public ComposeReadFidelityHarnessSeamTests(ITestOutputHelper output) => _output = output;

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (a) TEXT-EXACTNESS — NFR-01, every corpus doc, per paragraph.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>xUnit [MemberData] source — every corpus `.docx`, glob-enumerated (task 004 precedent).</summary>
    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    /// <summary>
    /// NFR-01: for every corpus doc, per paragraph, the projected text must equal the source run text
    /// character-for-character (only &amp;/&lt;/&gt; HTML-structural encoding reversed).
    /// </summary>
    /// <remarks>
    /// WS-2 flip (task 020, spaarkeai-compose-fidelity-r4.5): task 002 originally special-cased any
    /// paragraph carrying a <c>w:sym</c>/<c>w:cr</c> descendant as a KNOWN, characterized drop (FR-05/
    /// FR-06 were not yet implemented — <c>ComposeDocxProjectionBuilder.RenderRun</c> had no case for
    /// <c>SymbolChar</c>/<c>CarriageReturn</c> and fell through to <c>default: break</c>). Task 020
    /// implemented both: <c>w:cr</c> now emits <c>&lt;br&gt;</c> (mirrors <c>w:br</c>); <c>w:sym</c> now
    /// maps to its verified Unicode glyph (Symbol F0A7 → §) or, for an unmapped code point, a visible
    /// U+FFFD placeholder + an intra-run glyph-loss warning (never a silent drop). Every corpus
    /// <c>w:sym</c> occurrence (<c>symbol-section-mark.docx</c>'s 2 body-run instances — corpus-
    /// manifest.md row 12) resolves through the VERIFIED mapping, and the corpus carries zero <c>w:cr</c>
    /// occurrences (WS-0 baseline note), so with the fix landed every paragraph in every corpus doc is
    /// text-exact — the "characterized drop" branch this Theory used to need is gone; a straight
    /// exact-match assertion now holds unconditionally. (The corpus's OTHER <c>symbol-section-mark.docx</c>
    /// negative case — 2 Wingdings-bullet paragraphs whose PUA glyph has no canonical Unicode target — is
    /// NOT a body-run <c>w:sym</c> at all: it is defined on the numbering LEVEL's <c>lvlText</c>, a list
    /// MARKER the browser's default <c>&lt;ul&gt;</c> styling renders, never emitted as paragraph run
    /// text; per the task 002 WS-0 baseline note it is untestable via this projected-HTML-text comparison
    /// and was already, and remains, text-exact here — the unmapped-placeholder+warning behavior for a
    /// genuinely unmapped <paramref name="run"/>-level <c>w:sym</c> is covered instead by
    /// <c>ComposeDocxProjectionBuilderTests.Build_ParagraphWithSymbolCharRun_UnmappedSymbolFont_...</c>,
    /// a synthetic fixture — there is no real corpus document exercising that branch to assert against
    /// here without fabricating one, which root CLAUDE.md §11 forbids absent a concrete gap.)
    /// </remarks>
    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void TextExactness_OnEveryCorpusDoc_MatchesSourceRunTextExactly(string corpusDocPath)
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var sourceParagraphs = body.Descendants<Paragraph>().ToList();

        var projection = new ComposeDocxProjectionBuilder().Build(bytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed,
            $"'{docName}' must project headlessly over the corpus fixture (task 002 step 1 escalation check)");
        projection.ParaIdMap.Should().HaveCount(sourceParagraphs.Count,
            $"the paraId map must be index-aligned, one entry per source paragraph, for '{docName}' (F-01 single-walk invariant)");

        var textExactCount = 0;

        for (var i = 0; i < sourceParagraphs.Count; i++)
        {
            var paraId = projection.ParaIdMap[i].ParaId;
            var golden = ExtractGoldenParagraphText(sourceParagraphs[i]);
            var projected = ExtractProjectedParagraphText(projection.Html, paraId);

            projected.Should().Be(golden,
                $"NFR-01: paraId '{paraId}' in '{docName}' must emit source run text character-for-character " +
                "(only &/</> HTML-structural encoding reversed) — no trimming/collapsing/normalization permitted");
            textExactCount++;
        }

        _output.WriteLine($"[text-exact ✅] {docName}: {textExactCount} of {sourceParagraphs.Count} paragraphs exact");
    }

    // ── golden (source) extraction — mirrors ComposeDocxProjectionBuilder.RenderInline/RenderRun's
    //    construct set, but computes the NFR-01-CORRECT text (including the w:sym/w:cr glyphs WS-2
    //    will represent), not today's lossy output. ──────────────────────────────────────────────

    /// <summary>
    /// Golden Unicode mapping for symbol-font code points this corpus exercises (FR-06 scope): the
    /// Symbol-font PUA code point 0xF0A7 → § (U+00A7) — corpus-manifest.md row 12's documented,
    /// KNOWN-mapping case. Deliberately does NOT include a Wingdings entry: the corpus's Wingdings
    /// bullet glyph is the manifest's deliberate NEGATIVE case (no canonical Unicode target — FR-06's
    /// disposition there is "visible placeholder + warning", not a specific golden character), and it
    /// is defined on the numbering LEVEL (word/numbering.xml lvlText), not as a body run — outside
    /// this per-paragraph run-text comparison's scope.
    /// </summary>
    private static readonly Dictionary<(string Font, string Char), string> KnownSymbolGlyphMap = new()
    {
        [("Symbol", "F0A7")] = "§",
    };

    private static string ExtractGoldenParagraphText(Paragraph paragraph)
    {
        var sb = new StringBuilder();
        AppendGoldenInline(paragraph, sb);
        return sb.ToString();
    }

    private static void AppendGoldenInline(DocumentFormat.OpenXml.OpenXmlElement container, StringBuilder sb)
    {
        foreach (var child in container.Elements())
        {
            switch (child)
            {
                case Run run:
                    AppendGoldenRun(run, sb);
                    break;
                case Hyperlink hyperlink:
                    AppendGoldenInline(hyperlink, sb);
                    break;
                case InsertedRun insertedRun:
                    AppendGoldenInline(insertedRun, sb); // F-02: settled prose, wrapper stripped — mirrors RenderInline
                    break;
                case DeletedRun deletedRun:
                    AppendGoldenInline(deletedRun, sb); // F-02: deleted text present — mirrors RenderInline
                    break;
                case SdtRun sdtRun:
                    var content = sdtRun.GetFirstChild<SdtContentRun>();
                    if (content is not null) AppendGoldenInline(content, sb);
                    break;
                default:
                    break;
            }
        }
    }

    private static void AppendGoldenRun(Run run, StringBuilder sb)
    {
        foreach (var child in run.Elements())
        {
            switch (child)
            {
                case Text text:
                    sb.Append(text.Text);
                    break;
                case DeletedText deletedText:
                    sb.Append(deletedText.Text);
                    break;
                case TabChar:
                    sb.Append(' '); // mirrors AppendRun's non-collapsing <span class="compose-tab"> </span>
                    break;
                case Break:
                    sb.Append('\n'); // mirrors <br> — decoded back to "\n" on the projected side, see below
                    break;
                case CarriageReturn:
                    // WS-2 FR-05 target: a w:cr is a break, same as w:br — NOT a silent drop. Today
                    // ComposeDocxProjectionBuilder emits nothing for it (no case in RenderRun's switch).
                    sb.Append('\n');
                    break;
                case NoBreakHyphen:
                    sb.Append('‑'); // mirrors ctx.Append("‑") exactly (verified against source bytes)
                    break;
                case SymbolChar symbolChar:
                    sb.Append(ResolveGoldenSymbolGlyph(symbolChar));
                    break;
                default:
                    break;
            }
        }
    }

    private static string ResolveGoldenSymbolGlyph(SymbolChar symbolChar)
    {
        var font = symbolChar.Font?.Value;
        var code = symbolChar.Char?.Value;
        if (font is not null && code is not null && KnownSymbolGlyphMap.TryGetValue((font, code), out var mapped))
        {
            return mapped;
        }

        // FR-06 unmapped case (e.g. Wingdings PUA with no canonical target): no golden value — the
        // disposition is a visible placeholder + warning (not asserted here; not exercised by any
        // corpus BODY run today — see the KnownSymbolGlyphMap remarks).
        return string.Empty;
    }

    // ── projected (HTML) extraction — decodes ONLY the lossless HTML-structural encoding NFR-01
    //    permits (&amp;/&lt;/&gt;) plus <br> → "\n" (Break's own, already-correct representation);
    //    every other tag is markup, stripped without altering the enclosed text. ───────────────────

    private static readonly Regex ParagraphBlockPattern = new(
        "<(?<tag>p|h[1-6])\\sdata-paraid=\"(?<id>[0-9A-Fa-f]+)\"[^>]*>(?<inner>.*?)</\\k<tag>>",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex AnyTagPattern = new("<[^>]+>", RegexOptions.Compiled);

    private static string ExtractProjectedParagraphText(string html, string paraId)
    {
        foreach (Match match in ParagraphBlockPattern.Matches(html))
        {
            if (string.Equals(match.Groups["id"].Value, paraId, StringComparison.OrdinalIgnoreCase))
            {
                return DecodeInnerHtmlToText(match.Groups["inner"].Value);
            }
        }

        throw new InvalidOperationException(
            $"No projected HTML block found for paraId '{paraId}' — the paragraph may be an opaque block " +
            "atom (never emits data-paraid) or was emitted via a tag this pattern does not recognize.");
    }

    private static string DecodeInnerHtmlToText(string inner)
    {
        var withBreaks = inner.Replace("<br>", "\n", StringComparison.Ordinal);
        var stripped = AnyTagPattern.Replace(withBreaks, string.Empty);
        return stripped
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (b) NUMBERING-EXACTNESS — NFR-02, the R4.5 legal-numbering exemplars (corpus-manifest.md §1.5).
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// WS-3 flip point (tasks 031/032) — NOW LIVE. Task 031 landed the deterministic numbering
    /// computation engine and attaches the computed label to <see cref="ParaIdMapEntry.ComputedNumber"/>
    /// (ParaIdPreParser.cs) in the projection's single document-order walk. This reads that field for the
    /// paragraph's <c>paraId</c> — the one-line flip that turned
    /// <see cref="NumberingExactness_OnLegalNumberingExemplars_ComputedLabelMatchesGoldenWordLabel"/> from
    /// a <c>[Skip]</c>ped target into the live NFR-02 acceptance proof.
    /// </summary>
    private static string? GetCurrentComputedNumber(ComposeDocxProjection projection, string paraId) =>
        projection.ParaIdMap
            .FirstOrDefault(e => string.Equals(e.ParaId, paraId, StringComparison.OrdinalIgnoreCase))
            ?.ComputedNumber;

    public static IEnumerable<object[]> LegalNumberingExemplars()
    {
        // (docFileName, 0-based ordinal index into body.Descendants<Paragraph>(), goldenLabel) —
        // golden values transcribed verbatim from corpus-manifest.md §1.5 (task 001's hand-verified
        // Word-numbering simulation against the OOXML this task authored). Ordinal index (not a text
        // substring) is the lookup key deliberately: heading-style-numbering.docx's Heading1
        // "Confidentiality" and its child Heading2 "Confidentiality" have IDENTICAL paragraph text
        // (only their computed numbers — "4" vs "4.2" — differ), so a text-substring lookup would be
        // ambiguous; a positional index against a fixed, hand-verified synthetic fixture is not.
        yield return new object[] { "nda-interrupted-clauses.docx", 2, "1." };   // "Confidentiality Obligations."
        yield return new object[] { "nda-interrupted-clauses.docx", 3, "2." };   // "Term. This Agreement shall remain..."
        yield return new object[] { "nda-interrupted-clauses.docx", 4, "3." };   // "Definitions. ..."
        yield return new object[] { "nda-interrupted-clauses.docx", 12, "4." };  // "Remedies. ..." (post-interruption, SAME numId)
        yield return new object[] { "nda-interrupted-clauses.docx", 13, "5." };  // "Indemnification. ..."
        yield return new object[] { "nda-interrupted-clauses.docx", 14, "6." };  // "Miscellaneous. ..."
        yield return new object[] { "heading-style-numbering.docx", 0, "1" };    // Heading1 "Recitals"
        yield return new object[] { "heading-style-numbering.docx", 2, "2" };    // Heading1 "Definitions"
        yield return new object[] { "heading-style-numbering.docx", 4, "3" };    // Heading1 "Term"
        yield return new object[] { "heading-style-numbering.docx", 6, "4" };    // Heading1 "Confidentiality"
        yield return new object[] { "heading-style-numbering.docx", 7, "4.1" };  // Heading2 "Purpose"
        yield return new object[] { "heading-style-numbering.docx", 9, "4.2" };  // Heading2 "Confidentiality" — the literal FR-12 example
        yield return new object[] { "multilevel-1-1-1.docx", 1, "1." };          // ilvl0 "Introduction"
        yield return new object[] { "multilevel-1-1-1.docx", 2, "1.1." };        // ilvl1 "Background"
        yield return new object[] { "multilevel-1-1-1.docx", 3, "1.1.1." };      // ilvl2 "History"
        yield return new object[] { "multilevel-1-1-1.docx", 4, "1.1.2." };      // ilvl2 "Current State"
        yield return new object[] { "multilevel-1-1-1.docx", 5, "1.2." };        // ilvl1 "Scope"
        yield return new object[] { "multilevel-1-1-1.docx", 6, "2." };          // ilvl0 "Definitions" (level-1/2 counters reset)
        yield return new object[] { "multilevel-1-1-1.docx", 7, "2.1." };        // ilvl1 "Key Terms"
        yield return new object[] { "line-numbered-pleading.docx", 6, "1." };    // PARTIES clause 1
        yield return new object[] { "line-numbered-pleading.docx", 7, "2." };    // PARTIES clause 2
        yield return new object[] { "line-numbered-pleading.docx", 9, "3." };    // JURISDICTION AND VENUE clause 1
        yield return new object[] { "line-numbered-pleading.docx", 10, "4." };   // JURISDICTION AND VENUE clause 2
        yield return new object[] { "line-numbered-pleading.docx", 20, "12." };  // FIRST CAUSE OF ACTION, last clause
    }

    /// <summary>
    /// NFR-02 TARGET (currently [Skip]ped — WS-3 has not landed): the computed displayed label must
    /// equal the golden Word-displayed label from corpus-manifest.md §1.5, per numbered paragraph,
    /// across the interrupted/heading-style/multi-level/pleading exemplars. See
    /// <see cref="GetCurrentComputedNumber"/> remarks for the one-line flip that unblocks this Theory.
    /// </summary>
    [Theory]
    [MemberData(nameof(LegalNumberingExemplars))]
    public void NumberingExactness_OnLegalNumberingExemplars_ComputedLabelMatchesGoldenWordLabel(
        string docFileName, int paragraphOrdinalIndex, string goldenLabel)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        var docPath = Path.Combine(corpusDir, docFileName);
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(docPath);

        using var doc = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);
        var paragraph = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ElementAt(paragraphOrdinalIndex);
        var paraId = paragraph.ParagraphId!.Value!.ToUpperInvariant();

        var projection = new ComposeDocxProjectionBuilder().Build(bytes);
        var computed = GetCurrentComputedNumber(projection, paraId);

        computed.Should().Be(goldenLabel,
            $"NFR-02: paraId '{paraId}' in '{docFileName}' at ordinal index {paragraphOrdinalIndex} must render the " +
            $"Word-identical computed number '{goldenLabel}'");
    }

    // ── live characterization Facts — pin the CURRENT, structurally observable numbering defect per
    //    exemplar (no invented field required: the browser-<ol>-auto-count shape IS the bug). These
    //    are NOT skipped; WS-3 is expected to UPDATE (not silently break) them when numbering lands. ──

    private static int CountOccurrences(string haystack, string needle) =>
        haystack.Split(needle, StringSplitOptions.None).Length - 1;

    private static string LoadCorpusDoc(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        return Path.Combine(corpusDir, fileName);
    }

    [Fact]
    public void NumberingCharacterization_NdaInterruptedClauses_TodayRestartsTheListAtEachInterruption()
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(LoadCorpusDoc("nda-interrupted-clauses.docx"));
        var projection = new ComposeDocxProjectionBuilder().Build(bytes);

        // Golden (Word): ONE continuous numbered run, clauses 1..6 across the heading/body/table
        // interruption (corpus-manifest.md row 9). TODAY (WS-3 target, not yet built): the reader
        // closes the <ol> at every interruption and opens a fresh one — a naive browser auto-count
        // would show 1,2,3 then 1,2,3 again, not 4,5,6. Pinning that CURRENT shape (2 separate <ol>
        // blocks, 3 <li> each) so WS-3 landing a continuous 6-item list is a visible, intentional diff.
        CountOccurrences(projection.Html, "<ol>").Should().Be(2,
            "WS-3 target (FR-11): today the reader restarts a fresh <ol> after each interruption instead of " +
            "replaying one continuous per-numId counter — see corpus-manifest.md row 9");
        CountOccurrences(projection.Html, "<li>").Should().Be(6, "3 clauses before + 3 after the interruption");
    }

    [Fact]
    public void NumberingCharacterization_HeadingStyleNumbering_TodayRendersNoNumericPrefixOnAnyHeading()
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(LoadCorpusDoc("heading-style-numbering.docx"));
        var projection = new ComposeDocxProjectionBuilder().Build(bytes);

        // Golden (Word): "4.2 Confidentiality" (corpus-manifest.md row 10, the literal FR-12 acceptance
        // example) — the w:numPr lives on the Heading1/Heading2 STYLE, not on any paragraph directly.
        // TODAY: ComposeDocxProjectionBuilder.ListInfo only reads a DIRECT paragraph w:numPr (by design
        // — style-linked heading numbering is intentionally not treated as a list), and no numbering
        // is computed/prefixed onto headings at all — the heading renders as bare text. Zero headings
        // in this doc carry ANY digit prefix today; WS-3 (FR-12) is what makes that non-zero.
        projection.Html.Should().NotContain(">4.2 Confidentiality<",
            "WS-3 target (FR-12): style-linked heading numbering is not yet resolved — see corpus-manifest.md row 10");
        projection.Html.Should().Contain(">Confidentiality<",
            "today the heading renders its bare style-derived text with no computed number prefix");
    }

    [Fact]
    public void NumberingCharacterization_Multilevel111_TodayCollapsesAllLevelsIntoOneFlatList()
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(LoadCorpusDoc("multilevel-1-1-1.docx"));
        var projection = new ComposeDocxProjectionBuilder().Build(bytes);

        // Golden (Word): 1. / 1.1. / 1.1.1. / 1.1.2. / 1.2. / 2. / 2.1. (corpus-manifest.md row 11).
        // TODAY: EnsureList tracks ordered-vs-bullet only, never ilvl — all 7 items land in ONE flat
        // <ol> (a browser would auto-count 1..7, losing the hierarchy), and RenderParagraph raises a
        // "multi-level-numbering" warning once per level>0 paragraph instead of computing the nested
        // label. 5 of the 7 items are at ilvl>0 (Background, History, Current State, Scope, Key Terms).
        CountOccurrences(projection.Html, "<ol>").Should().Be(1,
            "WS-3 target (FR-11): today all ilvl 0/1/2 items land in a single flat <ol> — see corpus-manifest.md row 11");
        CountOccurrences(projection.Html, "<li>").Should().Be(7);
        projection.Warnings.Should().ContainSingle(w => w.Code == "multi-level-numbering" && w.Count == 5,
            "5 of the 7 numbered paragraphs are at ilvl>0 and are currently reduced to a warning, not a computed nested label");
    }

    [Fact]
    public void NumberingCharacterization_LineNumberedPleading_TodayRestartsTheListPerSection()
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(LoadCorpusDoc("line-numbered-pleading.docx"));
        var projection = new ComposeDocxProjectionBuilder().Build(bytes);

        // Golden (Word): ONE continuous numbered run, 1..12 across the 4 headed sections
        // (corpus-manifest.md row 13 — same interrupted-numbering construct as row 9, in pleading
        // form). TODAY: the same per-interruption <ol> restart as nda-interrupted-clauses.docx — 4
        // separate <ol> blocks (Parties=2, Jurisdiction and Venue=2, Factual Allegations=4, First
        // Cause of Action=4), each a naive browser auto-count would show restarting at 1.
        CountOccurrences(projection.Html, "<ol>").Should().Be(4,
            "WS-3 target (FR-11): today the reader opens a fresh <ol> per section heading instead of one " +
            "continuous 1..12 run — see corpus-manifest.md row 13");
        CountOccurrences(projection.Html, "<li>").Should().Be(12, "2 + 2 + 4 + 4 numbered paragraphs across the 4 sections");
    }
}
