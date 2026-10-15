# Soak passed — task CICD-075 (FR-B07 / SC-06)

> **Declared**: 2026-10-14, on the owner's ruling of the same day
> **Window**: 2026-10-07T15:24Z → 2026-10-14T15:24Z (7 consecutive days), master push runs of `CI` (`ci-router.yml`)
> **Full measurement and failure classification**: `notes/soak-monitoring.md`

## Verdict

✅ **The surviving suite was green for 7 consecutive days.** Of 147 completed master Router runs in the window (4 red),
every red is either a documented flake or the one merge collision the owner ruled out:

| Red | Classification | Resets? |
|---|---|---|
| 37775675230, 37780400046 (2026-10-08) | Merge collision: CS0411, two independently green PRs (issue #1418, fixed by PR #1424) | **No, by owner ruling 2026-10-14** |
| 38069733811 (2026-10-10), 38161601966 (2026-10-11) | Flake: Arch Tests `Task 167 f2-v2` reaches a real IMDS token (#1672, #1784) | No (POML step 3: flakes do not reset) |
| 37856261377 (2026-10-09, Tier 2 `rest` shard only) | Infra: test host exited non-zero with 0 failed tests | No |

Tier 1 job-level flake rate since the break was fixed: 2 of 710 = **0.28%** (NFR-03 < 1%).
Tier 2 `Full Unit Tests (Debug)`: no test failed both passes in any of the 52 master runs.

## Evidence — one green master Router run per day

| Day (UTC) | Run | Commit |
|---|---|---|
| 2026-10-07 | [37648907905](https://github.com/spaarke-dev/spaarke/actions/runs/37648907905) | `081065863` |
| 2026-10-08 | [37710455459](https://github.com/spaarke-dev/spaarke/actions/runs/37710455459) | `ccde3eb6d` |
| 2026-10-09 | [37864461389](https://github.com/spaarke-dev/spaarke/actions/runs/37864461389) | `871f19138` |
| 2026-10-10 | [38007173264](https://github.com/spaarke-dev/spaarke/actions/runs/38007173264) | `7e0291299` |
| 2026-10-11 | [38105381095](https://github.com/spaarke-dev/spaarke/actions/runs/38105381095) | `8daf41797` |
| 2026-10-12 | [38202324328](https://github.com/spaarke-dev/spaarke/actions/runs/38202324328) | `67e06a626` |
| 2026-10-13 | [38333114422](https://github.com/spaarke-dev/spaarke/actions/runs/38333114422) | `532d441bb` |
| 2026-10-14 | [38576680033](https://github.com/spaarke-dev/spaarke/actions/runs/38576680033) | `6da771b96` |

## What this unblocks

- **Task 077**: retire `sdap-ci.yml`.
- **FR-B07 Release build**: by owner ruling it goes into the new standalone workflow created by 077, not into
  `ci-tier2-advisory.yml` (the tier files are under the owner's standing no-edit rule).
