# AI Resource Activation & Integration — Design Specification

> **Project**: ai-spaarke-platform-enhancents-r3
> **Created**: 2026-03-04
> **Status**: Draft v2 — Tiered Knowledge Architecture
> **Predecessor**: ai-spaarke-platform-enhancements-r1 (Phases 1-4 complete), ai-json-prompt-schema (complete)
> **Related**: post-deployment-work.md (Phase 5 validation + OPS items)

---

## Executive Summary

The AI platform infrastructure is built (R1 Phases 1-4) but underutilized. Azure AI Search indexes are empty, golden reference documents aren't deployed, model selection is hardcoded, and playbook execution doesn't leverage RAG or advanced AI resources. This project activates and integrates the AI resources that already exist in code but aren't operational.

**Core Problem**: Playbooks run as "prompt-in, text-out" — they send document text + a skill prompt to Azure OpenAI and get a response. They don't consult reference knowledge, search for similar documents, or select appropriate models per task. The LLM operates on its training data alone, without domain-specific reference language to improve analysis quality.

---

## Industry Context & References

The RAG landscape has evolved significantly through 2025-2026. Key architectural shifts inform this design:

**Tiered Knowledge Architecture**: The industry has moved from monolithic RAG to tiered memory systems where different knowledge types use different retrieval strategies. High-value reference material should be separated from general document corpora for reliable, deterministic retrieval.
- [Standard RAG Is Dead: Why AI Architecture Split in 2026](https://ucstrategies.com/news/standard-rag-is-dead-why-ai-architecture-split-in-2026/)
- [RAG vs CAG: The Architect's Guide to LLM Memory](https://medium.com/@coyle_41098/rag-vs-cag-the-architects-guide-to-llm-memory-47b4b77eaaed)

**Agentic RAG**: Intelligent orchestration layers route queries to the appropriate knowledge source dynamically — not every query needs every index. The orchestrator decides which sources to consult based on the task.
- [Agentic RAG: How Intelligent Retrieval and Reasoning Are Reshaping Enterprise AI](https://www.kore.ai/blog/what-is-agentic-rag)
- [Legal Document RAG: Multi-Graph Multi-Agent Recursive Retrieval](https://medium.com/enterprise-rag/legal-document-rag-multi-graph-multi-agent-recursive-retrieval-through-legal-clauses-c90e073e0052)

**Hybrid Search as Baseline**: Combining keyword (BM25), vector (cosine similarity), and semantic reranking is now the standard — not an optimization. All our indexes should use this pattern.
- [Azure AI Search RAG Best Practice](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/rag-best-practice-with-ai-search/4357711)
- [Building Production RAG Systems in 2026](https://brlikhon.engineer/blog/building-production-rag-systems-in-2026-complete-architecture-guide)

**Domain-Specific Models**: Smaller, task-specific models outperform general-purpose models for constrained operations (classification, extraction) while larger models excel at nuanced reasoning.
- [The Silent Evolution of LLMs in 2026](https://dev.to/synergy_shock/the-silent-evolution-of-llms-in-2026-2mc4)
- [10 RAG Architectures in 2026: Enterprise Use Cases](https://www.techment.com/blogs/rag-architectures-enterprise-use-cases-2026/)

**Knowledge Runtime**: Successful enterprise deployments treat RAG as a knowledge runtime — an orchestration layer that manages retrieval, verification, and access control as integrated operations.
- [The Next Frontier of RAG: Enterprise Knowledge Systems 2026-2030](https://nstarxinc.com/blog/the-next-frontier-of-rag-how-enterprise-knowledge-systems-will-evolve-2026-2030/)
- [From RAG to Context — A 2025 Year-End Review](https://ragflow.io/blog/rag-review-2025-from-rag-to-context)

---

## Tiered Knowledge Architecture

### The Three Layers

Our platform requires three distinct types of knowledge, each with a different retrieval strategy, serving different purposes during playbook execution:

```
┌─────────────────────────────────────────────────────────────────────┐
│  SKILLS (Instructions — JPS Format)                                 │
│  Tell the LLM WHAT to do and HOW to structure output                │
│  Storage: sprk_analysisaction.sprk_systemprompt (JPS JSON)          │
│  Delivery: Direct prompt injection — no retrieval needed            │
│  Example: "Analyze this NDA. Extract parties, term, scope.          │
│           Output as structured JSON with confidence scores."        │
├─────────────────────────────────────────────────────────────────────┤
│  L1: GOLDEN REFERENCES (Domain Knowledge — Dedicated RAG Index)     │
│  Give the LLM expert reference LANGUAGE to improve understanding    │
│  Storage: Dataverse sprk_analysisknowledge → vectorized in          │
│           dedicated index (spaarke-rag-references)                  │
│  Delivery: RAG retrieval from dedicated small index → inject        │
│           most relevant reference chunks as context tokens          │
│  Example: When analyzing a force majeure clause, retrieve chunks    │
│           from the Contract Terms Glossary that contain expert      │
│           language about force majeure patterns, variations,        │
│           and red flags — giving the LLM richer vocabulary          │
│           and pattern recognition than its training data alone.     │
├─────────────────────────────────────────────────────────────────────┤
│  L2: CUSTOMER DOCUMENTS (Corpus Context — Existing RAG Index)       │
│  Give the LLM context from similar documents analyzed before        │
│  Storage: SPE files → chunked → vectorized in                      │
│           spaarke-knowledge-index-v2 (large, dynamic)               │
│  Delivery: RAG retrieval from main index → inject relevant          │
│           chunks as supplementary context                           │
│  Example: "Here are excerpts from 3 similar NDAs we've analyzed    │
│           previously for this client's matters."                    │
├─────────────────────────────────────────────────────────────────────┤
│  L3: ENTITY CONTEXT (Agentic Retrieval — Records Index)             │
│  Give the LLM awareness of related business entities                │
│  Storage: Dataverse records → vectorized in spaarke-records-index   │
│  Delivery: Orchestrator decides when to query; injects entity       │
│           metadata as context                                       │
│  Example: "This document is linked to Matter 'Acme Corp v BigCo',  │
│           involving parties Acme Corporation and BigCo LLC."        │
└─────────────────────────────────────────────────────────────────────┘
```

### Why Skills and Knowledge Are Different Things

| | Skills (Instructions) | Knowledge (Reference Language) |
|--|----------------------|-------------------------------|
| **Purpose** | Tell the LLM what to do | Improve the LLM's understanding |
| **Content type** | Rules, checklists, output schemas | Domain language, examples, terminology, patterns |
| **Format** | JPS (JSON Prompt Schema) | Vectorized text chunks in AI Search |
| **Delivery** | Direct injection (always loaded) | RAG retrieval (most relevant chunks selected) |
| **Storage** | `sprk_analysisaction.sprk_systemprompt` | `spaarke-rag-references` index |
| **Size** | Small (prompt instructions) | Large (reference corpora, glossaries, clause libraries) |
| **Example** | "Identify all force majeure clauses" | "Force majeure clauses typically include: acts of God, war, pandemic, government action. Standard variations include: narrow (enumerated list only) vs broad (catch-all 'beyond reasonable control'). Red flags: absence of notice requirements, no termination right after extended force majeure period..." |

The skill tells the LLM to look for force majeure clauses. The knowledge gives the LLM expert-level language about what force majeure clauses actually look like — making its analysis significantly more accurate and nuanced than relying on training data alone.

### Why Golden References Need Their Own Index

Golden references must NOT be stored in the same index as customer documents:

1. **Guaranteed retrieval**: In a small, curated index (hundreds of documents), relevant reference chunks are reliably found. In a 100K+ document index, they compete with customer content for relevance slots and may not surface.

2. **Different content nature**: References are authoritative, curated, versioned. Customer documents are varied, messy, constantly changing. Mixing them degrades retrieval precision for both.

3. **Different query intent**: When querying references, we want "what does expert knowledge say about this topic?" When querying customer documents, we want "what similar documents have we seen before?" These are different questions against different corpora.

4. **Deterministic behavior**: A playbook author linking KNW-002 (NDA Checklist) expects that reference material to reliably influence the analysis. With a shared index, there's no guarantee the reference chunks rank high enough to be included.

5. **Independent lifecycle**: References are updated by subject matter experts on a curation schedule. Customer documents flow through an ingestion pipeline. Separate indexes allow independent management.

### How Golden References Add Value (The Full Flow)

```
User uploads an NDA for analysis
  ↓
Playbook "NDA Review" executes Action "Clause Risk Analysis"
  ↓
SKILL loaded (JPS): "Identify and assess risk in each clause.
  For each clause, provide: clause type, risk level, explanation."
  ↓
L1 KNOWLEDGE retrieval (dedicated reference index):
  Query: "non-disclosure agreement confidentiality obligations
          exclusions term duration non-compete"
  (query built from document content + action context)
  ↓
  spaarke-rag-references index returns top-5 chunks:
  - KNW-001 chunk: "Confidentiality obligations typically require...
    Standard carve-outs include: (a) information already public,
    (b) independently developed, (c) received from third party..."
  - KNW-002 chunk: "Red flags in NDA review: missing definition of
    'confidential information', no exclusion for public knowledge,
    perpetual obligations without sunset clause..."
  - KNW-010 chunk: "Non-compete provisions exceeding 24 months are
    generally unenforceable in most US jurisdictions..."
  ↓
These reference chunks are TOKENS injected into the prompt context.
The LLM now has expert-level language about NDA patterns to compare
against the actual document — not just its training data.
  ↓
L2 CUSTOMER DOCUMENTS (optional, if playbook enables):
  Query same index (spaarke-knowledge-index-v2) for similar NDAs
  previously analyzed for this client
  ↓
LLM processes: Skill instructions + Reference knowledge tokens
  + Document content + (optional) similar document context
  ↓
Output: Structured clause-by-clause risk assessment with
  higher accuracy because the LLM had domain reference language
```

The key mechanism: **vector search against the golden reference index finds the most semantically relevant chunks of reference material and injects them as tokens into the LLM prompt.** This is RAG — but against a dedicated, curated corpus rather than the full customer document store.

---

## Azure AI Search Index Architecture

### Target State

```
Azure AI Search (spaarke-search-dev)
│
├── spaarke-rag-references (NEW — Golden Reference Index)
│   ├── Purpose: Domain knowledge for LLM context augmentation
│   ├── Content: KNW-001–010 + future curated references
│   ├── Size: Hundreds of documents (small, curated)
│   ├── Consumers: Playbook execution (L1 knowledge retrieval)
│   ├── Schema: Same vector config as knowledge-v2 (3072-dim)
│   └── Fields: content, contentVector3072, knowledgeSourceId,
│               domain, tags, version
│
├── spaarke-knowledge-index-v2 (Customer Document Index)
│   ├── Purpose: Customer document corpus — semantic search + L2 RAG
│   ├── Content: All customer-uploaded documents (SPE files)
│   ├── Size: 100K+ documents (large, dynamic)
│   ├── Consumers: Semantic Search UI (user-facing) +
│   │             Playbook execution (L2 optional context)
│   └── Existing — 554 docs, actively indexed
│
├── discovery-index (Customer Document Discovery)
│   ├── Purpose: Larger-chunk variant for retrieval quality testing
│   ├── Content: Same documents as knowledge-v2, different chunk size
│   ├── Size: Mirrors knowledge-v2
│   └── Existing — needs population via re-indexing
│
├── spaarke-records-index (Dataverse Entity Index)
│   ├── Purpose: Entity search for L3 agentic retrieval
│   ├── Content: Matters, Projects, Invoices from Dataverse
│   ├── Consumers: Record matching, cross-entity context in playbooks
│   └── Existing — needs population via admin sync endpoint
│
└── spaarke-invoices-dev (Finance-Specific Index)
    ├── Purpose: Invoice semantic search for Finance module
    ├── Content: Invoice documents with financial metadata
    └── Existing — needs population when invoice data available
```

### Indexes to Remove

| Index | Reason |
|-------|--------|
| `knowledge-index` | Legacy pre-v2, 0 bytes, superseded |
| `spaarke-knowledge-index` | Early version, 0 bytes, superseded by v2 |
| `spaarke-knowledge-shared` | Config placeholder, not a real index, 0 bytes |

### Semantic Search vs RAG Clarification

These are two different use cases that share Azure AI Search infrastructure but serve different purposes:

| | Semantic Search | RAG (Knowledge Retrieval) |
|--|----------------|--------------------------|
| **Who uses it** | The user, via search UI or SprkChat | The system, during playbook execution |
| **What it returns** | Document metadata (name, type, preview URL) | Chunk text content (tokens for LLM prompt) |
| **User sees results?** | Yes — search results in UI | No — injected into LLM context invisibly |
| **Deduplication** | Yes (1 result per document) | No (multiple chunks from same source OK) |
| **Index** | `spaarke-knowledge-index-v2` | `spaarke-rag-references` (L1) or `spaarke-knowledge-index-v2` (L2) |
| **Service** | `SemanticSearchService` | `RagService` |
| **Endpoint** | `POST /api/ai/search` | `POST /api/ai/rag/search` |

---

## Current State Assessment

### Azure AI Search Indexes

| Index | Documents | Status | Issue |
|-------|-----------|--------|-------|
| `spaarke-knowledge-index-v2` | 554 | Active | Only index with data; serves both semantic search and L2 RAG |
| `discovery-index` | 0 | Schema exists, dual-write code active | No documents re-indexed since pipeline created |
| `spaarke-records-index` | 0 | Schema exists, sync service exists | Manual trigger only — `BulkSyncAsync()` never called |
| `spaarke-invoices-dev` | 0 | Schema exists, job handler exists | No invoices processed yet |
| `spaarke-rag-references` | — | **DOES NOT EXIST** | Needs to be created for golden reference knowledge |
| `knowledge-index` | 0 | **DEPRECATED** | Remove |
| `spaarke-knowledge-index` | 0 | **DEPRECATED** | Remove |
| `spaarke-knowledge-shared` | 0 | **NOT A REAL INDEX** | Remove — config placeholder |

### Golden Reference Documents (KNW-001–010)

- Content authored in R1 task files (500+ words each, structured markdown)
- PowerShell creation script exists (`scripts/Create-KnowledgeSourceRecords.ps1`)
- **NOT deployed** to Dataverse or vectorized/indexed anywhere
- Playbook → knowledge source N:N relationships exist but knowledge retrieval is not wired into execution
- No dedicated index exists for reference material

### Model Configuration

- `ModelSelector` service exists with operation-type routing (classification → gpt-4o-mini, reasoning → o1-mini, generation → gpt-4o)
- `GenericAnalysisHandler` **hardcodes** `ModelName = "gpt-4o"` — ignores `ModelSelector` entirely
- `PlaybookNode.ModelDeploymentId` field exists in data model but tool handlers ignore it
- Only `gpt-4o-mini` confirmed deployed on Azure OpenAI resource
- `gpt-4o` and `o1-mini` referenced in config but may not be deployed

### Playbook Execution Pipeline

- 11 tool handlers exist but run independently (no knowledge context injection)
- `ScopeResolverService` loads inline skill text but does NOT retrieve vectorized knowledge
- `SemanticSearchToolHandler` is a chat tool, not wired into action node execution
- No pre-execution knowledge retrieval step in `AiAnalysisNodeExecutor`
- No connection between playbook knowledge source relationships and RAG queries during execution

---

## Scope

### In Scope

1. **Index Architecture** — Create golden reference index, populate empty indexes, remove deprecated ones
2. **Golden Reference Deployment** — Deploy KNW-001–010 to Dataverse, vectorize and index into dedicated reference index
3. **Knowledge-Augmented Execution** — Wire L1 reference retrieval + optional L2 document retrieval into playbook action node execution
4. **Model Selection Integration** — Wire `ModelSelector` into execution pipeline, deploy models to Azure OpenAI
5. **Embedding Model Governance** — Document strategy, clean up legacy 1536-dim vectors

### Out of Scope

- New tool handler implementations (Document Intelligence forms/tables, web search)
- R1 Phase 5 validation work (separate: post-deployment-work.md)
- Production ingestion pipeline (OPS-01 in post-deployment-work.md)
- Cost monitoring infrastructure (OPS-06 in post-deployment-work.md)
- Multi-provider support (Anthropic, OpenAI direct) — future
- Graph RAG / knowledge graph construction — future enhancement

---

## Phase 1: Index Architecture & Cleanup

### 1A. Create Golden Reference Index (`spaarke-rag-references`)

Create new Azure AI Search index for curated reference knowledge:

**Schema** (`infrastructure/ai-search/spaarke-rag-references.json`):
- `id` (string, key) — chunk ID: `{knowledgeSourceId}_{chunkIndex}`
- `tenantId` (string, filterable) — "system" for shared references, tenant GUID for tenant-specific
- `knowledgeSourceId` (string, filterable) — links back to `sprk_analysisknowledge` record
- `knowledgeSourceName` (string, searchable) — human-readable name (e.g., "NDA Checklist")
- `domain` (string, filterable, facetable) — knowledge domain (e.g., "contract-law", "finance", "employment")
- `content` (string, searchable) — chunk text content
- `contentVector3072` (vector, 3072-dim) — embedding via text-embedding-3-large
- `tags` (Collection(string), filterable) — categorization tags
- `version` (string, filterable) — content version for cache invalidation
- `chunkIndex` (int32) — position within source document
- `chunkCount` (int32) — total chunks for source
- `createdAt`, `updatedAt` (DateTimeOffset) — timestamps

**Vector configuration**: Same HNSW profile as knowledge-v2 (cosine metric, 3072 dimensions).
**Semantic configuration**: Enable semantic ranking with `content` as semantic field.

**Estimated size**: 10 knowledge sources × ~10 chunks each = ~100 chunks. Tiny index — fast, deterministic retrieval.

### 1B. Remove Deprecated Indexes

Remove from Azure AI Search:
- `knowledge-index` (legacy, 0 bytes)
- `spaarke-knowledge-index` (legacy, 0 bytes)
- `spaarke-knowledge-shared` (config placeholder, 0 bytes)

Update `AnalysisOptions.SharedIndexName` default from `"spaarke-knowledge-shared"` to `"spaarke-knowledge-index-v2"`.

Archive schema files in `infrastructure/ai-search/_archive/`.

### 1C. Populate Records Index

**Problem**: `DataverseIndexSyncService.BulkSyncAsync()` exists but has never been called.

**Action**:
1. Call `POST /api/admin/record-matching/sync` to bulk-sync all Matters, Projects, Invoices
2. Verify document count in `spaarke-records-index` matches Dataverse record count
3. Test search: `POST /api/ai/records/search` with sample queries
4. Add `tenantId` field to records index schema (currently missing — security gap)
5. Create background job for incremental sync (new `RecordIndexSyncJobHandler`):
   - Trigger: Service Bus message when records are created/updated
   - Fallback: Scheduled daily incremental sync via `IncrementalSyncAsync(since)`

### 1D. Validate Discovery-Index Dual-Write

**Problem**: `RagIndexingPipeline` dual-writes to both indexes, but discovery-index has 0 docs.

**Action**:
1. Verify `AiSearchOptions.DiscoveryIndexName` is correctly configured in App Service settings
2. Re-index a test document via `POST /api/ai/knowledge/documents` and verify both indexes receive chunks
3. If working: trigger re-index of existing 554 documents to populate discovery-index
4. If broken: debug the pipeline and fix

### 1E. Validate Invoice Index

**Problem**: `InvoiceIndexingJobHandler` exists but no invoices have been processed.

**Action**:
1. Check if any `sprk_invoice` records exist in Dataverse
2. If yes: trigger indexing endpoint
3. Verify documents appear in `spaarke-invoices-dev`
4. If no invoices exist: defer until Finance module has test data

---

## Phase 2: Golden Reference Deployment

### 2A. Deploy Knowledge Source Records to Dataverse

Execute `scripts/Create-KnowledgeSourceRecords.ps1` to create 10 `sprk_analysisknowledge` records (KNW-001–010).

Verify:
- All 10 records visible in Dataverse
- Content populated in `sprk_content` field (structured markdown, 500+ words each)
- N:N relationships to relevant playbooks established (`sprk_playbook_knowledge`)
- Domain tags assigned (contract-law, finance, employment, etc.)

### 2B. Build Reference Indexing Pipeline

Create a service to vectorize and index golden reference content into `spaarke-rag-references`:

**New service**: `ReferenceIndexingService`
- Input: `sprk_analysisknowledge` record (knowledge source ID + content)
- Process:
  1. Chunk content (512-token chunks, 100-token overlap — same as knowledge-v2)
  2. Generate embeddings via `text-embedding-3-large` (3072-dim)
  3. Build `KnowledgeDocument` objects with `knowledgeSourceId`, `domain`, `tags`
  4. Upload to `spaarke-rag-references` index
- Trigger: Manual via admin endpoint + callable from seed data script
- Idempotent: Delete existing chunks for source before re-indexing

**New endpoint**: `POST /api/admin/knowledge/index-references`
- Indexes all `sprk_analysisknowledge` records where `sprk_issystem = true`
- Or single: `POST /api/admin/knowledge/index-reference/{knowledgeSourceId}`

### 2C. Index All 10 Knowledge Sources

After deploying records (2A) and building the pipeline (2B):
1. Call `POST /api/admin/knowledge/index-references`
2. Verify chunk count in `spaarke-rag-references` (~100 chunks for 10 sources)
3. Test retrieval: query "force majeure clause obligations" and verify relevant KNW-001/KNW-008 chunks return
4. Test scoped retrieval: query with `knowledgeSourceId` filter for a specific source

### 2D. Wire Reference Retrieval into RagService

Extend `RagService` (or create `ReferenceRetrievalService`) to query the dedicated reference index:

```csharp
// New method on IRagService or new service
Task<RagSearchResponse> SearchReferencesAsync(
    string query,
    IEnumerable<string>? knowledgeSourceIds,  // filter to specific sources
    string? domain,                            // filter by domain
    int topK = 5,
    CancellationToken cancellationToken = default);
```

This queries `spaarke-rag-references` only — never the customer document index. The playbook's N:N knowledge source relationships provide the `knowledgeSourceIds` filter.

---

## Phase 3: Knowledge-Augmented Playbook Execution

### 3A. Add Knowledge Retrieval to Action Node Execution

**Current flow** (`AiAnalysisNodeExecutor`):
```
Load action → Load skill (JPS prompt) → Call OpenAI → Return result
```

**New flow**:
```
Load action → Load skill (JPS prompt)
  → Retrieve L1 reference knowledge (dedicated index)
  → [Optional] Retrieve L2 document context (main index)
  → [Optional] Retrieve L3 entity context (records index)
  → Build prompt: skill + knowledge tokens + document content
  → Call OpenAI (model selected per node config)
  → Return structured output
```

**Implementation in `AiAnalysisNodeExecutor.ExecuteAsync()`**:

1. After loading the action's JPS prompt, resolve linked knowledge sources from playbook N:N relationships
2. Build a semantic query from the document being analyzed (title + key content + action context)
3. Call `SearchReferencesAsync()` with `knowledgeSourceIds` filter → returns top-K reference chunks
4. Inject retrieved chunks into prompt as knowledge context section
5. Proceed with OpenAI call

**Prompt assembly order**:
```
[System Message]
{Skill instructions from JPS — role, task, constraints, output schema}

[Knowledge Context]
The following reference material provides domain expertise relevant to
this analysis. Use it to inform your assessment and improve accuracy.

### Reference: Common Contract Terms Glossary
{chunk content — expert language about contract terminology...}

### Reference: NDA Checklist
{chunk content — expert language about NDA review criteria...}

[Document Content]
Analyze the following document:
{user's document text}
```

### 3B. Configurable Knowledge Retrieval per Action

Not every action needs knowledge retrieval. Add configuration to action/playbook nodes:

| Setting | Options | Default |
|---------|---------|---------|
| `knowledgeRetrievalMode` | `auto` / `always` / `never` | `auto` |
| `knowledgeTopK` | 3–10 | 5 |
| `includeDocumentContext` | `true` / `false` | `false` |
| `includeEntityContext` | `true` / `false` | `false` |

- `auto`: Retrieve from reference index if playbook has linked knowledge sources
- `always`: Retrieve even if no explicit knowledge sources linked (uses domain tag matching)
- `never`: Skip knowledge retrieval (for simple classification tasks that don't benefit)

### 3C. Optional L2 Customer Document Context

When `includeDocumentContext = true`, also query `spaarke-knowledge-index-v2`:
- Query: Same semantic query used for L1
- Filter: `tenantId` + optionally `parentEntityId` (same matter/project)
- Returns: Chunks from previously analyzed customer documents
- Injected as: "Similar documents previously analyzed" section in prompt

This is optional per action because not all analyses benefit from seeing prior work.

### 3D. Optional L3 Entity Context via Records Index

When `includeEntityContext = true`, query `spaarke-records-index`:
- Query: Parent entity of the document being analyzed
- Returns: Matter/project/invoice metadata and description
- Injected as: "Business context" section in prompt
- Enables: "This NDA is part of Matter 'Acme Corp Acquisition' involving..."

### 3E. Knowledge Retrieval Result Caching

When multiple action nodes in a playbook query the same knowledge sources:
- Cache RAG results per `(query-hash, knowledgeSourceIds, topK)` in Redis
- TTL: Duration of playbook execution session
- Prevents duplicate embedding generation and search calls across nodes

---

## Phase 4: Model Selection Integration

### 4A. Deploy Additional Models to Azure OpenAI

Verify/deploy on `spaarke-openai-dev`:

| Deployment Name | Model | Purpose | Est. TPM |
|----------------|-------|---------|----------|
| `gpt-4o` | GPT-4o | High-quality analysis, RAG-augmented reasoning | 30K |
| `gpt-4o-mini` | GPT-4o Mini | Classification, validation, entity extraction, chat | 60K |
| `o1-mini` | o1-mini | Complex multi-step reasoning (legal analysis) | 10K |

Verify: `az cognitiveservices account deployment list --name spaarke-openai-dev --resource-group spe-infrastructure-westus2`

### 4B. Wire ModelSelector into Playbook Execution

**Fix GenericAnalysisHandler** (currently hardcoded):
```csharp
// BEFORE (hardcoded):
ModelName = "gpt-4o"

// AFTER (resolution chain):
ModelName = context.ModelDeploymentId           // 1. Node-level override
    ?? _modelSelector.SelectModel(context.OperationType)  // 2. Operation-type routing
    ?? _options.DefaultModel                    // 3. Global default
```

### 4C. Model Selection Guidelines

| Operation Type | Recommended Model | Rationale |
|---------------|------------------|-----------|
| Document classification | `gpt-4o-mini` | Fast, cheap, simple categorization |
| Entity extraction | `gpt-4o-mini` | Structured output, moderate complexity |
| Contract clause analysis | `gpt-4o` | Nuanced legal reasoning + reference context |
| Risk detection | `gpt-4o` | High-stakes, accuracy critical |
| Financial calculations | `gpt-4o-mini` | Structured math, low ambiguity |
| Summary generation | `gpt-4o-mini` | Straightforward distillation |
| Complex legal reasoning | `o1-mini` | Multi-step reasoning chains |
| Intent classification (chat) | `gpt-4o-mini` | Fastest response for interactive UX |
| RAG-augmented analysis | `gpt-4o` | Long context + reference integration |

### 4D. Playbook Builder UI: Model Selection

Add model selection dropdown to action node configuration in Playbook Builder:
- Default: "Auto (recommended)" — uses `ModelSelector` routing
- Override: Select specific deployment from `ModelEndpoints` list
- Stored in `PlaybookNode.ModelDeploymentId`

---

## Phase 5: Embedding Model Governance

### 5A. Document Current Strategy

- **Primary**: `text-embedding-3-large` (3072 dimensions) — all indexes use this
- **Legacy**: `text-embedding-3-small` (1536 dimensions) — deprecated, fields remain for backward compat
- **Cache**: Redis, 7-day TTL, SHA256 content hashing
- **New index** (`spaarke-rag-references`): Uses `contentVector3072` only — no legacy fields

### 5B. Clean Up Legacy Vector Fields

After confirming all queries use `contentVector3072`:
1. Stop writing to `contentVector` (1536-dim) in `RagIndexingPipeline`
2. Remove `contentVector` from new index schemas (already excluded from `spaarke-rag-references`)
3. Keep field in existing `spaarke-knowledge-index-v2` for backward compat (Azure AI Search doesn't support field deletion)

### 5C. Embedding Model Change Protocol

Document the re-indexing requirement for future model upgrades:
- Changing embedding model requires re-indexing ALL documents in affected indexes
- Process: Deploy new model → Create new vector field → Batch re-embed → Switch queries → Remove old field
- `spaarke-rag-references` is small (~100 chunks) — re-indexing takes seconds
- `spaarke-knowledge-index-v2` (554+ docs) — re-indexing takes ~15 minutes

---

## Success Criteria

| Metric | Target |
|--------|--------|
| Golden reference index created | `spaarke-rag-references` exists with ~100 chunks from 10 knowledge sources |
| Deprecated indexes removed | 3 removed (knowledge, knowledge-shared, spaarke-knowledge-index) |
| Active indexes with data | 4 of 5 (references, knowledge-v2, records, discovery); invoices deferred if no data |
| Playbook actions retrieve L1 knowledge | All 10 pre-built playbooks query reference index before analysis |
| Analysis quality improvement | Measurable improvement in output quality with vs without reference context |
| Model selection wired | GenericAnalysisHandler uses ModelSelector chain, not hardcoded `gpt-4o` |
| Models deployed | gpt-4o + gpt-4o-mini + o1-mini confirmed on Azure OpenAI |
| Records-index populated | Matters + Projects + Invoices indexed and searchable |

---

## Phase Summary

| Phase | Focus | Key Deliverables | Est. |
|-------|-------|-----------------|------|
| **1** | Index Architecture & Cleanup | Create `spaarke-rag-references`, remove 3 deprecated, populate records + discovery | 10h |
| **2** | Golden Reference Deployment | Deploy KNW-001–010, build reference indexing pipeline, vectorize and index | 12h |
| **3** | Knowledge-Augmented Execution | Wire L1/L2/L3 retrieval into action node execution, configurable per action | 14h |
| **4** | Model Selection Integration | Deploy models, wire ModelSelector into execution, Playbook Builder dropdown | 8h |
| **5** | Embedding Governance | Document strategy, clean up legacy fields, change protocol | 3h |

**Total estimate**: ~47 hours

---

## Dependencies

| Dependency | Required By | Status |
|-----------|-------------|--------|
| Azure OpenAI model deployments (gpt-4o, o1-mini) | Phase 4 | Needs verification/provisioning |
| Dataverse records (Matters, Projects, Invoices) | Phase 1C | Should exist from testing |
| R1 Phase 5 evaluation harness | Quality measurement (pre/post comparison) | Separate project (post-deployment-work.md) |
| Golden reference content (KNW-001–010) | Phase 2A | Content authored, seed script exists |
| Azure AI Search capacity for new index | Phase 1A | Verify SKU supports additional index |

---

## Relationship to post-deployment-work.md

This project (R3) and post-deployment-work.md are complementary:

| Activity | Owner |
|----------|-------|
| **R3**: Create reference index, deploy golden refs, wire knowledge retrieval, fix model selection | This project |
| **Post-deploy AIPL-070–075**: Test corpus, eval harness, quality baseline | post-deployment-work.md |
| **Post-deploy OPS-01**: Production ingestion pipeline for customer documents | post-deployment-work.md |
| **Post-deploy OPS-02**: Ongoing knowledge source curation (adding more golden refs) | post-deployment-work.md |
| **Post-deploy OPS-03**: Prompt refinement (needs R3 + AIPL-073 first) | post-deployment-work.md |

**Recommended sequence**: R3 first (activate infrastructure) → then post-deployment-work.md Phase 5 (measure quality with infrastructure active). This gives the evaluation harness something meaningful to measure — playbooks with vs without knowledge augmentation.

---

## Reference Materials

### Industry Research
- [Standard RAG Is Dead: Why AI Architecture Split in 2026](https://ucstrategies.com/news/standard-rag-is-dead-why-ai-architecture-split-in-2026/)
- [RAG vs CAG: The Architect's Guide to LLM Memory](https://medium.com/@coyle_41098/rag-vs-cag-the-architects-guide-to-llm-memory-47b4b77eaaed)
- [The Next Frontier of RAG: Enterprise Knowledge Systems 2026-2030](https://nstarxinc.com/blog/the-next-frontier-of-rag-how-enterprise-knowledge-systems-will-evolve-2026-2030/)
- [From RAG to Context — A 2025 Year-End Review](https://ragflow.io/blog/rag-review-2025-from-rag-to-context)
- [10 RAG Architectures in 2026: Enterprise Use Cases](https://www.techment.com/blogs/rag-architectures-enterprise-use-cases-2026/)
- [Building Production RAG Systems in 2026](https://brlikhon.engineer/blog/building-production-rag-systems-in-2026-complete-architecture-guide)
- [Legal Document RAG: Multi-Graph Multi-Agent Recursive Retrieval](https://medium.com/enterprise-rag/legal-document-rag-multi-graph-multi-agent-recursive-retrieval-through-legal-clauses-c90e073e0052)
- [Agentic RAG: How Intelligent Retrieval and Reasoning Are Reshaping Enterprise AI](https://www.kore.ai/blog/what-is-agentic-rag)
- [The Silent Evolution of LLMs in 2026](https://dev.to/synergy_shock/the-silent-evolution-of-llms-in-2026-2mc4)
- [Don't Do RAG: When Cache-Augmented Generation is All You Need](https://arxiv.org/html/2412.15605v1)

### Azure & Microsoft
- [Azure AI Search: RAG and Generative AI Overview](https://learn.microsoft.com/en-us/azure/search/retrieval-augmented-generation-overview)
- [Azure AI Search RAG Best Practice](https://techcommunity.microsoft.com/blog/azure-ai-foundry-blog/rag-best-practice-with-ai-search/4357711)
- [Multi-Index RAG with Agentic Search (Microsoft Q&A)](https://learn.microsoft.com/en-us/answers/questions/5555996/how-can-i-implement-multi-indexing-rag-chatbot-wit)
- [RAG with Azure AI Search Tutorial](https://www.pondhouse-data.com/blog/rag-with-azure-ai-search)

### Spaarke Internal
- `docs/guides/SPAARKE-AI-ARCHITECTURE.md` — Current AI tool framework
- `docs/guides/RAG-ARCHITECTURE.md` — RAG pipeline design
- `docs/architecture/auth-AI-azure-resources.md` — Azure resource inventory
- `projects/ai-spaarke-platform-enhancements-r1/spec.md` — R1 specification (Phases 1-4)
- `projects/ai-spaarke-platform-enhancents-r3/post-deployment-work.md` — Phase 5 + operational work

---

*This design should be transformed to spec.md via `/design-to-spec` and then processed via `/project-pipeline` for task decomposition.*
