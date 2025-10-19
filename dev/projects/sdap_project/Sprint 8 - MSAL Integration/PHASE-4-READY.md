# Phase 4 Testing - Ready to Start

**Date:** 2025-10-06
**Sprint:** 8 - MSAL Integration
**Status:** ✅ **READY FOR TESTING**

---

## What's Been Completed

**Phase 1-3 Implementation:** ✅ **COMPLETE**
- ✅ Phase 1: MSAL setup and configuration (Tasks 1.1-1.5)
- ✅ Phase 2: Token acquisition with caching and proactive refresh (Tasks 2.1-2.3)
- ✅ Phase 3: HTTP client integration and error handling (Tasks 3.1-3.2)

**Code Implementation:** ✅ **COMPLETE**
- ✅ MSAL.js v4.24.1 installed and configured
- ✅ MsalAuthProvider singleton with token caching (~82x speedup)
- ✅ Proactive token refresh (eliminates expiration delays)
- ✅ SdapApiClient integrated with MSAL authentication
- ✅ Automatic 401 retry logic (token expiration handling)
- ✅ User-friendly error messages (403, 404, 500, etc.)
- ✅ All builds passing (no TypeScript errors)

---

## Testing Documents Created

**Primary Testing Guide:**
- [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md) - Comprehensive E2E and performance testing instructions

**Pre-Deployment Preparation:**
- [PRE-DEPLOYMENT-CHECKLIST.md](PRE-DEPLOYMENT-CHECKLIST.md) - Environment verification before testing

---

## Your Current Environment Configuration

**Verified from Codebase:**

### Azure App Registration
- **App Name:** Sparke DSM-SPE Dev 2
- **Client ID:** `170c98e1-d486-4355-bcbe-170454e0207c`
- **Tenant ID:** `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Redirect URI:** `https://spaarkedev1.crm.dynamics.com` (added in Phase 1)
- **Scopes:** `api://spe-bff-api/user_impersonation`

### BFF API
- **Base URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`
- **Endpoints:** `/api/obo/drives/*`
- **Timeout:** 300000ms (5 minutes)

### Dataverse Environment
- **Environment:** SPAARKE DEV 1
- **URL:** `https://spaarkedev1.crm.dynamics.com`

---

## Recommended Testing Order

### Step 1: Pre-Deployment Verification (15 min)

**Open:** [PRE-DEPLOYMENT-CHECKLIST.md](PRE-DEPLOYMENT-CHECKLIST.md)

**Complete the 10-item checklist:**
1. Verify Azure App Registration (redirect URI, permissions, consent)
2. Check BFF API deployment and health
3. Build PCF control with MSAL integration
4. Verify Dataverse environment access
5. Package and deploy solution
6. Configure form with control
7. Verify test user permissions
8. Configure browser for testing
9. Run pre-flight smoke test
10. Final checklist review

**Expected Time:** 15 minutes (if all prerequisites already configured)

---

### Step 2: End-to-End Testing (45 min) ⭐ PRIMARY TEST

**Open:** [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md) → Task 4.2

**Test 7 scenarios in actual Dataverse environment:**

1. **Scenario 1: Happy Path (SSO Silent)** - ⭐ Most Important
   - Verify user already logged into Dataverse can use control without popup
   - Verify token acquisition from MSAL
   - Verify file operation succeeds
   - **Expected:** No errors, file downloads

2. **Scenario 2: Token Caching Performance**
   - Measure first token acquisition (500-1000ms)
   - Measure cached token retrieval (5-50ms)
   - **Expected:** ~82x speedup on cache hit

3. **Scenario 3: 401 Retry Logic**
   - Simulate token expiration
   - Verify automatic retry with fresh token
   - **Expected:** Transparent recovery, no user error

4. **Scenario 4: User-Friendly Error Messages**
   - Test 403 (permission denied)
   - Test 404 (file not found)
   - Test 500 (server error)
   - **Expected:** Friendly messages, not "HTTP 403"

5. **Scenario 5: First-Time User (Popup Login)**
   - Test with clean browser session
   - Verify popup appears for authentication
   - **Expected:** User logs in, popup closes, operation succeeds

6. **Scenario 6: Network Failure**
   - Simulate offline mode
   - Verify error handling
   - **Expected:** Friendly error message

7. **Scenario 7: All File Operations**
   - Test Upload, Download, Replace, Delete
   - Verify all operations use MSAL token
   - **Expected:** All operations succeed

---

### Step 3: Performance Testing (30 min)

**Open:** [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md) → Task 4.3

**Collect performance metrics:**
- First token acquisition time (cold start)
- Cached token retrieval time (cache hit)
- Calculate speedup ratio
- Test 10 rapid API calls
- Document all metrics

**Expected Results:**
- First call: 500-1000ms
- Cached call: 5-50ms
- Speedup: ~82x
- 10 calls total: < 2 seconds

---

### Step 4: Document Results (15 min)

**Create completion report:**
- Test results for all 7 scenarios
- Performance metrics collected
- Any issues found and resolutions
- Recommendations for deployment

**Template:** See end of this document

---

## Quick Start (If Prerequisites Already Met)

**If you've already:**
- ✅ Verified Azure App Registration
- ✅ Confirmed BFF is deployed and healthy
- ✅ Built PCF control successfully
- ✅ Deployed control to SPAARKE DEV 1
- ✅ Configured form with control

**Then you can start testing immediately:**

1. **Open Dataverse:** https://spaarkedev1.crm.dynamics.com
2. **Navigate to form** with Universal Dataset Grid control
3. **Open record** (create new if needed)
4. **Press F12** (open DevTools)
5. **Go to Console tab** (clear logs)
6. **Click "Download" button** (or any file operation)
7. **Check console for logs:**
   - `[MsalAuthProvider] MSAL initialized successfully`
   - `[Control] User authenticated: true`
   - `[SdapApiClient] Downloading file`
   - `[SdapApiClient] File downloaded successfully`
8. **Verify:** File downloads with no errors

**If this works:** ✅ Proceed with full test scenarios

**If this fails:** ❌ Complete [PRE-DEPLOYMENT-CHECKLIST.md](PRE-DEPLOYMENT-CHECKLIST.md) first

---

## What to Look For During Testing

### Success Indicators ✅

**Console Logs (DevTools → Console):**
- `[MsalAuthProvider] Initializing MSAL...`
- `[MsalAuthProvider] MSAL initialized successfully`
- `[Control] User authenticated: true`
- `[MsalAuthProvider] Getting token for scopes: api://spe-bff-api/user_impersonation`
- `[MsalAuthProvider] Token retrieved from cache` (on subsequent calls)
- `[SdapApiClient] File [operation] successfully`

**Network Tab (DevTools → Network):**
- Requests to `login.microsoftonline.com` (MSAL authentication)
- Requests to `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/*`
- Request headers include: `Authorization: Bearer <token>`
- Response status: 200 OK

**User Experience:**
- No authentication popups (for logged-in users)
- File operations complete quickly
- No error messages (unless expected, e.g., 404 for deleted file)
- Grid updates correctly after operations

---

### Failure Indicators ❌

**Console Errors:**
- `redirect_uri_mismatch` → Fix: Add redirect URI to Azure App Registration
- `consent_required` → Fix: Grant admin consent for API permissions
- `AADSTS50013` → Fix: Check token audience and API permissions
- `401 Unauthorized` (after retry) → Fix: Check BFF OBO configuration
- `Failed to retrieve user access token via MSAL` → Fix: Check MSAL configuration

**Network Errors:**
- 401 response from BFF → Token not accepted (check scopes)
- 403 response → User lacks permissions
- 404 response → File not found (expected in some tests)
- 500 response from BFF → BFF error (check BFF logs)
- CORS errors → BFF CORS configuration issue

**User Experience:**
- Popup appears for logged-in user → SSO silent failed (check Azure config)
- Operations timeout → Network issue or BFF down
- Errors show "HTTP 403" → User-friendly error messages not working

---

## Testing Tools and Resources

**Required:**
- Browser: Microsoft Edge or Google Chrome (latest)
- DevTools: Press F12
- Access to: https://spaarkedev1.crm.dynamics.com
- Test user credentials

**Optional but Helpful:**
- Postman (for BFF API testing)
- Azure Portal access (for debugging Azure App Registration)
- BFF logs access (for debugging server errors)

**Documentation:**
- [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md) - Full testing instructions
- [PRE-DEPLOYMENT-CHECKLIST.md](PRE-DEPLOYMENT-CHECKLIST.md) - Environment verification
- [TASKS-REMAINING-SUMMARY.md](TASKS-REMAINING-SUMMARY.md) - Overall Sprint 8 progress

---

## Expected Timeline

**Total Time:** ~2 hours (including setup)

| Activity | Duration | Cumulative |
|----------|----------|------------|
| **Pre-Deployment Checklist** | 15 min | 15 min |
| **Scenario 1 (Happy Path)** | 10 min | 25 min |
| **Scenarios 2-7** | 35 min | 60 min |
| **Performance Testing** | 30 min | 90 min |
| **Document Results** | 15 min | 105 min |
| **Buffer for Issues** | 15 min | 120 min |

**If Prerequisites Already Met:** ~75 minutes (skip pre-deployment checklist)

---

## Phase 4 Completion Criteria

**Phase 4 is complete when:**

✅ **All 7 E2E scenarios pass**
- Happy path (SSO silent) works
- Token caching provides performance boost
- 401 retry handles token expiration
- Error messages are user-friendly
- First-time user popup works
- Network failures handled gracefully
- All file operations (upload/download/delete/replace) work

✅ **Performance metrics collected**
- First token acquisition: _____ ms
- Cached token: _____ ms
- Speedup: _____x
- 10 rapid calls: _____ ms total

✅ **No critical issues found**
- MSAL integration works in production
- No blockers for deployment
- User experience acceptable

✅ **Completion report written**
- Test results documented
- Performance metrics recorded
- Issues and resolutions noted
- Recommendations for deployment

---

## Test Results Template

**Copy this template when documenting results:**

```markdown
# Phase 4 Testing - Completion Report

**Date:** 2025-10-06
**Tester:** [Your Name]
**Environment:** SPAARKE DEV 1

---

## Test Results Summary

**Overall Status:** [PASS / FAIL / PARTIAL]

**Scenarios Tested:** 7/7

---

## Scenario Results

### Scenario 1: Happy Path (SSO Silent)
- **Status:** [PASS / FAIL]
- **Notes:** [Observations]
- **Screenshots:** [If applicable]

### Scenario 2: Token Caching Performance
- **Status:** [PASS / FAIL]
- **First call time:** _____ ms
- **Cached call time:** _____ ms
- **Speedup:** _____x
- **Notes:** [Observations]

### Scenario 3: 401 Retry Logic
- **Status:** [PASS / FAIL]
- **Notes:** [Did retry work? Did operation succeed?]

### Scenario 4: User-Friendly Error Messages
- **Status:** [PASS / FAIL]
- **403 message:** [Copy message shown to user]
- **404 message:** [Copy message shown to user]
- **500 message:** [Copy message shown to user]

### Scenario 5: First-Time User (Popup)
- **Status:** [PASS / FAIL / SKIPPED]
- **Notes:** [Did popup appear? Did login work?]

### Scenario 6: Network Failure
- **Status:** [PASS / FAIL]
- **Error message:** [Copy message shown]

### Scenario 7: All File Operations
- **Upload:** [PASS / FAIL]
- **Download:** [PASS / FAIL]
- **Replace:** [PASS / FAIL]
- **Delete:** [PASS / FAIL]

---

## Performance Metrics

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| First token call | 500-1000ms | _____ ms | [PASS/FAIL] |
| Cached token call | < 50ms | _____ ms | [PASS/FAIL] |
| Speedup ratio | ~82x | _____x | [PASS/FAIL] |
| 10 rapid calls | < 2s | _____ ms | [PASS/FAIL] |

---

## Issues Found

**Critical Issues:** [Count]
1. [Description, steps to reproduce, resolution]

**Minor Issues:** [Count]
1. [Description]

**No Issues:** ✅

---

## Recommendations

**Ready for Deployment:** [YES / NO / CONDITIONAL]

**Conditions for deployment:**
- [Any prerequisites or fixes needed]

**Next Steps:**
- [Recommended actions]

---

## Conclusion

[Summary of testing, overall assessment, confidence in deployment]

---
```

---

## Next Steps After Phase 4

**If Phase 4 passes:**
- ✅ Proceed to Phase 5 (Documentation and Deployment)
  - Task 5.1: Create Deployment Runbook
  - Task 5.2: Create Troubleshooting Guide
  - Task 5.3: Update Sprint Documentation

**If Phase 4 has issues:**
- ❌ Address critical issues first
- ❌ Retest failed scenarios
- ❌ Do not proceed to Phase 5 until critical issues resolved

---

## Support and Assistance

**If you encounter issues during testing:**

1. **Check troubleshooting section:**
   - [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md) → "Troubleshooting Common Issues"

2. **Review configuration:**
   - [PRE-DEPLOYMENT-CHECKLIST.md](PRE-DEPLOYMENT-CHECKLIST.md)

3. **Check Azure App Registration:**
   - Redirect URI configured correctly
   - API permissions granted with admin consent

4. **Check BFF deployment:**
   - BFF responding to health checks
   - Sprint 4 OBO implementation complete

5. **Review console logs:**
   - Press F12 → Console tab
   - Look for error messages with context

---

## Ready to Start?

**Choose your path:**

1. **If prerequisites need verification:**
   - 📋 Start with: [PRE-DEPLOYMENT-CHECKLIST.md](PRE-DEPLOYMENT-CHECKLIST.md)
   - ⏱️ Time: 15 minutes

2. **If prerequisites already verified:**
   - 🧪 Jump to: [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md) → Task 4.2
   - ⏱️ Time: 45 minutes

3. **If doing quick validation first:**
   - ⚡ Quick test: Open Dataverse → Open form → Click Download → Check console
   - ⏱️ Time: 2 minutes

---

**Phase 4 Status:** ✅ **READY TO START**
**Code Status:** ✅ **ALL IMPLEMENTATIONS COMPLETE**
**Documentation Status:** ✅ **TESTING GUIDES READY**

**Let's validate this works in your actual environment!** 🚀

---
