# sdap-ci.yml retirement — task CICD-077 (2026-10-14)

> Gates: cutover 2026-09-29 (071, `notes/cutover-success.md`); 7-day soak passed 2026-10-14 by owner ruling
> (075, `notes/soak-passed.md`); cutover+14d = 2026-10-13.
> Owner rulings 2026-10-14: move, never delete; checks with no per-PR equivalent go into ONE new standalone,
> non-required workflow; Release build (FR-B07) goes there too; master failures raise quality alerts;
> do not edit `ci-router.yml` / `ci-tier1-blocking.yml` / `ci-tier2-advisory.yml`.

## What moved where

| Legacy job / step (`sdap-ci.yml`) | Disposition | Now |
|---|---|---|
| **Security Scan**: Trivy fs scan, SARIF to the Security tab | **Preserved** (review fix F3) | `ci-standalone-checks.yml` `trivy-fs`: same settings (`skip-dirs: knowledge`, `.trivyignore`, SARIF, upload with `if: always()`), same SARIF category (`.github/workflows/sdap-ci.yml:security-scan`) so existing code-scanning alerts continue as one series. Reports only; alerts only if the scan itself errors. `nightly-health.yml` `trivy-fs` still counts HIGH/CRITICAL nightly. |
| **Build & Test (Debug)**: full suite, two-pass classifier | Mapped | Tier 2 `Full Unit Tests (Debug)`, 4 shards, same classifier (`scripts/ci/classify-and-retry.ps1`). Master failures now raise `tier2-master/full-unit-tests` (`master-ci-alert.yml`). |
| Build & Test: coverage collection + upload | Mapped | `nightly-health.yml` `coverage-observation` (ADR-038: coverage is observation, never a gate). |
| Build & Test: `Release` matrix entry (removed 2026-06-24; FR-B07 restore) | **Preserved** | `ci-standalone-checks.yml` `release-build`: Release build of the whole solution. Tests stay in Debug (Tier 2). |
| Build & Test: `dotnet test` where the Router `bff` filter does not match (review fix F2) | **Mapped** (2026-10-15, PR #1922) | Tier 2 `Full Unit Tests`, triggered by the Router's `dotnet_tests` filter; the interim standalone jobs `unit-tests-outside-router` / `bff-seam-contract-tests` were removed. Gap: BFF source-scan tests reading `src/client/**` / `src/solutions/**` run nightly only. |
| **Eval Gate (Golden Utterances)** | Mapped | Tier 1 `eval-gate` (same command, blocking). |
| **Client Quality**: PCF ESLint | **Preserved** | `ci-standalone-checks.yml` `pcf-eslint` (same command). Tier 2 `lint` runs it only when SpaarkeAi/BFF paths change, and `client-lint.yml` does not cover `src/client/pcf/**`, so a PCF-only PR would otherwise get no ESLint. |
| Client Quality: Prettier check | Mapped (legacy check was vacuous) | It ran **after** `prettier --write`, so it could never fail. Tier 2 `lint` runs a real `prettier --check` on SpaarkeAi/BFF PRs; `.husky/pre-commit` formats staged files. A real check finds **48** unformatted files under `src/client/` today (local run 2026-10-14), which the legacy job hid. |
| Client Quality: Prettier auto-fix | **Dropped** | Rewrote the workspace and pushed nothing since 2026-08-28; `.husky/pre-commit` formats staged files. |
| Client Quality: 6 shared-lib `tsc` builds + `Spaarke.AI.Widgets` `tsc --noEmit` | **Preserved** | `ci-standalone-checks.yml` `client-typecheck` (no other workflow runs `tsc` on these libraries per push). |
| **Code Quality**: `dotnet format whitespace --verify-no-changes` | Mapped | Tier 2 `format`. |
| Code Quality: ArchTests (NetArchTest) | Mapped | Tier 1 `arch-tests` (full suite, **blocking**) + Tier 2 `adr-compliance`. |
| Code Quality: plugin size (ADR-002) | Mapped | Tier 2 `plugin-size`. Both pass trivially: the repo has had no net4x plugin since 2026-09-25. |
| Code Quality: **Dependencies audit** | **Preserved** (+ fail-closed fix) | `ci-standalone-checks.yml` `dependency-audit`; on master → alert `ci-standalone/dependency-audit`. Legacy step passed when `dotnet list` itself errored; now a non-zero exit fails. |
| Code Quality: naming conformance | Already moved | Tier 1 `naming-conformance` (2026-10-06, T230a). |
| **Tenant Isolation (I1–I5)** | Mapped | Tier 1 `tenant-isolation` (blocking). |
| **Integration Readiness**: BFF Release publish + `deployment-packages` artifact | **Preserved** | `ci-standalone-checks.yml` `release-build`. Same artifact name and `api/` layout (`deploy-promote.yml` downloads it); uploaded on master pushes only. |
| Integration Readiness: "Environment readiness check" | **Dropped** | Printed fixed "✓" lines; checked nothing. |
| **ADR Violations Report** (PR comment) | **Dropped** (duplicate) | Tier 2 `tier2-pr-comment` reports ADR Compliance in its own PR comment; Tier 1 `arch-tests` blocks. |
| **CI Summary** | **Dropped** | Static table + boilerplate "Next Steps"; replaced by the Router decision summary and the standalone `Summary` job. |
| **Compose Fidelity Gate** | Mapped | Tier 1 `compose-fidelity-gate` (blocking). |
| **Compose Client Gate** | **Preserved** | `ci-standalone-checks.yml` `compose-client-gate`, no longer `continue-on-error` (its flip condition was met: green on every observed master run). On master → alert `ci-standalone/compose-client-gate`. |
| `sdap-ci-docs-only.yml` (placeholder "Build & Test" checks) | **Dropped** | Existed only to satisfy a required check that branch protection no longer has. |

Both legacy files were **moved** to `docs/archive/workflows/` (GitHub does not execute them there).

## Alert wiring (owner requirement: CI failures are never silently lost)

| Alert (`check`) | Raised by | Raised when | Cleared when |
|---|---|---|---|
| `router-master` | `master-ci-alert.yml` (`workflow_run` on `CI` completed, master push) | Router or any non-Tier-2 job not success (cancelled/timed out count as failing) | next green master Router run |
| `tier2-master/full-unit-tests` | `master-ci-alert.yml` | a Tier 2 shard that ran has verdict ≠ `pass` or no verdict | next master run whose shards all pass (skipped when no shard ran or master did not compile) |
| `ci-standalone/dependency-audit` | `ci-standalone-checks.yml` `alerts` (push to master) | job not success | next green master run |
| `ci-standalone/release-build` | same | job not success | next green master run |
| `ci-standalone/compose-client-gate` | same | job not success | next green master run |
| `ci-standalone/client-typecheck` | same | job not success | next green master run |
| `ci-standalone/pcf-eslint` | same | job not success | next green master run |
| `ci-standalone/verdict-script-tests` (the `unit-tests-outside-router` / `bff-seam-contract-tests` alerts were retired with those jobs in #1922; failures now raise `tier2-master/full-unit-tests`) | same | job not success | next green master run |
| `ci-standalone/trivy-fs` | same | the scan itself errored (findings never fail it) | next green master run |
| `master-ci-alert/evaluation` | `master-ci-alert.yml` | the verdict script failed (GitHub API errors after 3 retries, script bug), so the two alerts above were not updated | next successful evaluation |

All use `.github/actions/quality-alert` (PR #1837): one issue per check, label `quality-alert`, assigned to the
owner, edited in place, closed with "Recovered". Decision logic for the first two is
`scripts/ci/master-ci-verdict.ps1`. It judges master's **current state**, not the triggering run alone: the newest
100 completed master push runs, each job (and each Tier 2 shard) from the most recent run that executed it. So a
docs-only push (Tier 1 skipped, Router green) cannot close a compile-break alert, a path-gated job keeps its last
real result, and runs finishing out of order cannot hide an older run's failure. A job the open alert issue lists as failing that no run in the window executed stays failing ("has not executed since …"), so a path-gated failure cannot age out and be reported Recovered (review fix F4); a newer queued/running master run holds `ok` back (K1). Known limit: a job that failed and was then renamed or removed never runs again, so its alert stays open until closed by hand. The standalone alerts skip posting when a newer, non-cancelled master
run of that workflow has already completed (K2). Replay any past state with `-AsOf`:

| Replayed run | router-master | tier2 | |
|---|---|---|---|
| 37775675230 (2026-10-08, CS0411) | failing (Compile, Router) | ok (shards judged from the last compiling run) | ✅ the alert that was missing |
| 38069733811 (2026-10-10, IMDS flake) | failing (Arch Tests, Router) | ok (from earlier runs) | ✅ |
| 37856261377 (2026-10-09, shard crash) | ok | failing (`rest`) | ✅ |
| 37370930255 (2026-10-05, cancelled) | failing (3 cancelled Tier 1 jobs, Router) | failing (4 shards without verdict) | ✅ |
| 38324167927 (2026-10-11, docs-only, Tier 1 skipped) | ok (Tier 1 jobs judged from earlier runs, all green) | ok | ✅ no false close possible |
| 38576680033 (2026-10-14, green) | ok | ok | ✅ |

## deploy-promote.yml (spec MUST: "functionally untouched")

Its three `dawidd6/action-download-artifact` steps named `workflow: sdap-ci.yml`. With an explicit
`artifact_run_id` that name is only logged (v14 lists artifacts by run id); with a blank one it searches
`sdap-ci.yml` for the latest successful run. After retirement that search can only find stale artifacts
(up to 30 days old) and then nothing. The three lines now name `ci-standalone-checks.yml`, which is the only
change, made to keep the workflow functioning as before. It is a separate commit so it can be reverted alone.

Review fix K3: the three download steps now also set `branch: master` (only when `artifact_run_id` is blank:
the action rejects `branch` together with `run_id`), `event: push`, `workflow_conclusion: completed` and
`search_artifacts: true`. A blank run id therefore takes the latest completed master push run that carries
`deployment-packages`; the default `workflow_conclusion: success` would have skipped a run whose `release-build`
passed but an unrelated standalone job was red and silently picked an older build. `if_no_artifact_found` was
`error`, which is not a valid value (`fail | warn | ignore`) and fell through to `ignore`; it is now `fail`.
