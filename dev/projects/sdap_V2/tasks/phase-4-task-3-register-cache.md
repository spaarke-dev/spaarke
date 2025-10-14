# Phase 4 - Task 3: Register GraphTokenCache in DI

**Phase**: 4 (Token Caching)
**Duration**: 15-30 minutes
**Risk**: Medium (DI registration - incorrect lifetime could cause issues)
**Patterns**: [di-feature-module.md](../patterns/di-feature-module.md)
**Anti-Patterns**: [anti-pattern-captive-dependency.md](../patterns/anti-pattern-captive-dependency.md)

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 4 of the SDAP BFF API refactoring, specifically registering GraphTokenCache in the DI container.

TASK: Register GraphTokenCache as Singleton in DocumentsModule (Infrastructure/DI/DocumentsModule.cs), ensuring it's available to GraphClientFactory.

CRITICAL DISCOVERY:
- Documents module already exists: src/api/Spe.Bff.Api/Infrastructure/DI/DocumentsModule.cs
- NOT in Extensions folder (Extensions was the original plan, DI is actual location)
- Module already registers: ContainerOperations, DriveItemOperations, UploadSessionManager, UserOperations, SpeFileStore
- Module is ALREADY called in Program.cs (line 181)

CONSTRAINTS:
- Must register as Singleton (shared cache, stateless, no per-request state)
- Must register in Infrastructure/DI/DocumentsModule.cs (NOT Extensions/DocumentsModule.Extensions.cs)
- Must register BEFORE any services that depend on it
- Must avoid captive dependency (Singleton → Singleton only, GraphTokenCache injects IDistributedCache which is Singleton)
- Must preserve existing module structure
- GraphClientFactory is registered elsewhere (likely in Graph module or Program.cs directly)

VERIFICATION BEFORE STARTING:
1. ✅ Verify Phase 4 Task 2 complete (cache integrated in GraphClientFactory)
2. ✅ Verify DocumentsModule.cs exists (confirmed: Infrastructure/DI/DocumentsModule.cs)
3. ✅ Verify Redis is ALREADY configured in Program.cs (lines 187-237)
4. ✅ Verify IDistributedCache is ALREADY registered (Singleton)
5. If any verification fails, STOP and complete previous tasks first

INTEGRATION STEPS:
1. Add using: using Spe.Bff.Api.Services;
2. Add registration: services.AddSingleton<GraphTokenCache>();
3. Place BEFORE other SPE operations (at top of SPE section)
4. Verify build succeeds
5. Verify application starts (no DI resolution errors)

FOCUS: Stay focused on DI registration only. Do NOT add metrics or monitoring (that's Task 4.4).
```

---

## Goal

Register **GraphTokenCache** as Singleton in DI container, making it available to **GraphClientFactory**.

**Registration Requirements**:
- **Lifetime**: Singleton (shared cache, stateless)
- **Location**: DocumentsModule.Extensions.cs
- **Order**: Before GraphClientFactory (dependency order)

**Why Singleton**: Cache is stateless and should be shared across all requests

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 4 Task 2 complete
- [ ] Check GraphClientFactory injects GraphTokenCache
grep "GraphTokenCache" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

# 2. Verify DocumentsModule.Extensions.cs exists
- [ ] ls src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs

# 3. Verify GraphClientFactory registered in module
- [ ] grep "IGraphClientFactory" src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
# Expected: Should find registration

# 4. Verify Redis configured in SpaarkeCore.Extensions.cs (or elsewhere)
- [ ] grep "AddStackExchangeRedisCache" src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs
# Expected: Should find Redis setup
```

**If any verification fails**: STOP and complete previous tasks first.

---

## Files to Edit

```bash
- [ ] src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
```

---

## Implementation

### Before (OLD - No GraphTokenCache)

```csharp
using Spaarke.Dataverse;
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Infrastructure.Graph;

namespace Spe.Bff.Api.Extensions;

public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // SPE file store
        services.AddScoped<SpeFileStore>();

        // Graph API
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();
        services.AddScoped<UploadSessionManager>();

        // Dataverse
        services.AddSingleton<DataverseServiceClientImpl>(sp => { ... });
        services.AddScoped<DataverseWebApiService>();

        return services;
    }
}
```

### After (NEW - With GraphTokenCache)

```csharp
using Spaarke.Dataverse;
using Spe.Bff.Api.Storage;
using Spe.Bff.Api.Infrastructure.Graph;
using Spe.Bff.Api.Services;  // ✨ NEW: For GraphTokenCache

namespace Spe.Bff.Api.Extensions;

/// <summary>
/// Registers SPE file operations, Graph API, and Dataverse services (ADR-010: DI Minimalism).
/// </summary>
public static class DocumentsModuleExtensions
{
    public static IServiceCollection AddDocumentsModule(this IServiceCollection services)
    {
        // ============================================================================
        // SPE File Storage (Scoped - may hold per-request context)
        // ============================================================================
        services.AddScoped<SpeFileStore>();

        // ============================================================================
        // Graph API (Factory pattern - Singleton, stateless)
        // ============================================================================

        // ✨ NEW: Graph token cache (Singleton - shared cache, stateless)
        // Must be registered BEFORE GraphClientFactory (dependency order)
        services.AddSingleton<GraphTokenCache>();

        // Graph client factory (Singleton - factory pattern, injects GraphTokenCache)
        services.AddSingleton<IGraphClientFactory, GraphClientFactory>();

        // Upload session manager (Scoped - per-request upload tracking)
        services.AddScoped<UploadSessionManager>();

        // ============================================================================
        // Dataverse Connection (Singleton - connection pooling, thread-safe SDK)
        // ============================================================================
        services.AddSingleton<DataverseServiceClientImpl>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<DataverseOptions>>().Value;

            var connectionString =
                $"AuthType=ClientSecret;" +
                $"Url={options.ServiceUrl};" +
                $"ClientId={options.ClientId};" +
                $"ClientSecret={options.ClientSecret};" +
                $"RequireNewInstance=false;";

            return new DataverseServiceClientImpl(connectionString);
        });

        // Dataverse Web API service (Scoped - per-request queries)
        services.AddScoped<DataverseWebApiService>();

        return services;
    }
}
```

**Key Changes**:
1. **Added using**: `using Spe.Bff.Api.Services;`
2. **Registered GraphTokenCache**: `services.AddSingleton<GraphTokenCache>();`
3. **Order**: GraphTokenCache registered BEFORE GraphClientFactory
4. **Lifetime**: Singleton (shared, stateless)

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Application Startup Check
```bash
# Start application
dotnet run --project src/api/Spe.Bff.Api

# Check logs for DI errors
# Expected: No "Unable to resolve service for type 'GraphTokenCache'" errors
# Expected: Application starts successfully
```

### Service Resolution Check
```bash
# Test endpoint to verify DI resolution
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test.txt" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: text/plain" \
  -d "test content"

# Check logs:
# Expected: Should see cache-related logs:
#   - "Cache MISS for token hash..." (first request)
#   - "Cached Graph token with 55-minute TTL"
#   - "Cache HIT for token hash..." (subsequent requests)
```

### Redis Verification
```bash
# Verify Redis cache keys after request
redis-cli KEYS "sdap:graph:token:*"
# Expected: Should see cached token keys

# Check TTL
redis-cli TTL "sdap:graph:token:{key}"
# Expected: ~3300 seconds (55 minutes)
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 4 Task 2 complete (cache integrated)
- [ ] **Pre-flight**: Verified DocumentsModule.Extensions.cs exists
- [ ] **Pre-flight**: Verified GraphClientFactory registered in module
- [ ] **Pre-flight**: Verified Redis configured
- [ ] Added using: `using Spe.Bff.Api.Services;`
- [ ] Registered GraphTokenCache as Singleton
- [ ] Registered BEFORE GraphClientFactory (dependency order)
- [ ] Used correct lifetime: `AddSingleton<GraphTokenCache>()`
- [ ] Build succeeds: `dotnet build`
- [ ] Application starts: `dotnet run`
- [ ] GraphClientFactory resolves successfully
- [ ] Cache logs appear in output

---

## Expected Results

**Before**:
- ❌ GraphTokenCache not registered
- ❌ "Unable to resolve GraphTokenCache" error at runtime

**After**:
- ✅ GraphTokenCache registered as Singleton
- ✅ GraphClientFactory resolves successfully
- ✅ Cache operational (logs show hits/misses)
- ✅ Redis keys created on requests

---

## Lifetime Decision: Why Singleton?

### Singleton is Correct
```csharp
services.AddSingleton<GraphTokenCache>();  // ✅ Correct
```

**Reasoning**:
- **Stateless**: GraphTokenCache has no per-request state
- **Shared cache**: All requests share same Redis connection
- **Dependencies**: Only injects IDistributedCache (Singleton) and ILogger (any)
- **Performance**: Creating once is efficient

### Scoped Would Be Wrong
```csharp
services.AddScoped<GraphTokenCache>();  // ❌ Wrong
```

**Problems**:
- **Wasteful**: Creates new instance per request (unnecessary)
- **No benefit**: Cache is stateless, no per-request context needed
- **Captive risk**: Could be captured by Singleton (GraphClientFactory)

---

## Anti-Pattern Verification

### ✅ Avoided: Captive Dependency
```bash
# Verify GraphTokenCache (Singleton) only injects Singletons
grep -A 10 "GraphTokenCache(" src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Expected: Should inject IDistributedCache (Singleton) and ILogger (any)
```

**Why**: Singleton can only safely inject Singleton or Transient services

### ✅ Dependency Order
```bash
# Verify GraphTokenCache registered before GraphClientFactory
grep -A 5 "AddSingleton<GraphTokenCache>" src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
# Expected: Should be followed by AddSingleton<IGraphClientFactory>
```

**Why**: GraphClientFactory depends on GraphTokenCache, must be registered first

---

## Troubleshooting

### Issue: "Unable to resolve service for type 'GraphTokenCache'"

**Cause**: GraphTokenCache not registered or wrong location

**Fix**: Verify registration exists:
```bash
grep "AddSingleton<GraphTokenCache>" src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
# Should find registration
```

### Issue: "Captive dependency" warning

**Cause**: GraphTokenCache (Singleton) injecting Scoped service

**Fix**: Verify GraphTokenCache only injects Singletons:
```csharp
public GraphTokenCache(
    IDistributedCache cache,  // ✅ Singleton
    ILogger<GraphTokenCache> logger)  // ✅ Any lifetime
```

### Issue: Cache not working (always cache miss)

**Cause**: Redis not configured or not connected

**Fix 1**: Verify Redis configuration:
```bash
grep "AddStackExchangeRedisCache" src/api/Spe.Bff.Api/Extensions/SpaarkeCore.Extensions.cs
```

**Fix 2**: Test Redis connection:
```bash
redis-cli ping
# Expected: PONG
```

**Fix 3**: Check connection string in appsettings.json:
```json
{
  "ConnectionStrings": {
    "Redis": "localhost:6379"
  }
}
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ GraphTokenCache registered as Singleton
- [ ] ✅ Registered in DocumentsModule.Extensions.cs
- [ ] ✅ Registered BEFORE GraphClientFactory
- [ ] ✅ Build succeeds
- [ ] ✅ Application starts successfully
- [ ] ✅ GraphClientFactory resolves successfully
- [ ] ✅ Cache logs appear (HIT/MISS messages)
- [ ] ✅ Task stayed focused (did NOT add metrics - that's Task 4.4)

**If any item unchecked**: Review and fix before proceeding to Task 4.4

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Extensions/DocumentsModule.Extensions.cs
git commit -m "feat(di): register GraphTokenCache as Singleton per ADR-010

- Register GraphTokenCache in DocumentsModule.Extensions.cs
- Use Singleton lifetime (shared cache, stateless)
- Register BEFORE GraphClientFactory (dependency order)
- Add using for Spe.Bff.Api.Services

Verification:
- Application starts successfully ✅
- GraphClientFactory resolves GraphTokenCache ✅
- Cache operational (logs show hits/misses) ✅
- Redis keys created on requests ✅

ADR Compliance: ADR-009 (Redis-First Caching), ADR-010 (DI Minimalism)
Anti-Patterns Avoided: Captive dependency, wrong lifetime
Task: Phase 4, Task 3"
```

---

## Next Task

➡️ [Phase 4 - Task 4: Add Cache Metrics (Optional)](phase-4-task-4-cache-metrics.md)

**What's next**: Create CacheMetrics.cs for monitoring cache hit/miss rates (optional enhancement)

---

## End-to-End Testing

Now that cache is fully integrated, test the complete flow:

```bash
# 1. Clear Redis cache
redis-cli FLUSHDB

# 2. Make first request (user A)
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test1.txt" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d "test"

# Check logs:
# ✅ "Cache MISS for token hash ab12cd34..."
# ✅ "Performing OBO token exchange"
# ✅ "Cached Graph token with 55-minute TTL"

# 3. Make second request (same user A)
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test2.txt" \
  -H "Authorization: Bearer $TOKEN_A" \
  -d "test"

# Check logs:
# ✅ "Cache HIT for token hash ab12cd34..."
# ✅ "Using cached Graph token"
# ✅ Much faster response (5ms vs 200ms)

# 4. Check Redis
redis-cli KEYS "sdap:graph:token:*"
# ✅ Should see 1 key (user A's token)

redis-cli TTL "sdap:graph:token:{key}"
# ✅ Should see ~3300 seconds (55 minutes)

# 5. Make request with different user (user B)
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test3.txt" \
  -H "Authorization: Bearer $TOKEN_B" \
  -d "test"

# Check logs:
# ✅ "Cache MISS for token hash ef56gh78..." (different hash)
# ✅ "Performing OBO token exchange"
# ✅ "Cached Graph token with 55-minute TTL"

redis-cli KEYS "sdap:graph:token:*"
# ✅ Should see 2 keys now (user A + user B)
```

**Expected Performance**:
- First request (cache miss): ~200ms
- Subsequent requests (cache hit): ~5-10ms
- Latency reduction: 95-97%

---

## Related Resources

- **Patterns**:
  - [di-feature-module.md](../patterns/di-feature-module.md)
  - [service-graph-token-cache.md](../patterns/service-graph-token-cache.md)
- **Anti-Patterns**:
  - [anti-pattern-captive-dependency.md](../patterns/anti-pattern-captive-dependency.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#token-caching)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-009, ADR-010
