# CRITICAL: Old Code Deployed to Dataverse

**Date:** 2025-10-06
**Issue:** Dataverse environment has OLD code (before MSAL integration)
**Status:** 🚨 **REQUIRES REBUILD AND REDEPLOY**

---

## Issue Analysis

### Error Messages from Console

```
[SdapApiClientFactory] No Authorization header found in WhoAmI response
[SdapApiClientFactory] Failed to retrieve access token
Error: Unable to retrieve access token from PCF context
```

### What This Tells Us

**These error messages and methods are from the OLD code:**

| Error/Method | Source | Status |
|--------------|--------|--------|
| `getTokenViaClientAuth` | OLD SdapApiClientFactory.ts (before Task 3.1) | ❌ Should not exist |
| `getTokenFromContext` | OLD SdapApiClientFactory.ts (before Task 3.1) | ❌ Should not exist |
| `WhoAmI response` | OLD fallback method #4 | ❌ Should not exist |
| `Unable to retrieve access token from PCF context` | OLD error message | ❌ Should not exist |

**Expected MSAL messages (not present):**

| Expected Message | Source | Status |
|-----------------|--------|--------|
| `Retrieving access token via MSAL` | NEW SdapApiClientFactory.ts (Task 3.1) | ❌ Not present |
| `MsalAuthProvider.getInstance()` | NEW factory integration | ❌ Not present |
| `Getting token for scopes: api://spe-bff-api/user_impersonation` | MsalAuthProvider.ts | ❌ Not present |

---

## Root Cause

**The PCF control deployed to SPAARKE DEV 1 was built BEFORE:**
- Task 3.1: Integrate MSAL with SdapApiClient (completed today)
- Task 3.2: Handle Token Errors (completed today)

**Current deployed version:**
- Uses OLD `SdapApiClientFactory` with 4 PCF context fallback methods
- Does NOT use `MsalAuthProvider.getToken()`
- Does NOT have MSAL integration

**Latest code (in repository):**
- Uses NEW `SdapApiClientFactory` with MSAL integration
- Calls `MsalAuthProvider.getInstance().getToken()`
- Has Task 3.1 and 3.2 implementations

---

## Solution: Rebuild and Redeploy

### Step 1: Clean Build with MSAL Integration

```bash
# Navigate to PCF control directory
cd "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid"

# Clean previous build
# (if clean script exists, otherwise skip this)
# npm run clean

# Rebuild with latest code
npm run build
```

**Expected output:**
```
[4:XX:XX PM] [build] Succeeded
webpack 5.102.0 compiled successfully
```

**Verify bundle includes MSAL:**
```bash
# Check bundle size (should be ~9.75 MiB with MSAL)
ls -lh out/controls/UniversalDatasetGrid/bundle.js

# Check for MSAL in bundle (should find matches)
grep -i "MsalAuthProvider" out/controls/UniversalDatasetGrid/bundle.js
grep -i "api://spe-bff-api" out/controls/UniversalDatasetGrid/bundle.js
```

---

### Step 2: Deploy Updated Control to Dataverse

**Option A: PAC CLI Push (Fastest for Development)**

```bash
# Ensure authenticated to correct environment
pac auth list

# If not authenticated or wrong environment, create new auth
pac auth create --url https://spaarkedev1.crm.dynamics.com

# Push updated control (increments version automatically)
cd "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid"
pac pcf push --publisher-prefix spk
```

**Expected output:**
```
Connected as [your-email]@spaarke.com
Pushing PCF control...
Control pushed successfully
Version: [incremented version number]
```

---

**Option B: Solution Import (More Reliable)**

```bash
# Package solution
cd "c:\code_files\spaarke\src\controls\UniversalDatasetGrid"

# Create managed solution
pac solution pack \
  --zipfile "../../solutions/UniversalDatasetGrid_managed_$(date +%Y%m%d_%H%M%S).zip" \
  --managed

# OR create unmanaged solution (for dev/test)
pac solution pack \
  --zipfile "../../solutions/UniversalDatasetGrid_unmanaged_$(date +%Y%m%d_%H%M%S).zip"
```

**Then:**
1. Open Dataverse: https://spaarkedev1.crm.dynamics.com
2. Navigate to: Settings → Solutions
3. Click "Import"
4. Select the .zip file created above
5. Follow import wizard
6. Wait for import to complete
7. Click "Publish All Customizations"

---

### Step 3: Clear Browser Cache

**Important:** Browser may have cached old bundle.js

```
Method 1: Hard Refresh
- Press Ctrl+Shift+R (Windows)
- Or Cmd+Shift+R (Mac)

Method 2: Clear Cache in DevTools
- Press F12
- Right-click refresh button → "Empty Cache and Hard Reload"

Method 3: Clear All Dataverse Cache
- Press F12 → Application tab
- Clear Storage → Clear site data
- Close and reopen browser
```

---

### Step 4: Verify New Code Deployed

**Open form with control and check console:**

**Expected NEW logs (MSAL integrated):**
```
[UniversalDatasetGrid][Control] Init complete
[UniversalDatasetGrid][MsalAuthProvider] Initializing MSAL...
[UniversalDatasetGrid][MsalAuthProvider] MSAL initialized successfully
[UniversalDatasetGrid][Control] User authenticated: true

// When clicking Download:
[UniversalDatasetGrid][SdapApiClientFactory] Creating SDAP API client with MSAL auth
[UniversalDatasetGrid][SdapApiClientFactory] Retrieving access token via MSAL
[UniversalDatasetGrid][MsalAuthProvider] Getting token for scopes: api://spe-bff-api/user_impersonation
[UniversalDatasetGrid][MsalAuthProvider] Attempting SSO silent token acquisition
[UniversalDatasetGrid][MsalAuthProvider] Token acquired successfully
```

**Should NOT see OLD logs:**
```
❌ getTokenFromContext
❌ getTokenViaClientAuth
❌ WhoAmI response
❌ Unable to retrieve access token from PCF context
```

---

### Step 5: Retry Download Test

**After redeployment:**

1. **Refresh form:**
   - Press F12 → Clear console
   - Press Ctrl+Shift+R (hard refresh)

2. **Select record with filename:**
   - Record: `fb67a728-3a9e-f011-bbd3-7c1e5215b8b5`
   - Filename: `test-document.pdf` (you populated this)

3. **Click Download button**

4. **Watch console for MSAL logs:**
   - Should see MSAL initialization
   - Should see token acquisition
   - Should see API call to BFF

5. **Check Network tab:**
   - Request to `login.microsoftonline.com` (MSAL)
   - Request to `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/...`
   - Authorization header: `Bearer <token>`

---

## Verification Checklist

**Before retrying test:**

- [ ] Rebuilt control with latest code (`npm run build`)
- [ ] Verified bundle includes MSAL (grep check)
- [ ] Deployed to Dataverse (pac pcf push OR solution import)
- [ ] Published all customizations
- [ ] Cleared browser cache (hard refresh)
- [ ] Opened fresh browser tab with form
- [ ] Console shows MSAL initialization logs

**If any item unchecked:** Complete it before retrying test.

---

## Why This Happened

**Timeline of events:**

1. **Earlier:** PCF control built and deployed (before Sprint 8)
   - Used old SdapApiClientFactory with PCF context methods

2. **Today:** Implemented Sprint 8 Tasks 3.1 and 3.2
   - Updated SdapApiClientFactory to use MSAL
   - Built successfully (local build)
   - Did NOT redeploy to Dataverse

3. **Testing:** Opened Dataverse form
   - Loaded OLD deployed version (still in Dataverse)
   - New code only in local repository

**Lesson:** Always rebuild AND redeploy after code changes.

---

## Quick Deployment Commands

**Complete rebuild and deploy:**

```bash
# 1. Navigate to control directory
cd "c:\code_files\spaarke\src\controls\UniversalDatasetGrid\UniversalDatasetGrid"

# 2. Build control
npm run build

# 3. Verify build succeeded (check output)

# 4. Deploy to Dataverse
pac pcf push --publisher-prefix spk

# 5. Verify deployment succeeded (check output)
```

**Then refresh browser and retry test.**

---

## Expected Timeline

| Step | Duration | Cumulative |
|------|----------|------------|
| **Rebuild control** | 2 min | 2 min |
| **Deploy to Dataverse** | 3 min | 5 min |
| **Clear browser cache** | 1 min | 6 min |
| **Verify deployment** | 2 min | 8 min |
| **Retry test** | 2 min | 10 min |

**Total:** ~10 minutes to rebuild, redeploy, and retest

---

## After Successful Redeployment

**Once MSAL logs appear in console:**

✅ Continue with [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md)
✅ Complete all 7 E2E scenarios
✅ Collect performance metrics
✅ Document results

---

## Issue Summary

**Problem:** Old code deployed (before MSAL integration)
**Root Cause:** Did not redeploy after Task 3.1/3.2 implementation
**Solution:** Rebuild and redeploy control
**ETA:** 10 minutes
**Next Test:** Retry download after redeployment

---

**Issue Status:** 🚨 **REQUIRES ACTION**
**Action Required:** Rebuild and redeploy PCF control
**Blocker:** Cannot test MSAL until new code deployed

---
