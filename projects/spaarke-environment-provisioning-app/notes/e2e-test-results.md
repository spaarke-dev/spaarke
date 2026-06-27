# E2E Test Results — spaarke-environment-provisioning-app (Task 040)

> **Date**: 2026-06-12
> **Environment**: spaarkedev1.crm.dynamics.com (Dev) + spaarke-bff-dev App Service
> **Method**: Dataverse MCP (live queries), static code verification, Azure CLI config inspection, unit tests
> **Spec**: projects/spaarke-environment-provisioning-app/spec.md — 11 success criteria

## Summary

| # | Criterion | Result | Method |
|---|-----------|--------|--------|
| 1 | Environment record creatable — form layout + all fields | ✅ PASS | Entity deployed with all 16 custom columns (Dataverse MCP `describe`); 2 records exist, created via MDA form (commit d1450fc2 deployed form/views/sitemap) |
| 2 | Registration request lookup blank by default | ✅ PASS | `sprk_dataverseenvironmentid` lookup exists on `sprk_registrationrequest`; no default mechanism (no plugin/business rule); recent Submitted records have blank lookup |
| 3 | Form approve without environment → alert blocks | ✅ PASS (static) | `sprk_registrationribbon.js` FR-06 validation: `getAttribute("sprk_dataverseenvironmentid")` → `openAlertDialog("Please select a Target Environment before approving.")` and returns. Live UI click-through: see Manual items |
| 4 | Grid approve without environment → per-record error | ✅ PASS (static) | Ribbon JS grid path retrieves each record with `$expand=sprk_dataverseenvironmentid`; pushes `"{Name}: No target environment selected"` to skipped errors |
| 5 | Approve with environment → BFF provisions from Dataverse config | 🔶 MANUAL | Code path verified (approve endpoint reads `DataverseEnvironmentService.GetByIdAsync` fresh per NFR-01, maps to `DemoEnvironmentConfig`). Live run creates a real Entra user + licenses — needs operator sign-off. Two Submitted test requests exist in Dev |
| 6 | Deactivated environment excluded from lookup | ✅ PASS (server) / 🔶 MANUAL (UI) | Server: approve endpoint returns 400 "environment is inactive". UI lookup filtering by Active view: verify manually in MDA |
| 7 | Malformed license JSON → clear error | ✅ PASS | Endpoint catches `JsonException` → 400 ProblemDetails "Invalid license configuration for environment '{name}'". Unit-tested: `ParseLicenseConfig_MalformedJson_ThrowsJsonException` ✅ |
| 8 | Provision into Dev works after migration | 🔶 MANUAL | Same as #5 — requires live provisioning run |
| 9 | Provision into Demo 1 uses correct config | 🔶 MANUAL | Multi-URL cross-environment path implemented (per-URL token cache, commit a95865c2); live verification pending |
| 10 | Remove `DemoProvisioning__Environments__*` from Azure | ❌ BLOCKED | Settings still present on `spaarke-bff-dev` AND still **required**: `DemoExpirationService.ResolveDefaultEnvironment()` reads `_options.Environments`/`DefaultEnvironment` (marked `[Obsolete]`, migration deferred). Removing now would break expiration. → carry to r2 |
| 11 | Bulk grid approve — per-record lookup, individual failures | ✅ PASS (static) / 🔶 MANUAL (live) | Grid JS validates each record independently (criterion 4); live bulk run pending |

**BFF health**: `https://spaarke-bff-dev.azurewebsites.net/healthz` → 200 Healthy ✅

## Defects found & fixed during E2E

### D-040-01 — FR-11 per-environment licenses never used (FIXED)

The approve endpoint parsed `sprk_licenseconfigjson` (FR-12 validation) but discarded the
result; `GraphUserService.AssignLicensesAsync` always used global `DemoProvisioning:Licenses`
from appsettings. Per-environment license sets (FR-11 acceptance: "Different environments can
have different license SKU sets") were silently ignored.

**Fix** (this session):
- `DemoEnvironmentConfig.Licenses` (new optional property) — `Configuration/DemoProvisioningOptions.cs`
- Approve endpoint maps parsed `LicenseConfig` → `LicenseSkuConfig` — `Endpoints/RegistrationEndpoints.cs`
- `DemoProvisioningService` step 5 passes `environment.Licenses` — `Services/Registration/DemoProvisioningService.cs`
- `GraphUserService.AssignLicensesAsync(userId, environmentLicenses, ct)` + internal static
  `ResolveLicenses(global, perEnvironment)`: per-env wins when it has ≥1 non-empty SKU, else
  falls back to global (protects legacy/empty environment records) — `Services/Registration/GraphUserService.cs`
- Tests: `tests/unit/Sprk.Bff.Api.Tests/Services/Registration/LicenseResolutionTests.cs` — 6/6 pass

### D-040-02 — Seed records had empty license SKU IDs (FIXED)

Both `sprk_dataverseenvironment` records (Dev, Demo 1) had `sprk_licenseconfigjson` with empty
SKU values (FR-14 migration copied repo appsettings placeholders, not real Azure values).
Populated both records with actual SKU IDs from `spaarke-bff-dev` app settings
(PowerApps Plan 2 Trial `dcb1a3ae…`, Fabric Free `a403ebcc…`, Power Automate Free `f30db892…`).
Verified via read-back query.

## Carry-overs (input for r2 systematization)

1. **Criterion 10 blocked**: migrate `DemoExpirationService` off `[Obsolete]`
   `DemoProvisioningOptions.Environments`/`DefaultEnvironment` — it should resolve the
   environment per-request via the request's `sprk_dataverseenvironmentid` lookup, then the
   Azure `DemoProvisioning__Environments__*` + `__DefaultEnvironment` settings can be deleted.
2. **Doc drift**: `docs/architecture/auth-azure-resources.md` names App Service
   `spe-api-dev-67e2xz` / RG `spe-infrastructure-westus2`; actual dev BFF is
   `spaarke-bff-dev` / `rg-spaarke-dev` (prod: `spaarke-bff-prod` / `rg-spaarke-platform-prod`).
3. **Manual verification round** (criteria 5, 8, 9, 11 + UI part of 3, 4, 6): one live
   approve per environment using the existing Submitted test requests, plus a bulk grid
   approve. Requires operator (creates real Entra users/licenses; cleanup via expiration flow).

## Quality gates (Step 9.5)

- Build: `dotnet build Sprk.Bff.Api` — 0 errors ✅
- Unit tests: LicenseResolutionTests 6/6 ✅
- Publish size (NFR-01 / CLAUDE.md §10.4): **46.02 MB compressed** (139.23 MB uncompressed) vs
  ~45.65 MB baseline (2026-05-26) → +0.37 MB cumulative drift since baseline, no escalation
  threshold crossed (ceiling 60 MB; single-task +5 MB)
- ADR check: ADR-001 (no new endpoints), ADR-002 (no plugins), ADR-010 (no DI changes;
  concrete types unchanged), ADR-019 (existing ProblemDetails paths unchanged) ✅
- BFF placement (§10): modification of existing Registration services only — no new endpoints,
  packages, DI registrations, or background work; placement unchanged ✅
