using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Reporting;
using Sprk.Bff.Api.Infrastructure.Cache;
using Sprk.Bff.Api.Tests.Infrastructure.Cache;
using Xunit;

namespace Sprk.Bff.Api.Tests.Api.Reporting;

/// <summary>
/// Unit tests for <see cref="ReportingEmbedService"/>.
///
/// Testing approach:
///   - Cache hit / miss / near-expiry paths are tested by seeding the IDistributedCache mock.
///   - The PowerBIClient is constructed internally by the service using MSAL, so tests that
///     require PBI API calls are annotated but left as structural tests — full PBI round-trips
///     belong in integration tests.
///   - Cache key format and token freshness logic are validated via the cache mock.
/// </summary>
public class ReportingEmbedServiceTests
{
    // =========================================================================
    // Helpers
    // =========================================================================

    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static IOptions<PowerBiOptions> BuildOptions(
        string tenantId = "test-tenant",
        string clientId = "test-client-id",
        string clientSecret = "test-client-secret",
        string apiUrl = "https://api.powerbi.com")
    {
        return Options.Create(new PowerBiOptions
        {
            TenantId = tenantId,
            ClientId = clientId,
            ClientSecret = clientSecret,
            ApiUrl = apiUrl
        });
    }

    private static Mock<ITenantCache> BuildCacheMock() => new(MockBehavior.Loose);

    private static Mock<ILogger<ReportingEmbedService>> BuildLoggerMock() =>
        new(MockBehavior.Loose);

    /// <summary>
    /// Builds an <see cref="IHttpContextAccessor"/> whose <c>HttpContext.User</c> carries
    /// a <c>tid</c> (tenant) claim so the production cache-aside path is exercised. Without
    /// this claim, <see cref="ReportingEmbedService"/> skips the cache entirely.
    /// </summary>
    private static IHttpContextAccessor BuildHttpContextAccessor(string tenantId = "test-tenant")
    {
        var ctx = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(
                new ClaimsIdentity(new[] { new Claim("tid", tenantId) }, "test"))
        };
        var accessor = new HttpContextAccessor { HttpContext = ctx };
        return accessor;
    }

    /// <summary>
    /// Serialises a <see cref="CachedEmbedEntry"/> (anonymous type matching the internal record)
    /// to the same JSON the service would write to Redis, so we can seed the cache mock.
    /// </summary>
    private static string BuildCacheEntryJson(
        string token,
        string embedUrl,
        Guid reportId,
        DateTimeOffset expiry,
        DateTimeOffset issuedAt,
        DateTimeOffset refreshAfter)
    {
        var entry = new
        {
            token,
            embedUrl,
            reportId,
            expiry,
            issuedAt,
            refreshAfter
        };
        return JsonSerializer.Serialize(entry, CacheJsonOptions);
    }

    // ITenantCache (FR-05) cache id component the production service builds.
    // Tenant-prefix + resource ("reporting-embed") + version are added by the wrapper.
    private static string BuildEmbedCacheId(Guid workspaceId, Guid reportId, string? username)
        => $"{workspaceId}:{reportId}:{username ?? "anonymous"}";

    private const string ReportingEmbedResource = "reporting-embed";
    private const string TestTenantId = "test-tenant";

    /// <summary>
    /// Decorator over <see cref="InMemoryTenantCache"/> that records every probe of
    /// <c>GetAsync&lt;T&gt;</c> so cache-format tests can assert the production service
    /// uses the expected (resource, id) coordinates. Optionally throws on read.
    /// </summary>
    private sealed class TrackingTenantCache : ITenantCache
    {
        private readonly InMemoryTenantCache _inner = new();
        public Exception? GetThrows { get; set; }
        public int GetCount { get; private set; }
        public string? LastTenantId { get; private set; }
        public string? LastResource { get; private set; }
        public string? LastId { get; private set; }

        public Task<T?> GetAsync<T>(string tenantId, string resource, string id, int version,
            string cacheInstance = "default", CancellationToken ct = default)
        {
            GetCount++;
            LastTenantId = tenantId; LastResource = resource; LastId = id;
            if (GetThrows is not null) throw GetThrows;
            return _inner.GetAsync<T>(tenantId, resource, id, version, cacheInstance, ct);
        }
        public Task SetAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => _inner.SetAsync(tenantId, resource, id, version, value, ttl, cacheInstance, ct);
        public Task RemoveAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => _inner.RemoveAsync(tenantId, resource, id, version, cacheInstance, ct);
        public Task<T> GetOrCreateAsync<T>(string tenantId, string resource, string id, int version, Func<CancellationToken, Task<T>> factory, TimeSpan? ttl = null, string cacheInstance = "default", CancellationToken ct = default)
            => _inner.GetOrCreateAsync(tenantId, resource, id, version, factory, ttl, cacheInstance, ct);
        public Task<string?> GetStringAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => _inner.GetStringAsync(tenantId, resource, id, version, cacheInstance, ct);
        public Task SetStringAsync(string tenantId, string resource, string id, int version, string value, TimeSpan? ttl = null, TimeSpan? slidingExpiration = null, string cacheInstance = "default", CancellationToken ct = default)
            => _inner.SetStringAsync(tenantId, resource, id, version, value, ttl, slidingExpiration, cacheInstance, ct);
        public Task RefreshAsync(string tenantId, string resource, string id, int version, string cacheInstance = "default", CancellationToken ct = default)
            => _inner.RefreshAsync(tenantId, resource, id, version, cacheInstance, ct);
        public Task SetSlidingAsync<T>(string tenantId, string resource, string id, int version, T value, TimeSpan slidingExpiration, string cacheInstance = "default", CancellationToken ct = default)
            => _inner.SetSlidingAsync(tenantId, resource, id, version, value, slidingExpiration, cacheInstance, ct);
    }

    // =========================================================================
    // Construction
    // =========================================================================

    [Fact]
    public void Constructor_ThrowsWhenOptionsIsNull()
    {
        // Arrange
        var cache = BuildCacheMock();
        var logger = BuildLoggerMock();

        // Act
        var act = () => new ReportingEmbedService(null!, cache.Object, BuildHttpContextAccessor(), logger.Object);

        // Assert — null options causes NullReferenceException when accessing .Value on the null IOptions<T>
        act.Should().Throw<Exception>("passing null for IOptions<PowerBiOptions> must fail on construction");
    }


    // =========================================================================
    // Cache key format
    // =========================================================================

    [Fact]
    public async Task GetEmbedConfigAsync_UsesCorrectCacheKeyFormat()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        const string username = "test@contoso.com";

        var expectedId = BuildEmbedCacheId(workspaceId, reportId, username);
        var cache = new TrackingTenantCache();

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache, BuildHttpContextAccessor(TestTenantId), logger.Object);

        // Act — will fail at MSAL token acquisition (expected), but cache key is checked first
        try
        {
            await service.GetEmbedConfigAsync(workspaceId, reportId, username, roles: null);
        }
        catch
        {
            // MSAL/PBI calls will fail in unit tests — we only care about the cache interaction
        }

        // Assert (FR-05 key format: tenant + "reporting-embed" + idComponent + v1)
        cache.LastResource.Should().Be(ReportingEmbedResource);
        cache.LastId.Should().Be(expectedId,
            "cache id must follow the '{workspaceId}:{reportId}:{userId}' pattern under resource 'reporting-embed'");
    }

    [Fact]
    public async Task GetEmbedConfigAsync_UsesAnonymousInCacheKey_WhenUsernameIsNull()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        var expectedId = BuildEmbedCacheId(workspaceId, reportId, null);
        var cache = new TrackingTenantCache();

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache, BuildHttpContextAccessor(TestTenantId), logger.Object);

        // Act
        try
        {
            await service.GetEmbedConfigAsync(workspaceId, reportId, username: null, roles: null);
        }
        catch
        {
            // Expected — MSAL will fail
        }

        // Assert
        cache.LastId.Should().Be(expectedId,
            "null username should produce 'anonymous' in the cache id");
    }

    // =========================================================================
    // Cache HIT — fresh token
    // =========================================================================

    [Fact]
    public async Task GetEmbedConfigAsync_ReturnsCachedConfig_WhenCacheHitAndTokenIsFresh()
    {
        // Arrange — seed the cache with a token that has plenty of lifetime remaining
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        const string username = "user@contoso.com";

        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(50);   // 50 min remaining of 60 total → 83% left → fresh
        var refreshAfter = issuedAt.AddMinutes(48);          // 80% of 60 min

        var cachedToken = "eyJ0eXAi.cached-token-value";
        var cachedEmbedUrl = "https://app.powerbi.com/reportEmbed?reportId=" + reportId;

        // Seed the real in-memory ITenantCache with the production's CachedEmbedEntry shape.
        var cache = new InMemoryTenantCache();
        var idComponent = BuildEmbedCacheId(workspaceId, reportId, username);
        await cache.SetAsync(
            TestTenantId, ReportingEmbedResource, idComponent, 1,
            new
            {
                token = cachedToken,
                embedUrl = cachedEmbedUrl,
                reportId,
                expiry,
                issuedAt,
                refreshAfter
            });

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache, BuildHttpContextAccessor(TestTenantId), logger.Object);

        // Act
        var result = await service.GetEmbedConfigAsync(workspaceId, reportId, username, roles: null);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be(cachedToken);
        result.EmbedUrl.Should().Be(cachedEmbedUrl);
        result.ReportId.Should().Be(reportId);
        result.Expiry.Should().Be(expiry);
    }

    // =========================================================================
    // Cache HIT — near-expiry (should regenerate)
    // =========================================================================


    // =========================================================================
    // Cache MISS
    // =========================================================================

    [Fact]
    public async Task GetEmbedConfigAsync_ChecksCacheBefore_CallingPbiApi_OnCacheMiss()
    {
        // Arrange — empty cache (miss)
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        const string username = "user@contoso.com";

        var cache = new TrackingTenantCache();
        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache, BuildHttpContextAccessor(TestTenantId), logger.Object);

        // Act — will fail at MSAL token acquisition (expected in unit tests)
        try
        {
            await service.GetEmbedConfigAsync(workspaceId, reportId, username, roles: null);
        }
        catch
        {
            // Expected — MSAL/PBI calls will fail without real credentials
        }

        // Assert — cache was checked exactly once before the PBI API attempt
        cache.GetCount.Should().Be(1, "cache must be probed exactly once before PBI API attempt");
    }

    // =========================================================================
    // Cache failure — graceful degradation (ADR-009)
    // =========================================================================

    [Fact]
    public async Task GetEmbedConfigAsync_ContinuesToPbiApi_WhenCacheReadThrows()
    {
        // Arrange — cache throws (Redis unavailable)
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        var cache = new TrackingTenantCache
        {
            GetThrows = new InvalidOperationException("Redis connection lost")
        };

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache, BuildHttpContextAccessor(TestTenantId), logger.Object);

        // Act — cache error should NOT surface as an exception to the caller (graceful degradation)
        // The code will fall through to PBI API which will fail (no real credentials), but it
        // must NOT throw a "Redis connection lost" exception.
        Func<Task> act = async () =>
            await service.GetEmbedConfigAsync(workspaceId, reportId, username: null, roles: null);

        // Assert — only MSAL/PBI exceptions are expected; cache exceptions must be swallowed
        var ex = await Record.ExceptionAsync(act);
        ex?.GetType().FullName.Should().NotContain("Redis",
            "cache exceptions must be swallowed — ADR-009 graceful degradation");
        ex?.Message.Should().NotBe("Redis connection lost",
            "the Redis exception must not propagate to the caller");
    }

    // =========================================================================
    // GetReportsAsync / GetReportAsync — structural tests
    // =========================================================================


    // =========================================================================
    // EmbedConfig DTO
    // =========================================================================

    [Fact]
    public void EmbedConfig_CanBeConstructed()
    {
        // Arrange
        var reportId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var expiry = now.AddHours(1);
        var refreshAfter = now.AddMinutes(48);

        // Act
        var config = new EmbedConfig(
            Token: "eyJ0eXAi.token",
            EmbedUrl: "https://app.powerbi.com/reportEmbed",
            ReportId: reportId,
            Expiry: expiry,
            RefreshAfter: refreshAfter);

        // Assert
        config.Token.Should().Be("eyJ0eXAi.token");
        config.EmbedUrl.Should().Be("https://app.powerbi.com/reportEmbed");
        config.ReportId.Should().Be(reportId);
        config.Expiry.Should().Be(expiry);
        config.RefreshAfter.Should().Be(refreshAfter);
        config.RefreshAfter.Should().BeBefore(config.Expiry,
            "RefreshAfter must be before Expiry (client refreshes before token expires)");
    }

    [Fact]
    public void PowerBiReport_CanBeConstructed()
    {
        // Arrange
        var id = Guid.NewGuid();
        var datasetId = Guid.NewGuid();

        // Act
        var report = new PowerBiReport(
            Id: id,
            Name: "Sales Dashboard",
            EmbedUrl: "https://app.powerbi.com/reportEmbed?reportId=" + id,
            DatasetId: datasetId);

        // Assert
        report.Id.Should().Be(id);
        report.Name.Should().Be("Sales Dashboard");
        report.DatasetId.Should().Be(datasetId);
    }

    // =========================================================================
    // ExportFormat enum
    // =========================================================================

    [Fact]
    public void ExportFormat_HasPdfAndPptxValues()
    {
        Enum.IsDefined(typeof(ExportFormat), ExportFormat.PDF).Should().BeTrue();
        Enum.IsDefined(typeof(ExportFormat), ExportFormat.PPTX).Should().BeTrue();
    }
}

/// <summary>Placeholder so the catch block compiles without a direct MSAL reference.</summary>
file class MsalException : Exception
{
    public MsalException(string message) : base(message) { }
}
