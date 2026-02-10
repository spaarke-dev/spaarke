# Events Workspace Apps UX R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-04
> **Source**: design.md

---

## Executive Summary

This project delivers three interconnected UX components that transform how users interact with Events in the Spaarke platform. The current Event experience relies on standard Dataverse forms and grids which are functional but not optimal for task management workflows. The solution provides context-preserving navigation, visual date-based filtering, and Event Type-aware editing through a Due Dates Widget, Calendar-integrated Grid, and Detail Side Pane.

---

## Scope

### In Scope

1. **Due Dates Widget PCF** - Card-based view of upcoming actionable Events on record Overview tabs
2. **Event Calendar PCF** - Date-based navigation and filtering using Fluent UI v9 calendar
3. **UniversalDatasetGrid Enhancement** - Calendar filter integration, bi-directional sync, hyperlink-to-sidepane pattern
4. **Event Detail Side Pane** - Custom Page for context-preserving Event editing with Event Type-aware field visibility
5. **Events Custom Page** - System-level Events view replacing OOB entity main view, reusing same components
6. **EventTypeService extraction** - Shared service for field visibility logic (from EventFormController)

### Out of Scope

- Grouping in Events page (Today, This Week, Later) - future enhancement
- View selector dropdown - using column/field filters instead
- Mobile-specific layouts - desktop-first
- Event creation wizard - using existing patterns
- Workflow automation triggers - separate project
- Activity entity integration - Events remain custom entity (`sprk_event`)

### Affected Areas

| Area | Path | Changes |
|------|------|---------|
| PCF Controls | `src/client/pcf/` | New: DueDatesWidget, EventCalendarFilter; Enhanced: UniversalDatasetGrid |
| Custom Pages | `src/solutions/` | New: EventDetailSidePane, EventsPage |
| Shared Library | `src/client/shared/Spaarke.UI.Components/` | New: EventTypeService, CalendarGridAdapter |
| Existing PCF | `src/client/pcf/EventFormController/` | Extract visibility logic to shared service |
| Existing PCF | `src/client/pcf/VisualHost/` | Reference only - React 18, cannot reuse directly |

---

## Requirements

### Functional Requirements

#### FR-01: Due Dates Widget
Display upcoming actionable Events as cards on record Overview tabs.

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| FR-01.1 | Filter to actionable Events only | Shows Events where EventType.Category IN ['Task', 'Deadline', 'Reminder', 'Action'] AND Status = Active |
| FR-01.2 | Show overdue items prominently | ALL overdue items displayed in red, sorted first |
| FR-01.3 | Show at least 5 items or 7-day window | MIN(all items in next 7 days, MAX(5, count)) |
| FR-01.4 | Card displays date, name, type, days-until-due | Day/weekday, Event name (bold), Event Type, countdown badge |
| FR-01.5 | Color-coded urgency badges | Overdue=red, Today=amber, Tomorrow=amber, 2-7d=default, >7d=muted |
| FR-01.6 | Click card opens Side Pane | Navigate to Events tab, open Side Pane for that Event |
| FR-01.7 | "All Events" link navigates to Events tab | Link at bottom of widget |

#### FR-02: Event Calendar
Provide date-based navigation and filtering for the Event Grid.

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| FR-02.1 | Use Fluent UI v9 calendar | `@fluentui/react-components` calendar, NOT third-party |
| FR-02.2 | Show event indicators on dates | Dot/badge on dates with Events |
| FR-02.3 | Single date click filters grid | Grid shows only Events due on selected date |
| FR-02.4 | Range selection (Shift+click or drag) | Grid filters to date range |
| FR-02.5 | Clear filter option | Click outside or "Clear" returns to all Events |
| FR-02.6 | Bi-directional sync with grid | Grid row selection highlights corresponding calendar date |
| FR-02.7 | Responsive month display | 2-3 months stacked based on available height |

#### FR-03: Event Grid Integration
Enhance UniversalDatasetGrid with calendar awareness and side pane integration.

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| FR-03.1 | Calendar filter awareness | Read filter from form field (form) or props (Custom Page) |
| FR-03.2 | Checkbox column for bulk actions | Multi-select for complete, delete, reassign |
| FR-03.3 | Event Name hyperlink opens Side Pane | Click name = side pane, NOT navigation |
| FR-03.4 | Row click selects and syncs calendar | Click row (not checkbox) highlights date on calendar |
| FR-03.5 | Column/field filters | Standard filtering via column headers |
| FR-03.6 | Match Power Apps grid look/feel | Must not appear retrofitted |
| FR-03.7 | Optimistic row update on save | Side Pane save updates just that row, no full refresh |

#### FR-04: Event Detail Side Pane
Context-preserving Event editing via Dataverse side pane.

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| FR-04.1 | Open via Xrm.App.sidePanes | 400px width, closeable |
| FR-04.2 | Header with name, type, parent link | Event name editable, type read-only, parent clickable |
| FR-04.3 | Status section with radio/segmented | Draft, Open, On Hold, Complete, Cancelled |
| FR-04.4 | Key fields always visible | Due Date, Priority, Owner with inline editing |
| FR-04.5 | Event Type-aware field visibility | Query Event Type config, show/hide sections accordingly |
| FR-04.6 | Collapsible sections | Dates, Related Event, Description, History |
| FR-04.7 | Save via WebAPI | Optimistic UI with error rollback |
| FR-04.8 | Security role awareness | Open read-only if user lacks edit permission |
| FR-04.9 | Reuse existing pane on event switch | Update content without close/reopen; prompt for unsaved changes |

#### FR-05: Events Custom Page (System-Level)
Replace OOB Events entity main view with custom implementation.

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| FR-05.1 | Replace OOB Events view | Uses existing "Events" navigation, not new nav item |
| FR-05.2 | Same Calendar + Grid + Side Pane | Reuses record-level components |
| FR-05.3 | No "My" branding | Page title is "Events", not "My Events" |
| FR-05.4 | "Regarding" column | Shows parent record name with clickable link |
| FR-05.5 | Filter: Assigned To me | Optional filter, not default state |
| FR-05.6 | Filter: Record Type | Matter, Project, Invoice, etc. |
| FR-05.7 | Filter: Status | Active, Complete, All |
| FR-05.8 | Filter: Date Range | Quick filters + custom range |

### Non-Functional Requirements

| ID | Requirement | Target |
|----|-------------|--------|
| NFR-01 | Calendar date query performance | < 500ms for 3-month date range |
| NFR-02 | Grid pagination | 50 records default, standard Dataverse paging |
| NFR-03 | Side Pane lazy loading | History/notes load on section expand only |
| NFR-04 | PCF bundle size | < 1MB per control (platform libraries external) |
| NFR-05 | Dark mode support | Full theme compatibility via Fluent tokens |
| NFR-06 | Accessibility | WCAG 2.1 AA, keyboard navigation, focus indicators |

---

## Technical Constraints

### Applicable ADRs

| ADR | Requirement | Application |
|-----|-------------|-------------|
| **ADR-006** | PCF over webresources | All components are PCF controls |
| **ADR-011** | Dataset PCF over subgrids | UniversalDatasetGrid for Events grid |
| **ADR-012** | Shared component library | EventTypeService in `@spaarke/ui-components` |
| **ADR-021** | Fluent UI v9, dark mode | All UI uses `@fluentui/react-components`, tokens |
| **ADR-022** | React 16 APIs, platform libraries | `ReactDOM.render()`, not `createRoot` |

### MUST Rules

- ✅ MUST use `@fluentui/react-components` for calendar (Fluent UI v9)
- ✅ MUST use React 16 APIs (`ReactDOM.render`, `unmountComponentAtNode`)
- ✅ MUST declare `platform-library` in PCF manifests
- ✅ MUST use Fluent design tokens (no hard-coded colors)
- ✅ MUST support light, dark, and high-contrast modes
- ✅ MUST wrap all UI in `FluentProvider` with theme
- ✅ MUST import shared components via `@spaarke/ui-components`
- ✅ MUST match Power Apps grid look and feel exactly

### MUST NOT Rules

- ❌ MUST NOT use React 18+ APIs (`createRoot`, `hydrateRoot`)
- ❌ MUST NOT bundle React/Fluent in PCF artifacts
- ❌ MUST NOT use Fluent v8 (`@fluentui/react`)
- ❌ MUST NOT hard-code colors (use tokens)
- ❌ MUST NOT reuse VisualHost CalendarVisual directly (React 18)
- ❌ MUST NOT create new legacy JS webresources
- ❌ MUST NOT brand as "My Events" or "My" anything

### Existing Patterns to Follow

| Pattern | Location | Usage |
|---------|----------|-------|
| PCF initialization | `src/client/pcf/UniversalDatasetGrid/control/index.ts` | React 16 render pattern |
| Theme provider | `src/client/pcf/*/control/providers/ThemeProvider.ts` | FluentProvider setup |
| Field visibility | `src/client/pcf/EventFormController/handlers/FieldVisibilityHandler.ts` | Extract to shared service |
| Side pane opening | Xrm.App.sidePanes API | Standard Dataverse pattern |

### VisualHost CalendarVisual Assessment

The existing `CalendarVisual` component in VisualHost:
- ✅ Uses Fluent UI v9 (`@fluentui/react-components`)
- ✅ Has date click handling and event indicators
- ❌ Uses React 18 (`"react": "^18.2.0"`)
- ❌ Cannot be directly reused in PCF (ADR-022 violation)

**Decision**: Build EventCalendarFilter fresh using Fluent UI v9 Calendar with React 16 APIs. Can reference VisualHost for design patterns but not import directly.

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| SC-01 | Due Dates widget shows events with correct filter logic | Filter by date range + minimum count, verify color coding |
| SC-02 | Calendar date selection filters grid correctly | Click date, verify grid shows only that date's events |
| SC-03 | Calendar range selection works | Shift+click two dates, verify grid filters to range |
| SC-04 | Grid row click highlights calendar date | Click row, verify calendar shows indicator |
| SC-05 | Event Name hyperlink opens Side Pane | Click hyperlink, verify pane opens without navigation |
| SC-06 | Side Pane shows Event Type-aware layout | Select different Event Types, verify field visibility changes |
| SC-07 | Side Pane save updates record optimistically | Edit field, save, verify row updates without full grid refresh |
| SC-08 | Events page shows cross-record events | Access Events nav, verify events from multiple records |
| SC-09 | All components support dark mode | Toggle theme, verify no visual issues |
| SC-10 | Checkbox vs hyperlink pattern works | Checkbox selects for bulk, hyperlink opens pane |
| SC-11 | Grid matches Power Apps look/feel | Visual comparison with standard grid |
| SC-12 | Side pane respects security role | Read-only user sees disabled fields, no Save button |

---

## Dependencies

### Prerequisites

| Dependency | Status | Notes |
|------------|--------|-------|
| UniversalDatasetGrid v2.1.4 | ✅ Exists | Base for enhancement |
| EventFormController v1.0.6 | ✅ Exists | Field visibility logic to extract |
| `@spaarke/ui-components` | ✅ Exists | Shared component library |
| Event data model | ✅ Exists | `sprk_event`, `sprk_eventtype` entities |

### External Dependencies

| Dependency | Purpose | Risk |
|------------|---------|------|
| Fluent UI v9 Calendar component | Calendar rendering | Low - stable API |
| Xrm.App.sidePanes API | Side pane opening | Low - standard Dataverse API |
| WebAPI | CRUD operations | Low - standard Dataverse API |

---

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Grid refresh | Full refresh or optimistic row update on save? | Optimistic row update only | Simpler UX, preserves scroll position |
| System-level page | Where in sitemap? New nav item? | Replaces OOB Events view, uses existing "Events" nav | No new sitemap entry needed |
| Branding | "My Events" or other name? | "Events" only - no "My" branding | Standard entity navigation |
| View switching | View selector dropdown needed? | No - use column/field filters instead | Reduces scope, standard grid filtering |
| Permissions | Read-only behavior for Side Pane? | Open in read-only mode per security role | Consistent UX for all users |
| Terminology | "Activities" as umbrella term? | Keep "Events" - avoids Dataverse Activity entity confusion | Clear entity distinction |

---

## Assumptions

*Proceeding with these assumptions (not explicitly specified):*

| Topic | Assumption | Affects |
|-------|------------|---------|
| Calendar library | Build fresh with Fluent v9 (not port VisualHost) | Phase 1 implementation |
| Event Type config | `sprk_fieldconfigjson` field approach is acceptable | Side Pane field visibility |
| Form field communication | Hidden `sprk_calendarfilter` field for Calendar↔Grid sync | Form integration |
| Custom Page communication | React props/context (no hidden fields) | Events Custom Page |

---

## Build Sequence

Based on dependencies and reusability:

| Phase | Component | Rationale |
|-------|-----------|-----------|
| **1** | EventCalendarFilter PCF | No dependencies, can be tested standalone |
| **2** | UniversalDatasetGrid Enhancement | Add calendar filter support, depends on Phase 1 |
| **3** | EventTypeService (shared) | Extract from EventFormController |
| **4** | EventDetailSidePane Custom Page | Depends on EventTypeService |
| **5** | DueDatesWidget PCF | Standalone but benefits from Side Pane being ready |
| **6** | Events Custom Page | Assembles all components for system-wide view |
| **7** | Integration & Testing | End-to-end testing on form and Custom Page |

---

## Data Model Reference

### Key Event Fields

| Field | Schema Name | Type |
|-------|-------------|------|
| Event Name | `sprk_eventname` | Text |
| Due Date | `sprk_duedate` | Date |
| Base Date | `sprk_basedate` | Date |
| Final Due Date | `sprk_finalduedate` | Date |
| Status | `statecode` | Choice |
| Status Reason | `statuscode` | Choice |
| Priority | `sprk_priority` | Choice |
| Event Type | `sprk_eventtype` | Lookup |
| Owner | `ownerid` | Lookup |
| Regarding Record Type | `sprk_regardingrecordtype` | Lookup |
| Regarding Record Name | `sprk_regardingrecordname` | Text |
| Regarding Record ID | `sprk_regardingrecordid` | Text |

### Status Reason Values

| Value | Label |
|-------|-------|
| 1 | Draft |
| 2 | Planned |
| 3 | Open |
| 4 | On Hold |
| 5 | Completed |
| 6 | Cancelled |

### New Field (Event Type table)

| Field | Schema Name | Type | Purpose |
|-------|-------------|------|---------|
| Field Config JSON | `sprk_fieldconfigjson` | MultiLine.Text | Configurable field visibility |

---

## Unresolved Questions

*All blocking questions resolved during interview.*

---

*AI-optimized specification. Original design: design.md*
