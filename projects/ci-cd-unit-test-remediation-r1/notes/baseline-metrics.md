# sdap-ci.yml Baseline Metrics (30-day window)

**Task**: CICD-011 — Measure current `sdap-ci.yml` p50/p95 end-to-end runtime as baseline for SC-01..SC-03 calibration.
**Date captured**: 2026-06-26
**Captured by**: task-execute (CICD-011, STANDARD rigor)
**Spec references**: SC-01 (Tier 1 p95 < 3 min), SC-02 (Tier 2 p95 < 8 min), SC-03 (PR-to-merge ≥30 % decrease).

---

## 1. Methodology

### Data source

GitHub REST API, workflow ID 193328845 (`.github/workflows/sdap-ci.yml`), paginated `actions/workflows/{id}/runs` endpoint via `gh api --paginate`.

### Query

```bash
gh api --paginate \
  "repos/spaarke-dev/spaarke/actions/workflows/193328845/runs?per_page=100&created=>=2026-05-27" \
  --jq '.workflow_runs[] | {id, name, event, status, conclusion, head_branch, run_attempt, created_at, updated_at, run_started_at}'
```

### Window

- **Start**: 2026-05-27 (30 days prior to capture)
- **End**: 2026-06-26 04:10 UTC (capture time)
- **Earliest observed**: 2026-05-27T02:07:50Z
- **Latest observed**: 2026-06-26T04:10:32Z
- **Total raw runs**: 706

### Filter (per task POML step 2)

PR-triggered runs only (`event == "pull_request"`), completed (`status == "completed"`), with terminal outcome `success` or `failure`. Excluded: `cancelled` (118), `action_required` (0 in PR set), `in_progress` (0 in PR set), `null` (0 in PR set).

- PR-triggered all events: 446
- PR-triggered cancelled (excluded): 118
- PR-triggered with conclusion `action_required`/null (excluded): 2
- **PR-triggered completed (success/failure) — analysis sample**: **n = 326**

This **exceeds the acceptance-criteria threshold of n ≥ 50 by 6.5×**, giving high confidence in the percentile estimates.

### Duration calculation

`duration_seconds = updated_at - max(run_started_at, created_at)`. For runs where `run_started_at` was null (rare; ~0 in this set), `created_at` substituted. Hand-computed using linear-interpolation percentile method (NumPy-equivalent default).

### Push-triggered runs (separate)

246 `push`-triggered completed runs were also captured for context but EXCLUDED from the primary baseline. Roughly half (~125) show 0-second durations — these are workflow-skipped-via-path-filter runs that GitHub still records as "success" with no jobs executed. Including them would understate the true work-doing baseline; excluding them keeps the SC-03 comparison apples-to-apples (PR signal is what gates merges).

---

## 2. Headline numbers — PR-triggered completed (n = 326)

| Percentile | Seconds | Minutes |
|------------|--------:|--------:|
| **p50**    |   874.0 | **14.57** |
| p75        |  1004.8 | 16.75 |
| p90        |  1296.5 | 21.61 |
| **p95**    |  1383.2 | **23.05** |
| p99        |  1481.0 | 24.68 |
| min        |   169.0 |  2.82 |
| max        |  1888.0 | 31.47 |
| mean       |   855.4 | 14.26 |

---

## 3. Success / retry rates

### Success rate

- Completed-only denominator: **120 / 326 = 36.8 %**
- Including cancelled in denominator: 120 / 444 = 27.0 %

Both numbers are alarmingly low. The 36.8 % completed-only figure is the relevant baseline for FR-A09 / SC-04 (false-failure reduction) and the 27 % all-attempts figure shows how much cancellation noise the current workflow generates.

### Retry rate (run_attempt > 1)

- PR-completed subset: **24 / 326 = 7.4 %**
- All runs: 31 / 706 = 4.4 %
- Distribution of attempts (all runs): {1: 675, 2: 21, 3: 9, 4: 1}

7.4 % of PR runs were re-tried at least once. This is the SC-04 floor: any new tiering scheme that triggers ≥7.4 % retry rate is regressing developer experience.

---

## 4. Success vs failure split (PR-completed)

| Outcome | n | p50 (min) | p95 (min) | mean (min) |
|---------|---:|---------:|---------:|----------:|
| success | 120 | 17.63 | 24.23 | 18.90 |
| failure | 206 | 13.54 | 15.68 | 11.55 |

**Key insight**: failures complete ~5 min faster than successes (fail-fast in early jobs). The p95 of 23.05 min is driven almost entirely by the success path. Tier-routing decisions should optimize for the success path because that is what merges block on.

---

## 5. Calibration targets

### SC-01 — Tier 1 (blocking, p95 < 3 min)

- **Baseline p95 (PR, all-jobs)**: 23.05 min
- **SC-01 target**: < 3 min for blocking tier only
- **Required reduction for SC-01-aligned scope**: 87 % of current runtime must move OUT of the blocking path
- **Achievability**: REACHABLE IF Tier 1 is scoped to: build + Tier-1-marked tests + auth smoke + cheap lints only. The 3-min budget implies ~70 s build + 60 s tests + 50 s setup. The router design (task 040) and Tier-1 yml (task 041) MUST keep this slice ≤ 3 min p95 to satisfy SC-01.
- **Risk**: NuGet restore alone is observed at ~60–90 s cold; warm-cache assumption is load-bearing.

### SC-02 — Tier 2 (advisory, p95 < 8 min)

- **Baseline p95**: 23.05 min
- **SC-02 target**: < 8 min advisory tier
- **Required reduction**: 65 %
- **Achievability**: REACHABLE if Tier 2 excludes Sprk.Bff.Api.Tests (slated for deletion via 053a/b/c) and Plugins/Scheduling tests (051/052). Post-deletion the remaining unit test footprint is materially smaller. The 8-min budget covers a full BFF + Spaarke.Core + Plugins-survivor build & test cycle with parallel matrix.

### SC-03 — PR-to-merge ≥ 30 % reduction

- **Baseline p50**: 14.57 min (target ≤ 10.20 min — 30 % decrease)
- **Baseline p95**: 23.05 min (target ≤ 16.14 min — 30 % decrease)
- **Achievability**: HIGH. Even without Tier 2 cuts, just moving Sprk.Bff.Api.Tests (current largest unit-test consumer) and Plugins.Tests out of the blocking path should drop the merge-blocking critical path by ~40 %. With the tiered router (Tier 1 blocking ≤ 3 min, Tier 2 advisory ≤ 8 min), Tier 1 IS the new "PR-to-merge gate". So SC-03 effectively becomes: "Tier 1 p95 (≤ 3 min) ≤ 70 % × baseline p95 (16.14 min)" — trivially satisfied at any Tier 1 target under 16 min.
- **Recommended measurement at task 076**: re-run this same query 30 days post-cutover (filtering to `ci-router.yml` + `ci-tier1-blocking.yml` chain only) and compute the equivalent PR-to-merge p50/p95. The 30 % SC-03 threshold compares apples-to-apples PR signal.

---

## 6. Calibration target summary (for FR-B07 reviewers)

| Success Criterion | Baseline (PR p95) | New-tier target | Delta required |
|-------------------|------------------:|----------------:|---------------:|
| SC-01 (Tier 1 blocking) | 23.05 min | < 3 min | -87 % |
| SC-02 (Tier 2 advisory) | 23.05 min | < 8 min | -65 % |
| SC-03 (PR-to-merge gate, ≥ 30 % decrease) | 23.05 min | ≤ 16.14 min | -30 % (floor) |

---

## 7. Data integrity notes

- **n = 326** PR-completed runs comfortably exceeds the acceptance-criteria minimum (50) — 6.5× headroom.
- **Cancellation rate (118/444 = 26.6 %) of PR runs** is itself a finding. The new tier router (task 040) should reduce cancellation pressure because path-aware dispatch will skip irrelevant jobs entirely rather than start-then-cancel.
- **Retry rate** (24/326 = 7.4 %) becomes a quiet SC-04 baseline: post-cutover retry rate should not exceed 7.4 % or the new system has regressed.
- **0-second push runs** (~125/246) are GitHub's "workflow-evaluated, no-jobs-ran" outcome from path filters; they inflate the push success rate cosmetically and should never be included in performance percentiles.

---

## 8. Files

- Raw paginated runs (jsonl): `c:/tmp/sdap-ci-all-runs.jsonl` (706 records, ~700 KB; ephemeral)
- Re-run with: `gh api --paginate "repos/spaarke-dev/spaarke/actions/workflows/193328845/runs?per_page=100&created=>=YYYY-MM-DD"`

---

*Generated by CICD-011. Re-run at task 076 (30-day post-cutover measurement) and task 075 (7-day soak gate) using the same methodology against the new workflows for like-for-like comparison.*
