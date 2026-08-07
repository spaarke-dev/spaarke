// Task 050 (spaarkeai-compose-r6, Phase 5, spec FR-07 / Success Criterion 4) — through-the-wire
// seam slice for the NEW user-context (OBO) document version-history endpoint pair:
//
//   GET /api/obo/drives/{driveId}/items/{itemId}/versions                       (list, metadata)
//   GET /api/obo/drives/{driveId}/items/{itemId}/versions/{versionId}/content   (open prior, bytes)
//
// WHAT THIS PROVES (production code — the REAL DocumentVersionEndpoints routes mapped by
// EndpointMappingExtensions.MapDomainEndpoints, through the REAL auth + rate-limit pipeline; ONLY
// the ISpeFileOperations Graph/SPE boundary is doubled — ADR-038 "mock at module boundaries", the
// SAME ComposeFidelitySeamFixture the Compose seam suite established, no new fixture class per
// CLAUDE.md §11):
//
//   (1) LIST — the endpoint returns the version-metadata projection (id/label, lastModified
//       timestamp, size — VersionInfoDto) resolved through the USER-CONTEXT facade method
//       (`ListFileVersionsAsUserAsync`, which runs on the caller's OBO token underneath), with the
//       AUTHENTICATED HttpContext threaded through, and NEVER touches any app-only facade method.
//   (2) OPEN PRIOR — with v3 and v4 both existing, opening v3 streams v3's EXACT bytes (not v4's),
//       via the existing OBO primitive `DownloadFileVersionAsUserAsync` (task 002 inventory row 4)
//       — and an unknown versionId yields 404, not someone else's bytes.
//   (3) NEGATIVE AUTHORIZATION — a caller whose OBO token is NOT authorized for the document (the
//       fake facade throws UnauthorizedAccessException, exactly how DriveItemOperations translates
//       Graph's 403 under the user's own token — the SPE layer IS the authorization boundary, not a
//       post-hoc filter) gets 403 from BOTH routes and NEVER the bytes/metadata. An unauthenticated
//       caller gets 401 (RequireAuthorization, ADR-008).
//   (4) NEGATIVE SCOPE — the surface is READ-ONLY: a full list+open round trip invokes NO
//       write-shaped facade method (no ReplaceFileContentAsUserAsync, no UploadSmall*), and no
//       restore/branch route exists (POST .../restoreVersion → 404).
//
// AUTH-PATH NOTE: these routes are deliberately DISTINCT from the app-only/config-scoped admin
// version list (`ContainerItemEndpoints.cs:48`) — ADR-028: the OBO token enforces the per-document
// boundary at the SPE layer. The mock models that boundary the same way existing Compose seam tests
// model SPE authorization (facade-translated UnauthorizedAccessException).
//
// ADR-038 seam DoD compliance: through-the-wire WebApplicationFactory slice only. NO
// Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test anywhere in this file. The
// mock lives ONLY at the ISpeFileOperations module boundary.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class SpeVersionHistoryOboSeamTests : IClassFixture<ComposeFidelitySeamFixture>
{
    private readonly ComposeFidelitySeamFixture _fixture;

    public SpeVersionHistoryOboSeamTests(ComposeFidelitySeamFixture fixture) => _fixture = fixture;

    private const string DriveId = "drive-050-version-history";
    private const string ItemId = "spe-item-050-version-history";

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (1) LIST — version metadata through the user-context facade, app-only path untouched.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ListVersions_ReturnsVersionMetadata_ResolvedThroughTheUserContextFacade_NeverAppOnly()
    {
        _fixture.ResetBoundaries();

        var v3Modified = new DateTimeOffset(2026, 8, 1, 10, 0, 0, TimeSpan.Zero);
        var v4Modified = new DateTimeOffset(2026, 8, 2, 15, 30, 0, TimeSpan.Zero);
        var metadata = new List<VersionInfoDto>
        {
            new(Id: "4.0", ETag: null, LastModifiedDateTime: v4Modified, Size: 2048),
            new(Id: "3.0", ETag: null, LastModifiedDateTime: v3Modified, Size: 1024),
        };

        // Capture the auth state INSIDE the request (the HttpContext is disposed once the
        // response completes, so it cannot be inspected afterwards).
        bool? facadeReceivedAuthenticatedUser = null;
        _fixture.SpeMock
            .Setup(s => s.ListFileVersionsAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()))
            .Callback<HttpContext, string, string, CancellationToken>((ctx, _, _, _) =>
                facadeReceivedAuthenticatedUser = ctx?.User?.Identity?.IsAuthenticated)
            .ReturnsAsync(metadata);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{ItemId}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the OBO list-versions route must serve the metadata — body: {await response.Content.ReadAsStringAsync()}");

        var deserialized = await response.Content.ReadFromJsonAsync<List<VersionInfoDto>>();
        deserialized.Should().NotBeNull();
        var returned = deserialized!;
        returned.Should().HaveCount(2);
        returned[0].Id.Should().Be("4.0", "the list is newest-first");
        returned[0].Size.Should().Be(2048);
        returned[0].LastModifiedDateTime.Should().Be(v4Modified);
        returned[1].Id.Should().Be("3.0");
        returned[1].Size.Should().Be(1024);
        returned[1].LastModifiedDateTime.Should().Be(v3Modified);

        // The USER-CONTEXT facade method carried the request's AUTHENTICATED HttpContext — the
        // handle the OBO token exchange (IGraphClientFactory.ForUserAsync) runs on underneath.
        _fixture.SpeMock.Verify(
            s => s.ListFileVersionsAsUserAsync(It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()),
            Times.Once,
            "the endpoint must resolve versions through the As-User (OBO) facade method");
        facadeReceivedAuthenticatedUser.Should().BeTrue(
            "the OBO facade must receive the calling user's authenticated context — that context IS the token source");

        // NO app-only elevation anywhere in the new path (task 050 hard rule / ADR-028).
        _fixture.SpeMock.Verify(
            s => s.GetFileMetadataAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the OBO version surface must never fall back to an app-only metadata read");
        _fixture.SpeMock.Verify(
            s => s.DownloadFileAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the OBO version surface must never fall back to an app-only download");
    }

    [Fact]
    public async Task ListVersions_ItemNotFound_Returns404()
    {
        _fixture.ResetBoundaries();

        _fixture.SpeMock
            .Setup(s => s.ListFileVersionsAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, "missing-item", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<VersionInfoDto>?)null);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync($"/api/obo/drives/{DriveId}/items/missing-item/versions");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a facade null (item not found under the user's token) surfaces as 404, never as data");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (2) OPEN PRIOR — v3's EXACT bytes after v4 exists, read-only; nothing mutated.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task OpenPriorVersion_AfterLaterVersionExists_ReturnsThatVersionsExactBytes_AndMutatesNothing()
    {
        _fixture.ResetBoundaries();

        // Fake SPE version store modeling the Graph contract task 002 verified (append-only:
        // every version's bytes stay addressable by id, never overwritten in place — same model
        // as SpeSaveVersioningSeamTests in this directory).
        var v3Bytes = Encoding.UTF8.GetBytes("VERSION-3 exact bytes — the render-on-save safety net payload (v3).");
        var v4Bytes = Encoding.UTF8.GetBytes("VERSION-4 exact bytes — the CURRENT version after a later save (v4).");
        var versionsById = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            ["3.0"] = v3Bytes,
            ["4.0"] = v4Bytes,
        };

        _fixture.SpeMock
            .Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<HttpContext, string, string, string, CancellationToken>((_, _, _, versionId, _) =>
                Task.FromResult<Stream?>(versionsById.TryGetValue(versionId, out var bytes) ? new MemoryStream(bytes) : null));

        using var client = _fixture.CreateAuthenticatedClient();

        // Open v3 — the PRIOR version — while v4 (current) exists.
        var v3Response = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{ItemId}/versions/3.0/content");
        v3Response.StatusCode.Should().Be(HttpStatusCode.OK);
        var v3Returned = await v3Response.Content.ReadAsByteArrayAsync();
        v3Returned.Should().Equal(v3Bytes,
            "opening a prior version must stream that version's EXACT bytes (FR-07 / Success Criterion 4)");
        v3Returned.Should().NotEqual(v4Bytes, "v3's response must NOT be the current (v4) version's bytes");

        // Open v4 too — proves the route addresses by versionId, not a hardcoded/latest fetch.
        var v4Response = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{ItemId}/versions/4.0/content");
        v4Response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await v4Response.Content.ReadAsByteArrayAsync()).Should().Equal(v4Bytes);

        // Unknown version → 404, never bytes.
        var missingResponse = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{ItemId}/versions/9.0/content");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // It reused the EXISTING OBO primitive (task 002 inventory row 4) — twice for real versions,
        // once for the miss.
        _fixture.SpeMock.Verify(
            s => s.DownloadFileVersionAsUserAsync(It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(3));

        // ── NEGATIVE SCOPE: the whole round trip is READ-ONLY — no write-shaped facade call. ─────
        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "the version-history surface must never write content");
        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never, "the version-history surface must never write content (etag overload)");
        _fixture.SpeMock.Verify(
            s => s.UploadSmallAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "the version-history surface must never create drive-items");
        _fixture.SpeMock.Verify(
            s => s.UploadSmallAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never, "the version-history surface must never create drive-items (app-only)");
    }

    [Fact]
    public async Task NoRestoreOrBranchRouteExists_RestoreShapedRequests_AreNotMapped()
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateAuthenticatedClient();

        // Graph's restore shape — deliberately NOT implemented (spec Out of Scope: restore/branch-from
        // are a fast-follow; task 050 maps a read-only pair ONLY).
        var restore = await client.PostAsync(
            $"/api/obo/drives/{DriveId}/items/{ItemId}/versions/3.0/restoreVersion", content: null);
        restore.StatusCode.Should().Be(HttpStatusCode.NotFound, "no restore route may exist on this surface");

        // Nor any write verb on the mapped version routes themselves.
        var postVersions = await client.PostAsync(
            $"/api/obo/drives/{DriveId}/items/{ItemId}/versions", content: null);
        postVersions.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            "the versions route is GET-only — no write verb is mapped");

        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (3) NEGATIVE AUTHORIZATION — 403/404 and NEVER the bytes, enforced at the OBO/SPE boundary.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task CallerNotAuthorizedForDocument_ListAndOpen_Return403_AndNeverTheBytes()
    {
        _fixture.ResetBoundaries();

        const string forbiddenItemId = "spe-item-050-someone-elses-document";
        var secretBytes = Encoding.UTF8.GetBytes("SECRET-DOCUMENT-CONTENT-another-users-version-bytes");

        // The SPE layer denies the caller's OWN OBO token on this item: the facade translates
        // Graph's 403 to UnauthorizedAccessException (DriveItemOperations' documented contract) —
        // the authorization decision happens AT the SPE boundary under the user's token, not in a
        // post-hoc BFF filter. The secret bytes exist in the store but are unreachable.
        _fixture.SpeMock
            .Setup(s => s.ListFileVersionsAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, forbiddenItemId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException($"Access denied to file {forbiddenItemId}"));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, forbiddenItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException($"Access denied to file {forbiddenItemId}"));

        using var client = _fixture.CreateAuthenticatedClient();

        var listResponse = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{forbiddenItemId}/versions");
        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a user must not be able to LIST versions of a document they cannot read");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().NotContain("3.0", "no version metadata may leak on a denial");

        var openResponse = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{forbiddenItemId}/versions/3.0/content");
        openResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "a user must not be able to OPEN a version of a document they cannot read");
        var openBody = await openResponse.Content.ReadAsByteArrayAsync();
        openBody.Should().NotEqual(secretBytes);
        Encoding.UTF8.GetString(openBody).Should().NotContain("SECRET-DOCUMENT-CONTENT",
            "the denied response must NEVER carry the document bytes");
    }

    [Fact]
    public async Task UnauthenticatedCaller_BothRoutes_Return401_AndNeverReachTheSpeFacade()
    {
        _fixture.ResetBoundaries();

        // No Authorization header → the auth handler fails → RequireAuthorization (ADR-008) challenges.
        using var client = _fixture.CreateClient();

        var listResponse = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{ItemId}/versions");
        listResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var openResponse = await client.GetAsync($"/api/obo/drives/{DriveId}/items/{ItemId}/versions/3.0/content");
        openResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _fixture.SpeMock.Verify(
            s => s.ListFileVersionsAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unauthenticated request must be rejected BEFORE any SPE call");
        _fixture.SpeMock.Verify(
            s => s.DownloadFileVersionAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unauthenticated request must be rejected BEFORE any SPE call");
    }
}
