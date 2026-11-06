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
| Handler registration (fix round) | `InvoiceIndexingJobHandler` is registered UNCONDITIONALLY (FinanceModule flag-off rule). `SearchIndexClient` (registered only with `DocumentIntelligence:Enabled`) is an optional constructor dependency; `Ebilling:Enabled` is read by the handler (ADR-018). Flag off: completes, nothing done. No search client: completes with one warning. No embedding (AI off, throttled, model error): indexed without a vector. None dead-letters `NoHandler`. |
| ADR-032 ledger (fix round) | `GateCombinationConstructibilityTests.KnownDefects`: the `DEF-FIN InvoiceIndexingJobHandler <- SearchIndexClient` row is deleted. The ratchet failed in the two `DocIntel=true, Analysis=false` combinations because the defect no longer reproduces (the handler is constructible without a search client); the test's own maintenance rule says to delete a row when it is fixed. |
| Producers beyond the status move (fix round) | One shared best-effort enqueue, `InvoiceIndexingJobHandler.TryEnqueueAsync` (the lifecycle service now uses it too). `InvoiceLineDecisionService` after the totals recalculation (change `d{decisionId}`); `InvoiceAdjustmentService` after an add (`a{adjustmentId}`) and a removal (`r{lineId}`); `InvoiceCorrectionReevaluation` after the final write (`c{submission row version}`); `SubmissionOverrideService` after an override releases a hold (`o{decisionId}`). Every key differs per change; a failed enqueue is logged (ids only) and never fails the change. |
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

- **A failed enqueue is not retried.** After a lifecycle move, a line decision, an adjustment, a correction follow-through or an override, a failed enqueue is logged (ids only) and not retried; the index catches up at the next change of the invoice. The acceptance step is stricter (a Retry; the submission stays Processing). A correction whose earlier step failed enqueues only when the client repeats it and it reaches its final write.
- **The search endpoint takes only `matterId` and a query.** The firm, core record, timekeeper, code and status filters exist on the index (and are in the deployer's invariants) but no endpoint parameter reads them yet; a consumer that needs them adds the parameters to `InvoiceSearchService`.
- **Blocked invoices are indexed**, per the 049 plan (an internal invoice that is In Review and held, which an override or a return can still move). The owner may later read FR-43's "rejected submissions are not in the invoice index" as excluding them; the change is one line: drop `SubmissionEvaluationKind.Blocked` from the step-3 condition in `InvoiceSubmissionAfterSteps`.
- **The index is eventually consistent by design.** A job indexes the invoice as it is when the job runs, so two changes that queue close together converge on the later state; a duplicate-detection drop of a repeated key (same decision, same adjustment) is correct because the document is rebuilt from current rows.
- **Legacy `InvoiceExtractionJobHandler` still compiles** a fixed-key enqueue (`invoice-index-{invoiceId}`); unreachable, deleted by 050b.
- **Resolved in the fix round (no longer limits):** totals-only changes (line decisions, adjustments, corrections), an override's release, and the `NoHandler` dead letter with `DocumentIntelligence:Enabled` off.

## 5. Live steps this task needs (not done)

1. Task 153: `scripts/ai-search/Deploy-AllIndexes.ps1 -Environment dev -Indexes invoices-index` (after `-DryRun`), then `-VerifyOnly`.
2. `AzureAd:TenantId` must be set on the dev BFF (it already is for the search filter).
3. `DocumentIntelligence:Enabled=true` (so `SearchIndexClient` exists) on any environment that should actually index. Since the fix round the handler is registered either way: with it off, an eBilling invoice's messages complete with one warning and nothing is indexed.
4. NFR-03 (5 minutes) is measured in dev by 153.
