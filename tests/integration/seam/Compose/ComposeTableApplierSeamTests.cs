// R5 task 014 (G4 table applier, spec FR-04 + gap SDL-3) — seam proof for ComposeShadowPatchEngine's `table`
// op: FULL tracked table structure. Each kind (InsertRow/DeleteRow/InsertColumn/DeleteColumn/SetCellContent/
// SetTableProps) resolves the target table by the op's base paraId ONLY — a paragraph INSIDE the table, then a
// w:p -> w:tc -> w:tr -> w:tbl ancestry walk (I-7 — NO text-search anywhere in the write path) — and emits
// Word-VALID tracked markup: w:trPr/w:ins + w:trPr/w:del for rows, w:tcPr/w:cellIns + w:tcPr/w:cellDel for
// columns (+ w:tblGridChange), in-cell w:del/w:ins for cell content, w:tblPrChange for table props.
//
// WORD-VALIDITY (NFR-07): every patched package is run through the OpenXmlValidator (Office2019 schema) and
// asserted to carry ZERO validation errors — the strongest "opens in Word with real accept/reject redlines"
// proof available without Word. The table corpus doc is built IN-MEMORY here (a paraId-stamped 2x2 table between
// two body paragraphs) so the byte-diff corpus (tests/fixtures/compose-corpus — the 24/24 harness) is NOT
// perturbed. Untouched subtrees (the intro/outro body paragraphs + every other package part) are asserted
// byte-identical (NFR-01 / I-4).
//
// Banned-pattern clean: no Mock<HttpMessageHandler>, no DI-registration test, no ctor-null test.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeTableApplierSeamTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 29, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeShadowPatchEngine _engine = new();

    // Stable paraIds for the in-memory table corpus doc (8-hex ST_LongHexNumber).
    private const string IntroParaId = "0A000001";
    private const string OutroParaId = "0A000009";
    private const string R0C0 = "0AA00001";
    private const string R0C1 = "0AA00002";
    private const string R1C0 = "0AB00001";
    private const string R1C1 = "0AB00002";

    // ==========================================================================================
    // Row structure — InsertRow / DeleteRow
    // ==========================================================================================

    [Fact]
    public void InsertRow_After_EmitsTrackedInsertedRow_WordValid_UntouchedSubtreesByteIdentical()
    {
        var original = BuildTableDoc();
        var introBefore = ReadParagraphOuterXml(original, IntroParaId);
        var outroBefore = ReadParagraphOuterXml(original, OutroParaId);

        var log = OneOp(new TableOperation
        {
            ParaId = R0C0,
            Kind = ComposeTableOpKind.InsertRow,
            Row = 0,
            Position = ComposeParagraphPosition.After,
            NewParaIds = new[] { "0AC00001", "0AC00002" },
        });

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        AssertWordValid(patched);

        using (var doc = OpenRead(patched))
        {
            var table = FirstTable(doc);
            var rows = table.Elements<TableRow>().ToList();
            rows.Should().HaveCount(3, "a row was inserted");
            var inserted = rows[1];
            inserted.TableRowProperties?.GetFirstChild<Inserted>()
                .Should().NotBeNull("the new row must carry a tracked w:trPr/w:ins so Word offers accept/reject");
            // The new cells' paragraphs carry the minted ids + an inserted paragraph mark.
            var newCellParaIds = inserted.Elements<TableCell>()
                .Select(c => c.Elements<Paragraph>().First().ParagraphId?.Value).ToList();
            newCellParaIds.Should().Equal("0AC00001", "0AC00002");
        }

        // Only document.xml changed; the intro/outro paragraphs are byte-identical.
        ReadParagraphOuterXml(patched, IntroParaId).Should().Be(introBefore);
        ReadParagraphOuterXml(patched, OutroParaId).Should().Be(outroBefore);
        AssertOnlyDocumentXmlChanged(original, patched);
    }

    [Fact]
    public void DeleteRow_EmitsTrackedDeletedRow_StrikesCellContent_WordValid()
    {
        var original = BuildTableDoc();

        var log = OneOp(new TableOperation { ParaId = R0C0, Kind = ComposeTableOpKind.DeleteRow, Row = 1 });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        AssertWordValid(patched);

        using var doc = OpenRead(patched);
        var rows = FirstTable(doc).Elements<TableRow>().ToList();
        rows.Should().HaveCount(2, "a tracked delete keeps the row physically present until accept");
        rows[1].TableRowProperties?.GetFirstChild<Deleted>()
            .Should().NotBeNull("the deleted row must carry a tracked w:trPr/w:del");
        rows[1].Descendants<DeletedRun>().Should().NotBeEmpty("the deleted row's cell content must be struck (w:del)");
    }

    // ==========================================================================================
    // Column structure — InsertColumn / DeleteColumn (+ tblGrid / tblGridChange)
    // ==========================================================================================

    [Fact]
    public void InsertColumn_After_AddsTrackedCellPerRow_UpdatesGrid_RecordsGridChange_WordValid()
    {
        var original = BuildTableDoc();

        var log = OneOp(new TableOperation
        {
            ParaId = R0C0,
            Kind = ComposeTableOpKind.InsertColumn,
            Column = 0,
            Position = ComposeParagraphPosition.After,
            NewParaIds = new[] { "0AD00001", "0AD00002" }, // one per row (2 rows)
        });

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        AssertWordValid(patched);

        using var doc = OpenRead(patched);
        var table = FirstTable(doc);

        foreach (var row in table.Elements<TableRow>())
        {
            row.Elements<TableCell>().Should().HaveCount(3, "every row gains a cell for the new column");
            var insertedCell = row.Elements<TableCell>().ElementAt(1);
            insertedCell.TableCellProperties?.GetFirstChild<CellInsertion>()
                .Should().NotBeNull("each new column cell must carry a tracked w:tcPr/w:cellIns");
        }

        var grid = table.GetFirstChild<TableGrid>()!;
        grid.Elements<GridColumn>().Should().HaveCount(3, "the grid gains a w:gridCol for the new column");
        grid.GetFirstChild<TableGridChange>().Should().NotBeNull("a w:tblGridChange must record the prior grid");
        grid.GetFirstChild<TableGridChange>()!.GetFirstChild<PreviousTableGrid>()!.Elements<GridColumn>()
            .Should().HaveCount(2, "the tblGridChange snapshots the 2-column prior grid");
    }

    [Fact]
    public void DeleteColumn_MarksTargetCellPerRow_WordValid()
    {
        var original = BuildTableDoc();

        var log = OneOp(new TableOperation { ParaId = R0C0, Kind = ComposeTableOpKind.DeleteColumn, Column = 1 });
        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        AssertWordValid(patched);

        using var doc = OpenRead(patched);
        foreach (var row in FirstTable(doc).Elements<TableRow>())
        {
            var target = row.Elements<TableCell>().ElementAt(1);
            target.TableCellProperties?.GetFirstChild<CellDeletion>()
                .Should().NotBeNull("each row's target cell must carry a tracked w:tcPr/w:cellDel");
            target.Descendants<DeletedRun>().Should().NotBeEmpty("the deleted column's cell content must be struck");
        }
    }

    // ==========================================================================================
    // Cell content — SetCellContent (in-cell w:del old + w:ins new)
    // ==========================================================================================

    [Fact]
    public void SetCellContent_StrikesOldRuns_InsertsNewAsTracked_WordValid()
    {
        var original = BuildTableDoc();

        var log = OneOp(new TableOperation
        {
            ParaId = R0C0,
            Kind = ComposeTableOpKind.SetCellContent,
            Row = 1,
            Column = 1,
            Text = "Replacement",
        });

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        AssertWordValid(patched);

        using var doc = OpenRead(patched);
        var cell = FirstTable(doc).Elements<TableRow>().ElementAt(1).Elements<TableCell>().ElementAt(1);
        cell.Descendants<DeletedRun>().Should().NotBeEmpty("the prior cell text must be struck (w:del)");
        cell.Descendants<InsertedRun>().Should().NotBeEmpty("the new cell text must be tracked-inserted (w:ins)");
        cell.Descendants<InsertedRun>().First().InnerText.Should().Be("Replacement");
    }

    // ==========================================================================================
    // Table properties — SetTableProps (w:tblPrChange records prior props)
    // ==========================================================================================

    [Fact]
    public void SetTableProps_Alignment_EmitsTblPrChangeRecordingPriorProps_WordValid()
    {
        var original = BuildTableDoc();

        var log = OneOp(new TableOperation
        {
            ParaId = R0C0,
            Kind = ComposeTableOpKind.SetTableProps,
            TableProp = ComposeTableProp.Alignment,
            Value = "Center",
        });

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        AssertWordValid(patched);

        using var doc = OpenRead(patched);
        var tblPr = FirstTable(doc).GetFirstChild<TableProperties>()!;
        tblPr.GetFirstChild<TableJustification>()!.Val!.Value.Should().Be(TableRowAlignmentValues.Center,
            "the live alignment is the new value");
        tblPr.GetFirstChild<TablePropertiesChange>()
            .Should().NotBeNull("a w:tblPrChange must record the prior table properties so Reject restores them");
    }

    // ==========================================================================================
    // I-7 / validation guards
    // ==========================================================================================

    [Fact]
    public void TableOp_WhenParaIdNotInTable_ThrowsTableNotFound_NeverTextSearches()
    {
        var original = BuildTableDoc();
        // IntroParaId is a body paragraph OUTSIDE any table.
        var log = OneOp(new TableOperation { ParaId = IntroParaId, Kind = ComposeTableOpKind.DeleteRow, Row = 0 });

        _engine.Invoking(e => e.Apply(original, log, author: "Seam", timestamp: When))
            .Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.TableNotFound);
    }

    [Fact]
    public void TableOp_WhenRowIndexOutOfRange_ThrowsTableIndexOutOfRange()
    {
        var original = BuildTableDoc();
        var log = OneOp(new TableOperation { ParaId = R0C0, Kind = ComposeTableOpKind.DeleteRow, Row = 9 });

        _engine.Invoking(e => e.Apply(original, log, author: "Seam", timestamp: When))
            .Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.TableIndexOutOfRange);
    }

    [Fact]
    public void InsertRow_WithWrongNewParaIdCount_ThrowsTableIndexOutOfRange_NeverPartiallyApplies()
    {
        var original = BuildTableDoc();
        var log = OneOp(new TableOperation
        {
            ParaId = R0C0,
            Kind = ComposeTableOpKind.InsertRow,
            Row = 0,
            Position = ComposeParagraphPosition.After,
            NewParaIds = new[] { "0AC00001" }, // only 1 for a 2-column table
        });

        _engine.Invoking(e => e.Apply(original, log, author: "Seam", timestamp: When))
            .Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.TableIndexOutOfRange);
    }

    // -- helpers --------------------------------------------------------------------------------

    private static ComposeOperationLog OneOp(ComposeOperation op) => new() { Operations = new[] { op } };

    private static WordprocessingDocument OpenRead(byte[] bytes) =>
        WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);

    private static Table FirstTable(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document!.Body!.Elements<Table>().First();

    private static void AssertWordValid(byte[] bytes)
    {
        using var doc = OpenRead(bytes);
        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        var errors = validator.Validate(doc).ToList();
        errors.Should().BeEmpty(
            "the patched package must be schema-valid WordprocessingML (opens in Word with real accept/reject redlines) — "
            + string.Join(" | ", errors.Take(5).Select(e => $"{e.Id}@{e.Path?.XPath}: {e.Description}")));
    }

    private static void AssertOnlyDocumentXmlChanged(byte[] original, byte[] patched)
    {
        var comparison = ComposeOoxmlPackagePartComparer.Compare(original, patched, strictDocumentXmlByteIdentity: false);
        comparison.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"a table edit must leave every package part except document.xml byte-identical — {comparison.DescribeMismatches()}");
    }

    private static string ReadParagraphOuterXml(byte[] bytes, string paraId)
    {
        using var doc = OpenRead(bytes);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase))
            .OuterXml;
    }

    /// <summary>Builds a paraId-stamped 2x2 table between an intro and an outro body paragraph, saved to bytes.
    /// The table corpus doc is in-memory so the byte-diff corpus (24/24 harness) is not perturbed.</summary>
    private static byte[] BuildTableDoc()
    {
        using var buffer = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(buffer, DocumentFormat.OpenXml.WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            var body = new Body();

            body.AppendChild(BodyParagraph(IntroParaId, "Intro paragraph."));

            var table = new Table(
                new TableProperties(
                    new TableWidth { Type = TableWidthUnitValues.Auto, Width = "0" },
                    new TableBorders(
                        new TopBorder { Val = BorderValues.Single, Size = 4, Space = 0 },
                        new LeftBorder { Val = BorderValues.Single, Size = 4, Space = 0 },
                        new BottomBorder { Val = BorderValues.Single, Size = 4, Space = 0 },
                        new RightBorder { Val = BorderValues.Single, Size = 4, Space = 0 },
                        new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4, Space = 0 },
                        new InsideVerticalBorder { Val = BorderValues.Single, Size = 4, Space = 0 })),
                new TableGrid(new GridColumn { Width = "4675" }, new GridColumn { Width = "4675" }),
                TableRowOf(("R0C0", R0C0), ("R0C1", R0C1)),
                TableRowOf(("R1C0", R1C0), ("R1C1", R1C1)));
            body.AppendChild(table);

            body.AppendChild(BodyParagraph(OutroParaId, "Outro paragraph."));

            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return buffer.ToArray();
    }

    private static TableRow TableRowOf(params (string Text, string ParaId)[] cells)
    {
        var row = new TableRow();
        foreach (var (text, paraId) in cells)
        {
            row.AppendChild(new TableCell(
                new TableCellProperties(new TableCellWidth { Type = TableWidthUnitValues.Dxa, Width = "4675" }),
                CellParagraph(paraId, text)));
        }

        return row;
    }

    private static Paragraph BodyParagraph(string paraId, string text) => new Paragraph(
        new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))
    {
        ParagraphId = paraId,
    };

    private static Paragraph CellParagraph(string paraId, string text) => new Paragraph(
        new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }))
    {
        ParagraphId = paraId,
    };
}
