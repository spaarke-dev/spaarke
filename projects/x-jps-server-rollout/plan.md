# Implementation Plan — JPS Server Rollout

> **Total Tasks**: 29
> **Phases**: 5
> **Dependencies**: Phase 1 → (Phase 2 ∥ Phase 3) → Phase 4 → Phase 5

## Architecture Context

### Discovered Resources

**ADRs**: ADR-001, ADR-004, ADR-008, ADR-010, ADR-013, ADR-014, ADR-015, ADR-016
**Constraints**: `.claude/constraints/ai.md`, `api.md`, `testing.md`, `jobs.md`
**Patterns**: `analysis-scopes.md`, `service-registration.md`, `streaming-endpoints.md`, `unit-test-structure.md`, `mocking-patterns.md`
**Scripts**: `Deploy-BffApi.ps1`, `Test-SdapBffApi.ps1`

### Existing Infrastructure (Do Not Rebuild)

- PromptSchema models (`Services/Ai/Models/PromptSchema.cs`, 412 lines)
- PromptSchemaRenderer (`Services/Ai/PromptSchemaRenderer.cs`, 969 lines)
- GenericAnalysisHandler JPS integration
- PromptSchemaOverrideMerger (in AiAnalysisNodeExecutor)
- Format detection, JSON Schema generation, $choices resolution
- 76 passing tests
- 9 JPS conversion examples

---

## Phase 1: Scope Resolution Completion

**Goal**: Make `ResolveScopesAsync()` return real data.

| # | Task | Key File | Est |
|---|------|----------|-----|
| 001 | Implement ResolveScopesAsync with parallel Dataverse queries | `ScopeResolverService.cs:93-106` | 2h |
| 002 | Unit tests for ResolveScopesAsync | `tests/unit/` | 1h |
| 003 | Verify orchestration flow — scopes reach handler context | `AnalysisOrchestrationService.cs` | 1h |

---

## Phase 2: Named $ref Resolution

**Goal**: Wire `$ref: "knowledge:{name}"` to Dataverse lookups.
**Design**: Static JpsRefResolver + name-based IScopeResolverService methods.

| # | Task | Key File | Est |
|---|------|----------|-----|
| 010 | Create JpsRefResolver static utility | New: `Services/Ai/JpsRefResolver.cs` | 2h |
| 011 | Add GetKnowledgeByNameAsync/GetSkillByNameAsync | `IScopeResolverService.cs`, `ScopeResolverService.cs` | 2h |
| 012 | Extend ToolExecutionContext with AdditionalKnowledge/Skills | `ToolExecutionContext.cs` | 1h |
| 013 | Wire ref resolution in AiAnalysisNodeExecutor | `AiAnalysisNodeExecutor.cs:270-317` | 2h |
| 014 | Wire GenericAnalysisHandler to pass resolved refs | `GenericAnalysisHandler.cs:249-250` | 1h |
| 015 | Unit tests for JpsRefResolver | `tests/unit/` | 1h |
| 016 | Integration test — ACT-001 refs in rendered prompt | `tests/unit/` | 1h |

---

## Phase 3: Template Parameters (parallel with Phase 2)

**Goal**: Connect `{{param}}` substitution from ConfigJson.

| # | Task | Key File | Est |
|---|------|----------|-----|
| 020 | Extract templateParameters from ConfigJson | `AiAnalysisNodeExecutor.cs` | 1h |
| 021 | Add TemplateParameters to ToolExecutionContext | `ToolExecutionContext.cs` | 0.5h |
| 022 | Wire GenericAnalysisHandler to pass params | `GenericAnalysisHandler.cs:247` | 0.5h |
| 023 | Verify/implement substitution in PromptSchemaRenderer + tests | `PromptSchemaRenderer.cs` | 2h |

---

## Phase 4: Handler Migration

**Goal**: Migrate 9 handlers — consolidate or thin-wrap.

| # | Task | Approach | Est |
|---|------|----------|-----|
| 030 | Migrate ClauseAnalyzerHandler | Consolidate → GenericAnalysisHandler | 2h |
| 031 | Migrate DateExtractorHandler | Consolidate → GenericAnalysisHandler | 2h |
| 032 | Migrate EntityExtractorHandler | Consolidate → GenericAnalysisHandler | 2h |
| 033 | Migrate RiskDetectorHandler | Consolidate → GenericAnalysisHandler | 2h |
| 034 | Migrate FinancialCalculatorHandler | Consolidate → GenericAnalysisHandler | 2h |
| 035 | Convert DocumentClassifierHandler (preserve RAG) | Thin wrapper | 3h |
| 036 | Convert SummaryHandler (preserve config) | Thin wrapper | 3h |
| 037 | Convert SemanticSearchToolHandler (preserve search) | Thin wrapper | 3h |
| 038 | Convert ClauseComparisonHandler (preserve multi-doc) | Thin wrapper | 3h |

---

## Phase 5: Data Seeding + E2E Validation

| # | Task | Est |
|---|------|-----|
| 050 | Create JPS seeding script for Dataverse Actions | 2h |
| 051 | Integration test: full JPS pipeline | 3h |
| 052 | Test $choices with real downstream nodes | 2h |
| 053 | Test override merge end-to-end | 1h |
| 054 | Backward compatibility verification | 1h |
| 055 | Documentation update (AI architecture guide) | 2h |

---

## Dependency Graph

```
Phase 1 (Scope Resolution) [001-003]
    |
    +---> Phase 2 ($ref Resolution) [010-016] ---+
    |                                              |
    +---> Phase 3 (Template Params) [020-023] ---+--> Phase 4 (Handlers) [030-038]
                                                                |
                                                    Phase 5 (E2E) [050-055]
```

## Parallel Execution Groups

| Group | Tasks | Prerequisite |
|-------|-------|-------------|
| A | Phase 2 + Phase 3 | Phase 1 complete |
| B | 030, 031, 032, 033, 034 | Phase 2+3 complete |
| C | 035, 036, 037, 038 | Phase 2+3 complete |
| D | 051, 052, 053 | Phase 4 complete |

## References

- `.claude/adr/ADR-001-minimal-api.md` through `ADR-016-ai-rate-limits.md`
- `.claude/constraints/ai.md`, `api.md`, `testing.md`
- `.claude/patterns/ai/analysis-scopes.md`
- `.claude/patterns/api/service-registration.md`
- `.claude/patterns/testing/unit-test-structure.md`
- `projects/ai-json-prompt-schema-system/notes/jps-conversions/` (9 reference JPS files)
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md`
