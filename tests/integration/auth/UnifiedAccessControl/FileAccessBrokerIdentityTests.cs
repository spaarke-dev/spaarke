using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Infrastructure.Dataverse;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.AccessControl;

/// <summary>
/// unified-access-control-r2 task 171 (owner round 69 — broker-only): the document READ routes call Graph APP-ONLY,
/// and only after the per-document gate allowed. Until 2026-10-06 preview-url / preview / content / office /
/// open-links / view-url / share-link read as the user (OBO) and failed for every caller without a container role —
/// which on a per-record secure container is everyone.
/// </summary>
/// <remarks>
/// <para>What is pinned: the allowed caller's request reaches the APP-ONLY facade member for the document's OWN pointer;
/// the denied caller's request reaches NO facade member at all (a 403 alone would pass even if a URL had been minted).
/// The OBO members these routes used are no longer on the facade, so a regression to them would not compile.</para>
/// <para>Host: <see cref="DocumentDestroyAuthorizationTestFixture"/> (rights stated by the token), with a document whose
/// pointer is a real <c>b!</c> drive, a recording facade, and a pointer world in which that drive is a business-unit
/// container of the owner's subtree (so the routes reach the facade; the pointer check's refusals have their own suite).</para>
/// </remarks>
public class FileAccessBrokerIdentityTests : IClassFixture<FileAccessBrokerTestFixture>
{
    private const string DocumentId = "17130000-0000-4000-8000-000000000001";
    private readonly FileAccessBrokerTestFixture _fixture;

    public FileAccessBrokerIdentityTests(FileAccessBrokerTestFixture fixture)
    {
        _fixture = fixture;
        _fixture.AppOnlyCalls.Clear();
    }

    [Theory(DisplayName = "Task 171: an allowed read route calls Graph APP-ONLY for the document's own pointer")]
    [InlineData("preview-url", "preview")]
    [InlineData("view-url", "preview")]
    [InlineData("content", "download")]
    [InlineData("office", "driveitem")]
    [InlineData("open-links", "driveitem")]
    public async Task AllowedReadRoute_CallsAppOnly_ForTheRowsPointer(string route, string expectedCall)
    {
        using var client = _fixture.CreateClientWithRights("ReadAccess");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/{route}");

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.AppOnlyCalls.Should().Contain($"{expectedCall}:{FileAccessBrokerTestFixture.DriveFor(DocumentId)}/item-{DocumentId}");
    }

    [Theory(DisplayName = "Task 171: a DENIED read route makes no Graph call of any kind")]
    [InlineData("preview-url")]
    [InlineData("view-url")]
    [InlineData("content")]
    [InlineData("office")]
    [InlineData("open-links")]
    public async Task DeniedReadRoute_MakesNoGraphCall(string route)
    {
        using var client = _fixture.CreateClientWithRights("");

        var response = await client.GetAsync($"/api/documents/{DocumentId}/{route}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.AppOnlyCalls.Should().BeEmpty("the gate runs before any SPE call, and an app-only call would bypass SPE entirely");
    }

    [Fact(DisplayName = "Task 171: share-link mints its link APP-ONLY, and only for a caller holding Share")]
    public async Task ShareLink_IsMintedAppOnly_OnlyForAShareHolder()
    {
        using var reader = _fixture.CreateClientWithRights("ReadAccess");
        (await reader.PostAsync($"/api/documents/{DocumentId}/share-link", content: null))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        _fixture.AppOnlyCalls.Should().BeEmpty();

        using var sharer = _fixture.CreateClientWithRights("ReadAccess,ShareAccess");
        var response = await sharer.PostAsync($"/api/documents/{DocumentId}/share-link", content: null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        _fixture.AppOnlyCalls.Should().ContainSingle()
            .Which.Should().Be($"createlink:{FileAccessBrokerTestFixture.DriveFor(DocumentId)}/item-{DocumentId}");
    }
}

/// <summary>Test host for <see cref="FileAccessBrokerIdentityTests"/>.</summary>
public sealed class FileAccessBrokerTestFixture : DocumentDestroyAuthorizationTestFixture
{
    /// <summary>Every app-only facade call, as "kind:drive/item".</summary>
    public ConcurrentQueue<string> AppOnlyCalls { get; } = new();

    public static string DriveFor(string documentId) => $"b!drive-{documentId}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton<IDocumentDataverseService>(new PointerDocumentService());

            services.RemoveAll<SpeFileStore>();
            services.AddSingleton<SpeFileStore>(sp => new RecordingAppOnlyStore(sp, AppOnlyCalls));

            services.RemoveAll<RecordContainerResolver>();
            services.AddScoped(_ => TestRecordContainerResolver.ForBusinessUnitContainers(
                c => c.StartsWith("b!drive-", StringComparison.Ordinal)));
        });
    }

    private sealed class RecordingAppOnlyStore : SpeFileStore
    {
        private readonly ConcurrentQueue<string> _calls;

        public RecordingAppOnlyStore(IServiceProvider sp, ConcurrentQueue<string> calls)
            : base(sp.GetRequiredService<ContainerOperations>(),
                   sp.GetRequiredService<DriveItemOperations>(),
                   sp.GetRequiredService<UploadSessionManager>(),
                   sp.GetRequiredService<UserOperations>())
            => _calls = calls;

        public override Task<string?> GetEmbedPreviewUrlAsync(
            string driveId, string itemId, IDictionary<string, object>? additionalData = null, CancellationToken ct = default)
        {
            _calls.Enqueue($"preview:{driveId}/{itemId}");
            return Task.FromResult<string?>("https://example.invalid/embed.aspx?token=redacted");
        }

        public override Task<Stream?> DownloadFileAsync(string driveId, string itemId, CancellationToken ct = default)
        {
            _calls.Enqueue($"download:{driveId}/{itemId}");
            return Task.FromResult<Stream?>(new MemoryStream([1, 2, 3]));
        }

        public override Task<SpeDriveItemSummary?> GetDriveItemAsync(
            string driveId, string itemId, IEnumerable<string>? selectFields = null, CancellationToken ct = default)
        {
            _calls.Enqueue($"driveitem:{driveId}/{itemId}");
            return Task.FromResult<SpeDriveItemSummary?>(new SpeDriveItemSummary(
                itemId, "Brief.docx", 3, "https://contoso.sharepoint.com/contentstorage/x/_layouts/15/Doc.aspx?sourcedoc=1",
                null, "application/vnd.openxmlformats-officedocument.wordprocessingml.document", null, null, null));
        }

        public override Task<string?> CreateSharingLinkAsync(
            string driveId, string itemId, string linkType, string scope, DateTimeOffset? expiration = null, CancellationToken ct = default)
        {
            _calls.Enqueue($"createlink:{driveId}/{itemId}");
            return Task.FromResult<string?>("https://example.invalid/share");
        }
    }

    private sealed class PointerDocumentService : IDocumentDataverseService
    {
        public Task<DocumentEntity?> GetDocumentAsync(string id, CancellationToken ct = default) =>
            Task.FromResult<DocumentEntity?>(new DocumentEntity
            {
                Id = id,
                Name = "Brief",
                FileName = "Brief.docx",
                MimeType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
                GraphDriveId = DriveFor(id),
                GraphItemId = $"item-{id}",
                HasFile = true,
            });

        private const string NotModelled = "FileAccessBrokerTestFixture models only the read routes' document read.";
        public Task DeleteDocumentAsync(string id, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task UpdateDocumentAsync(string id, UpdateDocumentRequest request, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<string> CreateDocumentAsync(CreateDocumentRequest request, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task UpdateDocumentFieldsAsync(string documentId, Dictionary<string, object?> fields, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByContainerAsync(string containerId, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<DocumentAccessLevel> GetUserAccessAsync(string userId, string documentId, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<DocumentEntity?> GetDocumentByEmailLookupAsync(Guid emailId, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<DocumentEntity?> GetEmailArchiveByCommunicationAsync(Guid communicationId, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByParentAsync(Guid parentDocumentId, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByMatterAsync(Guid matterId, Guid? excludeDocumentId = null, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByProjectAsync(Guid projectId, Guid? excludeDocumentId = null, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByInvoiceAsync(Guid invoiceId, Guid? excludeDocumentId = null, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByWorkAssignmentAsync(Guid workAssignmentId, Guid? excludeDocumentId = null, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
        public Task<IEnumerable<DocumentEntity>> GetDocumentsByConversationIndexAsync(string conversationIndexPrefix, Guid? excludeDocumentId = null, CancellationToken ct = default) => throw new NotSupportedException(NotModelled);
    }
}
