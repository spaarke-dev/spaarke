// FR-05 (spaarkeai-compose-r6 task 030) — unit tests for ComposeTemplatePartMergeEngine, the house-style
// chrome engine (editor body → firm/matter .dotx via DIRECT OOXML part-merge; NOT altChunk). These assert
// the task-030 acceptance criteria at the engine boundary:
//   1. chrome provenance — merged styles/sectPr/headers/footers come from the TEMPLATE (template wins on
//      style-id collision; template boilerplate body is dropped)
//   2. body provenance — the merged body content comes from the EDITOR render
//   3. numbering identity — the body's numId references are REMAPPED onto verbatim-cloned definitions,
//      never recomputed (lvlText/numFmt survive byte-for-byte); the template's own numbering is retained
//   4. relationship reconciliation — hyperlinks re-created on the merged main part; no dangling rIds
//   5. package validity — the merged output validates (the "opens clean in Word, no repair prompt" proxy)
// The through-the-wire provenance seam lands in task 033; these are the engine-boundary anchors.
//
// Pure engine (byte[] + byte[] in / byte[] out) — real OOXML assertions via the SDK, no transport mocks
// (ADR-038 B1), no DI/ctor tests (B3/B4). Co-located with the sibling Compose engine tests
// (ComposeDocumentRendererTests, ComposeShadowPatchEngineTests).

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

public sealed class ComposeTemplatePartMergeEngineTests
{
    private const string FirmHeaderText = "FIRM HEADER — Corteva & Helios LLP";
    private const string FirmFooterText = "FIRM FOOTER — Confidential";
    private const string TemplateBoilerplate = "TEMPLATE BOILERPLATE — replace me";
    private const string TemplateHeading1Color = "AA0000"; // distinctive: proves template wins the collision

    private static ComposeTemplatePartMergeEngine Engine() => new();

    // ── builders ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>A firm/matter .dotx: distinctive Heading1 (collision probe) + FirmBody style, its own
    /// numbering (abstract 0 / num 1), a header+footer wired through a LANDSCAPE sectPr, and boilerplate
    /// body content the merge must drop.</summary>
    private static byte[] BuildTemplateDotx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Template, autoSave: true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());
            body.AppendChild(new Paragraph(new Run(new Text(TemplateBoilerplate))));

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new Style(
                    new StyleName { Val = "Normal" })
                { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new Style(
                    new StyleName { Val = "heading 1" },
                    new StyleRunProperties(new Color { Val = TemplateHeading1Color }))
                { Type = StyleValues.Paragraph, StyleId = "Heading1" },
                new Style(
                    new StyleName { Val = "Firm Body" },
                    new StyleRunProperties(new RunFonts { Ascii = "Garamond" }))
                { Type = StyleValues.Paragraph, StyleId = "FirmBody" });

            var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering(
                new AbstractNum(
                    new Level(
                        new NumberingFormat { Val = NumberFormatValues.UpperRoman },
                        new LevelText { Val = "%1)" })
                    { LevelIndex = 0 })
                { AbstractNumberId = 0 },
                new NumberingInstance(new AbstractNumId { Val = 0 }) { NumberID = 1 });

            var headerPart = main.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(new Paragraph(new Run(new Text(FirmHeaderText))));
            var footerPart = main.AddNewPart<FooterPart>();
            footerPart.Footer = new Footer(new Paragraph(new Run(new Text(FirmFooterText))));

            body.AppendChild(new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(headerPart) },
                new FooterReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(footerPart) },
                new PageSize { Width = 15840, Height = 12240, Orient = PageOrientationValues.Landscape },
                new PageMargin { Top = 720, Right = 720, Bottom = 720, Left = 720, Header = 360, Footer = 360, Gutter = 0 }));

            main.Document.Save();
        }
        return stream.ToArray();
    }

    /// <summary>The editor side: a real ComposeDocumentRenderer render — heading (style-linked clause
    /// numbering), ordered list (numId reference), and an external hyperlink (main-part relationship).</summary>
    private static byte[] BuildRenderedBody() => new ComposeDocumentRenderer().SynthesizeDocument(
        new ComposeContentModel
        {
            Blocks = new[]
            {
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Heading,
                    Level = 1,
                    Runs = new[] { new ComposeInlineRun { Text = "Confidentiality Obligations" } },
                },
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.Paragraph,
                    Runs = new[]
                    {
                        new ComposeInlineRun { Text = "See " },
                        new ComposeInlineRun { Text = "the portal", Href = "https://spaarke.example.com/portal" },
                        new ComposeInlineRun { Text = " for details." },
                    },
                },
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.ListItem,
                    Ordered = true,
                    StartsNewList = true,
                    Runs = new[] { new ComposeInlineRun { Text = "First obligation" } },
                },
                new ComposeBlock
                {
                    Kind = ComposeBlockKind.ListItem,
                    Ordered = true,
                    Runs = new[] { new ComposeInlineRun { Text = "Second obligation" } },
                },
            },
        },
        "Jordan Avery");

    private static WordprocessingDocument Open(byte[] docx) =>
        WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);

    private static string BodyText(WordprocessingDocument doc) =>
        doc.MainDocumentPart!.Document!.Body!.InnerText;

    // ── criterion 1: chrome provenance (template wins) ───────────────────────────────────────────

    [Fact]
    public void Merge_WithFirmTemplate_AdoptsTemplateSectPrHeadersAndFooters()
    {
        var merged = Engine().Merge(BuildRenderedBody(), BuildTemplateDotx());

        using var doc = Open(merged);
        var main = doc.MainDocumentPart!;

        var sectPr = main.Document!.Body!.Elements<SectionProperties>().Last();
        sectPr.GetFirstChild<PageSize>()!.Orient!.Value.Should().Be(PageOrientationValues.Landscape,
            "the trailing sectPr (page chrome) must be the TEMPLATE's");
        sectPr.GetFirstChild<PageSize>()!.Width!.Value.Should().Be(15840u);

        var headerId = sectPr.GetFirstChild<HeaderReference>()!.Id!.Value!;
        ((HeaderPart)main.GetPartById(headerId)).Header!.InnerText.Should().Contain(FirmHeaderText,
            "the sectPr's header reference must resolve to the template's own header part");
        var footerId = sectPr.GetFirstChild<FooterReference>()!.Id!.Value!;
        ((FooterPart)main.GetPartById(footerId)).Footer!.InnerText.Should().Contain(FirmFooterText);
    }

    [Fact]
    public void Merge_OnStyleIdCollision_TemplateDefinitionWins()
    {
        var merged = Engine().Merge(BuildRenderedBody(), BuildTemplateDotx());

        using var doc = Open(merged);
        var styles = doc.MainDocumentPart!.StyleDefinitionsPart!.Styles!;

        var heading1Defs = styles.Elements<Style>().Where(s => s.StyleId?.Value == "Heading1").ToList();
        heading1Defs.Should().HaveCount(1, "a style id must stay unique after the merge");
        heading1Defs[0].StyleRunProperties?.GetFirstChild<Color>()?.Val?.Value
            .Should().Be(TemplateHeading1Color, "on id collision the TEMPLATE's definition wins — that IS house style");

        styles.Elements<Style>().Should().Contain(s => s.StyleId != null && s.StyleId.Value == "FirmBody",
            "template-only styles survive untouched");
    }

    [Fact]
    public void Merge_TemplateBoilerplateBody_IsDroppedAndEditorBodyKept()
    {
        var merged = Engine().Merge(BuildRenderedBody(), BuildTemplateDotx());

        using var doc = Open(merged);
        var text = BodyText(doc);
        text.Should().NotContain(TemplateBoilerplate, "the template contributes chrome, not content");
        text.Should().Contain("Confidentiality Obligations");
        text.Should().Contain("First obligation").And.Contain("Second obligation");
    }

    // ── criterion 3: numbering identity (remap, never recompute) ─────────────────────────────────

    [Fact]
    public void Merge_BodyNumbering_RemapsOntoVerbatimClonedDefinitionsAndKeepsTemplateNumbering()
    {
        var body = BuildRenderedBody();
        var merged = Engine().Merge(body, BuildTemplateDotx());

        using var sourceDoc = Open(body);
        using var doc = Open(merged);
        var numbering = doc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;

        // Template's own numbering intact (numId 1 → UpperRoman "%1)").
        var templateNum = numbering.Elements<NumberingInstance>().Single(n => n.NumberID?.Value == 1);
        var templateAbs = numbering.Elements<AbstractNum>()
            .Single(a => a.AbstractNumberId!.Value == templateNum.GetFirstChild<AbstractNumId>()!.Val!.Value);
        templateAbs.Elements<Level>().First().GetFirstChild<NumberingFormat>()!.Val!.Value
            .Should().Be(NumberFormatValues.UpperRoman, "the template's own numbering must survive the merge");

        // Every body numId reference resolves inside the merged part…
        var mergedNumIds = numbering.Elements<NumberingInstance>()
            .Select(n => n.NumberID!.Value).ToHashSet();
        var bodyRefs = doc.MainDocumentPart.Document!.Body!
            .Descendants<NumberingId>().Select(n => n.Val!.Value).Distinct().ToList();
        bodyRefs.Should().NotBeEmpty("the ordered list must still be list-numbered");
        bodyRefs.Should().OnlyContain(id => mergedNumIds.Contains(id), "no dangling numId after the remap");

        // …and the remapped definitions are the SOURCE's verbatim (lvlText/numFmt identical) — identity
        // preserved by cloning, never recomputed.
        var sourceListAbs = FindListAbstract(sourceDoc);
        var mergedListNum = numbering.Elements<NumberingInstance>()
            .Single(n => bodyRefs.Contains(n.NumberID!.Value) && n.NumberID!.Value != 1
                && IsReferencedByListParagraph(doc, n.NumberID!.Value));
        var mergedListAbs = numbering.Elements<AbstractNum>()
            .Single(a => a.AbstractNumberId!.Value == mergedListNum.GetFirstChild<AbstractNumId>()!.Val!.Value);

        var sourceLvl0 = sourceListAbs.Elements<Level>().First(l => l.LevelIndex?.Value == 0);
        var mergedLvl0 = mergedListAbs.Elements<Level>().First(l => l.LevelIndex?.Value == 0);
        mergedLvl0.GetFirstChild<NumberingFormat>()!.Val!.Value
            .Should().Be(sourceLvl0.GetFirstChild<NumberingFormat>()!.Val!.Value);
        mergedLvl0.GetFirstChild<LevelText>()!.Val!.Value
            .Should().Be(sourceLvl0.GetFirstChild<LevelText>()!.Val!.Value,
                "the abstractNum is cloned VERBATIM — computed numbers stay identical by construction");
    }

    private static AbstractNum FindListAbstract(WordprocessingDocument sourceDoc)
    {
        // The ordered-list paragraphs' numId → its abstract in the SOURCE package.
        var numbering = sourceDoc.MainDocumentPart!.NumberingDefinitionsPart!.Numbering!;
        var listNumId = sourceDoc.MainDocumentPart.Document!.Body!
            .Descendants<Paragraph>()
            .Where(p => p.InnerText.Contains("First obligation", StringComparison.Ordinal))
            .SelectMany(p => p.Descendants<NumberingId>())
            .Select(n => n.Val!.Value)
            .First();
        var num = numbering.Elements<NumberingInstance>().Single(n => n.NumberID?.Value == listNumId);
        return numbering.Elements<AbstractNum>()
            .Single(a => a.AbstractNumberId!.Value == num.GetFirstChild<AbstractNumId>()!.Val!.Value);
    }

    private static bool IsReferencedByListParagraph(WordprocessingDocument doc, int numId) =>
        doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Any(p => p.InnerText.Contains("obligation", StringComparison.Ordinal)
                && p.Descendants<NumberingId>().Any(n => n.Val?.Value == numId));

    // ── criterion 4: relationship reconciliation ─────────────────────────────────────────────────

    [Fact]
    public void Merge_BodyHyperlink_IsRecreatedOnMergedMainPartWithNoDanglingRIds()
    {
        var merged = Engine().Merge(BuildRenderedBody(), BuildTemplateDotx());

        using var doc = Open(merged);
        var main = doc.MainDocumentPart!;

        var hyperlink = main.Document!.Body!.Descendants<Hyperlink>().Single();
        var rel = main.HyperlinkRelationships.SingleOrDefault(h => h.Id == hyperlink.Id?.Value);
        rel.Should().NotBeNull("the grafted hyperlink's r:id must resolve on the MERGED main part");
        rel!.Uri.OriginalString.Should().Be("https://spaarke.example.com/portal");

        // No r:-namespace reference anywhere in the body may dangle (Word repair-prompt proxy).
        var known = main.Parts.Select(p => p.RelationshipId)
            .Concat(main.HyperlinkRelationships.Select(h => h.Id))
            .Concat(main.ExternalRelationships.Select(e => e.Id))
            .ToHashSet(StringComparer.Ordinal);
        var referenced = main.Document.Body.Descendants()
            .SelectMany(d => d.GetAttributes())
            .Where(a => a.NamespaceUri == "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
            .Select(a => a.Value)
            .OfType<string>();
        referenced.Should().OnlyContain(id => known.Contains(id));
    }

    // ── criterion 4/1: package validity + document (not template) output ─────────────────────────

    [Fact]
    public void Merge_Output_IsAValidDocumentPackage()
    {
        var merged = Engine().Merge(BuildRenderedBody(), BuildTemplateDotx());

        using var doc = Open(merged);
        doc.DocumentType.Should().Be(WordprocessingDocumentType.Document,
            "the merged output is a document, not a template");

        var errors = new OpenXmlValidator(FileFormatVersions.Office2019)
            .Validate(doc)
            .ToList();
        errors.Should().BeEmpty("the merged package must open clean in Word (no repair prompt)");
    }

    // ── degradation posture: loud, never silent ──────────────────────────────────────────────────

    [Fact]
    public void Merge_TemplateWithoutSectPr_KeepsBodyPageSetupAndWarnsLoudly()
    {
        byte[] chromelessTemplate;
        using (var stream = new MemoryStream())
        {
            using (var bare = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Template, autoSave: true))
            {
                var main = bare.AddMainDocumentPart();
                main.Document = new Document(new Body());
                main.Document.Save();
            }
            chromelessTemplate = stream.ToArray();
        }

        var warnings = new List<ComposeProjectionWarning>();
        var merged = Engine().Merge(BuildRenderedBody(), chromelessTemplate, warnings);

        warnings.Should().Contain(w => w.Code == "template-merge-missing-sectpr");
        warnings.Should().Contain(w => w.Code == "template-merge-template-has-no-styles");
        using var doc = Open(merged);
        doc.MainDocumentPart!.Document!.Body!.Elements<SectionProperties>().Should().NotBeEmpty(
            "the document's own page setup is adopted so the output stays a valid single-section docx");
        BodyText(doc).Should().Contain("Confidentiality Obligations");
    }
}
