# Task 151a: Mark Paid route and AP export file download

Branch `task/ebill-151a-mark-paid-download`, from `work/ebilling-r1` at `8e7d581b83`. Rigor FULL (bff-api, authorization). Model: Sonnet @ high. Server side of the AP export UI; 151b (views, ribbons, modal) and 151c (Email to AP, settings column) call these routes. Decision EB-D-181.

## Routes (both in `Api/Finance/ApExportEndpoints.cs`, group `/api/finance`, step 0, then the route filter, then `RequireEbillingEnabled`)

### `POST /api/finance/invoices/{invoiceId}/mark-paid`

No body. One invoice per call (the parent POML names a single-invoice route; the 151b modal calls it once per selected invoice and shows each answer). FR-42.

| Answer | When | Body |
|---|---|---|
| 200 | The invoice was Sent to AP and is now Paid | `{ "invoiceId": "<guid>", "status": "Paid" }` |
| 409 | Any other current status (In Review, Approved, Rejected, Returned to Firm, Cancelled, Superseded), and Paid (a repeat is not a move) | ProblemDetails, `errorCode` `invoice.transition_not_allowed`, `currentStatus` (`InReview`, `Approved`, `SentToAp`, `Paid`, `Rejected`, `ReturnedToFirm`, `Cancelled`, `Superseded`) |
| 409 | The stored status is not one the lifecycle knows | `errorCode` `invoice.status_unknown`, `currentStatus` null |
| 409 | The invoice changed on every attempt (retry) | `errorCode` `invoice.concurrent_change`, `currentStatus` |
| 404 | No decide right, no Read, unknown invoice, step-0 refusal, rights-read fault; also an invoice deleted after the rights check | The uniform record-not-found body (`reasonCode` `sdap.access.deny.record_unavailable`) |
| 401 | Not authenticated | |
| 503 | `ebilling.disabled`; or the lifecycle faulted | `errorCode` `ebilling.disabled`, or `ebilling.invoice.payment_unavailable` ("check the invoice's status and try again") |

The move is `IInvoiceLifecycleService.TransitionAsync(invoiceId, Paid)` and nothing else: no approval write, no Decision Record, no other side effect. The status codes in 409 are the lifecycle's own reason strings (`InvoiceTransitionReasons`); the route adds none. A refused move writes nothing (the tests assert the lifecycle made no conditional write). `AlreadyInTarget` is mapped to the not-allowed 409 on purpose: the parent POML says any status other than Sent to AP, Paid included, is refused, and a repeat call did not move the invoice.

### `GET /api/finance/export-batches/{batchId}/files/{format}` (`format` = `csv` or `json`, any case)

| Answer | When |
|---|---|
| 200 | The stored file, byte for byte as written at export. `Content-Type` `text/csv; charset=utf-8` or `application/json; charset=utf-8`; `Content-Disposition: attachment; filename=ap-export-{batchId}.{format}`; `X-Content-Type-Options: nosniff`, `Cache-Control: no-store`, sandbox CSP. One `StoredFileDownloaded` audit event. |
| 400 | `ebilling.export.invalid_format` |
| 404 | No Read on the batch, unknown batch, step-0 refusal, rights-read fault: the uniform record-not-found body |
| 404 | `ebilling.export.file_not_available`: the batch has no servable file of that format (the batch did not complete, or the format was not requested, or the row/blob is missing or Malicious). The caller can read the batch, so this leaks nothing |
| 409 | `file_scan_pending` (no scan verdict yet) |
| 503 | `ebilling.disabled`; `file_store_unavailable` (store not configured, or a storage fault) |

The file is never regenerated (a test changes the invoices after export and the download is unchanged). The filename is built from the batch id, not from the stored name. The batch's file list comes from `ApExportService.GetBatchAsync` (Completed batch, Servable, purpose Export file); the row is re-read and checked against the batch and purpose, then `StoredFileService.JudgeServableAsync` (own path, scan verdict) and the shared streaming helper.

## Authorization

- **Mark Paid:** `ApExportAuthorization.PaymentRouteTargets(context, "invoiceId")`: the decide right (`ebilling.invoice.decide`, Write) and `finance.read` (Read) on the invoice in the path, each asked of Dataverse as the caller, declared on the route with the uniform-404 denial. It lives in `ApExportAuthorization`, the one seam, so a later dedicated export or payment right changes that class only. It does NOT ask for Create on `sprk_billingexport` (the brief names "the decide right plus `finance.read`"; paying an invoice creates no batch). Say so if the owner wants the table privilege required too: one more `FinanceAuthorizationCheck.CallerPrivilege` in that method.
- **Download:** Read on the batch (`BatchReadTargets`, shared with the batch read route), uniform 404 otherwise. **Deviation to confirm:** the brief says "same authorization as export" and "a batch the caller can't read gives the uniform 404". Read on the batch is what the batch read route needs to list the files and what 090's `GET /files/{fileId}/content` needs to serve the same bytes today, so requiring Create on the batch table here would add no protection (a reader can fetch the same file through 090's route) and would hide the Billing Export form's Download action from read-only users (151b). If the owner wants it stricter, the stricter rule must also be put on 090's route for export files, which is a change to 090.

## Component justification (CLAUDE.md section 11)

**Mark Paid route**
1. Existing: `InvoiceLifecycleService` (014) is the only writer of invoice status and already allows Sent to AP to Paid; `ApExportAuthorization` (150) and the Finance route filter own the rights; no mark-paid route exists (`grep "mark-paid|MarkPaid"` in `src` and `tests` found none).
2. Extension: a route in the existing `ApExportEndpoints.cs` calling the existing lifecycle service, authorized by one new method on the existing seam; no new service, DI registration, endpoint file or package. A separate endpoints file (the parent POML's `InvoicePaymentEndpoints.cs`) would also have meant a route-ledger and endpoint-count change for three small handlers.
3. Cost of doing nothing: finance users cannot record payment, so invoices stop at Sent to AP (FR-42 and success criterion 7 fail).

**Download route**
1. Existing: 090's `GET /files/{fileId}/content` serves any Stored File to a caller with Read on its parent. It needs a file id (only the batch read lists it), sends no filename, and serves JSON as `application/octet-stream` (the allow-list has no `application/json`).
2. Extension: the new route reuses 090's rule (row, own path, scan verdict, audit, hardening headers) through one extracted helper, `StoredFileEndpoints.StreamServableAsync`; `GET /files/{id}/content` calls the same helper and behaves as before (its tests are unchanged and pass). Extending 090's route with a filename/type override was rejected: it has no batch or format and would carry export-specific knowledge into a generic route.
3. Cost of doing nothing: the Billing Export form and the export modal (151b) cannot offer "Download CSV / JSON" as a single call by batch and format, and the JSON download would arrive as an untyped, unnamed octet stream.

## Placement, publish size, CVE (CLAUDE.md section 10)

- **Placement:** the BFF, `Api/Finance`: both routes answer a user action as the caller (bff-extensions criteria B1 caller identity, B4 live request). ADRs: 001 (Minimal API), 008 (endpoint filters), 019 (ProblemDetails with stable codes), 002 (the lifecycle service owns status), 018 (kill switch after authorization). No background work, no AI, no package.
- **Publish size.** `dotnet publish -c Release -r linux-x64 --self-contained false src/server/api/Sprk.Bff.Api/ -o deploy/api-publish`, `Compress-Archive -CompressionLevel Optimal` over `deploy/api-publish/*`, both from fresh worktrees at short paths: `origin/master` at `081f6f0a7e` (`C:\w151m`) and this branch at `f9011c34a9` (`C:\w151b`); file counts match (192 and 192). Master 40,985,172 bytes (39.09 MB); branch 41,100,964 bytes (39.20 MB). **Delta +115,792 bytes (+0.11 MB).** This is against master, so it also carries the rest of `work/ebilling-r1` that master does not have yet; the two routes themselves are about 200 lines of code, no package. Far under the +5 MB trigger. Both folders deleted after.
- **CVE.** `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No package added or changed.

## Tests

| File | Covers |
|---|---|
| `tests/integration/auth/Finance/MarkPaidAuthorizationTests.cs` | Sent to AP becomes Paid (200, store Paid and inactive, one write); In Review, Approved, Paid, Rejected, Returned to Firm, Cancelled, Superseded each 409 `invoice.transition_not_allowed` with `currentStatus` and no write; a repeat is 409 with one write in all; unknown statuscode 409 `invoice.status_unknown`; a rival writer on every attempt 409 `invoice.concurrent_change`; a lifecycle fault 503 `payment_unavailable`; 401; Read without decide, Write without Read, unknown invoice, rights fault, an invoice gone after the rights check: identical uniform 404, nothing read or written; the four step-0 kinds; kill switch 503 and 404 for an unauthorized caller |
| `tests/integration/auth/Finance/ApExportDownloadTests.cs` | Both formats download the stored bytes with type and filename and hardening headers; never regenerated; 400 invalid format; json-only batch has no CSV; a batch that is not Completed advertises no file (no blob opened); scan pending 409; 401; no Read on the batch / unknown batch: uniform 404 and no blob call; the four step-0 kinds; kill switch |
| `tests/integration/auth/Finance/ApExportRoutesHost.cs` | Test host: the real routes, filter, authorization and kill switch; the real `InvoiceLifecycleService` over the world's invoice rows (so the 409 codes are the lifecycle's own) |

Why `InvoiceLifecycleService` is real and not the world's `WorldLifecycle`: that stand-in only moves Approved to Sent to AP; the Mark Paid contract is the lifecycle's matrix and codes.

### Seeding proof (authorization gate only; each mutation reverted, tests red, restored; restored files diffed identical)

| Mutation | Red |
|---|---|
| Decide check removed from `ApExportAuthorization.PaymentRouteTargets` | 1: `NoDecideRight_Is404_AndNothingIsReadOrWritten` |
| Read check removed from `PaymentRouteTargets` | 1: `WriteWithoutRead_Is404_AndNothingIsReadOrWritten` |
| Authorization filter removed from the download route | 6: `NoReadOnTheBatch_Is404_AndNoBytes`, the four step-0 cases, the kill-switch case (an unauthorized caller got the 503) |

"Fails before the change" for the new routes is by construction (they do not exist before this task); the seeds show each guard has teeth.

## Gate

| Check | Result |
|---|---|
| 5 test projects build (`Sprk.Bff.Api.Tests`, `Spaarke.ArchTests`, `Sprk.Bff.Api.IntegrationTests`, `Spe.Integration.Tests`, `Sprk.Provisioning.ControlPlane.LoadTests`) | 0 errors |
| New tests (`MarkPaidAuthorizationTests`, `ApExportDownloadTests`) | 39 passed (24 + 15), 0 failed |
| Unit filter `ApExport / Finance / Invoice` | 6,122 passed, 0 failed, 5 skipped |
| Full ArchTests | 1,868 passed, 1 failed (ISS-005 bounded-time guard, flaky, #2469); ISS filter re-run alone 187 / 0 |
| `Sprk.Bff.Api.IntegrationTests` | 199 passed, 0 failed, 8 skipped |
| `Spe.Integration.Tests` | 353 passed, 0 failed, 23 skipped |

The first unit-filter run had 19 failures in 150's `ApExportAuthorizationTests` route tests: its test host did not register `IInvoiceLifecycleService` and `StoredFileService`, which the two new routes take as handler parameters. Fixed by registering the world's two instances in that host (3 lines); all pass.

## Not done / for the owner

- Mark Paid is one invoice per call. A batch variant (up to 100 ids with a per-invoice outcome) is not built; the parent POML names the single-invoice route. If 151b prefers one call per selection, say so: it is a small addition over the same lifecycle call, but the rights would then be asked per id inside the handler (the route filter reads one id from the path).
- Payment writes no Decision Record or approval row (the parent POML and FR-42 ask only for the status move). If the owner wants a payment recorded as a decision, that is a new requirement.
