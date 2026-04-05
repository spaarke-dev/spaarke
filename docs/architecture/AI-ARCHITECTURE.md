# Spaarke AI Architecture

> **Last Updated**: April 5, 2026
> **Purpose**: Technical reference for the Spaarke AI platform — scope library, tool framework, execution runtime, and infrastructure.

---

## Overview

Spaarke AI provides document analysis, knowledge retrieval, and conversational AI capabilities as an extension of the BFF API (ADR-013). The architecture separates reusable AI primitives (scopes) from the execution machinery that runs them, enabling configuration-driven AI workflows without code deployment. The key design decision is the **four-tier separation**: scopes are independent of any execution engine, composition patterns define how scopes are assembled, the runtime executes them, and Azure infrastructure provides the backing services.

Two handler interface hierarchies coexist: `IAnalysisToolHandler` for the tool handler registry (analysis pipeline, playbook nodes), and `IAiToolHandler` for playbook workflow tool handlers (simpler interface with `ToolName` + `ExecuteAsync`). Both are registered in DI and resolved at runtime.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| AnalysisOrchestrationService | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisOrchestrationService.cs` | Top-level orchestrator: routes to action-based or playbook-based execution, streams SSE |
| ToolHandlerRegistry | `src/server/api/Sprk.Bff.Api/Services/Ai/ToolHandlerRegistry.cs` | Indexes all `IAnalysisToolHandler` by HandlerId and ToolType; supports config-based disabling |
| ToolFrameworkExtensions | `src/server/api/Sprk.Bff.Api/Services/Ai/ToolFrameworkExtensions.cs` | DI registration: assembly-scans for handlers, registers registry as Scoped |
| GenericAnalysisHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/GenericAnalysisHandler.cs` | Configuration-driven handler (95% of tools); supports JPS, structured output, streaming |
| IAnalysisToolHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/IAnalysisToolHandler.cs` | Handler interface: HandlerId, Metadata, Validate, ExecuteAsync |
| IStreamingAnalysisToolHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/IStreamingAnalysisToolHandler.cs` | Opt-in streaming: `StreamExecuteAsync` yields `ToolStreamEvent.Token` then `Completed` |
| IAiToolHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/IAiToolHandler.cs` | Simpler playbook tool interface: ToolName + ExecuteAsync(ToolParameters) |
| ScopeResolverService | `src/server/api/Sprk.Bff.Api/Services/Ai/IScopeResolverService.cs` | Loads scopes from Dataverse by ID, playbook, or node; CRUD; $choices resolution |
| AnalysisContextBuilder | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisContextBuilder.cs` | Prompt assembly: Action.SystemPrompt + Skill fragments + Knowledge + Document |
| AiAnalysisNodeExecutor | `src/server/api/Sprk.Bff.Api/Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Bridges playbook nodes to IToolHandlerRegistry; L1/L2/L3 knowledge retrieval |
| AnalysisEndpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/AnalysisEndpoints.cs` | SSE streaming endpoints: execute, continue, save |
| HandlerEndpoints | `src/server/api/Sprk.Bff.Api/Api/Ai/HandlerEndpoints.cs` | Handler discovery: `/api/ai/handlers` (registry metadata) + `/api/ai/tools/handlers` (class names) |
| AnalysisDocumentLoader | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisDocumentLoader.cs` | Document retrieval, text extraction, analysis caching |
| AnalysisRagProcessor | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisRagProcessor.cs` | RAG search, cache key computation, tenant resolution |
| AnalysisResultPersistence | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisResultPersistence.cs` | Output storage, RAG indexing enqueue, working doc finalization |
| IOpenAiClient | `src/server/api/Sprk.Bff.Api/Services/Ai/IOpenAiClient.cs` | Azure OpenAI abstraction: streaming, structured, vision, embeddings, tool-calling |
| SprkChatAgent | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/SprkChatAgent.cs` | Conversational AI agent with playbook-driven context |
| PlaybookChatContextProvider | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookChatContextProvider.cs` | Resolves scopes to chat agent tools at runtime |

---

## Four-Tier Architecture

```
 ┌─────────────────────────────────────────────────────────────────────┐
 │  TIER 1: SCOPE LIBRARY (Spaarke IP)                                │
 │  Reusable AI primitives stored in Dataverse                        │
 │  Actions · Skills · Knowledge · Tools · Outputs                    │
 │  Independent of any execution engine                               │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 2: COMPOSITION PATTERNS                                      │
 │  How scopes are assembled and invoked                              │
 │  Playbooks (visual canvas)  ·  SprkChat (conversational)           │
 │  Standalone invocation (API)  ·  Background jobs                   │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 3: EXECUTION RUNTIME                                         │
 │  Where AI logic actually runs                                      │
 │  In-Process (current) → Microsoft Agent Framework (future)         │
 │  PlaybookExecutionEngine · PlaybookOrchestrationService            │
 ├─────────────────────────────────────────────────────────────────────┤
 │  TIER 4: AZURE INFRASTRUCTURE                                      │
 │  Cloud services backing everything                                 │
 │  Azure OpenAI · Azure AI Search · Document Intelligence            │
 │  Redis · Service Bus · AI Foundry (future hosting option)          │
 └─────────────────────────────────────────────────────────────────────┘
```

### Key Architectural Principles

1. **Playbooks are the "frontend"** — the Spaarke-specific composition and management UI for AI workflows. The execution backend is flexible.
2. **Scopes are independent primitives** — consumable by playbooks, SprkChat, standalone API calls, and background jobs without requiring a playbook.
3. **AI nodes are backend-flexible** — a node can execute in-process (current), via Microsoft Agent Framework (future), or as a published AI Foundry agent (future).
4. **Workflow nodes stay Spaarke** — CreateTask, SendEmail, UpdateRecord, Condition, DeliverOutput, DeliverToIndex nodes always run as Spaarke code.
5. **AI Foundry is infrastructure** — it provides model hosting, Foundry IQ knowledge bases, and Agent Service runtime. It does not compete with the scope library.

---

## Data Flow

### Analysis Execution (Action-Based, No Playbook)

1. `POST /api/ai/analysis/execute` receives `AnalysisExecuteRequest` with document IDs, action ID, and scope IDs
2. `AnalysisEndpoints` sets SSE response headers and iterates `AnalysisOrchestrationService.ExecuteAnalysisAsync`
3. If `PlaybookId` is set: delegates to `ExecutePlaybookAsync` (playbook path); otherwise continues with action-based path
4. `AnalysisDocumentLoader` retrieves document from Dataverse, downloads file from SPE via `ISpeFileOperations` (OBO auth), extracts text via `ITextExtractor`
5. `IScopeResolverService.ResolveScopesAsync` loads skills, knowledge, tools from Dataverse in parallel with `GetActionAsync`
6. `AnalysisRagProcessor.ProcessRagKnowledgeAsync` queries Azure AI Search for RAG knowledge sources
7. `AnalysisContextBuilder.BuildSystemPrompt` assembles: Action.SystemPrompt + Skill.PromptFragments; `BuildUserPromptAsync` assembles: document text + RAG context
8. `IOpenAiClient.StreamCompletionAsync` streams tokens; each token is yielded as `AnalysisStreamChunk.TextChunk` (SSE)
9. `AnalysisResultPersistence` writes working document periodically (every 500 chars) and finalizes to Dataverse
10. RAG indexing job is enqueued via Service Bus for post-analysis indexing

### Playbook Node Execution (via AiAnalysisNodeExecutor)

1. `PlaybookOrchestrationService` topologically sorts the node graph and executes nodes in parallel batches
2. For AI nodes, `AiAnalysisNodeExecutor.ExecuteAsync` is called with `NodeExecutionContext`
3. Executor resolves `IToolHandlerRegistry` from a new DI scope (registry is Scoped, executor is Singleton)
4. Three-tier knowledge retrieval runs: L1 (ReferenceRetrievalService), L2 (IRagService, optional), L3 (IRecordSearchService, optional)
5. `LookupChoicesResolver.ResolveFromJpsAsync` pre-resolves `$choices` Dataverse lookups for constrained decoding
6. `ToolExecutionContext` is built with merged knowledge, resolved choices, and document text
7. Handler is looked up by `tool.HandlerClass` from registry; validated; then executed
8. **Streaming path**: if handler implements `IStreamingAnalysisToolHandler` and caller provided `OnTokenReceived` callback, uses `StreamExecuteAsync` yielding `ToolStreamEvent.Token` events
9. **Blocking path**: otherwise calls `IAnalysisToolHandler.ExecuteAsync` returning `ToolResult`

---

## Tool Handler Framework (Tier 3 Detail)

### Handler Registration

`ToolFrameworkExtensions.AddToolFramework()` is called at startup:
1. Configures `ToolFrameworkOptions` from `appsettings.json`
2. Registers `PromptSchemaRenderer` (singleton) and `LookupChoicesResolver` (scoped)
3. Assembly-scans for all `IAnalysisToolHandler` implementations and registers them as Scoped
4. Registers `ToolHandlerRegistry` as Scoped (receives `IEnumerable<IAnalysisToolHandler>` via DI)

### Handler Resolution Chain

```
Tier 1: Configuration (Dataverse)
  sprk_analysistool.sprk_handlerclass → handler name (optional)
         │
         ▼
Tier 2: GenericAnalysisHandler (95% of cases)
  If handlerclass is NULL or not found → GenericAnalysisHandler
  Configuration-driven: operation, prompt_template, output_schema, temperature
  No code deployment required for new tools
         │
         ▼
Tier 3: Custom Handlers (complex scenarios)
  Registered in DI at startup via ToolFrameworkExtensions assembly scanning
```

### ToolHandlerRegistry Internals

- Receives all `IAnalysisToolHandler` instances via constructor injection
- Indexes by `HandlerId` (case-insensitive `ConcurrentDictionary`) and by `ToolType`
- `ToolFrameworkOptions.DisabledHandlers` suppresses specific handlers without removing code
- `GetHandler(handlerId)` returns null (not exception) when handler is missing or disabled
- `GetAllHandlerInfo()` returns `ToolHandlerInfo` records with metadata, supported types, and enabled state

### IAnalysisToolHandler Contract

Every handler must provide:
- `HandlerId` — matches `sprk_analysistool.sprk_handlerclass` in Dataverse
- `Metadata` — `ToolHandlerMetadata` record with Name, Description, Version, SupportedInputTypes, Parameters, optional ConfigurationSchema (JSON Schema Draft 07)
- `SupportedToolTypes` — list of `ToolType` enum values (EntityExtractor, ClauseAnalyzer, DocumentClassifier, Summary, RiskDetector, ClauseComparison, DateExtractor, FinancialCalculator, Custom)
- `Validate(context, tool)` — returns `ToolValidationResult` (fail fast before execution)
- `ExecuteAsync(context, tool, ct)` — returns `ToolResult`

### Streaming Handlers

Handlers that implement `IStreamingAnalysisToolHandler` (extends `IAnalysisToolHandler`) provide:
- `StreamExecuteAsync(context, tool, ct)` — yields `IAsyncEnumerable<ToolStreamEvent>`
- Events: `ToolStreamEvent.Token(text)` for each token, then `ToolStreamEvent.Completed(result)` with final `ToolResult`
- Tokens are forwarded immediately to SSE — no buffering (ADR-014)
- Non-streaming handlers continue to work unchanged via `ExecuteAsync`

### GenericAnalysisHandler

The default handler for 95% of tools. Implements `IStreamingAnalysisToolHandler`:
- Reads tool configuration from `AnalysisTool.Configuration` JSON
- Supported operations: `extract`, `classify`, `validate`, `generate`, `transform`, `analyze`
- Resolves JPS (JSON Prompt Schema) or flat-text prompts from Action.SystemPrompt
- Uses `PromptSchemaRenderer` for JPS rendering with `$choices` injection
- Calls `IOpenAiClient.GetStructuredCompletionRawAsync` for structured output or `StreamCompletionAsync` for text
- Model selection via `ModelSelectorOptions.DefaultModel` (default: gpt-4o)

---

## Scope Resolution

`IScopeResolverService` loads AI primitives from Dataverse:

| Method | Resolution Source |
|--------|-------------------|
| `ResolveScopesAsync(skillIds, knowledgeIds, toolIds)` | Explicit IDs |
| `ResolvePlaybookScopesAsync(playbookId)` | Playbook N:N relationships |
| `ResolveNodeScopesAsync(nodeId)` | Node-level N:N + single tool lookup |

### Scope Types

| Scope | Entity | Purpose |
|-------|--------|---------|
| **Actions** | `sprk_analysisaction` | System prompt templates (flat text or JPS) |
| **Skills** | `sprk_analysisskill` | Prompt fragments appended as instructions |
| **Knowledge** | `sprk_analysisknowledge` | RAG context: Inline, Document, or RagIndex |
| **Tools** | `sprk_analysistool` | Handler class + configuration for execution |

### Scope Ownership

| Prefix | OwnerType | Mutable | Description |
|--------|-----------|---------|-------------|
| `SYS-` | System | No | Spaarke-provided, immutable |
| `CUST-` | Customer | Yes | Customer-created or extended |

Scopes support inheritance via `ParentScopeId` (extends parent) and `BasedOnId` (cloned via SaveAs).

### $choices — Dynamic Enum Resolution

JPS output fields declare `"$choices"` to auto-inject valid enum values at render time, constraining AI output via JSON Schema `"enum"`:

| Prefix | Resolution Source |
|--------|-------------------|
| `lookup:` | Active records from Dataverse reference entity |
| `optionset:` | Single-select choice/picklist metadata labels |
| `multiselect:` | Multi-select picklist metadata labels |
| `boolean:` | Two-option boolean field labels |
| `downstream:` | Downstream UpdateRecord node field mapping options |

---

## Knowledge-Augmented Execution

The `AiAnalysisNodeExecutor` retrieves tiered knowledge before calling the LLM:

```
AiAnalysisNodeExecutor
  ├── L1: ReferenceRetrievalService — curated domain knowledge (spaarke-rag-references index)
  ├── L2: IRagService — similar customer docs (spaarke-knowledge-index-v2, optional)
  ├── L3: IRecordSearchService — business entity metadata (optional)
  └── Merge → KnowledgeContext → Prompt assembly
```

Retrieval mode is configured per-node via `ConfigJson` (`auto`/`always`/`never`, default: auto with TopK=5).

### RAG Pipeline

**Search flow**: Query → EmbeddingCache (Redis, SHA256 keys, 7-day TTL) → Azure OpenAI (embedding, text-embedding-3-large, 3072-dim) → Azure AI Search (hybrid: BM25 + Vector + Semantic) → Security filter (tenantId) → Semantic reranking → Results.

**Search indexes**:
- `spaarke-knowledge-index-v2` — Customer documents (3072-dim, HNSW, cosine)
- `spaarke-rag-references` — Golden reference knowledge (3072-dim, HNSW, cosine)

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | SPE / Documents | `ISpeFileOperations` via `AnalysisDocumentLoader` | OBO auth for document download and text extraction |
| Depends on | Dataverse | `DataverseHttpServiceBase` (OData) | Scope loading, analysis persistence, record updates |
| Depends on | Azure OpenAI | `IOpenAiClient` | Completions, structured output, embeddings, vision |
| Depends on | Azure AI Search | `IRagService`, `ReferenceRetrievalService` | Hybrid search with security filters |
| Depends on | Redis | `IEmbeddingCache`, analysis caching | Embedding cache, analysis state cache |
| Depends on | Service Bus | `AnalysisResultPersistence` | Enqueues RAG indexing jobs post-analysis |
| Consumed by | Playbook System | `AiAnalysisNodeExecutor` → `IToolHandlerRegistry` | Node executor bridges playbook nodes to tool handlers |
| Consumed by | SprkChat | `PlaybookChatContextProvider` → `IChatContextProvider` | Resolves scopes to agent tools for conversational AI |
| Consumed by | PCF / Code Pages | `AnalysisEndpoints` (SSE) | Frontend consumes SSE token stream |
| Consumed by | Scope Config Editor | `HandlerEndpoints` | Handler discovery for dropdown population |

---

## Known Pitfalls

1. **HttpContext not propagated to node executors** — `AiAnalysisNodeExecutor` is Singleton but `IToolHandlerRegistry` is Scoped. The executor creates a new DI scope per execution (`_serviceProvider.CreateScope()`). HttpContext is **not** available in this child scope. Any handler that needs user identity must receive it via `NodeExecutionContext`/`ToolExecutionContext`, not from `IHttpContextAccessor`.

2. **Missing tool handler registration** — If a new `IAnalysisToolHandler` implementation is added but not in the scanned assembly, or its class is abstract, `ToolFrameworkExtensions.AddToolHandlersFromAssembly` silently skips it. The node executor logs a detailed error: `"Tool handler '{HandlerClass}' not found. Available handlers: [...]"`.

3. **SSE flush timing** — The streaming endpoints in `AnalysisEndpoints` iterate `IAsyncEnumerable<AnalysisStreamChunk>` and write each chunk as an SSE event. If the response stream is not flushed after each write, tokens buffer at the HTTP layer and appear in bursts rather than streaming. The working document update triggers every 500 characters regardless of flush state.

4. **Scoped registry vs Singleton executor** — `ToolHandlerRegistry` is registered as Scoped (to match handler lifetimes that may inject scoped services like `IScopeResolverService`). `AiAnalysisNodeExecutor` is Singleton (required by `NodeExecutorRegistry`). The executor **must** create a scope to resolve the registry — injecting `IToolHandlerRegistry` directly into a Singleton would cause a captive dependency.

5. **GenericAnalysisHandler fallback** — When `sprk_analysistool.sprk_handlerclass` is NULL or empty, `AnalysisToolService.GetToolAsync` defaults it to `"GenericAnalysisHandler"`. This means tools created without a handler class silently use the generic handler. This is by design but can mask misconfiguration.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| AI as BFF extension | Extend Minimal API, no separate service | Reuse auth, DI, observability infrastructure | ADR-013 |
| Configuration-driven tools | GenericAnalysisHandler as default | New tools without code deployment | ADR-013 |
| Assembly-scanned handler registration | ToolFrameworkExtensions auto-discovers handlers | Eliminates manual registration, follows ADR-010 module pattern | ADR-010 |
| Scoped handler registry | ToolHandlerRegistry is Scoped, not Singleton | Handlers may inject scoped services (IScopeResolverService) | ADR-010 |
| Redis-first caching | EmbeddingCache with SHA256 keys, 7-day TTL | Avoid redundant embedding API calls | ADR-009 |
| Per-node error isolation | ToolResult captures errors without aborting playbook | Soft failure: other nodes continue executing | ADR-016 |
| Dual output paths | Analysis Output (RTF) + Document Fields (JSON) | Different consumers need different formats | ADR-014 |
| Endpoint filters for auth | AiAuthorizationFilter per endpoint | No global middleware; fine-grained resource checks | ADR-008 |

---

## Constraints

- **MUST** extend BFF API for all AI endpoints; no separate AI microservice (ADR-013)
- **MUST NOT** leak Graph SDK types above SpeFileStore facade (ADR-007)
- **MUST** use endpoint filters for authorization, not global middleware (ADR-008)
- **MUST** use Redis as primary cache for embeddings and search results (ADR-009)
- **MUST** register tool handlers via ToolFrameworkExtensions; keep DI registrations minimal (ADR-010)
- **MUST** log at each execution step for observability (ADR-015)
- **MUST** isolate per-node errors; do not abort entire playbook on single node failure (ADR-016)
- **MUST NOT** hardcode model names; use ModelSelectorOptions configuration (ADR-013)

---

## Related

- [Playbook Architecture](playbook-architecture.md) — Node type system, execution engine, canvas data model
- [`.claude/adr/ADR-013-ai-architecture.md`](../../.claude/adr/ADR-013-ai-architecture.md) — AI Tool Framework constraints
- [`.claude/patterns/ai/`](../../.claude/patterns/ai/) — Pattern pointers for AI code entry points
- [JPS Authoring Guide](../guides/JPS-AUTHORING-GUIDE.md) — JPS schema, $choices, structured output
- [Scope Configuration Guide](../guides/SCOPE-CONFIGURATION-GUIDE.md) — Scope CRUD, builder UI
- [Azure AI Resources](auth-AI-azure-resources.md) — Endpoints, models, CLI commands

---

## Changelog

| Date | Version | Change |
|------|---------|--------|
| 2026-04-05 | 4.0 | Restored depth: tool handler framework internals, handler registration, streaming paths, scope resolution, knowledge retrieval, integration points, known pitfalls. Restructured to mandatory architecture doc format. |
| 2026-03-13 | 3.4 | Added DeliverToIndex node (ActionType 41). |
| 2026-03-06 | 3.3 | Added JSON Prompt Schema (JPS) documentation: $choices dynamic enum resolution with 5 Dataverse prefix types. |
| 2026-03-03 | 3.2 | Updated for typed field mappings: UpdateRecord OData PATCH with typed coercion. |
| 2026-03-01 | 3.1 | Updated for Playbook Builder R5: three-level node type system, Code Page builder as primary. |
| 2026-02-21 | 3.0 | Created from consolidation of AI-PLAYBOOK-ARCHITECTURE.md (v2.0) and AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md (v2.0). |
