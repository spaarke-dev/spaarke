# Task 045: R3 Production Deployment Checklist

> **Date**: December 30, 2025
> **Project**: AI Document Intelligence R3
> **Status**: READY FOR DEPLOYMENT

---

## Pre-Deployment Checklist

### Code Readiness

| Item | Status | Notes |
|------|--------|-------|
| All R3 tasks complete (001-044) | ✅ | Security review complete |
| Build succeeds | ✅ | 0 errors, 0 warnings |
| Unit tests pass | ✅ | ~750 tests passing |
| Security review complete | ✅ | Task 044 - all critical issues fixed |
| Code reviewed | ✅ | PR review required before merge |

### Security Fixes Applied

| Fix | File | Verified |
|-----|------|----------|
| TenantAuthorizationFilter | `Api/Filters/TenantAuthorizationFilter.cs` | ✅ NEW |
| AiAuthorizationFilter oid claim fix | `Api/Filters/AiAuthorizationFilter.cs:57-59` | ✅ |
| ResilienceEndpoints auth | `Api/ResilienceEndpoints.cs` | ✅ |
| DocumentIntelligence auth enabled | `Api/Ai/DocumentIntelligenceEndpoints.cs:32,47,62` | ✅ |

---

## Deployment Steps

### Step 1: Merge Feature Branch to Master

```bash
# Ensure branch is up-to-date
git checkout feature/ai-document-intelligence-r3-phases-1-3
git pull origin master
git push origin feature/ai-document-intelligence-r3-phases-1-3

# Create PR and merge via GitHub
# PR Title: "feat(ai): AI Document Intelligence R3 - Phases 1-5"
```

### Step 2: Trigger CI/CD Pipeline

The pipeline will automatically run on merge to `main`:
- Build and test
- Deploy infrastructure (Bicep)
- Deploy API to App Service
- Run integration tests

Or trigger manually via workflow_dispatch:
```bash
gh workflow run deploy-to-azure.yml -f environment=prod
```

### Step 3: Update Key Vault Configuration (if needed)

New configuration values for R3:

| Secret | Description | Required |
|--------|-------------|----------|
| `DocumentIntelligence--AiSearchEndpoint` | Azure AI Search endpoint | ✅ |
| `DocumentIntelligence--AiSearchKey` | Azure AI Search API key | ✅ |
| `DocumentIntelligence--EmbeddingModel` | text-embedding-3-small | Optional (default) |
| `Ai--OpenAiEndpoint` | Azure OpenAI endpoint | ✅ Already exists |
| `Ai--OpenAiKey` | Azure OpenAI key | ✅ Already exists |

```bash
# Update via Azure CLI
az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name "DocumentIntelligence--AiSearchEndpoint" \
  --value "https://spaarke-search-dev.search.windows.net"

az keyvault secret set \
  --vault-name spaarke-spekvcert \
  --name "DocumentIntelligence--AiSearchKey" \
  --value "<your-api-key>"
```

### Step 4: Create RAG Index in Production

```bash
# Deploy RAG index schema (if not exists)
curl -X PUT "https://spaarke-search-prod.search.windows.net/indexes/spaarke-knowledge-index?api-version=2024-07-01" \
  -H "Content-Type: application/json" \
  -H "api-key: <admin-key>" \
  -d @infrastructure/ai-search/spaarke-knowledge-index.json
```

### Step 5: Deploy Dataverse Solution (if PCF changes)

```bash
# Only if PCF controls modified
pac solution import --path solutions/SpaarkeAI_managed.zip --activate-plugins
```

### Step 6: Verify Health

```bash
# Health check
curl https://spe-api-prod.azurewebsites.net/healthz

# Ping check
curl https://spe-api-prod.azurewebsites.net/ping

# Expected: "Healthy" and "pong"
```

---

## Post-Deployment Verification

### API Endpoints

| Endpoint | Method | Expected | Test Command |
|----------|--------|----------|--------------|
| `/healthz` | GET | 200 "Healthy" | `curl /healthz` |
| `/ping` | GET | 200 "pong" | `curl /ping` |
| `/api/ai/document-intelligence/analyze` | POST | 401 (no auth) | `curl -X POST /api/ai/document-intelligence/analyze` |
| `/api/ai/rag/search` | POST | 401 (no auth) | `curl -X POST /api/ai/rag/search` |
| `/api/resilience/circuit-breakers` | GET | 401 (no auth) | `curl /api/resilience/circuit-breakers` |

### Smoke Tests

```bash
# With valid token
export TOKEN="<bearer-token>"

# Test RAG search
curl -X POST "https://spe-api-prod.azurewebsites.net/api/ai/rag/search" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"query": "test", "tenantId": "<tenant-id>", "topK": 3}'

# Test document analysis (SSE)
curl -X POST "https://spe-api-prod.azurewebsites.net/api/ai/document-intelligence/analyze" \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"documentId": "<doc-id>", "driveId": "<drive-id>", "itemId": "<item-id>"}'
```

---

## Monitoring

### Azure Monitor Dashboards

After deployment, verify dashboards are receiving data:
- AI Operations Dashboard
- Circuit Breaker Status
- Rate Limiting Metrics

### Log Queries

```kusto
// Check for errors in last 30 minutes
traces
| where timestamp > ago(30m)
| where severityLevel >= 3
| where message contains "AI" or message contains "Rag"
| project timestamp, message, severityLevel
| order by timestamp desc
```

---

## Rollback Procedure

See: `task-045-rollback-plan.md`

---

## Deployment History

| Date | Version | Deployer | Notes |
|------|---------|----------|-------|
| TBD | R3.1.0 | - | Initial R3 production deployment |

---

*Checklist created: December 30, 2025*
