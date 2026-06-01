# Phase 0 Baseline Artifacts — 2026-05-31

> **Observed `enforce_admins.enabled`: `false`** — confirms design.md §5.2 "fictional CI gate" hypothesis. Phase 1 P1.D (FR-09) will flip this to `true`.

> **Captured by**: Task 001 (`tasks/001-baseline-capture.poml`) on 2026-05-31.

---

## Files in this folder

| File | Purpose | Source command |
|---|---|---|
| `test-baseline-2026-05-31.trx` | Full `Sprk.Bff.Api.Tests` run (Release) — parseable XML with `<ResultSummary>` + `<Counters>` | `dotnet test ... --logger "trx;..."` |
| `compile-errors-2026-05-31.txt` | `dotnet build` log for `Sprk.Bff.Api.Tests.csproj` | `dotnet build ... 2>&1 \| tee ...` |
| `ci-gate-snapshot-2026-05-31.json` | `gh api` master branch protection + last 30 `sdap-ci.yml` runs (non-strict JSON concatenation) | `gh api repos/.../branches/master/protection`, then `gh run list --workflow=sdap-ci.yml --branch=master --limit=30 --json ...` appended |
| `integration-build-errors-2026-05-31.txt` | (produced by parallel Wave-1 task 002) — `Spe.Integration.Tests` build log | (other task) |

---

## Measured numbers — `Sprk.Bff.Api.Tests` (2026-05-31)

```
Total:    6,021
Passed:   5,572  (92.5%)
Failed:     342  ( 5.7%)
Skipped:    107  ( 1.8%)
Duration: 1m 13s (Release, RalphSchroeder Windows dev box)
```

### Comparison vs. design.md §3 (2026-05-30 baseline)

| Metric | Design.md §3 | Observed 2026-05-31 | Delta | Interpretation |
|---|---|---|---|---|
| Total tests | 5,215 | **6,021** | **+806** | All 17 previously compile-broken files now compile and contribute their tests (§5.6 "3 in-progress namespace fixes" appear to have unblocked broader compile drift) |
| Passed | 4,844 | **5,572** | **+728** | Pass rate steady (92.9% → 92.5%); the newly-unblocked compile-fixed files brought mostly passing tests |
| Failed | 269 | **342** | **+73** | Higher than design — but in the same order of magnitude. Driven by newly-compiled files contributing their own failures + drift accumulated 2026-05-30 → 2026-05-31 |
| Skipped | (102) | 107 | +5 | Negligible |
| Compile-broken files | 17 (138 errors) | **0** | **−17 / −138** | Build now succeeds with `0 Error(s)` (17 warnings only) — **major deviation**, see below |

---

## Significant Deviation: Compile errors

**Expected** (design.md §3.2): 17 files broken, 138 compile errors (CS7036, CS1503, CS1061, CS0618, CS1739, CS8625).

**Observed** (2026-05-31): `dotnet build tests/unit/Sprk.Bff.Api.Tests/Sprk.Bff.Api.Tests.csproj` returns:

```
17 Warning(s)
 0 Error(s)
Time Elapsed 00:00:17.07
```

**Acceptance criterion impact**: The criterion "compile-errors-*.txt contains at least one `error CS` line" is NOT met. `grep -c "error CS"` returns 0.

**Root cause hypothesis** (not yet validated): Per design.md §5.6, three in-progress namespace fixes were kept in the working tree from prior work. These — and possibly other follow-on compile fixes — may have already repaired the 17 broken files between the 2026-05-30 baseline capture and 2026-05-31 (project init day).

**Implication for Phase 1 P1.A** (FR-05): The 17-file compile-recovery track may already be effectively complete. Phase 1 P1.A scope should be re-evaluated against this observation BEFORE work begins. The 342 runtime failures replace the "compile + runtime" mixed bucket that informed P1.A sizing.

**This deviation is flagged for owner / Phase 1 planning review.** The task-001 artifact set is still authoritative for downstream use; the deviation is documented here, not glossed over.

---

## CI gate snapshot — key value

```
"enforce_admins": {
  "url": "https://api.github.com/repos/spaarke-dev/spaarke/branches/master/protection/enforce_admins",
  "enabled": false
}
```

This matches design.md §5.2 "fictional gate" framing. Last 30 `sdap-ci.yml` runs on master appended to the file (non-strict JSON concatenation — file is a captured snapshot, not a programmatic input).

---

## Usage

Every Phase 1+ task that cites failure counts MUST cite THIS baseline (§6.3 binding rule), not stale numbers from `bff.api-repair-overview.txt`. When citing, use the 2026-05-31 numbers above; when comparing to design.md, use the delta table.
