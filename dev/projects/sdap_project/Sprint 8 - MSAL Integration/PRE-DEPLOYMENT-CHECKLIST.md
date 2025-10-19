# Pre-Deployment Checklist - Sprint 8 MSAL Integration

**Date:** 2025-10-06
**Environment:** SPAARKE DEV 1
**Status:** Ready for Verification

---

## Purpose

This checklist ensures all prerequisites are met before deploying and testing the MSAL-integrated PCF control in the actual Dataverse environment.

---

## 1. Azure App Registration Configuration

**App Name:** Sparke DSM-SPE Dev 2

### Required Configuration

**From:** [services/auth/msalConfig.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/msalConfig.ts)

| Setting | Value | Status |
|---------|-------|--------|
| **Application (Client) ID** | `170c98e1-d486-4355-bcbe-170454e0207c` | ✅ Configured in code |
| **Directory (Tenant) ID** | `a221a95e-6abc-4434-aecc-e48338a1b2f2` | ✅ Configured in code |
| **Redirect URI** | `https://spaarkedev1.crm.dynamics.com` | ⚠️ **VERIFY IN AZURE PORTAL** |

### Verification Steps

**Azure Portal → App Registrations → "Sparke DSM-SPE Dev 2":**

1. **Overview Page:**
   - [ ] Application (client) ID matches: `170c98e1-d486-4355-bcbe-170454e0207c`
   - [ ] Directory (tenant) ID matches: `a221a95e-6abc-4434-aecc-e48338a1b2f2`

2. **Authentication Page:**
   - [ ] Platform: "Single-page application" (SPA) configured
   - [ ] Redirect URI present: `https://spaarkedev1.crm.dynamics.com`
   - [ ] **If missing:** Click "Add a platform" → "Single-page application" → Add URI → Save

3. **API Permissions Page:**
   - [ ] Permission: `api://spe-bff-api/user_impersonation` (Delegated)
   - [ ] Status: "Granted for [Tenant Name]" (green checkmark)
   - [ ] **If not granted:** Click "Grant admin consent for [Tenant]" → Confirm

4. **Token Configuration (Optional - for debugging):**
   - [ ] Optional claims configured for debugging (if needed)
   - [ ] Token version: 2.0 (recommended)

### Expected Result
- ✅ All configuration values match code
- ✅ Redirect URI includes SPAARKE DEV 1 environment URL
- ✅ API permissions granted with admin consent

---

## 2. BFF API Deployment

**Base URL:** `https://spe-api-dev-67e2xz.azurewebsites.net`

**From:** [types/index.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/index.ts:127)

### Required Configuration

| Setting | Value | Status |
|---------|-------|--------|
| **BFF Base URL** | `https://spe-api-dev-67e2xz.azurewebsites.net` | ✅ Configured in code |
| **Timeout** | 300000ms (5 minutes) | ✅ Configured in code |
| **OBO Endpoints** | `/api/obo/drives/*` | ⚠️ **VERIFY BFF DEPLOYED** |

### Verification Steps

1. **Check BFF Health:**
   ```bash
   # Open browser or use curl
   curl https://spe-api-dev-67e2xz.azurewebsites.net
   ```
   - [ ] BFF responds (not 404 or offline)
   - [ ] Response indicates service is running

2. **Verify Sprint 4 OBO Implementation:**
   - [ ] Sprint 4 Task 4.4 complete (OBO endpoints implemented)
   - [ ] `TokenHelper.ExtractBearerToken()` implemented
   - [ ] OBO flow: User token → Graph token working
   - [ ] Endpoints accept `Authorization: Bearer <token>` header

3. **Test BFF Accepts MSAL Tokens (Optional - requires Postman/curl):**
   ```bash
   # Get token manually from Azure Portal (for testing only)
   # Make test request to BFF
   curl -H "Authorization: Bearer <token>" \
        https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/test
   ```
   - [ ] BFF validates token
   - [ ] BFF performs OBO flow
   - [ ] Response indicates success (or appropriate error)

### Expected Result
- ✅ BFF deployed and responding
- ✅ OBO endpoints available
- ✅ Sprint 4 implementation complete

---

## 3. PCF Control Build

**Location:** `src/controls/UniversalDatasetGrid/UniversalDatasetGrid`

### Build Verification

1. **Clean Build:**
   ```bash
   cd "src/controls/UniversalDatasetGrid/UniversalDatasetGrid"
   npm run build
   ```
   - [ ] Build completes without errors
   - [ ] Output: `[build] Succeeded`
   - [ ] Bundle generated: `out/controls/UniversalDatasetGrid/bundle.js`

2. **Verify MSAL Integrated:**
   ```bash
   # Check bundle includes MSAL
   grep -i "msal" out/controls/UniversalDatasetGrid/bundle.js
   ```
   - [ ] MSAL code present in bundle
   - [ ] Bundle size: ~9.75 MiB (includes MSAL, Fluent UI, React)

3. **Check Phase 1-3 Implementation:**
   - [ ] Phase 1: MSAL initialization in `index.ts`
   - [ ] Phase 2: Token acquisition with caching in `MsalAuthProvider.ts`
   - [ ] Phase 3: MSAL integration in `SdapApiClientFactory.ts`
   - [ ] Task 3.2: 401 retry logic in `SdapApiClient.ts`

### Expected Result
- ✅ Control builds successfully with MSAL
- ✅ All Sprint 8 code integrated
- ✅ No build errors or warnings (except ESLint)

---

## 4. Dataverse Environment

**Environment Name:** SPAARKE DEV 1
**URL:** `https://spaarkedev1.crm.dynamics.com`

### Required Configuration

| Setting | Value | Status |
|---------|-------|--------|
| **Environment URL** | `https://spaarkedev1.crm.dynamics.com` | ✅ Matches redirect URI |
| **PCF Control** | Universal Dataset Grid | ⚠️ **VERIFY DEPLOYED** |
| **Entity** | Document entity (or test entity) | ⚠️ **VERIFY EXISTS** |

### Verification Steps

1. **Access Environment:**
   - [ ] Can log in to: `https://spaarkedev1.crm.dynamics.com`
   - [ ] User credentials work
   - [ ] Environment loads correctly

2. **Verify PCF Solution (if already deployed):**
   - [ ] Navigate to: Settings → Solutions
   - [ ] Find solution with Universal Dataset Grid control
   - [ ] Check version number (should match build)
   - [ ] **If not deployed:** Proceed to deployment steps below

3. **Verify Entity Form Configuration:**
   - [ ] Entity exists (e.g., Document entity)
   - [ ] Form includes Universal Dataset Grid control
   - [ ] Control configured with correct field mappings
   - [ ] Form published

4. **Verify Test Data:**
   - [ ] Test records exist in entity
   - [ ] At least one record has file attached (for download test)
   - [ ] At least one record without file (for upload test)
   - [ ] SharePoint Embedded container exists (Sprint 4 prerequisite)

### Expected Result
- ✅ Environment accessible
- ✅ Entity and form configured (or ready to configure)
- ✅ Test data available

---

## 5. Solution Packaging and Deployment

### Option A: Managed Solution (Recommended for Production)

**Steps:**

1. **Package Solution:**
   ```bash
   cd "src/controls/UniversalDatasetGrid"
   pac solution pack --zipfile "../../solutions/UniversalDatasetGrid_managed.zip" --managed
   ```
   - [ ] Solution packaged successfully
   - [ ] .zip file created

2. **Import to Dataverse:**
   - [ ] Navigate to: `https://spaarkedev1.crm.dynamics.com` → Settings → Solutions
   - [ ] Click "Import"
   - [ ] Select `UniversalDatasetGrid_managed.zip`
   - [ ] Follow import wizard
   - [ ] Wait for import to complete
   - [ ] Verify: No import errors

3. **Publish All Customizations:**
   - [ ] Click "Publish All Customizations"
   - [ ] Wait for publish to complete

### Option B: Unmanaged Solution (Development/Testing)

**Steps:**

1. **Package Solution:**
   ```bash
   cd "src/controls/UniversalDatasetGrid"
   pac solution pack --zipfile "../../solutions/UniversalDatasetGrid_unmanaged.zip"
   ```
   - [ ] Solution packaged successfully

2. **Import to Dataverse:**
   - Same as Option A, but with unmanaged solution

### Option C: Direct Deployment (PAC CLI - Fastest for Dev)

**Steps:**

1. **Authenticate to Environment:**
   ```bash
   pac auth create --url https://spaarkedev1.crm.dynamics.com
   ```
   - [ ] Authentication successful
   - [ ] Connected as correct user

2. **Push PCF Control:**
   ```bash
   cd "src/controls/UniversalDatasetGrid/UniversalDatasetGrid"
   pac pcf push --publisher-prefix spk
   ```
   - [ ] Control pushed successfully
   - [ ] Version incremented
   - [ ] No deployment errors

### Expected Result
- ✅ PCF control deployed to SPAARKE DEV 1
- ✅ Control available in form designer
- ✅ Ready for testing

---

## 6. Form Configuration

**If control not yet added to form:**

### Steps:

1. **Open Form Designer:**
   - Navigate to: Power Apps → Solutions → [Your Solution]
   - Find entity (e.g., Document entity)
   - Open main form in form designer

2. **Add PCF Control:**
   - Add a new section (if needed)
   - Add "Universal Dataset Grid" control
   - Configure control properties:
     - Dataset binding: Entity records
     - Field mappings: Use default (sprk_* fields)

3. **Save and Publish:**
   - Click "Save"
   - Click "Publish"
   - Wait for publish to complete

### Expected Result
- ✅ Control visible on form
- ✅ Control loads when opening record

---

## 7. Test User Configuration

### Required Permissions

**User:** [Your test user email]

**Permissions Required:**
- [ ] Access to SPAARKE DEV 1 environment
- [ ] Read access to Document entity (or test entity)
- [ ] Create/Update/Delete permissions (for file operations)
- [ ] SharePoint Embedded permissions (Sprint 4)
- [ ] Azure AD user in correct tenant

### Verification Steps

1. **Log in as Test User:**
   - [ ] Open: `https://spaarkedev1.crm.dynamics.com`
   - [ ] Log in with test user credentials
   - [ ] Environment loads successfully

2. **Verify Entity Access:**
   - [ ] Navigate to entity (e.g., Documents)
   - [ ] Can view records
   - [ ] Can open record form

3. **Verify SharePoint Embedded Access:**
   - [ ] User has permissions to SharePoint Embedded container
   - [ ] User can perform file operations via Sprint 4 BFF
   - [ ] No 403 errors when accessing files

### Expected Result
- ✅ Test user can access environment and entity
- ✅ Test user has file operation permissions

---

## 8. Browser Configuration

### Recommended Browsers

**Primary:** Microsoft Edge or Google Chrome (latest version)

**Configuration:**
- [ ] JavaScript enabled
- [ ] Cookies enabled for `spaarkedev1.crm.dynamics.com` and `login.microsoftonline.com`
- [ ] Pop-ups allowed (for MSAL authentication fallback)
- [ ] Browser cache cleared (for clean test)

### DevTools Setup (for testing)

**Press F12 to open DevTools:**
- [ ] Console tab available (for logs)
- [ ] Network tab available (for API monitoring)
- [ ] Application tab available (for sessionStorage inspection)
- [ ] Performance tab available (for performance testing)

### Expected Result
- ✅ Browser configured correctly for testing
- ✅ DevTools accessible

---

## 9. Pre-Flight Test (Quick Smoke Test)

**Before starting full E2E testing:**

### Quick Validation

1. **Open Form with PCF Control:**
   - [ ] Log in to Dataverse
   - [ ] Navigate to entity with Universal Dataset Grid
   - [ ] Open a record
   - [ ] Control loads (grid visible)

2. **Check Console Logs:**
   - [ ] Press F12 → Console tab
   - [ ] Look for: `[Control] Init complete`
   - [ ] Look for: `[MsalAuthProvider] MSAL initialized successfully`
   - [ ] Look for: `[Control] User authenticated: true`
   - [ ] No red errors

3. **Test Simple Operation:**
   - [ ] Click any button (e.g., Download)
   - [ ] Check console: `[SdapApiClientFactory] Retrieving access token via MSAL`
   - [ ] Check console: `[MsalAuthProvider] Getting token for scopes...`
   - [ ] Operation completes (or shows expected error if no file selected)

### Expected Result
- ✅ Control loads without errors
- ✅ MSAL initializes successfully
- ✅ Token acquisition attempts work
- ✅ Ready for full E2E testing

### If Pre-Flight Test Fails

**Common issues:**
- ❌ **Control doesn't load:** Check solution deployment, publish customizations
- ❌ **MSAL initialization fails:** Check Azure App Registration configuration
- ❌ **"redirect_uri_mismatch":** Add redirect URI to Azure App Registration
- ❌ **401 errors:** Check BFF configuration, verify OBO flow

**Action:** Fix issues before proceeding to Phase 4.2 E2E Testing

---

## 10. Deployment Checklist Summary

**Complete this checklist before starting E2E testing:**

### Critical Items (Must Complete)

- [ ] ✅ **Azure App Registration:** Verified configuration matches code
- [ ] ✅ **Redirect URI:** Added to Azure App Registration
- [ ] ✅ **API Permissions:** Granted admin consent
- [ ] ✅ **BFF API:** Deployed and responding
- [ ] ✅ **PCF Control:** Built successfully with MSAL
- [ ] ✅ **Solution:** Deployed to SPAARKE DEV 1
- [ ] ✅ **Form:** Control added and published
- [ ] ✅ **Pre-Flight Test:** Passed

### Nice-to-Have Items (Can Fix During Testing)

- [ ] Test data: Multiple records with/without files
- [ ] Multiple test users (optional)
- [ ] Performance baseline measurements (optional)

---

## Next Steps

**After completing this checklist:**

1. **If all critical items checked:**
   - ✅ Proceed to [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md)
   - ✅ Start with Task 4.2: End-to-End Testing
   - ✅ Begin with Scenario 1 (Happy Path)

2. **If any critical items unchecked:**
   - ❌ Address missing configuration
   - ❌ Complete deployment steps
   - ❌ Retry pre-flight test
   - ❌ Do not proceed to E2E testing until all critical items checked

---

## Deployment Support

**Resources:**
- Azure Portal: https://portal.azure.com
- Dataverse: https://spaarkedev1.crm.dynamics.com
- BFF API: https://spe-api-dev-67e2xz.azurewebsites.net
- Sprint 8 Documentation: [SPRINT-8-MSAL-OVERVIEW.md](SPRINT-8-MSAL-OVERVIEW.md)

**Troubleshooting:**
- See [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md) → Troubleshooting section

---

**Checklist Status:** ⚠️ **PENDING VERIFICATION**
**Next Action:** Complete checklist items and verify environment readiness

---
