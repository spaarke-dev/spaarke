# Spaarke Finance Intelligence MVP — Design Specification

Version: 1.0
Last Updated: 2026-02-11
Status: Final Design — Ready for spec.md generation
Audience: AI coding agent (Claude Code), Spaarke engineering, product owners

---

## 1. Goal and Non-Goals

### 1.1 Goal

Deliver a Finance Intelligence MVP that:

- Ingests emails and attachments into Spaarke Documents (SPE-backed) using the existing Email-to-Document pipeline
- Uses AI to classify attachments and identify invoice candidates
- Supports a human review queue for invoice confirmation with AI-suggested matter/vendor association
- Extracts minimum invoice financial facts and creates canonical BillingEvents
- Produces SpendSnapshots and SpendSignals (budget variance, velocity, anomalies)
- Uses VisibilityState to power persona-appropriate views (corporate "spend" vs firm "billing") without duplicating fields
- Indexes confirmed invoice content for semantic search via the existing RAG pipeline

### 1.2 Non-Goals (MVP)

- No dependency on a generalized Work Item framework
- No full e-billing submission or invoicing workflow (LEDES generation, invoice approvals, payment processing)
- No full time entry capture or firm TMS replacement
- No external portal for invoice submission (email-only intake for MVP)
- No firm-side VisibilityState stages (InternalWIP, PreBill) — Invoiced only
- No multi-currency conversion (store original currency; conversion is post-MVP)

---

## 2. MVP Pipeline

### 2.1 End-to-end flow

```
Email received (shared/monitored mailbox)
  │
  ▼
Email-to-Document ingestion (existing)
  │  Create Document (.eml) + attachment Documents in SPE
  │
  ▼
Classification job (new — Playbook A)
  │  Classify attachments: InvoiceCandidate / NotInvoice / Unknown
  │  Extract invoice header hints when candidate/unknown
  │  Create InvoiceCandidate entries in review queue
  │
  ▼
Invoice Review Queue (human gate)
  │  Reviewer confirms or rejects
  │  Sets Matter/Project + VendorOrg (required to confirm)
  │  Optionally corrects invoice hints
  │
  ▼
Invoice Extraction job (new — Playbook B)
  │  Create sprk_invoice record
  │  Extract header + best-effort line-like billing facts
  │  Create BillingEvents (VisibilityState = Invoiced)
  │
  ▼
Snapshot Generation job (new — downstream of extraction)
  │  Generate SpendSnapshots + SpendSignals
  │  Invalidate cached Matter rollups
  │
  ▼
Invoice Indexing job (extends existing RAG pipeline)
  │  Index confirmed invoice content into search index
  │
  ▼
Matter/Project Finance Intelligence UI
     Reads from SpendSnapshots via BFF (cached in Redis)
```

### 2.2 Key principle: each step is a separate, idempotent job

The pipeline is implemented as a chain of Service Bus jobs on the existing `sdap-jobs` queue, each with its own JobType. This follows ADR-004 (async job contract) and ensures:

- Each step can fail and retry independently
- Steps can be re-run without re-running predecessors (correction scenarios)
- Observability via ADR-017 job status contract
- Bounded concurrency per ADR-016

---

## 3. Document as Review Queue Object

### 3.1 Rationale

Dataverse OOTB Queues are insufficient for this workflow and a generalized Work Item framework is intentionally out of scope. The simplest MVP approach uses the existing `sprk_document` table as:

- Evidence object (email + attachment file in SPE)
- Classification result holder (AI output fields)
- Review queue item (via Dataverse views filtered by classification + review status)
- Source reference for extraction and indexing

This eliminates the need for a separate InvoiceCandidate entity. The `sprk_invoice` record is created only when a reviewer confirms an attachment as an invoice, keeping Invoice as a clean "confirmed artifact."

### 3.2 How it works

1. Classification Playbook writes classification fields on the attachment Document.
2. A "To Review" Dataverse view filters Documents where:
   - `sprk_documenttype` = 100000007 (Email Attachment) — existing field, already set by `EmailToDocumentJobHandler`
   - `sprk_classification` in (InvoiceCandidate, Unknown)
   - `sprk_invoicereviewstatus` = ToReview
3. Reviewer updates the Document (confirm/reject) and triggers extraction via an explicit API call.

> **Note**: No new `sprk_documentkind` field is needed. The existing `sprk_documenttype` choice field (100000006 = Email, 100000007 = Email Attachment) already distinguishes document types and is set during email-to-document processing.

### 3.3 Future upgrade path

A future Work Item framework can replace this pattern by mapping `Document.InvoiceReviewStatus = ToReview` → `WorkItemType = InvoiceTriage`. The Document fields remain as evidence; the Work Item becomes the orchestration object. Additionally, a PCF Dataset control (per ADR-011) can replace the Dataverse view for richer UX — inline actions, side-panel document preview via SpeFileViewer, bulk operations.

---

## 4. Visibility State

### 4.1 Purpose

VisibilityState applies to financial facts (BillingEvents) and derived summaries (SpendSnapshots). It enables persona-appropriate views without field duplication:

- Corporate views: Invoiced spend and exposure
- Law firm views (future): WIP / PreBill / Invoiced

### 4.2 MVP states

```
VisibilityState (choice field)
├─ Invoiced       ← required for MVP (corporate invoice intake)
├─ InternalWIP    ← future (firm-side)
├─ PreBill        ← future (firm-side)
├─ Paid           ← future
└─ WrittenOff     ← future
```

### 4.3 Deterministic assignment rule (MVP)

Because this MVP ingests invoices from corporate email attachments:

- Confirmed invoice extraction MUST set `BillingEvent.VisibilityState = Invoiced`
- VisibilityState is assigned deterministically in code, never inferred by the LLM
- The Extraction Playbook prompt does not output or suggest VisibilityState values

---

## 5. Entity Model

### 5.1 sprk_organization (existing)

Represents both law firms and client companies. A role/type field distinguishes usage.

Used as:
- Vendor/firm in billing contexts
- Client company on matters/projects

No changes required for MVP.

### 5.2 sprk_matter / sprk_project (existing)

Required existing fields:
- Name, type/practice area, status
- `sprk_clientcompany` (lookup → sprk_organization)
- `sprk_assignedfirm` (lookup → sprk_organization, optional in MVP)
- `sprk_containerid` (SPE container, existing)

No schema changes. Cached rollup fields are served via BFF/Redis, not stored on Matter (see Section 8).

### 5.3 sprk_document (existing — extend for MVP)

New fields for classification and review (note: `sprk_documenttype` already exists and distinguishes Email vs Attachment — no new field needed for that):

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Classification | `sprk_classification` | Choice (InvoiceCandidate, NotInvoice, Unknown) | AI classification result |
| Classification Confidence | `sprk_classificationconfidence` | Decimal (0..1) | AI confidence score |
| Invoice Review Status | `sprk_invoicereviewstatus` | Choice (ToReview, ConfirmedInvoice, RejectedNotInvoice) | Human review state |
| Reviewed By | `sprk_invoicereviewedby` | Lookup → systemuser | Reviewer identity |
| Reviewed On | `sprk_invoicereviewedon` | DateTime | Review timestamp |

Invoice header hints (populated by Classification Playbook):

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Vendor Name Hint | `sprk_invoicevendornamehint` | Text | AI-extracted vendor name |
| Invoice Number Hint | `sprk_invoicenumberhint` | Text | AI-extracted invoice number |
| Invoice Date Hint | `sprk_invoicedatehint` | Date | AI-extracted invoice date |
| Total Amount Hint | `sprk_invoicetotalhint` | Money | AI-extracted total |
| Currency Hint | `sprk_invoicecurrencyhint` | Text | AI-extracted currency code |
| Hints JSON | `sprk_invoicehintsjson` | Multiline Text | Full structured hints (overflow/extensions) |

Association suggestions (populated by Classification Playbook, confirmed by reviewer):

| Field | Logical Name | Type | Purpose |
|-------|-------------|------|---------|
| Matter Suggestion | `sprk_mattersuggestedref` | Text | AI-extracted matter reference string |
| Matter Suggestion JSON | `sprk_mattersuggestionjson` | Multiline Text | Structured match candidates with confidence |
| Related Matter | `sprk_relatedmatterid` | Lookup → sprk_matter | Confirmed by reviewer |
| Related Project | `sprk_relatedprojectid` | Lookup → sprk_project | Confirmed by reviewer |
| Related Vendor Org | `sprk_relatedvendororgid` | Lookup → sprk_organization | Confirmed by reviewer |

Existing fields used:
- `sprk_parentdocument` (lookup → sprk_document, for attachment→email relationship)
- `sprk_documenttype` (choice — Email = 100000006, Email Attachment = 100000007)
- SPE file reference fields (`sprk_graphitemid`, `sprk_graphdriveid`)

### 5.4 sprk_invoice (new — lightweight confirmed invoice artifact)

Created only when a reviewer confirms an attachment as an invoice.

| Field | Logical Name | Type | Required | Purpose |
|-------|-------------|------|----------|---------|
| Invoice ID | `sprk_invoiceid` | GUID | PK | Primary key |
| Source Document | `sprk_documentid` | Lookup → sprk_document | Yes | Confirmed attachment |
| Record Type | `sprk_recordtype` | Choice (Matter, Project) | Yes | Parent type |
| Matter | `sprk_matterid` | Lookup → sprk_matter | Conditional | Parent matter |
| Project | `sprk_projectid` | Lookup → sprk_project | Conditional | Parent project |
| Vendor Org | `sprk_vendororgid` | Lookup → sprk_organization | Yes | Invoice vendor/firm |
| Invoice Number | `sprk_invoicenumber` | Text | No | From extraction or reviewer |
| Invoice Date | `sprk_invoicedate` | Date | No | From extraction or reviewer |
| Currency | `sprk_currency` | Text | No | ISO currency code |
| Total Amount | `sprk_totalamount` | Money | No | Invoice total |
| Status | `sprk_status` | Choice (ToReview, Reviewed) | Yes | Minimal lifecycle |
| Visibility State | `sprk_visibilitystate` | Choice | Yes | Set to Invoiced |
| Extraction Status | `sprk_extractionstatus` | Choice (NotRun, Extracted, Failed) | Yes | Playbook B status |
| Correlation ID | `sprk_correlationid` | Text | Yes | Job chain traceability |

Alternate key: `sprk_documentid` (one invoice per confirmed document).

### 5.5 sprk_billingevent (new — canonical financial fact)

One BillingEvent = one extractable unit of spend (a fee line, an expense line, a time entry).

| Field | Logical Name | Type | Required | Purpose |
|-------|-------------|------|----------|---------|
| Billing Event ID | `sprk_billingeventid` | GUID | PK | Primary key |
| Event Date | `sprk_eventdate` | Date | Yes | Line date or invoice date fallback |
| Matter | `sprk_matterid` | Lookup → sprk_matter | Conditional | Parent matter |
| Project | `sprk_projectid` | Lookup → sprk_project | Conditional | Parent project |
| Vendor Org | `sprk_vendororgid` | Lookup → sprk_organization | Yes | Source vendor |
| Cost Type | `sprk_costtype` | Choice (Fee, Expense) | Yes | Fee vs expense |
| Amount | `sprk_amount` | Money | Yes | Line amount |
| Currency | `sprk_currency` | Text | Yes | ISO currency code |
| Role Class | `sprk_roleclass` | Text | No | Timekeeper role if extractable (Unknown allowed) |
| Visibility State | `sprk_visibilitystate` | Choice | Yes | MVP: Invoiced |
| Source Invoice | `sprk_invoiceid` | Lookup → sprk_invoice | Yes | Parent invoice |
| Description | `sprk_description` | Multiline Text | No | Line narrative (best-effort) |
| Line Sequence | `sprk_linesequence` | Int32 | Yes | 1-based line position within invoice (used in alternate key) |
| Correlation ID | `sprk_correlationid` | Text | Yes | Job chain traceability |

Alternate key: `sprk_correlationid` + `sprk_invoiceid` + `sprk_linesequence` (for idempotent upsert).

### 5.6 sprk_budgetplan and sprk_budgetbucket (new)

MVP minimum — enables budget variance signals.

**sprk_budgetplan:**

| Field | Logical Name | Type | Required | Purpose |
|-------|-------------|------|----------|---------|
| Budget Plan ID | `sprk_budgetplanid` | GUID | PK | Primary key |
| Matter/Project | `sprk_matterid` / `sprk_projectid` | Lookup | Yes | Parent |
| Total Budget | `sprk_totalbudget` | Money | Yes | Overall budget |
| Currency | `sprk_currency` | Text | Yes | Budget currency |
| Status | `sprk_status` | Choice (Draft, Active, Closed) | Yes | Lifecycle |

**sprk_budgetbucket:**

| Field | Logical Name | Type | Required | Purpose |
|-------|-------------|------|----------|---------|
| Bucket ID | `sprk_budgetbucketid` | GUID | PK | Primary key |
| Budget Plan | `sprk_budgetplanid` | Lookup | Yes | Parent plan |
| Bucket Key | `sprk_bucketkey` | Text | Yes | TOTAL (MVP), future: VENDOR:*, ROLE:* |
| Period Start | `sprk_periodstart` | Date | No | NULL = lifetime bucket |
| Period End | `sprk_periodend` | Date | No | NULL = lifetime bucket |
| Amount | `sprk_amount` | Money | Yes | Bucket allocation |

### 5.7 sprk_spendsnapshot and sprk_spendsignal (new)

**sprk_spendsnapshot:**

| Field | Logical Name | Type | Required | Purpose |
|-------|-------------|------|----------|---------|
| Snapshot ID | `sprk_spendsnapshotid` | GUID | PK | Primary key |
| Matter | `sprk_matterid` | Lookup → sprk_matter | Conditional | Parent matter |
| Project | `sprk_projectid` | Lookup → sprk_project | Conditional | Parent project |
| Period Type | `sprk_periodtype` | Choice (Month, Quarter, Year, ToDate) | Yes | Aggregation window |
| Period Key | `sprk_periodkey` | Text | Yes | e.g., "2026-01", "2026-Q1", "2026", "TO_DATE" |
| Bucket Key | `sprk_bucketkey` | Text | Yes | TOTAL (MVP) |
| Visibility Filter | `sprk_visibilityfilter` | Text | Yes | ACTUAL_INVOICED (MVP) |
| Invoiced Amount | `sprk_invoicedamount` | Money | Yes | Sum of BillingEvents |
| Budget Amount | `sprk_budgetamount` | Money | No | From matching BudgetBucket |
| Budget Variance | `sprk_budgetvariance` | Money | No | Budget - Invoiced |
| Budget Variance Pct | `sprk_budgetvariancepct` | Decimal | No | Variance / Budget |
| Velocity Pct | `sprk_velocitypct` | Decimal | No | % change from prior comparison period (positive = spend increasing) |
| Prior Period Amount | `sprk_priorperiodamount` | Money | No | Amount from comparison period (for transparency) |
| Prior Period Key | `sprk_priorperiodkey` | Text | No | Which period was compared (e.g., "2025-12" for MoM, "2025-01" for YoY) |
| Generated At | `sprk_generatedat` | DateTime | Yes | Snapshot computation time |
| Correlation ID | `sprk_correlationid` | Text | Yes | Triggering job chain |

**Velocity definition:** Spend velocity is the **rate of change versus a prior comparison period**, expressed as a percentage. The comparison type is configurable via `FinanceOptions.VelocityComparisonType`:

| Comparison Type | Period Key Example | Prior Period Key | Calculation |
|----------------|-------------------|-----------------|-------------|
| Month-over-Month (MoM) | "2026-01" | "2025-12" | (Jan − Dec) / Dec × 100 |
| Quarter-over-Quarter (QoQ) | "2026-Q1" | "2025-Q4" | (Q1 − Q4) / Q4 × 100 |
| Year-over-Year (YoY) | "2026-01" | "2025-01" | (Jan 2026 − Jan 2025) / Jan 2025 × 100 |

Default: **Month-over-Month**. When the prior period has zero spend, velocity is reported as null (not infinity). `VelocitySpike` signals fire when `sprk_velocitypct` exceeds a configurable threshold (default: 50% increase).

**sprk_spendsignal:**

| Field | Logical Name | Type | Required | Purpose |
|-------|-------------|------|----------|---------|
| Signal ID | `sprk_spendsignalid` | GUID | PK | Primary key |
| Matter | `sprk_matterid` | Lookup → sprk_matter | Conditional | Parent matter |
| Project | `sprk_projectid` | Lookup → sprk_project | Conditional | Parent project |
| Signal Type | `sprk_signaltype` | Choice (BudgetExceeded, BudgetWarning, VelocitySpike, AnomalyDetected) | Yes | Signal category |
| Severity | `sprk_severity` | Choice (Info, Warning, Critical) | Yes | Alert level |
| Message | `sprk_message` | Text | Yes | Human-readable description |
| Snapshot | `sprk_snapshotid` | Lookup → sprk_spendsnapshot | Yes | Source snapshot |
| Is Active | `sprk_isactive` | Boolean | Yes | Active until resolved |
| Generated At | `sprk_generatedat` | DateTime | Yes | Signal detection time |

### 5.8 Schema Change Summary

Total Dataverse deployment footprint:

| Change Type | Entity | Fields Added | Notes |
|------------|--------|-------------|-------|
| **Extend** | `sprk_document` | 13 new fields | Classification (3), hints (6), associations (4). Uses existing `sprk_documenttype`. |
| **New entity** | `sprk_invoice` | 13 fields + alt key | Lightweight confirmed invoice artifact |
| **New entity** | `sprk_billingevent` | 13 fields + alt key | Canonical financial fact (one per line item) |
| **New entity** | `sprk_budgetplan` | 5 fields | Budget plan header |
| **New entity** | `sprk_budgetbucket` | 6 fields | Budget allocation by bucket/period |
| **New entity** | `sprk_spendsnapshot` | 16 fields | Pre-computed aggregations with velocity metrics |
| **New entity** | `sprk_spendsignal` | 10 fields | Threshold-based alerts |
| **New view** | `sprk_document` | — | "Invoice Review Queue" filtered view |
| **New view** | `sprk_invoice` | — | "Active Invoices" view |

**Total**: 6 new entities (~63 new fields) + 13 fields added to existing entity + 2 new views.

---

## 6. Playbooks

### 6.1 Playbook A — Attachment Classification + Invoice Hints

**Trigger:** `AttachmentClassification` job enqueued after attachment Document creation in EmailToDocumentJobHandler.

**Job Contract:**

| Field | Value |
|-------|-------|
| JobType | `AttachmentClassification` |
| SubjectId | Attachment Document ID (GUID) |
| IdempotencyKey | `classify-{documentId}-attachment` |
| Payload | `{ "documentId": "{guid}", "driveId": "{driveId}", "itemId": "{itemId}" }` |

**Inputs:**
- Attachment document content via SpeFileStore (app-only mode)
- File type and filename metadata from sprk_document record

**Processing (IInvoiceAnalysisService — structured output):**
1. Download attachment content from SPE via `ISpeFileOperations` (app-only)
2. Extract text via `TextExtractorService` (Document Intelligence for PDF/images, raw text for others)
3. Call `IInvoiceAnalysisService.ClassifyAttachmentAsync()` → `IOpenAiClient.GetStructuredCompletionAsync<ClassificationResult>()` with "Attachment Classification" playbook prompt
4. Write classification + confidence + hints to `sprk_document`
5. If classification = InvoiceCandidate or Unknown: set `sprk_invoicereviewstatus = ToReview`
6. Run entity matching to populate matter/project suggestions (see Section 6.6)

**Outputs written to sprk_document:**
- `sprk_classification` (InvoiceCandidate / NotInvoice / Unknown)
- `sprk_classificationconfidence` (0..1)
- Invoice hint fields when candidate/unknown: vendor name, invoice number, invoice date, total, currency
- `sprk_mattersuggestedref` (extracted matter reference string, if found)
- `sprk_mattersuggestionjson` (structured match candidates from Dataverse lookup)
- `sprk_invoicereviewstatus` = ToReview (if candidate or unknown)

**Guardrails:**
- If confidence < configurable threshold → classify as Unknown (send to review anyway)
- Never create Invoice records
- Never create BillingEvents
- Never set VisibilityState
- Prompts/templates are versioned per ADR-014
- No document content in logs per ADR-015

### 6.2 Playbook B — Confirmed Invoice Extraction

**Trigger:** Explicit API call from review UI when reviewer confirms an invoice. The BFF endpoint validates required fields (Matter + VendorOrg), creates the Invoice record, then enqueues the extraction job.

**Job Contract:**

| Field | Value |
|-------|-------|
| JobType | `InvoiceExtraction` |
| SubjectId | Invoice ID (GUID) |
| IdempotencyKey | `extract-{invoiceId}-billing` |
| Payload | `{ "invoiceId": "{guid}", "documentId": "{guid}", "driveId": "{driveId}", "itemId": "{itemId}" }` |

**Inputs:**
- Confirmed document content via SpeFileStore (app-only mode)
- Invoice record with confirmed Matter/Project + VendorOrg
- Reviewer-corrected hints (invoice number, date, total) if provided

**Processing (IInvoiceAnalysisService — structured output):**
1. Download document content from SPE via `ISpeFileOperations` (app-only)
2. Extract text via `TextExtractorService`
3. Call `IInvoiceAnalysisService.ExtractInvoiceFactsAsync()` → `IOpenAiClient.GetStructuredCompletionAsync<ExtractionResult>()` with "Invoice Extraction" playbook prompt + reviewer-corrected hints
4. Create BillingEvents with `VisibilityState = Invoiced` (deterministic, set in handler code, not from LLM)
5. Update `sprk_invoice.sprk_extractionstatus = Extracted`
6. Enqueue `SpendSnapshotGeneration` job (downstream)
7. Enqueue `InvoiceIndexing` job (downstream)

**Outputs:**
- BillingEvent records (one per extractable line item) with:
  - Cost type (Fee/Expense)
  - Amount + Currency
  - Event date (line date if available, invoice date as fallback)
  - Role class (if extractable, otherwise "Unknown")
  - VisibilityState = Invoiced (set in code, not by LLM)
- Invoice record updated: extraction status, total amount, invoice number/date (if not already set)

**Guardrails:**
- VisibilityState is set deterministically in the job handler, never in the prompt
- BillingEvents use alternate key for idempotent upsert
- Extraction failures set `sprk_extractionstatus = Failed` and do not block the pipeline
- Prompts/templates versioned per ADR-014

### 6.3 Snapshot Generation (downstream job, not a playbook)

**Trigger:** Enqueued by InvoiceExtraction job handler upon successful extraction.

**Job Contract:**

| Field | Value |
|-------|-------|
| JobType | `SpendSnapshotGeneration` |
| SubjectId | Matter/Project ID (GUID) |
| IdempotencyKey | `snapshot-{matterId}-{correlationId}` |
| Payload | `{ "matterId": "{guid}", "correlationId": "{correlationId}" }` |

**Processing (deterministic, no AI):**
1. Query all BillingEvents for the Matter/Project where VisibilityState = Invoiced
2. Aggregate by period (Month, Quarter, Year, ToDate) and bucket (TOTAL for MVP)
3. Look up matching BudgetBucket for variance computation
4. Compute velocity metrics: load prior comparison period snapshot (configurable: MoM, QoQ, or YoY), calculate % change
5. Upsert SpendSnapshot records (with velocity pct, prior period amount, prior period key)
6. Evaluate signal rules (budget exceeded, budget warning, velocity spike, anomaly detection)
7. Upsert SpendSignal records
8. Invalidate Redis cache for Matter rollups (key: `matter:{matterId}:finance-summary`)

**Why separate from extraction:** Snapshots can be regenerated independently — when extraction is corrected, when budget allocations change, when new signal rules are added. Keeping this as its own job type follows ADR-004 and ensures each concern is independently retryable and observable.

### 6.4 Invoice Indexing (dedicated AI Search index)

Confirmed invoices require their own AI Search index, separate from the general document index (`spaarke-knowledge-index-v2`). Invoice content has domain-specific fields (vendor, amounts, line items) that require specialized search schema and faceting.

**Trigger:** Enqueued by InvoiceExtraction job handler upon successful extraction.

**Job Contract:**

| Field | Value |
|-------|-------|
| JobType | `InvoiceIndexing` (new — not reusing generic `RagIndexing`) |
| SubjectId | Invoice ID (GUID) |
| IdempotencyKey | `rag-index-invoice-{invoiceId}` |
| Payload | `{ "invoiceId": "{guid}", "documentId": "{guid}", "driveId": "{driveId}", "itemId": "{itemId}", "matterId": "{guid}", "vendorOrgId": "{guid}" }` |

#### Why a Separate Index (Not the General Document Index)?

The existing `spaarke-knowledge-index-v2` uses generic fields (`documentType`, `parentEntityType`, `Metadata` as string dictionary). For invoice search, financial metadata must be **typed fields** for range queries and sorting:

| Invoice Search Need | General Index Support | Why Not? |
|--------------------|-----------------------|----------|
| Filter by vendor name | Yes (string filter) | Works |
| Sort by `totalAmount` | **No** | Metadata stores strings, not `Edm.Double` — no numeric sort/range |
| Filter `invoiceDate` range | **No** | Metadata stores strings, not `Edm.DateTimeOffset` — no date range queries |
| "Find invoices > $10,000 in last 90 days" | **No** | Requires typed numeric + date filtering |
| Semantic ranking tuned for invoices | **No** | Semantic config uses generic `content`/`title` — can't prioritize `invoiceNumber`/`vendorName` |

Additional separation benefits:
- Separate index allows independent scaling, schema evolution, and lifecycle management
- Invoice index is small (only confirmed invoices) — general index is large (all documents)
- No risk of general document search performance degradation from invoice-specific fields
- Invoice re-indexing (after extraction correction) doesn't affect general search

#### Existing Infrastructure Reuse Analysis

The existing RAG pipeline (`IFileIndexingService` → `ITextChunkingService` → `IOpenAiClient` → `IRagService`) uses `KnowledgeDeploymentService` for index routing. However, `KnowledgeDeploymentService` routes by **tenant** (Shared/Dedicated/CustomerOwned models), not by **document domain**. Rather than adding invoice routing complexity to the general RAG pipeline, the invoice indexing handler manages its own `SearchClient`:

| Component | Reuse? | Details |
|-----------|--------|---------|
| `ITextExtractor` / `TextExtractorService` | **Reuse** | Same text extraction from PDFs |
| `ITextChunkingService` | **Reuse** | Same chunking strategy (512 tokens, 50 overlap) |
| `IOpenAiClient.GenerateEmbeddingsAsync` | **Reuse** | Same embedding generation |
| `SearchIndexClient` | **Reuse** | Already registered as singleton, can create clients for any index |
| `ISpeFileOperations` | **Reuse** | Same app-only file download |
| Job contract pattern (ADR-004) | **Reuse** | Same Service Bus queue, same `IJobHandler` interface |
| `KnowledgeDeploymentService` | **Not reused** | Routes by tenant, not document domain — wrong abstraction |
| `IRagService.IndexDocumentsBatchAsync` | **Not reused** | Tightly coupled to `KnowledgeDocument` schema and general knowledge index |
| `FileIndexingService` | **Not reused** | Invoice handler needs financial metadata enrichment from Dataverse records |

**Invoice AI Search Index Schema:**

```json
{
  "name": "spaarke-invoices-{tenantId}",
  "fields": [
    { "name": "id", "type": "Edm.String", "key": true },
    { "name": "content", "type": "Edm.String", "searchable": true },
    { "name": "contentVector", "type": "Collection(Edm.Single)", "searchable": true, "vectorSearchDimensions": 3072 },
    { "name": "chunkIndex", "type": "Edm.Int32", "filterable": true },
    { "name": "invoiceId", "type": "Edm.String", "filterable": true },
    { "name": "documentId", "type": "Edm.String", "filterable": true },
    { "name": "matterId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "projectId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "vendorOrgId", "type": "Edm.String", "filterable": true, "facetable": true },
    { "name": "vendorName", "type": "Edm.String", "searchable": true, "filterable": true },
    { "name": "invoiceNumber", "type": "Edm.String", "searchable": true, "filterable": true },
    { "name": "invoiceDate", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true },
    { "name": "totalAmount", "type": "Edm.Double", "filterable": true, "sortable": true },
    { "name": "currency", "type": "Edm.String", "filterable": true },
    { "name": "documentType", "type": "Edm.String", "filterable": true },
    { "name": "tenantId", "type": "Edm.String", "filterable": true },
    { "name": "indexedAt", "type": "Edm.DateTimeOffset", "filterable": true, "sortable": true }
  ],
  "vectorSearch": {
    "algorithms": [{ "name": "hnsw", "kind": "hnsw" }],
    "profiles": [{ "name": "vector-profile", "algorithm": "hnsw" }]
  },
  "semantic": {
    "configurations": [{
      "name": "invoice-semantic",
      "prioritizedFields": {
        "contentFields": [{ "fieldName": "content" }],
        "titleFields": [{ "fieldName": "invoiceNumber" }],
        "keywordsFields": [{ "fieldName": "vendorName" }]
      }
    }]
  }
}
```

Note: `vectorSearchDimensions: 3072` matches the current production embedding model (`text-embedding-3-large` with 3072 dimensions), not the deprecated 1536-dimension model.

**Processing (InvoiceIndexingJobHandler — new):**

The handler is NOT just "index the PDF." It enriches chunks with structured financial metadata from Dataverse:

```
InvoiceIndexingJobHandler receives job
  │
  ├─ 1. Load sprk_invoice from Dataverse (has extracted header data)
  ├─ 2. Load sprk_billingevent records for this invoice (for cost type metadata)
  ├─ 3. Load source sprk_document (has GraphDriveId, GraphItemId)
  ├─ 4. Download document from SPE via ISpeFileOperations (app-only)
  ├─ 5. Extract text via TextExtractorService
  ├─ 6. Chunk text via ITextChunkingService (512 tokens, 50 overlap)
  ├─ 7. Generate embeddings via IOpenAiClient.GenerateEmbeddingsAsync (text-embedding-3-large)
  ├─ 8. Build index documents with FINANCIAL metadata:
  │     ├─ invoiceNumber (from sprk_invoice)
  │     ├─ invoiceDate (from sprk_invoice)
  │     ├─ totalAmount (from sprk_invoice)
  │     ├─ currency (from sprk_invoice)
  │     ├─ vendorName (from sprk_vendororg lookup)
  │     ├─ matterId (from sprk_invoice.relatedmatter)
  │     └─ tenantId (for multi-tenant filtering)
  └─ 9. Upsert into spaarke-invoices-{tenantId} via SearchClient
```

**Index scope:**
- Index: Confirmed invoice documents only (after extraction succeeds)
- Do NOT index: NotInvoice, Rejected candidates, Unknown (until confirmed)
- Re-index: When extraction is corrected or re-run

**Search integration:**
- BFF endpoint: `GET /api/finance/invoices/search` (hybrid search: keyword + vector + semantic ranking)
- `InvoiceSearchService` manages its own `SearchClient` bound to the invoice index
- Filtered by matter, vendor, date range, amount range (using typed `Edm.Double` / `Edm.DateTimeOffset` fields)
- Results include invoice metadata + relevant content snippets

### 6.5 Playbook Specifications

#### Complete AI Processing Map

The following table shows all AI processing steps for the email-to-invoice pipeline, distinguishing existing capabilities from new ones:

| AI Processing Step | Playbook / Service | Model | Trigger | Exists? |
|-------------------|-------------------|-------|---------|---------|
| Document Profile | "Document Profile" playbook | gpt-4o | Email attachment creation | Yes — `AppOnlyAnalysisService` |
| Email Analysis | "Email Analysis" playbook | gpt-4o | Email ingestion | Yes — `AppOnlyAnalysisService` |
| **Attachment Classification** | "Attachment Classification" playbook | gpt-4o-mini | New enqueue from `EmailToDocumentJobHandler` | **New** — `IInvoiceAnalysisService` |
| **Invoice Extraction** | "Invoice Extraction" playbook | gpt-4o | Human review confirms invoice | **New** — `IInvoiceAnalysisService` |
| Document RAG Indexing | N/A (pipeline, no AI prompt) | text-embedding-3-large | Document creation | Yes — `RagIndexingJobHandler` |
| **Invoice RAG Indexing** | N/A (pipeline, no AI prompt) | text-embedding-3-large | Extraction succeeds | **New** — `InvoiceIndexingJobHandler` |
| **Spend Snapshot** | N/A (pure math, no AI) | N/A | BillingEvents created | **New** — `SpendSnapshotGenerationJobHandler` |

#### Playbook Execution Pattern (Finance vs. Existing)

There is an important architectural distinction between how Finance playbooks execute vs. existing playbooks:

**Existing pattern** (`AppOnlyAnalysisService` — multi-tool orchestration):
```
PlaybookService.GetByNameAsync("Document Profile")
  → ScopeResolver → Tool1 (Summarize) → Tool2 (ExtractEntities) → Tool3 (Classify)
  → Aggregate outputs into DocumentAnalysisResult (generic profile strings)
```

**Finance pattern** (`IInvoiceAnalysisService` — single-call structured output):
```
PlaybookService.GetByNameAsync("Attachment Classification")
  → Extract system prompt + user prompt template from playbook record
  → Build ChatMessages (system + user with document text)
  → IOpenAiClient.GetStructuredCompletionAsync<ClassificationResult>()
  → Return typed result (ClassificationResult with guaranteed schema conformance)
```

Both patterns load prompts from the same `sprk_playbook` Dataverse table. The difference is execution model: Finance playbooks use the playbook record purely for **prompt storage and versioning** (per ADR-014) — they do not use the tool/scope orchestration framework. This is intentional: structured output requires controlling the `response_format` parameter directly, which the tool framework doesn't expose.

#### Playbook Definitions

This MVP requires **two new Dataverse playbook records** for AI-driven classification and extraction. Playbooks are stored in the `sprk_playbook` table and loaded by name at runtime (same pattern as the existing "Document Profile" playbook).

#### Playbook A: "Attachment Classification"

**Purpose:** Classify email attachments and extract invoice header hints.

**Stored in Dataverse as:** `sprk_playbook` record with `Name = "Attachment Classification"`

**Input:** Extracted text content from attachment document (via `TextExtractorService`)

**Structured Output Schema (enforced via OpenAI `response_format`):**

```json
{
  "classification": "InvoiceCandidate | NotInvoice | Unknown",
  "confidence": 0.0,
  "hints": {
    "vendorName": "string | null",
    "invoiceNumber": "string | null",
    "invoiceDate": "YYYY-MM-DD | null",
    "totalAmount": 0.00,
    "currency": "USD | null",
    "matterReference": "string | null"
  },
  "reasoning": "string"
}
```

**Prompt Design Principles:**
- System prompt defines classification taxonomy with examples
- Few-shot examples for each classification category (invoice PDFs, expense reports, contracts, letters)
- Confidence calibration guidance: ≥0.8 → InvoiceCandidate, <0.5 → NotInvoice, middle → Unknown
- Explicit instruction: "Do NOT output VisibilityState. Do NOT create records."
- Invoice hint extraction is best-effort — null values are acceptable
- Matter reference extraction looks for matter numbers, project codes, reference lines

**Model selection:** `gpt-4o-mini` (fast classification, cost-effective for high volume)

**Versioning:** Prompt template stored in `sprk_playbook.sprk_systemprompt` and `sprk_playbook.sprk_userprompt`. Version tracked via `sprk_playbook.sprk_version` per ADR-014.

#### Playbook B: "Invoice Extraction"

**Purpose:** Extract structured billing facts from confirmed invoice documents.

**Stored in Dataverse as:** `sprk_playbook` record with `Name = "Invoice Extraction"`

**Input:** Extracted text content from confirmed invoice document + reviewer-corrected hints (invoice number, date, total, vendor)

**Structured Output Schema (enforced via OpenAI `response_format`):**

```json
{
  "header": {
    "invoiceNumber": "string",
    "invoiceDate": "YYYY-MM-DD",
    "totalAmount": 0.00,
    "currency": "USD",
    "vendorName": "string",
    "vendorAddress": "string | null",
    "paymentTerms": "string | null"
  },
  "lineItems": [
    {
      "lineNumber": 1,
      "description": "string",
      "costType": "Fee | Expense",
      "amount": 0.00,
      "currency": "USD",
      "eventDate": "YYYY-MM-DD | null",
      "roleClass": "string | null",
      "hours": 0.0,
      "rate": 0.00
    }
  ],
  "extractionConfidence": 0.0,
  "notes": "string | null"
}
```

**Prompt Design Principles:**
- System prompt defines extraction taxonomy: Fee (professional services, time-based) vs Expense (disbursements, costs)
- Reviewer-corrected hints are provided as "ground truth" overrides in the user prompt
- Line item extraction is best-effort — some invoices have no line detail
- If no line items extractable, create a single BillingEvent with the total amount as a "Fee" line
- Role class extraction: Partner, Associate, Paralegal, Other, Unknown
- Explicit instruction: "Do NOT output VisibilityState. Do NOT assign matter/vendor lookups."
- Date fallback chain: line date → invoice date → current date

**Model selection:** `gpt-4o` (complex extraction requiring high accuracy; cost justified by low volume — only confirmed invoices)

**Versioning:** Same pattern as Playbook A.

#### Playbook Creation Plan

Playbooks must be created in Dataverse before the classification/extraction pipelines can run:

| Task | Phase | Dependencies |
|------|-------|-------------|
| Define "Attachment Classification" prompt template | Phase 1 (Dataverse setup) | None |
| Define "Invoice Extraction" prompt template | Phase 1 (Dataverse setup) | None |
| Create playbook records in Dataverse | Phase 1 (Dataverse setup) | Prompt templates defined |
| Implement `IInvoiceAnalysisService` with structured output | Phase 2 (BFF API) | Playbook records exist |
| Test classification with sample documents | Phase 2 (BFF API) | Classification handler + playbook |
| Test extraction with sample invoices | Phase 2 (BFF API) | Extraction handler + playbook |
| Tune confidence thresholds | Phase 3 (Integration) | Real-world test data |
| Version prompts after tuning | Phase 3 (Integration) | Tuning complete |

**Prompt storage location:** Playbook prompts are NOT stored in source code. They are Dataverse records managed via the existing Playbook CRUD endpoints (`/api/ai/playbooks`). Initial prompt content is deployed via Dataverse solution import (unmanaged solution per ADR-022).

### 6.6 Entity Matching (AI-to-Dataverse Record Resolution)

Bridging AI-extracted references to actual Dataverse records is critical to the invoice pipeline. When the classification playbook extracts text like "Matter 2026-001" or "Smith & Associates", those strings must be matched to real `sprk_matter`, `sprk_project`, and `sprk_organization` records in Dataverse. This capability is required at two points in the pipeline:

1. **After classification** (Section 6.1, step 6): Populates `sprk_mattersuggestedref` and `sprk_mattersuggestionjson` on the attachment Document, giving the reviewer ranked suggestions
2. **During review confirmation** (Section 7.3): Reviewer selects from suggestions or manually searches — the confirmed match is stored as `sprk_relatedmatterid` / `sprk_relatedvendororgid`

#### Existing Infrastructure (Reuse)

The codebase already has a record matching system built for Document Intelligence:

| Component | Location | What It Does | Reuse? |
|-----------|----------|-------------|--------|
| `IRecordMatchService` | `Services/RecordMatching/RecordMatchService.cs` | Multi-strategy record matching using Azure AI Search index | **Reuse** — core matching engine |
| `DataverseIndexSyncService` | `Services/RecordMatching/DataverseIndexSyncService.cs` | Syncs `sprk_matter`, `sprk_project`, `sprk_invoice` records to AI Search index with reference numbers, names, descriptions | **Reuse** — keeps search index current |
| `SearchIndexDocument` | `Services/RecordMatching/SearchIndexDocument.cs` | Index schema: `RecordName`, `ReferenceNumbers`, `Organizations`, `People`, `ContentVector` | **Reuse** — already has the fields needed |
| `RecordMatchEndpoints` | `Api/Ai/RecordMatchEndpoints.cs` | `POST /api/ai/document-intelligence/match-records` endpoint | **Reuse** — for reviewer manual search |
| `EmailAssociationService` | `Services/Email/EmailAssociationService.cs` | Confidence-scored signal system for email-to-entity matching (tracking tokens, domain matching, contact matching) | **Pattern reference** — follow scoring model |

**Key existing fields for matching:**

| Entity | Name Field | Reference Number Field | Other Searchable Fields |
|--------|-----------|----------------------|------------------------|
| `sprk_matter` | `sprk_mattername` | `sprk_matternumber` | `sprk_description`, client org (lookup) |
| `sprk_project` | `sprk_projectname` | `sprk_projectnumber` | `sprk_description` |
| `sprk_organization` | `name` | — | Address, domain |

#### Matching Strategy (Classification Handler)

After the classification playbook returns `ClassificationResult` with extracted hints, the `AttachmentClassificationJobHandler` runs entity matching using a multi-signal approach:

```
ClassificationResult contains:
  hints.vendorName: "Smith & Associates LLP"
  hints.matterReference: "2026-001"

AttachmentClassificationJobHandler:
  │
  ├─ Signal 1: Reference number match (highest confidence)
  │   → IRecordMatchService.MatchAsync({
  │       ReferenceNumbers: ["2026-001"],
  │       RecordTypeFilter: "sprk_matter"
  │     })
  │   → If exact match on sprk_matternumber → confidence 0.95
  │
  ├─ Signal 2: Vendor organization match
  │   → IRecordMatchService.MatchAsync({
  │       Organizations: ["Smith & Associates LLP"],
  │       RecordTypeFilter: "sprk_organization"
  │     })
  │   → Fuzzy name match → confidence 0.60-0.85
  │
  ├─ Signal 3: Parent email context (if attachment has parent email)
  │   → Load parent sprk_document (via sprk_parentdocument)
  │   → If parent email already has sprk_relatedmatterid → confidence 0.90
  │   → (Most emails are about the same matter as their attachments)
  │
  ├─ Signal 4: Keyword/name overlap
  │   → IRecordMatchService.MatchAsync({
  │       Keywords: [extracted terms from classification],
  │       RecordTypeFilter: "all"
  │     })
  │   → Keyword match → confidence 0.40-0.60
  │
  └─ Combine signals → rank candidates → store top 5 in sprk_mattersuggestionjson
```

**Confidence scoring** (follows `EmailAssociationService` precedent):

| Signal | Confidence | Example |
|--------|-----------|---------|
| Exact reference number match (`sprk_matternumber`) | 0.95 | "2026-001" matches matter `sprk_matternumber = "2026-001"` |
| Parent email already associated with matter | 0.90 | Parent .eml has `sprk_relatedmatterid` set |
| Fuzzy organization name match | 0.60–0.85 | "Smith & Associates" matches "Smith & Associates LLP" (Jaccard similarity) |
| Keyword/description overlap | 0.40–0.60 | Invoice text mentions "Smith v Jones" matching matter name |

**Output stored on `sprk_document`:**

```json
// sprk_mattersuggestedref (simple text for display)
"2026-001"

// sprk_mattersuggestionjson (structured candidates for reviewer)
{
  "candidates": [
    {
      "recordId": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "recordType": "sprk_matter",
      "recordName": "Smith v. Jones (2026-001)",
      "confidence": 0.95,
      "matchReasons": ["Reference: 2026-001 (exact match)"],
      "signals": ["reference_number", "parent_email"]
    },
    {
      "recordId": "7fa85f64-5717-4562-b3fc-2c963f66afa7",
      "recordType": "sprk_matter",
      "recordName": "Smith Corp Retainer",
      "confidence": 0.65,
      "matchReasons": ["Organization: Smith & Associates (fuzzy match)"],
      "signals": ["vendor_org"]
    }
  ],
  "vendorSuggestion": {
    "recordId": "9fa85f64-5717-4562-b3fc-2c963f66afa8",
    "recordType": "sprk_organization",
    "recordName": "Smith & Associates LLP",
    "confidence": 0.85,
    "matchReasons": ["Organization name: fuzzy match (0.92 similarity)"]
  }
}
```

#### What's New vs. Reused

| Component | Status | Details |
|-----------|--------|---------|
| `IRecordMatchService` | **Reuse as-is** | Already supports `ReferenceNumbers`, `Organizations`, `Keywords` matching |
| `DataverseIndexSyncService` | **Reuse as-is** | Already syncs matters, projects with reference numbers |
| Parent email context signal | **New logic** | Load parent document, check existing matter association |
| Signal aggregation + ranking | **New logic** | Combine multiple signals, rank, select top candidates |
| `sprk_mattersuggestionjson` serialization | **New logic** | Serialize ranked candidates for reviewer display |
| `POST /api/ai/document-intelligence/match-records` | **Reuse for reviewer search** | Reviewer can search manually if suggestions are wrong |

#### Reviewer Interaction with Suggestions

In the review queue (Section 7), the reviewer sees:
1. **Top suggestion** pre-filled in matter/vendor dropdowns (if confidence ≥ 0.80)
2. **Alternative candidates** shown below as selectable options with confidence scores
3. **Manual search** available via the existing record match endpoint if no suggestion is correct
4. **Required action**: Reviewer MUST confirm matter and vendor before invoice extraction proceeds (enforced by BFF validation)

This ensures human oversight on every entity association while leveraging AI suggestions to accelerate the process.

---

## 7. Review Queue UX (MVP)

### 7.1 "Invoice Review Queue" view

Dataverse view over `sprk_document`:

**Filter criteria:**
- `sprk_documenttype` = 100000007 (Email Attachment) — existing field
- `sprk_invoicereviewstatus` = ToReview
- `sprk_classification` in (InvoiceCandidate, Unknown)

**Columns:**
- Received date (from parent email)
- File name
- Classification + confidence
- Vendor hint, invoice number hint, total hint
- Matter suggestion
- Related email (link to parent document)

### 7.2 Review actions

Reviewer performs via a form or (future) PCF control:

**Confirm invoice:**
1. Set `sprk_relatedmatterid` (required)
2. Set `sprk_relatedvendororgid` (required)
3. Optionally correct invoice number, date, total
4. Submit → BFF endpoint:
   - Validates required fields
   - Sets `sprk_invoicereviewstatus = ConfirmedInvoice`
   - Sets `sprk_invoicereviewedby` and `sprk_invoicereviewedon`
   - Creates `sprk_invoice` record (Status = ToReview, ExtractionStatus = NotRun)
   - Enqueues `InvoiceExtraction` job
   - Returns 202 with jobId per ADR-017

**Reject:**
1. Sets `sprk_invoicereviewstatus = RejectedNotInvoice`
2. Sets reviewer + timestamp
3. No invoice created, no extraction, no deletion (retained for audit)

### 7.3 BFF endpoint for confirmation

```
POST /api/finance/invoice-review/confirm
Authorization: Bearer {token}
Content-Type: application/json

{
  "documentId": "{guid}",
  "matterId": "{guid}",
  "vendorOrgId": "{guid}",
  "invoiceNumber": "INV-2026-001",    // optional correction
  "invoiceDate": "2026-01-15",        // optional correction
  "totalAmount": 15000.00,            // optional correction
  "currency": "USD"                   // optional
}

Response: 202 Accepted
{
  "jobId": "{guid}",
  "invoiceId": "{guid}",
  "statusUrl": "/api/jobs/{jobId}/status"
}
```

---

## 8. Financial Measures and Caching

### 8.1 Computed measures (from SpendSnapshots)

| Measure | Source | VisibilityFilter |
|---------|--------|-----------------|
| Total Invoiced Amount To Date | SpendSnapshot (ToDate, TOTAL) | ACTUAL_INVOICED |
| Budget Total | BudgetPlan.TotalBudget | N/A |
| Budget Remaining | Budget Total − Invoiced To Date | ACTUAL_INVOICED |
| Budget Variance | SpendSnapshot.BudgetVariance | ACTUAL_INVOICED |
| Budget Variance % | SpendSnapshot.BudgetVariancePct | ACTUAL_INVOICED |
| Spend Velocity % | SpendSnapshot.VelocityPct (configurable: MoM, QoQ, YoY) | ACTUAL_INVOICED |
| Prior Period Spend | SpendSnapshot.PriorPeriodAmount | ACTUAL_INVOICED |
| Open Signals Count | Count of active SpendSignals | N/A |

### 8.2 Caching strategy (per ADR-009)

Matter financial summaries are NOT stored as fields on the Matter entity. Instead:

- **BFF endpoint:** `GET /api/finance/matters/{matterId}/summary`
- **Source:** Queries SpendSnapshots (ToDate, TOTAL, ACTUAL_INVOICED) + BudgetPlan + active SpendSignals
- **Cache:** Redis with key `matter:{matterId}:finance-summary`, TTL 5 minutes
- **Invalidation:** SpendSnapshotGeneration job explicitly deletes the Redis key after writing new snapshots
- **Fallback:** On cache miss, compute from Dataverse, cache result

This avoids:
- Write contention on the Matter entity
- Plugin trigger risk from frequent writes
- Stale computed fields that drift from source data

### 8.3 PCF integration

The Finance Intelligence panel in the Matter form reads from the BFF endpoint above. It renders:
- Budget gauge (total, remaining, variance)
- Spend timeline (monthly snapshots)
- Active signals list
- Invoice history (from sprk_invoice records)

---

## 9. Status Models

### 9.1 Document classification

| Value | Meaning |
|-------|---------|
| InvoiceCandidate | AI believes this is an invoice |
| NotInvoice | AI believes this is NOT an invoice |
| Unknown | AI confidence below threshold |

### 9.2 Invoice review status (on Document)

| Value | Meaning |
|-------|---------|
| ToReview | Awaiting human review |
| ConfirmedInvoice | Reviewer confirmed as invoice |
| RejectedNotInvoice | Reviewer rejected (retained for audit) |

### 9.3 Invoice status

| Value | Meaning |
|-------|---------|
| ToReview | Invoice created, extraction pending or in progress |
| Reviewed | Extraction complete, BillingEvents created |

### 9.4 Extraction status

| Value | Meaning |
|-------|---------|
| NotRun | Extraction not yet attempted |
| Extracted | Extraction succeeded |
| Failed | Extraction failed (retryable) |

No "Delete" state anywhere. Rejected candidates and failed extractions are retained.

---

## 10. ADR Compliance Matrix

| ADR | Relevance | Compliance Approach |
|-----|-----------|-------------------|
| ADR-001 | Minimal API + workers | All new endpoints are Minimal API; all background work is BackgroundService + Service Bus |
| ADR-002 | No heavy plugins | No plugins involved; all logic in BFF API |
| ADR-004 | Async job contract | Four new JobTypes with standard JobContract envelope |
| ADR-008 | Endpoint auth filters | Invoice review endpoints use endpoint-level authorization filters |
| ADR-009 | Redis caching | Matter finance summaries cached in Redis with explicit invalidation |
| ADR-011 | PCF dataset over subgrids | Future review queue PCF follows dataset pattern |
| ADR-013 | AI architecture | Classification and extraction use structured output via `IInvoiceAnalysisService` (consuming `IOpenAiClient.GetStructuredCompletionAsync<T>`); entity matching via `IEntityMatchingService` |
| ADR-014 | AI caching/reuse | Prompt templates versioned; extraction results cached by correlation ID |
| ADR-015 | Data governance | No document content in logs; job payloads contain IDs only |
| ADR-016 | Rate limits/backpressure | AI extraction jobs bounded concurrency; rate-limited API endpoints |
| ADR-017 | Job status contract | All async endpoints return 202 + jobId + statusUrl |
| ADR-019 | ProblemDetails errors | All error responses use ProblemDetailsHelper |
| ADR-020 | Versioning | Job payload schema is tolerant; prompt versions tracked |

---

## 11. Job Chain Summary

```
EmailToDocumentJobHandler (existing)
  │ Creates .eml Document + attachment Documents
  │ Enqueues: RagIndexing (existing, for .eml)
  │ Enqueues: AttachmentClassification (new, for each attachment)
  │
  ▼
AttachmentClassificationJobHandler (new)
  │ Classifies attachment, writes hints
  │ Sets InvoiceReviewStatus = ToReview if candidate/unknown
  │ No downstream enqueue (human gate)
  │
  ▼
[Human review via UI → POST /api/finance/invoice-review/confirm]
  │ Creates sprk_invoice record
  │ Enqueues: InvoiceExtraction
  │
  ▼
InvoiceExtractionJobHandler (new)
  │ Extracts billing facts, creates BillingEvents
  │ Enqueues: SpendSnapshotGeneration
  │ Enqueues: RagIndexing (existing, for invoice document)
  │
  ▼
SpendSnapshotGenerationJobHandler (new)
  │ Computes snapshots + signals
  │ Invalidates Redis cache
  │ No downstream enqueue
```

---

## 12. Alignment with Full Deterministic E-Billing

This MVP is consistent with a future end-to-end process:

- **Intake channels:** Email remains one channel; future adds API/LEDES import, external portal. Same Document → Invoice → BillingEvent chain.
- **Invoice entity:** Exists as a durable artifact. Future adds submission, approvals, LEDES export, payment tracking. Schema is additive.
- **BillingEvents:** Remain the canonical analytic fact. Future deterministic line-item parsing from LEDES produces the same BillingEvent records.
- **VisibilityState:** Supports additional lifecycle stages without schema duplication. Firm-side WIP/PreBill is additive.
- **Snapshots/Signals:** Continue unchanged regardless of intake channel or invoice complexity.
- **Work Items:** Future framework replaces Document-as-queue by creating WorkItem records that reference Documents. Document classification fields remain as evidence.

The MVP is not throwaway. It is the "intelligence spine."

---

## 13. Acceptance Criteria

1. Email ingestion creates Document records for .eml and attachments with SPE files linked (existing pipeline extended)
2. Classification job populates classification + confidence + invoice hints on attachment Documents
3. Invoice Review Queue (Dataverse view) shows candidate/unknown attachments for review
4. Reviewer confirms invoice, links Matter/Project and VendorOrg, and triggers extraction via BFF endpoint
5. Confirmed invoice extraction:
   - Creates `sprk_invoice` record
   - Creates BillingEvents with `VisibilityState = Invoiced`
   - Enqueues snapshot generation and invoice indexing
6. Snapshot generation:
   - Creates SpendSnapshots with budget variance and velocity metrics
   - Creates SpendSignals for threshold breaches
   - Invalidates Redis cache for Matter rollup
7. Invoice content indexed into search index with invoice-specific metadata
8. Matter/Project Finance Intelligence panel reflects updated snapshots via BFF endpoint
9. Rejected candidates are retained with RejectedNotInvoice status (never deleted)
10. All async operations return 202 + job status URL per ADR-017
11. Unit tests for SpendSnapshot aggregation logic (deterministic, no AI — priority test target):
    - Monthly and ToDate aggregation from BillingEvents
    - Budget variance computation (positive and negative)
    - Velocity metrics (rate of change vs prior period: MoM, QoQ, YoY configurable)
    - Idempotent snapshot upsert (re-running produces same result)
12. Unit tests for signal evaluation rules:
    - BudgetExceeded signal fires at 100%+ spend
    - BudgetWarning signal fires at configurable threshold (default 80%)
    - VelocitySpike detection logic
13. Integration tests for classification → review → extraction → snapshot pipeline (end-to-end job chain)

---

## 14. Implementation Notes for Claude Code

### 14.1 General Architecture

- All new functionality lives in the BFF API (`src/server/api/Sprk.Bff.Api/`). No Azure Functions, no separate services.
- New endpoints under `Api/Finance/` namespace following existing Minimal API patterns (e.g., `AnalysisEndpoints.cs` uses `app.MapGroup("/api/ai/analysis")`).
- New job handlers under `Services/Jobs/Handlers/` following `EmailToDocumentJobHandler` and `RagIndexingJobHandler` patterns.
- All critical mutations (Invoice creation, BillingEvent creation) are behind the confirmed review gate.
- Idempotency via correlation IDs and alternate keys on BillingEvent and SpendSnapshot.
- VisibilityState is deterministic for MVP — set in handler code, never inferred by LLM.
- Snapshot computation is pure business logic (no AI) — query BillingEvents, aggregate, compare to budget, detect signals.
- Prompt templates stored as Dataverse playbook records, versioned per ADR-014.

### 14.2 DI Registration Strategy (ADR-010 Compliance)

ADR-010 specifies ≤15 non-framework DI registrations using feature module extensions. **The current codebase significantly exceeds this**:

| Location | Registrations | Purpose | In Feature Module? |
|----------|--------------|---------|-------------------|
| `Program.cs` (direct) | ~92 | Options, auth, AI, RAG, email, jobs, resilience | No (~85 inline) |
| `AddSpaarkeCore()` | ~7 | Auth, access data source, request cache | Yes |
| `AddDocumentsModule()` | ~10 | SPE operations, token cache, checkout service | Yes |
| `AddWorkersModule()` | ~4 | Service Bus, idempotency, batch status | Yes |
| `AddOfficeModule()` | ~4 | Office add-in services | Yes |
| **Total** | **~117** | | **~29 in modules, ~85 inline** |

**The core problem**: Only 4 feature modules exist covering ~29 registrations. The remaining ~85 are inline in `Program.cs` (1,790 lines total), many inside conditional blocks (`if (documentIntelligenceEnabled) { ... }`). The ADR's global ≤15 ceiling is exceeded 8x and cannot be restored.

**Performance impact assessment**:

| Concern | Impact | Details |
|---------|--------|---------|
| Startup time | Negligible | ~117 registrations × ~0.05ms = ~6ms (invisible against 2-5s startup from Redis/ServiceBus/Dataverse) |
| Runtime resolution | None | .NET 8 DI uses compiled expression trees: O(1) after first resolution (<1μs) |
| Memory per request | Low-moderate | ~40 scoped registrations = ~40 object allocations per HTTP request; Gen0 GC handles easily at normal load |
| Cognitive complexity | **High** | `Program.cs` is 1,790 lines; dependency chains are invisible when conditional blocks disable services; this is the real cost |

**Recommended ADR-010 change**: Update ADR-010 to replace the global ceiling with two new constraints:
1. **Per-module ceiling**: ≤15 registrations per feature module (achievable and meaningful)
2. **No inline registrations**: All registrations must live inside a feature module extension method; `Program.cs` should only call `builder.Services.AddXxxModule()`

**Future refactor** (separate project, not blocking Finance Intelligence R1):
- Extract `AddAnalysisModule()` (~30 registrations from Program.cs lines 379-539)
- Extract `AddEmailModule()` (~10 registrations from Program.cs lines 571-618)
- Extract `AddJobsModule()` (~10 registrations from Program.cs lines 621-689)
- Extract `AddResilienceModule()` (~5 registrations from Program.cs lines 1092-1105)
- Program.cs DI section shrinks from ~650 lines to ~30 lines of module calls

**Finance module approach for R1**: Create `AddFinanceModule(IServiceCollection, IConfiguration)` extension in `Infrastructure/DI/FinanceModule.cs`:

```csharp
// Program.cs — single line addition
builder.Services.AddFinanceModule(builder.Configuration);

// Infrastructure/DI/FinanceModule.cs
public static class FinanceModule
{
    public static IServiceCollection AddFinanceModule(
        this IServiceCollection services, IConfiguration configuration)
    {
        // Job handlers (registered via IJobHandler for dispatcher routing)
        services.AddScoped<IJobHandler, AttachmentClassificationJobHandler>();
        services.AddScoped<IJobHandler, InvoiceExtractionJobHandler>();
        services.AddScoped<IJobHandler, InvoiceIndexingJobHandler>();
        services.AddScoped<IJobHandler, SpendSnapshotGenerationJobHandler>();

        // Finance AI services (structured output via IOpenAiClient)
        services.AddScoped<IInvoiceAnalysisService, InvoiceAnalysisService>();

        // Finance business services
        services.AddScoped<InvoiceReviewService>();
        services.AddScoped<SpendSnapshotService>();
        services.AddScoped<SignalEvaluationService>();
        services.AddScoped<InvoiceSearchService>();

        // Finance options (includes VelocityComparisonType: MoM, QoQ, YoY)
        services.AddOptions<FinanceOptions>()
            .Bind(configuration.GetSection("Finance"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // Finance telemetry
        services.AddSingleton<FinanceTelemetry>();

        return services;
    }
}
```

This adds ~12 registrations (under the per-module ceiling of ≤15) bringing the total to ~129. Program.cs grows by 1 line.

### 14.3 Structured AI Output Architecture (Two-Layer)

Structured, typed AI output is **critical and fundamental** to the Spaarke AI solution architecture. This is not a finance-specific concern — it is a platform capability that Finance Intelligence is the first consumer of. The architecture uses two layers: a foundational method on `IOpenAiClient` and domain-specific services that consume it.

#### Layer 1: Foundation — `GetStructuredCompletionAsync<T>` on IOpenAiClient

The existing `IOpenAiClient` interface has `GetCompletionAsync(prompt, model)` returning raw strings and `GetChatCompletionWithToolsAsync(messages, tools, model)` for function calling. Neither supports Azure OpenAI's structured output mode (`response_format: { type: "json_schema" }`).

**New method added to `IOpenAiClient`:**

```csharp
/// <summary>
/// Get a typed completion with guaranteed JSON schema conformance.
/// Uses Azure OpenAI structured output (constrained decoding at the token level).
/// </summary>
Task<T> GetStructuredCompletionAsync<T>(
    IEnumerable<ChatMessage> messages,
    BinaryData jsonSchema,
    string schemaName,
    string? model = null,
    CancellationToken cancellationToken = default) where T : class;
```

**Implementation in `OpenAiClient`:**

```csharp
public async Task<T> GetStructuredCompletionAsync<T>(
    IEnumerable<ChatMessage> messages,
    BinaryData jsonSchema,
    string schemaName,
    string? model = null,
    CancellationToken cancellationToken = default) where T : class
{
    var deploymentName = model ?? _options.SummarizeModel;
    var chatClient = _client.GetChatClient(deploymentName);

    var chatOptions = new ChatCompletionOptions
    {
        MaxOutputTokenCount = _options.MaxOutputTokens,
        Temperature = _options.Temperature,
        ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
            schemaName, jsonSchema, strictSchemaEnabled: true)
    };

    var response = await _circuitBreaker.ExecuteAsync(async ct =>
        await chatClient.CompleteChatAsync(messages.ToList(), chatOptions, ct),
        cancellationToken);

    var json = response.Value.Content.FirstOrDefault()?.Text
        ?? throw new InvalidOperationException("Empty structured completion response");

    return JsonSerializer.Deserialize<T>(json, JsonSerializerOptions.Web)
        ?? throw new InvalidOperationException($"Failed to deserialize to {typeof(T).Name}");
}
```

**Key properties:**
- Uses `ChatResponseFormat.CreateJsonSchemaFormat` with `strictSchemaEnabled: true` — this is **constrained decoding at the token level**, not prompt engineering. The model physically cannot produce tokens that violate the schema.
- Inherits existing circuit breaker, logging, and model selection from `OpenAiClient`
- Reusable by any future module (contract analysis, LEDES import, etc.)
- Lives in `IOpenAiClient` / `OpenAiClient` — no new services needed at this layer

#### Layer 2: Domain Service — `IInvoiceAnalysisService`

Finance-specific AI logic is encapsulated in a domain service that consumes Layer 1:

```csharp
public interface IInvoiceAnalysisService
{
    Task<ClassificationResult> ClassifyAttachmentAsync(
        Guid documentId, string extractedText, CancellationToken ct);

    Task<ExtractionResult> ExtractInvoiceFactsAsync(
        Guid invoiceId, string extractedText, InvoiceHints? hints, CancellationToken ct);
}

public record ClassificationResult(
    InvoiceClassification Classification,
    decimal Confidence,
    InvoiceHints? Hints,
    MatterSuggestion? MatterSuggestion);

public record ExtractionResult(
    InvoiceHeader Header,
    IReadOnlyList<BillingEventLine> Lines,
    decimal ExtractionConfidence);
```

**Implementation pattern:**

```csharp
public class InvoiceAnalysisService : IInvoiceAnalysisService
{
    private readonly IOpenAiClient _openAiClient;
    private readonly IPlaybookService _playbookService;
    private readonly ILogger<InvoiceAnalysisService> _logger;

    public async Task<ClassificationResult> ClassifyAttachmentAsync(
        Guid documentId, string extractedText, CancellationToken ct)
    {
        // 1. Load playbook prompt from Dataverse (versioned per ADR-014)
        var playbook = await _playbookService.GetByNameAsync("Attachment Classification", ct);

        // 2. Build ChatMessages from playbook system + user prompts
        var messages = BuildClassificationMessages(playbook, extractedText);

        // 3. Call Layer 1 with structured output enforcement
        return await _openAiClient.GetStructuredCompletionAsync<ClassificationResult>(
            messages,
            ClassificationResultSchema,     // BinaryData JSON schema
            "ClassificationResult",
            model: "gpt-4o-mini",           // Fast, cost-effective for classification
            ct);
    }
}
```

#### Why Two Layers?

| Concern | Layer 1 (IOpenAiClient) | Layer 2 (IInvoiceAnalysisService) |
|---------|------------------------|-----------------------------------|
| Structured output enforcement | `response_format` + `ChatResponseFormat.CreateJsonSchemaFormat` | Consumed, not implemented |
| Circuit breaker, resilience | Handled | Inherited |
| Prompt construction | Not involved | Loads playbook, builds ChatMessages |
| JSON schema definition | Receives `BinaryData` | Defines `ClassificationResult` / `ExtractionResult` schemas |
| Domain logic | None | Finance-specific prompt templates, hint injection, model selection |
| Testability | Mock `IOpenAiClient` in service tests | Mock `IInvoiceAnalysisService` in handler tests |
| Reusability | Any module can call `GetStructuredCompletionAsync<T>` | Finance-specific; future modules create their own domain service |

**Without Layer 2**, every job handler would need to load playbooks, build prompts, define schemas, and call `GetStructuredCompletionAsync<T>` directly — too much AI plumbing in handlers.

**Without Layer 1**, structured output enforcement would be duplicated in every domain service that needs typed AI output.

#### Relationship to Existing AppOnlyAnalysisService

`IInvoiceAnalysisService` does NOT depend on `AppOnlyAnalysisService`. Both use `IOpenAiClient` and `IPlaybookService` independently:

```
IOpenAiClient (singleton, shared infrastructure)
  ├── AppOnlyAnalysisService (existing: multi-tool playbook orchestration)
  │     └── Returns DocumentAnalysisResult (generic profile strings)
  └── InvoiceAnalysisService (new: single-call structured output)
        └── Returns ClassificationResult / ExtractionResult (typed records)
```

The existing playbook/tool framework (`AppOnlyAnalysisService` → `IScopeResolverService` → `IToolHandlerRegistry`) is designed for open-ended multi-tool orchestration. Finance playbooks use a simpler pattern: load prompt from playbook record, call OpenAI with structured output, return typed result. Both patterns load prompts from the same `sprk_playbook` Dataverse table — the difference is execution model, not storage.

#### Options Evaluated

Three options were considered before arriving at the two-layer architecture:

| Option | Description | Structured Output? | Verdict |
|--------|-------------|-------------------|---------|
| **A: Extend IAppOnlyAnalysisService** | Add `AnalyzeDocumentWithSchemaAsync<T>()` | Would require threading `response_format` through entire playbook/tool framework | Rejected — too complex, wrong execution model |
| **B: Parse in handlers** | Handlers call `GetCompletionAsync()` then deserialize | No enforcement — raw string return, "prompt and pray" | Rejected — fragile for complex extraction (line items, nested objects) |
| **C: Two-layer** | Foundation on `IOpenAiClient` + domain service | Full enforcement via constrained decoding | **Selected** — clean, typed, reusable |

### 14.4 EmailToDocumentJobHandler Modification

The classification job must be enqueued from the existing `EmailToDocumentJobHandler.ProcessSingleAttachmentAsync()` method. This is a **modification to a production-critical handler**.

**Current code** (`ProcessSingleAttachmentAsync`, line ~773):
```csharp
// Currently enqueues for each attachment:
await EnqueueAiAnalysisJobAsync(childDocumentId, "EmailAttachment", ct);
await EnqueueRagIndexingJobAsync(driveId, fileHandle.Id, childDocumentId, attachment.FileName, ct);
```

**Required addition** (alongside existing enqueues, not replacing):
```csharp
await EnqueueAiAnalysisJobAsync(childDocumentId, "EmailAttachment", ct);
await EnqueueRagIndexingJobAsync(driveId, fileHandle.Id, childDocumentId, attachment.FileName, ct);
// NEW: Classify attachment for invoice detection (Finance Intelligence MVP)
await EnqueueAttachmentClassificationJobAsync(childDocumentId, driveId, fileHandle.Id, ct);
```

**Feature flag**: Add `AutoClassifyAttachments` to `EmailProcessingOptions` (default: `false` for safe rollout). Classification enqueue only fires when the flag is `true`. This follows the same pattern as the existing `AutoEnqueueAi` and `AutoIndexToRag` flags.

### 14.5 Redis Cache Key Convention

The Redis instance prefix is configured as `sdap:` (see `Program.cs` line 295). The full cache key for matter finance summaries will be:

```
sdap:matter:{matterId}:finance-summary
```

The `SpendSnapshotGenerationJobHandler` must use `IDistributedCache.RemoveAsync("matter:{matterId}:finance-summary")` — the `sdap:` prefix is added automatically by `StackExchangeRedisCache`.

Redis invalidation is **explicit** (delete key after snapshot write), not TTL-based. A 5-minute TTL acts as a safety net for cache entries that survive a failed invalidation.

### 14.6 Phased Build Sequence

This section defines the implementation order, explicitly showing when foundation capabilities, playbooks, AI services, job handlers, and RAG infrastructure are built. Dependencies flow left-to-right; tasks in the same phase can be parallelized.

#### Phase 1: Foundation (Dataverse schema + AI platform capability)

| Task | What | Type | Dependencies |
|------|------|------|-------------|
| 1a | Dataverse schema: Create `sprk_invoice`, `sprk_billingevent`, `sprk_spendsnapshot`, `sprk_spendsignal`, `sprk_budgetplan` entities + relationships | Schema | None |
| 1b | Dataverse schema: Add classification/review fields to existing `sprk_document` entity | Schema | None |
| 1c | Add `GetStructuredCompletionAsync<T>` to `IOpenAiClient` interface + `OpenAiClient` implementation | Code (foundation) | None |
| 1d | Unit tests for structured output method (mock schema, verify `ChatResponseFormat` configuration) | Tests | 1c |
| 1e | Write "Attachment Classification" prompt template (system prompt + user prompt template) | Content authoring | None |
| 1f | Write "Invoice Extraction" prompt template (system prompt + user prompt template) | Content authoring | None |
| 1g | Create playbook records in Dataverse: "Attachment Classification" and "Invoice Extraction" | Dataverse data | 1a, 1e, 1f |
| 1h | Update ADR-010 with per-module ceiling constraint | Documentation | None |

**Phase 1 exit criteria**: Dataverse entities exist, structured output method passes tests, playbook records exist in Dataverse.

#### Phase 2: AI Services + Job Handlers

| Task | What | Type | Dependencies |
|------|------|------|-------------|
| 2a | Define `ClassificationResult`, `ExtractionResult`, `InvoiceHints` C# record types + JSON schemas (`BinaryData`) | Code (models) | 1c |
| 2b | Implement `IInvoiceAnalysisService` / `InvoiceAnalysisService` (classification + extraction methods) | Code (AI service) | 1c, 1g, 2a |
| 2c | Create `AddFinanceModule()` DI registration in `Infrastructure/DI/FinanceModule.cs` | Code (DI) | 2b |
| 2d | Implement `AttachmentClassificationJobHandler` (classifies, writes hints to `sprk_document`, sets review status, runs entity matching) | Code (handler) | 2b, 2d1, 1b |
| 2d1 | Implement entity matching signal aggregation in classification handler (multi-signal: reference number, vendor org, parent email context, keyword overlap — reuses existing `IRecordMatchService`) | Code (matching) | 1b |
| 2e | Modify `EmailToDocumentJobHandler` to enqueue `AttachmentClassification` job (+ `AutoClassifyAttachments` feature flag) | Code (modification) | 2d |
| 2f | Implement `InvoiceReviewService` + `POST /api/finance/invoice-review/confirm` endpoint | Code (endpoint) | 1a |
| 2g | Implement `InvoiceExtractionJobHandler` (extracts facts, creates `sprk_invoice` + `sprk_billingevent` records, enqueues downstream jobs) | Code (handler) | 2b, 2f |
| 2h | Implement `SpendSnapshotService` + `SignalEvaluationService` (pure business logic — aggregate, compute variance, detect signals) | Code (business logic) | 1a |
| 2i | Implement `SpendSnapshotGenerationJobHandler` (computes snapshots, creates signals, invalidates Redis cache) | Code (handler) | 2h |
| 2j | Unit tests: SpendSnapshot aggregation (deterministic, no AI — priority test target per acceptance criteria 11-12) | Tests | 2h |

**Phase 2 exit criteria**: Classification → review → extraction → snapshot chain works end-to-end. Unit tests pass for snapshot aggregation and signal rules.

#### Phase 3: Invoice RAG + Search

| Task | What | Type | Dependencies |
|------|------|------|-------------|
| 3a | Define invoice AI Search index schema (JSON definition + Bicep/ARM template) | Infrastructure | None |
| 3b | Create/deploy invoice index in Azure AI Search (`spaarke-invoices-{tenantId}`) | Infrastructure | 3a |
| 3c | Implement `InvoiceIndexingJobHandler` (financial metadata enrichment from Dataverse + index upsert) | Code (handler) | 3b, 2g |
| 3d | Implement `InvoiceSearchService` + `GET /api/finance/invoices/search` endpoint (hybrid: keyword + vector + semantic) | Code (endpoint) | 3b |
| 3e | Wire invoice indexing into extraction job chain (enqueue `InvoiceIndexing` after extraction succeeds) | Code (wiring) | 3c, 2g |

**Phase 3 exit criteria**: Confirmed invoices are indexed with financial metadata. Invoice search returns results filtered by vendor/amount/date range.

#### Phase 4: Integration, Tuning, and Polish

| Task | What | Type | Dependencies |
|------|------|------|-------------|
| 4a | `GET /api/finance/matters/{matterId}/summary` endpoint (Redis-cached, 5-min TTL + explicit invalidation) | Code (endpoint) | 2h, 2i |
| 4b | Tune classification confidence thresholds with real test documents | AI tuning | 2d |
| 4c | Tune extraction prompts with real invoice samples | AI tuning | 2g |
| 4d | Version playbook prompts after tuning (increment `sprk_playbook.sprk_version`) | Dataverse data | 4b, 4c |
| 4e | End-to-end integration tests: classification → review → extraction → snapshot → indexing (acceptance criteria 13) | Tests | All above |
| 4f | Invoice Review Queue Dataverse view (acceptance criteria 3) | Configuration | 1b |

**Phase 4 exit criteria**: All 13 acceptance criteria pass. Prompts are tuned and versioned. Integration tests cover the full job chain.

#### Key Architecture Decisions in This Sequence

1. **Structured output foundation (1c) is built first** — it's a platform capability, prerequisite for both playbooks, and reusable by future modules
2. **Prompt authoring (1e, 1f) is parallel with schema and code work** — prompts don't depend on code; content can be drafted while schema is being built
3. **Playbook records in Dataverse (1g) must exist before handlers run** — they're runtime configuration, not compile-time code
4. **Invoice RAG is Phase 3** — it depends on extraction producing `sprk_invoice` records with financial metadata. The index can't be populated until extraction works.
5. **Snapshot service (2h) is pure business logic with no AI** — highest testability priority. Unit tests (2j) should cover aggregation, variance, velocity, and signal rules before integration testing.
6. **Prompt tuning (4b, 4c) is Phase 4** — requires real-world data and is iterative. Initial prompts are "good enough" for development; tuning happens with production-like data.
7. **Entity matching (2d1) reuses existing `IRecordMatchService`** — no new matching engine needed. The classification handler adds invoice-specific signals (parent email context, vendor matching) on top of the existing multi-strategy search.

---

## 15. Related Architecture Documents

| Document | Relevance |
|----------|-----------|
| EMAIL-TO-DOCUMENT-ARCHITECTURE.md | Existing email intake pipeline being extended |
| RAG-ARCHITECTURE.md | Existing indexing pipeline for invoice search |
| RAG-CONFIGURATION.md | Index configuration and deployment |
| sdap-component-interactions.md | BFF API patterns, job handlers, service layers |
| sdap-overview.md | Entity model, Document schema, SPE patterns |
| auth-AI-azure-resources.md | AI infrastructure, extraction fields |
| ADR-004, ADR-009, ADR-013, ADR-015, ADR-016, ADR-017 | Governing ADRs |
