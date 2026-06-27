# Task 032 — Consumer audit + partial-scope decision

> **Created**: 2026-06-03
> **Task**: 032 — Retire `@spaarke/events-components/{GridSection, AssignedToFilter, RecordTypeFilter, StatusFilter}`
> **Status**: ⚠ partial scope (3 of 4 retired; `GridSection/` deferred to task 033)

---

## Audit grep results

Repo-wide search for `import.*\b(GridSection|AssignedToFilter|RecordTypeFilter|StatusFilter)\b` across `src/**/*.{ts,tsx}`:

### Hits

| File | Symbol | Verdict |
|---|---|---|
| `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx:105` | `GridSection` | ⚠ **BLOCKER** — CalendarWorkspaceWidget (the SpaarkeAi Calendar widget task 033 migrates) still consumes GridSection |
| `src/client/shared/Spaarke.Events.Components/src/widgets/CalendarWorkspaceWidget/CalendarWorkspaceWidget.tsx:106` | `IEventRecord` (type from GridSection) | ⚠ Same blocker |
| `src/client/pcf/UniversalQuickCreate/control/components/RecordTypeSelector.tsx:25` | `RecordTypeFilter` | ✅ False positive — local type alias `type RecordTypeFilter = 'all' \| 'sprk_matter' \| 'sprk_project' \| 'sprk_invoice'` in `useRecordMatch.ts:16`, NOT the events-components component |
| `src/server/api/Sprk.Bff.Api/Models/Email/BatchProcessModels.cs:59,163,210` | `StatusFilter` / `EmailStatusFilter` | ✅ False positive — C# enum, different module |

### Internal re-exports affected by partial retirement

| Re-export path | Type | After partial 032 |
|---|---|---|
| `Spaarke.Events.Components/src/types/index.ts:20` | `export type { IUserOption } from '../components/AssignedToFilter/AssignedToFilter'` | DELETE — `IUserOption` has zero external consumers |
| `Spaarke.Events.Components/src/types/index.ts:24` | `export type { IStatusOption } from '../components/StatusFilter/StatusFilter'` | DELETE — CalendarWorkspaceWidget has its own inline `IStatusOption` at L146-151; not affected |
| `Spaarke.Events.Components/src/components/index.ts:25,31` | barrel re-exports of `AssignedToFilterProps`, `IUserOption`, `StatusFilterProps`, `IStatusOption` | DELETE |
| `Spaarke.Events.Components/src/context/EventsPageContext.tsx:463,476` | `useAssignedToFilter` + `useStatusFilter` hooks | KEEP — these are hooks defined INSIDE EventsPageContext.tsx (not in the retiring component dirs). The names are coincidental; they live in the context layer per design. |

---

## Decision: partial scope (D-032-01)

**POML acceptance criterion** (line 79): "Given grep for retired component imports, when run post-deletion, then zero matches."

**Reality**: `CalendarWorkspaceWidget` will continue to import `GridSection` until **task 033** (Calendar widget migrate to new DataGrid) completes. That task IS the consumer migration.

**Partial scope shipped in this task**:

| Directory | Action this task |
|---|---|
| `components/AssignedToFilter/` | ✅ DELETE |
| `components/RecordTypeFilter/` | ✅ DELETE |
| `components/StatusFilter/` | ✅ DELETE |
| `components/GridSection/` | ⏭ **DEFER to task 033 closure** |

**Why this is the right call**:
1. The 3 retired directories have **zero external consumers** (only internal type re-exports + EventsPage `App.tsx` which task 031 already cleaned up).
2. The CalendarWorkspaceWidget GridSection dependency is the precise dependency that task 033 (Calendar widget migration) exists to remove.
3. POML notes line 85: "Parallels task 033 (SpaarkeAi Calendar widget migrate) — they touch different surfaces." This statement is **factually wrong** — both tasks modify `Spaarke.Events.Components`. The parallel-safe declaration in the POML metadata (`parallel-group: D1`, `parallel-safe: true`) should be corrected.
4. Deferring `GridSection/` deletion keeps the build green between tasks 032 and 033, and prevents a broken interim state.

**Per task 033 acceptance criteria**: when CalendarWorkspaceWidget migrates to `<DataGrid configId=… />`, the `GridSection` import disappears and the directory becomes safe to delete. Task 033 will then complete the directory deletion as its closing step.

---

## Scope correction recorded for task 033

Task 033's closing step will gain an additional bullet:

> After CalendarWorkspaceWidget migrates to `<DataGrid />`, delete `src/client/shared/Spaarke.Events.Components/src/components/GridSection/` and remove its barrel re-exports from `src/index.ts`. This completes the Phase D retirement of legacy events-components grid primitives.

---

## Files modified in this task

| File | Change |
|---|---|
| `src/client/shared/Spaarke.Events.Components/src/components/AssignedToFilter/` | DELETED (directory) |
| `src/client/shared/Spaarke.Events.Components/src/components/RecordTypeFilter/` | DELETED (directory) |
| `src/client/shared/Spaarke.Events.Components/src/components/StatusFilter/` | DELETED (directory) |
| `src/client/shared/Spaarke.Events.Components/src/components/index.ts` | Removed re-exports for AssignedToFilter, RecordTypeFilter, StatusFilter |
| `src/client/shared/Spaarke.Events.Components/src/types/index.ts` | Removed `IUserOption`, `IStatusOption` re-exports |
| `src/client/shared/Spaarke.Events.Components/src/index.ts` | Verified — top-level barrel re-exports `components/*` and `types/*` transitively; no direct refs to retired components |

---

## Acceptance criteria status

| Criterion | Status |
|---|---|
| Grep for retired component imports = 0 matches | ⚠ **Partial** — 3 components: 0 matches. `GridSection` still has 2 matches in CalendarWorkspaceWidget. Closure deferred to task 033. |
| `npm run build` in `@spaarke/events-components` passes | ✅ (to verify) |
| `npm run build` in `src/solutions/EventsPage/` passes | ✅ (to verify) |
