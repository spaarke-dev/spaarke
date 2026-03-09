# JSON Prompt Schema (JPS) System

> **Status**: Complete
> **Branch**: `work/ai-json-prompt-schema`
> **Started**: 2026-03-03
> **Completed**: 2026-03-04

## Overview

Structured JSON-based format for defining AI prompts in Spaarke playbooks. Replaces flat text blobs in `sprk_systemprompt` with validated, composable schemas that bridge three authoring levels (power user forms, JSON editor, Builder Agent) into a single canonical format.

## Key Deliverables

### Phase 1: Schema Format + Renderer (Server)
- C# record models for JPS format
- `PromptSchemaRenderer` service with format detection
- Backward-compatible rendering pipeline
- Unit tests

### Phase 2: PlaybookBuilder UI (Level 1 + Level 2)
- `PromptSchemaForm` — structured form (role, task, constraints, output fields)
- `PromptSchemaEditor` — raw JSON editor with validation and preview
- Lossless toggle between form and JSON views
- Integration into PlaybookBuilder code page (React 19)

### Phase 3: Structured Output + $choices
- JSON Schema Draft-07 generation from `output.fields`
- `$choices` auto-injection from downstream UpdateRecord nodes
- Azure OpenAI constrained JSON decoding
- Canvas-time and pre-execution validation

### Phase 4: Builder Agent Integration (Level 3)
- `configure_prompt_schema` tool for Builder Agent
- Auto-wired `$choices` via canvas edge traversal
- JPS-aware system prompt updates

### Phase 5: Cross-Scope References + Advanced
- `$knowledge` and `$skill` named references (live render-time resolution)
- `promptSchemaOverride` merge with `__replace` directive support
- Schema template library

## Architecture

```
sprk_systemprompt (Dataverse field)
    │
    ▼ Format Detection
    ├─ null/empty → tool/operation defaults
    ├─ flat text → legacy BuildExecutionPrompt path
    └─ JPS JSON → PromptSchemaRenderer pipeline
                    │
                    ├─ Parse & validate schema
                    ├─ Resolve $choices from downstream nodes
                    ├─ Resolve $knowledge/$skill references
                    ├─ Render Handlebars templates
                    ├─ Assemble prompt sections
                    └─ Generate JSON Schema (if structured output)
                         │
                         ▼
                    RenderedPrompt { PromptText, JsonSchema?, Format }
```

## Affected Files

**Server (C# / .NET 8)**:
- `Services/Ai/Models/PromptSchema.cs` (new)
- `Services/Ai/PromptSchemaRenderer.cs` (new)
- `Services/Ai/Handlers/GenericAnalysisHandler.cs` (modify)
- `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` (modify)
- `Services/Ai/PlaybookOrchestrationService.cs` (modify)
- `Services/Ai/Builder/BuilderToolDefinitions.cs` (modify)
- `Services/Ai/Builder/BuilderToolExecutor.cs` (modify)
- `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` (modify)

**Client (TypeScript / React 19)**:
- `src/client/code-pages/PlaybookBuilder/src/components/properties/PromptSchemaForm.tsx` (new)
- `src/client/code-pages/PlaybookBuilder/src/components/properties/PromptSchemaEditor.tsx` (new)
- `src/client/code-pages/PlaybookBuilder/src/types/promptSchema.ts` (new)
- `src/client/code-pages/PlaybookBuilder/src/services/promptSchemaValidation.ts` (new)

## Graduation Criteria

1. Existing playbooks unchanged — zero regressions
2. Power users create prompts via Level 1 form without prompt engineering expertise
3. Choice values defined once in UpdateRecord, auto-propagated via $choices
4. Structured output returns guaranteed valid JSON
5. Canvas-time validation catches mismatches before execution
6. Builder Agent creates complete playbooks with JPS
7. Three authoring levels produce identical runtime behavior
8. All tests pass (`dotnet test` + `npm test`)

## References

- [Design Document](design.md)
- [Implementation Spec](spec.md)
- [Implementation Plan](plan.md)
- [Task Index](tasks/TASK-INDEX.md)
