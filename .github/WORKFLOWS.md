# GitHub Actions Workflows — Spaarke Repository

> **Last updated**: 2026-10-14 — retired `sdap-ci.yml` / `sdap-ci-docs-only.yml` (task CICD-077; archived at `docs/archive/workflows/`), added `ci-standalone-checks.yml` and `master-ci-alert.yml`. Earlier: 2026-09-10 — corrected "Required-status-checks on master" (live ruleset 21824191 requires exactly ONE check, `Router`; classic branch protection listing 4 contexts was stale); added `office-addins-tests.yml`, `client-tests.yml`, `css-reset-gate.yml` entries. Originally 2026-06-01 (github-actions-rationalization-r1)
> **Purpose**: Per-workflow operator reference. Triggers, owners, SLAs, common failures.
> **Companion runbook**: [`docs/procedures/workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md) — "a workflow failed, what now?"
> **Weekly health report**: [CI Health Report issue](https://github.com/spaarke-dev/spaarke/issues?q=is%3Aissue+%22CI+Health+Report%22) (updated weekly by `report-workflow-health.yml` per D-02)

This document is the operator-facing index of every workflow in `.github/workflows/`. It is paired with `docs/procedures/workflow-incident-response.md` (the runbook). Keep them in sync — adding or removing a workflow requires editing both.

## Quick reference

| Workflow | Purpose | Triggers | Owner | SLA |
|---|---|---|---|---|
| `adr-audit.yml` | Weekly ADR-compliance audit; opens/updates tracking issue | `schedule` (Monday 09:00 UTC), `workflow_dispatch` | DevOps | Best-effort weekly |
| `deploy-bff-api.yml` | Build + test + deploy BFF API to Azure App Service via staging slot + swap | `push` on master (`src/server/api/**`), `workflow_dispatch` | Platform | Same-day deploy |
| `deploy-infrastructure.yml` ("Validate Bicep Infrastructure") | Lint every Bicep file + compile `customer.bicep` and the remaining `stacks/*.bicep`. **Deploys nothing** — customer stamps are deployed only by the L2 control plane (H2a) | `pull_request` + `push` on master (`infrastructure/bicep/**`), `workflow_dispatch` (no inputs) | Platform | Per-PR (validation only) |
| `deploy-office-addins.yml` | Build + deploy Office Add-ins to Azure Static Web App | `push` on master (`src/client/office-addins/**`), `workflow_dispatch` | Apps | Same-day deploy |
| `deploy-promote.yml` | Multi-stage promotion dev→staging→prod with smoke tests + manual gate | `workflow_dispatch` only (downloads `deployment-packages` from a `ci-standalone-checks.yml` run) | Platform | Manual + ~10 min |
| `ci-standalone-checks.yml` ("CI Standalone Checks", added 2026-10-14; NOT a required check) | Checks the legacy `sdap-ci.yml` ran that have no Router equivalent: dependency audit, Release build + BFF publish (`deployment-packages` artifact, master pushes only), Compose client gate, shared-lib/AI.Widgets `tsc`, PCF ESLint, unit tests the Router `bff` filter misses (ControlPlane/Core/Scheduling/IntegrationTests), changed seam/contract test classes, the master-verdict Pester tests, per-change Trivy SARIF | `pull_request` (docs-only paths ignored), `push` on master, `workflow_dispatch` | Platform | Per-PR; failures on master raise quality alerts |
| `master-ci-alert.yml` (added 2026-10-14) | Raises/clears the `router-master` and `tier2-master/full-unit-tests` quality alerts when the `CI` workflow (Router) finishes on master | `workflow_run` on `CI` (master) | Platform | Per master push |
| ~~`sdap-ci.yml`~~ / ~~`sdap-ci-docs-only.yml`~~ | **Retired 2026-10-14** (task CICD-077). Archived at `docs/archive/workflows/`; they no longer run. Jobs moved to `ci-tier1-blocking.yml`, `ci-tier2-advisory.yml`, `ci-standalone-checks.yml`, `nightly-health.yml` (map: `projects/ci-cd-unit-test-remediation-r1/notes/sdap-ci-retirement.md`) | none | Platform | n/a |
| `workflows-validate.yml` | `actionlint` pre-merge validation (FR-07) | `pull_request` on `.github/workflows/**` | DevOps | Per-PR <5 sec |
| `report-workflow-health.yml` | Weekly per-workflow success-rate snapshot to "CI Health Report" issue (FR-11) | `schedule` (Monday 09:00 UTC), `workflow_dispatch` | DevOps | Weekly |
| `office-addins-tests.yml` (added 2026-09-10) | Runs the `src/client/office-addins` jest suite on every PR touching that package; **reports** pass/fail but does NOT block merge (not in the required-check list — see "Required-status-checks on master" below). Suite allow-list: `src/client/office-addins/ci-gated-suites.txt` | `pull_request` on `src/client/office-addins/**` | Apps | Per-PR (report-only) |
| `client-tests.yml` | Phase-1 visibility-only jest runner across 40 client packages (issue #851) — per-package pass/fail table; does not gate PRs (deliberately `schedule` + `workflow_dispatch`, not `pull_request`, pending Phase-2 promotion) | `schedule`, `workflow_dispatch` | Platform | Nightly (report-only) |
| `css-reset-gate.yml` | Verifies the universal box-sizing CSS reset is present in every Code Page host `index.html` | `pull_request` + `push` on master, scoped to `**/index.html` under Code Page/solution roots + the workflow file | DevOps | Per-PR <5 sec |

## Required-status-checks on master

**Corrected 2026-09-10 — this section was stale.** Classic branch protection is disabled on this repo; the live enforcement mechanism is a GitHub **ruleset**. Verified via:

```bash
gh api repos/spaarke-dev/spaarke/rules/branches/master
gh api repos/spaarke-dev/spaarke/rulesets/21824191
```

Ruleset `21824191` ("master: require CI Router") requires **two** status checks to pass before merge (re-verified 2026-10-14):

- `Router` (from `ci-router.yml`)
- `Client lint (type-aware ratchet)` (from `client-lint.yml`)

It also enforces `non_fast_forward`, `deletion` (no force-push/delete), and a `pull_request` rule (0 required approving reviews, all merge methods allowed). `current_user_can_bypass: "never"` — no bypass actors configured. The Tier 1/Tier 2 jobs, `ci-standalone-checks.yml`'s jobs and `workflows-validate.yml`'s `actionlint` job are NOT themselves required contexts (the retired `sdap-ci.yml` jobs `Build & Test (Debug)`, `Build & Test (Release)` and `Code Quality` were not either). `Router` is the CI gate: it fans out to the tiered workflows and reports the aggregate result as one check; `Client lint` is the only other required check. See [`docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`](../docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md) for background, noting that document may also predate the ruleset migration.

## Per-workflow detail

### adr-audit.yml

- **Purpose**: Runs ADR-compliance tests (`tests/Spaarke.ArchTests/`); creates or updates a single rolling GitHub issue (labels: `architecture,adr-audit`) summarizing violations.
- **Triggers**: weekly schedule (Monday 09:00 UTC), `workflow_dispatch`.
- **Common failures**: real ADR violations (working-as-intended), missing `GH_TOKEN_PROJECT` secret, GitHub API rate-limit (rare).
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### deploy-bff-api.yml

- **Purpose**: Build → test → deploy to Azure App Service staging slot → `/healthz` verify → swap to production → re-verify → rollback (swap-back) on failure. Concurrency-locked per environment. Preserves ADR-029 hash-verify + slot-swap rollback (per D-04 ledger).
- **Triggers**: `push` on master with paths under `src/server/api/**`; `workflow_dispatch` (env input: `dev` or `production`).
- **Common failures**: `dotnet test` step (unit test breakage), Azure OIDC login (expired federated credential), `/healthz` returning non-200 at staging slot.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### deploy-infrastructure.yml

- **Name**: "Validate Bicep Infrastructure" (file name kept).
- **Purpose**: Validates the Bicep IaC; **deploys nothing**. One `validate` job: `az bicep lint` on every `.bicep` file under `infrastructure/bicep/`, then `az bicep build --outfile` of `customer.bicep` and the remaining standalone `stacks/*.bicep`. `permissions: contents: read`; no Azure login, no GitHub Environment. `customer.bicep` is the only customer-stamp template and is deployed only by the L2 control plane's handler H2a (owner decision D19). Path-filtered to `infrastructure/bicep/**` (fixed in Wave B — FR-04). *(The what-if + deploy stages, the env/stack/deploy dispatch inputs and their `stacks/model2-full.bicep` target were retired by task 249, 2026-10-02.)*
- **Triggers**: `pull_request` + `push` on master scoped to `infrastructure/bicep/**`; `workflow_dispatch` (no inputs).
- **Common failures**: a Bicep lint error (warnings do not fail the job — there is no `bicepconfig.json` raising them), a template that no longer compiles (e.g. a module param rename not propagated), or a `parameters/*.bicepparam` that still sets a parameter its template no longer declares.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### deploy-office-addins.yml

- **Purpose**: Build + npm install + deploy to Azure Static Web App via `Azure/static-web-apps-deploy@v1`. Hardcoded dev `ADDIN_CLIENT_ID` / `TENANT_ID` / `BFF_API_*` env values (flagged for future review).
- **Triggers**: `push` on master + `work/SDAP-outlook-office-add-in` scoped to `src/client/office-addins/**` and this workflow file; `workflow_dispatch`.
- **Common failures**: npm install dependency resolution, SWA deploy token (`AZURE_STATIC_WEB_APPS_API_TOKEN`) rotation, transient SWA service errors.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### deploy-promote.yml

- **Purpose**: dev → staging → prod promotion gate. Downloads the `deployment-packages` artifact from a `ci-standalone-checks.yml` run (`release-build` job; uploaded on master pushes only), redeploys per env with smoke tests. Prod requires reviewer approval via GitHub environment `prod`. Wave-B artifact-contract verification + Wave-D `url:` removal applied (per D-02).
- **Triggers**: `workflow_dispatch` only (env input + artifact run id + skip-smoke-tests bool). The earlier `workflow_run` trigger was removed 2026-08-11.
- **Common failures**: `deployment-packages` artifact missing or mis-named (it comes from `ci-standalone-checks.yml` master runs), OIDC federated credential mismatch for the target env (see [D-11](../projects/github-actions-rationalization-r1/decisions/D-11-deploy-promote-oidc-federated-credential-gap.md) — AADSTS700213 on first post-merge run 2026-06-01; owner action pending), missing `PROD_APP_NAME` secret + unverified production App Service (see [D-12](../projects/github-actions-rationalization-r1/decisions/D-12-deploy-promote-prod-app-name-secret-and-app-service-gap.md)), smoke test failure post-deploy.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### sdap-ci.yml (retired 2026-10-14)

- **Status**: Retired by task CICD-077 together with `sdap-ci-docs-only.yml`. Both are archived at `docs/archive/workflows/` (GitHub does not execute them there). The required checks are `Router` (`ci-router.yml`) and `Client lint (type-aware ratchet)` (`client-lint.yml`). Job-by-job mapping: `projects/ci-cd-unit-test-remediation-r1/notes/sdap-ci-retirement.md`.

### ci-standalone-checks.yml

- **Purpose**: Checks the legacy `sdap-ci.yml` ran that have no Router/nightly equivalent: `dependency-audit` (per-push CVE gate), `release-build` (Release build of the solution + BFF publish; uploads `deployment-packages` on master pushes, consumed by `deploy-promote.yml`), `compose-client-gate`, `client-typecheck` (shared-lib `tsc` + `Spaarke.AI.Widgets` `tsc --noEmit`), `pcf-eslint` (PCF ESLint, which Tier 2 runs only on SpaarkeAi/BFF changes), `unit-tests-outside-router` (ControlPlane, Core, Scheduling, Bff.Api.IntegrationTests and Spe.Integration tests, which no Router-filtered Tier 2 run reaches), `bff-seam-contract-tests` (test classes in changed `tests/integration/{seam,contract,tenant,Shared}` / `tests/unit/domain` files), `verdict-script-tests` (Pester must-fire controls for `master-ci-verdict.ps1`), `ontology-script-tests` (Pester controls for `Apply-DoLanePolicies.ps1` and `Invoke-OntologyStampBackfill.ps1`), `trivy-fs` (per-change repo scan, SARIF to the Security tab; reports only, alerts only if the scan errors).
- **Triggers**: `pull_request` (docs-only paths ignored), `push` on master, `workflow_dispatch`. NOT a required check; no job uses `continue-on-error`.
- **Common failures**: a new HIGH CVE outside the accepted-risk list (`dependency-audit`), compile error under Release (`release-build`), TypeScript error in a shared library (`client-typecheck`). On master each failing job raises a `ci-standalone/<job>` quality alert and clears it on the next green run.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### master-ci-alert.yml

- **Purpose**: When the `CI` workflow (Router) completes on a master push, raises or clears the `router-master` alert (Router or a Tier 1 job red/cancelled) and the `tier2-master/full-unit-tests` alert (a Tier 2 shard did not pass), each judged from its most recent execution in the newest 100 master runs (a job the open alert lists as failing that is absent from the window stays failing, not recovered; a newer queued/running master run holds `ok` back); raises `master-ci-alert/evaluation` if the evaluation itself fails. Logic: `scripts/ci/master-ci-verdict.ps1`.
- **Triggers**: `workflow_run` on `CI` (master); `workflow_dispatch` (dry-run unless on the default branch).
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### workflows-validate.yml

- **Purpose**: `actionlint` validation on every PR that touches `.github/workflows/**`. Catches duplicate YAML keys, undefined vars, invalid runner labels, malformed expressions — the P1-class bugs that broke the (now retired) `sdap-ci.yml` before PR #314. shellcheck integration is disabled (`-shellcheck=`) by design (per D-01 / FR-07 — pre-existing SC2086/SC2129 nits exceed NFR-02 50% repair threshold).
- **Triggers**: `pull_request` scoped to `.github/workflows/**`.
- **Common failures**: real actionlint violations (working-as-intended), `download-actionlint.bash` upstream availability (very rare).
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### report-workflow-health.yml

- **Purpose**: Weekly per-workflow success-rate snapshot (last 7 days). Queries the GitHub Actions API, computes per-workflow run counts + success rates + loader-failure rates, creates or updates the rolling "CI Health Report" issue (per D-02).
- **Triggers**: weekly schedule (Monday 09:00 UTC), `workflow_dispatch`.
- **Common failures**: GitHub API rate limit (mitigated by paginated queries), token permission missing `issues: write` or `actions: read`.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### office-addins-tests.yml (added 2026-09-10)

- **Purpose**: Runs the `src/client/office-addins` jest suite on every PR touching that package. It closes the "vacuous-green" gap where the suite previously only ran nightly (via `client-tests.yml`, with `continue-on-error: true`) and could never fail a PR. It **reports** a real pass/fail; it is not a required status check (`Router` is the only one, per ruleset 21824191), so a red run does not block a merge.
- **Triggers**: `pull_request` scoped to `src/client/office-addins/**`.
- **Common failures**: real jest regressions in the office-addins suite (working-as-intended); suite additions not on the `ci-gated-suites.txt` allow-list silently not gated (by design — see the workflow's own header comment).
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### client-tests.yml

- **Purpose**: Phase-1 CI visibility for the broader client test surface — makes ~730 tracked jest test files across 40 client packages visible in CI (previously none ran anywhere). Produces a per-package pass/fail table. Deliberately does not fix tests or gate PRs in Phase 1 (see the workflow's own header comment); promotion to a `pull_request`-gated Tier 2 check is a separate, later phase.
- **Triggers**: `schedule`, `workflow_dispatch`.
- **Common failures**: pre-existing per-package test failures (expected during Phase 1; the table is the deliverable, not a green bar).
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### css-reset-gate.yml

- **Purpose**: Repository-wide enforcement of the universal `box-sizing` CSS reset in every Code Page host `index.html` (Vite + Webpack), preventing the class of DataGrid-overflow regression documented in `docs/guides/DATAGRID-CODE-PAGE-HOST-CONTRACT.md` §2.
- **Triggers**: `pull_request` + `push` on master, path-filtered to `src/solutions/**/index.html`, `src/client/code-pages/**/index.html`, `scripts/check-html-css-reset.mjs`, and the workflow file itself.
- **Common failures**: a new or edited Code Page host `index.html` missing the reset.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

## Notification routing (FR-12 / D-05)

### Why this is documented (not automated)

Per D-05 (see [`projects/github-actions-rationalization-r1/design.md`](../projects/github-actions-rationalization-r1/design.md)): workflow-level email sends (via SMTP actions) are fragile and historically have been the path of least signal. GitHub's built-in notification routing is the proper path. The owner applies this manually because it requires GitHub-account-level access and inbox verification — automating it inside a workflow would couple the routing to repo secrets and re-introduce the fragility D-05 explicitly rejects.

### Target destination

- **Email**: `dev@spaarke.com` (per owner clarification 2026-06-01)
- **Scope**: all `spaarke-dev` org-level notifications (per Assumption-5 in [`spec.md`](../projects/github-actions-rationalization-r1/spec.md))

### Steps for the owner to apply

1. **Add `dev@spaarke.com` as a verified email on the `spaarke-dev` GitHub account**:
   - Navigate: GitHub UI → top-right avatar → Settings → Emails
   - Click "Add email address"
   - Enter `dev@spaarke.com`
   - GitHub sends a verification email; click the link
   - Status should show "Verified"

2. **Set `dev@spaarke.com` as the primary notification email**:
   - Settings → Notifications → Default notification email
   - Select `dev@spaarke.com` from the dropdown
   - Save

3. **Configure per-event preferences** (for more granular control, if desired):
   - Settings → Notifications → System → Actions
   - Choose: Web (in-app) + Email (default email set above)
   - Save

4. **(Optional, if org-level routing is required)** Set custom email routing for `spaarke-dev` org:
   - Org settings → Notifications → Custom routing for organizations
   - Add `spaarke-dev` org → route to `dev@spaarke.com`
   - Verify

### Verification

1. Trigger a workflow failure (or wait for one to occur):
   ```bash
   gh workflow run ci-standalone-checks.yml --ref work/github-actions-rationalization-r1
   ```
2. Within 5 minutes, confirm `dev@spaarke.com` receives the GitHub Actions notification.
3. If the notification doesn't arrive:
   - Check the spam folder
   - Verify the email forwarding chain (if `dev@spaarke.com` is an alias)
   - Verify the email isn't filtered by Gmail/Outlook rules
   - Check Settings → Notifications has `dev@spaarke.com` listed as "Verified"

### Troubleshooting

- **Email not arriving**: GitHub may be sending to the verified email but a forwarding rule is silently dropping it. Test by sending a plain test email directly to `dev@spaarke.com` from a personal account to confirm the inbox is reachable.
- **Forwarding alias setup**: If `dev@spaarke.com` is a Google Group / forwarding alias, ensure the verification email can reach a real inbox. The owner may need to temporarily add a personal email as the verification recipient, then switch the primary back to `dev@spaarke.com`.
- **GitHub Actions notifications missing while other notifications work**: Settings → Notifications → System → Actions must be enabled.

### Owner sign-off (update when applied)

- [x] **Owner applied routing on 2026-06-01** — verified `dev@spaarke.com` receives `spaarke-dev` notifications. (Natural test signal: `Environment Promotion` failure at 2026-06-01 20:02:01 UTC; notification expected at dev@spaarke.com if routing took effect before that timestamp.)

Per Assumption-4 in [`spec.md`](../projects/github-actions-rationalization-r1/spec.md): the owner may need a forwarding alias OR a dedicated `spaarke-dev-bot` GitHub user if the `dev@spaarke.com` inbox isn't directly accessible for verification. Per FR-12 acceptance, **documentation existing is sufficient for project close** — the owner's manual application is tracked as a follow-up and does not gate the project graduation.

## Modifying or adding workflows

- **MUST** install actionlint locally: `bash <(curl -sSL https://raw.githubusercontent.com/rhysd/actionlint/main/scripts/download-actionlint.bash)` then `./actionlint .github/workflows/{your-file}.yml`.
- **MUST** pass the `actionlint` required-status-check on the PR before merge.
- **MUST** follow the spec's `repair-not-rewrite` discipline for fixes (NFR-02: <50% line replacement; >50% requires escalation OR explicit delete-and-rewrite decision record).
- **MUST NOT** push directly to master. Use a PR via a feature branch.
- For deletions: use `git rm` (NOT comment-out) + commit message referencing rationale (NFR-04).
- Reference [`docs/procedures/ci-cd-workflow.md`](../docs/procedures/ci-cd-workflow.md) for the broader CI pipeline architecture.

### Adding a workflow

1. Author `.github/workflows/{name}.yml`.
2. Run `actionlint .github/workflows/{name}.yml` locally.
3. Add an entry to this file (`WORKFLOWS.md`) under both the **Quick reference** table and the **Per-workflow detail** section.
4. Author or update the matching entry in [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).
5. Open a PR; ensure the `actionlint` required-status-check passes before merge.

### Removing a workflow

1. `git rm .github/workflows/{name}.yml`.
2. Update this file: remove the entry from **Quick reference** and **Per-workflow detail**.
3. Update [`docs/procedures/ci-cd-workflow.md`](../docs/procedures/ci-cd-workflow.md) if the workflow is referenced there.
4. If the workflow was deleted with rationale, link to the decision record (e.g., `projects/{project}/decisions/D-NN-...`) in the commit message.

## See also

- [`docs/procedures/ci-cd-workflow.md`](../docs/procedures/ci-cd-workflow.md) — broader CI/CD pipeline architecture
- [`docs/procedures/workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md) — runbook for "a workflow failed, what now?"
- [`docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`](../docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md) — branch-protection setup
- [`projects/github-actions-rationalization-r1/`](../projects/github-actions-rationalization-r1/) — origin of the current workflow-set rationalization
