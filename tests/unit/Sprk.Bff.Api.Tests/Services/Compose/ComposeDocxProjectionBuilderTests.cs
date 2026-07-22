// Phase-1 mammoth removal (design notes/design-server-side-docx-html-conversion.md) — unit tests for
// ComposeDocxProjectionBuilder: the single-walk server-side DOCX→editor projection that replaced the
// client mammoth convert + position-based paraId stamping (the two-engine drift that caused the recurring
// "w14:paraId matches no paragraph in the retained original" save failures).
//
// Each test names a concrete production behavior that breaks if deleted:
//   - THE single-walk invariant: the emitted data-paraid sequence == the ParaIdMap sequence, one-to-one,
//     order-identical, INCLUDING inside recursive containers (tables, nested tables, content controls) — F-01.
//   - existing ids verbatim / gaps minted; empty paragraphs preserved (the ignoreEmptyParagraphs root cause).
//   - revision flattening (F-02): w:ins/w:del text present + wrappers stripped; a fully-deleted paragraph
//     still emitted with its data-paraid so the count/id sequence never breaks.
//   - fail-closed (F-04/GPT §11): malformed bytes → Failed/CanEdit=false, NEVER a throw.
//   - runtime guard (F-03): a paragraph enumerated but not rendered (text box) degrades to Partial + warning.
//   - hyperlink protocol allowlist (GPT §13): javascript: is neutralized.
//
// Banned-pattern compliance (tests/CLAUDE.md): pure domain logic over real in-memory .docx fixtures (Open
// XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4), no getter/mirror tests (B6/B16).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Office2010.Word.DrawingShape;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeDocxProjectionBuilderTests
{
    // ── fixture builders ───────────────────────────────────────────────────────────────────────────
    private static Paragraph Para(string? paraId, string text)
    {
        var p = new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));
        if (paraId is not null) p.ParagraphId = new HexBinaryValue(paraId);
        return p;
    }

    private static Paragraph Heading(string? paraId, int level, string text)
    {
        var p = Para(paraId, text);
        p.ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = $"Heading{level}" });
        return p;
    }

    private static Paragraph EmptyPara(string? paraId)
    {
        var p = new Paragraph();
        if (paraId is not null) p.ParagraphId = new HexBinaryValue(paraId);
        return p;
    }

    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var child in bodyChildren) body.Append(child);
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static readonly Regex ParaIdAttr = new("data-paraid=\"([0-9A-Fa-f]+)\"", RegexOptions.Compiled);

    private static List<string> EmittedParaIds(string html) =>
        ParaIdAttr.Matches(html).Select(m => m.Groups[1].Value).ToList();

    // ── THE single-walk invariant ────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_MixedDocumentWithTablesAndContentControl_EmitsDataParaIdSequenceIdenticalToParaIdMap()
    {
        // Body: top para, heading, a table whose second cell holds a nested table + trailing para,
        // a content-control-wrapped para, then a closing para — the recursive containers F-01 flags.
        var docx = BuildDocx(
            Para("1A2B3C4D", "intro"),
            Heading(null, 2, "Clause"),
            new Table(new TableRow(
                new TableCell(Para(null, "cell A")),
                new TableCell(
                    new Table(new TableRow(new TableCell(Para(null, "nested cell")))),
                    Para(null, "cell B trailing")))),
            new SdtBlock(new SdtContentBlock(Para(null, "inside content control"))),
            Para(null, "closing"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        projection.CanEdit.Should().BeTrue();
        // The proof: the HTML's data-paraid order EXACTLY equals the ParaIdMap order — one engine, no ordinal join.
        EmittedParaIds(projection.Html)
            .Should().Equal(projection.ParaIdMap.Select(e => e.ParaId),
                "the emitted block ids and the paraId map come from the same single traversal (F-01)");
        projection.ParaIdMap.Select(e => e.ParaId).Should().OnlyHaveUniqueItems();
        projection.ParaIdMap.Should().HaveCount(7); // 5 top-level (incl. cell paragraphs) + nested cell counted
    }

    [Fact]
    public void Build_ExistingParaId_IsCarriedVerbatimOntoTheHtmlBlock()
    {
        var docx = BuildDocx(Para("00AB12CD", "kept"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Html.Should().Contain("data-paraid=\"00AB12CD\"");
        projection.ParaIdMap.Single().IsMinted.Should().BeFalse();
    }

    [Fact]
    public void Build_ParagraphWithoutParaId_MintsAnOoxmlValidIdOntoTheBlock()
    {
        var docx = BuildDocx(Para(null, "no id"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        var id = projection.ParaIdMap.Single();
        id.IsMinted.Should().BeTrue();
        id.ParaId.Should().MatchRegex("^[0-9A-F]{8}$");
        Convert.ToUInt32(id.ParaId, 16).Should().BeInRange(1u, 0x7FFFFFFFu);
        EmittedParaIds(projection.Html).Single().Should().Be(id.ParaId);
    }

    [Fact]
    public void Build_EmptyParagraph_IsPreservedWithItsDataParaId()
    {
        // The ignoreEmptyParagraphs root cause: an empty <w:p> MUST become an emitted, id-carrying block
        // or the paragraph set drifts out of alignment with the retained-original paraId sequence.
        var docx = BuildDocx(Para("11111111", "first"), EmptyPara("22222222"), Para("33333333", "third"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        EmittedParaIds(projection.Html).Should().Equal("11111111", "22222222", "33333333");
    }

    // ── formatting ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_BoldItalicRunsAndHeading_EmitStrongEmAndHeadingTags()
    {
        var bold = new Run(new RunProperties(new Bold()), new Text("bold"));
        var italic = new Run(new RunProperties(new Italic()), new Text("italic"));
        var docx = BuildDocx(
            Heading("AAAA0001", 1, "Title"),
            new Paragraph(new ParagraphProperties(), bold, italic) { ParagraphId = new HexBinaryValue("AAAA0002") });

        var html = new ComposeDocxProjectionBuilder().Build(docx).Html;

        html.Should().Contain("<h1 data-paraid=\"AAAA0001\">Title</h1>");
        html.Should().Contain("<strong>bold</strong>");
        html.Should().Contain("<em>italic</em>");
    }

    // ── revision flattening (F-02) ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_InsertedAndDeletedRuns_EmitAllTextAsPlainRunsWithWrappersStripped()
    {
        // w:ins keeps text; w:del keeps its w:delText — both present so the client overlay can re-anchor.
        var para = new Paragraph(
            new Run(new Text("keep ") { Space = SpaceProcessingModeValues.Preserve }),
            new InsertedRun(new Run(new Text("inserted"))) { Id = "1", Author = "A" },
            new DeletedRun(new Run(new DeletedText(" deleted"))) { Id = "2", Author = "B" })
        { ParagraphId = new HexBinaryValue("DE100001") };
        var docx = BuildDocx(para);

        var html = new ComposeDocxProjectionBuilder().Build(docx).Html;

        html.Should().Contain("keep ").And.Contain("inserted").And.Contain("deleted");
        html.Should().NotContain("<ins").And.NotContain("<del"); // wrappers stripped to plain runs
    }

    [Fact]
    public void Build_FullyDeletedParagraph_IsStillEmittedWithItsDataParaId()
    {
        // A paragraph whose only content is a deletion is still a <w:p> the pre-parse counts — it MUST be
        // emitted with its id (empty content is fine) or the count/id sequence breaks (F-02).
        var deletedPara = new Paragraph(new DeletedRun(new Run(new DeletedText("gone"))) { Id = "9" })
        { ParagraphId = new HexBinaryValue("DE200002") };
        var docx = BuildDocx(Para("DE200001", "before"), deletedPara, Para("DE200003", "after"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        EmittedParaIds(projection.Html).Should().Equal("DE200001", "DE200002", "DE200003");
    }

    // ── fail-closed (F-04 / GPT §11) ────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_MalformedBytes_ReturnsFailedAndCanEditFalseWithoutThrowing()
    {
        var notADocx = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03 }; // ZIP magic, garbage body

        var projection = new ComposeDocxProjectionBuilder().Build(notADocx);

        projection.Status.Should().Be(ComposeProjectionStatus.Failed);
        projection.CanEdit.Should().BeFalse();
        projection.Html.Should().BeEmpty();
    }

    [Fact]
    public void Build_EmptySource_ReturnsFailedClosed()
    {
        var projection = new ComposeDocxProjectionBuilder().Build(ReadOnlyMemory<byte>.Empty);

        projection.Status.Should().Be(ComposeProjectionStatus.Failed);
        projection.CanEdit.Should().BeFalse();
    }

    // ── runtime guard (F-03) — a paragraph enumerated but not rendered ──────────────────────────────

    [Fact]
    public void Build_TextBoxParagraph_DegradesToPartialWithUnrenderedWarning_NotSilentDrift()
    {
        // A text-box paragraph is reached by Descendants<Paragraph>() (so it gets an id) but is NOT a
        // top-level editable block, so it is intentionally not rendered. The guard must observe the
        // shortfall and degrade to Partial with a warning — never silently drop it (F-03).
        var docx = BuildDocx(Para("7B000001", "visible"), TextBoxParagraph("hidden in textbox"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.ParaIdMap.Count.Should().BeGreaterThan(EmittedParaIds(projection.Html).Count);
        projection.Status.Should().Be(ComposeProjectionStatus.Partial);
        projection.Warnings.Should().Contain(w => w.Code == "unrendered-paragraphs");
    }

    // ── hyperlink protocol allowlist (GPT §13) ──────────────────────────────────────────────────────

    [Fact]
    public void Build_HttpsHyperlink_EmitsAnchorHref()
    {
        var docx = BuildDocxWithHyperlink("https://example.com/", "link");

        var html = new ComposeDocxProjectionBuilder().Build(docx).Html;

        html.Should().Contain("<a href=\"https://example.com/\">link</a>");
    }

    [Fact]
    public void Build_JavascriptHyperlink_IsNeutralizedToPlainText()
    {
        var docx = BuildDocxWithHyperlink("javascript:alert(1)", "danger");

        var html = new ComposeDocxProjectionBuilder().Build(docx).Html;

        html.Should().NotContain("javascript:");
        html.Should().NotContain("<a "); // unsafe target → text without a link
        html.Should().Contain("danger");
    }

    // ── textbox + hyperlink fixture helpers ─────────────────────────────────────────────────────────

    private static Paragraph TextBoxParagraph(string innerText)
    {
        // A DrawingML text box: Run → Drawing → wps:wsp → wps:txbx → w:txbxContent → w:p. The inner
        // paragraph is a Descendants<Paragraph>() match but not a body/cell block child.
        var inner = new TextBoxContent(new Paragraph(new Run(new Text(innerText))));
        var shape = new WordprocessingShape(new TextBoxInfo2(inner));
        var graphicData = new A.GraphicData(shape) { Uri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape" };
        var graphic = new A.Graphic(graphicData);
        var inline = new DW.Inline(
            new DW.Extent { Cx = 100L, Cy = 100L },
            new DW.DocProperties { Id = 1U, Name = "tb" },
            graphic);
        return new Paragraph(new Run(new Drawing(inline)));
    }

    private static byte[] BuildDocxWithHyperlink(string uri, string text)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var rel = main.AddHyperlinkRelationship(new Uri(uri, UriKind.Absolute), true);
            var body = new Body(new Paragraph(new Hyperlink(new Run(new Text(text))) { Id = rel.Id })
            { ParagraphId = new HexBinaryValue("A11C0001") });
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }
}
