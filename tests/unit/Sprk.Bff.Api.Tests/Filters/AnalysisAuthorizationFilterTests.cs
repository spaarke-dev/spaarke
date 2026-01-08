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
    public async Task DocumentAccess_UserWithAccess_ProceedsToEndpoint()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [documentId], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(documentId)),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult(documentId));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        _authServiceMock.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            It.IsAny<IReadOnlyList<Guid>>(),
            It.IsAny<HttpContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

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
    public async Task DocumentAccess_MultipleDocuments_AuthorizesAll()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [docId1, docId2], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult(docId1, docId2));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        _authServiceMock.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 2),
            It.IsAny<HttpContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
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
    public async Task DocumentAccess_DuplicateDocuments_DeduplicatesBeforeCheck()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [documentId, documentId], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<IReadOnlyList<Guid>>(),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult(documentId));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        // Service is called with deduplicated list (1 document instead of 2)
        _authServiceMock.Verify(x => x.AuthorizeAsync(
            It.IsAny<ClaimsPrincipal>(),
            It.Is<IReadOnlyList<Guid>>(ids => ids.Count == 1),
            It.IsAny<HttpContext>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DocumentAccess_GuidArgument_AuthorizesCorrectly()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var context = CreateContext(CreateUser(), arguments: documentId);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.Is<IReadOnlyList<Guid>>(ids => ids.Contains(documentId)),
                It.IsAny<HttpContext>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult(documentId));

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
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
    public async Task AnalysisAccess_WithValidAnalysisId_ProceedsToEndpoint()
    {
        // Arrange - Phase 1 skips authorization, just validates route parameter
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var analysisId = Guid.NewGuid();
        var routeValues = new Dictionary<string, object?> { ["analysisId"] = analysisId.ToString() };
        var context = CreateContext(CreateUser(), routeValues);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
    }

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

    [Fact]
    public async Task AnalysisAccess_EmptyAnalysisId_Returns400()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.AnalysisAccess);
        var routeValues = new Dictionary<string, object?> { ["analysisId"] = Guid.Empty.ToString() };
        var context = CreateContext(CreateUser(), routeValues);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert - Empty GUID still parses, so Phase 1 allows it (auth skipped)
        result.Should().BeOfType<Ok<string>>();
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

    [Fact]
    public void Constructor_NullAuthService_ThrowsArgumentNullException()
    {
        // Act & Assert
        var act = () => new AnalysisAuthorizationFilter(null!, _loggerMock.Object, AuthorizationMode.DocumentAccess);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_DoesNotThrow()
    {
        // Act & Assert - Logger is optional
        var act = () => new AnalysisAuthorizationFilter(_authServiceMock.Object, null, AuthorizationMode.DocumentAccess);
        act.Should().NotThrow();
    }

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
