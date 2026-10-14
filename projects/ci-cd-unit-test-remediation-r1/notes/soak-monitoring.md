# Soak monitoring — task CICD-075 (7-day green gate, FR-B07 / SC-06)

> **Measured**: 2026-10-14T15:24Z (one retrospective measurement over the post-cutover record, not daily check-ins — the data is all in the Actions API)
> **Verdict**: ❌ **NOT MET** — counter reset by a legitimate master break on 2026-10-08. **Earliest pass: 2026-10-15T15:57Z**, if master has no further legitimate red.
> **Consequence**: task 077 (retire `sdap-ci.yml`) stays blocked. Nothing in this note changes any workflow.

---

## What was measured

The surviving suite is the Router (`ci-router.yml`, workflow name `CI`), the only required check since
2026-08-29 (ruleset `21824191`, context `Router`). Measured on `master` push runs:

- **Router / Tier 1** — the Router run conclusion and every Tier 1 job conclusion.
- **Tier 2** — Tier 2 jobs always exit 0 (advisory), so the job conclusion says nothing. The real
  verdict is the `Full Unit Tests (Debug)` aggregator's per-shard verdict lines, read from the job log.

`sdap-ci.yml` was not used as the oracle. The POML's step 1 names it, but it is the workflow being
retired, it is not required, and its `Build & Test` job is not the suite that survives.

## Daily record — master, Router runs (2026-10-07 → 2026-10-14)

| Day (UTC) | Completed runs | Red | Classification | Counts toward soak? |
|---|---|---|---|---|
| 10-07 | 25 (+3 cancelled) | 0 | — | ✅ |
| 10-08 | 34 | **2** | **Legitimate**: master did not compile | ❌ **reset** |
| 10-09 | 32 (+3 cancelled) | 0 | (Tier 2 `rest` shard: test host crash, 0 test failures — infra) | ✅ |
| 10-10 | 26 | 1 | Flake — MI token (#1672 / #1784) | ✅ (flake, no reset) |
| 10-11 | 6 | 1 | Flake — MI token (#1672 / #1784) | ✅ (flake, no reset) |
| 10-12 | 16 | 0 | — | ✅ |
| 10-13 | 12 | 0 | — | ✅ |
| 10-14 (to 15:24Z) | 7 | 0 | — | ✅ |

**Consecutive green since the reset: 5 d 23.5 h** (2026-10-08T15:57Z → 2026-10-14T15:24Z).

## Failures, classified

| Run | Time (UTC) | Commit | Failed | Classification |
|---|---|---|---|---|
| [37775675230](https://github.com/spaarke-dev/spaarke/actions/runs/37775675230) | 10-08 12:16 | `11dc9b0da` (PR #1359) | Tier 1 `Compile (Debug)` + all four Tier 2 shards' `Build` | **Legitimate.** `CS0411` in `tests/integration/Sprk.Bff.Api.IntegrationTests/Events/EventRoutesLiveTests.cs(407,56)`. Two independently green PRs broke master together (a semantic conflict). Issue #1418; fixed by PR **#1424**, merged **2026-10-08T15:57:32Z**. The counter resets to the fix. |
| [37780400046](https://github.com/spaarke-dev/spaarke/actions/runs/37780400046) | 10-08 12:55 | `23c359eaf` (PR #1386) | Tier 1 `Compile (Debug)` | Same break, still unfixed at that point. |
| [38069733811](https://github.com/spaarke-dev/spaarke/actions/runs/38069733811) | 10-10 16:57 | `907fda3b8` (PR #1605) | Tier 1 `Arch Tests` | **Flake.** The `Task 167 f2-v2` Production-boot tests in `tests/Spaarke.ArchTests/RouteAuthorizationGuardTests.Runtime.cs` reach a real IMDS token (`MsalServiceException: Identity not found`). The next master run, 37 min later on an unrelated commit, was green. Already filed as **#1672** and **#1784** (both OPEN). |
| [38161601966](https://github.com/spaarke-dev/spaarke/actions/runs/38161601966) | 10-11 17:54 | `848f63913` | Tier 1 `Arch Tests` | **Flake** — same tests, same error; the next master run was green. |
| [37856261377](https://github.com/spaarke-dev/spaarke/actions/runs/37856261377) (Tier 2 only) | 10-09 00:02 | `d7fdcafc3` | Tier 2 shard `rest` verdict `fail` | **Infra.** Every test project reported `Failed: 0` (incl. Sprk.Bff.Api.Tests 13,704 passed), but `dotnet test` exited non-zero with no failed test — the shard verdict reads that as a crash. One occurrence in 52 Tier 2 runs. |

## Flake rate (spec NFR-03: Tier 1 flake < 1%)

Since the reset (2026-10-08T16:00Z): **710 Tier 1 job executions, 2 failures = 0.28%** — this is the job-level
metric `notes/cutover-success.md` §4 used. ✅ Within NFR-03.

At **run** level the same 2 flakes are **2 of 101** master runs that executed Tier 1 (**2.0%**), all in the one
`Arch Tests` job. The job-level number passes; the run-level one would not. The cause is the open defect
#1672 / #1784, which already has issues. It is not a reason to reset the counter (the POML says flakes do not
reset), but it is the main threat to the next soak window.

## Tier 2 (advisory) record

52 master runs executed `Full Unit Tests (Debug)` since 2026-10-07: **50 all-shards-pass**, 1 the 10-08
compile break above, 1 the 10-09 infra crash above. **No test failed both passes in any Tier 2 run.**

## Before 2026-10-07 (context only, outside the window)

- 2026-10-01 → 10-03: every master run was **cancelled** — Tier 2 `Full Unit Tests` hit its 30-minute guard
  (Sprk.Bff.Api.Tests had grown to ~49 min), producing no verdict. Fixed 2026-10-05 by sharding Tier 2.
- 2026-10-05 20:37Z (run 37370930255): Router red because several Tier 1 jobs were **cancelled**, not failed — infra.

## How to re-measure (for the 2026-10-15T15:57Z re-check)

```bash
# master Router runs since the reset; any failure after 2026-10-08T15:57Z needs classifying
gh run list --workflow=ci-router.yml --branch=master --event=push --created ">=2026-10-08" \
  --limit 400 --json databaseId,conclusion,createdAt,headSha,displayTitle \
  --jq '.[] | select(.conclusion=="failure") | "\(.databaseId) \(.createdAt) \(.headSha[0:9]) \(.displayTitle)"'
# failing jobs/steps of one run
gh run view <id> --json jobs --jq '.jobs[] | select(.conclusion=="failure") | .name'
# Tier 2 truth = the aggregator log, not the job conclusion
gh api repos/spaarke-dev/spaarke/actions/jobs/<aggregator-job-id>/logs | grep -E "verdict-Debug-.*: (pass|fail)|missing"
```

## Open owner decision (not taken here)

The reset comes from a **semantic-conflict compile break**: two PRs, each green under the Router, broke
master when both merged. The owner declined a merge queue in task 071, so that failure shape is accepted by
design, and it says nothing about the stability of the *test suite* that FR-B07 is protecting. If the owner
rules that semantic-conflict breaks do not reset the soak, the window from 2026-10-07 is green apart from
documented flakes, and the gate is met now. That is a reading of FR-B07 for the owner to make. Applied
literally (POML step 3: "legitimate failure → reset"), the gate passes at **2026-10-15T15:57Z**.
