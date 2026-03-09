# Task Index — ai-json-prompt-schema-system

> **Total Tasks**: 28
> **Status**: Complete

## Task Registry

### Phase 1: Schema Format + Renderer (Server Only)

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 001 | Create C# PromptSchema Record Models | ✅ | bff-api, models | none | 2h |
| 002 | Create PromptSchemaRenderer Service with Format Detection | ✅ | bff-api, api | 001 | 3h |
| 003 | Integrate Renderer into GenericAnalysisHandler | ✅ | bff-api, refactor | 002 | 2h |
| 004 | DI Registration and Service Wiring | ✅ | bff-api, api | 002 | 1h |
| 005 | Unit Tests: Models and Format Detection | ✅ | testing | 002 | 2h |
| 006 | Unit Tests: Rendering Pipeline and Section Assembly | ✅ | testing | 005 | 2h |

### Phase 2: PlaybookBuilder UI (Level 1 + Level 2)

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 010 | Create TypeScript PromptSchema Type Definitions | ✅ | frontend, typescript | none | 1h |
| 011 | Create PromptSchemaForm Component (Level 1) | ✅ | frontend, fluent-ui | 010 | 4h |
| 012 | Create PromptSchemaEditor Component (Level 2) | ✅ | frontend, fluent-ui | 010 | 3h |
| 013 | Integrate Prompt Schema into Properties Panel | ✅ | frontend, fluent-ui | 011, 012 | 2h |
| 014 | Schema Serialization in playbookNodeSync | ✅ | frontend | 013 | 2h |

### Phase 3: Structured Output + $choices

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 020 | Implement $choices Reference Resolution (Server) | ✅ | bff-api, api | 002 | 3h |
| 021 | Implement JSON Schema Draft-07 Generation | ✅ | bff-api, api | 001, 020 | 3h |
| 022 | Integrate Structured Output with Azure OpenAI | ✅ | bff-api, api | 021 | 2h |
| 023 | Implement $choices UI in PromptSchemaForm (Client) | ✅ | frontend, fluent-ui | 011 | 2h |
| 024 | Implement Canvas-Time Validation | ✅ | frontend | 013, 023 | 3h |
| 025 | Implement Pre-Execution Server Validation | ✅ | bff-api, api | 020, 021 | 2h |
| 026 | Unit Tests: $choices and JSON Schema Generation | ✅ | testing | 020, 021 | 2h |

### Phase 4: Builder Agent Integration (Level 3)

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 030 | Add configure_prompt_schema Tool Definition | ✅ | bff-api, api | 001 | 2h |
| 031 | Implement configure_prompt_schema Tool Executor | ✅ | bff-api, api | 030, 002 | 3h |
| 032 | Update Builder Agent System Prompt for JPS | ✅ | bff-api, api | 030 | 1h |
| 033 | Unit Tests: Builder Agent Tool Execution | ✅ | testing | 031 | 2h |

### Phase 5: Cross-Scope References + Advanced

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 040 | Implement $knowledge Named Reference Resolution | ✅ | bff-api, api | 002 | 3h |
| 041 | Implement $skill Named Reference Resolution | ✅ | bff-api, api | 040 | 1h |
| 042 | Implement promptSchemaOverride Merge with Directives | ✅ | bff-api, api | 002 | 2h |
| 043 | Create Schema Template Library | ✅ | bff-api | 001 | 2h |
| 044 | Unit Tests: Scope References and Override Merge | ✅ | testing | 040, 041, 042 | 2h |

### Wrap-Up

| # | Title | Status | Tags | Deps | Est |
|---|-------|--------|------|------|-----|
| 090 | Project Wrap-Up | ✅ | documentation | all | 1h |

## Summary

| Metric | Value |
|--------|-------|
| Total tasks | 28 |
| Completed | 28 |
| Tests passing | 76 (PromptSchema-related) |
| Build status | 0 errors, 0 warnings |
