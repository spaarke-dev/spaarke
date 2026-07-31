// Task 055 (spaarkeai-compose-r5) — engine-level seam proof for the ROBUST paragraph-relative anchor
// (ComposeRunPoint.ParaOffset), the owner-approved ADDITIVE fix for the write-path 422:
//   ComposePatchException "run-local offset 45 is out of range for runIndex 3 ... (run editor length 9)".
//
// ROOT CAUSE (confirmed, not re-investigated): TipTap MERGES Word's fine-grained runs (a real paragraph had
// 74 OOXML <w:r> runs — run3="tincidunt"=9 chars, run73=45 chars). The client maps a paragraph-relative char
// offset k → (runIndex, offset) over the EDITOR's MERGED run list, so editor-run-index ≠ OOXML-run-index; the
// server resolves against OOXML's real runs → offset out of range → 422. The (paraId, runIndex, offset) anchor
// is not stable across the TipTap↔OOXML boundary.
//
// THE FIX (additive): the op carries the paragraph-relative offset k; the server, when ParaOffset is present,
// resolves the real OOXML (run, run-local-offset) by walking THAT paragraph's editor-run flatten to k. When
// absent, behaves EXACTLY as before (backward-compatible).
//
// This seam drives the production ComposeShadowPatchEngine.Apply() surface directly (byte[] in / byte[] out) —
// no client, no mock. Banned-pattern clean: no Mock<HttpMessageHandler>, no DI-registration, no ctor-null test.

using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Services.Compose.Operations;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeParaOffsetAnchorSeamTests
{
    private static readonly DateTimeOffset When = new(2026, 7, 30, 0, 0, 0, TimeSpan.Zero);
    private readonly ComposeShadowPatchEngine _engine = new();

    // A single paraId'd paragraph split into MANY single-word OOXML runs (mirrors Word's proofing/rsid split).
    // Run index 3 = "tincidunt" (9 chars); the FINAL run = a distinct 45-char run — the exact 74-run / offset-45
    // shape of the failing doc, scaled to 21 runs.
    private const string ParaId = "421E7EDC";
    private const string FinalRunText = "Make changes to the document in Word for Web!"; // 45 chars
    private const string RunAtIndex3 = "tincidunt";                                        // 9 chars

    private static readonly string[] RunWords =
    {
        "Lorem", "ipsum", "dolor", RunAtIndex3, "sit", "amet", "consectetur", "adipiscing", "elit", "sed",
        "do", "eiusmod", "tempor", "incididunt", "ut", "labore", "et", "dolore", "magna", "aliqua", FinalRunText,
    };

    // ── The fix: an edit at the end of the final long run resolves via paraOffset and lands correctly ────────
    [Fact]
    public void InsertViaParaOffset_AtEndOfFinalRunInManyRunParagraph_LandsAtCorrectPositionAndPersists()
    {
        var original = BuildManyRunParagraphDocx();
        var fullText = string.Concat(RunWords);
        var k = fullText.Length; // paragraph-relative offset to the END of the final run (= end of paragraph)

        // The SAME user gesture carries BOTH the legacy (runIndex=3, offset=45) anchor AND the robust paraOffset.
        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation
                {
                    ParaId = ParaId,
                    At = new ComposeRunPoint(RunIndex: 3, Offset: 45, ParaOffset: k),
                    Text = "[FIX]",
                },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        var (editorText, insertedText) = ReadParagraph(patched, ParaId);
        editorText.Should().Be(fullText + "[FIX]",
            "the paraOffset anchor must resolve to the END of the final run — the insert lands right after \"Web!\"");
        editorText.Should().EndWith("Word for Web![FIX]",
            "the edit must land in the FINAL run's text position, not a mis-resolved earlier run");
        insertedText.Should().Contain("[FIX]",
            "the insert must be emitted as a native tracked w:ins carrying the new text");
    }

    // ── Proof of the bug: the SAME op WITHOUT paraOffset (legacy anchor only) still 422s ─────────────────────
    [Fact]
    public void InsertWithoutParaOffset_RunIndex3Offset45_ThrowsOffsetOutOfRange()
    {
        var original = BuildManyRunParagraphDocx();

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation
                {
                    ParaId = ParaId,
                    At = new ComposeRunPoint(RunIndex: 3, Offset: 45), // no paraOffset → legacy resolution
                    Text = "[FIX]",
                },
            },
        };

        var act = () => _engine.Apply(original, log, author: "Seam", timestamp: When);

        act.Should().Throw<ComposePatchException>(
                "OOXML run 3 (\"tincidunt\") is 9 chars, so run-local offset 45 is out of range — the exact pre-fix 422")
            .Which.Kind.Should().Be(ComposePatchErrorKind.OffsetOutOfRange);
    }

    // ── Backward-compat: an op with ONLY (runIndex, offset) and no paraOffset resolves exactly as before ─────
    [Fact]
    public void InsertWithoutParaOffset_ValidLegacyAnchor_ResolvesExactlyAsBefore()
    {
        var original = BuildManyRunParagraphDocx();

        // (runIndex=3, offset=9) = the trailing edge of "tincidunt" — a valid legacy anchor, no paraOffset.
        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation
                {
                    ParaId = ParaId,
                    At = new ComposeRunPoint(RunIndex: 3, Offset: 9),
                    Text = "[BC]",
                },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        var (editorText, insertedText) = ReadParagraph(patched, ParaId);
        var prefix = string.Concat(RunWords.Take(4)); // Lorem ipsum dolor tincidunt
        var suffix = string.Concat(RunWords.Skip(4));
        editorText.Should().Be(prefix + "[BC]" + suffix,
            "a legacy (runIndex, offset) anchor with no paraOffset must resolve to the run-local position exactly as before");
        insertedText.Should().Contain("[BC]");
    }

    // ── Backward-compat parity: paraOffset targeting the SAME point yields the SAME result as the legacy anchor ─
    [Fact]
    public void InsertViaParaOffset_AtTrailingEdgeOfRun3_MatchesLegacyAnchorResult()
    {
        var original = BuildManyRunParagraphDocx();
        var kAtEndOfRun3 = string.Concat(RunWords.Take(4)).Length; // paragraph-relative offset to end of "tincidunt"

        var log = new ComposeOperationLog
        {
            Operations = new ComposeOperation[]
            {
                new InsertTextOperation
                {
                    ParaId = ParaId,
                    // Deliberately WRONG legacy indices; paraOffset must win and land at end of "tincidunt".
                    At = new ComposeRunPoint(RunIndex: 0, Offset: 0, ParaOffset: kAtEndOfRun3),
                    Text = "[BC]",
                },
            },
        };

        var patched = _engine.Apply(original, log, author: "Seam", timestamp: When);

        var (editorText, _) = ReadParagraph(patched, ParaId);
        var prefix = string.Concat(RunWords.Take(4));
        var suffix = string.Concat(RunWords.Skip(4));
        editorText.Should().Be(prefix + "[BC]" + suffix,
            "paraOffset resolves by numeric offset over the real OOXML runs, overriding a stale/merged legacy runIndex");
    }

    // -- helpers ---------------------------------------------------------------------------------------------

    /// <summary>Builds a .docx whose single body paragraph (paraId 421E7EDC) is split into <see cref="RunWords"/>
    /// as SEPARATE OOXML <w:r> runs — the fine-grained shape Word's proofing/rsid split produces and TipTap
    /// merges.</summary>
    private static byte[] BuildManyRunParagraphDocx()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            var para = new Paragraph { ParagraphId = new HexBinaryValue(ParaId) };
            foreach (var word in RunWords)
            {
                para.AppendChild(new Run(new Text(word) { Space = SpaceProcessingModeValues.Preserve }));
            }

            body.AppendChild(para);
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    /// <summary>Returns the paragraph's concatenated editor text (all w:t in document order, incl. any inserted
    /// run) and the concatenated text of every native w:ins in that paragraph.</summary>
    private static (string EditorText, string InsertedText) ReadParagraph(byte[] docxBytes, string paraId)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docxBytes, writable: false), isEditable: false);
        var para = doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .First(p => string.Equals(p.ParagraphId?.Value, paraId, StringComparison.OrdinalIgnoreCase));

        var editorText = string.Concat(para.Descendants<Text>().Select(t => t.Text));
        var insertedText = string.Concat(para.Descendants<InsertedRun>().SelectMany(i => i.Descendants<Text>()).Select(t => t.Text));
        return (editorText, insertedText);
    }
}
