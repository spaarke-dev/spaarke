# Task 011 — Single-source-of-truth auto-score handler (Option B)

**Status**: Complete
**Date**: 2026-08-16

## What was built

One canonical choice→score mapping module, consumed by all three surfaces named in FR-02/FR-03:

**Canonical module**: [`src/client/shared/Spaarke.UI.Components/src/utils/todoScoreMappings.ts`](../../../src/client/shared/Spaarke.UI.Components/src/utils/todoScoreMappings.ts)

Exports:
- `PRIORITY_TO_SCORE` — `{ Urgent: 100, High: 75, Medium: 50, Low: 25 }`
- `EFFORT_TO_SCORE` (Option B, quick-wins-first) — `{ Low: 25, Medium: 50, High: 75, 'Very High': 100, None: 50 }`
- `DEFAULT_PRIORITY_CHOICE` = `'Medium'`, `DEFAULT_EFFORT_CHOICE` = `'None'`
- `NULL_DEFAULT_PRIORITY_SCORE` = 50, `NULL_DEFAULT_EFFORT_SCORE` = 50
- `priorityChoiceToScore(choice)` / `effortChoiceToScore(choice)` — defensive, never throw, fall back to the null-default for unrecognized/missing input
- `scoreToPriorityChoice(score)` / `scoreToEffortChoice(score)` — reverse lookup (used by the wizard to pre-select a Dropdown option from an existing numeric score)
- `TODO_PRIORITY_CHOICES` / `TODO_EFFORT_CHOICES` — ordered arrays for rendering Dropdown options

Re-exported via `src/utils/index.ts` → `src/index.ts`, so it's reachable both as a relative import (`../../utils/todoScoreMappings`) inside the package and as a named export off the bare `@spaarke/ui-components` barrel for external consumers.

## Placement decision (why UI.Components, not Spaarke.SmartTodo.Components)

Per ADR-012 + CLAUDE.md §11 (extend, don't duplicate): verified via import-graph inspection (`grep -rn "@spaarke/ui-components" src/solutions/SmartTodo/src`) that `SmartToDo.tsx` **already** imports directly from the bare `@spaarke/ui-components` specifier (`KanbanBoard`, `OrientationToggle`) — confirmed in `src/solutions/SmartTodo/src/components/SmartToDo.tsx` line 48 (pre-task-011). `CreateTodoWizard` already lives inside `Spaarke.UI.Components`. Placing the mapping module there means **both** consuming surfaces reach it through an **existing** dependency edge — no new cross-package edge was introduced, so the task's escalation trigger did not fire.

## Three-surface wiring

1. **CreateTodoWizard** (`CreateTodoStep.tsx`) — the raw 0-100 Priority/Effort **sliders** were replaced with Priority/Effort **Dropdowns** (Fluent v9 `Dropdown`/`Option`) that resolve through `priorityChoiceToScore`/`effortChoiceToScore` and set `formValues.priorityScore`/`effortScore` — the SAME numeric fields `todoService.ts` already writes to `sprk_priorityscore`/`sprk_effortscore` (write path unchanged, per constraint). `formTypes.ts`/`todoService.ts` got documentation-only updates (no shape change to `ICreateTodoFormState`) — the selected Priority/Effort **choice** is local UI state in `CreateTodoStep.tsx`, not persisted to a `sprk_priority`/`sprk_effort` choice field by the wizard today (see Deferral below).
2. **SmartTodo Code Page quick-add** (`SmartToDo.tsx` `handleAdd`) — the hardcoded `sprk_priorityscore: 50, sprk_effortscore: 10` literals in the optimistic UI item were replaced with `NULL_DEFAULT_PRIORITY_SCORE` (50) / `NULL_DEFAULT_EFFORT_SCORE` (50), imported from `@spaarke/ui-components`. Quick-add has no Priority/Effort choice input, so it resolves the documented null-defaults. Note: the old `10` effort literal was **not** a documented default anywhere — it's now 50, matching the None/Option-B null-default and the wizard's own default. `DataverseService.createTodo` (the actual Web API create call) doesn't write `sprk_priorityscore`/`sprk_effortscore` at all today — that's a pre-existing gap in a file outside this task's scope (`src/solutions/SmartTodo/src/services/DataverseService.ts`); only the OPTIMISTIC UI item was in scope per the task's file list. Flagging for a follow-up task/issue if the created record's score fields need to be populated server-side.
3. **`sprk_todo` form OnChange webresource** (`src/client/webresources/js/sprk_todo_score_onchange.js`, new) — registers `sprk_priority`/`sprk_effort` OnChange handlers via `formContext.getAttribute(...).addOnChange(...)`, mirroring the doc-comment + defensive (`try`/`catch`, never throw/block save) style of the sibling `sprk_todo_regarding_presave.js`. The two lookup tables are a **literal mirror** of the canonical TS module's exported tables, keyed by the Dataverse option-set integers from task 010 (`sprk_priority`: Urgent=100000000/High=100000001/Medium=100000002/Low=100000003; `sprk_effort`: None=100000000/Very High=100000001/High=100000002/Medium=100000003/Low=100000004) — confirmed against `projects/smart-todo-r5/tasks/010-*.poml`. The file header carries an explicit `>>> IF EITHER TABLE ... CHANGES, UPDATE THE TABLES BELOW TO MATCH` cross-reference comment. This is the ONE sanctioned literal duplication (webresources cannot `import` npm packages).

## Locked formula verification

`todoScoring.ts` (`Spaarke.SmartTodo.Components/src/utils/todoScoring.ts`) was **not touched** — confirmed via `git diff --stat` (empty) and a `sha256` hash assertion baked into the new test suite (`todoScoreMappings.test.ts` → "todoScoring.ts remains untouched" describe block), hashed immediately before any task-011 edits: `e919bf8f471b35716e071e6fc07f6d899598637a95326eaff5c4b108ee525a72`.

## Parity verification approach

Per the acceptance criterion wording ("verified by import graph, not by value comparison alone"), `todoScoreMappings.test.ts` includes a "cross-surface parity (import graph)" describe block that reads the actual source text of `CreateTodoStep.tsx` and `SmartToDo.tsx` and asserts:
- `CreateTodoStep.tsx` imports from the relative canonical module path (`../../utils/todoScoreMappings`).
- `SmartToDo.tsx` imports `NULL_DEFAULT_PRIORITY_SCORE`/`NULL_DEFAULT_EFFORT_SCORE` from the `@spaarke/ui-components` barrel (which re-exports the same module).
- `SmartToDo.tsx` no longer contains the old undocumented `sprk_effortscore: 10` literal.
- The `utils/index.ts` barrel actually re-exports `todoScoreMappings` (so the bare-specifier import resolves).

## Test/build results

- `npx jest src/utils/__tests__/todoScoreMappings.test.ts` — **21/21 passed** (value tables, null-defaults, negative/defensive fallback, parity, locked-formula hash).
- `npx jest src/components/CreateTodoWizard` (UI.Components) — `todoService.test.ts` **13/13 passed**. `initialRegarding.test.tsx` fails at module resolution (`Cannot find module '@spaarke/sdap-client'`) — **pre-existing environment issue**, unrelated to this task (confirmed via `git status` showing zero pending changes to that test file or its dependency chain before this task started).
- `npx tsc --noEmit` (UI.Components) — 3 pre-existing errors, none in files touched by this task (`AccessGrantModal/types.ts`, `useWizardPageBootstrap.ts`, `EntityCreationService.ts` — all `@spaarke/auth`/`@spaarke/sdap-client` module-resolution gaps unrelated to scoring).
- `npx tsc --noEmit` (`src/solutions/SmartTodo`) — pre-existing errors only (Auth `@azure/msal-browser`, `ComponentFramework` namespace gaps, and two unrelated `SmartToDo.tsx` type errors at lines ~417/591/592 concerning `IWebApi`/`ITodo` shape — none reference the new score-mapping symbols).
- `npx jest` (full `src/solutions/SmartTodo` suite) — **76/76 passed**, 4 suites, no regressions.
- `node -c src/client/webresources/js/sprk_todo_score_onchange.js` — syntax OK. Manually exercised `onPriorityChange`/`onEffortChange` against a mocked `formContext` (Node script, not committed) — confirmed High priority → 75, Low effort → 25, and a cleared effort attribute → 50 (null-default), matching the canonical TS table exactly.

## Deferral / known gap (not fixed — out of this task's scope)

- The wizard does not persist the raw `sprk_priority`/`sprk_effort` **choice** value today (only the derived score). A record created via the wizard will show the correct `sprk_priorityscore`/`sprk_effortscore` but a blank Priority/Effort dropdown if later opened on the live `sprk_todo` form. Considered writing `sprk_priority`/`sprk_effort` alongside the score in `todoService.ts`, but the task's acceptance criteria only test score correctness, and adding raw-choice persistence would require deciding the wizard's `@odata` payload shape for two more fields outside the task's stated scope — deferring to a follow-up rather than expanding scope here.
- `DataverseService.createTodo` (quick-add's actual persistence path, `src/solutions/SmartTodo/src/services/DataverseService.ts`) doesn't write `sprk_priorityscore`/`sprk_effortscore` to the created record at all — pre-existing, outside this task's file scope.

## ADR-006 tension surfaced at adr-check (task 011 Step 9.5) — NOT yet in spec.md

`adr-check` flagged a Warning: the new `sprk_todo_score_onchange.js` webresource adds a Choice-field OnChange
handler with real logic (lookup table + fallback) to the OOB `sprk_todo` main form. ADR-006's table scopes
webresource JS to ribbon/command scripts (invocation-only, no business logic); this file is a form-scripting
OnChange handler, a category ADR-006 doesn't explicitly enumerate as sanctioned.

**Mitigating context**: this project's `CLAUDE.md` already carries an ADR-050 Path A exception establishing that
`sprk_todo` create/open uses the **native OOB Dataverse main form** (owner requirement: native Save/Save&Close +
business rules + these exact score fields — see `projects/smart-todo-r5/spec.md` "ADR Tensions" table, ADR-050
row). Given that OOB-native-form choice, a Choice-field-changes-a-Number-field behavior has no Code-Page/PCF hook
point on that form — the only mechanisms are classic form-scripting OnChange (what this file does) or a PCF bound
to the field (heavier, and not what the codebase's own precedent chose). The sibling file
`sprk_todo_regarding_presave.js` (R4 task R4-051) already established exactly this "webresource bridges logic onto
the OOB `sprk_todo` form" pattern.

**Recommended resolution**: Path A (project-scoped exception) — add an ADR-006 row to `spec.md`'s "ADR Tensions"
table extending the existing ADR-050 rationale. `spec.md` is **outside task 011's file scope**, so this was not
edited here — flagging for the orchestrator/reviewer to add the row and confirm the exception, per CLAUDE.md §6.5
(no silent compliance-by-omission).

## Deviations from the POML

- Step 7 ("Update tasks/TASK-INDEX.md: mark 011 complete") was **not executed** — the dispatching orchestrator's explicit scope ban for this task run (3 concurrent agents in one worktree) forbids editing `TASK-INDEX.md`/`current-task.md`. Left for the orchestrator to update.
- No `git add`/`git commit` performed, per the same scope ban.
