# AI Embedding Strategy

> **Version**: 1.1
> **Created**: 2026-03-04
> **Updated**: 2026-03-05
> **Project**: AI Spaarke Platform Enhancements R3

---

## Table of Contents

1. [Overview](#overview)
2. [Current Embedding Model](#current-embedding-model)
3. [Per-Index Embedding Configuration](#per-index-embedding-configuration)
4. [Embedding Cache Strategy](#embedding-cache-strategy)
5. [Cost Analysis](#cost-analysis)
6. [Decision Rationale](#decision-rationale)
7. [Embedding Model Change Protocol](#embedding-model-change-protocol)
8. [Related Documentation](#related-documentation)

---

## Overview

Spaarke uses OpenAI text embedding models to convert document text into dense vector representations for semantic search via Azure AI Search. Embeddings are generated during document indexing (write path) and at query time (read path) to enable hybrid search combining keyword matching with vector similarity.

All embedding operations flow through `IOpenAiClient.GenerateEmbeddingAsync()`, which delegates to the Azure OpenAI embedding deployment configured in `DocumentIntelligenceOptions.EmbeddingModel`.

---

## Current Embedding Model

| Property | Value |
|----------|-------|
| **Model** | `text-embedding-3-large` |
| **Dimensions** | 3072 |
| **Provider** | Azure OpenAI (`spaarke-openai-dev`) |
| **Configuration Key** | `DocumentIntelligence__EmbeddingModel` |
| **Dimensions Key** | `DocumentIntelligence__EmbeddingDimensions` |
| **Default (in code)** | `DocumentIntelligenceOptions.EmbeddingModel = "text-embedding-3-large"` |

### Deprecated Model

| Property | Value |
|----------|-------|
| **Model** | `text-embedding-3-small` |
| **Dimensions** | 1536 |
| **Status** | Deprecated. Migration completed via `EmbeddingMigrationService` (Phase 5b). |
| **Migration** | Re-embedded all existing 1536-dim vectors to 3072-dim using `contentVector3072` and `documentVector3072` fields. |

The codebase retains `EmbeddingMigrationService` as a background service that can be enabled via `EmbeddingMigration__Enabled=true` to re-embed documents that still carry only 1536-dim vectors. It processes documents in configurable batches with rate-limit-aware concurrency controls.

---

## Per-Index Embedding Configuration

The platform maintains several Azure AI Search indexes, each using the same embedding model but serving different purposes and data shapes.

### Index Inventory

<!-- TODO(ai-procedure-refactoring): Index inventory below may be stale — verify current index names, vector field names, and chunk sizes against the RagIndexingPipeline implementation and Azure AI Search resource; new indexes may have been added since 2026-03-05 -->

| Index Name | Purpose | Embedding Model | Dimensions | Vector Field(s) | Chunk Size | Estimated Scale |
|------------|---------|----------------|------------|------------------|------------|-----------------|
| `spaarke-knowledge-index-v2` | Customer document knowledge (shared tenant) | text-embedding-3-large | 3072 | `contentVector3072`, `documentVector3072` | 512 tokens (2048 chars) | 554+ documents, thousands of chunks |
| `discovery-index` | Auto-populated discovery chunks for broader context | text-embedding-3-large | 3072 | `contentVector3072`, `documentVector3072` | 1024 tokens (4096 chars) | Mirrors knowledge-v2 document count |
| `spaarke-rag-references` | Golden reference knowledge (domain terminology, clause libraries) | text-embedding-3-large | 3072 | `contentVector3072` | 512 tokens (100-token overlap) | ~100 chunks from ~10 knowledge sources |
| `spaarke-invoices-dev` | Invoice semantic search for financial intelligence | text-embedding-3-large | 3072 | `contentVector` (3072-dim) | Full invoice text | Grows with invoice volume |
| `{tenant}-knowledge` | Dedicated per-tenant indexes (enterprise customers) | text-embedding-3-large | 3072 | Same as knowledge-v2 | 512 tokens | Varies by customer |

### Vector Search Profile

All indexes share the same HNSW vector search configuration:

| Parameter | Value | Rationale |
|-----------|-------|-----------|
| Algorithm | HNSW | Best balance of speed and recall for production workloads |
| Metric | Cosine | Standard for text embeddings; measures angle between vectors |
| m (connections) | 4 | Connections per graph node; keeps memory usage reasonable |
| efConstruction | 400 | High build quality for accurate graph structure |
| efSearch | 500 | High query-time exploration for strong recall |

### Dual-Index Pipeline (Knowledge + Discovery)

The `RagIndexingPipeline` writes every document to two indexes in parallel:

1. **Knowledge Index** (`spaarke-knowledge-index-v2`): 512-token chunks with 50-token overlap. Used for precise RAG retrieval in analysis and chat.
2. **Discovery Index** (`discovery-index`): 1024-token chunks with 100-token overlap. Used for broader document discovery and semantic search.

Both indexes receive independently generated embeddings appropriate to their chunk boundaries. The pipeline generates embeddings with a semaphore-limited concurrency of 16 simultaneous API calls.

---

## Embedding Cache Strategy

Query-time embeddings are cached in Redis to avoid redundant Azure OpenAI API calls for repeated or similar queries.

### Cache Architecture

| Component | Value |
|-----------|-------|
| **Cache Service** | `IEmbeddingCache` (`EmbeddingCache.cs`) |
| **Backend** | Redis (ADR-009: Redis-first caching) |
| **Key Format** | `sdap:embedding:{base64-sha256-hash}` |
| **TTL** | 7 days |
| **Hashing** | SHA256 of input text, base64-encoded |
| **Serialization** | Binary (`float[]` to `byte[]` via `Buffer.BlockCopy`) |

### Cache Flow

```
Query arrives
    |
    v
Compute SHA256 hash of query text
    |
    v
Check Redis: sdap:embedding:{hash}
    |
    +-- HIT  --> Return cached float[] (skip OpenAI call)
    |
    +-- MISS --> Call Azure OpenAI GenerateEmbedding
                    |
                    v
                 Store in Redis with 7-day TTL
                    |
                    v
                 Return float[]
```

### Cache Scope

| Operation | Cached? | Rationale |
|-----------|---------|-----------|
| Search query embeddings | Yes | Repeated queries benefit from cache hits |
| Indexing pipeline embeddings | No | Each chunk is unique; caching adds overhead with no reuse (ADR-009) |
| Invoice indexing embeddings | No | Same as above -- unique content per document |
| Reference retrieval results | Yes (10min) | Multiple playbook nodes querying same references within a session |

### Reference Retrieval Result Cache (R3)

In addition to embedding caching, reference retrieval results are cached in Redis to prevent duplicate searches when multiple playbook action nodes query the same knowledge sources.

| Property | Value |
|----------|-------|
| **Cache Service** | `ReferenceRetrievalService` (inline cache logic) |
| **Backend** | Redis (`IDistributedCache`) |
| **Key Format** | `sdap:rag-ref:{tenantId}:{queryHash}:{sourceIdsHash}:{topK}` |
| **TTL** | 10 minutes (playbook session scope) |
| **Hashing** | SHA256 of query text and source ID list |
| **Serialization** | JSON (`ReferenceSearchResponse`) |

**Cache scope**: Only applies to `ReferenceRetrievalService.SearchReferencesAsync()` (L1 golden reference queries). Customer document queries (L2) and entity context queries (L3) are not cached at the result level.

**Cache key components**:
- `tenantId`: Security isolation (prevents cross-tenant cache hits)
- `queryHash`: SHA256 of the semantic query text
- `sourceIdsHash`: SHA256 of sorted knowledge source ID list
- `topK`: Result count (different topK = different cache entry)

### Error Handling

Cache failures are non-fatal. If Redis is unavailable, the system logs a warning and falls back to generating a fresh embedding from Azure OpenAI. This ensures search availability is not coupled to Redis health.

### Monitoring

| Metric | Description |
|--------|-------------|
| `cache_hits_total{cacheType="embedding"}` | Successful cache retrievals |
| `cache_misses_total{cacheType="embedding"}` | Cache misses requiring API call |
| `cache_hit_rate{cacheType="embedding"}` | Hit rate percentage |

Target cache hit rate: >80% for repeated query patterns.

---

## Cost Analysis

### Embedding API Costs (Azure OpenAI, as of March 2026)

| Model | Price per 1M Tokens | Dimensions | Relative Quality |
|-------|---------------------|------------|-----------------|
| text-embedding-3-large | ~$0.13 / 1M tokens | 3072 | Highest |
| text-embedding-3-small | ~$0.02 / 1M tokens | 1536 | Good |

### Cost Factors

| Factor | Impact | Mitigation |
|--------|--------|------------|
| **Indexing (write path)** | One-time cost per document chunk. Paid at upload time. | Batch processing, rate-limit-aware concurrency |
| **Search (read path)** | Per-query cost for embedding generation. | Redis cache (7-day TTL), >80% hit rate target |
| **Re-indexing** | Full corpus re-embedding required when changing models. | Plan migrations carefully; use `EmbeddingMigrationService` with rate controls |
| **Dual-index writes** | 2x embedding calls per document (knowledge + discovery chunks). | Parallel generation amortizes latency, not cost |

### Cost Estimate by Index

| Index | Estimated Chunks | Tokens per Chunk (avg) | Embedding Cost (one-time) |
|-------|-----------------|----------------------|--------------------------|
| spaarke-knowledge-index-v2 | ~2,770 (554 docs x ~5 chunks) | ~400 | ~$0.14 |
| discovery-index | ~1,108 (554 docs x ~2 chunks) | ~800 | ~$0.12 |
| spaarke-rag-references | ~100 | ~400 | < $0.01 |
| spaarke-invoices-dev | Varies | ~200 | Negligible at current scale |

At current scale, embedding costs are minimal. The dominant cost factor is Azure AI Search infrastructure (index storage and query units), not embedding generation.

---

## Decision Rationale

### Why text-embedding-3-large over text-embedding-3-small

| Criterion | text-embedding-3-large | text-embedding-3-small |
|-----------|----------------------|----------------------|
| **MTEB Benchmark** | Higher across retrieval tasks | Lower |
| **Semantic Precision** | Better discrimination between similar concepts | Adequate for simple queries |
| **RAG Accuracy** | Measurably better retrieval quality in legal/financial domains | Misses nuanced distinctions |
| **Vector Storage** | 3072 dims x 4 bytes = 12 KB per vector | 1536 dims x 4 bytes = 6 KB per vector |
| **Cost** | ~6.5x more expensive per token | Cheapest option |
| **Decision** | Selected as primary model | Deprecated |

**Key factors for the decision:**

1. **Domain Complexity**: Legal and financial documents contain subtle terminology distinctions (e.g., "indemnification" vs. "hold harmless", "net 30" vs. "net 60"). The larger model captures these nuances better.
2. **RAG Quality**: The platform's core value proposition depends on accurate document retrieval. Marginal cost increases are justified by measurably better search results.
3. **Scale Economics**: At current document volumes (hundreds to low thousands), the 6.5x cost difference amounts to pennies. Storage overhead for 3072-dim vectors is also negligible.
4. **Future-Proofing**: Starting with the highest-quality model avoids a costly re-indexing migration later.

### Why Not Reduced Dimensions

OpenAI's text-embedding-3-large supports [Matryoshka Representation Learning](https://openai.com/index/new-embedding-models-and-api-updates/) -- dimensions can be truncated (e.g., to 1024 or 256) with graceful quality degradation. This option was considered but rejected because:

- At current scale, storage savings are negligible
- Quality is the primary optimization target, not cost
- Azure AI Search pricing is driven by index count and search units, not vector size

This decision can be revisited if document volumes exceed 100K+ and storage becomes a concern.

---

## Embedding Model Change Protocol

This section defines the procedure for migrating from one embedding model to another (e.g., from `text-embedding-3-large` to a future model) with zero downtime and full rollback capability.

### When to Use This Protocol

- Upgrading to a newer embedding model (e.g., a future `text-embedding-4`)
- Changing embedding dimensions (e.g., switching to reduced dimensions for cost optimization)
- Switching providers (if moving away from Azure OpenAI)

### Prerequisites

- New embedding model deployed in Azure OpenAI (or alternative provider)
- Sufficient Azure OpenAI quota for batch re-embedding (TPM/RPM limits)
- Azure AI Search index schema supports adding new vector fields
- Maintenance window identified (re-indexing runs in background but consumes API quota)

### Migration Procedure

#### Phase 1: Deploy New Model (No Service Impact)

1. **Deploy the new embedding model** in Azure OpenAI (via AI Foundry portal or Bicep/CLI).
2. **Add a new vector field** to each affected index schema:
   ```json
   {
     "name": "contentVector{newDims}",
     "type": "Collection(Edm.Single)",
     "dimensions": <new-dimensions>,
     "vectorSearchProfile": "knowledge-vector-profile-{newDims}"
   }
   ```
3. **Add a new vector search profile** if dimensions differ:
   ```json
   {
     "name": "knowledge-vector-profile-{newDims}",
     "algorithmConfigurationName": "knowledge-hnsw"
   }
   ```
4. **Update index schemas** via Azure REST API:
   ```bash
   az rest --method PUT \
     --uri "https://spaarke-search-dev.search.windows.net/indexes/{index-name}?api-version=2024-07-01" \
     --headers "Content-Type=application/json" "api-key=<key>" \
     --body @infrastructure/ai-search/{index-name}.json
   ```

**At this point**: Existing search continues using old vectors. No user impact.

#### Phase 2: Batch Re-Embed (Background Processing)

1. **Configure `EmbeddingMigrationService`** (or create a new migration service) with:
   ```json
   {
     "EmbeddingMigration": {
       "Enabled": true,
       "BatchSize": 20,
       "ConcurrencyLimit": 5,
       "DelayBetweenBatchesMs": 2000,
       "SkipAlreadyMigrated": true
     }
   }
   ```
2. **Run the migration** as a background service. It will:
   - Query each index for documents missing the new vector field
   - Re-embed content using the new model
   - Write new vectors alongside existing vectors (both coexist in the same document)
3. **Monitor progress** via structured logs:
   - `[MIGRATION] Processed {count} documents, {remaining} remaining`
   - Watch for rate limit errors and adjust `DelayBetweenBatchesMs` if needed

**Estimated Re-indexing Times** (with default settings: batch=20, concurrency=5, 2s delay):

| Index | Estimated Chunks | Estimated Time | Notes |
|-------|-----------------|----------------|-------|
| `spaarke-rag-references` | ~100 | < 30 seconds | Small index, fast migration |
| `spaarke-knowledge-index-v2` | ~2,770 | ~15 minutes | 554+ docs, primary knowledge base |
| `discovery-index` | ~1,108 | ~6 minutes | Mirrors knowledge-v2 documents |
| `spaarke-invoices-dev` | Varies | Depends on volume | Typically small |
| `{tenant}-knowledge` | Varies | Per-tenant estimation needed | Run per tenant |

**At this point**: Both old and new vectors coexist. Search still uses old vectors.

#### Phase 3: Switch Queries (Controlled Cutover)

1. **Update `DocumentIntelligenceOptions`** to use the new model:
   ```json
   {
     "DocumentIntelligence": {
       "EmbeddingModel": "<new-model-name>",
       "EmbeddingDimensions": <new-dimensions>
     }
   }
   ```
2. **Update `RagService`** (and any other search services) to target the new vector field name in search queries. This typically means updating the `VectorQueries` configuration to reference `contentVector{newDims}` instead of `contentVector3072`.
3. **Update `KnowledgeDocument` model** to map `ContentVector` to the new field via `[JsonPropertyName]` and `[VectorSearchField]` attributes.
4. **Deploy the API** with the updated configuration.
5. **Invalidate the embedding cache**: Flush all `sdap:embedding:*` keys from Redis (the old model's cached embeddings are incompatible with the new model):
   ```bash
   redis-cli --scan --pattern "sdap:embedding:*" | xargs redis-cli DEL
   ```

**At this point**: All new queries use the new model. New document indexing writes new vectors.

#### Phase 4: Cleanup (After Validation)

1. **Validate search quality**: Run a representative set of test queries and compare results against the old model's baseline.
2. **Wait for validation period** (recommended: 1-2 weeks).
3. **Remove old vector fields** from index schemas:
   ```json
   // Remove contentVector3072 and documentVector3072 if migrated to a new dimension
   ```
4. **Remove old migration code** and model references.
5. **Update documentation** (this document, RAG-ARCHITECTURE.md, RAG-CONFIGURATION.md).

### Rollback Procedure

Rollback is possible at any phase because old and new vectors coexist:

| Phase | Rollback Action | Impact |
|-------|----------------|--------|
| Phase 1 (schema update) | Remove new vector fields from index | No impact -- old vectors untouched |
| Phase 2 (re-embedding) | Stop migration service, revert config | No impact -- old vectors still active |
| Phase 3 (query cutover) | Revert `EmbeddingModel` and `EmbeddingDimensions` config, redeploy | Search reverts to old vectors within minutes |
| Phase 4 (cleanup) | No rollback -- old fields deleted | Must re-run Phase 1-3 to restore |

**Critical rule**: Do not proceed to Phase 4 (field removal) until search quality is validated and stakeholders approve.

### Zero-Downtime Guarantee

This protocol achieves zero downtime through the following mechanisms:

1. **Additive schema changes**: New vector fields are added alongside existing fields. No existing fields are modified or removed during migration.
2. **Background re-embedding**: The migration service runs as a background task, consuming spare API quota without blocking user-facing operations.
3. **Atomic query cutover**: The switch from old to new vectors happens via a configuration change and API redeployment. The API is unavailable only during the standard deployment restart window (~30 seconds with Azure App Service slot swaps).
4. **Deferred cleanup**: Old vector fields remain in the index until explicitly removed, allowing instant rollback.

### Checklist

Before starting a model migration:

- [ ] New embedding model deployed in Azure OpenAI
- [ ] Azure OpenAI quota sufficient for batch re-embedding
- [ ] Index schema JSON files updated with new vector fields
- [ ] `EmbeddingMigrationService` configured and tested locally
- [ ] Search quality baseline established (test queries + expected results)
- [ ] Rollback plan reviewed with team
- [ ] Communication sent to stakeholders about maintenance window

After completing migration:

- [ ] All indexes re-embedded (verify via document count queries)
- [ ] Search quality validated against baseline
- [ ] Embedding cache flushed
- [ ] Configuration updated to new model
- [ ] API redeployed
- [ ] Validation period complete (1-2 weeks)
- [ ] Old vector fields removed from schemas
- [ ] Documentation updated

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [RAG-ARCHITECTURE.md](RAG-ARCHITECTURE.md) | Full RAG system architecture including hybrid search pipeline |
| [RAG-CONFIGURATION.md](RAG-CONFIGURATION.md) | Configuration reference for all RAG settings |
| [RAG-TROUBLESHOOTING.md](RAG-TROUBLESHOOTING.md) | Troubleshooting guide for RAG issues |
| [AI-ARCHITECTURE.md](../architecture/AI-ARCHITECTURE.md) | Overall AI architecture and tool framework |
| [auth-AI-azure-resources.md](../architecture/auth-AI-azure-resources.md) | Azure OpenAI resource details and endpoints |

### Key Source Files

| File | Purpose |
|------|---------|
| `src/server/api/Sprk.Bff.Api/Configuration/DocumentIntelligenceOptions.cs` | Embedding model and dimensions configuration |
| `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs` | Embedding generation via Azure OpenAI |
| `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` | Dual-index pipeline (knowledge + discovery) |
| `src/server/api/Sprk.Bff.Api/Services/Ai/IEmbeddingCache.cs` | Redis embedding cache interface |
| `src/server/api/Sprk.Bff.Api/Services/Jobs/EmbeddingMigrationService.cs` | Background migration from 1536 to 3072 dims |
| `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeDocument.cs` | Document schema with vector field annotations |
| `src/server/api/Sprk.Bff.Api/Options/AiSearchOptions.cs` | Index name configuration |
| `infrastructure/ai-search/spaarke-knowledge-index-v2.json` | Knowledge index schema definition |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceIndexingService.cs` | Golden reference knowledge indexing |
| `src/server/api/Sprk.Bff.Api/Services/Ai/ReferenceRetrievalService.cs` | Golden reference knowledge retrieval + result caching |
| `src/server/api/Sprk.Bff.Api/Models/Ai/ReferenceSearchResult.cs` | Reference search response models |
| `src/server/api/Sprk.Bff.Api/Models/Ai/KnowledgeRetrievalConfig.cs` | Per-action knowledge retrieval settings |
| `infrastructure/ai-search/spaarke-rag-references.json` | Golden reference index schema |

---

*Document created: 2026-03-04*
*Updated: 2026-03-05 - Reference retrieval result caching, new source files (AI Resource Activation R3)*
