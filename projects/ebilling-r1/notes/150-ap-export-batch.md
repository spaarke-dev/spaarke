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

- The caller needs **Create on `sprk_billingexport`** (the table privilege `prvCreatesprk_billingexport`, asked of Dataverse as the caller, on-behalf-of, uncached) **and `ebilling.invoice.decide` (Write) on EVERY invoice of the batch** (the right approval uses).
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
- No App Service setting is needed. No schema change; nothing to deploy.
- The export file bytes are limited by 090's checked size conversion (`sprk_sizebytes` is a whole number, max 2,147,483,647): an oversized file is 422 `file_too_large` before any write. The 2 GiB boundary itself is exercised in 090's `StoredFileServiceTests` (the uac-r2 pointer); a 2 GiB buffer is not allocated here.
- **DEF-049 assessed: 150 does not trigger it.** The issue says `SubmissionCommitGate` step 6 and its undo read "the invoice's files" by the invoice. An export file's parent is the **batch**, never the invoice (a test pins that the row carries no `sprk_invoice` lookup), so `ReadRowsForInvoiceAsync` cannot see it and the gate neither skips on it nor deletes it. O-14 (files added to an invoice) is what still triggers DEF-049; the precondition named for 150 is met by construction.
- Reads are bounded: an invoice's lines are read with a 5,000-row top count and an invoice with more than that fails the batch (clean compensation) rather than silently dropping lines; the batch is capped at 200 invoices, so no query relies on an unbounded page.

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

rework: none task 150
