# SmartTodoModal callsite audit — pre-retirement baseline

> **Task**: R2-020 — Enumerate every production `SmartTodoModal` callsite.
> **Date**: 2026-07-01
> **Purpose**: Baseline for FR-13 migration (task 021) and FR-14 deletion (task 022). Confirms migration complexity is straightforward.

## Component mount site (single)

`SmartTodoModal` is mounted at exactly ONE location:

- **File**: [`src/solutions/SmartTodo/src/SmartTodoApp.tsx:476-483`](../../../src/solutions/SmartTodo/src/SmartTodoApp.tsx#L476-L483)
- **Trigger**: `{modalTodoId !== null && <SmartTodoModal … />}` (conditional mount)
- **State driver**: `modalTodoId` — set by two paths (below); cleared by the modal's `onClose` callback

## What SETS `modalTodoId` (open triggers)

| # | Trigger | File:line | Path | Migration action |
|---|---|---|---|---|
| **1** | `OPEN_TODOS_EVENT` window event | Listener: [`SmartTodoApp.tsx:233-244`](../../../src/solutions/SmartTodo/src/SmartTodoApp.tsx#L233-L244) | Toolbar Open + card Open icon + card double-click all dispatch this event | Replace listener with direct `Xrm.Navigation.navigateTo` call at 85%×85% |
| **2** | `useLaunchContext()` → `openTodo` action | [`SmartTodoApp.tsx:207-214`](../../../src/solutions/SmartTodo/src/SmartTodoApp.tsx#L207-L214) | URL-param launch: `?action=openTodo&todoId=<guid>` | See note § 3.3 — this path is largely dead in production |

## Dispatchers of `OPEN_TODOS_EVENT`

Every dispatcher will be neutralized as part of task 021 (either the dispatchers themselves migrate to `Xrm.Navigation.navigateTo`, OR the listener migrates and dispatchers become a no-op event fire that the new listener catches and forwards to navigateTo).

| # | Dispatcher | File:line | Trigger | Notes |
|---|---|---|---|---|
| **A** | Toolbar Open button | [`components/Toolbar/ToolbarActions.ts:174-215`](../../../src/solutions/SmartTodo/src/components/Toolbar/ToolbarActions.ts#L174-L215) — `createToolbarActions().open` | User clicks toolbar Open | Dispatches with `firstId` + `selectedIds[]` |
| **B** | Per-card Open (icon + double-click) | [`SmartTodoApp.tsx:253-261`](../../../src/solutions/SmartTodo/src/SmartTodoApp.tsx#L253-L261) — `handleCardOpen` | User clicks card Open icon OR double-clicks card | Dispatches with `firstId = todoId` |
| **C** | Cross-hierarchy Open (SmartToDo component) | [`components/SmartToDo.tsx:284`](../../../src/solutions/SmartTodo/src/components/SmartToDo.tsx#L284) (comment ref only — actual dispatch is at (A) via `createToolbarActions`) | Referenced from SmartToDo's toolbar → routes back to (A) | No separate dispatch |

**Migration insight**: dispatchers (A), (B), (C) all converge on the same listener at `SmartTodoApp.tsx:237` which sets `modalTodoId`. The cleanest migration is to change the LISTENER at line 237 to call `Xrm.Navigation.navigateTo` on the received `firstId` — that single change absorbs every dispatcher without touching the dispatchers themselves.

## Related consumers (impacted by deletion but NOT modal callsites)

These files reference `SmartTodoModal` but are NOT callsites — they define / describe / test the component:

| File | Purpose |
|---|---|
| [`SmartTodo/src/components/Modal/SmartTodoModal.tsx`](../../../src/solutions/SmartTodo/src/components/Modal/SmartTodoModal.tsx) | Component source (~400 LOC) — deleted in task 022 |
| [`SmartTodo/src/components/Modal/buildTodoIframeUrl.ts`](../../../src/solutions/SmartTodo/src/components/Modal/buildTodoIframeUrl.ts) | Pure helper — URL construction for iframe src — deleted with component |
| [`SmartTodo/src/components/Modal/__tests__/buildTodoIframeUrl.test.ts`](../../../src/solutions/SmartTodo/src/components/Modal/__tests__/buildTodoIframeUrl.test.ts) | Unit tests for the helper — deleted with helper |
| [`SmartTodo/src/components/Modal/index.ts`](../../../src/solutions/SmartTodo/src/components/Modal/index.ts) | Barrel export — deleted with folder |
| `SmartTodo/src/components/index.ts` | Re-exports from `./Modal` — remove the re-export line |
| `SmartTodo/README.md` | Doc references to the modal — sharpen per task 023 |
| [`SmartTodo/src/SmartTodoApp.tsx`](../../../src/solutions/SmartTodo/src/SmartTodoApp.tsx) | Imports `SmartTodoModal` + `todosToModalRecords` + mounts + `modalTodoId` state — all removed in task 021/022 |

## Other places that reference `SmartTodoModal` (not mount / not source)

Doc / comment references only — do NOT open the modal:

- [`src/solutions/LegalWorkspace/src/sections/todo.registration.ts:8`](../../../src/solutions/LegalWorkspace/src/sections/todo.registration.ts#L8) — docstring only
- [`src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx:301`](../../../src/client/shared/Spaarke.SmartTodo.Components/src/widgets/SmartTodoWidget/SmartTodoWidget.tsx#L301) — docstring only
- [`src/client/shared/Spaarke.UI.Components/src/services/__tests__/TodoRegardingUpdateBuilder.test.ts:456`](../../../src/client/shared/Spaarke.UI.Components/src/services/__tests__/TodoRegardingUpdateBuilder.test.ts#L456) — comment only (test verifies URL shape, not SmartTodoModal itself)
- [`src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md:25`](../../../src/client/shared/Spaarke.UI.Components/src/components/RecordNavigationModalShell/README.md#L25) — example code in README demonstrating shell usage

**Task 023 will sweep the docstring references** to update terminology (e.g., "SmartTodoModal" → "Layout 1 (`Xrm.Navigation.navigateTo`)").

## LegalWorkspace path — ALREADY uses `Xrm.Navigation.navigateTo` (primary)

Important context: the LegalWorkspace SmartTodo widget's Open button ALREADY uses `Xrm.Navigation.navigateTo` in its primary code path (added earlier by UAT 2026-06-21 round 5 fixes). See [`todo.registration.ts:149-186`](../../../src/solutions/LegalWorkspace/src/sections/todo.registration.ts#L149-L186).

**Delta for R2 FR-20 compliance**: the LegalWorkspace `handleOpenTodo` currently uses **80% × 80%** ([lines 172-173](../../../src/solutions/LegalWorkspace/src/sections/todo.registration.ts#L172-L173)). Task 021 must also update this to **85% × 85%** (FR-20 binding).

The URL-param `openTodo` launch-context path in `todo.registration.ts:187-194` is a "Last-resort fallback: Code-Page-hop (legacy path)" per comment. In production it should almost never fire (Xrm.Navigation.navigateTo is available in every MDA host). Task 021 can either keep it as-is (dead-in-practice) or delete it — recommend delete for surface-area minimalism.

## Migration complexity assessment

**Complexity: LOW** ✅

- **Only ONE component mount** (`SmartTodoApp.tsx:476`)
- **Two open-state triggers** (event listener + launch-context) — both converge on `setModalTodoId`
- **Zero business logic to preserve** — the modal is a pure UI shell (iframe + navigation arrows); the underlying data operations happen inside the iframed OOB form
- **RecordNavigationModalShell preserved** per CLAUDE.md — used by `RichFilePreviewDialog` for Documents preview (Layout 2)
- The `< / >` record-navigation feature is INTENTIONALLY dropped per FR-13 (Layout 1 is single-record; navigating between records requires the user to return to the widget and pick the next one)

**Escalation**: **NOT required**. The migration is straightforward; no callsite has substantive preservation logic.

## Recommended migration approach for task 021

Two viable patterns:

**Pattern A — Listener migration (fewer file touches, recommended)**:
1. Change `SmartTodoApp.tsx:233-244` listener to call `Xrm.Navigation.navigateTo({ pageType: 'entityrecord', entityName: 'sprk_todo', entityId: detail.firstId }, LAYOUT_1_OPTIONS)` — dispatchers keep firing the same event; no listener changes needed
2. Also change `SmartTodoApp.tsx:207-214` `useEffect` for `useLaunchContext` `openTodo` — same treatment
3. Remove the `modalTodoId` state (no longer needed)
4. Update `LegalWorkspace/src/sections/todo.registration.ts:172-173` from 80% → 85%
5. Delete the last-resort Code-Page-hop fallback (`todo.registration.ts:187-194`) — dead path
6. Delete `SmartTodoModal.tsx` + `buildTodoIframeUrl.ts` + tests + barrel + Modal folder (task 022)

**Pattern B — Dispatcher migration (touches more files, cleaner event contract)**:
1. Change each dispatcher (Toolbar + handleCardOpen) to call `navigateTo` directly
2. Retire `OPEN_TODOS_EVENT` entirely (no listener needed)
3. Same LegalWorkspace + deletion steps as Pattern A

Recommend **Pattern A** for R2 — one central migration point at the listener; ships identical behavior; simpler diff for review.

## Acceptance criteria evidence

- [x] `notes/smart-todo-modal-callsites.md` exists (this file)
- [x] Every real SmartTodoModal open callsite listed with file:line + context
- [x] Migration complexity assessment: **LOW** (single mount, two convergent triggers, no business logic to preserve)
- [x] Escalation flagged: **NOT required** — proceed with task 021 using Pattern A
