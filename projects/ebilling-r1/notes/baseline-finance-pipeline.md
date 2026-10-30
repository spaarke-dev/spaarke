# Baseline — existing Finance invoice pipeline and dev rollups (task 001, FR-01)

> Observed 2026-10-15 against Dataverse dev (`spaarkedev1`) and `spaarke-bff-dev`. Read-only: no dev record, setting, playbook or deployment was created, changed or deleted. Evidence is identifiers, counts, timings and file:line only (ADR-015).
> Code wins over docs (root CLAUDE.md §2). Doc disagreements are listed in section 6.

## 0. Outcome in one paragraph

The pipeline **cannot be run in dev as it stands**, so step 3 of the task (run one sample invoice) was not performed. Running it would require changing dev configuration, seed data and a model deployment, which the task's escalation trigger forbids (see section 7). Every step below is therefore classified from code, dev configuration and dev data. Findings that matter for later tasks: the extraction job writes nothing to Dataverse on success and creates no `sprk_billingevent` rows (050, 044); the classification job has no trigger in code (042); the indexing job indexes an empty placeholder record (152); stored rollups on the two dev matters checked disagree with a recompute on every field, and the counted-status set `{Reviewed}` is unreachable because nothing sets that status (015, ontology-r1).

## 1. Expected flow (from code)

| # | Stage | What the code does | Source |
|---|---|---|---|
| 1 | Trigger | Nothing in `src/` submits an `AttachmentClassification` job (only the handler's own `JobTypeName`, plus comments). The architecture doc names `EmailToDocumentJobHandler` as the trigger; that class does not exist in `src/`. | `AttachmentClassificationJobHandler.cs:38`; `EndpointMappingExtensions.cs:337` (comment); grep of `src/` |
| 2 | Classification gate | Handler registered only when `DocumentIntelligence:RecordMatchingEnabled` is true. Downloads the file, extracts text, `IInvoiceAnalysisService.ClassifyAttachmentAsync` (deployment `FinanceOptions.ClassificationDeploymentName`, default `gpt-4o-mini`, system prompt loaded from playbook **by name** `FinanceClassification`), entity matching, then writes `sprk_classification`, `sprk_classificationconfidence`, `sprk_invoicehintsjson`, matter suggestions and (for InvoiceCandidate/Unknown) `sprk_invoicereviewstatus` = ToReview on the **document**. Never creates an invoice or billing event. | `FinanceModule.cs:156-159`; `AttachmentClassificationJobHandler.cs:67-292, 481-533`; `InvoiceAnalysisService.cs:35, 63-115` |
| 3 | Review / confirm | `POST /api/finance/invoice-review/confirm` -> `InvoiceReviewService.ConfirmInvoiceAsync`: creates a team-owned `sprk_invoice` (extraction status NotRun), links the document (`sprk_document.sprk_invoice`), marks the document ConfirmedInvoice, enqueues an `InvoiceExtraction` job. | `FinanceEndpoints.cs:37`; `InvoiceReviewService.cs:365-534, 512, 844` |
| 4 | Extraction job | `InvoiceExtractionJobHandler`: loads invoice (placeholder), loads document, container check, download, text extraction, `ExtractInvoiceFactsAsync` (deployment default `gpt-4o`, prompt from playbook by name `FinanceExtraction`), builds variables, runs `FinancialCalculationToolHandler`, then resolves the playbook id from `Finance:InvoiceExtractionPlaybookId` (empty -> mark Failed, return Success) and calls `OutputOrchestrator.ApplyOutputMappingAsync`; on success enqueues `InvoiceIndexing`. | `InvoiceExtractionJobHandler.cs:82-393` (config check `:328-336`, orchestrator `:338-339`, enqueue `:357`) |
| 5 | Writes | Only via the orchestrator's playbook `outputMapping` (see section 3, step 4). The handler itself writes only failure status, to the wrong entity (#1087). No code path in the handler writes `sprk_billingevent`. | `InvoiceExtractionJobHandler.cs:18-23, 559-583`; `OutputOrchestratorService.cs:33-92, 317-346` |
| 6 | Indexing job | `InvoiceIndexingJobHandler`: loads invoice and document via placeholders, embeds (empty text -> no vector), merge-or-upload into `spaarke-invoices-index`. | `InvoiceIndexingJobHandler.cs:69-233, 259-328` |
| 7 | Rollup | `FinanceRollupService.RecalculateMatterAsync/RecalculateProjectAsync` sums `sprk_totalamount` over invoices whose `sprk_invoicestatus` is in `FinanceBaseValues.CountedInvoiceStatuses` (= `{Reviewed 100000001}`), budgets from `sprk_budget`, writes 9 columns to the parent. Callers: the two manual `POST /api/finance/{matters|projects}/{id}/recalculate` routes and `SpendSnapshotGenerationJobHandler`; no code enqueues that job type. | `FinanceRollupService.cs:115-218, 235-250`; `FinanceRollupEndpoints.cs:45, 67`; `FinanceBaseValues.cs:49-56`; `FinanceModule.cs:168-174`; grep `"SpendSnapshotGeneration"` finds only the handler constant |

## 2. Step table

| Step | Result | Evidence |
|---|---|---|
| Classification gate | **FAIL** (no trigger; cannot run) | No `AttachmentClassification` job is ever submitted by `src/` (section 1 row 1). Handler is registered in dev (`RecordMatchingEnabled` = true; App Insights startup log lists it among 13 handlers, latest `2026-10-11T15:12Z`). Dev: 0 `sprk_document` rows have `sprk_classification` or `sprk_invoicereviewstatus` set. The playbook `FinanceClassification` it needs does not exist in dev (step below). Issue #1920. |
| PB-013 extraction configuration | **FAIL** | Playbook row `1e657651-9308-f111-8407-7c1e520aa4df` (Finance Invoice Processing, code PB-013, `sprk_playbookid` same GUID) exists and is active (statecode 0). Its `sprk_configjson` is empty (no `outputMapping`). `spaarke-bff-dev` has **no** `Finance__InvoiceExtractionPlaybookId` setting (no setting name contains "financ", "invoice" or "playbook" apart from `CopilotAgent__EnablePlaybookInvocation`), so the handler marks the invoice Failed at `InvoiceExtractionJobHandler.cs:329-336`. `FinanceExtraction` and `FinanceClassification` playbook rows (the names `InvoiceAnalysisService.cs:35,41` loads) are absent in dev and have no seed in the repo. `FinanceOptions` default deployments are `gpt-4o` / `gpt-4o-mini`; the BFF's OpenAI account `spaarke-openai-dev` has `gpt-4o-mini` (model gpt-4.1-mini) but no `gpt-4o`. `Analysis__Enabled`, `DocumentIntelligence__Enabled`, `DocumentIntelligence__RecordMatchingEnabled` are all true. Issue #1918. |
| InvoiceExtractionJobHandler | **FAIL** (cannot run; and on success persists nothing) | Not run (section 7). Handler registered (startup log). App Insights, 30-day window (2026-09-15 to 2026-10-15): the only matches for extraction / classification / indexing / rollup message text are the "Found N job handlers" startup lines (791 lines; none is a processing line), so no finance job has run in that window. Static: `LoadInvoiceRecordAsync` is a placeholder (`:499-527`, #229); success path never sets `sprk_extractionstatus` = Extracted (`ExtractionStatusExtracted` unused, `:44`); failure-status writes go to `sprk_document` by invoice id (`:559-583`; `DataverseServiceClientImpl.cs:790-804`; #1087). Issue #1919. |
| Writes to `sprk_invoice` | **FAIL** | The only success-path writer is the orchestrator, and it is a no-op: `PlaybookService.GetPlaybookAsync` selects no `sprk_configjson` (`PlaybookService.cs:234`), so `ParseOutputMapping` returns null and the orchestrator logs "no outputMapping defined, skipping updates" and returns Success (`OutputOrchestratorService.cs:52-57, 317-322`). Dev data: of 10 `sprk_invoice` rows, 0 have `sprk_extractedjson`, none has an extraction status value. The seed mapping targets `sprk_aisummary`, which is not a column of `sprk_invoice` (it has `sprk_recordsummary`; `describe tables/sprk_invoice`). |
| Writes to `sprk_billingevent` | **NOT-PRESENT** | The extraction job does **not** write `sprk_billingevent` rows today. Evidence: no reference to billing events in `InvoiceExtractionJobHandler.cs` (header states "no BillingEvents", `:21`), the seed notes say "no BillingEvents or SpendSnapshots" (`playbooks.json` PB-013 notes), and the orchestrator path is dead (above). Dev holds exactly 1 `sprk_billingevent` (`5ea5ad2a-9e9c-f111-b8de-7ced8ddc4cc6`, name "Document Review", invoice `4c0f4d12-9e9c-f111-b8de-7ced8ddc4cc6`, created by a user account 2026-08-20T09:50:57, 41 s after that invoice was created), i.e. manual, not job-written. |
| InvoiceIndexingJobHandler | **FAIL** (cannot run; placeholder inputs) | Registered (startup log). Not run. `LoadInvoiceRecordAsync` returns an all-null record (`:259-297`, #229) and `LoadDocumentRecordAsync` returns `ExtractedText = null` (`:302-328`, #233), so it would upload a document with no content and no vector. Index `spaarke-invoices-index` exists on `spaarke-search-dev` (index list); document count NOT-VERIFIED: data-plane read returned HTTP 403 for the current identity (no RBAC change made). The doc names the index `spaarke-invoices-dev`; that index does not exist. |
| Matter / project finance rollups | **FAIL** (stored values disagree with recompute) | Section 4. |

## 3. Dev configuration observed (names and ids only)

- App settings on `spaarke-bff-dev` (running): `Analysis__Enabled`, `DocumentIntelligence__Enabled`, `DocumentIntelligence__RecordMatchingEnabled` = true; `ServiceBus__QueueName`, `ServiceBus__FullyQualifiedNamespace` present. **Absent:** any `Finance__*` setting.
- Dev playbook rows matching "financ" / "invoice" / code like "FIN": `1e657651-9308-f111-8407-7c1e520aa4df` (PB-013), `7d9a9209-31da-f011-8406-7ced8d1dc988` (Analyze Agreement Financial Terms, PB-001, unrelated).
- OpenAI deployments on `spaarke-openai-dev`: gpt-4o-mini (gpt-4.1-mini), text-embedding-3-small, text-embedding-3-large, gpt-5-reasoning (gpt-5), gpt-5.4-mini, gpt-5.6-luna. No `gpt-4o`.
- Dev data totals: `sprk_invoice` 10, `sprk_billingevent` 1, `sprk_budget` 3, documents with classification/review status 0, invoices with extracted JSON 0.

## 4. Rollup check (step 5, DI-69 / #1663)

Method: read-only. Stored matter columns read from `sprk_matter`; recompute by hand from `sprk_invoice` rows linked by `sprk_matter` and `sprk_budget` rows, using `FinanceBaseValues`: spend = sum of `sprk_totalamount` over invoices with `sprk_invoicestatus` in `CountedInvoiceStatuses` (= Reviewed only), budget = sum of `sprk_budget.sprk_totalbudget`, utilization = spend / budget * 100. The recalculate route was not called (it writes).

| Matter | Field | Stored | Recomputed | Match |
|---|---|---|---|---|
| `b68299c6-bafb-f011-8407-7c1e520aa4df` (modified 2026-10-07) | spend to date | 250000 | 0 (6 linked invoices, none Reviewed; amounts all null) | No |
| | invoice count | 5 | 0 counted (6 linked) | No |
| | total budget | 150000 | 150000 (1 budget row) | Yes |
| | remaining budget | 100000 | 150000 | No |
| | utilization % | 1.25 | 0 (and 166.67 if the stored spend were used) | No |
| | average invoice | 5000 | 0 | No |
| | monthly spend current | 10000 | not recomputed (no counted invoice) | No |
| `2444af6d-e1f2-f011-8406-7ced8d1dc988` (modified 2026-10-13) | spend to date | 25000 | 0 (2 linked invoices, none Reviewed; INV-002 carries 1000 but status null) | No |
| | invoice count | 2 | 0 counted (2 linked) | No |
| | total budget | 55000 | 1500 (1 budget row) | No |
| | remaining budget | null | 1500 | No |
| | utilization % | null | 0 | No |
| | average invoice | 1250 | 0 | No |

**Verdict: MISMATCH on both matters.** Causes, all outside this project's code:
1. The stored columns are plain columns whose only writers are the manual recalculate routes and the form subgrid script; the stored values are round numbers (hand-entered or from an old run), not derived (the 1.25 vs 166.67 inconsistency on the first matter is already filed as #1663; unchanged).
2. `CountedInvoiceStatuses = {Reviewed}` is unreachable: grep of `src/` finds no code that sets `sprk_invoicestatus` to Reviewed, and all 10 dev invoices are ToReview (3) or null (7). Any recalculation today therefore stores spend 0 and count 0. The code comment calls the status basis a pending owner decision (`FinanceBaseValues.cs:49-52`); task 015 changes it (D-41).
3. Nothing enqueues `SpendSnapshotGeneration`, so the job-driven rollup path never runs (`FinanceModule.cs:168-174`).

Per the escalation trigger the mismatch is in ontology-owned territory (`FinanceBaseValues.cs`); nothing was edited. Evidence added to #1663 (https://github.com/spaarke-dev/spaarke/issues/1663#issuecomment-6194036109), which is the notification to ontology-r1.

## 5. Inputs for later tasks

- **042** (classification baseline for the FR-46 before/after comparison): there are **no baseline numbers**. The legacy classifier has no trigger and no dev playbook, and 0 dev documents carry a classification. A before/after comparison against production behaviour is not possible; 042 must build its own labelled baseline (the test-data set) rather than compare to this path. Legacy model config for reference: `gpt-4o-mini` deployment name, confidence threshold default 0.5 (`FinanceOptions.cs:17,37`).
- **044 / 050** (what the legacy extraction writes): nothing on success; the orchestrator is a no-op; no billing events; failure status goes to the wrong table. Deleting the path (D-71) loses no working write behaviour. Keep `sprk_totalamount` and the matter/project lookup for `FinanceBaseValues` (D-71).
- **152** (indexing job state): handler is a placeholder that indexes an empty record; the index `spaarke-invoices-index` exists; document count unknown (403 on data plane).
- **015** (status basis): `CountedInvoiceStatuses` unreachable today; stored matter rollups are stale/hand-entered; see section 4.
- **Existing dev invoices** are all manual/test rows (names like "INV-005", "Invoice Wizard"); none came from the pipeline.

## 6. Doc vs code disagreements (`docs/architecture/finance-intelligence-architecture.md`, last reviewed 2026-04-05)

| Doc says | Code / dev shows |
|---|---|
| Classification has a "feature-flagged trigger from `EmailToDocumentJobHandler`" | No such class in `src/`; nothing submits the job (#1920) |
| Extraction extracts "header + line items" and the entity list says `sprk_billingevent` is "one per invoice line... set by handler code" | Handler writes no billing events and nothing on success (#1919) |
| Aggregation job runs after extraction; rollup "after each snapshot run" | No code enqueues `SpendSnapshotGeneration` (`FinanceModule.cs:168-174`) |
| Indexing embeds "invoice content into `spaarke-invoices-dev`" with chunk headers | Index is `spaarke-invoices-index`; handler indexes one placeholder record, no chunks |
| Prompts via `IPlaybookService`; `IOpenAiClient` | Prompts via `IInvoiceAi` facade by playbook name (`InvoiceAnalysisService.cs:27`) |

Doc correction is not made here; it belongs with the eBilling docs work.

## 7. Escalation (task `<escalation>` trigger 1)

Running the pipeline in dev needs at least: (a) a `Finance__InvoiceExtractionPlaybookId` app setting on `spaarke-bff-dev`; (b) `FinanceExtraction` and `FinanceClassification` playbook rows seeded (and, for the output mapping to be reachable, a BFF code change); (c) a `gpt-4o` deployment on `spaarke-openai-dev` or overriding `Finance__ExtractionDeploymentName`; (d) for classification, a trigger (none exists). Each is a change to dev configuration, seed data or a deployed playbook, so the run was stopped before any write and escalated to the owner. See the task report.

## 8. Defects

| ID | Defect | Disposition |
|---|---|---|
| D1 | Dev cannot run the legacy extraction path (no playbook-id setting, no FinanceExtraction/FinanceClassification rows, no gpt-4o deployment) | Filed #1918 (owner decision requested; not repaired, per the task) |
| D2 | Legacy extraction persists nothing on success; output mapping unreachable; placeholder invoice load; no Extracted status; no billing events | Filed #1919 (related #229, #1087); path deleted by 050 |
| D3 | `AttachmentClassification` never enqueued; architecture doc misdescribes the pipeline | Filed #1920 |
| D4 | Wrong-entity failure-status write (`UpdateDocumentFieldsAsync` with an invoice id) | Already filed: #1087 (open) |
| D5 | Indexing handler placeholders (all-null invoice, null text) | Already filed: #229, #233 (open); covered in #1919 text |
| D6 | Matter rollups stale / inconsistent; counted status unreachable; snapshot job never enqueued | Already filed: #1663; evidence comment added; basis change in task 015 |

No `src/**` file was changed by this task; no dev record was created, so there is nothing to clean up (step 7: none).

> **Owner decision 2026-10-15 (D-80):** baseline accepted without a live run (option A). No dev repair.
