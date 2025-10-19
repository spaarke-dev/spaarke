# Sprint 7A - Task 3: Manual Testing - File Operations

**Task:** Manual Testing - File Operations with MSAL
**Status:** ⚠️ BLOCKED - Requires real test files
**Estimated Time:** 2-3 hours
**Prerequisites:** Task 2 completed, **real test files in SharePoint Embedded**

---

## ⚠️ IMPORTANT: Test Data Requirement

**BLOCKER:** You need test records with **real files** uploaded to SharePoint Embedded.

**Current Issue:**
```
Test records have placeholder itemIds:
sprk_graphitemid = "01PLACEHOLDER123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ"

API will return: 404 Not Found
```

**Resolution Options:**
1. **Wait for Sprint 7B** - Use Quick Create to upload test files (RECOMMENDED)
2. Upload files manually via SharePoint UI
3. Use Graph Explorer to upload test files via API

**If you don't have real test files, SKIP THIS TASK and proceed to Sprint 7B.**

---

## Goal

Test all Sprint 7A file operations (download, delete, replace) with MSAL authentication using real user sessions.

## Success Criteria

- [ ] Download tested with MSAL tokens (works end-to-end)
- [ ] Delete tested with MSAL tokens (works end-to-end)
- [ ] Replace tested with MSAL tokens (works end-to-end)
- [ ] Token caching verified (82x performance improvement)
- [ ] Error scenarios tested (401 retry, race condition)

---

## Environment Setup

### Prerequisites

- [ ] Control built and deployed (v2.1.4)
- [ ] Test environment: SPAARKE DEV 1 (`https://spaarkedev1.crm.dynamics.com`)
- [ ] Test records with **real files** (not placeholders)
- [ ] Browser DevTools ready (F12)

### Setup Steps

1. Navigate to SPAARKE DEV 1 environment
2. Open a model-driven app with Universal Dataset Grid
3. Navigate to a view/form showing `sprk_document` entity
4. Open browser DevTools console (F12)
5. Clear browser cache (Ctrl+Shift+Delete)

### Verify MSAL Initialization

**Expected Console Output on Page Load:**
```
[Control] Init - Creating single React root
[Control] Initializing MSAL authentication...
[Control] MSAL authentication initialized successfully ✅
[Control] User authenticated: true
[Control] Account info: { username: "your.name@spaarke.com", ... }
```

### Success Criteria

- [ ] MSAL initializes without errors
- [ ] User is authenticated
- [ ] No console errors related to MSAL

### If MSAL Fails to Initialize

Check MSAL state in console:
```javascript
// Check initialization state
window.sessionStorage.getItem('msal.initialized')
// Should return: "true"

// Check for tokens
Object.keys(window.sessionStorage).filter(k => k.includes('msal.token'))
// Should show token cache keys
```

---

## Test 3.1: File Download with MSAL

### Test Case: Single File Download

**Prerequisites:**
- [ ] Select a record with `sprk_hasfile = true`
- [ ] Verify record has valid `sprk_graphdriveid` and `sprk_graphitemid`
- [ ] Verify itemId is NOT a placeholder (should start with "01" and be real)

### Steps

1. Select one record with a file
2. Click **Download** button
3. Monitor browser console
4. Verify download completes

### Expected Console Output

**First Download (Cold Cache):**
```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Getting token for scopes: api://1e40baad-e065-4aea-a8d4-4b7ab273458c/user_impersonation
[MsalAuthProvider] Token not in cache, acquiring via ssoSilent
[MsalAuthProvider] Token acquired in 420ms
[MsalAuthProvider] Token cached with expiry: 2025-10-06T15:30:00.000Z
[SdapApiClient] Downloading file
[SdapApiClient] File downloaded successfully: { size: 2458624, type: "application/pdf" }
[FileDownloadService] Download triggered successfully: Contract.pdf
```

**Second Download (Warm Cache - within 1 hour):**
```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Token retrieved from cache (5ms)
[SdapApiClient] Downloading file
[SdapApiClient] File downloaded successfully
[FileDownloadService] Download triggered successfully
```

### Expected User Experience

- [ ] Browser download dialog appears
- [ ] File downloads with correct filename
- [ ] File opens successfully in appropriate application
- [ ] File content is valid (not corrupted)

### Success Criteria

- [ ] Token acquired via MSAL (cache or ssoSilent)
- [ ] First request: ~420ms token acquisition
- [ ] Second request: ~5ms token acquisition (82x faster!)
- [ ] Download completes successfully
- [ ] File is valid and not corrupted
- [ ] No console errors

### Test Results

**First Download:**
- Token Acquisition Time: ______ ms
- Download Status: ✅ / ❌
- File Valid: ✅ / ❌

**Second Download:**
- Token Acquisition Time: ______ ms (should be ~5ms)
- Download Status: ✅ / ❌
- Performance Improvement: ______ x faster

---

## Test 3.2: File Delete with MSAL

### Test Case: Single File Delete with Confirmation

**Prerequisites:**
- [ ] Select a record with `sprk_hasfile = true`
- [ ] Record has valid driveId and itemId
- [ ] Prepared to lose this file (it will be deleted!)

### Steps

1. Select one record with a file
2. Click **Remove File** button
3. Verify confirmation dialog appears with filename
4. Click **Delete** button
5. Monitor browser console
6. Verify file is deleted

### Expected Console Output

```
[SdapApiClientFactory] Retrieving access token via MSAL
[MsalAuthProvider] Token retrieved from cache (5ms)
[SdapApiClient] Deleting file: driveId=..., itemId=...
[SdapApiClient] File deleted successfully
[FileDeleteService] Updating Dataverse record: hasFile = false
[FileDeleteService] File deleted successfully
```

### Expected User Experience

- [ ] Confirmation dialog shows filename
- [ ] Cancel button works (closes dialog, no action)
- [ ] Delete button triggers delete operation
- [ ] Grid refreshes automatically
- [ ] Record remains in grid with `hasFile = false`

### Expected Dataverse Changes

After deletion, record should show:
```
sprk_hasfile = false
sprk_graphitemid = null
sprk_filename = null
sprk_filesize = null
sprk_filepath = null
sprk_createddatetime = null
sprk_lastmodifieddatetime = null
sprk_etag = null
sprk_parentfolderid = null
```

### Success Criteria

- [ ] Token acquired via MSAL cache (~5ms)
- [ ] Confirmation dialog works correctly
- [ ] File deleted from SharePoint Embedded
- [ ] Dataverse record updated correctly (hasFile = false)
- [ ] All file metadata fields cleared
- [ ] Grid refreshes automatically
- [ ] No console errors

### Test Results

- Confirmation Dialog: ✅ / ❌
- Token Acquisition: ✅ / ❌ (______ ms)
- SharePoint Delete: ✅ / ❌
- Dataverse Update: ✅ / ❌
- Grid Refresh: ✅ / ❌

---

## Test 3.3: File Replace with MSAL

### Test Case: Replace Existing File

**Prerequisites:**
- [ ] Select a record with `sprk_hasfile = true`
- [ ] Record has valid driveId and itemId
- [ ] Have a replacement file ready on your computer

### Steps

1. Select one record with a file
2. Click **Update File** button
3. File picker opens - select a new file
4. Monitor browser console
5. Verify file is replaced

### Expected Console Output

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

### Expected User Experience

- [ ] File picker opens
- [ ] After selection, upload begins
- [ ] Grid refreshes with new file metadata
- [ ] SharePoint link points to new file
- [ ] New file can be downloaded

### Expected Dataverse Changes

After replacement, record should show:
```
sprk_filename = "NewContract.pdf" (updated)
sprk_filesize = 1234567 (updated)
sprk_graphitemid = "01NEWID..." (updated - different from old)
sprk_filepath = "https://..." (updated)
sprk_createddatetime = <new timestamp> (updated)
sprk_lastmodifieddatetime = <new timestamp> (updated)
sprk_etag = "<new version>" (updated)
sprk_hasfile = true (unchanged)
```

### Success Criteria

- [ ] Token acquired via MSAL cache (~5ms)
- [ ] File picker works correctly
- [ ] Old file deleted from SharePoint
- [ ] New file uploaded to SharePoint
- [ ] New file gets new itemId (different from old)
- [ ] Dataverse record updated with new metadata
- [ ] Grid refreshes automatically
- [ ] SharePoint link works for new file
- [ ] No console errors

### Test Results

- File Picker: ✅ / ❌
- Token Acquisition: ✅ / ❌ (______ ms)
- Old File Delete: ✅ / ❌
- New File Upload: ✅ / ❌
- Dataverse Update: ✅ / ❌
- New File Valid: ✅ / ❌

---

## Test 3.4: Error Scenarios

### Test 3.4a: Token Expiration (401 Retry)

**Setup:**
Option 1: Wait 55+ minutes for token to expire
Option 2: Manually clear cache in console:
```javascript
MsalAuthProvider.getInstance().clearCache();
```

**Steps:**
1. Clear MSAL cache (if using Option 2)
2. Try to download a file
3. Monitor console for automatic retry

**Expected Console Output:**
```
[SdapApiClient] Request failed with 401 Unauthorized
[SdapApiClient] 401 Unauthorized - clearing cache and retrying
[MsalAuthProvider] Cache cleared
[MsalAuthProvider] Acquiring fresh token via ssoSilent
[SdapApiClient] Retrying with fresh token
[SdapApiClient] Download successful on retry
```

**Success Criteria:**
- [ ] 401 error handled automatically (no user action needed)
- [ ] Cache cleared on 401
- [ ] Fresh token acquired via ssoSilent
- [ ] Retry succeeds without user intervention
- [ ] Download completes successfully

### Test 3.4b: MSAL Initialization Race Condition

**Setup:**
1. Refresh page
2. **Immediately** click Download button (before MSAL initialization completes)

**Expected Console Output:**
```
[Control] Initializing MSAL authentication...
[SdapApiClientFactory] Retrieving access token via MSAL
[SdapApiClientFactory] MSAL not yet initialized, waiting...
[Control] MSAL authentication initialized successfully ✅
[SdapApiClientFactory] MSAL initialization complete
[MsalAuthProvider] Getting token...
[SdapApiClient] Downloading file...
```

**Success Criteria:**
- [ ] Factory waits for initialization
- [ ] No "MSAL not initialized" errors
- [ ] Token acquisition succeeds after wait
- [ ] Operation completes successfully

### Test 3.4c: Network Timeout (Optional)

**Setup:**
1. Open DevTools Network tab
2. Throttle to "Slow 3G" or "Offline"

**Steps:**
1. Try to download a file
2. Monitor console

**Expected Console Output:**
```
[SdapApiClient] Request timeout after 300000ms
[FileDownloadService] Download failed: Request timeout
```

**Success Criteria:**
- [ ] Timeout handled gracefully (no crash)
- [ ] User-friendly error message
- [ ] No unhandled promise rejection

---

## Task 3 Completion Checklist

### File Operations Tested

- [ ] Test 3.1: Download tested with MSAL ✅ / ❌
- [ ] Test 3.2: Delete tested with MSAL ✅ / ❌
- [ ] Test 3.3: Replace tested with MSAL ✅ / ❌

### Error Scenarios Tested

- [ ] Test 3.4a: 401 retry tested ✅ / ❌
- [ ] Test 3.4b: Race condition tested ✅ / ❌
- [ ] Test 3.4c: Network timeout tested (optional) ✅ / ❌

### Performance Validation

- [ ] Token caching verified (82x improvement)
- [ ] First request: ~420ms (ssoSilent)
- [ ] Second request: ~5ms (cache hit)

### Overall Test Results

**Download:**
- Status: ✅ Pass / ❌ Fail / ⏳ Not Tested
- Issues: _________________________

**Delete:**
- Status: ✅ Pass / ❌ Fail / ⏳ Not Tested
- Issues: _________________________

**Replace:**
- Status: ✅ Pass / ❌ Fail / ⏳ Not Tested
- Issues: _________________________

**Error Handling:**
- Status: ✅ Pass / ❌ Fail / ⏳ Not Tested
- Issues: _________________________

---

## Expected Outcome

✅ **All tests should pass** - MSAL authentication is working correctly from Sprint 8.

⚠️ **If you can't test:** No real test files available - SKIP and proceed to Sprint 7B.

---

## Next Steps

After completing Task 3:
- → **If all tests pass:** Proceed to [TASK-7A.4-DOCUMENTATION-UPDATES.md](TASK-7A.4-DOCUMENTATION-UPDATES.md)
- → **If tests fail:** Document failures and troubleshoot
- → **If can't test (no real files):** SKIP to Sprint 7B

---

## Quick Troubleshooting

### Downloads return 404
- Check if itemId is placeholder: `01PLACEHOLDER...`
- Verify file exists in SharePoint Embedded
- Check driveId is correct

### MSAL errors in console
- Verify Azure AD app registration
- Check user has permissions
- Try incognito mode

### 401 errors not retrying
- Verify `fetchWithTimeout()` has retry logic
- Check MsalAuthProvider.clearCache() is called

---

**Task Owner:** Sprint 7A MSAL Compliance
**Created:** October 6, 2025
**Estimated Completion:** 2-3 hours (if test data available)
**Status:** ⚠️ BLOCKED - Requires real test files
**Previous Task:** [TASK-7A.2-BUILD-VERIFICATION.md](TASK-7A.2-BUILD-VERIFICATION.md)
**Next Task:** [TASK-7A.4-DOCUMENTATION-UPDATES.md](TASK-7A.4-DOCUMENTATION-UPDATES.md)
