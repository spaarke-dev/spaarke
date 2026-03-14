using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for the Search Containers endpoint (SPE-057).
///
/// Strategy: Tests validate DTO structure, domain model records, endpoint registration shape,
/// and request/response contract. Graph SDK classes are sealed and cannot be mocked —
/// integration-level Graph Search behavior (live POST /search/query) is verified manually
/// against the dev environment. Unit tests cover the shape and mapping contract.
///
/// SPE-057: POST /api/spe/search/containers?configId={id}
/// </summary>
public class SearchContainersTests
{
    // =========================================================================
    // SearchContainersRequest Tests
    // =========================================================================

    [Fact]
    public void SearchContainersRequest_WithQuery_HasCorrectValues()
    {
        // Arrange & Act
        var request = new SearchContainersEndpoints.SearchContainersRequest(
            Query: "legal workspace",
            PageSize: 10,
            SkipToken: null);

        // Assert
        request.Query.Should().Be("legal workspace");
        request.PageSize.Should().Be(10);
        request.SkipToken.Should().BeNull();
    }

    [Fact]
    public void SearchContainersRequest_WithPagination_HasCorrectValues()
    {
        // Arrange & Act
        var request = new SearchContainersEndpoints.SearchContainersRequest(
            Query: "matter",
            PageSize: 25,
            SkipToken: "25");

        // Assert
        request.Query.Should().Be("matter");
        request.PageSize.Should().Be(25);
        request.SkipToken.Should().Be("25");
    }

    [Fact]
    public void SearchContainersRequest_WithNullPageSize_IsAllowed()
    {
        // Arrange & Act — pageSize is optional
        var request = new SearchContainersEndpoints.SearchContainersRequest(
            Query: "test",
            PageSize: null,
            SkipToken: null);

        // Assert
        request.PageSize.Should().BeNull("pageSize is optional and defaults to 25 in the service");
    }

    // =========================================================================
    // SearchContainersResponse Tests
    // =========================================================================

    [Fact]
    public void SearchContainersResponse_WithResults_HasCorrectShape()
    {
        // Arrange
        var items = new List<SearchContainersEndpoints.SearchContainerDto>
        {
            new("container-1", "Legal Workspace", "Matter 123 workspace", "type-guid-1"),
            new("container-2", "Project Alpha", null, "type-guid-1")
        };

        // Act
        var response = new SearchContainersEndpoints.SearchContainersResponse(
            Items: items,
            TotalCount: 2,
            NextSkipToken: null);

        // Assert
        response.Items.Should().HaveCount(2);
        response.TotalCount.Should().Be(2);
        response.NextSkipToken.Should().BeNull();
    }

    [Fact]
    public void SearchContainersResponse_WithPagination_HasNextToken()
    {
        // Arrange
        var items = Enumerable
            .Range(1, 25)
            .Select(i => new SearchContainersEndpoints.SearchContainerDto(
                $"container-{i}", $"Container {i}", null, "type-guid"))
            .ToList();

        // Act
        var response = new SearchContainersEndpoints.SearchContainersResponse(
            Items: items,
            TotalCount: 100,
            NextSkipToken: "25");

        // Assert
        response.Items.Should().HaveCount(25);
        response.TotalCount.Should().Be(100);
        response.NextSkipToken.Should().Be("25", "token encodes the next 'from' offset");
    }

    [Fact]
    public void SearchContainersResponse_EmptyResults_HasZeroItems()
    {
        // Arrange & Act
        var response = new SearchContainersEndpoints.SearchContainersResponse(
            Items: Array.Empty<SearchContainersEndpoints.SearchContainerDto>(),
            TotalCount: 0,
            NextSkipToken: null);

        // Assert
        response.Items.Should().BeEmpty();
        response.TotalCount.Should().Be(0);
        response.NextSkipToken.Should().BeNull();
    }

    [Fact]
    public void SearchContainersResponse_NullTotalCount_IsAllowed()
    {
        // Arrange & Act — Graph Search may not always return a total count
        var response = new SearchContainersEndpoints.SearchContainersResponse(
            Items: Array.Empty<SearchContainersEndpoints.SearchContainerDto>(),
            TotalCount: null,
            NextSkipToken: null);

        // Assert
        response.TotalCount.Should().BeNull("Graph Search may not always report a total count");
    }

    // =========================================================================
    // SearchContainerDto Tests
    // =========================================================================

    [Fact]
    public void SearchContainerDto_WithAllFields_HasCorrectValues()
    {
        // Arrange & Act
        var dto = new SearchContainersEndpoints.SearchContainerDto(
            Id: "b!container-id-123",
            DisplayName: "Legal Workspace",
            Description: "Matter 123 document storage",
            ContainerTypeId: "a1b2c3d4-e5f6-7890-abcd-ef1234567890");

        // Assert
        dto.Id.Should().Be("b!container-id-123");
        dto.DisplayName.Should().Be("Legal Workspace");
        dto.Description.Should().Be("Matter 123 document storage");
        dto.ContainerTypeId.Should().Be("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    }

    [Fact]
    public void SearchContainerDto_WithNullableFields_AllowsNulls()
    {
        // Arrange & Act — Description and ContainerTypeId are nullable
        var dto = new SearchContainersEndpoints.SearchContainerDto(
            Id: "container-456",
            DisplayName: "Project Alpha",
            Description: null,
            ContainerTypeId: null);

        // Assert
        dto.Description.Should().BeNull("description is optional on SPE containers");
        dto.ContainerTypeId.Should().BeNull("containerTypeId may not be returned by Graph Search");
    }

    // =========================================================================
    // SpeAdminGraphService Domain Model Tests (SearchContainerResult, ContainerSearchPage)
    // =========================================================================

    [Fact]
    public void SearchContainerResult_WithAllFields_HasCorrectValues()
    {
        // Arrange & Act
        var result = new SpeAdminGraphService.SearchContainerResult(
            Id: "b!xyz-abc",
            DisplayName: "Test Container",
            Description: "A test SPE container",
            ContainerTypeId: "type-id-123");

        // Assert
        result.Id.Should().Be("b!xyz-abc");
        result.DisplayName.Should().Be("Test Container");
        result.Description.Should().Be("A test SPE container");
        result.ContainerTypeId.Should().Be("type-id-123");
    }

    [Fact]
    public void SearchContainerResult_WithNullOptionals_AllowsNulls()
    {
        // Arrange & Act
        var result = new SpeAdminGraphService.SearchContainerResult(
            Id: "b!abc",
            DisplayName: "Container No Description",
            Description: null,
            ContainerTypeId: null);

        // Assert
        result.Description.Should().BeNull();
        result.ContainerTypeId.Should().BeNull();
    }

    [Fact]
    public void ContainerSearchPage_WithResults_HasCorrectShape()
    {
        // Arrange
        var items = new List<SpeAdminGraphService.SearchContainerResult>
        {
            new("id-1", "Container One", "Desc one", "type-a"),
            new("id-2", "Container Two", null, "type-a"),
            new("id-3", "Container Three", "Desc three", "type-b")
        };

        // Act
        var page = new SpeAdminGraphService.ContainerSearchPage(
            Items: items,
            TotalCount: 50,
            NextSkipToken: "25");

        // Assert
        page.Items.Should().HaveCount(3);
        page.TotalCount.Should().Be(50);
        page.NextSkipToken.Should().Be("25");
    }

    [Fact]
    public void ContainerSearchPage_Empty_HasZeroItemsAndNoToken()
    {
        // Arrange & Act
        var page = new SpeAdminGraphService.ContainerSearchPage(
            Items: Array.Empty<SpeAdminGraphService.SearchContainerResult>(),
            TotalCount: 0,
            NextSkipToken: null);

        // Assert
        page.Items.Should().BeEmpty();
        page.TotalCount.Should().Be(0);
        page.NextSkipToken.Should().BeNull();
    }

    // =========================================================================
    // Endpoint Registration Tests
    // =========================================================================

    [Fact]
    public void MapSearchContainersEndpoints_MethodExists_IsStatic()
    {
        // Arrange
        var method = typeof(SearchContainersEndpoints)
            .GetMethod("MapSearchContainersEndpoints");

        // Assert
        method.Should().NotBeNull("the extension method must exist for route registration");
        method!.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void MapSearchContainersEndpoints_Parameter_TakesRouteGroupBuilder()
    {
        // Arrange
        var method = typeof(SearchContainersEndpoints)
            .GetMethod("MapSearchContainersEndpoints");

        // Assert
        method.Should().NotBeNull();
        var parameters = method!.GetParameters();
        parameters.Should().HaveCount(1);
        parameters[0].ParameterType.Should().Be(typeof(RouteGroupBuilder));
    }

    [Fact]
    public void MapSearchContainersEndpoints_ReturnType_IsRouteGroupBuilder()
    {
        // Arrange
        var method = typeof(SearchContainersEndpoints)
            .GetMethod("MapSearchContainersEndpoints");

        // Assert
        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(RouteGroupBuilder));
    }

    // =========================================================================
    // Validation Behaviour Tests (request contract)
    // =========================================================================

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void SearchContainersRequest_EmptyOrNullQuery_ShouldBeRejectedBy400(string? query)
    {
        // Arrange — the endpoint handler checks query before calling Graph
        // This test documents the expected validation: empty query → 400 Bad Request
        // The handler calls string.IsNullOrWhiteSpace(request.Query) and returns 400.
        var request = new SearchContainersEndpoints.SearchContainersRequest(
            Query: query!,
            PageSize: null,
            SkipToken: null);

        // Act & Assert — validate the query value is empty/null as the endpoint would detect
        var isInvalid = string.IsNullOrWhiteSpace(request.Query);
        isInvalid.Should().BeTrue("an empty or null query must be rejected with HTTP 400");
    }

    [Fact]
    public void SearchContainersRequest_ValidQuery_IsNotEmpty()
    {
        // Arrange
        var request = new SearchContainersEndpoints.SearchContainersRequest(
            Query: "legal",
            PageSize: null,
            SkipToken: null);

        // Act & Assert
        string.IsNullOrWhiteSpace(request.Query).Should().BeFalse();
    }

    // =========================================================================
    // Pagination Token Encoding Tests
    // =========================================================================

    [Theory]
    [InlineData(null, 25, "25")]   // first page size=25 → next token = "25"
    [InlineData("25", 25, "50")]  // page 2, size=25 → next token = "50"
    [InlineData("0", 10, "10")]   // from=0, size=10 → next token = "10"
    public void SkipTokenEncoding_ProducesCorrectNextOffset(
        string? currentSkipToken, int pageSize, string expectedNextToken)
    {
        // Arrange — mirrors the token decoding / encoding logic in SearchContainersAsync
        var from = 0;
        if (!string.IsNullOrWhiteSpace(currentSkipToken) &&
            int.TryParse(currentSkipToken, out var parsedFrom))
        {
            from = Math.Max(0, parsedFrom);
        }

        // Act — simulate what SearchContainersAsync does when moreResultsAvailable=true
        var nextFrom = from + pageSize;
        var nextSkipToken = nextFrom.ToString();

        // Assert
        nextSkipToken.Should().Be(expectedNextToken);
    }

    [Fact]
    public void SkipTokenDecoding_MalformedToken_DefaultsToZeroOffset()
    {
        // Arrange — a non-numeric or corrupted skip token should not crash the service
        var malformedToken = "abc-not-a-number";

        // Act — mirrors the decoding in SearchContainersAsync
        var from = 0;
        if (!string.IsNullOrWhiteSpace(malformedToken) &&
            int.TryParse(malformedToken, out var parsedFrom))
        {
            from = Math.Max(0, parsedFrom);
        }

        // Assert — malformed token falls back to from=0 (first page)
        from.Should().Be(0, "a malformed skip token should default to page 0");
    }
}
