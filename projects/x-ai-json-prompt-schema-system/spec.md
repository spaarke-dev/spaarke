# JSON Prompt Schema (JPS) System — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-03-03
> **Source**: design.md (JSON Prompt Schema design document, ~990 lines)
> **Branch**: `work/ai-json-prompt-schema`

## Executive Summary

Introduce a structured JSON-based format (JSON Prompt Schema / JPS) for defining AI prompts in Spaarke playbooks. JPS formalizes the **Composition** stage of the AI pipeline — the step between authoring (defining what the AI should do) and orchestration (executing nodes in order). It replaces flat text blobs in `sprk_systemprompt` with validated, composable schemas that bridge three authoring levels: power user forms (L1), JSON editor (L2), and Builder Agent (L3), all producing identical runtime behavior.

## Scope

### In Scope

**Phase 1: Schema Format + Renderer (Server Only)**
- C# record models for JPS format (`PromptSchema`, `InstructionSection`, `OutputSection`, etc.)
- `PromptSchemaRenderer` service with format detection (flat text vs JPS JSON vs empty)
- Rendering pipeline: instruction → constraints → context → document → scopes → examples → output
- Backward compatibility: flat text path produces identical output to current `BuildExecutionPrompt`
- DI registration for renderer service
- Unit tests for rendering + backward compatibility

**Phase 2: PlaybookBuilder UI (Level 1 + Level 2)**
- `PromptSchemaForm` component — structured form (role, task, constraints, output fields)
- `PromptSchemaEditor` component — raw JSON editor with validation and preview
- Lossless toggle between form and JSON views
- Prompt preview (render the assembled prompt as text)
- TypeScript `PromptSchema` type definitions
- Integration into existing PlaybookBuilder HTML code page (React 19)

**Phase 3: Structured Output + $choices**
- `output.fields` → JSON Schema Draft-07 conversion
- `$choices` reference resolution from downstream UpdateRecord `fieldMappings[].options`
- Azure OpenAI `GetStructuredCompletionAsync<T>` integration (constrained JSON decoding)
- `DownstreamNodeInfo` collection during orchestration
- Canvas-side validation warnings (output coverage, choice consistency, type compatibility)
- Server-side pre-execution validation

**Phase 4: Builder Agent Integration (Level 3)**
- `configure_prompt_schema` tool definition in `BuilderToolDefinitions.cs`
- Tool executor generates full JPS JSON from simplified parameters
- `autoWireChoices` flag traverses canvas edges to auto-connect `$choices`
- System prompt updates in `PlaybookBuilderSystemPrompt.cs` for JPS awareness
- `metadata.authorLevel = 3` for provenance tracking

**Phase 5: Cross-Scope References + Advanced**
- `$knowledge` named references (resolve by `sprk_name` at render time, live queries)
- `$skill` named references
- Contextual placement with `as` labels (reference, definitions, examples)
- `promptSchemaOverride` merge with directive support (`__replace: true`)
- Prompt schema template library (pre-built schemas for common patterns)
- Schema versioning and upgrade tooling

### Out of Scope

- Dataverse schema changes — JPS uses existing `sprk_systemprompt` field (nvarchar(max))
- New Dataverse columns or entities
- Migration tooling for existing playbooks (they work as-is via format detection)
- Prompt performance analytics / A/B testing (future enhancement)
- Multi-model support (JPS targets Azure OpenAI GPT-4o only)
- Changes to the orchestration graph execution order (JPS affects composition, not orchestration)

### Affected Areas

**Server (C# / .NET 8 Minimal API)**
- `src/server/api/Sprk.Bff.Api/Services/Ai/` — New `PromptSchemaRenderer.cs`, new `Models/PromptSchema.cs`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs` — Replace `BuildExecutionPrompt()` (L408-456) with renderer call
- `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` — Pass downstream node info to handler context
- `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookOrchestrationService.cs` — Collect downstream ConfigJson for `$choices`
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolDefinitions.cs` — Add `configure_prompt_schema` tool
- `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolExecutor.cs` — Execute new tool
- `src/server/api/Sprk.Bff.Api/Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs` — JPS-aware system prompt

**Client (TypeScript / React 19 HTML Code Page)**
- PlaybookBuilder HTML code page — New prompt schema form and editor components
- `types/` — TypeScript `PromptSchema` and `OutputFieldDefinition` interfaces
- `services/` — Schema serialization/deserialization, validation utilities
- `components/properties/` — `PromptSchemaForm.tsx`, `PromptSchemaEditor.tsx`

**Tests**
- `tests/` — Unit tests for `PromptSchemaRenderer`, schema validation, backward compatibility
- Integration tests for $choices resolution, structured output generation

## Requirements

### Functional Requirements

1. **FR-01: Format Detection** — The renderer MUST detect three formats: null/empty (delegate to defaults), flat text (legacy path), JPS JSON (schema path). Detection via `TrimStart().StartsWith('{') && contains "$schema"`. — Acceptance: Existing flat text prompts render identically to current `BuildExecutionPrompt` output.

2. **FR-02: Schema Parsing** — Parse JPS JSON into strongly-typed C# `PromptSchema` model with `System.Text.Json` deserialization. Validate required fields (`instruction.task`). — Acceptance: Invalid schemas produce structured error with specific field failures.

3. **FR-03: Rendering Pipeline** — Assemble prompt sections in defined order: role → task → constraints → context → document → parameters → prior outputs → skills → knowledge → examples → output instructions. — Acceptance: Rendered output for a given schema is deterministic and matches the design document's assembled prompt example.

4. **FR-04: $choices Resolution** — Resolve `$choices: "downstream:nodeVar.fieldName"` by traversing the execution graph to the downstream UpdateRecord node and extracting `fieldMappings[].options` keys. — Acceptance: Choice values defined in UpdateRecord config automatically appear in the AI prompt with no manual duplication.

5. **FR-05: Structured Output** — When `structuredOutput: true`, convert `output.fields` to JSON Schema Draft-07 and use Azure OpenAI constrained decoding (`response_format`). — Acceptance: AI returns guaranteed valid JSON matching the field schema. Enum values from `$choices` are included in the JSON Schema.

6. **FR-06: Level 1 Form UI** — Structured form with fields for role (textarea), task (textarea, required), constraints (tag list), output fields (repeating row with name/type/description). — Acceptance: Power user can create a complete JPS schema without seeing JSON.

7. **FR-07: Level 2 JSON Editor** — Raw JSON editor with JPS schema validation, autocomplete for `$choices` and template variables, and rendered prompt preview. — Acceptance: Toggle between form and JSON is lossless. No data lost on switch.

8. **FR-08: Level 3 Builder Agent** — `configure_prompt_schema` tool allows the Builder Agent to create optimized JPS schemas with typed output fields and auto-wired `$choices`. — Acceptance: Agent-created schemas render identically to manually-created schemas with the same content.

9. **FR-09: Canvas Validation** — Validate at canvas save/sync time: output coverage (all downstream template refs have matching fields), choice consistency, type compatibility, unresolvable references. — Acceptance: Warnings surface in PlaybookBuilder UI before execution.

10. **FR-10: Pre-Execution Validation** — Server-side validation before playbook execution: $choices resolvable, $knowledge resolvable, schema parseable, structured output compatible. — Acceptance: Invalid schemas produce clear error messages with actionable fixes.

11. **FR-11: $knowledge Live Resolution** — Resolve `$knowledge` named references (`$ref: "knowledge:name"`) by querying Dataverse `sprk_analysisknowledge` by `sprk_name` at render time (every execution). — Acceptance: Knowledge content updates propagate to next execution without re-saving the schema.

12. **FR-12: Node-Level Override Merge** — Support `promptSchemaOverride` in `sprk_configjson` with merge directives: constraint arrays concatenate by default, `__replace: true` directive replaces instead of merging. Scalar fields (role, task) replace if present in override. — Acceptance: A reusable Action scope can be customized per-node without modifying the shared scope.

### Non-Functional Requirements

- **NFR-01: Backward Compatibility** — Zero regressions for existing flat-text prompts. Format detection ensures legacy path is identical to current behavior. No migration required.

- **NFR-02: Performance** — Rendering pipeline adds <5ms latency vs current `BuildExecutionPrompt`. $choices resolution uses already-loaded graph context (no additional Dataverse queries). $knowledge live resolution uses existing scope resolution caching patterns.

- **NFR-03: Testability** — `PromptSchemaRenderer` is a pure function (given inputs → deterministic output). Unit testable without Dataverse or Azure OpenAI dependencies. Mock `IScopeResolver` for scope reference tests.

- **NFR-04: Parallel Task Execution** — Project tasks MUST be structured for maximum parallelism. Server (C#) and client (TypeScript) work streams are independent. Within each stream, model definitions, renderer logic, validation, and integration are separable. Permission-free execution: tasks should use pre-approved tool patterns (Read, Write, Edit, Glob, Grep, dotnet build/test, npm build/test).

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Key Constraint |
|-----|-----------|----------------|
| **ADR-001** | Server-side renderer service | MUST use Minimal API patterns; no Azure Functions |
| **ADR-006** | PlaybookBuilder UI | MUST use React Code Page (HTML code page with React 19, not PCF) |
| **ADR-008** | Endpoint protection | MUST use endpoint filters for authorization; no global middleware |
| **ADR-009** | Caching rendered prompts | MUST use `IDistributedCache` (Redis); version cache keys |
| **ADR-010** | DI registration | MUST register concretes by default; ≤15 non-framework DI lines |
| **ADR-012** | Shared UI components | MUST use `@spaarke/ui-components`; Fluent UI v9 only |
| **ADR-013** | AI feature architecture | MUST extend BFF, not separate service; follow ADR-001/008/009 |
| **ADR-014** | AI caching | MUST cache via Redis; include version in keys; scope by tenant |
| **ADR-015** | AI data governance | MUST NOT log prompt content; log only identifiers and metrics |
| **ADR-016** | AI rate limits | MUST apply rate limiting to AI endpoints |
| **ADR-021** | UI design system | MUST use Fluent UI v9 exclusively; support light/dark/high-contrast |
| **ADR-026** | Full-page code pages | MUST use Vite + `vite-plugin-singlefile`; React 19 via devDependencies; single HTML output |

### MUST Rules

- MUST detect flat text vs JPS JSON and route accordingly — no breaking existing prompts
- MUST register `PromptSchemaRenderer` as concrete (no interface) per ADR-010
- MUST use `System.Text.Json` for C# deserialization (consistent with codebase)
- MUST wrap PlaybookBuilder UI in `<FluentProvider>` with theme per ADR-021/026
- MUST use Vite + single-file build for code page per ADR-026
- MUST NOT log prompt content or rendered prompts per ADR-015
- MUST NOT create separate AI microservice per ADR-013
- MUST NOT use React 16 or PCF platform libraries for this UI per ADR-026
- MUST NOT create interfaces without genuine seam requirement per ADR-010

### Existing Patterns to Follow

- `GenericAnalysisHandler.BuildExecutionPrompt()` at `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs:408-456` — current rendering logic to replace
- `AiAnalysisNodeExecutor.CreateToolExecutionContext()` at `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs:263-305` — scope resolution context
- `BuilderToolDefinitions` at `src/server/api/Sprk.Bff.Api/Services/Ai/Builder/BuilderToolDefinitions.cs` — existing tool definition patterns
- Existing PlaybookBuilder HTML code page under `src/solutions/` — UI component patterns with React 19

### Existing Code Modification Points

| File | Method/Lines | Change |
|------|-------------|--------|
| `GenericAnalysisHandler.cs` | `BuildExecutionPrompt()` L408-456 | Replace with `PromptSchemaRenderer.Render()` call |
| `GenericAnalysisHandler.cs` | `ExecuteAsync()` L237 | Pass renderer to execution context |
| `GenericAnalysisHandler.cs` | `StreamExecuteAsync()` L338 | Pass renderer to execution context |
| `AiAnalysisNodeExecutor.cs` | `CreateToolExecutionContext()` L263-305 | Add downstream node info to context |
| `AiAnalysisNodeExecutor.cs` | Knowledge/skill builders L310-354 | Enhanced by JPS scopes section |
| `PlaybookOrchestrationService.cs` | `ExecuteNodeAsync()` L817-1037 | Pass downstream ConfigJson for $choices |
| `BuilderToolDefinitions.cs` | `GetAllTools()` L18-32 | Add `configure_prompt_schema` tool |
| `BuilderToolExecutor.cs` | Execute methods | Handle `configure_prompt_schema` execution |
| `PlaybookBuilderSystemPrompt.cs` | `Build()` L680-877 | JPS-aware system prompt instructions |

## Success Criteria

1. [ ] **Existing playbooks unchanged** — Zero regressions; identical prompt output for all flat-text prompts. Verify by running existing playbooks and comparing prompt output byte-for-byte.
2. [ ] **Power users create effective prompts without prompt engineering expertise** — Level 1 form guides through role, task, constraints, output fields. Verify by creating a playbook via form only.
3. [ ] **Choice values defined once** — UpdateRecord options automatically appear in AI prompts via `$choices`. Verify by adding a choice value to UpdateRecord and confirming it appears in the rendered prompt.
4. [ ] **Structured output works** — AI returns guaranteed valid JSON matching the schema. Verify by enabling `structuredOutput: true` and confirming constrained decoding via Azure OpenAI.
5. [ ] **Canvas-time validation catches mismatches** — Missing output fields, unresolvable references, type mismatches surfaced before execution. Verify by creating a mismatched configuration and seeing warnings.
6. [ ] **Builder Agent creates complete playbooks** — Agent generates structured JPS prompts via `configure_prompt_schema` tool. Verify agent-created playbooks execute correctly.
7. [ ] **Three authoring levels produce identical runtime behavior** — Form, JSON editor, and agent output all render to the same prompt. Verify by creating identical schemas via each level and comparing rendered output.
8. [ ] **All tests pass** — `dotnet test` and `npm test` pass. Unit tests cover rendering, validation, backward compatibility, $choices resolution, structured output generation.

## Dependencies

### Prerequisites

- Existing PlaybookBuilder HTML code page with React 19 (already rebuilt)
- Existing playbook orchestration system (nodes, edges, scope resolution)
- Azure OpenAI structured output support (already available via `GetStructuredCompletionAsync<T>`)
- Existing `BuildExecutionPrompt` method in `GenericAnalysisHandler.cs` (replacement target)

### External Dependencies

- Azure OpenAI API — JSON Schema constrained decoding for structured output mode
- Dataverse — `sprk_analysisknowledge` and `sprk_analysisskill` records for `$knowledge`/`$skill` resolution

## Implementation Phases

### Phase 1: Schema Format + Renderer (Server Only) — No UI Dependencies
**New files**:
- `Services/Ai/Models/PromptSchema.cs` — C# record models
- `Services/Ai/PromptSchemaRenderer.cs` — Core renderer with format detection

**Modified files**:
- `GenericAnalysisHandler.cs` — Replace `BuildExecutionPrompt` with renderer call
- `AiAnalysisNodeExecutor.cs` — Pass renderer to handler context
- DI registration in `Program.cs` or feature module

**Parallelizable**: C# models and renderer can be built independently. Tests can be written concurrently with implementation.

### Phase 2: PlaybookBuilder UI (Level 1 + Level 2) — No Server Dependencies
**New files**:
- `PromptSchemaForm.tsx` — Level 1 structured form
- `PromptSchemaEditor.tsx` — Level 2 JSON editor with preview
- TypeScript `PromptSchema` type definitions

**Modified files**:
- PlaybookBuilder node properties panel
- Canvas store (promptSchema in node data)
- Node sync service (serialize promptSchema to configJson)

**Parallelizable**: Fully independent from Phase 1 server work. TypeScript types mirror C# models.

### Phase 3: Structured Output + $choices — Depends on Phase 1
**Modified files**:
- `PromptSchemaRenderer.cs` — Add $choices resolution, JSON Schema generation
- `PlaybookOrchestrationService.cs` — Collect downstream node info
- `GenericAnalysisHandler.cs` — Use `RenderedPrompt.JsonSchema` with structured completion
- `PromptSchemaForm.tsx` — $choices UI (values from downstream node)
- Canvas validation logic

**Parallelizable**: Server $choices and client $choices UI can be built independently.

### Phase 4: Builder Agent Integration (Level 3) — Depends on Phase 1
**Modified files**:
- `BuilderToolDefinitions.cs` — Add tool definition
- `BuilderToolExecutor.cs` — Execute tool
- `PlaybookBuilderSystemPrompt.cs` — JPS-aware instructions

**Parallelizable**: Fully independent from Phase 2 and Phase 3.

### Phase 5: Cross-Scope References + Advanced — Depends on Phase 1
**Modified files**:
- `PromptSchemaRenderer.cs` — $knowledge/$skill resolution, override merge
- Scope resolution services
- Template library infrastructure

**Parallelizable**: Independent from Phases 2-4. Only requires Phase 1 renderer foundation.

### Parallelism Map

```
Phase 1 (Server: Models + Renderer)
  ├──→ Phase 2 (Client: UI) ─── independent, can start immediately
  ├──→ Phase 3 ($choices + Structured Output) ─── after Phase 1 models complete
  ├──→ Phase 4 (Builder Agent) ─── after Phase 1 models complete
  └──→ Phase 5 (Cross-Scope + Advanced) ─── after Phase 1 renderer complete

Maximum parallelism: Phase 1 + Phase 2 fully parallel
Then: Phase 3 + Phase 4 + Phase 5 fully parallel (all depend only on Phase 1)
```

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| $knowledge resolution | Should resolution happen at render time (live) or save time (cached)? | **Render time (live)** — resolve on every execution | Knowledge updates propagate instantly; no need to re-save schemas. Adds Dataverse queries per execution (mitigated by existing scope resolution caching). |
| PlaybookBuilder platform | Should UI be built within existing PCF control or scoped differently? | **HTML code page with React 19** — PlaybookBuilder was rebuilt; use HTML code pages and React 19 | All UI components use React 19, Vite, single-file build. ADR-026 applies, not ADR-022. No React 16 constraints. |
| Override merge rules | Should constraint arrays concatenate or support replacement? | **Merge with directives** — concatenate by default, `__replace: true` to fully replace | Flexible override system: base constraints preserved unless explicitly replaced. Directive pattern extensible to other array fields. |
| Project phase scope | All 5 phases or subset? | **All 5 phases** — fully scoped | Complete implementation. Tasks structured for parallel Claude Code agent execution. Minimize permission interruptions by using pre-approved tool patterns. |
| Development flow | Any workflow preferences? | **Parallel agents + minimal interruptions** | Structure tasks for maximum parallelism. Use pre-approved permissions (Read, Write, Edit, Glob, Grep, git, dotnet, npm). Avoid bash commands that require approval. |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **$knowledge caching**: Although resolution is live, existing `RequestCache` deduplication will prevent duplicate Dataverse queries within the same execution. No new Redis caching layer for knowledge resolution specifically.
- **JSON Schema version**: Using Draft-07 for structured output (consistent with Azure OpenAI documentation). Not Draft 2020-12.
- **Error recovery**: If $choices or $knowledge resolution fails, the renderer produces a warning but continues with available data (graceful degradation, not hard failure).
- **Template engine**: Reusing existing `TemplateEngine.Render()` (Handlebars-based) for template variable resolution. No new template engine.
- **$choices scope**: Only resolves from downstream UpdateRecord nodes in the same playbook graph. Cross-playbook references not supported.
- **Metadata auto-population**: `metadata.createdAt` is set by the authoring interface (form/editor/agent), not by the renderer. `metadata.authorLevel` is set based on which interface created the schema.

## Unresolved Questions

*No blocking questions remain. All critical design decisions were addressed in the design document or owner clarifications.*

- [ ] **PlaybookBuilder file structure**: Exact file paths for the rebuilt PlaybookBuilder code page need to be confirmed during task creation (will be discovered in project-pipeline resource discovery). — Blocks: Phase 2 task file paths.

---

*AI-optimized specification. Original design: design.md*
