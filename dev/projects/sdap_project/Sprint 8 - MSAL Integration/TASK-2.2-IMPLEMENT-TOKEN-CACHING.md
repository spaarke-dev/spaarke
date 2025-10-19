# Task 2.2: Implement Token Caching in sessionStorage

**Sprint:** 8 - MSAL Integration
**Phase:** 2 - Token Acquisition
**Task:** 2.2 of 3 (Phase 2)
**Duration:** 45 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Add sessionStorage caching layer to MsalAuthProvider to avoid redundant token acquisitions and enable token expiration checking.**

**Success Criteria:**
- ✅ sessionStorage caching implemented in `getToken()` method
- ✅ Cache key based on scopes (support multiple tokens for different APIs)
- ✅ Token expiration checking before reusing cached token
- ✅ Expired tokens removed from cache and reacquired
- ✅ Cache management methods: get, set, clear
- ✅ TypeScript compiles without errors

---

## Context

### Why This Task Matters

Without caching, every API call would trigger MSAL token acquisition:
- ❌ **Performance issue** - 100-500ms overhead per call
- ❌ **User experience** - Unnecessary delays
- ❌ **Azure AD throttling** - Too many token requests

With caching:
- ✅ **Fast** - < 1ms to retrieve cached token
- ✅ **Efficient** - Only acquire when needed (expired/missing)
- ✅ **Scalable** - No Azure AD throttling concerns

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ Client-side caching (sessionStorage)
- ✅ No Dataverse plugins involved

**ADR-007 (Storage Seam Minimalism):**
- ✅ Simple cache implementation (no over-abstraction)
- ✅ Matches MSAL's internal caching pattern

### Sprint 4 Integration

Cached tokens will be used in Authorization headers for Spe.Bff.Api OBO endpoints:

**Flow:**
```
fileService.ts → getToken()
  ↓ Check cache (Task 2.2)
  ↓ If expired/missing → ssoSilent() (Task 2.1)
  ↓ Cache token
  ↓ Return token
Authorization: Bearer <cached-token>
  ↓
Spe.Bff.Api OBO endpoints
```

---

## Prerequisites

**Before starting:**
- ✅ Task 2.1 completed (`getToken()` with ssoSilent implemented)
- ✅ `TokenCacheEntry` interface defined in `types/auth.ts` (Task 1.2)

**Expected state:**
- `getToken()` acquires tokens but doesn't cache them
- Every call to `getToken()` triggers MSAL (slow)

---

## Step-by-Step Instructions

### Step 1: Review TokenCacheEntry Interface

**File:** `types/auth.ts`

**Verify interface exists (created in Task 1.2):**
```typescript
export interface TokenCacheEntry {
  /** Access token string */
  token: string;

  /** Expiration timestamp (Unix epoch milliseconds) */
  expiresAt: number;

  /** OAuth scopes for this token */
  scopes: string[];
}
```

**If missing, add to `types/auth.ts`.**

---

### Step 2: Add Cache Constants to MsalAuthProvider

**File:** `services/auth/MsalAuthProvider.ts`

**Add constants after class declaration (before constructor):**

```typescript
export class MsalAuthProvider implements IAuthProvider {
  // ============================================================================
  // Cache Configuration
  // ============================================================================

  /**
   * sessionStorage key prefix for token cache
   *
   * Format: msal.token.<scopes-hash>
   * Example: msal.token.api://spe-bff-api/user_impersonation
   */
  private static readonly CACHE_KEY_PREFIX = "msal.token.";

  /**
   * Token expiration buffer (milliseconds)
   *
   * Refresh token this many milliseconds BEFORE actual expiration
   * to avoid using expired tokens.
   *
   * 5 minutes = 300,000 ms
   */
  private static readonly EXPIRATION_BUFFER_MS = 5 * 60 * 1000;

  // ============================================================================
  // Private Fields (existing)
  // ============================================================================
  // ... (keep existing private fields)
```

---

### Step 3: Add Cache Helper Methods

**Add private methods after `acquireTokenPopup()`:**

```typescript
// ============================================================================
// Token Cache Management (sessionStorage)
// ============================================================================

/**
 * Get cached token from sessionStorage
 *
 * Checks sessionStorage for cached token matching requested scopes.
 * Returns null if token missing, expired, or scopes don't match.
 *
 * @param scopes - OAuth scopes to match
 * @returns Cached token string, or null if not found/expired
 */
private getCachedToken(scopes: string[]): string | null {
  try {
    const cacheKey = this.getCacheKey(scopes);
    const cachedData = sessionStorage.getItem(cacheKey);

    if (!cachedData) {
      console.debug("[MsalAuthProvider] No cached token found for scopes:", scopes);
      return null;
    }

    // Parse cached token entry
    const cacheEntry: TokenCacheEntry = JSON.parse(cachedData);

    // Check if token expired (with buffer)
    const now = Date.now();
    const expiresAt = cacheEntry.expiresAt;
    const bufferExpiration = expiresAt - MsalAuthProvider.EXPIRATION_BUFFER_MS;

    if (now >= bufferExpiration) {
      console.debug(
        "[MsalAuthProvider] Cached token expired or near expiration, will reacquire. " +
        `Expires: ${new Date(expiresAt).toISOString()}, Now: ${new Date(now).toISOString()}`
      );

      // Remove expired token from cache
      this.removeCachedToken(scopes);
      return null;
    }

    // Check if scopes match (in case of cache key collision)
    const scopesMatch = this.scopesMatch(cacheEntry.scopes, scopes);
    if (!scopesMatch) {
      console.debug("[MsalAuthProvider] Cached token scopes don't match requested scopes");
      return null;
    }

    console.debug(
      "[MsalAuthProvider] Using cached token ✅ " +
      `(expires in ${Math.round((expiresAt - now) / 1000 / 60)} minutes)`
    );

    return cacheEntry.token;

  } catch (error) {
    console.warn("[MsalAuthProvider] Failed to read cached token, will reacquire", error);
    return null;
  }
}

/**
 * Cache token in sessionStorage
 *
 * Stores token with expiration timestamp and scopes.
 *
 * @param token - Access token string
 * @param expiresOn - Token expiration date from MSAL
 * @param scopes - OAuth scopes for this token
 */
private setCachedToken(token: string, expiresOn: Date, scopes: string[]): void {
  try {
    const cacheKey = this.getCacheKey(scopes);

    const cacheEntry: TokenCacheEntry = {
      token,
      expiresAt: expiresOn.getTime(), // Convert Date to Unix epoch milliseconds
      scopes,
    };

    sessionStorage.setItem(cacheKey, JSON.stringify(cacheEntry));

    console.debug(
      "[MsalAuthProvider] Token cached ✅ " +
      `(expires: ${expiresOn.toISOString()})`
    );

  } catch (error) {
    // sessionStorage can throw if quota exceeded or in private browsing mode
    console.warn("[MsalAuthProvider] Failed to cache token (will continue without cache)", error);
  }
}

/**
 * Remove cached token from sessionStorage
 *
 * @param scopes - OAuth scopes to remove cache for
 */
private removeCachedToken(scopes: string[]): void {
  try {
    const cacheKey = this.getCacheKey(scopes);
    sessionStorage.removeItem(cacheKey);
    console.debug("[MsalAuthProvider] Cached token removed");
  } catch (error) {
    console.warn("[MsalAuthProvider] Failed to remove cached token", error);
  }
}

/**
 * Generate cache key for scopes
 *
 * Format: msal.token.<scopes-joined>
 * Example: msal.token.api://spe-bff-api/user_impersonation
 *
 * @param scopes - OAuth scopes
 * @returns Cache key string
 */
private getCacheKey(scopes: string[]): string {
  // Sort scopes for consistent cache keys
  // ["scope2", "scope1"] and ["scope1", "scope2"] → same key
  const sortedScopes = scopes.slice().sort();
  return MsalAuthProvider.CACHE_KEY_PREFIX + sortedScopes.join(",");
}

/**
 * Check if two scope arrays match
 *
 * Order-independent comparison.
 *
 * @param scopes1 - First scope array
 * @param scopes2 - Second scope array
 * @returns true if scopes match (ignoring order)
 */
private scopesMatch(scopes1: string[], scopes2: string[]): boolean {
  if (scopes1.length !== scopes2.length) {
    return false;
  }

  const sorted1 = scopes1.slice().sort();
  const sorted2 = scopes2.slice().sort();

  return sorted1.every((scope, index) => scope === sorted2[index]);
}
```

---

### Step 4: Update getToken() to Use Cache

**Modify `getToken()` method to check cache first:**

```typescript
/**
 * Get access token for specified scopes
 *
 * Flow:
 * 1. Check sessionStorage cache for unexpired token
 * 2. If cached and valid, return cached token (fast path)
 * 3. If not cached or expired, acquire new token via MSAL
 * 4. Cache newly acquired token
 * 5. Return token
 *
 * @param scopes - OAuth scopes to request
 * @returns Access token string for use in Authorization header
 */
public async getToken(scopes: string[]): Promise<string> {
  // Validate initialization
  if (!this.isInitialized || !this.msalInstance) {
    throw new Error("MSAL not initialized. Call initialize() first.");
  }

  // Validate scopes parameter
  if (!scopes || scopes.length === 0) {
    throw new Error("Scopes parameter is required and must not be empty.");
  }

  // ========================================================================
  // Step 1: Check Cache (Fast Path)
  // ========================================================================
  const cachedToken = this.getCachedToken(scopes);
  if (cachedToken) {
    // Cached token found and still valid
    return cachedToken;
  }

  // ========================================================================
  // Step 2: Acquire New Token (Cache Miss or Expired)
  // ========================================================================
  console.info(`[MsalAuthProvider] Acquiring token for scopes: ${scopes.join(", ")}`);

  try {
    // Try SSO silent token acquisition
    const tokenResponse = await this.acquireTokenSilent(scopes);

    console.info("[MsalAuthProvider] Token acquired successfully via silent flow ✅");
    console.debug("[MsalAuthProvider] Token expires:", tokenResponse.expiresOn);

    // Cache the newly acquired token
    if (tokenResponse.expiresOn) {
      this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
    }

    return tokenResponse.accessToken;

  } catch (error) {
    // Handle InteractionRequiredAuthError (Fallback to Popup)
    if (error instanceof InteractionRequiredAuthError) {
      console.warn(
        "[MsalAuthProvider] Silent token acquisition failed, user interaction required. " +
        "Falling back to popup login..."
      );

      try {
        const tokenResponse = await this.acquireTokenPopup(scopes);

        console.info("[MsalAuthProvider] Token acquired successfully via popup ✅");
        console.debug("[MsalAuthProvider] Token expires:", tokenResponse.expiresOn);

        // Cache the token acquired via popup
        if (tokenResponse.expiresOn) {
          this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
        }

        return tokenResponse.accessToken;

      } catch (popupError) {
        console.error("[MsalAuthProvider] Popup token acquisition failed ❌", popupError);
        throw new Error(
          `Failed to acquire token via popup: ${popupError instanceof Error ? popupError.message : "Unknown error"}`
        );
      }
    }

    // Handle other errors
    console.error("[MsalAuthProvider] Token acquisition failed ❌", error);
    throw new Error(
      `Failed to acquire token: ${error instanceof Error ? error.message : "Unknown error"}`
    );
  }
}
```

---

### Step 5: Update clearCache() to Clear sessionStorage

**Modify existing `clearCache()` method:**

```typescript
/**
 * Clear token cache and sign out
 *
 * Removes cached tokens from:
 * 1. sessionStorage (SDAP cache layer)
 * 2. MSAL internal cache
 *
 * User will need to re-authenticate on next getToken() call.
 */
public clearCache(): void {
  console.info("[MsalAuthProvider] Clearing token cache");

  // ========================================================================
  // Step 1: Clear sessionStorage cache
  // ========================================================================
  try {
    // Find all keys starting with our cache prefix
    const keysToRemove: string[] = [];
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i);
      if (key && key.startsWith(MsalAuthProvider.CACHE_KEY_PREFIX)) {
        keysToRemove.push(key);
      }
    }

    // Remove all cached tokens
    keysToRemove.forEach(key => sessionStorage.removeItem(key));

    console.debug(`[MsalAuthProvider] Removed ${keysToRemove.length} cached tokens from sessionStorage`);

  } catch (error) {
    console.warn("[MsalAuthProvider] Failed to clear sessionStorage cache", error);
  }

  // ========================================================================
  // Step 2: Clear MSAL cache
  // ========================================================================
  if (this.msalInstance) {
    const accounts = this.msalInstance.getAllAccounts();
    accounts.forEach((account) => {
      this.msalInstance?.clearCache(account);
    });
  }

  // Reset current account
  this.currentAccount = null;

  console.info("[MsalAuthProvider] Token cache cleared ✅");
}
```

---

### Step 6: Add TokenCacheEntry Import

**Verify import at top of MsalAuthProvider.ts:**

```typescript
import { IAuthProvider, AuthToken, AuthError, TokenCacheEntry } from "../../types/auth";
```

**If `TokenCacheEntry` not imported, add it.**

---

### Step 7: Build and Test

**Build:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm run build
```

**Test caching behavior:**

Create temporary test file: `test-token-caching.ts`

```typescript
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";

async function testCaching() {
  const provider = MsalAuthProvider.getInstance();
  await provider.initialize();

  const scopes = ["api://spe-bff-api/user_impersonation"];

  console.log("=== First call (should acquire token) ===");
  const start1 = Date.now();
  const token1 = await provider.getToken(scopes);
  const duration1 = Date.now() - start1;
  console.log(`Token acquired in ${duration1}ms`);
  console.log(`Token: ${token1.substring(0, 20)}...`);

  console.log("\n=== Second call (should use cache) ===");
  const start2 = Date.now();
  const token2 = await provider.getToken(scopes);
  const duration2 = Date.now() - start2;
  console.log(`Token acquired in ${duration2}ms`);
  console.log(`Token: ${token2.substring(0, 20)}...`);

  console.log(`\n✅ Cache speedup: ${duration1 / duration2}x faster`);
  console.log(`Same token: ${token1 === token2 ? "✅ Yes" : "❌ No"}`);
}

testCaching().catch(console.error);
```

**Expected output:**
```
=== First call (should acquire token) ===
[MsalAuthProvider] Acquiring token for scopes: api://spe-bff-api/user_impersonation
[MsalAuthProvider] Token acquired successfully via silent flow ✅
[MsalAuthProvider] Token cached ✅ (expires: 2025-10-06T15:30:00.000Z)
Token acquired in 450ms

=== Second call (should use cache) ===
[MsalAuthProvider] Using cached token ✅ (expires in 59 minutes)
Token acquired in 1ms

✅ Cache speedup: 450x faster
Same token: ✅ Yes
```

---

## Verification Checklist

**Task 2.2 complete when:**

- ✅ Cache helper methods implemented:
  - `getCachedToken(scopes)` - Retrieves from sessionStorage
  - `setCachedToken(token, expiresOn, scopes)` - Stores in sessionStorage
  - `removeCachedToken(scopes)` - Removes from sessionStorage
  - `getCacheKey(scopes)` - Generates cache key
  - `scopesMatch(scopes1, scopes2)` - Compares scope arrays
- ✅ `getToken()` updated to:
  - Check cache first
  - Return cached token if valid
  - Acquire new token if cache miss/expired
  - Cache newly acquired tokens
- ✅ `clearCache()` updated to clear sessionStorage
- ✅ Cache constants defined (CACHE_KEY_PREFIX, EXPIRATION_BUFFER_MS)
- ✅ Token expiration checking with 5-minute buffer
- ✅ `npm run build` compiles without errors
- ✅ Test shows significant speedup on cached calls

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
npx tsc --noEmit && \
npm run build && \
echo "✅ Task 2.2 Complete"
```

---

## Troubleshooting

### Issue 1: "Cannot access sessionStorage"

**Cause:** Browser in private/incognito mode or sessionStorage disabled.

**Fix:** Wrap in try-catch (already done in cache methods):
```typescript
try {
  sessionStorage.setItem(key, value);
} catch (error) {
  // Continue without cache
  console.warn("sessionStorage unavailable, continuing without cache");
}
```

---

### Issue 2: Tokens not being cached (always reacquiring)

**Cause:** `expiresOn` from MSAL response is null/undefined.

**Debug:**
```typescript
// In getToken() after acquiring token:
console.log("Token response:", {
  hasToken: !!tokenResponse.accessToken,
  expiresOn: tokenResponse.expiresOn,
  expiresOnType: typeof tokenResponse.expiresOn
});

// If expiresOn is null, MSAL may have an issue
```

**Fix:** Fallback to default expiration:
```typescript
const expiresOn = tokenResponse.expiresOn || new Date(Date.now() + 60 * 60 * 1000); // 1 hour default
this.setCachedToken(tokenResponse.accessToken, expiresOn, scopes);
```

---

### Issue 3: Cache key collisions (wrong token returned)

**Cause:** Multiple APIs with similar scope names.

**Verify:** Cache key includes all scopes:
```typescript
// Test cache key generation
const scopes1 = ["api://app1/read", "api://app1/write"];
const scopes2 = ["api://app1/write", "api://app1/read"]; // Same scopes, different order

const key1 = provider.getCacheKey(scopes1);
const key2 = provider.getCacheKey(scopes2);

console.log("Keys match:", key1 === key2); // Should be true (order-independent)
```

---

### Issue 4: sessionStorage quota exceeded

**Symptoms:** `setCachedToken()` throws QuotaExceededError.

**Cause:** sessionStorage full (usually 5-10 MB limit per origin).

**Fix:** Already handled with try-catch, but can add cleanup:
```typescript
private setCachedToken(token: string, expiresOn: Date, scopes: string[]): void {
  try {
    // ... existing code ...
    sessionStorage.setItem(cacheKey, JSON.stringify(cacheEntry));
  } catch (error) {
    if (error instanceof DOMException && error.name === "QuotaExceededError") {
      console.warn("[MsalAuthProvider] sessionStorage quota exceeded, clearing old tokens");
      // Clear all MSAL tokens to free space
      this.clearCache();
      // Retry caching
      try {
        sessionStorage.setItem(cacheKey, JSON.stringify(cacheEntry));
      } catch {
        console.warn("[MsalAuthProvider] Still can't cache, continuing without cache");
      }
    }
  }
}
```

---

## Performance Benefits

### Before Caching (Task 2.1)

- **First call:** 450ms (acquire token)
- **Second call:** 450ms (acquire token again)
- **Third call:** 450ms (acquire token again)
- **100 calls:** ~45 seconds

### After Caching (Task 2.2)

- **First call:** 450ms (acquire token + cache)
- **Second call:** < 1ms (cached)
- **Third call:** < 1ms (cached)
- **100 calls:** ~500ms (1 acquire + 99 cached)

**Speedup:** ~90x faster for repeated calls ✅

---

## Next Steps

**After Task 2.2 completion:**

✅ **Task 2.2 Complete** - Token caching with sessionStorage implemented

➡️ **Task 2.3: Add Token Refresh Logic**
- Proactive token refresh before expiration
- Background refresh without blocking API calls
- Handle refresh failures gracefully

**See:** `TASK-2.3-IMPLEMENT-TOKEN-REFRESH.md`

---

## Files Modified

**Modified:**
- `services/auth/MsalAuthProvider.ts` - Added cache methods, updated `getToken()` and `clearCache()`

---

## Related Documentation

- [Web Storage API (sessionStorage)](https://developer.mozilla.org/en-US/docs/Web/API/Window/sessionStorage)
- [MSAL.js Token Caching](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-acquire-cache-tokens)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)

---

**Task Status:** 📋 **READY TO START**
**Next Task:** Task 2.3 - Token Refresh Logic
**Estimated Duration:** 45 minutes

---
