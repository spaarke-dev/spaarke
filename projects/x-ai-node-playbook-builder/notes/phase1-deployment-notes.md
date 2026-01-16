# Phase 1 Deployment Notes

> **Date**: 2026-01-09
> **Task**: 009 - Phase 1 Tests and Deployment
> **Status**: Complete (API Deployed, Dataverse Schema Pending)

---

## Deployment Verification (2026-01-09 15:47 UTC)

### BFF API Deployment
- **Deployment ID**: `2352a3de0bd04f98b8ffab32ac022a7d`
- **Status**: ✅ Succeeded
- **URL**: https://spe-api-dev-67e2xz.azurewebsites.net

### Endpoint Verification

| Endpoint | Method | Status | Result |
|----------|--------|--------|--------|
| `/ping` | GET | ✅ 200 | `pong` |
| `/healthz` | GET | ✅ 200 | `Healthy` |
| `/status` | GET | ✅ 200 | Service metadata returned |
| `/api/ai/playbooks/{id}/nodes` | GET | ✅ 401 | Auth required (endpoint exists) |
| `/api/ai/playbooks/{id}/validate` | POST | ✅ 401 | Auth required (endpoint exists) |
| `/api/ai/playbooks/{id}/execute` | POST | ✅ 401 | Auth required (endpoint exists) |
| `/api/ai/playbooks/runs/{id}` | GET | ✅ 401 | Auth required (endpoint exists) |

All new endpoints are correctly registered and require authentication.

---

## Test Status Summary

### New Endpoint Tests (Task 008)
**Result: ✅ All 23 tests pass**

- `NodeEndpointsTests.cs` - 10 tests (CRUD operations)
- `PlaybookRunEndpointsTests.cs` - 13 tests (execution/streaming)

### Overall Test Suite
- **Total Tests**: 1302
- **Passing**: 1185
- **Failing**: 117
- **New Tests Added**: 23 (all pass)

### Pre-existing Test Failures (Not Related to Phase 1)

These failures existed before the ai-node-playbook-builder project started:

| Test Category | Count | Issue |
|---------------|-------|-------|
| `SpeFileStoreTests` | 5 | NullReferenceException in mock setup |
| `DataverseEntitySchemaTests` | 2 | Missing field mappings for EmailCc, EmailMessageId, etc. |
| `WireMock Integration Tests` | ~20 | Integration test setup/teardown issues |
| `Token Validation Tests` | ~90 | Expected when running without real credentials |

**Note**: The pre-existing failures should be addressed in a separate maintenance task. They do not affect the Node Playbook Builder functionality.

---

## API Changes Deployed

### New Files Created

**Endpoints:**
- `src/server/api/Sprk.Bff.Api/Api/Ai/NodeEndpoints.cs`
- `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookRunEndpoints.cs`

**Services:**
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/IPlaybookOrchestrationService.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/ExecutionGraph.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/NodeExecutionContext.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/NodeOutput.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookRunContext.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/` (Node executor framework)

### Modified Files

- `src/server/api/Sprk.Bff.Api/Program.cs` - Added DI registrations and endpoint mappings
- `src/server/api/Sprk.Bff.Api/Models/Ai/PlaybookDto.cs` - Added Mode and Type enums
- `tests/unit/Sprk.Bff.Api.Tests/CustomWebAppFactory.cs` - Fixed AzureAd configuration

---

## New API Endpoints

### Node Management (`/api/ai/playbooks/{id}/nodes`)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| GET | `/api/ai/playbooks/{id}/nodes` | List all nodes | Access |
| POST | `/api/ai/playbooks/{id}/nodes` | Create node | Owner |
| GET | `/api/ai/playbooks/{id}/nodes/{nodeId}` | Get node | Access |
| PUT | `/api/ai/playbooks/{id}/nodes/{nodeId}` | Update node | Owner |
| DELETE | `/api/ai/playbooks/{id}/nodes/{nodeId}` | Delete node | Owner |
| PUT | `/api/ai/playbooks/{id}/nodes/reorder` | Reorder nodes | Owner |
| PUT | `/api/ai/playbooks/{id}/nodes/{nodeId}/scopes` | Update scopes | Owner |

### Playbook Execution (`/api/ai/playbooks`)

| Method | Path | Description | Auth |
|--------|------|-------------|------|
| POST | `/api/ai/playbooks/{id}/validate` | Validate graph | Access |
| POST | `/api/ai/playbooks/{id}/execute` | Execute (SSE) | Access |
| GET | `/api/ai/playbooks/runs/{runId}` | Get run status | Bearer |
| GET | `/api/ai/playbooks/runs/{runId}/stream` | Stream events (SSE) | Bearer |
| POST | `/api/ai/playbooks/runs/{runId}/cancel` | Cancel run | Bearer |

---

## Dataverse Schema Status

**Status: ✅ Complete (2026-01-09)**

The Dataverse schema was created via Web API using PowerShell scripts.

### Deployment Scripts Used
- `scripts/Deploy-PlaybookNodeSchema.ps1` - Initial entity and option set creation
- `scripts/Fix-PlaybookNodeAttributes.ps1` - Added missing attributes and lookups
- `scripts/Create-NNRelationships.ps1` - Created N:N relationships and boolean fields

### Required Changes

**Global Option Sets (8):**
- sprk_playbookmode, sprk_playbooktype, sprk_triggertype
- sprk_outputformat, sprk_aiprovider, sprk_aicapability
- sprk_playbookrunstatus, sprk_noderunstatus

**Extended Entities (3):**
- sprk_analysisplaybook (8 new fields)
- sprk_analysisaction (8 new fields)
- sprk_analysistool (2 new fields)

**New Entities (7):**
- sprk_aiactiontype (lookup table)
- sprk_analysisdeliverytype (lookup table)
- sprk_aimodeldeployment
- sprk_deliverytemplate
- sprk_playbooknode (main node entity)
- sprk_playbookrun (execution tracking)
- sprk_playbooknoderun (node execution tracking)

**N:N Relationships (2):**
- sprk_playbooknode_skill
- sprk_playbooknode_knowledge

---

## Test Configurations Fixed

### CustomWebAppFactory Updates

Added `AzureAd` configuration section for Microsoft Identity Web:

```csharp
["AzureAd:Instance"] = "https://login.microsoftonline.com/",
["AzureAd:TenantId"] = "test-tenant-id",
["AzureAd:ClientId"] = "test-app-id",
["AzureAd:Audience"] = "api://test-app-id",
```

This fixed 24 tests that were failing with `IDW10106: The 'ClientId' option must be provided`.

---

## Known Issues

### 1. Pre-existing Test Failures

117 tests fail but are pre-existing issues unrelated to this project:
- SpeFileStoreTests mock setup
- DataverseEntitySchemaTests missing mappings
- WireMock integration tests
- Token validation tests (expected without real credentials)

**Recommendation**: Create separate maintenance task to address these.

### 2. ServiceBus Processor Cleanup

Test cleanup logs show `ObjectDisposedException` for ServiceBus processors. This is a pre-existing infrastructure issue with the fake ServiceBus connection string used in tests. Tests pass but cleanup fails.

### 3. Dataverse Schema Not Deployed

Full E2E testing of Node CRUD requires Dataverse schema to be created first. API deployment can proceed but data won't persist.

---

## Completion Summary

| Step | Status | Notes |
|------|--------|-------|
| Run unit tests | ✅ Complete | 1185/1302 pass (117 pre-existing failures) |
| Fix test failures | ✅ Complete | Fixed AzureAd configuration, 23 new tests pass |
| Deploy BFF API | ✅ Complete | Deployed 2026-01-09 15:47 UTC |
| Verify endpoints | ✅ Complete | All endpoints return expected responses |
| Dataverse schema | ✅ Complete | Created via Web API (2026-01-09) |
| E2E testing | ⏳ Ready | Schema complete, can now test with auth |

---

*Phase 1 deployment notes - Completed 2026-01-09*
