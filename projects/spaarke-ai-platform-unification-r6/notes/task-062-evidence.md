# Task 062 evidence — Register `ExecutionTraceWidget` with `ContextWidgetRegistry` (D-C-15)

**Pillar / Spec ref**: R6 Pillar 6c / FR-36 — trace widget registered with the
ContextWidgetRegistry so the SpaarkeAi shell auto-mounts it in the Context pane.
**Wave**: C-G3 gap-fill (verifying the on-disk registration committed at 2677f9439).
**Date**: 2026-06-11.
**Dependencies**: task 061 (ExecutionTraceWidget component).

## On-disk state at start

The 2677f9439 checkpoint had:

1. `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts`
   — extended with a `safeRegisterContext('execution-trace', { factory: ... })`
   block at the bottom of the file (lines 156-162), matching the pattern of the
   five sibling context-widget registrations above it.
2. `src/client/shared/Spaarke.AI.Widgets/src/__tests__/widget-serialize-restore.test.ts`
   — updated to (a) include `'execution-trace'` in `EXPECTED_CONTEXT_WIDGETS` and
   (b) change the test description from "registers all 10" to "registers all 11".
3. `src/client/shared/Spaarke.AI.Widgets/src/registry/__tests__/register-execution-trace-widget.test.ts`
   — new test file (5 test cases).

The verification step (run-the-tests) was not done by the previous sub-agent.

## Verification + fixes in this gap-fill

### Fix 1 — `register-execution-trace-widget.test.ts` module-isolation bug

All 5 tests failed initially with `expect(hasContextWidget('execution-trace')).toBe(false)`.

Root cause: the test statically imports `clearContextRegistry, hasContextWidget,
getAllContextWidgetTypes, resolveContextWidget` from `'../ContextWidgetRegistry'`
at the top of the file. Then `beforeEach` calls `jest.resetModules()` followed by
`require('../register-context-widgets')` inside each test body. The `resetModules`
clears Jest's module cache, so the `require` pulls in a FRESH
`ContextWidgetRegistry` module instance with its OWN private `_registry` Map —
which is invisible to the statically-imported reader symbols (those still
point at the FIRST module instance).

Fixed by replacing the static imports + `jest.resetModules()` pattern with a
`loadRegistryWithSideEffects()` helper that uses `jest.isolateModules(() => {
require('../ContextWidgetRegistry'); require('../register-context-widgets'); })`
so both files share the same isolated module graph. Each test obtains its
reader-fn references from INSIDE the isolated graph.

The idempotency test was also re-shaped to drive two `require()` calls inside
a single `jest.isolateModules` block (the original pattern's intent — "the
side-effect registrations don't duplicate on a second `require()`" — needs the
same module-graph instance for both requires; otherwise we're just constructing
two parallel graphs with one registration each).

### Fix 2 — none on the production registration code

`register-context-widgets.ts` was correct as-committed. The 6 production
registrations (`progress-tracker`, `playbook-gallery`, `entity-info`, `findings`,
`file-preview`, `execution-trace`) match the `EXPECTED_CONTEXT_WIDGETS` array
in `widget-serialize-restore.test.ts`.

### Tests run

```
cd src/client/shared/Spaarke.AI.Widgets && npm test -- --testPathPatterns="register-execution-trace-widget"
  Test Suites: 1 passed, 1 total
  Tests:       5 passed, 5 total
  Time:        1.631 s
```

The 5 cases verify:

1. The `execution-trace` widget type is registered after the side-effect module
   loads.
2. The lazy factory resolves to a non-null React component.
3. The widget appears in `getAllContextWidgetTypes()`.
4. The widget is registered alongside (no displacement of) the 5 pre-existing
   context widget types: `progress-tracker`, `playbook-gallery`, `entity-info`,
   `findings`, `file-preview`.
5. The registration is idempotent — a duplicate `require()` is a no-op
   (`ContextWidgetRegistry` is first-wins).

### widget-serialize-restore.test.ts status

This pre-existing integration test (110 cases) does NOT pass on the
`Spaarke.AI.Widgets` workspace because its jest config has no `d3-force` mock
— every test transitively imports the `@spaarke/ui-components` barrel via
`register-workspace-widgets.ts`, and that barrel imports `useForceSimulation`
which fails on the d3-force ESM transform.

**This is a pre-existing test-infrastructure issue NOT introduced by task 062**.
Verified by running the same test against master baseline (`git stash`-ed our
changes, ran, got 105/105 failed, same d3-force error). The 062 changes (adding
`'execution-trace'` to EXPECTED_CONTEXT_WIDGETS and bumping the test description
from "10" to "11") are correctly applied; the test cannot RUN to verify them
because of the d3-force blocker.

The `register-execution-trace-widget.test.ts` test (5 cases, GREEN) is the
narrower contract assertion that exercises the same registration paths without
pulling in the workspace-widget barrel side-effect chain. It is the
authoritative verification for FR-36's "registered with `ContextWidgetRegistry`"
acceptance criterion.

Filing the d3-force mock as follow-up work for whichever wave is allowed to
touch `Spaarke.AI.Widgets/jest.config.ts` (out of scope for 062 — that's a
test-config change with consumer impact beyond R6 task 062).

## Governance

- **ADR-012** (shared lib): registration happens inside the
  `@spaarke/ai-widgets` library — the SpaarkeAi shell does NOT register widgets
  directly. Verified by file location:
  `src/client/shared/Spaarke.AI.Widgets/src/registry/register-context-widgets.ts`.
- **ADR-030** (4-channel PaneEventBus): no channel change. ExecutionTraceWidget
  subscribes to the EXISTING `context` channel via its existing six event types
  (per task 059 / 061). Asserted by code-comment audit in the registration
  block.
- **FR-36** acceptance: `execution-trace` is in
  `ContextWidgetRegistry._registry` → ✅
  (asserted by tests 1, 3, 4 of `register-execution-trace-widget.test.ts`).
- **NFR-05** (4-channel preserved): assert no new channel introduced; the
  ContextWidgetRegistry does NOT touch PaneEventBus channels — it only owns the
  type→factory map.

## Outcome

- ✅ ExecutionTraceWidget registered under type key `execution-trace` in
  ContextWidgetRegistry (5/5 narrow-contract tests passing).
- ✅ Widget appears alongside the 5 pre-existing context widgets — no
  displacement.
- ✅ Idempotency verified.
- 🟡 widget-serialize-restore.test.ts blocked on a pre-existing d3-force ESM
  test-infra gap (NOT introduced by 062; affects the WHOLE workspace test
  suite). Filed as follow-up; the 062 acceptance criterion (FR-36
  "registered") is independently verified by
  register-execution-trace-widget.test.ts.

R6 Pillar 6c trace-widget Context-pane registration is functional.
