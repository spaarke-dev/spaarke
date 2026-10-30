# Task 097a: the Superseded lifecycle status and the duplicate-rule fix

Part one of task 097 (corrected invoice supersedes), split per EB-D-179. Rigor FULL. Branch `task/ebill-097a-superseded-status` from `ababcfd57b`.

## Schema check (escalation trigger, not fired)

The option exists: `sprk_invoice.statuscode` 659490005 "Superseded" at statecode 1 (Inactive), inserted by `scripts/ebilling/Deploy-EbillingStoredFileSchema.ps1` F5 (task 088, dev 2026-10-22), recorded in `docs/data-model/sprk_invoice.md`. No schema script was run.

## What changed

- `InvoiceLifecycleStatus.Superseded` and `InvoiceStatusCodes.Superseded = 659490005` (Inactive), in `StatusCodeOf`, `StateCodeOf` and `TryFromStatusCode`. The enum value is appended, so no existing value moves.
- Two request builders on `IInvoiceLifecycleService`, modelled on `BuildInvoiceCreateRequest` (pure, no I/O, returned not sent):
  - `BuildSupersedeRequest(invoiceId, current, rowVersion)`: In Review to Superseded.
  - `BuildSupersedeUndoRequest(invoiceId, current, rowVersion)`: Superseded to In Review, the only way out of Superseded.
  - Each returns `InvoiceTransitionRequestResult` holding one `UpdateRequest`: the statecode + statuscode pair, `RowVersion` = the version the caller read, and `ConcurrencyBehavior.IfRowVersionMatches`.
  - Any other starting status is refused with `invoice.transition_not_allowed` and no request. A row version of 0 or below throws, because the move could not be conditional.
- `TransitionAsync` refuses both Superseded moves. `IsAllowed` is unchanged; the two pairs live in a separate `IsAllowedInTransaction`. Decision: "no other path may move into or out of Superseded" was read strictly. Only the transaction builders reach or leave it, so no caller can supersede an invoice outside a correction's commit.
- Status readers:
  - Approve, reject and return: a Superseded invoice gets the existing 409 `not_in_review`.
  - Override and return of a blocked submission: the existing 409 `invoice_not_held`; the GET offers neither action.
  - Review read: status `"Superseded"`, not held, `CanDecide` false.
  - Invoice index document: status `"Superseded"` with its code.
  - Context panel: the prior invoice shows as `"Superseded"` and is excluded from disallow history (the decided allowlist).
  - AP export: refused `invoice_not_approved`, never a candidate.
  - Spend: not counted in spend or pending spend; both sets are allowlists.
  - Readers that already compared against In Review or Approved needed no code change. Approve, reject/return and override gained one comment each.
- Duplicate fix: `BillingFactLoader.StatusOf` maps Superseded to null, as Cancelled is, so the Duplicate rule never sees a Superseded invoice as a prior. Its remarks were updated to match. ontology-r1 owns `Services/Signals/Billing/**` and reviews this change.
- Docs: `docs/data-model/sprk_invoice.md` (Superseded row, transitions paragraph, live-run note) and the status comment in `infrastructure/ai-search/spaarke-invoices-index.json` (comment only; nothing deployed).

Component justification (§11), for `InvoiceTransitionRequestResult` and the two builder methods:
- **Existing:** `InvoiceCreateRequestResult` and `BuildInvoiceCreateRequest`.
- **Extension:** that result type is named and documented for a create. Reusing it for an update would mislead 097c's reader.
- **Cost of doing nothing:** 097c has no way to put the old invoice's close or reopen in its commit without becoming a second status writer, which FR-06 / ADR-002 forbid.

## Tests

- **Lifecycle:**
  - Every pair across 8 statuses × 8: 7 moves, 8 no-ops, 49 refused.
  - In Review to Superseded and Superseded to In Review are refused by `TransitionAsync`.
  - The live option values, Superseded included.
  - Both builders' request shape.
  - Each builder refuses every other starting status (theories over the enum).
  - The version guard (0 and -1 throw).
  - No I/O.
  - The undo at the current version reopens.
  - The undo at a stale version is refused and the invoice is left as it was.
  - The supersede at a stale version cannot overwrite an approval made since the read.
  - Superseded is not in counted or pending spend.
- **Readers:**
  - Approval: Superseded is 409 `not_in_review`.
  - Reject and return: Superseded is 409, both verbs.
  - Review read: `CanDecide` is false for Superseded; the header shows Superseded and is not held, even with a blocked submission.
  - Override: Superseded is 409 not held; the GET offers no action; return is 409.
  - Index: the document carries status Superseded.
  - Context: the prior is listed as Superseded and excluded from disallow history.
  - AP export: refused `invoice_not_approved`, and not a candidate.
- **Duplicate:**
  - Must fire: a Superseded prior is not loaded and raises no Duplicate (end to end through `InvoiceComplianceEvaluator`).
  - Must not fire: an In Review or Approved prior with the same number is still an Active prior and raises `DuplicateInvoiceNumber`.

Beyond the named set, with a one-line reason each:
- The context-panel test: it is a status reader found by grep.
- The supersede stale-version test: it is the forward twin of the undo guard.
- The spend allowlist assert: it guards the money consequence the data model doc states.

### Seeding proof (recorded)

1. Undo row-version guard: I removed `ConcurrencyBehavior = IfRowVersionMatches` from `BuildConditionalTransition`. 4 lifecycle tests failed, including `BuildSupersedeUndoRequest_AtAStaleVersion_IsRefusedByTheRowVersionGuard_AndTheInvoiceStaysAsItIs`. I restored the line and the tests passed.
2. Duplicate mapping: I commented out `InvoiceStatusCodes.Superseded => null`. `MustFire_ASupersededPrior_IsNoPrior_SoItRaisesNoDuplicate_Task097a` failed, and the other 36 loader tests passed. I restored the line and the tests passed.

## Gate

- Builds, exit 0: Sprk.Bff.Api, Sprk.Bff.Api.Tests, Spaarke.ArchTests, Sprk.Bff.Api.IntegrationTests, Spe.Integration.Tests, Sprk.Provisioning.ControlPlane.LoadTests.
- Full ArchTests: 1858 passed, 0 failed.
- Unit filter `Finance|Ebilling|Billing|Approval|Invoice|ApExport|Index`: 7562 passed, 5 skipped, 0 failed.
- Publish size (§10): base `ababcfd57b` 41,063,640 bytes; head 41,064,682 bytes; delta +1,042 bytes.
  - Both trees were exported with `git archive` to short paths and zipped with `Compress-Archive`.
  - Both publishes have 192 files.
- No package change, so no new CVE.

## Review

One full adversarial-reviewer pass of the diff (Opus). ontology-r1 also reviews the `BillingFactLoader` change, since that path is theirs.

| # | Class | Finding | Action |
|---|---|---|---|
| 1 | F4, out of 097a scope | The correction's own first evaluation runs before its commit supersedes the old invoice. At that point the old invoice is still In Review, so it is an Active prior with the same firm number (EB-D-179 (3)), and the correction raises a Duplicate against the invoice it replaces. `BillingEvaluationRequest` names no replaced invoice. | For 097b, below. The 097a mapping still covers every evaluation after the commit (re-evaluations, later submissions). The test name was narrowed to what it proves. |
| 2 | F2 | `BillingFactLoader` remarks and an inline comment named only Cancelled as not loaded. | Fixed. |
| 3 | K4 | The builders answer "already in target" with `NotAllowed`; only `Current` tells it apart from a forbidden move. | Documented on the interface; for 097c, below. |
| 4 | K4 | A `BuildTransaction(...).HaveCount(2)` assert proved nothing. | Removed. |
| 5 | K4 | A held (blocked, In Review) invoice that is corrected becomes Superseded. Its submission still reads Blocked, with no action offered. | For 097c/d, below. |
| 6 | K2 | The undo's statecode 1 to 0 conditional update inside `ExecuteTransaction` cannot be proved by unit tests. | Noted in `sprk_invoice.md` as a shape to verify on the first live run. |

## For 097b / 097c / 097d

- **097b, must do (review finding 1):** carry the replaced submission or invoice id into `BillingEvaluationRequest`. `BillingFactLoader.LoadPriorsAsync` must exclude it the way it excludes `request.SubmissionId`. Add a must-fire test with the predecessor still In Review; without it, a correction carrying the same number (EB-D-179 (3)) is flagged Duplicate, and Rejected when the policy's consequence is Reject.
- **097c, supersede:** read the old invoice's status and `versionnumber` in one read, then call `BuildSupersedeRequest(oldId, status, version)`. Put `result.Request` in the commit transaction. `Succeeded == false` means stop; it is a refusal, never send.
- **097c, undo:** read the old invoice again, then call `BuildSupersedeUndoRequest`. When `Current == InReview` (or generally `Current == Requested`), there is nothing to undo; treat it as done, not as a failure.
- **097c, re-index:** the builders cannot enqueue the index job. After the commit and after the undo, call `InvoiceIndexingJobHandler.TryEnqueueAsync` for the old invoice, as `TransitionAsync` does.
- **097c/d:** decide what the old submission shows once its invoice is Superseded. A held submission otherwise reads Blocked with no action (finding 5).
- **Resubmission link (persister `:994-997`):** it treats a Superseded invoice as not Returned to Firm, so there is no link. That is correct, because the correction is always the newer row. No change needed.

rework: fix-round-1 task 097
