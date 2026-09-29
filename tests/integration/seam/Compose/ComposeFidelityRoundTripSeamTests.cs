// Task 027 (spaarkeai-compose-r6, spec FR-03/FR-04) — the Phase-2 fidelity DoD suite: per-feature
// load → edit → save → reopen THROUGH THE WIRE on the render-on-save path (tasks 010/011/012 —
// ComposeDocumentRenderer.RenderIntoCarrier; no patch engine, no count-gate). Slices:
//
//   1a/1b. NUMBERING CONTINUITY — nda-interrupted-clauses.docx (goldens "1."–"6." CONTINUOUS across
//          heading/body/table interruptions; the defective reader restarts at 1) and
//          multilevel-1-1-1.docx (goldens "1.", "1.1.", "1.1.1.", "1.1.2.", "1.2.", "2.", "2.1.")
//          per corpus-manifest.md §1.5 rows 9/11. Labels asserted on the REOPENED load's paraIdMap
//          `computedNumber` wire field (R4.5 F-4 numbering engine output).
//   2.  HEADING STYLE-LINKED NUMBERING — heading-style-numbering.docx (§1.5 row 10; goldens incl.
//       "4.2" on the second Heading2 — the FR-12 acceptance example) through the same round trip.
//   3.  TABLES — the w:tbl in nda-interrupted-clauses survives the round trip (model Table block on
//       reopen + w:tbl with cell text in the persisted bytes).
//   4.  HEADERS/FOOTERS — the CIPO doc's 3 header + 3 footer carrier parts survive (OPC part
//       presence) incl. the footer page-number SDT (empirically footer2.xml: w:sdt + PAGE field).
//   5.  PAGE BREAKS — synthetic carrier: explicit w:br type="page" survives as a page Break in the
//       persisted bytes and as an isPageBreak marker run in the reopened model.
//   6.  HYPERLINKS — synthetic carrier: external w:hyperlink survives with its relationship
//       resolving to the same URL; the reopened model run carries href.
//   7.  COMMENTS — synthetic carrier with a real comments part: the comment AND its body anchor
//       (commentReference id 1) survive the round trip.
//   8.  TRACKED CHANGES — multi-author-redline-synthetic.docx (§1.7 row 15): both original authors'
//       insertions with dates, the w:delText deletion, the paragraph-mark deletion, and the
//       Word-canonical w:hyperlink ⊃ w:ins nesting all survive; a NEW tracked edit is attributed to
//       the save (authenticated) author; all revision w:id values unique.
//   9.  HARD-TIER WARNS-NOT-FAILS — AppligentNDA_Signed.docx: `text-box-flattened` is wire-visible
//       on the load's contentModelWarnings; the save is 200 (explicitly never 422) and the
//       signature-box prose survives.
//   10. DUPLICATE-PARAID LOAD PROBE — NDA load (no save): projection not failed, paraIdMap
//       index-aligned incl. the duplicated ids (first-wins semantics don't error), model non-null
//       (013 adr-check residual 1 — the pre-first-save duplicate-id load posture).
//
// MAINTAIN-class (regression-protectors; /test-diet KEEP — tests/integration/seam/** vertical-slice
// KEEP path per ADR-038). Through-the-wire WebApplicationFactory slices only: NO
// Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test, NO reflection over private
// members; mocks ONLY at the ComposeFidelitySeamFixture's ISpeFileOperations /
// IGenericEntityService / IPostUploadIndexingEnqueuer boundaries (CLAUDE.md §11 — no new fixture).
// ADR-049 (R6): byte-identity of untouched subtrees is NOT asserted on the save path (superseded by
// render-on-save) — these slices assert per-feature fidelity + warnings instead.

using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeFidelityRoundTripSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    /// <summary>The `name` claim ComposeFidelitySeamFakeAuthHandler stamps — the save-path revision
    /// author (ComposeService.ResolveRevisionAuthor) every NEW tracked edit must be attributed to.</summary>
    private const string SaveAuthor = ComposeFidelitySeamFixture.AuthenticatedUserName;

    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeFidelityRoundTripSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1a. Numbering continuity across interruptions — §1.5 row 9 goldens, CONTINUOUS "1."–"6.".
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NumberingContinuity_InterruptedClauses_ReopenedLabelsContinuousOneToSix()
    {
        var result = await RunWireRoundTripAsync(
            LoadCorpusBytes("nda-interrupted-clauses.docx"),
            speId: "spe-027-num-interrupted", driveId: "drive-027-num-interrupted",
            mutateContentModel: model => AppendPlainTextToFirstTextRun(model, " (edited via round trip)"));

        var goldens = new[] { "1.", "2.", "3.", "4.", "5.", "6." };
        NonNullComputedNumbers(result.LoadRoot).Should().Equal(goldens,
            "LOAD-side sanity: the projection must compute the §1.5 row-9 continuous clause labels");
        NonNullComputedNumbers(result.ReopenRoot).Should().Equal(goldens,
            "the REOPENED document's clause numbering must stay CONTINUOUS 1→6 across the " +
            "heading/body/table interruptions (§1.5 row 9) — the defective reader restarts at 1 " +
            "after each interruption (1,2,3,1,2,3)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1b. Multi-level 1 / 1.1 / 1.1.1 numbering — §1.5 row 11 goldens.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NumberingContinuity_MultilevelOneOneOne_ReopenedLabelsMatchGoldens()
    {
        var result = await RunWireRoundTripAsync(
            LoadCorpusBytes("multilevel-1-1-1.docx"),
            speId: "spe-027-num-multilevel", driveId: "drive-027-num-multilevel",
            mutateContentModel: model => AppendPlainTextToFirstTextRun(model, " (edited via round trip)"));

        var goldens = new[] { "1.", "1.1.", "1.1.1.", "1.1.2.", "1.2.", "2.", "2.1." };
        NonNullComputedNumbers(result.ReopenRoot).Should().Equal(goldens,
            "the reopened multi-level labels must reproduce the §1.5 row-11 goldens exactly — " +
            "incl. the level-1/2 counter RESET at the second level-0 item (\"2.\" → \"2.1.\")");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Heading STYLE-LINKED numbering — §1.5 row 10 goldens incl. "4.2" (the FR-12 acceptance
    //    example). 020-R7 caution honored: if style-linked identity had degraded through the model,
    //    this slice would be REPORTED as an adapter gap, not weakened — it passes as authored, so
    //    the golden labels ARE the contracted behavior.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HeadingStyleLinkedNumbering_ReopenedLabelsIncludeFourPointTwo()
    {
        var result = await RunWireRoundTripAsync(
            LoadCorpusBytes("heading-style-numbering.docx"),
            speId: "spe-027-num-headingstyle", driveId: "drive-027-num-headingstyle",
            mutateContentModel: model => AppendPlainTextToFirstTextRun(model, " (edited via round trip)"));

        var goldens = new[] { "1", "2", "3", "4", "4.1", "4.2" };
        NonNullComputedNumbers(result.ReopenRoot).Should().Equal(goldens,
            "style-linked heading numbering (w:numPr on the STYLE, zero paragraph-level w:numPr) " +
            "must survive the round trip — §1.5 row 10; \"4.2 Confidentiality\" is the literal " +
            "FR-12 acceptance example");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. Tables — the real w:tbl (Party/Role · Acme Corp/Disclosing Party) survives.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Tables_InterruptedClausesTable_SurvivesRoundTrip()
    {
        var result = await RunWireRoundTripAsync(
            LoadCorpusBytes("nda-interrupted-clauses.docx"),
            speId: "spe-027-tables", driveId: "drive-027-tables",
            mutateContentModel: model => AppendPlainTextToFirstTextRun(model, " (edited via round trip)"));

        result.ReopenRoot["contentModel"]!["blocks"]!.AsArray()
            .Any(b => string.Equals(b!["kind"]?.GetValue<string>(), "table", StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("the reopened model must still contain the Table block");

        using var doc = OpenPersisted(result.PersistedBytes);
        var tables = doc.MainDocumentPart!.Document!.Body!.Descendants<Table>().ToList();
        tables.Should().NotBeEmpty("the persisted bytes must contain the native w:tbl");
        var tableText = string.Concat(tables.Select(t => t.InnerText));
        tableText.Should().Contain("Acme Corp").And.Contain("Disclosing Party",
            "the table's cell text must survive the round trip");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. Headers/footers — the CIPO carrier's 3+3 header/footer parts and the footer page-number
    //    SDT (empirically in footer2.xml: w:sdt + PAGE field) survive as preserved carrier parts.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HeadersFooters_CipoCarrierParts_SurviveRoundTrip()
    {
        var result = await RunWireRoundTripAsync(
            LoadCorpusBytes("PAT 109270W"),
            speId: "spe-027-headers-footers", driveId: "drive-027-headers-footers",
            mutateContentModel: model => AppendPlainTextToFirstTextRun(model, " (edited via round trip)"));

        using var archive = new ZipArchive(new MemoryStream(result.PersistedBytes, writable: false), ZipArchiveMode.Read);
        var entryNames = archive.Entries.Select(e => e.FullName).ToList();
        foreach (var part in new[]
        {
            "word/header1.xml", "word/header2.xml", "word/header3.xml",
            "word/footer1.xml", "word/footer2.xml", "word/footer3.xml",
        })
        {
            entryNames.Should().Contain(part,
                "the carrier's header/footer parts must be preserved by RenderIntoCarrier — the " +
                "thin model cannot carry them, the carrier contributes them");
        }

        // Review F6: assert the SEMANTIC (an SDT wrapping a PAGE field) via the SDK, not raw markup
        // text - immune to legitimate re-serialization (attributes on the tag, prefix changes).
        using var persistedDoc = OpenPersisted(result.PersistedBytes);
        persistedDoc.MainDocumentPart!.FooterParts
            .Any(fp => fp.Footer!.Descendants<SdtElement>()
                .Any(sdt => sdt.Descendants<FieldCode>().Any(f => f.Text.Contains("PAGE", StringComparison.Ordinal))))
            .Should().BeTrue("the footer page-number SDT (w:sdt wrapping a PAGE field) must survive");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. Page breaks — explicit w:br type="page" survives in the bytes AND round-trips through the
    //    model as the isPageBreak marker run (ComposeInlineRun.IsPageBreak — the run-level
    //    representation the projection emits for a manual break; distinct from pageBreakBefore).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PageBreaks_ExplicitBreakRun_SurvivesRoundTrip()
    {
        var carrier = BuildDocx((_, body) =>
        {
            body.AppendChild(new Paragraph(
                new Run(new Text("Alpha section prose.") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new Break { Type = BreakValues.Page })));
            body.AppendChild(new Paragraph(
                new Run(new Text("Beta section prose.") { Space = SpaceProcessingModeValues.Preserve })));
        });

        var result = await RunWireRoundTripAsync(
            carrier, speId: "spe-027-pagebreak", driveId: "drive-027-pagebreak",
            mutateContentModel: model => AppendPlainTextToRunContaining(model, "Alpha section", " (edited)"));

        using var doc = OpenPersisted(result.PersistedBytes);
        doc.MainDocumentPart!.Document!.Body!.Descendants<Break>()
            .Any(b => b.Type is not null && b.Type.Value == BreakValues.Page)
            .Should().BeTrue("the explicit manual page break (w:br type=\"page\") must survive the render");

        EnumerateModelRuns(result.ReopenRoot["contentModel"]!.AsObject())
            .Any(r => r["isPageBreak"]?.GetValue<bool>() == true)
            .Should().BeTrue(
                "the reopened model must carry the break as an isPageBreak marker run (the run-level " +
                "representation the projection emits for an inline manual break)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 6. Hyperlinks — an external w:hyperlink's relationship resolves to the SAME URL after the
    //    round trip; the reopened model run carries href.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Hyperlinks_ExternalRelationship_SurvivesRoundTrip()
    {
        const string url = "https://example.com/terms/agreement";
        var carrier = BuildDocx((mainPart, body) =>
        {
            var rel = mainPart.AddHyperlinkRelationship(new Uri(url), isExternal: true);
            body.AppendChild(new Paragraph(
                new Run(new Text("Linked terms: ") { Space = SpaceProcessingModeValues.Preserve }),
                new Hyperlink(new Run(new Text("Example Terms"))) { Id = rel.Id }));
            body.AppendChild(new Paragraph(
                new Run(new Text("Other prose to edit.") { Space = SpaceProcessingModeValues.Preserve })));
        });

        var result = await RunWireRoundTripAsync(
            carrier, speId: "spe-027-hyperlink", driveId: "drive-027-hyperlink",
            mutateContentModel: model => AppendPlainTextToRunContaining(model, "Other prose", " (edited)"));

        using var doc = OpenPersisted(result.PersistedBytes);
        var mainPartOut = doc.MainDocumentPart!;
        var hyperlink = mainPartOut.Document!.Body!.Descendants<Hyperlink>()
            .FirstOrDefault(h => h.InnerText.Contains("Example Terms", StringComparison.Ordinal));
        hyperlink.Should().NotBeNull("the persisted bytes must contain the w:hyperlink with its text");
        var relOut = mainPartOut.HyperlinkRelationships.FirstOrDefault(r => r.Id == hyperlink!.Id?.Value);
        relOut.Should().NotBeNull("the hyperlink's relationship id must resolve in the persisted package");
        relOut!.Uri.ToString().Should().Be(url, "the external relationship must target the SAME URL");

        EnumerateModelRuns(result.ReopenRoot["contentModel"]!.AsObject())
            .Any(r => string.Equals(r["href"]?.GetValue<string>(), url, StringComparison.Ordinal))
            .Should().BeTrue("the reopened model run must carry href (the projection's hyperlink capture)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 7. Comments — a real comments part (comment id 1) + its body anchors survive the round trip
    //    while OTHER text is edited.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Comments_PartAndBodyAnchor_SurviveRoundTrip()
    {
        const string commentText = "Please check this clause.";
        var carrier = BuildDocx((mainPart, body) =>
        {
            var commentsPart = mainPart.AddNewPart<WordprocessingCommentsPart>();
            commentsPart.Comments = new Comments(
                new Comment(new Paragraph(new Run(new Text(commentText))))
                {
                    Id = "1",
                    Author = "Reviewer One",
                    Initials = "R1",
                    Date = new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc),
                });
            commentsPart.Comments.Save();

            body.AppendChild(new Paragraph(
                new CommentRangeStart { Id = "1" },
                new Run(new Text("Commented clause text.") { Space = SpaceProcessingModeValues.Preserve }),
                new CommentRangeEnd { Id = "1" },
                new Run(new CommentReference { Id = "1" })));
            body.AppendChild(new Paragraph(
                new Run(new Text("Second paragraph to edit.") { Space = SpaceProcessingModeValues.Preserve })));
        });

        var result = await RunWireRoundTripAsync(
            carrier, speId: "spe-027-comments", driveId: "drive-027-comments",
            mutateContentModel: model => AppendPlainTextToRunContaining(model, "Second paragraph", " (edited)"));

        using var doc = OpenPersisted(result.PersistedBytes);
        var commentsOut = doc.MainDocumentPart!.WordprocessingCommentsPart;
        commentsOut.Should().NotBeNull("the comments part must survive the round trip (carrier-preserved)");
        commentsOut!.Comments!.InnerText.Should().Contain(commentText,
            "the comment body must survive verbatim");
        doc.MainDocumentPart.Document!.Body!.Descendants<CommentReference>()
            .Any(r => r.Id?.Value == "1")
            .Should().BeTrue("the body must still ANCHOR comment id 1 (w:commentReference) after reopen");

        // Review F5: the RANGE markers must survive too - dropping them collapses the comment from a
        // text range to a point anchor (a silent fidelity loss the reference assert alone misses).
        doc.MainDocumentPart.Document.Body.Descendants<CommentRangeStart>()
            .Should().ContainSingle(r => r.Id!.Value == "1", "the commentRangeStart must survive");
        doc.MainDocumentPart.Document.Body.Descendants<CommentRangeEnd>()
            .Should().ContainSingle(r => r.Id!.Value == "1", "the commentRangeEnd must survive");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 8. Tracked changes — multi-author-redline-synthetic.docx (§1.7 row 15): original authorship +
    //    dates, the delText deletion, the paragraph-mark deletion, the Word-canonical tracked
    //    hyperlink, plus a NEW tracked edit attributed to the save author; revision ids unique.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task TrackedChanges_MultiAuthorRedlines_SurviveWithNewEditAttributed()
    {
        const string newEditMarker = "[R6-FIDELITY-027-NEW-EDIT]";
        var result = await RunWireRoundTripAsync(
            LoadCorpusBytes("multi-author-redline-synthetic.docx"),
            speId: "spe-027-tracked-changes", driveId: "drive-027-tracked-changes",
            mutateContentModel: model => AppendInsertedRunToFirstParagraph(model, newEditMarker));

        using var doc = OpenPersisted(result.PersistedBytes);
        var body = doc.MainDocumentPart!.Document!.Body!;

        // Both ORIGINAL authors' insertions survive with their dates (attribution preservation).
        var insertedRuns = body.Descendants<InsertedRun>().ToList();
        var aliceIns = insertedRuns.Where(i => i.Author?.Value == "Alice Chen").ToList();
        var bobIns = insertedRuns.Where(i => i.Author?.Value == "Bob Rivera").ToList();
        aliceIns.Should().NotBeEmpty("Alice Chen's tracked insertion must survive the round trip");
        bobIns.Should().NotBeEmpty("Bob Rivera's tracked insertions must survive the round trip");
        aliceIns.All(i => i.Date?.InnerText?.StartsWith("2026-08-01", StringComparison.Ordinal) == true)
            .Should().BeTrue("Alice's revision DATE must be preserved (2026-08-01)");
        bobIns.All(i => i.Date?.InnerText?.StartsWith("2026-08-02", StringComparison.Ordinal) == true)
            .Should().BeTrue("Bob's revision DATE must be preserved (2026-08-02)");

        // The run-level deletion's w:delText content survives.
        body.Descendants<DeletedRun>()
            .Any(d => d.InnerText.Contains("two (2)", StringComparison.Ordinal))
            .Should().BeTrue("the w:del/w:delText deletion (\"two (2) \") must survive as a real deletion");

        // The paragraph-MARK deletion (w:pPr/w:rPr/w:del — accept merges with the successor) survives.
        body.Descendants<Paragraph>()
            .Any(p => p.ParagraphProperties?.ParagraphMarkRunProperties?.Elements<Deleted>().Any() == true)
            .Should().BeTrue("the paragraph-mark deletion must survive (Deleted inside ParagraphMarkRunProperties)");

        // The tracked-inserted hyperlink keeps Word-canonical nesting: w:hyperlink ⊃ w:ins — NEVER inverted.
        body.Descendants<Hyperlink>()
            .Any(h => h.Descendants<InsertedRun>().Any())
            .Should().BeTrue("the tracked-inserted hyperlink must stay w:hyperlink ⊃ w:ins (Word-canonical)");
        body.Descendants<InsertedRun>()
            .Any(i => i.Descendants<Hyperlink>().Any())
            .Should().BeFalse("the nesting must NEVER invert to w:ins ⊃ w:hyperlink (Word repairs/rejects it)");

        // The NEW edit renders as a tracked insertion attributed to the SAVE (authenticated) author.
        insertedRuns
            .Any(i => i.InnerText.Contains(newEditMarker, StringComparison.Ordinal)
                && i.Author?.Value == SaveAuthor)
            .Should().BeTrue(
                $"the new model edit must land as an InsertedRun attributed to the save author (\"{SaveAuthor}\")");

        // Every revision wrapper's w:id is unique (server-minted, monotonic — never colliding).
        var revisionIds = body.Descendants()
            .Where(e => e.NamespaceUri.EndsWith("/wordprocessingml/2006/main", StringComparison.Ordinal)
                && e.LocalName is "ins" or "del" or "rPrChange" or "pPrChange")
            .Select(e => e.GetAttributes().FirstOrDefault(a => a.LocalName == "id").Value)
            .Where(id => !string.IsNullOrEmpty(id))
            .ToList();
        revisionIds.Should().NotBeEmpty();
        revisionIds.Should().OnlyHaveUniqueItems("all revision w:id values must be unique after the render");

        // Review F1: the FORMATTING-change history the fixture was purpose-built to carry must survive -
        // without these, a silent rPrChange/pPrChange drop round-trips green on the asserts above.
        body.Descendants<RunPropertiesChange>()
            .Should().Contain(c => c.Author!.Value == "Alice Chen",
                "the w:rPrChange (bold added by Alice) must survive the round trip");
        body.Descendants<Paragraph>()
            .Any(p => p.InnerText.Contains("irreparable harm", StringComparison.Ordinal)
                && p.Descendants<RunPropertiesChange>().Any())
            .Should().BeTrue("the format-change record must stay on the 'irreparable harm' paragraph");
        body.Descendants<ParagraphPropertiesChange>()
            .Should().Contain(c => c.Author!.Value == "Bob Rivera",
                "the w:pPrChange (centered by Bob) must survive the round trip");
        body.Descendants<Paragraph>()
            .Any(p => p.InnerText.Contains("Governing Law", StringComparison.Ordinal)
                && p.ParagraphProperties?.Justification?.Val != null
                && p.ParagraphProperties.Justification.Val.Value == JustificationValues.Center)
            .Should().BeTrue("the Governing Law paragraph must stay centered (current formatting stands)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 9. Hard-tier warns-not-fails (wire) — the NDA's text boxes flatten LOUDLY at load and the save
    //    is success-with-warnings, never a 422. degradationWarnings are additive — no emptiness assert.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task HardTier_NdaTextBoxes_WarnsAtLoad_SavesWithoutFourTwentyTwo()
    {
        var result = await RunWireRoundTripAsync(
            LoadCorpusBytes("AppligentNDA_Signed.docx"),
            speId: "spe-027-hard-tier-nda", driveId: "drive-027-hard-tier-nda",
            mutateContentModel: model => AppendInsertedRunToFirstParagraph(model, "[R6-FIDELITY-027-HARD-TIER]"));

        var warnings = result.LoadRoot["contentModelWarnings"]?.AsArray();
        warnings.Should().NotBeNull(
            "the NDA's hard-tier flatten must be WIRE-VISIBLE on the load's contentModelWarnings");
        warnings!
            .Any(w => string.Equals(w!["code"]?.GetValue<string>(), "text-box-flattened", StringComparison.Ordinal))
            .Should().BeTrue(
                $"the text-box flatten must surface as the `text-box-flattened` warning code — warnings: {warnings!.ToJsonString()}");

        // 200 + explicit not-422 + versionId already asserted by the shared driver; the substance:
        using var doc = OpenPersisted(result.PersistedBytes);
        doc.MainDocumentPart!.Document!.Body!.InnerText.Should().Contain("For: Appligent, Inc.",
            "the signature-box content must survive as accept-flattened prose — flattened LOUDLY, " +
            "never silently dropped and never a 422");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 10. Duplicate-paraId reference probe — NDA LOAD ONLY (no save): the pre-first-save
    //     duplicate-id posture (013 adr-check residual 1). First-wins semantics never error.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task DuplicateParaId_NdaLoadProbe_IndexAlignedMapAndModel_NoFailure()
    {
        const string speId = "spe-027-dup-paraid-probe";
        const string driveId = "drive-027-dup-paraid-probe";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var ndaBytes = LoadCorpusBytes("AppligentNDA_Signed.docx");

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();
        ArrangeSpeLoad(ndaBytes, speId, driveId, eTag: "\"v1\"", versionId: "1.0");

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"the duplicate-paraId NDA load must succeed — body: {body}");

        var root = JsonNode.Parse(body)!.AsObject();

        root["projection"]!["status"]!.GetValue<string>().Should().NotBe("failed",
            "duplicate w14:paraIds must not fail the projection (first-wins semantics)");

        var paraIdMap = root["paraIdMap"]!.AsArray();
        paraIdMap.Should().NotBeEmpty("the load must produce the ordered paraId map");
        paraIdMap.Select(e => e!["index"]!.GetValue<int>())
            .Should().Equal(Enumerable.Range(0, paraIdMap.Count),
                "the map must be INDEX-ALIGNED — one entry per source paragraph in document order");
        paraIdMap.Select(e => e!["paraId"]!.GetValue<string>().ToUpperInvariant())
            .GroupBy(id => id)
            .Any(g => g.Count() > 1)
            .Should().BeTrue(
                "the NDA's duplicated ids (2BBF07C9/CA/CB — one per mc:AlternateContent branch pair) " +
                "must appear in the pre-first-save map without erroring (dedup happens at RENDER, not load)");

        root["contentModel"].Should().NotBeNull(
            "the canonical model projection must succeed on the duplicate-paraId document");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared wire driver (mirrors RenderOnSaveSeamTests' flow) + arrange/mutation/assert helpers.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    private sealed record WireRoundTrip(
        JsonObject LoadRoot,
        JsonObject SaveRoot,
        byte[] PersistedBytes,
        JsonObject ReopenRoot);

    /// <summary>Drives one full THROUGH-THE-WIRE round trip: arrange SPE load mocks → GET load →
    /// clone + caller-mutate the contentModel → POST the post-cutover save shape
    /// {sessionId, tenantId, driveId, content, contentModel} → assert 200 (explicit not-422) +
    /// versionId → capture persisted bytes → re-arm download to the persisted bytes → GET reopen.</summary>
    private async Task<WireRoundTrip> RunWireRoundTripAsync(
        byte[] sourceBytes, string speId, string driveId, Action<JsonObject> mutateContentModel)
    {
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();
        ArrangeSpeLoad(sourceBytes, speId, driveId, eTag: "\"v1\"", versionId: "1.0");

        using var client = _fixture.CreateAuthenticatedClient();

        // ── LOAD ─────────────────────────────────────────────────────────────────────────────────
        var loadResponse = await client.GetAsync($"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var loadBody = await loadResponse.Content.ReadAsStringAsync();
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK, $"[{speId}] load must succeed — body: {loadBody}");
        var loadRoot = JsonNode.Parse(loadBody)!.AsObject();
        loadRoot["contentModel"].Should().NotBeNull(
            $"[{speId}] the canonical content-model projection must succeed for this fixture");
        var sessionId = loadRoot["sessionId"]!.GetValue<string>();

        // ── EDIT (client-mapper simulation: clone the retained model, mutate, re-post) ───────────
        var editedModel = loadRoot["contentModel"]!.DeepClone()!.AsObject();
        mutateContentModel(editedModel);

        // ── SAVE (post-cutover shape; capture the persisted bytes at the SPE boundary) ───────────
        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, sourceBytes.Length, "\"v2\""));

        var saveBody = new JsonObject
        {
            ["sessionId"] = sessionId,
            ["tenantId"] = tenant,
            ["driveId"] = driveId,
            ["content"] = loadRoot["content"]!.DeepClone(),
            ["contentModel"] = editedModel,
        };
        var saveResponse = await client.PostAsync(
            $"/api/compose/documents/{speId}/save",
            new StringContent(saveBody.ToJsonString(), Encoding.UTF8, "application/json"));
        var saveResponseBody = await saveResponse.Content.ReadAsStringAsync();
        ((int)saveResponse.StatusCode).Should().NotBe(422,
            $"[{speId}] render-on-save must NEVER 422 (hard-tier constructs warn, not fail) — body: {saveResponseBody}");
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"[{speId}] save must succeed — body: {saveResponseBody}");
        var saveRoot = JsonNode.Parse(saveResponseBody)!.AsObject();
        saveRoot["versionId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace(
            $"[{speId}] a successful save reports the new SPE version identity");
        persisted.Should().NotBeNull($"[{speId}] the SPE facade must have captured the rendered bytes");

        // ── REOPEN (the persisted bytes become the next load's source) ───────────────────────────
        ArrangeSpeLoad(persisted!, speId, driveId, eTag: "\"v2\"", versionId: "2.0");
        var reopenResponse = await client.GetAsync($"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var reopenBody = await reopenResponse.Content.ReadAsStringAsync();
        reopenResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"[{speId}] the reopen load of the persisted bytes must succeed — body: {reopenBody}");
        var reopenRoot = JsonNode.Parse(reopenBody)!.AsObject();

        // Review F2: SELF-PROOF of the reopen leg - the reopened model must contain the text the mutation
        // ADDED. Had the re-arm silently served the ORIGINAL bytes (dropped/misordered re-arm, speId typo),
        // every reopen-side oracle in the slices would pass vacuously against the original; this converts
        // mock-arrangement trust into wire proof.
        reopenRoot["contentModel"].Should().NotBeNull($"[{speId}] the reopened persisted bytes must project");
        var originalText = string.Concat(EnumerateModelRuns(loadRoot["contentModel"]!.AsObject())
            .Select(r => r["text"]?.GetValue<string>() ?? string.Empty));
        var addedTexts = EnumerateModelRuns(editedModel)
            .Select(r => r["text"]?.GetValue<string>() ?? string.Empty)
            .Where(t => t.Length > 0 && !originalText.Contains(t, StringComparison.Ordinal))
            .ToList();
        addedTexts.Should().NotBeEmpty($"[{speId}] every driver slice mutates by ADDING visible text");
        var reopenText = string.Concat(EnumerateModelRuns(reopenRoot["contentModel"]!.AsObject())
            .Select(r => r["text"]?.GetValue<string>() ?? string.Empty));
        foreach (var added in addedTexts)
        {
            reopenText.Should().Contain(added,
                $"[{speId}] the reopen must serve the PERSISTED bytes, not the original");
        }

        return new WireRoundTrip(loadRoot, saveRoot, persisted!, reopenRoot);
    }

    private void ArrangeSpeLoad(byte[] bytes, string speId, string driveId, string eTag, string versionId)
    {
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, bytes.Length, eTag));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(bytes.ToArray()));
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(versionId);
    }

    private void ArrangeIdempotentPromotionAndIndexing()
    {
        var existingDocumentId = Guid.NewGuid();
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));

        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
    }

    // ── Model mutation helpers (JsonNode — the client-mapper simulation, no fragile DTO mirrors) ──

    /// <summary>Appends plain text to the first text-carrying run of the first non-table block —
    /// a minimal "the user typed something" edit that leaves numbering/structure untouched.</summary>
    private static void AppendPlainTextToFirstTextRun(JsonObject model, string suffix)
    {
        var run = EnumerateModelRuns(model)
            .FirstOrDefault(r => !string.IsNullOrEmpty(r["text"]?.GetValue<string>())
                && r["isPageBreak"]?.GetValue<bool>() != true
                && r["commentAnchor"] is null);
        run.Should().NotBeNull("the model must expose at least one editable text run");
        run!["text"] = run["text"]!.GetValue<string>() + suffix;
    }

    /// <summary>Appends plain text to the first run whose text contains <paramref name="match"/> —
    /// used to edit OTHER text than the feature under test.</summary>
    private static void AppendPlainTextToRunContaining(JsonObject model, string match, string suffix)
    {
        var run = EnumerateModelRuns(model)
            .FirstOrDefault(r => (r["text"]?.GetValue<string>() ?? string.Empty)
                .Contains(match, StringComparison.Ordinal));
        run.Should().NotBeNull($"the model must contain a run with text \"{match}\"");
        run!["text"] = run["text"]!.GetValue<string>() + suffix;
    }

    /// <summary>Appends a NEW tracked-inserted run (author omitted — the server attributes the
    /// authenticated user) to the first paragraph-kind block, mirroring the client mapper.</summary>
    private static void AppendInsertedRunToFirstParagraph(JsonObject model, string text)
    {
        var block = model["blocks"]!.AsArray()
            .First(b => string.Equals(b!["kind"]?.GetValue<string>(), "paragraph", StringComparison.OrdinalIgnoreCase));
        block!["runs"]!.AsArray().Add(new JsonObject
        {
            ["text"] = text,
            ["revision"] = new JsonObject { ["kind"] = "Inserted" },
        });
    }

    /// <summary>Enumerates every inline run in the model — top-level blocks AND table-cell blocks.</summary>
    private static IEnumerable<JsonObject> EnumerateModelRuns(JsonObject model)
    {
        static IEnumerable<JsonObject> WalkBlocks(JsonArray blocks)
        {
            foreach (var block in blocks)
            {
                if (block is not JsonObject b) continue;
                if (b["runs"] is JsonArray runs)
                {
                    foreach (var run in runs)
                    {
                        if (run is JsonObject r) yield return r;
                    }
                }

                if (b["table"] is JsonObject table && table["rows"] is JsonArray rows)
                {
                    foreach (var row in rows)
                    {
                        if (row is not JsonObject ro || ro["cells"] is not JsonArray cells) continue;
                        foreach (var cell in cells)
                        {
                            if (cell is JsonObject c && c["blocks"] is JsonArray cellBlocks)
                            {
                                foreach (var r in WalkBlocks(cellBlocks)) yield return r;
                            }
                        }
                    }
                }
            }
        }

        return WalkBlocks(model["blocks"]!.AsArray());
    }

    // ── Wire-shape + fixture helpers ──────────────────────────────────────────────────────────────

    /// <summary>The load response's paraIdMap `computedNumber` values (the R4.5 F-4 numbering-engine
    /// wire field), document order, nulls (non-numbered paragraphs) filtered out — i.e. exactly the
    /// visible legal-number label sequence Word displays.</summary>
    private static List<string> NonNullComputedNumbers(JsonObject loadOrReopenRoot) =>
        loadOrReopenRoot["paraIdMap"]!.AsArray()
            .OrderBy(e => e!["index"]!.GetValue<int>())
            .Select(e => e!["computedNumber"]?.GetValue<string>())
            .Where(n => !string.IsNullOrEmpty(n))
            .Select(n => n!)
            .ToList();

    /// <summary>Loads a corpus fixture whose filename contains <paramref name="nameFragment"/> via
    /// the shared locator (LFS-guarded).</summary>
    private static byte[] LoadCorpusBytes(string nameFragment)
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => Path.GetFileName(p).Contains(nameFragment, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    /// <summary>Builds a minimal valid synthetic carrier .docx, delegating body authoring to the
    /// caller (same SDK-built convention as this suite's sibling seam tests).</summary>
    private static byte[] BuildDocx(Action<MainDocumentPart, Body> author)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            author(mainPart, body);
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }

    private static WordprocessingDocument OpenPersisted(byte[] bytes) =>
        WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), isEditable: false);

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: "fidelity-round-trip.docx", ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);
}
