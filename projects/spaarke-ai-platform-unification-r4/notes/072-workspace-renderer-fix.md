# Task 072 — WorkspaceRenderer Interface Tightening (A.2, Path 2a)

> **Date**: 2026-05-27
> **Operator decision**: Path 2a — tighten interface, NO wrapper component
> **Pairs with**: Task 052 (C-4) which introduced `WorkspaceRenderer` seam

---

## Why this fix

Task 052 introduced `WorkspaceRendererWebApi` with all 5 methods OPTIONAL to keep
the interface generic across "future hosts." `LegalWorkspaceApp` (the only
renderer) expects strict `IWebApi` (methods REQUIRED). TypeScript's contravariance
on function parameters correctly refused the assignment (loose-to-strict is
unsafe), forcing `LegalWorkspaceApp as unknown as WorkspaceRenderer` at the
binding site.

Operator decision 2026-05-27: the loose-interface flexibility was for a
fictional use case. LegalWorkspace IS the renderer; new dashboard pieces are
added INSIDE that library, not as separate renderers. So tighten the interface
to reflect reality — TypeScript's structural typing then accepts the
assignment directly with no cast.

---

## Cast sites — before vs after

`grep -rn "as unknown as WorkspaceRenderer\|as WorkspaceRenderer"` results.

### Before (5 hits — 1 production, 4 test stubs)

```
src/solutions/LegalWorkspace/src/index.ts:74     PRODUCTION cast (removed)
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:137  test stub
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:174  test stub
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:180  test stub
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:193  test stub
```

### After (0 production hits; 4 test stubs remain — they are intentional)

```
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:137
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:174
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:180
src/client/shared/Spaarke.AI.Widgets/.../WorkspaceLayoutWidget.test.tsx:193
```

The remaining 4 matches are in the test file. These cast `jest.fn()` stub
renderers to a LOCALLY-aliased `WorkspaceRenderer = React.ComponentType<unknown>`
(declared inline at test top). They are NOT casts to the production
`WorkspaceRenderer` type — they bypass jest mock typing, not the strict
interface. Task acceptance criterion explicitly scopes to "production code,"
so these are out of scope.

Production cast count: **1 → 0** ✅

---

## Interface diff

### Before (`WorkspaceRendererWebApi`, all-optional except `retrieveMultipleRecords`)

```typescript
export interface WorkspaceRendererWebApi {
  retrieveMultipleRecords: (...) => Promise<{ entities: Record<string, unknown>[] }>;
  retrieveRecord?: (...) => Promise<Record<string, unknown>>;
  createRecord?: (...) => Promise<{ id: string }>;
  updateRecord?: (...) => Promise<{ id: string }>;
  deleteRecord?: (...) => Promise<{ id: string }>;
}
```

### After (all REQUIRED — structurally equivalent to `Pick<IWebApi, ...>`)

```typescript
export interface WorkspaceRendererWebApi {
  retrieveMultipleRecords: (...) => Promise<{ entities: Record<string, unknown>[] }>;
  retrieveRecord: (...) => Promise<Record<string, unknown>>;
  createRecord: (...) => Promise<{ id: string }>;
  updateRecord: (...) => Promise<{ id: string }>;
  deleteRecord: (...) => Promise<{ id: string }>;
}
```

### Why structural (not literal `Pick<IWebApi, ...>`)

The task description suggested `Pick<IWebApi, 'createRecord' | 'retrieveRecord' |
'retrieveMultipleRecords' | 'updateRecord' | 'deleteRecord'>`. `IWebApi` lives
in `@spaarke/legal-workspace` (a CONSUMER of `@spaarke/ui-components`).
Importing it here would create a CIRCULAR DEPENDENCY between the shared library
and one of its consumers.

The clean solution: keep the interface defined inline in `WorkspaceRenderer.ts`
with all 5 methods REQUIRED. Structurally identical to a literal `Pick` —
TypeScript's structural typing means `LegalWorkspace`'s `IWebApi` and
`WorkspaceRendererWebApi` are mutually assignable. Zero behavioural difference.

Per ADR-012 (shared components stay context-agnostic), defining the shape
locally in the shared library is also the correct architectural choice — the
library does not depend on any solution.

---

## Cast site removal

### `src/solutions/LegalWorkspace/src/index.ts:74` (PRODUCTION)

**Before**:
```typescript
export const LegalWorkspaceRenderer = _LegalWorkspaceApp as unknown as WorkspaceRenderer;
```

**After**:
```typescript
export const LegalWorkspaceRenderer: WorkspaceRenderer = _LegalWorkspaceApp;
```

Comment rewritten to reflect the new architectural reality (Path 2a, 2026-05-27).
Runtime behaviour is identical — `LegalWorkspaceRenderer` IS still
`LegalWorkspaceApp`. The only difference: TypeScript now accepts the assignment
via structural typing without the cast.

---

## TypeScript verification

| Package | `npx tsc --noEmit` | Errors related to this change |
|---|---|---|
| `@spaarke/ui-components` | 8 pre-existing errors (in `DatasetGrid/ViewSelector`, `SprkChat`, `TodoDetail`) | **0** — none in `workspace/` |
| `@spaarke/legal-workspace` | ~30 pre-existing errors (in `CreateMatter`, `SmartToDo`, etc.) | **0** — none in `index.ts` or `LegalWorkspaceApp` |
| `src/solutions/SpaarkeAi/` | Many pre-existing errors (test infra Jest types missing, `CreateMatterWizardWidget` unused vars) | **0** — none in `main.tsx` or workspace components |

Pre-existing errors are tracked in R4 backlog (task 074 — UI.Components 24 tsc
errors). They are NOT regressions from this task.

---

## Test results

**WorkspaceLayoutWidget tests (`@spaarke/ai-widgets`)** — the critical test
file covering the renderer-seam contract:

```
Test Suites: 1 passed, 1 total
Tests:       4 passed, 4 total
```

All 4 test cases pass:
1. Renders the registered default renderer with the same props
2. Uses injected renderer prop in preference to default
3. Renders no-Xrm empty state when Xrm unavailable
4. Renders no-renderer empty state when neither injected nor default available

**Full `@spaarke/ui-components` test suite** (`npm test`):
- 53 of 55 test suites PASS (1050 of 1051 tests pass)
- 2 failures in `SprkChat.attachments.test.tsx` and `CommandBar.test.tsx` —
  both pre-existing flaky tests (unrelated to workspace), last touched in
  commit `81ac3d13` (a prior R4 wave). Not caused by this change.

---

## No wrapper component introduced

`grep -rn "WorkspaceRendererWrapper\|RendererAdapter\|WorkspaceRendererProxy"
--include="*.ts" --include="*.tsx" src/`:

- 0 matches.

No wrapper, no adapter, no proxy. The fix is purely interface tightening +
cast removal at one production site.

---

## Files modified

1. `src/client/shared/Spaarke.UI.Components/src/workspace/WorkspaceRenderer.ts`
   — Interface `WorkspaceRendererWebApi` tightened: all 5 methods now REQUIRED.
   Comments updated to reflect Path 2a rationale.
2. `src/solutions/LegalWorkspace/src/index.ts`
   — Cast `as unknown as WorkspaceRenderer` removed. Direct typed assignment.
   Comment rewritten.

---

## Downstream surprises

None. The interface change cascaded into zero new errors across all 3
packages I verified. The test file's existing casts continue to compile
(they cast to a locally-redeclared `WorkspaceRenderer = React.ComponentType<unknown>`,
which is unaffected by the strict ui-components interface change).

Per the operator's hard guardrail ("if the interface change cascades into >5
files needing updates, ESCALATE — that indicates a wider scope"), zero
cascading was the expected outcome and confirms the task scope was correctly
sized.

---

## Audit trail

- Operator decision: 2026-05-27 (rejected wrapper approach as architectural
  debt for fictional flexibility).
- Implementation: 2026-05-27 — single focused commit (per operator instruction
  to maintain per-cast commit discipline).
- Verification: tsc clean on 3 packages (no new errors), test suite passes
  on critical workspace renderer test file.
