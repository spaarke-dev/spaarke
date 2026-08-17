# Task 002 — Hoist 13-file rich Kanban subtree into `@spaarke/smart-todo-components` (FR-01)

> Status: **COMPLETE** · FULL rigor / opus / xhigh · 2026-08-15
> Branch `work/smart-todo-r5`. LW-local originals LEFT IN PLACE (task 003 coordinates deletion). NOTHING committed.

## Outcome

The 13-file LegalWorkspace rich Kanban subtree is hoisted host-agnostically into
`src/client/shared/Spaarke.SmartTodo.Components/src/components/SmartToDo/` (+ 2 new package type files).
`tsc --noEmit` = **exit 0**. Zero `src/solutions` reach-in imports. Zero hex/rgb introduced. Scoring
bit-for-bit preserved (hoisted files call the LOCKED `utils/todoScoring.ts`; no re-implementation).

## Where files landed

| Source (LW-local, left in place) | Hoisted to (package) |
|---|---|
| `.../SmartToDo/KanbanCard.tsx` | `src/components/SmartToDo/KanbanCard.tsx` (folder-internal; root alias `SmartToDoKanbanCard`) |
| `.../SmartToDo/KanbanHeader.tsx` | `src/components/SmartToDo/KanbanHeader.tsx` |
| `.../SmartToDo/AddTodoBar.tsx` | `src/components/SmartToDo/AddTodoBar.tsx` |
| `.../SmartToDo/DismissedSection.tsx` | `src/components/SmartToDo/DismissedSection.tsx` |
| `.../SmartToDo/ThresholdSettings.tsx` | `src/components/SmartToDo/ThresholdSettings.tsx` |
| `.../SmartToDo/TodoAISummaryDialog.tsx` | `src/components/SmartToDo/TodoAISummaryDialog.tsx` |
| `.../SmartToDo/TodoDetailPane.tsx` | `src/components/SmartToDo/TodoDetailPane.tsx` |
| `.../SmartToDo/PriorityScoreCard.tsx` | `src/components/SmartToDo/PriorityScoreCard.tsx` |
| `.../SmartToDo/EffortScoreCard.tsx` | `src/components/SmartToDo/EffortScoreCard.tsx` |
| `.../SmartToDo/SmartToDo.tsx` | `src/components/SmartToDo/SmartToDo.tsx` (host-agnostic redesign) |
| `.../SmartToDo/SmartToDoDialog.tsx` | `src/components/SmartToDo/SmartToDoDialog.tsx` |
| `.../SmartToDo/todoScoringTypes.ts` | `src/types/todoScoringTypes.ts` (moved to types/) |
| `.../SmartToDo/index.ts` | `src/components/SmartToDo/index.ts` (folder barrel) |

New package type file: `src/types/entities.ts` (`ITodo`, `PriorityLevel`, `EffortLevel`,
`ITodoKanbanPreferences`, `ITodoMutationResult`). Barrels updated: `src/components/index.ts`,
`src/types/index.ts` (root `src/index.ts` unchanged — it `export *`s the two).

## Per-file LW-local dependency inventory + host-agnostic resolution

Legend: **prop** = lifted to a prop the host supplies · **iface** = optional injected service interface ·
**pure-type** = package-local pure type mirror · **pkg-util** = re-pointed at the package's locked util ·
**barrel** = already a clean `@spaarke/ui-components` package import (kept).

| File | LW-local dep | Resolution |
|---|---|---|
| `todoScoringTypes.ts` | (none) | moved to `types/`, pure-type |
| `PriorityScoreCard` | `./todoScoringTypes` | pure-type → `../../types/todoScoringTypes` |
| `EffortScoreCard` | `./todoScoringTypes` | pure-type → `../../types/todoScoringTypes` |
| `TodoAISummaryDialog` | `./todoScoringTypes`, `./PriorityScoreCard`, `./EffortScoreCard` | pure-type re-home; siblings stay folder-local |
| `AddTodoBar` | (none) | verbatim (Fluent only) |
| `KanbanHeader` | `MicrosoftToDoIcon` (barrel), `./AddTodoBar` | verbatim (both already host-agnostic) |
| `ThresholdSettings` | `useUserPreferences`: `DEFAULT_TODAY/TOMORROW_THRESHOLD`, `ITodoKanbanPreferences` | consts → `../../hooks/useKanbanColumns` (pkg); type → `../../types/entities` (pure-type) |
| `DismissedSection` | `ITodo`, `PriorityLevel`/`EffortLevel` (`types/enums`), `computeDueLabel`/`parseDueDate`/`DueUrgency` (`utils/dueLabelUtils`) | types → `../../types/entities` (pure-type); helpers → `../../utils/todoScoring` (pkg-util, locked) |
| `TodoDetailPane` | `ITodo`, `PriorityLevel`/`EffortLevel`, `computeTodoScore`/`ITodoScoreBreakdown` (`utils/todoScoreUtils`), `computeDueLabel`/`parseDueDate`/`DueUrgency`, `createXrmNavigationService` (barrel) | types → `types/entities`; scoring → `utils/todoScoring` (pkg-util, locked); nav adapter kept as `@spaarke/ui-components` barrel import (sanctioned, ADR-012) |
| `KanbanCard` (rich) | `ITodo`, `computeTodoScore`/`computeDueLabel`/`parseDueDate`/`DueUrgency`, `RecordCardShell`/`CardIcon` (barrel) | types → `types/entities`; scoring → `utils/todoScoring` (locked); shells kept as barrel imports |
| `SmartToDoDialog` | `./SmartToDo`, `IWebApi` (`types/xrm`) | forwards a `smartTodoProps: ISmartToDoProps` bag; `IWebApi` dropped (no longer needed) |
| `SmartToDo` (container) | `useTodoItems`, LW `useKanbanColumns`, `useUserPreferences`, `useFeedTodoSync`, `DataverseService`, `ITodo`, `todoScoreUtils`, `types/enums`, `types/xrm` | see redesign below |

### `SmartToDo.tsx` host-agnostic redesign (orchestrator pre-authorized injection)

The 805-LOC self-fetching container became a ~560-LOC presentational-ish container whose data layer is
injected by the host shim (task 003), mirroring the `SmartTodoWidget` "host brokers coupling" pattern:

| LW-local dependency | Host-agnostic resolution |
|---|---|
| `useTodoItems` (webApi/userId fetch) | **prop**: `items` / `isLoading` / `error` / `onRefetch` |
| `useUserPreferences` (webApi/userId) | **prop**: `preferences` / `onUpdatePreferences` / `prefsLoading` |
| `DataverseService.createTodo/dismissTodo/updateTodoStatus` | **prop callbacks**: `onCreateTodo` / `onDismissTodo` / `onRestoreTodo` → `Promise<ITodoMutationResult>` |
| LW `useKanbanColumns({…, webApi, userId})` | **pkg hook** `../../hooks/useKanbanColumns` + **iface** `dataverseService?: IKanbanDataverseService` (column/pin persistence) |
| `useFeedTodoSync().notifyTodoChange` | **iface** `feedSync?: IFeedSyncBridge` → `feedSync?.notifyChange(...)` |
| `Xrm.Navigation.navigateTo` block (`handleOpenSmartTodo`) | **prop** `onOpenTodo?(todoId?)` (host owns navigation / surface-launch) |
| `ITodo` / `computeTodoScore` / `TodoColumn` | `../../types/entities`, `../../utils/todoScoring` (locked), `../../types/kanban` |

New package types (`types/entities.ts`): `ITodo`, `PriorityLevel`, `EffortLevel`, `ITodoKanbanPreferences`
(pure-type mirrors), plus `ITodoMutationResult` (justified per §11 — conveys success/id/error from an
injected mutation without importing LW's `IResult`; concrete failure otherwise: `handleAdd` cannot choose
rollback-vs-notify).

## Public-surface exports (all resolve from `@spaarke/smart-todo-components`)

Verified by a temp consumer file type-checked against the root barrel (exit 0, then deleted):
`SmartToDo`, `SmartToDoDialog`, `KanbanHeader`, `AddTodoBar`, `DismissedSection`, `ThresholdSettingsPopover`,
`ThresholdSettings`, `TodoDetailPane`, `TodoAISummaryDialog`, `PriorityScoreCard`, `EffortScoreCard`,
`SmartToDoKanbanCard` (rich card alias) + all matching prop types + `ITodo`/`PriorityLevel`/`EffortLevel`/
`ITodoKanbanPreferences`/`ITodoMutationResult` + the `todoScoringTypes` types.

## DEVIATION — `KanbanCard` name collision (the one forced deviation)

The package already root-exports `KanbanCard` / `IKanbanCardProps` (a DIFFERENT flexbox/`IKanbanCardTodo`-generic
card hoisted in R4-102 from the **SmartTodo Code Page**), and that export is **consumed externally** by
`src/solutions/SmartTodo/src/components/SmartToDo.tsx:54`. The LW rich card (`RecordCardShell`/`CardIcon`,
`ITodo`-typed) genuinely collides. Root-clobbering would break the Code Page + is a duplicate-export (TS2308).

**Resolution:** the LW rich card is hoisted **folder-internal** to `components/SmartToDo/KanbanCard.tsx` and
re-exported at the package root under the **alias `SmartToDoKanbanCard` / `ISmartToDoKanbanCardProps`**. `SmartToDo`
composes it via a folder-local relative import, so no behavior/visual change. The bare `KanbanCard` root name
still resolves (to the pre-existing widget card), so the acceptance criterion "KanbanCard resolves from the
public surface" holds; the deviation is only that the bare name is NOT the LW rich card. Full unification of the
two card implementations is out of scope for a "move 13 files" task and a candidate follow-up (§11).

## Verification / gate results

- `npx tsc --noEmit` (package) → **exit 0** (compiles all 13 hoisted files + redesigned SmartToDo + barrels + existing widget/kanban consumers).
- Public-surface temp-consumer type-check → **exit 0** (all 13 symbols + prop types + scoring types import from `@spaarke/smart-todo-components`).
- Grep `src/solutions` / `../../../` in **import** statements of hoisted files → **0** (the 20 textual hits are doc-comment provenance references only).
- Grep hex/`rgb(`/`hsl(` in hoisted files → **0** introduced (ADR-021).
- Grep Fluent v8 (`@fluentui/react`) → **0**.
- Grep scoring weight constants (`W_PRIORITY` etc.) / re-impl in hoisted files → **0**; 4 scoring-consuming files import the locked `../../utils/todoScoring`.
- **adr-check**: CLEAN — ADR-012 ✅ (0 reach-in; injected iface + prop-lift), ADR-021 ✅ (0 color literals, 0 v8; 37 `"Npx"` *dimension* literals are verbatim carryover, flagged informational for the FR-04 polish sweep), scoring ✅ (0 drift).
- **code-review**: PASS — 0 critical. W1 = task-003 must supply the injected props/service (by design, flagged so persistence isn't dropped). Suggestions: prune carried-over dead `handleDismiss`/`dismissingIds` (parity-preserved), `SmartToDo` ~560 LOC (down from 805), `TodoDetailPane` sanctioned-adapter Xrm coupling (latent, not mounted).

## Contract handed to task 003 (shim conversion + deletion)

The LW shim must, for `<SmartToDo>` (and `<SmartToDoDialog smartTodoProps=…>`):
run its `useTodoItems`/`useUserPreferences` and pass `items/isLoading/error/onRefetch/preferences/
onUpdatePreferences/prefsLoading`; provide `onCreateTodo/onDismissTodo/onRestoreTodo` (DataverseService-backed,
returning `ITodoMutationResult`); provide a `dataverseService: IKanbanDataverseService` adapter (else column/pin
moves are local-only); provide `feedSync: IFeedSyncBridge` (FeedTodoSyncContext bridge); provide `onOpenTodo`
(Xrm.Navigation / surface-launch). THEN delete the 13 LW-local originals per POML step 8.
