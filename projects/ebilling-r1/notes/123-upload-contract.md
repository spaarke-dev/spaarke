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
| Status | Code | When | Retry |
|---|---|---|---|
| 400 | `ebilling.files.file_required` | no `file` part, or more than one file part | no |
| 400 | `ebilling.files.file_empty` | the file has 0 bytes | no |
| 413 | `ebilling.files.file_too_large` | over the size limit (also the server's request-size limit) | no |
| 401 | (none) | not authenticated | no |
| 403 | `sdap.access.deny.internal_callers_only` | step 0: flagged-external caller | no |
| 403 | `sdap.access.error.system_failure` | step 0 or the rights question could not be answered | yes |
| 404 | `sdap.access.deny.record_unavailable` | the invoice does not exist, or the caller has no Write on it (one body for both) | no |
| 409 | `ebilling.invoice.inactive` | the invoice is Rejected, Returned to Firm, Cancelled or Superseded | no |
| 503 | `ebilling.files.store_unavailable` (`Retry-After`) | staging, the row write or the flip failed; nothing servable was left | yes |
| 503 | `ebilling.disabled` | eBilling is off | no |

The final codes follow the code's existing constants where they exist (task 123 reuses them verbatim and reports any rename here before it merges).
