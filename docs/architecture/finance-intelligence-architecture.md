# Finance Intelligence Architecture

> **Last Updated**: November 22, 2026 (ebilling-r1 050b)
> **Last Reviewed**: 2026-11-22
> **Reviewed By**: ai-procedure-refactoring-r2
> **Status**: Current
> **Purpose**: Architecture documentation for the Finance Intelligence module — AI-powered invoice classification and extraction, deterministic spend aggregation, signal evaluation, VisualHost visualization, and semantic invoice search.

---

## Overview

The Finance Intelligence module provides automated invoice processing with human-in-the-loop review, deterministic financial aggregation, threshold-based alerting, and semantic search over invoice content. It combines AI capabilities (classification, extraction, embeddings) with purely deterministic computation (snapshots, signals, rollups) to produce trusted financial data.

The core design decision is a **sequential pipeline with a human review gate**: AI classifies email attachments as invoice candidates, a human reviewer confirms or rejects, then AI extracts structured line items. All downstream aggregation and visualization is deterministic — no AI involvement in financial calculations.

---

## Component Structure

| Component | Path | Responsibility |
|-----------|------|---------------|
| InvoiceReviewService | `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceReviewService.cs` | Human review confirmation/rejection workflow; on confirm it creates an Internal invoice submission and enqueues `InvoiceSubmissionProcessing` (ebilling-r1 050a, D-134); it creates no invoice itself |
| SpendSnapshotService | `src/server/api/Sprk.Bff.Api/Services/Finance/SpendSnapshotService.cs` | Aggregates BillingEvents into Month + ToDate snapshots; upserts via 5-field alternate key |
| SignalEvaluationService | `src/server/api/Sprk.Bff.Api/Services/Finance/SignalEvaluationService.cs` | Threshold-based signal detection (BudgetExceeded, BudgetWarning, VelocitySpike); strategy pattern for extensibility |
| FinanceSummaryService | `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceSummaryService.cs` | Aggregates snapshots + signals + recent invoices into Redis-cached summary |
| FinanceRollupService | `src/server/api/Sprk.Bff.Api/Services/Finance/FinanceRollupService.cs` | Denormalizes financial fields onto Matter/Project entities for VisualHost |
| InvoiceSearchService | `src/server/api/Sprk.Bff.Api/Services/Finance/InvoiceSearchService.cs` | Hybrid semantic search (vector + keyword + reranking) via Azure AI Search |
| FinancialCalculationToolHandler | `src/server/api/Sprk.Bff.Api/Services/Finance/Tools/FinancialCalculationToolHandler.cs` | AI tool handler for finance calculations (ADR-013) |
| InvoiceExtractionToolHandler | `src/server/api/Sprk.Bff.Api/Services/Ai/Handlers/InvoiceExtractionToolHandler.cs` | AI tool handler for extraction (ADR-013) |
| Models/ | `src/server/api/Sprk.Bff.Api/Services/Finance/Models/` | DocumentClassification, InvoiceHints (classification hints). The extraction models (`ExtractionResult`, `InvoiceHeader`, `BillingEventLine`) and `FinanceJsonSchemas` were deleted by ebilling-r1 task 050b |

---

## Data Flow

### Job Pipeline (Sequential with Human Review Gate)

1. **Classification** (`AttachmentClassification` job, Fast tier): `AttachmentClassificationJobHandler` calls `IInvoiceAi.ClassifyAsync` (Action `invoice-classify`, INVOICE-CLASSIFY@v1, resolved through its `sprk_playbookconsumer` Binding) to classify an email attachment as `InvoiceCandidate`, `NotInvoice`, or `Unknown`. Returns confidence score (0-1) and six hint fields (vendor name, invoice number, invoice date, total amount, currency, matter reference). Feature-flagged trigger from `EmailToDocumentJobHandler`.

2. **Human Review Gate**: Reviewer confirms or rejects via `InvoiceReviewService`. On confirm (ebilling-r1 050a, D-134): marks `sprk_document` `ConfirmedInvoice`, creates one Internal invoice submission for the document through `InvoiceSubmissionService` and enqueues an `InvoiceSubmissionProcessing` job; no confirm path creates `sprk_invoice`. On reject: updates document status to `RejectedNotInvoice`.

3. **Extraction and persistence** (`InvoiceSubmissionProcessing` job, ebilling-r1 049): the submission job reads the invoice through `IInvoiceAi.ReadInvoiceAsync` (a Content Understanding custom analyzer; fail-closed), evaluates it, and its persister is the one writer that creates `sprk_invoice` and its lines. The legacy `InvoiceExtraction` job (`InvoiceExtractionJobHandler`, gpt-4o through a by-name playbook, `InvoiceAnalysisService.ExtractInvoiceFactsAsync`, `FinanceExtraction` / PB-013) was deleted by ebilling-r1 task 050b (D-71): nothing enqueues it, no handler is registered for it, and `FinanceOptions.ExtractionDeploymentName` / `InvoiceExtractionPlaybookId` are gone. A message of that type left on a queue now dead-letters as having no handler.

4. **Aggregation** (`SpendSnapshotGeneration` job): `SpendSnapshotService.GenerateAsync` queries `sprk_billingevent` records where `VisibilityState = Invoiced`, groups by year-month, computes MoM velocity (`(current - prior) / prior * 100`; null when prior = 0), computes budget variance (`budget - invoiced`; positive = under budget). Upserts Month + ToDate snapshots via 5-field composite alternate key.

5. **Signal Evaluation**: `SignalEvaluationService.EvaluateAsync` evaluates snapshots against threshold rules using the strategy pattern. Signals are upserted idempotently via deterministic GUID (matterId XOR signalType).

6. **Rollup**: `FinanceRollupService.RecalculateMatterAsync` denormalizes 9 fields onto `sprk_matter`/`sprk_project` (totalSpendToDate, invoiceCount, monthlySpendCurrent, totalBudget, remainingBudget, budgetUtilizationPercent, monthOverMonthVelocity, averageInvoiceAmount, monthlySpendTimeline JSON). Called by subgrid parent rollup web resource and after background processing.

7. **Indexing** (`InvoiceIndexing` job): Embeds invoice content into `spaarke-invoices-dev` index (3072-dim `text-embedding-3-large` vectors) with contextual metadata header prepended to each chunk.

---

## VisualHost Integration

Finance visualization uses **denormalized fields** on `sprk_matter` / `sprk_project` entities rendered via the VisualHost PCF control with native Dataverse charts. This replaced a custom PCF chart implementation. The `FinanceRollupService` writes these fields after each snapshot run:

- `sprk_totalspendtodate`, `sprk_invoicecount`, `sprk_monthlyspendcurrent`
- `sprk_totalbudget`, `sprk_remainingbudget`, `sprk_budgetutilizationpercent`
- `sprk_monthovermonthvelocity`, `sprk_averageinvoiceamount`
- `sprk_monthlyspendtimeline` (JSON array: `[{month, spend}, ...]` for last 12 months)

**Tradeoff**: Update latency (fields are current at last job run) vs. real-time rendering complexity. For most legal finance use cases, near-real-time (post-extraction) is sufficient.

---

## Invoice Analysis (AI)

**Classification** (Action `invoice-classify`, INVOICE-CLASSIFY@v1; ebilling-r1 task 042): `IInvoiceAi.ClassifyAsync` resolves the Action only through the `invoice-classify` Binding and runs it through `IActionRunner` (ADR-039 / ADR-043); the prompt, output schema and model tier (Fast) live in the `sprk_analysisaction` row, mirrored in `infra/dataverse/actions/invoice-classify.action.json` and `infra/dataverse/outputschemas/invoice-classify.schema.json`. It fails closed (FR-46): a missing Binding, failed completion or invalid output raises `InvoiceAiException` (`ai.invoice.binding_missing` / `completion_failed` / `output_invalid`) and the job retries the first two a bounded number of times, then poisons; it never falls back to a hard-coded prompt. Returns a verdict with confidence score and six hint fields.

**Reading** (`IInvoiceAi.ReadInvoiceAsync`, ebilling-r1 task 043): reads the invoice document with the configured Content Understanding custom analyzer, then applies a deterministic completion step; it is called from the submission job (049). It is a document-model call, not a prompt, and fails closed.

**Retired (ebilling-r1 task 050b, D-71 / D-52):** the legacy Playbook B extraction (`FinanceExtraction`, by-name playbook lookup, `gpt-4o`, `ExtractionResult`) and the C# JSON schemas in `FinanceJsonSchemas.cs`. `IInvoiceAi` no longer has `GetPlaybookByNameAsync` or `GetStructuredCompletionAsync<T>`; every AI capability on it runs an Action through `IActionRunner` (prompt, output schema and model tier in the Action / Binding rows) except embeddings and reading.

---

## Signal Evaluation

`SignalEvaluationService` uses the **strategy pattern** with three rules evaluated per snapshot:

| Signal Type | Rule | Threshold | Severity | Applies To |
|-------------|------|-----------|----------|------------|
| `BudgetExceeded` | `BudgetExceededRule` | spend/budget >= 100% | Critical | ToDate snapshots only |
| `BudgetWarning` | `BudgetWarningRule` | spend/budget >= configurable % (default 80%) | Warning | ToDate snapshots only; suppressed when BudgetExceeded fires |
| `VelocitySpike` | `VelocitySpikeRule` | MoM velocity >= configurable % (default 50%) | Warning | Month snapshots only |

Signals are upserted using a deterministic GUID derived from `matterId XOR signalType` bytes, ensuring idempotent re-evaluation without duplicates.

---

## Spend Snapshots

`SpendSnapshotService` computes purely deterministic aggregations:

- **Period types**: Month + ToDate (Quarter/Year are post-MVP)
- **Velocity**: MoM only (`(current - prior) / prior * 100`); null when prior month has zero spend
- **Budget variance**: `Budget - Invoiced` (positive = under budget, negative = over budget)
- **Budget source**: SUM of all `sprk_budget` records for a matter/project (1:N relationship across fiscal cycles)
- **Alternate key** (5-field composite): `sprk_matter` + `sprk_periodtype` + `sprk_periodkey` + `sprk_bucketkey` + `sprk_visibilityfilter`
- **MVP constants**: BucketKey = `TOTAL`, VisibilityFilter = `ACTUAL_INVOICED`
- **Upsert**: Uses Dataverse `UpsertRequest` for idempotent writes

Supports both matter-level and project-level snapshot generation via `GenerateAsync` and `GenerateForProjectAsync`.

---

## Data Model (Key Relationships)

- **sprk_document**: classification fields (3) + hint fields (6) + association fields (4) on existing entity
- **sprk_invoice**: links confirmed document to matter/vendor; stores reviewer-corrected values; alternate key on `sprk_invoice + sprk_linesequence` for BillingEvent
- **sprk_billingevent**: canonical financial fact (one per invoice line); `VisibilityState` set deterministically to `Invoiced` by handler code, never by AI
- **sprk_budget**: 1:N to matter/project across fiscal cycles; total budget = SUM of all records (not TopCount=1)
- **sprk_spendsnapshot**: pre-computed aggregation; 5-field composite alternate key
- **sprk_spendsignal**: threshold alerts; deterministic GUID for idempotent upsert

**Budget variance**: positive = under budget; negative = over budget.
**Velocity**: `(current month - prior month) / prior month * 100`. Null when prior = 0.

---

## Integration Points

| Direction | Subsystem | Interface | Notes |
|-----------|-----------|-----------|-------|
| Depends on | Email/Communication | `EmailToDocumentJobHandler` | Feature-flagged trigger for classification |
| Depends on | AI Platform | `IOpenAiClient`, `IPlaybookService` | Structured output, playbook-driven prompts |
| Depends on | SPE/Documents | `SpeFileStore` | Document text extraction for AI analysis |
| Depends on | Azure AI Search | `SearchIndexClient` | Invoice semantic search index |
| Depends on | Redis | `IDistributedCache` | Finance summary caching (5-min TTL) |
| Depends on | Service Bus | `JobSubmissionService` | Background job enqueue/dequeue |
| Consumed by | VisualHost PCF | Denormalized fields on `sprk_matter`/`sprk_project` | Native Dataverse chart rendering |
| Consumed by | AI Playbooks | `FinancialCalculationToolHandler`, `InvoiceExtractionToolHandler` | IAiToolHandler implementations (ADR-013) |
| Consumed by | Subgrid rollup | `sprk_subgrid_parent_rollup.js` web resource | Triggers `FinanceRollupService` on form changes |

---

## Caching

| Cache | Key Pattern | TTL | Invalidation |
|-------|-------------|-----|-------------|
| Finance summary (Redis) | `finance:summary:{matterId}` | 5 min (configurable via `FinanceOptions.FinanceSummaryCacheTtlMinutes`) | Explicit `InvalidateSummaryAsync` after SpendSnapshot job |
| Lookup services (IMemoryCache) | By code field | 1 hour | Manual (`ClearCache`) |

Per ADR-009: Redis-first with graceful degradation — cache failures are logged as warnings, never break functionality.

---

## Known Pitfalls

1. **Budget is 1:N, not 1:1**: A matter/project can have multiple `sprk_budget` records across fiscal cycles. Always SUM all records; never use TopCount=1 or FirstOrDefault.

2. **VisibilityState must be deterministic**: The `Invoiced` state is set in handler code, never inferred by AI. This prevents hallucination from bleeding into financial records.

3. **CorrelationId excluded from alternate key**: The SpendSnapshot alternate key intentionally excludes `sprk_correlationid` so re-extraction with new correlation IDs doesn't create duplicate snapshots.

4. **Contextual metadata before vectorization**: A metadata header (`Firm | Matter | Invoice | Date | Total`) must be prepended to each chunk before embedding. Without this, semantic search loses entity context in generic text chunks.

5. **ServiceClient cast required**: `SpendSnapshotService` and `FinanceSummaryService` cast `IDataverseService` to `DataverseServiceClientImpl` for FetchXML/QueryExpression access. Unit tests must mock `ISpendSnapshotService`/`IFinanceSummaryService` directly.

6. **Signal deduplication via deterministic GUID**: Re-evaluation updates existing signals rather than creating duplicates. The GUID is derived from `matterId XOR signalType` with version 4 bits set.

7. **BudgetWarning suppressed when BudgetExceeded fires**: The warning rule explicitly checks `ratio < 1.0m` to avoid double-signaling when budget is exceeded.

---

## Design Decisions

| Decision | Choice | Rationale | ADR |
|----------|--------|-----------|-----|
| Hybrid VisualHost architecture | Denormalized fields + native charts | Avoids PCF lifecycle complexity for read-only visualizations | — |
| Structured output for AI | Action output schema (Dataverse row) run through `IActionRunner` | Prevents hallucinated formats; no C# schema, no by-name playbook (D-52) | ADR-013, ADR-039 |
| Idempotency via alternate keys | BillingEvent + SpendSnapshot composite keys | Safe re-runs without duplicates | — |
| Strategy pattern for signals | `ISignalRule` with 3 implementations | Extensible rule evaluation; new rules without modifying existing | — |
| App-only auth for background jobs | `GraphClientFactory.ForApp()` | No user context in job handlers | ADR-008 |
| Action-driven prompts | Loaded from Dataverse Action rows via `IActionResolver` + the Binding | Prompts travel with solution imports; not hardcoded | ADR-014, ADR-039 |

---

## Constraints

- **MUST**: Reach AI only through `IInvoiceAi` (Action-backed methods through `IActionRunner`; no by-name playbook, no C# output schema) (ADR-013, D-52)
- **MUST**: Set `VisibilityState` deterministically in handler code, never let AI infer it
- **MUST**: SUM all `sprk_budget` records for budget calculations (1:N relationship)
- **MUST NOT**: Log document content, extracted text, or prompts (ADR-015)
- **MUST NOT**: Use global auth middleware; use endpoint filters (ADR-008)
- **MUST**: Extend BFF API, not create separate service (ADR-013)

---

## Related

- [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) — Structured output platform capability
- [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) — BFF patterns including job handler pattern
- [sdap-auth-patterns.md](sdap-auth-patterns.md) — Auth patterns (app-only for background jobs)
- [VISUALHOST-ARCHITECTURE.md](VISUALHOST-ARCHITECTURE.md) — VisualHost PCF control

---

*Last Updated: November 22, 2026*
