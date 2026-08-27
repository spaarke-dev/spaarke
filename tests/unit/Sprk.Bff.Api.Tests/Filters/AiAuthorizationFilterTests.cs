using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;
using AuthorizationResult = Sprk.Bff.Api.Services.Ai.AuthorizationResult;

namespace Sprk.Bff.Api.Tests.Filters;

[Trait("status", "repaired")]
public class AiAuthorizationFilterTests
{
    private readonly Mock<IAiAuthorizationService> _authServiceMock;
    private readonly Mock<ILogger<AiAuthorizationFilter>> _loggerMock;
    private readonly AiAuthorizationFilter _filter;

    public AiAuthorizationFilterTests()
    {
        _authServiceMock = new Mock<IAiAuthorizationService>();
        _loggerMock = new Mock<ILogger<AiAuthorizationFilter>>();

        _filter = new AiAuthorizationFilter(_authServiceMock.Object, _loggerMock.Object);
    }

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
        params object[] arguments)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id",
            RequestServices = new ServiceCollection().AddLogging().BuildServiceProvider()
        };

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
        var context = CreateContext(CreateAnonymousUser(), new DocumentAnalysisRequest(Guid.NewGuid(), "drive", "item"));

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(401);
    }

    #endregion

    #region Single Document Tests


    [Fact]
    public async Task InvokeAsync_UserWithoutAccess_Returns403()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = new DocumentAnalysisRequest(documentId, "drive-id", "item-id");
        var context = CreateContext(CreateUser(), request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeniedResult());

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }


    #endregion

    #region Batch Document Tests


    [Fact]
    public async Task InvokeAsync_BatchWithPartialAccess_Returns403()
    {
        // Arrange
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var requests = new List<DocumentAnalysisRequest>
        {
            new(docId1, "drive-1", "item-1"),
            new(docId2, "drive-2", "item-2")
        };
        var context = CreateContext(CreateUser(), requests);

        // Partial authorization - only docId1 is authorized
        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AuthorizationResult.Partial(new[] { docId1 }, "Access denied to some documents"));

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }


    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task InvokeAsync_AuthorizationThrows_Returns500()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = new DocumentAnalysisRequest(documentId, "drive-id", "item-id");
        var context = CreateContext(CreateUser(), request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database error"));

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(500);
    }

    #endregion

    #region Guid Argument Tests


    #endregion
}
