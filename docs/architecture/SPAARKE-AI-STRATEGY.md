# Spaarke AI Strategy & Architecture

> **Version**: 2.1
> **Date**: January 4, 2026
> **Status**: Active
> **Authors**: Spaarke Engineering
> **Updated**: Clarified custom "Spaarke IQ" approach vs Microsoft Foundry adoption

---

## Executive Summary

Spaarke builds **custom AI capabilities** ("Spaarke IQ") integrated into the SharePoint Document Access Platform (SDAP). While we use the **same underlying Azure components** as Microsoft Foundry (Azure OpenAI, Azure AI Search, Document Intelligence), we maintain **full control** over our implementation, enabling unique capabilities like our **Playbook System** that Microsoft doesn't offer.

This strategy is **independent of M365 Copilot and Copilot Studio**, ensuring consistent AI features across both deployment models.

> **Note (January 2026)**: Microsoft Foundry (formerly Azure AI Foundry) represents Microsoft's "packaged" AI services. Our custom implementation uses the same backends but provides domain-specific capabilities and flexibility that managed services cannot match.

**Key Decisions:**
- Build **"Spaarke IQ"** - custom AI stack with full control over implementation
- Use **Azure primitives directly** (Azure OpenAI, Azure AI Search, Document Intelligence) rather than managed abstractions
- Maintain **Playbook System** as our unique differentiator (no Microsoft equivalent)
- **Optionally adopt** Foundry IQ/Agent Service post-GA where it adds value without sacrificing control
- Support both Spaarke-hosted and Customer-hosted deployments
- Design for multi-tenant isolation from day one

---

## Why Custom "Spaarke IQ" vs Microsoft Foundry

### The "Spaarke IQ" Approach

We are essentially building our own "IQ" service - custom AI capabilities using the same Azure components that Microsoft packages into Foundry:

```
┌─────────────────────────────────────────────────────────────────┐
│                    "SPAARKE IQ" (Custom Stack)                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   UNIQUE TO SPAARKE (No Microsoft Equivalent):                   │
│   ┌──────────────────────────────────────────────────────────┐  │
│   │  Playbook System - No-code AI workflow composition       │  │
│   │  • Domain experts create analysis workflows              │  │
│   │  • Link Actions, Skills, Knowledge, Tools                │  │
│   │  • Stored as Dataverse records (not code)                │  │
│   │  • Shareable, versionable, auditable                     │  │
│   └──────────────────────────────────────────────────────────┘  │
│                                                                  │
│   CUSTOM IMPLEMENTATIONS (Full Control):                         │
│   ┌────────────────┐  ┌────────────────┐  ┌────────────────┐   │
│   │ RagService     │  │ Orchestration  │  │ Tool Handlers  │   │
│   │ (Hybrid RAG)   │  │ Service        │  │ (C# plugins)   │   │
│   └────────────────┘  └────────────────┘  └────────────────┘   │
│                                                                  │
│   AZURE PRIMITIVES (Same as Foundry Backend):                    │
│   ┌────────────────┐  ┌────────────────┐  ┌────────────────┐   │
│   │ Azure OpenAI   │  │ Azure AI       │  │ Document       │   │
│   │ (GPT-4, embed) │  │ Search         │  │ Intelligence   │   │
│   └────────────────┘  └────────────────┘  └────────────────┘   │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

### Why Custom is Better (For Our Use Case)

| Aspect | Custom "Spaarke IQ" | Microsoft Foundry (Managed) |
|--------|--------------------|-----------------------------|
| **Playbook System** | ✅ Unique differentiator | ❌ No equivalent |
| **Multi-tenant RAG** | ✅ Custom tenantId filtering | ❓ Unknown/limited |
| **Domain-specific UI** | ✅ Custom PCF controls | ❌ Generic Copilot chat |
| **Chunking strategy** | ✅ Full control | ❌ Pre-built strategies |
| **Caching** | ✅ Redis with SHA256 keys | ❌ Managed (opaque) |
| **Cost optimization** | ✅ Direct control | ⚠️ Abstraction overhead |
| **Dataverse integration** | ✅ Deep entity integration | ❌ Limited |
| **Debugging** | ✅ Full observability | ⚠️ Black box |

### Where Foundry Adoption Makes Sense (Future)

We may adopt specific Foundry services post-GA where they add value:

| Service | Adoption Timeline | Use Case | Trade-off |
|---------|------------------|----------|-----------|
| **Foundry IQ** | Post-GA (mid-2026) | Simplify RAG for simple queries | Less chunking/caching control |
| **Foundry Agent Service** | Post-GA (2026+) | Runtime hosting option | Keep Playbook as config layer |
| **Model Router** | When GA | Cost optimization | Adds latency |

**Principle**: Adopt managed services only when they **add clear value** without sacrificing our differentiators (Playbook system, multi-tenant isolation, domain-specific UX).

> **Detailed Analysis**: See [Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md](Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md) for comprehensive comparison and migration paths.

---

## Table of Contents

- [Why Custom "Spaarke IQ" vs Microsoft Foundry](#why-custom-spaarke-iq-vs-microsoft-foundry)
1. [Deployment Models](#1-deployment-models)
2. [Architecture Overview](#2-architecture-overview)
3. [ADR Alignment](#3-adr-alignment)
4. [BFF Orchestration Pattern](#4-bff-orchestration-pattern)
5. [Azure Services & Components](#5-azure-services--components)
6. [Application Components](#6-application-components)
7. [Use Cases & Solutions](#7-use-cases--solutions)
8. [Data Flow & Pipelines](#8-data-flow--pipelines)
9. [Security & Compliance](#9-security--compliance)
10. [Implementation Details](#10-implementation-details)
11. [Implementation Roadmap](#11-implementation-roadmap)
12. [Cost Model](#12-cost-model)
13. [Future Considerations](#13-future-considerations)

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

#### Customer Management via Microsoft Foundry

With Model 2 (Customer-hosted), customers have **full control** over their AI resources through Microsoft Foundry portal ([ai.azure.com](https://ai.azure.com)):

| Management Capability | Description |
|----------------------|-------------|
| **Model Deployments** | Create, update, delete model deployments (e.g., gpt-4.1-mini) |
| **Quota Management** | Request and manage TPM (tokens per minute) limits |
| **Usage Monitoring** | View token consumption, costs, and usage patterns |
| **Content Filtering** | Configure content moderation policies |
| **Prompt Engineering** | Test and refine prompts in Foundry Playground |
| **Model Selection** | Switch between models (GPT-4.1, GPT-5, Claude, Mistral) |
| **Fine-tuning** | Fine-tune models on domain-specific data (future) |

**Benefits of Foundry-based Customer Management:**
- Self-service: Customers can adjust configurations without Spaarke involvement
- Transparency: Direct visibility into costs and usage
- Compliance: Customer maintains control over data governance
- Flexibility: Customers can experiment with newer models as released

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

> **Implementation Details**: For detailed code structure, DI patterns, and endpoint implementations, see [SPAARKE-AI-ARCHITECTURE.md](../../ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md)

### 2.1 Azure Components (Shared with Microsoft Foundry)

Spaarke uses the **same Azure primitives** that Microsoft packages into Foundry, but we access them directly for maximum control:

| Azure Component | Microsoft Foundry Name | Spaarke Implementation | Status |
|-----------------|----------------------|------------------------|--------|
| **Azure OpenAI** | Foundry Models | Direct SDK usage | ✅ Active |
| **Azure AI Search** | Foundry IQ (backend) | Custom RagService | ✅ Active |
| **Document Intelligence** | (part of Foundry) | Direct SDK usage | ✅ Active |
| **Azure Redis** | N/A | Custom EmbeddingCache | ✅ Active |

### 2.2 Microsoft Foundry Services (Future Consideration)

Microsoft Foundry provides "managed" abstractions over Azure components. We may adopt these post-GA where they add value:

| Foundry Service | Purpose | Spaarke Status | Decision |
|-----------------|---------|----------------|----------|
| **Foundry IQ** | Managed RAG-as-a-Service | 🔮 Future | Evaluate post-GA for simple queries |
| **Foundry Agent Service** | Hosted agent runtime | 🔮 Future | Potential runtime option (keep Playbook system) |
| **Model Router** | Dynamic model selection | 🔮 Future | Cost optimization when GA |
| **Foundry Tools (MCP)** | Tool catalog, connectors | 🔮 Future | May expose SPE/Dataverse as MCP tools |
| **Foundry Control Plane** | Governance, observability | ⚠️ Evaluate | May adopt for enterprise governance |

**Current Approach**: Build custom ("Spaarke IQ") now, adopt Foundry services selectively post-GA where they add measurable value without sacrificing our unique capabilities.

### 2.3 Layered Architecture

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
│                         BFF API (Sprk.Bff.Api)                              │
│  ┌──────────────────────────────────────────────────────────────────────┐  │
│  │  AI Endpoints                                                        │  │
│  │  • POST /api/ai/chat              - Conversational Q&A               │  │
│  │  • POST /api/ai/search            - Semantic document search         │  │
│  │  • POST /api/ai/document-intelligence - Document analysis & extraction │  │
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
│  │                   Microsoft Agent Framework                          │  │
│  │           (Successor to Semantic Kernel + AutoGen)                   │  │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐  ┌────────────┐    │  │
│  │  │ Chat       │  │ RAG        │  │ Prompt     │  │ Tool       │    │  │
│  │  │ Completion │  │ Pipeline   │  │ Templates  │  │ Calling    │    │  │
│  │  └────────────┘  └────────────┘  └────────────┘  └────────────┘    │  │
│  │  ┌────────────┐  ┌────────────┐  ┌────────────┐                    │  │
│  │  │ Multi-Agent│  │ AG-UI      │  │ OpenTel    │                    │  │
│  │  │ Orchestr.  │  │ Protocol   │  │ Tracing    │                    │  │
│  │  └────────────┘  └────────────┘  └────────────┘                    │  │
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
│                      MICROSOFT FOUNDRY LAYER                                │
├─────────────────────────────────────────────────────────────────────────────┤
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐               │
│  │ Foundry Models │  │ Foundry IQ     │  │ Azure Document │               │
│  │ (Azure OpenAI) │  │ (AI Search)    │  │ Intelligence   │               │
│  └────────────────┘  └────────────────┘  └────────────────┘               │
│  ┌────────────────┐  ┌────────────────┐  ┌────────────────┐               │
│  │ SharePoint     │  │ Dataverse      │  │ Azure Key      │               │
│  │ Embedded (SPE) │  │                │  │ Vault          │               │
│  └────────────────┘  └────────────────┘  └────────────────┘               │
└─────────────────────────────────────────────────────────────────────────────┘
```

---

## 3. ADR Alignment

All AI features must comply with existing Spaarke Architecture Decision Records:

| ADR | Requirement | AI Implementation |
|-----|-------------|-------------------|
| **ADR-001** | Minimal API + BackgroundService; no Azure Functions | AI endpoints via Minimal API; indexing via `BackgroundService` + Service Bus |
| **ADR-003** | Lean authorization with UAC and file storage seams | AI access controlled by existing `AuthorizationService`; no new service layers |
| **ADR-004** | Async job contract and uniform processing | AI indexing uses standard `JobContract` with `JobType: "ai-indexing"` |
| **ADR-007** | SpeFileStore facade; no Graph SDK leakage | AI services use `SpeFileStore` for document access; no direct Graph calls |
| **ADR-008** | Endpoint filters for authorization | `AiAuthorizationFilter` for per-resource AI access checks |
| **ADR-009** | Redis-first caching; no hybrid L1 without proof | Embeddings and search results cached in Redis with appropriate TTLs |
| **ADR-010** | DI minimalism (≤15 registrations) | ≤3 new AI service registrations: `AiSearchService`, `AiChatService`, `EmbeddingService` |

### 3.1 AI-Specific Caching Strategy (ADR-009)

| Data Type | TTL | Cache Key Pattern | Rationale |
|-----------|-----|-------------------|----------|
| Document embeddings | 24 hours | `{customerId}:ai:embed:{docHash}` | Deterministic, expensive to compute |
| Search results | 5 minutes | `{customerId}:ai:search:{queryHash}` | Balance freshness vs. cost |
| Document summaries | 1 hour | `{customerId}:ai:summary:{docId}` | Expensive, document rarely changes |
| Chat context | Request only | N/A (not cached) | Personalized, not cacheable |
| Metadata extractions | 1 hour | `{customerId}:ai:extract:{docId}` | Expensive, document rarely changes |

### 3.2 AI Job Contract (ADR-004)

```json
{
  "JobId": "guid",
  "JobType": "ai-indexing",
  "SubjectId": "document-guid",
  "CorrelationId": "request-guid",
  "IdempotencyKey": "doc-{docId}-v{version}",
  "Attempt": 1,
  "MaxAttempts": 3,
  "Payload": {
    "customerId": "customer-guid",
    "containerId": "spe-container-id",
    "action": "index|reindex|delete"
  }
}
```

---

## 4. BFF Orchestration Pattern

### 4.1 Single BFF Architecture

AI capabilities are implemented as **extensions to the existing `Sprk.Bff.Api`**, not as a separate microservice. The BFF serves as the **orchestration layer** that coordinates Dataverse, Azure, SPE, and AI services.

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                              Sprk.Bff.Api                                   │
│                    "Backend for Frontend - Orchestration Layer"             │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                             │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │                     Unified Access Control (UAC)                    │   │
│  │                   Entra ID + Dataverse Permissions                  │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                    │                                        │
│         ┌──────────────────────────┼──────────────────────────┐            │
│         ▼                          ▼                          ▼            │
│  ┌─────────────┐           ┌─────────────┐           ┌─────────────┐       │
│  │     SPE     │           │  Dataverse  │           │   Azure AI  │       │
│  │  (Graph)    │           │   (CRUD)    │           │  (OpenAI)   │       │
│  └─────────────┘           └─────────────┘           └─────────────┘       │
│         │                          │                          │            │
│         └──────────────────────────┴──────────────────────────┘            │
│                                    │                                        │
│                          Orchestrated Operations                            │
│  ┌─────────────────────────────────────────────────────────────────────┐   │
│  │  Example: AI Document Summary                                       │   │
│  │  1. Authenticate via UAC (Entra + Dataverse permissions)            │   │
│  │  2. Get file content from SPE via SpeFileStore                      │   │
│  │  3. Extract text via Document Intelligence                          │   │
│  │  4. Generate summary via Azure OpenAI                               │   │
│  │  5. Update sprk_document record in Dataverse                        │   │
│  └─────────────────────────────────────────────────────────────────────┘   │
│                                                                             │
└─────────────────────────────────────────────────────────────────────────────┘
```

### 4.2 Why Single BFF (Not Separate AI Service)

| Factor | Single BFF | Separate AI Service |
|--------|------------|---------------------|
| **Auth reuse** | ✅ UAC already wired | ❌ Wire UAC again |
| **File access** | ✅ SpeFileStore exists | ❌ Duplicate or call BFF |
| **Deployment** | ✅ One artifact | ❌ Two artifacts |
| **Latency** | ✅ In-process | ❌ Network hop |
| **ADR compliance** | ✅ ADR-001 | ⚠️ May conflict |

**When to reconsider**: If AI request volume exceeds 10x SDAP volume, teams separate, or compliance requires isolation.

### 4.3 Tool-Focused Implementation Approach

AI features are built as **focused, self-contained tools** rather than a generic framework. Each tool has specific:
- Input/output contracts
- BFF endpoints
- UI components (if needed)
- Dataverse fields (if persisted)

**Rationale**: 
- YAGNI - Don't build abstractions for hypothetical future tools
- First tool teaches what's actually reusable
- Faster time-to-value for each feature
- Shared utilities extracted organically after 2+ tools exist

| Tool | Integration | Output |
|------|-------------|--------|
| **Document Summary** | SDAP upload flow | `sprk_filesummary` field |
| **Translate** (future) | On-demand | New file in SPE |
| **Draft Response** (future) | On-demand dialog | Text to UI |
| **Extract Data** (future) | On-demand | JSON fields |

### 4.4 Shared Infrastructure (Available to All Tools)

| Component | Purpose | Location |
|-----------|---------|----------|
| `SpeFileStore` | File download from SPE | Existing |
| `DataverseClient` | Dataverse CRUD | Existing |
| `OpenAiClient` | Azure OpenAI calls | New (shared) |
| `TextExtractorService` | Text extraction | New (shared) |
| Redis cache | Cache extracted text, embeddings | Existing |
| Service Bus | Background job queue | Existing |

---

## 5. Azure Services & Components

### 4.1 Microsoft Foundry Services

| Service | Purpose | Model 1 | Model 2 |
|---------|---------|---------|---------|
| **Foundry Models (Azure OpenAI)** | LLM for chat, summarization, extraction | Spaarke subscription | Customer subscription |
| **Foundry IQ (Azure AI Search)** | Dynamic RAG, vector search, grounding | Spaarke subscription | Customer subscription |
| **Azure Document Intelligence** | PDF/image text extraction, layout analysis | Spaarke subscription | Customer subscription |

### 4.2 Foundry Models Configuration

> **Foundry Models** provides access to 11,000+ models including OpenAI, Anthropic Claude, Mistral, and more. The **Model Router** (GA) can dynamically select the best model per task.

| Deployment | Model | Purpose | TPM (Recommended) |
|------------|-------|---------|-------------------|
| `gpt-4.1-turbo` | gpt-4.1-turbo | Primary chat/reasoning | 80K |
| `gpt-4.1` | gpt-4.1 | Fast responses, multimodal | 150K |
| `gpt-4.1-mini` | gpt-4.1-mini | Classification, simple tasks, summarization | 200K |
| `gpt-5` | gpt-5 | Highest quality, latest capabilities | 100K |
| `text-embedding-3-large` | text-embedding-3-large | Document embeddings | 350K |

> **Note:** Model deployment names should match what's configured in [Microsoft Foundry portal](https://ai.azure.com). Customers using BYOK (Model 2) manage their own deployments through Foundry.

**Future Model Options (Available in Foundry):**
| Model Provider | Models | Consideration |
|----------------|--------|---------------|
| **Anthropic** | Claude Sonnet 4.5, Opus 4.5, Haiku 4.5 | Alternative for specific tasks |
| **Mistral** | Mistral Large 3 | Open-weight, cost-effective |
| **Cohere** | Command R+, Embed | Specialized embedding/retrieval |

### 4.3 Foundry IQ Configuration

> **Foundry IQ** (Public Preview) reimagines RAG as a dynamic reasoning process. It provides a single grounding API that simplifies orchestration while respecting user permissions and data classifications.

**Key Foundry IQ Features:**
- Simplified cross-source grounding (no upfront indexing required)
- Multi-source selection with iterative retrieval
- Reflection to dynamically improve response quality
- Foundry Agent Service integration

**Azure AI Search Configuration (Foundry IQ Backend):**

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

### 4.4 Azure Document Intelligence

| Feature | Usage |
|---------|-------|
| **Read API** | Extract text from PDFs, images, Office docs |
| **Layout API** | Understand document structure (tables, sections) |
| **Prebuilt Models** | Invoice, receipt, ID extraction (future) |

### 4.5 Foundry Tools & MCP Integration

> **Foundry Tools** (Public Preview) enables agents to securely access business systems via Model Context Protocol (MCP).

| Capability | Spaarke Usage |
|------------|---------------|
| **MCP Tools Catalog** | Expose SPE/Dataverse APIs as MCP tools |
| **1,400+ Connectors** | SAP, Salesforce, UiPath integration (future) |
| **Built-in Tools** | Transcription, translation, document processing |
| **API Management** | Expose existing APIs as MCP tools |

### 4.6 Foundry Agent Service

> **Foundry Agent Service** provides hosted agents, multi-agent workflows, and memory for building sophisticated AI systems.

| Feature | Status | Spaarke Usage |
|---------|--------|---------------|
| **Hosted Agents** | Public Preview | Run Spaarke agents in managed environment |
| **Multi-Agent Workflows** | Public Preview | Orchestrate document processing agents |
| **Memory** | Public Preview | Retain context across sessions |
| **M365 Integration** | Public Preview | Deploy agents to M365 apps (Model 2) |

### 4.7 Foundry Control Plane

> **Foundry Control Plane** (Public Preview) provides unified identity, controls, observability, and security.

| Capability | Description |
|------------|-------------|
| **Entra Agent ID** | Durable identity for agents |
| **Guardrails** | Input/output/tool interaction controls |
| **Observability** | OpenTelemetry tracing, evaluations, dashboards |
| **Security** | Defender + Purview integration, risk detection |
| **Fleet Operations** | Health, cost, performance monitoring |

### 4.8 Supporting Azure Services

| Service | Purpose |
|---------|---------|
| **Azure Key Vault** | Store API keys, connection strings |
| **Azure Service Bus** | Queue document indexing jobs |
| **Azure Redis Cache** | Cache embeddings, search results |
| **Azure Monitor / App Insights** | AI telemetry, usage tracking |
| **Azure Blob Storage** | Temporary document processing |

---

## 5. Application Components

### 5.1 Microsoft Agent Framework

> **Microsoft Agent Framework** is the unified SDK from the Semantic Kernel and AutoGen teams. It's the recommended approach for new agentic AI projects.

**Key Features:**
| Feature | Description |
|---------|-------------|
| **Multi-Agent Orchestration** | Coordinate specialized agents |
| **AG-UI Protocol** | Agent-to-UI communication standard |
| **DevUI** | Development and debugging interface |
| **OpenTelemetry** | Built-in observability |
| **Migration Path** | Guides from Semantic Kernel available |

**Migration Note:** For existing Semantic Kernel projects, Microsoft provides migration guides:
- [.NET Migration Guide](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/SemanticKernelMigration)
- [Python Migration Guide](https://github.com/microsoft/agent-framework/tree/main/python/samples/semantic-kernel-migration)

### 5.2 Backend Components (C#/.NET 8)

| Component | Location | Description |
|-----------|----------|-------------|
| **Spaarke.AI** | `src/server/shared/Spaarke.AI/` | Core AI library |
| **AI Endpoints** | `src/server/api/Sprk.Bff.Api/Api/AIEndpoints.cs` | Minimal API endpoints |
| **AI Services** | `src/server/api/Sprk.Bff.Api/Services/AI/` | AI service implementations |

**Core Interfaces:**
```csharp
// AI Provider Abstraction
public interface IAIProvider
{
    Task<string> CompleteAsync(string prompt, CompletionOptions options, CancellationToken ct);
    Task<float[]> EmbedAsync(string text, CancellationToken ct);
    IAsyncEnumerable<string> StreamCompleteAsync(string prompt, CompletionOptions options, CancellationToken ct);
}

// Vector Search (Foundry IQ / Azure AI Search)
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

### 5.3 Frontend Components (TypeScript/React)

| Component | Location | Description |
|-----------|----------|-------------|
| **AI Chat PCF** | `src/client/pcf/AIChatControl/` | Conversational interface |
| **AI Search PCF** | `src/client/pcf/AISearchControl/` | Semantic search UI |
| **AI Insights Panel** | `src/client/pcf/AIInsightsPanel/` | Document insights sidebar |
| **AI Components** | `src/client/shared/Spaarke.UI.Components/ai/` | Shared AI UI components |

### 5.4 Dataverse Components

| Entity | Purpose |
|--------|---------|
| `sprk_AIConfiguration` | AI provider settings (endpoints, keys) |
| `sprk_AIConversation` | Chat session storage |
| `sprk_AIMessage` | Chat message history |
| `sprk_AIPromptTemplate` | Custom prompt templates |
| `sprk_AIUsageLog` | Usage tracking for billing |

### 5.5 Background Workers

| Worker | Purpose |
|--------|---------|
| `DocumentIndexingWorker` | Process document indexing queue |
| `EmbeddingWorker` | Generate embeddings for new/updated docs |
| `IndexCleanupWorker` | Remove deleted document chunks |

---

## 6. Use Cases & Solutions

### 6.1 Document Q&A (RAG)

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

### 6.2 Semantic Document Search

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

### 6.3 Document Summarization

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

### 6.4 Metadata Extraction

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

### 6.5 Document Classification

**Description:** Auto-categorize documents based on content.

| Category | Examples |
|----------|----------|
| **Document Type** | Contract, Agreement, Letter, Invoice, Report |
| **Legal Category** | NDA, MSA, SOW, Amendment, Addendum |
| **Priority** | High, Medium, Low |
| **Confidentiality** | Public, Internal, Confidential, Restricted |

**Approach:**
- Few-shot classification with GPT-4.1-mini
- Customer-configurable categories
- Confidence scoring

---

### 6.6 Multi-Document Analysis

**Description:** Compare, contrast, or analyze multiple documents together.

| Analysis Type | Description |
|---------------|-------------|
| **Comparison** | Side-by-side clause comparison between contracts |
| **Conflict Detection** | Identify conflicting terms across documents |
| **Coverage Analysis** | Check if all required clauses are present |
| **Timeline** | Extract and visualize key dates across documents |

---

### 6.7 Smart Suggestions

**Description:** Proactive AI suggestions based on context.

| Context | Suggestion |
|---------|------------|
| Viewing expired contract | "This contract expired 30 days ago. Would you like to find the renewal?" |
| Opening matter | "3 documents need review. Would you like a summary?" |
| Searching | "Did you mean documents related to [matter name]?" |

---

## 7. Data Flow & Pipelines

### 7.1 Document Indexing Pipeline

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

### 7.2 RAG Query Pipeline

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

## 8. Security & Compliance

### 8.1 Data Security

| Concern | Mitigation |
|---------|------------|
| **Data at rest** | Azure AI Search encrypted, OpenAI stateless |
| **Data in transit** | TLS 1.3 for all API calls |
| **Data residency** | Configure Azure region per customer needs |
| **PII handling** | Option to mask PII before LLM processing |
| **No model training** | Azure OpenAI does not train on customer data |

### 8.2 Access Control

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

### 8.3 Audit & Compliance

| Audit Point | Captured Data |
|-------------|---------------|
| AI queries | Query text, user, timestamp, response summary |
| Document access | Which documents were retrieved for context |
| Token usage | Input/output tokens per request |
| Errors | Failed requests, rate limits, errors |

---

## 9. Implementation Details

> **Full Implementation Guide**: See [SPAARKE-AI-ARCHITECTURE.md](../../ai-knowledge/guides/SPAARKE-AI-ARCHITECTURE.md) for detailed code structure and patterns.

### 9.1 BFF API Configuration

Add to `appsettings.json`:

```json
{
  "AiServices": {
    "OpenAi": {
      "Endpoint": "${OPENAI_ENDPOINT}",
      "ApiKey": "@Microsoft.KeyVault(VaultName=${KEY_VAULT_NAME};SecretName=openai-api-key)",
      "ChatModel": "gpt-4.1",
      "EmbeddingModel": "text-embedding-3-large",
      "MaxTokensPerRequest": 4000
    },
    "AiSearch": {
      "Endpoint": "${AI_SEARCH_ENDPOINT}",
      "ApiKey": "@Microsoft.KeyVault(VaultName=${KEY_VAULT_NAME};SecretName=aisearch-admin-key)",
      "IndexNamePrefix": "",
      "SemanticConfigName": "default"
    },
    "DocIntelligence": {
      "Endpoint": "${DOC_INTELLIGENCE_ENDPOINT}",
      "ApiKey": "@Microsoft.KeyVault(VaultName=${KEY_VAULT_NAME};SecretName=docintel-key)"
    },
    "Caching": {
      "EmbeddingTtlMinutes": 1440,
      "SearchResultTtlSeconds": 300,
      "SummaryTtlMinutes": 60
    },
    "RateLimiting": {
      "RequestsPerMinute": 100,
      "TokensPerMinute": 50000
    }
  }
}
```

### 9.2 Service Bus Queues

| Queue Name | Purpose | Message Type |
|------------|---------|--------------|
| `ai-indexing` | Document vectorization | `JobContract` with `JobType: "ai-indexing"` |
| `document-indexing` | Metadata extraction | `JobContract` with `JobType: "document-indexing"` |

### 9.3 DI Registrations (ADR-010)

```csharp
// Program.cs - AI Services (3 registrations)
builder.Services.AddSingleton<AiSearchService>();
builder.Services.AddSingleton<AiChatService>();
builder.Services.AddSingleton<EmbeddingService>();

// Job handler (existing pattern)
builder.Services.AddScoped<IJobHandler, AiIndexingJobHandler>();
```

### 9.4 Per-Customer Rate Limiting

```csharp
// Program.cs - Rate limiting for AI endpoints
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("ai-per-customer", context =>
        RateLimitPartition.GetTokenBucketLimiter(
            context.User.FindFirst("tenant_id")?.Value ?? "anonymous",
            _ => new TokenBucketRateLimiterOptions
            {
                TokenLimit = 100,
                ReplenishmentPeriod = TimeSpan.FromMinutes(1),
                TokensPerPeriod = 50
            }));
});

// Apply to AI endpoints
app.MapGroup("/api/ai")
   .RequireRateLimiting("ai-per-customer");
```

### 9.5 Azure Resource Naming (Model 1 vs Model 2)

| Resource | Model 1 (Shared) | Model 2 (Customer) |
|----------|------------------|---------------------|
| Azure OpenAI | `sprkshared{env}-openai` | `sprk{custId}{env}-openai` |
| AI Search | `sprkshared{env}-search` | `sprk{custId}{env}-search` |
| Doc Intelligence | `sprkshared{env}-docintel` | `sprk{custId}{env}-docintel` |
| Index Name | `{customerId}-documents` | `documents` |

---

## 10. Implementation Roadmap

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

## 11. Cost Model

### 11.1 Azure OpenAI Pricing (Estimated)

> **Note:** Pricing varies by model and is subject to change. Check [Azure OpenAI pricing](https://azure.microsoft.com/pricing/details/cognitive-services/openai-service/) for current rates.

| Model | Price per 1K tokens | Typical usage |
|-------|---------------------|---------------|
| GPT-4.1 Input | $0.01 | RAG context (4K tokens avg) |
| GPT-4.1 Output | $0.03 | Responses (500 tokens avg) |
| GPT-4.1-mini Input | $0.005 | Fast queries, summarization |
| GPT-4.1-mini Output | $0.015 | Fast responses |
| text-embedding-3-large | $0.00013 | Document indexing |

**Example: 1 RAG query**
- Input: 4,000 tokens (context) = $0.04
- Output: 500 tokens = $0.015
- **Total: ~$0.055 per query**

### 11.2 Azure AI Search Pricing

| SKU | Monthly Cost | Documents | Queries/sec |
|-----|--------------|-----------|-------------|
| S1 | ~$250 | 2M chunks | 50 QPS |
| S2 | ~$1,000 | 10M chunks | 100 QPS |

### 11.3 Model 1 Cost Recovery

| Approach | Description |
|----------|-------------|
| **Bundled** | Include AI in subscription tier |
| **Metered** | Track per-query, bill monthly |
| **Hybrid** | Included queries + overage |

---

## 13. Future Considerations

> **Detailed Analysis**: See [Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md](Spaarke-Microsoft-IQ-ADOPTION-ANALYSIS.md) for comprehensive comparison of custom vs managed approaches, migration paths, and decision framework.

### 13.1 Microsoft Foundry Adoption Strategy

Our approach is "**custom first, adopt selectively**":

```
┌─────────────────────────────────────────────────────────────────┐
│                    Adoption Decision Framework                   │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│   NOW (Jan 2026)      June 2026 (Launch)      Post-GA (2026+)   │
│   ──────────────      ─────────────────      ───────────────    │
│   Custom "Spaarke IQ" Launch with custom     Evaluate Foundry   │
│   ├─ RagService       ├─ Full control        ├─ Foundry IQ      │
│   ├─ Orchestration    ├─ Proven architecture │   for simple RAG │
│   ├─ Tool Handlers    ├─ Playbook System     ├─ Agent Service   │
│   └─ Playbook System  └─ Multi-tenant        │   for runtime    │
│                                              └─ Model Router    │
│                                                  for cost opt   │
│                                                                  │
│   ALWAYS KEEP: Playbook System (our differentiator)              │
│                Custom PCF Controls (domain-specific UX)          │
│                Multi-tenant RAG isolation                        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

| Foundry Service | Adoption Decision | Rationale |
|-----------------|-------------------|-----------|
| **Foundry IQ** | ⏳ Evaluate post-GA | May simplify RAG for basic queries; keep custom for advanced |
| **Foundry Agent Service** | ⏳ Evaluate post-GA | Runtime option; keep Playbook as config layer above it |
| **Model Router** | ⏳ Evaluate when GA | Cost optimization without changing architecture |
| **Foundry Control Plane** | ✅ Consider early | Governance/compliance without changing code |

### 13.2 M365 Copilot Integration (Model 2 Only)

If customers with M365 Copilot licenses want integration:
- **Graph Connector**: Index SPE documents to Microsoft Search
- **Declarative Agent**: Spaarke-specific agent in customer's Copilot
- **Plugin**: Expose Spaarke AI as Copilot plugin
- **Agent 365**: Deploy from Foundry Agent Service to M365 (optional)

### 13.3 Foundry Agent Service (Future Runtime Option)

| Feature | Timeline | Description |
|---------|----------|-------------|
| **Hosted Agents** | Post-GA 2026 | Optional runtime for our tool handlers |
| **Multi-Agent Workflows** | 2026+ | Document review, contract analysis agents |
| **Memory** | 2026+ | Cross-session context for personalization |
| **Foundry Tools (MCP)** | 2026+ | Expose Spaarke APIs via MCP protocol |

**Key Principle**: If we adopt Foundry Agent Service, our **Playbook System remains** as the configuration layer above it. Foundry would be the runtime; Playbooks define the workflows.

### 13.4 Advanced Capabilities

| Capability | Timeline | Description |
|------------|----------|-------------|
| **Multimodal** | 2026 | Process images, diagrams in documents (GPT-4/5 with vision) |
| **Agentic workflows** | 2026+ | Multi-step reasoning, tool use via Agent Framework |
| **Model Router** | Post-GA | Dynamic model selection per task (cost/quality) |
| **Foundry Local** | 2027+ | Edge deployment for offline/privacy scenarios |

### 13.5 Technology Watch

| Technology | Interest | Notes |
|------------|----------|-------|
| **Foundry IQ GA** | High | Evaluate for RAG simplification post-GA |
| **Foundry Agent Service GA** | High | Evaluate as optional runtime |
| **GPT-5+ / o3** | High | Performance improvements, latest capabilities |
| **Anthropic Claude (via Foundry)** | Medium | Alternative for specific tasks |
| **Mistral Large 3** | Medium | Cost-effective, open-weight |
| **Foundry Local (Android/iOS)** | Low | Mobile offline AI (future) |
| **Model Router** | Medium | Automatic model selection for cost optimization |

### 13.6 Migration Path: Semantic Kernel → Microsoft Agent Framework

Microsoft Agent Framework is the successor to Semantic Kernel. Current guidance:

| Scenario | Recommendation |
|----------|----------------|
| **New projects** | Start with Microsoft Agent Framework if can wait for GA |
| **Existing SK projects** | Continue with SK until Agent Framework reaches GA |
| **Need SK features** | OK to use SK; migration path will be provided |
| **Need new Agent Framework features** | Can start with Agent Framework in Preview |

**Support Timeline:**
- Semantic Kernel will be supported for at least 1 year after Agent Framework GA
- Critical bugs and security fixes will continue
- Most new features will be Agent Framework only

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

### Microsoft Foundry
- [Microsoft Foundry Portal](https://ai.azure.com/)
- [Microsoft Foundry Blog Post (Ignite 2025)](https://azure.microsoft.com/en-us/blog/microsoft-foundry-scale-innovation-on-a-modular-interoperable-and-secure-agent-stack/)
- [Foundry IQ Documentation](https://aka.ms/IgniteFoundryIQ)
- [Foundry Agent Service Documentation](https://aka.ms/IgniteFoundryAgents)
- [Foundry Tools Documentation](https://aka.ms/IgniteFoundryTools)
- [Foundry Control Plane](https://aka.ms/IgniteFoundryControlPlane)

### Microsoft Agent Framework
- [Microsoft Agent Framework](https://aka.ms/AgentFramework)
- [Agent Framework Documentation](https://aka.ms/AgentFramework/Docs)
- [Semantic Kernel Migration (.NET)](https://github.com/microsoft/agent-framework/tree/main/dotnet/samples/SemanticKernelMigration)
- [Semantic Kernel Migration (Python)](https://github.com/microsoft/agent-framework/tree/main/python/samples/semantic-kernel-migration)
- [Semantic Kernel Documentation](https://learn.microsoft.com/en-us/semantic-kernel/) (legacy, still supported)

### Azure AI Services
- [Azure OpenAI Documentation](https://learn.microsoft.com/en-us/azure/ai-services/openai/)
- [Azure AI Search Documentation](https://learn.microsoft.com/en-us/azure/search/)
- [Azure Document Intelligence](https://learn.microsoft.com/en-us/azure/ai-services/document-intelligence/)

### Learning Resources
- [Microsoft Learn: Develop AI Agents on Azure](https://learn.microsoft.com/en-us/training/paths/develop-ai-agents-on-azure/)
- [AI Agents for Beginners (GitHub)](https://github.com/microsoft/ai-agents-for-beginners)
- [AI Show Demos](https://aka.ms/AgentFramework/AIShow)

---

*Document Owner: Spaarke Engineering*  
*Review Cycle: Quarterly*
