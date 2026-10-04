// Task 011 (spaarkeai-compose-fidelity-r4.5, FR-03 / WS-1 "one reader everywhere", ADR Tension
// T-2 path-A resolution) — the Browse->project vertical-slice seam. Before this task, a
// Browse-local `.docx` had NO server round-trip at all (ADR-040): the editor mounted it via the
// client `mammoth` convert, the ONE remaining doorway that did not render through the server's
// single-walk `ComposeDocxProjectionBuilder` (F-2 "one reader"). This task adds a NEW, stateless,
// read-only `POST /api/compose/project` endpoint — bytes straight off the wire in, a projection
// out, NOTHING persisted — so Browse renders through the SAME reader as Load/Upload without
// violating the "client authors no bytes" invariant (ADR-040 / R4 I-2): the server only READS
// bytes the client already holds locally and hands back a render; it stores nothing.
//
// What this file proves THROUGH THE WIRE (ADR-038 "unit-green != done" — the vertical-slice-seam
// KEEP category exists precisely because a unit test of `ComposeService.ProjectDocument` alone
// would not catch a broken DI wire between `ComposeEndpoints.Project` and the service, nor prove
// the endpoint touches NO persistence boundary):
//   1. A real `.docx` POSTed directly to `POST /api/compose/project` (no retained-cache lookup,
//      unlike Upload) returns a projection whose Html reflects the real content, Status/CanEdit
//      fail-open, and — the F-2 "same reader" proof — is BYTE-FOR-BYTE identical to what the REAL
//      `GET /api/compose/documents/{id}` (Load) route produces for the SAME source bytes.
//   2. STATELESSNESS (FR-03's core constraint): a `project` call makes ZERO calls into the SPE
//      (`ISpeFileOperations`), Dataverse (`IGenericEntityService`), or indexing
//      (`IPostUploadIndexingEnqueuer`) module boundaries — no download, no upload, no document
//      row, no indexing enqueue. The response ALSO carries no `sessionId`/`documentId` (unlike
//      Upload's `ComposeUploadResponse`) — there is no server-side identity to persist against
//      because nothing is retained. A second call for the SAME bytes returns a byte-identical
//      projection (idempotent — no hidden memoization/session state affecting the render).
//   3. Unreadable/garbage bytes fail CLOSED through the real wire (Status="failed", CanEdit=false,
//      HTTP 200 — never an unhandled 500), mirroring the Load/Upload paths' own fail-closed
//      contract (`ComposePhase1IngestSeamTests`, `ComposeUploadProjectionSeamTests`).
//
// REUSES (root CLAUDE.md §11 — extend, don't duplicate): `ComposeFidelitySeamFixture` (real
// ComposeService/ComposeEndpoints wiring; SPE/Dataverse/indexing module-boundary mocks only) — the
// SAME fixture `ComposeUploadProjectionSeamTests`/`ComposePhase1IngestSeamTests` use.
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
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeProjectSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposeProjectSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Project_RealDocxBytes_ReturnsProjectionMatchingLoadPathShape_AndTouchesNoPersistenceBoundary()
    {
        _fixture.ResetBoundaries();

        var original = BuildDocx(
            new Paragraph(new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                new Run(new Text("Mutual Non-Disclosure Agreement")))
            { ParagraphId = new HexBinaryValue("BBBB0001") },
            new Paragraph(new Run(new Text("This is the body of a Browse-local document.")))
            { ParagraphId = new HexBinaryValue("BBBB0002") });

        using var client = _fixture.CreateAuthenticatedClient();

        // ── (1) the real, stateless project door — bytes straight off the wire, no cache lookup ──
        var projectResponse = await client.PostAsJsonAsync("/api/compose/project",
            new { content = original, fileName = "browsed.docx" });
        projectResponse.StatusCode.Should().Be(HttpStatusCode.OK, "a well-formed .docx must project without error");
        var project = await projectResponse.Content.ReadFromJsonAsync<ComposeProjectResponse>();
        project.Should().NotBeNull();

        project!.Projection.Should().NotBeNull("FR-03: the project response must carry a projection");
        project.Projection.Status.Should().NotBe("failed", "a well-formed .docx must project successfully");
        project.Projection.CanEdit.Should().BeTrue();
        project.Projection.Html.Should().Contain("Mutual Non-Disclosure Agreement")
            .And.Contain("This is the body of a Browse-local document.");
        project.Projection.Html.Should().Contain("data-paraid", "the projection HTML is paraId-tagged (same builder as Load/Upload)");
        project.Projection.SchemaVersion.Should().Be("compose-html-v1");
        project.CorrelationId.Should().NotBeNullOrEmpty();

        // ── (2) STATELESSNESS — zero calls into every persistence/authoring module boundary. A
        //    project() call must not download/upload via SPE, must not touch a sprk_document row,
        //    and must not enqueue indexing — there is nothing to persist because nothing IS
        //    persisted (FR-03's core constraint; the T-2 path-A resolution rests on this). ────────
        _fixture.SpeMock.Invocations.Should().BeEmpty("project() is stateless — it must never call the SPE facade (no download, no upload, no replace)");
        _fixture.DataverseMock.Invocations.Should().BeEmpty("project() is stateless — it must never touch a sprk_document row or any other Dataverse entity");
        _fixture.IndexingMock.Invocations.Should().BeEmpty("project() is stateless — it must never enqueue post-upload indexing (nothing was uploaded)");

        // ── (3) a SECOND call for the SAME bytes is byte-identical — idempotent, no hidden
        //    memoization/session state affecting the render (negative acceptance criterion). ─────
        var secondResponse = await client.PostAsJsonAsync("/api/compose/project",
            new { content = original, fileName = "browsed.docx" });
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var second = await secondResponse.Content.ReadFromJsonAsync<ComposeProjectResponse>();
        second!.Projection.Html.Should().Be(project.Projection.Html, "a repeated project() call for the same bytes must be idempotent — zero cumulative server-side state");
        second.Projection.Status.Should().Be(project.Projection.Status);
        second.Projection.CanEdit.Should().Be(project.Projection.CanEdit);
        _fixture.SpeMock.Invocations.Should().BeEmpty("the SECOND call is equally stateless");
        _fixture.DataverseMock.Invocations.Should().BeEmpty("the SECOND call is equally stateless");
        _fixture.IndexingMock.Invocations.Should().BeEmpty("the SECOND call is equally stateless");

        // ── (4) the SAME bytes through the REAL Load door — proving ONE reader, not a forked shape
        //    (F-2). project() has no server-side identity to carry (no sessionId/documentId — unlike
        //    Upload), consistent with it never minting a ChatSession or drive-item pointer. ────────
        var driveId = $"drive-project-{Guid.NewGuid():N}";
        var speId = $"spe-item-project-{Guid.NewGuid():N}";
        SetupLoadBoundary(driveId, speId, "browsed.docx", original);

        var loadResponse = await client.GetAsync(
            $"/api/compose/documents/{speId}?driveId={Uri.EscapeDataString(driveId)}&tenantId={Uri.EscapeDataString(ComposeFidelitySeamFixture.TestTenantId)}");
        loadResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var load = await loadResponse.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        load.Should().NotBeNull();

        // Load's own MintAndPersist may stamp a fresh w14:paraId if the source lacks one — this fixture's
        // paragraphs already carry explicit ParagraphIds, so Load's projection is built from byte-identical
        // source content to Project's, and the SAME builder must therefore emit byte-identical HTML.
        load!.Projection.Html.Should().Be(project.Projection.Html,
            "the SAME ComposeDocxProjectionBuilder run on the SAME source bytes via two different doorways (Browse->project, Load) must emit byte-identical projection HTML — proving one reader, not a forked shape (F-2)");
        load.Projection.Status.Should().Be(project.Projection.Status);
        load.Projection.CanEdit.Should().Be(project.Projection.CanEdit);
        load.Projection.SchemaVersion.Should().Be(project.Projection.SchemaVersion);
    }

    [Fact]
    public async Task Project_UnreadableBytes_FailsClosedThroughTheWire_NeverAnUnhandledException()
    {
        _fixture.ResetBoundaries();

        var garbage = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03 }; // ZIP magic, not a real docx

        using var client = _fixture.CreateAuthenticatedClient();

        Func<Task<HttpResponseMessage>> act = () => client.PostAsJsonAsync("/api/compose/project",
            new { content = garbage, fileName = "garbage.docx" });
        var response = await act.Should().NotThrowAsync("unreadable bytes must fail closed, never throw an unhandled exception");
        response.Subject.StatusCode.Should().Be(HttpStatusCode.OK, "the request itself still succeeds (HTTP 200) — only the projection fails closed");

        var body = await response.Subject.Content.ReadFromJsonAsync<ComposeProjectResponse>();
        body.Should().NotBeNull();
        body!.Projection.Status.Should().Be("failed");
        body.Projection.CanEdit.Should().BeFalse("fail-closed: the client must never mount a blank editable doc over unreadable content");

        // A failed projection still touches NO persistence boundary — fail-closed does not mean
        // "fall back to persisting the garbage bytes somewhere".
        _fixture.SpeMock.Invocations.Should().BeEmpty();
        _fixture.DataverseMock.Invocations.Should().BeEmpty();
        _fixture.IndexingMock.Invocations.Should().BeEmpty();
    }

    [Fact]
    public async Task Project_EmptyContent_ReturnsBadRequest_NotAServerError()
    {
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/compose/project", new { content = Array.Empty<byte>() });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, "empty content is a client error, not a projection failure or a 500");
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

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // #696 (DEF-02) — the size ceiling on this door.
    //
    // This handler runs SYNCHRONOUS OOXML projection on bytes taken straight off the wire, and had
    // only Kestrel's implicit ~28.6 MB cap. It now carries the same two-level bound the save routes
    // do, from the same constants: a transport cap at MaxRequestBodyBytes (the LARGER number, so a
    // legal document is never pre-empted by a bodiless 413) and an honest per-document refusal at
    // MaxDocumentBytes.
    //
    // Note what #696 asked for that is NOT here. The issue names `/api/compose/upload` alongside
    // this route as running "projection on byte[] Content". It does not: ComposeUploadRequest carries
    // a sessionId and a documentId — two strings — and the bytes come from ITenantCache, where the
    // chat-upload pipeline already bounded them at ingress. There is no unbounded request body on
    // that route to cap, so capping it would have been ceremony against a threat that is not there.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Project_DocumentOverTheLimit_RefusedWithTheStatedLimit_BeforeAnyProjection()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateAuthenticatedClient();

        // One byte over — the boundary IS the contract, so test at the boundary (same stance as the
        // save-path ceiling test in ConcurrencySaveSeamTests).
        var oversize = new byte[ComposeSaveLimits.MaxDocumentBytes + 1];
        oversize[0] = 0x50; oversize[1] = 0x4B; oversize[2] = 0x03; oversize[3] = 0x04;

        var response = await client.PostAsJsonAsync("/api/compose/project",
            new { content = oversize, fileName = "huge.docx" });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"an oversize document is refused as invalid, with a body that explains it. Body: {body}");
        response.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge,
            "a raw 413 carries no body and tells the user nothing about what to do — the same reason " +
            "FR-S08 replaced the transport rejection on the save routes");

        // Reads the very constant the endpoint enforces, so advertising one number while enforcing
        // another fails here rather than in front of a user.
        body.Should().Contain(ComposeSaveLimits.MaxDocumentDisplay,
            "the refusal must name the real limit, not a stale hard-coded number");
        body.Should().Contain("Document Too Large");
    }

    [Fact]
    public async Task Project_DocumentUnderTheLimit_StillProjects()
    {
        // The paired positive. A ceiling test alone cannot tell a working limit from a door that
        // refuses everything, and this endpoint's whole job is to render what it is given.
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateAuthenticatedClient();

        var docx = BuildDocx(new Paragraph(new Run(new Text("Under the limit."))));
        docx.Length.Should().BeLessThan((int)ComposeSaveLimits.MaxDocumentBytes);

        var response = await client.PostAsJsonAsync("/api/compose/project",
            new { content = docx, fileName = "small.docx" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("Document Too Large");
    }

    /// <summary>Minimal in-memory .docx builder — mirrors ComposeUploadProjectionSeamTests.BuildDocx
    /// (same shape, separate copy per this project's existing per-file-fixture convention).</summary>
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
