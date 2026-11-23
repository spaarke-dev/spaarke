# 123 supporting-file upload: wire contract (pinned 2026-10-27 for word-add-in-r1)

Binding once word-add-in-r1 agrees; a change needs their agreement first. Behaviour: task 123's POML (EB-D-161). Code is `errorCode ?? reasonCode`, as in the 095 contract.

## Route
`POST /api/finance/invoices/{invoiceId}/files`, in the /api/finance group (step 0: internal callers only). `multipart/form-data` with exactly one file part named `file`. The part's file name and content type are stored as given.

## Limits
- **Size:** at most `Ebilling:MaxSubmissionFileSizeMb` MB, the same setting as invoice submission (default 25 MB, ceiling 100 MB). The add-in's own 20 MB cap is inside it. An empty file is refused.
- **Content types:** any. Supporting files are not restricted by type (the Invoice PDF rule applies only to submissions).
- **One file per request.** Send several files as several requests.

## Synchronous
The request stages the bytes, writes the Stored File row and flips it to Servable before it answers (090 `UploadForCallerAsync`). **201 means stored.** A download additionally needs a clean malware-scan verdict, which arrives later; until then (or if Malicious) the internal download route refuses it. The upload itself never waits for the scan and never answers with a scan result.

## 201 Created
`Location: /api/finance/files/{fileId}/content`
```json
{ "fileId": "guid", "fileName": "timesheet.csv", "sizeBytes": 40211 }
```
The file is purpose Invoice supporting file, Firm-visible No, parent the invoice. It is never a `sprk_document`.

## Errors
Final codes as built by task 123 (2026-11-22). The field is `errorCode` for the route's own refusals and `reasonCode` for the filter's (403, 404); read `errorCode ?? reasonCode`.

| Status | Code | When | Retry |
|---|---|---|---|
| 400 | `ebilling.files.file_required` | no `file` part, more than one file part, a single file part not named `file`, a body that is not multipart/form-data (or no body), or a name or content type the store refuses (a content type over 255 characters) | no |
| 400 | `ebilling.files.file_empty` | the file has 0 bytes | no |
| 413 | `ebilling.files.file_too_large` | over the size limit (also the server's request-size limit) | no |
| 401 | (none) | not authenticated | no |
| 403 | `sdap.access.deny.internal_callers_only` (reasonCode) | step 0: flagged-external caller, or no systemuser | no |
| 403 | `sdap.access.error.system_failure` (reasonCode) | step 0 or the rights question could not be answered | yes |
| 404 | `sdap.access.deny.record_unavailable` (reasonCode) | the invoice does not exist, or the caller has no Write on it (one body for both); also an invoice removed between the rights check and the upload | no |
| 409 | `ebilling.invoice.inactive` | the invoice is Inactive: Rejected, Returned to Firm, Cancelled, Superseded **or Paid** (statecode Inactive; see task 123 notes) | no |
| 409 | the owner refusal code (reasonCode) | no owner could be resolved for the file (the platform's one owner-refusal answer, `ProblemDetailsHelper.RecordOwnerRefused`; 503 for its one transient code) | no |
| 503 | `file_store_unavailable` (`Retry-After: 30`) | staging, the row write or the flip failed, the store is not configured, or the invoice's state could not be read; nothing servable was left | yes |
| 503 | `ebilling.disabled` | eBilling is off | no |

The final codes follow the code's existing constants where they exist (task 123 reuses them verbatim and reports any rename here before it merges).

**Differences from the version pinned 2026-10-27 (task 123, for word-add-in-r1's agreement before merge):**
- 503 store code: `file_store_unavailable`, the existing `StoredFileEndpoints.StoreUnavailableErrorCode` (the same file's download and list routes answer it for the same condition), not `ebilling.files.store_unavailable`.
- 409 `ebilling.invoice.inactive` also covers Paid: the code's own Active/Inactive definition is statecode, and Paid is statecode Inactive.
- Added row: 409 owner refusal (the platform's one answer for it; the pinned table had no row for this case).
- 400 `file_required` and 404 cover the extra cases named in their rows. A file part with no content type is stored as `application/octet-stream`.
- `ebilling.files.file_required`, `ebilling.files.file_empty`, `ebilling.files.file_too_large` and `ebilling.invoice.inactive` are new constants (no existing constant has the same meaning; the submission and Procedure routes' size and file codes are scoped to those routes).
