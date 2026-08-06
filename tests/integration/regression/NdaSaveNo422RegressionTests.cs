// Regression anchor for the 2026-08 PRODUCTION NDA HTTP 422 (spaarkeai-compose-r6 task 013).
//
// Root cause (anchor-reconciliation class): the AppligentNDA_Signed.docx fixture (7
// mc:AlternateContent pairs, 12 w:txbxContent, 3 duplicate w14:paraId) hit the old surgical save
// path where the ComposeBaselineParaIdStamper COUNT-GATE failed OPEN on the map/body paragraph-count
// mismatch → the client's minted paraIds were never stamped → ZERO anchorable ops → a hard
// ComposePatchException → HTTP 422 in production. The render-on-save pivot (tasks 010/011/012)
// retired that path for model-carrying saves: {contentModel, content} routes through
// ComposeDocumentRenderer.RenderIntoCarrier — no patch engine, no count-gate — so the 422 class is
// unreachable by construction. These tests pin that permanently:
//
//   (1) NdaSave_PostCutoverShape_WithHostileParaIdMap_Returns200_Never422 — the post-cutover shape
//       with a DELIBERATELY count-mismatched paraIdMap (the exact mismatch class that started the
//       422 chain) saves 200. The NDA fixture must never 422 again.
//   (2) NdaSave_MixedContractWithOpLog_IgnoresOpsLoudly_NoEngineExecution — a mixed-contract save
//       (contentModel + operationLog) ignores the ops LOUDLY (wire-visible `op-log-ignored`
//       degradation warning) and the surgical engine path provably does NOT execute: no
//       reanchorSummary, no partialApply (both arise ONLY on the engine path), and the op's text
//       appears NOWHERE in the persisted bytes (ignored, not applied).
//
// MAINTAIN-class (regression-protector; /test-diet KEEP — tests/integration/regression/** KEEP path
// per ADR-038: "every bug = regression test"). Through-the-wire WebApplicationFactory slice via the
// EXISTING ComposeFidelitySeamFixture (CLAUDE.md §11: no new fixture class). NO
// Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test, NO reflection over private
// members.

using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Services.Ai;
using Sprk.Bff.Api.Services.Ai.Chat;
using Sprk.Bff.Api.Services.Compose;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Sprk.Bff.Api.Tests.Seam.Compose;
using Xunit;

namespace Sprk.Bff.Api.Tests.Integration.Regression;

public sealed class NdaSaveNo422RegressionTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private const string NdaFixtureFileName = "AppligentNDA_Signed.docx";

    private readonly ComposeFidelitySeamFixture _fixture;

    public NdaSaveNo422RegressionTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 1. The post-cutover save shape + a HOSTILE (count-mismatched) paraIdMap — the exact count-gate
    //    mismatch class that started the production 422 chain — must save 200. Never 422 again.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NdaSave_PostCutoverShape_WithHostileParaIdMap_Returns200_Never422()
    {
        const string speId = "spe-item-013-nda-no422-hostile-map";
        const string driveId = "drive-013-nda-no422-hostile-map";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var ndaBytes = LoadNdaFixtureBytes();

        // Build the canonical model server-side from the NDA bytes — the same projection the load
        // path hands the client (simpler than a wire load; the model IS the post-cutover payload).
        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(ndaBytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed,
            "the NDA must project into the canonical content model — the render-on-save input");

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, ndaBytes.Length, "\"v1\""));
        _fixture.SpeMock
            .Setup(s => s.ReplaceFileContentAsUserAsync(
                It.IsAny<HttpContext>(), driveId, speId, It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, ndaBytes.Length, "\"v2\""));

        // The DELIBERATELY hostile map: ONE entry against a ~55-paragraph body — the exact
        // count-gate mismatch class that (fail-open, zero stamped ids, zero anchorable ops)
        // produced the production ComposePatchException → 422 on the old surgical path.
        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = ndaBytes,
            contentModel = projection.Model,
            paraIdMap = new object[]
            {
                new { index = 0, paraId = "7B00AA01", text = "junk" },
            },
        });

        var body = await response.Content.ReadAsStringAsync();
        ((int)response.StatusCode).Should().NotBe(422,
            "REGRESSION (2026-08 production NDA 422): the ComposeBaselineParaIdStamper count-gate " +
            "fail-open → zero anchorable ops → hard ComposePatchException chain must be UNREACHABLE " +
            $"on the render-on-save path — a hostile paraIdMap must be irrelevant — body: {body}");
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the NDA post-cutover save must succeed — body: {body}");

        var root = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();
        root["versionId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace(
            "a successful save reports the new SPE version identity");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // 2. Mixed contract (contentModel + operationLog): the ops are ignored LOUDLY — wire-visible
    //    `op-log-ignored` warning — and the surgical engine path provably does not execute.
    // ═══════════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task NdaSave_MixedContractWithOpLog_IgnoresOpsLoudly_NoEngineExecution()
    {
        const string speId = "spe-item-013-nda-no422-mixed-oplog";
        const string driveId = "drive-013-nda-no422-mixed-oplog";
        const string engineMarker = "[ENGINE-WOULD-HAVE-APPLIED-THIS]";
        var tenant = ComposeFidelitySeamFixture.TestTenantId;
        var ndaBytes = LoadNdaFixtureBytes();

        var projection = new ComposeDocxProjectionBuilder().BuildContentModel(ndaBytes);
        projection.Status.Should().NotBe(ComposeProjectionStatus.Failed);

        _fixture.ResetBoundaries();
        ArrangeIdempotentPromotionAndIndexing();

        using var client = _fixture.CreateAuthenticatedClient();
        var sessionId = await CreateSessionAsync(tenant, speId);

        _fixture.SpeMock
            .Setup(s => s.GetFileMetadataAsUserAsync(It.IsAny<HttpContext>(), driveId, speId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildFileHandle(speId, driveId, ndaBytes.Length, "\"v1\""));

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
            .ReturnsAsync(BuildFileHandle(speId, driveId, ndaBytes.Length, "\"v2\""));

        // The op targets a REAL NDA paraId (2BBF07C9 physically exists in the fixture) — if the
        // engine path DID execute, this insert would resolve and land. That makes the
        // "marker appears nowhere in the persisted bytes" assertion behavioral, not vacuous.
        var response = await client.PostAsJsonAsync($"/api/compose/documents/{speId}/save", new
        {
            sessionId,
            tenantId = tenant,
            driveId,
            content = ndaBytes,
            contentModel = projection.Model,
            operationLog = new
            {
                schemaVersion = "compose-ops-v2",
                operations = new object[]
                {
                    new { type = "insertText", paraId = "2BBF07C9", at = new { runIndex = 0, offset = 0 }, text = engineMarker },
                },
            },
        });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"a mixed-contract save is success-with-warnings, never a failure — body: {body}");

        var root = System.Text.Json.Nodes.JsonNode.Parse(body)!.AsObject();

        var degradationWarnings = root["degradationWarnings"]?.AsArray();
        degradationWarnings.Should().NotBeNull(
            "the ignored op-log must be surfaced on the wire, not just logged server-side");
        degradationWarnings!
            .Any(w => string.Equals(w!["code"]?.GetValue<string>(), "op-log-ignored", StringComparison.Ordinal))
            .Should().BeTrue(
                "the render-on-save path ignores a mixed-contract op-log LOUDLY via the " +
                $"`op-log-ignored` degradation warning — warnings: {degradationWarnings!.ToJsonString()}");

        // Behavioral proof the SURGICAL path did not execute: reanchorSummary and partialApply arise
        // ONLY on the engine path (stale-base re-anchor / best-effort per-paragraph recovery).
        (root["reanchorSummary"] is null).Should().BeTrue(
            "reanchorSummary arises only on the engine path's stale-base re-anchor — it must be null " +
            "on a render-on-save");
        (root["partialApply"] is null).Should().BeTrue(
            "partialApply arises only on the engine path's best-effort recovery — it must be null " +
            "on a render-on-save");

        // The op was IGNORED, not applied: its text appears NOWHERE in the persisted package.
        persisted.Should().NotBeNull("the SPE facade must have captured the rendered bytes");
        AssertMarkerAbsentFromPackage(persisted!, engineMarker);

        root["versionId"]!.GetValue<string>().Should().NotBeNullOrWhiteSpace();
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════════
    // Shared arrange + helpers (mirrors the ConcurrencySaveSeamTests per-file-local-helper convention).
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

    private static byte[] LoadNdaFixtureBytes()
    {
        var path = ComposeCorpusFixtureLocator.EnumerateDocumentPaths()
            .Single(p => string.Equals(Path.GetFileName(p), NdaFixtureFileName, StringComparison.OrdinalIgnoreCase));
        return ComposeCorpusFixtureLocator.LoadVerifiedBytes(path);
    }

    /// <summary>Asserts the marker string appears in NO part of the persisted OPC package — the
    /// honest "nowhere" check (body, comments part, headers/footers, anything): every zip entry's
    /// decoded content is scanned. Deflate compression means a raw byte scan of the package would be
    /// vacuous; scanning the DECOMPRESSED entries is the real assertion.</summary>
    private static void AssertMarkerAbsentFromPackage(byte[] packageBytes, string marker)
    {
        using var archive = new ZipArchive(new MemoryStream(packageBytes, writable: false), ZipArchiveMode.Read);
        foreach (var entry in archive.Entries)
        {
            using var reader = new StreamReader(entry.Open());
            var content = reader.ReadToEnd();
            content.Should().NotContain(marker,
                $"the ignored op's text must NOT be applied anywhere in the package (part '{entry.FullName}')");
        }
    }

    private static FileHandleDto BuildFileHandle(string speId, string driveId, int size, string eTag) =>
        new(Id: speId, Name: NdaFixtureFileName, ParentId: null, Size: size,
            CreatedDateTime: DateTimeOffset.UtcNow, LastModifiedDateTime: DateTimeOffset.UtcNow,
            ETag: eTag, IsFolder: false, WebUrl: null, DriveId: driveId);
}
