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

public class AiAuthorizationFilterTests
{
    private readonly Mock<IAuthorizationService> _authServiceMock;
    private readonly Mock<ILogger<AiAuthorizationFilter>> _loggerMock;
    private readonly AiAuthorizationFilter _filter;

    public AiAuthorizationFilterTests()
    {
        _authServiceMock = new Mock<IAuthorizationService>(MockBehavior.Strict);
        _loggerMock = new Mock<ILogger<AiAuthorizationFilter>>();

        _filter = new AiAuthorizationFilter(_authServiceMock.Object, _loggerMock.Object);
    }

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
        params object[] arguments)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id"
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
    public async Task InvokeAsync_UserWithAccess_ProceedsToEndpoint()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = new DocumentAnalysisRequest(documentId, "drive-id", "item-id");
        var context = CreateContext(CreateUser(), request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(It.Is<AuthorizationContext>(c =>
                c.ResourceId == documentId.ToString() &&
                c.Operation == "read"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        _authServiceMock.Verify(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_UserWithoutAccess_Returns403()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var request = new DocumentAnalysisRequest(documentId, "drive-id", "item-id");
        var context = CreateContext(CreateUser(), request);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(DeniedResult());

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_NoDocumentInRequest_Returns400()
    {
        // Arrange
        var context = CreateContext(CreateUser()); // No arguments

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }

    #endregion

    #region Batch Document Tests

    [Fact]
    public async Task InvokeAsync_BatchWithAllAccess_ProceedsToEndpoint()
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

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        _authServiceMock.Verify(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

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

        // First doc is allowed, second is not
        _authServiceMock
            .SetupSequence(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult())
            .ReturnsAsync(DeniedResult());

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task InvokeAsync_BatchWithDuplicateDocuments_ChecksOnce()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var requests = new List<DocumentAnalysisRequest>
        {
            new(docId, "drive-1", "item-1"),
            new(docId, "drive-1", "item-1") // Same document twice
        };
        var context = CreateContext(CreateUser(), requests);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
        // Should only check once due to Distinct()
        _authServiceMock.Verify(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(x => x.AuthorizeAsync(It.IsAny<AuthorizationContext>(), It.IsAny<CancellationToken>()))
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

    [Fact]
    public async Task InvokeAsync_GuidArgument_AuthorizesCorrectly()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var context = CreateContext(CreateUser(), documentId);

        _authServiceMock
            .Setup(x => x.AuthorizeAsync(It.Is<AuthorizationContext>(c =>
                c.ResourceId == documentId.ToString()), It.IsAny<CancellationToken>()))
            .ReturnsAsync(AllowedResult());

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
    }

    [Fact]
    public async Task InvokeAsync_EmptyGuidArgument_Returns400()
    {
        // Arrange
        var context = CreateContext(CreateUser(), Guid.Empty);

        // Act
        var result = await _filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(400);
    }

    #endregion
}
