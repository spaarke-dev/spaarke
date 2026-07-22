// C2 fix (UAT 2026-07-20) — regression tests for ComposeBaselineParaIdStamper: the save-time step that
// stamps the client's MINTED w14:paraIds physically onto the retained-original baseline's id-less
// paragraphs so the E1 synthesizer / E2 anchoring can resolve them. Each test names a concrete production
// behavior that breaks if deleted:
//   - an id-LESS baseline paragraph whose text matches the client's snapshot is stamped with the client id
//     (the exact repro: editing/accepting a redline on an originally-id-less paragraph no longer aborts)
//   - an existing (authoritative) id is NEVER overwritten (fill-gaps-only)
//   - a document-order divergence (text mismatch) is a SKIP, never a wrong-paragraph stamp (no corruption)
//   - a paragraph-count mismatch stamps NOTHING (untrustworthy alignment)
//   - the fold is applied so a curly-apostrophe-vs-straight-apostrophe drift still verifies + stamps
//
// Banned-pattern compliance (tests/CLAUDE.md): pure domain logic over real in-memory .docx fixtures (Open
// XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4), no getter/mirror tests (B6/B16).
// Mirrors ParaIdPreParserTests's fixture conventions.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Services.Compose;

public sealed class ComposeBaselineParaIdStamperTests
{
    private static Paragraph Para(string? paraId, string text)
    {
        var p = new Paragraph(new Run(new Text(text)));
        if (paraId is not null)
        {
            p.ParagraphId = new HexBinaryValue(paraId);
        }
        return p;
    }

    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var child in bodyChildren)
            {
                body.Append(child);
            }
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static List<string?> ParaIdsOf(byte[] docx)
    {
        using var ms = new MemoryStream(docx);
        using var doc = WordprocessingDocument.Open(ms, isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!
            .Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .ToList();
    }

    [Fact]
    public void Stamp_IdLessParagraphWithMatchingText_ReceivesTheClientMintedId()
    {
        // The exact C2 repro shape: paragraph 1 carries no id in the baseline (Word left it out), but the
        // client's editor holds a load-minted id "1E5EC15C" for it. Without the stamp, an edit on it fails
        // "w14:paraId matches no paragraph in the retained original".
        var baseline = BuildDocx(
            Para("1A2B3C4D", "First clause with an id."),
            Para(null, "Second clause the source left id-less."));
        var map = new List<ComposeBaselineParaId>
        {
            new(0, "1A2B3C4D", "First clause with an id."),
            new(1, "1E5EC15C", "Second clause the source left id-less."),
        };

        var result = new ComposeBaselineParaIdStamper().Stamp(baseline, map);

        ParaIdsOf(result).Should().Equal("1A2B3C4D", "1E5EC15C");
    }

    [Fact]
    public void Stamp_ExistingId_IsNeverOverwritten_EvenWhenTheMapDisagrees()
    {
        // Fill-gaps-only: an existing (authoritative) id must survive verbatim even if the client map (e.g.
        // after a Word round-trip that regenerated ids) carries a different value at that position.
        var baseline = BuildDocx(Para("1A2B3C4D", "A clause that already has an id."));
        var map = new List<ComposeBaselineParaId> { new(0, "DEADBEEF", "A clause that already has an id.") };

        var result = new ComposeBaselineParaIdStamper().Stamp(baseline, map);

        ParaIdsOf(result).Should().Equal("1A2B3C4D");
    }

    [Fact]
    public void Stamp_TextMismatchAtIndex_SkipsTheStamp_NeverStampsTheWrongParagraph()
    {
        // Document-order divergence (e.g. mammoth merged/reordered paragraphs on an uploaded doc): the map
        // entry at index 1 claims text that does NOT match the baseline paragraph there. The stamper must
        // SKIP (leaving it id-less → an honest downstream error) rather than stamp the id onto the wrong
        // paragraph (silent corruption).
        var baseline = BuildDocx(
            Para(null, "Alpha paragraph."),
            Para(null, "Beta paragraph."));
        // Ids are in-range ST_LongHexNumber (first hex digit 0-7, i.e. < 0x80000000).
        var map = new List<ComposeBaselineParaId>
        {
            new(0, "0AAAAAA1", "Alpha paragraph."),
            new(1, "0BBBBBB2", "A totally different paragraph the client thinks is here."),
        };

        var result = new ComposeBaselineParaIdStamper().Stamp(baseline, map);

        // Index 0 matches → stamped; index 1 mismatches → left id-less.
        ParaIdsOf(result).Should().Equal("0AAAAAA1", null);
    }

    [Fact]
    public void Stamp_ParagraphCountDiffersFromMap_StampsNothing()
    {
        // A count mismatch means the ordered alignment is untrustworthy — stamp nothing at all.
        var baseline = BuildDocx(
            Para(null, "One."),
            Para(null, "Two."),
            Para(null, "Three."));
        var map = new List<ComposeBaselineParaId>
        {
            new(0, "0AAAAAA1", "One."),
            new(1, "0BBBBBB2", "Two."),
        };

        var result = new ComposeBaselineParaIdStamper().Stamp(baseline, map);

        ParaIdsOf(result).Should().Equal(null, null, null);
    }

    [Fact]
    public void Stamp_TypographicDriftBetweenBaselineAndSnapshotText_StillVerifiesAndStamps()
    {
        // The baseline OOXML holds a curly apostrophe; the client's snapshot text (echoed through the
        // editor/model) straightened it. The shared fold must make them match so the stamp still lands.
        var baseline = BuildDocx(Para(null, "The Examiner’s Report is noted."));
        var map = new List<ComposeBaselineParaId> { new(0, "0FF1CE12", "The Examiner's Report is noted.") };

        var result = new ComposeBaselineParaIdStamper().Stamp(baseline, map);

        ParaIdsOf(result).Should().Equal("0FF1CE12");
    }

    [Fact]
    public void Stamp_EmptyMap_ReturnsBaselineUnchanged()
    {
        var baseline = BuildDocx(Para(null, "Untouched."));

        var result = new ComposeBaselineParaIdStamper().Stamp(baseline, new List<ComposeBaselineParaId>());

        result.Should().Equal(baseline);
    }
}
