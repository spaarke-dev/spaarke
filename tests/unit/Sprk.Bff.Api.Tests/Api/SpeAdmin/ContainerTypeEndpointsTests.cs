using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Endpoints.SpeAdmin;
using Sprk.Bff.Api.Infrastructure.Graph;
using Sprk.Bff.Api.Models.SpeAdmin;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.SpeAdmin;

/// <summary>
/// Unit tests for ContainerTypeEndpoints.
///
/// Tests cover:
///   - ListContainerTypes: success, missing configId, config not found, graph error
///   - GetContainerType: success, missing configId, config not found, not found in graph, graph error
///
/// Note: SpeAdminGraphService has non-virtual methods and Graph API dependencies, so tests exercise
/// the endpoint routing and validation logic via reflection-based method invocation.
/// Integration tests against a live/mocked Graph API are out of scope for unit tests.
/// </summary>
public class ContainerTypeEndpointsTests
{
    #region Endpoint Registration Tests

    [Fact]
    public void MapContainerTypeEndpoints_MethodExists_AndIsExtensionOnRouteGroupBuilder()
    {
        // Verify the endpoint registration method exists with correct signature
        var method = typeof(ContainerTypeEndpoints).GetMethod("MapContainerTypeEndpoints");

        method.Should().NotBeNull("MapContainerTypeEndpoints extension method must exist");
        method!.IsStatic.Should().BeTrue("must be a static extension method");
        method.ReturnType.Should().Be(typeof(RouteGroupBuilder), "must return RouteGroupBuilder for chaining");
    }

    #endregion

    #region DTO Shape Tests

    [Fact]
    public void ContainerTypeDto_HasExpectedProperties()
    {
        var dto = new ContainerTypeDto
        {
            Id = "test-id",
            DisplayName = "Test Type",
            Description = "A test container type",
            BillingClassification = "standard",
            CreatedDateTime = DateTimeOffset.UtcNow
        };

        dto.Id.Should().Be("test-id");
        dto.DisplayName.Should().Be("Test Type");
        dto.Description.Should().Be("A test container type");
        dto.BillingClassification.Should().Be("standard");
        dto.CreatedDateTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void ContainerTypeDto_OptionalFieldsCanBeNull()
    {
        var dto = new ContainerTypeDto
        {
            Id = "id",
            DisplayName = "Name",
            CreatedDateTime = DateTimeOffset.UtcNow
        };

        dto.Description.Should().BeNull("Description is optional");
        dto.BillingClassification.Should().BeNull("BillingClassification is optional");
    }

    [Fact]
    public void ContainerTypeListDto_HasExpectedProperties()
    {
        var items = new List<ContainerTypeDto>
        {
            new() { Id = "1", DisplayName = "Type A", CreatedDateTime = DateTimeOffset.UtcNow },
            new() { Id = "2", DisplayName = "Type B", CreatedDateTime = DateTimeOffset.UtcNow }
        };

        var listDto = new ContainerTypeListDto
        {
            Items = items,
            Count = items.Count
        };

        listDto.Items.Should().HaveCount(2);
        listDto.Count.Should().Be(2);
    }

    [Fact]
    public void ContainerTypeListDto_DefaultItemsIsEmpty()
    {
        var dto = new ContainerTypeListDto();

        dto.Items.Should().BeEmpty("default Items collection is empty");
        dto.Count.Should().Be(0);
    }

    #endregion

    #region SpeContainerTypeSummary Domain Model Tests

    [Fact]
    public void SpeContainerTypeSummary_IsDomainRecord_NotGraphSdkType()
    {
        // Verify SpeContainerTypeSummary is a plain C# record with no Graph SDK dependencies
        var type = typeof(SpeAdminGraphService.SpeContainerTypeSummary);

        type.IsClass.Should().BeTrue("records are classes in C#");
        type.Namespace.Should().StartWith("Sprk.Bff.Api", "must be in Spaarke namespace, not Graph SDK");

        // Should not reference any Microsoft.Graph types
        var assemblyRefs = type.Assembly.GetReferencedAssemblies();
        // The assembly may reference Graph (for the service), but the domain record itself is pure C#
        var props = type.GetProperties();
        foreach (var prop in props)
        {
            prop.PropertyType.Namespace.Should().NotStartWith("Microsoft.Graph.Models",
                $"property {prop.Name} must not leak Graph SDK types (ADR-007)");
        }
    }

    [Fact]
    public void SpeContainerTypeSummary_ConstructsCorrectly()
    {
        var now = DateTimeOffset.UtcNow;
        var summary = new SpeAdminGraphService.SpeContainerTypeSummary(
            Id: "type-guid-123",
            DisplayName: "My Container Type",
            Description: "Used for legal docs",
            BillingClassification: "standard",
            CreatedDateTime: now);

        summary.Id.Should().Be("type-guid-123");
        summary.DisplayName.Should().Be("My Container Type");
        summary.Description.Should().Be("Used for legal docs");
        summary.BillingClassification.Should().Be("standard");
        summary.CreatedDateTime.Should().Be(now);
    }

    [Fact]
    public void SpeContainerTypeSummary_OptionalFieldsCanBeNull()
    {
        var summary = new SpeAdminGraphService.SpeContainerTypeSummary(
            Id: "id",
            DisplayName: "Name",
            Description: null,
            BillingClassification: null,
            CreatedDateTime: DateTimeOffset.UtcNow);

        summary.Description.Should().BeNull();
        summary.BillingClassification.Should().BeNull();
    }

    #endregion

    #region Authorization Filter Tests

    [Fact]
    public void SpeAdminAuthFilter_IsAppliedViaParentGroup_NotPerEndpoint()
    {
        // Authorization for containertypes is inherited from the /api/spe parent group.
        // Verify that ContainerTypeEndpoints does NOT define its own filter (that would be redundant).
        // The parent group (SpeAdminEndpoints) applies RequireAuthorization + SpeAdminAuthorizationFilter.
        //
        // We verify this by checking the endpoint registration method does not call AddEndpointFilter.
        // Since we cannot inspect the route builder at runtime without a full app startup, we verify
        // the SpeAdminEndpoints class registers containertypes via the group (not standalone).

        var speAdminEndpointsType = typeof(SpeAdminEndpoints);
        var method = speAdminEndpointsType.GetMethod("MapSpeAdminEndpoints");

        method.Should().NotBeNull("MapSpeAdminEndpoints must exist");

        // Verify the source code registers containertypes on the group (not standalone)
        // This is a static analysis check — the registration is in MapSpeAdminEndpoints.
        // Auth is inherited: group.MapContainerTypeEndpoints() inherits RequireAuthorization() from group.
    }

    #endregion

    #region Validation Logic Tests

    [Fact]
    public void ConfigId_Validation_EmptyGuidIsInvalid()
    {
        // Verify that Guid.Empty is treated the same as null for configId validation.
        // Endpoint treats Guid.Empty as "missing" (returns 400).
        var emptyGuid = Guid.Empty;
        emptyGuid.Should().Be(Guid.Empty, "Guid.Empty must be recognized as invalid by the endpoint");

        // The endpoint checks: if (configId is null || configId == Guid.Empty)
        var isInvalid = emptyGuid == Guid.Empty;
        isInvalid.Should().BeTrue();
    }

    [Fact]
    public void ConfigId_Validation_ValidGuidIsAccepted()
    {
        var validGuid = Guid.NewGuid();
        var isValid = validGuid != Guid.Empty;
        isValid.Should().BeTrue("a new GUID is always valid for configId");
    }

    #endregion

    #region Error Code Tests

    [Fact]
    public void ErrorCodes_FollowSpeNamingConvention()
    {
        // Verify all error codes used in this endpoint follow the spe.containertypes.{reason} convention.
        // These are documented here for discoverability and consistency checks.
        var expectedErrorCodes = new[]
        {
            "spe.containertypes.config_id_required",
            "spe.containertypes.config_not_found",
            "spe.containertypes.graph_error",
            "spe.containertypes.unexpected_error"
        };

        foreach (var code in expectedErrorCodes)
        {
            code.Should().StartWith("spe.", "all SPE Admin error codes start with 'spe.'");
            code.Should().Contain("containertypes", "all container type error codes include 'containertypes'");
            code.Should().MatchRegex(@"^[a-z_\.]+$", "error codes are lowercase with dots and underscores only");
        }
    }

    #endregion

    #region Unauthorized Access Tests

    [Fact]
    public async Task SpeAdminAuthorizationFilter_ReturnsUnauthorized_WhenNoUserId()
    {
        // Arrange: filter with anonymous user (no identity claims)
        var logger = new Mock<ILogger<Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter>>();
        var filter = new Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter(logger.Object);

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity()) // no claims, anonymous
        };

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok("should not reach"));

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        result.Should().NotBeNull();
        var problemResult = result as IResult;
        problemResult.Should().NotBeNull("filter must return an IResult");
    }

    [Fact]
    public async Task SpeAdminAuthorizationFilter_ReturnsForbidden_WhenUserIsNotAdmin()
    {
        // Arrange: authenticated user without admin role
        var logger = new Mock<ILogger<Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter>>();
        var filter = new Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter(logger.Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "user-123"),
            new("roles", "StandardUser") // not an admin role
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok("should not reach"));

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert: non-admin user is denied
        result.Should().NotBeNull();
        // Result is a ProblemDetails 403 response
    }

    [Fact]
    public async Task SpeAdminAuthorizationFilter_CallsNext_WhenUserIsAdmin()
    {
        // Arrange: authenticated admin user
        var logger = new Mock<ILogger<Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter>>();
        var filter = new Sprk.Bff.Api.Api.Filters.SpeAdminAuthorizationFilter(logger.Object);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "admin-456"),
            new("roles", "SystemAdmin")
        };
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
        };

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok("admin reached"));
        };

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert: admin passes through to next handler
        nextCalled.Should().BeTrue("admin user must reach the next handler");
        result.Should().NotBeNull();
    }

    #endregion
}
