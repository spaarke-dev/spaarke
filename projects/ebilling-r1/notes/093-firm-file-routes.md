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
6. **A systemuser's Read on the invoice row** (fix round 1, uac-r2 ruling): for any systemuser caller (internal or flagged external), `CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync` is asked, app-only, about the systemuser the caller principal was resolved to, on `sprk_invoices({id})`. The caller must hold Read on the invoice AND on its root (step 3). No Read → the uniform 404; no answer or a fault → 503 (never step 3's answer alone). A contact is not asked.
7. **The files**: `StoredFileService.ReadFirmVisibleRowsForInvoiceAsync(invoice, 51)`, filtered by the store (invoice, purpose Invoice PDF, `sprk_firmvisible` true, Servable, no source document, no source communication), then re-checked row by row by `IsFirmFile` (the same conditions). More than 50 such rows → 503 (never a truncated list); internal rows of the invoice are not read and do not count. The content route then keeps only the file it names. Then 090's `JudgeServableAsync` per file (Servable, blob path parent matches, scan verdict): Serve → Clean or NotScanned; ScanPending → Checking (listed, not served); NotFound (Malicious, not Servable, Removed, path mismatch, missing blob) → omitted; anything else → 503.
8. Ordered by created-on, then id.

Any exception in steps 1–7 (other than the caller's own cancellation) → 503. The handlers do no reads: the list projects the set; content serves only a file of the set.

## Responses

- **List 200** (`ExternalCollectionResponse<ExternalInvoiceFileItem>`, the same envelope as the other external collections): `{ "value": [ { "id", "fileName", "contentType", "sizeBytes", "createdOn" (UTC, `Z`), "scanStatus": "Clean" | "NotScanned" | "Checking" } ] }`. No blob path, container, source id, URL or SAS (a test asserts the exact key set and the absence of the blob path).
- **Content 200**: the bytes through 090's `StreamServableAsync` with `downloadName: null`: the content type, `nosniff`, `no-store` and the sandbox CSP; **no file name is sent**. A PDF (an allow-listed type) is served inline with no `Content-Disposition`; a type outside the allow-list is served as `application/octet-stream` with `Content-Disposition: attachment` and no name. The SPA names a saved file from the list's `fileName`. One `StoredFileDownloaded` event (EventId 9090; route kind `firm`; caller `contact` + contact id, or `systemuser` + systemuser id) written before the first byte.
- **409** (content of a Checking file): `StoredFileAccessFilter.ScanPending`, StoredFileEndpoints.cs:479. Title "File not yet available", detail "The file is waiting for its malware scan. Try again in a few minutes.", `errorCode` `file_scan_pending`, `correlationId`.
- **404**: `ProblemDetailsHelper.UniformRecordNotFound`, ProblemDetailsHelper.cs:397. Title "Not Found", detail "The requested record was not found.", `reasonCode` `sdap.access.deny.record_unavailable`, `correlationId`. The same for an unknown invoice, an invisible invoice and a file outside the set; never an empty 200 for an invisible invoice.
- **503**: `StoredFileAccessFilter.Unavailable`, StoredFileEndpoints.cs:491. Title "File store unavailable", detail "The file could not be read right now. Try again later.", `errorCode` `file_store_unavailable`, `correlationId`. One body for every fault: the firm read, the systemuser flag, the invoice read, (a′), the secure flags, a systemuser's Read on the invoice row unanswered, the file rows, more than 50 firm-visible files, storage.
- **403** from the workforce role ceiling (`RequireWorkforceCeiling(ReadInvoiceFiles)`, Read on `sprk_invoice`), for a workforce contact caller (workforce plane, no systemuser) only, decided before any record is read: `reasonCode` `sdap.access.deny.workforce_baseline_privilege_missing` (the role lacks the privilege) or `sdap.access.deny.workforce_baseline_unavailable` (the role is missing, ambiguous or unreadable). Both routes declare `.ProducesProblem(403)`.
- **503 `ebilling.disabled`** when the eBilling flag is off. The flag filter runs after the authorization filter, so a caller who may not see the invoice still gets the 404.

## Retired

- The four `/api/v1/external/invoices/{id}/documents…` mappings (list, content, upload, versions), their handler wrappers and the invoice arm of the upload ceiling (ExternalProjectDataEndpoints.cs).
- `WorkforceRoleCeilingGate.ReadInvoiceDocuments` and `UploadInvoiceDocument` (no other user).
- Kept, still used: `RightsForInvoiceAsync` (delete-own), `DocumentParentKind.Invoice` and `GetDocumentParentBinding` (ExternalMessageEndpoints).
- Tests and censuses: the invoice cases in `ExternalLiveParentDocumentTests`, `ExternalRecordDocumentRoutesTests` and `ExternalMultiRootSecureRootsTests` are removed; `AuthenticationSchemeCensusTests` names the two new routes instead of the four; the ledger's invoice-document handler decisions are removed; `WorkforceCeilingRouteFamilyTests` has rows for the two new routes.

## Guards

- **Task 188 guard** (`ExternalDocumentNarrowingGuardTests`, new section "ebilling-r1 task 093: the firm file gate"): every external-plane method that reaches a Stored File read is either a listed firm file gate (it must also reach the record gate and the firm read) or a listed handler that serves the gate's set. (a′) is not checked by reach (fix round 1, item C below). Must-fire and must-not-fire controls are compiled fixtures in the same file. The 188 guard's own document scan had no slot for a route-level gate over Stored Files (it scans document reads), so this is a self-contained section beside it, not a weakening of it. **For uac-r2 to confirm at review.**
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
| `GetInvoiceFileContentAsync` removed from `FirmFileHandlers` | the guard fails: "NEW external Stored File surface: …GetInvoiceFileContentAsync" |

The guard's two seeding runs: 1 failed / 29 passed of the 30 `ExternalDocumentNarrowingGuardTests` each time; restored: 30 / 30.

Found while running the suites: the content handler's `StoredFileService` parameter was inferred as a request body in a host that does not register the service (the submission contract tests' host), which failed that host at startup (all its tests). Production registers the service unconditionally (FinanceModule.cs:490), so production was not affected; the parameter is now `[FromServices]`, as the sibling handlers' seams are.

### Totals

- `ExternalInvoiceFileRoutesTests`: 40 / 40.
- Targeted set (`ExternalInvoiceFileRoutesTests`, `ExternalRecordDocumentRoutesTests`, `ExternalLiveParentDocumentTests`, `ExternalMultiRootSecureRootsTests`, `WorkforceCeilingRouteFamilyTests`, `InvoiceSubmissionEndpointsContractTests`, `StoredFileEndpointsTests`, `StoredFileServiceTests`): 888 passed, 0 failed.
- Full `tests/Spaarke.ArchTests`: 2,624 passed, 0 failed.
- `dotnet build src/server/api/Sprk.Bff.Api --warnaserror --no-incremental`: 0 warnings, 0 errors. `tests/unit/Sprk.Bff.Api.Tests` and `tests/Spaarke.ArchTests` build with 0 warnings, 0 errors.

## Publish size and CVEs

Release publish of `Sprk.Bff.Api`, each zipped with `Compress-Archive`, in two temporary worktrees (removed afterwards with `Remove-WorktreeSafe.ps1`):

| Build | Files | Raw bytes | Zip bytes |
|---|---|---|---|
| origin/master `d03537e133` | 191 | 140,307,565 | 42,311,916 |
| this branch `5e536cac48` | 191 | 140,326,629 | 42,319,222 |
| Delta | 0 (identical file list) | +19,064 | **+7,306 (≈ +7 KB)** |

The branch side also carries the ebilling work branch's other differences from master, so this is an upper bound for the task. Well under the +5 MB threshold; 42.3 MB total, under 55 MB. No package added. `dotnet list package --vulnerable --include-transitive`: no vulnerable packages.

## Other guard touched

`InvoiceMoneyColumnsAppOnlyWriteGuardTests` (166 K4 C1) failed in the full ArchTests run: the gate names `sprk_vendororg` (it adds it to the record gate's invoice read). The gate only reads it, app-only through `ExternalDataService.GetInvoiceLookupsAsync`, so it is added to that guard's `Readers` with a reason, and recorded in `166-invoice-field-security.md` ("Reviewed after the 164a merge").

## Decisions

- One gate method for both routes and both caller kinds; the handlers do no reads of their own.
- The firm match sits on the record gate's single invoice read, so another firm's invoice is indistinguishable from a missing one, after the same work. Internal systemusers are exempt (criterion 4).
- One 503 body (`file_store_unavailable`) for every fault, so a fault never looks like a refusal and the SPA has one retry rule.
- The ceiling is Read on `sprk_invoice` only: `sprk_storedfile` is read only by the BFF's app identity.
- The gate also refuses a firm-visible row that has a source id (defense in depth; the writer never marks such a row).

## Known limits

- **(a′) is covered by behaviour, not by the guard.** The guard checks reach only, and any path through the record gate reaches the predicate, so it does not check (a′) at all (fix round 1, C). What covers it: the record gate refuses every invoice when its (a′) seam is null (fail closed), and the route tests' hidden, predicate-fault and "asked once" cases fail when the seam is null or hides nothing (seeding proofs above).
- **Unknown rights (0x8004d24d)** are resolved upstream by the caller-principal resolver (036), which fails closed before this gate runs; this task adds no separate test for it.
- **Blank `sprk_isexternal` is internal** (owner round 67): a systemuser whose flag is blank is treated as an internal user (no firm match). The systemuser flag comes from the shared resolver, which caches it for 10 minutes, so a change to the flag takes up to 10 minutes to reach this gate (uac-r2 accepted this as the shared resolver's standing behaviour).
- **The systemuser invoice-row question inherits open issue #2826**: the probe answers from the person's Dataverse rights and does not yet take the person's access mode into account. When #2826 lands, this gate takes the fix with it.
- **Firm match** uses the conferring organization memberships of the caller's contact (for a flagged-external systemuser, its linked contact's), the same definition as the submission routes (`ReadCallerFirmsAsync`).
- **The list judges every firm-visible file of the invoice**, so one file's storage fault makes the list a 503 (K3). The content route judges only the file it names (fix round 1), so another file's fault no longer refuses a healthy download.
- **The 404's timing is not equalised** across the checks: the bytes are identical, but a refusal after the record gate (the systemuser's invoice-row question) takes longer than an unknown invoice's.
- **The separate 188-guard section** ("ebilling-r1 task 093: the firm file gate" in `ExternalDocumentNarrowingGuardTests`) awaits uac-r2's confirmation of its shape.
- **SPA:** until the R4 SPA (task 021) ships, the external SPA's old invoice-documents call (`RecordDetailFields.tsx:149`) returns 404. Deploy order: the BFF swap, then the SPA immediately after (079, through R3).
- In `ExternalRecordDocumentRoutesTests` one container test keeps a harmless `MatterAccess` entry from the removed invoice case.

## Not done in this run (operator direction)

- Step 9.5 adversarial reviews, code-review, adr-check and the uac-r2 review (the POML's step 5).
- `/conflict-check` and the SPA-r3 notification. The route contract for SPA-r3 is the "Responses" section above.

## Fix round 1

From the uac-r2 ruling and reviews 1 and 2. The route contract the SPA depends on is unchanged: `value[]` of `{id, fileName, contentType, sizeBytes, createdOn, scanStatus}`, 409 `file_scan_pending`, the uniform 404, the 503. The only addition is the declared 403 (F).

| Item | Source | Done | Where |
|---|---|---|---|
| A | uac-r2 ruling | A systemuser caller (internal or flagged external) also needs Read on the invoice row itself, asked app-only of Dataverse for the systemuser the caller principal was resolved to (`CallerRecordAccessProbe.GetPrincipalRightsAppOnlyOrUnknownAsync`, the plane's no-OBO form of the probe). The record gate stays, so the result is Read on the invoice AND on its root. No Read → the uniform 404; no answer or a fault → 503. Contacts unchanged. Both routes (one gate). | Gate step 6; `ExternalInvoiceSubmissionAuthorizationFilter.DecideInvoiceFilesAsync` |
| B | review 1 F1 | `StoredFileService.ReadFirmVisibleRowsForInvoiceAsync`: the invoice's rows filtered by the store (invoice, Invoice PDF, firm-visible, Servable, no source document, no source communication), copied from task 116's filtered read. `IsFirmFile` re-checks each row; the 50 cap counts the filtered set only. Also K3: the content route judges only the file it names. | Gate step 7 |
| C | review 2 F1 | The guard's (a′) reach check is removed (it could not fire: any path through the record gate reaches the predicate). The guard comment says what covers (a′) instead. The freed control slot now holds a must-fire control for the record gate check, which had none. | `ExternalDocumentNarrowingGuardTests`, 093 section |
| D | review 2 F2 | Four delete-own cases with an invoice parent: Read through the invoice's matter → deleted; no Read → 403; parent read fault → 503 `invoice_unreadable`; hidden by (a′) → 403. | `ExternalInvoiceLinkedDeleteTests` |
| E | review 2 F3 | `RightsForInvoiceAsync` answers Read or None: the write-confirmation branch and the Create secure-roots step are gone (nothing passes `confirmWrites` for an invoice parent: the list and upload routes that did were the retired ones). The invoice arms of `DocumentParentNoun` and `DocumentParentSegment` are gone (only the four retired routes passed an invoice kind). Doc comments updated. D's tests stay green. | `ExternalProjectDataEndpoints.cs` |
| F | review 2 F4, review 1 F2 | "Attachment name" corrected (Responses: no file name is sent; a PDF is inline). Both routes declare `.ProducesProblem(403)`, and their descriptions and the Responses section name the ceiling's two 403 codes. | `ExternalInvoiceSubmissionEndpoints.cs`; Responses |
| G | review 2 F6 | The header of `ProcedureLibraryCiamDenyTests` names the new Stored File content route and why it cannot serve a Procedure Version file (parent and purpose). | `tests/integration/auth/ExternalAccess/ProcedureLibraryCiamDenyTests.cs` |
| H | reviews | Known limits added: blank `sprk_isexternal` is internal and the flag cache; #2826; firm match definition; K3; unknown rights upstream; the 188-guard section awaiting uac-r2; the SPA 404 window and deploy order; the 404's timing. | Known limits |

### Red before, green after

Each new must-fire test was run against the gate file of `dc2e5ab86c` (only that file swapped back, then restored; no stash):

| Test | On `dc2e5ab86c` | After |
|---|---|---|
| A(a) systemuser (flagged and internal) with Read on the matter, None on the invoice row → the unknown invoice's 404, no file row or byte read | fail ×2 (served) | pass |
| A(b) the row question unanswered or faulting → 503, nothing read or served | fail ×4 (served) | pass |
| A(c) Read on both → 200, Dataverse asked about that systemuser and that invoice | fail ×2 (the old gate asks nothing; the 200 itself held) | pass |
| A(d) a contact with the same root grant is served, no row question | pass (must not fire) | pass |
| B 60 internal files and one firm PDF → listed and served | fail (503) | pass |
| B the Stored File query carries the six filters | fail | pass |
| K3 another file's storage fault does not refuse a healthy download | fail (503) | pass |

D's cases are coverage of existing behaviour (green before and after E). Seeding proofs for them: ignoring (a′) in `RightsForInvoiceAsync` fails the hidden case; ignoring the parent Read fails the no-Read case (1 of 18 each, restored).

### Totals (fix round 1)

- `ExternalInvoiceFileRoutesTests`: 52 / 52 (40 before, plus 12).
- `ExternalInvoiceLinkedDeleteTests`: 18 / 18 (plus 4); with `ExternalDeleteOwnTests`: 188 / 188.
- Every class matching `ExternalAccess|StoredFile|InvoiceSubmission` in `Sprk.Bff.Api.Tests` (which compiles `tests/integration/contract/**` and `tests/integration/auth/**`; includes `WorkforceCeilingRouteFamilyTests`, `StoredFileEndpointsTests`, `InvoiceSubmissionEndpointsContractTests` and the delete-own tests): 8,309 passed, 0 failed.
- `ProcedureLibraryCiamDenyTests.cs` (3 classes): 7 / 7.
- Full `tests/Spaarke.ArchTests`: 2,624 passed, 0 failed.
- `dotnet build src/server/api/Sprk.Bff.Api --warnaserror --no-incremental`: 0 warnings, 0 errors. No package added, so no publish-size re-measure.
- Not run: `tests/integration/Spe.Integration.Tests` `ExternalAccessIntegrationTests` (a live-environment suite; it does not reach these routes, and this round runs against no live Dataverse).
