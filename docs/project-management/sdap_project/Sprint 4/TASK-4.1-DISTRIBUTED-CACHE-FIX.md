# Task 4.1: Fix Distributed Cache Configuration

**Sprint:** 4
**Priority:** 🔴 P0 BLOCKER
**Estimated Effort:** 4 hours
**Status:** Ready for Implementation
**Dependencies:** Azure Redis Cache provisioned

---

## Problem Statement

### Current State (BROKEN)
The application is configured with `AddDistributedMemoryCache()` in `Program.cs:184`, which is an **in-memory cache that is NOT distributed** across multiple application instances. This breaks the idempotency system for background job processing.

**Critical Impact:**
- Duplicate job processing in multi-instance deployments
- Cache coherence failures across pods/instances
- Violates ADR-009 (Redis First caching policy)
- Production deployment will fail to prevent duplicate document processing events

**Evidence:**
```csharp
// Program.cs line 184 - INCORRECT for production
builder.Services.AddDistributedMemoryCache(); // This is NOT distributed!
```

### Target State (CORRECT)
Use Redis-backed distributed cache via `AddStackExchangeRedisCache()` to ensure cache coherence across all application instances.

---

## Architecture Context

### ADR-009: Caching Policy - Redis First
The codebase follows a two-level caching strategy:
1. **L1 Cache (Per-Request):** `RequestCache` - scoped to HTTP request lifetime
2. **L2 Cache (Distributed):** Redis - shared across all instances for permissions, idempotency

**Critical Services Using Distributed Cache:**
- `IdempotencyService` (src/api/Spe.Bff.Api/Services/Jobs/IdempotencyService.cs)
  - Tracks processed job IDs to prevent duplicate execution
  - Uses distributed cache with 24-hour retention
- `DataverseAccessDataSource` (src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs)
  - Caches user access rights for 60 minutes
  - Uses distributed cache to reduce Dataverse API calls by ~80%

**Configuration Schema:**
```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": "{Azure-Redis-Connection-String}",
    "InstanceName": "sdap:",
    "DefaultExpirationMinutes": 60,
    "AbsoluteExpirationMinutes": 1440
  }
}
```

---

## Solution Design

### Step 1: Add NuGet Package
Add `Microsoft.Extensions.Caching.StackExchangeRedis` to the API project.

**File:** `src/api/Spe.Bff.Api/Spe.Bff.Api.csproj`

**Current Packages (relevant):**
```xml
<PackageReference Include="Microsoft.Extensions.Caching.Abstractions" />
```

**Add:**
```xml
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" />
```

**Alternative (if using Central Package Management):**
Add to `Directory.Packages.props`:
```xml
<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="8.0.0" />
```

---

### Step 2: Update Program.cs Configuration

**File:** `src/api/Spe.Bff.Api/Program.cs`

**Current Implementation (lines 180-187):**
```csharp
// 3. Distributed cache (for permissions, idempotency)
builder.Services.AddDistributedMemoryCache(); // TODO: Replace with Redis in production

builder.Services.AddSingleton<IdempotencyService>();
builder.Services.AddSingleton<JobSubmissionService>();
```

**Replacement Implementation:**
```csharp
// 3. Distributed cache (Redis for production, in-memory for local dev)
var redisEnabled = builder.Configuration.GetValue<bool>("Redis:Enabled");
if (redisEnabled)
{
    var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
        ?? builder.Configuration["Redis:ConnectionString"];

    if (string.IsNullOrWhiteSpace(redisConnectionString))
    {
        throw new InvalidOperationException(
            "Redis is enabled but no connection string found. " +
            "Set 'ConnectionStrings:Redis' or 'Redis:ConnectionString' in configuration.");
    }

    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = builder.Configuration["Redis:InstanceName"] ?? "sdap:";
    });

    builder.Logging.LogInformation(
        "Distributed cache: Redis enabled with instance name '{InstanceName}'",
        options.InstanceName);
}
else
{
    // Use in-memory cache for local development only
    builder.Services.AddDistributedMemoryCache();

    builder.Logging.LogWarning(
        "Distributed cache: Using in-memory cache (not distributed). " +
        "This should ONLY be used in local development.");
}

builder.Services.AddSingleton<IdempotencyService>();
builder.Services.AddSingleton<JobSubmissionService>();
```

**Key Design Decisions:**
1. **Configuration-driven:** Redis can be disabled for local development
2. **Fail-fast validation:** Throws exception if Redis enabled but connection string missing
3. **Dual connection string support:** Checks both `ConnectionStrings:Redis` and `Redis:ConnectionString`
4. **Logging:** Emits startup log indicating which cache implementation is active
5. **Instance name prefix:** Prevents cache key collisions in shared Redis instances

---

### Step 3: Update Configuration Files

#### appsettings.json (Development)
**File:** `src/api/Spe.Bff.Api/appsettings.json`

**Add to existing configuration:**
```json
{
  "Redis": {
    "Enabled": false,
    "ConnectionString": null,
    "InstanceName": "sdap-dev:",
    "DefaultExpirationMinutes": 60,
    "AbsoluteExpirationMinutes": 1440
  }
}
```

**Rationale:** Local development uses in-memory cache (no Redis required)

---

#### appsettings.Production.json
**File:** `src/api/Spe.Bff.Api/appsettings.Production.json` (create if doesn't exist)

**Add:**
```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": null,
    "InstanceName": "sdap-prod:",
    "DefaultExpirationMinutes": 60,
    "AbsoluteExpirationMinutes": 1440
  },
  "ConnectionStrings": {
    "Redis": null
  }
}
```

**Important:** Connection strings should come from Azure App Configuration or Key Vault, not committed to source control.

---

#### appsettings.Staging.json
**File:** `src/api/Spe.Bff.Api/appsettings.Staging.json` (create if doesn't exist)

**Add:**
```json
{
  "Redis": {
    "Enabled": true,
    "ConnectionString": null,
    "InstanceName": "sdap-staging:",
    "DefaultExpirationMinutes": 30,
    "AbsoluteExpirationMinutes": 720
  }
}
```

---

### Step 4: Add Configuration Options Class (Optional but Recommended)

**File:** `src/api/Spe.Bff.Api/Configuration/RedisOptions.cs` (create new)

```csharp
namespace Spe.Bff.Api.Configuration;

/// <summary>
/// Configuration options for Redis distributed cache.
/// </summary>
public sealed class RedisOptions
{
    public const string SectionName = "Redis";

    /// <summary>
    /// Whether Redis distributed cache is enabled. If false, falls back to in-memory cache.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Redis connection string (Azure Redis Cache or local Redis instance).
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>
    /// Redis key prefix to prevent collisions in shared instances (e.g., "sdap-prod:").
    /// </summary>
    public string InstanceName { get; init; } = "sdap:";

    /// <summary>
    /// Default cache expiration in minutes (sliding).
    /// </summary>
    public int DefaultExpirationMinutes { get; init; } = 60;

    /// <summary>
    /// Absolute expiration in minutes (maximum cache lifetime).
    /// </summary>
    public int AbsoluteExpirationMinutes { get; init; } = 1440;
}
```

**Then update Program.cs to use strongly-typed options:**
```csharp
var redisOptions = builder.Configuration.GetSection(RedisOptions.SectionName).Get<RedisOptions>()
    ?? new RedisOptions();

if (redisOptions.Enabled)
{
    // ... existing Redis setup code
}
```

---

### Step 5: Update DistributedCacheExtensions (Optional Enhancement)

**File:** `src/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs`

**Current Implementation:** Already has good extension methods for cache operations.

**Optional Addition:** Add cache statistics helper
```csharp
/// <summary>
/// Extension method to test Redis connectivity at startup.
/// </summary>
public static async Task<bool> TestConnectivityAsync(
    this IDistributedCache cache,
    CancellationToken cancellationToken = default)
{
    try
    {
        const string testKey = "_health_check_";
        await cache.SetStringAsync(testKey, "ok", cancellationToken);
        var result = await cache.GetStringAsync(testKey, cancellationToken);
        await cache.RemoveAsync(testKey, cancellationToken);
        return result == "ok";
    }
    catch
    {
        return false;
    }
}
```

---

## Testing Strategy

### Unit Tests

**File:** `tests/unit/Spe.Bff.Api.Tests/CacheTests.cs`

**Add new test:**
```csharp
[Fact]
public async Task IdempotencyService_WithDistributedCache_PreventsDoubleProcessing()
{
    // Arrange
    var services = new ServiceCollection();
    services.AddDistributedMemoryCache(); // Use in-memory for test
    services.AddSingleton<IdempotencyService>();
    var serviceProvider = services.BuildServiceProvider();
    var idempotencyService = serviceProvider.GetRequiredService<IdempotencyService>();

    var jobId = Guid.NewGuid().ToString();
    var ttl = TimeSpan.FromMinutes(5);

    // Act
    var firstAttempt = await idempotencyService.TryMarkAsProcessedAsync(jobId, ttl);
    var secondAttempt = await idempotencyService.TryMarkAsProcessedAsync(jobId, ttl);

    // Assert
    Assert.True(firstAttempt, "First attempt should succeed");
    Assert.False(secondAttempt, "Second attempt should be blocked by idempotency check");
}
```

---

### Integration Tests

**File:** `tests/integration/Spe.Integration.Tests/CacheIntegrationTests.cs` (create new)

```csharp
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Spe.Integration.Tests;

[Collection("Integration")]
public class CacheIntegrationTests : IClassFixture<IntegrationTestFixture>
{
    private readonly IntegrationTestFixture _fixture;

    public CacheIntegrationTests(IntegrationTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task DistributedCache_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var cache = _fixture.Services.GetRequiredService<IDistributedCache>();
        var key = $"test:{Guid.NewGuid()}";
        var value = "test-value";

        // Act
        await cache.SetStringAsync(key, value);
        var retrieved = await cache.GetStringAsync(key);

        // Assert
        Assert.Equal(value, retrieved);

        // Cleanup
        await cache.RemoveAsync(key);
    }

    [Fact]
    public async Task DistributedCache_Expiration_RemovesKeyAfterTtl()
    {
        // Arrange
        var cache = _fixture.Services.GetRequiredService<IDistributedCache>();
        var key = $"expiration-test:{Guid.NewGuid()}";
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(2)
        };

        // Act
        await cache.SetStringAsync(key, "value", options);
        var immediateGet = await cache.GetStringAsync(key);

        await Task.Delay(TimeSpan.FromSeconds(3)); // Wait for expiration

        var expiredGet = await cache.GetStringAsync(key);

        // Assert
        Assert.NotNull(immediateGet);
        Assert.Null(expiredGet);
    }
}
```

---

### Manual Testing

**1. Local Development (In-Memory Cache):**
```bash
cd src/api/Spe.Bff.Api
dotnet run
```

Check logs for:
```
warn: Distributed cache: Using in-memory cache (not distributed). This should ONLY be used in local development.
```

**2. Staging Environment (Redis):**
```bash
# Set environment variable
$env:Redis__Enabled="true"
$env:Redis__ConnectionString="your-redis-connection-string"

dotnet run
```

Check logs for:
```
info: Distributed cache: Redis enabled with instance name 'sdap-staging:'
```

**3. Redis Connectivity Test:**
```bash
# Test Redis connection using redis-cli
redis-cli -h your-redis.redis.cache.windows.net -p 6380 -a your-access-key --tls ping
# Expected: PONG
```

---

## Deployment Checklist

### Azure Redis Cache Provisioning

**PowerShell Script:**
```powershell
# Variables
$resourceGroup = "sdap-rg"
$redisName = "sdap-redis"
$location = "eastus"
$sku = "Basic"  # Basic (C0), Standard (C1), or Premium
$vmSize = "C0"  # C0=250MB, C1=1GB, C2=2.5GB

# Create Redis Cache
az redis create `
    --name $redisName `
    --resource-group $resourceGroup `
    --location $location `
    --sku $sku `
    --vm-size $vmSize `
    --enable-non-ssl-port false `
    --minimum-tls-version 1.2

# Get connection string
$primaryKey = az redis list-keys --name $redisName --resource-group $resourceGroup --query primaryKey -o tsv
$hostname = az redis show --name $redisName --resource-group $resourceGroup --query hostName -o tsv

Write-Host "Redis Connection String:"
Write-Host "$hostname:6380,password=$primaryKey,ssl=True,abortConnect=False"
```

**Estimated Cost:**
- Basic C0 (250MB): ~$16/month
- Standard C1 (1GB): ~$58/month
- Recommended: Start with Basic C0, monitor, upgrade if needed

---

### Azure App Service Configuration

**Add Application Settings (Azure Portal or CLI):**
```bash
az webapp config appsettings set `
    --name sdap-api-prod `
    --resource-group sdap-rg `
    --settings `
    Redis__Enabled=true `
    Redis__ConnectionString="your-redis-connection-string" `
    Redis__InstanceName="sdap-prod:"
```

**Alternative: Azure Key Vault Reference (Recommended):**
```bash
# Store in Key Vault
az keyvault secret set `
    --vault-name sdap-keyvault `
    --name RedisConnectionString `
    --value "your-redis-connection-string"

# Reference in App Service
az webapp config appsettings set `
    --name sdap-api-prod `
    --resource-group sdap-rg `
    --settings `
    Redis__ConnectionString="@Microsoft.KeyVault(SecretUri=https://sdap-keyvault.vault.azure.net/secrets/RedisConnectionString/)"
```

---

## Validation & Verification

### Success Criteria

✅ **Build & Compilation:**
- [ ] `dotnet build Spaarke.sln` completes with 0 errors
- [ ] No new NuGet package conflicts

✅ **Unit Tests:**
- [ ] All existing cache tests pass
- [ ] New idempotency tests pass

✅ **Integration Tests:**
- [ ] Cache set/get operations work
- [ ] Cache expiration works correctly
- [ ] No Redis connection errors in logs

✅ **Functional Tests:**
- [ ] Submit duplicate job with same JobId - second attempt should be blocked
- [ ] Restart API instance - cache data persists across restarts
- [ ] Deploy to multi-instance environment - cache shared across all instances

✅ **Production Readiness:**
- [ ] Redis connection string stored in Key Vault
- [ ] Logs confirm Redis cache is active (not in-memory)
- [ ] Application Insights shows no cache-related errors

---

## Rollback Plan

**If Redis Integration Fails:**

1. **Immediate Fallback:**
   ```json
   {
     "Redis": {
       "Enabled": false
     }
   }
   ```

2. **Restart application** - falls back to in-memory cache

3. **Impact of Fallback:**
   - Idempotency only works within single instance
   - Permissions cache not shared across instances
   - Increased Dataverse API calls (~5x)

**Monitoring After Rollback:**
- Watch for duplicate job processing in logs
- Monitor Dataverse throttling (429 responses)

---

## Known Issues & Limitations

### Issue #1: Redis Connection Failures
**Symptom:** Application crashes on startup if Redis is enabled but unreachable

**Mitigation:**
Add connection resilience in Program.cs:
```csharp
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString;
    options.ConfigurationOptions = new ConfigurationOptions
    {
        AbortOnConnectFail = false, // Don't crash on connection failure
        ConnectTimeout = 5000,
        SyncTimeout = 5000
    };
});
```

### Issue #2: Cache Key Collisions
**Symptom:** Staging and production share Redis instance, keys collide

**Mitigation:** Enforce unique `InstanceName` per environment:
- Development: `sdap-dev:`
- Staging: `sdap-staging:`
- Production: `sdap-prod:`

### Issue #3: Redis Memory Limits
**Symptom:** Cache evictions under load

**Monitoring:**
```bash
redis-cli -h your-redis.redis.cache.windows.net -p 6380 -a your-key --tls INFO memory
```

**Resolution:** Upgrade to larger VM size or implement LRU eviction policy

---

## References

### Documentation
- [Microsoft Docs: Distributed Caching](https://learn.microsoft.com/en-us/aspnet/core/performance/caching/distributed)
- [StackExchange.Redis Configuration](https://stackexchange.github.io/StackExchange.Redis/Configuration.html)
- [Azure Redis Cache Docs](https://learn.microsoft.com/en-us/azure/azure-cache-for-redis/)

### Related ADRs
- **ADR-009:** Caching Policy - Redis First
- **ADR-004:** Async Job Contract (Idempotency)

### Related Files
- `src/api/Spe.Bff.Api/Program.cs` (lines 180-187)
- `src/api/Spe.Bff.Api/Services/Jobs/IdempotencyService.cs`
- `src/shared/Spaarke.Dataverse/DataverseAccessDataSource.cs`
- `src/shared/Spaarke.Core/Cache/DistributedCacheExtensions.cs`

---

## AI Implementation Prompt

**Copy this prompt to your AI coding assistant:**

```
Implement distributed Redis cache to fix broken in-memory cache configuration.

CONTEXT:
- Application currently uses AddDistributedMemoryCache() which is NOT distributed
- Breaks idempotency in multi-instance deployments
- Must use AddStackExchangeRedisCache() for production

TASKS:
1. Add Microsoft.Extensions.Caching.StackExchangeRedis NuGet package to Spe.Bff.Api.csproj
2. Update src/api/Spe.Bff.Api/Program.cs lines 180-187:
   - Add Redis configuration with Enabled flag check
   - Use AddStackExchangeRedisCache() when Redis:Enabled=true
   - Fall back to AddDistributedMemoryCache() when Redis:Enabled=false
   - Add logging to indicate which cache is active
   - Add connection string validation
3. Update src/api/Spe.Bff.Api/appsettings.json:
   - Add Redis section with Enabled=false for local dev
4. Create src/api/Spe.Bff.Api/appsettings.Production.json:
   - Add Redis section with Enabled=true
5. Run dotnet build and verify no errors

VALIDATION:
- Build succeeds with 0 errors
- Startup logs show "Using in-memory cache" in development
- Configuration ready for Redis connection string injection in production

Reference the code examples in Step 2 of this task document.
```

---

**Task Owner:** [Assign to developer]
**Reviewer:** [Assign to senior developer/architect]
**Created:** 2025-10-02
**Updated:** 2025-10-02
