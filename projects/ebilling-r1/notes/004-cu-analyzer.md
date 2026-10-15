# Task 004 - Content Understanding analyzer definition v1 and deploy script

> Executed 2026-10-15 by task-execute (STANDARD rigor, prescriptive steps), dev only, GA API `2025-11-01` throughout (D-53).
> Resource: `spaarke-docintel-invoices-dev` (westus). Auth: Entra bearer token from `az account get-access-token` (operator ralph.schroeder@spaarke.com). No account secret was listed, read or sent. No Key Vault change.

## 0. Deviation to reconcile (read first)

**The service rejects a hyphen in an analyzer id.** `PUT .../analyzers/sprk-probe-004?api-version=2025-11-01` returned HTTP 400 `InvalidAnalyzerId: The 'analyzerId' cannot contain '-'`. Allowed: letters, digits, `.` and `_` (the `prebuilt-` prefix is the service's own). The deployed analyzer is therefore **`sprk_invoice_v1`**, not `sprk-invoice-v1`. The definition file keeps the name `sprk-invoice-v1.json` (the POML output path); the id inside it is `sprk_invoice_v1`. Next version: `sprk-invoice-v2.json` with `"analyzerId": "sprk_invoice_v2"`.

Places that still say `sprk-invoice-vN` and need the underscore form (not edited here; the main session decides, D-49 is an owner decision):
- `src/server/api/Sprk.Bff.Api/appsettings.template.json` line 462, `ContentUnderstanding:AnalyzerId` (written by task 003). **As it stands, the BFF would GET a non-existent analyzer.**
- `projects/ebilling-r1/spec.md` (4), `README.md`, `plan.md`, `CLAUDE.md` (section 3, D-46/D-49 line), `notes/decisions-log.md`, `notes/003-model-deployments.md`, `notes/drafts/task-decomposition.md`.
- Task POMLs 003, 005, 027, 043, 101, 162.

## 1. Step 1 - convention check

Glob of `infrastructure/` and `scripts/` before the task: no `content-understanding` folder. Closest convention: `infrastructure/ai-search/*.json` + `scripts/ai-search/Deploy-AllIndexes.ps1` (definition-as-JSON, PUT, `-WhatIf`, exit codes). New files: `infrastructure/content-understanding/sprk-invoice-v1.json`, `scripts/content-understanding/Deploy-Analyzers.ps1`.

## 2. Step 2 - prebuilt-invoice reference

`GET analyzers/prebuilt-invoice?api-version=2025-11-01` HTTP 200, saved at `notes/004/prebuilt-invoice.reference.json`. Facts used:
- `baseAnalyzerId: prebuilt-document`; `models: {completion: prebuilt-analyzer-completion, embedding: prebuilt-analyzer-embedding}`; `processingLocation: geography`.
- `config`: returnDetails, enableOcr, enableLayout true; `tableFormat: html`; `chartFormat: chartjs`; `annotationFormat: markdown`; **`estimateFieldSourceAndConfidence: true`** (the analyzer-wide source-and-confidence switch on GA; the per-field form is `estimateSourceAndConfidence`, per the research note section 1.5).
- Prebuilt `LineItems` has Date, Description, ProductCode, Quantity, QuantityUnit, UnitPrice, TotalAmount, TaxAmount, TaxRate (no timekeeper, hours, rate or narrative semantics), as the research note states.
- Server-added properties on any analyzer: `createdAt`, `lastModifiedAt`, `status`, `warnings`, `supportedModels`. The service also defaults `config.chartFormat` and `config.annotationFormat`, so the file states them explicitly.
- The resource held only prebuilt analyzers before this task (listing checked); nothing of ours existed.

## 3. Step 3 - definition `sprk_invoice_v1`

`config.estimateFieldSourceAndConfidence: true` (property name confirmed from the GET above and accepted by PUT; the research note's second spelling is the per-field name). `models.completion = gpt-5.2`, `models.embedding = text-embedding-3-large` (both are keys in the resource defaults set by task 003). `processingLocation: geography`, `tags {project, schemaVersion}`.

| Group | Fields (method) |
|---|---|
| Header | FirmName, FirmInvoiceNumber, InvoiceDate, ClientName, MatterName, FirmMatterReference, PurchaseOrderAsQuoted, FeesTotal, ExpensesTotal, AdjustmentsTotal, TotalDue (extract); BillingPeriodStart, BillingPeriodEnd, Currency (generate: derived from a period statement or a symbol) |
| TimeEntries[] | Date, TimekeeperAsBilled, TitleAsBilled, Hours, Rate, Amount, Narrative, TaskCodeAsBilled, ActivityCodeAsBilled (extract) |
| Expenses[] | Date, Description, Quantity, UnitCost, Amount, ExpenseCodeAsBilled (extract) |
| TimekeeperSummary[] | Name, Title, Hours, Rate, Amount (extract) |

Depth: array > object > property = 3. Alias descriptions are on the three tables and the header totals ("Professional Services", "Fees", "Time Detail", "Disbursements", "Costs", and the matter / invoice-number label variants). Every description is under 1,024 characters. Line type is carried by which array a row sits in. Tax, prior balance and the UTBMS activity-code classify experiment are not in v1 (005 decides the classify experiment; tax and prior balance are fast followers).

## 4. Step 4/5 - script and live runs

`Deploy-Analyzers.ps1` (pwsh 7): parameters `-Endpoint` (or `CONTENT_UNDERSTANDING_ENDPOINT`), `-AnalyzerFile[]`, `-ApiVersion` (pattern-validated to a plain date, default 2025-11-01), `-Replace`, `-TimeoutSeconds`, native `-WhatIf`. Plan pass reads every analyzer first and refuses before any write; then create / replace with operation polling; then a post-write GET that must equal the file. Operation-Location host is checked against the endpoint before the token is sent. Never deletes; refuses ids in the `prebuilt` namespace. Exit codes: 0 ok / no change; 2 bad args; 3 bad file; 4 no token; 5 differs without -Replace; 6 service or operation failure; 7 post-write difference.

Live runs (all `-Endpoint https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com`):

| Run | Result |
|---|---|
| `-WhatIf` | `sprk_invoice_v1: create`, `WOULD CREATE (what-if, nothing written)`, exit 0 |
| Run 1 | `CREATED and verified (deployed definition equals the file)`, exit 0. createdAt = lastModifiedAt = 2026-10-15T14:14:47Z |
| Run 2 | `NO CHANGE (deployed definition equals the file; no PUT performed)`, exit 0 |
| Run 3 (after later script edits) | same `NO CHANGE`, exit 0. lastModifiedAt unchanged across all runs, which confirms no write |

Refusal behaviour (criterion 3), against the **real** analyzer using a scratch file in the session scratchpad (description changed, FirmName description changed, Currency field removed): exit 5, "Nothing was written", 5 differing properties listed, deployed `lastModifiedAt` unchanged afterwards.

Replace path (`allowReplace=true`), tested on a scratch analyzer, not the real one: created `sprk_probe_004b` from the file (CREATED, verified); changed copy without `-Replace` -> exit 5; with `-Replace` -> `REPLACED and verified`; run again -> `NO CHANGE`.

Argument guards tested: hyphenated id -> exit 2; `prebuilt*` id -> exit 2; `-ApiVersion 2026-06-01-x` -> refused by parameter validation.

### Live Azure changes (complete list)

| Change | Detail |
|---|---|
| Analyzer created (kept) | `sprk_invoice_v1` on spaarke-docintel-invoices-dev, GA 2025-11-01, by `Deploy-Analyzers.ps1` run 1 |
| Analyzer created and **deleted** | `sprk_probe_004` (first probe, to learn the id rule and the server-added properties), created by this task, DELETE HTTP 204, GET afterwards 404 |
| Analyzer created and **deleted** | `sprk_probe_004b` (replace-path test), created by this task, DELETE HTTP 204 |

Delete statement (task escalation trigger 2): the two deletes are of analyzers this task created minutes earlier; no analyzer this task did not create was touched, and `sprk_invoice_v1` was never deleted or replaced. The analyze calls below read only.

## 5. Step 6 - analyze smoke test

Sample: **synthetic** evaluation-set invoices from task 002 (`notes/evaluation-set/local/synthetic/B-0001.pdf`, `B-0004.pdf`; synthetic, no third-party content, no PII). Call: `POST analyzers/sprk_invoice_v1:analyzeBinary?api-version=2025-11-01` (application/pdf), poll the operation. Output below is names, counts and confidence only; no document text.

| | B-0001 (2 pages, scan noise) | B-0004 (2 pages) |
|---|---|---|
| Status / warnings | Succeeded / none | Succeeded / none |
| Latency (POST to Succeeded, 2 s polling) | 27.0 s | 18.8 s |
| Header fields returned | 14 of 14, every one with `confidence` and `source` | 17 top-level fields returned; 12 header values compared with ground truth |
| TimeEntries rows | 16 | 9 |
| Expenses rows | 6 | 0 (invoice has none) |
| TimekeeperSummary rows | 7 | 5 |
| Confidence over all returned leaf values | min 0.58, max 0.99, mean 0.82 (229 values) | min 0.54, max 0.99, mean 0.81 (120 values) |

Per-field confidence ranges on B-0001: header 0.90-0.99; TimeEntries Date 0.76-0.88, TimekeeperAsBilled 0.78-0.79, Hours 0.70-0.88, Rate 0.79-0.89, Amount 0.73-0.85, Narrative 0.64-0.86, TaskCodeAsBilled 0.82-0.93; Expenses Description 0.66-0.74, Amount 0.73-0.80, ExpenseCodeAsBilled 0.89-0.95; TimekeeperSummary Name 0.58-0.68, Hours 0.65-0.76, Amount 0.79-0.86. Table-row subfields are the lowest and need threshold calibration in 005 / 161.

Source and confidence per subfield: **every returned value carries `confidence`, `source` (`D(page,x1,y1,...,x4,y4)`, normalised inches or 0-1 per the service) and `spans`**, including each array-item subfield. A subfield with no value (an empty title or activity code the invoice does not show) returns a `confidence` but no value, `source` or `spans`. Nothing needed from the preview API. `result.usage` was null on GA.

Comparison with the evaluation-set ground truth (spot check, not the spike): B-0004: header 12 of 12 equal, 9 of 9 time entries with hours and amount equal, 5 summary rows (count equal, values not compared), expenses 0 (the one non-fee ground-truth line is an adjustment, which is in `AdjustmentsTotal`). B-0001: all 16 fee lines equal, but 6 of 12 header values and the expense rows differ from the ground truth. The extraction is internally consistent with the PDF (FeesTotal 11890.00 + sum of the 6 extracted expense amounts 2569.00 + AdjustmentsTotal -594.50 = TotalDue 13864.50), so the **B-0001 PDF (written 10:15:09) and ground-truth JSON (rewritten 10:15:30) disagree** on invoice number, date, matter reference, PO, expenses and total due. B-0004's PDF is newer than its JSON and agrees. Most likely a stale PDF against regenerated ground truth while task 002 was running; reported to the main session for task 002 to confirm (not filed as an issue because 002 was in flight and may already have regenerated it). 005 should not score B-0001 until 002 confirms PDF and ground truth agree.

## 6. Escalation triggers

- Trigger 1 (GA cannot express an FR-21 field): **not fired.** Every FR-21 header and line field is expressible and returned with confidence and source on GA 2025-11-01. No gap recorded for 005 on field expressibility.
- Trigger 2 (replace needs delete-then-create): **not fired.** The service replaces in place (`allowReplace=true`, tested on a scratch analyzer). The only deletes were of this task's own probes (section 4).

## 7. Notes for later tasks

- 043: field names are stable now; a rename after 005 is a v2. The BFF analyzer id value is `sprk_invoice_v1` (section 0). Grant needed for analyze as the BFF identity is the Reader role from 003 (not exercised here, since this task ran as the operator).
- 005: the bundled-PDF (`contentCategories`) and the activity-code classify experiments are not in v1. 25+ page and high-row-count behaviour is untested here (largest sample tested is 2 pages).
- 161 / 162: `Narrative`, `TimekeeperSummary.Name` and expense `Description` are the lowest-confidence fields.
- The pipeline needs the deploying identity to hold Cognitive Services Content Understanding Contributor (create / replace); Reader is not enough.

## 8. Rollback

`DELETE https://spaarke-docintel-invoices-dev.cognitiveservices.azure.com/contentunderstanding/analyzers/sprk_invoice_v1?api-version=2025-11-01` with an Entra token (operator-run; not done here).

rework: none task 004
