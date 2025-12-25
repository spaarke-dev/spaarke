# Task Index - AI Document Intelligence R1

> **Project**: ai-document-intelligence-r1
> **Last Updated**: December 25, 2025
> **Version**: 2.0 (Rescoped)
> **Total Tasks**: ~45 (R1 Core Infrastructure only)

## Project Subdivision

This project was subdivided into three parts:

| Project | Scope | Status |
|---------|-------|--------|
| **R1** (this) | Core Infrastructure | In Progress |
| [R2](../../ai-document-intelligence-r2/) | Analysis Workspace UI | Not Started |
| [R3](../../ai-document-intelligence-r3/) | Advanced Features & Production | Not Started |

## Task Status Legend

- 🔲 Not Started
- 🔄 In Progress
- ✅ Complete
- ⏸️ Blocked
- ❌ Cancelled
- ⚠️ Needs Verification

---

## Phase 1: Core Infrastructure

### Multi-Tenant Parameterization

| ID | Task | Status | Dependencies | Notes |
|----|------|--------|--------------|-------|
| 001 | Create Environment Variables in Dataverse Solution | 🔲 | none | Not verified in Dataverse |
| 002 | Create Bicep Parameter Template | ✅ | 001 | ai-foundry.bicepparam exists |
| 003 | Create Token-Replacement appsettings.json Template | 🔲 | 001 | Needs verification |
| 004 | Update BFF API Configuration to Use Environment Variables | 🔲 | 001, 003 | Needs verification |

### Azure AI Foundry Infrastructure

| ID | Task | Status | Dependencies | Notes |
|----|------|--------|--------------|-------|
| 005 | Create Parameterized Bicep Template for AI Foundry Hub | ✅ | 002 | Exists in infrastructure/bicep |
| 006 | Deploy AI Foundry Hub and Project | ✅ | 005 | Deployed: sprkspaarkedev-aif-hub |
| 007 | Create Prompt Flow: analysis-execute | ⚠️ | 006 | Template created, needs completion |
| 008 | Create Prompt Flow: analysis-continue | ⚠️ | 006 | Template created, needs completion |
| 009 | Configure AI Foundry Evaluation Pipeline | ⚠️ | 006 | Config created, needs verification |

### Dataverse Entities (CRITICAL - Status Corrected)

| ID | Task | Status | Dependencies | Notes |
|----|------|--------|--------------|-------|
| 010 | Create sprk_analysis Entity with Fields and Relationships | ⚠️ | none | Referenced in code, needs Dataverse verification |
| 011 | Create sprk_analysisaction Entity | ⚠️ | none | Referenced in code, needs Dataverse verification |
| 012 | Create sprk_analysisskill Entity | ⚠️ | none | Referenced in code, needs Dataverse verification |
| 013 | Create sprk_analysisknowledge Entity | ⚠️ | none | Referenced in code, needs Dataverse verification |
| 014 | Create sprk_knowledgedeployment Entity | ⚠️ | none | Referenced in code, needs Dataverse verification |
| 015 | Create sprk_analysistool Entity | ⚠️ | none | Referenced in code, needs Dataverse verification |
| 016 | Create sprk_analysisplaybook Entity with N:N Relationships | ⚠️ | 010-015 | Referenced in code, needs Dataverse verification |
| 017 | Create sprk_analysisworkingversion Entity | ⚠️ | 010 | Referenced in code, needs Dataverse verification |
| 018 | Create sprk_analysisemailmetadata Entity | ⚠️ | 010 | Referenced in code, needs Dataverse verification |
| 019 | Create sprk_analysischatmessage Entity | ⚠️ | 010 | Referenced in code, needs Dataverse verification |
| 020 | Create Security Roles for Analysis Feature | 🔲 | 010-019 | Depends on entity verification |
| 021 | Export Dataverse Solution Package | 🔲 | 001, 010-020 | Depends on entity verification |

### BFF API Implementation (Complete)

| ID | Task | Status | Dependencies | Notes |
|----|------|--------|--------------|-------|
| 022 | Create AnalysisEndpoints.cs with POST /execute | ✅ | 010, 004 | See [CODE-INVENTORY.md](../CODE-INVENTORY.md) |
| 023 | Create AnalysisEndpoints.cs with POST /continue | ✅ | 022 | Complete |
| 024 | Create AnalysisEndpoints.cs with POST /save | ✅ | 022 | Complete |
| 025 | Create AnalysisEndpoints.cs with POST /export | ✅ | 022 | Complete |
| 026 | Create AnalysisOrchestrationService | ✅ | 022-025 | 18KB, complete |
| 027 | Create ScopeResolverService with Redis Caching | ✅ | 026 | 4KB, complete |
| 028 | Create AnalysisContextBuilder with Prompt Flow Integration | ✅ | 007, 008, 026 | 5KB, complete |
| 029 | Create WorkingDocumentVersionService with SPE Storage | ✅ | 026 | 4KB, complete |
| 030 | Create AnalysisAuthorizationFilter | ✅ | 022-025 | Complete |
| 031 | Add Unit Tests for Analysis Services | ✅ | 026-029 | 5 test files |
| 032 | Add Integration Tests for Analysis Endpoints | ✅ | 022-025, 026-029 | Complete |

### Deployment & Verification

| ID | Task | Status | Dependencies | Notes |
|----|------|--------|--------------|-------|
| 033 | Test Bicep Deployment to External Test Subscription | 🔲 | 005, 006, 021 | Blocked by 021 |
| 034 | Test Dataverse Solution Import to Clean Environment | 🔲 | 021 | Blocked by 021 |
| 035 | Verify All Environment Variables Resolve Correctly | 🔲 | 001, 004, 033 | Blocked by 001 |
| 036 | Run Integration Tests Against Dev Environment | 🔲 | 032, 033 | Blocked by 033 |
| 037 | Create Phase 1 Deployment Guide | 🔲 | 033-036 | Blocked by prior tasks |

---

## NEW: Verification Tasks

These tasks were added to verify existing work before proceeding:

| ID | Task | Status | Dependencies | Notes |
|----|------|--------|--------------|-------|
| V-001 | Verify Dataverse entities exist in spaarkedev1.crm.dynamics.com | 🔲 | none | Use Power Apps maker portal |
| V-002 | Document entity fields and relationships | 🔲 | V-001 | Update CODE-INVENTORY.md |
| V-003 | Verify Environment Variables in Dataverse | 🔲 | none | Check solution components |
| V-004 | Test BFF API endpoints against dev environment | 🔲 | V-001 | Verify SSE streaming works |
| V-005 | Verify AI Foundry connections are configured | 🔲 | none | Check Azure portal |

---

## Summary

### R1 Scope (This Project)

| Category | Tasks | Status |
|----------|-------|--------|
| Multi-Tenant Parameterization | 4 | 1 complete, 3 need verification |
| Azure AI Foundry | 5 | 2 complete, 3 need verification |
| Dataverse Entities | 12 | All need verification |
| BFF API Implementation | 11 | All complete |
| Deployment & Verification | 5 | All pending |
| **New Verification Tasks** | 5 | All pending |
| **Total** | ~42 | ~14 complete |

### Out of Scope (Moved to R2/R3)

| Phase | Description | New Project |
|-------|-------------|-------------|
| Phase 2: UI Components | Custom Pages, PCF deployment | R2 |
| Phase 3: Scope System & RAG | Admin UI, Hybrid RAG, Tools | R3 |
| Phase 4: Playbooks & Export | Playbooks, DOCX/PDF, Email, Teams | R3 |
| Phase 5: Production Readiness | Performance, monitoring, production deploy | R3 |

---

## Critical Path

**Immediate Priority:**
1. V-001: Verify Dataverse entities exist
2. V-003: Verify Environment Variables exist
3. V-005: Verify AI Foundry connections

**Then:**
- Complete tasks 010-021 if entities missing
- Complete task 001 if Environment Variables missing
- Run verification tasks V-002, V-004

---

## Existing Code Reference

See [CODE-INVENTORY.md](../CODE-INVENTORY.md) for complete list of:
- BFF API endpoints and services (all complete)
- PCF controls (complete but not deployed)
- Unit tests (complete)
- Infrastructure templates (partial)

**DO NOT recreate files listed in CODE-INVENTORY.md**

---

*Last Updated: December 25, 2025*
