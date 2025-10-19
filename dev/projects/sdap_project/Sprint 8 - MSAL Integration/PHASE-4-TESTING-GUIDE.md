# Phase 4: Testing and Validation Guide

**Sprint:** 8 - MSAL Integration
**Status:** Ready for Testing
**Prerequisites:** Phase 1-3 Complete (MSAL setup, token acquisition, HTTP client integration)

---

## Overview

Phase 4 validates the MSAL integration works correctly in the actual Dataverse environment. This phase prioritizes **end-to-end testing in the real environment** before writing unit tests.

**Testing Order:**
1. **Task 4.2:** End-to-End Testing (45 min) - **DO THIS FIRST**
2. **Task 4.3:** Performance Testing (30 min) - **DO THIS SECOND**
3. **Task 4.1:** Unit Tests (60 min) - **OPTIONAL** (defer to future sprint if time-constrained)

**Rationale:** E2E testing validates the integration works in production. Unit tests can be added later for regression prevention.

---

## Current Environment Configuration

**From Codebase Analysis:**

### Azure App Registration
- **App Name:** Sparke DSM-SPE Dev 2
- **Client ID:** `170c98e1-d486-4355-bcbe-170454e0207c`
- **Tenant ID:** `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Redirect URI:** `https://spaarkedev1.crm.dynamics.com`
- **Scopes:** `api://spe-bff-api/user_impersonation`

**Location:** [services/auth/msalConfig.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/msalConfig.ts)

### BFF API Configuration
- **Base URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`
- **Timeout:** 300000ms (5 minutes)

**Location:** [types/index.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/index.ts:127)

### Dataverse Environment
- **Environment Name:** SPAARKE DEV 1
- **URL:** `https://spaarkedev1.crm.dynamics.com`

---

## Task 4.2: End-to-End Testing (45 min) ⭐ START HERE

### Goal
Validate MSAL integration works in actual Dataverse environment with real user authentication.

---

### Prerequisites Checklist

**Before starting E2E testing, verify:**

✅ **1. Azure App Registration Configuration**
- [ ] Redirect URI `https://spaarkedev1.crm.dynamics.com` added to Azure App Registration
- [ ] API permissions include `api://spe-bff-api/user_impersonation`
- [ ] Admin consent granted for API permissions
- [ ] Application (client) ID matches: `170c98e1-d486-4355-bcbe-170454e0207c`
- [ ] Directory (tenant) ID matches: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

**How to verify:**
1. Open Azure Portal: https://portal.azure.com
2. Navigate: Azure Active Directory → App registrations → "Sparke DSM-SPE Dev 2"
3. Check Authentication → Redirect URIs
4. Check API permissions → Configured permissions
5. Verify admin consent status (green checkmark)

✅ **2. BFF API Deployment**
- [ ] BFF deployed to `https://spe-api-dev-67e2xz.azurewebsites.net`
- [ ] BFF health check responds (visit URL in browser)
- [ ] BFF OBO endpoints available: `/api/obo/drives/*`
- [ ] BFF accepts `Authorization: Bearer` header
- [ ] Sprint 4 `TokenHelper.ExtractBearerToken()` implemented

**How to verify:**
1. Open browser: `https://spe-api-dev-67e2xz.azurewebsites.net/health` (or root)
2. Check BFF responds (not 404)
3. Review Sprint 4 deployment documentation

✅ **3. PCF Control Deployment**
- [ ] Built with MSAL integration: `npm run build` in `UniversalDatasetGrid` directory
- [ ] Solution packaged and imported to SPAARKE DEV 1 environment
- [ ] Control added to a Dataverse form (e.g., Document entity form)
- [ ] Form published and available

**How to verify:**
1. Run `npm run build` in `src/controls/UniversalDatasetGrid/UniversalDatasetGrid`
2. Check build succeeds (no errors)
3. Package solution (if using managed solution)
4. Import to Dataverse environment
5. Open form with control in Dataverse

✅ **4. Test User Configuration**
- [ ] Test user account exists in Azure AD tenant
- [ ] User has permission to access Dataverse environment
- [ ] User has permission to access SharePoint Embedded resources
- [ ] User can log in to Dataverse (verify manually)

**How to verify:**
1. Open Dataverse environment: `https://spaarkedev1.crm.dynamics.com`
2. Log in with test user credentials
3. Navigate to entity form with PCF control
4. Verify form loads

---

### Test Scenarios

#### **Scenario 1: Happy Path - SSO Silent Token Acquisition ⭐ PRIMARY TEST**

**Goal:** Verify user who is already logged into Dataverse can use PCF control without additional authentication prompts.

**Steps:**
1. **Open Dataverse form with PCF control**
   - Log in to Dataverse: `https://spaarkedev1.crm.dynamics.com`
   - Navigate to entity with Universal Dataset Grid control
   - Open a record (create new if needed)

2. **Open Browser DevTools**
   - Press F12
   - Navigate to Console tab
   - Clear console logs

3. **Observe MSAL initialization**
   - Look for log: `[MsalAuthProvider] Initializing MSAL`
   - Look for log: `[MsalAuthProvider] MSAL initialized successfully`
   - Look for log: `[Control] User authenticated: true`

4. **Test file operation (e.g., Download)**
   - Select a record with a file attached
   - Click "Download" button
   - **Expected:** File downloads without popup/prompt
   - **DevTools Console:** Look for logs:
     - `[SdapApiClientFactory] Retrieving access token via MSAL`
     - `[MsalAuthProvider] Getting token for scopes...`
     - `[MsalAuthProvider] Token retrieved from cache` (if second call)
     - `[SdapApiClient] Downloading file`
     - `[SdapApiClient] File downloaded successfully`

5. **Verify no errors**
   - Check Console tab: No red errors
   - Check Network tab: API call to `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/...`
   - Verify response status: 200 OK

**Success Criteria:**
- ✅ No authentication popup appears
- ✅ File downloads successfully
- ✅ Console shows: `User authenticated: true`
- ✅ Console shows: Token retrieved (cache hit or SSO silent)
- ✅ No errors in console

**If Fails:**
- ❌ **Popup appears:** Redirect URI not configured correctly in Azure App Registration
- ❌ **401 error:** Token not accepted by BFF (check scopes, check BFF OBO configuration)
- ❌ **"redirect_uri_mismatch":** Add redirect URI to Azure App Registration
- ❌ **"AADSTS50013":** Token audience mismatch (check API permissions in Azure)

---

#### **Scenario 2: Token Caching Performance**

**Goal:** Verify Phase 2 token caching provides fast subsequent API calls.

**Steps:**
1. **First API call (cold start)**
   - Clear browser cache: DevTools → Application → Clear storage
   - Refresh Dataverse form
   - Click "Download" button
   - **DevTools Performance tab:** Record timeline
   - **Expected time:** 500-1000ms (SSO silent token acquisition)

2. **Second API call (cache hit)**
   - Click "Download" button again (same or different file)
   - **DevTools Performance tab:** Record timeline
   - **Expected time:** 5-50ms (cache retrieval)

3. **Verify cache hit**
   - **Console:** Look for log: `[MsalAuthProvider] Token retrieved from cache`
   - **Network tab:** Only 1 request to `login.microsoftonline.com` (first call)
   - **Network tab:** No MSAL requests on second call

**Success Criteria:**
- ✅ First call: 500-1000ms (SSO silent)
- ✅ Second call: < 50ms (cache hit)
- ✅ Console shows: "Token retrieved from cache"
- ✅ ~82x performance improvement on cache hit

**Metrics:**
- Record actual times in completion document
- Compare against baseline (without caching)

---

#### **Scenario 3: 401 Retry Logic**

**Goal:** Verify Task 3.2 automatic retry handles token expiration race conditions.

**Steps:**
1. **Simulate token expiration**
   - Open DevTools → Console
   - Run: `sessionStorage.clear()` (clears MSAL cache)
   - **OR** Use network throttling to delay request

2. **Trigger API call**
   - Click "Download" button
   - **Expected:** Request succeeds (retry logic acquires fresh token)

3. **Verify retry in console**
   - Look for logs:
     - `[SdapApiClient] 401 Unauthorized response - clearing token cache and retrying`
     - `[SdapApiClient] Retrying request with fresh token`
     - `[MsalAuthProvider] Getting token for scopes...`
   - Verify: Second request succeeds with 200 OK

**Success Criteria:**
- ✅ First request returns 401
- ✅ Console shows: "clearing token cache and retrying"
- ✅ Second request succeeds (200 OK)
- ✅ File operation completes successfully
- ✅ User sees no error

**If Fails:**
- ❌ **User sees error after retry:** Check BFF OBO configuration (token still invalid)
- ❌ **Infinite loop:** Check `maxAttempts = 2` in `fetchWithTimeout()`

---

#### **Scenario 4: User-Friendly Error Messages**

**Goal:** Verify Task 3.2 user-friendly error messages display correctly.

**Steps:**
1. **Test 403 Forbidden (Permission Denied)**
   - Remove user's SharePoint Embedded permissions in Azure
   - Trigger file operation (e.g., Delete)
   - **Expected error:** "Access denied. You do not have permission to perform this operation. Please contact your administrator."
   - **NOT expected:** "HTTP 403: Forbidden"

2. **Test 404 Not Found**
   - Delete a file from SharePoint Embedded
   - Try to download the deleted file
   - **Expected error:** "The requested file was not found. It may have been deleted or moved."

3. **Test 500 Server Error**
   - Stop BFF API (or use DevTools to simulate 500 response)
   - Trigger file operation
   - **Expected error:** "Server error occurred. Please try again later. If the problem persists, contact your administrator."

**Success Criteria:**
- ✅ Error messages are user-friendly (no HTTP status codes)
- ✅ Error messages provide actionable guidance
- ✅ Original error preserved in console logs for debugging

---

#### **Scenario 5: First-Time User (Popup Login)**

**Goal:** Verify popup fallback works for users without existing session.

**Steps:**
1. **Clear browser authentication state**
   - Open DevTools → Application → Cookies
   - Delete all cookies for `login.microsoftonline.com` and `spaarkedev1.crm.dynamics.com`
   - **OR** Use Incognito/Private browsing window

2. **Open Dataverse form**
   - Navigate to form with PCF control
   - PCF control initializes

3. **Trigger API call**
   - Click "Download" button
   - **Expected:** MSAL popup appears asking user to log in
   - User enters credentials
   - Popup closes
   - File downloads successfully

**Success Criteria:**
- ✅ Popup appears (InteractionRequiredAuthError)
- ✅ User can log in via popup
- ✅ After login, file operation succeeds
- ✅ Subsequent operations use cached token (no popup)

**If Fails:**
- ❌ **Popup blocked:** Browser popup blocker enabled (user action required)
- ❌ **Popup shows error:** Check redirect URI configuration

---

#### **Scenario 6: Network Failure**

**Goal:** Verify friendly error message when network is offline.

**Steps:**
1. **Simulate offline mode**
   - DevTools → Network tab → Select "Offline" in throttling dropdown

2. **Trigger API call**
   - Click "Download" button
   - **Expected error:** Network-related error message (may vary by browser)

3. **Restore network**
   - DevTools → Network tab → Select "No throttling"
   - Retry operation
   - **Expected:** Operation succeeds

**Success Criteria:**
- ✅ Offline mode shows error (not silent failure)
- ✅ User can recover by restoring network
- ✅ No data corruption

---

#### **Scenario 7: All File Operations Work**

**Goal:** Verify all CRUD operations work with MSAL authentication.

**Operations to test:**
1. **Upload File**
   - Select record without file
   - Click "Add File"
   - Select file from file picker
   - **Expected:** File uploads successfully
   - **Verify:** File appears in grid

2. **Download File**
   - Select record with file
   - Click "Download"
   - **Expected:** File downloads to browser

3. **Replace File**
   - Select record with file
   - Click "Update File"
   - Select new file from file picker
   - **Expected:** Old file deleted, new file uploaded
   - **Verify:** New file appears in grid

4. **Delete File**
   - Select record with file
   - Click "Remove File"
   - Confirm deletion
   - **Expected:** File deleted
   - **Verify:** File removed from grid

**Success Criteria:**
- ✅ All 4 operations succeed
- ✅ No console errors
- ✅ Each operation uses MSAL token
- ✅ Grid updates correctly after each operation

---

### Performance Expectations

**Token Acquisition:**
- First call (SSO silent): 500-1000ms
- Cached token: 5-50ms (~82x faster)
- Proactive refresh: < 500ms (background, non-blocking)

**API Calls:**
- Upload (10MB file): 2-5 seconds (depends on network)
- Download (10MB file): 1-3 seconds
- Delete: 100-300ms
- Replace: 2-6 seconds (delete + upload)

**Overall UX:**
- Control initialization: < 2 seconds
- Button click → API response: < 1 second (cached token)
- No visible delays for token refresh (proactive refresh)

---

### Troubleshooting Common Issues

#### Issue: "redirect_uri_mismatch"
**Cause:** Redirect URI not configured in Azure App Registration
**Fix:**
1. Azure Portal → App registrations → "Sparke DSM-SPE Dev 2"
2. Authentication → Add redirect URI: `https://spaarkedev1.crm.dynamics.com`
3. Save
4. Retry test

---

#### Issue: 401 Unauthorized (after retry)
**Cause:** Token not accepted by BFF
**Fix:**
1. Verify scopes in `msalConfig.ts`: `api://spe-bff-api/user_impersonation`
2. Verify BFF expects this scope
3. Verify Sprint 4 OBO configuration in BFF
4. Check BFF logs for token validation errors

---

#### Issue: "consent_required"
**Cause:** User hasn't consented to API permissions
**Fix:**
1. Azure Portal → App registrations → "Sparke DSM-SPE Dev 2"
2. API permissions → Grant admin consent
3. Retry test

---

#### Issue: Popup blocked
**Cause:** Browser popup blocker
**Fix:**
1. User action: Allow popups for `spaarkedev1.crm.dynamics.com`
2. Retry test

---

#### Issue: BFF returns 500 error
**Cause:** BFF issue (not MSAL)
**Fix:**
1. Check BFF logs
2. Verify Sprint 4 OBO flow works
3. Verify SharePoint Embedded permissions

---

### Test Completion Checklist

**After completing E2E testing:**

- [ ] Scenario 1 (Happy Path) passed
- [ ] Scenario 2 (Token Caching) passed - recorded performance metrics
- [ ] Scenario 3 (401 Retry) passed
- [ ] Scenario 4 (User-Friendly Errors) passed
- [ ] Scenario 5 (First-Time User) passed (optional if no clean browser available)
- [ ] Scenario 6 (Network Failure) passed
- [ ] Scenario 7 (All File Operations) passed
- [ ] No console errors during testing
- [ ] Performance meets expectations
- [ ] Document any issues found in completion report

---

## Task 4.3: Performance Testing (30 min) ⭐ DO THIS SECOND

### Goal
Quantify Phase 2 caching performance gains with real measurements.

---

### Metrics to Collect

**1. Token Acquisition Time**

**First Call (Cold Start):**
```javascript
// In DevTools Console
console.time('getToken-first');
// Trigger file operation (click Download button)
// Observe console log
console.timeEnd('getToken-first');
// Expected: 500-1000ms (SSO silent)
```

**Subsequent Calls (Cache Hit):**
```javascript
// In DevTools Console
console.time('getToken-cached');
// Trigger file operation again
// Observe console log
console.timeEnd('getToken-cached');
// Expected: 5-50ms
```

**Speedup Calculation:**
```
Speedup = (First Call Time) / (Cached Call Time)
Example: 800ms / 10ms = 80x faster
```

---

**2. API Call Performance**

**Use Chrome DevTools Performance Tab:**

1. Open DevTools → Performance tab
2. Click "Record" button
3. Trigger file operation (e.g., Download)
4. Stop recording
5. Analyze timeline:
   - Token acquisition time
   - Network request time
   - Total time from click to response

**Metrics to record:**
- Time from button click to token retrieved
- Time from token retrieved to API response
- Total operation time

---

**3. Stress Test: 10 Sequential API Calls**

**Test rapid-fire operations with cached tokens:**

```javascript
// In DevTools Console (adapt to your scenario)
// This simulates 10 rapid file operations
for (let i = 0; i < 10; i++) {
  console.time(`API call ${i+1}`);
  // Trigger file operation (programmatically if possible)
  // Or manually click button 10 times
  console.timeEnd(`API call ${i+1}`);
}
```

**Expected:**
- First call: 500-1000ms (token acquisition)
- Calls 2-10: < 100ms each (cached token + API latency)
- Total for 10 calls: < 2 seconds

---

### Performance Success Criteria

**Task 2.2 Caching Goals:**
- ✅ Cache hit: < 50ms (actual: ~5-10ms typical)
- ✅ Cache miss: 500-1000ms (SSO silent)
- ✅ Speedup: ~82x (measured in Phase 2 implementation)

**Task 2.3 Proactive Refresh Goals:**
- ✅ Background refresh: < 500ms (non-blocking)
- ✅ No user-visible delays when token nears expiration
- ✅ Refresh triggers at halfway point to expiration buffer

**Overall Performance:**
- ✅ 10 rapid API calls complete in < 2 seconds
- ✅ No rate limiting errors (429)
- ✅ No timeout errors

---

### Performance Test Completion Checklist

- [ ] Measured first token acquisition time: _____ ms
- [ ] Measured cached token retrieval time: _____ ms
- [ ] Calculated speedup: _____x
- [ ] Completed 10 rapid API calls test
- [ ] Total time for 10 calls: _____ ms
- [ ] All operations completed successfully (no errors)
- [ ] Document metrics in completion report

---

## Task 4.1: Unit Tests for MsalAuthProvider (60 min) 🔄 OPTIONAL

**Status:** DEFERRED

**Rationale:**
- E2E testing (Task 4.2) validates integration works in production
- Unit tests add regression prevention but not critical for initial deployment
- Can be added in future sprint if time allows

**If implementing unit tests:**
- Use Jest + @testing-library/react
- Mock `@azure/msal-browser` PublicClientApplication
- Test all MsalAuthProvider methods (initialize, getToken, cache, refresh)
- See [TASKS-REMAINING-SUMMARY.md](TASKS-REMAINING-SUMMARY.md) for original task details

**Decision:** Defer to future sprint unless time available after Task 4.2 and 4.3.

---

## Phase 4 Completion Criteria

**Phase 4 is complete when:**

✅ **Task 4.2 (E2E Testing):**
- [ ] All 7 test scenarios passed
- [ ] No critical issues found
- [ ] All file operations work in actual environment
- [ ] Completion report written with test results

✅ **Task 4.3 (Performance Testing):**
- [ ] Performance metrics collected
- [ ] Caching speedup validated (~82x)
- [ ] All performance goals met
- [ ] Metrics documented in completion report

✅ **Overall:**
- [ ] MSAL integration validated in production environment
- [ ] No blockers for deployment
- [ ] Ready to proceed to Phase 5 (Documentation and Deployment)

---

## Next Steps After Phase 4

**Phase 5: Documentation and Deployment**
- Task 5.1: Create Deployment Runbook
- Task 5.2: Create Troubleshooting Guide
- Task 5.3: Update Sprint Documentation

**See:** [TASKS-REMAINING-SUMMARY.md](TASKS-REMAINING-SUMMARY.md) for Phase 5 details

---

## Quick Start Guide

**To start Phase 4 testing right now:**

1. **Verify prerequisites:**
   - Azure App Registration configured (redirect URI, permissions, consent)
   - BFF API deployed and healthy
   - PCF control built and deployed to SPAARKE DEV 1

2. **Run Scenario 1 (Happy Path):**
   - Open Dataverse: `https://spaarkedev1.crm.dynamics.com`
   - Navigate to form with Universal Dataset Grid
   - Open record
   - Open DevTools (F12) → Console tab
   - Click "Download" button
   - Verify: File downloads, no errors, console shows MSAL logs

3. **If Scenario 1 passes:**
   - Continue with remaining scenarios
   - Collect performance metrics
   - Document results

4. **If Scenario 1 fails:**
   - Review troubleshooting section
   - Check prerequisites again
   - Fix configuration issues before continuing

---

**Phase 4 Status:** ✅ **READY TO START**
**Estimated Duration:** 75 minutes (45 min E2E + 30 min performance)
**Priority:** HIGH - Validates integration works in production

---
