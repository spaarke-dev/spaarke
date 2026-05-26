# Azure AI Search — Insights Engine supplement (2026-05)

> **Status**: Researched 2026-05-19 for `projects/ai-spaarke-insights-engine-r1/`.
> Supplements (does not replace) [`NOTES.md`](./NOTES.md) and the existing `docs/` snapshots in this folder.
> Owner of follow-up: Insights Engine design author.

---

## Status as of 2026-05

Azure AI Search is GA on all the capabilities the Insights Engine needs: vector + hybrid + semantic-ranking retrieval, integrated vectorization (skillset-driven embedding), per-document filter pushdown for ACL trimming, and built-in indexers for Azure Blob, Cosmos DB NoSQL, ADLS Gen2, Table Storage, and OneLake. Semantic ranker remains a separately-billed add-on (free tier 1,000 req/mo) available on Basic/S1/S2/S3/L1/L2. The 2026 platform direction is toward *knowledge sources* (an indexed-knowledge abstraction that wraps a search index + vectorizer and can be parented to a Foundry Agent Service knowledge base) — useful to know about even though the Insights Engine deliberately bypasses Foundry hosting.

## Key URLs consulted (2026-05-19)

- [Vector Search Overview](https://learn.microsoft.com/en-us/azure/search/vector-search-overview) — last updated 2026-05-13
- [Integrated Vectorization Overview](https://learn.microsoft.com/en-us/azure/search/vector-search-integrated-vectorization) — last updated 2026-03-18
- [Semantic Search Overview](https://learn.microsoft.com/en-us/azure/search/semantic-search-overview) — already snapshotted in `docs/`
- [Hybrid Search Overview](https://learn.microsoft.com/en-us/azure/search/hybrid-search-overview)
- [Security filters to trim search results](https://learn.microsoft.com/en-us/previous-versions/azure/search/search-security-trimming-for-azure-search-with-aad) — moved to archive area but still authoritative for the OData filter pattern
- [Choose a Service Tier](https://learn.microsoft.com/en-us/azure/search/search-sku-tier)
- [Azure AI Search pricing](https://azure.microsoft.com/en-us/pricing/details/search/)
- [Service limits](https://learn.microsoft.com/en-us/azure/search/search-limits-quotas-capacity)

## Findings

### 1. Hybrid + vector + semantic ranking (current capabilities)

- **Hybrid retrieval** runs BM25 keyword + vector kNN in the same request; results are fused via Reciprocal Rank Fusion (RRF). Microsoft's benchmarks consistently show hybrid + semantic ranker as the strongest configuration on RAG-style relevance evaluations — better than vector-only, BM25-only, or hybrid-without-semantic.
- **Vector queries** accept either a raw vector (`Vector`) or a text string with the index's configured `vectorizer` doing query-time embedding (`VectorizableTextQuery`). The latter is the integrated-vectorization happy path.
- **Semantic ranker** is an L2 reranker layered on top of the L1 BM25+vector result set. It also produces `Caption` and `Answer` extracts. Pricing: 1,000 req/mo free, then $1/1,000 above (Standard semantic ranker pricing plan). Available on Basic, S1, S2, S3, L1, L2. **Not available on Free tier.**
- **Filter pushdown direction**: by default Azure AI Search applies filters *after* vector retrieval (postFilter), which means a restrictive filter can leave you with zero results even when the unfiltered top-k had relevant matches. Setting `vectorFilterMode=preFilter` makes the engine filter first then retrieve — slower but mandatory for high-cardinality ACLs (e.g., a user belongs to 50 groups). Recommend `preFilter` for the Insights Engine's ACL-based trimming.

### 2. Integrated vectorization (status, embedders, push vs. pull)

Status: **GA** in all regions and tiers. Free at the chunking layer (Text Split skill); embedding cost is billed by the embedding model provider (Azure OpenAI typically).

Supported embedders (skill → vectorizer pairs):

| Embedding skill | Vectorizer | Notes |
|---|---|---|
| `AzureOpenAIEmbedding` | Azure OpenAI vectorizer | text-embedding-ada-002 (1536 dims), text-embedding-3-small (1536), text-embedding-3-large (3072). **Recommended for Spaarke.** |
| Custom Web API skill | Custom Web API vectorizer | Any embedding endpoint; use for non-Azure models or air-gapped scenarios. |
| Azure Vision multimodal | Azure Vision vectorizer | CLIP-style image+text embedding. Multimodal only. |
| AML skill (Foundry model catalog) | Foundry model catalog vectorizer | Open-source embeddings deployed via Foundry. Useful if you need Qwen/BGE/etc. |

**When to use pull (integrated vectorization indexer) vs. push (BFF/Function writes documents directly via SDK):**

- **Pull (indexer + integrated vectorization)** — supported sources: Azure Blob, ADLS Gen2, Cosmos DB NoSQL, Azure Table Storage, OneLake, Azure SQL. Indexer handles chunking, embedding, retry, throttling. Best for bulk content where the source is one of the supported services.
- **Push** — write SDK code that chunks, embeds, and uploads. Required when:
  - The source isn't a supported indexer source (Dataverse is NOT, SPE is NOT).
  - You need custom chunking logic that doesn't fit Text Split skill (e.g., clause-aware splitting on legal documents driven by application-side semantics).
  - You need transactional control (write to SPE + AI Search atomically from the same code path).

**Spaarke recommendation for Insights Engine**: Use **push** from an Azure Function or the BFF for Dataverse-sourced content (no pull-mode Dataverse indexer exists in 2026). Use integrated vectorization's vectorizer for *query-time* text-to-vector even when push-mode at index time — that way the query side stays simple and you never ship raw embeddings from the agent.

### 3. Security trimming patterns for multi-tenant

Azure AI Search has **no native ACL trimming**. You implement trimming by:

1. Indexing per-document `groups` (and/or `oids`) string-array fields.
2. At query time, building an OData filter from the caller's claims:
   ```
   groups/any(g: search.in(g, 'group-id-1,group-id-2,...'))
   ```
3. Passing that filter on `SearchOptions.Filter` along with `vectorFilterMode=preFilter` if you have high-cardinality ACLs.

For **multi-tenant physical isolation** (the Insights Engine pattern):
- **One search service per tenant** is the cleanest answer. The Insights Engine's Bicep already does this.
- **Tenant-as-index within one service** is possible (one index per tenant on a shared service) and cheaper, but mixes blast radius and capacity planning. **Not recommended** for legal-tech where tenant isolation is contractual.
- **Tenant-as-filter on a shared index** is the third option (a `tenantId` field on every document, query-time filter). **Avoid** — too easy to leak across tenants if the filter is dropped.

Cross-tenant search isn't a requirement for the Insights Engine; physical isolation is the right call.

For an **internal tenant** with role-based ACLs (a single tenant's users have different access to different matters), the standard pattern applies: index `groups[]` per document, filter by the caller's group membership at query time. Group membership comes from Entra (`memberOf` claim, or via Microsoft Graph `getMemberGroups` if the token is shallow).

### 4. Index schema for artifact + metadata + embedding

Canonical shape for the Insights Engine's Observation/Insight index (Type column → `Observation` per the design doc):

```json
{
  "name": "insights-observations",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "matterId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "artifactType", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "playbookId", "type": "Edm.String", "filterable": true },
    { "name": "playbookVersion", "type": "Edm.String", "filterable": true },
    { "name": "title", "type": "Edm.String", "searchable": true },
    { "name": "content", "type": "Edm.String", "searchable": true, "analyzer": "en.microsoft" },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "dimensions": 1536, "vectorSearchProfile": "default-profile" },
    { "name": "confidence", "type": "Edm.Double", "filterable": true, "sortable": true },
    { "name": "evidenceRefs", "type": "Collection(Edm.String)", "filterable": false },
    { "name": "extractedAt", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },
    { "name": "milestone", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "groups", "type": "Collection(Edm.String)", "filterable": true },
    { "name": "tags", "type": "Collection(Edm.String)", "filterable": true, "facetable": true }
  ],
  "vectorSearch": { ... HNSW profile + Azure OpenAI vectorizer ... },
  "semantic": {
    "configurations": [{
      "name": "default-semantic",
      "prioritizedFields": {
        "titleField": { "fieldName": "title" },
        "prioritizedContentFields": [{ "fieldName": "content" }],
        "prioritizedKeywordsFields": [{ "fieldName": "tags" }]
      }
    }]
  }
}
```

Notes:
- `contentVector` at 1536 dims uses text-embedding-3-small. Move to 3072/text-embedding-3-large only if measured recall demands it; doubling dimensions doubles vector index storage.
- `evidenceRefs` stores pointers (URIs or IDs) into Dataverse / SPE / source URLs — never the raw evidence inline.
- `groups` is the ACL trimming field. For matters with shared-access models, populate this from the matter's access list at index time and rebuild on permission changes.
- `playbookId` + `playbookVersion` are critical for the Observation type — they tell the agent which playbook produced this Observation, and let you re-extract when a playbook ships v2.

### 5. Pricing / tier guidance for 1M–10M artifacts per tenant

| Tier | Storage/partition | Max partitions | Indexes | Approx. monthly cost (1 replica × 1 partition, East US 2) |
|---|---|---|---|---|
| Basic | 15 GB | 1 | 15 | ~$74 |
| S1 | 160 GB | 12 | 50 | ~$245 |
| S2 | 512 GB | 12 | 200 | ~$981 |
| S3 | 1 TB (or 2 TB with HD) | 12 | 200 (1000 HD) | ~$1,962 |
| L1 | 1 TB | 12 | 10 | Storage-optimized |
| L2 | 2 TB | 12 | 10 | Storage-optimized |

Sizing thumb-rules for legal-firm scale:

- **1M artifacts** with ~5KB metadata + 1536-dim float32 vector (~6KB) per chunk → ~12 GB raw, ~20 GB after index overhead. Fits comfortably on Basic.
- **10M artifacts** with same shape and one parent + 5–8 chunks per parent → ~30M docs × ~15KB → ~450 GB index. **S2 minimum (512 GB)** with 1 partition; recommend 2 replicas for HA.
- **Vector index quotas** are separately governed (vector-index size limit per partition). Newer service generations (post April 2024) have ~10× higher vector quotas. Provision new services, not upgrades.
- **Semantic ranker** at 1 req/s sustained = ~2.6M req/mo = ~$2,600/mo for the ranker alone. Cache aggressively.

Note: physical per-tenant means each tenant pays its own SU cost. Bin-pack small tenants on Basic and graduate to S1/S2 when their index outgrows 15 GB. Track this in the tenant catalog.

## Implications for Spaarke Insights Engine

1. **Push mode at ingest, integrated vectorizer at query time.** Dataverse isn't a supported indexer source; an Azure Function does chunk/embed/upload via the SDK. But configure an `AzureOpenAIVectorizer` on the index so the BFF/agent can issue `VectorizableTextQuery(text, fields)` without ever calling the embedding endpoint itself.
2. **`vectorFilterMode=preFilter` is mandatory** for the Insights Engine's ACL filtering. Default postFilter will drop relevant Observations when a user's accessible matter set is restrictive.
3. **Store `playbookId` + `playbookVersion` on every Observation.** This is non-obvious but load-bearing for the design's "re-extract when playbook ships v2" requirement.
4. **Two-index pattern**: a primary `insights-observations` index (one document per Observation with full content + vector) and optionally a secondary `insights-observation-snippets` index for fine-grained chunk retrieval. Microsoft calls these "index projections" — supported in integrated vectorization indexers, but you implement the projection yourself when push-mode.
5. **Cache semantic ranker results in Redis (ADR-009)** keyed on `(indexName, queryText, vectorHash, filterString, top, callerGroups)`. The caller groups must be part of the cache key or stale ACL hits can leak across users. TTL ~15 min for matter-context queries; invalidate on permission events.

## Open questions

- **Knowledge source vs. direct SDK from BFF.** The 2026 platform direction wraps an index in a knowledge source that can be parented to Foundry Agent Service. Insights Engine doesn't use Foundry-hosted agents, but should the BFF call the search index *through* a knowledge source (gain access to whatever the knowledge source layer adds — query rewriting, agentic retrieval) or directly via `SearchClient`? Probably direct, for control. But worth a 1-day spike when the design hits the prototype stage.
- **Per-tenant data residency.** Some Spaarke tenants will require EU or UK regional pinning. The Bicep template needs a region parameter and the deployment process needs validation that the AI Search SKU is available in that region. AI Search has good regional coverage; semantic ranker is somewhat narrower.
- **Cost of 1536 vs. 3072.** Worth a small A/B on a representative legal corpus to know whether the 2× storage and 2× embedding cost of text-embedding-3-large is worth the recall improvement on legal terminology. Recommend running this in Phase 1 to set the design baseline.
