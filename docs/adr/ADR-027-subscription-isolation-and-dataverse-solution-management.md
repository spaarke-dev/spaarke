# ADR-027: Subscription Isolation and Dataverse Solution Management

| Field | Value |
|-------|-------|
| **Status** | Accepted (amended 2026-06-02, 2026-09-28) |
| **Date** | 2026-03-13 |
| **Amended** | 2026-06-02 — managed-solution prescriptions softened to "future direction" (see amendment block below)<br>2026-09-28 — **Decision 1 replaced**: subscriptions are now separated **per customer**, not per environment. Answers this ADR's own open Context question 4. See the amendment block below. |
| **Decision Makers** | Ralph Schroeder |
| **Supersedes** | None |
| **Related** | ADR-001 (Minimal API), ADR-006 (PCF over webresources), ADR-026 (Full-Page Custom Page) |

---

## 🟡 AMENDMENT 2026-09-28 — Subscription strategy is now PER-CUSTOMER (supersedes Decision 1)

> **Path**: CLAUDE.md §6.5 **path B** (ADR amendment — context changed; the prior decision is no longer
> correct as written).
> **Source decision**: D-12, `projects/unified-access-control-r2/notes/D-12-deployment-model-redefinition.md`
> §3a, decided by the owner 2026-09-28.
> **Scope**: Decision 1 only. Decisions 2, 3 and 4 are unaffected except where Decision 2's resource-group
> table is re-read under the new subscription boundary (see "What Decision 2 now means", below).

### The amendment

**Every customer gets their own Azure subscription, containing their own resource group.** This replaces
"one production subscription holding all production shared and customer resources."

| | Before (2026-03-13) | After (2026-09-28) |
|---|---|---|
| Separation axis | **environment** (dev / prod) | **customer** (one subscription each) |
| Production customers | all in one production subscription | one subscription per customer |
| Per-customer subscriptions | *"**MAY** add … for enterprise customers … but this is NOT required"* | **MUST**, for every customer |

Applies to both tenancy models as redefined by D-12 — Model 1 (customer's Dataverse environment in
**Spaarke's** Azure tenant) and Model 2 (in the **customer's own** tenant). Under Model 1 the per-customer
subscription lives inside Spaarke's tenant; under Model 2, inside the customer's.

### Why the prior decision is no longer correct

**This ADR raised the question and never answered it.** §Context item 4 asks *"**Customer isolation**:
Whether customers need their own subscriptions"* — Decision 4 turned out to be about Dataverse CI/CD, so
item 4 was left open, and Decision 1's *"All production shared and customer resources"* became the de facto
answer by omission rather than by decision. This amendment answers it explicitly.

Three things changed since 2026-03-13 that make the answer "yes":

1. **Billing segregation became a requirement, not a preference.** The owner's stated need is to segregate
   and bill usage per customer. A subscription is Azure's billing boundary; resource-group tags are a
   reporting convenience layered on top of a shared invoice.
2. **Model 1 collapsed the `tenantId` isolation controls.** Under D-12, every Model 1 customer presents the
   **same** Entra `tenantId` (Spaarke's). Controls keyed on `tenantId` — the AI Search filter, the Cosmos
   partition key, the SPE container resolver, and the `tenant:{tenantId}:…` Redis cache key — therefore
   cannot separate customers, **and report success while failing**. Moving isolation from a query filter to
   a resource boundary is the fix; a subscription is the outermost such boundary.
3. **Azure OpenAI TPM quota is per-subscription-per-region.** Separate OpenAI *resources* inside one
   subscription still share one quota pool, so one customer's load throttles another's. Only subscription
   separation gives genuine quota isolation. This was not a consideration in March 2026 — the AI workload
   did not exist in its current form.

### Amended constraints (Decision 1)

- **MUST** provision one Azure subscription per customer, containing that customer's resource group.
- **MUST** parameterize subscription ID in all deployment scripts (unchanged, now load-bearing per customer
  rather than per environment).
- **MUST** use separate service principals per subscription (unchanged).
- **MUST** use Azure Management Groups to apply common policy across the now-many customer subscriptions.
  This was **SHOULD** before; with a subscription per customer, policy drift is no longer a theoretical risk
  and hand-application does not scale.
- **MUST NOT** place two customers' resources in one subscription.
- The former *"**MAY** add customer-specific subscriptions … but this is NOT required for initial
  customers"* is **withdrawn** — it is now the required model.
- Dev/test resources remain separated from production. Environment separation is not abandoned; it is now
  **subordinate** to customer separation rather than the top-level axis.

### What Decision 2 now means

Decision 2's per-customer pattern `rg-spaarke-{customerId}-{env}` is **unchanged and still correct** — but
that resource group now sits in the customer's **own** subscription rather than sharing one with every other
customer. `targetScope = 'subscription'` templates therefore run against a different subscription per
customer; the templates do not change, the target does.

⚠️ The `rg-spaarke-platform-{env}` **shared-platform** resource group in Decision 2 is a survivor of the
retired shared-tier model. D-12 §3 dedicates every Azure resource per customer, so the shared-platform group
is expected to shrink to genuinely cross-customer infrastructure or disappear entirely. That inventory is
tracked in D-12 §6 and `COMPONENT-INVENTORY.md` §7 — **not resolved by this amendment**.

### 🔴 Consequence: a dedicated App Service Plan per customer is FORCED

An App Service **app cannot use an App Service Plan in a different subscription**. With one subscription per
customer, a plan shared across customers would sit in one subscription while the apps sat in others — which
Azure does not support. The "shared plan, dedicated app per customer" shape is therefore **unavailable**,
not merely rejected on preference.

**Verification (2026-09-28)**: Microsoft's canonical pages do not state the subscription rule as a single
quotable sentence, so it was verified from two directions, and the result is **stricter** than "same
subscription":

- [Manage an App Service plan](https://learn.microsoft.com/en-us/azure/app-service/app-service-plan-manage#move-an-app-to-another-app-service-plan)
  — *"You can move an app to another App Service plan, as long as the source plan and the target plan are in
  the same resource group and geographical region and of the same OS type."* Plus a *webspace* constraint:
  *"your app can only move between plans that are created in the same webspace,"* where webspace is
  determined by resource group + region + OS. Same-subscription is implied by same-resource-group.
- [Microsoft Q&A — Share App Service plan across different subscriptions](https://learn.microsoft.com/en-us/answers/questions/1048743/share-app-service-plan-across-different-subscripti)
  — a single plan cannot be used across different subscriptions.

⚠️ **Note the asymmetry, so nobody re-derives a wrong conclusion from these sources.** The *move* restriction
(same **resource group**) is tighter than the *create*-time rule: at create time, apps in **different**
resource groups of the **same** subscription can share a plan. Both readings kill cross-**subscription**
sharing, which is the only thing this amendment depends on — but the resource-group clause is about moving
an existing app and must not be cited as the general rule.

**Cost impact**: a genuine App Service Plan floor per customer — the largest single fixed per-customer cost
in the stack. Accepted as the price of billing segregation and boundary-based isolation.

### Consequences of the amendment

**Positive**: billing separation is native, not reconstructed from tags · blast radius is one customer ·
per-customer RBAC · OpenAI quota isolation · customer-specific policy and compliance scoping · offboarding
is a subscription deletion.

**Negative**: an App Service Plan floor per customer · N subscriptions to create, govern and monitor ·
Management Groups become mandatory rather than advisory · cross-customer operational tooling must iterate
subscriptions · Azure subscription-count limits become a real ceiling to track as the customer base grows.

---

## 🟡 AMENDMENT 2026-06-02 — Read this BEFORE the body

**Spaarke does NOT currently use Power Platform managed solutions in any environment.** All Dataverse customizations are deployed as unmanaged solutions, even in test/staging/prod.

The body of this ADR (sections "Dataverse Solution Management Strategy", "Constraints", "Key Patterns", etc.) describes managed-solution adoption as the **target end-state** — a future direction that Spaarke intends to reach when ALM discipline justifies the additional process overhead. That target is not yet adopted in practice.

**Current operating rules (2026-06-02, supersedes the body's managed-solution prescriptions until further amendment)**:
- All environments (dev, test, staging, prod) use **unmanaged solutions**.
- `pac solution export --managed false` (not `--managed true`).
- The "MUST use managed solutions for all non-dev environments" constraint in the original body is **suspended**.
- The "MUST back up the target environment before first managed import (one-time migration risk)" constraint is **suspended**.
- Setting up a new Dataverse schema component (entity/attribute/relationship/option set) for an Insights Engine, AI feature, or other Spaarke domain → use one of: (a) unmanaged solution export + commit solution.xml, (b) idempotent PowerShell script using Dataverse Web API (the existing `scripts/Setup-*.ps1` and `scripts/Backfill-*.ps1` patterns), or (c) manual setup documented in project CLAUDE.md.

**Why amended now**: The original ADR was authored assuming managed adoption would land soon. As of 2026-06-02, managed adoption hasn't happened and isn't on the immediate roadmap. The Insights Engine r2 Wave B work surfaced the gap when the schema-portability question came up — the project owner clarified that unmanaged-everywhere is current practice, and the ADR was discovered to be enforcing a phantom mandate that nobody actually follows.

**Future amendment trigger**: When managed-solution adoption is formally scheduled (likely a new ADR or a substantive amendment to this one), this block will be removed and the body's prescriptions will be restored.

---

## Context

Spaarke deploys a hybrid architecture with shared platform resources (BFF API, AI services) and per-customer isolated resources (storage, Key Vault, Service Bus, Redis, Dataverse environment). The initial production deployment (March 2026) placed all resources in a single Azure subscription with isolation via resource group naming. This ADR formalizes decisions around:

1. **Subscription strategy**: Should dev and production use separate Azure subscriptions?
2. **Resource group model**: How resource groups are created and named
3. **Dataverse solution management**: Managed vs unmanaged solutions, and the dev-to-production deployment pipeline
4. **Customer isolation**: Whether customers need their own subscriptions — ✅ **ANSWERED 2026-09-28: yes.**
   Left open in the original ADR (Decision 4 addressed Dataverse CI/CD instead). See the 2026-09-28
   amendment block above.

---

## Decision 1: Subscription Isolation

> 🔴 **SUPERSEDED 2026-09-28 by the amendment block above — subscriptions are separated per CUSTOMER, not
> per environment.** The text below is retained for history. Do not implement from it.

### Decision

**Environment-separated subscriptions (Model B)** is the recommended target architecture:

- **Dev subscription**: All development and testing resources
- **Production subscription**: All production shared and customer resources

### Current State

Single subscription with resource group isolation (Model A). This is acceptable for initial deployment but should migrate to Model B before onboarding paying customers.

### Rationale

- **Blast radius**: A misconfigured script targeting the wrong resource group in a shared subscription can affect production
- **RBAC isolation**: Separate subscriptions allow restricting production access to operations roles only
- **Cost tracking**: Separate subscriptions provide cleaner billing separation
- **Compliance**: Some customer contracts may require dedicated subscription isolation

### Constraints

- **MUST** use separate service principals per subscription
- **MUST** parameterize subscription ID in all deployment scripts
- **MUST** use different GitHub Actions secrets for dev vs production environments
- **SHOULD** use Azure Management Groups to apply common policies across subscriptions
- **MAY** add customer-specific subscriptions (Model C) for enterprise customers requiring billing isolation or data sovereignty, but this is NOT required for initial customers

### Migration Impact

Minimal — all scripts already parameterize resource group names and environment names. Only changes needed:
- Add `-SubscriptionId` parameter to `Deploy-Platform.ps1` and `Provision-Customer.ps1`
- Add `az account set --subscription` at script entry points
- Create separate GitHub environment variables for each subscription

---

## Decision 2: Resource Group Model

### Decision

**Explicit resource groups per scope**, created automatically by subscription-scoped Bicep templates.

| Scope | Resource Group Pattern | Created By |
|-------|----------------------|------------|
| Shared platform | `rg-spaarke-platform-{env}` | `platform.bicep` (subscription-scoped) |
| Per-customer | `rg-spaarke-{customerId}-{env}` | `customer.bicep` (subscription-scoped) |

### Constraints

- **MUST** use `targetScope = 'subscription'` in Bicep templates so resource groups are created declaratively
- **MUST NOT** create resource groups manually — they are infrastructure-as-code artifacts
- **MUST** follow naming convention in `AZURE-RESOURCE-NAMING-CONVENTION.md` (Adopted v2.0)
- **MUST** tag all resource groups with `environment`, `project`, and `customer` tags for cost tracking
- Platform resource groups (`rg-spaarke-platform-*`) **MUST** be blocked from deletion by `Decommission-Customer.ps1`

---

## Decision 3: Dataverse Solution Management

### Decision

**Managed solutions for all non-dev environments.** Unmanaged solutions are only used in the dev environment where active development occurs.

| Environment | Solution Type | Rationale |
|-------------|--------------|-----------|
| Dev (`spaarkedev1`) | Unmanaged | Developers need to edit tables, forms, views directly |
| Staging/QA | Managed | Validates that managed import works before production |
| Demo-Production | Managed | Prevents ad-hoc changes, clean uninstall capability |
| Customer-Production | Managed | Clean lifecycle, version rollback, component ownership |

### Solution Dependency Order

All solutions **MUST** be imported in this order:

1. **SpaarkeCore** (Tier 1) — Base entities, option sets, security roles
2. **SpaarkeWebResources** (Tier 2) — JS files and icons used by feature solutions
3. **Tier 3 feature solutions** (any order) — AnalysisBuilder, CalendarSidePane, DocumentUploadWizard, EventCommands, EventDetailSidePane, EventsPage, LegalWorkspace

### Dev → Production Pipeline

```
Dev Environment (unmanaged)
  → pac solution export --managed true
  → Solution ZIP artifacts
  → Deploy-DataverseSolutions.ps1 --managed
  → Production Environment (managed)
```

### Constraints

- **MUST** export as managed for all non-dev environments
- **MUST** version-bump solutions before export (`pac solution version`)
- **MUST** use `Deploy-DataverseSolutions.ps1` for imports (handles dependency order)
- **MUST NOT** make direct customizations in production environments (all changes go through dev → export → import)
- **MUST** back up the target environment before first managed import (one-time migration risk)
- **SHOULD** test managed import in a staging/QA environment before production
- **SHOULD** automate export via GitHub Actions (see CI/CD section below)
- **MAY** adopt Solution Packager for source-control of schema changes when team grows

### Unmanaged-to-Managed Migration

For environments currently using unmanaged solutions:

1. Back up the environment (Power Platform Admin Center)
2. Identify overlapping unmanaged components (components that exist both in an unmanaged solution and will be in the managed import)
3. Remove conflicting unmanaged customizations
4. Import managed solutions in dependency order
5. Verify all components are functional

**Risk**: This is a one-time operation per environment. If unmanaged components overlap with managed solution components, the import will fail. Plan a maintenance window.

---

## Decision 4: Dataverse CI/CD

### Decision

**Phase the automation adoption**:

| Phase | What | Timeline |
|-------|------|----------|
| Phase 1 (Current) | Manual export from dev, automated import via `Deploy-DataverseSolutions.ps1` | Now |
| Phase 2 | GitHub Actions workflow for managed import (`deploy-dataverse.yml`) with environment protection | Near-term |
| Phase 3 | Automated export from dev + solution checker + import pipeline | Medium-term |
| Phase 4 | Solution Packager integration (unpack to source control) | Future |

### Phase 2 Implementation (Near-term)

Create `deploy-dataverse.yml` GitHub Actions workflow:
- **Trigger**: `workflow_dispatch` with target environment and solution selection
- **Authentication**: Service principal via PAC CLI (`pac auth create`)
- **Import**: `Deploy-DataverseSolutions.ps1` or Microsoft Power Platform Actions
- **Approval**: GitHub environment protection rules for production

### Constraints

- **MUST** use service principal authentication (not interactive user login) for CI/CD
- **MUST** require reviewer approval before production Dataverse imports
- **MUST** run `pac solution check` (solution checker) before production import
- **SHOULD** store solution ZIPs as GitHub release artifacts (not in repository — too large)
- **SHOULD NOT** commit binary solution ZIPs to git (use artifacts or Azure Blob storage)

---

## Consequences

### Positive

- Clear separation of development and production environments
- Clean uninstall capability for customer offboarding
- Version rollback for Dataverse solutions
- Auditable deployment pipeline
- No accidental production customizations

### Negative

- Cannot make quick fixes directly in production — all changes must flow through dev
- One-time migration effort to move existing environments to managed solutions
- Additional CI/CD pipeline complexity
- Solution export/import adds time to the deployment cycle (10-30 minutes per import)

### Risks

- Unmanaged-to-managed migration may surface hidden component conflicts
- Solution Packager drift: unpacked source files may not perfectly match the environment state
- PAC CLI service principal auth requires careful secret management

---

## References

- [Production Deployment Guide](../../docs/guides/PRODUCTION-DEPLOYMENT-GUIDE.md) — Sections 15-18
- [Customer Onboarding Runbook](../../docs/guides/CUSTOMER-ONBOARDING-RUNBOOK.md)
- [Azure Resource Naming Convention](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)
- [Microsoft Power Platform ALM Guide](https://learn.microsoft.com/en-us/power-platform/alm/)
- [PAC CLI Reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference)
