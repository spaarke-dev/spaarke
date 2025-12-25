# AI Document Intelligence R1 - Core Infrastructure

> **Status**: In Progress
> **Version**: 2.0
> **Last Updated**: December 25, 2025
> **Phase**: Verification → Implementation

---

## Quick Links

- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
- [Design Specification](spec.md)
- [Code Inventory](CODE-INVENTORY.md)
- [AI Context](CLAUDE.md)

---

## Overview

R1 delivers the **core infrastructure** for the Analysis feature:
- Dataverse entities for Analysis, Actions, Skills, Knowledge, Tools
- BFF API endpoints with SSE streaming
- Environment variable parameterization
- AI Foundry infrastructure
- Verification and deployment testing

**This project was rescoped from 178 tasks to ~45 tasks.** UI components moved to R2, advanced features to R3.

---

## Project Subdivision

| Project | Scope | Tasks | Status |
|---------|-------|-------|--------|
| **R1** (this) | Core Infrastructure | ~45 | In Progress |
| [R2](../ai-document-intelligence-r2/) | UI Components | ~23 | Not Started |
| [R3](../ai-document-intelligence-r3/) | Advanced Features + Production | ~111 | Not Started |

---

## Existing Code Inventory

### BFF API (COMPLETE)

| File | Location | Status |
|------|----------|--------|
| AnalysisEndpoints.cs | [src/server/api/Sprk.Bff.Api/Api/Ai/](../../src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs) | Complete |
| AnalysisOrchestrationService.cs | [src/server/api/Sprk.Bff.Api/Services/Ai/](../../src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs) | Complete |
| AnalysisContextBuilder.cs | Services/Ai/ | Complete |
| ScopeResolverService.cs | Services/Ai/ | Complete |
| WorkingDocumentService.cs | Services/Ai/ | Complete |
| AnalysisAuthorizationFilter.cs | Api/Filters/ | Complete |

**API Endpoints:**
- POST /api/ai/analysis/execute (SSE streaming)
- POST /api/ai/analysis/{id}/continue (SSE streaming)
- POST /api/ai/analysis/{id}/save
- POST /api/ai/analysis/{id}/export
- GET /api/ai/analysis/{id}

**Models (All Complete):**
- AnalysisExecuteRequest.cs
- AnalysisContinueRequest.cs
- AnalysisSaveRequest.cs
- AnalysisExportRequest.cs
- AnalysisResult.cs
- AnalysisChunk.cs

### Unit Tests (COMPLETE)

| File | Location | Status |
|------|----------|--------|
| AnalysisEndpointsTests.cs | [tests/unit/Sprk.Bff.Api.Tests/](../../tests/unit/Sprk.Bff.Api.Tests/Api/Ai/AnalysisEndpointsTests.cs) | Complete |
| AnalysisOrchestrationServiceTests.cs | tests/unit/ | Complete |
| AnalysisContextBuilderTests.cs | tests/unit/ | Complete |
| AnalysisAuthorizationFilterTests.cs | tests/unit/ | Complete |

### Infrastructure (PARTIAL)

| File | Location | Status |
|------|----------|--------|
| ai-foundry.bicepparam | [infrastructure/bicep/](../../infrastructure/bicep/ai-foundry.bicepparam) | Exists |
| AI Foundry Hub | Azure | Deployed |
| AI Foundry Project | Azure | Deployed |
| Prompt Flow Templates | infrastructure/ai-foundry/ | Created |

### PCF Controls (Built - Move to R2)

| Control | Location | Status |
|---------|----------|--------|
| AnalysisBuilder | [src/client/pcf/AnalysisBuilder/](../../src/client/pcf/AnalysisBuilder/) | Built, deployment in R2 |
| AnalysisWorkspace | [src/client/pcf/AnalysisWorkspace/](../../src/client/pcf/AnalysisWorkspace/) | Built, deployment in R2 |

---

## Dataverse Entities (CRITICAL - Status Unknown)

The following entities were marked complete in TASK-INDEX but **no solution files exist in the codebase**. These need verification:

| Entity | Logical Name | Status |
|--------|--------------|--------|
| Analysis | sprk_analysis | **Verify in Dataverse** |
| Analysis Action | sprk_analysisaction | **Verify in Dataverse** |
| Analysis Skill | sprk_analysisskill | **Verify in Dataverse** |
| Analysis Knowledge | sprk_analysisknowledge | **Verify in Dataverse** |
| Knowledge Deployment | sprk_knowledgedeployment | **Verify in Dataverse** |
| Analysis Tool | sprk_analysistool | **Verify in Dataverse** |
| Analysis Playbook | sprk_analysisplaybook | **Verify in Dataverse** |
| Analysis Working Version | sprk_analysisworkingversion | **Verify in Dataverse** |
| Analysis Email Metadata | sprk_analysisemailmetadata | **Verify in Dataverse** |
| Analysis Chat Message | sprk_analysischatmessage | **Verify in Dataverse** |

**Action Required**: Connect to spaarkedev1.crm.dynamics.com and verify entity existence before proceeding.

---

## Revised Task List

### Phase 1A: Verification (Priority 1)

| ID | Task | Status | Notes |
|----|------|--------|-------|
| 001 | Verify Dataverse entities exist | Not Started | Check Power Apps maker portal |
| 002 | Verify Environment Variables in solution | Not Started | Check solution components |
| 003 | Verify AI Foundry Hub connections work | Not Started | Test via Azure portal |
| 004 | Run API health check | Not Started | Test /ping and /healthz |

### Phase 1B: Entity Creation (If Needed)

Only execute if verification fails:

| ID | Task | Status | Hours |
|----|------|--------|-------|
| 010 | Create sprk_analysis entity | Conditional | 4h |
| 011 | Create sprk_analysisaction entity | Conditional | 2h |
| 012 | Create sprk_analysisskill entity | Conditional | 2h |
| 013 | Create sprk_analysisknowledge entity | Conditional | 3h |
| 014 | Create sprk_knowledgedeployment entity | Conditional | 3h |
| 015 | Create sprk_analysistool entity | Conditional | 2h |
| 016 | Create sprk_analysisplaybook entity | Conditional | 4h |
| 017 | Create sprk_analysisworkingversion entity | Conditional | 3h |
| 018 | Create sprk_analysisemailmetadata entity | Conditional | 2h |
| 019 | Create sprk_analysischatmessage entity | Conditional | 2h |
| 020 | Create security roles | Conditional | 3h |
| 021 | Export solution package | Conditional | 2h |

### Phase 1C: Deployment Testing

| ID | Task | Status | Hours |
|----|------|--------|-------|
| 033 | Test Bicep deployment to external subscription | Not Started | 4h |
| 034 | Test Dataverse solution import to clean env | Not Started | 3h |
| 035 | Verify environment variables resolve | Not Started | 2h |
| 036 | Run integration tests against dev | Not Started | 3h |
| 037 | Create Phase 1 deployment guide | Not Started | 4h |

---

## R1 Completion Criteria

- [ ] All Dataverse entities verified or created
- [ ] API endpoints tested end-to-end
- [ ] Environment variables resolve correctly
- [ ] Solution exports and imports cleanly
- [ ] Integration tests pass
- [ ] Deployment guide created

**When R1 is complete**: Proceed to R2 for UI deployment.

---

## Quick Start

### 1. Verify Dataverse Entities

```powershell
# Connect to Dataverse
pac auth create --url https://spaarkedev1.crm.dynamics.com

# List entities (look for sprk_analysis*)
pac solution list
```

### 2. Test API

```bash
# Health check
curl https://spe-api-dev-67e2xz.azurewebsites.net/ping

# Test analysis endpoint (requires auth token)
curl -X POST https://spe-api-dev-67e2xz.azurewebsites.net/api/ai/analysis/execute \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"documentIds":["guid"],"actionId":"guid"}'
```

---

## Change Log

| Date | Version | Changes |
|------|---------|---------|
| Dec 11, 2025 | 1.0 | Initial project (178 tasks) |
| Dec 25, 2025 | 2.0 | Rescoped: R1=Core, R2=UI, R3=Advanced |

---

*Last Updated: December 25, 2025*
