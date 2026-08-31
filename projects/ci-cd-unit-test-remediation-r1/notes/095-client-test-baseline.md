# Task 095 — client test CI workflow, Phase 1 (#851)

> **Date**: 2026-08-28 · **Branch**: `ci/task-095-client-tests-workflow` · **Status**: plumbing complete; baseline table lands on first dispatch

---

## The gap, verified independently

| | #851 said | Measured 2026-08-28 |
|---|---|---|
| Client test files | 707 | **730** (tracked, `*.{test,spec}.{ts,tsx,js,jsx}`) |
| Packages with a jest script | 39 | **40** |
| Workflows running jest | 0 | **0** — confirmed by `grep -rln "jest" .github/workflows/` |
| Server `.cs` files that DO run in CI | 923 | unchanged |

There is also **no npm workspace root** (`package.json` declares no `workspaces`), so each of
the 40 packages installs independently. That single fact drove the trigger decision below.

## Why this is in scope at all — the split

The spec puts **"React/PCF Jest test architecture" OUT of scope**, and it stays out. How those
tests are designed, organised and fixed is not this project's business.

But *"no workflow runs jest at all"* is a **CI gap**, and CI is precisely what this project owns.
So task 095 adds the workflow, baselines every package, and **stops**. No jest test file was
modified. Remediation is handed on with numbers attached rather than absorbed.

---

## What shipped

`.github/workflows/client-tests.yml` — three jobs:

1. **discover** — builds the matrix by *scanning* for packages with a real jest test script.
   A hardcoded list would drift the first time someone adds a package, and a baseline that
   quietly stops covering things is the same false-green this project exists to remove. It also
   rejects the npm-init default (`"Error: no test specified" && exit 1`) so a stub script is not
   counted as coverage, and **fails loudly if it discovers zero packages** rather than reporting
   a vacuous green.
2. **test** — one package per matrix leg, `continue-on-error: true`, `fail-fast: false`,
   `max-parallel: 8`. Install failure and test failure are recorded as **distinct** statuses;
   collapsing them would report a stale lockfile as "tests failing" and send the next person to
   debug the wrong thing.
3. **summarize** — emits the per-package table into the job summary (readable without
   downloading anything) plus a 90-day artifact. If fewer legs reported than were discovered, it
   prints an explicit **incomplete-baseline** warning, so a partial run can never read as a
   complete one.

### `npm install`, not `npm ci`

Per root CLAUDE.md §12: roughly 14 of 16 Vite solutions carry stale `package-lock.json` files and
`npm ci` fails outright on them. Uses `npm install --legacy-peer-deps --no-audit --no-fund`.

---

## Why it does NOT run on `pull_request` (deliberate)

40 packages with no workspace root means 40 independent `npm install`s. Hanging that off every PR
would contend for runners with the very PRs the **CI shadow window** is trying to accumulate
(20 agreeing PRs) — working directly against owner north star #2, *"CI must not hold up
high-frequency master builds and pushes"*.

Phase 1's job is to **SEE** the surface, not gate it, and a nightly baseline sees it just as well
at a fraction of the contention. Triggers are `schedule` (07:00 UTC — one hour after
`nightly-health.yml` at 06:00, so the two do not collide) plus `workflow_dispatch`.

**Adding `pull_request` is part of the Phase 2 promotion, not an oversight.**

## Freeze-safety

**New file.** No existing workflow was touched, so the shadow-window freeze on
`ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml` is not engaged. The workflow
appears in **no blocking filter** and every step is `continue-on-error` — it cannot block a PR
even in principle.

---

## Baseline

The full 40-package table is produced by the first run. `workflow_dispatch` requires the workflow
to exist on the **default branch**, so the baseline is generated **after this PR merges**, then
recorded here.

**Local spot-check** (validating the exact CI command shape, `npm test -- --ci --reporters=default
--passWithNoTests=false`):

| Package | Result |
|---|---|
| `src/client/shared/Spaarke.SdapClient` | ❌ **fail** — `Test Suites: 1 failed, 1 total; Tests: 0 total`. Module resolution blows up at `src/__tests__/SdapApiClient.test.ts:3:25` before a single test executes. |

That is precisely the class of finding this workflow exists to surface: a package whose tests have
not run in CI once, failing at *import* time, under a board that showed green.

> **Follow-on**: once the baseline is in hand, file the failures for the client surface owners.
> Fixing them is explicitly NOT this task and NOT this project.

---

## Promotion path (NOT this task)

1. Shadow window closes; `sdap-ci.yml` retired (tasks 071 → 077).
2. Drive the baseline to green — **owned by the client surface, not by CI**.
3. Only then add `pull_request` and move it into a blocking filter.

Do not add this workflow to `ci-tier1-blocking.yml` or `ci-tier2-advisory.yml` before step 3.
