# Finance Intelligence Module R1 — Technical Architecture

> **Version**: 1.1
> **Last Updated**: 2026-02-13
> **Status**: Implementation Complete - Pending Deployment

---

## Key Design Decisions

| Decision | Rationale |
|----------|-----------|
| Hybrid VisualHost architecture | Denormalized finance fields on Matter/Project entities + native Dataverse charts (replaced custom PCF — avoids PCF lifecycle complexity for read-only visualizations) |
| Structured output foundation (`GetStructuredCompletionAsync<T>`) | Reusable platform capability using OpenAI constrained decoding; prevents hallucinated formats; enables typed deserialization |
| Idempotency via alternate keys | BillingEvent (`sprk_invoice + sprk_linesequence`) and SpendSnapshot (5-field composite) use composite alternate keys for safe re-runs — `CorrelationId` is intentionally excluded so re-extraction with new correlation IDs doesn't create duplicates |
| VisibilityState determinism | Workflow states (`Invoiced`, future `InternalWIP`, `PreBill`) are set in handler code, never by AI — prevents hallucination from bleeding into financial records |
| Budget 1:N relationship | A Matter/Project can have multiple Budget records across fiscal cycles. Total budget = SUM of all Budget records, NOT TopCount=1 |
| Contextual metadata enrichment before vectorization | Metadata header (`Firm | Matter | Invoice | Date | Total`) prepended to each chunk so semantic search captures entity context even in generic text chunks |
| Signal thresholds (80%/100% budget, 50% MoM velocity) | Hard-coded in MVP but designed for per-matter configuration post-MVP |
| AI analysis via app-only | Background jobs (classification, extraction, indexing) have no user context — uses app-only auth, not OBO |

---

## Job Pipeline (Summary)

The pipeline is sequential with a human review gate after classification:

| Stage | Job | AI? | Trigger |
|-------|-----|-----|---------|
| **Classification** | `AttachmentClassification` | Yes (gpt-4o-mini) | EmailToDocumentJobHandler (feature-flagged) |
| **Human Review Gate** | — | No | User confirms/rejects via UI |
| **Extraction** | `InvoiceExtraction` | Yes (gpt-4o) | Confirm endpoint |
| **Aggregation** | `SpendSnapshotGeneration` | No | InvoiceExtractionJobHandler |
| **Indexing** | `InvoiceIndexing` | No (embeddings) | InvoiceExtractionJobHandler |

All jobs are idempotent. Classification updates document fields in place. Extraction upserts `sprk_billingevent` records via alternate key. Snapshot upserts `sprk_spendsnapshot` via composite alternate key.

---

## AI Integration

**Classification** (gpt-4o-mini + structured output): Classifies email attachments as `InvoiceCandidate`, `NotInvoice`, or `Unknown`. Returns confidence score (0–1) and six hint fields (vendor name, invoice number, invoice date, total amount, currency, matter reference). Hints are pre-populated suggestions for human review — reviewer can correct before extraction.

**Extraction** (gpt-4o + structured output): Extracts billing line items from confirmed invoices. Takes reviewer-corrected hints as input to ground the extraction. Returns header + line items array. `VisibilityState` is set deterministically in the job handler to `Invoiced` — it is never inferred by AI.

**Lookup services** (alternate key pattern): AI configuration records (`sprk_playbook`, `sprk_analysisaction`, etc.) are resolved by code fields (alternate keys), not GUIDs. GUIDs regenerate across environments; alternate keys travel with solution imports. Cached in IMemoryCache with 1-hour TTL.

---

## Data Model (Key Relationships)

- **sprk_document** → classification fields (3) + hint fields (6) + association fields (4) added to existing entity
- **sprk_invoice** → lightweight artifact linking a confirmed document to matter/vendor; reviewer-corrected values stored here
- **sprk_billingevent** → canonical financial fact (one per invoice line); alternate key = `sprk_invoice + sprk_linesequence`
- **sprk_budget** → 1:N to matter/project; **must SUM all records**, not take first
- **sprk_spendsnapshot** → pre-computed aggregation; alternate key = 5-field composite (matter/project + period type + period key + bucket key + visibility filter)
- **sprk_spendsignal** → threshold alerts; created by `SignalEvaluationService`

**Budget variance**: positive = under budget; negative = over budget.
**Velocity**: `(current month − prior month) / prior month × 100`. Null when prior = 0.

---

## Signal Thresholds

| Signal Type | Threshold | Severity |
|-------------|-----------|----------|
| `BudgetExceeded` | 100%+ utilization | Critical |
| `BudgetWarning` | 80%+ utilization (configurable) | Warning |
| `VelocitySpike` | 50%+ MoM increase (configurable) | Warning |

---

## VisualHost Architecture

Finance visualization uses denormalized fields on `sprk_matter` / `sprk_project` + native Dataverse charts rendered via the VisualHost PCF. Fields (`sprk_budget`, `sprk_currentspend`, `sprk_budgetvariance`, `sprk_budgetutilizationpct`, `sprk_velocitypct`, `sprk_lastfinanceupdatedate`) are updated by `SpendSnapshotGenerationJobHandler` after each snapshot run.

This replaced a custom PCF chart implementation. The tradeoff: update latency (denormalized fields are current at last job run) vs. real-time rendering.

---

## Caching

| Cache | Key | TTL | Invalidation |
|-------|-----|-----|-------------|
| Finance summary (Redis) | `matter:{id}:finance-summary` | 5 min | Explicit delete after SpendSnapshot job |
| Finance summary (Redis) | `project:{id}:finance-summary` | 5 min | Explicit delete after SpendSnapshot job |
| Lookup services (IMemoryCache) | By code field | 1 hour | Manual (`ClearCache`) |

---

## Related Documentation

| Document | Purpose |
|----------|---------|
| [sdap-bff-api-patterns.md](sdap-bff-api-patterns.md) | BFF patterns including job handler pattern |
| [sdap-auth-patterns.md](sdap-auth-patterns.md) | Auth patterns (app-only for background jobs) |
| [AI-ARCHITECTURE.md](AI-ARCHITECTURE.md) | Structured output platform capability |

---

*Last Updated: 2026-02-13*
