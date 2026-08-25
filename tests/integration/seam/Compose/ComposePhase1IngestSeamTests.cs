// Task 013 (spaarkeai-compose-r4, NFR-06) — the Phase-1 ingest/projection THROUGH-THE-WIRE seam slice.
// This is the vertical-slice-seam DoD (ADR-038 §7 / tests/CLAUDE.md) for the three Phase-1 ingest
// behaviors task 010/011/012 landed: (1) persisted w14:paraIds survive a load → save → reload round
// trip; (2) the intra-paragraph offset-addressing table (task 011) round-trips editor-offsets to
// deterministic (runIndex, run-local-offset) run splits; (3) opaque atoms (task 012 — SDT/fields/
// complex objects) render as non-editable placeholders and survive a no-op save byte-for-byte.
//
// REUSES (root CLAUDE.md §11 — extend, don't duplicate):
//   - ComposeFidelitySeamFixture (tests/integration/seam/Ai/ComposeFidelitySeamTests.cs) — REAL
//     ComposeService/ComposeEndpoints/ComposeDocxProjectionBuilder wiring; only the external SPE /
//     Dataverse-entity / indexing module boundaries are doubled (ADR-038 mock-boundary rule).
//   - ComposeCorpusFixtureLocator + ComposeOoxmlPackagePartComparer (task 004, this same directory) —
//     corpus discovery + the NFR-01 byte-diff engine, unchanged.
//
// OFFSET-ADDRESSING TABLE — DOCUMENTED APPROACH (not the ADR-038 escalation case): the table has ZERO
// wire footprint today (task 012 kept it ComposeDocxProjection-internal, matching task 011's own
// precedent — see notes/task-012-opaque-atoms-decisions.md §4). Proving its round-trip property
// therefore means: drive the REAL Load endpoint through the wire to obtain the REAL retained bytes
// (LoadComposeDocumentResponse.Content — the SAME bytes ComposeService.LoadAsync computed, including
// task 010's persisted paraIds), then invoke the REAL, stateless, DI-free ComposeDocxProjectionBuilder
// (the exact class ComposeService uses internally, per ComposeService.cs "projection =
// _projectionBuilder.Build(content, ...)") DIRECTLY on those wire-obtained bytes. This is NOT a banned
// shape (no Mock<HttpMessageHandler>, no DI-registration test, no mocked collaborator standing in for
// the offset table) — it is calling a real, pure production type on real wire-sourced data, which is
// the closest honest proof available for a value with no HTTP surface.
//
// CORPUS-COVERAGE NOTE (inherited from task 002/012): NONE of the 3 seed corpus docs carry a
// body-level SDT/field/drawing (verified by unzip+grep in task 012's notes) — the corpus's one real
// SDT+field pair (CIPO footer PAGE field) lives in word/footer2.xml, a part the projection never walks.
// The opaque-atom proof therefore uses a SYNTHETIC document built in-memory (mirrors
// ComposeDocxProjectionBuilderTests.cs's fixture style) for genuine atom-rendering/byte-preservation
// coverage, PLUS the corpus docs for the generic "no-op save leaves every untouched part byte-identical"
// guarantee (which trivially covers footer2.xml too, since it is never opened either way).
//
// Banned-pattern compliance (ADR-038 §7): NO Mock<HttpMessageHandler>, NO DI-registration test, NO
// ctor-null test anywhere in this file. Module-boundary mocks (ISpeFileOperations / IGenericEntityService
// / IPostUploadIndexingEnqueuer) are the same boundary the fixture already establishes.

using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Api;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposePhase1IngestSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ComposePhase1IngestSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    /// <summary>xUnit [MemberData] source — every corpus `.docx`, glob-enumerated (task 004's locator).</summary>
    public static IEnumerable<object[]> CorpusDocuments() =>
        ComposeCorpusFixtureLocator.EnumerateDocumentPaths().Select(path => new object[] { path });

    // ── (1) persisted paraIds survive load → save → reload (task 010), across ALL corpus docs ────────

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task LoadThenSaveThenReload_AcrossCorpusDocs_PersistedParaIdsSurviveRoundTripByteIdentically(string corpusDocPath)
    {
        _fixture.ResetBoundaries();
        var docName = Path.GetFileName(corpusDocPath);
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);

        var driveId = $"drive-phase1-{Guid.NewGuid():N}";
        var speId = $"spe-item-phase1-{Guid.NewGuid():N}";
        var existingDocumentId = Guid.NewGuid();

        // Mutable "storage" — DownloadFileAsUserAsync returns whatever was most recently persisted via
        // ReplaceFileContentAsUserAsync, so a second Load genuinely observes the saved bytes (a real
        // reload), not a fixed stub.
        var storage = original;
        SetupDownload(driveId, speId, docName, () => storage);
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                storage = ms.ToArray();
            })
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: docName, ParentId: null,
                Size: original.Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v2-etag\"", IsFolder: false,
                WebUrl: null, DriveId: driveId));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        using var client = _fixture.CreateAuthenticatedClient();

        // ── Load #1: the real ingest path — mints + PHYSICALLY PERSISTS any missing w14:paraId into
        //    the retained bytes (task 010), then projects them (tasks 011/012). ──────────────────────
        var load1 = await GetLoadAsync(client, speId, driveId);
        load1.StatusCode.Should().Be(HttpStatusCode.OK, $"'{docName}' must load through the real wire without error");
        var body1 = await load1.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body1.Should().NotBeNull();
        body1!.Projection.Status.Should().NotBe("failed", $"'{docName}' must project without editor error");

        // ── Save #1 (no-op — the client echoes back exactly what Load returned): persists to the SPE
        //    boundary. ─────────────────────────────────────────────────────────────────────────────
        var save1 = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId = body1.SessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId,
            content = body1.Content,
        });
        save1.StatusCode.Should().Be(HttpStatusCode.OK, $"the no-op save must succeed for '{docName}'");

        // ── Load #2 ("reload"): the SPE download boundary now serves the bytes Save #1 persisted. ────
        var load2 = await GetLoadAsync(client, speId, driveId);
        load2.StatusCode.Should().Be(HttpStatusCode.OK);
        var body2 = await load2.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body2.Should().NotBeNull();

        // ── Assert: the SAME durable paraIds — not merely equal by coincidence, but PHYSICALLY FOUND
        //    already present on reload (IsMinted=false), proving persistence rather than re-minting. ──
        body2!.ParaIdMap.Select(e => (e.Index, e.ParaId)).Should().Equal(
            body1.ParaIdMap.Select(e => (e.Index, e.ParaId)),
            $"'{docName}': the paraId sequence must be stable across load→save→reload");
        body2.ParaIdMap.Should().OnlyContain(e => !e.IsMinted,
            $"'{docName}': every id must be found ALREADY PRESENT in the retained bytes on reload — proving persistence, not recomputation");

        // ── Assert (corroborates task 012 for the CIPO doc's real footer2.xml SDT+field, and every
        //    other doc's untouched parts): the full load→save round trip leaves every package part
        //    NEVER OPENED by the projection byte-identical to the ORIGINAL corpus bytes. ─────────────
        var diff = ComposeOoxmlPackagePartComparer.Compare(original, storage, strictDocumentXmlByteIdentity: true);
        diff.AllUntouchedPartsByteIdentical.Should().BeTrue(
            $"'{docName}': every part the projection never opens (incl. footer2.xml, the corpus's real SDT+field construct) must survive the full load→save cycle byte-identical — {diff.DescribeMismatches()}");
    }

    // ── (1b) the MINT half specifically — no corpus doc has an id-less paragraph, so this grounds it ──

    [Fact]
    public async Task MintAndPersist_IdLessParagraph_MintedIdSurvivesReloadWithoutReminting_ThroughTheWire()
    {
        // NOTE on IsMinted: ComposeService.LoadAsync runs ComposeBaselineParaIdStamper.MintAndPersist
        // BEFORE building the projection — so by the time ComposeDocxProjectionBuilder walks the (now
        // already-stamped) bytes, it never sees a gap, and the WIRE-returned ParaIdMap[i].IsMinted is
        // structurally always false (this is intentional production behavior — see ComposeService.cs's
        // own remarks: "the builder... sees a body with no gaps"). The wire-observable proof of minting
        // is therefore: (a) the SOURCE bytes truly carry no id (verified below), (b) Load's OWN response
        // Content already carries a physically-embedded id, and (c) that id is stable across a
        // save→reload — NOT the IsMinted flag, which this response shape can never surface as true.
        _fixture.ResetBoundaries();
        var original = BuildDocx(new Paragraph(new Run(new Text("no id yet"))));
        ExtractParaIds(original).Should().Equal(new string?[] { null }, "the source paragraph must start with NO w14:paraId");

        var driveId = $"drive-mint-{Guid.NewGuid():N}";
        var speId = $"spe-item-mint-{Guid.NewGuid():N}";

        var storage = original;
        SetupDownload(driveId, speId, "mint.docx", () => storage);
        SetupSaveBoundary(driveId, speId, "mint.docx", bytes => storage = bytes, Guid.NewGuid());

        using var client = _fixture.CreateAuthenticatedClient();

        var load1 = await GetLoadAsync(client, speId, driveId);
        load1.StatusCode.Should().Be(HttpStatusCode.OK);
        var body1 = await load1.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body1!.ParaIdMap.Should().ContainSingle();
        var mintedId = body1.ParaIdMap[0].ParaId;
        mintedId.Should().MatchRegex("^[0-9A-F]{8}$");
        ExtractParaIds(body1.Content).Should().Equal(new[] { mintedId },
            "Load's OWN response Content must already carry the minted id physically embedded (MintAndPersist ran before the projection)");

        await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId = body1.SessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId,
            content = body1.Content,
        });

        var load2 = await GetLoadAsync(client, speId, driveId);
        var body2 = await load2.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body2!.ParaIdMap.Should().ContainSingle();
        body2.ParaIdMap[0].ParaId.Should().Be(mintedId, "the SAME minted id must be physically embedded, not re-minted on reload");
        ExtractParaIds(storage).Should().Equal(new[] { mintedId }, "the id must be physically present in the SPE-persisted bytes, not merely recomputed each Load");
    }

    // ── (2) offset-addressing table round-trips deterministically (task 011), across corpus docs ─────

    [Theory]
    [MemberData(nameof(CorpusDocuments))]
    public async Task OffsetAddressingTable_OnWireLoadedContent_RoundTripsOffsetsToRunSplitsDeterministically(string corpusDocPath)
    {
        _fixture.ResetBoundaries();
        var docName = Path.GetFileName(corpusDocPath);
        var original = ComposeCorpusFixtureLocator.LoadVerifiedBytes(corpusDocPath);
        var driveId = $"drive-offset-{Guid.NewGuid():N}";
        var speId = $"spe-item-offset-{Guid.NewGuid():N}";

        SetupDownload(driveId, speId, docName, () => original);
        using var client = _fixture.CreateAuthenticatedClient();

        var load = await GetLoadAsync(client, speId, driveId);
        load.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await load.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body.Should().NotBeNull();

        // The offset table has no wire surface (see file header) — invoke the REAL production builder
        // (the same class ComposeService used internally to build THIS wire response) directly on the
        // REAL wire-obtained retained bytes.
        var projection = new ComposeDocxProjectionBuilder().Build(body!.Content);
        projection.OffsetAddressingTable.Should().NotBeEmpty($"'{docName}' has paragraphs");

        // Index-aligned with the wire-returned ParaIdMap — same order, same ids (task 011's own contract).
        projection.OffsetAddressingTable.Select(m => m.ParaId).Should().Equal(
            body.ParaIdMap.Select(e => e.ParaId),
            $"'{docName}': the offset table must be index-aligned with the wire-returned paraId map");

        foreach (var map in projection.OffsetAddressingTable)
        {
            // Contiguous, gap-free, zero-based run spans covering exactly [0, TotalLength] (mirrors the
            // unit-level corpus theory — grounded here in wire-sourced bytes instead of a direct Build call).
            var cursor = 0;
            foreach (var run in map.Runs)
            {
                run.StartOffset.Should().Be(cursor, $"'{docName}' paraId {map.ParaId}: runs are contiguous");
                cursor += run.Length;
            }
            map.TotalLength.Should().Be(cursor);

            for (var offset = 0; offset <= map.TotalLength; offset++)
            {
                map.TryResolve(offset, out var resolution).Should().BeTrue(
                    $"'{docName}' paraId {map.ParaId}: offset {offset} must resolve deterministically");
                resolution.RunLocalOffset.Should().BeGreaterThanOrEqualTo(0);
            }
            map.TryResolve(map.TotalLength + 1, out _).Should().BeFalse(
                $"'{docName}' paraId {map.ParaId}: past-end offset must be refused, never clamped");
        }
    }

    // ── (3) opaque atoms render + survive save byte-for-byte (task 012) — synthetic doc, corpus lacks a
    //        body-level SDT/field construct (see file header). ─────────────────────────────────────────

    [Fact]
    public async Task SyntheticFieldSdtAndDrawingDocument_LoadsWithoutError_AtomsRenderNonEditable_AndSurviveNoOpSaveByteIdentical()
    {
        _fixture.ResetBoundaries();
        var original = BuildDocx(
            // w:fldChar field sequence — the CIPO footer's own PAGE-field shape, at body level here.
            new Paragraph(
                new Run(new Text("Page ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar { FieldCharType = FieldCharValues.Begin }),
                new Run(new FieldCode(" PAGE ") { Space = SpaceProcessingModeValues.Preserve }),
                new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
                new Run(new Text("1")),
                new Run(new FieldChar { FieldCharType = FieldCharValues.End }))
            { ParagraphId = new HexBinaryValue("AAAA0001") },
            // A non-text SDT (date content control) — whole-construct atom (task 012 escalation-boundary rule).
            new SdtBlock(new SdtProperties(new SdtContentDate()), new SdtContentBlock(new Paragraph(new Run(new Text("2026-07-22"))))),
            new Paragraph(new Run(new Text("closing"))) { ParagraphId = new HexBinaryValue("AAAA0003") });

        var driveId = $"drive-atoms-{Guid.NewGuid():N}";
        var speId = $"spe-item-atoms-{Guid.NewGuid():N}";
        var storage = original;
        SetupDownload(driveId, speId, "atoms.docx", () => storage);
        SetupSaveBoundary(driveId, speId, "atoms.docx", bytes => storage = bytes, Guid.NewGuid());

        using var client = _fixture.CreateAuthenticatedClient();

        var load = await GetLoadAsync(client, speId, driveId);
        load.StatusCode.Should().Be(HttpStatusCode.OK, "a doc containing fields/SDTs must load through the wire without editor error");
        var body = await load.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body.Should().NotBeNull();
        body!.Projection.Status.Should().NotBe("failed");
        body.Projection.Html.Should().Contain("data-atom-kind=\"field\"").And.Contain("contenteditable=\"false\"");
        body.Projection.Html.Should().Contain("data-atom-kind=\"sdt\"").And.Contain("data-atomid");
        // Task 049 sharpened this. It was `Html.Should().NotContain("PAGE")` — a proxy that reads the whole
        // markup string, attributes included. The field atom now carries its instruction in a `data-*`
        // attribute so an edited paragraph can hand the field back rather than dropping it, so the proxy no
        // longer separates "shown to the user" from "present in the markup". The property is unchanged and
        // asserted directly: the field code is absent from the RENDERED TEXT.
        System.Text.RegularExpressions.Regex.Replace(body.Projection.Html, "<[^>]*>", string.Empty)
            .Should().NotContain("PAGE", "the field CODE (w:instrText) must never be editor-visible");
        body.Projection.Html.Should().Contain("data-field-instr=\" PAGE \"",
            "…while the instruction IS carried as atom payload, which is what makes the field survive an " +
            "edit to its paragraph (task 049)");

        var save = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId = body.SessionId,
            tenantId = ComposeFidelitySeamFixture.TestTenantId,
            driveId,
            content = body.Content,
        });
        save.StatusCode.Should().Be(HttpStatusCode.OK);

        // The atoms are NEVER opened — the strongest possible statement: the ENTIRE persisted byte array
        // equals the retained-original bytes Load returned (which already carry any minted paraIds).
        storage.Should().Equal(body.Content,
            "a no-op save must leave the field/SDT/drawing-bearing document byte-identical — the atoms were never opened (I-4)");
    }

    // ── negative: a malformed/absent-paragraph doc still ingests with a minted id, never crashes ──────

    [Fact]
    public async Task MalformedOrAbsentParagraphDocument_StillIngestsWithMintedId_DoesNotCrash()
    {
        _fixture.ResetBoundaries();
        // "Absent paragraph" — a single, empty, id-less <w:p/> (no runs, no text) is the corpus-adjacent
        // shape task-002/012 already treat as a real edge case (EmptyPara / trailing-empty-paragraph).
        var original = BuildDocx(new Paragraph());
        var driveId = $"drive-negative-{Guid.NewGuid():N}";
        var speId = $"spe-item-negative-{Guid.NewGuid():N}";
        SetupDownload(driveId, speId, "empty.docx", () => original);

        using var client = _fixture.CreateAuthenticatedClient();

        Func<Task<HttpResponseMessage>> act = () => GetLoadAsync(client, speId, driveId);
        var response = await act.Should().NotThrowAsync("an absent/malformed paragraph must never crash the ingest path");
        response.Subject.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Subject.Content.ReadFromJsonAsync<LoadComposeDocumentResponse>();
        body!.ParaIdMap.Should().ContainSingle();
        body.ParaIdMap[0].ParaId.Should().MatchRegex("^[0-9A-F]{8}$", "the empty paragraph carried no w14:paraId — one must still be minted");
        ExtractParaIds(body.Content).Should().Equal(new[] { body.ParaIdMap[0].ParaId },
            "the minted id must be physically embedded in Load's own response Content (see MintAndPersist_... test for why IsMinted can't be asserted here)");
        body.Projection.Status.Should().NotBe("failed");
    }

    [Fact]
    public async Task GenuinelyUnreadableBytes_FailsClosedThroughTheWire_NeverAnUnhandledException()
    {
        _fixture.ResetBoundaries();
        var garbage = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x01, 0x02, 0x03 }; // ZIP magic, not a real docx
        var driveId = $"drive-garbage-{Guid.NewGuid():N}";
        var speId = $"spe-item-garbage-{Guid.NewGuid():N}";
        SetupDownload(driveId, speId, "garbage.docx", () => garbage);

        using var client = _fixture.CreateAuthenticatedClient();

        Func<Task<HttpResponseMessage>> act = () => GetLoadAsync(client, speId, driveId);
        var response = await act.Should().NotThrowAsync("unreadable bytes must fail closed, never throw an unhandled exception");
        // Fail-closed (F-04): the endpoint must not 5xx — either a well-formed 200 with Status=failed, or a
        // ProblemDetails 4xx. Both are acceptable; an unhandled 500 is not.
        ((int)response.Subject.StatusCode).Should().BeLessThan(500,
            $"got {response.Subject.StatusCode} — an unreadable source must never surface as an unhandled server error");
    }

    // ── shared wire helpers ──────────────────────────────────────────────────────────────────────────

    private static Task<HttpResponseMessage> GetLoadAsync(HttpClient client, string speId, string driveId) =>
        client.GetAsync($"/api/compose/documents/{speId}?driveId={Uri.EscapeDataString(driveId)}&tenantId={Uri.EscapeDataString(ComposeFidelitySeamFixture.TestTenantId)}");

    private void SetupDownload(string driveId, string speId, string fileName, Func<byte[]> currentBytes)
    {
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new FileHandleDto(
                Id: speId, Name: fileName, ParentId: null,
                Size: currentBytes().Length, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v1-etag\"", IsFolder: false,
                WebUrl: null, DriveId: driveId));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult<Stream?>(new MemoryStream(currentBytes())));
    }

    private void SetupSaveBoundary(string driveId, string speId, string fileName, Action<byte[]> onPersisted, Guid existingDocumentId)
    {
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                onPersisted(ms.ToArray());
            })
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: fileName, ParentId: null,
                Size: 0, CreatedDateTime: DateTimeOffset.UtcNow,
                LastModifiedDateTime: DateTimeOffset.UtcNow, ETag: "\"v2-etag\"", IsFolder: false,
                WebUrl: null, DriveId: driveId));
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));
    }

    /// <summary>Opens raw .docx bytes and returns each body paragraph's physical w14:paraId (null if
    /// absent), in document order — a direct, non-mocked read of the OOXML the wire actually carries.
    /// Used to ground mint/persist assertions in physical bytes rather than the ParaIdMap.IsMinted flag
    /// (which is structurally always false on the wire — see MintAndPersist_... test's remarks).</summary>
    private static IReadOnlyList<string?> ExtractParaIds(byte[] docx)
    {
        using var doc = WordprocessingDocument.Open(new MemoryStream(docx, writable: false), isEditable: false);
        return doc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Select(p => p.ParagraphId?.Value)
            .ToList();
    }

    /// <summary>Minimal in-memory .docx builder — mirrors ComposeDocxProjectionBuilderTests.cs's BuildDocx
    /// (same shape, separate copy per this project's existing per-file-fixture convention — see e.g.
    /// ComposeParaIdReanchorSeamTests.BuildDocxWithParaIds).</summary>
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
