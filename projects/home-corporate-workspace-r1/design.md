# Legal Operations Workspace — UI/UX Design Document

**Document Version:** 2.0
**Last Updated:** February 17, 2026
**Status:** Draft — Review
**Prototype Reference:** `projects/2025-01-corporate-legal-home/` (Refined Home variant)

> **Freestanding Document.** This document is self-contained. All architecture context, technology decisions, entity references, and scoring logic are included inline. No external documents are required to understand this specification.

---

## Table of Contents

1. [Purpose & Scope](#purpose--scope)
2. [Technology Stack & Architecture](#technology-stack--architecture)
3. [Design Principles](#design-principles)
4. [Page Layout](#page-layout)
5. [Build Blocks](#build-blocks)
   - [Block 1: Get Started & Quick Summary](#block-1-get-started--quick-summary)
   - [Block 2: Portfolio Health Summary](#block-2-portfolio-health-summary)
   - [Block 3: Updates Feed](#block-3-updates-feed)
   - [Block 4: Smart To Do](#block-4-smart-to-do)
   - [Block 5: My Portfolio Widget](#block-5-my-portfolio-widget)
   - [Block 6: Create New Matter Dialog](#block-6-create-new-matter-dialog)
   - [Block 7: Notification Panel](#block-7-notification-panel)
6. [Priority & Effort Scoring Logic](#priority--effort-scoring-logic)
7. [AI Playbook Integration](#ai-playbook-integration)
8. [VisualHost Relationship](#visualhost-relationship)
9. [Dataverse Entity Requirements](#dataverse-entity-requirements)
10. [Dark Mode Compliance](#dark-mode-compliance)
11. [Accessibility Requirements](#accessibility-requirements)
12. [Build Sequence & Dependencies](#build-sequence--dependencies)
13. [Fluent UI v9 Component Inventory](#fluent-ui-v9-component-inventory)
14. [Supporting Patterns](#supporting-patterns)
15. [Appendix: Prototype Component Mapping](#appendix-prototype-component-mapping)
16. [Open Questions](#open-questions)

---

## Purpose & Scope

This document defines the UI/UX design for the **Legal Operations Workspace** — a custom React page embedded in a Power Apps Model-Driven App. It compiles and organizes all features prototyped in the Spaarke UX sandbox into logical build blocks suitable for implementation planning.

### What This Document Covers

- UI components, layout, interactions, and data requirements
- Priority and effort scoring logic (Event-level fields already in Dataverse)
- AI integration patterns using the Spaarke AI Playbook platform
- VisualHost component reuse evaluation
- Dark mode compliance requirements
- Build sequencing and dependency mapping

### Key Constraints

1. **Existing entities only.** All features use existing Dataverse entities — `sprk_event`, `sprk_matter`, `sprk_project`, `sprk_document`, `sprk_contact`, `sprk_organization`. No new core entity creation required.
2. **Single custom page.** The workspace is a single dashboard page — no page-level tab navigation. Users navigate to entity views, forms, and dialogs via MDA when they need deeper entity interaction.
3. **Fluent UI v9 only.** All components use `@fluentui/react-components` and `@fluentui/react-icons`. No third-party UI libraries.
4. **AI via Playbook platform.** All AI capabilities (summaries, scoring, pre-fill) run through the Spaarke AI Playbook system.

---

## Technology Stack & Architecture

### Technology Requirements

| Required | Prohibited |
|----------|------------|
| React + TypeScript (.ts/.tsx only) | Tailwind CSS |
| `@fluentui/react-components` (Fluent UI v9) | shadcn/ui |
| `@fluentui/react-icons` | Material UI (MUI) |
| Fluent v9 tokens (`makeStyles`, `tokens`) | Chakra UI, Bootstrap |
| Vite (prototype) | Custom CSS color/shadow/typography systems |

### Architectural Context

| Aspect | Detail |
|--------|--------|
| **Hosting** | Custom Page embedded in Power Apps Model-Driven App |
| **React Runtime** | React 18+ with TypeScript strict mode |
| **Styling** | Fluent UI v9 `makeStyles` with `tokens` — zero custom colors, shadows, or typography |
| **Data Access** | Xrm.WebApi (Dataverse), BFF for aggregation queries |
| **AI System** | Spaarke AI Playbook platform (see [AI Playbook Integration](#ai-playbook-integration)) |
| **Navigation** | Navigation Contract — `postMessage` to parent MDA for entity view/form navigation |
| **Theme** | Fluent UI `FluentProvider` with light/dark theme toggle; theme persists to localStorage |
| **Icons** | `@fluentui/react-icons` — auto-adapt to theme |

### Navigation Contract

The workspace is a **hub, not a destination**. All entity-level interactions (viewing matter details, editing records, opening views) navigate to MDA primitives:

| User Interaction | Navigation Target |
|-----------------|-------------------|
| Click matter in portfolio | Opens matter main form |
| Click "View All Matters" | Opens filtered entity view |
| Click "Create New Matter" | Opens Create New Matter dialog (in-page) |
| Click notification item | Opens related entity record |
| Click "View All" on any feed item | Opens entity view filtered to type |

---

## Design Principles

1. **MDA-First Navigation** — Workspace widgets navigate to entity views, forms, and dialogs. The workspace is a hub, not a destination.
2. **Fluent UI v9 Only** — All components use `@fluentui/react-components` and `@fluentui/react-icons`. No custom color systems. Dark mode mandatory.
3. **AI-Enhanced, Not AI-Dependent** — AI adds value (summaries, scoring, pre-fill) but every feature works without it. Deterministic rules provide the baseline. All AI runs through the Spaarke AI Playbook platform.
4. **Event-Centric Data Model** — Priority, effort, and to-do flags are Event-level fields. The workspace surfaces Events through multiple views (feed, to-do list).
5. **Dark Mode First** — All UI uses Fluent semantic tokens. Zero hardcoded hex/RGB values. Both light and dark modes are tested and verified.

---

## Page Layout

The workspace is a **single dashboard page** with no page-level tabs. The layout uses a vertical stack with a 50/50 grid for the main content area.

```
┌─────────────────────────────────────────────────────────────────┐
│  ┌─────────────────────────────┐ ┌──────────────────┐          │
│  │  Get Started (action cards) │ │  Quick Summary    │          │
│  └─────────────────────────────┘ └──────────────────┘          │
│                                                                 │
│  ┌─────────┐ ┌─────────┐ ┌─────────┐ ┌─────────┐             │
│  │ Spend   │ │ At Risk │ │ Overdue │ │ Active  │  Health Bar  │
│  └─────────┘ └─────────┘ └─────────┘ └─────────┘             │
│                                                                 │
│  ┌──────────────────────────────┐ ┌──────────────────────────┐ │
│  │ WORKSPACE TABS:              │ │                          │ │
│  │ Updates | To Do              │ │  My Portfolio             │ │
│  │                              │ │  (Matters/Projects        │ │
│  │ [Feed items / To-do list]    │ │   /Documents)             │ │
│  │                              │ │                          │ │
│  └──────────────────────────────┘ └──────────────────────────┘ │
│                                                                 │
│         50% width                       50% width               │
└─────────────────────────────────────────────────────────────────┘
```

### Layout Specification

| Section | Layout | Responsive |
|---------|--------|-----------|
| Get Started + Quick Summary | Flex row (Get Started fills, Summary 280-320px fixed) | Stacks vertically below 900px |
| Portfolio Health Summary | 4-column CSS grid (`repeat(4, 1fr)`) | 2-column below 900px |
| Main Content Area | 2-column CSS grid (`1fr 1fr`) — 50/50 split | Single column below 1024px |

**Implementation:**

```css
/* Main content grid — 50/50 split */
gridTemplateColumns: '1fr 1fr'

/* Responsive breakpoint */
'@media (max-width: 1024px)': {
  gridTemplateColumns: '1fr'
}
```

---

## Build Blocks

The workspace is organized into 7 logical build blocks. Each block is independently testable and can be implemented sequentially. Dependencies between blocks are noted.

---

### Block 1: Get Started & Quick Summary

**Purpose:** The top row of the workspace — quick action cards and an AI-generated portfolio briefing.

#### 1A: Get Started Section

**Layout:** Horizontal scrollable row of action cards.

**Component:** `GetStartedSection`

**Each card:**
- 48px circular icon container (`colorBrandBackground2` / `colorBrandForeground1`)
- Label text below (semibold, 300)
- Click → opens corresponding dialog or navigates
- Hover → `colorNeutralBackground1Hover` + `shadow8`

**Quick Actions:**

| Action | Icon | Click Target | Sub-Project |
|--------|------|-------------|-------------|
| Create New Matter | `DocumentAdd24Regular` | Create New Matter dialog (Block 6) | Yes — see Block 6 |
| Create New Project | `FolderAdd24Regular` | Quick create dialog | Planned |
| Assign To Counsel | `BriefcaseMedical24Regular` | Assign Counsel dialog | Planned |
| Analyze New Document | `DocumentSearch24Regular` | Document analysis dialog | Planned |
| Search Document Files | `Search24Regular` | Search dialog | Planned |
| Send Email Message | `Mail24Regular` | Email compose | Planned |
| Schedule New Meeting | `CalendarAdd24Regular` | Calendar event create | Planned |

> **Note:** Each Get Started action card will be implemented as a separate sub-project with specific features and functionality defined at that time. The Create New Matter dialog (Block 6) is the first completed sub-project. Remaining cards will show placeholder behavior until their sub-project is built.

#### 1B: Quick Summary (AI Briefing)

**Layout:** Fixed-width card (280-320px) positioned right of Get Started.

**Component:** Briefing card with sparkle icon header.

**Content (computed from matter data):**
- **Active matters count** (bold) + total spend / total budget
- **Matters at risk count** (red bold) + names of at-risk matters
- **Overdue events count** (red bold) + number of affected matters
- **Top priority matter** (if any deadline within 7 days) — name, event type, days until
- **"Full briefing" button** → opens expanded dialog

**Expanded Briefing Dialog:**
- `DialogSurface` (500-700px width)
- Sparkle icon + "Portfolio Briefing" title
- Longer narrative paragraphs covering portfolio status, at-risk matters, overdue events, top priority, and budget watch items
- Close button

**AI Integration:** The quick summary uses deterministic aggregation by default. When available, the AI Playbook platform can enhance the briefing with narrative analysis and risk assessment (see [AI Playbook Integration](#ai-playbook-integration)).

**Data Requirements:**
- `sprk_matter` — financials (totalBudget, totalSpend, utilizationPercent), performanceGrades, overdueEventCount
- `sprk_event` — critical dates within 7 days

**Dependencies:** None — this is the topmost section.

**Prototype Reference:** `GetStartedSection.tsx`, `RefinedHomeVariant.tsx` (lines 179-208, 308-347, 428-466)

---

### Block 2: Portfolio Health Summary

**Purpose:** A 4-card metric strip providing at-a-glance portfolio health indicators.

**Layout:** 4-column CSS grid (`repeat(4, 1fr)`), responsive to 2-column at 900px.

**Component:** `PortfolioHealthSummary`

**Cards:**

| Card | Icon | Value | Subtext | Color Logic |
|------|------|-------|---------|-------------|
| Portfolio Spend | `Wallet20Regular` | `$X.XM` / `$X.XM` | Utilization bar + percentage | Bar: green (<65%), orange (65-85%), red (>85%) |
| Matters at Risk | `Warning20Regular` | Count | "D or below in any grade" | Icon bg: red if >0, green if 0 |
| Overdue Events | `CalendarCancel20Regular` | Count | "Across N matters" | Icon bg: red if >0, green if 0 |
| Active Matters | `Briefcase20Regular` | Count | "$XK avg spend" | Brand blue |

**Utilization Bar:** 4px track with colored fill. Percentage text to the right.

```
Color thresholds:
  < 65%  → colorPaletteGreenBackground2  (healthy)
  65-85% → colorPaletteDarkOrangeBackground3  (warning)
  > 85%  → colorPaletteRedBackground3  (critical)
```

**Data Requirements:**
- Aggregated from `sprk_matter` records: sum of financials.totalBudget, financials.totalSpend
- Count of matters where any performanceGrade starts with D or F
- Sum of overdueEventCount across all matters

**VisualHost Note:** This component is built custom rather than using VisualHost — see [VisualHost Relationship](#visualhost-relationship).

**Dependencies:** None

**Prototype Reference:** `PortfolioHealthSummary.tsx`

---

### Block 3: Updates Feed

**Purpose:** A chronological activity stream showing system events, emails, documents, invoices, and alerts. Serves as the primary "what happened" view with filtering, AI summaries, and flag-to-todo capability.

**Layout:** Full-width within the left column of the 50/50 grid. Part of a tabbed container shared with To Do (Block 4).

**Component:** `ActivityFeed`

#### 3A: Workspace Tab Switcher

The Updates feed and To Do list share a `TabList` header:

| Tab | Badge |
|-----|-------|
| Updates | — |
| To Do | Dynamic count (system items + user-flagged items) |

#### 3B: Filter Bar

Horizontal scrollable row of filter "pill" cards:

| Filter | Match Logic |
|--------|-------------|
| All | No filter |
| High Priority | `priority === 'critical' \|\| 'high'` |
| Overdue | `type === 'overdue-event'` |
| Alerts | `type === 'financial-alert' \|\| 'status-change'` |
| Emails | `type === 'email'` |
| Documents | `type === 'document'` |
| Invoices | `type === 'invoice'` |
| Tasks | `type === 'task'` |

- Each filter shows its count in parentheses
- Active filter uses `colorBrandBackground2` / `colorBrandStroke1`
- Only filters with matching items are displayed

#### 3C: Feed Items

Each feed item is a card with:

| Element | Description |
|---------|-------------|
| Unread dot | 8px blue circle (left edge) if `isUnread` |
| Type icon | 36px circular container with type-specific icon and color |
| Title | Semibold, truncated single line |
| Description | Regular weight, truncated single line |
| Meta row | Priority badge + type badge + entity name + timestamp |
| Flag button | Toggle flag icon — adds/removes item from To Do list |
| AI Summary button | Sparkle icon + "AI Summary" text |

**Feed Item Types:**

| Type | Icon | Color (bg/fg) |
|------|------|---------------|
| email | `Mail20Regular` | Blue |
| document | `Document20Regular` | Brand |
| matter | `Briefcase20Regular` | Purple |
| task | `ClipboardTask20Regular` | Green |
| invoice | `ReceiptMoney20Regular` | Dark Orange |
| ai-analysis | `Sparkle20Regular` | Brand |
| status-change | `Warning20Regular` | Yellow |
| overdue-event | `CalendarCancel20Regular` | Red |
| financial-alert | `MoneyCalculator20Regular` | Red |

**Priority Styling:**
- Critical: Red border + red background (`colorPaletteRedBackground1`)
- High: Dark orange border
- Normal: Standard neutral border; blue border if unread

**Sort Order:** Priority first (critical > high > normal), then timestamp descending.

#### 3D: Flag-as-To-Do Interaction

- Each feed item has a flag toggle button (left of AI Summary button)
- Unflagged: `Flag16Regular` with neutral color
- Flagged: `Flag16Filled` with `colorBrandForeground1`
- Tooltip: "Add to To Do" / "Remove from To Do"
- Click toggles the flag state and creates/removes a ToDoItem
- The "Add to To Do" button also appears in the AI Summary dialog footer (for unflagged items)

**State Flow:**
1. User clicks flag on feed item
2. `sprk_todoflag = true` set on underlying Event
3. `sprk_todosource = 'User'` recorded
4. Item appears in To Do tab with flag source indicator
5. To Do badge count increments
6. Click flag again → unflag, remove from To Do, decrement badge

#### 3E: AI Summary Dialog

Triggered by the "AI Summary" button on any feed item. Uses the Spaarke AI Playbook platform.

**Dialog Layout:**
- Title: Sparkle icon + "AI Summary"
- Item header: Type badge + item title
- Loading state: Spinner + "Analyzing..." + "Running AI analysis playbook" (1.8s simulated delay in prototype; production calls AI Playbook endpoint)
- Analysis card: `colorNeutralBackground2` card with sparkle header + narrative text
- Suggested Actions: List of clickable action rows with `ArrowRight16Regular` icon
- Footer: "Add to To Do" primary button (if not flagged) + Close button

**Suggested Action Types:**

| Type | Behavior |
|------|----------|
| navigate | Navigate to entity view/record |
| dialog | Open dialog (create task, analyze document, etc.) |
| action | Execute action (approve invoice, flag for review) |

**Data Requirements:**
- `sprk_event` — feed items are Events with type, priority, timestamp, regarding lookup
- Feed items sorted and filtered on the client from a BFF query of recent Events

**Dependencies:** Block 4 (for flag-to-todo state)

**Prototype Reference:** `ActivityFeed.tsx`

---

### Block 4: Smart To Do

**Purpose:** A prioritized work queue showing system-generated, user-flagged, and manually created to-do items. Each item displays transparent priority and effort scores.

**Layout:** Shares the left column with Updates Feed (Block 3) via workspace tab switcher.

**Component:** `SmartToDo`

#### 4A: Manual Add Bar

- Plus icon + underline input ("Add a to-do...") + "Add" button
- Enter key submits
- Creates a new Event with `sprk_todoflag = true`, `sprk_todosource = 'User'`, `type = Task`

#### 4B: To Do Item

Each item is a card with:

| Element | Description |
|---------|-------------|
| Drag handle | `ReOrder16Regular` (grip icon for future reorder) |
| Checkbox | Toggle complete/open |
| Source indicator | System (bot), User (flag), AI (sparkle) with tooltip |
| Title | Semibold, with strikethrough if completed |
| Priority badge | Critical (red), High (orange), Medium (blue), or none |
| Effort badge | Timer icon + "Low"/"Med"/"High" with color (green/orange/red) and tooltip showing estimated minutes |
| Context text | Organization name, practice area, or "Manually created" |
| Due label | "Overdue" (red), "3d" (red), "7d" (orange), or date |
| Dismiss button | `DismissCircle16Regular` — moves to dismissed section |
| AI button | Sparkle icon + "AI" — opens AI Summary dialog |

**Critical Items:** Red border + red background (`colorPaletteRedBackground1`).

**Completed Items:** 50% opacity, strikethrough title, no badges.

**Dismissed Section:** Collapsible at bottom. Arrow toggle + "N dismissed" label.

#### 4C: System-Generated To Do Items

Auto-flagged Events that appear in the To Do list:

| Trigger | Title Pattern | Source Type |
|---------|---------------|-------------|
| Overdue events on a matter | "Address N overdue events on [Matter]" | overdue |
| Budget utilization >85% | "Review budget for [Matter] (X% utilized)" | budget |
| Deadline within 14 days | "Prepare for [Event Type] — [Matter]" | deadline |
| Invoice pending review | "Review Invoice #[ID] ($[Amount])" | invoice |
| Task assigned to user | "Complete [Task Description]" | task |

**Idempotency:** System checks for existing open to-dos before creating duplicates.

#### 4D: AI Summary Dialog (To Do)

Similar to Block 3E but includes a Priority × Effort scoring grid:

**Scoring Grid (2-column):**
- Left column: Priority score (large number, color-coded) + badge + reason string
- Right column: Effort score (large number, color-coded) + effort badge + estimated minutes + factor checklist

**Effort Factor Checklist:**
- Each factor shown as "● applied" (orange) or "○ not applied" (muted) with multiplier value
- Factors: Multiple overdue events, External coordination, High-value matter, High activity volume, Multiple performance issues

> For full scoring methodology, see [Priority & Effort Scoring Logic](#priority--effort-scoring-logic).

**Data Requirements:**
- `sprk_event` — all Event-level fields including `sprk_todoflag`, `sprk_todostatus`, `sprk_todosource`, `sprk_priority`, `sprk_priorityscore`, `sprk_effort`, `sprk_effortscore`, `sprk_estimatedminutes`
- `sprk_matter` — financials, performanceGrades, overdueEventCount for scoring context
- `sprk_criticaldate` — upcoming deadlines

**Dependencies:** Block 3 (shares workspace tab container and flag state)

**Prototype Reference:** `SmartToDo.tsx`

---

### Block 5: My Portfolio Widget

**Purpose:** A tabbed sidebar widget showing the user's matters, projects, and recent documents with status indicators and performance grades.

**Layout:** Right column of the 50/50 grid. Fixed card with header, tab bar, item list, and footer.

**Component:** `MyPortfolioWidget`

#### 5A: Widget Structure

| Section | Description |
|---------|-------------|
| Header | "My Portfolio" title |
| Tab bar | Matters (briefcase) / Projects (clipboard) / Documents (document) — each with count badge |
| Item list | Scrollable list of items (max 5 default) |
| Footer | "View All [Tab]" link button with arrow icon → navigates to MDA entity view |

#### 5B: Matters Tab

Each matter item displays:

| Element | Description |
|---------|-------------|
| Name | Semibold, truncated |
| Status badge | Derived: Critical (red), Warning (orange), On Track (green) |
| Description | Type + Organization |
| Meta | Practice area + last activity timestamp |
| Grade pills | 3 small colored pills: Budget Controls, Guidelines, Outcomes |
| Overdue indicator | Red text with `CalendarCancel16Regular` icon if >0 overdue |

**Status Derivation Logic (computed, not stored):**
- **Critical:** Overdue events >0, OR utilization >90%, OR any D/F grade
- **Warning:** Utilization >75%, OR any C grade
- **On Track:** All metrics healthy

**Grade Pill Colors:**

| Grade Letter | Background | Foreground |
|-------------|------------|------------|
| A | `colorPaletteGreenBackground2` | `colorPaletteGreenForeground1` |
| B | `colorBrandBackground2` | `colorBrandForeground1` |
| C | `colorPaletteDarkOrangeBackground2` | `colorPaletteDarkOrangeForeground1` |
| D, F | `colorPaletteRedBackground2` | `colorPaletteRedForeground1` |

Tooltip on each pill shows the dimension name (e.g., "Budget Controls").

#### 5C: Projects Tab

Each project item displays:
- Name + status badge (Planning/Active/On Hold/Completed with appropriate colors)
- Description: Type + Owner
- Meta: Practice area + last activity

#### 5D: Documents Tab

Each document item displays:
- Document icon (`Document20Regular`, brand color)
- Name (semibold)
- Description
- Meta: Document type + Matter name + last modified timestamp

#### 5E: Footer Navigation

"View All [Tab]" button navigates to the corresponding MDA entity view (Matters, Projects, Documents).

**Data Requirements:**
- `sprk_matter` — name, type, organization (lookup to `sprk_organization`), practiceArea, financials, performanceGrades, overdueEventCount, lastActivity
- `sprk_project` — name, type, owner, status, practiceArea, lastActivity
- `sprk_document` — name, type, description, matter relationship, modifiedon

**Dependencies:** None

**Prototype Reference:** `MyPortfolioWidget.tsx`

---

### Block 6: Create New Matter Dialog

**Purpose:** A multi-step wizard for creating new legal matters with AI-assisted document analysis and form pre-fill. This is the first fully-specified Get Started action card sub-project.

**Component:** `CreateNewMatterDialog`

#### 6A: Dialog Shell

- Full-width dialog surface (960px max, 85vh max height)
- Header: Title + close button on `colorNeutralBackground3`
- Body: 2-column layout — sidebar (260px, steps) + content area (flexible)
- Footer: Cancel + Back + Next/Finish buttons

#### 6B: Sidebar Step Tracker

Vertical list of step items with state indicators:

| State | Circle Style | Icon |
|-------|-------------|------|
| Completed | Green background | `Checkmark20Regular` |
| Active | Brand background | `Circle20Regular` |
| Pending | Brand tint background | `Circle20Regular` |

Steps are clickable (navigate back to completed steps only).

**Base Steps:**
1. Add file(s)
2. Create record
3. Next Steps

**Follow-On Steps** (dynamic, added when selected in step 3):
- Assign to Counsel
- Draft Matter Summary
- Send Email to Client

A visual divider separates base steps from follow-on steps.

#### 6C: Step 1 — Add Files

- Drag-and-drop zone with `ArrowUpload24Regular` icon
- Click or drop to add files
- File list below: icon + name + size + delete button
- Supported formats: PDF, DOCX, XLSX (max 10MB each)
- Files are optional — user can proceed without uploading

#### 6D: Step 2 — Create Record (AI Pre-Fill)

**AI Processing State:**
- If files were uploaded: Show spinner + "AI is analyzing your documents..." + "Extracting matter type, parties, key dates, and summary information"
- Uses AI Playbook platform for document analysis (see [AI Playbook Integration](#ai-playbook-integration))
- Simulated 2.5s delay in prototype; production calls AI Playbook endpoint

**Form (2-column grid):**

| Field | Type | AI Pre-Fill | Required |
|-------|------|------------|----------|
| Matter Type | Dropdown | Yes | Yes |
| Practice Area | Dropdown | Yes | No |
| Matter Name / Title | Input | Yes | Yes |
| Organization | Input (lookup → `sprk_organization`) | Yes | No |
| Lead Attorney | Dropdown | No | No |
| Estimated Budget | Input | Yes | No |
| Key Parties | Input | Yes | No |
| Summary | Textarea (4 rows) | Yes | No |

AI-prefilled fields show a sparkle tag: `<Sparkle20Regular /> AI` next to the label.

Top-right corner shows "AI Pre-filled" badge when AI extraction completed.

#### 6E: Step 3 — Next Steps

Checkbox selection of follow-on actions. Each option is a full-width card:
- Checkbox + title (semibold) + description text
- Selected state: Brand border + brand background tint
- Zero or more can be selected
- If none selected, "Next" button becomes "Finish"

**Follow-On Options:**

| Action | Description |
|--------|-------------|
| Assign to Counsel | Assign this matter to outside counsel from approved vendor list |
| Draft Matter Summary | Generate AI-assisted summary for stakeholder communication |
| Send Email to Client | Compose and send notification email to client or stakeholders |

#### 6F: Follow-On Steps

**Assign to Counsel:** Organization dropdown (lookup → `sprk_organization`) + budget allocation input + assignment notes textarea

**Draft Matter Summary:** AI-generated summary card (pre-filled from Step 2 summary via AI Playbook) + recipients input

**Send Email to Client:** To field (pre-filled from organization) + subject (pre-filled from matter name) + message body textarea (pre-filled with template)

**Data Requirements:**
- `sprk_matter` — create new record with all form fields
- `sprk_event` — create related Events for follow-on actions
- `sprk_document` — attach uploaded files via SharePoint Embedded
- AI Playbook for document text extraction and entity recognition

**Dependencies:** None (dialog triggered from Get Started cards)

**Prototype Reference:** `CreateNewMatterDialog.tsx`

---

### Block 7: Notification Panel

**Purpose:** A slide-out drawer showing recent activity notifications with type filtering.

**Component:** `NotificationPanel` (Fluent `Drawer`)

**Features:**
- Opens from the notification bell button in the page header
- Activity list with avatar, type badge, timestamp
- Filter toggle buttons: Documents, Invoices, Status, Analysis
- Close button

**Data Requirements:** Same as Block 3 (Events query), filtered to recent/unread items.

**Dependencies:** None

**Prototype Reference:** `NotificationPanel.tsx`

---

## Priority & Effort Scoring Logic

Priority and effort are **Event-level** fields already added to the Dataverse `sprk_event` entity. Every Event in the system has a calculable priority (urgency) and effort (work required). These scores:

- Drive sort order in the To Do list
- Display on Event records in matter timelines, calendar views, and reports
- Are transparent — users can see exactly how each score was computed via reason strings

### To Do as an Event Flag

Rather than a separate entity, the To Do is implemented as **a flag on the existing Event entity**. Any Event can be flagged as a to-do, which adds it to the user's To Do list view.

**Why a flag, not a separate entity:**
- No entity proliferation (no duplicate forms, views, security roles)
- Calendar integration is automatic (flagged events with due dates appear on calendar)
- Updates feed already knows about Events (overdue events already surface)
- One record, one status (no sync issues between duplicate records)
- Event-to-Event relationships preserved

**To Do list view query:**
```
Events WHERE sprk_todoflag = true
  AND ownerid = [current user]
  AND sprk_todostatus = 'Open'
ORDER BY sprk_priorityscore DESC, sprk_tododuedate ASC
```

### To Do Status Model

| Status | Meaning | Behavior |
|--------|---------|----------|
| **Open** | Active, needs attention | Shown in main To Do list |
| **Completed** | User completed the action | Shown dimmed with strikethrough; checkbox checked |
| **Dismissed** | User acknowledges but won't act | Hidden in collapsible "dismissed" section |

### Priority Score

Priority answers: **"How urgently does this need my attention?"**

Base score starts at 0, factors are additive:

| Factor | Points | Data Source | Logic |
|--------|--------|-------------|-------|
| **Overdue** | 30 + (days × 5) | `sprk_event.duedate < now()` | Escalates daily — 7d overdue = 65 pts |
| **Due within 3 days** | +20 | `sprk_event.duedate` | Imminent deadline |
| **Due within 7 days** | +10 | `sprk_event.duedate` | Near-term deadline |
| **Due within 14 days** | +5 | `sprk_event.duedate` | Approaching deadline |
| **Budget >90% utilized** | +15 | `sprk_matter.sprk_utilizationpercent` | Critical threshold |
| **Budget >75% utilized** | +5 | `sprk_matter.sprk_utilizationpercent` | Warning threshold |
| **D/F performance grade** | +20 | `sprk_matter` performance grades | Any dimension D or below |
| **C performance grade** | +5 | `sprk_matter` performance grades | Any dimension at C level |
| **High-value matter (>$200K)** | +5 | `sprk_matter.sprk_totalbudget` | Financial exposure |
| **Pending invoice** | +10 | `sprk_event.type = Invoice Review` | Approval bottleneck |

**Score → Priority Label:**

| Score | Priority | Badge Color | `sprk_priority` Value |
|-------|----------|-------------|----------------------|
| > 50 | Critical | Red (danger) | High |
| 31–50 | High | Orange (warning) | High |
| 11–30 | Medium | Blue (informative) | Normal |
| 0–10 | Normal | None | Low |

**AI Enhancement:** When deterministic scoring is insufficient, the AI Playbook can adjust the score (-20 to +20) with reasoning. AI adjustments are stored separately so the deterministic base and AI modifier are both transparent.

### Effort Score

Effort answers: **"How much work will this take to complete?"**

#### Step 1: Base Effort by Event Type

| Event Type | Base Score | Base Minutes | Rationale |
|------------|-----------|--------------|-----------|
| Hearing / Filing Prep | 40 | 90 min | Coordination, document assembly |
| Overdue Event Resolution | 35 | 60 min | Review & resolve multiple items |
| Budget Review | 30 | 45 min | Financial analysis & decision |
| Task / Assignment | 25 | 30 min | Action requiring judgment or delegation |
| Performance Review | 20 | 30 min | Grade analysis and follow-up |
| Invoice Review | 15 | 20 min | Check rates, hours, guidelines |
| Feed Item Follow-up | 15 | 15 min | Quick review and action |
| Manual To Do | 10 | 15 min | User-created (default baseline) |

#### Step 2: Complexity Multipliers

| Factor | Multiplier | Condition |
|--------|-----------|-----------|
| Multiple overdue events (>2) | ×1.3 | `sprk_matter.overdueEventCount > 2` |
| External coordination | ×1.2 | Matter has `sprk_organization` assigned |
| High-value matter (>$200K) | ×1.1 | `sprk_matter.sprk_totalbudget > 200000` |
| High activity volume | ×1.2 | `sprk_matter` invoiceCount > 12 |
| Multiple performance issues | ×1.3 | 2+ grades at C or below |

**Calculation:** `effortScore = min(baseScore × product(applicable multipliers), 100)`

#### Step 3: Score → Effort Label

| Score | Effort | Meaning | Typical Time |
|-------|--------|---------|-------------|
| 1–25 | Low | Quick action, minimal judgment | < 30 minutes |
| 26–50 | Medium | Moderate analysis or coordination | 30 min – 2 hours |
| 51–100 | High | Significant work, multiple steps | 2+ hours |

### Priority × Effort Matrix

When both scores are visible together, they create a natural quadrant for decision-making:

```
                    LOW EFFORT      MEDIUM EFFORT    HIGH EFFORT
CRITICAL PRIORITY   Do Now          Do Now           Plan & Start
HIGH PRIORITY       Do Next         Schedule         Plan & Delegate
MEDIUM PRIORITY     Quick Win       Batch Together   Delegate or Defer
NORMAL PRIORITY     When Free       Batch Together   Defer
```

### Scoring Refresh Strategy

| Score | Refresh Trigger | Method |
|-------|----------------|--------|
| **Priority** | Real-time on data change | Dataverse plugin on Event/Matter update recalculates. Daily batch for time-based factors (days overdue). |
| **Effort** | One-time on creation | Calculated once when Event is created or first flagged. Only recalculated if Event changes significantly or user requests re-analysis. |
| **AI adjustments** | On-demand + daily batch | Priority AI runs daily for all open events. Effort AI runs only on creation or user request. |

### How Items Get Flagged

**System-Generated (Automatic):** An update service (Dataverse plugin or Power Automate flow) runs on entity triggers and sets `sprk_todoflag = true`, `sprk_todosource = 'System'`.

**User-Flagged (Manual):**
- From the Updates Feed: flag toggle button on each feed item
- From the To Do Tab: "Add a to-do..." input creates a new Event
- From any Record View (future): Command bar "Add to To Do" button

**AI-Recommended (Future):** AI Playbook analysis can suggest to-do items. When the user accepts, `sprk_todoflag = true`, `sprk_todosource = 'AI'`.

---

## AI Playbook Integration

All AI functionality in the workspace runs through the **Spaarke AI Playbook platform**. The workspace does not implement standalone AI logic — it invokes playbooks and consumes their responses.

### AI Touchpoints in the Workspace

| Feature | Playbook Type | Input | Output |
|---------|--------------|-------|--------|
| AI Summary (feed item) | Event Analysis | Event record + related matter context | Narrative summary + suggested actions |
| AI Summary (to-do item) | Event Analysis + Scoring | Event + matter + priority/effort context | Summary + scoring grid + action suggestions |
| Quick Summary briefing | Portfolio Analysis | All user's matters (aggregated) | Narrative briefing text |
| Create Matter — AI Pre-fill | Document Analysis | Uploaded file(s) | Extracted fields: matter type, parties, key dates, summary |
| Draft Matter Summary | Content Generation | Matter record fields | Formatted stakeholder summary |
| Priority AI adjustment | Priority Assessment | Event + matter structured context | Score modifier (-20 to +20) + reasoning |
| Effort AI adjustment | Effort Assessment | Event + matter structured context | Score modifier + reasoning |

### Integration Pattern

```
Workspace Component → BFF Endpoint → AI Playbook Service → LLM → Response
```

1. Component triggers AI action (user click or system event)
2. BFF endpoint assembles structured context from Dataverse
3. AI Playbook service selects and executes the appropriate playbook
4. Response returned to component for display

### Key Principle: AI-Enhanced, Not AI-Dependent

Every feature that uses AI has a deterministic fallback:
- **Priority scoring** works without AI (rule-based scoring covers all factors)
- **Effort scoring** works without AI (base effort + multipliers)
- **Quick Summary** works without AI (aggregated metrics displayed as data)
- **Create Matter** works without AI (manual form entry)

AI enhances these features but never gates them.

---

## VisualHost Relationship

The VisualHost framework (`@spaarke/ui-components`) provides configuration-driven visualization components (MetricCard, BarChart, DonutChart, etc.) used elsewhere in the Spaarke platform. It was evaluated for use in this workspace.

**Conclusion:** VisualHost is not applicable to the workspace solution. The workspace's UI patterns — activity feed, to-do list, portfolio cards, briefing narrative — are interaction-heavy work management patterns, not data visualizations. VisualHost's strength is charts and metrics dashboards, which this page does not contain.

**Consistency requirement:** Although VisualHost components are not used in this workspace, the workspace and VisualHost-based views should maintain visual consistency. Both use Fluent UI v9 tokens for colors, typography, and spacing, ensuring a unified look across the application.

---

## Dataverse Entity Requirements

All UI features map to existing Dataverse entities. No new entities are required. The following fields are used:

### sprk_event (Existing Entity)

**To Do Flag Fields (already added to Dataverse):**

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_todoflag` | Yes/No | When true, Event appears in To Do list |
| `sprk_tododuedate` | Date | Optional due date for to-do aspect |
| `sprk_todostatus` | Choice | Open / Completed / Dismissed |
| `sprk_todoassigned` | Lookup → Contact | Who the to-do is assigned to |
| `sprk_todosource` | Choice | System / User / AI |

**Event-Level Scoring Fields (already added to Dataverse):**

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_priority` | Choice | Low / Normal / High — system-calculated |
| `sprk_priorityscore` | Integer (0-100) | Numeric score for ranking |
| `sprk_priorityreason` | Text (500) | Transparent explanation of score factors |
| `sprk_effort` | Choice | Low / Medium / High — system-calculated |
| `sprk_effortscore` | Integer (1-100) | Numeric effort score |
| `sprk_effortreason` | Text (500) | Transparent explanation of effort factors |
| `sprk_estimatedminutes` | Integer | Estimated completion time in minutes |

### sprk_matter (Existing Entity — Used As-Is)

| Field | Purpose |
|-------|---------|
| `sprk_name`, `sprk_type`, `sprk_practicearea` | Matter identity |
| Organization lookup (→ `sprk_organization`) | Associated organization |
| `sprk_totalbudget`, `sprk_totalspend`, `sprk_utilizationpercent` | Financial rollups |
| `sprk_budgetcontrols_grade`, `sprk_guidelinescompliance_grade`, `sprk_outcomessuccess_grade` | Performance grades |
| `sprk_overdueeventcount` | Rollup or calculated field |

### sprk_project (Existing Entity — Used As-Is)

- `sprk_name`, `sprk_type`, `sprk_practicearea`, `sprk_owner`, `sprk_status`, `sprk_budgetused`

### sprk_organization (Existing Entity — Used As-Is)

- Used for the Organization field on matters, counsel assignments, and any non-person references
- **Note:** There is no `sprk_firm` entity. All firm/organization references use `sprk_organization`.

### sprk_document (Existing Entity — Used As-Is)

- `sprk_name`, `sprk_type`, matter relationship, `modifiedon`
- Files stored via SharePoint Embedded

### sprk_contact (Existing Entity — Used As-Is)

- Used for `sprk_todoassigned` lookup and Lead Attorney field
- Used for notification recipients

---

## Dark Mode Compliance

Dark mode is a **strict requirement** for all workspace components. The Fluent UI v9 theme system handles light/dark mode switching automatically when semantic tokens are used correctly.

### Rules

1. **Use Fluent UI tokens for ALL colors** — never hardcode hex/rgb/hsl values
2. **Use semantic tokens** — e.g., `tokens.colorNeutralBackground1` not `tokens.colorWhite`
3. **Icons from `@fluentui/react-icons`** — they automatically adapt to theme
4. **Test in both modes** — every component verified in light mode AND dark mode before acceptance
5. **Images and logos** — provide dark-mode variants or use transparent backgrounds

### Theme System

- Theme managed via `FluentProvider` with `webLightTheme` / `webDarkTheme`
- Theme toggle accessible from page header overflow menu
- Theme persists to localStorage and respects system preference on first load

### Token Usage Patterns

| Purpose | Token | Adapts to Dark Mode |
|---------|-------|:------------------:|
| Page background | `colorNeutralBackground2` | Yes |
| Card background | `colorNeutralBackground1` | Yes |
| Card hover | `colorNeutralBackground1Hover` | Yes |
| Borders | `colorNeutralStroke1`, `colorNeutralStroke2` | Yes |
| Primary text | `colorNeutralForeground1` | Yes |
| Secondary text | `colorNeutralForeground2` | Yes |
| Muted text | `colorNeutralForeground3` | Yes |
| Brand accent | `colorBrandForeground1`, `colorBrandBackground2` | Yes |
| Danger (red) | `colorPaletteRedForeground1`, `colorPaletteRedBackground1` | Yes |
| Warning (orange) | `colorPaletteDarkOrangeForeground1`, `colorPaletteDarkOrangeBackground2` | Yes |
| Success (green) | `colorPaletteGreenForeground1`, `colorPaletteGreenBackground2` | Yes |

### Definition of Done (Dark Mode)

Every component must pass:
- [ ] Uses Fluent tokens for all colors
- [ ] No hardcoded color values in styles
- [ ] Tested and verified in light mode
- [ ] Tested and verified in dark mode
- [ ] Icons render correctly in both modes
- [ ] Text is legible in both modes (correct contrast)
- [ ] Borders and dividers visible in both modes

---

## Accessibility Requirements

| Requirement | Implementation |
|-------------|---------------|
| All inputs have labels | `<Label>` component with `htmlFor` attribute |
| Keyboard navigation | Tab through all interactive elements. `TabList` handles arrow keys. |
| ARIA labels | All icon-only buttons have `aria-label` props |
| Tooltips | All abbreviated or icon-only elements have `<Tooltip>` |
| Focus management | Dialog traps focus. Drawer manages focus on open/close. |
| Color contrast | Fluent tokens ensure WCAG AA compliance in both light and dark modes |
| Screen reader | Badge values, metric values, and status text are accessible to screen readers |

---

## Build Sequence & Dependencies

```
Block 2: Portfolio Health Summary  ─┐
Block 5: My Portfolio Widget       ─┤ (no dependencies — build in parallel)
Block 7: Notification Panel        ─┘
     │
Block 1: Get Started & Quick Summary (independent — build anytime)
     │
Block 3: Updates Feed ◄───────────┐
     └── Block 3D: Flag-as-ToDo   │ (shared state)
Block 4: Smart To Do ◄────────────┘
     │
Block 6: Create New Matter Dialog (independent — build in parallel with 3-4)
```

**Recommended Build Order:**

| Phase | Block | Rationale |
|-------|-------|-----------|
| 1 | **Block 2** — Portfolio Health Summary | Simple, high visibility, independent |
| 1 | **Block 5** — My Portfolio Widget | Independent, data display only |
| 2 | **Block 3** — Updates Feed | Core feature, drives engagement |
| 2 | **Block 4** — Smart To Do | Requires Block 3 for flag state sharing |
| 3 | **Block 1** — Get Started + Quick Summary | Polish, requires briefing logic |
| 3 | **Block 6** — Create New Matter Dialog | Can be built in parallel with 3-4 |
| 4 | **Block 7** — Notification Panel | Lower priority, supplements Block 3 |

**Parallel Opportunities:**
- Blocks 2, 5, 7 can be built in parallel (no dependencies on each other)
- Block 6 can be built in parallel with Blocks 3-4 (different developer)

---

## Fluent UI v9 Component Inventory

All Fluent components used across the workspace:

| Category | Components |
|----------|-----------|
| **Layout** | `makeStyles`, `tokens`, `mergeClasses` |
| **Typography** | `Text`, `Title3` |
| **Navigation** | `TabList`, `Tab` |
| **Data Display** | `Badge`, `Card`, `Tooltip` |
| **Input** | `Input`, `Textarea`, `Dropdown`, `Option`, `Checkbox`, `Label` |
| **Actions** | `Button` |
| **Feedback** | `Spinner`, `Dialog`, `DialogSurface`, `DialogBody`, `DialogTitle`, `DialogContent`, `DialogActions`, `Drawer` |

**Icon Inventory (from `@fluentui/react-icons`):**

| Usage | Icons |
|-------|-------|
| Get Started | `DocumentAdd24Regular`, `FolderAdd24Regular`, `BriefcaseMedical24Regular`, `DocumentSearch24Regular`, `Search24Regular`, `Mail24Regular`, `CalendarAdd24Regular` |
| Feed types | `Mail20Regular`, `Document20Regular`, `Briefcase20Regular`, `ClipboardTask20Regular`, `ReceiptMoney20Regular`, `Warning20Regular`, `CalendarCancel20Regular`, `MoneyCalculator20Regular` |
| AI | `Sparkle20Regular` |
| To Do | `Bot20Regular`, `Flag16Regular`, `Flag16Filled`, `Timer16Regular`, `ReOrder16Regular`, `DismissCircle16Regular`, `Add20Regular` |
| Portfolio | `Wallet20Regular`, `CalendarCancel16Regular`, `ArrowRight16Regular` |
| Dialog | `Dismiss24Regular`, `ArrowUpload24Regular`, `Checkmark20Regular`, `Delete20Regular`, `Circle20Regular` |
| General | `Alert24Regular`, `Open16Regular` |

---

## Supporting Patterns

These are reusable UI patterns used across multiple blocks:

### AI Summary Dialog Pattern

Used in Blocks 3 and 4. Common pattern:
1. Sparkle icon + "AI Summary" title
2. Loading state with Spinner (prototype: 1.8s simulated; production: AI Playbook call)
3. Analysis card with narrative text
4. Suggested actions list with arrow icons
5. Optional scoring grid (To Do variant)

### Source Indicator Pattern

Used in Block 4. Three-state indicator:

| Source | Icon | Tooltip |
|--------|------|---------|
| System | `Bot20Regular` | "Auto-flagged by system" |
| User | `Flag16Filled` | "Flagged by you" |
| AI | `Sparkle20Regular` | "AI-recommended" |

### Grade Pill Pattern

Used in Block 5. Compact colored badge showing letter grade:
- A grades: Green (`colorPaletteGreenBackground2` / `colorPaletteGreenForeground1`)
- B grades: Brand blue (`colorBrandBackground2` / `colorBrandForeground1`)
- C grades: Dark orange (`colorPaletteDarkOrangeBackground2` / `colorPaletteDarkOrangeForeground1`)
- D/F grades: Red (`colorPaletteRedBackground2` / `colorPaletteRedForeground1`)

### Effort Badge Pattern

Used in Block 4. Compact badge with timer icon:
- Low: Green bg, "Low" label
- Medium: Orange bg, "Med" label
- High: Red bg, "High" label
- Tooltip: "Effort: [Level] · ~Xh Xm"

---

## Appendix: Prototype Component Mapping

| Block | Prototype Component | Production Approach |
|-------|--------------------|--------------------|
| 1A | `GetStartedSection.tsx` | Copy & adapt — action cards are sub-project placeholders |
| 1B | Inline in `RefinedHomeVariant.tsx` | Extract to `BriefingSummary` component |
| 2 | `PortfolioHealthSummary.tsx` | Copy & adapt — replace mock data with Dataverse queries |
| 3 | `ActivityFeed.tsx` | Copy & adapt — replace mock data with BFF queries |
| 4 | `SmartToDo.tsx` | Copy & adapt — replace mock scoring with server-side calculation |
| 5 | `MyPortfolioWidget.tsx` | Copy & adapt — replace mock data with BFF queries, replace `useNavigate` with navigation contract |
| 6 | `CreateNewMatterDialog.tsx` | Adapt — integrate with production form handling + AI Playbook for document analysis |
| 7 | `NotificationPanel.tsx` | Copy & adapt — replace mock data with Events query |

---

## Open Questions

1. **Feed data source:** Should the Updates feed query Events directly via Xrm.WebApi, or should there be a dedicated BFF endpoint that aggregates across entity types?
2. **Quick Summary AI:** Should the briefing summary be generated by AI Playbook or computed deterministically from aggregate queries? (Deterministic is the fallback.)
3. **Calendar integration:** Should future iterations add a calendar widget to this page, or should calendar always navigate to the MDA calendar view?
4. **Notification persistence:** Should notification read/unread state be tracked in Dataverse or only in the client session?

---

**End of Document**
