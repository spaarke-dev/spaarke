# CLAUDE.md — ai-json-prompt-schema-system

> **Project**: JSON Prompt Schema (JPS) System
> **Branch**: `work/ai-json-prompt-schema`
> **Status**: In Progress

## Quick Context

JPS introduces a structured JSON format for AI prompts stored in `sprk_systemprompt`. Replaces flat text with validated, composable schemas. Three authoring levels (form, JSON editor, Builder Agent) produce identical output.

## Applicable ADRs

| ADR | Key Constraint |
|-----|----------------|
| ADR-001 | Minimal API patterns; no Azure Functions |
| ADR-006 | Code Page for standalone UI (not PCF) |
| ADR-008 | Endpoint filters for auth |
| ADR-009 | Redis caching; version keys |
| ADR-010 | Concrete DI registrations; ≤15 non-framework |
| ADR-012 | @spaarke/ui-components; Fluent v9 |
| ADR-013 | Extend BFF; no separate AI service |
| ADR-014 | Redis + tenant-scoped cache keys |
| ADR-015 | No prompt logging; identifiers only |
| ADR-016 | Rate limit AI endpoints |
| ADR-021 | Fluent v9 only; dark mode; no hard-coded colors |
| ADR-026 | Single-file build; React 19; createRoot |

## Key File Paths

**Server (modification targets)**:
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs` — `BuildExecutionPrompt()` L408-456
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — `CreateToolExecutionContext()` L263-305
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolExecutionContext.cs` — Data record
- `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` — Scope models (ResolvedScopes, AnalysisAction, etc.)
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` — `ExecuteNodeAsync()` L817-1037
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolDefinitions.cs` — Tool definitions
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolExecutor.cs` — Tool execution
- `src/server/api/Sprk.Bff.Api/Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` — System prompts
- `src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs` — DI registration

**Client (PlaybookBuilder code page)**:
- `src/client/code-pages/PlaybookBuilder/src/` — Root source
- `src/client/code-pages/PlaybookBuilder/src/components/properties/` — Node property forms
- `src/client/code-pages/PlaybookBuilder/src/types/canvas.ts` — PlaybookNodeData
- `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts` — Canvas sync
- `src/client/code-pages/PlaybookBuilder/src/stores/canvasStore.ts` — Node state

**Knowledge Docs**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md`
- `docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md`
- `.claude/patterns/ai/analysis-scopes.md`
- `.claude/patterns/api/service-registration.md`
- `.claude/constraints/ai.md`

## Design Decisions

1. **$knowledge resolution**: Render time (live) — every execution queries Dataverse for fresh content
2. **Override merge**: Concatenate by default; `__replace: true` directive for full replacement
3. **UI platform**: React 19 HTML code page (ADR-026), NOT PCF
4. **Format detection**: `TrimStart().StartsWith('{') && contains "$schema"` → JPS; otherwise flat text
5. **Backward compatibility**: Flat text path produces byte-for-byte identical output to current `BuildExecutionPrompt`

## Task Execution Protocol

When executing tasks in this project, you MUST use the `task-execute` skill. Do NOT read POML files directly and implement manually.

Trigger phrases: "work on task X", "continue", "next task", "resume task X", "keep going"

All trigger phrases MUST invoke `task-execute` with the appropriate POML file path.

## Parallel Execution Groups

Tasks within each group can run simultaneously (no file conflicts):

| Group | Tasks | Prerequisite | Notes |
|-------|-------|-------------|-------|
| A | Phase 1 + Phase 2 | None | Server + Client fully independent |
| B | Phase 3 + Phase 4 + Phase 5 | Phase 1 complete | All three independent of each other |
