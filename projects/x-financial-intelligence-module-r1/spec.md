# Finance Intelligence Module R1 — AI Implementation Specification

> **Status**: Ready for Implementation
> **Created**: 2026-02-11
> **Source**: `Spaarke_Finance_Intelligence_MVP_Design 2.md`

## Executive Summary

Deliver a Finance Intelligence MVP that extends the existing email-to-document pipeline with AI-powered invoice classification, a human review gate, structured billing fact extraction, pre-computed spend analytics (snapshots and signals), invoice-specific semantic search, and a Finance Intelligence PCF panel on the Matter form. The system uses deterministic VisibilityState assignment, idempotent job chains, and a two-layer structured AI output architecture built on `IOpenAiClient.GetStructuredCompletionAsync<T>`.

## Scope

### In Scope

- **Attachment Classification (Playbook A)**: AI classifies email attachments as InvoiceCandidate / NotInvoice / Unknown with confidence scores and invoice header hints; uses `gpt-4o-mini` via structured output
- **Entity Matching**: Multi-signal matter/vendor suggestion using existing `IRecordMatchService` (reference number, vendor org, parent email context, keyword overlap)
- **Invoice Review Queue**: Dataverse filtered view over `sprk_document` for human review of candidates/unknowns
- **Invoice Confirmation Endpoint**: `POST /api/finance/invoice-review/confirm` — validates required fields, creates `sprk_invoice`, enqueues extraction
- **Invoice Extraction (Playbook B)**: AI extracts structured billing facts from confirmed invoices; uses `gpt-4o` via structured output; creates BillingEvents with `VisibilityState = Invoiced`
- **Spend Snapshot Generation**: Deterministic aggregation of BillingEvents into SpendSnapshots (Month + ToDate periods for MVP) with budget variance and Month-over-Month velocity metrics
- **Spend Signal Detection**: Threshold-based alerts (BudgetExceeded, BudgetWarning, VelocitySpike, AnomalyDetected)
- **Invoice Indexing**: Dedicated AI Search index (`spaarke-invoices-{tenantId}`) with typed financial metadata fields for range queries and faceting
- **Invoice Search Endpoint**: `GET /api/finance/invoices/search` — hybrid search (keyword + vector + semantic ranking)
- **Finance Summary Endpoint**: `GET /api/finance/matters/{matterId}/summary` — Redis-cached (5-min TTL + explicit invalidation)
- **Finance Intelligence PCF Panel**: PCF control on Matter form rendering budget gauge, spend timeline, active signals, and invoice history (reads from BFF endpoint)
- **Dataverse Schema**: 6 new entities (~63 fields) + 13 fields on existing `sprk_document` + 2 new views
- **Structured Output Foundation**: `GetStructuredCompletionAsync<T>` on `IOpenAiClient` — platform capability reusable by future modules
- **DI Module**: `AddFinanceModule()` with ≤15 registrations per ADR-010

### Out of Scope

- No generalized Work Item framework
- No full e-billing submission or invoicing workflow (LEDES generation, invoice approvals, payment processing)
- No full time entry capture or firm TMS replacement
- No external portal for invoice submission (email-only intake for MVP)
- No firm-side VisibilityState stages (InternalWIP, PreBill) — Invoiced only
- No multi-currency conversion (store original currency; conversion is post-MVP)
- No PCF-based review queue (Dataverse view only for R1; PCF Dataset control is future upgrade)
- No bulk review actions (single-item confirm/reject for R1)

### Affected Areas

- `src/server/api/Sprk.Bff.Api/` — New endpoints, job handlers, services, DI module
- `src/server/api/Sprk.Bff.Api/Api/Finance/` — New endpoint group (review, search, summary)
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/` — 4 new job handlers
- `src/server/api/Sprk.Bff.Api/Services/Finance/` — New finance services (InvoiceAnalysis, SpendSnapshot, SignalEvaluation, InvoiceSearch, InvoiceReview)
- `src/server/api/Sprk.Bff.Api/Services/Ai/` — `IOpenAiClient` / `OpenAiClient` extension (structured output)
- `src/server/api/Sprk.Bff.Api/Infrastructure/DI/` — `FinanceModule.cs`
- `src/server/api/Sprk.Bff.Api/Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` — Modified to enqueue classification job
- `src/client/pcf/` — New Finance Intelligence PCF panel control
- `src/solutions/` — Dataverse solution with new entities, fields, views
- `infrastructure/` — AI Search index schema (Bicep/ARM)

## Requirements

### Functional Requirements

1. **FR-01**: Email ingestion creates Document records for .eml and attachments with SPE files linked (existing pipeline extended) — Acceptance: Attachment Documents have `sprk_documenttype = 100000007` and valid SPE file references
2. **FR-02**: Classification job populates `sprk_classification`, `sprk_classificationconfidence`, and invoice hint fields on attachment Documents — Acceptance: All three classification values produce correct field writes; hints populated for InvoiceCandidate/Unknown
3. **FR-03**: Entity matching populates `sprk_mattersuggestedref` and `sprk_mattersuggestionjson` with ranked matter/vendor candidates — Acceptance: Top 5 candidates stored with confidence scores and match reasons
4. **FR-04**: Invoice Review Queue (Dataverse view) shows candidate/unknown attachments filtered by `sprk_documenttype`, `sprk_classification`, and `sprk_invoicereviewstatus` — Acceptance: View returns only unreviewed candidates/unknowns
5. **FR-05**: Reviewer confirms invoice via BFF endpoint, linking Matter/Project + VendorOrg, and triggers extraction — Acceptance: Endpoint validates required fields, creates `sprk_invoice` (Status=ToReview, ExtractionStatus=NotRun), enqueues InvoiceExtraction job, returns 202
6. **FR-06**: Confirmed invoice extraction creates BillingEvents with `VisibilityState = Invoiced` (set deterministically in code, never by LLM) and sets `sprk_invoice.sprk_status = Reviewed` upon successful extraction — Acceptance: BillingEvents have correct cost type, amount, currency, event date, role class; VisibilityState always equals Invoiced; Invoice status transitions ToReview → Reviewed
7. **FR-07**: Extraction enqueues SpendSnapshotGeneration and InvoiceIndexing downstream jobs — Acceptance: Both jobs enqueued upon successful extraction; extraction failure sets `sprk_extractionstatus = Failed` without blocking pipeline
8. **FR-08**: Snapshot generation creates SpendSnapshots (Month + ToDate for MVP) with budget variance and Month-over-Month velocity metrics — Acceptance: Variance = Budget - Invoiced; VelocityPct = (current month - prior month) / prior month × 100; null when prior period is zero. Quarter/Year period types and QoQ/YoY velocity are post-MVP additions.
9. **FR-09**: Signal evaluation creates SpendSignals for threshold breaches — Acceptance: BudgetExceeded at 100%+, BudgetWarning at 80% (configurable), VelocitySpike at 50% increase (configurable)
10. **FR-10**: Snapshot generation invalidates Redis cache key `matter:{matterId}:finance-summary` — Acceptance: Cache key deleted after snapshot write; BFF endpoint serves fresh data on next request
11. **FR-11**: Confirmed invoices indexed in dedicated AI Search index with typed financial metadata (invoiceDate as DateTimeOffset, totalAmount as Double) and contextual metadata enrichment (vendor name, matter name/number, invoice header prepended to each chunk before vectorization) — Acceptance: Invoice search supports vendor filter, amount range, date range, and semantic ranking; a query like "high cost research in the Smith matter" returns relevant chunks
12. **FR-12**: `GET /api/finance/matters/{matterId}/summary` returns finance summary from Redis cache with 5-min TTL fallback — Acceptance: Response includes invoiced amount, budget total, budget remaining, variance, velocity, open signals count
13. **FR-13**: Finance Intelligence PCF panel renders on Matter form with budget gauge, spend timeline, active signals, and invoice history — Acceptance: Panel loads from BFF endpoint, uses Fluent UI v9, supports dark mode, renders within model-driven app
14. **FR-14**: Rejected candidates retained with `RejectedNotInvoice` status — no deletion — Acceptance: Rejected documents remain queryable with reviewer identity and timestamp
15. **FR-15**: All async operations return 202 + jobId + statusUrl per ADR-017 — Acceptance: Enqueue endpoints return correct response shape; status endpoint returns job progress

### Non-Functional Requirements

- **NFR-01**: Idempotent job processing — BillingEvents use alternate key (`sprk_invoiceid` + `sprk_linesequence`) for upsert (correlationId excluded from key to support re-extraction); SpendSnapshots use alternate key (`sprk_matterid` + `sprk_periodtype` + `sprk_periodkey` + `sprk_bucketkey` + `sprk_visibilityfilter`) for upsert
- **NFR-02**: VisibilityState deterministic — set in handler code, never inferred by LLM; extraction prompt explicitly excludes VisibilityState output
- **NFR-03**: Data governance (ADR-015) — no document content in logs; job payloads contain IDs only; prompts versioned per ADR-014
- **NFR-04**: Feature flag — `AutoClassifyAttachments` on `EmailProcessingOptions` (default: `false`) gates classification enqueue for safe rollout
- **NFR-05**: Rate limiting — AI endpoints bounded per ADR-016; extraction jobs use bounded concurrency
- **NFR-06**: Text extraction failure handling — when `TextExtractorService` fails to extract text, classify as Unknown with confidence=0 and send to review queue
- **NFR-07**: Cache invalidation — explicit Redis key deletion after snapshot write; 5-min TTL as safety net only
- **NFR-08**: PCF bundle size under 5MB per ADR-021; Fluent UI v9 exclusively; React 16 APIs only

## Technical Constraints

### Applicable ADRs

| ADR | Relevance | Key Constraint |
|-----|-----------|----------------|
| **ADR-001** | All new endpoints + job handlers | Minimal API pattern; no Azure Functions |
| **ADR-002** | No plugins involved | All logic in BFF API; no plugin business logic |
| **ADR-004** | 4 new JobTypes | Standard job contract envelope; idempotent handlers; correlation IDs |
| **ADR-006** | Finance Intelligence PCF | Must be PCF control, not webresource |
| **ADR-008** | Invoice review + finance endpoints | Endpoint-level authorization filters; no global auth middleware |
| **ADR-009** | Finance summary caching | Redis-first with `IDistributedCache`; explicit invalidation; 5-min TTL |
| **ADR-010** | `AddFinanceModule()` DI | ≤15 registrations per feature module; no inline registrations |
| **ADR-011** | Future review queue upgrade | Dataset PCF pattern when/if review queue PCF is built |
| **ADR-012** | PCF shared components | Reuse `@spaarke/ui-components`; Fluent v9 only; 90%+ coverage on shared |
| **ADR-013** | Classification + extraction via AI | Extend BFF, not separate service; structured output via `IOpenAiClient`; rate limiting |
| **ADR-014** | Playbook prompts | Versioned in Dataverse `sprk_playbook` table; tenant-scoped cache keys |
| **ADR-015** | Data governance | No document content in logs; IDs only in job payloads; minimum text to AI |
| **ADR-016** | AI rate limits | Bounded concurrency for AI calls; explicit timeouts; 429/503 ProblemDetails |
| **ADR-017** | Async job status | 202 Accepted + jobId + statusUrl; persist status transitions |
| **ADR-019** | Error responses | ProblemDetails with stable errorCode and correlation ID |
| **ADR-020** | Versioning | Tolerant reader for job payloads; prompt version tracking |
| **ADR-021** | Fluent UI v9 | All PCF UI uses Fluent v9; React 16 APIs only; dark mode required; no hard-coded colors |

### MUST Rules (from ADRs)

- MUST use Minimal API for all new HTTP endpoints (ADR-001)
- MUST use BackgroundService + Service Bus for all async work (ADR-001)
- MUST implement job handlers as idempotent with deterministic IdempotencyKeys (ADR-004)
- MUST propagate CorrelationId through entire job chain (ADR-004)
- MUST use endpoint filters for authorization on finance endpoints (ADR-008)
- MUST use `IDistributedCache` (Redis) for finance summary caching (ADR-009)
- MUST register all finance services in `AddFinanceModule()` extension (ADR-010)
- MUST use `IOpenAiClient.GetStructuredCompletionAsync<T>` for AI calls (ADR-013)
- MUST version playbook prompts via `sprk_playbook.sprk_version` (ADR-014)
- MUST NOT log document content, extracted text, or prompts (ADR-015)
- MUST NOT place document bytes in job payloads (ADR-015)
- MUST apply rate limiting to all AI-consuming endpoints (ADR-016)
- MUST return 202 + jobId + statusUrl from all enqueue endpoints (ADR-017)
- MUST return ProblemDetails with errorCode for all errors (ADR-019)
- MUST use Fluent UI v9 exclusively in PCF panel (ADR-021)
- MUST use React 16 APIs only (no `createRoot`, no concurrent features) (ADR-021)
- MUST support light, dark, and high-contrast modes in PCF (ADR-021)

### MUST NOT Rules (from ADRs)

- MUST NOT create Azure Functions (ADR-001)
- MUST NOT create separate AI microservice (ADR-013)
- MUST NOT call Azure AI services directly from PCF (ADR-013)
- MUST NOT cache authorization decisions (ADR-009)
- MUST NOT use `IMemoryCache` without profiling justification (ADR-009)
- MUST NOT create global auth middleware (ADR-008)
- MUST NOT create legacy JS webresources (ADR-006)
- MUST NOT mix Fluent UI versions (ADR-021)
- MUST NOT hard-code colors (ADR-021)
- MUST NOT bundle React/Fluent in PCF artifacts (ADR-021)

### Existing Patterns to Follow

- Endpoint pattern: See `Api/Ai/AnalysisEndpoints.cs` for Minimal API group mapping
- Job handler pattern: See `Services/Jobs/Handlers/EmailToDocumentJobHandler.cs` and `RagIndexingJobHandler.cs`
- AI service pattern: See `Services/Ai/AppOnlyAnalysisService.cs` (for playbook loading), `Services/Ai/OpenAiClient.cs` (for AI calls)
- Record matching: See `Services/RecordMatching/RecordMatchService.cs` for entity matching
- DI module pattern: See `Infrastructure/DI/SpaarkeCore.cs`, `DocumentsModule.cs`, `WorkersModule.cs`
- PCF pattern: See existing controls in `src/client/pcf/`
- See `.claude/patterns/` for detailed code patterns

## Entity Model Summary

### New Entities

| Entity | Fields | Purpose |
|--------|--------|---------|
| `sprk_invoice` | 13 + alt key (`sprk_documentid`) | Lightweight confirmed invoice artifact |
| `sprk_billingevent` | 13 + alt key (`sprk_invoiceid` + `sprk_linesequence`) | Canonical financial fact (one per extractable line item). `sprk_correlationid` is a traceability field only — excluded from alternate key to support idempotent re-extraction with new correlation IDs. |
| `sprk_budgetplan` | 5 (includes `sprk_status`: Draft/Active/Closed — transitioned manually for MVP) | Budget plan header |
| `sprk_budgetbucket` | 6 | Budget allocation by bucket/period |
| `sprk_spendsnapshot` | 16 + alt key (`sprk_matterid` + `sprk_periodtype` + `sprk_periodkey` + `sprk_bucketkey` + `sprk_visibilityfilter`) | Pre-computed aggregations with velocity metrics. Alt key enables idempotent upsert on snapshot re-generation. |
| `sprk_spendsignal` | 10 | Threshold-based alerts |

### Extended Entity

| Entity | New Fields | Purpose |
|--------|-----------|---------|
| `sprk_document` | 13 (classification: 3, hints: 6, associations: 4) | Classification results, invoice hints, matter/vendor suggestions |

### New Views

| View | Entity | Filter |
|------|--------|--------|
| Invoice Review Queue | `sprk_document` | `documenttype=EmailAttachment` AND `classification IN (InvoiceCandidate, Unknown)` AND `reviewstatus=ToReview` |
| Active Invoices | `sprk_invoice` | Standard active view |

## Job Chain

| Job # | JobType | Trigger | Handler | AI? |
|-------|---------|---------|---------|-----|
| 1 | `AttachmentClassification` | EmailToDocumentJobHandler (feature-flagged) | `AttachmentClassificationJobHandler` | Yes (gpt-4o-mini) |
| — | *Human review gate* | Reviewer confirms via BFF endpoint | — | — |
| 2 | `InvoiceExtraction` | Confirm endpoint creates invoice + enqueues | `InvoiceExtractionJobHandler` | Yes (gpt-4o) |
| 3 | `SpendSnapshotGeneration` | InvoiceExtractionJobHandler enqueues | `SpendSnapshotGenerationJobHandler` | No (pure math) |
| 4 | `InvoiceIndexing` | InvoiceExtractionJobHandler enqueues (new JobType — NOT reusing generic `RagIndexing`) | `InvoiceIndexingJobHandler` | No (embeddings only) |

## AI Playbooks

### Playbook A: "Attachment Classification"

- **Model**: gpt-4o-mini (fast, cost-effective for high-volume classification)
- **Input**: Extracted text from attachment via `TextExtractorService`
- **Output**: `ClassificationResult` — classification enum, confidence decimal, invoice hints (vendor name, invoice number, date, total, currency, matter reference string). Note: entity matching (matter/vendor record resolution) is performed by the handler *after* the AI call returns, not by the AI model. The `ClassificationResult` record type contains AI output only; suggestion fields on `sprk_document` are populated separately by the handler's matching logic.
- **Execution**: `IInvoiceAnalysisService.ClassifyAttachmentAsync()` → `IOpenAiClient.GetStructuredCompletionAsync<ClassificationResult>()`
- **Prompt storage**: Dataverse `sprk_playbook` record, versioned per ADR-014

### Playbook B: "Invoice Extraction"

- **Model**: gpt-4o (high accuracy for complex extraction)
- **Input**: Extracted text from confirmed invoice + reviewer-corrected hints. Note: reviewer corrections (invoice number, date, total) are read from the `sprk_invoice` and `sprk_document` records by the handler at execution time — NOT passed in the job payload (per ADR-015: IDs only in payloads).
- **Output**: `ExtractionResult` — invoice header, line items array (costType, amount, roleClass, etc.), extraction confidence
- **Execution**: `IInvoiceAnalysisService.ExtractInvoiceFactsAsync()` → `IOpenAiClient.GetStructuredCompletionAsync<ExtractionResult>()`
- **Prompt storage**: Dataverse `sprk_playbook` record, versioned per ADR-014
- **Guardrail**: VisibilityState set deterministically in handler code, never in prompt output
- **Status transition**: `InvoiceExtractionJobHandler` sets `sprk_invoice.sprk_status = Reviewed` upon successful extraction (this is the only handler that transitions invoice status)

## Invoice Indexing Strategy

### Contextual Metadata Enrichment

During the `InvoiceIndexing` job, the `InvoiceIndexingJobHandler` MUST inject contextual metadata into each text chunk **before** vectorization. Raw invoice text lacks the relational context needed for high-quality semantic search — enriching chunks with structured metadata from Dataverse records ensures vector similarity captures the full meaning.

**Pattern**: Before generating embeddings, prepend each chunk's `content` field with a metadata header derived from the `sprk_invoice`, `sprk_organization`, and `sprk_matter`/`sprk_project` records:

```
Firm: {vendorOrg.Name} | Matter: {matter.Name} ({matter.Number}) | Invoice: {invoice.InvoiceNumber} | Date: {invoice.InvoiceDate} | Total: {invoice.Currency} {invoice.TotalAmount}
---
{original chunk text}
```

**Example**: Instead of indexing the raw line:
> "Researching case law - $400"

The enriched chunk becomes:
> "Firm: Smith & Associates LLP | Matter: Acme v. Beta Corp (2026-001) | Invoice: INV-2026-042 | Date: 2026-01-15 | Total: USD 15,000.00\n---\nResearching case law - $400"

**Why this matters for search quality**:
- A vector search for *"high cost research in the Smith matter"* now has vendor name, matter name, and amount all co-located in a single vector — producing a high-relevance match
- Without enrichment, the vector for "Researching case law - $400" has no matter/vendor signal and would rank poorly against that query
- The typed metadata fields (`invoiceDate`, `totalAmount`, `vendorName`) on the index document handle **filtering** (exact match, range queries); the enriched content handles **semantic ranking**

**Implementation in `InvoiceIndexingJobHandler`** (step 8 in the handler pipeline):
1. Load `sprk_invoice` → get header fields (number, date, total, currency)
2. Load `sprk_organization` via `sprk_vendororgid` → get vendor name
3. Load `sprk_matter`/`sprk_project` via invoice lookups → get matter name and number
4. For each text chunk from `ITextChunkingService`: prepend metadata header
5. Generate embeddings on the **enriched** chunk text via `IOpenAiClient.GenerateEmbeddingsAsync`
6. Store enriched text in `content` field; store original metadata in typed index fields

**Guardrails**:
- Metadata header is deterministic (no AI) — simple string interpolation from Dataverse records
- If a lookup fails (e.g., vendor org deleted), fall back to the raw chunk without enrichment — do not fail the indexing job
- Enrichment adds ~100-200 tokens per chunk; well within the embedding model's context window

## BFF Endpoints

| Method | Path | Purpose | Response |
|--------|------|---------|----------|
| POST | `/api/finance/invoice-review/confirm` | Confirm attachment as invoice | 202 + jobId + invoiceId |
| POST | `/api/finance/invoice-review/reject` | Reject attachment | 200 + documentId |
| GET | `/api/finance/matters/{matterId}/summary` | Finance summary (Redis-cached) | 200 + summary JSON |
| GET | `/api/finance/invoices/search` | Hybrid invoice search | 200 + search results |

### Reject Endpoint Detail

```
POST /api/finance/invoice-review/reject
Authorization: Bearer {token}
Content-Type: application/json

{
  "documentId": "{guid}"
}

Response: 200 OK
{
  "documentId": "{guid}",
  "reviewStatus": "RejectedNotInvoice",
  "reviewedBy": "{userId}",
  "reviewedOn": "2026-01-15T10:30:00Z"
}
```

Sets `sprk_invoicereviewstatus = RejectedNotInvoice`, `sprk_invoicereviewedby`, `sprk_invoicereviewedon` on the document. No invoice record created, no downstream jobs enqueued. Document retained for audit.

## Phased Build Sequence

### Phase 1: Foundation (Dataverse schema + AI platform capability)

| Task | What | Type |
|------|------|------|
| 1a | Dataverse schema: Create 6 new entities + relationships | Schema |
| 1b | Dataverse schema: Add 13 classification/review fields to `sprk_document` | Schema |
| 1c | Add `GetStructuredCompletionAsync<T>` to `IOpenAiClient` | Code (foundation) |
| 1d | Unit tests for structured output method | Tests |
| 1e | Write "Attachment Classification" prompt template | Content |
| 1f | Write "Invoice Extraction" prompt template | Content |
| 1g | Create playbook records in Dataverse | Dataverse data |
| 1h | Update ADR-010 with per-module ceiling constraint | Documentation |

### Phase 2: AI Services + Job Handlers

| Task | What | Type |
|------|------|------|
| 2a | Define `ClassificationResult`, `ExtractionResult`, `InvoiceHints` C# record types + JSON schemas | Code (models) |
| 2b | Implement `IInvoiceAnalysisService` / `InvoiceAnalysisService` | Code (AI service) |
| 2c | Create `AddFinanceModule()` DI registration | Code (DI) |
| 2d | Implement `AttachmentClassificationJobHandler` (classify, hints, entity matching) | Code (handler) |
| 2d1 | Entity matching signal aggregation (reuses `IRecordMatchService`) | Code (matching) |
| 2e | Modify `EmailToDocumentJobHandler` to enqueue classification (+ feature flag) | Code (modification) |
| 2f | Implement `InvoiceReviewService` + confirm/reject endpoints | Code (endpoint) |
| 2g | Implement `InvoiceExtractionJobHandler` | Code (handler) |
| 2h | Implement `SpendSnapshotService` + `SignalEvaluationService` | Code (business logic) |
| 2i | Implement `SpendSnapshotGenerationJobHandler` | Code (handler) |
| 2j | Unit tests: SpendSnapshot aggregation + signal rules (priority) | Tests |

### Phase 3: Invoice RAG + Search

| Task | What | Type |
|------|------|------|
| 3a | Define invoice AI Search index schema | Infrastructure |
| 3b | Deploy invoice index in Azure AI Search | Infrastructure |
| 3c | Implement `InvoiceIndexingJobHandler` (with contextual metadata enrichment — see Invoice Indexing Strategy) | Code (handler) |
| 3d | Implement `InvoiceSearchService` + search endpoint | Code (endpoint) |
| 3e | Wire invoice indexing into extraction job chain | Code (wiring) |

### Phase 4: PCF Panel + Integration

| Task | What | Type |
|------|------|------|
| 4a | `GET /api/finance/matters/{matterId}/summary` endpoint (Redis-cached) | Code (endpoint) |
| 4b | Finance Intelligence PCF panel (budget gauge, spend timeline, signals, invoice history) | Code (PCF) |
| 4c | Tune classification confidence thresholds | AI tuning |
| 4d | Tune extraction prompts with real invoice samples | AI tuning |
| 4e | Version playbook prompts after tuning | Dataverse data |
| 4f | Invoice Review Queue Dataverse view | Configuration |
| 4g | End-to-end integration tests (full job chain) | Tests |

## Success Criteria

1. [ ] Email ingestion creates Document records for .eml and attachments with SPE files linked — Verify: Documents exist with correct `sprk_documenttype` and SPE references
2. [ ] Classification job populates classification + confidence + invoice hints on attachment Documents — Verify: Query `sprk_document` fields after classification job runs
3. [ ] Invoice Review Queue (Dataverse view) shows candidate/unknown attachments for review — Verify: View returns expected filtered set
4. [ ] Reviewer confirms invoice, links Matter/Project and VendorOrg, triggers extraction via BFF — Verify: 202 response, `sprk_invoice` created, extraction job enqueued
5. [ ] Confirmed invoice extraction creates `sprk_invoice` + BillingEvents with `VisibilityState = Invoiced`, enqueues downstream jobs — Verify: Records exist with correct field values
6. [ ] Snapshot generation creates SpendSnapshots with budget variance and velocity metrics + SpendSignals for threshold breaches + invalidates Redis cache — Verify: Snapshots match expected aggregation; signals fire at correct thresholds; Redis key deleted
7. [ ] Invoice content indexed into dedicated search index with invoice-specific metadata — Verify: Search returns results filtered by vendor/amount/date range
8. [ ] Finance Intelligence PCF panel reflects updated snapshots via BFF endpoint — Verify: Panel renders budget gauge, timeline, signals, history from live BFF data
9. [ ] Rejected candidates retained with RejectedNotInvoice status (never deleted) — Verify: Rejected documents queryable with reviewer identity and timestamp
10. [ ] All async operations return 202 + job status URL per ADR-017 — Verify: Enqueue endpoints return correct response shape
11. [ ] Unit tests for SpendSnapshot aggregation logic (deterministic, no AI) — Verify: Monthly/ToDate aggregation, budget variance (positive/negative), MoM velocity metrics, idempotent upsert via alternate key
12. [ ] Unit tests for signal evaluation rules — Verify: BudgetExceeded at 100%+, BudgetWarning at 80% default, VelocitySpike detection
13. [ ] Integration tests for classification → review → extraction → snapshot pipeline (end-to-end job chain) — Verify: Full pipeline runs with test data

## Dependencies

### Prerequisites

- Existing email-to-document pipeline operational (`EmailToDocumentJobHandler`)
- Existing `IRecordMatchService` and `DataverseIndexSyncService` operational
- Existing `TextExtractorService` operational (Document Intelligence for PDF/images)
- Existing `IOpenAiClient` / `OpenAiClient` with circuit breaker
- Existing `IPlaybookService` for loading playbook records from Dataverse
- Existing `ISpeFileOperations` for SPE file access (app-only mode)
- Existing `ITextChunkingService` for document chunking
- Azure AI Search service available for invoice index creation
- Redis instance available for finance summary caching

### External Dependencies

- Azure OpenAI: `gpt-4o-mini` deployment for classification, `gpt-4o` deployment for extraction, `text-embedding-3-large` for embeddings
- Azure AI Search: Index creation and management for `spaarke-invoices-{tenantId}`
- Dataverse: Schema deployment for new entities and fields
- Azure Document Intelligence: Text extraction from PDF/image attachments (existing)

## Owner Clarifications

*Answers captured during design-to-spec interview:*

| Topic | Question | Answer | Impact |
|-------|----------|--------|--------|
| Finance Intelligence PCF | Is building the PCF panel in scope for R1, or just the BFF endpoint? | Include PCF control in R1 | Phase 4 includes full PCF panel task (budget gauge, spend timeline, signals, invoice history). Adds frontend work to R1. |
| Invoice Review Queue UX | Should R1 include a PCF Dataset control for the review queue, or is the Dataverse view sufficient? | Dataverse view only for R1 | Review queue uses standard Dataverse filtered view + form-based confirm/reject. PCF Dataset control is a future upgrade. |
| Text extraction failures | When TextExtractorService fails (corrupted PDF, password-protected file), what should happen? | Classify as Unknown, send to review | Classification handler sets `classification=Unknown`, `confidence=0`, `reviewstatus=ToReview` when text extraction fails. Reviewer can manually inspect. |
| BillingEvent alternate key | Design had `correlationId + invoiceId + lineSequence` — fragile for re-extraction (new correlationId = duplicates) | Remove correlationId from key | Alternate key is `sprk_invoiceid + sprk_linesequence` only. CorrelationId kept as traceability field, not identity. |
| SpendSnapshot alternate key | Design didn't define one — re-generation would create duplicates | Add composite alternate key | `sprk_matterid + sprk_periodtype + sprk_periodkey + sprk_bucketkey + sprk_visibilityfilter` for idempotent upsert. |
| Invoice status transition | Design didn't specify which handler sets `sprk_status = Reviewed` | InvoiceExtractionJobHandler sets it | `sprk_invoice.sprk_status` transitions ToReview → Reviewed upon successful extraction in `InvoiceExtractionJobHandler`. |
| Reject endpoint shape | Design described reject action but no endpoint spec | Added `POST /api/finance/invoice-review/reject` | Request: `{ documentId }`. Response: 200 with documentId, reviewStatus, reviewedBy, reviewedOn. No downstream jobs. |
| Snapshot period types for MVP | Design included Quarter/Year but acceptance criteria only mention Monthly/ToDate | Month + ToDate only for MVP | Quarter/Year period types and QoQ/YoY velocity are additive post-MVP. Velocity hardcoded to MoM. |
| ClassificationResult record shape | Design included `MatterSuggestion?` but entity matching happens post-AI-call | Removed from AI result type | `ClassificationResult` contains AI output only (classification, confidence, hints). Entity matching suggestions are populated separately by handler logic. |
| Reviewer corrections in extraction | Design said "reviewer-corrected hints" are inputs but job payload has IDs only | Handler reads from Dataverse records | `InvoiceExtractionJobHandler` loads `sprk_invoice` + `sprk_document` records at execution time to get corrections. No corrections in job payload (ADR-015). |
| BudgetPlan status field | Design used `sprk_status` without naming or lifecycle details | `sprk_status` (Draft/Active/Closed), manual transition for MVP | No automated status transitions. Admin manually activates budget plans. |
| Job chain diagram | Design Section 11 showed "RagIndexing (existing)" for invoice indexing | Corrected to InvoiceIndexing (new) | InvoiceIndexing is a new JobType with its own handler, not reusing generic RagIndexing. |

## Assumptions

*Proceeding with these assumptions (owner did not specify):*

- **Retry policy**: New job types follow existing retry/backoff patterns from ADR-004 (no custom retry configuration needed)
- **Max attachment size**: Classification follows existing `TextExtractorService` file size limits
- **Re-classification**: Attachments are classified once; no re-run on document update for MVP
- **Tenant resolution**: Invoice index `spaarke-invoices-{tenantId}` uses existing tenant resolution patterns from the codebase
- **FinanceOptions configuration values**: Classification confidence threshold, budget warning percentage (default: 80%), velocity spike threshold (default: 50% increase) — all configurable via `appsettings.json` Finance section. MVP velocity is hardcoded to MoM; `VelocityComparisonType` config is post-MVP when Quarter/Year snapshots are added.
- **Snapshot period types**: MVP computes Month + ToDate only. Quarter and Year period types (and QoQ/YoY velocity) are additive post-MVP.
- **Single-item review**: MVP supports one-at-a-time confirm/reject; no bulk review actions
- **Invoice alternate key enforcement**: One invoice per confirmed document (`sprk_documentid` as alternate key)

## Unresolved Questions

*No blocking questions remain. All gaps resolved during clarification interview.*

---

*AI-optimized specification. Original design: `Spaarke_Finance_Intelligence_MVP_Design 2.md`*
