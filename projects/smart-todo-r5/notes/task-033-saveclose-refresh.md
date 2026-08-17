# Task 033 — Save & Close dismisses inner modal + refreshes the kanban (FR-14)

> Spec ref: FR-14 (F-8, owner-decided 2026-08-14 Option 1 — keep OOB form).
> Gate: 032. Rigor FULL · model-tier opus · effort xhigh · prescriptive.

## Stale-framing correction (cite in PR)

The spec/design describe this as "extending the **SmartTodoModal interceptor**" /
"parent-side interceptor." **That component does not exist.** `SmartTodoModal.tsx`
and its iframe-hosted `main.aspx` approach were fully retired 2026-07-01 by
`ai-spaarke-ai-workspace-UI-r2` task 022. There is no interceptor object to
extend and — per ADR-050 Path A — this task does **not** intercept
`Xrm.Page.ui.close` (that API is form-context-scoped; this task never needs
in-form access). The load-bearing signal is the **outer `navigateTo` Promise
resolving**, which is standard `target:2` dialog behavior already exploited by
`navigateToEntityRecordSurfaceAsync`.

## The save-vs-cancel signal (finding — reused from task 031 step 2, re-confirmed)

Microsoft Learn `Xrm.Navigation.navigateTo` "Return value": an object is passed
**only** when `pageType = entityRecord` **and the form was opened in CREATE
mode**. Consequences, already baked into the shared launcher's doc comment
(`wizardLaunchers.ts`) and its unit tests:

| Mode | Resolve value on close | Save-vs-cancel distinguishable? |
|---|---|---|
| **CREATE** (`entityId` absent) | `{ savedEntityReference:[{id,…}] }` on save; empty/undefined on cancel | **Yes** — gate refetch on `savedEntityReference` |
| **OPEN** (`entityId` present) | plain `{ launched:true }` whether saved or cancelled | **No** — refetch UNCONDITIONALLY on close |

`NavigateToOutcome` therefore is: CREATE → `savedEntityReference` present on save,
`cancelled:true` otherwise; OPEN → `{ launched:true }` (no flags); either → a
rejected/erroring promise maps to `{ launched:true, cancelled:true }`.

## Decision — OPEN refetches unconditionally on close; CREATE gates on save

Per POML step 4: because OPEN cannot distinguish save from cancel via the resolve
value, both Code Page and widget OPEN paths **refetch unconditionally** when the
dialog closes (`outcome.launched === true`). A redundant refetch on cancel is the
explicitly-tolerated trade-off; a **missing** refetch on save is the failure to
avoid (FR-14). CREATE does **not** need this fallback — it has a reliable
`savedEntityReference` signal, so it refetches **only** on save (no jarring no-op
reload on cancel — the negative acceptance criterion).

**Read-after-write race — checked, not triggered.** The `navigateTo` promise
resolves *after* Save & Close commits the server write, so the subsequent OData
refetch reads committed data. No evidence of the resolve-before-write race the
escalation trigger names for the standard `target:2` entityrecord flow →
**not escalated**.

## Per-surface wiring (3 paths)

1. **Code Page CREATE — VERIFY-ONLY (task 030, unchanged).**
   `SmartTodoApp.handleNewTask` → `launchNewTaskCreateForm(launchContext, handleRefresh)`
   → `if (outcome.savedEntityReference) onSaved()`. Confirmed correct; **not**
   double-wired.

2. **Code Page OPEN — NEW.** The pre-033 module-scope `openSprkTodoAsLayout1`
   void wrapper was extracted into **`src/solutions/SmartTodo/src/services/openTodoLauncher.ts`**
   → `launchOpenTodoForm(todoId, onClose?)`, mirroring the CREATE path's
   `newTaskLauncher.ts`. Extraction rationale (§11): the wrapper now has real
   branching (launched-gate + unconditional on-close refetch) worth unit-testing
   in isolation, and testing it in-place would require pulling `SmartTodoApp.tsx`'s
   full import graph (Header, SmartToDo, `@spaarke/auth`, …) into the test — the
   exact reason `newTaskLauncher.ts` already exists. On `launched:true` it invokes
   `onClose`; on `launched:false` it emits the preserved "Xrm unavailable" warn and
   skips `onClose`. Both component-scope callers — the `useLaunchContext` openTodo
   effect and the `OPEN_TODOS_EVENT` listener (which also serves card
   double-click / per-card Open via `handleCardOpen`'s dispatch) — now call
   `launchOpenTodoForm(id, handleRefresh)`. `handleRefresh` (innerRefetchRef ??
   TodoContext.refetch) is stable; the listener lists it in its deps (harmless
   re-subscribe), the mount-once openTodo effect closure-captures it.

3. **LegalWorkspace widget OPEN — NEW.** `FeedSyncBridgeHost.handleOpenTodo`
   (`todo.registration.ts`), todoId-present branch: on `outcome.launched === true`
   it now calls **`refetchRef.current?.()`** (the widget's authoritative
   onRefetchReady-captured refetch — re-queries the list, reflects edits *and*
   completions) **and `feedSync.notifyChange(todoId, true)`** (cross-block
   FeedTodoSyncContext fan-out). `feedSync` added to the callback deps.
   The `openForm` page-nav fallback (`launched === false`) and the no-selection
   `onOpenWizard` branch are **untouched**.

   **Why `isActive: true` (not `false`) for the cross-block notify.** Subscribers
   (`useTodoItems.ts`) treat `isActive:false` as "**REMOVE** this todo now" and
   `isActive:true` as "reconcile-by-refetch, no-op if already listed." Since we
   cannot know from the OPEN resolve whether the user *completed* the todo,
   passing `false` could wrongly drop a still-active row from sibling blocks —
   a data regression. `true` at worst leaves a just-completed row visible in a
   sibling until its own next refresh (tolerable staleness, never loss). This
   widget's own list is refreshed authoritatively by `refetchRef` regardless.

## Files changed

| File | Change |
|---|---|
| `src/solutions/SmartTodo/src/SmartTodoApp.tsx` | Import + call `launchOpenTodoForm(id, handleRefresh)` from both OPEN triggers; removed the inline `openSprkTodoAsLayout1` wrapper + its now-unused `navigateToEntityRecordSurfaceAsync` import; listener dep `[handleRefresh]`; doc-comment refresh |
| `src/solutions/SmartTodo/src/services/openTodoLauncher.ts` | **NEW** — `launchOpenTodoForm(todoId, onClose?)`; unconditional on-close refetch |
| `src/solutions/SmartTodo/src/services/__tests__/openTodoLauncher.test.ts` | **NEW** — 5 jest tests (OPEN args; refetch fires on close incl. error-resolve; NOT on non-host; onClose-omitted safe) |
| `src/solutions/LegalWorkspace/src/sections/todo.registration.ts` | widget OPEN branch: `refetchRef.current?.()` + `feedSync.notifyChange(todoId,true)` on close; deps `[ctx, feedSync]` |

**`wizardLaunchers.ts` — NO change.** Tasks 030/031/032 already gave the shared
launcher the correct create-vs-open outcome shape; task 033 is pure call-site
wiring. The OPEN `{launched:true}` contract is already covered by
`wizardLaunchers.test.ts`.

## Verification

- **tsc `--noEmit`** (git-stash baseline compare): Spaarke.UI.Components 3=3,
  SmartTodo 40=40, LegalWorkspace 226=226 → **ZERO new errors** in every package;
  none of the preexisting errors reference the changed files. (Preexisting errors
  are missing peer deps — `@spaarke/auth`, `@spaarke/sdap-client`, `@spaarke/ai-widgets`,
  `ComponentFramework` namespace — and orphaned/unrunnable `.test.tsx` files.)
- **jest** — SmartTodo: **8 suites / 133 passed** (incl. new `openTodoLauncher` 5/5).
  Spaarke.UI.Components `wizardLaunchers`: **11/11**. (Full shared-lib suite has 35
  preexisting failed suites from peer-dep gaps — package **untouched** by this task,
  confirmed via `git status`.)
- **hex/rgb/`1px`**: zero in all 4 changed/new files (ADR-021).
- **LegalWorkspace has no jest runner** (no `test` script, no jest dep, no config;
  the 3 `.test.tsx` files present are orphaned/unrunnable). Its widget call-site
  logic is covered by **inspection + tsc** per the task constraint.

## Operator UAT script (POML step 5 — no live MDA in the agent session)

Run in a model-driven app with the SmartTodo Code Page + LegalWorkspace widget
deployed (after tasks 014/025/032/035 deploys), under BOTH `webLightTheme` and
`webDarkTheme`:

**A. Code Page — CREATE + Save & Close**
1. Open the SmartTodo Code Page. Click **+ New Task**. The OOB `sprk_todo` main
   form opens full-cover (100%×100%).
2. Fill required fields → **Save & Close**.
3. **Expect:** dialog dismisses; the new To Do appears in the kanban **without a
   manual reload**.

**B. Code Page — OPEN + edit + Save & Close**
1. Open an existing To Do (toolbar **Open** with one selected, card double-click,
   or per-card Open icon). Change a field → **Save & Close**.
2. **Expect:** the kanban reflects the change **without a manual reload**.

**C. Code Page — OPEN + Cancel/X**
1. Open an existing To Do, make no changes, close via **X/Cancel**.
2. **Expect:** no data change; kanban stays consistent. (A brief redundant refetch
   is expected + acceptable — OPEN cannot distinguish cancel from save.)

**D. LegalWorkspace widget — OPEN + edit + Save & Close**
1. In the LegalWorkspace corporate workspace, in the Smart To Do block, click a
   card's Open. Edit a field → **Save & Close**.
2. **Expect:** the widget's list reflects the change without reload; any sibling
   block (ActivityFeed / second SmartTodo) showing the same todo stays consistent
   (may lag one refresh on a *completion* — acceptable).

**E. Widget — no-selection Open (regression guard)**
1. Trigger the widget Open affordance with **no** card selected.
2. **Expect:** the full SmartTodo Code Page opens at its default kanban view (the
   `onOpenWizard` branch) — unchanged by this task.

**F. Dark-mode (ADR-021):** repeat A/B under `webDarkTheme` — no visual regression
in Spaarke-authored chrome.
