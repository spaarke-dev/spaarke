# AI Document Intelligence R1 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: December 25, 2025
> **Source**: README.md (rescoped project definition)

## Executive Summary

R1 delivers the **core infrastructure** for the AI Analysis feature, including Dataverse entity verification/creation, BFF API endpoint verification, environment variable parameterization, and deployment testing. This release establishes the foundation that R2 (UI) and R3 (Advanced Features) build upon.

## Scope

### In Scope

- Verify Dataverse entities exist in dev environment (sprk_analysis, sprk_analysisaction, etc.)
- Create missing Dataverse entities if verification fails
- Verify BFF API endpoints function correctly with SSE streaming
- Verify environment variables resolve in Dataverse solution
- Test AI Foundry Hub connections and prompt flows
- Create security roles for Analysis feature
- Export Dataverse solution package
- Test Bicep deployment to external subscription
- Create Phase 1 deployment guide

### Out of Scope

- PCF control deployment (moved to R2)
- Custom Pages creation (moved to R2)
- Form customizations (moved to R2)
- Hybrid RAG infrastructure (moved to R3)
- Playbook system (moved to R3)
- Export to DOCX/PDF/Email/Teams (moved to R3)
- Production deployment (moved to R3)
- Performance optimization (moved to R3)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/Api/Ai/` - Analysis API endpoints (verification)
- `src/server/api/Sprk.Bff.Api/Services/Ai/` - Analysis services (verification)
- `src/solutions/` - Dataverse solution package (create/update)
- `infrastructure/bicep/` - AI Foundry infrastructure (verification)
- `infrastructure/ai-foundry/` - Prompt flow templates (verification)

## Requirements

### Functional Requirements

1. **FR-01**: Verify all 10 Dataverse entities exist - Acceptance: Entity metadata visible in Power Apps maker portal
2. **FR-02**: Create missing entities with correct fields and relationships - Acceptance: Entity schema matches design spec
3. **FR-03**: Verify API endpoints return valid responses - Acceptance: /execute returns SSE stream, /save returns document ID
4. **FR-04**: Environment variables resolve at runtime - Acceptance: BFF API reads config from Dataverse env vars
5. **FR-05**: AI Foundry connections work - Acceptance: Can invoke prompt flow from BFF API
6. **FR-06**: Security roles grant appropriate access - Acceptance: Analysis User can execute, Analysis Admin can configure
7. **FR-07**: Solution exports cleanly - Acceptance: Managed solution imports to clean environment without errors

### Non-Functional Requirements

- **NFR-01**: API SSE stream starts within 2 seconds (P95)
- **NFR-02**: Solution import completes within 5 minutes
- **NFR-03**: Zero hard-coded configuration values in BFF API

## Technical Constraints

### Applicable ADRs

- **ADR-001**: Minimal API pattern - All endpoints use .NET 8 Minimal API, no Azure Functions
- **ADR-003**: Lean Authorization - Use endpoint filters for authorization checks
- **ADR-007**: SpeFileStore facade - All file access through SpeFileStore, no Graph SDK leakage
- **ADR-008**: Per-resource authorization filters - Each endpoint has explicit auth filter
- **ADR-013**: AI Architecture - AI features extend BFF API, use AI Tool Framework pattern
- **ADR-010**: DI Minimalism - Maximum 15 non-framework DI registrations

### MUST Rules

- MUST use Minimal API pattern for all endpoints (no MVC controllers)
- MUST use endpoint filters for authorization (no global middleware)
- MUST NOT hard-code any configuration values (use environment variables)
- MUST NOT make HTTP calls from Dataverse plugins
- MUST use Server-Sent Events (SSE) for streaming responses
- MUST include unit tests for all new services

### Existing Patterns

- See `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` for endpoint pattern (already implemented)
- See `src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs` for auth filter pattern
- See `.claude/patterns/api/endpoint-definition.md` for endpoint conventions

## Success Criteria

1. [ ] All 10 Dataverse entities verified or created - Verify: Query entities via pac CLI
2. [ ] API health check returns 200 - Verify: curl /ping and /healthz
3. [ ] SSE streaming works for /execute endpoint - Verify: Integration test with real AI response
4. [ ] Environment variables load from Dataverse - Verify: BFF logs show env var resolution
5. [ ] Solution imports to clean environment - Verify: Import to test org without errors
6. [ ] Deployment guide created - Verify: External developer can follow guide successfully

## Dependencies

### Prerequisites

- Azure subscription with AI Foundry Hub deployed (sprkspaarkedev-aif-hub)
- Dataverse dev environment (spaarkedev1.crm.dynamics.com)
- PAC CLI authenticated to dev environment
- BFF API deployed to Azure App Service

### External Dependencies

- Azure OpenAI service (spaarke-openai-dev)
- Azure AI Search service (spaarke-search-dev)
- Azure Document Intelligence (spaarke-docintel-dev)

## Existing Implementation (DO NOT RECREATE)

> **CRITICAL**: The following files already exist and are COMPLETE. Tasks should VERIFY functionality, not recreate code.
> See [CODE-INVENTORY.md](CODE-INVENTORY.md) for full details.

### BFF API Endpoints (COMPLETE - Verify Only)

| File | Path | Status |
|------|------|--------|
| AnalysisEndpoints.cs | `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | COMPLETE |

**Endpoints implemented:**
- `POST /api/ai/analysis/execute` - SSE streaming
- `POST /api/ai/analysis/{id}/continue` - SSE streaming
- `POST /api/ai/analysis/{id}/save`
- `POST /api/ai/analysis/{id}/export`
- `GET /api/ai/analysis/{id}`

### BFF Services (COMPLETE - Verify Only)

| File | Path | Status |
|------|------|--------|
| AnalysisOrchestrationService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | COMPLETE |
| IAnalysisOrchestrationService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisOrchestrationService.cs` | COMPLETE |
| AnalysisContextBuilder.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` | COMPLETE |
| IAnalysisContextBuilder.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisContextBuilder.cs` | COMPLETE |
| ScopeResolverService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | COMPLETE |
| IScopeResolverService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | COMPLETE |
| WorkingDocumentService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/WorkingDocumentService.cs` | COMPLETE |
| IWorkingDocumentService.cs | `src/server/api/Sprk.Bff.Api/Services/Ai/IWorkingDocumentService.cs` | COMPLETE |

### BFF Models (COMPLETE - No Changes Needed)

| File | Path | Status |
|------|------|--------|
| AnalysisExecuteRequest.cs | `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisExecuteRequest.cs` | COMPLETE |
| AnalysisContinueRequest.cs | `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisContinueRequest.cs` | COMPLETE |
| AnalysisSaveRequest.cs | `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisSaveRequest.cs` | COMPLETE |
| AnalysisExportRequest.cs | `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisExportRequest.cs` | COMPLETE |
| AnalysisResult.cs | `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisResult.cs` | COMPLETE |
| AnalysisChunk.cs | `src/server/api/Sprk.Bff.Api/Models/Ai/AnalysisChunk.cs` | COMPLETE |

### Authorization Filter (COMPLETE - No Changes Needed)

| File | Path | Status |
|------|------|--------|
| AnalysisAuthorizationFilter.cs | `src/server/api/Sprk.Bff.Api/Api/Filters/AnalysisAuthorizationFilter.cs` | COMPLETE |

### Configuration (COMPLETE - No Changes Needed)

| File | Path | Status |
|------|------|--------|
| AnalysisOptions.cs | `src/server/api/Sprk.Bff.Api/Configuration/AnalysisOptions.cs` | COMPLETE |

### Unit Tests (COMPLETE - May Need Updates)

| File | Path | Status |
|------|------|--------|
| AnalysisEndpointsTests.cs | `tests/unit/Sprk.Bff.Api.Tests/Api/Ai/AnalysisEndpointsTests.cs` | COMPLETE |
| AnalysisAuthorizationFilterTests.cs | `tests/unit/Sprk.Bff.Api.Tests/Filters/AnalysisAuthorizationFilterTests.cs` | COMPLETE |
| AnalysisOrchestrationServiceTests.cs | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisOrchestrationServiceTests.cs` | COMPLETE |
| AnalysisContextBuilderTests.cs | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/AnalysisContextBuilderTests.cs` | COMPLETE |

### Infrastructure (PARTIAL - Verify and Complete)

| File | Path | Status |
|------|------|--------|
| ai-foundry.bicepparam | `infrastructure/bicep/ai-foundry.bicepparam` | EXISTS |
| AI Foundry Hub | Azure: sprkspaarkedev-aif-hub | DEPLOYED |
| AI Foundry Project | Azure: sprkspaarkedev-aif-proj | DEPLOYED |
| Prompt flows | `infrastructure/ai-foundry/prompt-flows/` | TEMPLATE ONLY |

## Dataverse Entities (STATUS UNKNOWN - Verify in Portal)

These entities are referenced in existing code but need verification in Dataverse:

| Entity | Logical Name | Action Required |
|--------|--------------|-----------------|
| Analysis | sprk_analysis | VERIFY exists, CREATE if missing |
| Analysis Action | sprk_analysisaction | VERIFY exists, CREATE if missing |
| Analysis Skill | sprk_analysisskill | VERIFY exists, CREATE if missing |
| Analysis Knowledge | sprk_analysisknowledge | VERIFY exists, CREATE if missing |
| Knowledge Deployment | sprk_knowledgedeployment | VERIFY exists, CREATE if missing |
| Analysis Tool | sprk_analysistool | VERIFY exists, CREATE if missing |
| Analysis Playbook | sprk_analysisplaybook | VERIFY exists, CREATE if missing |
| Analysis Working Version | sprk_analysisworkingversion | VERIFY exists, CREATE if missing |
| Analysis Email Metadata | sprk_analysisemailmetadata | VERIFY exists, CREATE if missing |
| Analysis Chat Message | sprk_analysischatmessage | VERIFY exists, CREATE if missing |

## Task Type Guidelines

When generating tasks, use these guidelines:

| Existing Status | Task Type | Task Action |
|-----------------|-----------|-------------|
| COMPLETE | Verify | Test existing code works, no code changes |
| EXISTS | Verify + Complete | Check current state, finish if incomplete |
| TEMPLATE ONLY | Complete | Finish implementation from template |
| STATUS UNKNOWN | Verify + Create | Check if exists, create if missing |
| (not listed) | Create | New implementation needed |

## Questions/Clarifications

- [ ] Are the Dataverse entities already created in dev? (Critical - determines if tasks 010-021 needed)
- [ ] Are the environment variables already in the Dataverse solution?
- [ ] What is the current state of AI Foundry prompt flows? (template vs. working)

---

*AI-optimized specification. Original: README.md (v2.0)*
