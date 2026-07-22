// FR-04 / FR-11 / D5 (spaarkeai-compose-r4 task 030) — behavior tests for the ComposeShadowPatchEngine,
// the SINGLE unified byte-author. Each test names a concrete production behavior that breaks if deleted
// (maintain-class per tests/CLAUDE.md): an interior edit/redline/comment lands at the intended
// w14:paraId + run-local offset with ZERO write-path text-search; RunProperties survive the split;
// native w:ins/w:del (w:delText) / w:comment are emitted per EDGE-1..4; an unknown paraId is REFUSED
// (never applied to the wrong node); the editor-visible run flatten (NOT raw Elements<Run>()) is the
// index space; a structural op / a tracked-change reconciliation are refused at their seams.
//
// Pure domain test (no mocks, no HTTP, no DI): builds a synthetic .docx in-memory, applies the engine,
// reopens, and asserts on the OOXML DOM — the through-the-wire seam proof is task 034. Banned-pattern
// clean (tests/CLAUDE.md B1-B17): no Mock<HttpMessageHandler>, no DI-registration, no ctor-null, no
// getter/mirror — these assert byte-author behavior a consumer (Word) notices.

using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeShadowPatchEngineTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 22, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeShadowPatchEngine _engine = new();

    // ── insertText ────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: an interior insertText lands a native w:ins at the paraId + run-local offset")]
    public void Apply_InteriorInsertText_EmitsInsertedRunAtAnchor()
    {
        // Arrange — one paragraph, one run "The method of filing"
        var docx = Pack(Para("712269E5", TextRun("The method of filing")));
        var log = Log(new InsertTextOperation
        {
            ParaId = "712269E5",
            At = new ComposeRunPoint(0, 10), // after "The method"
            Text = " AMENDED",
        });

        // Act
        var result = _engine.Apply(docx, log, author: "Tester", timestamp: When);

        // Assert — native w:ins with the inserted text, anchored at the split boundary (no text-search)
        var para = ParagraphById(result, "712269E5");
        var ins = para.Descendants<InsertedRun>().Single();
        RunText(ins.GetFirstChild<Run>()!).Should().Be(" AMENDED");
        ins.Author!.Value.Should().Be("Tester");

        // The original run was split exactly at offset 10 and the w:ins sits at that boundary.
        VisibleText(para).Should().Be("The method AMENDED of filing");
    }

    [Fact(DisplayName = "Apply: insertText carries its inline marks onto the inserted run properties")]
    public void Apply_InsertTextWithMarks_AppliesMarksToInsertedRun()
    {
        var docx = Pack(Para("AAAA0001", TextRun("plain")));
        var log = Log(new InsertTextOperation
        {
            ParaId = "AAAA0001",
            At = new ComposeRunPoint(0, 5),
            Text = "BOLD",
            Marks = new[] { ComposeMarkType.Bold, ComposeMarkType.Italic },
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        var ins = ParagraphById(result, "AAAA0001").Descendants<InsertedRun>().Single();
        var insRun = ins.GetFirstChild<Run>()!;
        insRun.RunProperties!.Bold.Should().NotBeNull();
        insRun.RunProperties!.Italic.Should().NotBeNull();
    }

    // ── deleteRange (w:del + w:delText, EDGE-4) ─────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: deleteRange emits a native w:del with w:delText over the exact span")]
    public void Apply_DeleteRange_EmitsDeletedRunWithDelTextOverExactSpan()
    {
        // "asset information " occupies offsets [0,18) of a longer run.
        var docx = Pack(Para("410C9E5F", TextRun("asset information stays")));
        var log = Log(new DeleteRangeOperation
        {
            ParaId = "410C9E5F",
            Range = new ComposeRunRange(new ComposeRunPoint(0, 0), new ComposeRunPoint(0, 18)),
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        var para = ParagraphById(result, "410C9E5F");
        var del = para.Descendants<DeletedRun>().Single();
        // EDGE-4: the deleted text is w:delText (DeletedText), never w:t.
        del.Descendants<DeletedText>().Single().Text.Should().Be("asset information ");
        del.Descendants<Text>().Should().BeEmpty();
        // The surviving (non-deleted) text is exactly the remainder.
        SettledText(para).Should().Be("stays");
    }

    // ── run split preserves RunProperties ───────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: a split of a formatted run preserves RunProperties on both halves")]
    public void Apply_SplitOfFormattedRun_PreservesRunPropertiesOnBothHalves()
    {
        // A single BOLD run; insert into its middle so it splits into two halves.
        var docx = Pack(Para("B01D0001", TextRun("HelloWorld", bold: true)));
        var log = Log(new InsertTextOperation
        {
            ParaId = "B01D0001",
            At = new ComposeRunPoint(0, 5),
            Text = "-",
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        var para = ParagraphById(result, "B01D0001");
        // The two settled halves ("Hello" and "World") must BOTH still be bold.
        var settledRuns = para.Elements<Run>().Where(r => r.GetFirstChild<Text>() is not null).ToList();
        settledRuns.Should().HaveCount(2);
        settledRuns.Should().OnlyContain(r => r.RunProperties != null && r.RunProperties.Bold != null);
    }

    // ── replaceRange (w:ins + w:del) ────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: replaceRange emits an insertion AND a deletion (ins before del)")]
    public void Apply_ReplaceRange_EmitsInsertionThenDeletion()
    {
        var docx = Pack(Para("4EF10001", TextRun("old value")));
        var log = Log(new ReplaceRangeOperation
        {
            ParaId = "4EF10001",
            Range = new ComposeRunRange(new ComposeRunPoint(0, 0), new ComposeRunPoint(0, 3)), // "old"
            Text = "new",
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        var para = ParagraphById(result, "4EF10001");
        RunText(para.Descendants<InsertedRun>().Single().GetFirstChild<Run>()!).Should().Be("new");
        para.Descendants<DeletedRun>().Single().Descendants<DeletedText>().Single().Text.Should().Be("old");
    }

    // ── setMark ─────────────────────────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: setMark(Bold) over a range bolds exactly the covered runs")]
    public void Apply_SetMarkBoldOverRange_BoldsCoveredRunsOnly()
    {
        var docx = Pack(Para("5E140001", TextRun("make this bold")));
        var log = Log(new SetMarkOperation
        {
            ParaId = "5E140001",
            Range = new ComposeRunRange(new ComposeRunPoint(0, 5), new ComposeRunPoint(0, 9)), // "this"
            Mark = ComposeMarkType.Bold,
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        var para = ParagraphById(result, "5E140001");
        var boldRun = para.Elements<Run>().Single(r => RunText(r) == "this");
        boldRun.RunProperties!.Bold.Should().NotBeNull();
        para.Elements<Run>().Where(r => RunText(r) is "make " or " bold")
            .Should().OnlyContain(r => r.RunProperties == null || r.RunProperties.Bold == null);
    }

    // ── comment (w:comment, EDGE-1) ─────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: an anchored comment emits w:comment + matching comment-range marks (no text-search)")]
    public void Apply_AnchoredComment_EmitsCommentAndRangeMarks()
    {
        var docx = Pack(Para("C0FFEE01", TextRun("comment on this clause")));
        var comment = new ComposeAnchoredComment
        {
            ParaId = "C0FFEE01",
            Range = new ComposeRunRange(new ComposeRunPoint(0, 11), new ComposeRunPoint(0, 15)), // "this"
            CommentText = "Reviewer note",
            Author = "Alice Reviewer",
            Date = When,
        };

        var result = _engine.Apply(docx, EmptyLog(), comments: new[] { comment });

        using var doc = OpenRead(result);
        var body = doc.MainDocumentPart!.Document!.Body!;
        var startId = body.Descendants<CommentRangeStart>().Single().Id!.Value;
        var endId = body.Descendants<CommentRangeEnd>().Single().Id!.Value;
        var refId = body.Descendants<CommentReference>().Single().Id!.Value;
        startId.Should().Be(endId).And.Be(refId); // three-part comment invariant, matching w:id

        var commentsPart = doc.MainDocumentPart.GetPartsOfType<WordprocessingCommentsPart>().Single();
        var stored = commentsPart.Comments!.Elements<Comment>().Single();
        stored.Id!.Value.Should().Be(startId);
        stored.InnerText.Should().Be("Reviewer note");
        stored.Author!.Value.Should().Be("Alice Reviewer");
    }

    [Fact(DisplayName = "Apply: a comment and a deletion on the same span compose (EDGE-1 comment-before-trackchange)")]
    public void Apply_CommentAndDeletionOnSameSpan_BothEmittedWithoutError()
    {
        var docx = Pack(Para("ED010001", TextRun("delete and note me")));
        var range = new ComposeRunRange(new ComposeRunPoint(0, 0), new ComposeRunPoint(0, 6)); // "delete"
        var comment = new ComposeAnchoredComment
        {
            ParaId = "ED010001", Range = range, CommentText = "why?", Author = "Bob", Date = When,
        };
        var log = Log(new DeleteRangeOperation { ParaId = "ED010001", Range = range });

        var act = () => _engine.Apply(docx, log, comments: new[] { comment }, timestamp: When);

        var result = act.Should().NotThrow().Subject;
        var para = ParagraphById(result, "ED010001");
        para.Descendants<CommentRangeStart>().Should().ContainSingle();
        para.Descendants<DeletedRun>().Should().ContainSingle();
    }

    // ── editor-visible flatten alignment (NOT raw Elements<Run>()) ──────────────────────────────────

    [Fact(DisplayName = "Apply: runIndex addresses the editor-visible flatten (descends into w:hyperlink), not raw Elements<Run>()")]
    public void Apply_RunIndexOverHyperlinkFlatten_LandsAfterTheHyperlink()
    {
        // Editor flatten = [ "AB"(0), "CD"(1, inside hyperlink), "EF"(2) ]. Raw para.Elements<Run>()
        // sees only [AB, EF] (CD is nested), so runIndex 2 would be OUT OF RANGE with the raw enumeration
        // — it is valid ("EF") only against the editor-visible flatten this engine indexes.
        var hyperlink = new Hyperlink(TextRun("CD")) { Anchor = "bm" };
        var docx = Pack(new Paragraph(TextRun("AB"), hyperlink, TextRun("EF")) { ParagraphId = "F1A70001" });

        var log = Log(new InsertTextOperation
        {
            ParaId = "F1A70001",
            At = new ComposeRunPoint(2, 0), // start of the "EF" run in the flatten
            Text = "X",
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        var para = ParagraphById(result, "F1A70001");
        VisibleText(para).Should().Be("ABCDXEF"); // inserted right before EF, i.e. AFTER the hyperlink
        para.Descendants<InsertedRun>().Single().GetFirstChild<Run>()!.InnerText.Should().Be("X");
    }

    // ── NEGATIVE: unknown paraId is refused, never mis-applied ───────────────────────────────────────

    [Fact(DisplayName = "Apply: an operation with an unknown paraId is REFUSED (never applied to the wrong node)")]
    public void Apply_UnknownParaId_IsRefused()
    {
        var docx = Pack(Para("AAAA0001", TextRun("only paragraph")));
        var log = Log(new InsertTextOperation
        {
            ParaId = "DEADBEEF", // not present
            At = new ComposeRunPoint(0, 0),
            Text = "x",
        });

        var act = () => _engine.Apply(docx, log, timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.ParagraphNotFound);
    }

    // ── no-op passthrough (byte-identical) ──────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: an empty operation log with no comments returns the retained bytes byte-identical")]
    public void Apply_EmptyLog_ReturnsRetainedBytesByteIdentical()
    {
        var docx = Pack(Para("AAAA0001", TextRun("unchanged")));

        var result = _engine.Apply(docx, EmptyLog());

        result.Should().BeSameAs(docx); // true no-op passthrough — never re-serialized
    }

    // ── seam: setBlockAttr is outside the four structural ops ───────────────────────────────────────

    [Fact(DisplayName = "Apply: a setBlockAttr op is refused at its own (non-task-031) seam")]
    public void Apply_SetBlockAttrOp_RefusedAtSeam()
    {
        var docx = Pack(Para("AAAA0001", TextRun("para")));
        var log = Log(new SetBlockAttrOperation { ParaId = "AAAA0001", Attr = ComposeBlockAttr.Alignment, Value = "Center" });

        var act = () => _engine.Apply(docx, log, timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.StructuralOpNotYetImplemented);
    }

    // ── structural: splitParagraph (inserted para-mark) ─────────────────────────────────────────────

    [Fact(DisplayName = "Apply: splitParagraph divides at the offset, moving the tail to a new paraId with an inserted para-mark")]
    public void Apply_SplitParagraph_DividesAndInsertsParaMarkOnLeadingParagraph()
    {
        var docx = Pack(Para("5111A001", TextRun("HelloWorld")));
        var log = Log(new SplitParagraphOperation
        {
            ParaId = "5111A001",
            At = new ComposeRunPoint(0, 5), // after "Hello"
            NewParaId = "5111A002",
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        var leading = ParagraphById(result, "5111A001");
        var trailing = ParagraphById(result, "5111A002");
        SettledText(leading).Should().Be("Hello");
        SettledText(trailing).Should().Be("World");
        // The NEW paragraph mark terminating the leading paragraph is a tracked INSERTION (w:pPr/w:rPr/w:ins).
        leading.ParagraphProperties!.ParagraphMarkRunProperties!.GetFirstChild<Inserted>().Should().NotBeNull();
        // The trailing paragraph keeps a clean (non-inserted) mark.
        trailing.ParagraphProperties?.ParagraphMarkRunProperties?.GetFirstChild<Inserted>().Should().BeNull();
    }

    // ── structural: mergeParagraph (THE hardest edge — para-mark deletion) ──────────────────────────

    [Fact(DisplayName = "Apply: mergeParagraph deletes the TARGET paragraph mark (w:pPr/w:rPr/w:del), joining the two")]
    public void Apply_MergeParagraph_DeletesTargetParagraphMarkGlyph()
    {
        var docx = Pack(
            Para("A0000001", TextRun("first")),
            Para("A0000002", TextRun("second")));
        var log = Log(new MergeParagraphOperation { ParaId = "A0000002", TargetParaId = "A0000001" });

        var result = _engine.Apply(docx, log, timestamp: When);

        // The paragraph-mark deletion is a w:del on the para-mark glyph in the TARGET's w:pPr/w:rPr — the
        // bridge-prior-art finding #6 representation, NOT a naive removal of the w:p node.
        var target = ParagraphById(result, "A0000001");
        target.ParagraphProperties!.ParagraphMarkRunProperties!.GetFirstChild<Deleted>().Should().NotBeNull();
        // Both paragraphs still physically present (tracked change — Word joins them only on accept).
        using var doc = OpenRead(result);
        doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Count(p => p.ParagraphId?.Value is "A0000001" or "A0000002").Should().Be(2);
    }

    // ── structural: insertParagraph ─────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: insertParagraph adds a new empty paragraph (inserted mark) after the reference")]
    public void Apply_InsertParagraph_AddsNewParagraphWithInsertedMark()
    {
        var docx = Pack(Para("B0000001", TextRun("only")));
        var log = Log(new InsertParagraphOperation
        {
            ParaId = "B0000001",
            NewParaId = "B0000002",
            Position = ComposeParagraphPosition.After,
        });

        var result = _engine.Apply(docx, log, timestamp: When);

        using var doc = OpenRead(result);
        var paras = doc.MainDocumentPart!.Document!.Body!.Elements<Paragraph>().ToList();
        paras.Should().HaveCount(2);
        paras[0].ParagraphId!.Value.Should().Be("B0000001");
        paras[1].ParagraphId!.Value.Should().Be("B0000002");
        paras[1].ParagraphProperties!.ParagraphMarkRunProperties!.GetFirstChild<Inserted>().Should().NotBeNull();
    }

    // ── structural: deleteParagraph (strike runs + para-mark; closes task 023 server-side gap) ───────

    [Fact(DisplayName = "Apply: deleteParagraph strikes the runs (w:del) AND marks the paragraph mark deleted")]
    public void Apply_DeleteParagraph_StrikesRunsAndParagraphMark()
    {
        var docx = Pack(Para("D0000001", TextRun("goodbye clause")));
        var log = Log(new DeleteParagraphOperation { ParaId = "D0000001" });

        var result = _engine.Apply(docx, log, timestamp: When);

        var para = ParagraphById(result, "D0000001");
        // The run content is struck as a tracked deletion.
        para.Descendants<DeletedRun>().Single().Descendants<DeletedText>().Single().Text.Should().Be("goodbye clause");
        SettledText(para).Should().BeEmpty();
        // AND the paragraph mark is marked deleted so the whole paragraph vanishes on accept (the {paraId,text:''}
        // sentinel semantic — closes task 023's coverage gap server-side).
        para.ParagraphProperties!.ParagraphMarkRunProperties!.GetFirstChild<Deleted>().Should().NotBeNull();
    }

    // ── structural: sequenced LAST — text-op anchors unaffected by a structural change ──────────────

    [Fact(DisplayName = "Apply: structural ops run LAST — a text op earlier in log order lands against the pre-split paragraph")]
    public void Apply_StructuralOpsSequencedLast_TextAnchorUnaffected()
    {
        // Log ORDER puts the split FIRST, the insertText SECOND. If applied in log order the split would move
        // runs and shift the insertText's offset. Sequencing structural LAST means the insertText lands against
        // the original single paragraph, THEN the split happens — proving text-op anchors are unaffected.
        var docx = Pack(Para("5E000001", TextRun("HelloWorldFoo")));
        var log = Log(
            new SplitParagraphOperation { ParaId = "5E000001", At = new ComposeRunPoint(0, 5), NewParaId = "5E000002" },
            new InsertTextOperation { ParaId = "5E000001", At = new ComposeRunPoint(0, 10), Text = "X" });

        var result = _engine.Apply(docx, log, timestamp: When);

        var leading = ParagraphById(result, "5E000001");
        var trailing = ParagraphById(result, "5E000002");
        // insertText landed at editor offset 10 (after "HelloWorld") of the ORIGINAL paragraph; the split at 5
        // then divides "Hello" | "WorldXFoo". The inserted "X" is in the trailing paragraph.
        SettledText(leading).Should().Be("Hello");
        VisibleText(trailing).Should().Be("WorldXFoo");
        trailing.Descendants<InsertedRun>().Single().GetFirstChild<Run>()!.InnerText.Should().Be("X");
    }

    // ── NEGATIVE: a merge across a table boundary is refused, not corrupted ──────────────────────────

    [Fact(DisplayName = "Apply: a mergeParagraph whose target is separated from the source by a table is REFUSED")]
    public void Apply_MergeParagraphAcrossTable_IsRefused()
    {
        // Body: para(target) , table , para(source). The target is NOT the immediate predecessor of the source.
        var target = Para("70000001", TextRun("before table"));
        var table = new Table(
            new TableRow(new TableCell(new Paragraph(TextRun("cell")))));
        var source = Para("70000002", TextRun("after table"));
        var docx = PackElements(target, table, source);

        var log = Log(new MergeParagraphOperation { ParaId = "70000002", TargetParaId = "70000001" });

        var act = () => _engine.Apply(docx, log, timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.StructuralOperationRefused);
    }

    // ── escalation boundary: a pre-existing tracked change is refused, not guessed ───────────────────

    [Fact(DisplayName = "Apply: an edit resolving inside a pre-existing w:ins is refused (escalation boundary, not guessed)")]
    public void Apply_EditInsidePreExistingTrackedChange_IsRefused()
    {
        // A paragraph whose only editor-visible run is inside a pre-existing <w:ins>.
        var existingIns = new InsertedRun(TextRun("already inserted")) { Id = "5", Author = "Prior", Date = When.UtcDateTime };
        var docx = Pack(new Paragraph(existingIns) { ParagraphId = "17150001" });
        var log = Log(new InsertTextOperation
        {
            ParaId = "17150001",
            At = new ComposeRunPoint(0, 7), // inside the pre-existing inserted run
            Text = "x",
        });

        var act = () => _engine.Apply(docx, log, timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.TrackedChangeReconciliationUnsupported);
    }

    // ── unsupported schema version ──────────────────────────────────────────────────────────────────

    [Fact(DisplayName = "Apply: an operation log with an unsupported schema version is refused")]
    public void Apply_UnsupportedSchemaVersion_IsRefused()
    {
        var docx = Pack(Para("AAAA0001", TextRun("para")));
        var log = new ComposeOperationLog
        {
            SchemaVersion = "compose-ops-v999",
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation { ParaId = "AAAA0001", At = new ComposeRunPoint(0, 0), Text = "x" },
            },
        };

        var act = () => _engine.Apply(docx, log, timestamp: When);

        act.Should().Throw<ComposePatchException>()
            .Which.Kind.Should().Be(ComposePatchErrorKind.UnsupportedSchemaVersion);
    }

    // ── helpers ─────────────────────────────────────────────────────────────────────────────────────

    private static ComposeOperationLog EmptyLog() => new();

    private static ComposeOperationLog Log(params ComposeOperation[] ops) => new() { Operations = ops };

    private static Run TextRun(string text, bool bold = false)
    {
        var run = new Run();
        if (bold)
        {
            var rpr = new RunProperties { Bold = new Bold() };
            run.AppendChild(rpr);
        }

        run.AppendChild(new Text(text) { Space = SpaceProcessingModeValues.Preserve });
        return run;
    }

    private static Paragraph Para(string paraId, params Run[] runs)
    {
        var p = new Paragraph { ParagraphId = paraId };
        foreach (var run in runs)
        {
            p.AppendChild(run);
        }

        return p;
    }

    private static byte[] Pack(params Paragraph[] paragraphs) =>
        PackElements(paragraphs.Cast<OpenXmlElement>().ToArray());

    private static byte[] PackElements(params OpenXmlElement[] blockLevelElements)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(blockLevelElements));
            main.Document.Save();
        }

        return ms.ToArray();
    }

    private static WordprocessingDocument OpenRead(byte[] bytes) =>
        WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);

    private static Paragraph ParagraphById(byte[] bytes, string paraId)
    {
        using var doc = OpenRead(bytes);
        var para = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));
        // Detach a deep clone so the returned node outlives the disposed package.
        return (Paragraph)para.CloneNode(true);
    }

    private static string RunText(Run run) => run.GetFirstChild<Text>()?.Text ?? string.Empty;

    /// <summary>All editor-visible text (settled + inserted + deleted), in document order.</summary>
    private static string VisibleText(Paragraph para) => DocumentOrderText(para, includeDeleted: true);

    private static string SettledText(Paragraph para) => DocumentOrderText(para, includeDeleted: false);

    private static string DocumentOrderText(OpenXmlElement element, bool includeDeleted)
    {
        var sb = new StringBuilder();
        Walk(element, sb, includeDeleted);
        return sb.ToString();

        static void Walk(OpenXmlElement el, StringBuilder acc, bool incDel)
        {
            foreach (var child in el.Elements())
            {
                switch (child)
                {
                    case Text t:
                        acc.Append(t.Text);
                        break;
                    case DeletedText dt when incDel:
                        acc.Append(dt.Text);
                        break;
                    case DeletedText:
                        break;
                    default:
                        Walk(child, acc, incDel);
                        break;
                }
            }
        }
    }
}
