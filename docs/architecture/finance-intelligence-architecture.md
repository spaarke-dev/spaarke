# Finance Intelligence Module R1 — Technical Architecture

> **Version**: 1.1
> **Last Updated**: 2026-02-13
> **Status**: Implementation Complete - Pending Deployment

---

## Table of Contents

1. [Executive Summary](#executive-summary)
2. [System Overview](#system-overview)
3. [Architecture Diagram](#architecture-diagram)
4. [Component Architecture](#component-architecture)
5. [Data Model](#data-model)
6. [Job Pipeline](#job-pipeline)
7. [AI Integration](#ai-integration)
8. [API Specifications](#api-specifications)
9. [Key Methods and Interfaces](#key-methods-and-interfaces)
10. [Technology Stack](#technology-stack)
11. [Security Considerations](#security-considerations)
12. [Performance Characteristics](#performance-characteristics)
13. [Deployment Architecture](#deployment-architecture)

---

## Executive Summary

The Finance Intelligence Module R1 extends Spaarke's email-to-document pipeline with AI-powered invoice processing capabilities. The system provides end-to-end automation for invoice classification, human-in-the-loop review, structured billing fact extraction, pre-computed spend analytics, and semantic search.

**Key Capabilities**:
- AI classification of email attachments as invoice candidates (gpt-4o-mini)
- Human review workflow with matter/vendor association
- AI extraction of billing line items with structured output (gpt-4o)
- Deterministic spend aggregation with budget variance and velocity metrics
- Threshold-based spend signals (budget warnings, velocity spikes)
- Invoice-specific semantic search with contextual metadata enrichment
- Finance visualization via VisualHost + denormalized fields

**Architectural Highlights**:
- **Hybrid VisualHost Architecture**: Denormalized finance fields on Matter/Project entities + native Dataverse charts (replaced custom PCF)
- **Structured Output Foundation**: Reusable `GetStructuredCompletionAsync<T>` platform capability using OpenAI's constrained decoding
- **Idempotency via Alternate Keys**: BillingEvent and SpendSnapshot use composite keys for safe re-runs
- **VisibilityState Determinism**: Workflow states set in code, never by AI — prevents hallucination
- **Contextual Metadata Enrichment**: Semantic search quality improvement via metadata prepending before vectorization

---

## System Overview

### High-Level Architecture

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         SPAARKE FINANCE INTELLIGENCE                     │
└─────────────────────────────────────────────────────────────────────────┘

┌──────────────────┐      ┌──────────────────┐      ┌──────────────────┐
│  Email Gateway   │─────▶│  Classification  │─────▶│  Review Queue    │
│  (Existing)      │      │  (AI + Matching) │      │  (Dataverse)     │
└──────────────────┘      └──────────────────┘      └──────────────────┘
                                   │                          │
                                   ▼                          ▼
                          sprk_document fields      ┌──────────────────┐
                          (classification,          │  Human Review    │
                           confidence, hints)       │  (Confirm/Reject)│
                                                     └──────────────────┘
                                                              │
                          ┌───────────────────────────────────┘
                          │
                          ▼
                   ┌──────────────────┐
                   │   Extraction     │
                   │   (AI Structured │
                   │    Output)       │
                   └──────────────────┘
                          │
              ┌───────────┴───────────┐
              ▼                       ▼
     ┌──────────────────┐    ┌──────────────────┐
     │  Snapshot Gen    │    │  Invoice Index   │
     │  (Aggregation)   │    │  (Semantic Search)│
     └──────────────────┘    └──────────────────┘
              │
              ▼
     ┌──────────────────┐
     │  Signal Detect   │
     │  (Thresholds)    │
     └──────────────────┘
              │
              ▼
     ┌──────────────────┐
     │  Finance Summary │
     │  (Redis Cache)   │
     └──────────────────┘
              │
              ▼
     ┌──────────────────┐
     │  VisualHost      │
     │  (Charts)        │
     └──────────────────┘
```

### Component Layers

| Layer | Components | Responsibility |
|-------|------------|----------------|
| **Ingestion** | EmailToDocumentJobHandler (existing) | Email intake, SPE file storage, Document record creation |
| **Classification** | AttachmentClassificationJobHandler, IInvoiceAnalysisService | AI classification, entity matching, hint extraction |
| **Review** | Dataverse filtered view, FinanceReviewEndpoints | Human review queue, confirm/reject workflow |
| **Extraction** | InvoiceExtractionJobHandler, IInvoiceAnalysisService | AI billing fact extraction with structured output |
| **Analytics** | SpendSnapshotGenerationJobHandler, SpendSnapshotService, SignalEvaluationService | Spend aggregation, budget variance, velocity metrics, threshold alerts |
| **Search** | InvoiceIndexingJobHandler, InvoiceSearchService, Azure AI Search | Invoice indexing with contextual enrichment, hybrid search |
| **API** | FinanceReviewEndpoints, FinanceSummaryEndpoint, InvoiceSearchEndpoint | REST endpoints for review, summary, search |
| **Visualization** | VisualHost charts, denormalized fields | Budget gauge, spend timeline on Matter form |

---

## Architecture Diagram

### System Context

```
┌────────────────────────────────────────────────────────────────────────┐
│                          EXTERNAL SYSTEMS                               │
├────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────┐   ┌──────────────┐   ┌──────────────┐              │
│  │ Email Server │   │ Azure OpenAI │   │ AI Search    │              │
│  │ (Exchange)   │   │ (gpt-4o-mini,│   │ (Invoice     │              │
│  └──────┬───────┘   │  gpt-4o)     │   │  Index)      │              │
│         │           └──────┬───────┘   └──────┬───────┘              │
└─────────┼──────────────────┼──────────────────┼────────────────────────┘
          │                  │                  │
          ▼                  ▼                  ▼
┌────────────────────────────────────────────────────────────────────────┐
│                       SPAARKE BFF API (.NET 8)                          │
├────────────────────────────────────────────────────────────────────────┤
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐ │
│  │                    FINANCE INTELLIGENCE MODULE                    │ │
│  ├──────────────────────────────────────────────────────────────────┤ │
│  │                                                                   │ │
│  │  AI Services         │  Job Handlers      │  Endpoints           │ │
│  │  ─────────────       │  ──────────────    │  ──────────          │ │
│  │  • InvoiceAnalysis   │  • Classification  │  • Review            │ │
│  │  • SpendSnapshot     │  • Extraction      │  • Summary           │ │
│  │  • SignalEvaluation  │  • Snapshot        │  • Search            │ │
│  │  • InvoiceSearch     │  • Indexing        │                      │ │
│  │                                                                   │ │
│  └──────────────────────────────────────────────────────────────────┘ │
│                                                                         │
│  ┌──────────────────────────────────────────────────────────────────┐ │
│  │                     PLATFORM SERVICES                             │ │
│  ├──────────────────────────────────────────────────────────────────┤ │
│  │  • IOpenAiClient (GetStructuredCompletionAsync<T>)               │ │
│  │  • IDataverseService (Entity CRUD)                               │ │
│  │  • IRecordMatchService (Entity matching)                         │ │
│  │  • ITextExtractorService (Document Intelligence)                 │ │
│  │  • IDistributedCache (Redis)                                     │ │
│  │  • IJobSubmissionService (Service Bus)                           │ │
│  └──────────────────────────────────────────────────────────────────┘ │
│                                                                         │
└─────────┬───────────────────────────────────────────────┬───────────────┘
          │                                               │
          ▼                                               ▼
┌────────────────────────┐                    ┌────────────────────────┐
│ Microsoft Dataverse    │                    │ Azure Redis Cache      │
│ (Entities, Views)      │                    │ (Finance Summary)      │
└────────────────────────┘                    └────────────────────────┘
```

### Job Pipeline Flow

```
┌────────────────────────────────────────────────────────────────────────┐
│                        FINANCE JOB PIPELINE                             │
└────────────────────────────────────────────────────────────────────────┘

Email Arrival
     │
     ▼
┌─────────────────────────┐
│ EmailToDocumentJob      │ (Existing)
│ Creates:                │
│ - sprk_document (Email) │
│ - sprk_document (Attach)│
└──────────┬──────────────┘
           │ (Feature flag: AutoClassifyAttachments)
           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ AttachmentClassificationJob                                             │
│ ───────────────────────────                                             │
│ 1. Load attachment from SPE via ISpeFileOperations                      │
│ 2. Extract text via ITextExtractorService (Document Intelligence)       │
│ 3. Classify via IInvoiceAnalysisService.ClassifyAttachmentAsync()      │
│    → AI call: gpt-4o-mini with structured output                       │
│    → Returns: ClassificationResult (enum, confidence, hints)            │
│ 4. Entity matching via IRecordMatchService (matter/vendor suggestions) │
│ 5. Update sprk_document fields:                                        │
│    - sprk_classification (InvoiceCandidate/NotInvoice/Unknown)         │
│    - sprk_classificationconfidence (decimal 0-1)                       │
│    - sprk_vendornamehint, sprk_invoicenumberhint, etc. (6 hint fields)│
│    - sprk_mattersuggestedref, sprk_mattersuggestionjson (top 5)       │
│    - sprk_invoicereviewstatus = ToReview                               │
└──────────┬──────────────────────────────────────────────────────────────┘
           │ (Candidates/Unknowns appear in Review Queue view)
           │
           │ ◄──── HUMAN REVIEW GATE ────►
           │ (User confirms via POST /api/finance/invoice-review/confirm)
           │
           ▼
┌─────────────────────────────────────────────────────────────────────────┐
│ InvoiceExtractionJob                                                    │
│ ────────────────────                                                    │
│ 1. Load sprk_invoice + sprk_document records                           │
│ 2. Get reviewer-corrected hints from sprk_invoice                      │
│ 3. Load document text from SPE via ISpeFileOperations                  │
│ 4. Extract via IInvoiceAnalysisService.ExtractInvoiceFactsAsync()     │
│    → AI call: gpt-4o with structured output                           │
│    → Returns: ExtractionResult (header + line items array)             │
│ 5. Create sprk_billingevent records:                                   │
│    - Alternate key: sprk_invoiceid + sprk_linesequence                 │
│    - VisibilityState = Invoiced (set deterministically, NOT by AI)     │
│    - Upsert via alternate key for idempotency                          │
│ 6. Set sprk_invoice.sprk_status = Reviewed                             │
│ 7. Enqueue downstream jobs: SpendSnapshotGeneration, InvoiceIndexing   │
└──────────┬──────────────────────────┬────────────────────────────────────┘
           │                          │
           │                          │
     ┌─────┴─────┐           ┌────────┴────────┐
     ▼           ▼           ▼                 ▼
┌─────────────────────┐ ┌─────────────────────────────────────────────────┐
│ SpendSnapshotGenJob │ │ InvoiceIndexingJob                              │
│ ─────────────────── │ │ ──────────────────                              │
│ 1. Load matter      │ │ 1. Load sprk_invoice + related entities         │
│ 2. Load budget plan │ │    (sprk_organization, sprk_matter/project)    │
│ 3. Aggregate events │ │ 2. Load document text from SPE                  │
│ 4. Compute variance │ │ 3. Chunk text via ITextChunkingService          │
│ 5. Compute velocity │ │ 4. Enrich chunks with metadata header:          │
│ 6. Upsert snapshots │ │    "Firm: {vendor} | Matter: {name} ({num}) |  │
│    (via alt key)    │ │     Invoice: {num} | Date: {date} | Total: {$}"│
│ 7. Evaluate signals │ │ 5. Generate embeddings via IOpenAiClient        │
│ 8. Create signals   │ │    (text-embedding-3-large)                     │
│    if thresholds    │ │ 6. Index to Azure AI Search                     │
│    breached         │ │    (spaarke-invoices-{tenantId})                │
│ 9. Update denorm    │ │ 7. Store typed metadata fields:                 │
│    fields on Matter │ │    - invoiceDate (DateTimeOffset)               │
│ 10. Invalidate      │ │    - totalAmount (Double)                       │
│     Redis cache     │ │    - vendorName (string, filterable)            │
└─────────────────────┘ └─────────────────────────────────────────────────┘
```

---

## Component Architecture

### Entities (Dataverse Schema)

#### New Entities

| Entity | Logical Name | Fields | Alternate Key | Purpose |
|--------|-------------|--------|---------------|---------|
| **Invoice** | `sprk_invoice` | 13 | None | Lightweight confirmed invoice artifact linking Document to Matter/Vendor |
| **BillingEvent** | `sprk_billingevent` | 13 + alt key | `sprk_invoice` + `sprk_linesequence` | Canonical financial fact (one per invoice line item) |
| **Budget** | `sprk_budget` | 12 | None | Budget plan header (matter-level OR project-level, 1:N relationship - matters may span multiple budget cycles) |
| **BudgetBucket** | `sprk_budgetbucket` | 8 | None | Budget allocation by bucket/period (optional subdivisions of Budget.TotalBudget) |
| **SpendSnapshot** | `sprk_spendsnapshot` | 16 + alt key | 5-field composite (matter + project + period + periodkey + bucketkey + visibilityfilter) | Pre-computed spend aggregation with velocity metrics |
| **SpendSignal** | `sprk_spendsignal` | 12 | None | Threshold-based alerts (budget warnings, velocity spikes) |

#### Extended Entity

| Entity | New Fields | Purpose |
|--------|-----------|---------|
| **Document** (`sprk_document`) | 13 fields | Classification results (3), invoice hints (6), associations (4) |

**Classification Fields**:
- `sprk_classification` (OptionSet): InvoiceCandidate=1, NotInvoice=2, Unknown=3
- `sprk_classificationconfidence` (Decimal 0-1)
- `sprk_invoicereviewstatus` (OptionSet): ToReview=1, Confirmed=2, RejectedNotInvoice=3

**Hint Fields** (populated by AI):
- `sprk_vendornamehint`, `sprk_invoicenumberhint`, `sprk_invoicedatehint`, `sprk_totalamounthint`, `sprk_currencyhint`, `sprk_matterreferencehint`

**Association Fields**:
- `sprk_mattersuggestedref` (string - top match reference)
- `sprk_mattersuggestionjson` (multiline text - top 5 ranked matches)
- `sprk_invoicereviewedby` (Lookup → sprk_contact)
- `sprk_invoicereviewedon` (DateTime)

**Denormalized Finance Fields** (on `sprk_matter` and `sprk_project`):
- `sprk_budget`, `sprk_currentspend`, `sprk_budgetvariance`
- `sprk_budgetutilizationpct`, `sprk_velocitypct`, `sprk_lastfinanceupdatedate`

#### Budget Relationship Model

**Matter and Project Independence**:
- Matter and Project entities are **independent** (no required linkage)
- Invoices can be associated with Matter **OR** Project (mutually exclusive in MVP)
- Budgets can be linked to Matter **OR** Project (separate budget plans)

**Budget 1:N Relationship**:
- A Matter or Project can have **multiple Budget records** (1:N relationship)
- **Rationale**: Matters/projects may span multiple fiscal periods or budget cycles
- **Total Budget Calculation**: Sum of `Budget.sprk_totalbudget` across ALL Budget records for the Matter/Project
- **NOT TopCount=1**: Financial calculations must SUM all Budget records, not just the first

**Budget Bucket Design**:
- BudgetBucket entities are **optional** subdivisions of `Budget.sprk_totalbudget`
- `Budget.sprk_totalbudget` is the authoritative budget amount
- Budget Buckets enable category-level tracking but are not required for variance calculations
- If no Budget Buckets exist, total budget from Budget entity is used directly

#### Views

| View | Entity | Filter Criteria |
|------|--------|-----------------|
| **Invoice Review Queue** | `sprk_document` | `documenttype=EmailAttachment` AND `classification IN (InvoiceCandidate, Unknown)` AND `reviewstatus=ToReview` |
| **Active Invoices** | `sprk_invoice` | Standard active view (statecode=0) |

---

### Services

#### IInvoiceAnalysisService

**Location**: `src/server/api/Sprk.Bff.Api/Services/Finance/IInvoiceAnalysisService.cs`

**Purpose**: AI-powered invoice classification and extraction using structured output.

**Methods**:
```csharp
public interface IInvoiceAnalysisService
{
    /// <summary>
    /// Classifies an attachment as InvoiceCandidate, NotInvoice, or Unknown
    /// with confidence score and invoice hints.
    /// </summary>
    /// <param name="documentText">Extracted text from document</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Classification result with confidence and hints</returns>
    Task<ClassificationResult> ClassifyAttachmentAsync(
        string documentText,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Extracts structured billing facts from a confirmed invoice.
    /// Uses reviewer-corrected hints as inputs.
    /// </summary>
    /// <param name="documentText">Extracted text from invoice</param>
    /// <param name="invoiceHints">Reviewer-corrected hints from sprk_invoice</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Extraction result with header and line items</returns>
    Task<ExtractionResult> ExtractInvoiceFactsAsync(
        string documentText,
        InvoiceHints invoiceHints,
        CancellationToken cancellationToken = default);
}
```

**Implementation**: Uses `IOpenAiClient.GetStructuredCompletionAsync<T>` with playbook prompts loaded from Dataverse `sprk_playbook` records.

---

#### ISpendSnapshotService

**Location**: `src/server/api/Sprk.Bff.Api/Services/Finance/ISpendSnapshotService.cs`

**Purpose**: Deterministic spend aggregation with budget variance and velocity metrics for both Matter and Project entities.

**Methods**:
```csharp
public interface ISpendSnapshotService
{
    /// <summary>
    /// Generates spend snapshots for a matter across all periods and buckets.
    /// Computes budget variance and Month-over-Month velocity.
    /// </summary>
    /// <param name="matterId">Matter GUID</param>
    /// <param name="correlationId">Optional correlation ID for traceability</param>
    /// <param name="ct">Cancellation token</param>
    Task GenerateAsync(Guid matterId, string? correlationId = null, CancellationToken ct = default);

    /// <summary>
    /// Generates spend snapshots for a project across all periods and buckets.
    /// Computes budget variance and Month-over-Month velocity.
    /// </summary>
    /// <param name="projectId">Project GUID</param>
    /// <param name="correlationId">Optional correlation ID for traceability</param>
    /// <param name="ct">Cancellation token</param>
    Task GenerateForProjectAsync(Guid projectId, string? correlationId = null, CancellationToken ct = default);
}
```

**Key Calculations**:
- **Budget Variance**: `Variance = SUM(Budget.sprk_totalbudget) - Invoiced` (positive = under budget, negative = over budget)
- **Total Budget**: Sum of ALL Budget records for Matter/Project (not just first record)
- **Month-over-Month Velocity**: `VelocityPct = (current month - prior month) / prior month × 100`
- **Snapshot Periods (MVP)**: Month, ToDate (Quarter/Year post-MVP)
- **Bucket Keys (MVP)**: "TOTAL" only (category breakdowns post-MVP)

**Alternate Key for Idempotency**: `sprk_matter/sprk_project + sprk_periodtype + sprk_periodkey + sprk_bucketkey + sprk_visibilityfilter`

---

#### ISignalEvaluationService

**Location**: `src/server/api/Sprk.Bff.Api/Services/Finance/ISignalEvaluationService.cs`

**Purpose**: Threshold-based spend signal detection.

**Methods**:
```csharp
public interface ISignalEvaluationService
{
    /// <summary>
    /// Evaluates spend snapshots against configured thresholds.
    /// Creates SpendSignal records when thresholds breached.
    /// </summary>
    /// <param name="snapshots">Spend snapshots for matter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of signals created</returns>
    Task<IReadOnlyList<SpendSignal>> EvaluateSignalsAsync(
        IReadOnlyList<SpendSnapshot> snapshots,
        CancellationToken cancellationToken = default);
}
```

**Signal Types**:
| Signal Type | Threshold | Severity |
|------------|-----------|----------|
| **BudgetExceeded** | 100%+ utilization | Critical |
| **BudgetWarning** | 80%+ utilization (configurable) | Warning |
| **VelocitySpike** | 50%+ increase (configurable) | Warning |

---

#### IInvoiceSearchService

**Location**: `src/server/api/Sprk.Bff.Api/Services/Finance/IInvoiceSearchService.cs`

**Purpose**: Hybrid invoice search (keyword + vector + semantic ranking) with filters.

**Methods**:
```csharp
public interface IInvoiceSearchService
{
    /// <summary>
    /// Searches invoices with hybrid search (keyword + vector + semantic ranking).
    /// Supports filtering by vendor, amount range, date range.
    /// </summary>
    /// <param name="request">Search request with query and filters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Search results with relevance scores</returns>
    Task<InvoiceSearchResult> SearchAsync(
        InvoiceSearchRequest request,
        CancellationToken cancellationToken = default);
}
```

**Azure AI Search Index**: `spaarke-invoices-{tenantId}`

**Index Fields**:
- **Searchable content**: `content` (enriched with contextual metadata before vectorization)
- **Vector field**: `contentVector` (3072 dimensions, text-embedding-3-large)
- **Filterable metadata**: `vendorName`, `matterName`, `matterNumber`, `invoiceNumber`, `invoiceDate`, `totalAmount`, `currency`

---

#### Lookup Services (Portable Alternate Keys)

**Purpose**: Cached lookup services for AI configuration records using alternate keys for SaaS multi-environment portability.

**Pattern**: All lookup services follow the same implementation pattern:
- **IMemoryCache** with 1-hour TTL
- **Alternate key lookups** via `IDataverseService.RetrieveByAlternateKeyAsync()`
- **GUIDs regenerate** across environments; alternate keys (code fields) travel with solution imports

**Implemented Services**:

| Service | Entity | Alternate Key Field | Purpose |
|---------|--------|---------------------|---------|
| `IPlaybookLookupService` | `sprk_playbook` | `sprk_playbookcode` | AI prompt template resolution |
| `IActionLookupService` | `sprk_analysisaction` | `sprk_actioncode` | Analysis action resolution |
| `ISkillLookupService` | `sprk_analysisskill` | `sprk_skillcode` | Analysis skill resolution |
| `IToolLookupService` | `sprk_analysistool` | `sprk_toolcode` | Analysis tool resolution |

**Example Interface** (`IPlaybookLookupService`):
```csharp
public interface IPlaybookLookupService
{
    /// <summary>
    /// Get playbook by portable code (alternate key).
    /// Results are cached for 1 hour to minimize Dataverse queries.
    /// </summary>
    /// <param name="playbookCode">Portable playbook code (e.g., "PB-001")</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Playbook entity with ID and metadata</returns>
    Task<PlaybookResponse> GetByCodeAsync(string playbookCode, CancellationToken ct = default);

    void ClearCache(string playbookCode);
    void ClearAllCache();
}
```

**Performance Characteristics**:
- First lookup: ~50-100ms (Dataverse query + cache write)
- Cached lookups: <1ms (in-memory)
- Cache TTL: 1 hour (configuration rarely changes)
- Memory usage: ~1KB per cached record (negligible)

**Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/*LookupService.cs`

---

### Job Handlers

#### AttachmentClassificationJobHandler

**Location**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/AttachmentClassificationJobHandler.cs`

**Job Contract**:
```json
{
  "jobType": "AttachmentClassification",
  "documentId": "{guid}",
  "correlationId": "{guid}"
}
```

**Execution Flow**:
1. Load `sprk_document` record by documentId
2. Load document bytes from SPE via `ISpeFileOperations.GetFileAsync()`
3. Extract text via `ITextExtractorService.ExtractTextAsync()` (Document Intelligence)
4. Classify via `IInvoiceAnalysisService.ClassifyAttachmentAsync()`
   - AI call: gpt-4o-mini with structured output
   - Playbook: "Attachment Classification" (Playbook A)
5. Entity matching via `IRecordMatchService.FindMatchingRecordsAsync()` (matter/vendor)
6. Update `sprk_document` fields (classification, confidence, hints, suggestions, reviewstatus)
7. If InvoiceCandidate, create `sprk_invoice` record (Status=ToReview, ExtractionStatus=NotRun)

**Idempotency**: Safe to re-run (updates document fields, creates invoice if missing)

**Feature Flag**: Controlled by `EmailProcessingOptions.AutoClassifyAttachments` (default: false)

---

#### InvoiceExtractionJobHandler

**Location**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs`

**Job Contract**:
```json
{
  "jobType": "InvoiceExtraction",
  "invoiceId": "{guid}",
  "correlationId": "{guid}"
}
```

**Execution Flow**:
1. Load `sprk_invoice` record by invoiceId
2. Load linked `sprk_document` record
3. Get reviewer-corrected hints from `sprk_invoice` (invoice number, date, total)
4. Load document text from SPE via `ISpeFileOperations.GetFileAsync()`
5. Extract via `IInvoiceAnalysisService.ExtractInvoiceFactsAsync()`
   - AI call: gpt-4o with structured output
   - Playbook: "Invoice Extraction" (Playbook B)
   - Input: document text + reviewer-corrected hints
6. Create/update `sprk_billingevent` records:
   - Alternate key: `sprk_invoiceid + sprk_linesequence`
   - Set `VisibilityState = Invoiced` (deterministic, NOT from AI)
   - Upsert via alternate key for idempotency
7. Set `sprk_invoice.sprk_status = Reviewed` (only handler that transitions status)
8. Set `sprk_invoice.sprk_extractionstatus = Completed`
9. Enqueue downstream jobs: `SpendSnapshotGeneration`, `InvoiceIndexing`

**Idempotency**: Re-running extraction produces idempotent upserts (alternate key ensures no duplicates)

**VisibilityState Determinism (NFR-02)**: VisibilityState set in handler code, never inferred by AI. Extraction prompt explicitly excludes VisibilityState output.

---

#### SpendSnapshotGenerationJobHandler

**Location**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/SpendSnapshotGenerationJobHandler.cs`

**Job Contract** (supports both Matter and Project):
```json
{
  "jobType": "SpendSnapshotGeneration",
  "matterId": "{guid}",        // OR projectId (mutually exclusive)
  "projectId": "{guid}",        // OR matterId (mutually exclusive)
  "correlationId": "{guid}"
}
```

**Execution Flow**:
1. Load matter/project and budget plan
2. Load all `sprk_billingevent` records for matter/project (filtered by `VisibilityState = Invoiced`)
3. Call `ISpendSnapshotService.GenerateAsync(matterId)` OR `GenerateForProjectAsync(projectId)`
   - Query ALL Budget records for matter/project (1:N relationship)
   - Sum `Budget.sprk_totalbudget` across all Budget records
   - Aggregate events by period (Month, ToDate)
   - Compute budget variance: `SUM(Budget.sprk_totalbudget) - Invoiced`
   - Compute Month-over-Month velocity: `(current - prior) / prior × 100`
4. Upsert `sprk_spendsnapshot` records via alternate key
5. Call `ISignalEvaluationService.EvaluateSignalsAsync(snapshots)`
   - Detect threshold breaches (BudgetExceeded, BudgetWarning, VelocitySpike)
   - Create `sprk_spendsignal` records
6. Update denormalized fields on `sprk_matter` or `sprk_project`:
   - `sprk_budget`, `sprk_currentspend`, `sprk_budgetvariance`
   - `sprk_budgetutilizationpct`, `sprk_velocitypct`, `sprk_lastfinanceupdatedate`
7. Invalidate Redis cache key: `matter:{matterId}:finance-summary` or `project:{projectId}:finance-summary`

**Idempotency**: Upsert via composite alternate key ensures safe re-runs

---

#### InvoiceIndexingJobHandler

**Location**: `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/InvoiceIndexingJobHandler.cs`

**Job Contract**:
```json
{
  "jobType": "InvoiceIndexing",
  "invoiceId": "{guid}",
  "correlationId": "{guid}"
}
```

**Execution Flow**:
1. Load `sprk_invoice` record
2. Load related entities: `sprk_organization` (vendor), `sprk_matter`/`sprk_project`, `sprk_document`
3. Load document text from SPE
4. Chunk text via `ITextChunkingService.ChunkTextAsync()` (500 tokens per chunk, 50 token overlap)
5. **Contextual metadata enrichment** (critical for search quality):
   - For each chunk, prepend metadata header before vectorization:
   ```
   Firm: {vendorName} | Matter: {matterName} ({matterNumber}) | Invoice: {invoiceNumber} | Date: {invoiceDate} | Total: {currency} {totalAmount}
   ---
   {original chunk text}
   ```
6. Generate embeddings via `IOpenAiClient.GenerateEmbeddingsAsync()` (text-embedding-3-large, 3072 dimensions)
7. Create search documents with:
   - `content`: Enriched chunk text
   - `contentVector`: Embedding vector
   - Typed metadata fields: `invoiceDate`, `totalAmount`, `vendorName`, `matterName`, etc.
8. Index to Azure AI Search: `spaarke-invoices-{tenantId}`

**Idempotency**: Indexing by invoiceId; re-running replaces existing documents

**Search Quality Impact**: Enrichment ensures semantic search for "high cost research in the Smith matter" captures vendor, matter, and amount in a single vector.

---

### API Endpoints

#### POST /api/finance/invoice-review/confirm

**Purpose**: Confirm attachment as invoice, link to matter/vendor, trigger extraction.

**Request**:
```json
{
  "documentId": "{guid}",
  "matterId": "{guid}",          // OR projectId (mutually exclusive)
  "projectId": "{guid}",          // OR matterId (mutually exclusive)
  "vendorOrgId": "{guid}",
  "invoiceNumber": "INV-2026-001",  // Reviewer-corrected
  "invoiceDate": "2026-01-15",       // Reviewer-corrected
  "totalAmount": 15000.00,           // Reviewer-corrected
  "currency": "USD"                  // Reviewer-corrected
}
```

**Response** (202 Accepted):
```json
{
  "jobId": "{guid}",
  "invoiceId": "{guid}",
  "statusUrl": "/api/jobs/{jobId}/status"
}
```

**Logic**:
1. Validate required fields (documentId, matterId OR projectId, vendorOrgId)
2. Load `sprk_document` record
3. Verify `sprk_classification` IN (InvoiceCandidate, Unknown)
4. Create `sprk_invoice` record:
   - Link to document, matter/project, vendor org
   - Store reviewer-corrected values (invoice number, date, total, currency)
   - Set `sprk_status = ToReview`
   - Set `sprk_extractionstatus = NotRun`
5. Update `sprk_document.sprk_invoicereviewstatus = Confirmed`
6. Enqueue `InvoiceExtraction` job
7. Return 202 + jobId + invoiceId + statusUrl

**Authorization**: `FinanceAuthorizationFilter` (ADR-008: endpoint-level authorization)

---

#### POST /api/finance/invoice-review/reject

**Purpose**: Reject attachment as not an invoice.

**Request**:
```json
{
  "documentId": "{guid}"
}
```

**Response** (200 OK):
```json
{
  "documentId": "{guid}",
  "reviewStatus": "RejectedNotInvoice",
  "reviewedBy": "{userId}",
  "reviewedOn": "2026-01-15T10:30:00Z"
}
```

**Logic**:
1. Load `sprk_document` record
2. Set `sprk_invoicereviewstatus = RejectedNotInvoice`
3. Set `sprk_invoicereviewedby` (current user)
4. Set `sprk_invoicereviewedon` (current timestamp)
5. No invoice created, no downstream jobs
6. Document retained in Dataverse (never deleted)

---

#### GET /api/finance/matters/{matterId}/summary

**Purpose**: Finance summary for matter (Redis-cached, 5-min TTL). Also available for projects.

**Endpoints**:
- `GET /api/finance/matters/{matterId}/summary`
- `GET /api/finance/projects/{projectId}/summary`

**Response** (200 OK):
```json
{
  "matterId": "{guid}",           // OR projectId
  "budget": 100000.00,             // Sum of ALL Budget records
  "currentSpend": 75000.00,
  "budgetRemaining": 25000.00,
  "budgetUtilizationPct": 75.0,
  "budgetVariance": 25000.00,
  "velocityPct": 15.5,
  "openSignalsCount": 2,
  "lastUpdated": "2026-01-15T14:30:00Z"
}
```

**Logic**:
1. Check Redis cache: `matter:{matterId}:finance-summary` or `project:{projectId}:finance-summary`
2. If cached, return (TTL: 5 minutes)
3. If not cached:
   - Query Dataverse for ALL Budget records (1:N) and sum `sprk_totalbudget`
   - Query latest `sprk_spendsnapshot` records (ToDate period)
   - Query `sprk_spendsignal` for open signals
   - Compute summary
   - Cache in Redis (5-min TTL)
   - Return

**Cache Invalidation**: Explicit deletion after `SpendSnapshotGenerationJobHandler` completes

---

#### GET /api/finance/invoices/search

**Purpose**: Hybrid invoice search (keyword + vector + semantic ranking).

**Query Parameters**:
```
?query=high cost research in the Smith matter
&vendor=Smith & Associates
&minAmount=1000
&maxAmount=20000
&dateFrom=2026-01-01
&dateTo=2026-01-31
&top=20
```

**Response** (200 OK):
```json
{
  "results": [
    {
      "invoiceId": "{guid}",
      "invoiceNumber": "INV-2026-042",
      "vendorName": "Smith & Associates LLP",
      "matterName": "Acme v. Beta Corp",
      "matterNumber": "2026-001",
      "invoiceDate": "2026-01-15",
      "totalAmount": 15000.00,
      "currency": "USD",
      "relevanceScore": 0.95,
      "highlights": [
        "...Firm: Smith & Associates LLP | Matter: Acme v. Beta Corp (2026-001) | Invoice: INV-2026-042..."
      ]
    }
  ],
  "totalCount": 1
}
```

**Logic**:
1. Call `IInvoiceSearchService.SearchAsync(request)`
2. Azure AI Search hybrid query:
   - Keyword search on `content` field
   - Vector search on `contentVector` field (cosine similarity)
   - Semantic ranking (if configured)
3. Apply filters: `vendorName`, `totalAmount` (range), `invoiceDate` (range)
4. Return results with highlights

---

## Data Model

### Entity Relationships

```
sprk_document (Email Attachment)
    │
    ├──[classification]──────────────┐
    │                                 │
    ▼                                 ▼
sprk_invoice                    (Review Queue View)
    │
    ├──[extraction]──────────────────▶ sprk_billingevent
    │                                       │
    │                                       │ [aggregation]
    │                                       ▼
    │                                  sprk_spendsnapshot
    │                                       │
    │                                       │ [threshold detection]
    │                                       ▼
    │                                  sprk_spendsignal
    │
    └──[indexing]────────────────────▶ Azure AI Search (spaarke-invoices-{tenantId})

sprk_matter / sprk_project
    │
    ├──[denormalized fields]──────────▶ VisualHost Charts
    │                                   (Budget Gauge, Spend Timeline)
    │
    └──[budget plan]───────────────────▶ sprk_budget
                                             │
                                             └──▶ sprk_budgetbucket
```

### BillingEvent Schema

```csharp
public class BillingEvent
{
    public Guid Id { get; set; }  // Primary key
    public Guid InvoiceId { get; set; }  // FK to sprk_invoice
    public int LineSequence { get; set; }  // Line number (1-based)
    public Guid MatterId { get; set; }  // FK to sprk_matter
    public string CostType { get; set; }  // "Professional", "Expense", "Disbursement"
    public decimal Amount { get; set; }
    public string Currency { get; set; }  // "USD", "EUR", "GBP"
    public DateOnly EventDate { get; set; }  // Date of service or expense
    public string Description { get; set; }
    public string TimekeeperRoleClass { get; set; }  // "Partner", "Associate", "Paralegal", "Other"
    public decimal? Hours { get; set; }  // Nullable (null for non-time entries)
    public string VisibilityState { get; set; }  // "Invoiced" (MVP - deterministic, not AI)
    public Guid CorrelationId { get; set; }  // Traceability only (NOT in alternate key)

    // Alternate Key: InvoiceId + LineSequence (enables idempotent upsert)
    public string AlternateKey => $"{InvoiceId}-{LineSequence}";
}
```

**VisibilityState Values (MVP)**:
- `Invoiced`: Billing event from vendor invoice (only value in MVP)
- Post-MVP: `InternalWIP`, `PreBill` (for firm-side time entry)

**Alternate Key Design**: Excludes `CorrelationId` to support idempotent re-extraction with new correlation IDs.

---

### SpendSnapshot Schema

```csharp
public class SpendSnapshot
{
    public Guid Id { get; set; }  // Primary key
    public Guid MatterId { get; set; }  // FK to sprk_matter
    public string PeriodType { get; set; }  // "Month", "ToDate" (MVP); "Quarter", "Year" post-MVP
    public string PeriodKey { get; set; }  // "2026-01" (Month), "2026-Q1" (Quarter), "2026" (Year)
    public string BucketKey { get; set; }  // Budget bucket identifier (or "All" for total)
    public string VisibilityFilter { get; set; }  // "Invoiced" (MVP)
    public decimal InvoicedAmount { get; set; }
    public decimal BudgetAmount { get; set; }
    public decimal BudgetVariance { get; set; }  // Budget - Invoiced
    public decimal BudgetUtilizationPct { get; set; }  // (Invoiced / Budget) × 100
    public decimal? VelocityPct { get; set; }  // MoM: (current - prior) / prior × 100
    public DateTime GeneratedAt { get; set; }

    // Alternate Key: MatterId + PeriodType + PeriodKey + BucketKey + VisibilityFilter
    public string AlternateKey => $"{MatterId}-{PeriodType}-{PeriodKey}-{BucketKey}-{VisibilityFilter}";
}
```

**Budget Variance Interpretation**:
- Positive: Under budget (good)
- Negative: Over budget (alert)

**Velocity Calculation (MVP: MoM only)**:
- `VelocityPct = (current month - prior month) / prior month × 100`
- Null when prior period is zero (avoid division by zero)

---

## Job Pipeline

### Job Types

| JobType | Trigger | AI? | Idempotent? |
|---------|---------|-----|-------------|
| `AttachmentClassification` | EmailToDocumentJobHandler (feature-flagged) | Yes (gpt-4o-mini) | Yes (updates document fields) |
| `InvoiceExtraction` | Confirm endpoint creates invoice + enqueues | Yes (gpt-4o) | Yes (alternate key upsert) |
| `SpendSnapshotGeneration` | InvoiceExtractionJobHandler enqueues | No (pure math) | Yes (alternate key upsert) |
| `InvoiceIndexing` | InvoiceExtractionJobHandler enqueues | No (embeddings only) | Yes (index by invoiceId) |

### Job Contract Envelope (ADR-004)

All jobs use standard contract envelope:
```json
{
  "jobType": "{JobTypeName}",
  "correlationId": "{guid}",
  "idempotencyKey": "{deterministic-key}",
  "payload": {
    // Job-specific fields
  }
}
```

### Correlation ID Propagation

CorrelationId flows through entire job chain:
```
User Action (correlationId: A123)
  → Classification (correlationId: A123)
    → Review (correlationId: A123)
      → Extraction (correlationId: A123)
        → Snapshot (correlationId: A123)
        → Indexing (correlationId: A123)
```

Used for tracing, NOT identity (excluded from alternate keys).

---

## AI Integration

### Structured Output Platform Capability

**Extension Method**: `IOpenAiClient.GetStructuredCompletionAsync<T>()`

**Location**: `src/server/api/Sprk.Bff.Api/Services/Ai/OpenAiClient.cs`

**Signature**:
```csharp
public static async Task<T> GetStructuredCompletionAsync<T>(
    this IOpenAiClient client,
    string prompt,
    string systemMessage,
    string modelDeploymentName,
    CancellationToken cancellationToken = default)
    where T : class
{
    // Implementation uses OpenAI's response_format: json_schema
    // Constrained decoding at token level — model physically cannot violate schema
    // Deserializes response to type T with validation
}
```

**How It Works**:
1. Generate JSON schema from C# type `T` using System.Text.Json.JsonSerializer
2. Call Azure OpenAI with `response_format: { type: "json_schema", schema: {...} }`
3. Model generates output constrained to schema (token-level enforcement)
4. Deserialize JSON response to type `T`
5. Validate against schema
6. Return strongly-typed result

**Benefits**:
- **Type Safety**: Compile-time checks for result structure
- **Zero Hallucination**: Schema enforcement at token level
- **No Parsing**: Direct deserialization, no regex/string manipulation
- **Reusable**: Any module can use this pattern

**Used By**:
- `IInvoiceAnalysisService.ClassifyAttachmentAsync()` → `ClassificationResult`
- `IInvoiceAnalysisService.ExtractInvoiceFactsAsync()` → `ExtractionResult`

---

### AI Playbooks (ADR-014)

**Storage**: Dataverse `sprk_playbook` table

**Schema**:
```
sprk_playbook
  - sprk_name (string): "Attachment Classification", "Invoice Extraction"
  - sprk_prompttemplate (multiline text): Full prompt template
  - sprk_version (string): Semantic version "1.0.0"
  - sprk_model (string): "gpt-4o-mini", "gpt-4o"
  - sprk_tenantid (lookup): Tenant scope
```

**Loading**: `IPlaybookService.GetPlaybookAsync(playbookName, tenantId)` with Redis caching (key: `playbook:{tenantId}:{playbookName}`)

**Versioning**: Prompts versioned via `sprk_version` field. Increment version after tuning. Keep prior versions for rollback.

---

### Playbook A: "Attachment Classification"

**Model**: gpt-4o-mini (fast, cost-effective for high-volume classification)

**Input**: Extracted text from attachment

**Output**: `ClassificationResult`
```csharp
public record ClassificationResult
{
    public InvoiceClassification Classification { get; init; }  // InvoiceCandidate, NotInvoice, Unknown
    public decimal Confidence { get; init; }  // 0-1
    public InvoiceHints? Hints { get; init; }  // Populated if InvoiceCandidate or Unknown
}

public record InvoiceHints
{
    public string? VendorName { get; init; }
    public string? InvoiceNumber { get; init; }
    public string? InvoiceDate { get; init; }
    public decimal? TotalAmount { get; init; }
    public string? Currency { get; init; }
    public string? MatterReferenceString { get; init; }  // Free-text reference (not entity resolution)
}
```

**Prompt Template** (stored in `sprk_playbook`):
```
You are an AI assistant specialized in document classification for legal invoice processing.

Classify the following document as one of:
- InvoiceCandidate: Document appears to be an invoice from a vendor (law firm, consultant, service provider)
- NotInvoice: Document is clearly not an invoice (contract, memo, email, report)
- Unknown: Unable to determine with confidence

If InvoiceCandidate or Unknown, extract invoice hints (vendor name, invoice number, date, total amount, currency, matter reference string).

Do NOT resolve entity references. Return strings only.

Document text:
{documentText}

Return JSON matching the ClassificationResult schema.
```

**Note**: Entity matching (matter/vendor record resolution) performed by handler AFTER AI call using `IRecordMatchService`.

---

### Playbook B: "Invoice Extraction"

**Model**: gpt-4o (high accuracy for complex extraction)

**Input**:
- Extracted text from confirmed invoice
- Reviewer-corrected hints from `sprk_invoice` (invoice number, date, total)

**Output**: `ExtractionResult`
```csharp
public record ExtractionResult
{
    public InvoiceHeader Header { get; init; }
    public IReadOnlyList<LineItem> LineItems { get; init; }
    public decimal ExtractionConfidence { get; init; }
}

public record InvoiceHeader
{
    public string InvoiceNumber { get; init; }
    public DateOnly InvoiceDate { get; init; }
    public DateOnly? BillingPeriodStart { get; init; }
    public DateOnly? BillingPeriodEnd { get; init; }
    public decimal TotalAmount { get; init; }
    public string Currency { get; init; }
}

public record LineItem
{
    public int LineSequence { get; init; }  // 1-based
    public string CostType { get; init; }  // "Professional", "Expense", "Disbursement"
    public string Description { get; init; }
    public decimal Amount { get; init; }
    public DateOnly? ServiceDate { get; init; }
    public string? TimekeeperRoleClass { get; init; }  // "Partner", "Associate", "Paralegal", "Other"
    public decimal? Hours { get; init; }
}
```

**Prompt Template** (stored in `sprk_playbook`):
```
You are an AI assistant specialized in extracting billing facts from legal invoices.

The reviewer has confirmed the following values:
- Invoice Number: {invoiceNumber}
- Invoice Date: {invoiceDate}
- Total Amount: {totalAmount} {currency}

Extract ALL line items from the invoice below. For each line item:
- Assign a sequential line number (1-based)
- Identify cost type: Professional (legal work), Expense (direct costs), Disbursement (out-of-pocket)
- Extract description
- Extract amount
- Extract service date (or use invoice date as fallback)
- Identify timekeeper role class: Partner, Associate, Paralegal, Other
- Extract hours (if time entry)

DO NOT include VisibilityState in the output. This field is set by the system.

Use the date fallback chain:
1. Line item service date (if available)
2. Billing period end date (if available)
3. Invoice date (as last resort)

Invoice text:
{documentText}

Return JSON matching the ExtractionResult schema.
```

**Guardrail**: Prompt explicitly excludes VisibilityState output. Handler sets `VisibilityState = Invoiced` after extraction (NFR-02: deterministic, not AI).

---

## Key Methods and Interfaces

### IOpenAiClient.GetStructuredCompletionAsync<T>

**Purpose**: Reusable structured output capability using OpenAI's `response_format: json_schema`.

**Signature**:
```csharp
Task<T> GetStructuredCompletionAsync<T>(
    string prompt,
    string systemMessage,
    string modelDeploymentName,
    CancellationToken cancellationToken = default)
    where T : class;
```

**Example Usage**:
```csharp
var result = await _openAiClient.GetStructuredCompletionAsync<ClassificationResult>(
    prompt: documentText,
    systemMessage: playbookPrompt,
    modelDeploymentName: "gpt-4o-mini",
    cancellationToken: cancellationToken);
```

---

### IDataverseService Extensions (Finance Entities)

**Purpose**: Extend `IDataverseService` with finance-specific operations.

**Methods** (from Task 049):
```csharp
// Invoice operations
Task<InvoiceEntity?> GetInvoiceAsync(Guid invoiceId, CancellationToken ct);
Task<Guid> CreateInvoiceAsync(InvoiceEntity invoice, CancellationToken ct);
Task UpdateInvoiceAsync(InvoiceEntity invoice, CancellationToken ct);

// BillingEvent operations
Task<IReadOnlyList<BillingEventEntity>> GetBillingEventsForMatterAsync(Guid matterId, CancellationToken ct);
Task UpsertBillingEventAsync(BillingEventEntity billingEvent, CancellationToken ct);

// SpendSnapshot operations
Task<IReadOnlyList<SpendSnapshotEntity>> GetSnapshotsForMatterAsync(Guid matterId, CancellationToken ct);
Task UpsertSnapshotAsync(SpendSnapshotEntity snapshot, CancellationToken ct);

// SpendSignal operations
Task<IReadOnlyList<SpendSignalEntity>> GetOpenSignalsForMatterAsync(Guid matterId, CancellationToken ct);
Task CreateSignalAsync(SpendSignalEntity signal, CancellationToken ct);

// Budget operations
Task<BudgetEntity?> GetActiveBudgetAsync(Guid matterId, CancellationToken ct);
```

---

## Technology Stack

### Backend (.NET 8)

| Component | Technology | Version |
|-----------|------------|---------|
| **Runtime** | .NET | 8.0 |
| **API Framework** | ASP.NET Core Minimal API | 8.0 |
| **Job Processing** | Service Bus + BackgroundService | Azure SDK |
| **AI Integration** | Azure OpenAI | SDK 2.0+ |
| **Document Intelligence** | Azure Document Intelligence | SDK 1.0+ |
| **Search** | Azure AI Search | SDK 11.5+ |
| **Dataverse** | Microsoft.PowerPlatform.Dataverse.Client | 1.1+ |
| **Caching** | IDistributedCache (Redis) | StackExchange.Redis |
| **Testing** | xUnit + NSubstitute + FluentAssertions | Latest |

### Azure Services

| Service | Purpose | SKU/Tier |
|---------|---------|----------|
| **Azure OpenAI** | AI classification (gpt-4o-mini), extraction (gpt-4o), embeddings (text-embedding-3-large) | Standard |
| **Azure Document Intelligence** | PDF/image text extraction | S0 |
| **Azure AI Search** | Invoice semantic search | Standard |
| **Azure Service Bus** | Job queue | Standard |
| **Azure Redis Cache** | Finance summary caching | Standard C1 |
| **Azure App Service** | BFF API hosting | P1V3 |
| **Microsoft Dataverse** | Entity storage, views | Production |

### Frontend (VisualHost)

| Component | Technology |
|-----------|------------|
| **Charts** | Dataverse VisualHost (native) |
| **Chart Types** | Gauge (budget utilization), Line (spend timeline) |
| **Data Source** | Denormalized fields on Matter/Project |
| **Configuration** | XML chart definitions + JSON VisualHost config |

---

## Security Considerations

### Authentication

- **BFF API**: OAuth 2.0 On-Behalf-Of (OBO) flow (ADR-008)
- **Dataverse**: Service principal (app-only) for backend operations
- **Azure OpenAI**: Managed Identity or API Key
- **Azure AI Search**: API Key (admin key for indexing, query key for search)

### Authorization

- **Endpoint-Level**: `FinanceAuthorizationFilter` on all finance endpoints (ADR-008)
- **Entity-Level**: Dataverse security roles for invoice review permissions
- **Matter-Level**: Verify user has access to matter before returning finance summary

### Data Governance (ADR-015)

- **No document content in logs**: Job payloads contain IDs only
- **No prompts in telemetry**: Playbook prompts not logged
- **Minimum text to AI**: Only necessary document text sent to OpenAI
- **Synthetic test data**: Tests use mock data, never real documents

### Secrets Management

- **Azure Key Vault**: API keys, connection strings
- **Managed Identity**: Preferred for Azure service access
- **No secrets in code**: All secrets via configuration

---

## Performance Characteristics

### Target Metrics

| Operation | Target | Rationale |
|-----------|--------|-----------|
| **Finance Summary Endpoint** | < 200ms from cache | Redis-cached, 5-min TTL |
| **Classification Job** | < 10s per attachment | gpt-4o-mini fast, Document Intelligence ~2-3s |
| **Extraction Job** | < 30s per invoice | gpt-4o structured output, complex prompt |
| **Snapshot Generation** | < 5s per matter | Pure math, no AI |
| **Invoice Indexing** | < 15s per invoice | Chunking + embedding generation |

### Concurrency

- **Classification**: Bounded concurrency (10 concurrent jobs) via semaphore
- **Extraction**: Bounded concurrency (5 concurrent jobs) via semaphore
- **Snapshot/Indexing**: No bounds (fast, non-AI operations)

### Caching Strategy

- **Finance Summary**: Redis cache (5-min TTL + explicit invalidation)
- **Playbook Prompts**: Redis cache (1-hour TTL)
- **Dataverse Metadata**: In-memory cache (10-min TTL)

---

## Deployment Architecture

### Azure Resources

```
┌─────────────────────────────────────────────────────────────────┐
│  Resource Group: spe-infrastructure-westus2                     │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  App Service Plan: spe-plan-dev                                 │
│  └─▶ App Service: spe-api-dev-67e2xz                           │
│      (BFF API with Finance Module)                              │
│                                                                  │
│  Azure OpenAI: spaarke-openai-dev                               │
│  ├─▶ Deployment: gpt-4o-mini (classification)                  │
│  ├─▶ Deployment: gpt-4o (extraction)                           │
│  └─▶ Deployment: text-embedding-3-large (embeddings)           │
│                                                                  │
│  Document Intelligence: spaarke-docintel-dev                    │
│                                                                  │
│  AI Search: spaarke-search-dev                                  │
│  └─▶ Index: spaarke-invoices-{tenantId}                        │
│                                                                  │
│  Service Bus: spe-servicebus-dev                                │
│  ├─▶ Queue: jobs                                                │
│  └─▶ Topic: job-status                                          │
│                                                                  │
│  Redis Cache: spaarke-redis-dev                                 │
│  └─▶ Cache: finance-summary (5-min TTL)                        │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘

┌─────────────────────────────────────────────────────────────────┐
│  Microsoft Dataverse: spaarkedev1.crm.dynamics.com              │
├─────────────────────────────────────────────────────────────────┤
│  Entities: sprk_invoice, sprk_billingevent, sprk_budget,       │
│            sprk_budgetbucket, sprk_spendsnapshot, sprk_spendsignal│
│  Extended: sprk_document (13 finance fields)                    │
│  Views: Invoice Review Queue, Active Invoices                   │
│  Charts: Budget Utilization Gauge, Monthly Spend Timeline       │
└─────────────────────────────────────────────────────────────────┘
```

### Deployment Steps

1. **Dataverse Schema**:
   - Deploy solution with 6 new entities + extended sprk_document
   - Import 2 views (Invoice Review Queue, Active Invoices)

2. **Azure AI Search Index**:
   - Deploy via Bicep: `infrastructure/ai-search/deploy-invoice-index.bicep`
   - Index: `spaarke-invoices-{tenantId}`

3. **Playbook Records**:
   - Create sprk_playbook records for "Attachment Classification" and "Invoice Extraction"
   - Store prompt templates with version 1.0.0

4. **BFF API**:
   - Deploy code to App Service
   - Update appsettings.json:
     ```json
     {
       "Finance": {
         "AutoClassifyAttachments": false,
         "ClassificationConfidenceThreshold": 0.7,
         "BudgetWarningThreshold": 0.8,
         "VelocitySpikeThreshold": 0.5
       }
     }
     ```
   - Add `builder.Services.AddFinanceModule(builder.Configuration);` to Program.cs

5. **VisualHost Charts**:
   - Import chart XML definitions to Dataverse
   - Import JSON configurations for VisualHost

6. **Feature Flag**:
   - Set `AutoClassifyAttachments: true` when ready to enable classification pipeline

---

## Appendix: File Locations

### Source Code

| Component | Path |
|-----------|------|
| **AI Services** | `src/server/api/Sprk.Bff.Api/Services/Finance/` |
| **Job Handlers** | `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` |
| **Endpoints** | `src/server/api/Sprk.Bff.Api/Api/Finance/` |
| **DI Module** | `src/server/api/Sprk.Bff.Api/Infrastructure/DI/FinanceModule.cs` |
| **Configuration** | `src/server/api/Sprk.Bff.Api/Configuration/FinanceOptions.cs` |
| **Authorization** | `src/server/api/Sprk.Bff.Api/Api/Filters/FinanceAuthorizationFilter.cs` |
| **Telemetry** | `src/server/api/Sprk.Bff.Api/Telemetry/FinanceTelemetry.cs` |

### Tests

| Type | Path |
|------|------|
| **Unit Tests** | `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/` |
| **Structured Output Tests** | `tests/unit/Sprk.Bff.Api.Tests/Services/Ai/GetStructuredCompletionAsyncTests.cs` |

### Infrastructure

| Resource | Path |
|----------|------|
| **AI Search Index** | `infrastructure/ai-search/invoice-index-schema.json` |
| **Bicep Template** | `infrastructure/ai-search/deploy-invoice-index.bicep` |
| **PowerShell Script** | `infrastructure/ai-search/Deploy-InvoiceSearchIndex.ps1` |
| **VisualHost Charts** | `infrastructure/dataverse/charts/` |
| **Dataverse Views** | `infrastructure/dataverse/views/` |

### Documentation

| Document | Path |
|----------|------|
| **Specification** | `projects/financial-intelligence-module-r1/spec.md` |
| **Implementation Plan** | `projects/financial-intelligence-module-r1/plan.md` |
| **User Guide** | `docs/guides/finance-intelligence-user-guide.md` |
| **Spend Snapshot Visualization Guide** | `docs/guides/finance-spend-snapshot-visualization-guide.md` |
| **Verification Results** | `projects/financial-intelligence-module-r1/notes/verification-results.md` |
| **Lessons Learned** | `projects/financial-intelligence-module-r1/notes/lessons-learned.md` |
| **Tuning Guides** | `projects/financial-intelligence-module-r1/notes/*-tuning-guide.md` |
| **Integration Tests** | `projects/financial-intelligence-module-r1/notes/integration-test-implementation-guide.md` |

---

**Document Version**: 1.1
**Last Updated**: 2026-02-13
**Maintained By**: Platform Engineering Team
**Next Review**: Post-deployment (after validation)
