# JSON Prompt Schema (JPS) — Implementation Plan

> **Project**: ai-json-prompt-schema-system
> **Created**: 2026-03-03
> **Phases**: 5
> **Estimated Tasks**: ~45-55

## Architecture Context

### Discovered Resources

**ADRs (12)**:
| ADR | Title | Key Constraint |
|-----|-------|----------------|
| ADR-001 | Minimal API + BackgroundService | No Azure Functions; Minimal API patterns |
| ADR-006 | PCF over Webresources | Code Page for standalone UI; no legacy JS |
| ADR-008 | Endpoint Filters | Endpoint filters for auth; no global middleware |
| ADR-009 | Redis-First Caching | IDistributedCache; version cache keys |
| ADR-010 | DI Minimalism | Concrete registrations; ≤15 non-framework lines |
| ADR-012 | Shared Component Library | Fluent UI v9; @spaarke/ui-components |
| ADR-013 | AI Architecture | Extend BFF; no separate AI service |
| ADR-014 | AI Caching | Redis; version + tenant scoped keys |
| ADR-015 | AI Data Governance | No prompt logging; identifiers only |
| ADR-016 | AI Rate Limits | Rate limit all AI endpoints |
| ADR-021 | Fluent UI v9 Design System | Fluent v9 only; dark mode; no hard-coded colors |
| ADR-026 | Full-Page Custom Pages | Vite/Webpack single-file build; React 19; createRoot |

**Knowledge Docs**:
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — BFF orchestration, four-tier AI architecture
- `docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md` — Scope types and assembly
- `.claude/patterns/ai/analysis-scopes.md` — Three-tier scope system, prompt assembly
- `.claude/patterns/ai/streaming-endpoints.md` — SSE chunks, circuit breaker
- `.claude/patterns/api/endpoint-definition.md` — Minimal API patterns
- `.claude/patterns/api/service-registration.md` — DI module pattern
- `.claude/patterns/api/error-handling.md` — ProblemDetails, RFC 7807
- `.claude/constraints/ai.md` — MUST/MUST NOT rules for AI features

**Existing Code (Key Integration Points)**:
- `GenericAnalysisHandler.cs` — `BuildExecutionPrompt()` L408-456 (replacement target)
- `AiAnalysisNodeExecutor.cs` — `CreateToolExecutionContext()` L263-305
- `ToolExecutionContext.cs` — Data record with ActionSystemPrompt, SkillContext, KnowledgeContext
- `PlaybookOrchestrationService.cs` — `ExecuteNodeAsync()` L817-1037
- `BuilderToolDefinitions.cs` — Tool definitions (add `configure_prompt_schema`)
- `ToolFrameworkExtensions.cs` — DI registration via assembly scanning

**PlaybookBuilder Code Page**:
- Location: `src/client/code-pages/PlaybookBuilder/`
- React 19 + @xyflow/react v12 + Zustand v5 + Fluent UI v9
- Webpack build → `out/bundle.js` → inlined to `out/sprk_playbookbuilder.html`
- Key directories: `components/properties/`, `types/`, `services/`, `stores/`

### Parallelism Strategy

```
Phase 1 (Server: Models + Renderer)  ←→  Phase 2 (Client: UI)
         │                                        │
    FULLY PARALLEL (no dependencies between them)
         │                                        │
         ▼                                        ▼
Phase 3 (Structured Output + $choices) — needs Phase 1 models
Phase 4 (Builder Agent) — needs Phase 1 models
Phase 5 (Cross-Scope Refs) — needs Phase 1 renderer
         │
    ALL THREE PARALLEL (independent of each other)
```

**Maximum parallelism**: 2 streams initially (server + client), then 3 streams (phases 3+4+5).

---

## Phase Breakdown

### Phase 1: Schema Format + Renderer (Server Only)

**Goal**: JPS renders identically to flat text for existing prompts. New prompts can use schema format.

**Deliverables**:
1. C# record models: `PromptSchema`, `InstructionSection`, `OutputSection`, `OutputFieldDefinition`, `InputSection`, `ScopesSection`, `ExampleEntry`, `MetadataSection`
2. `PromptSchemaRenderer` service:
   - Format detection (null/empty → defaults, flat text → legacy, JPS JSON → schema path)
   - Schema parsing with `System.Text.Json`
   - Section-by-section rendering (role → task → constraints → context → document → scopes → examples → output)
   - `RenderedPrompt` return type with `PromptText`, `JsonSchema?`, `SchemaName`, `Format`
3. `GenericAnalysisHandler` integration — replace `BuildExecutionPrompt` with renderer call
4. DI registration in `ToolFrameworkExtensions.cs`
5. Unit tests:
   - Format detection (flat text, JPS JSON, null, empty)
   - Rendering pipeline (each section type)
   - Backward compatibility (flat text produces identical output)
   - Edge cases (missing optional sections, empty arrays)

**Key Files**:
- NEW: `src/server/api/Sprk.Bff.Api/Services/Ai/Models/PromptSchema.cs`
- NEW: `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs`
- NEW: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PromptSchemaRendererTests.cs`

**Dependencies**: None (can start immediately)

### Phase 2: PlaybookBuilder UI (Level 1 + Level 2)

**Goal**: Power users create prompts via form; advanced users edit JSON directly.

**Deliverables**:
1. TypeScript `PromptSchema` type definitions (mirror C# models)
2. `PromptSchemaForm` component:
   - Role (textarea), Task (textarea, required), Constraints (tag list with add/remove)
   - Output fields repeating row (name, type dropdown, description, remove)
   - "Use Structured Output" checkbox
   - "Switch to JSON Editor" toggle
3. `PromptSchemaEditor` component:
   - Monaco-style JSON editor with JPS validation
   - Rendered prompt preview tab
   - Lossless toggle back to form
4. Integration into `NodePropertiesForm.tsx`:
   - Detect AI node types → show prompt schema accordion
   - Read/write `promptSchema` on `PlaybookNodeData`
5. Schema serialization in `playbookNodeSync.ts`:
   - Serialize `promptSchema` → `sprk_systemprompt` (Action scope)
   - Or into `configJson.promptSchemaOverride` (node-level)
6. Canvas store updates for `promptSchema` in node data

**Key Files**:
- NEW: `src/client/code-pages/PlaybookBuilder/src/types/promptSchema.ts`
- NEW: `src/client/code-pages/PlaybookBuilder/src/components/properties/PromptSchemaForm.tsx`
- NEW: `src/client/code-pages/PlaybookBuilder/src/components/properties/PromptSchemaEditor.tsx`
- NEW: `src/client/code-pages/PlaybookBuilder/src/services/promptSchemaValidation.ts`
- MODIFY: `src/client/code-pages/PlaybookBuilder/src/components/properties/NodePropertiesForm.tsx`
- MODIFY: `src/client/code-pages/PlaybookBuilder/src/types/canvas.ts`
- MODIFY: `src/client/code-pages/PlaybookBuilder/src/services/playbookNodeSync.ts`
- MODIFY: `src/client/code-pages/PlaybookBuilder/src/stores/canvasStore.ts`

**Dependencies**: None (fully parallel with Phase 1)

### Phase 3: Structured Output + $choices

**Goal**: Choice fields auto-wire. Structured output guarantees valid JSON.

**Deliverables**:
1. Server: `$choices` reference resolution
   - Parse `"downstream:nodeVar.fieldName"` syntax
   - Traverse execution graph to find downstream UpdateRecord node
   - Extract `fieldMappings[].options` keys
   - Inject as `enum` values on output field
2. Server: JSON Schema Draft-07 generation from `output.fields`
   - Type mapping (string → string, number → number, etc.)
   - Enum values (from `$choices` or inline `enum`)
   - Array items schema, nested objects
   - `required` properties
3. Server: `GetStructuredCompletionAsync<T>` integration
   - Pass JSON Schema as `response_format` to Azure OpenAI
   - Handle constrained decoding response
4. Client: $choices UI in `PromptSchemaForm`
   - Output field type "string" + downstream match → show "Values from: [node]"
   - Display resolved choice labels as read-only chips
5. Client: Canvas validation
   - Output coverage: downstream template refs have matching output fields
   - Choice consistency: UpdateRecord options match AI output field enum
   - Type compatibility: output field types match downstream expectations
6. Server: Pre-execution validation
   - $choices resolvable
   - Schema parseable, structured output compatible

**Key Files**:
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs`
- MODIFY: `src/client/code-pages/PlaybookBuilder/src/components/properties/PromptSchemaForm.tsx`
- NEW: `src/client/code-pages/PlaybookBuilder/src/services/canvasValidation.ts`

**Dependencies**: Phase 1 (C# models and renderer must exist)

### Phase 4: Builder Agent Integration (Level 3)

**Goal**: Builder Agent creates optimized schema-based prompts.

**Deliverables**:
1. `configure_prompt_schema` tool definition in `BuilderToolDefinitions.cs`
   - Parameters: nodeId, role, task, constraints, outputFields, useStructuredOutput, autoWireChoices
   - Returns: JPS JSON stored in node's Action scope
2. Tool executor in `BuilderToolExecutor.cs`
   - Generate full JPS JSON from simplified parameters
   - `autoWireChoices`: traverse canvas edges to find downstream UpdateRecord nodes, auto-connect `$choices`
   - Set `metadata.authorLevel = 3`, `metadata.author = "builder-agent"`
3. System prompt updates in `PlaybookBuilderSystemPrompt.cs`
   - Instruct agent to always use `configure_prompt_schema` for AI nodes
   - Explain JPS structure, output fields, $choices wiring
4. Unit tests for tool execution and schema generation

**Key Files**:
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolDefinitions.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolExecutor.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs`
- NEW: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/Builder/ConfigurePromptSchemaTests.cs`

**Dependencies**: Phase 1 (C# models must exist)

### Phase 5: Cross-Scope References + Advanced

**Goal**: Full scope composition with named references and override merge.

**Deliverables**:
1. `$knowledge` named reference resolution
   - Parse `{"$ref": "knowledge:name"}` syntax
   - Query Dataverse `sprk_analysisknowledge` by `sprk_name` at render time
   - Support contextual labels (`as: "reference" | "definitions" | "examples"`)
   - Merge with N:N resolved scopes (N:N precedence on conflicts)
2. `$skill` named reference resolution
   - Parse `{"$ref": "skill:name"}` syntax
   - Query Dataverse `sprk_analysisskill` by `sprk_name`
3. `promptSchemaOverride` merge logic
   - Read override from `sprk_configjson.promptSchemaOverride`
   - Default: concatenate array fields (constraints, output.fields)
   - Directive: `__replace: true` replaces instead of merging
   - Scalar fields (role, task) replace if present
4. Schema template library
   - Pre-built schemas for common patterns (document classification, entity extraction, risk assessment)
   - Templates stored as JPS JSON with placeholder descriptions
5. Schema versioning
   - `$version` field tracking
   - Upgrade path from v1 to future versions

**Key Files**:
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaRenderer.cs`
- MODIFY: `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs`
- NEW: `src/server/api/Sprk.Bff.Api/Services/Ai/PromptSchemaOverrideMerger.cs`
- NEW: `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/PromptSchemaOverrideMergerTests.cs`

**Dependencies**: Phase 1 (renderer foundation must exist)

---

## Task Numbering Convention

| Phase | Task Range | Description |
|-------|-----------|-------------|
| Phase 1 | 001-009 | Schema Format + Renderer |
| Phase 2 | 010-019 | PlaybookBuilder UI |
| Phase 3 | 020-029 | Structured Output + $choices |
| Phase 4 | 030-039 | Builder Agent Integration |
| Phase 5 | 040-049 | Cross-Scope References + Advanced |
| Wrap-up | 090 | Project wrap-up |

---

## References

- [Design Document](design.md) — Complete JPS schema format, authoring levels, rendering pipeline
- [Implementation Spec](spec.md) — Requirements, ADRs, success criteria, modification points
- [AI Architecture Guide](../../docs/guides/SPAARKE-AI-ARCHITECTURE.md)
- [Scope Creation Guide](../../docs/guides/HOW-TO-CREATE-AI-PLAYBOOK-SCOPES.md)
- [AI Constraints](../../.claude/constraints/ai.md)
