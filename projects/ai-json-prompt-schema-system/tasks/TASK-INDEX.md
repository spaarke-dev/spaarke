# Task Index — ai-json-prompt-schema-system

> **Total Tasks**: 22
> **Status**: Ready for Execution

## Task Registry

### Phase 1: Schema Format + Renderer (Server Only)

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 001 | Create C# PromptSchema Record Models | 🔲 | bff-api, models | none | 2h |
| 002 | Create PromptSchemaRenderer Service with Format Detection | 🔲 | bff-api, api | 001 | 3h |
| 003 | Integrate Renderer into GenericAnalysisHandler | 🔲 | bff-api, refactor | 002 | 2h |
| 004 | DI Registration and Service Wiring | 🔲 | bff-api, api | 002 | 1h |
| 005 | Unit Tests: Models and Format Detection | 🔲 | testing | 002 | 2h |
| 006 | Unit Tests: Rendering Pipeline and Section Assembly | 🔲 | testing | 005 | 2h |

### Phase 2: PlaybookBuilder UI (Level 1 + Level 2)

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 010 | Create TypeScript PromptSchema Type Definitions | 🔲 | frontend, typescript | none | 1h |
| 011 | Create PromptSchemaForm Component (Level 1) | 🔲 | frontend, fluent-ui | 010 | 4h |
| 012 | Create PromptSchemaEditor Component (Level 2) | 🔲 | frontend, fluent-ui | 010 | 3h |
| 013 | Integrate Prompt Schema into Properties Panel | 🔲 | frontend, fluent-ui | 011, 012 | 2h |
| 014 | Schema Serialization in playbookNodeSync | 🔲 | frontend | 013 | 2h |

### Phase 3: Structured Output + $choices

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 020 | Implement $choices Reference Resolution (Server) | 🔲 | bff-api, api | 002 | 3h |
| 021 | Implement JSON Schema Draft-07 Generation | 🔲 | bff-api, api | 001, 020 | 3h |
| 022 | Integrate Structured Output with Azure OpenAI | 🔲 | bff-api, api | 021 | 2h |
| 023 | Implement $choices UI in PromptSchemaForm (Client) | 🔲 | frontend, fluent-ui | 011 | 2h |
| 024 | Implement Canvas-Time Validation | 🔲 | frontend | 013, 023 | 3h |
| 025 | Implement Pre-Execution Server Validation | 🔲 | bff-api, api | 020, 021 | 2h |
| 026 | Unit Tests: $choices and JSON Schema Generation | 🔲 | testing | 020, 021 | 2h |

### Phase 4: Builder Agent Integration (Level 3)

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 030 | Add configure_prompt_schema Tool Definition | 🔲 | bff-api, api | 001 | 2h |
| 031 | Implement configure_prompt_schema Tool Executor | 🔲 | bff-api, api | 030, 002 | 3h |
| 032 | Update Builder Agent System Prompt for JPS | 🔲 | bff-api, api | 030 | 1h |
| 033 | Unit Tests: Builder Agent Tool Execution | 🔲 | testing | 031 | 2h |

### Phase 5: Cross-Scope References + Advanced

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 040 | Implement $knowledge Named Reference Resolution | 🔲 | bff-api, api | 002 | 3h |
| 041 | Implement $skill Named Reference Resolution | 🔲 | bff-api, api | 040 | 1h |
| 042 | Implement promptSchemaOverride Merge with Directives | 🔲 | bff-api, api | 002 | 2h |
| 043 | Create Schema Template Library | 🔲 | bff-api | 001 | 2h |
| 044 | Unit Tests: Scope References and Override Merge | 🔲 | testing | 040, 041, 042 | 2h |

### Wrap-Up

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 090 | Project Wrap-Up | 🔲 | documentation | 006, 014, 026, 033, 044 | 1h |

## Parallel Execution Groups

| Group | Tasks | Prerequisite | Notes |
|-------|-------|-------------|-------|
| **A** | 001-006 + 010-014 | None | Phase 1 (server) + Phase 2 (client) fully parallel |
| **B** | 020-026 + 030-033 + 040-044 | Phase 1 models (001) | Phase 3 + 4 + 5 all parallel (independent of each other) |

### Detailed Parallelism Within Groups

**Group A — Can Start Immediately:**
```
Server stream:  001 → 002 → 003, 004, 005 → 006
Client stream:  010 → 011, 012 → 013 → 014
                      (parallel)
```

**Group B — After Task 001 (models) + 002 (renderer):**
```
Phase 3:  020 → 021 → 022, 025, 026    |  023 → 024
Phase 4:  030 → 031, 032 → 033
Phase 5:  040 → 041, 042 → 044  |  043
```

## Critical Path

```
001 → 002 → 020 → 021 → 022 → 090 (longest dependency chain)
```

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 22 |
| Phase 1 (Server) | 6 tasks, ~12h |
| Phase 2 (Client) | 5 tasks, ~12h |
| Phase 3 ($choices) | 7 tasks, ~17h |
| Phase 4 (Agent) | 4 tasks, ~8h |
| Phase 5 (Advanced) | 5 tasks, ~10h |
| Wrap-up | 1 task, ~1h |
| Total estimate | ~60h |
| Max parallel streams | 2 (Group A), then 3 (Group B) |
