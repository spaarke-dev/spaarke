// Task 050 (spaarkeai-compose-r6, Phase 5, spec FR-07 / Success Criterion 4) — through-the-wire
// seam slice for the user-context (OBO) document version-history endpoint pair:
//
//   GET /api/documents/{documentId}/versions                       (list, metadata)
//   GET /api/documents/{documentId}/versions/{versionId}/content   (open prior, bytes)
//
// ══════════════════════════════════════════════════════════════════════════════════════════════════
// MIGRATED by unified-access-control-r2 task 079 — AND ITS PREMISE CORRECTED
// ══════════════════════════════════════════════════════════════════════════════════════════════════
//
// This file used to exercise the DRIVE-keyed pair
// (`GET /api/obo/drives/{driveId}/items/{itemId}/versions[...]`) and its header asserted:
//
//     "the OBO token enforces the per-document boundary at the SPE layer ... the SPE layer IS the
//      authorization boundary, not a post-hoc filter"
//
// That claim was FALSE, and it is the precise reason the routes were a bypass. SPE permission is
// CONTAINER-scoped: it is coarser than per-document Dataverse rights, so a caller holding a container
// ACL passed the "SPE boundary" for every document in that container — including a secure matter's,
// and including its PRIOR-VERSION BYTES. Task 079 re-keyed both routes onto the sprk_document row and
// put a real per-document gate (`AddDocumentAuthorizationFilter("read")`) in front of them.
//
// The authorization matrix — denial for a caller without Read, no bytes on denial, fail-closed on an
// errored check — now lives in `tests/integration/auth/UnifiedAccessControl/
// DocumentVersionAuthorizationTests.cs`, which owns that concern with a fixture that can STATE the
// caller's rights. This file keeps what it uniquely proves and no longer duplicates it: the caller
// here is deliberately AUTHORIZED, so the assertions below are about the FACADE and ADDRESSING
// contract rather than about access.
//
// WHAT THIS PROVES (production code — the REAL DocumentVersionEndpoints routes mapped by
// EndpointMappingExtensions.MapDomainEndpoints, through the REAL auth + rate-limit pipeline; only the
// ISpeFileOperations / IDocumentDataverseService / IAccessDataSource boundaries are doubled —
// ADR-038 "mock at module boundaries", extending the SAME ComposeFidelitySeamFixture rather than
// forking a host, per CLAUDE.md §11):
//
//   (1) LIST — the endpoint returns the version-metadata projection (id/label, lastModified
//       timestamp, size — VersionInfoDto) resolved through the USER-CONTEXT facade method
//       (`ListFileVersionsAsUserAsync`, which runs on the caller's OBO token underneath), with the
//       AUTHENTICATED HttpContext threaded through, and NEVER touches any app-only facade method.
//   (2) SERVER-DERIVED POINTER (task 079) — the drive/item handed to SPE come off the AUTHORIZED
//       document ROW, not from the URL. The caller names a document; it cannot name an SPE item.
//   (3) OPEN PRIOR — with v3 and v4 both existing, opening v3 streams v3's EXACT bytes (not v4's),
//       via the existing OBO primitive `DownloadFileVersionAsUserAsync` (task 002 inventory row 4)
//       — and an unknown versionId yields 404, not someone else's bytes.
//   (4) NEGATIVE SCOPE — the surface is READ-ONLY: a full list+open round trip invokes NO
//       write-shaped facade method (no ReplaceFileContentAsUserAsync, no UploadSmall*), and no
//       restore/branch route exists (POST .../restoreVersion → 404).
//   (5) SPE DENIAL IS STILL HONOURED, as defence in depth BEHIND the gate — a facade
//       UnauthorizedAccessException still yields 403 and never the bytes.
//   (6) The DELETED drive-keyed pair is not routed.
//
// ADR-038 seam DoD compliance: through-the-wire WebApplicationFactory slice only. NO
// Mock<HttpMessageHandler>, NO DI-registration test, NO ctor-null test anywhere in this file. The
// mocks live ONLY at module boundaries.

using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Core.Auth;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Models;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class SpeVersionHistoryOboSeamTests : IClassFixture<DocumentVersionSeamFixture>
{
    private readonly DocumentVersionSeamFixture _fixture;

    public SpeVersionHistoryOboSeamTests(DocumentVersionSeamFixture fixture) => _fixture = fixture;

    // The SPE pointers the fixture's document row resolves to. NOTE the "b!" prefix: SPE drive ids
    // must carry it or the route's pointer validation rejects the row with 400 before any Graph call
    // — a fixture returning "drive-…" would make every authorized test fail for an unrelated reason.
    private const string DriveId = DocumentVersionSeamFixture.DriveId;
    private const string ItemId = DocumentVersionSeamFixture.ItemId;

    private const string DocumentId = DocumentVersionSeamFixture.DocumentId;

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (1) + (2) LIST — metadata through the user-context facade, with a SERVER-DERIVED pointer,
    //                  app-only path untouched.
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
        var response = await client.GetAsync($"/api/documents/{DocumentId}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"the gated list-versions route must serve the metadata to an authorized caller — body: {await response.Content.ReadAsStringAsync()}");

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
        //
        // task 079: the (DriveId, ItemId) matched here came off the AUTHORIZED DOCUMENT ROW. The
        // request URL contained only a document id, so this Verify is simultaneously the proof that
        // the SPE pointer is server-derived and no longer caller-supplied.
        _fixture.SpeMock.Verify(
            s => s.ListFileVersionsAsUserAsync(It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()),
            Times.Once,
            "the endpoint must resolve versions through the As-User (OBO) facade method, using the pointer from the authorized row");
        facadeReceivedAuthenticatedUser.Should().BeTrue(
            "the OBO facade must receive the calling user's authenticated context — that context IS the token source");

        // NO app-only elevation anywhere in the path (task 050 hard rule / ADR-028).
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
                It.IsAny<HttpContext>(), DriveId, DocumentVersionSeamFixture.MissingItemId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<VersionInfoDto>?)null);

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.GetAsync(
            $"/api/documents/{DocumentVersionSeamFixture.MissingItemDocumentId}/versions");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a facade null (item not found under the user's token) surfaces as 404, never as data");

        // Not vacuous: prove the 404 came from the FACADE returning null, not from the route being
        // absent. Before task 079 migrated this file, this test passed for exactly the wrong reason —
        // the drive-keyed route it called had been deleted, so "404" meant "not routed".
        _fixture.SpeMock.Verify(
            s => s.ListFileVersionsAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, DocumentVersionSeamFixture.MissingItemId, It.IsAny<CancellationToken>()),
            Times.Once,
            "the request must have REACHED the facade — otherwise this 404 proves nothing about the handler");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (3) OPEN PRIOR — v3's EXACT bytes after v4 exists, read-only; nothing mutated.
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
        var v3Response = await client.GetAsync($"/api/documents/{DocumentId}/versions/3.0/content");
        v3Response.StatusCode.Should().Be(HttpStatusCode.OK,
            $"body: {await v3Response.Content.ReadAsStringAsync()}");
        var v3Returned = await v3Response.Content.ReadAsByteArrayAsync();
        v3Returned.Should().Equal(v3Bytes,
            "opening a prior version must stream that version's EXACT bytes (FR-07 / Success Criterion 4)");
        v3Returned.Should().NotEqual(v4Bytes, "v3's response must NOT be the current (v4) version's bytes");

        // Open v4 too — proves the route addresses by versionId, not a hardcoded/latest fetch.
        var v4Response = await client.GetAsync($"/api/documents/{DocumentId}/versions/4.0/content");
        v4Response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await v4Response.Content.ReadAsByteArrayAsync()).Should().Equal(v4Bytes);

        // Unknown version → 404, never bytes.
        var missingResponse = await client.GetAsync($"/api/documents/{DocumentId}/versions/9.0/content");
        missingResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // It reused the EXISTING OBO primitive (task 002 inventory row 4) — twice for real versions,
        // once for the miss — each time with the pointer from the authorized row.
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
            $"/api/documents/{DocumentId}/versions/3.0/restoreVersion", content: null);
        restore.StatusCode.Should().Be(HttpStatusCode.NotFound, "no restore route may exist on this surface");

        // Nor any write verb on the mapped version routes themselves.
        var postVersions = await client.PostAsync($"/api/documents/{DocumentId}/versions", content: null);
        postVersions.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed,
            "the versions route is GET-only — no write verb is mapped");

        _fixture.SpeMock.Verify(
            s => s.ReplaceFileContentAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (5) SPE denial is still honoured — defence in depth BEHIND the per-document gate.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Fact]
    public async Task SpeRefusesTheCallersOwnToken_ListAndOpen_Return403_AndNeverTheBytes()
    {
        _fixture.ResetBoundaries();

        var secretBytes = Encoding.UTF8.GetBytes("SECRET-DOCUMENT-CONTENT-another-users-version-bytes");

        // The caller IS authorized at the Dataverse gate here (this fixture states rights), and SPE
        // then refuses their own OBO token anyway — the facade translates Graph's 403 to
        // UnauthorizedAccessException (DriveItemOperations' documented contract). Task 079 demoted
        // this from "the authorization boundary" to defence in depth, but it must still be honoured:
        // a 403 from SPE may never be rendered as data.
        _fixture.SpeMock
            .Setup(s => s.ListFileVersionsAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException($"Access denied to file {ItemId}"));
        _fixture.SpeMock
            .Setup(s => s.DownloadFileVersionAsUserAsync(
                It.IsAny<HttpContext>(), DriveId, ItemId, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new UnauthorizedAccessException($"Access denied to file {ItemId}"));

        using var client = _fixture.CreateAuthenticatedClient();

        var listResponse = await client.GetAsync($"/api/documents/{DocumentId}/versions");
        listResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an SPE refusal must surface as 403, never as version metadata");
        var listBody = await listResponse.Content.ReadAsStringAsync();
        listBody.Should().NotContain("3.0", "no version metadata may leak on a denial");

        var openResponse = await client.GetAsync($"/api/documents/{DocumentId}/versions/3.0/content");
        openResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "an SPE refusal must surface as 403, never as bytes");
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

        var listResponse = await client.GetAsync($"/api/documents/{DocumentId}/versions");
        listResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var openResponse = await client.GetAsync($"/api/documents/{DocumentId}/versions/3.0/content");
        openResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        _fixture.SpeMock.Verify(
            s => s.ListFileVersionsAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unauthenticated request must be rejected BEFORE any SPE call");
        _fixture.SpeMock.Verify(
            s => s.DownloadFileVersionAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "an unauthenticated request must be rejected BEFORE any SPE call");
    }

    // ═══════════════════════════════════════════════════════════════════════════════════════════
    // (6) The DELETED drive-keyed pair is not routed.
    // ═══════════════════════════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("/api/obo/drives/b!d/items/spe-item-000000000000/versions")]
    [InlineData("/api/obo/drives/b!d/items/spe-item-000000000000/versions/3.0/content")]
    public async Task RetiredDriveKeyedVersionRoute_WhenRequested_Returns404NotRouted(string route)
    {
        _fixture.ResetBoundaries();
        using var client = _fixture.CreateAuthenticatedClient();

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound,
            "task 079 DELETED the drive-keyed pair; a caller-supplied (driveId, itemId) must not "
            + "address SPE version content at all");
        _fixture.SpeMock.Verify(
            s => s.ListFileVersionsAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _fixture.SpeMock.Verify(
            s => s.DownloadFileVersionAsUserAsync(It.IsAny<HttpContext>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// unified-access-control-r2 task 079: the Compose seam host plus the two doubles the now-GATED
/// version routes need — a stated-rights <see cref="IAccessDataSource"/> and an
/// <see cref="IDocumentDataverseService"/> that answers with SPE pointers.
/// </summary>
/// <remarks>
/// <para>
/// Extends <see cref="ComposeFidelitySeamFixture"/> (unsealed by this task) rather than forking a
/// second Compose seam host — CLAUDE.md §11. Every other Compose seam test keeps using the base type
/// unchanged, so these two extra registrations have zero blast radius on that suite.
/// </para>
/// <para>
/// The caller this fixture creates is deliberately AUTHORIZED. Denial behaviour is not this file's
/// concern — it belongs to <c>DocumentVersionAuthorizationTests</c>, whose fixture varies the
/// caller's rights. Splitting it this way keeps each file's failure message meaningful: a red test
/// here means the facade/addressing contract broke, not that access control changed.
/// </para>
/// </remarks>
public sealed class DocumentVersionSeamFixture : ComposeFidelitySeamFixture
{
    /// <summary>
    /// The "b!" prefix is REQUIRED: the route validates SPE drive-id format and rejects anything else
    /// with 400 before touching Graph, so a "drive-…" fixture value would fail every authorized test
    /// for a reason unrelated to what is being tested.
    /// </summary>
    public const string DriveId = "b!drive-050-version-history";
    public const string ItemId = "spe-item-050-version-history";

    public const string DocumentId = "aaaa1111-2222-3333-4444-555555555555";

    /// <summary>A document whose SPE item the facade reports as not found.</summary>
    public const string MissingItemDocumentId = "bbbb1111-2222-3333-4444-555555555555";
    public const string MissingItemId = "spe-item-050-missing-item";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IAccessDataSource>();
            services.AddScoped<IAccessDataSource, AuthorizedReaderAccessDataSource>();

            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton<IDocumentDataverseService>(new VersionSeamDocumentDataverseService());
        });
    }

    /// <summary>
    /// Grants Read (only) on every resource. Read is exactly what the version routes require, so this
    /// models "an authorized reader" without over-granting — a double handing out full rights would
    /// hide a route that had accidentally been gated on Write.
    /// </summary>
    private sealed class AuthorizedReaderAccessDataSource : IAccessDataSource
    {
        public Task<AccessSnapshot> GetUserAccessAsync(
            string userId, string resourceId, string? userAccessToken = null, CancellationToken ct = default)
            => Task.FromResult(Read(userId, resourceId));

        /// <summary>The entity-agnostic path (task 070) — same answer, so the two cannot disagree.</summary>
        public Task<AccessSnapshot> GetRecordAccessAsync(
            string userId, string entitySetName, Guid recordId, string? userAccessToken = null, CancellationToken ct = default)
            => Task.FromResult(Read(userId, recordId.ToString()));

        private static AccessSnapshot Read(string userId, string resourceId) => new()
        {
            UserId = userId,
            ResourceId = resourceId,
            AccessRights = AccessRights.Read
        };
    }

    /// <summary>
    /// Maps the two seam document ids to the SPE pointers the <c>SpeMock</c> setups are keyed on, so
    /// migrating this file to document-keyed routes did not require rewriting every Moq setup.
    /// </summary>
    private sealed class VersionSeamDocumentDataverseService : IDocumentDataverseService
    {
        private static readonly Dictionary<string, string> ItemIdByDocument =
            new(StringComparer.OrdinalIgnoreCase)
            {
                [DocumentId] = ItemId,
                [MissingItemDocumentId] = MissingItemId,
            };

        public Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default)
        {
            if (!ItemIdByDocument.TryGetValue(id, out var itemId))
            {
                // Unmodelled document → not found, which the route renders as 404. Deliberately not
                // a fabricated row: a test using an id this fixture does not model should see that.
                return Task.FromResult<DocumentEntity?>(null);
            }

            return Task.FromResult<DocumentEntity?>(new DocumentEntity
            {
                Id = id,
                Name = "Version Seam Document",
                FileName = $"{id}.docx",
                ContainerId = Guid.NewGuid().ToString(),
                GraphDriveId = DriveId,
                GraphItemId = itemId,
                HasFile = true
            });
        }

        // Everything else throws rather than returning a default, so a future test that strays onto
        // an unmodelled path fails loudly instead of asserting against a fabricated answer.
        public Task DeleteDocumentAsync(string id, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task UpdateDocumentFieldsAsync(string documentId, Dictionary<string, object?> fields, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<DocumentEntity?> GetDocumentByEmailLookupAsync(Guid emailId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<DocumentEntity?> GetEmailArchiveByCommunicationAsync(Guid communicationId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByParentAsync(Guid parentDocumentId, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByMatterAsync(Guid matterId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByProjectAsync(Guid projectId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByInvoiceAsync(Guid invoiceId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByWorkAssignmentAsync(Guid workAssignmentId, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByConversationIndexAsync(string conversationIndexPrefix, Guid? excludeDocumentId = null, CancellationToken ct = default) =>
            throw new NotSupportedException(NotModelled);

        private const string NotModelled =
            "DocumentVersionSeamFixture models only the version routes' document read. Model a new "
            + "member deliberately rather than returning an empty default.";
    }
}
