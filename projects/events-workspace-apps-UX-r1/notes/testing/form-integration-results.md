# Form Integration Testing Results

> **Project**: Events Workspace Apps UX R1
> **Test Type**: Form Integration Testing (Matter/Project)
> **Task**: 070
> **Created**: 2026-02-04
> **Status**: Test Plan Ready

---

## Test Summary

| Metric | Value |
|--------|-------|
| **Total Test Scenarios** | 32 |
| **Forms Tested** | Matter, Project |
| **Components Tested** | 5 (EventCalendarFilter, UniversalDatasetGrid, EventDetailSidePane, DueDatesWidget, Form Communication) |
| **Test Status** | Ready for Manual Execution |

---

## Prerequisites

### Environment Setup

- [ ] Dev environment: `https://spaarkedev1.crm.dynamics.com`
- [ ] User has System Administrator or equivalent role
- [ ] Test Matter record exists with 10+ Events (various dates, statuses, types)
- [ ] Test Project record exists with 10+ Events (various dates, statuses, types)
- [ ] Multiple Event Types configured with different `sprk_fieldconfigjson` values
- [ ] Events span past (overdue), today, this week, and future dates

### Component Deployment Verification

| Component | Version | Deployment Status |
|-----------|---------|-------------------|
| EventCalendarFilter PCF | 1.0.0 | Verify in solution |
| UniversalDatasetGrid PCF | (enhanced) | Verify in solution |
| EventDetailSidePane Custom Page | 1.0.0 | Verify in make.powerapps.com |
| DueDatesWidget PCF | 1.0.0 | Verify in solution |

---

## Test Scenarios

### Section 1: EventCalendarFilter PCF on Forms

#### TC-1.1: Calendar Rendering

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-1.1.1 | Calendar displays on Matter form Events tab | 1. Navigate to Matter record<br>2. Click "Events" tab | Calendar renders with 2-3 months visible | [ ] Pass / [ ] Fail |
| TC-1.1.2 | Calendar displays on Project form Events tab | 1. Navigate to Project record<br>2. Click "Events" tab | Calendar renders with 2-3 months visible | [ ] Pass / [ ] Fail |
| TC-1.1.3 | Event indicators show on dates with Events | 1. Observe calendar dates<br>2. Compare with known Event due dates | Dots/badges appear on dates that have Events | [ ] Pass / [ ] Fail |
| TC-1.1.4 | Dark mode renders correctly | 1. Toggle system dark mode (or Dataverse theme)<br>2. Observe calendar | Calendar uses proper dark theme colors, no hard-coded colors visible | [ ] Pass / [ ] Fail |

#### TC-1.2: Single Date Selection

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-1.2.1 | Click date filters grid to single date | 1. Click a date with Events<br>2. Observe grid below calendar | Grid shows only Events due on selected date | [ ] Pass / [ ] Fail |
| TC-1.2.2 | Click date without Events | 1. Click a date with no Events | Grid shows empty state (no Events) | [ ] Pass / [ ] Fail |
| TC-1.2.3 | Selected date visual indicator | 1. Click a date | Selected date shows highlight/selection state | [ ] Pass / [ ] Fail |

#### TC-1.3: Range Selection

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-1.3.1 | Shift+click creates range selection | 1. Click start date<br>2. Shift+click end date | Date range highlighted, grid filters to range | [ ] Pass / [ ] Fail |
| TC-1.3.2 | Range spanning multiple months | 1. Select start date in Month 1<br>2. Shift+click date in Month 2 | Range correctly spans months, grid filters accordingly | [ ] Pass / [ ] Fail |

#### TC-1.4: Clear Filter

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-1.4.1 | Clear filter returns to all Events | 1. Select a date (filter active)<br>2. Click "Clear" or click outside dates | Grid returns to showing all Events | [ ] Pass / [ ] Fail |
| TC-1.4.2 | Calendar selection cleared visually | 1. Clear filter | Calendar shows no date selection | [ ] Pass / [ ] Fail |

---

### Section 2: UniversalDatasetGrid on Forms

#### TC-2.1: Grid Display

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-2.1.1 | Grid displays Events on Matter form | 1. Navigate to Matter record Events tab | Grid shows Events related to Matter | [ ] Pass / [ ] Fail |
| TC-2.1.2 | Grid displays Events on Project form | 1. Navigate to Project record Events tab | Grid shows Events related to Project | [ ] Pass / [ ] Fail |
| TC-2.1.3 | Grid matches Power Apps styling | 1. Compare grid with standard Dataverse grid | Styling matches (fonts, colors, row height) | [ ] Pass / [ ] Fail |
| TC-2.1.4 | Dark mode renders correctly | 1. Toggle dark mode<br>2. Observe grid | Grid uses proper dark theme colors | [ ] Pass / [ ] Fail |

#### TC-2.2: Calendar Filter Integration

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-2.2.1 | Grid receives calendar date filter | 1. Select date in calendar | Grid updates to show only Events for selected date | [ ] Pass / [ ] Fail |
| TC-2.2.2 | Grid receives calendar range filter | 1. Select date range in calendar | Grid shows Events within date range | [ ] Pass / [ ] Fail |
| TC-2.2.3 | Hidden field `sprk_calendarfilter` updates | 1. Select date in calendar<br>2. Check hidden field value (dev tools) | Field contains filter JSON: `{"type":"single","date":"2026-02-XX"}` | [ ] Pass / [ ] Fail |

#### TC-2.3: Bi-directional Sync (Row to Calendar)

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-2.3.1 | Row click highlights calendar date | 1. Click a grid row (not checkbox, not hyperlink)<br>2. Observe calendar | Calendar highlights the due date of selected Event | [ ] Pass / [ ] Fail |
| TC-2.3.2 | Multiple row selections | 1. Select row A<br>2. Select row B | Calendar highlights row B's date (most recent selection) | [ ] Pass / [ ] Fail |

#### TC-2.4: Hyperlink Column (Side Pane Opening)

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-2.4.1 | Event Name hyperlink opens Side Pane | 1. Click Event Name hyperlink in grid | Side Pane opens showing Event details | [ ] Pass / [ ] Fail |
| TC-2.4.2 | Hyperlink does NOT navigate away | 1. Click Event Name hyperlink | Page remains on current record (no navigation) | [ ] Pass / [ ] Fail |
| TC-2.4.3 | Side Pane shows correct Event | 1. Click hyperlink for "Test Event A" | Side Pane shows "Test Event A" details | [ ] Pass / [ ] Fail |

#### TC-2.5: Checkbox Column

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-2.5.1 | Checkbox selects row for bulk action | 1. Click checkbox on row | Row selected (visual indicator), no Side Pane opens | [ ] Pass / [ ] Fail |
| TC-2.5.2 | Multiple checkboxes for multi-select | 1. Check row A<br>2. Check row B | Both rows selected | [ ] Pass / [ ] Fail |
| TC-2.5.3 | Checkbox vs hyperlink distinction | 1. Click checkbox (no pane)<br>2. Click hyperlink (pane opens) | Different behaviors for checkbox vs hyperlink | [ ] Pass / [ ] Fail |

---

### Section 3: EventDetailSidePane on Forms

#### TC-3.1: Side Pane Opening

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-3.1.1 | Side Pane opens via Xrm.App.sidePanes | 1. Click Event Name hyperlink | Side Pane opens at 400px width | [ ] Pass / [ ] Fail |
| TC-3.1.2 | Side Pane is closeable | 1. Open Side Pane<br>2. Click close button | Side Pane closes | [ ] Pass / [ ] Fail |
| TC-3.1.3 | Side Pane reuses for different Events | 1. Open Side Pane for Event A<br>2. Click Event B hyperlink | Pane content updates to Event B (no close/reopen) | [ ] Pass / [ ] Fail |

#### TC-3.2: Header Section

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-3.2.1 | Event Name displays and is editable | 1. Open Side Pane<br>2. Observe header | Event Name shown, editable input field | [ ] Pass / [ ] Fail |
| TC-3.2.2 | Event Type displays (read-only) | 1. Observe header | Event Type shown, not editable | [ ] Pass / [ ] Fail |
| TC-3.2.3 | Parent link is clickable | 1. Click parent record link (Matter/Project) | Navigates to parent record | [ ] Pass / [ ] Fail |

#### TC-3.3: Status Section

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-3.3.1 | Status segmented buttons render | 1. Open Side Pane<br>2. Observe status section | Segmented buttons: Draft, Open, On Hold, Complete, Cancelled | [ ] Pass / [ ] Fail |
| TC-3.3.2 | Current status is selected | 1. Open Event with "Open" status | "Open" button is selected/highlighted | [ ] Pass / [ ] Fail |
| TC-3.3.3 | Status change updates Event | 1. Click "Complete"<br>2. Save | Event status changes to Complete | [ ] Pass / [ ] Fail |

#### TC-3.4: Event Type-Aware Field Visibility

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-3.4.1 | Different Event Types show different fields | 1. Open Event with Type A (has config)<br>2. Open Event with Type B (different config) | Field visibility differs based on Event Type configuration | [ ] Pass / [ ] Fail |
| TC-3.4.2 | Sections collapse/expand per config | 1. Check Event Type with `sectionDefaults` | Sections collapsed/expanded per configuration | [ ] Pass / [ ] Fail |
| TC-3.4.3 | Hidden fields are not shown | 1. Event Type config hides "Description"<br>2. Open Event | Description section not visible | [ ] Pass / [ ] Fail |

#### TC-3.5: Save and Optimistic Update

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-3.5.1 | Save button triggers WebAPI save | 1. Edit Event Name<br>2. Click Save | Save completes via WebAPI | [ ] Pass / [ ] Fail |
| TC-3.5.2 | Optimistic row update (no full refresh) | 1. Edit Event Name to "Updated Name"<br>2. Save<br>3. Observe grid | Grid row updates to "Updated Name" without full grid refresh | [ ] Pass / [ ] Fail |
| TC-3.5.3 | Scroll position preserved | 1. Scroll grid down<br>2. Edit and save in Side Pane | Grid scroll position maintained | [ ] Pass / [ ] Fail |
| TC-3.5.4 | Error rollback works | 1. Create condition that causes save failure<br>2. Observe | UI rolls back to previous state, error displayed | [ ] Pass / [ ] Fail |

#### TC-3.6: Unsaved Changes Prompt

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-3.6.1 | Prompt on close with unsaved changes | 1. Edit a field<br>2. Click close | Prompt: "You have unsaved changes. Discard?" | [ ] Pass / [ ] Fail |
| TC-3.6.2 | Prompt on Event switch with unsaved | 1. Edit a field<br>2. Click different Event hyperlink | Prompt: "You have unsaved changes. Discard?" | [ ] Pass / [ ] Fail |
| TC-3.6.3 | No prompt when no changes | 1. Open Side Pane (no edits)<br>2. Close | Closes immediately, no prompt | [ ] Pass / [ ] Fail |

#### TC-3.7: Security Role Awareness

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-3.7.1 | Read-only user sees disabled fields | 1. Login as user without edit permission<br>2. Open Side Pane | Fields are read-only/disabled | [ ] Pass / [ ] Fail |
| TC-3.7.2 | Read-only user has no Save button | 1. Login as read-only user | Save button not visible | [ ] Pass / [ ] Fail |

---

### Section 4: DueDatesWidget on Overview Tab

#### TC-4.1: Widget Display

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-4.1.1 | Widget displays on Matter Overview tab | 1. Navigate to Matter record<br>2. Stay on Overview tab | Due Dates Widget visible | [ ] Pass / [ ] Fail |
| TC-4.1.2 | Widget displays on Project Overview tab | 1. Navigate to Project record<br>2. Stay on Overview tab | Due Dates Widget visible | [ ] Pass / [ ] Fail |
| TC-4.1.3 | Dark mode renders correctly | 1. Toggle dark mode | Widget uses proper dark theme colors | [ ] Pass / [ ] Fail |

#### TC-4.2: Filter Logic

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-4.2.1 | Shows actionable Events only | 1. Verify test data has actionable and non-actionable Event Types<br>2. Observe widget | Only actionable Events shown (Task, Deadline, Reminder, Action categories) | [ ] Pass / [ ] Fail |
| TC-4.2.2 | Shows Active Events only | 1. Verify test data has Active and Completed Events<br>2. Observe widget | Only Active Events shown | [ ] Pass / [ ] Fail |
| TC-4.2.3 | Shows MIN(7 days, MAX(5, count)) | 1. Verify count logic | At least 5 items or all in next 7 days | [ ] Pass / [ ] Fail |

#### TC-4.3: Card Display

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-4.3.1 | Overdue items displayed in red | 1. Create overdue Event<br>2. Observe widget | Overdue card has red indicator | [ ] Pass / [ ] Fail |
| TC-4.3.2 | Overdue items sorted first | 1. Have mix of overdue and upcoming<br>2. Observe order | Overdue items appear at top | [ ] Pass / [ ] Fail |
| TC-4.3.3 | Card shows date, name, type, countdown | 1. Observe card content | Date/weekday, Event name (bold), Event Type, days-until-due badge | [ ] Pass / [ ] Fail |
| TC-4.3.4 | Urgency color coding | 1. Verify: Overdue=red, Today=amber, Tomorrow=amber, 2-7d=default, >7d=muted | Colors match specification | [ ] Pass / [ ] Fail |

#### TC-4.4: Card Click Navigation

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-4.4.1 | Click card navigates to Events tab | 1. Click a card in widget | Form navigates to Events tab | [ ] Pass / [ ] Fail |
| TC-4.4.2 | Click card opens Side Pane | 1. Click a card | Side Pane opens for that Event | [ ] Pass / [ ] Fail |

#### TC-4.5: All Events Link

| ID | Scenario | Steps | Expected Result | Status |
|----|----------|-------|-----------------|--------|
| TC-4.5.1 | "All Events" link visible | 1. Observe widget footer | "All Events" link present | [ ] Pass / [ ] Fail |
| TC-4.5.2 | Click navigates to Events tab | 1. Click "All Events" | Form navigates to Events tab | [ ] Pass / [ ] Fail |

---

## Cross-Form Verification

### Matter Form Tests

| Test ID | Description | Status |
|---------|-------------|--------|
| MF-1 | All components render on Matter form | [ ] Pass / [ ] Fail |
| MF-2 | Calendar-Grid sync works on Matter | [ ] Pass / [ ] Fail |
| MF-3 | Side Pane opens from Matter grid | [ ] Pass / [ ] Fail |
| MF-4 | DueDatesWidget works on Matter Overview | [ ] Pass / [ ] Fail |
| MF-5 | Dark mode works across all Matter components | [ ] Pass / [ ] Fail |

### Project Form Tests

| Test ID | Description | Status |
|---------|-------------|--------|
| PF-1 | All components render on Project form | [ ] Pass / [ ] Fail |
| PF-2 | Calendar-Grid sync works on Project | [ ] Pass / [ ] Fail |
| PF-3 | Side Pane opens from Project grid | [ ] Pass / [ ] Fail |
| PF-4 | DueDatesWidget works on Project Overview | [ ] Pass / [ ] Fail |
| PF-5 | Dark mode works across all Project components | [ ] Pass / [ ] Fail |

---

## Event Type Configuration Tests

Verify the following Event Types have different field visibility configurations:

| Event Type | Expected Visible Fields | Expected Hidden Fields | Test Status |
|------------|------------------------|------------------------|-------------|
| Task | Due Date, Priority, Owner, Description | Related Event | [ ] Verified |
| Deadline | Due Date, Priority, Owner | Description, Related Event | [ ] Verified |
| Reminder | Due Date, Owner | Priority, Description, Related Event | [ ] Verified |
| Meeting | Due Date, Owner, Description | Priority | [ ] Verified |

---

## Manual Testing Steps Summary

### Pre-Test Setup

1. **Verify Deployment**: Confirm all components are deployed to dev environment
2. **Create Test Data**:
   - Create Matter record with 15+ Events (various dates, statuses, types)
   - Create Project record with 15+ Events (various dates, statuses, types)
   - Ensure Events include: overdue, today, tomorrow, this week, next week, future
   - Configure Event Types with different `sprk_fieldconfigjson` values
3. **Prepare Accounts**: Have admin user and read-only user available

### Test Execution Order

1. **Section 1**: EventCalendarFilter (TC-1.1 through TC-1.4)
2. **Section 2**: UniversalDatasetGrid (TC-2.1 through TC-2.5)
3. **Section 3**: EventDetailSidePane (TC-3.1 through TC-3.7)
4. **Section 4**: DueDatesWidget (TC-4.1 through TC-4.5)
5. **Cross-Form**: Repeat key tests on Project form
6. **Event Type Config**: Verify field visibility variations

### Post-Test Actions

1. Document any failures in "Issues Found" section below
2. Create bug reports for critical failures
3. Update task status based on results

---

## Issues Found

| Issue ID | Severity | Description | Component | Reproduction Steps | Status |
|----------|----------|-------------|-----------|-------------------|--------|
| *No issues documented yet* | | | | | |

---

## Test Environment Details

| Property | Value |
|----------|-------|
| **Environment URL** | https://spaarkedev1.crm.dynamics.com |
| **Browser** | Chrome / Edge (specify during test) |
| **Browser Version** | (document during test) |
| **OS** | Windows 11 (specify during test) |
| **Screen Resolution** | (document during test) |
| **Test Date** | (document during test) |
| **Tester** | (document during test) |

---

## Success Criteria Mapping

| Spec Success Criterion | Related Test Cases | Status |
|------------------------|-------------------|--------|
| SC-02: Calendar date selection filters grid correctly | TC-1.2.1, TC-2.2.1 | Pending |
| SC-03: Calendar range selection works | TC-1.3.1, TC-1.3.2, TC-2.2.2 | Pending |
| SC-04: Grid row click highlights calendar date | TC-2.3.1 | Pending |
| SC-05: Event Name hyperlink opens Side Pane | TC-2.4.1, TC-2.4.2, TC-2.4.3 | Pending |
| SC-06: Side Pane shows Event Type-aware layout | TC-3.4.1, TC-3.4.2, TC-3.4.3 | Pending |
| SC-07: Side Pane save updates record optimistically | TC-3.5.1, TC-3.5.2, TC-3.5.3 | Pending |
| SC-09: All components support dark mode | TC-1.1.4, TC-2.1.4, TC-4.1.3 | Pending |
| SC-10: Checkbox vs hyperlink pattern works | TC-2.5.3 | Pending |
| SC-11: Grid matches Power Apps look/feel | TC-2.1.3 | Pending |
| SC-12: Side pane respects security role | TC-3.7.1, TC-3.7.2 | Pending |

---

## Acceptance Criteria Verification

| Criterion | Test Coverage | Status |
|-----------|---------------|--------|
| Calendar-Grid sync works on both forms | TC-2.2.x, MF-2, PF-2 | [ ] Verified |
| Side Pane opens from grid on both forms | TC-2.4.x, TC-3.1.x, MF-3, PF-3 | [ ] Verified |
| DueDatesWidget functional on Overview tabs | TC-4.x, MF-4, PF-4 | [ ] Verified |
| All issues documented | Issues Found section | [ ] Verified |

---

*Test plan created: 2026-02-04*
*Last updated: 2026-02-04*
*Status: Ready for Manual Execution*
