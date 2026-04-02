# Current Task State - M365 Copilot Integration (R1)

> **Last Updated**: 2026-04-02 03:15 (by context-handoff)
> **Recovery**: Read "Quick Recovery" section first

---

## Quick Recovery (READ THIS FIRST)

| Field | Value |
|-------|-------|
| **Task** | Auth Resolution + API Integration Fixes |
| **Step** | Auth SOLVED. API spec fixes need verification. |
| **Status** | in-progress |
| **Next Action** | Fix remaining API integration issues: (1) OpenAPI spec params vs actual endpoint params, (2) user context scoping on events, (3) confirmation cards on read ops, (4) verify search endpoints work. Start new session fresh with these as discrete tasks. |

### Files Modified This Session
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/AuthorizationModule.cs` - PostConfigure for Copilot audience + auth failure logging
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/MiddlewarePipelineExtensions.cs` - Diagnostic middleware logging auth headers before JWT validation
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` - Added AgentToken.CopilotAudience
- `src/solutions/CopilotAgent/spaarke-api-plugin.json` - Changed auth None→OAuthPluginVault, updated reference_id to OAuth registration
- `src/solutions/CopilotAgent/spaarke-bff-openapi.yaml` - Fixed SearchRequest (added scope), RecordSearchRequest (entityTypes→recordTypes), events params (statusCode/dueDateFrom/dueDateTo), added securitySchemes for OAuth
- `src/solutions/CopilotAgent/appPackage/manifest.json` - Currently devPreview, removed webApplicationInfo (testing), restored it
- `src/solutions/CopilotAgent/declarativeAgent.json` - Rewrote to remove BOM + mojibake, updated instructions for correct API usage
- `scripts/Deploy-CopilotAgent.ps1` - Fixed BOM issue (UTF8Encoding without BOM)

### Critical Context

**AUTH IS RESOLVED** — The full OAuth flow works:
1. Copilot shows "Allow" consent card → user clicks Allow
2. Sign-in popup appears → user authenticates
3. Copilot sends Bearer token with `scp=access_as_user`, `aud=api://1e40baad-...`
4. BFF accepts token via PostConfigure<JwtBearerOptions> with CopilotAudience

**Key auth setup (DO NOT CHANGE):**
- OAuth client registration in Teams Dev Portal (NOT Entra SSO — SSO didn't work)
- Plugin manifest: `auth.type: "OAuthPluginVault"`, `reference_id: "YTIyMWE5NWUtNmFiYy00NDM0LWFlY2MtZTQ4MzM4YTFiMmYyIyMwNmViNWJjMC0wMTE4LTQ3OTgtODlkMS05MzBkNTNlYmFkNmE="`
- Entra app (1e40baad): has `access_as_user` scope, enterprise token store (ab3be6b7) pre-authorized, redirect URI `https://teams.microsoft.com/api/platform/v1.0/oAuthRedirect`
- BFF accepts both `api://1e40baad-...` and `api://auth-3e04ab58-...` audiences

**REMAINING ISSUES (not auth):**

1. **OpenAPI spec mismatches causing 400s on search**:
   - `POST /api/ai/search` requires `scope: "all"` field (REQUIRED, was missing from original spec, now added)
   - `POST /api/ai/search/records` requires `recordTypes` not `entityTypes` (fixed in spec)
   - But Copilot sometimes ignores spec and makes up params — may need function descriptions to guide it

2. **Events endpoint has NO user filtering**:
   - `GET /api/v1/events` uses app-only Dataverse auth (ClientSecretCredential)
   - No OBO, no user impersonation, no OID extraction from token
   - Returns ALL events or none — not scoped to current user
   - EventEntity has no OwnerId/AssignedTo field
   - NEEDS: Add user context extraction + Dataverse user filtering (separate task)

3. **Confirmation cards on every read request**:
   - Added `capabilities.confirmation.type: "None"` to read functions but had to strip it (schema validation issue with v2.2)
   - Need to verify correct schema version that supports this

4. **Copilot sometimes uses native Dataverse instead of API plugin**:
   - Agent instructions say to always use plugin functions
   - But Copilot may query Task/Activity entities directly (empty, since Spaarke uses sprk_event)

5. **Teams app package upload sensitivity**:
   - BOM in JSON files causes "can't read manifest" error
   - devPreview manifest works for upload; v1.24 was rejected on fresh upload
   - Deploy script now writes UTF-8 without BOM
   - declarativeAgent.json was rewritten clean (had BOM + mojibake from PowerShell ConvertTo-Json)

---

## Full State (Detailed)

### Project Progress: 23/31 Tasks Complete (74%)

**Completed Tasks**: 001, 002, 006-008, 010-014, 016-023, 024-029
**Remaining Tasks**: 003-005 (spikes), 009 (validation), 015 (integration tests), 030 (E2E), 031 (wrap-up)

### Infrastructure Deployed

| Component | Status | Details |
|-----------|--------|---------|
| BFF API | Live | https://spe-api-dev-67e2xz.azurewebsites.net (with Copilot audience + diagnostic middleware) |
| Bot Service | Live | spaarke-bot-dev |
| Entra App (Bot) | Registered | f257a0a9-1061-4f9b-8918-3ad056fe90db |
| Entra App (BFF) | Configured | 1e40baad-e065-4aea-a8d4-4b7ab273458c (access_as_user scope + enterprise token store) |
| Teams App | Uploaded v1.1.0 | Spaarke AI in org app catalog |
| OAuth Registration | Active | Teams Dev Portal, OAuth client registration |
| App Service Config | Set | AgentToken__CopilotAudience configured |

### Key Entra App Configuration

BFF App (1e40baad-e065-4aea-a8d4-4b7ab273458c):
- identifierUris: `api://1e40baad-...` AND `api://auth-3e04ab58-.../1e40baad-...`
- Scopes: `user_impersonation`, `SDAP.Access`, `access_as_external_user`, `access_as_user`
- Authorized clients: enterprise token store (ab3be6b7), Bot app (f257a0a9), MDA app (170c98e1), others
- Redirect URIs: `oAuthRedirect` AND `oAuthConsentRedirect` (both Teams URLs)

### Git State

Branch: work/ai-m365-copilot-integration-r1
Commits this session:
- 42aa7728 - fix(copilot): resolve API Plugin auth — OAuth flow with Entra SSO
- 37aa1a7b - fix(copilot): align OpenAPI spec with actual endpoints + remove confirmations
Uncommitted: manifest.json, declarativeAgent.json, Deploy-CopilotAgent.ps1, deploy/ binaries

### Recovery Instructions

1. Read this file for current state
2. Auth is SOLVED — do not change the OAuth flow
3. Next priorities: (a) Fix OpenAPI spec to match actual endpoints precisely, (b) Add user context to events endpoint, (c) Add confirmation:None to read functions (verify schema support), (d) Test search endpoints
4. The diagnostic middleware in MiddlewarePipelineExtensions.cs should be removed before production
5. The auth failure logging in AuthorizationModule.cs should be removed before production

---

*Checkpoint saved by context-handoff skill*
