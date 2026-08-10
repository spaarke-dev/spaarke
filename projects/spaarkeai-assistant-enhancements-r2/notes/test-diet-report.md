# Test diet report — spaarkeai-assistant-enhancements-r2

**Run date**: 2026-08-10
**Branch**: work/spaarkeai-assistant-enhancements-r2
**Scope**: test files added/modified by R2's own 41 commits (`git log --all --grep="assistant-r2"`), from `de0019359` to `a227f1f60`. R2's test deltas were merged to master across the phase PRs (E/A/B/D/C, PR #743 + UAT merges), so classification spans the project's full life, not just the unmerged tip.
**Gate**: CLAUDE.md §7 project-close test-diet (ADR-038 §7, 17-ban classifier).

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 45 | confirmed — no action |
| SCAFFOLDING (DELETE candidate) | 0 | none |
| AMBIGUOUS (reviewer judgment) | 0 | none |
| PATH-VIOLATION (wrong KEEP path) | 0 | none |
| Compose-domain files R2 touched incidentally (not R2's to diet) | 4 | out of scope — owned by compose-r* |
| **Total test files touched by R2 commits** | **49** | — |

## Headline finding — two orphan-suspects verified LIVE (no dead-code tests)

The highest-value output of a close-out diet is catching tests of deleted code. R2 deleted two surfaces during its life; both suspected orphan tests were checked and are **testing live code** → MAINTAIN, not orphans:

| Suspect test | Why suspected | Verdict | Evidence |
|---|---|---|---|
| `SuggestionCard.test.tsx` | Phase E task 001 removed the proactive-suggestion surface (`useSuggestionCards.tsx` **deleted**) | **MAINTAIN** | `SuggestionCard.tsx` is intentionally **retained** — reused by `useRerunFullAnalysisCard.tsx:31/103` (a separate kept surface). Task 001 deleted only the hook lifecycle; the test's own docblock (lines 5–8) removed the hook-lifecycle `describe` block and documents the retention. Component is live. |
| `useAuthProbe.test.ts` | `useAuthProbe` was framed as the superseded "wrong" auth fix (real fix = the `requireSilentOnly` flag revert) | **MAINTAIN** | `useAuthProbe.ts` is still **wired live** in `App.tsx:184` (`const isAuthenticated = useAuthProbe()`) and is part of the deployed tree. Its test targets live code. (Whether the probe remains the right *design* post-flag-revert is a code-review question, not a test-diet delete — see note below.) |

## Why zero SCAFFOLDING deletions

1. **Path discipline** — every R2 test file lives at a KEEP path or the repo-mandated BFF suite:
   - `tests/integration/contract/**`, `tests/integration/regression/**`, `tests/integration/seam/**`, `tests/integration/Spe.Integration.Tests/**` — ADR-038 KEEP categories.
   - `tests/unit/domain/**` — the unit KEEP path (e.g. `AnalysisRegardingWriteTests.cs`).
   - `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/**` — the BFF §10 **test-update obligation** explicitly requires tests here for `Services/` changes; this is a maintained suite by repo policy, not a path violation.
   - TS: `__tests__/**` colocated component/e2e suites — the SpaarkeAi/shared-lib convention.
2. **Behavioral, named-by-scenario** — R2's suites are scenario tests (`FirstTurnCosmosWriteSurvivesEviction`, `WorkspaceState`, `ContextFilter`, `HistoryProjection`, `UploadedFiles`, `widget-load-dedupe`, `history-restore-overwrite`, `email-focus-restamp`, `scrollPinTop`, `active-context-decorate.e2e`), not `Test1`/`Foo_Works` (B13) or DI/ctor-null wiring (B3/B4).
3. **Already gated at merge** — each phase PR ran `code-review` + `adr-check`, which apply the ADR-038 B1–B17 bans on the delta. The diet re-confirms; it found nothing new to remove.
4. **Residual unmerged delta adds no tests** — the 2 commits ahead of master (`5169c6c42` auth revert, `a227f1f60` docs) touch zero test files.

## Out of scope — Compose-domain files (owned by compose-r*)

Four `.cs` files appear under R2's grep because an R2 commit (DI-02 compose flush-on-unmount, `513dd03e0`) or a sync merge touched them, but they are Compose-domain tests owned by the compose projects, not R2's to reconcile:
`AnchoredAnnotationPersistenceTests.cs`, `ComposeServiceLoadImportedCommentsTests.cs`, `ComposeServiceLoadImportedRevisionsTests.cs`, `CrossVersionSessionPersistenceTests.cs`. (R2's own Compose delta was `ComposeWorkspace.unmountFlush.test.tsx`, a behavioral unmount-flush test → MAINTAIN.)

## Delete commands

None.

## Path-move commands

None.

## Count delta

- Test files touched by R2 commits: 49 (25 `.cs` + 24 TS)
- MAINTAIN: 45 · SCAFFOLDING: 0 · AMBIGUOUS: 0 · PATH-VIOLATION: 0 · out-of-scope compose: 4
- Net post-diet expected count: unchanged (no reviewer-approved deletes)

## Follow-up flagged (NOT a test-diet action — code-review item for the reviewer)

`useAuthProbe.ts` is live in `App.tsx` and shipped in this deploy, but was previously characterized as structurally superseded by the `requireSilentOnly` flag revert. With the flag reverted the popup fallback succeeds, so the probe's retry can now resolve — it is not broken, but its continued necessity is a design question. Left in place because it is part of the deployed, about-to-be-re-UAT'd state; ripping it out during wrap-up would change App-mount auth gating unverified. Recommend a follow-up review (or R3 note) rather than a wrap-up change. Its test stays (MAINTAIN) as long as the hook stays.

## Industry citation

Build-vs-maintain per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes). 17-ban classifier B1–B17.
