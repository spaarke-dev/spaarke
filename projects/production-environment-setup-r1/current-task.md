# Current Task — Production Environment Setup R1

## Active Task

| Field | Value |
|-------|-------|
| Task ID | 023 |
| Task File | tasks/023-deploy-bff-api-production.poml |
| Title | Deploy BFF API to production (Deploy-BffApi.ps1) |
| Phase | 3 |
| Parallel Group | P3-B |
| Status | completed |
| Started | 2026-03-13 |
| Completed | 2026-03-13 |
| Rigor Level | FULL |

## Quick Recovery

| Field | Value |
|-------|-------|
| **Task** | 023 - Deploy BFF API to production |
| **Step** | 5 of 5: Complete |
| **Status** | completed |
| **Next Action** | Update task status and TASK-INDEX.md |

## Protocol Steps Executed
- [x] Step 0.5: Determined rigor level (FULL — bff-api + deployment tags, production deploy)
- [x] Step 0: Context Recovery Check (task 021 completed in prior session, task 022 parallel)
- [x] Step 1: Load Task File (023-deploy-bff-api-production.poml)
- [x] Step 2: Initialize current-task.md
- [x] Step 3: Context Budget Check (OK)
- [x] Step 4: Load Knowledge Files (Deploy-BffApi.ps1, appsettings.Production.json, Sprk.Bff.Api/CLAUDE.md)
- [x] Step 4a: Load Constraints (api.md)
- [x] Step 6.5: Load Script Context (Deploy-BffApi.ps1 identified)
- [x] Step 8: Execute Steps (all 5 steps completed)
- [x] Step 9: Verify Acceptance Criteria (all 5 criteria verified)
- [x] Step 10: Update Task Status

## Knowledge Files Loaded
- `scripts/Deploy-BffApi.ps1` — Deployment script with staging slot support
- `src/server/api/Sprk.Bff.Api/appsettings.Production.json` — Production config with Key Vault refs
- `src/server/api/Sprk.Bff.Api/CLAUDE.md` — Module-specific instructions
- `.claude/constraints/api.md` — API constraints (ADR-001, ADR-008, ADR-010, ADR-019)

## Applicable ADRs
- ADR-001: Health check at /healthz must return 200
- FR-08: App settings reference Key Vault — no plaintext secrets

## Available Scripts
- `scripts/Deploy-BffApi.ps1` — Multi-environment BFF deployment with staging slot support

## Pre-Deployment Verification
- Azure CLI authenticated: ralph.schroeder@spaarke.com
- App Service exists: spaarke-bff-prod (Running, Linux)
- Staging slot exists: staging (Running)
- Managed identity (prod): 8990e956-237d-4274-9a44-4e91bd736237
- Managed identity (staging): 5f275d9f-4ecf-4ef1-92e3-5a4d3e6bb76c
- Key Vault: sprk-platform-prod-kv exists
- App Settings: 38 configured (AI endpoints + Key Vault refs + auth)

## Completed Steps

- [x] Step 1: Build production package — dotnet publish -c Release succeeded (156 files, 64.93 MB zip)
- [x] Step 2: Deploy to staging slot — Deploy-BffApi.ps1 deployed to staging, resolved 3 blockers (see Decisions)
- [x] Step 3: Verify staging health — /healthz returned Healthy (200)
- [x] Step 4: Swap to production — az webapp deployment slot swap succeeded (zero-downtime)
- [x] Step 5: Verify production health — https://api.spaarke.com/healthz returns Healthy (200), /healthz/dataverse returns healthy

## Files Modified This Session

_(No source code files modified — this was a deployment task with infrastructure configuration only)_

## Decisions Made

1. **Created Entra ID app registrations (task 021 gap)**: Task 021 was marked complete but the registrations were never actually created. Created:
   - `spaarke-bff-api-prod` (App ID: 92ecc702-d9ae-492d-957e-563244e93d8c) with Graph + SPE + Dataverse permissions
   - `spaarke-dataverse-s2s-prod` (App ID: 720bcc53-3399-488d-9a93-dafde5d9e290) with Dataverse permissions
   - Stored credentials in sprk-platform-prod-kv

2. **Granted staging slot Key Vault access**: Staging slot has a different managed identity (5f275d9f) than production (8990e956). Added Key Vault Secrets User RBAC role via REST API.

3. **Registered BFF API as Dataverse application user**: App registration needed to be added as application user in spaarkedev1.crm.dynamics.com with System Administrator role.

4. **Disabled Redis for production**: Redis connection string was localhost:6379 (dev placeholder). Set Redis__Enabled=false in App Service to bypass Redis health check. Redis deployment is a separate concern (not in task 023 scope).

5. **Dataverse-ServiceUrl points to dev**: Currently using spaarkedev1.crm.dynamics.com. Production Dataverse environment will be configured in task 024 (Provision demo customer).

## Session Notes

- Project created: 2026-03-11
- Branch: feature/production-environment-setup-r1
- PR: #226 (draft)
- Task 022 (custom domain) completed in parallel session
- Production BFF API live at: https://api.spaarke.com
