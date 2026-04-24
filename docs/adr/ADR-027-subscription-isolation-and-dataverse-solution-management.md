# ADR-027: Subscription Isolation and Dataverse Solution Management

| Field | Value |
|-------|-------|
| **Status** | Proposed |
| **Date** | 2026-03-13 |
| **Decision Makers** | Ralph Schroeder |
| **Supersedes** | None |
| **Related** | ADR-001 (Minimal API), ADR-006 (PCF over webresources), ADR-026 (Full-Page Custom Page) |

---

## Context

Spaarke deploys a hybrid architecture with shared platform resources (BFF API, AI services) and per-customer isolated resources (storage, Key Vault, Service Bus, Redis, Dataverse environment). The initial production deployment (March 2026) placed all resources in a single Azure subscription with isolation via resource group naming. This ADR formalizes decisions around:

1. **Subscription strategy**: Should dev and production use separate Azure subscriptions?
2. **Resource group model**: How resource groups are created and named
3. **Dataverse solution management**: Managed vs unmanaged solutions, and the dev-to-production deployment pipeline
4. **Customer isolation**: Whether customers need their own subscriptions

---

## Decision 1: Subscription Isolation

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
3. **Tier 3 feature solutions** (any order) — AnalysisBuilder, CalendarSidePane, DocumentUploadWizard, EventCommands, EventDetailSidePane, EventsPage, LegalWorkspace, TodoDetailSidePane

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

- [Spaarke Deployment Guide](../../docs/guides/SPAARKE-DEPLOYMENT-GUIDE.md) — §3 Deployment Models, §7 Dataverse Solutions, §17-18 CI/CD
- [Customer Onboarding Runbook](../../docs/guides/CUSTOMER-ONBOARDING-RUNBOOK.md)
- [Azure Resource Naming Convention](../../docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md)
- [Microsoft Power Platform ALM Guide](https://learn.microsoft.com/en-us/power-platform/alm/)
- [PAC CLI Reference](https://learn.microsoft.com/en-us/power-platform/developer/cli/reference)
