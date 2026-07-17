// FR-03 / FR-05 (task 021, E1) — unit tests for ComposeRedlineComparerService: the thin Docxodus
// WmlComparer adapter that synthesizes the minimal w:ins/w:del (+ rPr/pPrChange) redline between the
// retained load-time original and the task-020 spliced-edited .docx. Each test names a concrete
// production behavior that breaks if deleted (FR-03 / FR-05 / D4 acceptance):
//   - N paragraphs with K edited → minimal ins/del + author attribution; untouched paras carry NO
//     revisions and keep their w14:paraId (S1: comparer preserves ids on unchanged paragraphs)
//   - bolding a word inside an otherwise-unchanged paragraph → a FORMAT change (rPr/pPrChange),
//     asserted NOT a full-run delete+re-insert (FR-05 / D4)
//   - the adapter throws NO exception on the S1/S1b hard fixtures: nested tables, 3-level numbering,
//     whole-paragraph delete, paragraph split
//   - malformed/empty inputs are HANDLED errors (throw), never a silent wrong write
//
// Banned-pattern compliance (tests/CLAUDE.md B1-B17): pure domain logic over real in-memory .docx
// fixtures (Open XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4). The
// text-edit fixtures are produced through ComposeParagraphSpliceService (task 020) so the tests
// exercise the real 020→021 pipeline shape; structural edge cases (delete/split/format) are hand-built.
// Assertions are behavioral (which revisions were emitted, what was preserved), not implementation-shape.

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

public sealed class ComposeRedlineComparerServiceTests
{
    private const string Author = "Spaarke AI";
    private static readonly DateTimeOffset RevisionStamp = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private readonly ComposeRedlineComparerService _sut = new();
    private readonly ComposeParagraphSpliceService _splicer = new();

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

    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren) =>
        BuildDocx(numbering: null, bodyChildren);

    private static byte[] BuildDocx(Numbering? numbering, params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();

            // A styles part is present on every real Word-authored .docx (and preserved through the
            // task-020 splice). WmlComparer reads it (AddFootnotesEndnotesStyles) — a fixture without
            // one is unrealistically minimal and makes the comparer throw. Include Normal + Heading2.
            var stylePart = main.AddNewPart<StyleDefinitionsPart>();
            stylePart.Styles = new Styles(
                new Style(new StyleName { Val = "Normal" }) { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new Style(new StyleName { Val = "heading 2" }) { Type = StyleValues.Paragraph, StyleId = "Heading2" });
            stylePart.Styles.Save();

            if (numbering is not null)
            {
                var numPart = main.AddNewPart<NumberingDefinitionsPart>();
                numPart.Numbering = numbering;
                numPart.Numbering.Save();
            }

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

    // -- revision reader ------------------------------------------------------------------------

    private sealed record Revisions(
        int InsCount,
        int DelCount,
        IReadOnlyList<string> Authors,
        int RunFormatChanges,
        int ParagraphFormatChanges);

    private static Revisions ReadRevisions(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;

        var ins = body.Descendants<InsertedRun>().ToList();
        var del = body.Descendants<DeletedRun>().ToList();
        var authors = ins.Select(i => i.Author?.Value)
            .Concat(del.Select(d => d.Author?.Value))
            .Where(a => a is not null)
            .Select(a => a!)
            .Distinct()
            .ToList();

        return new Revisions(
            ins.Count,
            del.Count,
            authors,
            body.Descendants<RunPropertiesChange>().Count(),
            body.Descendants<ParagraphPropertiesChange>().Count());
    }

    private static Paragraph? ParagraphByParaId(byte[] docx, string paraId)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .FirstOrDefault(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase))
            // The reopened paragraph is detached from the disposed package, so clone it out.
            ?.CloneNode(true) as Paragraph;
    }

    // -- minimal ins/del + author attribution (FR-03) -------------------------------------------

    [Fact]
    public void SynthesizeRedline_ThreeEditedParagraphs_EmitsInsDelWithAuthorAttribution()
    {
        var original = FiveParagraphDoc();
        var edited = _splicer.SpliceEditedParagraphs(original, new[]
        {
            new ComposeEditedParagraph("0000000A", "The parties hereby enter into this amended Agreement."),
            new ComposeEditedParagraph("0000000C", "Each Party shall perform its obligations diligently and professionally."),
            new ComposeEditedParagraph("0000000E", "Confidential Information shall not be disclosed to any third party without prior written consent."),
        });

        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        var rev = ReadRevisions(redline);
        rev.InsCount.Should().BeGreaterThan(0, "the 3 text edits synthesize tracked insertions");
        rev.DelCount.Should().BeGreaterThan(0, "the 3 text edits synthesize tracked deletions of the replaced text");
        rev.Authors.Should().ContainSingle().Which.Should().Be(Author, "every emitted revision carries the supplied author attribution");
    }

    [Fact]
    public void SynthesizeRedline_UntouchedParagraphs_CarryNoRevisionsAndKeepParaId()
    {
        var original = FiveParagraphDoc();
        var edited = _splicer.SpliceEditedParagraphs(original, new[]
        {
            new ComposeEditedParagraph("0000000B", "Indemnification is now UNCAPPED for confidentiality breaches."),
        });

        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        // The 4 untouched paragraphs pass through with their w14:paraId (S1: comparer preserves ids on
        // unchanged paragraphs) and carry NO tracked-change markup — the redline is minimal, not a
        // whole-body rewrite.
        foreach (var untouchedId in new[] { "0000000A", "0000000C", "0000000D", "0000000E" })
        {
            var p = ParagraphByParaId(redline, untouchedId);
            p.Should().NotBeNull($"untouched paragraph {untouchedId} survives the comparison and keeps its paraId");
            p!.Descendants<InsertedRun>().Should().BeEmpty($"{untouchedId} was not edited — no w:ins");
            p.Descendants<DeletedRun>().Should().BeEmpty($"{untouchedId} was not edited — no w:del");
        }
    }

    [Fact]
    public void SynthesizeRedline_IdenticalDocuments_EmitNoRevisions()
    {
        var original = FiveParagraphDoc();

        var redline = _sut.SynthesizeRedline(original, original, Author, RevisionStamp);

        var rev = ReadRevisions(redline);
        rev.InsCount.Should().Be(0, "identical documents produce no insertions");
        rev.DelCount.Should().Be(0, "identical documents produce no deletions");
        rev.RunFormatChanges.Should().Be(0);
        rev.ParagraphFormatChanges.Should().Be(0);
    }

    // -- FR-05 / D4: format change, NOT del+ins -------------------------------------------------

    [Fact]
    public void SynthesizeRedline_BoldingAWord_EmitsFormatChangeNotDeleteReinsert()
    {
        // Original: one plain run. Edited: identical TEXT, but the word "indemnity" is now a bold run.
        var original = BuildDocx(
            Para("0000000A", "The parties enter into this Agreement."),
            new Paragraph(
                new Run(new Text("The indemnity term is capped at fees paid.") { Space = SpaceProcessingModeValues.Preserve }))
            { ParagraphId = new HexBinaryValue("0000000B") },
            Para("0000000C", "Termination requires thirty days written notice."));

        var edited = BuildDocx(
            Para("0000000A", "The parties enter into this Agreement."),
            new Paragraph(
                new Run(new Text("The ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new RunProperties(new Bold()), new Text("indemnity") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new Text(" term is capped at fees paid.") { Space = SpaceProcessingModeValues.Preserve }))
            { ParagraphId = new HexBinaryValue("0000000B") },
            Para("0000000C", "Termination requires thirty days written notice."));

        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        var rev = ReadRevisions(redline);
        (rev.RunFormatChanges + rev.ParagraphFormatChanges).Should()
            .BeGreaterThan(0, "an inline run-format edit is represented via Format-Change Detection (rPr/pPrChange), not del+ins (FR-05 / D4)");

        // And critically: the unchanged word "indemnity" was NOT struck-and-retyped.
        DeletedTextValues(redline).Should()
            .NotContain(t => t.Contains("indemnity", StringComparison.Ordinal),
                "a bold-only change must NOT delete+re-insert the run text (the D4 regression this guards)");
    }

    private static IReadOnlyList<string> DeletedTextValues(byte[] docx)
    {
        using var buffer = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(buffer, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!
            .Descendants<DeletedText>()
            .Select(d => d.Text)
            .ToList();
    }

    // -- S1/S1b edge cases: no exception --------------------------------------------------------

    [Fact]
    public void SynthesizeRedline_NestedTables_DoesNotThrowAndPreservesTables()
    {
        // A table whose cell contains a nested table (table-in-a-cell) between two body paragraphs.
        var original = BuildDocx(
            Para("00000001", "Before the table."),
            new Table(new TableRow(new TableCell(
                new Table(new TableRow(new TableCell(Para("0000000N", "Nested cell text.")))),
                Para("0000000T", "Outer cell text.")))),
            Para("00000002", "After the table."));

        var edited = _splicer.SpliceEditedParagraphs(original, new[]
        {
            new ComposeEditedParagraph("0000000N", "Nested cell text, revised."),
        });

        // A throw here IS the failure this test guards (WmlComparer must be robust on nested tables, S1b).
        var redline = _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        using var buffer = new MemoryStream(redline);
        using var doc = WordprocessingDocument.Open(buffer, isEditable: false);
        doc.MainDocumentPart!.Document!.Body!.Descendants<Table>().Count()
            .Should().Be(2, "both the outer and nested tables survive the comparison (S1b)");
    }

    [Fact]
    public void SynthesizeRedline_MultiLevelNumbering_DoesNotThrow()
    {
        var numbering = ThreeLevelNumbering();
        var original = BuildDocx(numbering,
            NumberedPara("00000010", "Scope of the engagement.", level: 0),
            NumberedPara("00000011", "Deliverables and milestones.", level: 1),
            NumberedPara("00000012", "Acceptance criteria for each milestone.", level: 2),
            Para("00000013", "General provisions follow."));

        var edited = _splicer.SpliceEditedParagraphs(original, new[]
        {
            new ComposeEditedParagraph("00000012", "Acceptance criteria and testing gates for each milestone."),
        });

        var act = () => _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        act.Should().NotThrow("WmlComparer is robust on 3-level numbering (S1b)");
    }

    [Fact]
    public void SynthesizeRedline_WholeParagraphDelete_DoesNotThrowAndEmitsDeletion()
    {
        var original = FiveParagraphDoc();
        // Edited doc = original minus paragraph C (a whole-paragraph delete — a boundary case).
        var edited = BuildDocx(
            Para("0000000A", "The parties enter into this Agreement."),
            Para("0000000B", "Indemnification is capped at fees paid in the prior twelve months.", styleId: "Heading2"),
            Para("0000000D", "Termination requires thirty days written notice."),
            Para("0000000E", "Confidential Information shall not be shared without consent."));

        // A throw here IS the failure (WmlComparer represents a whole-paragraph delete as a w:del-marked
        // paragraph, not an exception — S1b).
        var rev = ReadRevisions(_sut.SynthesizeRedline(original, edited, Author, RevisionStamp));
        rev.DelCount.Should().BeGreaterThan(0, "the removed paragraph surfaces as tracked deletion markup (struck, not vanished)");
    }

    [Fact]
    public void SynthesizeRedline_ParagraphSplit_DoesNotThrow()
    {
        var original = BuildDocx(
            Para("0000000A", "The parties enter into this Agreement."),
            Para("0000000B", "First sentence stands alone. Second sentence follows it."),
            Para("0000000C", "Termination requires thirty days written notice."));

        // Edited: paragraph B split into two — one half keeps the original id, the other gets a fresh id.
        var edited = BuildDocx(
            Para("0000000A", "The parties enter into this Agreement."),
            Para("0000000B", "First sentence stands alone."),
            Para("00000031", "Second sentence follows it."),
            Para("0000000C", "Termination requires thirty days written notice."));

        var act = () => _sut.SynthesizeRedline(original, edited, Author, RevisionStamp);

        act.Should().NotThrow("WmlComparer is robust on a paragraph split (S1b)");
    }

    // -- negative / guard paths -----------------------------------------------------------------

    [Fact]
    public void SynthesizeRedline_EmptyOriginal_ThrowsArgumentException()
    {
        var act = () => _sut.SynthesizeRedline(ReadOnlyMemory<byte>.Empty, FiveParagraphDoc(), Author);
        act.Should().Throw<ArgumentException>().WithParameterName("retainedOriginal");
    }

    [Fact]
    public void SynthesizeRedline_EmptyEdited_ThrowsArgumentException()
    {
        var act = () => _sut.SynthesizeRedline(FiveParagraphDoc(), ReadOnlyMemory<byte>.Empty, Author);
        act.Should().Throw<ArgumentException>().WithParameterName("splicedEdited");
    }

    [Fact]
    public void SynthesizeRedline_WhitespaceAuthor_ThrowsArgumentException()
    {
        var act = () => _sut.SynthesizeRedline(FiveParagraphDoc(), FiveParagraphDoc(), "   ");
        act.Should().Throw<ArgumentException>().WithParameterName("author");
    }

    [Fact]
    public void SynthesizeRedline_MalformedBytes_ThrowsComposeRedlineException()
    {
        var notADocx = new byte[] { 1, 2, 3, 4, 5 };

        var act = () => _sut.SynthesizeRedline(notADocx, FiveParagraphDoc(), Author);

        act.Should().Throw<ComposeRedlineException>();
    }

    // ── numbering fixture helpers ───────────────────────────────────────────────────────────────

    private static Numbering ThreeLevelNumbering()
    {
        var abstractNum = new AbstractNum(
            new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1." }) { LevelIndex = 0 },
            new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2." }) { LevelIndex = 1 },
            new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2.%3." }) { LevelIndex = 2 })
        { AbstractNumberId = 0 };
        var num = new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 };
        return new Numbering(abstractNum, num);
    }

    private static Paragraph NumberedPara(string paraId, string text, int level)
    {
        var p = new Paragraph { ParagraphId = new HexBinaryValue(paraId) };
        p.AppendChild(new ParagraphProperties(
            new NumberingProperties(
                new NumberingLevelReference { Val = level },
                new NumberingId { Val = 1 })));
        p.AppendChild(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        return p;
    }
}
