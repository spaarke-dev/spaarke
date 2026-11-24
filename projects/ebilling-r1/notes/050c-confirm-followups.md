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
