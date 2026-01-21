using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Configuration;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters.Office;

/// <summary>
/// Unit tests for <see cref="OfficeRateLimitFilter"/> and <see cref="OfficeRateLimitService"/>.
/// Tests rate limiting behavior for Office endpoints.
/// </summary>
public class RateLimitFilterTests
{
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<OfficeRateLimitService>> _serviceLoggerMock;
    private readonly Mock<ILogger<OfficeRateLimitFilter>> _filterLoggerMock;
    private readonly OfficeRateLimitOptions _options;

    public RateLimitFilterTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _serviceLoggerMock = new Mock<ILogger<OfficeRateLimitService>>();
        _filterLoggerMock = new Mock<ILogger<OfficeRateLimitFilter>>();
        _options = new OfficeRateLimitOptions
        {
            Enabled = true,
            WindowSizeSeconds = 60,
            SegmentsPerWindow = 6,
            KeyPrefix = "test:ratelimit:",
            Limits = new EndpointLimits
            {
                SaveRequestsPerMinute = 10,
                QuickCreateRequestsPerMinute = 5,
                SearchRequestsPerMinute = 30,
                JobsRequestsPerMinute = 60,
                ShareRequestsPerMinute = 20,
                RecentRequestsPerMinute = 30
            }
        };
    }

    #region Helper Methods

    private IOfficeRateLimitService CreateRateLimitService()
    {
        return new OfficeRateLimitService(
            _cacheMock.Object,
            Options.Create(_options),
            _serviceLoggerMock.Object);
    }

    private OfficeRateLimitFilter CreateFilter(OfficeRateLimitCategory category)
    {
        return new OfficeRateLimitFilter(
            CreateRateLimitService(),
            category,
            _filterLoggerMock.Object);
    }

    private static ClaimsPrincipal CreateAuthenticatedUser(string userId = "user-123")
    {
        var claims = new List<Claim>
        {
            new("oid", userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
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

    #region Rate Limit Service Tests

    [Fact]
    public async Task RateLimitService_DisabledRateLimiting_AllowsAllRequests()
    {
        // Arrange
        _options.Enabled = false;
        var service = CreateRateLimitService();

        // Act
        var result = await service.CheckAndIncrementAsync("user-123", OfficeRateLimitCategory.Save);

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(int.MaxValue);
        result.Remaining.Should().Be(int.MaxValue);
    }

    [Fact]
    public async Task RateLimitService_FirstRequest_Allowed()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = CreateRateLimitService();

        // Act
        var result = await service.CheckAndIncrementAsync("user-123", OfficeRateLimitCategory.Save);

        // Assert
        result.IsAllowed.Should().BeTrue();
        result.Limit.Should().Be(10); // Save limit
        result.Remaining.Should().Be(9); // 10 - 1
    }

    [Theory]
    [InlineData(OfficeRateLimitCategory.Save, 10)]
    [InlineData(OfficeRateLimitCategory.QuickCreate, 5)]
    [InlineData(OfficeRateLimitCategory.Search, 30)]
    [InlineData(OfficeRateLimitCategory.Jobs, 60)]
    [InlineData(OfficeRateLimitCategory.Share, 20)]
    [InlineData(OfficeRateLimitCategory.Recent, 30)]
    public async Task RateLimitService_CorrectLimitsForCategory(OfficeRateLimitCategory category, int expectedLimit)
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var service = CreateRateLimitService();

        // Act
        var result = await service.CheckAndIncrementAsync("user-123", category);

        // Assert
        result.Limit.Should().Be(expectedLimit);
    }

    [Fact]
    public async Task RateLimitService_CacheError_FailsOpen()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        var service = CreateRateLimitService();

        // Act
        var result = await service.CheckAndIncrementAsync("user-123", OfficeRateLimitCategory.Save);

        // Assert
        result.IsAllowed.Should().BeTrue(); // Fail open
    }

    #endregion

    #region Rate Limit Filter Tests

    [Fact]
    public async Task Filter_RequestAllowed_ProceedsToEndpoint()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var filter = CreateFilter(OfficeRateLimitCategory.Save);
        var context = CreateContext(CreateAuthenticatedUser());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();

        // Verify rate limit headers are set
        var headers = context.Object.HttpContext.Response.Headers;
        headers.Should().ContainKey("X-RateLimit-Limit");
        headers.Should().ContainKey("X-RateLimit-Remaining");
        headers.Should().ContainKey("X-RateLimit-Reset");
    }

    [Fact]
    public async Task Filter_RateLimitExceeded_Returns429()
    {
        // Arrange - Simulate hitting the limit
        var segmentCounts = new Dictionary<long, int>();
        var now = DateTimeOffset.UtcNow;
        var segmentSeconds = _options.WindowSizeSeconds / _options.SegmentsPerWindow;
        var currentSegment = now.ToUnixTimeSeconds() / segmentSeconds;

        // Fill up the window to exceed the limit
        for (int i = 0; i < _options.SegmentsPerWindow; i++)
        {
            segmentCounts[currentSegment - i] = 2; // 2 requests per segment = 12 total (> 10 limit)
        }

        var cachedState = System.Text.Json.JsonSerializer.Serialize(new { SegmentCounts = segmentCounts });
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedState);

        var filter = CreateFilter(OfficeRateLimitCategory.Save);
        var context = CreateContext(CreateAuthenticatedUser());

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(429);

        // Verify Retry-After header is set
        var headers = context.Object.HttpContext.Response.Headers;
        headers.Should().ContainKey("Retry-After");
    }

    [Fact]
    public async Task Filter_UnauthenticatedUser_UsesIpAddress()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var filter = CreateFilter(OfficeRateLimitCategory.Save);
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var context = CreateContext(anonymousUser);

        // Act
        var result = await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Ok<string>>();
    }

    #endregion

    #region User ID Extraction Tests

    [Fact]
    public async Task Filter_UserWithOid_UsesOidForRateLimit()
    {
        // Arrange
        var userId = "oid-user-123";
        _cacheMock
            .Setup(c => c.GetStringAsync(It.Is<string>(s => s.Contains(userId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var filter = CreateFilter(OfficeRateLimitCategory.Save);
        var user = CreateAuthenticatedUser(userId);
        var context = CreateContext(user);

        // Act
        await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        _cacheMock.Verify(
            c => c.GetStringAsync(It.Is<string>(s => s.Contains(userId)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Filter_UserWithNameIdentifier_UsesNameIdForRateLimit()
    {
        // Arrange
        var userId = "name-id-user-456";
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        var user = new ClaimsPrincipal(identity);

        _cacheMock
            .Setup(c => c.GetStringAsync(It.Is<string>(s => s.Contains(userId)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var filter = CreateFilter(OfficeRateLimitCategory.Save);
        var context = CreateContext(user);

        // Act
        await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        _cacheMock.Verify(
            c => c.GetStringAsync(It.Is<string>(s => s.Contains(userId)), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Rate Limit Headers Tests

    [Fact]
    public async Task Filter_SetsCorrectRateLimitHeaders()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var filter = CreateFilter(OfficeRateLimitCategory.Search);
        var context = CreateContext(CreateAuthenticatedUser());

        // Act
        await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        var headers = context.Object.HttpContext.Response.Headers;

        headers["X-RateLimit-Limit"].ToString().Should().Be("30"); // Search limit
        headers["X-RateLimit-Remaining"].ToString().Should().Be("29");
        headers["X-RateLimit-Reset"].ToString().Should().NotBeNullOrEmpty();
    }

    #endregion

    #region Category Isolation Tests

    [Fact]
    public async Task Filter_DifferentCategories_HaveSeparateLimits()
    {
        // Arrange
        var userId = "user-123";
        var saveCategory = "Save";
        var searchCategory = "Search";

        // Setup separate tracking for each category
        _cacheMock
            .Setup(c => c.GetStringAsync(It.Is<string>(s => s.Contains(saveCategory)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _cacheMock
            .Setup(c => c.GetStringAsync(It.Is<string>(s => s.Contains(searchCategory)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var saveFilter = CreateFilter(OfficeRateLimitCategory.Save);
        var searchFilter = CreateFilter(OfficeRateLimitCategory.Search);
        var context = CreateContext(CreateAuthenticatedUser(userId));

        // Act
        var saveResult = await saveFilter.InvokeAsync(context.Object, NextDelegate);
        var searchResult = await searchFilter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        saveResult.Should().BeOfType<Ok<string>>();
        searchResult.Should().BeOfType<Ok<string>>();

        // Both should succeed independently
    }

    #endregion

    #region Logging Tests

    [Fact]
    public async Task Filter_RateLimitExceeded_LogsWarning()
    {
        // Arrange - Simulate hitting the limit
        var segmentCounts = new Dictionary<long, int>();
        var now = DateTimeOffset.UtcNow;
        var segmentSeconds = _options.WindowSizeSeconds / _options.SegmentsPerWindow;
        var currentSegment = now.ToUnixTimeSeconds() / segmentSeconds;

        for (int i = 0; i < _options.SegmentsPerWindow; i++)
        {
            segmentCounts[currentSegment - i] = 2;
        }

        var cachedState = System.Text.Json.JsonSerializer.Serialize(new { SegmentCounts = segmentCounts });
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedState);

        var filter = CreateFilter(OfficeRateLimitCategory.Save);
        var context = CreateContext(CreateAuthenticatedUser());

        // Act
        await filter.InvokeAsync(context.Object, NextDelegate);

        // Assert
        _filterLoggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Rate limit exceeded")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}
