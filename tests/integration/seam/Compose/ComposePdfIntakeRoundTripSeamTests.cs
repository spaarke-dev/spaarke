// Task 042 (spaarkeai-compose-r6, FR-06 + Success Criterion 2) — the END-TO-END PDF round-trip seam:
// a PDF NDA OPENS in Compose (task 040 intake: DocumentLayout → canonical model → synthesized docx),
// carries the HONEST-lossiness data (sourceFormat:"pdf" + counted pdf-intake-* warnings — what the 041
// client banner renders), is EDITED, and SAVES AS A NEW DOCX via render-on-save (create-on-save — never
// docx bytes over the .pdf item) with the source record's links inherited (B-MED-3, option C).
//
// Through-the-wire WebApplicationFactory over the REAL endpoints + REAL ComposeService (PDF branch) +
// REAL ComposePdfModelProjector + REAL ComposeDocumentRenderer + REAL projection builders. Mocks live
// ONLY at the module boundaries (ADR-038): ISpeFileOperations / IGenericEntityService / indexing, and
// the IComposePdfIntakeSource PublicContracts seam (the Azure Document Intelligence call — external by
// definition; the Azure-DI-shape → DocumentLayout mapping is covered by TextExtractorLayoutMappingTests
// via DocumentIntelligenceModelFactory). NO Mock<HttpMessageHandler>, NO DI-registration test, NO
// ctor-null test.
//
// The honest-lossiness UX MESSAGE itself renders client-side (ComposeBannerStack "Opened from PDF") —
// asserted by the client jest suites; THIS seam asserts the SERVER data contract that drives it
// (sourceFormat marker + the always-present pdf-intake-fixed-layout-reflowed warning) plus the honest
// 503/422 endpoint mapping (typed ComposePdfIntakeException — 041-review HIGH-1/HIGH-2 lock-ins).
//
// MAINTAIN-class (/test-diet KEEP — tests/integration/seam/** vertical-slice KEEP path per ADR-038):
// the standing regression net for Success Criterion 2. Per the task escalation trigger, a hard-fail at
// any stage (intake 422, save-not-a-docx, missing lossiness data) FAILS this test loudly — the
// assertions are not weakened.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.PublicContracts;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposePdfIntakeRoundTripSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposePdfIntakeRoundTripSeamTests(ComposeFidelitySeamFixture fixture)
    {
        _fixture = fixture;
    }

    private const string PdfItemId = "spe-item-pdf-nda-001";
    private const string PdfDriveId = "drive-pdf-nda-001";
    private const string PdfFileName = "Corteva NDA (signed).pdf";
    private const string EditMarker = "(edited via the PDF-intake seam)";

    /// <summary>Minimal real %PDF- header bytes — enough for the bytes-first source detection
    /// (IsPdfSource) to route the load onto the intake branch.</summary>
    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< >>\n%%EOF\n");

    /// <summary>An NDA-shaped structured layout — what the 040 intake seam yields for a real signed
    /// NDA: title, section headings, body prose, a bullet line, a term table, repeating page chrome,
    /// across 2 fixed pages.</summary>
    private static DocumentLayout NdaLayout() => new()
    {
        PageCount = 2,
        Blocks = new[]
        {
            Para("CONFIDENTIAL", DocumentLayoutParagraphRole.PageHeader),
            Para("MUTUAL NON-DISCLOSURE AGREEMENT", DocumentLayoutParagraphRole.Title),
            Para("1. Confidential Information", DocumentLayoutParagraphRole.SectionHeading),
            Para("Each party agrees to hold in confidence all Confidential Information disclosed by the other party."),
            Para("• Trade secrets and know-how"),
            new DocumentLayoutBlock
            {
                Table = new DocumentLayoutTable(
                    RowCount: 1,
                    ColumnCount: 2,
                    Cells: new[]
                    {
                        new DocumentLayoutTableCell(0, 0, 1, 1, "Term", IsHeader: true),
                        new DocumentLayoutTableCell(0, 1, 1, 1, "Two (2) years", IsHeader: false),
                    },
                    PageNumber: 2),
            },
            Para("2. Term", DocumentLayoutParagraphRole.SectionHeading),
            Para("This Agreement remains in effect for the period stated above."),
            Para("Page 1 of 2", DocumentLayoutParagraphRole.PageNumber),
        },
    };

    private static DocumentLayoutBlock Para(string text, DocumentLayoutParagraphRole role = DocumentLayoutParagraphRole.Body)
        => new() { Paragraph = new DocumentLayoutParagraph(text, role, 1) };

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (A) Success Criterion 2 — the full round trip: PDF opens → honest data → edit → saves as a
    //     NEW docx (create-on-save; the .pdf item untouched) with source-record links inherited.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PdfNda_OpensEditsAndSavesAsNewDocx_WithHonestLossinessDataAndInheritedLinks()
    {
        _fixture.ResetBoundaries();

        var sourceDocumentId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var newDocumentId = Guid.NewGuid();
        const string containerId = "b!container-bu-pdf-seam";
        const string mintedDocxItemId = "spe-item-pdf-docx-seam-001";
        const string mintedDriveId = "drive-pdf-docx-seam-001";

        // ── Boundary: SPE serves the PDF's metadata + bytes on load ───────────────────────────────
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, PdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: PdfItemId, Name: PdfFileName, ParentId: null, Size: PdfBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"pdf-v1\"", IsFolder: false, WebUrl: null, DriveId: PdfDriveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, PdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(PdfBytes, writable: false));

        // ── Boundary: the intake seam yields the NDA-shaped layout (the Azure DI double) ─────────
        _fixture.PdfIntakeSourceMock
            .Setup(p => p.ParseAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(NdaLayout());

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: OPEN the PDF over the real load route ──────────────────────────────────────────
        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{PdfItemId}?driveId={PdfDriveId}&tenantId={ComposeFidelitySeamFixture.TestTenantId}" +
            $"&documentRecordId={sourceDocumentId}");

        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "a PDF NDA must OPEN through the intake — a hard-fail here is the exact 422 class this project retired");
        var load = JsonNode.Parse(await loadResponse.Content.ReadAsStringAsync())!.AsObject();

        // Honest-lossiness DATA contract (drives the 041 client banner):
        load["sourceFormat"]!.GetValue<string>().Should().Be("pdf",
            "the client keys the honest-lossiness UX + create-on-save routing off this marker");
        var warningCodes = load["contentModelWarnings"]!.AsArray()
            .Select(w => w!["code"]!.GetValue<string>())
            .ToList();
        warningCodes.Should().Contain("pdf-intake-fixed-layout-reflowed",
            "the fixed-layout reflow fact is ALWAYS surfaced — a PDF projection never claims to be identical to source");
        warningCodes.Should().Contain("pdf-intake-page-chrome-dropped",
            "the dropped page header/number are counted loudly, never silent");
        load["versionId"]!.Should().BeNull(
            "the .pdf item's version id is a save-path booby trap and must not be surfaced (041-review MEDIUM-3)");

        // The served Content is a SYNTHESIZED DOCX (PK zip), not the PDF bytes:
        var contentBase64 = load["content"]!.GetValue<string>();
        var synthesized = Convert.FromBase64String(contentBase64);
        synthesized.Take(4).Should().Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 },
            "the intake synthesizes a first-class docx carrier from the canonical model");
        var contentModel = load["contentModel"]!.AsObject();
        contentModel["blocks"]!.AsArray().Count.Should().BeGreaterThan(3,
            "the canonical model carries the NDA's headings/prose/list/table (page chrome dropped)");

        // ── Act 2: EDIT the retained model (mirrors the 041 client's retain-merge-repost) ─────────
        var firstTextRun = contentModel["blocks"]!.AsArray()
            .Select(b => b!.AsObject())
            .Where(b => b["runs"] is JsonArray { Count: > 0 })
            .Select(b => b["runs"]![0]!.AsObject())
            .First(r => !string.IsNullOrWhiteSpace(r["text"]?.GetValue<string>()));
        firstTextRun["text"] = firstTextRun["text"]!.GetValue<string>() + " " + EditMarker;

        // ── Boundary: create-on-save mints the NEW docx item + record; capture what persists ──────
        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(containerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(mintedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), mintedDriveId, It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(new FileHandleDto(
                Id: mintedDocxItemId, Name: "Corteva NDA (signed).docx", ParentId: null, Size: 1,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"docx-v1\"", IsFolder: false, WebUrl: null, DriveId: mintedDriveId));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        // B-MED-3: the SOURCE PDF's record carries a matter link the new docx must inherit.
        var sourceEntity = new Entity("sprk_document", sourceDocumentId);
        sourceEntity["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync(
                "sprk_document", sourceDocumentId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceEntity);
        Entity? createdEntity = null;
        _fixture.DataverseMock
            .Setup(d => d.CreateAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => createdEntity = e)
            .ReturnsAsync(newDocumentId);
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        // ── Act 3: SAVE — the exact 041 client contract: create-on-save (NEVER the replace route),
        //    merged model + retained synthesized bytes, .docx-swapped name, source record id. ───────
        var saveResponse = await client.PostAsJsonAsync(
            "/api/compose/documents/create-on-save",
            new
            {
                containerId,
                tenantId = ComposeFidelitySeamFixture.TestTenantId,
                sessionId = load["sessionId"]!.GetValue<string>(),
                displayName = "Corteva NDA (signed).docx",
                contentModel = JsonSerializer.Deserialize<JsonElement>(contentModel.ToJsonString()),
                content = synthesized,
                transientKey = $"pdf-seam-{Guid.NewGuid():N}",
                sourceDocumentRecordId = sourceDocumentId,
            });

        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            "the PDF-sourced save renders the edited model into a NEW docx via render-on-save — no 422 by construction");
        var save = JsonNode.Parse(await saveResponse.Content.ReadAsStringAsync())!.AsObject();
        save["documentSpeId"]!.GetValue<string>().Should().Be(mintedDocxItemId,
            "a NEW SPE drive-item was minted — the .pdf item is untouched");
        save["documentRecordId"]!.GetValue<Guid>().Should().Be(newDocumentId);

        // The persisted bytes are a VALID DOCX containing the edit (the docx version of the NDA):
        persisted.Should().NotBeNull();
        persisted!.Take(4).Should().Equal(new byte[] { 0x50, 0x4B, 0x03, 0x04 });
        using (var doc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var bodyText = string.Concat(
                doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));
            bodyText.Should().Contain(EditMarker, "the user's edit landed in the rendered docx");
            bodyText.Should().Contain("MUTUAL NON-DISCLOSURE AGREEMENT", "the NDA content round-tripped");
            bodyText.Should().Contain("Two (2) years", "the term table's cells round-tripped");
            bodyText.Should().NotContain("CONFIDENTIAL\n", "page chrome was dropped, not inlined into the body");
        }

        // B-MED-3: the new record inherits the source PDF's matter link (filed alongside it).
        createdEntity.Should().NotBeNull();
        createdEntity!.GetAttributeValue<EntityReference>("sprk_matter")!.Id.Should().Be(matterId,
            "the new Word document files under the SAME matter as the source PDF (operator option C)");

        // NEGATIVE: nothing wrote onto the .pdf item.
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "a PDF-sourced save must NEVER replace the .pdf drive-item's content");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (B) Honest endpoint mapping — the typed ComposePdfIntakeException lock-ins (041 review).
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PdfLoad_WhenIntakeUnavailable_Returns503WithHonestDetail()
    {
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, PdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: PdfItemId, Name: PdfFileName, ParentId: null, Size: PdfBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"pdf-v1\"", IsFolder: false, WebUrl: null, DriveId: PdfDriveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, PdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(PdfBytes, writable: false));
        // The intake seam resolves null — compound-OFF / parse-service failure (the Null peer's contract).
        _fixture.PdfIntakeSourceMock
            .Setup(p => p.ParseAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((DocumentLayout?)null);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/compose/documents/{PdfItemId}?driveId={PdfDriveId}&tenantId={ComposeFidelitySeamFixture.TestTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable,
            "intake unavailability maps to an honest 503 ProblemDetails, never a generic 500 (041-review HIGH-1)");
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        problem["detail"]!.GetValue<string>().Should().Contain("PDF intake failed",
            "the real, user-presentable reason crosses the wire");
    }

    [Fact]
    public async Task PdfLoad_WhenNothingProjectable_Returns422NotABlankMount()
    {
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, PdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: PdfItemId, Name: PdfFileName, ParentId: null, Size: PdfBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"pdf-v1\"", IsFolder: false, WebUrl: null, DriveId: PdfDriveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, PdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(PdfBytes, writable: false));
        // Only page chrome — nothing projectable (the projector's ONLY Failed outcome).
        _fixture.PdfIntakeSourceMock
            .Setup(p => p.ParseAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DocumentLayout
            {
                PageCount = 1,
                Blocks = new[] { Para("Page 1 of 1", DocumentLayoutParagraphRole.PageNumber) },
            });

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/compose/documents/{PdfItemId}?driveId={PdfDriveId}&tenantId={ComposeFidelitySeamFixture.TestTenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "an unprojectable PDF fails the open LOUDLY — never a silent empty editor over a non-empty PDF");
    }

    [Fact]
    public async Task ReplaceSave_OntoAPdfItem_Returns422PdfCannotBeSavedInPlace()
    {
        _fixture.ResetBoundaries();

        // Version-skew vector (041-review A-HIGH-1): an older client replace-saves valid DOCX content
        // while the TARGET item is still the .pdf. The metadata choke point must refuse.
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, PdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: PdfItemId, Name: PdfFileName, ParentId: null, Size: PdfBytes.Length,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"pdf-v1\"", IsFolder: false, WebUrl: null, DriveId: PdfDriveId));

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{PdfItemId}/save",
            new
            {
                sessionId = Guid.NewGuid().ToString("N"),
                tenantId = ComposeFidelitySeamFixture.TestTenantId,
                driveId = PdfDriveId,
                // A perfectly valid docx baseline (PK bytes) — the baseline guard passes; the TARGET
                // guard is what must fire.
                content = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00 },
            });

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            "docx bytes must never be written over a .pdf drive-item (041-review A-HIGH-1 target guard)");
        var problem = JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
        problem["detail"]!.GetValue<string>().Should().Contain("NEW Word document",
            "the refusal carries the honest instruction, not a generic error");
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "the refusal fires BEFORE any write");
    }
}
