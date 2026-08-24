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
            new Run(new Text(" after")))
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
    public void Build_ParagraphWithLeftIndent_EmitsMarginLeftStyle()
    {
        // FR-07 (task 021): w:ind/@w:left (twips) -> margin-left (pt). AppendIndentDeclarations,
        // TwipsToPoints: 720 twips / 20 = 36pt exactly (1pt == 20 twips is an OOXML unit identity, not an
        // approximation) — no w:ind construct test existed before this task.
        var para = Para("00E00001", "Indented clause");
        para.ParagraphProperties = new ParagraphProperties(new Indentation { Left = "720" });

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("style=\"margin-left:36pt\"");
    }

    [Fact]
    public void Build_ParagraphWithFirstLineIndent_EmitsPositiveTextIndentAlongsideMarginLeft()
    {
        // FR-07: w:ind/@w:firstLine is an ADDITIONAL positive offset for the first line only, on top of
        // @w:left — emitted as a positive text-indent alongside margin-left (720 twips = 36pt left;
        // 240 twips = 12pt first-line offset).
        var para = Para("00E00002", "First-line indented clause");
        para.ParagraphProperties = new ParagraphProperties(new Indentation { Left = "720", FirstLine = "240" });

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("style=\"margin-left:36pt;text-indent:12pt\"");
    }

    [Fact]
    public void Build_ParagraphWithHangingIndent_EmitsNegativeTextIndentAlongsideMarginLeft()
    {
        // FR-07: w:ind/@w:hanging outdents the FIRST line relative to the rest of the paragraph — the
        // inverse of firstLine — emitted as a NEGATIVE text-indent (720 twips = 36pt left; 360 twips =
        // 18pt hanging outdent, so the first line starts at 36pt - 18pt = 18pt).
        var para = Para("00E00003", "Hanging indented clause");
        para.ParagraphProperties = new ParagraphProperties(new Indentation { Left = "720", Hanging = "360" });

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("style=\"margin-left:36pt;text-indent:-18pt\"");
    }

    [Fact]
    public void Build_ParagraphWithHangingAndFirstLineBothPresent_HangingTakesPrecedence()
    {
        // FR-07 edge case: per ECMA-376 §17.3.1.12, w:hanging and w:firstLine are mutually exclusive on one
        // w:ind. If a malformed source somehow carries both, w:hanging wins (Word's own resolution) — the
        // firstLine value here (999 twips) must NOT appear in the output.
        var para = Para("00E00004", "Conflicting indent attributes");
        para.ParagraphProperties = new ParagraphProperties(new Indentation { Hanging = "360", FirstLine = "999" });

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("text-indent:-18pt").And.NotContain("999");
    }

    [Fact]
    public void Build_ParagraphWithAlignmentAndIndent_CombinesIntoOneStyleAttribute()
    {
        // FR-07/FR-09: an HTML element cannot carry two `style` attributes — AppendParagraphStyle MUST
        // combine alignment (FR-09, pre-existing) and indentation (FR-07, this task) into ONE style
        // attribute, semicolon-joined, rather than emitting two separate style="..." attributes.
        var para = Para("00E00005", "Aligned and indented clause");
        para.ParagraphProperties = new ParagraphProperties(
            new Justification { Val = JustificationValues.Center },
            new Indentation { Left = "720" });

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("style=\"text-align:center;margin-left:36pt\"");
        Regex.Matches(projection.Html, "style=\"").Count.Should().Be(1,
            "alignment and indentation must share a single style attribute per paragraph");
    }

    [Fact]
    public void Build_ParagraphWithNoIndentation_EmitsNoMarginOrTextIndentStyle()
    {
        // Negative case: a paragraph with no w:ind at all emits neither margin-left nor text-indent —
        // AppendIndentDeclarations is a no-op when ParagraphProperties.Indentation is null.
        var para = Para("00E00006", "Unindented clause");

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().NotContain("margin-left").And.NotContain("text-indent");
    }

    [Fact]
    public void Build_ParagraphWithSymbolCharRun_MappedSymbolFont_RendersUnicodeGlyphWithNoWarning()
    {
        // WS-2 FR-06 flip (spaarkeai-compose-fidelity-r4.5 task 020) of the task 002 characterization
        // Build_ParagraphWithSymbolCharRun_CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr06: a
        // VERIFIED symbol-font mapping (Symbol F0A7 -> section mark, corpus-manifest.md row 12) now
        // renders its Unicode glyph verbatim, in place, with no intra-run glyph-loss warning.
        var para = new Paragraph(
            new Run(new SymbolChar { Font = "Symbol", Char = "F0A7" }),
            new Run(new Text("Confidentiality") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("00D00001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        // Task 048: the glyph is UNCHANGED — this is still exactly what the user sees, still immediately
        // before the following run's text — but it is now wrapped in an atom carrying the font + code point,
        // so a save re-emits the original w:sym instead of the resolved look-alike. § in a legal document is
        // usually Symbol-font F0A7, not U+00A7, and writing back the look-alike changes the character the
        // document contains.
        projection.Html.Should().Contain(
            "<span class=\"compose-atom\" data-atom-kind=\"symbol\" data-sym-font=\"Symbol\" " +
            "data-sym-char=\"F0A7\" contenteditable=\"false\">§</span>Confidentiality",
            "WS-2 FR-06: the mapped Symbol-font glyph (section mark, U+00A7) renders immediately before " +
            "the following run's text, verbatim — no separator invented, no glyph dropped — and task 048 " +
            "carries the source font/char alongside it so the write path never has to guess");
        projection.Warnings.Should().NotContain(w => w.Code == "unmapped-symbol-char",
            "a VERIFIED mapping must never raise the unmapped-glyph warning");

        // The offset space is what everything else is addressed in, so it must not move: one editor-visible
        // character for the symbol, exactly as before it became an atom.
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "00D00001");
        map.TotalLength.Should().Be(1 + "Confidentiality".Length,
            "a w:sym contributes exactly 1 editor-visible character whether or not it is wrapped in an atom");
    }

    [Fact]
    public void Build_ParagraphWithSymbolCharRun_UnmappedSymbolFont_RendersPlaceholderAndRaisesWarning()
    {
        // FR-06 negative case + FR-10 warning mechanism: a symbol-font code point with NO verified
        // mapping (e.g. a Wingdings PUA glyph — corpus-manifest.md row 12's deliberate negative case)
        // must NEVER silently vanish. It renders a visible U+FFFD placeholder in place AND raises the
        // intra-run glyph-loss warning ("unmapped-symbol-char") — represent-or-warn, never silent drop.
        var para = new Paragraph(
            new Run(new Text("Item ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new SymbolChar { Font = "Wingdings", Char = "F0A8" }),
            new Run(new Text(" text") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("00D00002") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        // Task 048: the placeholder is still shown — that is the READ contract, unchanged — but the atom now
        // carries the TRUE font + code point beside it. This is the case where the fix matters most: without
        // it, a save would have written the U+FFFD placeholder into the document as the user's content, so a
        // marker that exists purely to be honest on screen would have become the glyph itself.
        projection.Html.Should().Contain(
            "Item <span class=\"compose-atom\" data-atom-kind=\"symbol\" data-sym-font=\"Wingdings\" " +
            "data-sym-char=\"F0A8\" contenteditable=\"false\">�</span> text",
            "an unmapped w:sym renders a visible U+FFFD placeholder in place — never a silent gap — and " +
            "task 048 keeps the real Wingdings F0A8 on the atom so the write path re-emits it, not the �");
        projection.Warnings.Should().ContainSingle(w => w.Code == "unmapped-symbol-char" && w.Count == 1,
            "FR-10: the intra-run glyph-loss warning must fire exactly once for the one unmapped w:sym run " +
            "— the placeholder and the warning always co-occur, never one without the other");
        projection.Status.Should().Be(ComposeProjectionStatus.Partial,
            "any raised warning demotes the projection to Partial per the existing status contract");
    }

    [Fact]
    public void Build_ParagraphWithCarriageReturnRun_RendersBreakLikeExistingWBr()
    {
        // WS-2 FR-05 flip (spaarkeai-compose-fidelity-r4.5 task 020) of the task 002 characterization
        // Build_ParagraphWithCarriageReturnRun_CurrentlyDropsGlyphSilently_CharacterizationForWS2Fr05:
        // w:cr now emits <br>, mirroring the pre-existing w:br (Break) handling exactly, instead of
        // vanishing with no separator at all.
        var para = new Paragraph(
            new Run(new Text("before") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new CarriageReturn()),
            new Run(new Text("after") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("00E00001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("before<br>after",
            "WS-2 FR-05: w:cr must emit a break the same way w:br (Break) already does — never a silent, " +
            "separator-less concatenation of the surrounding runs' text");
    }

    // ── task 022 — full construct audit: additional silent-drop fixes found beyond 020/021 ────────────

    [Fact]
    public void Build_RunWithVmlPictureFallback_BecomesComplexObjectAtom_InsteadOfSilentlyVanishing()
    {
        // Task 022 construct audit: w:pict (legacy VML picture fallback) previously fell through
        // RenderRun's default case — zero HTML, zero offset-table length, no warning — a genuine silent
        // drop distinct from the pre-existing w:drawing/w:object coverage (Build_DrawingRunOutsideTextBox_...
        // above). Now IsComplexObjectRun treats it identically: a non-editable atom placeholder.
        var para = new Paragraph(
            new Run(new Text("caption: ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Picture()))
        { ParagraphId = new HexBinaryValue("66660001") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("data-atom-kind=\"object\"").And.Contain("contenteditable=\"false\"");
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "66660001");
        map.Runs.Should().Contain(r => r.AtomKind == ComposeAtomKind.ComplexObject && r.Length == 1);
    }

    [Fact]
    public void Build_ParagraphWithPositionalTab_RendersComposeTabSpanLikeRegularTab()
    {
        // Task 022 construct audit: w:ptab (positional/custom tab stop, e.g. TOC-style leaders) previously
        // fell through to the default case (silently dropped — zero HTML, zero offset length). Now
        // represented identically to w:tab (the established compose-tab, non-collapsing-space simplification).
        var para = new Paragraph(
            new Run(new Text("before") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new PositionalTab { Alignment = AbsolutePositionTabAlignmentValues.Right, RelativeTo = AbsolutePositionTabPositioningBaseValues.Margin, Leader = AbsolutePositionTabLeaderCharValues.Dot }),
            new Run(new Text("after") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("66660002") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        // Note: the compose-tab span's interior character is U+2003 (EM SPACE, not a plain ASCII space) —
        // a deliberate pre-existing choice (predates this task) so the placeholder can never collapse under
        // HTML whitespace rules the way a literal U+0020 could.
        //
        // Task 048: the span is now an ATOM as well as a compose-tab. The class and the em space are both
        // unchanged — it looks exactly as it did — and the only addition is the identity that lets the
        // mapper tell this em space from a typed one, which is what stopped tabs being flattened on save.
        projection.Html.Should().Contain(
            "before<span class=\"compose-atom compose-tab\" data-atom-kind=\"tab\" "
            + "contenteditable=\"false\"> </span>after");
        var map = projection.OffsetAddressingTable.Single(m => m.ParaId == "66660002");
        map.TotalLength.Should().Be("before".Length + 1 + "after".Length,
            "the ptab contributes exactly 1 editor-visible character, mirroring w:tab");
    }

    [Fact]
    public void Build_ParagraphWithFootnoteReference_RaisesWarning_NeverSilentlyVanishes()
    {
        // Task 022 construct audit: w:footnoteReference carries no text of its own (Word computes its
        // displayed number from position in word/footnotes.xml, a part this body-only projection never
        // opens). Previously fell through the default case with no HTML AND no warning — a genuine F-1
        // violation (the reference marker vanishes with zero trace). Fabricating a guessed number is
        // rejected per the same reasoning as w:sym's escalation trigger, so this is warn-only.
        var para = new Paragraph(
            new Run(new Text("as noted") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new RunProperties(new VerticalTextAlignment { Val = VerticalPositionValues.Superscript }), new FootnoteReference { Id = 1 }))
        { ParagraphId = new HexBinaryValue("66660003") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Warnings.Should().ContainSingle(w => w.Code == "unrepresented-footnote-reference" && w.Count == 1);
        projection.Status.Should().Be(ComposeProjectionStatus.Partial);
        projection.Html.Should().Contain("as noted");
    }

    [Fact]
    public void Build_ParagraphWithEndnoteReference_RaisesWarning_NeverSilentlyVanishes()
    {
        var para = new Paragraph(
            new Run(new Text("as noted") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new EndnoteReference { Id = 1 }))
        { ParagraphId = new HexBinaryValue("66660004") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Warnings.Should().ContainSingle(w => w.Code == "unrepresented-endnote-reference" && w.Count == 1);
        projection.Status.Should().Be(ComposeProjectionStatus.Partial);
    }

    [Fact]
    public void Build_ParagraphWithRubyAnnotation_RendersBaseTextVerbatim_DropsPhoneticGuideWithWarning()
    {
        // Task 022 construct audit: w:ruby previously fell through the default case — silently dropping
        // BOTH the base text (rubyBase, real recoverable prose) and the phonetic guide (rt). Unlike w:sym,
        // the base text requires no interpretation/guessing (it is plain w:t content), so it is now
        // rendered verbatim; only the supplementary phonetic guide is omitted, and that omission is warned.
        var ruby = new Ruby(
            new RubyProperties(),
            new RubyContent(new Run(new Text("phonetic-guide-ignored"))),
            new RubyBase(new Run(new Text("base text"))));
        var para = new Paragraph(
            new Run(new Text("prefix ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(ruby),
            new Run(new Text(" suffix") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("66660005") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("prefix base text suffix",
            "the ruby BASE text is real document prose and renders verbatim");
        projection.Html.Should().NotContain("phonetic-guide-ignored",
            "the phonetic guide is a supplementary pronunciation annotation, not the document's own words");
        projection.Warnings.Should().ContainSingle(w => w.Code == "ruby-phonetic-guide-dropped" && w.Count == 1);
    }

    [Theory]
    [InlineData("page", "page-break-rendered-as-line-break")]
    [InlineData("column", "column-break-rendered-as-line-break")]
    public void Build_ParagraphWithPageOrColumnBreak_RendersLineBreakAndWarnsOfFidelityDowngrade(
        string breakTypeToken, string expectedWarningCode)
    {
        // Task 022 construct audit (design §4: "w:br type=page — currently a line break"): this editor has
        // no page/pagination concept (F-5/WS-5 deferred), so a page/column break still renders as <br> —
        // but the semantic downgrade is now surfaced as a warning instead of being silently absorbed into
        // the default TextWrapping-break disposition.
        BreakValues breakType = breakTypeToken switch
        {
            "page" => BreakValues.Page,
            "column" => BreakValues.Column,
            _ => throw new ArgumentOutOfRangeException(nameof(breakTypeToken)),
        };
        var para = new Paragraph(
            new Run(new Text("before") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Break { Type = breakType }),
            new Run(new Text("after") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("66660006") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("before<br>after");
        projection.Warnings.Should().ContainSingle(w => w.Code == expectedWarningCode && w.Count == 1);
    }

    [Fact]
    public void Build_ParagraphWithDefaultTextWrappingBreak_RendersLineBreakWithNoWarning()
    {
        // Negative case / non-regression guard: an ordinary w:br (no @type, or @type="textWrapping" — the
        // pre-existing, already-corpus-exercised case per corpus-manifest.md row 2's "w:br line breaks for
        // the letterhead block") must NOT trip either new page/column warning.
        var para = new Paragraph(
            new Run(new Text("before") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new Break()),
            new Run(new Text("after") { Space = SpaceProcessingModeValues.Preserve }))
        { ParagraphId = new HexBinaryValue("66660007") };

        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(para));

        projection.Html.Should().Contain("before<br>after");
        projection.Warnings.Should().NotContain(w =>
            w.Code == "page-break-rendered-as-line-break" || w.Code == "column-break-rendered-as-line-break");
    }

    // ── task 030 (spaarkeai-compose-fidelity-r4.5, WS-3): numbering-MODEL reader (FR-11/FR-12) ─────────
    // Sanity-tests the PARSED MODEL only — abstractNum/level fields (numFmt/lvlText/start/lvlRestart/
    // isLgl/lvlOverride) + per-paragraph (numId,ilvl) resolution, direct AND style-linked. No computed
    // display label is asserted anywhere here — that is task 031's job, not this one's. Drives the real
    // `internal` parser (InternalsVisibleTo) over real in-memory .docx fixtures and the real task-001
    // corpus — no Mock<HttpMessageHandler>/DI/ctor tests (ADR-038).

    private static byte[] BuildDocxWithMultiLevelNumberingAndOverride()
    {
        // AbstractNum id=7, 2 levels: level0 decimal "%1." start=1; level1 lowerLetter "%1.%2" start=1
        // lvlRestart=1 isLgl=true. NumberingInstance numId=3 -> abstractNumId=7, WITH a level-0
        // w:lvlOverride/w:startOverride=5 (numId-scoped restart, independent of the abstractNum's own
        // w:start) — exercises every FR-11 field in one fixture.
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = wordDoc.AddMainDocumentPart();
            var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering(
                new AbstractNum(
                    new Level(
                        new StartNumberingValue { Val = 1 },
                        new NumberingFormat { Val = NumberFormatValues.Decimal },
                        new LevelText { Val = "%1." })
                    { LevelIndex = 0 },
                    new Level(
                        new StartNumberingValue { Val = 1 },
                        new NumberingFormat { Val = NumberFormatValues.LowerLetter },
                        new LevelText { Val = "%1.%2" },
                        new LevelRestart { Val = 1 },
                        new IsLegalNumberingStyle())
                    { LevelIndex = 1 })
                { AbstractNumberId = 7 },
                new NumberingInstance(
                    new AbstractNumId { Val = 7 },
                    new LevelOverride(new StartOverrideNumberingValue { Val = 5 }) { LevelIndex = 0 })
                { NumberID = 3 });
            numberingPart.Numbering.Save();

            main.Document = new Document(new Body(NumberedPara("A0000001", "clause", ilvl: 0, numId: 3)));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    [Fact]
    public void BuildNumberingModel_ForMultiLevelAbstractNumWithOverride_ExposesEveryFr11Field()
    {
        var docx = BuildDocxWithMultiLevelNumberingAndOverride();
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var mainPart = doc.MainDocumentPart!;

        var model = ComposeDocxProjectionBuilder.BuildNumberingModel(mainPart);

        model.AbstractNumIdByNumId[3].Should().Be(7);

        var level0 = model.Levels[(7, 0)];
        level0.NumFmt.Should().Be(NumberFormatValues.Decimal);
        level0.LvlText.Should().Be("%1.");
        level0.Start.Should().Be(1);
        level0.IsLgl.Should().BeFalse();

        var level1 = model.Levels[(7, 1)];
        level1.NumFmt.Should().Be(NumberFormatValues.LowerLetter);
        level1.LvlText.Should().Be("%1.%2");
        level1.LvlRestart.Should().Be(1);
        level1.IsLgl.Should().BeTrue("w:isLgl forces legal (decimal-style) numbering per FR-11");

        model.ResolveStartOverride(numId: 3, ilvl: 0).Should().Be(5,
            "the numId-scoped w:lvlOverride/w:startOverride must be captured independent of the abstractNum's own w:start");
    }

    [Fact]
    public void ResolveParagraphNumbering_ForDirectNumPr_ResolvesNumIdAndIlvlAsNotStyleLinked()
    {
        var docx = BuildDocxWithMultiLevelNumberingAndOverride();
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var mainPart = doc.MainDocumentPart!;
        var model = ComposeDocxProjectionBuilder.BuildNumberingModel(mainPart);
        var paragraph = mainPart.Document!.Body!.Elements<Paragraph>().Single();

        var resolved = ComposeDocxProjectionBuilder.ResolveParagraphNumbering(paragraph, model);

        resolved.Should().NotBeNull();
        resolved!.NumId.Should().Be(3);
        resolved.Ilvl.Should().Be(0);
        resolved.StyleLinked.Should().BeFalse("a direct w:numPr paragraph is not style-linked (FR-12 is the OTHER case)");
    }

    private static byte[] BuildDocxWithHeadingStyleCarryingNumPr()
    {
        // FR-12 shape: numId/ilvl live on the "Heading2" STYLE's own w:pPr/w:numPr (styles.xml), NOT on
        // the paragraph directly — mirrors heading-style-numbering.docx (corpus-manifest.md row 10).
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = wordDoc.AddMainDocumentPart();

            var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering(
                new AbstractNum(
                    new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1" }) { LevelIndex = 0 },
                    new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2" }) { LevelIndex = 1 })
                { AbstractNumberId = 9 },
                new NumberingInstance(new AbstractNumId { Val = 9 }) { NumberID = 4 });
            numberingPart.Numbering.Save();

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            var heading2 = new Style(
                new StyleParagraphProperties(new NumberingProperties(
                    new NumberingLevelReference { Val = 1 }, new NumberingId { Val = 4 })))
            { Type = StyleValues.Paragraph, StyleId = "Heading2" };
            stylesPart.Styles = new Styles(heading2);
            stylesPart.Styles.Save();

            // Paragraph carries ONLY pStyle — no direct w:numPr (the FR-12 defect this task fixes the READ for).
            var p = new Paragraph(new Run(new Text("Confidentiality") { Space = SpaceProcessingModeValues.Preserve }))
            {
                ParagraphId = new HexBinaryValue("00H20001"),
                ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = "Heading2" }),
            };
            main.Document = new Document(new Body(p));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    [Fact]
    public void ResolveParagraphNumbering_ForHeadingStyleCarryingNumPrDirectly_ResolvesStyleLinkedNumIdAndIlvl()
    {
        // FR-12 acceptance example ("4.2 Confidentiality") — the model side, not the computed label.
        var docx = BuildDocxWithHeadingStyleCarryingNumPr();
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var mainPart = doc.MainDocumentPart!;
        var model = ComposeDocxProjectionBuilder.BuildNumberingModel(mainPart);
        var paragraph = mainPart.Document!.Body!.Elements<Paragraph>().Single();

        var resolved = ComposeDocxProjectionBuilder.ResolveParagraphNumbering(paragraph, model);

        resolved.Should().NotBeNull("the paragraph has NO direct w:numPr — resolution must fall back to its pStyle (FR-12)");
        resolved!.NumId.Should().Be(4);
        resolved.Ilvl.Should().Be(1);
        resolved.StyleLinked.Should().BeTrue();
        resolved.SourceStyleId.Should().Be("Heading2");
    }

    [Fact]
    public void ResolveParagraphNumbering_ForStyleInheritingNumberingViaBasedOn_ResolvesThroughAncestorChain()
    {
        // FR-12's inheritance edge: a style with NO numPr of its own but w:basedOn an ancestor that HAS
        // one — Word's paragraph-property style inheritance applies to numbering too.
        using var ms0 = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms0, WordprocessingDocumentType.Document))
        {
            var main = wordDoc.AddMainDocumentPart();

            var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering(
                new AbstractNum(new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1" }) { LevelIndex = 0 })
                { AbstractNumberId = 11 },
                new NumberingInstance(new AbstractNumId { Val = 11 }) { NumberID = 6 });
            numberingPart.Numbering.Save();

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            var ancestor = new Style(
                new StyleParagraphProperties(new NumberingProperties(
                    new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 6 })))
            { Type = StyleValues.Paragraph, StyleId = "Heading2" };
            var child = new Style(new StyleParagraphProperties()) // no numPr of its own
            { Type = StyleValues.Paragraph, StyleId = "Heading2Sub", BasedOn = new BasedOn { Val = "Heading2" } };
            stylesPart.Styles = new Styles(ancestor, child);
            stylesPart.Styles.Save();

            var p = new Paragraph(new Run(new Text("Sub-clause") { Space = SpaceProcessingModeValues.Preserve }))
            {
                ParagraphId = new HexBinaryValue("00H30001"),
                ParagraphProperties = new ParagraphProperties(new ParagraphStyleId { Val = "Heading2Sub" }),
            };
            main.Document = new Document(new Body(p));
            main.Document.Save();
        }
        var docx = ms0.ToArray();

        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var mainPart = doc.MainDocumentPart!;
        var model = ComposeDocxProjectionBuilder.BuildNumberingModel(mainPart);
        var paragraph = mainPart.Document!.Body!.Elements<Paragraph>().Single();

        var resolved = ComposeDocxProjectionBuilder.ResolveParagraphNumbering(paragraph, model);

        resolved.Should().NotBeNull("Heading2Sub inherits numbering from its w:basedOn ancestor Heading2");
        resolved!.NumId.Should().Be(6);
        resolved.StyleLinked.Should().BeTrue();
        resolved.SourceStyleId.Should().Be("Heading2", "the ANCESTOR that actually carries the w:numPr, not the queried style Heading2Sub");
    }

    // ── task 030: same sanity assertions over the REAL task-001 corpus exemplars (no invented fixture) ──

    private static string CorpusDocPath(string fileName) =>
        Path.Combine(Path.GetDirectoryName(ComposeCorpusFixtureLocator.EnumerateDocumentPaths().First())!, fileName);

    [Fact]
    public void BuildNumberingModel_OverHeadingStyleNumberingCorpusDoc_ResolvesStyleLinkedHeadingsAtBothLevels()
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(CorpusDocPath("heading-style-numbering.docx"));
        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var mainPart = doc.MainDocumentPart!;

        var model = ComposeDocxProjectionBuilder.BuildNumberingModel(mainPart);

        // corpus-manifest.md row 10: Heading1 -> level 0 "%1"; Heading2 -> level 1 "%1.%2" — resolved
        // from the STYLE, since document.xml itself carries zero w:numPr (task 001's confirmed fact).
        var resolvedByOrdinal = mainPart.Document!.Body!.Descendants<Paragraph>()
            .Select(p => ComposeDocxProjectionBuilder.ResolveParagraphNumbering(p, model))
            .ToList();

        var heading1Refs = resolvedByOrdinal.Where(r => r is { StyleLinked: true, Ilvl: 0 }).ToList();
        heading1Refs.Should().NotBeEmpty("Heading1 paragraphs (e.g. 'Recitals', 'Definitions') resolve at ilvl 0 via their style");
        var heading2Refs = resolvedByOrdinal.Where(r => r is { StyleLinked: true, Ilvl: 1 }).ToList();
        heading2Refs.Should().NotBeEmpty("Heading2 paragraphs (e.g. '4.1 Purpose', '4.2 Confidentiality') resolve at ilvl 1 via their style");
    }

    [Fact]
    public void BuildNumberingModel_OverMultilevelCorpusDoc_Exposes3LevelsWithComposedLvlTextTemplates()
    {
        var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(CorpusDocPath("multilevel-1-1-1.docx"));
        using var ms = new MemoryStream(bytes, writable: false);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        var mainPart = doc.MainDocumentPart!;

        var model = ComposeDocxProjectionBuilder.BuildNumberingModel(mainPart);

        // corpus-manifest.md row 11: single abstractNum, 3 levels, "%1." / "%1.%2." / "%1.%2.%3.".
        var directRefs = mainPart.Document!.Body!.Descendants<Paragraph>()
            .Select(p => ComposeDocxProjectionBuilder.ResolveParagraphNumbering(p, model))
            .Where(r => r is not null)
            .Select(r => r!)
            .ToList();

        directRefs.Should().Contain(r => r.Ilvl == 0);
        directRefs.Should().Contain(r => r.Ilvl == 1);
        directRefs.Should().Contain(r => r.Ilvl == 2);
        directRefs.Should().OnlyContain(r => !r.StyleLinked, "multilevel-1-1-1.docx uses direct w:numPr at every level, not style-linked numbering");

        // Every resolved (numId, ilvl) pair must have a level definition with a non-empty lvlText template.
        foreach (var r in directRefs.DistinctBy(r => (r.NumId, r.Ilvl)))
        {
            var level = model.ResolveLevel(r.NumId, r.Ilvl);
            level.Should().NotBeNull($"level {r.Ilvl} of numId {r.NumId} must be defined in numbering.xml");
            level!.LvlText.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void Build_OverNumberingExemplarCorpusDocs_ParsesModelWithoutAnyEscalationWarning()
    {
        // Negative/escalation-boundary check (this task's <escalation> trigger): none of the three WS-3
        // numbering exemplars use a numStyleLink chain or a picture bullet, so wiring the model reader
        // into Build() must NOT introduce a new "numstylelink-unresolved" or "picture-bullet-unresolved"
        // warning on any of them — a false escalation on ordinary numbering would itself be a defect.
        foreach (var fileName in new[] { "nda-interrupted-clauses.docx", "heading-style-numbering.docx", "multilevel-1-1-1.docx" })
        {
            var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(CorpusDocPath(fileName));

            var projection = new ComposeDocxProjectionBuilder().Build(bytes);

            projection.Warnings.Should().NotContain(
                w => w.Code == "numstylelink-unresolved" || w.Code == "picture-bullet-unresolved",
                $"{fileName} does not use either construct — the model reader must not false-positive escalate");
        }
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Task 032 (WS-3, FR-13) — AppendNumberingAttrs: the 031-computed label emitted as a PARAGRAPH DATA
    // ATTRIBUTE (data-computed-number/data-numbering-level), never as text content. The client
    // (composeNumberAtomExtension.ts) renders the visible atom; this only proves the server carries the
    // data correctly and — critically — that it does NOT leak into run text (the text-exactness harness
    // invariant: source-run text == projected text).
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public void Build_NumberedListItem_EmitsComputedNumberAndLevelDataAttributesOnParagraph()
    {
        var docx = BuildDocxWithNumbering(NumberFormatValues.Decimal, NumberedPara("00F00001", "First clause"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Html.Should().Contain("data-computed-number=\"1.\"");
        projection.Html.Should().Contain("data-numbering-level=\"0\"");
    }

    [Fact]
    public void Build_UnnumberedParagraph_EmitsNoComputedNumberOrLevelAttribute()
    {
        var projection = new ComposeDocxProjectionBuilder().Build(BuildDocx(Para("00F00002", "Plain paragraph")));

        projection.Html.Should().NotContain("data-computed-number").And.NotContain("data-numbering-level");
    }

    [Fact]
    public void Build_NumberedParagraph_NeverInjectsTheComputedLabelAsRunText()
    {
        // FR-13's core constraint: the computed label is COMPUTED, not source text. If it ever leaked
        // into the run text (rather than staying an attribute), the text-exactness harness (source-run
        // text == projected text) would break. The paragraph's own source text ("First clause") carries
        // no leading "1." in the .docx — so the projected INNER TEXT (between the closing '>' of the
        // opening <p> tag and '</p>') must be exactly the source text, never the label prepended to it.
        var docx = BuildDocxWithNumbering(NumberFormatValues.Decimal, NumberedPara("00F00003", "First clause"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        var openTagEnd = projection.Html.IndexOf("</p>", StringComparison.Ordinal);
        var innerTextStart = projection.Html.LastIndexOf('>', openTagEnd) + 1;
        var innerText = projection.Html[innerTextStart..openTagEnd];
        innerText.Should().Be("First clause", "the computed label must live ONLY in the data attribute, never prepended to the run text");
    }

    [Fact]
    public void Build_SecondLevelNumberedListItem_EmitsNumberingLevelMatchingIlvl()
    {
        // A direct w:numPr paragraph at ilvl=1 against a 2-level abstractNum (same 2-level shape as
        // BuildDocxWithHeadingStyleCarryingNumPr's fixture, reused here for a DIRECT — not style-linked —
        // numbered paragraph) — proves data-numbering-level carries the paragraph's OWN ilvl, not always 0.
        using var ms = new MemoryStream();
        using (var wordDoc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = wordDoc.AddMainDocumentPart();
            var numberingPart = main.AddNewPart<NumberingDefinitionsPart>();
            numberingPart.Numbering = new Numbering(
                new AbstractNum(
                    new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1" }) { LevelIndex = 0 },
                    new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2" }) { LevelIndex = 1 })
                { AbstractNumberId = 12 },
                new NumberingInstance(new AbstractNumId { Val = 12 }) { NumberID = 5 });
            numberingPart.Numbering.Save();
            main.Document = new Document(new Body(NumberedPara("00F00005", "Sub-item", ilvl: 1, numId: 5)));
            main.Document.Save();
        }

        var projection = new ComposeDocxProjectionBuilder().Build(ms.ToArray());

        projection.Html.Should().Contain("data-numbering-level=\"1\"");
    }

    [Fact]
    public void Build_StyleLinkedNumberedHeading_EmitsComputedNumberOnTheHeadingTagItself()
    {
        // FR-12/FR-13: style-linked numbering (numPr lives on the STYLE, not the paragraph) must ALSO
        // carry the computed label — on the <h#> tag, exactly like a direct-numPr <p>/<li><p>.
        var docx = BuildDocxWithHeadingStyleCarryingNumPr();

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Html.Should().MatchRegex("<h\\d[^>]*data-computed-number=\"[^\"]+\"[^>]*>",
            "a style-linked numbered heading carries its computed label as a data attribute on the <h#> tag");
    }

    [Fact]
    public void Build_BulletListItem_EmitsTheBulletGlyphVerbatimNeverAFabricatedArabicNumeral()
    {
        // Bullet format's lvlText is the literal glyph "•" (no %n placeholder to substitute) —
        // ComposeLabel composes it verbatim (031 notes: "bullet -> the lvlText glyph verbatim"), so a
        // bullet DOES carry a (non-numeric) computed label. This locks the actual behavior and confirms
        // 032's attribute wiring never fabricates a spurious arabic "1." on a bullet.
        var docx = BuildDocxWithNumbering(NumberFormatValues.Bullet, NumberedPara("00F00004", "Bulleted item"));

        var projection = new ComposeDocxProjectionBuilder().Build(docx);

        projection.Html.Should().Contain("data-computed-number=\"•\"");
        projection.Html.Should().NotContain("data-computed-number=\"1\"");
    }

    // ══════════════════════════════════════════════════════════════════════════════════════════════
    // Task 031 (WS-3, FR-11..FR-14) — the deterministic numbering COMPUTATION engine. Asserts the
    // computed label == the label Word displays, per numbered paragraph. THE flagship / NFR-02 release
    // blocker. Synthetic fixtures cover the schemes the decimal-only corpus does not (letters, roman,
    // legal); the interrupted / multi-level / style-linked cases are asserted BOTH over synthetic
    // fixtures here and over the real corpus exemplars (golden labels from corpus-manifest.md §1.5).
    // KEEP-path: pure domain logic over real in-memory .docx (ADR-038 — no Mock<HttpMessageHandler>/DI/
    // ctor tests). The Build() path exercises the engine end-to-end via ParaIdMapEntry.ComputedNumber.
    // ══════════════════════════════════════════════════════════════════════════════════════════════

    // ── engine fixture builders ─────────────────────────────────────────────────────────────────────

    /// <summary>Build a docx carrying an explicit numbering definition + body children, then project it
    /// and return each paragraph's computed label (null for a non-numbered paragraph), in doc order.</summary>
    private static List<string?> ComputedNumbers(Numbering numbering, params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var np = main.AddNewPart<NumberingDefinitionsPart>();
            np.Numbering = numbering;
            np.Numbering.Save();
            var body = new Body();
            foreach (var c in bodyChildren) body.Append(c);
            main.Document = new Document(body);
            main.Document.Save();
        }
        return new ComposeDocxProjectionBuilder().Build(ms.ToArray()).ParaIdMap.Select(e => e.ComputedNumber).ToList();
    }

    /// <summary>A single-level numbering instance (numId → abstractNumId) with one <c>w:lvl</c>.</summary>
    private static Numbering SingleLevel(int numId, int abstractNumId, NumberFormatValues fmt, string lvlText, int start = 1) =>
        new(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = start },
                    new NumberingFormat { Val = fmt },
                    new LevelText { Val = lvlText })
                { LevelIndex = 0 })
            { AbstractNumberId = abstractNumId },
            new NumberingInstance(new AbstractNumId { Val = abstractNumId }) { NumberID = numId });

    private static List<string?> ComputedNumbersForCorpus(string fileName) =>
        new ComposeDocxProjectionBuilder()
            .Build(ComposeCorpusFixtureLocator.LoadVerifiedBytes(CorpusDocPath(fileName)))
            .ParaIdMap.Select(e => e.ComputedNumber).ToList();

    // ── decimal + the interrupted-run defect (the heart of R4.5) ────────────────────────────────────

    [Fact]
    public void Compute_DecimalClausesInterruptedByHeadingBodyAndTable_ContinuesTheCountNeverRestartsAt1()
    {
        // FR-11 / project "interrupted runs": clauses 1..5 with a heading, a plain body paragraph AND a
        // table (all non-numbered) between clause 3 and clause 4. The count MUST continue 4,5 — a naive
        // <ol>-per-run reader would restart at 1. Same numId throughout (single per-numId counter).
        var labels = ComputedNumbers(
            SingleLevel(numId: 1, abstractNumId: 1, NumberFormatValues.Decimal, "%1."),
            NumberedPara("0C1A0001", "Confidentiality", numId: 1),
            NumberedPara("0C1A0002", "Term", numId: 1),
            NumberedPara("0C1A0003", "Definitions", numId: 1),
            Heading("0C1A0004", 1, "SCHEDULE A"),           // interruption 1: heading
            Para("0C1A0005", "Some intervening prose."),     // interruption 2: body paragraph
            new Table(new TableRow(new TableCell(Para("0C1A0006", "cell")))), // interruption 3: table
            NumberedPara("0C1A0007", "Remedies", numId: 1),
            NumberedPara("0C1A0008", "Miscellaneous", numId: 1));

        // Doc order: clause,clause,clause,heading,body,(table cell para),clause,clause.
        labels[0].Should().Be("1.");
        labels[1].Should().Be("2.");
        labels[2].Should().Be("3.");
        labels[3].Should().BeNull("a Heading is not a numbered paragraph");
        labels[4].Should().BeNull("a plain body paragraph is not numbered");
        labels[5].Should().BeNull("the table cell paragraph is not numbered");
        labels[6].Should().Be("4.", "the numbered run CONTINUES across the interruption — no restart at 1");
        labels[7].Should().Be("5.");
    }

    // ── numFmt formatters: letters, roman ───────────────────────────────────────────────────────────

    [Fact]
    public void Compute_LowerLetterScheme_FormatsAThroughCAndOverflowsZToAa()
    {
        // FR-11 lowerLetter incl. the z→aa overflow. 27 items: a..z (26) then aa (27).
        var paras = Enumerable.Range(1, 27)
            .Select(i => (OpenXmlElement)NumberedPara($"0A{i:X6}", $"item {i}", numId: 1)).ToArray();
        var labels = ComputedNumbers(SingleLevel(1, 1, NumberFormatValues.LowerLetter, "%1)"), paras);

        labels[0].Should().Be("a)");
        labels[1].Should().Be("b)");
        labels[2].Should().Be("c)");
        labels[25].Should().Be("z)");
        labels[26].Should().Be("aa)", "bijective base-26 overflow: the 27th letter is aa");
    }

    [Fact]
    public void Compute_UpperLetterScheme_FormatsAThroughC()
    {
        var labels = ComputedNumbers(
            SingleLevel(1, 1, NumberFormatValues.UpperLetter, "%1."),
            NumberedPara("0B1A0001", "one", numId: 1),
            NumberedPara("0B1A0002", "two", numId: 1),
            NumberedPara("0B1A0003", "three", numId: 1));

        labels.Should().Equal("A.", "B.", "C.");
    }

    [Fact]
    public void Compute_LowerRomanScheme_FormatsIThroughIvWithSubtractiveNotation()
    {
        var paras = Enumerable.Range(1, 4)
            .Select(i => (OpenXmlElement)NumberedPara($"0R{i:X6}", $"item {i}", numId: 1)).ToArray();
        var labels = ComputedNumbers(SingleLevel(1, 1, NumberFormatValues.LowerRoman, "%1."), paras);

        labels.Should().Equal("i.", "ii.", "iii.", "iv.");
    }

    [Fact]
    public void Compute_UpperRomanScheme_FormatsIThroughIv()
    {
        var paras = Enumerable.Range(1, 4)
            .Select(i => (OpenXmlElement)NumberedPara($"0S{i:X6}", $"item {i}", numId: 1)).ToArray();
        var labels = ComputedNumbers(SingleLevel(1, 1, NumberFormatValues.UpperRoman, "%1."), paras);

        labels.Should().Equal("I.", "II.", "III.", "IV.");
    }

    // ── multi-level composition + reset-deeper-on-higher-increment ───────────────────────────────────

    [Fact]
    public void Compute_MultiLevelDecimal_ComposesNestedLabelsAndResetsDeeperCountersOnHigherIncrement()
    {
        // FR-11 lvlText composition + the standard reset rule (design §4 WS-3): 1 / 1.1 / 1.1.1 / 1.1.2
        // / 1.2 / 2 / 2.1 — the level-1/2 counters reset when a new level-0 paragraph appears.
        var numbering = new Numbering(
            new AbstractNum(
                new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1" }) { LevelIndex = 0 },
                new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2" }) { LevelIndex = 1 },
                new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2.%3" }) { LevelIndex = 2 })
            { AbstractNumberId = 2 },
            new NumberingInstance(new AbstractNumId { Val = 2 }) { NumberID = 1 });

        var labels = ComputedNumbers(numbering,
            NumberedPara("0M1A0001", "Introduction", ilvl: 0, numId: 1),
            NumberedPara("0M1A0002", "Background", ilvl: 1, numId: 1),
            NumberedPara("0M1A0003", "History", ilvl: 2, numId: 1),
            NumberedPara("0M1A0004", "Current State", ilvl: 2, numId: 1),
            NumberedPara("0M1A0005", "Scope", ilvl: 1, numId: 1),
            NumberedPara("0M1A0006", "Definitions", ilvl: 0, numId: 1),
            NumberedPara("0M1A0007", "Key Terms", ilvl: 1, numId: 1));

        labels.Should().Equal("1", "1.1", "1.1.1", "1.1.2", "1.2", "2", "2.1");
    }

    [Fact]
    public void Compute_SubItemDepth_ComposesFourLevelLabelForWs4CitationGranularity()
    {
        // FR-13 granularity: WS-4's citation model resolves "4.2(b)(iii)". Mixed-format cascade:
        // level0 decimal, level1 decimal, level2 lowerLetter, level3 lowerRoman.
        var numbering = new Numbering(
            new AbstractNum(
                new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1" }) { LevelIndex = 0 },
                new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2" }) { LevelIndex = 1 },
                new Level(new NumberingFormat { Val = NumberFormatValues.LowerLetter }, new LevelText { Val = "%1.%2(%3)" }) { LevelIndex = 2 },
                new Level(new NumberingFormat { Val = NumberFormatValues.LowerRoman }, new LevelText { Val = "%1.%2(%3)(%4)" }) { LevelIndex = 3 })
            { AbstractNumberId = 3 },
            new NumberingInstance(new AbstractNumId { Val = 3 }) { NumberID = 1 });

        // Walk to 4 at level0, 2 at level1, b at level2, iii at level3 → "4.2(b)(iii)".
        var body = new List<OpenXmlElement>();
        for (var i = 0; i < 4; i++) body.Add(NumberedPara($"0D0{i:X5}", $"top {i}", ilvl: 0, numId: 1)); // →4
        body.Add(NumberedPara("0D100001", "s1", ilvl: 1, numId: 1)); // 4.1
        body.Add(NumberedPara("0D100002", "s2", ilvl: 1, numId: 1)); // 4.2
        body.Add(NumberedPara("0D200001", "a", ilvl: 2, numId: 1));  // 4.2(a)
        body.Add(NumberedPara("0D200002", "b", ilvl: 2, numId: 1));  // 4.2(b)
        body.Add(NumberedPara("0D300001", "i", ilvl: 3, numId: 1));  // 4.2(b)(i)
        body.Add(NumberedPara("0D300002", "ii", ilvl: 3, numId: 1)); // 4.2(b)(ii)
        body.Add(NumberedPara("0D300003", "iii", ilvl: 3, numId: 1)); // 4.2(b)(iii)

        var labels = ComputedNumbers(numbering, body.ToArray());

        labels[^1].Should().Be("4.2(b)(iii)", "the composed label must reach sub-item depth for WS-4 citations");
    }

    // ── w:isLgl (legal) — forces decimal for EVERY inserted level reference ──────────────────────────

    [Fact]
    public void Compute_LegalNumbering_ForcesDecimalForAllInsertedLevelReferencesInThatLabel()
    {
        // FR-11 w:isLgl: level0 is upperRoman, level1 is decimal WITH w:isLgl. The level-1 label inserts
        // its parent (%1) — isLgl forces THAT reference to decimal too, so "I" becomes "1": label "1.1",
        // NOT "I.1". This is the "wrong legal number is worse than absent" case the flagship guards.
        var numbering = new Numbering(
            new AbstractNum(
                new Level(new NumberingFormat { Val = NumberFormatValues.UpperRoman }, new LevelText { Val = "%1" }) { LevelIndex = 0 },
                new Level(
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1.%2" },
                    new IsLegalNumberingStyle())
                { LevelIndex = 1 })
            { AbstractNumberId = 4 },
            new NumberingInstance(new AbstractNumId { Val = 4 }) { NumberID = 1 });

        var labels = ComputedNumbers(numbering,
            NumberedPara("0L1A0001", "Article", ilvl: 0, numId: 1),
            NumberedPara("0L1A0002", "Legal sub", ilvl: 1, numId: 1));

        labels[0].Should().Be("I", "level 0 is upperRoman → I");
        labels[1].Should().Be("1.1", "w:isLgl on level 1 forces the inserted upperRoman parent reference to decimal");
    }

    // ── w:startOverride ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_StartOverride_SeedsTheNumIdsFirstCounterValueThenIncrementsNormally()
    {
        // FR-11 w:lvlOverride/w:startOverride: this numId restarts level 0 at 5 (independent of the
        // abstractNum's own w:start=1). First clause → 5., then 6., 7.
        var numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1." })
                { LevelIndex = 0 })
            { AbstractNumberId = 5 },
            new NumberingInstance(
                new AbstractNumId { Val = 5 },
                new LevelOverride(new StartOverrideNumberingValue { Val = 5 }) { LevelIndex = 0 })
            { NumberID = 1 });

        var labels = ComputedNumbers(numbering,
            NumberedPara("0O1A0001", "a", numId: 1),
            NumberedPara("0O1A0002", "b", numId: 1),
            NumberedPara("0O1A0003", "c", numId: 1));

        labels.Should().Equal("5.", "6.", "7.");
    }

    // ── multiple w:num over ONE w:abstractNum: instance-scoped counters (ECMA-376) — task-035/DEF-03 ──
    // The golden corpus never exercised this (every corpus doc uses a single numId per abstractNum), yet
    // the WRITE side (ComposeDocumentRenderer) authors exactly this shape: every ordered list broken by a
    // non-list block gets a FRESH w:num instance + w:startOverride=1 so it "Restart[s] at 1". These two
    // tests permanently cover that blind spot — the read-side counter is keyed by (numId, level), NOT the
    // shared (abstractNumId, level), so two instances keep INDEPENDENT counters per ECMA-376.

    [Fact]
    public void Compute_TwoNumIdsSharingOneAbstractNumWithStartOverride_SecondListRestartsAt1()
    {
        // The exact DEF-03 shape at the engine unit level: numId 1 and numId 2 both reference abstractNum 8
        // ("%1." decimal, start=1); numId 2 carries a level-0 w:startOverride=1 (the standard "Restart at 1"
        // idiom the renderer emits for the second list). Two clauses on numId 1, an intervening paragraph,
        // then two clauses on numId 2. The second list MUST read "1.", "2." — NOT "3.", "4." (the pre-fix
        // (abstractNumId, level)-keyed engine continued list 1's count into list 2).
        var numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1." })
                { LevelIndex = 0 })
            { AbstractNumberId = 8 },
            new NumberingInstance(new AbstractNumId { Val = 8 }) { NumberID = 1 },
            new NumberingInstance(
                new AbstractNumId { Val = 8 },
                new LevelOverride(new StartOverrideNumberingValue { Val = 1 }) { LevelIndex = 0 })
            { NumberID = 2 });

        var labels = ComputedNumbers(numbering,
            NumberedPara("0P1A0001", "First A", numId: 1),
            NumberedPara("0P1A0002", "First B", numId: 1),
            Para("0P1A0003", "Some intervening prose that breaks the ordered run."),
            NumberedPara("0P2A0001", "Second A", numId: 2),
            NumberedPara("0P2A0002", "Second B", numId: 2));

        labels.Should().Equal(new string?[] { "1.", "2.", null, "1.", "2." },
            "a second w:num instance sharing the abstractNum keeps an INDEPENDENT counter (ECMA-376) and " +
            "its w:startOverride=1 restarts the list — it must NOT continue the first list's count");
    }

    [Fact]
    public void Compute_TwoIndependentNumIdsInterleaved_EachMaintainsItsOwnCounterIndependently()
    {
        // ECMA-376 instance-scoping proven without ANY startOverride: numId 1 and numId 2 both reference
        // abstractNum 8. Two items on numId 1, two on numId 2, then RESUME numId 1. numId 2 must start
        // fresh at "1." (its own abstractNum start, independent counter), while numId 1 CONTINUES to "3."
        // after the numId-2 interruption — a single test that guards BOTH the restart (independence) and
        // the continue-within-a-single-numId (no false restart) behaviors the fix must satisfy together.
        var numbering = new Numbering(
            new AbstractNum(
                new Level(
                    new StartNumberingValue { Val = 1 },
                    new NumberingFormat { Val = NumberFormatValues.Decimal },
                    new LevelText { Val = "%1." })
                { LevelIndex = 0 })
            { AbstractNumberId = 8 },
            new NumberingInstance(new AbstractNumId { Val = 8 }) { NumberID = 1 },
            new NumberingInstance(new AbstractNumId { Val = 8 }) { NumberID = 2 });

        var labels = ComputedNumbers(numbering,
            NumberedPara("0Q1A0001", "list1 a", numId: 1),
            NumberedPara("0Q1A0002", "list1 b", numId: 1),
            NumberedPara("0Q2A0001", "list2 a", numId: 2),
            NumberedPara("0Q2A0002", "list2 b", numId: 2),
            NumberedPara("0Q1A0003", "list1 c", numId: 1));

        labels.Should().Equal(new string?[] { "1.", "2.", "1.", "2.", "3." },
            "numId 2 keeps its own counter (restarts at 1) while numId 1 continues its own count (to 3) " +
            "across the interleaving — counters are instance-scoped, never shared via the abstractNum");
    }

    // ── style-linked headings (FR-12) ───────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_StyleLinkedHeadings_NumbersHeadingsByStyleExactlyLikeDirectNumPr()
    {
        // FR-12: numId/ilvl live on the Heading1/Heading2 STYLES (styles.xml), not on any paragraph.
        // The engine must count style-linked headings identically to direct w:numPr — H1 → 1, 2; the H2
        // under heading 2 → 2.1, 2.2. Proves heading-style numbers are no longer dropped.
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var np = main.AddNewPart<NumberingDefinitionsPart>();
            np.Numbering = new Numbering(
                new AbstractNum(
                    new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1" }) { LevelIndex = 0 },
                    new Level(new NumberingFormat { Val = NumberFormatValues.Decimal }, new LevelText { Val = "%1.%2" }) { LevelIndex = 1 })
                { AbstractNumberId = 9 },
                new NumberingInstance(new AbstractNumId { Val = 9 }) { NumberID = 4 });
            np.Numbering.Save();

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new Style(new StyleParagraphProperties(new NumberingProperties(
                    new NumberingLevelReference { Val = 0 }, new NumberingId { Val = 4 })))
                { Type = StyleValues.Paragraph, StyleId = "Heading1" },
                new Style(new StyleParagraphProperties(new NumberingProperties(
                    new NumberingLevelReference { Val = 1 }, new NumberingId { Val = 4 })))
                { Type = StyleValues.Paragraph, StyleId = "Heading2" });
            stylesPart.Styles.Save();

            main.Document = new Document(new Body(
                Heading("0H1A0001", 1, "Recitals"),          // 1
                Heading("0H1A0002", 1, "Definitions"),        // 2
                Heading("0H1A0003", 2, "Purpose"),            // 2.1
                Heading("0H1A0004", 2, "Confidentiality")));  // 2.2
            main.Document.Save();
        }
        var labels = new ComposeDocxProjectionBuilder().Build(ms.ToArray()).ParaIdMap.Select(e => e.ComputedNumber).ToList();

        labels.Should().Equal("1", "2", "2.1", "2.2");
    }

    // ── golden labels over the REAL corpus exemplars (NFR-02 acceptance, per-doc) ────────────────────

    [Fact]
    public void Compute_OverNdaInterruptedClausesCorpusDoc_ProducesContinuousGoldenLabels1Through6()
    {
        // corpus-manifest.md row 9: clauses continue 1..6 across the heading/body/table interruption.
        var labels = ComputedNumbersForCorpus("nda-interrupted-clauses.docx");
        labels[2].Should().Be("1.");
        labels[3].Should().Be("2.");
        labels[4].Should().Be("3.");
        labels[12].Should().Be("4.", "post-interruption clause 4 CONTINUES the count (same numId)");
        labels[13].Should().Be("5.");
        labels[14].Should().Be("6.");
    }

    [Fact]
    public void Compute_OverHeadingStyleNumberingCorpusDoc_RendersTheFr12Example4Point2()
    {
        // corpus-manifest.md row 10 — the literal FR-12 acceptance example "4.2 Confidentiality".
        var labels = ComputedNumbersForCorpus("heading-style-numbering.docx");
        labels[0].Should().Be("1");
        labels[6].Should().Be("4");
        labels[7].Should().Be("4.1");
        labels[9].Should().Be("4.2", "the FR-12 acceptance example — style-linked heading numbering resolved");
    }

    [Fact]
    public void Compute_OverMultilevelCorpusDoc_RendersGolden1Point1Point1Cascade()
    {
        // corpus-manifest.md row 11.
        var labels = ComputedNumbersForCorpus("multilevel-1-1-1.docx");
        labels[1].Should().Be("1.");
        labels[2].Should().Be("1.1.");
        labels[3].Should().Be("1.1.1.");
        labels[4].Should().Be("1.1.2.");
        labels[5].Should().Be("1.2.");
        labels[6].Should().Be("2.");
        labels[7].Should().Be("2.1.");
    }

    // ── determinism (NFR-06) ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Compute_OverEveryNumberingExemplar_ProducesIdenticalLabelsAcrossTwoRuns()
    {
        // NFR-06: identical inputs → identical labels, no reliance on render/hash order. Run the whole
        // projection twice over each corpus exemplar and compare the full computed-label sequence.
        foreach (var fileName in new[]
        {
            "nda-interrupted-clauses.docx", "heading-style-numbering.docx",
            "multilevel-1-1-1.docx", "line-numbered-pleading.docx",
        })
        {
            var bytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(CorpusDocPath(fileName));
            var run1 = new ComposeDocxProjectionBuilder().Build(bytes).ParaIdMap.Select(e => e.ComputedNumber).ToList();
            var run2 = new ComposeDocxProjectionBuilder().Build(bytes).ParaIdMap.Select(e => e.ComputedNumber).ToList();
            run2.Should().Equal(run1, $"numbering computation must be deterministic for {fileName} (NFR-06)");
        }
    }
}
