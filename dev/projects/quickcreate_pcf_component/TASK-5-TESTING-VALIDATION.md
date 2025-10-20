# Task 5: Testing & Validation

**Sprint:** Custom Page Migration v3.0.0
**Estimate:** 12 hours
**Status:** Not Started
**Depends On:** Tasks 1-4 complete

---

## Pre-Task Review

```
TASK REVIEW: Verify deployment complete before testing.

1. Verify all components deployed to SPAARKE DEV 1:
   - [ ] Custom Page: sprk_documentuploaddialog
   - [ ] PCF Control: v3.0.0.0
   - [ ] Web Resources: Updated
   - [ ] Ribbon Customizations: Updated

2. Verify Phase 7 services operational:
   curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz

3. Verify Redis cache available

4. Create test data if needed:
   - Matter record with container ID
   - Project record with container ID
   - Invoice, Account, Contact records

Output: "Ready to test" OR "Blockers: [list]"
```

---

## Task Context

**What:** Execute comprehensive test matrix across all 5 entities and scenarios

**Why:** Ensure Custom Page migration works correctly before UAT

**Test Coverage:**
- 5 entities (Matter, Project, Invoice, Account, Contact)
- 6 scenarios per entity
- Phase 7 metadata discovery validation
- Performance benchmarking
- Error handling

---

## Test Matrix

### Test Scenarios (repeat for each entity)

| # | Scenario | Expected Result |
|---|----------|----------------|
| 1 | Single file upload | Document created, subgrid refreshed |
| 2 | Multiple files (5) | All 5 documents created |
| 3 | Maximum files (10) | All 10 documents created |
| 4 | Exceeds maximum (11) | Error: "Maximum 10 files allowed" |
| 5 | Oversized file (>50MB) | Error: "File size exceeds 50MB" |
| 6 | Missing container ID | Error: "Container Required" |

### Entities to Test

1. **sprk_matter**
   - Display name field: sprk_matternumber
   - Navigation property: Verify Phase 7 lookup

2. **sprk_project**
   - Display name field: sprk_projectname
   - Navigation property: Verify Phase 7 lookup

3. **sprk_invoice**
   - Display name field: sprk_invoicenumber
   - Navigation property: Verify Phase 7 lookup

4. **account**
   - Display name field: name
   - Navigation property: Verify Phase 7 lookup

5. **contact**
   - Display name field: fullname
   - Navigation property: Verify Phase 7 lookup

---

## Test Procedures

### Procedure 1: Functional Testing

**For EACH entity:**

1. Open record with container ID
2. Click "Upload Documents" ribbon button
3. Verify Custom Page dialog opens (modal, centered, 800px)
4. Select test file(s)
5. Click "Upload & Create"
6. Verify:
   - Files upload to SPE (check Network tab)
   - Document records created in Dataverse
   - Navigation property set correctly (Phase 7)
   - Dialog closes automatically
   - Subgrid refreshes
   - New documents visible

**Document Results:**
- Screenshot of dialog open
- Screenshot of successful upload
- Screenshot of created documents in subgrid
- Note any errors or issues

### Procedure 2: Phase 7 Validation

**Verify dynamic metadata discovery:**

1. For each test entity, capture NavMap API calls:
   ```
   https://spe-api-dev-67e2xz.azurewebsites.net/api/navmap/{entityName}/{navigationProperty}/lookup
   ```

2. Verify response contains correct navigation property name
3. Verify case-sensitivity preserved (e.g., `sprk_Matter` vs `sprk_matter`)
4. Verify Redis caching (second call faster)

### Procedure 3: Error Handling

Test error scenarios:

1. **Missing Container ID**
   - Remove container ID from record
   - Click "Upload Documents"
   - Expected: Error dialog "Container Required"

2. **Invalid Container ID**
   - Set container ID to "invalid-test"
   - Try upload
   - Expected: SPE error, graceful handling

3. **File Validation Errors**
   - Too many files (11+)
   - Oversized file (>50MB)
   - Invalid file type (if implemented)
   - Expected: Clear error messages

4. **Network Errors**
   - Disable network (offline mode)
   - Try upload
   - Expected: Timeout error, graceful handling

### Procedure 4: Performance Benchmarking

**Measure key metrics:**

1. **Dialog Open Time**
   - Time from button click to dialog visible
   - Target: < 2 seconds

2. **File Upload Time**
   - Time to upload 1MB file to SPE
   - Target: < 5 seconds

3. **Document Creation Time**
   - Time to create Document record in Dataverse
   - Target: < 2 seconds

4. **End-to-End Time**
   - Total time from "Upload" click to dialog close
   - Target: < 10 seconds for single file

5. **NavMap Cache Performance**
   - First call (cache miss): ~100-200ms
   - Second call (cache hit): < 50ms

**Document Results:**
- Create performance table
- Note any outliers
- Compare to v2.3.0 if possible

---

## Acceptance Criteria

- [ ] All 5 entities tested (Matter, Project, Invoice, Account, Contact)
- [ ] All 6 scenarios passed for each entity
- [ ] Phase 7 metadata discovery validated
- [ ] Error handling tested and working
- [ ] Performance benchmarks meet targets
- [ ] No console errors (except expected API failures)
- [ ] All test results documented with screenshots
- [ ] Issues logged in tracking system

---

## Deliverables

1. ✅ Test execution log (all 30 scenarios: 5 entities × 6 scenarios)
2. ✅ Screenshots for each entity (success and error cases)
3. ✅ Phase 7 validation report
4. ✅ Performance benchmark table
5. ✅ Issue log (if any issues found)
6. ✅ Test sign-off document

---

## Test Report Template

Create file: `dev/projects/quickcreate_pcf_component/testing/TEST-REPORT-v3.0.0.md`

```markdown
# Test Report: Custom Page Migration v3.0.0

**Date:** 2025-10-20
**Tester:** [Name]
**Environment:** SPAARKE DEV 1

## Summary

- Total Tests: 30 (5 entities × 6 scenarios)
- Passed: X
- Failed: Y
- Blocked: Z

## Test Results

### sprk_matter
| Scenario | Result | Notes |
|----------|--------|-------|
| Single file | PASS | Upload time: 3.2s |
| Multiple files (5) | PASS | All created successfully |
| Maximum files (10) | PASS | Upload time: 12s |
| Exceeds maximum (11) | PASS | Error shown correctly |
| Oversized file | PASS | Error shown correctly |
| Missing container | PASS | Error shown correctly |

(Repeat for all entities)

## Phase 7 Validation

- NavMap API calls working: YES
- Cache performance: 88% hit rate
- Navigation properties correct: YES
- Case-sensitivity preserved: YES

## Performance

| Metric | Target | Actual | Status |
|--------|--------|--------|--------|
| Dialog open | < 2s | 1.2s | PASS |
| File upload (1MB) | < 5s | 3.5s | PASS |
| Document creation | < 2s | 1.8s | PASS |
| End-to-end | < 10s | 7.2s | PASS |

## Issues Found

1. [Issue description if any]

## Sign-Off

Tested by: _______________
Date: _______________
Approved: YES / NO
```

---

**Created:** 2025-10-20
**Sprint:** Custom Page Migration v3.0.0
**Version:** 1.0.0
