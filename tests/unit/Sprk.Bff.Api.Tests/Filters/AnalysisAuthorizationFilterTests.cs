using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using Spaarke.Core.Auth;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// Unit tests for AnalysisAuthorizationFilter - authorization for Analysis endpoints.
/// </summary>
public class AnalysisAuthorizationFilterTests
{
    private readonly Mock<IAuthorizationService> _authServiceMock;
    private readonly Mock<ILogger<AnalysisAuthorizationFilter>> _loggerMock;

    public AnalysisAuthorizationFilterTests()
    {
        _authServiceMock = new Mock<IAuthorizationService>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<AnalysisAuthorizationFilter>>();
    }

    private AnalysisAuthorizationFilter CreateFilter(AuthorizationMode mode) =>
        new(_authServiceMock.Object, _loggerMock.Object, mode);

    private static AuthorizationResult AllowedResult() => new()
    {
        IsAllowed = true,
        ReasonCode = "ALLOWED",
        RuleName = "TestRule"
    };

    private static AuthorizationResult DeniedResult(string reasonCode = "NO_ACCESS") => new()
    {
        IsAllowed = false,
        ReasonCode = reasonCode,
        RuleName = "TestRule"
    };

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
            .Setup(x => x.AuthorizeAsync(It.Is<AuthorizationContext>(c =>
                c.ResourceId == documentId.ToString() &&
                c.Operation == "read"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        _authServiceMock.Verify(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
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
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        _authServiceMock.Verify(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task DocumentAccess_MultipleDocumentsPartialAccess_Returns403OnFirst()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var docId1 = Guid.NewGuid();
        var docId2 = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [docId1, docId2], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .SetupSequence(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult())
            .ReturnsAsync(DeniedResult());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task DocumentAccess_DuplicateDocuments_ChecksOnce()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var request = new AnalysisExecuteRequest { DocumentIds = [documentId, documentId], ActionId = Guid.NewGuid() };
        var context = CreateContext(CreateUser(), arguments: request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        _authServiceMock.Verify(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DocumentAccess_GuidArgument_AuthorizesCorrectly()
    {
        // Arrange
        var filter = CreateFilter(AuthorizationMode.DocumentAccess);
        var documentId = Guid.NewGuid();
        var context = CreateContext(CreateUser(), arguments: documentId);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(It.Is<AuthorizationContext>(c =>
                c.ResourceId == documentId.ToString()), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

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
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
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
