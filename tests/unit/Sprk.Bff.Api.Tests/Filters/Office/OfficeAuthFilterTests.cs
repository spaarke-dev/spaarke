using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters.Office;

/// <summary>
/// Unit tests for <see cref="OfficeAuthFilter"/>.
/// Tests authentication validation for Office endpoints.
/// </summary>
public class OfficeAuthFilterTests
{
    private readonly Mock<ILogger<OfficeAuthFilter>> _loggerMock;
    private readonly OfficeAuthFilter _sut;

    public OfficeAuthFilterTests()
    {
        _loggerMock = new Mock<ILogger<OfficeAuthFilter>>();
        _sut = new OfficeAuthFilter(_loggerMock.Object);
    }

    #region Helper Methods

    private static ClaimsPrincipal CreateAuthenticatedUser(
        string? oid = null,
        string? tenantId = null,
        string? email = null)
    {
        var claims = new List<Claim>();

        if (oid != null)
            claims.Add(new Claim("oid", oid));

        if (tenantId != null)
            claims.Add(new Claim("tid", tenantId));

        if (email != null)
            claims.Add(new Claim(ClaimTypes.Email, email));

        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static Mock<EndpointFilterInvocationContext> CreateContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id"
        };

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);
        contextMock.Setup(c => c.Arguments).Returns(new List<object?>());

        return contextMock;
    }

    private static ValueTask<object?> NextDelegate(EndpointFilterInvocationContext context)
        => ValueTask.FromResult<object?>(Results.Ok("Success"));

    #endregion

    #region Authentication Tests

    [Fact]
    public async Task InvokeAsync_UnauthenticatedUser_Returns401()
    {
        // Arrange
        var context = CreateContext(CreateAnonymousUser());

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(401);
        problemResult.ProblemDetails.Extensions.Should().ContainKey("errorCode");
        problemResult.ProblemDetails.Extensions["errorCode"].Should().Be("OFFICE_AUTH_001");
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUserWithOid_Succeeds()
    {
        // Arrange
        var userId = "user-oid-123";
        var context = CreateContext(CreateAuthenticatedUser(oid: userId));

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        var httpContext = context.Object.HttpContext;
        httpContext.Items[OfficeAuthFilter.UserIdKey].Should().Be(userId);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUserWithAlternativeOidClaim_Succeeds()
    {
        // Arrange
        var userId = "user-alt-oid-456";
        var claims = new List<Claim>
        {
            new("http://schemas.microsoft.com/identity/claims/objectidentifier", userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        context.Object.HttpContext.Items[OfficeAuthFilter.UserIdKey].Should().Be(userId);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUserWithNameIdentifier_Succeeds()
    {
        // Arrange
        var userId = "user-name-id-789";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        context.Object.HttpContext.Items[OfficeAuthFilter.UserIdKey].Should().Be(userId);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUserWithSubClaim_Succeeds()
    {
        // Arrange
        var userId = "user-sub-012";
        var claims = new List<Claim>
        {
            new("sub", userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        context.Object.HttpContext.Items[OfficeAuthFilter.UserIdKey].Should().Be(userId);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUserWithoutAnyIdentifier_Returns401()
    {
        // Arrange
        var claims = new List<Claim>
        {
            new(ClaimTypes.Email, "user@test.com") // Email but no identifier
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(401);
        problemResult.ProblemDetails.Extensions["errorCode"].Should().Be("OFFICE_AUTH_002");
    }

    #endregion

    #region Tenant and Email Extraction Tests

    [Fact]
    public async Task InvokeAsync_WithTenantId_StoresTenantInContext()
    {
        // Arrange
        var userId = "user-123";
        var tenantId = "tenant-456";
        var context = CreateContext(CreateAuthenticatedUser(oid: userId, tenantId: tenantId));

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        var httpContext = context.Object.HttpContext;
        httpContext.Items[OfficeAuthFilter.TenantIdKey].Should().Be(tenantId);
    }

    [Fact]
    public async Task InvokeAsync_WithAlternativeTenantIdClaim_StoresTenantInContext()
    {
        // Arrange
        var userId = "user-123";
        var tenantId = "tenant-789";
        var claims = new List<Claim>
        {
            new("oid", userId),
            new("http://schemas.microsoft.com/identity/claims/tenantid", tenantId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user);

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        context.Object.HttpContext.Items[OfficeAuthFilter.TenantIdKey].Should().Be(tenantId);
    }

    [Fact]
    public async Task InvokeAsync_WithEmail_StoresEmailInContext()
    {
        // Arrange
        var userId = "user-123";
        var email = "user@test.com";
        var context = CreateContext(CreateAuthenticatedUser(oid: userId, email: email));

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        context.Object.HttpContext.Items[OfficeAuthFilter.UserEmailKey].Should().Be(email);
    }

    [Fact]
    public async Task InvokeAsync_WithPreferredUsername_StoresEmailInContext()
    {
        // Arrange
        var userId = "user-123";
        var preferredUsername = "user@company.com";
        var claims = new List<Claim>
        {
            new("oid", userId),
            new("preferred_username", preferredUsername)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user);

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        context.Object.HttpContext.Items[OfficeAuthFilter.UserEmailKey].Should().Be(preferredUsername);
    }

    [Fact]
    public async Task InvokeAsync_WithoutTenant_DoesNotStoreTenantKey()
    {
        // Arrange
        var userId = "user-123";
        var context = CreateContext(CreateAuthenticatedUser(oid: userId));

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        context.Object.HttpContext.Items.Should().NotContainKey(OfficeAuthFilter.TenantIdKey);
    }

    [Fact]
    public async Task InvokeAsync_WithoutEmail_DoesNotStoreEmailKey()
    {
        // Arrange
        var userId = "user-123";
        var context = CreateContext(CreateAuthenticatedUser(oid: userId));

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        context.Object.HttpContext.Items.Should().NotContainKey(OfficeAuthFilter.UserEmailKey);
    }

    #endregion

    #region Claim Priority Tests

    [Fact]
    public async Task InvokeAsync_WithMultipleIdClaims_PrefersOid()
    {
        // Arrange
        var oid = "oid-priority";
        var nameId = "name-id-lower-priority";
        var sub = "sub-lowest-priority";
        var claims = new List<Claim>
        {
            new("sub", sub),
            new(ClaimTypes.NameIdentifier, nameId),
            new("oid", oid)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);
        var context = CreateContext(user);

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        context.Object.HttpContext.Items[OfficeAuthFilter.UserIdKey].Should().Be(oid);
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task InvokeAsync_UnauthenticatedUser_LogsWarning()
    {
        // Arrange
        var context = CreateContext(CreateAnonymousUser());

        // Act
        await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("not authenticated")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}
