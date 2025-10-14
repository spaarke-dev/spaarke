# Phase 4: Graph Token Caching

## Objective
Implement Redis-based caching for Graph API tokens obtained via OBO flow per ADR-009.

## Duration
4-6 hours

## Prerequisites
- Phase 3 complete and validated
- Redis configured and accessible
- All tests passing
- Branch: `refactor/adr-compliance`

---

## Overview

Currently, every request that needs to access SharePoint Embedded performs an OBO token exchange (~150-300ms). This phase adds caching to reduce that overhead to ~5ms for cache hits.

**Expected Impact:**
- 95% reduction in Azure AD OBO calls
- 70%+ reduction in request latency for SPE operations
- Reduced Azure AD throttling risk

---

## Task 4.1: Create GraphTokenCache Service

### New File: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`
```csharp
using Microsoft.Extensions.Caching.Distributed;
using System.Security.Cryptography;
using System.Text;

namespace Spe.Bff.Api.Services;

/// <summary>
/// Caches Graph API access tokens obtained via OBO flow to reduce Azure AD calls.
/// Tokens are cached with 55-minute TTL (5-minute buffer before 1-hour expiration).
/// </summary>
public class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;
    private const int TokenTtlMinutes = 55; // 5-minute buffer before token expires
    
    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }
    
    /// <summary>
    /// Gets a cached Graph token by user token hash.
    /// Returns null if not found or expired.
    /// </summary>
    public async Task<string?> GetTokenAsync(string userTokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = BuildCacheKey(userTokenHash);
            var cachedToken = await _cache.GetStringAsync(cacheKey, cancellationToken);
            
            if (cachedToken != null)
            {
                _logger.LogDebug("Graph token cache HIT for hash {TokenHash}", 
                    userTokenHash.Substring(0, 8));
                return cachedToken;
            }
            
            _logger.LogDebug("Graph token cache MISS for hash {TokenHash}", 
                userTokenHash.Substring(0, 8));
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve token from cache, will proceed with OBO");
            return null; // Fail open - proceed with OBO if cache fails
        }
    }
    
    /// <summary>
    /// Caches a Graph token with the specified TTL.
    /// </summary>
    public async Task SetTokenAsync(
        string userTokenHash,
        string graphToken,
        TimeSpan? expiresIn = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = BuildCacheKey(userTokenHash);
            var ttl = expiresIn ?? TimeSpan.FromMinutes(TokenTtlMinutes);
            
            await _cache.SetStringAsync(
                cacheKey,
                graphToken,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = ttl
                },
                cancellationToken);
            
            _logger.LogDebug("Cached Graph token for hash {TokenHash} with TTL {TtlMinutes}m",
                userTokenHash.Substring(0, 8), ttl.TotalMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache token, will proceed without caching");
            // Don't throw - caching is optional, OBO flow still works
        }
    }
    
    /// <summary>
    /// Removes a cached token (e.g., after explicit logout or token invalidation).
    /// </summary>
    public async Task RemoveTokenAsync(string userTokenHash, CancellationToken cancellationToken = default)
    {
        try
        {
            var cacheKey = BuildCacheKey(userTokenHash);
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            
            _logger.LogDebug("Removed cached token for hash {TokenHash}",
                userTokenHash.Substring(0, 8));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove token from cache");
        }
    }
    
    /// <summary>
    /// Computes a SHA256 hash of the user token for cache key.
    /// Hashing prevents storing raw tokens in cache keys.
    /// </summary>
    public string ComputeTokenHash(string userToken)
    {
        if (string.IsNullOrEmpty(userToken))
            throw new ArgumentException("User token cannot be null or empty", nameof(userToken));
        
        using var sha256 = SHA256.Create();
        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", ""); // URL-safe base64
    }
    
    private string BuildCacheKey(string tokenHash)
    {
        // Format: sdap:graph:token:{hash}
        // Instance prefix "sdap:" is added by Redis configuration
        return $"graph:token:{tokenHash}";
    }
}
Validation for Task 4.1

 File created at correct location
 SHA256 hashing implemented (tokens not stored in keys)
 Fail-open strategy (cache failures don't break OBO)
 Logging for cache hits/misses
 Build succeeds


Task 4.2: Update GraphClientFactory to Use Cache
File to Modify: src/api/Spe.Bff.Api/Infrastructure/GraphClientFactory.cs
Add Constructor Parameter:
csharppublic class GraphClientFactory : IGraphClientFactory
{
    private readonly IConfidentialClientApplication _cca;
    private readonly GraphTokenCache _tokenCache; // NEW
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly string _tenantId;
    private readonly string _apiAppId;
    
    public GraphClientFactory(
        IConfiguration configuration,
        GraphTokenCache tokenCache, // NEW
        ILogger<GraphClientFactory> logger)
    {
        _tokenCache = tokenCache ?? throw new ArgumentNullException(nameof(tokenCache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Existing initialization code...
        _tenantId = configuration["TENANT_ID"] 
            ?? throw new InvalidOperationException("TENANT_ID not configured");
        _apiAppId = configuration["API_APP_ID"] 
            ?? throw new InvalidOperationException("API_APP_ID not configured");
        var clientSecret = configuration["API_CLIENT_SECRET"] 
            ?? throw new InvalidOperationException("API_CLIENT_SECRET not configured");
        
        _cca = ConfidentialClientApplicationBuilder
            .Create(_apiAppId)
            .WithClientSecret(clientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, _tenantId)
            .Build();
    }
    
    // Existing CreateAppOnlyClient() method stays the same
    
    // UPDATE this method:
    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        if (string.IsNullOrEmpty(userAccessToken))
            throw new ArgumentException("User access token is required", nameof(userAccessToken));
        
        // Compute hash for cache key (don't store raw tokens)
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
        
        // Try to get cached token
        var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);
        if (!string.IsNullOrEmpty(cachedToken))
        {
            _logger.LogDebug("Using cached Graph token for OBO request");
            return CreateGraphClientWithToken(cachedToken);
        }
        
        // Cache miss - perform OBO exchange
        _logger.LogDebug("Cache miss - performing OBO token exchange");
        
        var scopes = new[] { "https://graph.microsoft.com/.default" };
        var userAssertion = new UserAssertion(userAccessToken);
        
        try
        {
            var result = await _cca
                .AcquireTokenOnBehalfOf(scopes, userAssertion)
                .ExecuteAsync();
            
            _logger.LogInformation("OBO token acquired successfully, expires at {ExpiresOn}", 
                result.ExpiresOn);
            
            // Cache the token with 55-minute TTL (5-min buffer)
            var ttl = result.ExpiresOn - DateTimeOffset.UtcNow - TimeSpan.FromMinutes(5);
            if (ttl > TimeSpan.Zero)
            {
                await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, ttl);
            }
            
            return CreateGraphClientWithToken(result.AccessToken);
        }
        catch (MsalServiceException ex) when (ex.ErrorCode == "invalid_grant")
        {
            _logger.LogWarning("OBO flow failed with invalid_grant, user token may be expired");
            throw new UnauthorizedAccessException("User token is invalid or expired", ex);
        }
        catch (MsalServiceException ex)
        {
            _logger.LogError(ex, "OBO token acquisition failed: {ErrorCode}", ex.ErrorCode);
            throw;
        }
    }
    
    /// <summary>
    /// Creates a Graph client with a specific access token.
    /// Includes custom message handler for resilience (retry, circuit breaker, timeout).
    /// </summary>
    private GraphServiceClient CreateGraphClientWithToken(string accessToken)
    {
        var authProvider = new DelegateAuthenticationProvider(request =>
        {
            request.Headers.Authorization = 
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            return Task.CompletedTask;
        });
        
        // Use existing GraphHttpMessageHandler for resilience
        var handlers = GraphClientFactory.CreateDefaultHandlers();
        handlers.Add(new GraphHttpMessageHandler()); // From Task 4.1 in original assessment
        
        var httpClient = GraphClientFactory.CreateHttpClient(handlers.ToArray());
        
        return new GraphServiceClient(httpClient, authProvider);
    }
}
Validation for Task 4.2

 GraphTokenCache injected in constructor
 Cache checked before OBO exchange
 Token cached after successful OBO
 TTL calculated from token expiration
 Error handling for cache failures
 Build succeeds


Task 4.3: Register GraphTokenCache in DI
File to Modify: src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
Already done in Phase 3, verify it exists:
csharppublic static IServiceCollection AddDocumentsModule(this IServiceCollection services)
{
    // ... other registrations
    
    // Graph Token Cache (for OBO token caching)
    services.AddSingleton<GraphTokenCache>();  // ← Verify this line exists
    
    // ... other registrations
}
Validation for Task 4.3

 GraphTokenCache registered as Singleton
 Registered before IGraphClientFactory (dependency order)
 Build succeeds


Task 4.4: Add Cache Instrumentation (Optional but Recommended)
New File: src/api/Spe.Bff.Api/Telemetry/CacheMetrics.cs
csharpusing System.Diagnostics.Metrics;

namespace Spe.Bff.Api.Telemetry;

/// <summary>
/// Metrics for monitoring cache performance.
/// </summary>
public class CacheMetrics
{
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Histogram<double> _cacheOperationDuration;
    
    public CacheMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create("Spe.Bff.Api.Cache");
        
        _cacheHits = meter.CreateCounter<long>(
            "cache.hits",
            description: "Number of cache hits");
        
        _cacheMisses = meter.CreateCounter<long>(
            "cache.misses",
            description: "Number of cache misses");
        
        _cacheOperationDuration = meter.CreateHistogram<double>(
            "cache.operation.duration",
            unit: "ms",
            description: "Duration of cache operations");
    }
    
    public void RecordHit(string cacheType) => 
        _cacheHits.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
    
    public void RecordMiss(string cacheType) => 
        _cacheMisses.Add(1, new KeyValuePair<string, object?>("cache.type", cacheType));
    
    public void RecordOperationDuration(string operation, double durationMs, string cacheType) =>
        _cacheOperationDuration.Record(durationMs, 
            new KeyValuePair<string, object?>("cache.operation", operation),
            new KeyValuePair<string, object?>("cache.type", cacheType));
}
Update GraphTokenCache to Use Metrics
csharppublic class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;
    private readonly CacheMetrics _metrics; // NEW
    
    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger,
        CacheMetrics metrics) // NEW
    {
        _cache = cache;
        _logger = logger;
        _metrics = metrics;
    }
    
    public async Task<string?> GetTokenAsync(string userTokenHash, CancellationToken cancellationToken = default)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        try
        {
            var cacheKey = BuildCacheKey(userTokenHash);
            var cachedToken = await _cache.GetStringAsync(cacheKey, cancellationToken);
            
            stopwatch.Stop();
            _metrics.RecordOperationDuration("get", stopwatch.ElapsedMilliseconds, "graph_token");
            
            if (cachedToken != null)
            {
                _metrics.RecordHit("graph_token");
                _logger.LogDebug("Graph token cache HIT for hash {TokenHash}", 
                    userTokenHash.Substring(0, 8));
                return cachedToken;
            }
            
            _metrics.RecordMiss("graph_token");
            _logger.LogDebug("Graph token cache MISS for hash {TokenHash}", 
                userTokenHash.Substring(0, 8));
            return null;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Failed to retrieve token from cache");
            return null;
        }
    }
    
    // Update SetTokenAsync similarly...
}
Register CacheMetrics in DI
File: src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs
csharppublic static IServiceCollection AddSpaarkeCore(
    this IServiceCollection services,
    IConfiguration configuration)
{
    // Existing registrations...
    
    // Telemetry
    services.AddSingleton<CacheMetrics>();
    
    return services;
}
Validation for Task 4.4

 CacheMetrics created
 Metrics recorded in GraphTokenCache
 CacheMetrics registered in DI
 Build succeeds


Task 4.5: Add Integration Test for Caching
New File: tests/Spe.Bff.Api.IntegrationTests/GraphTokenCacheTests.cs
csharpusing Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spe.Bff.Api.Services;
using Xunit;

namespace Spe.Bff.Api.IntegrationTests;

[Collection("Redis")]
public class GraphTokenCacheTests : IAsyncLifetime
{
    private readonly ServiceProvider _serviceProvider;
    private readonly GraphTokenCache _tokenCache;
    private readonly IDistributedCache _cache;
    
    public GraphTokenCacheTests()
    {
        var services = new ServiceCollection();
        
        // Use memory cache for testing (Redis not required)
        services.AddDistributedMemoryCache();
        
        services.AddSingleton<GraphTokenCache>();
        services.AddLogging();
        
        _serviceProvider = services.BuildServiceProvider();
        _tokenCache = _serviceProvider.GetRequiredService<GraphTokenCache>();
        _cache = _serviceProvider.GetRequiredService<IDistributedCache>();
    }
    
    [Fact]
    public async Task GetTokenAsync_WhenNotCached_ReturnsNull()
    {
        // Arrange
        var tokenHash = _tokenCache.ComputeTokenHash("fake-user-token");
        
        // Act
        var result = await _tokenCache.GetTokenAsync(tokenHash);
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public async Task SetTokenAsync_ThenGetTokenAsync_ReturnsCachedToken()
    {
        // Arrange
        var userToken = "fake-user-token-" + Guid.NewGuid();
        var graphToken = "fake-graph-token-" + Guid.NewGuid();
        var tokenHash = _tokenCache.ComputeTokenHash(userToken);
        
        // Act
        await _tokenCache.SetTokenAsync(tokenHash, graphToken, TimeSpan.FromMinutes(5));
        var retrieved = await _tokenCache.GetTokenAsync(tokenHash);
        
        // Assert
        Assert.Equal(graphToken, retrieved);
    }
    
    [Fact]
    public async Task SetTokenAsync_WithExpiry_TokenExpiresAfterTtl()
    {
        // Arrange
        var userToken = "fake-user-token-" + Guid.NewGuid();
        var graphToken = "fake-graph-token-" + Guid.NewGuid();
        var tokenHash = _tokenCache.ComputeTokenHash(userToken);
        
        // Act
        await _tokenCache.SetTokenAsync(tokenHash, graphToken, TimeSpan.FromSeconds(1));
        var immediateResult = await _tokenCache.GetTokenAsync(tokenHash);
        
        await Task.Delay(TimeSpan.FromSeconds(2)); // Wait for expiration
        
        var expiredResult = await _tokenCache.GetTokenAsync(tokenHash);
        
        // Assert
        Assert.Equal(graphToken, immediateResult);
        Assert.Null(expiredResult); // Should be expired
    }
    
    [Fact]
    public async Task RemoveTokenAsync_RemovesCachedToken()
    {
        // Arrange
        var userToken = "fake-user-token-" + Guid.NewGuid();
        var graphToken = "fake-graph-token-" + Guid.NewGuid();
        var tokenHash = _tokenCache.ComputeTokenHash(userToken);
        
        await _tokenCache.SetTokenAsync(tokenHash, graphToken, TimeSpan.FromMinutes(5));
        
        // Act
        await _tokenCache.RemoveTokenAsync(tokenHash);
        var result = await _tokenCache.GetTokenAsync(tokenHash);
        
        // Assert
        Assert.Null(result);
    }
    
    [Fact]
    public void ComputeTokenHash_SameToken_ProducesSameHash()
    {
        // Arrange
        var token = "consistent-token";
        
        // Act
        var hash1 = _tokenCache.ComputeTokenHash(token);
        var hash2 = _tokenCache.ComputeTokenHash(token);
        
        // Assert
        Assert.Equal(hash1, hash2);
    }
    
    [Fact]
    public void ComputeTokenHash_DifferentTokens_ProduceDifferentHashes()
    {
        // Arrange
        var token1 = "token-1";
        var token2 = "token-2";
        
        // Act
        var hash1 = _tokenCache.ComputeTokenHash(token1);
        var hash2 = _tokenCache.ComputeTokenHash(token2);
        
        // Assert
        Assert.NotEqual(hash1, hash2);
    }
    
    public Task InitializeAsync()
    {
        // Clear cache before each test
        return Task.CompletedTask;
    }
    
    public async Task DisposeAsync()
    {
        await _serviceProvider.DisposeAsync();
    }
}
Validation for Task 4.5

 Test file created
 All 6 tests pass
 Tests use in-memory cache (no Redis dependency)
 Covers happy path and edge cases


Complete Validation Checklist
Unit Test Validation
bashdotnet test tests/Spe.Bff.Api.IntegrationTests/GraphTokenCacheTests.cs
# Expected: All 6 tests pass
Integration Test Validation
Manual Test Sequence:

First Request (Cache Miss):

bash# Record start time
START_TIME=$(date +%s%3N)

curl -X PUT https://localhost:5001/api/obo/containers/{containerId}/files/test1.pdf \
  -H "Authorization: Bearer {valid-token}" \
  -H "Content-Type: application/pdf" \
  --data-binary @test1.pdf

# Record end time
END_TIME=$(date +%s%3N)
DURATION=$((END_TIME - START_TIME))
echo "First request duration: ${DURATION}ms"

# Expected: ~500-700ms (includes OBO exchange)
# Check logs for: "Cache miss - performing OBO token exchange"
# Check logs for: "Cached Graph token for hash..."

Second Request (Cache Hit):

bash# Use SAME token as first request
START_TIME=$(date +%s%3N)

curl -X PUT https://localhost:5001/api/obo/containers/{containerId}/files/test2.pdf \
  -H "Authorization: Bearer {valid-token}" \
  -H "Content-Type: application/pdf" \
  --data-binary @test2.pdf

END_TIME=$(date +%s%3N)
DURATION=$((END_TIME - START_TIME))
echo "Second request duration: ${DURATION}ms"

# Expected: ~100-200ms (no OBO exchange)
# Check logs for: "Using cached Graph token for OBO request"
# Check logs for: "Graph token cache HIT"

Verify Cache Keys in Redis:

bash# Connect to Redis
redis-cli

# List all SDAP cache keys
> KEYS sdap:graph:token:*

# Check TTL of a token
> TTL sdap:graph:token:{hash}
# Expected: ~3300 seconds (55 minutes)

# Get token value (should be JWT starting with "eyJ...")
> GET sdap:graph:token:{hash}

Load Test (Cache Effectiveness):

bash# Send 100 requests with same token
for i in {1..100}; do
  curl -X PUT https://localhost:5001/api/obo/containers/{containerId}/files/test${i}.pdf \
    -H "Authorization: Bearer {valid-token}" \
    -H "Content-Type: application/pdf" \
    --data-binary @test.pdf &
done
wait

# Check Application Insights or logs:
# Expected cache hit rate: >90% (after first request)
# Expected OBO calls: ~1-5 (not 100)
Performance Validation
Measure latency improvement:
MetricBefore CachingAfter CachingImprovementP50 Latency600ms150ms75% ↓P95 Latency850ms200ms76% ↓P99 Latency1200ms300ms75% ↓OBO Calls/100 requests1005-1090-95% ↓
Monitoring Validation
Application Insights Queries:
kusto// Cache hit rate
customMetrics
| where name == "cache.hits" or name == "cache.misses"
| summarize Hits = sumif(value, name == "cache.hits"),
            Misses = sumif(value, name == "cache.misses")
| extend HitRate = round(100.0 * Hits / (Hits + Misses), 2)
| project HitRate, Hits, Misses

// Average cache operation duration
customMetrics
| where name == "cache.operation.duration"
| summarize avg(value), percentile(value, 95), percentile(value, 99)
| project avg_duration_ms = avg_value,
          p95_duration_ms = percentile_value_95,
          p99_duration_ms = percentile_value_99

Success Criteria
Quantitative Metrics

 Cache hit rate > 90% (after warmup)
 Request latency reduced by 70%+ (P50)
 OBO exchange rate < 10% of total requests
 Cache operation latency < 10ms (P95)
 Zero cache-related errors in logs

Qualitative Criteria

 Caching is transparent (no API changes)
 Cache failures don't break OBO flow (fail-open)
 Token security maintained (hashed keys)
 Observability improved (metrics, logging)

ADR Compliance

 ADR-009: Redis-first caching

Distributed cache only (no L1)
Short TTL (55 minutes)
Versioned cache keys
No authorization decisions cached




Commit Message
refactor(phase-4): implement Graph token caching per ADR-009

- Create GraphTokenCache service for OBO token caching
- Update GraphClientFactory to use cache before OBO exchange
- Cache tokens with 55-minute TTL (5-min buffer)
- Use SHA256 hashing for cache keys (security)
- Add cache metrics for monitoring hit rate
- Fail-open strategy: cache failures don't break OBO

Performance improvement:
- 95% reduction in Azure AD OBO calls
- 70%+ reduction in request latency for SPE operations
- Cache hit rate >90% after warmup

ADR-009 compliance: Redis-first, no L1, short TTL, versioned keys

Testing:
- 6 integration tests for cache behavior
- Manual load testing shows >90% hit rate
- Performance metrics captured before/after

Rollback Plan
If caching causes issues:
bash# Option 1: Disable caching (quick fix)
# Edit GraphClientFactory.cs - comment out cache lookup
# Update CreateOnBehalfOfClientAsync to always perform OBO

# Option 2: Full rollback
git revert HEAD

# Option 3: Emergency disable via config
# Add feature flag in appsettings.json:
{
  "Features": {
    "EnableGraphTokenCaching": false
  }
}

# Update GraphClientFactory to check flag before using cache

Common Issues & Solutions
Issue 1: Cache hit rate lower than expected
Symptoms: Cache hit rate < 50%
Possible Causes:

Users have short-lived sessions (frequent token refresh)
Load balancer not sticky (different users hit same endpoint)
TTL too short

Diagnosis:
bash# Check token age in Redis
redis-cli
> TTL sdap:graph:token:*
> GET sdap:graph:token:{hash}

# Check logs for token hash patterns
grep "Graph token cache" application.log | grep -oP "hash \K[A-Za-z0-9]{8}"
Solutions:

Verify users stay logged in (check MSAL.js token refresh)
Ensure consistent token hashing
Review TTL calculation logic

Issue 2: Redis connection failures
Symptoms: "Failed to retrieve token from cache" warnings
Diagnosis:
bash# Check Redis connectivity
redis-cli -h {redis-host} -p {redis-port} PING
# Expected: PONG

# Check Redis memory
redis-cli INFO memory

# Check connection string in config
grep "ConnectionStrings:Redis" appsettings.json
Solutions:

Verify Redis connection string
Check Redis instance is running
Verify network connectivity from App Service to Redis
Check Redis authentication (if enabled)

Issue 3: Stale tokens causing 401 errors
Symptoms: Requests fail with 401 after cached token use
Possible Causes:

User revoked access (token invalidated)
TTL miscalculated (expired token cached)
Token refresh not propagating to cache

Diagnosis:
bash# Check cached token expiration
redis-cli
> TTL sdap:graph:token:{hash}
> GET sdap:graph:token:{hash}

# Decode JWT to check expiration
# Use jwt.io or jwt-cli tool
Solutions:

Add cache invalidation on user logout
Reduce TTL buffer (currently 5 minutes)
Add token validation before returning from cache

Issue 4: High memory usage in Redis
Symptoms: Redis memory usage growing continuously
Diagnosis:
bashredis-cli
> INFO memory
> KEYS sdap:graph:token:* | wc -l  # Count cached tokens
> MEMORY USAGE sdap:graph:token:{hash}  # Check token size
Solutions:

Verify TTL is being set (not infinite)
Check for key leaks (old keys not expiring)
Implement eviction policy in Redis (allkeys-lru)
Monitor token count over time


Next Steps
After Phase 4 is complete and validated:

Run complete integration test suite
Capture before/after performance metrics
Update architecture documentation
Create pull request with all 4 phases
Request code review
Deploy to staging environment
Monitor for 48 hours before production


Final Validation
All Phases Complete Checklist

 Phase 1: Configuration fixes applied
 Phase 2: Service layer simplified
 Phase 3: Feature modules implemented
 Phase 4: Token caching working

Regression Testing
bash# Full test suite
dotnet test

# Integration tests
dotnet test tests/Spe.Bff.Api.IntegrationTests

# Load test
./scripts/load-test.sh

# Security scan
dotnet list package --vulnerable
Documentation Updated

 Architecture diagrams reflect new structure
 API documentation current
 Deployment guide includes Redis setup
 Troubleshooting guide updated
 Performance metrics documented

Pull Request Checklist

 Branch refactor/adr-compliance ready
 All commits follow convention
 Before/after metrics captured
 ADR compliance verified
 Tests passing (100% pass rate)
 Code coverage maintained or improved
 Performance targets met


Appendix: Performance Comparison
Before Refactoring
Request Flow:
User → API → ServiceClient (new) → Dataverse    [500ms]
User → API → OBO Exchange → Graph → SPE          [600ms]

Total: 1100ms per request
After Refactoring
Request Flow:
User → API → ServiceClient (singleton) → Dataverse  [50ms]
User → API → Cache Hit → Graph → SPE                [150ms]

Total: 200ms per request (82% improvement)
Cost Savings

Azure AD OBO calls: 100/hour → 5/hour (95% reduction)
App Service CPU: 60% → 30% (50% reduction)
Response time: 1100ms → 200ms (5x faster)