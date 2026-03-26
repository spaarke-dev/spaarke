# Current Task State - Demo Environment Deployment

> **Last Updated**: 2026-03-26 03:05 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Deploy Spaarke to spaarke-demo environment |
| **Step** | DEPLOYMENT COMPLETE — remaining: model-driven app, PCF migration, cleanup |
| **Status** | complete (core deployment) |
| **Next Action** | 1. Create model-driven app in demo. 2. Migrate 3 PCF controls to env var resolution. 3. Clean up old R1 resources. 4. Rotate BFF API secret. |

### Files Modified This Session
- `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` — Updated to v3 (per-env model, subscriptions, SPE naming)
- `scripts/Audit-DataverseComponents.ps1` — NEW: Discovers all sprk_ customizations, compares against solutions
- `scripts/Find-StaleFormParams.ps1` — NEW: Finds/fixes stale PCF static params in forms
- `scripts/Fix-DocumentFormParams.ps1` — NEW: Targeted Document form fix
- `scripts/Fix-AllStaleForms.ps1` — NEW: Batch fix all forms
- `logs/dataverse-audit-20260325-120223.md` — Audit report (372 components)
- `exports/SpaarkeCore_fixed.zip` — Fixed solution ZIP (stale params + manifests + empty sitemaps removed)
- `exports/SpaarkeCore_v2_fixed.zip` — v2 with app module (failed - custom page deps)
- `memory/demo-environment-deployment.md` — Session memory

### Critical Context
All Azure infrastructure, Dataverse solutions, Entra ID apps, Key Vault secrets, and env vars are deployed. SPE container type created and registered but container creation returns 403 — likely needs token refresh after permission grants. Model-driven app depends on legacy custom pages that should be removed (moved to code pages per ADR-006). Documentation for SPE setup process needs to be written.

---

## Full State (Detailed)

### Azure Infrastructure — COMPLETE
- **Subscription**: `Spaarke Demo Environment` (2ff9ee48-6f1d-4664-865c-f11868dd1b50)
- **Resource Group**: `rg-spaarke-demo` (westus2)
- **Resources** (11 total, all v3 naming):
  - `spaarke-bff-demo` — App Service (.NET 8, B1 Linux), managed identity enabled
  - `spaarke-openai-demo` — Azure OpenAI (westus3), 3 models: gpt-4o (50TPM), gpt-4o-mini (50TPM), text-embedding-3-large (50TPM)
  - `spaarke-search-demo` — AI Search (standard, 1 replica)
  - `spaarke-docintel-demo` — Document Intelligence (S0)
  - `sprk-demo-kv` — Key Vault (RBAC, 13 secrets stored)
  - `spaarke-demo-sbus` — Service Bus (Standard, 5 queues)
  - `spaarke-demo-cache` — Redis (Basic C0)
  - `sprkdemosa` — Storage Account (Standard_LRS)
  - `spaarke-demo-insights` — App Insights
  - `spaarke-demo-logs` — Log Analytics (90-day retention)
  - `spaarke-demo-plan` — App Service Plan (B1)

### Entra ID App Registrations — COMPLETE
- **BFF API**: `Spaarke BFF API - Demo` (da03fe1a-4b1d-4297-a4ce-4b83cae498a9)
  - Secret: `(stored in sprk-demo-kv as BFF-API-ClientSecret)` (stored in KV)
  - Service Principal: e7c9e67f-89b5-465f-b3d2-b2385c245ec0
  - Graph permissions: Sites.Read.All, User.Read.All, FileStorageContainer.Selected, FileStorageContainer.ReadWrite.All + 2 more
  - SharePoint permissions: Container.Selected (19766c1b), Sites.ReadWrite.All
- **UI SPA**: `Spaarke UI - Demo` (8d356f5b-bf13-4ec0-91fd-638a93e70b20) — public client, no secret

### Key Vault Secrets (sprk-demo-kv) — COMPLETE
TenantId, BFF-API-ClientId, BFF-API-ClientSecret, BFF-API-Audience, Dataverse-ServiceUrl, ai-openai-endpoint, ai-openai-key, ai-docintel-endpoint, ai-docintel-key, ai-search-endpoint, ai-search-key, ServiceBus-ConnectionString, AppInsights-ConnectionString, SPE-ContainerTypeId

### Dataverse Solutions — COMPLETE
- **SpaarkeFeatures** (1.0.0.0) — 192 web resources — imported first (dependency)
- **SpaarkeCore** (1.1.0.0) — 259 components — imported second (entities, fields, option sets, env vars, security roles, sitemaps)
- Import required fixes: stale form XML params, SpeDocumentViewer manifest (required→false), empty sitemaps removed
- Model-driven app NOT imported — depends on legacy custom pages (should be removed per ADR-006)

### Dataverse Environment Variables — COMPLETE
- sprk_BffApiBaseUrl = https://spaarke-bff-demo.azurewebsites.net/api
- sprk_BffApiAppId = api://da03fe1a-4b1d-4297-a4ce-4b83cae498a9
- sprk_MsalClientId = 8d356f5b-bf13-4ec0-91fd-638a93e70b20
- sprk_TenantId = a221a95e-6abc-4434-aecc-e48338a1b2f2
- sprk_AzureOpenAiEndpoint = https://spaarke-openai-demo.openai.azure.com/
- sprk_ShareLinkBaseUrl = https://spaarke-bff-demo.azurewebsites.net/share
- sprk_SharePointEmbeddedContainerId = b!FzmtPrWQEEi1yPtUOXM4_h7X4udVbCVJgu1ClOi23elAbPdL3-EGQK-D8YZ9tcZp

### SPE (SharePoint Embedded) — COMPLETE
- **Container Type**: `Spaarke Demo Documents` (362f90b3-7b72-4ab1-bb4c-20a1399ca838)
  - Owner: da03fe1a-4b1d-4297-a4ce-4b83cae498a9 (Demo BFF API)
  - Classification: Standard
  - Billing: Linked to demo subscription, region westus (not westus2 — Syntex limitation)
  - Registration: Done via Graph API (full delegated + full appOnly)
- **Root Container**: `Demo Root Documents` — ACTIVE
  - Container ID: `b!FzmtPrWQEEi1yPtUOXM4_h7X4udVbCVJgu1ClOi23elAbPdL3-EGQK-D8YZ9tcZp`
  - Stored in Key Vault and Dataverse env var

### BFF API — COMPLETE AND HEALTHY
- App Service: `spaarke-bff-demo.azurewebsites.net`
- Code: Deployed (.NET 8, Release build, zip deploy)
- App settings: 44 settings configured with Key Vault references to `sprk-demo-kv`
- CORS: Overridden to HTTPS-only (`spaarke-demo.crm.dynamics.com`)
- Dataverse app user: Created (da03fe1a, System Administrator role)
- Health: `/healthz` → `Healthy` (HTTP 200), `/ping` → `pong`
- **All startup errors resolved** (6 total — see DEPLOYMENT-ASSESSMENT.md for full sequence)
- Key missing setting was `AzureOpenAI__ChatModelName=gpt-4o` — without it, `IChatClient` was never registered in DI

### Remaining Work (Post-Deployment)
1. **Model-driven app** — Create Corporate Counsel app in demo (canvas app deps removed, but app module wasn't in solution)
2. **PCF control migration** — Migrate SpeDocumentViewer, EmailProcessingMonitor, UpdateRelatedButton to env var resolution
3. **Rotate BFF API secret** — Secret was exposed during session; generate new one and update Key Vault
4. **Clean up old R1 resources** — Delete `rg-spaarke-demo-prod` (old naming)
5. **Clean up legacy canvas apps** — Remove 6 canvas apps from dev (replaced by code pages per ADR-006)
6. **Create automated export script** — `Export-SpaarkeCoreSolution.ps1` with fix pipeline built in
7. **Document incremental update process** — How to push dev→demo updates for Dataverse solutions + BFF API

### Issues Discovered During This Session (Document These)
1. **PCF manifest mismatch** — R2 migration removed tenantId/apiBaseUrl from runtime code but not from manifests. Forms still had stale static values. Fix: remove from form XML in exported solution ZIP.
2. **SpeDocumentViewer not migrated** — Still has tenantId/bffApiUrl as required="true" with hardcoded dev URL. Needs R2-style migration to env var resolution.
3. **EmailProcessingMonitor not migrated** — Same issue, hardcoded dev URL.
4. **UpdateRelatedButton not migrated** — Uses apiBaseUrl directly without env var fallback.
5. **Empty sitemaps** — DocumentManagement and LawFirmCaseManagement sitemaps had zero areas/groups, causing import failure.
6. **Solution import order** — SpaarkeFeatures (web resources) MUST be imported BEFORE SpaarkeCore (entities reference web resources in forms/ribbons).
7. **Dataverse max upload size** — Default 5MB too small for PCF control bundles. Increased to 32MB.
8. **SPE container type creation** — Requires New-SPOContainerType (SPO Management Shell), NOT Graph API. Graph API returns 403 even with all permissions.
9. **SPE billing** — Separate step via Add-SPOContainerTypeBilling. Syntex must be registered on subscription. Region must be Syntex-compatible (westus, not westus2).
10. **Custom pages legacy** — 6 canvas apps in dev should be removed, replaced by code pages per ADR-006.
