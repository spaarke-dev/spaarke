# Per-fix Triple-Run — RB-T028-02 Insights Layer 2 Outcome Extraction

> **Date**: 2026-06-01
> **Task**: 012 (Phase 1 / P1-W1; FR-05 path-b)
> **Ledger entry**: `RB-T028-02` (Layer 2 outcome-extraction LLM-mock fixture drift — 3 tests)
> **Production fix**: `src/server/api/Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs` — promoted `Normalize` from `internal static` → `public static` with expanded XML doc documenting CRLF↔LF tolerance contract
> **Test change** (in-scope per NFR-01, Skip→Pass transition): 3 tests in `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Layer2/Layer2OutcomeExtractionTests.cs` — `[Skip = "..."]` removed; `[Trait("status", "real-bug-pending-fix")]` → `[Trait("status", "repaired")]`; manual `documentText.Should().Contain(quote)` replaced with `GroundingVerifier.Normalize(documentText).Should().Contain(GroundingVerifier.Normalize(quote))` to mirror production grounding semantics precisely
> **Resolution mode**: `repaired`

---

## Root cause (NOT what the ledger hypothesized)

The r1 ledger entry hypothesized "fixture-text-drift" — fixtures drifted away from prompt-template / mock-LLM-response wiring after sibling-project edits. **This was incorrect.**

Actual root cause (verified by Python byte-level inspection + `dotnet test` with Skip temporarily removed):

1. The 3 fixture files (`closing-letter-M-2024-0341.txt`, `settlement-agreement-M-2024-0188.txt`, `decision-memo-M-2024-0512.txt`) are stored on Windows with **CRLF (`\r\n`) line endings** — 67/85/83 CRLFs respectively; ZERO LF-only newlines.
2. C# raw-string literals (`"""..."""`) — used for the mocked LLM JSON in the test — normalize multi-line content to **LF (`\n`)** at compile time (C# 11 spec).
3. The 3 tests' manual GroundingVerifier mirror (lines 165, 254, 338 of `Layer2OutcomeExtractionTests.cs`) used raw `String.Contains` against `documentText` (CRLF on disk → CRLF in memory after `File.ReadAllText`) with the LF-only evidence quote string. `String.Contains` is byte-exact — `\n` does not match `\r\n`. **The test asserted a stricter invariant than production enforces, producing false failures on the CRLF↔LF boundary.**
4. **Production behavior is correct**: `GroundingVerifier.Normalize` (line 266 pre-fix; now line 281) collapses ALL `char.IsWhiteSpace(ch)` runs (including `\r\n`) into a single space. Production grounding verification is line-ending-tolerant; the test failed to mirror that.

The ledger hypothesis ("fixture drift") was based on the failure symptom (TRX `"Expected documentText `...long fixture text...`"` message) without static analysis of the line-ending difference. Sibling-project owner (`dev@spaarke.com`) chose **path (b)** — r2 takes the bug; production fix here — on 2026-06-01.

## Production fix scope

**Single file modified**: `src/server/api/Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs`

**Change**:
- Promoted `Normalize` method from `internal static` → `public static` (1-line visibility change).
- Expanded XML doc comment from 3 lines to 16 lines — documents the canonical grounding-text normalization contract (CRLF↔LF tolerance via whitespace collapse + lowercase) as a public API surface; explicitly states why raw `String.Contains` is a category-error on CRLF documents.

**Lines changed**: 5 → 21 (net +16 lines of doc) in a 313-line file. Approximately 5% line replacement / +5% line addition. Well under NFR-02's 50% replacement limit. Pure additive — no behavioral change to production grounding verification; existing callers (`GroundingVerifier.VerifyOne` lines 145-195) continue to operate identically.

## Test-side change scope (in Skip→Pass scope per NFR-01)

**Single file modified**: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Layer2/Layer2OutcomeExtractionTests.cs`

**Changes** (3 tests):
1. `[Fact(Skip = "RB-T028-02: ...")]` → `[Fact]` (3×)
2. `[Trait("status", "real-bug-pending-fix")]` → `[Trait("status", "repaired")]` (3×)
3. `using` directive added: `using Sprk.Bff.Api.Services.Ai.CitationVerification;`
4. 7 `documentText.Should().Contain(quote)` calls → `GroundingVerifier.Normalize(documentText).Should().Contain(GroundingVerifier.Normalize(quote))` (with optional `normalizedDoc` local for readability in test 1)
5. 3 XML-doc / inline comments updated to cite RB-T028-02 resolution + 2026-06-01 + production-mirror rationale

## Triple-run results

```
Run 1 — Passed: 5902 / Failed: 0 / Skipped: 129 / Total: 6031 / Duration: 1m 15s
Run 2 — Passed: 5902 / Failed: 0 / Skipped: 129 / Total: 6031 / Duration: 1m 14s
Run 3 — Passed: 5902 / Failed: 0 / Skipped: 129 / Total: 6031 / Duration: 1m 13s
```

| Metric | Value | Notes |
|---|---|---|
| Failed (all 3 runs) | **0** | NFR-05 triple-run validation ✅ |
| Passed delta vs. task 010 close | **+3** (5899 → 5902) | RB-T028-02 3 tests flipped Skip→Pass |
| Skipped delta vs. task 010 close | **−3** (132 → 129) | Same 3 tests |
| Total | 6031 | Unchanged |

TRX files in `projects/sdap.bff.api-test-suite-repair-r2/baseline/trx-rb-t028-02/` (run-1.trx, run-2.trx, run-3.trx).

## Phase 0 baseline cross-reference

Per [`baseline/r1-closeout-2026-06-01.md`](r1-closeout-2026-06-01.md) §1, r1 close had 137 unit-test skips (51 tagged `real-bug-pending-fix`). At task 012 close:
- Phase 1 reductions so far: task 010 closed 5 Privilege tests + task 012 closed 3 Layer2 tests = 8 Skip→Pass.
- Remaining `real-bug-pending-fix` traits (51 - 8 = ~43): Phase 1 P1-S2 task 011 (RB-T028-03/04/05/06 cluster, 37 tests) + Phase 2 (MEDIUM entries, ~10 tests) + Phase 3 (LOW entries, ~12 tests). Numbers approximate because RB-T028-02's 3 tests don't account in the unit-test bucket distinct from r1 close's 137 — pre-fix the 3 were already Skip'd (so `137 → 137 - 3 + 3 = 137`); post-fix `137 - 3 = 134` initial unit Skip; task 010 plus task 012 reduce to `134 - 5 - 3 = 126` real-bug-tagged Skips remaining (but observed 129 — overlap with non-tagged Skips). Phase 1 exit delta (task 013) reconciles exactly.

## Files changed by this task

| File | Change | Scope |
|---|---|---|
| `src/server/api/Sprk.Bff.Api/Services/Ai/CitationVerification/GroundingVerifier.cs` | Promoted `Normalize` visibility `internal` → `public`; expanded XML doc | Production fix |
| `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Insights/Layer2/Layer2OutcomeExtractionTests.cs` | 3 tests: Skip removed; trait `real-bug-pending-fix` → `repaired`; manual checks now mirror production via `GroundingVerifier.Normalize` on both sides | Skip→Pass transition (NFR-01 in-scope) |
| `projects/sdap.bff.api-test-suite-repair-r2/baseline/trx-rb-t028-02/` (3 TRX files) | Triple-run validation evidence | Artifact |
| `projects/sdap.bff.api-test-suite-repair-r2/baseline/per-fix-triple-run-rb-t028-02-2026-06-01.md` | This file | Artifact |
| `projects/sdap.bff.api-test-suite-repair-r2/decisions/D-07-insights-layer2-resolution.md` | Placeholder → finalized: path (b) chosen 2026-06-01 | Decision record |
| `projects/sdap-bff.api-test-suite-repair/ledgers/real-bug-ledger.md` | RB-T028-02 Status `open` → `repaired` + fix-commit SHA | Ledger transition |
| `projects/sdap.bff.api-test-suite-repair-r2/tasks/TASK-INDEX.md` | Task 012 🔲 → ✅; Task 026 (Phase 2 fallback) 🔲 → ⏭ (subsumed by 012) | Task tracker |
| `projects/sdap.bff.api-test-suite-repair-r2/tasks/012-resolve-insights-hold.poml` | `<status>not-started</status>` → `<status>completed</status>` + completion notes | Task POML |
| `projects/sdap.bff.api-test-suite-repair-r2/current-task.md` | Task 012 entry + transition to next | Active task state |

## Acceptance criteria (POML 012)

| Criterion | Status |
|---|---|
| RB-T028-02 ledger entry Status transitions to `repaired` | ✅ |
| 3 tests in `Layer2OutcomeExtractionTests.cs` per-test trait updated to `repaired` | ✅ |
| Build passes; 3 tests now Pass | ✅ (15/15 Pass in `--filter Layer2OutcomeExtraction`) |
| Per-fix triple-run Failed: 0 across 3 runs | ✅ |
| Commit message cites RB-T028-02 + resolution mode `repaired` (NFR-04) | ✅ |
| Production fix <50% line replacement (NFR-02) | ✅ (~5% additive doc expansion) |
| Step 9.5 gates pass | ✅ (see commit message; code-review + adr-check completed) |
| No files modified outside path-appropriate scope | ✅ (1 prod file + 1 test file + project-artifacts) |

---

*Authored 2026-06-01 by Phase 1 P1-W1 task 012 (path-b). NFR-04 + NFR-05 + NFR-06 evidence.*
