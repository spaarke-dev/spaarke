using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 079 — the two document version routes in
/// <c>DocumentVersionEndpoints.cs</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>The defect.</b> Both routes used to be keyed by <c>(driveId, itemId)</c> off the URL
/// (<c>GET /api/obo/drives/{driveId}/items/{itemId}/versions[/{versionId}/content]</c>) behind
/// <c>RequireAuthorization()</c> alone. SPE permission is CONTAINER-scoped, so any caller holding a
/// container ACL could read the version history — and the PRIOR-VERSION BYTES — of every document in
/// that container with no per-document check. Task 079 re-keyed both routes onto the
/// <c>sprk_document</c> row and gated them <c>"read"</c>.
/// </para>
/// <para>
/// <b>The load-bearing assertion in every denial test here is
/// <see cref="DocumentVersionTestFixture.VersionListReads"/> /
/// <see cref="DocumentVersionTestFixture.VersionByteReads"/> being empty — not the status code.</b>
/// Task 009's lesson. A 403 rendered after the bytes have already been streamed is not a denial, and
/// the byte route is the one that matters: prior-version content is exactly as disclosing as current
/// content, and often contains material later redacted from the current version.
/// </para>
/// <para>
/// <b>Why the allowed-path tests are not optional.</b> Offline, every access check fails closed, so a
/// denial-only suite passes identically whether the gate works or the feature is simply broken. The
/// authorized cases here are what distinguish "gated" from "bricked" — and the live caller
/// (<c>AllDocuments/src/versionHistory.ts</c>) depends on exactly these two requests succeeding.
/// </para>
/// </remarks>
public class DocumentVersionAuthorizationTests : IClassFixture<DocumentVersionTestFixture>
{
    private readonly DocumentVersionTestFixture _fixture;

    /// <summary>A document id whose SPE pointers the fixture answers for.</summary>
    private const string DocumentId = "11111111-1111-1111-1111-111111111111";

    private static string VersionsRoute => $"/api/documents/{DocumentId}/versions";
    private static string VersionContentRoute => $"/api/documents/{DocumentId}/versions/3.0/content";

    public DocumentVersionAuthorizationTests(DocumentVersionTestFixture fixture)
    {
        _fixture = fixture;
        // One IClassFixture instance per class, so the recorders accumulate across tests. A
        // "read nothing" assertion would otherwise pass or fail on another test's residue.
        _fixture.Reset();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The defect: no per-document authorization on the version-history LIST
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListVersions_WhenCallerHasNoRightsOnTheDocument_IsDeniedAndReadsNothing()
    {
        var client = _fixture.CreateClientWithRights("None");

        var response = await client.GetAsync(VersionsRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.VersionListReads.Should().BeEmpty(
            "version history must not be enumerated for a caller with no Read on the document — the "
            + "list alone discloses the document's edit cadence, size history and timestamps");
    }

    [Fact]
    public async Task ListVersions_WithNoToken_IsRefusedBeforeAnyGraphCall()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync(VersionsRoute);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        _fixture.VersionListReads.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The route that leaks bytes — the easier of the two to overlook
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task OpenPriorVersion_WhenCallerHasNoRightsOnTheDocument_IsDeniedAndServesNoBytes()
    {
        var client = _fixture.CreateClientWithRights("None");

        var response = await client.GetAsync(VersionContentRoute);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.VersionByteReads.Should().BeEmpty(
            "PRIOR-VERSION BYTES must never be fetched for an unauthorized caller. This is the "
            + "assertion that matters: a 403 produced after the stream was opened is not a denial, "
            + "and a prior version of a secure matter's document is the same confidential content as "
            + "the current one");

        // Corroboration: nothing that looks like the file body came back on the wire either.
        var body = await response.Content.ReadAsByteArrayAsync();
        Encoding.UTF8.GetString(body).Should().NotContain(
            DocumentVersionTestFixture.PriorVersionBytesMarker,
            "the response body must not contain the prior version's content under any status code");
    }

    [Fact]
    public async Task OpenPriorVersion_WithNoToken_IsRefusedBeforeAnyGraphCall()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync(VersionContentRoute);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
        _fixture.VersionByteReads.Should().BeEmpty();
    }

    /// <summary>
    /// ADR-003: an access check that THROWS must deny. Reaching that catch block is only possible
    /// through the fixture's designated throwing id.
    /// </summary>
    [Fact]
    public async Task OpenPriorVersion_WhenTheAccessCheckThrows_DeniesAndServesNoBytes()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync(
            $"/api/documents/{DocumentDestroyAuthorizationTestFixture.ThrowingDocumentId}/versions/3.0/content");

        response.StatusCode.Should().NotBe(HttpStatusCode.OK);
        _fixture.VersionByteReads.Should().BeEmpty(
            "an errored authorization decision must fail closed, not fall through to the bytes");
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The live caller still works — otherwise the gate is indistinguishable from
    // having broken version history (AllDocuments/src/versionHistory.ts)
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListVersions_WhenCallerHoldsRead_ReturnsTheHistoryNewestFirst()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync(VersionsRoute);

        // Body in the failure message: on the ALLOWED path every handler-side fault (a DI mistake, a
        // pointer validation, a missing registration) presents identically as a non-200.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "body was: {0}", await response.Content.ReadAsStringAsync());

        var versions = await response.Content.ReadFromJsonAsync<List<VersionInfoDto>>();
        versions.Should().NotBeNull();
        versions!.Select(v => v.Id).Should().Equal("4.0", "3.0", "2.0");
        _fixture.VersionListReads.Should().HaveCount(1);
    }

    [Fact]
    public async Task OpenPriorVersion_WhenCallerHoldsRead_StreamsTheExactPriorVersionBytes()
    {
        var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync(VersionContentRoute);

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "body was: {0}", await response.Content.ReadAsStringAsync());

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(DocumentVersionTestFixture.PriorVersionBytesMarker,
            "the authorized caller must receive the EXACT bytes of the named prior version — this is "
            + "the positive control proving the denial tests above mean 'refused', not 'fixture "
            + "returns nothing to anybody'");

        // The version id from the ROUTE reached the byte source; the drive/item did not come from
        // the caller at all — they were read off the authorized document row.
        _fixture.VersionByteReads.Should().ContainSingle()
            .Which.Should().Be(($"b!drive-{DocumentId}", $"item-{DocumentId}", "3.0"));
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // The deleted drive-keyed pair must not come back
    // ─────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("/api/obo/drives/b!some-drive/items/some-item-000000000/versions")]
    [InlineData("/api/obo/drives/b!some-drive/items/some-item-000000000/versions/3.0/content")]
    public async Task RetiredDriveKeyedVersionRoute_WhenRequested_Returns404NotRouted(string route)
    {
        // Task 079 DELETED both drive-keyed routes rather than gating them in place: the filter
        // authorizes an sprk_document ROW, and a driveId is not a document GUID, so a gate bolted
        // onto that shape denies every caller including the legitimate one. Re-adding the route —
        // even "temporarily", even gated — reopens the standing invitation to grant container
        // access, which is the question broker-only exists to foreclose.
        var client = _fixture.CreateClientWithRights("ReadAccess,WriteAccess,DeleteAccess");

        var response = await client.GetAsync(route);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _fixture.VersionListReads.Should().BeEmpty();
        _fixture.VersionByteReads.Should().BeEmpty();
    }

    /// <summary>
    /// Positive control for the two 404s above: proves they mean "route absent", not "this fixture
    /// 404s everything". A route that DOES exist on the same host answers differently.
    /// </summary>
    [Fact]
    public async Task SurvivingVersionRoute_WithoutBearer_Returns401NotFound()
    {
        var client = _fixture.CreateClient();

        var response = await client.GetAsync(VersionsRoute);

        response.StatusCode.Should().NotBe(HttpStatusCode.NotFound,
            "if this were 404 the route-absence assertions above would be vacuous");
    }
}

/// <summary>
/// Records every version-history read that reached the SPE facade, and with which pointers.
/// </summary>
/// <remarks>
/// <para>
/// Extends <see cref="DocumentDestroyAuthorizationTestFixture"/> rather than forking it — that
/// fixture is explicitly "not sealed" for this purpose and already supplies token-stated rights via
/// <c>IAccessDataSource</c> plus the tenant-bearing auth scheme.
/// </para>
/// <para>
/// The byte source substituted here is <c>ISpeFileOperations</c>, NOT <c>SpeFileStore</c>: the two
/// version methods on the concrete facade are not <c>virtual</c>, so they cannot be overridden, and
/// the interface is what the endpoints inject. Substituting the interface is also the more honest
/// seam — it is the actual boundary the handler calls through.
/// </para>
/// </remarks>
public class DocumentVersionTestFixture : DocumentDestroyAuthorizationTestFixture
{
    /// <summary>Sentinel content for the prior version, so a leak is identifiable in a response body.</summary>
    public const string PriorVersionBytesMarker = "PRIOR-VERSION-CONFIDENTIAL-BYTES";

    /// <summary>Every (driveId, itemId) whose version LIST reached the SPE facade.</summary>
    public ConcurrentBag<(string DriveId, string ItemId)> VersionListReads { get; } = new();

    /// <summary>Every (driveId, itemId, versionId) whose BYTES were fetched from the SPE facade.</summary>
    public ConcurrentBag<(string DriveId, string ItemId, string VersionId)> VersionByteReads { get; } = new();

    public new void Reset()
    {
        base.Reset();
        VersionListReads.Clear();
        VersionByteReads.Clear();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            // SCOPED, and that is load-bearing. SpeFileStore's constructor dependencies are all
            // registered Scoped in DocumentsModule, so a Singleton factory receives the ROOT
            // provider and GetRequiredService throws "Cannot resolve scoped service from root
            // provider" — which surfaces as a 500 on the AUTHORIZED path ONLY, because endpoint
            // filters reject before handler parameters are resolved. A green denial suite beside a
            // broken allowed path is exactly the shape that makes a gate look verified when it is
            // not. The recorders are captured from the fixture instance, so they still accumulate
            // across scopes.
            services.RemoveAll<ISpeFileOperations>();
            services.AddScoped<ISpeFileOperations>(
                _ => new RecordingVersionSpeOperations(VersionListReads, VersionByteReads));

            // The base fixture's document double returns GraphDriveId = "drive-{id}", which the
            // route's SPE-pointer validation rejects (SPE drive ids MUST start with "b!"), so every
            // authorized request would 409 before reaching the facade and the allowed-path tests
            // would be indistinguishable from a denial.
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton<IDocumentDataverseService>(new VersionedDocumentDataverseService());
        });
    }

    /// <summary>
    /// A document whose SPE pointers the version routes will accept: a <c>b!</c>-prefixed drive id
    /// and <c>HasFile = true</c>.
    /// </summary>
    private sealed class VersionedDocumentDataverseService : IDocumentDataverseService
    {
        public Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<DocumentEntity?>(new DocumentEntity
            {
                Id = id,
                Name = "Versioned Document",
                FileName = $"{id}.docx",
                ContainerId = Guid.NewGuid().ToString(),
                GraphDriveId = $"b!drive-{id}",
                GraphItemId = $"item-{id}",
                HasFile = true
            });

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
            "DocumentVersionTestFixture models only the version routes' document read. Model a new "
            + "member deliberately rather than returning an empty default.";
    }

    /// <summary>
    /// The byte source. Only the two version members are modelled; every other member of the facade
    /// throws, so a test that strays outside the version routes fails loudly.
    /// </summary>
    private sealed class RecordingVersionSpeOperations : ISpeFileOperations
    {
        private readonly ConcurrentBag<(string, string)> _listReads;
        private readonly ConcurrentBag<(string, string, string)> _byteReads;

        public RecordingVersionSpeOperations(
            ConcurrentBag<(string, string)> listReads,
            ConcurrentBag<(string, string, string)> byteReads)
        {
            _listReads = listReads;
            _byteReads = byteReads;
        }

        public Task<IReadOnlyList<VersionInfoDto>?> ListFileVersionsAsUserAsync(
            HttpContext ctx, string driveId, string itemId, CancellationToken ct = default)
        {
            _listReads.Add((driveId, itemId));
            return Task.FromResult<IReadOnlyList<VersionInfoDto>?>(new List<VersionInfoDto>
            {
                new("4.0", "e4", new DateTimeOffset(2026, 8, 5, 14, 0, 0, TimeSpan.Zero), 2097152),
                new("3.0", "e3", new DateTimeOffset(2026, 8, 1, 10, 30, 0, TimeSpan.Zero), 1048576),
                new("2.0", "e2", new DateTimeOffset(2026, 7, 20, 9, 0, 0, TimeSpan.Zero), 524288),
            });
        }

        public Task<Stream?> DownloadFileVersionAsUserAsync(
            HttpContext ctx, string driveId, string itemId, string versionId, CancellationToken ct = default)
        {
            _byteReads.Add((driveId, itemId, versionId));
            return Task.FromResult<Stream?>(
                new MemoryStream(Encoding.UTF8.GetBytes($"{PriorVersionBytesMarker}:{versionId}")));
        }

        private const string NotModelled =
            "DocumentVersionTestFixture's SPE double models only the two version reads. A version "
            + "route that reaches any other facade member is doing something this task did not "
            + "intend — model it deliberately.";

        private static T Unmodelled<T>() => throw new NotSupportedException(NotModelled);

        public Task<FileHandleDto?> GetFileMetadataAsync(string driveId, string itemId, CancellationToken ct = default) => Unmodelled<Task<FileHandleDto?>>();
        public Task<FileHandleDto?> GetFileMetadataAsUserAsync(HttpContext ctx, string driveId, string itemId, CancellationToken ct = default) => Unmodelled<Task<FileHandleDto?>>();
        public Task<Stream?> DownloadFileAsync(string driveId, string itemId, CancellationToken ct = default) => Unmodelled<Task<Stream?>>();
        // Deliberately UNMODELLED, not recorded. These are the INTERNAL OBO version routes; the
        // app-only overload exists only for the external-access surface (unified-access-control-r2).
        // If an internal route ever reaches this, it has silently dropped from the caller's delegated
        // permission to the broker identity — a privilege escalation — and this throws instead of
        // quietly returning a version list.
        public Task<IReadOnlyList<VersionInfoDto>?> ListFileVersionsAsync(string driveId, string itemId, CancellationToken ct = default) => Unmodelled<Task<IReadOnlyList<VersionInfoDto>?>>();
        public Task<Stream?> DownloadFileAsUserAsync(HttpContext ctx, string driveId, string itemId, CancellationToken ct = default) => Unmodelled<Task<Stream?>>();
        public Task<string?> GetCurrentVersionIdAsUserAsync(HttpContext ctx, string driveId, string itemId, CancellationToken ct = default) => Unmodelled<Task<string?>>();
        public Task<FileHandleDto?> UploadSmallAsUserAsync(HttpContext ctx, string containerId, string path, Stream content, CancellationToken ct = default) => Unmodelled<Task<FileHandleDto?>>();
        public Task<FileHandleDto?> UploadSmallAsync(string driveId, string path, Stream content, CancellationToken ct = default) => Unmodelled<Task<FileHandleDto?>>();
        public Task<FileHandleDto?> ReplaceFileContentAsUserAsync(HttpContext ctx, string driveId, string itemId, Stream content, CancellationToken ct = default) => Unmodelled<Task<FileHandleDto?>>();
        public Task<FileHandleDto?> ReplaceFileContentAsUserAsync(HttpContext ctx, string driveId, string itemId, Stream content, string? ifMatch, CancellationToken ct = default) => Unmodelled<Task<FileHandleDto?>>();
        public Task<string> ResolveDriveIdAsync(string containerOrDriveId, CancellationToken ct = default) => Unmodelled<Task<string>>();
        public Task<SpeSubscriptionDto> CreateDriveRootSubscriptionAsync(string driveId, string notificationUrl, string clientState, DateTimeOffset expirationDateTime, CancellationToken ct = default) => Unmodelled<Task<SpeSubscriptionDto>>();
        public Task<SpeSubscriptionDto> RenewSubscriptionAsync(string subscriptionId, DateTimeOffset newExpirationDateTime, CancellationToken ct = default) => Unmodelled<Task<SpeSubscriptionDto>>();
        public Task DeleteSubscriptionAsync(string subscriptionId, CancellationToken ct = default) => Unmodelled<Task>();
        public Task<SpeDeltaResult> EnumerateDriveDeltaAsync(string driveId, string? deltaLink, CancellationToken ct = default) => Unmodelled<Task<SpeDeltaResult>>();
    }
}
