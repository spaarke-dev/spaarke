# Task 6.6: Testing and Validation

## Task Prompt (For AI Agent)

```
You are working on Task 6.6 of Phase 6: Testing and Validation

BEFORE starting work:
1. Read this entire task document carefully
2. Verify Task 6.5 is complete - deployment must be successful and v2.2.0 active
3. Confirm version badge shows V2.2.0 in browser (not V2.1.0)
4. Open browser DevTools and keep Console and Network tabs visible during testing
5. Review test environment details (Matter GUID, Container ID, Dataverse org)
6. Update status section with current state

DURING work:
1. Execute ALL 14 test cases in the order presented
2. Document ACTUAL results for each test (not just pass/fail)
3. Update test result checkboxes: ✅ Pass | ❌ Fail | ⬜ Skipped
4. Take screenshots of critical tests (especially browser console showing no errors)
5. If any test fails, document in "Issues Found" table with severity
6. Update test results summary table with counts
7. Record performance metrics (metadata queries, upload times)

CRITICAL TESTS:
- Test 1.1: Single file - MUST pass before continuing
- Test 1.3: Multiple files - Validates multi-file support
- Test 1.4: Large batch (10+) - Validates caching (only 1 metadata query)
- Test 5.1: No "undeclared property" errors in console
- Test 5.2: Verify metadata caching (2nd upload = 0 metadata queries)
- Test 7.1: Version = V2.2.0 (critical - if wrong, deployment failed)

AFTER completing work:
1. Calculate pass rate: (Passed / Total) * 100
2. Complete test results summary table
3. Complete performance metrics table
4. Fill in "Issues Found" table if any failures
5. Complete regression testing checklist
6. Fill in tester name, test date, overall status
7. Determine if approved for production (all tests pass) or needs fixes
8. If fixes needed, return to relevant task (6.2, 6.3, 6.4, or 6.5)
9. If all pass, mark Phase 6 as COMPLETE

Your goal: Execute comprehensive test plan to validate Document record creation works correctly with no errors and proper performance.
```

---

## Overview

**Task ID:** 6.6
**Phase:** 6 - PCF Control Document Record Creation Fix
**Duration:** 1 day
**Dependencies:** Task 6.5 (Deployment complete)
**Status:** Ready to Start

---

## Objective

Execute comprehensive testing to validate that Document record creation works correctly using both Option A and Option B, with proper error handling and performance characteristics.

---

## Test Environment

**Dataverse Org:** https://spaarkedev1.crm.dynamics.com
**Test Matter GUID:** 3a785f76-c773-f011-b4cb-6045bdd8b757
**Test Container ID:** b!yLRdWEOAdkaWXskuRfByIRiz1S9kb_xPveFbearu6y9k1_PqePezTIDObGJTYq50
**PCF Version:** V2.2.0

---

## Test Plan

### Test Category 1: Positive Tests - Option A (@odata.bind)

#### Test 1.1: Single File Upload with Auto-Generated Document Name

**Steps:**
1. Open Matter record (GUID: 3a785f76-c773-f011-b4cb-6045bdd8b757)
2. Click "Upload Documents" or open Quick Create dialog
3. Select 1 PDF file (e.g., "test-document.pdf")
4. Leave document name field empty (use auto-generated from filename)
5. Leave description empty
6. Click Upload/Create

**Expected Result:**
- ✅ File uploads to SPE successfully (200 OK)
- ✅ Document record created in Dataverse
- ✅ sprk_documentname = "test-document" (extension removed)
- ✅ sprk_filename = "test-document.pdf"
- ✅ sprk_matter lookup set to test Matter
- ✅ No "undeclared property" errors in console
- ✅ Success message shown
- ✅ Dialog closes
- ✅ Subgrid refreshes showing new Document

**Actual Result:** ⬜ Pass | ⬜ Fail
**Notes:** _______________

---

#### Test 1.2: Single File Upload with Custom Document Name and Description

**Steps:**
1. Open Matter record
2. Click "Upload Documents"
3. Select 1 PDF file
4. Enter custom document name: "My Custom Document"
5. Enter description: "This is a test document with custom metadata"
6. Click Upload/Create

**Expected Result:**
- ✅ File uploads successfully
- ✅ Document record created
- ✅ sprk_documentname = "My Custom Document"
- ✅ sprk_documentdescription = "This is a test document with custom metadata"
- ✅ sprk_filename = original filename
- ✅ Success message and subgrid refresh

**Actual Result:** ⬜ Pass | ⬜ Fail
**Notes:** _______________

---

#### Test 1.3: Multiple Files Upload (3-5 files)

**Steps:**
1. Open Matter record
2. Click "Upload Documents"
3. Select 3-5 files of different types (PDF, DOCX, XLSX)
4. Use auto-generated names
5. Click Upload/Create

**Expected Result:**
- ✅ All files upload successfully
- ✅ Document record created for EACH file
- ✅ All records linked to Matter
- ✅ Each sprk_documentname matches filename (no extension)
- ✅ Success message shows count: "3 documents created successfully"
- ✅ Subgrid shows all new Documents

**Actual Result:** ⬜ Pass | ⬜ Fail
**Notes:** _______________

---

#### Test 1.4: Large Batch Upload (10+ files) - Performance Test

**Steps:**
1. Open Matter record
2. Click "Upload Documents"
3. Select 10-15 files
4. Monitor browser DevTools → Network tab
5. Click Upload/Create

**Expected Result:**
- ✅ Only 1 metadata query to EntityDefinitions (check Network tab)
- ✅ 1 metadata query + 10-15 create requests (not 10-15 metadata queries)
- ✅ All Documents created successfully
- ✅ Process completes in reasonable time (< 30 seconds for 10 files)

**Actual Result:** ⬜ Pass | ⬜ Fail
**Network Requests Count:**
- Metadata queries: _______
- Create requests: _______
**Performance:**
- Total time: _______ seconds

---

### Test Category 2: Positive Tests - Option B (Relationship URL)

**Note:** Option B may require code modification to use `createDocumentsViaRelationship()` method instead of `createDocuments()`.

#### Test 2.1: Single File Upload via Relationship URL

**Steps:**
1. Modify code to call `createDocumentsViaRelationship()` (or add UI toggle)
2. Upload 1 file
3. Verify success

**Expected Result:**
- ✅ Document created successfully
- ✅ Linked to Matter
- ✅ Network tab shows POST to `/sprk_matters(guid)/sprk_matter_document`

**Actual Result:** ⬜ Pass | ⬜ Fail | ⬜ Skipped (Option B not exposed in UI)
**Notes:** _______________

---

### Test Category 3: Negative Tests

#### Test 3.1: Invalid Parent GUID

**Steps:**
1. Modify code temporarily to use invalid GUID: "00000000-0000-0000-0000-000000000000"
2. Upload 1 file
3. Observe error handling

**Expected Result:**
- ✅ User-friendly error message shown
- ✅ No Document records created
- ✅ Error logged to console with details
- ✅ UI remains responsive (not frozen)

**Actual Result:** ⬜ Pass | ⬜ Fail
**Error Message:** _______________

---

#### Test 3.2: Missing Container ID

**Steps:**
1. Test with Matter that has no sprk_containerid value
2. Upload 1 file

**Expected Result:**
- ✅ Error message: "Container ID not found" or similar
- ✅ No Document record created
- ✅ User notified of issue

**Actual Result:** ⬜ Pass | ⬜ Fail
**Notes:** _______________

---

#### Test 3.3: Metadata Access Denied (Simulated)

**Steps:**
1. If possible, test with user account that lacks EntityDefinitions read permission
2. Upload 1 file

**Expected Result:**
- ✅ Friendly error: "Permission denied: Unable to access EntityDefinitions metadata"
- ✅ Error provides guidance to user/admin
- ✅ No cryptic technical error shown

**Actual Result:** ⬜ Pass | ⬜ Fail | ⬜ Skipped (cannot simulate)
**Notes:** _______________

---

### Test Category 4: Field Validation

#### Test 4.1: Verify All Fields Populated Correctly

**Steps:**
1. Upload 1 file with custom name and description
2. Open created Document record in Dataverse
3. Verify all fields

**Expected Values:**
- sprk_documentname: (custom name or filename without extension)
- sprk_filename: (original filename with extension)
- sprk_graphitemid: (SPE item ID - starts with "01K" or similar)
- sprk_graphdriveid: (Container ID)
- sprk_filesize: (size in bytes)
- sprk_documentdescription: (custom description or null)
- sprk_matter: (lookup to Matter record)

**Actual Result:** ⬜ Pass | ⬜ Fail

**Field Values:**
| Field | Expected | Actual | Status |
|-------|----------|--------|--------|
| sprk_documentname | _____________ | _____________ | ⬜ |
| sprk_filename | _____________ | _____________ | ⬜ |
| sprk_graphitemid | _____________ | _____________ | ⬜ |
| sprk_graphdriveid | _____________ | _____________ | ⬜ |
| sprk_filesize | _____________ | _____________ | ⬜ |
| sprk_documentdescription | _____________ | _____________ | ⬜ |
| sprk_matter | Matter GUID | _____________ | ⬜ |

---

### Test Category 5: Browser Console Validation

#### Test 5.1: No Errors in Console

**Steps:**
1. Open browser DevTools → Console
2. Upload 3 files
3. Review console for errors

**Expected Result:**
- ✅ No "undeclared property" errors
- ✅ No JavaScript errors
- ✅ No 400/404/500 API errors
- ✅ Success logs visible (if implemented)

**Actual Result:** ⬜ Pass | ⬜ Fail
**Console Errors (if any):** _______________

---

#### Test 5.2: Verify Metadata Caching

**Steps:**
1. Clear browser cache
2. Upload 3 files (triggers metadata query)
3. Check Network tab for EntityDefinitions request
4. Upload 3 MORE files to same Matter
5. Check Network tab again

**Expected Result:**
- ✅ First upload: 1 EntityDefinitions request
- ✅ Second upload: 0 EntityDefinitions requests (cache hit)

**Actual Result:** ⬜ Pass | ⬜ Fail
**Metadata Requests:**
- First upload: _______
- Second upload: _______

---

### Test Category 6: User Experience

#### Test 6.1: Custom Page Closes on Success

**Steps:**
1. Upload 1 file
2. Verify success
3. Observe UI behavior

**Expected Result:**
- ✅ Success message displayed
- ✅ Custom Page/dialog closes automatically (or on user click)
- ✅ User returned to Matter form

**Actual Result:** ⬜ Pass | ⬜ Fail
**Notes:** _______________

---

#### Test 6.2: Subgrid Refreshes Automatically

**Steps:**
1. Note current Document count in subgrid
2. Upload 2 files
3. Observe subgrid after upload

**Expected Result:**
- ✅ Subgrid automatically refreshes
- ✅ New Documents appear in list
- ✅ Document count increases by 2

**Actual Result:** ⬜ Pass | ⬜ Fail
**Notes:** _______________

---

### Test Category 7: Version Verification

#### Test 7.1: Correct Version Displayed

**Steps:**
1. Open Custom Page with PCF control
2. Check version badge/indicator

**Expected Result:**
- ✅ Version shows "V2.2.0"
- ✅ No old version (V2.1.0 or earlier)

**Actual Result:** ⬜ Pass | ⬜ Fail
**Version Displayed:** _______________

---

## Test Results Summary

| Category | Tests | Passed | Failed | Skipped |
|----------|-------|--------|--------|---------|
| 1. Positive (Option A) | 4 | _____ | _____ | _____ |
| 2. Positive (Option B) | 1 | _____ | _____ | _____ |
| 3. Negative Tests | 3 | _____ | _____ | _____ |
| 4. Field Validation | 1 | _____ | _____ | _____ |
| 5. Console Validation | 2 | _____ | _____ | _____ |
| 6. User Experience | 2 | _____ | _____ | _____ |
| 7. Version Verification | 1 | _____ | _____ | _____ |
| **Total** | **14** | _____ | _____ | _____ |

**Pass Rate:** _____% (Passed / Total)

---

## Issues Found

| Test ID | Issue Description | Severity | Status |
|---------|-------------------|----------|--------|
| | | P0/P1/P2 | Open/Fixed |

---

## Performance Metrics

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Metadata queries per upload session | 1 | _____ | ⬜ |
| Single file upload time | < 5s | _____ | ⬜ |
| 10 files upload time | < 30s | _____ | ⬜ |
| Browser memory usage | Stable | _____ | ⬜ |

---

## Regression Testing

Verify existing functionality still works:

- [ ] File upload to SPE (without Document creation) - still works
- [ ] Metadata display in UI - still works
- [ ] Form validation - still works
- [ ] Cancel/close button - still works

---

## Sign-Off

**Tested By:** _______________
**Test Date:** _______________
**Overall Status:** ⬜ Pass | ⬜ Fail | ⬜ Conditional Pass
**Notes:** _______________

**Approved for Production:** ⬜ Yes | ⬜ No | ⬜ With Conditions

---

## Next Steps

### If All Tests Pass:
1. Mark Phase 6 as complete
2. Update PHASE-6-OVERVIEW.md with completion date
3. Document lessons learned
4. Plan for multi-parent expansion (Account, Contact, etc.)

### If Tests Fail:
1. Document failures in Issues Found table
2. Return to relevant task for fixes:
   - Metadata issues → Task 6.2
   - Create errors → Task 6.3
   - Configuration issues → Task 6.4
   - Deployment issues → Task 6.5
3. Re-deploy and re-test

---

**Task Owner:** _______________
**Completion Date:** _______________
**Reviewed By:** _______________
**Status:** ⬜ Not Started | ⬜ In Progress | ⬜ Blocked | ⬜ Complete
