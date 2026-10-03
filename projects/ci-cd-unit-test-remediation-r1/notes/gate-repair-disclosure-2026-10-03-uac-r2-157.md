# Gate-repair disclosure: `ci-tier1-blocking.yml` + `ci-router.yml`, 2026-10-03 (unified-access-control-r2 task 157)

> **From**: `unified-access-control-r2`, task 157 (fix rounds c2 and c2-r1)
> **To**: `ci-cd-unit-test-remediation-r1`, owner of the shadow window
> **Why this note exists**: the freeze banner in `projects/INDEX.md` says a GATE REPAIR to a frozen tier file is
> allowed, but must be "disclosed in a note to `ci-cd-unit-test-remediation-r1`". This is that note. The full record
> (what, why, verdict impact) is in `projects/INDEX.md`, section "Disclosure record — `ci-tier1-blocking.yml` +
> `ci-router.yml` touched 2026-10-03 by `unified-access-control-r2` (task 157, residual 4)". This note summarises it and
> does not replace it.

## What changed

| File | Change |
|---|---|
| `.github/workflows/ci-tier1-blocking.yml` | New BLOCKING job `datagrid-external-host-gate`: `npm install`, then `npx jest --ci` on `Spaarke.UI.Components/src/components/DataGrid/`, then an assertion step that requires `DataGrid.externalHost.test.tsx` to have run with at least 7 tests, all passing. New `workflow_call` input `ui_components_changed`, default `'true'` (fail closed). |
| `.github/workflows/ci-router.yml` | New paths-filters `ui_components` (`src/client/shared/Spaarke.UI.Components/**`, also passed to Tier 1 OR'd with `ci_workflows`), `external_spa` (`src/client/external-spa/**`) and `arch_tests` (`tests/Spaarke.ArchTests/**`). All three are added to the `docs_only` exclusion. |
| `tests/Spaarke.ArchTests/ExternalSpaGridViewSelectorGuardTests.cs` | `SharedDataGrid_ExternalHostJestSuiteIsABlockingTier1Gate` plus 31 seeded negative controls refuse the named ways the job could stop blocking. |

## Why it is a gate repair

The external-host jest suite is the only check that catches an added nested-scope shadow of the forced
`showViewSelector` in the shared `DataGrid.tsx`. Before this change, no PR-triggered workflow ran that package's jest,
so it was "a guard present but not armed". The `docs_only` part is the same repair. A PR that edits the grid, the
external SPA or the arch guard, together with any `*.md` file or task note, was classed docs-only and skipped Tier 1.

## Verdict impact on the shadow window

1. **New job.** It runs only when `Spaarke.UI.Components` or a workflow changes, and it is green today.
2. **`docs_only` reclassification. This changes verdicts.** PRs that touch one of the three paths plus only
   docs-class files used to skip Tier 1 and Tier 2. They now run both, so they can go red where they were green before.
   When comparing verdicts, read these as a classification change, not as disagreement between the two systems.
3. **Re-baseline the window** from the merge that carries this change, per the carve-out.

## Found, not fixed here: `docs_only` fails open for every unclassified path

`docs_only` is true when a docs-class file changed and none of the named code filters (`bff`, `spaarke_ai`,
`ci_workflows`, and now `ui_components`, `external_spa`, `arch_tests`) matched. Any path that no filter names therefore
counts as docs when it ships with a `*.md` file or a `projects/**` note, and Tier 1 is skipped. Examples:
`src/client/pcf/**`, `src/solutions/*` other than SpaarkeAi, `src/client/office-addins/**`, `scripts/**`, `infra/**`,
and `tests/**` outside the BFF and arch projects. Task 157 closed this only for the paths its own guards cover.

A fail-closed fix would add a second `dorny/paths-filter` step with `predicate-quantifier: 'every'` and one filter,
`'**'` plus the negated docs patterns. `docs_only` would then be true only when that filter does not match. That
change would alter verdicts for many more PRs, so it is this project's decision, not ours. Recorded in task 157's
note, §8 F3.

## Open item for the owner

The job has not yet run on `ubuntu-latest`. Locally it has run on Node 22.14 and on Node 20.20.2, the major the job
pins, both on Windows. The repo's standard of "N green runs on the runner before a gate blocks" is waived by a
main-session ruling, which owner round 10 lists as reversible and recorded for the owner. The owner has not confirmed
it.
