// Task 013 (spaarkeai-compose-r6, spec FR-01/FR-02/NFR-05) — the THROUGH-THE-WIRE proof of Success
// Criterion 1 for the render-on-save pivot (tasks 010/011/012): a save posting {contentModel, content}
// routes through ComposeDocumentRenderer.RenderIntoCarrier — no ComposeShadowPatchEngine, no
// ComposeBaselineParaIdStamper count-gate — so the NDA fixture that produced the production HTTP 422
// on the old surgical path (AppligentNDA_Signed.docx: 7 mc:AlternateContent pairs, 12 w:txbxContent,
// 3 duplicate w14:paraId) now completes a full load → edit → save → reopen round trip:
//
//   (1) LOAD projects the NDA into a non-null canonical contentModel (the projection succeeds on the
//       exact document class the old path could not anchor against).
//   (2) A client-mapper-shaped edit (one appended run with revision.kind=Inserted, author omitted —
//       the server attributes the authenticated user) posted as {contentModel, content} SAVES with
//       200 — explicitly NEVER the 422 the surgical path threw — and lands ONE new immutable SPE
//       version (ReplaceFileContentAsUserAsync exactly once; append-only semantics per task 002).
//   (3) The persisted bytes carry the edit as a REAL Word redline (w:ins InsertedRun), every
//       w14:paraId is unique (the NDA's duplicate-paraId class dedup'd by the render), and the
//       signature-box content survives as accept-flattened prose ("For: Appligent, Inc.").
//   (4) REOPEN: loading the persisted bytes again projects the edit back — the redline survives a
//       full reopen round trip, not just the save.
//
// MAINTAIN-class (regression-protector; /test-diet KEEP — tests/integration/seam/** vertical-slice
// KEEP path per ADR-038). Through-the-wire WebApplicationFactory slice only: NO
// Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test, NO reflection over private
// members. Mocks live ONLY at the ISpeFileOperations / IGenericEntityService /
// IPostUploadIndexingEnqueuer module boundaries (the SAME ComposeFidelitySeamFixture task 024/034
// established — CLAUDE.md §11: no new fixture class).

using System.Net;
using System.Text;
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
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class RenderOnSaveSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private const string NdaFixtureFileName = "AppligentNDA_Signed.docx";
    private const string EditMarker = "[R6-RENDER-ON-SAVE-013]";

    private readonly ComposeFidelitySeamFixture _fixture;

    public RenderOnSaveSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task NdaLoadEditSaveReopen_NewVersionAndEditLands_ThroughTheWire()
    {
        const string speId = "spe-item-013-nda-render-on-save";
        const string driveId = "drive-013-nda-render-on-save";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var ndaBytes = LoadNdaFixtureBytes();

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        // ── Arrange the SPE boundary for LOAD: metadata (eTag v1), the NDA bytes, load-time version. ──
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, ndaBytes.Length, "\"v1\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(ndaBytes.ToArray()));
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("1.0");

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Act 1: GET the load route — the NDA must project into a non-null canonical model. ────────
        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var loadBody = await loadResponse.Content.ReadAsStringAsync();
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the NDA load must succeed through the wire — body: {loadBody}");

        var loadRoot = JsonNode.Parse(loadBody)!.AsObject();
        var loadedModel = loadRoot["contentModel"];
        loadedModel.Should().NotBeNull(
            "the NDA's canonical content-model projection must succeed (FR-01) — the exact document " +
            "class the old surgical path 422'd on must be projectable on the render-on-save path");
        var blocks = loadedModel!["blocks"]!.AsArray();
        blocks.Should().NotBeEmpty("the NDA canonical projection must produce a non-empty block list");

        var sessionId = loadRoot["sessionId"]!.GetValue<string>();
        sessionId.Should().NotBeNullOrWhiteSpace();

        // ── Act 2: simulate the CLIENT MAPPER's edit — append a tracked-inserted run to the FIRST
        //    paragraph-kind block's runs. Author omitted: the server attributes the authenticated
        //    user (ResolveRevisionAuthor). Mutating the load response's own JSON (not a fragile DTO
        //    mirror) is exactly what the post-cutover client does: retain the model, merge editor
        //    state, re-post with every server-set field preserved. ─────────────────────────────────
        var editedModel = loadedModel.DeepClone()!.AsObject();
        var firstParagraph = editedModel["blocks"]!.AsArray()
            .First(b => string.Equals(b!["kind"]?.GetValue<string>(), "paragraph", StringComparison.OrdinalIgnoreCase));
        firstParagraph!["runs"]!.AsArray().Add(new JsonObject
        {
            ["text"] = EditMarker,
            ["revision"] = new JsonObject { ["kind"] = "Inserted" },
        });

        // ── Arrange the SPE boundary for SAVE: capture the persisted bytes; new immutable version. ──
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
            .ReturnsAsync(BuildFileHandle(speId, driveId, ndaBytes.Length, "\"v2\""));

        // ── Act 3: POST the post-cutover save shape — {contentModel, content} only. NO operationLog,
        //    NO paraIdMap, NO comments. `content` is the load response's own minted bytes (the render
        //    carrier); `contentModel` is the edited model. ─────────────────────────────────────────
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
            "the render-on-save path must make the NDA's production 422 (anchor-reconciliation class) " +
            $"unreachable by construction — body: {saveResponseBody}");
        saveResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the NDA render-on-save must succeed through the wire — body: {saveResponseBody}");

        var saveRoot = JsonNode.Parse(saveResponseBody)!.AsObject();
        saveRoot["versionId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace(
            "a successful save reports the new SPE version identity");

        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "exactly ONE SPE content write — a new immutable version by append-only semantics (task 002)");

        // ── Assert on the persisted OOXML: the edit landed as a real Word redline; paraIds unique;
        //    signature-box prose survived. ──────────────────────────────────────────────────────────
        persisted.Should().NotBeNull("the SPE facade must have captured the rendered bytes");
        using (var doc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false))
        {
            var body = doc.MainDocumentPart!.Document!.Body!;

            body.Descendants<InsertedRun>()
                .Any(ins => ins.InnerText.Contains(EditMarker, StringComparison.Ordinal))
                .Should().BeTrue(
                    "the tracked edit must land as a REAL Word redline (w:ins), not plain prose");

            var paraIds = body.Descendants<Paragraph>()
                .Select(p => p.ParagraphId?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .Select(id => id!.ToUpperInvariant())
                .ToList();
            paraIds.Should().OnlyHaveUniqueItems(
                "the NDA's duplicate-w14:paraId class (2BBF07C9/CA/CB) must be dedup'd by the render — " +
                "duplicate anchors were part of the production-422 failure chain");

            body.InnerText.Should().Contain("For: Appligent, Inc.",
                "the signature-box (w:txbxContent) content must survive as accept-flattened prose, " +
                "not silently vanish");
        }

        // ── Act 4 (REOPEN): load the just-persisted bytes — the edit must survive the round trip. ──
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, persisted!.Length, "\"v2\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(persisted!.ToArray()));
        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("2.0");

        var reopenResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={driveId}&tenantId={tenant}");
        var reopenBody = await reopenResponse.Content.ReadAsStringAsync();
        reopenResponse.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the reopen load of the persisted bytes must succeed — body: {reopenBody}");

        var reopenRoot = JsonNode.Parse(reopenBody)!.AsObject();
        var reopenHtml = reopenRoot["projection"]?["html"]?.GetValue<string>() ?? string.Empty;
        var reopenModelJson = reopenRoot["contentModel"]?.ToJsonString() ?? string.Empty;
        (reopenHtml.Contains(EditMarker, StringComparison.Ordinal)
                || reopenModelJson.Contains(EditMarker, StringComparison.Ordinal))
            .Should().BeTrue(
                "the tracked edit must survive a FULL reopen round trip — visible in the reopened " +
                "projection html or canonical content model");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared arrange + helpers (mirrors ConcurrencySaveSeamTests' per-file-local-helper convention).
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

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

    private static byte[] LoadNdaFixtureBytes()
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), NdaFixtureFileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: NdaFixtureFileName, ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);
}
