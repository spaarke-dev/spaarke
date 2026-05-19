# AI Subsystem Inventory — Insights Engine Perspective

> **Date**: 2026-05-19
> **Scope**: `src/server/api/Sprk.Bff.Api/Services/Ai/`, `Api/Ai/`, `Services/RecordMatching/`
> **Lens**: What exists today, what's reusable for the Insights Engine, what's stub/intentional-default, what to flag as concerning.
> **Method**: DI-registration-anchored. Authoritative source: `Program.cs` → `AddAnalysisServicesModule` + `AddAiModule`. Files registered in DI are presumed active; files NOT registered are flagged for closer review.

## TL;DR

| Question | Answer |
|---|---|
| Is there already a Dataverse → AI Search sync service? | **Yes.** `DataverseIndexSyncService` (in `Services/RecordMatching/`) syncs `sprk_matter`, `sprk_project`, `sprk_invoice` to `spaarke-records-index`. The Insights Engine sync work should **extend this pattern**, not start from scratch. |
| How many AI Search indexes already exist (referenced in code)? | **At least 4**: `spaarke-records-index` (Dataverse entity records), `spaarke-rag-references` (golden reference knowledge), plus a "knowledge index (512-token)" and "discovery index (1024-token)" referenced in `RagIndexingPipeline`. |
| Is the DI registration well-managed? | **Yes** — `AiModule.cs` (290 lines) has a maintained registration-count audit and explicit ADR-010 compliance. 15 unconditional + 4 conditional registrations. |
| Most reusable foundations for the Insights Engine | `DataverseIndexSyncService`, `RagIndexingPipeline`, `ReferenceIndexingService`, `RagService`, `EmbeddingCache`, `SemanticDocumentChunker`, `RagQueryBuilder`, `RecordSearchService` |
| Are there many dead-code files? | **Not obviously.** The "Fallback*" and "NoOp*" files are intentional defaults (well-documented). Only 2 files contain `NotImplementedException` references (`ChatContextMappingService.cs`, `ScopeManagementService.cs`) — and one is likely a `throw new NotImplementedException()` inside a switch's default branch, not a stub. |
| Notable architectural debt | (1) `PlaybookIndexingBackgroundService` was created as a workaround for ADR-001's old no-Functions rule — with the rule narrowed, this could move to a Function if it fits the out-of-band criteria. (2) `DataverseIndexSyncService` is HTTP-coupled to BFF lifecycle; the Insights Engine version should be event-driven (Functions + Service Bus). |

## The DI registration picture

The authoritative source for what's actually wired is `Program.cs` calling these module extensions:

| Module | Purpose | File | Registrations |
|---|---|---|---|
| `AddSpaarkeCore` | Core (AuthorizationService, RequestCache) | `SpaarkeCore.cs` | Core only |
| `AddAnalysisServicesModule` | Analysis orchestration, playbooks, builder, testing, delivery, nodes, RAG, tool framework | `AnalysisServicesModule.cs` (224 lines) | ~50+ services across 8 sub-methods |
| `AddAiModule` | AI Platform Foundation (chat client, doc parsing, chunking, RAG, chat session lifecycle) | `AiModule.cs` (290 lines) | 15 unconditional + 4 conditional |
| `AddJobProcessingModule` | Job handlers, Service Bus, AI platform options | `JobProcessingModule.cs` (85 lines) | Multiple handlers |
| `AddAgentModule` | M365 Copilot Agent gateway, auth, cards, conversation, telemetry | `AgentModule.cs` (64 lines) | M365 Copilot agent path |

`AiModule.cs` has a registration-count audit comment at the bottom (lines 252-290) that lists every registration with its purpose and ADR reference. This is the easiest way to see what's in the AI subsystem.

## Existing AI Search indexes — what's in production today

Found via grep across the codebase. These are all referenced in `RagIndexingPipeline`, `ReferenceIndexingService`, `DataverseIndexSyncService`, and `AiSearchOptions`:

| Index name | Source service | Purpose | Used by |
|---|---|---|---|
| `spaarke-records-index` | `DataverseIndexSyncService` | Dataverse entity records (Matters, Projects, Invoices) | `RecordSearchService` (hybrid search), `AiAnalysisNodeExecutor` (L3 context lookup) |
| `spaarke-rag-references` | `ReferenceIndexingService` | Golden reference knowledge (system + user-uploaded) | `ReferenceRetrievalService`, `AdminKnowledgeEndpoints` |
| Knowledge index (512-token chunks) | `RagIndexingPipeline` | Customer document content | `RagService` |
| Discovery index (1024-token chunks) | `RagIndexingPipeline` | Customer document content (broader chunks) | `RagService` |

**Implication for Insights Engine**: rather than create entirely new infrastructure, the Engine adds new indexes (e.g., `insight-matters`, `insight-decisions`, `insight-risks`, `insight-sessions`) following the same patterns. Sync uses an extension of `DataverseIndexSyncService` (or a new Function-based equivalent).

## Reusable foundations for the Insights Engine

These are production-grade services the Engine should **compose**, not reimplement:

### 1. Dataverse sync — `DataverseIndexSyncService` (already production)

`Services/RecordMatching/DataverseIndexSyncService.cs`
- Currently syncs: `sprk_matter`, `sprk_project`, `sprk_invoice` → `spaarke-records-index`
- Has `EntityConfig` pattern with `EntitySetName`, `NameField`, `DescriptionField`, `ReferenceField`, `SelectFields` — easy to extend to additional entity types
- Uses `TokenCredential` (DefaultAzureCredential) — Managed Identity ready
- Registered as: `HttpClient` + `Singleton<IDataverseIndexSyncService>` when `DocumentIntelligence:RecordMatchingEnabled` = true
- **Insights Engine plan**: Either extend this service (more entities, more indexes) OR replace its trigger from BFF call to event-driven Function. Given the new ADR-001 freedom for out-of-band Functions, the latter is the right direction.

### 2. RAG indexing pipeline — `RagIndexingPipeline`

`Services/Ai/RagIndexingPipeline.cs`
- Full pipeline: chunk → embed → index
- Targets both knowledge index (512-token) and discovery index (1024-token)
- Called by `RagIndexingJobHandler` via Service Bus
- Singleton, conditional on `DocumentIntelligence:Enabled`
- **Insights Engine plan**: Reuse for content-chunked Observations. Closure-extraction playbook can emit chunks that flow through this same pipeline.

### 3. Reference knowledge — `ReferenceIndexingService` + `ReferenceRetrievalService`

`Services/Ai/ReferenceIndexingService.cs`, `Services/Ai/ReferenceRetrievalService.cs`
- Indexes "golden reference" knowledge sources (512-token chunks, 100-token overlap, 3072-dim embeddings) into `spaarke-rag-references`
- Idempotent (deletes existing chunks before re-indexing)
- Has admin-only endpoints (`AdminKnowledgeEndpoints`) and Dataverse-driven bulk indexing
- **Insights Engine plan**: This pattern is the closest analog to how Insight Observations should be stored — small, structured, embedding-enriched, idempotent. The Engine's `insight-*` indexes should mirror this architecture.

### 4. RAG search — `RagService` + `RagQueryBuilder` + `EmbeddingCache`

`Services/Ai/RagService.cs`, `Services/Ai/RagQueryBuilder.cs`, `Services/Ai/EmbeddingCache.cs`
- Hybrid keyword + vector + semantic-reranker search
- `RagQueryBuilder` builds metadata-aware queries from analysis results
- `EmbeddingCache` caches embeddings (reduces OpenAI cost)
- All singleton, conditional on `DocumentIntelligence:Enabled`
- **Insights Engine plan**: The Insights Agent's retrieval layer should use `RagService` (or a thin wrapper) to query the new `insight-*` indexes. No new search service needed.

### 5. Chunking — `SemanticDocumentChunker` + `TextChunkingService`

`Services/Ai/SemanticDocumentChunker.cs`, `Services/Ai/TextChunkingService.cs`
- `SemanticDocumentChunker`: clause-aware (operates on `AnalyzeResult` from Document Intelligence)
- `TextChunkingService`: generic text chunking
- Singleton, thread-safe, stateless
- **Insights Engine plan**: Reuse for splitting longer Observations (closure summaries, decision rationale). Probably no new chunking logic needed.

### 6. Record search — `RecordSearchService`

`Services/Ai/RecordSearch/RecordSearchService.cs`
- Hybrid semantic search against `spaarke-records-index`
- Used by `RecordSearchEndpoints` and `AiAnalysisNodeExecutor`
- **Insights Engine plan**: Reusable as-is for the Tier 1 (Record) layer of the Engine. The new `insight-matters` index follows the same pattern; this service can be templated.

### 7. Embedding / OpenAI client — `OpenAiClient` + `IOpenAiClient`

`Services/Ai/EmbeddingCache.cs` (related infrastructure)
- Single OpenAI client abstraction (singleton)
- Used by every embedding-producing service
- **Insights Engine plan**: Reuse for closure-extraction LLM calls and Insights Agent synthesis calls.

### 8. Playbook execution — `PlaybookExecutionEngine` + `INodeExecutor` registry

`Services/Ai/PlaybookExecutionEngine.cs`, `Services/Ai/Nodes/*`
- 10 node executors registered: CreateTask, SendEmail, UpdateRecord, DeliverOutput, DeliverToIndex, Condition, AiAnalysis, CreateNotification, QueryDataverse, AgentService
- `DeliverToIndexNodeExecutor` writes playbook output to AI Search indexes — **directly relevant to closure-extraction playbook**
- **Insights Engine plan**: Closure-extraction is just a JPS playbook that ends with a `DeliverToIndex` node targeting an `insight-*` index. No new infrastructure needed — write the playbook.

### 9. Tool framework — `IAiToolHandler`, `IToolHandlerRegistry`, `ToolExecutionContext`

`Services/Ai/IAiToolHandler.cs`, `Services/Ai/ToolExecutionContext.cs`, `Services/Ai/Tools/*`
- Pattern for AI tool calls (used by SemanticSearchToolHandler, SendCommunicationToolHandler, AnalysisQueryTools, AnalysisExecutionTools, TextRefinementTools, KnowledgeRetrievalTools, DocumentSearchTools)
- Provides typed tool definitions for the AI agent
- **Insights Engine plan**: The Insights Agent's tools (FindComparableMatters, GetMatterFacts, AssessEvidenceSufficiency, etc.) should follow this existing pattern. Don't reinvent the tool registry.

### 10. Chat / agent infrastructure — `SprkChatAgentFactory` + `ChatSessionManager` + `ChatHistoryManager`

`AiModule.cs` registrations 8, 11, 12
- Chat session lifecycle (Redis + Dataverse)
- `IChatClient` with `UseFunctionInvocation` pipeline (Microsoft.Extensions.AI)
- `PendingPlanManager` for plan-preview gating (compound intent detection)
- **Insights Engine plan**: The Insights Agent reuses `IChatClient` + tool-calling pipeline. Probably needs a new agent factory variant (or a tool-only invocation path without session/history overhead, depending on whether Insights questions go through chat or via direct API).

## Stubs and intentional defaults (NOT dead code)

These are well-documented placeholders; do not remove without understanding context:

| File | Classification | Why it exists |
|---|---|---|
| `FallbackPrompts.cs` | STUB / FALLBACK | Provides prompt strings when Dataverse is unavailable. Builder service uses these as graceful fallback. |
| `FallbackScopeCatalog.cs` | STUB / FALLBACK | Provides scope catalog entries when Dataverse scopes haven't been populated. Lets builder respond to "what skills are available?" |
| `SemanticSearch/NoOpQueryPreprocessor.cs` | NO-OP DEFAULT | Default preprocessor when no semantic enrichment is configured. |
| `SemanticSearch/NoOpResultPostprocessor.cs` | NO-OP DEFAULT | Default postprocessor when no result transformation is configured. |
| `Testing/MockDataGenerator.cs` | TEST INFRASTRUCTURE | Test data generation for analysis testing. Registered in `AddTestingServices`. |
| `Testing/MockTestExecutor.cs` | TEST INFRASTRUCTURE | Mock test execution path. Registered. |
| `Testing/TempBlobStorageService.cs` | TEST INFRASTRUCTURE | Temporary blob storage for test artifacts. Registered conditionally on storage connection string. |

These represent ~7 files of intentional defaults / test infrastructure — not waste.

## Concerning findings to flag

### 1. `PlaybookIndexingBackgroundService` was forced into BackgroundService by the old ADR-001

From `AiModule.cs` line 39 and 237:
```
PlaybookIndexingBackgroundService — hosted service (ADR-001 mandate, no Azure Functions).
Processes playbook embedding indexing requests from a bounded Channel<string>.
```

With the new ADR-001 (commit `84cec9f9`) permitting Functions for out-of-band integration, this is a candidate to migrate to a Function — if its trigger semantics fit better there (event-driven indexing of playbook changes). Not urgent, but worth a follow-up evaluation.

### 2. `DataverseIndexSyncService` is HTTP-coupled to BFF lifecycle

The current sync service runs inside the BFF process. For the Insights Engine's multi-index sync needs, this should move to an event-driven Function App that subscribes to Dataverse change events (Service Bus or webhooks) and writes to AI Search.

**Migration path** (not required for Insights Engine MVP, but desirable):
- Keep `DataverseIndexSyncService` running for `spaarke-records-index` (don't break what works)
- New Function App handles `insight-*` indexes from the start
- Eventually consolidate the BFF version into the Function App when the Engine stabilizes

### 3. `RecordSearchAuthorizationFilter` notes that `spaarke-records-index` has NO `tenantId` field

From `Api/Filters/RecordSearchAuthorizationFilter.cs:43`:
> `Note: The spaarke-records-index does NOT have a tenantId field.`

This is a multi-tenant correctness concern. If Spaarke ships to multiple customer tenants and they share an index, that's a privilege boundary problem. The Insights Engine should NOT make this mistake — new indexes must have `tenantId` (and likely `matterId`, `partyId` for filter trimming) as first-class fields.

### 4. Two `NotImplementedException` references

`Services/Ai/Chat/ChatContextMappingService.cs` and `Services/Ai/ScopeManagementService.cs` contain `NotImplementedException` references. Likely guard clauses on unimplemented branches (e.g., switch defaults), not full stubs — but worth a 5-minute confirmation before relying on either service in the Insights Engine path. Both are registered in DI.

### 5. Multi-modal account inconsistency (from azure-inventory.md)

- Dev OpenAI account: `spaarke-openai-dev` is kind `AIServices` (newer multi-service)
- Prod OpenAI account: `spaarke-openai-prod` is kind `OpenAI` (older OpenAI-only)
- Code uses `IOpenAiClient` abstraction — likely tolerant — but inconsistency may surface for new capabilities (Document Intelligence integrated, multi-model routing). Worth aligning dev → AIServices if features are needed.

## What's NOT in the inventory (deliberately scoped out)

- **PCF / frontend AI surfaces** — not in scope; the Insights Engine is server-side.
- **Dataverse plugins** — not in scope.
- **JPS schema authoring** — separate project; covered in JPS playbook design.
- **Specific job handlers** — covered in `JobProcessingModule.cs` audit (separate doc if needed).
- **M365 Copilot Agent integration** — covered in `AgentModule.cs`; tangential to Insights Engine.

## Method note

This inventory is **DI-registration-anchored** rather than file-system-anchored. The justification: a file's existence proves nothing about its production use; its DI registration (plus call sites from endpoints / job handlers / hosted services) proves it's wired up. Files not registered or referenced are flagged for closer review but not exhaustively catalogued — a per-file walkthrough was attempted via a subagent and timed out at 14 minutes. The DI-anchored method produces an actionable inventory in a fraction of the time, at the cost of skipping deeply orphaned files.

If exhaustive file-by-file review is needed later, the recommended approach is:
1. List all `.cs` files under `Services/Ai/`
2. Cross-reference against grep for each file's primary type as a referenced symbol
3. Flag any type that's never referenced outside its declaring file
4. Run that as a smaller, more contained subagent task (or a script).

## Decisions this inventory enables

1. **Sync foundation reuses `DataverseIndexSyncService` as a pattern**, not as a service to copy. New event-driven Function-based sync for `insight-*` indexes follows its `EntityConfig` model but runs out-of-band.
2. **Closure-extraction is a JPS playbook ending with a `DeliverToIndex` node** — no new orchestrator needed.
3. **The Insights Agent reuses `IChatClient` + tool framework + `RagService`** — no new agent host needed.
4. **New `insight-*` indexes follow `spaarke-rag-references` schema patterns** (small structured docs + embedding + idempotency) — not the chunked-document patterns used for customer content.
5. **`tenantId` MUST be a first-class field on every new index** — the existing `spaarke-records-index` omits it, but the Insights Engine cannot.
6. **Migrate `PlaybookIndexingBackgroundService` to a Function** is on the post-MVP cleanup list, not the MVP path.
