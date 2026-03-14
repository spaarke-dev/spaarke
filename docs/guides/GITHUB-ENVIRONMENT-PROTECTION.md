# GitHub Environment Protection & Secrets Configuration

> **Last Updated**: 2026-03-13
> **Repository**: `spaarke-dev/spaarke`
> **Configured By**: Task PRODENV-033

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
| `deploy-platform.yml` | `deploy` | Apply Bicep infrastructure changes |
| `deploy-bff-api.yml` | `swap-production` | Swap staging slot to production |
| `provision-customer.yml` | `provision` | Provision new customer resources |

---

## Required GitHub Actions Secrets

### Currently Configured (Repository-Level)

These secrets are already set at the repository level and available to all workflows:

| Secret | Status | Used By | Purpose |
|--------|--------|---------|---------|
| `AZURE_CLIENT_ID` | Configured | All 3 production workflows | OIDC federated credential — app registration client ID |
| `AZURE_TENANT_ID` | Configured | All 3 production workflows | Azure AD tenant ID (`a221a95e-...`) |
| `AZURE_SUBSCRIPTION_ID` | Configured | All 3 production workflows | Target Azure subscription |

### Required But Not Yet Configured

These secrets are referenced by workflows but need to be created:

| Secret | Required By | Purpose | How to Obtain |
|--------|-------------|---------|---------------|
| `AZURE_CLIENT_SECRET` | `provision-customer.yml` | Service principal secret for Provision-Customer.ps1 (passed to script for Dataverse/Graph API calls) | Create in Entra ID > App registrations > spaarke-bff-api-prod > Certificates & secrets |

> **Note**: The `AZURE_CLIENT_SECRET` is only needed by `provision-customer.yml` because Provision-Customer.ps1 requires a client secret for Dataverse and Graph API operations that cannot use OIDC tokens. The other two workflows use OIDC exclusively.

### OIDC Federation (Preferred — No Secret Rotation)

The production workflows use **OIDC federated credentials** via `azure/login@v2` with `client-id`, `tenant-id`, and `subscription-id`. This eliminates the need for long-lived client secrets for Azure login.

**Federated credential configuration** (already set up in task 021):
- **App Registration**: `spaarke-bff-api-prod` (App ID: `92ecc702-d9ae-492d-957e-563244e93d8c`)
- **Federated credential**: GitHub Actions for `spaarke-dev/spaarke` repo
- **Subject**: `repo:spaarke-dev/spaarke:environment:production` and `repo:spaarke-dev/spaarke:environment:staging`

### Secrets by Workflow

#### deploy-platform.yml

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC login |
| `AZURE_TENANT_ID` | OIDC login |
| `AZURE_SUBSCRIPTION_ID` | OIDC login |

#### deploy-bff-api.yml

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC login (staging deploy, production swap, rollback) |
| `AZURE_TENANT_ID` | OIDC login |
| `AZURE_SUBSCRIPTION_ID` | OIDC login |

#### provision-customer.yml

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | OIDC login + passed to Provision-Customer.ps1 |
| `AZURE_TENANT_ID` | OIDC login + passed to Provision-Customer.ps1 |
| `AZURE_SUBSCRIPTION_ID` | OIDC login |
| `AZURE_CLIENT_SECRET` | Passed to Provision-Customer.ps1 for Dataverse/Graph API |

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

- **FR-09**: All three production workflows reference GitHub environments with protection rules.
- **NFR-05**: All deployment runs are logged in GitHub Actions history. Provisioning workflows upload logs as artifacts (90-day retention).
- **FR-08**: No secrets are stored in code. All sensitive values are in GitHub Actions secrets or Azure Key Vault.
