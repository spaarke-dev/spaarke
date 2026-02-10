# UAT Test Plan - Events Workspace Apps UX R1

> **Version**: 1.0
> **Date**: 2026-02-04
> **Status**: Ready for UAT

---

## Overview

This document outlines the User Acceptance Testing (UAT) plan for the Events Workspace Apps UX R1 project. All components have been implemented, integration tested, and deployed to the dev environment.

### Components Under Test

| Component | Type | Status |
|-----------|------|--------|
| EventCalendarFilter | PCF Control | Deployed |
| UniversalDatasetGrid Enhancements | PCF Control | Deployed |
| EventDetailSidePane | Custom Page | Deployed |
| DueDatesWidget | PCF Control | Deployed |
| Events Custom Page | Custom Page | Deployed |
| EventTypeService | Shared Library | Deployed |

---

## Pre-UAT Verification

### Placeholder Code Verification

| Check | Status | Notes |
|-------|--------|-------|
| PLACEHOLDER-TRACKER.md has zero open items | PASS | All placeholders resolved (P001, P002) |
| No `// PLACEHOLDER:` comments in project code | PASS | Verified via grep search |
| No stub/mock code in production | PASS | All services use real implementations |

### Deployment Verification

| Environment | URL | Status |
|-------------|-----|--------|
| Spaarke Dev | https://spaarkedev1.crm.dynamics.com | Deployed |
| Solution Version | 1.0.5 | Current |

---

## UAT Test Scenarios

### Scenario 1: Calendar Navigation and Filtering

**Purpose**: Verify the EventCalendarFilter PCF control provides intuitive date navigation and filtering.

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| 1.1 Single Date Selection | 1. Open Events tab on Matter form<br>2. Click on any date in calendar | Grid filters to show only events on selected date |
| 1.2 Range Selection | 1. Click start date<br>2. Shift+Click end date | Grid filters to show events within date range |
| 1.3 Clear Selection | 1. Select a date<br>2. Click selected date again | Grid clears filter, shows all events |
| 1.4 Month Navigation | 1. Click forward/back arrows | Calendar scrolls to adjacent months |
| 1.5 Event Indicators | 1. Create event with specific due date<br>2. Check calendar | Date shows indicator dot |

### Scenario 2: Events Grid Functionality

**Purpose**: Verify UniversalDatasetGrid enhancements work correctly.

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| 2.1 Calendar Sync | 1. Select row in grid | Calendar highlights the event's due date |
| 2.2 Hyperlink Click | 1. Click event name hyperlink | Side pane opens with event details |
| 2.3 Checkbox Selection | 1. Check multiple rows<br>2. Verify selection state | Checkboxes persist, can bulk select/deselect |
| 2.4 Column Filtering | 1. Click column header filter<br>2. Enter filter criteria | Grid filters to matching records |
| 2.5 Sorting | 1. Click column header | Grid sorts ascending/descending |

### Scenario 3: Event Detail Side Pane

**Purpose**: Verify EventDetailSidePane provides efficient event editing without leaving context.

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| 3.1 Open Side Pane | 1. Click event hyperlink in grid | Side pane opens on right, grid visible |
| 3.2 Edit Status | 1. Click different status button | Status changes, Save button enables |
| 3.3 Edit Due Date | 1. Click date picker<br>2. Select new date | Date updates, marked as dirty |
| 3.4 Save Changes | 1. Make edits<br>2. Click Save | Changes saved, success message shown |
| 3.5 Unsaved Changes Warning | 1. Make edits<br>2. Click Close | Dialog prompts: Save/Discard/Cancel |
| 3.6 Section Collapse | 1. Click section header (Dates, Description) | Section expands/collapses |
| 3.7 Field Visibility by Event Type | 1. Open event of specific type<br>2. Verify visible fields | Fields match Event Type configuration |
| 3.8 Read-Only Mode | 1. Open event without edit permission | Banner shows, fields disabled |

### Scenario 4: Due Dates Widget

**Purpose**: Verify DueDatesWidget provides actionable event overview.

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| 4.1 Widget Display | 1. Navigate to Matter Overview tab | Widget shows upcoming events |
| 4.2 Event Cards | 1. Review event cards | Shows name, type badge, days until due |
| 4.3 Click Card | 1. Click event card | Navigates to Events tab, opens side pane |
| 4.4 All Events Link | 1. Click "All Events" link | Navigates to Events Custom Page |
| 4.5 Empty State | 1. View widget with no upcoming events | Shows "No upcoming events" message |

### Scenario 5: Events Custom Page (System-Level)

**Purpose**: Verify system-level Events page works for all users.

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| 5.1 Navigation | 1. Click Events in sitemap | Events Custom Page opens |
| 5.2 Calendar + Grid Layout | 1. Verify page layout | Calendar on left, grid on right |
| 5.3 Assigned To Filter | 1. Filter by user | Grid shows user's events only |
| 5.4 Record Type Filter | 1. Filter by Matter/Project | Grid filters by parent type |
| 5.5 Status Filter | 1. Filter by status | Grid shows filtered status only |
| 5.6 Regarding Column | 1. Click parent record link | Navigates to parent record |

### Scenario 6: Dark Mode Support

**Purpose**: Verify all components support dark mode.

| Test Case | Steps | Expected Result |
|-----------|-------|-----------------|
| 6.1 System Dark Mode | 1. Enable system dark mode<br>2. Refresh Dynamics | All components render in dark theme |
| 6.2 Theme Consistency | 1. Navigate between components | Theme consistent across all UI |
| 6.3 High Contrast | 1. Enable high contrast mode | Components accessible in high contrast |

---

## UAT Environment Setup

### Test Users

| Role | User | Purpose |
|------|------|---------|
| Admin | UAT-Admin@spaarkedev.onmicrosoft.com | Full access testing |
| Standard User | UAT-User@spaarkedev.onmicrosoft.com | Limited permission testing |
| Read-Only | UAT-ReadOnly@spaarkedev.onmicrosoft.com | Read-only mode verification |

### Test Data Requirements

1. **Matters**: At least 5 Matter records with varying event counts
2. **Projects**: At least 3 Project records with events
3. **Events**: Minimum 20 events across different:
   - Due dates (past, today, future)
   - Statuses (Draft, Planned, Open, Completed)
   - Event Types (with different field configurations)
   - Owners (different users)

### Browser Requirements

| Browser | Minimum Version | Status |
|---------|-----------------|--------|
| Microsoft Edge | 120+ | Required |
| Google Chrome | 120+ | Required |
| Firefox | 115+ | Optional |

---

## UAT Schedule

| Phase | Duration | Activities |
|-------|----------|------------|
| Preparation | 1 day | Environment setup, data creation, user training |
| Testing | 3 days | Execute test scenarios, log feedback |
| Issue Resolution | 2 days | Fix critical issues, re-test |
| Sign-off | 1 day | Final verification, approval |

---

## Issue Severity Definitions

| Severity | Definition | Response |
|----------|------------|----------|
| Critical | Functionality broken, no workaround | Block UAT until resolved |
| Major | Functionality impaired, workaround exists | Must fix before production |
| Minor | Cosmetic or minor usability issue | Fix in next release |
| Enhancement | Improvement suggestion | Consider for backlog |

---

## UAT Sign-Off Criteria

The following criteria must be met for UAT approval:

1. All Critical and Major issues resolved
2. All test scenarios executed with pass rate > 95%
3. No regression in existing functionality
4. Performance within acceptable limits (< 500ms calendar queries)
5. Dark mode verified on all components
6. Accessibility requirements met (WCAG 2.1 AA)

---

## Related Documentation

| Document | Location |
|----------|----------|
| Form Integration Test Results | `notes/testing/form-integration-results.md` |
| Custom Page Test Results | `notes/testing/custompage-results.md` |
| Browser Compatibility | `notes/testing/browser-compatibility.md` |
| Dark Mode Verification | `notes/testing/darkmode-verification.md` |
| Performance Results | `notes/testing/performance-results.md` |
| Accessibility Audit | `notes/testing/accessibility-audit.md` |

---

*UAT Plan prepared for Events Workspace Apps UX R1 project*
