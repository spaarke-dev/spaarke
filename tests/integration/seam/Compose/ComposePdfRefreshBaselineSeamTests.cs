// Task 044 FR-A09 (spaarkeai-compose-r8) — the PDF-sourced SECOND SAVE, across a real page REFRESH.
//
// THE FAILURE THIS FILE EXISTS FOR. A PDF opened in Compose is projected to a synthesized .docx that
// lives only in the client's hands: the `.pdf` drive-item's own version id is deliberately suppressed
// (041-review MEDIUM-3 — re-fetching it would hand %PDF- bytes to the OOXML engine), and the synthesized
// bytes are never stored anywhere the server can find again. The first save mints a NEW .docx item. Then
// the user refreshes.
//
// A refresh destroys every client-held coordinate — the retained bytes, the re-targeted documentRef, and
// the per-mount `transientKey` (minted at each mount door and NEVER persisted, by design:
// composeIdentity.ts). The client re-mounts against the only durable pointer it has: the ORIGINAL PDF.
// So save two arrives with a brand-new transient key, no baseline version coordinates, and a model
// projected from the PDF a second time — and the server has no way to know it already made this document.
//
// The POML is explicit that this must be verified across a REFRESH ("the failure requires losing client
// state"), not two saves in one session — an in-session second save re-targets onto the new .docx via the
// reducer and works fine. That is why every act below re-derives its state from the wire instead of
// reusing the prior act's variables.
//
// MAINTAIN-class (tests/integration/seam/** vertical-slice KEEP path, ADR-038): the standing regression
// net for FR-A09. Mocks live ONLY at module boundaries (SPE / Dataverse / indexing / the Azure-DI intake
// seam); the REAL ComposeService, renderer, projection builders, merge and endpoints stay in play.

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
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposePdfRefreshBaselineSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposePdfRefreshBaselineSeamTests(ComposeFidelitySeamFixture fixture)
    {
        _fixture = fixture;
    }

    private const string PdfDriveId = "drive-pdf-refresh-001";
    private const string PdfFileName = "Master Services Agreement (executed).pdf";
    /// <summary>Issue #858: the container is SERVER-derived (acting user → business unit →
    /// sprk_containerid; arranged by the fixture via <c>TestActingUserBusinessUnit</c>) — the client
    /// no longer names it. This const aliases the arranged value so the specific
    /// <c>ResolveDriveIdAsync(ContainerId, …)</c> matchers below only match when the REAL derivation
    /// produced it: the mint silently proves server-side resolution on every one of these tests.</summary>
    private const string ContainerId = TestActingUserBusinessUnit.ContainerId;
    private const string MintedDriveId = "drive-pdf-docx-refresh-001";

    /// <summary>The edit the FIRST save lands. If the second save is resolving the right baseline, this
    /// text is still in the document afterwards — it is the whole measurement.</summary>
    private const string FirstSaveEdit = "(first save — added before the refresh)";

    /// <summary>The edit the SECOND save lands, after the refresh.</summary>
    private const string SecondSaveEdit = "(second save — added after the refresh)";

    private static readonly byte[] PdfBytes = Encoding.ASCII.GetBytes(
        "%PDF-1.7\n1 0 obj\n<< /Type /Catalog >>\nendobj\ntrailer\n<< >>\n%%EOF\n");

    private static DocumentLayout MsaLayout() => new()
    {
        PageCount = 2,
        Blocks = new[]
        {
            Para("MASTER SERVICES AGREEMENT", DocumentLayoutParagraphRole.Title),
            Para("1. Scope of Services", DocumentLayoutParagraphRole.SectionHeading),
            Para("Provider shall perform the services described in each Statement of Work."),
            Para("2. Fees", DocumentLayoutParagraphRole.SectionHeading),
            Para("Client shall pay the fees set forth in the applicable Statement of Work."),
            Para("3. Term and Termination", DocumentLayoutParagraphRole.SectionHeading),
            Para("This Agreement continues until terminated in accordance with this Section 3."),
        },
    };

    private static DocumentLayoutBlock Para(string text, DocumentLayoutParagraphRole role = DocumentLayoutParagraphRole.Body)
        => new() { Paragraph = new DocumentLayoutParagraph(text, role, 1) };

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The measurement / regression: open a PDF → edit → save → REFRESH → edit → save.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PdfSourced_SecondSaveAfterRefresh_ResolvesTheCreatedDocxAndKeepsTheFirstSavesWork()
    {
        _fixture.ResetBoundaries();

        var pdfItemId = $"spe-item-pdf-refresh-{Guid.NewGuid():N}";
        var pdfRecordId = Guid.NewGuid();
        var matterId = Guid.NewGuid();
        var createdRecordId = Guid.NewGuid();
        var mintedDocxItemId = $"spe-item-docx-refresh-{Guid.NewGuid():N}";

        // ── Boundaries ────────────────────────────────────────────────────────────────────────────
        ArrangePdfSource(pdfItemId);

        // The SPE item store: every mint/replace lands here so a later download serves the CURRENT
        // bytes — this is what makes a second save's baseline resolution observable rather than mocked
        // away. A dictionary is the honest double for "storage that remembers"; the alternative
        // (a canned byte array) would make the test pass regardless of which baseline resolved.
        var speItems = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var mintedItemIds = new List<string>();
        var docxVersions = new Dictionary<string, List<byte[]>>(StringComparer.Ordinal);

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MintedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string _, string name, Stream stream, CancellationToken _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                // Every mint after the first is a DUPLICATE document — the id is unique per call so the
                // count, not an overwrite, is what the assertions see.
                var id = mintedItemIds.Count == 0 ? mintedDocxItemId : $"{mintedDocxItemId}-dup{mintedItemIds.Count}";
                mintedItemIds.Add(id);
                speItems[id] = ms.ToArray();
                docxVersions[id] = new List<byte[]> { ms.ToArray() };
                return BuildHandle(id, name, MintedDriveId, speItems[id].Length, "\"docx-v1\"");
            });

        // Replace + version download over the SAME store, so save two can genuinely re-fetch save one's
        // bytes when it resolves the right coordinates.
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string _, string itemId, Stream stream, CancellationToken _) =>
                RecordReplace(speItems, docxVersions, itemId, stream));
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(), It.IsAny<Stream>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string _, string itemId, Stream stream, string _, CancellationToken _) =>
                RecordReplace(speItems, docxVersions, itemId, stream));
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string _, string itemId, CancellationToken _) =>
                speItems.TryGetValue(itemId, out var bytes)
                    ? BuildHandle(itemId, "Master Services Agreement (executed).docx", MintedDriveId, bytes.Length,
                        $"\"docx-v{docxVersions[itemId].Count}\"")
                    : null);
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string _, string itemId, CancellationToken _) =>
                speItems.TryGetValue(itemId, out var bytes) ? new MemoryStream(bytes, writable: false) : null);
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string _, string itemId, CancellationToken _) =>
                docxVersions.TryGetValue(itemId, out var versions) ? $"v{versions.Count}" : null);
        _fixture.SpeMock
            .Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((HttpContext _, string _, string itemId, string versionId, CancellationToken _) =>
            {
                if (!docxVersions.TryGetValue(itemId, out var versions)) return null;
                var index = int.TryParse(versionId.TrimStart('v'), out var n) ? n - 1 : -1;
                return index >= 0 && index < versions.Count
                    ? new MemoryStream(versions[index], writable: false)
                    : null;
            });

        // Dataverse: rows REMEMBER. The promotion's idempotency check looks a row up by its SPE
        // drive-item id (alt key sprk_graphitemid_uk) before creating one, so a double that answers "no
        // such row" forever would report a second row on the second save no matter what the code did —
        // it would be measuring the double, not the behavior. The transient-key lookup still misses, and
        // that is the real condition under test: the refresh minted a NEW per-mount key.
        var rowsByGraphItemId = new Dictionary<string, Entity>(StringComparer.Ordinal);
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, KeyAttributeCollection keys, string[] _, CancellationToken _) =>
                keys.TryGetValue("sprk_graphitemid", out var itemId)
                    && itemId is string id
                    && rowsByGraphItemId.TryGetValue(id, out var row)
                        ? row
                        : null!);
        var sourceEntity = new Entity("sprk_document", pdfRecordId);
        sourceEntity["sprk_matter"] = new EntityReference("sprk_matter", matterId);
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync("sprk_document", pdfRecordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(sourceEntity);
        var promotions = new List<Entity>();
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) =>
            {
                promotions.Add(e);
                var created = new Entity("sprk_document", createdRecordId);
                foreach (var attribute in e.Attributes)
                {
                    created[attribute.Key] = attribute.Value;
                }
                if (e.GetAttributeValue<string>("sprk_graphitemid") is { Length: > 0 } graphItemId)
                {
                    rowsByGraphItemId[graphItemId] = created;
                }
            })
            .ReturnsAsync((createdRecordId, true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: open the PDF ───────────────────────────────────────────────────────────────────
        var firstLoad = await LoadAsync(client, pdfItemId, pdfRecordId);
        firstLoad["sourceFormat"]!.GetValue<string>().Should().Be("pdf");

        var firstModel = firstLoad["contentModel"]!.AsObject();
        AppendToFirstRun(firstModel, FirstSaveEdit);

        // ── Act 2: first save — create-on-save, exactly the 041 client contract ───────────────────
        var firstSave = await CreateOnSaveAsync(
            client,
            sessionId: firstLoad["sessionId"]!.GetValue<string>(),
            model: firstModel,
            content: Convert.FromBase64String(firstLoad["content"]!.GetValue<string>()),
            transientKey: $"pdf-refresh-mount-1-{Guid.NewGuid():N}",
            sourceDocumentRecordId: pdfRecordId);

        firstSave.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstSaveBody = JsonNode.Parse(await firstSave.Content.ReadAsStringAsync())!.AsObject();
        firstSaveBody["documentSpeId"]!.GetValue<string>().Should().Be(mintedDocxItemId);
        speItems[mintedDocxItemId].Should().NotBeEmpty();
        ReadBodyText(speItems[mintedDocxItemId]).Should().Contain(FirstSaveEdit,
            "the first save must land the user's edit — everything below measures against this document");

        // ══ THE REFRESH ══════════════════════════════════════════════════════════════════════════
        // Every client-held coordinate is gone: the retained bytes, the re-targeted documentRef, the
        // transient key. Nothing from Act 1/2 is reused below — the client re-mounts against the only
        // durable pointer the host still has, the ORIGINAL PDF.
        // ═════════════════════════════════════════════════════════════════════════════════════════

        // ── Act 3: re-open, post-refresh ──────────────────────────────────────────────────────────
        var secondLoad = await LoadAsync(client, pdfItemId, pdfRecordId);

        secondLoad["documentSpeId"]!.GetValue<string>().Should().Be(mintedDocxItemId,
            "after a refresh the PDF has ALREADY become a Word document — re-opening it must resume on " +
            "that document, not project the PDF a second time. Without this the user's saved work is " +
            "invisible to them and their next save creates a duplicate.");
        secondLoad["versionId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace(
            "the resumed .docx has real version coordinates (unlike the .pdf item, whose version id is " +
            "deliberately suppressed) — this is what lets save two resolve a baseline and CLONE");

        var secondModel = secondLoad["contentModel"]!.AsObject();
        BlockTexts(secondModel).Should().Contain(t => t.Contains(FirstSaveEdit, StringComparison.Ordinal),
            "the re-opened document is the one the first save wrote, so the first save's edit is in the model");

        AppendToLastRun(secondModel, SecondSaveEdit);

        // ── Act 4: second save ────────────────────────────────────────────────────────────────────
        var secondSave = await SaveAsync(
            client,
            documentSpeId: secondLoad["documentSpeId"]!.GetValue<string>(),
            driveId: secondLoad["driveId"]!.GetValue<string>(),
            sessionId: secondLoad["sessionId"]!.GetValue<string>(),
            model: secondModel,
            content: Convert.FromBase64String(secondLoad["content"]!.GetValue<string>()),
            baselineVersionId: secondLoad["versionId"]!.GetValue<string>());

        secondSave.StatusCode.Should().Be(HttpStatusCode.OK);

        // ── The measurement ───────────────────────────────────────────────────────────────────────
        mintedItemIds.Should().ContainSingle(
            "the refresh must NOT mint a second document. A duplicate here is the visible failure: the " +
            "user ends up with two Word documents from one PDF and no indication which is theirs.");

        var finalText = ReadBodyText(speItems[mintedDocxItemId]);
        finalText.Should().Contain(FirstSaveEdit,
            "the second save resolved the created .docx as its baseline, so the first save's work SURVIVED. " +
            "This is the FR-A09 assertion: without tracked coordinates the second save re-projects the PDF " +
            "and silently discards everything the first save wrote.");
        finalText.Should().Contain(SecondSaveEdit, "the second save's own edit landed");

        promotions.Should().ContainSingle(
            "one PDF becomes ONE Word document — the second save replaces in place and its promotion is " +
            "the idempotent existing-row no-op, never a second row");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // FR-A08's unmet half: a PDF-sourced document must be STAMPED Authored, or the suppression that
    // shipped with FR-A08 never fires for the class the requirement names first.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PdfSourcedCreateOnSave_StampsTheRecordAuthored_SoTheFrA08SuppressionCanReachIt()
    {
        _fixture.ResetBoundaries();

        var pdfItemId = $"spe-item-pdf-origin-{Guid.NewGuid():N}";
        var pdfRecordId = Guid.NewGuid();
        var mintedDocxItemId = $"spe-item-docx-origin-{Guid.NewGuid():N}";

        ArrangePdfSource(pdfItemId);

        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MintedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHandle(mintedDocxItemId, "MSA.docx", MintedDriveId, 1, "\"docx-v1\""));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync("sprk_document", pdfRecordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", pdfRecordId));
        Entity? promoted = null;
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => promoted = e)
            .ReturnsAsync((Guid.NewGuid(), true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        var load = await LoadAsync(client, pdfItemId, pdfRecordId);
        var model = load["contentModel"]!.AsObject();

        var response = await CreateOnSaveAsync(
            client,
            sessionId: load["sessionId"]!.GetValue<string>(),
            model: model,
            content: Convert.FromBase64String(load["content"]!.GetValue<string>()),
            transientKey: $"pdf-origin-{Guid.NewGuid():N}",
            sourceDocumentRecordId: pdfRecordId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        promoted.Should().NotBeNull();

        var stamped = promoted!.Contains("sprk_composeorigin")
            ? promoted.GetAttributeValue<OptionSetValue>("sprk_composeorigin")?.Value
            : null;

        stamped.Should().Be((int)ComposeOrigin.Authored,
            "a PDF-sourced document is OUR file — the content model IS the document, there is no prior " +
            ".docx it could be a lossy view of. FR-A08 suppresses degradation warnings by reading this " +
            "durable marker, so a PDF-sourced row stamped Imported makes that suppression unreachable for " +
            "the very class the requirement names first. The routing discriminant (which sees the " +
            "synthesized carrier bytes and correctly says Imported for CLEAN-APPLY purposes) must not be " +
            "the value that gets persisted here.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The mapping is a recovery aid, not a redirect the user is stuck behind.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task PdfSourced_WhenTheDerivedDocumentWasDeleted_ProjectsThePdfAfreshInsteadOfFailing()
    {
        _fixture.ResetBoundaries();

        var pdfItemId = $"spe-item-pdf-stale-{Guid.NewGuid():N}";
        var pdfRecordId = Guid.NewGuid();
        var mintedDocxItemId = $"spe-item-docx-stale-{Guid.NewGuid():N}";

        ArrangePdfSource(pdfItemId);

        // The mint succeeds and records the mapping…
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MintedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHandle(mintedDocxItemId, "MSA.docx", MintedDriveId, 1, "\"docx-v1\""));
        // …and then the user DELETES the Word document. Metadata for it returns null from here on,
        // which is how a deleted drive-item presents. They are entitled to re-open the PDF and start
        // over; a dangling mapping must not fail their load with a 404 on an item they never asked for.
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileHandleDto?)null);
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        _fixture.DataverseMock
            .Setup(d => d.RetrieveAsync("sprk_document", pdfRecordId, It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", pdfRecordId));
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid.NewGuid(), true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        var load = await LoadAsync(client, pdfItemId, pdfRecordId);
        var save = await CreateOnSaveAsync(
            client,
            sessionId: load["sessionId"]!.GetValue<string>(),
            model: load["contentModel"]!.AsObject(),
            content: Convert.FromBase64String(load["content"]!.GetValue<string>()),
            transientKey: $"pdf-stale-{Guid.NewGuid():N}",
            sourceDocumentRecordId: pdfRecordId);
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        // Re-open the PDF. The mapping points at a document that no longer exists.
        var reopened = await LoadAsync(client, pdfItemId, pdfRecordId);

        reopened["documentSpeId"]!.GetValue<string>().Should().Be(pdfItemId,
            "with the derived document gone, re-opening the PDF must project it afresh — the recovery " +
            "aid falls back to the pre-044 behavior rather than becoming a new way to fail");
        reopened["sourceFormat"]!.GetValue<string>().Should().Be("pdf",
            "this really is a PDF projection again, and the honest-lossiness data must say so");

        // ANTI-VACUITY. Everything above is also true of a save that never recorded a mapping at all —
        // which is the pre-044 behavior and would prove nothing. This verifies the mapping WAS written
        // and WAS consulted: the existence probe on the derived item is the only reason this call is made.
        _fixture.SpeMock.Verify(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, mintedDocxItemId, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce,
            "the re-open must have resolved the recorded mapping and probed the derived document — " +
            "without that probe this test is indistinguishable from one where nothing was ever mapped");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // The marker's one unsafe direction: a stale "PDF-sourced" fact reaching a real .docx.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SessionThatServedAPdfThenServesADocx_DoesNotStampTheDocxAuthored()
    {
        _fixture.ResetBoundaries();

        var pdfItemId = $"spe-item-pdf-carryover-{Guid.NewGuid():N}";
        var docxItemId = $"spe-item-docx-carryover-{Guid.NewGuid():N}";
        var docxRecordId = Guid.NewGuid();

        ArrangePdfSource(pdfItemId);

        // A REAL .docx served from the same drive — an imported document with an original to lose against.
        var docxBytes = BuildMinimalDocx();
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, docxItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHandle(docxItemId, "Executed Agreement.docx", PdfDriveId, docxBytes.Length, "\"docx-v1\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, docxItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(docxBytes, writable: false));
        _fixture.SpeMock
            .Setup(s => s.ResolveDriveIdAsync(ContainerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MintedDriveId);
        _fixture.SpeMock
            .Setup(s => s.UploadSmallAsUserAsync(
                It.IsAny<HttpContext>(), MintedDriveId, It.IsAny<string>(),
                It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHandle("spe-item-docx-fork", "Executed Agreement.docx", MintedDriveId, 1, "\"v1\""));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Entity)null!);
        Entity? promoted = null;
        _fixture.DataverseMock
            .Setup(d => d.UpsertAsync(It.IsAny<Entity>(), It.IsAny<CancellationToken>()))
            .Callback<Entity, CancellationToken>((e, _) => promoted = e)
            .ReturnsAsync((docxRecordId, true));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // The session serves a PDF first — the marker is written against it.
        var pdfLoad = await LoadAsync(client, pdfItemId, Guid.NewGuid());
        var sessionId = pdfLoad["sessionId"]!.GetValue<string>();
        pdfLoad["sourceFormat"]!.GetValue<string>().Should().Be("pdf");

        // The SAME session id is then handed to a load of a real .docx.
        var docxLoad = await client.GetAsync(
            $"/api/compose/documents/{docxItemId}?driveId={PdfDriveId}&tenantId={ComposeFidelitySeamFixture.TestTenantId}" +
            $"&sessionId={sessionId}");
        docxLoad.StatusCode.Should().Be(HttpStatusCode.OK);
        var docx = JsonNode.Parse(await docxLoad.Content.ReadAsStringAsync())!.AsObject();

        docx["sessionId"]!.GetValue<string>().Should().NotBe(sessionId,
            "MECHANISM CHECK, recorded rather than assumed: the session is bound to a document, so a load " +
            "of a DIFFERENT document does not resume it — it mints a new one. That binding, not the marker " +
            "clear, is the primary reason a stale PDF fact cannot reach a .docx save. Stating it here keeps " +
            "the assertion below honest about what it does and does not prove.");

        // Save that .docx as a new document, on whatever session the docx load resolved.
        var save = await CreateOnSaveAsync(
            client,
            sessionId: docx["sessionId"]!.GetValue<string>(),
            model: docx["contentModel"]!.AsObject(),
            content: Convert.FromBase64String(docx["content"]!.GetValue<string>()),
            transientKey: $"docx-carryover-{Guid.NewGuid():N}",
            sourceDocumentRecordId: Guid.NewGuid());
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        promoted.Should().NotBeNull();
        var stamped = promoted!.Contains("sprk_composeorigin")
            ? promoted.GetAttributeValue<OptionSetValue>("sprk_composeorigin")?.Value
            : null;

        stamped.Should().Be((int)ComposeOrigin.Imported,
            "a real .docx has an original to preserve against and must stay Imported. This is the marker's " +
            "one unsafe direction: stamping it Authored would put its later saves on the clean-apply branch " +
            "and drop redlines — the SEV-1 shape. Missing the marker costs a false warning; a stale one " +
            "costs redlines, and those are not the same stakes.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    private void ArrangePdfSource(string pdfItemId)
    {
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, pdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildHandle(pdfItemId, PdfFileName, PdfDriveId, PdfBytes.Length, "\"pdf-v1\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(
                It.IsAny<HttpContext>(), PdfDriveId, pdfItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(PdfBytes, writable: false));
        _fixture.PdfIntakeSourceMock
            .Setup(p => p.ParseWithDiagnosticsAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PdfIntakeParseResult.Success(MsaLayout()));
    }

    /// <summary>A minimal but REAL .docx — the projection must succeed on it, so a canned byte array
    /// would not do: an unreadable package fails closed and the save would never reach the stamp.</summary>
    private static byte[] BuildMinimalDocx()
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, DocumentFormat.OpenXml.WordprocessingDocumentType.Document, autoSave: true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(
                new Body(
                    new Paragraph(new Run(new Text("This agreement was executed by the parties.")))));
            main.Document.Save();
        }
        return ms.ToArray();
    }

    private static FileHandleDto BuildHandle(string id, string name, string driveId, long size, string eTag) =>
        new(Id: id, Name: name, ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);

    private static FileHandleDto RecordReplace(
        Dictionary<string, byte[]> items,
        Dictionary<string, List<byte[]>> versions,
        string itemId,
        Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        items[itemId] = ms.ToArray();
        if (!versions.TryGetValue(itemId, out var list))
        {
            list = new List<byte[]>();
            versions[itemId] = list;
        }
        list.Add(ms.ToArray());
        return BuildHandle(itemId, "Master Services Agreement (executed).docx", MintedDriveId,
            items[itemId].Length, $"\"docx-v{list.Count}\"");
    }

    private static async Task<JsonObject> LoadAsync(HttpClient client, string speId, Guid recordId)
    {
        var response = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={PdfDriveId}&tenantId={ComposeFidelitySeamFixture.TestTenantId}" +
            $"&documentRecordId={recordId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, "the PDF must open");
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!.AsObject();
    }

    private static Task<HttpResponseMessage> CreateOnSaveAsync(
        HttpClient client, string sessionId, JsonObject model, byte[] content,
        string transientKey, Guid sourceDocumentRecordId) =>
        client.PostAsJsonAsync("/api/compose/documents/create-on-save", new
        {
            containerId = ContainerId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            sessionId,
            displayName = "Master Services Agreement (executed).docx",
            contentModel = JsonSerializer.Deserialize<JsonElement>(model.ToJsonString()),
            content,
            transientKey,
            sourceDocumentRecordId,
        });

    private static Task<HttpResponseMessage> SaveAsync(
        HttpClient client, string documentSpeId, string driveId, string sessionId,
        JsonObject model, byte[] content, string baselineVersionId) =>
        client.PostAsJsonAsync($"/api/compose/documents/{documentSpeId}/save", new
        {
            sessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId,
            contentModel = JsonSerializer.Deserialize<JsonElement>(model.ToJsonString()),
            content,
            baselineVersionId,
        });

    private static IEnumerable<string> BlockTexts(JsonObject model) =>
        model["blocks"]!.AsArray()
            .Select(b => b!.AsObject())
            .Where(b => b["runs"] is JsonArray)
            .Select(b => string.Concat(b["runs"]!.AsArray()
                .Select(r => r!.AsObject()["text"]?.GetValue<string>() ?? string.Empty)));

    private static void AppendToFirstRun(JsonObject model, string suffix) =>
        AppendToRun(model, suffix, last: false);

    private static void AppendToLastRun(JsonObject model, string suffix) =>
        AppendToRun(model, suffix, last: true);

    private static void AppendToRun(JsonObject model, string suffix, bool last)
    {
        var candidates = model["blocks"]!.AsArray()
            .Select(b => b!.AsObject())
            .Where(b => b["runs"] is JsonArray { Count: > 0 })
            .Select(b => b["runs"]![0]!.AsObject())
            .Where(r => !string.IsNullOrWhiteSpace(r["text"]?.GetValue<string>()))
            .ToList();

        candidates.Should().NotBeEmpty("the model must carry editable text for the edit to land on");
        var run = last ? candidates[^1] : candidates[0];
        run["text"] = run["text"]!.GetValue<string>() + " " + suffix;
    }

    private static string ReadBodyText(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return string.Concat(doc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text));
    }
}
