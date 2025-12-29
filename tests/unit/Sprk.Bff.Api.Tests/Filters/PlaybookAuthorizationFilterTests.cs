using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Models.Ai;
using Sprk.Bff.Api.Services.Ai;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// Unit tests for PlaybookAuthorizationFilter - authorization for Playbook endpoints.
/// Tests owner-only and shared access modes.
/// </summary>
public class PlaybookAuthorizationFilterTests
{
    private readonly Mock<IPlaybookService> _playbookServiceMock;
    private readonly Mock<IPlaybookSharingService> _sharingServiceMock;
    private readonly Mock<ILogger<PlaybookAuthorizationFilter>> _loggerMock;

    private static readonly Guid TestUserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherUserId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TestPlaybookId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public PlaybookAuthorizationFilterTests()
    {
        _playbookServiceMock = new Mock<IPlaybookService>();
        _sharingServiceMock = new Mock<IPlaybookSharingService>();
        _loggerMock = new Mock<ILogger<PlaybookAuthorizationFilter>>();
    }

    private PlaybookAuthorizationFilter CreateFilter(PlaybookAuthorizationMode mode) =>
        new(_playbookServiceMock.Object, _sharingServiceMock.Object, _loggerMock.Object, mode);

    private static ClaimsPrincipal CreateUser(Guid userId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateUserWithOidClaim(Guid userId)
    {
        var claims = new List<Claim>
        {
            new("oid", userId.ToString())
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    private PlaybookResponse CreatePlaybook(Guid ownerId, bool isPublic = false) => new()
    {
        Id = TestPlaybookId,
        Name = "Test Playbook",
        OwnerId = ownerId,
        IsPublic = isPublic
    };

    #region PlaybookAuthorizationMode Tests

    [Fact]
    public void PlaybookAuthorizationMode_OwnerOnly_HasCorrectValue()
    {
        Assert.Equal(0, (int)PlaybookAuthorizationMode.OwnerOnly);
    }

    [Fact]
    public void PlaybookAuthorizationMode_OwnerOrSharedOrPublic_HasCorrectValue()
    {
        Assert.Equal(1, (int)PlaybookAuthorizationMode.OwnerOrSharedOrPublic);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullPlaybookService_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new PlaybookAuthorizationFilter(null!, _sharingServiceMock.Object, _loggerMock.Object, PlaybookAuthorizationMode.OwnerOnly));
    }

    [Fact]
    public void Constructor_WithNullSharingService_DoesNotThrow()
    {
        // Sharing service is optional (nullable)
        var filter = new PlaybookAuthorizationFilter(
            _playbookServiceMock.Object,
            null,
            _loggerMock.Object,
            PlaybookAuthorizationMode.OwnerOnly);

        Assert.NotNull(filter);
    }

    [Fact]
    public void Constructor_WithAllParameters_CreatesFilter()
    {
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);
        Assert.NotNull(filter);
    }

    #endregion

    #region OwnerOnly Mode Tests

    [Fact]
    public async Task OwnerOnly_WithOwner_ShouldAllowAccess()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);
        var playbook = CreatePlaybook(TestUserId);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);
        var nextCalled = false;

        // Act
        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        });

        // Assert
        Assert.True(nextCalled);
        Assert.Equal("success", result);
    }

    [Fact]
    public async Task OwnerOnly_WithNonOwner_ShouldDenyAccess()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);
        var playbook = CreatePlaybook(OtherUserId); // Different owner

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);

        // Act
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should not reach"));

        // Assert
        Assert.IsType<ProblemHttpResult>(result);
    }

    [Fact]
    public async Task OwnerOnly_WithPublicPlaybook_NonOwner_ShouldDenyAccess()
    {
        // Arrange - OwnerOnly ignores public flag
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);
        var playbook = CreatePlaybook(OtherUserId, isPublic: true);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);

        // Act
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should not reach"));

        // Assert - Should deny because OwnerOnly mode requires ownership
        Assert.IsType<ProblemHttpResult>(result);
    }

    #endregion

    #region OwnerOrSharedOrPublic Mode Tests

    [Fact]
    public async Task OwnerOrSharedOrPublic_WithOwner_ShouldAllowAccess()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);
        var playbook = CreatePlaybook(TestUserId, isPublic: false);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);
        var nextCalled = false;

        // Act
        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        });

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task OwnerOrSharedOrPublic_WithPublicPlaybook_ShouldAllowAccess()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);
        var playbook = CreatePlaybook(OtherUserId, isPublic: true);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);
        var nextCalled = false;

        // Act
        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        });

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task OwnerOrSharedOrPublic_WithSharedAccess_ShouldAllowAccess()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);
        var playbook = CreatePlaybook(OtherUserId, isPublic: false);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        _sharingServiceMock
            .Setup(s => s.UserHasSharedAccessAsync(
                TestPlaybookId,
                TestUserId,
                PlaybookAccessRights.Read,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);
        var nextCalled = false;

        // Act
        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        });

        // Assert
        Assert.True(nextCalled);
    }

    [Fact]
    public async Task OwnerOrSharedOrPublic_WithNoAccess_ShouldDenyAccess()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOrSharedOrPublic);
        var playbook = CreatePlaybook(OtherUserId, isPublic: false);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        _sharingServiceMock
            .Setup(s => s.UserHasSharedAccessAsync(
                TestPlaybookId,
                TestUserId,
                PlaybookAccessRights.Read,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);

        // Act
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should not reach"));

        // Assert
        Assert.IsType<ProblemHttpResult>(result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task Filter_WithNoUserClaim_ShouldReturn401()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);
        var user = CreateAnonymousUser();
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);

        // Act
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should not reach"));

        // Assert
        Assert.IsType<ProblemHttpResult>(result);
    }

    [Fact]
    public async Task Filter_WithInvalidPlaybookId_ShouldReturn400()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);
        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContextWithInvalidPlaybookId(user);
        var context = CreateInvocationContext(httpContext);

        // Act
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should not reach"));

        // Assert
        Assert.IsType<ProblemHttpResult>(result);
    }

    [Fact]
    public async Task Filter_WithNonExistentPlaybook_ShouldReturn404()
    {
        // Arrange
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlaybookResponse?)null);

        var user = CreateUser(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);

        // Act
        var result = await filter.InvokeAsync(context, _ => ValueTask.FromResult<object?>("should not reach"));

        // Assert
        Assert.IsType<NotFound>(result);
    }

    [Fact]
    public async Task Filter_WithOidClaim_ShouldExtractUserId()
    {
        // Arrange - Uses "oid" claim instead of NameIdentifier
        var filter = CreateFilter(PlaybookAuthorizationMode.OwnerOnly);
        var playbook = CreatePlaybook(TestUserId);

        _playbookServiceMock
            .Setup(s => s.GetPlaybookAsync(TestPlaybookId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(playbook);

        var user = CreateUserWithOidClaim(TestUserId);
        var httpContext = CreateHttpContext(user, TestPlaybookId);
        var context = CreateInvocationContext(httpContext);
        var nextCalled = false;

        // Act
        var result = await filter.InvokeAsync(context, _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>("success");
        });

        // Assert
        Assert.True(nextCalled);
    }

    #endregion

    #region Helper Methods

    private static HttpContext CreateHttpContext(ClaimsPrincipal user, Guid playbookId)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user
        };
        httpContext.Request.RouteValues["id"] = playbookId.ToString();
        return httpContext;
    }

    private static HttpContext CreateHttpContextWithInvalidPlaybookId(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user
        };
        httpContext.Request.RouteValues["id"] = "not-a-guid";
        return httpContext;
    }

    private static EndpointFilterInvocationContext CreateInvocationContext(HttpContext httpContext)
    {
        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);
        return contextMock.Object;
    }

    #endregion
}
