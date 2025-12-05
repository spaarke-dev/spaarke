# Spaarke AI Strategy & Architecture

> **Version**: 1.0  
> **Date**: December 2025  
> **Status**: Draft  
> **Authors**: Spaarke Engineering

---

## Executive Summary

Spaarke will build **custom AI capabilities** integrated into the SharePoint Document Access Platform (SDAP) using Azure AI services, Semantic Kernel, and custom RAG pipelines. This strategy is **independent of M365 Copilot and Copilot Studio**, ensuring consistent AI features across both deployment models.

**Key Decisions:**
- Build Spaarke-owned AI Copilots (not M365 Copilot dependent)
- Use Azure OpenAI + Azure AI Search as primary AI stack
- Support both Spaarke-hosted and Customer-hosted deployments
- Design for multi-tenant isolation from day one

---

## Table of Contents

1. [Deployment Models](#1-deployment-models)
2. [Architecture Overview](#2-architecture-overview)
3. [Azure Services & Components](#3-azure-services--components)
4. [Application Components](#4-application-components)
5. [Use Cases & Solutions](#5-use-cases--solutions)
6. [Data Flow & Pipelines](#6-data-flow--pipelines)
7. [Security & Compliance](#7-security--compliance)
8. [Implementation Roadmap](#8-implementation-roadmap)
9. [Cost Model](#9-cost-model)
10. [Future Considerations](#10-future-considerations)

---

## 1. Deployment Models

### 1.1 Model 1: Spaarke-Hosted

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                         MODEL 1: SPAARKE-HOSTED                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  SPAARKE TENANT                                                             │
│  ─────────────                                                              │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                    SPAARKE AZURE SUBSCRIPTION                       │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │ Azure OpenAI │  │ Azure AI     │  │ Document     │              │   │
│  │  │ (Shared)     │  │ Search       │  │ Intelligence │              │   │
│  │  │              │  │ (Multi-index)│  │              │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  │         │                 │                 │                       │   │
│  │         └─────────────────┼─────────────────┘                       │   │
│  │                           │                                         │   │
│  │  ┌────────────────────────▼────────────────────────────────────┐   │   │
│  │  │                 SPAARKE BFF API                             │   │   │
│  │  │  • AI Endpoints                                             │   │   │
│  │  │  • Multi-tenant isolation                                   │   │   │
│  │  │  • Usage metering                                           │   │   │
│  │  └─────────────────────────────────────────────────────────────┘   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │              CUSTOMER ENVIRONMENTS (in Spaarke Tenant)              │   │
│  │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐                 │   │
│  │  │ Customer A  │  │ Customer B  │  │ Customer C  │                 │   │
│  │  │ Environment │  │ Environment │  │ Environment │                 │   │
│  │  │ (Dataverse) │  │ (Dataverse) │  │ (Dataverse) │                 │   │
│  │  └─────────────┘  └─────────────┘  └─────────────┘                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  CUSTOMER USERS: Guest Entra ID                                             │
│  AI BILLING: Spaarke meters usage, bills customer                           │
│  DATA ISOLATION: Logical (per-customer index, security filters)             │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Characteristics:**
| Aspect | Description |
|--------|-------------|
| **AI Resources** | Spaarke-owned Azure subscription |
| **Provisioning** | Automatic with customer onboarding |
| **Configuration** | Managed by Spaarke |
| **Data Isolation** | Logical isolation via customer-specific indexes and filters |
| **Cost Model** | Spaarke tracks usage, bills customer (bundled or metered) |
| **User Identity** | Guest Entra ID users |

---

### 1.2 Model 2: Customer-Hosted

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        MODEL 2: CUSTOMER-HOSTED                             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  CUSTOMER TENANT                                                            │
│  ───────────────                                                            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                   CUSTOMER AZURE SUBSCRIPTION                       │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │ Azure OpenAI │  │ Azure AI     │  │ Document     │              │   │
│  │  │ (Dedicated)  │  │ Search       │  │ Intelligence │              │   │
│  │  │              │  │ (Dedicated)  │  │              │              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                   CUSTOMER POWER PLATFORM                           │   │
│  │  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐              │   │
│  │  │ Dataverse    │  │ Spaarke      │  │ Spaarke      │              │   │
│  │  │ Environment  │  │ Solution     │  │ BFF API      │              │   │
│  │  │              │  │ (Installed)  │  │ (App Service)│              │   │
│  │  └──────────────┘  └──────────────┘  └──────────────┘              │   │
│  │                                              │                      │   │
│  │  ┌───────────────────────────────────────────▼──────────────────┐  │   │
│  │  │              AI CONFIGURATION (Dataverse Entity)             │  │   │
│  │  │  • Azure OpenAI Endpoint + Key (Key Vault ref)               │  │   │
│  │  │  • Azure AI Search Endpoint + Key                            │  │   │
│  │  │  • Model Deployment Names                                    │  │   │
│  │  └──────────────────────────────────────────────────────────────┘  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
│  CUSTOMER USERS: Internal Entra ID                                          │
│  AI BILLING: Customer pays Azure directly                                   │
│  DATA ISOLATION: Physical (dedicated resources)                             │
│  FUTURE OPTION: Customer can integrate with their M365 Copilot              │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Characteristics:**
| Aspect | Description |
|--------|-------------|
| **AI Resources** | Customer-owned Azure subscription (BYOK) |
| **Provisioning** | Customer provisions with Spaarke guidance/scripts |
| **Configuration** | Customer provides endpoints via Dataverse config entity |
| **Data Isolation** | Physical isolation (dedicated resources) |
| **Cost Model** | Customer pays Azure directly |
| **User Identity** | Internal Entra ID users |
| **Future Options** | Can integrate with customer's M365 Copilot if desired |

---

### 1.3 Feature Parity

| AI Capability | Model 1 | Model 2 | Notes |
|---------------|---------|---------|-------|
| Document Q&A (RAG) | ✅ | ✅ | Identical functionality |
| Semantic Search | ✅ | ✅ | Identical functionality |
| Document Summarization | ✅ | ✅ | Identical functionality |
| Metadata Extraction | ✅ | ✅ | Identical functionality |
| Document Classification | ✅ | ✅ | Identical functionality |
| Multi-document Analysis | ✅ | ✅ | Identical functionality |
| Chat History | ✅ | ✅ | Stored in Dataverse |
| Custom Prompts | ✅ | ✅ | Customer-configurable |

---

## 2. Architecture Overview

### 2.1 Layered Architecture

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              PRESENTATION LAYER                             │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐   │
│  │ AI Chat      │  │ AI Search    │  │ AI Insights  │  │ AI Actions   │   │
│  │ PCF Control  │  │ PCF Control  │  │ Panel PCF    │  │ Command Bar  │   │
│  └──────────────┘  └──────────────┘  └──────────────┘  └──────────────┘   │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                                 API LAYER                                   │
├─────────────────────────────────────────────────────────────────────────────┤
│                         BFF API (Spe.Bff.Api)                               │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  AI Endpoints                                                        │  │
│  │  • POST /api/ai/chat              - Conversational Q&A               │  │
│  │  • POST /api/ai/search            - Semantic document search         │  │
│  │  • POST /api/ai/summarize         - Document summarization           │  │
│  │  • POST /api/ai/extract           - Metadata/entity extraction       │  │
│  │  • POST /api/ai/classify          - Document classification          │  │
│  │  • POST /api/ai/compare           - Multi-document comparison        │  │
│  │  • GET  /api/ai/chat/{id}/history - Chat session history             │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                            ORCHESTRATION LAYER                              │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                      Semantic Kernel                                 │  │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐    │  │
│  │  │ Chat       │  │ RAG        │  │ Prompt     │  │ Function   │    │  │
│  │  │ Completion │  │ Pipeline   │  │ Templates  │  │ Calling    │    │  │
│  │  └────────────┘  └────────────┘  └────────────┘  └────────────┘    │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │                    AI Provider Abstraction                           │  │
│  │  interface IAIProvider {                                             │  │
│  │    CompleteAsync(prompt, options) → string                           │  │
│  │    EmbedAsync(text) → float[]                                        │  │
│  │    StreamCompleteAsync(prompt) → IAsyncEnumerable<string>            │  │
│  │  }                                                                   │  │
│  │  ┌─────────────────────┐    ┌─────────────────────┐                 │  │
│  │  │ SpaarkeAIProvider   │    │ CustomerAIProvider  │                 │  │
│  │  │ (Model 1)           │    │ (Model 2 - BYOK)    │                 │  │
│  │  └─────────────────────┘    └─────────────────────┘                 │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                              SERVICES LAYER                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐               │
│  │ Document       │  │ Vector         │  │ Chat           │               │
│  │ Indexing       │  │ Search         │  │ History        │               │
│  │ Service        │  │ Service        │  │ Service        │               │
│  └────────────────┘  └────────────────┘  └────────────────┘               │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐               │
│  │ Chunking       │  │ Embedding      │  │ Prompt         │               │
│  │ Service        │  │ Service        │  │ Management     │               │
│  └────────────────┘  └────────────────┘  └────────────────┘               │
└─────────────────────────────────────────────────────────────────────────────┘
                                      │
                                      ▼
┌─────────────────────────────────────────────────────────────────────────────┐
│                           INFRASTRUCTURE LAYER                              │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐               │
│  │ Azure OpenAI   │  │ Azure AI       │  │ Azure Document │               │
│  │                │  │ Search         │  │ Intelligence   │               │
│  └────────────────┘  └────────────────┘  └────────────────┘               │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐               │
│  │ SharePoint     │  │ Dataverse      │  │ Azure Key      │               │
│  │ Embedded (SPE) │  │                │  │ Vault          │               │
│  └────────────────┘  └────────────────┘  └────────────────┘               │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Azure Services & Components

### 3.1 Core AI Services

| Service | Purpose | Model 1 | Model 2 |
|---------|---------|---------|---------|
| **Azure OpenAI** | LLM for chat, summarization, extraction | Spaarke subscription | Customer subscription |
| **Azure AI Search** | Vector search, hybrid search, indexes | Spaarke subscription | Customer subscription |
| **Azure Document Intelligence** | PDF/image text extraction, layout analysis | Spaarke subscription | Customer subscription |

### 3.2 Azure OpenAI Configuration

| Deployment | Model | Purpose | TPM (Recommended) |
|------------|-------|---------|-------------------|
| `gpt-4-turbo` | gpt-4-turbo-2024-04-09 | Primary chat/reasoning | 80K |
| `gpt-4o` | gpt-4o-2024-08-06 | Fast responses, multimodal | 150K |
| `gpt-4o-mini` | gpt-4o-mini-2024-07-18 | Classification, simple tasks | 200K |
| `text-embedding-3-large` | text-embedding-3-large | Document embeddings | 350K |

### 3.3 Azure AI Search Configuration

| Component | Configuration |
|-----------|---------------|
| **SKU** | Standard S1 or S2 (based on document volume) |
| **Indexes** | One per customer (Model 1) or dedicated service (Model 2) |
| **Vector Config** | HNSW algorithm, 3072 dimensions (text-embedding-3-large) |
| **Semantic Ranker** | Enabled for hybrid search |

**Index Schema:**
```json
{
  "name": "spaarke-documents-{customer-id}",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "document_id", "type": "Edm.String", "filterable": true },
    { "name": "matter_id", "type": "Edm.String", "filterable": true },
    { "name": "customer_id", "type": "Edm.String", "filterable": true },
    { "name": "chunk_id", "type": "Edm.Int32" },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "title", "type": "Edm.String", "searchable": true },
    { "name": "file_type", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "created_date", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },
    { "name": "modified_date", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },
    { "name": "content_vector", "type": "Collection(Edm.Single)", "dimensions": 3072, "vectorSearchProfile": "default" },
    { "name": "metadata", "type": "Edm.String" }
  ],
  "vectorSearch": {
    "algorithms": [{ "name": "hnsw", "kind": "hnsw" }],
    "profiles": [{ "name": "default", "algorithm": "hnsw" }]
  },
  "semantic": {
    "configurations": [{
      "name": "default",
      "prioritizedFields": {
        "titleField": { "fieldName": "title" },
        "contentFields": [{ "fieldName": "content" }]
      }
    }]
  }
}
```

### 3.4 Azure Document Intelligence

| Feature | Usage |
|---------|-------|
| **Read API** | Extract text from PDFs, images, Office docs |
| **Layout API** | Understand document structure (tables, sections) |
| **Prebuilt Models** | Invoice, receipt, ID extraction (future) |

### 3.5 Supporting Azure Services

| Service | Purpose |
|---------|---------|
| **Azure Key Vault** | Store API keys, connection strings |
| **Azure Service Bus** | Queue document indexing jobs |
| **Azure Redis Cache** | Cache embeddings, search results |
| **Azure Monitor / App Insights** | AI telemetry, usage tracking |
| **Azure Blob Storage** | Temporary document processing |

---

## 4. Application Components

### 4.1 Backend Components (C#/.NET 8)

| Component | Location | Description |
|-----------|----------|-------------|
| **Spaarke.AI** | `src/server/shared/Spaarke.AI/` | Core AI library |
| **AI Endpoints** | `src/server/api/Spe.Bff.Api/Api/AIEndpoints.cs` | Minimal API endpoints |
| **AI Services** | `src/server/api/Spe.Bff.Api/Services/AI/` | AI service implementations |

**Core Interfaces:**
```csharp
// AI Provider Abstraction
public interface IAIProvider
{
    Task<string> CompleteAsync(string prompt, CompletionOptions options, CancellationToken ct);
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
    IAsyncEnumerable<string> StreamCompleteAsync(string prompt, CompletionOptions options, CancellationToken ct);
}

// Vector Search
public interface IVectorSearchService
{
    Task<SearchResult[]> SearchAsync(string query, SearchOptions options, CancellationToken ct);
    Task IndexDocumentAsync(DocumentChunk[] chunks, CancellationToken ct);
    Task DeleteDocumentAsync(string documentId, CancellationToken ct);
}

// Document Processing
public interface IDocumentProcessor
{
    Task<DocumentChunk[]> ChunkDocumentAsync(Stream content, string fileName, CancellationToken ct);
    Task<string> ExtractTextAsync(Stream content, string fileName, CancellationToken ct);
}

// Chat Service
public interface IChatService
{
    Task<ChatResponse> ChatAsync(ChatRequest request, CancellationToken ct);
    IAsyncEnumerable<ChatStreamEvent> ChatStreamAsync(ChatRequest request, CancellationToken ct);
    Task<ChatSession[]> GetSessionsAsync(Guid userId, CancellationToken ct);
}
```

### 4.2 Frontend Components (TypeScript/React)

| Component | Location | Description |
|-----------|----------|-------------|
| **AI Chat PCF** | `src/client/pcf/AIChatControl/` | Conversational interface |
| **AI Search PCF** | `src/client/pcf/AISearchControl/` | Semantic search UI |
| **AI Insights Panel** | `src/client/pcf/AIInsightsPanel/` | Document insights sidebar |
| **AI Components** | `src/client/shared/Spaarke.UI.Components/ai/` | Shared AI UI components |

### 4.3 Dataverse Components

| Entity | Purpose |
|--------|---------|
| `sprk_AIConfiguration` | AI provider settings (endpoints, keys) |
| `sprk_AIConversation` | Chat session storage |
| `sprk_AIMessage` | Chat message history |
| `sprk_AIPromptTemplate` | Custom prompt templates |
| `sprk_AIUsageLog` | Usage tracking for billing |

### 4.4 Background Workers

| Worker | Purpose |
|--------|---------|
| `DocumentIndexingWorker` | Process document indexing queue |
| `EmbeddingWorker` | Generate embeddings for new/updated docs |
| `IndexCleanupWorker` | Remove deleted document chunks |

---

## 5. Use Cases & Solutions

### 5.1 Document Q&A (RAG)

**Description:** Users ask natural language questions about their documents and receive AI-generated answers with citations.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                          DOCUMENT Q&A FLOW                                  │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  User: "What are the payment terms in the Smith contract?"                  │
│                           │                                                 │
│                           ▼                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 1. QUERY UNDERSTANDING                                              │   │
│  │    • Extract intent: find payment terms                             │   │
│  │    • Identify entity: Smith contract                                │   │
│  │    • Apply filters: matter_id, document access                      │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                           │                                                 │
│                           ▼                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 2. RETRIEVAL                                                        │   │
│  │    • Generate query embedding                                       │   │
│  │    • Hybrid search (vector + keyword)                               │   │
│  │    • Apply security filter: user's accessible documents             │   │
│  │    • Return top-k relevant chunks                                   │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                           │                                                 │
│                           ▼                                                 │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 3. GENERATION                                                       │   │
│  │    • Construct prompt with retrieved context                        │   │
│  │    • Call LLM (GPT-4)                                               │   │
│  │    • Generate answer with citations                                 │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                           │                                                 │
│                           ▼                                                 │
│  AI: "According to the Smith Services Agreement (Section 4.2),             │
│       payment terms are Net 30 from invoice date. A 2% late fee            │
│       applies after 45 days. [Source: smith-agreement.pdf, p.12]"          │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

**Prompt Template:**
```
You are an AI assistant for a legal document management system. Answer the user's question based ONLY on the provided context. If the answer is not in the context, say "I couldn't find that information in the available documents."

Always cite your sources using [Document Name, Page X] format.

## Context
{retrieved_chunks}

## Question
{user_question}

## Answer
```

---

### 5.2 Semantic Document Search

**Description:** Users search across documents using natural language, not just keywords.

| Traditional Search | Semantic Search |
|--------------------|-----------------|
| "payment terms contract" | "What are the conditions for late payments?" |
| Exact keyword match | Conceptual understanding |
| Limited to indexed terms | Understands synonyms, context |

**Implementation:**
- Hybrid search: Vector similarity + BM25 keyword scoring
- Semantic reranking for result quality
- Faceted filtering (file type, date, matter)

---

### 5.3 Document Summarization

**Description:** Generate concise summaries of long documents.

| Summary Type | Use Case | Length |
|--------------|----------|--------|
| **Brief** | Quick preview | 2-3 sentences |
| **Standard** | Document overview | 1 paragraph |
| **Detailed** | Comprehensive summary | Multi-paragraph with sections |
| **Key Points** | Bullet list | 5-10 bullet points |

**Trigger Points:**
- On-demand via AI Insights panel
- Auto-generate on document upload (background)
- Matter summary across multiple documents

---

### 5.4 Metadata Extraction

**Description:** Automatically extract structured metadata from documents.

| Metadata Type | Examples |
|---------------|----------|
| **Dates** | Effective date, expiration date, signature date |
| **Parties** | Client name, counterparty, signatories |
| **Monetary** | Contract value, payment amounts, fees |
| **Terms** | Duration, renewal terms, termination clauses |
| **Classification** | Document type, matter category |

**Workflow:**
```
Document Upload → Text Extraction → LLM Extraction → Dataverse Update
                                          │
                                          ▼
                                   sprk_Document entity
                                   • sprk_effectivedate
                                   • sprk_expirationdate
                                   • sprk_contractvalue
                                   • sprk_parties (JSON)
```

---

### 5.5 Document Classification

**Description:** Auto-categorize documents based on content.

| Category | Examples |
|----------|----------|
| **Document Type** | Contract, Agreement, Letter, Invoice, Report |
| **Legal Category** | NDA, MSA, SOW, Amendment, Addendum |
| **Priority** | High, Medium, Low |
| **Confidentiality** | Public, Internal, Confidential, Restricted |

**Approach:**
- Few-shot classification with GPT-4o-mini
- Customer-configurable categories
- Confidence scoring

---

### 5.6 Multi-Document Analysis

**Description:** Compare, contrast, or analyze multiple documents together.

| Analysis Type | Description |
|---------------|-------------|
| **Comparison** | Side-by-side clause comparison between contracts |
| **Conflict Detection** | Identify conflicting terms across documents |
| **Coverage Analysis** | Check if all required clauses are present |
| **Timeline** | Extract and visualize key dates across documents |

---

### 5.7 Smart Suggestions

**Description:** Proactive AI suggestions based on context.

| Context | Suggestion |
|---------|------------|
| Viewing expired contract | "This contract expired 30 days ago. Would you like to find the renewal?" |
| Opening matter | "3 documents need review. Would you like a summary?" |
| Searching | "Did you mean documents related to [matter name]?" |

---

## 6. Data Flow & Pipelines

### 6.1 Document Indexing Pipeline

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                       DOCUMENT INDEXING PIPELINE                            │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌──────────────┐                                                          │
│  │ Document     │  Trigger: Upload, Update, Association                    │
│  │ Change       │                                                          │
│  └──────┬───────┘                                                          │
│         │                                                                   │
│         ▼                                                                   │
│  ┌──────────────┐                                                          │
│  │ Service Bus  │  Queue: document-indexing                                │
│  │ Message      │  Payload: { documentId, action, matterId }               │
│  └──────┬───────┘                                                          │
│         │                                                                   │
│         ▼                                                                   │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │ INDEXING WORKER                                                      │  │
│  │                                                                      │  │
│  │  1. Fetch document from SPE                                          │  │
│  │     └─ GET /drives/{driveId}/items/{itemId}/content                  │  │
│  │                                                                      │  │
│  │  2. Extract text                                                     │  │
│  │     ├─ Office docs: Direct text extraction                           │  │
│  │     ├─ PDFs: Azure Document Intelligence                             │  │
│  │     └─ Images: OCR via Document Intelligence                         │  │
│  │                                                                      │  │
│  │  3. Chunk content                                                    │  │
│  │     ├─ Strategy: Semantic chunking (paragraph/section aware)         │  │
│  │     ├─ Size: 512-1024 tokens                                         │  │
│  │     └─ Overlap: 20%                                                  │  │
│  │                                                                      │  │
│  │  4. Generate embeddings                                              │  │
│  │     └─ Azure OpenAI text-embedding-3-large                           │  │
│  │                                                                      │  │
│  │  5. Index to Azure AI Search                                         │  │
│  │     └─ Upsert chunks with metadata                                   │  │
│  │                                                                      │  │
│  │  6. Update Dataverse                                                 │  │
│  │     └─ Set sprk_isindexed = true, sprk_indexedon = now               │  │
│  │                                                                      │  │
│  └──────────────────────────────────────────────────────────────────────┘  │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 6.2 RAG Query Pipeline

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           RAG QUERY PIPELINE                                │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  User Query ──► Embed Query ──► Vector Search ──► Rerank ──► LLM ──► Answer│
│                     │              │                │          │            │
│                     ▼              ▼                ▼          ▼            │
│               ┌─────────┐   ┌───────────┐    ┌─────────┐  ┌─────────┐      │
│               │ OpenAI  │   │ AI Search │    │ Semantic│  │ GPT-4   │      │
│               │Embedding│   │  Hybrid   │    │ Ranker  │  │ Turbo   │      │
│               └─────────┘   └───────────┘    └─────────┘  └─────────┘      │
│                                   │                                         │
│                                   ▼                                         │
│                         ┌─────────────────────┐                            │
│                         │  SECURITY FILTER    │                            │
│                         │  • customer_id      │                            │
│                         │  • user permissions │                            │
│                         │  • matter access    │                            │
│                         └─────────────────────┘                            │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 7. Security & Compliance

### 7.1 Data Security

| Concern | Mitigation |
|---------|------------|
| **Data at rest** | Azure AI Search encrypted, OpenAI stateless |
| **Data in transit** | TLS 1.3 for all API calls |
| **Data residency** | Configure Azure region per customer needs |
| **PII handling** | Option to mask PII before LLM processing |
| **No model training** | Azure OpenAI does not train on customer data |

### 7.2 Access Control

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                        AI ACCESS CONTROL FLOW                               │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  User Request                                                               │
│       │                                                                     │
│       ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 1. AUTHENTICATION (Existing Spaarke Auth)                           │   │
│  │    • Validate Entra ID token                                        │   │
│  │    • Resolve user to customer context                               │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│       │                                                                     │
│       ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 2. AI FEATURE AUTHORIZATION                                         │   │
│  │    • Check user has AI access (license/role)                        │   │
│  │    • Check AI feature is enabled for customer                       │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│       │                                                                     │
│       ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 3. DOCUMENT ACCESS FILTER                                           │   │
│  │    • Query only documents user can access                           │   │
│  │    • Apply matter-level permissions                                 │   │
│  │    • Enforce row-level security                                     │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│       │                                                                     │
│       ▼                                                                     │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │ 4. SEARCH FILTER INJECTION                                          │   │
│  │    • Add customer_id filter to all queries                          │   │
│  │    • Add accessible_document_ids filter                             │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 7.3 Audit & Compliance

| Audit Point | Captured Data |
|-------------|---------------|
| AI queries | Query text, user, timestamp, response summary |
| Document access | Which documents were retrieved for context |
| Token usage | Input/output tokens per request |
| Errors | Failed requests, rate limits, errors |

---

## 8. Implementation Roadmap

### Phase 1: Foundation (Q1)

| Deliverable | Description | Owner |
|-------------|-------------|-------|
| AI Provider abstraction | IAIProvider interface + implementations | Backend |
| Azure AI Search setup | Index schema, security filters | Backend |
| Document indexing worker | Chunking, embedding, indexing | Backend |
| AI configuration entity | Dataverse entity for settings | Platform |
| Basic AI endpoints | /chat, /search | Backend |

### Phase 2: Core Features (Q2)

| Deliverable | Description | Owner |
|-------------|-------------|-------|
| RAG chat endpoint | Streaming responses, citations | Backend |
| AI Chat PCF control | Conversational UI | Frontend |
| Semantic search | Hybrid search, reranking | Backend |
| AI Search PCF control | Search results UI | Frontend |
| Chat history | Conversation persistence | Full stack |

### Phase 3: Intelligence (Q3)

| Deliverable | Description | Owner |
|-------------|-------------|-------|
| Document summarization | Auto-summary, on-demand | Backend |
| Metadata extraction | Dates, parties, values | Backend |
| AI Insights panel | Document-level insights | Frontend |
| Classification | Auto-categorization | Backend |

### Phase 4: Advanced (Q4)

| Deliverable | Description | Owner |
|-------------|-------------|-------|
| Multi-document analysis | Compare, conflict detection | Backend |
| Smart suggestions | Proactive recommendations | Full stack |
| Custom prompts | Customer-configurable | Full stack |
| Usage analytics | AI usage dashboard | Full stack |

---

## 9. Cost Model

### 9.1 Azure OpenAI Pricing (Estimated)

| Model | Price per 1K tokens | Typical usage |
|-------|---------------------|---------------|
| GPT-4 Turbo Input | $0.01 | RAG context (4K tokens avg) |
| GPT-4 Turbo Output | $0.03 | Responses (500 tokens avg) |
| GPT-4o Input | $0.005 | Fast queries |
| GPT-4o Output | $0.015 | Fast responses |
| text-embedding-3-large | $0.00013 | Document indexing |

**Example: 1 RAG query**
- Input: 4,000 tokens (context) = $0.04
- Output: 500 tokens = $0.015
- **Total: ~$0.055 per query**

### 9.2 Azure AI Search Pricing

| SKU | Monthly Cost | Documents | Queries/sec |
|-----|--------------|-----------|-------------|
| S1 | ~$250 | 2M chunks | 50 QPS |
| S2 | ~$1,000 | 10M chunks | 100 QPS |

### 9.3 Model 1 Cost Recovery

| Approach | Description |
|----------|-------------|
| **Bundled** | Include AI in subscription tier |
| **Metered** | Track per-query, bill monthly |
| **Hybrid** | Included queries + overage |

---

## 10. Future Considerations

### 10.1 M365 Copilot Integration (Model 2 Only)

If customers with M365 Copilot licenses want integration:
- **Graph Connector**: Index SPE documents to Microsoft Search
- **Declarative Agent**: Spaarke-specific agent in customer's Copilot
- **Plugin**: Expose Spaarke AI as Copilot plugin

### 10.2 Advanced Capabilities

| Capability | Timeline | Description |
|------------|----------|-------------|
| **Multimodal** | 2025 H2 | Process images, diagrams in documents |
| **Agentic workflows** | 2026 | Multi-step reasoning, tool use |
| **Fine-tuned models** | 2026 | Customer-specific model training |
| **On-premise option** | 2026+ | Air-gapped deployment |

### 10.3 Technology Watch

| Technology | Interest | Notes |
|------------|----------|-------|
| **GPT-5** | High | Performance improvements |
| **Anthropic Claude** | Medium | Alternative provider |
| **Local models (Llama, Phi)** | Medium | Cost reduction, privacy |
| **Microsoft Copilot APIs** | High | Future integration |

---

## Appendix A: Related ADRs

| ADR | Status | Description |
|-----|--------|-------------|
| ADR-013 | Proposed | AI Provider Abstraction |
| ADR-014 | Proposed | Vector Storage Strategy |
| ADR-015 | Proposed | AI Data Isolation |
| ADR-016 | Proposed | AI Configuration Management |

---

## Appendix B: References

- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Azure AI Search Documentation](https://learn.microsoft.com/en-us/azure/search/)
- [Semantic Kernel Documentation](https://learn.microsoft.com/en-us/semantic-kernel/)
- [Azure Document Intelligence](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/)

---

*Document Owner: Spaarke Engineering*  
*Review Cycle: Quarterly*
