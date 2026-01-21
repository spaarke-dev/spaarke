using System.Security.Claims;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters.Office;

/// <summary>
/// Unit tests for <see cref="IdempotencyFilter"/>.
/// Tests idempotency behavior for POST endpoints.
/// </summary>
public class IdempotencyFilterTests
{
    private readonly Mock<IDistributedCache> _cacheMock;
    private readonly Mock<ILogger<IdempotencyFilter>> _loggerMock;
    private readonly IdempotencyFilter _sut;

    public IdempotencyFilterTests()
    {
        _cacheMock = new Mock<IDistributedCache>();
        _loggerMock = new Mock<ILogger<IdempotencyFilter>>();
        _sut = new IdempotencyFilter(_cacheMock.Object, _loggerMock.Object);
    }

    #region Helper Methods

    private static ClaimsPrincipal CreateAuthenticatedUser(string userId = "user-123")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static Mock<EndpointFilterInvocationContext> CreateContext(
        ClaimsPrincipal user,
        string method = "POST",
        string path = "/office/save",
        string? body = null,
        string? idempotencyKeyHeader = null)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id"
        };

        httpContext.Request.Method = method;
        httpContext.Request.Path = path;

        if (body != null)
        {
            httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            httpContext.Request.ContentLength = body.Length;
            httpContext.Request.ContentType = "application/json";
        }

        if (idempotencyKeyHeader != null)
        {
            httpContext.Request.Headers["X-Idempotency-Key"] = idempotencyKeyHeader;
        }

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);
        contextMock.Setup(c => c.Arguments).Returns(new List<object?>());

        return contextMock;
    }

    private static ValueTask<object?> NextDelegate(EndpointFilterInvocationContext context)
        => ValueTask.FromResult<object?>(Results.Accepted(null, new { jobId = Guid.NewGuid() }));

    #endregion

    #region Method Filtering Tests

    [Fact]
    public async Task InvokeAsync_GetRequest_SkipsIdempotencyCheck()
    {
        // Arrange
        var context = CreateContext(CreateAuthenticatedUser(), "GET");

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Accepted<object>>();
        _cacheMock.Verify(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_DeleteRequest_SkipsIdempotencyCheck()
    {
        // Arrange
        var context = CreateContext(CreateAuthenticatedUser(), "DELETE");

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Accepted<object>>();
        _cacheMock.Verify(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    #endregion

    #region User ID Tests

    [Fact]
    public async Task InvokeAsync_NoUserId_SkipsIdempotencyCheck()
    {
        // Arrange
        var anonymousUser = new ClaimsPrincipal(new ClaimsIdentity());
        var context = CreateContext(anonymousUser, body: """{"test": "data"}""");

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Accepted<object>>();
    }

    #endregion

    #region Cached Response Tests

    [Fact]
    public async Task InvokeAsync_CachedResponse_ReturnsCachedResult()
    {
        // Arrange
        var cachedResponse = JsonSerializer.Serialize(new
        {
            StatusCode = 202,
            Value = new { jobId = "cached-job-id" },
            ResultType = "Microsoft.AspNetCore.Http.HttpResults.Accepted`1"
        });

        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        var body = """{"contentType": "Email", "targetEntity": {"entityType": "account", "entityId": "123"}}""";
        var context = CreateContext(CreateAuthenticatedUser(), body: body);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        // When cached, it returns the deserialized result
        context.Object.HttpContext.Response.Headers.Should().ContainKey("X-Idempotency-Status");
        context.Object.HttpContext.Response.Headers["X-Idempotency-Status"].ToString().Should().Be("cached");
    }

    [Fact]
    public async Task InvokeAsync_NoCachedResponse_CallsNextAndCaches()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // No lock exists
        _cacheMock
            .Setup(c => c.GetAsync(It.Is<string>(s => s.Contains("lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var body = """{"contentType": "Email", "targetEntity": {"entityType": "account", "entityId": "123"}}""";
        var context = CreateContext(CreateAuthenticatedUser(), body: body);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Accepted<object>>();
        context.Object.HttpContext.Response.Headers["X-Idempotency-Status"].ToString().Should().Be("new");

        // Verify caching was attempted
        _cacheMock.Verify(
            c => c.SetStringAsync(
                It.Is<string>(s => s.Contains("idempotency:request:")),
                It.IsAny<string>(),
                It.IsAny<DistributedCacheEntryOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Client-Provided Key Tests

    [Fact]
    public async Task InvokeAsync_ClientProvidedKey_UsesProvidedKey()
    {
        // Arrange
        var clientKey = "my-unique-key-123";
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _cacheMock
            .Setup(c => c.GetAsync(It.Is<string>(s => s.Contains("lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var context = CreateContext(
            CreateAuthenticatedUser(),
            body: """{"contentType": "Email"}""",
            idempotencyKeyHeader: clientKey);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Accepted<object>>();

        // Verify cache was checked with client-provided key
        _cacheMock.Verify(
            c => c.GetStringAsync(
                It.Is<string>(s => s.Contains(clientKey)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Lock Conflict Tests

    [Fact]
    public async Task InvokeAsync_LockExists_Returns409Conflict()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.Is<string>(s => s.Contains("request:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        // Lock exists
        _cacheMock
            .Setup(c => c.GetAsync(It.Is<string>(s => s.Contains("lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Encoding.UTF8.GetBytes("locked"));

        var body = """{"contentType": "Email"}""";
        var context = CreateContext(CreateAuthenticatedUser(), body: body);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<ProblemHttpResult>();
        var problemResult = (ProblemHttpResult)result!;
        problemResult.StatusCode.Should().Be(409);
        problemResult.ProblemDetails.Extensions.Should().ContainKey("errorCode");
        problemResult.ProblemDetails.Extensions["errorCode"].Should().Be("OFFICE_IDEMPOTENCY_CONFLICT");
    }

    #endregion

    #region Body Handling Tests

    [Fact]
    public async Task InvokeAsync_EmptyBody_SkipsIdempotencyCheck()
    {
        // Arrange
        var context = CreateContext(CreateAuthenticatedUser(), body: null);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Accepted<object>>();
    }

    [Fact]
    public async Task InvokeAsync_InvalidJsonBody_SkipsIdempotencyCheck()
    {
        // Arrange
        var context = CreateContext(CreateAuthenticatedUser(), body: "not valid json {{{");

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        result.Should().BeOfType<Accepted<object>>();
    }

    #endregion

    #region Cache Failure Tests

    [Fact]
    public async Task InvokeAsync_CacheFailure_ProceedsWithRequest()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis unavailable"));

        var body = """{"contentType": "Email"}""";
        var context = CreateContext(CreateAuthenticatedUser(), body: body);

        // Act
        var result = await _sut.InvokeAsync(context.Object, NextDelegate);

        // Assert
        // Should fail open - proceed with request
        result.Should().BeOfType<Accepted<object>>();
    }

    #endregion

    #region Custom TTL Tests

    [Fact]
    public async Task Constructor_CustomTtl_UsesTtlForCaching()
    {
        // Arrange
        var customTtl = TimeSpan.FromHours(12);
        var filterWithCustomTtl = new IdempotencyFilter(_cacheMock.Object, _loggerMock.Object, customTtl);

        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        _cacheMock
            .Setup(c => c.GetAsync(It.Is<string>(s => s.Contains("lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var body = """{"contentType": "Email"}""";
        var context = CreateContext(CreateAuthenticatedUser(), body: body);

        // Act
        await filterWithCustomTtl.InvokeAsync(context.Object, NextDelegate);

        // Assert
        _cacheMock.Verify(
            c => c.SetStringAsync(
                It.Is<string>(s => s.Contains("request:")),
                It.IsAny<string>(),
                It.Is<DistributedCacheEntryOptions>(o => o.AbsoluteExpirationRelativeToNow == customTtl),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region Canonical JSON Tests

    [Fact]
    public async Task InvokeAsync_DifferentJsonFormatting_GeneratesSameKey()
    {
        // Arrange - Two requests with same content but different JSON formatting
        var body1 = """{"contentType":"Email","targetEntity":{"entityType":"account","entityId":"123"}}""";
        var body2 = """
        {
          "targetEntity": {
            "entityId": "123",
            "entityType": "account"
          },
          "contentType": "Email"
        }
        """;

        string? capturedKey1 = null;
        string? capturedKey2 = null;

        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) =>
            {
                if (capturedKey1 == null)
                    capturedKey1 = key;
                else
                    capturedKey2 = key;
            })
            .ReturnsAsync((string?)null);

        _cacheMock
            .Setup(c => c.GetAsync(It.Is<string>(s => s.Contains("lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var context1 = CreateContext(CreateAuthenticatedUser("same-user"), body: body1);
        var context2 = CreateContext(CreateAuthenticatedUser("same-user"), body: body2);

        // Act
        await _sut.InvokeAsync(context1.Object, NextDelegate);
        await _sut.InvokeAsync(context2.Object, NextDelegate);

        // Assert - Both should generate the same cache key (after canonical normalization)
        capturedKey1.Should().NotBeNull();
        capturedKey2.Should().NotBeNull();
        capturedKey1.Should().Be(capturedKey2);
    }

    #endregion

    #region User Scoping Tests

    [Fact]
    public async Task InvokeAsync_DifferentUsers_GenerateDifferentKeys()
    {
        // Arrange
        var body = """{"contentType": "Email"}""";
        string? capturedKey1 = null;
        string? capturedKey2 = null;

        _cacheMock
            .Setup(c => c.GetStringAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) =>
            {
                if (capturedKey1 == null)
                    capturedKey1 = key;
                else
                    capturedKey2 = key;
            })
            .ReturnsAsync((string?)null);

        _cacheMock
            .Setup(c => c.GetAsync(It.Is<string>(s => s.Contains("lock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);

        var context1 = CreateContext(CreateAuthenticatedUser("user-1"), body: body);
        var context2 = CreateContext(CreateAuthenticatedUser("user-2"), body: body);

        // Act
        await _sut.InvokeAsync(context1.Object, NextDelegate);
        await _sut.InvokeAsync(context2.Object, NextDelegate);

        // Assert - Different users should have different keys
        capturedKey1.Should().NotBeNull();
        capturedKey2.Should().NotBeNull();
        capturedKey1.Should().NotBe(capturedKey2);
    }

    #endregion
}
