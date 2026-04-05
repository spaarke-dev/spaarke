# RAG Architecture

> **Last Updated**: April 2026
> **Last Reviewed**: 2026-04-05
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: New
> **Purpose**: Describes the Retrieval-Augmented Generation (RAG) pipeline — text chunking, embedding generation and caching, indexing strategies, hybrid search, and multi-tenant index routing.

---

## Overview

The RAG subsystem provides document-grounded AI responses by indexing document content into Azure AI Search and retrieving relevant chunks at query time. The architecture uses a **dual-index strategy** (knowledge index at 512 tokens, discovery index at 1024 tokens), **hybrid search** combining keyword, vector, and semantic ranking, and a **Redis-based embedding cache** to reduce Azure OpenAI API costs.

The key design decision is **idempotent re-indexing** (ADR-004): re-indexing the same document always replaces previous chunks rather than accumulating duplicates. All index documents carry a `tenantId` field for query-time multi-tenant isolation (ADR-014).

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| IRagService | `src/server/api/Sprk.Bff.Api/Services/Ai/IRagService.cs` | Search, index, delete, and embedding generation contracts |
| RagService | `src/server/api/Sprk.Bff.Api/Services/Ai/RagService.cs` | Hybrid search implementation: keyword + vector + semantic ranking via Azure AI Search |
| RagIndexingPipeline | `src/server/api/Sprk.Bff.Api/Services/Ai/RagIndexingPipeline.cs` | Orchestrates chunk -> embed -> index for parsed documents; dual-index writes |
| RagQueryBuilder | `src/server/api/Sprk.Bff.Api/Services/Ai/RagQueryBuilder.cs` | Builds metadata-aware queries from DocumentAnalysisResult (entities, key phrases, doc type) |
| AnalysisRagProcessor | `src/server/api/Sprk.Bff.Api/Services/Ai/AnalysisRagProcessor.cs` | RAG search, caching, and tenant resolution for the analysis pipeline |
| FileIndexingService | `src/server/api/Sprk.Bff.Api/Services/Ai/FileIndexingService.cs` | Unified file indexing: download -> extract text -> chunk -> index |
| EmbeddingCache | `src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs` | Redis-based embedding cache with SHA256 content hashing, 7-day TTL |
| IEmbeddingCache | `src/server/api/Sprk.Bff.Api/Services/Ai/IEmbeddingCache.cs` | Embedding cache contract |
| ScheduledRagIndexingService | `src/server/api/Sprk.Bff.Api/Services/Jobs/ScheduledRagIndexingService.cs` | BackgroundService: periodic catch-up indexing for unindexed documents |
| RagIndexingJobHandler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/RagIndexingJobHandler.cs` | Service Bus job handler for single-document RAG indexing |
| BulkRagIndexingJobHandler | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/BulkRagIndexingJobHandler.cs` | Service Bus job handler for batch RAG indexing |
| RagTelemetry | `src/server/api/Sprk.Bff.Api/Telemetry/RagTelemetry.cs` | OpenTelemetry-compatible metrics for RAG operations |
| PlaybookEmbeddingService | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookEmbeddingService.cs` | Vector search for playbook dispatch (separate index: `playbook-embeddings`) |
| PlaybookIndexingService | `src/server/api/Sprk.Bff.Api/Services/Ai/PlaybookEmbedding/PlaybookIndexingService.cs` | Indexes playbook metadata into the playbook-embeddings index |
| DocumentSearchTools | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/DocumentSearchTools.cs` | AI function tool: entity-scoped document search for SprkChat |
| KnowledgeRetrievalTools | `src/server/api/Sprk.Bff.Api/Services/Ai/Chat/Tools/KnowledgeRetrievalTools.cs` | AI function tool: knowledge-source-scoped retrieval for SprkChat |

## Data Flow

### Indexing Pipeline (Document -> AI Search)

1. **Trigger**: Document upload (via FileIndexingService), analysis completion (via RagIndexingJobHandler), or scheduled catch-up (via ScheduledRagIndexingService)
2. **Text extraction**: `ITextExtractor` extracts text from the source file (PDF, DOCX, etc.)
3. **Dual chunking**: `ITextChunkingService` chunks text at two granularities:
   - Knowledge index: 512 tokens (2048 chars), 50-token overlap (200 chars), sentence-boundary-preserving
   - Discovery index: 1024 tokens (4096 chars), 100-token overlap (400 chars), sentence-boundary-preserving
4. **Stale chunk deletion**: Existing chunks for the document are deleted from both indexes (ADR-004 idempotency)
5. **Embedding generation**: All chunks are embedded in parallel batches (max 16 concurrent) via Azure OpenAI `text-embedding-3-large` (3072 dimensions)
6. **Index upload**: Chunks uploaded to both knowledge and discovery indexes in parallel
7. **Metadata stamping**: Each indexed document carries `tenantId`, `knowledgeSourceId`, `fileName`, `documentType`, `tags`, and `parentEntityType`/`parentEntityId`

### Search Pipeline (Query -> Results)

1. **Query construction**: `RagQueryBuilder` builds composite search text from analysis metadata (entities, key phrases, document type, summary prefix) with tenant-scoped OData filter
2. **Embedding generation**: Query text is embedded via Azure OpenAI; checked against `EmbeddingCache` first (SHA256 content hash key)
3. **Hybrid search execution**: `RagService` sends to Azure AI Search combining:
   - Full-text keyword search on `content`, `fileName`, `knowledgeSourceName` fields
   - Vector search on `contentVector3072` field (cosine similarity, 3072 dimensions)
4. **Semantic ranking**: Results re-ranked using `knowledge-semantic-config` semantic configuration
5. **Filtering**: OData filters applied for tenantId (always), knowledgeSourceIds (inclusion/exclusion), tags (required/optional/excluded), parentEntityType/parentEntityId, documentType
6. **Score threshold**: Results below `MinScore` (default 0.7) are filtered out
7. **Return**: Ranked results with content, highlights, chunk metadata, and telemetry

### Scheduled Catch-Up Indexing

1. **ScheduledRagIndexingService** runs on a configurable interval (default: 60 minutes)
2. Queries Dataverse for documents with `sprk_hasfile=true` and `sprk_ragindexedon=null`
3. Submits BulkRagIndexing jobs to the `sdap-jobs` Service Bus queue
4. Max 100 documents per run, max 5 concurrent processing

## Integration Contracts

| Contract | Value | Notes |
|----------|-------|-------|
| Embedding model | `text-embedding-3-large` | Azure OpenAI deployment |
| Embedding dimensions | 3072 | Vector field: `contentVector3072` |
| Knowledge chunk size | 512 tokens (~2048 chars) | 50-token overlap |
| Discovery chunk size | 1024 tokens (~4096 chars) | 100-token overlap |
| Semantic config name | `knowledge-semantic-config` | Must match AI Search index definition |
| Search fields | `content`, `fileName`, `knowledgeSourceName` | Keyword search targets |
| Embedding cache key | `sdap:embedding:{base64-sha256-hash}` | Redis, 7-day TTL |
| RAG result cache TTL | 15 minutes | `AnalysisRagProcessor` cache |
| Default TopK | 5 | Max: 20 |
| Default MinScore | 0.7 | Score threshold for result filtering |
| Max concurrent embeddings | 16 | Pipeline-level concurrency limit |
| Max concurrent RAG searches | 5 | `AnalysisRagProcessor` semaphore (ADR-013) |
| Indexing performance target | < 60,000 ms per document | NFR-11 |
| Search P95 latency target | < 500 ms | Embedding ~50ms cached, ~150ms uncached; search ~100-300ms |

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Azure OpenAI | `IOpenAiClient` | Embedding generation (text-embedding-3-large) |
| Depends on | Azure AI Search | `SearchClient`, `SearchIndexClient` | Hybrid search and index management |
| Depends on | Redis | `IDistributedCache` | Embedding cache, RAG result cache |
| Depends on | SPE (SharePoint Embedded) | `ISpeFileOperations` | File download for indexing |
| Depends on | Text extraction | `ITextExtractor` | PDF/DOCX/etc. text extraction |
| Depends on | Knowledge deployment | `IKnowledgeDeploymentService` | Multi-tenant index routing |
| Consumed by | Chat system | `DocumentSearchTools`, `KnowledgeRetrievalTools` | Entity-scoped search during chat |
| Consumed by | Analysis pipeline | `AnalysisRagProcessor` | Reference retrieval during analysis |
| Consumed by | Playbook dispatch | `PlaybookEmbeddingService` | Playbook matching via separate index |

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Dual-index strategy | Knowledge (512-token) + Discovery (1024-token) | Fine-grained retrieval for chat; broader context for discovery | -- |
| Idempotent re-indexing | Delete-then-upload per documentId | Same document always produces consistent index state | ADR-004 |
| SHA256 content hashing for cache | Base64-encoded SHA256 | Consistent key length, safe for any content, deterministic | ADR-009 |
| Hybrid search (keyword + vector + semantic) | Three-way combination | Best relevance: keyword for exact matches, vector for semantic similarity, semantic ranking for re-ordering | -- |
| Tenant-scoped index documents | tenantId field on every document | Query-time multi-tenant isolation without separate indexes per tenant | ADR-014 |
| Embedding cache not used during indexing | Direct-to-index, no Redis for chunk embeddings | Indexing is write-once; caching benefits queries not writes | ADR-009 |
| Bounded concurrency for AI calls | SemaphoreSlim limits | Prevents rate-limit errors and Azure AI Search overload | ADR-013 |

## Constraints

- **MUST**: Every AI Search document carries `tenantId` for query-time isolation (ADR-014)
- **MUST**: Re-indexing deletes stale chunks before uploading new ones (ADR-004)
- **MUST**: Concurrency for embedding generation is bounded to 16 (pipeline) and 5 (search) to avoid rate limits
- **MUST**: Indexing is triggered via Service Bus jobs, not inline in API requests (ADR-001)
- **MUST NOT**: Cache chunk embeddings in Redis during indexing -- index directly to AI Search (ADR-009)
- **MUST NOT**: Use unbounded `Task.WhenAll` on throttled AI services (ADR-013)

## Known Pitfalls

- **Embedding model version changes**: The embedding cache has a 7-day TTL. If the Azure OpenAI embedding model is updated, cached embeddings from the old model version will be stale until they expire. A cache flush may be needed after model changes.
- **Vector backfill**: Documents indexed before vector search was enabled will not have `contentVector3072` populated. The ScheduledRagIndexingService acts as a catch-up mechanism, but documents must be explicitly re-indexed to gain vector search capability.
- **File type support**: `RagService` supports 14 file types (pdf, docx, doc, xlsx, xls, pptx, ppt, msg, eml, txt, html, htm, rtf, csv). Unsupported file types will fail text extraction silently.
- **Concurrency limit contention**: The static `SemaphoreSlim` in `AnalysisRagProcessor` (max 5) is shared across all instances in the process. Under high load, search requests may queue behind the semaphore.

## Related

- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) -- Four-tier AI framework overview
- [sdap-document-processing-architecture.md](sdap-document-processing-architecture.md) -- Document processing and summarization pipeline
