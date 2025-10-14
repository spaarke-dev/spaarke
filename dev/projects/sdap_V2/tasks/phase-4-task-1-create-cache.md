# Phase 4 - Task 1: Create GraphTokenCache

**Phase**: 4 (Token Caching)
**Duration**: 1 hour
**Risk**: 🔴 **HIGH** (Security-sensitive: token caching)
**Security**: 🚨 **CRITICAL** - Must complete [PHASE-4-SECURITY-CHECKLIST.md](PHASE-4-SECURITY-CHECKLIST.md) first
**Patterns**: [service-graph-token-cache.md](../patterns/service-graph-token-cache.md)
**Anti-Patterns**: Token exposure in logs, plaintext cache keys, unsafe TTL

---

## 🤖 AI PROMPT

```
CONTEXT: You are working on Phase 4 of the SDAP BFF API refactoring, specifically implementing Redis-based OBO token caching.

🚨 CRITICAL SECURITY REQUIREMENT:
BEFORE starting this task, you MUST review and complete:
→ PHASE-4-SECURITY-CHECKLIST.md (MANDATORY SECURITY REVIEW)

This checklist identifies a SECURITY VULNERABILITY in the current code that MUST be fixed before Phase 4.

TASK: Create GraphTokenCache.cs service with Redis backend to cache Graph API OBO tokens, reducing Azure AD load by 97%.

CONSTRAINTS:
- Must use IDistributedCache (Redis) - already configured in Program.cs
- Must compute SHA256 hash of user tokens for cache keys (SECURITY)
- Must use 55-minute TTL (5-minute buffer before 60-minute expiration) - NOT 60!
- Must handle errors gracefully (fallback to non-cached OBO) - NO throws in Get/Set
- Must NOT expose user tokens in logs (only hash first 8 chars) - SECURITY CRITICAL
- Must NOT store user tokens in cache (only Graph tokens) - SECURITY CRITICAL

SECURITY CHECKLIST (MUST COMPLETE BEFORE STARTING):
1. ❌ Remove full token logging from GraphClientFactory.cs:139 (SECURITY VULNERABILITY)
2. ✅ Verify no other full token logging exists in codebase
3. ✅ Verify Redis is already configured in Program.cs (lines ~187-237)
4. ✅ Review PHASE-4-SECURITY-CHECKLIST.md for complete security requirements

VERIFICATION BEFORE STARTING:
1. Verify Phase 3 complete (feature modules, Program.cs simplified)
2. ✅ SECURITY: Verified no full token logging exists (see security checklist)
3. Verify Redis already configured in Program.cs (should exist)
4. Verify IDistributedCache available (Redis or MemoryCache)
5. If any verification fails, STOP and complete security checklist first

FOCUS: Stay focused on creating GraphTokenCache only. Do NOT integrate it yet (that's Task 4.2).

CRITICAL CONTEXT:
- Current OBO implementation: GraphClientFactory.cs:127-133
- Integration point (Task 4.2): BEFORE line 127 (check cache), AFTER line 133 (cache result)
- Redis already configured: Program.cs:187-237 (reuse existing IDistributedCache)
- Security pattern: SHA256(userToken) → cache key, store Graph token (NOT user token)
```

---

## 🚨 SECURITY REQUIREMENT: Pre-Flight Check

**BEFORE starting this task**, complete the security checklist:

📋 **Read**: [PHASE-4-SECURITY-CHECKLIST.md](PHASE-4-SECURITY-CHECKLIST.md)

**Critical Issue Identified**:
```
SECURITY VULNERABILITY in GraphClientFactory.cs:139
→ Logs full JWT tokens (MUST be removed before Phase 4)
```

**Verify Security** (run these commands):
```bash
# 1. Check for full token logging (MUST be 0 results)
grep -n "REMOVE IN PRODUCTION\|FULL JWT\|LogWarning.*Token" \
  src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

# Expected: 0 matches (if line 139 was removed)
# If found: STOP and remove vulnerable logging first

# 2. Verify no other token exposure
grep -rn "AccessToken.*Log\|Log.*result.AccessToken" src/api/Spe.Bff.Api/

# Expected: 0 matches (except for cache code after Task 4.2)
```

**If any security check fails**: STOP and complete [PHASE-4-SECURITY-CHECKLIST.md](PHASE-4-SECURITY-CHECKLIST.md) first.

---

## Goal

Create **GraphTokenCache** service to cache Graph API OBO tokens in Redis, reducing Azure AD OBO exchange latency from ~200ms to ~5ms (97% reduction).

**Performance Target**:
- Cache hit latency: <10ms
- Cache miss latency: ~200ms (same as before)
- Cache hit rate: >90% (after warmup)

**Why**: OBO token exchange is expensive (~200ms), caching reduces latency and Azure AD load

---

## Pre-Flight Verification

### Step 0: Verify Context and Prerequisites

**CRITICAL**: Complete security checklist FIRST (see above)

```bash
# 0. 🚨 SECURITY: Remove vulnerable token logging (REQUIRED)
grep -n "REMOVE IN PRODUCTION\|FULL JWT" \
  src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

# If found (line 139): Remove entire LogWarning statement
# This MUST be 0 matches before proceeding

# 1. Verify Phase 3 complete
- [x] Feature modules created (Task 3.1) ✅ VERIFIED in Phase 3 review
- [x] Program.cs simplified (Task 3.2) ✅ VERIFIED in Phase 3 review
- [x] All tests pass (121 tests execute) ✅ VERIFIED in Phase 2

# 2. Verify Redis ALREADY CONFIGURED (discovered in codebase review)
grep -n "AddStackExchangeRedisCache" src/api/Spe.Bff.Api/Program.cs

# Expected: Line ~187-237 - Redis configuration block exists
# NOTE: Redis is ALREADY configured! You will reuse existing IDistributedCache

# 3. Verify current Redis registration
grep -A 15 "AddStackExchangeRedisCache" src/api/Spe.Bff.Api/Program.cs

# Expected output (Program.cs:187-237):
# if (!string.IsNullOrWhiteSpace(redisConnectionString))
# {
#     builder.Services.AddStackExchangeRedisCache(options =>
#     {
#         options.Configuration = redisConnectionString;
#         options.InstanceName = redisOptions?.InstanceName ?? "sdap:";
#     });
# }
# else
# {
#     builder.Services.AddDistributedMemoryCache(); // Fallback for dev
# }

# 4. Verify IDistributedCache is available
grep -n "IDistributedCache" src/api/Spe.Bff.Api/Program.cs

# Expected: Redis health check uses IDistributedCache (line ~305)

# 5. Identify OBO integration point
grep -n "AcquireTokenOnBehalfOf" \
  src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

# Expected: Line ~127-133 - This is where cache integration will happen (Task 4.2)
```

**IMPORTANT DISCOVERIES**:
- ✅ Redis is ALREADY configured in Program.cs (lines 187-237)
- ✅ IDistributedCache is ALREADY available (Redis or MemoryCache fallback)
- ✅ Health check already uses IDistributedCache (line 305)
- ✅ OBO integration point identified (GraphClientFactory.cs:127-133)
- ❌ Security vulnerability exists (line 139) - MUST remove before Phase 4

**Action Required**: You will reuse existing IDistributedCache (no new Redis setup needed)

---

## Files to Create

```bash
- [ ] src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
```

---

## Implementation

### Step 1: Create GraphTokenCache Service

**File**: `src/api/Spe.Bff.Api/Services/GraphTokenCache.cs`

**Pattern**: [service-graph-token-cache.md](../patterns/service-graph-token-cache.md)

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;

namespace Spe.Bff.Api.Services;

/// <summary>
/// Caches Graph API OBO tokens in Redis to reduce Azure AD load (ADR-009).
/// Target: 95% cache hit rate, 97% latency reduction (200ms → 5ms).
/// Cache key: SHA256 hash of user token (security + consistent length).
/// TTL: 55 minutes (5-minute buffer before 60-minute token expiration).
/// </summary>
public class GraphTokenCache
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<GraphTokenCache> _logger;

    // Cache key prefix for namespacing
    private const string CacheKeyPrefix = "sdap:graph:token:";

    // Default TTL: 55 minutes (5-minute buffer before token expires)
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(55);

    public GraphTokenCache(
        IDistributedCache cache,
        ILogger<GraphTokenCache> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Compute SHA256 hash of user token for cache key.
    /// Ensures consistent key length and prevents token exposure in logs/metrics.
    /// </summary>
    /// <param name="userToken">User access token (JWT)</param>
    /// <returns>Base64-encoded SHA256 hash</returns>
    public string ComputeTokenHash(string userToken)
    {
        if (string.IsNullOrEmpty(userToken))
            throw new ArgumentException("User token cannot be null or empty", nameof(userToken));

        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Get cached Graph token by user token hash.
    /// Returns null on cache miss or error (graceful degradation).
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token</param>
    /// <returns>Cached Graph token or null</returns>
    public async Task<string?> GetTokenAsync(string tokenHash)
    {
        if (string.IsNullOrEmpty(tokenHash))
            throw new ArgumentException("Token hash cannot be null or empty", nameof(tokenHash));

        var cacheKey = CacheKeyPrefix + tokenHash;

        try
        {
            var cachedToken = await _cache.GetStringAsync(cacheKey);

            if (cachedToken != null)
            {
                // Log cache hit (only first 8 chars of hash for security)
                _logger.LogDebug(
                    "Cache HIT for token hash {HashPrefix}... Key: {CacheKey}",
                    tokenHash[..Math.Min(8, tokenHash.Length)],
                    cacheKey);
            }
            else
            {
                // Log cache miss
                _logger.LogDebug(
                    "Cache MISS for token hash {HashPrefix}... Key: {CacheKey}",
                    tokenHash[..Math.Min(8, tokenHash.Length)],
                    cacheKey);
            }

            return cachedToken;
        }
        catch (Exception ex)
        {
            // Graceful degradation: log error but don't throw
            // Caller will fall back to OBO exchange
            _logger.LogError(
                ex,
                "Error retrieving token from cache for hash {HashPrefix}... Continuing without cache.",
                tokenHash[..Math.Min(8, tokenHash.Length)]);

            return null; // Treat as cache miss
        }
    }

    /// <summary>
    /// Cache Graph token with TTL.
    /// Errors are logged but not thrown (graceful degradation).
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token</param>
    /// <param name="graphToken">Graph API access token to cache</param>
    /// <param name="ttl">Time-to-live (defaults to 55 minutes)</param>
    public async Task SetTokenAsync(
        string tokenHash,
        string graphToken,
        TimeSpan? ttl = null)
    {
        if (string.IsNullOrEmpty(tokenHash))
            throw new ArgumentException("Token hash cannot be null or empty", nameof(tokenHash));

        if (string.IsNullOrEmpty(graphToken))
            throw new ArgumentException("Graph token cannot be null or empty", nameof(graphToken));

        var cacheKey = CacheKeyPrefix + tokenHash;
        var expiry = ttl ?? DefaultTtl;

        try
        {
            await _cache.SetStringAsync(
                cacheKey,
                graphToken,
                new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiry
                });

            _logger.LogDebug(
                "Cached token for hash {HashPrefix}... TTL: {TTL} minutes. Key: {CacheKey}",
                tokenHash[..Math.Min(8, tokenHash.Length)],
                expiry.TotalMinutes,
                cacheKey);
        }
        catch (Exception ex)
        {
            // Graceful degradation: log error but don't throw
            // Caching is optimization, not requirement
            _logger.LogError(
                ex,
                "Error caching token for hash {HashPrefix}... Continuing without cache.",
                tokenHash[..Math.Min(8, tokenHash.Length)]);

            // Don't throw - caching failure shouldn't break app
        }
    }

    /// <summary>
    /// Remove token from cache (e.g., on logout or token revocation).
    /// </summary>
    /// <param name="tokenHash">SHA256 hash of user token</param>
    public async Task RemoveTokenAsync(string tokenHash)
    {
        if (string.IsNullOrEmpty(tokenHash))
            throw new ArgumentException("Token hash cannot be null or empty", nameof(tokenHash));

        var cacheKey = CacheKeyPrefix + tokenHash;

        try
        {
            await _cache.RemoveAsync(cacheKey);

            _logger.LogDebug(
                "Removed cached token for hash {HashPrefix}... Key: {CacheKey}",
                tokenHash[..Math.Min(8, tokenHash.Length)],
                cacheKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error removing token from cache for hash {HashPrefix}...",
                tokenHash[..Math.Min(8, tokenHash.Length)]);

            // Don't throw - removal failure shouldn't break app
        }
    }

    /// <summary>
    /// Get cache statistics (for monitoring/debugging).
    /// Note: Redis doesn't provide built-in stats, this is a placeholder.
    /// </summary>
    public async Task<CacheStats> GetStatsAsync()
    {
        // Placeholder - would need custom implementation with Redis commands
        // or use IConnectionMultiplexer directly
        return new CacheStats
        {
            IsConnected = true, // Assume connected (would need health check)
            Message = "Redis stats not implemented (use Redis INFO command)"
        };
    }
}

/// <summary>
/// Cache statistics (placeholder for monitoring).
/// </summary>
public record CacheStats
{
    public bool IsConnected { get; init; }
    public string? Message { get; init; }
}
```

---

## Validation

### Build Check
```bash
dotnet build
# Expected: Success, 0 warnings
```

### Code Review Checklist
```bash
# Verify GraphTokenCache uses IDistributedCache
- [ ] grep "IDistributedCache" src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Expected: Should find usage

# Verify SHA256 hashing
- [ ] grep "SHA256" src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Expected: Should find ComputeTokenHash method

# Verify 55-minute TTL
- [ ] grep "55" src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Expected: Should find DefaultTtl = TimeSpan.FromMinutes(55)

# Verify graceful error handling (no throws in Get/Set)
- [ ] grep -A 10 "catch.*Exception" src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Expected: Should see return null or continue (no throw)

# Verify no token exposure in logs (only hash prefix)
- [ ] grep "tokenHash\[" src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Expected: Should see tokenHash[..8] or similar (only log prefix)
```

### Unit Test (Optional - can be done later)
```bash
# Create test file (optional)
# tests/Spe.Bff.Api.Tests/Services/GraphTokenCacheTests.cs
```

---

## Checklist

- [ ] **Pre-flight**: Verified Phase 3 complete
- [ ] **Pre-flight**: Verified Redis configuration exists
- [ ] **Pre-flight**: Verified Redis connection string configured
- [ ] Created `GraphTokenCache.cs` in Services folder
- [ ] Implemented `ComputeTokenHash()` with SHA256
- [ ] Implemented `GetTokenAsync()` with graceful error handling
- [ ] Implemented `SetTokenAsync()` with 55-minute default TTL
- [ ] Implemented `RemoveTokenAsync()` for logout scenarios
- [ ] Added XML documentation comments
- [ ] Used cache key prefix: `sdap:graph:token:`
- [ ] Log only first 8 chars of hash (security)
- [ ] Graceful degradation (no throws in Get/Set)
- [ ] Build succeeds: `dotnet build`

---

## Expected Results

**File Created**:
- ✅ `GraphTokenCache.cs` (~200 lines)

**Capabilities**:
- ✅ Hash user token (SHA256)
- ✅ Get cached Graph token (or null on miss)
- ✅ Set cached Graph token (55-min TTL)
- ✅ Remove cached token
- ✅ Graceful error handling

**Security**:
- ✅ User tokens hashed (not stored in cache keys)
- ✅ Only hash prefix logged (first 8 chars)
- ✅ No token exposure in logs or metrics

---

## Cache Key Design

### Key Pattern
```
sdap:graph:token:{SHA256_HASH}
```

**Example**:
```
User token: eyJ0eXAiOiJKV1QiLCJhbGc...
SHA256 hash: a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6...
Cache key: sdap:graph:token:a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6...
```

### Why SHA256?
1. **Consistent length**: All keys same length (base64 encoded hash)
2. **Security**: User token never exposed in cache keys
3. **Uniqueness**: Different users = different hashes
4. **Deterministic**: Same token = same hash = same cache key

### TTL Strategy
- **Token lifetime**: 60 minutes (Azure AD default)
- **Cache TTL**: 55 minutes (5-minute buffer)
- **Why buffer**: Prevents using expired token cached at 59:59

---

## Troubleshooting

### Issue: Redis configuration not found

**Cause**: Missing Redis section in appsettings.json

**Fix**: Add Redis configuration:
```json
{
  "Redis": {
    "Enabled": true,
    "InstanceName": "sdap-dev:",
    "DefaultExpirationMinutes": 60
  },
  "ConnectionStrings": {
    "Redis": "localhost:6379"
    // OR KeyVault reference:
    // "Redis": "@Microsoft.KeyVault(SecretUri=https://...Redis-ConnectionString)"
  }
}
```

### Issue: "SHA256 not found" compile error

**Cause**: Missing using statement

**Fix**: Add using:
```csharp
using System.Security.Cryptography;
```

### Issue: IDistributedCache not registered

**Cause**: Redis not added to DI (will be fixed in Task 4.3)

**Expected**: This is OK for now, DI registration happens in Task 4.3

---

## Context Verification

Before marking complete, verify:
- [ ] ✅ GraphTokenCache.cs created
- [ ] ✅ SHA256 hashing implemented
- [ ] ✅ Get/Set/Remove methods implemented
- [ ] ✅ 55-minute TTL used
- [ ] ✅ Graceful error handling (no throws)
- [ ] ✅ Security: only hash prefix logged
- [ ] ✅ Build succeeds
- [ ] ✅ Task stayed focused (did NOT integrate cache - that's Task 4.2)

**If any item unchecked**: Review and fix before proceeding to Task 4.2

---

## Commit Message

```bash
git add src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
git commit -m "feat(cache): create GraphTokenCache for OBO token caching per ADR-009

- Create GraphTokenCache.cs with Redis backend
- Implement SHA256 hashing for cache keys (security)
- Use 55-minute TTL (5-minute buffer before token expiration)
- Graceful error handling (fallback to OBO on cache failure)
- Log only hash prefix (first 8 chars) for security
- Cache key pattern: sdap:graph:token:{SHA256_HASH}

Performance Target:
- Cache hit latency: <10ms (vs 200ms OBO)
- Cache hit rate: >90% (after warmup)
- Latency reduction: 97% on cache hits

ADR Compliance: ADR-009 (Redis-First Caching)
Task: Phase 4, Task 1"
```

---

## Next Task

➡️ [Phase 4 - Task 2: Integrate Cache into GraphClientFactory](phase-4-task-2-integrate-cache.md)

**What's next**: Integrate GraphTokenCache into GraphClientFactory to check cache before OBO exchange

---

## Related Resources

- **Patterns**:
  - [service-graph-token-cache.md](../patterns/service-graph-token-cache.md)
- **Architecture**: [TARGET-ARCHITECTURE.md](../TARGET-ARCHITECTURE.md#token-caching)
- **ADR**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md) - ADR-009
- **Phase 4 Overview**: [REFACTORING-CHECKLIST.md](../REFACTORING-CHECKLIST.md#phase-4-token-caching)
