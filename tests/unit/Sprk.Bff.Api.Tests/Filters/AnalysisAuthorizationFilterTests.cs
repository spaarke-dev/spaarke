using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;
using AuthorizationResult = Sprk.Bff.Api.Services.Ai.AuthorizationResult;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// Unit tests for AnalysisAuthorizationFilter - authorization for Analysis endpoints.
/// </summary>
[Trait("status", "repaired")]
public class AnalysisAuthorizationFilterTests
{
    private readonly Mock<IAiAuthorizationService> _authServiceMock;
    private readonly Mock<ILogger<AnalysisAuthorizationFilter>> _loggerMock;

    public AnalysisAuthorizationFilterTests()
    {
        _authServiceMock = new Mock<IAiAuthorizationService>();
        _loggerMock = new Mock<ILogger<AnalysisAuthorizationFilter>>();
    }

    private AnalysisAuthorizationFilter CreateFilter(AuthorizationMode mode) =>
        new(_authServiceMock.Object, _loggerMock.Object, mode);

    private static AuthorizationResult AllowedResult(params Guid[] documentIds) =>
        AuthorizationResult.Authorized(documentIds.Length > 0 ? documentIds : new[] { Guid.NewGuid() });

    private static AuthorizationResult DeniedResult(string reason = "NO_ACCESS") =>
        AuthorizationResult.Denied(reason);

    /// <summary>
    /// A realistic authenticated caller: the supplied id is issued as the Entra <c>oid</c>, and a
    /// DIVERGENT, sub-shaped <see cref="ClaimTypes.NameIdentifier"/> is issued alongside it.
    /// </summary>
    /// <remarks>
    /// This helper used to mint <see cref="ClaimTypes.NameIdentifier"/> ONLY — a principal shape no
    /// Entra caller ever has, since a real token always carries <c>oid</c> and routes <c>sub</c> to
    /// NameIdentifier under inbound claim mapping. Because the filter read NameIdentifier, the tests
    /// passed; because the stub keyed on the same string, they could not tell a correct read from a
    /// broken one. The divergent value is load-bearing: if the resolver ever falls back to
    /// NameIdentifier again, it returns SubClaim, the stub does not match, and these tests fail.
    /// </remarks>
    private static ClaimsPrincipal CreateUser(string userId = "9d4f7a12-6c3b-4e58-b0d1-2a7f5e9c4813")
    {
        var claims = new List<Claim>
        {
            new("oid", userId),
            new(ClaimTypes.NameIdentifier, SubClaim)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    /// <summary>Entra's pairwise <c>sub</c> — never a GUID, never a systemuser match.</summary>
    private const string SubClaim = "d12L59FRq8kZ0m2Xr7bTn4wPqLzYhVcJ8sNdEuRkjg";

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static Mock<EndpointFilterInvocationContext> CreateContext(
        ClaimsPrincipal user,
        Dictionary<string, object?>? routeValues = null,
        params object[] arguments)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id"
        };

        // Add route values if provided
        if (routeValues != null)
        {
            foreach (var kvp in routeValues)
            {
                httpContext.Request.RouteValues[kvp.Key] = kvp.Value;
            }
        }

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);
        contextMock.Setup(c => c.Arguments).Returns(arguments.ToList()!);

        return contextMock;
    }

    private static ValueTask<object?> NextDelegate(EndpointFilterInvocationContext context)
        => ValueTask.FromResult<object?>(Results.Ok("Success"));

    #region Authentication Tests

    [Fact]
    public async Task InvokeAsync_NoUserIdentity_Returns401()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var request = new AnalysisExecuteRequest { DocumentIds = [Guid.NewGuid()], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateAnonymousUser(), arguments: request);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(401);
    }

    [Fact]
    public async Task InvokeAsync_AnalysisMode_NoUserIdentity_Returns401()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var routeValues = new Dictionary<string, object?> { ["analysisId"] = Guid.NewGuid().ToString() };
        var context = CreateContext(CreateAnonymousUser(), routeValues);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(401);
    }

    #endregion

    #region DocumentAccess Mode Tests


    [Fact]
    public async Task DocumentAccess_UserWithoutAccess_Returns403()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [documentId], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeniedResult());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task DocumentAccess_NoDocumentsInRequest_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var request = new AnalysisExecuteRequest { DocumentIds = [], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }


    [Fact]
    public async Task DocumentAccess_MultipleDocumentsPartialAccess_Returns403()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [docId1, docId2], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        // Partial authorization - only docId1 is authorized
        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Partial(new[] { docId1 }, "Access denied to some documents"));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }


    [Fact]
    public async Task DocumentAccess_EmptyGuidArgument_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var context = CreateContext(CreateUser(), arguments: Guid.Empty);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }

    #endregion

    #region AnalysisAccess Mode Tests


    [Fact]
    public async Task AnalysisAccess_MissingAnalysisId_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var context = CreateContext(CreateUser()); // No route values

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task AnalysisAccess_InvalidAnalysisIdFormat_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var routeValues = new Dictionary<string, object?> { ["analysisId"] = "not-a-guid" };
        var context = CreateContext(CreateUser(), routeValues);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }


    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task DocumentAccess_AuthorizationThrows_Returns500()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [documentId], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Constructor Tests


    #endregion
}

/// <summary>
/// Tests for AuthorizationMode enum.
/// </summary>
public class AuthorizationModeTests
{
    [Fact]
    public void AuthorizationMode_HasExpectedValues()
    {
        // Assert
        ((int)AuthorizationMode.DocumentAccess).Should().Be(0);
        ((int)AuthorizationMode.AnalysisAccess).Should().Be(1);
    }
}
