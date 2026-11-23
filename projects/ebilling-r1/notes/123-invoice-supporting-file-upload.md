# Task 123: internal upload of supporting files to an existing invoice

`POST /api/finance/invoices/{invoiceId}/files` (EB-D-161). Wire contract: `notes/123-upload-contract.md` (updated by this task to the final codes; the differences are listed there and in section 5).

## 1. What was built

- **Route** in `src/server/api/Sprk.Bff.Api/Api/Finance/StoredFileEndpoints.cs`, mapped beside the GET routes in the `/api/finance` group (`RequireAuthorization`, `RequireInternalFinanceCaller`).
- **Request:** `multipart/form-data` with exactly one file part named `file`. The part's file name (`Path.GetFileName`) and content type are stored as given; a part with no content type is stored as `application/octet-stream`.
- **201 Created:** `Location: /api/finance/files/{fileId}/content` and body `{ "fileId": "guid", "fileName": "timesheet.csv", "sizeBytes": 40211 }` (`InvoiceFileUploaded`, in `StoredFileContracts.cs`). `fileName` is the name as stored (the store keeps at most 400 characters).
- **The Stored File:** created by `StoredFileService.UploadForCallerAsync` with purpose Invoice supporting file, Firm-visible No, parent `sprk_invoice` = `{invoiceId}`, staged, then the row (Not yet servable), then the verified flip to Servable. No `sprk_document` is created. The upload never waits for the malware scan; the download route holds the file until its verdict.
- **Filter mode:** `FinanceDenial.UniformNotFoundForRecord` in `Api/Filters/FinanceAuthorizationFilter.cs` (see section 3).

## 2. Component justification (CLAUDE.md §11)

1. **Existing:** 090's `StoredFileService.UploadForCallerAsync` (and `PrecheckUploadAsync`), which had no route; the GET download route and the 151e GET list in `StoredFileEndpoints`. For the denial shape: `FinanceAuthorizationFilter` and its `FinanceDenial` modes.
2. **Extension:** the route is added beside the GETs in `StoredFileEndpoints`, through the existing service, filter and kill switch. The denial shape is one new value of the existing `FinanceDenial` enum and two switch arms; no new filter, operation, service, package or configuration key. The 201 DTO is one record beside `InvoiceFileListItem`.
3. **Cost of doing nothing:** there is no way to add a supporting file to an existing invoice; model 9.1 and 097 point at a route that does not exist, and word-add-in-r1 has no upload target. Without the new mode, the route cannot meet its contract: `UniformNotFound` renders step 0 and a rights fault as 404 (the client cannot tell "retry" from "no such invoice"), and `Forbidden` renders no Write as 403 `insufficient_rights` (not the uniform 404).

## 3. Filter and check order

1. `RequireAuthorization` (group): 401.
2. `AddFinanceAuthorizationFilter("ebilling.invoice.decide", InvoiceEntitySet, "invoiceId", UniformNotFoundForRecord)`. `ebilling.invoice.decide` is the existing key for Write on the invoice (no new operation):
   - step 0: 403 `internal_callers_only` (flagged external, no systemuser) or 403 `system_failure` (a fault), before any record read;
   - the caller's own rights on the invoice, as the caller (`AuthorizationService.GetCallerRecordAccessAsync` with the caller's token; never app-only): no Write or no invoice → the uniform 404 (`ProblemDetailsHelper.UniformRecordNotFound`, the same bytes for both); an answer Dataverse did not complete and that does not hold Write → 403 `system_failure`; a thrown check → 403 `system_failure`.
   - This runs before the handler, so the body is not read (the handler binds no form parameter).
3. `RequireEbillingEnabled()`: 503 `ebilling.disabled`, after authorization (a caller with no Write still gets the 404).
4. Handler (`UploadInvoiceFile`):
   1. The invoice's `statecode` / `statuscode`, read app-only. Gone → the uniform 404; statecode Inactive → 409 `ebilling.invoice.inactive`; a read fault or a missing or unknown statecode → 503 (fail closed). Fix round 1: then the invoice's Stored File rows (an Invoice PDF not yet Servable → 409 `ebilling.invoice.files_pending`; 50 rows or more → 409 `ebilling.files.limit_reached`), and the whole of this step runs again after the body is read (section 10).
   2. The request: Content-Length over the limit plus 1 MB of envelope → 413; the per-request body cap set to the same; not multipart → 400; the server's cap while reading → 413; not exactly one file part named `file` → 400 `file_required`; 0 bytes → 400 `file_empty`; over `Ebilling:MaxSubmissionFileSizeMb` → 413. The route also carries the submission routes' `RequestSizeLimitAttribute(InvoiceSubmissionHttp.MaxRequestBodyBytes)`.
   3. `PrecheckUploadAsync` (name and type rules, size, a store that can stage now), with no write.
   4. `UploadForCallerAsync`: step 0 again, the caller's Write again through the gate variant (`GetCallerRightsForAuthorizationGateAsync`, the backstop), owner, stage, row, flip.
- Every refusal before 4.4 stores nothing (tests assert no storage call and no Dataverse write). A staging fault stores no row. A row-write fault deletes the bytes this attempt staged (090). A refused flip leaves the row Not yet servable, never Servable. Each answers 503 `file_store_unavailable` with `Retry-After: 30`.
- `.Accepts<IFormFile>("multipart/form-data")` is deliberately not on the route: that metadata makes routing answer a non-multipart body 415 before authorization, where the contract answers 400 `file_required` after it.

## 4. Paid

The POML lists Rejected, Returned to Firm, Cancelled and Superseded. The code's own definition of Active is statecode (`InvoiceStatusCodes.StateActive` = In Review, Approved, Sent to AP), and Paid is statecode Inactive ("Final: one payment of the full approved total, never reopened"). The route therefore refuses Paid with the same 409. The check is `statecode == InvoiceStatusCodes.StateInactive`; no new helper. There was no existing inactive-invoice error code (searched `invoice.inactive`, `not_active`, `_inactive`, `StateInactive` readers), so `ebilling.invoice.inactive` is new, verbatim from the contract.

## 5. Final error codes vs the contract

| Contract (2026-10-27) | Built | Why |
|---|---|---|
| 503 `ebilling.files.store_unavailable` | 503 `file_store_unavailable` | The existing `StoredFileEndpoints.StoreUnavailableErrorCode`, the same file's code for the same condition (reused verbatim) |
| 409 inactive: Rejected, Returned to Firm, Cancelled, Superseded | also Paid | section 4 |
| (no row) | 409 owner refusal (reasonCode), 503 for its one transient code | `ProblemDetailsHelper.RecordOwnerRefused`, the platform's one answer for "no owner resolves" |
| 400 `file_required`: no part, or more than one | also: not multipart or no body; one part not named `file`; a content type over 255 characters | the contract's code for a body without one usable `file` part |
| 404: unknown, or no Write | also: removed between the rights check and the state read | the same uniform 404 |
| 503 store failure | also: store not configured, the invoice state unreadable or unknown | nothing was stored; a retry may succeed |
| 400 `file_empty`, 413 `file_too_large`, 401, 403 ×2, 503 `ebilling.disabled` | as pinned | — |

`ebilling.files.file_required`, `file_empty`, `file_too_large` and `ebilling.invoice.inactive` are new constants on `StoredFileEndpoints`. No existing constant has the same meaning: `ebilling.submission.*`, `procedure_version.*` and `ratecard.*` are those routes' own codes. The contract note's table is updated, and these differences need word-add-in-r1's agreement before merge.

## 6. Seeding proofs

Run with `dotnet test tests/unit/Sprk.Bff.Api.Tests --no-build --filter "FullyQualifiedName~InvoiceFileUpload"` (51 tests). Each mutant was applied to a copy, run, then the original was copied back. The tree was verified to match the intended version, and the run was repeated (51 passed).

| Mutant | Exactly what was changed | Result |
|---|---|---|
| A. No Write check at all | Route filter operation `"ebilling.invoice.decide"` → `"finance.read"` (Read only) AND in `StoredFileService.UploadForCallerAsync` `if (!rights.HasFlag(AccessRights.Write))` → `if (false && …)` | **3 failed**, 48 passed: `NoWrite_404_NothingStaged`, contract `FilterRefusal_Shape("no Write")`, contract `NoWrite_And_Unknown_ByteIdentical` |
| A′. Filter's Write check only (backstop kept) | the filter operation change only | **1 failed**: `NoWrite_404_NothingStaged` (the backstop still answers the 404 and stages nothing, but the invoice is read before the refusal: the refusal is no longer before the handler) |
| B. No Inactive check | `if (state == StateInactive)` → `if (false && …)` and `if (state != StateActive)` → `if (state is null)` (an Inactive invoice goes on to the upload) | **6 failed**: `InactiveInvoice_409_NothingStaged` ×5 (Paid, Rejected, Returned to Firm, Cancelled, Superseded) and contract `RouteRefusal_Shape("inactive invoice")` |
| C. The new denial mode renders every denial as the uniform 404 | the `UniformNotFoundForRecord` arm of `Deny` → `true ? UniformRecordNotFound : …` | **8 failed**: `UnknownRights_403SystemFailure`, `StepZero_Refuses_403` ×4, contract `FilterRefusal_Shape` ×3 (flagged external, step 0 fault, rights not answered) |
| Restored | originals copied back | **51 passed** |

## 7. Tests and commands

- `dotnet build src/server/api/Sprk.Bff.Api/`, `dotnet build tests/unit/Sprk.Bff.Api.Tests/`, `dotnet build tests/Spaarke.ArchTests`: 0 warnings, 0 errors.
- New: `tests/unit/Sprk.Bff.Api.Tests/Api/Finance/InvoiceFileUploadTests.cs` (36: the stored row, every refusal with "nothing staged", Inactive and Active statuses, store failures with `Retry-After`, the caller token) and `tests/integration/contract/Api/Finance/InvoiceFileUploadContractTests.cs` (15: the 201 shape and `Location`, every row of the error table with its code field and `Retry-After`, the byte-identical 404). The contract file also holds the shared `InvoiceFileUploadSeed` helpers, so the KEEP-path test does not depend on the unit file.
- Changed: `StoredFileEndpointsTests.Host` registers the in-memory entity service and the size option, and gains `PostAsync`. `RigAccessDataSource` marks a record in the probe's `Unknown` set as `Faulted`, as the production source does.
- `--filter "FullyQualifiedName~InvoiceFileUpload|FullyQualifiedName~StoredFileEndpointsTests|FullyQualifiedName~InvoiceFilesListTests"`: **124 passed**, 0 failed.
- `--filter "FullyQualifiedName~Sprk.Bff.Api.Tests.Api.Finance|FullyQualifiedName~Sprk.Bff.Api.Tests.Api.Filters"`: **3142 passed**, 0 failed.
- `--filter "FullyQualifiedName~FinanceAuthorization|…FinanceStepZero|…StoredFile|…InvoiceFile|…ExternalInvoiceFileRoutes|…ProcedureEndpoints|…InvoiceCorrectionPrefill|…InvoiceSubmissionEndpointsContract"`: **1236 passed**, 0 failed.
- ArchTests, `--filter "FullyQualifiedName~RouteAuthorization|…FinanceStepZero|…ExternalFileStoreRouteCensus|…StoredFile|…ExternalInvoiceVisibility|…InvoicePdfWriter|…AuthenticationSchemeCensus|…InboundBodyDto"`: **148 passed**, 0 failed.
  - The route ledger needed the size-limit attribute written as `new RequestSizeLimitAttribute(...)`, the classified form.
  - The `StoredFileEndpoints.cs` GovernedFile text now names the list and the upload.
  - `FinanceStepZeroCoverageGuardTests` covers the route through the group flag and the filter marker.
  - `ExternalFileStoreRouteCensusTests` needs no entry, because the route is internal.

## 8. Publish size and CVE (BFF hygiene item 4)

- Method: the 124 §12 procedure. `dotnet publish -c Release src/server/api/Sprk.Bff.Api/Sprk.Bff.Api.csproj -r linux-x64 --self-contained false -o deploy/api-publish`, then `Compress-Archive -CompressionLevel Optimal`.
- Worktrees: fresh `C:\wt123m` at `origin/master` `2dcbe35e34`, and `C:\wt123b` at the branch tip `135760e20f`.
- File counts: master 191, branch 191 (they match).
- Sizes: master 42,583,841 bytes (40.611 MB); branch 42,541,326 bytes (40.571 MB). **Delta −42,515 bytes (−0.041 MB).**
  - The branch is `work/ebilling-r1` plus this task, so the delta is the project's against master, not this task's alone.
  - It is far under +5 MB, 55 MB and 60 MB.
- Packages: no package changed (no csproj or props diff).
- Cleanup: both scratch worktrees were removed with `scripts/worktree/Remove-WorktreeSafe.ps1`.
- CVE: `dotnet list src/server/api/Sprk.Bff.Api package --vulnerable --include-transitive` reports "has no vulnerable packages given the current sources". There is no new HIGH.

## 9. Known limits and findings (not fixed here)

- **K1:** the filter's record check is the shared entity-generic path (`CallerRecordVerdictAsync` over `GetCallerRecordAccessAsync`), the same as on every other `ebilling.invoice.decide` route. The upload's backstop uses the gate variant (`GetCallerRightsForAuthorizationGateAsync`). Any change to the filter's path belongs to all of those routes together, not to this task.
- **K2, orphans while the reconcile job is off:** a refused flip (a hash mismatch, a missing blob, a concurrent change) leaves a Not yet servable row and its blob. Nothing is servable, but they stay until the reconcile job runs, and `Ebilling:StoredFileReconcileEnabled` is off by default. This is 090's behaviour, now reachable from a client route.
- **Finding F-a, doc drift:** the comment on `"ebilling.invoice.decide"` in `src/server/shared/Spaarke.Core/Auth/OperationAccessPolicy.cs` lists the routes that reuse the key, and it does not name this route. Left unedited, to keep `Spaarke.Core` out of this change.
- **Finding F-b, 415 before authorization:** the multipart routes that carry `.Accepts<IFormFile>("multipart/form-data")` (`ProcedureEndpoints` `PUT …/versions/{versionId}/file` and others) answer a non-multipart body with 415 from routing.
  - This was observed for an authenticated caller in this task's first run.
  - It was not checked for unauthenticated callers.
  - Failure scenario: a client that sends JSON gets 415 instead of that route's own 400 code, before any authorization runs.
  - Not a disclosure; reported for the owners of those routes.
- (Superseded by fix round 1, F1-d: the backstop's refusal is now 403 `system_failure`, and the test host can answer the filter and the backstop differently.)

## 10. Fix round 1 (2026-11-23)

Two adversarial reviews. Every F item is fixed; each fix has a test that fails on the version at `b7e5c19c91` and passes after (seeding proofs: the original file was copied back, the tests run, then the fixed file restored and compared byte for byte).

| Finding | Fix | Test (fails before → passes after) |
|---|---|---|
| **F1-a, gate.** The route could add rows to a committed invoice whose commit gate had not passed yet, so a gate resume would skip step 5, count the route's rows against the 50 cap, and flip (or undo the whole submission over) a route orphan. | `SubmissionCommitGate.RunAsync`: the flip loop flips the Invoice PDF row LAST; at the start, before the count, a Servable Invoice PDF answers Passed at once (no count, no step 5, no flip). The partial-flip rule (any Servable → skip step 5) is kept for a PDF not yet Servable. Class and method comments updated. | `InvoiceSubmissionPersisterTests.UploadRouteRows.cs`: `Gate_FlipsTheInvoicePdfLast`, `Gate_ServablePdf_WithARouteOrphan_PassesAndTouchesNothing`, `Gate_ServablePdf_WithMoreThanMaxFilesRows_Passes` (all 3 failed on the old gate). `Gate_PartialFlip_SkipsTheReRun_AndFlipsThePdf` pins the kept rule (passes on both). Every existing gate test passes. |
| **F1-a, route.** | `StoredFileEndpoints.RefuseForInvoiceAsync` (one row read, `ReadRowsForInvoiceAsync`): an Invoice PDF row that is not Servable → 409 `ebilling.invoice.files_pending` + `Retry-After: 30`. An invoice with no Invoice PDF row is not refused for it. | `InvoiceFileUploadTests.PdfNotServable_409FilesPending` (Not yet servable and Removed), contract row `submission files pending` (failed before); `PdfServable_Created` (must not fire). |
| **F1-b, no per-invoice cap.** | Same read: `MaxListedFiles` (50) rows or more, any status → 409 `ebilling.files.limit_reached` (no `Retry-After`). | `FileLimit_49Created_50Refused` (49 → 201; 50 → 409, nothing staged, no write), contract row `file limit reached` (both failed before). |
| **F1-c, malformed multipart → 500.** | `catch (IOException) when (!ct.IsCancellationRequested)` → 400 `ebilling.files.file_required`. An IOException of a cancelled request is not caught; the cancellation pass-through is unchanged. | `TruncatedMultipart_400_FileRequired` (no closing boundary; failed before). |
| **F1-d / F4, backstop rights fault → uniform 404.** | `NoWriteOnParent` from the service maps to 403 `sdap.access.error.system_failure` (as `CallerNotInternal` did). | `BackstopRefuses_403SystemFailure_NothingStaged`, contract `BackstopRefusal_Shape` (both failed before). The test host now answers the two questions differently: `RecordingRightsProbe.SingleQuestionAnswersNone` makes the single rights question (the backstop) answer None while the filter's access-data seam still reads the grant. |
| **F1-e, Inactive race.** | After `ReadFormAsync`, before the precheck and stage, `RefuseForInvoiceAsync` runs again from fresh reads: the state, then `files_pending` and the limit. | `InactiveAfterTheBody_409_NothingStaged` (`StoredFileDataverse.BeforeRetrieve` flips the invoice to Rejected before the second read; failed before). |
| **F2, contract test rows.** | Contract test gains the owner-refusal rows (409 `record_owner_no_source`, 503 `record_owner_direct_team_unavailable`, `reasonCode`), the two new 409 rows and the backstop 403. | The owner rows pin existing behaviour (pass on both); the others failed before. |
| **F2, `ContentLengthOverLimit_413` could not fail.** | The test now sends a valid one-file body of a few bytes that declares `Content-Length: 3 MB`, and asserts the errorCode. Only the pre-check can refuse it. | Seeding proof: with the pre-check disabled (`if (false && request.ContentLength > …)`) the test fails, `Expected 413, but found 201`; restored, it passes. |
| **F2, `ContentTypeTooLong_400`.** | Asserts `errorCode` `ebilling.files.file_required`. | — |
| **F2, duplicated name truncation.** | `StoredFileSchema.StoredName(name)` is the one rule; `StoredFileService.BuildRow` and the 201 `fileName` both use it. | `LongName_201FileName_IsTheRowsName` (450 characters → the row's 400; a refactor, so it passes on both). |
| **F1-a follow-up, gate read cap** (found in this round). The Servable-PDF check read the PDF from the capped read of every row (at most 51, in no set order). Concurrent uploads that all pass the route's check at 49 rows can push an invoice past 51 rows; a later resume of a gate that had passed could then miss the PDF, count more than 50 rows and undo a valid submission. | `RunAsync` decides "the Invoice PDF is Servable" from its own read, BEFORE the capped read: `StoredFileService.ReadRowsForInvoiceAsync` gained an optional `purpose` that adds a server-side `sprk_purpose` condition (the PDF's id cannot be derived: an upload's is `ForUpload`, a Submit Invoice copy's is `ForSourceVersion` with the source's version). A fault of that read throws, like the row read after it: the job retries. | `Gate_ServablePdf_PastTheCappedRead_PassesAndTouchesNothing`: 55 route rows whose ids sort before the PDF's, and the fake store answering as Dataverse does (new opt-in `StoredFileDataverse.ApplyQueryFilters`: Equal/Null conditions and `TopCount` over the id-ordered rows), so the PDF is past the 51-row cap. Seeding proof: on the gate at `db03b8cfa2` it fails (not Passed); restored, it passes. `Gate_PdfReadFault_Throws_NothingUndone` pins the fault path. |
| **Hardening, filter mode.** | `FinanceDenial.UniformNotFoundForRecord` inverted: only `InternalCallersOnlyReasonCode` and `SystemFailureReasonCode` are 403; every other deny reason is the uniform 404. The upload route is still the only route in this mode. | `FinanceUniformNotFoundForRecordTests.AnyOtherReason_IsTheUniform404` (a table privilege not held; was 403 `insufficient_privilege`, failed before); the other direction: `SystemFailure_Is403`, `StepZero_Is403` ×3, `NoRights_IsTheUniform404`, `Allowed_ReachesTheHandler`. |

**Tests and commands (fix round 1).**
- `dotnet build` of `src/server/api/Sprk.Bff.Api/`, `tests/unit/Sprk.Bff.Api.Tests`, `tests/Spaarke.ArchTests`: 0 warnings, 0 errors.
- `--filter "FullyQualifiedName~InvoiceFileUpload|…StoredFileEndpoints|…InvoiceFilesList|…SubmissionCommitGate|…InvoiceSubmissionPersister|…FinanceAuthorizationFilter|…FinanceUniformNotFoundForRecord|…StoredFileService|…InvoiceSubmissionProcessingFlow|…InvoiceSubmissionAfterSteps|…FinanceStepZero"`: 833 passed, 0 failed.
- `--filter "FullyQualifiedName~Sprk.Bff.Api.Tests.Services.Finance|…Services.Jobs.Handlers.InvoiceSubmission|…Api.Finance|…Api.Filters"`: 8705 passed, 0 failed.
- ArchTests `--filter "FullyQualifiedName~RouteAuthorization|…FinanceStepZero|…StoredFile|…ExternalFileStoreRouteCensus|…InvoicePdfWriter"`: 126 passed, 0 failed.
- After the gate read-cap fix: `--filter "FullyQualifiedName~SubmissionCommitGate|…InvoiceSubmissionPersister|…InvoiceFileUpload|…StoredFileService"`: 543 passed, 0 failed; ArchTests `StoredFile|ExternalFileStoreRouteCensus|InvoicePdfWriter`: 19 passed, 0 failed.

**Found in passing (not changed; out of scope).** The same narrow catch around `ReadFormAsync` (only `InvalidDataException` / `BadHttpRequestException`), so a truncated multipart body is an unhandled `IOException` (500) there too:
- `src/server/api/Sprk.Bff.Api/Api/Finance/InvoiceSubmissionHttp.cs:60`
- `src/server/api/Sprk.Bff.Api/Api/Finance/ProcedureEndpoints.cs:543`
- `src/server/api/Sprk.Bff.Api/Api/Finance/RateCardImportEndpoints.cs:385`

**Known limits left.**
- **Stage-to-flip window.** The post-body re-read narrows the race, but the invoice can still become Inactive (or a submission can begin storing) between that read and the flip. A file added then is stored under an invoice that just closed.
- **Lost flip response.** If the flip's write lands but its response is lost, 090's service reads a concurrency answer and the route answers 503 while the row is already Servable (090 service, K4). A retry adds a second copy.
- **`Path.GetFileName` on Linux** does not strip a Windows backslash path, so a client that sends `C:\dir\file.csv` as the part's file name has that whole string stored as the name.
- **A Removed Invoice PDF** (an undo in progress or held) also answers `files_pending`. The invoice is being removed; once the undo completes the invoice is gone and the answer becomes the 404. While an undo is held for operator action, the answer stays `files_pending`.

## 11. Fix round 2 (uac-r2 condition 1)

**Defect.** The 50-file limit was checked only by reads before the write. Two concurrent uploads at 49 rows both passed, and the invoice reached 51 rows. From then on `GET /api/finance/invoices/{id}/files` answered 503 for that invoice permanently, because it treats more than `MaxListedFiles` rows as a fault.

**Fix** (`StoredFileEndpoints.UploadInvoiceFile`, new step (5), with `RecountAfterUploadAsync` and `RemoveUploadedFileAsync`; the removal itself is `StoredFileService.RemoveOwnUploadAsync`):
- After `UploadForCallerAsync` answers Created, the invoice's rows are counted again with the same capped read: `ReadRowsForInvoiceAsync(invoiceId, MaxListedFiles + 1, …)`.
- At most 50 → 201, as before.
- More than 50 → this request's own file is removed, and the answer is 409 `ebilling.files.limit_reached`, the existing code, with no `Retry-After`. The removal reuses the submission undo's steps in its order (`SubmissionCommitGate.CompensateAsync`), with no new delete logic:
  1. the row is read for its row version;
  2. it is withdrawn with `WithdrawForUndoAsync` (Removed, so it is never served again);
  3. its blob is deleted with `DeleteBlobForUndoAsync`;
  4. the row is deleted (`IGenericEntityService.DeleteAsync`, the single-row form of the undo's `DeleteRequest`; a row already gone counts as done).
  - Follow-up after uac-r2's approval: these steps live in `StoredFileService.RemoveOwnUploadAsync(fileId, ct)` (internal), on the row's writer. The endpoint calls it with `CancellationToken.None` and no longer deletes a Stored File row itself. The service logs the step that failed and rethrows; the endpoint maps the fault to the 503.
- Concurrent uploads that are all over the limit each remove their own file, so more than one may be removed. Each can send its file again.
- The re-count read faults → this file is removed in the same way, and the answer is 503 `file_store_unavailable` with `Retry-After: 30`. The route's 503 means nothing was stored, so a retry does not add a second copy.
- The removal faults (after an over-limit count or a faulted count) → 503 `file_store_unavailable` with `Retry-After: 30`. The fault is logged with the step that failed.
- Step (5) runs on `CancellationToken.None`. The row already exists, so a cancelled request must not skip its removal. This is the rule `StoredFileService.FlipToServableAsync` and the replace path follow for their committed steps.
- The ordering is documented on `UploadInvoiceFile` (step 5) and on both helpers. The `WithdrawForUndoAsync` comment now names its callers. The route description adds the after-the-write 409.

**Residue on failure.**
- **The removal fails after the withdraw** (blob delete or row delete): the row stays as Removed, never Servable. Its blob may stay too. The invoice can then stay over 50 rows, so the list answers 503 for it. This is the state before this fix, now reachable only when a concurrent over-limit upload coincides with a store or Dataverse fault. The reconcile job (off by default, K2) or an operator removes the row.
- **The read or the withdraw fails:** the row may stay Servable, under an invoice that may be over the limit. The client was answered 503 and may retry; a retry then meets the limit check before the write (409) or adds a second copy. This is the same lost-response class as the "Lost flip response" limit in section 10.
- **The re-count read faults and the removal succeeds:** nothing is left, and the client's retry is clean.

**Tests** (`tests/unit/Sprk.Bff.Api.Tests/Api/Finance/InvoiceFileUploadTests.cs`, "Fix round 2"):

| Test | What it pins |
|---|---|
| `Race_OverLimitAfterTheWrite_409_OwnFileRemoved` | 49 rows (Invoice PDF + 48). `Gateway.BeforeOp` lands a concurrent upload's row when this request stages its bytes, after both checks before the write, so 50 rows exist when this file's row lands. Result: 409 `limit_reached` with no `Retry-After`; the invoice ends at exactly 50; this file's row and blob are gone; the concurrent row stays; the withdraw update ran. |
| `Recount_48Before_Created_RowStays` (must not fire) | 48 rows before, 49 after → 201; the row is Servable, its blob is present, and no delete ran. The boundary (exactly 50 after the write → 201) stays pinned by `FileLimit_49Created_50Refused`. |
| `RecountFault_503_OwnFileRemoved` | The stored-file query faults from the staging write on, so only the re-count faults → 503 `file_store_unavailable` with `Retry-After`; this file's row and blob are removed, and only the Invoice PDF is left. |
| `RemovalFault_503_RowLeftRemoved` | The race, with blob deletes faulting → 503 with `Retry-After`; this file's row is Removed, never Servable (the residue above). |

Contract (`tests/integration/contract/Api/Finance/InvoiceFileUploadContractTests.cs`): `RouteRefusal_Shape` gains the row "file limit reached after the file was stored", 409 `ebilling.files.limit_reached` with no `Retry-After`, using the same `Gateway.BeforeOp` race.

**Seeding proofs.** Run with `dotnet test tests/unit/Sprk.Bff.Api.Tests --no-build --filter "FullyQualifiedName~InvoiceFileUpload"` (69 tests). Each version was copied over `StoredFileEndpoints.cs` and built; at the end the fixed file was restored and compared byte for byte (`cmp`: identical).

| Version of `StoredFileEndpoints.cs` | Result |
|---|---|
| Fix round 1 (`c15d510af3`, no re-count) | **4 failed**, 65 passed: the race, the re-count fault, the removal fault and the contract row |
| Must fire: re-count removed (`if (await RecountAfterUploadAsync(` → `if (fileId == Guid.Empty && await RecountAfterUploadAsync(`) | **4 failed**, 65 passed: the same four |
| Boundary: `count <= MaxListedFiles` → `count < MaxListedFiles` | **1 failed**: `FileLimit_49Created_50Refused` (exactly 50 after the write must stay 201) |
| Restored | **69 passed** |

The must-not-fire test passes on every version.

**Commands (fix round 2).**
- `dotnet build` of `src/server/api/Sprk.Bff.Api/`, `tests/unit/Sprk.Bff.Api.Tests` and `tests/Spaarke.ArchTests`: 0 warnings, 0 errors.
- Unit tests, `--filter "InvoiceFileUpload|StoredFileEndpoints|InvoiceFilesList|StoredFileService|SubmissionCommitGate"`: 332 passed, 0 failed.
- ArchTests, `--filter "RouteAuthorization|StoredFile|InvoicePdfWriter"`: 109 passed, 0 failed (at `d12b115f7a`). This filter did not include `WriterOnlyTablesDeleteGuardTests`; see the follow-up below.

**Follow-up (after uac-r2's approval): the removal moves to the service, and the ArchTest guards.**
- **Found:** `WriterOnlyTablesDeleteGuardTests` (field-mapping 048, D-204) is a closed list. Every BFF member that sends a delete through `IGenericEntityService.DeleteAsync` (or the two other scanned delete APIs) must either ask `RecordTableRules.RefuseWriterOnlyTableDelete` or be listed with its fixed table. The fix-round-2 endpoint helper was neither, so this guard failed at `d12b115f7a`. The filter `RouteAuthorization|StoredFile|InvoicePdfWriter` does not select it, so the run above missed it.
- **Fixed:**
  - `StoredFileService.RemoveOwnUploadAsync` is added to the guard's `FixedTableDeletes` list. Its table is `StoredFileSchema.Entity`, fixed in code, the same reason as the listed `StoredFileReconcileJob.DeleteRowAsync`.
  - Must-fire: before the entry, the guard failed and named `StoredFileService.RemoveOwnUploadAsync`; with the entry, it passes.
- **Guards that touch Stored File writes or deletes:** found by grepping `tests/Spaarke.ArchTests` for `StoredFile`, `sprk_storedfile` and `DeleteAsync`.

  | Guard | What it checks | Effect of the move |
  |---|---|---|
  | `InvoicePdfWriterGuardTests` | Stored File writers: Entity, Create, Update and Upsert requests and `IGenericEntityService` Create, Update and Upsert | A delete is not a write primitive there, and the withdraw is the approved `WithdrawRowsAsync`; it flags neither the service-side delete nor the old endpoint one |
  | `WriterOnlyTablesDeleteGuardTests` | Every delete through a scanned API | Listed now, as above |
  | `RecordOwnerAssignmentCensusTests` | Owner writes per file and table (`sprk_storedfile` in `StoredFileService.cs`: the one `BuildRow` create) | No owner write added |
  | `HardDenyOwnershipQuestionGuardTests` | The `BuildRow` owner question | Unchanged |
  | `StoredFileRolePrivilegeGuardTests` | Security-role privileges on `sprk_storedfile`, not code | Unaffected |
  | `ExternalFileStoreRouteCensusTests`, `ExternalDocumentNarrowingGuardTests`, `ExternalInvoiceVisibilityGuardTests`, `AccessTableReaderScanTests`, `BillingExportClaimWriterOnlyGuardTests` | Routes, reads and the export claim | No new route, read or claim |
  | `DryRunNoWriteGuardTests`, `RemovalSweepSharedRemovalGuardTests` | Matched only on `DeleteAsync` (no Stored File reference) | Pass |

- **Runs:**
  - ArchTests `--filter "RouteAuthorization|StoredFile|InvoicePdfWriter|WriterOnlyTablesDelete|RecordOwnerAssignmentCensus|HardDenyOwnershipQuestion|DryRunNoWrite|RemovalSweepSharedRemoval|AccessTableReaderScan|ExternalFileStoreRouteCensus|ExternalDocumentNarrowing|BillingExportClaimWriterOnly|ExternalInvoiceVisibility"`: 254 passed, 0 failed.
  - ArchTests `RouteAuthorization|StoredFile|InvoicePdfWriter`: 109 passed, 0 failed. `WriterOnlyTablesDelete` alone: 3 passed, 0 failed.
  - Unit tests, `--filter "InvoiceFileUpload|StoredFileEndpoints|InvoiceFilesList|StoredFileService|SubmissionCommitGate"`: 332 passed, 0 failed.
  - Builds of the BFF, the unit tests and the ArchTests: 0 warnings, 0 errors.
- **No separate service test:** the route tests drive the real `StoredFileService` over the fakes. `Race_…` covers the removal's withdraw, blob and row; `RemovalFault_…` covers a failed step leaving the row Removed; `RecountFault_…` covers the removal after a faulted count.

**Not tested here.** No test cancels a request between the write and step (5). The test host has no seam to cancel a request at that point. The rule is structural instead: every call in step (5) passes `CancellationToken.None`, and the request's `ct` is not in scope in either helper.

## 12. Sign-offs and merge (2026-11-23)

**uac-r2.**
- Approved the task at `c15d510af3` with two conditions.
- Condition 1 was the 50-limit race. It was fixed in fix round 2, and uac-r2 approved the fix diff at `d12b115f7a`. Its one nit was to move the row delete into `StoredFileService`; `5bb6965b77` does that.
- Condition 2 stays open on the deploy step, below.
- uac-r2 accepted the access known limits by message; their detail is not recorded in this public repo.

**word-add-in-r1.**
- Agreed the contract changes at `b7e5c19c91` and at `c15d510af3`.
- Was sent the fix round 2 row change at `d12b115f7a` (no new codes).

**Merged** into `work/ebilling-r1` as `72c416e948` (`--no-ff`; branch tip `5bb6965b77`).

**Deploy condition (uac-r2 condition 2, open).** 123's BFF change goes into a later dev deploy. It is not part of the deploy at `9df4e5c819`. After the deploy that carries 123, run a read-only check on dev:
- count the invoices whose Invoice PDF is Servable and that still hold a non-PDF row from their submission's commit that is Not yet servable;
- send the count to uac-r2, with any ids by message only.

A count of zero closes K1 (the transitional part-flip limit). Any hit is filed, without ids in the repo.

**Process note.** One adversarial reviewer ran the whole ArchTests suite once, against the project rule; all 2722 passed. A guard the fix-round filters did not select (`WriterOnlyTablesDeleteGuardTests`) failed on `d12b115f7a` and was fixed in `5bb6965b77` (§11). On the merged work branch (`72c416e948`), the builds have 0 errors. The ArchTests filter `Finance|StoredFile|Writer|Delete|RouteAuthorization|Ownership|Submission|ExternalFileStore|InvoicePdf` passed 369, failed 0. The unit filter `InvoiceFileUpload|StoredFile|InvoiceFilesList|SubmissionCommitGate|InvoiceSubmissionPersister|FinanceAuthorizationFilter|FinanceUniformNotFoundForRecord` passed 721, failed 0.

rework: fix-round-2 task 123
