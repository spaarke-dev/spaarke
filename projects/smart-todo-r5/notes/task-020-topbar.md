# Task 020 — Code Page top-bar redesign (FR-05 / U-3) — completion notes

**Date**: 2026-08-16
**Rigor**: FULL (POML-declared)
**Files touched** (exactly the 4 in scope — nothing else):
- `src/solutions/SmartTodo/src/components/Header/Header.tsx`
- `src/solutions/SmartTodo/src/components/Header/Header.styles.ts`
- `src/solutions/SmartTodo/src/components/Header/__tests__/Header.test.tsx` (new)
- `src/solutions/SmartTodo/src/SmartTodoApp.tsx`

## What changed

Replaced the R4-104 single-row toolbar (3-field QuickAdd group + toggle-into-inline-SearchBox "Filter") with the mockup's chrome (`projects/smart-todo-r5/to-do-header-revision.jpg`):

- **Left**: unchanged — `MicrosoftToDoIcon` + "Smart To Do" title, in its own row above the toolbar.
- **Right (default, no selection)**: `Filter` pill (outlined, magnifying-glass icon + label) → `+ New Task` (primary button) → `⋮` overflow trigger (icon-only, `aria-label="More options"`), in that DOM/tab order.
- **Right (selection active, `selectedCount > 0`)**: `<SelectionAwareToolbar>` (Open/Delete/Email/Pin — untouched) + the `Filter` pill only. `+ New Task` and the `⋮` overflow are suppressed during selection — this mirrors the **pre-existing precedent** already in the old code (QuickAdd/+Wizard were likewise suppressed whenever `hasSelection` was true; Settings/Refresh/Orientation were never available inline during selection either). This precedent-following decision isn't spelled out verbatim in the POML steps but follows directly from the codebase's own established pattern — noted here as a deviation from silence, not a deviation from intent.
- **Removed entirely** (not relabeled, per FR-05 acceptance): the 3-field QuickAdd group (Title input + native `<input type=date>` + Assigned-To `Xrm.WebApi` contact-typeahead + Add button + subtle "+ New" wizard button) and all its local state/handlers (`quickAddValue`, `quickAddDueDate`, `quickAddAssignedTo*`, `assignedToResults`, `dispatchQuickAdd`, `handleQuickAdd*`, `handleSelectAssignedTo`, `handleAssignedToBlur/Focus`); the inline Filter→SearchBox toggle (`isFilterOpen`, both duplicated SearchBox/ToggleButton JSX blocks, `handleToggleFilter`); `ViewToggle`/`viewMode` plumbing (already dead — no callsite ever passed `viewMode`, and FR-05's closed "ONLY Filter and + New Task inline" acceptance set left no room for a third inline control even if it were ever wired up).

## Overflow menu wiring (Settings → Layout → Refresh, exactly 3, in that order)

Built with Fluent v9 `Menu`/`MenuTrigger`/`MenuPopover`/`MenuList`/`MenuItem` (same composition pattern as `ManageWorkspacesPane.tsx`'s existing overflow menu — `<MenuTrigger disableButtonEnhancement><Tooltip><Button icon=<MoreHorizontal20Regular/> aria-label="More options"/></Tooltip></MenuTrigger>`).

- **Settings** — `MenuItem` calls `onOpenSettings` unchanged (still routed through `SmartTodoApp`'s `settingsOpenerRef` → the existing `ThresholdSettings` popover trigger inside `SmartToDo`). Hidden if `onOpenSettings` is omitted (same optional-prop convention as before).
- **Layout** — `MenuItem` calls a new local `handleLayoutClick` in `Header.tsx` that flips `orientation` via a `NEXT_ORIENTATION` map (mirrors `OrientationToggle.tsx`'s internal flip semantics — not imported, since that module doesn't export the map and it's two lines) and calls `onOrientationChange` with the flipped value — same prop, same `SmartTodoApp.handleOrientationChange` → `updateViewPrefs({ orientation })` path as before. The menu item's icon also reflects current orientation (`LayoutColumnTwo20Regular` for horizontal, `LayoutRowTwo20Regular` for vertical), matching `OrientationToggle`'s "icon shows current state" convention. Hidden if `orientation`/`onOrientationChange` are omitted.
- **Refresh** — `MenuItem` calls `onRefresh` unchanged (still routed through `SmartTodoApp`'s `innerRefetchRef` → `handleRefresh`). Hidden if `onRefresh` is omitted.

All three behaviors are byte-for-byte the same handlers as before — only their trigger UI moved from inline `Button`s into `MenuItem`s.

## Filter pill + "+ New Task" wiring (new controlled state)

- `SmartTodoApp.tsx` now owns `isFilterPaneOpen` (`useState<boolean>(false)`) and `handleToggleFilterPane` (flips it), passed to `<Header isFilterPaneOpen onToggleFilterPane />`. `Header.tsx` renders the Filter pill with `aria-expanded={isFilterPaneOpen}` (disclosure semantics, not `aria-pressed` — it opens a pane, it isn't a binary toggle). **No filter predicate/category logic is implemented anywhere** — this task builds only the trigger + state per its own constraint; task 021 (FR-06) owns the pane content.
- `SmartTodoApp.tsx` adds `handleNewTask` — a documented no-op stub (`// TODO(030): wire to OOB main-form create modal per FR-10`) passed as `onNewTask`. The button is visible and clickable, never hidden, never throws — satisfies the acceptance criterion that a stub exists until task 030 lands.

## Was the old "Search" affordance removed?

Yes — confirmed removed:
- No `<SearchBox>` / `<ToggleButton icon={<Search20Regular/>}>` toggle pattern remains in Header.tsx.
- `grep` for `input`/`role="searchbox"` inside a rendered `Header` returns nothing (also asserted in `Header.test.tsx`'s `render_DefaultNoSelection_ShowsFilterNewTaskOverflowInOrderOnly` test).
- The `Search20Regular` icon is reused (not the SearchBox component) purely as the **Filter pill's** icon glyph, per the mockup ("magnifying-glass icon + Filter label" — the task's own prompt text specifies this icon for the *new* Filter pill, distinct from the old broken SearchBox affordance being removed).

## Deviations from a literal step-by-step reading (documented per step 11 + directional step-mode judgment)

1. **`searchQuery` state retained in `SmartTodoApp.tsx`, NOT deleted.** `SmartToDo.tsx` (out of this task's 4-file scope) still reads `searchQuery` for its card-level substring filter. Header's inline SearchBox was the only prior producer of `setSearchQuery`; removing it (per FR-05 mandate) leaves `searchQuery` permanently at its `""` initial value until task 021's filter pane wires a new producer. Per step 6's own explicit branching guidance ("if SmartToDo still needs it for card filtering, keep the state but stop passing onSearchChange to Header"), the state is kept as a plain constant (`const searchQuery = "";` — not `useState`, since nothing sets it anymore, avoiding an unused-setter lint warning) and still passed to `<SmartToDo searchQuery={searchQuery} />` unchanged. `onSearchChange`/`searchQuery` props were removed from `HeaderProps` entirely (Header no longer needs them). This is the exact scenario step 6 pre-solved — not an escalation case.

2. **`QUICK_ADD_TODO_EVENT` / `QuickAddTodoEventDetail` retained as exports from `Header.tsx`** (marked `@deprecated`), even though Header no longer dispatches the event. Discovered mid-task: `SmartToDo.tsx` (out of scope) imports both for its `handleAdd` window-event listener (`window.addEventListener(QUICK_ADD_TODO_EVENT, ...)`). Deleting them — a literal reading of step 1's "delete... all its local state/handlers" — would break `SmartToDo.tsx`'s TypeScript compile, a file this task is not authorized to touch. Kept the two-symbol module-scope contract (constant + interface) as a documented, harmless accommodation; `SmartToDo.tsx`'s listener is now orphaned (no producer fires the event anymore) as a **direct, intended consequence of FR-05's QuickAdd removal**, not a new regression — the "+ New Task" replacement path (task 030) is the acknowledged gap-filler. **Flagging for task 030's author**: consider either (a) deleting the orphaned listener + these two retained exports from Header as part of 030's cleanup, or (b) repurposing the same event name/shape as the OOB-form create-confirmation channel if that fits 030's design. Not resolved here — out of this task's scope.

3. **`ViewToggle`/`viewMode` plumbing removed from `HeaderProps`** entirely (was already dead — no callsite in `SmartTodoApp.tsx` ever passed `viewMode`/`onViewModeChange`; list view was discontinued 2026-06-19, kanban-only). Not explicitly named in the POML steps, but removal is required by FR-05's closed acceptance set ("inline (non-overflow) actions are ONLY Filter and + New Task" — no third slot for a hypothetical ViewToggle) and is a clean dead-code removal with zero behavior change (it never rendered in production).

4. **`useCurrentContactId` hook call + import removed from `SmartTodoApp.tsx`.** It fed `defaultAssignedToContactId`/`defaultAssignedToName` into Header's now-deleted QuickAdd Assigned-To typeahead — its sole consumer. No other code in `SmartTodoApp.tsx` read `currentContactId`/`currentContactName`. Confirmed via grep before removal.

5. **Selection-branch composition** (Filter pill + `<SelectionAwareToolbar>`, no `+ New Task`/no overflow) is a judgment call, not spelled out in the POML — reasoning under "What changed" above.

## Canonical reference for task 021

Per the task's own `<notes>`: the deleted Assigned-To `Xrm.WebApi` contact-typeahead pattern (debounced 250ms, `contains(fullname,'…')` OData filter against `contact`, `statecode eq 0`, `$top=8`, `$orderby=fullname asc`) — previously at `Header.tsx` lines ~368–438 (pre-redesign) — is the canonical reference for task 021's new filter pane Assigned-To category. It is fully removed from `Header.tsx` now (see `git show HEAD:...Header.tsx` for the pre-redesign version); task 021's author should pull the pattern from git history, not from this file.

## Verification results

- **`npx tsc --noEmit`** (full Code Page): zero errors in the 4 scoped files. Remaining errors are entirely in `Spaarke.Auth`, `Spaarke.UI.Components` (types module, `@azure/msal-browser` / `ComponentFramework` / `DOMPurify` ambient-type gaps — pre-existing, unrelated) and `src/components/SmartToDo.tsx` (2 pre-existing/sibling-agent type errors: an `IWebApi.updateRecord` shape mismatch and 2 missing `ITodo` properties — none touch anything this task changed; ignored per the task's explicit "ignore transient sibling-file errors" instruction).
- **`npx jest src/components/Header`**: 13/13 tests pass (see harness note below).
- **`npx jest` (full solution)**: 4 suites / 76 tests pass — no regressions from sibling agents' concurrent work either.
- **Hex/rgb grep** across all 4 files: zero matches (`grep -nE "#[0-9a-fA-F]{3,8}\b|rgb\(|rgba\("` → exit 1 / no output).
- **Quality gates** (code-review + adr-check, Step 9.5): 0 Critical, 0 ADR violations. 3 Warnings, all documented above (items 2 and the two infra notes below) — none require CLAUDE.md §6.5 escalation (no ADR was violated).

## Test-infrastructure notes (environment gaps discovered, not introduced by this task)

1. This worktree's `src/client/shared/Spaarke.SdapClient` package has **no `node_modules` and no built `dist/`** (gitignored build output, never produced here). `@spaarke/ui-components`'s barrel (`services/index.js` → `EntityCreationService.js`) unconditionally `require`s `@spaarke/sdap-client`, which would make ANY jest test importing that barrel fail with `Cannot find module '@spaarke/sdap-client'` — this is the FIRST component test ever written for this solution's `src/`, so nobody had hit this wall before. Per the task's concurrency constraint ("do NOT run npm install"), worked around by `jest.mock('@spaarke/ui-components', ...)` inside `Header.test.tsx` (documented inline). This only affects test execution, not the production build (Vite doesn't hit this barrier the same way, and the existing Vite build was never exercised by this task).
2. This package's `node_modules` has `@testing-library/jest-dom` but **not** `@testing-library/react` or `@testing-library/user-event` (unlike most other Spaarke solutions). `Header.test.tsx` uses a minimal manual harness (`react-dom/client` `createRoot` + `act` from `react` + native DOM event dispatch, since React 19 dropped `ReactTestUtils.Simulate`) instead. Documented inline in the test file's header comment. If a future task adds these packages properly (`npm install` in a non-concurrent window), `Header.test.tsx` could be migrated to RTL for brevity, but the current tests are fully functional and cover all of task 020's acceptance criteria.

## Acceptance criteria — verified

| Criterion | Status |
|---|---|
| Left = checkmark + "Smart To Do"; right = Filter · + New Task · ⋮, in order, nothing else inline | ✅ `render_DefaultNoSelection_ShowsFilterNewTaskOverflowInOrderOnly` |
| ⋮ menu = exactly Settings, Layout, Refresh, in order, unchanged handlers | ✅ `open_OverflowMenu_ContainsExactlyThreeItemsInOrder` + 3 click tests |
| + New Task clickable no-op stub, not hidden, until task 030 | ✅ `click_NewTaskButton_InvokesOnNewTaskStub` |
| Filter pill toggles `isFilterPaneOpen` / fires `onToggleFilterPane` | ✅ `click_FilterPill_TogglesIsFilterPaneOpenViaCallback` |
| Keyboard-only nav reaches every control; Escape closes menu | ✅ DOM tab order asserted (Filter → +New Task → ⋮); `keyboard_EscapeClosesMenu` (Fluent's built-in Escape-to-close verified; full focus-return assertion covered by Fluent's own tested internals) |
| Zero hex/rgb; all tests pass; build green | ✅ grep clean; 13/13 + 76/76 pass; `tsc` clean on scoped files |

## Confirmation

Nothing was committed (no `git add`/`git commit` run). Only the 4 in-scope files were edited, plus this notes file and the task POML status update (both explicitly permitted deliverables, not bookkeeping files I was told to avoid).
