# ADR-027: Subscription Isolation and Dataverse Solution Management

| Field | Value |
|-------|-------|
| **Status** | Proposed |
| **Date** | 2026-03-13 |

## Decisions

### 1. Subscription Isolation
- **Target**: Environment-separated subscriptions (Dev subscription + Production subscription)
- **Current**: Single subscription with resource group isolation (acceptable for initial deployment)

### 2. Resource Groups
- Shared platform: `rg-spaarke-platform-{env}` — created by `platform.bicep`
- Per-customer: `rg-spaarke-{customerId}-{env}` — created by `customer.bicep`
- Both use `targetScope = 'subscription'` (declarative RG creation)

### 3. Dataverse Solutions
- **Dev**: Unmanaged (active development)
- **All other environments**: Managed (locked-down, clean uninstall)
- Import order: SpaarkeCore → SpaarkeWebResources → Feature solutions (Tier 3)

### 4. Dataverse CI/CD
- Phase 1 (now): Manual export, automated import via `Deploy-DataverseSolutions.ps1`
- Phase 2: GitHub Actions `deploy-dataverse.yml` with environment protection
- Phase 3: Automated export + solution checker pipeline

## Constraints

- **MUST** use managed solutions for all non-dev environments
- **MUST** version-bump solutions before export
- **MUST** use `Deploy-DataverseSolutions.ps1` for imports (handles dependency order)
- **MUST NOT** make direct customizations in production
- **MUST NOT** commit binary solution ZIPs to git (use artifacts)
- **MUST** back up environment before first unmanaged→managed migration
- **MUST** use service principal auth for CI/CD (not interactive)
- **SHOULD** use separate subscriptions for dev vs production
- **SHOULD** run `pac solution check` before production import

## Key Patterns

```powershell
# Export managed solution from dev
pac solution export --name SpaarkeCore --managed true --path ./exports/

# Import to production (handles dependency order)
.\scripts\Deploy-DataverseSolutions.ps1 `
    -EnvironmentUrl "https://spaarke-prod.crm.dynamics.com" `
    -TenantId "..." -ClientId "..." -ClientSecret "..."

# Version bump before export
pac solution version --solution-name SpaarkeCore --strategy solution --value 1.2.0.0
```

## Full ADR
See `docs/adr/ADR-027-subscription-isolation-and-dataverse-solution-management.md`
