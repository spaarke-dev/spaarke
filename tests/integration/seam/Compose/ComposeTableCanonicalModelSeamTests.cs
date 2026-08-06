// Task 022 (spaarkeai-compose-r6, FR-04) — TABLES THROUGH THE CANONICAL MODEL: the table structural-fact
// round-trip slice. The model (ComposeTable/Row/Cell, widened by task 022) carries the CLOSED structural
// set — gridSpan / vMerge / explicit grid widths / table+cell widths / borders (tri-state: null = editor
// chrome, empty = borderless) / tblStyle identity / tblLook / repeat-header rows / cell vAlign — and the
// renderer reproduces exactly that set on the render-on-save path. Visual chrome OUTSIDE the set (shading,
// floating tblpPr, row heights, cell margins, per-cell borders, jc…) flattens LOUDLY via the counted
// `table-formatting-flattened` projection warning (F-1, never silent); widening is 026/follow-up scope.
//
// The R5 tracked-table work (ComposeShadowPatchEngine.ApplyTableOperation — tracked row/column/cell EDIT
// OPERATIONS) is REUSED AS-IS on the transitional op-log path; render-on-save needs the model to CARRY
// table structure, which is what this task adds. Tracked-change table DATA in the model is task 025.
//
// Word-validity oracle: OpenXmlValidator — the rendered package must introduce NO new schema-validation
// errors vs the source (the POML's "Word-valid markup" acceptance made mechanical).
//
// NEGATIVE (ADR-038): NO Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeTableCanonicalModelSeamTests
{
    private readonly ComposeDocxProjectionBuilder _builder = new();
    private readonly ComposeDocumentRenderer _renderer = new();

    // ── SDK-authored rich-table source (self-contained; no binary fixture needed) ─────────────────

    private static Paragraph Para(string text) => new(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static TableCell Cell(string text, TableCellProperties? tcPr = null)
    {
        var cell = new TableCell();
        if (tcPr is not null)
        {
            cell.AppendChild(tcPr);
        }
        cell.AppendChild(Para(text));
        return cell;
    }

    /// <summary>Table A — the legal signature-block shape: BORDERLESS layout table, explicit grid widths,
    /// a full-width gridSpan row. The round-trip must NOT grow borders on it.</summary>
    private static Table BorderlessSignatureTable() => new(
        new TableProperties(),
        new TableGrid(
            new GridColumn { Width = "2880" },
            new GridColumn { Width = "2880" },
            new GridColumn { Width = "2880" }),
        new TableRow(Cell("Name"), Cell("Signature"), Cell("Date")),
        new TableRow(Cell("By signing below, the parties agree.", new TableCellProperties(new GridSpan { Val = 3 }))));

    /// <summary>Table B — styled + merged: tblStyle identity, pct width, PARTIAL borders (top+bottom only),
    /// tblLook, a repeat-header row, a vertical merge (restart/continue), a dxa cell width, a
    /// bottom-aligned cell.</summary>
    private static Table StyledMergedTable() => new(
        new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 8, Color = "FF0000" },
                new BottomBorder { Val = BorderValues.Double, Size = 4, Color = "auto" }),
            new TableLook { Val = "04A0" }),
        new TableGrid(new GridColumn { Width = "4320" }, new GridColumn { Width = "4320" }),
        new TableRow(
            new TableRowProperties(new TableHeader()),
            Cell("Column A", new TableCellProperties(new TableCellWidth { Width = "4320", Type = TableWidthUnitValues.Dxa })),
            Cell("Column B")),
        new TableRow(
            Cell("Merged tall cell", new TableCellProperties(new VerticalMerge { Val = MergedCellValues.Restart })),
            Cell("Bottom aligned", new TableCellProperties(new TableCellVerticalAlignment { Val = TableVerticalAlignmentValues.Bottom }))),
        new TableRow(
            new TableCell(new TableCellProperties(new VerticalMerge()), new Paragraph()),
            Cell("Regular")));

    private static byte[] BuildRichTableSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();

            // A minimal styles part carrying the TableGrid TABLE style, so the carrier round-trip keeps a
            // real (non-dangling) tblStyle reference and the renderer does not author its own catalog.
            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(new Style(new StyleName { Val = "Table Grid" })
            {
                Type = StyleValues.Table,
                StyleId = "TableGrid",
            });

            main.Document = new Document(new Body(
                Para("Intro prose."),
                BorderlessSignatureTable(),
                Para("Between the tables."),
                StyledMergedTable(),
                new SectionProperties(
                    new PageSize { Width = 12240, Height = 15840 },
                    new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static List<Table> BodyTables(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Elements<Table>().Select(t => (Table)t.CloneNode(true)).ToList();
    }

    private static IReadOnlyList<string> ValidationErrors(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(doc)
            .Select(e => e.Description)
            .ToList();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Projection captures the closed structural set.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildContentModel_OnRichTableSource_CapturesStructuralFacts()
    {
        var projection = _builder.BuildContentModel(BuildRichTableSource());
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        projection.Warnings.Should().NotContain(w => w.Code == "table-formatting-flattened",
            "the authored source uses only closed-set constructs — nothing should flatten");

        var tables = projection.Model.Blocks.Where(b => b.Kind == ComposeBlockKind.Table).Select(b => b.Table!).ToList();
        tables.Should().HaveCount(2);

        var borderless = tables[0];
        borderless.Borders.Should().NotBeNull("a projected table is ALWAYS source-faithful (tri-state contract)");
        borderless.Borders!.Top.Should().BeNull("the signature table is borderless");
        borderless.Borders.InsideHorizontal.Should().BeNull();
        borderless.StyleId.Should().BeNull();
        borderless.GridColumnWidthsTwips.Should().Equal("2880", "2880", "2880");
        borderless.Rows[1].Cells.Should().ContainSingle().Which.GridSpan.Should().Be(3);

        var styled = tables[1];
        styled.StyleId.Should().Be("TableGrid");
        styled.Width.Should().BeEquivalentTo(new ComposeTableWidth { Type = "pct", Value = "5000" });
        styled.LookHex.Should().Be("04A0");
        styled.Borders!.Top.Should().NotBeNull();
        styled.Borders.Top!.Val.Should().Be("single", "the model carries the raw XML token, not the SDK struct name");
        styled.Borders.Top.Size.Should().Be(8u);
        styled.Borders.Top.Color.Should().Be("FF0000");
        styled.Borders.Bottom!.Val.Should().Be("double");
        styled.Borders.Left.Should().BeNull("only top+bottom were authored — per-edge nullability");
        styled.Rows[0].RepeatAsHeaderRow.Should().BeTrue();
        styled.Rows[0].Cells[0].Width.Should().BeEquivalentTo(new ComposeTableWidth { Type = "dxa", Value = "4320" });
        styled.Rows[1].Cells[0].VMerge.Should().Be(ComposeVerticalMerge.Restart);
        styled.Rows[2].Cells[0].VMerge.Should().Be(ComposeVerticalMerge.Continue);
        styled.Rows[1].Cells[1].VerticalAlignment.Should().Be("bottom");
        styled.Rows[0].Cells[1].VerticalAlignment.Should().Be("top",
            "a projected cell without explicit vAlign carries Word's default 'top' — never the editor's center chrome");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. The rendered OOXML reproduces the structural set (carrier round-trip).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenderIntoCarrier_RichTableRoundTrip_ReproducesStructureInRenderedOoxml()
    {
        var source = BuildRichTableSource();
        var projection = _builder.BuildContentModel(source);
        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");

        var tables = BodyTables(rendered);
        tables.Should().HaveCount(2);

        var borderless = tables[0];
        borderless.GetFirstChild<TableProperties>()!.GetFirstChild<TableBorders>().Should().BeNull(
            "a borderless signature table must NOT grow the renderer's border chrome on save");
        borderless.GetFirstChild<TableGrid>()!.Elements<GridColumn>().Select(g => g.Width?.Value)
            .Should().Equal("2880", "2880", "2880");
        borderless.Elements<TableRow>().ElementAt(1).Elements<TableCell>().Single()
            .TableCellProperties!.GetFirstChild<GridSpan>()!.Val!.Value.Should().Be(3);

        var styled = tables[1];
        var tblPr = styled.GetFirstChild<TableProperties>()!;
        tblPr.TableStyle!.Val!.Value.Should().Be("TableGrid");
        tblPr.GetFirstChild<TableWidth>()!.Width!.Value.Should().Be("5000");
        var borders = tblPr.GetFirstChild<TableBorders>()!;
        borders.TopBorder!.Size!.Value.Should().Be(8u);
        borders.TopBorder.Color!.Value.Should().Be("FF0000");
        borders.LeftBorder.Should().BeNull("un-authored edges stay un-emitted");
        var rows = styled.Elements<TableRow>().ToList();
        rows[0].TableRowProperties!.GetFirstChild<TableHeader>().Should().NotBeNull("tblHeader must survive");
        var restartCell = rows[1].Elements<TableCell>().First().TableCellProperties!;
        restartCell.GetFirstChild<VerticalMerge>()!.Val!.Value.Should().Be(MergedCellValues.Restart);
        rows[2].Elements<TableCell>().First().TableCellProperties!
            .GetFirstChild<VerticalMerge>().Should().NotBeNull("the continue cell keeps its vMerge");
        rows[1].Elements<TableCell>().ElementAt(1).TableCellProperties!
            .GetFirstChild<TableCellVerticalAlignment>()!.Val!.Value.Should().Be(TableVerticalAlignmentValues.Bottom);
        rows[0].Elements<TableCell>().First().TableCellProperties!
            .GetFirstChild<TableCellWidth>()!.Width!.Value.Should().Be("4320");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Word-validity: the rendered package introduces NO new schema-validation errors.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenderIntoCarrier_RichTableRoundTrip_IntroducesNoNewSchemaValidationErrors()
    {
        var source = BuildRichTableSource();
        var sourceErrors = ValidationErrors(source);

        var rendered = _renderer.RenderIntoCarrier(source, _builder.BuildContentModel(source).Model, author: "seam-test");
        var newErrors = ValidationErrors(rendered).Except(sourceErrors).ToList();

        newErrors.Should().BeEmpty("the render-on-save path must emit Word-valid table markup");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Structural fixed point across the WHOLE corpus: every table's shape facts survive
    //    model→docx→model (only corpus docs that actually contain body tables participate).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    private static IEnumerable<(int RowCount, string CellShape)> TableShapeFacts(ComposeContentModel model) =>
        model.Blocks.Where(b => b.Kind == ComposeBlockKind.Table).Select(b => (
            b.Table!.Rows.Count,
            string.Join("|", b.Table.Rows.Select(r =>
                string.Join(",", r.Cells.Select(c => $"{c.GridSpan}:{c.VMerge}"))))));

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void EveryCorpusDocWithTables_TableShapeSurvivesCarrierRoundTrip(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var projection = _builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must project");
        var sourceFacts = TableShapeFacts(projection.Model).ToList();
        if (sourceFacts.Count == 0)
        {
            return; // no body tables in this doc — nothing to prove here
        }

        var rendered = _renderer.RenderIntoCarrier(original, projection.Model, author: "seam-test");
        var reprojection = _builder.BuildContentModel(rendered);
        reprojection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' rendered output must re-project");

        TableShapeFacts(reprojection.Model).Should().Equal(sourceFacts,
            $"'{docName}' — every table's row/cell/span/merge shape must survive the model→docx→model cycle");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. Loud degradation: chrome OUTSIDE the closed set counts table-formatting-flattened.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildContentModel_TableWithOutOfSetChrome_CountsTableFormattingFlattened()
    {
        byte[] source;
        using (var stream = new MemoryStream())
        {
            using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
            {
                var main = doc.AddMainDocumentPart();
                main.Document = new Document(new Body(
                    new Table(
                        new TableProperties(
                            // CT_TblPr schema order: tblpPr precedes tblW/jc; shd follows tblBorders.
                            new TablePositionProperties { TablePositionY = 720, VerticalAnchor = VerticalAnchorValues.Page },
                            new TableJustification { Val = TableRowAlignmentValues.Center },
                            new Shading { Val = ShadingPatternValues.Clear, Fill = "DDDDDD" }),
                        new TableGrid(new GridColumn()),
                        new TableRow(
                            new TableRowProperties(new TableRowHeight { Val = 720 }),
                            Cell("Shaded floating table",
                                new TableCellProperties(new Shading { Val = ShadingPatternValues.Clear, Fill = "EEEEEE" })))),
                    new SectionProperties(new PageSize { Width = 12240, Height = 15840 })));
                main.Document.Save();
            }
            source = stream.ToArray();
        }

        var projection = _builder.BuildContentModel(source);

        projection.Status.Should().Be(ComposeProjectionStatus.Partial, "out-of-set chrome must degrade LOUDLY");
        projection.Warnings.Should().ContainSingle(w => w.Code == "table-formatting-flattened")
            .Which.Count.Should().Be(5,
                "tblpPr (floating) + jc + table shd + trHeight + cell shd each count once — never a silent drop");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 6. Born-in-editor stability: a client-authored table (no structural facts) keeps the EXACT legacy
    //    chrome — the live client's rendered look must not change under this task.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void SynthesizeDocument_ClientAuthoredTable_KeepsLegacyEditorChrome()
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
                                    new ComposeTableCell { Blocks = new[] { new ComposeBlock { Kind = ComposeBlockKind.Paragraph, Runs = new[] { new ComposeInlineRun { Text = "cell" } } } } },
                                },
                            },
                        },
                    },
                },
            },
        };

        var rendered = _renderer.SynthesizeDocument(model, author: "seam-test");
        var table = BodyTables(rendered).Single();
        var tblPr = table.GetFirstChild<TableProperties>()!;

        tblPr.GetFirstChild<TableWidth>()!.Width!.Value.Should().Be("5000");
        tblPr.GetFirstChild<TableBorders>()!.Elements().Should().HaveCount(6, "the legacy single-border chrome");
        tblPr.GetFirstChild<TableLook>()!.Val!.Value.Should().Be("04A0");
        table.Descendants<TableCellVerticalAlignment>().Single().Val!.Value
            .Should().Be(TableVerticalAlignmentValues.Center, "the legacy center vAlign chrome");
    }
}
