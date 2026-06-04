# Post-084 Triple-Run Report — 2026-05-31

> **Source TRX (6 files)**: see [`final-runs-summary.md`](final-runs-summary.md) for the canonical headline table
> **Task**: 084 (P4.B1 — full suite triple-run validation per FR-26)
> **Predecessor measurement**: [`post-028-classification-2026-05-31.md`](post-028-classification-2026-05-31.md) (Phase 2+3 FULL CLOSURE)
> **Authority**: this file is the Phase 4 Wave 4.1 FR-26 attestation report; [`final-runs-summary.md`](final-runs-summary.md) contains the verbatim per-TRX `<Counters>` extract

---

## Headline

**6 of 6 runs pass with `Failed: 0`. No flake surfaced. FR-26 SATISFIED.**

| Run | Suite | Passed | Failed | Skipped | Total | Duration |
|---|---|---:|---:|---:|---:|---:|
| 1 | Unit (Sprk.Bff.Api.Tests) | 5,893 | **0** | 137 | 6,030 | 1 m 13 s |
| 1 | Integration (Spe.Integration.Tests) | 323 | **0** | 98 | 421 | 21 s |
| 2 | Unit | 5,893 | **0** | 137 | 6,030 | 1 m 13 s |
| 2 | Integration | 323 | **0** | 98 | 421 | 21 s |
| 3 | Unit | 5,893 | **0** | 137 | 6,030 | 1 m 13 s |
| 3 | Integration | 323 | **0** | 98 | 421 | 21 s |

---

## Procedure executed

1. **Run 1**: `dotnet test … -c Release --logger "trx;LogFileName=final-run-1{,-integration}-2026-05-31.trx" --results-directory projects/sdap-bff.api-test-suite-repair/baseline/`
2. **Run 2**: same with `final-run-2*`
3. **Run 3**: same with `final-run-3*`

Each `dotnet test` invocation triggered a fresh build (incremental, Release configuration); xUnit runner enumerated tests and produced both stdout per-test report and TRX artifact. The 6 TRX artifacts are the FR-26 evidence; the stdout per-suite summary line is the human-readable counterpart.

---

## Cross-run stability analysis

| Stability dimension | Observation | Verdict |
|---|---|---|
| `passed` count variance | 0 (5,893 unit × 3; 323 integration × 3) | ✅ stable |
| `failed` count variance | 0 (`Failed: 0` × 3 in both suites) | ✅ stable |
| `Skipped` count variance | 0 (137 unit × 3; 98 integration × 3) | ✅ stable |
| Wall-clock variance | <2 s (1m 13s unit × 3; 21s integration × 3) | ✅ stable |
| Skip-Trait persistence | 51 RB-T028-NN tests remain Skipped × 3 runs | ✅ persistent |
| Test order non-determinism | identical pass/skip distribution | ✅ no effect |
| Cumulative state leakage | run-3 == run-2 == run-1 | ✅ no leak |

**Conclusion**: zero flake signal. The post-028 zero-failure end-state is genuinely stable, not lucky.

---

## NFR compliance proof (task 084)

| NFR | Requirement | Verification | Status |
|---|---|---|---|
| **NFR-01** | No `src/`/`power-platform/`/`infra/`/`scripts/` changes | `git status` post-validation: no files under those paths modified by 084 (only baseline TRX adds + summary md) | ✅ |
| **NFR-09** | `<repair-not-rewrite>true</repair-not-rewrite>` | Declared in task 084 POML metadata line 12 | ✅ |
| **§4.5** | No `CustomWebAppFactory.cs` rewrite | File untouched (read-only `dotnet test`) | ✅ |
| **§6.4** | Full suite before AND after any factory change | This is the "after" run for tasks 018+019; matches §6.4 sequence | ✅ |
| **§4.3 / NFR-10** | Zero `Failed` end-state at project close | 0 Failed × 6 TRX files = empirically satisfied | ✅ |
| **FR-26** | 3 TRX files showing `Failed: 0` (per suite) | 6 TRX files showing `failed="0"` (3 per suite) | ✅ |

---

## Flake detection results

**Zero flakes detected.** No new entries needed in `projects/sdap-bff.api-test-suite-repair/ledgers/flaky-ledger.md` — it remains at the post-028 zero-entry canonical-schema state.

If any run had surfaced `failed > 0`, the §6.2 fix path would have been:
1. Identify the failing test(s) + cluster pattern
2. Classify per §6.2 taxonomy: `repaired` (if signature drift), `real-bug-pending-fix` (production bug), `flaky-quarantined` (environmental), or archived
3. Apply the taxonomy via `[Trait("status", …)]` + Skip
4. Append to the appropriate ledger
5. **Re-run all 3 iterations** (the triple-run gate is non-cumulative)

No fix path activated this iteration.

---

## What Phase 4 task 086 should do next

- **Task 086** (final verification gate): cite this report + `final-runs-summary.md` as the FR-26 evidence anchor. The §4.3 / NFR-10 check passes without exception. Branch protection check (FR-09) was already verified by task 020 + `ci-gate-post-flip-2026-05-31.json`.

---

## Attestation (canonical)

> **FR-26 satisfied — `Failed: 0` across 3 stable runs in both suites.**
> 6/6 TRX files report `failed="0"`. Zero flakes. Phase 4 exit gate (`task 086`) CLEARED TO START with no carry-over `Failed`-state debt.

---

*End of post-084 triple-run report (Phase 4 Wave 4.1).*
