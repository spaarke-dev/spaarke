# 050c: confirm follow-ups (EB-D-262, EB-D-263, EB-D-264)

Branch `task/ebilling-050c`, based on `44c9c97b65` (tip of `work/ebilling-r1`). Rigor FULL. No live Dataverse call, no deploy.

## 1. What changed

### EB-D-262: reject after a Failed confirm submission
- `InvoiceReviewService.RejectInvoiceAsync` still checks the document's invoice link first (409 `DocumentLinkedToInvoice`). It then reads the confirm submission through the new private `ReadBlockingConfirmAttemptAsync`.
- The new helper returns the first confirm attempt whose status is not exactly `Failed`; the reject refuses with the same 409 for it. No attempt, or every attempt `Failed`, lets the reject through. A read fault is still `ConfirmStateUnreadable` (503) and writes nothing. An unknown or null status blocks (fail closed).
- The reject still writes only the review status, notes and reviewed-on stamp, conditional on the document version.
- **Deviation from the brief, deliberate.** The brief reads "the confirm submission" (generation 1). Since EB-D-167 a confirm after a Failed attempt is a NEW attempt (generation 2, 3, ...), with its own submission id (`ExistingDocumentSubmissionIdFor(doc, g)`), and `SubmitExistingDocumentAsync` walks them. Reading only generation 1 would let a reject overwrite ConfirmedInvoice while a later attempt is Received/Processing/Accepted and about to create an invoice. The helper therefore walks the generations as the confirm does, so the LATEST attempt decides. For the cases in the brief (one attempt) the behaviour and the single read are unchanged.

### EB-D-263: confirm request drops InvoiceDate and TotalAmount
- Both members are deleted from `InvoiceReviewConfirmRequest`. Nothing read them. The default JSON binding ignores unknown members, so a body that still sends them is 202 as before.
- `infrastructure/dataverse/views/invoice-review-queue.md` updated (no `invoiceDate`/`totalAmount`, the rights the confirm asks, the reject refusal rule).
- Grep of `src` (ts, tsx, js, json, cs, html; no node_modules, obj, bin) for `invoice-review/confirm`, `invoice-review/reject` and `InvoiceReviewConfirm`: the only hits are the BFF files (`FinanceEndpoints.cs`, `InvoiceReviewService.cs`, `EbillingOptions.cs`, `appsettings.template.json`). No client calls the route.

### EB-D-264: confirm stops asking Create on sprk_invoice
- `FinanceEndpoints.ResolveConfirmTargets` loses the `FinanceAuthorizationCheck.CallerPrivilege(CreateInvoicePrivilege, ...)` check. It now asks: Write on the document (`finance.confirm`), Write+Append on the document (`finance.link_invoice`), Read+AppendTo on the matter (`finance.submit_invoice`), AppendTo on the firm (`finance.attach_invoice`). The last two are what `InvoiceSubmissionEndpoints.ResolveSubmitTargets` asks, so confirm now asks the manual submission's rights plus its own document rights.
- `FinanceAuthorizationFilter.CreateInvoicePrivilege` had no caller left (only a doc cref, an `EventEndpoints` comment and one unit test fixture), so it is deleted. Its cref/comment mentions and the fixture in `FinanceUniformNotFoundForRecordTests` now use the literal name.
- `FeatureRolePrivilegeGuardTests` Create-on-`sprk_invoice` row: R1 (confirm) is closed; the row stays for R4 (the wizard launcher's `Mscrm.CreateSelectedEntityPermission` display rule) and is re-cited to `sprk_Invoice/RibbonDiff.xml:162-167`. Comment in `OperationAccessPolicy.cs` (`finance.confirm`) updated; no behaviour change there.
- No ArchTests route census declares the confirm route's checks by privilege; the only affected ArchTest was the 046 role-privilege row above.

## 2. Tests

New or changed (all in the Sprk.Bff.Api.Tests assembly):
- `tests/integration/data-mutation/FinanceInvoiceReview/InvoiceReviewWritePathTests.cs`: reject after Failed is 200 and writes only the review decision; Received/Processing/Accepted/Rejected are 409 (theory); a Failed first attempt followed by a later attempt that is not Failed is 409 (theory); every attempt Failed is allowed (and the three generation reads are asserted); a Failed submission with a linked document is 409 before any submission read.
- `tests/integration/contract/Api/Finance/FinanceEndpointsAuthorizationContractTests.cs`: caller without Create but with the submit rights is 202 and no privilege is asked; a faulting privilege probe no longer decides; without AppendTo on the matter confirm is still 403 (even holding Create); a body with `invoiceDate`/`totalAmount` is 202 and the contract has neither member. `GrantFullConfirm` and the two link-right tests no longer hold Create (they now prove the refusals independent of it). The old "no Create is 403", "probe throws is denied" and "probe not registered is denied" tests are replaced (their behaviour is removed on purpose).
- `FeatureRolePrivilegeGuardTests` row re-cited; `FinanceUniformNotFoundForRecordTests` literal.

Counts after the change:
- `Sprk.Bff.Api.Tests --filter "Finance|InvoiceReview|InvoiceSubmission"`: 9760 passed, 0 failed, 4 skipped (9764).
- ArchTests `--filter "RouteAuthorization|Finance|Ownership|FeatureRolePrivilege|ExternalDocumentNarrowing"`: 235 tests, 1 failed on the first run (the 046 evidence rule wanted a `:line` on the new evidence path); fixed; `FeatureRolePrivilegeGuardTests` 55/55 passed afterwards. The full filtered set was not re-run after that one-line evidence fix.
- BFF build: 0 errors, 0 warnings.

Fail-before evidence: the three production files (`FinanceEndpoints.cs`, `FinanceAuthorizationFilter.cs`, `InvoiceReviewService.cs`) were restored from `44c9c97b65` over the new tests (class filter `InvoiceReviewWritePathTests|FinanceEndpointsAuthorizationContractTests`): 29 failed, 159 passed, then restored. Failing among the new tests:
- `Reject_AfterTheConfirmSubmissionFailed_Succeeds_AndWritesOnlyTheReviewStatus` and `Reject_EveryConfirmAttemptFailed_Succeeds` (reject after Failed);
- `Confirm_CallerWithoutCreateOnSprkInvoice_ButWithTheSubmitRights_Is202_AndNoPrivilegeIsAsked` and `Confirm_PrivilegeProbeFaults_DoesNotDecideTheConfirm` (Create no longer asked);
- `Confirm_BodyWithInvoiceDateAndTotalAmount_IsAccepted_AndTheRequestCarriesNeither` (members removed).
- Another ~20 existing confirm tests also fail on the old code because they now grant no Create privilege (the old filter denies them): that is the same change seen from the other side.
Pass before and after, by design (they pin what must not change): the reject 409 theories for Received/Processing/Accepted/Rejected, the "later attempt not Failed" theory (the old code refuses on any generation-1 submission), the linked-document 409, the existing read-fault 503 test, and "without AppendTo on the matter is still 403".

## 3. Publish size and CVE

`dotnet publish -c Release -r linux-x64 --self-contained false` from fresh worktrees (directory size of the output, uncompressed):

| Tree | Bytes | Files |
|---|---|---|
| master | 141,225,197 | 191 |
| base `44c9c97b65` | 144,380,995 | 194 |
| branch (`f4a2505bd3`) | 144,381,599 | 194 |

Task delta (branch vs base): +604 bytes. Branch vs master: +3.16 MB, all from the ebilling-r1 work already on the base (not this task). These are raw output sizes; I did not compare them to the 55/60 MB thresholds, which look like a different measure (likely compressed). `dotnet list package --vulnerable --include-transitive` on `Sprk.Bff.Api`: no vulnerable packages. No new package, DI registration, endpoint or file surface (CLAUDE.md §10, §11): the change removes surface.

## 4. Found in passing, not fixed

- `docs/architecture/sdap-overview.md:221` still says confirm is "Confirm + enqueue extraction" (stale since 050a).
- The operator doc said a retry after a Failed submission is 409 `submission_already_ended` (D-139), and the task constraint says the same. The code (EB-D-167, tested by `Confirm_RetryAfterTheSubmissionFailed_IsANewAttempt_AndMarks`) starts a new attempt after Failed; only Rejected is 409. I corrected the operator doc to match the code and did not change the confirm flow.
- The repo's pre-commit hook (lint-staged) runs `git stash` internally during `git commit`; it restored cleanly. The one stash entry in the shared list (`wave-3c-siblings-workingtree`) is another session's.
- Two reject races are unchanged and rely on the earlier design: a confirm that creates attempt N+1 between the reject's read and write is caught by the conditional write (document version) or, if it only lands after, by the job refusing a rejected document (`document_rejected`).

## 5. Fix round 1

Adversarial review of the branch (tip `18204c1cea`) found one fix-now defect (F1) and several stale statements (F2). All fixed.

1. **F1: reject treated a Failed confirm attempt with an unfinished undo as "no invoice created".** `ReadBlockingConfirmAttemptAsync` (`InvoiceReviewService.cs`) now remembers the latest Failed attempt and, when `SubmissionCommitGate.UndoUnfinished(Failed, reasonCode, invoiceId)` is true for it (reason `source_check_failed` or `file_unservable`, `sprk_invoice` still set), throws a new `InvoiceReviewFailure.EarlierAttemptBeingRemoved`. Nothing is written. `FinanceEndpoints.ConfirmFailure` renders it as 503 with `errorCode` = `ebilling.submission.earlier_attempt_being_removed`, the code and detail confirm answers for the same state (no new code; `InvoiceReviewException.ReasonCode` returns the same constant). Only the LATEST attempt decides, as for confirm. Task 094 left nothing linking the document, so the link check could not catch this state.
   - Tests (`InvoiceReviewWritePathTests`): must fire, Failed + `source_check_failed` / `file_unservable` + invoice id set gives 503 and no write (2 cases); the same on generation 2 after a finished generation 1; must not fire, Failed with no invoice id, and Failed with an invoice id but a non-undo reason, both still succeed. Contract test `Reject_EarlierAttemptBeingRemoved_Is503_WithTheConfirmRoutesErrorCode` pins the wire shape.
   - Fail-before evidence: with the new check switched off (condition `&& false`), `InvoiceReviewWritePathTests` ran 48 tests: 3 failed (the two undo-reason cases and the generation-2 case), 45 passed, including both must-not-fire cases. With the check restored: 0 failed.
2. **F2: mislabelled test.** `Reject_FailedSubmission_ButTheDocumentIsLinked_Is409` claimed to cover an unfinished undo; renamed `Reject_FailedSubmission_ButTheDocumentIsHandLinked_Is409_AndWritesNothing`, display name and comment say it is a hand-linked document (the undo case is item 1).
3. **F2: docs.** `sdap-bff-api-patterns.md:108` and `uac-access-control.md:91` now list the EB-D-264 set (`finance.confirm` Write and `finance.link_invoice` Write+Append on the document, `finance.submit_invoice` Read+AppendTo on the core record, `finance.attach_invoice` AppendTo on the firm; no Create on `sprk_invoice`). `sdap-overview.md:221` now says "Confirm + submit as an invoice (202; the job creates the invoice)".
4. **F2: operator doc.** `invoice-review-queue.md`: the job copies the document's file into the invoice's Stored File and does not link, re-file or re-own the document (task 094); the re-confirm sentence adds 503 `earlier_attempt_being_removed` (undo running) and 409 `submission_already_ended` at the attempt limit; the reject paragraph describes the new undo-unfinished 503.
5. **F2: OpenAPI.** The reject route declares `ProducesProblem(503)` (it answers 503 for the unreadable confirm state, the unreadable review state and now the undo), with the description updated. Confirm already declared 503.
6. **K1 cleanup.** `FinanceAuthHost.StartAsync(registerProbe ...)` in `FinanceEndpointsAuthorizationContractTests.cs`: no caller in that test class passed it (the only `registerProbe:` caller is in `RecordWritePermissionsContractTests`, a different host class); the parameter and its branch are removed, the probe is always registered.
7. **uac-r2 leftover (096).** The Office quick-create ledger entry in `RouteAuthorizationGuardTests.Ledger.cs` named `prvCreatesprk_Invoice`; `QuickCreateSourceAccessFilter` refuses an invoice (400 OFFICE_023) before any privilege question, so it now names Matter and Project only and says the invoice is refused first.

Counts: `Sprk.Bff.Api.Tests` filter `InvoiceReview|FinanceEndpoints|InvoiceSubmission|FinanceUniformNotFound`: 1869 passed, 0 failed. ArchTests `RouteAuthorization|Finance|Ownership|FeatureRolePrivilege|Office`: 226 passed, 0 failed. `dotnet build src/server/api/Sprk.Bff.Api/`: 0 errors, 0 warnings.

Stale POML constraint (POML left unchanged): "re-confirm after Failed or Rejected stays 409" is stale since EB-D-167. A Failed attempt starts a new attempt; only Rejected is 409.

## Live on dev (2026-11-24)

uac-r2 deployed dev BFF 0362599b94, which contains #3038, at about 05:21:45Z on the owner's go. Read-only checks: `/healthz` 200; `POST /api/finance/invoice-review/confirm` and `/reject` unauthenticated: 401. No authenticated call was made.
