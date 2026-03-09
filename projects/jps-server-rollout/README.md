# JPS Server Rollout

> **Status**: Complete
> **Branch**: `work/ai-json-prompt-schema`
> **Predecessor**: `ai-json-prompt-schema-system` (complete, 28/28 tasks)
> **Created**: 2026-03-04
> **Completed**: 2026-03-04

## Purpose

Complete the server-side JPS (JSON Prompt Schema) pipeline so that JPS becomes the standard prompt format across all Spaarke playbook actions. The predecessor project built the core infrastructure (models, renderer, GenericAnalysisHandler integration). This project wires the remaining gaps: scope resolution, named $ref resolution, template parameters, handler migration, and end-to-end validation.

## Scope

- **Scope Resolution** — Complete `ScopeResolverService.ResolveScopesAsync()` stub
- **Named $ref Resolution** — Wire `$ref: "knowledge:{name}"` to Dataverse lookups
- **Template Parameters** — Connect `{{param}}` substitution from ConfigJson
- **Handler Migration** — Consolidate 5 prompt-only handlers; thin-wrap 4 with custom logic
- **Data Seeding** — Populate Action records with JPS JSON
- **E2E Validation** — Integration tests for full pipeline

## Out of Scope

- PlaybookBuilder UI (separate project: ai-playbook-node-builder-r5)
- Builder Agent JPS generation
- Dataverse schema changes

## Graduation Criteria

1. `ScopeResolverService.ResolveScopesAsync()` returns real records
2. JPS `$ref` entries resolve by name and appear in rendered prompts
3. Template parameters flow through to rendered prompt text
4. All 9 specialized handlers migrated
5. Flat-text playbooks unchanged (backward compatibility)
6. `dotnet build` — 0 errors, 0 warnings
7. `dotnet test` — all tests passing
8. Document Profiler playbook works end-to-end with JPS

## Key Files

| File | Role |
|------|------|
| `Services/Ai/ScopeResolverService.cs` | Scope resolution (stub → real) |
| `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Central wiring point |
| `Services/Ai/ToolExecutionContext.cs` | Context record (3 new props) |
| `Services/Ai/Handlers/GenericAnalysisHandler.cs` | JPS consumer |
| `Services/Ai/PromptSchemaRenderer.cs` | Rendering engine (969 lines) |
| New: `Services/Ai/JpsRefResolver.cs` | Static $ref extraction |
| `Services/Ai/Tools/*.cs` | 9 handler files to migrate |

## Deliverables Summary

All 5 phases completed (55 tasks, 217 tests passing):

### Phase 1: Scope Resolution
- `ScopeResolverService.ResolveScopesAsync()` implemented with parallel Dataverse queries
- Resolves skills, knowledge, and tools by ID from `sprk_analysisskill`, `sprk_analysisknowledge`, and `sprk_analysistool`

### Phase 2: Named $ref Resolution
- `JpsRefResolver` (static class, no DI per ADR-010) extracts `knowledge:{name}` and `skill:{name}` references from JPS `scopes`
- Name-based lookups via `IScopeResolverService.GetKnowledgeByNameAsync()` and `GetSkillByNameAsync()`
- Wired into `AiAnalysisNodeExecutor` to resolve refs before rendering

### Phase 3: Template Parameters
- `{{paramName}}` substitution in instruction fields via `PromptSchemaRenderer`
- Parameters sourced from `ConfigJson.templateParameters` on playbook nodes
- Override merge via `PromptSchemaOverrideMerger` (static class, supports `$clear` for constraint replacement)

### Phase 4: Handler Migration
- 5 prompt-only handlers consolidated into `GenericAnalysisHandler` (clause analyzer, entity extractor, document classifier, risk assessor, key terms extractor)
- 4 handlers retained as thin wrappers with JPS dual-path (document profiler, summarizer, comparison handler, compliance checker)
- All 9 handlers support both JPS and legacy flat-text prompts

### Phase 5: Validation and Documentation
- Seeding script populates Action records with JPS JSON definitions
- 217 tests passing (pipeline, choices, override merge, backward compatibility)
- AI Architecture Guide updated with JPS section (Section 19)
- JPS Authoring Guide created for prompt authors
