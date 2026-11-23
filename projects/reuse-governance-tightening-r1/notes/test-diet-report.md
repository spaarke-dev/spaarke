# Test diet report — reuse-governance-tightening-r1

**Run date**: 2026-11-17
**Branch**: `work/reuse-governance-tightening-r1`
**Scope**: test files changed by this project's non-merge commits (`git log --no-merges --grep=reuse-governance-tightening-r1`), from `a0c2a0e9fc` (2026-11-10) to HEAD. That gives 21 files: 1 C# ArchTest and 20 jest files.

## Summary

| Class | Count | Action |
|---|---|---|
| FITNESS FUNCTION (KEEP) | 8 methods in 1 file | confirmed |
| MAINTAIN (moved verbatim, behavioural) | 20 tests in 1 file | confirmed |
| AMBIGUOUS (reviewer judgment) | 1 test | keep (see below) |
| SCAFFOLDING (delete candidate) | 0 | none |
| ORPHAN | 1 file, already deleted by the project | evidence below |
| DETACHED | 0 | — |
| PATH-VIOLATION | 0 | — |
| Mock-path edits only (methods not changed) | 18 files | swept at file level, see below |

The project added no new test methods. It moved one test file and deleted its old copy, edited three census rows in an ArchTest, and changed module-mock paths in 18 jest files after it retired three packages.

## Delete commands

None. No scaffolding found.

## Path-move commands

None. ADR-038's KEEP paths cover `tests/**`. The jest files live next to their package source, which is the client convention, and the project moved none of them except the consolidation below.

## Fitness function — `tests/Spaarke.ArchTests/SharedPackageCensusTests.cs` (heuristic 0, KEEP)

Tasks 031–033 each replaced one `CensusEntry` with a "retired" comment (`cdedecefd6`, `c9da4dd556`, `2679e94bc7`). All 8 methods are ADR-012 structural invariants: four rules, three negative controls, one positive control. Last verified 8/8 green at task 080.

## Moved — `useDocumentActions.test.tsx`

`src/client/shared/Spaarke.DocumentOperations/__tests__/hooks/useDocumentActions.test.tsx` (387 lines, deleted) → `src/client/shared/Spaarke.UI.Components/src/hooks/__tests__/useDocumentActions.test.tsx` (384 lines, added). Task 033, `2679e94bc7`, consolidated `@spaarke/document-operations` into `@spaarke/ui-components`.

- **ORPHAN evidence (old file):** the subject package folder `src/client/shared/Spaarke.DocumentOperations/` was deleted in the same commit. The behaviour moved to `src/client/shared/Spaarke.UI.Components/src/hooks/useDocumentActions.ts`, and the tests moved with it in the same PR (#2843). No route or security path was removed. The census row was retired in the same commit.
- **MAINTAIN (20 tests):** each test drives the real hook against a mocked fetch boundary and asserts behaviour a user sees: which URL opens, the filename taken from `Content-Disposition`, DELETE per id, no request after a cancelled confirm, a 202 treated as success, and the seven thrown-failure messages ("temporarily unavailable", "not found", the sign-in message, "wait"). These are behavioural scenarios, not B6/B9/B10 shapes.
- **AMBIGUOUS (1):** `hook surface › initial state — isActing=false, actionError=null` asserts only the initial state (close to B10/B16). It predates this project and was moved verbatim. **Suggestion: keep.** It is cheap and pins the contract the action frame reads, and deleting it is not a call for a move-only change.

## Mock-path edits only — swept at file level

These 18 files changed only `jest.mock(...)` targets or the comments next to them. The `@spaarke/document-operations` / `@spaarke/ai-context` module mocks moved to `@spaarke/ui-components` / `@spaarke/ai-widgets`, between 1 and 9 lines per file. No test method body changed.

- `src/client/shared/Spaarke.Compose.Components/src/widgets/ComposeWorkspace.*.test.tsx`: 13 files (activeDocShare, bornInEditorSave, changeSummary, imports, memoEmailHandlers, openInWordError, renderOnSave, saveErrorRouting, saveLifecycle, saveOpLogPreservation, search, unmountFlush, upload)
- `src/client/shared/Spaarke.AI.Widgets/src/__tests__/sse-event-routing.test.tsx`, `src/client/shared/Spaarke.AI.Widgets/src/providers/__tests__/AiSessionProvider.test.tsx`
- `src/client/code-pages/SemanticSearch/src/__tests__/components/App.documentActionError.test.tsx`
- `src/solutions/SpaarkeAi/src/__tests__/compose/ComposeToolbar.test.tsx`

**Deviation from the touch-radius rule, recorded:** the methods in these files were not classified one by one. Compose alone has 112 suites and 1,457 tests (task 080 fix verification), and the project's edit was the module path, not test logic. A per-method diet of Compose belongs to the Compose owner (R7), not to a package-retirement change.

## Reliability registry

None of the touched files or methods are in `tests/.reliability-registry.json`. No stale entry to remove.

## Count delta

- Tests added: 0 new. 20 moved, 20 deleted at the old path.
- Net change in test count: 0.
- Scaffolding removed: 0.

## Citation

Build-vs-maintain criteria per ADR-038 §7 (B1–B17); Amendment A1 (ArchTests KEEP path); Amendment A3 (orphan evidence).
