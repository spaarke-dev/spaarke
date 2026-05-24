# Build a New Workspace Widget

> **Purpose**: Step-by-step tutorial for adding a new system workspace (or extending the existing pipeline). Walks through the file edits, the Dataverse seed update, the BFF impact analysis, and the deploy sequence. Includes a worked example: the **Calendar widget** — now a real shipped implementation (tasks 114/115, Round 9, 2026-05-22) rather than a forward projection.
>
> **Last reviewed**: 2026-05-22 (Task 123, Round 13). Refreshed from task 113 (Round 9) — Calendar widget shipped + Pattern D added + new pitfalls. Periodic review required.

> **Required reading before starting**:
> - [`../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — pipeline reference
> - [`../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — known coupling that affects new-widget design choices
> - [`CLAUDE.md` §10 — BFF Hygiene](../../CLAUDE.md) — binding rules if you add ANY BFF code
> - [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) — required pre-merge checklist for BFF additions

---

## 1. Decision tree: which pattern fits your new widget?

Before writing code, classify the new feature:

```
Is the new widget a "workspace" — i.e. one or more sections inside the workspace pipeline?
├── YES → Does the section need to be reusable across non-LegalWorkspace hosts
│         (or does the implementation already live in / fit a shared lib)?
│         ├── YES → Pattern D: SHARED-LIB WIDGET + THIN LW SHIM (Calendar pattern)
│         │         Worked example: Calendar (tasks 114 + 115)
│         │         - Widget proper in @spaarke/<lib>-components
│         │         - LegalWorkspace registration is a ~60-line shim that imports
│         │           the widget and renders it
│         │         - The 5 original sections (get-started, quick-summary,
│         │           latest-updates, todo, documents) do NOT yet follow this —
│         │           they remain LW-internal (still Pattern A); only NEW widgets
│         │           should default to Pattern D unless there's a reason not to
│         │
│         └── NO  → Pattern A: Section factory + Dataverse layout (LW-internal)
│                   Examples: Daily Briefing, My Work, Documents, Smart To Do List
│                   The section component lives in src/solutions/LegalWorkspace/
│                   and the registration factory reaches into LW-local
│                   DataverseService / FeedTodoSyncContext / hooks
│
└── NO  → Is it a one-off Code Page dispatcher (open an existing wizard) or AI output?
          ├── Code Page dispatcher → Pattern B: register a new workspace widget type with a
          │   thin wrapper that calls Xrm.Navigation.navigateTo
          │   Examples: create-project-wizard, find-similar-wizard
          │
          └── AI output (chat-driven) → Pattern C: register a new workspace widget type with
              a React component that renders the AI tool's output
              Examples: RedlineViewer, AnalysisEditor
```

**Pattern D is the recommended default for new widgets going forward** (task 115 proved it). Pattern A is still valid for sections that genuinely belong inside LegalWorkspace (e.g. they reuse `FeedTodoSyncContext` or LW-specific data shapes). Patterns B and C follow simpler paths in `register-workspace-widgets.ts` and are not unique to this guide.

---

## 2. Pattern A: Adding a new system workspace section

### Steps overview

1. Decide whether the section needs new components, or whether an existing section's content suffices.
2. Implement the section component(s) inside LegalWorkspace.
3. Create the `<sectionName>.registration.ts` file in `src/solutions/LegalWorkspace/src/sections/`.
4. Add the registration to `src/solutions/LegalWorkspace/src/sectionRegistry.ts`.
5. Add the layout to `scripts/system-layouts.json` (or design a multi-section layout).
6. Run `scripts/Deploy-SystemWorkspaceLayouts.ps1` to seed the layout in Dataverse.
7. Verify cold load in dev (operator smoke).
8. Deploy `LegalWorkspace` web resource via `code-page-deploy` skill.

NO BFF code changes are required IF:
- The section reads data only via `Xrm.WebApi` (Dataverse-side).
- The section does not need new BFF endpoints for cross-tenant data, AI grounding, or SharePoint Embedded operations.

If the section needs new BFF endpoints, you MUST follow CLAUDE.md §10 (BFF Hygiene) and load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md) before designing the addition.

### Step 1 — Decide section scope

Examples of section scope:
- **Single-data-source section** (e.g. todo): one Dataverse entity, one query pattern. Maps cleanly to one `ContentSectionConfig` with one query hook.
- **Multi-card aggregation section** (e.g. quick-summary): N independent counts, presented as a card grid. Maps to a `MetricCardSectionConfig` (or a `ContentSectionConfig` if cards are interactive).
- **AI-curated section** (e.g. daily-briefing): server-side analysis returned by a BFF endpoint. Maps to `ContentSectionConfig` with a TTL-cached fetch hook.

### Step 2 — Implement section components

Place under `src/solutions/LegalWorkspace/src/components/<SectionName>/`. Required for the widget to be data-correct in BOTH embedded (SpaarkeAi tab) and standalone (LegalWorkspace) hosts:

- Use the `webApi` + `userId` props passed via `SectionFactoryContext` — NEVER reach for the Xrm global directly.
- Use the `scope` + `businessUnitId` context if the section needs "my records" vs "all in my BU" filtering.
- Honor `context.onNavigate`, `context.onOpenWizard`, `context.onBadgeCountChange`, `context.onRefetchReady` callbacks for cross-section UX.

Reference the existing `quickSummary.registration.ts` (single content section with custom toolbar + 6-card grid) or `documents.registration.ts` (single content section with a per-card grid + view picker).

### Step 3 — Create the registration file

File: `src/solutions/LegalWorkspace/src/sections/<sectionName>.registration.ts`.

```ts
import * as React from "react";
import { CalendarRegular } from "@fluentui/react-icons";
import type {
  SectionRegistration,
  SectionFactoryContext,
  ContentSectionConfig,
} from "@spaarke/ui-components";
import { CalendarSection } from "../components/Calendar/CalendarSection";

export const calendarRegistration: SectionRegistration = {
  id: "calendar",                          // UNIQUE — must not clash with existing IDs
  label: "Calendar",                       // shown in wizard step 2
  description: "Upcoming events and deadlines",
  icon: CalendarRegular,
  category: "productivity",                // overview | data | ai | productivity
  defaultHeight: "440px",

  factory(context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "calendar",
      type: "content",
      title: "Calendar",
      style: {},
      renderContent: () =>
        React.createElement(CalendarSection, {
          webApi: context.webApi as any,
          userId: context.userId,
          scope: context.scope,
          businessUnitId: context.businessUnitId,
        }),
    };
  },
};

export default calendarRegistration;
```

### Step 4 — Register in `sectionRegistry.ts`

```ts
import { calendarRegistration } from "./sections/calendar.registration";

export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  dailyBriefingRegistration,
  calendarRegistration,                  // NEW
] as const;
```

The dev-mode guard at the bottom of `sectionRegistry.ts` will alert in console if you accidentally duplicate an ID.

### Step 5 — Add the system layout

Open `scripts/system-layouts.json` and add a new entry:

```json
{
  "name": "Calendar",
  "sectionId": "calendar",
  "layoutTemplateId": "single-column",
  "sortOrder": 4,
  "isDefault": false,
  "description": "Upcoming events and deadlines. Operator request 2026-MM-DD."
}
```

- `name`: appears in the Workspaces dropdown.
- `sectionId`: must match the registration `id` above.
- `layoutTemplateId`: typically `single-column` for a single-section workspace. Use `3-row-mixed` for the Corporate Workspace pattern.
- `sortOrder`: position among the Dataverse-system layouts (Daily Briefing=0, todo=1, quick-summary=2, documents=3, calendar=4).
- `isDefault`: set TRUE for ONLY ONE layout. Setting two will pick the lowest-sortOrder one (the BFF's `GetDefaultLayoutAsync` Step 2 orders by sortOrder ASC).

### Step 6 — Seed the Dataverse record

Run the seed script:

```pwsh
pwsh scripts/Deploy-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <DataverseUrl>
```

The script reads `system-layouts.json` and upserts each layout via the `sprk_workspacelayout` Web API with `sprk_issystem=true`. The user running the script becomes the `ownerid`, but this does NOT gate visibility — system records are visible to all authenticated users.

Verify via:

```pwsh
pwsh scripts/Verify-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <DataverseUrl>
```

(If a verify script doesn't exist for your environment, query Dataverse directly: `GET /api/data/v9.2/sprk_workspacelayouts?$filter=sprk_issystem eq true&$select=sprk_name,sprk_sortorder,sprk_isdefault`.)

### Step 7 — Verify cold load in dev

After seeding, restart SpaarkeAi (or hit `localStorage.removeItem('lw-layout-cache')` + refresh):

1. The Workspaces dropdown should show **Calendar** in the system section (between Documents and any user layouts).
2. Click **Calendar** → a new tab opens with title "Calendar".
3. The tab renders the embedded LegalWorkspaceApp with `initialWorkspaceId` set to the new layout's GUID.
4. `WorkspaceGrid` calls LegalWorkspace's `useWorkspaceLayouts`, parses the sectionsJson, and resolves `calendar` from `SECTION_REGISTRY`.
5. The `CalendarSection` component renders.

Standalone test (in a SEPARATE browser tab):
1. Open the LegalWorkspace web resource directly.
2. Switch to **Calendar** via LegalWorkspace's internal workspace dropdown.
3. Verify byte-identical rendering with embedded mode (FR-25 / NFR-10 invariant — see audit §5).

### Step 8 — Deploy

Use the appropriate deploy skill:

- **LegalWorkspace web resource**: `code-page-deploy` skill (or `Deploy-LegalWorkspace.ps1`)
- **SpaarkeAi web resource**: `code-page-deploy` skill (or `Deploy-SpaarkeAi.ps1`) — only needed if SpaarkeAi code changed, which Pattern A does NOT require
- **System layouts seed**: `scripts/Deploy-SystemWorkspaceLayouts.ps1` per environment

---

## 3. Pattern B — New BFF endpoint (if your section needs server-side data)

**STOP and read CLAUDE.md §10 first.** Then load [`.claude/constraints/bff-extensions.md`](../../.claude/constraints/bff-extensions.md). Then:

1. Justify placement in your design doc's **Placement Justification** section (mandatory).
2. Verify publish-size impact stays under the baseline 60 MB compressed.
3. Verify no new HIGH-severity CVE from `dotnet list package --vulnerable --include-transitive`.
4. Endpoint goes in an existing endpoint group if topical (`WorkspaceFileEndpoints`, `WorkspaceAiEndpoints`, ...) or a NEW endpoint group registered in `Program.cs`.
5. Use `RequireAuthorization()` (the `/api/workspace` group already does this).
6. Service goes in `Services/<Area>/` as a concrete type registered Scoped (ADR-010 — no interface unless multiple impls).

The decision criteria from CLAUDE.md §10 boil down to: does this CRUD code need to inject AI types directly? If yes, route through the `Services/Ai/PublicContracts/` facade. Do NOT inject `IOpenAiClient`, `IPlaybookService`, or other AI-internal types.

---

## 4. Pattern B + C — New widget type (Code Page dispatcher OR AI output)

For non-workspace widgets, edit `src/client/shared/Spaarke.AI.Widgets/src/widgets/workspace/register-workspace-widgets.ts`:

```ts
registerWorkspaceWidget(
  'my-new-widget',                        // type string — must match server / dispatcher
  {
    displayName: 'My New Widget',
    category: 'wizard',                   // or 'ai' / 'document' / etc.
    icon: 'CalendarRegular',
    allowMultiple: true,
    defaultOrder: 150,                    // append after existing entries
  },
  () => import('./MyNewWidget').then((m) => ({ default: m.MyNewWidget as WorkspaceWidgetComponent })),
);
```

Then implement `MyNewWidget.tsx` as a `WorkspaceWidgetComponent`:

```tsx
export const MyNewWidget: WorkspaceWidgetComponent<MyData> = ({ data }) => {
  // ... render ...
};
MyNewWidget.displayName = 'MyNewWidget';
```

Pattern B (Code Page dispatcher) — body of MyNewWidget calls `Xrm.Navigation.navigateTo({ pageType: 'webresource', webresourceName: 'sprk_mywizard', ... })` and renders a launch/relaunch button.

Pattern C (AI output) — body of MyNewWidget renders the AI tool's output payload (passed via `data`).

---

## 5. Worked example: Calendar widget (shipped tasks 114 + 115, 2026-05-22)

**Operator request (Round 9, 2026-05-22)**: "Add a Calendar system workspace that surfaces all events + tasks the user has access to (matches standalone EventsPage), with a full Events toolbar, a horizontal month strip, and event detail opens as a modal via `Xrm.Navigation.navigateTo` (NOT `Xrm.App.sidePanes`)."

This shipped end-to-end in two tasks:

- **Task 114** (`53e3323e`) — hoisted EventsPage components to a new shared library `@spaarke/events-components` so the standalone EventsPage code page AND the embedded Calendar widget could share components (architectural unity).
- **Task 115** (`cc83a68a`) — built `CalendarWorkspaceWidget` in the shared lib, wrote the 62-line LegalWorkspace section shim, added the Dataverse-system layout entry, and deployed.

It is the canonical **Pattern D** example.

### 5.1 Scope decision (Step 1)

Operator decided Pattern D (NOT Pattern A) because:
- The Events components are already used by a standalone Code Page (EventsPage / `sprk_eventspage`). The Calendar workspace and the standalone EventsPage should share the components — operator-stated "architectural unity."
- The widget's data access is Xrm.WebApi-only via the shared services in `@spaarke/events-components/services/` — no LegalWorkspace coupling needed.

### 5.2 Hoist the shared library (task 114)

Created `src/client/shared/Spaarke.Events.Components/` with:
- `components/` — CalendarSection (+ CalendarDrawer), GridSection, AssignedToFilter, RecordTypeFilter, StatusFilter, ColumnFilterHeader, ColumnHeaderMenu, ViewSelectorDropdown
- `services/` — Xrm.WebApi-based FetchXML query builders
- `hooks/` — view + filter hooks
- `context/` — EventsPageContext + EventsPageProvider
- `types/` — IEventRecord, IEventDateInfo, filter type unions
- `utils/` — date helpers
- `widgets/` (added in task 115)

Barrel file `src/index.ts` exports the public surface. EventsPage migrated to import from `@spaarke/events-components`; pre-existing standalone import paths in EventsPage were re-wired.

### 5.3 Build the widget (task 115)

`src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` (~1100 LOC) composes:

- Date-range filter row inline: Fluent v9 `<Dropdown>` for date-field selection + two `<Input type="date">` for From/To + collapse caret on the right edge (task 118).
- Single `<CalendarSection layout="horizontal">` with responsive month count (1→5 by viewport breakpoints via ResizeObserver) + external ◀ ▶ arrow navigation. The strip is collapsible — persists in `localStorage["spaarke:calendar:collapsed"]` (task 116).
- Full Events toolbar: `<Toolbar size="small">` with 9 CRUD buttons (New/Delete/Complete/Close/Cancel/On Hold/Archive/Refresh/Calendar) + flex spacer + `<Open24Regular>` icon at right (task 120 moved the Open icon here from the view-selector row).
- View-selector row: `<ViewSelectorDropdown>` with `useViewSelection` defaulted to Active Events.
- `<GridSection>` auto-binding via `EventsPageContext.filters`, with `onRecordsLoaded={handleRecordsLoaded}` (task 120 added this prop) feeding event-date highlighting back into the calendar.

Side-pane behavior: the widget overrides `Xrm.App.sidePanes` and instead uses `Xrm.Navigation.navigateTo({pageType:'entitylist', entityName:'sprk_event'}, {target:2, width:80%, height:80%})` for the Open click target — matches task 111 Documents Expand UX.

### 5.4 Write the LegalWorkspace shim (task 115)

`src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` is 62 lines total. The factory:

```ts
import { CalendarWorkspaceWidget } from "@spaarke/events-components";

export const calendarRegistration: SectionRegistration = {
  id: "calendar",
  label: "Calendar",
  description: "All events + tasks you have access to",
  icon: CalendarLtr24Regular,
  category: "data",
  defaultHeight: "720px",
  factory(_context: SectionFactoryContext): ContentSectionConfig {
    return {
      id: "calendar",
      type: "content",
      title: "Calendar",
      style: { overflow: "hidden" },
      renderContent: () => React.createElement(CalendarWorkspaceWidget),
    };
  },
};
```

Note that `factory` does NOT forward `SectionFactoryContext` props — the widget is self-contained via `Xrm.WebApi` + the shared `@spaarke/events-components` services. This is the defining trait of Pattern D versus Pattern A.

### 5.5 Register in `sectionRegistry.ts` (task 115)

```ts
import { calendarRegistration } from "./sections/calendar.registration";

export const SECTION_REGISTRY: readonly SectionRegistration[] = [
  getStartedRegistration,
  quickSummaryRegistration,
  latestUpdatesRegistration,
  todoRegistration,
  documentsRegistration,
  dailyBriefingRegistration,
  calendarRegistration,                  // task 115
] as const;
```

### 5.6 Add the layout seed + deploy (task 115)

`scripts/system-layouts.json`:

```json
{
  "name": "Calendar",
  "sectionId": "calendar",
  "layoutTemplateId": "single-column",
  "sortOrder": 5,
  "isDefault": false
}
```

Run `pwsh scripts/Deploy-SystemWorkspaceLayouts.ps1 -EnvironmentUrl <url>`.

Deploy LegalWorkspace via `code-page-deploy`. Deploy SpaarkeAi via `Deploy-SpaarkeAi.ps1` so the new `@spaarke/events-components` lib lands in the SpaarkeAi bundle (the lib is a workspace dep — bundle delta was ~+30 KB gzip).

### 5.7 Polish rounds — R10–R13

After the initial Round 9 ship, the widget got 6 follow-up polish rounds based on operator smoke testing:

- **Task 116** (R10) — horizontal-strip responsive month count + external ◀ ▶ arrow navigation + collapsible strip (`spaarke:calendar:collapsed`).
- **Task 118** (R11) — collapse chevron moved to filter row right edge (filter row stays visible when calendar collapsed); CaretUp/Down24 icons distinct from month chevrons; "📅 Calendar" sub-heading removed; ~20px gap between months; event-day highlighting via `dayWithEvents` Griffel class + click-to-filter via new `selectedDate` / `onSelectDate` controlled props on `CalendarSection`; grid Open icon.
- **Task 120** (R13) — event-day highlight bug fix: `GridSection` got optional `onRecordsLoaded` callback; widget added `handleRecordsLoaded` to derive `IEventDateInfo[]` from records using LOCAL date components + dispatch `setEventDates`; `CalendarSection.toIsoDateString` rewritten to use local components (UTC timezone bug fix). Grid spacing: `marginTop: tokens.spacingVerticalL` on grid container. Open icon moved from view-selector row to toolbar row.
- **Task 121** (R13 follow-up) — calendar date fallback policy: removed `sprk_startdate` from the chain. Events without a due date anchor to `sprk_duedate || createdon` only.
- **Task 122** (R13 follow-up #2) — removed `dateState !== "in-range"` exclusion from `showEventsTint` so event-day highlight wins over From/To range visualization; `dayWithEvents` uses solid `colorBrandBackground` + `colorNeutralForegroundOnBrand` ("blue background, white font"); Clear button on filter row (Dismiss24Regular, conditionally rendered when `fromDate || toDate` non-empty); inter-month divider via `borderLeft: 1px solid colorNeutralStroke2` on every non-first horizontal month container.

The polish-round history demonstrates how Pattern D scales: every change landed in `@spaarke/events-components` (shared lib) and the standalone EventsPage benefited from most of them automatically (the timezone fix in particular). The LW section shim was untouched after the initial ship.

### 5.8 What changes were NOT required

- **NO** changes to SpaarkeAi source code (zero across all 9 tasks 114–122) — the existing `workspace` widget type covers every new layout.
- **NO** changes to BFF — the existing `WorkspaceLayoutService.GetLayoutsAsync` already includes ALL `sprk_issystem=true` records via `QueryDataverseSystemLayoutsAsync`. Zero BFF endpoints / services / DI / NuGet / publish-size delta on any of tasks 114–122.
- **NO** changes to `@spaarke/ai-widgets` — `WorkspaceLayoutWidget` is unchanged.
- **NO** new auth wiring — `useAuth` + `authenticatedFetch` + `Xrm.WebApi` already in place.
- **NO** new ADRs — Pattern D extends Pattern A's pipeline with a clean placement choice.

### 5.9 What's still imperfect (honest answer)

- **`CalendarSidePane` (separate web resource)** carries its own legacy copy of `CalendarSection` divergent from `@spaarke/events-components`. Pre-existing follow-up flagged across 114/115/116/118/119/120/121/122 — pending reconciliation.
- **Bulk-action handlers** are partially duplicated between standalone EventsPage's `App.tsx` and `CalendarWorkspaceWidget`. Pending extraction to a shared `useEventsBulkActions` hook.
- **`CalendarDrawer.eventDates: string[]` vs `IEventDateInfo[]` API drift** — the drawer still accepts strings, the widget produces `IEventDateInfo[]`. Pending reconciliation.
- **Xrm.WebApi vs BFF decision criteria** STILL undocumented at the `docs/standards/` level. Task 114 reinforced the unwritten norm (all Events services use Xrm.WebApi); the rationale is not codified. See audit §4.

---

## 6. Cheat sheet — file edit list for the Calendar example

| File | Action | Notes |
|---|---|---|
| `src/solutions/LegalWorkspace/src/components/Calendar/CalendarSection.tsx` | CREATE | Section component |
| `src/solutions/LegalWorkspace/src/components/Calendar/CalendarItemCard.tsx` | CREATE | Card component |
| `src/solutions/LegalWorkspace/src/hooks/useCalendarEvents.ts` | CREATE | Xrm.WebApi fetch hook |
| `src/solutions/LegalWorkspace/src/sections/calendar.registration.ts` | CREATE | `SectionRegistration` factory |
| `src/solutions/LegalWorkspace/src/sectionRegistry.ts` | EDIT | Add `calendarRegistration` import + entry |
| `scripts/system-layouts.json` | EDIT | Add Calendar layout entry |
| (run) `scripts/Deploy-SystemWorkspaceLayouts.ps1` | RUN | Seed Dataverse |
| (deploy) `code-page-deploy` for LegalWorkspace | RUN | Push LegalWorkspace web resource |

Total: 5 new files + 2 edits + 2 commands. NO SpaarkeAi changes. NO BFF changes. NO new packages.

---

## 7. Common pitfalls

| Pitfall | Symptom | Fix |
|---|---|---|
| Forgot to add to `SECTION_REGISTRY` | Tab opens but section doesn't render | Dev-mode console warns about unknown section ID — check `sectionRegistry.ts` |
| Duplicate section ID | Dev-mode `console.error` at load | Pick a unique ID |
| `sortOrder` collision with existing system layouts | Layout appears in unexpected dropdown position | Verify all existing `sortOrder` values (the 5 Dataverse-system layouts use 1..5) and pick the next free integer |
| Tried to set TWO layouts `isDefault: true` | Only one appears as default; other behaves as non-default | Only ONE Dataverse-system layout can be the global default |
| Section needs auth-bearing fetch from BFF | Uses `webApi` instead — works in MDA, but fails when embedded in a future non-Xrm host | Use `authenticatedFetch` from a new hook; pass `bffBaseUrl` from `SectionFactoryContext` |
| Forgot to seed the layout | Workspaces dropdown doesn't show the new entry | Run `Deploy-SystemWorkspaceLayouts.ps1`; verify with the API query in §2.6 |
| Section behaves differently in embedded vs standalone | Section uses LegalWorkspace-internal context (e.g. FeedTodoSyncContext) that doesn't exist if its provider isn't mounted | Only depend on `SectionFactoryContext`; if you need extra context, hoist its provider into `WorkspaceShell`. **Pattern D widgets (Calendar-style) avoid this trap by design** — they live in a shared lib and never reach into LW. |
| **Timezone-asymmetric date keys** (NEW, task 120) | Event-day highlight does not appear on the expected calendar cell for users in positive UTC offsets | Any new component that derives date-only keys from a `Date` must use LOCAL components (`getFullYear/Month/Date`) — never `date.toISOString().split('T')[0]`. The widget side that PRODUCES `IEventDateInfo[]` and the calendar side that CONSUMES `eventDateMap` must use the same key derivation. See task 120's diagnosis (CAUSE D). |
| **Filter-state conflicts with passive indicators** (NEW, task 122) | A passive visual indicator (event-day highlight, today, in-range, etc.) gets clobbered by an active filter-state indicator | When a component has multiple visual states (selected, in-range, has-events, today, other-month, etc.), be deliberate about which states are mutually exclusive vs which can coexist. Task 122 fixed an exclusion bug where `dateState !== "in-range"` suppressed the event-day tint inside an active From/To range. General rule: explicit user actions (selected) win, but passive indicators (has-events) should still be visible whenever possible. |
| **Field-priority chain for date derivation** (NEW, task 121) | The date used for highlighting / sorting doesn't match what the user expects from the UI | When a record-set drives a date-based highlight, use the SAME field-priority chain the user expects from the UI. For Events: `sprk_duedate → createdon` (operator decision task 121 — skipped `sprk_startdate` because events without a due date should anchor to creation date, not scheduled-start, which can mislead about deadline visibility). Document the rationale; operator expectations rarely match the schema's apparent semantic order. |
| **localStorage key drift between sessions** (NEW, task 116) | Per-component collapse state doesn't persist | Use a consistent key scheme. New `spaarke:calendar:collapsed` follows the same `spaarke:<surface>:<feature>` pattern as `spaarke:workspace:pinned-list` + `spaarke:panes:collapsed`. See SPAARKEAI-WORKSPACE-ARCHITECTURE.md §7. |
| **Pattern A vs Pattern D placement choice not justified upfront** (NEW) | Section ships as LW-internal (Pattern A) but later needs to be reused in a non-LW host | Decide Pattern A vs D before writing code (see §1 decision tree). New widgets default to Pattern D unless they genuinely depend on LW-internal context. The 5 original sections are Pattern A because they predate Pattern D — they're not the model for new widgets. |

---

## 8. Related docs

- [`../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md`](../architecture/SPAARKEAI-WORKSPACE-ARCHITECTURE.md) — pipeline reference
- [`../architecture/SPAARKEAI-COMPONENT-MODEL.md`](../architecture/SPAARKEAI-COMPONENT-MODEL.md) — component inventory
- [`../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md`](../architecture/SPAARKEAI-COMPONENTIZATION-AUDIT.md) — coupling + reuse gaps
- [`SHARED-UI-COMPONENTS-GUIDE.md`](./SHARED-UI-COMPONENTS-GUIDE.md) — `@spaarke/ui-components` consumption
- [`PCF-DEPLOYMENT-GUIDE.md`](./PCF-DEPLOYMENT-GUIDE.md) — Code Page deploy reference (the section about `Deploy-SpaarkeAi.ps1` + `Deploy-LegalWorkspace.ps1`)
