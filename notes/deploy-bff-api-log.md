# Task 121 — Deploy BFF API

**Status**: READY FOR EXECUTION
**Date Prepared**: 2026-05-17
**Dependencies**: Task 120 (Cosmos DB must be deployed and RBAC assigned)

---

## Pre-Deployment Checklist

- [ ] Task 120 complete (Cosmos DB deployed, RBAC verified)
- [ ] Azure CLI authenticated (`az login` + correct subscription)
- [ ] `dotnet build src/server/api/Sprk.Bff.Api/` succeeds locally
- [ ] `dotnet test` passes (all tests green)
- [ ] Verify `appsettings.json` contains Cosmos endpoint configuration
- [ ] Verify App Service app settings include:
  - `CosmosDb__Endpoint` or equivalent pointing to `https://spaarke-cosmos-dev.documents.azure.com:443/`
  - `CosmosDb__DatabaseName` = `spaarke-ai`
  - `OPENAI_ENDPOINT`, `AI_SEARCH_ENDPOINT`, `DOC_INTELLIGENCE_ENDPOINT` (via Key Vault refs)
  - `APPLICATIONINSIGHTS_CONNECTION_STRING`
- [ ] Content Safety resource `spaarke-contentsafety-dev` is provisioned (for safety pipeline)

---

## Deployment Script Reference

Script: `scripts/Deploy-BffApi.ps1`

### Key Parameters

| Parameter | Dev Default | Production |
|---|---|---|
| `Environment` | `dev` | `production` |
| `ResourceGroupName` | `spe-infrastructure-westus2` | `rg-spaarke-platform-prod` |
| `AppServiceName` | `spe-api-dev-67e2xz` | `spaarke-bff-prod` |
| `UseSlotDeploy` | `$false` (direct) | `$true` (zero-downtime swap) |
| `HealthCheckPath` | `/healthz` | `/healthz` |
| `MaxHealthCheckRetries` | 24 (120s ceiling) | 24 |
| `RollbackOnFailure` | `$true` | `$true` |

### Deployment Flow

1. Build API in Release mode (`dotnet publish -c Release`)
2. Create zip deployment package
3. Deploy via `az webapp deploy` (direct for dev, slot for prod)
4. Verify file replacement (SHA-256 hash check against Kudu VFS)
5. Health check verification (`GET /healthz`)
6. (Prod only) Swap staging slot to production
7. (Prod only) Rollback if post-swap health check fails

---

## Deployment Commands

### Dev Environment

```powershell
# Standard dev deployment (build + deploy + verify)
.\scripts\Deploy-BffApi.ps1

# Skip build (use existing publish artifacts)
.\scripts\Deploy-BffApi.ps1 -SkipBuild
```

### Production Environment

```powershell
.\scripts\Deploy-BffApi.ps1 `
  -Environment production `
  -ResourceGroupName "rg-spaarke-platform-prod" `
  -AppServiceName "spaarke-bff-prod" `
  -UseSlotDeploy
```

---

## Post-Deployment Verification

### 1. Health Check

```bash
# Dev
curl https://spe-api-dev-67e2xz.azurewebsites.net/healthz
# Expected: "Healthy"

curl https://spe-api-dev-67e2xz.azurewebsites.net/ping
# Expected: 200 OK
```

### 2. R2 Endpoint Verification (New AI Platform Endpoints)

Verify all new R2 endpoints are responding (require auth token):

```bash
BASE_URL="https://spe-api-dev-67e2xz.azurewebsites.net"
TOKEN=$(az account get-access-token --resource api://sprk-bff --query accessToken -o tsv)
AUTH="Authorization: Bearer $TOKEN"

# Chat session management
curl -s -o /dev/null -w "%{http_code}" -H "$AUTH" -X POST "$BASE_URL/api/ai/chat/sessions" \
  -H "Content-Type: application/json" -d '{"playbookId":"test"}'
# Expected: 200 or 201 (session created)

# Session history
curl -s -o /dev/null -w "%{http_code}" -H "$AUTH" "$BASE_URL/api/ai/chat/sessions/{sessionId}/history"
# Expected: 200

# Session restore
curl -s -o /dev/null -w "%{http_code}" -H "$AUTH" "$BASE_URL/api/ai/chat/sessions/{sessionId}/restore"
# Expected: 200

# Playbook listing
curl -s -o /dev/null -w "%{http_code}" -H "$AUTH" "$BASE_URL/api/ai/chat/playbooks"
# Expected: 200

# Feedback submission
curl -s -o /dev/null -w "%{http_code}" -H "$AUTH" -X POST "$BASE_URL/api/ai/feedback" \
  -H "Content-Type: application/json" -d '{"sessionId":"test","rating":5}'
# Expected: 200 or 201

# Context mappings
curl -s -o /dev/null -w "%{http_code}" -H "$AUTH" "$BASE_URL/api/ai/chat/context-mappings"
# Expected: 200

# Analysis endpoints
curl -s -o /dev/null -w "%{http_code}" -H "$AUTH" -X POST "$BASE_URL/api/ai/analysis/create" \
  -H "Content-Type: application/json" -d '{}'
# Expected: 200 or 400 (validates input)

# Capability refresh webhook (shared-secret auth, not bearer)
curl -s -o /dev/null -w "%{http_code}" -X POST "$BASE_URL/api/ai/capabilities/refresh"
# Expected: 401 (no secret provided — confirms endpoint is registered and protected)
```

### 3. Safety Service Connectivity Check

```bash
# Verify Content Safety endpoint is reachable from BFF
# Check App Service logs for safety pipeline initialization
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz \
  --filter "ContentSafety|SafetyPipeline|PromptShield" | head -20

# Send a test message and check that safety headers are present in SSE response
# The safety pipeline middleware (SafetyPipelineMiddleware.cs) should log:
#   - Prompt shield check result
#   - Groundedness check result (for RAG responses)
```

### 4. Cosmos DB Connectivity Check

```bash
# Verify BFF can connect to Cosmos DB via managed identity
# Check startup logs for Cosmos client initialization
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz \
  --filter "Cosmos|cosmos|SessionPersistence" | head -20

# Expected: Logs showing successful CosmosClient initialization with DefaultAzureCredential
# No connection string errors or 401/403 from Cosmos
```

---

## R1 Regression Smoke Test Checklist

Verify existing R1 functionality is unaffected by R2 deployment:

| Test | Endpoint/Action | Expected | Actual | Pass/Fail |
|---|---|---|---|---|
| Health check | `GET /healthz` | "Healthy" | | |
| Ping | `GET /ping` | 200 OK | | |
| Analysis create | `POST /api/ai/analysis/create` | 200 with analysis ID | | |
| Analysis execute (SSE) | `POST /api/ai/analysis/execute` | SSE stream with events | | |
| Analysis continue | `POST /api/ai/analysis/{id}/continue` | SSE stream continues | | |
| Analysis save | `POST /api/ai/analysis/{id}/save` | 200 document saved | | |
| Analysis export | `POST /api/ai/analysis/{id}/export` | 200 export created | | |
| Context mappings | `GET /api/ai/chat/context-mappings` | 200 with mapping list | | |
| Playbook list | `GET /api/ai/chat/playbooks` | 200 with playbook array | | |
| Playbook builder | `POST /api/ai/playbook-builder/process` | SSE stream | | |

---

## Troubleshooting

```bash
# View live logs
az webapp log tail --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz

# Check deployment status
az webapp deployment list-publishing-profiles \
  --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz

# Restart if needed
az webapp restart --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz

# Check app settings
az webapp config appsettings list \
  --resource-group spe-infrastructure-westus2 --name spe-api-dev-67e2xz \
  --query "[?name=='CosmosDb__Endpoint' || contains(name, 'OPENAI') || contains(name, 'ContentSafety')]" \
  --output table
```

---

## Results

| Check | Expected | Actual | Pass/Fail |
|---|---|---|---|
| Build succeeds | Exit code 0 | | |
| Tests pass | All green | | |
| Package size | ~60 MB | | |
| Deploy succeeds | No errors | | |
| File hash verification | All match | | |
| Health check | "Healthy" | | |
| R2 chat endpoints respond | 200/201 | | |
| R2 feedback endpoint responds | 200/201 | | |
| Safety pipeline initialized | Logs confirm | | |
| Cosmos DB connected | Logs confirm | | |
| R1 regression tests | All pass | | |

**Deployed By**: _______________
**Date Executed**: _______________
