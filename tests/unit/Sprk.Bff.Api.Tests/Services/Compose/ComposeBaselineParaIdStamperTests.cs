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
// FR-01 (task 010, ingest) — regression tests for MintAndPersist: the LOAD-TIME step that mints +
// PERSISTS a w14:paraId into the retained package's DOM for every editable paragraph that lacks one, so
// the id is durable across a load -> save -> reload round-trip (I-1/I-3) rather than only carried in a
// projection map. Each test names a concrete production behavior that breaks if deleted:
//   - an id-less paragraph is minted AND the minted id is physically present in the returned bytes
//   - an existing (authoritative) id is NEVER re-minted or touched (idempotent, fill-gaps-only)
//   - a document whose paragraphs already all carry ids is returned byte-identical (no needless re-save)
//   - table-cell / nested-table paragraphs are covered and every id is unique across the whole document
//   - applying MintAndPersist a second time to its own output is a no-op (round-trip stability)
//   - every editable paragraph across the real fidelity corpus resolves to a unique persisted paraId
//
// Banned-pattern compliance (tests/CLAUDE.md): pure domain logic over real in-memory .docx fixtures (Open
// XML SDK) — no Mock<HttpMessageHandler> (B1), no DI/ctor tests (B3/B4), no getter/mirror tests (B6/B16).
// Mirrors ParaIdPreParserTests's fixture conventions. The corpus test reuses ComposeCorpusFixtureLocator
// (task 004, tests/integration/seam/Compose/) — compiled into this same test assembly (see
// Sprk.Bff.Api.Tests.csproj's `tests/integration/seam/**` Compile glob) so no fixture duplication.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Compose;
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

    // ── FR-01 (task 010, ingest): MintAndPersist ──────────────────────────────────────────────

    // Mirrors ParaIdPreParserTests.MixedDocxWithTables: 6 body paragraphs in document order —
    //   0 top-with-id (existing "1A2B3C4D"), 1 top-without-id,
    //   2 table cell A paragraph, 3 nested-table cell paragraph, 4 cell B trailing paragraph,
    //   5 after-table paragraph.
    private static byte[] MixedDocxWithTables() => BuildDocx(
        Para("1A2B3C4D", "top with id"),
        Para(null, "top without id"),
        new Table(
            new TableRow(
                new TableCell(Para(null, "cell A")),
                new TableCell(
                    new Table(new TableRow(new TableCell(Para(null, "nested cell")))),
                    Para(null, "cell B trailing")))),
        Para(null, "after table"));

    [Fact]
    public void MintAndPersist_IdLessParagraphs_AreMintedAndPhysicallyWrittenIntoTheRetainedBytes()
    {
        var source = BuildDocx(Para("1A2B3C4D", "has id"), Para(null, "id-less"));

        var result = new ComposeBaselineParaIdStamper().MintAndPersist(source);

        result.Mutated.Should().BeTrue();
        var idsInBytes = ParaIdsOf(result.Bytes);
        idsInBytes[0].Should().Be("1A2B3C4D", "an existing id is carried verbatim");
        idsInBytes[1].Should().NotBeNullOrEmpty(
            "the id-less paragraph's minted id must be PHYSICALLY present in the retained bytes, not only in a projection map");
        result.ParaIdMap.Should().HaveCount(2);
        result.ParaIdMap[1].IsMinted.Should().BeTrue();
        result.ParaIdMap[1].ParaId.Should().Be(idsInBytes[1], "the returned map matches what was actually written into the DOM");
    }

    [Fact]
    public void MintAndPersist_MixedDocumentInclTableCells_EveryParagraphGetsAUniquePersistedId()
    {
        var source = MixedDocxWithTables();

        var result = new ComposeBaselineParaIdStamper().MintAndPersist(source);

        result.Mutated.Should().BeTrue();
        var idsInBytes = ParaIdsOf(result.Bytes);
        idsInBytes.Should().HaveCount(6, "every body paragraph — incl. table-cell + nested-table paragraphs — is covered");
        idsInBytes.Should().OnlyContain(id => !string.IsNullOrEmpty(id), "no editable paragraph is left id-less after ingest");
        idsInBytes.Should().OnlyHaveUniqueItems("paraIds must be unique across the whole document, incl. table cells / nested tables");
        idsInBytes[0].Should().Be("1A2B3C4D", "the pre-existing id is never touched");
    }

    [Fact]
    public void MintAndPersist_DocumentWhereEveryParagraphAlreadyHasAnId_IsNotReMinted_ReturnsBytesUnchanged()
    {
        // Negative / idempotency acceptance criterion: a document that already carries w14:paraId values
        // on every paragraph must NOT be re-minted — existing ids are left byte-identical.
        var source = BuildDocx(
            Para("1A2B3C4D", "One."),
            Para("2B3C4D5E", "Two."));

        var result = new ComposeBaselineParaIdStamper().MintAndPersist(source);

        result.Mutated.Should().BeFalse("nothing needs minting — every paragraph already carries an id");
        result.Bytes.Should().Equal(source, "an already-identified document is returned byte-identical, never re-opened/re-saved");
        ParaIdsOf(result.Bytes).Should().Equal("1A2B3C4D", "2B3C4D5E");
    }

    [Fact]
    public void MintAndPersist_AppliedASecondTimeToItsOwnOutput_IsANoOp_IdsSurviveTheRoundTrip()
    {
        // Simulates load -> save (client echoes the minted-and-persisted bytes back) -> reload: the second
        // MintAndPersist pass must see every paragraph already identified and leave the bytes untouched,
        // proving the ids the first pass minted survive the round-trip unchanged.
        var source = MixedDocxWithTables();
        var stamper = new ComposeBaselineParaIdStamper();

        var first = stamper.MintAndPersist(source);
        first.Mutated.Should().BeTrue();

        var second = stamper.MintAndPersist(first.Bytes);

        second.Mutated.Should().BeFalse("every paragraph already carries the id the first pass minted");
        second.Bytes.Should().Equal(first.Bytes, "a reload sees byte-identical content — the ids survived the round-trip");
        ParaIdsOf(second.Bytes).Should().Equal(ParaIdsOf(first.Bytes));
    }

    [Fact]
    public void MintAndPersist_EmptySource_ReturnsUnchanged()
    {
        var result = new ComposeBaselineParaIdStamper().MintAndPersist(System.ReadOnlyMemory<byte>.Empty);

        result.Mutated.Should().BeFalse();
        result.Bytes.Should().BeEmpty();
        result.ParaIdMap.Should().BeEmpty();
    }

    [Fact]
    public void MintAndPersist_UnreadableSource_FailsOpen_ReturnsInputUnchanged()
    {
        var notADocx = new byte[] { 0x50, 0x4B, 0x03, 0x04 };

        var result = new ComposeBaselineParaIdStamper().MintAndPersist(notADocx);

        result.Mutated.Should().BeFalse();
        result.Bytes.Should().Equal(notADocx);
    }

    [Fact]
    public void MintAndPersist_AcrossTheFidelityCorpus_EveryEditableParagraphResolvesToAUniquePersistedParaId()
    {
        // Acceptance criterion (task 010): every editable paragraph in the corpus docs resolves to a
        // unique w14:paraId PRESENT IN THE RETAINED PACKAGE BYTES, incl. paragraphs inside table cells
        // and nested tables (the 3 seed fixtures include a table-free formatted letter, a flat
        // plain-paragraph doc, and the CIPO track-changes/footer-SDT doc — corpus-manifest.md).
        var stamper = new ComposeBaselineParaIdStamper();

        foreach (var path in ComposeCorpusFixtureLocator.EnumerateDocumentPaths())
        {
            var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);

            var result = stamper.MintAndPersist(original);

            var idsInBytes = ParaIdsOf(result.Bytes);
            idsInBytes.Should().NotBeEmpty($"{Path.GetFileName(path)} has at least one body paragraph");
            idsInBytes.Should().OnlyContain(id => !string.IsNullOrEmpty(id),
                $"every editable paragraph in {Path.GetFileName(path)} must carry a persisted paraId after ingest");
            idsInBytes.Should().OnlyHaveUniqueItems($"paraIds must be unique across {Path.GetFileName(path)}");
        }
    }
}
