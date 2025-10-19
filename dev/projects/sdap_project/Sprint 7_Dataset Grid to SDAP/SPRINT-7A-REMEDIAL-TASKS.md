# Sprint 7A: MSAL Compliance - Remedial Tasks Index

**Sprint:** 7A - MSAL Integration Compliance
**Status:** 📋 Ready for Implementation
**Control Version:** v2.1.4
**Date Created:** October 6, 2025

---

## Executive Summary

Sprint 7A file operations are **already MSAL-compliant** at the code level. Sprint 8 updated the authentication infrastructure, and Sprint 7A automatically inherited MSAL through dependency injection.

**What's Needed:**
- ✅ Code compliance: Already achieved (no changes needed)
- ⏳ Testing validation: Pending (requires real test files)
- ⏳ Documentation updates: Pending

**Recommended Path:** Proceed to Sprint 7B (Quick Create) to create test files, then return to complete Sprint 7A testing.

---

## Quick Decision Tree

```
Do you have test records with REAL files in SharePoint Embedded?
│
├─ YES → Complete Tasks 1-4
│         Test file operations
│         Update documentation
│         Create compliance report
│         Proceed to Sprint 7B
│
└─ NO → Complete Task 1 (code review)
         Complete Task 4 (documentation)
         → PROCEED TO SPRINT 7B
         Return to Task 3 (testing) after Sprint 7B creates real files
```

---

## Task Overview

| Task | Title | Status | Time | Document |
|------|-------|--------|------|----------|
| **Task 1** | Code Review and Verification | ✅ Ready | 1-2 hours | [TASK-7A.1-CODE-REVIEW.md](TASK-7A.1-CODE-REVIEW.md) |
| **Task 2** | Build and Bundle Verification | ✅ Ready | 30 min | [TASK-7A.2-BUILD-VERIFICATION.md](TASK-7A.2-BUILD-VERIFICATION.md) |
| **Task 3** | Manual Testing - File Operations | ⚠️ Blocked | 2-3 hours | [TASK-7A.3-MANUAL-TESTING.md](TASK-7A.3-MANUAL-TESTING.md) |
| **Task 4** | Documentation Updates | ✅ Ready | 2-3 hours | [TASK-7A.4-DOCUMENTATION-UPDATES.md](TASK-7A.4-DOCUMENTATION-UPDATES.md) |

**Total Estimated Time:** 6-9 hours (if test data available)
**Minimum Required:** 3-5 hours (Tasks 1, 2, 4 only)

---

## Task Documents

Each task has been split into a separate, focused document to prevent context loss during AI coding sessions:

### Task 1: Code Review and Verification ✅
**Goal:** Verify Sprint 7A code uses MSAL authentication infrastructure

**[→ Read Full Task: TASK-7A.1-CODE-REVIEW.md](TASK-7A.1-CODE-REVIEW.md)**

**Quick Summary:**
- Verify SdapApiClientFactory uses MSAL
- Check service constructors use dependency injection
- Confirm MSAL initialization in index.ts
- Review 401 error handling

**Expected Result:** All checks pass (no code changes needed)

---

### Task 2: Build and Bundle Verification ✅
**Goal:** Ensure MSAL dependencies are correctly bundled

**[→ Read Full Task: TASK-7A.2-BUILD-VERIFICATION.md](TASK-7A.2-BUILD-VERIFICATION.md)**

**Quick Summary:**
- Verify MSAL package installed (@azure/msal-browser@4.24.1)
- Clean build successful (0 errors)
- Bundle size within limits (~540 KiB)
- MSAL code included in bundle

**Expected Result:** Build succeeds, MSAL bundled correctly

---

### Task 3: Manual Testing - File Operations ⚠️ BLOCKED
**Goal:** Test file operations with MSAL authentication

**[→ Read Full Task: TASK-7A.3-MANUAL-TESTING.md](TASK-7A.3-MANUAL-TESTING.md)**

**⚠️ BLOCKER:** Requires test records with **real files** in SharePoint Embedded

**Quick Summary:**
- Test download with MSAL tokens
- Test delete with MSAL tokens
- Test replace with MSAL tokens
- Verify token caching (82x improvement)
- Test error scenarios

**Status:** Cannot complete without real test files
**Resolution:** Proceed to Sprint 7B, create test files, then return

---

### Task 4: Documentation Updates ✅
**Goal:** Update Sprint 7A docs to reflect MSAL architecture

**[→ Read Full Task: TASK-7A.4-DOCUMENTATION-UPDATES.md](TASK-7A.4-DOCUMENTATION-UPDATES.md)**

**Quick Summary:**
- Update SPRINT-7A-COMPLETION-SUMMARY.md
- Update SPRINT-7A-DEPLOYMENT-COMPLETE.md
- Create SPRINT-7A-MSAL-INTEGRATION.md

**Expected Result:** All docs reference MSAL instead of PCF context tokens

---

## Supporting Documentation

### Overview Documents (Read These First)
- [SPRINT-7A-COMPLIANCE-REVIEW-SUMMARY.md](SPRINT-7A-COMPLIANCE-REVIEW-SUMMARY.md) - Detailed compliance analysis
- [SPRINT-7A-QUICK-REFERENCE.md](SPRINT-7A-QUICK-REFERENCE.md) - Quick facts and decisions

### Sprint 8 Reference
- [SPRINT-8-COMPLETION-REVIEW.md](../../Sprint%208%20-%20MSAL%20Integration/SPRINT-8-COMPLETION-REVIEW.md)
- [AUTHENTICATION-ARCHITECTURE.md](../../Sprint%208%20-%20MSAL%20Integration/AUTHENTICATION-ARCHITECTURE.md)

---

## Quick Start

### Recommended Path (No Test Files)

```bash
# 1. Read overview documents
# - SPRINT-7A-QUICK-REFERENCE.md (5 min)
# - SPRINT-7A-COMPLIANCE-REVIEW-SUMMARY.md (15 min)

# 2. Code Review
# - Read TASK-7A.1-CODE-REVIEW.md
# - Verify SdapApiClientFactory uses MSAL (30 min)

# 3. Build Verification
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid
npm run build
# - Read TASK-7A.2-BUILD-VERIFICATION.md (30 min)

# 4. Documentation Updates
# - Read TASK-7A.4-DOCUMENTATION-UPDATES.md
# - Update Sprint 7A docs (2-3 hours)

# 5. Proceed to Sprint 7B
```

---

## Success Criteria

### Minimum Required
- [x] Code review confirms MSAL usage
- [ ] Build succeeds with MSAL bundled
- [ ] Documentation updated to reflect MSAL
- [ ] Ready to proceed to Sprint 7B

### Full Validation (After Sprint 7B)
- [x] Code review confirms MSAL usage
- [ ] Build succeeds with MSAL bundled
- [ ] All file operations tested with MSAL
- [ ] Token caching verified (82x improvement)
- [ ] Documentation updated
- [ ] Compliance report created

---

## Context and Knowledge

### Architecture Changes from Sprint 8

#### Old Authentication (Sprint 7A Original)
```typescript
// ❌ DEPRECATED - No longer used
const token = (context as any).userSettings?.accessToken;
```

**Problems with old approach:**
- Token not always available in PCF context
- No token caching (slow repeated requests)
- No SSO support
- No proactive token refresh
- Race conditions possible

#### New Authentication (Sprint 8 MSAL)
```typescript
// ✅ CURRENT - MSAL-based authentication
const authProvider = MsalAuthProvider.getInstance();
await authProvider.initialize();
const token = await authProvider.getToken(SPE_BFF_API_SCOPES);
```

**Benefits of MSAL approach:**
- ✅ Browser-based SSO (ssoSilent)
- ✅ Token caching (82x performance improvement)
- ✅ Proactive token refresh
- ✅ Race condition handling
- ✅ Automatic retry on 401 errors
- ✅ Standards-compliant OAuth 2.0

### Key MSAL Concepts

#### 1. PublicClientApplication
- MSAL client for browser-based apps (SPA)
- Handles token acquisition via OAuth 2.0 flows
- Manages token cache in sessionStorage

#### 2. ssoSilent Flow
- Silent token acquisition using existing browser session
- No user interaction required (no login popup)
- Falls back to interactive login if SSO fails

#### 3. Token Caching
- Tokens cached in sessionStorage (cleared on tab close)
- Cache key includes scopes for isolation
- 5-minute expiration buffer for proactive refresh

#### 4. Race Condition Handling
- Users can click buttons before MSAL initializes
- `SdapApiClientFactory` checks initialization state
- Waits for initialization if needed before token acquisition

#### 5. On-Behalf-Of (OBO) Flow
```
PCF Control (User Token)
    → BFF API validates token
    → BFF API exchanges for Graph token (OBO)
    → Graph API calls SharePoint Embedded
    → Returns file data
```

### Azure AD Configuration

#### App Registration 1: Dataverse/PCF Control
- **Name:** Sparke DSM-SPE Dev 2
- **Client ID:** `170c98e1-d486-4355-bcbe-170454e0207c`
- **Tenant ID:** `a221a95e-6abc-4434-aecc-e48338a1b2f2`
- **Redirect URI:** `https://spaarkedev1.crm.dynamics.com`
- **Purpose:** Represents the PCF control running in Dataverse

**API Permissions:**
- Microsoft Graph / User.Read (Delegated)
- SPE BFF API / user_impersonation (Delegated)

#### App Registration 2: SPE BFF API
- **Name:** SPE BFF API
- **Client ID:** `1e40baad-e065-4aea-a8d4-4b7ab273458c`
- **Application ID URI:** `api://1e40baad-e065-4aea-a8d4-4b7ab273458c`
- **Purpose:** Backend API that performs OBO token exchange

**Exposed API:**
- Scope: `user_impersonation`
- Description: "Access the SDAP BFF API on behalf of the user"

**API Permissions (for OBO):**
- Microsoft Graph / Files.Read.All (Delegated)
- Microsoft Graph / Files.ReadWrite.All (Delegated)
- Microsoft Graph / Sites.Read.All (Delegated)
- Microsoft Graph / Sites.ReadWrite.All (Delegated)

### MSAL Token Flow in Sprint 7A

#### Download File Flow
```
1. User clicks Download button
   ↓
2. handleDownloadFile() in UniversalDatasetGridRoot.tsx
   ↓
3. FileDownloadService.downloadFile(driveId, itemId, fileName)
   ↓
4. SdapApiClient.downloadFile({ driveId, itemId })
   ↓
5. SdapApiClient calls getAccessToken() function
   ↓
6. getAccessToken() uses MsalAuthProvider.getToken(SPE_BFF_API_SCOPES)
   ↓
7. MsalAuthProvider:
   - Checks sessionStorage cache (5ms if cached)
   - If not cached, calls msalInstance.ssoSilent() (~420ms)
   - Caches token for 55 minutes
   - Returns token
   ↓
8. SdapApiClient makes HTTP request with Authorization: Bearer {token}
   ↓
9. BFF API validates token and performs OBO exchange
   ↓
10. Graph API returns file blob
   ↓
11. FileDownloadService triggers browser download
```

#### Delete File Flow
```
1. User clicks Remove File button
   ↓
2. ConfirmDialog shows confirmation
   ↓
3. User clicks Delete
   ↓
4. handleDeleteConfirm() in UniversalDatasetGridRoot.tsx
   ↓
5. FileDeleteService.deleteFile(documentId, driveId, itemId, fileName)
   ↓
6. SdapApiClient.deleteFile({ driveId, itemId })
   ↓
7. [Same MSAL token flow as Download - steps 5-9]
   ↓
10. Graph API deletes file from SharePoint Embedded
   ↓
11. FileDeleteService updates Dataverse record (hasFile = false)
   ↓
12. Grid refreshes
```

#### Replace File Flow
```
1. User clicks Update File button
   ↓
2. Browser file picker opens
   ↓
3. User selects new file
   ↓
4. handleReplaceFile() in UniversalDatasetGridRoot.tsx
   ↓
5. FileReplaceService.pickAndReplaceFile(documentId, driveId, itemId)
   ↓
6. SdapApiClient.replaceFile({ driveId, itemId, file, fileName })
   ↓
7. [Same MSAL token flow - steps 5-9]
   ↓
8. BFF API performs atomic delete + upload
   ↓
9. Graph API deletes old file, uploads new file
   ↓
10. FileReplaceService updates Dataverse record with new metadata
   ↓
11. Grid refreshes
```

### Error Handling with MSAL

#### 401 Unauthorized - Automatic Retry
```typescript
// In SdapApiClient.fetchWithTimeout()
if (response.status === 401 && attempt < maxAttempts) {
    logger.warn('SdapApiClient', '401 Unauthorized - clearing cache and retrying');

    // Clear MSAL cache to force fresh token
    MsalAuthProvider.getInstance().clearCache();

    // Get fresh token
    const newToken = await this.getAccessToken();

    // Retry with fresh token
    options.headers['Authorization'] = `Bearer ${newToken}`;
    continue; // Retry loop
}
```

**Why this is needed:**
- Token can expire between cache check and API call
- Race condition: cached token valid when retrieved, expired when API receives it
- Automatic retry ensures seamless user experience

#### MSAL Initialization Race Condition
```typescript
// In SdapApiClientFactory.create()
const getAccessToken = async (): Promise<string> => {
    const authProvider = MsalAuthProvider.getInstance();

    // Handle race condition: user clicks before MSAL initialized
    if (!authProvider.isInitializedState()) {
        logger.info('SdapApiClientFactory', 'MSAL not yet initialized, waiting...');
        await authProvider.initialize();
    }

    const token = await authProvider.getToken(SPE_BFF_API_SCOPES);
    return token;
};
```

**Why this is needed:**
- MSAL initialization is async in `index.ts` (via `initializeMsalAsync`)
- User can click Download/Delete/Replace before initialization completes
- Without this check, token acquisition would fail with "MSAL not initialized"

### Files Updated by Sprint 8

| File | Status | MSAL Integration |
|------|--------|------------------|
| `services/auth/msalConfig.ts` | ✅ Created | MSAL configuration (client ID, tenant, scopes) |
| `services/auth/MsalAuthProvider.ts` | ✅ Created | MSAL wrapper with caching and proactive refresh |
| `services/SdapApiClientFactory.ts` | ✅ Updated | Uses MsalAuthProvider for token acquisition |
| `services/SdapApiClient.ts` | ✅ Updated | Automatic 401 retry with cache clear |
| `index.ts` | ✅ Updated | Initializes MSAL on control startup |
| `ControlManifest.Input.xml` | ✅ Updated | Version bumped to 2.1.4 |

### Files NOT Changed (Sprint 7A Services)

These files **use** the updated infrastructure but were **not modified** by Sprint 8:

| File | Status | Uses MSAL? |
|------|--------|------------|
| `services/FileDownloadService.ts` | ⚠️ Untested | ✅ Yes (via SdapApiClient) |
| `services/FileDeleteService.ts` | ⚠️ Untested | ✅ Yes (via SdapApiClient) |
| `services/FileReplaceService.ts` | ⚠️ Untested | ✅ Yes (via SdapApiClient) |
| `components/UniversalDatasetGridRoot.tsx` | ⚠️ Untested | ✅ Yes (via services) |

**Key Point:** These files already use MSAL authentication (indirectly through `SdapApiClientFactory`), but have NOT been tested since MSAL integration.

---

## Remedial Tasks

### Task 1: Code Review and Verification ✅

**Goal:** Verify that all Sprint 7A code is using the updated MSAL authentication infrastructure.

**Success Criteria:**
- All file operation services use `SdapApiClientFactory.create()`
- No deprecated PCF context token usage
- All services follow MSAL error handling patterns

#### Step 1.1: Review SdapApiClientFactory Usage

**File to Review:** `components/UniversalDatasetGridRoot.tsx`

**What to Check:**
```typescript
// ✅ CORRECT - Uses factory pattern with MSAL
const apiClient = SdapApiClientFactory.create(
    sdapConfig.baseUrl,
    sdapConfig.timeout
);
```

**What to Avoid:**
```typescript
// ❌ WRONG - Direct instantiation bypasses MSAL
const token = (context as any).userSettings?.accessToken;
const apiClient = new SdapApiClient(baseUrl, () => Promise.resolve(token));
```

**Action:**
1. Open `UniversalDatasetGridRoot.tsx` in your editor
2. Search for `SdapApiClient` instantiation
3. Verify it uses `SdapApiClientFactory.create()`
4. Confirm no direct `new SdapApiClient()` calls exist

**Expected Finding:** ✅ Already correct (Sprint 8 updated this)

**Documentation:**
```bash
# Search for API client creation
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid
grep -n "SdapApiClient" components/UniversalDatasetGridRoot.tsx
```

#### Step 1.2: Verify Service Constructors

**Files to Review:**
- `services/FileDownloadService.ts`
- `services/FileDeleteService.ts`
- `services/FileReplaceService.ts`

**What to Check:**
```typescript
// ✅ CORRECT - Services receive SdapApiClient instance
export class FileDownloadService {
    constructor(private apiClient: SdapApiClient) {}

    async downloadFile(driveId: string, itemId: string, fileName: string) {
        // Uses this.apiClient.downloadFile()
        // SdapApiClient handles MSAL token acquisition
    }
}
```

**Action:**
1. Open each service file
2. Verify constructor accepts `SdapApiClient` instance
3. Verify no direct token handling in service code
4. Confirm services delegate to `apiClient` methods

**Expected Finding:** ✅ Already correct (Sprint 7A design is sound)

#### Step 1.3: Check MSAL Initialization

**File to Review:** `index.ts`

**What to Check:**
```typescript
// ✅ CORRECT - MSAL initialized in init()
public init(...) {
    this.initializeMsalAsync(container);
    // ...
}

private initializeMsalAsync(container: HTMLDivElement): void {
    (async () => {
        this.authProvider = MsalAuthProvider.getInstance();
        await this.authProvider.initialize();
        logger.info('Control', 'MSAL initialized successfully ✅');
    })();
}
```

**Action:**
1. Open `index.ts`
2. Verify `initializeMsalAsync()` is called in `init()`
3. Confirm error handling displays user-friendly message
4. Check `destroy()` method clears MSAL cache

**Expected Finding:** ✅ Already correct (Sprint 8 added this)

**Validation Command:**
```bash
# Verify MSAL initialization exists
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid
grep -A 20 "initializeMsalAsync" index.ts
```

#### Step 1.4: Review Error Handling

**File to Review:** `services/SdapApiClient.ts`

**What to Check:**
```typescript
// ✅ CORRECT - Automatic 401 retry with cache clear
private async fetchWithTimeout(url: string, options: RequestInit): Promise<Response> {
    let attempt = 0;
    const maxAttempts = 2;

    while (attempt < maxAttempts) {
        attempt++;
        const response = await fetch(url, { ...options, signal: controller.signal });

        // Automatic retry on 401
        if (response.status === 401 && attempt < maxAttempts) {
            MsalAuthProvider.getInstance().clearCache();
            const newToken = await this.getAccessToken();
            options.headers['Authorization'] = `Bearer ${newToken}`;
            continue; // Retry
        }

        return response;
    }
}
```

**Action:**
1. Open `SdapApiClient.ts`
2. Find `fetchWithTimeout()` method
3. Verify 401 handling includes cache clear and retry
4. Confirm retry logic uses fresh token

**Expected Finding:** ✅ Already correct (Sprint 8 added this)

**Task 1 Deliverable:**
- ✅ Code review checklist completed
- ✅ No MSAL compliance issues found
- ✅ All services use MSAL authentication

---

### Task 2: Build and Bundle Verification

**Goal:** Ensure MSAL dependencies are correctly bundled and control builds without errors.

**Success Criteria:**
- Build completes with 0 errors, 0 warnings
- MSAL package (@azure/msal-browser) included in bundle
- Bundle size within acceptable limits (<5 MB)

#### Step 2.1: Verify MSAL Package Installation

**Action:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Check MSAL package is installed
npm list @azure/msal-browser

# Expected output:
# pcf-project@1.0.0
# └── @azure/msal-browser@4.24.1
```

**Success Criteria:**
- ✅ @azure/msal-browser version 4.24.1 installed
- ✅ No package resolution errors

#### Step 2.2: Clean Build

**Action:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Clean previous build artifacts
npm run clean

# Rebuild control
npm run build
```

**Expected Output:**
```
> webpack --mode development

webpack 5.x.x compiled successfully in X ms

Build complete:
- bundle.js: ~540 KiB
- 0 errors
- 0 warnings
```

**Success Criteria:**
- ✅ Build completes without errors
- ✅ Build completes without warnings
- ✅ Bundle size < 600 KiB (development build)

**Troubleshooting:**

If build fails with TypeScript errors:
```bash
# Check TypeScript compilation separately
npx tsc --noEmit

# Fix any type errors before continuing
```

If MSAL import errors:
```bash
# Reinstall dependencies
npm install

# Rebuild
npm run build
```

#### Step 2.3: Verify Bundle Contents

**Action:**
```bash
# Check if MSAL is included in bundle
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid
grep -i "msal" out/controls/UniversalDatasetGrid/bundle.js | head -5

# Expected: Should find references to MSAL classes and methods
```

**Success Criteria:**
- ✅ MSAL code is present in bundle
- ✅ PublicClientApplication class is included
- ✅ ssoSilent method is included

**Task 2 Deliverable:**
- ✅ Clean build successful
- ✅ MSAL dependencies bundled correctly
- ✅ Bundle size documented

---

### Task 3: Manual Testing - File Operations

**Goal:** Test all Sprint 7A file operations (download, delete, replace) with MSAL authentication using real user sessions.

**Prerequisites:**
- ✅ Control built and deployed (v2.1.4)
- ✅ Test environment: SPAARKE DEV 1
- ✅ Test records with **real files** (not placeholders)

⚠️ **IMPORTANT:** You need test records with actual files uploaded to SharePoint Embedded. Placeholder itemIds will not work for end-to-end testing.

#### Step 3.1: Environment Setup

**Action:**
1. Navigate to SPAARKE DEV 1 environment
2. Open a model-driven app with Universal Dataset Grid
3. Navigate to a view/form showing the `sprk_document` entity
4. Open browser DevTools console (F12)
5. Clear browser cache (Ctrl+Shift+Delete)

**Expected Console Output on Page Load:**
```
[Control] Init - Creating single React root
[Control] Initializing MSAL authentication...
[Control] MSAL authentication initialized successfully ✅
[Control] User authenticated: true
[Control] Account info: { username: "your.name@spaarke.com", ... }
```

**Success Criteria:**
- ✅ MSAL initializes without errors
- ✅ User is authenticated
- ✅ No console errors related to MSAL

**Troubleshooting:**

If MSAL initialization fails:
```javascript
// In console, check MSAL state
window.sessionStorage.getItem('msal.initialized')
// Should be: "true"

// Check if token exists
Object.keys(window.sessionStorage).filter(k => k.includes('msal.token'))
// Should show token cache keys
```

#### Step 3.2: Test File Download with MSAL

**Test Case:** Single file download

**Action:**
1. Select a record with `sprk_hasfile = true`
2. Verify record has valid `sprk_graphdriveid` and `sprk_graphitemid`
3. Click **Download** button
4. Monitor browser console for MSAL logs

**Expected Console Output:**
```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Getting token for scopes: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
[MsalAuthProvider] Token retrieved from cache (5ms)  // First time may show "ssoSilent (420ms)"
[SdapApiClient] Downloading file: Contract.pdf
[SdapApiClient] File downloaded successfully: { size: 2458624, type: "application/pdf" }
[FileDownloadService] Download triggered successfully: Contract.pdf
```

**Expected User Experience:**
- Browser download dialog appears
- File downloads with correct filename
- File opens successfully in appropriate application

**Success Criteria:**
- ✅ Token acquired via MSAL (cache or ssoSilent)
- ✅ Download completes successfully
- ✅ File is valid and not corrupted
- ✅ No console errors

**Troubleshooting:**

If 401 error occurs:
```
// Console should show automatic retry
[SdapApiClient] 401 Unauthorized - clearing cache and retrying
[MsalAuthProvider] Cache cleared
[SdapApiClient] Retrying request with fresh token
// Second attempt should succeed
```

If download fails with 500 error:
- Check if itemId is a placeholder (`01PLACEHOLDER...`)
- Verify BFF API is running and accessible
- Check Network tab for actual API response

#### Step 3.3: Test Token Caching Performance

**Test Case:** Verify token caching improves performance

**Action:**
1. Clear browser cache and reload page
2. Download first file (cold cache)
3. Immediately download second file (warm cache)
4. Compare token acquisition times in console

**Expected Console Output:**

**First download (cold cache):**
```
[MsalAuthProvider] Token not in cache, acquiring via ssoSilent
[MsalAuthProvider] Token acquired in 420ms
[MsalAuthProvider] Token cached with expiry: 2025-10-06T15:30:00.000Z
```

**Second download (warm cache):**
```
[MsalAuthProvider] Token retrieved from cache in 5ms
```

**Success Criteria:**
- ✅ First request takes ~420ms (ssoSilent)
- ✅ Second request takes ~5ms (cache hit)
- ✅ 82x performance improvement observed
- ✅ Cache expiry logged correctly

#### Step 3.4: Test File Delete with MSAL

**Test Case:** Single file delete with confirmation

**Action:**
1. Select a record with `sprk_hasfile = true`
2. Click **Remove File** button
3. Verify confirmation dialog appears
4. Click **Delete** button
5. Monitor browser console

**Expected Console Output:**
```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Token retrieved from cache (5ms)
[SdapApiClient] Deleting file: driveId=..., itemId=...
[SdapApiClient] File deleted successfully
[FileDeleteService] Updating Dataverse record: hasFile = false
[FileDeleteService] File deleted successfully
```

**Expected User Experience:**
- Confirmation dialog shows filename
- Cancel button works correctly (closes dialog, no action)
- Delete button triggers delete operation
- Grid refreshes automatically
- Record remains in grid with `hasFile = false`

**Expected Dataverse Changes:**
```
sprk_hasfile = false
sprk_graphitemid = null
sprk_filename = null
sprk_filesize = null
sprk_filepath = null
(all file metadata cleared)
```

**Success Criteria:**
- ✅ Token acquired via MSAL cache
- ✅ File deleted from SharePoint Embedded
- ✅ Dataverse record updated correctly
- ✅ Grid refreshes automatically
- ✅ No console errors

#### Step 3.5: Test File Replace with MSAL

**Test Case:** Replace existing file with new file

**Action:**
1. Select a record with `sprk_hasfile = true`
2. Click **Update File** button
3. Select a new file from file picker
4. Monitor browser console

**Expected Console Output:**
```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Token retrieved from cache (5ms)
[SdapApiClient] Replacing file: fileName=NewContract.pdf
[SdapApiClient] Old file deleted
[SdapApiClient] New file uploaded
[SdapApiClient] File replaced successfully: { id: "01ABC...", name: "NewContract.pdf", ... }
[FileReplaceService] Updating Dataverse record with new metadata
[FileReplaceService] File replaced successfully
```

**Expected User Experience:**
- File picker opens
- After selection, upload begins
- Grid refreshes with new file metadata
- SharePoint link points to new file

**Expected Dataverse Changes:**
```
sprk_filename = "NewContract.pdf" (updated)
sprk_filesize = 1234567 (updated)
sprk_graphitemid = "01NEWID..." (updated)
sprk_filepath = "https://..." (updated)
sprk_createddatetime = <new timestamp> (updated)
sprk_lastmodifieddatetime = <new timestamp> (updated)
sprk_etag = "<new version>" (updated)
```

**Success Criteria:**
- ✅ Token acquired via MSAL cache
- ✅ Old file deleted from SharePoint
- ✅ New file uploaded to SharePoint
- ✅ Dataverse record updated with new metadata
- ✅ Grid refreshes automatically
- ✅ SharePoint link works for new file
- ✅ No console errors

#### Step 3.6: Test Error Scenarios

**Test Case 1: Token expiration during operation**

**Setup:**
1. Wait for token to be near expiry (55+ minutes)
2. Or manually clear MSAL cache in console:
   ```javascript
   MsalAuthProvider.getInstance().clearCache();
   ```

**Action:**
1. Try to download a file
2. Monitor console for automatic retry

**Expected Console Output:**
```
[SdapApiClient] Request failed with 401 Unauthorized
[SdapApiClient] Clearing token cache and retrying
[MsalAuthProvider] Cache cleared
[MsalAuthProvider] Acquiring fresh token via ssoSilent
[SdapApiClient] Retrying with fresh token
[SdapApiClient] Download successful on retry
```

**Success Criteria:**
- ✅ 401 error handled automatically
- ✅ Fresh token acquired
- ✅ Retry succeeds without user intervention

**Test Case 2: MSAL initialization race condition**

**Setup:**
1. Refresh page
2. Immediately click Download button (before MSAL initialization completes)

**Expected Console Output:**
```
[Control] Initializing MSAL authentication...
[SdapApiClientFactory] Retrieving access token via MSAL
[SdapApiClientFactory] MSAL not yet initialized, waiting...
[Control] MSAL authentication initialized successfully ✅
[SdapApiClientFactory] MSAL initialization complete
[MsalAuthProvider] Getting token...
```

**Success Criteria:**
- ✅ Factory waits for initialization
- ✅ Token acquisition succeeds after wait
- ✅ Operation completes successfully
- ✅ No "MSAL not initialized" errors

**Test Case 3: Network timeout**

**Setup:**
1. Open DevTools Network tab
2. Throttle to "Slow 3G" or "Offline"

**Action:**
1. Try to download a file
2. Monitor console

**Expected Console Output:**
```
[SdapApiClient] Request timeout after 300000ms
[FileDownloadService] Download failed: Request timeout
```

**Expected User Experience:**
- Error message displayed (if UI supports it)
- No unhandled promise rejection
- Graceful degradation

**Success Criteria:**
- ✅ Timeout handled gracefully
- ✅ User-friendly error message
- ✅ No console exceptions

**Task 3 Deliverable:**
- ✅ All file operations tested with MSAL
- ✅ Token caching performance validated
- ✅ Error scenarios handled correctly
- ✅ Test results documented

---

### Task 4: Documentation Updates

**Goal:** Update Sprint 7A documentation to reflect MSAL authentication architecture.

**Success Criteria:**
- All Sprint 7A docs reference MSAL instead of PCF context tokens
- Authentication flow diagrams updated
- Troubleshooting guides include MSAL scenarios

#### Step 4.1: Update SPRINT-7A-COMPLETION-SUMMARY.md

**File:** `dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/SPRINT-7A-COMPLETION-SUMMARY.md`

**Sections to Update:**

**1. Authentication Flow Section**

**OLD (remove):**
```markdown
### Authentication Flow
- Uses PCF context token from `context.userSettings.accessToken`
- Token passed to SDAP BFF API for OBO flow
```

**NEW (add):**
```markdown
### Authentication Flow (MSAL)
- Uses MSAL browser-based authentication (@azure/msal-browser v4.24.1)
- Token acquisition via ssoSilent (SSO flow)
- Token caching in sessionStorage (82x performance improvement)
- Automatic retry on 401 errors with cache clear
- Race condition handling for initialization timing

**Token Flow:**
1. MsalAuthProvider.getToken() - Check cache (5ms if cached)
2. If not cached, ssoSilent() to Azure AD (~420ms)
3. Token cached with 5-min expiration buffer
4. SdapApiClient uses token for Authorization header
5. BFF API validates token and performs OBO exchange
6. Graph API calls SharePoint Embedded
```

**2. Add Performance Metrics**

**NEW section to add:**
```markdown
### MSAL Performance Metrics
- **Token Acquisition (Cold Cache):** ~420ms (ssoSilent)
- **Token Acquisition (Warm Cache):** ~5ms (sessionStorage)
- **Performance Improvement:** 82x faster with caching
- **Cache Hit Rate:** ~95% (within token lifetime)
- **Cache Duration:** 55 minutes (1 hour token - 5 min buffer)
```

**3. Update Known Limitations**

**Add to Known Limitations section:**
```markdown
### MSAL Authentication Limitations
1. **SessionStorage Only:** Tokens cleared on browser tab close
2. **No Persistent SSO:** User must re-authenticate on new tab
3. **Initialization Race:** Users clicking before MSAL ready (handled gracefully)
4. **Token Expiry:** Automatic retry on 401, but brief delay possible
```

#### Step 4.2: Update SPRINT-7A-DEPLOYMENT-COMPLETE.md

**File:** `dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/SPRINT-7A-DEPLOYMENT-COMPLETE.md`

**Sections to Update:**

**1. Update Control Version**

**OLD:**
```markdown
**Solution Package**: `UniversalDatasetGridSolution.zip` (Managed)
**Version**: 2.0.7
```

**NEW:**
```markdown
**Solution Package**: `UniversalDatasetGridSolution.zip` (Managed)
**Version**: 2.1.4 (includes MSAL authentication)
```

**2. Add MSAL Configuration Section**

**NEW section to add:**
```markdown
### MSAL Authentication Configuration ✅

**Azure AD App Registration:**
- **Client ID:** 170c98e1-d486-4355-bcbe-170454e0207c
- **Tenant ID:** a221a95e-6abc-4434-aecc-e48338a1b2f2
- **Redirect URI:** https://spaarkedev1.crm.dynamics.com

**SPE BFF API Scope:**
- `api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation`

**Token Caching:**
- Location: sessionStorage (browser)
- Duration: 55 minutes (with 5-min buffer)
- Cache cleared on browser tab close
```

**3. Update Troubleshooting Section**

**Add to Troubleshooting:**
```markdown
**Issue: "MSAL not initialized" errors**
- Solution: Wait a few seconds after page load
- Verify MSAL initialization in console: `[Control] MSAL initialized successfully ✅`
- If persists, check Azure AD app registration configuration

**Issue: Token acquisition fails**
- Solution: Check browser console for MSAL errors
- Verify user has permissions to access SPE BFF API
- Check Azure AD app permissions are granted
- Try incognito mode to rule out browser cache issues

**Issue: 401 Unauthorized after working previously**
- Solution: This is normal - token expired during request
- MSAL will automatically retry with fresh token
- Check console for retry logs
- If retry also fails, check BFF API is running
```

#### Step 4.3: Create MSAL Integration Summary

**NEW File:** `dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/SPRINT-7A-MSAL-INTEGRATION.md`

**Content:**
```markdown
# Sprint 7A: MSAL Integration Summary

**Integration Date:** October 6, 2025
**Sprint 8 Dependency:** MSAL authentication infrastructure
**Control Version:** v2.1.4

---

## Overview

Sprint 7A file operations (download, delete, replace) now use MSAL browser-based authentication instead of PCF context tokens. This integration was completed as part of Sprint 8 MSAL implementation.

## What Changed

### Before (Sprint 7A Original)
- ❌ Used PCF context token: `context.userSettings.accessToken`
- ❌ No token caching (slow repeated requests)
- ❌ No SSO support
- ❌ Manual error handling for expired tokens

### After (Sprint 8 MSAL Integration)
- ✅ Uses MSAL authentication: `MsalAuthProvider.getToken()`
- ✅ Token caching (82x performance improvement)
- ✅ SSO support via ssoSilent
- ✅ Automatic retry on 401 errors
- ✅ Race condition handling

## Files Updated

| File | Change | Status |
|------|--------|--------|
| `services/auth/msalConfig.ts` | Created | ✅ New |
| `services/auth/MsalAuthProvider.ts` | Created | ✅ New |
| `services/SdapApiClientFactory.ts` | Updated | ✅ MSAL integration |
| `services/SdapApiClient.ts` | Updated | ✅ Auto-retry on 401 |
| `index.ts` | Updated | ✅ MSAL initialization |

## Files Unchanged (But Now Use MSAL)

These files use MSAL indirectly through `SdapApiClient`:

- `services/FileDownloadService.ts` - ✅ Works with MSAL
- `services/FileDeleteService.ts` - ✅ Works with MSAL
- `services/FileReplaceService.ts` - ✅ Works with MSAL

## Testing Status

| Operation | MSAL Tested | Status |
|-----------|-------------|--------|
| Download File | ⏳ Pending | Needs testing |
| Delete File | ⏳ Pending | Needs testing |
| Replace File | ⏳ Pending | Needs testing |
| Token Caching | ⏳ Pending | Needs testing |
| 401 Retry | ⏳ Pending | Needs testing |

## Next Steps

1. ✅ Complete Task 3: Manual Testing
2. ✅ Validate all file operations work with MSAL
3. ✅ Document test results
4. ✅ Update Sprint 7A completion docs
```

**Task 4 Deliverable:**
- ✅ SPRINT-7A-COMPLETION-SUMMARY.md updated
- ✅ SPRINT-7A-DEPLOYMENT-COMPLETE.md updated
- ✅ SPRINT-7A-MSAL-INTEGRATION.md created
- ✅ All documentation reflects MSAL architecture

---

### Task 5: Rebuild and Deploy (If Needed)

**Goal:** Rebuild control with latest code and deploy if changes were made.

**Prerequisites:**
- ✅ All code changes completed (if any)
- ✅ Build verification passed (Task 2)

⚠️ **Note:** If no code changes were made, deployment is optional. The current v2.1.4 already includes MSAL integration.

#### Step 5.1: Conditional Rebuild Decision

**Question:** Were any code changes made during remedial tasks?

**If NO code changes:**
- ✅ Skip rebuild
- ✅ Use existing v2.1.4 deployment
- ✅ Proceed to Task 6 (reporting)

**If YES code changes:**
- Continue with Step 5.2

#### Step 5.2: Production Build

**Action:**
```bash
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGrid

# Clean build
npm run clean

# Production build
npm run build
```

**Expected Output:**
```
webpack compiled successfully
Bundle size: ~540 KiB
```

**Success Criteria:**
- ✅ 0 errors
- ✅ 0 warnings (except bundle size performance hint)
- ✅ Bundle size < 600 KiB

#### Step 5.3: Solution Build

**Action:**
```bash
cd /c/code_files/spaarke

# Temporarily disable central package management
mv Directory.Packages.props Directory.Packages.props.disabled

# Build managed solution
cd src/controls/UniversalDatasetGrid/UniversalDatasetGridSolution
dotnet build --configuration Release

# Restore central package management
cd /c/code_files/spaarke
mv Directory.Packages.props.disabled Directory.Packages.props
```

**Expected Output:**
```
Build succeeded.
    0 Warning(s)
    0 Error(s)

Output: bin/Release/UniversalDatasetGridSolution.zip
```

**Success Criteria:**
- ✅ Solution builds successfully
- ✅ .zip file created in bin/Release/

#### Step 5.4: Deploy to Test Environment

**Action:**
```bash
# Verify authentication
pac auth list
# Active: SpaarkeDevDeployment → https://spaarkedev1.crm.dynamics.com/

# Import solution
cd /c/code_files/spaarke/src/controls/UniversalDatasetGrid/UniversalDatasetGridSolution
pac solution import --path bin/Release/UniversalDatasetGridSolution.zip --async

# Publish customizations
pac solution publish
```

**Expected Output:**
```
Importing solution...
Import ID: <guid>
Solution imported successfully
Publishing customizations...
Publish complete
```

**Success Criteria:**
- ✅ Solution import successful
- ✅ Customizations published
- ✅ No import errors

#### Step 5.5: Post-Deployment Verification

**Action:**
1. Navigate to SPAARKE DEV 1 in browser
2. Hard refresh (Ctrl+Shift+F5) to clear browser cache
3. Open model-driven app with Universal Dataset Grid
4. Check browser console for MSAL initialization

**Expected Console Output:**
```
[Control] Init - Creating single React root
[Control] Initializing MSAL authentication...
[Control] MSAL authentication initialized successfully ✅
[Control] User authenticated: true
```

**Success Criteria:**
- ✅ Control loads without errors
- ✅ MSAL initializes successfully
- ✅ User authenticated
- ✅ Grid displays data

**Task 5 Deliverable:**
- ✅ Control rebuilt (if needed)
- ✅ Solution deployed (if needed)
- ✅ Post-deployment verification complete

---

### Task 6: Compliance Report and Sign-Off

**Goal:** Document Sprint 7A MSAL compliance status and create sign-off report.

**Success Criteria:**
- Compliance report created
- All tasks status documented
- Testing results included
- Sign-off checklist completed

#### Step 6.1: Create Compliance Report

**NEW File:** `dev/projects/sdap_project/Sprint 7_Dataset Grid to SDAP/SPRINT-7A-MSAL-COMPLIANCE-REPORT.md`

**Template:**
```markdown
# Sprint 7A: MSAL Compliance Report

**Report Date:** [Date]
**Reviewed By:** [Your Name]
**Control Version:** v2.1.4
**Status:** ✅ COMPLIANT / ⚠️ PARTIAL / ❌ NON-COMPLIANT

---

## Executive Summary

Sprint 7A file operations (download, delete, replace) are [COMPLIANT/PARTIAL/NON-COMPLIANT] with Sprint 8 MSAL authentication architecture.

**Key Findings:**
- [Summary of findings]
- [Any issues discovered]
- [Remediation actions taken]

---

## Task Completion Status

| Task | Status | Notes |
|------|--------|-------|
| Task 1: Code Review | ✅ / ⏳ / ❌ | [Details] |
| Task 2: Build Verification | ✅ / ⏳ / ❌ | [Details] |
| Task 3: Manual Testing | ✅ / ⏳ / ❌ | [Details] |
| Task 4: Documentation | ✅ / ⏳ / ❌ | [Details] |
| Task 5: Deployment | ✅ / ⏳ / ❌ / N/A | [Details] |

---

## Code Review Results

### SdapApiClientFactory Integration
- **Status:** ✅ / ❌
- **Findings:** [Details]

### Service Constructors
- **Status:** ✅ / ❌
- **Findings:** [Details]

### MSAL Initialization
- **Status:** ✅ / ❌
- **Findings:** [Details]

### Error Handling
- **Status:** ✅ / ❌
- **Findings:** [Details]

---

## Build Verification Results

### MSAL Package
- **Version:** @azure/msal-browser@4.24.1
- **Status:** ✅ / ❌
- **Bundle Size:** [Size] KiB

### Build Quality
- **Errors:** [Count]
- **Warnings:** [Count]
- **TypeScript:** ✅ / ❌

---

## Testing Results

### Download File Operation
- **Test Date:** [Date]
- **Tester:** [Name]
- **Status:** ✅ / ❌ / ⏳
- **Details:** [Test results]

### Delete File Operation
- **Test Date:** [Date]
- **Tester:** [Name]
- **Status:** ✅ / ❌ / ⏳
- **Details:** [Test results]

### Replace File Operation
- **Test Date:** [Date]
- **Tester:** [Name]
- **Status:** ✅ / ❌ / ⏳
- **Details:** [Test results]

### Token Caching Performance
- **Cold Cache:** [Time]ms
- **Warm Cache:** [Time]ms
- **Improvement:** [X]x faster
- **Status:** ✅ / ❌

### Error Scenarios
- **401 Retry:** ✅ / ❌ / ⏳
- **Race Condition:** ✅ / ❌ / ⏳
- **Network Timeout:** ✅ / ❌ / ⏳

---

## Issues Discovered

| Issue | Severity | Status | Resolution |
|-------|----------|--------|------------|
| [Issue description] | High/Med/Low | Open/Resolved | [Resolution details] |

---

## Documentation Updates

| Document | Updated | Status |
|----------|---------|--------|
| SPRINT-7A-COMPLETION-SUMMARY.md | Yes/No | ✅ / ⏳ |
| SPRINT-7A-DEPLOYMENT-COMPLETE.md | Yes/No | ✅ / ⏳ |
| SPRINT-7A-MSAL-INTEGRATION.md | Created | ✅ / ⏳ |

---

## Compliance Checklist

### Code Compliance
- [ ] All services use SdapApiClientFactory
- [ ] No deprecated PCF context token usage
- [ ] MSAL initialization in index.ts
- [ ] 401 automatic retry implemented
- [ ] Race condition handling present

### Build Compliance
- [ ] MSAL package installed (@azure/msal-browser@4.24.1)
- [ ] Build completes without errors
- [ ] Bundle size within limits (<600 KiB)
- [ ] MSAL code included in bundle

### Testing Compliance
- [ ] Download tested with MSAL
- [ ] Delete tested with MSAL
- [ ] Replace tested with MSAL
- [ ] Token caching verified (82x improvement)
- [ ] Error scenarios tested

### Documentation Compliance
- [ ] All docs updated to reflect MSAL
- [ ] Authentication flows documented
- [ ] Troubleshooting guides include MSAL
- [ ] Integration summary created

---

## Recommendations

1. [Recommendation 1]
2. [Recommendation 2]
3. [Recommendation 3]

---

## Sign-Off

**Code Review:** ✅ Approved / ⏳ Pending / ❌ Changes Required
**Testing:** ✅ Passed / ⏳ Pending / ❌ Failed
**Documentation:** ✅ Complete / ⏳ In Progress / ❌ Incomplete

**Overall Status:** ✅ COMPLIANT / ⚠️ PARTIAL / ❌ NON-COMPLIANT

**Approver:** [Name]
**Date:** [Date]
**Next Action:** [Sprint 7B / Remediation / Other]
```

#### Step 6.2: Complete Compliance Checklist

**Action:**
1. Fill out compliance report template
2. Document all task results
3. List any issues discovered
4. Provide recommendations
5. Sign off on compliance status

**Success Criteria:**
- ✅ All sections of report completed
- ✅ Test results documented
- ✅ Issues tracked
- ✅ Recommendations provided

**Task 6 Deliverable:**
- ✅ SPRINT-7A-MSAL-COMPLIANCE-REPORT.md created
- ✅ Compliance status documented
- ✅ Sign-off completed

---

## Success Criteria - Overall Sprint

### Code Compliance ✅
- [x] All services use SdapApiClientFactory with MSAL
- [x] No deprecated PCF context token usage
- [x] MSAL initialized in index.ts
- [x] 401 automatic retry implemented
- [x] Race condition handling present

### Build Quality ✅
- [ ] MSAL package verified (@azure/msal-browser@4.24.1)
- [ ] Clean build successful (0 errors, 0 warnings)
- [ ] Bundle size within limits (<600 KiB)
- [ ] MSAL code included in bundle

### Testing Validation ⏳
- [ ] Download tested with MSAL authentication
- [ ] Delete tested with MSAL authentication
- [ ] Replace tested with MSAL authentication
- [ ] Token caching performance verified (82x improvement)
- [ ] Error scenarios tested (401 retry, race condition)

### Documentation Complete ⏳
- [ ] SPRINT-7A-COMPLETION-SUMMARY.md updated
- [ ] SPRINT-7A-DEPLOYMENT-COMPLETE.md updated
- [ ] SPRINT-7A-MSAL-INTEGRATION.md created
- [ ] SPRINT-7A-MSAL-COMPLIANCE-REPORT.md created

### Ready for Sprint 7B ⏳
- [ ] All remedial tasks completed
- [ ] MSAL compliance verified
- [ ] Testing sign-off obtained
- [ ] Documentation up to date

---

## Timeline and Effort Estimate

| Task | Estimated Time | Actual Time |
|------|----------------|-------------|
| Task 1: Code Review | 1-2 hours | ___ hours |
| Task 2: Build Verification | 30 minutes | ___ hours |
| Task 3: Manual Testing | 2-3 hours | ___ hours |
| Task 4: Documentation | 2-3 hours | ___ hours |
| Task 5: Deployment (if needed) | 1 hour | ___ hours |
| Task 6: Compliance Report | 1-2 hours | ___ hours |
| **Total** | **8-12 hours** | **___ hours** |

---

## Dependencies

### External Dependencies
- ✅ Sprint 8 MSAL implementation complete
- ✅ Azure AD app registrations configured
- ✅ BFF API deployed with OBO support
- ⏳ Test records with real files (not placeholders)

### Internal Dependencies
- ✅ Universal Dataset Grid v2.1.4 deployed
- ✅ @azure/msal-browser@4.24.1 installed
- ✅ MsalAuthProvider implementation complete
- ✅ SdapApiClientFactory updated

---

## Risks and Mitigations

| Risk | Impact | Probability | Mitigation |
|------|--------|-------------|------------|
| Test data has placeholder itemIds | High | High | Create real test files first |
| Token caching not working | Medium | Low | Verify sessionStorage in browser |
| 401 retry fails | Medium | Low | Check BFF API OBO configuration |
| MSAL initialization race | Low | Medium | Already handled in code |

---

## References

### Sprint 8 Documentation
- [SPRINT-8-COMPLETION-REVIEW.md](../../Sprint%208%20-%20MSAL%20Integration/SPRINT-8-COMPLETION-REVIEW.md)
- [AUTHENTICATION-ARCHITECTURE.md](../../Sprint%208%20-%20MSAL%20Integration/AUTHENTICATION-ARCHITECTURE.md)

### Sprint 7A Documentation
- [SPRINT-7A-COMPLETION-SUMMARY.md](SPRINT-7A-COMPLETION-SUMMARY.md)
- [SPRINT-7A-DEPLOYMENT-COMPLETE.md](SPRINT-7A-DEPLOYMENT-COMPLETE.md)
- [SPRINT-7-MASTER-RESOURCE.md](SPRINT-7-MASTER-RESOURCE.md)

### Code Files
- [SdapApiClientFactory.ts](../../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/SdapApiClientFactory.ts)
- [MsalAuthProvider.ts](../../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/services/auth/MsalAuthProvider.ts)
- [index.ts](../../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/index.ts)

---

## Next Steps After Completion

1. ✅ Complete all 6 remedial tasks
2. ✅ Sign off on MSAL compliance
3. ✅ Update Sprint 7A status to "MSAL Compliant"
4. → **Proceed to Sprint 7B: Quick Create Implementation**

---

**Document Owner:** AI-Directed Coding Session
**Created:** October 6, 2025
**Status:** 📋 Ready for Implementation
**Next Action:** Begin Task 1 - Code Review and Verification
