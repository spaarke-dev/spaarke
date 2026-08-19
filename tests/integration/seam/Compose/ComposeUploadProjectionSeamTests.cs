// Task 010 (spaarkeai-compose-fidelity-r4.5, FR-01 / WS-1 "one reader everywhere") — the
// upload→projection vertical-slice seam. Before this task, `POST /api/compose/upload` (the
// assistant-upload transient-mount door) returned ONLY raw retained bytes; the editor mounted
// them via the client mammoth convert. This task wires the SAME single-walk
// `ComposeDocxProjectionBuilder` the stored-document Load path already uses (F-2 "one reader")
// into the Upload door via a new thin seam on IComposeService (`ProjectDocument`), so the wire
// response now ALSO carries a `ComposeProjectionResponse` — the IDENTICAL shape
// `LoadComposeDocumentResponse.Projection` carries.
//
// What this file proves THROUGH THE WIRE (ADR-038 "unit-green != done" — the vertical-slice-seam
// KEEP category exists precisely because a unit test of `ComposeService.ProjectDocument` alone
// would not catch a broken DI wire between `ComposeEndpoints.Upload` and the service, nor a
// forked/divergent wire shape):
//   1. A real `.docx` retained in `ITenantCache` under the SAME key/resource contract the chat
//      upload pipeline uses (`doc-upload-binary`, `{sessionId}:{documentId}`, version 1 — see
//      `ChatDocumentEndpoints`/`ComposeEndpoints.Upload`) is served through the REAL
//      `POST /api/compose/upload` route with a projection whose Html reflects the real content,
//      Status/CanEdit fail-open, and — the F-2 "same reader" proof — is BYTE-FOR-BYTE identical
//      to what the REAL `GET /api/compose/documents/{id}` (Load) route produces for the SAME
//      source bytes. Two different doorways, one projection engine, one output.
//   2. `Content` (the raw bytes) is UNCHANGED and still returned alongside the projection — FR-01
//      ADDS the projection, it does not replace the baseline bytes a later create-on-save still
//      needs (ComposeWorkspace.tsx `triggerSave` sends `state.docxBytes` as the Save baseline).
//   3. Unreadable/garbage retained bytes fail CLOSED through the real wire (Status="failed",
//      CanEdit=false, HTTP 200 — never an unhandled 500), mirroring the Load path's own
//      fail-closed contract (`ComposePhase1IngestSeamTests.GenuinelyUnreadableBytes_...`).
//
// REUSES (root CLAUDE.md §11 — extend, don't duplicate): `ComposeFidelitySeamFixture` (real
// ComposeService/ComposeEndpoints wiring; SPE/Dataverse/indexing module-boundary mocks only) —
// the SAME fixture `ComposePhase1IngestSeamTests`/`ComposeNoOpRoundTripByteDiffSeamTests` use.
// `ITenantCache` is NOT mocked — the fixture's real (in-memory-fallback, `Redis:Enabled=false`)
// registration is used, seeded directly, matching production's own read/write contract exactly
// rather than re-describing it through a mock.
//
// Banned-pattern compliance (ADR-038 §7 / tests/CLAUDE.md): NO `Mock<HttpMessageHandler>`, NO
// DI-registration test, NO ctor-null test anywhere in this file.

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeUploadProjectionSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeUploadProjectionSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private const string DocBinaryResource = "doc-upload-binary";
    private const int DocCacheVersion = 1;

    [Fact]
    public async Task Upload_RealDocx_ReturnsProjectionMatchingLoadPathShape_AndPreservesContentForSave()
    {
        _fixture.ResetBoundaries();

        var original = BuildDocx(
            new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(new Text("Amended and Restated Agreement")))
            { ParagraphId = new HexBinaryValue("AAAA0001") },
            new Paragraph(new Run(new Text("This is the body of the uploaded document.")))
            { ParagraphId = new HexBinaryValue("AAAA0002") });

        var sessionId = $"session-{Guid.NewGuid():N}";
        var documentId = $"doc-{Guid.NewGuid():N}";
        var tenantId = ComposeFidelitySeamFixture.TestTenantId;

        var cache = _fixture.Services.GetRequiredService<ITenantCache>();
        await cache.SetAsync(tenantId, DocBinaryResource, $"{sessionId}:{documentId}", DocCacheVersion,
            original, TimeSpan.FromHours(4));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── (1) the real upload-mount door ──────────────────────────────────────────────────
        var uploadResponse = await client.PostAsJsonAsync("/api/compose/upload",
            new { sessionId, documentId });
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a retained, well-formed .docx must upload-mount without error");
        var upload = await uploadResponse.Content.ReadFromJsonAsync<ComposeUploadResponse>();
        upload.Should().NotBeNull();

        upload!.Projection.Should().NotBeNull("FR-01: the upload response must carry a projection, not raw bytes alone");
        upload.Projection.Status.Should().NotBe("failed", "a well-formed .docx must project successfully");
        upload.Projection.CanEdit.Should().BeTrue();
        upload.Projection.Html.Should().Contain("Amended and Restated Agreement")
            .And.Contain("This is the body of the uploaded document.");
        upload.Projection.Html.Should().Contain("data-paraid", "the projection HTML is paraId-tagged (same builder as Load)");
        upload.Projection.SchemaVersion.Should().Be("compose-html-v1");

        // (2) Content is UNCHANGED — the baseline bytes a later create-on-save still needs
        // (ComposeWorkspace.tsx triggerSave sends state.docxBytes) are NOT replaced by the projection.
        upload.Content.Should().Equal(original, "FR-01 ADDS the projection; it must not drop the raw retained bytes Save still needs");

        // ── (3) the SAME bytes through the REAL Load door — proving ONE reader, not a forked shape ──
        var driveId = $"drive-upload-proj-{Guid.NewGuid():N}";
        var speId = $"spe-item-upload-proj-{Guid.NewGuid():N}";
        SetupLoadBoundary(driveId, speId, "uploaded.docx", original);

        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={Uri.EscapeDataString(driveId)}&tenantId={Uri.EscapeDataString(tenantId)}");
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var load = await loadResponse.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        load.Should().NotBeNull();

        // Load's own MintAndPersist may stamp a fresh w14:paraId if the source lacks one — this fixture's
        // paragraphs already carry explicit ParagraphIds, so Load's projection is built from byte-identical
        // source content to Upload's, and the SAME builder must therefore emit byte-identical HTML.
        load!.Projection.Html.Should().Be(upload.Projection.Html,
            "the SAME ComposeDocxProjectionBuilder run on the SAME source bytes via two different doorways (Upload, Load) must emit byte-identical projection HTML — proving one reader, not a forked shape (F-2)");
        load.Projection.Status.Should().Be(upload.Projection.Status);
        load.Projection.CanEdit.Should().Be(upload.Projection.CanEdit);
        load.Projection.SchemaVersion.Should().Be(upload.Projection.SchemaVersion);
    }

    [Fact]
    public async Task Upload_UnreadableRetainedBytes_FailsClosedThroughTheWire_NeverAnUnhandledException()
    {
        _fixture.ResetBoundaries();

        var garbage = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03 }; // ZIP magic, not a real docx
        var sessionId = $"session-{Guid.NewGuid():N}";
        var documentId = $"doc-{Guid.NewGuid():N}";
        var tenantId = ComposeFidelitySeamFixture.TestTenantId;

        var cache = _fixture.Services.GetRequiredService<ITenantCache>();
        await cache.SetAsync(tenantId, DocBinaryResource, $"{sessionId}:{documentId}", DocCacheVersion,
            garbage, TimeSpan.FromHours(4));

        using var client = _fixture.CreateAuthenticatedClient();

        Func<Task<HttpResponseMessage>> act = () => client.PostAsJsonAsync("/api/compose/upload", new { sessionId, documentId });
        var response = await act.Should().NotThrowAsync("unreadable retained bytes must fail closed, never throw an unhandled exception");
        response.Subject.StatusCode.Should().Be(HttpStatusCode.OK, "the upload-mount itself still succeeds (the bytes WERE retrieved) — only the projection fails closed");

        var body = await response.Subject.Content.ReadFromJsonAsync<ComposeUploadResponse>();
        body.Should().NotBeNull();
        body!.Projection.Status.Should().Be("failed");
        body.Projection.CanEdit.Should().BeFalse("fail-closed: the client must never mount a blank editable doc over unreadable content");
        body.Content.Should().Equal(garbage, "the retained bytes are still returned even when they cannot be projected");
    }

    private void SetupLoadBoundary(string driveId, string speId, string fileName, byte[] bytes)
    {
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: fileName, ParentId: null,
                Size: bytes.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v1-etag\"", IsFolder: false,
                WebUrl: null, DriveId: driveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Stream?>(new MemoryStream(bytes)));
    }

    /// <summary>Minimal in-memory .docx builder — mirrors ComposePhase1IngestSeamTests.BuildDocx (same
    /// shape, separate copy per this project's existing per-file-fixture convention).</summary>
    private static byte[] BuildDocx(params OpenXmlElement[] bodyChildren)
    {
        using var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document))
        {
            var main = doc.AddMainDocumentPart();
            var body = new Body();
            foreach (var child in bodyChildren) body.Append(child);
            body.Append(new SectionProperties());
            main.Document = new Document(body);
            main.Document.Save();
        }
        return ms.ToArray();
    }
}
