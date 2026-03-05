# JPS Server Rollout — Specification

> **Project**: jps-server-rollout
> **Created**: 2026-03-04
> **Status**: Specification
> **Branch**: work/ai-json-prompt-schema
> **Predecessor**: ai-json-prompt-schema-system (complete, 28/28 tasks)

---

## Executive Summary

Complete the server-side JPS (JSON Prompt Schema) pipeline so that JPS becomes the standard prompt format across all Spaarke playbook actions. The JPS infrastructure (models, renderer, GenericAnalysisHandler integration, format detection, JSON Schema generation, $choices resolution) was built in the predecessor project and passes 76 tests. This project completes the remaining gaps: scope resolution, named $ref resolution, template parameters, handler migration, and end-to-end validation.

## Scope

### In Scope

1. **Scope Resolution Completion** — `ScopeResolverService.ResolveScopesAsync()` currently returns empty arrays. Implement actual Dataverse queries for skills, knowledge, and tools by ID.
2. **Named $ref Resolution** — Wire `$ref: "knowledge:{name}"` and `$ref: "skill:{name}"` to Dataverse lookups. Create `JpsRefResolver` static utility for extraction, add name-based lookups to `IScopeResolverService`, wire through `AiAnalysisNodeExecutor` to `GenericAnalysisHandler`.
3. **Template Parameters** — Connect `templateParameters` from node `ConfigJson` through `ToolExecutionContext` to `PromptSchemaRenderer.Render()` for `{{param}}` Handlebars substitution in JPS instruction fields.
4. **Handler Migration** — Migrate 9 specialized analysis handlers:
   - **Consolidate** 5 prompt-only handlers (ClauseAnalyzer, DateExtractor, EntityExtractor, RiskDetector, FinancialCalculator) into GenericAnalysisHandler + JPS config
   - **Thin wrapper** 4 handlers with custom logic (DocumentClassifier/RAG, Summary/config, SemanticSearch/search, ClauseComparison/multi-doc) to delegate prompt construction to JPS while preserving custom logic
5. **Data Seeding** — Populate Dataverse Action records with JPS JSON using the 9 conversion examples from the predecessor project
6. **End-to-End Validation** — Integration tests for full pipeline, $choices, override merge, backward compatibility

### Out of Scope

- PlaybookBuilder UI (separate project: ai-playbook-node-builder-r5)
- Builder Agent JPS generation (Phase 3+ future)
- New JPS conversions beyond the existing 9 examples
- Dataverse schema changes (all entities/fields already exist)

## Success Criteria

1. `ScopeResolverService.ResolveScopesAsync()` returns real Skill/Knowledge/Tool records from Dataverse
2. JPS `$ref` entries resolve to Dataverse records by name and appear in rendered prompts
3. Template parameters from ConfigJson flow through to rendered prompt text
4. All 9 specialized handlers either consolidated or converted to thin JPS consumers
5. All existing flat-text playbooks continue working unchanged (backward compatibility)
6. `dotnet build` — 0 errors, 0 warnings
7. `dotnet test` — all tests passing (76 existing + new)
8. Document Profiler playbook executes with JPS and populates Dataverse fields correctly

## Technical Approach

### Architecture

The JPS pipeline flows:

```
Action.sprk_systemprompt (JPS JSON)
  → AiAnalysisNodeExecutor.CreateToolExecutionContext()
    → JpsRefResolver.Extract*Refs() → ScopeResolverService.Get*ByNameAsync()
    → Extract templateParameters from ConfigJson
    → ApplyPromptSchemaOverride() (existing)
    → Build ToolExecutionContext with resolved refs + params
  → GenericAnalysisHandler / Thin Handler
    → PromptSchemaRenderer.Render(prompt, skills, knowledge, document, params, downstream, additionalKnowledge, additionalSkills)
    → RenderedPrompt { PromptText, JsonSchema, SchemaName, Format }
  → Azure OpenAI (structured output or plain completion)
```

### Key Design Decisions

1. **JpsRefResolver is static** — No new DI registration (ADR-010: ≤15 non-framework registrations)
2. **Resolution in AiAnalysisNodeExecutor** — Central integration point that already has access to IScopeResolverService
3. **ToolExecutionContext extended** — New properties: `AdditionalKnowledge`, `AdditionalSkills`, `TemplateParameters`
4. **Backward compatibility via format detection** — `IsJpsFormat()` routes flat text through legacy path

### Phased Rollout

| Phase | Focus | Tasks | Dependencies |
|-------|-------|-------|-------------|
| 1 | Scope Resolution | 3 | None |
| 2 | Named $ref Resolution | 7 | Phase 1 |
| 3 | Template Parameters | 4 | Phase 1 (parallel with Phase 2) |
| 4 | Handler Migration | 9 | Phases 2 + 3 |
| 5 | Data Seeding + E2E Validation | 6 | All previous |

Phases 2 and 3 are fully parallel.

### Critical Files

| File | Changes |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` | Complete stub (P1), add name lookups (P2) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Wire ref resolution + template params |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ToolExecutionContext.cs` | Add 3 new properties |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs` | Pass resolved refs + params |
| `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs` | Verify template substitution |
| New: `Services/Ai/JpsRefResolver.cs` | Static $ref extraction utility |
| `src/server/api/Sprk.Bff.Api/Services/Ai/Tools/*.cs` | 9 handler files |

### Existing Infrastructure (Do Not Rebuild)

| Component | Location | Status |
|-----------|----------|--------|
| PromptSchema models | `Services/Ai/Models/PromptSchema.cs` (412 lines) | Complete |
| PromptSchemaRenderer | `Services/Ai/PromptSchemaRenderer.cs` (969 lines) | Complete |
| GenericAnalysisHandler JPS integration | `Handlers/GenericAnalysisHandler.cs` | Complete |
| PromptSchemaOverrideMerger | `AiAnalysisNodeExecutor.cs` | Complete |
| Format detection | `PromptSchemaRenderer.IsJpsFormat()` | Complete |
| JSON Schema generation | `PromptSchemaRenderer.GenerateJsonSchema()` | Complete |
| $choices resolution | `PromptSchemaRenderer.ResolveChoices()` | Complete |
| 9 JPS conversion examples | `projects/ai-json-prompt-schema-system/notes/jps-conversions/` | Complete |
| 76 passing tests | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PromptSchema*Tests.cs` | Complete |

### Handler Migration Strategy

| Handler | Custom Logic | Approach |
|---------|-------------|----------|
| ClauseAnalyzerHandler | None | Consolidate → GenericAnalysisHandler |
| DateExtractorHandler | None | Consolidate → GenericAnalysisHandler |
| EntityExtractorHandler | None | Consolidate → GenericAnalysisHandler |
| RiskDetectorHandler | None | Consolidate → GenericAnalysisHandler |
| FinancialCalculatorHandler | None | Consolidate → GenericAnalysisHandler |
| DocumentClassifierHandler | RAG example retrieval | Thin wrapper (preserve RAG) |
| SummaryHandler | Config-driven format | Thin wrapper (preserve config) |
| SemanticSearchToolHandler | Search integration | Thin wrapper (preserve search) |
| ClauseComparisonHandler | Multi-doc comparison | Thin wrapper (preserve comparison) |

### Risks

| Risk | Mitigation |
|------|-----------|
| DI count exceeds ADR-010 limit | JpsRefResolver is static, no new registration |
| Name-based Dataverse queries slow | Redis caching for knowledge/skill lookups |
| Handler migration breaks behavior | Per-handler tasks with dedicated tests |
| Structured output edge cases | 76 existing tests + new integration tests |

### ADR Compliance

| ADR | Constraint | Impact |
|-----|-----------|--------|
| ADR-001 | No Azure Functions | All server-side, BFF API only |
| ADR-008 | Endpoint filters for auth | No new endpoints, existing auth unchanged |
| ADR-010 | ≤15 DI registrations | JpsRefResolver static, handler consolidation reduces registrations |
| ADR-013 | AI Tool Framework | Extends existing framework, no separate service |

## Estimated Total

**29 tasks** across 5 phases. Server-side only.
