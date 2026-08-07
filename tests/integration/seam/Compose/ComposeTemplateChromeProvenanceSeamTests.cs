// Task 033 (spaarkeai-compose-r6, Success Criterion 3) — the dedicated chrome-provenance seam suite
// for POST /api/compose/documents/{documentSpeId}/apply-template, asserting on the PERSISTED merged
// bytes captured at the SPE facade boundary (the same wire + fixture family as the sibling
// ComposeApplyTemplateSeamTests — task 032).
//
// COVERAGE SPLIT vs the existing suites (CLAUDE.md §11 don't-duplicate):
//   - 032's ComposeApplyTemplateSeamTests already proves, through the wire: header-part text
//     reachable via the trailing sectPr rId, landscape PageSize orientation, body provenance
//     (document text kept / template boilerplate dropped), and Document (not Template) output type.
//     Its document input carried NO chrome of its own — provenance was never CONTESTED.
//   - 030's ComposeTemplatePartMergeEngineTests prove styles-collision/footer/numbering/carrier-class
//     behavior at the ENGINE boundary only (direct Merge() calls, no wire).
//
// WHAT THIS SUITE ADDS (through the wire, on persisted bytes):
//   (1) CONTESTED chrome — the document carries its OWN conflicting Heading1 style def, its OWN
//       header part, and its OWN portrait sectPr; the persisted output must adopt the TEMPLATE's
//       styles.xml definition (distinctive color wins the style-id collision), the TEMPLATE's page
//       geometry (landscape + margins), the TEMPLATE's header AND FOOTER parts (footer provenance
//       was not asserted anywhere at seam level), and the document's chrome must be GONE.
//       OpenXmlValidator clean on the persisted bytes (also new at seam level).
//   (2) THE IMPORTED-CARRIER WIRE SLICE (020-canonical-hub-design.md §21: "033's seam suite should
//       include an IMPORTED carrier slice through the wire — the carrier class is where the bodies
//       are buried"): REAL corpus documents (Git-LFS, via ComposeCorpusFixtureLocator's LFS guard)
//       driven through apply-template:
//         (2a) multi-author-redline-synthetic.docx — live w:ins/w:del with two authors, w:rPrChange,
//              w:pPrChange, paragraph-MARK deletion, tracked-inserted hyperlink — plus an anchored
//              comment grafted in-test onto the REAL corpus bytes (NO corpus doc carries live
//              comments per corpus-manifest.md; the enrichment keeps the real redline content and
//              adds the comment-carry probe §21 requires). Asserts revisions survive WITH authors,
//              the comment is carried with resolvable anchors, the hyperlink relationship resolves,
//              template chrome is adopted, no dangling r-refs, validator clean.
//         (2b) nda-interrupted-clauses.docx — real direct-numPr clause numbering (numId 1) that
//              COLLIDES with the template's own numId 1, plus a table and a real Heading1 style
//              collision. Asserts every body numId resolves post-remap (no dangling numId), the
//              remapped abstract is the SOURCE's verbatim (decimal "%1."), the template's own
//              numbering survives untouched (UpperRoman "%1)"), the table is carried, the template
//              wins the REAL corpus style collision, validator clean.
//
// Banned-pattern compliance (ADR-038 §4 + tests/CLAUDE.md): NO Mock<HttpMessageHandler>; NO
// DI-registration test; NO ctor-null test. Mocks live ONLY at the ISpeFileOperations /
// IComposeTemplateSource module boundaries (ComposeFidelitySeamFixture — the same fixture family
// as every Compose seam suite; no new fixture class per CLAUDE.md §11).

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeTemplateChromeProvenanceSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeTemplateChromeProvenanceSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private const string DriveId = "drive-033-chrome-provenance-seam";
    private const string ItemId = "spe-item-033-chrome-provenance-seam";
    private const string TemplateTitle = "Firm Standard 033";

    // Template chrome — DISTINCTIVE so provenance is unambiguous (task constraint).
    private const string FirmHeaderText = "FIRM HEADER — Chrome Provenance Seam LLP";
    private const string FirmFooterText = "FIRM FOOTER — Privileged & Confidential";
    private const string TemplateBoilerplate = "TEMPLATE BOILERPLATE — must be dropped";
    private const string TemplateHeading1Color = "AA0000"; // the collision probe: template must win

    // Editor/document chrome that must LOSE.
    private const string EditorHeaderText = "EDITOR HEADER — must lose";
    private const string EditorHeading1Color = "00AA00";
    private const string EditorHeadingText = "Confidentiality Obligations";
    private const string EditorBodyText = "The receiving party shall hold all Confidential Information in strict confidence.";

    // Imported-carrier probes.
    private const string CarrierCommentText = "Please verify this clause against the precedent library.";
    private const string CarrierCommentAuthor = "Reviewer Riley";
    private const string CorpusHyperlinkUri = "https://precedents.example.com/library";

    // ── real OOXML builders (the engine under the route is the REAL 030 engine) ──────────────────

    /// <summary>The firm .dotx: distinctive Heading1 (style-collision probe) + FirmBody template-only
    /// style, its own numbering (numId 1 — deliberately colliding with the NDA corpus doc's numId 1),
    /// header AND footer parts wired through a LANDSCAPE sectPr with distinctive margins, and
    /// boilerplate body content the merge must drop.</summary>
    private static byte[] BuildFirmTemplateDotx()
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
                new Style(new StyleName { Val = "Normal" })
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

    /// <summary>The CONTESTED document: carries its OWN conflicting Heading1 definition, its OWN
    /// header part, and its OWN portrait sectPr (1440 margins) referencing that header — chrome
    /// the merge must REPLACE with the template's. 032's document carried no chrome at all, so
    /// this is the first through-the-wire slice where provenance is actually contested.</summary>
    private static byte[] BuildContestedDocumentBytes()
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, autoSave: true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document();
            var body = main.Document.AppendChild(new Body());

            body.AppendChild(new Paragraph(
                new ParagraphProperties(new ParagraphStyleId { Val = "Heading1" }),
                new Run(new Text(EditorHeadingText))));
            body.AppendChild(new Paragraph(new Run(new Text(EditorBodyText))));

            var stylesPart = main.AddNewPart<StyleDefinitionsPart>();
            stylesPart.Styles = new Styles(
                new Style(new StyleName { Val = "Normal" })
                { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true },
                new Style(
                    new StyleName { Val = "heading 1" },
                    new StyleRunProperties(new Color { Val = EditorHeading1Color }))
                { Type = StyleValues.Paragraph, StyleId = "Heading1" });

            var headerPart = main.AddNewPart<HeaderPart>();
            headerPart.Header = new Header(new Paragraph(new Run(new Text(EditorHeaderText))));

            body.AppendChild(new SectionProperties(
                new HeaderReference { Type = HeaderFooterValues.Default, Id = main.GetIdOfPart(headerPart) },
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 }));

            main.Document.Save();
        }
        return stream.ToArray();
    }

    // ── shared wire plumbing (mirrors ComposeApplyTemplateSeamTests exactly) ─────────────────────

    private static FileHandleDto ReplacedDriveItem() => new(
        Id: "9.0",
        Name: "merged-provenance-seam.docx",
        ParentId: null,
        Size: 23456,
        CreatedDateTime: DateTimeOffset.UtcNow,
        LastModifiedDateTime: DateTimeOffset.UtcNow,
        ETag: "\"etag-seam-merged-033\"",
        IsFolder: false,
        WebUrl: "https://spe/web/merged-provenance-seam",
        DriveId: DriveId);

    /// <summary>Arms both boundaries and drives the document through apply-template over the wire,
    /// returning the PERSISTED bytes captured at the SPE facade boundary.</summary>
    private async Task<byte[]> ApplyTemplateThroughWireAsync(byte[] documentBytes)
    {
        _fixture.ResetBoundaries();

        _fixture.TemplateSourceMock
            .Setup(t => t.ResolveAsync(
                TemplateTitle, It.IsAny<Dictionary<string, object?>?>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ComposeResolvedTemplate(
                TemplateBytes: BuildFirmTemplateDotx(),
                TemplateName: TemplateTitle,
                FileName: "firm-standard-033.dotx",
                VariablesRendered: false));

        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(documentBytes));

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var buffer = new MemoryStream();
                stream.CopyTo(buffer);
                persisted = buffer.ToArray();
            })
            .ReturnsAsync(ReplacedDriveItem());

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{ItemId}/apply-template",
            new { driveId = DriveId, templateIdOrName = TemplateTitle });

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"apply-template must merge + persist — body: {await response.Content.ReadAsStringAsync()}");

        var payload = await response.Content.ReadFromJsonAsync<ApplyTemplateSeamResponse>();
        payload!.VersionId.Should().Be("9.0", "the new SPE version rides the response for the client re-mount");
        payload.MergeWarnings.Should().BeNullOrEmpty(
            "a clean merge must not cry wolf — a template-merge-* warning here means a part was degraded, not merged");

        persisted.Should().NotBeNull("the merged document must be persisted at the SPE facade boundary");
        return persisted!;
    }

    private static WordprocessingDocument Open(byte[] docx) =>
        WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);

    private static void AssertTemplateChromeAdopted(WordprocessingDocument doc)
    {
        var main = doc.MainDocumentPart!;
        var sectPr = main.Document!.Body!.Elements<SectionProperties>().Last();

        var pageSize = sectPr.GetFirstChild<PageSize>()!;
        pageSize.Orient!.Value.Should().Be(PageOrientationValues.Landscape,
            "the trailing sectPr (page geometry) must be the TEMPLATE's");
        pageSize.Width!.Value.Should().Be(15840u);
        sectPr.GetFirstChild<PageMargin>()!.Top!.Value.Should().Be(720,
            "the TEMPLATE's margins (720) must win over the document's (1440)");

        var headerRef = sectPr.GetFirstChild<HeaderReference>();
        headerRef.Should().NotBeNull("the merged sectPr must reference the template's header");
        ((HeaderPart)main.GetPartById(headerRef!.Id!.Value!)).Header!.InnerText.Should().Contain(FirmHeaderText,
            "the template's header part travels with its sectPr reference — header provenance");

        var footerRef = sectPr.GetFirstChild<FooterReference>();
        footerRef.Should().NotBeNull("the merged sectPr must reference the template's footer");
        ((FooterPart)main.GetPartById(footerRef!.Id!.Value!)).Footer!.InnerText.Should().Contain(FirmFooterText,
            "the template's footer part travels with its sectPr reference — footer provenance (not asserted by 032)");
    }

    private static void AssertPackageValidates(WordprocessingDocument doc)
    {
        doc.DocumentType.Should().Be(WordprocessingDocumentType.Document,
            "the output is a plain document (the template package re-typed), never a .dotx");
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc)
            .Select(e => $"{e.Id}: {e.Description} @ {e.Path?.XPath}")
            .ToList();
        errors.Should().BeEmpty("the PERSISTED package must open clean in Word (no repair prompt)");
    }

    /// <summary>Validator net for a carrier whose SOURCE bytes are not themselves validator-clean.
    /// `nda-interrupted-clauses.docx` (a synthetic R4.5 fixture) carries 8 spec-invalid
    /// `w14:paraId` values (≥ 0x80000000 — the OOXML MaxExclusive bound; verified against the RAW
    /// corpus bytes, identical 8 ids). The merge engine carries source content VERBATIM — correct:
    /// re-minting existing paraIds would break paraId-keyed anchor identity across sessions — so
    /// those inherited errors ride through. The honest assertion is therefore "the merge introduces
    /// NO NEW validation error": every merged-output error must already exist, by (Id, Description)
    /// identity, on the source document itself. This is NOT a weakened net — a dangling rId, a
    /// schema-order violation, or any error the MERGE causes still fails loudly.</summary>
    private static void AssertMergeIntroducesNoNewValidationErrors(byte[] sourceBytes, WordprocessingDocument mergedDoc)
    {
        mergedDoc.DocumentType.Should().Be(WordprocessingDocumentType.Document,
            "the output is a plain document (the template package re-typed), never a .dotx");

        var validator = new OpenXmlValidator(FileFormatVersions.Office2019);
        List<string> sourceBaseline;
        using (var source = Open(sourceBytes))
        {
            sourceBaseline = validator.Validate(source).Select(e => $"{e.Id}: {e.Description}").ToList();
        }
        sourceBaseline.Should().NotBeEmpty(
            "this helper exists ONLY for a source with a known validation baseline — if the fixture " +
            "has been cleaned up, switch this slice back to the strict AssertPackageValidates");

        var newErrors = validator.Validate(mergedDoc)
            .Select(e => $"{e.Id}: {e.Description}")
            .Where(e => !sourceBaseline.Contains(e))
            .ToList();
        newErrors.Should().BeEmpty(
            "the merge must introduce NO validation error beyond the source document's own pre-existing baseline");
    }

    private static void AssertNoDanglingMainPartReferences(MainDocumentPart main)
    {
        var known = main.Parts.Select(p => p.RelationshipId)
            .Concat(main.HyperlinkRelationships.Select(h => h.Id))
            .Concat(main.ExternalRelationships.Select(e => e.Id))
            .ToHashSet(StringComparer.Ordinal);
        main.Document!.Body!.Descendants()
            .SelectMany(d => d.GetAttributes())
            .Where(a => a.NamespaceUri == "http://schemas.openxmlformats.org/officeDocument/2006/relationships")
            .Select(a => a.Value)
            .OfType<string>()
            .Should().OnlyContain(id => known.Contains(id),
                "no r:-namespace reference in the persisted body may dangle (Word repair-prompt proxy)");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (1) CONTESTED chrome — the document's own styles/header/sectPr must LOSE to the template's,
    //     on the persisted bytes, through the wire. Success Criterion 3's provenance assertion
    //     where both sides actually bring chrome to the fight.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplate_ContestedChrome_TemplateStylesSectPrHeaderFooterWinOnPersistedBytes()
    {
        var persisted = await ApplyTemplateThroughWireAsync(BuildContestedDocumentBytes());

        using var doc = Open(persisted);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        // styles.xml provenance — the TEMPLATE's distinctive Heading1 definition wins the id collision.
        var heading1Defs = main.StyleDefinitionsPart!.Styles!.Elements<Style>()
            .Where(s => s.StyleId?.Value == "Heading1").ToList();
        heading1Defs.Should().HaveCount(1, "a style id must stay unique after the merge");
        heading1Defs[0].StyleRunProperties?.GetFirstChild<Color>()?.Val?.Value
            .Should().Be(TemplateHeading1Color,
                "on style-id collision the TEMPLATE's definition wins — that IS house style (the document's " +
                EditorHeading1Color + " must be gone)");
        main.StyleDefinitionsPart.Styles.Elements<Style>()
            .Should().Contain(s => s.StyleId != null && s.StyleId.Value == "FirmBody",
                "template-only styles survive onto the persisted bytes");

        // sectPr + header + footer provenance — template geometry and parts, document's gone.
        AssertTemplateChromeAdopted(doc);
        main.HeaderParts.Should().OnlyContain(h => !h.Header!.InnerText.Contains(EditorHeaderText),
            "the document's own header part must NOT survive the merge under any reference");

        // Body provenance — the document's content, still bound to the (now template-defined) style.
        body.InnerText.Should().Contain(EditorHeadingText).And.Contain(EditorBodyText,
            "the DOCUMENT's body is the content source");
        body.InnerText.Should().NotContain(TemplateBoilerplate, "the template contributes chrome, not content");
        body.Descendants<Paragraph>()
            .Single(p => p.InnerText.Contains(EditorHeadingText, StringComparison.Ordinal))
            .ParagraphProperties!.ParagraphStyleId!.Val!.Value.Should().Be("Heading1",
                "the heading keeps its style binding — which now resolves to the template's definition");

        AssertNoDanglingMainPartReferences(main);
        AssertPackageValidates(doc);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (2a) IMPORTED CARRIER — the REAL multi-author redline corpus doc through the wire (§21).
    //      Tracked revisions survive with authors; the anchored comment is carried; the tracked-
    //      inserted hyperlink's relationship resolves; template chrome adopted; validator clean.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplate_ImportedRedlineCarrier_RevisionsCommentsAndHyperlinkSurviveWithTemplateChrome()
    {
        var corpusPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => Path.GetFileName(p) == "multi-author-redline-synthetic.docx");
        var carrierBytes = EnrichWithAnchoredComment(ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusPath));

        var persisted = await ApplyTemplateThroughWireAsync(carrierBytes);

        using var doc = Open(persisted);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;

        // Tracked INSERTIONS survive with authors (Alice's mid-paragraph insert; Bob's replace-pair insert).
        var inserts = body.Descendants<InsertedRun>().ToList();
        inserts.Should().Contain(i => i.Author!.Value == "Alice Chen" && i.InnerText.Contains("Confidential"),
            "Alice's tracked insert must survive the merge verbatim, author intact");
        inserts.Should().Contain(i => i.Author!.Value == "Bob Rivera" && i.InnerText.Contains("three (3)"),
            "Bob's replace-pair insert must survive with authorship");

        // Tracked DELETION survives with author + delText.
        var deletion = body.Descendants<DeletedRun>().Single();
        deletion.Author!.Value.Should().Be("Bob Rivera");
        deletion.Descendants<DeletedText>().Single().Text.Should().Be("two (2) ",
            "the deleted text must ride the w:del verbatim — a lost redline is a silent legal-content change");

        // Formatting revisions + paragraph-mark deletion survive (the carrier class's exotic corners).
        body.Descendants<RunPropertiesChange>().Single().Author!.Value.Should().Be("Alice Chen",
            "the w:rPrChange (tracked bold) must be carried");
        body.Descendants<ParagraphPropertiesChange>().Single().Author!.Value.Should().Be("Bob Rivera",
            "the w:pPrChange (tracked centering) must be carried");
        body.Descendants<Paragraph>().Should().Contain(
            p => p.ParagraphProperties != null
                && p.ParagraphProperties.Descendants<Deleted>().Any(d => d.Author!.Value == "Alice Chen"),
            "the paragraph-MARK deletion (tracked merge of two paragraphs) must be carried");

        // The anchored comment is carried and its anchors resolve on the persisted bytes.
        var commentId = body.Descendants<CommentReference>().Single().Id!.Value!;
        body.Descendants<CommentRangeStart>().Single().Id!.Value.Should().Be(commentId);
        body.Descendants<CommentRangeEnd>().Single().Id!.Value.Should().Be(commentId);
        var comment = main.WordprocessingCommentsPart!.Comments!.Elements<Comment>()
            .Single(c => c.Id?.Value == commentId);
        comment.InnerText.Should().Contain(CarrierCommentText);
        comment.Author!.Value.Should().Be(CarrierCommentAuthor);

        // The tracked-inserted hyperlink's relationship is re-created on the MERGED main part.
        var hyperlink = body.Descendants<Hyperlink>().Single();
        hyperlink.Descendants<InsertedRun>().Single().Author!.Value.Should().Be("Bob Rivera",
            "the Word-canonical w:hyperlink ⊃ w:ins nesting must survive");
        var rel = main.HyperlinkRelationships.SingleOrDefault(h => h.Id == hyperlink.Id?.Value);
        rel.Should().NotBeNull("the carried hyperlink's r:id must resolve on the merged main part");
        rel!.Uri.OriginalString.Should().Be(CorpusHyperlinkUri);

        // Body text (the non-revised prose) is intact.
        body.InnerText.Should().Contain("MUTUAL CONFIDENTIALITY AGREEMENT");

        // Template chrome adopted over the carrier's portrait sectPr.
        AssertTemplateChromeAdopted(doc);
        AssertNoDanglingMainPartReferences(main);
        AssertPackageValidates(doc);
    }

    /// <summary>Grafts an anchored comment onto the REAL corpus redline bytes. No corpus document
    /// carries live comments (corpus-manifest.md §1/§1.5 — comments columns all "No"), and §21's
    /// carrier slice requires the comment-carry probe; enriching the real bytes keeps the live
    /// multi-author revision content authentic while adding the one missing carrier feature.</summary>
    private static byte[] EnrichWithAnchoredComment(byte[] corpusBytes)
    {
        using var stream = new MemoryStream();
        stream.Write(corpusBytes, 0, corpusBytes.Length);
        stream.Position = 0;
        using (var doc = WordprocessingDocument.Open(stream, isEditable: true))
        {
            var main = doc.MainDocumentPart!;
            var commentsPart = main.AddNewPart<WordprocessingCommentsPart>();
            commentsPart.Comments = new Comments(
                new Comment(new Paragraph(new Run(new Text(CarrierCommentText))))
                {
                    Id = "42",
                    Author = CarrierCommentAuthor,
                    Date = System.Xml.XmlConvert.ToDateTime("2026-08-03T09:00:00Z", System.Xml.XmlDateTimeSerializationMode.Utc),
                });

            var target = main.Document!.Body!.Elements<Paragraph>()
                .First(p => p.InnerText.Contains("reasonable care", StringComparison.Ordinal));
            target.InsertAt(new CommentRangeStart { Id = "42" }, 0);
            target.AppendChild(new CommentRangeEnd { Id = "42" });
            target.AppendChild(new Run(new CommentReference { Id = "42" }));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (2b) IMPORTED CARRIER — the REAL NDA corpus doc (direct-numPr clauses + table + Heading1)
    //      through the wire, against a template whose OWN numId 1 collides with the carrier's.
    //      Numbering must remap without dangling refs and without recomputation; the table must
    //      carry; the template must win the REAL corpus style collision.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ApplyTemplate_ImportedNdaCarrier_NumberingRemapsTableCarriesAndTemplateStyleWins()
    {
        var corpusPath = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => Path.GetFileName(p) == "nda-interrupted-clauses.docx");
        var carrierBytes = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusPath);

        var persisted = await ApplyTemplateThroughWireAsync(carrierBytes);

        using var doc = Open(persisted);
        var main = doc.MainDocumentPart!;
        var body = main.Document!.Body!;
        var numbering = main.NumberingDefinitionsPart!.Numbering!;

        // Every body numId resolves inside the merged numbering part — no dangling numId post-remap.
        var mergedNumIds = numbering.Elements<NumberingInstance>().Select(n => n.NumberID!.Value).ToHashSet();
        var bodyNumIds = body.Descendants<NumberingId>().Select(n => n.Val!.Value).Distinct().ToList();
        bodyNumIds.Should().NotBeEmpty("the NDA's numbered clauses must still be list-numbered");
        bodyNumIds.Should().OnlyContain(id => mergedNumIds.Contains(id),
            "no dangling numId after the collision remap");

        // The carrier's numId 1 COLLIDED with the template's numId 1 — the body must have been
        // remapped AWAY (not left to silently capture the template's UpperRoman scheme), and the
        // remapped definition must be the SOURCE's verbatim decimal "%1." (identity, not recompute).
        var clauseNumId = bodyNumIds.Single();
        clauseNumId.Should().NotBe(1,
            "the carrier's clause numbering must be remapped off the colliding template numId 1");
        var clauseNum = numbering.Elements<NumberingInstance>().Single(n => n.NumberID!.Value == clauseNumId);
        var clauseAbs = numbering.Elements<AbstractNum>()
            .Single(a => a.AbstractNumberId!.Value == clauseNum.GetFirstChild<AbstractNumId>()!.Val!.Value);
        var clauseLvl0 = clauseAbs.Elements<Level>().First(l => l.LevelIndex?.Value == 0);
        clauseLvl0.GetFirstChild<NumberingFormat>()!.Val!.Value.Should().Be(NumberFormatValues.Decimal);
        clauseLvl0.GetFirstChild<LevelText>()!.Val!.Value.Should().Be("%1.",
            "the abstractNum is cloned VERBATIM from the source — clause labels 1.–6. stay identical by construction");

        // The template's OWN numbering survives untouched under its original id.
        var templateNum = numbering.Elements<NumberingInstance>().Single(n => n.NumberID?.Value == 1);
        var templateAbs = numbering.Elements<AbstractNum>()
            .Single(a => a.AbstractNumberId!.Value == templateNum.GetFirstChild<AbstractNumId>()!.Val!.Value);
        templateAbs.Elements<Level>().First().GetFirstChild<NumberingFormat>()!.Val!.Value
            .Should().Be(NumberFormatValues.UpperRoman, "the template's own numbering must survive the merge");

        // The REAL corpus style collision: the NDA brings its own Heading1; the template's wins.
        var heading1Defs = main.StyleDefinitionsPart!.Styles!.Elements<Style>()
            .Where(s => s.StyleId?.Value == "Heading1").ToList();
        heading1Defs.Should().HaveCount(1);
        heading1Defs[0].StyleRunProperties?.GetFirstChild<Color>()?.Val?.Value
            .Should().Be(TemplateHeading1Color, "the template wins the REAL corpus Heading1 collision through the wire");
        body.Descendants<Paragraph>()
            .Single(p => p.InnerText.Contains("ARTICLE II", StringComparison.Ordinal))
            .ParagraphProperties!.ParagraphStyleId!.Val!.Value.Should().Be("Heading1",
                "the carrier's heading keeps its style binding");

        // The table carries intact.
        var table = body.Descendants<Table>().Single();
        table.InnerText.Should().Contain("Party").And.Contain("Role",
            "the carrier's table content must ride the merge");

        // Body clause text intact.
        body.InnerText.Should().Contain("NON-DISCLOSURE AGREEMENT")
            .And.Contain("Confidentiality Obligations")
            .And.Contain("irreparable harm");
        body.InnerText.Should().NotContain(TemplateBoilerplate);

        AssertTemplateChromeAdopted(doc);
        AssertNoDanglingMainPartReferences(main);
        // The NDA corpus fixture itself is NOT validator-clean (8 spec-invalid w14:paraId values,
        // pre-existing on the RAW bytes) — see the helper's doc comment. Assert no NEW errors.
        AssertMergeIntroducesNoNewValidationErrors(carrierBytes, doc);
    }

    /// <summary>Deserialization target for the 200 response (the fields this seam asserts).</summary>
    private sealed record ApplyTemplateSeamResponse(
        string DocumentSpeId,
        string? DriveId,
        string TemplateName,
        string VersionId,
        string? ETag,
        long? Size,
        string CorrelationId,
        IReadOnlyList<MergeWarning>? MergeWarnings);

    private sealed record MergeWarning(string Code, int Count, string Message);
}
