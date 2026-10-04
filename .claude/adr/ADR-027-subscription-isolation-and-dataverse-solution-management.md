# ADR-027: Subscription Isolation and Dataverse Solution Management

| Field | Value |
|-------|-------|
| **Status** | Accepted (amended 2026-06-02, 2026-09-28) |
| **Date** | 2026-03-13 |
| **Accepted** | 2026-05-19 (in production: platform.bicep / customer.bicep split deployed) |
| **Amended** | 2026-06-02 — managed-solution prescriptions softened to "future direction"; current Spaarke practice is **unmanaged solutions in all environments** (dev, test, staging, prod). The managed-solution model remains the target long-term but is not enforced today. Amendment reflects actual practice confirmed by project owner.<br>2026-09-28 — **Decision 1 replaced**: one Azure subscription **per customer**, not per environment. Answers this ADR's own open Context question 4. Source: D-12 §3a (`projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md`). CLAUDE.md §6.5 path B. |

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

### 3. Dataverse Solutions

**Current practice (Phase 1, amended 2026-06-02)**:
- **All environments**: Unmanaged solutions. Managed adoption is deferred — practical Spaarke deployment uses unmanaged everywhere.
- Import order: SpaarkeCore → SpaarkeWebResources → Feature solutions (Tier 3)

**Target end-state (future direction, not yet adopted)**:
- **Dev**: Unmanaged (active development)
- **All other environments**: Managed (locked-down, clean uninstall) — when the org adopts managed-solution discipline
- Migration path: per-solution managed conversion + ALM workflow setup; tracked separately from this ADR

Use unmanaged solutions today; do not enforce managed in code review.

### 4. Dataverse CI/CD
- Phase 1 (now): Manual export, automated import via `Deploy-DataverseSolutions.ps1`
- Phase 2: GitHub Actions `deploy-dataverse.yml` with environment protection
- Phase 3: Automated export + solution checker pipeline

## Constraints

- **MUST** version-bump solutions before export
- **MUST** use `Deploy-DataverseSolutions.ps1` for imports (handles dependency order)
- **MUST NOT** make direct customizations in production
- **MUST NOT** commit binary solution ZIPs to git (use artifacts)
- **MUST** use service principal auth for CI/CD (not interactive)
- **MUST** provision one Azure subscription per customer; **MUST NOT** put two customers in one
  subscription *(amended 2026-09-28 — replaces "**SHOULD** use separate subscriptions for dev vs
  production", which separated on the wrong axis)*
- **MUST** parameterize subscription ID in all deployment scripts, and use separate service principals per
  subscription
- **MUST** use Azure Management Groups for common policy across customer subscriptions *(raised from
  SHOULD 2026-09-28 — hand-application does not scale to one subscription per customer)*
- **SHOULD** run `pac solution check` before production import

**Amended 2026-06-02 — removed**: managed-solution mandates ("MUST use managed solutions for all non-dev environments"; "MUST back up environment before first unmanaged→managed migration"). Spaarke practice is unmanaged-everywhere today; these constraints are reserved for a future managed-adoption ADR.

## Key Patterns

```powershell
# Export unmanaged solution from dev (current Spaarke practice 2026-06-02)
pac solution export --name SpaarkeCore --managed false --path ./exports/

# Import to any environment (handles dependency order)
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-{env}.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..."

# Version bump before export
pac solution version --solution-name SpaarkeCore --strategy solution --value 1.2.0.0

# (Future state — when managed adoption lands, add `--managed true` to the export)
```

## Full ADR
See `docs/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md`
