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

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
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
    // 1b. UAT-25/26 (2026-08-18) — a stale-base save on the WHOLE-BODY ContentModel path CANNOT re-anchor
    //     (it re-authors the entire body), so it must REFUSE with 412 (reload + reapply — nothing
    //     overwritten) rather than SILENTLY OVERWRITING the external writer's new version. The honest
    //     counterpart to test #1: the op-log path re-anchors; the model path refuses.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Save_StaleBase_ContentModelPath_Refuses412_WithoutOverwriting_ThroughTheWire()
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

        // Track that the refused save NEVER overwrites (no ReplaceFileContentAsUserAsync after the seed).
        var overwroteAfterSeed = false;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback(() => overwroteAfterSeed = true)
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
        secondSave.StatusCode.Should().Be(HttpStatusCode.PreconditionFailed,
            $"a stale-base whole-body ContentModel save must refuse (412), never silently overwrite the external writer — body: {secondBody}");
        overwroteAfterSeed.Should().BeFalse(
            "nothing may be written on the refused save — the external writer's version stays intact (no silent lost-update)");
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

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
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

        byte[]? persisted = null;
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, Stream, CancellationToken>((_, _, _, stream, _) =>
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

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared arrange + OOXML helpers.
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

    private async Task<string> CreateSessionAsync(string tenant, string speId)
    {
        using var scope = _fixture.Services.CreateScope();
        var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
        var session = await sessions.CreateSessionAsync(tenant, documentId: speId);
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
