# AI Document Intelligence R4 - AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-01-04
> **Source**: AI-ANALYSIS-IMPLEMENTATION-DESIGN.md (v1.2)
> **Lineage**: R1 (foundation) → R2 (refactoring) → R3 (RAG/Playbooks) → **R4 (Scope implementation)**

---

## Executive Summary

Implement the Playbook scope system to enable no-code AI workflow composition for document analysis. This project creates reusable Actions, Skills, Knowledge, and Tools that domain experts can assemble into Playbooks without engineering involvement. All scope configurations are stored in Dataverse, and the implementation extends the substantial R3 infrastructure.

---

## Scope

### In Scope

**Phase 1: Dataverse Entity Validation**
- Verify entity fields match C# models
- Confirm N:N relationships exist (already verified: ✅)
- Validate type lookup tables have data

**Phase 2: Seed Data Population**
- Populate type lookup tables (ActionType, SkillType, KnowledgeType, ToolType)
- Create 8 Action records (ACT-001 through ACT-008)
- Create 8 Tool records (TL-001 through TL-008)
- Create 10 Knowledge records (KNW-001 through KNW-010)
- Create 10 Skill records (SKL-001 through SKL-010)

**Phase 3: Tool Handler Implementation**
- Implement 5 new tool handlers:
  - `SummaryHandler.cs` - Document summarization
  - `RiskDetectorHandler.cs` - Risk identification
  - `ClauseComparisonHandler.cs` - Compare to standard terms
  - `DateExtractorHandler.cs` - Date extraction/normalization
  - `FinancialCalculatorHandler.cs` - Financial calculations
- Unit test each handler

**Phase 4: Service Layer Extension**
- Add scope listing endpoints (`/api/ai/scopes/*`)
- Extend `AnalysisOrchestrationService` with `ExecutePlaybookAsync` method

**Phase 5: Playbook Assembly**
- Create MVP playbooks (PB-001, PB-002, PB-010)
- Link scopes to playbooks via N:N relationships
- Validate playbook configurations

**Phase 6: UI/PCF Enhancement**
- Enhance `PlaybookSelector` in AnalysisBuilder
- Integrate playbook selection into AnalysisWorkspace
- Show playbook name during/after analysis

### Out of Scope

- PlaybookBuilder UI (Future - not MVP)
- Playbook versioning (deferred decision)
- Team sharing via Dataverse security roles (future consideration)
- Customer-created playbooks (enabled but not marketed in R4)

### Affected Areas

| Area | Path | Description |
|------|------|-------------|
| BFF API Services | `src/server/api/Sprk.Bff.Api/Services/Ai/` | Tool handlers, service extensions |
| BFF API Endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/` | Scope listing endpoints |
| PCF Controls | `src/client/pcf/AnalysisBuilder/` | PlaybookSelector enhancement |
| PCF Controls | `src/client/pcf/AnalysisWorkspace/` | Playbook integration |
| Dataverse | `infrastructure/dataverse/solutions/` | Seed data scripts |
| Unit Tests | `tests/unit/Sprk.Bff.Api.Tests/` | Handler tests |

---

## Requirements

### Functional Requirements

| ID | Requirement | Acceptance Criteria |
|----|-------------|---------------------|
| FR-01 | Create 5 new tool handlers implementing `IAiToolHandler` | Each handler returns structured `ToolResult` with success/error and execution time |
| FR-02 | Populate type lookup tables with categories | Each table has 4-5 categories with sort order |
| FR-03 | Create Action seed data (8 records) | Actions exist with Name, Description, SystemPrompt, Handler reference |
| FR-04 | Create Tool seed data (8 records) | Tools exist with HandlerClass, JSON Configuration |
| FR-05 | Create Knowledge seed data (10 records) | Knowledge records with Type (Inline/RagIndex), Content or Deployment reference |
| FR-06 | Create Skill seed data (10 records) | Skills with PromptFragment and Category assignment |
| FR-07 | Add scope listing API endpoints | `GET /api/ai/scopes/{skills,knowledge,tools,actions}` return paginated lists |
| FR-08 | Extend AnalysisOrchestrationService | `ExecutePlaybookAsync` loads playbook, resolves scopes, executes tools |
| FR-09 | Create MVP playbooks (3) | PB-001 Quick Review, PB-002 Full Contract, PB-010 Risk Scan |
| FR-10 | Link scopes to playbooks via N:N | Each playbook has associated Skills, Knowledge, Tools, Actions |
| FR-11 | Validate playbook configurations | Playbooks must have at least one Skill; tools must have handlers |
| FR-12 | Enhance PlaybookSelector UI | Users can select playbooks based on document type |
| FR-13 | Show playbook info in AnalysisWorkspace | Display playbook name during/after analysis |

### Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-01 | Tool handlers must complete within 30 seconds for standard documents |
| NFR-02 | All configurations stored in Dataverse (not code) |
| NFR-03 | Handler implementations must be stateless for scalability |
| NFR-04 | PCF enhancements must support dark mode (ADR-021) |
| NFR-05 | All API endpoints require authorization (ADR-008) |

---

## Technical Constraints

### Applicable ADRs

| ADR | Requirement | Impact on R4 |
|-----|-------------|--------------|
| **ADR-013** | AI Architecture - extend BFF | Tool handlers live in `Services/Ai/Tools/` |
| **ADR-001** | Minimal API pattern | Scope endpoints use endpoint groups with filters |
| **ADR-006** | PCF over webresources | UI enhancements are PCF-only |
| **ADR-008** | Authorization endpoint filters | Scope endpoints use `Add*AuthorizationFilter()` |
| **ADR-010** | DI minimalism | Small, focused handler services |
| **ADR-021** | Fluent UI v9 Design System | PlaybookSelector uses Fluent v9, dark mode ready |

### MUST Rules

- ✅ MUST implement tool handlers as `IAiToolHandler` (existing pattern)
- ✅ MUST store all scope configurations in Dataverse entities
- ✅ MUST use existing `ToolHandlerRegistry` for handler discovery
- ✅ MUST follow existing endpoint patterns from `PlaybookEndpoints.cs`
- ✅ MUST use Fluent UI v9 for all PCF UI changes
- ✅ MUST support dark mode in PCF enhancements
- ❌ MUST NOT recreate existing services (PlaybookService, ScopeResolverService exist)
- ❌ MUST NOT add business logic to ribbon/command bar scripts (ADR-006)
- ❌ MUST NOT hard-code colors in PCF controls (ADR-021)

### Existing Patterns to Follow

| Pattern | Reference |
|---------|-----------|
| Tool Handler | `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/EntityExtractorHandler.cs` |
| API Endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs` |
| Handler Registry | `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerRegistry.cs` |
| PCF Control | `src/client/pcf/AnalysisWorkspace/` |
| Unit Tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EntityExtractorHandlerTests.cs` |

### Existing Code (DO NOT RECREATE)

| File | Purpose | Status |
|------|---------|--------|
| `PlaybookService.cs` | Playbook CRUD | ✅ Complete |
| `ScopeResolverService.cs` | Load scopes by ID/playbook | ✅ Complete |
| `AnalysisOrchestrationService.cs` | Orchestration (needs extension) | ✅ Extend |
| `ToolHandlerRegistry.cs` | Handler discovery | ✅ Complete |
| `EntityExtractorHandler.cs` | Entity extraction | ✅ Complete |
| `ClauseAnalyzerHandler.cs` | Clause analysis | ✅ Complete |
| `DocumentClassifierHandler.cs` | Classification | ✅ Complete |

---

## Success Criteria

| # | Criterion | Verification Method |
|---|-----------|---------------------|
| 1 | [ ] All 5 new tool handlers implemented and tested | Unit tests pass, handlers registered in DI |
| 2 | [ ] Type lookup tables populated in Dataverse | Query returns expected categories |
| 3 | [ ] Action/Tool/Knowledge/Skill seed data created | Query returns expected record counts (8/8/10/10) |
| 4 | [ ] MVP playbooks created and functional | Execute each playbook, verify output |
| 5 | [ ] Scope listing endpoints return data | API tests return paginated results |
| 6 | [ ] `ExecutePlaybookAsync` works end-to-end | Integration test: select playbook → analyze → verify result |
| 7 | [ ] PlaybookSelector shows available playbooks | Manual test in Dataverse model-driven app |
| 8 | [ ] AnalysisWorkspace displays playbook name | Manual test during analysis |
| 9 | [ ] Dark mode works for PCF changes | Visual test in dark mode |
| 10 | [ ] All unit tests pass | `dotnet test` green |

---

## Dependencies

### Prerequisites

- R3 infrastructure complete (RAG, Playbooks, Export) ✅
- Dataverse entities exist (verified in solution.xml) ✅
- N:N relationships exist (verified in customizations.xml) ✅

### External Dependencies

- Azure OpenAI for handler AI operations
- Dataverse Web API for seed data population
- No new Azure resources required

---

## Implementation Phases

| Phase | Focus | Priority |
|-------|-------|----------|
| 1 | Dataverse Entity Validation | P0 (1 task) |
| 2 | Seed Data Population | P0 (5-6 tasks) |
| 3 | Tool Handler Implementation | P0 (5-8 tasks) |
| 4 | Service Layer Extension | P1 (3-4 tasks) |
| 5 | Playbook Assembly | P1 (3-4 tasks) |
| 6 | UI/PCF Enhancement | P2 (4-6 tasks) |

**MVP Priority**: Phases 1-3 + P1 from Phase 5 (PB-001 Quick Review working end-to-end)

---

## Questions/Clarifications Needed

| # | Question | Impact | Recommendation |
|---|----------|--------|----------------|
| 1 | Should Skills have N:N with Actions? | Data model | Defer - use PromptFragment pattern for now |
| 2 | How to handle playbook versioning? | Future feature | Defer to R5 - create new record for now |
| 3 | Sharing permissions model? | Team collaboration | Use existing Owner + IsPublic flag |
| 4 | Who creates/maintains RAG indexes? | Knowledge sources | Admin responsibility, document in R4 |

---

## Related Documents

- [AI-ANALYSIS-IMPLEMENTATION-DESIGN.md](AI-ANALYSIS-IMPLEMENTATION-DESIGN.md) - Full design document
- [ai-dataverse-entity-model.md](ai-dataverse-entity-model.md) - Entity schema reference
- [AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md](../../docs/architecture/AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md) - Playbook recipes
- [CODE-INVENTORY.md](../ai-document-intelligence-r3/CODE-INVENTORY.md) - R3 code inventory

---

*AI-optimized specification. Original design: AI-ANALYSIS-IMPLEMENTATION-DESIGN.md v1.2*
