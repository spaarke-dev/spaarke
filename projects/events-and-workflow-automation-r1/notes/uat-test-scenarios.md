# User Acceptance Testing (UAT) Scenarios
## Events and Workflow Automation R1

---

## Overview

This document provides comprehensive user acceptance testing scenarios for the Events and Workflow Automation R1 system. All 15 graduation criteria are covered with step-by-step test procedures, expected results, and sign-off areas.

**Test Date**: _______________
**Test Environment**: Dev / Test / Staging
**Participants**: Product Owner, Lead User/Admin, Technical Tester

---

## Test User Roles

1. **Standard User** - Creates and manages Events, uses field mapping features
2. **Admin User** - Configures field mapping profiles, manages seed data
3. **System Administrator** - Deploys solutions, verifies technical integrations

---

## Graduation Criteria Test Coverage

### SC-01: AssociationResolver PCF - Select from All 8 Entity Types

**Objective**: Verify users can create Events associated with any of the 8 supported entity types

**Test Scenario**: Create Event for Each Supported Entity Type

**Setup**:
- Navigate to Spaarke application
- Create/identify test records for each entity type:
  - Matter (e.g., "Smith v. Jones")
  - Project (e.g., "Annual Audit")
  - Invoice (e.g., "INV-001")
  - Analysis (e.g., "Legal Analysis")
  - Account (e.g., "ABC Corp")
  - Contact (e.g., "John Doe")
  - Work Assignment (e.g., "Document Review")
  - Budget (e.g., "Case Budget 2026")

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Open Event form (+ New Event) | Form opens with AssociationResolver control visible | | [ ] Pass [ ] Fail |
| 2 | In Regarding section, click entity selector | Dropdown shows all 8 entity types | | [ ] Pass [ ] Fail |
| 3 | Select "Matter" | Matter type selected, search field enables | | [ ] Pass [ ] Fail |
| 4 | Type "Smith" in search field | Matching Matter records appear in results | | [ ] Pass [ ] Fail |
| 5 | Select "Smith v. Jones" from results | Matter populated, all regarding fields updated | | [ ] Pass [ ] Fail |
| 6 | Repeat steps 2-5 for each remaining entity type (Project, Invoice, Analysis, Account, Contact, Work Assignment, Budget) | All 8 entity types can be selected and associated | | [ ] Pass [ ] Fail |
| 7 | For each entity type, verify that selecting a different entity clears previous entity lookups | Previous regarding lookup fields cleared, new entity fields populated | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ All 8 entity types available in selector
- ✅ Search returns matching records
- ✅ Selection populates all regarding fields
- ✅ Changing entity clears previous lookups

**Pass Criteria**: All 7 steps pass without errors

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-02: RegardingLink PCF - Display Clickable Links in All Events View

**Objective**: Verify users can click regarding links in the All Events grid to navigate to source records

**Test Scenario**: Navigate to Regarding Record from All Events View

**Setup**:
- Create 3-5 Events with different regarding entity types
- Navigate to "All Events" view in Dataverse

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Open "All Events" view | View displays all events with columns including Regarding Record (RegardingLink PCF) | | [ ] Pass [ ] Fail |
| 2 | Verify RegardingLink column shows entity names as links | Each row shows regarding record name formatted as clickable link (blue, underlined) | | [ ] Pass [ ] Fail |
| 3 | Click on a "Matter" link (first event) | Navigation occurs, Matter form opens for selected matter | | [ ] Pass [ ] Fail |
| 4 | Go back to Events view, click on a "Project" link | Navigation occurs, Project form opens for selected project | | [ ] Pass [ ] Fail |
| 5 | Go back to Events view, click on an "Account" link | Navigation occurs, Account form opens for selected account | | [ ] Pass [ ] Fail |
| 6 | Test clicking links for remaining entity types | Navigation successful to each entity type | | [ ] Pass [ ] Fail |
| 7 | Verify dark mode renders links correctly (if dark mode enabled) | Links visible and clickable in dark theme | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ All Events view displays RegardingLink control
- ✅ Entity names shown as clickable links
- ✅ Clicking link navigates to target record
- ✅ Works for all 8 entity types

**Pass Criteria**: All 7 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-03: EventFormController PCF - Show/Hide Fields Based on Event Type

**Objective**: Verify Event Type controls field visibility/requirements

**Test Scenario**: Event Type Determines Field Display

**Setup**:
- Seed Event Type records must exist:
  - Type A: "Meeting" (requires Base Date, Start Time, End Time, Location)
  - Type B: "Deadline" (requires Due Date only)
  - Type C: "General" (no required fields)

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Open new Event form | EventFormController PCF loaded, base field visible | | [ ] Pass [ ] Fail |
| 2 | Select Event Type "Meeting" | Base Date, Start Time, End Time, Location fields visible and marked required | | [ ] Pass [ ] Fail |
| 3 | Attempt to save without entering required fields | Form save blocked with validation message | | [ ] Pass [ ] Fail |
| 4 | Enter required field values for Meeting | All required fields accept input | | [ ] Pass [ ] Fail |
| 5 | Save event as Meeting | Event saves successfully with Meeting type | | [ ] Pass [ ] Fail |
| 6 | Create another event, select Event Type "Deadline" | Due Date field visible and required, Start Time/End Time/Location hidden | | [ ] Pass [ ] Fail |
| 7 | Create another event, select Event Type "General" | All optional fields hidden except description | | [ ] Pass [ ] Fail |
| 8 | Change Event Type from "Meeting" to "Deadline" mid-form | Fields dynamically update: Start Time/Location hide, Due Date shows as required | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ EventFormController shows/hides fields based on type
- ✅ Required fields enforced per type
- ✅ Field visibility updates dynamically
- ✅ Validation prevents save with missing required fields

**Pass Criteria**: All 8 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-04: Entity Subgrids - Show Only Relevant Events

**Objective**: Verify entity-specific subgrids filter Events correctly

**Test Scenario**: Matter Subgrid Shows Only Matter Events

**Setup**:
- Create Test Matter with 3 associated Events
- Create second Test Matter with 2 associated Events
- Create Events for other entity types (Project, Invoice)

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Open first Test Matter form | Events subgrid visible on form | | [ ] Pass [ ] Fail |
| 2 | Verify subgrid header shows "Events for [Matter Name]" | Header clearly indicates filtered view | | [ ] Pass [ ] Fail |
| 3 | Count Events in subgrid - should match only this Matter's events | Exactly 3 events shown (only events linked to this Matter) | | [ ] Pass [ ] Fail |
| 4 | Open second Test Matter | Events subgrid shows only this Matter's events (2 total) | | [ ] Pass [ ] Fail |
| 5 | Verify Events from first Matter do NOT appear in second Matter's subgrid | Subgrid shows 2 events, not the 3 from first Matter | | [ ] Pass [ ] Fail |
| 6 | Verify Events for other entity types (Project, Invoice) do NOT appear | Subgrid contains only Matter-related events | | [ ] Pass [ ] Fail |
| 7 | Repeat test for other entity types (if subgrids exist) | Subgrids for Project, Account, etc. show only their respective events | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Entity subgrids filter by regarding entity type
- ✅ Only events for the specific entity shown
- ✅ No cross-entity contamination in subgrids
- ✅ Count matches expected associated events

**Pass Criteria**: All 7 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-05: Event Log - Capture State Transitions

**Objective**: Verify Event Log tracks state changes (create, complete, cancel, delete)

**Test Scenario**: Event Log Records State Changes

**Setup**:
- Create a Test Event
- Create Event Log lookup on Event form to view logs

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Create new Event "Test Event" and save | Event Log entry created with action = "Created" | | [ ] Pass [ ] Fail |
| 2 | Open Event Log related records from Event form | At least 1 log entry visible showing "Created" | | [ ] Pass [ ] Fail |
| 3 | Verify log entry contains: Action (Created), Timestamp, User | All fields populated with correct values | | [ ] Pass [ ] Fail |
| 4 | Mark Event as Complete (via status change or button) | New Event Log entry created with action = "Completed" | | [ ] Pass [ ] Fail |
| 5 | Check Event Log related records | Log shows both "Created" and "Completed" entries in chronological order | | [ ] Pass [ ] Fail |
| 6 | Cancel the Event | New Event Log entry created with action = "Cancelled" | | [ ] Pass [ ] Fail |
| 7 | Create another Event and delete it (if allowed) | Event Log entry created with action = "Deleted" | | [ ] Pass [ ] Fail |
| 8 | Verify all log entries have correct timestamps and user info | Timestamps are in sequence, user matches logged-in user | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Event Log created for each state transition
- ✅ All 4 actions tracked: Created, Completed, Cancelled, Deleted
- ✅ Timestamps accurate and sequential
- ✅ User information captured

**Pass Criteria**: All 8 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-06: Event API Endpoints - Functional Integration Tests

**Objective**: Verify Event API endpoints work correctly

**Test Scenario**: API CRUD Operations

**Setup**:
- Use Postman, VS Code API Client, or similar
- Authenticate with valid BFF API token
- Base URL: `https://spe-api-dev-{region}.azurewebsites.net`

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | GET /api/v1/events (no filters) | Returns 200 with list of all events (JSON array) | | [ ] Pass [ ] Fail |
| 2 | POST /api/v1/events (create event) | Returns 201 with new event ID and populated fields | | [ ] Pass [ ] Fail |
| 3 | GET /api/v1/events/{id} (retrieve specific event) | Returns 200 with event details matching POST response | | [ ] Pass [ ] Fail |
| 4 | PUT /api/v1/events/{id} (update event) | Returns 200 with updated fields reflected | | [ ] Pass [ ] Fail |
| 5 | GET /api/v1/events?regardingType=1 (filter by Matter) | Returns 200 with only Matter-related events | | [ ] Pass [ ] Fail |
| 6 | POST /api/v1/events/{id}/complete (mark complete) | Returns 200, status changes to Completed | | [ ] Pass [ ] Fail |
| 7 | POST /api/v1/events/{id}/cancel (cancel event) | Returns 200, status changes to Cancelled | | [ ] Pass [ ] Fail |
| 8 | DELETE /api/v1/events/{id} (delete event) | Returns 204 or 200, event no longer returned by GET | | [ ] Pass [ ] Fail |
| 9 | Attempt API call without authentication | Returns 401 Unauthorized | | [ ] Pass [ ] Fail |
| 10 | Verify error responses include ProblemDetails format | Error responses follow standard format | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ All CRUD operations functional
- ✅ Proper HTTP status codes returned
- ✅ Response bodies match specifications
- ✅ Authentication enforced

**Pass Criteria**: All 10 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-07: Admin Field Mapping Profile Configuration

**Objective**: Verify admins can create Field Mapping Profiles and Rules

**Test Scenario**: Admin Creates Matter-to-Event Field Mapping Profile

**Setup**:
- Admin user logged in
- Matter and Event field metadata available for reference

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Navigate to Field Mapping Profiles | List of existing profiles shown | | [ ] Pass [ ] Fail |
| 2 | Click + New Field Mapping Profile | Form opens with input fields | | [ ] Pass [ ] Fail |
| 3 | Enter Profile Name: "Matter to Event - Client Field" | Name saved | | [ ] Pass [ ] Fail |
| 4 | Select Source Entity: "Matter (sprk_matter)" | Entity selected | | [ ] Pass [ ] Fail |
| 5 | Select Target Entity: "Event (sprk_event)" | Entity selected | | [ ] Pass [ ] Fail |
| 6 | Select Mapping Direction: "Parent to Child" | Direction set | | [ ] Pass [ ] Fail |
| 7 | Select Sync Mode: "One-time" | Sync mode configured | | [ ] Pass [ ] Fail |
| 8 | Click Save | Profile created successfully | | [ ] Pass [ ] Fail |
| 9 | Add Field Mapping Rule: Source Field "Matter.sprk_client", Target Field "Event.sprk_regardingaccount" | Rule form opens | | [ ] Pass [ ] Fail |
| 10 | Set Source Field Type: "Lookup", Target Field Type: "Lookup" | Types selected | | [ ] Pass [ ] Fail |
| 11 | Set Compatibility Mode: "Strict" | Mode set | | [ ] Pass [ ] Fail |
| 12 | Click Save Rule | Rule created and linked to profile | | [ ] Pass [ ] Fail |
| 13 | Create 2 additional rules for other fields | All 3 rules visible on profile | | [ ] Pass [ ] Fail |
| 14 | Activate profile (Is Active = Yes) | Profile marked active | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Profile form accepts all inputs
- ✅ Rules can be added to profile
- ✅ Profile can be activated
- ✅ Multiple rules supported per profile

**Pass Criteria**: All 14 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-08: Field Mappings Apply on Child Record Creation

**Objective**: Verify field mappings auto-apply when Event created for a Matter

**Test Scenario**: Create Event for Matter with Configured Mappings

**Setup**:
- Field Mapping Profile "Matter to Event" exists and is active
- Test Matter record exists with Client field populated (e.g., "ABC Corp")
- AssociationResolver PCF deployed on Event form

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Open test Matter form | Matter shows Client field = "ABC Corp" | | [ ] Pass [ ] Fail |
| 2 | Open + New Event form | Event form loads with empty fields | | [ ] Pass [ ] Fail |
| 3 | In AssociationResolver, select test Matter | AssociationResolver populates Regarding Matter and other regarding fields | | [ ] Pass [ ] Fail |
| 4 | Observe mapped fields | Regarding Account field auto-populated with "ABC Corp" (from Matter.Client) | | [ ] Pass [ ] Fail |
| 5 | Verify toast notification appears | Toast shows "Field mapping applied: 1 field(s) populated from Matter" (or similar) | | [ ] Pass [ ] Fail |
| 6 | Save Event | Event saves with mapped fields intact | | [ ] Pass [ ] Fail |
| 7 | Reopen Event | Mapped fields still populated with values from Matter | | [ ] Pass [ ] Fail |
| 8 | Test with second Matter having different Client value | Second Event maps correctly with second Matter's Client value | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Mappings apply automatically on record selection
- ✅ Correct field values copied from parent
- ✅ User notification provided
- ✅ Mappings persist after save

**Pass Criteria**: All 8 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-09: Refresh from Parent Button Functionality

**Objective**: Verify "Refresh from Parent" button re-applies mappings

**Test Scenario**: Update Parent Matter and Refresh Event

**Setup**:
- Event created with field mappings from Matter
- Field Mapping Profile has "Manual Refresh" sync mode enabled

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Open Event form created with Matter mappings | Event shows mapped fields (e.g., Client = "ABC Corp") | | [ ] Pass [ ] Fail |
| 2 | Verify "Refresh from Parent" button visible on form | Button visible and enabled | | [ ] Pass [ ] Fail |
| 3 | Go to parent Matter form, change Client to "XYZ Inc" | Matter updated with new Client value | | [ ] Pass [ ] Fail |
| 4 | Return to Event form | Event still shows old Client value ("ABC Corp") | | [ ] Pass [ ] Fail |
| 5 | Click "Refresh from Parent" button | Button processes, toast shows "Refresh complete: 1 field(s) updated" (or similar) | | [ ] Pass [ ] Fail |
| 6 | Observe Event form after refresh | Client field updated to "XYZ Inc" (new value from Matter) | | [ ] Pass [ ] Fail |
| 7 | Save Event | Event saves with refreshed values | | [ ] Pass [ ] Fail |
| 8 | Test with multiple fields being refreshed | All mapped fields update from parent values | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Refresh button available on Event form
- ✅ Button queries current parent values
- ✅ Mapped fields update to latest parent values
- ✅ User notification provided

**Pass Criteria**: All 8 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-10: Update Related Button - Push Mappings to Children

**Objective**: Verify "Update Related" button pushes mappings from parent to all children

**Test Scenario**: Update Matter and Push Changes to All Related Events

**Setup**:
- Matter with 4-5 associated Events
- Field Mapping Profile with "Update Related" button configured
- All Events have mapped fields from Matter

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Open Matter form with multiple related Events | Matter form loaded | | [ ] Pass [ ] Fail |
| 2 | Locate "Update Related" button (UpdateRelatedButton PCF) | Button visible on form | | [ ] Pass [ ] Fail |
| 3 | Change a mapped field on Matter (e.g., Client from "ABC Corp" to "New Corp") | Matter field updated | | [ ] Pass [ ] Fail |
| 4 | Click "Update Related" button | Confirmation dialog appears: "Update all related Event records with current values?" | | [ ] Pass [ ] Fail |
| 5 | Confirm in dialog | Button processes, progress indicator shows | | [ ] Pass [ ] Fail |
| 6 | Wait for completion | Toast notification appears: "Updated 4 of 4 Event records" (or similar) | | [ ] Pass [ ] Fail |
| 7 | Open each related Event one by one | All Events show updated Client value = "New Corp" | | [ ] Pass [ ] Fail |
| 8 | Test with partial success (if one child record fails) | Toast shows accurate count: "Updated 3 of 4 Event records" (errors listed) | | [ ] Pass [ ] Fail |
| 9 | Verify failed records identified | Error details provide record ID and reason for failure | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Update Related button visible on parent form
- ✅ Confirmation dialog prevents accidental updates
- ✅ All child records updated with parent values
- ✅ Accurate count returned
- ✅ Failed records identified

**Pass Criteria**: All 9 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-11: Type Compatibility Validation

**Objective**: Verify incompatible field type mappings are blocked in Strict mode

**Test Scenario**: Attempt to Create Incompatible Mapping

**Setup**:
- Field Mapping Profile admin form open
- FieldMappingAdmin PCF deployed on Field Mapping Rule form

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Create new Field Mapping Rule | Rule form opens | | [ ] Pass [ ] Fail |
| 2 | Set Source Field Type: "Text" | Type selected | | [ ] Pass [ ] Fail |
| 3 | Set Target Field Type: "Lookup" (incompatible) | Type selected | | [ ] Pass [ ] Fail |
| 4 | Set Compatibility Mode: "Strict" | Strict mode selected | | [ ] Pass [ ] Fail |
| 5 | Observe compatibility indicator | FieldMappingAdmin shows warning: "Text → Lookup not compatible in Strict mode" | | [ ] Pass [ ] Fail |
| 6 | Attempt to save rule | Form save blocked with validation error | | [ ] Pass [ ] Fail |
| 7 | Change Target Field Type to "Text" (compatible) | Type changed to compatible option | | [ ] Pass [ ] Fail |
| 8 | Observe compatibility indicator updates | Indicator now shows: ✅ Compatible | | [ ] Pass [ ] Fail |
| 9 | Save rule | Rule saves successfully | | [ ] Pass [ ] Fail |
| 10 | Test other incompatible combinations (Text→Number, etc.) | All incompatible mappings blocked with appropriate warnings | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Incompatible mappings identified by FieldMappingAdmin
- ✅ Save blocked for incompatible rules in Strict mode
- ✅ Clear error messages shown
- ✅ Compatible mappings allowed

**Pass Criteria**: All 10 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-12: Cascading Mappings Execution

**Objective**: Verify dependent mappings execute in correct sequence (two-pass)

**Test Scenario**: Rule A Populates Field X, Rule B Uses Field X as Source

**Setup**:
- Field Mapping Profile with 2 cascading rules:
  - Rule A: Matter.Client → Event.Regarding Account
  - Rule B: Event.Regarding Account (populated by Rule A) → Event.Custom Field
- Execution Order: Rule A = 1, Rule B = 2

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Set up cascading rule profile with correct execution order | Rules configured with IS_CASCADING_SOURCE flag and execution order | | [ ] Pass [ ] Fail |
| 2 | Create Event for Matter with cascading mappings | Event form loads with AssociationResolver | | [ ] Pass [ ] Fail |
| 3 | Select Matter in AssociationResolver | Mappings execute in sequence | | [ ] Pass [ ] Fail |
| 4 | Verify Rule A executes (Pass 1): Matter.Client → Regarding Account | Regarding Account field populated from Matter.Client | | [ ] Pass [ ] Fail |
| 5 | Verify Rule B executes (Pass 2): Regarding Account → Custom Field | Custom field populated from the value set by Rule A | | [ ] Pass [ ] Fail |
| 6 | Check execution order in profile | Both fields show correct final values from two-pass execution | | [ ] Pass [ ] Fail |
| 7 | Test with 3-level cascade (Rule A → Rule B → Rule C) | All three rules execute in sequence, final field populated correctly | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ Cascading rules execute in order
- ✅ Second rule uses output of first rule
- ✅ Two-pass execution prevents infinite loops
- ✅ Final field receives correct value

**Pass Criteria**: All 7 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-13: Push API Returns Accurate Counts

**Objective**: Verify field mapping Push API returns correct update counts

**Test Scenario**: Push Mappings and Verify Response Counts

**Setup**:
- Matter with 5 related Events
- Field Mapping Profile for Matter → Event
- Test API client ready (Postman, etc.)

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Call API: POST /api/v1/field-mappings/push with Matter ID | Request sent, response returned | | [ ] Pass [ ] Fail |
| 2 | Check response: "totalRecords" field | Response shows totalRecords = 5 (all related Events) | | [ ] Pass [ ] Fail |
| 3 | Check response: "updatedRecords" field | If all succeed: updatedRecords = 5 | | [ ] Pass [ ] Fail |
| 4 | Check response: "failedRecords" field | If all succeed: failedRecords = 0 | | [ ] Pass [ ] Fail |
| 5 | Verify all 5 Events in Dataverse have updated field values | Manual check confirms all 5 Events have new mapped values | | [ ] Pass [ ] Fail |
| 6 | Test with one Event having insufficient permissions (read-only) | API response: updatedRecords = 4, failedRecords = 1 | | [ ] Pass [ ] Fail |
| 7 | Check error array in response | Errors array contains record ID and reason ("Insufficient permissions") | | [ ] Pass [ ] Fail |
| 8 | Verify counts match actual updates in Dataverse | 4 Events updated, 1 skipped matches API response | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ totalRecords matches actual child record count
- ✅ updatedRecords matches successfully updated records
- ✅ failedRecords matches failed records
- ✅ Error details provided for failures

**Pass Criteria**: All 8 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-14: Dark Mode Support - All PCF Controls

**Objective**: Verify all PCF controls render correctly in dark mode

**Test Scenario**: Toggle Dark Theme and Verify UI

**Setup**:
- Access Dataverse Settings > Personalization Options
- Controls deployed: AssociationResolver, RegardingLink, EventFormController, FieldMappingAdmin, UpdateRelatedButton

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Switch theme to Light mode | UI renders in light theme | | [ ] Pass [ ] Fail |
| 2 | Open Event form with all controls | AssociationResolver, EventFormController visible and readable | | [ ] Pass [ ] Fail |
| 3 | Open All Events view | RegardingLink PCF renders correctly in light mode | | [ ] Pass [ ] Fail |
| 4 | Open Field Mapping Rule form | FieldMappingAdmin PCF renders correctly in light mode | | [ ] Pass [ ] Fail |
| 5 | Go to Settings > Personalization Options, select Dark theme | System switches to dark mode | | [ ] Pass [ ] Fail |
| 6 | Refresh Event form | AssociationResolver renders in dark mode: text readable, no white backgrounds | | [ ] Pass [ ] Fail |
| 7 | Test dark mode with All Events view | RegardingLink links visible and clickable in dark background | | [ ] Pass [ ] Fail |
| 8 | Test dark mode with Field Mapping forms | All form controls readable with dark theme | | [ ] Pass [ ] Fail |
| 9 | Toggle back to Light mode | UI switches cleanly back to light theme | | [ ] Pass [ ] Fail |
| 10 | Test with High Contrast theme (if available) | All controls render correctly with high contrast | | [ ] Pass [ ] Fail |
| 11 | Verify no console errors in dark mode | Browser dev tools show no errors related to theme | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ All controls render in light mode
- ✅ All controls render in dark mode
- ✅ Text readable in both themes
- ✅ No hard-coded colors visible
- ✅ Links/buttons still clickable in dark mode
- ✅ Theme changes apply without page refresh

**Pass Criteria**: All 11 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

### SC-15: PCF Bundle Sizes Under 1MB

**Objective**: Verify PCF control bundles meet size requirements (< 1MB per control)

**Test Scenario**: Verify Bundle File Sizes

**Setup**:
- PCF build output available
- Check compiled bundle files

**Test Steps**:

| Step # | Action | Expected Result | Actual Result | Status |
|--------|--------|-----------------|----------------|--------|
| 1 | Locate AssociationResolver bundle file | Bundle found: AssociationResolver*.bundle.js | | [ ] Pass [ ] Fail |
| 2 | Check file size | Size < 1MB (e.g., 850KB) | | [ ] Pass [ ] Fail |
| 3 | Locate RegardingLink bundle | Bundle found: RegardingLink*.bundle.js | | [ ] Pass [ ] Fail |
| 4 | Check file size | Size < 1MB | | [ ] Pass [ ] Fail |
| 5 | Locate EventFormController bundle | Bundle found: EventFormController*.bundle.js | | [ ] Pass [ ] Fail |
| 6 | Check file size | Size < 1MB | | [ ] Pass [ ] Fail |
| 7 | Locate FieldMappingAdmin bundle | Bundle found: FieldMappingAdmin*.bundle.js | | [ ] Pass [ ] Fail |
| 8 | Check file size | Size < 1MB | | [ ] Pass [ ] Fail |
| 9 | Locate UpdateRelatedButton bundle | Bundle found: UpdateRelatedButton*.bundle.js | | [ ] Pass [ ] Fail |
| 10 | Check file size | Size < 1MB | | [ ] Pass [ ] Fail |
| 11 | Verify platform-library declarations in ControlManifest.xml | React and Fluent UI declared as platform-library (not bundled) | | [ ] Pass [ ] Fail |
| 12 | Calculate total solution size | Solution size reasonable, PCF components not oversized | | [ ] Pass [ ] Fail |

**Expected Results**:
- ✅ All 5 controls under 1MB
- ✅ Platform libraries declared (React, Fluent UI not bundled)
- ✅ Solution size optimal

**Pass Criteria**: All 12 steps pass

**Notes**:

Tester: ________________
Result: [ ] PASS [ ] FAIL
Issues/Comments: _________________________________________________________________

---

## Test Execution Summary

### Test Results by Criterion

| SC # | Criterion | Result | Tester | Date |
|------|-----------|--------|--------|------|
| SC-01 | AssociationResolver - 8 Entity Types | [ ] PASS [ ] FAIL | | |
| SC-02 | RegardingLink - Grid Navigation | [ ] PASS [ ] FAIL | | |
| SC-03 | EventFormController - Field Visibility | [ ] PASS [ ] FAIL | | |
| SC-04 | Entity Subgrids - Filtered Views | [ ] PASS [ ] FAIL | | |
| SC-05 | Event Log - State Tracking | [ ] PASS [ ] FAIL | | |
| SC-06 | Event API - Functional | [ ] PASS [ ] FAIL | | |
| SC-07 | Field Mapping Admin - Profile Config | [ ] PASS [ ] FAIL | | |
| SC-08 | Field Mappings - Auto-Apply | [ ] PASS [ ] FAIL | | |
| SC-09 | Refresh from Parent - Button | [ ] PASS [ ] FAIL | | |
| SC-10 | Update Related - Push to Children | [ ] PASS [ ] FAIL | | |
| SC-11 | Type Compatibility - Validation | [ ] PASS [ ] FAIL | | |
| SC-12 | Cascading Mappings - Multi-Pass | [ ] PASS [ ] FAIL | | |
| SC-13 | Push API - Accurate Counts | [ ] PASS [ ] FAIL | | |
| SC-14 | Dark Mode - All Controls | [ ] PASS [ ] FAIL | | |
| SC-15 | Bundle Sizes - < 1MB | [ ] PASS [ ] FAIL | | |

---

## Issues Log

### Critical Issues (Block Release)

| Issue # | Criterion | Description | Severity | Assigned To | Status | Resolution |
|---------|-----------|-------------|----------|------------|--------|-----------|
| | | | | | | |

---

### High Priority Issues (Deferred to Future Release)

| Issue # | Criterion | Description | Severity | Assigned To | Status | Resolution |
|---------|-----------|-------------|----------|------------|--------|-----------|
| | | | | | | |

---

### Low Priority / Known Limitations (For Documentation)

| Issue # | Criterion | Description | Severity | Assigned To | Status | Resolution |
|---------|-----------|-------------|----------|------------|--------|-----------|
| | | | | | | |

---

## UAT Sign-Off

### Test Completion Status

- **Total Criteria Tested**: 15
- **Passed**: ____ / 15
- **Failed**: ____ / 15
- **Deferred**: ____ / 15

### Overall Assessment

[ ] System **APPROVED FOR RELEASE**
[ ] System **APPROVED WITH DEFERRED ISSUES** (list below)
[ ] System **REQUIRES FIXES** (cannot release)

**Deferred Issues** (if any, explain why deferral is acceptable):

_____________________________________________________________________

_____________________________________________________________________

---

## Stakeholder Sign-Off

### Product Owner

**Name**: ______________________________
**Title**: ______________________________
**Organization**: ______________________________
**Date**: ______________________________
**Signature**: ______________________________

**Approval**: [ ] Approve for Release [ ] Approve with Deferrals [ ] Reject

---

### Lead User / Admin Representative

**Name**: ______________________________
**Title**: ______________________________
**Organization**: ______________________________
**Date**: ______________________________
**Signature**: ______________________________

**Approval**: [ ] Approve for Release [ ] Approve with Deferrals [ ] Reject

---

### Technical Lead

**Name**: ______________________________
**Title**: ______________________________
**Organization**: ______________________________
**Date**: ______________________________
**Signature**: ______________________________

**Approval**: [ ] Approve for Release [ ] Approve with Deferrals [ ] Reject

---

## UAT Notes & Recommendations

### What Went Well
-

### Areas for Improvement
-

### Recommendations for Future Releases
-

---

*UAT Document Version 1.0 | Events and Workflow Automation R1 | 2026-02-01*
