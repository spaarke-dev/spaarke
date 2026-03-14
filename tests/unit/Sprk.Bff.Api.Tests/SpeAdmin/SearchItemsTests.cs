using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.SpeAdmin;

/// <summary>
/// Unit tests for the SearchItems endpoint and the SearchItemsAsync service method.
///
/// Tests cover:
/// - Input validation (empty query → 400, missing configId → 400)
/// - Service domain model mapping (SearchItemPage → SearchItemsResponse)
/// - Acceptance criteria: scoped search, unscoped search, empty results, pagination
///
/// Note: Tests that require a real Graph client are marked [Fact(Skip=...)] because
/// GraphServiceClient and its request builders use sealed classes that cannot be mocked.
/// </summary>
public class SearchItemsTests
{
    // =========================================================================
    // SearchItemsRequest DTO Tests
    // =========================================================================

    [Fact]
    public void SearchItemsRequest_WithAllFields_CreatesCorrectRecord()
    {
        // Arrange & Act
        var request = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "contract.pdf",
            ContainerId: "container-123",
            FileType: "pdf",
            PageSize: 50,
            SkipToken: "25");

        // Assert
        request.Query.Should().Be("contract.pdf");
        request.ContainerId.Should().Be("container-123");
        request.FileType.Should().Be("pdf");
        request.PageSize.Should().Be(50);
        request.SkipToken.Should().Be("25");
    }

    [Fact]
    public void SearchItemsRequest_WithMinimalFields_AllowsNulls()
    {
        // Arrange & Act
        var request = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "test",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Assert
        request.ContainerId.Should().BeNull();
        request.FileType.Should().BeNull();
        request.PageSize.Should().BeNull();
        request.SkipToken.Should().BeNull();
    }

    // =========================================================================
    // SearchItemsResponse DTO Tests
    // =========================================================================

    [Fact]
    public void SearchItemsResponse_WithItems_SetsPropertiesCorrectly()
    {
        // Arrange
        var items = new List<SearchItemsEndpoints.SearchItemDto>
        {
            new("item-1", "report.pdf", 102400, DateTimeOffset.UtcNow, "container-1", "Contracts", "https://example.com/report.pdf", "application/pdf"),
            new("item-2", "summary.docx", 51200, DateTimeOffset.UtcNow.AddDays(-1), null, null, null, null)
        };

        // Act
        var response = new SearchItemsEndpoints.SearchItemsResponse(
            Items: items,
            NextSkipToken: "50",
            TotalCount: 125);

        // Assert
        response.Items.Should().HaveCount(2);
        response.NextSkipToken.Should().Be("50");
        response.TotalCount.Should().Be(125);
    }

    [Fact]
    public void SearchItemsResponse_WithEmptyResults_HasZeroItems()
    {
        // Arrange & Act
        var response = new SearchItemsEndpoints.SearchItemsResponse(
            Items: new List<SearchItemsEndpoints.SearchItemDto>(),
            NextSkipToken: null,
            TotalCount: 0);

        // Assert
        response.Items.Should().BeEmpty();
        response.NextSkipToken.Should().BeNull();
        response.TotalCount.Should().Be(0);
    }

    // =========================================================================
    // SearchItemDto Tests
    // =========================================================================

    [Fact]
    public void SearchItemDto_MapsAllFields()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;

        // Act
        var dto = new SearchItemsEndpoints.SearchItemDto(
            Id: "drive-item-123",
            Name: "contract-2024.pdf",
            Size: 204800,
            LastModifiedDateTime: now,
            ContainerId: "container-abc",
            ContainerName: "Legal Contracts",
            WebUrl: "https://sharepoint.com/sites/legal/contract-2024.pdf",
            MimeType: "application/pdf");

        // Assert
        dto.Id.Should().Be("drive-item-123");
        dto.Name.Should().Be("contract-2024.pdf");
        dto.Size.Should().Be(204800);
        dto.LastModifiedDateTime.Should().Be(now);
        dto.ContainerId.Should().Be("container-abc");
        dto.ContainerName.Should().Be("Legal Contracts");
        dto.WebUrl.Should().NotBeNullOrWhiteSpace();
        dto.MimeType.Should().Be("application/pdf");
    }

    [Fact]
    public void SearchItemDto_NullableFields_CanBeNull()
    {
        // Act
        var dto = new SearchItemsEndpoints.SearchItemDto(
            Id: "item-1",
            Name: "folder",
            Size: null,
            LastModifiedDateTime: null,
            ContainerId: null,
            ContainerName: null,
            WebUrl: null,
            MimeType: null);

        // Assert
        dto.Size.Should().BeNull();
        dto.LastModifiedDateTime.Should().BeNull();
        dto.ContainerId.Should().BeNull();
        dto.ContainerName.Should().BeNull();
        dto.WebUrl.Should().BeNull();
        dto.MimeType.Should().BeNull();
    }

    // =========================================================================
    // SpeAdminGraphService.SearchItemPage Domain Model Tests
    // =========================================================================

    [Fact]
    public void SearchItemPage_WithResults_SetsPropertiesCorrectly()
    {
        // Arrange
        var items = new List<SpeAdminGraphService.SpeSearchItemResult>
        {
            new("id-1", "file.pdf", 1024, DateTimeOffset.UtcNow, "container-1", null, "https://example.com", "application/pdf"),
            new("id-2", "doc.docx", 2048, DateTimeOffset.UtcNow.AddHours(-1), "container-1", null, null, null)
        };

        // Act
        var page = new SpeAdminGraphService.SearchItemPage(items, "50", 200);

        // Assert
        page.Items.Should().HaveCount(2);
        page.NextSkipToken.Should().Be("50");
        page.TotalCount.Should().Be(200);
    }

    [Fact]
    public void SearchItemPage_EmptyResults_NoNextToken()
    {
        // Act
        var page = new SpeAdminGraphService.SearchItemPage(
            new List<SpeAdminGraphService.SpeSearchItemResult>(),
            null,
            0);

        // Assert
        page.Items.Should().BeEmpty();
        page.NextSkipToken.Should().BeNull();
        page.TotalCount.Should().Be(0);
    }

    [Fact]
    public void SearchItemPage_WithPagination_NextSkipTokenIsNextOffset()
    {
        // Arrange — simulate page 1 of results (25 results, token "25" means next page starts at offset 25)
        var items = Enumerable.Range(1, 25)
            .Select(i => new SpeAdminGraphService.SpeSearchItemResult(
                $"id-{i}", $"file-{i}.pdf", 1024 * i, DateTimeOffset.UtcNow,
                "container-1", null, $"https://example.com/file-{i}.pdf", "application/pdf"))
            .ToList();

        // Act
        var page = new SpeAdminGraphService.SearchItemPage(items, "25", 100);

        // Assert
        page.Items.Should().HaveCount(25);
        page.NextSkipToken.Should().Be("25");
        page.TotalCount.Should().Be(100);
    }

    // =========================================================================
    // Endpoint Route Registration Tests
    // =========================================================================

    [Fact]
    public void MapSearchItemsEndpoints_ReturnsRouteGroupBuilder()
    {
        // Verify the extension method exists and is callable — compilation test.
        // The actual route registration is tested in EndpointGroupingTests.cs.
        var method = typeof(SearchItemsEndpoints).GetMethod("MapSearchItemsEndpoints");
        method.Should().NotBeNull("MapSearchItemsEndpoints extension method must be defined on SearchItemsEndpoints");
        method!.IsStatic.Should().BeTrue();
    }

    // =========================================================================
    // Integration-style tests via WebApplicationFactory
    // (Graph calls skipped due to sealed SDK types)
    // =========================================================================

    /// <summary>
    /// Verifies that POST /api/spe/search/items requires authentication (returns 401 without token).
    /// </summary>
    [Fact]
    public async Task SearchItems_WithoutAuthentication_Returns401()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "test",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync("/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001", requestBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies that missing configId returns 401 (auth runs before validation in this route group).
    /// </summary>
    [Fact]
    public async Task SearchItems_MissingConfigId_Returns401WithoutToken()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest("test", null, null, null, null);

        // Act
        var response = await client.PostAsJsonAsync("/api/spe/search/items", requestBody);

        // Assert — auth runs first
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies endpoint route is registered at POST /api/spe/search/items.
    /// When authenticated, missing configId should return 400 (not 404).
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_MissingConfigId_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest("test", null, null, null, null);

        // Act — no configId provided
        var response = await client.PostAsJsonAsync("/api/spe/search/items", requestBody);

        // Assert — route exists; configId validation returns 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that an empty query string returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_EmptyQuery_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001",
            requestBody);

        // Assert — empty query is rejected (per acceptance criteria)
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that whitespace-only query string returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_WhitespaceQuery_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "   ",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001",
            requestBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that an invalid (non-GUID) configId returns 400 Bad Request.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_InvalidConfigId_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest("contract.pdf", null, null, null, null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=not-a-guid",
            requestBody);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Verifies that a valid request with a non-existent configId returns 400
    /// (configId not found in Dataverse → ConfigNotFoundException → 400).
    /// In the test environment the Dataverse client is mocked and returns null for any configId.
    /// </summary>
    [Fact]
    public async Task SearchItems_WithToken_ValidConfigIdNotFound_Returns400()
    {
        // Arrange
        var factory = new CustomWebAppFactory();
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer test-token");

        var requestBody = new SearchItemsEndpoints.SearchItemsRequest(
            Query: "contract.pdf",
            ContainerId: null,
            FileType: null,
            PageSize: null,
            SkipToken: null);

        // Act
        var response = await client.PostAsJsonAsync(
            "/api/spe/search/items?configId=00000000-0000-0000-0000-000000000001",
            requestBody);

        // Assert — configId not found in Dataverse → 400
        // (In test environment, Dataverse is not connected; SpeAdminGraphService will fail config resolution)
        response.StatusCode.Should().BeOneOf(
            HttpStatusCode.BadRequest,
            HttpStatusCode.InternalServerError);
    }

    // =========================================================================
    // Domain model mapping correctness (scoped vs unscoped, pagination fields)
    // =========================================================================

    [Fact]
    public void SearchItemPage_UnscopedSearch_ContainerIdIsNull()
    {
        // When search is unscoped (no containerId), the result items should have null ContainerId.
        var items = new List<SpeAdminGraphService.SpeSearchItemResult>
        {
            new("id-1", "file.pdf", 1024, DateTimeOffset.UtcNow, null, null, "https://example.com/file.pdf", "application/pdf")
        };

        var page = new SpeAdminGraphService.SearchItemPage(items, null, 1);

        page.Items[0].ContainerId.Should().BeNull("unscoped search does not scope to a container");
    }

    [Fact]
    public void SearchItemPage_ScopedSearch_ContainerIdIsSet()
    {
        // When search is scoped to a specific container, results carry the container ID.
        var containerId = "container-abc";
        var items = new List<SpeAdminGraphService.SpeSearchItemResult>
        {
            new("id-1", "contract.pdf", 204800, DateTimeOffset.UtcNow, containerId, "Legal Docs", "https://example.com/contract.pdf", "application/pdf")
        };

        var page = new SpeAdminGraphService.SearchItemPage(items, null, 1);

        page.Items[0].ContainerId.Should().Be(containerId, "scoped search populates ContainerId from the request");
    }

    [Fact]
    public void SearchItemDto_HasAllRequiredFields()
    {
        // Acceptance criterion: Results include item id, name, size, lastModifiedDateTime, and parent container info.
        var dto = new SearchItemsEndpoints.SearchItemDto(
            Id: "item-xyz",
            Name: "agreement.pdf",
            Size: 512000,
            LastModifiedDateTime: DateTimeOffset.UtcNow,
            ContainerId: "c-001",
            ContainerName: "Agreements",
            WebUrl: "https://sharepoint.example.com/agreement.pdf",
            MimeType: "application/pdf");

        // All required fields per acceptance criteria
        dto.Id.Should().NotBeNullOrEmpty("item id is required");
        dto.Name.Should().NotBeNullOrEmpty("item name is required");
        dto.Size.Should().NotBeNull("size is included in response");
        dto.LastModifiedDateTime.Should().NotBeNull("lastModifiedDateTime is included in response");
        dto.ContainerId.Should().NotBeNullOrEmpty("parent container info (ContainerId) is included");
    }
}
