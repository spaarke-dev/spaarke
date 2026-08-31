// Task 054 (spaarkeai-compose-r4, NFR-06/NFR-08, Success Criterion 4) — the THROUGH-THE-WIRE seam
// evidence for the Phase-5 concurrency behaviors tasks 050/051/052 built into `ComposeService.SaveAsync`:
//
//   (1) STALE-BASE save re-anchors WITHOUT an eTag 500 (task 050): a second save of the SAME
//       drive-item, where the live SPE eTag no longer matches the version stamp THIS save path
//       persisted after its own last write, re-anchors the operation log via
//       `AnnotationReanchorService` instead of throwing. AUTO (exact-paraId) re-anchors apply through
//       the Patch Engine; the response carries the `reanchorSummary` (band counts + per-op outcome).
//   (2) NEGATIVE — an un-re-anchorable operation (its paraId is absent from BOTH the retained
//       baseline and the freshly re-downloaded current bytes, so neither the paraId-primary nor the
//       fuzzy fallback can place it) surfaces as ORPHAN in `reanchorSummary` — never silently applied,
//       never silently dropped, and never partially written to the persisted bytes.
//   (3) NEGATIVE — an UNEXPECTED, non-lock, non-precondition failure during the SPE write still
//       surfaces as a generic 500 ProblemDetails (the `catch (Exception ex)` fallback in
//       `ComposeEndpoints.ExecuteSaveAsync`) — proving the DEF-14/051 typed-exception handling for
//       423/412 does NOT accidentally swallow or misclassify an unrelated failure as a lock/precondition.
//
// The OTHER two Phase-5 behaviors this task's prompt names are already proven through the wire
// elsewhere and are cited rather than duplicated (CLAUDE.md §11 "prefer extending over introducing a
// new component" — a passing near-duplicate is scope creep, not coverage):
//   - "create-on-save then a content write does not throw the eTag mismatch" (task 051 positive path)
//     is proven by `ComposeCreateOnSaveEndpointContractTests.UploadThenCreateOnSave_TransientDraft_...`
//     (tests/integration/contract/Api/Ai/ComposeCreateOnSaveEndpointContractTests.cs) — a full upload
//     -> create-on-save round trip through the REAL routes with the REAL ComposeService. The task 051
//     root-cause fix itself (the missing ODataError catch chain on `UploadSmallAsUserAsync`) is proven
//     by `Def14_ComposeSaveLockedDocumentTests` (423 case, both route + translation layers) — THIS file
//     extends that regression file with the missing 412 (EtagPreconditionFailedException) counterpart
//     for the create-on-save route, closing FR-08's "concurrent external edit... yields 412" negative
//     case for that specific route (see the diff to Def14_ComposeSaveLockedDocumentTests.cs).
//   - HTTP 423 (Word lock) surfacing as a ProblemDetails, not a 500 (task 052) is already proven for
//     BOTH the replace route and the create-on-save route by `Def14_ComposeSaveLockedDocumentTests`
//     (route-layer 423 tests) + the ODataError translation tests (translation-layer 423 tests).
//   - "imported revisions/comments are accept/reject-able and survive a save" (task 053) — the SERVER
//     side (survival through a real Load -> dirty-edit-elsewhere -> Save -> Load round trip, byte-level,
//     incl. the separate comments.xml OPC part) is already proven end-to-end by
//     `ComposeImportedAnchorsSurviveSaveSeamTests` (tests/integration/seam/Ai/, task 052/R3). The
//     "accept/reject-able" interaction itself is a CLIENT (TipTap) concept with no BFF endpoint — the
//     BFF only ever sees the resulting operation log on the next Save (exactly what that seam test
//     drives). The negative "unresolvable imported anchor surfaces, not dropped" case was implemented
//     and tested CLIENT-SIDE only (task 053 added no `.cs` change — see its own task notes) in
//     `importRoundTrip.test.tsx` (`renderUnresolvedRevisionPlaceholders`) — there is no BFF-side
//     equivalent to seam-test: `DocxAnnotationReader` reads an imported mark's anchor from the SAME
//     document it is inside, so it cannot itself produce an "unresolvable" paraId at read time; the
//     staleness that can leave a paraId unresolvable arises only in the client's editor-side paraId map.
//
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slices only. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test anywhere in this file. Mocks live only at the
// ISpeFileOperations / IGenericEntityService / IPostUploadIndexingEnqueuer module boundaries (the SAME
// fixture task 024/034 established — CLAUDE.md §11: no new fixture class).

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
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ConcurrencySaveSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public ConcurrencySaveSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private static readonly string[] Paragraphs =
    {
        "This Master Services Agreement governs the engagement between the parties.",
        "Confidential Information shall not be disclosed to any third party.",
        "This clause shall survive termination of the Agreement.",
    };

    private static readonly string[] ParaIds = { "00000001", "00000002", "00000003" };

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. Stale-base save RE-ANCHORS (AUTO band, exact paraId) WITHOUT an eTag 500 — task 050.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_StaleBase_ReanchorsAutoBand_NoETagFiveHundred_ThroughTheWire()
    {
        const string speId = "spe-item-054-stale-auto";
        const string driveId = "drive-054-stale-auto";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // ── Save #1 — no operations. Seeds the version stamp (ADR-009 IDistributedCache) at the eTag
        //    THIS write's own response returns — the assert-baseline for the NEXT save of this item. ──
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });
        var firstBody = await firstSave.Content.ReadAsStringAsync();
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK, $"the seeding save must succeed — body: {firstBody}");

        // ── Simulate an EXTERNAL writer landing a new version between save #1 and save #2: the live
        //    SPE eTag (from GetFileMetadataAsUserAsync, fetched fresh on every existing-item save) now
        //    differs from the "v1-etag" stamp just persisted. The re-downloaded CURRENT bytes still
        //    carry the SAME w14:paraId set (00000001..3) an external Word round-trip preserves for its
        //    OWN edits (design D2) — the exact-paraId AUTO path task 050 added. ──────────────────────
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag-external\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(original.ToArray()));

        // FR-S02 (r8 task 011): the post-stale write goes through the IF-MATCH overload — an existing-item
        // save always reads live metadata, so `preWriteETag` is set and the PUT carries the precondition.
        // The etag-less overload above still serves the seed save (no metadata read, nothing to assert).
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
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v3-etag\""));

        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[R4-STALE-REANCHOR]" },
            },
        };

        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var secondBody = await secondSave.Content.ReadAsStringAsync();
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a stale-base save must RE-ANCHOR via AnnotationReanchorService and complete — NEVER an eTag 500 — body: {secondBody}");

        var saveResult = await secondSave.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult.Should().NotBeNull();
        saveResult!.ReanchorSummary.Should().NotBeNull(
            "a stale-base save must surface the re-anchor summary on the response (task 050 FR-08)");
        saveResult.ReanchorSummary!.Total.Should().Be(1);
        saveResult.ReanchorSummary.AutoCount.Should().Be(1,
            "the op's paraId (00000001) is present in the re-downloaded current bytes — an exact-paraId AUTO match");
        saveResult.ReanchorSummary.ReviewCount.Should().Be(0);
        saveResult.ReanchorSummary.OrphanCount.Should().Be(0);
        saveResult.ReanchorSummary.Annotations.Single().Band.Should().Be(ReanchorBand.Auto);
        saveResult.ReanchorSummary.Annotations.Single().Confidence.Should().Be(1.0);

        persisted.Should().NotBeNull("the SPE facade must have captured the re-anchored, patched bytes");
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var editedPara = patchedDoc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, ParaIds[0], StringComparison.OrdinalIgnoreCase));
        editedPara.Descendants<InsertedRun>().SelectMany(i => i.Descendants<Text>()).Select(t => t.Text)
            .Should().Contain("[R4-STALE-REANCHOR]",
                "the AUTO-band re-anchored op must actually be APPLIED (not just reported) through the Patch Engine");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1b. FR-S02 (spaarkeai-compose-r8 task 011) — a stale-base save on the WHOLE-BODY ContentModel path
    //     PERSISTS (last-writer-wins) and WARNS. Owner decision 2026-08-19, superseding the UAT-25/26
    //     412 refusal this test previously asserted.
    //
    //     Why the reversal is the safer behavior, not the laxer one: Compose versions every save, so the
    //     superseded writer's content is the PREVIOUS version and is recoverable from version history —
    //     whereas the refusal left the USER with unsaved work in a browser tab and no way forward, and
    //     its client-side recovery handler was dead code the day it shipped (r8 task 010, FR-S01).
    //
    //     This test is also the NFR-08 pairing for the outcome: the server half asserted here, the
    //     rendered client affordance asserted in ComposeWorkspace.saveErrorRouting.test.tsx.
    //
    //     Third assertion, the one that makes "last writer wins" a guarantee rather than a hope: the PUT
    //     carries `If-Match` set to the LIVE version this save's baseline was resolved against, closing
    //     the check-then-act window between the metadata read and the write.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Save_StaleBase_ContentModelPath_PersistsLastWriterWins_WarnsAndSendsIfMatch_ThroughTheWire()
    {
        const string speId = "spe-item-uat25-stale-model";
        const string driveId = "drive-uat25-stale-model";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // ── Save #1 seeds the version stamp at "v1-etag" (the assert-baseline for the next save). ──
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });
        var firstBody = await firstSave.Content.ReadAsStringAsync();
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK, $"the seeding save must succeed — body: {firstBody}");

        // ── An EXTERNAL writer lands a new version: the live SPE eTag now differs from the "v1-etag" stamp. ──
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag-external\""));

        // The stale-base save now WRITES (last-writer-wins). Capture the If-Match the write carried — a
        // resolved live version means the save must go through the PRECONDITIONED overload, never the
        // blind one, or the read-to-write window is still open.
        var wroteAfterSeed = false;
        string? sentIfMatch = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, _, ifMatch, _) =>
            {
                wroteAfterSeed = true;
                sentIfMatch = ifMatch;
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v3-etag\""));

        // ── Save #2 on the whole-body ContentModel path against the stale base. ──
        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            contentModel = new { blocks = Array.Empty<object>() },
        });

        var secondBody = await secondSave.Content.ReadAsStringAsync();
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"FR-S02: a moved base no longer refuses the save — concurrency is last-writer-wins with a warning — body: {secondBody}");

        wroteAfterSeed.Should().BeTrue("the save must actually persist — last-writer-wins means the write happens");

        sentIfMatch.Should().Be("\"v2-etag-external\"",
            "the PUT must carry If-Match set to the LIVE version this save's baseline was resolved against — " +
            "that is what closes the check-then-act window and makes last-writer-wins a guarantee rather than a hope. " +
            "Sending the client's stale load-time ETag instead would re-create the refusal FR-S02 removed.");

        secondBody.Should().Contain("concurrent-external-change",
            "the user MUST be told their save superseded another writer's version, with version history as the " +
            "recovery — a silent supersession is the dishonest half of last-writer-wins");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. NEGATIVE — an un-re-anchorable operation surfaces as ORPHAN, never silently dropped, never
    //    silently (mis-)applied — task 050's "no operation is ever silently lost" contract.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_StaleBase_UnReanchorableOperation_SurfacesAsOrphan_NotDroppedNotApplied_ThroughTheWire()
    {
        const string speId = "spe-item-054-stale-orphan";
        const string driveId = "drive-054-stale-orphan";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag-external\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(original.ToArray()));

        // FR-S02 (r8 task 011): the post-stale write goes through the IF-MATCH overload — an existing-item
        // save always reads live metadata, so `preWriteETag` is set and the PUT carries the precondition.
        // The etag-less overload above still serves the seed save (no metadata read, nothing to assert).
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
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v3-etag\""));

        // A paraId present in NEITHER the retained baseline NOR the freshly re-downloaded current
        // bytes — the paraId-primary path misses, AND the fuzzy fallback has no textPattern to score
        // (hint=-1 in the OLD baseline too), so the combined score is 0.0 — below even REVIEW. Never a
        // forced pin to "the least bad" paragraph (Spike 6 scenario F) — a genuine ORPHAN.
        const string unresolvableParaId = "DEADBEEF";
        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = unresolvableParaId, at = new { runIndex = 0, offset = 0 }, text = "should never land" },
            },
        };

        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var secondBody = await secondSave.Content.ReadAsStringAsync();
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"an un-re-anchorable operation is SURFACED, not a hard failure — body: {secondBody}");

        var saveResult = await secondSave.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult!.ReanchorSummary.Should().NotBeNull();
        saveResult.ReanchorSummary!.Total.Should().Be(1);
        saveResult.ReanchorSummary.AutoCount.Should().Be(0);
        saveResult.ReanchorSummary.OrphanCount.Should().Be(1,
            "an unresolvable paraId (absent from both the old baseline and the current bytes) must surface as ORPHAN — never silently dropped");
        var orphan = saveResult.ReanchorSummary.Annotations.Single();
        orphan.Band.Should().Be(ReanchorBand.Orphan);
        orphan.MatchedParagraphIndex.Should().Be(-1);

        persisted.Should().NotBeNull();
        using var persistedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        persistedDoc.MainDocumentPart!.Document!.Body!.Descendants<Text>()
            .Should().NotContain(t => t.Text.Contains("should never land"),
                "the ORPHAN operation must NEVER be silently applied — the current bytes pass through unmodified apart from re-anchoring");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2b. Seam A (ai-advanced-capabilities-nda-r1 UAT round-2 item #4) — a STALE-base save whose
    //     advisory comment is anchored to a CLIENT-MINTED paraId (present only in the save's paraIdMap,
    //     physically ABSENT from the docx — the uploaded-NDA case) is STAMPED into the current bytes in
    //     the re-anchor path, so the comment re-anchors AUTO (exact paraId) and BAKES as a native
    //     w:comment. Before the fix this exact case orphaned every advisory comment ("comments don't
    //     survive Save to Word"): the client-minted paraId matched nothing in the re-downloaded current
    //     bytes → 0.0 score → ORPHAN → surfaced-but-never-baked. This test fails without the Stamp call
    //     added to ReanchorStaleSaveAsync.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_StaleBase_ClientMintedComment_StampsAndBakesNativeComment_ThroughTheWire()
    {
        const string speId = "spe-item-nda-stale-minted-comment";
        const string driveId = "drive-nda-stale-minted-comment";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        // An UPLOADED NDA carries NO physical w14:paraIds — every editor id is client-minted and travels
        // ONLY in the save's paraIdMap (never physically in the bytes). Build the doc id-less to model
        // exactly that (the case the non-stale path already handles via its own Stamp, but the stale
        // re-anchor path did not until Seam A).
        var original = BuildDocxWithoutParaIds(Paragraphs);

        // The client's load-time paraId map: one entry per body paragraph, in document order.
        var mintedParaIds = new[] { "0A000001", "0A000002", "0A000003" };
        var paraIdMap = new object[]
        {
            new { index = 0, paraId = mintedParaIds[0], text = Paragraphs[0] },
            new { index = 1, paraId = mintedParaIds[1], text = Paragraphs[1] },
            new { index = 2, paraId = mintedParaIds[2], text = Paragraphs[2] },
        };

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // ── Save #1 — seed the version stamp at v1 (no ops/comments; persists the id-less original). ──
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the seeding save must succeed — body: {await firstSave.Content.ReadAsStringAsync()}");

        // ── A BENIGN external version bump: the live SPE eTag moves (v2 ≠ the v1 stamp → STALE) but the
        //    re-downloaded bytes are the SAME id-less content (the exact "opened in Word / re-saved,
        //    version counter moved, content effectively unchanged" case the UAT hit). ────────────────
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag-external\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(original.ToArray()));

        // FR-S02 (r8 task 011): the post-stale write goes through the IF-MATCH overload — an existing-item
        // save always reads live metadata, so `preWriteETag` is set and the PUT carries the precondition.
        // The etag-less overload above still serves the seed save (no metadata read, nothing to assert).
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
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v3-etag\""));

        // ── Save #2 — an advisory comment anchored to paragraph 1's CLIENT-MINTED paraId, plus the
        //    paraIdMap. Stale path fires → Seam A stamps the minted ids into the current bytes → the
        //    comment re-anchors AUTO (confidence 1.0) → bakes as a native w:comment. ────────────────────
        const string commentBody = "Flag: this confidentiality carve-out is broader than the firm standard.";
        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            paraIdMap,
            comments = new object[]
            {
                new
                {
                    paraId = mintedParaIds[1],
                    range = new { start = new { runIndex = 0, offset = 0 }, end = new { runIndex = 0, offset = 10 } },
                    commentText = commentBody,
                    author = "AI Advisory Review",
                    date = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc),
                },
            },
        });

        var secondBody = await secondSave.Content.ReadAsStringAsync();
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a stale-base comment save must re-anchor and complete — NEVER an eTag 500 — body: {secondBody}");

        var saveResult = await secondSave.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult.Should().NotBeNull();
        saveResult!.ReanchorSummary.Should().NotBeNull("a stale-base save surfaces the re-anchor summary");
        saveResult.ReanchorSummary!.Total.Should().Be(1);
        saveResult.ReanchorSummary.AutoCount.Should().Be(1,
            "Seam A stamped the client-minted paraId into the current bytes → the comment re-anchors as an exact-paraId AUTO match (was ORPHAN before the fix)");
        saveResult.ReanchorSummary.OrphanCount.Should().Be(0,
            "the advisory comment must NOT orphan on a stale save — that was the 'comments don't survive Save to Word' bug");

        persisted.Should().NotBeNull("the SPE facade must have captured the re-anchored, patched bytes");
        using var patchedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        var commentedPara = patchedDoc.MainDocumentPart!.Document!.Body!.Descendants<Paragraph>()
            .Single(p => string.Equals(p.ParagraphId?.Value, mintedParaIds[1], StringComparison.OrdinalIgnoreCase));
        commentedPara.Descendants<CommentRangeStart>().Should().NotBeEmpty(
            "the stamped paragraph must carry the native w:comment range markers after the stale save");
        var commentsPart = patchedDoc.MainDocumentPart!.WordprocessingCommentsPart;
        commentsPart.Should().NotBeNull("a native w:comment part must exist after the advisory comment bakes");
        commentsPart!.Comments!.Descendants<Comment>()
            .SelectMany(c => c.Descendants<Text>())
            .Select(t => t.Text)
            .Should().Contain(t => t.Contains("broader than the firm standard", StringComparison.Ordinal),
                "the advisory comment body must be emitted natively — the whole point of 'comments travel with the file'");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1c. FR-S07 (spaarkeai-compose-r8 task 014) — a stale-base re-anchor that CANNOT re-download the
    //     current bytes must write NOTHING.
    //
    //     The defect this replaces: the re-download failure returned the LOAD-TIME baseline as the bytes
    //     to persist, and the save proceeded. Because this branch runs only when the base has ALREADY
    //     been observed to move, those bytes are by definition older than the version about to be
    //     overwritten — so the fallback silently replaced a newer document with pre-edit content, and
    //     reported HTTP 200. It is the only data-destroying path in Track S, and the only one on the
    //     engine side rather than the client contract.
    //
    //     The fallback is deleted, not guarded: a re-anchor with no current bytes has no valid basis for
    //     a save. The refusal is a defined terminal outcome (`refused-stale`, FR-S06), never an HTTP 422
    //     content-refusal (ADR-049).
    //
    //     Two arrangements, because the SPE facade can fail either way and both took the same fallback:
    //     the download THROWS, and the download returns NULL.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(true)]   // DownloadFileAsUserAsync throws
    [InlineData(false)]  // DownloadFileAsUserAsync returns null
    public async Task Save_StaleBase_ReanchorDownloadFails_WritesNothing_RefusesStale_ThroughTheWire(bool downloadThrows)
    {
        var speId = $"spe-item-frs07-{(downloadThrows ? "throw" : "null")}";
        var driveId = $"drive-frs07-{(downloadThrows ? "throw" : "null")}";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // ── Save #1 seeds the version stamp at "v1-etag" (the assert-baseline for the next save). ──
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the seeding save must succeed — body: {await firstSave.Content.ReadAsStringAsync()}");

        // ── An EXTERNAL writer lands a new version, so this save's base has moved and the op log must be
        //    re-anchored against the CURRENT bytes... which we then make unobtainable. ──────────────────
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v2-etag-external\""));

        if (downloadThrows)
        {
            _fixture.SpeMock
                .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated SPE download failure on the re-anchor path."));
        }
        else
        {
            _fixture.SpeMock
                .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
                .ReturnsAsync((Stream?)null);
        }

        // Capture ANY write attempted after the seed, on EITHER overload, along with the bytes it carried.
        // Capturing the bytes rather than only a flag is deliberate: when this assertion failed against the
        // pre-fix code it showed WHICH document would have been written, which is the evidence that the
        // fallback was destructive rather than merely wasteful.
        byte[]? persistedAfterSeed = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                persistedAfterSeed = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v3-etag\""));

        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[FR-S07]" },
            },
        };

        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var body = await secondSave.Content.ReadAsStringAsync();

        // (a) NOTHING was written. This is the assertion that matters — the stored version must be exactly
        //     what the external writer left, not our pre-edit copy of it.
        persistedAfterSeed.Should().BeNull(
            "a re-anchor that could not obtain the current bytes has no valid basis for a save; writing the " +
            "load-time baseline would overwrite a version we already know is newer");

        // (b) The failure is a DEFINED refusal the user can act on — not a 200, and not a 422 content
        //     refusal (ADR-049 forbids reintroducing that failure mode on this path).
        secondSave.StatusCode.Should().Be(HttpStatusCode.Conflict,
            $"the save is refused because the base moved and could not be rebased — body: {body}");
        secondSave.StatusCode.Should().NotBe(HttpStatusCode.UnprocessableEntity,
            "ADR-049: the outcome is a defined save outcome, never a content refusal");
        body.Should().Contain("could not", "the detail must say plainly why the save did not happen");
        body.Should().Contain("still", "and that the user's changes survive for a retry");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. NEGATIVE — an unrelated, non-lock, non-precondition failure during the SPE write still
    //    surfaces as a GENERIC 500 ProblemDetails — proving the DEF-14/051 typed catches for 423/412
    //    do not swallow or misclassify an unrelated failure as a lock/precondition.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_UnrelatedNonLockFailure_SurfacesGenericProblemDetails_NotMisclassifiedAsLock_ThroughTheWire()
    {
        const string speId = "spe-item-054-generic-5xx";
        const string driveId = "drive-054-generic-5xx";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // An UNRELATED failure — deliberately NOT DocumentLockedByWordException / EtagPreconditionFailedException
        // — must still surface as a plain 500 ProblemDetails, not silently mapped to 423/412 copy.
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Simulated unrelated SPE failure — not a lock, not a precondition."));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError,
            "an unrelated failure is a genuine 500 — the typed 423/412 catches must not swallow it as a false negative");

        var payload = await response.Content.ReadAsStringAsync();
        payload.Should().Contain("Internal Server Error");
        payload.Should().NotContain("checked out", "an unrelated failure must NOT be misclassified as the 423 lock copy");
        payload.Should().NotContain("This document changed since you opened it", "an unrelated failure must NOT be misclassified as the 412 precondition copy");
    }

    // ════════════════════════════════════════
    // 4. FR-S08 (spaarkeai-compose-r8 task 015) — the document-size ceiling is a STATED limit, not a
    //    transport rejection.
    //
    //    Two ceilings used to sit on this path and neither told the user anything:
    //      - `UploadSmallAsUserAsync` threw an ArgumentException above 4 MB, enforcing a Graph limit that
    //        has not existed since October 2023 (simple upload is 250 MB, SPE-confirmed). A first save of
    //        any document over 4 MB failed outright.
    //      - Kestrel's 30 MB default request-body cap rejected the base64+JSON envelope from about 22 MB
    //        of document up, at the transport layer, before any handler ran.
    //
    //    Now: ONE limit (`ComposeSaveLimits.MaxDocumentBytes`), enforced on the shared save path with a
    //    ProblemDetails that names it, and a request-body cap derived from the same constant so the
    //    transport can never pre-empt the honest refusal.
    // ════════════════════════════════════════

    [Fact]
    public async Task Save_DocumentOverTheLimit_RefusedWithTheStatedLimit_NoRawTransportRejection()
    {
        const string speId = "spe-item-frs08-oversize";
        const string driveId = "drive-frs08-oversize";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // Nothing may be written. BOTH overloads are watched: a refusal that still touched storage would
        // be a refusal in name only.
        var wrote = false;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback(() => wrote = true)
            .ReturnsAsync(BuildFileHandle(speId, driveId, 1, "\"v1\""));
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Callback(() => wrote = true)
            .ReturnsAsync(BuildFileHandle(speId, driveId, 1, "\"v1\""));

        // One byte over the limit — the boundary IS the contract, so test at the boundary.
        var oversize = new byte[ComposeSaveLimits.MaxDocumentBytes + 1];
        oversize[0] = 0x50; oversize[1] = 0x4B; oversize[2] = 0x03; oversize[3] = 0x04;

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = oversize,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest,
            $"an oversize document is refused as invalid — retrying it unchanged cannot succeed. Body: {body}");
        response.StatusCode.Should().NotBe(HttpStatusCode.RequestEntityTooLarge,
            "a raw 413 carries no body and tells the user nothing about what to do");

        // The message must state the ACTUAL enforced number. This assertion is what keeps the advertised
        // limit and the enforced limit the same thing: it reads the very constant the endpoint enforces,
        // so changing one without the other fails here.
        body.Should().Contain(ComposeSaveLimits.MaxDocumentDisplay,
            "the refusal must name the real limit, not a stale hard-coded number");
        body.Should().Contain("Document Too Large");
        body.Should().Contain("still here", "and must say the user's work survives");

        wrote.Should().BeFalse("an oversize document is refused before any render or byte transfer");
    }

    [Fact]
    public async Task Save_DocumentOverFourMegabytes_Succeeds_TheStaleGraphCeilingIsGone()
    {
        const string speId = "spe-item-frs08-6mb";
        const string driveId = "drive-frs08-6mb";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // A real, openable docx padded past the retired 4 MB boundary. Padding a custom part (rather than
        // appending junk to the zip) keeps it a document the save path genuinely processes, so this
        // exercises the real pipeline instead of an early parse rejection.
        // 12 MB of filler, not 6: GUID hex still deflates by roughly half inside the OPC zip, so the
        // request is pre-compression and the assertion below is what actually holds the contract.
        var large = BuildDocxPaddedTo(12 * 1024 * 1024);
        large.Length.Should().BeGreaterThan(4 * 1024 * 1024, "the fixture must clear the OLD 4 MB ceiling");
        large.Length.Should().BeLessThan((int)ComposeSaveLimits.MaxDocumentBytes, "and stay under the current limit");

        var wrote = false;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback(() => wrote = true)
            .ReturnsAsync(BuildFileHandle(speId, driveId, large.Length, "\"v1-large\""));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = large,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a 6 MB document sits far inside both Graph's 250 MB simple-upload limit and Compose's own {ComposeSaveLimits.MaxDocumentDisplay}. Body: {body}");
        wrote.Should().BeTrue("the document must actually reach storage — a 200 that wrote nothing is the FR-S06 defect");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared arrange + OOXML helpers.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. FR-S09 (r8 task 016) — the honest-failure set, server half.
    //
    //    Three defects that shared one signature: the save ended in a state the response did not
    //    describe. (a) the SPE write landed and the Dataverse record step did not, and the user was
    //    told "not saved"; (b) Graph asked us to wait and the user was told the server had errored;
    //    (c) every replace save left `sprk_filesize`/`sprk_filepath` describing the FIRST version
    //    forever, and nothing anywhere said so. Plus (d), the negative: a healthy save is untouched.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_RecordPromotionFailsAfterTheWrite_ReportsPartiallyRecorded_NotAFailedSave()
    {
        const string speId = "spe-item-frs09-promote";
        const string driveId = "drive-frs09-promote";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // The write SUCCEEDS. This is the whole point: the bytes are durable before the record step runs.
        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, body, _) =>
            {
                using var ms = new MemoryStream();
                body.CopyTo(ms);
                persisted = ms.ToArray();
            })
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1\""));

        // ...and THEN Dataverse is unavailable. A TimeoutException (not an InvalidOperationException) on
        // purpose: the alt-key lookup swallows InvalidOperationException as "not found", and the endpoint
        // maps the two identity-KEY faults itself — this is the third class, the one that used to fall
        // through to `catch (Exception)` and return a 500 reading "Save failed: ...".
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("Dataverse request timed out."));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();

        persisted.Should().NotBeNull("the SPE write ran and completed before the record step was attempted");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the document IS stored — a 500 here told the user their save failed while their bytes sat safely in storage. Body: {body}");
        body.Should().Contain("partially-recorded",
            "the bytes are durable and the identity record is not — that is precisely what this member means");
        body.Should().NotContain("storage-failed",
            "storage succeeded; saying otherwise would tell the user their document is gone when it provably is not");
        body.Should().NotContain("\"outcome\":\"persisted\"",
            "and it must not read as a clean success either — the record step really did fail");
    }

    [Fact]
    public async Task Save_GraphThrottles_Returns429WithRetryAfter_NotAGenericFiveHundred()
    {
        const string speId = "spe-item-frs09-throttle";
        const string driveId = "drive-frs09-throttle";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // The typed translation `UploadSessionManager` produces from a Graph 429 — the same level the
        // 423 lock tests mock at.
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Sprk.Bff.Api.Infrastructure.Graph.GraphThrottledException(speId, TimeSpan.FromSeconds(17)));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            $"a throttle is a rate limit, not a server fault — it used to surface as HTTP 500. Body: {body}");
        response.Headers.RetryAfter?.Delta.Should().Be(TimeSpan.FromSeconds(17),
            "Graph told us how long to wait, and that number is the only actionable part of a throttle — it was being discarded");
        body.Should().Contain("17", "the wait must reach the user, not just the header");
        body.Should().Contain("still here", "and the copy must say the user's work survives");
        body.Should().NotContain("InvalidOperationException",
            "the old message leaked a .NET type name at the user, which reads as a crash");
    }

    [Fact]
    public async Task Save_ReplacePath_RefreshesFileSizeAndFilePath_OnTheExistingRecord()
    {
        const string speId = "spe-item-frs09-metadata";
        const string driveId = "drive-frs09-metadata";
        const string newWebUrl = "https://contoso.sharepoint.com/contentstorage/spe-item-frs09-metadata";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        // The idempotent EXISTING-row branch — i.e. every save after the first. This is the branch that
        // used to return without touching the row.
        var existingDocumentId = Guid.NewGuid();
        _fixture.DataverseMock
            .Setup(d => d.RetrieveByAlternateKeyAsync(
                It.IsAny<string>(), It.IsAny<KeyAttributeCollection>(), It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Entity("sprk_document", existingDocumentId));
        _fixture.IndexingMock
            .Setup(i => i.EnqueueIfApplicableAsync(
                It.IsAny<PostUploadIndexingRequest>(), It.IsAny<HttpContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(PostUploadIndexingResult.Submitted(Guid.NewGuid()));

        Guid? updatedId = null;
        Dictionary<string, object>? updatedFields = null;
        _fixture.DataverseMock
            .Setup(d => d.UpdateAsync("sprk_document", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Guid, Dictionary<string, object>, CancellationToken>((_, id, fields, _) =>
            {
                updatedId = id;
                updatedFields = fields;
            })
            .Returns(Task.CompletedTask);

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // The write reports the NEW size and the NEW web URL — the two facts the row was never told.
        const int newSize = 4242;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new FileHandleDto(
                Id: speId, Name: "concurrency-seam.docx", ParentId: null, Size: newSize,
                CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                ETag: "\"v2\"", IsFolder: false, WebUrl: newWebUrl, DriveId: driveId));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"the save itself is unaffected — body: {body}");

        updatedId.Should().Be(existingDocumentId, "the refresh must target the row this document already has");
        updatedFields.Should().NotBeNull(
            "a replace save changed the file's size and URL; the row used to keep reporting the FIRST version's forever");
        var fields = updatedFields!;
        fields.Should().ContainKey("sprk_filesize");
        fields["sprk_filesize"].Should().Be(newSize,
            "the Documents grid reads this column — a stale size is a wrong number shown to the user, not a hidden one");
        fields.Should().ContainKey("sprk_filepath");
        fields["sprk_filepath"].Should().Be(newWebUrl,
            "\"Open in SharePoint\" follows this column");

        // Identity is the create branch's business. A later save must never mutate it.
        fields.Should().NotContainKey("sprk_composeorigin");
        fields.Should().NotContainKey("sprk_composetransientkey");
        fields.Should().NotContainKey("sprk_graphitemid");
    }

    [Fact]
    public async Task Save_HealthyReplace_StillReportsPersisted_WithNoNewWarnings()
    {
        // NEGATIVE (FR-S09 acceptance): none of the above may cost the ordinary case anything. A clean
        // replace save must still be a plain `persisted` with no warning surface — the outcome decision
        // now reads the record step and the metadata refresh, and this is what proves that reading them
        // did not turn every healthy save into a warning.
        const string speId = "spe-item-frs09-healthy";
        const string driveId = "drive-frs09-healthy";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();
        _fixture.DataverseMock
            .Setup(d => d.UpdateAsync("sprk_document", It.IsAny<Guid>(), It.IsAny<Dictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1\""));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {body}");
        body.Should().Contain("\"outcome\":\"persisted\"", "a healthy save is a plain success");
        body.Should().NotContain("partially-recorded");
        body.Should().NotContain("document-metadata-stale");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 3. NEGATIVE — a FUZZY auto-band re-anchor (band=AUTO on content similarity, confidence < 1.0) is
    //    SURFACED but NOT applied. This is invariant I-7 in its sharpest form, and it had no test.
    //
    //    Discovered 2026-08-29 by task 070's cluster-1 mutation pass: deleting the `Confidence >= 1.0`
    //    half of the auto-apply gate — i.e. auto-applying every AUTO-band result, fuzzy ones included —
    //    left all 1,791 Compose tests green. The suite covered exact-paraId AUTO (confidence 1.0) and
    //    total ORPHAN (0.0) but never produced a score BETWEEN the two, so the one branch that decides
    //    "scored well on content" != "is the same paragraph" was unguarded.
    //
    //    Why the distinction matters: an op's anchor is never rewritten (no write-path text search), so
    //    an op auto-applied against a paragraph the SCORER liked would be applied under its ORIGINAL
    //    paraId — landing on the wrong paragraph or failing to resolve at all. Exact-id AUTO is safe;
    //    fuzzy AUTO is a suggestion for the user, and must stay one.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_StaleBase_FuzzyAutoBandMatch_SurfacedButNotApplied_ThroughTheWire()
    {
        const string speId = "spe-item-070-stale-fuzzy-auto";
        const string driveId = "drive-070-stale-fuzzy-auto";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        // The external writer's version: an ordinary Word round trip that REGENERATED every paraId
        // (Open-XML-SDK #925 — the documented reason paraId is not a durable file key) and made a small
        // edit to the first paragraph. The op's paraId now matches nothing, so the fuzzy scorer runs:
        // content similarity ≈ 0.89 against paragraph 0, structural proximity 1.0 (same index) →
        // combined ≈ 0.92, comfortably over the 0.85 AUTO cut-point but NOT the exact-id 1.0.
        var driftedParagraphs = new[]
        {
            Paragraphs[0] + " Amended.",
            Paragraphs[1],
            Paragraphs[2],
        };
        var externalVersion = BuildDocxWithParaIds(driftedParagraphs, new[] { "AAAA0001", "AAAA0002", "AAAA0003" });

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the seeding save must succeed — body: {await firstSave.Content.ReadAsStringAsync()}");

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, externalVersion.Length, "\"v2-etag-external\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(externalVersion.ToArray()));

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
            .ReturnsAsync(BuildFileHandle(speId, driveId, externalVersion.Length, "\"v3-etag\""));

        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[FUZZY-MUST-NOT-LAND]" },
            },
        };

        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments = (object?)null,
        });

        var secondBody = await secondSave.Content.ReadAsStringAsync();
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a fuzzy re-anchor is surfaced for review, not a hard failure — body: {secondBody}");

        var saveResult = await secondSave.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult!.ReanchorSummary.Should().NotBeNull();
        saveResult.ReanchorSummary!.Total.Should().Be(1);
        saveResult.ReanchorSummary.AutoCount.Should().Be(1,
            "content similarity ≈0.92 clears the 0.85 AUTO cut-point — the scorer's band is AUTO");

        var annotation = saveResult.ReanchorSummary.Annotations.Single();
        annotation.Band.Should().Be(ReanchorBand.Auto);
        annotation.Confidence.Should().BeInRange(ReanchorBands.AutoThreshold, 0.9999,
            "this must be a FUZZY auto — over the AUTO cut-point but strictly below the exact-paraId 1.0, " +
            "which is the whole scenario under test; if it reaches 1.0 the fixture stopped exercising the branch");
        annotation.MatchedParagraphIndex.Should().Be(0);
        annotation.StructuralProximity.Should().Be(1.0,
            "the paragraph hint comes from the op's paraId position in the RETAINED baseline (index 0) — " +
            "an off-by-one there would silently degrade every fuzzy score");

        // The assertion the whole test exists for.
        persisted.Should().NotBeNull();
        using var persistedDoc = WordprocessingDocument.Open(new MemoryStream(persisted!, writable: false), isEditable: false);
        persistedDoc.MainDocumentPart!.Document!.Body!.Descendants<Text>().Select(t => t.Text)
            .Should().NotContain(t => t.Contains("[FUZZY-MUST-NOT-LAND]", StringComparison.Ordinal),
                "ONLY an exact-paraId match (confidence 1.0) may be auto-applied — a fuzzy AUTO scored well on " +
                "CONTENT but is not known to be the same paragraph, and the op carries its original paraId " +
                "unrewritten (I-7). It is reported for the user to redo, never silently applied.");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 4. NEGATIVE — when the re-downloaded current bytes cannot be READ as a document, the save
    //    fails CLOSED: every op AND every comment surfaces as ORPHAN, and nothing is applied.
    //
    //    Also found unguarded by the task-070 cluster-1 mutation pass: zeroing the fail-closed summary's
    //    OrphanCount left all 1,791 tests green. The suite exercised ORPHAN as produced by the SCORER
    //    (a paraId that matches nothing) but never the fallback that runs when scoring cannot happen at
    //    all — so the code path that guarantees "never silently dropped" when the corpus is unreadable
    //    could report an empty summary and no test would notice.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_StaleBase_CurrentBytesUnreadable_EveryOpAndCommentSurfacesAsOrphan_ThroughTheWire()
    {
        const string speId = "spe-item-070-stale-unreadable";
        const string driveId = "drive-070-stale-unreadable";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, original.Length, "\"v1-etag\""));

        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the seeding save must succeed — body: {await firstSave.Content.ReadAsStringAsync()}");

        // The base moved, AND what came back is not an openable package (a truncated/corrupt download —
        // the case the paragraph-corpus read is wrapped in a try/catch for).
        var unreadable = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0xFF, 0xFF, 0xFF, 0xFF };
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, unreadable.Length, "\"v2-etag-external\""));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(unreadable.ToArray()));
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, unreadable.Length, "\"v3-etag\""));

        // Two ops and one comment: the fail-closed summary must account for BOTH collections, not just ops.
        var operationLog = new
        {
            schemaVersion = "compose-ops-v2",
            operations = new object[]
            {
                new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[OP-ONE]" },
                new { type = "insertText", paraId = ParaIds[1], at = new { runIndex = 0, offset = 0 }, text = "[OP-TWO]" },
            },
        };
        var comments = new object[]
        {
            new
            {
                paraId = ParaIds[2],
                range = new { start = new { runIndex = 0, offset = 0 }, end = new { runIndex = 0, offset = 8 } },
                commentText = "please revisit this clause",
                author = "AI Advisory Review",
                date = "2026-08-29T00:00:00Z",
            },
        };

        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog,
            comments,
        });

        var secondBody = await secondSave.Content.ReadAsStringAsync();
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"an unreadable current base degrades to an all-orphan report, a DEFINED outcome — body: {secondBody}");

        var saveResult = await secondSave.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        saveResult!.ReanchorSummary.Should().NotBeNull();
        saveResult.ReanchorSummary!.Total.Should().Be(3, "two operations plus one comment are all accounted for");
        saveResult.ReanchorSummary.AutoCount.Should().Be(0);
        saveResult.ReanchorSummary.ReviewCount.Should().Be(0);
        saveResult.ReanchorSummary.OrphanCount.Should().Be(3,
            "when the current bytes cannot be read, NOTHING can be safely anchored — every op and comment " +
            "must surface as ORPHAN rather than vanish from the report");
        saveResult.ReanchorSummary.Annotations.Should().HaveCount(3)
            .And.OnlyContain(a => a.Band == ReanchorBand.Orphan && a.MatchedParagraphIndex == -1);
        saveResult.ReanchorSummary.Annotations.Should().Contain(a => a.Type == "comment",
            "the comment must appear in the fail-closed report as its own entry, not be folded into the op count");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 5. NEGATIVE — a save whose BASELINE resolves to PDF bytes is refused with an honest 422 and
    //    writes nothing, by BOTH routes a PDF can reach the baseline resolver.
    //
    //    The guard is the task-040 Step-9.5 HIGH-2 fix, and task 070's cluster-3 mutation pass found it
    //    completely untested: disabling it left all 1,791 Compose tests green. Without it a %PDF- baseline
    //    either throws deep inside the OOXML stack as a generic 500, or — the worse outcome — the save
    //    proceeds and writes DOCX bytes over the .pdf drive item, destroying the source document.
    //
    //    Its doc comment names two ways a PDF gets there, so both are covered: the raw PDF echoed back as
    //    "retained bytes", and a re-fetched version of a .pdf item. One test each, because a guard that is
    //    only proven on one of its two entry paths is only half a guard.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    /// <summary>%PDF- magic plus enough filler to look like a real payload — the sniff reads the first
    /// five bytes, which is exactly the point: the guard must not need to parse anything to refuse.</summary>
    private static readonly byte[] PdfMagicBytes = "%PDF-1.7\n%âãÏÓ\n1 0 obj\n<< >>\nendobj\n"u8.ToArray();

    [Fact]
    public async Task Save_RetainedBytesAreAPdf_RefusedWithFourTwentyTwo_NothingWritten_ThroughTheWire()
    {
        const string speId = "spe-item-070-pdf-retained";
        const string driveId = "drive-070-pdf-retained";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, PdfMagicBytes.Length, "\"v1-etag\""));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = PdfMagicBytes,
            operationLog = new
            {
                schemaVersion = "compose-ops-v2",
                operations = new object[]
                {
                    new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[NEVER]" },
                },
            },
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"a PDF baseline is refused LOUDLY at the single choke point, never allowed deeper into the " +
            $"OOXML stack where it surfaces as a generic 500 — body: {body}");
        body.Should().Contain("PDF Cannot Be Saved In Place",
            "the refusal must be the honest, actionable one — a document opened from a PDF saves as a NEW " +
            "Word document via create-on-save; it cannot replace the PDF in place");

        AssertNothingWrittenTo(driveId, speId);
    }

    [Fact]
    public async Task Save_RefetchedBaselineVersionIsAPdf_RefusedWithFourTwentyTwo_NothingWritten_ThroughTheWire()
    {
        const string speId = "spe-item-070-pdf-refetch";
        const string driveId = "drive-070-pdf-refetch";
        const string baselineVersionId = "pdf-version-1";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, PdfMagicBytes.Length, "\"v1-etag\""));

        // No retained bytes — the FR-06 route: the save re-fetches its load-time version, and what comes
        // back is a PDF (a stale/rogue caller pointing at a .pdf item's version).
        _fixture.SpeMock
            .Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, baselineVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(PdfMagicBytes.ToArray()));

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            baselineVersionId,
            operationLog = new
            {
                schemaVersion = "compose-ops-v2",
                operations = new object[]
                {
                    new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[NEVER]" },
                },
            },
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity,
            $"the re-fetch route passes through the same single choke point as the retained-bytes route — " +
            $"body: {body}");
        body.Should().Contain("PDF Cannot Be Saved In Place");

        AssertNothingWrittenTo(driveId, speId);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 7. FR-01/FR-06 — when the load-time baseline version is GONE, the save fails. It does not proceed
    //    on empty bytes.
    //
    //    Third hole from task 070's cluster-3 mutation pass: replacing the "version not found" throw with
    //    `return Array.Empty<byte>()` left the whole suite green. That mutant is not a subtle one — a save
    //    would apply its delta onto zero bytes and write the result over a real document. "A dirty save
    //    never falls back to a reconstruction" was stated in a comment and enforced by a throw that no
    //    test had ever caused to fire.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_BaselineVersionNoLongerExists_FailsLoudly_WritesNothing_ThroughTheWire()
    {
        const string speId = "spe-item-070-missing-version";
        const string driveId = "drive-070-missing-version";
        const string baselineVersionId = "version-that-was-pruned";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, 1024, "\"v1-etag\""));

        // The client lost its in-memory bytes (page refresh) and asks for its load-time version back —
        // but that version is gone (pruned / the item was replaced out from under it).
        _fixture.SpeMock
            .Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, baselineVersionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Stream?)null);

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            baselineVersionId,
            operationLog = new
            {
                schemaVersion = "compose-ops-v2",
                operations = new object[]
                {
                    new { type = "insertText", paraId = ParaIds[0], at = new { runIndex = 0, offset = 0 }, text = "[NEVER]" },
                },
            },
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();

        // Asserting the SPECIFIC failure, not merely "not 200". A save that proceeds on empty bytes also
        // fails — the patch engine rejects the empty package — so a `NotBe(OK)` assertion passes on the
        // broken behaviour too. The distinction is what the user is told and where the save stopped: a
        // missing baseline version is a 404 refusal BEFORE any content work, not a malformed-document 422
        // discovered after the delta was applied to bytes that were never the user's document.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            $"an unresolvable baseline stops the save at resolution and is reported honestly (FR-01/FR-06) — body: {body}");
        body.Should().Contain("Document Not Found");

        AssertNothingWrittenTo(driveId, speId);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 6. FR-S02 — when the If-Match precondition fails, the save retries EXACTLY ONCE and rebases onto
    //    the version that actually landed.
    //
    //    Test 1b above proves an If-Match is SENT. Nothing proved what happens when it is REJECTED, and
    //    task 070's cluster-3 mutation pass showed it: making the retry re-send the STALE eTag — the
    //    precise mistake the method's own remarks warn about ("reusing it would fail identically, and the
    //    point of the retry is to rebase onto whatever landed") — left all 1,791 Compose tests green.
    //
    //    Both halves are asserted, because each fails differently. Re-sending the stale eTag turns a
    //    recoverable race into a dead-end save; retrying more than once lets a hot document spin.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task Save_PreconditionFails_RetriesOnceAgainstTheFreshVersion_NotTheStaleETag_ThroughTheWire()
    {
        const string speId = "spe-item-070-precondition-retry";
        const string driveId = "drive-070-precondition-retry";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var original = BuildDocxWithParaIds(Paragraphs, ParaIds);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        // The live version the save resolves against, and then the version an interleaving writer left
        // behind. The first metadata read is the save's own pre-write read; the second is the re-read the
        // retry performs, and it MUST see the newer version for the rebase to mean anything.
        var metadataReads = 0;
        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => BuildFileHandle(
                speId, driveId, original.Length, ++metadataReads == 1 ? "\"v1-etag\"" : "\"v2-etag-interleaved\""));

        var sentPreconditions = new List<string?>();
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<HttpContext, string, string, Stream, string?, CancellationToken>((_, _, _, _, ifMatch, _) =>
            {
                sentPreconditions.Add(ifMatch);
                if (sentPreconditions.Count == 1)
                {
                    // A writer landed inside the check-then-act window.
                    throw new EtagPreconditionFailedException(speId, ifMatch);
                }

                return Task.FromResult<FileHandleDto?>(BuildFileHandle(speId, driveId, original.Length, "\"v3-etag\""));
            });

        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = original,
            operationLog = (object?)null,
            comments = (object?)null,
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a lost precondition race is recoverable and must NOT surface to the user — body: {body}");

        sentPreconditions.Should().HaveCount(2,
            "exactly one retry: failing immediately would resurrect the dead-end save this contract removed, " +
            "and retrying unbounded would let a hot document spin");
        sentPreconditions[0].Should().Be("\"v1-etag\"", "the first attempt asserts the version the save resolved against");
        sentPreconditions[1].Should().Be("\"v2-etag-interleaved\"",
            "the retry must rebase onto the version that actually landed. Re-sending the stale eTag would fail " +
            "identically every time — the retry would be decoration, and the save a dead end.");
    }

    /// <summary>The half of the PDF-guard assertion that matters most: refusing with a 422 is good, but the
    /// failure being guarded against is DOCX bytes landing on a .pdf drive item, so assert directly that no
    /// write of either overload was attempted.</summary>
    private void AssertNothingWrittenTo(string driveId, string speId)
    {
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
            It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "the PDF drive-item must never be overwritten with docx bytes");
        _fixture.SpeMock.Verify(s => s.ReplaceFileContentAsUserAsync(
            It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "the PDF drive-item must never be overwritten with docx bytes");
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

    private async Task<string> CreateSessionAsync(string tenant, string speId)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(tenant, TestSessionOwner.Oid, documentId: speId);
        return session.SessionId;
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: "concurrency-seam.docx", ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);

    /// <summary>Builds a valid DOCX whose paragraphs carry the supplied physical w14:paraIds (mirrors
    /// <c>ComposeParaIdReanchorSeamTests.BuildDocxWithParaIds</c> — no shared helper extracted per-file,
    /// consistent with this suite's established per-file-local-helper convention).</summary>
    private static byte[] BuildDocxWithParaIds(IReadOnlyList<string> paragraphs, IReadOnlyList<string> paraIds)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            for (var i = 0; i < paragraphs.Count; i++)
            {
                var p = new Paragraph(new Run(new Text(paragraphs[i]) { Space = SpaceProcessingModeValues.Preserve }))
                {
                    ParagraphId = new HexBinaryValue(paraIds[i]),
                };
                body.AppendChild(p);
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }

    /// <summary>Builds a valid DOCX whose paragraphs carry NO physical w14:paraId — the shape an UPLOADED
    /// document has (every editor id is client-minted at load and travels only in the save's paraIdMap,
    /// never physically in the bytes). Used by the Seam A stale-comment test to prove the re-anchor path
    /// stamps those minted ids into the current bytes so an advisory comment bakes instead of orphaning.</summary>
    /// <summary>A VALID docx padded to at least <paramref name="targetBytes"/> via a custom XML part —
    /// still openable by the save pipeline, unlike appending raw bytes to the zip. Used to clear the
    /// retired 4 MB simple-upload ceiling with a document the server will genuinely process.</summary>
    private static byte[] BuildDocxPaddedTo(int targetBytes)
    {
        var baseDoc = BuildDocxWithParaIds(Paragraphs, ParaIds);
        using var ms = new MemoryStream();
        ms.Write(baseDoc, 0, baseDoc.Length);
        ms.Position = 0;
        using (var doc = WordprocessingDocument.Open(ms, isEditable: true))
        {
            var part = doc.MainDocumentPart!.AddCustomXmlPart(CustomXmlPartType.CustomXml);
            using var writer = new StreamWriter(part.GetStream(FileMode.Create));
            writer.Write("<padding>");
            // Fresh GUIDs, not repeated characters: repeated text deflates to a few KB and the fixture
            // would silently stay under 4 MB, quietly testing nothing.
            var written = 0;
            while (written < targetBytes)
            {
                var piece = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
                writer.Write(piece);
                written += piece.Length;
            }
            writer.Write("</padding>");
        }
        return ms.ToArray();
    }

    private static byte[] BuildDocxWithoutParaIds(IReadOnlyList<string> paragraphs)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            foreach (var text in paragraphs)
            {
                body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            }
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }
        return stream.ToArray();
    }
}
