# Task 033b — Deviations + closure notes

> **Date**: 2026-06-03
> **Task**: 033b — CalendarWorkspaceWidget migration to `<DataGrid hostFilters={…}/>` + deferred GridSection deletion
> **Parent**: task 033 (SpaarkeAi Calendar widget migrate, FR-MIG-05) — split into 033a (framework extension, shipped commit `cbe393d4`) + 033b (this widget migration).

---

## What shipped

### Widget rewrite

`src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` — 1,220 → **887 lines** (~27% reduction).

Per the task 033 sign-off Q&A matrix (`notes/drafts/033-widget-owner-signoff.md`):

| Question | Answer | Implementation |
|---|---|---|
| Q1 | Drop widget toolbar; rely on DataGrid command bar | Toolbar JSX (L1102-1155 in old file) removed entirely. 6 bulk-status callbacks + helpers (`bulkStatusUpdate`, `bulkArchive`, `confirmDialog`, `selectedIds` state, `hasSelection`) deleted. DataGrid's own command bar (configjson `commandBar.primary = +New / Delete / Refresh` per task 030) handles these natively. |
| Q2 | Preserve widget filter row AS-IS + build framework extension | Filter row (Event Type / Status / Date Field / From / To + Apply/Clear) preserved verbatim. `pending`/`applied` state model + `filterSetsEqual` + `filterSetIsEmpty` helpers preserved. The dispatch path changed: instead of `setCalendarFilter`/`setRecordTypeFilter`/`setStatusFilter` through `EventsPageContext`, the `applied` state + `selectedDate` build a `HostFilterCondition[]` via `useMemo`, passed to `<DataGrid hostFilters={…}/>` (task 033a API). |
| Q3 | Calendar event-date counting — automatic | `handleRecordsLoaded` wired to `<DataGrid onRecordsLoaded={…}/>`. Implementation logic (sprk_duedate/createdon fallback + per-date count map + auto-anchor-to-earliest) preserved verbatim; only the input type changed from `IEventRecord[]` to `ReadonlyArray<Record<string, unknown>>`. |
| Q4 | Keep `EventsPageProvider` wrapper | Provider retained at the outer widget. `eventDates`/`setEventDates`/`refreshTrigger`/`openEvent` still flow through context (drives the calendar strip dot indicators + the row-click → modal handler). |

### hostFilters mapping (the heart of the migration)

```ts
const hostFilters = React.useMemo<HostFilterCondition[]>(() => {
  const conditions: HostFilterCondition[] = [];

  if (applied.eventTypeId) {
    conditions.push({ attribute: 'sprk_eventtype_ref', operator: 'eq', value: applied.eventTypeId });
  }
  if (applied.eventStatusValue !== null) {
    conditions.push({ attribute: 'sprk_eventstatus', operator: 'eq', value: applied.eventStatusValue });
  }
  if (selectedDate) {
    // Day-click bypasses the filter row range. Original task 130 used a
    // divergence effect to clear selectedDate when range fired; here we
    // express priority directly so there's one source of truth.
    const iso = toIsoDate(selectedDate);
    const effectiveDateField =
      applied.dateField && applied.dateField !== DATE_FIELD_NONE ? applied.dateField : 'sprk_duedate';
    conditions.push({ attribute: effectiveDateField, operator: 'on', value: iso });
  } else if (applied.dateField && applied.dateField !== DATE_FIELD_NONE) {
    if (applied.fromDate && applied.toDate) {
      conditions.push({ attribute: applied.dateField, operator: 'between', value: [applied.fromDate, applied.toDate] });
    } else if (applied.fromDate) {
      conditions.push({ attribute: applied.dateField, operator: 'on-or-after', value: applied.fromDate });
    } else if (applied.toDate) {
      conditions.push({ attribute: applied.dateField, operator: 'on-or-before', value: applied.toDate });
    }
  }
  return conditions;
}, [applied, selectedDate]);
```

### DataGrid mount

```tsx
<DataGrid
  key={refreshTrigger}                                  // existing refreshTrigger-driven remount preserved
  configId={EVENT_CONFIG_ID}                            // e15c2b93-… (task 030 record; same as EventsPage)
  dataverseClient={dataverseClientRef.current}          // stable XrmDataverseClient instance
  hostFilters={hostFilters}                             // task 033a — third composition layer
  onRecordsLoaded={handleRecordsLoaded}                 // calendar dot indicator derivation (Q3)
  onRecordOpen={onRecordOpen}                           // bridges to openEvent → modal navigation
/>
```

### Deferred GridSection deletion (closes task 032 D-032-01)

Per `notes/drafts/032-consumer-audit.md`, GridSection was deferred until the widget migration. Now executed:

| Deleted | Reason |
|---|---|
| `src/client/shared/Spaarke.Events.Components/src/components/GridSection/GridSection.tsx` | Sole legacy implementation; replaced by the framework `<DataGrid />`. |
| `src/client/shared/Spaarke.Events.Components/src/components/GridSection/index.ts` | Barrel for the deleted directory. |
| 2 lines in `src/client/shared/Spaarke.Events.Components/src/components/index.ts` | `GridSection` + `IEventRecord` re-exports. Replaced with a "RETIRED in task 033b" comment block alongside the task-032 retirement block. |
| 1 line in `src/client/shared/Spaarke.Events.Components/src/types/index.ts` | `IEventRecord` re-export. Replaced with a "RETIRED in task 033b" comment. |

Repo-wide grep for `from '…/GridSection'` returns ZERO matches outside the deleted directory itself (which is gone).

---

## Cross-package import — design note

The widget imports from `@spaarke/ui-components` SOURCE via deep relative paths:

```ts
import { DataGrid, type HostFilterCondition } from '../../../../Spaarke.UI.Components/src/components/DataGrid';
import { XrmDataverseClient } from '../../../../Spaarke.UI.Components/src/services/XrmDataverseClient';
```

**Why deep paths instead of the package root**: `Spaarke.UI.Components/src/index.ts` re-exports the whole library — including PCF-framework-dependent code (`UniversalDatasetGrid`, `useDatasetMode`, `SprkChat` infrastructure) that requires `ComponentFramework` types not installed in `Spaarke.Events.Components`'s `node_modules`. Importing the root index would trip ~40 stale type errors during `tsc --noEmit` from cross-package source resolution. Deep imports skip the heavy index and pull only the DataGrid surface, which is React-16-safe per ADR-022 and depends on no PCF types.

Alternative considered + rejected (for now): add `@spaarke/ui-components` as a workspace dep to `Spaarke.Events.Components/package.json` + a tsconfig `paths` mapping. That would be cleaner long-term, but creates a new package-manifest contract that needs review and would block this task on a dependency-graph audit. The deep-path import is functionally equivalent for tsc/Vite resolution and ships today. Promotion to a proper workspace dep is a follow-up cleanup.

---

## Behavioral differences vs. the legacy widget

| Behavior | Old | New |
|---|---|---|
| Toolbar (New/Delete/Complete/Close/Cancel/OnHold/Archive/Refresh/Calendar/Open) | Widget owned | DataGrid command bar (+New/Delete/Refresh only) per Q1 |
| Bulk status changes (Complete/Close/Cancel/OnHold/Archive) | Inline widget callbacks | Not exposed in the widget; will appear automatically if the sprk_event configjson commandBar is extended with custom handlers (e.g. via `registerCommandHandler` like the standalone EventsPage does in `registerEventHandlers.ts`) |
| Row-click event open | `GridSection.onRowClick → openEvent → handleOpenEvent → Xrm.Navigation.navigateTo modal` | `DataGrid.onRecordOpen → openEvent → handleOpenEvent → Xrm.Navigation.navigateTo modal` (same modal navigation; new contract) |
| Calendar dot indicators (per-date event counts) | `GridSection.onRecordsLoaded → handleRecordsLoaded → setEventDates` | `DataGrid.onRecordsLoaded → handleRecordsLoaded → setEventDates` (same derivation, same context flow) |
| Day-cell click → grid filter | `setCalendarFilter` via context → `GridSection` buildDateFilter OR-clause across `dateFields` | `setSelectedDate` → `hostFilters` useMemo emits one `{ attribute: …, operator: 'on', value: iso }` condition → DataGrid re-fetches |
| Filter divergence effect (L871-887 in old file) | Cleared `selectedDate` when context calendarFilter changed | Removed — `selectedDate` is now the only source of truth; no more dispatch loop to detect divergence from |
| Filter row | 5 dropdowns + Apply/Clear + chevron | UNCHANGED (Q2 sign-off) |
| Calendar strip | Responsive 1-5 month horizontal strip + month nav + collapse | UNCHANGED |
| `EventsPageProvider` wrapper | Provides context to inner layout | UNCHANGED (Q4 sign-off) |

Acceptance criteria from POML 033:
- ✅ Widget owner sign-off documented (`033-widget-owner-signoff.md`).
- ⏳ Pre/post screenshots → for UAT at task 035 (visual regression confirmation requires deploying + opening in DEV).
- ✅ `npm run build` (Vite) for EventsPage solution passes (1,258 KB single-file HTML).
- ✅ `tsc --noEmit` for both `Spaarke.Events.Components` AND `Spaarke.UI.Components` PASS.
- ✅ Repo-wide grep for `from '…/GridSection'` returns ZERO matches.

---

## Deviations from POML

The POML's literal steps 3-7 (find Calendar widget, identify GridSection import, replace, preserve chrome, build, dev test) all executed as-described. The expanded scope (033a/033b split, hostFilters as a permanent framework feature) is documented in:

- `tasks/033-spaarkeai-calendar-widget-migrate.poml` (notes section)
- `current-task.md` (resume protocol)
- `033a-deviations.md` (framework extension)
- This file (widget migration + GridSection deletion closure)

**Filename mismatch** between POML and code: the POML references `src/client/shared/@spaarke/legal-workspace/src/widgets/Calendar/Calendar.tsx`. The actual widget lives at `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx`. The POML reference is stale (the widget was hoisted into events-components long before this project). No action needed — current-task.md called this out correctly.

**Pattern A (clean break — drop widget toolbar, drop filter row, drop provider) was rejected by the user** in favor of preserving the filter row + provider and building a framework extension instead. This grew the framework surface (task 033a) but kept the widget UX consistent for current users. Net code reduction is smaller than the "Pattern A" estimate (~700-800 lines) but matches user direction.

---

## What was intentionally NOT done

- **No Storybook story for the widget** — `Spaarke.Events.Components` has no Storybook infrastructure. Adding one would be inconsistent with the package's posture.
- **No new tests** — same reason as 033a; package has zero existing `*.test.ts` files. Test coverage decisions belong to project wrap-up (task 090).
- **No PR description Placement Justification** — 033b modifies a shared library, NOT `Sprk.Bff.Api`. BFF binding governance (CLAUDE.md §10) does not apply.
- **No upgrade of the cross-package import to a workspace dep** — see "Cross-package import — design note" above. Deferred to a follow-up cleanup task.
- **No bulk-status command handlers registered for the widget** — Q1 explicitly accepted the loss of Complete/Close/Cancel/OnHold/Archive. If they need to come back, the sprk_event configjson's `commandBar.primary` can add custom-action entries that resolve against `registerCommandHandler` (the standalone EventsPage's `registerEventHandlers.ts` registers them already; the Calendar widget would need to either consume that module OR have its own registration site).

---

## Build verification (final)

| Build | Result |
|---|---|
| `tsc --noEmit` in `Spaarke.UI.Components/` | ✅ EXIT 0 (after task 033a; unchanged in 033b) |
| `tsc --noEmit` in `Spaarke.Events.Components/` | ✅ EXIT 0 (with deep imports; would error with package-root imports due to cross-package PCF-types pollution — documented above) |
| `npm run build` (Vite) in `src/solutions/EventsPage/` | ✅ EXIT 0 (1,258 KB `dist/index.html` produced) |

---

## Closing — task 033 complete

Combined task 033 (across 033a + 033b) graduation criteria, per POML acceptance-criteria + the expanded-scope user direction:

- ✅ Widget owner sign-off documented (UQ-06 closed).
- ✅ `npm run build` passes.
- ✅ Grep for `GridSection` import in widget code returns ZERO matches.
- ✅ Framework extension shipped + documented (task 033a).
- ✅ Widget migration shipped (task 033b).
- ✅ Deferred task 032 D-032-01 closing step completed (GridSection deleted; barrels updated).
- ⏳ Visual diff (pre/post screenshots) deferred to task 035 UAT.

Next: task 034 (Phase D deploy) + task 035 (Phase D UAT — including the visual regression check that the legacy widget vs. new widget render equivalently for the preserved chrome).
