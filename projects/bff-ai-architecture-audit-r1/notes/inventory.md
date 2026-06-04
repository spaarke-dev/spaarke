# BFF AI Infrastructure — Comprehensive Inventory

> **Snapshot taken at**: commit `357e6936` (origin/master: "Merge pull request #339 from spaarke-dev/work/ai-spaarke-insights-engine-r2-wave-f") on 2026-06-04.
> **Authored by**: Phase 1 sub-agent for `bff-ai-architecture-audit-r1`.
> **Status**: COMPLETED (Phase 1 of 4).
> **Scope**: `src/server/api/Sprk.Bff.Api/Services/Ai/` (357 .cs files), `Infrastructure/DI/` (34 modules), `Configuration/` (25 files), `Options/` (2 files), with cross-references to `infrastructure/ai-search/`, `infra/insights/`, `scripts/ai-search/`.
>
> **Note on the brief's "baseline" doc**: `projects/bff-ai-architecture-audit-r1/notes/initial-findings.md` does NOT exist in this worktree (only `decisions/` subdir is present). Inventory built directly from the brief's enumerated baseline categories and extended via systematic file walk + grep on the snapshot.

---

## §1 Methodology applied

1. **Snapshot fix**: `git log --oneline origin/master -1` → `357e6936`. All findings are against this commit.
2. **File enumeration**: `find Services/Ai -name "*.cs" -type f | wc -l` → **357** (vs initial-findings estimate ~340). 6 categories from brief, expanded to 11 in §2.
3. **Consumer mapping**: `Grep` with `output_mode=files_with_matches` against `src/` to identify production consumers (test files explicitly excluded from "active" calculus — see §6).
4. **DI capture**: read all 34 `Infrastructure/DI/*Module.cs` files, captured registration mechanism (`AddSingleton`/`AddScoped`/`AddHostedService`/`AddHttpClient`/keyed/factory-instantiated/not-registered) and which feature gate guards each registration.
5. **Configuration capture**: enumerate all `Configuration/*Options.cs` + `Options/*Options.cs` (25 + 2 = 27 options classes); cross-reference with services that inject them.
6. **State classification**: empirical via consumer count (0 non-test consumers + no Hosted/HttpClient pipeline → candidate "unused"; replaced by Null-Object peer → "feature-gated"; ≥2 non-test consumers → "active").
7. **Category assignment**: 6 baseline (intent classification / lookup services / search services / cache patterns / prompt builders / DI+config) + 5 discovered (Null-Object kill-switches / Public Contracts facade / Node executors / Foundry agent / Insights extraction pipeline).
8. **Drift acknowledgment**: per owner direction, parallel projects may add code during this audit; all Phase 2 analysis works against commit `357e6936` and explicitly disregards later additions.

---

## §2 Service catalog

### §2.1 Intent classification (Category 1) — 4 parallel systems

This is the headline finding: four implementations with overlapping semantics, registered through different modules, with different lifetimes and dependencies.

#### §2.1.1 `CapabilityRouter` (three-tier classifier)
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/Capabilities/CapabilityRouter.cs`
2. **Purpose**: Layer 1 of three-tier capability router (AIPU2-012). Synchronous keyword classifier — score = matched_hints / total_hints; confidence = top/(top+second+ε); <50ms target. Layer 2 (LLM) injected via `IChatClient("raw")` keyed service. Layer 3 fallback inlined.
3. **Consumers**: `Chat/SprkChatAgentFactory.cs`, `Chat/NullSprkChatAgentFactory.cs`, `Telemetry/AiLatencyTracker.cs`, `Telemetry/AiLatencyTelemetry.cs` (4 production consumers).
4. **State**: **ACTIVE** — production-wired in SprkChatAgentFactory's tool selection path.
5. **Originating project**: Spaarke AI Platform Unification R2 (AIPU2-012/013/014 per inline XML doc).
6. **DI registration**: Singleton (factory-instantiated with `GetKeyedService<IChatClient>("raw")` to gracefully degrade if AI off). Module: `AiCapabilitiesModule.cs:117-123`. Also exposed as `ICapabilityRouter`.
7. **Configuration**: `IOptions<CapabilityRouterOptions>` bound from `Capabilities:Router` section.
8. **Category**: Intent classification.

#### §2.1.2 `PlaybookDispatcher` (two-stage vector + LLM)
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/PlaybookDispatcher.cs`
2. **Purpose**: Two-stage playbook intent matching (SprkChat r2-015). Stage 1: vector similarity search via `PlaybookEmbeddingService` against `playbook-embeddings` index (1.5s budget; ≥0.85 short-circuits). Stage 2: LLM refinement + parameter extraction (0.5s budget). Output-type enrichment from DeliverOutput node.
3. **Consumers**: `Api/Ai/ChatEndpoints.cs`, `Chat/SprkChatAgentFactory.cs`, `Chat/NullSprkChatAgentFactory.cs`, `Chat/PlaybookOutputHandler.cs` (4 production consumers via factory).
4. **State**: **ACTIVE** — factory-instantiated, not DI-registered (by design per its own XML doc and ADR-010).
5. **Originating project**: SprkChat r2 (task r2-015 per XML doc).
6. **DI registration**: **NOT REGISTERED** — `SprkChatAgentFactory` instantiates via `new PlaybookDispatcher(...)`. ADR-010 compliant intentional choice.
7. **Configuration**: Inline constants (HighConfidenceThreshold=0.85, Stage1Timeout=1500ms, Stage2Timeout=500ms, TotalTimeout=2s, CacheTtl=5min). Uses Redis (`IDistributedCache`) for dispatch result caching tied to playbook catalog version.
8. **Category**: Intent classification.

#### §2.1.3 `IntentClassificationService` (playbook builder, 11 categories)
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/IntentClassificationService.cs`
2. **Purpose**: Classifies user intent into 11 categories (CREATE_PLAYBOOK, ADD_NODE, etc.) for the AI Chat Playbook Builder. 0.75 confidence threshold. Hardcoded model `gpt-4o-mini`.
3. **Consumers**: NONE in production source code beyond its DI registration. `Grep IIntentClassificationService` → only `AnalysisServicesModule.cs` (registration) and `IntentClassificationService.cs` (self) — interface defined in same dir.
4. **State**: **UNUSED / ORPHANED** — registered (`AnalysisServicesModule.cs:372`) but no consumers grep'd in `src/`. Strong candidate for deprecation or repurposing.
5. **Originating project**: AI Chat Playbook Builder (per XML doc "AI Chat Playbook Builder design").
6. **DI registration**: Scoped via `AddBuilderServices` helper in `AnalysisServicesModule.cs:372` (compound `Analysis:Enabled && DocumentIntelligence:Enabled` gate).
7. **Configuration**: No `IOptions<T>` — hardcoded model `gpt-4o-mini`. No Null-Object peer.
8. **Category**: Intent classification.

#### §2.1.4 `InsightsIntentClassifier` (Insights Engine, JSON-schema-constrained)
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/Insights/Routing/InsightsIntentClassifier.cs`
2. **Purpose**: Phase 1.5 LLM-based routing between Insights playbook synthesis (`/api/insights/ask`) and open-ended RAG retrieval (`/api/insights/search`). JSON-schema-constrained output; SHA-256 cache key per normalized query; 500ms FR-05 budget.
3. **Consumers**: `Services/Ai/Insights/AssistantToolCallHandler.cs` (Wave E3 task 042). Mock-friendly via `IInsightsIntentClassifier`. Null peer registered ALWAYS (forward-compat for Wave E3 unconditional consumer).
4. **State**: **ACTIVE (gated)** — real impl behind compound AI gate + fine-grained `Insights:IntentClassifier:Enabled` opt-out. `NullInsightsIntentClassifier` registered in compound-OFF branch + when fine-gate disabled.
5. **Originating project**: Insights Engine r2 Wave E2 task 041 (FR-05) per XML doc.
6. **DI registration**: Singleton via `AddInsightsIntentClassifier` helper in `AnalysisServicesModule.cs:508-534`. Real impl OR `NullInsightsIntentClassifier` based on `InsightsIntentClassifierOptions.Enabled`. Null-Object always registered in `AddNullObjectsForCompoundOff` as well (ADR-032 §F.1 forward-mitigation).
7. **Configuration**: `IOptions<InsightsIntentClassifierOptions>` bound from `Insights:IntentClassifier` section. Fields: `ConfidenceThreshold=0.7`, `CacheTtlMinutes=15`, `Model=null` (defers to `IOpenAiClient.SummarizeModel`), `MaxOutputTokens=80`, `Enabled=true`.
8. **Category**: Intent classification.

**Cross-cutting note for Category 1**: All 4 classifiers depend on `IOpenAiClient` (Spaarke's own facade — see §2.4 OpenAiClient). They differ in:
- **Caching backend**: CapabilityRouter (in-process via Manifest snapshot), PlaybookDispatcher (Redis via `IDistributedCache`), IntentClassificationService (NONE), InsightsIntentClassifier (in-process `IMemoryCache`).
- **Output shape**: CapabilityRouter returns `CapabilityRoutingResult`, PlaybookDispatcher returns `DispatchResult`, IntentClassificationService returns `IntentClassificationResult`, InsightsIntentClassifier returns `IntentClassificationResult` (same name as #3, different shape!).
- **Multi-stage**: Only CapabilityRouter (3-tier) and PlaybookDispatcher (2-stage) cascade. The other two are single-LLM-call.

---

### §2.2 Lookup services (Category 2) — 4 near-identical implementations

#### §2.2.1 `PlaybookLookupService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookLookupService.cs`
2. **Purpose**: Cached lookup by portable alternate key (`sprk_playbookcode`). 1-hour TTL via `IMemoryCache`. Lines 1-40 are functionally identical (except entity name + cache prefix `playbook:code:`) to the other three.
3. **Consumers**: `Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`, `Services/Ai/Chat/DefaultPlaybookConstants.cs` (2 production consumers).
4. **State**: **ACTIVE** (low consumer count but used in Finance + chat default-playbook resolution).
5. **Originating project**: Finance Intelligence (per FinanceModule comment "Playbook Lookup Service (cached alternate key lookups for SaaS portability)").
6. **DI registration**: Scoped in `FinanceModule.cs:114`.
7. **Configuration**: Hardcoded constants (`CacheDuration=1hr`, `CacheKeyPrefix="playbook:code:"`).
8. **Category**: Lookup services.

#### §2.2.2 `ActionLookupService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/ActionLookupService.cs`
2. **Purpose**: Same pattern as §2.2.1 for `sprk_actioncode` alternate key.
3. **Consumers**: ONLY `FinanceModule.cs` registration. No production consumers.
4. **State**: **UNUSED / ORPHANED** (DI-registered, no `Grep` hits in non-test code).
5. **Originating project**: Finance Intelligence.
6. **DI registration**: Scoped in `FinanceModule.cs:123`.
7. **Configuration**: Hardcoded constants (`action:code:` prefix).
8. **Category**: Lookup services.

#### §2.2.3 `SkillLookupService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/SkillLookupService.cs`
2. **Purpose**: Same pattern for `sprk_skillcode`.
3. **Consumers**: ONLY `FinanceModule.cs` registration.
4. **State**: **UNUSED / ORPHANED**.
5. **Originating project**: Finance Intelligence.
6. **DI registration**: Scoped in `FinanceModule.cs:132`.
7. **Configuration**: Hardcoded constants (`skill:code:` prefix).
8. **Category**: Lookup services.

#### §2.2.4 `ToolLookupService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/ToolLookupService.cs`
2. **Purpose**: Same pattern for `sprk_toolcode`.
3. **Consumers**: ONLY `FinanceModule.cs` registration.
4. **State**: **UNUSED / ORPHANED**.
5. **Originating project**: Finance Intelligence.
6. **DI registration**: Scoped in `FinanceModule.cs:141`.
7. **Configuration**: Hardcoded constants (`tool:code:` prefix).
8. **Category**: Lookup services.

**Cross-cutting note for Category 2**: All four are line-for-line near-identical (≤5-line diff each, all in entity name + cache prefix). All depend on `IGenericEntityService` + `IMemoryCache`. All have 1-hour TTL. The XML docstrings differ only in entity noun. Three of four have ZERO consumers — classic DRY violation + dead code.

---

### §2.3 Search services (Category 3) — 4 distinct substrates

#### §2.3.1 `RagService` / `IRagService` / `NullRagService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` (+ `IRagService.cs`, `NullRagService.cs`).
2. **Purpose**: Hybrid RAG search (keyword + vector + semantic ranking) against the knowledge index. P95 <500ms target. Integrates with `IKnowledgeDeploymentService` for multi-tenant routing.
3. **Consumers**: 38 files reference `IRagService` (largest consumer count of any AI service). Major: `RagEndpoints`, `KnowledgeBaseEndpoints`, `AnalysisRagProcessor`, `Chat/Tools/DocumentSearchTools`, `Chat/Tools/KnowledgeRetrievalTools`, `RagIndexingPipeline`, `AiAnalysisNodeExecutor`.
4. **State**: **ACTIVE** (most-consumed). Has P3 Fail-Fast Null-Object peer for AI-Search-keys-missing fallback.
5. **Originating project**: AIPL (AI Platform; per `RagIndexingPipeline` reference).
6. **DI registration**: Singleton in `AnalysisServicesModule.AddRagServices` (line 550) when `DocumentIntelligence:AiSearchEndpoint/Key` set; otherwise `NullRagService` (line 561). Compound-OFF branch also registers Null (line 223).
7. **Configuration**: `IOptions<DocumentIntelligenceOptions>` (legacy keys) + `IOptions<AiSearchOptions>` (newer; `Options/AiSearchOptions.cs`). Asymmetric — pre-dates `Options/` directory pattern.
8. **Category**: Search services.

#### §2.3.2 `SemanticSearchService` / `ISemanticSearchService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/SemanticSearch/SemanticSearchService.cs`
2. **Purpose**: Hybrid semantic search via Azure AI Search with RRF (Reciprocal Rank Fusion), vector-only, keyword-only modes. Embedding-failure fallback to keyword-only. Pipeline of `IQueryPreprocessor` / `IResultPostprocessor` (no-ops in R1).
3. **Consumers**: `Api/Ai/SemanticSearchEndpoints.cs`, `Services/Ai/Tools/SemanticSearchToolHandler.cs`, `Services/Ai/Tools/DocumentClassifierHandler.cs`.
4. **State**: **ACTIVE**.
5. **Originating project**: AIPU R1 (SemanticSearchExtensions naming pattern).
6. **DI registration**: Via `services.AddSemanticSearch()` extension in `AnalysisServicesModule.cs:55`. Inside the compound AI gate.
7. **Configuration**: Uses `IKnowledgeDeploymentService` for index routing; no direct options class.
8. **Category**: Search services.

#### §2.3.3 `RecordSearchService` / `IRecordSearchService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/RecordSearch/RecordSearchService.cs`
2. **Purpose**: Hybrid semantic search against `spaarke-records-index` (Dataverse entity records, NOT documents). RRF + vector + keyword. NOT tenant-isolated (XML doc warns: "The spaarke-records-index does NOT have a tenantId field; security is enforced at Dataverse layer").
3. **Consumers**: `Api/Ai/RecordSearchEndpoints.cs`, `Services/Ai/PublicContracts/RecordMatchingAi.cs`.
4. **State**: **ACTIVE**.
5. **Originating project**: Record-matching feature (DocumentIntelligence:RecordMatchingEnabled gate).
6. **DI registration**: Via `services.AddRecordSearch()` extension in `AnalysisServicesModule.cs:58`. Inside compound AI gate.
7. **Configuration**: Uses `DocumentIntelligenceOptions.AiSearchIndexName` (defaults to `spaarke-records-index`).
8. **Category**: Search services.

#### §2.3.4 `PlaybookEmbeddingService`
1. **Path**: `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs`
2. **Purpose**: Embeddings for playbook content + manages `playbook-embeddings` AI Search index. Vector: `text-embedding-3-large` (3072 dim, HNSW cosine).
3. **Consumers**: `PlaybookEmbedding/PlaybookIndexingBackgroundService.cs`, `PlaybookEmbedding/PlaybookIndexingService.cs`, `Chat/PlaybookDispatcher.cs`.
4. **State**: **ACTIVE** (factory-instantiated per XML doc explicit "NOT registered in DI ... ADR-010 budget constraint").
5. **Originating project**: SprkChat r2 (companion to PlaybookDispatcher).
6. **DI registration**: **NOT REGISTERED** (XML doc: "BFF is at 16 non-framework registrations, over the ≤15 limit ... factory-instantiated"). Instantiated by `PlaybookIndexingBackgroundService` + `PlaybookDispatcher`.
7. **Configuration**: Hardcoded index name + vector field; no options class.
8. **Category**: Search services.

**Cross-cutting note for Category 3**: All four hit Azure AI Search but query DIFFERENT indices: `spaarke-knowledge-index-v2` (RagService), no fixed index (SemanticSearchService — routed by deployment service), `spaarke-records-index` (RecordSearchService), `playbook-embeddings` (PlaybookEmbeddingService). RagService is the canonical pattern (Null-Object, AI-Search-keys gate); the other three have no Null peer.

---

### §2.4 Cache patterns (Category 4) — 32 files use `IMemoryCache`/`IDistributedCache`

Cache infrastructure is highly fragmented. Two dedicated cache services, one cross-cutting kill-switch cache, plus 29 inline `IMemoryCache.TryGetValue` / `IDistributedCache.GetAsync` consumers.

#### §2.4.1 Dedicated cache services

**`EmbeddingCache` / `IEmbeddingCache`** (`Services/Ai/EmbeddingCache.cs`)
- Purpose: Redis (`IDistributedCache`) cache for query embeddings to reduce OpenAI cost/latency. 7-day TTL. SHA-256 hash keys (`sdap:embedding:{base64-sha256-hash}`). Float[] → byte[] via `Buffer.BlockCopy` → Base64.
- Consumers: `RagService.cs`, `SemanticSearchService.cs`, `RecordSearchService.cs`, `ReferenceRetrievalService.cs`.
- State: **ACTIVE** (canonical cache pattern — should be model for others).
- DI: Singleton in `AnalysisServicesModule.AddRagServices:549` (inside AI Search keys sub-gate).
- Config: Hardcoded `DefaultTtl=7d`, `CacheKeyPrefix="sdap:embedding:"`.

**`InsightsPlaybookExecutionCache` / `IInsightsPlaybookExecutionCache`** (`Services/Ai/Insights/InsightsPlaybookExecutionCache.cs`)
- Purpose: D-P13 SPEC §3.1 wrapper around `IPlaybookExecutionEngine` calls; Redis-backed per ADR-009; OpenTelemetry meter via `InsightsCacheMetrics`.
- Consumer: `InsightsOrchestrator.cs`.
- State: **ACTIVE**.
- DI: Singleton in `AnalysisServicesModule.AddInsightsCache:475`.

#### §2.4.2 Inline `IMemoryCache` usage (in-process, no shared abstraction)

The brief listed 11+ direct `IMemoryCache` consumers; actual count is **32 files** (`Grep IMemoryCache|IDistributedCache` in `Services/Ai`). Inline usage by service:

| Service | Cache type | TTL | Prefix/key pattern |
|---|---|---|---|
| `PlaybookLookupService` | `IMemoryCache` | 1 hr | `playbook:code:{code}` |
| `ActionLookupService` | `IMemoryCache` | 1 hr | `action:code:{code}` |
| `SkillLookupService` | `IMemoryCache` | 1 hr | `skill:code:{code}` |
| `ToolLookupService` | `IMemoryCache` | 1 hr | `tool:code:{code}` |
| `InsightsIntentClassifier` | `IMemoryCache` | 15 min sliding | SHA-256 of normalized query |
| `Chat/PlaybookDispatcher` | `IDistributedCache` (Redis) | 5 min | playbook catalog version key |
| `Chat/ChatContextMappingService` | `IDistributedCache` | varies | per chat session |
| `Chat/SprkChatAgentFactory` | `IMemoryCache` | session lifetime | per session id |
| `Chat/PendingPlanManager` | `IDistributedCache` | 30 min | `plan:pending:{tenantId}:{sessionId}` |
| `Chat/ChatSessionManager` | `IDistributedCache` | session | session storage |
| `Chat/DynamicCommandResolver` | `IMemoryCache` | varies | command resolution |
| `Capabilities/CapabilityManifest` | `IMemoryCache` (in-process snapshot) | manual refresh | manifest hash |
| `Chat/OrchestratorPromptBuilder` | in-process `MemoryCache` | manifest hash | stable prefix |
| `Sessions/SessionPersistenceService` | `IDistributedCache` | session lifetime | per-tenant session |
| `Security/PrivilegeGroupResolver` | `IMemoryCache` | per-user | resolved privileges |
| `RecordSearch/RecordSearchService` | `IDistributedCache` | configurable | per query |
| `Foundry/AgentServiceClient` | `IDistributedCache` | configurable | thread id persistence |
| `AnalysisRagProcessor`, `AnalysisDocumentLoader`, `AnalysisCacheEntry` | `IMemoryCache` | analysis-scope | analysis context |
| `AiPlaybookBuilderService`, `ReferenceRetrievalService`, `PlaybookService` | `IMemoryCache` | varies | service-specific |
| `Insights/Routing/Null*`, `InsightsActionRouter` | `IMemoryCache` | (varies) | routing decisions |
| `TextExtractorService`, others | `IMemoryCache` | varies | extraction artifacts |

**Cross-cutting observation**: No `ISprkCache<T>` abstraction. Every service hand-rolls cache-key construction, TTL choice, and serialization. The `EmbeddingCache` pattern (typed wrapper around `IDistributedCache` + telemetry hook + structured key prefix) is the canonical model that the other 30 services do NOT follow.

---

### §2.5 Prompt builders (Category 5)

#### §2.5.1 `CapabilityClassificationPromptBuilder`
- Path: `Services/Ai/Capabilities/CapabilityClassificationPromptBuilder.cs`
- Purpose: Builds compact GPT-4o-mini prompt for Layer 2 routing (AIPU2-013). ≤600 token target. Static class.
- Consumer: `CapabilityRouter` (Layer 2 helper).
- State: **ACTIVE**.
- DI: **NOT REGISTERED** (static class).

#### §2.5.2 `OrchestratorPromptBuilder` / `IOrchestratorPromptBuilder`
- Path: `Services/Ai/Chat/OrchestratorPromptBuilder.cs`
- Purpose: Two-layer system prompt for orchestrator LLM. Layer 1 (stable prefix, ~2000 tokens, cached by manifest hash). Layer 2 (per-turn suffix, 0-3000 tokens, never cached). 9000-token total budget.
- Consumers: `DirectOpenAiAgent`, `SprkChatAgent`, others via `IOrchestratorPromptBuilder` interface.
- State: **ACTIVE**.
- DI: Singleton in `AiCapabilitiesModule.cs:102-104` (concrete + interface).

#### §2.5.3 `PlaybookBuilderSystemPrompt`
- Path: `Services/Ai/Prompts/PlaybookBuilderSystemPrompt.cs`
- Purpose: System prompts for AI Playbook Builder. 11 intent categories, confidence thresholds (Intent=0.75, Entity=0.80, ScopeMatch=0.70), canvas-state awareness. Static class.
- Consumer: `IntentClassificationService` (§2.1.3 — itself unused). Indirect: `AiPlaybookBuilderService`.
- State: **AT RISK** — consumed primarily by an orphaned service (§2.1.3).
- DI: **NOT REGISTERED** (static class).

#### §2.5.4 Inline prompt construction (anti-pattern)

Many services build prompts inline (string concatenation / interpolation), bypassing the three explicit builders above:
- `InsightsIntentClassifier.BuildPrompt()` — embedded constant + few-shot demonstration + JSON schema.
- `AnalysisOrchestrationService`, `AnalysisActionService` — inline prompt assembly from `AnalysisOptions` + `IConfiguration`.
- `Insights/AssistantToolCallHandler` — inline tool-call prompt construction.
- `BuilderAgentService`, `EntityResolutionService`, `ClarificationService` — inline templates with hardcoded fewshots.

**`PromptLibrary`** (`Services/Ai/PromptLibrary/`) exists with `IPromptLibraryService` + `PromptTemplate.cs` + Cosmos backing, but its consumer footprint within `Services/Ai/` is limited — most LLM-calling services do NOT route through it.

---

### §2.6 Public Contracts facade (Category 6 — discovered as 6th formal layer)

A formal "facade boundary" introduced by task 046 (FR-E1) per refined ADR-013 (2026-05-20). External (non-`Services/Ai/`) code MUST consume AI via these facades.

| Path | State | DI |
|---|---|---|
| `Services/Ai/PublicContracts/IBriefingAi.cs` + `BriefingAi.cs` + `NullBriefingAi.cs` | ACTIVE + Null peer | Scoped in `AddPublicContractsFacade` |
| `Services/Ai/PublicContracts/IInvoiceAi.cs` + `InvoiceAi.cs` | ACTIVE | Scoped |
| `Services/Ai/PublicContracts/IWorkspacePrefillAi.cs` + `WorkspacePrefillAi.cs` | ACTIVE | Scoped |
| `Services/Ai/PublicContracts/IRecordMatchingAi.cs` + `RecordMatchingAi.cs` | ACTIVE | Scoped |
| `Services/Ai/PublicContracts/IInsightsAi.cs` (impl: `Insights/InsightsOrchestrator.cs`) | ACTIVE | Scoped in `InsightsFacadeModule:105` |
| `Services/Ai/PublicContracts/IObservationMirror.cs` | facade interface only; impl swapped between `NoOpObservationMirror` and `DataverseObservationMirror` (Zone B) | Singleton |

**State summary**: All 5 facades active. 1 has Null peer (`NullBriefingAi`); the other 4 do NOT — which means consumers can't fall back gracefully if facade dependencies are gated off (a structural gap relative to the §2.4 Null-Object pattern).

---

### §2.7 Node executors (Category 7 — discovered, large surface)

**ADR-010 `INodeExecutor` registry**: 16 registered concrete executors discoverable via `NodeExecutorRegistry`.

| Executor | ActionType | DI | Module |
|---|---|---|---|
| `CreateTaskNodeExecutor` | (default) | Singleton | `AnalysisServicesModule.AddNodeExecutors` |
| `SendEmailNodeExecutor` | (default) | Singleton | same |
| `UpdateRecordNodeExecutor` | (default) | Singleton | same |
| `DeliverOutputNodeExecutor` | (default) | Singleton | same |
| `DeliverToIndexNodeExecutor` | (default) | Singleton | same |
| `ConditionNodeExecutor` | (default) | Singleton | same |
| `AiAnalysisNodeExecutor` | (default) | Singleton | same |
| `CreateNotificationNodeExecutor` | (default) | Singleton | same |
| `QueryDataverseNodeExecutor` | (default) | Singleton | same |
| `AgentServiceNodeExecutor` | 60 | Singleton (kill-switched) | same |
| `GroundingVerifyNode` | 70 | Singleton | same |
| `LiveFactNode` | 80 | Singleton | same |
| `IndexRetrieveNode` | 90 | Singleton | same |
| `EvidenceSufficiencyNode` | 100 | Singleton | same |
| `DeclineToFindNode` | 110 | Singleton | same |
| `ReturnInsightArtifactNode` | 120 | Singleton | same |
| `SanitizerNodeExecutor` | 130 | Singleton | `InsightsIngestModule:111` |
| `ObservationEmitterNodeExecutor` | 140 | Singleton | `InsightsIngestModule:112` |

All Singletons, all auto-discovered by `NodeExecutorRegistry` (`INodeExecutorRegistry`). The dispatch is uniform but the executors themselves call HEAVILY into other categories (LLM, search, Dataverse).

---

### §2.8 Foundry agent (Category 8 — discovered, isolated subsystem)

`Services/Ai/Foundry/` is a self-contained Azure AI Foundry integration with its own options + kill switch.

- `AgentServiceClient.cs` — wraps `Azure.AI.Projects.AgentsClient`. Singleton with `SemaphoreSlim` for `MaxConcurrency` gate. ADR-018 kill switch via `AgentServiceOptions.Enabled`. Thread-ID persistence via Redis.
- `CodeInterpreterBridge.cs` — thin wrapper around `AgentServiceClient` for Code Interpreter sandbox. Singleton.
- Options: `AgentServiceOptions`, `BingGroundingOptions`, `CodeInterpreterOptions` — all bound in `ConfigurationModule`, deferred validation per kill-switch pattern.

State: **ACTIVE** (gated by Foundry kill switches; can be flipped off without affecting non-Foundry paths).

---

### §2.9 Insights extraction pipeline (Category 9 — discovered, Phase 1.5+ pipeline)

`Services/Ai/Insights/Extraction/` is a post-extraction processing pipeline added by the Insights Engine project. Distinct from the Insights Engine FACADE (`PublicContracts/IInsightsAi`) and the Insights ORCHESTRATOR.

Key types:
- `IObservationEmitter` / `ObservationEmitter` — per-field threshold gating + Observation emission. Singleton, `IOptionsMonitor<ConfidenceThresholdOptions>`.
- `ILayer1ClassificationEmitter` / `Layer1ClassificationEmitter` — emits single classification Observation per document.
- `Layer1ClassificationResult`, `OutcomeExtractionProjection`, `OutcomeExtractionResponse`, `OutcomeExtractionResponseValidator` — data shapes.

State: **ACTIVE** (recently added; gated by compound AI gate).

DI: `InsightsExtractionModule.cs` (2 unconditional singletons, separate module per Zone A/B boundary §3.5).

---

### §2.10 Tool framework (Category 10 — discovered)

Multiple parallel "tool" surfaces:
- `Services/Ai/Chat/Tools/` — 10 AIFunction tools for chat (`AnalysisExecutionTools`, `AnalysisQueryTools`, `CodeInterpreterTools`, `CompareDocumentsTool`, `DataverseQueryTools`, `DocumentSearchTools`, `KnowledgeRetrievalTools`, `LegalResearchTools`, `TextRefinementTools`, `VerifyCitationsTool`, `WebSearchTools`, `WorkingDocumentTools`).
- `Services/Ai/Tools/` — 5 separate `IAiToolHandler` impls (`DataverseUpdateToolHandler`, `DocumentClassifierHandler`, `SemanticSearchToolHandler`, `SendCommunicationToolHandler`, `SummaryHandler`).
- `Services/Ai/Chat/Tools/AnalysisQueryTools.cs` and `Services/Ai/Tools/SummaryHandler.cs` are different abstractions: one is `AIFunction` discoverable by `IChatClient.UseFunctionInvocation()`, the other is `IAiToolHandler` with explicit registry. They overlap semantically.

Registration: `AddToolFramework` helper in `AnalysisServicesModule.cs:575-590`. Gated by `ToolFrameworkOptions.Enabled`.

---

### §2.11 OpenAI client (Category 11 — discovered, single canonical wrapper)

- `Services/Ai/OpenAiClient.cs` / `IOpenAiClient.cs` — Spaarke's own facade over `Microsoft.Extensions.AI.IChatClient` + Azure OpenAI SDK. All 4 intent classifiers + most LLM-calling services depend on this.
- DI: Singleton in `AnalysisServicesModule.cs:26-27` (concrete + interface).
- State: **ACTIVE** (the single canonical LLM facade — good).

---

## §3 DI registration patterns (Q-001 expansion)

### §3.1 Module inventory (34 DI modules)

| Module | Concern |
|---|---|
| `AiModule.cs` | AI Platform Foundation (15/15 unconditional cap per ADR-010) |
| `AiCapabilitiesModule.cs` | Multi-provider capability services (R2 AIPU2-010+) |
| `AiChatModule.cs` | R2 chat agent boundary (`ISprkAgent`, telemetry) |
| `AiPersistenceModule.cs` | AI persistence (sessions) |
| `AiSafetyModule.cs` | Safety services (citations, groundedness, prompt shield) |
| `AnalysisServicesModule.cs` | **CENTRAL HUB** — orchestrates 8 helper methods + compound AI gate |
| `InsightsExtractionModule.cs` | Insights Engine Zone A extraction primitives |
| `InsightsFacadeModule.cs` | Insights Engine Zone A public facade (`IInsightsAi`) |
| `InsightsIngestModule.cs` | Insights Engine Zone A ingest pipeline + node executors |
| `InsightsModule.cs` | Insights Engine Zone B (PrecedentBoard, LiveFactResolvers) |
| `FinanceModule.cs` | Finance Intelligence + the 4 lookup services |
| `WorkspaceModule.cs` | Workspace prefill |
| `AgentModule.cs` | Agent infrastructure |
| `CacheModule.cs` | Redis / `IDistributedCache` |
| `ConfigurationModule.cs` | Options pattern bindings (all options classes) |
| `GraphModule.cs` | Graph + `IGenericEntityService` |
| `JobProcessingModule.cs`, `WorkersModule.cs`, `RegistrationModule.cs`, etc. | Non-AI |

### §3.2 Registration pattern taxonomy

Six distinct registration patterns observed across `Services/Ai/`:

1. **Unconditional Singleton/Scoped** — most node executors, public-contracts facades, capability subsystem.
2. **Compound-gated** — guarded by `Analysis:Enabled && DocumentIntelligence:Enabled` in `AnalysisServicesModule`. Most "real AI" services.
3. **Fine-grained gated** — extra `if (option.Enabled)` inside the compound gate (e.g., `InsightsIntentClassifierOptions.Enabled`).
4. **AI-Search-keys sub-gate** — extra `if (!string.IsNullOrEmpty(docIntelOptions?.AiSearchEndpoint && ...AiSearchKey))` inside compound (RAG services, embedding cache, file indexing).
5. **Null-Object dual registration** — real impl behind gate, Null peer in `AddNullObjectsForCompoundOff` for P3 Fail-Fast (ADR-032). 10 Null-Object impls (see §2.7 inventory).
6. **Factory-instantiated (NOT registered)** — by intentional ADR-010 budget decision: `PlaybookDispatcher`, `PlaybookEmbeddingService`, `CompoundIntentDetector` (inferred), tool factories.

### §3.3 ADR-010 budget commentary

`AiModule.cs` explicitly tracks "15/15 unconditional cap" — visible per-line auditing in its inline audit table (lines 269-313). Several services have been pushed to `AnalysisServicesModule` or `AnalysisServicesModule.AddUnconditionalChatAndNotificationServices` (D-09 §2 B4/B5/L5 promotion, 2026-06-01) to keep the AiModule under cap. The cap-management mechanism is robust but adds cognitive load: a registration's location in the codebase no longer maps directly to its conceptual home.

### §3.4 Asymmetric registration audit

The `bff-extensions.md` §F.1 anti-pattern is heavily commented in the modules (especially `InsightsModule.cs`, `InsightsIngestModule.cs`, `AnalysisServicesModule.cs`). Every conditional registration has an "ADR-032 §F.1 inspection" comment block. This is excellent rigor — and a symptom of how many feature gates exist.

---

## §4 Configuration patterns (Q-001 expansion)

### §4.1 Inventory: 27 options classes

`Configuration/` (25 files; `Sprk.Bff.Api.Configuration`):
- `AgentTokenOptions`, `AiSearchResilienceOptions`, `AnalysisOptions`, `AssistantCitationHrefOptions`, `CommunicationOptions`, `DataverseOptions`, `DemoProvisioningOptions`, `DocumentIntelligenceOptions` (+`DocumentIntelligenceOptionsValidator`), `EmailProcessingOptions`, `FinanceOptions`, `GraphOptions` (+`GraphOptionsValidator`), `GraphResilienceOptions`, `InsightsIntentClassifierOptions`, `OfficeRateLimitOptions`, `RedisOptions`, `ReindexingOptions`, `ServiceBusOptions`, `SharePointEmbeddedOptions`, `SpeAdminOptions`, `ToolFrameworkOptions`, `WorkspaceOptions`.
- Non-options: `FeatureDisabledException`, `FeatureDisabledResults`.

`Options/` (2 files; `Sprk.Bff.Api.Options`):
- `AiSearchOptions`, `LlamaParseOptions`.

Plus options inline in `Services/Ai/` itself:
- `Capabilities/CapabilityRouterOptions`, `Capabilities/ManifestRefreshOptions`.
- `Insights/Extraction/ConfidenceThresholdOptions`.
- `Foundry/AgentServiceOptions`, `Foundry/BingGroundingOptions`, `Foundry/CodeInterpreterOptions`.

**Total**: 27 + 2 (Options/) + 6 (in-Services) = **35 options classes** across the BFF.

### §4.2 Binding patterns observed

Four distinct binding styles:

1. **`AddOptions<T>().BindConfiguration("Section:Name")`** — newest pattern; allows fluent chaining of `ValidateDataAnnotations()` + `ValidateOnStart()`. Used in `InsightsExtractionModule`, `AiCapabilitiesModule`, `InsightsModule`. Modern canonical.
2. **`AddOptions<T>().Bind(configuration.GetSection(...))`** — variant with explicit `IConfiguration`. Used in `AiCapabilitiesModule:85`.
3. **`services.Configure<T>(configuration.GetSection(...))`** — older pattern, no chaining. Used in `AnalysisServicesModule.cs:275`, `:585`.
4. **Direct `configuration.GetValue<bool>(...)` at startup** — bypasses `IOptions` entirely (decision is fixed at startup). Used in `AnalysisServicesModule.cs:22, 43, 519` (compound gate). Justified per inline comment ("registration choice is made at startup — IOptions reload binding wouldn't switch the registered type").

### §4.3 Configuration namespace split

- `Sprk.Bff.Api.Configuration` (25 classes) vs `Sprk.Bff.Api.Options` (2 classes) — **redundant namespace split** with no clear delineating principle. Both contain options classes; the split appears to be temporal (`AiSearchOptions` and `LlamaParseOptions` were added in different waves and landed in `Options/`).

### §4.4 Section name patterns

Section names are inconsistent:
- `"Insights:IntentClassifier"`, `"Insights:Extraction:ConfidenceThresholds"` — nested colon notation.
- `"Capabilities"`, `"Capabilities:Router"`, `"Analysis"` — flat or shallow.
- `"AzureOpenAI"`, `"DocumentIntelligence"`, `"AiSearch"` — top-level.
- `"AzureOpenAI:Endpoint"` read directly via `configuration["AzureOpenAI:Endpoint"]` instead of going through an options class (`AiModule.cs:92-93`) — bypasses the `IOptions<T>` pattern entirely.

---

## §5 Cross-reference matrix (which categories share infrastructure)

| Category | `IOpenAiClient` | `IMemoryCache` | `IDistributedCache` | Azure AI Search | `IChatClient` |
|---|---|---|---|---|---|
| Intent classification (§2.1) | All 4 | 3 of 4 (CR, IIC, IntCS) | 1 of 4 (PD) | 1 of 4 (PD via PlaybookEmbedding) | 2 of 4 (CR keyed "raw", PD) |
| Lookup services (§2.2) | None | All 4 | None | None | None |
| Search services (§2.3) | 3 of 4 (all but PlaybookEmbedding indirectly) | 0 | 1 of 4 (RecordSearch) | All 4 | None |
| Cache infrastructure (§2.4) | None | 2 (lookup, classifier patterns) | 2 (Embedding, InsightsPlaybook) | None | None |
| Prompt builders (§2.5) | Indirect (consumed by classifiers) | 1 (Orchestrator stable prefix) | None | None | Indirect |
| Public Contracts (§2.6) | All 5 indirectly | None | None | None | None |
| Node executors (§2.7) | Most (AiAnalysis, ObservationEmitter, etc.) | Some | Some | RagService-routed | Indirect |
| Foundry (§2.8) | None (uses `AgentsClient` directly) | None | Thread persistence | None | None |
| Insights extraction (§2.9) | None | None | None | None | None |

**Key cross-cutting observation**: Intent classification (§2.1) is the most cross-cutting — every classifier depends on `IOpenAiClient`, three use in-process caching, one uses Redis. A unified intent-classification abstraction would clean up `IOpenAiClient` integration plus eliminate at least 2 of 4 cache implementations.

---

## §6 State summary

### §6.1 Active vs unused

Counting categories §2.1-§2.11:

| State | Count | Examples |
|---|---|---|
| **ACTIVE** | ~50 services | RagService, SemanticSearchService, RecordSearchService, PlaybookEmbeddingService, CapabilityRouter, PlaybookDispatcher, InsightsIntentClassifier, all 16 node executors, OpenAiClient, all 5 public contracts facades, EmbeddingCache, etc. |
| **ACTIVE-GATED** | ~30 services | All compound-AI-gated services (Analysis, RAG, intent classifier); Foundry agent + bridge; all Insights Zone A services |
| **UNUSED / ORPHANED** | **4 confirmed** | `ActionLookupService` (only DI registration), `SkillLookupService` (only DI registration), `ToolLookupService` (only DI registration), `IntentClassificationService` (only DI registration) |
| **AT RISK** | 1 | `PlaybookBuilderSystemPrompt` — primary consumer is orphaned `IntentClassificationService` |
| **NOT-REGISTERED (intentional)** | ~5 | `PlaybookDispatcher`, `PlaybookEmbeddingService`, `CompoundIntentDetector`, `CapabilityClassificationPromptBuilder`, `PlaybookBuilderSystemPrompt` (last two are static classes) |
| **NULL-OBJECT (kill-switch)** | 10 | NullRagService, NullPlaybookService, NullPlaybookOrchestrationService, NullTextExtractor, NullVisualizationService, NullFileIndexingService, NullSprkChatAgentFactory, NullPendingPlanManager, NullInsightsIntentClassifier, NullBriefingAi |

### §6.2 The four orphans

The 4 confirmed UNUSED services are the strongest "dead code" finding:
1. `ActionLookupService` (Category 2)
2. `SkillLookupService` (Category 2)
3. `ToolLookupService` (Category 2)
4. `IntentClassificationService` (Category 1)

All four are registered in DI but have ZERO production consumers identified by `Grep`. Combined LOC: ~600 lines + ~150 lines (interfaces). They are part of `Sprk.Bff.Api.dll` publish artifact and contribute to the publish-size budget tracked per `bff-extensions.md` NFR-01.

### §6.3 Lifetime distribution (estimate from samples)

- Singleton: ~60% of registered AI services (Spaarke's heavy ADR-010 bias toward Singleton)
- Scoped: ~35% (Chat services, public contracts facades, capability validator)
- Transient: ~5% (typed HttpClient registrations)
- Hosted services: 4 (`CapabilityManifestInitializer`, `ManifestRefreshService`, `PlaybookIndexingBackgroundService`, `PlaybookSchedulerService`)

---

## §7 Open questions surfaced for Phase 2 (per-category analysis)

These questions are HANDOFFS to Phase 2 sub-agents who will deep-dive each category:

### §7.1 Category 1 (Intent classification)
- Can the 4 classifiers consolidate into ONE generic `IIntentClassifier<TResult>` with strategy plug-ins (keyword, vector, LLM)?
- What's the migration path for `IntentClassificationService` consumers? Is `AiPlaybookBuilderService` still actively used or part of the orphan cluster?
- Should `PlaybookDispatcher` (factory-instantiated) join DI to enable the same testability seam the other three have?
- The two `IntentClassificationResult` types (different shapes, same name in `Models/Ai/`) cause confusion. Rename one?

### §7.2 Category 2 (Lookup services)
- Three of four are unused. Did historical projects WRITE the code without WIRING it? Or did consumers exist and get refactored away?
- Generic candidate: `ILookupService<TEntity, TResponse>(string code)` with the entity-specific bits as type parameters or generic factories.
- The XML docstrings are line-for-line copy/paste — what % is templated vs hand-edited?

### §7.3 Category 3 (Search services)
- `RagService` has Null-Object peer; the other 3 don't. Why?
- `PlaybookEmbeddingService` index lookup is a special case of "search by content vector" — should it merge with `SemanticSearchService`?
- `RecordSearchService` not tenant-isolated (security at Dataverse layer per XML doc). Is this a security gap or an intentional layered defense?

### §7.4 Category 4 (Cache patterns)
- 32 cache consumers, 2 dedicated cache services (`EmbeddingCache`, `InsightsPlaybookExecutionCache`), no unified abstraction. The `EmbeddingCache` pattern is the most disciplined — should it become `SpaarkeCache<T>` or `IDataverseLookupCache`?
- Cache key prefixes are entity-coupled (`playbook:code:`, `action:code:`, `skill:code:`, `tool:code:`) — would benefit from a typed factory.

### §7.5 Category 5 (Prompt builders)
- Three explicit builders, many inline. The `PromptLibrary` Cosmos-backed service exists but isn't broadly adopted.
- Should builders consolidate behind a single `IPromptComposer` interface?

### §7.6 Category 6 (Public Contracts facade)
- Only `BriefingAi` has Null peer. The other 4 facades don't degrade gracefully under compound-AI-OFF. Is this intentional or an asymmetric-registration anti-pattern (`bff-extensions.md` §F.1)?

### §7.7 Categories 7-11 (discovered)
- **Node executors**: Is the `ActionType` numeric assignment (60/70/80/.../140) ad-hoc or registry-coordinated? Risk of collision across teams.
- **Foundry**: Wrapping `AzureAIProjects.AgentsClient` in `AgentServiceClient`. Is this a future-proof abstraction or a current-need wrapper that will be retired with R3?
- **Insights extraction**: Multiple "emitter" interfaces (`IObservationEmitter`, `ILayer1ClassificationEmitter`). Will the pattern continue ("Layer 2..N emitters")? If so, should a generic emitter base exist?
- **Tool framework**: Two parallel surfaces (`Chat/Tools/` `AIFunction` discoverable + `Tools/` `IAiToolHandler` registry). Are they intended to coexist or is one slated for retirement?

### §7.8 DI and Configuration (Q-001 expansion)
- **Module count creeping**: 34 DI modules, 6 directly AI-flavored (`AiModule`, `AiCapabilitiesModule`, `AiChatModule`, `AiPersistenceModule`, `AiSafetyModule`, `AnalysisServicesModule`). Should they consolidate, or is the per-concern split good practice?
- **`Configuration/` vs `Options/`**: redundant namespace split with no clear delineator. Recommend Phase 2 consolidate.
- **35 options classes**: Each new AI feature adds an options class. Is there a pattern for "feature manifest" that collapses 3-4 related options classes into one?
- **Compound AI gate complexity**: The `if (analysisEnabled && documentIntelligenceEnabled) { ... } else if (...) { ... } else { ... }` triad in `AnalysisServicesModule` is the central decision point. Phase 2 should evaluate whether ADR-032 Null-Object Kill-Switch + per-service fine-grained gates could replace the compound gate.

---

## §8 Phase 2 handoff guidance

Each of Categories 1-11 should get a dedicated Phase 2 brief. Recommended order (highest expected leverage first):

1. **Category 4 (Cache patterns)** — 32 consumers, no shared abstraction. Highest leverage; cheapest to canonicalize via the `EmbeddingCache` pattern.
2. **Category 2 (Lookup services)** — 4 near-identical, 3 unused. Lowest-risk consolidation; immediate publish-size win.
3. **Category 1 (Intent classification)** — most architecturally significant; requires owner/team input.
4. **Category 3 (Search services)** — 4 substrates, each justified by different index; consolidation harder.
5. **Category 5 (Prompt builders)** — depends on Category 1 outcome.
6. **Category 6 (Public Contracts)** — small surface; close out Null-Object gap quickly.
7. **DI + Configuration (Q-001 expansion)** — touches every other category; should be LAST.
8. **Categories 7-11** — each needs its own Phase 2 brief but can run in parallel.
