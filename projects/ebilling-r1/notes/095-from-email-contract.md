# 095 from-email: wire contract (pinned 2026-10-27 for word-add-in-r1)

Binding for 095 and word-add-in-r1; changes need word-add-in-r1's agreement first. Behaviour is model §9.1 (v5.9 FINAL); this pins names, statuses and codes only. Citations: `work/ebilling-r1` @ 786d1ea17a, under `src/server/api/Sprk.Bff.Api/` unless shown. NEW = no existing code to follow.

## 1. Route

`POST /api/finance/invoice-submissions/from-email`, JSON, bearer token; `/api/finance` group, step 0 first (InvoiceSubmissionEndpoints.cs:34-37). Body limit as `/office/save` (none set, OfficeEndpoints.cs:185-205: Kestrel's 30,000,000 bytes; the add-in's 28,000,000 budget, `emailContentCapture.ts:49`, fits). Over: 413, no code.

## 2. Request

Header `X-Idempotency-Key`: optional (section 4). Body (camelCase):

| Field | Type | Req | Rule | Follows |
|---|---|---|---|---|
| `targetEntity.entityType` | string | yes | `Matter`, `Project`, `WorkAssignment` (any case; `sprk_*` logical names too); else 400. `displayName` ignored | SaveRequest.cs:105-123; EntityAccessFilter.cs:116-143; InvoiceFamilyCoreRecordGuard.cs:125 |
| `targetEntity.entityId` | GUID | yes | not empty | SaveRequest.cs:117 |
| `email` | object | yes | exactly `/office/save`'s `email` (EmailMetadata), not a whole SaveRequest | SaveRequest.cs:128-242 |
| `email.internetMessageId` | string | yes (OP-2) | RFC 5322 Message-ID | SaveRequest.cs:181 |
| `email.attachments[]` | array | yes | `attachmentId`, `fileName`, `size`, `contentType`, `contentBase64`, `isInline`, `contentId` | SaveRequest.cs:281-321 |
| `email.selectedAttachmentFileNames` | – | – | ignored: the server overwrites it with `[]` | §9.1 step 1; UploadFinalizationWorker.cs:1033-1045 |
| `marks[]` | array | yes | one per non-inline attachment: `fileName` (string, ordinal), `size` (int64), `contentType` (string, ignore case), each equal to the attachment's value as sent; `mark`: `Invoice` \| `Supporting` \| `DontSave` (exact case) | NEW (§9.1 Marks) |
| `mode` | string | yes | `New` \| `Correction` (exact case) | NEW (§9.1 key) |
| `invoiceId` | GUID or null | Correction | required for `Correction`; absent or null for `New` | NEW |

- **Why `email`, not a whole SaveRequest:** a nested one brings a second `targetEntity` (two client-chosen targets for one decision, the defect SaveRequest.cs:24-40 records) and a body `idempotencyKey`, which replaces the save's own duplicate key (OfficeService.cs:1356-1357) that step 1a relies on.
- **In-process save:** `ContentType=Email`, `TargetEntity=targetEntity`, `Email=email` with `SelectedAttachmentFileNames=[]`, `IdempotencyKey=null`. Other properties (`contentType`, `idempotencyKey`, `aiOptions`, `triggerAiProcessing`) are ignored (OP-6).
- `mark` and `mode` bind as strings, validated in code: an unknown value gets this contract's 400, never a binding failure. A mark matches the non-inline entry with the same `fileName`, `size` and `contentType`. Send `email.isNameSystemDerived: true` (no name box; SaveRequest.cs:241).

```json
{
  "targetEntity": { "entityType": "Matter", "entityId": "6f1c0e8a-2b1d-4c7e-9a52-0d3f1b7e9c41" },
  "email": {
    "subject": "Invoice 4711", "senderEmail": "billing@firm.example",
    "internetMessageId": "<CA+abc123@mail.firm.example>", "body": "<p>Attached.</p>", "isBodyHtml": true,
    "isNameSystemDerived": true,
    "attachments": [
      { "attachmentId": "AAMkAD...1", "fileName": "INV-4711.pdf", "size": 182344, "contentType": "application/pdf", "contentBase64": "JVBERi0x...", "isInline": false },
      { "attachmentId": "AAMkAD...2", "fileName": "timesheet.csv", "size": 40211, "contentType": "text/csv", "contentBase64": "UEsDB...", "isInline": false },
      { "attachmentId": "AAMkAD...3", "fileName": "logo.png", "size": 5120, "contentType": "image/png", "isInline": true }
    ]
  },
  "marks": [
    { "fileName": "INV-4711.pdf", "size": 182344, "contentType": "application/pdf", "mark": "Invoice" },
    { "fileName": "timesheet.csv", "size": 40211, "contentType": "text/csv", "mark": "Supporting" }
  ],
  "mode": "New",
  "invoiceId": null
}
```

Pending OP-1 (not pinned): `firmId`, `firmInvoiceNumber`, `poNumber`.

## 3. Response

**202 Accepted**, `Location` = `statusUrl`:

```json
{ "submissionId": "0b9e...", "jobId": "5d2a...", "trackingNumber": "FIRMF-000123",
  "statusUrl": "/api/finance/invoice-submissions/0b9e...", "emlDocumentId": "a41c..." }
```

- The first four fields are `InvoiceSubmissionAccepted` (InvoiceSubmissionEndpoints.cs:184; InvoiceSubmissionHttp.cs:112-113). `emlDocumentId` is NEW: the .eml source (fresh, or resolved in 1a).
- `jobId` is the submission job (deterministic, as InvoiceSubmissionService.cs:207), not an Office job: never poll `/api/office/jobs/{id}`. The .eml's save job is not returned; its failure ends the submission Failed (§9.1 step 3).
- **Progress:** `GET statusUrl`, the existing route (InvoiceSubmissionEndpoints.cs:66-74), returns 200 `InvoiceSubmissionView` (InvoiceSubmissionContracts.cs:185-215). `status`: `Received`, `Processing` (not final); `Accepted`, `Rejected`, `Failed` (final). `outcomeReasonCode` is display-only and not pinned. Unreadable or absent: 404 reasonCode `sdap.access.deny.record_unavailable`; read fault: 503 `ebilling.submission.record_unavailable`.

## 4. Idempotency

**Rule: the server derives the operation key; the client cannot choose or collide it.**

- **K** is the §9.1 key: the RFC Message-ID (`OfficeEmailEnricher.ResolveRfcMessageId`, :603); the core record (kind, id); `mode`; `invoiceId` (empty for New); and every mark as (`fileName`, `mark`), sorted ordinal by fileName, then by mark.
- **Ids:** submission id = `IdOf("ebilling-from-email-submission-" + K)` and job id = `IdOf("ebilling-from-email-job-" + K)` (InvoiceSubmissionPersister.cs:199). This is the 050a scheme (InvoiceSubmissionService.cs:204-207): one row per key, race-safe through the allocator's `AlreadyCreated`.
- **`X-Idempotency-Key`:** the existing header (IdempotencyFilter.cs:112; the add-in sends it, `useSaveFlow.ts:1229`), opaque and optional. It names only the per-user 24 h response cache (:108, :181-207), bound to K as in OfficeEndpoints.cs:384-386, so a reused header on another body is never replayed. Send one value per user action; reuse it on retries. Never compared with K.
- A body `idempotencyKey` is not accepted (it is ignored).

## 5. Duplicate replay

| Case | Answer |
|---|---|
| Same caller, same K, earlier not Failed/Rejected | 202 with the identical body (same `submissionId`, `jobId`, `trackingNumber`, `emlDocumentId`); a Received row is re-queued (InvoiceSubmissionService.cs:454-490) |
| The same, from the response cache | 202 with the identical body plus `X-Idempotency-Status: cached` (IdempotencyFilter.cs:113) |
| Same K, earlier ended Failed or Rejected | 409 `ebilling.submission.submission_already_ended` (D-139, :469-476); see OP-4 |
| Same K, other details differ (Invoice part SHA-256; firm or number after OP-1) | 409 `ebilling.submission.document_already_submitted` (:457-467) |
| Same header, different K | Not a replay: handled as its own operation |
| Same header, first request still running | 409 `OFFICE_IDEMPOTENCY_CONFLICT`; retry shortly (IdempotencyFilter.cs:255-272) |
| Different marking, mode or invoice | A different K: a new submission (§9.1) |

None of these 409s writes anything.

## 6. Errors

ProblemDetails (ADR-019). Own refusals carry `errorCode` (submission routes, InvoiceSubmissionHttp.cs:115-118); Finance filter denials `reasonCode` (ProblemDetailsHelper.cs:199-215); EntityAccessFilter's 403 both. 409s from §5 also apply. **Client rule: code = `errorCode ?? reasonCode`.**

| Status | Code | When | Extra fields | Retry | Source |
|---|---|---|---|---|---|
| 401 | none, or `OFFICE_AUTH_001`/`_002` | Token missing or invalid | – | after a token refresh | JWT; OfficeAuthFilter.cs:84-112 |
| 403 | `sdap.access.deny.internal_callers_only` (reasonCode) | Step 0: flagged-external caller, or no systemuser | – | no | FinanceAuthorizationFilter.cs:359, :523 |
| 403 | `sdap.access.error.system_failure` (reasonCode) | Step 0 or a rights read faulted | – | yes | :341, :594-597 |
| 403 | `sdap.access.deny.insufficient_rights` (reasonCode) | Finance check: no Read + AppendTo on the core record | – | no | :338 |
| 403 | `OFFICE_009` + reasonCode `insufficient_rights` | EntityAccessFilter, same rights | `entityType` | no | EntityAccessFilter.cs:286-301 |
| 400 | ValidationProblem (no code) | Body shape: missing field, bad GUID, other entity type, unknown `mode`, `invoiceId` vs `mode`, no RFC `internetMessageId` (OP-2) | `errors` {jsonPath: [msg]} | no | FinanceEndpoints.cs:143 |
| 429 | `OFFICE_015` | The Office Save bucket, shared with `/office/save` | `Retry-After` header, `retryAfterSeconds`, `limit` | after Retry-After | OfficeRateLimitFilter.cs:135, :359 |
| 503 | `ebilling.disabled` | eBilling is off | – | no | EbillingEnabledFilter.cs:22 |
| 400 | `ebilling.submission.marks_invalid` NEW | Marks break a rule (below) | `reason`, `fileName` (when one file) | no | §9.1 Marks |
| 422 | `ebilling.submission.marked_file_missing` NEW | Invoice/Supporting mark with no entry or no `contentBase64` | `fileName`, `mark` | no | §9.1 step 0 |
| 422 | `ebilling.submission.email_unidentifiable` NEW | Permanent: email can't be identified; `detail` names the internal upload route | `reason` | no | §9.1 F1-c |
| 503 | `ebilling.submission.email_unavailable` NEW | Graph timeout, 429 or 5xx | `Retry-After` header (Graph's value, else 5) | yes | §9.1 F1-c |
| 503 | `ebilling.submission.record_unavailable` | Transient Dataverse fault | – | yes | InvoiceSubmissionContracts.cs:289 |
| 503 | `ebilling.submission.failed` | The job could not be queued | – | yes | :290 |
| 409 | `ebilling.submission.document_core_record_mismatch` | The communication is filed under another core record | – | no | :279 |
| 409 | `ebilling.submission.correction_not_allowed` NEW | Correction refused | `reason`: `invoice_status` (+`invoiceStatus`), `different_firm`, `different_core_record` | no | §9.1 Existing invoice |
| 404 | `sdap.access.deny.record_unavailable` (reasonCode) | Correction: `invoiceId` the caller cannot Write, or absent | – | no | ProblemDetailsHelper.cs:357-367; 097 constraint 1 |
| 409 | RecordOwnerRefused reasonCode | No owner team for the new rows | – | no | InvoiceSubmissionHttp.cs:108 |
| as `/office/save` | `OFFICE_004`/`005`/`006` (400), `OFFICE_020` (409), `OFFICE_022` (403), `OFFICE_012` (502, retry), `OFFICE_INTERNAL` (500) | Passed through from the in-process save | as there | – | OfficeErrorCodes.cs:226-249 |

**`marks_invalid` reasons:** `invoice_count` (zero or more than one Invoice), `unknown_mark`, `inline_attachment`, `unmarked_attachment` (a non-inline entry with no mark), `duplicate_mark` (two marks on one entry), `ambiguous_attachment` (two non-inline entries share all three identity fields; OP-3). A `DontSave` mark matching no entry is accepted.

**`email_unidentifiable` reasons:** `no_message_id`; `email_not_found` (Graph 403/404); `earlier_save_unlinked` (duplicate onto an unlinked or task-130 Restricted .eml); `no_communication`; `eml_ambiguous` (none or several candidates after the SHA check). Clients may show a reason and must accept unknown ones.

**Order:** filter refusals (401, 403, 400 shape, 429, 503 disabled) come before any handler refusal. The handler checks marks (400), then bytes (422), then the rest. Nothing is written before a refusal except step A's filing (§9.1).

## 7. Open points for the main session

- **OP-1 (blocks the shape): firm and firm invoice number.** Intake needs `FirmId` linked to the record (D-106/D-110; the tracking number is firm-scoped, D-87; InvoiceSubmissionContracts.cs:106) and a firm invoice number refused before any write (D-91; :227); confirm sends `vendorOrgId` + `invoiceNumber` (Services/Finance/InvoiceReviewService.cs:72-85). §9.1 names neither. Add `firmId`, `firmInvoiceNumber`, `poNumber` (codes `firm_invalid` 400, `firm_not_linked` 403, `firm_invoice_number_required` 400), or say how the submission gets them later (`trackingNumber` may then be null). The shapes are frozen, so either needs word-add-in-r1.
- **OP-2: `internetMessageId` required**, so K exists before any Graph call and the header can bind to it. "No Message-ID" then means "the client sent none or not RFC-shaped"; Graph is not asked. Confirm.
- **OP-3: key collision.** The §9.1 key is (file name, mark): two same-name attachments differing only in size or type give one K with their marks swapped. Proposal: key on (fileName, size, contentType, mark) plus `ambiguous_attachment`.
- **OP-4: retry after Failed.** D-139's code answers 409 `submission_already_ended`; §9.1's marker text ("the retry would then reuse a Restricted .eml") implies a new submission, which needs a generation in K.
- **OP-5: caller scope.** K has no caller; the save's duplicate check is per user (OfficeService.cs:445). A second user with the same email, record and marks gets the same submission id with another .eml source, so 409 `document_already_submitted`. Should K include the caller?
- **OP-6: AI options and channel.** The in-process save uses `/office/save` defaults (`TriggerAiProcessing` true), so the own-Restricted .eml is profiled like any save. Channel: `Internal` or the reserved Outlook value? The job refuses unsupported channels (SubmissionReasonCodes.cs:73).
- **OP-7:** what `mode: "Correction"` answers before 097 merges.
- **OP-8: `errorCode` vs `reasonCode`.** The brief asked for `reasonCode`; the code uses `errorCode` on submission refusals and `reasonCode` on filter denials. Pinned per code with the client rule.
