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

    private static ClaimsPrincipal CreateUser(string userId = "user-123")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

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
