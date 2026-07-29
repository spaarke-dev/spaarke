// Task 042 (spaarkeai-compose-fidelity-r4.5, FR-18) — WS-4 CITATION RESOLVER seam slice.
//
// Proves the single most valuable analysis output of R4.5: a human legal citation resolves to the exact
// paragraph w14:paraId(s), for all three reference shapes the project's locked WS-4 decision requires —
// single label ("Section 4.2"), sub-item depth ("4.2(b)(iii)"), and contiguous range ("Sections 4–7") —
// plus the explicit not-found negative (an unresolvable citation returns zero matches, NEVER a fabricated
// paraId or a throw), and the bidirectional reverse (paraId → canonical citation number).
//
// TWO evidence bases, both driving the SAME CitationResolver:
//   (1) REAL CORPUS end-to-end — drives ComposeDocxProjectionBuilder.Build() over the real §1.5 numbering
//       exemplars (heading-style-numbering / multilevel-1-1-1 / nda-interrupted-clauses) and resolves
//       citations against the REAL projection ParaIdMap. This is the seam: source bytes → projection →
//       resolver → paraId. Single label, DECIMAL sub-item depth ("1.1.1" → [1,1,1]), and range are all
//       exercised on real corpus paragraphs.
//   (2) LETTER/ROMAN sub-item ("4.2(b)(iii)") — the corpus manifest (§1.5 + §2 placeholders) documents that
//       NO corpus fixture carries lower-letter / lower-roman sub-item numbering (deepest real chain is the
//       decimal [1,1,1]); fabricating one is forbidden by root CLAUDE.md §11. The letter/roman PARSE +
//       match is therefore proven against a small in-memory ParaIdMap whose ListPath chains are the exact
//       structured form such a paragraph would carry (ListPath is always the numeric counter chain, whatever
//       the label FORMAT). Constructing ParaIdMapEntry input to a pure function is the resolver's real data
//       contract — not a mocked collaborator (ADR-038: no Mock<HttpMessageHandler>/DI/ctor-null shapes here).
//
// KEEP-path classification (ADR-038 §"vertical-slice-seam"): tests/integration/seam/**. Drives the REAL
// ComposeDocxProjectionBuilder over REAL corpus fixtures + the REAL CitationResolver — no banned test shapes.

using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeCitationResolverSeamTests
{
    private static string LoadCorpusDoc(string fileName)
    {
        var corpusDir = Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!;
        return Path.Combine(corpusDir, fileName);
    }

    private static ComposeDocxProjection ProjectCorpus(string fileName)
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(LoadCorpusDoc(fileName));
        return new ComposeDocxProjectionBuilder().Build(bytes);
    }

    /// <summary>The projection ParaIdMap entry's paraId at a 0-based document-order ordinal.</summary>
    private static string ParaIdAt(ComposeDocxProjection projection, int ordinal) =>
        projection.ParaIdMap.Single(e => e.Index == ordinal).ParaId;

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (1) SINGLE LABEL — "Section 4.2" → the exact clause paraId, on the real corpus.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Section 4.2")]
    [InlineData("section 4.2")]
    [InlineData("4.2")]
    [InlineData("§ 4.2")]
    [InlineData("§4.2")]
    [InlineData("Article 4.2")]
    [InlineData("  Section   4.2  ")]
    public void Resolve_SingleLabel_OnHeadingStyleCorpus_ReturnsExactClauseParaId(string citation)
    {
        // heading-style-numbering.docx ordinal 9 is the Heading2 "Confidentiality" → label "4.2",
        // ListPath [4,2] (the literal FR-12 example — corpus-manifest §1.5 row 10).
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var expectedParaId = ParaIdAt(projection, 9);

        var result = CitationResolver.Resolve(citation, projection.ParaIdMap);

        result.Shape.Should().Be(CitationShape.Single);
        result.IsResolved.Should().BeTrue();
        result.ParaIds.Should().ContainSingle().Which.Should().Be(expectedParaId,
            $"'{citation}' must resolve to the paragraph whose ordinal chain is [4,2]");
    }

    [Fact]
    public void Resolve_SingleTopLevelLabel_OnHeadingStyleCorpus_ReturnsTheHeadingParaId()
    {
        // "Section 4" → ListPath [4] → Heading1 "Confidentiality" at ordinal 6.
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var expectedParaId = ParaIdAt(projection, 6);

        var result = CitationResolver.Resolve("Section 4", projection.ParaIdMap);

        result.ParaIds.Should().ContainSingle().Which.Should().Be(expectedParaId);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (2) SUB-ITEM DEPTH — decimal on the real corpus + letter/roman on the structured in-memory map.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_DecimalSubItemDepth_OnMultilevelCorpus_ReturnsDeepestClauseParaId()
    {
        // multilevel-1-1-1.docx ordinal 3 is "History" → label "1.1.1.", ListPath [1,1,1] (§1.5 row 11).
        // A sub-item citation at depth 3 must resolve to the deep clause, NOT the top-level "1".
        var projection = ProjectCorpus("multilevel-1-1-1.docx");
        var expectedParaId = ParaIdAt(projection, 3);

        var result = CitationResolver.Resolve("1.1.1", projection.ParaIdMap);

        result.IsResolved.Should().BeTrue();
        result.ParaIds.Should().ContainSingle().Which.Should().Be(expectedParaId,
            "'1.1.1' must resolve to ListPath [1,1,1], the deepest sub-item — not the top-level [1]");
    }

    [Fact]
    public void Resolve_LetterRomanSubItem_OnStructuredMap_ParsesTokensAndMatchesExactChain()
    {
        // "4.2(b)(iii)" → [4, 2, 2, 3]  (b = 2nd lower-letter, iii = 3rd lower-roman). No corpus fixture
        // carries letter/roman sub-items (corpus-manifest §1.5/§2) — proven against the structured chain
        // such a paragraph would carry. The map also contains the shallower [4,2] and a decoy [4,2,2,4]
        // ("(iv)") to prove the parse lands on EXACTLY [4,2,2,3], not a prefix or a neighbor.
        var map = new[]
        {
            new ParaIdMapEntry(0, "AAAA0001", IsMinted: false, ComputedNumber: "4.2",         NumberingLevel: 1, ListPath: new[] { 4, 2 }),
            new ParaIdMapEntry(1, "AAAA0002", IsMinted: false, ComputedNumber: "4.2(a)",      NumberingLevel: 2, ListPath: new[] { 4, 2, 1 }),
            new ParaIdMapEntry(2, "AAAA0003", IsMinted: false, ComputedNumber: "4.2(b)",      NumberingLevel: 2, ListPath: new[] { 4, 2, 2 }),
            new ParaIdMapEntry(3, "AAAA0004", IsMinted: false, ComputedNumber: "4.2(b)(iii)", NumberingLevel: 3, ListPath: new[] { 4, 2, 2, 3 }),
            new ParaIdMapEntry(4, "AAAA0005", IsMinted: false, ComputedNumber: "4.2(b)(iv)",  NumberingLevel: 3, ListPath: new[] { 4, 2, 2, 4 }),
        };

        var result = CitationResolver.Resolve("4.2(b)(iii)", map);

        result.Shape.Should().Be(CitationShape.SubItem);
        result.ParaIds.Should().ContainSingle().Which.Should().Be("AAAA0004");
    }

    [Fact]
    public void Resolve_SectionPrefixedLetterRomanSubItem_OnStructuredMap_ToleratesPrefix()
    {
        var map = new[]
        {
            new ParaIdMapEntry(0, "BBBB0001", IsMinted: false, ComputedNumber: "4.2(b)(iii)", NumberingLevel: 3, ListPath: new[] { 4, 2, 2, 3 }),
        };

        CitationResolver.Resolve("Section 4.2(b)(iii)", map).ParaIds.Should().ContainSingle().Which.Should().Be("BBBB0001");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (3) CONTIGUOUS RANGE — "Sections 4–7" → the full contiguous span, in document order.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("Sections 4–7")]  // en-dash (the literal owner-clarification example)
    [InlineData("Sections 4-7")]  // hyphen-minus
    [InlineData("4-7")]
    [InlineData("§§ 4-7")]
    [InlineData("Sections 7–4")]  // descending normalizes to the same span
    public void Resolve_ContiguousRange_OnInterruptedClausesCorpus_ReturnsFullSpanInDocumentOrder(string citation)
    {
        // nda-interrupted-clauses.docx: 6 continuous clauses (ListPath [1]..[6]) at ordinals 2,3,4 then
        // 12,13,14 (§1.5 row 9). Range 4–7 selects top-level ordinals {4,5,6} (7 does not exist) =
        // ordinals 12,13,14, in document order.
        var projection = ProjectCorpus("nda-interrupted-clauses.docx");
        var expected = new[] { ParaIdAt(projection, 12), ParaIdAt(projection, 13), ParaIdAt(projection, 14) };

        var result = CitationResolver.Resolve(citation, projection.ParaIdMap);

        result.Shape.Should().Be(CitationShape.Range);
        result.ParaIds.Should().Equal(expected,
            "the range must return every clause whose top-level ordinal is in [4..7], in document order");
    }

    [Fact]
    public void Resolve_RangeSpanningWholeDocument_IncludesSubItemsUnderEachTopLevelSection()
    {
        // multilevel-1-1-1.docx top-level sections are [1] and [2]; range "1-2" must include their sub-items
        // (1.1, 1.1.1, ...) too — "all paragraphs whose top-level ordinal is in [1..2]" (parent directive).
        var projection = ProjectCorpus("multilevel-1-1-1.docx");

        var result = CitationResolver.Resolve("Sections 1-2", projection.ParaIdMap);

        // Ordinals 1..7 are the 7 numbered items (1., 1.1., 1.1.1., 1.1.2., 1.2., 2., 2.1.) per §1.5 row 11;
        // ordinal 0 is the un-numbered title (excluded — not citable).
        var expected = Enumerable.Range(1, 7).Select(o => ParaIdAt(projection, o)).ToArray();
        result.ParaIds.Should().Equal(expected);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (4) NEGATIVE — unresolvable / malformed citations return an EXPLICIT not-found, never a throw.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Resolve_NonexistentSection_OnCorpus_ReturnsExplicitNotFoundNotAFabricatedParaId()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var result = CitationResolver.Resolve("Section 99", projection.ParaIdMap);

        result.IsResolved.Should().BeFalse();
        result.ParaIds.Should().BeEmpty("a citation that resolves to 0 paragraphs is a valid answer, not a guess");
        result.Shape.Should().Be(CitationShape.Single);
    }

    [Fact]
    public void Resolve_RangeWithNoMembers_OnCorpus_ReturnsExplicitNotFound()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx"); // top-level sections only reach 4
        CitationResolver.Resolve("Sections 20-30", projection.ParaIdMap).IsResolved.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a citation")]
    [InlineData("Section")]
    [InlineData("Section abc")]
    [InlineData("4.2(")]        // unbalanced parenthesis
    [InlineData("4.2(!!)")]     // non letter/roman/digit token
    [InlineData("4.2 xyz")]     // trailing junk
    public void Resolve_MalformedInput_OnCorpus_ReturnsHandledNotFoundNeverThrows(string? citation)
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");

        var act = () => CitationResolver.Resolve(citation, projection.ParaIdMap);

        var result = act.Should().NotThrow().Subject;
        result.IsResolved.Should().BeFalse();
        result.ParaIds.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_BulletParagraph_IsNotReachableByNumericCitation()
    {
        // A bullet carries a non-numeric ComputedNumber (glyph) but a numeric ListPath (task 040 note).
        // It must NOT be reachable by a section/range citation — the citability gate excludes it.
        var map = new[]
        {
            new ParaIdMapEntry(0, "CCCC0001", IsMinted: false, ComputedNumber: "•", NumberingLevel: 0, ListPath: new[] { 4 }),
        };

        CitationResolver.Resolve("Section 4", map).IsResolved.Should().BeFalse();
        CitationResolver.Resolve("Sections 1-9", map).IsResolved.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (5) BIDIRECTIONAL — paraId → its canonical human citation number.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void ResolveCitation_FromParaId_OnHeadingStyleCorpus_ReturnsCanonicalNumber()
    {
        var projection = ProjectCorpus("heading-style-numbering.docx");
        var paraId42 = ParaIdAt(projection, 9); // the "4.2" Heading2

        CitationResolver.ResolveCitation(paraId42, projection.ParaIdMap).Should().Be("4.2");
    }

    [Fact]
    public void ResolveCitation_RoundTrips_WithForwardResolution()
    {
        var projection = ProjectCorpus("multilevel-1-1-1.docx");
        var paraId = ParaIdAt(projection, 3); // "1.1.1."

        var citation = CitationResolver.ResolveCitation(paraId, projection.ParaIdMap);
        citation.Should().Be("1.1.1");

        // Forward-resolving the reverse label lands back on the same paraId.
        CitationResolver.Resolve(citation!, projection.ParaIdMap).ParaIds.Should().ContainSingle().Which.Should().Be(paraId);
    }

    [Fact]
    public void ResolveCitation_ForUnnumberedOrUnknownParaId_ReturnsNull()
    {
        var projection = ProjectCorpus("multilevel-1-1-1.docx");
        var titleParaId = ParaIdAt(projection, 0); // the un-numbered title paragraph

        CitationResolver.ResolveCitation(titleParaId, projection.ParaIdMap).Should().BeNull("an un-numbered paragraph has no citation");
        CitationResolver.ResolveCitation("DEADBEEF", projection.ParaIdMap).Should().BeNull("an unknown paraId has no citation");
    }
}
