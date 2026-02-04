# E2E Test: Event Creation with Regarding Record

**Test Date**: 2026-02-01
**Test Scenario**: Task 060 - E2E test for Event creation workflow
**Status**: Documentation Complete (Executable Test Plan)

## Executive Summary

This document provides comprehensive E2E test scenarios for the Event creation workflow with regarding record selection. The scenarios validate:
- **SC-01**: AssociationResolver allows selection from all 8 entity types
- **SC-04**: Entity subgrids filter records by regarding type
- **SC-08**: Field mappings apply on child record creation

Since no deployed environment is currently available, this document contains **executable test scenarios** that can be run manually or automated with a deployment.

---

## Test Prerequisites

**System Requirements:**
- Dataverse environment with Event solution deployed (Phase 4 + Phase 5 complete)
- User with Event form access (Create, Update permissions on sprk_Event)
- At least one Matter record in the system
- (Optional) Field Mapping profiles configured for Matter → Event mappings

**Test Data Setup:**
```sql
-- Required test records (create manually or via solution)
Matter: "Test Matter 001" (custom fields: sprk_client = "Client A")
Matter: "Test Matter 002"
Project: "Test Project 001"
Invoice: "Test Invoice 001"
Account: "Acme Corp"
Contact: "John Smith"
```

**Form Configuration:**
- Event form must have AssociationResolver control deployed
- RegardingLink control must be visible
- EventFormController must be controlling field visibility
- Event Log subgrid must display creation events

---

## Test Scenario 1: Create Event with Matter Selection (SC-01, SC-08)

**Objective**: Verify that user can create an Event with a Matter regarding record and field mappings auto-apply.

### Preconditions
- User on Event form (new record)
- At least 1 Matter exists: "Test Matter 001"
- (Optional) Field Mapping profile exists: Matter → Event with mapping for `sprk_client`

### Test Steps

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Navigate to Events list | Events list displays | ✅ N/A | Standard Dataverse navigation |
| 2 | Click "New Event" button | New Event form opens with blank fields | ✅ N/A | Form should render AssociationResolver control |
| 3 | Verify AssociationResolver dropdown populated | Dropdown shows 8 entity types: Matter, Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget | 🔲 Test | Core SC-01 requirement |
| 4 | Click entity type dropdown | All 8 options are listed and visible | 🔲 Test | Verify no missing types |
| 5 | Select "Matter" from dropdown | Matter selected; control updates to show search interface | 🔲 Test | UI should show search/lookup UI for Matter |
| 6 | Type "Test Matter 001" in search box | Search executes; results show "Test Matter 001" | 🔲 Test | Verify WebAPI query executes |
| 7 | Click to select the Matter | Matter record selected; form updates | 🔲 Test | Regarding fields should populate |
| 8 | Verify `sprk_regardingmatter` field | Field contains Matter ID (GUID) | 🔲 Test | Lookup field populated |
| 9 | Verify `sprk_regardingrecordname` field | Field shows "Test Matter 001" | 🔲 Test | Display name populated |
| 10 | Verify `sprk_regardingrecordtype` field | Field shows "1" (Matter type) | 🔲 Test | Type code matches configuration |
| 11 | Check for field mapping auto-application | If mapping exists: `description` field shows "Client A" (from Matter) | 🔲 Test | SC-08 requirement |
| 12 | Verify RegardingLink control | Shows clickable link to Matter record | 🔲 Test | Link navigates to Matter |
| 13 | Set Event Subject | Enter "Event from Matter 001" | 🔲 Test | Required field |
| 14 | Select Event Type | Choose a type (e.g., "Meeting") | 🔲 Test | May trigger field show/hide via EventFormController |
| 15 | Fill any additional required fields | As per Event Type requirements | 🔲 Test | Event Type may show/hide fields |
| 16 | Click Save | Event created successfully; no validation errors | 🔲 Test | Event should save with all regarding fields |
| 17 | Verify Event Log entry | Event Log shows "Created" entry | 🔲 Test | Event state change logged |
| 18 | Re-open saved Event | All regarding fields retain values | 🔲 Test | Data persists correctly |

### Expected Results
✅ **PASS IF:**
- All 8 entity types are available in dropdown (SC-01)
- Matter can be selected via search
- All regarding fields populate correctly after selection
- Field mapping applies if configured (SC-08)
- Event saves successfully
- Event Log entry created
- All data persists on re-open

❌ **FAIL IF:**
- Fewer than 8 entity types shown
- Search doesn't execute or returns no results
- Regarding fields remain blank after selection
- Field mapping doesn't apply (if configured)
- Event fails to save
- Data is lost on form refresh

---

## Test Scenario 2: Clear and Change Regarding Record

**Objective**: Verify user can clear a regarding record selection and select a different entity/record.

### Preconditions
- User on Event form with Matter already selected (from Scenario 1)
- At least 1 Project exists: "Test Project 001"

### Test Steps

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Locate AssociationResolver control | Control shows current Matter selection | ✅ N/A | Should show selected record |
| 2 | Locate "Clear" button in AssociationResolver | Button is visible and clickable | 🔲 Test | Clear button present |
| 3 | Click "Clear" button | All regarding fields cleared: `sprk_regardingmatter`, `sprk_regardingrecordname`, `sprk_regardingrecordtype` | 🔲 Test | Regarding fields reset |
| 4 | Verify RegardingLink control | Link disappears or shows "None" | 🔲 Test | Link disabled when no regarding |
| 5 | Click entity type dropdown | Dropdown shows all 8 types | 🔲 Test | Still functional after clear |
| 6 | Select "Project" type | Project search interface shown | 🔲 Test | Entity type changed |
| 7 | Search for "Test Project 001" | Search returns Project records | 🔲 Test | WebAPI query for Project |
| 8 | Click to select Project | Project record selected; form updates | 🔲 Test | Regarding fields populate with Project |
| 9 | Verify `sprk_regardingproject` field | Field contains Project ID | 🔲 Test | Different lookup field than Matter |
| 10 | Verify `sprk_regardingrecordname` | Shows "Test Project 001" | 🔲 Test | Project name displayed |
| 11 | Verify `sprk_regardingrecordtype` | Field shows "0" (Project type) | 🔲 Test | Project type code = 0 |
| 12 | Verify field mapping re-applies | If mapping exists for Project: fields update | 🔲 Test | Mapping changes with entity type |
| 13 | Click Save | Event saved with new Project regarding | 🔲 Test | Save succeeds with changed data |

### Expected Results
✅ **PASS IF:**
- Clear button removes all regarding data
- New entity type can be selected after clear
- Correct regarding fields populate for Project
- Field mapping adapts to new entity type
- Event saves with updated data

❌ **FAIL IF:**
- Clear doesn't remove all fields
- Cannot select new entity after clear
- Wrong regarding fields populate (e.g., still shows Matter field)
- Field mapping doesn't update
- Save fails

---

## Test Scenario 3: Field Mapping Auto-Application (SC-08)

**Objective**: Verify field mappings automatically apply when a regarding record is selected.

**Note**: This requires a Field Mapping profile to be configured.

### Preconditions
- Field Mapping profile exists: Matter → Event
- Profile has mapping: `sprk_matter.sprk_client` → `sprk_event.sprk_description`
- Matter record exists with `sprk_client` value: "Client A"

### Test Steps

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Navigate to new Event form | Form opens blank | ✅ N/A | Clean slate |
| 2 | Open AssociationResolver | Show entity type dropdown | 🔲 Test | Control accessible |
| 3 | Select Matter entity type | Matter search interface shown | 🔲 Test | Matter mode active |
| 4 | Search and select Matter (with `sprk_client = "Client A"`) | Matter selected | 🔲 Test | Record selection |
| 5 | Observe `sprk_description` field | **Automatically populated with "Client A"** | 🔲 Test | **SC-08: Auto-application** |
| 6 | Verify toast notification | Toast shows: "1 field mapped from Matter" | 🔲 Test | User feedback |
| 7 | Verify no user action required | Mapping applied without user clicking "Apply" button | 🔲 Test | Automatic (not manual) |
| 8 | Edit `sprk_description` to different value | User can override | 🔲 Test | Not locked |
| 9 | Change Matter selection to different record | Previous mapped value replaced with new Matter's client | 🔲 Test | Mapping updates with selection |
| 10 | Click Save | Event saves with mapped values | 🔲 Test | Data persists |

### Expected Results
✅ **PASS IF:**
- Field mapping applies automatically upon record selection
- Toast notification confirms mapping
- User can override mapped values
- Changing selection updates mapped fields
- All mapped data saves correctly

❌ **FAIL IF:**
- Field mapping requires manual button click
- No toast notification shown
- Fields don't update when selection changes
- Mapped values don't save
- Error occurs during mapping

---

## Test Scenario 4: Multi-Entity Type Validation (SC-01)

**Objective**: Verify AssociationResolver supports selection from all 8 entity types.

### Preconditions
- Test records exist for all 8 types:
  - Matter: "Test Matter"
  - Project: "Test Project"
  - Invoice: "Test Invoice"
  - Analysis: "Test Analysis"
  - Account: "Acme Corp"
  - Contact: "John Smith"
  - Work Assignment: "Task WA-001"
  - Budget: "Budget Q1-2026"

### Test Steps

| Entity Type | Select | Verify Lookup Field | Verify Type Code | Status |
|-------------|--------|-------------------|------------------|--------|
| Matter | "Test Matter" | `sprk_regardingmatter` populated | 1 | 🔲 |
| Project | "Test Project" | `sprk_regardingproject` populated | 0 | 🔲 |
| Invoice | "Test Invoice" | `sprk_regardinginvoice` populated | 2 | 🔲 |
| Analysis | "Test Analysis" | `sprk_regardinganalysis` populated | 3 | 🔲 |
| Account | "Acme Corp" | `sprk_regardingaccount` populated | 4 | 🔲 |
| Contact | "John Smith" | `sprk_regardingcontact` populated | 5 | 🔲 |
| Work Assignment | "Task WA-001" | `sprk_regardingworkassignment` populated | 6 | 🔲 |
| Budget | "Budget Q1-2026" | `sprk_regardingbudget` populated | 7 | 🔲 |

### Expected Results
✅ **PASS IF:**
- All 8 entity types are selectable
- Correct lookup field populates for each type
- Type code matches entity (1=Matter, 0=Project, etc.)
- `sprk_regardingrecordname` shows correct display name
- RegardingLink works for all types

❌ **FAIL IF:**
- Any entity type missing from dropdown
- Wrong lookup field populated
- Type code incorrect
- Display name shows wrong entity
- RegardingLink broken

---

## Test Scenario 5: Event Log Validation

**Objective**: Verify Event Log entries are created when Event is created with regarding record.

### Preconditions
- Event form configured with Event Log subgrid
- Matter selected as regarding record (from previous tests)

### Test Steps

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Create Event with Matter regarding record | Event saves successfully | ✅ Assume | From Scenario 1 |
| 2 | Open saved Event record | Form displays with all data | ✅ Assume | Refresh to see log |
| 3 | Scroll to Event Log subgrid | Subgrid visible with entries | 🔲 Test | Log must be present |
| 4 | Check for "Created" entry | Log shows entry with action="Created" | 🔲 Test | Event state change logged |
| 5 | Verify log entry details | Timestamp, creator, action, state change logged | 🔲 Test | All audit fields present |
| 6 | Click log entry | Details show Event was created (state transition) | 🔲 Test | Log navigation |

### Expected Results
✅ **PASS IF:**
- Event Log subgrid displays
- "Created" entry exists
- Timestamp and creator captured
- Event state transition visible

❌ **FAIL IF:**
- No log entries
- Missing "Created" action
- Log is empty or missing
- Timestamp/creator not recorded

---

## Test Scenario 6: Light and Dark Mode Verification (ADR-021)

**Objective**: Verify AssociationResolver, RegardingLink, and EventFormController display correctly in both light and dark modes.

### Preconditions
- Dataverse light mode enabled
- Dataverse dark mode available

### Test Steps (Light Mode)

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Set Dataverse to Light Mode | Theme changes to light | ✅ N/A | System setting |
| 2 | Open Event form | AssociationResolver visible | 🔲 Test | Light theme rendering |
| 3 | Verify dropdown colors | Text readable; contrast adequate | 🔲 Test | ADR-021 compliance |
| 4 | Verify button colors | "Clear", "Refresh" buttons visible | 🔲 Test | Fluent UI light tokens |
| 5 | Verify RegardingLink colors | Link text visible; hover state clear | 🔲 Test | Link styling |

### Test Steps (Dark Mode)

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Set Dataverse to Dark Mode | Theme changes to dark | ✅ N/A | System setting |
| 2 | Open Event form | AssociationResolver visible in dark mode | 🔲 Test | Dark theme rendering |
| 3 | Verify dropdown colors | Text readable; contrast adequate | 🔲 Test | ADR-021 compliance |
| 4 | Verify button colors | Buttons visible; not washed out | 🔲 Test | Fluent UI dark tokens |
| 5 | Verify RegardingLink colors | Link distinguishable; underline visible | 🔲 Test | Link styling in dark |

### Expected Results
✅ **PASS IF:**
- All controls render in both light and dark modes
- Text is readable in both modes
- Buttons are clickable and visible
- No color contrast violations
- Links are clearly identifiable

❌ **FAIL IF:**
- Controls don't render in one mode
- Text is hard to read
- Colors are hard-coded (not using Fluent tokens)
- Contrast ratio < 4.5:1 WCAG standard

---

## Test Scenario 7: Entity Subgrid Filtering (SC-04)

**Objective**: Verify that entity subgrids on Matter (and other parent records) show only Events related to that entity type.

### Preconditions
- Matter record exists: "Test Matter 001"
- Events created with different regarding types:
  - Event 1: Regarding = "Test Matter 001"
  - Event 2: Regarding = "Test Project 001"
  - Event 3: Regarding = "Test Matter 001"

### Test Steps

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Navigate to Matter record: "Test Matter 001" | Matter form opens | ✅ N/A | Standard navigation |
| 2 | Locate Events subgrid | Subgrid displays on Matter form | 🔲 Test | SC-04: Subgrid present |
| 3 | Verify Events in subgrid | Only Events with regarding=Matter shown | 🔲 Test | **Filter applied** |
| 4 | Count Events | Should show Event 1 and Event 3 (not Event 2) | 🔲 Test | Only Matter events |
| 5 | Verify filter logic | No Events from other entity types visible | 🔲 Test | Project event excluded |
| 6 | Create new Event from subgrid | New Event form opens with Matter pre-selected | 🔲 Test | Regarding auto-populated |
| 7 | Navigate to Project record | Project Events subgrid shown | 🔲 Test | Project filter works |
| 8 | Verify Project events | Only Events with regarding=Project shown | 🔲 Test | Project-specific filter |

### Expected Results
✅ **PASS IF:**
- Matter subgrid shows only Matter Events
- Project subgrid shows only Project Events
- Cross-entity Events filtered correctly
- New Event creation from subgrid auto-populates regarding

❌ **FAIL IF:**
- Subgrid shows Events from other entity types
- Filter doesn't work
- Events from Project shown in Matter subgrid
- Regarding not auto-populated from subgrid

---

## Test Scenario 8: Error Handling and Validation

**Objective**: Verify error handling when creating Event with invalid or missing data.

### Test Steps

| Step # | Action | Expected Result | Status | Notes |
|--------|--------|-----------------|--------|-------|
| 1 | Try to save Event without selecting regarding | Validation error shown | 🔲 Test | Regarding required? |
| 2 | Try to save Event without Event Subject | Validation error for Subject | 🔲 Test | Required field |
| 3 | Try to save Event without Event Type | Validation error for Event Type | 🔲 Test | Required field |
| 4 | Select Matter then clear it | Regarding fields cleared | 🔲 Test | Clear works |
| 5 | Try to save without re-selecting | Validation error shown | 🔲 Test | Cannot save without regarding |
| 6 | Navigate away with unsaved changes | Save prompt shown | 🔲 Test | Standard Dataverse behavior |

### Expected Results
✅ **PASS IF:**
- All validation errors are clear
- User cannot save invalid records
- Error messages guide user to required fields
- No data loss on validation error

❌ **FAIL IF:**
- No validation shown
- Invalid records are saved
- Error messages are unclear
- Data is lost on validation error

---

## Test Results Summary

### Overall Test Status
- **Total Scenarios**: 8
- **Passed**: 🔲
- **Failed**: 🔲
- **Not Tested**: 🔲

### Success Criteria Coverage

| Criterion | Scenario | Status | Evidence |
|-----------|----------|--------|----------|
| **SC-01**: AssociationResolver supports 8 entity types | 1, 4 | 🔲 | Dropdown shows all types |
| **SC-04**: Entity subgrids filter by regarding type | 7 | 🔲 | Matter subgrid shows only Matter events |
| **SC-08**: Field mappings auto-apply | 3 | 🔲 | Mapped fields populate on selection |

### Critical Issues Found
- [ ] None identified in documentation
- [ ] (Add any issues found during execution)

### Warnings or Observations
- AssociationResolver control must be deployed and visible
- Field Mapping profiles must be configured for SC-08 testing
- Event Log subgrid required for log verification
- Dark mode support requires Fluent UI v9 tokens

---

## Test Environment Details

**Environment**: Dataverse (Dev)
**Test Date**: 2026-02-01 (Planned)
**Tested By**: (To be filled in)
**Build Version**: Phase 4 + Phase 5 (Tasks 036 + 058)

---

## Recommendations

1. **Automation Candidate**: Scenarios 1, 2, 4, 7 are ideal for automated E2E testing with Playwright or similar
2. **Manual Focus Areas**: Scenario 3 (field mapping) and 6 (dark mode) benefit from manual validation
3. **Regression Testing**: Run all scenarios after any changes to AssociationResolver or field mapping
4. **Performance Testing**: Consider adding load test for large Matter/Project record sets (search performance)

---

## Sign-Off

**Documentation Complete**: ✅ 2026-02-01
**Ready for Execution**: ✅ (when deployed environment available)
**Reviewed By**: (To be filled in)

---

*This test plan documents comprehensive E2E scenarios for Event creation with regarding record selection. Execute these scenarios manually or with automated tools once the Dataverse environment is deployed with Phase 4 and Phase 5 components.*
