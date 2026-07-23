// FR-01a / FR-27 (task 026, E1 born-in-editor) — unit tests for ComposeDocumentRenderer, the from-scratch
// high-fidelity OOXML authoring engine (the server-side replacement for the removed client docx.js exporter).
// These assert the PRODUCTION FIDELITY behaviors a born-in-editor legal document depends on:
//   - a 1/1.1/1.1.1 clause tree is authored as a STYLE-LINKED multi-level abstractNum (%N lvlText), NOT
//     literal "1." text runs and NOT a direct paragraph numId alongside the style numId (the double-numbering
//     failure FR-27 forbids)
//   - real Heading1..6 styles (mammoth maps "heading N" style → hN on the next Load), inline w:b/w:i/w:u, native w:tbl
//   - every emitted w:p (incl. table-cell paragraphs) carries a unique OOXML-valid w14:paraId (E2 substrate)
//   - the rendered doc re-opens + round-trips through ParaIdPreParser (the load-time E2 pre-parse)
//   - a subsequent tracked-change edit (ComposeParagraphRedlineSynthesizer) does NOT corrupt the numbering
//   - a golden-file assertion pins the numbering.xml SHAPE (the single largest net-new authoring surface)
//
// Pure engine (ComposeContentModel in / byte[] out) — real .docx assertions via the Open XML SDK, no transport
// mocks (ADR-038 B1), no DI/ctor tests (B3/B4). Domain-logic tests co-located with the sibling Compose
// component tests (ParaIdPreParserTests, ComposeParagraphRedlineSynthesizerTests).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeDocumentRendererTests
{
    private static ComposeDocumentRenderer Renderer() => new();

    // ── model builders ────────────────────────────────────────────────────────────────────────────

    private static ComposeBlock Heading(int level, string text, string? paraId = null) => new()
    {
        Kind = ComposeBlockKind.Heading,
        Level = level,
        ParaId = paraId,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeBlock Paragraph(string text, string? paraId = null) => new()
    {
        Kind = ComposeBlockKind.Paragraph,
        ParaId = paraId,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeBlock ListItem(int level, bool ordered, string text, bool startsNewList = false) => new()
    {
        Kind = ComposeBlockKind.ListItem,
        Level = level,
        Ordered = ordered,
        StartsNewList = startsNewList,
        Runs = new[] { new ComposeInlineRun { Text = text } },
    };

    private static ComposeContentModel ClauseTree() => new()
    {
        Blocks = new[]
        {
            Heading(1, "Definitions"),
            Heading(2, "Confidential Information"),
            Heading(3, "Exclusions"),
        },
    };

    // ── OOXML query helpers ───────────────────────────────────────────────────────────────────────

    private static WordprocessingDocument Open(byte[] docx) =>
        WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);

    private static IReadOnlyList<Paragraph> Paragraphs(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>().ToList();

    private static Numbering Numbering(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;

    private static Styles StyleCatalog(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!;

    private static Level LevelOf(AbstractNum abstractNum, int ilvl) =>
        abstractNum.Elements<Level>().Single(l => l.LevelIndex?.Value == ilvl);

    // ── criterion 1: style-linked multi-level clause numbering (the keystone) ───────────────────────

    [Fact]
    public void SynthesizeDocument_WithClauseTree_AuthorsStyleLinkedMultiLevelAbstractNum()
    {
        var bytes = Renderer().SynthesizeDocument(ClauseTree(), "Jordan Avery");

        using var doc = Open(bytes);
        var numbering = Numbering(doc);

        var headingAbstract = numbering.Elements<AbstractNum>().Single(a => a.AbstractNumberId?.Value == 0);
        headingAbstract.GetFirstChild<MultiLevelType>()!.Val!.Value.Should().Be(MultiLevelValues.Multilevel,
            "a clause scheme is ONE multilevel abstractNum, not per-level singles");

        // Each level back-links its Heading style (the abstract side of the style-link) and cascades %N.
        LevelOf(headingAbstract, 0).GetFirstChild<ParagraphStyleIdInLevel>()!.Val!.Value.Should().Be("Heading1");
        LevelOf(headingAbstract, 0).GetFirstChild<LevelText>()!.Val!.Value.Should().Be("%1");
        LevelOf(headingAbstract, 1).GetFirstChild<ParagraphStyleIdInLevel>()!.Val!.Value.Should().Be("Heading2");
        LevelOf(headingAbstract, 1).GetFirstChild<LevelText>()!.Val!.Value.Should().Be("%1.%2");
        LevelOf(headingAbstract, 2).GetFirstChild<ParagraphStyleIdInLevel>()!.Val!.Value.Should().Be("Heading3");
        LevelOf(headingAbstract, 2).GetFirstChild<LevelText>()!.Val!.Value.Should().Be("%1.%2.%3");

        // ONE num instance (numId 1) → the heading abstract; the Heading styles reference it.
        var headingNum = numbering.Elements<NumberingInstance>().Single(n => n.NumberID?.Value == 1);
        headingNum.GetFirstChild<AbstractNumId>()!.Val!.Value.Should().Be(0);
    }

    [Fact]
    public void SynthesizeDocument_HeadingParagraphs_UseStyleOnlyWithNoDirectNumIdOrLiteralNumberText()
    {
        var bytes = Renderer().SynthesizeDocument(ClauseTree(), "Jordan Avery");

        using var doc = Open(bytes);
        var headings = Paragraphs(doc);

        headings.Should().HaveCount(3);
        for (var i = 0; i < headings.Count; i++)
        {
            var pPr = headings[i].GetFirstChild<ParagraphProperties>()!;
            pPr.GetFirstChild<ParagraphStyleId>()!.Val!.Value.Should().Be($"Heading{i + 1}",
                "a clause heading is numbered THROUGH its Heading style");
            pPr.GetFirstChild<NumberingProperties>().Should().BeNull(
                "a DIRECT paragraph numId alongside the style numId is exactly the double-numbering FR-27 forbids");
            headings[i].InnerText.Should().NotMatchRegex(@"^\s*\d+\.",
                "the number is rendered by Word from the numbering definition — NOT baked in as literal '1.' text");
        }

        // The Heading styles carry the numPr (the STYLE side of the style-link) at their own ilvl.
        var styles = StyleCatalog(doc);
        for (var level = 1; level <= 3; level++)
        {
            var numPr = styles.Elements<Style>().Single(s => s.StyleId == $"Heading{level}")
                .StyleParagraphProperties!.GetFirstChild<NumberingProperties>()!;
            numPr.GetFirstChild<NumberingId>()!.Val!.Value.Should().Be(1);
            numPr.GetFirstChild<NumberingLevelReference>()!.Val!.Value.Should().Be(level - 1);
        }
    }

    // ── criterion 2: real styles + inline marks + native tables ─────────────────────────────────────

    [Fact]
    public void SynthesizeDocument_Headings_UseRealHeadingStylesNamedForMammothRoundTrip()
    {
        var bytes = Renderer().SynthesizeDocument(ClauseTree(), "author");

        using var doc = Open(bytes);
        var styles = StyleCatalog(doc);

        // mammoth (the Load-path convert) maps a paragraph style NAMED "heading N" → hN. Assert the names.
        for (var level = 1; level <= 6; level++)
        {
            var style = styles.Elements<Style>().SingleOrDefault(s => s.StyleId == $"Heading{level}");
            style.Should().NotBeNull($"Heading{level} must exist for clause depth {level}");
            style!.StyleName!.Val!.Value.Should().Be($"heading {level}");
            style.GetFirstChild<OutlineLevel>().Should().BeNull("outlineLvl lives under the style's pPr");
            style.StyleParagraphProperties!.GetFirstChild<OutlineLevel>()!.Val!.Value.Should().Be(level - 1);
        }

        styles.Elements<Style>().Should().Contain(s => s.StyleId == "Normal");
        styles.Elements<Style>().Should().Contain(s => s.StyleId == "ListParagraph");
    }

    [Fact]
    public void SynthesizeDocument_InlineMarks_EmitBoldItalicUnderlineRunProperties()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun { Text = "plain " },
                        new ComposeInlineRun { Text = "bold ", Bold = true },
                        new ComposeInlineRun { Text = "italic ", Italic = true },
                        new ComposeInlineRun { Text = "underlined", Underline = true },
                    },
                },
            },
        };

        var bytes = Renderer().SynthesizeDocument(model, "author");

        using var doc = Open(bytes);
        var runs = Paragraphs(doc).Single().Elements<Run>().ToList();

        runs.Should().HaveCount(4);
        runs[0].RunProperties.Should().BeNull("a plain run carries no rPr");
        runs[1].RunProperties!.GetFirstChild<Bold>().Should().NotBeNull();
        runs[2].RunProperties!.GetFirstChild<Italic>().Should().NotBeNull();
        runs[3].RunProperties!.GetFirstChild<Underline>()!.Val!.Value.Should().Be(UnderlineValues.Single);
    }

    [Fact]
    public void SynthesizeDocument_Table_RendersNativeTblWithCellParagraphs()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Table,
                    Table = new ComposeTable
                    {
                        Rows = new[]
                        {
                            new ComposeTableRow
                            {
                                Cells = new[]
                                {
                                    new ComposeTableCell { IsHeader = true, Blocks = new[] { Paragraph("Term") } },
                                    new ComposeTableCell { IsHeader = true, Blocks = new[] { Paragraph("Meaning") } },
                                },
                            },
                            new ComposeTableRow
                            {
                                Cells = new[]
                                {
                                    new ComposeTableCell { Blocks = new[] { Paragraph("Affiliate") } },
                                    new ComposeTableCell { Blocks = new[] { Paragraph("A controlled entity.") } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var bytes = Renderer().SynthesizeDocument(model, "author");

        using var doc = Open(bytes);
        var table = doc.MainDocumentPart!.Document!.Body!.Elements<Table>().Single();
        table.Elements<TableRow>().Should().HaveCount(2);
        table.Elements<TableRow>().First().Elements<TableCell>().Should().HaveCount(2);
        table.Descendants<Paragraph>().Select(p => p.InnerText)
            .Should().Contain(new[] { "Term", "Meaning", "Affiliate", "A controlled entity." });

        // Header cells are emphasized (bold runs).
        var headerCellRun = table.Elements<TableRow>().First().Elements<TableCell>().First().Descendants<Run>().First();
        headerCellRun.RunProperties!.GetFirstChild<Bold>().Should().NotBeNull("header cells bold their text");
    }

    // ── criterion 3: unique paraId on every w:p incl. table cells ───────────────────────────────────

    [Fact]
    public void SynthesizeDocument_AssignsUniqueParaIdToEveryParagraph_IncludingTableCells()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                Heading(1, "Scope"),
                Paragraph("This section defines scope."),
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Table,
                    Table = new ComposeTable
                    {
                        Rows = new[]
                        {
                            new ComposeTableRow
                            {
                                Cells = new[]
                                {
                                    new ComposeTableCell { Blocks = new[] { Paragraph("cell A") } },
                                    new ComposeTableCell { Blocks = new[] { Paragraph("cell B") } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var bytes = Renderer().SynthesizeDocument(model, "author");

        using var doc = Open(bytes);
        var ids = Paragraphs(doc).Select(p => p.ParagraphId?.Value).ToList();

        ids.Should().OnlyContain(id => !string.IsNullOrEmpty(id), "every w:p — body + table-cell — carries a paraId");
        ids.Should().OnlyHaveUniqueItems();
        ids.Should().OnlyContain(id => IsValidParaId(id!), "every minted id is a valid ST_LongHexNumber (0 < x < 0x80000000)");
        ids.Should().HaveCount(4, "1 heading + 1 paragraph + 2 table-cell paragraphs");
    }

    [Fact]
    public void SynthesizeDocument_CarriesClientParaId_WhenSupplied_AndMintsForGaps()
    {
        const string clientId = "0A1B2C3D";
        var model = new ComposeContentModel
        {
            Blocks = new[] { Paragraph("carried", clientId), Paragraph("minted", paraId: null) },
        };

        var bytes = Renderer().SynthesizeDocument(model, "author");

        using var doc = Open(bytes);
        var ids = Paragraphs(doc).Select(p => p.ParagraphId!.Value!).ToList();

        ids[0].Should().Be(clientId, "a valid client-carried paraId is preserved (E2 identity survives the render)");
        ids[1].Should().NotBeNullOrEmpty();
        ids[1].Should().NotBe(clientId);
        IsValidParaId(ids[1]).Should().BeTrue();
    }

    [Fact]
    public void SynthesizeDocument_WithDuplicateClientParaIds_ReMintsToKeepEveryParaIdUnique()
    {
        const string dupId = "0000ABCD";
        var model = new ComposeContentModel
        {
            Blocks = new[] { Paragraph("first", dupId), Paragraph("second", dupId), Paragraph("third", dupId) },
        };

        var bytes = Renderer().SynthesizeDocument(model, "author");

        using var doc = Open(bytes);
        var ids = Paragraphs(doc).Select(p => p.ParagraphId!.Value!).ToList();

        ids.Should().HaveCount(3);
        ids.Should().OnlyHaveUniqueItems("a malformed model that repeats a paraId must never emit a duplicate splice key");
        ids.Should().Contain(dupId, "the FIRST occurrence keeps the client id; later duplicates are re-minted");
        ids.Should().OnlyContain(id => IsValidParaId(id), "the re-minted ids are valid ST_LongHexNumber");
    }

    // ── criterion 2 (round-trip): rendered doc re-opens + round-trips through ParaIdPreParser ────────

    [Fact]
    public void SynthesizeDocument_Output_RoundTripsThroughParaIdPreParser()
    {
        var bytes = Renderer().SynthesizeDocument(ClauseTree(), "author");

        // The load-time E2 pre-parse re-reads the rendered doc: one entry per paragraph, every id already
        // present (verbatim, none minted at Load — the renderer already assigned them).
        var result = new ParaIdPreParser().Parse(bytes);

        result.Entries.Should().HaveCount(3);
        result.Entries.Should().OnlyContain(e => e.IsMinted == false,
            "the rendered doc's paraIds are read back verbatim — the pre-parse mints nothing");
        result.Entries.Select(e => e.ParaId).Should().OnlyHaveUniqueItems();
    }

    // ── criterion 5: a tracked-change edit does not corrupt the numbering ───────────────────────────

    [Fact]
    public void RenderedDoc_AfterTrackedChangeEdit_KeepsNumberingIntactAndStyleLinked()
    {
        var rendered = Renderer().SynthesizeDocument(ClauseTree(), "Jordan Avery");
        var numberingBefore = PartXml(rendered, d => d.MainDocumentPart!.NumberingDefinitionsPart!);

        // Edit the middle heading's text in place (task 022 synthesizer), keyed by its paraId.
        string editedParaId;
        using (var doc = Open(rendered))
        {
            editedParaId = Paragraphs(doc)[1].ParagraphId!.Value!;
        }

        var edited = new ComposeParagraphRedlineSynthesizer().SynthesizeRedline(
            rendered,
            new[] { new ComposeEditedParagraph(editedParaId, "Confidential Information and Trade Secrets") },
            "Jordan Avery",
            DateTimeOffset.UtcNow);

        var numberingAfter = PartXml(edited, d => d.MainDocumentPart!.NumberingDefinitionsPart!);
        numberingAfter.Should().Be(numberingBefore, "the tracked-change edit rewrites run content only — numbering is instance-clean + style-linked at birth, so it is untouched");

        using var editedDoc = Open(edited);
        var editedHeading = Paragraphs(editedDoc).Single(p => string.Equals(p.ParagraphId?.Value, editedParaId, StringComparison.OrdinalIgnoreCase));
        editedHeading.GetFirstChild<ParagraphProperties>()!.GetFirstChild<ParagraphStyleId>()!.Val!.Value.Should().Be("Heading2");
        editedHeading.GetFirstChild<ParagraphProperties>()!.GetFirstChild<NumberingProperties>().Should().BeNull();
        editedDoc.MainDocumentPart!.Document!.Body!.Descendants<InsertedRun>().Should().NotBeEmpty("the edit produced tracked-change insertions");
    }

    // ── ordered-list restart ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SynthesizeDocument_TwoOrderedLists_EmitSeparateRestartingNumInstances()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                ListItem(0, ordered: true, "first list, item 1"),
                ListItem(0, ordered: true, "first list, item 2"),
                Paragraph("a paragraph breaks the list run"),
                ListItem(0, ordered: true, "second list, item 1", startsNewList: true),
            },
        };

        var bytes = Renderer().SynthesizeDocument(model, "author");

        using var doc = Open(bytes);
        var numbering = Numbering(doc);

        var orderedInstances = numbering.Elements<NumberingInstance>()
            .Where(n => n.GetFirstChild<AbstractNumId>()!.Val!.Value == 1) // ordered abstract
            .ToList();
        orderedInstances.Should().HaveCount(2, "two distinct ordered lists → two num instances");
        orderedInstances.Should().OnlyContain(n =>
            n.GetFirstChild<LevelOverride>()!.GetFirstChild<StartOverrideNumberingValue>()!.Val!.Value == 1,
            "each restarting list carries a startOverride of 1");

        // The two list-item paragraphs of the FIRST list share one numId; the second list uses a different numId.
        var listParas = Paragraphs(doc)
            .Where(p => p.GetFirstChild<ParagraphProperties>()?.GetFirstChild<ParagraphStyleId>()?.Val?.Value == "ListParagraph")
            .Select(p => p.GetFirstChild<ParagraphProperties>()!.GetFirstChild<NumberingProperties>()!.GetFirstChild<NumberingId>()!.Val!.Value)
            .ToList();
        listParas.Should().HaveCount(3);
        listParas[0].Should().Be(listParas[1], "the two items of the first list share a num instance");
        listParas[2].Should().NotBe(listParas[0], "the second (restarted) list uses a fresh num instance");
    }

    // ── golden-file: pin the heading numbering.xml SHAPE (largest net-new surface) ───────────────────

    [Fact]
    public void SynthesizeDocument_HeadingNumberingXml_MatchesGoldenShape()
    {
        var bytes = Renderer().SynthesizeDocument(ClauseTree(), "author");

        using var doc = Open(bytes);
        var headingAbstract = Numbering(doc).Elements<AbstractNum>().Single(a => a.AbstractNumberId?.Value == 0);

        // Golden shape: for each of the 9 levels, (ilvl, pStyle-or-none, lvlText, numFmt). Levels 0-5
        // style-link Heading1..6; 6-8 are decimal-cascaded but unlinked (headings only reach level 6).
        var shape = headingAbstract.Elements<Level>()
            .OrderBy(l => l.LevelIndex!.Value)
            .Select(l => (
                Ilvl: l.LevelIndex!.Value,
                Style: l.GetFirstChild<ParagraphStyleIdInLevel>()?.Val?.Value ?? "-",
                LvlText: l.GetFirstChild<LevelText>()!.Val!.Value!,
                Fmt: l.GetFirstChild<NumberingFormat>()!.Val!.Value))
            .ToList();

        shape.Should().Equal(new List<(int, string, string, NumberFormatValues)>
        {
            (0, "Heading1", "%1", NumberFormatValues.Decimal),
            (1, "Heading2", "%1.%2", NumberFormatValues.Decimal),
            (2, "Heading3", "%1.%2.%3", NumberFormatValues.Decimal),
            (3, "Heading4", "%1.%2.%3.%4", NumberFormatValues.Decimal),
            (4, "Heading5", "%1.%2.%3.%4.%5", NumberFormatValues.Decimal),
            (5, "Heading6", "%1.%2.%3.%4.%5.%6", NumberFormatValues.Decimal),
            (6, "-", "%1.%2.%3.%4.%5.%6.%7", NumberFormatValues.Decimal),
            (7, "-", "%1.%2.%3.%4.%5.%6.%7.%8", NumberFormatValues.Decimal),
            (8, "-", "%1.%2.%3.%4.%5.%6.%7.%8.%9", NumberFormatValues.Decimal),
        });
    }

    // ── well-formed / Word-openable: OpenXML schema validation ──────────────────────────────────────

    [Fact]
    public void SynthesizeDocument_RichModel_ProducesSchemaValidOpenXml()
    {
        var model = new ComposeContentModel
        {
            Blocks = new[]
            {
                Heading(1, "Agreement"),
                Heading(2, "Definitions"),
                Paragraph("The parties agree as follows."),
                ListItem(0, ordered: true, "First obligation"),
                ListItem(1, ordered: true, "A sub-obligation"),
                ListItem(0, ordered: false, "A bullet point"),
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Table,
                    Table = new ComposeTable
                    {
                        Rows = new[]
                        {
                            new ComposeTableRow { Cells = new[] { new ComposeTableCell { Blocks = new[] { Paragraph("x") } }, new ComposeTableCell { Blocks = new[] { Paragraph("y") } } } },
                        },
                    },
                },
            },
        };

        var bytes = Renderer().SynthesizeDocument(model, "author");

        using var doc = Open(bytes);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();
        var detail = string.Join("\n", errors.Select(e => $"{e.Path?.XPath}: {e.Description}"));

        // Plain xUnit assert: the message is emitted verbatim (FluentAssertions treats {namespace} in
        // validation descriptions as format placeholders and throws). A schema-valid doc opens in Word
        // without a repair prompt.
        Assert.True(errors.Count == 0, "the authored .docx must be schema-valid — offenders:\n" + detail);
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────────

    private static bool IsValidParaId(string id) =>
        id.Length == 8
        && uint.TryParse(id, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out var v)
        && v > 0 && v < 0x80000000u;

    private static string PartXml(byte[] docx, Func<WordprocessingDocument, OpenXmlPart> selector)
    {
        using var doc = Open(docx);
        using var reader = new StreamReader(selector(doc).GetStream(FileMode.Open, FileAccess.Read));
        return reader.ReadToEnd();
    }
}
