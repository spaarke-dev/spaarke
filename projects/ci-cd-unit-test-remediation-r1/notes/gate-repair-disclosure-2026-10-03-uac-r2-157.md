# Gate-repair disclosure: `ci-tier1-blocking.yml`, 2026-10-03 (unified-access-control-r2 task 157)

> **From**: `unified-access-control-r2`, task 157 (fix rounds c2, c2-r1 and c2-r2)
> **To**: `ci-cd-unit-test-remediation-r1`, owner of the shadow window and of `ci-router.yml`
> **Why this note exists**: the freeze banner in `projects/INDEX.md` says a GATE REPAIR to a frozen tier file is
> allowed, but must be "disclosed in a note to `ci-cd-unit-test-remediation-r1`". This is that note. The full record
> (what, why, verdict impact) is in `projects/INDEX.md`, section "Disclosure record — `ci-tier1-blocking.yml` touched
> 2026-10-03 by `unified-access-control-r2` (task 157, residual 4)". This note summarises it and does not replace it.
>
> **Revised in fix round c2-r2 (owner round 13, 2026-10-03).** Item 11: the gate lands ADVISORY and becomes blocking
> after N green runs on the runner. Item 12: `ci-router.yml` is not changed by `unified-access-control-r2`; its
> pre-existing `docs_only` skip belongs to this project, and the uac-r2 main session files a GitHub issue here. The
> earlier version of this note described a blocking job plus three router `docs_only` exclusions. Neither merged.

## What changed (net, as it will merge)

| File | Change |
|---|---|
| `.github/workflows/ci-tier1-blocking.yml` | New job `datagrid-external-host-gate`, **advisory** (job-level `continue-on-error: true`): `npm install`, then `npx jest --ci` on `Spaarke.UI.Components/src/components/DataGrid/`, then an assertion step that requires `DataGrid.externalHost.test.tsx` to have run with at least 7 tests, all passing. `classify-tier1` gains a `datagrid_gate` paths-filter output (`src/client/shared/Spaarke.UI.Components/**`, `.github/workflows/**`). The job `needs: classify-tier1` and runs unless that output is exactly `'false'`, and always on `workflow_dispatch`. No new `workflow_call` input. |
| `.github/workflows/ci-router.yml` | **No change.** |
| `tests/Spaarke.ArchTests/ExternalSpaGridViewSelectorGuardTests.cs` | `SharedDataGrid_ExternalHostJestSuiteRunsAsATier1Gate`, 24 seeded Theory rows and one state fact refuse the named ways the job could stop running or reporting. It reads only `ci-tier1-blocking.yml`, so it never constrains a router change of yours. |

## Why it is a gate repair

The external-host jest suite is the only check that catches an added nested-scope shadow of the forced
`showViewSelector` in the shared `DataGrid.tsx`. Before this change, no PR-triggered workflow ran that package's jest,
so it was "a guard present but not armed".

## Verdict impact on the shadow window

1. **None while advisory.** A red job is visible in the run but does not fail Tier 1 or `CI / Router`. (That is
   GitHub's documented behaviour for job-level `continue-on-error`; the first red run on the runner will confirm it.)
2. **The flip will be a verdict change**, disclosed when it lands. FLIP CONDITION: three consecutive runs of the job on
   `ubuntu-latest`, on diffs that do not change `src/components/DataGrid/`, with the jest step and the assertion step
   both `success` and no red such run in between. Then one PR deletes the `continue-on-error: true` line and sets
   `DataGridGateAdvisory` to `false` in the guard (the guard refuses either alone), and cites the run ids.
3. **No `docs_only` reclassification.** The c2 / c2-r1 router exclusions were reverted.

## For you: the `docs_only` skip (owner round 13 item 12; the uac-r2 main session files the issue)

`docs_only` is true when a docs-class file changed and none of the named code filters (`bff`, `spaarke_ai`,
`ci_workflows`) matched. Any path that no filter names therefore counts as docs when it ships with a `*.md` file or a
`projects/**` note, and Tier 1 and Tier 2 are skipped. For task 157 that means: a PR that edits `DataGrid.tsx`, the
external SPA (`src/client/external-spa/**`) or the arch guard (`tests/Spaarke.ArchTests/**`) together with only
docs-class files skips this job and the arch-tests job. Other examples: `src/client/pcf/**`, `src/solutions/*` other
than SpaarkeAi, `src/client/office-addins/**`, `scripts/**`, `infra/**`, and `tests/**` outside the BFF.

A fail-closed design, for your decision: a second `dorny/paths-filter` step with `predicate-quantifier: 'every'` and
one filter, `'**'` plus the negated docs patterns; `docs_only` would be true only when that filter does not match. It
would change verdicts for many PRs, which is why it is yours. Recorded in task 157's note, §8 F3.
