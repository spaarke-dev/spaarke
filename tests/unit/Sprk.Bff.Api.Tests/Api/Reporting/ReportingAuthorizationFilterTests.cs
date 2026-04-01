using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Reporting;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Reporting;

/// <summary>
/// Unit tests for <see cref="ReportingAuthorizationFilter"/>.
///
/// Tests the three sequential authorization checks performed by the filter:
///   1. Module gate (Reporting:ModuleEnabled config key) → 404 when disabled/missing
///   2. User authentication → 401 when unauthenticated
///   3. Security role (sprk_ReportingAccess) → 403 when role absent
///   4. Privilege extraction (Viewer / Author / Admin) → stored in HttpContext.Items
/// </summary>
public class ReportingAuthorizationFilterTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static IConfiguration BuildConfig(bool? moduleEnabled)
    {
        var values = new Dictionary<string, string?>();

        if (moduleEnabled.HasValue)
            values[ReportingAuthorizationFilter.ModuleEnabledConfigKey] = moduleEnabled.Value.ToString();

        return new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
    }

    private static DefaultHttpContext BuildAuthenticatedHttpContext(
        string userId = "user-001",
        params string[] roles)
    {
        var claims = new List<Claim>
        {
            new("oid", userId),
        };

        foreach (var role in roles)
            claims.Add(new Claim("roles", role));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        var principal = new ClaimsPrincipal(identity);

        return new DefaultHttpContext { User = principal };
    }

    private static DefaultHttpContext BuildUnauthenticatedHttpContext()
    {
        // No authentication type → IsAuthenticated = false
        var identity = new ClaimsIdentity();
        var principal = new ClaimsPrincipal(identity);
        return new DefaultHttpContext { User = principal };
    }

    /// <summary>
    /// Creates a minimal <see cref="EndpointFilterInvocationContext"/> around the supplied <see cref="HttpContext"/>.
    /// We use a mock to avoid the internal constructor requirements of the real type.
    /// </summary>
    private static EndpointFilterInvocationContext BuildFilterContext(HttpContext httpContext)
    {
        var mock = new Mock<EndpointFilterInvocationContext>();
        mock.Setup(c => c.HttpContext).Returns(httpContext);
        return mock.Object;
    }

    private static ReportingAuthorizationFilter BuildFilter(IConfiguration config)
    {
        return new ReportingAuthorizationFilter(config, logger: null);
    }

    // =========================================================================
    // Check 1: Module Gate
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_ReturnsNotFound_WhenModuleDisabledByConfig()
    {
        // Arrange
        var config = BuildConfig(moduleEnabled: false);
        var filter = BuildFilter(config);
        var httpContext = BuildAuthenticatedHttpContext(roles: ReportingAuthorizationFilter.ReportingAccessRole);
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeFalse("next should not be called when module is disabled");
        result.Should().BeAssignableTo<IResult>();

        var problemResult = result as IResult;
        problemResult.Should().NotBeNull();

        // Execute the result and verify status code
        var fakeHttpContext = new DefaultHttpContext();
        fakeHttpContext.Response.Body = new System.IO.MemoryStream();
        await problemResult!.ExecuteAsync(fakeHttpContext);
        fakeHttpContext.Response.StatusCode.Should().Be(404);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsNotFound_WhenModuleEnabledKeyMissing()
    {
        // Arrange — config has no Reporting:ModuleEnabled key at all
        var config = BuildConfig(moduleEnabled: null);
        var filter = BuildFilter(config);
        var httpContext = BuildAuthenticatedHttpContext(roles: ReportingAuthorizationFilter.ReportingAccessRole);
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeFalse();

        var fakeHttpContext = new DefaultHttpContext();
        fakeHttpContext.Response.Body = new System.IO.MemoryStream();
        await ((IResult)result!).ExecuteAsync(fakeHttpContext);
        fakeHttpContext.Response.StatusCode.Should().Be(404);
    }

    // =========================================================================
    // Check 2: Authentication
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenUserIsNotAuthenticated()
    {
        // Arrange
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);
        var httpContext = BuildUnauthenticatedHttpContext();
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeFalse("next should not be called for unauthenticated users");

        var fakeHttpContext = new DefaultHttpContext();
        fakeHttpContext.Response.Body = new System.IO.MemoryStream();
        await ((IResult)result!).ExecuteAsync(fakeHttpContext);
        fakeHttpContext.Response.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_ReturnsUnauthorized_WhenUserHasNoOidClaim()
    {
        // Arrange — authenticated identity but no oid/sub/nameidentifier claim
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);

        // ClaimsIdentity with an auth type but no OID/NameIdentifier claim
        var identity = new ClaimsIdentity(new[] { new Claim("roles", "someRole") }, "TestAuth");
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity)
        };
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeFalse();

        var fakeHttpContext = new DefaultHttpContext();
        fakeHttpContext.Response.Body = new System.IO.MemoryStream();
        await ((IResult)result!).ExecuteAsync(fakeHttpContext);
        fakeHttpContext.Response.StatusCode.Should().Be(401);
    }

    // =========================================================================
    // Check 3: Security Role
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_ReturnsForbidden_WhenUserLacksReportingAccessRole()
    {
        // Arrange — authenticated user, module enabled, but no sprk_ReportingAccess role
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);
        var httpContext = BuildAuthenticatedHttpContext(userId: "user-no-role");
        // No roles added
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeFalse("next should not be called for users without the access role");

        var fakeHttpContext = new DefaultHttpContext();
        fakeHttpContext.Response.Body = new System.IO.MemoryStream();
        await ((IResult)result!).ExecuteAsync(fakeHttpContext);
        fakeHttpContext.Response.StatusCode.Should().Be(403);
    }

    // =========================================================================
    // Check 4: Privilege Level Extraction
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_SetsViewerPrivilege_AndCallsNext_WhenUserHasOnlyAccessRole()
    {
        // Arrange — user has sprk_ReportingAccess but not Author or Admin
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);
        var httpContext = BuildAuthenticatedHttpContext(
            userId: "viewer-user",
            roles: ReportingAuthorizationFilter.ReportingAccessRole);
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeTrue();
        httpContext.Items.Should().ContainKey(ReportingAuthorizationFilter.PrivilegeLevelItemKey);
        httpContext.Items[ReportingAuthorizationFilter.PrivilegeLevelItemKey]
            .Should().Be(ReportingPrivilegeLevel.Viewer);
    }

    [Fact]
    public async Task InvokeAsync_SetsAuthorPrivilege_AndCallsNext_WhenUserHasAuthorRole()
    {
        // Arrange — user has sprk_ReportingAccess + sprk_ReportingAuthor
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);
        var httpContext = BuildAuthenticatedHttpContext(
            userId: "author-user",
            roles:
            [
                ReportingAuthorizationFilter.ReportingAccessRole,
                ReportingAuthorizationFilter.ReportingAuthorRole
            ]);
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeTrue();
        httpContext.Items[ReportingAuthorizationFilter.PrivilegeLevelItemKey]
            .Should().Be(ReportingPrivilegeLevel.Author);
    }

    [Fact]
    public async Task InvokeAsync_SetsAdminPrivilege_AndCallsNext_WhenUserHasAdminRole()
    {
        // Arrange — user has sprk_ReportingAccess + sprk_ReportingAdmin (highest privilege)
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);
        var httpContext = BuildAuthenticatedHttpContext(
            userId: "admin-user",
            roles:
            [
                ReportingAuthorizationFilter.ReportingAccessRole,
                ReportingAuthorizationFilter.ReportingAdminRole
            ]);
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeTrue();
        httpContext.Items[ReportingAuthorizationFilter.PrivilegeLevelItemKey]
            .Should().Be(ReportingPrivilegeLevel.Admin);
    }

    [Fact]
    public async Task InvokeAsync_SetsAdminPrivilege_EvenWhenAllRolesPresent()
    {
        // Arrange — Admin supersedes Author when both roles are present
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);
        var httpContext = BuildAuthenticatedHttpContext(
            userId: "super-user",
            roles:
            [
                ReportingAuthorizationFilter.ReportingAccessRole,
                ReportingAuthorizationFilter.ReportingAuthorRole,
                ReportingAuthorizationFilter.ReportingAdminRole
            ]);
        var filterContext = BuildFilterContext(httpContext);

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok());

        // Act
        await filter.InvokeAsync(filterContext, next);

        // Assert
        httpContext.Items[ReportingAuthorizationFilter.PrivilegeLevelItemKey]
            .Should().Be(ReportingPrivilegeLevel.Admin,
                "Admin is the highest privilege and must supersede Author");
    }

    // =========================================================================
    // Role claim shapes
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_GrantsAccess_WhenRoleIsInClaimTypes_Role()
    {
        // Arrange — role is stored as ClaimTypes.Role rather than "roles"
        var config = BuildConfig(moduleEnabled: true);
        var filter = BuildFilter(config);

        var claims = new List<Claim>
        {
            new("oid", "user-002"),
            new(ClaimTypes.Role, ReportingAuthorizationFilter.ReportingAccessRole)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) };
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(filterContext, next);

        // Assert
        nextCalled.Should().BeTrue("ClaimTypes.Role should be accepted as a valid role claim shape");
    }

    // =========================================================================
    // Configuration — logger is optional
    // =========================================================================

    [Fact]
    public async Task InvokeAsync_WorksWithoutLogger()
    {
        // Arrange — logger is null (optional parameter)
        var config = BuildConfig(moduleEnabled: true);
        var filter = new ReportingAuthorizationFilter(config, logger: null);
        var httpContext = BuildAuthenticatedHttpContext(
            userId: "user-003",
            roles: ReportingAuthorizationFilter.ReportingAccessRole);
        var filterContext = BuildFilterContext(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var act = async () => await filter.InvokeAsync(filterContext, next);

        // Assert
        await act.Should().NotThrowAsync();
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_WorksWithLogger()
    {
        // Arrange — logger is provided
        var config = BuildConfig(moduleEnabled: true);
        var logger = new Mock<ILogger<ReportingAuthorizationFilter>>();
        var filter = new ReportingAuthorizationFilter(config, logger.Object);
        var httpContext = BuildAuthenticatedHttpContext(
            userId: "user-004",
            roles: ReportingAuthorizationFilter.ReportingAccessRole);
        var filterContext = BuildFilterContext(httpContext);

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok());

        // Act
        var act = async () => await filter.InvokeAsync(filterContext, next);

        // Assert — no exceptions regardless of log calls
        await act.Should().NotThrowAsync();
    }

    // =========================================================================
    // Guard clause
    // =========================================================================

    [Fact]
    public void Constructor_ThrowsArgumentNullException_WhenConfigurationIsNull()
    {
        // Arrange / Act
        var act = () => new ReportingAuthorizationFilter(null!, logger: null);

        // Assert
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    // =========================================================================
    // Constant verification
    // =========================================================================

    [Fact]
    public void ReportingAccessRole_HasExpectedValue()
    {
        ReportingAuthorizationFilter.ReportingAccessRole.Should().Be("sprk_ReportingAccess");
    }

    [Fact]
    public void ReportingAuthorRole_HasExpectedValue()
    {
        ReportingAuthorizationFilter.ReportingAuthorRole.Should().Be("sprk_ReportingAuthor");
    }

    [Fact]
    public void ReportingAdminRole_HasExpectedValue()
    {
        ReportingAuthorizationFilter.ReportingAdminRole.Should().Be("sprk_ReportingAdmin");
    }

    [Fact]
    public void ModuleEnabledConfigKey_HasExpectedValue()
    {
        ReportingAuthorizationFilter.ModuleEnabledConfigKey.Should().Be("Reporting:ModuleEnabled");
    }

    [Fact]
    public void PrivilegeLevelItemKey_HasExpectedValue()
    {
        ReportingAuthorizationFilter.PrivilegeLevelItemKey.Should().Be("ReportingPrivilegeLevel");
    }
}
