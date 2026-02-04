# Events Workspace Apps UX R1 - Design Document

> **Status**: Design Review
> **Created**: 2026-02-03
> **Related Project**: events-and-workflow-automation-r1

---

## Design Decisions (Q&A Captured)

| Topic | Decision | Notes |
|-------|----------|-------|
| **Calendar Library** | Must use Fluent UI v9 calendar control | VisualHost module exists but may need React 16/17 refactor |
| **Calendar Size - Overview** | 1-2 months (constrained) | Right column is 34% of 66/34 two-column layout |
| **Calendar Size - Events Tab** | 2-3 months (more space) | Full tab width available |
| **Overdue Display** | Show all overdue items | May need future solution if historical overdue accumulates |
| **Event Type Field Config** | Low-overhead only | JSON text field on Event Type table if easy, skip if complex |

---

## Executive Summary

This project delivers three interconnected UX components that transform how users interact with Events in the Spaarke platform. The current Event experience relies on standard Dataverse forms and grids which are functional but not optimal for task management workflows.

**Key UX Problems Addressed:**
1. **Context loss** - Full-screen main forms navigate away from user's working context
2. **Information overload** - Main forms show too much data for simple tasks
3. **Poor scannability** - No visual hierarchy for due dates, urgency, or status
4. **Limited filtering** - Standard grids lack intuitive date-based navigation
5. **CRM-oriented dashboards** - Standard Dataverse dashboards don't fit task management patterns

**Solution Components:**
1. **Due Dates Widget** (Overview Tab) - Card-based view of upcoming actionable items
2. **Event Calendar + Grid** (Events Tab) - Date-filtered grid with bi-directional calendar sync
3. **Event Detail Side Pane** - Context-preserving edit experience with Event Type-aware layout

---

## Use Cases

### UC-01: Record-Level Event Management
**As a** user viewing a Matter or Project record
**I want to** see upcoming due dates and manage events without leaving the record
**So that** I can stay focused on my current work context

**Current Experience:**
- Events subgrid shows flat list
- Clicking event navigates to full-screen form
- No visual indication of urgency or upcoming dates
- Must navigate back to return to Matter/Project

**Target Experience:**
- Due Dates widget on Overview tab shows upcoming actionable items
- Events tab has calendar for date navigation
- Clicking event name opens side pane (stays in context)
- Checkbox for bulk actions, hyperlink for details

### UC-02: System-Wide Task Management
**As a** user with events across multiple records
**I want to** view and manage all my events in one place
**So that** I can prioritize my workload across all matters/projects

**Current Experience:**
- Navigate to Events entity view
- Standard grid with basic sorting
- No calendar visualization
- Click navigates to full-screen form

**Target Experience:**
- Dedicated "My Events" Custom Page
- Same Calendar + Grid experience as record-level
- Filters by Record Type, Date Range, Status, Assigned To
- Side pane for details/editing

### UC-03: Quick Event Completion
**As a** user completing a task
**I want to** mark events complete with minimal clicks
**So that** I can efficiently work through my task list

**Target Experience:**
- Checkbox on grid row for quick complete (bulk action)
- Side pane shows status toggle
- Visual feedback on completion

---

## Information Architecture

```
┌────────────────────────────────────────────────────────────────────────────────┐
│  RECORD LEVEL (Matter/Project Form)                                            │
│                                                                                │
│  ┌─────────────────────────────────────────────────────────────────────────┐  │
│  │  OVERVIEW TAB                                                            │  │
│  │  ┌──────────────────────────────────────────────────────────────────┐   │  │
│  │  │  DUE DATES WIDGET (PCF)                                           │   │  │
│  │  │  - Cards: Date | Event Name | Type | Parent (if viewing all)     │   │  │
│  │  │  - Filter: Next 7 days + next 5 items (whichever shows more)     │   │  │
│  │  │  - Color: Overdue (red), Today (amber), Upcoming (default)       │   │  │
│  │  │  - Click card → Opens Events Tab + Side Pane                     │   │  │
│  │  │  - "All Events →" link navigates to Events Tab                   │   │  │
│  │  └──────────────────────────────────────────────────────────────────┘   │  │
│  └─────────────────────────────────────────────────────────────────────────┘  │
│                                                                                │
│  ┌─────────────────────────────────────────────────────────────────────────┐  │
│  │  EVENTS TAB                                                              │  │
│  │  ┌─────────────────┐  ┌────────────────────────────────────────────┐   │  │
│  │  │  CALENDAR (PCF)  │  │  EVENT GRID (PCF - UniversalDatasetGrid)   │   │  │
│  │  │                 │  │                                            │   │  │
│  │  │  Feb 2026       │  │  ☐ Event Name    Status   Due Date  Owner  │   │  │
│  │  │  ┌─┬─┬─┬─┬─┬─┬─┐│  │  ☐ Filing...    Active   Feb 10    RS     │   │  │
│  │  │  │ │●│●│ │ │●│ ││  │  ☐ Hearing      Active   Feb 6     RS     │   │  │
│  │  │  ├─┼─┼─┼─┼─┼─┼─┤│  │  ☐ Review...    Active   Feb 13    RS     │   │  │
│  │  │  │●│ │ │●│ │ │ ││  │                                            │   │  │
│  │  │  └─┴─┴─┴─┴─┴─┴─┘│  │  Rows: 7                    Refresh  New  │   │  │
│  │  │                 │  └────────────────────────────────────────────┘   │  │
│  │  │  Click date →   │                    │                              │  │
│  │  │  filters grid   │  ◄─────────────────┘                              │  │
│  │  │                 │  Click row → bi-directional highlight             │  │
│  │  │  Range select   │                                                   │  │
│  │  │  supported      │                                                   │  │
│  │  └─────────────────┘                                                   │  │
│  └─────────────────────────────────────────────────────────────────────────┘  │
│                                                                                │
│  ┌─────────────────────────────────────────────────────────────────────────┐  │
│  │  SIDE PANE (Custom Page - opens on Event Name hyperlink click)          │  │
│  │  ┌──────────────────────────────────────────────────────────────────┐   │  │
│  │  │  Event: Filing Deadline                              [X] Close   │   │  │
│  │  │  ─────────────────────────────────────────────────────────────   │   │  │
│  │  │  Status: ◉ Active  ○ Complete  ○ Cancelled                       │   │  │
│  │  │  Due Date: Feb 10, 2026                                          │   │  │
│  │  │  Priority: ● High                                                │   │  │
│  │  │  ─────────────────────────────────────────────────────────────   │   │  │
│  │  │  ▼ Details (collapsed based on Event Type)                       │   │  │
│  │  │  ▼ Notes                                                         │   │  │
│  │  │  ▼ History                                                       │   │  │
│  │  │  ─────────────────────────────────────────────────────────────   │   │  │
│  │  │  [Save]  [Delete]                                                │   │  │
│  │  └──────────────────────────────────────────────────────────────────┘   │  │
│  └─────────────────────────────────────────────────────────────────────────┘  │
└────────────────────────────────────────────────────────────────────────────────┘

┌────────────────────────────────────────────────────────────────────────────────┐
│  SYSTEM LEVEL (Custom Page - "My Events")                                      │
│                                                                                │
│  Same Calendar + Grid layout as Events Tab                                     │
│  Additional filters: Record Type, Assigned To                                  │
│  Shows "Regarding" column with clickable link to parent record                 │
│  Reuses same PCF components                                                    │
└────────────────────────────────────────────────────────────────────────────────┘
```

---

## Component Specifications

### Component 1: Due Dates Widget PCF

**Purpose**: Surface upcoming actionable Events on the Overview tab of parent records (Matter, Project, etc.)

**Placement**: Overview tab, right column (34% width in 66/34 two-column layout), alongside Report Card widget

**Title**: "Due Dates" (not "Upcoming Events" - focused on actionable items)

#### Filter Logic

```
DISPLAY EVENTS WHERE:
  (DueDate IS NOT NULL OR FinalDueDate IS NOT NULL)
  AND Status = Active
  AND (
    EventType.Category IN ['Task', 'Deadline', 'Reminder', 'Action']  -- Actionable types
    NOT IN ['Notification', 'System', 'Audit']  -- Non-actionable types
  )

ORDER BY:
  COALESCE(DueDate, FinalDueDate) ASC

LIMIT:
  MIN(
    COUNT(Events where DueDate <= Today + 7 days),  -- All in next 7 days
    MAX(5, COUNT(...))  -- At least 5 shown
  )

SPECIAL CASES:
  - Overdue items (DueDate < Today): ALWAYS show ALL overdue items, display in RED
    ⚠️ FUTURE: May need limiting strategy if historical overdue accumulates
       (e.g., "Show 10 most recent overdue + 'X more overdue' link")
  - Today: Display in AMBER
  - Future: Default styling
  - If < 5 events in next 7 days, show up to 5 regardless of date
```

#### Card Layout

```
┌──────────────────────────────────────────────────────────────┐
│  6    📅  Hearing                                        36d │
│  FRI      Contract Dispute                                   │
│           Motion hearing in District Court                   │
└──────────────────────────────────────────────────────────────┘
  │         │   │                                           │
  │         │   └── Event Name (bold) + Event Type          │
  │         │       Description (truncated, muted)          │
  │         │                                               │
  │         └── Event Type Icon                             │
  │                                                         │
  └── Day of Month (large)                                  └── Days until due
      Day of Week (small)                                       (badge, color-coded)
```

#### Color Coding (Days Until Due Badge)

| Condition | Badge Color | Example |
|-----------|-------------|---------|
| Overdue (< Today) | Red (`colorPaletteRedBackground3`) | `-2d` |
| Today | Amber (`colorPaletteYellowBackground3`) | `Today` |
| Tomorrow | Amber | `1d` |
| 2-7 days | Default (`colorNeutralBackground3`) | `5d` |
| > 7 days | Muted (`colorNeutralBackground2`) | `36d` |

#### Interactions

| Action | Behavior |
|--------|----------|
| Click card | Navigate to Events tab, open Side Pane for that event |
| "All Events →" link | Navigate to Events tab (no specific event selected) |
| Hover card | Subtle highlight, cursor pointer |

#### Manifest Properties

```xml
<property name="parentEntityLogicalName" of-type="SingleLine.Text" usage="input" required="true" />
<property name="parentRecordId" of-type="SingleLine.Text" usage="input" required="true" />
<property name="maxDays" of-type="Whole.None" usage="input" required="false" default-value="7" />
<property name="minItems" of-type="Whole.None" usage="input" required="false" default-value="5" />
<property name="showOverdue" of-type="TwoOptions" usage="input" required="false" default-value="true" />
```

---

### Component 2: Event Calendar PCF

**Purpose**: Date-based navigation and filtering for the Event Grid

**Placement**: Left side of Events tab (form) or My Events Custom Page

#### Implementation Notes

**MUST use Fluent UI v9 Calendar**: Use `@fluentui/react-components` calendar control (Calendar, CalendarMonth, or DatePicker components). Do NOT use third-party calendar libraries.

**VisualHost Consideration**: The existing `VisualHost` PCF module contains calendar/charting components that could potentially be leveraged. However, VisualHost may be built with React 18 and would require refactoring to React 16/17 for PCF platform compatibility (per ADR-022). Evaluate before reuse:
- If VisualHost calendar is React 16/17 compatible → reuse
- If VisualHost requires React 18 → build calendar component fresh using Fluent UI v9

#### Display Modes & Sizing

| Context | Display Mode | Rationale |
|---------|--------------|-----------|
| **Overview Tab** (Due Dates Widget) | No calendar (cards only) | 34% column too narrow for calendar |
| **Events Tab** (Record form) | 2-3 months stacked | More screen real estate, matches screenshot |
| **Events Entity View** (Custom Page) | 2-3 months stacked | Full page width available |

The number of months shown should be responsive to available height. Default to 2 months with option for 3 if space permits.

#### Date Indicators

- **Dot on date**: At least one Event has Due Date on that date
- **Multiple dots or badge**: Multiple Events on that date
- **Selected range**: Highlighted background for selected date range

#### Interactions

| Action | Behavior |
|--------|----------|
| Click single date | Filter grid to events due on that date |
| Click + drag (or Shift+click) | Select date range, filter grid to range |
| Click "Clear" or click outside | Clear filter, show all events |
| Grid row selected | Highlight corresponding date on calendar |

#### Communication with Grid

**Option B: Two PCFs communicating via form field**

```
Calendar PCF                          Grid PCF
     │                                    │
     │ writes to                          │ reads from
     │ sprk_calendarfilter                │ sprk_calendarfilter
     │ (hidden text field)                │
     ▼                                    ▼
┌─────────────────────────────────────────────────────────────┐
│  Form Field: sprk_calendarfilter (hidden, SingleLine.Text)  │
│  Value: JSON string                                         │
│  {"type":"range","start":"2026-02-01","end":"2026-02-07"}   │
│  {"type":"single","date":"2026-02-10"}                      │
│  {"type":"clear"}                                           │
└─────────────────────────────────────────────────────────────┘
```

**For Custom Page**: Communication via React context or callback props (no form field needed)

#### Manifest Properties (Form-bound version)

```xml
<property name="filterOutput" of-type="SingleLine.Text" usage="bound" required="true"
          description="JSON filter output - bind to hidden sprk_calendarfilter field" />
<property name="eventDataSource" of-type="SingleLine.Text" usage="input" required="false"
          description="FetchXML or OData query for event dates" />
<property name="displayMode" of-type="Enum" usage="input" required="false" default-value="month"
          description="month | multiMonth" />
```

---

### Component 3: Event Grid Integration

**Purpose**: Display and manage Events with calendar-aware filtering

**Reuse**: Extend existing **UniversalDatasetGrid** (v2.1.4) with calendar filter support

#### Grid Enhancements Required

1. **Calendar Filter Awareness**
   - Read filter criteria from `sprk_calendarfilter` field (form) or props (Custom Page)
   - Apply date filter to dataset query
   - Emit selected row info for bi-directional calendar highlight

2. **Row Selection Pattern**
   - **Checkbox column**: For bulk actions (complete, delete, reassign)
   - **Event Name hyperlink**: Opens Side Pane (not navigation)
   - Click row (not checkbox): Highlights row, updates calendar, does NOT open side pane

3. **Bi-directional Sync**
   - Grid row click → Highlight date on calendar
   - Requires writing selected event's due date to a "reverse" field or callback

#### Configuration JSON Addition

```json
{
  "calendarIntegration": {
    "enabled": true,
    "filterFieldName": "sprk_calendarfilter",
    "dueDateField": "sprk_duedate",
    "selectedEventOutput": "sprk_selectedeventid"
  },
  "rowClickBehavior": "select",
  "hyperlinkColumn": "sprk_eventname",
  "hyperlinkAction": "sidepane"
}
```

---

### Component 4: Event Detail Side Pane (Custom Page)

**Purpose**: View and edit Event details without navigating away from grid

**Implementation**: Custom Page rendered in Dataverse side pane

#### Opening the Side Pane

```typescript
// From Grid PCF when Event Name hyperlink clicked
Xrm.App.sidePanes.createPane({
  title: "Event Details",
  paneId: "eventDetailPane",
  canClose: true,
  width: 400,
  webResourceParams: {
    eventId: selectedEventId,
    eventType: selectedEventType
  }
});

// Custom Page receives params and renders EventDetailPane component
```

#### Layout Structure

```
┌─────────────────────────────────────────────────────────────┐
│  HEADER                                                     │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Event Name (editable)                        [X]     │  │
│  │  Event Type: Filing Deadline (read-only)              │  │
│  │  Parent: Matter #12345 (clickable link)               │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  STATUS SECTION                                             │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  ◉ Draft  ○ Open  ○ On Hold  ○ Complete  ○ Cancelled  │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  KEY FIELDS (always visible)                                │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  Due Date:      [Feb 10, 2026    ] [📅]               │  │
│  │  Priority:      [● High          ▼]                   │  │
│  │  Owner:         [Ralph Schroeder ▼]                   │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  CONDITIONAL SECTIONS (based on Event Type config)          │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  ▼ Dates (expanded if Event Type requires dates)      │  │
│  │    Base Date:    [Jan 15, 2026]                       │  │
│  │    Final Date:   [Feb 28, 2026]                       │  │
│  │    Remind At:    [Feb 8, 2026 9:00 AM]                │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  ▶ Related Event (collapsed - click to expand)        │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  ▼ Description                                        │  │
│  │    [Multiline text editor                         ]   │  │
│  └───────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  ▶ History (collapsed)                                │  │
│  │    - Created by RS on Jan 15, 2026                    │  │
│  │    - Status changed to Open on Jan 20, 2026           │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                             │
│  FOOTER                                                     │
│  ┌───────────────────────────────────────────────────────┐  │
│  │  [Save]  [Delete]              v1.0.0 • Feb 3, 2026   │  │
│  └───────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────┘
```

#### Event Type-Aware Field Visibility

**Leverage existing EventFormController PCF pattern**

The EventFormController (v1.0.6) already implements field show/hide based on Event Type configuration. The Side Pane should:

1. Query Event Type record to get field requirements
2. Use same visibility logic as EventFormController
3. Collapse empty sections rather than hide completely (progressive disclosure)

**Configurable Field Visibility (Low-Overhead Approach)**

Add a JSON text field to the Event Type table for flexible field configuration. Only implement if this doesn't add significant complexity.

```
New Field on sprk_eventtype:
  sprk_fieldconfigjson (MultiLine.Text)
```

```typescript
interface EventTypeConfig {
  requiresBaseDate: boolean;
  requiresDueDate: boolean;
  requiresReminder: boolean;
  // Configurable via JSON field on Event Type record
  fieldConfig?: {
    visibleFields?: string[];      // Fields to show (whitelist)
    hiddenFields?: string[];       // Fields to hide (blacklist)
    requiredFields?: string[];     // Additional required fields
    sectionDefaults?: {            // Section expand/collapse defaults
      dates?: "expanded" | "collapsed";
      relatedEvent?: "expanded" | "collapsed";
      description?: "expanded" | "collapsed";
    };
  };
}

// Query Event Type configuration
const eventType = await webApi.retrieveRecord("sprk_eventtype", eventTypeId,
  "?$select=sprk_requiresbasedate,sprk_requiresduedate,sprk_fieldconfigjson");

// Parse JSON config if present
const fieldConfig = eventType.sprk_fieldconfigjson
  ? JSON.parse(eventType.sprk_fieldconfigjson)
  : null;
```

**Decision**: Implement JSON field config ONLY if it can be done without adding a separate admin UI. Admins would edit JSON directly in the Event Type form (acceptable for power users). If this approach is deemed too technical for admins, defer to future release.

#### Editable Fields

| Section | Fields | Edit Mode |
|---------|--------|-----------|
| Header | Event Name | Inline text edit |
| Status | Status Reason | Radio/segmented button |
| Key Fields | Due Date, Priority, Owner | Inline with pickers |
| Dates | Base Date, Final Date, Remind At | Inline with pickers |
| Description | Description | Multiline text area |
| History | Read-only | N/A |

#### Save Behavior

- **Auto-save on field blur**: Optional, configurable
- **Explicit Save button**: Updates record via WebAPI
- **Optimistic UI**: Show changes immediately, revert on error
- **Grid refresh**: After save, grid should reflect changes (event via form field or callback)

---

## System-Wide "My Events" Custom Page

**Purpose**: Centralized task management across all records

**Reuses**: Same Calendar + Grid + Side Pane components

**Additional Features**:

1. **Filters Panel**
   - Record Type dropdown (Matter, Project, Invoice, etc.)
   - Assigned To (default: current user)
   - Status (Active, Complete, All)
   - Date Range (Quick: Today, This Week, This Month, Custom)

2. **Regarding Column**
   - Shows parent record name
   - Clickable link to parent record (uses RegardingLink PCF pattern)

3. **Grouping** (future enhancement)
   - Group by Record Type
   - Group by Due Date (Today, This Week, Later)

---

## Existing Components to Leverage

| Component | Version | Purpose in This Project |
|-----------|---------|------------------------|
| **UniversalDatasetGrid** | v2.1.4 | Base grid component - extend with calendar filter support |
| **EventFormController** | v1.0.6 | Field visibility logic based on Event Type - reuse in Side Pane |
| **RegardingLink** | - | Clickable parent record link - reuse in My Events grid |
| **AssociationResolver** | v1.0.6 | Record type selection - not directly used but pattern reference |
| **EventAutoAssociate** | v1.0.0 | Auto-populate denormalized fields - continues to work alongside |

### EventFormController Integration

The EventFormController PCF already handles:
- Querying Event Type configuration
- Determining which fields to show/hide
- Field requirement validation

**For Side Pane**: Extract the visibility logic into a shared utility:

```typescript
// src/client/shared/Spaarke.UI.Components/services/EventTypeService.ts

export interface EventTypeFieldConfig {
  fieldName: string;
  isVisible: boolean;
  isRequired: boolean;
  sectionName: string;
}

export async function getEventTypeFieldConfig(
  webApi: ComponentFramework.WebApi,
  eventTypeId: string
): Promise<EventTypeFieldConfig[]> {
  // Query Event Type record
  // Return field visibility configuration
}
```

---

## Build Sequence

Based on reusability and dependencies:

| Phase | Component | Rationale |
|-------|-----------|-----------|
| **1** | **EventCalendarFilter PCF** | No dependencies, can be tested standalone |
| **2** | **UniversalDatasetGrid Enhancement** | Add calendar filter support, depends on Phase 1 |
| **3** | **EventDetailSidePane Custom Page** | Depends on EventTypeService extraction |
| **4** | **DueDatesWidget PCF** | Standalone but benefits from Side Pane being ready |
| **5** | **MyEventsPage Custom Page** | Assembles all components for system-wide view |
| **6** | **Integration & Testing** | End-to-end testing on form and Custom Page |

---

## Technical Considerations

### Form vs Custom Page Differences

| Aspect | Form (Events Tab) | Custom Page (My Events) |
|--------|-------------------|------------------------|
| PCF Communication | Via hidden form fields | Via React props/context |
| Side Pane Opening | `Xrm.App.sidePanes.createPane()` | Same, or inline panel |
| Dataset Source | Subgrid dataset binding | WebAPI query |
| Calendar Filter | Write to bound field | Callback prop |

**Implication**: Components need to support both patterns. Use adapter pattern:

```typescript
interface CalendarGridAdapter {
  onDateFilterChange(filter: DateFilter): void;
  onEventSelected(eventId: string): void;
}

// Form implementation
class FormCalendarGridAdapter implements CalendarGridAdapter {
  constructor(private context: ComponentFramework.Context<IInputs>) {}

  onDateFilterChange(filter: DateFilter) {
    // Write to bound field
    this.context.parameters.filterOutput.raw = JSON.stringify(filter);
  }
}

// Custom Page implementation
class CustomPageCalendarGridAdapter implements CalendarGridAdapter {
  constructor(private setFilter: (filter: DateFilter) => void) {}

  onDateFilterChange(filter: DateFilter) {
    // Call React state setter
    this.setFilter(filter);
  }
}
```

### Side Pane Persistence

When user clicks different events in grid, the side pane should:
- **Update content** without closing/reopening (smoother UX)
- **Preserve scroll position** if same section structure
- **Prompt for unsaved changes** if user edited without saving

```typescript
// Check for existing pane before creating
const existingPane = Xrm.App.sidePanes.getPane("eventDetailPane");
if (existingPane) {
  // Update existing pane content
  existingPane.navigate({
    webResourceParams: { eventId: newEventId }
  });
} else {
  // Create new pane
  Xrm.App.sidePanes.createPane({ ... });
}
```

### Performance Considerations

1. **Calendar date dots**: Query event dates separately from full event data
   ```
   GET /api/v1/events/dates?regardingId={id}&startDate={start}&endDate={end}
   Returns: [{ date: "2026-02-10", count: 3 }, ...]
   ```

2. **Grid pagination**: Use standard Dataverse paging (50 records default)

3. **Side Pane lazy loading**: Don't load history/notes until section expanded

---

## ADR Compliance

| ADR | Requirement | This Project |
|-----|-------------|--------------|
| ADR-006 | PCF over webresources | ✅ All components are PCF controls |
| ADR-021 | Fluent UI v9, dark mode | ✅ Using `@fluentui/react-components` |
| ADR-022 | React 16 APIs, platform libraries | ✅ `ReactDOM.render()`, platform-library declarations |

---

## Resolved Questions

| # | Question | Decision |
|---|----------|----------|
| 1 | **Calendar library** | ✅ MUST use Fluent UI v9 calendar. Check if VisualHost can be reused (may need React 16/17 refactor). |
| 2 | **Multi-month vs single month** | ✅ Depends on context: 1-2 months for 34% column (Overview), 2-3 months for Events tab/Custom Page. Responsive to available height. |
| 3 | **Overdue items display** | ✅ Show ALL overdue items. Future: may need limiting strategy if historical overdue accumulates. |
| 4 | **Event Type field configuration** | ✅ Low-overhead only: Add `sprk_fieldconfigjson` (MultiLine.Text) to Event Type table. Admins edit JSON directly. Skip if adds significant complexity. |

---

## Success Criteria

| # | Criterion | Verification |
|---|-----------|--------------|
| SC-01 | Due Dates widget shows events with correct filter logic | Filter by date range + minimum count, verify display |
| SC-02 | Calendar date selection filters grid correctly | Click date, verify grid shows only that date's events |
| SC-03 | Calendar range selection works | Shift+click two dates, verify grid filters to range |
| SC-04 | Grid row click highlights calendar date | Click row, verify calendar shows indicator |
| SC-05 | Event Name hyperlink opens Side Pane | Click hyperlink, verify pane opens without navigation |
| SC-06 | Side Pane shows Event Type-aware layout | Select different Event Types, verify field visibility changes |
| SC-07 | Side Pane save updates record | Edit field, save, verify record updated in Dataverse |
| SC-08 | My Events page shows cross-record events | Access Custom Page, verify events from multiple records |
| SC-09 | All components support dark mode | Toggle theme, verify no visual issues |
| SC-10 | Checkbox vs hyperlink pattern works | Checkbox selects for bulk, hyperlink opens pane |

---

## Appendix: Event Data Model Reference

From `events-and-workflow-automation-r1/spec.md`:

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
| Regarding Record URL | `sprk_regardingrecordurl` | URL |

### Status Reason Values

| Value | Label |
|-------|-------|
| 1 | Draft |
| 2 | Planned |
| 3 | Open |
| 4 | On Hold |
| 5 | Completed |
| 6 | Cancelled |

---

*Document created: 2026-02-03*
*Ready for review and refinement*
