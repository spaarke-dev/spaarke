# Current Task State

> **Auto-updated by task-execute and context-handoff skills**
> **Last Updated**: 2026-06-03 (PRE-COMPACT CHECKPOINT — Task 033 sign-off received; scope expanded)
> **Protocol**: [Context Recovery](../../docs/procedures/context-recovery.md)

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | 033 — Calendar widget migrate + **NEW: `hostFilters` framework extension** |
| **Step** | 1 of 12 (sign-off received) — about to start 033a (framework extension) |
| **Status** | in-progress (sign-off ✅, no code touched yet) |
| **Next Action** | Resume by reading this file + the sign-off doc + the structural audit summary below. Begin task 033a: add `hostFilters` prop to `<DataGrid>` + plumb through `fetchXmlOverlay.ts`. Then task 033b: migrate CalendarWorkspaceWidget (preserve filter row + toolbar + calendar strip UNCHANGED, swap only `<GridSection ... />` for `<DataGrid configId="e15c2b93-..." hostFilters={applied} ... />`) + delete GridSection (the deferred task 032 cleanup). |

---

## CRITICAL — User's task 033 sign-off answers (2026-06-03)

The widget owner sign-off (POML step 1) was obtained via AskUserQuestion. Answers:

| Question | Answer | Implication |
|---|---|---|
| **Q1** — Widget toolbar vs DataGrid command bar? | **A. Drop widget toolbar; rely on DataGrid's command bar** | Widget loses ~80 lines of toolbar code. Existing widget command bar (New/Delete/Complete/Close/Cancel/OnHold/Archive/Refresh) goes away. Configjson `commandBar.primary` (currently `+ New / Delete / Refresh`) is the new bar. |
| **Q2** — Filter row + filter dispatch? | **Preserve widget filter row AS-IS** + **build `hostFilters` framework extension** for reuse | The widget's 5-dropdown filter row (Event Type / Status / Date Field / From / To + Apply/Clear) STAYS. The dispatch path changes: instead of `setStatusFilter`/`setCalendarFilter`/etc. through EventsPageContext, the `applied` state is piped into `<DataGrid hostFilters={...} />`. This is a **new standard framework feature** per user direction. |
| **Q4** — EventsPageProvider wrapper? | **Required for Q2** answer | Keep the provider since the filter row still dispatches `applied` state internally; remove it only if the filter row goes away (it doesn't). |
| **Scope** | **Proceed now** (rejected pacing question) | User explicitly chose to keep going + told me to checkpoint first then compact. |

**User's verbatim quote on Q2**: "i don't understand 'both' option--we want to preserve the calendar widget as it is --and make available / extendible for other widget scenarios. NOTE this will be standard feature of the dataset grid"

---

## Expanded scope — task 033 has TWO sub-parts

### Task 033a — Framework extension (NEW INFRASTRUCTURE)

**Goal**: Add `hostFilters?` prop to `<DataGrid>` so any host can inject FetchXML conditions at runtime. Becomes a documented, permanent framework feature alongside `parentContext` + `behavior.parentContextFilter`.

**Files to author/modify** (estimated):
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx`
  - Add `hostFilters?: HostFilterValue[]` (or similar) to `DataGridProps`
  - Pass through to the FetchXML composition pipeline
- `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts`
  - Add `overlayHostFilters(fetchXml, hostFilters)` function (sibling to existing `overlayParentContextFilter`)
  - Composition order: `base (savedquery/inline) → parentContext overlay → hostFilters overlay → chip augmentation`
- `src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts` (maybe — if `hostFilters` need a configjson counterpart for declarative use)
- Tests + Storybook story for the new prop
- `docs/architecture/SPAARKE-DATAGRID-FRAMEWORK-ARCHITECTURE.md` — document the 3rd composition layer
- `docs/guides/DATAGRID-FRAMEWORK-CONFIGURATION-GUIDE.md` — add `hostFilters` recipe with worked example

**Open API design decision** (decide in next session):
- Shape: `hostFilters: Array<{ attribute, operator, value }>`? Or richer (allow nested filter groups, AND/OR)?
- Per user direction ("standard feature of the dataset grid"), should this also work declaratively from `behavior.hostFilterTemplate` so a configjson can describe the overlay shape, with the host only injecting values? — Probably yes, but possibly out of R1 scope.
- **R1 minimum**: imperative prop + flat array of conditions (matches the existing `parentContextFilter` shape). Anything richer can be a follow-up.

### Task 033b — Widget migration

**Goal**: CalendarWorkspaceWidget swap `<GridSection ... />` for `<DataGrid configId="e15c2b93-..." hostFilters={...} />`. Preserve the rest.

**Per Q1 decision**: REMOVE the toolbar (L1102-1155). 6 bulk-status callbacks (Delete/Complete/Close/Cancel/OnHold/Archive) become dead code — DELETE them. Toolbar handlers (`onDelete`, `onComplete`, etc.) become dead code.

**Per Q2 decision**: KEEP the filter row (L954-1064). KEEP `pending`/`applied` state + Apply/Clear handlers. CHANGE the dispatch in the effect at L697-743:
- Was: `setCalendarFilter(...)`, `setRecordTypeFilter(...)`, `setStatusFilter(...)` through context
- New: convert `applied` to the `hostFilters` array shape, store in local state, pass to `<DataGrid hostFilters={...} />`

**Per Q4 decision**: KEEP `EventsPageProvider` wrapper (needed by filter row internal context use).

**Per Q3 (automatic)**: `handleRecordsLoaded` at L585-620 stays — wire to `<DataGrid onRecordsLoaded={...} />`.

**Closing step (from task 032 D-032-01)**: Delete `src/client/shared/Spaarke.Events.Components/src/components/GridSection/` directory + remove the 3 lines from `components/index.ts` + 1 line from `types/index.ts` (currently retained pending this widget migration).

**Expected line count**: 1220 → ~700-800 (down from current; preserves filter row + calendar strip + provider, drops toolbar + GridSection).

---

## Pre-existing artifacts from this session

### Sign-off doc (committed e3f0e585? No, written in this session AFTER 032 commit — need to commit)
`projects/spaarke-datagrid-framework-r1/notes/drafts/033-widget-owner-signoff.md` — full sign-off document with the question/answer matrix, the Q1-Q4 options, and the recommended choices.

### Structural audit results (in this conversation, not in any file)

`CalendarWorkspaceWidget.tsx` 1220 lines, mounted by `LegalWorkspace/src/sections/calendar.registration.ts` only (no other consumers).

| Section | Lines | Action |
|---|---|---|
| Imports | L1-130 | Drop `GridSection`, `IEventRecord`; add `DataGrid`, `XrmDataverseClient` |
| Types/constants/styles | L131-525 | Keep mostly; some may be obsolete after toolbar removal |
| `CalendarWorkspaceLayout` (inner) | L529-1175 | Heavy refactor (per Q1+Q2 decisions above) |
| Filter row UI | L954-1064 | KEEP (Q2) |
| Calendar strip | L1067-1099 | KEEP |
| Toolbar | L1102-1155 | DELETE (Q1) |
| GridSection mount | L1162-1172 | REPLACE with `<DataGrid hostFilters={...} />` |
| `CalendarWorkspaceWidget` outer | L1181-1217 | KEEP shell |
| EventsPageProvider wrap | L1190ish | KEEP (Q4) |
| Effect at L697-743 (filter dispatch) | L697-743 | REWIRE: build `hostFilters` array from `applied`, store in local state, pass to DataGrid |
| Bulk-status callbacks (onDelete, onComplete, etc.) | scattered | DELETE (toolbar gone) |

### Task 032 closing step (DEFERRED to this task — task 033b)

Delete:
- `src/client/shared/Spaarke.Events.Components/src/components/GridSection/` (directory)
- 3 export lines in `src/client/shared/Spaarke.Events.Components/src/components/index.ts` (currently retained with comment "GridSection retained pending task 033")
- 1 export line in `src/client/shared/Spaarke.Events.Components/src/types/index.ts`

After deletion, `tsc --noEmit` in `Spaarke.Events.Components` MUST pass.

---

## Files modified this session (already committed)

Phase C UAT (commit `0de55261`, `5a17fbc3`, `b267fb85`, `948d86d4`, `e4fe6b05`):
- DataGrid framework header chevron menu (Clear filter + active-filter glyph)
- Empty-state fix
- Prettier normalization
- Architecture doc + configuration guide

Task 030 (commit `48be0b0a`):
- `sprk_gridconfiguration` record id `e15c2b93-a05f-f111-a825-70a8a59455f4` created in DEV
- `notes/drafts/030-event-configjson.json` + `030-config-record-id.md`

Task 031 (commit `da9262c3`):
- `src/solutions/EventsPage/src/App.tsx` (1868 → 161 lines)
- `src/solutions/EventsPage/src/registerEventHandlers.ts` (NEW, 196 lines, 6 framework command handlers)
- `src/solutions/EventsPage/src/calendarPaneOrchestrator.ts` (NEW, 251 lines)
- `src/solutions/EventsPage/src/xrmHelpers.ts` (NEW, 44 lines)
- `notes/drafts/031-deviations.md` (6 deviations)

Task 032 partial (commit `e3f0e585`):
- DELETED: `Spaarke.Events.Components/src/components/{AssignedToFilter,RecordTypeFilter,StatusFilter}/`
- MODIFIED: barrels in `components/index.ts` + `types/index.ts`
- `notes/drafts/032-consumer-audit.md` (partial-scope decision D-032-01)
- GridSection retained pending task 033

Task 033 sign-off (THIS CHECKPOINT — about to commit):
- `notes/drafts/033-widget-owner-signoff.md` (sign-off doc)
- `current-task.md` (this file)
- `tasks/033-spaarkeai-calendar-widget-migrate.poml` (notes section expanded)

---

## TASK-INDEX status snapshot

| Phase | Status |
|---|---|
| Phase A — Foundation (001-009) | ✅ All complete |
| Phase B — BFF passthrough (010-016) | ✅ 010-016 complete; 017 deploy ⏸ deferred (insights-engine-r2 master merge dependency) |
| Phase C — Matter drill-throughs (020-026) | ✅ All complete |
| Phase D — EventsPage migration | ✅ 030, 031, 032¹; 🔄 **033 in-progress (this checkpoint)**; 🔲 034, 035 |
| Phase E — SemanticSearch (040-042) | 🔲 not started |
| Phase F — Legacy retirement (050-054) | 🔲 not started |
| Wrap-up (090) | 🔲 not started |

---

## Resume protocol when next session opens

1. **READ this file first** (current-task.md). Quick Recovery + critical Q1-Q4 answers + expanded scope.
2. **READ `notes/drafts/033-widget-owner-signoff.md`** for the full sign-off context.
3. **READ `notes/drafts/032-consumer-audit.md`** for the GridSection deletion deferral.
4. **Run `git status`** and `git log --oneline -10` to verify clean state.
5. **Decide task ordering**: I recommend authoring task 033a (framework extension) FIRST as a focused mini-task (~1 hour), commit + push, THEN task 033b (widget migration) (~1 hour), commit + push. Do NOT do them as one mega-commit — split makes review + reversibility easier.
6. **For task 033a**, open these for reading:
   - `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/fetchXmlOverlay.ts` (extend with `overlayHostFilters`)
   - `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/DataGrid.tsx` (add prop + plumb through)
   - `src/client/shared/Spaarke.UI.Components/src/types/DataGridConfiguration.ts` (consider if a declarative counterpart belongs here)
7. **For task 033b**, open `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx` and apply the rewire (preserve filter row + calendar strip + provider; drop toolbar + bulk-status callbacks; swap GridSection for DataGrid; complete the deferred GridSection deletion).
8. **Acceptance criteria**: per POML (sign-off ✅, zero visual regression, build passes, grep for GridSection import in widget = 0).

---

## Open API design notes for task 033a (decide next session)

The `hostFilters` API shape needs thought. Options:

**Option 1 (minimal, recommended for R1)**:
```typescript
interface HostFilterCondition {
  attribute: string;
  operator: 'eq' | 'neq' | 'in' | 'gt' | 'lt' | 'ge' | 'le' | 'like' | 'on-or-after' | 'on-or-before' | 'between';
  value: string | number | string[] | number[];
}

interface DataGridProps {
  hostFilters?: ReadonlyArray<HostFilterCondition>;
  // ...
}
```
- Mirrors `behavior.parentContextFilter` shape extended
- Composes via `overlayHostFilters(fetchXml, conditions)` in `fetchXmlOverlay.ts`
- Conditions go into the savedquery's top-level `<filter type='and'>`

**Option 2 (richer, follow-up project)**:
- Nested AND/OR groups
- Lookup-multi support (the `in` operator handles single-attribute multi-value already)
- Date-range as a single condition (e.g., `operator: 'between', value: ['2026-01-01', '2026-12-31']`)

**Recommendation**: ship Option 1 for R1 task 033a. Calendar widget's filter row maps cleanly:
- Event Type dropdown → `{ attribute: 'sprk_eventtype_ref', operator: 'eq', value: <id> }`
- Status dropdown → `{ attribute: 'sprk_eventstatus', operator: 'in', value: [<value>] }`
- Date field + From/To → `{ attribute: <field>, operator: 'between', value: [<from>, <to>] }`

---

## Important reminders for next session

- **PR #329** is the active PR. All commits on `work/spaarke-datagrid-framework-r1` flow into it.
- **CI status**: last known passing (after Prettier normalization in `b267fb85`). Verify before resuming.
- **DEV environment**: `spaarkedev1.crm.dynamics.com`. `sprk_event` config record `e15c2b93-a05f-f111-a825-70a8a59455f4`.
- **Don't forget the deferred GridSection cleanup**: it lives at the end of task 033b. Do not mark 033 ✅ until GridSection is deleted + both builds pass.

---

*This file is the primary source of truth for active work state. Keep it updated.*
