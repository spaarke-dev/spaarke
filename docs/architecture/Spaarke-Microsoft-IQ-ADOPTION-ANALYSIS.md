# Spaarke & Microsoft IQ Services: Adoption Analysis

> **Last Updated**: January 2026
> **Status**: Strategic Analysis
> **Target**: Product Launch June 2026

---

## Executive Summary

This document analyzes Microsoft's emerging "IQ" family of AI services and their relationship to Spaarke's AI architecture. Key findings:

1. **Spaarke is well-aligned** with Microsoft's AI direction
2. **Our Playbook system is a unique differentiator** - Microsoft doesn't offer equivalent no-code AI workflow composition
3. **Migration paths exist** but are optional - we control our destiny
4. **Foundry Agent Service supports custom agents** - M365 Copilot is optional, not required

---

## Microsoft IQ Family Overview

Microsoft's "IQ" branding represents managed AI services that simplify complex AI operations:

| Service | Purpose | Status (Jan 2026) | Spaarke Equivalent |
|---------|---------|-------------------|-------------------|
| **Foundry IQ** | Managed RAG service | Public Preview | Custom RagService |
| **Fabric IQ** | Data intelligence | GA | N/A (different domain) |
| **Work IQ** | M365 productivity insights | GA | N/A (different domain) |

**Note**: There is no "Agent IQ" - agent capabilities are provided by:
- **Foundry Agent Service** - Runtime for hosting/deploying agents
- **Microsoft Agent Framework** - Open-source SDK (replaces Semantic Kernel + AutoGen)

---

## Foundry IQ (Knowledge IQ) Analysis

### What It Is

Foundry IQ is a **managed RAG-as-a-Service** offering that packages:
- Azure AI Search (vector database)
- Azure OpenAI (embeddings + completions)
- Document processing pipeline
- Pre-built chunking strategies

### How Spaarke Currently Does RAG

We built equivalent functionality in R3:

```
┌─────────────────────────────────────────────────────────────────┐
│                     Spaarke Custom RAG Stack                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────────┐  │
│   │ RagService   │───▶│ OpenAiClient │───▶│ Azure OpenAI     │  │
│   │ (Hybrid      │    │ (Embeddings) │    │ text-embedding-  │  │
│   │  Search)     │    │              │    │ 3-small          │  │
│   └──────┬───────┘    └──────────────┘    └──────────────────┘  │
│          │                                                       │
│          ▼                                                       │
│   ┌──────────────┐    ┌──────────────┐    ┌──────────────────┐  │
│   │ Embedding    │───▶│ Redis Cache  │    │ Azure AI Search  │  │
│   │ Cache        │    │ (7-day TTL)  │    │ spaarke-         │  │
│   │ (SHA256 key) │    │              │    │ knowledge-index  │  │
│   └──────────────┘    └──────────────┘    └──────────────────┘  │
│                                                                  │
│   Search Strategy: Keyword (BM25) + Vector (HNSW) + Semantic    │
│   Dimensions: 1536 | Algorithm: Cosine Similarity               │
│   Multi-tenant: OData filter on tenantId                        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Key Implementation Details

**Azure AI Search Index Schema** (`spaarke-knowledge-index`):
- `contentVector`: 1536-dimension embeddings
- `tenantId`: Filterable for multi-tenant isolation
- `content`: Full-text searchable
- HNSW algorithm with cosine metric
- Semantic ranking configuration

**RagService Hybrid Search**:
```csharp
// Simplified flow
public async Task<RagSearchResponse> SearchAsync(string query, ...)
{
    // 1. Check embedding cache (Redis)
    var embedding = await _embeddingCache.GetEmbeddingForContentAsync(query);

    // 2. Generate embedding if cache miss
    if (!embedding.HasValue)
    {
        embedding = await _openAiClient.GenerateEmbeddingAsync(query);
        await _embeddingCache.SetEmbeddingForContentAsync(query, embedding);
    }

    // 3. Build hybrid search (keyword + vector + semantic)
    var options = new SearchOptions
    {
        VectorSearch = new VectorSearchOptions { ... },
        SemanticSearch = new SemanticSearchOptions { ... },
        Filter = $"tenantId eq '{tenantId}'"  // Multi-tenant isolation
    };

    // 4. Execute against Azure AI Search
    return await _searchClient.SearchAsync<KnowledgeDocument>(query, options);
}
```

### Foundry IQ vs Custom RagService

| Aspect | Foundry IQ | Spaarke RagService |
|--------|------------|-------------------|
| **Backend** | Azure AI Search | Azure AI Search (same!) |
| **Embeddings** | Azure OpenAI | Azure OpenAI (same!) |
| **API Complexity** | Simplified single API | Custom multi-step |
| **Chunking** | Pre-built strategies | Custom via ChunkingService |
| **Caching** | Unknown/managed | Redis with SHA256 keys |
| **Multi-tenant** | Unknown | OData filter on tenantId |
| **Customization** | Limited | Full control |

### What "Migration" Would Mean

Migration to Foundry IQ would **not** require changing databases - we already use Azure AI Search. It would mean:

```
BEFORE (Custom):                    AFTER (Foundry IQ):
┌─────────────────────┐            ┌─────────────────────┐
│ RagService          │            │ Foundry IQ Client   │
│ ├─ ChunkingService  │   ───▶     │ (Single API call)   │
│ ├─ EmbeddingCache   │            │                     │
│ ├─ OpenAiClient     │            │ Manages internally: │
│ └─ SearchClient     │            │ • Chunking          │
└─────────────────────┘            │ • Embeddings        │
                                   │ • Search            │
                                   └─────────────────────┘
```

**Trade-offs**:
- **Simplicity**: Less code to maintain
- **Control**: Less customization (chunking, caching strategies)
- **Cost**: Potentially different pricing model
- **Features**: May not support all our multi-tenant patterns

---

## Foundry Agent Service Analysis

### What It Is

Foundry Agent Service is a **runtime for hosting AI agents** with:
- Multi-agent orchestration
- Visual workflow designer
- YAML-based agent definitions
- Governance and monitoring
- Optional M365 Copilot deployment

### Key Question: Custom Agents vs M365 Copilot

**Answer: Foundry Agent Service fully supports custom agents that we create, deploy, and manage independently. M365 Copilot integration is an OPTION, not a requirement.**

```
┌─────────────────────────────────────────────────────────────────┐
│                    Foundry Agent Service                         │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   ┌──────────────────┐    ┌──────────────────────────────────┐  │
│   │  Custom Agents   │    │  M365 Copilot Extensions         │  │
│   │  (Standalone)    │    │  (Optional Integration)          │  │
│   ├──────────────────┤    ├──────────────────────────────────┤  │
│   │ • Your own UI    │    │ • Appears in Copilot chat        │  │
│   │ • API endpoints  │    │ • Declarative agents             │  │
│   │ • PCF controls   │    │ • M365 context awareness         │  │
│   │ • Web apps       │    │ • Teams/Outlook integration      │  │
│   └──────────────────┘    └──────────────────────────────────┘  │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Foundry Agent Service Capabilities

| Capability | Description | Spaarke Relevance |
|------------|-------------|-------------------|
| **Agent Runtime** | Host agents built with any framework | Could host our tools |
| **Visual Designer** | Build workflows visually + YAML | Alternative to code |
| **Multi-Agent** | Coordinate specialized agents | Like our orchestration |
| **Governance** | Enterprise controls, monitoring | Production requirement |
| **Open Standards** | MCP, A2A, OpenAPI support | Interoperability |

### Spaarke vs Foundry Agent Service

| Spaarke Component | Foundry Agent Service Equivalent |
|-------------------|----------------------------------|
| Tool Handlers (EntityExtractor, etc.) | Agent "skills" or "tools" |
| AnalysisOrchestrationService | Multi-agent orchestration |
| Playbook system | **No equivalent - our differentiator** |
| Custom PCF UI | Keep as-is (our differentiator) |
| BFF API hosting | Foundry runtime (optional) |

---

## Spaarke's Unique Differentiator: Playbook System

Microsoft's offerings focus on developer-centric tooling. Spaarke's Playbook system enables **no-code AI workflow composition** for domain experts:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Spaarke Playbook System                       │
│              (No Microsoft Equivalent Exists)                    │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   Domain Expert (No Code Required):                              │
│   ┌──────────────────────────────────────────────────────────┐  │
│   │  "Create NDA Review Playbook"                             │  │
│   │                                                           │  │
│   │  ┌─────────┐   ┌─────────┐   ┌─────────┐   ┌─────────┐  │  │
│   │  │ Extract │──▶│ Analyze │──▶│ Detect  │──▶│ Format  │  │  │
│   │  │ Parties │   │ Clauses │   │ Risks   │   │ Report  │  │  │
│   │  └─────────┘   └─────────┘   └─────────┘   └─────────┘  │  │
│   │                                                           │  │
│   │  + Knowledge: "Standard NDA Terms"                        │  │
│   │  + Skills: "NDA Review", "Risk Assessment"                │  │
│   └──────────────────────────────────────────────────────────┘  │
│                                                                  │
│   Stored as Dataverse records (not code):                        │
│   • sprk_aiplaybook (workflow definition)                        │
│   • sprk_aiaction (individual AI operations)                     │
│   • sprk_aiskill (reusable skill bundles)                        │
│   • sprk_aiknowledge (RAG context sources)                       │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

**Why This Matters**:
- Legal teams can create contract review workflows without developers
- Compliance officers can build audit playbooks
- Customers can customize for their specific document types
- Playbooks are shareable, versionable, and auditable

---

## Strategic Migration Paths

### Option A: Stay Custom (Full Control)

```
Continue current architecture:
├── Custom RagService (Azure AI Search backend)
├── Custom AnalysisOrchestrationService
├── Custom Tool Handlers
├── Playbook system (Dataverse)
└── PCF Controls (custom UI)

Pros: Full control, no external dependencies, proven architecture
Cons: More code to maintain, miss potential optimizations
```

### Option B: Hybrid Adoption (Recommended)

```
Adopt Foundry IQ for RAG, keep custom orchestration:
├── Foundry IQ (replaces RagService)
│   └── Simpler API, managed infrastructure
├── Custom AnalysisOrchestrationService (keep)
│   └── Our Playbook system drives this
├── Custom Tool Handlers (keep)
└── PCF Controls (keep)

Pros: Reduced maintenance for RAG, keep differentiators
Cons: Less control over RAG behavior, dependency on preview service
```

### Option C: Full Foundry Adoption (Future Option)

```
Full adoption of Microsoft Foundry platform:
├── Foundry IQ (RAG)
├── Foundry Agent Service (orchestration runtime)
├── Playbook system (Dataverse - our config layer)
└── PCF Controls (keep - UI differentiator)

Pros: Maximum alignment with Microsoft, reduced infrastructure
Cons: Less control, potential lock-in, dependency on GA timeline
```

### Recommended Approach

```
┌─────────────────────────────────────────────────────────────────┐
│                    Recommended Timeline                          │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   Now ────────────── June 2026 ────────────── Post-GA           │
│    │                     │                        │              │
│    │  Build custom       │  Launch with          │  Evaluate    │
│    │  (proven, works)    │  custom stack         │  Foundry IQ  │
│    │                     │                        │  migration   │
│    │                     │                        │              │
│    └─────────────────────┴────────────────────────┘              │
│                                                                  │
│   Key Principle: Build for flexibility, not for migration        │
│                                                                  │
│   Our architecture already uses Azure AI Search (same backend    │
│   as Foundry IQ), so migration is a refactor, not a rewrite.    │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## Technical Alignment Summary

### What We Share with Microsoft's Stack

| Component | Microsoft Service | Spaarke Implementation | Alignment |
|-----------|------------------|----------------------|-----------|
| Vector DB | Azure AI Search | Azure AI Search | **Identical** |
| Embeddings | Azure OpenAI | Azure OpenAI | **Identical** |
| LLM | Azure OpenAI (GPT-4) | Azure OpenAI (GPT-4) | **Identical** |
| Document Processing | Document Intelligence | Document Intelligence | **Identical** |
| Identity | Entra ID | Entra ID | **Identical** |
| Hosting | Azure App Service | Azure App Service | **Identical** |

### What We Add Beyond Microsoft

| Capability | Microsoft Offering | Spaarke Addition |
|------------|-------------------|------------------|
| **Playbook System** | None | No-code AI workflow composition |
| **Domain-Specific UI** | Generic Copilot | Custom PCF controls |
| **Dataverse Integration** | Limited | Deep entity integration |
| **Multi-tenant RAG** | Unknown | tenantId-based isolation |
| **SharePoint Embedded** | Not integrated | Core document storage |

---

## Appendix: Microsoft Agent Framework

The Microsoft Agent Framework (open-source) is the recommended SDK for building agents:

| Feature | Description |
|---------|-------------|
| **Multi-model** | Works with Azure OpenAI, Anthropic, Ollama |
| **Multi-agent** | Orchestrate teams of specialized agents |
| **Agentic Loops** | ReAct, Plan-and-Execute, Tree of Thoughts |
| **Tool Integration** | MCP, OpenAPI, custom tools |
| **Deployment** | Foundry Agent Service, Azure Container Apps, self-hosted |

**Relationship to Semantic Kernel**: Agent Framework builds on Semantic Kernel but adds agent-specific patterns (loops, multi-agent, etc.).

**Spaarke Integration Option**: We could refactor our Tool Handlers to use Agent Framework patterns, enabling deployment to Foundry Agent Service while keeping Playbook system as the configuration layer.

---

## References

- [Azure AI Foundry Documentation](https://learn.microsoft.com/azure/ai-foundry/)
- [Foundry IQ Preview](https://learn.microsoft.com/azure/ai-foundry/knowledge/)
- [Foundry Agent Service](https://learn.microsoft.com/azure/ai-foundry/agents/)
- [Microsoft Agent Framework](https://github.com/microsoft/agentframework)
- [Spaarke AI Architecture](SPAARKE-AI-STRATEGY.md)

---

## Changelog

| Date | Change |
|------|--------|
| 2026-01-04 | Initial document created from strategic analysis session |
