# Test diet report — ai-advanced-capabilities-nda-r1

**Run date**: 2026-07-28
**Branch**: work/ai-advanced-capabilities-nda-r1 (all work merged to origin/master; worktree 0/0)
**Scope**: test files added/modified during the project (first commit `0a4d08f42` → HEAD)
**Gate**: project-close per CLAUDE.md §7 + ADR-038 §7 (build-vs-maintain, B1–B17)

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 40 | confirmed |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 | — |
| **Total test files touched** | **40** | — |

## Verdict: ALL MAINTAIN — no deletions, no path moves

The project added **no scaffolding-class tests**. Two clean reasons:

1. **C# tests are already at ADR-038 §7 KEEP paths.** Every C# test the project added lives under
   `tests/integration/seam/**` (the vertical-slice-seam category — the MUST-keep DoD for dispatch-spine
   changes, added by E-40) or `tests/integration/contract/**`:
   - `tests/integration/seam/Ai/ModelTierOverrideDispatchSeamTests.cs` — model-tier override routing (task 011/010)
   - `tests/integration/seam/Ai/NdaReviewFanOutSeamTests.cs` — whole-doc review fan-out (task 023)
   - `tests/integration/seam/Ai/ComposeImportedAnchorsSurviveSaveSeamTests.cs` — anchor round-trip (task 040/042)
   - `tests/integration/seam/Compose/ComposeSummaryPageSeamTests.cs` — Summary-Page writer (task 041)
   - `tests/integration/seam/Compose/ConcurrencySaveSeamTests.cs` — SPE save concurrency (task 042)
   - `tests/integration/contract/Eval/NdaReviewDispatchEvalTests.cs` — golden-utterance dispatch eval (task 051)

   These are exactly the maintain-class, real-seam integration tests ADR-038 prescribes. None use
   `Mock<HttpMessageHandler>` (B1), DI-registration assertions (B3), ctor null-checks (B4), or any B1–B17 ban.

2. **TypeScript tests are client component-behavior tests** (React Testing Library render/interaction/state).
   The B1–B17 classifier is C#/xUnit-oriented (mock-shape, `GetRequiredService`, `ArgumentNullException`
   ctor checks) and its B-path rule is scoped to the C# `tests/**` tree; co-located `*.test.tsx` client
   tests are outside that scheme by repo convention. Each asserts a real user-visible scenario, not a
   mirror/wiring artifact. Representative:
   - `NdaReviewProgressModal.test.tsx` — progress popup states (idle/running/complete/error, rotating phrase)
   - `ThreePaneLayout.statePreserved.test.tsx` — panes keep-mounted-hidden on collapse (UAT #2/#3 regression)
   - `WorkspacePane.compose-multi-tab.test.tsx` — tab independence (UAT #1 regression)
   - `ComposeEditor.advisoryComments.test.tsx` / `useNdaReviewAdvisoryCommentsBridge.test.ts` — advisory-comment materialization (task 031)
   - `NdaReviewSummaryPanel.test.tsx` — review summary panel (task 030)
   - `useComposeToolbarActivation.test.tsx` / `ComposeAiToolbar.test.tsx` — contextual tool library activation
   - `useNdaReviewRunProgress.test.ts` — progress state machine

   Every UAT bug this project fixed shipped with a regression test (the "every bug = a regression test" rule).

## Delete commands
_None._

## Path-move commands
_None — C# tests already at canonical KEEP paths; TS client tests co-located per repo convention._

## Ambiguous — reviewer judgment
_None._

## Count delta
- Test files touched during project: 40
- MAINTAIN: 40 · SCAFFOLDING: 0 · AMBIGUOUS: 0
- Net post-diet expected count: unchanged (40)

## Industry citation
Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior;
Google test-sizes). 17-ban classifier B1–B17. This project's deltas are behavior + seam + contract tests —
the classes ADR-038 explicitly retains.
