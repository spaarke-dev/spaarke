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
using Sprk.Bff.Api.Tests.Seam.Compose;
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

    // ── FR-01 (task 011): intra-paragraph offset-addressing table ───────────────────────────────────

    [Fact]
    public void Build_MultipleFormattedRuns_OffsetTableResolvesEachRunSplitDeterministically()
    {
        // A single logical span split across three w:r with different w:rPr — the "formatted/split runs"
        // case FR-01 must account for. Editor text: "Hello brave world" (17 chars).
        var para = new Paragraph(
            new Run(new Text("Hello ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new RunProperties(new Bold()), new Text("brave ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Text("world")))
        { ParagraphId = new HexBinaryValue("AAAA1001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "AAAA1001");

        map.Runs.Select(r => (r.RunIndex, r.StartOffset, r.Length))
            .Should().Equal((0, 0, 6), (1, 6, 6), (2, 12, 5));
        map.TotalLength.Should().Be(17);
        map.Runs.Should().OnlyContain(r => r.TrackChange == RunTrackChange.None);

        // Deterministic offset → (runIndex, run-local-offset) resolution (left-biased at boundaries).
        Resolve(map, 0).Should().Be(new RunOffsetResolution(0, 0));
        Resolve(map, 8).Should().Be(new RunOffsetResolution(1, 2));   // inside the bold run "br|ave "
        Resolve(map, 6).Should().Be(new RunOffsetResolution(1, 0));   // boundary → start of the following run
        Resolve(map, 12).Should().Be(new RunOffsetResolution(2, 0));
        Resolve(map, 17).Should().Be(new RunOffsetResolution(2, 5));  // terminal → end of last run
    }

    [Fact]
    public void Build_OffsetAddressingTable_IsIndexAlignedWithParaIdMap()
    {
        // The table is emitted in the same Descendants<Paragraph>() pass as ParaIdMap, so it is 1:1
        // aligned — same count, same paraIds, same order — including inside recursive containers.
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

        projection.OffsetAddressingTable.Select(m => m.ParaId)
            .Should().Equal(projection.ParaIdMap.Select(e => e.ParaId));
    }

    [Fact]
    public void Build_ParagraphWithPreExistingTrackedChanges_IncludesTagsAndKeepsThemAddressable()
    {
        // FR-01: the table accounts for pre-existing w:ins/w:del. Their runs are editor-visible (F-02
        // flattening), so they MUST be in the flatten AND tagged — an offset inside pre-existing tracked
        // content still resolves to a run split (it is not lost). Editor text: "keep inserted deleted".
        var para = new Paragraph(
            new Run(new Text("keep ") { Space = SpaceProcessingModeValues.Preserve }),
            new InsertedRun(new Run(new Text("inserted"))) { Id = "1", Author = "A" },
            new DeletedRun(new Run(new DeletedText(" deleted"))) { Id = "2", Author = "B" })
        { ParagraphId = new HexBinaryValue("DE100001") };

        var map = new ComposeDocxProjectionBuilder().Build(BuildDocx(para))
            .OffsetAddressingTable.Single(m => m.ParaId == "DE100001");

        map.Runs.Select(r => (r.StartOffset, r.Length, r.TrackChange))
            .Should().Equal(
                (0, 5, RunTrackChange.None),
                (5, 8, RunTrackChange.Inserted),
                (13, 8, RunTrackChange.Deleted));
        map.TotalLength.Should().Be(21);
        Resolve(map, 8).Should().Be(new RunOffsetResolution(1, 3));   // inside pre-existing w:ins
        Resolve(map, 15).Should().Be(new RunOffsetResolution(2, 2));  // inside pre-existing w:del
    }

    [Fact]
    public void Build_ResolvedSplit_MatchesIndependentSdkSplitAtSameOffset()
    {
        // Round-trip determinism (FR-01 acceptance): the table's resolved (runIndex, run-local-offset)
        // matches an INDEPENDENT split of the same run at the same offset — the text left of the split,
        // concatenated across the flatten, equals the editor-visible text up to that offset.
        var para = new Paragraph(
            new Run(new Text("The quick ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new RunProperties(new Italic()), new Text("brown ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Text("fox")))
        { ParagraphId = new HexBinaryValue("B0000001") };
        var docx = BuildDocx(para);

        var map = new ComposeDocxProjectionBuilder().Build(docx)
            .OffsetAddressingTable.Single(m => m.ParaId == "B0000001");

        // Re-open the SAME bytes and independently flatten the runs (mirrors the projection's descent).
        var reopened = SingleBodyParagraph(docx);
        var flatRuns = FlattenRuns(reopened);
        var editorText = string.Concat(flatRuns.Select(RunText));

        for (var offset = 0; offset <= editorText.Length; offset++)
        {
            var res = Resolve(map, offset);
            var prefixBeforeRun = flatRuns.Take(res.RunIndex).Sum(r => RunText(r).Length);
            var independentPrefix = editorText[..offset];

            // The independent split point (preceding runs + run-local offset) is exactly the editor offset,
            // and the text left of it equals the editor text up to that offset — deterministic, no clamping.
            (prefixBeforeRun + res.RunLocalOffset).Should().Be(offset);
            (string.Concat(flatRuns.Take(res.RunIndex).Select(RunText)) + RunText(flatRuns[res.RunIndex])[..res.RunLocalOffset])
                .Should().Be(independentPrefix);
        }
    }

    [Fact]
    public void Build_OffsetPastParagraphEnd_IsRejectedNotClamped()
    {
        // FR-01 negative: an offset past paragraph end is REFUSED, never silently clamped to the end.
        var map = new ComposeDocxProjectionBuilder().Build(BuildDocx(Para("C0000001", "world")))
            .OffsetAddressingTable.Single(m => m.ParaId == "C0000001");

        map.TotalLength.Should().Be(5);
        map.TryResolve(5, out _).Should().BeTrue("the terminal offset (== length) is the point after the last char");
        map.TryResolve(6, out _).Should().BeFalse("an offset past the end is out of range, not clamped");
        map.TryResolve(-1, out _).Should().BeFalse();
        map.Invoking(m => m.Resolve(6)).Should().Throw<ArgumentOutOfRangeException>();
        map.Invoking(m => m.Resolve(500)).Should().Throw<ArgumentOutOfRangeException>();
    }

    // ── FR-02 (task 012): opaque atoms — SDT/content controls, fields, complex/floating objects ────

    [Fact]
    public void Build_SdtBlockWithDateType_BecomesWholeConstructAtom_NotEditableParagraphs()
    {
        // A date content control is a genuinely non-text declared type — the whole block becomes ONE
        // opaque atom (data-atomid, never data-paraid), and the inner paragraph is never separately
        // rendered — so the projection degrades to Partial via the existing F-03 guard.
        var docx = BuildDocx(
            Para("11110001", "before"),
            new SdtBlock(new SdtProperties(new SdtContentDate()), new SdtContentBlock(Para(null, "2026-07-22"))),
            Para("11110003", "after"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Status.Should().Be(ComposeProjectionStatus.Partial, "the atom's inner paragraph is never emitted");
        projection.CanEdit.Should().BeTrue();
        projection.BlockAtoms.Should().HaveCount(1);
        projection.BlockAtoms[0].Kind.Should().Be(ComposeAtomKind.Sdt);
        projection.Html.Should().Contain("data-atomid=\"" + projection.BlockAtoms[0].AtomId + "\"");
        projection.Html.Should().Contain("contenteditable=\"false\"");
        // The atom's minted id is NOT a paragraph id — the F-01 single-walk invariant (every EMITTED
        // data-paraid has a ParaIdMap entry) must stay intact even though ParaIdMap itself still carries
        // the atom's inner (never-rendered) paragraph — same F-03 shortfall shape as the text-box case.
        projection.ParaIdMap.Select(e => e.ParaId).Should().NotContain(projection.BlockAtoms[0].AtomId);
        EmittedParaIds(projection.Html).Should().BeSubsetOf(projection.ParaIdMap.Select(e => e.ParaId));
        projection.ParaIdMap.Should().HaveCount(EmittedParaIds(projection.Html).Count + 1, "the atom's inner date paragraph is minted but never separately rendered");
        // Document order preserved: the atom's placeholder sits between the two real paragraph blocks.
        var beforeIdx = projection.Html.IndexOf("11110001", StringComparison.Ordinal);
        var atomIdx = projection.Html.IndexOf("data-atomid", StringComparison.Ordinal);
        var afterIdx = projection.Html.IndexOf("11110003", StringComparison.Ordinal);
        beforeIdx.Should().BeLessThan(atomIdx);
        atomIdx.Should().BeLessThan(afterIdx);
    }

    [Fact]
    public void Build_SdtBlockWithNoDeclaredType_StaysTransparent_NoBlockAtomEmitted()
    {
        // Plain/rich-text content control (no SdtProperties at all, the corpus's actual shape) — the
        // shell stays transparent so the wrapped paragraph remains editable (the escalation-boundary
        // decision documented on IsSpecialSdtControl). Zero regression vs. pre-task-012 behavior.
        var docx = BuildDocx(new SdtBlock(new SdtContentBlock(Para(null, "inside content control"))));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.BlockAtoms.Should().BeEmpty();
        projection.Html.Should().NotContain("compose-atom");
        projection.Html.Should().Contain("inside content control");
        projection.Warnings.Should().Contain(w => w.Code == "content-control");
    }

    [Fact]
    public void Build_InlineSdtRunWithGroupType_BecomesInlineAtom_TaggedInOffsetTable()
    {
        var para = new Paragraph(
            new Run(new Text("before ") { Space = SpaceProcessingModeValues.Preserve }),
            new SdtRun(new SdtProperties(new SdtContentGroup()), new SdtContentRun(new Run(new Text("grouped")))),
            new Run(new Text(" after") ))
        { ParagraphId = new HexBinaryValue("22220001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("data-atom-kind=\"sdt\"").And.Contain("contenteditable=\"false\"").And.Contain("grouped");
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "22220001");
        map.Runs.Should().Contain(r => r.AtomKind == ComposeAtomKind.Sdt && r.IsAtom && r.Length == "grouped".Length);
        // No separate identity for an inline atom — it carries the containing paragraph's paraId only.
        projection.BlockAtoms.Should().BeEmpty();
    }

    [Fact]
    public void Build_InlineSdtRunWithNoDeclaredType_StaysTransparent()
    {
        var para = new Paragraph(new SdtRun(new SdtContentRun(new Run(new Text("plain value")))))
        { ParagraphId = new HexBinaryValue("22220002") };

        var html = new ComposeDocxProjectionBuilder().Build(BuildDocx(para)).Html;

        html.Should().Contain("plain value");
        html.Should().NotContain("compose-atom");
    }

    [Fact]
    public void Build_SimpleField_BecomesAtomCarryingCachedDisplayValue()
    {
        // w:fldSimple — its cached value (here "1") becomes the atom's non-editable content.
        var para = new Paragraph(
            new Run(new Text("Page ") { Space = SpaceProcessingModeValues.Preserve }),
            new SimpleField(new Run(new Text("1"))) { Instruction = "PAGE" })
        { ParagraphId = new HexBinaryValue("33330001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("data-atom-kind=\"field\"").And.Contain(">1</span>");
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "33330001");
        map.Runs.Select(r => (r.StartOffset, r.Length, r.AtomKind))
            .Should().Equal((0, 5, (ComposeAtomKind?)null), (5, 1, ComposeAtomKind.Field));
        map.TotalLength.Should().Be(6);
    }

    [Fact]
    public void Build_FldCharFieldSequence_EmitsOneAtomWithCachedResultOnly_InstrTextNeverShown()
    {
        // The CIPO corpus doc's actual page-number field shape: begin → instrText " PAGE " (never shown) →
        // separate → cached result "1" → end. ONE atom, length == the result text only.
        var para = new Paragraph(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("1")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }))
        { ParagraphId = new HexBinaryValue("44440001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().NotContain("PAGE", "the field CODE is never editor-visible");
        projection.Html.Should().Contain("data-atom-kind=\"field\"").And.Contain(">1</span>");
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "44440001");
        map.Runs.Should().ContainSingle();
        map.Runs[0].AtomKind.Should().Be(ComposeAtomKind.Field);
        map.Runs[0].Length.Should().Be(1);
        map.TotalLength.Should().Be(1);
    }

    [Fact]
    public void Build_UnterminatedFieldBegin_NeverThrows_DegradesGracefully()
    {
        // FR-02 negative / F-04 fail-closed: a malformed field with no closing `end` must never throw or
        // corrupt the scan — the swallowed runs simply contribute no offset-visible content.
        var para = new Paragraph(
            new Run(new Text("keep ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("orphaned")))
        { ParagraphId = new HexBinaryValue("44440002") };
        var docx = BuildDocx(para);

        var builder = new ComposeDocxProjectionBuilder();
        Action act = () => builder.Build(docx);

        act.Should().NotThrow();
        var projection = builder.Build(docx);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);
        projection.Html.Should().Contain("keep ");
    }

    [Fact]
    public void Build_DrawingRunOutsideTextBox_BecomesComplexObjectAtom_InsteadOfSilentlyVanishing()
    {
        // Pre-task-012 behavior silently dropped a Run containing only a w:drawing (zero length, no HTML).
        // FR-02: it now becomes a visible, non-editable atom placeholder instead.
        var inline = new DW.Inline(
            new DW.Extent { Cx = 100L, Cy = 100L },
            new DW.DocProperties { Id = 1U, Name = "img" },
            new A.Graphic(new A.GraphicData { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" }));
        var para = new Paragraph(
            new Run(new Text("caption: ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Drawing(inline)))
        { ParagraphId = new HexBinaryValue("55550001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("data-atom-kind=\"object\"").And.Contain("contenteditable=\"false\"");
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "55550001");
        map.Runs.Should().Contain(r => r.AtomKind == ComposeAtomKind.ComplexObject && r.Length == 1);
    }

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public void Build_CorpusDocument_OffsetTableRoundTripsDeterministicallyAndRejectsOutOfRange(string corpusPath)
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusPath);

        var first = new ComposeDocxProjectionBuilder().Build(bytes);
        var second = new ComposeDocxProjectionBuilder().Build(bytes);

        first.OffsetAddressingTable.Should().NotBeEmpty();
        // Deterministic: two independent projections of the same bytes yield identical boundary maps.
        first.OffsetAddressingTable.Should().BeEquivalentTo(
            second.OffsetAddressingTable, o => o.WithStrictOrdering(),
            "the offset-addressing table must be a pure function of the document bytes");

        foreach (var map in first.OffsetAddressingTable)
        {
            // Contiguous, gap-free, zero-based run spans covering exactly [0, TotalLength].
            var cursor = 0;
            foreach (var run in map.Runs)
            {
                run.StartOffset.Should().Be(cursor, $"runs are contiguous in paraId {map.ParaId}");
                run.Length.Should().BeGreaterThanOrEqualTo(0);
                cursor += run.Length;
            }
            map.TotalLength.Should().Be(cursor);

            // Every offset in the closed domain resolves; one past the end is rejected (never clamped).
            for (var offset = 0; offset <= map.TotalLength; offset++)
            {
                map.TryResolve(offset, out var res).Should().BeTrue();
                res.RunLocalOffset.Should().BeGreaterThanOrEqualTo(0);
            }
            map.TryResolve(map.TotalLength + 1, out _).Should().BeFalse($"paraId {map.ParaId}: past-end offset must be refused");
        }
    }

    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(p => new object[] { p });

    private static RunOffsetResolution Resolve(ParaOffsetMap map, int offset)
    {
        map.TryResolve(offset, out var res).Should().BeTrue($"offset {offset} is in range for paraId {map.ParaId}");
        return res;
    }

    private static Paragraph SingleBodyParagraph(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().First();
    }

    // Independent re-derivation of the editor-visible run flatten (mirrors the builder's descent) so the
    // determinism test compares the table against a SEPARATE walk, not the builder's own output.
    private static List<Run> FlattenRuns(OpenXmlElement container)
    {
        var runs = new List<Run>();
        void Walk(OpenXmlElement c)
        {
            foreach (var child in c.Elements())
            {
                switch (child)
                {
                    case Run r: runs.Add(r); break;
                    case Hyperlink h: Walk(h); break;
                    case InsertedRun ins: Walk(ins); break;
                    case DeletedRun del: Walk(del); break;
                    case SdtRun sdt when sdt.GetFirstChild<SdtContentRun>() is { } content: Walk(content); break;
                    default: break;
                }
            }
        }
        Walk(container);
        return runs;
    }

    private static string RunText(Run run)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var child in run.Elements())
        {
            switch (child)
            {
                case Text t: sb.Append(t.Text); break;
                case DeletedText dt: sb.Append(dt.Text); break;
                default: break;
            }
        }
        return sb.ToString();
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

    // ── FR-09 construct audit — alignment / ordered-list / symbol tests (task 002, spaarkeai-compose-
    //    fidelity-r4.5). None of these existed before this task (verified: this class had zero
    //    Justification/NumberingProperties/SymbolChar/CarriageReturn fixtures until now). ──────────────

    private static byte[] BuildDocxWithNumbering(NumberFormatValues format, Paragraph paragraph)
    {
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = wordDoc.AddMainDocumentPart();
            var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering(
                new AbstractNum(
                    new Level(
                        new NumberingFormat { Val = format },
                        new LevelText { Val = format == NumberFormatValues.Bullet ? "•" : "%1." })
                    { LevelIndex = 0 })
                { AbstractNumberId = 1 },
                new NumberingInstance(new AbstractNumId { Val = 1 }) { NumberID = 1 });
            numberingPart.Numbering.Save();

            main.Document = new Document(new Body(paragraph));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static Paragraph NumberedPara(string? paraId, string text, int ilvl = 0, int numId = 1)
    {
        var p = Para(paraId, text);
        p.ParagraphProperties = new ParagraphProperties(
            new NumberingProperties(
                new NumberingLevelReference { Val = ilvl },
                new NumberingId { Val = numId }));
        return p;
    }

    [Fact]
    public void Build_ParagraphWithDecimalNumPr_RendersInsideOrderedList()
    {
        // FR-09: no ordered-list construct test existed before this task.
        var docx = BuildDocxWithNumbering(NumberFormatValues.Decimal, NumberedPara("00A00001", "First clause"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Html.Should().Contain("<ol>").And.Contain("<li>").And.NotContain("<ul>");
    }

    [Fact]
    public void Build_ParagraphWithBulletNumPr_RendersInsideUnorderedList()
    {
        // FR-09: no bullet/unordered-list construct test existed before this task.
        var docx = BuildDocxWithNumbering(NumberFormatValues.Bullet, NumberedPara("00B00001", "Bulleted item"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Html.Should().Contain("<ul>").And.Contain("<li>").And.NotContain("<ol>");
    }

    [Theory]
    [InlineData("center", "center")]
    [InlineData("right", "right")]
    [InlineData("both", "justify")]
    public void Build_ParagraphWithJustification_EmitsTextAlignStyle(string justificationToken, string expectedCss)
    {
        // FR-09: no alignment construct test existed before this task (AppendAlignment, :816-830).
        // JustificationValues is not a compile-time-constant attribute argument type, so the [Theory]
        // carries a string token and maps it to the OOXML enum value here.
        JustificationValues justification = justificationToken switch
        {
            "center" => JustificationValues.Center,
            "right" => JustificationValues.Right,
            "both" => JustificationValues.Both,
            _ => throw new ArgumentOutOfRangeException(nameof(justificationToken)),
        };
        var para = Para("00C00001", "Aligned text");
        para.ParagraphProperties = new ParagraphProperties(new Justification { Val = justification });

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain($"style=\"text-align:{expectedCss}\"");
    }

    [Fact]
    public void Build_ParagraphWithLeftJustification_EmitsNoTextAlignStyle()
    {
        // AppendAlignment only emits a style for center/right/both — left (Word's default) emits nothing.
        var para = Para("00C00002", "Left text");
        para.ParagraphProperties = new ParagraphProperties(new Justification { Val = JustificationValues.Left });

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().NotContain("text-align");
    }

    [Fact]
    public void Build_ParagraphWithSymbolCharRun_CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr06()
    {
        // Characterization baseline (WS-2 FR-06 target, spaarkeai-compose-fidelity-r4.5 task 002):
        // w:sym (e.g. Symbol-font F0A7 -> section-mark) is NOT yet mapped to its Unicode equivalent —
        // ComposeDocxProjectionBuilder.RenderRun has no case for SymbolChar, so the whole run silently
        // contributes nothing (no glyph, no placeholder, no warning). This test PINS that current gap;
        // when WS-2 lands FR-06, update (not delete) this test to assert the mapped glyph IS present.
        var para = new Paragraph(
            new Run(new SymbolChar { Font = "Symbol", Char = "F0A7" }),
            new Run(new Text("Confidentiality") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("00D00001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("Confidentiality");
        projection.Html.Should().NotContain("§",
            "WS-2 FR-06 target: the Symbol-font glyph (section mark) is currently dropped silently, not mapped or placeholdered");
    }

    [Fact]
    public void Build_ParagraphWithCarriageReturnRun_CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr05()
    {
        // Characterization baseline (WS-2 FR-05 target, spaarkeai-compose-fidelity-r4.5 task 002): w:cr
        // is a break with the same intent as w:br, but ComposeDocxProjectionBuilder.RenderRun has no
        // case for CarriageReturn — it silently contributes nothing (no <br>, no separator at all),
        // unlike Break (w:br) which correctly emits <br> today. This test PINS that current gap; update
        // (not delete) it when WS-2 lands FR-05.
        var para = new Paragraph(
            new Run(new Text("before") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new CarriageReturn()),
            new Run(new Text("after") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("00E00001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().NotContain("<br>",
            "WS-2 FR-05 target: w:cr does not yet emit a break representation the way w:br (Break) does");
        projection.Html.Should().Contain("beforeafter",
            "today the two runs' text is concatenated with NO separator at all — the w:cr break is fully invisible, not merely unstyled");
    }
}
