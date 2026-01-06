# AI Document Intelligence R4 - Playbook Scope Implementation

> **Status**: In Progress
> **Started**: 2026-01-04
> **Branch**: `feature/ai-document-intelligence-r4`

---

## Quick Links

| Resource | Path |
|----------|------|
| Implementation Plan | [plan.md](plan.md) |
| Task Index | [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) |
| Design Spec | [spec.md](spec.md) |
| Full Design | [AI-ANALYSIS-IMPLEMENTATION-DESIGN.md](AI-ANALYSIS-IMPLEMENTATION-DESIGN.md) |
| Entity Model | [ai-dataverse-entity-model.md](ai-dataverse-entity-model.md) |
| R3 Code Inventory | [CODE-INVENTORY.md](CODE-INVENTORY.md) |

---

## Overview

This project implements the Playbook scope system to enable **no-code AI workflow composition** for document analysis. Domain experts will be able to assemble reusable Actions, Skills, Knowledge, and Tools into Playbooks without engineering involvement. All configurations are stored in Dataverse, extending the substantial R3 infrastructure.

**Project Lineage**: R1 (foundation) → R2 (refactoring) → R3 (RAG/Playbooks) → **R4 (Scope implementation)**

---

## Problem Statement

The R3 implementation established the core playbook framework and RAG foundation, but playbooks cannot yet be dynamically composed from reusable scopes. Domain experts need a way to configure AI analysis workflows without code changes, combining:
- **Actions**: System prompts and handler references
- **Skills**: Prompt fragments with categories
- **Knowledge**: Inline content or RAG index references
- **Tools**: Handler classes with JSON configurations

Currently, adding new analysis capabilities requires engineering involvement.

---

## Proposed Solution

Implement the scope system infrastructure:
1. **Validate Dataverse entities** match C# models (N:N relationships already verified)
2. **Populate seed data** for type lookups and scope records (8 Actions, 8 Tools, 10 Knowledge, 10 Skills)
3. **Implement 5 new tool handlers** (Summary, RiskDetector, ClauseComparison, DateExtractor, FinancialCalculator)
4. **Extend service layer** with scope listing endpoints and `ExecutePlaybookAsync`
5. **Create MVP playbooks** (Quick Review, Full Contract, Risk Scan)
6. **Enhance PCF controls** for playbook selection and display

---

## Scope

### In Scope

- Dataverse entity field validation (Phase 1)
- Seed data population for type tables and scope records (Phase 2)
- 5 new tool handler implementations with unit tests (Phase 3)
- Scope listing API endpoints (`/api/ai/scopes/*`) (Phase 4)
- `ExecutePlaybookAsync` method extension (Phase 4)
- 3 MVP playbooks with N:N scope linkage (Phase 5)
- PlaybookSelector UI enhancement (Phase 6)
- AnalysisWorkspace playbook integration (Phase 6)

### Out of Scope

- PlaybookBuilder UI (Future - not MVP)
- Playbook versioning (deferred to R5)
- Team sharing via Dataverse security roles
- Customer-created playbooks (enabled but not marketed in R4)

---

## Graduation Criteria

| # | Criterion | Verification |
|---|-----------|--------------|
| 1 | All 5 new tool handlers implemented and tested | Unit tests pass, handlers registered in DI |
| 2 | Type lookup tables populated in Dataverse | Query returns expected categories |
| 3 | Action/Tool/Knowledge/Skill seed data created | Query returns expected counts (8/8/10/10) |
| 4 | MVP playbooks created and functional | Execute each playbook, verify output |
| 5 | Scope listing endpoints return data | API tests return paginated results |
| 6 | `ExecutePlaybookAsync` works end-to-end | Integration test: select → analyze → verify |
| 7 | PlaybookSelector shows available playbooks | Manual test in model-driven app |
| 8 | AnalysisWorkspace displays playbook name | Manual test during analysis |
| 9 | Dark mode works for PCF changes | Visual test in dark mode |
| 10 | All unit tests pass | `dotnet test` green |

---

## Technical Stack

| Component | Technology |
|-----------|------------|
| Backend | .NET 8 Minimal API (Sprk.Bff.Api) |
| Frontend | PCF Controls (TypeScript/React/Fluent v9) |
| Data | Dataverse entities with N:N relationships |
| AI | Azure OpenAI (gpt-4o-mini, text-embedding-3-small) |
| Testing | xUnit, Moq |

---

## Applicable ADRs

| ADR | Constraint |
|-----|------------|
| ADR-013 | AI Architecture - tool handlers in `Services/Ai/Tools/` |
| ADR-001 | Minimal API pattern for scope endpoints |
| ADR-006 | PCF over webresources - UI enhancements PCF-only |
| ADR-008 | Authorization endpoint filters for scope endpoints |
| ADR-010 | DI minimalism - focused handler services |
| ADR-021 | Fluent UI v9 Design System - dark mode ready |

---

## Related Projects

- [AI Document Intelligence R3](../ai-document-intelligence-r3/) - RAG, Playbooks, Export, Monitoring
- [AI Document Intelligence R2](../ai-document-intelligence-r2/) - Refactoring
- [AI Document Intelligence R1](../ai-document-intelligence-r1/) - Foundation

---

*Last updated: 2026-01-04*
