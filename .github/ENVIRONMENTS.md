# GitHub Environment Configuration

> **Purpose**: Documents the three deployment environments, their protection rules, and required secrets/variables for the environment promotion pipeline (`deploy-promote.yml`).

## Environment Overview

| Environment | Protection Rules | OIDC Auth | Auto-Deploy |
|-------------|-----------------|-----------|-------------|
| **dev** | None | Yes | Yes (on CI success) |
| **staging** | None | Yes | Yes (after dev passes) |
| **prod** | Required reviewers | Yes | No (manual approval) |

## Promotion Flow

```
SDAP CI (master) ──success──► dev ──auto──► staging ──approval──► prod
                                │              │                    │
                                ▼              ▼                    ▼
                           smoke tests    smoke tests +       smoke tests
                                         integration
```

- **dev -> staging**: Automatic. Proceeds immediately after dev smoke tests pass.
- **staging -> prod**: Manual. Pauses for reviewer approval via GitHub environment protection rules.

## Setup Instructions

### Step 1: Create Environments

In GitHub: Settings > Environments > New environment

Create three environments: `dev`, `staging`, `prod`.

### Step 2: Configure Protection Rules

#### dev

- No protection rules required
- Deployment branch: `master` only

#### staging

- No required reviewers (auto-promoted from dev)
- Deployment branch: `master` only
- Optional: Add wait timer (e.g., 5 minutes) for observation

#### prod

- **Required reviewers**: Add at least 1 reviewer (e.g., repository admin, tech lead)
- **Prevent self-review**: Enable (the person who triggered the workflow cannot approve)
- Deployment branch: `master` only
- Optional: Add wait timer for additional observation window

### Step 3: Configure OIDC Federated Credentials

Each environment needs an Azure AD app registration with federated credentials for OIDC (no stored secrets).

1. In Azure AD: App registrations > Select the deployment app > Certificates & secrets > Federated credentials
2. Add one credential per environment:

| Environment | Subject Identifier |
|-------------|-------------------|
| dev | `repo:spaarke-dev/spaarke:environment:dev` |
| staging | `repo:spaarke-dev/spaarke:environment:staging` |
| prod | `repo:spaarke-dev/spaarke:environment:prod` |

Issuer: `https://token.actions.githubusercontent.com`
Audience: `api://AzureADTokenExchange`

### Step 4: Configure Environment Secrets

#### All Environments (Repository-Level)

These secrets are shared across all environments and set at the repository level:

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Azure AD app registration client ID (OIDC) |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID |
| `AZURE_TENANT_ID` | Azure AD tenant ID |

#### Per-Environment Secrets

Set these in each environment's settings:

| Secret | dev | staging | prod |
|--------|-----|---------|------|
| `DEV_APP_NAME` | `spe-api-dev-67e2xz` | - | - |
| `STAGING_APP_NAME` | - | `spe-api-staging-{id}` | - |
| `PROD_APP_NAME` | - | - | `spe-api-prod-{id}` |

> Replace `{id}` with the actual App Service random suffix after Bicep deployment.

### Step 5: Verify Configuration

Run the promotion workflow manually:

```bash
# Deploy to dev only
gh workflow run deploy-promote.yml -f target_environment=dev

# Promote through dev + staging
gh workflow run deploy-promote.yml -f target_environment=staging

# Full promotion: dev → staging → prod (will pause at prod for approval)
gh workflow run deploy-promote.yml -f target_environment=prod
```

## Troubleshooting

| Issue | Solution |
|-------|----------|
| OIDC login fails | Verify federated credential subject matches `repo:{owner}/{repo}:environment:{env}` |
| Approval not requested | Ensure `prod` environment has "Required reviewers" configured |
| Artifact download fails | Verify SDAP CI workflow produces `deployment-packages` artifact |
| Smoke tests fail | Check App Service is running and `/ping`, `/healthz` endpoints are accessible |

## Related Workflows

| Workflow | Purpose |
|----------|---------|
| `sdap-ci.yml` | Build, test, produce deployment artifacts |
| `deploy-infrastructure.yml` | Bicep IaC deployment (separate from app deployment) |
| `deploy-promote.yml` | Environment promotion (this pipeline) |
| `deploy-staging.yml` | Legacy staging deployment (superseded by deploy-promote.yml) |
