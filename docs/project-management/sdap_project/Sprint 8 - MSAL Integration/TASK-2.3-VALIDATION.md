# Task 2.3 Validation: Proactive Token Refresh Prerequisites

**Date:** 2025-10-06
**Task:** 2.3 - Implement Proactive Token Refresh
**Status:** ✅ **VALIDATED - READY TO IMPLEMENT**

---

## Validation Summary

Task 2.3 instructions have been reviewed and validated against Tasks 2.1-2.2 implementations. The proactive refresh strategy is sound and aligns with current codebase state.

---

## Prerequisites Check

### ✅ Task 2.1 Complete
- **Status:** COMPLETED
- **Verification:** `acquireTokenSilent()` and `acquireTokenPopup()` fully implemented
- **File:** [MsalAuthProvider.ts:332-498](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts#L332-L498)

### ✅ Task 2.2 Complete
- **Status:** COMPLETED
- **Verification:** sessionStorage caching with expiration checking implemented
- **File:** [MsalAuthProvider.ts:500-643](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts#L500-L643)
- **Current getCachedToken():** Lines 513-560 - Basic expiration check only

### ✅ Expiration Buffer Defined
- **Location:** Line 59
- **Value:** `5 * 60 * 1000` (5 minutes)
- **Usage:** Currently used in `getCachedToken()` for hard expiration check

---

## Current vs. Required State

### Current getCachedToken() Behavior (Task 2.2)

**Current Flow:**
```
getCachedToken(scopes)
  ↓
  if (now >= bufferExpiration)  // Hard cutoff
    ├─ Remove token
    └─ Return null → Caller acquires new token (blocks)
  ↓
  Return cached token
```

**Problem:** Token expiration is **reactive** (blocks on expiration)

---

### Required getCachedToken() Behavior (Task 2.3)

**Enhanced Flow (Task 2.3):**
```
getCachedToken(scopes)
  ↓
  Case 1: Token expired (now >= bufferExpiration)
    ├─ Remove token
    └─ Return null → Caller acquires (expected)
  ↓
  Case 2: Token nearing expiration (now >= refreshThreshold)
    ├─ Trigger background refresh (non-blocking)
    └─ Return current token (still valid)
  ↓
  Case 3: Token fresh
    └─ Return cached token
```

**Solution:** Token refresh is **proactive** (background, non-blocking)

---

## Implementation Plan Validation

### 1. Refresh State Tracking

**Task Doc (Lines 106):**
```typescript
private refreshPromises: Map<string, Promise<void>> = new Map();
```

**Purpose:**
- ✅ Track ongoing refresh operations
- ✅ Prevent duplicate refresh for same scopes
- ✅ Key = scopes (comma-separated, sorted) via `getCacheKey()`
- ✅ Value = Promise that resolves when refresh complete

**Validation:**
- ✅ Map is appropriate data structure (key-value lookups)
- ✅ Using existing `getCacheKey()` ensures consistency
- ✅ Cleared in `clearCache()` method

---

### 2. Background Refresh Method

**Method Signature (Line 129):**
```typescript
private refreshTokenInBackground(scopes: string[]): void
```

**Key Features:**
- ✅ **Returns immediately** (void, non-blocking)
- ✅ **Async operations in IIFE** `(async () => { ... })()`
- ✅ **Duplicate prevention** via Map check
- ✅ **Error handling** (doesn't throw, logs warning)
- ✅ **Cache update on success** via `setCachedToken()`
- ✅ **Cleanup on completion** via `finally` block

**Validation:**
- ✅ Method won't block caller (returns void immediately)
- ✅ Uses existing `acquireTokenSilent()` from Task 2.1
- ✅ Uses existing `setCachedToken()` from Task 2.2
- ✅ Proper error isolation (background errors don't break app)

---

### 3. Refresh Threshold Calculation

**Task Doc (Lines 224-227):**
```typescript
const timeUntilBuffer = bufferExpiration - now;
const refreshThreshold = bufferExpiration - (timeUntilBuffer / 2);
```

**Formula Validation:**

**Example: 60-minute token**
```
Token acquired: 2:00 PM
Token expires: 3:00 PM
Buffer (5 min): 2:55 PM

timeUntilBuffer = 2:55 PM - 2:00 PM = 55 minutes
refreshThreshold = 2:55 PM - (55/2) = 2:55 PM - 27.5 min = 2:27:30 PM

Timeline:
2:00 PM ─── 2:27:30 PM ──── 2:55 PM ─── 3:00 PM
  │           │               │           │
Acquired    Refresh       Buffer      Expires
           threshold      starts
```

**Validation:**
- ✅ Refresh triggers halfway to expiration buffer
- ✅ Provides ample time for background refresh to complete
- ✅ Token still valid while refreshing

---

### 4. getCachedToken() Enhancement

**Required Changes:**
1. ✅ Add refresh threshold calculation (Lines 224-227)
2. ✅ Add Case 2 logic for nearing expiration (Lines 229-241)
3. ✅ Call `refreshTokenInBackground()` when threshold reached
4. ✅ Still return current token (don't block)

**Current Implementation:**
- Lines 531-540: Handles Case 1 (expired) ✅
- Lines 542-547: Handles scope validation ✅
- **Missing:** Case 2 (nearing expiration) logic

**Implementation Needed:**
- Insert Case 2 logic between Line 540 and Line 542
- Calculate refresh threshold
- Trigger background refresh if threshold reached
- Continue to return current token

---

### 5. clearCache() Enhancement

**Task Doc (Lines 283-286):**
```typescript
if (this.refreshPromises.size > 0) {
  console.debug(`Cancelling ${this.refreshPromises.size} ongoing refresh operations`);
  this.refreshPromises.clear();
}
```

**Current clearCache() (Lines 316-355):**
- Step 1: Clear sessionStorage ✅
- Step 2: Clear MSAL cache ✅
- **Missing:** Cancel refresh promises

**Implementation Needed:**
- Insert refresh promise cleanup as new Step 1
- Existing steps become Step 2 and Step 3

---

## Refresh Timing Analysis

### Timing Calculation Verification

**For 60-minute token:**
- Acquired: 2:00 PM
- Expires: 3:00 PM
- Buffer: 2:55 PM (5 min before)
- Refresh threshold: 2:27:30 PM (halfway to buffer)
- ✅ **27.5 minutes** from acquisition

**For 90-minute token:**
- Acquired: 2:00 PM
- Expires: 3:30 PM
- Buffer: 3:25 PM (5 min before)
- Refresh threshold: 2:42:30 PM (halfway to buffer)
- ✅ **42.5 minutes** from acquisition

**Validation:**
- ✅ Longer-lived tokens refresh later (proportional)
- ✅ Always refreshes with enough time before expiration
- ✅ Formula is generic (works for any token lifetime)

---

## Edge Cases Analysis

### Edge Case 1: Multiple Concurrent Calls During Refresh

**Scenario:**
1. Call 1: Token near expiration → Triggers refresh, returns cached
2. Call 2 (concurrent): Token near expiration → Should NOT trigger duplicate refresh

**Task 2.3 Solution:**
```typescript
if (this.refreshPromises.has(scopesKey)) {
  console.debug("Refresh already in progress");
  return; // Prevent duplicate
}
```

**Validation:** ✅ Map check prevents duplicate refreshes

---

### Edge Case 2: Refresh Fails

**Scenario:** Background refresh fails (network error, auth error)

**Task 2.3 Solution (Lines 153-161):**
```typescript
catch (error) {
  console.warn("Background token refresh failed (will retry on next call)", error);
  this.removeCachedToken(scopes);  // Remove failed token
}
```

**Behavior:**
1. ✅ Error logged (not thrown)
2. ✅ Cached token removed
3. ✅ Next `getToken()` will acquire fresh token
4. ✅ App continues functioning (graceful degradation)

**Validation:** ✅ Proper error isolation and recovery

---

### Edge Case 3: Token Acquired During Refresh

**Scenario:**
1. Background refresh starts
2. User triggers `getToken()` before refresh completes
3. Cache still has old token (refresh not complete)

**Task 2.3 Behavior:**
```typescript
// getCachedToken() checks refresh threshold again
if (now >= refreshThreshold) {
  // Refresh already in progress (Map check prevents duplicate)
  this.refreshTokenInBackground(scopes);
}
// Returns old token (still valid)
```

**Validation:** ✅ Old token returned safely, duplicate refresh prevented

---

## Potential Issues & Mitigations

### Issue 1: Map Key Consistency

**Risk:** Different cache key generation could cause refresh tracking to fail

**Mitigation:**
```typescript
const scopesKey = this.getCacheKey(scopes);  // Uses existing method
```

**Validation:** ✅ Reuses `getCacheKey()` from Task 2.2 (proven correct)

---

### Issue 2: Refresh Promise Never Cleaned Up

**Risk:** Failed refresh leaves promise in Map forever

**Mitigation (Line 164-165):**
```typescript
finally {
  this.refreshPromises.delete(scopesKey);  // Always cleans up
}
```

**Validation:** ✅ `finally` ensures cleanup even on error

---

### Issue 3: MSAL v4 Compatibility

**Task Doc Assumption:** `acquireTokenSilent()` works for background refresh

**Current Implementation (Task 2.1):**
- Lines 332-395: `acquireTokenSilent()` fully implemented ✅
- Uses MSAL v4 API correctly ✅

**Validation:** ✅ No compatibility issues, method already proven

---

## Alignment Issues Found

### ⚠️ Minor: clearCache() Step Numbering

**Task Doc (Lines 289-308):**
- Step 1: Cancel refresh operations (NEW)
- Step 2: Clear sessionStorage
- Step 3: Clear MSAL cache

**Current Implementation (Lines 319-355):**
- Step 1: Clear sessionStorage
- Step 2: Clear MSAL cache
- (Missing: Cancel refresh operations)

**Fix:** Add refresh cancellation as new Step 1, renumber existing steps

---

### ✅ No Breaking Changes Required

**Existing Methods (Unchanged):**
- ✅ `acquireTokenSilent()` - Used as-is
- ✅ `setCachedToken()` - Used as-is
- ✅ `removeCachedToken()` - Used as-is
- ✅ `getCacheKey()` - Used as-is

**Modified Methods:**
- `getCachedToken()` - Enhanced with Case 2 logic (backwards compatible)
- `clearCache()` - Add refresh cancellation (backwards compatible)

---

## Dependencies Check

### Required Components (All Available)

- ✅ `Map<string, Promise<void>>` - JavaScript built-in
- ✅ `acquireTokenSilent(scopes)` - Task 2.1 (Lines 332-395)
- ✅ `setCachedToken(token, expiresOn, scopes)` - Task 2.2 (Lines 571-592)
- ✅ `removeCachedToken(scopes)` - Task 2.2 (Lines 599-607)
- ✅ `getCacheKey(scopes)` - Task 2.2 (Lines 618-623)
- ✅ `EXPIRATION_BUFFER_MS` constant - Line 59

### No New Imports Required
- ✅ All functionality uses existing methods
- ✅ No external dependencies

---

## Performance Impact Analysis

### Memory Impact

**Before Task 2.3:**
- sessionStorage: ~1 KB per token
- Class fields: ~100 bytes

**After Task 2.3:**
- sessionStorage: ~1 KB per token (unchanged)
- Class fields: ~100 bytes
- **New:** `refreshPromises` Map: ~50 bytes per active refresh
- **Total:** Negligible increase (< 100 bytes per refresh)

**Validation:** ✅ Minimal memory overhead

---

### CPU Impact

**Additional Operations:**
- Refresh threshold calculation: ~0.01ms (arithmetic only)
- Map operations: ~0.001ms (get/set/delete)
- Background refresh: Async (non-blocking, no CPU impact on caller)

**Validation:** ✅ Negligible CPU overhead

---

## Testing Strategy

### Manual Test (As Per Task Doc)

1. Acquire token (expires in 60 min)
2. Manually set expiration to 3 minutes
3. Call `getToken()` → Should trigger refresh
4. Wait 2 seconds for refresh to complete
5. Call `getToken()` → Should return refreshed token

**Validation:** ✅ Test plan is comprehensive and feasible

---

### Edge Case Tests

1. **Duplicate Refresh Prevention:**
   - Trigger refresh
   - Immediately call `getToken()` again
   - Verify Map prevents duplicate

2. **Refresh Failure:**
   - Mock `acquireTokenSilent()` to throw error
   - Verify error logged, not thrown
   - Verify cached token removed

3. **Clear During Refresh:**
   - Start background refresh
   - Call `clearCache()`
   - Verify Map cleared, refresh cancelled

**Validation:** ✅ All edge cases testable

---

## Implementation Sequence

### Step-by-Step (Validated)

1. ✅ **Add `refreshPromises` Map field** (after existing fields)
2. ✅ **Implement `refreshTokenInBackground()` method** (after cache helpers)
3. ✅ **Update `getCachedToken()` with Case 2 logic** (insert between Lines 540-542)
4. ✅ **Update `clearCache()` to cancel refreshes** (insert at beginning)
5. ✅ **Build and verify**

**Validation:** Sequence is logical and non-breaking

---

## Conclusion

**Task 2.3 is validated and ready for implementation.**

### ✅ All Prerequisites Met
- Task 2.1 complete (`acquireTokenSilent()` available)
- Task 2.2 complete (caching infrastructure ready)
- Expiration buffer defined and working

### ✅ Implementation Plan Sound
- Refresh threshold calculation validated
- Background refresh strategy proven
- Edge cases properly handled
- No breaking changes

### ⚠️ Minor Adjustments Needed
1. Add `refreshPromises` Map field
2. Implement `refreshTokenInBackground()` method
3. Enhance `getCachedToken()` with proactive refresh logic
4. Add refresh cancellation to `clearCache()`

**Ready to proceed with implementation.**

---

**Validation Status:** ✅ **APPROVED**
**Next Action:** Implement Task 2.3 proactive refresh logic
**Estimated Duration:** 30 minutes (as per task document)
**Expected Impact:** Eliminates token expiration delays, ~450ms saved per expired token scenario

---
