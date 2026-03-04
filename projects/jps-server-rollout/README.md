# JPS Server Rollout

> **Status**: In Progress
> **Branch**: `work/ai-json-prompt-schema`
> **Predecessor**: `ai-json-prompt-schema-system` (complete, 28/28 tasks)
> **Created**: 2026-03-04

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
