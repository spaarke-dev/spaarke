# Current Task - Spaarke AI Platform Unification R2

> **Project**: spaarke-ai-platform-unification-r2
> **Status**: deployed-testing
> **Active Wave**: Post-deployment testing
> **Last Updated**: 2026-05-18

## Quick Recovery

**Next Action**: Continue functional testing in deployed dev environment
**Last Checkpoint**: All code committed + pushed at 24b78f65
**Context**: 86/86 tasks complete. Deployed to dev. Testing in progress.
**Branch**: work/spaarke-ai-platform-unification-r2 (pushed to origin)

## Deployment Status

| Component | Status | Endpoint/Resource |
|-----------|--------|------------------|
| **Cosmos DB** | Deployed | `spe-cosmos-dev-ai` / `spaarke-ai` database / 5 containers |
| **BFF API** | Deployed + healthy | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| **SpaarkeAi web resource** | Deployed | `sprk_spaarkeai` (resource ID: 5206a442-3451-f111-bec7-7ced8d1dc988) |

## Issues Found During Testing

### Fixed (committed at 24b78f65)
1. **GET /api/ai/chat/sessions → 404**: Added list endpoint (returns empty — Cosmos list query TBD)
2. **DI scope mismatch (500.30)**: SpeFileStore (scoped) injected into WorkingDocumentService consumed by singletons → resolved via IServiceProvider at call time
3. **CapabilityManifest fast-fail (500.30)**: Startup crashed when Dataverse capability entities not provisioned → graceful degradation with empty manifest

### Fixed (committed at 39222103)
4. **Cosmos analyticalStorageTtl**: Not supported on serverless → removed from Bicep
5. **Cosmos publicNetworkAccess**: Was Disabled → changed to Enabled for dev
6. **Vite build failures**: Added source aliases for @spaarke/ai-widgets, ai-outputs, ai-context + deep import aliases
7. **DocumentCompare24Regular icon**: Doesn't exist → replaced with ColumnDoubleCompareRegular

### Fixed (committed at 2beddfe8)
8. **Pre-existing stubs replaced**: WorkingDocumentService SPE upload, CreateTaskNodeExecutor, OutputOrchestratorService, ScopeManagementService

### Known Remaining Issues (for next session)
- **401 on POST /api/ai/chat/sessions and GET /api/ai/chat/playbooks**: Auth token not being sent on some SprkChat requests. Investigate token acquisition flow in ConversationPane → SprkChat.
- **GET /api/ai/chat/sessions returns empty array**: Need to implement ListRecentSessionsAsync in SessionPersistenceService (Cosmos query by tenantId, ordered by lastActivity desc)
- **workspace/layouts endpoints 401**: These are separate workspace layout endpoints — may be pre-existing R1 endpoints with auth issues

## Deployment Lessons Learned (DOCUMENT IN FAILURE-MODES.md)
1. **DI scope validation**: Never constructor-inject scoped services into classes consumed transitively by singletons. Use IServiceProvider.GetService<T>() at call time.
2. **IHostedService startup**: Services loading from external data sources (Dataverse, APIs) should degrade gracefully, not fast-fail, in dev environments.
3. **Cosmos serverless**: analyticalStorageTtl is not supported — remove from Bicep for serverless accounts.
4. **Vite source aliases**: When workspace packages use deep imports (`@pkg/src/components/...`), add explicit `/src` aliases to prevent double `/src/src/` path resolution.
5. **Linux App Service cold start**: Use Deploy-BffApi.ps1 with 30+ retries × 10s intervals (300s total) for reliable health check.

## Git State
- Branch: `work/spaarke-ai-platform-unification-r2`
- Last commit: `24b78f65` (pushed to origin)
- Clean working tree (no uncommitted changes)

## On the Other Computer
```bash
git clone https://github.com/spaarke-dev/spaarke spaarke-wt-spaarke-ai-platform-unification-r2
cd spaarke-wt-spaarke-ai-platform-unification-r2
git checkout work/spaarke-ai-platform-unification-r2
# Then: "continue project spaarke-ai-platform-unification-r2"
```
