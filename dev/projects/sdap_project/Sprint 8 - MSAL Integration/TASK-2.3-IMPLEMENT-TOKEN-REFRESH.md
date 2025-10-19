# Task 2.3: Implement Proactive Token Refresh

**Sprint:** 8 - MSAL Integration
**Phase:** 2 - Token Acquisition
**Task:** 2.3 of 3 (Phase 2)
**Duration:** 30 minutes
**Status:** 📋 **READY TO START**

---

## Task Goal

> **Add proactive token refresh logic to prevent using expired tokens during API calls.**

**Success Criteria:**
- ✅ Token refresh triggered before expiration (not after)
- ✅ Background refresh doesn't block current API calls
- ✅ Refresh failures handled gracefully (fallback to cache or reacquire)
- ✅ Logging for refresh operations
- ✅ TypeScript compiles without errors

---

## Context

### Why This Task Matters

**Problem with reactive refresh** (Task 2.2):
1. Token expires at 3:00 PM
2. User makes API call at 3:01 PM
3. Cache returns null (token expired)
4. **Blocks API call** while acquiring new token (450ms delay)
5. Poor user experience

**Solution with proactive refresh**:
1. Token expires at 3:00 PM
2. At 2:55 PM (5 minutes before), **background refresh** starts
3. New token acquired and cached
4. User makes API call at 3:01 PM
5. Cache returns new token (< 1ms, no delay)
6. ✅ Smooth user experience

### ADR Compliance

**ADR-002 (No Heavy Plugins):**
- ✅ Refresh happens client-side (no plugins)
- ✅ Background operation (non-blocking)

### Sprint 4 Integration

Proactive refresh ensures Spe.Bff.Api OBO calls never fail due to expired tokens:

**Flow:**
```
fileService.ts → getToken()
  ↓ Cache check (Task 2.2)
  ↓ Token valid (refreshed proactively in Task 2.3)
  ↓ Return cached token (fast)
Authorization: Bearer <always-valid-token>
  ↓
Spe.Bff.Api OBO endpoints ✅ No token expiration errors
```

---

## Prerequisites

**Before starting:**
- ✅ Task 2.1 completed (token acquisition with ssoSilent)
- ✅ Task 2.2 completed (sessionStorage caching)

**Expected state:**
- `getToken()` checks cache, returns cached if valid
- Expired tokens detected via `EXPIRATION_BUFFER_MS` (5 minutes)

---

## Step-by-Step Instructions

### Step 1: Add Refresh State Tracking

**File:** `services/auth/MsalAuthProvider.ts`

**Add private field after existing fields:**

```typescript
export class MsalAuthProvider implements IAuthProvider {
  // ============================================================================
  // Private Fields (existing)
  // ============================================================================
  private static instance: MsalAuthProvider;
  private msalInstance: PublicClientApplication | null = null;
  private currentAccount: AccountInfo | null = null;
  private isInitialized = false;

  // ============================================================================
  // Token Refresh State (NEW)
  // ============================================================================

  /**
   * Track ongoing refresh operations (prevent duplicate refreshes)
   *
   * Key: scopes (comma-separated, sorted)
   * Value: Promise<void> (resolves when refresh complete)
   */
  private refreshPromises: Map<string, Promise<void>> = new Map();

  // ... rest of class
}
```

---

### Step 2: Implement Token Refresh Method

**Add method after cache helper methods:**

```typescript
/**
 * Proactively refresh token before expiration
 *
 * Called when cached token is near expiration (within EXPIRATION_BUFFER_MS).
 * Acquires new token in background and updates cache.
 *
 * Non-blocking: Returns immediately, refresh happens asynchronously.
 *
 * @param scopes - OAuth scopes to refresh token for
 */
private refreshTokenInBackground(scopes: string[]): void {
  const scopesKey = this.getCacheKey(scopes);

  // Check if refresh already in progress for these scopes
  if (this.refreshPromises.has(scopesKey)) {
    console.debug("[MsalAuthProvider] Token refresh already in progress for scopes:", scopes);
    return;
  }

  console.info("[MsalAuthProvider] Starting background token refresh for scopes:", scopes);

  // Create refresh promise
  const refreshPromise = (async () => {
    try {
      // Acquire new token silently
      const tokenResponse = await this.acquireTokenSilent(scopes);

      console.info("[MsalAuthProvider] Background token refresh succeeded ✅");

      // Update cache with new token
      if (tokenResponse.expiresOn) {
        this.setCachedToken(tokenResponse.accessToken, tokenResponse.expiresOn, scopes);
      }

    } catch (error) {
      // Log error but don't throw (background operation should not break app)
      console.warn(
        "[MsalAuthProvider] Background token refresh failed (will retry on next call)",
        error
      );

      // Remove failed token from cache so next getToken() will acquire fresh token
      this.removeCachedToken(scopes);

    } finally {
      // Remove from refresh tracking
      this.refreshPromises.delete(scopesKey);
    }
  })();

  // Track refresh promise
  this.refreshPromises.set(scopesKey, refreshPromise);
}
```

---

### Step 3: Update getCachedToken() to Trigger Refresh

**Modify `getCachedToken()` method to trigger proactive refresh:**

```typescript
/**
 * Get cached token from sessionStorage
 *
 * Checks sessionStorage for cached token matching requested scopes.
 * If token found but nearing expiration, triggers background refresh.
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

    const now = Date.now();
    const expiresAt = cacheEntry.expiresAt;
    const bufferExpiration = expiresAt - MsalAuthProvider.EXPIRATION_BUFFER_MS;

    // ========================================================================
    // Case 1: Token Expired (Past Buffer)
    // ========================================================================
    if (now >= bufferExpiration) {
      console.debug(
        "[MsalAuthProvider] Cached token expired or past expiration buffer. " +
        `Expires: ${new Date(expiresAt).toISOString()}, Now: ${new Date(now).toISOString()}`
      );

      // Remove expired token
      this.removeCachedToken(scopes);
      return null; // Caller will acquire new token
    }

    // ========================================================================
    // Case 2: Token Valid but Nearing Expiration (Proactive Refresh)
    // ========================================================================
    // Calculate refresh threshold (halfway between now and expiration buffer)
    // Example: Token expires in 60 min, buffer is 5 min, refresh at 32.5 min remaining
    const timeUntilBuffer = bufferExpiration - now;
    const refreshThreshold = bufferExpiration - (timeUntilBuffer / 2);

    if (now >= refreshThreshold) {
      const minutesUntilExpiration = Math.round((expiresAt - now) / 1000 / 60);
      console.info(
        `[MsalAuthProvider] Token nearing expiration (${minutesUntilExpiration} min remaining), ` +
        "triggering background refresh..."
      );

      // Trigger non-blocking background refresh
      this.refreshTokenInBackground(scopes);

      // Still return current token (valid for now)
      // Next call will get refreshed token from cache
    }

    // ========================================================================
    // Case 3: Token Valid and Fresh
    // ========================================================================
    // Check if scopes match
    const scopesMatch = this.scopesMatch(cacheEntry.scopes, scopes);
    if (!scopesMatch) {
      console.debug("[MsalAuthProvider] Cached token scopes don't match requested scopes");
      return null;
    }

    const minutesRemaining = Math.round((expiresAt - now) / 1000 / 60);
    console.debug(
      `[MsalAuthProvider] Using cached token ✅ (expires in ${minutesRemaining} minutes)`
    );

    return cacheEntry.token;

  } catch (error) {
    console.warn("[MsalAuthProvider] Failed to read cached token, will reacquire", error);
    return null;
  }
}
```

---

### Step 4: Update clearCache() to Clear Refresh Tracking

**Add refresh promise cleanup to `clearCache()`:**

```typescript
/**
 * Clear token cache and sign out
 */
public clearCache(): void {
  console.info("[MsalAuthProvider] Clearing token cache");

  // ========================================================================
  // Step 1: Cancel ongoing refresh operations
  // ========================================================================
  if (this.refreshPromises.size > 0) {
    console.debug(`[MsalAuthProvider] Cancelling ${this.refreshPromises.size} ongoing refresh operations`);
    this.refreshPromises.clear();
  }

  // ========================================================================
  // Step 2: Clear sessionStorage cache
  // ========================================================================
  try {
    const keysToRemove: string[] = [];
    for (let i = 0; i < sessionStorage.length; i++) {
      const key = sessionStorage.key(i);
      if (key && key.startsWith(MsalAuthProvider.CACHE_KEY_PREFIX)) {
        keysToRemove.push(key);
      }
    }

    keysToRemove.forEach(key => sessionStorage.removeItem(key));
    console.debug(`[MsalAuthProvider] Removed ${keysToRemove.length} cached tokens from sessionStorage`);

  } catch (error) {
    console.warn("[MsalAuthProvider] Failed to clear sessionStorage cache", error);
  }

  // ========================================================================
  // Step 3: Clear MSAL cache
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

### Step 5: Build and Test

**Build:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid
npm run build
```

**Test proactive refresh:**

Create test file: `test-token-refresh.ts`

```typescript
import { MsalAuthProvider } from "./services/auth/MsalAuthProvider";

async function testProactiveRefresh() {
  const provider = MsalAuthProvider.getInstance();
  await provider.initialize();

  const scopes = ["api://spe-bff-api/user_impersonation"];

  console.log("=== Acquiring initial token ===");
  const token1 = await provider.getToken(scopes);
  console.log(`Token: ${token1.substring(0, 20)}...`);

  console.log("\n=== Simulating time passing (wait for refresh threshold) ===");
  // In real scenario, wait ~30 minutes for refresh threshold
  // For testing, manually trigger by modifying cached token expiration:

  // Get cached token
  const cacheKey = "msal.token.api://spe-bff-api/user_impersonation";
  const cached = JSON.parse(sessionStorage.getItem(cacheKey) || "{}");

  // Set expiration to 3 minutes from now (within refresh threshold)
  cached.expiresAt = Date.now() + (3 * 60 * 1000);
  sessionStorage.setItem(cacheKey, JSON.stringify(cached));

  console.log("\n=== Getting token again (should trigger background refresh) ===");
  const token2 = await provider.getToken(scopes);
  console.log(`Token: ${token2.substring(0, 20)}...`);
  console.log("Check console for background refresh logs");

  console.log("\n=== Waiting 2 seconds for background refresh to complete ===");
  await new Promise(resolve => setTimeout(resolve, 2000));

  console.log("\n=== Getting token again (should have refreshed token) ===");
  const token3 = await provider.getToken(scopes);
  console.log(`Token: ${token3.substring(0, 20)}...`);
  console.log(`Token changed: ${token2 !== token3 ? "✅ Yes (refreshed)" : "❌ No"}`);
}

testProactiveRefresh().catch(console.error);
```

**Expected output:**
```
=== Acquiring initial token ===
[MsalAuthProvider] Acquiring token for scopes: api://spe-bff-api/user_impersonation
Token: eyJ0eXAiOiJKV1QiLCJ...

=== Simulating time passing ===
=== Getting token again (should trigger background refresh) ===
[MsalAuthProvider] Using cached token ✅ (expires in 3 minutes)
[MsalAuthProvider] Token nearing expiration (3 min remaining), triggering background refresh...
[MsalAuthProvider] Starting background token refresh for scopes: api://spe-bff-api/user_impersonation
Token: eyJ0eXAiOiJKV1QiLCJ...

[MsalAuthProvider] Background token refresh succeeded ✅
[MsalAuthProvider] Token cached ✅ (expires: 2025-10-06T16:30:00.000Z)

=== Waiting 2 seconds ===
=== Getting token again (should have refreshed token) ===
[MsalAuthProvider] Using cached token ✅ (expires in 59 minutes)
Token: eyJ0eXAiOiJKV1QiLCJh... (different token)
Token changed: ✅ Yes (refreshed)
```

---

## Verification Checklist

**Task 2.3 complete when:**

- ✅ `refreshTokenInBackground()` method implemented with:
  - Duplicate refresh prevention (Map tracking)
  - Silent token acquisition
  - Cache update on success
  - Error handling (doesn't throw, logs warning)
  - Promise cleanup
- ✅ `getCachedToken()` updated with:
  - Three cases: expired, nearing expiration, fresh
  - Refresh threshold calculation
  - Background refresh trigger
  - Still returns current token while refreshing
- ✅ `refreshPromises` Map field added to class
- ✅ `clearCache()` updated to cancel refresh operations
- ✅ `npm run build` compiles without errors
- ✅ Test shows background refresh working

**Quick verification command:**
```bash
cd c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid && \
npx tsc --noEmit && \
npm run build && \
echo "✅ Task 2.3 Complete"
```

---

## Troubleshooting

### Issue 1: Background refresh never triggers

**Cause:** Refresh threshold never reached (token expires too far in future).

**Debug:**
```typescript
// In getCachedToken(), add logging:
const timeUntilBuffer = bufferExpiration - now;
const refreshThreshold = bufferExpiration - (timeUntilBuffer / 2);

console.log({
  now: new Date(now).toISOString(),
  expiresAt: new Date(expiresAt).toISOString(),
  bufferExpiration: new Date(bufferExpiration).toISOString(),
  refreshThreshold: new Date(refreshThreshold).toISOString(),
  shouldRefresh: now >= refreshThreshold
});
```

---

### Issue 2: Multiple refreshes triggered for same scopes

**Cause:** `refreshPromises` Map not preventing duplicates.

**Fix:** Already handled with Map check:
```typescript
if (this.refreshPromises.has(scopesKey)) {
  console.debug("Refresh already in progress");
  return; // Prevent duplicate
}
```

**Verify Map key is consistent:**
```typescript
// Test cache key generation
const scopes = ["api://spe-bff-api/user_impersonation"];
const key1 = this.getCacheKey(scopes);
const key2 = this.getCacheKey(scopes);
console.log("Keys match:", key1 === key2); // Should be true
```

---

### Issue 3: Background refresh fails but cached token still used

**Expected behavior:** This is **correct**.

**Explanation:**
- Cached token is still valid (within expiration buffer)
- Background refresh failure logged as warning
- Next `getToken()` call will try to acquire fresh token
- No breaking error for background operation

**If refresh always fails, check:**
1. User still authenticated (session may have ended)
2. Network connectivity
3. MSAL logs for specific error

---

## Refresh Timing Diagram

```
Token acquired at 2:00 PM, expires at 3:00 PM

Timeline:
2:00 PM ──────────────────── 2:27 PM ─────────────── 2:55 PM ──────── 3:00 PM
         │                     │                       │                │
         Token acquired        Refresh threshold       Expiration      Token
         (cache: 60 min)       (refresh triggered)     buffer          expires

         ← Valid & Fresh    →  ← Valid but refreshing → ← Expired   →

         getToken() returns    getToken() returns        getToken()
         cached (< 1ms)        cached + triggers         reacquires
                               background refresh        (450ms)
```

**Key timings:**
- **Expiration buffer:** 5 minutes before actual expiration
- **Refresh threshold:** Halfway between now and expiration buffer
  - For 60-minute token: Refresh at ~27.5 minutes
  - For 90-minute token: Refresh at ~42.5 minutes

---

## Phase 2 Complete! 🎉

**Congratulations!** Phase 2 (Token Acquisition) is now complete:

✅ **Task 2.1** - SSO silent token acquisition with popup fallback
✅ **Task 2.2** - sessionStorage caching with expiration checking
✅ **Task 2.3** - Proactive token refresh before expiration

**What we have now:**
- Full token acquisition flow (ssoSilent → popup fallback)
- Efficient caching (90x speedup on repeated calls)
- Proactive refresh (no expired tokens during API calls)
- Error handling for all failure scenarios

**Token acquisition is production-ready!** ✅

---

## Next Steps

**After Task 2.3 completion:**

✅ **Phase 2 Complete** - Token acquisition, caching, and refresh implemented

➡️ **Phase 3: HTTP Client Integration**
- Update `fileService.ts` to use `MsalAuthProvider`
- Add `Authorization: Bearer <token>` header to API calls
- Update API endpoint URLs to Spe.Bff.Api OBO endpoints
- Remove any Custom API references

**See:** Phase 3 task documents

---

## Files Modified

**Modified:**
- `services/auth/MsalAuthProvider.ts` - Added refresh logic, updated `getCachedToken()` and `clearCache()`

---

## Related Documentation

- [JWT Token Expiration](https://datatracker.ietf.org/doc/html/rfc7519#section-4.1.4)
- [MSAL.js Token Refresh](https://learn.microsoft.com/en-us/azure/active-directory/develop/msal-acquire-cache-tokens#token-refresh)
- [Sprint 8 MSAL Overview](./SPRINT-8-MSAL-OVERVIEW.md)

---

**Task Status:** 📋 **READY TO START**
**Next Phase:** Phase 3 - HTTP Client Integration
**Estimated Duration:** 30 minutes

---
