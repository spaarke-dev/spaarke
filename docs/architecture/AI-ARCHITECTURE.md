# Spaarke AI Architecture

> **Last Updated**: 2026-06-26 (canonical-truth loop step 3: scope statement added; Tool Handler / Scope Resolution / Known Pitfalls sections moved out)
> **Last Reviewed**: 2026-06-26
> **Reviewed By**: spaarke-daily-update-service-r4 canonical-truth loop
> **Status**: Current
> **Purpose**: Technical reference for the Spaarke AI 4-tier platform overview — scope library, infrastructure, capability router, safety pipeline, Cosmos persistence, facade boundary.

---

## Scope of this document

This doc covers the **4-tier AI platform overview only**. After the canonical-truth loop (2026-06-26), runtime detail has been moved out:

- **Playbook runtime** (dispatch shapes, mode detection, action lookup, three config columns, scope-array semantics, empty-payload contract, Legacy-mode log catalog, the two parallel orchestrators) → `ai-architecture-playbook-runtime.md` (LOAD-BEARING)
- **Consumer routing & Path A.5** (`sprk_playbookconsumer`, `IConsumerRoutingService`, `IInvokePlaybookAi`) → `ai-architecture-playbook-consumer-routing.md`
- **Where new config fields belong** (Action vs Node vs Playbook decision tree; `sprk_configjson` boundary) → `ai-architecture-actions-nodes-scopes.md`
- **Deploy procedure** (`Deploy-Playbook.ps1` recipe) → `ai-guide-playbook-deploy-recipe.md`

---

## 🆕 Audit findings — bff-ai-architecture-audit-r1 (2026-06-05)

The Spaarke BFF AI Architecture Audit r1 completed 2026-06-04. Four binding architectural decisions now apply to this doc; full evidence is in [`projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md`](../../projects/bff-ai-architecture-audit-r1/notes/canonical-architecture-decisions.md):

| Decision | Where codified | Migration PR |
|---|---|---|
| **Spaarke Public-Contracts Facade DI Fascia** — external CRUD code consumes AI only through `PublicContracts/` facades (per refined ADR-013) | [`.claude/patterns/ai/public-contracts-facade.md`](../../.claude/patterns/ai/public-contracts-facade.md) + [DR-003](../../projects/bff-ai-architecture-audit-r1/decisions/DR-003-public-contracts-facade.md) | PR #351 (LATENT BUG #1 fix + 4 Null peers) |
| **Endpoint↔DI Registration Conditionality Symmetry Rule** — NEW load-bearing rule preventing the LATENT BUG #1 anti-pattern (facade unconditional, transitive deps conditional → 500 instead of 503) | [`.claude/patterns/ai/endpoint-di-symmetry.md`](../../.claude/patterns/ai/endpoint-di-symmetry.md) + [DR-008](../../projects/bff-ai-architecture-audit-r1/decisions/DR-008-di-configuration.md) + [`.claude/constraints/bff-extensions.md` §F.1](../../.claude/constraints/bff-extensions.md) | PR #351 |
| **BFF Canonical Cache Stack** — `IDistributedCache` + `GetOrCreateAsync<T>` only; `EmbeddingCache` canonical model; `MemoryCache` requires explicit ADR-009 exception XML doc | [DR-002](../../projects/bff-ai-architecture-audit-r1/decisions/DR-002-cache-patterns.md) | Phased PR #5+ (26 sites; per-team) |
| **3140 LOC of dead AI code removed** — 3 lookup orphans, intent classifier cascade, 5th orphan, 3 Cat 10 tool handlers, PlaybookBuilderSystemPrompt 800-LOC dead bulk | — | PR #353 + PR #357 |

REJECTED options the audit explicitly considered and locked:
- Generic `IIntentClassifier<TResult>` interface — REJECTED (3 canonicals KEEP, no forced consolidation)
- 4-substrate search consolidation — REJECTED (each substrate justified by different index; KEEP all 4)
- Forced DI module consolidation — REJECTED (31 per-concern modules KEEP)

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
 │  Redis · Service Bus · Cosmos DB · Content Safety                  │
│  AI Foundry (future hosting option)                                │
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

> **Moved**: Tool Handler Framework, handler registration, resolution chain, and `IAnalysisToolHandler` contract have been consolidated into the runtime canonical doc. See [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) §1 (Component model — Layer E) and §5 (Action lookup precedence) for canonical content. See `.claude/patterns/ai/` for code-pointer files.

---

## Scope Resolution

> **Moved**: scope resolution methods, scope types, ownership prefixes, and `$choices` resolution detail have been moved. Runtime semantics (advisory-not-enforcing) live in [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) §6. Config-bag boundary (where scopes belong: Home D N:N relationships, not inline JSON) lives in [`ai-architecture-actions-nodes-scopes.md`](ai-architecture-actions-nodes-scopes.md). `$choices` schema reference lives in [`ai-guide-jps-authoring.md`](../guides/ai-guide-jps-authoring.md).

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

## AI Search Consumer Map

> **Canonical source**: [`AI-SEARCH-INDEX-CATALOG.md`](AI-SEARCH-INDEX-CATALOG.md) — single source of truth for per-index schema, naming convention, property policy, vector config, retired-index history, and post-deploy invariants. **This section is the consumer-map view only**; it does NOT duplicate catalog content. If consumer info here disagrees with the catalog, the catalog wins — open a PR to update this table.

The seven active Spaarke AI Search indexes and their primary BFF consumers (services + endpoints) and data-flow direction. Flow direction = `inbound` (write-only from BFF), `outbound` (read-only from BFF), or `bidirectional`.

| Index name | Primary consumers (services + endpoints) | Data flow direction |
|---|---|---|
| `spaarke-files-index` | `RagService`, `RagIndexingPipeline`, `FileIndexingService`, `IndexRetrieveNode`, `KnowledgeBaseEndpoints`, `BulkRagIndexingJobHandler`, `RagIndexingJobHandler` · endpoints `POST /api/ai/rag/query`, `POST /api/ai/rag/index-file`, semantic search endpoints | bidirectional |
| `spaarke-records-index` | `DataverseIndexSyncService`, `RecordSyncJob`, `RecordSearchAuthorizationFilter` · endpoint `POST /api/ai/search` (scope=entity) | bidirectional |
| `spaarke-rag-references` | `ReferenceIndexingService`, `ReferenceRetrievalService` · ingestion via PowerShell `scripts/ai-search/Add-ReferenceToIndex.ps1` + `Index-AllReferences.ps1` (KNW-*.md golden references); read path via L1 knowledge retrieval | bidirectional |
| `spaarke-insights-index` | `PrecedentProjectionSync` + insights projection pipeline · endpoint `POST /api/ai/insights/search` | bidirectional |
| `spaarke-session-files` | `SessionFilesCleanupJob` (cleanup only) · schema-only in this project per FR-18 (no ingestion path) | outbound (cleanup reads only) |
| `spaarke-invoices-index` | `InvoiceIndexingJobHandler`, `InvoiceSearchService` · schema-only in this project per FR-18 (no ingestion) | outbound (search reads only) |
| `spaarke-playbook-embeddings` | `PlaybookEmbeddingService`, `PlaybookIndexingService`, `PlaybookIndexingBackgroundService`, `PlaybookIndexDriftDetectionJob` · consumed by playbook dispatch routing | bidirectional |

---

## AI Public Contracts Facade Boundary (Phase 4 Outcome E, 2026-05-25)

Refined **ADR-013** (2026-05-20) requires external CRUD code to consume AI capabilities through a **stable, narrow facade**, not by directly injecting AI-internal types like `IOpenAiClient` or `IPlaybookService`. Boundary intent: AI internals stay AI-internal; CRUD-tier code consumes only what it needs through purpose-built interfaces.

### The 4 Facade Interfaces

Located in `src/server/api/Sprk.Bff.Api/Services/Ai/PublicContracts/`:

| Interface | Wraps |
|---|---|
| `IBriefingAi` | `IOpenAiClient.GetCompletionAsync` (narrative generation) |
| `IInvoiceAi` | `IPlaybookService.GetByNameAsync` + `IOpenAiClient.GetStructuredCompletionAsync<T>` + `IOpenAiClient.GenerateEmbeddingAsync` |
| `IRecordMatchingAi` | `IRecordSearchService.SearchAsync` |
| `IWorkspacePrefillAi` | `IPlaybookOrchestrationService.ExecuteAsync` (matter prefill) |

### DI Registration

A new `AddPublicContractsFacade(services)` method in `Infrastructure/DI/AnalysisServicesModule.cs` adds +4 scoped registrations — within the ADR-010 expected +4/+8 Outcome E delta. All four interfaces are gated by the same `documentIntelligenceEnabled && analysisEnabled` flags as the wrapped internal types.

### Migration Scope (Post-Facade)

10 consumer files migrated across Finance (3), Workspace (4), Jobs (1), Dataverse + Filters + Endpoints (2). Net reduction: **148 → 12 occurrences across 59 → 5 files** (92% reduction in direct `IOpenAiClient` / `IPlaybookService` injection from CRUD-side code).

### Documented Boundary Exceptions

5 files remain on direct injection because they ARE the AI API surface, not external CRUD consumers:

| File | Why direct injection is retained |
|---|---|
| `Api/Ai/ChatEndpoints.cs` | Chat API surface (raw AI exposure to clients) |
| `Api/Ai/PlaybookEndpoints.cs` | Playbook CRUD API — 10 handlers that wrap `IPlaybookService` 1:1; facade-wrapping would duplicate the surface |
| `Api/Ai/AiPlaybookBuilderEndpoints.cs` | AI-internal builder for constructing playbooks |
| `Api/Agent/AgentEndpoints.cs` | M365 Copilot agent gateway (playbook-discovery pattern) |
| `Api/Filters/PlaybookAuthorizationFilter.cs` | ADR-008 authorization filter using `IPlaybookService.GetPlaybookAsync(Guid)` for ownership checks |

### AI-Coupled Handler Relocation (FR-E3)

5 files moved from `Services/Jobs/{Handlers,}` → `Services/Ai/Jobs/`:

- `AppOnlyDocumentAnalysisJobHandler`
- `BulkRagIndexingJobHandler`
- `EmailAnalysisJobHandler`
- `ProfileSummaryJobHandler`
- `EmbeddingMigrationService`

Handlers with mixed AI + Dataverse coupling stay in `Services/Jobs/Handlers/` per the G1 reconciliation (AI-coupled = references `Sprk.Bff.Api.Services.Ai.*` AND does NOT require `Spaarke.Dataverse` / `Microsoft.Xrm.Sdk`).

### FR-C6 CI Guard (Task 082, Deferred)

A CI guard will codify the boundary by blocking any new direct `IOpenAiClient` or `IPlaybookService` injection in non-AI-internal modules. This converts Outcome E from a one-time refactor into a permanent architectural boundary.

**References**: [`projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md`](../../projects/sdap-bff-api-remediation-fix/EXECUTION-LOG.md) Phase 4 Outcome E (tasks 046–053) for evidence; [ADR-013](../../.claude/adr/ADR-013-ai-architecture.md) refined 2026-05-20 for binding rule.

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
| Consumed by | **CRUD-tier consumers** (Finance, Workspace, Jobs, Dataverse) | **`Services/Ai/PublicContracts/` facade** (`IBriefingAi`, `IInvoiceAi`, `IRecordMatchingAi`, `IWorkspacePrefillAi`) | **Refined ADR-013 boundary — CRUD code MUST NOT inject `IOpenAiClient` / `IPlaybookService` directly** |
| Depends on | Cosmos DB | `CosmosClient` via `AiPersistenceModule` | Session, audit, feedback, memory, prompt persistence (R2) |
| Depends on | Azure Content Safety | `PromptShieldService`, `GroundednessCheckService` | Prompt injection detection, groundedness annotation (R2) |
| Consumed by | Capability Router | `ICapabilityRouter` via `AiCapabilitiesModule` | Three-tier intent classification for chat turns (R2) |
| Consumed by | Feedback | `FeedbackEndpoints` | Per-response quality feedback collection (R2) |

---

## Capability Router (R2)

The `CapabilityRouter` provides three-tier intent classification to route user messages to the correct AI capability before prompt assembly. Introduced in Spaarke AI Platform Unification R2 (AIPU2-012/013/014).

```
User Message
     │
     ▼
Layer 1: Keyword Classifier (synchronous, <50ms, no I/O)
  ├── Confident (confidence >= threshold)  → Return capability
  └── Uncertain                            → Escalate ↓
     ▼
Layer 2: GPT-4o-mini Intent Classifier (async, JSON-mode, configurable timeout)
  ├── Confident (above threshold)          → Return capability
  ├── Timeout / 429 / parse failure        → Fail through ↓
  └── Below threshold                      → Escalate ↓
     ▼
Layer 3: Broad Superset Fallback
  └── Return union of all capability tool names (capped at MaxSupersetTools)
```

| Component | Path | Responsibility |
|-----------|------|---------------|
| CapabilityRouter | `Services/Ai/Capabilities/CapabilityRouter.cs` | Three-tier classifier: keyword → GPT-4o-mini → superset |
| ICapabilityRouter | `Services/Ai/Capabilities/ICapabilityRouter.cs` | Router interface: `RouteSync`, `RouteAsync`, `Layer3Fallback` |
| CapabilityRouterOptions | `Services/Ai/Capabilities/CapabilityRouterOptions.cs` | Thresholds, Layer 2 toggle, timeout, max candidates |
| CapabilityRoutingResult | `Services/Ai/Capabilities/CapabilityRoutingResult.cs` | Result record: `Confident`, `Uncertain`, `Fallback` factories |
| AiCapabilitiesModule | `Infrastructure/DI/AiCapabilitiesModule.cs` | DI registration for router and manifest |

**Layer 1** scores each capability by keyword hint match ratio plus a weak description-word bonus. Playbook bias multiplier (1.5x) boosts capabilities belonging to the active playbook, with a lower confidence threshold (`PlaybookBiasThreshold`).

**Layer 2** sends a compact classification prompt to GPT-4o-mini with JSON-mode response. Candidates are capped at `MaxCandidates`. Timeout, HTTP 429, and parse failures all fail through to Layer 3 (never block the request).

**Layer 3** computes the union of all tool names across all manifest capabilities (or `GeneralSupersetFallbackTools` if empty), enabling the LLM to self-select tools from the full set.

**OTEL instrumentation**: Activity `capability_router.layer1` / `ai.routing.layer2`; counters `ai_routing_layer1_hit`, `ai_routing_layer2_hit`, `ai_routing_layer3_hit`; histograms for latency. ADR-015: user message content is never logged or recorded in spans.

---

## Safety Pipeline (R2)

The safety perimeter comprises four services that run pre-LLM and post-LLM to detect prompt injection, verify groundedness, validate citations, and enforce privilege boundaries. All services are registered in `AiSafetyModule` (ADR-010 module pattern). Services fail open to preserve availability.

| Service | Path | Stage | Purpose |
|---------|------|-------|---------|
| PromptShieldService | `Services/Ai/Safety/PromptShieldService.cs` | Pre-LLM | Calls Azure AI Content Safety Prompt Shields API to detect prompt injection (user and document attacks). 100ms hard timeout; fail-open on 429/5xx/timeout. |
| GroundednessCheckService | `Services/Ai/Safety/GroundednessCheckService.cs` | Post-LLM | Retroactive groundedness annotation via Azure AI Content Safety. Scores claims against source documents. |
| CitationVerificationService | `Services/Ai/Safety/Citations/CitationVerificationService.cs` | Post-LLM | Verifies citation references against `IVerificationProvider` implementations (e.g. InternalIndexProvider for spaarke-rag-references). |
| PrivilegeGroupResolver | `Services/Ai/Security/PrivilegeGroupResolver.cs` | Pre-LLM | Resolves the user's Dataverse security role memberships to determine which tools and capabilities are authorized. |

**SafetyPipelineMiddleware** (`Services/Ai/Chat/Middleware/SafetyPipelineMiddleware.cs`) orchestrates the pipeline as a decorator on `ISprkChatAgent`. It runs PromptShield pre-LLM and GroundednessCheck + CitationVerification post-LLM.

**Cross-matter safety** (AIPU2-028): `MatterContextDetector` detects when a conversation crosses matter boundaries. `ConversationHistorySanitizer` strips prior matter context from the message history to prevent information leakage.

**Required configuration**:

| Setting | Description |
|---------|-------------|
| `AiSafety:ContentSafety:Endpoint` | Azure AI Content Safety endpoint (default: `https://spaarke-contentsafety-dev.cognitiveservices.azure.com/`) |
| `AiSafety:ContentSafety:ApiKey` | Content Safety API key (supports Key Vault rotation) |

---

## Cosmos DB Persistence (R2)

Session state, audit logs, feedback, memory, and prompt history are persisted to Azure Cosmos DB (serverless, RBAC-only auth via `DefaultAzureCredential`). Registered in `AiPersistenceModule` (ADR-010 module pattern).

**Access pattern**: Write-through (decision D-06: no idle-flush). Redis serves as the hot cache (24h TTL); Cosmos DB is warm storage (90-day retention for most containers, permanent for audit).

| Container | Partition Key | TTL | Purpose | Service |
|-----------|--------------|-----|---------|---------|
| `sessions` | `/userId` | 90 days | AI conversation sessions | SessionPersistenceService |
| `prompts` | `/sessionId` | 90 days | Individual prompt/completion pairs | PromptLibraryService |
| `audit` | `/tenantId` | None (permanent) | Immutable compliance audit trail (ADR-015 Tier 2) | AuditLogService |
| `memory` | `/userId` | 90 days | Per-matter structured AI memory snapshots | MatterMemoryService |
| `feedback` | `/tenantId` | 90 days | User feedback (thumbs up/down) on AI responses | FeedbackService |

**CosmosClient** is registered as Singleton (thread-safe, manages connection pool internally). Uses `CosmosClientBuilder` with `WithConnectionModeDirect()` and throttling retry (30s wait, 9 retries).

**Required configuration**:

| Setting | Description |
|---------|-------------|
| `CosmosPersistence:Endpoint` | Cosmos DB account endpoint URI |
| `CosmosPersistence:DatabaseName` | Target database name (default: `spaarke-ai`) |

---

## Feedback Collection (R2)

`FeedbackService` stores per-response user feedback (thumbs up/down with optional comment) in the Cosmos DB `feedback` container and provides aggregation queries for playbook and capability quality reporting (AIPU2-036).

| Method | Purpose |
|--------|---------|
| `SubmitAsync` | Writes a `FeedbackEntry` to Cosmos DB; enforces 500-char comment cap |
| `GetAggregateByPlaybookAsync` | Counts thumbs-up/down and retrieves top-10 negative comments for a playbook |
| `GetAggregateByCapabilityAsync` | Same aggregation scoped to a capability ID |

**Endpoint**: `POST /api/ai/feedback` / `GET /api/ai/feedback/playbook/{id}` / `GET /api/ai/feedback/capability/{id}` (registered in `FeedbackEndpoints.cs`).

All queries are tenant-scoped (partition key = `/tenantId`). Aggregation queries use Cosmos SQL with parameterized filters to prevent injection.

---

## Known Pitfalls

> **Moved**: tool-handler-specific pitfalls (HttpContext propagation, missing handler registration, SSE flush timing, Scoped-vs-Singleton DI captive dependency, `GenericAnalysisHandler` fallback) and playbook runtime pitfalls G1-G12 have been consolidated into [`ai-architecture-playbook-runtime.md`](ai-architecture-playbook-runtime.md) §10. G12 (Spaarke `sprk_event`/`sprk_communication` rule, not OOB activity entities) was added 2026-06-25.

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
| Three-tier capability routing | Keyword → GPT-4o-mini → superset fallback | Fast sync path for common intents; LLM escalation only when needed | AIPU2-012 |
| Fail-open safety perimeter | PromptShield returns FailOpen on timeout/429/5xx | Availability over blocking; safety events logged for review | AIPU2-020 |
| Write-through Cosmos persistence | Redis hot (24h) + Cosmos warm (90d) | No idle-flush complexity; dual-write guarantees durability | AIPU2-030 |
| RBAC-only Cosmos auth | DefaultAzureCredential, no connection strings | No secrets in app settings; managed identity only | AIPU2-002 |

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
| 2026-05-17 | 5.0 | R2 additions: Capability Router (3-tier), Safety Pipeline (PromptShield, Groundedness, Citations, privilege filter), Cosmos DB persistence (5 containers, write-through), Feedback Collection. Updated Tier 4, integration points, design decisions. |
| 2026-04-05 | 4.0 | Restored depth: tool handler framework internals, handler registration, streaming paths, scope resolution, knowledge retrieval, integration points, known pitfalls. Restructured to mandatory architecture doc format. |
| 2026-03-13 | 3.4 | Added DeliverToIndex node (ActionType 41). |
| 2026-03-06 | 3.3 | Added JSON Prompt Schema (JPS) documentation: $choices dynamic enum resolution with 5 Dataverse prefix types. |
| 2026-03-03 | 3.2 | Updated for typed field mappings: UpdateRecord OData PATCH with typed coercion. |
| 2026-03-01 | 3.1 | Updated for Playbook Builder R5: three-level node type system, Code Page builder as primary. |
| 2026-02-21 | 3.0 | Created from consolidation of AI-PLAYBOOK-ARCHITECTURE.md (v2.0) and AI-ANALYSIS-PLAYBOOK-SCOPE-DESIGN.md (v2.0). |
