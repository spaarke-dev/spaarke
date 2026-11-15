# Task 093: firm file routes, and the four invoice-document routes retire

Worktree branch `worktree-agent-aa64df149f3786340`, based on `f9f74de82e`. Rigor FULL, opus tier. Operator direction for this run: no Step 9.5 adversarial review, no agents, no `/conflict-check`, no message to other sessions (see "Not done" below).

## What was built

| Piece | Where |
|---|---|
| `GET /api/v1/external/invoices/{id}/files` (`ListExternalInvoiceFiles`) | ExternalInvoiceSubmissionEndpoints.cs:106, handler `ListInvoiceFilesAsync` :144 |
| `GET /api/v1/external/invoices/{id}/files/{fileId}/content` (`GetExternalInvoiceFileContent`) | ExternalInvoiceSubmissionEndpoints.cs:120, handler `GetInvoiceFileContentAsync` :165 |
| The one gate (filter mode `InvoiceFiles`) | ExternalInvoiceSubmissionAuthorizationFilter.cs:183 (switch arm), `DecideInvoiceFilesAsync` :419, `IsFirmFile` :547 |
| The visible file set carried to the handler | `ExternalInvoiceFileSet` / `ExternalInvoiceVisibleFile`, ExternalInvoiceSubmissionAuthorizationFilter.cs:92, :98 (on `ExternalInvoiceSubmissionScope.InvoiceFiles`) |
| The ceiling | `WorkforceRoleCeilingGate.ReadInvoiceFiles` = Read on `sprk_invoice` |
| List DTO | `ExternalInvoiceFileItem`, StoredFileContracts.cs:628; `ExternalInvoiceFileScanStatus` :632 |
| Streaming + audit | `StoredFileEndpoints.StreamServableAsync(..., auditCaller)` (090's path, now taking the firm route's caller); `StoredFileServable` carries the scan state; `StoredFileRow.FirmVisible` |

Both routes chain `AddExternalInvoiceSubmissionAuthorizationFilter(InvoiceFiles)`, `RequireWorkforceCeiling(ReadInvoiceFiles)` and `RequireEbillingEnabled()`, on the external group (its policy and caller-principal filter). No new policy, no package, no upload route, no versions route.

## The gate (one method, both routes, both caller kinds)

1. **Internal or firm caller.** A systemuser is internal only when `ISystemUserIdentityResolver.IsExternalAsync` says it is not flagged external (no resolver registered: treated as a firm caller). A contact is always a firm caller.
2. **The caller's firms** (firm callers only): `ReadCallerFirmsAsync`, the same firm definition as `DecideReadOneAsync` (a flagged systemuser's firms are those of its linked contact). Unreadable → 503. None (no contact, no conferring membership) → the uniform 404.
3. **The record gate**: `ExternalMessageEndpoints.GetRecordRightsDecisionAsync` on the invoices root, with the production module registry, secure-root reader, live-parent reader and the (a′) seam (`ExternalInvoiceHiding`). This is the 036 / 187 machinery: the caller's post-veto rights through the invoice's parents (matter, project, work assignment), the secure-root rule, and (a′) at invoice level, asked once with `[invoice]`. No second implementation.
4. **The firm match** runs on the record gate's one invoice read: the read also asks for `sprk_vendororg`, and for a firm caller a vendor that is not one of its firms is answered as `RecordNotFoundException`, i.e. the same as a missing invoice. If the gate admits without that read having run, the method refuses (fail closed).
5. **No Read** → the uniform 404; an undecided gate (secure flag or (a′) could not be read) → 503.
6. **The files**: `StoredFileService.ReadRowsForInvoiceAsync(invoice, 51)`; more than 50 → 503 (never a truncated list). `IsFirmFile`: parent is this invoice, purpose Invoice PDF, `sprk_firmvisible` true, no source document and no source communication. Then 090's `JudgeServableAsync` per file (Servable, blob path parent matches, scan verdict): Serve → Clean or NotScanned; ScanPending → Checking (listed, not served); NotFound (Malicious, not Servable, Removed, path mismatch, missing blob) → omitted; anything else → 503.
7. Ordered by created-on, then id.

Any exception in steps 1–6 (other than the caller's own cancellation) → 503. The handlers do no reads: the list projects the set; content serves only a file of the set.

## Responses

- **List 200** (`ExternalCollectionResponse<ExternalInvoiceFileItem>`, the same envelope as the other external collections): `{ "value": [ { "id", "fileName", "contentType", "sizeBytes", "createdOn" (UTC, `Z`), "scanStatus": "Clean" | "NotScanned" | "Checking" } ] }`. No blob path, container, source id, URL or SAS (a test asserts the exact key set and the absence of the blob path).
- **Content 200**: the bytes through 090's `StreamServableAsync` (content type, `nosniff`, `no-store`, attachment name), with one `StoredFileDownloaded` event (EventId 9090; route kind `firm`; caller `contact` + contact id, or `systemuser` + systemuser id) written before the first byte.
- **409** (content of a Checking file): `StoredFileAccessFilter.ScanPending`, StoredFileEndpoints.cs:479. Title "File not yet available", detail "The file is waiting for its malware scan. Try again in a few minutes.", `errorCode` `file_scan_pending`, `correlationId`.
- **404**: `ProblemDetailsHelper.UniformRecordNotFound`, ProblemDetailsHelper.cs:397. Title "Not Found", detail "The requested record was not found.", `reasonCode` `sdap.access.deny.record_unavailable`, `correlationId`. The same for an unknown invoice, an invisible invoice and a file outside the set; never an empty 200 for an invisible invoice.
- **503**: `StoredFileAccessFilter.Unavailable`, StoredFileEndpoints.cs:491. Title "File store unavailable", detail "The file could not be read right now. Try again later.", `errorCode` `file_store_unavailable`, `correlationId`. One body for every fault: the firm read, the systemuser flag, the invoice read, (a′), the secure flags, the file rows, more than 50 files, storage.
- **503 `ebilling.disabled`** when the eBilling flag is off. The flag filter runs after the authorization filter, so a caller who may not see the invoice still gets the 404.

## Retired

- The four `/api/v1/external/invoices/{id}/documents…` mappings (list, content, upload, versions), their handler wrappers and the invoice arm of the upload ceiling (ExternalProjectDataEndpoints.cs).
- `WorkforceRoleCeilingGate.ReadInvoiceDocuments` and `UploadInvoiceDocument` (no other user).
- Kept, still used: `RightsForInvoiceAsync` (delete-own), `DocumentParentKind.Invoice` and `GetDocumentParentBinding` (ExternalMessageEndpoints).
- Tests and censuses: the invoice cases in `ExternalLiveParentDocumentTests`, `ExternalRecordDocumentRoutesTests` and `ExternalMultiRootSecureRootsTests` are removed; `AuthenticationSchemeCensusTests` names the two new routes instead of the four; the ledger's invoice-document handler decisions are removed; `WorkforceCeilingRouteFamilyTests` has rows for the two new routes.

## Guards

- **Task 188 guard** (`ExternalDocumentNarrowingGuardTests`, new section "ebilling-r1 task 093: the firm file gate"): every external-plane method that reaches a Stored File read is either a listed firm file gate (it must also reach the record gate, the firm read and the (a′) predicate) or a listed handler that serves the gate's set. Must-fire and must-not-fire controls are compiled fixtures in the same file. The 188 guard's own document scan had no slot for a route-level gate over Stored Files (it scans document reads), so this is a self-contained section beside it, not a weakening of it. **For uac-r2 to confirm at review.**
- **(a′) wiring guard** (`ExternalInvoiceVisibilityGuardTests`): `DecideInvoiceFilesAsync` is a wired surface; "093" is removed from the pending list (only "152" remains).
- **Note:** 098's guard did not flag `GetInvoiceFileContentAsync`, because `StreamServableAsync` is outside its scan scope; the new 093 section covers that handler.

## Tests

`tests/unit/Sprk.Bff.Api.Tests/Api/ExternalAccess/ExternalInvoiceFileRoutesTests.cs`, 40 cases, over the real mapping, the real caller-principal group filter, the real gate, the real record gate with the production module registry, the real flag filter and the real `StoredFileService` (over 090's fakes). Substituted only at Dataverse, the blob store, memberships, the systemuser flag and the (a′) seam.

- Must not fire: a firm contact, a flagged systemuser whose linked contact is in the firm, and an internal systemuser each list and download; an internal systemuser sees another firm's invoice with no firm read; row shape; one audit event before the first byte; (a′) asked once with `[invoice]`.
- Must fire (the unknown invoice's 404 byte for byte on both routes, with no file-row read, no storage call and no audit): other firm's invoice; contact without Read; contact with no grant; contact with no firm; systemuser without Read; flagged systemuser with no rights on the root (the post-veto answer for a Restricted root); flagged systemuser whose linked contact is in another firm; flagged systemuser with no linked contact; internal systemuser without Read; hidden by (a′); also filed under a secure root the caller cannot read.
- File level (not listed, content = the unknown file's 404): not firm-visible, supporting file, copy of a document, copy of an email, not yet servable, removed, malicious, another invoice's file.
- Pending → Checking and 409 with no audit; scanning off → NotScanned and 200; no files → empty 200.
- Faults → 503 `file_store_unavailable` on both routes: firm read, systemuser flag, invoice read, (a′), secure flag, file rows, storage, more than 50 files.
- Flag off → 503 `ebilling.disabled`, refused caller still 404; no token → 401; POST on `/files` → 405; `/files/{id}/versions` → 404.

Beyond the stated contract (one line each): "copy of an email" mirrors "copy of a document" (the gate refuses either source); "no files → empty 200" pins that the empty list is only for a visible invoice; "versions → 404" pins the no-versions-route contract.

### Seeding proofs (code changed, tests run, code restored)

| Change | Result |
|---|---|
| Firm match removed (vendor never compared) | 3 fail: `other-firm-invoice`, `flagged-systemuser-contact-of-another-firm`, flag-off's refused caller |
| (a′) seam replaced by one that hides nothing | 3 fail: `hidden-by-the-predicate`, fault `predicate`, "asked once" |
| (a′) seam replaced by null | 23 fail (the record gate fails closed on a missing seam: every invoice refused) |
| `FirmVisible` check removed from `IsFirmFile` | 1 fails: `not-firm-visible` |
| `DecideInvoiceFilesAsync` removed from `FirmFileGates` | the guard fails: "NEW external Stored File surface: …DecideInvoiceFilesAsync" |
| `GetInvoiceFileContentAsync` removed from `FirmFileHandlers` | TBD |

### Totals

TBD

## Publish size and CVEs

TBD

## Decisions

- One gate method for both routes and both caller kinds; the handlers do no reads of their own.
- The firm match sits on the record gate's single invoice read, so another firm's invoice is indistinguishable from a missing one, after the same work. Internal systemusers are exempt (criterion 4).
- One 503 body (`file_store_unavailable`) for every fault, so a fault never looks like a refusal and the SPA has one retry rule.
- The ceiling is Read on `sprk_invoice` only: `sprk_storedfile` is read only by the BFF's app identity.
- The gate also refuses a firm-visible row that has a source id (defense in depth; the writer never marks such a row).

## Known limits

- **Reach is not path.** The guard proves the gate can reach (a′) through the record gate, not that it passes a non-null seam. The behaviour tests and the seeding proofs cover that.
- **Unknown rights (0x8004d24d)** are resolved upstream by the caller-principal resolver (036), which fails closed before this gate runs; this task adds no separate test for it.
- **SPA:** until R4/R3 ships, the external SPA's invoice-documents call (`RecordDetailFields.tsx:149`) returns 404. Task 079 deploys BFF then SPA back to back through R3.
- In `ExternalRecordDocumentRoutesTests` one container test keeps a harmless `MatterAccess` entry from the removed invoice case.

## Not done in this run (operator direction)

- Step 9.5 adversarial reviews, code-review, adr-check and the uac-r2 review (the POML's step 5).
- `/conflict-check` and the SPA-r3 notification. The route contract for SPA-r3 is the "Responses" section above.
