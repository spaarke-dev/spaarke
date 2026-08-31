# Research: Programmatically Provisioning a Dataverse Environment (End-to-End, 2026-08-22)

> **Author**: researcher subagent, on request of owner (Ralph Schroeder) as follow-on to the Model 1 Prod stand-up session that landed the Azure stamp but stalled at the "install SpaarkeMaster into the fresh Dataverse env" step (F12 in [`lessons-learned-model1-prod-standup-2026-08-22.md`](./lessons-learned-model1-prod-standup-2026-08-22.md)).
> **Scope**: Foundational knowledge sweep to close the gap between what Spaarke's r1 project currently automates and what Microsoft actually offers for programmatic Dataverse env provisioning in 2026. Covers env creation, environment groups, managed environments, database/capacity, PAYG, solution import, application users, customer users, ISV multi-tenant patterns, and 2026 delta.
> **Audience**: r1 owner + implementers writing the L2 handler code (H5, H6, H10, H11 + a possible new H5b/H5c).

---

## Executive Summary

"Programmatically provisioning a Dataverse env end-to-end" in 2026 is **eight distinct API surfaces** that must be composed in a specific order — not one API. The surfaces are: (1) Dataverse env creation via **pac admin create** or **BAP REST** (`api.bap.microsoft.com`), (2) **Managed Environments enable** via `pac admin set-governance-config` or `Set-AdminPowerAppEnvironmentGovernanceConfiguration`, (3) **Environment Group** create + assign via the **Power Platform API** (`api.powerplatform.com/environmentmanagement/environmentGroups`, api-version `2024-10-01`), (4) **PAYG billing-plan** create + link via PPAC UI or the **BAP `billingPolicies`** endpoint, backed by a Power Platform account resource in an Azure sub, (5) **Solution import** via Dataverse Web API `ImportSolution` / `StageAndUpgrade` + `ImportJob` polling — with the dependency chain issue being an ISV-packaging problem, not an import-API problem, (6) **Application-user create** via **BAP `addAppUser`** endpoint (single POST, always System Administrator, works for MI-client-id or SP-client-id), (7) **B2B guest user** flow via Entra + Dataverse `restrictGuestUserAccess` toggle + `systemuser` insert, and (8) **Post-provisioning `pac env update-settings`** for per-env config knobs (audit, restrictGuestUserAccess, etc.). The **Microsoft Terraform Power Platform provider v4.1.0** (Jan 2026, still **public preview**) wraps 5 of the 8 surfaces (environment, managed_environment, environment_group, billing_policy, billing_policy_environment, solution, user, application_package_install) — this is Spaarke's D14 target and is a legitimate one-tool path when it lands, but is not yet GA. Solution-import missing-dependency errors are an **ISV solution-packaging** problem: solutions must ship self-contained or with a documented dependency chain, not "export from dev and hope." Spaarke's F12 (240 missing deps) is the smoking gun of exactly this.

The r1 handler catalog (H5, H6, H10, H11) covers 5 of the 8 surfaces. **Three surfaces are currently absent or under-specified** and are what today's live stand-up hit: (a) **Managed Environments enable** is not a distinct handler and is not mentioned in design.md; (b) **Environment Groups** are entirely absent from r1 scope; (c) **PAYG billing-plan link** is absent. All three are required-if-tenant-wanted governance/billing features — not required-for-basic-functionality — but as SaaS platform operators, Spaarke needs to make a conscious yes/no choice per feature per tenancy model, not silently omit.

---

## 1. Environment Creation (Programmatic)

### 1.1 Env types — 2026 reality

| Type | When to use | Billing / license | Notes |
|---|---|---|---|
| **Production** | Live customer workloads | Consumes 1 GB Dataverse DB capacity from tenant pool (or PAYG) | Default type for Spaarke customer envs. Backup retention 28 days default. |
| **Sandbox** | Dev/test alongside a production | Consumes 1 GB DB capacity | Copy/reset semantics: can `pac admin copy` from prod → sandbox. Backup retention 7 days. |
| **Trial** | 30-day evaluation | Zero minimum capacity | Not for r1. Auto-deletes after trial period. |
| **Developer** | Personal dev environments (per-maker) | Free with Power Apps Developer Plan | **SPs cannot create Developer envs** — must be user-context (per pac docs + TF provider limitation). Includes a Dataverse DB. |
| **Teams** | Dataverse-for-Teams (deprecated for new adoption) | Zero cost, tied to a Team | Requires `--security-group-id` (M365 Group ID). Restricted feature set. Not for r1. |
| **SubscriptionBasedTrial** | Trial tied to a subscription (converts to production) | Free during trial | Enterprise trial path. Not for r1's stamp flow. |
| **Default** | Auto-created per tenant | 1 TB DB pool, shared | Every user in Maker role by default. Spaarke's Model 1 conceptually resembles a per-customer default-like shared env but is a proper Production env. Not a type Spaarke creates. |

**Only Production and Sandbox support PAYG** ([Microsoft Learn: PAYG setup](https://learn.microsoft.com/en-us/power-platform/admin/pay-as-you-go-set-up)).

### 1.2 Programmatic creation interfaces

Four supported paths in 2026:

**(a) `pac admin create` — Power Platform CLI** ([reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/admin)):

```powershell
pac admin create `
  --name "Spaarke Model 1 Prod" `
  --type Production `
  --region unitedstates `
  --currency USD `
  --language English `
  --domain spaarke-model1-prod `
  --async `
  --max-async-wait-time 30 `
  --json
```

All parameters supported: `--type`, `--currency`, `--domain`, `--language`, `--name`, `--region`, `--security-group-id`, `--templates` (D365 first-party apps like `D365_Sales`), `--user`, `--async`, `--max-async-wait-time`, `--input-file` (JSON of same args), `--json` (output as JSON).

**What is NOT supported on `pac admin create`**: managed-environment enable, environment-group assignment, PAYG billing-plan link, "add Dataverse database later" (all envs get DB at create time unless you pick a special no-database canvas-only env — which is not a `pac admin create` option). These are separate follow-up commands.

**(b) BAP REST — `api.bap.microsoft.com`**:

The `Provision-Customer.ps1` step 5 sequence already used by Spaarke. Endpoint pattern:
```
POST https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments?api-version=2020-10-01
```
Auth scope: `api.bap.microsoft.com/.default`. Body includes location, sku (`Production`), currency, language, domain, securityGroupId. Response is a long-running operation with polling URL. **This is what `pac admin create` calls underneath.** For a service-principal driver, direct BAP calls give the same outcome without the CLI shell-out.

**(c) Microsoft Power Platform Terraform provider — `powerplatform_environment`** ([docs](https://microsoft.github.io/terraform-provider-power-platform/resources/environment/)):

```terraform
resource "powerplatform_environment" "customer" {
  display_name     = "Spaarke Model 1 Prod"
  location         = "unitedstates"
  azure_region     = "westus2"           # optional, forces geo-affinity
  environment_type = "Production"
  billing_policy_id = powerplatform_billing_policy.payg.id  # optional; PAYG link at create time
  dataverse = {
    language_code     = "1033"
    currency_code     = "USD"
    security_group_id = "00000000-0000-0000-0000-000000000000"
    domain            = "spaarke-model1-prod"
  }
}
```

Under the hood: same BAP REST. **Advantages**: idempotency, state file, `terraform plan` = dry-run. **Limitations** as of v4.1.0 (Jan 2026 — **still public preview, not GA**): SP cannot create Developer envs; managed_environment is a **separate resource** (`powerplatform_managed_environment`) that references the env; environment-group assignment currently done via `powerplatform_tenant_settings` `environment_routing_target_environment_group_id`, no direct per-env "add to group" resource yet.

**(d) `Microsoft.PowerApps.Administration.PowerShell` module** ([`New-AdminPowerAppEnvironment`](https://learn.microsoft.com/en-us/powershell/module/microsoft.powerapps.administration.powershell/new-adminpowerappenvironment)):

Legacy but still supported. Uses BAP under the hood. Considered less-preferred in 2026 — Microsoft's investment is in `pac admin`, BAP REST, and the TF provider. Only reach for this module if you need one of its ~10 admin cmdlets that pac doesn't cover (e.g., `Add-PowerAppsSyncUser`).

### 1.3 Recommended interface (2026)

For Spaarke's current position (Model 1 shared, no customer commit yet):
- **Now**: `pac admin create` (interim per r1 §11.2 v3.2 M-10). Already what today's stand-up used.
- **Next**: BAP REST direct calls from L2 (removes pwsh shell-out — matches `Provision-Customer.ps1` STEP 5 pattern already validated in dev).
- **Target (post-Model-2 customer commit)**: TF Power Platform provider (D14 in design.md).

The BAP-REST-from-L2 path is the least-risk incremental win — no new CLI dependency, matches the pattern the rest of the L2 handlers use (`HttpClient` + `DefaultAzureCredential`), and closes the "handler shells out to pwsh" gap for H5.

### 1.4 What still requires PPAC UI (2026)

Very little. As of mid-2026, the following are documented UI-only or require the Power Platform for Admins V2 (preview) connector:
- Some tenant-wide governance settings (accessible via `pac admin update-tenant-settings` with `--setting-name` / `--setting-value`, but the schema is not fully published — trial-and-error required).
- Weekly-digest recipient email addresses (Managed Environments feature — no documented API).
- Managed Env Data Policies UI (the underlying DLP API is programmatic, but the tenant-DLP-vs-env-DLP linkage may need UI for some scenarios).

None of these block Spaarke's stand-up flow.

---

## 2. Environment Groups

### 2.1 What they are

Environment Groups are Power Platform's **tenant-level folder abstraction** for managed environments — a "cluster of envs governed by shared rules." Rules can enforce security/sharing, AI feature enablement, ALM/pipelines settings, backup retention, solution checker enforcement, generative AI settings, and maker onboarding markdown — all published centrally and enforced across every env in the group. When a rule is published at group level, the corresponding per-env setting becomes **locked (read-only)** in the individual env. Per-env exceptions are **not currently supported**.

**Hard prerequisite**: Environment Groups can only contain **Managed Environments**. Adding a non-managed env prompts you to upgrade it in place.

Each env can belong to **only one group** (no overlap, no nesting). Envs can span regions and types within a group.

### 2.2 Programmatic API

**Power Platform REST API** ([reference](https://learn.microsoft.com/en-us/rest/api/power-platform/environmentmanagement/environment-groups)), base URL `https://api.powerplatform.com`, api-version `2024-10-01`:

| Operation | Endpoint |
|---|---|
| Create | `POST /environmentmanagement/environmentGroups?api-version=2024-10-01` |
| Get | `GET /environmentmanagement/environmentGroups/{groupId}?api-version=2024-10-01` |
| List | `GET /environmentmanagement/environmentGroups?api-version=2024-10-01` |
| Update | `PATCH /environmentmanagement/environmentGroups/{groupId}?api-version=2024-10-01` |
| Delete | `DELETE /environmentmanagement/environmentGroups/{groupId}?api-version=2024-10-01` |
| Add environment | `POST /environmentmanagement/environmentGroups/{groupId}/environments/{envId}?api-version=2024-10-01` |
| Remove environment | `DELETE /environmentmanagement/environmentGroups/{groupId}/environments/{envId}?api-version=2024-10-01` |
| Operation status (LRO) | `GET /environmentmanagement/environmentGroups/{groupId}/operations/{opId}?api-version=2024-10-01` |

Auth scope: **`https://api.powerplatform.com/.default`** (this is a *different* audience from BAP `api.bap.microsoft.com`). Auth: `Bearer {token}`.

**pac CLI equivalents** (per admin.md):
- `pac admin add-group --environment-group <name> --environment <id>` — add env to group
- `pac admin list-groups` — enumerate groups
- No `pac admin create-group` documented — group creation currently CLI-less; the REST API or PPAC UI or Power Platform for Admins V2 connector must be used.

**Terraform**: `powerplatform_environment_group` resource ([docs](https://microsoft.github.io/terraform-provider-power-platform/resources/environment_group/)) supports create/get/delete of the group but does NOT (as of v4.1.0) expose a direct "assign env to group" resource. Workaround: use `powerplatform_tenant_settings` `environment_routing_target_environment_group_id` to make new-routed envs go into the group, or fall back to REST/pac for existing envs.

### 2.3 Recommended pattern for Spaarke

For **Model 1 shared** (multiple customers logically isolated in ONE shared Dataverse env): a single group is overkill — one env doesn't need a group. But keeping a `Spaarke Model 1 Shared` group with just the shared env in it is a clean way to enforce backup retention, solution checker, and sharing rules from a single control point that will still scale if you ever add a second shared stamp (e.g., regional).

For **Model 2 dedicated** (one env per customer): a group per **tier** (e.g., `Spaarke Enterprise Tier`, `Spaarke Regulated Tier`) is the right level of grouping — not one per customer (would defeat the purpose of centralized policy). Envs move between groups by removing + re-adding, so tier upgrades are supported.

**Not-recommended alternative**: one group per customer. This gives you 100 groups per 100 customers, each with identical rules, which is what the group abstraction is designed to eliminate.

---

## 3. Managed Environments

### 3.1 Features unlocked

Enabling Managed Environments on an env activates the following features (per [managed-environment-enable.md](https://learn.microsoft.com/en-us/power-platform/admin/managed-environment-enable) and the `pac admin set-governance-config` schema):

- **Weekly Digest email**: env-level usage insights for admins (parameter: `--exclude-analysis` to opt out).
- **Sharing Limits**: cap how many users a canvas app can be shared with (`--limit-sharing-mode`, `--max-limit-user-sharing`, `--disable-group-sharing`).
- **Solution Checker enforcement**: `--solution-checker-mode` = `none` / `warn` / `block`. `block` prevents solution import if checker fails.
- **Maker Onboarding**: `--maker-onboarding-markdown` + `--maker-onboarding-url`.
- **Suppress validation emails**: `--suppress-validation-emails`.
- **Data Policies (DLP)**: managed-env DLP scoped tighter than tenant DLP.
- **Pipelines**: the in-product Power Platform Pipelines feature **only works on Managed Environments** (both source and target must be managed) — this is the ALM plumbing for staged solution deployment across envs.
- **Backup differences**: Managed Envs support longer backup retention (`pac admin set-backup-retention-period` valid values: 7/14/21/28 days).
- **Env Group eligibility**: cannot be added to an Env Group without being Managed first.

### 3.2 Programmatic enable

**pac CLI** (recommended, per [admin.md](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/admin)):

```powershell
pac admin set-governance-config `
  --environment <env-id-or-url> `
  --protection-level Standard `                    # Standard = enable; Basic = disable
  --solution-checker-mode warn `
  --limit-sharing-mode NoLimit `
  --exclude-analysis                                # opt out of weekly digest
```

**PowerShell** (Microsoft.PowerApps.Administration.PowerShell):

```powershell
$GovernanceConfiguration = [pscustomobject] @{
    protectionLevel = "Standard"                  # or "Basic" to disable
    settings = [pscustomobject]@{
        extendedSettings = @{}
    }
}
Set-AdminPowerAppEnvironmentGovernanceConfiguration `
    -EnvironmentName <EnvironmentID> `
    -UpdatedGovernanceConfiguration $GovernanceConfiguration
```

**Terraform** (`powerplatform_managed_environment` resource, [docs](https://microsoft.github.io/terraform-provider-power-platform/resources/managed_environment/)):

```terraform
resource "powerplatform_managed_environment" "customer" {
  environment_id             = powerplatform_environment.customer.id
  is_usage_insights_disabled = false
  is_group_sharing_disabled  = true
  limit_sharing_mode         = "ExcludeSharingToSecurityGroups"
  max_limit_user_sharing     = 10
  solution_checker_mode      = "warn"
  suppress_validation_emails = true
  maker_onboarding_markdown  = "Welcome to Spaarke customer env..."
  maker_onboarding_url       = "https://docs.spaarke.com/customer-onboarding"
}
```

### 3.3 Licensing

Managed Environments **is included as an entitlement** with:
- Power Apps standalone licenses (per-app, per-user)
- Power Automate standalone
- Power Pages
- Copilot Studio
- All Dynamics 365 licenses (Sales, Customer Service, Field Service, etc.)

**PAYG environments count** — a PAYG env qualifies for Managed Environments if any user in the env has a qualifying license (which they generally will if they're using premium connectors). See [managed-environment-licensing](https://learn.microsoft.com/en-us/power-platform/admin/managed-environment-licensing) for the full matrix.

### 3.4 Recommended for Spaarke

**For Model 1 shared**: **yes** — enable Managed Environments on the shared env. Rationale: (a) required to use Environment Groups, (b) Weekly Digest gives Spaarke ops team visibility into shared-env activity, (c) Solution Checker enforcement on `warn` catches bad customer solution imports (if we ever allow customer solutions), (d) Sharing Limits prevent an accidentally over-shared component.

**For Model 2 dedicated**: **yes** — always enable. Enterprise/regulated customers expect governance features; Pipelines feature needs it; Env Group tier scoping needs it.

This is a **missing handler** in the r1 design. Recommend adding **H6a Enable Managed Environments** step between H5 (env create) and H6 (solution import). Cost: one API call per env. Idempotent: setting `protectionLevel = Standard` when already Standard is a no-op.

---

## 4. Dataverse Database / Storage

### 4.1 Database creation

**In 2026, Production/Sandbox/Developer envs created via `pac admin create` or the TF provider get a Dataverse database by default.** There is no separate "add database" step for the standard SaaS flow.

The "Add Dataverse database" concept exists for envs that were created *without* a database (a canvas-only or Automate-only env). This is an option in PPAC UI at create time only for legacy env types. For Spaarke's use case, this is not relevant — all Spaarke envs must have a database (to hold `sprk_*` entities + SpaarkeMaster solution).

**Important gotcha**: at DB creation time, the "Enable Dynamics 365 apps" checkbox is a **permanent decision**. If NO is chosen, D365 first-party apps (Sales, Customer Service, etc.) can NEVER be installed later. `pac admin create` does not expose this flag — it defaults to allowing D365 apps. If Spaarke ever wants to explicitly disable D365 apps for compliance reasons, that's a REST-only decision at env creation.

### 4.2 Capacity model (2026)

Per [capacity-storage](https://learn.microsoft.com/en-us/power-platform/admin/capacity-storage):

Three metered storage types, pooled at **tenant level** (not env level):
- **Database storage**: structured data tables, relationships, metadata. Pricing: ~$40/GB/mo pre-paid.
- **File storage**: files/images stored in Dataverse (activity attachments, notes files, etc.). ~$2/GB/mo. **April 2026 reclassification**: solution-aware components + metadata moved from database → file tier, easing the DB pressure many tenants hit.
- **Log storage**: audit log data. ~$10/GB/mo.

Tenant base entitlements (Dec 2025 refresh):
- Power Apps Premium: 20 GB DB + 40 GB file included per tenant, plus per-user accrual.
- PAYG env: **one-time entitlement of 1 GB DB + 1 GB file per environment**. Anything above is metered to Azure.

Each new Production or Sandbox env requires **at least 1 GB free DB capacity** from the tenant pool at creation. If tenant pool is exhausted, `pac admin create` fails with a capacity error — a preflight gap in the current F1-F12 lessons list.

### 4.3 Adding capacity programmatically

Tenant-pool capacity can be added via:
- Purchase (contracted) — buy more via M365 Admin Center or partner.
- PAYG — enable PAYG on the env, then any capacity above the 1 GB entitlement is metered to Azure automatically.

There is **no supported programmatic "add 10 GB of DB storage to my tenant" API** — that's a purchase transaction that goes through Microsoft billing (M365 admin, Azure marketplace, or reseller). This is *fine* because for Spaarke's Model 1 shared, one env consumes 1 GB baseline; even 100 customers of tenant-data sums to well under the ~200 GB you'd have with modest license attach. For Model 2 dedicated, per-customer PAYG solves this cleanly (customer pays for their own storage growth via their own Azure bill).

---

## 5. Pay-As-You-Go with Azure (Dataverse-Specific)

### 5.1 What PAYG means

Per [PAYG overview](https://learn.microsoft.com/en-us/power-platform/admin/pay-as-you-go-overview): PAYG links a Power Platform env to an **Azure subscription** via a **billing plan**. Once linked:
- Any Power Apps user access above the free entitlement → metered to Azure.
- Any Dataverse storage above the 1 GB DB + 1 GB file per-env entitlement → metered to Azure.
- Any Dataverse API request above the entitlement → metered to Azure.
- Any Power Automate flow run above the entitlement → metered to Azure.
- Any Copilot Studio message above the entitlement → metered to Azure (added Dec 2024).
- Any AI Builder consumption → metered to Azure.

A billing plan **creates a `Microsoft.PowerPlatform/accounts` resource** in the specified Azure sub + RG. Azure meters are billed under that resource on the standard Azure invoice.

**Only Production and Sandbox envs support PAYG.** Default, Developer, Trial, Teams envs do NOT.

### 5.2 Programmatic setup

**PPAC UI** is the primary Microsoft-documented path (create billing plan → select Azure sub → select RG → select Power Platform products → select envs to link).

**BAP REST endpoint** exists (`api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/billingPolicies?api-version=2022-03-01-preview` — under-documented but visible in PPAC UI network traffic). Not officially documented in Learn as of Aug 2026 — trial-and-error path.

**Terraform** ([`powerplatform_billing_policy`](https://microsoft.github.io/terraform-provider-power-platform/resources/billing_policy/) + [`powerplatform_billing_policy_environment`](https://microsoft.github.io/terraform-provider-power-platform/resources/billing_policy_environment/)):

```terraform
resource "azurerm_resource_group" "payg_rg" {
  name     = "rg-spaarke-payg"
  location = "westus2"
}

resource "powerplatform_billing_policy" "payg" {
  name     = "spaarke-model1-payg"
  location = "unitedstates"
  status   = "Enabled"
  billing_instrument = {
    resource_group  = azurerm_resource_group.payg_rg.name
    subscription_id = data.azurerm_client_config.current.subscription_id
  }
}

resource "powerplatform_billing_policy_environment" "link" {
  billing_policy_id = powerplatform_billing_policy.payg.id
  environment_id    = powerplatform_environment.customer.id
}
```

Or link at env create time via `billing_policy_id` on `powerplatform_environment` (single-shot).

### 5.3 Prerequisites

- Azure sub in the same Entra tenant as the Power Platform env.
- The user/SP creating the billing plan must be **owner or contributor** on the Azure sub.
- The **Microsoft.PowerPlatform resource provider** must be registered in the Azure sub (`az provider register --namespace Microsoft.PowerPlatform`). This is analogous to the Microsoft.Compute provider registration issue F6 hit — same class of "fresh sub needs provider registration first" gotcha.

### 5.4 Interaction with Managed Environments

**No conflict** — PAYG and Managed Environments are orthogonal. A PAYG env can be Managed. Managed features (Weekly Digest, DLP, etc.) work identically.

### 5.5 Recommended for Spaarke

**Model 1 shared** — **not needed**. The shared env is licensed via Spaarke's own Power Apps Premium seats (Spaarke's tenant, Spaarke pays). PAYG for shared adds cost pass-through complexity with no benefit — Spaarke absorbs all usage cost anyway and passes it via SaaS pricing.

**Model 2 dedicated** — **strongly recommended**. Each customer's env lives in the customer's Azure sub (or Spaarke's per-customer sub). PAYG lets the customer's own Azure bill absorb the meters cleanly, matches ISV cost-pass-through models, and avoids Spaarke pre-purchasing capacity per customer. Also: PAYG's 1 GB DB per-env entitlement + Azure-metered overage matches how customer envs actually grow.

**Missing from r1 design**: PAYG is not mentioned in the H2a-H14 handler catalog nor in the D-decision tables. This is a **gap** for Model 2 tenancy. For Model 1 it's a legitimate omission.

---

## 6. Solution Import Best Practices

### 6.1 F12's actual root cause

The 240 missing dependencies is **NOT a `pac solution import` failure** — it's a **solution-packaging failure at export time**. The SpaarkeMaster.zip on disk was exported from a source env that had 240+ components (Dynamics 365 first-party solution components, other Spaarke solution components, or unmanaged customizations) that the fresh Model 1 Prod env doesn't have.

Per [Microsoft's missing-dependency doc](https://learn.microsoft.com/en-us/troubleshoot/power-platform/dataverse/working-with-solutions/missing-dependency-on-solution-import), the fix depends on the source of each missing dep:

| Source of dep | Fix |
|---|---|
| A Dynamics 365 first-party app (e.g., D365 Sales) | Install that app in target env via `pac application install` or PPAC UI, then re-import. Track: **Applications** section of missing-deps page. |
| A first-party app that supports auto-deploy | Click **Deploy Dependencies** → system installs required first-party solutions, then imports. Sometimes ISV solutions inherit from OOB Dataverse first-party solutions — safe auto-install path. |
| Another managed solution (Spaarke's own solution chain) | Import the **same version** of that managed solution FIRST. Track: **Managed Solutions** section. |
| Unmanaged customizations from source env | Go back to source, include the missing components in the solution export, re-export. Track: **Unmanaged Components** section. |

### 6.2 Best-practice ISV packaging

For Spaarke to ship a fresh-env-installable SpaarkeMaster:

1. **Enumerate first-party dependencies**: does SpaarkeMaster reference anything from D365 apps (Sales, Customer Service, etc.)? If yes, either (a) declare them as prerequisites in ISV docs + install via `pac application install --application-name` before SpaarkeMaster, or (b) remove the dependencies (best if not truly needed).
2. **Order the Spaarke solution chain**: r1 design.md §11.1a already identifies **8 solutions** in `Deploy-DataverseSolutions.ps1`. Import must respect the dependency DAG — parents before children. This is what `Deploy-DataverseSolutions.ps1` does; the F12 failure suggests the fresh env is missing OTHER things (first-party or unmanaged) that even the correctly-ordered 8 depend on.
3. **Never export unmanaged from a dirty env**: if the SpaarkeMaster source-env has any unmanaged customizations (dev experiments, one-off tweaks), they leak into the export and become missing deps in target. Discipline: **always export from a clean managed-installed env** OR always export as **managed** (which strips unmanaged deps but only if the source was itself managed).
4. **Consider Package Deployer**: for the "install all 8 + 1 first-party in one shot" scenario, wrap them in a **Package Deployer package** (`.zip` with `PackageDeployer` project). Runs a stub .NET assembly that can install first-party apps, then multiple solutions in order, then post-install data seeding. Historic Spaarke inheritance path. Docs: [Overview of tools and apps used for ALM](https://learn.microsoft.com/en-us/power-platform/alm/tools-apps-used-alm), [Create AppSource package](https://learn.microsoft.com/en-us/power-platform/developer/appsource/create-package-app).
5. **Or use `pac solution import --stage-and-upgrade`**: better for upgrades than fresh installs. For fresh, plain `pac solution import` with `--force-overwrite` disabled is right.

### 6.3 The Web API path (for L2 handler)

Per [solution-api.md](https://github.com/MicrosoftDocs/power-platform/blob/main/power-platform/alm/solution-api.md):

- **Fresh install (small solution)**: `POST /api/data/v9.2/ImportSolution` synchronous. Blocks until done. Not recommended for large.
- **Fresh install (large solution) — recommended**: `POST /api/data/v9.2/ImportSolutionAsync` returns `AsyncOperationId`; poll `GET /api/data/v9.2/asyncoperations({id})?$select=statecode,message` until `statecode = 3` (Completed) or non-zero failure.
- **Upgrade (recommended for iterative deploys)**: two-step `StageSolution` → returns `StageSolutionUploadId` → `ImportSolutionAsync` with `StageSolutionUploadId` → poll. The two-step gives you validation feedback before the actual apply.
- **`ImportSolutionAsync` + `DeleteAndPromoteAsync`** = the "stage and upgrade" pattern for major-version bumps.

**Key parameters**:
- `OverwriteUnmanagedCustomizations`: **false** for prod (per doc: force-overwrite significantly slows managed imports; leave unmanaged customizations alone).
- `PublishWorkflows`: **true** for solutions with cloud flows/processes.
- `HoldingSolution`: **true** for staged upgrade.
- `LayerDesiredOrder`: control layer ordering for multi-solution deployments (rarely needed if you import in dep order).

This is what the Spaarke `Deploy-DataverseSolutions.ps1` does today via `pac solution import`. Porting to `HttpClient` + `ImportSolutionAsync` in H6 gets it in-process and removes the shell-out.

### 6.4 Managed vs unmanaged for customer envs

**Spaarke customer envs MUST get managed solutions only.** Unmanaged means the customer can modify components (delete a table, rename a column) and those changes stick, blocking future Spaarke upgrades. Managed layer prevents this — customer changes become a separate unmanaged layer that Spaarke's next upgrade can overwrite. This is Dataverse ALM 101 and is already the design intent in r1 §11.1a.

---

## 7. Application Users (MI-BFF-API Pattern)

### 7.1 Concept

An **Application User** = a `systemuser` row in the customer env with `applicationid` set to the Entra application-registration client ID (or the User-Assigned Managed Identity's app-id). Non-interactive user. No M365 license required. Not counted against the 7-non-interactive-user limit. Represents "the BFF making API calls into Dataverse on its own behalf."

Under the hood, an app user is created when you POST to the BAP `addAppUser` endpoint. Per [Microsoft Learn preview doc](https://learn.microsoft.com/en-us/power-platform/admin/create-dataverseapplicationuser):

```http
POST https://api.bap.microsoft.com/providers/Microsoft.BusinessAppPlatform/scopes/admin/environments/{environmentId}/addAppUser?api-version=2020-10-01
Authorization: Bearer {token}
Content-Type: application/json

{
  "servicePrincipalAppId": "AzureAD_App_Registration_ClientID"
}
```

Response: **200 OK** always (as long as auth succeeds). The app user is created and **always given System Administrator role** by this endpoint. To scope it tighter (Spaarke's ADR-028 preference), use `pac admin assign-user` post-facto (see 7.3).

### 7.2 Managed Identity variant

**A User-Assigned Managed Identity (UAMI) has a client ID and a service principal in Entra.** Passing the UAMI's client ID to `servicePrincipalAppId` above works identically to a regular SP. Spaarke's H10 pattern uses this: create a UAMI in Azure → grant Graph app-role permissions on its SP → add it as Dataverse app user via BAP → assign a scoped security role. The `MI-BFF-API` name is the UAMI's display name; the `applicationid` on the systemuser is the UAMI's clientId.

**Key benefit**: no client secret. `DefaultAzureCredential(ManagedIdentityClientId)` in BFF gets tokens for both Graph AND Dataverse (`{envUrl}/.default` scope) with zero rotation surface. Exactly what ADR-028 requires and what r3 landed.

### 7.3 pac CLI shortcuts

Per [admin.md](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/admin):

**Create app user WITH a fresh Entra app-reg** (one-shot):
```powershell
pac admin create-service-principal --environment <env-id> --name "MI-BFF-API-Customer1" --role "System Administrator"
```
Returns: TenantId, Application ID, Client Secret (clear-text), Expiration. **Note**: this creates a NEW app-reg — probably not what Spaarke wants for MI (Spaarke wants to USE the existing UAMI, not create a new SP).

**Assign an EXISTING SP/MI as an app user** (Spaarke's canonical path):
```powershell
pac admin assign-user `
  --environment <env-id> `
  --user <application-id-of-existing-sp-or-uami> `
  --role "Spaarke BFF Custom Role" `
  --business-unit <bu-id> `
  --application-user
```
The `--application-user` switch tells the command "this is an app user, not a human." The `--user` param takes the Application ID (client ID). `--role` picks a security role by name or ID — use a **custom scoped role** per ADR-028 (not System Administrator).

**Verify (Dataverse Web API query)**:
```
GET {envUrl}/api/data/v9.2/systemusers?$filter=applicationid eq {app-id}&$select=systemuserid,fullname,isdisabled
```
Should return exactly 1 record with `isdisabled = false`. This is Silent-Fail Trap T2 in the design (§4B).

### 7.4 Terraform

[`powerplatform_user`](https://microsoft.github.io/terraform-provider-power-platform/resources/user/) — creates + assigns:
```terraform
resource "powerplatform_user" "bff_api" {
  environment_id = powerplatform_environment.customer.id
  aad_id         = azurerm_user_assigned_identity.bff.client_id
  security_roles = ["Spaarke BFF Custom Role"]
  business_unit_id = data.powerplatform_business_units.root.id
}
```

D14 target for r1. Adopts cleanly when TF migration lands.

### 7.5 Common failure modes

- **T1 (design.md §4B)**: forgot `--application-user` flag → pac tries to look up the app-id as a human user in Entra → confusing "user not found" error.
- **T2**: app-reg exists but wasn't added to the env → `systemusers` query returns empty → every BFF call 403s silently.
- **T3**: app user exists but missing Graph app-role assignments on its SP (the 14 GUIDs in `GraphAppRoles.cs`) → Graph calls 403 while Dataverse calls succeed. Requires nightly parity check.
- **Cross-tenant Entra app-user with mismatched tenant**: adding an app-id from a foreign tenant fails silently — must be same-tenant SP/MI.

---

## 8. Customer Users (B2B Guest + Internal)

### 8.1 The full flow

For a customer user to reach a Dataverse env, five steps must complete:

1. **User exists in Entra** — either created in customer's own tenant (Model 2) or invited as a B2B guest into Spaarke's tenant (Model 1).
2. **User has a license** that entitles Dataverse access (any Power Apps standalone license, D365 license, or the env is PAYG).
3. **`restrictGuestUserAccess` is Disabled** on the env — this is the per-env B2B guest toggle. Default is **true (restricted)** for all NEW envs since March 2026, so this must be flipped explicitly for guest access.
4. **User synced/added to env** as a `systemuser` row — either via automatic Entra sync (if in a security group added to the env), on-demand access (first attempt to reach env prompts sync), or explicit API call (`Add-AdminPowerAppsSyncUser`).
5. **Security role assigned** — `pac admin assign-user --user <upn-or-oid> --role "Customer User Role" --environment <env>`.

### 8.2 B2B guest specifics (Model 1)

For Model 1, external customer users are B2B guests in **Spaarke's Entra tenant**. This means:
- Customer signs up via Spaarke's onboarding flow.
- Spaarke creates a B2B invite via Graph (`POST /invitations`) — same pattern as the existing r1 `GraphUserService`.
- Guest accepts invite (redeems in their own home tenant).
- Guest is added to Spaarke's Dataverse env with a per-customer security role.
- `restrictGuestUserAccess = false` on the shared env.
- **Licensing** — the shared env's Power Apps Premium seats must cover the guest count, OR the env is PAYG (each guest's usage metered).

Per [guest access doc](https://learn.microsoft.com/en-us/power-platform/admin/security/guest-access):

```powershell
# Set once at env level
pac env update-settings --environment <env-url> --name "restrictGuestUserAccess" --value false

# Then per-user
pac admin assign-user --environment <env-id> --user "guest@customer.com" --role "Customer User Role"
```

**Limitations of B2B guests in Dataverse**:
- Cannot own records by default (must configure "Allow guest ownership" — check current 2026 status).
- Some connectors don't support guest identities (Bing, custom OAuth flows). Must test per-integration.
- **Copilot Studio Graph connector knowledge sources** may leak to guests even when `restrictGuestUserAccess = true`, per the current guest-access doc. Worth flagging in Spaarke's Model 1 security review.

### 8.3 Internal Spaarke tenant users (dev/support)

Simpler flow — same tenant users:
- Add to security group (auto-sync into env), or
- `pac admin assign-user --user "spaarke.employee@spaarke.com" --role "Spaarke Support Role"` (on-demand sync).

### 8.4 Model 2 dedicated (customer users in customer tenant)

Different mechanism — Spaarke has an **app-only** presence in customer's Dataverse (via app user); the customer's own Entra admin adds their own users to their own env. This is a **customer-managed step** in Model 2, not something Spaarke automates. Spaarke's docs must clearly hand-off this step to the customer's IT.

Spaarke's r1 handler H11 (per design.md line 154) covers the user-provisioning identity preset for Spaarke's own control. Model 2 external-customer users are OUT of H11's scope by design.

### 8.5 The Graph invitation pattern (already in Spaarke)

`src/server/api/Sprk.Bff.Api/Services/Registration/GraphUserService.cs` already implements the B2B invite pattern (per `resource-discovery-2026-08-16.md`). Reusable for Model 1 guest-onboarding.

---

## 9. Common 2026 Gotchas + Post-Cutoff Changes

### 9.1 Post-2024 Microsoft changes affecting Spaarke

- **Fresh Azure subscriptions now get zero App Service Plan quota in East US** — auto-denied on request (see F3). Spaarke's canonical WestUS2+WestUS3 pattern is now the DEFAULT recommendation, not an alternative.
- **`restrictGuestUserAccess` defaults to TRUE for new envs since March 2026** — all Spaarke Model 1 shared envs must explicitly flip to `false` after creation, or B2B onboarding silently fails.
- **Solution-aware components reclassified to file storage (April 2026)** — reduces DB pressure. Positive news for Spaarke's SpaarkeMaster footprint.
- **Environment Groups GA'd in 2024** — this is why r1 design predates the concept and doesn't mention it. It's now a first-class governance surface Spaarke should embrace.
- **Managed Environments included in ALL Power Apps standalone + D365 licenses** (was originally a paid add-on) — no longer a cost objection.
- **Copilot Studio PAYG (Dec 2024)** — Copilot messages billable via same PAYG plan. Relevant if Spaarke ships Copilot agents in customer envs.
- **Pay-as-you-go for Dataverse capacity confirmed** — 1 GB DB + 1 GB file per env free, overage metered.
- **`pac admin create-service-principal`** now returns clear-text secret + expiration — treat output carefully (log stripping).
- **Dataverse Agents with Entra Agent ID** (preview May 2026) — new identity type for AI agents in Dataverse, separate from app users. Worth watching but not blocking r1.
- **`Add-AdminPowerAppsSyncUser` still supported** but Microsoft's investment is in pac CLI + BAP REST + TF. Legacy PS module maintenance-only.

### 9.2 What's deprecated / moving away

- **`Xrm.Tooling.CrmConnector.PowerShell`** — legacy Dynamics 365 module. Retired in favor of pac CLI. Any Spaarke script still using it should port.
- **`Microsoft.Xrm.Tooling.Connector`** .NET assembly — soft-deprecated. Use `Microsoft.PowerPlatform.Dataverse.Client` (already ADR-028 stack).
- **Environment-scoped DLP policies without Managed Environments** — still work but Microsoft steers you to tenant DLP + managed-env DLP for large fleets.

### 9.3 What's still preview

- **Power Platform Terraform provider** — still public preview at v4.1.0 (Jan 2026). Ships new features monthly. **Not production-blocked** — the auth model, resource model, and REST wrappers are stable; risk is mainly around edge-case features (e.g., env-group direct assign).
- **Dataverse `addAppUser` REST endpoint** — labelled "(preview)" in Microsoft Learn since 2024. Still the only documented programmatic endpoint. Consumed at scale by every SP-based deployment tool. Effectively production.
- **Dataverse Agents (Entra Agent ID)** — preview May 2026.

---

## 10. Reference Architectures for SaaS/ISV Using Dataverse

### 10.1 Microsoft's official ISV guidance

**Package Deployer** ([tools-apps-used-alm](https://learn.microsoft.com/en-us/power-platform/alm/tools-apps-used-alm)) is Microsoft's canonical ISV distribution mechanism: bundle multiple solutions + first-party app installs + code + data seeding + post-install customizations into a single `.zip`. Runs a stub .NET assembly against a target env. Historic pattern; still fully supported. AppSource offerings (Microsoft's marketplace for Power Platform ISV apps) use Package Deployer under the hood.

**AppSource** ([create-package-app](https://learn.microsoft.com/en-us/power-platform/developer/appsource/create-package-app)) — Microsoft's ISV marketplace. If Spaarke wants zero-touch customer install (customer picks Spaarke from AppSource, Microsoft handles all the install machinery), this is the endgame. Requires MPN partner status, packaging certification, storefront listing. **Not a near-term Spaarke path** but worth architecting toward — the Package Deployer + versioned managed solution structure Spaarke is building IS the AppSource-compatible artifact.

### 10.2 Common ISV patterns

- **Template environments**: maintain a "gold master" env with all Spaarke solutions installed, use `pac admin copy --type FullCopy` to instantiate per-customer. Faster than fresh-install-then-import-8-solutions. Downsides: template drift, per-region copy latency, doesn't support Azure sub differences for PAYG.
- **Provisioning bots**: exactly what Spaarke's r1 L2 control-plane is — a service that queues env creation + config work. Microsoft's own CoE Starter Kit uses this pattern.
- **Solution channels**: many-solution ordered install (Spaarke's 8-solution `Deploy-DataverseSolutions.ps1` = this pattern). Best when solutions have dependency graph.
- **Solution + Package Deployer hybrid**: solutions for schema/UI, Package Deployer for first-party dependencies + data seed + config. Spaarke's shape.
- **AppSource marketplace**: end-state for enterprise self-service install.

### 10.3 CoE Starter Kit as reference

Microsoft's [CoE Starter Kit](https://learn.microsoft.com/en-us/power-platform/guidance/coe/starter-kit) is the largest public reference of tenant-wide Dataverse env provisioning + governance. Uses Managed Environments extensively, Env Groups for tier segregation, DLP for connector governance, Power Automate flows for env-creation-request approvals. Spaarke's L2 control-plane is a smaller-scope custom version of the CoE pattern.

### 10.4 Mapping to Spaarke's Model 1 vs Model 2

| Aspect | Model 1 shared | Model 2 dedicated | ISV reference |
|---|---|---|---|
| Env creation | 1 shared env, created ONCE per stamp | 1 env per customer, created per contract | ISV template-env clone OR fresh install |
| Managed Environments | Yes on the shared env | Yes on each customer env | Universally recommended |
| Env Groups | 1 group per stamp (holding the 1 shared env) | 1 group per tier (Enterprise, Regulated), holding many envs | Group-per-tier pattern from CoE Kit |
| PAYG | No (Spaarke absorbs cost) | Yes (customer's Azure sub) | Customer-Azure-billed is the ISV norm |
| Solutions | Same SpaarkeMaster + config solutions installed once per stamp | Same SpaarkeMaster + config solutions installed per env | Managed solution ALM |
| App users | 1 UAMI (MI-BFF-API) added to shared env | 1 UAMI per customer OR 1 SP added to each customer env | Per-env app-user via BAP addAppUser |
| End users | B2B guests in Spaarke tenant | Users in customer's own tenant | Model 2 matches enterprise SaaS pattern |
| Customer isolation | Logical (tenantId filter, per-tenant KV, POA sharing) | Physical (dedicated Azure sub + Dataverse env) | Model 2 = compliant; Model 1 = trial/SMB |

Both models are legitimate ISV patterns. Model 1 is unusual (most Dataverse ISVs go dedicated-only) but is a reasonable trial-tier / SMB compromise IF the logical isolation invariants (§4D in design.md) are strictly enforced.

---

## Spaarke Gap Analysis

For each of the 10 topics: where Spaarke currently is vs. where Microsoft's recommended approach is.

| # | Topic | Spaarke today | Microsoft recommended | Gap | Priority |
|---|---|---|---|---|---|
| 1 | Env creation | `pac admin create` shell-out from PS1 (H5, interim per M-10) | BAP REST from L2 for interim; TF `powerplatform_environment` when TF migration lands | Shell-out to CLI; F12 was hit at post-create step, not create itself. Interim is fine short-term. | Medium |
| 2 | Env Groups | **Not mentioned in design.md** | Env Group per tier for policy scale-out | ABSENT — new capability | Low for Model 1 (1 env); Medium for Model 2 |
| 3 | Managed Environments | **Not mentioned in design.md** as a distinct enable step | `pac admin set-governance-config` post-H5 | ABSENT — new step needed | High — required for Env Groups + Pipelines, and Sharing Limits is a real Spaarke concern |
| 4 | Dataverse DB | Implicit at env-create (default in all `pac admin create --type Production`) | Same. No separate step for Spaarke's flow. | No gap — already implicit | N/A |
| 5 | PAYG w/ Azure | **Not mentioned in design.md** | Billing plan + Azure sub link for Model 2 dedicated | ABSENT for Model 2 (Model 1 legitimately N/A) | High for Model 2 (unblocks per-customer-Azure-billed pattern) |
| 6 | Solution import | `pac solution import` via `Deploy-DataverseSolutions.ps1` (8 solutions, dependency-ordered); H6 planned as Web API `ImportSolutionAsync` port | Web API `ImportSolutionAsync` + `AsyncOperation` polling. Missing-deps is a packaging-time issue, not import-time. | F12's actual gap is upstream in the packaging pipeline. Need: (a) enumerate first-party deps in SpaarkeMaster; (b) either declare + install prerequisites OR remove; (c) verify solutions truly package as self-contained managed. | **Critical — this is F12** |
| 7 | Application Users (MI-BFF-API) | H10 planned via TF `powerplatform_user` (D14 target); interim = `pac admin assign-user --application-user`; the `addAppUser` BAP endpoint is the underlying REST | Same as Spaarke's plan. Good alignment. | No architectural gap. Execution deferred (M-10). | Low |
| 8 | Customer users | H11 covers Spaarke-controlled provisioning; B2B via existing `GraphUserService` | Same. Model 2 out of scope per design. | Missing: explicit toggle of `restrictGuestUserAccess = false` for Model 1 shared env. Silent-fail risk if omitted. | Medium |
| 9 | 2026 gotchas | Design.md doesn't cite Env Groups (2024), PAYG for Dataverse, or the guest-access default flip (Mar 2026) | See §9 above | Documentation staleness | Low (doc-only) |
| 10 | ISV reference architecture | Design.md §11.1a documents solution reconciliation; L2 control-plane pattern IS the ISV bot pattern | AppSource-compatible endgame; Package Deployer for post-install seeding | No hard gap — future direction | Low (roadmap) |

---

## Recommended Plan

Ordered by dependency + priority. Each item is one r1 task (or task chain).

### Immediate (unblock F12, this session or next)

**R1. Diagnose SpaarkeMaster missing-deps upstream at packaging time** (Critical) — Open SpaarkeMaster.zip, extract `solution.xml`, enumerate the `<MissingDependencies>` node. Categorize each: (a) D365 first-party app (must install prerequisite), (b) another Spaarke managed solution (must import in dep order first), (c) unmanaged customization from source env (must re-export). This tells us whether the fix is: install D365 apps first, adjust solution ordering, or fix a leaky export.

**R2. Fix the packaging pipeline based on R1's findings** (Critical) — Depending on R1: either (a) update `Deploy-DataverseSolutions.ps1` to prepend `pac application install` calls for identified first-party deps, (b) fix solution DAG ordering, or (c) re-export SpaarkeMaster from a clean managed source (a rebuild-from-managed-source scenario).

**R3. Enable Managed Environments on the fresh Model 1 Prod env** (High) — One command:
```powershell
pac admin set-governance-config `
  --environment https://spaarke-model1-prod.crm.dynamics.com `
  --protection-level Standard `
  --solution-checker-mode warn
```
Unlocks: Weekly Digest, Sharing Limits, Solution Checker enforcement, Env Group eligibility, Pipelines (future).

**R4. Flip `restrictGuestUserAccess = false` on the fresh Model 1 Prod env** (Medium — blocks B2B) —
```powershell
pac env update-settings --environment https://spaarke-model1-prod.crm.dynamics.com --name "restrictGuestUserAccess" --value false
```
Without this, any B2B customer user gets silent access-denied.

### Short-term (within r1 Phase B/C absorption)

**R5. Add H6a "Enable Managed Environments" handler between H5 and H6** — spec update, handler skeleton, POST via BAP `governance/environmentSetting` endpoint OR `pac admin set-governance-config` in the interim sidecar. Idempotent by design.

**R6. Document the Env Group decision** in design.md — even a "we're NOT using Env Groups for Model 1 because it's a single env, but Model 2 will use a `spaarke-model2-{tier}` group" line captures the intent. Add to D-decision table as D21.

**R7. Amend design.md §11 with the F12-lessons packaging discipline** — a subsection titled **"SpaarkeMaster packaging must be self-contained OR declare first-party prerequisites"** with the R1 categorization procedure and the "always re-export from clean managed source" rule.

**R8. Document PAYG deferral for Model 1, adoption for Model 2** — new spec/design section explicitly saying Model 1 = no PAYG (Spaarke-tenant licenses), Model 2 = PAYG mandatory (customer-Azure-billed).

### Medium-term (Phase D-E of r1)

**R9. Port H5 from `pac admin create` shell-out to BAP REST direct call from L2** — matches the H0 quota-check pattern; removes the last CLI dependency from H5.

**R10. Port H6 from `Deploy-DataverseSolutions.ps1` shell-out to Web API `ImportSolutionAsync` + `AsyncOperation` polling** — planned per Wave D-2. Now with F12 lessons baked in: pre-flight each solution's dependencies against the target env BEFORE calling `ImportSolutionAsync`.

**R11. Add H11a "Set restrictGuestUserAccess" (Model 1 only)** — the guest-access flip. One `pac env update-settings` call. Runs after H10 (app user setup) and before H11 (user provisioning).

### Long-term (Model 2 first-customer engagement)

**R12. Add H2c "Create PAYG billing plan + link to env" (Model 2 only)** — Azure sub + RG must exist first (H2a Bicep territory). Add `powerplatform_billing_policy` + `powerplatform_billing_policy_environment` to TF module (or REST equivalent).

**R13. Migrate H5 + H6 + H10 to TF Power Platform provider** (D14 target) — deferred to first Model 2 customer per M-10. TF v4.1.0 still preview; monitor for GA before committing.

**R14. Add H1a "Create Environment Group + assign env" (Model 2 tier-based)** — becomes a real handler when the second Model 2 customer signs (first customer alone doesn't need a group).

### Roadmap (not r1 scope)

**R15. Package Deployer wrapper for SpaarkeMaster** — bundle all 8 solutions + prerequisite installs + post-install seeding into a single `.zip`. Enables one-command install to a target env. Precursor to any AppSource ambitions.

**R16. AppSource listing** — if Spaarke ever wants self-service install by customer admins.

---

## Open Questions (Owner Decision Required)

1. **Managed Environments for Model 1 shared env — yes or no?** Recommendation is yes, but this locks certain settings (sharing limits become read-only per group rules). Owner call.
2. **Env Groups scope**: 1 group per stamp (holding the shared env), no groups at all for Model 1, or wait until Model 2? Recommendation: 1 group per stamp for future-proofing, but this can be added later without disruption.
3. **PAYG for Model 1**: confirm the "Spaarke absorbs cost" model matches the SaaS pricing intent. If Spaarke ever wants per-customer usage attribution in the shared env, PAYG per-customer-app is technically possible but adds significant complexity.
4. **B2B guest access default**: OK to flip `restrictGuestUserAccess = false` on the shared env at H5 time as a hard-coded default? Any Spaarke customer that wants to disable B2B onboarding for their own users would need a per-customer setting somewhere — which is a Model-1 architecture question, not just a provisioning question.
5. **SpaarkeMaster packaging discipline**: does Spaarke want to invest in Package Deployer wrapping (R15) now, or keep the multi-solution PS1 approach through Model 2 launch? Package Deployer is a one-time authoring cost with lasting payoff.
6. **Model 2 first-customer commit trigger**: current design says TF migration + PAYG + Env Group work all wait for first Model 2 commit. Is there a specific customer/date the r1 team should design toward, or is "we'll refactor when the deal closes" acceptable risk?
7. **`sprk_dataverseenvironment` registry schema — does it capture Managed-Env-enabled Y/N, Env-Group membership, PAYG billing-plan-id?** If not, adding these 3 columns now is cheap; deferring adds a schema migration when Model 2 or Env Groups arrive.

---

## Sources Consulted (Most-Authoritative First)

### Microsoft Learn (primary)

- [Environment groups (Power Platform)](https://learn.microsoft.com/en-us/power-platform/admin/environment-groups) — full concept + programmatic surface (updated 2026-08-14)
- [Enable managed environments](https://learn.microsoft.com/en-us/power-platform/admin/managed-environment-enable) — pac + PowerShell (updated 2026-06-24)
- [Pay-as-you-go plan overview](https://learn.microsoft.com/en-us/power-platform/admin/pay-as-you-go-overview) — Azure-billed meters model
- [Set up a pay-as-you-go plan](https://learn.microsoft.com/en-us/power-platform/admin/pay-as-you-go-set-up) — billing plan create flow
- [pac admin command reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference/admin) — all admin subcommands (2026-02-25)
- [Create a Dataverse application user (preview)](https://learn.microsoft.com/en-us/power-platform/admin/create-dataverseapplicationuser) — BAP REST endpoint
- [Missing Dependencies During Solution Import](https://learn.microsoft.com/en-us/troubleshoot/power-platform/dataverse/working-with-solutions/missing-dependency-on-solution-import) — F12 root-cause reference
- [Solution API](https://github.com/MicrosoftDocs/power-platform/blob/main/power-platform/alm/solution-api.md) — ImportSolutionAsync + StageAndUpgrade
- [Sample: Solution staging with asynchronous import](https://learn.microsoft.com/en-us/power-apps/developer/data-platform/org-service/samples/solution-stage-and-import) — StageSolutionUploadId pattern
- [Environment Groups REST API](https://learn.microsoft.com/en-us/rest/api/power-platform/environmentmanagement/environment-groups) — full CRUD surface (api-version 2024-10-01)
- [Dataverse capacity-based storage details](https://learn.microsoft.com/en-us/power-platform/admin/capacity-storage) — 2026 storage model
- [Control guest access to Power Platform environments](https://learn.microsoft.com/en-us/power-platform/admin/security/guest-access) — restrictGuestUserAccess default (Mar 2026)
- [Create users (Power Platform)](https://learn.microsoft.com/en-us/power-platform/admin/create-users) — user sync overview
- [Overview of tools and apps used for ALM](https://learn.microsoft.com/en-us/power-platform/alm/tools-apps-used-alm) — Package Deployer, pac pipeline

### Microsoft Terraform Power Platform Provider (public preview v4.1.0)

- [Provider home](https://microsoft.github.io/terraform-provider-power-platform/) — resources + auth
- [powerplatform_environment](https://microsoft.github.io/terraform-provider-power-platform/resources/environment/)
- [powerplatform_managed_environment](https://microsoft.github.io/terraform-provider-power-platform/resources/managed_environment/)
- [powerplatform_environment_group](https://microsoft.github.io/terraform-provider-power-platform/resources/environment_group/)
- [powerplatform_solution](https://microsoft.github.io/terraform-provider-power-platform/resources/solution/)
- [powerplatform_billing_policy](https://microsoft.github.io/terraform-provider-power-platform/resources/billing_policy/)
- [powerplatform_environment_application_package_install](https://microsoft.github.io/terraform-provider-power-platform/resources/environment_application_package_install/)
- [GitHub: microsoft/terraform-provider-power-platform](https://github.com/microsoft/terraform-provider-power-platform)

### Microsoft GitHub (docs source-of-truth)

- [MicrosoftDocs/power-platform: admin folder](https://github.com/MicrosoftDocs/power-platform/tree/main/power-platform/admin) — env/managed/payg canonical
- [MicrosoftDocs/SupportArticles-docs: missing-dependency doc](https://github.com/MicrosoftDocs/SupportArticles-docs/blob/main/support/power-platform/dataverse/working-with-solutions/missing-dependency-on-solution-import.md)

### Community references (secondary — cross-checked against Microsoft primary)

- [Rajeev Pentyala: PAYG billing plan step-by-step (Jul 2026)](https://rajeevpentyala.com/2026/07/12/step-by-step-power-platform-pay-as-you-go-create-a-billing-plan-and-link-an-environment/)
- [Sky Soft Connections: Env Group strategy for multi-tenant](https://www.skysoftconnections.com/environment-group-strategy-for-multi-tenant-power-platform/)
- [Dynamics Chronicles: Managed Envs deep dive](https://dynamics-chronicles.com/article/dataverse-managed-environments-deep-dive)
- [NETWORG: Making Dataverse solution imports fast](https://blog.networg.com/making-solution-imports-fast/)

### Spaarke project sources

- [`projects/customer-provisioning-orchestration-r1/design.md`](../design.md) — v3.5 (H0-H14 catalog, D14 TF target, §4B silent-fail traps, §11.1a solutions reconciliation)
- [`projects/customer-provisioning-orchestration-r1/notes/lessons-learned-model1-prod-standup-2026-08-22.md`](./lessons-learned-model1-prod-standup-2026-08-22.md) — F1-F12 arc, especially F12 (solution install gap)
- [`projects/customer-provisioning-orchestration-r1/notes/r1-gap-analysis-2026-08-18.md`](./r1-gap-analysis-2026-08-18.md) — prior gap analysis for cross-reference
- [`projects/customer-provisioning-orchestration-r1/notes/design-study-ds8-uami-dv-appuser-maturity.md`](./design-study-ds8-uami-dv-appuser-maturity.md) — UAMI as Dataverse app user (H10 canonical)
- [`scripts/Deploy-DataverseSolutions.ps1`](../../../scripts/Deploy-DataverseSolutions.ps1) — 8-solution import chain (per design.md §11.1a)
- [`scripts/Provision-Customer.ps1`](../../../scripts/Provision-Customer.ps1) STEP 5 — BAP REST env-creation sequence

---

*Report authored by researcher subagent, 2026-08-22. Findings written to `.claude/agent-memory/researcher/` for future-session recall.*
