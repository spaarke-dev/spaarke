# Test diet report — `ai-advanced-capabilities-analysis-hub-r1`

**Run date**: 2026-08-03
**Branch**: `work/ai-advanced-capabilities-analysis-hub-r1`
**Gate**: mandatory project-close reconciliation per CLAUDE.md §7 + spec FR-B09 (ADR-038 §7 build-vs-maintain classifier, 17 bans B1–B17)
**Classifier run**: read-only — this report emits `git rm` / `git mv` commands only; no test was deleted or moved by the skill.

## Scoping note (read first)

This project is ≈80% reuse composition, and its branch tip shares ancestry with several sibling projects merged into master during the same window (`agreements-r1`, `spaarkeai-compose-fidelity-r4.5`, `email-communication-intelligence`, `messaging-r3`). A raw `git log {start}..HEAD -- tests/**` range therefore attributes many sibling-project test files to this branch's ancestry and is **not** a reliable scope for this project's own deltas.

Scope was instead taken as the **test files touched by commits with analysis-hub authorship** (scopes `feat/fix(analysis|session)` + hub tasks 020/023/025/030/040 and this session's A1/A3/headless/focused/Q2 work). Sibling-project test files (Compose `*.test.tsx`, agreements-r1 `ConversationPane.agreement-*`, `ReviewMemo*`, `AgreementClassify*`, Communication/Email tests, `PromoteDurableFkVisibilityTests.cs` — authored by agreements-r1 as a regression against this project's Q2 fix) are **out of scope** here; they reconcile under their own projects' wrap-ups.

## Summary

| Class | Count | Action |
|---|---|---|
| MAINTAIN (KEEP at canonical path) | 6 files / 129 tests | confirmed — no action |
| SCAFFOLDING (DELETE candidate) | 0 | — |
| AMBIGUOUS (reviewer judgment) | 0 | — |
| PATH-VIOLATION (wrong KEEP path) | 0 | — |
| **Total test files touched (in scope)** | **6** | — |

## Delete commands

None. No test method matched any of the 17 bans (B1–B17).

Signature scan (all negative):
- **B1/B2** `Mock<HttpMessageHandler>` / `Mock<*ServiceClient>` → 0 hits
- **B3/B4** `GetRequiredService` wiring assert / ctor `ArgumentNullException` → 0 hits
- **B13** name-without-scenario (`Test1`/`Works`/`Foo`/`DoIt`) → 0 hits

## Path-move commands

None. Frontend suites live beside their component under `__tests__/` (project convention for the jest-based Vite solutions); BFF service tests live at `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Chat/` (the established BFF service-test location — 35/35 green there this run). No canonical-path violation.

## Maintain — confirmed (no action)

| File | Tests | Task | Why MAINTAIN |
|---|---|---|---|
| `Spaarke.AI.Widgets/.../__tests__/CreateAnalysisWizardWidget.test.tsx` | 10 | 040 / A1 | Behavioral: renders the 3-step wizard, loads `sprk_agreementtype` rows, asserts the `@odata.bind` lookup payload persists the selected type. Fails on real regression of the picker. |
| `Spaarke.AI.Widgets/.../__tests__/AnalysisHubWidget.test.tsx` | 5 | 030 / headless | Behavioral: asserts 3 cards render, grid lists analyses, and a hosted row-click dispatches `open_analysis_headless` → `openSpaarkeAi` (ADR-039 record-open path). |
| `SpaarkeAi/.../workspace/__tests__/WorkspaceTabManager.test.ts` | 67 | 025 / reopen+focused | Behavioral: `MAX_WORKSPACE_TABS`, tab-anchor restore, server-tabs vs local-anchor precedence, focused-open (`mode==='existing'`) skips accumulated-tab restore. |
| `Spaarke.AI.Outputs/.../__tests__/AnalysisEditorWidget.test.tsx` | 12 | 025 | Behavioral: editor widget render + edit-restore state. |
| `Sprk.Bff.Api.Tests/Services/Ai/Chat/ChatDataverseRepositoryTests.cs` | 12 | 020 / Q2 | Behavioral: `BindSessionToAnalysisAsync` creates the `sprk_aichatsummary` anchor row **with the FK** when missing (Q2 durable-FK fix); real regression coverage for orphaned-Analysis. |
| `Sprk.Bff.Api.Tests/Services/Ai/Chat/ChatSessionManagerTests.cs` | 23 | 023 | Behavioral: two-tier session model (loose vs Analysis-owned) + explicit promotion. |

## Count delta

- Test files touched (in scope): **6**
- Classified MAINTAIN: **6** (129 tests)
- Classified SCAFFOLDING: **0**
- Classified AMBIGUOUS: **0**
- Net post-diet expected count: **unchanged** (no reviewer-confirmed deletes)

## Verification run (this session)

| Suite | Result |
|---|---|
| WorkspaceTabManager.test.ts (jest) | 67/67 pass |
| CreateAnalysisWizardWidget + AnalysisHubWidget (jest) | 15/15 pass |
| ChatDataverseRepository + ChatSessionManager (dotnet, Q2) | 35/35 pass |
| Retirement grep-clean (SC#7) | clean — no `sprk_analysisworkspace` in code; tree + deploy script deleted |
| Record opens via `openSpaarkeAi` (SC#9) | confirmed — no `surfaceLaunchRegistry` misuse |

## Industry citation

Build-vs-maintain criteria per ADR-038 §7 (Beck "delete the scaffolding"; Feathers characterization-vs-behavior; Google test-sizes; DHH less-tests). 17-ban classifier B1–B17. Result: this project added maintain-class behavioral tests only — no scaffolding to delete.
