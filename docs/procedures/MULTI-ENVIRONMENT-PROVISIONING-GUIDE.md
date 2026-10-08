# Multi-Environment Provisioning Guide

> **Status**: Operational draft (2026-06-01)
> **Audience**: Platform / DevOps engineer provisioning a new Spaarke environment beyond the original "demo"
> **Scope**: End-to-end chain — Azure infrastructure → Dataverse environment → `sprk_dataverseenvironment` record → BFF API integration
> **Companion docs**:
> - [`ENVIRONMENT-DEPLOYMENT-GUIDE.md`](../guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md) — first-environment setup (Azure-side)
> - [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — **authoritative** per-customer infrastructure model (supersedes `CUSTOMER-DEPLOYMENT-GUIDE.md`, now a stub)
> - [`SPAARKE-SELF-SERVICE-USER-REGISTRATION.md`](../guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md) — registration / demo expiration architecture
> - [`production-release.md`](production-release.md) — release orchestration, manual script execution path

---

## 1. When to use this guide

Use this guide when you need to provision a **second or subsequent** Spaarke environment beyond the original `demo`. Each new environment requires:

- A Dataverse environment (Power Platform tenant)
- Azure infrastructure (App Service, Key Vault, etc., per `ENVIRONMENT-DEPLOYMENT-GUIDE.md`)
- A `sprk_dataverseenvironment` row in the **admin Dataverse** (the single Dataverse instance that hosts the registration workflow + environment registry)
- Configured BFF API + Entra app registrations
- Optional: federated OIDC credentials for CI/CD deployment

The first environment (`demo`) was set up via the original `ENVIRONMENT-DEPLOYMENT-GUIDE.md` workflow. Subsequent environments reuse most of that chain but layer on multi-tenant registry awareness.

## 2. Conceptual model (read this first)

Spaarke has **two distinct levels** of environment configuration:

### Level 1 — Dataverse-level environment **types** (registry)

In the admin Dataverse, the `sprk_dataverseenvironment` entity holds one row per provisioned environment. Each row has:

- `sprk_name` — display name (e.g., "demo", "acme-customer-prod")
- `sprk_environmenttype` — choice: Development / Demo / Sandbox / Trial / Partner / Training / Production
- `sprk_dataverseurl` — e.g., `https://spaarke-acme.crm.dynamics.com`
- `sprk_mdaappid` — model-driven app ID
- `sprk_teamname`, `sprk_specontainerid`, `sprk_securitygroupid`, etc. — see `DataverseEnvironmentRecord.cs` for full field list
- `sprk_isactive` — boolean
- `sprk_isdefault` — boolean (one row enforced as default; legacy concept being phased out per D-11 follow-on)

These records are what the BFF API queries at runtime via `DataverseEnvironmentService` to resolve which environment a registration request maps to.

### Level 2 — Azure / CI/CD deployment **targets**

When CI/CD workflows refer to `dev`, `staging`, `prod` (as in `deploy-promote.yml`'s job names), they refer to **deployment targets** with their own:

- Azure subscriptions / resource groups
- GitHub Environments (the `environment:` key in workflow jobs)
- Federated identity credentials in Entra (one per GitHub Environment)
- Per-environment secrets (`DEV_APP_NAME`, `STAGING_APP_NAME`, `PROD_APP_NAME`)

**The two are decoupled for Spaarke's own non-customer environments** (`dev`, `staging`), where one deployment target may register more than one Dataverse-level row.

> 🔴 **CORRECTED 2026-09-28 (owner decision [D-12](../../projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md)).**
> This paragraph previously said a single deployment target *"can host multiple Dataverse-level environments
> … running on the same Azure App Service"*. **For customers that is not an option, and it is not even
> possible.** Every customer gets their own Dataverse environment **and their own Azure subscription and
> resource group**, so a customer's BFF App Service cannot be shared with another customer's — an App
> Service app cannot even use an App Service Plan in a different subscription.
>
> The reason the shared-app shape was rejected rather than merely disliked: under **Model 1** all customers
> live in **Spaarke's** Azure tenant, so `tenantId` is the **same GUID for every Model 1 customer**. A shared
> app would have to separate customers by a `tenantId`-keyed control — an AI Search filter, a Cosmos
> partition, a Redis key prefix, an SPE container resolver — and **none of those can distinguish customers,
> while all of their tests pass.**

**For a customer environment: one Dataverse environment ↔ one Azure subscription ↔ one full dedicated
Azure stack.** No consolidation. See `SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` for the authoritative
per-customer pattern.

## 3. Prerequisites

Before starting:

| Requirement | How to get it |
|---|---|
| Azure CLI installed + logged in | `az login` (must have Owner or Contributor on the target subscription) |
| PAC CLI installed + authenticated | `pac auth create` against the admin Power Platform tenant (must have Power Platform Admin role) |
| The admin Dataverse URL (where `sprk_dataverseenvironment` lives) | Look up from your owner's records or the original first-environment setup notes |
| Owner / Application Administrator role in the Entra tenant | Needed for adding federated identity credentials (per D-11) |
| The Bicep templates location | Default: `infrastructure/bicep/` (some templates may live in a separate Bicep modules repo per your Phase setup) |
| The latest published solutions | Most recent versions of all Dataverse solutions (managed) — get from CI artifacts or from the `release/` branch |
| A name for the new environment | Short kebab-case (e.g., `trial-a`, `acme-staging`, `customer-z-prod`). Used for resource group names, secret names, and the Dataverse env display name. |

## 4. End-to-end provisioning chain

Throughout this chain, `{name}` is the new environment's short name. For concreteness, examples below use `trial-a`.

### Step 1 — Create the customer's Azure subscription and resource group

> 🔴 **REPLACED 2026-09-28 (owner decision [D-12](../../projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md)).**
> Step 1 used to be *"Provision shared platform infrastructure"* — a shared App Service Plan, Azure OpenAI,
> AI Search, Key Vault, App Insights and Log Analytics that customer stacks were then layered on top of.
> **That layer is retired.** There is no shared-then-overlay shape: **every Azure resource is dedicated per
> customer, in that customer's own Azure subscription and resource group, in both deployment models.**
> `./scripts/Deploy-Platform.ps1` deploys Spaarke's own non-customer-serving platform resources and is
> **not** a prerequisite for onboarding a customer.

Each customer gets **their own Azure subscription and resource group** so that usage is segregable and
billable per customer — and because Azure OpenAI TPM quota is per-subscription-per-region, this is what
gives genuine quota isolation. It is also what forces the App Service Plan to be dedicated: **an App
Service app cannot use a plan in a different subscription.**

1. **Create the subscription** for this customer under the appropriate billing account:
   - **Model 1** — in **Spaarke's** Azure tenant. No admin consent, no Azure Lighthouse delegation needed.
   - **Model 2** — in the **customer's own** Azure tenant. Requires **H0.5 admin consent** *and* **Azure
     Lighthouse delegation** before Spaarke can deploy into it. These two items are the *only* deployment
     differences between the models.
2. **Create the resource group** in that subscription (e.g. `spaarke-trial-a-rg`), in the customer's region.
3. **Register the required resource providers** on the new subscription.

**Idempotency**: yes — creating an existing subscription/resource group is a no-op; re-running is safe.

### Step 2 — Deploy the full per-customer stack

This single chain deploys **everything** for the customer into the resource group from Step 1 — App Service
Plan + BFF App Service, Key Vault, Redis (Standard), Service Bus, Storage, Cosmos, App Insights / Log
Analytics, Azure OpenAI, AI Search, Document Intelligence — plus the Dataverse environment, solutions and
SPE container. There is no per-customer "overlay" on a shared base, because there is no shared base.

```pwsh
./scripts/Provision-Customer.ps1 `
  -CustomerId trial-a `
  -EnvironmentName trial-a `
  -Subscription <the customer's own subscription-id from Step 1> `
  -ResourceGroup spaarke-trial-a-rg `
  -Location eastus
```

This script executes a 13-step idempotent chain:
1. Validate inputs + check for existing state file
2. Create or verify resource group
3. Deploy environment-specific Bicep (`infrastructure/bicep/environment.bicep`)
4. Configure Key Vault access policies
5. Create the Dataverse environment via Power Platform Admin API (returns the new Dataverse URL)
6. Wait for Dataverse environment to reach Ready state
7. Import SpaarkeMaster (managed, the CI-published version) — uses `solution-authoring/Import-SpaarkeMasterPackage.ps1` (T218f; `Deploy-DataverseSolutions.ps1` retired)
8. Set environment-variable values (BFF API base URL, Key Vault URI, etc.)
9. Provision the demo team + business unit
10. Create the SharePoint Embedded container
11. Configure SPE container permissions
12. Run smoke tests against the new environment
13. Register the new environment in the admin Dataverse `sprk_dataverseenvironment` (TBD — currently this step is **manual**, see Step 3 below)

**Resume after failure**: re-run the same command with `-ResumeFromStep N` where N is the step that failed. The state file in `~/.spaarke/provision-state/provision-{customerId}-{envName}.state.json` tracks completed steps.

**Caveat (per github-actions-rationalization-r1 closeout assessment)**: Steps 1–6 + 11 have explicit idempotency checks. Steps 7 (solution import), 8 (env vars), 12 (smoke tests) rely on the underlying tooling's idempotency — most are safe to re-run. Step 7 is: `Import-SpaarkeMasterPackage.ps1` skips an equal version, refuses a type switch or downgrade, and verifies the installed version after the import.

### Step 3 — Register the new environment in `sprk_dataverseenvironment`

This step is **currently manual** (see TODO at end of guide for automation plans).

Connect to the admin Dataverse (the original first-environment Dataverse that hosts the registration workflow):

```pwsh
pac auth select --name <admin-env-name>
```

Create the new record via PAC CLI:

```pwsh
pac data create --table sprk_dataverseenvironment --data @"
{
  "sprk_name": "trial-a",
  "sprk_environmenttype": <choice-value-for-Trial>,
  "sprk_dataverseurl": "https://<output-from-step-2>.crm.dynamics.com",
  "sprk_mdaappid": "<output-from-step-2>",
  "sprk_envaccountdomain": "trial-a.spaarke.com",
  "sprk_businessunitname": "<from-step-9-output>",
  "sprk_teamname": "<from-step-9-output>",
  "sprk_specontainerid": "<from-step-10-output>",
  "sprk_securitygroupid": "<Entra-group-id>",
  "sprk_defaultdurationdays": 14,
  "sprk_licenseconfigjson": "{...}",
  "sprk_adminemails": "dev@spaarke.com",
  "sprk_isactive": true,
  "sprk_isdefault": false
}
"@
```

Choice values for `sprk_environmenttype` are defined in the solution schema. Look them up via `pac data list --table sprk_dataverseenvironment --select sprk_environmenttype`.

**Important**: do NOT set `sprk_isdefault: true` for the new environment unless you're intentionally rotating which environment is the "default". The default flag is currently load-bearing in `DemoExpirationService.cs` per the migration gap documented in `docs/assessments/bff-warning-suppression-analysis-2026-06-01.md` § 3.1. Setting two rows to default may cause unpredictable behavior in the daily expiration job.

### Step 4 — Configure CI/CD federated identity (if deploying via `deploy-promote.yml`)

If you want CI/CD-driven deployments to the new environment, you need a federated identity credential on the Entra app registration that `deploy-promote.yml` uses. Per [D-11](../../projects/github-actions-rationalization-r1/decisions/D-11-deploy-promote-oidc-federated-credential-gap.md):

1. Add a GitHub Environment named after your new env in the repo: **GitHub repo → Settings → Environments → New environment** → name: `trial-a`.
2. Optionally add environment-protection rules (required reviewers, deployment branches).
3. Add a federated credential to the Entra app registration:
   - Entra portal → App registrations → (the deploy-promote client) → Certificates & secrets → Federated credentials → Add
   - Scenario: GitHub Actions deploying Azure resources
   - Organization: `spaarke-dev`
   - Repository: `spaarke`
   - Entity type: Environment
   - GitHub environment name: `trial-a`
   - Name: `spaarke-dev-deploy-promote-trial-a` (or similar)
4. Add the per-env GitHub secret for the App Service name: `TRIAL_A_APP_NAME` (or whatever your naming convention is).
5. Update `deploy-promote.yml` to add the new env to its `plan` job's matrix or conditional branches. Run `actionlint` locally before pushing.

Alternatively, keep deployment to the new environment as a local `Deploy-BffApi.ps1` invocation per `production-release.md`.

### Step 5 — Verify end-to-end

1. Submit a test demo request against the new environment's registration endpoint (or the central admin Dataverse's, if you're not running per-env BFF instances).
2. Approve the request via the admin UI.
3. Verify the user is provisioned: receives welcome email, can log in, has access to the demo team in Dataverse, has read access to the SPE container.
4. Trigger the daily `DemoExpirationService` manually (if you have an admin endpoint) or wait for its midnight UTC run — verify the new environment's expirations are processed correctly. **Note**: per the assessment § 3.1, this currently uses a single global default; multi-env iteration is on the follow-on cleanup project's backlog.

## 5. Cleanup / decommissioning

Reverse the chain when retiring an environment:

1. Set `sprk_isactive: false` on the `sprk_dataverseenvironment` row (do NOT delete the row — historical registration requests link to it via lookup).
2. Disable the GitHub federated credential (Entra → App registration → Federated credentials → Delete the env-specific one).
3. Delete the GitHub Environment (repo Settings → Environments → Delete).
4. Remove per-env GitHub secrets.
5. Delete the Dataverse environment via Power Platform Admin Center (if appropriate; this is destructive).
6. `az group delete --name spaarke-{name}-rg --yes --no-wait` to remove Azure infrastructure (if dedicated to this env).
7. Update any documentation that references the deleted environment.

## 6. Known gaps / TODO

These are documented gaps tracked by `github-actions-rationalization-r1` and its assessment doc:

- **Step 3 is manual**: provisioning a `sprk_dataverseenvironment` row requires hand-running PAC CLI. A future enhancement should automate this as Step 13 of `Provision-Customer.ps1`.
- **Step 5 verification is partially manual**: no end-to-end smoke test for the daily expiration job in a multi-env config.
- **`DemoExpirationService` multi-env migration**: see [`docs/assessments/bff-warning-suppression-analysis-2026-06-01.md`](../assessments/bff-warning-suppression-analysis-2026-06-01.md) § 3.1. Currently the service uses a single global default; full multi-env iteration is on the cleanup follow-on backlog.
- **`Provision-Customer.ps1` idempotency on Steps 7–12**: not 100% verified. Manual re-runs may need cleanup if a step fails partway.
- **OIDC federated credentials**: must be set up per-env per [D-11](../../projects/github-actions-rationalization-r1/decisions/D-11-deploy-promote-oidc-federated-credential-gap.md) before `deploy-promote.yml` can deploy to a new env.

## 7. Operator checklist (printable)

For each new environment `{name}`:

- [ ] Step 1a: the customer's **own Azure subscription** created (Model 1: in Spaarke's tenant · Model 2: in the customer's tenant) and resource providers registered
- [ ] Step 1b (**Model 2 only**): H0.5 admin consent granted **and** Azure Lighthouse delegation established
- [ ] Step 2: `Provision-Customer.ps1 -CustomerId {name} -EnvironmentName {name}` ran to completion against **that** subscription, deploying the full dedicated per-customer stack
- [ ] Step 3: `sprk_dataverseenvironment` row created via PAC CLI with all required fields populated
- [ ] Step 4a: GitHub Environment `{name}` created in repo settings (if CI/CD deploy desired)
- [ ] Step 4b: Entra federated credential added for `repo:spaarke-dev/spaarke:environment:{name}`
- [ ] Step 4c: Per-env GitHub secrets added (`{NAME}_APP_NAME` etc.)
- [ ] Step 4d: `deploy-promote.yml` updated to include the new env (if applicable)
- [ ] Step 5: end-to-end demo registration verified against the new env
- [ ] Documentation: any env-specific notes added to a project-internal runbook
- [ ] Ledger: the new env added to the team's environment registry (separate from `sprk_dataverseenvironment`, this is operational tracking)

---

## 8. References

- [`scripts/Provision-Customer.ps1`](../../scripts/Provision-Customer.ps1) — Step 2 driver (the full per-customer stack)
- [`scripts/Deploy-Platform.ps1`](../../scripts/Deploy-Platform.ps1) — Spaarke's own non-customer-serving platform resources. ⚠️ **No longer the Step 1 driver** (D-12, 2026-09-28)
- [`projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md`](../../projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md) — the deployment-model decision this guide now follows
- [`src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentService.cs`](../../src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentService.cs) — runtime lookup
- [`src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentRecord.cs`](../../src/server/api/Sprk.Bff.Api/Services/Registration/DataverseEnvironmentRecord.cs) — entity field reference
- [`docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md`](../guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md) — original Azure setup
- [`docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md`](../guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md) — authoritative per-customer deployment model
- [`docs/guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md`](../guides/SPAARKE-SELF-SERVICE-USER-REGISTRATION.md) — registration workflow architecture
- [`docs/procedures/production-release.md`](production-release.md) — manual-script release path
- [`docs/assessments/bff-warning-suppression-analysis-2026-06-01.md`](../assessments/bff-warning-suppression-analysis-2026-06-01.md) — multi-env migration gap
- [`projects/github-actions-rationalization-r1/decisions/D-11-deploy-promote-oidc-federated-credential-gap.md`](../../projects/github-actions-rationalization-r1/decisions/D-11-deploy-promote-oidc-federated-credential-gap.md) — federated-credential gap

---

*Authored 2026-06-01 during github-actions-rationalization-r1 closeout (Item 3 of 3). Update as the multi-env provisioning chain matures — especially as Step 3 automation lands and the `DemoExpirationService` multi-env migration is completed.*
