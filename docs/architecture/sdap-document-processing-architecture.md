# SDAP Document Processing Architecture

> **Version**: 1.0
> **Last Updated**: January 2026
> **Status**: Authoritative Reference
> **Supersedes**: `ai-document-summary-architecture.md`, `email-to-document-automation.md`, `sdap-overview.md`, `EMAIL-TO-DOCUMENT-ARCHITECTURE.md`

## Executive Summary

The SharePoint Document Access Platform (SDAP) provides unified document management with integrated AI processing capabilities. Documents enter the system through three distinct routes, each converging on a common AI processing pipeline that produces summaries, keywords, entity extraction, and search indexing.

**Key Architectural Principles:**
- **App-only authentication** for background processing (Service Bus jobs)
- **Delegated (OBO) authentication** for user-initiated requests
- **Dual pipeline execution** - SPE storage and AI processing run in parallel
- **Automatic output extraction** - AI outputs derived from handler type, not configuration

---

## Document Creation Routes

### Route Comparison Matrix

| Aspect | File Upload (PCF) | Email-to-Document | Office Add-in |
|--------|-------------------|-------------------|---------------|
| **Entry Point** | FileUploadManager PCF | Webhook/Polling Service | Outlook/Word Add-in |
| **Authentication** | OBO (delegated) | App-only | Dialog API (delegated) |
| **SPE Container** | User-selected | Rule-matched | Profile-linked |
| **Trigger** | User action | Email arrival | Save button |
| **AI Enqueueing** | Immediate (streaming) | Job-based | Job-based |
| **Service Bus Job** | N/A (direct call) | `EmailToDocument` + `AppOnlyDocumentAnalysis` | `AppOnlyDocumentAnalysis` |

### Route 1: File Upload (PCF Control)

```
┌─────────────────────────────────────────────────────────────────────┐
│                     FILE UPLOAD FLOW (PCF)                          │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  User Action          API (OBO Auth)              AI Processing     │
│  ───────────          ──────────────              ─────────────     │
│                                                                     │
│  ┌──────────┐    ┌─────────────────┐    ┌────────────────────┐     │
│  │FileUpload│───▶│POST /api/files  │───▶│SpeFileStore        │     │
│  │Manager   │    │  /upload        │    │.UploadFileAsync()  │     │
│  │PCF       │    └────────┬────────┘    └────────────────────┘     │
│  └──────────┘             │                                         │
│                           ▼                                         │
│              ┌────────────────────────┐                            │
│              │POST /api/ai/analyze/   │                            │
│              │  document-profile      │                            │
│              │  (SSE streaming)       │                            │
│              └───────────┬────────────┘                            │
│                          │                                          │
│                          ▼                                          │
│              ┌────────────────────────┐                            │
│              │AnalysisOrchestration   │                            │
│              │Service (real-time)     │                            │
│              └───────────┬────────────┘                            │
│                          │                                          │
│                          ▼                                          │
│              ┌────────────────────────┐                            │
│              │Document Profile        │                            │
│              │Results → Dataverse     │                            │
│              └────────────────────────┘                            │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Components:**
- `FileUploadManager` PCF control initiates upload
- `FileEndpoints.cs` handles file upload via `POST /api/files/upload`
- `SpeFileStore.UploadFileAsync()` stores in SharePoint Embedded
- `AiAnalysisEndpoints.cs` handles `POST /api/ai/analyze/document-profile`
- `AnalysisOrchestrationService` executes Document Profile playbook
- Results streamed via SSE to UI, then persisted to Dataverse

**Authentication Flow:**
```
User Token → OBO Exchange → SPE Access Token → Graph API
                         → OpenAI Access (same OBO context)
```

### Route 2: Email-to-Document Automation

```
┌─────────────────────────────────────────────────────────────────────┐
│                 EMAIL-TO-DOCUMENT FLOW                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Trigger              Processing                  AI Pipeline       │
│  ───────              ──────────                  ───────────       │
│                                                                     │
│  ┌──────────┐    ┌─────────────────┐                               │
│  │Graph     │───▶│EmailWebhook     │                               │
│  │Webhook   │    │Endpoints.cs     │                               │
│  └──────────┘    └────────┬────────┘                               │
│       OR                  │                                         │
│  ┌──────────┐             │                                         │
│  │Polling   │─────────────┤                                         │
│  │Service   │             │                                         │
│  └──────────┘             ▼                                         │
│              ┌────────────────────────┐                            │
│              │EmailToDocumentJob      │ (Service Bus)              │
│              │Handler                 │                            │
│              └───────────┬────────────┘                            │
│                          │                                          │
│                          ▼                                          │
│              ┌────────────────────────┐                            │
│              │EmailToDocumentService  │                            │
│              │.ProcessEmailAsync()    │                            │
│              │ • Convert to .eml      │                            │
│              │ • Process attachments  │                            │
│              │ • Apply filter rules   │                            │
│              └───────────┬────────────┘                            │
│                          │                                          │
│                          ▼                                          │
│              ┌────────────────────────┐                            │
│              │SpeFileStore            │ (App-only auth)            │
│              │.UploadFileAppOnlyAsync │                            │
│              └───────────┬────────────┘                            │
│                          │                                          │
│              ┌───────────┴───────────┐                             │
│              ▼                       ▼                              │
│  ┌────────────────────┐  ┌────────────────────┐                   │
│  │RagIndexingJob      │  │AppOnlyDocument     │                   │
│  │(Search indexing)   │  │AnalysisJob         │                   │
│  └────────────────────┘  │(Document Profile)  │                   │
│                          └────────────────────┘                    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Components:**
- `EmailWebhookEndpoints.cs` receives Graph change notifications
- `EmailPollingWorker` polls for new emails on schedule
- `EmailToDocumentJobHandler` processes Service Bus messages
- `EmailToDocumentService` converts emails to .eml, processes attachments
- `SpeFileStore.UploadFileAppOnlyAsync()` uploads with app-only auth
- `AppOnlyDocumentAnalysisJobHandler` runs Document Profile playbook
- `RagIndexingJobHandler` indexes documents for AI Search

**Authentication Flow:**
```
Graph Webhook → App-only Token (Client Credentials) → SPE Access
                                                    → OpenAI Access
                                                    → Dataverse Access
```

### Route 3: Office Add-in (Outlook/Word)

```
┌─────────────────────────────────────────────────────────────────────┐
│                    OFFICE ADD-IN FLOW                               │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Add-in UI            BFF API                     Background        │
│  ─────────            ───────                     ──────────        │
│                                                                     │
│  ┌──────────┐    ┌─────────────────┐                               │
│  │Outlook/  │───▶│Dialog API       │                               │
│  │Word      │    │Authentication   │                               │
│  │Add-in    │    └────────┬────────┘                               │
│  └──────────┘             │                                         │
│       │                   ▼                                         │
│       │          ┌─────────────────┐                               │
│       └─────────▶│POST /api/office │                               │
│                  │  /upload-file   │                               │
│                  │ (with metadata) │                               │
│                  └────────┬────────┘                               │
│                           │                                         │
│                           ▼                                         │
│              ┌────────────────────────┐                            │
│              │UploadFinalization      │ (Background Worker)        │
│              │Worker                  │                            │
│              │ • Wait for SPE upload  │                            │
│              │ • Process with timeout │                            │
│              └───────────┬────────────┘                            │
│                          │                                          │
│              ┌───────────┴───────────┐                             │
│              ▼                       ▼                              │
│  ┌────────────────────┐  ┌────────────────────┐                   │
│  │RagIndexingJob      │  │AppOnlyDocument     │                   │
│  │(Search indexing)   │  │AnalysisJob         │                   │
│  └────────────────────┘  │(Document Profile)  │                   │
│                          └────────────────────┘                    │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

**Key Components:**
- Office Add-in uses Dialog API for authentication
- `OfficeAddInEndpoints.cs` handles `POST /api/office/upload-file`
- `UploadFinalizationWorker` monitors pending uploads
- Once SPE upload complete, enqueues AI processing jobs
- Same `AppOnlyDocumentAnalysisJobHandler` and `RagIndexingJobHandler` as email route

**Authentication Flow:**
```
Dialog API → User Token → OBO for SPE Upload
Background Worker → App-only Token → AI Processing + Dataverse Update
```

---

## AI Processing Pipeline

### Core Architecture

All three routes converge on the same AI processing components:

```
┌─────────────────────────────────────────────────────────────────────┐
│                    AI PROCESSING PIPELINE                           │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  Entry Points (Job Handlers)                                        │
│  ──────────────────────────                                         │
│                                                                     │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ AppOnlyDocumentAnalysisJobHandler                           │   │
│  │   • Receives: DocumentId, DriveId, ItemId, PlaybookId       │   │
│  │   • Auth: App-only (client credentials)                     │   │
│  │   • Calls: AppOnlyAnalysisService                           │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                          │                                          │
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ AppOnlyAnalysisService                                      │   │
│  │   • Downloads file content from SPE (app-only)              │   │
│  │   • Extracts text via DocumentIntelligence                  │   │
│  │   • Calls: AnalysisOrchestrationService                     │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                          │                                          │
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ AnalysisOrchestrationService                                │   │
│  │   • Loads Playbook from Dataverse                           │   │
│  │   • Resolves Scope for each node                            │   │
│  │   • Executes nodes via AiAnalysisNodeExecutor               │   │
│  │   • Extracts outputs based on handler type                  │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                          │                                          │
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ AiAnalysisNodeExecutor                                      │   │
│  │   • Routes to appropriate IAiToolHandler                    │   │
│  │   • Handlers: SummaryHandler, EntityExtractorHandler,       │   │
│  │               DocumentClassifierHandler                     │   │
│  │   • Calls Azure OpenAI with tool-specific prompts           │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                          │                                          │
│                          ▼                                          │
│  ┌─────────────────────────────────────────────────────────────┐   │
│  │ Output Persistence                                          │   │
│  │   • DocumentProfileFieldMapper maps outputs → Dataverse     │   │
│  │   • sprk_filetldr, sprk_filesummary, sprk_filekeywords     │   │
│  │   • sprk_documenttype, sprk_entities                        │   │
│  └─────────────────────────────────────────────────────────────┘   │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### AI Tool Handlers

Each handler generates specific output types automatically:

| Handler | Output Types Generated | Dataverse Fields |
|---------|----------------------|------------------|
| `SummaryHandler` | TL;DR, Summary, Keywords | `sprk_filetldr`, `sprk_filesummary`, `sprk_filekeywords` |
| `EntityExtractorHandler` | Entities | `sprk_entities` (JSON) |
| `DocumentClassifierHandler` | Document Type | `sprk_documenttype` |

**Important**: Output types are derived from the handler type, not from playbook configuration. When a playbook node uses `SummaryHandler`, all three outputs (TL;DR, Summary, Keywords) are generated regardless of what's configured in Output Types.

### Playbook Resolution Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                    PLAYBOOK RESOLUTION                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. Load Playbook Definition                                        │
│     └─▶ PlaybookService.GetPlaybookAsync(playbookId)               │
│         └─▶ Fetches from Dataverse: sprk_aiplaybook                │
│                                                                     │
│  2. Load Playbook Nodes                                             │
│     └─▶ PlaybookService.GetPlaybookNodesAsync(playbookId)          │
│         └─▶ Fetches from Dataverse: sprk_aiplaybooknode            │
│         └─▶ Orders by sprk_executionorder                          │
│                                                                     │
│  3. For Each Node: Resolve Scope                                    │
│     └─▶ ScopeResolverService.ResolveScopeAsync(scopeId, context)   │
│         └─▶ Returns: System Prompt, User Prompt, Input Context     │
│                                                                     │
│  4. Execute Node                                                    │
│     └─▶ AiAnalysisNodeExecutor.ExecuteNodeAsync(node, scope)       │
│         └─▶ Routes to handler based on sprk_toolid                 │
│         └─▶ Handler calls Azure OpenAI                             │
│         └─▶ Returns structured output                              │
│                                                                     │
│  5. Extract and Persist Outputs                                     │
│     └─▶ AnalysisOrchestrationService extracts by handler type      │
│     └─▶ DocumentProfileFieldMapper maps to Dataverse fields        │
│     └─▶ DataverseService.UpdateDocumentAsync()                     │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Scope Resolution Details

The `ScopeResolverService` resolves scope definitions from Dataverse:

```csharp
// Scope resolution returns three components:
public class ResolvedScope
{
    public string SystemPrompt { get; set; }    // AI persona/instructions
    public string UserPrompt { get; set; }      // Task-specific prompt
    public string InputContext { get; set; }    // Document content/context
}

// Resolution sources (in priority order):
// 1. Scope-specific prompts from sprk_aiplaybookscope
// 2. Tool default prompts from sprk_aitool
// 3. Hardcoded fallbacks in handler
```

---

## Service Bus Job Processing

### Job Types

| Job Type | Handler | Purpose | Idempotency Key |
|----------|---------|---------|-----------------|
| `EmailToDocument` | `EmailToDocumentJobHandler` | Process incoming email | `email-{messageId}` |
| `AppOnlyDocumentAnalysis` | `AppOnlyDocumentAnalysisJobHandler` | Run Document Profile playbook | `analysis-{documentId}` |
| `RagIndexing` | `RagIndexingJobHandler` | Index document for AI Search | `rag-index-{driveId}-{itemId}` |

### Job Flow Pattern

```
┌─────────────────────────────────────────────────────────────────────┐
│                    JOB PROCESSING FLOW                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. Job Enqueued                                                    │
│     └─▶ JobEnqueueService.EnqueueAsync(jobType, payload)           │
│         └─▶ Sends to Service Bus queue: sdap-jobs                  │
│                                                                     │
│  2. Job Received                                                    │
│     └─▶ JobProcessingWorker (BackgroundService)                    │
│         └─▶ Listens on Service Bus queue                           │
│         └─▶ Deserializes JobContract                               │
│                                                                     │
│  3. Handler Resolution                                              │
│     └─▶ JobHandlerFactory.GetHandler(jobContract.JobType)          │
│         └─▶ Returns appropriate IJobHandler                        │
│                                                                     │
│  4. Idempotency Check                                               │
│     └─▶ IdempotencyService.IsEventProcessedAsync(key)              │
│         └─▶ If processed: Return Success (skip duplicate)          │
│         └─▶ If not: Acquire lock, proceed                          │
│                                                                     │
│  5. Job Execution                                                   │
│     └─▶ Handler.ProcessAsync(jobContract, cancellationToken)       │
│         └─▶ Returns JobOutcome (Success/Failure/Poisoned)          │
│                                                                     │
│  6. Completion                                                      │
│     └─▶ Mark idempotency key as processed                          │
│     └─▶ Complete/Abandon Service Bus message                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Job Payload Structures

**EmailToDocumentJobPayload:**
```csharp
{
    TenantId: string,
    MessageId: string,
    UserId: string,
    Subject: string,
    ReceivedDateTime: DateTimeOffset,
    FilterRuleId: string?,
    ProcessAttachments: bool
}
```

**AppOnlyDocumentAnalysisJobPayload:**
```csharp
{
    TenantId: string,
    DocumentId: string,       // Dataverse sprk_document ID
    DriveId: string,          // SPE drive ID
    ItemId: string,           // SPE item ID
    FileName: string,
    PlaybookId: string,       // Dataverse sprk_aiplaybook ID
    PlaybookName: string?,
    AnalysisId: string?       // Existing analysis record (optional)
}
```

**RagIndexingJobPayload:**
```csharp
{
    TenantId: string,
    DriveId: string,
    ItemId: string,
    FileName: string,
    DocumentId: string?,      // Optional - for Dataverse tracking update
    KnowledgeSourceId: string?,
    KnowledgeSourceName: string?,
    Metadata: Dictionary<string, string>?,
    ParentEntity: ParentEntityContext?
}
```

---

## RAG (Search) Indexing

### Indexing Flow

```
┌─────────────────────────────────────────────────────────────────────┐
│                    RAG INDEXING FLOW                                │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. Job Received                                                    │
│     └─▶ RagIndexingJobHandler.ProcessAsync()                       │
│                                                                     │
│  2. File Retrieval                                                  │
│     └─▶ FileIndexingService.IndexFileAppOnlyAsync()                │
│         └─▶ Downloads file from SPE (app-only auth)                │
│                                                                     │
│  3. Text Extraction                                                 │
│     └─▶ ContentExtractionService                                   │
│         └─▶ Native: .txt, .md, .json, .csv, .xml, .html           │
│         └─▶ Document Intelligence: .pdf, .docx, .doc              │
│                                                                     │
│  4. Chunking                                                        │
│     └─▶ ChunkingService.ChunkTextAsync()                           │
│         └─▶ Splits into semantic chunks                            │
│         └─▶ Preserves metadata per chunk                           │
│                                                                     │
│  5. Embedding Generation                                            │
│     └─▶ EmbeddingService.GenerateEmbeddingsAsync()                 │
│         └─▶ Azure OpenAI text-embedding-3-large                    │
│         └─▶ 3072-dimensional vectors                               │
│                                                                     │
│  6. Index Upload                                                    │
│     └─▶ AiSearchService.UploadDocumentsAsync()                     │
│         └─▶ Uploads to Azure AI Search index                       │
│         └─▶ Index: sprk-knowledge-shared (configurable)            │
│                                                                     │
│  7. Dataverse Update                                                │
│     └─▶ DataverseService.UpdateDocumentAsync()                     │
│         └─▶ sprk_searchindexed = true                              │
│         └─▶ sprk_searchindexname = index name                      │
│         └─▶ sprk_searchindexedon = timestamp                       │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

### Index Schema

The Azure AI Search index (`sprk-knowledge-shared`) stores:

| Field | Type | Purpose |
|-------|------|---------|
| `id` | string | Unique chunk identifier |
| `content` | string | Chunk text content |
| `contentVector` | vector(3072) | Embedding for semantic search |
| `fileName` | string | Source file name |
| `documentId` | string | Dataverse document ID |
| `customerId` | string | Tenant ID for filtering |
| `parentEntityType` | string | Entity type (matter, project, etc.) |
| `parentEntityId` | string | Entity ID for entity-scoped search |
| `knowledgeSourceId` | string | Knowledge source reference |
| `metadata` | string | JSON metadata |

---

## Authentication Patterns

### OBO (On-Behalf-Of) Flow - User-Initiated Requests

```
┌─────────────────────────────────────────────────────────────────────┐
│  User Request (PCF, Direct API)                                     │
│                                                                     │
│  ┌──────────┐    ┌─────────────────┐    ┌────────────────────┐     │
│  │Browser/  │───▶│BFF API          │───▶│Azure AD            │     │
│  │PCF       │    │(Bearer Token)   │    │OBO Exchange        │     │
│  └──────────┘    └─────────────────┘    └────────┬───────────┘     │
│                                                   │                 │
│                                                   ▼                 │
│                                          ┌────────────────────┐    │
│                                          │Downstream Token    │    │
│                                          │(SPE, Graph, OpenAI)│    │
│                                          └────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
```

**Scopes Requested:**
- `Files.ReadWrite.All` (SPE access)
- `User.Read` (User profile)
- OpenAI endpoint access

### App-Only Flow - Background Processing

```
┌─────────────────────────────────────────────────────────────────────┐
│  Background Job (Service Bus)                                       │
│                                                                     │
│  ┌──────────┐    ┌─────────────────┐    ┌────────────────────┐     │
│  │Job       │───▶│App-only Token   │───▶│Azure AD            │     │
│  │Handler   │    │Request          │    │Client Credentials  │     │
│  └──────────┘    │(Client ID +     │    └────────┬───────────┘     │
│                  │ Client Secret)  │             │                  │
│                  └─────────────────┘             ▼                  │
│                                          ┌────────────────────┐    │
│                                          │App Token           │    │
│                                          │(Tenant-wide access)│    │
│                                          └────────────────────┘    │
└─────────────────────────────────────────────────────────────────────┘
```

**App Registration Requirements:**
- `Files.ReadWrite.All` (Application permission)
- `Sites.ReadWrite.All` (Application permission for SPE)
- Container access via `ContainerSelected` permission

---

## Data Flow: Complete Document Lifecycle

### Example: Email with Attachment

```
┌─────────────────────────────────────────────────────────────────────┐
│  EMAIL-TO-DOCUMENT: Complete Data Flow                              │
├─────────────────────────────────────────────────────────────────────┤
│                                                                     │
│  1. EMAIL ARRIVES                                                   │
│     │                                                               │
│     ├── Graph sends webhook notification                            │
│     ├── Webhook validates, deduplicates, enqueues job              │
│     └── Job: { messageId, userId, tenantId }                       │
│                                                                     │
│  2. EMAIL PROCESSING                                                │
│     │                                                               │
│     ├── EmailToDocumentJobHandler receives job                      │
│     ├── Fetches email via Graph (app-only)                         │
│     ├── Converts to .eml format                                     │
│     ├── Extracts attachments (filters signature images)            │
│     └── Applies filter rules for container/folder selection        │
│                                                                     │
│  3. DOCUMENT CREATION                                               │
│     │                                                               │
│     ├── Creates sprk_document record in Dataverse                  │
│     ├── Uploads .eml to SPE container (app-only)                   │
│     ├── Uploads each attachment to SPE (app-only)                  │
│     └── Links documents to parent entity if rule specifies         │
│                                                                     │
│  4. AI PROCESSING (per document)                                    │
│     │                                                               │
│     ├── Enqueues AppOnlyDocumentAnalysis job                       │
│     ├── Enqueues RagIndexing job                                   │
│     │                                                               │
│     ├── AppOnlyDocumentAnalysisJobHandler:                         │
│     │   ├── Downloads from SPE (app-only)                          │
│     │   ├── Extracts text via Document Intelligence                │
│     │   ├── Loads Document Profile playbook                        │
│     │   ├── Executes SummaryHandler → TL;DR, Summary, Keywords     │
│     │   ├── Updates sprk_document with results                     │
│     │   └── Creates sprk_analysis record                           │
│     │                                                               │
│     └── RagIndexingJobHandler:                                     │
│         ├── Downloads from SPE (app-only)                          │
│         ├── Extracts text, chunks, generates embeddings            │
│         ├── Uploads to Azure AI Search                             │
│         └── Updates sprk_searchindexed = true                      │
│                                                                     │
│  5. FINAL STATE                                                     │
│     │                                                               │
│     ├── sprk_document record with:                                 │
│     │   ├── sprk_filetldr, sprk_filesummary, sprk_filekeywords    │
│     │   ├── sprk_searchindexed = true                              │
│     │   ├── sprk_searchindexname = "sprk-knowledge-shared"        │
│     │   └── sprk_searchindexedon = timestamp                       │
│     │                                                               │
│     ├── sprk_analysis record with full analysis results            │
│     │                                                               │
│     └── Indexed chunks in Azure AI Search                          │
│                                                                     │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Component Reference

### BFF API Services

| Service | File | Purpose |
|---------|------|---------|
| `AnalysisOrchestrationService` | `Services/Ai/AnalysisOrchestrationService.cs` | Orchestrates playbook execution |
| `AppOnlyAnalysisService` | `Services/Ai/AppOnlyAnalysisService.cs` | App-only analysis wrapper |
| `PlaybookService` | `Services/Ai/PlaybookService.cs` | Playbook/node retrieval |
| `ScopeResolverService` | `Services/Ai/ScopeResolverService.cs` | Scope resolution from Dataverse |
| `AiAnalysisNodeExecutor` | `Services/Ai/Nodes/AiAnalysisNodeExecutor.cs` | Node execution routing |
| `SpeFileStore` | `Services/Storage/SpeFileStore.cs` | SPE file operations |
| `FileIndexingService` | `Services/Ai/FileIndexingService.cs` | RAG indexing orchestration |
| `EmailToDocumentService` | `Services/Email/EmailToDocumentService.cs` | Email processing logic |

### Job Handlers

| Handler | File | Job Type |
|---------|------|----------|
| `EmailToDocumentJobHandler` | `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` | `EmailToDocument` |
| `AppOnlyDocumentAnalysisJobHandler` | `Services/Jobs/Handlers/AppOnlyDocumentAnalysisJobHandler.cs` | `AppOnlyDocumentAnalysis` |
| `RagIndexingJobHandler` | `Services/Jobs/Handlers/RagIndexingJobHandler.cs` | `RagIndexing` |

### AI Tool Handlers

| Handler | File | Tool ID |
|---------|------|---------|
| `SummaryHandler` | `Services/Ai/Tools/SummaryHandler.cs` | `summary` |
| `EntityExtractorHandler` | `Services/Ai/Tools/EntityExtractorHandler.cs` | `entity-extractor` |
| `DocumentClassifierHandler` | `Services/Ai/Tools/DocumentClassifierHandler.cs` | `document-classifier` |

### Background Workers

| Worker | File | Purpose |
|--------|------|---------|
| `JobProcessingWorker` | `Workers/JobProcessingWorker.cs` | Service Bus job consumer |
| `UploadFinalizationWorker` | `Workers/UploadFinalizationWorker.cs` | Office add-in upload completion |
| `EmailPollingWorker` | `Workers/EmailPollingWorker.cs` | Scheduled email polling |
| `ProfileSummaryWorker` | `Workers/ProfileSummaryWorker.cs` | Profile summary generation |

### API Endpoints

| Endpoint | File | Purpose |
|----------|------|---------|
| `/api/files/*` | `Endpoints/FileEndpoints.cs` | File upload/download |
| `/api/ai/analyze/*` | `Endpoints/AiAnalysisEndpoints.cs` | AI analysis (streaming) |
| `/api/office/*` | `Endpoints/OfficeAddInEndpoints.cs` | Office add-in operations |
| `/api/email/webhook` | `Endpoints/EmailWebhookEndpoints.cs` | Graph webhook receiver |
| `/api/rag/*` | `Endpoints/RagEndpoints.cs` | RAG indexing operations |

---

## Dataverse Schema

### Core Tables

| Table | Purpose |
|-------|---------|
| `sprk_document` | Document metadata and AI outputs |
| `sprk_analysis` | Analysis execution records |
| `sprk_aiplaybook` | Playbook definitions |
| `sprk_aiplaybooknode` | Playbook execution nodes |
| `sprk_aiplaybookscope` | Scope definitions (prompts) |
| `sprk_aitool` | Tool definitions |
| `sprk_emailfilterrule` | Email-to-document routing rules |
| `sprk_analysisknowledge` | Knowledge source definitions |

### Key Fields on sprk_document

| Field | Type | Updated By |
|-------|------|------------|
| `sprk_filetldr` | Text | Document Profile playbook |
| `sprk_filesummary` | Text | Document Profile playbook |
| `sprk_filekeywords` | Text | Document Profile playbook |
| `sprk_documenttype` | Text | Document Profile playbook |
| `sprk_entities` | Text (JSON) | Document Profile playbook |
| `sprk_searchindexed` | Boolean | RagIndexingJobHandler |
| `sprk_searchindexname` | Text | RagIndexingJobHandler |
| `sprk_searchindexedon` | DateTime | RagIndexingJobHandler |

---

## Configuration

### appsettings.json Key Sections

```json
{
  "Analysis": {
    "Enabled": true,
    "SharedIndexName": "sprk-knowledge-shared",
    "MaxKnowledgeResults": 5,
    "MinRelevanceScore": 0.7
  },
  "DocumentIntelligence": {
    "Enabled": true,
    "OpenAiEndpoint": "...",
    "SummarizeModel": "gpt-4o",
    "EmbeddingModel": "text-embedding-3-large",
    "EmbeddingDimensions": 3072
  },
  "Email": {
    "Enabled": true,
    "AutoEnqueueAi": true,
    "AutoIndexToRag": false
  }
}
```

### Feature Flags

| Flag | Purpose |
|------|---------|
| `Analysis:Enabled` | Master switch for AI features |
| `Email:AutoEnqueueAi` | Auto-run Document Profile on email documents |
| `Email:AutoIndexToRag` | Auto-index email documents for search |

---

## Troubleshooting

### Common Issues

| Symptom | Cause | Resolution |
|---------|-------|------------|
| AI outputs not appearing | Playbook not assigned to document | Verify `sprk_aiplaybook` lookup on document |
| Search fields not updating | RagIndexingJobHandler missing DocumentId | Ensure payload includes DocumentId |
| OBO token failure | Scope not consented | Re-consent app permissions |
| App-only auth failure | Missing application permissions | Grant `Files.ReadWrite.All` (Application) |
| Job stuck in queue | Handler throwing unhandled exception | Check App Insights for errors |

### Diagnostic Logging

Key log patterns to search in Application Insights:

```kusto
// AI processing
traces | where message contains "AnalysisOrchestration"

// Job processing
traces | where message contains "JobHandler" and message contains "Processing"

// RAG indexing
traces | where message contains "RAG indexing"

// Email processing
traces | where message contains "EmailToDocument"
```

---

## Related Documentation

- **API Reference**: [ai-document-summary.md](../guides/ai-document-summary.md)
- **Office Add-in Guide**: [office-addins-admin-guide.md](../guides/office-addins-admin-guide.md)
- **Deployment Checklist**: [office-addins-deployment-checklist.md](../guides/office-addins-deployment-checklist.md)
- **Component Interactions**: [sdap-component-interactions.md](./sdap-component-interactions.md)

---

*This document consolidates all SDAP document processing architecture documentation into a single authoritative reference.*
