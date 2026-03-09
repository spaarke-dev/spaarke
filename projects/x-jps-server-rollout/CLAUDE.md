# CLAUDE.md — jps-server-rollout

> **Project**: JPS Server Rollout
> **Branch**: `work/ai-json-prompt-schema`
> **Status**: In Progress

## Quick Context

Complete the server-side JPS pipeline. Core infrastructure exists (models, renderer, GenericAnalysisHandler). This project wires the gaps: scope resolution, $ref resolution, template parameters, handler migration, E2E validation.

## Applicable ADRs

| ADR | Key Constraint |
|-----|----------------|
| ADR-001 | Minimal API patterns; no Azure Functions |
| ADR-004 | Async Job Contract; idempotent handlers |
| ADR-008 | Endpoint filters for auth |
| ADR-010 | Concrete DI; ≤15 non-framework registrations |
| ADR-013 | Extend BFF; no separate AI service |
| ADR-014 | Redis caching; tenant-scoped versioned keys |
| ADR-015 | No prompt logging; identifiers only |
| ADR-016 | Rate limit AI endpoints |

## Key File Paths

**Modification targets**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/ScopeResolverService.cs` — Stub at L93-106
- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` — Interface + models
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — CreateToolExecutionContext() L270-317
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolExecutionContext.cs` — Data record
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs` — Render() call L240-257
- `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs` — Template substitution
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs` — DI registration

**New files**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/JpsRefResolver.cs` — Static $ref extraction

**Handler files (migration)**:
- `Services/Ai/Tools/DocumentClassifierHandler.cs` — Thin wrapper (preserve RAG)
- `Services/Ai/Tools/ClauseAnalyzerHandler.cs` — Consolidate
- `Services/Ai/Tools/DateExtractorHandler.cs` — Consolidate
- `Services/Ai/Tools/EntityExtractorHandler.cs` — Consolidate
- `Services/Ai/Tools/RiskDetectorHandler.cs` — Consolidate
- `Services/Ai/Tools/FinancialCalculatorHandler.cs` — Consolidate
- `Services/Ai/Tools/SummaryHandler.cs` — Thin wrapper (preserve config)
- `Services/Ai/Tools/SemanticSearchToolHandler.cs` — Thin wrapper (preserve search)
- `Services/Ai/Tools/ClauseComparisonHandler.cs` — Thin wrapper (preserve comparison)

**Tests**:
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PromptSchemaTests.cs`
- `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PromptSchemaRendererTests.cs`

**Knowledge docs**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md`
- `.claude/patterns/ai/analysis-scopes.md`
- `.claude/patterns/api/service-registration.md`
- `.claude/constraints/ai.md`
- `.claude/constraints/api.md`
- `.claude/constraints/testing.md`

**JPS reference examples**:
- `projects/ai-json-prompt-schema-system/notes/jps-conversions/` (9 files)

## Design Decisions

1. **JpsRefResolver is static** — No new DI registration (ADR-010)
2. **Resolution in AiAnalysisNodeExecutor** — Central point with IScopeResolverService access
3. **ToolExecutionContext extended** — AdditionalKnowledge, AdditionalSkills, TemplateParameters
4. **Format detection unchanged** — `IsJpsFormat()` backward compatible
5. **Consolidation strategy** — Prompt-only handlers → GenericAnalysisHandler; custom-logic handlers → thin wrapper

## Task Execution Protocol

When executing tasks in this project, you MUST use the `task-execute` skill. Do NOT read POML files directly and implement manually.

Trigger phrases: "work on task X", "continue", "next task", "resume task X", "keep going"

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|-------------|-------|
| A | Phase 2 + Phase 3 | Phase 1 complete | $ref resolution and template params independent |
| B | 030-034 | Phase 2+3 complete | Five consolidation tasks independent |
| C | 035-038 | Phase 2+3 complete | Four thin-wrapper tasks independent |
