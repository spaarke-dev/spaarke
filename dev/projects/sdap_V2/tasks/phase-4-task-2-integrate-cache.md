# Phase 4 - Task 2: Integrate Cache into GraphClientFactory

**Phase**: 4 (Token Caching)
**Duration**: 1 hour
**Risk**: 🔴 **HIGH** (Critical integration - affects all OBO flows)
**Security**: 🚨 **MUST remove** vulnerable token logging (line 139) BEFORE integration
**Patterns**: [service-graph-client-factory.md](../patterns/service-graph-client-factory.md), [service-graph-token-cache.md](../patterns/service-graph-token-cache.md)
**Anti-Patterns**: Logging tokens, breaking OBO flow, skipping cache on errors

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 4 of the SDAP BFF API refactoring, specifically integrating GraphTokenCache into GraphClientFactory.

🚨 CRITICAL SECURITY REQUIREMENT:
BEFORE starting this task, you MUST:
1. Remove line 139 in GraphClientFactory.cs (logs full token - SECURITY VULNERABILITY)
2. Verify [PHASE-4-SECURITY-CHECKLIST.md](PHASE-4-SECURITY-CHECKLIST.md) complete
3. Ensure no other token logging exists

TASK: Update GraphClientFactory.CreateOnBehalfOfClientAsync() to check cache before performing OBO exchange, achieving 97% latency reduction on cache hits.

CRITICAL INTEGRATION POINTS IDENTIFIED:
- File: src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
- Method: CreateOnBehalfOfClientAsync (lines ~99-155)
- Current OBO call: Line 127-133 (AcquireTokenOnBehalfOf)
- Integration: BEFORE line 127 (check cache), AFTER line 133 (cache result)
- Security issue: Line 139 (logs full token) - MUST REMOVE FIRST

CONSTRAINTS:
- Must check cache BEFORE OBO exchange (cache-first pattern)
- Must fall back to OBO exchange on cache miss (graceful degradation)
- Must cache token after successful OBO exchange (55-min TTL, NOT 60!)
- Must preserve existing OBO logic (no behavior changes except caching)
- Must handle cache failures gracefully (fallback to OBO, no exceptions)
- Must remove line 139 token logging (SECURITY VULNERABILITY)
- Must use existing scopes (Sites.FullControl.All, Files.ReadWrite.All)
- Must inject GraphTokenCache in constructor (will be registered in Task 4.3)

VERIFICATION BEFORE STARTING:
1. ✅ Verify Phase 4 Task 1 complete (GraphTokenCache.cs created)
2. ✅ Verify GraphClientFactory.cs exists (confirmed: Infrastructure/Graph/)
3. ✅ Verify CreateOnBehalfOfClientAsync exists (confirmed: lines 99-155)
4. ❌ SECURITY: Remove line 139 token logging (REQUIRED BEFORE INTEGRATION)
5. ✅ Review PHASE-4-SECURITY-CHECKLIST.md (understand security requirements)
6. If any verification fails, STOP and resolve before proceeding

INTEGRATION FLOW (EXACT STEPS):
1. Remove line 139 (token logging - SECURITY)
2. Inject GraphTokenCache in constructor
3. Add field: private readonly GraphTokenCache _tokenCache;
4. In CreateOnBehalfOfClientAsync:
   a. Compute hash: var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
   b. Check cache: var cached = await _tokenCache.GetTokenAsync(tokenHash);
   c. If cached != null: return CreateGraphClientWithToken(cached);
   d. Else: perform OBO (lines 127-133)
   e. Cache result: await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));
   f. Return: CreateGraphClientWithToken(result.AccessToken);
5. Extract helper: CreateGraphClientWithToken(string accessToken)

FOCUS: Stay focused on integrating cache into GraphClientFactory only. Do NOT register cache in DI (that's Task 4.3).
```

---

## Goal

Integrate **GraphTokenCache** into **GraphClientFactory** to check cache before performing OBO exchange, reducing Azure AD load and latency.

**Cache-First Flow**:
1. Compute user token hash
2. Check cache for Graph token
3. **Cache HIT**: Return cached token (~5ms)
4. **Cache MISS**: Perform OBO exchange (~200ms) + cache result

**Performance Target**:
- Cache hit: <10ms (vs 200ms)
- Cache miss: ~200ms (same as before)
- Overall: 97% latency reduction (assuming 90% hit rate)

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

```bash
# 1. Verify Phase 4 Task 1 complete
- [ ] Check GraphTokenCache.cs exists
ls src/api/Spe.Bff.Api/Services/GraphTokenCache.cs

# 2. Verify GraphClientFactory.cs exists
- [ ] ls src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
# OR
- [ ] find src/api -name "GraphClientFactory.cs"

# 3. Find CreateOnBehalfOfClientAsync method
- [ ] grep "CreateOnBehalfOfClientAsync" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
# Expected: Should find method signature

# 4. Document current OBO latency (baseline)
- [ ] Measure current OBO exchange time: _____ ms
# Typical: 150-250ms per exchange
```

**If any verification fails**: STOP and complete Task 4.1 first.

---

## Files to Edit

```bash
- [ ] src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
```

---

## Implementation

### Before (OLD - No caching)

```csharp
using Microsoft.Graph;
using Microsoft.Identity.Client;

namespace Spe.Bff.Api.Infrastructure.Graph;

public class GraphClientFactory : IGraphClientFactory
{
    private readonly IConfidentialClientApplication _cca;
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly HttpMessageHandler _httpMessageHandler;

    public GraphClientFactory(
        IConfiguration configuration,
        ILogger<GraphClientFactory> logger,
        HttpMessageHandler httpMessageHandler)
    {
        var apiAppId = configuration["API_APP_ID"]!;
        var clientSecret = configuration["CLIENT_SECRET"]!;
        var tenantId = configuration["TENANT_ID"]!;

        _cca = ConfidentialClientApplicationBuilder
            .Create(apiAppId)
            .WithClientSecret(clientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();

        _logger = logger;
        _httpMessageHandler = httpMessageHandler;
    }

    // ❌ OLD: No caching, performs OBO on every call
    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        _logger.LogDebug("Performing OBO token exchange");

        // Perform OBO exchange (200ms latency)
        var result = await _cca.AcquireTokenOnBehalfOf(
            scopes: new[]
            {
                "https://graph.microsoft.com/Sites.FullControl.All",
                "https://graph.microsoft.com/Files.ReadWrite.All"
            },
            userAssertion: new UserAssertion(userAccessToken))
            .ExecuteAsync();

        // Create Graph client with acquired token
        var authProvider = new DelegateAuthenticationProvider((request) =>
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", result.AccessToken);
            return Task.CompletedTask;
        });

        return new GraphServiceClient(authProvider, _httpMessageHandler);
    }
}
```

### After (NEW - With caching)

```csharp
using Microsoft.Graph;
using Microsoft.Identity.Client;
using Spe.Bff.Api.Services;

namespace Spe.Bff.Api.Infrastructure.Graph;

public class GraphClientFactory : IGraphClientFactory
{
    private readonly IConfidentialClientApplication _cca;
    private readonly GraphTokenCache _tokenCache;  // ✨ NEW
    private readonly ILogger<GraphClientFactory> _logger;
    private readonly HttpMessageHandler _httpMessageHandler;

    public GraphClientFactory(
        IConfiguration configuration,
        GraphTokenCache tokenCache,  // ✨ NEW: Inject cache
        ILogger<GraphClientFactory> logger,
        HttpMessageHandler httpMessageHandler)
    {
        var apiAppId = configuration["API_APP_ID"]!;
        var clientSecret = configuration["CLIENT_SECRET"]!;
        var tenantId = configuration["TENANT_ID"]!;

        _cca = ConfidentialClientApplicationBuilder
            .Create(apiAppId)
            .WithClientSecret(clientSecret)
            .WithAuthority(AzureCloudInstance.AzurePublic, tenantId)
            .Build();

        _tokenCache = tokenCache;  // ✨ NEW
        _logger = logger;
        _httpMessageHandler = httpMessageHandler;
    }

    // ✅ NEW: Cache-first approach
    public async Task<GraphServiceClient> CreateOnBehalfOfClientAsync(string userAccessToken)
    {
        // ============================================================================
        // Step 1: Compute token hash for cache key
        // ============================================================================
        var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);

        // ============================================================================
        // Step 2: Check cache first (cache-first pattern)
        // ============================================================================
        var cachedGraphToken = await _tokenCache.GetTokenAsync(tokenHash);

        if (cachedGraphToken != null)
        {
            // Cache HIT: Use cached token (~5ms total)
            _logger.LogDebug("Using cached Graph token (cache hit)");
            return CreateGraphClientWithToken(cachedGraphToken);
        }

        // ============================================================================
        // Step 3: Cache MISS - Perform OBO exchange
        // ============================================================================
        _logger.LogDebug("Cache miss, performing OBO token exchange");

        var result = await _cca.AcquireTokenOnBehalfOf(
            scopes: new[]
            {
                "https://graph.microsoft.com/Sites.FullControl.All",
                "https://graph.microsoft.com/Files.ReadWrite.All"
            },
            userAssertion: new UserAssertion(userAccessToken))
            .ExecuteAsync();

        // ============================================================================
        // Step 4: Cache the acquired token (55-minute TTL)
        // ============================================================================
        await _tokenCache.SetTokenAsync(
            tokenHash,
            result.AccessToken,
            TimeSpan.FromMinutes(55)); // 5-minute buffer before 60-min expiration

        _logger.LogDebug("Cached Graph token with 55-minute TTL");

        // Return Graph client with newly acquired token
        return CreateGraphClientWithToken(result.AccessToken);
    }

    /// <summary>
    /// Create GraphServiceClient with a given access token.
    /// Extracted to avoid duplication (used for both cached and fresh tokens).
    /// </summary>
    private GraphServiceClient CreateGraphClientWithToken(string accessToken)
    {
        var authProvider = new DelegateAuthenticationProvider((request) =>
        {
            request.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", accessToken);
            return Task.CompletedTask;
        });

        return new GraphServiceClient(authProvider, _httpMessageHandler);
    }
}
```

**Key Changes**:
1. **Inject GraphTokenCache** in constructor
2. **Compute token hash** before cache check
3. **Check cache first** (cache-first pattern)
4. **Return cached token** on hit (~5ms)
5. **Perform OBO** on miss (~200ms)
6. **Cache result** after OBO (55-min TTL)
7. **Extract helper method** CreateGraphClientWithToken (DRY principle)

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Code Review Checklist
```bash
# Verify GraphTokenCache injected
- [ ] grep "GraphTokenCache" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
# Expected: Should find field and constructor parameter

# Verify cache-first pattern (check before OBO)
- [ ] grep -A 20 "CreateOnBehalfOfClientAsync" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
# Expected: Should see GetTokenAsync() BEFORE AcquireTokenOnBehalfOf()

# Verify caching after OBO
- [ ] grep "SetTokenAsync" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
# Expected: Should find SetTokenAsync() after AcquireTokenOnBehalfOf()

# Verify 55-minute TTL
- [ ] grep "FromMinutes(55)" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
# Expected: Should find TimeSpan.FromMinutes(55)
```

### Manual Testing (after Task 4.3 - DI registration)

```bash
# Test sequence after DI is configured:

# 1. Clear Redis cache
redis-cli FLUSHDB

# 2. Make API request (user A)
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test1.txt" \
  -H "Authorization: Bearer $TOKEN_USER_A" \
  -H "Content-Type: text/plain" \
  -d "test content"

# Check logs:
# Expected: "Cache miss, performing OBO token exchange"
# Expected: "Cached Graph token with 55-minute TTL"

# 3. Make second request (same user A)
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test2.txt" \
  -H "Authorization: Bearer $TOKEN_USER_A" \
  -H "Content-Type: text/plain" \
  -d "test content 2"

# Check logs:
# Expected: "Using cached Graph token (cache hit)"

# 4. Make request with different user (user B)
curl -X POST "https://localhost:5001/api/obo/upload?containerId=xxx&fileName=test3.txt" \
  -H "Authorization: Bearer $TOKEN_USER_B" \
  -H "Content-Type: text/plain" \
  -d "test content 3"

# Check logs:
# Expected: "Cache miss, performing OBO token exchange" (different user = different hash)
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 4 Task 1 complete (GraphTokenCache exists)
- [ ] **Pre-flight**: Verified GraphClientFactory.cs exists
- [ ] **Pre-flight**: Verified CreateOnBehalfOfClientAsync method exists
- [ ] Injected `GraphTokenCache` in constructor
- [ ] Added field: `private readonly GraphTokenCache _tokenCache;`
- [ ] Compute token hash: `_tokenCache.ComputeTokenHash(userAccessToken)`
- [ ] Check cache BEFORE OBO: `await _tokenCache.GetTokenAsync(tokenHash)`
- [ ] Return cached token on hit: `CreateGraphClientWithToken(cachedGraphToken)`
- [ ] Perform OBO on cache miss (preserve existing logic)
- [ ] Cache token after OBO: `await _tokenCache.SetTokenAsync(...)`
- [ ] Use 55-minute TTL: `TimeSpan.FromMinutes(55)`
- [ ] Extracted helper method: `CreateGraphClientWithToken()`
- [ ] Added logging for cache hits/misses
- [ ] Build succeeds: `dotnet build`

---

## Expected Results

**Before**:
- ❌ Every call performs OBO exchange (~200ms)
- ❌ High Azure AD load
- ❌ No caching

**After**:
- ✅ Cache hit: ~5ms (95% faster)
- ✅ Cache miss: ~200ms (same as before)
- ✅ Overall: 97% latency reduction (with 90% hit rate)
- ✅ Reduced Azure AD load by 90%

**Flow Diagram**:
```
Request → GraphClientFactory.CreateOnBehalfOfClientAsync()
  ├─→ Compute token hash (SHA256)
  ├─→ Check cache (GetTokenAsync)
  │   ├─→ Cache HIT → Return client with cached token (~5ms) ✅
  │   └─→ Cache MISS → Perform OBO exchange (~200ms)
  │       └─→ Cache result (SetTokenAsync)
  │           └─→ Return client with fresh token
```

---

## Performance Metrics

### Target Metrics (after Task 4.3)
| Metric | Before | After | Improvement |
|--------|--------|-------|-------------|
| Cache hit latency | N/A | <10ms | New capability |
| Cache miss latency | 200ms | 200ms | Same |
| Overall avg latency | 200ms | ~25ms* | 87% reduction |
| Azure AD load | 100% | 10%* | 90% reduction |

*Assuming 90% cache hit rate after warmup

### Calculation
```
Overall latency = (hit_rate × hit_latency) + (miss_rate × miss_latency)
                = (0.90 × 5ms) + (0.10 × 200ms)
                = 4.5ms + 20ms
                = 24.5ms (~25ms)

Improvement = (200ms - 25ms) / 200ms = 87% reduction
```

---

## Troubleshooting

### Issue: Compile error "GraphTokenCache not found"

**Cause**: GraphTokenCache not created (Task 4.1 not complete)

**Fix**: Complete Task 4.1 first:
```bash
ls src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Should exist
```

### Issue: "Unable to resolve GraphTokenCache" at runtime

**Cause**: GraphTokenCache not registered in DI (Task 4.3 not complete)

**Expected**: This is OK for now, DI registration happens in Task 4.3

### Issue: Cache always misses

**Cause 1**: Redis not connected

**Fix**: Verify Redis connection:
```bash
redis-cli ping
# Expected: PONG
```

**Cause 2**: Token hash changes on every request

**Fix**: Verify user token is consistent:
```bash
# User token should be the same for same user/session
# Different sessions = different tokens = different hashes (expected)
```

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ GraphTokenCache injected in constructor
- [ ] ✅ Cache checked BEFORE OBO exchange (cache-first)
- [ ] ✅ Cache hit returns cached token
- [ ] ✅ Cache miss performs OBO and caches result
- [ ] ✅ 55-minute TTL used
- [ ] ✅ Helper method extracted (CreateGraphClientWithToken)
- [ ] ✅ Build succeeds
- [ ] ✅ Task stayed focused (did NOT register in DI - that's Task 4.3)

**If any item unchecked**: Review and fix before proceeding to Task 4.3

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
git commit -m "feat(cache): integrate GraphTokenCache into GraphClientFactory per ADR-009

- Inject GraphTokenCache in GraphClientFactory constructor
- Implement cache-first pattern: check cache before OBO exchange
- Cache tokens after OBO with 55-minute TTL
- Extract CreateGraphClientWithToken() helper method (DRY)
- Add logging for cache hits/misses

Performance Impact:
- Cache hit: ~5ms (vs 200ms OBO)
- Cache miss: ~200ms (same as before)
- Overall: 87% latency reduction (with 90% hit rate)
- Azure AD load reduced by 90%

Flow:
1. Compute token hash (SHA256)
2. Check cache (GetTokenAsync)
3. Cache hit → Return cached token
4. Cache miss → Perform OBO + cache result

ADR Compliance: ADR-009 (Redis-First Caching)
Task: Phase 4, Task 2"
```

---

## Next Task

➡️ [Phase 4 - Task 3: Register Cache in DI](phase-4-task-3-register-cache.md)

**What's next**: Register GraphTokenCache as Singleton in DocumentsModule.Extensions.cs

---

## Related Resources

- **Patterns**:
  - [service-graph-client-factory.md](../patterns/service-graph-client-factory.md)
  - [service-graph-token-cache.md](../patterns/service-graph-token-cache.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#token-caching)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-009
- **Phase 4 Overview**: [REFACTORING-CHECKLIST.md](../REFACTORING-CHECKLIST.md#phase-4-token-caching)
