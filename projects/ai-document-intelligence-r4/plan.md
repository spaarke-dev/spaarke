# AI Document Intelligence R4 - Implementation Plan

> **Project**: AI Document Intelligence R4 - Playbook Scope Implementation
> **Status**: Planning Complete
> **Created**: 2026-01-04

---

## Executive Summary

### Purpose

Implement the Playbook scope system enabling no-code AI workflow composition for document analysis. This project creates reusable Actions, Skills, Knowledge, and Tools that domain experts can assemble into Playbooks without engineering involvement.

### Scope

- 6 implementation phases
- ~25-30 estimated tasks
- Extends R3 infrastructure (do not recreate existing services)
- All configurations stored in Dataverse

### Dependencies

- R3 infrastructure complete (RAG, Playbooks, Export) ✅
- Dataverse entities exist (verified) ✅
- N:N relationships exist (verified) ✅

---

## Architecture Context

### Discovered Resources

**Applicable ADRs**:
| ADR | Requirement | Impact |
|-----|-------------|--------|
| ADR-013 | AI Architecture | Tool handlers in `Services/Ai/Tools/` |
| ADR-001 | Minimal API pattern | Scope endpoints use endpoint groups |
| ADR-006 | PCF over webresources | UI enhancements are PCF-only |
| ADR-008 | Authorization filters | Scope endpoints use filters |
| ADR-010 | DI minimalism | Small, focused handler services |
| ADR-021 | Fluent UI v9 | Dark mode ready, semantic tokens |

**Existing Code (DO NOT RECREATE)**:
| File | Purpose |
|------|---------|
| `PlaybookService.cs` | Playbook CRUD |
| `ScopeResolverService.cs` | Load scopes by ID/playbook |
| `AnalysisOrchestrationService.cs` | Orchestration (extend only) |
| `ToolHandlerRegistry.cs` | Handler discovery |
| `EntityExtractorHandler.cs` | Entity extraction pattern |
| `ClauseAnalyzerHandler.cs` | Clause analysis pattern |
| `DocumentClassifierHandler.cs` | Classification pattern |

**Pattern References**:
| Pattern | Location |
|---------|----------|
| Tool Handler | `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/EntityExtractorHandler.cs` |
| API Endpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/PlaybookEndpoints.cs` |
| PCF Control | `src/client/pcf/AnalysisWorkspace/` |
| Unit Tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/EntityExtractorHandlerTests.cs` |

**Applicable Skills**:
- `dataverse-deploy` - For seed data deployment
- `code-review` - Before commits
- `adr-check` - Validate ADR compliance

---

## Phase Breakdown

### Phase 1: Dataverse Entity Validation (P0)
**Priority**: Critical - validates data model before implementation
**Estimated Tasks**: 1

| Objective | Description |
|-----------|-------------|
| Validate entity fields | Verify Dataverse entities match C# model expectations |
| Confirm N:N relationships | Already verified ✅ |
| Check type lookup tables | Ensure tables exist and are queryable |

**Key Deliverables**:
- Entity validation report
- Field mapping documentation
- Type table structure confirmation

---

### Phase 2: Seed Data Population (P0)
**Priority**: Critical - provides test data for handler development
**Estimated Tasks**: 5-6

| Objective | Description |
|-----------|-------------|
| Populate ActionType lookup | 4-5 categories with sort order |
| Populate SkillType lookup | 4-5 categories with sort order |
| Populate KnowledgeType lookup | 4-5 categories with sort order |
| Populate ToolType lookup | 4-5 categories with sort order |
| Create Action seed data | 8 Action records (ACT-001 through ACT-008) |
| Create Tool seed data | 8 Tool records (TL-001 through TL-008) |
| Create Knowledge seed data | 10 Knowledge records (KNW-001 through KNW-010) |
| Create Skill seed data | 10 Skill records (SKL-001 through SKL-010) |

**Key Deliverables**:
- Seed data scripts (PowerShell or JSON)
- Type lookup population
- 8 Actions, 8 Tools, 10 Knowledge, 10 Skills created

---

### Phase 3: Tool Handler Implementation (P0)
**Priority**: Critical - core functionality
**Estimated Tasks**: 8-10

| Objective | Description |
|-----------|-------------|
| Implement SummaryHandler | Document summarization with configurable length |
| Implement RiskDetectorHandler | Risk identification and categorization |
| Implement ClauseComparisonHandler | Compare document clauses to standard terms |
| Implement DateExtractorHandler | Date extraction and normalization |
| Implement FinancialCalculatorHandler | Financial calculations and summaries |
| Unit test each handler | Following EntityExtractorHandlerTests pattern |
| Register handlers in DI | Add to ToolHandlerRegistry |

**Key Deliverables**:
- 5 new handler classes implementing `IAnalysisToolHandler`
- Unit tests for each handler
- DI registration code

---

### Phase 4: Service Layer Extension (P1)
**Priority**: High - enables API access
**Estimated Tasks**: 3-4

| Objective | Description |
|-----------|-------------|
| Add scope listing endpoints | `GET /api/ai/scopes/{skills,knowledge,tools,actions}` |
| Extend AnalysisOrchestrationService | Add `ExecutePlaybookAsync` method |
| Add endpoint authorization | Use `Add*AuthorizationFilter()` patterns |
| Add pagination support | Follow PlaybookEndpoints pattern |

**Key Deliverables**:
- `ScopeEndpoints.cs` with listing endpoints
- Extended `AnalysisOrchestrationService`
- Authorization filters applied

---

### Phase 5: Playbook Assembly (P1)
**Priority**: High - creates usable playbooks
**Estimated Tasks**: 3-4

| Objective | Description |
|-----------|-------------|
| Create PB-001 Quick Review | Basic document review playbook |
| Create PB-002 Full Contract | Comprehensive contract analysis |
| Create PB-010 Risk Scan | Risk-focused analysis playbook |
| Link scopes via N:N | Associate Skills, Knowledge, Tools, Actions |
| Validate configurations | Ensure playbooks have required scopes |

**Key Deliverables**:
- 3 MVP playbooks in Dataverse
- N:N relationship data
- Playbook validation logic

---

### Phase 6: UI/PCF Enhancement (P2)
**Priority**: Medium - improves user experience
**Estimated Tasks**: 4-6

| Objective | Description |
|-----------|-------------|
| Enhance PlaybookSelector | Load and display available playbooks |
| Integrate with AnalysisBuilder | Connect selector to workflow |
| Show playbook in AnalysisWorkspace | Display name during/after analysis |
| Support dark mode | Use Fluent v9 semantic tokens |
| Test in model-driven app | Verify rendering in Dataverse forms |

**Key Deliverables**:
- Enhanced `PlaybookSelector` component
- Updated `AnalysisWorkspace` with playbook display
- Dark mode verified

---

## Risk Assessment

| Risk | Likelihood | Impact | Mitigation |
|------|------------|--------|------------|
| Entity schema mismatch | Low | High | Phase 1 validates before implementation |
| Handler complexity | Medium | Medium | Follow existing handler patterns |
| N:N relationship issues | Low | High | Already verified in R3 |
| PCF dark mode issues | Low | Medium | Use semantic tokens only |

---

## Success Metrics

| Metric | Target |
|--------|--------|
| Unit test coverage | 80%+ for new handlers |
| Handler execution time | <30s for standard documents |
| Scope endpoint latency | <500ms for paginated lists |
| PCF bundle size | <5MB |

---

## References

- [spec.md](spec.md) - Full specification
- [AI-ANALYSIS-IMPLEMENTATION-DESIGN.md](AI-ANALYSIS-IMPLEMENTATION-DESIGN.md) - Detailed design
- [ai-dataverse-entity-model.md](ai-dataverse-entity-model.md) - Entity schema
- [CODE-INVENTORY.md](CODE-INVENTORY.md) - R3 code reference

---

*Implementation plan generated: 2026-01-04*
