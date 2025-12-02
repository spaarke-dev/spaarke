# Work Item 9: End-to-End Testing

**Sprint:** 7B - Document Quick Create
**Estimated Time:** 4 hours
**Prerequisites:** Work Items 1-8 completed (full implementation and deployment)
**Status:** Ready to Start

---

## Objective

Perform comprehensive end-to-end testing of the Document Quick Create functionality. Test happy path, error scenarios, edge cases, and performance under various conditions.

---

## Context

Testing ensures:
1. User story requirements met (all 10 steps)
2. Multi-file upload works correctly
3. Document records created with SPE metadata
4. Button management functions properly
5. Error handling works
6. Performance acceptable
7. No regression in existing functionality

**Result:** Confidence in production readiness and identification of any issues.

---

## Test Environment Setup

### Prerequisites

- [ ] Solution deployed (Work Item 8)
- [ ] Quick Create form configured (Work Item 7)
- [ ] Test Matter record exists
- [ ] Test user has required permissions
- [ ] SDAP BFF API accessible
- [ ] SharePoint Embedded container configured
- [ ] Browser console open (F12) for monitoring

---

## Test Scenarios

### Scenario 1: Happy Path - Single File Upload

**Test Case 1.1: Upload Single Small File**

**Steps:**
1. Navigate to Matter record
2. Go to Documents tab
3. Click "+ New Document" on subgrid
4. Quick Create dialog opens
5. Verify PCF control visible
6. Click "+ Add File" button
7. Select one file (<1MB)
8. Verify file appears in list with name and size
9. Verify button text: "Save and Create 1 Document"
10. Verify button enabled (blue)
11. Click "Save and Create 1 Document"
12. Verify progress UI appears
13. Verify progress bar updates
14. Verify file status changes: pending → uploading → complete
15. Verify form closes automatically
16. Verify Document subgrid refreshes
17. Verify new record visible in subgrid
18. Open created Document record
19. Verify fields populated:
    - sprk_sharepointurl (SharePoint URL)
    - sprk_driveitemid (unique ID)
    - sprk_filename (original file name)
    - sprk_filesize (file size in bytes)
    - sprk_createddate (timestamp)
    - sprk_modifieddate (timestamp)

**Expected Result:** ✅ Single document created successfully with all metadata.

**Performance:** <3 seconds for <1MB file

---

### Scenario 2: Happy Path - Multiple Files (Sync-Parallel)

**Test Case 2.1: Upload 3 Small Files**

**Trigger sync-parallel strategy:** ≤3 files, each <10MB, total <20MB

**Steps:**
1. Open Quick Create
2. Select 3 files (each ~500KB)
3. Verify all 3 appear in file list
4. Verify button: "Save and Create 3 Documents"
5. Click button
6. Verify progress shows "Uploading 1 of 3..."
7. Watch progress update to 2 of 3, then 3 of 3
8. Verify all files show green checkmark
9. Verify form closes
10. Verify 3 new records in subgrid

**Expected Result:** ✅ All 3 documents created

**Performance:** ~3-4 seconds total (parallel upload)

---

### Scenario 3: Happy Path - Multiple Files (Long-Running)

**Test Case 3.1: Upload 5 Large Files**

**Trigger long-running strategy:** >3 files OR large files

**Steps:**
1. Open Quick Create
2. Select 5 files (each ~5MB)
3. Verify button: "Save and Create 5 Documents"
4. Click button
5. Verify batched upload (not all at once)
6. Verify progress updates sequentially
7. Verify button text updates: "Uploading 2 of 5..."
8. Watch all files complete
9. Verify 5 new records created

**Expected Result:** ✅ All 5 documents created

**Performance:** ~17-25 seconds (batched, sequential-safe)

---

### Scenario 4: Error Handling - Network Failure

**Test Case 4.1: SDAP API Unreachable**

**Setup:** Stop SDAP BFF API or use invalid URL

**Steps:**
1. Open Quick Create
2. Select 2 files
3. Click "Save and Create 2 Documents"
4. Verify upload starts
5. Verify network error occurs
6. Verify error message displayed
7. Verify files show red X icon
8. Verify form stays open (doesn't close)
9. Verify user can retry

**Expected Result:** ✅ Graceful error handling, user notified

---

### Scenario 5: Error Handling - Partial Failure

**Test Case 5.1: Some Files Fail**

**Setup:** Use mix of valid and invalid files (e.g., empty file)

**Steps:**
1. Select 3 files (1 valid, 1 empty, 1 valid)
2. Click upload
3. Verify some succeed, some fail
4. Verify summary: "2 of 3 files uploaded successfully"
5. Verify successful records created
6. Verify failed files show error message
7. Verify user can see which files failed

**Expected Result:** ✅ Partial success handled, clear feedback

---

### Scenario 6: Edge Cases

**Test Case 6.1: No Files Selected**

**Steps:**
1. Open Quick Create
2. Don't select any files
3. Verify button disabled
4. Verify button text: "Select Files to Continue"
5. Verify button gray (not clickable)

**Expected Result:** ✅ Button disabled, clear instruction

---

**Test Case 6.2: Remove All Files**

**Steps:**
1. Select 3 files
2. Remove all files one by one
3. Verify button becomes disabled again
4. Verify button text: "Select Files to Continue"

**Expected Result:** ✅ Button state updates correctly

---

**Test Case 6.3: Very Large File**

**Steps:**
1. Select file >50MB
2. Verify upload starts
3. Verify progress updates
4. Verify long-running strategy used
5. Verify successful upload

**Expected Result:** ✅ Large file handled correctly

**Performance:** Proportional to file size

---

**Test Case 6.4: Special Characters in File Name**

**Steps:**
1. Select file with name: "Test Document (Draft) - Version 2.0 [FINAL].pdf"
2. Upload file
3. Verify file name preserved in metadata
4. Verify record created

**Expected Result:** ✅ Special characters handled

---

**Test Case 6.5: Duplicate File Names**

**Steps:**
1. Select same file twice (or files with same name)
2. Upload
3. Verify both files uploaded
4. Verify separate records created
5. Verify unique DriveItemIds

**Expected Result:** ✅ Duplicates allowed, separate records

---

### Scenario 7: Form Integration

**Test Case 7.1: Form Data Included**

**Steps:**
1. Open Quick Create
2. Fill Title field: "Test Document"
3. Fill Description: "Test description"
4. Select file
5. Upload
6. Open created record
7. Verify Title and Description populated

**Expected Result:** ✅ Form data carried to record

---

**Test Case 7.2: Owner Field**

**Steps:**
1. Open Quick Create
2. Change Owner to different user
3. Select file
4. Upload
5. Verify Owner set correctly

**Expected Result:** ✅ Owner assignment works

---

### Scenario 8: Button Management

**Test Case 8.1: Standard Button Hidden**

**Steps:**
1. Open Quick Create
2. Inspect form footer
3. Verify standard "Save and Close" button NOT visible

**Expected Result:** ✅ Standard button hidden by CSS

---

**Test Case 8.2: Custom Button Location**

**Steps:**
1. Open Quick Create
2. Verify custom button in form footer (next to Cancel)
3. Verify button NOT inside PCF control area

**Expected Result:** ✅ Button in correct location

---

**Test Case 8.3: Button State Changes**

**Steps:**
1. Open Quick Create (no files)
   - Verify: "Select Files to Continue" (gray, disabled)
2. Select 1 file
   - Verify: "Save and Create 1 Document" (blue, enabled)
3. Select 2 more files
   - Verify: "Save and Create 3 Documents" (blue, enabled)
4. Click button
   - Verify: "Uploading 1 of 3..." (gray, disabled)
5. Wait for completion
   - Form closes

**Expected Result:** ✅ Button state transitions correctly

---

### Scenario 9: Subgrid Refresh

**Test Case 9.1: Subgrid Updates After Upload**

**Steps:**
1. Note current record count in subgrid
2. Open Quick Create
3. Upload 3 files
4. Wait for form to close
5. Verify subgrid shows +3 records
6. Verify new records visible without manual refresh

**Expected Result:** ✅ Subgrid refreshes automatically

---

**Test Case 9.2: Subgrid Sorted Correctly**

**Steps:**
1. Upload new documents
2. Verify new records appear at top (if sorted by Created On descending)
3. Verify sorting matches subgrid configuration

**Expected Result:** ✅ Sort order preserved

---

### Scenario 10: Performance Testing

**Test Case 10.1: Concurrent Uploads**

**Steps:**
1. Open 2 different Quick Create forms (different browser tabs)
2. Upload files from both simultaneously
3. Verify both complete successfully
4. Verify no conflicts or data corruption

**Expected Result:** ✅ Concurrent operations work

---

**Test Case 10.2: Maximum File Count**

**Steps:**
1. Select 10 files
2. Upload
3. Verify all complete
4. Monitor performance

**Expected Result:** ✅ All 10 uploaded successfully

**Performance:** ~30-40 seconds for 10x1MB files

---

### Scenario 11: Parent Relationship

**Test Case 11.1: Matter Relationship Created**

**Steps:**
1. Open Quick Create from Matter record
2. Upload file
3. Open created Document record
4. Verify Matter lookup field populated
5. Verify relationship: Document.sprk_MatterId → Matter

**Expected Result:** ✅ Parent relationship established

---

### Scenario 12: Browser Compatibility

**Test browsers:**
- [ ] Chrome (latest)
- [ ] Edge (latest)
- [ ] Firefox (latest)
- [ ] Safari (if available)

**For each browser, test:**
1. Quick Create opens
2. File picker works
3. Upload completes
4. Form closes
5. No console errors

**Expected Result:** ✅ Works in all modern browsers

---

## Regression Testing

### Verify No Impact on Existing Features

- [ ] Standard form (non-Quick Create) still works
- [ ] Manual Document creation (without PCF) still works
- [ ] Existing Document records display correctly
- [ ] Other subgrids on Matter form still work
- [ ] Matter form saves normally

---

## Test Data Requirements

**Files for testing:**
- Small file: <100KB (e.g., test.txt)
- Medium files: 1-5MB (e.g., PDF documents)
- Large file: 50MB+ (e.g., video, large PDF)
- Various formats: .pdf, .docx, .xlsx, .png, .jpg, .zip
- Special names: "Document (Draft) - Version 2.0 [FINAL].pdf"

**Test Matter record:**
- Active Matter record
- Documents subgrid configured
- User has read/write access

---

## Test Results Template

For each test case, record:

```
Test Case: 1.1 - Upload Single Small File
Date: 2025-10-07
Tester: [Name]
Environment: [Dev/Test/Prod]

Result: ✅ PASS / ❌ FAIL
Duration: 2.3 seconds
Notes: All metadata populated correctly

Issues Found: None

Screenshots: [Attach if needed]
```

---

## Defect Logging

If bugs found, log with:

**Template:**
```
Defect ID: BUG-001
Title: Progress bar stuck at 50%
Severity: High / Medium / Low
Steps to Reproduce:
1. Upload 5 files
2. Watch progress
3. Stuck at 50%

Expected: Progress to 100%
Actual: Stuck at 50%, form doesn't close

Environment: Dev
Browser: Chrome 120
Date: 2025-10-07
```

---

## Success Criteria

All test cases must PASS:
- ✅ Single file upload
- ✅ Multiple files (sync-parallel)
- ✅ Multiple files (long-running)
- ✅ Error handling (network failure)
- ✅ Partial failure handling
- ✅ Button states correct
- ✅ Form data included in records
- ✅ Subgrid refresh automatic
- ✅ Parent relationship created
- ✅ No console errors
- ✅ Works in Chrome, Edge, Firefox

**Performance Criteria:**
- Single file (<1MB): <3 seconds
- 3 files (sync-parallel): <5 seconds
- 5 files (long-running): <25 seconds
- 10 files: <45 seconds

---

## Monitoring During Testing

**Browser Console:**
- Watch for JavaScript errors
- Check logger output (if enabled)
- Monitor network requests

**Network Tab:**
- Verify API calls to SDAP BFF
- Check response codes (200, 201)
- Monitor payload sizes

**Application Insights (if configured):**
- Check telemetry data
- Monitor error rates
- Track performance metrics

---

## Testing Checklist

- [ ] All test scenarios executed
- [ ] Test results documented
- [ ] Defects logged and prioritized
- [ ] Performance criteria met
- [ ] Browser compatibility verified
- [ ] Regression testing complete
- [ ] No console errors
- [ ] User acceptance criteria met
- [ ] Screenshots/videos captured
- [ ] Test data cleaned up (optional)

---

## User Acceptance Testing (UAT)

After internal testing, conduct UAT:

1. **Prepare UAT script** for business users
2. **Train testers** on feature
3. **Monitor UAT sessions**
4. **Collect feedback**
5. **Address issues** before production

---

**Status:** Ready for execution
**Time:** 4 hours (initial round)
**Prerequisites:** Full implementation deployed
**Next:** Work Item 10 - Documentation
