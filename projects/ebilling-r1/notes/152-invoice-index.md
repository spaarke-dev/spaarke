# 152: the invoice index is built from structured records

Rigor STANDARD (the POML's; the gate ran code-review and adr-check anyway, per the dispatch brief). Branch `task/ebill-152-invoice-index`, from `origin/work/ebilling-r1` @ `f39d7f127b`. FR-43, D-44, NFR-03.

## 1. What changed

| Area | Change |
|---|---|
| `InvoiceIndexingJobHandler` | Reads `sprk_invoice` and its `sprk_billingevent` lines through `IGenericEntityService`; no document, no `TextExtractorService`, no submission. Payload `{InvoiceId, Change}`; a legacy `DocumentId` is accepted and ignored. `MergeOrUpload` keyed by the invoice id. `BuildJob(invoiceId, change, correlationId)` is the one place the job and its idempotency key `invoice-index-{invoiceId:N}-{change}` are made. |
| `InvoiceIndexDocument` (new, internal) | The pure mapping from the two row sets to the document and its content text; the two Dataverse queries. |
| `spaarke-invoices-index.json` | 14 additive fields (below). No existing field's attributes changed. |
| `Deploy-AllIndexes.ps1` | `invoices-index` `RequiredFilterableFields` extended with `vendorOrgId`, `coreRecordType`, `coreRecordId`, `timekeeperIds`, `taskCodes`, `activityCodes`, `expenseCodes`, `statusCode`. |
| `InvoiceLifecycleService` | After a successful move, queues the re-index (best effort). Change key = the row version the move replaced. |
| `InvoiceSubmissionAfterSteps` | Step 3 (was "no indexing", task 094) queues the acceptance's index job for Accepted, AcceptedUncoded and Blocked; not for an automatic rejection. A queue fault is a Retry (the submission stays Processing). |
| `InvoiceSearchService` | `documentId` is optional (`Guid.Empty` when absent). The old `Guid.Parse(doc.DocumentId)` would have thrown on every structured document without one. |
| `AI-SEARCH-INDEX-CATALOG.md` | Row 6 and the restoration-matrix row. |

New index fields: `coreRecordType`, `coreRecordId`, `coreRecordName`, `timekeeperIds`, `timekeeperNames`, `taskCodes`, `activityCodes`, `expenseCodes`, `trackingNumber`, `billingPeriodStart`, `billingPeriodEnd`, `approvedTotal`, `statusCode`, `status`. Reused: the firm is `vendorOrgId` / `vendorName`; the billed total is `totalAmount`; `matterId` / `projectId` stay filled for those two core types (the search endpoint filters on `matterId`).

## 2. Decisions and deviations

1. **Document granularity: one document per invoice (id = invoice id), not per line.** The constraint's default. Lines contribute collections (codes, timekeepers) and narrative text to the one document. Per-line documents were not chosen: the search endpoint already groups hits by invoice, and a per-line shape would multiply embeddings for no filter the collections do not give.
2. **No index recreate.** Every change is a new field, so the deployer's PUT updates the index in place. The dev index holds no documents (catalog restoration matrix: schema only), so the escalation trigger "recreate with documents others rely on" does not fire. Legacy documents keyed `{invoice}_{document}` (none expected) would stay beside the new ones; the search groups by `invoiceId`, and they carry the old placeholder tenant so the tenant filter hides them.
3. **Tenant.** The old handler wrote `tenantId = "default"` (a placeholder), while `InvoiceSearchService` filters on `AzureAd:TenantId`: nothing it wrote could be found. The handler now writes `AzureAd:TenantId` and poisons the job when it is not configured (ADR-014: never index without it).
4. **Out-of-order jobs converge.** A job names an invoice and a change; the handler reads the current rows. Two jobs that run in either order leave the latest state.
5. **Vector kept on a failed embedding.** The document omits `contentVector` when there is no embedding (AI off, throttled), so a Merge keeps the vector an earlier run wrote. The old code wrote a null vector.
6. **Blocked invoices are indexed.** A blocked internal submission is a Rejected submission whose invoice is In Review, held (049 map section 2). 049's plan queued it for the index, and it is an internal invoice that can still be overridden or returned, so it is indexed and its Return re-indexes it through the lifecycle hook. FR-43's "rejected submissions are not in the invoice index" is met for what the product calls a rejected submission: an automatic rejection (SPA), whose invoice is created Rejected and is never queued. If the owner reads the rule as covering Blocked too, drop `SubmissionEvaluationKind.Blocked` from the step-3 condition.
7. **Approval is covered by the lifecycle hook.** Approve ends in `IInvoiceLifecycleService.TransitionAsync(Approved)`, which queues the re-index. `InvoiceApprovalService` is not edited (it belongs to another branch's scope). Override (105) accepts a held submission without a status move, so it does not re-index today; see section 4.
8. **Task 094's reflection guard replaced.** `InvoiceSubmissionAfterStepsTests.NoIndexingJob_IsEnqueued` asserted that no constructor takes `JobSubmissionService`, because the invoice index was document-keyed and a Submit Invoice copy had to stay out of it (DEF-047, owner K). The index is now keyed to the invoice and the job carries no document, so the rule holds by the job's shape. The replacement test (`AcceptanceIndexJob_CarriesTheInvoiceOnly`) pins the payload to `{InvoiceId, Change}`, and the handler tests prove only the invoice and line tables are read.
9. **Escalation trigger 2 does not fire.** 050a already redirected the confirm route; nothing enqueues `InvoiceExtraction` any more (no producer in `src`). The legacy handler's own `InvoiceIndexing` enqueue is therefore unreachable, and a payload from it would still index the invoice correctly. 050b deletes the handler.

## 3. Verification

Unit tests: `InvoiceIndexingJobHandlerTests` (document shape, core record types, codes, tenant, re-delivery, status change, keys, payloads, AI paths, search outcomes, document vs index JSON, deployer invariants, search binding), `InvoiceLifecycleIndexEnqueueTests`, and 7 new cases in `InvoiceSubmissionAfterStepsTests`. Each behaviour was proven to fail against a seeded mutation (constant change key, indexing an automatic rejection, a default tenant, a null vector key, the old `Guid.Parse`). `Deploy-AllIndexes.ps1 -Environment dev -Indexes invoices-index -DryRun` prints the plan with the extended invariants; the invariants were also checked offline against the JSON (every required field present and filterable, vector 3072-dim). No live call was made.

## 4. Known limits and found-not-fixed

- **Totals-only changes do not re-index.** Line decisions, adjustments and corrections change `sprk_approvedtotal` and the line text without a status move. The task names status and approval changes only; the next move re-indexes. A producer in `InvoiceLineDecisionService` / `InvoiceAdjustmentService` would close this (not edited here).
- **Override (105) does not re-index** a held invoice that it releases (no status move). The document is already indexed (Blocked is indexed at acceptance); only the "held" notion is not in it, so there is no wrong content, only none to update.
- **A failed enqueue after a move** is logged and not retried; the index catches up at the next change. The acceptance step is stricter (Retry).
- **Handler registration is still gated on `DocumentIntelligence:Enabled`** (`SearchIndexClient`; ADR-032 DEF-FIN). With the gate off a queued `InvoiceIndexing` message is dead-lettered `NoHandler`, now on every lifecycle move rather than only after extraction. Task 153 must run with the gate on.
- **Legacy `InvoiceExtractionJobHandler` still compiles** a fixed-key enqueue (`invoice-index-{invoiceId}`); unreachable, deleted by 050b.

## 5. Live steps this task needs (not done)

1. Task 153: `scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -Indexes invoices-index` (after `-DryRun`), then `-VerifyOnly`.
2. `AzureAd:TenantId` must be set on the dev BFF (it already is for the search filter).
3. `DocumentIntelligence:Enabled=true` on any environment that runs eBilling, so the handler is registered.
4. NFR-03 (5 minutes) is measured in dev by 153.
