// FR-02 (task 020, E1) — unit tests for ComposeParagraphSpliceService: the paraId-keyed
// edited-paragraph rebuild + splice that produces the spliced-edited .docx task 021's WmlComparer
// diffs against the retained original. Each test names a concrete production behavior that breaks if
// deleted (FR-02 / FR-12 / NFR-07 acceptance):
//   - given N paragraphs with K edited, exactly K differ pre-comparer; the other N-K are unchanged
//   - an edit to paragraph P updates EXACTLY the original paragraph with matching w14:paraId
//   - untouched paragraphs preserve paraId + text + structure (incl. tables); edited paragraphs
//     preserve paraId + w:pPr (style/numbering)
//   - a table-cell paragraph splices by its paraId (S1b)
//   - an unmatched/duplicate paraId is a HANDLED error (throws), never a silent no-op or wrong write
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): pure domain logic over real in-memory .docx
// fixtures (Open XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4). Assertions are
// behavioral (which paragraph changed, what was preserved), not implementation-shape.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeParagraphSpliceServiceTests
{
    private readonly ComposeParagraphSpliceService _sut = new();

    // ── in-memory .docx fixture builders ────────────────────────────────────────────────────────
    private static Paragraph Para(string paraId, string text, string? styleId = null)
    {
        var p = new Paragraph { ParagraphId = new HexBinaryValue(paraId) };
        if (styleId is not null)
        {
            p.AppendChild(new ParagraphProperties(new ParagraphStyleId { Val = styleId }));
        }
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
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
            body.Append(new SectionProperties());
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    // 5 top-level paragraphs, ids 0000000A..0000000E.
    private static byte[] FiveParagraphDoc() => BuildDocx(
        Para("0000000A", "The parties enter into this Agreement."),
        Para("0000000B", "Indemnification is capped at fees paid in the prior twelve months.", styleId: "Heading2"),
        Para("0000000C", "Each Party shall perform in a professional manner."),
        Para("0000000D", "Termination requires thirty days written notice."),
        Para("0000000E", "Confidential Information shall not be shared without consent."));

    // A doc with a table-cell paragraph (0000000T) between two body paragraphs.
    private static byte[] TableDoc() => BuildDocx(
        Para("00000001", "Before the table."),
        new Table(new TableRow(new TableCell(Para("0000000T", "Original cell text.")))),
        Para("00000002", "After the table."));

    /// <summary>Reads back paraId → settled text for every body paragraph (document order).</summary>
    private static Dictionary<string, string> ParaTextsById(byte[] docx)
    {
        using var buffer = new MemoryStream();
        buffer.Write(docx, 0, docx.Length);
        buffer.Position = 0;
        using var doc = WordprocessingDocument.Open(buffer, isEditable: false);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>())
        {
            map[p.ParagraphId!.Value!] = p.InnerText;
        }
        return map;
    }

    // -- exactly K differ -----------------------------------------------------------------------

    [Fact]
    public void SpliceEditedParagraphs_GivenNParagraphsWithKEdited_ExactlyKParagraphsDiffer()
    {
        var original = FiveParagraphDoc();
        var edits = new[]
        {
            new ComposeEditedParagraph("0000000B", "Indemnification is now UNCAPPED for confidentiality breaches."),
            new ComposeEditedParagraph("0000000D", "Termination now requires SIXTY days written notice."),
        };

        var spliced = _sut.SpliceEditedParagraphs(original, edits);

        var before = ParaTextsById(original);
        var after = ParaTextsById(spliced);
        var changed = after.Keys.Where(id => after[id] != before[id]).ToList();

        changed.Should().BeEquivalentTo(new[] { "0000000B", "0000000D" }, "exactly the 2 edited paragraphs differ");
        after["0000000A"].Should().Be(before["0000000A"], "untouched paragraphs pass through unchanged");
        after["0000000C"].Should().Be(before["0000000C"]);
        after["0000000E"].Should().Be(before["0000000E"]);
        after["0000000B"].Should().Be("Indemnification is now UNCAPPED for confidentiality breaches.");
        after["0000000D"].Should().Be("Termination now requires SIXTY days written notice.");
    }

    [Fact]
    public void SpliceEditedParagraphs_EditToParagraphP_UpdatesExactlyOriginalParagraphWithMatchingParaId()
    {
        var original = FiveParagraphDoc();

        var spliced = _sut.SpliceEditedParagraphs(original,
            new[] { new ComposeEditedParagraph("0000000C", "Rewritten performance clause.") });

        var after = ParaTextsById(spliced);
        after["0000000C"].Should().Be("Rewritten performance clause.");
        // Every OTHER paragraph is byte-for-byte its original text.
        after["0000000A"].Should().Be("The parties enter into this Agreement.");
        after["0000000B"].Should().Be("Indemnification is capped at fees paid in the prior twelve months.");
        after["0000000D"].Should().Be("Termination requires thirty days written notice.");
        after["0000000E"].Should().Be("Confidential Information shall not be shared without consent.");
    }

    // -- table-cell paragraph -------------------------------------------------------------------

    [Fact]
    public void SpliceEditedParagraphs_TableCellParagraph_SplicesByParaId()
    {
        var original = TableDoc();

        var spliced = _sut.SpliceEditedParagraphs(original,
            new[] { new ComposeEditedParagraph("0000000T", "Rewritten cell text.") });

        var after = ParaTextsById(spliced);
        after["0000000T"].Should().Be("Rewritten cell text.", "the table-cell paragraph spliced by its paraId (S1b)");
        after["00000001"].Should().Be("Before the table.", "surrounding paragraphs untouched");
        after["00000002"].Should().Be("After the table.");

        // The table structure survived the splice.
        using var buffer = new MemoryStream(spliced);
        using var doc = WordprocessingDocument.Open(buffer, isEditable: false);
        doc.MainDocumentPart!.Document!.Body!.Descendants<Table>().Should().ContainSingle();
    }

    // -- preservation (NFR-07) ------------------------------------------------------------------

    [Fact]
    public void SpliceEditedParagraphs_EditedParagraph_PreservesParaIdAndParagraphStyle()
    {
        var original = FiveParagraphDoc(); // 0000000B carries ParagraphStyleId "Heading2"

        var spliced = _sut.SpliceEditedParagraphs(original,
            new[] { new ComposeEditedParagraph("0000000B", "New heading text.") });

        using var buffer = new MemoryStream(spliced);
        using var doc = WordprocessingDocument.Open(buffer, isEditable: false);
        var edited = doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>().Single(p => p.ParagraphId?.Value == "0000000B");

        edited.InnerText.Should().Be("New heading text.");
        edited.ParagraphId!.Value.Should().Be("0000000B", "the w14:paraId (splice key + comparer anchor) survives the rebuild");
        edited.ParagraphProperties?.ParagraphStyleId?.Val?.Value.Should()
            .Be("Heading2", "the paragraph style is preserved through the rebuild (NFR-07)");
    }

    [Fact]
    public void SpliceEditedParagraphs_UntouchedParagraphs_PreserveParaIds()
    {
        var original = FiveParagraphDoc();

        var spliced = _sut.SpliceEditedParagraphs(original,
            new[] { new ComposeEditedParagraph("0000000A", "Edited first paragraph.") });

        var after = ParaTextsById(spliced);
        after.Keys.Should().BeEquivalentTo(new[] { "0000000A", "0000000B", "0000000C", "0000000D", "0000000E" },
            "every original paraId is preserved — the splice never drops or regenerates an id");
    }

    // -- no-op / negative -----------------------------------------------------------------------

    [Fact]
    public void SpliceEditedParagraphs_EmptyEditList_ReturnsDocumentWithAllParagraphsUnchanged()
    {
        var original = FiveParagraphDoc();

        var spliced = _sut.SpliceEditedParagraphs(original, Array.Empty<ComposeEditedParagraph>());

        ParaTextsById(spliced).Should().BeEquivalentTo(ParaTextsById(original), "a no-op splice changes nothing");
    }

    [Fact]
    public void SpliceEditedParagraphs_UnmatchedParaId_ThrowsAndModifiesNothing()
    {
        var original = FiveParagraphDoc();
        // A batch with one VALID edit and one UNMATCHED id — fail-fast means NEITHER applies.
        var edits = new[]
        {
            new ComposeEditedParagraph("0000000C", "This valid edit must NOT be applied when the batch fails."),
            new ComposeEditedParagraph("DEADBEEF", "No paragraph has this id."),
        };

        var act = () => _sut.SpliceEditedParagraphs(original, edits);

        act.Should().Throw<ComposeSpliceException>().WithMessage("*DEADBEEF*")
            .Which.Message.Should().Contain("aborted", "an unmatched paraId aborts the whole splice — no partial/wrong write");
    }

    [Fact]
    public void SpliceEditedParagraphs_DuplicateEditedParaId_Throws()
    {
        var original = FiveParagraphDoc();
        var edits = new[]
        {
            new ComposeEditedParagraph("0000000C", "first"),
            new ComposeEditedParagraph("0000000C", "second — same id"),
        };

        var act = () => _sut.SpliceEditedParagraphs(original, edits);

        act.Should().Throw<ComposeSpliceException>().WithMessage("*Duplicate*");
    }

    [Fact]
    public void SpliceEditedParagraphs_MalformedBytes_ThrowsComposeSpliceException()
    {
        var notADocx = new byte[] { 1, 2, 3, 4, 5 };

        var act = () => _sut.SpliceEditedParagraphs(notADocx,
            new[] { new ComposeEditedParagraph("0000000A", "x") });

        act.Should().Throw<ComposeSpliceException>().WithMessage("*not a readable*");
    }

    [Fact]
    public void SpliceEditedParagraphs_EmptyBytes_ThrowsArgumentException()
    {
        var act = () => _sut.SpliceEditedParagraphs(ReadOnlyMemory<byte>.Empty, Array.Empty<ComposeEditedParagraph>());

        act.Should().Throw<ArgumentException>();
    }
}
