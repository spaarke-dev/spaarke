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
   1. The invoice's `statecode` / `statuscode`, read app-only. Gone → the uniform 404; statecode Inactive → 409 `ebilling.invoice.inactive`; a read fault or a missing or unknown statecode → 503 (fail closed).
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
- No backstop-only test: the route test host answers the filter and the backstop from one rights grant, so a case where the filter allows and the backstop refuses cannot be staged there. The mapping (`NoWriteOnParent` → the uniform 404) is one switch arm, and 090's `Upload_NoWrite_Refused` covers the service side.
