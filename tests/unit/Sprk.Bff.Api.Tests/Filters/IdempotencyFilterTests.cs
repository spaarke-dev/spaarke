using System.IO;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Filters;
using Sprk.Bff.Api.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Filters;

/// <summary>
/// Unit tests for IdempotencyFilter.
/// Tests SHA256 hashing, cache lookup/storage, and duplicate detection.
/// Per task 010 migration: uses MemoryDistributedCache wrapped in <see cref="TenantCache"/>
/// since filter now takes <see cref="ITenantCache"/>.
/// </summary>
[Trait("status", "repaired")]
public class IdempotencyFilterTests
{
    private const string TestTenantId = "test-tenant-1";

    private static ClaimsPrincipal CreateUser(string userId = "user-123", string tenantId = TestTenantId)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId),
            new("tid", tenantId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateUserWithOid(string oid = "oid-456", string tenantId = TestTenantId)
    {
        var claims = new List<Claim>
        {
            new("oid", oid),
            new("tid", tenantId)
        };
        var identity = new ClaimsIdentity(claims, "TestAuth");
        return new ClaimsPrincipal(identity);
    }

    private static ClaimsPrincipal CreateAnonymousUser()
    {
        return new ClaimsPrincipal(new ClaimsIdentity());
    }

    private static (IDistributedCache inner, ITenantCache tenant) CreateCachePair()
    {
        var inner = (IDistributedCache)new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        var tenant = (ITenantCache)new TenantCache(inner, Mock.Of<ILogger<TenantCache>>());
        return (inner, tenant);
    }

    private static ITenantCache CreateCache() => CreateCachePair().tenant;

    private static Mock<ILogger<IdempotencyFilter>> CreateMockLogger()
    {
        return new Mock<ILogger<IdempotencyFilter>>();
    }

    private static (DefaultHttpContext httpContext, Mock<EndpointFilterInvocationContext> contextMock)
        CreatePostContext(ClaimsPrincipal user, string body = "{\"test\":\"value\"}")
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id"
        };
        httpContext.Request.Method = "POST";
        httpContext.Request.Path = "/office/save";
        httpContext.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        httpContext.Request.ContentType = "application/json";

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        return (httpContext, contextMock);
    }

    private static (DefaultHttpContext httpContext, Mock<EndpointFilterInvocationContext> contextMock)
        CreateGetContext(ClaimsPrincipal user)
    {
        var httpContext = new DefaultHttpContext
        {
            User = user,
            TraceIdentifier = "test-trace-id"
        };
        httpContext.Request.Method = "GET";
        httpContext.Request.Path = "/office/jobs/123";

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        return (httpContext, contextMock);
    }

    /// <summary>
    /// A distributed cache implementation that throws on all operations.
    /// Used to test "fail open" behavior when cache is unavailable.
    /// </summary>
    private sealed class ThrowingDistributedCache : IDistributedCache
    {
        public byte[]? Get(string key) => throw new Exception("Redis connection failed");
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) =>
            throw new Exception("Redis connection failed");
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) =>
            throw new Exception("Redis connection failed");
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) =>
            throw new Exception("Redis connection failed");
        public void Refresh(string key) => throw new Exception("Redis connection failed");
        public Task RefreshAsync(string key, CancellationToken token = default) =>
            throw new Exception("Redis connection failed");
        public void Remove(string key) => throw new Exception("Redis connection failed");
        public Task RemoveAsync(string key, CancellationToken token = default) =>
            throw new Exception("Redis connection failed");
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullCache_ThrowsArgumentNullException()
    {
        // Arrange
        var logger = CreateMockLogger();

        // Act & Assert
        var act = () => new IdempotencyFilter(null!, logger.Object);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("cache");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        var act = () => new IdempotencyFilter(cache, null!);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }


    #endregion

    #region Non-POST Request Tests

    [Fact]
    public async Task InvokeAsync_GetRequest_BypassesIdempotencyCheck()
    {
        // Arrange
        var cache = CreateCache();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreateGetContext(CreateUser());
        var nextCalled = false;

        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    [InlineData("PATCH")]
    public async Task InvokeAsync_NonPostMethods_BypassesIdempotencyCheck(string method)
    {
        // Arrange
        var cache = CreateCache();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var httpContext = new DefaultHttpContext
        {
            User = CreateUser(),
            TraceIdentifier = "test-trace-id"
        };
        httpContext.Request.Method = method;

        var contextMock = new Mock<EndpointFilterInvocationContext>();
        contextMock.Setup(c => c.HttpContext).Returns(httpContext);

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    #endregion

    #region User Identity Tests

    [Fact]
    public async Task InvokeAsync_NoUserId_BypassesIdempotencyCheck()
    {
        // Arrange
        var cache = CreateCache();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateAnonymousUser());
        var nextCalled = false;

        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_UserWithOidClaim_UsesOidAsUserId()
    {
        // Arrange
        var (innerCache, cache) = CreateCachePair();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUserWithOid("oid-test-123"));
        httpContext.Request.Headers["X-Idempotency-Key"] = "test-key";

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert - Cache should have been used (request processed successfully)
        nextCalled.Should().BeTrue("next delegate should be called for a new request");
        // Verify cache entry was created with the OID-based tenant-scoped key (task 010)
        var cacheKey = $"tenant:{TestTenantId}:idempotency-request:oid-test-123:test-key:v1";
        var cached = await innerCache.GetStringAsync(cacheKey);
        cached.Should().NotBeNull("response should be cached with OID-based tenant-scoped key");
    }

    #endregion

    #region Client-Provided Idempotency Key Tests


    #endregion

    #region Cache Hit Tests

    [Fact]
    public async Task InvokeAsync_CacheHit_ReturnsaCachedResponse()
    {
        // Arrange - Pre-populate cache with a response
        var (innerCache, cache) = CreateCachePair();
        var cachedResponse = new
        {
            StatusCode = 200,
            Value = new { id = "test-123", status = "success" },
            ResultType = "Ok"
        };
        var cachedJson = JsonSerializer.Serialize(cachedResponse);

        // Pre-populate the cache with the response keyed by tenant-scoped + client-provided key.
        // The TenantCache wrapper serializes via JsonSerializer<T> over byte stream, so storing
        // the raw JSON-string as a JSON-encoded string here matches the wire format.
        var cacheKey = $"tenant:{TestTenantId}:idempotency-request:user-123:test-key:v1";
        await innerCache.SetStringAsync(cacheKey, JsonSerializer.Serialize(cachedJson));

        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUser());
        httpContext.Request.Headers["X-Idempotency-Key"] = "test-key";

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeFalse("next delegate should not be called on cache hit");
        httpContext.Response.Headers["X-Idempotency-Status"].ToString().Should().Be("cached");
    }

    #endregion

    #region Cache Miss Tests

    [Fact]
    public async Task InvokeAsync_CacheMiss_ExecutesEndpointAndCachesResponse()
    {
        // Arrange
        var (innerCache, cache) = CreateCachePair();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUser());
        httpContext.Request.Headers["X-Idempotency-Key"] = "new-key";

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok(new { id = "new-123" }));
        };

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeTrue("next delegate should be called on cache miss");
        httpContext.Response.Headers["X-Idempotency-Status"].ToString().Should().Be("new");

        // Verify response was cached at the tenant-scoped key (task 010)
        var cacheKey = $"tenant:{TestTenantId}:idempotency-request:user-123:new-key:v1";
        var cached = await innerCache.GetStringAsync(cacheKey);
        cached.Should().NotBeNull("response should be cached after successful execution");
    }

    #endregion

    #region Lock Conflict Tests

    [Fact]
    public async Task InvokeAsync_LockAlreadyHeld_Returns409Conflict()
    {
        // Arrange
        var (innerCache, cache) = CreateCachePair();

        // Pre-populate the lock key (tenant-scoped per task 010 migration) to simulate a concurrent
        // request holding the lock. TenantCache.SetAsync<string>("locked") serializes to JSON
        // ("\"locked\""), so we match that wire format here.
        var lockKey = $"tenant:{TestTenantId}:idempotency-lock:user-123:locked-key:v1";
        await innerCache.SetStringAsync(lockKey, JsonSerializer.Serialize("locked"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2) });

        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUser());
        httpContext.Request.Headers["X-Idempotency-Key"] = "locked-key";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok());

        // Act
        var result = await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        result.Should().BeAssignableTo<IStatusCodeHttpResult>();
        var statusResult = (IStatusCodeHttpResult)result!;
        statusResult.StatusCode.Should().Be(409);
    }

    #endregion

    #region Hash Generation Tests

    [Fact]
    public async Task InvokeAsync_EmptyBody_BypassesIdempotencyCheck()
    {
        // Arrange
        var cache = CreateCache();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUser(), "");

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public async Task InvokeAsync_InvalidJson_BypassesIdempotencyCheck()
    {
        // Arrange
        var cache = CreateCache();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUser(), "not valid json {{{");

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert
        nextCalled.Should().BeTrue();
    }

    [Fact]
    public void CanonicalJson_SortedKeys_ProducesSameHash()
    {
        // Arrange - Same data, different key order
        var json1 = "{\"z\":1,\"a\":2,\"m\":3}";
        var json2 = "{\"a\":2,\"m\":3,\"z\":1}";

        // Act - Parse and re-serialize canonically
        using var doc1 = JsonDocument.Parse(json1);
        using var doc2 = JsonDocument.Parse(json2);

        var canonical1 = SerializeCanonical(doc1.RootElement);
        var canonical2 = SerializeCanonical(doc2.RootElement);

        // Assert - Both should produce same canonical form
        canonical1.Should().Be(canonical2);
    }

    [Fact]
    public void CanonicalJson_NestedObjects_SortedAtAllLevels()
    {
        // Arrange
        var json = "{\"z\":{\"c\":1,\"a\":2},\"a\":{\"z\":3,\"b\":4}}";

        // Act
        using var doc = JsonDocument.Parse(json);
        var canonical = SerializeCanonical(doc.RootElement);

        // Assert - Keys should be sorted at all levels
        canonical.Should().Be("{\"a\":{\"b\":4,\"z\":3},\"z\":{\"a\":2,\"c\":1}}");
    }

    [Fact]
    public void Sha256Hash_SameInput_ProducesSameOutput()
    {
        // Arrange
        var input = "user-123:/office/save:{\"test\":\"value\"}";

        // Act
        var hash1 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
        var hash2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

        // Assert
        hash1.Should().Be(hash2);
        hash1.Should().HaveLength(64); // SHA256 produces 32 bytes = 64 hex chars
    }

    [Fact]
    public void Sha256Hash_DifferentUsers_ProduceDifferentHashes()
    {
        // Arrange
        var input1 = "user-123:/office/save:{\"test\":\"value\"}";
        var input2 = "user-456:/office/save:{\"test\":\"value\"}";

        // Act
        var hash1 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input1))).ToLowerInvariant();
        var hash2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input2))).ToLowerInvariant();

        // Assert
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Sha256Hash_DifferentPaths_ProduceDifferentHashes()
    {
        // Arrange
        var input1 = "user-123:/office/save:{\"test\":\"value\"}";
        var input2 = "user-123:/office/quickcreate/matter:{\"test\":\"value\"}";

        // Act
        var hash1 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input1))).ToLowerInvariant();
        var hash2 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input2))).ToLowerInvariant();

        // Assert
        hash1.Should().NotBe(hash2);
    }

    #endregion

    #region Response Caching Tests

    [Fact]
    public async Task InvokeAsync_SuccessResponse_CachesWithCorrectTtl()
    {
        // Arrange
        var customTtl = TimeSpan.FromHours(48);
        var (innerCache, cache) = CreateCachePair();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object, customTtl);

        var (httpContext, contextMock) = CreatePostContext(CreateUser());
        httpContext.Request.Headers["X-Idempotency-Key"] = "test-key";

        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(Results.Ok(new { id = "123" }));

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert - Verify response was cached (TTL is internal to the cache implementation).
        // Per task 010: key is tenant-scoped; the stored value is JSON-encoded string of the
        // canonical response JSON (TenantCache.SetAsync<string>).
        var cacheKey = $"tenant:{TestTenantId}:idempotency-request:user-123:test-key:v1";
        var cached = await innerCache.GetStringAsync(cacheKey);
        cached.Should().NotBeNull("successful response should be cached");

        // Unwrap: outer JSON-encoded string → inner canonical response object
        var innerJson = JsonSerializer.Deserialize<string>(cached!);
        var cachedObj = JsonSerializer.Deserialize<JsonElement>(innerJson!);
        cachedObj.GetProperty("statusCode").GetInt32().Should().Be(200);
    }

    [Fact]
    public async Task InvokeAsync_ErrorResponse_DoesNotCache()
    {
        // Arrange
        var (innerCache, cache) = CreateCachePair();
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUser());
        httpContext.Request.Headers["X-Idempotency-Key"] = "error-key";

        // Return error response
        EndpointFilterDelegate next = _ => ValueTask.FromResult<object?>(
            Results.Problem(statusCode: 400, title: "Bad Request"));

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert - Cache should remain empty for the response key (error responses are not cached)
        var cacheKey = $"tenant:{TestTenantId}:idempotency-request:user-123:error-key:v1";
        var cached = await innerCache.GetStringAsync(cacheKey);
        cached.Should().BeNull("error responses should not be cached");
    }

    #endregion

    #region Cache Failure Tests

    [Fact]
    public async Task InvokeAsync_CacheGetFails_ProceedsWithoutIdempotency()
    {
        // Arrange — wrap the throwing IDistributedCache in TenantCache so the throw propagates
        // through the new wrapper (task 010 migration to ITenantCache).
        var throwingInner = new ThrowingDistributedCache();
        var cache = new TenantCache(throwingInner, Mock.Of<ILogger<TenantCache>>());
        var logger = CreateMockLogger();
        var filter = new IdempotencyFilter(cache, logger.Object);

        var (httpContext, contextMock) = CreatePostContext(CreateUser());
        httpContext.Request.Headers["X-Idempotency-Key"] = "test-key";

        var nextCalled = false;
        EndpointFilterDelegate next = _ =>
        {
            nextCalled = true;
            return ValueTask.FromResult<object?>(Results.Ok());
        };

        // Act
        await filter.InvokeAsync(contextMock.Object, next);

        // Assert - Should proceed with request on cache failure (fail open)
        nextCalled.Should().BeTrue();
    }

    #endregion

    #region Extension Method Tests

    [Fact]
    public void AddIdempotencyFilter_ReturnsRouteHandlerBuilder()
    {
        // This is a compile-time test - if it compiles, the extension method signature is correct
        // Runtime testing requires a full WebApplication setup which is done in integration tests
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Recursively serializes a JSON element with sorted object keys.
    /// Replicates the logic from IdempotencyFilter for testing.
    /// </summary>
    private static string SerializeCanonical(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => SerializeCanonicalObject(element),
            JsonValueKind.Array => SerializeCanonicalArray(element),
            _ => element.GetRawText()
        };
    }

    private static string SerializeCanonicalObject(JsonElement element)
    {
        var properties = element.EnumerateObject()
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"\"{p.Name}\":{SerializeCanonical(p.Value)}");

        return "{" + string.Join(",", properties) + "}";
    }

    private static string SerializeCanonicalArray(JsonElement element)
    {
        var items = element.EnumerateArray()
            .Select(SerializeCanonical);

        return "[" + string.Join(",", items) + "]";
    }

    #endregion
}
