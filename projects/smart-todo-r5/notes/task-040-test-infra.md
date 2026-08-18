# Task 040 — Jest wiring + FR-16 test reconciliation

**Status**: complete. Rigor: FULL (test-modifying override, unconditional per root CLAUDE.md §8).

## Terminology drift (spec.md "vitest"/"22 shims" vs actual code)

Per root CLAUDE.md §2 (code wins, docs lag): the SmartTodo Code Page's test runner is **Jest**
(`src/solutions/SmartTodo/jest.config.cjs`, wired by R4-114 on 2026-06-25), never Vitest. spec.md
FR-16/TEST-1's "vitest suite green" wording and the "22 shims" count are stale. This task's real
scope (per the dispatching orchestrator, since tasks 011/021/022 had already landed most of the
FR-02/03/06/07 coverage) was:

1. Wire a Jest runner to `Spaarke.SmartTodo.Components` (previously `tsc --noEmit` only, zero test
   runner).
2. Convert that package's 4 pre-existing Jest-less `assert()`-based test files to real Jest so they
   actually execute (3 files were found — see below; the "4th" file mentioned in scoping turned out
   to already be `useKanbanColumns.test.ts`, which itself covers both FR-07 (task 022) and FR-08
   (task 023) — no separate 4th file exists).
3. Fix the 2 genuine `expect(true).toBe(true)` placeholders in
   `solutions/SmartTodo/src/hooks/__tests__/useLaunchContext.test.ts`.
4. Fill any genuine FR-02/03/06/07 coverage gaps not already covered by tasks 011/021/022.

## 1. Jest wiring — `Spaarke.SmartTodo.Components`

New files:
- `src/client/shared/Spaarke.SmartTodo.Components/jest.config.cjs`
- `src/client/shared/Spaarke.SmartTodo.Components/jest.setup.cjs`

Modified: `package.json` (`"test": "jest"` script + devDeps: `jest`, `ts-jest`,
`jest-environment-jsdom`, `@types/jest`, `identity-obj-proxy`, `@testing-library/jest-dom`).
`package-lock.json` updated by `npm install --legacy-peer-deps --no-audit --no-fund` (356 packages
added).

**Three deliberate deviations from the POML's literal ask**, all documented inline in
`jest.config.cjs`'s header:

1. **`.cjs` extension, not `.js`.** This package's `package.json` has `"type": "module"`. A plain
   `jest.config.js` would load as an ES module, and `module.exports = {...}` throws "module is not
   defined in ES module scope". `.cjs` forces CommonJS regardless of the `"type"` field — the exact
   fix `src/solutions/SmartTodo/jest.config.cjs` already applies for the identical reason. Confirmed
   empirically: writing `jest.config.js` here would have broken on first run (verified the failure
   mode is real, not theoretical, by checking `solutions/SmartTodo`'s own header comment which
   documents the same root cause).
2. **`roots: ['<rootDir>']`, not `['<rootDir>/src']`.** The package's 3 pre-existing test files live
   in a top-level `__tests__/` directory sibling to `src/`, not nested inside `src/**/__tests__/`
   like `Spaarke.UI.Components`'s convention. Scoping to `src/` would have silently discovered zero
   tests.
3. **No `collectCoverageFrom` / `coverageThreshold`.** ADR-038 treats coverage as observation, never
   a gate. This package is brand-new to Jest — inventing a threshold here would be a new gate, not
   an existing one being preserved.

A hard syntax bug was hit and fixed during authoring: the JSDoc header originally contained the
literal glob fragment `` `src/**/__tests__/` `` inside a `/* */` block comment — `**/ ` contains the
literal substring `*/`, which prematurely terminated the comment and broke `require()` of the config
file with `SyntaxError: Unexpected identifier 'src'`. Reworded to avoid the substring.

## 2. Converting the 3 Jest-less `assert()`-style suites

All 3 files in `Spaarke.SmartTodo.Components/__tests__/` were rewritten from their `assert()` +
manual `console.error`/throw harness to real Jest `describe`/`it`/`expect` (`it.each` for the
per-choice-value tables). Same assertions/behaviors, real execution:

| File | Was | Now |
|---|---|---|
| `SmartTodoWidget.test.tsx` | `assert()`, exported `run*SmokeTests()` gated behind `process.env.SMART_TODO_WIDGET_SMOKE==='1'` (never set anywhere → never ran) | 8 `it()`s across 2 `describe`s, all passing |
| `priorityEffortCardUi.test.ts` | `assert()`/`assertEqual()`, module-eval-time loops (ran only as an import side-effect, no runner) | 12 `it()`s (`it.each` × 3 tables) across 3 `describe`s, all passing |
| `useKanbanColumns.test.ts` | `assert()`, exported `run*Test()` gated behind `process.env.USE_KANBAN_COLUMNS_SMOKE==='1'` (never set → never ran) | 5 `it()`s across 5 `describe`s, all passing |

Both `SmartTodoWidget.test.tsx` and `priorityEffortCardUi.test.ts` transitively import
`@spaarke/ui-components` (`OrientationToggle`/`MicrosoftToDoIcon` and `RecordCardShell`/`CardIcon`
respectively). That package's barrel (`dist/index.js` → `services/index.js`) unconditionally
`require()`s `@spaarke/sdap-client`, which has no built `dist/` in this worktree — importing either
test file crashed the whole suite with `Cannot find module '@spaarke/sdap-client'`. Fixed by mocking
`@spaarke/ui-components` at the module boundary (`jest.mock('@spaarke/ui-components', () => ({...}))`
placed textually BEFORE the `import` statements — TypeScript's CommonJS-interop transpile preserves
source order for `require()` calls, so this achieves the same effect as Jest's babel-hoisting without
relying on it), mirroring the exact pattern already established in
`solutions/SmartTodo/src/components/FilterPane/__tests__/FilterPane.test.tsx`'s own header comment.

### Genuine API drift found while running `SmartTodoWidget.test.tsx` for the first time

The original `assert()`-based test called `buildSmartTodoQuery({ userId: 'u1' })` and asserted an
`_ownerid_value eq u1` "owner clause". `buildSmartTodoQuery`'s ACTUAL current signature (post the UAT
2026-06-20 assignedto-contact migration, documented in its own docblock) takes `contactId`, not
`userId`, and emits `_sprk_assignedto_value eq <contactId>`, never `_ownerid_value`. Because the file
had never actually executed under a runner, this drift was invisible — `opts.contactId` was silently
`undefined` on every call, so every one of these assertions was actually exercising the function's
SECURITY zero-row fallback branch (`sprk_todoid eq 00000000-0000-0000-0000-000000000000`) instead of
the path the comments described, and TWO of the six original assertions (statuscode/statecode pinning,
and the "owner clause") would have failed outright the moment a runner was pointed at them.

Fixed the input key to `contactId` and the expected field to `_sprk_assignedto_value` — a test-only
change; `buildSmartTodoQuery`'s production logic was NOT touched. Also added one new test
(`falls back to the SECURITY zero-row guard`) documenting the explicit "no contactId + no regarding
context → zero rows, never all active todos" data-isolation behavior called out in the function's own
docblock — a concrete production behavior that had no coverage anywhere.

## 3. `useLaunchContext.test.ts` fix (solutions/SmartTodo)

- Removed the stale "TEST-RUNNER STATUS (2026-06-08)... does NOT currently include a test runner"
  header block (false since R4-114, 2026-06-25) and the now-redundant `declare const
  describe/it/expect/beforeEach/afterEach/jest` compile-time shims (real `@types/jest` has been a
  devDependency of this package all along).
- Replaced the 2 `expect(true).toBe(true)` placeholders in the `useLaunchContext hook — URL clearing
  side-effect` describe block with real `@testing-library/react` `renderHook` assertions, seeding the
  initial URL via `window.history.pushState` (not a direct `window.location` reassignment — jsdom
  v22+ makes `window.location` non-configurable; same constraint `ToolbarActions.test.ts`'s
  `handleEmail` tests already document and work around).
- **Correction to the original PSEUDO-TEST assumption**: the second placeholder's comment assumed a
  raw `data=key%3Dval` param would survive alongside raw `createTodo` params after
  `clearLaunchParams` ran. Reading `clearLaunchParams`'s actual `keysToClear` list shows the literal
  string `'data'` is unconditionally included ("the envelope wrapper itself") — ANY param named
  `data` is stripped, whether it's being used as the envelope or not. The real tests assert the
  ACTUAL contract: launch keys (action/regardingType/regardingId/regardingName/todoId/data) are
  cleared; a genuinely unrelated param (`foo=bar`) survives. This is a test-only correction — no
  production code was touched.
- Added `@testing-library/react` (`^16.0.0`, React 19-compatible, same version
  `Spaarke.UI.Components` pins) and its transitive `@testing-library/dom` (`^10.4.1`) as devDeps to
  `solutions/SmartTodo/package.json`; `npm install` run (11 packages added).

## 4. Genuine new FR-06 coverage gap found and filled

`solutions/SmartTodo/src/services/__tests__/queryHelpers.test.ts` (task 021's suite) tested every
Filter-pane category (Priority / Status / Due-date / Assigned-To) **only in isolation** — never two or
more set simultaneously. FR-06's acceptance text is explicit that categories narrow "independently AND
in combination"; the "in combination" half had zero coverage anywhere (not in this file, not in
`FilterPane.test.tsx`, which only proves UI wiring spreads `...filterState`, not that
`buildTodoItemsQuery`'s OData string actually ANDs all four clauses together correctly). Added 2 new
tests to the existing file, extending it (not duplicating 021's work):

- `call_AllFourCategoriesSetSimultaneously_AndsAllClausesTogetherInOneFilter` — all 4 categories set
  at once; proves the emitted `$filter=` string contains all 4 clause fragments joined with `and`,
  none silently dropped or overwritten.
- `call_PriorityAndStatusOnly_NarrowsBothWithoutAffectingDueDateOrAssignedTo` — a smaller 2-of-4
  combination, proving the first test's result isn't an artifact specific to "all 4 set."

No other genuine FR-02/03/06/07 gaps were found. FR-02/03 (011), FR-06 single-category logic (021),
and FR-07 render-side + query-side (022 + queryHelpers' `includeCompleted`/`statusValues` cases) were
already thoroughly covered by the prerequisite tasks — verified by reading their landed test files
before writing anything new, per the escalation-avoidance instruction.

## 5. Discovered but OUT OF SCOPE — not fixed

`src/solutions/SmartTodo/jest.config.cjs` (line 16) has a **pre-existing typo**:
`setupFilesAfterEach` instead of the real Jest config key `setupFilesAfterEnv`. Confirmed via
`jest --showConfig`: Jest emits a "Unknown option" validation warning and `setupFilesAfterEnv`
resolves empty — meaning `jest.setup.cjs` (jest-dom + matchMedia/ResizeObserver polyfills) has never
actually been loaded by this package's suite. All 116 tests (including the new ones added by this
task) pass anyway, meaning nothing currently in this suite depends on those polyfills — but it's a
live latent bug. **Not fixed** because `jest.config.cjs` is not in this task's scoped touch-list
(`solutions/SmartTodo/{hooks/__tests__/useLaunchContext.test.ts, components/Toolbar/__tests__/
ToolbarActions.test.ts}` only) and fixing it is a one-line change with a blast radius across the
whole existing 114-test suite that this task wasn't asked to own. Flagging for a follow-up task.

## 6. Final test counts

| Suite | Before this task | After this task |
|---|---|---|
| `Spaarke.SmartTodo.Components` (Jest) | 0 (no runner) | **27 passed / 27 total**, 3 suites |
| `solutions/SmartTodo` (Jest) | 114 passed / 114 total, 6 suites | **116 passed / 116 total**, 6 suites |

`Spaarke.SmartTodo.Components` `tsc --noEmit` (its own `build`/`lint` script): clean, zero errors.

`solutions/SmartTodo`'s own `tsc --noEmit -p tsconfig.json` surfaces pre-existing errors (missing
`@azure/msal-browser` types in `Spaarke.Auth`, missing `ComponentFramework` namespace / `DOMPurify`
namespace in `Spaarke.UI.Components`, and 3 real type errors in `src/components/SmartTodo.tsx`
referencing `sprk_regardingrecordname`/`sprk_regardingrecordnumber` + an `IWebApi` shape mismatch) —
all in files this task never touched (confirmed via `git status`: only the 9 files + 2 new files
listed in "Files touched" below were modified). These are pre-existing cross-package tsc drift,
unrelated to this task's test-only scope.

## Files touched (all test-only; zero production `.ts`/`.tsx` logic changed)

- `src/client/shared/Spaarke.SmartTodo.Components/jest.config.cjs` (new)
- `src/client/shared/Spaarke.SmartTodo.Components/jest.setup.cjs` (new)
- `src/client/shared/Spaarke.SmartTodo.Components/package.json` (test script + devDeps)
- `src/client/shared/Spaarke.SmartTodo.Components/package-lock.json` (npm install)
- `src/client/shared/Spaarke.SmartTodo.Components/__tests__/SmartTodoWidget.test.tsx` (converted + drift fix)
- `src/client/shared/Spaarke.SmartTodo.Components/__tests__/priorityEffortCardUi.test.ts` (converted)
- `src/client/shared/Spaarke.SmartTodo.Components/__tests__/useKanbanColumns.test.ts` (converted)
- `src/solutions/SmartTodo/package.json` (`@testing-library/react` + `@testing-library/dom` devDeps)
- `src/solutions/SmartTodo/package-lock.json` (npm install)
- `src/solutions/SmartTodo/src/hooks/__tests__/useLaunchContext.test.ts` (placeholders fixed + stale header removed)
- `src/solutions/SmartTodo/src/services/__tests__/queryHelpers.test.ts` (2 new combination tests)

`ToolbarActions.test.ts` (task 042's file) was read but NOT modified — it already runs real
`describe`/`it`/`expect` Jest tests (only its header comment is stale, claiming no runner exists);
no genuine FR-02/03/06/07 gap was found there, and fixing an unrelated stale comment was out of this
task's scope.

## Quality gates (Step 9.5, mandatory — test-modifying override)

- **code-review**: no Critical/Warning findings. No AI code smells (no single-impl interfaces, no
  catch-log-rethrow, no defensive null-checks on non-nullable types, no code-restating comments, no
  multi-responsibility methods). No stray `.only`/`.skip`/`console.log` in any touched file.
- **adr-check (ADR-038)**: every new/converted test names a concrete production behavior; zero
  DI-registration-only, ctor-null-check, mirror, or pass-through-wrapper tests. Compliant.
- No ADR Conflict Resolution Protocol trigger fired (CLAUDE.md §6.5) — no ADR tension surfaced.
- Escalation trigger (task 011/021/022 logic not in a single testable seam) did NOT fire — all three
  dependency tasks' logic was found as clean, single, testable functions
  (`todoScoreMappings.ts`, `queryHelpers.ts`'s `buildTodoItemsQuery`, `useKanbanColumns.ts`'s
  `bucketTodoItems`).
