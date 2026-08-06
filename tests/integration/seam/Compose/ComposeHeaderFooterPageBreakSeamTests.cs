// Task 023 (spaarkeai-compose-r6, FR-04) — HEADERS/FOOTERS + PAGE BREAKS through the canonical model.
//
// Two mechanisms, proven separately:
//   1. HEADERS/FOOTERS ride the CARRIER, not the model: header/footer PARTS are preserved wholesale by
//      RenderIntoCarrier (byte-identical) and the trailing body sectPr — which carries the
//      w:headerReference/w:footerReference relationship ids — is detached + re-attached around the body
//      swap, so the references still RESOLVE to their parts after the round-trip. (Template-chrome
//      part-merge for firm .dotx is Phase 3; editing header CONTENT is not an editor capability.)
//   2. PAGE BREAKS are MODEL data (task-023 widening): a manual w:br type="page" projects to a dedicated
//      ComposeInlineRun.IsPageBreak at its exact inline position (splitting the surrounding text — no
//      longer the line-break-flattened SPACE it degraded to before this task), and w:pPr/pageBreakBefore
//      projects to ComposeBlock.PageBreakBefore; the renderer authors both back out.
// INTERIOR section breaks (pPr-nested w:sectPr) are NOT model data: they flatten LOUDLY via the new
// counted `section-break-flattened` warning (content joins the final section) — full multi-section
// modeling is a follow-up; the corpus has no multi-section doc (manifest row 8 = placeholder).
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

public sealed class ComposeHeaderFooterPageBreakSeamTests
{
    private readonly ComposeDocxProjectionBuilder _builder = new();
    private readonly ComposeDocumentRenderer _renderer = new();

    // ── SDK-authored source: header + footer parts, a mid-paragraph page break, a pageBreakBefore ──

    private static byte[] BuildHeaderFooterPageBreakSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();

            var headerPart = main.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(new Paragraph(new Run(new Text("CONFIDENTIAL — Draft"))));
            var footerPart = main.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(new Paragraph(new Run(new Text("Attorney Work Product"))));

            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Opening prose."))),
                new Paragraph(
                    new Run(new Text("Text before the break") { Space = SpaceProcessingModeValues.Preserve }),
                    new Run(new Break { Type = BreakValues.Page }),
                    new Run(new Text("text after the break") { Space = SpaceProcessingModeValues.Preserve })),
                new Paragraph(
                    new ParagraphProperties(new PageBreakBefore()),
                    new Run(new Text("Starts on a fresh page."))),
                new SectionProperties(
                    new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(headerPart) },
                    new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footerPart) },
                    new PageSize { Width = 12240, Height = 15840 },
                    new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 })));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static byte[] BuildInteriorSectionSource()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(
                new Paragraph(new Run(new Text("Section one content."))),
                new Paragraph(
                    // The INTERIOR section break: this paragraph ends section 1.
                    new ParagraphProperties(new SectionProperties(new PageSize { Width = 12240, Height = 15840 })),
                    new Run(new Text("Last paragraph of section one."))),
                new Paragraph(new Run(new Text("Section two content."))),
                new SectionProperties(new PageSize { Width = 15840, Height = 12240 }))); // final: landscape
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static Dictionary<string, int> ValidationErrorCounts(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(doc)
            .GroupBy(e => e.Description)
            .ToDictionary(g => g.Key, g => g.Count());
    }

    private static byte[] PartBytes(OpenXmlPart part)
    {
        using var s = part.GetStream();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Projection captures page breaks as model data — at the exact inline position, warning-free.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildContentModel_OnPageBreakSource_CapturesBreaksAtExactPositionsWithoutFlattenWarnings()
    {
        var projection = _builder.BuildContentModel(BuildHeaderFooterPageBreakSource());

        projection.Status.Should().Be(ComposeProjectionStatus.Success,
            "manual page breaks are MODEL data now — nothing in this source should warn " +
            $"(warnings: {string.Join(", ", projection.Warnings.Select(w => $"{w.Code}×{w.Count}"))})");
        projection.Warnings.Should().NotContain(w => w.Code == "line-break-flattened",
            "the page-break-as-space degradation is retired for manual page breaks");

        var breakParagraph = projection.Model.Blocks[1];
        breakParagraph.Runs.Should().HaveCount(3, "text-before / the break / text-after split at the exact position");
        breakParagraph.Runs[0].Text.Should().Be("Text before the break");
        breakParagraph.Runs[1].IsPageBreak.Should().BeTrue();
        breakParagraph.Runs[1].Text.Should().BeEmpty();
        breakParagraph.Runs[2].Text.Should().Be("text after the break");

        projection.Model.Blocks[2].PageBreakBefore.Should().BeTrue("w:pPr/pageBreakBefore is model data");
        projection.Model.Blocks[0].PageBreakBefore.Should().BeFalse();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Carrier round-trip: header/footer PARTS byte-identical, references RESOLVE, breaks re-authored.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void RenderIntoCarrier_HeaderFooterPageBreakRoundTrip_PreservesPartsReferencesAndBreaks()
    {
        var source = BuildHeaderFooterPageBreakSource();
        var projection = _builder.BuildContentModel(source);
        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");

        using var sourceDoc = WordprocessingDocument.Open(new MemoryStream(source, writable: false), isEditable: false);
        using var renderedDoc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var renderedMain = renderedDoc.MainDocumentPart!;

        // Header/footer PARTS byte-identical (the carrier preservation mechanism).
        PartBytes(renderedMain.HeaderParts.Single()).AsSpan()
            .SequenceEqual(PartBytes(sourceDoc.MainDocumentPart!.HeaderParts.Single())).Should().BeTrue(
                "the header part must survive the body swap byte-identically");
        PartBytes(renderedMain.FooterParts.Single()).AsSpan()
            .SequenceEqual(PartBytes(sourceDoc.MainDocumentPart!.FooterParts.Single())).Should().BeTrue(
                "the footer part must survive the body swap byte-identically");

        // The trailing sectPr's references still RESOLVE to those parts (relationship integrity).
        var sectPr = renderedMain.Document!.Body!.Elements<SectionProperties>().Single();
        var headerRef = sectPr.Elements<HeaderReference>().Single();
        var footerRef = sectPr.Elements<FooterReference>().Single();
        renderedMain.GetPartById(headerRef.Id!.Value!).Should().BeOfType<HeaderPart>(
            "w:headerReference must still point at the preserved header part");
        renderedMain.GetPartById(footerRef.Id!.Value!).Should().BeOfType<FooterPart>(
            "w:footerReference must still point at the preserved footer part");

        // Page breaks re-authored from the model.
        var body = renderedMain.Document.Body!;
        body.Descendants<Break>().Count(b => b.Type is not null && b.Type.Value == BreakValues.Page)
            .Should().Be(1, "the mid-paragraph manual page break must be re-authored as w:br type=page");
        var breakParagraph = body.Elements<Paragraph>().ElementAt(1);
        breakParagraph.Descendants<Break>().Should().ContainSingle("the break stays inside ITS paragraph");
        breakParagraph.InnerText.Should().Be("Text before the breaktext after the break",
            "the split runs carry the exact surrounding text");
        body.Elements<Paragraph>().ElementAt(2).ParagraphProperties!.PageBreakBefore.Should().NotBeNull(
            "w:pageBreakBefore must be re-authored");

        // No new schema errors (multiset).
        var sourceErrors = ValidationErrorCounts(source);
        ValidationErrorCounts(rendered)
            .Where(kv => kv.Value > (sourceErrors.TryGetValue(kv.Key, out var had) ? had : 0))
            .Should().BeEmpty("the rendered package must stay Word-valid");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Model fixed point: the rendered output re-projects to the SAME break facts.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void CarrierRoundTrip_PageBreakFacts_AreAFixedPoint()
    {
        var source = BuildHeaderFooterPageBreakSource();
        var projection = _builder.BuildContentModel(source);
        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");
        var reprojection = _builder.BuildContentModel(rendered);

        static IEnumerable<string> BreakFacts(ComposeContentModel m) =>
            m.Blocks.Select(b =>
                (b.PageBreakBefore ? "PBB:" : string.Empty) +
                string.Join("|", b.Runs.Select(r => r.IsPageBreak ? "<PAGEBREAK>" : r.Text)));

        BreakFacts(reprojection.Model).Should().Equal(BreakFacts(projection.Model),
            "page-break positions + pageBreakBefore flags must survive model→docx→model");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Interior section breaks flatten LOUDLY — never silently, never a hard-fail.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void BuildContentModel_InteriorSectionBreak_CountsSectionBreakFlattened_AndStillRoundTrips()
    {
        var source = BuildInteriorSectionSource();
        var projection = _builder.BuildContentModel(source);

        projection.Status.Should().Be(ComposeProjectionStatus.Partial, "an interior section break degrades loudly");
        projection.Warnings.Should().ContainSingle(w => w.Code == "section-break-flattened")
            .Which.Count.Should().Be(1, "one pPr-nested sectPr in the source");
        projection.Model.Blocks.Should().HaveCount(3, "all three paragraphs' prose survives");

        // Round-trip still succeeds (no hard-fail; the content joins the final section).
        var rendered = _renderer.RenderIntoCarrier(source, projection.Model, author: "seam-test");
        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var body = doc.MainDocumentPart!.Document!.Body!;
        body.Descendants<Paragraph>().SelectMany(p => p.Descendants<Text>()).Select(t => t.Text)
            .Should().Contain("Section two content.");
        body.Elements<SectionProperties>().Single().GetFirstChild<PageSize>()!.Width!.Value
            .Should().Be(15840u, "the FINAL (trailing) section's landscape setup is the one preserved");
        body.Descendants<Paragraph>().Should().NotContain(
            p => p.ParagraphProperties != null && p.ParagraphProperties.SectionProperties != null,
            "the interior sectPr flattened (its loss was counted at projection time)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. Corpus-wide: header/footer references resolve + page-break counts stable through the round-trip.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void EveryCorpusDoc_HeaderFooterReferencesResolve_AndPageBreakCountIsStable(string corpusDocPath)
    {
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var docName = Path.GetFileName(corpusDocPath);

        var projection = _builder.BuildContentModel(original);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed, $"'{docName}' must project");
        var modelPageBreaks = projection.Model.Blocks
            .SelectMany(FlattenBlocks)
            .SelectMany(b => b.Runs)
            .Count(r => r.IsPageBreak);

        var rendered = _renderer.RenderIntoCarrier(original, projection.Model, author: "seam-test");
        using var doc = WordprocessingDocument.Open(new MemoryStream(rendered, writable: false), isEditable: false);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        // Every header/footer reference in the preserved trailing sectPr resolves to a real part.
        foreach (var sectPr in body.Elements<SectionProperties>())
        {
            foreach (var reference in sectPr.ChildElements.OfType<HeaderReference>())
            {
                main.GetPartById(reference.Id!.Value!).Should().BeOfType<HeaderPart>(
                    $"'{docName}' header reference {reference.Id!.Value} must resolve after the round-trip");
            }
            foreach (var reference in sectPr.ChildElements.OfType<FooterReference>())
            {
                main.GetPartById(reference.Id!.Value!).Should().BeOfType<FooterPart>(
                    $"'{docName}' footer reference {reference.Id!.Value} must resolve after the round-trip");
            }
        }

        // Every model page break is authored back out (and none are invented).
        body.Descendants<Break>().Count(b => b.Type is not null && b.Type.Value == BreakValues.Page)
            .Should().Be(modelPageBreaks,
                $"'{docName}' rendered page-break count must equal the model's IsPageBreak count");
    }

    private static IEnumerable<ComposeBlock> FlattenBlocks(ComposeBlock block)
    {
        yield return block;
        if (block.Table is not null)
        {
            foreach (var nested in block.Table.Rows
                .SelectMany(r => r.Cells)
                .SelectMany(c => c.Blocks)
                .SelectMany(FlattenBlocks))
            {
                yield return nested;
            }
        }
    }
}
