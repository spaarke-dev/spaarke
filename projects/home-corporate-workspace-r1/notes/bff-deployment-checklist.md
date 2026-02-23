# BFF Endpoint Deployment Checklist — Home Corporate Workspace R1

> **Task**: 042 — BFF Endpoint Deployment
> **Phase**: 5 — Deployment & Wrap-up
> **Created**: 2026-02-18
> **Deployment Script**: `scripts/Deploy-WorkspaceBff.ps1`

---

## Environment Details

| Item | Value |
|------|-------|
| App Service | `spe-api-dev-67e2xz` |
| Resource Group | `spe-infrastructure-westus2` |
| BFF Base URL | `https://spe-api-dev-67e2xz.azurewebsites.net` |
| Health Endpoint | `https://spe-api-dev-67e2xz.azurewebsites.net/healthz` |
| Dataverse (Dev) | `https://spaarkedev1.crm.dynamics.com` |

---

## Workspace Endpoints Being Deployed

| Method | Route | Service | Notes |
|--------|-------|---------|-------|
| GET | `/api/workspace/portfolio` | `PortfolioService` | Redis-cached 5 min; requires auth (ADR-008) |
| GET | `/api/workspace/health` | `PortfolioService` | Redis-cached 5 min; health indicators only |
| GET | `/api/workspace/briefing` | `BriefingService` | Redis-cached 10 min; AI-enhanced when available |
| POST | `/api/workspace/calculate-scores` | `PriorityScoringService`, `EffortScoringService` | Max 50 items per batch |
| GET | `/api/workspace/events/{id}/scores` | `PriorityScoringService`, `EffortScoringService` | Single-event scoring |
| POST | `/api/workspace/ai/summary` | `WorkspaceAiService` | Rate-limited (ai-stream policy, 10 req/min) |

---

## Pre-Deployment Checks

### Code Verification

- [ ] All workspace endpoint files are present:
  - `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceEndpoints.cs`
  - `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceAiEndpoints.cs`
  - `src/server/api/Sprk.Bff.Api/Api/Workspace/WorkspaceMatterEndpoints.cs`
- [ ] All workspace service files are present:
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/PortfolioService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/BriefingService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/PriorityScoringService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/EffortScoringService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/WorkspaceAiService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/TodoGenerationService.cs`
  - `src/server/api/Sprk.Bff.Api/Services/Workspace/MatterPreFillService.cs`
- [ ] Workspace models are present under `Api/Workspace/Models/` and `Api/Workspace/Contracts/`
- [ ] Endpoints are registered in `Program.cs` (`MapWorkspaceEndpoints`, `MapWorkspaceAiEndpoints`)
- [ ] `WorkspaceAuthorizationFilter` is implemented under `Api/Filters/`

### Build Verification

- [ ] Local build succeeds: `dotnet build src/server/api/Sprk.Bff.Api/`
- [ ] No build warnings or errors related to workspace endpoints
- [ ] Release build succeeds: `dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o ./publish`

### Test Verification

- [ ] Unit tests pass: `dotnet test tests/` (Task 035: scoring engine tests)
- [ ] Integration tests pass: `dotnet test tests/` (Task 036: BFF endpoint tests)
- [ ] No failing tests before deployment

---

## Azure Prerequisites

- [ ] Azure CLI is installed: `az --version`
- [ ] Authenticated to Azure: `az account show`
- [ ] Correct subscription is active (confirm `spe-infrastructure-westus2` resource group is accessible)
- [ ] App Service `spe-api-dev-67e2xz` is in Running state
- [ ] Redis Cache is running and connected (required by ADR-009)
- [ ] Verify Redis connection string is set in App Service configuration:
  - Setting name: `ConnectionStrings__Redis` (or equivalent)
  - Confirm via Azure Portal > App Service > Configuration > Application Settings
- [ ] Confirm App Registration / managed identity is configured for Dataverse access

---

## Deployment Steps

### Step 1: Run Deployment Script

```powershell
# From repository root
.\scripts\Deploy-WorkspaceBff.ps1

# Or with options:
.\scripts\Deploy-WorkspaceBff.ps1 -SkipBuild          # Use existing build
.\scripts\Deploy-WorkspaceBff.ps1 -SkipEndpointTests  # Skip smoke tests
```

### Step 2: Manual Deployment (if script unavailable)

```powershell
# 1. Build (from repo root)
dotnet publish src/server/api/Sprk.Bff.Api/ -c Release -o ./publish

# 2. Deploy to Azure
az webapp deploy `
    --resource-group spe-infrastructure-westus2 `
    --name spe-api-dev-67e2xz `
    --src-path ./publish `
    --type zip `
    --async false

# 3. Verify health
Invoke-RestMethod https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

- [ ] Script executed without errors
- [ ] `dotnet publish` succeeded (exit code 0)
- [ ] `az webapp deploy` completed successfully
- [ ] App Service restarted and is accepting requests

---

## Deployment Verification

### Health Check

- [ ] `GET /healthz` returns `Healthy` or `{ "status": "Healthy" }`
- [ ] Response time is under 2 seconds (cold start may be slower)

Command:
```powershell
Invoke-RestMethod https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

### Endpoint Reachability (Unauthenticated — Expect 401)

These tests confirm endpoints are registered and authentication is enforced (ADR-008).
A 401 response means the endpoint exists and is protected — this is the expected result without a bearer token.

- [ ] `GET /api/workspace/portfolio` → 401 Unauthorized
- [ ] `GET /api/workspace/health` → 401 Unauthorized
- [ ] `GET /api/workspace/briefing` → 401 Unauthorized
- [ ] `POST /api/workspace/calculate-scores` → 401 Unauthorized
- [ ] `POST /api/workspace/ai/summary` → 401 Unauthorized

Commands:
```powershell
# Each should return 401 (endpoint exists, auth enforced)
Invoke-WebRequest https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/portfolio -UseBasicParsing
Invoke-WebRequest https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/health -UseBasicParsing
Invoke-WebRequest https://spe-api-dev-67e2xz.azurewebsites.net/api/workspace/briefing -UseBasicParsing
```

---

## Post-Deployment Verification

### Authentication (ADR-008)

- [ ] Authenticated request to `/api/workspace/portfolio` returns 200 with `PortfolioSummaryResponse` shape
- [ ] Request without token returns 401 (not 403 or 500)
- [ ] `WorkspaceAuthorizationFilter` sets `UserId` in `HttpContext.Items`

### Redis Caching (ADR-009)

- [ ] First request to `/api/workspace/portfolio` hits Dataverse and caches result
- [ ] Second request within 5 minutes returns cached data (verify via `CachedAt` field in response)
- [ ] First request to `/api/workspace/briefing` caches for 10 minutes
- [ ] Confirm Redis connection is active in App Service logs (no connection errors)

### Response Shape Verification

Use an authenticated token (from browser dev tools or Postman) to verify:

**Portfolio Response** (`GET /api/workspace/portfolio`):
```json
{
  "activeMatters": 12,
  "mattersAtRisk": 2,
  "totalSpend": 450000.00,
  "budgetTotal": 600000.00,
  "utilizationPercent": 75.0,
  "overdueEvents": 3,
  "cachedAt": "2026-02-18T10:00:00Z"
}
```

**Batch Score Response** (`POST /api/workspace/calculate-scores`):
```json
{
  "results": [
    {
      "eventId": "00000000-0000-0000-0000-000000000001",
      "priorityScore": 85,
      "priorityLevel": "High",
      "priorityFactors": { ... },
      "effortScore": 3,
      "effortLevel": "Medium"
    }
  ]
}
```

**Briefing Response** (`GET /api/workspace/briefing`):
```json
{
  "activeMatters": 12,
  "mattersAtRisk": 2,
  "narrative": "You have 12 active matters...",
  "isAiEnhanced": true,
  "generatedAt": "2026-02-18T10:00:00Z"
}
```

- [ ] Portfolio endpoint returns expected `PortfolioSummaryResponse` fields
- [ ] Batch score endpoint returns `BatchScoreResponse` with `results` array
- [ ] Briefing endpoint returns `BriefingResponse` with `narrative` string and `isAiEnhanced` flag
- [ ] AI summary endpoint returns `AiSummaryResponse` with `confidence` field
- [ ] All error responses use `ProblemDetails` format (ADR-019)

### Existing Endpoints Not Disrupted

Verify that existing BFF endpoints still respond correctly after workspace deployment:

- [ ] `GET /healthz` → Healthy
- [ ] `GET /ping` → responds
- [ ] At least one existing non-workspace endpoint responds as expected

---

## Rollback Procedure

If deployment causes issues, revert to the previous working deployment:

### Option 1: Re-deploy Previous Build (Recommended)

```powershell
# 1. Identify the previous deployment slot or artifact
#    (Check GitHub Actions artifacts or local publish history)

# 2. Re-deploy the previous zip
az webapp deploy `
    --resource-group spe-infrastructure-westus2 `
    --name spe-api-dev-67e2xz `
    --src-path ./previous-publish.zip `
    --type zip `
    --async false

# 3. Verify health
Invoke-RestMethod https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

### Option 2: Restart App Service

If the App Service is in a bad state but the binary is intact:

```powershell
az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
Start-Sleep -Seconds 30
Invoke-RestMethod https://spe-api-dev-67e2xz.azurewebsites.net/healthz
```

### Option 3: Check Deployment Logs

```powershell
# Stream live logs from App Service
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz
```

---

## ADR Compliance Confirmation

| ADR | Constraint | Status |
|-----|-----------|--------|
| ADR-001 | BFF uses .NET 8 Minimal API — deployed as single application | Workspace endpoints use `MapGroup`, no controllers |
| ADR-008 | Endpoint filters for authorization — no global middleware | `WorkspaceAuthorizationFilter` applied per-endpoint |
| ADR-009 | Redis-first caching — portfolio and briefing data cached in Redis | `PortfolioService` and `BriefingService` use `IDistributedCache` |
| ADR-019 | ProblemDetails for all error responses | All catch blocks return `Results.Problem(...)` |

---

## Notes

- The `GET /api/workspace/events/{id}/scores` endpoint is also deployed but not listed in the main smoke test above (requires a valid event GUID in the URL). Test separately with a known event ID.
- The `POST /api/workspace/ai/summary` endpoint is rate-limited to 10 requests/minute per user (`ai-stream` policy). Rate limit is enforced via existing middleware registered in `Program.cs`.
- Workspace models are co-located under `Api/Workspace/Models/` and `Api/Workspace/Contracts/` (not a separate `Models/Workspace/` folder), which is the pattern used by this project.
- Redis cache TTLs: portfolio = 5 min, briefing = 10 min. Cache is per-user keyed by user OID.
- If Redis is unavailable, the services should degrade gracefully (verify no 500s without Redis).
