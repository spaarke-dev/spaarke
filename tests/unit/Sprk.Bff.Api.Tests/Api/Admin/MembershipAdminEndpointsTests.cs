using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Moq;
using Sprk.Bff.Api.Api.Admin.Models;
using Sprk.Bff.Api.Services.Ai.Membership;
using Sprk.Bff.Api.Services.Ai.Membership.Models;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Admin;

/// <summary>
/// Integration tests for R3 task 036 — admin membership-discovery audit + cache-refresh endpoints:
/// <list type="bullet">
///   <item><c>GET  /api/admin/membership/discovered/{entityType}</c> (FR-1A.10, AC-1A.2)</item>
///   <item><c>POST /api/admin/membership/refresh-metadata</c> (FR-1A.11, AC-1A.7)</item>
/// </list>
/// Validates the auth contract (401 unauthenticated / 403 non-admin / 200 admin) and the
/// discovery + refresh handler delegation to <see cref="IMembershipFieldDiscoveryService"/>.
/// </summary>
public sealed class MembershipAdminEndpointsTests : IClassFixture<AdminMembershipTestFixture>
{
    private readonly AdminMembershipTestFixture _fixture;

    public MembershipAdminEndpointsTests(AdminMembershipTestFixture fixture)
    {
        _fixture = fixture;
        // Reset the strict mock between tests so leftover setups from one test don't
        // affect another. Test code re-establishes the expectations it needs.
        _fixture.MembershipDiscoveryMock.Reset();
    }

    // ================================================================================
    // ===== Auth: 401 unauthenticated, 403 non-admin =================================
    // ================================================================================

    [Fact]
    public async Task GetDiscovered_Returns401_WhenUnauthenticated()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.GetAsync("/api/admin/membership/discovered/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetDiscovered_Returns403_WhenAuthenticatedButNotAdmin()
    {
        using var client = _fixture.CreateNonAdminClient();

        var response = await client.GetAsync("/api/admin/membership/discovered/sprk_matter");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task PostRefresh_Returns401_WhenUnauthenticated()
    {
        using var client = _fixture.CreateUnauthenticatedClient();

        var response = await client.PostAsJsonAsync("/api/admin/membership/refresh-metadata", new RefreshMetadataRequest("sprk_matter"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostRefresh_Returns403_WhenAuthenticatedButNotAdmin()
    {
        using var client = _fixture.CreateNonAdminClient();

        var response = await client.PostAsJsonAsync("/api/admin/membership/refresh-metadata", new RefreshMetadataRequest("sprk_matter"));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ================================================================================
    // ===== GET /api/admin/membership/discovered/{entityType} ========================
    // ================================================================================

    [Fact]
    public async Task GetDiscovered_Returns200_WithFullDiscoveryResultShape()
    {
        // Arrange — seed a representative DiscoveryResult with auto + override sources
        // (AC-1A.2: response must include source "auto" or "override").
        var seedResult = new DiscoveryResult(
            EntityType: "sprk_matter",
            DiscoveredAt: DateTimeOffset.UtcNow,
            DiscoveredFields: new[]
            {
                new MembershipDescriptor("ownerid", "owner", "SystemUser", "systemuser", "auto"),
                new MembershipDescriptor("sprk_assignedlawfirm1", "assignedLawFirm", "Organization", "sprk_organization", "override"),
            },
            ExcludedFields: new[]
            {
                new IgnoredField("createdby", "global-exclusion"),
            },
            IgnoredFields: new[]
            {
                new IgnoredField("sprk_chartdefinition", "target-table-not-in-identity-list", "sprk_chartdefinition"),
            });

        _fixture.MembershipDiscoveryMock
            .Setup(d => d.DiscoverAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(seedResult);

        using var client = _fixture.CreateAdminClient();

        // Act
        var response = await client.GetAsync("/api/admin/membership/discovered/sprk_matter");

        // Assert — 200 + body matches design.md "Discovery Report endpoint" shape exactly.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DiscoveryResult>();
        body.Should().NotBeNull();
        body!.EntityType.Should().Be("sprk_matter");
        body.DiscoveredFields.Should().HaveCount(2);
        body.DiscoveredFields.Should().Contain(f => f.Field == "ownerid" && f.Source == "auto" && f.IdentityType == "SystemUser");
        body.DiscoveredFields.Should().Contain(f => f.Field == "sprk_assignedlawfirm1" && f.Source == "override" && f.IdentityType == "Organization");
        body.ExcludedFields.Should().ContainSingle(e => e.Field == "createdby" && e.Reason == "global-exclusion");
        body.IgnoredFields.Should().ContainSingle(i => i.Field == "sprk_chartdefinition" && i.Target == "sprk_chartdefinition");
    }

    [Fact]
    public async Task GetDiscovered_Returns200_WithEmptyCollections_WhenEntityHasNoDiscoveredFields()
    {
        // Arrange — unknown entity that the service successfully introspects but returns
        // empty for. Verifies the endpoint serializes empty collections (NOT 404 or error).
        var emptyResult = new DiscoveryResult(
            EntityType: "sprk_minimalentity",
            DiscoveredAt: DateTimeOffset.UtcNow,
            DiscoveredFields: Array.Empty<MembershipDescriptor>(),
            ExcludedFields: Array.Empty<IgnoredField>(),
            IgnoredFields: Array.Empty<IgnoredField>());

        _fixture.MembershipDiscoveryMock
            .Setup(d => d.DiscoverAsync("sprk_minimalentity", It.IsAny<CancellationToken>()))
            .ReturnsAsync(emptyResult);

        using var client = _fixture.CreateAdminClient();

        // Act
        var response = await client.GetAsync("/api/admin/membership/discovered/sprk_minimalentity");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<DiscoveryResult>();
        body!.EntityType.Should().Be("sprk_minimalentity");
        body.DiscoveredFields.Should().BeEmpty();
        body.ExcludedFields.Should().BeEmpty();
        body.IgnoredFields.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiscovered_Returns404_WhenServiceReportsEntityNotFoundInDataverse()
    {
        // Arrange — service surfaces unknown entities as InvalidOperationException
        // ("Entity 'X' not found in Dataverse metadata.") per MembershipFieldDiscoveryService.
        _fixture.MembershipDiscoveryMock
            .Setup(d => d.DiscoverAsync("unknown_entity", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Entity 'unknown_entity' not found in Dataverse metadata."));

        using var client = _fixture.CreateAdminClient();

        // Act
        var response = await client.GetAsync("/api/admin/membership/discovered/unknown_entity");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ================================================================================
    // ===== POST /api/admin/membership/refresh-metadata ==============================
    // ================================================================================

    [Fact]
    public async Task PostRefresh_InvalidatesSingleEntity_WhenBodyProvided()
    {
        // Arrange — mock service confirms it invalidated the requested entity.
        _fixture.MembershipDiscoveryMock
            .Setup(d => d.InvalidateCacheAsync("sprk_matter", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "sprk_matter" });

        using var client = _fixture.CreateAdminClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/admin/membership/refresh-metadata",
            new RefreshMetadataRequest("sprk_matter"));

        // Assert — service called with the targeted entity-type + response body reflects it.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RefreshMetadataResponse>();
        body!.Refreshed.Should().BeEquivalentTo(new[] { "sprk_matter" });
        body.At.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        _fixture.MembershipDiscoveryMock.Verify(
            d => d.InvalidateCacheAsync("sprk_matter", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostRefresh_InvalidatesAll_WhenBodyOmitted()
    {
        // Arrange — service returns the list of entities it had cached (refresh-all path).
        _fixture.MembershipDiscoveryMock
            .Setup(d => d.InvalidateCacheAsync(It.Is<string?>(s => string.IsNullOrWhiteSpace(s)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "sprk_matter", "sprk_document" });

        using var client = _fixture.CreateAdminClient();

        // Act — POST with NO body (Content-Length 0). Minimal API binds [FromBody] to null
        // for empty/missing bodies; the handler then forwards null to the service.
        using var emptyContent = new ByteArrayContent(Array.Empty<byte>());
        emptyContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var response = await client.PostAsync("/api/admin/membership/refresh-metadata", emptyContent);

        // Assert — 200 + service called with null/empty entity-type + response surfaces both invalidated entries.
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RefreshMetadataResponse>();
        body!.Refreshed.Should().BeEquivalentTo(new[] { "sprk_matter", "sprk_document" });
        _fixture.MembershipDiscoveryMock.Verify(
            d => d.InvalidateCacheAsync(It.Is<string?>(s => string.IsNullOrWhiteSpace(s)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task PostRefresh_InvalidatesAll_WhenBodyEntityTypeIsEmpty()
    {
        // Arrange — explicit { entityType: "" } in body should behave like refresh-all
        // (the handler forwards the empty string and the service normalizes it to "all").
        _fixture.MembershipDiscoveryMock
            .Setup(d => d.InvalidateCacheAsync(It.Is<string?>(s => string.IsNullOrEmpty(s)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());

        using var client = _fixture.CreateAdminClient();

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/admin/membership/refresh-metadata",
            new RefreshMetadataRequest(""));

        // Assert — 200 even when nothing was invalidated (cold-process refresh-all is valid).
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<RefreshMetadataResponse>();
        body!.Refreshed.Should().BeEmpty();
    }
}
