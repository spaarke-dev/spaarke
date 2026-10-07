// #698 (spaarkeai-compose-r8) — STYLE-INHERITED NUMBERING seam slice.
//
// WHAT THIS FIXTURE PROVES, AND WHY IT HAD TO BE A REAL WORD DOCUMENT.
//
// `style-inherited-numbering.docx` is an owner-supplied Word document (2026-09-01). Its value is that
// three of its twelve body paragraphs carry NO `w:numPr` of their own — they are numbered because the
// `ListParagraph` STYLE they reference carries `<w:numPr><w:ilvl w:val="1"/><w:numId w:val="7"/></w:numPr>`
// in `styles.xml`. Word renders them `1.1.`, `1.2.` and `2.1.`.
//
// WHAT BREAKS IF THE STYLE LOOKUP IS DROPPED — measured, not assumed (a mutation that makes the
// style-linked lookup miss was run against this suite, 2026-09-01): those three paragraphs lose their
// labels entirely, AND the level-1 counter never advances, so `1.2.` and `2.1.` cease to exist in the
// document. A user's cross-reference to "Section 1.2" then misses, or worse resolves one level too
// shallow onto "Section 1" — plausible-looking and wrong. Note what does NOT break: the deeper labels
// are unaffected, because Word gives an un-incremented level its `start` value, so `1.1.1.` still
// computes as `1.1.1.`. The damage is to the style-numbered paragraphs and their siblings, not to the
// chain beneath them.
//
// That failure mode cannot be proven against a hand-built fixture. A synthetic document would encode
// OUR assumption about how Word resolves style-inherited numbering and then pass against itself — the
// two-engine drift this project exists to prevent (root CLAUDE.md §11; corpus-manifest preamble).
// Word is the oracle; the corpus is real documents for exactly this reason.
//
// WHAT THIS ADDS OVER `heading-style-numbering.docx` (which already covers FR-12 style-linked
// numbering, and whose tests also go red under the same mutation): that fixture links numbering through
// the built-in Heading1/Heading2 styles. This one links it through `ListParagraph` — a NON-heading,
// body-text style, which is how firm precedent documents and Word's own "Multilevel List → link to
// style" flow actually number clauses. It also exercises a SKIPPED level (the chain runs ilvl
// 0 → 1 → 2 → 3 → 4 with ilvl 1 supplied only by the style) and the sibling-advance case above, which
// the heading fixture does not reach.
//
// KEEP-path classification (ADR-038 §"vertical-slice-seam"): tests/integration/seam/**. Drives the REAL
// ComposeDocxProjectionBuilder over a REAL corpus fixture and the REAL CitationResolver — no mocks.

using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeStyleInheritedNumberingSeamTests
{
    private const string Fixture = "style-inherited-numbering.docx";

    private static ComposeDocxProjection ProjectCorpus(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(Path.Combine(corpusDir, fileName));
        return new ComposeDocxProjectionBuilder().Build(bytes);
    }

    /// <summary>
    /// The labels Word displays, in document order. Derived from the fixture's own OOXML, not from our
    /// renderer: `numId=7` → `abstractNum 4`, `multiLevelType=multilevel`, every level `decimal` with
    /// `start=1` and `lvlText` `%1.` / `%1.%2.` / `%1.%2.%3.` / … and no `lvlRestart` override (so each
    /// level restarts when a shallower one increments). Ordinal 0 is the `Title` paragraph, which carries
    /// no numbering at all; ordinals 12–13 are the trailing empty paragraphs before `sectPr`.
    /// </summary>
    private static readonly (int Ordinal, string? Expected)[] ExpectedLabels =
    {
        (0,  null),           // "Title of the Document" — {Title}, no numbering
        (1,  "1."),           // "Section 1"                 direct numPr, ilvl 0
        (2,  "1.1."),         // "Sub section1"              STYLE-INHERITED ilvl 1
        (3,  "1.1.1."),       // "Next sub section"          direct numPr, ilvl 2
        (4,  "1.1.1.1."),     // "Next next sub section"     direct numPr, ilvl 3
        (5,  "1.1.1.1.1."),   // "Next next next sub section" direct numPr, ilvl 4
        (6,  "1.2."),         // "Sub section 2"             STYLE-INHERITED ilvl 1 — increments to 2
        (7,  "2."),           // "Section 2"                 direct numPr, ilvl 0 — resets 1..4
        (8,  "2.1."),         // "Sub"                       STYLE-INHERITED ilvl 1
        (9,  "2.1.1."),       // "Next sub section"          direct numPr, ilvl 2
        (10, "2.1.1.1."),     // "Next next sub section"     direct numPr, ilvl 3
        (11, "2.1.1.1.1."),   // "Next next next sub section" direct numPr, ilvl 4
    };

    [Fact]
    public void Build_OnStyleInheritedNumberingCorpus_ComputesTheLabelsWordDisplays()
    {
        var projection = ProjectCorpus(Fixture);

        var actual = ExpectedLabels
            .Select(e => (e.Ordinal, Number: projection.ParaIdMap.Single(m => m.Index == e.Ordinal).ComputedNumber))
            .ToArray();

        actual.Should().Equal(ExpectedLabels,
            "the numbering engine must agree with Word on every label — a paragraph numbered by its " +
            "STYLE rather than by its own w:numPr is still a numbered paragraph, and dropping one " +
            "shifts every deeper label beneath it");
    }

    [Fact]
    public void Build_OnStyleInheritedNumberingCorpus_CountsTheStyleInheritedParagraphAsALevel()
    {
        // The sharpest single assertion, isolated so a failure names the cause rather than the symptom:
        // ordinal 2 ("Sub section1") has NO w:numPr on the paragraph. If the engine reads only
        // pPr/numPr it computes null here, and ordinal 3 then reads "1.1." instead of "1.1.1.".
        var projection = ProjectCorpus(Fixture);

        var styleInherited = projection.ParaIdMap.Single(m => m.Index == 2);

        styleInherited.ComputedNumber.Should().Be("1.1.",
            "ListParagraph carries numPr(ilvl=1, numId=7) in styles.xml — the paragraph is numbered by " +
            "its style, exactly as Word renders it");
        styleInherited.ListPath.Should().Equal(new[] { 1, 1 });
    }

    [Fact]
    public void Resolve_DeepChainOnStyleInheritedCorpus_ReturnsTheDeepestClauseAndNotItsAncestors()
    {
        // End-to-end: source bytes → projection → CitationResolver, on the SKIPPED-LEVEL chain (this
        // document supplies ilvl 1 only through the style, so the deep paragraphs sit under a level the
        // paragraph properties never mention).
        //
        // Scope note, established by the negative control rather than assumed: this assertion is NOT a
        // detector for style-inheritance. Word gives an un-incremented level its `start` value, so
        // ordinal 3 computes "1.1.1." whether or not ordinal 2 was counted — breaking the style lookup
        // leaves this test green. The three tests above are the style-inheritance detectors; this one
        // covers the resolver over a real deep chain, which is its own thing worth holding.
        var projection = ProjectCorpus(Fixture);
        var expectedParaId = projection.ParaIdMap.Single(m => m.Index == 3).ParaId;

        var result = CitationResolver.Resolve("1.1.1", projection.ParaIdMap);

        result.IsResolved.Should().BeTrue();
        result.ParaIds.Should().ContainSingle().Which.Should().Be(expectedParaId);
    }

    [Fact]
    public void Resolve_SecondTopLevelSubClause_DistinguishesItFromTheFirstSectionsSubClause()
    {
        // "2.1" and "1.1" are BOTH style-inherited paragraphs. If the engine silently dropped them the
        // two would collapse to their parents and this citation would resolve to "Section 2" itself —
        // a reference landing one level too shallow, which reads plausible and is wrong.
        var projection = ProjectCorpus(Fixture);
        var expectedParaId = projection.ParaIdMap.Single(m => m.Index == 8).ParaId;

        var result = CitationResolver.Resolve("2.1", projection.ParaIdMap);

        result.IsResolved.Should().BeTrue();
        result.ParaIds.Should().ContainSingle().Which.Should().Be(expectedParaId);
    }
}
