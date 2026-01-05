# AI Document Intelligence R4 - Task Index

> **Status**: Generated
> **Created**: 2026-01-04
> **Total Tasks**: 28

---

## Quick Navigation

| Phase | Tasks | Status |
|-------|-------|--------|
| [Phase 1: Entity Validation](#phase-1-dataverse-entity-validation) | 001 | 1/1 ✅ |
| [Phase 2: Seed Data](#phase-2-seed-data-population) | 010-015 | 6/6 ✅ |
| [Phase 3: Tool Handlers](#phase-3-tool-handler-implementation) | 020-029 | 10/10 ✅ |
| [Phase 4: Service Layer](#phase-4-service-layer-extension) | 030-032 | 3/3 ✅ |
| [Phase 5: Playbook Assembly](#phase-5-playbook-assembly) | 040-042 | 3/3 ✅ |
| [Phase 6: UI/PCF](#phase-6-uipcf-enhancement) | 050-053 | 0/4 |
| [Wrap-up](#wrap-up) | 090 | 0/1 |

---

## Phase 1: Dataverse Entity Validation

| Task | Name | Status | Priority |
|------|------|--------|----------|
| ✅ 001 | [Validate Dataverse entity fields](001-validate-dataverse-entities.poml) | Complete | P0 |

---

## Phase 2: Seed Data Population

| Task | Name | Status | Priority |
|------|------|--------|----------|
| ✅ 010 | [Populate type lookup tables](010-populate-type-lookups.poml) | Complete | P0 |
| ✅ 011 | [Create Action seed data](011-create-action-seed-data.poml) | Complete | P0 |
| ✅ 012 | [Create Tool seed data](012-create-tool-seed-data.poml) | Complete | P0 |
| ✅ 013 | [Create Knowledge seed data](013-create-knowledge-seed-data.poml) | Complete | P0 |
| ✅ 014 | [Create Skill seed data](014-create-skill-seed-data.poml) | Complete | P0 |
| ✅ 015 | [Deploy seed data to Dataverse](015-deploy-seed-data.poml) | Complete | P0 |

---

## Phase 3: Tool Handler Implementation

| Task | Name | Status | Priority |
|------|------|--------|----------|
| ✅ 020 | [Implement SummaryHandler](020-implement-summary-handler.poml) | Complete | P0 |
| ✅ 021 | [Write SummaryHandler tests](021-test-summary-handler.poml) | Complete | P0 |
| ✅ 022 | [Implement RiskDetectorHandler](022-implement-riskdetector-handler.poml) | Complete | P0 |
| ✅ 023 | [Write RiskDetectorHandler tests](023-test-riskdetector-handler.poml) | Complete | P0 |
| ✅ 024 | [Implement ClauseComparisonHandler](024-implement-clausecomparison-handler.poml) | Complete | P0 |
| ✅ 025 | [Write ClauseComparisonHandler tests](025-test-clausecomparison-handler.poml) | Complete | P0 |
| ✅ 026 | [Implement DateExtractorHandler](026-implement-dateextractor-handler.poml) | Complete | P0 |
| ✅ 027 | [Write DateExtractorHandler tests](027-test-dateextractor-handler.poml) | Complete | P0 |
| ✅ 028 | [Implement FinancialCalculatorHandler](028-implement-financialcalculator-handler.poml) | Complete | P0 |
| ✅ 029 | [Write FinancialCalculatorHandler tests](029-test-financialcalculator-handler.poml) | Complete | P0 |

---

## Phase 4: Service Layer Extension

| Task | Name | Status | Priority |
|------|------|--------|----------|
| ✅ 030 | [Create scope listing endpoints](030-create-scope-endpoints.poml) | Complete | P1 |
| ✅ 031 | [Implement ExecutePlaybookAsync](031-implement-execute-playbook.poml) | Complete | P1 |
| ✅ 032 | [Add authorization filters](032-add-authorization-filters.poml) | Complete | P1 |

---

## Phase 5: Playbook Assembly

| Task | Name | Status | Priority |
|------|------|--------|----------|
| ✅ 040 | [Create MVP playbooks in Dataverse](040-create-mvp-playbooks.poml) | Complete | P1 |
| ✅ 041 | [Link scopes to playbooks](041-link-scopes-to-playbooks.poml) | Complete | P1 |
| ✅ 042 | [Validate playbook configurations](042-validate-playbook-configs.poml) | Complete | P1 |

---

## Phase 6: UI/PCF Enhancement

| Task | Name | Status | Priority |
|------|------|--------|----------|
| 🔲 050 | [Enhance PlaybookSelector component](050-enhance-playbook-selector.poml) | Not Started | P2 |
| 🔲 051 | [Integrate playbook selection in AnalysisWorkspace](051-integrate-playbook-workspace.poml) | Not Started | P2 |
| 🔲 052 | [Display playbook info during analysis](052-display-playbook-info.poml) | Not Started | P2 |
| 🔲 053 | [Test dark mode support](053-test-dark-mode.poml) | Not Started | P2 |

---

## Wrap-up

| Task | Name | Status | Priority |
|------|------|--------|----------|
| 🔲 090 | [Project wrap-up](090-project-wrap-up.poml) | Not Started | P1 |

---

## Legend

| Symbol | Meaning |
|--------|---------|
| 🔲 | Not Started |
| 🔄 | In Progress |
| ✅ | Completed |
| ⏸️ | Blocked |

---

## Dependencies

```
Phase 1 (Entity Validation)
    └─→ Phase 2 (Seed Data) ─→ Phase 5 (Playbook Assembly)
                │
                └─→ Phase 3 (Tool Handlers) ─→ Phase 4 (Service Layer)
                                                    │
                                                    └─→ Phase 6 (UI/PCF)
```

---

## Critical Path

1. **001** → Entity validation (blocks all other work)
2. **020-029** → Tool handlers (blocks service layer)
3. **030-031** → Service endpoints (blocks playbook execution)
4. **040-042** → Playbook assembly (blocks UI integration)

---

*Task index generated: 2026-01-04*
