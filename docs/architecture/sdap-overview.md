# SDAP System Overview

> **SDAP**: **Spaarke Data & AI Platform** (formerly "SharePoint Document Access Platform")
> **Last Updated**: March 4, 2026
> **Applies To**: Any work involving the Spaarke BFF API, document management, AI services, or platform integrations

---

## TL;DR

SDAP is an enterprise platform integrating Dataverse with SharePoint Embedded (SPE), Azure AI services, and Microsoft 365. The single BFF API (`Sprk.Bff.Api`) serves as the unified backend, orchestrating 7 functional domains: document management, AI/RAG, Office Add-ins, email/communication, finance intelligence, workspace analytics, and background processing. Files stored in SPE containers, metadata in Dataverse, intelligence powered by Azure OpenAI + AI Search.

**Architecture**: Structured monolith — one App Service hosting all HTTP endpoints + background workers. Designed for operational simplicity at current team scale, with internal module boundaries for future extraction if needed.

---

## Applies When

- Building features that upload, download, or preview documents
- Adding document support to a new Dataverse entity
- Working with AI features (chat, analysis, RAG search, playbooks)
- Building or modifying Office Add-in integrations
- Working with email-to-document automation
- Building workspace/portfolio intelligence features
- Working with finance/invoice processing
- Modifying background job processing
- Understanding authentication flows for any platform operation
- Troubleshooting any BFF API behavior

---

## Platform Architecture

### High-Level Component Model

```
┌──────────────────────────────────────────────────────────────────────────────┐
│  CLIENT LAYER                                                                │
│                                                                              │
│  ┌──────────────────┐  ┌──────────────────┐  ┌──────────────────────────┐   │
│  │ PCF Controls     │  │ React Code Pages │  │ Office Add-ins          │   │
│  │ (Dataverse Forms)│  │ (Standalone)     │  │ (Outlook, Word)         │   │
│  │ React 16/17      │  │ React 18         │  │ React 18                │   │
│  └────────┬─────────┘  └────────┬─────────┘  └────────────┬─────────────┘   │
│           │                     │                          │                 │
│           └─────────────────────┼──────────────────────────┘                 │
│                                 │                                            │
│                    HTTPS + Bearer Token (Azure AD)                           │
└─────────────────────────────────┼────────────────────────────────────────────┘
                                  │
┌─────────────────────────────────┼────────────────────────────────────────────┐
│  BFF API (Sprk.Bff.Api)        │        .NET 8 Minimal API                  │
│  spe-api-dev-67e2xz.azurewebsites.net                                       │
│                                                                              │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌──────────────────┐  │
│  │   SPE    │ │    AI    │ │  Office  │ │  Email / │ │  Finance /       │  │
│  │  Module  │ │  Module  │ │  Module  │ │  Comms   │ │  Workspace       │  │
│  │          │ │          │ │          │ │  Module  │ │  Modules         │  │
│  └────┬─────┘ └────┬─────┘ └────┬─────┘ └────┬─────┘ └────────┬─────────┘  │
│       │             │            │             │                │            │
│  ┌────┴─────────────┴────────────┴─────────────┴────────────────┴─────────┐  │
│  │  SHARED INFRASTRUCTURE                                                 │  │
│  │  Auth (JWT + OBO) │ Redis Cache │ Polly Resilience │ OpenTelemetry    │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
│                                                                              │
│  ┌────────────────────────────────────────────────────────────────────────┐  │
│  │  BACKGROUND PROCESSING                                                 │  │
│  │  ServiceBusJobProcessor → 13+ Job Handlers                            │  │
│  └────────────────────────────────────────────────────────────────────────┘  │
└──────────────────────────────────────────────────────────────────────────────┘
           │              │              │              │
           ↓ OBO Token    ↓ API Key      ↓ Client Cred  ↓ Conn String
┌──────────────────┐ ┌──────────────┐ ┌──────────────┐ ┌──────────────┐
│ Microsoft Graph  │ │ Azure OpenAI │ │ Dataverse    │ │ Azure        │
│ (SPE, Mail,     │ │ AI Search    │ │ Web API      │ │ Service Bus  │
│  OneDrive)      │ │ Doc Intel    │ │              │ │ Redis Cache  │
└──────────────────┘ └──────────────┘ └──────────────┘ └──────────────┘
           │                                    │
           ↓                                    ↓
┌──────────────────┐              ┌──────────────────────────┐
│ SPE Containers   │              │ Dataverse Tables          │
│ (File Storage)   │              │ (sprk_document, sprk_    │
│                  │              │  matter, sprk_analysis,  │
│                  │              │  sprk_chatsession, etc.) │
└──────────────────┘              └──────────────────────────┘
```

### Domain Modules

The BFF API is organized into 7 functional domains, each with its own endpoint group, services, and external dependencies.

| Domain | Route Prefix | Endpoints | Key Services | External Dependencies |
|--------|-------------|-----------|--------------|----------------------|
| **SPE / Documents** | `/api/containers`, `/api/documents`, `/api/obo` | ~25 | SpeFileStore, UploadSessionManager | Graph API (OBO + App) |
| **AI Platform** | `/api/ai/*` | ~40+ | ChatSessionManager, RagService, AnalysisOrchestrationService, SprkChatAgentFactory | Azure OpenAI, AI Search, Document Intelligence |
| **Office Add-ins** | `/api/office/*` | ~10 | IOfficeService, JobStatusService | Graph API, Service Bus |
| **Email / Communication** | `/api/emails`, `/api/communications` | ~10 | EmailService, CommunicationService | Graph API (Mail), Service Bus |
| **Finance Intelligence** | `/api/finance/*` | ~6 | FinanceService, InvoiceExtractionJobHandler, ScorecardCalculatorService | Document Intelligence, AI Search |
| **Workspace / Portfolio** | `/api/workspace/*` | ~18 | PortfolioService, BriefingService, PriorityScoringService, WorkspaceLayoutService, SectionRegistry | Azure OpenAI (optional), Dataverse |
| **Admin / System** | `/api/admin/*`, `/api/resilience` | ~8 | BuilderScopeAdmin, RecordMatchingAdmin | AI Search, Dataverse |

**Total**: 128+ endpoints, 99+ DI registrations, 13+ background job handlers.

---

## Domain Details

### 1. SPE / Document Management (Original Core)

The foundational domain. All document file storage uses SharePoint Embedded (SPE) via Microsoft Graph, with metadata in Dataverse.

**Key Pattern**: "SPE First, Dataverse Second" — file is uploaded to SPE container, then a Dataverse `sprk_document` record is created linking to the SPE DriveItem.

**Services**:
| Service | Purpose | Constraint |
|---------|---------|-----------|
| `SpeFileStore` | Facade for all SPE operations | ADR-007: No Graph SDK type leakage above this layer |
| `GraphClientFactory` | Constructs Graph clients with app-only or OBO auth | Singleton, Redis-cached tokens |
| `UploadSessionManager` | Chunked upload sessions for large files (>4MB) | |
| `IDataverseService` | Document metadata CRUD | From Spaarke.Dataverse shared library |

**Endpoints**:
```
/api/containers                           Container CRUD (app-only auth)
/api/containers/{id}/files/{*path}        File upload/replace
/api/documents/{id}/preview-url           Office preview URL
/api/documents/{id}/content               File download (OBO)
/api/documents/{id}/office                Office Online editor URL
/api/obo/drives/{driveId}/items/{itemId}  OBO file operations
/api/upload-session/chunk                 Chunked upload
/api/navmap/{entity}/{rel}/lookup         Dynamic metadata discovery
```

**Container Architecture**: Single container per environment. All documents stored in the same SPE container. Parent entity association is via Dataverse lookup (`@odata.bind`), not container location.

### 2. AI Platform

The largest domain by endpoint count and service complexity. Provides document intelligence, conversational AI, RAG search, playbook management, and analysis capabilities.

**Services**:
| Service | Lifetime | Purpose |
|---------|----------|---------|
| `SprkChatAgentFactory` | Singleton | Constructs chat agents per session with playbook context |
| `ChatSessionManager` | Scoped | Session lifecycle (create, retrieve, delete) |
| `PlaybookChatContextProvider` | Scoped | Resolves entity-scoped knowledge context from playbook |
| `ChatHistoryManager` | Scoped | Message history with Redis hot cache + Dataverse persistence |
| `RagService` | Singleton | Hybrid search (keyword + vector + semantic ranking) |
| `AnalysisOrchestrationService` | — | Orchestrates document analysis pipeline |
| `DocumentParserRouter` | Singleton | Routes documents to appropriate parser |
| `DocumentIntelligenceService` | Singleton | Azure Document Intelligence integration |
| `OpenAiClient` | Singleton | Azure OpenAI wrapper with circuit breaker |
| `IChatClient` | Singleton | Microsoft.Extensions.AI bridge to Azure OpenAI |

**Endpoint Groups**:
```
/api/ai/chat/sessions                     Chat session management (SSE streaming)
/api/ai/chat/playbooks                    Playbook discovery
/api/ai/analysis                          Document analysis (SSE streaming)
/api/ai/rag/search                        Hybrid RAG search
/api/ai/rag/index                         Document indexing
/api/ai/knowledge/sources                 Knowledge source CRUD
/api/ai/search                            Semantic search (standalone)
/api/ai/search/records                    Dataverse record search
/api/ai/playbooks                         Playbook CRUD
/api/ai/playbooks/{id}/run                Playbook execution
/api/ai/visualization/render              Chart/graph rendering
```

**Critical Flow — HostContext Propagation**:
```
ChatEndpoints → ChatSessionManager → SprkChatAgentFactory
  → PlaybookChatContextProvider → ChatKnowledgeScope
    → DocumentSearchTools / KnowledgeRetrievalTools → RagService → Azure AI Search
```
`ChatHostContext` (entity type, entity ID, workspace type) must flow through every layer. When null, search is tenant-wide (backward compatible).

### 3. Office Add-ins

Supports Outlook and Word add-ins for document saving, entity search, and sharing.

**Endpoints**:
```
/api/office/save                          Save document/email from Office
/api/office/jobs/{jobId}                  Async job status polling
/api/office/jobs/{jobId}/stream           SSE job status streaming
/api/office/search/entities               Entity search from add-in
/api/office/search/documents              Document search from add-in
/api/office/quickcreate/{entityType}      Quick create entity from add-in
/api/office/share/links                   Create sharing links
/api/office/recent                        Recent documents
```

**Rate Limiting**: Per-endpoint limits (5-60/min depending on operation).

### 4. Email / Communication

Email-to-document automation and outbound communication.

**Email Processing Pipeline**:
```
Dataverse Webhook → BFF API → Service Bus → EmailToDocumentJobHandler
  → Graph API (fetch email) → EML generation → SPE upload → Dataverse record
```

**Communication Endpoints**:
```
/api/communications/send                  Send communication
/api/communications/send-bulk             Bulk send
/api/communications/{id}/status           Delivery status
/api/communications/incoming-webhook      Inbound webhook (unauthenticated)
```

### 5. Finance Intelligence

Invoice classification, field extraction, and financial aggregation.

**Pipeline**: Upload → Document Intelligence OCR → AI field extraction → AI Search indexing → Semantic search.

**Endpoints**:
```
/api/finance/invoice-review/confirm       Confirm + enqueue extraction
/api/finance/invoice-review/reject        Reject as non-invoice
/api/finance/invoices/search              Semantic invoice search
/api/finance/matters/{matterId}/summary   Financial summary per matter
/api/finance/rollup/calculations          Rollup calculations
/api/finance/rollup/recalculate           Trigger recalculation
```

### 6. Workspace / Portfolio

Portfolio analytics, priority scoring, briefing generation, to-do management, and user-configurable workspace layouts.

**Endpoints — Portfolio & Analytics**:
```
/api/workspace/portfolio                  Aggregated portfolio metrics
/api/workspace/health                     Health indicators (at-risk matters, overdue events)
/api/workspace/briefing                   Quick summary (optional AI enhancement)
/api/workspace/calculate-scores           Batch priority/effort scoring
/api/workspace/matters                    Matter lifecycle
/api/workspace/ai/tools                   Workspace AI tools
```

**Endpoints — Workspace Layout Management**:
```
GET    /api/workspace/layouts             List user's layouts (system + user-created)
GET    /api/workspace/layouts/default     Get user's default/active layout
GET    /api/workspace/layouts/{id}        Get specific layout by ID
POST   /api/workspace/layouts             Create new layout (max 10 per user enforced)
PUT    /api/workspace/layouts/{id}        Update layout (user-created only; system layouts immutable)
DELETE /api/workspace/layouts/{id}        Delete layout (user-created only)
GET    /api/workspace/sections            List available sections from section registry
GET    /api/workspace/templates           List layout templates for new layout creation
```

**Workspace Configuration System**:

Users can customize their workspace layout — which sections appear, their order, and sizing. The system combines a server-side **Section Registry** with per-user stored layouts:

- **Section Registry** — Each workspace section registers via `SectionRegistration` (id, label, default width, capabilities). `SectionFactoryContext` provides runtime props for rendering. The registry serves as the source of truth for available sections (`GET /api/workspace/sections`).
- **Dynamic Config Builder** — Merges the user's stored layout JSON (`sprk_workspacelayout` record) with the current section registry to produce a `WorkspaceConfig`. Handles registry additions/removals gracefully (new sections appear with defaults; removed sections are pruned).
- **WorkspaceHeader Dropdown** — Client-side layout switcher in the workspace header. Users select from their saved layouts or the system default.
- **Layout Wizard Code Page** (`sprk_workspacelayoutwizard`) — A React 18 Code Page dialog for creating and editing layouts. Opened via `Xrm.Navigation.navigateTo` (webresource target).
- **Dataverse Entity**: `sprk_workspacelayout` — Stores per-user layout configurations (section list, ordering, sizing, default flag). System-provided layouts are read-only.

**Caching**: Redis with 5-10 minute TTL per user. Layout data cached per-user with 15 minute TTL.

### 7. Background Processing

All async work runs through Azure Service Bus with a single `ServiceBusJobProcessor` dispatching to typed handlers.

| Handler | Purpose | Trigger |
|---------|---------|---------|
| `DocumentProcessingJobHandler` | Chunking, extraction, indexing | Upload completion |
| `EmailToDocumentJobHandler` | Email → EML → SPE | Webhook or polling |
| `EmailAnalysisJobHandler` | Email content analysis | Email save |
| `BatchProcessEmailsJobHandler` | Bulk email processing | Admin trigger |
| `InvoiceExtractionJobHandler` | Invoice field extraction | Finance confirm |
| `InvoiceIndexingJobHandler` | Invoice → AI Search | Post-extraction |
| `BulkRagIndexingJobHandler` | Batch embedding generation | Admin trigger |
| `RagIndexingJobHandler` | Single document indexing | Knowledge source upload |
| `ProfileSummaryJobHandler` | Matter profile aggregation | Scheduled |
| `AppOnlyDocumentAnalysisJobHandler` | Background analysis (app-only auth) | API enqueue |
| `IncomingCommunicationJobHandler` | Inbound email processing | Webhook |
| `SpendSnapshotGenerationJobHandler` | Financial snapshot generation | Scheduled |
| `DocumentVectorBackfillService` | Embedding migration/backfill | On-demand |

---

## Cross-Cutting Infrastructure

### Authentication & Authorization

| Concern | Implementation | Reference |
|---------|---------------|-----------|
| **JWT Validation** | Azure AD via `Microsoft.Identity.Web` | Every endpoint |
| **OBO Token Exchange** | User token → Graph token (Redis-cached) | SPE file operations |
| **Endpoint Filters** | Per-resource authorization (not global middleware) | ADR-008 |
| **Authorization Policies** | 26 named policies (file ops, container ops, admin) | Program.cs |
| **Endpoint Filters** | 12 filters (DocumentAuth, AiAuth, FinanceAuth, OfficeAuth, etc.) | `Api/Filters/` |

### Caching (ADR-009: Redis-First)

| Cache | Backend | TTL | Purpose |
|-------|---------|-----|---------|
| Graph OBO tokens | Redis | Varies (token expiry) | 97% Azure AD load reduction |
| UAC snapshots | Redis | 15 min | User access control |
| Navigation metadata | Redis | 15 min | Dataverse metadata |
| RAG embeddings | Redis | Configurable | Embedding reuse |
| Portfolio data | Redis | 5-10 min | Workspace aggregation |
| Chat sessions | Redis (hot) + Dataverse (durable) | Session lifetime | Active chat state |
| Per-request dedup | `RequestCache` (in-memory) | Single request | Prevents duplicate DB calls |

### Resilience (Polly 8.x)

| Service | Pattern | Details |
|---------|---------|---------|
| **Graph API** | Retry + circuit breaker + timeout | `GraphHttpMessageHandler`, honors `Retry-After` |
| **Azure OpenAI** | Circuit breaker | `OpenAiCircuitBrokenException` → HTTP 503 |
| **AI Search** | Retry + circuit breaker | `ResilientSearchClient` wrapper |
| **All external** | Rate limiting | 4 named limiters: `graph-read`, `graph-write`, `ai-batch`, `ai-stream` |

### Observability

| Layer | Technology | Details |
|-------|-----------|---------|
| **Tracing** | OpenTelemetry + Application Insights | Correlation IDs via `X-Correlation-ID` header |
| **Metrics** | Custom OTel meters | `Sprk.Bff.Api.Ai`, `Sprk.Bff.Api.Rag`, `Sprk.Bff.Api.Cache`, `Sprk.Bff.Api.CircuitBreaker`, `Sprk.Bff.Api.Finance` |
| **Logging** | `ILogger<T>` + structured properties | Identifier-only logging (ADR-015: never log document content) |
| **Error Responses** | RFC 7807 ProblemDetails | Stable `errorCode` extension + correlation ID (ADR-019) |

---

## Data Model

### Core Tables

**sprk_document** (child — holds file references)
```
sprk_documentid         GUID        Primary key
sprk_documentname       Text        Display name
sprk_filename           Text        Original file name
sprk_filesize           Int         File size in bytes
sprk_graphitemid        Text        SPE DriveItem ID ← Links to SharePoint
sprk_graphdriveid       Text        SPE Container Drive ID
sprk_matter             Lookup      → sprk_matter (1:N relationship)
sprk_project            Lookup      → sprk_project (1:N relationship)

# Email Archive Fields
sprk_isemailarchive     Boolean     Is this an archived email (.eml)
sprk_email              Lookup      → email (email activity reference)
sprk_emailsubject       Text        Email subject line
sprk_emailfrom          Text        Sender email address
sprk_emailto            Text        Primary recipients (semicolon-separated)
sprk_emailcc            Text        CC recipients (semicolon-separated)
sprk_emaildate          DateTime    Email sent/received date
sprk_emaildirection     Choice      Received (100000000) / Sent (100000001)
sprk_documenttype       Choice      Email = 100000006
sprk_relationshiptype   Choice      Email Attachment = 100000000
sprk_parentdocument     Lookup      → sprk_document (for attachments)
```

**sprk_matter / sprk_project** (parents — own the SPE container)
```
sprk_containerid    Text        SPE Container Drive ID
```

**sprk_chatsession** (AI chat sessions)
```
sprk_chatsessionid      GUID        Primary key
sprk_sessionname        Text        Display name
sprk_playbookid         Lookup      → sprk_playbook
sprk_entitytype         Text        Host entity type
sprk_entityid           Text        Host entity ID
sprk_workspacetype      Text        Workspace context
```

**sprk_playbook** (AI playbook definitions)
```
sprk_playbookid         GUID        Primary key
sprk_name               Text        Playbook name
sprk_systemprompt       Memo        System prompt template
sprk_actions            Memo        JSON action definitions
sprk_skills             Memo        JSON skill configuration
sprk_knowledgesources   Memo        JSON knowledge source mapping
sprk_tools              Memo        JSON tool configuration
sprk_ispublic           Boolean     Available to all users
```

**sprk_workspacelayout** (workspace layout configurations)
```
sprk_workspacelayoutid  GUID        Primary key
sprk_name               Text        Layout display name
sprk_layoutjson         Memo        JSON section configuration (order, sizing, visibility)
sprk_issystem           Boolean     System-provided layout (read-only, not deletable)
sprk_isdefault          Boolean     User's active/default layout
sprk_ownerid            Lookup      → systemuser (owning user)
```

### Relationship Pattern

```
sprk_matter (1) ───── sprk_matter_document_1n ────→ (N) sprk_document
                      ↑
                      Navigation Property: "sprk_Matter" (capital M!)
```

---

## Key Patterns

### 1. Dynamic Metadata Discovery

Instead of hardcoding navigation property names, query at runtime:

```typescript
// PCF calls BFF API
GET /api/navmap/sprk_document/sprk_matter_document_1n/lookup

// Response (from cache or Dataverse)
{
  "navigationPropertyName": "sprk_Matter",  // Correct casing!
  "source": "cache"  // or "dataverse" on first call
}
```

### 2. Document Record Creation with @odata.bind

```typescript
const payload = {
  "sprk_documentname": fileName,
  "sprk_graphitemid": driveItemId,
  "sprk_graphdriveid": containerId,
  "sprk_Matter@odata.bind": `/sprk_matters(${matterId})`
};
```

### 3. Container Architecture (Single Container Per Environment)

Each environment has **one default SPE container**. All documents stored in the same container.

- Parent entities have `sprk_containerid` — all point to the **same environment container**
- File-to-entity association is via Dataverse parent lookup (`@odata.bind`), NOT container location
- Background jobs use `DefaultContainerId` from config

### 4. OBO (On-Behalf-Of) Token Flow

```
PCF Control                    BFF API                      Graph API
    |                              |                            |
    |-- Token A (user) ---------->|                            |
    |                              |-- OBO Exchange ---------->|
    |                              |<-- Token B (graph) -------|
    |                              |                            |
    |                              |-- Graph Call (Token B) -->|
    |<-- Response ----------------|<-- Response --------------|
```

**Token Scopes:**
- PCF requests: `api://{bff-client-id}/user_impersonation`
- BFF exchanges for: `FileStorageContainer.Selected`, `Files.Read.All`

### 5. AI Dual Pipeline Pattern

AI operations use two incompatible patterns that coexist:

| Pattern | Use Case | Error Handling | Auth Model |
|---------|----------|---------------|------------|
| **Sync SSE streaming** | `/api/ai/chat/*/messages`, `/api/ai/analysis` | Terminal SSE event | Endpoint filter |
| **Async job enqueue** | Batch indexing, email processing, invoice extraction | Job status record | Job-context |

### 6. Service Bus Job Contract (ADR-004)

All background work uses a standard schema. Handlers must be idempotent (at-least-once delivery). Document bytes are **never** placed in Service Bus payloads — only references/IDs.

---

## Architecture Constraints (ADR Summary)

| ADR | Constraint | Key Rule |
|-----|-----------|----------|
| **ADR-001** | Minimal API + BackgroundService | No Azure Functions. Single runtime for HTTP + async. |
| **ADR-004** | Async Job Contract | Idempotent handlers. No content blobs in Service Bus. |
| **ADR-007** | SpeFileStore Facade | All Graph calls through `SpeFileStore`. No `GraphServiceClient` injection outside facade. |
| **ADR-008** | Endpoint Filters for Auth | No global middleware for resource authorization. Per-endpoint filters only. |
| **ADR-009** | Redis-First Caching | No `IMemoryCache` except metadata. No hybrid L1/L2 without profiling proof. |
| **ADR-010** | DI Minimalism | Register concretes. Use feature modules (`AddXxxModule()`). |
| **ADR-013** | AI Architecture | Extend BFF (no separate AI service). Endpoint filters for AI auth. Rate limit all AI endpoints. |
| **ADR-015** | Data Governance | Log identifiers only — never document content, prompts, or model responses. |
| **ADR-016** | Rate Limiting | Bound concurrency per upstream service. Use async jobs for heavy work. Return 429/503 under load. |
| **ADR-019** | Error Handling | RFC 7807 ProblemDetails. Stable `errorCode`. No content leakage. |

---

## Architecture Assessment (March 2026)

### Current State

The BFF has grown from its original SPE gateway role to a 7-domain platform. This was **by design** — ADR-001 and ADR-013 explicitly mandate extending the BFF rather than creating separate services. However, the scale (128+ endpoints, 99+ DI registrations, 1,881-line Program.cs) has exceeded the original "minimal composition" intent.

### Known Risks

| Risk | Severity | Status |
|------|----------|--------|
| **In-memory state loss** — App Service restart loses active analyses | High | OPS-05 identified, not yet resolved |
| **No domain isolation** — failure in one domain can crash others | Medium | No runtime circuit breaking between domains |
| **Background job contention** — 13 handler types share one processor | Medium | No priority queues |
| **Program.cs complexity** — 1,881 lines in composition root | Medium | Modularized via `AddXxxModule()` but still one file |

### Recommended Enhancements

| Priority | Enhancement | Effort | Impact |
|----------|-------------|--------|--------|
| 1 | Persistent analysis state (Redis + Dataverse) | Scoped in OPS-05 | Removes #1 production blocker |
| 2 | Decompose Program.cs into domain startup modules | 4-6 hours | Clarity, reduced merge conflicts |
| 3 | Feature flags to disable/enable domains at runtime | 4 hours | Incident isolation without redeploy |
| 4 | Priority-based Service Bus queues (critical/standard/bulk) | 8 hours | Prevent bulk jobs from starving user ops |
| 5 | Domain module contracts (explicit cross-domain interfaces) | 2-3 weeks | Future-proof extraction seams |

**Key Principle**: Invest in internal structure (modular monolith), not external separation (microservices). The coordination benefits of a single deployment outweigh the isolation benefits of splitting — at current team size and scale.

Full assessment: `projects/ai-spaarke-platform-enhancements-r1/notes/bff-api-architecture-assessment.md`

---

## Configuration Lookup

**App Registrations**:
- PCF Control: `5175798e-f23e-41c3-b09b-7a90b9218189`
- BFF API: `1e40baad-e065-4aea-a8d4-4b7ab273458c`

**Azure Resources**:
| Resource | Dev Environment |
|----------|----------------|
| App Service | `spe-api-dev-67e2xz` |
| Azure OpenAI | `spaarke-openai-dev` |
| Document Intelligence | `spaarke-docintel-dev` |
| AI Search | `spaarke-search-dev` |
| Redis | Via connection string |
| Service Bus | Via connection string |

**Key Files**:
```
src/server/api/Sprk.Bff.Api/              BFF API (all 7 domains)
src/server/api/Sprk.Bff.Api/CLAUDE.md     Module-specific AI instructions
src/server/shared/Spaarke.Core/           Core library (auth, cache, constants)
src/server/shared/Spaarke.Dataverse/      Dataverse client library
src/client/pcf/                           PCF controls (React 16/17)
src/client/code-pages/                    React Code Pages (React 18)
src/client/office-addins/                 Office Add-ins (React 18)
src/client/shared/Spaarke.UI.Components/  Shared UI library
```

---

## Common Mistakes

| Mistake | Why It Fails | Correct Approach |
|---------|--------------|------------------|
| Hardcoding `sprk_matter@odata.bind` | Case-sensitive, varies by relationship | Use metadata discovery (`/api/navmap`) |
| Using friendly scope `api://spe-bff-api/...` | Azure AD requires full URI | Use `api://1e40baad-.../user_impersonation` |
| Injecting `GraphServiceClient` directly | Violates ADR-007 facade pattern | Use `SpeFileStore` |
| Using global auth middleware | Violates ADR-008, lacks route context | Use endpoint filters |
| Logging document content or prompts | Violates ADR-015 data governance | Log identifiers and sizes only |
| Placing document bytes in Service Bus | Violates ADR-004 job contract | Pass document ID, fetch in handler |
| Creating separate AI service | Violates ADR-001/ADR-013 | Extend BFF with AI module |

---

## Related Documentation

- [sdap-auth-patterns.md](sdap-auth-patterns.md) — Authentication flows and token exchange
- [sdap-pcf-patterns.md](sdap-pcf-patterns.md) — PCF control implementation details
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF API endpoints and services
- [sdap-component-interactions.md](sdap-component-interactions.md) — Component interaction patterns
- [sdap-workspace-integration-patterns.md](sdap-workspace-integration-patterns.md) — Workspace integration
- [EMAIL-TO-DOCUMENT-ARCHITECTURE.md](../guides/EMAIL-TO-DOCUMENT-ARCHITECTURE.md) — Email automation architecture
- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) — AI platform architecture
- [BFF API Architecture Assessment](../../projects/ai-spaarke-platform-enhancements-r1/notes/bff-api-architecture-assessment.md) — Full assessment with recommendations

---

*SDAP = Spaarke Data & AI Platform. Redefined March 2026 from the original "SharePoint Document Access Platform" to reflect the platform's expanded scope beyond document management.*
