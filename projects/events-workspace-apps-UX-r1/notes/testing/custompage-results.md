# Events Custom Page Integration Test Plan

> **Task**: 071 - Events Custom Page Testing
> **Created**: 2026-02-04
> **Status**: Test Plan Document
> **Tester**: [Enter name]

---

## Overview

This document provides a comprehensive test plan for the Events Custom Page system-level view. The page combines:
- **CalendarSection** - Date selection/filtering with multi-month display
- **GridSection** - Events data grid with filtering and row click
- **AssignedToFilter** - User assignment filter dropdown
- **RecordTypeFilter** - Event type filter dropdown
- **StatusFilter** - Status reason filter dropdown
- **EventDetailSidePane** - Side pane for event editing (opens on row click)

**Components communicate via React Context** (`EventsPageContext`), enabling centralized state management across all filter components and sections.

---

## Test Environment

| Item | Value |
|------|-------|
| Environment URL | https://spaarkedev1.crm.dynamics.com |
| Test Date | |
| Browser(s) | Edge, Chrome |
| Test User | |
| Theme Tested | Light / Dark (both required) |

---

## 1. Page Load and Navigation Tests

### 1.1 Sitemap Navigation

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 1.1.1 | Navigate via sitemap | Click "Events" in sitemap navigation | Events Custom Page loads without errors | [ ] | |
| 1.1.2 | Page title correct | Observe page title | Title shows "Events" (not "My Events") | [ ] | FR-05.3 |
| 1.1.3 | URL format | Check browser URL | Custom Page URL format (not OOB grid) | [ ] | |
| 1.1.4 | Console errors | Open browser DevTools Console | No console errors on page load | [ ] | |

### 1.2 Initial Load State

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 1.2.1 | Calendar renders | Observe left panel | 3-month calendar displays correctly | [ ] | |
| 1.2.2 | Grid renders | Observe main panel | Event grid loads with data | [ ] | |
| 1.2.3 | Filters render | Observe header toolbar | Assigned To, Type, Status filters visible | [ ] | |
| 1.2.4 | Default status filter | Check Status filter value | Shows "Actionable" (Draft, Planned, Open, On Hold) | [ ] | FR-05.7 |
| 1.2.5 | Version footer | Observe page footer | Version number displays (e.g., v1.5.0) | [ ] | ADR-021 |
| 1.2.6 | Loading states | Observe during data fetch | Spinner shows while loading, then data appears | [ ] | |

---

## 2. Calendar Section Tests

### 2.1 Calendar Display

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 2.1.1 | Month headers | Observe calendar | Current month + 2 future months shown | [ ] | FR-02.7 |
| 2.1.2 | Day headers | Observe calendar | Sun-Sat headers display correctly | [ ] | |
| 2.1.3 | Today highlight | Find today's date | Today's date has visual emphasis | [ ] | |
| 2.1.4 | Other month days | Observe start/end of months | Days from prev/next month show muted | [ ] | |

### 2.2 Event Indicators

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 2.2.1 | Event dots show | Find dates with events | Dot indicator appears below date number | [ ] | FR-02.2 |
| 2.2.2 | No dots on empty dates | Find dates without events | No indicator on dates without events | [ ] | |

### 2.3 Single Date Selection

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 2.3.1 | Click date | Click a date with events | Date highlights, grid filters to that date | [ ] | FR-02.3 |
| 2.3.2 | Selection info shows | After clicking date | "Selected: YYYY-MM-DD" banner appears | [ ] | |
| 2.3.3 | Grid updates | After date selection | Grid shows only events for selected date | [ ] | FR-03.1 |
| 2.3.4 | Toggle off | Click same date again | Selection clears, grid shows all events | [ ] | FR-02.5 |

### 2.4 Range Selection (Shift+Click)

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 2.4.1 | Start range | Click first date | Date highlights (single selection) | [ ] | |
| 2.4.2 | End range | Shift+click second date | Range highlights, dates in between also highlighted | [ ] | FR-02.4 |
| 2.4.3 | Range filter | After range selection | Grid shows events in date range | [ ] | |
| 2.4.4 | Range info shows | After range selection | "Selected: start to end" banner appears | [ ] | |
| 2.4.5 | Reverse range | Select later date first, Shift+click earlier | Range works in reverse direction | [ ] | |

### 2.5 Clear Selection

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 2.5.1 | Clear button | Click "Clear" button in calendar footer | Selection clears, grid shows all events | [ ] | FR-02.5 |
| 2.5.2 | Button visibility | With no selection | Clear button is hidden | [ ] | |
| 2.5.3 | Button visibility | With selection | Clear button is visible | [ ] | |

---

## 3. Grid Section Tests

### 3.1 Grid Display

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 3.1.1 | Columns display | Observe grid headers | Checkbox, Event Name, Regarding, Due Date, Status, Priority, Owner, Event Type | [ ] | |
| 3.1.2 | Data loads | Observe grid body | Events display with correct field values | [ ] | |
| 3.1.3 | Date formatting | Check Due Date column | Dates format as "MMM D, YYYY" (e.g., "Feb 15, 2026") | [ ] | |
| 3.1.4 | Status badges | Check Status column | Colored badges with correct labels | [ ] | |
| 3.1.5 | Priority styling | Check Priority column | High=red, Normal=default, Low=muted | [ ] | |
| 3.1.6 | Footer info | Observe grid footer | "X events - Y selected" + version | [ ] | |

### 3.2 Row Interaction

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 3.2.1 | Row hover | Hover over row | Row background highlights | [ ] | FR-03.6 |
| 3.2.2 | Row click | Click row (not checkbox) | Side Pane opens with event details | [ ] | FR-03.3 |
| 3.2.3 | Event Name click | Click Event Name hyperlink | Side Pane opens (same as row click) | [ ] | FR-03.3 |
| 3.2.4 | Click prevents propagation | Click checkbox | Only checkbox toggles, side pane doesn't open | [ ] | |

### 3.3 Checkbox Selection

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 3.3.1 | Single checkbox | Click checkbox on one row | Row selected, count updates | [ ] | FR-03.2 |
| 3.3.2 | Multiple checkboxes | Click checkboxes on multiple rows | All selected rows highlighted | [ ] | |
| 3.3.3 | Select all | Click header checkbox | All visible rows selected | [ ] | |
| 3.3.4 | Deselect all | Click header checkbox when all selected | All rows deselected | [ ] | |
| 3.3.5 | Mixed state | Select some rows, observe header | Header checkbox shows "mixed" state | [ ] | |

### 3.4 Regarding Column Navigation

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 3.4.1 | Regarding link visible | Find event with parent record | Regarding column shows hyperlink with parent name | [ ] | FR-05.4 |
| 3.4.2 | Click Regarding link | Click Regarding hyperlink | Navigates to parent record (Matter/Project form) | [ ] | FR-05.4 |
| 3.4.3 | No Regarding | Find event without parent | Regarding column shows "---" | [ ] | |
| 3.4.4 | Click doesn't open pane | Click Regarding link | Opens parent record, NOT side pane | [ ] | |

### 3.5 Empty and Error States

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 3.5.1 | Empty state - no data | Apply filter with no matching events | "No events found" message with filter hint | [ ] | |
| 3.5.2 | Loading state | During data fetch | Spinner with "Loading events..." | [ ] | |
| 3.5.3 | Error state | (Force network error if possible) | Error message displays | [ ] | |

---

## 4. Filter Tests

### 4.1 Assigned To Filter

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 4.1.1 | Dropdown opens | Click Assigned To dropdown | User list appears | [ ] | |
| 4.1.2 | Current user marked | Open dropdown | Current user shows "(me)" indicator | [ ] | |
| 4.1.3 | Search users | Type in search box | User list filters by name/email | [ ] | |
| 4.1.4 | Select single user | Select one user | Grid filters to that user's events | [ ] | FR-05.5 |
| 4.1.5 | Select multiple users | Select multiple users | Grid shows events for all selected | [ ] | |
| 4.1.6 | Clear selection | Deselect all users | Grid shows all events (no owner filter) | [ ] | |
| 4.1.7 | Display value | With multiple selected | Shows "X users selected" | [ ] | |

### 4.2 Record Type (Event Type) Filter

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 4.2.1 | Dropdown opens | Click Type dropdown | Event type list appears | [ ] | |
| 4.2.2 | Types load | Open dropdown | Filing Deadline, Meeting, Task, Hearing, etc. | [ ] | |
| 4.2.3 | Select type | Select one type | Grid filters to that event type | [ ] | FR-05.6 |
| 4.2.4 | Search types | Type in search box | Type list filters by name | [ ] | |
| 4.2.5 | Multiple types | Select multiple types | Grid shows events of all selected types | [ ] | |
| 4.2.6 | Clear selection | Deselect all types | Grid shows all event types | [ ] | |

### 4.3 Status Filter

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 4.3.1 | Default selection | On page load | "Actionable" statuses pre-selected | [ ] | FR-05.7 |
| 4.3.2 | Status options | Open dropdown | All 6 statuses visible with colored badges | [ ] | |
| 4.3.3 | Status colors | Observe dropdown | Draft=subtle, Planned=info, Open=brand, On Hold=warning, Completed=success, Cancelled=danger | [ ] | |
| 4.3.4 | Select single | Select only "Open" | Grid shows only Open events | [ ] | |
| 4.3.5 | Include completed | Add "Completed" to selection | Completed events appear in grid | [ ] | |
| 4.3.6 | Show all | Select all statuses | All events visible regardless of status | [ ] | |
| 4.3.7 | Display value | With actionable selected | Shows "Actionable" | [ ] | |

### 4.4 Filter Combinations

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 4.4.1 | Date + Status | Select date + specific status | Grid shows events matching both criteria | [ ] | |
| 4.4.2 | User + Type | Select user + event type | Grid shows events matching both criteria | [ ] | |
| 4.4.3 | All filters | Apply date + user + type + status | Grid shows events matching ALL criteria | [ ] | |
| 4.4.4 | No results | Apply restrictive combination | Empty state shows "No events found" | [ ] | |

### 4.5 Clear Filters

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 4.5.1 | Clear button visible | Apply any filter | "Clear filters" button appears | [ ] | |
| 4.5.2 | Clear button hidden | No filters applied | "Clear filters" button not visible | [ ] | |
| 4.5.3 | Clear all filters | Click "Clear filters" | All filters reset, grid shows default view | [ ] | |
| 4.5.4 | Status resets to default | After clear | Status filter resets to "Actionable" | [ ] | |

---

## 5. Side Pane Integration Tests

### 5.1 Side Pane Opening

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 5.1.1 | Open from row click | Click event row in grid | EventDetailSidePane opens on right | [ ] | FR-04.1 |
| 5.1.2 | Open from name link | Click Event Name hyperlink | Same pane opens | [ ] | |
| 5.1.3 | Pane width | Observe pane width | 400px width as specified | [ ] | FR-04.1 |
| 5.1.4 | Pane title | Observe pane title | Shows "Event Details" | [ ] | |
| 5.1.5 | Pane closeable | Check close button | X button present and functional | [ ] | FR-04.1 |

### 5.2 Side Pane Reuse

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 5.2.1 | Switch events | Click different row with pane open | Pane updates to new event (no close/reopen) | [ ] | FR-04.9 |
| 5.2.2 | Multiple clicks | Click several different events | Same pane instance reused | [ ] | |

### 5.3 Grid Update After Save

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 5.3.1 | Edit in side pane | Modify event in side pane and save | Grid row updates to reflect changes | [ ] | FR-03.7 |
| 5.3.2 | Status change | Change status in side pane | Status badge updates in grid | [ ] | |
| 5.3.3 | No full refresh | After save | Grid doesn't flash/reload entirely | [ ] | FR-03.7 |

---

## 6. Cross-Component Communication Tests

These tests verify the React Context integration (Task 066).

### 6.1 Calendar -> Grid Communication

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 6.1.1 | Date selection | Select date in calendar | Grid immediately filters | [ ] | |
| 6.1.2 | Range selection | Select range in calendar | Grid filters to range | [ ] | |
| 6.1.3 | Clear selection | Clear calendar selection | Grid shows all events (respecting other filters) | [ ] | |

### 6.2 Filters -> Grid Communication

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 6.2.1 | Assigned To change | Change Assigned To filter | Grid updates immediately | [ ] | |
| 6.2.2 | Type change | Change Type filter | Grid updates immediately | [ ] | |
| 6.2.3 | Status change | Change Status filter | Grid updates immediately | [ ] | |
| 6.2.4 | Combined updates | Change multiple filters quickly | Grid reflects all changes | [ ] | |

### 6.3 Grid -> Side Pane Communication

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 6.3.1 | Row click | Click row | Context updates activeEventId, pane opens | [ ] | |
| 6.3.2 | Event type passed | Click row | Side pane receives eventTypeId for field visibility | [ ] | |

### 6.4 Refresh Trigger

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 6.4.1 | Refresh button | Click refresh button in header | Grid reloads data | [ ] | |
| 6.4.2 | Data freshness | After refresh | Latest data from Dataverse displayed | [ ] | |

---

## 7. Dark Mode Tests

### 7.1 Theme Detection

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 7.1.1 | Light mode | With Dataverse in light mode | Page renders with light theme | [ ] | ADR-021 |
| 7.1.2 | Dark mode | With Dataverse in dark mode | Page renders with dark theme | [ ] | ADR-021 |
| 7.1.3 | Theme switch | Toggle Dataverse theme | Page updates to match | [ ] | |

### 7.2 Component Appearance (Dark Mode)

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 7.2.1 | Calendar | In dark mode | Calendar uses dark backgrounds, light text | [ ] | |
| 7.2.2 | Grid | In dark mode | Grid uses dark backgrounds, appropriate borders | [ ] | |
| 7.2.3 | Filters | In dark mode | Dropdowns render with dark theme | [ ] | |
| 7.2.4 | Status badges | In dark mode | Colors remain distinguishable | [ ] | |
| 7.2.5 | Selected states | In dark mode | Selection highlight visible | [ ] | |
| 7.2.6 | No hard-coded colors | Inspect all components | All colors use Fluent tokens | [ ] | ADR-021 |

---

## 8. Accessibility Tests

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 8.1 | Keyboard navigation | Tab through page | All interactive elements focusable | [ ] | NFR-06 |
| 8.2 | Calendar keyboard | Use Enter/Space on calendar dates | Date selection works with keyboard | [ ] | |
| 8.3 | Grid keyboard | Use arrow keys in grid | Row navigation works | [ ] | |
| 8.4 | ARIA labels | Inspect elements | Filters, checkboxes have appropriate labels | [ ] | |
| 8.5 | Focus indicators | Tab through elements | Focus rings visible | [ ] | |
| 8.6 | Screen reader | Use screen reader (NVDA/Narrator) | All content announced correctly | [ ] | |

---

## 9. Performance Tests

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 9.1 | Initial load | Time page load | Page loads < 3 seconds | [ ] | |
| 9.2 | Calendar filter | Time filter application | Grid updates < 500ms | [ ] | NFR-01 |
| 9.3 | Dropdown filter | Time filter change | Grid updates < 500ms | [ ] | |
| 9.4 | Side pane open | Time pane open | Pane appears < 300ms | [ ] | |
| 9.5 | Large dataset | Test with 100+ events | Page remains responsive | [ ] | NFR-02 |

---

## 10. Edge Cases and Error Handling

| # | Test Case | Steps | Expected Result | Status | Notes |
|---|-----------|-------|-----------------|--------|-------|
| 10.1 | No events at all | With empty Events entity | Empty state displays gracefully | [ ] | |
| 10.2 | Very long event names | Event with 200+ char name | Name truncates or wraps appropriately | [ ] | |
| 10.3 | Missing Regarding | Event without parent record | Regarding shows "---", no errors | [ ] | |
| 10.4 | Invalid event type | Event with deleted event type | Handles gracefully | [ ] | |
| 10.5 | Network timeout | Simulate slow network | Loading states show, no freeze | [ ] | |
| 10.6 | Concurrent edits | Two users edit same event | Last save wins, no corruption | [ ] | |

---

## Test Results Summary

| Section | Total | Passed | Failed | Blocked | Notes |
|---------|-------|--------|--------|---------|-------|
| 1. Page Load | 10 | | | | |
| 2. Calendar | 14 | | | | |
| 3. Grid | 18 | | | | |
| 4. Filters | 25 | | | | |
| 5. Side Pane | 8 | | | | |
| 6. Cross-Component | 10 | | | | |
| 7. Dark Mode | 8 | | | | |
| 8. Accessibility | 6 | | | | |
| 9. Performance | 5 | | | | |
| 10. Edge Cases | 6 | | | | |
| **TOTAL** | **110** | | | | |

---

## Issues Found

| # | Severity | Test Case | Description | Status |
|---|----------|-----------|-------------|--------|
| | | | | |

**Severity Levels**: Critical / High / Medium / Low

---

## Sign-Off

| Role | Name | Date | Signature |
|------|------|------|-----------|
| Tester | | | |
| Developer | | | |
| Product Owner | | | |

---

*Test plan created for Events Workspace Apps UX R1 - Task 071*
