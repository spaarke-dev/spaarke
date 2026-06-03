# GitHub Actions Workflows — Spaarke Repository

> **Last updated**: 2026-06-01 (github-actions-rationalization-r1)
> **Purpose**: Per-workflow operator reference. Triggers, owners, SLAs, common failures.
> **Companion runbook**: [`docs/procedures/workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md) — "a workflow failed, what now?"
> **Weekly health report**: [CI Health Report issue](https://github.com/spaarke-dev/spaarke/issues?q=is%3Aissue+%22CI+Health+Report%22) (updated weekly by `report-workflow-health.yml` per D-02)

This document is the operator-facing index of every workflow in `.github/workflows/`. It is paired with `docs/procedures/workflow-incident-response.md` (the runbook). Keep them in sync — adding or removing a workflow requires editing both.

## Quick reference

| Workflow | Purpose | Triggers | Owner | SLA |
|---|---|---|---|---|
| `adr-audit.yml` | Weekly ADR-compliance audit; opens/updates tracking issue | `schedule` (Monday 09:00 UTC), `workflow_dispatch` | DevOps | Best-effort weekly |
| `deploy-bff-api.yml` | Build + test + deploy BFF API to Azure App Service via staging slot + swap | `push` on master (`src/server/api/**`), `workflow_dispatch` | Platform | Same-day deploy |
| `deploy-infrastructure.yml` | Validate + what-if + deploy Bicep templates (Model 1 + Model 2) | `pull_request` + `push` on master (`infrastructure/bicep/**`), `workflow_dispatch` | Platform | Same-day deploy |
| `deploy-office-addins.yml` | Build + deploy Office Add-ins to Azure Static Web App | `push` on master (`src/client/office-addins/**`), `workflow_dispatch` | Apps | Same-day deploy |
| `deploy-promote.yml` | Multi-stage promotion dev→staging→prod with smoke tests + manual gate | `workflow_dispatch`, `workflow_run` after SDAP CI success on master | Platform | Manual + ~10 min |
| `sdap-ci.yml` | Primary CI: security scan, build/test matrix (Debug+Release), client quality, ADR checks | `pull_request`, `push` on master | Platform | Per-PR ~15 min |
| `workflows-validate.yml` | `actionlint` pre-merge validation (FR-07) | `pull_request` on `.github/workflows/**` | DevOps | Per-PR <5 sec |
| `report-workflow-health.yml` | Weekly per-workflow success-rate snapshot to "CI Health Report" issue (FR-11) | `schedule` (Monday 09:00 UTC), `workflow_dispatch` | DevOps | Weekly |

## Required-status-checks on master

Per FR-08, branch protection on `master` requires these 4 contexts to pass before merge:

- `Build & Test (Debug)` (from `sdap-ci.yml`)
- `Build & Test (Release)` (from `sdap-ci.yml`)
- `Code Quality` (from `sdap-ci.yml`)
- `actionlint` (from `workflows-validate.yml`)

`enforce_admins: true` — admins cannot bypass. See [`docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md`](../docs/guides/GITHUB-ENVIRONMENT-PROTECTION.md) for the full branch-protection configuration.

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

- **Purpose**: Bicep IaC for Model 1 (shared multi-tenant) and Model 2 (customer-dedicated) stacks. Validates Bicep, runs what-if preview, posts PR comment, deploys on manual approval. Path-filtered to `infrastructure/bicep/**` (fixed in Wave B — FR-04).
- **Triggers**: `pull_request` + `push` on master scoped to `infrastructure/bicep/**`; `workflow_dispatch` (env + stack + deploy-bool).
- **Common failures**: Bicep parameter file missing for the selected environment, OIDC federated credential expired, deployment quota exceeded.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### deploy-office-addins.yml

- **Purpose**: Build + npm install + deploy to Azure Static Web App via `Azure/static-web-apps-deploy@v1`. Hardcoded dev `ADDIN_CLIENT_ID` / `TENANT_ID` / `BFF_API_*` env values (flagged for future review).
- **Triggers**: `push` on master + `work/SDAP-outlook-office-add-in` scoped to `src/client/office-addins/**` and this workflow file; `workflow_dispatch`.
- **Common failures**: npm install dependency resolution, SWA deploy token (`AZURE_STATIC_WEB_APPS_API_TOKEN`) rotation, transient SWA service errors.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### deploy-promote.yml

- **Purpose**: dev → staging → prod promotion gate. Downloads deployment artifact from upstream SDAP CI run, redeploys per env with smoke tests. Prod requires reviewer approval via GitHub environment `prod`. Wave-B artifact-contract verification + Wave-D `url:` removal applied (per D-02).
- **Triggers**: `workflow_dispatch` (env input + artifact run id + skip-smoke-tests bool); `workflow_run` after `SDAP CI` completes on master.
- **Common failures**: upstream SDAP CI artifact missing or mis-named, OIDC federated credential mismatch for the target env (see [D-11](../projects/github-actions-rationalization-r1/decisions/D-11-deploy-promote-oidc-federated-credential-gap.md) — AADSTS700213 on first post-merge run 2026-06-01; owner action pending), missing `PROD_APP_NAME` secret + unverified production App Service (see [D-12](../projects/github-actions-rationalization-r1/decisions/D-12-deploy-promote-prod-app-name-secret-and-app-service-gap.md)), smoke test failure post-deploy.
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### sdap-ci.yml

- **Purpose**: Canonical CI pipeline. Jobs: Security Scan (Trivy SARIF) → Build & Test matrix (Debug + Release) → Client Quality (Prettier + ESLint) → Code Quality (Format + ADR). Provides the 3 build/test/quality required-status contexts. Risk R1 (post-PR-#314 master Build + Prettier failure) DEFERRED per D-01.
- **Triggers**: `pull_request`; `push` on master. Concurrency-cancel-in-progress per ref.
- **Common failures**: real C# compile errors (Build), Prettier format diff (Client Quality), unit/integration test fail, `dotnet format` diff (Code Quality).
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### workflows-validate.yml

- **Purpose**: `actionlint` validation on every PR that touches `.github/workflows/**`. Catches duplicate YAML keys, undefined vars, invalid runner labels, malformed expressions — the P1-class bugs that broke `sdap-ci.yml` before PR #314. shellcheck integration is disabled (`-shellcheck=`) by design (per D-01 / FR-07 — pre-existing SC2086/SC2129 nits exceed NFR-02 50% repair threshold).
- **Triggers**: `pull_request` scoped to `.github/workflows/**`.
- **Common failures**: real actionlint violations (working-as-intended), `download-actionlint.bash` upstream availability (very rare).
- **Escalation**: see [`workflow-incident-response.md`](../docs/procedures/workflow-incident-response.md).

### report-workflow-health.yml

- **Purpose**: Weekly per-workflow success-rate snapshot (last 7 days). Queries the GitHub Actions API, computes per-workflow run counts + success rates + loader-failure rates, creates or updates the rolling "CI Health Report" issue (per D-02).
- **Triggers**: weekly schedule (Monday 09:00 UTC), `workflow_dispatch`.
- **Common failures**: GitHub API rate limit (mitigated by paginated queries), token permission missing `issues: write` or `actions: read`.
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
   gh workflow run sdap-ci.yml --ref work/github-actions-rationalization-r1
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
