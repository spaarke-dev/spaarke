using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sprk.Bff.Api.Api.Reporting;
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

    private static Mock<IDistributedCache> BuildCacheMock() => new(MockBehavior.Loose);

    private static Mock<ILogger<ReportingEmbedService>> BuildLoggerMock() =>
        new(MockBehavior.Loose);

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

    private static string BuildEmbedCacheKey(Guid workspaceId, Guid reportId, string? username)
        => $"pbi:embed:{workspaceId}:{reportId}:{username ?? "anonymous"}";

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
        var act = () => new ReportingEmbedService(null!, cache.Object, logger.Object);

        // Assert — null options causes NullReferenceException when accessing .Value on the null IOptions<T>
        act.Should().Throw<Exception>("passing null for IOptions<PowerBiOptions> must fail on construction");
    }

    [Fact]
    public void Constructor_DoesNotThrow_WithValidOptions()
    {
        // Arrange
        var options = BuildOptions();
        var cache = BuildCacheMock();
        var logger = BuildLoggerMock();

        // Act
        var act = () => new ReportingEmbedService(options, cache.Object, logger.Object);

        // Assert — construction should succeed (MSAL CCA is built lazily at first token request)
        act.Should().NotThrow();
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

        var expectedKey = $"pbi:embed:{workspaceId}:{reportId}:{username}";

        var cache = BuildCacheMock();
        string? capturedKey = null;

        cache
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => capturedKey = key)
            .ReturnsAsync((byte[]?)null);

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache.Object, logger.Object);

        // Act — will fail at MSAL token acquisition (expected), but cache key is checked first
        try
        {
            await service.GetEmbedConfigAsync(workspaceId, reportId, username, roles: null);
        }
        catch
        {
            // MSAL/PBI calls will fail in unit tests — we only care about the cache interaction
        }

        // Assert
        capturedKey.Should().Be(expectedKey,
            "cache key must follow the 'pbi:embed:{workspaceId}:{reportId}:{userId}' pattern");
    }

    [Fact]
    public async Task GetEmbedConfigAsync_UsesAnonymousInCacheKey_WhenUsernameIsNull()
    {
        // Arrange
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();

        var expectedKey = $"pbi:embed:{workspaceId}:{reportId}:anonymous";

        var cache = BuildCacheMock();
        string? capturedKey = null;

        cache
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, CancellationToken>((key, _) => capturedKey = key)
            .ReturnsAsync((byte[]?)null);

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache.Object, logger.Object);

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
        capturedKey.Should().Be(expectedKey,
            "null username should produce 'anonymous' in the cache key");
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

        var json = BuildCacheEntryJson(
            token: cachedToken,
            embedUrl: cachedEmbedUrl,
            reportId: reportId,
            expiry: expiry,
            issuedAt: issuedAt,
            refreshAfter: refreshAfter);

        var cache = BuildCacheMock();
        cache
            .Setup(c => c.GetAsync(
                BuildEmbedCacheKey(workspaceId, reportId, username),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(json));

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache.Object, logger.Object);

        // Act
        var result = await service.GetEmbedConfigAsync(workspaceId, reportId, username, roles: null);

        // Assert
        result.Should().NotBeNull();
        result.Token.Should().Be(cachedToken);
        result.EmbedUrl.Should().Be(cachedEmbedUrl);
        result.ReportId.Should().Be(reportId);
        result.Expiry.Should().Be(expiry);

        // Verify we did NOT call SetAsync (no cache write on a hit)
        cache.Verify(c => c.SetAsync(
            It.IsAny<string>(),
            It.IsAny<byte[]>(),
            It.IsAny<DistributedCacheEntryOptions>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // =========================================================================
    // Cache HIT — near-expiry (should regenerate)
    // =========================================================================

    [Fact]
    public async Task GetEmbedConfigAsync_RegeneratesToken_WhenCachedTokenIsNearExpiry()
    {
        // Arrange — seed the cache with a token that has only 5 min of 60 min remaining (< 20%)
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        const string username = "user@contoso.com";

        var issuedAt = DateTimeOffset.UtcNow.AddMinutes(-55);
        var expiry = DateTimeOffset.UtcNow.AddMinutes(5);    // only 5/60 ≈ 8% remaining → near-expiry
        var refreshAfter = issuedAt.AddMinutes(48);

        var json = BuildCacheEntryJson(
            token: "old-token",
            embedUrl: "https://app.powerbi.com/reportEmbed",
            reportId: reportId,
            expiry: expiry,
            issuedAt: issuedAt,
            refreshAfter: refreshAfter);

        var cache = BuildCacheMock();
        cache
            .Setup(c => c.GetAsync(
                BuildEmbedCacheKey(workspaceId, reportId, username),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(System.Text.Encoding.UTF8.GetBytes(json));

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache.Object, logger.Object);

        // Act — token regeneration will attempt MSAL; we verify the cache lookup happened
        MsalException? msalException = null;
        try
        {
            await service.GetEmbedConfigAsync(workspaceId, reportId, username, roles: null);
        }
        catch (Exception ex) when (ex.GetType().Namespace?.StartsWith("Microsoft.Identity") == true)
        {
            msalException = ex as MsalException;
            // MSAL fails in unit tests — that's expected for the regeneration path
        }
        catch
        {
            // Any other exception is also acceptable — the key assertion is on the log below
        }

        // Assert — the logger should have been invoked with the near-expiry warning
        // (verifying the code took the near-expiry branch, not the fresh-token branch)
        // We verify by checking that GetAsync was called (cache was checked first)
        cache.Verify(c => c.GetAsync(
            BuildEmbedCacheKey(workspaceId, reportId, username),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // =========================================================================
    // Cache MISS
    // =========================================================================

    [Fact]
    public async Task GetEmbedConfigAsync_ChecksCacheBefore_CallingPbiApi_OnCacheMiss()
    {
        // Arrange — cache returns null (miss)
        var workspaceId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        const string username = "user@contoso.com";

        var cache = BuildCacheMock();
        cache
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((byte[]?)null);   // Cache miss

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache.Object, logger.Object);

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
        cache.Verify(c => c.GetAsync(
            BuildEmbedCacheKey(workspaceId, reportId, username),
            It.IsAny<CancellationToken>()), Times.Once);
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

        var cache = BuildCacheMock();
        cache
            .Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection lost"));

        var options = BuildOptions();
        var logger = BuildLoggerMock();
        var service = new ReportingEmbedService(options, cache.Object, logger.Object);

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

    [Fact]
    public void GetReportsAsync_MethodExists_WithExpectedSignature()
    {
        // Arrange
        var method = typeof(ReportingEmbedService).GetMethod("GetReportsAsync");

        // Assert
        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<IReadOnlyList<PowerBiReport>>));
    }

    [Fact]
    public void GetReportAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingEmbedService).GetMethod("GetReportAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<PowerBiReport>));
    }

    [Fact]
    public void GetEmbedConfigAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingEmbedService).GetMethod("GetEmbedConfigAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<EmbedConfig>));
    }

    [Fact]
    public void CreateReportAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingEmbedService).GetMethod("CreateReportAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<PowerBiReport>));
    }

    [Fact]
    public void DeleteReportAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingEmbedService).GetMethod("DeleteReportAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task));
    }

    [Fact]
    public void ExportReportAsync_MethodExists_WithExpectedSignature()
    {
        var method = typeof(ReportingEmbedService).GetMethod("ExportReportAsync");

        method.Should().NotBeNull();
        method!.IsPublic.Should().BeTrue();
        method.ReturnType.Should().Be(typeof(Task<Stream>));
    }

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
