# ADR-027: Subscription Isolation and Dataverse Solution Management

| Field | Value |
|-------|-------|
| **Status** | Accepted (amended 2026-06-02) |
| **Date** | 2026-03-13 |
| **Accepted** | 2026-05-19 (in production: platform.bicep / customer.bicep split deployed) |
| **Amended** | 2026-06-02 — managed-solution prescriptions softened to "future direction"; current Spaarke practice is **unmanaged solutions in all environments** (dev, test, staging, prod). The managed-solution model remains the target long-term but is not enforced today. Amendment reflects actual practice confirmed by project owner. |

## Decisions

### 1. Subscription Isolation
- **Target**: Environment-separated subscriptions (Dev subscription + Production subscription)
- **Current**: Single subscription with resource group isolation (acceptable for initial deployment)

### 2. Resource Groups
- Shared platform: `rg-spaarke-platform-{env}` — created by `platform.bicep`
- Per-customer: `rg-spaarke-{customerId}-{env}` — created by `customer.bicep`
- Both use `targetScope = 'subscription'` (declarative RG creation)

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
- **SHOULD** use separate subscriptions for dev vs production
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
