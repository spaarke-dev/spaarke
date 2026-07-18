// FR-12 (task 012, E2) — unit tests for ComposeParaIdSpliceMap: the paraId SPLICE KEY the E1 delta
// save (FR-02 splice, task 020) consumes to map each edited editor paragraph back to EXACTLY the
// original OOXML paragraph carrying the matching w14:paraId. Each test names a concrete production
// behavior that breaks if deleted (FR-12 acceptance):
//   - an edit to paragraph P maps to exactly the original paragraph with P's paraId (not a wrong one)
//   - a table-cell paragraph is indexed + resolved by its paraId (S1b nested/table-cell case)
//   - an unmatched paraId is a HANDLED miss (surfaced), never a silent no-op or a wrong-paragraph write
//   - a duplicate paraId (document corruption) throws rather than silently picking one occurrence
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): pure domain logic over real in-memory .docx
// fixtures (Open XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4), no
// getter/mirror tests (B6/B16). Assertions are behavioral (which original paragraph an edited id maps
// to), not implementation-shape.

using System;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeParaIdSpliceMapTests
{
    // ── in-memory .docx fixture builders ────────────────────────────────────────────────────────
    private static Paragraph Para(string? paraId, string text)
    {
        var p = new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        if (paraId is not null)
        {
            p.ParagraphId = new HexBinaryValue(paraId);
        }
        return p;
    }

    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var child in bodyChildren)
            {
                body.Append(child);
            }
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    /// <summary>Opens the bytes read-only and returns the body for indexing. Caller keeps the doc in a
    /// using so the returned live paragraphs stay valid for assertions.</summary>
    private static WordprocessingDocument Open(byte[] docx)
    {
        var buffer = new MemoryStream();
        buffer.Write(docx, 0, docx.Length);
        buffer.Position = 0;
        return WordprocessingDocument.Open(buffer, isEditable: false);
    }

    // 5 body paragraphs: 0/1/4 top-level with ids, 2 a table-cell paragraph with an id, 3 a nested
    // table-cell paragraph with an id.
    private static byte[] MixedDocxWithParaIds() => BuildDocx(
        Para("0000000A", "The parties enter into this Agreement."),
        Para("0000000B", "Indemnification is capped at fees paid in the prior twelve months."),
        new Table(
            new TableRow(
                new TableCell(Para("0000000C", "Cell paragraph — payment terms.")),
                new TableCell(
                    new Table(new TableRow(new TableCell(Para("0000000D", "Nested cell — late-fee schedule.")))),
                    Para("0000000E", "Cell trailing — net thirty.")))),
        Para("0000000F", "Termination requires thirty days notice."));

    // -- BuildParagraphIndex --------------------------------------------------------------------

    [Fact]
    public void BuildParagraphIndex_MapsEachParaIdToItsParagraph_InclTableAndNestedCells()
    {
        using var doc = Open(MixedDocxWithParaIds());
        var body = doc.MainDocumentPart!.Document!.Body!;

        var index = ComposeParaIdSpliceMap.BuildParagraphIndex(body);

        // Every id-bearing paragraph is indexed — including the table-cell (0000000C), nested-table-cell
        // (0000000D), and cell-trailing (0000000E) paragraphs (Descendants<Paragraph>() recurses).
        index.Should().HaveCount(6);
        index["0000000A"].InnerText.Should().Be("The parties enter into this Agreement.");
        index["0000000C"].InnerText.Should().Be("Cell paragraph — payment terms.");
        index["0000000D"].InnerText.Should().Be("Nested cell — late-fee schedule.");
        index["0000000F"].InnerText.Should().Be("Termination requires thirty days notice.");
    }

    [Fact]
    public void BuildParagraphIndex_SkipsParagraphsWithoutParaId()
    {
        using var doc = Open(BuildDocx(
            Para("0000000A", "has an id"),
            Para(null, "no id — not a splice target"),
            Para("0000000B", "also has an id")));
        var body = doc.MainDocumentPart!.Document!.Body!;

        var index = ComposeParaIdSpliceMap.BuildParagraphIndex(body);

        index.Should().HaveCount(2, "only id-bearing paragraphs are splice targets");
        index.Keys.Should().BeEquivalentTo(new[] { "0000000A", "0000000B" });
    }

    [Fact]
    public void BuildParagraphIndex_DuplicateParaId_Throws()
    {
        // Two paragraphs share an id — a document-integrity violation. Silently picking one would splice
        // an edit into the wrong paragraph, so the index build MUST fail loudly.
        using var doc = Open(BuildDocx(
            Para("0000000A", "first"),
            Para("0000000A", "second — same id (corrupt)")));
        var body = doc.MainDocumentPart!.Document!.Body!;

        var act = () => ComposeParaIdSpliceMap.BuildParagraphIndex(body);

        act.Should().Throw<InvalidOperationException>().WithMessage("*Duplicate w14:paraId*");
    }

    // -- Resolve --------------------------------------------------------------------------------

    [Fact]
    public void Resolve_EditToParagraphP_MapsToExactlyOriginalParagraphWithMatchingParaId()
    {
        // FR-12 acceptance: an edit to paragraph P (id 0000000B) resolves to EXACTLY the original
        // paragraph carrying that id — not paragraph A, not F.
        using var doc = Open(MixedDocxWithParaIds());
        var index = ComposeParaIdSpliceMap.BuildParagraphIndex(doc.MainDocumentPart!.Document!.Body!);

        var resolution = ComposeParaIdSpliceMap.Resolve(index, new[] { "0000000B" });

        resolution.IsFullyMatched.Should().BeTrue();
        resolution.Unmatched.Should().BeEmpty();
        resolution.Matched.Should().ContainSingle();
        resolution.Matched["0000000B"].InnerText.Should()
            .Be("Indemnification is capped at fees paid in the prior twelve months.");
    }

    [Fact]
    public void Resolve_TableCellParagraph_SplicesByItsParaId()
    {
        // S1b: a table-cell paragraph is a first-class splice target — an edit to the nested-cell
        // paragraph (0000000D) resolves to that exact cell paragraph.
        using var doc = Open(MixedDocxWithParaIds());
        var index = ComposeParaIdSpliceMap.BuildParagraphIndex(doc.MainDocumentPart!.Document!.Body!);

        var resolution = ComposeParaIdSpliceMap.Resolve(index, new[] { "0000000D" });

        resolution.IsFullyMatched.Should().BeTrue();
        resolution.Matched["0000000D"].InnerText.Should().Be("Nested cell — late-fee schedule.");
    }

    [Fact]
    public void Resolve_UnmatchedParaId_SurfacedAsHandledError_NotSilentNoOp()
    {
        // FR-12: an edited paraId that matches no original paragraph (e.g. a client-side split minted an
        // id the original never had) is a HANDLED miss — it appears in Unmatched and drops IsFullyMatched,
        // so task 020's splice can surface it instead of silently no-op'ing or writing the wrong paragraph.
        // A matched id in the SAME batch still resolves.
        using var doc = Open(MixedDocxWithParaIds());
        var index = ComposeParaIdSpliceMap.BuildParagraphIndex(doc.MainDocumentPart!.Document!.Body!);

        var resolution = ComposeParaIdSpliceMap.Resolve(index, new[] { "0000000A", "DEADBEEF" });

        resolution.IsFullyMatched.Should().BeFalse();
        resolution.Unmatched.Should().ContainSingle().Which.Should().Be("DEADBEEF");
        resolution.Matched.Should().ContainKey("0000000A");
        resolution.Matched.Should().NotContainKey("DEADBEEF", "an unmatched id is never mapped to a wrong paragraph");
    }

    [Fact]
    public void Resolve_IsCaseInsensitive_LowercaseEditedIdMatchesUppercasedIndex()
    {
        // A Word-lowercased or client-cased edited id must still match the canonical (upper-cased) index
        // key — the paraId identity is case-insensitive.
        using var doc = Open(MixedDocxWithParaIds());
        var index = ComposeParaIdSpliceMap.BuildParagraphIndex(doc.MainDocumentPart!.Document!.Body!);

        var resolution = ComposeParaIdSpliceMap.Resolve(index, new[] { "0000000c" });

        resolution.IsFullyMatched.Should().BeTrue();
        resolution.Matched["0000000C"].InnerText.Should().Be("Cell paragraph — payment terms.");
    }
}
