# Test diet report — ai-spaarke-ai-workspace-UI-r2

**Run date**: 2026-07-01
**Branch**: `work/ai-spaarke-ai-workspace-UI-r2`
**Scope**: tests touched between commit `367bdb4a1` (project scaffolding) and HEAD (Phase 3 wrap-up)

## Summary

| Class | Count | Action |
|---|---|---|
| **MAINTAIN** (KEEP at canonical path) | 9 tests (1 file) | Confirmed — no action |
| **SCAFFOLDING** (DELETE candidate) | 0 | — |
| **AMBIGUOUS** (reviewer judgment) | 0 | — |
| **PATH-VIOLATION** (wrong KEEP path) | 0 | — |
| **Retirement-delete** (feature deletion, not diet) | 1 file | Already deleted in task 022 as part of FR-14 (see § 4) |
| **Total test files touched** | **2** | 1 added + 1 deleted |

## Enumeration of touched test files

Per `git log 367bdb4a1..HEAD --diff-filter=AMD -- '**/*.test.ts'`:

| Status | File | Reason |
|---|---|---|
| **A** (added) | `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/__tests__/DataGrid.recordOpen.test.ts` | Task 002 (Phase 1) — unit tests for `buildRecordOpenNavArgs` helper (9 tests mapping FR-01/FR-02/FR-03/FR-20) |
| **D** (deleted) | `src/solutions/SmartTodo/src/components/Modal/__tests__/buildTodoIframeUrl.test.ts` | Task 022 (Phase 3) — deleted with the entire retired `components/Modal/` folder per FR-14 (SmartTodoModal iframe retirement). Source file (`buildTodoIframeUrl.ts`) also deleted — tests would not compile if kept |

## 1. MAINTAIN — confirmed (no action)

### File: `src/client/shared/Spaarke.UI.Components/src/components/DataGrid/__tests__/DataGrid.recordOpen.test.ts`

**Path check**: Colocated `__tests__/` under a shared-library component. TypeScript tests do not use the same C# KEEP-path structure as ADR-038 §7's `tests/integration/{auth,regression,data-mutation,tenant,contract}` — for TypeScript, colocated unit tests for pure exported helpers ARE the KEEP pattern. ✅

**All 9 tests pass every heuristic**:

| Test name | Assertion | Ban check |
|---|---|---|
| "emits exactly target=2, position=1, 85% width, 85% height" | Exact literal shape of `LAYOUT_1_NAV_OPTIONS` | Behavioral (FR-20 contract). Not B10 coverage-filler. |
| "emits identical nav options regardless of entity" | Deep-equality across 3 entity calls | Behavioral (framework contract). Not B6 mirror. |
| "sets pageType=entityrecord and forwards entityName + entityId" | Exact field values on pageInput | Behavioral. Not B10. |
| "strips curly braces from recordId" | Behavior of GUID normalization | Edge-case behavior. Not B14 language-feature. |
| "omits formId from pageInput when rowOpen is undefined" | Structural + `'formId' in pageInput` check | Contract invariant. Not B10. |
| "omits formId from pageInput when rowOpen.formId is absent" | Same as above, empty object arg | Contract invariant. |
| "forwards rowOpen.formId as pageInput.formId when set" | Exact GUID roundtrip | Behavioral (FR-01/FR-02 contract). |
| "honors formId across every entity uniformly" | Cross-entity behavior stability | Framework contract. Not B6 mirror. |
| "tolerates null rowOpen" | Null-safety guarantee | Edge-case behavior. Not B3 null-check wiring. |

**Ban screen (per heuristic order)**:

- **B1** `Mock<HttpMessageHandler>` — N/A (no mocks; pure function)
- **B2** All-mocks-trivial — N/A (0 mocks)
- **B3** Constructor null-check — N/A (helper is not a class)
- **B4** DI-registration test — N/A
- **B5** Wiring-only — no
- **B6** Mirror (1:1) — no; tests exercise multi-argument permutations, not method mirror
- **B7** Mock-heavy — 0 mocks
- **B8** Reflection / internal-access — no
- **B9** Pass-through Verify.Once() — no
- **B10** `NotThrow()` / `NotNull()` only — no; every test has concrete value assertions
- **B11** Record equality — no
- **B12** Snapshot trivial — no
- **B13** Name without scenario — no (every name follows `{action}_{condition}_{result}` shape)
- **B14** Language feature — no
- **B15** Setup:assert > 10:1 — no (arrange ~2 lines, assert ~2 lines)
- **B16** Getter/setter round-trip — no
- **B17** Generated mapper — no

**Classification: MAINTAIN** — 9 tests × MAINTAIN. Keep at current path.

## 2. SCAFFOLDING — DELETE candidates

**None.** No tests added during R2 fit any of the 17 bans.

## 3. AMBIGUOUS — reviewer judgment

**None.**

## 4. Retirement-delete (feature-deletion, not diet-driven)

### File: `src/solutions/SmartTodo/src/components/Modal/__tests__/buildTodoIframeUrl.test.ts` (deleted)

This file was deleted in task 022 (Phase 3) as part of FR-14 — the R4 iframe-hosted `SmartTodoModal` and all of `components/Modal/` were retired per MS Learn's 2025-05-07 "iframe-embedded form not supported" statement (see [notes/researcher-iframe-main-aspx-2026-07-01.md](researcher-iframe-main-aspx-2026-07-01.md)). The source it tested (`buildTodoIframeUrl.ts`) was also deleted; keeping the tests would fail on broken imports.

This is NOT a diet-driven delete (the tests were not scaffolding at the time they were written — they exercised real URL-construction behavior for the R4 iframe pattern). It is a **retirement delete**: the feature they tested is gone; the tests go with it.

No further action required.

## Path-move commands

None. No path violations found.

## Delete commands (DO NOT auto-execute)

None from diet. The one deletion (`buildTodoIframeUrl.test.ts`) was already executed in task 022 as feature retirement.

## Count delta

- Tests added during project: **9** (all in `DataGrid.recordOpen.test.ts`)
- Tests classified MAINTAIN: **9**
- Tests classified SCAFFOLDING: **0**
- Tests classified AMBIGUOUS: **0**
- Tests removed via retirement (not diet): **~16** (`buildTodoIframeUrl.test.ts` had multiple `it()` blocks — not counted here since they are feature-driven removal, not diet)
- **Net post-diet expected count**: +9 (all tests added stay)

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1-B17. This project's test additions all fall in the "behavior tests at framework-contract seams" category per Feathers.

## Wrap-up PR description note

Include in the wrap-up PR body: **"test-diet: PASS (9 MAINTAIN, 0 SCAFFOLDING, 0 AMBIGUOUS; report at [`notes/test-diet-report.md`](../notes/test-diet-report.md))"**.

Or, since Phase 1 + 2 + 3 already folded into PR #530 per operator direction, the equivalent note goes into the PR's wrap-up comment.
