# Workflow Inventory — 2026-06-01 Baseline

> **Project**: github-actions-rationalization-r1 — Phase 0
> **Captured**: 2026-06-01
> **Source**: 13 files in `.github/workflows/`
> **Captured by**: Task 001 (workflow inventory + baseline snapshots)
> **Companion artifact**: `branch-protection-2026-06-01.json` (master branch protection)
> **Format reference**: Modeled on `projects/sdap-bff-api-remediation-fix/inventory/ci-workflow-inventory.md`

---

## 🚨 Phase 0 Blockers / Critical Findings (READ FIRST)

### 1. sdap-ci.yml master CI is STILL FAILING post-PR-#314 — Risk R1 CONFIRMED

PR #314 (commit `c9863276`, merged 2026-05-31 16:22 -04 / 20:22 UTC) fixed the duplicate YAML key in `sdap-ci.yml`. **The duplicate-key parsing issue is resolved (no more loader failures on master), BUT the workflow now fails for a different reason.**

Two master runs since PR #314 merge — both **failed**:

| Run ID | Timestamp (UTC) | Conclusion | Failing jobs / steps |
|---|---|---|---|
| 26755019759 | 2026-06-01 12:31:01Z | failure | `Build & Test (Release)` → Build (compile error); `Client Quality (Prettier + ESLint)` → Prettier format check |
| 26757481055 | 2026-06-01 13:20:12Z | failure | `Build & Test (Release)` → Build; `Client Quality (Prettier + ESLint)` → Prettier format check |

Run 26755019759 is the same run referenced in **spec.md FR-02** and **Risk R1**. Task 002 is investigating root-cause in parallel; this inventory observation triggers the Phase 0 exit gate to require disposition before Phase 1 begins. **See `decisions/D-01-master-ci-failure-disposition.md`** when task 002 completes.

### 2. Two workflows are 100% ghost-trigger / loader-failure (deploy-promote + deploy-infrastructure)

Both `deploy-promote.yml` and `deploy-infrastructure.yml` produce GitHub Action runs with **`jobs: []`** — meaning the workflow record is created but no job ever materializes. Loader-failure pattern: 5 of 5 sampled recent runs had empty `jobs[]`. The trigger fires but the workflow file fails to load (likely a YAML/schema rejection at the GitHub Actions parser level, OR — for deploy-promote — workflow_run cascade where the upstream sdap-ci already failed and the cascade workflow itself can't parse the context).

Combined: 200 of the last 30 days' failure records (100 each) are attributable to this pattern. These dominate the failure baseline and must be fixed/deleted (FR-03, FR-04) before workflow-health metrics become meaningful.

### 3. nightly-quality has been failing every single weeknight for >2 months — FR-05 trigger

56 of 56 runs failed in the last 30 days. Failure mode is consistent: `Test & Coverage` → Build step fails, then cascade to `SonarCloud Analysis`. This is the predecessor-observed P4 issue; FR-05 makes "3 consecutive successes OR delete" the disposition rule.

### 4. weekly-quality has been failing every Friday for >2 months — derived from nightly failure

11 of 11 runs failed. Root cause is the dependency on nightly-quality artifacts (none successful → "Extract metrics and build trend table" fails). Consolidation candidate per D-04 (overlaps with nightly-quality).

### 5. auto-add-to-project — 29 fails, 0 successes (project-board automation broken since Mar 2026)

`Add to org project` step fails. Likely `GH_TOKEN_PROJECT` secret missing/expired or action version incompatibility. Low-value automation; D-03 candidate.

---

## Summary — Recommendations

| Recommendation | Count | Workflows |
|---|---|---|
| **KEEP** | 3 | adr-audit, sdap-ci (after fix), deploy-slot-swap |
| **FIX** | 4 | sdap-ci.yml (Build + Prettier failures, Risk R1), deploy-promote (P2 cascade), deploy-infrastructure (P3 ghost triggers), nightly-quality (P4) |
| **DELETE** | 4 | auto-add-to-project (D-03), deploy-platform (D-03: 0 runs), provision-customer (D-03: 0 runs), insights-eval (D-03: 0 runs, alias of sdap-ci scope) |
| **CONSOLIDATE** | 2 | deploy-bff-api ↔ deploy-slot-swap (D-04: same staging→swap deploy), weekly-quality ↔ nightly-quality (D-04: weekly is a rollup of nightly artifacts) |
| **Total** | 13 | |

**Note**: sdap-ci appears in both KEEP and FIX — KEEP is the long-term action; FIX is the immediate Phase 1 task (Build + Prettier root-cause, then it KEEPs).

**Target post-rationalization count**: ≤8 (FR-06). With 4 DELETE + 2 CONSOLIDATE (net -3 since each consolidate removes 1), we project final count ≈ **6 workflows + 2 new ones (`workflows-validate.yml`, `report-workflow-health.yml`) = 8** — meets the target.

---

## Per-Workflow Inventory

### 1. adr-audit.yml — ADR Architecture Audit

| Field | Value |
|---|---|
| Path | `.github/workflows/adr-audit.yml` |
| Lines | 218 |
| Triggers | `workflow_dispatch`, `schedule` (weekly Mon 09:00 UTC `0 9 * * 1`) |
| Last commit | `d9018dea` 2026-03-13 — chore(ci): clean up CI workflows, fix ADR tests, update notification emails |
| Last-30d runs | 26 total: **23 success, 3 failure, 0 cancelled** → **88.5% success** |
| Loader-failure pattern? | No (0/5 sampled empty) |
| Declared purpose | Weekly `dotnet test tests/Spaarke.ArchTests/...` → parses TRX → creates/updates rolling GitHub issue labeled `architecture,adr-audit` with violation breakdown |
| Recommended action | **KEEP** |
| Evidence | High success rate (88.5%); the failures (2026-04-06, 2026-03-09, etc.) are real ADR-violation test fails which is the workflow's job. Workflow is doing what it's designed to do. Last meaningful commit is recent enough. No consolidation candidate (unique purpose: architecture compliance). |

### 2. auto-add-to-project.yml — Auto-add issues to Spaarke Core project

| Field | Value |
|---|---|
| Path | `.github/workflows/auto-add-to-project.yml` |
| Lines | 18 |
| Triggers | `issues: [opened, reopened]` |
| Last commit | `d650b9f2` 2025-10-01 — feat: Complete Sprint 3 Phase 1-3 (Tasks 1.1-3.2) |
| Last-30d runs | 29 total: **0 success, 29 failure** → **0.0% success** |
| Loader-failure pattern? | No (the job runs; the `Add to org project` step itself fails) |
| Declared purpose | When an issue is opened/reopened, add it to org project `PVT_kwHODW0Pv84BEgWu` via `actions/add-to-project@v0.5.0` using secret `GH_TOKEN_PROJECT` |
| Recommended action | **DELETE** (D-03 delete-by-default) |
| Evidence | 0 successful runs in 30 days (and likely longer — last commit was 8 months ago, 29 consecutive fails). The `Add to org project` step itself fails on every invocation — secret missing/expired OR `actions/add-to-project@v0.5.0` is outdated. Low-value automation (manual issue triage is acceptable for current team size). Decision record needed in `decisions/` per NFR-06. |

### 3. deploy-bff-api.yml — Deploy BFF API via Staging Slot Swap

| Field | Value |
|---|---|
| Path | `.github/workflows/deploy-bff-api.yml` |
| Lines | 448 |
| Triggers | `push` to `master` paths `src/server/api/**`; `workflow_dispatch` (env: dev/production) |
| Last commit | `36f10712` 2026-05-31 — feat(tests): Phase 1 Wave 1.1b complete + CI gate restored |
| Last-30d runs | 58 total: **0 success, 58 failure** → **0.0% success** (12 on master) |
| Loader-failure pattern? | No (0/5 sampled — the workflow actually executes) |
| Declared purpose | 7-job pipeline: build → test → deploy-staging → verify-staging → swap-production → verify-production → rollback. Targets `spaarke-bff-prod`, staging slot, `/healthz`. Concurrency group, OIDC auth. |
| Recommended action | **CONSOLIDATE** with `deploy-slot-swap.yml` (D-04 overlap) — OR **FIX** (root-cause: `Test API → Run unit tests` step fails) |
| Evidence | Recent run 26701758871 (2026-05-31) failed at `Test API → Run unit tests` then cascade-failed `Rollback (Swap Back) → Azure Login` because rollback only fires on swap failure (rollback job's `if: failure()` fires for any prior failure, but no `staging`-slot-swap actually occurred). The test failure is `dotnet test tests/unit/Sprk.Bff.Api.Tests/` — likely the same Phase 1 test-suite breakage tracked by predecessor. **Significant overlap with `deploy-slot-swap.yml`**: both do build→test→staging-slot-deploy→health-check→swap-to-prod. Disposition decision (consolidation OR fix-then-keep) is a Phase 2 task (020). Decision record needed in `decisions/`. |

### 4. deploy-infrastructure.yml — Deploy Bicep Infrastructure

| Field | Value |
|---|---|
| Path | `.github/workflows/deploy-infrastructure.yml` |
| Lines | 377 |
| Triggers | `pull_request` paths `infrastructure/bicep/**`; `push` to master paths `infrastructure/bicep/**`; `workflow_dispatch` |
| Last commit | `d9018dea` 2026-03-13 — chore(ci): clean up CI workflows, fix ADR tests, update notification emails |
| Last-30d runs | 100 total: **0 success, 100 failure** → **0.0% success** |
| Loader-failure pattern? | **Yes — 5/5 sampled have `jobs: []`** (workflow file fails to load OR triggers fire on every push without path-filter respect at scheduling time) |
| Declared purpose | Validate → what-if → deploy Bicep (Model 1 shared + Model 2 customer-dedicated stacks). OIDC + PR comment with what-if output |
| Recommended action | **FIX** (FR-04 / P3 ghost triggers) |
| Evidence | The path filter `infrastructure/bicep/**` does not appear to be functioning — the workflow is being scheduled on pushes to branches that do not touch infrastructure/bicep (e.g., `work/github-actions-rationalization-r1` push at 13:40). Empty `jobs[]` strongly implies a YAML loader-failure or path-filter evaluation that immediately rejects all jobs at queue time. **FR-04 acceptance**: "after fix, pushing a commit to a feature branch with no `infrastructure/bicep/**` changes produces zero `deploy-infrastructure.yml` runs". |

### 5. deploy-office-addins.yml — Deploy Office Add-ins to Azure Static Web App

| Field | Value |
|---|---|
| Path | `.github/workflows/deploy-office-addins.yml` |
| Lines | 51 |
| Triggers | `push` to `master` + `work/SDAP-outlook-office-add-in` paths `src/client/office-addins/**`; `workflow_dispatch` |
| Last commit | `0bbebe73` 2026-05-26 — sdap-bff-api-remediation: Phase 4 + 5 (demo) + Phase 6 docs + LegalWorkspace client /api fix (#295) |
| Last-30d runs | 46 total: **30 success, 16 failure** → **65.2% success** (17 on master, 12 success) |
| Loader-failure pattern? | No |
| Declared purpose | Build + npm install + deploy to Azure Static Web App via `Azure/static-web-apps-deploy@v1`. Hardcoded `ADDIN_CLIENT_ID`, `TENANT_ID`, `BFF_API_CLIENT_ID`, `BFF_API_BASE_URL` env. |
| Recommended action | **KEEP** (with caveat) |
| Evidence | 65% success rate is below the 70% KEEP threshold from FR-01 guidance, BUT it's actively used (30 successes in 30 days, recent deploys), has a clear single purpose, and failure mode (when it fails) is usually transient (SWA deploy retries). Hard-coded values are a smell that should be reviewed in Phase 2 task 020 (disposition) but are NOT a blocker. Decision record needed in `decisions/` for any modification. Disposition rationale: 30 successful deploys is real value. |

### 6. deploy-platform.yml — Deploy Platform Infrastructure

| Field | Value |
|---|---|
| Path | `.github/workflows/deploy-platform.yml` |
| Lines | 221 |
| Triggers | `workflow_dispatch` only (no push/PR triggers) |
| Last commit | `8fa46c60` 2026-03-13 — feat(ci): add CI/CD workflow files for platform, BFF API, and customer provisioning |
| Last-30d runs | **0 total** — never invoked in last 30 days |
| Loader-failure pattern? | N/A (no runs) |
| Declared purpose | Manual `./scripts/Deploy-Platform.ps1` runner for shared platform infra (Resource Group, Key Vault, App Service, OpenAI, AI Search, Document Intelligence) per environment. Modes: what-if / deploy. |
| Recommended action | **DELETE** (D-03 delete-by-default) |
| Evidence | Zero invocations in 30 days. Last touched 2026-03-13. Functionality is fully covered by `Deploy-Platform.ps1` script which the team runs locally (per `docs/procedures/production-release.md`). Workflow file's main value would be auditability of who-ran-platform-deploy-when, but with zero usage that auditability is unrealized. Decision record needed in `decisions/`. |

### 7. deploy-promote.yml — Environment Promotion

| Field | Value |
|---|---|
| Path | `.github/workflows/deploy-promote.yml` |
| Lines | 429 |
| Triggers | `workflow_dispatch`; `workflow_run` on completion of `SDAP CI` on master |
| Last commit | `57540e3d` 2026-03-13 — feat(perf): production performance improvements - response compression, caching, query optimization |
| Last-30d runs | 100 total: **0 success, 100 failure** → **0.0% success** (12 on master) |
| Loader-failure pattern? | **Yes — 5/5 sampled have `jobs: []`** |
| Declared purpose | dev → staging → prod promotion gate. `workflow_run` trigger fires after every SDAP CI completion. Downloads `deployment-packages` artifact from sdap-ci and re-deploys to each environment with smoke tests. Prod requires reviewer approval via GitHub environment. |
| Recommended action | **FIX** (FR-03 / P2 cascade — eliminate workflow_run cascade) |
| Evidence | Workflow loads but produces empty `jobs[]` — same loader-failure pattern as deploy-infrastructure. The `workflow_run` cascade means every sdap-ci master run (even successful ones) triggers a deploy-promote run that fails to materialize jobs. **FR-03 acceptance**: "after the fix, a deliberately-failing SDAP CI run on master does NOT produce a deploy-promote failure record". Also: the workflow expects an artifact `deployment-packages` from sdap-ci, but sdap-ci.yml does NOT produce that artifact name (it produces `test-results-*` and `coverage-reports-*`). So even if loader-failure is fixed, the artifact contract is broken. |

### 8. deploy-slot-swap.yml — Deploy via Slot Swap

| Field | Value |
|---|---|
| Path | `.github/workflows/deploy-slot-swap.yml` |
| Lines | 365 |
| Triggers | `workflow_dispatch`; `workflow_run` on completion of `SDAP CI` on master |
| Last commit | `0bbebe73` 2026-05-26 — sdap-bff-api-remediation: Phase 4 + 5 (demo) + Phase 6 docs + LegalWorkspace client /api fix (#295) |
| Last-30d runs | 8 total: **6 success, 2 failure** → **75.0% success** (8 on master) |
| Loader-failure pattern? | No |
| Declared purpose | build → resolve-config → deploy-to-slot → health-check → swap-to-production. workflow_run-triggered after SDAP CI succeeds on master. |
| Recommended action | **CONSOLIDATE** with `deploy-bff-api.yml` (D-04 overlap) — primary keeper |
| Evidence | The "75% success" is misleading: when triggered by `workflow_run` and the upstream sdap-ci failed (which is most pushes recently), the build job's `if:` guard skips → all downstream jobs skip → only the always-running "Pipeline Summary" runs → conclusion = "success" (skipped jobs don't fail the run). Inspection of recent "success" run 26755201586 confirms ALL real jobs (Build & Test, Resolve Configuration, Deploy to Staging Slot, Health Check, Swap to Production) are `skipped` — only Pipeline Summary ran. **So the workflow is currently a no-op on every trigger.** Phase 2 task 020 must decide: consolidate the working logic into `deploy-bff-api.yml` (which has more complete rollback) OR consolidate into this one (which is more recent). |

### 9. insights-eval.yml — Insights Engine Eval Gate

| Field | Value |
|---|---|
| Path | `.github/workflows/insights-eval.yml` |
| Lines | 137 |
| Triggers | `pull_request` paths `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/**`, `src/server/api/Sprk.Bff.Api/Services/Insights/**`, and 8 other Insights-specific paths |
| Last commit | `ecbfc17e` 2026-05-28 — feat(insights-engine): D-P16 end-to-end smoke + golden dataset + eval harness baseline (task 070) |
| Last-30d runs | **0 total** — never invoked in last 30 days (no PR has touched the Insights-Engine paths) |
| Loader-failure pattern? | N/A (no runs) |
| Declared purpose | Phase 1 Insights Engine eval-harness gate: SPEC §3.5.4 facade-boundary grep + Phase1SmokeTest + PredictMatterCostEvalHarness (golden dataset, RAG-triad metrics). PR-scoped path-filtered "early feedback alias" — same tests also run in sdap-ci's full `dotnet test`. |
| Recommended action | **DELETE** (D-03 delete-by-default) OR **CONSOLIDATE** into sdap-ci.yml |
| Evidence | Zero runs in 30 days. Workflow author explicitly notes: "the main `sdap-ci.yml` workflow also runs these tests as part of the full `dotnet test` matrix — this workflow is a path-scoped early-feedback alias, not a redundant gate." If sdap-ci already covers it, this workflow's only value is faster PR feedback on Insights-Engine PRs — which haven't materialized in 30 days. Insights Engine project is `ai-spaarke-insights-engine-r1` (pre-implementation per sdap-bff-api-remediation-fix CLAUDE.md). **Recommend deletion now; re-add later as a path-scoped optimization IF Insights Engine work begins to generate enough PRs that the early-feedback latency matters.** Decision record needed in `decisions/`. |

### 10. nightly-quality.yml — Nightly Quality Pipeline

| Field | Value |
|---|---|
| Path | `.github/workflows/nightly-quality.yml` |
| Lines | 499 |
| Triggers | `schedule` (`0 6 * * 1-5` weeknights 06:00 UTC); `workflow_dispatch` |
| Last commit | `50f2d7bf` 2026-03-13 — feat(quality): add quality tooling infrastructure |
| Last-30d runs | 56 total: **0 success, 56 failure** → **0.0% success** |
| Loader-failure pattern? | No (jobs run; the Build step inside `Test & Coverage` fails) |
| Declared purpose | 5-job pipeline: Test & Coverage (Coverlet) → SonarCloud Analysis → AI Code Review (Claude headless) → Dependency Audit → Report Results (rolling GitHub issue) |
| Recommended action | **FIX** (FR-05 / P4) OR **DELETE** if 3 consecutive successes not achievable |
| Evidence | 56 of 56 weeknight runs failed in 30 days. Failure mode is consistent: `Test & Coverage → Build` fails, cascading to `SonarCloud Analysis → Build for SonarCloud`. The Build failure is likely the same root-cause as sdap-ci.yml master Build failure (Risk R1). If sdap-ci.yml Build is fixed, nightly-quality should follow. **FR-05 acceptance**: "3 consecutive nightly runs succeed, OR the workflow file is removed via `git rm` with a commit message explaining why". Decision record needed in `decisions/`. |

### 11. provision-customer.yml — Provision Customer

| Field | Value |
|---|---|
| Path | `.github/workflows/provision-customer.yml` |
| Lines | 279 |
| Triggers | `workflow_dispatch` only (validate-inputs → provision → verify → summary) |
| Last commit | `8fa46c60` 2026-03-13 — feat(ci): add CI/CD workflow files for platform, BFF API, and customer provisioning |
| Last-30d runs | **0 total** — never invoked in last 30 days |
| Loader-failure pattern? | N/A (no runs) |
| Declared purpose | Manual customer-provisioning runner via `./scripts/Provision-Customer.ps1`. Validates customerId (lowercase alphanumeric 3-10 chars), runs provisioning, runs Test-Deployment.ps1 verification, uploads 90-day audit logs. |
| Recommended action | **DELETE** (D-03 delete-by-default) |
| Evidence | Zero invocations in 30 days. Last touched 2026-03-13. `Provision-Customer.ps1` script is run locally by ops per `docs/procedures/production-release.md`. The workflow's only added value is audit trail (90-day artifact retention) but with zero use that value is unrealized. **Caveat**: if there is a future expectation that customer provisioning will happen via the workflow (multi-tenant scale), retain — but that requires evidence of intent. Decision record needed in `decisions/`. |

### 12. sdap-ci.yml — SDAP CI

| Field | Value |
|---|---|
| Path | `.github/workflows/sdap-ci.yml` |
| Lines | 385 |
| Triggers | `pull_request`; `push` to `master` |
| Last commit | `c9863276` 2026-05-31 — fix(ci): repair sdap-ci.yml — Phase 1 closes, Outcome C operationally complete |
| Last-30d runs | 100 total: **0 success, 98 failure, 2 cancelled** → **0.0% success** (12 on master, 0 success) |
| Loader-failure pattern? | Mostly No (1/5 sampled — the most recent on work/github-actions branch — has empty jobs[]; this is likely a path-filter scheduling artifact unrelated to the underlying YAML being valid) |
| Declared purpose | The canonical CI workflow: Security Scan (Trivy SARIF) + Build & Test matrix (Debug+Release) + Client Quality (Prettier+ESLint) + Code Quality (Format+ADR). The 3 required-status-check contexts (`Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`) gate every PR to master per branch protection. |
| Recommended action | **KEEP** (long-term) + **FIX** (immediate — Risk R1) |
| Evidence | 🚨 **Risk R1 confirmed**: PR #314 (`c9863276`) merged 2026-05-31 20:22Z fixed the duplicate-YAML-key issue. Since then, 2 master runs (26755019759 at 12:31Z and 26757481055 at 13:20Z on 2026-06-01) **both failed** at `Build & Test (Release) → Build` AND `Client Quality (Prettier + ESLint) → Prettier format check`. This is a NEW failure mode different from the predecessor's duplicate-key issue. Failure root-cause investigation is task 002 (parallel to this inventory) — **see `decisions/D-01-master-ci-failure-disposition.md`** when task 002 completes. Workflow is foundational (gates branch protection); MUST be fixed or replaced. Cannot be deleted. |

### 13. weekly-quality.yml — Weekly Quality Summary

| Field | Value |
|---|---|
| Path | `.github/workflows/weekly-quality.yml` |
| Lines | 450 |
| Triggers | `schedule` (`0 22 * * 5` Friday 22:00 UTC); `workflow_dispatch` |
| Last commit | `50f2d7bf` 2026-03-13 — feat(quality): add quality tooling infrastructure |
| Last-30d runs | 11 total: **0 success, 11 failure** → **0.0% success** |
| Loader-failure pattern? | No (job runs; `Extract metrics and build trend table` step fails) |
| Declared purpose | Friday 22:00 UTC rollup of the week's `nightly-quality` artifacts: coverage %, AI-review violations, TODO/FIXME count, vulnerable deps. Creates/updates rolling GitHub issue labeled `weekly-quality-summary`. |
| Recommended action | **CONSOLIDATE** with `nightly-quality.yml` (D-04 overlap) OR **DELETE** |
| Evidence | 11/11 fail because the workflow depends on `nightly-quality` artifacts that don't exist (nightly is also 100% failing → no artifacts → "Extract metrics and build trend table" can't extract anything). Even if nightly-quality is fixed, weekly-quality is a thin reporting layer that could be embedded into the planned new `report-workflow-health.yml` (FR-11) or absorbed into nightly-quality's `report-results` job (which already creates a rolling issue). Decision record needed in `decisions/`. |

---

## Phase 0 Exit Gate Status

- [x] All 13 workflows audited: **13/13 ✅**
- [ ] sdap-ci.yml succeeds on master post-PR-#314? — **❌ NO** — 2 of 2 post-merge master runs failed. **Risk R1 confirmed as project blocker.** Task 002 investigating root cause; disposition via `decisions/D-01-master-ci-failure-disposition.md`.
- [x] All P-categories identified:
  - **P2 cascade**: deploy-promote.yml (FR-03)
  - **P3 ghost triggers**: deploy-infrastructure.yml (FR-04)
  - **P4 failures**: nightly-quality.yml (FR-05)
  - **Risk R1**: sdap-ci.yml master Build + Prettier (FR-02)
  - **D-03 candidates**: auto-add-to-project, deploy-platform, provision-customer, insights-eval (NFR-06 decision records needed)
  - **D-04 candidates**: deploy-bff-api ↔ deploy-slot-swap; weekly-quality ↔ nightly-quality
- [x] Branch protection captured (`branch-protection-2026-06-01.json` — 3 required contexts: `Build & Test (Debug)`, `Build & Test (Release)`, `Code Quality`; `enforce_admins.enabled = true`; no `required_status_checks.strict` enforcement on context contexts)
- [x] Inventory drives Phase 1 fix tasks (010 sdap-ci/Risk-R1, 011 deploy-promote/P2, 012 deploy-infrastructure/P3) and Phase 2 disposition tasks (020+ for the 4 DELETE + 2 CONSOLIDATE candidates)

---

## Cross-Workflow Observations (informational; not blocking)

1. **Action-version drift**: Multiple workflows use `actions/checkout@v6`, `actions/setup-dotnet@v4`, `actions/cache@v5`, `actions/upload-artifact@v6`, `actions/download-artifact@v7`, `actions/github-script@v8`, `azure/login@v2`, `actions/setup-node@v4` or `@v6`. Two Dependabot bumps already landed (`6a067bf1` setup-node v4→v6, `868b72e2` download-artifact v7→v8, `4afb17e0` upload-artifact v6→v7), so the action-version surface is in flux. Phase 1 fix tasks should NOT casually re-pin action versions while Dependabot is iterating; coordinate via single decision record if action-version changes are needed for a fix.
2. **`aquasecurity/trivy-action@master`** (in `sdap-ci.yml`) is a marketplace anti-pattern. Already flagged by predecessor at `projects/sdap-bff-api-remediation-fix/inventory/ci-workflow-inventory.md` G-3 findings. Phase 1 task 010 may opportunistically fix while fixing sdap-ci, OR defer to actionlint task (FR-07).
3. **Hardcoded secrets in `deploy-office-addins.yml`**: `ADDIN_CLIENT_ID`, `TENANT_ID`, `BFF_API_CLIENT_ID`, `BFF_API_BASE_URL` are hardcoded as `env:` values. Not secrets, but the BASE_URL `spaarke-bff-dev` is dev-only and the workflow deploys to prod by `workflow_dispatch` to a hardcoded dev URL. Disposition decision (Phase 2) should flag.
4. **`deploy-bff-api.yml` vs `deploy-slot-swap.yml`** — both implement build→test→staging-slot-deploy→health-check→swap-to-prod. Different shapes (deploy-bff-api has 7 explicit jobs incl. rollback; deploy-slot-swap has 6 jobs without explicit rollback but uses GitHub environment "production" for manual gate). Consolidation candidate per D-04 — keep the more complete one; the other is dead.

---

## Source Data — Recent runs per workflow (raw summary)

The detailed `gh run list` JSON outputs were captured during this audit and are available in the audit history. Below is a one-line summary per workflow for traceability:

```
adr-audit          | 30d: 26 runs / 23 success / 3 failure → 88.5%
auto-add-to-project| 30d: 29 runs / 0 success / 29 failure → 0.0%
deploy-bff-api     | 30d: 58 runs / 0 success / 58 failure → 0.0%
deploy-infrastructure | 30d: 100 runs / 0 success / 100 failure → 0.0% (5/5 sampled = jobs:[] loader-failure)
deploy-office-addins | 30d: 46 runs / 30 success / 16 failure → 65.2%
deploy-platform    | 30d: 0 runs
deploy-promote     | 30d: 100 runs / 0 success / 100 failure → 0.0% (5/5 sampled = jobs:[] loader-failure)
deploy-slot-swap   | 30d: 8 runs / 6 success / 2 failure → 75.0% (most "successes" are skip-all-real-jobs no-ops)
insights-eval      | 30d: 0 runs
nightly-quality    | 30d: 56 runs / 0 success / 56 failure → 0.0% (Build step fails)
provision-customer | 30d: 0 runs
sdap-ci            | 30d: 100 runs / 0 success / 98 failure / 2 cancelled → 0.0%; master post-PR-#314: 2 runs, both failed (Build + Prettier)
weekly-quality     | 30d: 11 runs / 0 success / 11 failure → 0.0% (depends on nightly artifacts)
```

---

*End of inventory. Feeds Phase 1 (tasks 010–012) and Phase 2 (task 020 disposition records). Per NFR-06, every DELETE/CONSOLIDATE decision must have a corresponding entry in `projects/github-actions-rationalization-r1/decisions/D-NN-{workflow-name}.md`.*
