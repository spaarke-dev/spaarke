# 095 from-email: wire contract (pinned 2026-10-27 for word-add-in-r1)

> **Agreed by word-add-in-r1 at 717cf238d9 (2026-10-27).** Changes 2026-10-28 (095 review round 1, for word-add-in-r1's agreement): firm details out of K (sections 2, 4, 5); EB-D-165 rows (section 5); new 422 `attachment_not_preserved` and two body-shape keys (section 6); `firmId` and `poNumber` rules (section 2); 500 `OFFICE_AUTH_ERROR` and the firms route's 404 (sections 6, 8). Earlier changes: EB-D-160 (re-attempt after Failed, section 5), agreed by word-add-in-r1 2026-10-27; `marks_invalid` reason `invoice_not_pdf` (2026-10-27, from their review).

Changes 2026-10-28 (094 review round 5, for word-add-in-r1's agreement): 503 `earlier_attempt_being_removed` (sections 5, 6, 9); 422 `email_save_failed` and `document_unavailable` as synchronous refusals before step A, where the submit answered a 503 before (section 6); the `record_unavailable` row covers the firm and record checks (section 6); a race loser's refusal says the filing is kept (section 6, "After step A"); step A's re-file answers only section 6 codes, and `storage_not_configured` refuses before step A (section 6); outcome code `source_changed` (section 9).

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
  "firmId": "8b0c6f1e-4c2a-4f7e-9a51-2f3d1c0e9b77",
  "firmInvoiceNumber": "INV-4711",
  "poNumber": null,
  "mode": "New",
  "invoiceId": null
}
```

**Firm fields (OP-1, settled 2026-10-27 from D-91 / D-106; same meaning as the internal intake's):**

| Field | Type | Required | Rule | Refusal |
|---|---|---|---|---|
| `firmId` | GUID (`sprk_organization` id) | yes | Must be linked to `targetEntity` (D-106). For `Correction` it must equal the old invoice's firm. | A firm id that does not exist: 403 reasonCode `sdap.access.deny.insufficient_rights` (the Finance filter's AppendTo check on the firm runs first; Changed 2026-10-28). 400 `ebilling.submission.firm_invalid`: the empty GUID, or an inactive firm (Changed 2026-10-28). 403 `ebilling.submission.firm_not_linked`; Correction mismatch: 409 `correction_not_allowed` reason `different_firm` |
| `firmInvoiceNumber` | string, trimmed, 1-100 | yes | Typed by the user (D-91); the tracking number is `{firmInvoiceNumber}.{n}` (D-87). A different number read from the PDF holds the invoice for review, never refuses it. | 400 `ebilling.submission.firm_invoice_number_required` |
| `poNumber` | string or null | no | Trimmed; blank (empty or whitespace) is stored as null (Changed 2026-10-28). Over 100 characters after trimming: the body-shape 400. | none |

The firm fields are **not** part of the idempotency key K (section 4; Changed 2026-10-28, 095 review F2): the same email, record and marks with another firm or number is the same submission with other details, which section 5 answers (409 `document_already_submitted` while its latest attempt is not Failed; a new attempt with the new details after Failed). They stay in the `X-Idempotency-Key` cache binding.

## 3. Response

**202 Accepted**, `Location` = `statusUrl`:

```json
{ "submissionId": "0b9e...", "jobId": "5d2a...", "trackingNumber": "INV-4711.1",
  "statusUrl": "/api/finance/invoice-submissions/0b9e...", "emlDocumentId": "a41c..." }
```

- The first four fields are `InvoiceSubmissionAccepted` (InvoiceSubmissionEndpoints.cs:184; InvoiceSubmissionHttp.cs:112-113). `emlDocumentId` is NEW: the .eml source (fresh, or resolved in 1a).
- `jobId` is the submission job (deterministic, as InvoiceSubmissionService.cs:207), not an Office job: never poll `/api/office/jobs/{id}`. The .eml's save job is not returned; its failure ends the submission Failed (§9.1 step 3).
- **Progress:** `GET statusUrl`, the existing route (InvoiceSubmissionEndpoints.cs:66-74), returns 200 `InvoiceSubmissionView` (InvoiceSubmissionContracts.cs:185-215). `status`: `Received`, `Processing` (not final); `Accepted`, `Rejected`, `Failed` (final). `outcomeReasonCode` is pinned in section 9 (G2). Unreadable or absent: 404 reasonCode `sdap.access.deny.record_unavailable`; read fault: 503 `ebilling.submission.record_unavailable`.

## 4. Idempotency

**Rule: the server derives the operation key; the client cannot choose or collide it.**

- **K** is the §9.1 key: the RFC Message-ID (`OfficeEmailEnricher.ResolveRfcMessageId`, :603); the core record (kind, id); `mode`; `invoiceId` (empty for New); and every mark as (`fileName`, `size`, `contentType`, `mark`), sorted ordinal by fileName, then size, contentType, mark (OP-3). Changed 2026-10-28 (095 review F2): `firmId` and `firmInvoiceNumber` are no longer in K, so another firm or number cannot make a second invoice for one email (section 5). K has no caller (OP-5).
- **Ids:** submission id = `IdOf("ebilling-from-email-submission-" + K)` and job id = `IdOf("ebilling-from-email-job-" + K)` (InvoiceSubmissionPersister.cs:199). This is the 050a scheme (InvoiceSubmissionService.cs:204-207): one row per key, race-safe through the allocator's `AlreadyCreated`.
- **`X-Idempotency-Key`:** the existing header (IdempotencyFilter.cs:112; the add-in sends it, `useSaveFlow.ts:1229`), opaque and optional. It names only the per-user 24 h response cache (:108, :181-207), bound to K plus `firmId` and the trimmed `firmInvoiceNumber` as in OfficeEndpoints.cs:384-386, so a reused header on another body (including another firm or number) is never replayed (Changed 2026-10-28). Send one value per user action; reuse it on retries. Never compared with K.
- A body `idempotencyKey` is not accepted (it is ignored).

## 5. Duplicate replay

| Case | Answer |
|---|---|
| Same caller, same K, latest not Failed/Rejected | 202 with the identical body (same `submissionId`, `jobId`, `trackingNumber`, `emlDocumentId`); a Received row is re-queued (InvoiceSubmissionService.cs:454-490) |
| The same, from the response cache | 202 with the identical body plus `X-Idempotency-Status: cached` (IdempotencyFilter.cs:113) |
| Same K, latest submission ended **Failed** | **A new attempt** (EB-D-160; not while the failed attempt is still being removed: 503 `earlier_attempt_being_removed`, section 6, Added 2026-10-28 (094)): 202 with a NEW `submissionId`, `jobId` and `trackingNumber` (`{firmInvoiceNumber}.{n+1}` through the allocator). Ids derive from K plus a generation; generation 1 is the first attempt's derivation. Two concurrent retries of one Failed K create exactly one new attempt; the other gets its 202 replay |
| Same K, latest submission ended **Rejected** | 409 `ebilling.submission.submission_already_ended` (D-139 stands for Rejected) |
| Same K, other details differ (Invoice part SHA-256; firm or number after OP-1) | 409 `ebilling.submission.document_already_submitted` (:457-467), detail "This email was already submitted as an invoice with other details. Nothing was saved." Applies while the latest attempt is not Failed; after Failed the request is a new attempt carrying the new details (row above). Changed 2026-10-28 (095 review F2): with firm and number out of K this row now catches them; before, they made a different K and so a second invoice |
| Another caller submits the same email and marking, and the latest submission (by someone else) ended **Failed** | **A new attempt created by this caller** (EB-D-165, owner 2026-10-28, uac-r2 recommended): 202 with a NEW `submissionId`, `jobId` and `trackingNumber`, as in the Failed row above; the new submission's creator is this caller. Added 2026-10-28 |
| Another caller, same email, the latest submission (by someone else) is Received, Processing, Accepted or Rejected | 409 `ebilling.submission.document_already_submitted`, detail: "This email was already submitted as an invoice for this record by someone else. Nothing was saved." (names no person or id; 095 review F1-2). Same code as today; only the text changes |
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
| 400 | ValidationProblem (no code) | Body shape: missing field, bad GUID, other entity type, unknown `mode`, `invoiceId` vs `mode`, no RFC `internetMessageId` (OP-2); a null `email.attachments[i]` entry (key `email.attachments[i]`); a `message/*` content type on a mark of `Invoice` or `Supporting` (key `marks[i].contentType`: an attached email cannot be kept byte-for-byte). The last two Added 2026-10-28 (095 review F1, F4) | `errors` {jsonPath: [msg]} | no | FinanceEndpoints.cs:143 |
| 429 | `OFFICE_015` | The Office Save bucket, shared with `/office/save` | `Retry-After` header, `retryAfterSeconds`, `limit` | after Retry-After | OfficeRateLimitFilter.cs:135, :359 |
| 503 | `ebilling.disabled` | eBilling is off | – | no | EbillingEnabledFilter.cs:22 |
| 400 | `ebilling.submission.marks_invalid` NEW | Marks break a rule (below) | `reason`, `fileName` (when one file) | no | §9.1 Marks |
| 422 | `ebilling.submission.marked_file_missing` NEW | Invoice/Supporting mark with no entry or no `contentBase64` | `fileName`, `mark` | no | §9.1 step 0 |
| 422 | `ebilling.submission.email_unidentifiable` NEW | Permanent: email can't be identified; `detail` names the internal upload route | `reason` | no | §9.1 F1-c |
| 503 | `ebilling.submission.email_unavailable` NEW | Graph timeout, 429 or 5xx | `Retry-After` header (Graph's value, else 5) | yes | §9.1 F1-c |
| 503 | `ebilling.submission.record_unavailable` | Transient Dataverse fault. Also (task 094) any firm, firm-link or core-record check of the submission that cannot be completed, and a fault reading the marked files out of the saved `.eml`; this route never sends the internal intake's `firm_unverifiable`. Changed 2026-10-28 (094). Also a transient refusal of step A's filing (a throttled or unavailable Dataverse, or the communication's owner changed concurrently), and an earlier attempt whose recorded source cannot be read (replay). Changed 2026-10-28 (094) | – | yes | InvoiceSubmissionContracts.cs:289 |
| 503 | `ebilling.submission.failed` | The job could not be queued. Also (task 094) any other refusal of step A's filing that section 6 does not list (nothing is filed). Changed 2026-10-28 (094) | – | yes | :290 |
| 409 | `ebilling.submission.document_core_record_mismatch` | The communication is filed under another core record | – | no | :279 |
| 422 | `ebilling.submission.attachment_not_preserved` NEW | A marked file could not be kept byte-for-byte in the saved email (checked after the `.eml` is built, before it is uploaded); nothing is saved or submitted. `detail` names the internal upload route. Added 2026-10-28 (095 review F1) | – | no | OfficeService.SaveCoreAsync |
| 500 | `OFFICE_AUTH_ERROR` | The EntityAccessFilter's access probe faulted. Added 2026-10-28 (095 review F7) | `correlationId` | yes | EntityAccessFilter.cs:304-320 |
| 409 | `ebilling.submission.correction_not_allowed` NEW | Correction refused | `reason`: `invoice_status` (+`invoiceStatus`), `different_firm`, `different_core_record` | no | §9.1 Existing invoice |
| 404 | `sdap.access.deny.record_unavailable` (reasonCode) | Correction: `invoiceId` the caller cannot Write, or absent | – | no | ProblemDetailsHelper.cs:357-367; 097 constraint 1 |
| 409 | RecordOwnerRefused reasonCode | No owner team for the new rows | – | no | InvoiceSubmissionHttp.cs:108 |
| 403 | `ebilling.submission.source_forbidden` | Step 1 (task 094): the caller has no Read on the saved `.eml` or on its communication. Added 2026-10-28 (094) | – | no | InvoiceSubmissionEndpoints.PrepareSourceAsync |
| 404 | `ebilling.submission.document_not_found` | Step 1 (task 094): the saved `.eml` no longer exists. Added 2026-10-28 (094) | – | no | as above |
| 422 | `ebilling.submission.source_communication_mismatch` | Step 1 (task 094): the saved `.eml` is not that communication's. Added 2026-10-28 (094) | – | no | as above |
| 403 | `COMMUNICATION_REFILE_FORBIDDEN` (the communication re-file route's own ProblemDetails, returned as it is) | Step A (task 094): the communication is unfiled and the caller may not file it under the record. Nothing of the submission is written. Added 2026-10-28 (094). Every 403 of step A's filing is this answer; no other code of the re-file route reaches this route (its transient answers are 503 `record_unavailable`, the rest 503 `failed`). Changed 2026-10-28 (094) | as that route | no | CommunicationEndpoints re-file |
| 409 | `ebilling.submission.document_core_record_mismatch` | Also from step A (task 094): the unfiled communication is held under a secure record and cannot be filed here. Added 2026-10-28 (094) | – | no | InvoiceSubmissionEndpoints.SourceFiling |
| 409 | `ebilling.submission.document_already_submitted` | Also (task 094): other bytes are already staged under this attempt (the same email, record and marking submitted with other Invoice or Supporting files). Added 2026-10-28 (094) | – | no | SubmitInvoiceFromEmailOperation |
| 422 | `ebilling.submission.storage_not_configured` | Task 094: the Finance file store is not configured. Added 2026-10-28 (094). Checked before step A, so nothing is filed. Changed 2026-10-28 (094) | – | no | as above |
| 503 | `ebilling.tracking.unavailable` | Task 094: no tracking number could be allocated. Added 2026-10-28 (094) | – | yes | as above |
| 422 | `ebilling.submission.email_save_failed` | Task 094: the saved `.eml` is gone or holds no file when the submission reads its marked files. Nothing is submitted and nothing is filed; `detail` names the internal upload route. The same code is the section 9 outcome when the job finds it. Added 2026-10-28 (094) | – | no | SubmitInvoiceFromEmailOperation |
| 422 | `ebilling.submission.document_unavailable` | Task 094: a marked file is not in the saved `.eml` as marked (missing, or another name or content at its position). Nothing is submitted and nothing is filed; `detail` names the internal upload route. The same code is the section 9 outcome. Added 2026-10-28 (094) | – | no | as above |
| 503 | `ebilling.submission.earlier_attempt_being_removed` | The latest attempt of this email ended Failed (`source_check_failed` or `file_unservable`) and what it committed is still being removed, so no new attempt is made yet. Nothing is written. Once the removal completes, the same request makes the new attempt (section 5). Added 2026-10-28 (094) | – | yes | FromEmailSubmissionService (replay of K) |
| as `/office/save` | `OFFICE_004`/`005`/`006` (400), `OFFICE_020` (409), `OFFICE_022` (403), `OFFICE_012` (502, retry), `OFFICE_INTERNAL` (500) | Passed through from the in-process save | as there | – | OfficeErrorCodes.cs:226-249 |

**`marks_invalid` reasons:** `invoice_count` (zero or more than one Invoice), `unknown_mark`, `inline_attachment`, `unmarked_attachment` (a non-inline entry with no mark), `duplicate_mark` (two marks on one entry), `ambiguous_attachment` (two non-inline entries share all three identity fields; OP-3). `invoice_not_pdf` (the entry marked `Invoice` is not a PDF: its content type does not contain `pdf` and its name does not end in `.pdf`; model 9.1 "PDF only"; added 2026-10-27 at word-add-in-r1's review, matching the 118a component). A `DontSave` mark matching no entry is accepted.

**`email_unidentifiable` reasons:** `no_message_id`; `email_not_found` (Graph 403/404); `earlier_save_unlinked` (duplicate onto an unlinked or task-130 Restricted .eml); `no_communication`; `eml_ambiguous` (none or several candidates after the SHA check). Clients may show a reason and must accept unknown ones.

**After step A (task 094, added 2026-10-28):** a refusal that comes after step A filed the communication keeps the filing, and its `detail` ends with "The communication was filed under the record first; that filing is kept." This includes a concurrent retry that filed and then lost the create race (Changed 2026-10-28, 094 review round 5). Step A runs only after the read-only refusals (step 1, the firm, the core record, the owner, the file store's configuration (Changed 2026-10-28, 094), the read of the marked files out of the saved `.eml`) on every path that creates an attempt, including that retry. Changed 2026-10-28 (094 review round 5): the marked files are read before step A, so a read fault (503 `record_unavailable`), a `.eml` that is gone or holds no file (422 `email_save_failed`) and a marked file the `.eml` no longer carries as marked (422 `document_unavailable`) file nothing.

**Order:** filter refusals (401, 403, 400 shape, 429, 503 disabled) come before any handler refusal. The handler checks marks (400), then bytes (422), then the rest. Nothing is written before a refusal except step A's filing (§9.1).

## 7. Settled points (main session, 2026-10-27)

Field names and codes in this file are binding once word-add-in-r1 agrees (095 POML constraint).

- **OP-1 firm fields:** added above (`firmId`, `firmInvoiceNumber`, `poNumber`), following D-91 and D-106. Not a new behaviour: the internal intake already requires them.
- **OP-2:** `email.internetMessageId` is required; a missing or non-RFC value is the 400 body-shape refusal, and Graph is not asked.
- **OP-3:** marks match attachments on (`fileName`, `size`, `contentType`); K uses (fileName, size, contentType, mark), sorted. Two non-inline attachments equal on all three are 400 `marks_invalid` reason `ambiguous_attachment`.
- **OP-4 (owner, EB-D-160, 2026-10-27):** a re-attempt after **Failed** is allowed on this route (a generation in the id derivation, a new tracking suffix); after **Rejected** D-139 stands (409 `submission_already_ended`). The 050a confirm route keeps D-139 unchanged.
- **OP-5:** K has no caller. The same email, record and marks from a second user is the same submission and answers 409 `document_already_submitted` while its latest attempt is Received, Processing, Accepted or Rejected; it prevents a second invoice for one email. Changed 2026-10-28 (EB-D-165): after another user's attempt ended Failed, this caller's request is a new attempt created by this caller (section 5).
- **OP-6:** the in-process save keeps `/office/save`'s AI defaults; the .eml is an ordinary Document under its own Restricted narrowing, and D7 rules govern its indexing. Channel: `Internal` (R1 supports SPA and Internal; the reserved Outlook value is not enabled in R1).
- **OP-7:** until task 097 merges, `mode: "Correction"` answers 409 `ebilling.submission.correction_not_allowed` with `reason: "not_available"` before any write. After 097 the reasons are `invoice_status`, `different_firm`, `different_core_record`.
- **OP-8:** code is `errorCode ?? reasonCode` (route refusals carry `errorCode`, filter denials `reasonCode`), as the existing finance routes do.

- **G3 (correction prefill):** deferred to task 097, which pins a read of an invoice's firm, core record, status and tracking number for the caller. Until then Correction is `not_available` (OP-7).

**Not on this route:** adding supporting files to an existing invoice. No internal upload route exists today (090 built the service, no route); whether R1 gets one is an open owner question (2026-10-27).

## 8. Firms for a record (G1, built in 095)

`GET /api/finance/core-records/{entityType}/{entityId}/firms`, in the /api/finance group with step 0, internal callers only.

- `entityType`: `Matter` | `Project` | `WorkAssignment` (as `targetEntity.entityType`); anything else is 400 ValidationProblem.
- Gate: the caller's Read on the core record (the same gate the submission applies); no Read or not found: 404 reasonCode `sdap.access.deny.record_unavailable`. The same 404 answers a step-0 refusal and a rights-read fault on this route (the uniform read gate; Added 2026-10-28, 095 review F7).
- **200** `[{ "firmId": "guid", "name": "string" }]`, sorted by name (ordinal, case-insensitive), then id. It holds exactly the firms D-106 accepts: the record's assigned law firms (`sprk_assignedlawfirm1`, `sprk_assignedlawfirm2`; the one definition is `InvoiceSubmissionService.CheckFirmLinkAsync`), each at most once. **No linked firm: 200 `[]`** (the view says no firm is linked to this record).
- A read fault: 503 `ebilling.submission.record_unavailable`; never a partial or empty list on a fault.
- Every `firmId` it returns passes `firm_not_linked` on the from-email route at that moment.

## 9. Submission outcome codes (G2)

`InvoiceSubmissionView.outcomeReasonCode` when `status` is final and not `Accepted`, or while an Accepted invoice is held. Clients must accept unknown codes and show a generic message for them. No server prose is pinned; the add-in localises.

| status | outcomeReasonCode | Meaning |
|---|---|---|
| Failed | `invoice.read.no_invoice` | The file marked Invoice is not an invoice (it is the one file marked Invoice, so no file name is sent) |
| Failed | `invoice.read.unreadable` | The Invoice file could not be read (damaged or protected PDF) |
| Failed | `invoice.read.page_limit` | The Invoice file has too many pages |
| Failed | `invoice.read.service_failed` | Extraction failed after retries |
| Failed | `ebilling.submission.email_save_failed` | NEW (095/094): the .eml save did not finish |
| Failed | `ebilling.submission.document_unavailable` | The marked parts could not be read from the saved .eml |
| Failed | `ebilling.submission.file_malicious` | A marked file failed the malware scan (task 092) |
| Failed | `ebilling.submission.file_scan_timeout` | The malware scan of a marked file did not finish in time (about 19 minutes across retries; task 092). A re-attempt is allowed (EB-D-160). Added 2026-10-27, agreed by word-add-in-r1 |
| Failed | `ebilling.submission.file_unservable` | A marked file could not be stored or verified (task 092) |
| Failed | `ebilling.submission.source_check_failed` | NEW (094 step 5): the source's filing or isolation re-check failed at commit |
| Failed | `ebilling.submission.document_already_invoiced` | Added 2026-10-28 (094 step 3 on the `.eml`): the saved `.eml` was linked to another invoice before the commit |
| Failed | `ebilling.submission.document_rejected` | Added 2026-10-28 (094 step 3): the saved `.eml` was rejected as not an invoice before the commit |
| Failed | `ebilling.submission.document_core_record_mismatch` | Added 2026-10-28 (094 step 3): the saved `.eml` is filed under or held by another record at commit |
| Failed | `ebilling.submission.document_not_found` | Added 2026-10-28 (094 step 3): the saved `.eml` was deleted before the commit |
| Failed | `ebilling.submission.source_changed` | Added 2026-10-28 (094): the saved `.eml` no longer belongs to the communication its marked files were read from (Failed at once), or it kept changing until the processing attempts ran out. A re-attempt is allowed (EB-D-160) |
| Failed | `ebilling.submission.processing_failed` | Any other processing failure |
| Rejected | `ebilling.submission.blocked` | A blocking rule rejected the invoice |
| Rejected | `ebilling.submission.returned` | Returned to the firm |

`ebilling.submission.attachment_not_preserved` (Added 2026-10-28) is a synchronous 422 from the submit (section 6), never an `outcomeReasonCode`: a request it refuses creates no submission.

Replay after `Failed` or `Rejected` follows section 5: after `Failed` the same request makes a new attempt; after `Rejected` it is 409 `submission_already_ended` (EB-D-160).

Added 2026-10-28 (094): after `Failed` with `ebilling.submission.source_check_failed` or `ebilling.submission.file_unservable`, the attempt's commit is removed in the background. Until that removal completes, the same request is a synchronous 503 `ebilling.submission.earlier_attempt_being_removed` (section 6): retryable, nothing written, never an `outcomeReasonCode`. After it completes, the request makes the new attempt.
