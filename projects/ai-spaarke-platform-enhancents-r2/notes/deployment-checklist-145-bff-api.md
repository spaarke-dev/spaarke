# Deployment Checklist: Task 145 - Deploy BFF API

> **Task**: R2-145 - Deploy BFF API with New Endpoints and Tool Classes
> **Status**: Preparation complete (code-only environment - no live Azure access)
> **Date**: 2026-02-26

---

## Pre-Deployment Verification

### Build Verification

- [x] `dotnet build src/server/api/Sprk.Bff.Api/` -- **BUILD SUCCEEDED** (0 warnings, 0 errors)
- [x] `dotnet test` -- **3210 passed** out of 3584 total (374 failures are integration tests requiring live services)
- [x] Deployment script exists: `scripts/Deploy-BffApi.ps1`
- [x] Deploy script targets correct App Service: `spe-api-dev-67e2xz`
- [x] Deploy script targets correct Resource Group: `spe-infrastructure-westus2`

### Test Results Summary

| Test Project | Result |
|-------------|--------|
| `Spaarke.ArchTests` | Passed |
| `Spaarke.Core.Tests` | Passed |
| `Spaarke.Plugins.Tests` | Passed |
| `Sprk.Bff.Api.Tests` (unit) | Passed (bulk of 3210 passing tests) |
| `Spe.Integration.Tests` | Partial failures (374 -- requires live Azure/Dataverse services) |

### New R2 Endpoints Verified in Codebase

| Method | Path | File | Status |
|--------|------|------|--------|
| POST | `/api/ai/chat/sessions/{sessionId}/refine` | `ChatEndpoints.cs:73` | Exists, has auth filter + rate limiting |
| GET | `/api/ai/scopes/actions` | `ScopeEndpoints.cs:42` | Exists (actions endpoint) |

### New R2 Tool Classes Verified

| Tool Class | File | Purpose |
|-----------|------|---------|
| `PlaybookCapabilities` | `Models/Ai/Chat/PlaybookCapabilities.cs` | Capability constants (search, analyze, write_back, reanalyze, selection_revise, web_search, summarize) |
| `ChatActionsResponse` | `Models/Ai/Chat/ChatActionsResponse.cs` | Response model for actions endpoint |
| `SprkChatAgentFactory` | `Services/Ai/Chat/SprkChatAgentFactory.cs` | Capability-filtered tool resolution |

### Dependencies Confirmed

| Dependency | Task | Status |
|-----------|------|--------|
| SSE document stream events | R2-025 | Completed |
| GET /api/ai/chat/actions endpoint | R2-045 | Completed |
| WorkingDocumentTools | R2-035 | Completed |
| AnalysisExecutionTools | R2-065 | Completed |
| WebSearchTools | R2-090 | Completed |

---

## Deployment Steps (Requires Live Azure Environment)

### Step 1: Run Full Build and Test Suite

```bash
# Build
dotnet build src/server/api/Sprk.Bff.Api/ -c Release

# Run unit tests (skip integration tests if no live services)
dotnet test tests/unit/Sprk.Bff.Api.Tests/ --no-restore
dotnet test tests/unit/Spaarke.Core.Tests/ --no-restore
dotnet test tests/Spaarke.ArchTests/ --no-restore
```

Verify: Zero build errors, all unit tests pass.

### Step 2: Deploy to Azure Dev Environment

**Using Deploy-BffApi.ps1:**

```powershell
# Full build and deploy
.\scripts\Deploy-BffApi.ps1

# Or skip build if already done
.\scripts\Deploy-BffApi.ps1 -SkipBuild
```

**What the script does:**
1. Builds API in Release mode (`dotnet publish -c Release`)
2. Creates deployment ZIP package
3. Deploys to Azure App Service via `az webapp deploy`
4. Waits 10 seconds for restart
5. Verifies health check (up to 6 retries)

**Manual alternative:**

```bash
# Build and publish
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o publish

# Create ZIP
Compress-Archive -Path publish/* -DestinationPath publish.zip

# Deploy
az webapp deploy --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz --src-path publish.zip --type zip --async false
```

### Step 3: Verify Health Check

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: HTTP 200 "Healthy"

curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected: HTTP 200
```

### Step 4: Verify New Endpoints

#### /api/ai/chat/sessions/{sessionId}/refine

```bash
# Authenticated POST (requires Bearer token)
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/chat/sessions/{sessionId}/refine \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{"selectedText": "test text", "instruction": "improve clarity"}'

# Expected: SSE stream with token events
# Verify: Unauthenticated returns 401
```

#### /api/ai/scopes/actions

```bash
# Authenticated GET (requires Bearer token)
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/scopes/actions \
  -H "Authorization: Bearer {token}"

# Expected: HTTP 200 with JSON body (categories, actions)
# Verify: Unauthenticated returns 401
```

### Step 5: Verify SSE Document Streaming Events

Send a chat request that triggers document write tools. Verify SSE response includes:
- `document_stream_start` event
- `document_stream_token` events (streaming content)
- `document_stream_end` event
- Standard `token`/`done`/`error` events still present

### Step 6: Verify Tool Classes Are Registered

Send chat requests that exercise each new tool:
- **WorkingDocumentTools**: Streaming write to active document
- **AnalysisExecutionTools**: Trigger re-analysis
- **WebSearchTools**: Web search query

Verify each tool is invoked correctly by the AI pipeline via `SprkChatAgentFactory.ResolveTools()`.

### Step 7: Verify ProblemDetails Error Handling

```bash
# Malformed request (expect 400)
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/chat/sessions/invalid/refine \
  -H "Authorization: Bearer {token}" \
  -H "Content-Type: application/json" \
  -d '{}'

# Expected: ProblemDetails JSON with errorCode, status 400

# Unauthenticated (expect 401)
curl https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/scopes/actions

# Expected: ProblemDetails JSON with status 401
```

### Step 8: Verify Existing Endpoints Unaffected

```bash
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz    # 200
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping        # 200

# Existing chat endpoints still work
# Existing document endpoints still work
```

---

## Azure Target Configuration

| Setting | Value |
|---------|-------|
| Resource Group | `spe-infrastructure-westus2` |
| App Service Name | `spe-api-dev-67e2xz` |
| API URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Health Check | `https://spe-api-dev-67e2xz.azurewebsites.net/healthz` |
| Deployment Script | `scripts/Deploy-BffApi.ps1` |

---

## Key Files Reference

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj` | API project file |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ChatEndpoints.cs` | Chat SSE + refine endpoints |
| `src/server/api/Sprk.Bff.Api/Api/Ai/ScopeEndpoints.cs` | Actions endpoint |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgentFactory.cs` | Tool resolution with capabilities |
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/PlaybookCapabilities.cs` | Capability constants |
| `src/server/api/Sprk.Bff.Api/Models/Ai/Chat/ChatActionsResponse.cs` | Actions response model |
| `scripts/Deploy-BffApi.ps1` | Deployment automation script |

---

## ADR Compliance

| ADR | Requirement | Status |
|-----|------------|--------|
| ADR-001 | Minimal API pattern (no Azure Functions) | Verified |
| ADR-008 | Endpoint authorization filters on all AI endpoints | Verified (`.AddAiAuthorizationFilter()`) |
| ADR-016 | Rate limiting on AI endpoints | Verified (`.RequireRateLimiting("ai-stream")`) |
| ADR-019 | ProblemDetails for all errors | Verified |
| ADR-010 | No new DI registrations (factory-instantiated tools) | Verified |

---

## Notes

- **Actual deployment requires live Azure environment access** which is not available in this code-only session
- Build succeeds with 0 warnings, 0 errors
- 374 test failures are integration tests that require live Azure/Dataverse services (expected in code-only environment)
- All 3210 unit tests pass
- The Deploy-BffApi.ps1 script handles the complete build-package-deploy-verify cycle
