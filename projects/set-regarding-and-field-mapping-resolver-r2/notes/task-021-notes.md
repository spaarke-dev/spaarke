# Task 021 — Wire engine into todo + workAssignment services — Notes

**Completed**: 2026-07-09 · Rigor: FULL · Model: sonnet@high · Verdict: COMPLETED
**Parallel wave**: Group B, alongside tasks 020 (event/matter/project) and 022 (invoice/reportCard). Only
`todoService.ts` and `workAssignmentService.ts` (plus one call-site file, see below) were touched.

## What was done

Wired the Field Mapping Framework engine (`applyFieldMappings` from
`services/FieldMappingService.ts`) into the create paths of `TodoService.createTodo` and
`WorkAssignmentService.createWorkAssignment`, mirroring task 020's contract: call the engine
AFTER `applyResolverFields` / the resolver block and BEFORE `createRecord`, on the same payload
object; append engine warnings to the service's existing warnings; never abort on engine failure;
no behavior change when `profileFound:false`.

### Files changed
- **`src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/todoService.ts`**
  — added optional `authenticatedFetch`/`bffBaseUrl` constructor params, a `warnings: string[]`
    accumulator, the engine call (guarded on a resolved regarding parent + injected deps), and a
    new optional `warnings?: string[]` field on `ICreateTodoResult`.
- **`src/client/shared/Spaarke.UI.Components/src/components/CreateWorkAssignmentWizard/workAssignmentService.ts`**
  — stored the already-injected `authenticatedFetch`/`bffBaseUrl` constructor params as private
    fields (previously forwarded only to `EntityCreationService`, never retained), and added the
    engine call inside the existing `if (refMapping) { … applyResolverFields(...) }` block.
- **`src/client/shared/Spaarke.UI.Components/src/components/CreateTodoWizard/TodoWizardDialog.tsx`**
  (one line) — updated the single `new TodoService(dataService)` call at the wizard's `onFinish`
    to `new TodoService(dataService, authenticatedFetch, bffBaseUrl)`. Both were already
    destructured props at that call site (used at line ~242 for `EntityCreationService`), so this
    is a zero-new-dependency wiring, not scope expansion. Without this, the primary CreateTodoWizard
    flow could never actually invoke the engine (see design gap note below) — so this one-line edit
    was necessary to satisfy acceptance criterion 2 ("mapped fields appear with a matching profile"),
    not just criterion 1 ("calls the engine after resolver, before createRecord").

## Design gap found + how it was resolved

Task 020's notes assume "WizardHostProps already injects dataService/authenticatedFetch/bffBaseUrl"
into every Create*Wizard service. That's true for `eventService`/`matterService`/`projectService`/
`invoiceService`/`reportCardService`/`workAssignmentService` — but **not** for `TodoService`, whose
constructor took only `dataService` before this task. Two call sites construct it:

1. **`TodoWizardDialog.tsx`** (primary "Create New To Do" wizard finish handler) — `authenticatedFetch`/
   `bffBaseUrl` ARE already available as props at that call site. **Updated** (see above) — this is
   the real production wiring point for standalone To Do creation.
2. **`WizardFollowOns/steps/AddTodoFollowOnStep.tsx` → `createTodoRegardingChild`** (the "Add a To Do"
   follow-on offered by CreateEventWizard/CreateInvoiceWizard/CreateReportCardWizard/etc. after they
   create their own record) — this function's signature has no `authenticatedFetch`/`bffBaseUrl`
   parameters today, and its 2 callers (`CreateInvoiceWizard.tsx:550`, `CreateReportCardWizard.tsx:542`)
   are components owned by the concurrently-running task 022, not task 021. **Left unwired** —
   `new TodoService(dataService)` there still resolves to the 2-arg fallback, so the engine call is
   skipped as a graceful no-op (same code path as "profile not found"). This is a known, deliberate,
   documented scope boundary — not a bug — kept out of scope to avoid touching invoice/reportCard
   wizard files during the parallel wave. A follow-up task should thread `authenticatedFetch`/
   `bffBaseUrl` through `createTodoRegardingChild` + its 2 call sites if follow-on To Dos should also
   get field-mapping treatment.

To make this safe, `TodoService`'s new `authenticatedFetch`/`bffBaseUrl` constructor params are
**optional** (unlike `WorkAssignmentService`, where they were already required/present at every call
site). When either is missing, the engine call is skipped entirely (no fetch attempted, no warning
recorded) — this preserves 100% backward compatibility with the ~14 existing 1-arg
`new TodoService(dataService)` test call sites and the follow-on path above.

## Insertion-point code shape

**`todoService.ts`** (inside `if (regarding && regarding.entityType && regarding.recordId) { … }`,
after the `applyResolverFields` try/catch closes, still before "4. Create the record"):
```ts
if (this._authenticatedFetch && this._bffBaseUrl) {
  const mappingResult = await applyFieldMappings({
    sourceEntity: catalogEntry.entityType,
    sourceId: regarding.recordId,
    targetEntity: 'sprk_todo',
    payload: entity,
    dataService: this._dataService,
    authenticatedFetch: this._authenticatedFetch,
    bffBaseUrl: this._bffBaseUrl,
  });
  warnings.push(...mappingResult.warnings);
}
```

**`workAssignmentService.ts`** (inside `if (refMapping) { … }`, immediately after
`await applyResolverFields(...)`, before the block closes and before the matterType/practiceArea
`bindLookup` calls / the `createRecord` try block further down):
```ts
const mappingResult = await applyFieldMappings({
  sourceEntity: refMapping.refEntity,
  sourceId: form.recordId,
  targetEntity: 'sprk_workassignment',
  payload: entity,
  dataService: this._dataService,
  authenticatedFetch: this._authenticatedFetch,
  bffBaseUrl: this._bffBaseUrl,
});
warnings.push(...mappingResult.warnings);
```

## Constraint confirmations

- **Engine called after resolver / before createRecord, same payload object** — confirmed for both
  services (code shapes above).
- **Warnings appended, not replaced** — `todoService` now has a `warnings: string[]` accumulator
  (didn't exist before this task — `ICreateTodoResult` had no warnings field at all) that the engine
  result is pushed onto; `workAssignmentService` pushes onto its pre-existing `warnings` array.
- **No abort-on-failure try/catch** — the engine call in both services is NOT wrapped in any
  try/catch (it sits either after the `applyResolverFields` try/catch closes (todoService) or
  entirely outside any try/catch (workAssignmentService)); `applyFieldMappings` itself never throws
  per its task-010 contract.
- **No hard-coded target field names** — `targetEntity` is a literal entity logical name
  (`'sprk_todo'` / `'sprk_workassignment'`), not a field name; the engine resolves rule-level field
  names entirely from the fetched profile.
- **`TodoRegardingUpdateBuilder.ts` / `TODO_REGARDING_CATALOG` untouched** — confirmed via
  `git diff --stat` (empty diff for that file) and via `grep` on the todoService.ts diff: the only
  occurrences of `TODO_REGARDING_CATALOG` are the pre-existing import line and a doc-comment
  cross-reference; the catalog is only *read* (`catalogEntry.entityType`), never mutated.

## Verification

- **`npx tsc --noEmit`** (from `src/client/shared/Spaarke.UI.Components`): first run surfaced 1 error
  in `CreateReportCardWizard.tsx` and a second run surfaced 2 errors in `projectService.ts` — both
  transient artifacts of tasks 020/022 mid-editing their own files concurrently in the same worktree
  (confirmed via `git status --short`, which showed `eventService.ts`, `invoiceService.ts`,
  `projectService.ts`, `reportCardService.ts`, `CreateReportCardWizard.tsx` all modified by the
  parallel agents). **Zero errors** referenced `todoService.ts`, `workAssignmentService.ts`, or
  `TodoWizardDialog.tsx` in either run (`grep -E "todoService|workAssignmentService|TodoWizardDialog"`
  on the tsc output → "NO ERRORS IN MY FILES"). Per the dispatch instructions, the main session runs
  the authoritative build after Wave B completes.
- **`npx jest todoService workAssignmentService TodoRegarding`**:
  ```
  PASS src/components/CreateWorkAssignmentWizard/__tests__/workAssignmentService.cascade.test.ts
  PASS src/components/CreateTodoWizard/__tests__/todoService.test.ts
  PASS src/services/__tests__/TodoRegardingUpdateBuilder.test.ts
  Test Suites: 3 passed, 3 total
  Tests:       53 passed, 53 total
  ```
  The `TodoRegardingUpdateBuilder.test.ts` pass confirms the regarding-catalog mechanism is
  undisturbed.

## Quality gates (Step 9.5)

**code-review** (self-applied against the 3-file diff; skill invocation returned the procedure body
rather than executing autonomously, so the review was performed directly against the checklist):
- **Correctness of insertion point**: Compliant for both services (see code shapes above).
- **No hard-coded field names**: Compliant.
- **Graceful no-op**: Compliant. Two flavors: (a) engine returns `profileFound:false` on 404 — handled
  inside `applyFieldMappings` itself, unchanged by this task; (b) todoService additionally no-ops when
  `authenticatedFetch`/`bffBaseUrl` are absent (the follow-on-path gap above) — Suggestion: this is a
  deliberate, documented scope boundary, not a defect, but flagged for a follow-up task to close.
- **No behavior change to existing callers**: Compliant — `TodoService`'s 2 new params are optional
  and appended after the existing required one; `WorkAssignmentService`'s constructor signature is
  unchanged (params were already there, just newly retained as fields).
- **AI code smells**: none introduced — no new interfaces, no try/catch-log-rethrow, no null-checks
  on non-nullable types (the `this._authenticatedFetch && this._bffBaseUrl` guard checks genuinely
  optional/nullable fields), comments explain the wiring contract rather than restating code.
- **Quantitative**: `todoService.ts` 237 lines (was ~190), `workAssignmentService.ts` 695 lines (was
  ~672, largely task 011's nav-prop consolidation, not this task) — both under/near the review's size
  thresholds; `WorkAssignmentService`'s constructor now has 2 additional private fields but no new
  constructor parameters (0 new ctor params — the 2 fields reuse already-injected params).
- **Verdict**: CLEAN — 0 Critical, 0 Warning (beyond the documented follow-on-path scope note above),
  1 Suggestion (thread `authenticatedFetch`/`bffBaseUrl` through `createTodoRegardingChild` in a
  future task so follow-on-created To Dos also get field-mapping treatment).

**adr-check** (self-applied):
- **ADR-012** (context-agnostic, no `ComponentFramework`/PCF types, injected `IDataService` +
  `authenticatedFetch`): ✅ Compliant. No PCF imports added; `TodoService`'s new params are injected
  (optional) dependencies, consistent with the pattern; `applyFieldMappings` itself already
  established as context-agnostic in task 010.
- **ADR-024** (polymorphic resolver pattern): ✅ Compliant. The engine call is strictly additive and
  ordered after `applyResolverFields` in both services; it does not modify, wrap, or duplicate
  resolver logic.
- **ADR-028** (Spaarke Auth v2 client contract): ✅ Compliant. `authenticatedFetch` is passed through
  as an injected function; no raw `fetch` + `Authorization` header, no `tokenBridge`, no
  `accessToken: string` props introduced.
- **§10 BFF Hygiene**: N/A confirmed — all 3 changed files are under `src/client/shared/...`; no BFF
  endpoint/service/DI/package changes.
- **§11 Component Justification**: N/A — no new files, interfaces, DI registrations, or Dataverse
  columns; only optional-parameter additions to two existing constructors.
- **Verdict**: CLEAN — 0 violations, 0 warnings.

## Notes for downstream tasks

- Task 040 (Architecture doc + CLAUDE.md pointer) should mention the `TodoService` constructor-deps
  asymmetry (optional vs. `WorkAssignmentService`'s always-present) and the follow-on-path gap
  (`createTodoRegardingChild` not yet wired) as a known limitation of the Phase 2 wiring wave.
- If a future task closes that gap, the change is: add `authenticatedFetch`/`bffBaseUrl` params to
  `createTodoRegardingChild` (`WizardFollowOns/steps/AddTodoFollowOnStep.tsx`) and thread them from
  its 2 call sites (`CreateInvoiceWizard.tsx:550`, `CreateReportCardWizard.tsx:542`, both of which
  already have `authFetch ?? fetch.bind(window)` / `bffBaseUrl ?? ''` in local scope for their own
  `WorkAssignmentService`/`InvoiceService`/`ReportCardService` construction).
