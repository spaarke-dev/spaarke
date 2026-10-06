# GitHub Environment Protection & Secrets Configuration

> **Last Updated**: 2026-09-30
> **Repository**: `spaarke-dev/spaarke`
> **Configured By**: Task PRODENV-033 (environments/secrets setup); workflow inventory reconciled 2026-09-30 by `customer-provisioning-orchestration-r1` after the 2026-06-01 `deploy-platform.yml` / `provision-customer.yml` deletions

---

## Environments Overview

| Environment | Purpose | Approval Required | Wait Timer | Branch Restriction |
|-------------|---------|-------------------|------------|--------------------|
| **staging** | Pre-production validation (slot deploys, smoke tests) | No | None | `master` only |
| **production** | Live production deployments | Yes (1 reviewer) | 5 minutes | `master` only |

---

## Staging Environment

**Purpose**: Used by `deploy-bff-api.yml` for staging slot deployments. No manual approval required — the staging slot is a safe pre-production validation step before the production swap.

### Protection Rules

| Rule | Value |
|------|-------|
| Required reviewers | None (auto-approve) |
| Wait timer | 0 minutes |
| Deployment branches | `master` only |
| Admin bypass | Yes |

### Used By

| Workflow | Job | Purpose |
|----------|-----|---------|
| `deploy-bff-api.yml` | `deploy-staging` | Deploy API build artifact to staging slot |
| `deploy-promote.yml` | `deploy-staging` | Direct-target deploy to the Staging App Service (no dev/prod chaining — see D-12) |

> `deploy-infrastructure.yml` no longer uses any GitHub Environment *(retired by task 249, 2026-10-02)*: it is
> now "Validate Bicep Infrastructure" — lint + compile only, no Azure login, no deploy. Customer stamps are
> deployed only by the L2 control plane's handler H2a.

---

## Production Environment

**Purpose**: Gates all production-impacting deployments. Requires manual approval from a designated reviewer before proceeding.

### Protection Rules

| Rule | Value |
|------|-------|
| Required reviewers | `heliosip` (Ralph Schroeder) |
| Wait timer | 5 minutes |
| Deployment branches | `master` only |
| Admin bypass | Yes |
| Prevent self-review | No |

### Used By

| Workflow | Job | Purpose |
|----------|-----|---------|
| `deploy-bff-api.yml` | `swap-production` | Swap staging slot to production |
| `deploy-promote.yml` | `deploy-prod` | Direct-target deploy to the Production App Service |
| `deploy-spaarke-ai.yml` | `deploy-production` | Deploy the `sprk_spaarkeai` Dataverse web resource to production |

> `deploy-infrastructure.yml`'s `deploy` job (dispatched with `environment=production`, `deploy=true`) was
> retired by task 249, 2026-10-02 — the workflow now only validates Bicep.

> **`deploy-platform.yml` and `provision-customer.yml` removed** 2026-06-01 (commit `902bebc49c`, Wave C workflow rationalization, D-05 / D-08). Neither is a GitHub Actions workflow today:
> - Platform infrastructure (Bicep applied by the old `deploy-platform.yml`) is now deployed by running `scripts/Deploy-Platform.ps1` directly from an operator shell (`az`/`pwsh`) — see that script's own header comment.
> - Customer provisioning (the old `provision-customer.yml`) runs through the L1 handler / L2 control-plane REST API / L3 `/provision-environment` Claude Code skill (`.claude/skills/provision-environment/SKILL.md`), invoked under the **operator's own AAD identity** (NFR-11) rather than a GitHub Actions service principal — see [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md).

---

## Required GitHub Actions Secrets

### Currently Configured (Repository-Level)

These secrets are already set at the repository level and available to all workflows:

| Secret | Status | Used By | Purpose |
|--------|--------|---------|---------|
| `AZURE_CLIENT_ID` | Configured | Every workflow deploying to `staging`/`production` (`deploy-bff-api.yml`, `deploy-promote.yml`, `deploy-spaarke-ai.yml`) — `deploy-infrastructure.yml` no longer logs into Azure (task 249) | OIDC federated credential — app registration client ID |
| `AZURE_TENANT_ID` | Configured | Same set | Azure AD tenant ID (`a221a95e-...`) |
| `AZURE_SUBSCRIPTION_ID` | Configured | Same set | Target Azure subscription |

> **`AZURE_CLIENT_SECRET` is not required by any current workflow.** It was previously listed here as needed by `provision-customer.yml` (removed 2026-06-01, commit `902bebc49c`) for Dataverse/Graph API calls that could not use OIDC. Customer provisioning no longer runs through a GitHub Actions workflow at all — the L2 control-plane / `/provision-environment` skill path authenticates as the **operator's own AAD identity** (NFR-11), not a GitHub Actions service principal secret — so this requirement has no replacement here.

### OIDC Federation (Preferred — No Secret Rotation)

The production workflows use **OIDC federated credentials** via `azure/login@v2` with `client-id`, `tenant-id`, and `subscription-id`. This eliminates the need for long-lived client secrets for Azure login.

**Federated credential configuration** (already set up in task 021):
- **App Registration**: `spaarke-bff-api-prod` (App ID: `92ecc702-d9ae-492d-957e-563244e93d8c`)
- **Federated credential**: GitHub Actions for `spaarke-dev/spaarke` repo
- **Subject**: `repo:spaarke-dev/spaarke:environment:production` and `repo:spaarke-dev/spaarke:environment:staging`

### Secrets by Workflow

#### deploy-bff-api.yml

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC login (staging deploy, production swap, rollback) |
| `AZURE_TENANT_ID` | OIDC login |
| `AZURE_SUBSCRIPTION_ID` | OIDC login |

#### deploy-promote.yml

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC login |
| `AZURE_TENANT_ID` | OIDC login |
| `AZURE_SUBSCRIPTION_ID` | OIDC login |
| `DEV_APP_NAME` / `STAGING_APP_NAME` / `PROD_APP_NAME` | App Service name for the dispatched `target_environment`'s smoke tests |

#### deploy-spaarke-ai.yml

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC login |
| `AZURE_TENANT_ID` | OIDC login |
| `AZURE_SUBSCRIPTION_ID` | OIDC login |
| `SPAARKE_DATAVERSE_PROD_URL` | Production Dataverse URL (production deploy only) |

> `deploy-platform.yml` and `provision-customer.yml` are deleted — see the note under [Production Environment](#production-environment) above; neither has a GitHub Actions secrets footprint today.

---

## Adding a New Reviewer

To add additional reviewers to the production environment:

```bash
# Get the user's GitHub ID
gh api users/{username} --jq '.id'

# Update the environment (include ALL existing reviewers + new ones)
gh api repos/spaarke-dev/spaarke/environments/production -X PUT --input - <<EOF
{
  "wait_timer": 5,
  "reviewers": [
    { "type": "User", "id": 55122302 },
    { "type": "User", "id": NEW_USER_ID }
  ],
  "deployment_branch_policy": {
    "protected_branches": false,
    "custom_branch_policies": true
  }
}
EOF
```

---

## Verification Commands

```bash
# List all environments
gh api repos/spaarke-dev/spaarke/environments --jq '.environments[] | {name, protection_rules: [.protection_rules[].type]}'

# Check staging details
gh api repos/spaarke-dev/spaarke/environments/staging

# Check production details (shows reviewers, wait timer, branch policy)
gh api repos/spaarke-dev/spaarke/environments/production

# List deployment branch policies
gh api repos/spaarke-dev/spaarke/environments/production/deployment-branch-policies

# List configured secrets (names only, values are hidden)
gh api repos/spaarke-dev/spaarke/actions/secrets --jq '.secrets[].name'
```

---

## Compliance Notes

- **FR-09**: Every workflow deploying to production (`deploy-bff-api.yml`, `deploy-promote.yml`, `deploy-spaarke-ai.yml`) references the `production` GitHub Environment and its protection rules. (`deploy-infrastructure.yml` deploys nothing since task 249, 2026-10-02.)
- **NFR-05**: All deployment runs are logged in GitHub Actions history. Provisioning workflows upload logs as artifacts (90-day retention).
- **FR-08**: No secrets are stored in code. All sensitive values are in GitHub Actions secrets or Azure Key Vault.
