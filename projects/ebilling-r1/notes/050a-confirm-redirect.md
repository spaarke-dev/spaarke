# 050a: the review-confirm route goes through the submission operation (D-134)

Status: implemented and verified. It is waiting for the adversarial review and for uac-r2's review of the re-file (section 4). Rigor is FULL.

## 1. What changed

`POST /api/finance/invoice-review/confirm` no longer creates `sprk_invoice` and no longer enqueues `InvoiceExtraction`. Instead:

1. **`InvoiceReviewService.ConfirmInvoiceAsync`** calls `InvoiceSubmissionService.SubmitExistingDocumentAsync`, the new existing-document entry. It passes:

   | Field | Value |
   |---|---|
   | firm | the request's vendor |
   | core record | the matter |
   | channel | Internal |
   | firm invoice number | the reviewer's number |
   | `ExistingDocumentId` | the document |
   | `CallerObjectId` | the reviewer's oid |

   When the submission is Received, confirm marks the document ConfirmedInvoice and returns the intake's outcome.
2. **`SubmitExistingDocumentAsync`** works like this:
   - It runs the same checks as `SubmitAsync`: the number (D-91), the firm is active, the core-record fields (013), the firm link (D-106 / D-110) and the owner. They share one code path, the new private `DecideAsync`, which `SubmitAsync` now calls too, so the two entries cannot drift apart.
   - It skips the file checks, the upload and the document create.
   - It creates one Internal submission with `sprk_document` set to the confirmed document, then enqueues `InvoiceSubmissionProcessing`.
3. **The submission job (049)** links the document to the invoice it creates. 049 already does this in its transaction. The after-steps then **re-file** the document under that invoice with the secure-record isolation step. The step is the new `InvoiceDocumentRefile`; section 4 covers it.
4. **The response** is `202 {submissionId, jobId, trackingNumber, statusUrl}`. It is rendered by `InvoiceSubmissionEndpoints.ToResult`, so the type is `InvoiceSubmissionAccepted`, the same body the internal submission route returns. The `Location` header is `/api/finance/invoice-submissions/{submissionId}`. A refusal is the intake's ProblemDetails with its `errorCode`.
5. **The confirm route** now carries `.RequireEbillingEnabled()`, after its authorization filter (ADR-018). The submission job fails every submission when eBilling is off, so an ungated confirm would create submissions that can only fail. Reject is not gated.
6. **Reject** refuses with 409 `DocumentLinkedToInvoice` in two cases: the document is linked to an invoice (as before), or it has a confirm submission (new). The confirm submission exists before the job links the document.

ADR-002 / D-19 are met: no confirm path creates `sprk_invoice`, and the submission job's persister is the only invoice creator. The old create, link, reparent, delete-on-failure and enqueue-extraction machinery in `InvoiceReviewService` is deleted. 050b deletes the legacy extraction handler, schemas and options.

## 2. The dedupe key: one submission per document

The key is **a deterministic submission id**: `InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId)`, which is `IdOf("ebilling-confirm-submission-{documentId}")`, the persister's SHA-256 id scheme. The job id is deterministic in the same way.

Why this key:
- **Concurrency-safe without a lock.** Two confirms cannot create two rows. The primary key is unique, and the allocator answers a second create of the same id with `AlreadyCreated`, which covers both a concurrent creator and a lost reply.
- **A retry gets the same answer.** It returns the same submission id, job id and tracking number. It does this whether the first reply was lost or the client simply retried.
- **The job can recognise a confirm submission with no schema change.** The marker is `submissionId == ExistingDocumentSubmissionIdFor(sprk_document)`. An intake upload has a random id, so it never matches.

Alternative rejected: a Redis lock plus a query by document. It needs a lock, compensation deletes and a separate marker column, and it still races on a lost reply.

How a retry is answered:
- The retry reads the earlier submission first.
- If the firm, the core record, the document and the number (trimmed, case-insensitive) all match and a tracking number exists, the retry returns that submission. When it is still Received, the job is re-queued: the job's lock and terminal-state check make a duplicate message harmless.
- Anything else is 409 `document_already_submitted`.

**Nothing is deleted.** There is no compensation:
- A failed enqueue is 503 `ebilling.submission.failed`, and its message tells the reviewer to retry; the retry re-queues the job.
- An allocator reconcile conflict (the same id with another firm or number) is 409 and leaves the row alone.

Costs, listed as open questions in section 9:
- A confirm submission that ends Failed or Rejected blocks a re-confirm of the same document. The same id answers with the earlier submission.
- A document with a confirm submission can no longer be rejected.

## 3. D-91, D-106 / D-110 and the other refusals

All refusals happen before any write. Each row below is tested (section 6).

| Case | Answer |
|---|---|
| Missing or blank number | 400 `ebilling.submission.firm_invoice_number_required`, before any read. The classifier's hint is never used. |
| Vendor is not one of the matter's `sprk_assignedlawfirm1/2` | 403 `ebilling.submission.firm_not_linked` (the existing `CheckFirmLinkAsync` rule, reused through `DecideAsync`) |
| Firm links unreadable | 503 `firm_unverifiable` |
| Inactive or unknown firm | 400 `firm_invalid` |
| No owner team | `OwnerRefused` (409 ProblemDetails, as the submission route returns it) |
| Document missing | 404 `document_not_found` |
| Document already linked to an invoice (confirmed before 050a) | 409 `document_already_invoiced` |
| Document has no stored file (no drive or item pointer) | 422 `document_has_no_file` |
| Document unreadable | 503 `record_unavailable` |

## 4. The re-file and its isolation step (for uac-r2)

**Where it runs.** `InvoiceSubmissionAfterSteps.RunFromStoredAsync` runs the re-file as its first step:
- after the stored-state checks (B, link, findings, plan, A);
- before the totals refusal, the Signals, the Decision Record, indexing, the notification and the terminal status.

It therefore runs on the first run (`RunAsync` after Committed) and on every resume (`ResumeAsync`, which covers a redelivery of a linked submission that is still Processing). Every call in it is idempotent.

**Why after the commit.** 049's persister writes the invoice, its lines and approval, the document link and the submission link in ONE transaction. An owner reassignment cannot be part of that transaction. Wrapping the transaction inside `ReparentAsync` would change 049's all-or-nothing contract, which is the escalation trigger, so the re-file runs after the commit instead. In the window between the commit and the reparent, the document stays readable by its old owner set, so nothing new is exposed.

**Which document.** Only the submission's own `sprk_document`, and only when it was confirmed. The test is `InvoiceDocumentRefile.AppliesTo(submissionId, documentId)`, the deterministic id from section 2. An intake upload's document was created under the core record's owner, so it returns `NotApplicable` and nothing is read.

### Exact calls, in order (`InvoiceDocumentRefile.RefileAsync`)

These are the same calls the old confirm made in `RefileDocumentUnderInvoiceAsync` (#2095, task 146), moved into the job.

1. `RefileIsolationRead.BeforeWriteAsync(children, "sprk_document", documentId, ct)`, then `await answer()`.
   - This is "was the document isolated?", read **before** anything can move its owner. It reads once and retries once after a fault.
2. `IRecordOwnershipResolver.ReparentAsync(new RecordReparent { EntityLogicalName = "sprk_document", RecordId = documentId, ParentChanges = {}, InheritedParents = [ ("sprk_invoice", invoiceId) ] }, _ => Task.CompletedTask, ct)`.
   - The change is a no-op because the persister's transaction already wrote `sprk_document.sprk_invoice`.
   - `sprk_invoice` is an ownership parent, so the resolver re-derives the owner over every parent the document has plus the invoice (secure-if-any). It reassigns the owner with a separate write and reads it back when the owner differs.
3. `SecureChildReconciler.AfterRefileAsync("sprk_document", documentId, () => Task.FromResult(wasIsolated), CancellationToken.None)`.
   - It runs unless the reparent reports `ParentsUnchanged`.
   - It mirrors the document's secure-child state and passes over what is filed under it.
   - `CancellationToken.None` is used because the re-file has already landed.

### What each failure does

| Failure | Result | What was written | What happens next |
|---|---|---|---|
| Isolation read faults twice | `IsolationUnreadable` → after-steps **Retry** (`ebilling.submission.document_isolation_unreadable`) | Nothing by this step. The persister's commit stands. | The job is transient, so a redelivery resumes the after-steps and re-runs the re-file. No Signal, record, job, notification or terminal status is written first. The submission stays Processing. |
| Reparent refused (owner unresolved, secure-removal refused, …) | `Refused(code)` → after-steps **Refused** with the resolver's code | Nothing (the resolver writes nothing on a refusal) | The handler's existing Refused mapping (`LeaveLinkedSubmissionAsync`): Poisoned, admin attention, submission stays Processing. An administrator fixes the cause and the job is resumed. |
| Reparent throws (a Dataverse fault, including the owner assignment after the resolver's own restore) | after-steps **Retry** (`ebilling.submission.document_refile_failed`) | Whatever the resolver's own recovery left. It restores the columns it changed (none here) and rethrows. | A redelivery re-runs the step. The step is idempotent: if the owner already moved, no owner write happens. |
| `AfterRefileAsync` fails internally | — (it never throws; it logs) | The re-file stands | The two-minute recent-changes net reconciles it, as for every other re-file (#2091) |
| Job token cancelled after the reparent | `AfterRefileAsync` still runs (`CancellationToken.None`) | Mirror moved | — |

Tests: section 6, "Re-file". Six of them are the old confirm's #2095 tests rewritten against `InvoiceDocumentRefile`.

## 5. Clients of the confirm route

`src/client` and `src/solutions` contain no code that calls `/api/finance/invoice-review/confirm`; the grep was run at task start and again before commit. The only references are:
- `infrastructure/dataverse/views/invoice-review-queue.md`, the operator instructions. **Updated** with the new body (`invoiceNumber` required), the gate and the 202 shape.
- `tests/integration/contract/Api/Finance/FinanceEndpointsAuthorizationContractTests.cs`. **Updated** (section 6).

The old `InvoiceReviewResult` type (`invoiceId`, `jobId`, `statusUrl`, `extractionAlreadyQueued`) is deleted. Comments elsewhere that still describe the old confirm are listed in section 10.

## 6. Tests

All are maintain-class except where noted (ADR-038). The write-path file is a KEEP path; the replaced tests are listed below the table.

| File | What it proves |
|---|---|
| `tests/unit/.../Finance/Submission/InvoiceSubmissionExistingDocumentTests.cs` (new, 22 cases) | The checks run: the firm link is read and the owner is resolved. No upload, no document create, no external create. One Internal submission with the derived id points at the document, and its job is queued. The writes use `CancellationToken.None`. A retry returns the earlier submission and creates nothing; a Received one is re-queued. Other number or firm → 409. Missing or blank number → 400 before any read. `firm_not_linked` 403, with a linked-firm negative control. Unreadable links → 503. Inactive firm, owner refused, and four document refusals (404, 409, 422, 503) all write nothing. AlreadyCreated → Received. Reconcile conflict → 409 with no delete. Queue failure → 503 with no delete. Argument guards. Determinism of the ids. |
| `tests/integration/data-mutation/FinanceInvoiceReview/InvoiceReviewWritePathTests.cs` (rewritten; KEEP path) | **Confirm:** it submits through the REAL intake, creates no invoice and no link, queues no `InvoiceExtraction`, and writes only the review mark. The reviewer's oid reaches the intake. 400 and 403 write nothing. An already-invoiced document → 409. A retry returns the same submission. A failed mark names the submission and a retry completes it. An unrelated write is retried. The bound is respected. The mark lands under `CancellationToken.None`. **Confirm vs reject:** a reject after a confirm → 409; a confirm landing during a reject → reject 409, Confirmed stands; an unreadable confirm state → `ConfirmStateUnreadable`. **Re-file:** the reparent request shape; isolated→ordinary (`wasIsolated=true`, read before the reparent); ordinary→Secure (mirror created); a double isolation fault writes nothing and makes no reparent; K1 `CancellationToken.None`; a refusal skips the step; `ParentsUnchanged` skips the step; a fault propagates; an intake document is NotApplicable. The reject tests (4) and the invoice-name tests are kept. |
| `tests/unit/.../Finance/Submission/InvoiceSubmissionAfterStepsTests.cs` (+6 cases) | The re-file runs FIRST with the stored document and the invoice id. `IsolationUnreadable` → Retry and `Refused` → Refused, each with no Signal, record, job, notification or terminal status, and the submission stays Processing. A throw → Retry `document_refile_failed`. A resume runs it again and completes. |
| `tests/integration/contract/Api/Finance/FinanceEndpointsAuthorizationContractTests.cs` | The 202 body is exactly `{submissionId, jobId, trackingNumber, statusUrl}`, with `Location` and every body id authorized. Seven intake refusals are rendered with status and `errorCode`. `InvoiceReviewException` rendering (`StatusNotUpdated` names `submissionId`). eBilling off: authorized → 503 `ebilling.disabled` after the authorization filter; unauthorized → 403; reject is not gated. |
| `tests/unit/.../Infrastructure/DI/RefileWriterReconcilerRegistrationTests.cs` | `InvoiceDocumentRefile` resolves with the scope's own reconciler under scope validation. The after-steps compose. |
| ArchTests: `RefileWriterAfterRefileGuardTests`, `RecordOwnerAssignmentCensusTests`, `FinanceBaseValuesSingleDefinitionGuardTests` | Updated registries. In the AfterRefile guard, the compliant non-vacuity entry is now `InvoiceDocumentRefile`. In the census: the `sprk_invoice` create and the `BuildInvoiceCreateFields` owner write are removed (deleted code); the unscanned writer is now `InvoiceDocumentRefile.cs`; the person-bearing writer `InvoiceSubmissionService.DecideAsync` replaces `SubmitAsync`; `InvoiceReviewService.CreateInvoiceRecordAsync` is removed. |

**Replaced in the KEEP path (ADR-038: replacement in the same change).** The old confirm tests covered the create, link, compensation-delete, concurrent-link, link-retry, extraction-queue and old status-mark behaviour, about 30 cases. The code they tested is deleted, so they cannot apply. The confirm tests listed above replace them. The six #2095 re-file tests are kept in substance, rewritten against `InvoiceDocumentRefile`.

## 7. Fail-before / pass-after and the seeded mutation

The new tests compile only against the new API (`SubmitExistingDocumentAsync`, `InvoiceDocumentRefile`, the new confirm return type). Against the old `InvoiceReviewService` and `FinanceEndpoints` they do not compile, and that is the "before" at the API level. Each behaviour was then proven by reverting **only that behaviour** in place, rebuilding, running the affected tests, and restoring. The results are in the mutation log below.

All 13 reverts were caught. Each row shows the reverted behaviour, the number of tests that failed, and which ones.

| # | Reverted behaviour | Failed | Caught by |
|---|---|---|---|
| **M1 (seeded, brief)** | Firm-link check: `NotLinked` treated as `Linked` | **18** | 050a: `FirmNotLinked_Is403_AndWritesNothing` (existing-document unit) and `Confirm_FirmNotLinkedToTheMatter_Is403_AndWritesNothing` (confirm write path). Pre-existing: `InvoiceSubmissionServiceTests` (6), `InvoiceSubmissionFirmLinkWireTests` (6), `InvoiceSubmissionEndpointsContractTests` (4). |
| M2 | After-steps never call the re-file | 5 | the 5 after-steps re-file tests |
| M3a | Isolation not read before the reparent (`wasIsolated` forced false) | 2 | `Refile_AnIsolatedDocumentMovedOutOfSecure…`, `Refile_CancelledAfterTheReparent…` |
| M3b | `AfterRefileAsync` skipped | 3 | isolated→ordinary, ordinary→Secure, K1 |
| M3c | `AfterRefileAsync` on the job's token instead of `None` | 1 | K1 `Refile_CancelledAfterTheReparent_StillRunsAfterRefile` |
| M3d | Reparent refusal ignored | 1 | `Refile_WhenTheReparentIsRefused_DoesNotRunAfterRefile` |
| M4 | Reject ignores the confirm submission | 2 | `Reject_AfterConfirm_BeforeTheJobLinks…`, `ConfirmLandsFirst_DuringAReject…` |
| M5 | Confirm route not gated by eBilling | 1 | `Confirm_EbillingDisabled_Is503_AfterAuthorization…` |
| M6 | Confirm answers the outcome object, not the receipt shape | 8 | the response-shape test and the 7 intake-refusal rendering cases |
| M7 | No dedupe by document (random submission id) | 6 | retry, AlreadyCreated, main path, mark-retry tests |
| M8 | D-91: number defaulted instead of required | 6 | the 3 + 3 missing or blank number cases (unit and write path) |
| M9 | Existing-document entry ignores the `DecideAsync` refusals | 5 | firm-not-linked (2), inactive firm, owner refused, links unreadable |
| M10 | Confirm never marks the document | 7 | the confirm write-path mark tests |

The mutation loop is the scratchpad script `mutations.py`. Each revert was made in place, the test project was rebuilt, the filter ran, and the original file was restored; the restore was verified with grep.

## 8. CLAUDE.md §10 and §11

**Placement.** Everything is in the BFF (`Services/Finance/Submission`, `Services/Finance`, `Api/Finance`) as in-process steps of an ADR-004 job and an existing route; ADR-052 applies. There is:
- no new endpoint;
- no new package;
- no AI call;
- one new DI registration: `AddScoped<InvoiceDocumentRefile>()` in `FinanceModule`.

**Publish-size delta** against a fresh master build. Each ref was published as a Release build of a `git archive` of `src/server` and `config`.

| Ref | Zip | Raw | `Sprk.Bff.Api.dll` |
|---|---|---|---|
| origin/master `2b07480ab` | 38,331,869 B (36.56 MB) | 131,136,808 B | 19,830,784 B |
| pre-task `c1e36d398` | 39,029,120 B (37.22 MB) | 133,295,504 B | 21,661,184 B |
| 050a `9c97f8403` | 39,021,706 B (37.21 MB) | 133,272,464 B | 21,640,192 B |

- **This task:** −7,414 B zipped (−0.01 MB); the dll is 20,992 B smaller. More legacy confirm code was deleted than intake code added.
- **Branch total against master:** +0.66 MB, all from earlier ebilling-r1 work.
- **Thresholds:** 37.21 MB is under the 55 MB review threshold and far under the +5 MB per-task threshold. **CVE:** no package changed, so no new HIGH CVE is possible from this change.

**§11, new surface.** Each item answers three questions.

| Surface | Existing | Extension | Cost of doing nothing |
|---|---|---|---|
| `InvoiceSubmissionService.SubmitExistingDocumentAsync` and `InvoiceSubmissionRequest.ExistingDocumentId` | `SubmitAsync` is the only intake | `SubmitAsync` validates, uploads and creates a document. The shared checks are factored into `DecideAsync`, which both call, rather than adding a branch to every step of `SubmitAsync`. | D-134 fails: confirm would have to create a second copy of the PDF, or keep creating the invoice itself (ADR-002). |
| `ExistingDocumentSubmissionIdFor` / `ExistingDocumentJobIdFor` | `InvoiceSubmissionPersister.IdOf` (reused) | These are two named uses of it. | No dedupe by document. A retry creates a second submission and a second invoice. |
| Four error codes: `document_not_found`, `document_already_invoiced`, `document_already_submitted`, `document_has_no_file` | `InvoiceSubmissionErrors` | Added to the existing class. | A confirm of a missing, already-invoiced or fileless document, or a conflicting re-confirm, has no stable code (ADR-019). |
| `InvoiceDocumentRefile` and its DI registration | `InvoiceReviewService.RefileDocumentUnderInvoiceAsync` (deleted; the calls moved) | The persister's transaction cannot hold an owner reassignment. A separate type keeps the re-file and its after-re-file step together, under `RefileWriterAfterRefileGuardTests` and the registration test. | A confirmed document stays owned and shared as before (#2095 regresses). |
| `InvoiceSubmissionAfterSteps.DocumentRefileFailedCode` and the constructor parameter | The after-steps' Stop/Retry codes | Same pattern | A reparent fault has no reason code. |
| `InvoiceReviewFailure.ConfirmStateUnreadable` | `InvoiceReviewFailure` | Added to the enum. Values with no producer left were removed. | A reject cannot tell "confirmed" from "unreadable", and would either fail open or answer 500. |

## 9. Open questions for the owner

1. **Re-confirm after a failed or rejected submission.** The deterministic id ties one submission to a document for good. A confirm submission that the job Fails or Rejects cannot be re-confirmed for the same document: the retry answers with the earlier submission. Is a new confirm after a terminal failure wanted? If so, the key needs a generation. The firm's own resubmission path (D-37) is a new upload and is unaffected.
2. **Reject after confirm.** A document with a confirm submission is now always 409 on reject, even if the job later rejected the invoice. This is correct for "neither decision overwrites the other", but the owner may want a different rule once the submission is terminal.
3. **`InvoiceDate` and `TotalAmount` in the confirm request** are now ignored, because the job reads them from the document. They are kept in the request contract for compatibility. Should they be dropped from the contract?
4. **Confirm's authorization filter is unchanged.** It still checks the caller's Create privilege on `sprk_invoice` (G5). The app still creates an invoice, only later, in the job. Keep it, or move to a submission privilege?

## 10. Known limits and things noticed (not fixed)

- **K2.** If the process crashes between the submission create and the enqueue, the submission stays Received with no job. It is repaired only by a retry of the confirm, which re-queues it. The same limit applies to `SubmitAsync`'s non-compensated paths.
- Stale comments outside this change's files still describe the old confirm creating an invoice. They are comments only; behaviour is unaffected. Left for doc drift or 050b:
  - `Services/Jobs/Handlers/InvoiceExtractionJobHandler.cs:48` (050b deletes the file)
  - `Services/Dataverse/OwnedChildWrite.cs:299`
  - `Services/Finance/InvoiceTotalsService.cs:32,40`
  - `Services/Finance/SubmissionRejectionRule.cs:156`
  - `Services/Ai/Nodes/ActionCore/TaskActionCore.cs:115`
  - `Services/Dataverse/InvoiceFamilyCoreRecordGuard.cs:7`
  - `src/server/shared/Spaarke.Dataverse/IFieldMappingDataverseService.cs:58`
  - `tests/integration/data-mutation/FinanceRollup/RecalculateWriteNoCreateTests.cs:24,124`
- `current-task.md` is not updated by this agent. It is shared with the parallel 100 and 105 work, and the main session owns it.
