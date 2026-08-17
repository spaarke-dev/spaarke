# Task 030 — "+ New Task" opens sprk_todo OOB main form (create) as a modal (FR-10)

> Status: implementation complete, real-DV UI smoke (POML `<ui-tests>`) still pending an operator with MDA access.

## What changed

- `src/solutions/SmartTodo/src/SmartTodoApp.tsx` — replaced the task-020 documented no-op `handleNewTask` stub with a call to `launchNewTaskCreateForm(launchContext, handleRefresh)`. Reordered `handleRefresh` and `const launchContext = useLaunchContext();` earlier in `SmartTodoLayout` so `handleNewTask` (also moved earlier, next to its own doc block) can reference both — no behavior change to either, pure reorder. `LaunchCreateTodoWizardHost` / `CreateTodoWizard` (Outlook + parent-form ribbon "Create To Do" flow) were **not** touched.
- `src/solutions/SmartTodo/src/services/newTaskLauncher.ts` (**new**) — `buildNewTaskDefaultValues()` + `launchNewTaskCreateForm()`. Extracted from `SmartTodoApp.tsx` so the regarding→defaultValues mapping is unit-testable without pulling in `SmartTodoApp.tsx`'s full import graph (Header/SmartToDo/FilterPane/Toolbar/TodoContext/`@spaarke/auth`). Justification per CLAUDE.md §11: existing = `openSprkTodoAsLayout1` is the only sibling launcher and has no branching logic worth isolating; extension = not applicable (no existing create-form-launch service to extend); cost-of-doing-nothing = the regarding-mapping branch logic (openTodos vs createTodo vs unknown-entity-type) would otherwise be untestable dead weight inside `SmartTodoApp.tsx`'s already-large component.
- `src/solutions/SmartTodo/src/__tests__/SmartTodoApp.test.tsx` (**new**) — 12 tests covering `buildNewTaskDefaultValues` mapping branches + `launchNewTaskCreateForm`'s call contract (entityName `sprk_todo`, no `entityId`, refresh-on-save, no-refresh-on-cancel/no-launch).

## Reused launcher (CLAUDE.md §11 / POML constraint)

`navigateToEntityRecordSurfaceAsync()` (`src/client/shared/Spaarke.UI.Components/src/components/WorkspaceShell/wizardLaunchers.ts:397`) — already exported from the package's top-level barrel (`@spaarke/ui-components`, via `components/index.ts` → `WorkspaceShell/index.ts` re-export). **No barrel change was needed** — verified before implementing (POML step 2's "verify it is exported" check passed as-is). No second `Xrm.Navigation.navigateTo` call site was written.

## ADR-050 Path A exception (cite in PR)

Per `spec.md` ADR Tensions + CLAUDE.md §6.5, this task deliberately keeps the OOB `Xrm.Navigation.navigateTo` main-form CREATE surface (`pageType: 'entityrecord'`, no `entityId`, `createForm` OOB size 70%×80%) rather than building a `SprkModal`/`FormModal` config. `SprkModal`/ADR-050 does not govern OOB `navigateTo` dialogs (`docs/standards/MODAL-DECISION-CRITERIA.md` Family 1 row) — this is a deliberate OOB-family choice, not a shell violation. The owner requires the real Dataverse form (native Save/Save & Close, business rules, the F-4/F-5 score fields, the RegardingResolver control from task 013).

## defaultValues / regarding pre-seed — decision + documented gap

Regarding source precedence: prefer `useLaunchContext()`'s `openTodos` branch (`regardingFilter` — an ACTIVE Kanban filter, the more likely scenario for a mid-session "+ New Task" click) over the `createTodo` branch's `initialRegarding` (present only on the very first render, before `LaunchCreateTodoWizardHost` consumes it for the separate Outlook/ribbon wizard flow). If neither is present, or the launch context's `entityType` is not one of the 12 canonical `sprk_todo` regarding targets (`TODO_REGARDING_CATALOG` in `TodoRegardingUpdateBuilder.ts` — reused verbatim, no invented attribute names), `defaultValues` is `undefined` and the create form opens as a plain, unfilled create (still satisfies the "STILL wire the plain-create path" fallback constraint).

When a valid regarding source IS present, `buildNewTaskDefaultValues()` pre-seeds:

1. **The entity-specific single-valued lookup** (e.g. `sprk_regardingmatter`) — via the Microsoft Learn documented `navigateTo` `data`-param lookup-default shape: `JSON.stringify([{id, entityType, name}])` (the same shape as Microsoft's own `customerid` pre-seed example — a JSON-stringified single-element array).
2. **`sprk_regardingrecordid`** and (when available) **`sprk_regardingrecordname`** — the two plain TEXT resolver fields (ADR-024) — pre-seeded directly as strings, the unambiguous case for the `data` param's documented scalar-attribute default-value support.

### Documented gap (escalation-trigger territory, not silently degraded)

**`sprk_regardingrecordtype`** (the 4th resolver field — a lookup to the `sprk_recordtype_ref` reference table) is **deliberately NOT pre-seeded**. Its target GUID is a reference-table row keyed by entity-type *name*, not the regarding record's own GUID — resolving it would require an extra Web API query this client-side pre-seed step does not perform (out of this task's scope; would need e.g. a `sprk_recordtype_ref` lookup-by-name call before the form even opens). The RegardingResolver control already wired onto the `sprk_todo` form (task 013) is where the user completes/reconciles the full regarding association once the form is open — this pre-seed only needs to give the form a head start, not be complete.

**Unverified-in-this-session**: the entity-specific-lookup `data`-param shape (`JSON.stringify([{id, entityType, name}])`) is applied per the documented Microsoft convention, but this development environment has no live Dataverse/MDA to empirically confirm it actually pre-fills the `sprk_todo` form's polymorphic lookup on the real, deployed form. This is the POML's `<ui-tests>` "Regarding pre-fill when launched with context" scenario — **left for the operator to run against real Dataverse** (per this project's established convention: "`sprk_todo` form XML is NOT in the repo — tasks 013/030/031/032 treat it as an MCP/maker/live target," `CLAUDE.md` codebase-drift reconciliations). If the operator's smoke test shows the lookup does NOT pre-fill, that is a documentation update (remove the lookup pre-seed line, keep the two text fields), not a silent regression — the plain-create path and the two text-field pre-seeds are unconditional regardless of that outcome.

No escalation was raised via CLAUDE.md §6 because the task's own escalation trigger permits documenting the gap rather than forcing a fix, and a plain-create fallback is unconditionally wired — the negative/plain-create acceptance criterion is satisfied either way.

## Verification run (task 030, 2026-08-16)

- `npx tsc --noEmit` in `src/solutions/SmartTodo`: 43 pre-existing errors (confirmed byte-identical via before/after diff with this task's 3 changed files removed — zero new errors introduced). All 43 are pre-existing, unrelated to this task (`Spaarke.Auth`/`@azure/msal-browser` type resolution, `ComponentFramework` namespace in `Spaarke.UI.Components` PCF-shared files, `SmartToDo.tsx` pre-existing `ITodo`/`IWebApi` type gaps).
- `npx jest` in `src/solutions/SmartTodo`: 7 suites / 128 tests, all green (12 new in `SmartTodoApp.test.tsx`).
- Hex/rgb/`'1px'` grep across all 3 changed files: zero matches.
