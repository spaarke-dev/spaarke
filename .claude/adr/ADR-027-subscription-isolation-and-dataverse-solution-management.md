# ADR-027: Subscription Isolation and Dataverse Solution Management

| Field | Value |
|-------|-------|
| **Status** | Accepted (amended 2026-06-02, 2026-09-28, 2026-10-07) |
| **Date** | 2026-03-13 |
| **Accepted** | 2026-05-19 (in production: platform.bicep / customer.bicep split deployed) |
| **Amended** | 2026-06-02 — managed-solution prescriptions softened to "future direction"; current Spaarke practice is **unmanaged solutions in all environments** (dev, test, staging, prod). The managed-solution model remains the target long-term but is not enforced today. Amendment reflects actual practice confirmed by project owner.<br>2026-09-28 — **Decision 1 replaced**: one Azure subscription **per customer**, not per environment. Answers this ADR's own open Context question 4. Source: D-12 §3a (`projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md`). CLAUDE.md §6.5 path B.<br>2026-10-07 — **Decisions 3 + 4 replaced**: customer environments get **one** solution, `SpaarkeMaster`, **managed by default and unmanaged only on explicit instruction** (owner D8, 2026-09-30); the 2026-06-02 "unmanaged everywhere" practice and the 9-solution tier list are withdrawn. Source: `projects/customer-provisioning-orchestration-r1/notes/t218-plan.md`. CLAUDE.md §6.5 path B. |

## Decisions

### 1. Subscription Isolation — **AMENDED 2026-09-28: PER-CUSTOMER**

**Every customer gets their own Azure subscription**, containing their own resource group. Applies to both
D-12 tenancy models — Model 1 puts that subscription in **Spaarke's** Azure tenant, Model 2 in the
**customer's**.

🔴 **SUPERSEDED**: "environment-separated subscriptions (Dev + Production)" with all customers sharing one
production subscription. Environment separation still exists but is **subordinate** to customer separation.

**Why**: billing segregation is a requirement (a subscription is Azure's billing boundary) · under Model 1
every customer presents the **same** `tenantId`, so every `tenantId`-keyed isolation control — AI Search
filter, Cosmos partition, SPE container resolver, `tenant:{tenantId}:…` Redis key — **cannot separate
customers and reports success while failing**; a subscription is a boundary, not a filter · Azure OpenAI TPM
quota is **per-subscription-per-region**, so separate OpenAI resources in one subscription still share a
quota pool.

🔴 **Forced consequence — a dedicated App Service Plan per customer.** An App Service app cannot use a plan
in a **different subscription**, so "shared plan, dedicated app per customer" is **unavailable**, not merely
rejected. Verified 2026-09-28 against [Manage an App Service plan](https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage#move-an-app-to-another-app-service-plan)
and [MS Q&A](https://learn.microsoft.com/en-us/answers/questions/1048743/share-app-service-plan-across-different-subscripti).
⚠️ Do not cite the move-restriction's "same **resource group**" clause as the general rule — at *create*
time, apps in different resource groups of the **same subscription** can share a plan. Only the
cross-*subscription* prohibition is load-bearing here.

### 2. Resource Groups
- Shared platform: `rg-spaarke-platform-{env}` — created by `platform.bicep`. ⚠️ Survivor of the retired
  shared-tier model; D-12 §3 dedicates every resource per customer, so this group is expected to shrink or
  disappear. Inventory tracked in D-12 §6 / `COMPONENT-INVENTORY.md` §7 — not resolved by the amendment.
- Per-customer: `rg-spaarke-{customerId}-{env}` — created by `customer.bicep`. **Unchanged pattern, new
  location**: as of 2026-09-28 this group sits in the customer's **own** subscription.
- Both use `targetScope = 'subscription'` (declarative RG creation) — the templates don't change, the
  target subscription does, per customer.
- **Runtime handle on this boundary** (D-14, `unified-access-control-r2` task 123): the per-customer
  stamp emits `Customer__Id` as an App Service setting, which the BFF reads through
  `Configuration/CustomerIdentity.cs`. Absent it, the id is derived from `WEBSITE_RESOURCE_GROUP`
  (itself `rg-spaarke-{customerId}-{env}`); absent both, the BFF **refuses to start** — there is no
  default, because an absent customer identity must never resolve to a shared value. 🔴 The
  `rg-spaarke-platform-{env}` group above matches the per-customer *shape*, so it is explicitly
  **deny-listed** from derivation; a BFF deployed there must set `Customer__Id` by hand. Procedure:
  [`SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md` § 6.5.1](../../docs/guides/SPAARKE-CUSTOMER-DEPLOYMENT-GUIDE.md).

### 3. Dataverse Solutions — **AMENDED 2026-10-07: ONE PACKAGE, MANAGED BY DEFAULT**

- **One solution, `SpaarkeMaster`**, holds everything a customer environment needs (Microsoft ALM "single
  solution" pattern). Its scope is a **rule**: every `sprk` component in the authoring environment (spaarkedev1)
  plus the `sprk_` columns on OOB tables, minus a committed exclusion list with a reason per entry.
- **Customer environments: managed by default.** Unmanaged **only on explicit instruction** — the run's
  `solutionPackageType = unmanaged`, recorded on the registry row. No environment- or server-level default flips it.
- **Dev (authoring): unmanaged.**
- Provisioning (H6) **refuses** to switch an environment from one type to the other and refuses a downgrade.
- Environment-variable **definitions** ship in the package; **values never do** (H7 writes them per customer).

🔴 **SUPERSEDED**: "unmanaged in all environments; do not enforce managed" (2026-06-02) and the
`SpaarkeCore → SpaarkeWebResources → Tier 3` import order.

### 4. Dataverse CI/CD — **AMENDED 2026-10-07**
- **Git is the source of record**: each release exports SpaarkeMaster from dev (managed + unmanaged) and commits it
  unpacked (`src/dataverse/solutions/SpaarkeMaster/`); the diff is reviewed in a PR.
- CI packs both zips from git and publishes them, with a manifest, to the provisioning-artifacts store; H6 imports
  from there. No hand-built or hand-uploaded artifact.
- Upgrade = re-run H6 (`StageAndUpgrade`; a component removed from the package is deleted in managed environments).
- Runbook: [`docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md`](../../docs/procedures/SPAARKE-SOLUTION-RELEASE-PROCESS.md).
  *(H6 side built 2026-10-07, task 218b; the scope rule, git source and CI publish are tasks 218c–218e.)*

## Constraints

- **MUST** version-bump solutions before export
- **MUST** import customer environments through provisioning H6 (the one package, typed per run); never by hand *(amended 2026-10-07 — replaces "MUST use `Deploy-DataverseSolutions.ps1`")*
- **MUST NOT** make direct customizations in production
- **MUST NOT** commit binary solution ZIPs to git (commit the unpacked source; CI packs the zips)
- **MUST** deliver SpaarkeMaster **managed** to customer environments unless the run explicitly asks for unmanaged *(added 2026-10-07, D8)*
- **MUST NOT** ship environment-variable values in the package *(added 2026-10-07)*
- **MUST** use non-interactive, secret-free auth for CI/CD (OIDC / managed identity)
- **MUST** provision one Azure subscription per customer; **MUST NOT** put two customers in one
  subscription *(amended 2026-09-28 — replaces "**SHOULD** use separate subscriptions for dev vs
  production", which separated on the wrong axis)*
- **MUST** parameterize subscription ID in all deployment scripts, and use separate service principals per
  subscription
- **MUST** use Azure Management Groups for common policy across customer subscriptions *(raised from
  SHOULD 2026-09-28 — hand-application does not scale to one subscription per customer)*
- **SHOULD** run `pac solution check` before production import

**History**: the 2026-06-02 amendment removed the managed mandate; the 2026-10-07 amendment restores managed as the customer default (D8) with the explicit unmanaged exception above. An environment that already holds unmanaged SpaarkeMaster stays unmanaged unless an owner-approved migration says otherwise (H6 refuses a silent switch).

## Key Patterns

```powershell
# Release (authoring env -> git): export both types, unpack into source, no env-var values
./scripts/solution-authoring/Export-SpaarkeMasterSource.ps1      # task 218c
# CI packs managed + unmanaged from src/dataverse/solutions/SpaarkeMaster and publishes them (task 218d)
# A customer gets the package through provisioning: intake solutionPackageType = managed (default) | unmanaged
```

## Full ADR
See `docs/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md`
