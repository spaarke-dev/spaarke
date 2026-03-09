# Task 052: Performance Test Plan - Subgrid Load Time

> **Project**: matter-performance-KPI-r1
> **Requirement**: NFR-02 - Subgrid load time MUST be < 2 seconds for 100 assessment records
> **Date**: 2026-02-12
> **Status**: Complete (test plan documented)

---

## 1. Objective

Validate that the KPI Assessments subgrid on the Matter Report Card tab loads within 2 seconds when displaying 100 `sprk_kpiassessment` records linked to a single matter. This addresses NFR-02 from `spec-r1.md`.

---

## 2. Subgrid Configuration Reference

**File**: `src/solutions/SpaarkeCore/entities/sprk_matter/FormXml/reportcard/matter-reportcard-tab.xml`

| Parameter | Value | Notes |
|-----------|-------|-------|
| Control ID | `subgrid_kpiassessments` | Line 70 |
| Target Entity | `sprk_kpiassessment` | Line 72 |
| Relationship | `sprk_matter_kpiassessments` | Line 77 |
| AutoExpand | `Fixed` | Line 78 |
| RecordsPerPage | `10` | Line 79 - only 10 records rendered per page |
| EnableQuickFind | `true` | Line 73 |
| EnableViewPicker | `false` | Line 74 |
| EnableChartPicker | `false` | Line 81 |

**Key observation**: The subgrid is configured with `RecordsPerPage=10`, meaning the Dataverse platform will render 10 records at a time with pagination. The test measures initial page load (first 10 records), not all 100 records at once. However, the backend query retrieves the full filtered dataset, so the total record count affects query time.

---

## 3. Test Environment

### Prerequisites

| Item | Details |
|------|---------|
| **Environment** | Spaarke Dev1 (`https://spaarkedev1.crm.dynamics.com`) |
| **Browser** | Microsoft Edge (Chromium) latest stable |
| **Network** | Wired connection, minimum 50 Mbps |
| **Cache** | Clear browser cache before each test run (Ctrl+Shift+Delete) |
| **Extensions** | Disable all browser extensions (use InPrivate/Incognito) |
| **Console** | Open DevTools (F12) before navigation |

### Test Data Setup

1. **Create a test matter record** (or use an existing matter in Dev environment)
2. **Create 100 `sprk_kpiassessment` records** linked to the test matter via `sprk_matter` lookup
   - Distribute across performance areas: ~34 Guidelines, ~33 Budget, ~33 Outcomes
   - Use varied grades (A through F) for realistic data distribution
   - Use varied `createdon` dates (spread across 30 days) to simulate real usage
3. **Record the test matter GUID** for consistent test runs

**Data creation method** (preferred):
```
Option A: Dataverse bulk import via Excel
Option B: Power Automate instant flow with loop
Option C: Manual Quick Create (not recommended for 100 records)
Option D: Dataverse API script using fetch:
  POST /api/data/v9.2/sprk_kpiassessments
  { "sprk_matter@odata.bind": "/sprk_matters({matterId})", ... }
```

---

## 4. Test Procedure

### Test 1: Initial Tab Load Time (Primary)

**Steps**:

1. Clear browser cache (Ctrl+Shift+Delete -> All time -> Clear data)
2. Open DevTools (F12) -> Performance tab
3. Navigate to the test matter record in Dataverse
4. Wait for the main form to fully load
5. Click **Start Recording** in DevTools Performance tab
6. Click the **Report Card** tab
7. Wait until the subgrid fully renders (loading spinner disappears, 10 rows visible)
8. Click **Stop Recording** in DevTools Performance tab
9. Record the total elapsed time from tab click to subgrid render complete

**Measurement points**:
- **Start**: Click event on "Report Card" tab label
- **End**: Last network request completes + DOM paint for subgrid rows

**Pass criteria**: Total elapsed time < 2000ms

### Test 2: Subsequent Tab Switch (Cached)

**Steps**:

1. With the matter record still open, switch to a different tab (e.g., Summary)
2. Start DevTools Performance recording
3. Switch back to Report Card tab
4. Wait for subgrid to render
5. Stop recording and record elapsed time

**Pass criteria**: Total elapsed time < 2000ms (expected to be faster due to caching)

### Test 3: Subgrid Pagination Performance

**Steps**:

1. On the Report Card tab with subgrid showing first 10 records
2. Start DevTools Performance recording
3. Click the "Next Page" (>) button on the subgrid
4. Wait for the next 10 records to load
5. Stop recording and record elapsed time

**Pass criteria**: Page navigation < 1000ms (no explicit NFR, but should be responsive)

### Test 4: Subgrid Refresh Performance

**Steps**:

1. On the Report Card tab with subgrid showing
2. Start DevTools Performance recording
3. Click the subgrid refresh button (circular arrow)
4. Wait for records to re-render
5. Stop recording and record elapsed time

**Pass criteria**: Refresh < 2000ms

---

## 5. Tools and Measurement Methods

### Method A: Browser DevTools Performance Tab (Primary)

1. Open DevTools (F12) -> **Performance** tab
2. Click the **Record** button (circle icon)
3. Perform the test action (tab click)
4. Click **Stop**
5. In the timeline, locate:
   - First input event (click on Report Card tab)
   - Last paint/render event after network requests complete
6. Measure the delta between these two events

### Method B: Browser DevTools Network Tab (Supplementary)

1. Open DevTools (F12) -> **Network** tab
2. Check **Preserve log** and **Disable cache**
3. Perform the test action
4. Look for the Dataverse query request:
   - URL pattern: `/api/data/v9.2/sprk_kpiassessments?$filter=...`
   - Record the **Time** column value (total request time)
   - Record the **Size** column value (response payload size)

### Method C: Dataverse Performance Diagnostics

1. Open the matter record in Dataverse
2. Press **Ctrl+Shift+Q** to open Performance Diagnostics
3. Navigate to the Report Card tab
4. Review the diagnostics panel for:
   - Form load time breakdown
   - Network request timings
   - Control initialization times
   - Subgrid-specific metrics

### Method D: Console Timing (Manual)

```javascript
// Run in DevTools Console before clicking Report Card tab
performance.mark('subgrid-start');

// After subgrid loads, run:
performance.mark('subgrid-end');
performance.measure('subgrid-load', 'subgrid-start', 'subgrid-end');
console.log(performance.getEntriesByName('subgrid-load')[0].duration + 'ms');
```

---

## 6. Pass/Fail Criteria

| Test | Metric | Pass Threshold | Fail Threshold |
|------|--------|----------------|----------------|
| Test 1: Initial Tab Load | Tab click to subgrid render | < 2000ms | >= 2000ms |
| Test 2: Cached Tab Switch | Tab switch to subgrid render | < 2000ms | >= 2000ms |
| Test 3: Pagination | Page button click to render | < 1000ms (target) | >= 2000ms |
| Test 4: Refresh | Refresh click to render | < 2000ms | >= 2000ms |

**Overall Pass Criteria**: Test 1 (Initial Tab Load) MUST pass. Tests 2-4 are supplementary.

**Statistical approach**: Run each test 5 times, discard the highest and lowest values, report the median of the remaining 3 runs.

---

## 7. Risk Factors and Mitigations

| Risk | Impact | Mitigation |
|------|--------|------------|
| Network latency variation | Inconsistent results | Run 5 iterations, use median |
| Browser caching | Artificially fast results | Clear cache before Test 1 |
| Dataverse platform load | Slower during peak hours | Test during off-peak (early morning or evening) |
| Other form scripts interfering | Slower load | Check for other OnLoad/OnTabChange handlers |
| View definition missing columns | Extra query overhead | Verify the view used by `ViewId` includes only needed columns |

---

## 8. Expected Outcome

The subgrid is configured with `RecordsPerPage=10` and `AutoExpand=Fixed`, which means:
- Dataverse only renders 10 rows initially (not all 100)
- The backend query filters by `sprk_matter` relationship, which is indexed by default
- The query should be a simple lookup-based filter, expected to be fast

**Expected result**: PASS. Subgrid load time should be well under 2 seconds given:
1. Only 10 records rendered per page (not 100)
2. Relationship-based filtering uses indexed foreign key
3. Standard Dataverse subgrid rendering (platform-optimized)
4. No custom JavaScript transformations on the subgrid data

---

## 9. Results Template

| Test | Run 1 | Run 2 | Run 3 | Run 4 | Run 5 | Median | Result |
|------|-------|-------|-------|-------|-------|--------|--------|
| Test 1: Initial Load | _ms | _ms | _ms | _ms | _ms | _ms | PASS/FAIL |
| Test 2: Cached Switch | _ms | _ms | _ms | _ms | _ms | _ms | PASS/FAIL |
| Test 3: Pagination | _ms | _ms | _ms | _ms | _ms | _ms | PASS/FAIL |
| Test 4: Refresh | _ms | _ms | _ms | _ms | _ms | _ms | PASS/FAIL |

**Tester**: _______________
**Date**: _______________
**Environment**: Spaarke Dev1
**Browser**: Edge (version: ___)
**Network**: _______________
**Test Matter ID**: _______________
**Total Assessment Records**: 100

---

## 10. Remediation Plan (If Fail)

If subgrid load time exceeds 2 seconds:

1. **Analyze Network tab**: Identify the slowest request. Is it the Dataverse query or rendering?
2. **Check view columns**: Reduce columns in the subgrid view to only essential fields
3. **Reduce RecordsPerPage**: Change from 10 to 5 in `matter-reportcard-tab.xml` (line 79)
4. **Check for N+1 queries**: Verify no additional queries per row (e.g., lookup expansions)
5. **Add indexing**: If the `sprk_matter` lookup is not indexed, request index creation
6. **Profile with Dataverse diagnostics**: Use Ctrl+Shift+Q to identify bottleneck component

---

*Test plan created: 2026-02-12*
*Reference: spec-r1.md NFR-02, Task 052*
