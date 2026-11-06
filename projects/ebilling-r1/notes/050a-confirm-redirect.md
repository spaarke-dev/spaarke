# 050a: the review-confirm route goes through the submission operation (D-134)

Status: implemented and verified; the adversarial review's findings and uac-r2's review rounds 2 and 3 are fixed (section 11), with the owner's answers of 2026-10-20 (D-139) and uac-r2's design for the re-file (section 4). Rigor is FULL.

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
3. **The submission job (049)** creates the invoice. For a confirm submission its transaction does **not** write the document's `sprk_invoice` link (review #1): the persister checks the document's live row first and makes the commit conditional on that row's version (review round 2), and the after-steps then **re-file** the document under the invoice with the secure-record isolation step. The re-file, `InvoiceDocumentRefile`, is the link's one writer; section 4 covers it. An intake upload's document is still linked in the transaction, as before.
4. **The response** is `202 {submissionId, jobId, trackingNumber, statusUrl}`. It is rendered by `InvoiceSubmissionEndpoints.ToResult`, so the type is `InvoiceSubmissionAccepted`, the same body the internal submission route returns. The `Location` header is `/api/finance/invoice-submissions/{submissionId}`. A refusal is the intake's ProblemDetails with its `errorCode`.
5. **The confirm route** now carries `.RequireEbillingEnabled()`, after its authorization filter (ADR-018). The submission job fails every submission when eBilling is off, so an ungated confirm would create submissions that can only fail. Reject is not gated.
6. **Reject** refuses with 409 `DocumentLinkedToInvoice` in two cases: the document is linked to an invoice (as before), or it has a confirm submission (new). The confirm submission exists before the job links the document.

ADR-002 / D-19 are met: no confirm path creates an invoice; the submission job creates it. (Other `sprk_invoice` creators remain on purpose, kept in task 013: `ChildRecordEndpoints` CreateTables through `OwnedChildWrite`, the chat `create_record` tool and the decision route.) The old create, link, reparent, delete-on-failure and enqueue-extraction machinery in `InvoiceReviewService` is deleted. 050b deletes the legacy extraction handler, schemas and options.

## 2. The dedupe key: one submission per document

The key is **a deterministic submission id**: `InvoiceSubmissionService.ExistingDocumentSubmissionIdFor(documentId)`, which is `IdOf("ebilling-confirm-submission-{documentId}")`, the persister's SHA-256 id scheme. The job id is deterministic in the same way.

Why this key:
- **Concurrency-safe without a lock.** Two confirms cannot create two rows. The primary key is unique, and the allocator answers a second create of the same id with `AlreadyCreated`, which covers both a concurrent creator and a lost reply.
- **A retry gets the same answer.** It returns the same submission id, job id and tracking number. It does this whether the first reply was lost or the client simply retried.
- **The job can recognise a confirm submission with no schema change.** The marker is `submissionId == ExistingDocumentSubmissionIdFor(sprk_document)`. An intake upload has a random id, so it never matches.

Alternative rejected: a Redis lock plus a query by document. It needs a lock, compensation deletes and a separate marker column, and it still races on a lost reply.

How a retry is answered:
- The retry reads the earlier submission first.
- If the firm, the core record, the document and the number (trimmed, case-insensitive) all match and a tracking number exists, the retry returns that submission. When it is still Received, the job is re-queued. A duplicate message is bounded, not harmless: see the known limit in section 10 (review #10).
- If they match but the submission already ended Failed or Rejected, the answer is 409 `submission_already_ended`, naming the status, and the document is not re-marked (review #4, D-139).
- Anything else is 409 `document_already_submitted`.
- A concurrent create (`AlreadyCreated`) is answered exactly like a retry: the row is read and compared on the firm, the number AND the core record (review #5).

**Nothing is deleted.** There is no compensation:
- A failed enqueue is 503 `ebilling.submission.failed`, and its message tells the reviewer to retry; the retry re-queues the job.
- An allocator reconcile conflict (the same id with another firm or number) is 409 and leaves the row alone.

Costs, listed as open questions in section 9:
- A confirm submission that ends Failed or Rejected blocks a re-confirm of the same document: 409 `submission_already_ended` (owner decision D-139; a new attempt is a later feature).
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
| Document filed under a core record other than the request's (review #3; D-140) | 409 `ebilling.submission.document_core_record_mismatch`. "Filed under" is the document's EFFECTIVE core record through its filing chain (D-140): its own core lookups (`sprk_matter` / `sprk_project` / `sprk_workassignment` and their `sprk_related*` twins), else its communication's (`sprk_relatedcommunication`) `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment`, else its parent document (`sprk_parentdocument`), at most 4 documents. A chain that is still unresolved past 4 documents, or loops, is refused the same way (fail closed). A truly UNFILED document (nothing in the chain names a core record) is allowed (owner 2026-10-20, D-139). |
| A row of the filing chain (a communication or a parent document) unreadable (D-140) | 503 `ebilling.submission.record_unavailable`; a chain row that no longer exists files nothing. |
| The confirm's pre-intake review-state read faults (review round 2, item 5) | 503 `sdap.finance.invoice_review.review_state_unreadable`, "nothing was saved, retry" (it was a generic 500). |
| The same submission already ended Failed or Rejected (review #4) | 409 `ebilling.submission.submission_already_ended`, naming the status (owner 2026-10-20, D-139) |
| Document already rejected as not an invoice (review #2) | 409 `sdap.finance.invoice_review.review_decision_changed`, before any submission is created |
| A reject lands between the submission create and the confirm's mark (review #2) | 409 `review_decision_changed`, naming the submission; the reject stands and the job refuses the submission (`document_rejected`) |
| Document deleted after the submission was created (review #6) | 404 `document_not_found`, naming the submission (it was a 500 `status_not_updated` that a retry could never complete) |

## 4. The re-file and its isolation step (for uac-r2)

**Where it runs.** `InvoiceSubmissionAfterSteps.RunFromStoredAsync` runs the re-file as its first step:
- after the stored-state checks (B, link, findings, plan, A);
- before the totals refusal, the Signals, the Decision Record, indexing, the notification and the terminal status.

It therefore runs on the first run (`RunAsync` after Committed) and on every resume (`ResumeAsync`, which covers a redelivery of a linked submission that is still Processing). Every call in it is idempotent.

**Why after the commit.** 049's persister writes the invoice, its lines and approval, the document link and the submission link in ONE transaction. An owner reassignment cannot be part of that transaction. Wrapping the transaction inside `ReparentAsync` would change 049's all-or-nothing contract, which is the escalation trigger, so the re-file runs after the commit instead. In the window between the commit and the reparent, the document stays readable by its old owner set, so nothing new is exposed.

**Who writes the link (review #1, uac-r2's design).** For a confirm submission the persister's transaction does not write `sprk_document.sprk_invoice`. The root cause the review found was two writers of one filing lookup: the transaction wrote the link unconditionally, so `ReparentAsync` then read the NEW parents as the "before" state and its F3 check on a move out of a Secure record saw nothing. Now:
- the persister reads the document's live row (link, review decision, `versionnumber`) before anything is built: linked to an invoice other than `InvoiceIdFor(submissionId)` → Refused `document_already_invoiced` (the job marks the submission Failed); rejected as not an invoice → Refused `document_rejected`; gone → Refused `document_not_found`. Nothing is written.
- **the transaction carries a guard on the document (review round 2, item 2(a)).** `InvoiceSubmissionPersister.BuildExistingDocumentGuard`: an `UpdateRequest` with `ConcurrencyBehavior.IfRowVersionMatches` at the version the check read. It writes NOTHING to `sprk_invoice`. Its one column is `sprk_invoicereviewstatus`, written back with the value the check read (see "The guard's column" below). A filing, a reject or a delete that lands between the check and the commit faults the guard, Dataverse rolls the whole transaction back (no invoice, lines, approval or submission link), and the persister takes 049's existing `DataverseTransactionException` path. On that path it now checks the live row again for a confirm submission: a refusal (`document_already_invoiced` / `document_rejected` / `document_not_found`) is answered in the same delivery, so the submission is Failed with that code and no invoice; anything else (an unrelated write moved the version) is the transaction's ordinary Retry (`transaction_failed`), and the redelivery runs again from the read.
- the re-file is the one writer of the link, inside `ReparentAsync`, conditional on the row version it read. So the resolver weighs the document's TRUE prior parents, and a filing that lands after the check is caught by the conditional write.
- No reader needs the document link inside the transaction: the commit marker is the submission's own `sprk_invoice`, and the only after-step that uses the document (indexing) runs after the re-file.

Of the two options the brief offered (reparent before the link write, or pass the prior parents read before the write), this is the first in substance: the reparent runs before the link is written, because the link write IS the reparent's change. The guard does not add a second writer of the link; it only makes the commit conditional on the document being as the check saw it.

**The guard's column, and why.** `sprk_invoicereviewstatus`, written back with exactly the value the check read (null when it read none). The version condition guarantees the row still holds that value, so the write changes no value. A column is written at all so that the request is a real update of the row: Dataverse then evaluates `IfRowVersionMatches` and moves `versionnumber`. Dataverse does not document how an update that names no column behaves under `IfRowVersionMatches`, and it could not be verified live from this worktree, so the guard does not rely on one. This column was chosen because:
- the check already reads it (no second read, so no race between two reads);
- it is not the link (`sprk_invoice` stays the re-file's alone);
- its only writers (confirm's mark and reject) are themselves conditional on the row version, so a guard that moves the version makes them re-read and decide again, never overwrite. A side effect that helps: a stalled reject that read the row before the confirm's submission existed now gets a 412 after the commit, re-reads, sees the confirm submission and refuses.
- Cost: `modifiedon` moves and the confirm's mark may meet one more 412 if the job commits before the mark lands (the job runs after an AI read, so in practice the mark lands first; the mark re-reads and retries, 3 attempts).

**What is left: the window between the commit and the re-file (review round 2, item 2(b)).** The guard closes the window between the check and the commit. The re-file still re-reads the row after the commit, and it can still refuse: the document was filed under another invoice, rejected or deleted in the milliseconds between the commit and the re-file (or before a redelivery that resumes a commit whose after-steps failed), or the ownership resolver refused. The outcome is defined as follows, so an invoice is never left without its document silently:
- **The submission ends** Processing (never Failed: Failed is terminal and would strand the committed invoice). The job is Poisoned (dead-lettered).
- **The administrator sees** the D-133 `sprk_adminattention` segment `Processing stopped: {code}; the invoice is committed but its document is NOT filed under it and the submission stays Processing: resolve the document, then resume` (`AdminAttentionText.StoppedNote(code, documentNotFiled: true)`; the after-steps flag the result `DocumentNotFiled`, the handler words it). The submission appears in the "Submissions needing attention" view.
- **A replay** of the dead-lettered message resumes the after-steps, whose first step is the re-file. It completes when the cause is resolved: `document_already_invoiced` once the document is unlinked from the other invoice; `document_rejected` once the review decision is changed back; a resolver refusal once its cause is fixed. The completed run withdraws the "Processing stopped" segment (R7). `document_not_found` can never complete by replay: the document is gone, and the administrator must decide what happens to the invoice (it is In Review with no Signals, record or notification).

**Which document.** Only the submission's own `sprk_document`, and only when it was confirmed. The test is `InvoiceDocumentRefile.AppliesTo(submissionId, documentId)`, the deterministic id from section 2. An intake upload's document was created under the core record's owner, so it returns `NotApplicable` and nothing is read.

### Exact calls, in order (`InvoiceDocumentRefile.RefileAsync`)

These are the calls the old confirm made in `RefileDocumentUnderInvoiceAsync` (#2095, task 146), moved into the job, with the link write moved into the reparent (review #1). Each attempt (at most 3):

1. The live row through `IFieldMappingDataverseService.RetrieveRecordFieldsAsync`: `_sprk_invoice_value`, `sprk_invoicereviewstatus` and `versionnumber`. Gone → Refused `document_not_found`. Linked to another invoice → Refused `document_already_invoiced` (never overwritten). Rejected as not an invoice → Refused `document_rejected` (review round 2, item 3: a stalled reject never ends up linked to an invoice, so confirm's 409 "will not create an invoice" stays true). Nothing is written.
2. `RefileIsolationRead.BeforeWriteAsync(children, "sprk_document", documentId, ct)`, then `await answer()`.
   - This is "was the document isolated?", read **before** anything can move its owner. It reads once and retries once after a fault.
3. `IRecordOwnershipResolver.ReparentAsync(InvoiceDocumentRefile.ReparentRequestFor(documentId, invoiceId), apply, ct)`. The request is `ParentChanges = { sprk_invoice = invoice }`, `InheritedParents = [ ("sprk_invoice", invoiceId) ]`, no `SecureExitCaller`.
   - The resolver reads the row's current parents, overlays the link, re-derives the owner (secure-if-any) and refuses a move out of a Secure record without F3. The job acts for nobody, so any such move is refused.
   - `apply` writes `sprk_Invoice@odata.bind` with `UpdateRecordFieldsIfUnchangedAsync` on the version read in step 1. A 412 is read again and decided again. When the link already holds (a redelivery), `apply` writes nothing, and the inherited parent still makes the resolver re-derive the owner.
   - The resolver reassigns the owner with a separate write and reads it back when the owner differs.
4. `SecureChildReconciler.AfterRefileAsync("sprk_document", documentId, () => Task.FromResult(wasIsolated), CancellationToken.None)`.
   - It runs unless the reparent reports `ParentsUnchanged`.
   - It mirrors the document's secure-child state and passes over what is filed under it.
   - `CancellationToken.None` is used because the re-file has already landed.

### What each failure does

| Failure | Result | What was written | What happens next |
|---|---|---|---|
| Isolation read faults twice | `IsolationUnreadable` → after-steps **Retry** (`ebilling.submission.document_isolation_unreadable`) | Nothing by this step. The persister's commit stands. | The job is transient, so a redelivery resumes the after-steps and re-runs the re-file. No Signal, record, job, notification or terminal status is written first. The submission stays Processing. |
| Document gone, linked to another invoice or rejected when the re-file reads it (review #1; round 2 item 3) | `Refused(document_not_found / document_already_invoiced / document_rejected)` → after-steps **Refused**, `DocumentNotFiled` | Nothing | Poisoned; the submission stays Processing; the attention text says the invoice has NO document (round 2, 2(b), above). A replay completes once the cause is resolved; `document_not_found` never can. |
| The document changed between the re-file's read and its link write | 412 → read again and decided again, 3 attempts; then the `DBConcurrencyException` propagates → after-steps **Retry** (`document_refile_failed`) | Nothing | A redelivery re-runs the step. |
| Reparent refused (owner unresolved, secure-removal refused, …) | `Refused(code)` → after-steps **Refused** with the resolver's code, `DocumentNotFiled` | Nothing (the resolver writes nothing on a refusal) | The handler's Refused mapping (`LeaveLinkedSubmissionAsync`): Poisoned, the attention text says the invoice has NO document, submission stays Processing. An administrator fixes the cause and the job is resumed. |
| Reparent throws (a Dataverse fault, including the owner assignment after the resolver's own restore) | after-steps **Retry** (`ebilling.submission.document_refile_failed`) | Whatever the resolver's own recovery left. It restores the columns it changed (none here) and rethrows. | A redelivery re-runs the step. The step is idempotent: if the owner already moved, no owner write happens. |
| `AfterRefileAsync` fails internally | — (it never throws; it logs) | The re-file stands | The two-minute recent-changes net reconciles it, as for every other re-file (#2091) |
| Job token cancelled after the reparent has returned | `AfterRefileAsync` still runs (`CancellationToken.None`) | Mirror moved | — |
| Job token cancelled before the link write | The write is never sent; `OperationCanceledException` propagates | Nothing | The job's own cancellation handling (a redelivery). |

Tests: section 6, "Re-file", and section 11. Since review #7 they run the REAL ownership resolver and reconciler.

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
| `tests/integration/data-mutation/FinanceInvoiceReview/InvoiceReviewWritePathTests.cs` (rewritten; KEEP path) | **Confirm:** it submits through the REAL intake, creates no invoice and no link, queues no `InvoiceExtraction`, and writes only the review mark. The reviewer's oid reaches the intake. 400 and 403 write nothing. An already-invoiced document → 409. A retry returns the same submission. A failed mark names the submission and a retry completes it. An unrelated write is retried. The bound is respected. The mark lands under `CancellationToken.None`. **Confirm vs reject:** a reject after a confirm → 409; a confirm landing during a reject → reject 409, Confirmed stands; an unreadable confirm state → `ConfirmStateUnreadable`. **Re-file (rewritten for review #7, over the REAL resolver and reconciler; section 11 lists them):** an unfiled document under a Secure matter's invoice gets the link, is isolated and mirrored; a redelivery writes no link and still re-derives the owner; a document under another invoice is refused and nothing moves; the real resolver refuses to release a document from a Secure matter's invoice without F3; a filing that lands before the link write is read again and refused; an unrelated write is retried; a resolver refusal writes nothing; a double isolation fault writes nothing; a cancel after the re-file landed still mirrors (round 2: the fake honours the token); a gone document is refused; a fault propagates; an intake document is NotApplicable. The reject tests (4) and the invoice-name tests are kept. |
| `tests/unit/.../Finance/Submission/InvoiceSubmissionAfterStepsTests.cs` (+6 cases) | The re-file runs FIRST with the stored document and the invoice id. `IsolationUnreadable` → Retry and `Refused` → Refused, each with no Signal, record, job, notification or terminal status, and the submission stays Processing. A throw → Retry `document_refile_failed`. A resume runs it again and completes. |
| `tests/integration/contract/Api/Finance/FinanceEndpointsAuthorizationContractTests.cs` | The 202 body is exactly `{submissionId, jobId, trackingNumber, statusUrl}`, with `Location` and every body id authorized. Seven intake refusals are rendered with status and `errorCode`. `InvoiceReviewException` rendering (`StatusNotUpdated` names `submissionId`). eBilling off: authorized → 503 `ebilling.disabled` after the authorization filter; unauthorized → 403; reject is not gated. |
| Review round 2 (section 11, "Round 2") | `InvoiceSubmissionPersisterTests`: the guard's shape (conditional, at the version read, no `sprk_invoice`, the review status written back as read); a filing, reject or delete between the check and the commit rolls back and is refused in the same delivery (3 cases); an unrelated write is the transaction's Retry. `InvoiceSubmissionExistingDocumentTests` (D-140, 11 cases): an email attachment under another matter / project / work assignment → 409, under the same matter → received, a communication with no regarding → received, the document's own lookup decides first, the `sprk_relatedmatter` twin, a parent-document chain (409 and its negative control), the bound and a loop (fail closed), an unreadable communication → 503. `InvoiceSubmissionProcessingJobHandlerTests`: a refused re-file after the commit stays Processing, is Poisoned, tells the administrator the invoice has no document, and a replay completes (4 codes); a must-not-fire control. `InvoiceSubmissionAfterStepsTests`: `DocumentNotFiled` is set for a refused re-file only. `InvoiceReviewWritePathTests`: the rejected document (and its ConfirmedInvoice control), the already-isolated document, the isolated document with no Secure parent, the cancel before the link write, K1 rewritten (the cancel lands after the resolver returns; the link write and the share seam honour the token), the 503 confirm read. Contract: `ReviewStateUnreadable` → 503. |
| `tests/unit/.../Infrastructure/DI/RefileWriterReconcilerRegistrationTests.cs` | `InvoiceDocumentRefile` resolves with the scope's own reconciler under scope validation. The after-steps compose. |
| ArchTests: `RefileWriterAfterRefileGuardTests`, `RecordOwnerAssignmentCensusTests`, `FinanceBaseValuesSingleDefinitionGuardTests` | Updated registries. In the AfterRefile guard, the compliant non-vacuity entry is now `InvoiceDocumentRefile`. In the census: the `sprk_invoice` create and the `BuildInvoiceCreateFields` owner write are removed (deleted code); the unscanned writer is now `InvoiceDocumentRefile.cs`; the person-bearing writer `InvoiceSubmissionService.DecideAsync` replaces `SubmitAsync`; `InvoiceReviewService.CreateInvoiceRecordAsync` is removed. |

**Replaced in the KEEP path (ADR-038: replacement in the same change).** The old confirm tests covered the create, link, compensation-delete, concurrent-link, link-retry, extraction-queue and old status-mark behaviour, about 30 cases. The code they tested is deleted, so they cannot apply. The confirm tests listed above replace them. The six #2095 re-file tests were first rewritten against `InvoiceDocumentRefile` over a resolver double; review #7 replaced them with tests over the real resolver (section 11 says which were removed and why).

## 7. Fail-before / pass-after and the seeded mutation

The new tests compile only against the new API (`SubmitExistingDocumentAsync`, `InvoiceDocumentRefile`, the new confirm return type). Against the old `InvoiceReviewService` and `FinanceEndpoints` they do not compile, and that is the "before" at the API level. Each behaviour was then proven by reverting **only that behaviour** in place, rebuilding, running the affected tests, and restoring. The results are in the mutation log below.

All 13 reverts were caught. Each row shows the reverted behaviour, the number of tests that failed, and which ones.

| # | Reverted behaviour | Failed | Caught by |
|---|---|---|---|
| **M1 (seeded, brief)** | Firm-link check: `NotLinked` treated as `Linked` | **18** | 050a: `FirmNotLinked_Is403_AndWritesNothing` (existing-document unit) and `Confirm_FirmNotLinkedToTheMatter_Is403_AndWritesNothing` (confirm write path). Pre-existing: `InvoiceSubmissionServiceTests` (6), `InvoiceSubmissionFirmLinkWireTests` (6), `InvoiceSubmissionEndpointsContractTests` (4). |
| M2 | After-steps never call the re-file | 5 | the 5 after-steps re-file tests |
| M3a | Isolation not read before the reparent (`wasIsolated` forced false) | 2 (then); **0 since review #7** | Then: `Refile_AnIsolatedDocumentMovedOutOfSecure…` and the old K1, both over a resolver double. Review #7 removed them (section 11), and round 2 re-ran it (N6): it is an **equivalent mutation** on the job's path, see section 11, "Round 2". |
| M3b | `AfterRefileAsync` skipped | 3 | isolated→ordinary, ordinary→Secure, K1 |
| M3c | `AfterRefileAsync` on the job's token instead of `None` | 1 | K1, now `Refile_CancelledAfterTheReFileLanded_StillRunsAfterRefile`. Round 2 found it had **survived** once the double was replaced (0 failures: the share fake ignored the token). It is caught again since the share seam honours the token (N7). |
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

1. **Re-confirm after a failed or rejected submission.** ANSWERED (owner 2026-10-20, D-139): refused with 409 `submission_already_ended`; a new attempt (new submission, new tracking `.n`, a generation key) is a later feature. The firm's own resubmission path (D-37) is a new upload and is unaffected.
2. **Reject after confirm.** A document with a confirm submission is now always 409 on reject, even if the job later rejected the invoice. This is correct for "neither decision overwrites the other", but the owner may want a different rule once the submission is terminal.
3. **`InvoiceDate` and `TotalAmount` in the confirm request** (review #12, known limit) are now ignored, because the job reads them from the document. They are kept in the request contract for compatibility. Should they be dropped from the contract?
4. **Confirm's authorization filter is unchanged.** It still checks the caller's Create privilege on `sprk_invoice` (G5). The app still creates an invoice, only later, in the job. Keep it, or move to a submission privilege?

## 10. Known limits and things noticed (not fixed)

- **K2 (review #10): a duplicate job message is bounded, not harmless.** The confirm's job id is fixed, and the processing lock is owned by the job id, so a second message of the same job can take the lock over once it is older than the idempotency service's owner takeover age (one minute). A second AI run of the same submission is therefore possible while the first is still running. Section 2 used to say the lock made a duplicate harmless; that was wrong. The harm is bounded: the invoice, line, approval and decision ids are deterministic (a second create of the same id fails), the persister re-reads the submission's link and resumes instead of persisting twice, and the terminal status write is conditional. The cost is a second AI read and possibly a contradictory evaluation that is never persisted.
- **K11 (review #11; narrowed in review round 2): a refused re-file leaves the submission Processing.** Since round 2 the persister's guard rolls the commit back for anything that lands between its check and the commit, so a refused re-file needs a change in the short window between the commit and the re-file, or a resolver refusal. The invoice is then committed, so the handler's linked-refusal path applies: the submission stays Processing, its `sprk_adminattention` says the invoice has NO document filed under it, and the job is Poisoned. An administrator resolves the document and replays the message (section 4, "What is left"). A deleted document cannot be completed by replay.
- **K12 (review #12): `InvoiceDate` and `TotalAmount` are ignored** (section 9 question 3). The queue document no longer lists Project as an input.
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

## 11. Review fixes (adversarial review of 050a)

Owner answers of 2026-10-20 are recorded as D-139 in `decisions-log.md`. Code: commit `9feb489db` (round 1); round 2 is at the end of this section.

| # | Finding | Fix | Tests (fail before the fix) |
|---|---|---|---|
| 1 | The job overwrote `sprk_document.sprk_invoice` without checking it, and the resolver then read the new parents as the "before" state | uac-r2's design (section 4): for a confirm submission the persister's transaction no longer writes the link. The persister checks the live row first (linked elsewhere → Refused `document_already_invoiced` → Failed). The re-file writes the link inside `ReparentAsync`, conditional on the row version, and refuses a document linked to another invoice. | `InvoiceSubmissionPersisterTests.ConfirmSubmission_*` (unlinked commits without the link; linked elsewhere refused; linked to its own invoice commits; gone refused); `IntakeSubmission_DocumentIsNotRead_AndIsLinkedInTheTransaction` (negative control); `InvoiceReviewWritePathTests.Refile_*` |
| 2 | A reject that landed between the confirm's create and its mark was overwritten | The confirm reads the review state (version and decision) before the intake. A document already rejected is 409 `review_decision_changed` with no submission. The mark is conditional on that version; on a 412 a changed reject decision is 409 `review_decision_changed` naming the submission, any other change is retried. The job refuses a rejected document (`document_rejected`). | `Confirm_RejectLandsBetweenTheCreateAndTheMark_Is409_AndTheRejectStands`, `Confirm_DocumentAlreadyRejected_Is409_AndNothingIsSubmitted`, `ConfirmSubmission_DocumentRejected_IsRefused_NothingWritten`; contract rows for `ReviewDecisionChanged` → 409 |
| 3 | The document's own core record was not compared with the request | 409 `document_core_record_mismatch` when any of `sprk_matter`, `sprk_project`, `sprk_workassignment` names another record. Unfiled documents are allowed: **owner decision 2026-10-20 (D-139)**. | `DocumentFiledUnderAnotherCoreRecord_Is409_AndWritesNothing` (3 columns), `DocumentFiledUnderTheSameMatter_IsReceived`, `UnfiledDocument_IsReceived` |
| 4 | A retry after the submission ended Failed or Rejected answered 202 with the dead submission | 409 `submission_already_ended`, naming the status; nothing is marked or queued. **Owner decision 2026-10-20 (D-139)**: a new attempt (new submission, tracking `.n`, generation key) is a later feature. | `Retry_AfterTheSubmissionEnded_Is409_NamingTheStatus` (Failed, Rejected), `ConcurrentCreate_AlreadyCreated_AlreadyEnded_Is409`, `Confirm_RetryAfterTheSubmissionFailed_Is409_AndDoesNotMark` |
| 5 | `AlreadyCreated` (a concurrent create) was answered Received without comparing the row | The stored row is read and answered exactly like a retry: firm, number and core record compared, ended status refused. | `ConcurrentCreate_AlreadyCreated_ForAnotherMatter_Is409`, `ConcurrentCreate_AlreadyCreated_AlreadyEnded_Is409`, `Confirm_RetryWithAnotherMatter_Is409` |
| 6 | A document deleted after the submission was created gave 500 `status_not_updated`, which a retry could never complete | `KeyNotFoundException` in the mark → 404 `DocumentNotFound`, naming the submission. | `Confirm_DocumentDeletedAfterTheSubmission_IsDocumentNotFound_NamingTheSubmission`; contract row (`DocumentNotFound`, 404, with submission) |
| 7 | The re-file tests used a resolver double that did not behave like the real resolver; negative cases were missing | The re-file tests run the REAL `RecordOwnershipResolver` and `SecureChildReconciler` over one in-memory Dataverse, including the real F3 refusal of a move out of a Secure matter's invoice. Negative tests added throughout (rows above). | `Refile_*` (12 tests, listed below) |
| 8 | Confirm asked the matter for `finance.attach_invoice` (AppendTo only); the submission route asks `finance.submit_invoice` (Read + AppendTo) | Confirm uses `finance.submit_invoice`. | `Confirm_MatterWithAppendToButNotRead_IsDenied_AsTheSubmissionRouteIs` |
| 9 | Comments claimed the job's persister was "the only invoice creator" | Reworded to "no confirm path creates an invoice; the submission job creates it", naming the creators kept on purpose (task 013). No code changed. | — (comments: `InvoiceReviewService`, `FinanceModule`, `RecordOwnerAssignmentCensusTests`, this note) |
| 10 | Section 2 said the lock made a duplicate job message harmless | Note only: corrected to a known limit (K2 in section 10). | — |
| 11 | A refused re-file leaves the submission Processing | Note only: known limit K11 (section 10); the admin-attention path applies. | — |
| 12 | `InvoiceDate` / `TotalAmount` ignored; the queue doc listed Project as an input | Note only: known limit K12. `infrastructure/dataverse/views/invoice-review-queue.md` now lists the Matter line, the core-record rule and the new 409s. | — |

**A default I chose (for the owner or coordinator to confirm).** Confirm of a document that is ALREADY rejected as not an invoice is refused with 409 `review_decision_changed` before any submission is created. Without it, a confirm after a reject would create a submission whose job races the mark: the job could run first and refuse it (`document_rejected`), leaving a Failed submission for a document the reviewer never confirmed. The review queue lists only ToReview documents, so this is reached only by a stale page or a direct call.

**Re-file tests over the real resolver.** `Refile_UnfiledDocument_UnderASecureMattersInvoice_WritesTheLink_IsolatesIt_AndMirrors`, `Refile_LinkAlreadyWritten_WritesNoLink_AndStillReDerivesTheOwner`, `Refile_DocumentFiledUnderAnotherInvoice_IsRefused_NothingMoves`, `Refile_DocumentUnderASecureMattersInvoice_IsNeverReleased_TheRealResolverRefusesWithoutF3`, `Refile_AFilingThatLandsBeforeTheLinkWrite_IsReadAgain_AndRefused`, `Refile_AnUnrelatedWriteBeforeTheLinkWrite_IsRetried`, `Refile_WhenTheRealResolverRefuses_NothingIsWritten`, `Refile_WhenTheIsolationReadFaultsTwice_WritesNothing`, `Refile_CancelledAfterTheReFileLanded_StillRunsAfterRefile (renamed in round 2)`, `Refile_DocumentGone_IsRefused`, `Refile_WhenTheReparentFaults_ItPropagates` (a mock resolver, for the fault only), `Refile_ForAnIntakeDocument_IsNotApplicable`.

**Tests removed, and why.** `Refile_IsAReparentUnderTheInvoice_WithNoColumnChange` asserted the pre-fix shape (no column change), which review #1 replaced. The isolated → ordinary, ordinary → Secure, refused and `ParentsUnchanged` double tests asserted what a resolver double returned, not what the real resolver does. The real path makes the isolated → ordinary move unreachable without F3 (now proven by `Refile_DocumentUnderASecureMattersInvoice_IsNeverReleased_...`), and `ParentsUnchanged` is unreachable because the request always names the invoice as an inherited parent. The old K1 test described the overwrite that review #1 removed.

**Fail-before evidence and seeded mutations.** Each fix was reverted in place (a script restores the file and rebuilds after each) and the named tests were run:

All 14 reverts were caught (two are the brief's seeded mutations: R1c removes the doc-link check, R3 removes the core-record comparison).

| # | Reverted behaviour | Failed | Caught by |
|---|---|---|---|
| R1a | Persister's live-row pre-check skipped (#1, job side) | 4 | `ConfirmSubmission_DocumentGone_…`, `…LinkedToAnotherInvoice_…`, `…UnlinkedDocument_CommitsWithoutTheDocumentLink`, `…DocumentRejected_…` |
| R1b | Persister writes the document link in the transaction again (#1) | 1 | `ConfirmSubmission_UnlinkedDocument_CommitsWithoutTheDocumentLink` |
| **R1c (seeded)** | Re-file's live-row link check removed (#1, re-file side) | 3 | `Refile_DocumentUnderASecureMattersInvoice_IsNeverReleased_…`, `Refile_DocumentFiledUnderAnotherInvoice_IsRefused_…`, `Refile_AFilingThatLandsBeforeTheLinkWrite_…` |
| R1d | Re-file never writes the link (the pre-fix shape) | 4 | `Refile_UnfiledDocument_UnderASecureMattersInvoice_…`, `Refile_AFilingThatLands…`, `Refile_AnUnrelatedWrite…`, `Refile_CancelledAfterTheLinkWrite_…` (renamed in round 2) |
| R2a | Confirm does not refuse an already-rejected document (#2) | 1 | `Confirm_DocumentAlreadyRejected_Is409_…` |
| R2b | The mark overwrites a reject that landed in between (#2) | 1 | `Confirm_RejectLandsBetweenTheCreateAndTheMark_…` |
| R2c | The job does not refuse a rejected document (#2) | 1 | `ConfirmSubmission_DocumentRejected_IsRefused_…` |
| **R3 (seeded)** | Document core-record comparison removed (#3) | 3 | `DocumentFiledUnderAnotherCoreRecord_Is409_…` (matter, project, work assignment) |
| R4 | A retry after Failed/Rejected answers with the earlier submission (#4) | 4 | `Retry_AfterTheSubmissionEnded_…` (Failed, Rejected), `Confirm_RetryAfterTheSubmissionFailed_…`, `ConcurrentCreate_AlreadyCreated_AlreadyEnded_…` |
| R5a | `AlreadyCreated` answered Received without reading the row (#5) | 2 | `ConcurrentCreate_AlreadyCreated_ForAnotherMatter_…`, `…_AlreadyEnded_…` |
| R5b | Core record dropped from the retry / `AlreadyCreated` comparison (#5) | 2 | `Confirm_RetryWithAnotherMatter_Is409`, `ConcurrentCreate_AlreadyCreated_ForAnotherMatter_…` |
| R6 | `KeyNotFoundException` in the mark falls to `StatusNotUpdated` (#6) | 1 | `Confirm_DocumentDeletedAfterTheSubmission_…` |
| R8 | Confirm asks the matter for `finance.attach_invoice` (#8) | 1 | `Confirm_MatterWithAppendToButNotRead_IsDenied_…` |
| R2/R6 rendering | `ReviewDecisionChanged` not mapped to 409 | 2 | `Confirm_ServiceFailure_IsRenderedWithItsMessage_…` (ReviewDecisionChanged, with and without a submission) |

The analyzers reject constant-`false` code (CS0162, CS8359), so four reverts use a condition that is false only at runtime (`Environment.TickCount64 < 0`).

**Things noticed, not fixed.** `InvoiceSubmissionAfterSteps.cs:10` still says the persister "is the only creator of the invoice" (job-scoped wording that predates 050a). The persister's live-row check runs after the job's AI read, so a refused confirm submission still pays for one AI read. The fixes interleave in the same files, so they are one code commit, not one per finding.

### Round 2 (uac-r2 review of 050a, 2026-10-20)

Code: `916ae9449` (item 1, D-140) and `a5dafd714` (items 2-5). Two commits follow them: `dcf4edce5`, the K1 token fix (item 4), and `d6293e174`, the D-140 columns as a projection of the canonical map (found by the ArchTests run). These notes come last.

| # | Finding | Fix | Tests (fail before the fix) |
|---|---|---|---|
| 1 (D-140) | Confirm compared only the document's own `sprk_matter` / `sprk_project` / `sprk_workassignment`. An email attachment has none, so it could be confirmed onto any matter. | `InvoiceSubmissionService.ResolveFilingAsync` resolves the EFFECTIVE core record through the filing chain. (a) The document's own core lookups, both families (`sprk_matter` and `sprk_relatedmatter`, and so on). (b) Else its communication's (`sprk_relatedcommunication`) `sprk_regardingmatter` / `sprk_regardingproject` / `sprk_regardingworkassignment`. (c) Else its parent document (`sprk_parentdocument`), the same way. The bound is **4 documents** (`MaxFilingChainDocuments`), each with at most one communication read. Past the bound, or on a loop: 409 (fail closed). A chain row that cannot be read: 503 `record_unavailable`. A row that is gone files nothing. Another record: 409 `document_core_record_mismatch`. Nothing resolves: allowed (D-139). | `InvoiceSubmissionExistingDocumentTests` (11 cases, section 6) |
| 2(a) | The persister's live-row check and its commit were not atomic: a filing, reject or delete in between was committed over. | A conditional `UpdateRequest` (`IfRowVersionMatches`, at the version the check read) on `sprk_document` joins the transaction. It writes `sprk_invoicereviewstatus` back exactly as read, and never `sprk_invoice`. On a non-unknown transaction fault the row is re-checked: a refusal now is answered in the same delivery (no invoice committed); otherwise it is the transaction's Retry. | `ConfirmSubmission_Guard_*`, `…ChangeBetweenTheCheckAndTheCommit_RollsBack_AndIsRefused` (filed, rejected, deleted), `…UnrelatedWrite…_IsRetry`, `…UnlinkedDocument_CommitsWithoutTheDocumentLink` (now expects the guard) |
| 2(b) | The window between the commit and the re-file had no defined outcome. | A refused re-file sets `SubmissionAfterStepsResult.DocumentNotFiled`. The handler leaves the submission Processing (no status write) and Poisoned, and `sprk_adminattention` says the invoice is committed but its document is NOT filed under it. A replay resumes at the re-file and completes once the cause is resolved (section 4, "What is left"). | `RefileRefusedAfterTheCommit_StaysProcessing_…_AndAReplayCompletes` (4 codes), `OtherRefusal_KeepsTheOrdinaryStoppedNote` (must not fire), `Refile_Stop_WritesNoLaterStep` |
| 2(c) | The handler called such a refusal "a bug". | The comment (case and class remarks) now describes the window and the replay. | - |
| 3 | The re-file did not read the review status, so a stalled reject could be filed under the invoice. | `sprk_invoicereviewstatus` joins the re-file's row read; `RejectedNotInvoice` is Refused `document_rejected`, with no link and no owner move. | `Refile_RejectedDocument_IsRefused_NothingMoves`, `Refile_ConfirmedDocument_IsRefiled` (control) |
| 4 | No test re-filed an already-isolated document. K1 meant nothing, because the doubles ignored the token. | Tests added. The document read's link write throws on a cancelled token. The re-file's reconciler gets a share seam that honours the token (`TokenHonouringShares`, local to the test; the shared `FakeRecordShareTable` is unchanged). The K1 cancel lands after the resolver returns. | `Refile_AlreadyIsolatedDocument_UnderItsOwnSecureMattersInvoice_StaysIsolated`, `Refile_IsolatedDocumentWithNoSecureParent_IsNeverReleasedByTheJob`, `Refile_CancelledBeforeTheLinkWrite_WritesNothing`, K1 `Refile_CancelledAfterTheReFileLanded_StillRunsAfterRefile` |
| 5 | A fault on confirm's pre-intake review-state read was a 500. | `InvoiceReviewFailure.ReviewStateUnreadable` (`sdap.finance.invoice_review.review_state_unreadable`) → 503 "Nothing was saved; retry the confirmation". | `Confirm_ReviewStateReadFaults_IsReviewStateUnreadable_AndNothingIsSubmitted`; contract row → 503 |

**The guard's column, and why (2(a)).** `sprk_invoicereviewstatus` is written back with the value the check read.
- It is already in the check's column set.
- It is not the link, so the single-writer rule for `sprk_invoice` holds.
- Its only other writers (the confirm's mark and the reject) are themselves conditional on the row version.
- A real column write makes Dataverse evaluate `IfRowVersionMatches` and move the version, which also closes the stalled-reject window. An update with no attributes is undocumented and could not be verified live.
- The cost: `modifiedon` moves, and the confirm's own mark may meet one more 412, which it already retries.

**Which document columns (D-140 column question).**
- Both families are live filing links: the unprefixed `sprk_matter` / `sprk_project` / `sprk_invoice` / `sprk_workassignment` and their `sprk_related*` twins.
  - `DocumentLinkFields.All` (`Spaarke.Dataverse/Models.cs`) and `SecureChildLineage` treat all of them as parents.
  - `DocumentLinkFieldMap.SupersededBy` records a planned migration, not a dead column.
- The D-140 check therefore reads both families for the core record. Its columns are a named projection of `DocumentLinkFields.All` (every link whose target is a matter, project or work assignment; exclusions written down in the remarks). The first version hard-coded the six columns, and `DocumentLinkVocabularyGuardTests` (ArchTests) refused it as a second copy of the vocabulary. The communication hop's column also comes from the map.
- Not followed: `sprk_email` (the OOB `email` activity). The brief names the communication (`sprk_relatedcommunication`) only, and the e-billing intake files through `sprk_communication`. A document linked ONLY to an OOB email, with no own or communication core link, resolves to nothing and is allowed (D-139). This is for the owner, if OOB email filing should count.
- The persister's live-row check and `InvoiceDocumentRefile` keep `sprk_invoice` on purpose. It is the e-billing link that the reject check, the review queue and indexing read.
- `sprk_relatedinvoice` has no BFF writer other than Compose's copy-forward (`ComposeService`, through `DocumentLinkFields`). It is a resolver parent, so it cannot cause an isolation leak. **No change.**

**Mutations (round 2).** Script `mut050a2.py`. It applies each revert in place, rebuilds `tests/unit/Sprk.Bff.Api.Tests`, runs the filter and restores the file, and checks the restore with `git diff --quiet`. All restores were clean.

| # | Reverted behaviour | Failed | Caught by |
|---|---|---|---|
| N1 | 2(a): guard dropped from the transaction | 6 | the guard-shape test, the 3 rolled-back refusals, the unrelated-write Retry, `…UnlinkedDocument_CommitsWithoutTheDocumentLink` |
| N2 | 2(a): no re-check after the rolled-back transaction | 4 | the 3 rolled-back refusals, the unrelated-write Retry |
| N3 | 2(b): after-steps do not flag `DocumentNotFiled` | 1 | `Refile_Stop_WritesNoLaterStep` (Refused) |
| N4 | 2(b): handler ignores the flag (the ordinary stopped note) | 4 | `RefileRefusedAfterTheCommit_…` (4 codes) |
| N5 | 3: re-file ignores a rejected document | 1 | `Refile_RejectedDocument_IsRefused_NothingMoves` |
| N6 | 4: M3a, `wasIsolated` forced false | **0 (equivalent)** | See below. |
| N7 | 4: M3c, `AfterRefileAsync` on the job's token | 1 | K1. It survived (0) until the share seam honoured the token. |
| N8 | 5: confirm read fault not caught | 1 | `Confirm_ReviewStateReadFaults_…` |
| **N9 (seeded)** | D-140: communication hop dropped | 5 | another matter (A→B), another project, another work assignment, the parent's communication, communication unreadable |
| N10 | D-140: `sprk_related*` twins not read | 1 | `RelatedMatterTwin_UnderAnotherMatter_Is409` |
| N11 | D-140: parent-document hop dropped | 2 | `ParentDocumentChain_IsResolved_…`, `ParentDocumentChain_PastTheBoundOrLooping_IsRefused` |

**N6 / M3a is an equivalent mutation on the job's path. The brief asked for it to be killed; it cannot be.**
- `wasIsolated` is consulted only when the row is NOT isolated after the re-file:
  - `SecureChildShareSynchronizer.AfterRefileAsync` goes on to remove the mirror only when the mirror is NotApplicable;
  - `SecureChildReconciler.RefileStepsAsync` sets `mayRelease = before && !now`.
- So it changes behaviour only for a document that LEFT isolation in the re-file.
- The job's re-file cannot do that. The real resolver refuses a move out of a Secure matter's invoice without F3 (`Refile_IsolatedDocumentWithNoSecureParent_IsNeverReleasedByTheJob`, `Refile_DocumentUnderASecureMattersInvoice_IsNeverReleased_…`).
- An already-isolated document re-filed under its own Secure matter's invoice stays isolated (`now` is true). `before` is read, but it decides nothing there (`Refile_AlreadyIsolatedDocument_…` pins that it stays isolated and keeps its mirror).
- The read is kept because the reconciler's contract requires it of every writer. It becomes live if F3 is ever granted to the job.

### Round 3 (uac-r2 round 2 review of 050a, head `857260dbb9`, 2026-10-20)

Code: `bf44f1728e` (F1-a and F1-b) and `1af1877dda` (F2). These notes come last. The branch was not rebased or merged with master (uac-r2 reviews the range as a diff).

**Which F1-a version: the full one.** `SecureChildShareSynchronizer.SecureRootsAboveAsync` (uac-r2 task 147) is already `public` on this branch (`Services/Access/SecureChildShareSynchronizer.cs:665`), and so is `IsSecureOwnerTeamAsync`. No merge was needed and no uac-r2 file was changed or widened.

| # | Finding | Fix | Tests (fail before the fix) |
|---|---|---|---|
| F1-a | "Unfiled" was decided from the D-140 column list (`ResolveFilingAsync`: six core columns, the communication's three `sprk_regarding*`, the parent chain), which is narrower than the isolation rule (`RecordOwnershipResolver.OwnershipParentEntities`: ANY ownership parent). A thread-joined communication, a communication regarding a Secure matter's invoice / event / budget, or a document's own `sprk_relatedevent` / `sprk_relatedtodo` / `sprk_relatedagreement` / `sprk_relatedinvoice` slipped through as "unfiled", and the job committed the invoice under the confirm's matter. | New `ExistingDocumentIsolationCheck` (`Services/Finance/Submission`). It reads the document with every lineage lookup of `sprk_document` (`SecureChildLineage`, the one map, never a list) and its owner, and asks `SecureRootsAboveAsync` for its secure roots, the ownership resolver's own walk. **Allowed** only when every secure root above the document is the confirm's own record (none, or exactly that one). **409** `document_core_record_mismatch`: a root other than the confirm's record (an ordinary matter named, or another Secure matter); roots undetermined (a missing ancestor, a root flagged secure but not isolated, a filing deeper than the walk); or a document owned by the Secure Record owner team with no root found above it. **503** `record_unavailable`: a Dataverse fault on any read, or the walk's Failed (an ambiguous owner team). No Secure Record owner team in the environment (NotApplicable) isolates nothing, so every document is allowed there. `InvoiceSubmissionService.CheckExistingDocumentAsync` runs it after the D-140 check, which stays. | `InvoiceSubmissionExistingDocumentTests`, 12 cases over the REAL synchronizer and an in-memory `SecureChildShareWorld`. **Must fire:** `ThreadJoinedCommunication_OnASecureMatter_ConfirmedOntoAnOrdinaryMatter_Is409` (the review's scenario), `RelatedEventOnly_UnderASecureMatter_Is409`, `CommunicationRegardingASecureMattersInvoice_Is409`, `FiledUnderTheConfirmsMatter_ButIsolatedThroughASecureThread_Is409` (variant 1), `IsolatedUnderS_ConfirmedOntoAnotherSecureMatterS2_Is409` (variant 2), `IsolatedUnderSAndS2_ConfirmedOntoS2_Is409`, `SecureOwnedDocument_UnderNoSecureRoot_Is409`, `DocumentUnderAFlaggedNotIsolatedRoot_Is409`, `IsolationReadFaults_Is503_AndWritesNothing`. **Must not fire:** `TrulyUnfiled_NotIsolated_IsReceived`, `SecureOwned_IsolatedExactlyUnderTheConfirmsMatter_IsReceived`, `FiledUnderTheConfirmsOrdinaryMatter_NotIsolated_IsReceived`. |
| F1-b | The filing and isolation check ran only at confirm, not at commit. The persister's live-row check covered only the invoice link, a reject and a delete. | `InvoiceSubmissionPersister.CheckExistingDocumentAsync` runs the same `ExistingDocumentIsolationCheck` against the submission's core record. It therefore runs before the transaction AND in `RecheckExistingDocumentAsync` after a rolled-back transaction. A refusal is Refused `document_core_record_mismatch`: the job marks the submission Failed and no invoice exists. An unreadable isolation is Retry `document_isolation_unreadable`: nothing is committed. The re-check returns only a Refused result; any other outcome stays the transaction's own Retry. | `InvoiceSubmissionPersisterTests`: `ConfirmSubmission_DocumentIsolatedUnderAnotherSecureRecord_IsRefused_NoInvoice` (isolated after the confirm, before the commit), `ConfirmSubmission_FiledUnderASecureRecordBetweenTheCheckAndTheCommit_RollsBack_AndIsRefused` (the recheck-after-rollback path: the guard faults, the re-check refuses in the same delivery), `ConfirmSubmission_IsolationUnreadable_IsRetry_NothingCommitted`, and the must-not-fire `ConfirmSubmission_IsolatedUnderItsOwnSecureMatter_Commits`. Refused → Failed is the handler's existing mapping (`FailAsync`, covered by the handler tests). |
| F2 | When a re-file Retry ran out, the administrator was not told that the document is unfiled. The two re-file Retry stops set no `DocumentNotFiled`, and the final delivery wrote the one-argument `StoppedNote`. | `InvoiceSubmissionAfterSteps` sets `DocumentNotFiled = true` on both re-file Retry stops (the reparent threw: `document_refile_failed`; the isolation read faulted twice: `document_isolation_unreadable`). `InvoiceSubmissionProcessingJobHandler.MapAfterStepsAsync` passes it to `TransientAsync`, and the final delivery writes `StoppedNote(code, documentNotFiled)`. | `InvoiceSubmissionProcessingJobHandlerTests.RefileRetry_OnEveryDelivery_FinalNoteSaysTheDocumentIsNotFiled` (both codes: Failed, Failed, then Poisoned, no status write, and the note says the invoice is committed and its document is NOT filed). `InvoiceSubmissionAfterStepsTests`: `Refile_Stop_WritesNoLaterStep` (now expects the flag on the Retry too) and `Refile_Throws_IsRetry_WritesNoLaterStep`. **Must not fire:** `OpenDifferent_ChangedSinceRead_Retries` (a Retry after the re-file is not flagged) and the existing `LinkedFinalDelivery_SetsAdminAttention_EarlierDeliveryDoesNot` (ordinary note). |

**The rule is stricter than "the confirm's record is among the roots", on purpose.** A document under S and S2, confirmed onto S2, is refused (`IsolatedUnderSAndS2_ConfirmedOntoS2_Is409`). Its content committed under S2 alone would reach S2's sharees, while the document itself is visible only to the sharees of both (the mirror is the intersection). The literal rule is mutation R3-3 below. This is for uac-r2 to confirm.

**The email activity.** `sprk_email` is not followed, as the review asked. It is not a lineage lookup of `sprk_document` (`SecureChildLineage.DocumentLinksThatAreNotFiling`), and an isolation-based check covers legacy rows: a document isolated through any lineage parent is seen whatever its email link says.

**§11, new surface: `ExistingDocumentIsolationCheck`, its verdict type and its DI registration (`FinanceModule`, scoped).**
- **Existing:** `SecureChildShareSynchronizer.SecureRootsAboveAsync` and `IsSecureOwnerTeamAsync`. Both are reused, never re-implemented. The D-140 chain is a narrower column list, which is the finding.
- **Extension:** the walk answers "which secure roots", not "may this confirm name this record". That decision belongs to the e-billing intake, and is not added to a uac-r2 file. One type, because the intake (at confirm) and the persister (at commit and on the re-check) must decide it identically.
- **Cost of doing nothing:** an isolated document is confirmed onto an ordinary matter as "unfiled", and the job commits the invoice and its content under that matter (F1-a).

The intake service and the persister each take one new constructor parameter. There is no new endpoint, no package and no AI call.

**Mutations (round 3).** Script `mut050a3.py` (scratchpad). It applies each revert in place, rebuilds `tests/unit/Sprk.Bff.Api.Tests`, runs the brief's filter, then restores the file with `git checkout` and checks the restore with `git diff --quiet`. All restores were clean. R3-1, R3-7 and R3-10 are the seeded mutations, one per finding: each reverts that finding's fix. The analyzers reject constant-`false` code, so some reverts use a condition that is false only at runtime (`Environment.TickCount64 < 0`).

All 13 reverts were caught.

| # | Reverted behaviour | Failed | Caught by |
|---|---|---|---|
| **R3-1 (seeded, F1-a)** | Confirm ignores the isolation verdict | 9 | Every confirm must-fire case: thread-joined, `sprk_relatedevent`, the communication regarding an invoice, variant 1 (filed under M2), variant 2 (S onto S2), S and S2, Secure-owned with no root, flagged-not-isolated root, the read fault (503) |
| R3-2 | The walk reads a column list (the D-140 core columns plus the `sprk_related*` twins) instead of the lineage map | 5 | `CommunicationRegardingASecureMattersInvoice_Is409` (user-owned document), `IsolatedUnderSAndS2_…`, both read-fault tests, and `SecureOwned_IsolatedExactlyUnderTheConfirmsMatter_IsReceived` (now refused). The thread-joined and event cases stay refused under this mutation, because the Secure-owned-with-no-root rule catches a Secure-owned document whose route is not read. That rule is the second line of defence, and R3-5 pins it on its own. |
| R3-3 | The literal rule: allowed when the confirm's record is among the roots | 1 | `IsolatedUnderSAndS2_ConfirmedOntoS2_Is409` |
| R3-4 | Undetermined roots ignored | 1 | `DocumentUnderAFlaggedNotIsolatedRoot_Is409` |
| R3-5 | A Secure-owned document under no root read as unfiled | 1 | `SecureOwnedDocument_UnderNoSecureRoot_Is409` |
| R3-6 | A fault on the isolation read answers Allowed | 2 | `IsolationReadFaults_Is503_…` (confirm), `ConfirmSubmission_IsolationUnreadable_IsRetry_…` (commit) |
| **R3-7 (seeded, F1-b)** | The persister does not decide isolation at commit | 3 | `ConfirmSubmission_DocumentIsolatedUnderAnotherSecureRecord_…`, `…FiledUnderASecureRecordBetweenTheCheckAndTheCommit_…`, `…IsolationUnreadable_IsRetry_…` |
| R3-8 | The re-check after a rollback drops an isolation refusal | 1 | `ConfirmSubmission_FiledUnderASecureRecordBetweenTheCheckAndTheCommit_RollsBack_AndIsRefused` |
| R3-9 | The persister commits when the isolation is unreadable | 1 | `ConfirmSubmission_IsolationUnreadable_IsRetry_NothingCommitted` |
| **R3-10 (seeded, F2)** | The reparent-fault Retry is not flagged `DocumentNotFiled` | 1 | `Refile_Throws_IsRetry_WritesNoLaterStep` |
| R3-11 | The isolation-unreadable Retry is not flagged | 1 | `Refile_Stop_WritesNoLaterStep` (IsolationUnreadable) |
| R3-12 | The final delivery writes the one-argument `StoppedNote` | 2 | `RefileRetry_OnEveryDelivery_FinalNoteSaysTheDocumentIsNotFiled` (both codes) |
| R3-13 | `MapAfterStepsAsync` does not pass the flag to `TransientAsync` | 2 | the same theory (both codes) |

**Known limits (no code change).**
- **K1: the guard's value-preserving write is unproven live.** The round 2 guard writes `sprk_invoicereviewstatus` back as read, so that Dataverse evaluates `IfRowVersionMatches` and moves `versionnumber`. That this write moves the version is unproven against Dataverse. One dev data-mutation test is recommended before deploy. After #2150 merges, do NOT switch the guard to `BuildConditionalUpdate`: it drops null values, and the guard writes a null when the document has no review decision yet.
- **K2: #2150 makes a confirm catch dead.** Once #2150 merges, its `FinanceEndpoints` confirm catch of `RecordOwnerUnsettledException` and that catch's contract test become dead code. Whoever merges second removes them. In the job, the exception becomes Retry `document_refile_failed`, which F2 now covers: the final delivery tells the administrator the document is not filed.
  - **Round 4 addition (uac-r2, 2026-10-20).** #2150 also moves thread-parent resolution into `Services/Dataverse/RecordThreadFiling.ParentsOf`, which `ContextFor` will use. It reads `sprk_threadtype`: a Direct thread (100000001, the master thread included) contributes no parents; a Record thread (100000000) or an untyped thread contributes its regarding parents. The round-4 D-140 thread hop in `InvoiceSubmissionService.ResolveFilingAsync` reads the thread's regarding columns whatever the type. Today the two agree, because the BFF never writes a regarding on a Direct thread (only a hand edit makes that shape). Whoever merges second switches the hop to `RecordThreadFiling.ParentsOf`, so one copy of the rule remains. uac-r2 tells us when #2150 merges.
- **K3: the Poisoned state.** While the job is Poisoned, the approval stays actionable. `document_not_found` cannot be fixed by replay: the administrator must decide what happens to the invoice.
- **K4 (new, noticed in this round): the commit's guard covers the document's own row only.** The persister's isolation check and its transaction are atomic only for the DOCUMENT's row: the guard is conditional on its version. A change to ANOTHER row of the lineage is not caught. Example: the document's communication joins a Secure thread in the milliseconds between the check and the commit. The re-file that follows cannot release the document: the resolver re-derives secure-if-any, so the document stays isolated. But the invoice would already be committed under the confirm's record. The window is the gap between one read and the transaction. Closing it would need a guard on every lineage row, which is not proposed here.
- **Also noticed, not changed:**
  - A confirm retry that finds its earlier submission (`AnswerWithEarlierAsync`) does not re-run the document checks. The job decides isolation at commit (F1-b).
  - The persister does not re-run the D-140 ordinary-record chain at commit. This was outside the brief. A document re-filed under another ORDINARY record after the confirm is committed under the confirm's record, a mismatch but not an isolation leak.
  - Undetermined includes "a parent row does not exist", so a document with a dangling lookup is refused (fail closed). Dataverse normally clears a lookup when its target is deleted.

### Round 4 (uac-r2 round 3 review of 050a, base `38368ddbdc`, 2026-10-20)

Code: `7635a1ff9b` (F3, tests only) and `e8fc8e6e91` (F1). These notes come last. Not rebased or merged with master.

| # | Finding | Fix | Tests (fail before the fix) |
|---|---|---|---|
| F3 | The walk's Failed (an ambiguous Secure Record owner team) and NotApplicable (no Secure Record BU) branches of `ExistingDocumentIsolationCheck` were untested. `IsolationReadFaults_Is503` reached Unavailable through the catch, not through `default:`. A `default:` → Allowed mutant survived: with a duplicated owner team, every isolated document would pass at confirm and at commit. | Tests only. The production code was already right. | **Must fire:** `AmbiguousSecureOwnerTeam_AtConfirm_Is503_AndWritesNothing` (503 `record_unavailable`), `ConfirmSubmission_AmbiguousSecureOwnerTeam_IsRetry_NothingCommitted` (Retry `document_isolation_unreadable`, no transaction). **Must not fire:** `NoSecureRecordBusinessUnit_IsReceived` (`SecureChildShareWorld.WithoutSecureBusinessUnit()`). The ambiguous team is a second `Team(...)` with the owner team's name, type and business unit (`InvoiceSubmissionExistingDocumentTests.AddAmbiguousSecureOwnerTeam`). |
| F1 (pre-existing) | The D-140 chain (`ResolveFilingAsync`) read only the communication's own `sprk_regarding{matter,project,workassignment}`. A firm's reply on ordinary matter S's thread, where the communication itself names no core record, read as unfiled. The isolation check found no secure root, so the invoice could be committed under M from S's email. | The communication hop also reads the communication's thread (`sprk_communicationthread`; the column comes from `SecureChildLineage`, the one map) and the thread's same three regarding columns. It takes the **union** of the communication's own core records and the thread's: the rule `SecureChildReconciler.ContextFor` applies (the code comment cites it). A thread that is gone files nothing. A thread read fault propagates to the chain's existing catch: 503 `record_unavailable`, exactly as a communication read fault does. The comment block at the top of `ExistingDocumentIsolationCheck.cs` now says what the chain reads. | **Must fire:** `ThreadOnlyReply_UnderAnotherMatter_Is409_AndWritesNothing`, `CommunicationRegardingM_OnSsThread_IsRefusedForMAndForS` (both directions), `ThreadUnreadable_Is503_AndWritesNothing`. **Must not fire:** `ThreadOnlyReply_UnderTheConfirmsMatter_IsReceived`, `ThreadGone_FilesNothing_IsReceived`. Against the round-3 service file, 3 of the new tests fail; the 2 must-not-fire tests pass. |

**The union rule, and why (F1).** The brief asked for "the communication's own regarding wins; the thread only when the communication names none". `SecureChildReconciler.ContextFor` does not do that: it takes `ParentsOf(communication)` and then adds `ParentsOf(thread)`. So precedence would have made the two disagree, which is what uac-r2 asked to avoid. The coordinator chose the union.
- The communication's lineage is the union of both parents. An invoice from it may only be filed where every parent agrees.
- **A deliberate refusal:** a communication regarding M on S's thread is 409 `document_core_record_mismatch` whether it is confirmed onto M or onto S. The administrator re-files the communication (or its thread), or confirms the attachment onto the right record.
- ContextFor itself is unchanged.
- The D-140 precedence one level up stays: the document's own core lookups still decide before its communication.

**K4: accepted known limit, hardening not done.** uac-r2's reasoning for accepting it:
- The invoice has no document lookup in its lineage, so the reconciler never narrows the invoice.
- The end state equals a legitimate thread join one second after the commit.
- No check-time gate can enforce a stronger invariant.

The optional hardening was tried and backed out, because it is not cheap. The idea: `InvoiceDocumentRefile` re-runs `ExistingDocumentIsolationCheck` against the invoice's core record before the re-file. That takes one constructor parameter and one read of the invoice's core lookups, with no new DI. But it runs ahead of the resolver, so it takes over refusals the resolver owns, and three existing re-file tests fail:
- `Refile_DocumentUnderASecureMattersInvoice_IsNeverReleased_TheRealResolverRefusesWithoutF3` and `Refile_WhenTheRealResolverRefuses_NothingIsWritten` now answer `document_core_record_mismatch` instead of the resolver's own code (`record_owner_secure_parent_not_isolated`, …). Those tests prove the resolver backstop (no move out of a Secure record without F3), and they would stop exercising it.
- `Refile_WhenTheIsolationReadFaultsTwice_WritesNothing` sees a different read order.

That changes the re-file's refusal contract, and so the reason codes the administrator's note carries. Doing it properly needs a decision on which refusal wins, so it was not done. The race stays visible after the fact: the reconciler keeps the document isolated, and the invoice stands under its record.

**K1 items uac-r2 accepted (no change).**
- After a reparent fault, the administrator's note says the document is "NOT filed" while `sprk_invoice` may already be set (the link write landed and the owner assignment faulted). The action asked of the administrator (resolve the document, then resume) is still right. The note's wording was not changed: the text is pinned by the handler tests and is user-visible.
- A confirm that lands while the record is mid "make secure" is Undetermined (a root flagged secure but not isolated). It is refused, so it fails closed.
- `ExistingDocumentIsolationCheck` (around `:136`) resolves the Secure Record owner team a second time in `IsSecureOwnerTeamAsync`, after the walk resolved it once. The race between the two reads is negligible: renaming or adding an owner team is an administrator action.

**Mutations (round 4).** Script `mut050a4.ps1` (scratchpad). It applies the revert in place, rebuilds `tests/unit/Sprk.Bff.Api.Tests`, and runs the brief's filter (`Finance|SecureChild|Submission|Isolation`). It then restores the file with `git checkout` and checks the restore with `git diff --quiet`. All restores were clean.

| # | Reverted behaviour | Failed | Caught by |
|---|---|---|---|
| **R4-1 (seeded, F3)** | `ExistingDocumentIsolationCheck`: `default:` (walk Failed / Incomplete) returns Allowed | 2 | `AmbiguousSecureOwnerTeam_AtConfirm_Is503_…`, `ConfirmSubmission_AmbiguousSecureOwnerTeam_IsRetry_…` |
| R4-2 | NotApplicable (no Secure Record owner team) answers Unavailable | 1 | `NoSecureRecordBusinessUnit_IsReceived` |
| **R4-3 (seeded, F1)** | The thread hop removed (`Environment.TickCount64 < 0 &&`) | 3 | `ThreadOnlyReply_UnderAnotherMatter_Is409_…`, `CommunicationRegardingM_OnSsThread_IsRefusedForMAndForS`, `ThreadUnreadable_Is503_…` |
| R4-4 | Precedence instead of the union (the thread read only when the communication names none) | 1 | `CommunicationRegardingM_OnSsThread_IsRefusedForMAndForS` |
