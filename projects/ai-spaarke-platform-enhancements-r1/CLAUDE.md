# CLAUDE.md — AI Platform Foundation Phase 1

> **Project**: ai-spaarke-platform-enhancements-r1
> **Last Updated**: 2026-02-22
> **Status**: In Progress

## Project Context

Building the AI platform foundation for June 2026 product launch. Four parallel workstreams:
- **A**: Retrieval Foundation (LlamaParse, SemanticDocumentChunker, RagIndexingPipeline, KnowledgeBaseEndpoints)
- **B**: Scope Library & Seed Data (8 Actions, 10 Skills, 10 Knowledge Sources, 8 Tools, 10 Playbooks, ScopeConfigEditorPCF)
- **C**: SprkChat & Agent Framework (IChatClient, SprkChatAgent, ChatEndpoints SSE, SprkChat React component, AnalysisWorkspace integration)
- **D**: End-to-End Validation (test corpus, E2E tests, evaluation harness, quality baseline)

## Key Files

| File | Purpose |
|------|---------|
| [spec.md](spec.md) | Full technical specification |
| [plan.md](plan.md) | Phase breakdown and dependencies |
| [tasks/TASK-INDEX.md](tasks/TASK-INDEX.md) | All tasks with status |
| [current-task.md](current-task.md) | Active task state (context recovery) |

## Applicable ADRs

| ADR | Constraint | Workstreams |
|-----|-----------|-------------|
| ADR-001 | No Azure Functions; use Minimal API + BackgroundService | A, C, D |
| ADR-002 | No AI/HTTP in plugins; seed data is records only | B |
| ADR-004 | Idempotent handlers; deterministic IdempotencyKey; emit JobOutcome | A |
| ADR-006 | New UI as PCF controls; no legacy webresources | B, C |
| ADR-007 | All document access via SpeFileStore; no Graph SDK leakage | A |
| ADR-008 | Endpoint filters for authorization on all new endpoints | A, C, D |
| ADR-009 | Redis IDistributedCache; version cache keys; short TTL | A, C |
| ADR-010 | Concrete types; <= 15 non-framework DI registrations | All |
| ADR-012 | SprkChat in @spaarke/ui-components; 90%+ test coverage | B, C |
| ADR-013 | Extend BFF, not separate AI service; rate limit AI endpoints | All |
| ADR-014 | Tenant-scoped cache keys; version by model + content | A, C |
| ADR-021 | Fluent UI v9 only; design tokens; dark mode; WCAG 2.1 AA | B, C |
| ADR-022 | React 16 APIs only; platform-library manifest; unmanaged solutions | B, C |

## Critical Implementation Rules

### API / BFF

```csharp
// ✅ DO: Use Minimal API for new endpoints
app.MapGroup("/api/ai/chat").MapChatEndpoints();

// ✅ DO: Use endpoint filters for authorization
app.MapPost("/api/ai/chat/sessions", CreateSession)
   .AddEndpointFilter<AiAuthorizationFilter>();

// ✅ DO: BackgroundService + Service Bus for indexing
// RagIndexingJobHandler : BackgroundJobHandlerBase<RagIndexingJob>

// ✅ DO: Concrete types in DI (unless seam genuinely required)
services.AddSingleton<RagQueryBuilder>();
services.AddSingleton<SemanticDocumentChunker>();

// ✅ DO: Tenant-scoped cache keys
var key = $"chat:session:{tenantId}:{sessionId}";

// ❌ DON'T: Create Azure Functions
// ❌ DON'T: Call Graph SDK directly (use SpeFileStore)
// ❌ DON'T: Global auth middleware (use endpoint filters)
// ❌ DON'T: Exceed 15 non-framework DI registrations
```

### PCF / Frontend

```typescript
// ✅ DO: Fluent UI v9 exclusively
import { Button, Input, makeStyles } from "@fluentui/react-components";

// ✅ DO: React 16 APIs (ReactDOM.render, not createRoot)
ReactDOM.render(<ScopeConfigEditorApp {...props} />, container);

// ✅ DO: Place SprkChat in shared library
// src/client/shared/Spaarke.UI.Components/src/components/SprkChat/

// ✅ DO: CodeMirror for JSON editing (not Monaco) — bundle < 1MB
import CodeMirror from "@codemirror/...";

// ❌ DON'T: Use Monaco editor (too large for PCF bundle)
// ❌ DON'T: Call Azure AI services directly from PCF
// ❌ DON'T: Use createRoot (React 18 API)
```

### Agent Framework Pattern

```csharp
// ✅ DO: Register IChatClient via Agent Framework
services.AddAzureOpenAIChatClient(config["AzureOpenAI:Endpoint"], ...)
    .AsAIAgent();

// ✅ DO: Register chat tools via AIFunctionFactory
var tools = AIFunctionFactory.Create(DocumentSearchTools.SearchDocuments);

// ✅ DO: Use SSE streaming for chat responses
// Pattern: AnalysisEndpoints.cs StreamAnalysis method
```

## New Files Being Created

### Workstream A
- `Services/Ai/RagQueryBuilder.cs` — metadata-aware RAG query strategy
- `Services/Ai/IDocumentChunker.cs` + `SemanticDocumentChunker.cs` — clause-aware chunking
- `Services/Ai/DocumentParserRouter.cs` + `LlamaParseClient.cs` — dual-parser
- `Services/Ai/RagIndexingPipeline.cs` — indexing orchestrator
- `Services/Jobs/Handlers/RagIndexingJobHandler.cs` — background indexing
- `Api/Ai/KnowledgeBaseEndpoints.cs` — KB management API

### Workstream C
- `Services/Ai/Chat/SprkChatAgent.cs`
- `Services/Ai/Chat/SprkChatAgentFactory.cs`
- `Services/Ai/Chat/IChatContextProvider.cs`
- `Services/Ai/Chat/ChatSessionManager.cs`
- `Services/Ai/Chat/ChatHistoryManager.cs`
- `Services/Ai/Chat/Tools/DocumentSearchTools.cs`
- `Services/Ai/Chat/Tools/AnalysisQueryTools.cs`
- `Services/Ai/Chat/Tools/KnowledgeRetrievalTools.cs`
- `Services/Ai/Chat/Tools/TextRefinementTools.cs`
- `Api/Ai/ChatEndpoints.cs`
- `Api/Ai/EvaluationEndpoints.cs`

### Workstream B (PCF)
- `src/client/pcf/ScopeConfigEditor/` — new PCF control

### Shared Component
- `src/client/shared/Spaarke.UI.Components/src/components/SprkChat/` — new component

## Files Being Modified

- `Services/Ai/AnalysisOrchestrationService.cs` — replace first-500-chars query (A1), remove old chat path (C4)
- `Services/Ai/DocumentIntelligenceService.cs` — wire dual-parser + indexing pipeline (A3, A4)
- `src/client/pcf/AnalysisWorkspace/` — integrate SprkChat replacing current chat panel (C6)
- `Program.cs` — register new services and endpoints

## Patterns to Follow

| Pattern | Location | Use For |
|---------|----------|---------|
| SSE streaming endpoint | `Api/Ai/AnalysisEndpoints.cs` | ChatEndpoints SSE responses |
| AI orchestration | `Services/Ai/AnalysisOrchestrationService.cs` | SprkChatAgent orchestration pattern |
| RAG search | `Services/Ai/RagService.cs` | RagQueryBuilder integration |
| Job handler | Existing job handlers in `Services/Jobs/Handlers/` | RagIndexingJobHandler pattern |
| PCF with Fluent v9 | `src/client/pcf/AnalysisWorkspace/` | ScopeConfigEditorPCF base pattern |
| Shared component | `src/client/shared/Spaarke.UI.Components/` | SprkChat component structure |

## Package Versions (Pinned)

| Package | Version | Notes |
|---------|---------|-------|
| `Microsoft.Extensions.AI` | `10.3.0` | AI abstractions (IChatClient, AIFunction, etc.) |
| `Microsoft.Extensions.AI.OpenAI` | `10.3.0` | OpenAI/Azure OpenAI → IChatClient adapter bridge (added AIPL-050) |
| `Microsoft.Agents.AI` | `1.0.0-rc1` | Agent Framework RC — shipped Feb 19, 2026 |
| `OpenAI` | `2.8.0` | Pinned: required by Microsoft.Extensions.AI.OpenAI 10.3.0 (added AIPL-050) |
| `Microsoft.Extensions.Caching.Abstractions` | `10.0.3` | Bumped from 10.0.1 to satisfy Microsoft.Extensions.AI 10.3.0 dependency chain |

- Added: 2026-02-23 (Phase 1, AIPL-002); updated 2026-02-23 (AIPL-050)
- **CRITICAL**: Do NOT upgrade `Microsoft.Agents.AI` or `Microsoft.Extensions.AI` without full build + test verification. The RC → GA transition may have breaking API changes.
- `Microsoft.Agents.AI 1.0.0-rc1` requires `Microsoft.Extensions.AI >= 10.3.0` — these two packages are tightly coupled and must be upgraded together.
- `Microsoft.Extensions.AI.OpenAI 10.3.0` requires `OpenAI >= 2.8.0`; `Azure.AI.OpenAI 2.1.0` depends on OpenAI >= 2.1.0 (compatible with 2.8.0). All three must be upgraded together.

## DI Registration Budget (ADR-010)

- **Baseline (Phase 1)**: 89 non-framework registrations in Program.cs
- **Budget**: ≤15 per ADR-010 (aspirational — Program.cs has grown beyond the module pattern over prior projects)
- **Status**: ADR-010 budget already exceeded by prior projects; new Workstream A/B/C services MUST use feature module extensions (e.g., `AddAiPlatformModule`) — do NOT add inline `AddScoped`/`AddSingleton` calls directly to Program.cs
- **Expected additions**: ~10-12 new services across Workstreams A, B, C — all must go into module extension methods
- **Note**: `Configure<T>` bindings (LlamaParseOptions, AiSearchOptions added in AIPL-004) do NOT count — they are Microsoft framework calls
- **Current count (AIPL-052)**: 99 — IChatDataverseRepository/ChatDataverseRepository (scoped) + ChatSessionManager (scoped) + ChatHistoryManager (scoped) added in AiModule.cs (2026-02-23)
- **Tracked**: Updated after each phase by implementing task (AIPL-004 baseline audit: 2026-02-23; AIPL-050 update: 2026-02-23; AIPL-051 update: 2026-02-23; AIPL-052 update: 2026-02-23)

## Important Context

- **Agent Framework**: `Microsoft.Agents.AI 1.0.0-rc1` pinned; API surface stable per spec; do NOT upgrade without testing
- **LlamaParse**: Optional enhancement with mandatory fallback to Azure Doc Intel; API key stored in Key Vault as `LlamaParseApiKey`; config in `appsettings.template.json` with `"Enabled": false` by default
- **CodeMirror over Monaco**: ScopeConfigEditorPCF must stay < 1MB bundle (Monaco is 4MB+)
- **DI count**: 89 non-framework registrations at Phase 1 baseline (see DI Registration Budget section above); new services must go into feature modules
- **Dataverse entities**: `sprk_aichatmessage` and `sprk_aichatsummary` schema defined in task 001
- **Old chat endpoint**: `/api/ai/analysis/{id}/continue` deprecated in C6; keep in parallel during transition

## 🚨 MANDATORY: Task Execution Protocol

When executing project tasks, ALWAYS invoke the `task-execute` skill. DO NOT read POML files directly.

| Trigger | Required Action |
|---------|----------------|
| "work on task X" | Invoke task-execute with task X POML file |
| "continue" / "next task" | Check TASK-INDEX.md for next 🔲, invoke task-execute |
| "resume task X" | Invoke task-execute with task X POML file |

**Bypassing task-execute leads to**: missing ADR constraints, no checkpointing, skipped quality gates.
