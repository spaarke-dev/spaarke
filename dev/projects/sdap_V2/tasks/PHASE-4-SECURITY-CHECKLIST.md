# Phase 4: Pre-Flight Security Checklist

**Purpose**: Ensure secure token caching implementation
**Risk Level**: 🔴 **HIGH** - Involves caching security tokens
**Required**: MUST complete before starting Phase 4 tasks

---

## 🚨 CRITICAL: Security Issues in Current Code

### Issue #1: Full Token Logging (SECURITY VULNERABILITY)

**Location**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs:139`

**Current Code** (VULNERABLE):
```csharp
// TEMPORARY DEBUG - Log full Token B for analysis (REMOVE BEFORE PRODUCTION)
_logger.LogWarning("Token B (FULL JWT - REMOVE IN PRODUCTION): {TokenB}", result.AccessToken);
```

**Risk**:
- ❌ Exposes full JWT tokens in logs
- ❌ Violates security best practices
- ❌ Could leak sensitive user claims
- ❌ Will be exacerbated by token caching (more tokens in memory)

**Action Required**:
```bash
# 1. Locate the vulnerable line
grep -n "REMOVE IN PRODUCTION\|FULL JWT\|LogWarning.*Token" \
  src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

# 2. Remove the entire logging statement (line 139)
# 3. Verify no other full token logging exists
grep -rn "AccessToken.*Log\|Log.*AccessToken" src/api/Spe.Bff.Api/
```

**Verification**:
```bash
# After removal, this should return NOTHING:
grep -rn "result.AccessToken" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
# Expected: No matches (except in cache integration code)
```

---

## Pre-Flight Checklist

### Step 1: Security Audit (REQUIRED)

Run these checks BEFORE starting Phase 4:

```bash
# ❌ Check for full token logging (MUST be 0)
grep -rn "LogWarning.*Token\|LogInformation.*Token.*result\|AccessToken.*Log" \
  src/api/Spe.Bff.Api/Infrastructure/Graph/

# Expected: 0 matches (or only safe logging like "Token exchange successful")

# ❌ Check for token storage in unsafe locations
grep -rn "AccessToken.*=" src/api/Spe.Bff.Api/ | grep -v "result.AccessToken"

# ❌ Check for user token exposure
grep -rn "userAccessToken.*Log" src/api/Spe.Bff.Api/

# ✅ Verify SHA256 hashing is used for cache keys
# (Will be added in Phase 4, but check no plaintext token keys exist)
grep -rn "cache.*userToken\|SetString.*token" src/api/Spe.Bff.Api/
```

**All checks MUST pass (0 unsafe matches) before proceeding.**

---

### Step 2: Current State Verification

```bash
# 1. Verify GraphClientFactory exists and has OBO method
ls -la src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs
grep -n "CreateOnBehalfOfClientAsync" src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

# Expected: Method exists around line 99

# 2. Verify Redis is already configured
grep -A 10 "AddStackExchangeRedisCache" src/api/Spe.Bff.Api/Program.cs

# Expected: Redis configuration block exists (lines ~187-237)

# 3. Verify IDistributedCache is available
grep -n "IDistributedCache" src/api/Spe.Bff.Api/Program.cs

# Expected: Redis or MemoryCache registered

# 4. Check current OBO implementation
grep -A 20 "AcquireTokenOnBehalfOf" \
  src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs

# Expected: OBO flow around lines 127-133, NO token caching yet
```

---

### Step 3: Integration Point Identification

**OBO Flow Current Location**:
- **File**: `src/api/Spe.Bff.Api/Infrastructure/Graph/GraphClientFactory.cs`
- **Method**: `CreateOnBehalfOfClientAsync(string userAccessToken)`
- **Lines**: ~127-133 (OBO token exchange)
- **Integration Point**: BEFORE line 127 (check cache), AFTER line 133 (cache result)

**Current Code Structure** (lines 127-133):
```csharp
var result = await _cca.AcquireTokenOnBehalfOf(
    new[] {
        "https://graph.microsoft.com/Sites.FullControl.All",
        "https://graph.microsoft.com/Files.ReadWrite.All"
    },
    new UserAssertion(userAccessToken)
).ExecuteAsync();
```

**Will Become** (Phase 4 Task 2):
```csharp
// 1. Check cache
var tokenHash = _tokenCache.ComputeTokenHash(userAccessToken);
var cachedToken = await _tokenCache.GetTokenAsync(tokenHash);

if (cachedToken != null)
{
    _logger.LogDebug("Using cached Graph token");
    return CreateGraphClientFromToken(cachedToken); // Cache HIT
}

// 2. Cache MISS - perform OBO exchange
var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();

// 3. Cache the result
await _tokenCache.SetTokenAsync(tokenHash, result.AccessToken, TimeSpan.FromMinutes(55));

// 4. Return client
return CreateGraphClientFromToken(result.AccessToken);
```

---

## Security Requirements for Phase 4

### ✅ Token Handling Rules

1. **NEVER log full tokens**:
   ```csharp
   // ❌ WRONG
   _logger.LogInformation("Token: {Token}", result.AccessToken);

   // ✅ CORRECT
   _logger.LogInformation("Token exchange successful");
   ```

2. **NEVER use plaintext tokens as cache keys**:
   ```csharp
   // ❌ WRONG
   await _cache.SetStringAsync(userAccessToken, graphToken);

   // ✅ CORRECT
   var hash = ComputeTokenHash(userAccessToken); // SHA256
   await _cache.SetStringAsync($"sdap:graph:token:{hash}", graphToken);
   ```

3. **ONLY log hash prefixes (first 8 chars)**:
   ```csharp
   // ✅ CORRECT
   _logger.LogDebug("Cache HIT for hash {Hash}...", tokenHash[..8]);
   ```

4. **NEVER store user tokens in cache**:
   ```csharp
   // ❌ WRONG - caching user token
   await _cache.SetStringAsync("user:token", userAccessToken);

   // ✅ CORRECT - caching Graph token (result of OBO)
   await _cache.SetStringAsync(cacheKey, result.AccessToken);
   ```

5. **ALWAYS use SHA256 for token hashing**:
   ```csharp
   // ✅ CORRECT
   using var sha256 = SHA256.Create();
   var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userToken));
   return Convert.ToBase64String(hashBytes);
   ```

6. **ALWAYS handle cache errors gracefully**:
   ```csharp
   // ✅ CORRECT
   try
   {
       var cachedToken = await _cache.GetStringAsync(cacheKey);
       return cachedToken;
   }
   catch (Exception ex)
   {
       _logger.LogWarning(ex, "Cache retrieval failed, will perform OBO");
       return null; // Fall back to OBO exchange
   }
   ```

---

## TTL (Time-To-Live) Requirements

### Token Expiration Rules

**Azure AD Token Lifetime**: 60 minutes (default)

**Cache TTL**: 55 minutes (5-minute safety buffer)

**Why 55 minutes?**
- Prevents using expired tokens from cache
- 5-minute buffer accounts for clock skew
- Allows token refresh before expiration
- Reduces "token expired" errors

**Implementation**:
```csharp
// ✅ CORRECT
await _cache.SetStringAsync(
    cacheKey,
    graphToken,
    new DistributedCacheEntryOptions
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(55) // NOT 60!
    });
```

**Verification**:
```bash
# After Phase 4 complete, verify TTL in Redis:
redis-cli TTL "sdap:graph:token:ABC123..."
# Expected: ~3300 seconds (55 minutes)
```

---

## Graceful Degradation Requirements

### Redis Failure Scenarios

**Scenario 1: Redis Connection Lost**
```
MUST: Fall back to OBO exchange
MUST NOT: Throw exception
MUST: Log warning
MUST: Continue serving requests
```

**Implementation Pattern**:
```csharp
try
{
    var cached = await _cache.GetStringAsync(key);
    if (cached != null) return cached;
}
catch (Exception ex)
{
    _logger.LogWarning(ex, "Redis unavailable, performing OBO");
    // Fall through to OBO exchange
}

// Perform OBO exchange (works with or without cache)
var result = await _cca.AcquireTokenOnBehalfOf(...).ExecuteAsync();
```

**Scenario 2: Cache Corruption**
```
MUST: Detect invalid cached tokens
MUST: Fall back to OBO exchange
MUST: Clear corrupted cache entry
```

**Scenario 3: Cache Key Collision** (Unlikely with SHA256)
```
RISK: Very low (SHA256 collision probability: ~1 in 2^256)
MITIGATION: Include scope hash in cache key if using dynamic scopes
```

---

## Scope Handling Considerations

### Current Code Analysis

**Current Scopes** (GraphClientFactory.cs:128-131):
```csharp
new[] {
    "https://graph.microsoft.com/Sites.FullControl.All",
    "https://graph.microsoft.com/Files.ReadWrite.All"
}
```

**Risk**: If scopes change per request, cache key MUST include scope hash.

**Decision Options**:

**Option A: Fixed Scopes (RECOMMENDED)**
```
PRO: Simpler cache key (just user token hash)
PRO: Better cache hit rate
PRO: Easier to reason about
CON: Less flexible if different operations need different scopes
```

**Option B: Dynamic Scopes**
```
PRO: Supports different scopes per operation
CON: Cache key must include scope hash (complexity)
CON: Lower cache hit rate (different scopes = cache miss)
CON: More complex code
```

**Recommendation**: Use **Option A (Fixed Scopes)** unless dynamic scopes are required.

**Cache Key Pattern** (if Option A):
```csharp
var cacheKey = $"sdap:graph:token:{ComputeTokenHash(userToken)}";
// Assumes fixed scopes for all OBO exchanges
```

**Cache Key Pattern** (if Option B):
```csharp
var scopeHash = ComputeScopeHash(scopes); // Hash of sorted scopes
var cacheKey = $"sdap:graph:token:{ComputeTokenHash(userToken)}:{scopeHash}";
// Different scopes = different cache key
```

---

## Pre-Flight Checklist Summary

**Before Starting Phase 4, Verify**:

- [ ] ❌ **SECURITY**: Remove full token logging (line 139 in GraphClientFactory.cs)
- [ ] ✅ **SECURITY**: No other full token logging exists in codebase
- [ ] ✅ **SECURITY**: No plaintext token cache keys exist
- [ ] ✅ **STATE**: GraphClientFactory.CreateOnBehalfOfClientAsync exists
- [ ] ✅ **STATE**: OBO exchange happens at lines ~127-133
- [ ] ✅ **STATE**: Redis is configured in Program.cs
- [ ] ✅ **STATE**: IDistributedCache is available
- [ ] ✅ **INTEGRATION**: Identified exact integration point (BEFORE line 127)
- [ ] ✅ **REQUIREMENTS**: 55-minute TTL understood (not 60!)
- [ ] ✅ **REQUIREMENTS**: SHA256 hashing requirement understood
- [ ] ✅ **REQUIREMENTS**: Graceful degradation requirement understood
- [ ] ✅ **REQUIREMENTS**: Fixed vs dynamic scopes decision made

**If ANY item unchecked**: STOP and resolve before Phase 4.

---

## Post-Phase 4 Security Validation

**After Phase 4 Complete, Verify**:

```bash
# 1. No full token logging
grep -rn "LogWarning.*Token.*result\|AccessToken.*Log" src/api/Spe.Bff.Api/
# Expected: 0 matches

# 2. Only hash-based cache keys
grep -rn "sdap:graph:token:" src/api/Spe.Bff.Api/
# Expected: Only hashed keys (no plaintext tokens)

# 3. SHA256 hashing used
grep -rn "SHA256.Create\|ComputeTokenHash" src/api/Spe.Bff.Api/
# Expected: GraphTokenCache uses SHA256

# 4. 55-minute TTL used
grep -rn "FromMinutes(55)" src/api/Spe.Bff.Api/
# Expected: Cache TTL is 55 minutes (not 60)

# 5. Graceful error handling
grep -A 5 "catch.*Exception" src/api/Spe.Bff.Api/Services/GraphTokenCache.cs
# Expected: Catch blocks return null or log warnings (no throw)
```

---

## Critical Success Criteria

**Security**:
- ✅ No full tokens in logs
- ✅ SHA256 hashing for cache keys
- ✅ Only hash prefixes logged (8 chars)

**Reliability**:
- ✅ Redis failure → graceful degradation (no exceptions)
- ✅ Cache miss → OBO exchange (fallback works)
- ✅ Invalid cached token → detected and handled

**Performance**:
- ✅ Cache hit latency: <10ms
- ✅ Cache miss latency: ~200ms (baseline)
- ✅ Cache hit rate: >90% (target)

**Production Readiness**:
- ✅ No "REMOVE IN PRODUCTION" comments
- ✅ No debug token logging
- ✅ Observability (hit/miss metrics)
- ✅ Documentation (why 55min TTL, etc.)

---

## Related Resources

- **ADR-009**: [ARCHITECTURAL-DECISIONS.md](../ARCHITECTURAL-DECISIONS.md#adr-009-caching-policy--redis-first)
- **Pattern**: [service-graph-token-cache.md](../patterns/service-graph-token-cache.md)
- **Task 1**: [phase-4-task-1-create-cache.md](phase-4-task-1-create-cache.md)
- **Task 2**: [phase-4-task-2-integrate-cache.md](phase-4-task-2-integrate-cache.md)
- **Task 3**: [phase-4-task-3-register-cache.md](phase-4-task-3-register-cache.md)
- **Task 4**: [phase-4-task-4-cache-metrics.md](phase-4-task-4-cache-metrics.md)

---

**Last Updated**: 2025-10-14
**Status**: 🔴 **CRITICAL** - Must complete before Phase 4
