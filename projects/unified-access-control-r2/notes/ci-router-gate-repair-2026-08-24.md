# CI Router gate repair — 2026-08-24

> **Owner of the touched files**: `ci-cd-unit-test-remediation-r1` (per [`projects/INDEX.md`](../../INDEX.md) line 171 — "owns existing CI workflow modifications for the 28-day window"). That window opened 2026-06-25 and lapsed ~60 days ago. Fixed here with owner approval because the defect was repo-wide and blocking honest verification of every branch. **Please fold these changes into your CI model** — cross-referenced as a GitHub issue.
>
> **Files changed**: `.github/workflows/ci-router.yml`, `.github/workflows/ci-tier2-advisory.yml`. `sdap-ci.yml` deliberately untouched (PR #806 has it open).

---

## Why this happened at all

`CI / Router` had **never been green on `work/unified-access-control-r2`** — 17 runs, 0 successes (13 `cancelled`, 3 `action_required`, 1 `failure`). It is the intended single composite gate (spec FR-A01). Because it was permanently red for reasons unrelated to the code under test, its signal was worthless, and this project had been substituting "N tests pass locally" for CI verification. Local runs never execute Arch Tests, Changed-Surface Integration Smoke, Auth Smoke, Plugin Size or the Last-Reviewed stamp.

**Scope check: this was repo-wide, not one branch.** Sampling the last 20 `ci-router.yml` runs across all branches, the tier2 `full-unit-tests` job was `cancelled` in **20 of 20** — it has not completed once in the repo's recent history. `work/spaarkeai-compose-r8`'s Router fails identically.

Nothing was *blocked*, because branch protection is disabled repo-wide (`GET /branches/master/protection` → 404 "Branch protection has been disabled") and the Router is still in the shadow mode its own header describes ("not flipped to required until task 071"). The cost was not blocked merges — it was a gate nobody could believe.

---

## Three defects, one visible symptom

### D1 — the advisory tier's unit-test budget was structurally unreachable

`ci-tier2-advisory.yml` `full-unit-tests` carried `timeout-minutes: 6` beneath a comment reading `Budget: <3 min`. Measured on run **32744675143** (`windows-latest`), overhead *before a single test runs*:

| Step | Duration |
|---|---|
| Checkout (`fetch-depth: 0` + LFS) | 28s |
| Setup .NET 10 | 3s |
| Cache NuGet | 1m27s |
| Restore | 29s |
| Build | 1m56s |
| **Total overhead** | **4m23s** |

That left ~1m35s of the 6-minute budget for `dotnet test`. It ran 2m49s and was killed at the wall.

### D2 — a timeout kill is `cancelled`, and `allowed-failures` doesn't cover cancellation

GitHub reports a `timeout-minutes` kill as `conclusion=cancelled`, **not** `failure`. That silently defeats the job's own `continue-on-error: true`. The Router's `alls-green` was configured `allowed-failures: tier2` / `allowed-skips: tier1,tier2` — and allowed-failures covers `failure` only:

```
📝 classify → ✓ success [required to succeed]
📝 tier1    → ✓ success [required to succeed or be skipped]
📝 tier2    → ⬜ cancelled [allowed to fail]
❌ Some of the required to succeed jobs failed 😢😢😢
Process completed with exit code 1
```

So `CI / Router` = **failure** while the blocking tier was fully green.

`ci-tier2-advisory.yml`'s own header already documented this exact failure mode. The prior fix — `613fb9b53` "per-SHA tier2 concurrency group to stop spurious `CI / Router` red" — addressed the **cross-commit** cause of a cancelled tier2 and could not address the **timeout** cause.

### D3 — the advisory workflow was cancelling itself

`ci-tier2-advisory.yml` declared both `workflow_call` **and** its own `pull_request` trigger. Under `workflow_call`, a reusable workflow evaluates `github.ref` / `github.sha` from the **caller** — so the standalone PR run and the Router's tier2 call computed the identical group `ci-tier2-advisory-${{ github.ref }}-${{ github.sha }}`, and `cancel-in-progress: true` killed one.

Timeline on run 32744675143 confirms the mechanism: the Router's `classify` completed **15:25:00** and dispatched tier2; the standalone died **15:25:01**. Result: a `CI Tier 2 (Advisory)` check `cancelled` at **100% of SHAs** on every PR in the repo, and a contributor to `mergeStateStatus: UNSTABLE`.

---

## What changed

**1. `ci-tier2-advisory.yml` — `timeout-minutes: 6` → `30`** on `full-unit-tests`, with the measured overhead recorded inline and the stale `<3 min` claim corrected. For an advisory tier that can no longer redden the gate, this value is a *runaway guard*, not a performance budget.

**2. `ci-router.yml` — tier2 excluded from adjudication by construction.** Rather than allow-listing a status `alls-green` doesn't cover, a step now builds the adjudication set from `classify` + `tier1` only (shape mirrors `toJSON(needs)` including empty `outputs`), passed as `jobs:`. `allowed-failures` dropped; `allowed-skips: tier1` retained for the legitimate no-Tier-1-surface case. `tier2` stays in `needs:` so the Router still sequences after it and the decision summary still reports its real result. This makes FR-A03's "tier2 is advisory" true by construction instead of aspirational.

**3. `ci-tier2-advisory.yml` — standalone `pull_request:` trigger removed**, leaving `workflow_call` as the sole entry point. Verified safe at removal time: all open PRs target `master` (so the Router always fires), branch protection is disabled (no required-check name depended on the standalone), and `tier2-pr-comment` already runs inside the Router's call (observed succeeding at 15:33:39). The jobs still run — as `CI / Tier 2 (Advisory) / *`.

D2 and D3 are fixed **independently on purpose**: the gate must survive a cancelled advisory tier even if some future cancellation source appears.

---

## Verified — run 32747593600, SHA `f695ce38f`

**`CI / Router` = SUCCESS. The first green Router in this branch's history** (previously 17 runs, 0 successes).

| Evidence | Result |
|---|---|
| `CI / Router` | ✅ SUCCESS |
| All 5 Tier 1 (blocking) jobs | ✅ SUCCESS |
| All 7 Tier 2 (advisory) jobs | ✅ SUCCESS — including `Full Unit Tests`, **which had never completed once** |
| Standalone `CI Tier 2 (Advisory)` run | ✅ **does not exist at this SHA** — D3 fixed; every prior SHA had one, always `cancelled` |
| `CANCELLED` rows in the PR check rollup | ✅ **zero** (was 8+) |
| Adjudication log | ✅ `classify=success tier1=success tier2=success (tier2 not adjudicated)` → `✓ All of the required dependency jobs succeeded` |

### ⚠️ The number that matters for anyone re-tuning this

**`Full Unit Tests` ran 15:53:46 → 16:17:46 — exactly 24 minutes — and passed.**

That is the first observed duration this job has ever produced, and it invalidates two candidate values:

- **6 (the original)** — killed at 6 min, 18 minutes short. Never completed, 20/20 runs across all branches.
- **20 (this change's first draft)** — would have been killed at 20 min, **4 minutes short**. It would have shipped a "fix" that did not fix anything, and the failure would have looked identical to the original bug.

20 was rejected before commit only because the estimate (~13-21 min, from a measured 4m03s local run × 2-4× runner slowdown + 4m23s overhead) put it *at the edge of the range* — which is exactly how the original 6 became unreachable. **The lesson generalizes: when a timeout is a runaway guard rather than a performance budget, sizing it at the edge of your estimate is the bug, not the fix.** 30 completed with ~6 min headroom; re-tune from p95 once several green runs exist, and keep meaningful headroom above it.

---

## For `ci-cd-unit-test-remediation-r1` to decide

1. **Router latency vs. advisory completeness — the real trade-off in this change.** `tier2` remains in the Router's `needs:`, so the gate verdict now waits for a tier2 allowed to run up to 30 min instead of dying at 6. **That is a genuine latency regression**: the Router previously rendered at ~9 min (because tier2 always died at 6), and will now render whenever the suite actually finishes. If gate latency matters more than having tier2 in the summary table, drop `tier2` from `needs:` entirely — the Router would then render on `classify` + `tier1` (~4 min, *faster than before*) and tier2 would run on as pure advisory. Not done here: it changes the signal model, which is yours.
2. **Calibrate the 30 from real data once you have some.** No tier2 unit-test job had completed in recent repo history, so there was no observed duration to size against. Chosen from: plain suite ~4m03s on a fast local box (10,762 tests, measured 2026-08-24) × 2-4× for a 2-core `windows-latest` runner + 4m23s overhead ≈ 13-21 min, then headroom because a value at the edge of the estimate is how the original 6 became unreachable. Set from observed p95 once green runs accumulate.
3. **`sdap-ci-docs-only.yml` check-name collision (not fixed — reported).** It emits check runs named exactly `Build & Test (Debug)` / `(Release)` as ~3-second no-ops. Its header asserts "Branch protection treats the REAL check from `sdap-ci.yml` as authoritative" — GitHub makes no such guarantee for same-named check runs. Benign while branch protection is off *and* while the real job finishes later and overwrites. The hole: when `sdap-ci.yml` is **cancelled** (5 of the last 8 commits on this branch), the stale no-op success is the only bearer of that name. If protection is ever re-enabled with `Build & Test (Debug)` required, that is a green gate over an unrun build. Suggest distinct check names plus an explicit aggregator.
4. **`action_required` on bot commits.** Workflows on `github-actions[bot]`-authored commits (the `style: auto-format dotnet whitespace (CI)` commits) require manual approval and produce dead runs — `7ca8669d5`, `7f36a5ffe`, `e12cc48d3` here. Each was superseded within minutes, so nothing was lost, but the repo setting generates permanent noise. Owner decision.

## Lesson recorded for this project

A red gate is not the same as a failing build, and a green local suite is not CI. This project reported local test counts as verification for six consecutive commits while the repo's own gate had never once rendered a verdict on the branch. **Read the gate, not the substitute** — and when the gate is red for reasons that look unrelated to the diff, that is a finding to chase, not noise to route around.
