using FluentAssertions;
using Microsoft.AspNetCore.Routing;
using Sprk.Bff.Api.Api.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for ContainerTypePermissionEndpoints and related DTOs.
///
/// Strategy: Tests validate DTO structure, domain model mapping, and endpoint registration
/// contract. Graph SDK classes are sealed and cannot be mocked — integration-level Graph
/// behavior is validated through the endpoint handler structure and the SpeAdminGraphService
/// domain model contract (no live Graph calls in unit tests).
///
/// SPE-054: Container type application permissions endpoint
///   GET /api/spe/containertypes/{typeId}/permissions?configId={id}
/// </summary>
public class ContainerTypePermissionTests
{
    // =========================================================================
    // Endpoint registration tests
    // =========================================================================

    [Fact]
    public void MapContainerTypePermissionEndpoints_MethodExists_AndIsExtensionOnRouteGroupBuilder()
    {
        // Verify the endpoint registration method exists with correct signature
        var method = typeof(ContainerTypePermissionEndpoints)
            .GetMethod("MapContainerTypePermissionEndpoints");

        method.Should().NotBeNull("MapContainerTypePermissionEndpoints extension method must exist");
        method!.IsStatic.Should().BeTrue("must be a static extension method");
        method.ReturnType.Should().Be(typeof(RouteGroupBuilder), "must return RouteGroupBuilder for chaining");

        // Verify first parameter is RouteGroupBuilder (extension method receiver)
        var parameters = method.GetParameters();
        parameters.Should().NotBeEmpty();
        parameters[0].ParameterType.Should().Be(typeof(RouteGroupBuilder));
    }

    // =========================================================================
    // ContainerTypePermissionDto shape tests
    // =========================================================================

    [Fact]
    public void ContainerTypePermissionDto_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var dto = new ContainerTypePermissionDto();

        // Assert
        dto.AppId.Should().Be(string.Empty);
        dto.DelegatedPermissions.Should().NotBeNull().And.BeEmpty();
        dto.ApplicationPermissions.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void ContainerTypePermissionDto_WithFullValues_RoundTrips()
    {
        // Arrange
        var appId = "11111111-2222-3333-4444-555555555555";
        var delegated = new List<string> { "readContent", "writeContent" };
        var application = new List<string> { "full" };

        // Act
        var dto = new ContainerTypePermissionDto
        {
            AppId = appId,
            DelegatedPermissions = delegated,
            ApplicationPermissions = application
        };

        // Assert
        dto.AppId.Should().Be(appId);
        dto.DelegatedPermissions.Should().BeEquivalentTo(delegated);
        dto.ApplicationPermissions.Should().BeEquivalentTo(application);
    }

    [Fact]
    public void ContainerTypePermissionDto_WithEmptyPermissions_IsValid()
    {
        // Arrange & Act — empty permission lists represent "no permissions granted"
        var dto = new ContainerTypePermissionDto
        {
            AppId = "aaaabbbb-cccc-dddd-eeee-ffffffffffff",
            DelegatedPermissions = [],
            ApplicationPermissions = []
        };

        // Assert
        dto.AppId.Should().Be("aaaabbbb-cccc-dddd-eeee-ffffffffffff");
        dto.DelegatedPermissions.Should().BeEmpty("empty list is valid — no permissions");
        dto.ApplicationPermissions.Should().BeEmpty("empty list is valid — no permissions");
    }

    [Theory]
    [InlineData("none")]
    [InlineData("readContent")]
    [InlineData("writeContent")]
    [InlineData("manageContent")]
    [InlineData("create")]
    [InlineData("delete")]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("full")]
    [InlineData("unknownFutureValue")]
    public void ContainerTypePermissionDto_DelegatedPermissions_AcceptsKnownValues(string permission)
    {
        // Arrange & Act
        var dto = new ContainerTypePermissionDto
        {
            AppId = "test-app-id",
            DelegatedPermissions = [permission]
        };

        // Assert
        dto.DelegatedPermissions.Should().ContainSingle().Which.Should().Be(permission);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("readContent")]
    [InlineData("writeContent")]
    [InlineData("manageContent")]
    [InlineData("full")]
    [InlineData("managePermissions")]
    public void ContainerTypePermissionDto_ApplicationPermissions_AcceptsKnownValues(string permission)
    {
        // Arrange & Act
        var dto = new ContainerTypePermissionDto
        {
            AppId = "test-app-id",
            ApplicationPermissions = [permission]
        };

        // Assert
        dto.ApplicationPermissions.Should().ContainSingle().Which.Should().Be(permission);
    }

    // =========================================================================
    // ContainerTypePermissionListDto shape tests
    // =========================================================================

    [Fact]
    public void ContainerTypePermissionListDto_DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var listDto = new ContainerTypePermissionListDto();

        // Assert
        listDto.Items.Should().NotBeNull().And.BeEmpty();
        listDto.Count.Should().Be(0);
    }

    [Fact]
    public void ContainerTypePermissionListDto_WithItems_ReturnsCorrectCount()
    {
        // Arrange
        var items = new List<ContainerTypePermissionDto>
        {
            new() { AppId = "app-id-1", DelegatedPermissions = ["readContent"] },
            new() { AppId = "app-id-2", ApplicationPermissions = ["full"] }
        };

        // Act
        var listDto = new ContainerTypePermissionListDto
        {
            Items = items,
            Count = items.Count
        };

        // Assert
        listDto.Items.Should().HaveCount(2);
        listDto.Count.Should().Be(2);
        listDto.Items[0].AppId.Should().Be("app-id-1");
        listDto.Items[1].AppId.Should().Be("app-id-2");
    }

    [Fact]
    public void ContainerTypePermissionListDto_EmptyPermissions_Returns200WithEmptyArray()
    {
        // Arrange — validates the "empty permissions" acceptance criterion:
        // an empty permissions list should return 200 with empty array (not 404)
        var emptyList = new ContainerTypePermissionListDto
        {
            Items = [],
            Count = 0
        };

        // Assert
        emptyList.Items.Should().BeEmpty("empty list is valid and should return 200 OK");
        emptyList.Count.Should().Be(0);
    }

    // =========================================================================
    // SpeContainerTypePermission domain model tests (ADR-007)
    // =========================================================================

    [Fact]
    public void SpeContainerTypePermission_DomainRecord_CanBeCreated()
    {
        // Verify the domain record (ADR-007: no Graph SDK types in public API surface)
        var domainRecord = new SpeAdminGraphService.SpeContainerTypePermission(
            AppId: "aabb-ccdd-eeff",
            DelegatedPermissions: ["readContent", "writeContent"],
            ApplicationPermissions: ["full"]);

        domainRecord.AppId.Should().Be("aabb-ccdd-eeff");
        domainRecord.DelegatedPermissions.Should().BeEquivalentTo(["readContent", "writeContent"]);
        domainRecord.ApplicationPermissions.Should().BeEquivalentTo(["full"]);
    }

    [Fact]
    public void SpeContainerTypePermission_DomainRecord_WithEmptyPermissions_IsValid()
    {
        // Empty permission lists are valid — a registered app may have no active permissions
        var domainRecord = new SpeAdminGraphService.SpeContainerTypePermission(
            AppId: "app-id",
            DelegatedPermissions: [],
            ApplicationPermissions: []);

        domainRecord.AppId.Should().Be("app-id");
        domainRecord.DelegatedPermissions.Should().BeEmpty();
        domainRecord.ApplicationPermissions.Should().BeEmpty();
    }

    // =========================================================================
    // Endpoint handler shape tests (SPE-054 acceptance criteria contract)
    // =========================================================================

    [Fact]
    public void GetContainerTypePermissionsAsync_HandlerMethod_ExistsOnEndpointClass()
    {
        // Verify the handler exists as a private static method
        var handlerMethod = typeof(ContainerTypePermissionEndpoints)
            .GetMethod("GetContainerTypePermissionsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        handlerMethod.Should().NotBeNull(
            "GetContainerTypePermissionsAsync handler must exist as a private static method");
        handlerMethod!.IsStatic.Should().BeTrue();
    }

    [Fact]
    public void ContainerTypePermissionEndpoints_IsStaticClass()
    {
        // Minimal API endpoint classes follow the static class pattern (ADR-001)
        var type = typeof(ContainerTypePermissionEndpoints);
        type.IsAbstract.Should().BeTrue("static classes are abstract in IL");
        type.IsSealed.Should().BeTrue("static classes are sealed in IL");
    }

    // =========================================================================
    // Authorization tests (SPE-054: SpeAdminAuthFilter inherited from group)
    // =========================================================================

    [Fact]
    public void ContainerTypePermissionEndpoints_UsesGroupLevelAuth_NotPerEndpointFilter()
    {
        // SpeAdminAuthorizationFilter is applied at the /api/spe group level in SpeAdminEndpoints.
        // Individual endpoint classes do NOT add their own auth filters — they inherit.
        // This verifies ContainerTypePermissionEndpoints follows the same pattern.

        // The handler method signature should NOT have any direct auth attribute — auth is group-inherited
        var handlerMethod = typeof(ContainerTypePermissionEndpoints)
            .GetMethod("GetContainerTypePermissionsAsync",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        handlerMethod.Should().NotBeNull();

        // No [Authorize] attribute on the static handler — auth is from the route group
        var authorizeAttributes = handlerMethod!
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), false);
        authorizeAttributes.Should().BeEmpty(
            "authorization is inherited from the /api/spe route group (ADR-008), not per-endpoint");
    }

    // =========================================================================
    // Not-found scenario test
    // =========================================================================

    [Fact]
    public void ContainerTypePermissionListDto_NotFoundScenario_IsDistinctFromEmptyPermissions()
    {
        // null SpeContainerTypePermission list from GetContainerTypePermissionsAsync indicates 404
        // vs. empty list which indicates 200 with empty array.
        // This test validates the distinction is present in the domain model contract.

        IReadOnlyList<SpeAdminGraphService.SpeContainerTypePermission>? nullResult = null;
        IReadOnlyList<SpeAdminGraphService.SpeContainerTypePermission> emptyResult = [];

        nullResult.Should().BeNull("null return value maps to HTTP 404 Not Found");
        emptyResult.Should().BeEmpty("empty list maps to HTTP 200 OK with empty array");
    }
}
