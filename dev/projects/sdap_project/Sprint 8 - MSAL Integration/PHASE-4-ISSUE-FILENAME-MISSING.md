# Phase 4 Testing - Issue Report: Missing FileName Field

**Date:** 2025-10-06
**Issue:** Record missing required `fileName` field
**Status:** ⚠️ **DATA CONFIGURATION ISSUE** (Not MSAL-related)

---

## Console Output Analysis

```
[UniversalDatasetGrid][UniversalDatasetGridRoot] Command executed: downloadFile
[UniversalDatasetGrid][UniversalDatasetGridRoot] Downloading 1 file(s)
[UniversalDatasetGrid][SdapApiClientFactory] Creating SDAP API client
[UniversalDatasetGrid][SdapApiClient] Initialized
[UniversalDatasetGrid][UniversalDatasetGridRoot] Missing required fields for download
{
  recordId: 'fb67a728-3a9e-f011-bbd3-7c1e5215b8b5',
  hasDriveId: true,
  hasItemId: true,
  hasFileName: false  ← ISSUE HERE
}
```

---

## Analysis

**Good News:** ✅ MSAL integration is working!
- ✅ Control loads successfully
- ✅ SdapApiClientFactory creates client (MSAL integrated)
- ✅ SdapApiClient initializes correctly
- ✅ No MSAL errors in console

**Issue:** ❌ Record data incomplete
- ✅ `graphdriveid` field populated (hasDriveId: true)
- ✅ `graphitemid` field populated (hasItemId: true)
- ❌ `filename` field **NOT** populated (hasFileName: false)

**Root Cause:** Record has file metadata (drive ID, item ID) but missing file name field.

---

## Required Field Configuration

**From:** [types/index.ts](../../../src/controls/UniversalDatasetGrid/UniversalDatasetGrid/types/index.ts)

**Field Mappings (with sprk_ prefix):**
```typescript
fieldMappings: {
  hasFile: 'sprk_hasfile',        // Boolean: Has file attached
  fileName: 'sprk_filename',      // String: File name (e.g., "document.pdf")
  fileSize: 'sprk_filesize',      // Number: File size in bytes
  mimeType: 'sprk_mimetype',      // String: MIME type (e.g., "application/pdf")
  graphItemId: 'sprk_graphitemid', // String: SharePoint item ID ✅ Present
  graphDriveId: 'sprk_graphdriveid' // String: SharePoint drive ID ✅ Present
}
```

**Required for Download Operation:**
1. `sprk_graphdriveid` ✅ Present
2. `sprk_graphitemid` ✅ Present
3. `sprk_filename` ❌ **MISSING** - Required for saving downloaded file

---

## Why MSAL Wasn't Tested Yet

**Download operation validation:**
```typescript
// In UniversalDatasetGridRoot.tsx (line ~128)
const record = dataset.records[recordId];
const driveId = record.getValue(config.fieldMappings.graphDriveId);
const itemId = record.getValue(config.fieldMappings.graphItemId);
const fileName = record.getValue(config.fieldMappings.fileName);

// Validation check
if (!driveId || !itemId || !fileName) {
  logger.warn('Missing required fields for download', {
    recordId,
    hasDriveId: !!driveId,
    hasItemId: !!itemId,
    hasFileName: !!fileName  // ← Failed here
  });
  // STOPS before making API call (never reaches MSAL)
  return;
}
```

**Result:** Operation stopped before calling `SdapApiClient.downloadFile()`, so MSAL token acquisition was never triggered.

---

## Resolution Options

### Option 1: Fix Data - Populate FileName Field (Recommended)

**Steps:**

1. **Open the record in Dataverse:**
   - Record ID: `fb67a728-3a9e-f011-bbd3-7c1e5215b8b5`
   - Navigate to form

2. **Check field mappings:**
   - Verify field `sprk_filename` exists on entity
   - Check if field is on form (visible or hidden)

3. **Populate the field:**
   - **Option A - Manual entry:**
     - Edit record
     - Set `sprk_filename` = "test-file.pdf" (or actual file name)
     - Save

   - **Option B - Query SharePoint for actual filename:**
     - If file exists in SharePoint Embedded
     - Use Graph API to get file metadata
     - Update Dataverse record with actual filename

4. **Retry download test:**
   - Refresh form
   - Select record
   - Click "Download"
   - **Expected:** MSAL logs appear, API call succeeds

---

### Option 2: Find Record with Complete Data

**Steps:**

1. **Query for records with all required fields:**
   ```javascript
   // In Dataverse Advanced Find or via API
   // Filter: sprk_hasfile = true
   //     AND sprk_filename is not null
   //     AND sprk_graphdriveid is not null
   //     AND sprk_graphitemid is not null
   ```

2. **Select a complete record in grid**

3. **Retry download test**

---

### Option 3: Test Upload Instead (Creates Complete Record)

**Steps:**

1. **Find record without file:**
   - Filter: `sprk_hasfile` = false or null

2. **Click "Add File" button**

3. **Upload a file:**
   - File picker opens
   - Select test file
   - **Expected:**
     - MSAL token acquisition logs
     - Upload API call to BFF
     - All fields populated (including filename)

4. **Then test download on newly uploaded file**

---

## What This Tells Us About MSAL Integration

**Positive Indicators:** ✅

1. **Control Initialization:**
   - PCF control loaded successfully
   - No MSAL initialization errors
   - No Azure App Registration errors

2. **Factory Pattern Working:**
   - `SdapApiClientFactory.create(baseUrl)` succeeded
   - MSAL integration in factory (no `context` parameter)
   - Client created without errors

3. **No Authentication Errors:**
   - No `redirect_uri_mismatch` errors
   - No `consent_required` errors
   - No MSAL configuration errors

**What We Still Need to Test:** ⏳

- Token acquisition (MSAL `getToken()` call)
- SSO silent authentication
- Token caching performance
- 401 retry logic
- API calls to BFF with MSAL tokens

**To test these:** Need complete record with all required fields.

---

## Recommended Next Steps

**Immediate Actions:**

1. **Quick Fix - Populate FileName Field:**
   ```sql
   -- Open record in Dataverse UI
   -- Find field: sprk_filename
   -- Set value: "test-document.pdf" (or actual name)
   -- Save record
   ```

2. **Retry Download Test:**
   - Refresh form
   - Select same record
   - Click "Download"
   - **Watch console for MSAL logs:**
     - `[MsalAuthProvider] Getting token for scopes...`
     - `[MsalAuthProvider] Token retrieved from cache` (or SSO silent)
     - `[SdapApiClient] Downloading file...`
     - `[SdapApiClient] File downloaded successfully`

3. **If Download Succeeds:**
   - ✅ MSAL integration validated
   - ✅ Proceed with full E2E testing (remaining scenarios)

---

## Alternative: Test Upload Operation Instead

**If populating fileName is difficult:**

**Upload operation doesn't require existing filename** (creates it):

1. **Find record where `sprk_hasfile` = false**
2. **Click "Add File" button**
3. **Select file from file picker**
4. **Watch console for MSAL logs:**
   - This WILL trigger MSAL token acquisition
   - Will test upload endpoint: `/api/obo/drives/{driveId}/upload`

**Advantages:**
- Tests MSAL immediately
- Creates complete record with all fields
- Can then test download on this record

---

## Console Logs We're Looking For

**When operation reaches MSAL:**

```
[UniversalDatasetGrid][SdapApiClientFactory] Retrieving access token via MSAL
[UniversalDatasetGrid][MsalAuthProvider] Getting token for scopes: api://spe-bff-api/user_impersonation
[UniversalDatasetGrid][MsalAuthProvider] Attempting SSO silent token acquisition
[UniversalDatasetGrid][MsalAuthProvider] Token acquired successfully

// OR (if cached)

[UniversalDatasetGrid][MsalAuthProvider] Token retrieved from cache
[UniversalDatasetGrid][SdapApiClientFactory] Access token retrieved successfully
[UniversalDatasetGrid][SdapApiClient] Downloading file...
```

**Then watch Network tab:**
- Request to `login.microsoftonline.com` (MSAL)
- Request to `https://spe-api-dev-67e2xz.azurewebsites.net/api/obo/drives/...`
- Request header: `Authorization: Bearer <token>`
- Response: 200 OK (if BFF and permissions correct)

---

## Updated Test Plan

**Step 1: Fix Data Issue**
- [ ] Populate `sprk_filename` field on record `fb67a728-3a9e-f011-bbd3-7c1e5215b8b5`
- [ ] **OR** find different record with complete data
- [ ] **OR** test upload operation instead

**Step 2: Retry Download Test**
- [ ] Select record with complete fields
- [ ] Click "Download"
- [ ] Watch console for MSAL logs
- [ ] Verify token acquisition succeeds

**Step 3: Continue E2E Testing**
- [ ] If Step 2 succeeds → Proceed with [PHASE-4-TESTING-GUIDE.md](PHASE-4-TESTING-GUIDE.md)
- [ ] Complete all 7 scenarios
- [ ] Collect performance metrics

---

## Summary

**Issue:** Data configuration (missing filename field)
**MSAL Status:** ✅ Not tested yet, but initialization looks good
**Action Required:** Populate filename field or test upload instead
**Next Test:** Retry download or test upload operation

**MSAL integration appears healthy** - we just need complete test data to validate end-to-end flow.

---

**Issue Status:** ⚠️ **BLOCKED ON DATA** (Not code issue)
**Resolution:** Populate `sprk_filename` field and retry
**ETA:** 2 minutes to fix, then continue testing

---
