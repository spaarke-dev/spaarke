# Task Index — jps-server-rollout

> **Total Tasks**: 29
> **Status**: In Progress

## Task Registry

### Phase 1: Scope Resolution Completion

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 001 | Implement ResolveScopesAsync with Parallel Dataverse Queries | 🔲 | bff-api, api | none | 2h |
| 002 | Unit Tests for ResolveScopesAsync | 🔲 | testing | 001 | 1h |
| 003 | Verify Orchestration Scope Flow to Handler Context | 🔲 | bff-api | 001 | 1h |

### Phase 2: Named $ref Resolution

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 010 | Create JpsRefResolver Static Utility | 🔲 | bff-api, api | none | 2h |
| 011 | Add Name-Based Scope Lookups (GetKnowledgeByNameAsync/GetSkillByNameAsync) | 🔲 | bff-api, api | 001 | 2h |
| 012 | Extend ToolExecutionContext with AdditionalKnowledge/Skills | 🔲 | bff-api | none | 1h |
| 013 | Wire Ref Resolution in AiAnalysisNodeExecutor | 🔲 | bff-api, api | 010, 011, 012 | 2h |
| 014 | Wire GenericAnalysisHandler to Pass Resolved Refs | 🔲 | bff-api | 012, 013 | 1h |
| 015 | Unit Tests for JpsRefResolver | 🔲 | testing | 010 | 1h |
| 016 | Integration Test — Ref Resolution End-to-End | 🔲 | testing | 013, 014 | 1h |

### Phase 3: Template Parameters (parallel with Phase 2)

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 020 | Extract Template Parameters from ConfigJson | 🔲 | bff-api, api | 001 | 1h |
| 021 | Add TemplateParameters to ToolExecutionContext | 🔲 | bff-api | none | 0.5h |
| 022 | Wire GenericAnalysisHandler to Pass Template Params | 🔲 | bff-api | 021 | 0.5h |
| 023 | Verify/Implement Template Substitution in Renderer + Tests | 🔲 | bff-api, testing | 020, 022 | 2h |

### Phase 4: Handler Migration

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 030 | Migrate ClauseAnalyzerHandler → GenericAnalysisHandler | 🔲 | bff-api, refactor | 014, 022 | 2h |
| 031 | Migrate DateExtractorHandler → GenericAnalysisHandler | 🔲 | bff-api, refactor | 014, 022 | 2h |
| 032 | Migrate EntityExtractorHandler → GenericAnalysisHandler | 🔲 | bff-api, refactor | 014, 022 | 2h |
| 033 | Migrate RiskDetectorHandler → GenericAnalysisHandler | 🔲 | bff-api, refactor | 014, 022 | 2h |
| 034 | Migrate FinancialCalculatorHandler → GenericAnalysisHandler | 🔲 | bff-api, refactor | 014, 022 | 2h |
| 035 | Convert DocumentClassifierHandler (Preserve RAG) | 🔲 | bff-api, refactor | 014, 022 | 3h |
| 036 | Convert SummaryHandler (Preserve Config) | 🔲 | bff-api, refactor | 014, 022 | 3h |
| 037 | Convert SemanticSearchToolHandler (Preserve Search) | 🔲 | bff-api, refactor | 014, 022 | 3h |
| 038 | Convert ClauseComparisonHandler (Preserve Multi-Doc) | 🔲 | bff-api, refactor | 014, 022 | 3h |

### Phase 5: Data Seeding + E2E Validation

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 050 | Create JPS Seeding Script for Dataverse Actions | 🔲 | deploy | 030-034 | 2h |
| 051 | Integration Test: Full JPS Pipeline | 🔲 | testing | 038 | 3h |
| 052 | Test $choices Resolution with Downstream Nodes | 🔲 | testing | 051 | 2h |
| 053 | Test Override Merge End-to-End | 🔲 | testing | 051 | 1h |
| 054 | Backward Compatibility Verification | 🔲 | testing | 038 | 1h |
| 055 | Documentation Update + Project Wrap-Up | 🔲 | documentation | all | 2h |

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 29 |
| Completed | 0 |
| In Progress | 0 |
| Pending | 29 |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|-------------|-------|
| A | Phase 2 (010-016) + Phase 3 (020-023) | Phase 1 complete | $ref resolution and template params independent |
| B | 030, 031, 032, 033, 034 | 014 + 022 | Five consolidation tasks — independent handlers |
| C | 035, 036, 037, 038 | 014 + 022 | Four thin-wrapper tasks — independent handlers |
| D | 051, 052, 053, 054 | Phase 4 complete | Integration tests independent |

## Dependency Graph

```
001 ──→ 002
  │ ──→ 003
  │ ──→ 011 ──→ 013 ──→ 014 ──→ 030-038 ──→ 050-055
  │                ↑       ↑
010 ──→ 015    012 ─┘       │
  └────────────────────────→ 013
                             │
020 ──→ 023 ──────────────→ 022 ──→ 030-038
021 ──→ 022 ──────────────────┘
```
