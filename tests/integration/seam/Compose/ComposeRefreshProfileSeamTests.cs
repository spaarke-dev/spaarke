// Task 040 (spaarkeai-compose-r5, FR-09 / gap G10) — the THROUGH-THE-WIRE proof for the manual
// "Refresh Profile" leg: POST /api/compose/documents/{documentId}/refresh-profile routes through
// the REAL endpoint → IComposeService.RefreshProfileAsync → the shared fire-and-forget
// DispatchBackgroundProfile pipeline, returning 202 Accepted.
//
// unified-access-control-r2 task 166 r1 (route-authorization sweep, the amendment-(d) family): the profile is
// PERSISTED app-only onto the sprk_document row the route names, so the route now carries the document WRITE filter
// (the route parameter was renamed {documentId} so the filter reads it — the URL is unchanged); the tenant is the
// caller's claim (the body's tenantId is obsolete — a body without it is accepted, not 400); and the "profiled eTag"
// stamp is written only for the SPE item the AUTHORIZED row points at, never for an item id the body names.
//
// The RELOAD/onload re-trigger leg is storm-guarded (re-fires only when the live eTag differs from the
// per-doc profiled-eTag stamp) and best-effort — its dispatch is a detached Task.Run by design
// (fire-and-forget), so it is not deterministically observable in-process; the storm-guard LOGIC + this
// endpoint (which shares the SAME RefreshProfileAsync → DispatchBackgroundProfile path) are the
// verifiable surface. See notes/task-040-deviations.md.
//
// Reuses ComposeFidelitySeamFixture (host + SPE/Dataverse/indexing module-boundary mocks + fake auth), extended with
// a stated-rights IAccessDataSource and a one-row IDocumentDataverseService (the DocumentVersionSeamFixture precedent).
// ADR-038 seam DoD: through-the-wire WebApplicationFactory slice. NO Mock<HttpMessageHandler>, NO
// DI-registration test, NO ctor-null test.

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Spaarke.Dataverse;
using Sprk.Bff.Api.Tests.Seam.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Seam.Compose;

public sealed class ComposeRefreshProfileSeamTests : IClassFixture<RefreshProfileSeamFixture>
{
    private const string ProfiledETagKeyPrefix = "sdap:compose:profiled-etag:";

    private readonly RefreshProfileSeamFixture _fixture;

    public ComposeRefreshProfileSeamTests(RefreshProfileSeamFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task RefreshProfile_ValidRequest_Returns202_ThroughTheWire()
    {
        _fixture.ResetBoundaries();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{RefreshProfileSeamFixture.WritableDocument}/refresh-profile",
            new { tenantId = ComposeFidelitySeamFixture.TestTenantId, documentSpeId = RefreshProfileSeamFixture.WritableItem, eTag = "\"v1-etag\"" });

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            $"a manual Refresh Profile re-dispatches the profile fire-and-forget and accepts (202) — body: {body}");
        (await StampFor(RefreshProfileSeamFixture.WritableItem)).Should().Be("\"v1-etag\"",
            "the stamp is written for the item the authorized row points at");
    }

    [Fact]
    public async Task RefreshProfile_ABodyWithNoTenantId_IsAccepted_TheTenantIsTheClaim()
    {
        _fixture.ResetBoundaries();

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{RefreshProfileSeamFixture.WritableDocument}/refresh-profile",
            new { documentSpeId = RefreshProfileSeamFixture.WritableItem }); // no tenantId

        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "tenantId is obsolete on the body (task 166 r1) — no longer required, never read");
    }

    [Theory]
    [InlineData("read-only")]
    [InlineData("unknown")]
    public async Task RefreshProfile_WithoutWriteOnTheDocument_IsTheSame403_AndStampsNothing(string shape)
    {
        _fixture.ResetBoundaries();
        var documentId = shape == "read-only" ? RefreshProfileSeamFixture.ReadOnlyDocument : Guid.NewGuid();
        var item = $"spe-item-166-denied-{shape}";

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{documentId}/refresh-profile",
            new { documentSpeId = item, eTag = "\"forged\"" });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the profile is written onto the row, so the caller must hold WRITE on it — an unknown id gets the same answer");
        (await StampFor(item)).Should().BeNull("nothing ran past the route's document filter");
    }

    [Fact]
    public async Task RefreshProfile_ABodyItemThatIsNotTheRowsItem_IsNeverStamped()
    {
        _fixture.ResetBoundaries();
        const string someoneElsesItem = "spe-item-166-someone-elses";

        using var client = _fixture.CreateAuthenticatedClient();
        var response = await client.PostAsJsonAsync(
            $"/api/compose/documents/{RefreshProfileSeamFixture.WritableDocument}/refresh-profile",
            new { documentSpeId = someoneElsesItem, eTag = "\"suppress-their-reload\"" });

        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await StampFor(someoneElsesItem)).Should().BeNull(
            "the stamp suppresses that item's reload re-trigger, so only the authorized row's OWN item may be stamped");
    }

    private async Task<string?> StampFor(string speId)
    {
        var cache = _fixture.Services.GetRequiredService<IDistributedCache>();
        return await cache.GetStringAsync(ProfiledETagKeyPrefix + speId);
    }
}

/// <summary>
/// The Compose seam host plus two doubles for the refresh-profile route's document filter: a stated-rights
/// <see cref="IAccessDataSource"/> (Write on <see cref="WritableDocument"/>, Read only on
/// <see cref="ReadOnlyDocument"/>, nothing on anything else) and a one-row <see cref="IDocumentDataverseService"/>
/// whose row points at <see cref="WritableItem"/>.
/// </summary>
public sealed class RefreshProfileSeamFixture : ComposeFidelitySeamFixture
{
    public static readonly Guid WritableDocument = Guid.Parse("aaaa1660-0000-4000-8000-000000000001");
    public static readonly Guid ReadOnlyDocument = Guid.Parse("aaaa1660-0000-4000-8000-000000000002");
    public const string WritableItem = "spe-item-166-refresh-writable";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureTestServices(services =>
        {
            var access = new Mock<IAccessDataSource>();
            access.Setup(a => a.GetUserAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string userId, string resourceId, string? _, CancellationToken _) => Snapshot(userId, resourceId));
            access.Setup(a => a.GetRecordAccessAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string userId, string _, Guid recordId, string? _, CancellationToken _) => Snapshot(userId, recordId.ToString()));
            services.RemoveAll<IAccessDataSource>();
            services.AddSingleton(access.Object);

            var documents = new Mock<IDocumentDataverseService>(MockBehavior.Loose);
            documents.Setup(d => d.GetDocumentAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((string id, CancellationToken _) =>
                    Guid.TryParse(id, out var g) && g == WritableDocument
                        ? new DocumentEntity { Id = id, Name = "Refresh Seam Document", GraphItemId = WritableItem, GraphDriveId = "b!drive-166" }
                        : null);
            services.RemoveAll<IDocumentDataverseService>();
            services.AddSingleton(documents.Object);
        });
    }

    private static AccessSnapshot Snapshot(string userId, string resourceId) => new()
    {
        UserId = userId,
        ResourceId = resourceId,
        AccessRights = Guid.TryParse(resourceId, out var id) && id == WritableDocument
            ? AccessRights.Read | AccessRights.Write
            : Guid.TryParse(resourceId, out id) && id == ReadOnlyDocument
                ? AccessRights.Read
                : AccessRights.None,
    };
}
