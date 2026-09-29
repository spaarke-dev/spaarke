// Task 042 (ai-advanced-capabilities-nda-r1, Phase 4, spec FR-15) — the THROUGH-THE-WIRE seam
// evidence that the inherited Compose save path creates a REAL SharePoint Embedded version on every
// save, and that a PRIOR version stays addressable after a later save has advanced the current one.
//
// WHAT THIS PROVES (production code — the real `ComposeService.SaveAsync`, reached through the real
// `POST /api/compose/documents/{id}/save` route; ONLY the `ISpeFileOperations` Graph/SPE boundary is
// doubled — ADR-038 "mock at module boundaries", the SAME fixture `ComposeFidelitySeamFixture`
// task 024/034 established, no new fixture class per CLAUDE.md §11):
//
//   (1) Every real save calls `ISpeFileOperations.ReplaceFileContentAsUserAsync` — the facade's OWN
//       XML doc states this call "commit[s] a new SPE version" (ISpeFileOperations.cs). The FAKE
//       double below honors that documented Graph contract: it mints a NEW versionId + distinct
//       ETag per invocation and RETAINS every prior version's bytes (never overwriting a slot in
//       place) — exactly what Graph itself does. Saving the SAME driveItem twice therefore produces
//       two DISTINCT, independently addressable versions — spec FR-15's "free inherited" claim.
//   (2) `ComposeService`'s OWN production baseline-resolution code (`ResolveSaveBaselineAsync`, the
//       E1 keystone) re-fetches an OLDER version by id via `DownloadFileVersionAsUserAsync` when the
//       caller supplies `BaselineVersionId` instead of retained bytes. A third save that names the
//       FIRST save's versionId as its baseline resolves to the FIRST save's bytes — NOT the second
//       (current-at-the-time) version's — proving the prior version is genuinely retrievable through
//       production code, not merely retained in name by the test's own bookkeeping.
//
// CAVEAT NOTED PER TASK CONSTRAINT (NOT a gap): `ComposeService.SaveAsync` sends NO Graph-level
// `If-Match` precondition on the `ReplaceFileContentAsUserAsync` write (see the code comment at that
// call site: "Upgrading this write to a Graph-level If-Match precondition is a further-hardening
// candidate, not required by this task's acceptance criteria"). Concurrent-write safety is instead
// handled by a SEPARATE, already-tested mechanism: the Redis (ADR-009 `IDistributedCache`) save-path
// version STAMP + assert-before-apply + `AnnotationReanchorService` re-anchor (FR-08, task 050/051)
// — see `ConcurrencySaveSeamTests.cs` in this same directory, which exercises exactly that path. This
// file deliberately does NOT set up `GetFileMetadataAsUserAsync` (Loose mock ⇒ `preWriteETag` stays
// null ⇒ the staleness/re-anchor branch never fires here — see `ComposeFidelitySeamTests.DirtySave_*`
// for the same established precedent), keeping this file scoped to the versioning claim (FR-15)
// alone, without re-testing the concurrency mechanism.
//
// ENV BOUNDARY (per task constraint — flagged, not faked): this seam test proves the WIRING contract
// (ComposeService really calls the version-committing / version-fetching facade methods, on the real
// route, and correctly threads an OLDER version's id back into a later save). It does NOT call a live
// SharePoint Embedded tenant, so it cannot itself observe Graph's live versions-endpoint behavior —
// that Graph, in production, truly commits a new version per content PUT and truly serves prior
// versions by id is documented Graph API behavior (`drives/{id}/items/{id}/versions[/…]`) and is
// asserted here via a fake that models that documented contract, per the task's explicit fallback
// instruction ("implement the test against the module-boundary fake ... flag the live-SPE assertion
// as env-blocked"). A live-tenant verification of Graph's own versioning is ENV-BLOCKED in this
// harness (no live SPE container in CI) — see task notes.
//
// ADR-038 seam DoD compliance: through-the-wire WebApplicationFactory slice only. NO
// Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test anywhere in this file. The
// mock lives ONLY at the ISpeFileOperations module boundary.

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
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class SpeSaveVersioningSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public SpeSaveVersioningSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Save_TwiceOnSameDocument_CreatesTwoDistinctSpeVersions_AndPriorVersionStaysDownloadable_ThroughTheWire()
    {
        const string speId = "spe-item-042-versioning";
        const string driveId = "drive-042-versioning";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        // ── FAKE SPE version history — models the REAL Graph contract ISpeFileOperations documents:
        //    every ReplaceFileContentAsUserAsync call commits a NEW version; every prior version's
        //    bytes stay addressable by id via DownloadFileVersionAsUserAsync (never overwritten in
        //    place). Keyed by a monotonically-assigned versionId, distinct from the driveItem id
        //    (mirrors Graph's own `drives/{d}/items/{i}/versions/{versionId}` shape). ──────────────
        var versionsById = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var versionOrder = new List<string>();

        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .Returns<HttpContext, string, string, Stream, CancellationToken>(async (_, _, _, stream, _) =>
            {
                using var ms = new MemoryStream();
                await stream.CopyToAsync(ms);
                var versionId = $"v{versionOrder.Count + 1}";
                versionOrder.Add(versionId);
                versionsById[versionId] = ms.ToArray();

                return new FileHandleDto(
                    Id: speId, Name: "nda-versioning.docx", ParentId: null, Size: ms.Length,
                    CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
                    ETag: $"\"{versionId}-etag\"", IsFolder: false, WebUrl: null, DriveId: driveId);
            });

        _fixture.SpeMock
            .Setup(s => s.GetCurrentVersionIdAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => versionOrder.Count > 0 ? versionOrder[^1] : null);

        _fixture.SpeMock
            .Setup(s => s.DownloadFileVersionAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<HttpContext, string, string, string, CancellationToken>((_, _, _, versionId, _) =>
                Task.FromResult<Stream?>(versionsById.TryGetValue(versionId, out var bytes) ? new MemoryStream(bytes) : null));

        using var client = _fixture.CreateAuthenticatedClient();
        string sessionId;
        using (var scope = _fixture.Services.CreateScope())
        {
            var sessions = scope.ServiceProvider.GetRequiredService<ChatSessionManager>();
            var session = await sessions.CreateSessionAsync(tenant, TestSessionOwner.Oid, documentId: speId);
            sessionId = session.SessionId;
        }

        var doc1 = BuildSimpleDocx("This Master Services Agreement governs the engagement (version one).");
        var doc2 = BuildSimpleDocx("This Master Services Agreement governs the engagement (version two, revised).");

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // Save #1 and Save #2 — two real saves of the SAME driveItem through the REAL /save route.
        // ═══════════════════════════════════════════════════════════════════════════════════════
        var firstSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = doc1,
        });
        var firstBody = await firstSave.Content.ReadAsStringAsync();
        firstSave.StatusCode.Should().Be(HttpStatusCode.OK, $"save #1 must succeed — body: {firstBody}");
        var firstResult = await firstSave.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        firstResult.Should().NotBeNull();

        var secondSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = doc2,
        });
        var secondBody = await secondSave.Content.ReadAsStringAsync();
        secondSave.StatusCode.Should().Be(HttpStatusCode.OK, $"save #2 must succeed — body: {secondBody}");
        var secondResult = await secondSave.Content.ReadFromJsonAsync<SaveComposeDocumentResponse>();
        secondResult.Should().NotBeNull();

        // ── Assert 1: TWO distinct SPE versions exist — the facade was invoked twice and minted a
        //    NEW version identity each time (never re-using / overwriting the prior version's slot),
        //    and the SAVE RESPONSE surfaces that new identity (ETag) to the caller. ────────────────
        versionOrder.Should().HaveCount(2,
            "every Compose save commits a NEW SPE version (FR-15) — two saves must yield two versions");
        versionOrder[0].Should().NotBe(versionOrder[1], "the two saves must NOT collapse onto the same version identity");
        secondResult!.ETag.Should().NotBe(firstResult!.ETag,
            "the save response must reflect a genuinely new version's identity on the second save, not a stale/reused one");

        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2),
            "ComposeService.SaveAsync must route every save through the SpeFileStore facade (ADR-007) — this call IS the mechanism that commits a new SPE version");

        // ── Assert 2: the PRIOR (first) version's bytes are exactly what save #1 sent — proving
        //    save #2 did not overwrite/mutate the first version's stored bytes. ────────────────────
        versionsById[versionOrder[0]].Should().Equal(doc1, "the first version's bytes must be untouched by the second save");
        versionsById[versionOrder[1]].Should().Equal(doc2, "the second (current) version's bytes must be what save #2 sent");

        // ═══════════════════════════════════════════════════════════════════════════════════════
        // Save #3 — names the FIRST save's versionId as its baseline (no retained `content` bytes).
        // Proves the PRIOR version is genuinely retrievable through ComposeService's OWN production
        // code (ResolveSaveBaselineAsync's FR-06 "primary" branch), not merely retained in the test's
        // own bookkeeping.
        // ═══════════════════════════════════════════════════════════════════════════════════════
        var firstVersionId = versionOrder[0];

        var thirdSave = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            baselineVersionId = firstVersionId,
        });
        var thirdBody = await thirdSave.Content.ReadAsStringAsync();
        thirdSave.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a save naming an OLDER baselineVersionId must resolve via DownloadFileVersionAsUserAsync — body: {thirdBody}");

        versionOrder.Should().HaveCount(3, "the baseline-only save still performs a write, minting a THIRD version");

        _fixture.SpeMock.Verify(
            s => s.DownloadFileVersionAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, firstVersionId, It.IsAny<CancellationToken>()),
            Times.Once,
            "ComposeService.ResolveSaveBaselineAsync must re-fetch the NAMED baseline version by id — the production code path proving a prior version is retrievable, not just retained in name");

        versionsById[versionOrder[2]].Should().Equal(doc1,
            "save #3's persisted bytes must equal the FIRST version's content (not the second/current version's) — " +
            "proving DownloadFileVersionAsUserAsync fetched the correctly-addressed PRIOR version, which therefore " +
            "remained addressable even after a later save advanced the current version");
    }

    // ── shared arrange + OOXML helpers (per-file-local per this suite's established convention —
    //    see ConcurrencySaveSeamTests.cs). ──────────────────────────────────────────────────────────

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

    private static byte[] BuildSimpleDocx(string text)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());
            body.AppendChild(new Paragraph(new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve })));
            body.AppendChild(new SectionProperties());
            mainPart.Document.Save();
        }

        return stream.ToArray();
    }
}
