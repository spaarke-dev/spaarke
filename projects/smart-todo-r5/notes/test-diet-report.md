# Test diet report — smart-todo-r5

**Run date**: 2026-08-17
**Branch**: work/smart-todo-r5
**Scope**: test files touched by smart-todo-r5's OWN commits (`--no-merges --grep="smart-todo-r5"`, excluding the ~120 `.cs` contract tests that entered via master merges from compose-r7 et al.)
**Classifier**: ADR-038 §7 17-ban list (B1–B17) — `.cs` scope. TS jest/e2e assessed separately (governed by their own conventions, outside the .NET classifier).

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP) | 1 file (.cs) + 16 TS jest + 3 e2e | confirmed |
| SCAFFOLDING (DELETE) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 2 methods (.cs) | listed below → resolved KEEP |
| PATH-VIOLATION | 1 file (.cs, pre-existing) | noted, out of scope |
| Already-clean removal | FilterPane.test.tsx | deleted with its component (D-7) — no orphan |

## `.cs` classifier scope — 1 file

`tests/unit/Sprk.Bff.Api.Tests/Services/Workspace/TodoGenerationServiceTests.cs` — **MODIFIED** (not created) by this project for the INBOUND reroute. Pre-existing `[Trait("status","repaired")]` unit-with-mocks suite.

This project's deltas:
- **Harness update** (mock `IEventDataverseService`, reflection-set `_events`, enabled-options) — re-points the existing 26 tests' mocks after the reroute so they stay green. Intent unchanged → **MAINTAIN** (not this project's to re-classify).
- **2 NEW tests** (lines 729, 761):
  - `RunGenerationPass_Rule1_WhenEventSourcedDisabled_QueriesEventsButCreatesNothing` — dry-run gate behavior.
  - `RunGenerationPass_EventSourcedRules_QueryEventsViaEventService_NeverViaComposite` — **the regression guard for the INBOUND bug** (the reroute must never call the now-throwing composite stub).

### Ambiguous — reviewer judgment (resolved)

| File:Method | Ambiguity | Resolution |
|---|---|---|
| TodoGenerationServiceTests.cs:`…WhenEventSourcedDisabled…` | Behavioral assertion (dry-run creates nothing) but mock-heavy (B7-ish) at a non-KEEP path | **KEEP** — guards a live-deployed correctness gate; behavioral, well-named. |
| TodoGenerationServiceTests.cs:`…NeverViaComposite` | Interaction-verification (`Verify(…Times.Never/Exactly)`, B7-ish) at a non-KEEP path | **KEEP** — genuine regression guard ("every bug = regression test"); it literally proved the reroute co-exists safely with r3's stub→throw that landed on master 2026-08-17. Deleting it would remove protection for a just-shipped fix. |

**Path note (not actioned):** the whole suite lives at `tests/unit/Sprk.Bff.Api.Tests/…`, not `tests/integration/regression/…`. It predates the KEEP-path convention; the ideal home for the 2 new guards is an integration/regression test (real `WebApplicationFactory` + `FakeTimeProvider`). Migrating the pre-existing `[status=repaired]` suite is **out of this project's scope** — flagged for a future BFF-test-architecture pass, not a delete/move here.

## TS surface (outside the `.cs` 17-ban classifier)

16 jest files + 3 e2e specs + page object. All test real logic/UI behavior — **maintain-class**:
- Logic: `todoSearchUtils` (date-search matching), `todoScoreMappings`, `queryHelpers`, `oobModalSizes`, `useKanbanColumns`.
- UI: `SearchFilter`, `Header`, `ToolbarActions`, `SmartTodoApp`, `priorityEffortCardUi`, `SmartTodoWidget`, `RegardingResolverApp`, `wizardLaunchers`, `useLaunchContext`, `openTodoLauncher`.
- e2e (041): `SmartTodoPage` + performance/accessibility/orientation specs — NFR maintain-class; **authored + typecheck + wired**, not run green (real-env deferred, D-12). Not deleted — they're the deliverable.
- **`FilterPane.test.tsx`** — already deleted with the FilterPane component (D-7 filter→text-search). Clean removal, no orphan.

## Verdict

**Nothing to delete.** 0 scaffolding. The 2 new `.cs` tests are AMBIGUOUS-but-KEEP (real regression guards for a deployed fix). 1 pre-existing path-violation noted (out of scope). TS surface is maintain-class. `FilterPane` test already cleaned up with its component.

## Count delta
- Test methods added by this project (.cs): 2 → both KEEP.
- Scaffolding deleted: 0.
- Post-diet net .cs count: unchanged (+2).

## Industry citation
ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17.
