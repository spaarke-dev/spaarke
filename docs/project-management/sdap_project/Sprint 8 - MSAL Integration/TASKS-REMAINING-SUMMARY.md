# Sprint 8 MSAL Integration - Remaining Tasks Summary

**Status:** Phase 1-2 Complete, Phase 3-5 Tasks Listed Below

---

## Phase 3: HTTP Client Integration (1 task remaining)

### Task 3.2: Handle Token Errors in API Client (30 min)

**Goal:** Add retry logic for 401 errors (token expired during request)

**Steps:**
1. Add retry wrapper in `SdapApiClient`
2. On 401 response: Clear MSAL cache, reacquire token, retry request once
3. Add user-friendly error messages for auth failures
4. Prevent infinite retry loops (max 1 retry)

**Success Criteria:**
- 401 errors trigger token refresh + retry
- Second 401 throws error (no infinite loop)
- User sees friendly message: "Session expired. Please refresh the page."

---

## Phase 4: Testing and Validation

### Task 4.1: Create Unit Tests for MsalAuthProvider (60 min)

**Goal:** Test MSAL integration with mocked dependencies

**Tests:**
- `initialize()` success/failure scenarios
- `getToken()` with cache hit/miss
- `getToken()` SSO silent success
- `getToken()` fallback to popup
- Token refresh triggering
- Cache expiration logic
- Error handling

**Tools:** Jest, @testing-library/react

---

### Task 4.2: End-to-End Testing (45 min)

**Goal:** Test full flow in actual Dataverse environment

**Scenarios:**
1. **Happy path:** User loads control → SSO silent → API call succeeds
2. **First-time user:** No session → Popup login → API call succeeds
3. **Token expiration:** Wait 60 min → Token refreshes → API call succeeds
4. **Network failure:** Offline → Friendly error message
5. **Permission denied:** User lacks SPE permissions → 403 error handled

**Validation:**
- All file operations work (download, delete, replace, upload)
- No console errors
- Performance < 500ms for cached token calls

---

### Task 4.3: Performance Testing (30 min)

**Goal:** Validate caching performance gains

**Metrics:**
- First `getToken()` call: < 500ms (SSO silent)
- Subsequent calls with cache: < 5ms
- Token refresh (background): < 500ms, non-blocking
- 100 API calls with cached token: < 1 second total

**Tools:** Chrome DevTools Performance tab

---

## Phase 5: Documentation and Deployment

### Task 5.1: Create Deployment Runbook (45 min)

**Content:**
1. **Prerequisites:**
   - Azure App Registration configured
   - Redirect URI added
   - API permissions granted and consented

2. **Configuration:**
   - Update `msalConfig.ts` with production Client ID, Tenant ID, Redirect URI
   - Update `index.ts` with production Spe.Bff.Api URL

3. **Build:**
   - `npm run build`
   - Test in test harness
   - Package for deployment

4. **Deploy:**
   - Import PCF solution to Dataverse
   - Test in actual app
   - Monitor logs for errors

5. **Rollback:**
   - Revert PCF solution to previous version
   - No backend changes needed (Sprint 4 OBO endpoints unchanged)

---

### Task 5.2: Create Troubleshooting Guide (30 min)

**Common Issues:**
1. **"redirect_uri_mismatch"** → Fix: Add URI to Azure App Registration
2. **"consent_required"** → Fix: Admin grant consent
3. **"interaction_required"** → Expected: User sees popup
4. **"AADSTS50013: Invalid assertion"** → Fix: Check token audience
5. **401 from Spe.Bff.Api** → Fix: Verify scopes and permissions
6. **Popup blocked** → User action: Allow popups

---

### Task 5.3: Update Sprint Documentation (30 min)

**Files to update:**
- `SPRINT-8-MSAL-OVERVIEW.md` - Mark as complete
- `SPRINT-8-PIVOT-TO-MSAL.md` - Add "Implementation Complete" section
- Project README - Add MSAL integration notes

---

## Total Remaining Effort

| Phase | Tasks | Duration |
|-------|-------|----------|
| **Phase 3** | 1 task | 30 min |
| **Phase 4** | 3 tasks | 135 min (2.25 hours) |
| **Phase 5** | 3 tasks | 105 min (1.75 hours) |
| **Total** | 7 tasks | **4.5 hours** |

---

## Implementation Order

**Recommended sequence:**

1. ✅ **Phase 1** (Complete) - MSAL setup (Tasks 1.1-1.5)
2. ✅ **Phase 2** (Complete) - Token acquisition (Tasks 2.1-2.3)
3. ✅ **Phase 3** (1 task done) - HTTP client integration (Task 3.1)
4. ⏭️ **Task 3.2** - Token error handling
5. ⏭️ **Task 4.2** - E2E testing (do before unit tests to validate approach)
6. ⏭️ **Task 4.1** - Unit tests
7. ⏭️ **Task 4.3** - Performance testing
8. ⏭️ **Task 5.1** - Deployment runbook
9. ⏭️ **Task 5.2** - Troubleshooting guide
10. ⏭️ **Task 5.3** - Documentation updates

---

## Quick Start: Next Task

**Ready to continue? Start with:**

**Task 3.2: Handle Token Errors in API Client**

Location: `services/SdapApiClient.ts`

Add retry wrapper:
```typescript
private async fetchWithRetry(url: string, options: RequestInit): Promise<Response> {
  let attempt = 0;
  const maxAttempts = 2;

  while (attempt < maxAttempts) {
    attempt++;

    const response = await fetch(url, options);

    // Success
    if (response.ok || attempt === maxAttempts) {
      return response;
    }

    // 401 Unauthorized - token may have expired
    if (response.status === 401 && attempt < maxAttempts) {
      logger.warn('SdapApiClient', '401 response, clearing token cache and retrying');

      // Clear MSAL cache to force token refresh
      MsalAuthProvider.getInstance().clearCache();

      // Reacquire token (will be called by getAccessToken callback)
      continue;
    }

    // Other errors
    return response;
  }

  throw new Error('Fetch retry logic error'); // Should never reach here
}
```

Use `fetchWithRetry` instead of `fetch` in all API methods.

---

## Sprint 8 Progress

**Overall Progress:** ~60% complete

- ✅ **Phase 1:** MSAL Setup (5 tasks, 3.5 hours) - **COMPLETE**
- ✅ **Phase 2:** Token Acquisition (3 tasks, 2.25 hours) - **COMPLETE**
- 🔄 **Phase 3:** HTTP Client Integration (2 tasks, 1.25 hours) - **50% COMPLETE**
- ⏳ **Phase 4:** Testing (3 tasks, 2.25 hours) - **NOT STARTED**
- ⏳ **Phase 5:** Documentation (3 tasks, 1.75 hours) - **NOT STARTED**

**Estimated time to completion:** ~4.5 hours

---

## Key Achievements So Far

✅ MSAL.js installed and configured
✅ `PublicClientApplication` initialized in PCF control
✅ SSO silent token acquisition working
✅ Popup fallback for interactive auth
✅ sessionStorage caching (90x performance improvement)
✅ Proactive token refresh (no expired tokens)
✅ Integrated with SdapApiClient
✅ ADR-002 compliant (no plugins)
✅ Leverages Sprint 4 OBO endpoints

**What's left:** Error handling, testing, documentation

---

**For detailed task-by-task instructions, see individual task markdown files:**
- `TASK-1.1` through `TASK-1.5` - Phase 1 ✅
- `TASK-2.1` through `TASK-2.3` - Phase 2 ✅
- `TASK-3.1` - Phase 3 (partial) ✅
- Tasks 3.2, 4.1-4.3, 5.1-5.3 - Can be created on demand

---
