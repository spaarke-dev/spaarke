# Current Task - Spaarke AI Platform Unification R2

> **Project**: spaarke-ai-platform-unification-r2
> **Status**: deployed-testing
> **Active Wave**: Post-deployment testing — bug fixes
> **Last Updated**: 2026-05-18

## Quick Recovery

**Next Action**: Deploy SprkChat 401 fix to dev + re-verify chat session/playbook calls
**Last Checkpoint**: 401 auth fix applied to SprkChat hooks (uncommitted, builds clean)
**Context**: 86/86 tasks complete. Deployed to dev. Functional bugs being triaged.
**Branch**: work/spaarke-ai-platform-unification-r2 (pushed to origin at 24b78f65)

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

### In Progress (2026-05-18 — uncommitted)
- **401 on POST /api/ai/chat/sessions and GET /api/ai/chat/playbooks** — fix applied, awaiting commit + deploy.
  - **Root cause (CONFIRMED via App Insights)**: Expired access token. `CopilotAuth` warning trace at `2026-05-18T23:17:26Z` shows `IDX10223: Lifetime validation failed. The token is expired. ValidTo (UTC): '5/18/2026 3:29:02 PM', Current time (UTC): '5/18/2026 11:17:26 PM'` — token had been expired for nearly 8 hours when the request arrived. The page had been idle long enough for the cached token to expire, and SprkChat's hooks (`useChatSession`, `useChatPlaybooks`, `useChatContextMapping`) used raw `fetch()` with the `accessToken` React-prop captured at render time — no path to refresh on 401, no 401 retry. BFF auth config was fine all along (audience `api://1e40baad-...` and tenant `a221a95e-...` both match the token's claims).
  - **Why the auth provider didn't refresh proactively**: `useAiSession().token` exposes whatever token was cached when the AuthProvider initialised. React state doesn't actively poll for expiry — the stale token sits in the prop until something forces a refresh. `@spaarke/auth`'s `authenticatedFetch` is what forces it (calls `provider.getAccessToken()` per-request + clears cache + retries 3× on 401).
  - **Fix**: Added optional `authenticatedFetch?: AuthenticatedFetchFn` prop to `ISprkChatProps` and to all three SprkChat hooks. When provided (R2 Code Page path), hooks route through it — gaining silent token refresh and 401 retry with exponential backoff. When omitted, falls back to the existing raw fetch + accessToken (PCF / legacy path — backward compatible). `ConversationPane` imports `authenticatedFetch` from `@spaarke/auth` and passes it to SprkChat.
  - **Files changed**: `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/{types.ts,SprkChat.tsx,hooks/useChatSession.ts,hooks/useChatPlaybooks.ts,hooks/useChatContextMapping.ts}`, `src/solutions/SpaarkeAi/src/components/conversation/ConversationPane.tsx`.
  - **Verification**: `tsc --noEmit` clean for the change. `vite build` of SpaarkeAi succeeded (2720 modules, 9.88s). Jest tests not runnable in this worktree (pre-existing `react` peer-dep install issue, not introduced by this fix).
  - **Post-deploy test**: Open SpaarkeAi, leave the page idle for >80 min (or close laptop overnight), return and try chat. Should succeed instead of 401.

### Security finding (separate from 401 issue) — 2026-05-18
- **`AzureAd__ClientSecret` and `AgentToken__ClientSecret` stored as plain values in App Service config** (resource `spe-api-dev-67e2xz`, RG `spe-infrastructure-westus2`). Plain-text secret values visible in `az webapp config appsettings list` output (redacted from this note). Should be rotated and moved to Key Vault references (`@Microsoft.KeyVault(SecretUri=...)`). The deployment template already uses Key Vault for `Dataverse:ClientSecret` — same pattern should apply here. **Action required**: rotate secret as part of v2 Workstream C1; the prior value was exposed in tooling output and must be treated as compromised.

### Known Remaining Issues (for next session)
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
