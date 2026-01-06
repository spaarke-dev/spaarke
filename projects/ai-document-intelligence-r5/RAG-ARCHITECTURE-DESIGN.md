# AI Document Intelligence R5: RAG Architecture Design

> **Status**: Working Document
> **Created**: January 2026
> **Author**: AI Architecture Team
> **Prerequisites**: [RAG-ARCHITECTURE.md](../../docs/guides/RAG-ARCHITECTURE.md), [RAG-CONFIGURATION.md](../../docs/guides/RAG-CONFIGURATION.md)

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [Existing Documentation Reference](#existing-documentation-reference)
3. [What is RAG?](#what-is-rag)
4. [Two-Index Strategy](#two-index-strategy)
5. [Query Strategy Analysis](#query-strategy-analysis)
6. [Embedding Models Deep Dive](#embedding-models-deep-dive)
7. [Existing Resources Assessment](#existing-resources-assessment)
8. [Gap Analysis: What's Missing](#gap-analysis-whats-missing)
9. [Enhanced Pipeline Design](#enhanced-pipeline-design)
10. [Quality Management](#quality-management)
11. [Implementation Roadmap](#implementation-roadmap)
12. [Future Enhancements: Alternative Embedding Models](#future-enhancements-alternative-embedding-models)
13. [Advanced Architecture: Embedding Model as Knowledge Scope](#advanced-architecture-embedding-model-as-knowledge-scope)

---

## Executive Summary

This document defines the enhanced RAG (Retrieval-Augmented Generation) architecture for Spaarke's AI Document Intelligence platform. Based on analysis of existing capabilities and requirements, we recommend:

1. **Two-Index Strategy**: Knowledge Base (curated) + Document Discovery (all documents)
2. **Query Strategy Improvement**: Use analysis metadata instead of first 500 characters
3. **Automated Pipeline**: Connect analysis completion → RAG indexing

Key findings:
- Existing RAG infrastructure is production-ready (R3 complete)
- Analysis pipeline already extracts rich metadata (Summary, Keywords, DocumentType, Entities)
- Missing: automated document-to-index pipeline and intelligent query construction
- SPE FileUpload components can be enhanced to feed both indexes

---

## Existing Documentation Reference

**R3 produced comprehensive RAG documentation. Review these before proceeding:**

| Document | Content | Location |
|----------|---------|----------|
| **RAG-ARCHITECTURE.md** | Components, deployment models, hybrid search pipeline, index schema, caching, security | [docs/guides/RAG-ARCHITECTURE.md](../../docs/guides/RAG-ARCHITECTURE.md) |
| **RAG-CONFIGURATION.md** | App settings, index config, search options, environment variables, code examples | [docs/guides/RAG-CONFIGURATION.md](../../docs/guides/RAG-CONFIGURATION.md) |
| **RAG-TROUBLESHOOTING.md** | Common issues, diagnostics, monitoring, Application Insights queries | [docs/guides/RAG-TROUBLESHOOTING.md](../../docs/guides/RAG-TROUBLESHOOTING.md) |

**This R5 document focuses on:**
- What's **missing** from R3 (gaps)
- What needs to be **built** (new components)
- **Improvements** to existing functionality (query strategy)

---

## What is RAG?

### Definition

**RAG (Retrieval-Augmented Generation)** enhances AI responses by retrieving relevant information from a knowledge base before generating answers. Instead of relying solely on model training data, RAG "looks up" relevant context first.

### How RAG Works

```
WITHOUT RAG:
User Question → AI Model → Answer (from training only)
                              ↓
                      May hallucinate, outdated, generic

WITH RAG:
User Question → Search Knowledge Base → Retrieve Relevant Docs →
AI Model (with docs as context) → Answer (grounded in your data)
                                      ↓
                              Accurate, current, specific
```

### RAG in Spaarke Context

| Use Case | How RAG Helps |
|----------|---------------|
| **Contract Analysis** | Compares against your standard terms, policies |
| **Risk Detection** | References your risk categories and thresholds |
| **Compliance Review** | Checks against regulatory requirements in KB |
| **Document Discovery** | Finds semantically similar documents |

---

## Two-Index Strategy

### Why Two Indexes?

| Single Index Problem | Two Index Solution |
|---------------------|-------------------|
| All docs compete for relevance | Purpose-specific ranking |
| Noise drowns signal | Curated knowledge stays clean |
| Old docs pollute results | Discovery index accepts all |
| Quality inconsistent | Different quality bars per index |

### Index 1: Knowledge Base (for AI Analysis)

```
┌─────────────────────────────────────────────────────────────────────┐
│  KNOWLEDGE BASE INDEX                                                │
│  Purpose: Provide reference context during playbook-based analysis  │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Characteristics:                                                    │
│  ├── Size: Small (10-100 documents per tenant)                      │
│  ├── Curation: High (human-reviewed, approved content)              │
│  ├── Update frequency: Low (when standards/policies change)         │
│  ├── Source: Manually selected authoritative documents              │
│  └── Quality bar: Authoritative, current, accurate                  │
│                                                                      │
│  Content Types:                                                      │
│  ├── Standard Contract Terms (company-approved language)            │
│  ├── Company Policies (HR, legal, financial)                        │
│  ├── Regulatory Guidelines (compliance requirements)                │
│  ├── Template Documents (approved templates)                        │
│  └── Best Practice Guides (internal procedures)                     │
│                                                                      │
│  Used By:                                                            │
│  ├── Playbooks with KnowledgeType.RagIndex scopes                   │
│  ├── "Compare to standard terms" analysis                           │
│  └── Risk assessment with policy context                            │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Index 2: Document Discovery (for Semantic Search)

```
┌─────────────────────────────────────────────────────────────────────┐
│  DOCUMENT DISCOVERY INDEX                                            │
│  Purpose: Enable "Find Similar Documents" and cross-document search │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Characteristics:                                                    │
│  ├── Size: Large (1,000-100,000+ documents per tenant)              │
│  ├── Curation: Low (automated indexing on upload)                   │
│  ├── Update frequency: High (every document upload)                 │
│  ├── Source: All documents from SPE storage                         │
│  └── Quality bar: Indexed, not necessarily authoritative            │
│                                                                      │
│  Content Types:                                                      │
│  ├── All contracts (current and historical)                         │
│  ├── All correspondence (emails, letters)                           │
│  ├── All invoices and financial docs                                │
│  └── All other user documents                                       │
│                                                                      │
│  Used By:                                                            │
│  ├── "Find documents similar to this one" feature                   │
│  ├── Cross-document search ("all contracts mentioning ACME")        │
│  └── Document clustering and categorization                         │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

### Comparison

| Aspect | Knowledge Base | Document Discovery |
|--------|---------------|-------------------|
| **Purpose** | Context for AI analysis | Find similar documents |
| **Size** | 10-100 docs | 1,000-100,000+ docs |
| **Curation** | Human-reviewed | Automated |
| **Update** | Manual | On every upload |
| **Quality** | Must be authoritative | Just needs to be indexed |
| **Chunking** | Full chunking | Summary + key chunks |
| **User action** | Admin adds to knowledge base | Automatic on upload |

---

## Query Strategy Analysis

### Current Problem: First 500 Characters

The current implementation uses the **first 500 characters** of a document as the RAG query:

```csharp
// Current code in AnalysisOrchestrationService.cs (lines 674-675)
// Build search query from document text (use first 500 chars for query efficiency)
var searchQuery = documentText.Length > 500 ? documentText[..500] : documentText;
```

**This is problematic:**

| Document Type | First 500 Chars Contains | Misses |
|---------------|-------------------------|--------|
| **Contract** | "MASTER SERVICES AGREEMENT between ACME Corp and XYZ Inc dated..." | Payment terms, liability, termination |
| **Email** | Headers, greeting, first sentence of body | The actual request/content |
| **Report** | Title page, table of contents | Analysis, conclusions |
| **Policy** | Document title, effective date, preamble | Actual policy content |

The first 500 characters are often **boilerplate** - the least useful part of a document.

### Why 500 Characters Was Chosen

The comment says "for query efficiency" - likely because:
1. **Embedding cost**: Longer text = more tokens = more API cost
2. **Query length limits**: Some search systems have query length limits
3. **Relevance assumption**: First part often contains key identifiers

### Cost Analysis: Is Longer Query Expensive?

**No. The cost is negligible:**

```
Azure OpenAI text-embedding-3-small pricing:
$0.00002 per 1K tokens

Example costs per query:
┌────────────────────────────────┬─────────┬──────────────┐
│ Query Type                     │ Tokens  │ Cost         │
├────────────────────────────────┼─────────┼──────────────┤
│ 500 characters (current)       │ ~125    │ $0.0000025   │
│ Summary only (~200 words)      │ ~250    │ $0.000005    │
│ Summary + entities             │ ~400    │ $0.000008    │
│ Full document (10 pages)       │ ~4000   │ $0.00008     │
└────────────────────────────────┴─────────┴──────────────┘

At 10,000 analyses/month:
• Current (500 chars): $0.025/month
• Summary + entities:  $0.08/month

Cost is NOT a real constraint.
```

### Latency Analysis

```
Azure AI Search latency breakdown:
┌────────────────────────────────┬──────────────────────┐
│ Operation                      │ Typical Latency      │
├────────────────────────────────┼──────────────────────┤
│ Embedding generation (cached)  │ ~50ms                │
│ Embedding generation (new)     │ ~150ms               │
│ Keyword search only            │ ~50-100ms            │
│ Vector search only             │ ~100-200ms           │
│ Hybrid (keyword + vector)      │ ~150-250ms           │
│ Hybrid + semantic ranking      │ ~200-400ms           │
└────────────────────────────────┴──────────────────────┘

Total RAG lookup: 250-600ms per knowledge source
(Acceptable - analysis itself takes 5-30 seconds)
```

### Recommended Query Strategy

**Use analysis output instead of raw document text:**

```csharp
private string BuildRagSearchQuery(DocumentAnalysisResult analysis, string documentText)
{
    var components = new List<string>();

    // Priority 1: AI-generated summary (best semantic representation)
    if (!string.IsNullOrEmpty(analysis.Summary))
    {
        components.Add(analysis.Summary);
    }

    // Priority 2: Document type (category matching)
    if (!string.IsNullOrEmpty(analysis.Entities.DocumentType))
    {
        components.Add(analysis.Entities.DocumentType);
    }

    // Priority 3: Key entities (specific matching)
    var entities = analysis.Entities.Organizations
        .Concat(analysis.Entities.References)
        .Take(5);
    if (entities.Any())
    {
        components.Add(string.Join(" ", entities));
    }

    // Priority 4: Keywords (topic matching)
    if (!string.IsNullOrEmpty(analysis.Keywords))
    {
        var keywords = analysis.Keywords.Split(',').Take(5);
        components.Add(string.Join(" ", keywords));
    }

    // Fallback: Intelligent sampling (not just first 500)
    if (!components.Any())
    {
        return SampleDocument(documentText, sampleCount: 3, charsPerSample: 300);
    }

    return string.Join(" ", components);
    // Result: ~300-500 tokens, semantically rich
}
```

### Query Strategy by Use Case

| Use Case | Query Construction | Search Config |
|----------|-------------------|---------------|
| **AI Analysis (RAG Context)** | Summary + DocumentType + Keywords + Entities | Hybrid (vector + keyword + semantic) |
| **Find Similar Documents** | Summary only (or full doc if short) | Vector-primary (semantic similarity) |
| **Search by Content** | User's search terms | Keyword-primary |

### Search Configuration by Use Case

**AI Analysis:**
```csharp
var searchOptions = new RagSearchOptions
{
    UseVectorSearch = true,      // Semantic matching
    UseKeywordSearch = true,     // Exact term matching
    UseSemanticRanking = true,   // AI re-ranking
    TopK = 5,                    // Limit results
    MinScore = 0.7f              // Quality threshold
};
```

**Document Discovery:**
```csharp
var searchOptions = new RagSearchOptions
{
    UseVectorSearch = true,      // Primary: semantic similarity
    UseKeywordSearch = false,    // Skip: we want similar meaning
    UseSemanticRanking = true,   // Refine ranking
    TopK = 20,                   // More results for discovery
    MinScore = 0.5f              // Lower threshold
};
```

---

## Embedding Models Deep Dive

### What is an Embedding?

An **embedding** converts text into a numerical representation (vector) that captures semantic meaning. This enables computers to understand *meaning*, not just match keywords.

```
Traditional Search (keyword matching):
"contract termination clause" → finds documents containing those exact words

Embedding/Vector Search (semantic matching):
"contract termination clause" → finds documents about:
  - "agreement cancellation provisions"
  - "how to end the deal"
  - "exit terms and conditions"

Different words, SAME meaning → found via vector similarity
```

### Embedding Model vs LLM

| Aspect | Embedding Model | LLM (Large Language Model) |
|--------|-----------------|---------------------------|
| **Purpose** | Convert text → numbers (vectors) | Generate text from prompts |
| **Output** | Fixed-size array of floats `[0.02, -0.08, ...]` | Variable-length text |
| **Example** | `text-embedding-3-small` | `gpt-4o`, `gpt-4o-mini` |
| **Use case** | Search, similarity, clustering | Chat, analysis, summarization |
| **Deterministic** | Yes - same input = same output | No - varies with temperature |
| **Cost** | Very cheap ($0.00002/1K tokens) | More expensive ($0.01+/1K tokens) |

### How Embeddings Work

Azure OpenAI's `text-embedding-3-small` converts text into a **1536-dimensional vector**:

```
Input: "The contract expires on December 31, 2025"
                    ↓
        Azure OpenAI Embedding Model
                    ↓
Output: [0.0234, -0.0891, 0.0456, ..., 0.0123]
        (1536 floating-point numbers)
```

Each dimension represents a **learned concept** from training on billions of text examples. Similar meanings produce similar vectors, measured by **cosine similarity** (0.0 = unrelated, 1.0 = identical meaning).

### Current Embedding Configuration

| Setting | Value | Location |
|---------|-------|----------|
| Model | `text-embedding-3-small` | Azure OpenAI deployment |
| Dimensions | 1536 | Fixed by model |
| Cache | Redis, 7-day TTL | `EmbeddingCache.cs` |
| Cache key | SHA256 hash of text | Consistent, content-addressable |

### Why Embeddings Are Cached (7-Day TTL)

From [EmbeddingCache.cs](../../src/server/api/Sprk.Bff.Api/Services/Ai/EmbeddingCache.cs):

```csharp
// Cache TTL: 7 days - embeddings are deterministic for same content + model
// Balance between freshness (model updates) and cost savings
private static readonly TimeSpan DefaultTtl = TimeSpan.FromDays(7);
```

**Why cache?** Same text + same model = same vector. Avoid repeated API calls.

| Scenario | Without Cache | With Cache |
|----------|--------------|------------|
| First query | 150ms, API call | 150ms, API call |
| Repeat query | 150ms, API call | **5-10ms**, Redis lookup |

### Why Embeddings Can Become Stale

Azure periodically updates embedding models. When this happens:

```
BEFORE UPDATE (Model v1):
"contract termination" → [0.0234, -0.0891, 0.0456, ...]

AFTER UPDATE (Model v2):
"contract termination" → [0.0198, -0.0923, 0.0512, ...]
                          ↑ Different numbers
```

**The problem**: Cached embeddings (v1) compared to index embeddings (v2) produce incorrect similarity scores. The 7-day TTL ensures eventual consistency.

| Model Change Type | Frequency | Impact |
|-------------------|-----------|--------|
| Minor updates | Rare | Small vector shifts |
| Major version changes | Announced | Requires re-indexing |
| Bug fixes | Occasional | Specific concepts shift |

**Practical impact during mismatch**: Slightly off similarity scores (0.85 instead of 0.88), results still generally correct, not catastrophic.

### Can We Improve Embedding Accuracy?

| Approach | Availability | Impact |
|----------|--------------|--------|
| **Fine-tuning** | ❌ Not available for Azure OpenAI embeddings | Would significantly improve domain accuracy |
| **Query enrichment** | ✅ Available (R5 focus) | Use Summary + Entities instead of raw text |
| **Hybrid search** | ✅ Already implemented | Combines vector + keyword for better results |
| **Better model** | ✅ `text-embedding-3-large` | 6x cost, ~10-15% better accuracy |
| **Specialized model** | ✅ Via AI Foundry | Legal-specific models available |

---

## Existing Resources Assessment

### What Already Exists (R3 Complete)

#### 1. RAG Service Infrastructure

| Component | Location | Status | Purpose |
|-----------|----------|--------|---------|
| `RagService` | `Services/Ai/RagService.cs` | **Exists** | Hybrid search + indexing |
| `RagEndpoints` | `Api/Ai/RagEndpoints.cs` | **Exists** | REST API for search/index |
| `KnowledgeDocument` | `Models/Ai/KnowledgeDocument.cs` | **Exists** | Index document schema |
| `KnowledgeDeploymentService` | `Services/Ai/KnowledgeDeploymentService.cs` | **Exists** | Multi-tenant routing |
| `EmbeddingCache` | `Services/Ai/EmbeddingCache.cs` | **Exists** | Redis embedding cache |
| `ResilientSearchClient` | `Infrastructure/Resilience/` | **Exists** | Circuit breaker for AI Search |

**Assessment**: RAG search and indexing APIs are production-ready. Missing: automated ingestion pipeline.

#### 2. Document Analysis Pipeline

| Component | Location | Status | Output |
|-----------|----------|--------|--------|
| `DocumentIntelligenceService` | `Services/Ai/DocumentIntelligenceService.cs` | **Exists** | Analysis orchestration |
| `TextExtractorService` | `Services/Ai/TextExtractorService.cs` | **Exists** | Text from files |
| `DocumentAnalysisResult` | `Models/Ai/DocumentAnalysisResult.cs` | **Exists** | Structured result |
| `ExtractedEntities` | `Models/Ai/ExtractedEntities.cs` | **Exists** | Rich metadata |

**DocumentAnalysisResult Already Produces:**
```csharp
public class DocumentAnalysisResult
{
    public string Summary { get; set; }           // → RAG query + summary chunk
    public string[] TlDr { get; set; }            // → Key points
    public string Keywords { get; set; }          // → RAG tags
    public ExtractedEntities Entities { get; set; }
}

public class ExtractedEntities
{
    public string[] Organizations { get; set; }   // → metadata.organizations
    public string[] People { get; set; }          // → metadata.people
    public string[] Amounts { get; set; }         // → metadata.amounts
    public string[] Dates { get; set; }           // → metadata.dates
    public string DocumentType { get; set; }      // → RAG documentType
    public string[] References { get; set; }      // → metadata.references
}
```

**Assessment**: Analysis already extracts all metadata needed for RAG indexing. Just needs connection to RAG pipeline.

#### 3. Background Job Infrastructure

| Component | Location | Status | Purpose |
|-----------|----------|--------|---------|
| `DocumentEventHandler` | `Services/Jobs/Handlers/DocumentEventHandler.cs` | **Exists** | Document lifecycle events |
| `DocumentProcessingJobHandler` | `Services/Jobs/Handlers/DocumentProcessingJobHandler.cs` | **Placeholder** | Document processing jobs |
| Service Bus integration | `Services/Jobs/` | **Exists** | Async job processing |

**Assessment**: Job infrastructure exists but RAG indexing not wired in.

#### 4. Playbook & Scope System (R4)

| Component | Location | Status | Purpose |
|-----------|----------|--------|---------|
| `ScopeResolverService` | `Services/Ai/ScopeResolverService.cs` | **Exists** | Resolve playbook scopes |
| `AnalysisOrchestrationService` | `Services/Ai/AnalysisOrchestrationService.cs` | **Exists** | Playbook execution |
| `ProcessRagKnowledgeAsync` | Line 645 | **Exists** | RAG knowledge processing |
| Knowledge entity | `sprk_analysisknowledge` | **Exists** | Knowledge source definitions |

**Assessment**: Playbook system can consume RAG knowledge. Query construction needs improvement.

### Component Interaction Map

```
┌─────────────────────────────────────────────────────────────────────┐
│                    EXISTING COMPONENTS                               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  FILE UPLOAD FLOW (exists):                                         │
│  SPE Upload → SpeFileStore → Graph API → Document stored           │
│         │                                                            │
│         ▼                                                            │
│  ┌─────────────────────────────────────────────────────┐            │
│  │ DocumentIntelligenceService (exists)                 │            │
│  │ ├── TextExtractorService.ExtractAsync()             │            │
│  │ ├── OpenAiClient.StreamCompletionAsync()            │            │
│  │ └── Returns: DocumentAnalysisResult                 │            │
│  │     ├── Summary, TlDr, Keywords                     │            │
│  │     └── Entities (DocumentType, Organizations...)   │            │
│  └─────────────────────────────────────────────────────┘            │
│         │                                                            │
│         ▼                                                            │
│  ┌─────────────────────────────────────────────────────┐            │
│  │ Saved to Dataverse (exists)                          │            │
│  │ sprk_analysis record with results                    │            │
│  └─────────────────────────────────────────────────────┘            │
│         │                                                            │
│         ▼                                                            │
│  ┌─────────────────────────────────────────────────────┐            │
│  │ RAG INDEXING ← ← ← ← ← ← MISSING CONNECTION         │            │
│  │ RagService.IndexDocumentAsync() (exists but unused) │            │
│  └─────────────────────────────────────────────────────┘            │
│                                                                      │
│  PLAYBOOK EXECUTION FLOW (exists):                                  │
│  ┌─────────────────────────────────────────────────────┐            │
│  │ AnalysisOrchestrationService                         │            │
│  │ ├── ResolvePlaybookScopesAsync()                    │            │
│  │ ├── ProcessRagKnowledgeAsync() ← QUERY NEEDS FIX    │            │
│  │ └── StreamCompletionAsync()                         │            │
│  └─────────────────────────────────────────────────────┘            │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Gap Analysis: What's Missing

### Critical Gaps

| Gap | Priority | Description | Impact |
|-----|----------|-------------|--------|
| **Query Strategy** | P0 | Using first 500 chars instead of analysis metadata | Poor RAG relevance |
| **Chunking Service** | P0 | No automatic document chunking | Can't index long docs |
| **Indexing Pipeline** | P0 | No connection from analysis → RAG | Manual indexing only |
| **Knowledge Base UI** | P1 | No way for admins to manage knowledge sources | Can't curate KB |
| **Discovery Index Trigger** | P1 | No automatic indexing on upload | No document discovery |
| **Find Similar Feature** | P2 | No "find similar documents" UI | Missing feature |

### Detailed Gap Analysis

#### Gap 1: Query Strategy (P0)

**Current State**: First 500 characters used as query.

**Required**: Use analysis output (Summary, Keywords, Entities, DocumentType).

**Fix Location**: `AnalysisOrchestrationService.ProcessRagKnowledgeAsync()` (line 674)

**Implementation**: See [Query Strategy Analysis](#query-strategy-analysis) section above.

#### Gap 2: Chunking Service (P0)

**Current State**: Client must manually chunk documents before indexing.

**Required**: Automatic chunking with:
- Configurable chunk size (default: 1500 tokens)
- Overlap between chunks (default: 200 tokens)
- Semantic boundary detection (split at paragraphs/sections)
- Metadata preservation across chunks

**Proposed Component**:
```csharp
public interface IDocumentChunker
{
    IAsyncEnumerable<DocumentChunk> ChunkAsync(
        string content,
        ChunkingOptions options,
        CancellationToken ct = default);
}

public record ChunkingOptions
{
    public int MaxChunkTokens { get; init; } = 1500;
    public int OverlapTokens { get; init; } = 200;
    public ChunkingStrategy Strategy { get; init; } = ChunkingStrategy.Semantic;
}
```

#### Gap 3: Indexing Pipeline (P0)

**Current State**: Analysis completes → results saved to Dataverse → stops there.

**Required**: After analysis:
1. Check if document should be indexed (based on container/settings)
2. Chunk the extracted text
3. Map analysis metadata to RAG fields
4. Index to appropriate index(es)

**Proposed Flow**:
```
DocumentAnalysisResult
        ↓
RagIndexingPipeline (NEW)
        ├── Should index? (check container settings)
        ├── Chunk text (IDocumentChunker)
        ├── Map metadata (from ExtractedEntities)
        └── Index (RagService.IndexBatchAsync)
              ├── Knowledge Base Index (if admin-selected)
              └── Discovery Index (if auto-index enabled)
```

#### Gap 4: Knowledge Base Management UI (P1)

**Current State**: No UI for admins to manage knowledge content.

**Required**:
- PCF component or model-driven app views
- "Add to Knowledge Base" action on documents
- Knowledge source management CRUD
- Index health dashboard

#### Gap 5: Discovery Index Trigger (P1)

**Current State**: No automatic indexing when documents are uploaded.

**Required**:
- Container-level setting: "Auto-index to Discovery"
- Hook into document upload flow
- Background job for async indexing
- Re-index on document update/delete

#### Gap 6: Find Similar Feature (P2)

**Current State**: No UI for semantic document search.

**Required**:
- "Find Similar Documents" button on document viewer
- Search results UI with relevance scores
- Filter by document type, date range, etc.

---

## Enhanced Pipeline Design

### Leveraging Existing Analysis Pipeline

The existing document analysis flow already extracts everything we need:

```
CURRENT FLOW (stops at step 6):

1. User uploads file via PCF control
2. File stored in SPE
3. Analysis triggered (user clicks "Analyze")
4. Text extracted (TextExtractorService)
5. AI analyzes and extracts metadata
6. Results saved to Dataverse (sprk_analysis record)

ENHANCED FLOW (continues to step 7):

7. NEW: RAG Indexing Pipeline
   └── RagIndexingService.IndexFromAnalysisAsync()
       ├── Input: DocumentAnalysisResult + extracted text
       ├── Chunk text (IDocumentChunker)
       ├── Build smart query (from analysis metadata)
       ├── Map metadata:
       │   ├── documentType = Entities.DocumentType
       │   ├── tags = ParseKeywords(Keywords)
       │   ├── metadata.organizations = Entities.Organizations
       │   └── etc.
       └── Index to:
           ├── Discovery Index (if auto-index enabled)
           └── Knowledge Base Index (if admin-flagged)
```

### Metadata Mapping

| Analysis Field | RAG Field | Transformation |
|----------------|-----------|----------------|
| `Entities.DocumentType` | `documentType` | Direct mapping |
| `Keywords` | `tags[]` | Split by comma, trim |
| `Summary` | Query + first chunk | Use for both |
| `Entities.Organizations` | `metadata.organizations` | JSON array |
| `Entities.People` | `metadata.people` | JSON array |
| `Entities.Dates` | `metadata.dates` | JSON array |
| `Entities.Amounts` | `metadata.amounts` | JSON array |
| `Entities.References` | `metadata.references` | JSON array |
| Extracted text | Chunk `content` | Via IDocumentChunker |

### Proposed New Components

```
┌─────────────────────────────────────────────────────────────────────┐
│                    NEW COMPONENTS TO BUILD                           │
├─────────────────────────────────────────────────────────────────────┤
│                                                                      │
│  Services/Ai/                                                        │
│  ├── IDocumentChunker.cs              (interface)                   │
│  ├── SemanticDocumentChunker.cs       (implementation)              │
│  ├── IRagIndexingPipeline.cs          (interface)                   │
│  ├── RagIndexingPipeline.cs           (orchestrates indexing)       │
│  └── RagQueryBuilder.cs               (smart query construction)    │
│                                                                      │
│  Services/Jobs/Handlers/                                             │
│  └── RagIndexingJobHandler.cs         (background indexing)         │
│                                                                      │
│  Api/Ai/                                                             │
│  ├── KnowledgeBaseEndpoints.cs        (manage knowledge sources)    │
│  └── DocumentDiscoveryEndpoints.cs    (find similar)                │
│                                                                      │
│  Configuration/                                                      │
│  └── RagIndexingOptions.cs            (chunking, auto-index config) │
│                                                                      │
│  Models/Ai/                                                          │
│  ├── DocumentChunk.cs                 (chunk model)                 │
│  └── ChunkingOptions.cs               (chunking configuration)      │
│                                                                      │
│  MODIFICATIONS TO EXISTING:                                          │
│  ├── AnalysisOrchestrationService.cs  (fix query construction)      │
│  └── DocumentEventHandler.cs          (wire in indexing trigger)    │
│                                                                      │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Quality Management

### Quality Metrics

| Metric | Target | Measurement |
|--------|--------|-------------|
| **Search relevance** | >0.7 avg semantic score | Track in RagService |
| **Result precision** | 80%+ useful results | User feedback |
| **Index freshness** | <24h for new docs | Indexing timestamp |
| **Query quality** | Uses analysis metadata | Code review |

### Quality Improvement Process

```
1. SEED PHASE (Initial Population)
   ├── Admin selects authoritative documents
   ├── Review and approve each document
   └── Assign to appropriate knowledge sources

2. MONITORING PHASE (Ongoing)
   ├── Track search queries with low relevance scores
   ├── Identify frequently asked but unanswered topics
   └── Monitor analysis quality feedback

3. CURATION PHASE (Periodic - Monthly)
   ├── Remove outdated documents from Knowledge Base
   ├── Add new authoritative sources
   └── Improve document tagging

4. VALIDATION PHASE (Quarterly)
   ├── Run test queries against RAG
   ├── Compare results to expected answers
   └── Document lessons learned
```

---

## Implementation Roadmap

### Phase 1: Query Strategy Fix (P0)

**Goal**: Improve RAG relevance by using analysis metadata for queries.

| Task | Description |
|------|-------------|
| 1.1 | Create `RagQueryBuilder` service |
| 1.2 | Update `ProcessRagKnowledgeAsync` to use builder |
| 1.3 | Add configuration for query strategy |
| 1.4 | Unit tests for query construction |
| 1.5 | Measure relevance improvement |

**Deliverable**: RAG queries use Summary + Entities instead of first 500 chars.

### Phase 2: Chunking & Indexing Pipeline (P0)

**Goal**: Enable automated document indexing after analysis.

| Task | Description |
|------|-------------|
| 2.1 | Create `IDocumentChunker` and `SemanticDocumentChunker` |
| 2.2 | Create `RagIndexingPipeline` service |
| 2.3 | Add `IndexFromAnalysisAsync()` method |
| 2.4 | Wire pipeline to analysis completion |
| 2.5 | Create `RagIndexingOptions` configuration |
| 2.6 | Unit tests for chunking and indexing |

**Deliverable**: After analysis, documents can be indexed to RAG automatically.

### Phase 3: Knowledge Base Management (P1)

**Goal**: Enable admins to manage curated knowledge content.

| Task | Description |
|------|-------------|
| 3.1 | Create `KnowledgeBaseEndpoints` |
| 3.2 | Add "Add to Knowledge Base" action |
| 3.3 | Create Knowledge Base management views in MDA |
| 3.4 | Implement knowledge source CRUD |
| 3.5 | Add index health monitoring |

**Deliverable**: Admins can add/remove documents from knowledge bases via UI.

### Phase 4: Automatic Discovery Indexing (P1)

**Goal**: Automatically index documents on upload for semantic search.

| Task | Description |
|------|-------------|
| 4.1 | Create `RagIndexingJobHandler` |
| 4.2 | Add container-level auto-index setting |
| 4.3 | Wire document upload to indexing job |
| 4.4 | Handle document updates (re-index) |
| 4.5 | Handle document deletes (remove from index) |

**Deliverable**: New documents automatically appear in Discovery Index.

### Phase 5: Find Similar Documents (P2)

**Goal**: Enable users to find semantically similar documents.

| Task | Description |
|------|-------------|
| 5.1 | Create `DocumentDiscoveryEndpoints` |
| 5.2 | Add "Find Similar" button to document viewer |
| 5.3 | Create search results UI component |
| 5.4 | Add filters (type, date, container) |
| 5.5 | Performance optimization for large indexes |

**Deliverable**: Users can find documents similar to current document.

---

## Appendix A: Files to Modify

| File | Modification |
|------|--------------|
| `AnalysisOrchestrationService.cs` | Replace 500-char query with RagQueryBuilder |
| `DocumentIntelligenceService.cs` | Call RagIndexingPipeline after analysis |
| `DocumentEventHandler.cs` | Trigger indexing job on document events |
| `Program.cs` | Register new services |

## Appendix B: Files to Create

| File | Purpose |
|------|---------|
| `Services/Ai/IDocumentChunker.cs` | Chunking interface |
| `Services/Ai/SemanticDocumentChunker.cs` | Chunking implementation |
| `Services/Ai/IRagIndexingPipeline.cs` | Pipeline interface |
| `Services/Ai/RagIndexingPipeline.cs` | Pipeline implementation |
| `Services/Ai/RagQueryBuilder.cs` | Smart query construction |
| `Services/Jobs/Handlers/RagIndexingJobHandler.cs` | Background indexing |
| `Api/Ai/KnowledgeBaseEndpoints.cs` | KB management API |
| `Api/Ai/DocumentDiscoveryEndpoints.cs` | Discovery API |
| `Configuration/RagIndexingOptions.cs` | Configuration |
| `Models/Ai/DocumentChunk.cs` | Chunk model |

---

## Future Enhancements: Alternative Embedding Models

### Azure AI Foundry Model Catalog

Spaarke already has [Azure AI Foundry infrastructure](../../infrastructure/ai-foundry/README.md) deployed. The AI Foundry [Model Catalog](https://ai.azure.com/catalog/models) provides access to **11,000+ models** from multiple providers:

| Provider | Embedding Models | Specialization |
|----------|------------------|----------------|
| **Azure OpenAI** | text-embedding-3-small, text-embedding-3-large | General purpose |
| **Cohere** | Cohere Embed v3 Multilingual | Semantic search, RAG, multi-modal |
| **Hugging Face** | Various BERT-based models | Domain-specific options |
| **Open Source** | Sentence Transformers, E5, BGE | Free, self-hosted |

### Cohere Embed for Legal Documents

[Cohere Embed v3](https://learn.microsoft.com/en-us/azure/ai-studio/how-to/deploy-models-cohere-embed) is particularly relevant for legal use cases:

**Real-World Legal Implementation**: [DraftWise](https://www.microsoft.com/en/customers/story/23918-draftwise-azure-ai-foundry) uses Cohere Embed + Cohere Rerank via Azure AI Foundry to:
- Enable semantic search through legal documents
- Find relevant precedents by meaning, not keywords
- Surface the most relevant results for complex legal cases

| Feature | text-embedding-3-small | Cohere Embed v3 |
|---------|------------------------|-----------------|
| **Dimensions** | 1536 | 1024 (configurable) |
| **Languages** | 100+ | 100+ (multilingual) |
| **Multi-modal** | Text only | Text + Images |
| **Fine-tuning** | ❌ Not available | ✅ Available |
| **Input types** | Single | search_document, search_query, classification, clustering |
| **Legal accuracy** | Good | Better (with fine-tuning) |

### Legal-Specialized Models

While not directly in Azure AI Foundry's featured models, legal-specific embedding models exist:

| Model | Source | Training Data | Availability |
|-------|--------|---------------|--------------|
| **Legal-BERT** | Research | Contracts, case law, legal texts | Hugging Face (self-host) |
| **Saul-7B** | Equall.ai | Legal documents | Commercial API |
| **ContractBERT** | Research | Contract-specific | Hugging Face (self-host) |

**Trade-off**: Legal-specialized models require self-hosting (Azure ML, Container Apps) vs. managed service convenience.

### Recommended Upgrade Path

```
CURRENT STATE (R5):
┌──────────────────────────────────────────────────────────────┐
│ text-embedding-3-small via Azure OpenAI                       │
│ + Query enrichment (Summary + Entities)                       │
│ + Hybrid search (vector + keyword + semantic ranking)         │
│                                                               │
│ Expected result: Significant accuracy improvement at low cost │
└──────────────────────────────────────────────────────────────┘

FUTURE STATE (R6/R7 if needed):
┌──────────────────────────────────────────────────────────────┐
│ Option A: Cohere Embed v3 via AI Foundry                      │
│ • Fine-tune on customer's legal documents                     │
│ • Use input_type="search_document" for indexing               │
│ • Use input_type="search_query" for queries                   │
│ • Add Cohere Rerank for result quality                        │
│                                                               │
│ Option B: text-embedding-3-large                              │
│ • Simple upgrade, no architecture changes                     │
│ • 6x cost, ~10-15% accuracy improvement                       │
│ • Good middle ground before specialized models                │
│                                                               │
│ Option C: Self-hosted Legal-BERT                              │
│ • Maximum legal domain accuracy                               │
│ • Requires Azure ML or Container Apps hosting                 │
│ • Higher maintenance burden                                   │
└──────────────────────────────────────────────────────────────┘
```

### Implementation Considerations for Model Switch

If switching embedding models in the future:

| Task | Complexity | Notes |
|------|------------|-------|
| Deploy new model | Low | AI Foundry makes this easy |
| Update RagService | Medium | Abstract embedding provider |
| **Re-index all documents** | High | Different model = incompatible vectors |
| Update cache key strategy | Medium | Include model ID in cache key |
| A/B testing | Medium | Run both models, compare quality |

**Critical**: Switching models requires **complete re-indexing** because vectors from different models are not comparable.

### Integration with Existing AI Foundry Infrastructure

Spaarke's AI Foundry is already configured:

```
AI Foundry Hub: sprkspaarkedev-aif-hub
AI Foundry Project: sprkspaarkedev-aif-proj

Current connections:
├── azure-openai-connection → spaarke-openai-dev
└── ai-search-connection → spaarke-search-dev

To add Cohere:
└── cohere-connection → Deploy Cohere Embed v3 from Model Catalog
```

### Recommendation

**For R5**: Focus on query strategy improvement (Summary + Entities) - this is the highest-impact, lowest-cost change.

**Future evaluation trigger**: If after R5 deployment, search relevance scores consistently fall below 0.7, evaluate:
1. First: `text-embedding-3-large` (simple upgrade)
2. Then: Cohere Embed v3 with fine-tuning (if legal accuracy is specifically the issue)
3. Finally: Self-hosted Legal-BERT (if maximum legal specialization required)

---

## Advanced Architecture: Embedding Model as Knowledge Scope

### Concept

Instead of a single embedding model for all RAG operations, make the embedding model **configurable per Knowledge scope**. This allows domain-specific optimization:

```
CURRENT ARCHITECTURE:
┌─────────────────────────────────────────────────────────────────┐
│ All Knowledge Sources → text-embedding-3-small → Single Index   │
│                                                                 │
│ Playbook: Contract Review    ───┐                               │
│ Playbook: Patent Analysis    ───┼──→ Same embedding model       │
│ Playbook: Financial Reports  ───┘                               │
└─────────────────────────────────────────────────────────────────┘

PROPOSED ARCHITECTURE:
┌─────────────────────────────────────────────────────────────────┐
│ Knowledge Scope determines embedding model + index               │
│                                                                 │
│ Playbook: Contract Review                                       │
│   └─→ Knowledge: "Legal Standards"                              │
│       └─→ Model: Cohere-Legal  ───→ legal-knowledge-index       │
│                                                                 │
│ Playbook: Patent Analysis                                       │
│   └─→ Knowledge: "Patent Database"                              │
│       └─→ Model: Patent-BERT   ───→ patent-knowledge-index      │
│                                                                 │
│ Playbook: General Analysis                                      │
│   └─→ Knowledge: "Company Docs"                                 │
│       └─→ Model: text-embedding-3-small ───→ general-index      │
└─────────────────────────────────────────────────────────────────┘
```

### Dataverse Schema Extension

Extend `sprk_analysisknowledge` entity:

| Field | Type | Purpose |
|-------|------|---------|
| `sprk_name` | String | Knowledge source name (existing) |
| `sprk_ragindexname` | String | Azure AI Search index name (existing) |
| `sprk_embeddingmodel` | Choice | **NEW**: Embedding model to use |
| `sprk_embeddingendpoint` | String | **NEW**: Model endpoint (for non-Azure OpenAI) |

**Choice values for `sprk_embeddingmodel`**:

| Value | Label | Model | Use Case |
|-------|-------|-------|----------|
| 1 | Azure OpenAI Small | text-embedding-3-small | General purpose (default) |
| 2 | Azure OpenAI Large | text-embedding-3-large | Higher accuracy general |
| 3 | Cohere Embed | Cohere Embed v3 | Fine-tunable, multilingual |
| 4 | Legal-BERT | legal-bert-base | Legal documents |
| 5 | Patent-BERT | patent-bert | Patent/IP documents |
| 6 | Custom | (from endpoint) | Customer-provided |

### Query-Time Model Selection

At query time, the system must use the **same model** that indexed the documents:

```csharp
public class RagService
{
    public async Task<SearchResults> SearchAsync(
        string query,
        KnowledgeSource knowledgeSource,  // Includes embedding model config
        CancellationToken ct)
    {
        // Get the correct embedding provider for this knowledge source
        var embeddingProvider = _embeddingProviderFactory.GetProvider(
            knowledgeSource.EmbeddingModel,
            knowledgeSource.EmbeddingEndpoint);

        // Generate embedding using the source's model
        var queryVector = await embeddingProvider.GetEmbeddingAsync(query, ct);

        // Search the source's specific index
        return await _searchClient.SearchAsync(
            knowledgeSource.RagIndexName,
            queryVector,
            ct);
    }
}
```

### Embedding Provider Abstraction

```csharp
public interface IEmbeddingProvider
{
    string ModelId { get; }
    int Dimensions { get; }
    Task<ReadOnlyMemory<float>> GetEmbeddingAsync(string text, CancellationToken ct);
}

public class EmbeddingProviderFactory
{
    public IEmbeddingProvider GetProvider(EmbeddingModel model, string? endpoint)
    {
        return model switch
        {
            EmbeddingModel.AzureOpenAISmall => new AzureOpenAIEmbeddingProvider("text-embedding-3-small"),
            EmbeddingModel.AzureOpenAILarge => new AzureOpenAIEmbeddingProvider("text-embedding-3-large"),
            EmbeddingModel.CohereEmbed => new CohereEmbeddingProvider(endpoint),
            EmbeddingModel.LegalBERT => new SelfHostedEmbeddingProvider(endpoint, "legal-bert"),
            EmbeddingModel.PatentBERT => new SelfHostedEmbeddingProvider(endpoint, "patent-bert"),
            EmbeddingModel.Custom => new SelfHostedEmbeddingProvider(endpoint),
            _ => throw new ArgumentException($"Unknown embedding model: {model}")
        };
    }
}
```

### Index Naming Convention

Each embedding model requires its own index (vectors are incompatible across models):

```
Index naming pattern:
{tenant}-{purpose}-{model}

Examples:
├── contoso-legal-cohere          (Legal-BERT indexed legal docs)
├── contoso-patents-patentbert    (Patent-BERT indexed patents)
├── contoso-general-small         (text-embedding-3-small general)
└── contoso-discovery-small       (All documents for semantic search)
```

### Cache Key Strategy

Include model ID in cache key to prevent cross-model cache hits:

```csharp
// Current (problematic if multiple models):
var cacheKey = $"sdap:embedding:{contentHash}";

// Enhanced (model-aware):
var cacheKey = $"sdap:embedding:{modelId}:{contentHash}";

// Examples:
// sdap:embedding:text-embedding-3-small:abc123...
// sdap:embedding:cohere-embed-v3:abc123...
// sdap:embedding:legal-bert:abc123...
```

### Use Case Examples

**Example 1: Law Firm with Multiple Practice Areas**

```
┌─────────────────────────────────────────────────────────────────┐
│ Playbook: Corporate M&A Review                                   │
│   Actions: Risk Detection, Due Diligence                        │
│   Knowledge Sources:                                            │
│   ├── "M&A Precedents" (Cohere-Legal, fine-tuned on M&A)       │
│   └── "Regulatory Filings" (text-embedding-3-large)            │
├─────────────────────────────────────────────────────────────────┤
│ Playbook: Patent Infringement Analysis                          │
│   Actions: Prior Art Search, Claim Mapping                      │
│   Knowledge Sources:                                            │
│   ├── "USPTO Patents" (Patent-BERT)                            │
│   └── "Technical Standards" (text-embedding-3-small)           │
├─────────────────────────────────────────────────────────────────┤
│ Playbook: Employment Dispute                                    │
│   Actions: Policy Comparison, Case Law Search                   │
│   Knowledge Sources:                                            │
│   └── "Employment Law KB" (Legal-BERT)                         │
└─────────────────────────────────────────────────────────────────┘
```

**Example 2: Financial Services**

```
┌─────────────────────────────────────────────────────────────────┐
│ Playbook: Regulatory Compliance Review                          │
│   Knowledge: "SEC Filings KB" (FinBERT for financial language) │
├─────────────────────────────────────────────────────────────────┤
│ Playbook: Contract Analysis                                     │
│   Knowledge: "Standard Terms" (Cohere-Legal)                   │
└─────────────────────────────────────────────────────────────────┘
```

### Implementation Phases

| Phase | Scope | Deliverable |
|-------|-------|-------------|
| **R5** | Single model, improved queries | Query strategy fix (Summary + Entities) |
| **R6** | Abstraction layer | `IEmbeddingProvider` interface, factory pattern |
| **R7** | Multi-model support | Cohere integration, model-per-knowledge-source |
| **R8** | Self-hosted models | Azure ML hosting for Legal-BERT, Patent-BERT |

### Benefits

| Benefit | Impact |
|---------|--------|
| **Domain optimization** | Legal playbooks get legal-trained embeddings |
| **Cost flexibility** | Use cheaper models where accuracy isn't critical |
| **Future-proof** | Add new models without architecture changes |
| **Customer customization** | Customers can bring their own fine-tuned models |
| **A/B testing** | Compare model performance on same content |

### Challenges

| Challenge | Mitigation |
|-----------|------------|
| **Index proliferation** | Naming conventions, index lifecycle management |
| **Cost complexity** | Per-model cost tracking, usage dashboards |
| **Model management** | AI Foundry centralizes model deployments |
| **Re-indexing** | Background job to re-index when model changes |
| **Cache complexity** | Model-aware cache keys |

### Admin UX Concept

```
┌─────────────────────────────────────────────────────────────────┐
│ Knowledge Source: Legal Standards                                │
├─────────────────────────────────────────────────────────────────┤
│ Name:           [Legal Standards                    ]           │
│ Description:    [Authoritative legal reference docs ]           │
│                                                                 │
│ RAG Configuration:                                              │
│ ┌─────────────────────────────────────────────────────────────┐│
│ │ Embedding Model:  [Cohere Embed v3 (Legal)     ▼]          ││
│ │                                                             ││
│ │ ℹ️ Cohere Embed v3 is recommended for legal documents.     ││
│ │    Fine-tuned on contract language and legal terminology.  ││
│ │                                                             ││
│ │ Index Name:       contoso-legal-cohere (auto-generated)    ││
│ │ Documents:        47 indexed                                ││
│ │ Last Updated:     2026-01-06 14:32                         ││
│ └─────────────────────────────────────────────────────────────┘│
│                                                                 │
│ [Re-index with New Model]  [Test Search Quality]                │
└─────────────────────────────────────────────────────────────────┘
```

---

*Document Version: 1.3 | Created: January 2026 | Added embedding model as knowledge scope concept*
