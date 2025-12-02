# Work Item 7: End-to-End Testing

**Estimated Time:** 2-3 hours
**Prerequisites:** Work Item 6 complete (Quick Create form configured)
**Status:** Ready to Start

---

## Objective

Perform comprehensive end-to-end testing of the SPE File Upload control in Quick Create forms.

---

## Context

Testing ensures:
- File upload works correctly
- Multi-file upload works
- Error scenarios are handled gracefully
- Container ID detection works
- Document records are created correctly
- SPE metadata is stored properly

---

## Test Environment Setup

### Prerequisites

Before testing, verify:

1. ✅ Solution deployed to test environment
2. ✅ Quick Create form configured for Document entity
3. ✅ Test Matter record exists with valid Container ID
4. ✅ SDAP API is running and accessible
5. ✅ Test user has appropriate permissions:
   - Create permission on Document entity
   - Write permission on SharePoint container
   - Read permission on Matter entity

### Test Data

Create test Matter record:

**Matter Record:**
- **Name:** Test Matter - File Upload
- **Matter Number:** TM-2025-001
- **Container ID:** [Valid SharePoint container GUID]
- **Owner:** Test User

**Verify Container ID:**
```bash
# Test Container ID is valid
curl https://your-api.azurewebsites.net/api/spe/containers/{containerId}
# Should return 200 OK
```

---

## Test Cases

### Test Case 1: Single File Upload - Success

**Objective:** Verify single file upload works correctly

**Steps:**
1. Navigate to Test Matter record
2. Scroll to **Documents** subgrid
3. Click **+ New** button
4. Quick Create form opens
5. Verify Container ID is displayed (from parent Matter)
6. Enter Document Title: "Test Contract"
7. Click **Choose Files**
8. Select 1 PDF file (e.g., "contract.pdf", ~2 MB)
9. Wait for upload to complete
10. Verify file appears in "Uploaded Files (1)" section
11. Click **Save**
12. Wait for form to close

**Expected Results:**
- ✅ Quick Create form opens in modal
- ✅ Container ID displayed: "abc-123-def-456"
- ✅ File picker opens when clicking "Choose Files"
- ✅ Upload progress shown: "Uploading 1 of 1 files..."
- ✅ File appears in uploaded list: "✅ contract.pdf (2.0 MB)"
- ✅ Form closes after save
- ✅ New Document record appears in subgrid
- ✅ Document Title: "Test Contract"

**Verify in Dataverse:**
1. Open created Document record
2. Check `sprk_fileuploadmetadata` field contains JSON:
   ```json
   [
       {
           "driveItemId": "01ABC...",
           "fileName": "contract.pdf",
           "fileSize": 2097152,
           "sharePointUrl": "https://...",
           "webUrl": "https://...",
           "createdDateTime": "2025-10-07T12:00:00Z",
           "lastModifiedDateTime": "2025-10-07T12:00:00Z"
       }
   ]
   ```

**Verify in SharePoint Embedded:**
1. Open Container in SharePoint
2. Navigate to Documents library
3. Verify "contract.pdf" file exists
4. File size: 2.0 MB
5. File content matches original

**Browser Console Logs:**
```
[FileUploadPCF] Initializing SPE File Upload control
[FileUploadPCF] Container ID loaded successfully: { containerId: "abc-123..." }
[FileUploadPCF] Files selected for upload: { fileCount: 1 }
[FileUploadPCF] Uploading file 1/1: { fileName: "contract.pdf" }
[FileUploadService] File uploaded successfully: { driveItemId: "01ABC..." }
[FileUploadPCF] Upload complete: { successCount: 1, failCount: 0 }
[FileUploadPCF] getOutputs() called: { metadataCount: 1 }
```

**Pass Criteria:**
- ✅ File uploads successfully
- ✅ Document record created
- ✅ Metadata stored correctly
- ✅ File exists in SharePoint
- ✅ No errors in browser console

---

### Test Case 2: Multi-File Upload - Success

**Objective:** Verify multiple files can be uploaded at once

**Steps:**
1. Navigate to Test Matter record
2. Click **+ New** in Documents subgrid
3. Enter Document Title: "Test Multiple Files"
4. Click **Choose Files**
5. Select 3 files (hold Ctrl/Cmd):
   - contract.pdf (~2 MB)
   - invoice.pdf (~1 MB)
   - receipt.pdf (~500 KB)
6. Wait for all files to upload
7. Verify all 3 files appear in uploaded list
8. Click **Save**

**Expected Results:**
- ✅ File picker allows multiple selection
- ✅ Upload progress shows: "Uploading 1 of 3...", "2 of 3...", "3 of 3..."
- ✅ Progress bar fills to 100%
- ✅ All 3 files appear in "Uploaded Files (3)" section
- ✅ Form saves successfully
- ✅ Single Document record created

**Verify Metadata:**
```json
[
    { "driveItemId": "01ABC...", "fileName": "contract.pdf", "fileSize": 2097152 },
    { "driveItemId": "01DEF...", "fileName": "invoice.pdf", "fileSize": 1048576 },
    { "driveItemId": "01GHI...", "fileName": "receipt.pdf", "fileSize": 512000 }
]
```

**Verify SharePoint:**
- All 3 files exist in SharePoint container
- Correct file names and sizes

**Pass Criteria:**
- ✅ All 3 files upload successfully
- ✅ Single Document record with 3 files' metadata
- ✅ All files exist in SharePoint
- ✅ No errors

---

### Test Case 3: Drag-and-Drop File Upload

**Objective:** Verify drag-and-drop functionality works

**Steps:**
1. Open Quick Create form
2. Open File Explorer (separate window)
3. Select 1 file (contract.pdf)
4. Drag file over drop zone
5. Verify drop zone highlights (blue background)
6. Drop file
7. Verify file uploads automatically
8. Click **Save**

**Expected Results:**
- ✅ Drop zone highlights when dragging over
- ✅ Drop zone accepts file on drop
- ✅ File uploads automatically (no need to click button)
- ✅ File appears in uploaded list
- ✅ Form saves successfully

**Pass Criteria:**
- ✅ Drag-and-drop works
- ✅ Visual feedback (highlight)
- ✅ Automatic upload
- ✅ No errors

---

### Test Case 4: Large File Upload

**Objective:** Verify large files can be uploaded

**Steps:**
1. Open Quick Create form
2. Select large file (e.g., 50 MB PDF)
3. Wait for upload (may take 30-60 seconds)
4. Verify upload completes
5. Click **Save**

**Expected Results:**
- ✅ Large file uploads successfully
- ✅ Upload progress shown
- ✅ No timeout errors
- ✅ File stored in SharePoint

**Pass Criteria:**
- ✅ File < API limit (e.g., 100 MB) uploads successfully
- ✅ File > API limit shows error message

**Note:** Check SDAP API file size limit before testing.

---

### Test Case 5: No Container ID - Error Handling

**Objective:** Verify error handling when Container ID is missing

**Steps:**
1. Create new Matter record WITHOUT Container ID
2. Open Quick Create form from this Matter
3. Verify warning message shows
4. Attempt to select file
5. Verify file picker is disabled

**Expected Results:**
- ✅ Warning message: "No Container ID found. Please ensure the parent Matter has a valid SharePoint container."
- ✅ Drop zone is grayed out
- ✅ "Choose Files" button is disabled
- ✅ Cannot select files

**Browser Console:**
```
[FileUploadPCF] Parent record has no Container ID
```

**Pass Criteria:**
- ✅ Clear warning message
- ✅ UI disabled (cannot select files)
- ✅ No JavaScript errors

---

### Test Case 6: Network Error During Upload

**Objective:** Verify error handling when network fails

**Steps:**
1. Open Quick Create form
2. Select 3 files
3. Start upload
4. During upload, disconnect network (turn off Wi-Fi)
5. Wait for error
6. Reconnect network
7. Retry upload

**Expected Results:**
- ✅ Error message: "File upload failed: Network error"
- ✅ Partial success possible (files before disconnect succeed)
- ✅ Can retry after reconnecting

**Browser Console:**
```
[FileUploadService] File upload failed: Network error
[FileUploadPCF] Upload complete: { successCount: 1, failCount: 2 }
```

**Pass Criteria:**
- ✅ Error message shown
- ✅ Partial success supported
- ✅ Can retry without refreshing page

---

### Test Case 7: Invalid SDAP API URL

**Objective:** Verify error handling when API is unreachable

**Steps:**
1. Update form control parameter: `sdapApiBaseUrl` to invalid URL
2. Publish form
3. Open Quick Create form
4. Select file
5. Verify error message

**Expected Results:**
- ✅ Error message: "File upload failed: API not reachable"
- ✅ Clear error in browser console

**Browser Console:**
```
[SdapApiClient] API request failed: 404 Not Found
[FileUploadService] File upload failed
```

**Pass Criteria:**
- ✅ Error message shown
- ✅ No unhandled exceptions
- ✅ Form remains usable

---

### Test Case 8: Authentication Failure (401)

**Objective:** Verify error handling when MSAL token is invalid

**Steps:**
1. Clear browser cookies/storage (to clear cached token)
2. Open Quick Create form
3. Select file
4. If MSAL prompt appears, click Cancel
5. Verify error message

**Expected Results:**
- ✅ MSAL prompt may appear (popup or redirect)
- ✅ If user cancels, error message: "Authentication failed"
- ✅ If token expires, auto-retry with new token

**Browser Console:**
```
[MsalAuthProvider] Token acquisition failed
[FileUploadService] File upload failed: 401 Unauthorized
```

**Pass Criteria:**
- ✅ Authentication prompt appears when needed
- ✅ Error message if authentication fails
- ✅ Auto-retry with fresh token

---

### Test Case 9: Metadata Field Size Limit

**Objective:** Verify handling of 10,000 character limit

**Steps:**
1. Open Quick Create form
2. Select 30 files (simulate approaching limit)
3. Upload all files
4. Verify warning if approaching limit

**Expected Metadata Size:**
- Single file: ~250 characters
- 30 files: ~7,500 characters (safe)
- 50 files: ~12,500 characters (exceeds limit)

**Expected Results (30 files):**
- ✅ All files upload successfully
- ✅ No size limit error

**Expected Results (50 files):**
- ✅ Error message: "Too many files. Metadata exceeds 10,000 character limit."
- ✅ Upload stops or shows warning

**Pass Criteria:**
- ✅ Reasonable file count works (up to 30 files)
- ✅ Error message if limit exceeded
- ✅ No data truncation

---

### Test Case 10: Quick Create from Different Contexts

**Objective:** Verify control works from different parent entities

**Test 10A: From Matter Subgrid**
- ✅ Container ID detected from Matter
- ✅ File upload works

**Test 10B: From Standalone Quick Create**
- ✅ No parent context detected
- ✅ Warning message shown
- ✅ Manual Container ID entry required (if configured)

**Test 10C: From Account Subgrid (No Container ID)**
- ✅ Warning message shown
- ✅ File picker disabled

**Pass Criteria:**
- ✅ Control adapts to different contexts
- ✅ Clear messaging when Container ID unavailable

---

## Performance Testing

### Test Case 11: First Upload Performance

**Objective:** Measure performance of first file upload (includes MSAL token acquisition)

**Steps:**
1. Clear browser cache
2. Open Quick Create form
3. Select 1 small file (~100 KB)
4. Start upload
5. Measure time from file selection to upload complete

**Expected Time:**
- MSAL token acquisition: ~200-500 ms
- File upload (100 KB): ~500-1000 ms
- **Total:** ~1-2 seconds

**Pass Criteria:**
- ✅ First upload completes in < 5 seconds

---

### Test Case 12: Subsequent Upload Performance

**Objective:** Measure performance with cached MSAL token

**Steps:**
1. After Test Case 11, upload another file
2. Measure time from file selection to upload complete

**Expected Time:**
- MSAL token from cache: ~5 ms
- File upload (100 KB): ~500-1000 ms
- **Total:** ~0.5-1 seconds

**Pass Criteria:**
- ✅ Subsequent upload completes in < 2 seconds
- ✅ 2-10x faster than first upload

---

## Browser Compatibility Testing

Test in multiple browsers:

### Test Case 13: Chrome/Edge (Chromium)

**Steps:**
1. Run Test Cases 1-3 in Chrome
2. Verify all functionality works

**Pass Criteria:**
- ✅ All test cases pass

---

### Test Case 14: Firefox

**Steps:**
1. Run Test Cases 1-3 in Firefox
2. Verify all functionality works

**Pass Criteria:**
- ✅ All test cases pass

---

### Test Case 15: Safari (macOS only)

**Steps:**
1. Run Test Cases 1-3 in Safari
2. Verify all functionality works

**Pass Criteria:**
- ✅ All test cases pass

---

## Regression Testing

After any code changes, run core test cases:

### Smoke Test Suite (15 minutes)

**Required Tests:**
1. ✅ Test Case 1: Single file upload
2. ✅ Test Case 2: Multi-file upload
3. ✅ Test Case 5: No Container ID error

**Pass Criteria:**
- All 3 tests pass without errors

---

## Test Report Template

After completing testing, document results:

### Test Execution Summary

**Date:** YYYY-MM-DD
**Environment:** Test / Production
**Tested By:** [Name]

| Test Case | Status | Notes |
|-----------|--------|-------|
| TC1: Single file upload | ✅ Pass | |
| TC2: Multi-file upload | ✅ Pass | |
| TC3: Drag-and-drop | ✅ Pass | |
| TC4: Large file | ✅ Pass | |
| TC5: No Container ID | ✅ Pass | |
| TC6: Network error | ✅ Pass | |
| TC7: Invalid API URL | ✅ Pass | |
| TC8: Auth failure | ✅ Pass | |
| TC9: Field size limit | ✅ Pass | |
| TC10: Different contexts | ✅ Pass | |
| TC11: First upload perf | ✅ Pass | 1.8s |
| TC12: Cached upload perf | ✅ Pass | 0.7s |
| TC13: Chrome | ✅ Pass | |
| TC14: Firefox | ✅ Pass | |
| TC15: Safari | ⏭️ Skip | macOS not available |

**Overall Status:** ✅ Pass
**Pass Rate:** 14/14 (100%)
**Issues Found:** 0

---

## Known Issues / Limitations

Document any known issues:

1. **Parent context not always available in Quick Create**
   - **Workaround:** Manual Container ID entry
   - **Future Fix:** Backend plugin to auto-populate

2. **Field size limit (10,000 characters)**
   - **Workaround:** Limit to 30 files max
   - **Future Fix:** Use separate entity for metadata

3. **Sequential file upload (not parallel)**
   - **Impact:** Slower for many files
   - **Future Fix:** Implement parallel upload

---

## Next Steps

After completing testing:

1. ✅ All test cases pass
2. ✅ Document test results
3. ✅ Fix any issues found
4. ⏳ Move to Work Item 8: Documentation

---

**Status:** Ready for implementation
**Estimated Time:** 2-3 hours
**Next:** Work Item 8 - Documentation
