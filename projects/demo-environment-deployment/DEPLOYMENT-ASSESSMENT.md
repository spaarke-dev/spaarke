# Demo Environment Deployment — Situation Assessment

> **Date**: 2026-03-25
> **Author**: Claude Code Session (collaborative with Ralph Schroeder)
> **Status**: In Progress — BFF API startup issue remaining
> **Environment**: spaarke-demo (https://spaarke-demo.crm.dynamics.com)

---

## Executive Summary

The Spaarke platform is being deployed to a new `spaarke-demo` environment for beta testers and demos. This is the first non-dev deployment and serves as validation for the deployment process. All infrastructure, Dataverse solutions, and SPE storage are deployed and operational. The BFF API is deployed but fails to start due to a Service Bus background worker crash. This is the last blocking issue.

---

## What Was Accomplished

### Azure Infrastructure (COMPLETE)

| Resource | Name | Status |
|----------|------|--------|
| Subscription | `Spaarke Demo Environment` (2ff9ee48-6f1d-4664-865c-f11868dd1b50) | Active, billed to Spaarke Inc. |
| Resource Group | `rg-spaarke-demo` | Created (westus2) |
| App Service Plan | `spaarke-demo-plan` | B1 Linux |
| App Service (BFF) | `spaarke-bff-demo` | Code deployed, managed identity enabled |
| Azure OpenAI | `spaarke-openai-demo` | 3 models (gpt-4o, gpt-4o-mini, embeddings) @ 50 TPM each |
| AI Search | `spaarke-search-demo` | Standard, 1 replica |
| Doc Intelligence | `spaarke-docintel-demo` | S0 |
| Key Vault | `sprk-demo-kv` | RBAC, 14 secrets |
| Service Bus | `spaarke-demo-sbus` | Standard, 5 queues |
| Redis | `spaarke-demo-cache` | Basic C0 |
| Storage | `sprkdemosa` | Standard_LRS |
| App Insights | `spaarke-demo-insights` | Connected to Log Analytics |
| Log Analytics | `spaarke-demo-logs` | 90-day retention |

### Entra ID (COMPLETE)

| App Registration | App ID | Purpose |
|-----------------|--------|---------|
| Spaarke BFF API - Demo | `da03fe1a-4b1d-4297-a4ce-4b83cae498a9` | BFF API auth, SPE owner |
| Spaarke UI - Demo | `8d356f5b-bf13-4ec0-91fd-638a93e70b20` | MSAL SPA client |

- BFF API has Graph + SharePoint permissions granted via appRoleAssignments
- Both apps have SPA redirect URIs for `spaarke-demo.crm.dynamics.com`
- Legacy dev app (`170c98e1`) also has demo redirect URIs (temporary, until PCF controls migrate)
- BFF API scope exposed: `api://da03fe1a-4b1d-4297-a4ce-4b83cae498a9/user_impersonation`

### Dataverse (COMPLETE)

| Solution | Version | Components | Status |
|----------|---------|------------|--------|
| SpaarkeFeatures | 1.0.0.0 | 192 web resources | Imported + published |
| SpaarkeCore | 1.1.0.0 | 259 (entities, fields, option sets, env vars, security roles, sitemaps) | Imported + published |

- 7 environment variables set
- Application user created for BFF API (System Administrator role)
- Max upload size increased to 32MB
- Model-driven app NOT imported (depends on legacy canvas apps — see "Remaining Work")

### SharePoint Embedded (COMPLETE)

| Item | Value |
|------|-------|
| Container Type | `Spaarke Demo Documents` (362f90b3-7b72-4ab1-bb4c-20a1399ca838) |
| Owner | `da03fe1a-4b1d-4297-a4ce-4b83cae498a9` (Demo BFF API) |
| Billing | Standard, linked to demo subscription, region westus |
| Registration | Full delegated + full appOnly via Graph API |
| Root Container | `Demo Root Documents` — active |
| Container ID | `b!FzmtPrWQEEi1yPtUOXM4_h7X4udVbCVJgu1ClOi23elAbPdL3-EGQK-D8YZ9tcZp` |

### BFF API (PARTIALLY COMPLETE)

- **Code**: Deployed (.NET 8, Release build, zip deploy)
- **App Settings**: 43+ settings configured with Key Vault references
- **Dataverse**: Application user created, connection works
- **Status**: FAILING TO START

---

## Current Blocker: BFF API Startup Crash

### Error Sequence (in order of discovery and resolution)

| # | Error | Fix Applied | Result |
|---|-------|-------------|--------|
| 1 | `SpeAdmin:KeyVaultUri configuration is required` | Added `KeyVaultUri` and `SpeAdmin__KeyVaultUri` app settings | Fixed |
| 2 | `CORS: Non-HTTPS origin not allowed in Production` | Overrode `Cors__AllowedOrigins__0-4` to HTTPS-only | Fixed |
| 3 | `The user is not a member of the organization` | Created Dataverse application user via PP Admin Center | Fixed |
| 4 | `ServiceBusOptions.QueueName is required` | Added `ServiceBus__QueueName=sdap-jobs` | Fixed |
| 5 | Service Bus AMQP CBS auth timeout → exit code 134 | **NOT YET FIXED** | App crashes |
| 6 | `Failure to infer one or more parameters` | **NOT YET FIXED** | DI issue after crash |

### Root Cause Analysis

The BFF API starts up successfully — Dataverse connects, circuit breakers initialize, DI configuration completes. However, the **background `ServiceBusProcessor`** attempts to connect to the Service Bus queue and fails with an AMQP CBS (Claims-Based Security) authorization timeout. This unhandled exception in the `BackgroundService` propagates up and crashes the entire .NET host process (exit code 134 = SIGABRT).

### Likely Causes

1. **Service Bus connection string SAS token**: The connection string uses `RootManageSharedAccessKey` which should have full access. Verified the connection string is correct and the queue exists.

2. **Unhandled BackgroundService exception**: .NET's default behavior for `BackgroundService` is to let unhandled exceptions crash the host (changed in .NET 8 to `StopHost`). The BFF API may not have exception handling in its Service Bus worker.

3. **Missing Service Bus configuration**: Other queue-specific settings (like `Communication__*` and `Email__*` settings) are set to placeholders. Background workers may try to connect to non-existent queues or use invalid configuration.

### Recommended Investigation Steps

1. **Check `Program.cs`** — Find how Service Bus workers are registered. Look for:
   - `AddHostedService<>` or `AddServiceBusProcessor` calls
   - Any feature flag that disables background workers (e.g., `BackgroundServices:Enabled`)
   - Multiple queue processors that each need their own queue name

2. **Check `ServiceBusOptions`** — What fields are validated? May need more than just `QueueName`.

3. **Check the background worker code** — Find the `BackgroundService` implementations that process Service Bus messages. Look for missing error handling.

4. **Possible quick fix**: Add `BackgroundServices:Enabled=false` if the code checks this flag, OR set `ConnectionStrings__ServiceBus` to empty string to prevent connection.

5. **Possible code fix**: Wrap the ServiceBus processor in a try/catch that logs errors instead of crashing. This is a code change that requires rebuild + redeploy.

---

## Remaining Work

### Priority 1: BFF API Startup (Blocking)

| Task | Effort | Approach |
|------|--------|----------|
| Investigate Program.cs Service Bus registration | 30 min | Read code, find feature flags |
| Fix or disable background workers for demo | 30 min | Config setting or code change |
| Rebuild + redeploy if code change needed | 15 min | `dotnet publish` + `az webapp deploy` |
| Verify `/healthz` responds | 5 min | `curl https://spaarke-bff-demo.azurewebsites.net/healthz` |

### Priority 2: Model-Driven App

| Task | Effort | Approach |
|------|--------|----------|
| Create Corporate Counsel app in demo manually | 30 min | Power Apps Maker UI |
| OR: Remove canvas app dependencies from dev app, re-export | 1 hr | Clean dev → export → import |

### Priority 3: PCF Control Migration Cleanup

| Task | Controls | Effort |
|------|----------|--------|
| Remove hardcoded dev app ID (`170c98e1`) from `config.ts` | Already done | — |
| Migrate SpeDocumentViewer to env var resolution | 1 control | 2 hrs |
| Migrate EmailProcessingMonitor to env var resolution | 1 control | 2 hrs |
| Migrate UpdateRelatedButton to env var resolution | 1 control | 1 hr |
| Rebuild all PCF controls and deploy to dev + demo | All | 1 hr |

### Priority 4: Documentation & Process

| Task | Effort |
|------|--------|
| Review/finalize ENVIRONMENT-DEPLOYMENT-GUIDE.md | 30 min |
| Update HOW-TO-SETUP-CONTAINERTYPES-AND-CONTAINERS.md with SPO commands | 30 min |
| Create automated Export-SpaarkeCoreSolution.ps1 with fix pipeline built in | 1 hr |
| Document release/update process (dev → demo incremental updates) | 1 hr |

### Priority 5: Cleanup

| Task | Effort |
|------|--------|
| Delete old R1 resources (`rg-spaarke-demo-prod`) | 5 min |
| Remove 6 legacy canvas apps from dev | 15 min |
| Remove empty sitemaps from dev | 5 min |
| Clean up stale form XML in dev (tenantId/apiBaseUrl static values) | 15 min (script exists) |

---

## Key Artifacts Created

| Artifact | Path | Purpose |
|----------|------|---------|
| Naming Convention v3 | `docs/architecture/AZURE-RESOURCE-NAMING-CONVENTION.md` | Per-environment naming standard |
| Deployment Guide | `docs/guides/ENVIRONMENT-DEPLOYMENT-GUIDE.md` | Repeatable deployment process |
| Dataverse Audit Script | `scripts/Audit-DataverseComponents.ps1` | Discover all sprk_ components |
| Form Fix Script | `scripts/Fix-AllStaleForms.ps1` | Remove stale PCF static params |
| Document Form Fix | `scripts/Fix-DocumentFormParams.ps1` | Targeted Document form fix |
| Stale Param Finder | `scripts/Find-StaleFormParams.ps1` | Find stale params across all forms |
| Audit Report | `logs/dataverse-audit-20260325-120223.md` | Baseline audit (372 components) |
| Solution Exports | `exports/SpaarkeCore_fixed.zip`, `exports/SpaarkeFeatures.zip` | Deployable solution ZIPs |
| Session Memory | `memory/demo-environment-deployment.md` | Auto-memory for future sessions |
| This Document | `projects/demo-environment-deployment/DEPLOYMENT-ASSESSMENT.md` | Situation assessment |
| Task State | `projects/demo-environment-deployment/current-task.md` | Recovery checkpoint |

---

## Key Values Reference (Demo Environment)

| Item | Value |
|------|-------|
| **Subscription** | `Spaarke Demo Environment` / `2ff9ee48-6f1d-4664-865c-f11868dd1b50` |
| **Resource Group** | `rg-spaarke-demo` |
| **Dataverse URL** | `https://spaarke-demo.crm.dynamics.com` |
| **BFF API URL** | `https://spaarke-bff-demo.azurewebsites.net` |
| **BFF API App ID** | `da03fe1a-4b1d-4297-a4ce-4b83cae498a9` |
| **BFF API Secret** | `(stored in sprk-demo-kv as BFF-API-ClientSecret)` (in KV) |
| **BFF API SP ID** | `e7c9e67f-89b5-465f-b3d2-b2385c245ec0` |
| **BFF API Managed Identity** | `e9cf6ee8-ead4-47be-85bf-71b3a588316e` |
| **UI SPA App ID** | `8d356f5b-bf13-4ec0-91fd-638a93e70b20` |
| **Key Vault** | `sprk-demo-kv` / `https://sprk-demo-kv.vault.azure.net/` |
| **OpenAI Endpoint** | `https://spaarke-openai-demo.openai.azure.com/` |
| **SPE Container Type** | `362f90b3-7b72-4ab1-bb4c-20a1399ca838` |
| **SPE Root Container** | `b!FzmtPrWQEEi1yPtUOXM4_h7X4udVbCVJgu1ClOi23elAbPdL3-EGQK-D8YZ9tcZp` |
| **PAC CLI Profile (demo)** | Index 2 |
| **PAC CLI Profile (dev)** | Index 3 |

---

## Lessons Learned

1. **Solution export needs a fix pipeline** — Cannot import raw exports from dev to new environments. Stale form XML, manifest mismatches, empty sitemaps, and canvas app dependencies must be stripped. This should be automated.

2. **SPE container type creation requires SPO Management Shell** — The Graph API doesn't work for creation despite having all permissions. The `New-SPOContainerType` cmdlet is the only reliable path.

3. **Syntex billing region matters** — `westus2` is NOT supported. Use `westus` for the billing linkage.

4. **Permission propagation takes time** — Newly granted Graph permissions don't appear in client credentials tokens immediately. Wait 10-30 minutes, or force a new token by generating a new client secret.

5. **CORS array merging** — App Service environment variable array indices MERGE with `appsettings.json` arrays, they don't REPLACE. Must override all indices to prevent localhost origins from leaking through.

6. **SpeAdmin:KeyVaultUri** — Undocumented required setting that's not in the prod reference. Causes startup crash without a clear error unless verbose logging is enabled.

7. **Dataverse application user** — Must be created via Power Platform Admin Center (not API or CLI). Without it, the Dataverse ServiceClient connection fails at startup.

8. **`config.ts` dev fallbacks** — The shared auth library had hardcoded dev app IDs as fallback defaults. These should throw errors in non-dev environments (fixed during this session).

9. **Import order matters** — SpaarkeFeatures (web resources) before SpaarkeCore (entities). Entities reference web resources in forms/ribbons.

10. **B1 cold start is slow** — .NET 8 apps on B1 Linux take 60-120 seconds to cold start. Health check timeouts during deployment monitoring are expected.

---

## How to Continue This Work

### Resume Command
Say: **"continue with demo deployment"**

### What Claude Code Will Do
1. Read `projects/demo-environment-deployment/current-task.md` for full state
2. Load `memory/demo-environment-deployment.md` for context
3. Pick up at: BFF API Service Bus startup investigation

### Specific Next Step
1. Read `src/server/api/Sprk.Bff.Api/Program.cs` to find Service Bus worker registration
2. Find the feature flag or configuration to disable background workers
3. Apply fix (config or code change)
4. Rebuild + redeploy if needed
5. Verify `/healthz` responds

---

*Assessment created 2026-03-25. Last blocker: BFF API Service Bus background worker crash on startup.*
