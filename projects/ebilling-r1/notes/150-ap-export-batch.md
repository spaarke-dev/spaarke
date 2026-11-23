# Task 150: AP export batch

Branch `task/ebill-150-ap-export`, from `work/ebilling-r1` at `b8912e5553`. Rigor FULL (bff-api, authorization, data mutation). Model: Sonnet @ high.

## What was built

| Component | File | What it does |
|---|---|---|
| `ApExportService` | `Services/Finance/ApExportService.cs` | Create a batch (claim, generate, send, complete), list candidates, read a batch. Owns the compensation path. |
| `ApExportAuthorization` | `Services/Finance/ApExportAuthorization.cs` | THE seam: `AuthorizeExportAsync` (and `FilterExportableAsync` for the list, `RouteTargets` for the route filter). See "Authorization". |
| Mapping and writers | `Services/Finance/ApExportMapping.cs` | Mapping model, D-56 default, settings parser, RFC 4180 CSV writer, JSON writer (with `lines[]`). |
| Endpoints | `Api/Finance/ApExportEndpoints.cs` | Three routes (below). Step 0 on the group, the route filter, then `RequireEbillingEnabled`. |
| File withdrawal | `Services/Finance/Files/StoredFileService.cs` (+18 lines) | `internal WithdrawExportFileAsync`: removes a file of a failed batch through 090's own undo helpers (row Removed, blob deleted). It loads no `sprk_storedfile` string itself. |
| DI and routes | `FinanceModule.cs` (2 registrations), `EndpointMappingExtensions.cs` (1 line) | |
| ArchTests | `RouteAuthorizationGuardTests.Ledger.cs`, plus the allow-lists named under "Gate" | Endpoint file count 148 to 149 (written on the branch; reconciled at merge), a `RouteLevelGate` ledger entry. |

## Routes (all under `/api/finance`)

| Route | Purpose | Answers |
|---|---|---|
| `GET /export-batches/candidates` | Approved invoices with no batch that the caller may export, oldest invoice date first (at most 500 examined; `truncated` says so). | 200; 403 without Create on the batch table; 503 `ebilling.export.authorization_unavailable` / `unavailable`; 503 `ebilling.disabled` |
| `POST /export-batches` | Body `{ invoiceIds (1..200, distinct), formats? ("csv", "json"; both when omitted) }`. | 201 + `Location`; 400 `invalid_request`; 400 `selection_refused` (`invoices[]` with a reason each: `invoice_not_approved`, `invoice_already_exported`, `invoice_not_found`; nothing claimed); 403 `not_permitted` (names no invoice) / `caller_unresolved`; 409 `invoice_conflict` (nothing claimed); 422 `mapping_invalid` / `file_too_large`; 500 `transition_incomplete` / `compensation_incomplete` / `completion_incomplete`; 503 `file_failed` / `transition_failed` / `unavailable` / `authorization_unavailable` |
| `GET /export-batches/{batchId}` | Status, count, total, formats, the servable files (download through the 090 route), invoice ids. | 200; uniform 404 without Read on the batch |

Every code is `ebilling.export.*`. A failure that undid itself says "Nothing was exported."

## Authorization (EB-D-178, owner decision 2026-10-30)

The coordinator relayed the owner's choice of option 3 for the POML escalation "finance role is not defined in code":

- The caller needs **Create on `sprk_billingexport`** (the table privilege `prvCreatesprk_billingexport`, asked of Dataverse as the caller, on-behalf-of, uncached) **and `ebilling.invoice.decide` (Write) on EVERY invoice of the batch** (the right approval uses), **and Read on every invoice** (D-85, added by the review fixes: the files carry the invoice's values).
- If anything fails, the **whole batch is refused before anything is read or written**, and the answer names no invoice (`ApExportAuthorizationOutcome` carries no id; the 403 body has the stable code only).
- A rights read that cannot be answered (a faulted lookup, a thrown check, an OBO failure) is **Undetermined: 503, never an allow** (fail closed).
- No schema change. The Core User role already holds the Create privilege (a test pins the privilege name against `Spaarke Core User.xml`).

**One seam, on purpose.** The whole "may this caller export these invoices" decision is `ApExportAuthorization`. The route filter (`RouteTargets`) and the service (`AuthorizeExportAsync`) ask only that class. A future dedicated export right (the owner: "leave open the option that we move this to a more specific role in the future"), built on the D-76 override-right pattern (`EbillingOverridePrivilege`, a carrier table, `CallerRecordAccessProbe.CallerHoldsPrivilegeAsync`), replaces the privilege constants and the body of `AuthorizeExportAsync` and changes nothing else: not the endpoints, not the service, not the export flow. The class remarks cite EB-D-178.

**Decision for task 151 (the export UI).** Show the Export action to a user who can export under the rule above. Use the candidates route as the list (it already hides every invoice the caller may not export, and returns nothing without Create on the batch table); treat a 403 `ebilling.export.not_permitted` as "you may not export this selection" with no invoice named, and 503 `authorization_unavailable` as "try again". When the dedicated right arrives, 151 changes nothing: the routes and codes stay.

## Behaviour (order; nothing is written before the claim)

1. Validate the request (1 to 200 distinct ids, formats).
2. `AuthorizeExportAsync` (EB-D-178), before any invoice is read.
3. Read the mapping from settings (blank is the D-56 default; an unknown source, format, duplicate name or wrong version is 422 before any claim) and check the file store is configured.
4. Read the selection: every invoice Approved with an empty batch lookup, else 400 with a reason per invoice.
5. Resolve the owner (the acting user's team, D-90) and create the batch, status Generating, with the mapping snapshot (the settings JSON verbatim; the canonical default JSON when blank).
6. **Claim** in invoice-id order, each `UpdateIfRowVersionMatchesAsync` on the version read in step 4. A `DBConcurrencyException` is 409 and releases this batch's claims. Ordered claims make exactly one of two overlapping batches win.
7. Re-check every claimed invoice is still Approved and still claimed by this batch (a status change does not touch the batch lookup).
8. Read the values once, write the CSV and the JSON as Stored Files through 090's `CreateForAppAsync` (purpose Export file, parent the batch, Firm-visible No, `requestedBy` = the acting user's systemuser: the 194b ownership rule, otherwise the write fails closed). No `sprk_document` is created; `sprk_csvdocument` and `sprk_jsondocument` stay unset (see "For the owner").
9. Move each invoice to Sent to AP through `IInvoiceLifecycleService`.
10. Mark the batch Completed with the count and the total (one retry).

CSV: one row per invoice in firm-invoice-number order (ordinal), header from the mapping, RFC 4180 (CRLF, no BOM, quoting), ISO dates, invariant decimals, a text value starting with `=`, `+`, `-`, `@`, tab or CR prefixed with `'` (CSV injection; numbers and dates are not). JSON: `{ batchId, invoiceCount, invoices: [ { mapped columns, lines: [ { mapped line columns } ] } ] }`, dates as ISO strings, amounts as numbers, an absent value as `null`. Same input, same bytes.

## Compensation design (a Failed batch blocks nothing)

| Where it stops | What is undone | Answer |
|---|---|---|
| Steps 6 to 8 (conflict, status changed, value read, file write, file too large) | Files already written are withdrawn through 090 (row Removed, blob deleted); every claim is released (re-read, If-Match, two attempts); the batch is marked Failed with the code as the reason | 409 / 422 / 503, "Nothing was exported" |
| Step 9, first invoice | Same as above. Nothing was sent. | 503 `transition_failed` |
| Step 9, after at least one invoice moved | The lifecycle has no way back from Sent to AP. Files are withdrawn (so no payment file exists for the Failed batch), the claims of the invoices NOT sent are released, the sent invoices keep their claim on the Failed batch (so they can never be exported again) | 500 `transition_incomplete`, naming the batch for an operator |
| Step 10 (cannot mark Completed) | Nothing: every invoice is sent and the files exist | 500 `completion_incomplete`; the batch stays Generating for an operator |
| A compensation step itself fails | The other steps still run; the failure is logged | 500 `compensation_incomplete` |

Because Sent to AP is the one irreversible step, it is done last, after every file exists, and it ignores the request's cancellation token (a client that disconnects mid-way must not split a batch). A claim is recorded as attempted BEFORE its write, so a write that committed and then faulted is still released; a move that committed and then faulted is counted as sent (status re-read; unknown counts as sent) and keeps its claim. The batch read lists files only for a Completed batch.

## Known limits

- **Mid-sequence transition fault** (step 9 after the first invoice): the sent invoices are paid-out-able but their batch is Failed with no files. A narrow window (a fault or a concurrent change between the step 7 re-check and step 9); an operator resolves it. The Failed reason says how many were sent.
- **Process crash between steps 5 and 10** leaves a Generating batch holding claims. Nothing sweeps it in R1; the claim is the double-payment guard, so it fails safe (the invoices cannot be exported again until an operator clears the batch). No sweeper job is added (CLAUDE.md section 11: no concrete failing behaviour in R1 that justifies a new scheduled job).
- **Candidates cap**: the list examines the 500 oldest Approved, un-batched invoices; if the caller can export none of those, newer exportable ones are not shown (`truncated` is true). The POST path has no such cap.
- **Secure invoices (D-85).** The batch is outside the secure family: a secure invoice in a batch is visible, through the batch and its files, to anyone who can see the batch. The export rule above (decide on every invoice) limits who creates one, not who reads it. Raise in the PR description for the owner.
- **The firm file route does not exist yet (093).** The export files carry Firm-visible No and no source document, which the schema check enforces for every purpose except the invoice PDF; 093's route must filter on Firm-visible. 153 verifies the file is absent from search (not indexed).

## Empty D-56 columns (a column with no source is written empty, never omitted)

Always empty in R1 (no schema field): `VendorApId` (`firm.apVendorId`), `VendorTaxId` (`firm.taxId`), `FirmClientMatterNumber` (`record.firmClientMatterNumber`), `PONumber` (`invoice.poNumber`), `DocumentUrl` (`invoice.documentUrl`). Empty when the invoice has no value: `DueDate`, `ApprovedBy` / `ApprovedOn` (no approval decided), line `activityCode` / `expenseCode` / `taskCode` depending on the line type. `LawDepartmentMatterNumber` is the core record number (there is no separate field). The owner decides whether to add a source for the five.

## For the owner / for 153

- `sprk_billingexport.sprk_csvdocument` and `sprk_jsondocument` (023) still exist and are **left unset**; no `sprk_document` is written to fill them (POML escalation). The files are found through the Stored File rows (parent the batch). The owner can retire the two lookups.
- No App Service setting is needed. **Prerequisite (replaces "no schema change; nothing to deploy"):** `scripts/ebilling/Deploy-EbillingInvoiceTotalsRange.ps1` must run on an environment before a batch containing credit memos can export there. Until it has, `sprk_billingexport.sprk_totalamount` has minimum 0 and a batch whose total is below 0 is refused up front (400 `ebilling.export.batch_total_out_of_range`, nothing claimed). The review fixes did NOT run the script. After it has run on an environment, relax `ApExportService.BatchTotalMinimum` (the one place the minimum is spelled).
- The export file bytes are limited by 090's checked size conversion (`sprk_sizebytes` is a whole number, max 2,147,483,647): an oversized file is 422 `file_too_large` before any write. The 2 GiB boundary itself is exercised in 090's `StoredFileServiceTests` (the uac-r2 pointer); a 2 GiB buffer is not allocated here.
- **DEF-049 assessed: 150 does not trigger it.** The issue says `SubmissionCommitGate` step 6 and its undo read "the invoice's files" by the invoice. An export file's parent is the **batch**, never the invoice (a test pins that the row carries no `sprk_invoice` lookup), so `ReadRowsForInvoiceAsync` cannot see it and the gate neither skips on it nor deletes it. O-14 (files added to an invoice) is what still triggers DEF-049; the precondition named for 150 is met by construction.
- Reads are bounded: an invoice's lines are read as one page of 5,000 rows (Dataverse caps a page and TopCount at 5,000; see "Review fixes") and an invoice with more than that fails the batch (clean compensation) rather than silently dropping lines; the batch is capped at 200 invoices, so no query relies on an unbounded page.

## Tests

| File | Covers |
|---|---|
| `tests/unit/Sprk.Bff.Api.Tests/Services/Finance/ApExportMappingTests.cs` | Default columns and order, custom mapping changes output, empty source columns, RFC 4180, CSV injection, determinism, JSON `lines[]`, invalid mappings |
| `tests/integration/data-mutation/Ebilling/ApExportBatchTests.cs` | Happy path (files, snapshot, count, total, Completed, Sent to AP, no `sprk_document`, owner), refused statuses, concurrent batches (barrier-driven, 15 rounds x 2), rival claim, status change after claim, upload / second-file / transition faults, mapping change affects only the next batch, invalid mapping, store not configured, candidates, read |
| `tests/integration/auth/Finance/ApExportAuthorizationTests.cs` | The EB-D-178 rule on the seam and the routes: authorized, no Create, missing decide on one of several (whole batch refused, nothing written, no invoice named), Read is not decide, rights fault, privilege fault, no token; 401, 403, step 0, `ebilling.disabled`, 201, 400 reasons, batch read 404 / 200, candidates; the privilege pinned against the Core User role |
| `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/ApExportFileDownloadTests.cs` | Export files are internal-only on 090's download route: flagged-external refused before any read; Read on the batch downloads; no Read is the uniform 404 |

### Seeding proof (each mutation reverted, tests red, restored; the restored files diffed identical)

| Mutation | Red |
|---|---|
| Per-invoice decide check disabled in `AuthorizeExportAsync` | 4: `MissingDecideOnOneInvoice_RefusesTheWholeBatch_NothingWritten`, `ReadAloneIsNotTheDecideRight`, `RightOnAnotherInvoice_IsNotRightOnThisOne`, route `MissingDecide_Is403_NamingNoInvoice` |
| Create-privilege check disabled in `AuthorizeExportAsync` | 3: `NoCreatePrivilege_Is_Refused_WithoutAskingForInvoiceRights`, `Authorized_IsAllowed` (the privilege question), `PrivilegeProbeThrowing_FailsClosed`. The route 403 still holds because the route filter asks the same privilege (two layers, by design). |
| Claim by plain update instead of If-Match | 4: both concurrent-batch cases, the rival-claim case, the cancelled-after-claim case |

"Fails before the change" for the new files is by construction (the code does not exist before this task); the seeds above show each guard has teeth.

## Publish size, CVE

BFF Hygiene section 10 + NFR-01 verified.

- **Placement.** In the BFF (bff-extensions criteria B1 caller identity: the batch acts on the caller's own rights; B4 live request: it answers a user action). No new package, no background work, no AI facade involved.
- **Publish size.** Command `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish`, framework-dependent linux-x64, Release, PDBs included (the default), `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`. Both sides built from fresh worktrees at short paths: the task's base `b8912e5553` (`C:\wt150m`) and the feature commit `348d02e8bd` (`C:\wt150b`; the review fixes after it add about 50 lines of code). File counts match (192 and 192). Base 40,932,405 bytes (39.04 MB); with the task 40,970,728 bytes (39.07 MB). **Delta +38,323 bytes (+0.04 MB)**, far under the +5 MB trigger. The base is the task's own merge base, not master itself: master moves with other projects' merges, and the coordinator measures the merged tip.
- **CVE.** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package was added or changed.

## Gate

| Check | Result |
|---|---|
| `Sprk.Bff.Api.Tests` filter `Finance / Ebilling / Billing / Export / Invoice` (includes the data-mutation and auth integration folders it compiles) | 6,749 passed, 0 failed, 5 skipped (run before the review fixes); the 92 `ApExport*` tests alone pass after them |
| `Sprk.Bff.Api.IntegrationTests` | 199 passed, 0 failed, 8 skipped |
| Projects compiling `tests/integration/Shared/**` (ArchTests, `Sprk.Bff.Api.Tests`, `Sprk.Bff.Api.IntegrationTests`, `Spe.Integration.Tests`, `Sprk.Provisioning.ControlPlane.LoadTests`) | All five build, 0 errors |
| Full ArchTests | First run 1,855 passed, 3 failed: `ISS-005 guard ... resolve in bounded time` (a timing test; passes alone, unrelated); D-130 amount-column ratchet and task 188 r4e generic-writer guard (both real, fixed by listing `ApExportService` with the reasons in `FinanceBaseValuesSingleDefinitionGuardTests.cs` and `ExternalDocumentNarrowingGuardTests.cs`). The affected guards then ran 227 / 0 (route ledger, document narrowing, amount-column, 188 r4e, ISS-005) and ran again after the review fixes (see the final line of this table) |
| Final re-run after the review fixes | Affected ArchTests 256 / 0 (route ledger, narrowing, amount-column, 188 r4e, ISS-005); unit filter 6,756 passed, 0 failed, 5 skipped |

### Independent review (Step 9.5, one adversarial pass, auth work)

No F1. Three F2 findings, all fixed and seeded (revert, red, restore):

| Finding | Fix | Test |
|---|---|---|
| The irreversible send step honoured the request's cancellation token, so a client disconnect mid-step made a part-sent batch | Step 9 ignores the request token | `CancellationDuringTheSendStep_DoesNotSplitTheBatch` |
| A claim write that committed and then faulted was never released (not in the claimed list) | The claim is recorded before the write; release re-reads and does nothing when the claim is not this batch's | `ClaimCommittedThenFaulted_IsStillReleased` (red without the fix) |
| A move to Sent to AP that committed and then faulted was reported as "nothing exported" and its claim was released | After a failed move the status is re-read; sent, or unknown, counts as sent (claim kept, `transition_incomplete`) | `TransitionCommittedThenFaulted_IsCountedAsSent` (red without the fix) |

Also fixed: the batch read advertised a Servable file of a batch that was not Completed (now files only for Completed; `CompletionFault_...` red without it); an owner resolver that throws is now a clean `unavailable`; the "403 before the body is read" wording is "before the handler runs" (Minimal API binds the body before endpoint filters; a malformed body is 400 first, which leaks nothing). New tests also cover `completion_incomplete`, `compensation_incomplete`, json-only, and the snapshot being the settings text verbatim. Left as known limits (K3): the candidates list makes up to 500 per-record rights calls, and `UnknownInvoice_IsRefused` models a state Dataverse rights make unreachable (kept: the service still must not fail on it).

## Review fixes (branch `task/ebill-150-fixes`, from `work/ebilling-r1` at `536c2caee3`)

Findings of the adversarial review of the merged task, fixed in scope. Every fix has a test, and each was proven by disabling the fix (red) and restoring it (green): `ApExportReviewFixTests` (data-mutation), `ApExportMappingTests`, `ApExportAuthorizationTests`. The test world (`ApExportWorld.ExportDataverse`) now evaluates nested filters (`And` / `Or`), `NotEqual` and `statecode` (a row without one is Active), enforces Dataverse's 5,000 cap on `TopCount` and on a page (with paging and `MoreRecords`), and enforces the batch total column's range (0 to 1,000,000,000, the column as configured today).

| # | Finding | Fix | Tests (red without the fix) |
|---|---|---|---|
| 1 | The line reader filtered only on the invoice, so a removed (deactivated) Adjustment line was exported as a live line | `ValueReader.ReadLinesAsync` applies `InvoiceTotalsService.ExcludeInactiveAdjustments(query)`, reads `statecode`, and filters `InvoiceTotalsService.IsCounted` as the second layer, exactly as `InvoiceTotalsService`, `InvoiceReviewReadService` and `InvoiceIndexDocument` do. A deactivated Fee or Expense line is still exported (only Adjustment lines are removed by deactivation) | `RemovedAdjustment_IsNotExported`, `InactiveFeeLine_IsStillExported` (control) |
| 2 | An Adjustment line's `billed` and `approved` were `sprk_amount`, a positive deduction, so the line sums did not reconcile | **Convention (D-56 `lines[]`): an Adjustment line is its signed contribution to the invoice totals.** `approved` = minus its amount (a deduction is negative); `billed` = 0 (the firm billed nothing for it, so `BilledTotal` = fees + expenses is still the sum of `billed`). So `sum(lines[].approved)` = `ApprovedTotal`, `sum(lines[].billed)` = `BilledTotal`, and `approved - billed` is the adjustment on every line type. A removed adjustment is not a line (finding 1). Fee and Expense lines are unchanged (Disallowed approves 0, Reduced its approved amount) | `LineSums_ReconcileWithTheInvoiceTotals` (totals taken from `InvoiceTotalsCalculator`: fees, an expense, a reduced and a disallowed line, an active and a removed adjustment) |
| 3 | `TopCount = ReadLimit + 1` = 5,001, which Dataverse rejects (cap 5,000) | The line read is one page of 5,000 (`PageInfo`); `MoreRecords` means "more than 5,000" and fails the batch as before (clean compensation, nothing sent). Every other `TopCount` in the 150 code is 1, 10, 501 or 5,000 (the batch read of at most 200 invoices); none is above 5,000 | `LineReadStaysWithinTheRowCap` (5,000 lines export; 5,001 fails closed), `TheWorldEnforcesTheCap` |
| 4 | A negative or over-range batch total failed at the Completed write, AFTER the invoices were sent | The batch total is computed in the selection step and checked against `ApExportService.BatchTotalMinimum` (0 today) and `BatchTotalMaximum` (1,000,000,000) before the batch row or any claim: 400 `ebilling.export.batch_total_out_of_range`, nothing claimed or written. The minimum is that one constant; relax it once the widening script has run on the environment (see "For the owner / for 153": prerequisite). Route answer: a new refusal reason on the existing 400, no change of shape | `TotalOutsideTheColumnRange_IsRefusedBeforeAnyClaim` (4 cases), `TotalAtTheEdgeOfTheRange_Exports` (3 cases) |
| 5 | Authorization asked Write (`ebilling.invoice.decide`) only; D-85 requires the caller can read every invoice | `ApExportAuthorization` asks two rights per invoice, each of Dataverse as the caller: the decide right (Write), then Read with the existing `finance.read` policy key (the key the invoice read routes use on an invoice). No new `OperationAccessPolicy` key (a combined key would also need the completeness tests extended; two asks reuse the existing pattern, at the cost of a second rights lookup per invoice when the first passes). Both in `AuthorizeExportAsync` and `FilterExportableAsync` (the list narrows by each right in turn). Any refusal is the whole-batch refusal naming no invoice; a Read lookup that cannot be answered is Undetermined (503) | `WriteWithoutRead_RefusesTheWholeBatch_NothingWritten`, `WriteWithoutRead_Is403_NamingNoInvoice` (route), `WriteWithoutRead_IsNotACandidate`, `ReadAnswerFault_FailsClosed`, `Authorized_IsAllowed` (now 2 asks per invoice) |
| 6 | Step 9 counted `AlreadyInTarget` as a send, and treated any non-Approved status as "sent" | `TrySendAsync` succeeds only on `Transitioned` (an invoice already in Sent to AP was not moved by this batch: the batch stops). `MaybeSentAsync` is true only for Sent to AP or Paid, or when the status cannot be read (fail safe, claim kept); a status moved elsewhere (for example Rejected) is not a send by this batch and its claim is released | `AlreadyInTarget_IsNotASuccessfulSend`, `StatusChangedByAnotherWriter_IsNotCountedAsSent`, `SentOrPaid_IsCountedAsSent_AndKeepsItsClaim`, `UnreadableStatus_IsCountedAsSent` |
| 7 | `ApprovedOn` took the UTC date of the stored instant, up to a day off from the approver's local date | `approval.approvedOn` is a UTC instant and both files write the full ISO 8601 UTC timestamp, `yyyy-MM-ddTHH:mm:ssZ` (for example `2026-10-20T15:00:00Z`), whatever the column's `format` says. Other dates (`InvoiceDate`, `DueDate`, billing period, line date) are unchanged: date values | `ApprovedOn_IsTheFullUtcInstant_InBothFiles`, `Instant_IsWrittenAsTheFullUtcTimestamp`, the happy path (CSV) |
| 8 | Mapping reserved names; CSV header injection | The parser refuses an invoice column named `lines` (any case) with its usual `columns: ...` refusal (422 `mapping_invalid`): the JSON invoice object already carries the `lines` array. **`batchId` is deliberately not refused:** the document root's `batchId` is a different object from the invoice object, and the D-56 default column `BatchId` is an invoice column, so refusing it case-insensitively would reject the default mapping and every seeded mapping. CSV header names get the same injection prefix as values | `ReservedColumnName_Lines_IsRefusedWithTheUsualShape`, `InvalidMappings_AreRefused` (+3 cases), `BatchIdColumn_AndALineColumnNamedLines_AreValid`, `CsvHeader_PrefixesFormulaNames` |

### Known limits (listed, no code change)

- **Sequential calls versus the front-end timeout.** A 200-invoice batch makes about 2,000 sequential Dataverse calls (per invoice: the selection read, the claim, the re-check, the firm, approval and approver reads, the lines read and code lookups, the move), plus two rights lookups per invoice (finding 5). The front end times out at 230 seconds and there is no list-batches route, so a client that times out cannot find the batch it started except through the candidates list (its invoices leave it). Mitigation if it bites: cap the batch below 200 or move the work to a job (ADR-052); not done in R1.
- **Currencies are summed into one batch total** (`sprk_totalamount` is the sum of the invoices' approved totals, whatever their currency). R1 is USD-only; multi-currency needs a per-currency total or a refusal first.
- **A lost create response leaves an empty Generating batch.** If the batch row is created and the response is lost, the request answers 503 with nothing claimed, and an empty Generating batch remains. It blocks nothing (no invoice names it). An operator can delete it.
- **Two rights lookups per invoice** (finding 5): up to 400 for a full batch and up to 1,000 for the candidates list, each as the caller. A combined `OperationAccessPolicy` key would halve it; deferred because it touches the shared policy and its completeness tests.

### Found while fixing, not changed

- `InvoiceDate`, `DueDate`, billing period and line dates go through `DateOf` (the UTC date of the stored value). Checked by the main session (2026-10-30): `sprk_invoicedate` and `sprk_eventdate` are date-format, user-local columns in the solution (`Behavior` 1); the billing period columns are documented Date only (`docs/data-model/sprk_invoice.md`). The server writes these dates as midnight UTC, so `DateOf` returns the written date. Known limit: a date a user edits in the app from a time zone east of UTC is stored as the previous day's evening in UTC and exports one day early. Changing the columns to Date only is a schema change, not done here.

## Owner of the export files: the batch asks before it writes (dev incident 2026-11-03, uac-r2)

### The incident

On dev, 2026-11-03, a System Administrator whose systemuser sits in the ROOT business unit called `POST /api/finance/export-batches`:

1. `ApExportService` resolved the batch owner from the caller (`_owners.ResolveOwnerAsync`, no child table), which gave the root unit's default team. The batch row `f6788e95` was created there, in Generating.
2. The CSV Stored File then asked the hard-deny owner question (`sprk_storedfile` is a restricted-content table). `RestrictedContentOwnerTeam.CustomerTopBusinessUnitAsync` returned `AnswerIsRootBusinessUnit` (the root holds no customer data, so there is no customer unit), and the resolver refused with `record_owner_restricted_content_team_unresolved`.
3. `StoredFileService` collapsed that to `stored_file.owner_unresolved`, the export mapped every non-size refusal to `ebilling.export.file_failed`, and the route answered a retryable 503 "Try again". Compensation was clean (claims released, nothing sent), but batch `f6788e95` stayed Failed in the root unit, and every retry would leave another.

The fail-closed refusal itself is correct and unchanged: a hard-deny row is never owned by a default team.

### Defect 1: a refusal that can never succeed was answered as retryable

- **Threading, no second copy of the rule.** `StoredFileCreateResult` gained an optional `OwnerRefusalCode`: when the file store refuses with `OwnerUnresolved`, the resolver's own `RecordOwnerRefusal` code rides along unchanged. `ApExportService.IsOwnerUnitRefusal` matches the one code the resolver gives for every non-`Resolved` `RestrictedContentOwnerTeamStatus` (`RestrictedContentOwnerTeamUnresolved`): root unit, Secure Record unit, an unreadable or looping unit chain, and a missing, ambiguous or member-holding owner team. Each is configuration, not a fault (a Dataverse fault throws and never becomes a refusal), so a retry cannot change any of them. The status text is not parsed; the rule stays in `RestrictedContentOwnerTeam` and the resolver.
- **New stable code `ebilling.export.owner_unit_not_allowed`, 409.** 409 is what this module already gives refusals a retry will not change (`email_owner_refused`, `email_send_refused`, `ap_email_recipient_monitored`); 422 is used here for mapping and file-size faults, so it is not this class. Text: "Your business unit cannot own export files, so nothing was exported, and trying again will not help. An administrator must run the export from a user in a business unit that can own them." It names no id and carries no `batchId` extension.
- **503 `file_failed` stays for genuine faults:** a resolver or storage exception, and any owner refusal that is not the restricted-content one. The ApExport client already treats an unknown code as not retryable and shows the server's `detail` (`src/solutions/ApExport/src/services/exportFlow.ts` lists the retryable codes explicitly), so no client change was needed.
- The endpoint's `.WithDescription` and class remarks list the new 409. `.ProducesProblem(409)` and `(503)` were already declared on the route, so no `.Produces` line changed.

### Defect 2: the batch row was created before the file owner question was asked

- **Step (3b), before the selection read, the batch row and every claim.** `ApExportService` calls `StoredFileService.ResolveOwnerForAppAsync` with the same spec shape (`ExportFileSpec`, one builder shared with the write loop) and the same acting user the file write uses. That method builds the question through the same `OwnerQuestionOf` and the same private `AskOwnerAsync` that `CreateForAppAsync` uses, so the pre-check cannot be asked differently from the file's own check. A deterministic refusal writes nothing: no batch row, no claim, no blob, no file row.
- **Why the two checks cannot disagree, and what happens if they do.** A billing export is not an ownership parent (`RecordOwnershipResolver.OwnershipParentEntities`), so the file's question depends only on the acting user's business unit, never on the batch; asking before the batch exists changes nothing. They can differ only if the world changes between the two asks (the user moves to another unit, a member is added to the owner team). The file's own check then still refuses: the batch is compensated (files withdrawn, claims released, marked Failed with the code as its reason) and the answer is the file's (409 for a deterministic refusal, 503 `file_failed` for a fault). The pre-check is an early exit and never a grant.
- **Fail closed at the pre-check.** A fault, an answer that is neither an owner nor a refusal (`Unchanged`, an owner with an empty id) and any refusal other than the restricted-content one are a 503 `unavailable` with nothing written. Never the 409, never "allowed".
- **A Failed batch still blocks nothing.** Compensation (`CompensateAsync`) releases every claim not sent, withdraws files and marks the batch Failed; no invoice names a Failed batch, so the same invoices re-export (asserted in `TransientFileOwnerFault_Is503FileFailed_AndCompensates` and `FileOwnerRefusalAfterPreCheck_StillRefuses_409_AndCompensates`). A caller in the root unit now never reaches step (5), so no Failed row is created in the root unit. Batch `f6788e95` and any like it on dev are Failed rows that block nothing; clearing them is an operator decision, not done here (no live writes).

### Component justification (CLAUDE.md section 11): the new code and its supporting surface

1. **Existing.** `ebilling.export.file_failed` (503, retry) and `ebilling.export.email_owner_refused` (409, the email route). `StoredFileRefusal.OwnerUnresolved` names the refusal but drops why.
2. **Extension.** `file_failed` cannot carry it: its status tells a client to retry and the ApExport dialog offers "Try again" for it. The email code is another route and another cause (the email cannot be filed under the batch's team). So one new code. The status (409) and response helper (`InvoiceLineReviewEndpoints.Refused`) are existing; the carried status is one optional field on an existing record (`StoredFileCreateResult.OwnerRefusalCode`); the pre-check is one public method over the existing question builder (`StoredFileService.ResolveOwnerForAppAsync`). No new type, DI registration, endpoint, package or Dataverse column.
3. **Cost of doing nothing.** A user in the root business unit is told "Try again" for a refusal that can never succeed, and every attempt leaves a Failed batch row in the root unit. Without the pre-check method the export either copies the file's owner question (a second copy of the owner rule that can drift) or creates the batch first.

### Tests

`ApExportOwnerUnitTests` (data-mutation, service over the in-memory world, 10 cases) and `ApExportOwnerUnitRouteTests` (the real route, 4 cases):

- a root-unit caller gets the 409 code with no batch row, no claim, no blob and no file row, and a retry writes nothing either;
- a pre-check fault is a 503 `unavailable` with nothing written (service and route);
- four unknown or unreadable pre-check answers (another owner code, no code, `Unchanged`, an owner with an empty id) are 503, never the 409;
- a BU1 caller passes the pre-check and the export completes (service and route); the questions are asserted in order (pre-check, the batch's own owner, then one per file), the first asked before the first write, and the three file questions identical field for field;
- a transient owner fault in the file's own check (after the pre-check passed) is still 503 `file_failed`, compensates, and the same invoices re-export (service and route);
- a deterministic refusal in the file's check after the pre-check passed is the 409 and compensates; a non-restricted refusal there stays 503 `file_failed`.

`ApExportWorld` gained `CallerInRootBusinessUnit` (the resolver's contract for a hard-deny row: refused for the root unit while the batch's own ordinary question is still answered, which is how dev's batch was created) and a scripted `OwnerAnswer` hook.

**Seed proof (once).** With the step (3b) block removed, 7 of the 14 new tests fail, among them `RootUnitCaller_Is409_AndNothingIsWritten` at `result.BatchId.Should().BeNull("no batch exists")` (the batch was created before the file refused) and its route twin. The pre-check was restored from a byte-identical copy (63,131 bytes) and the 14 tests are green again.

### Found, not changed

- Other deterministic refusals of the plain (non-restricted) owner question, such as `record_owner_no_default_team` and `record_owner_acting_user_unresolved`, are still the retryable 503 here. A retry cannot fix those either. They are left out because the brief scopes the 409 to the restricted-content refusals; widening it is one more arm in `IsOwnerUnitRefusal`.
- The API answer names no unit id by design. An operator finds which status applied in the resolver's Error log line (`OrdinaryExitAsync`), which carries the status and the business unit ids.

## A batch is created only by Export to AP (uac-r2 finding on dev, 2026-11-03)

The Active Billing Exports view showed **+ New** and Delete. A batch made by hand has no invoices, no files and no claim. Core User keeps Create at Deep (the BFF's OBO create needs it), so the privilege stays; the fix is in the UI and on the server.

**Ribbon (source only).** `Entities/sprk_billingexport/RibbonDiff.xml` gained six `HideCustomAction`s, ids `sprk.<Location>.Hide`. Each Location is a control id read from the PUBLISHED ribbon of `sprk_billingexport` on spaarkedev1 (`RetrieveEntityRibbon`, read-only, 2026-11-03): `Mscrm.HomepageGrid.sprk_billingexport.NewRecord`, `...HomepageGrid...DeleteMenu` (the split button that holds Delete and Bulk Delete), `Mscrm.Form.sprk_billingexport.NewRecord`, `Mscrm.Form.sprk_billingexport.Delete`, `Mscrm.SubGrid.sprk_billingexport.AddNewStandard` and `...SubGrid...NewRecord`. The four existing CustomActions are unchanged. Not hidden (not asked): `Mscrm.Form.sprk_billingexport.SaveAndNew` (opens a blank form), `...NewRecordForBPFEntity` (shown only for process-flow entities), the sub-grid Delete. Hiding is display only; the server check below is the guard.

**Quick create.** Off. `IsQuickCreateEnabled` is false on dev (`EntityDefinitions`) and in `Entity.xml`; dev has no quick-create form (the `quick` form in source is a quick view). Nothing to change.

**Server.** Who can set `sprk_exportstatus` on create: the user. The column is Create/Update-valid, not field-secured, on the main form, and no server code owns it on create (no plugin, ADR-002); `sprk_formats` is the same. So a hand-made batch can read Completed with formats typed in, but it has no Stored File row (the columns that make one servable are field-secured to the BFF writer profile, 088) and no invoice claim.

| Path | Hand-made Completed batch | Result |
|---|---|---|
| Mark Sent to AP | read the status only (before) | now reads the batch like Email does: 409 `file_not_available` when it has no servable export file or no invoice; a real batch is unchanged |
| Email to AP | Completed and formats present, no Stored File row | already 409 `file_not_available` (the per-format file lookup), nothing read from storage, nothing sent; test added |
| Download | same | already 404 `file_not_available` (documented), no blob opened; test added |
| Batch read | lists files only from Stored File rows | a hand-made batch shows no files; safe |
| EligibleApprover, export census | do not read the batch table | not reachable (grep); safe |

The status and formats columns were not made read-only on the form: a form lock is not a guard, the server check is. Field security is out of scope.

**Legacy form fields.** `sprk_csvdocument` / `sprk_jsondocument` removed from the main form (both copies, and the column list in `Deploy-EbillingFormsViews.ps1`, so a re-run does not bring them back). The columns and their relationships stay; retiring the schema is separate. An empty "CSV Document" read as "no file".

**AP emails sub-grid "Add Existing Communication": known limit, not changed.** The 1:N button `Mscrm.SubGrid.sprk_communication.AddExistingStandard` runs the platform command `Mscrm.AddExistingRecordFromSubGridStandard` that every table's sub-grid uses. Scoping it to this form would mean overriding that command in `Entities/sprk_communication/RibbonDiff.xml` with a `FormEntityContextRule` (inverted) display rule. That is not done: it changes the command for every communication sub-grid in every app, whether a form-entity rule is evaluated for a sub-grid's host form cannot be proven without a live write, and the import replaces the table's whole RibbonDiffXml, which `SpaarkeSecureChildRibbons` already customises on dev (`sprk.SecureChild.sprk_communication.*`). Instead the server never treats an associated communication as an AP send: the Sent to AP Date is written only by Email to AP (after the BFF's own send) and Mark Sent to AP; nothing in `src` reads `sprk_communication.sprk_billingexport` (the BFF only writes it, `ServerSendOptions.BillingExportId`); a test attaches one by hand and proves Mark Sent is still 409 and reads no communication.

**Deploy (owner approves; nothing was imported).** `scripts/ebilling/Deploy-EbillingBillingExportRibbon.ps1` (dry run by default; `-Apply`; `-Verify`) packs the ribbon-only solution `BillingExportRibbons` (shell in `infrastructure/dataverse/ribbon/BillingExportRibbons`, the one RibbonDiff.xml copied from SpaarkeMaster at pack time) and imports it with a scoped publish of `sprk_billingexport` only (`Import-SolutionScoped` machinery, never PublishAllXml). The form change deploys with `scripts/ebilling/Deploy-EbillingFormsViews.ps1`. Order: BFF first (Mark Sent guard), then the ribbon, then the form.

rework: none task 150
