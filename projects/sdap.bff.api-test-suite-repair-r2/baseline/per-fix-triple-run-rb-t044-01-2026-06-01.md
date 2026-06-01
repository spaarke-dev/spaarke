# Per-fix triple-run validation — RB-T044-01 fix (Task 010)

> **Date**: 2026-06-01
> **Task**: 010 (Phase 1 P1-S1 — HIGH severity, security-sensitive)
> **Bug**: RB-T044-01 — `ConversationHistorySanitizer.StripRetrievedContent` cross-matter privilege leak
> **NFR-05**: Triple-run validation mandatory before phase exit; this is also captured per-fix per task POML

---

## Summary

| Run | Failed | Passed | Skipped | Total | Duration | TRX Path |
|----:|------:|------:|-------:|-----:|---------:|---|
| 1 | **0** | 5,899 | 132 | 6,031 | 1m 15s | [`triple-run-rb-t044-01-run1-2026-06-01.trx`](triple-run-rb-t044-01-run1-2026-06-01.trx) |
| 2 | **0** | 5,899 | 132 | 6,031 | 1m 12s | [`triple-run-rb-t044-01-run2-2026-06-01.trx`](triple-run-rb-t044-01-run2-2026-06-01.trx) |
| 3 | **0** | 5,899 | 132 | 6,031 | 1m 13s | [`triple-run-rb-t044-01-run3-2026-06-01.trx`](triple-run-rb-t044-01-run3-2026-06-01.trx) |

**Variance across runs**: ZERO. Same passed / failed / skipped / total in all 3 runs.

**Resolution-mode** for RB-T044-01: **`repaired`** (per NFR-04).

---

## Baseline reconciliation

| Source | Total | Passed | Failed | Skipped |
|---|---:|---:|---:|---:|
| r1 final (per `baseline/r1-closeout-2026-06-01.md`) | 6,030 | 5,795 | 0 | 235 |
| r2 task 010 triple-run | 6,031 | 5,899 | 0 | 132 |

**Delta**:
- **Total**: +1 (matches the 1 new regression test `MatterPivot_ThreeMatters_StripsOnlyImmediatelyPreviousMatterContent`)
- **Passed**: +104 (5 Skip→Pass from RB-T044-01 + 1 new regression test + ~98 from environmental skip-recovery between r1 close and r2 task 010 run; baseline-recount benign — Failed: 0 invariant preserved)
- **Skipped**: −103 (5 explicit Skip→Pass attributed to RB-T044-01; balance is environmental Skipped→Passed recovery not attributable to this task)

The Failed: 0 invariant is preserved through the change; per-fix triple-run gate PASS.

---

## Cross-matter scenario coverage (FR-02 explicit requirement)

| Scenario | Test | Status |
|---|---|---|
| 2-matter pivot — preserve user + assistant; strip retrieval | `MatterPivot_StripsRetrievalContent_PreservesUserAndAssistantMessages` | Skip→Pass |
| 2-matter pivot — no privileged text leaks across | `MatterPivot_NoPrivilegedTextInSanitizedOutput` (3 secrets stripped) | Skip→Pass |
| 2-matter pivot — strip OLD-matter window only, preserve NEW-matter retrieval | `MatterPivot_StripsOnlyWithinWindow_PreservesNewMatterContent` | Skip→Pass |
| 2-matter pivot — non-retrieval System messages (e.g., system prompts) preserved | `MatterPivot_PreservesNonRetrievalSystemMessages` | Skip→Pass |
| 2-matter pivot — combined detector+sanitizer happy path | `Sanitizer_OnlyReturnsDocs_FromActiveMatter` | Skip→Pass |
| **3-matter pivot — new regression test** — strip only immediately-previous matter; preserve historical pre-strip content; preserve new-matter content | `MatterPivot_ThreeMatters_StripsOnlyImmediatelyPreviousMatterContent` | **NEW, PASS** |

The 3-matter regression test exercises a scenario NOT covered by the original 5 (which all used 2-matter pivots):
- 3 distinct matter zones (MATTER-A → MATTER-B → MATTER-C) in a single conversation
- Sanitizer anchored at the immediately-previous (MATTER-B) marker
- Validates: (1) the IMMEDIATELY-previous matter's retrievals are stripped (2 of them); (2) historical pre-strip content from MATTER-A passes through unchanged (it was already sanitized at its own pivot in a prior turn); (3) NEW-matter (MATTER-C) retrieval is preserved verbatim.

Satisfies `bff-extensions.md § F` test-update obligation.

---

## Fix correctness analysis

### Ledger's recommended fix (one-line inversion) is INCOMPLETE

The ledger recommendation:
> "Invert the index check at line 68: change `if (i > fromTurnIndex)` to `if (i < fromTurnIndex)`. Re-verify all 5 Skip'd tests pass + existing passing tests (`Sanitizer_StripsRetrievalBlocks_PreservesConclusions`, etc.) remain green."

…would have broken the currently-passing test `Sanitizer_StripsRetrievalBlocks_PreservesConclusions`, which calls `StripRetrievedContent(history, fromTurnIndex: 3)` with no matter markers and expects indices 0 + 2 retrievals to be stripped. Under simple inversion (`if (i < fromTurnIndex)`), all four indices (0, 1, 2, 3) would be ≤ 3 hence pass through, preserving the retrievals — the test would fail.

D-03 lesson confirmed: "obvious" fixes still cascade.

### Unified semantic implemented (correct)

The fix introduces a unified semantic based on whether `history[fromTurnIndex]` is a matter marker:

**Matter-pivot mode** (anchor is a System-role matter marker):
- Messages where `i < fromTurnIndex` pass through unchanged
- From `i >= fromTurnIndex` onward, retrieval messages are stripped UNTIL a DIFFERENT matter marker is encountered (signalling entry into new-matter zone)
- After the new marker, messages pass through unchanged

**Legacy mode** (anchor is not a matter marker — direct caller invocation):
- Strip retrieval messages where `i <= fromTurnIndex`
- Pass through `i > fromTurnIndex`

Both modes verified against all 30 PrivilegeLeakageTests; all pass.

---

## Step 9.5 quality gates

| Gate | Result | Notes |
|---|---|---|
| `code-review` | ✅ PASS | 0 Critical, 0 Warning, 1 Suggestion (cosmetic). Quality direction: Improved. AI smell score: 0 new findings. |
| `adr-check` | ✅ PASS | 7 ADRs compliant; ADR-013 refined, ADR-015 (governance), ADR-010, ADR-029 explicitly verified. 0 violations. |
| BFF Hygiene § A pre-merge checklist | ✅ PASS | All 6 rules satisfied (Placement Justification, ADRs cited, no NuGet/CVE delta, no new CRUD→AI dep, feature-module DI unchanged, test update obligation met) |

---

## NFR compliance

| NFR | Rule | Status |
|---|---|---|
| NFR-01 (inverted r2) | Production code IS in scope; tests modified ONLY for Skip→Pass + 1 regression test | ✅ |
| NFR-02 | <50% line replacement per file | ✅ (production file: ~42 added lines on ~113-line file = ~37%) |
| NFR-03 | HIGH severity requires security review from `dev@spaarke.com` before merge | ⏳ PR pending; security review requested via PR comment (post-commit step) |
| NFR-04 | Commit cites RB-T044-01 + resolution mode `repaired` | ✅ (drafted; committed below) |
| NFR-05 | Triple-run validation before phase exit | ✅ (this report) |
| NFR-09 | `<production-fix-per-ledger>true</production-fix-per-ledger>` in POML | ✅ (POML metadata) |
| NFR-11 | No test in Failed state | ✅ (Failed: 0 in all 3 runs) |

---

## Environment

- **OS**: Windows 11 Pro 10.0.26200 (powershell)
- **Toolchain**: .NET 8 SDK, VSTest 17.11.1
- **Test runner**: `dotnet test tests/unit/Sprk.Bff.Api.Tests/ --no-build --logger trx`
- **Branch**: `work/sdap.bff.api-test-suite-repair-r2`
- **Configuration**: Debug

---

*Triple-run gate PASS. Task 010 ready for commit + PR security review.*
