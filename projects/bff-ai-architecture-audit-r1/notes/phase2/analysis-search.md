# Phase 2 Analysis — Category 3: Search Services

> **Authored by**: Phase 2 W2 Sub-Agent F
> **Pinned to**: commit `357e6936` (Phase 1 inventory snapshot)
> **HEAD at analysis time**: `d862bec6` (same HEAD W2 Sub-Agent E observed; ZERO code drift in `src/server/api/Sprk.Bff.Api/Services/Ai/` between `357e6936` → `d862bec6` confirmed via `git diff --stat`)
> **Scope boundary**: 4-substrate search-service decisions; out-of-scope per brief §4 = index schemas (incl. `playbook-embeddings` rename owned by r3 Wave 1), relevance/RRF tuning, embedding model choice, vector dim, HNSW vs IVF, multi-tenancy strategy for `spaarke-records-index` (surface only), search-result caching (defer to Cat 4).

---

## §1 Phase 1 baseline (verbatim from inventory §2.3 + §7.3)

### §1.1 Inventory §2.3 header — 4 distinct substrates against Azure AI Search
> "4 distinct substrates hitting Azure AI Search but querying DIFFERENT indices."

### §1.2 Inventory §2.3.1 — `RagService` / `IRagService` / `NullRagService`
Verbatim: "Path: `Services/Ai/RagService.cs` (+ `IRagService.cs`, `NullRagService.cs`). Hybrid RAG search (keyword + vector + semantic ranking) against the knowledge index. P95 <500ms target. Integrates with `IKnowledgeDeploymentService` for multi-tenant routing. Consumers: 38 files reference `IRagService`. State: **ACTIVE** (most-consumed). Has P3 Fail-Fast Null-Object peer for AI-Search-keys-missing fallback. DI: Singleton in `AnalysisServicesModule.AddRagServices` (line 550) when `DocumentIntelligence:AiSearchEndpoint/Key` set; otherwise `NullRagService` (line 561). Compound-OFF branch also registers Null (line 223). Config: `IOptions<DocumentIntelligenceOptions>` (legacy keys) + `IOptions<AiSearchOptions>` (newer). Origin: AIPL."

### §1.3 Inventory §2.3.2 — `SemanticSearchService` / `ISemanticSearchService`
Verbatim: "Path: `Services/Ai/SemanticSearch/SemanticSearchService.cs`. Hybrid semantic search via Azure AI Search with RRF, vector-only, keyword-only modes. Pipeline of `IQueryPreprocessor`/`IResultPostprocessor` (no-ops in R1). Consumers: `SemanticSearchEndpoints.cs`, `SemanticSearchToolHandler.cs`, `DocumentClassifierHandler.cs`. State: **ACTIVE**. NO Null peer. DI: Via `services.AddSemanticSearch()` extension in `AnalysisServicesModule.cs:55`. Inside the compound AI gate. Origin: AIPU R1."

### §1.4 Inventory §2.3.3 — `RecordSearchService` / `IRecordSearchService`
Verbatim: "Path: `Services/Ai/RecordSearch/RecordSearchService.cs`. Hybrid semantic search against `spaarke-records-index`. NOT tenant-isolated (XML doc warns: 'security is enforced at Dataverse layer'). Consumers: `RecordSearchEndpoints.cs`, `RecordMatchingAi.cs`. State: **ACTIVE**. NO Null peer. DI: Via `services.AddRecordSearch()` extension in `AnalysisServicesModule.cs:58`. Inside compound AI gate."

### §1.5 Inventory §2.3.4 — `PlaybookEmbeddingService`
Verbatim: "Path: `Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs`. Embeddings for playbook content + manages `playbook-embeddings` AI Search index. Vector: `text-embedding-3-large` (3072 dim, HNSW cosine). Consumers: `PlaybookIndexingBackgroundService.cs`, `PlaybookIndexingService.cs`, `PlaybookDispatcher.cs`. State: **ACTIVE** (factory-instantiated; NOT DI-registered per ADR-010 budget constraint). Origin: SprkChat r2."

### §1.6 Inventory §7.3 open questions (verbatim)
> 1. "`RagService` has Null-Object peer; the other 3 don't. Why?"
> 2. "`PlaybookEmbeddingService` index lookup — should it merge with `SemanticSearchService`?"
> 3. "`RecordSearchService` not tenant-isolated. Security gap or intentional layered defense?"

---

## §2 Empirical reproduction (consumer counts re-run at HEAD `d862bec6`)

### §2.1 Drift check
`git diff --stat 357e6936..d862bec6 -- src/server/api/Sprk.Bff.Api/Services/Ai/` returns EMPTY. **ZERO code drift on Cat 3 surface.**

### §2.2 LOC inventory
| File | LOC |
|---|---|
| `RagService.cs` | 1253 |
| `NullRagService.cs` | 135 |
| `SemanticSearch/SemanticSearchService.cs` | ~550 |
| `RecordSearch/RecordSearchService.cs` | ~520 |
| `PlaybookEmbedding/PlaybookEmbeddingService.cs` | 336 |

### §2.3 Consumer reproduction (grep at HEAD)

**§2.3.1 `IRagService`** — 23 files in `Sprk.Bff.Api`; **12 behavioral consumers**: `RagEndpoints.cs` (6 handlers), `KnowledgeBaseEndpoints.cs`, `AnalysisRagProcessor.cs`, `RagIndexingPipeline.cs`, `AiAnalysisNodeExecutor.cs`, `FileIndexingService.cs`, `Chat/Tools/DocumentSearchTools.cs`, `Chat/Tools/KnowledgeRetrievalTools.cs`, `Chat/SprkChatAgentFactory.cs`, `Tools/DocumentClassifierHandler.cs`, `Insights/InsightsOrchestrator.cs`, `Insights/AssistantToolCallHandler.cs`. State: **ACTIVE — most-consumed.**

**§2.3.2 `ISemanticSearchService`** — 5 files in `Sprk.Bff.Api`; **2 behavioral consumers**: `Api/Ai/SemanticSearchEndpoints.cs`, `Services/Ai/Tools/SemanticSearchToolHandler.cs`. **Inventory mislabel**: `DocumentClassifierHandler.cs` imports `IRagService` NOT `ISemanticSearchService` at HEAD. State: **ACTIVE**.

**§2.3.3 `IRecordSearchService`** — 8 files in `Sprk.Bff.Api`; **3 behavioral consumers**: `Api/Ai/RecordSearchEndpoints.cs`, `Services/Ai/PublicContracts/RecordMatchingAi.cs`, `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` (line 37, 44 — **NEW consumer not in inventory**; Cat 7 Sub-Agent D caught it). State: **ACTIVE**.

**§2.3.4 `PlaybookEmbeddingService`** — 5 files in `Sprk.Bff.Api`; **3 behavioral consumers**: `PlaybookIndexingService.cs`, `PlaybookIndexingBackgroundService.cs`, `Chat/PlaybookDispatcher.cs`, `Chat/SprkChatAgentFactory.cs`. Note: `Api/Ai/PlaybookEmbeddingEndpoints.cs` uses static `PlaybookIndexingBackgroundService.Instance`, NOT direct injection. State: **ACTIVE** (factory-instantiated).

### §2.4 Null-peer asymmetry empirical verification (THE HEADLINE INVESTIGATION)

Inventory §7.3 bullet 1 asks: "Why does RagService have a Null peer and the other 3 don't?"

**Empirical reading of DI + endpoint mapping reveals a clean architectural rationale**:

| Service | Endpoint mapping conditionality | Consumer DI registration conditionality | Symmetric? | Null peer needed? |
|---|---|---|---|---|
| `IRagService` | **UNCONDITIONAL** (`EndpointMappingExtensions.cs:133 app.MapRagEndpoints()` outside compound gate) | Real impl registered compound-AI-ON + AI-Search-keys-ON (line 550); Null registered in compound-OFF (line 223) AND in compound-ON + AI-Search-keys-OFF (line 561) | **NO** — endpoint always mapped, consumer always needs a service | **YES** (existing `NullRagService` correct) |
| `ISemanticSearchService` | **CONDITIONAL** (`EndpointMappingExtensions.cs:152-156` inside compound-AI-ON gate) | Real impl registered compound-AI-ON only (line 55) | **YES** — gates match | **NO** (DI symmetry holds) |
| `IRecordSearchService` | **CONDITIONAL** (line 156 inside compound-AI-ON gate) | Real impl registered compound-AI-ON only (line 58) | **YES** — gates match | **NO** (DI symmetry holds) |
| `PlaybookEmbeddingService` | **CONDITIONAL** transitively (endpoint at line 124 inside compound gate; uses static `BackgroundService.Instance`) | **NOT DI-registered** — factory-instantiated by hosted service (registered compound-AI-ON only at `AiModule.cs:262`) + `PlaybookDispatcher` (factory pattern with per-request tenantId) | **YES** — entire chain compound-AI-ON-gated | **NO** (no DI graph to break) |

**Verdict on inventory §7.3 bullet 1**: The Null-peer asymmetry is **NOT a §F.1 anti-pattern** — it is the **direct, intentional consequence of differential endpoint mapping conditionality**. `RagEndpoints` is mapped unconditionally because the RAG knowledge base is fundamental to Spaarke's value prop (consumers need a clean 503 when AI is OFF); the other 3 substrates are conditionally mapped together with their consumers because they are AI-specific verticals that don't exist when AI is OFF. **This is architecturally consistent with ADR-032 §F.1.**

**Cross-reference to Cat 6 §F.1 framework**: Sub-Agent C's analysis identified asymmetric registration as material §F.1 for `IInvoiceAi`, `IWorkspacePrefillAi`, `IInsightsAi`. **The 3 search services without Null peers are NOT instances of that anti-pattern** because their endpoints are gate-symmetric. Cat 3's asymmetry is intentional and correct; Cat 6's is unintentional and broken.

### §2.5 `RagService` LATENT BUG verification — NOT IN IRagService

The brief asked whether `NullRagService` fires correctly under compound-OFF for unconditionally-mapped consumers. **Empirical answer**: YES. Verified at:
1. Compound-OFF branch (`AnalysisServicesModule.cs:223`): `services.AddSingleton<IRagService, NullRagService>()` — Null peer registered.
2. Compound-ON + AI-Search-keys-missing (line 561): `services.AddSingleton<IRagService, NullRagService>()` — Null peer registered.
3. Compound-ON + AI-Search-keys-ON (line 550): real `RagService` registered.
4. `NullRagService.cs:32-95`: every method throws `FeatureDisabledException("ai.rag.disabled", ...)` (P3 Fail-Fast).

**Important distinction from Cat 6's `IInsightsAi` LATENT BUG**: `IInsightsAi.SearchAsync()` (impl `InsightsOrchestrator.SearchAsync()` line 892) calls `_ragService.SearchAsync(...)` at line 934. Under compound-OFF, `NullRagService` is registered and would throw `FeatureDisabledException` at this call. **BUT** Cat 6 found that `InsightsOrchestrator`'s ctor injects `IOpenAiClient` (line 112) which is NOT registered under compound-OFF. So the LATENT BUG fires at `InsightsOrchestrator` ctor resolution (500 InvalidOperationException) BEFORE reaching `_ragService.SearchAsync`. **The Cat 6 bug is upstream in InsightsOrchestrator's transitive-conditional ctor deps, NOT in the IRagService layer.** Cat 3 confirms `NullRagService` works correctly within its own layer; the Cat 6 IInsightsAi fix (add NullInsightsAi) is the correct remediation.

### §2.6 `PlaybookEmbeddingService` ↔ `SemanticSearchService` consolidation feasibility

This is the §7.3 bullet 2 question.

**Substrate similarity**: Both hit Azure AI Search; both use `text-embedding-3-large` 3072-dim embeddings; both use vector-search with cosine/HNSW.

**Substrate divergence**:
- **Different indices**: `playbook-embeddings` vs deployment-routed `spaarke-knowledge-index-v2`.
- **Different document shapes**: `PlaybookEmbeddingDocument` (playbookName, description, triggerPhrases, tags, recordType, entityType) vs `KnowledgeDocument` (content chunks + tenantId + privilegeGroupIds + parentEntityType).
- **Different security models**: PlaybookEmbedding has no tenant filter, no privilege filter, no semantic-ranking config; SemanticSearch routes per tenant + supports three hybrid modes.
- **Different API surfaces**: PlaybookEmbedding `SearchPlaybooksAsync` is a single Top-K vector lookup (≤5, no semantic re-ranking, optional `recordType eq` filter only). SemanticSearch handles RRF/vector/keyword, post-processing pipeline, score thresholding, count endpoint.
- **Different lifecycles**: PlaybookEmbedding includes write operations (`IndexPlaybookAsync`, `DeletePlaybookAsync`) driven by Dataverse plugins. SemanticSearch is read-only.
- **Different DI patterns**: PlaybookEmbedding factory-instantiated (ADR-010 budget); SemanticSearch DI-registered Scoped.

**Consolidation verdict — REJECT**: The shared substrate is a thin coincidence. Domains, security models, document shapes, API surfaces, and lifecycle responsibilities all differ. Forced consolidation would either bloat `SemanticSearchService` with playbook-specific concerns or create a generic search abstraction that papers over distinct domain semantics. **Mirrors W2 Sub-Agent E's REJECT verdict for forced classifier consolidation.** Recommendation: **KEEP SEPARATE.**

What COULD be canonicalized opportunistically (Cat 4 territory, not Cat 3): the "try cache → cache miss → generate via IOpenAiClient → cache result" pattern replicated in 3-of-4 search services. Extract to `IEmbeddingCache.GetOrGenerateAsync` extension method. Flag for Cat 4.

### §2.7 `RecordSearchService` security model — explicit owner-adjudication surface

This is the §7.3 bullet 3 question.

**Empirical evidence** (`RecordSearchService.cs:36-39` XML doc):
> "Important: The spaarke-records-index does NOT have a tenantId field for tenant isolation. This differs from the knowledge-index. Security is enforced at the Dataverse layer."

**Filter builder** (`RecordSearchService.cs:240-288 BuildRecordFilter`):
- Mandatory: `recordType eq '<type>'` OR `search.in(recordType, ...)`.
- Optional: `organizations/any()`, `people/any()`, `referenceNumbers/any()`.
- **NO tenant filter. NO privilege-group filter** (compare to `RagService.cs:1052-1053` `PrivilegeFilterBuilder.BuildFilter(userGroupIds)` AIPU2-027 fail-closed).

**Index content** (per `SelectFields` `RecordSearchService.cs:65-69`): `id, recordType, recordName, recordDescription, organizations, people, referenceNumbers, keywords, lastModified, dataverseRecordId, dataverseEntityName` — metadata projections, no document content.

**Layered-defense reasoning**:
- Records in the index are scrubbed metadata projections of Dataverse records.
- Dataverse access is governed by Business Units, Security Roles, FLS, Hierarchical Security — enforced at Dataverse read.
- The index does NOT carry sensitive content.
- Authoritative read remains Dataverse-mediated.

**VERDICT: SURFACE FOR OWNER ADJUDICATION**, not adjudicate here. The architectural reasoning is internally consistent IF AND ONLY IF: (a) index contents are confirmed non-sensitive, (b) every consumer chain resolves through Dataverse before exposing content, (c) the entropic correlation-attack risk (learning record IDs ↔ organization names via index without read-access) is acceptable. Per brief §4 + Q-003 (sequential cross-team coordination), this routes to Security team + Record-Matching feature team. **Recommendation: KEEP-PENDING-SECURITY-REVIEW.**

---

## §3 Per-service decision table

| # | Service | Path (Services/Ai/) | Decision | Rationale | Migration cost | Cross-team owner |
|---|---|---|---|---|---|---|
| 1 | `RagService` + `NullRagService` | `RagService.cs` + `NullRagService.cs` + `IRagService.cs` | **KEEP (designate canonical reference impl)** | 12 behavioral consumers (most of any AI service); P3 Fail-Fast Null peer correctly handles BOTH compound-OFF (line 223) AND AI-Search-keys-missing (line 561) gates; **gold-standard ADR-032 double-gate reference impl**; tenant + privilege-group filter compulsory (AIPU2-027 fail-closed). LATENT BUG check (§2.5): PASSED. | n/a | AIPL (originating) |
| 2 | `SemanticSearchService` | `SemanticSearch/SemanticSearchService.cs` | **KEEP** | 2 behavioral consumers; conditional endpoint mapping symmetric with conditional DI → §F.1 anti-pattern does NOT apply (§2.4 verified); embedding-failure fallback to keyword-only correct; RRF/vector/keyword three-mode hybrid materially distinct from RagService. | n/a | AIPU R1 |
| 3 | `RecordSearchService` | `RecordSearch/RecordSearchService.cs` | **KEEP (with explicit security adjudication surface)** | 3 behavioral consumers (inventory undercounted `AiAnalysisNodeExecutor`); conditional endpoint mapping symmetric with conditional DI; distinct domain; security model layering consistent per §2.7 IF assumptions hold. Surface §7.3 bullet 3 to Security. NOT §F.1 anti-pattern. | n/a (KEEP) + S (security review owner-driven) | Record-Matching + Security |
| 4 | `PlaybookEmbeddingService` (concrete) | `PlaybookEmbedding/PlaybookEmbeddingService.cs` | **KEEP-SEPARATE (REJECT consolidation with SemanticSearchService)** | Per §2.6: shared Azure AI Search substrate is thin coincidence; domains, security models, document shapes, API surfaces, lifecycles all genuinely distinct. Factory-instantiation per ADR-010 correct. Mirrors Sub-Agent E's REJECT verdict for forced classifier consolidation. | n/a | SprkChat r2 |
| 5 | `NullSemanticSearchService` (hypothetical) | (does not exist) | **DO NOT ADD** | Per §2.4 endpoint-mapping symmetry — no §F.1 anti-pattern to remediate. Adding Null peer would add publish-size + cognitive load for zero defect-coverage gain. Confirm rejection of inventory §7.3 bullet 1. | n/a | — |
| 6 | `NullRecordSearchService` (hypothetical) | (does not exist) | **DO NOT ADD** | Same rationale as #5. | n/a | — |
| 7 | `NullPlaybookEmbeddingService` (hypothetical) | (does not exist) | **DO NOT ADD** | Same rationale as #5 + factory-instantiation pattern (no DI graph to fail). | n/a | — |

**Decision distribution**: 4 KEEP (all 4 substrates) + 3 explicit DO-NOT-ADD-Null-peer (responding to inventory §7.3 bullet 1) + 0 consolidate + 0 deprecate + 0 delete + 1 security-surface (KEEP-PENDING for #3).

### §3.1 HARD GATE on "delete" — N/A
No DELETE candidates. All 4 services ACTIVE with 2-12 behavioral consumers each.

---

## §4 Cross-cutting findings

### §4.1 Inventory §7.3 bullet 1 has a clean architectural answer
**Endpoint mapping conditionality drives Null-peer requirements**. RagEndpoints unconditional → needs Null peer. Other 3 endpoints compound-gated symmetric with service registration → no Null peer needed. The asymmetry (1-of-4) is intentional. Consistent with ADR-032 §F.1.

### §4.2 `RagService` is the canonical reference for ADR-032 double-gate sub-mechanism
`RagService`+`NullRagService` is the ONLY service demonstrating the "double-gate" pattern: compound-AI gate (outer) + AI-Search-keys gate (inner); both OFF branches register the Null peer; single Null impl throws `FeatureDisabledException` with stable `ai.rag.disabled` ErrorCode. Gold-standard reference for future BFF services depending on configurable Azure-service credentials. **Currently undocumented**; ADR candidate.

### §4.3 Tenant-isolation differential is a real architectural distinction

| Service | Tenant filter | Privilege filter | Cross-tenant risk |
|---|---|---|---|
| `RagService` | MANDATORY (line 973) | MANDATORY (AIPU2-027 fail-closed line 1052) | LOW (defense-in-depth) |
| `SemanticSearchService` | YES (via `SearchFilterBuilder.BuildFilter`) | NO | MEDIUM (single layer) |
| `RecordSearchService` | NO (explicit disclaim) | NO | HIGH-IF-INDEX-SENSITIVE / LOW-IF-METADATA-ONLY |
| `PlaybookEmbeddingService` | NO (system-config artifacts) | NO | LOW (playbooks are admin-scoped) |

Per-substrate design choice is legitimate when each substrate has a different sensitivity profile. **Should be documented** in the canonical-search-stack ADR candidate.

### §4.4 Inventory minor corrections
- §2.3.2: `DocumentClassifierHandler.cs` imports `IRagService` at HEAD, NOT `ISemanticSearchService` (inventory typo).
- §2.3.3: `AiAnalysisNodeExecutor.cs` injects `IRecordSearchService` (line 37, 44) — inventory missed; Cat 7 Sub-Agent D caught it; Cat 3 confirms.

### §4.5 Shared substrate canonicalization opportunity (Cat 4 territory)
All 4 search services use Azure AI Search + 3072-dim text-embedding-3-large. 3 of 4 (Rag, Semantic, Record) use `IEmbeddingCache`. The "try cache → cache miss → generate via IOpenAiClient → cache result" pattern is replicated in `RagService.cs:169-179`, `SemanticSearchService.GetEmbeddingWithCacheAsync` (private), and `RecordSearchService.cs:218-234`. **Extract to `IEmbeddingCache.GetOrGenerateAsync` extension** — Cat 4 consolidation lever. Not Cat 3 scope.

### §4.6 `PlaybookEmbeddingService` lacks `IEmbeddingCache` optimization
`SearchPlaybooksAsync:177-179` calls `_openAiClient.GenerateEmbeddingAsync` without cache lookup. If `PlaybookDispatcher` Stage 1 (1.5s budget per chat session) hits this, the missed cache hit is a real latency cost. Flag for SprkChat team.

### §4.7 `RecordSearchService` Redis cache uses non-canonical pattern
Per W1 Sub-Agent A Cat 4 row 18: inline `IDistributedCache` per query. Cat 3 confirms. Cat 4 consolidation lever.

### §4.8 Configuration namespace split surfaces in Cat 3
`RagService` injects BOTH `IOptions<DocumentIntelligenceOptions>` (legacy `Configuration/` namespace) AND `IOptions<AiSearchOptions>` (newer `Options/` namespace). `RecordSearchService` injects `DocumentIntelligenceOptions` only. `SemanticSearchService` injects neither (uses `IKnowledgeDeploymentService` routing). `PlaybookEmbeddingService` uses hardcoded constants. This is inventory §4.3 "redundant namespace split" manifesting in Cat 3. **Surface for DI+Config sub-agent.**

---

## §5 Canonical naming candidates (Q-004 framing — candidates only, NOT locked)

### §5.1 Candidate framing: "Spaarke Canonical Search Substrates"
A four-substrate stack with explicit roles:
1. **Knowledge-base search** — `IRagService` (canonical, double-gate Null peer, tenant + privilege filtered, AIPL-owned).
2. **Discovery / semantic search** — `ISemanticSearchService` (tenant-routed, single-deployment, three hybrid modes, no privilege filter today — security review surface).
3. **Record-matching search** — `IRecordSearchService` (non-tenant-indexed metadata layer, Dataverse-mediated security, security review surface).
4. **Playbook intent-matching search** — `PlaybookEmbeddingService` concrete (factory-instantiated, single-purpose, integral to SprkChat r2 PlaybookDispatcher two-stage).

### §5.2 Naming axes
- "Search Substrate" — descriptive of four-index/four-domain reality (**recommended**).
- "Search Service Layer" — implies a layer; loses per-domain distinctness.
- "AI Search Facade Family" — overloads "facade" with ADR-013 Public Contracts facade.

**Recommendation for W3**: adopt "Spaarke Canonical Search Substrates" as cross-reference name in ADRs and constraint docs. Defer lock to end-of-audit per Q-004.

### §5.3 Cat 3 does NOT recommend folder restructuring
The 4 substrates currently live in 4 paths (`Services/Ai/` for RAG, plus 3 subfolders). Moving `RagService.cs`+`NullRagService.cs`+`IRagService.cs` to `Services/Ai/Rag/` for symmetry is **VERY LOW PRIORITY** cosmetic cleanup.

---

## §6 Drift report (snapshot 357e6936 vs HEAD d862bec6)

### §6.1 Code drift verified
`git diff --stat 357e6936..HEAD -- src/server/api/Sprk.Bff.Api/Services/Ai/` returns EMPTY. ZERO drift.

Verified by reading full source: `RagService.cs` (1253 LOC), `NullRagService.cs` (135 LOC), `SemanticSearchService.cs` sample (300/~550), `RecordSearchService.cs` sample (300/~520), `PlaybookEmbeddingService.cs` (336 LOC) — all match inventory descriptions. DI registration sites (`AnalysisServicesModule.cs:55, 58, 222-225, 549-561`) match.

### §6.2 Inventory minor corrections empirically surfaced
- §2.3.3 undercounted `AiAnalysisNodeExecutor` as `IRecordSearchService` consumer (Cat 7 Sub-Agent D caught; Cat 3 confirms).
- §2.3.2 mislabeled `DocumentClassifierHandler` as `ISemanticSearchService` consumer; at HEAD it imports `IRagService`.

### §6.3 No new search services / no new Null peers / no new endpoint mappings
Symmetry intact. The "1-of-4 has Null peer" state is unchanged.

---

## §7 Open questions for owner review (packaged per Q-002 single end-of-audit)

1. **(MEDIUM URGENCY) `RecordSearchService` security model adjudication**. Per §2.7 + §4.3: tenant isolation disclaimed in favor of Dataverse-layer security. Internally consistent IF (a) index carries only non-sensitive metadata projections, (b) every consumer chain resolves through Dataverse before exposing content, (c) correlation-attack entropy risk is acceptable. **Security team + Record-Matching feature team adjudication needed.** Cat 3 recommends: KEEP-PENDING-SECURITY-REVIEW.

2. **Confirm rejection of inventory §7.3 bullet 1** ("RagService has Null peer; the other 3 don't. Why?"). Cat 3's empirical answer: **endpoint mapping conditionality differential drives Null-peer requirements; the 1-of-4 asymmetry is intentional and architecturally consistent.** Owner accepts?

3. **Confirm rejection of inventory §7.3 bullet 2** ("PlaybookEmbeddingService merge with SemanticSearchService?"). Cat 3 verdict: REJECT — different domains, security models, document shapes, API surfaces, lifecycles.

4. **`SemanticSearchService` privilege-filter gap**: per §4.3, only `RagService` mandates a privilege-group filter. `SemanticSearchService` filters by tenant but NOT by privilege groups. Intentional access-model difference or security gap predating AIPU2-027? **Surface for security team adjudication** (parallel to bullet 1).

5. **`PlaybookEmbeddingService` cache-gap opportunity (low priority)**: `SearchPlaybooksAsync` does NOT cache query embeddings. If `PlaybookDispatcher` Stage 1 calls per-request, per-call OpenAI cost is real. Worth profiling? **SprkChat team owns the call-frequency assessment.**

6. **Configuration namespace split surfaces in Cat 3**: `RagService` injects both `DocumentIntelligenceOptions` (`Configuration/`) AND `AiSearchOptions` (`Options/`). Whether these merge is a DI+Config concern; Cat 3 surfaces the duplication as evidence. **Defer to DI+Config sub-agent.**

7. **Promote double-gate Null-Object pattern (RagService) to explicit ADR candidate** — peer to ADR-030 single-gate Null-Object? Currently undocumented. See §8.

---

## §8 ADR candidates (per Q-005 — surfaced as bullet items only, NOT authored)

- **(ADR-candidate F-1) "Search-Substrate Canonical Architecture"** — codify the 4-substrate Spaarke Canonical Search Stack per §5.1. Cross-references ADR-013, ADR-032, ADR-007.

- **(ADR-candidate F-2) "DI Double-Gate Null-Object Pattern"** — peer to ADR-030 single-gate. Codifies the pattern observed in `RagService`/`NullRagService` where two configuration gates (outer feature + inner credentials) both require the Null peer. Currently the only example in the BFF. Canonical reference for future services depending on configurable Azure credentials.

- **(ADR-candidate F-3) "Search-Substrate Security Model Matrix"** — document per-substrate tenant + privilege filter requirements per §4.3 table. Captures which substrate uses fail-closed group resolution (RagService AIPU2-027) and which relies on Dataverse-mediated consumer-chain enforcement (RecordSearchService). Cross-reference ADR-014 + AIPU2-027.

- **(ADR-candidate F-4) "Endpoint Mapping ↔ DI Registration Symmetry Rule"** — codify §2.4 principle: when endpoint is unconditionally-mapped, ALL injected services MUST have Null peer under every OFF-branch; when endpoint mapping is conditional, services MUST be gate-symmetric. Formal converse of §F.1 anti-pattern. (Cat 6 Sub-Agent C's ADR-candidate C is a specific instance; F-4 generalizes.)

- **(ADR-candidate F-5, LOW PRIORITY) "Shared Embedding-Cache Helper"** — extract 3-of-4 search services' "try cache → cache miss → generate → cache" pattern into `IEmbeddingCache.GetOrGenerateAsync` extension. Cross-coordinate with Cat 4.

---

# Sub-Agent F Final Status Report

1. **Status**: COMPLETED (8/8 sections delivered; empirical verification of all 3 inventory §7.3 questions complete)
2. **Output file path**: `projects/bff-ai-architecture-audit-r1/notes/phase2/analysis-search.md`
3. **Services analyzed**: 4 search services + index-schema cross-reference
4. **Decision distribution**: 4 KEEP (all substrates) + 1 KEEP-PENDING-SECURITY-REVIEW (overlapping with #3) + 3 DO-NOT-ADD-Null-peer (rejecting inventory §7.3 bullet 1) + 1 REJECT-consolidation (PlaybookEmbedding↔Semantic) + 0 deprecate/delete. LATENT BUG verified at IRagService layer: NONE.
5. **Drift findings**: ZERO code drift `357e6936`→`d862bec6`. Two minor inventory mislabels surfaced (§6.2).
6. **Cross-cutting observations**:
   - The Null-peer asymmetry has a clean architectural answer (§2.4 + §4.1): endpoint mapping conditionality drives Null-peer requirements. 1-of-4 is correct.
   - RagService/NullRagService is the canonical reference for ADR-032 double-gate sub-mechanism.
   - RecordSearchService security model internally consistent IF assumptions hold (§2.7); surface for owner.
   - PlaybookEmbedding↔Semantic consolidation REJECTED (§2.6).
   - Tenant + privilege filter matrix (§4.3) is a per-substrate design choice; should be documented.
   - Cat 4 lever (not Cat 3): 3-of-4 share embedding-cache pattern (§4.5); extract to `IEmbeddingCache.GetOrGenerateAsync`.
7. **Open questions for owner**: 7 total in §7; highest priority = bullet 1 RecordSearchService security adjudication; bullet 4 SemanticSearchService privilege-filter gap.
8. **Recommendations for W3 dispatch (Cat 5 Prompts precondition gate)**:
   - **Cat 5 precondition MET**: Cat 1 + Cat 3 verdicts both landed.
   - Cat 3's KEEP verdicts do NOT change consumer contracts of any search service; Cat 5 can focus on prompt-tooling concerns without search-substrate variability.
   - Cat 7 deferred re-dispatch NOT TRIGGERED by Cat 3.
   - For Cat 4: Cat 3 surfaces shared embedding-cache helper opportunity (§4.5) as new lever; confirms Sub-Agent A's row 18 (RecordSearchService inline IDistributedCache).
   - For Cat 6: Sub-Agent C's IInsightsAi LATENT BUG remains valid; Cat 3 confirms the bug is in InsightsOrchestrator transitive-deps, NOT in IRagService Null-Object layer.
   - For DI+Config sub-agent: surface `Configuration/` vs `Options/` namespace split as evidence (RagService injects both) + inconsistent `IOptions<T>` adoption.
