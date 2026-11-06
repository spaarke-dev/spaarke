# 164a-4a: the Add line service, steps 0 to 6, and the route

Branch `task/ebilling-164a4a` (from `task/ebilling-164a` at `5103558c69`). Steps 7 to 9 (the totals, the Decision Record, the Signals) are 164a-4b.

## What it is

- `Services/Finance/InvoiceLineAddService.cs`: `AddLineCommand`, `AddLineStatus`, `AddLineCodes` (`ebilling.line_add.*`), `AddLineResult` and the service.
- The route is `POST /api/finance/invoices/{invoiceId}/lines` in `Api/Finance/InvoiceLineReviewEndpoints.cs`, with `AddLineRequest`, `AddLineResponse` and `ToResult(AddLineResult, …)`. It uses the line-decision group (`RequireAuthorization`, `RequireInternalFinanceCaller`), `AddFinanceAuthorizationFilter("ebilling.invoice.decide", sprk_invoices, invoiceId)` and `RequireEbillingEnabled`.
- The DI line is in `FinanceModule` (scoped, next to `InvoiceAdjustmentService`).
- Text updates for the new route and writer:
  - the `OperationAccessPolicy` comment;
  - the `ParentLineage` typed-writer text for `sprk_findingsjson`;
  - the route ledger `GovernedFile` text (G1; no count change, because the file is not new);
  - the census `UnscannedWriter` (G3).

## Component justification (CLAUDE.md section 11)

1. **Existing.** These already exist:
   - `InvoiceAdjustmentService` creates Adjustment lines;
   - `InvoiceLineDecisionService` decides lines;
   - `InvoiceCorrectionReevaluation` re-runs the rules after a correction;
   - the submission persister creates intake lines.
2. **Extension.** None of them adds a Fee or Expense line after intake. Giving one of them this write would give it a second invariant (ADR-002). The new service reuses their parts and copies none of them:
   - the per-invoice lock;
   - `InvoiceFirmGate`;
   - `InvoiceFindingsDiff` (`RequestBasis.ForAddedLine`, `PatchFindings`, `PatchPlan`, `GapSubjects`);
   - `InvoiceTotalsCalculator`;
   - `IRecordOwnershipResolver`;
   - `BillingDecisionRecorder.Preflight`.
3. **Cost of doing nothing.** A reviewer cannot add a line the firm did not bill (EB-D-188).

## Steps 0 to 6 as built

0. **Shape (400), with no I/O.**
   - The reason is required and at most 2000 characters (`ebilling.line.reason_required` / `reason_too_long`).
   - The description is required and at most 2000 characters. The cost type is `fee` or `expense`. The date is required.
   - **Fee:**
     - the timekeeper is required (at most 200 characters);
     - hours and rate come together, or an amount is given;
     - when all three are given, `round(h*r, 2)` must equal the amount, otherwise `amount_mismatch`;
     - takes task and activity codes only.
   - **Expense:** an amount is required; no timekeeper, hours or rate; takes the expense code only.
   - Codes are at most 20 characters. Amounts are non-zero, at most 1e9, with at most 2 decimals.
   - The endpoint runs step 0 before the decider lookup, so a malformed body is 400 without any I/O.
1. **Lock and reads.**
   - Taking the lock: busy gives 409; a fault gives 503.
   - Then, in order:
     - read the invoice (missing gives the uniform 404);
     - check it is In Review (409 `not_in_review`);
     - check for a single core record (500 `owner_unresolved`);
     - run `InvoiceFirmGate` (NoFirm, HeldForFirm or RerunPending gives 409 `firm_missing` with its own sentence; a fault gives 503);
     - read the lines;
     - check for unapplied corrections (409 `corrections_pending`);
     - read the derived row (same content gives `alreadyApplied`; other content gives 409 `id_reused`);
     - read the submission, the stored lines and the correction rows.
2. **Resolve.**
   - The code set comes from the stored Fee and Expense rows (`CodeSetResolver`). If none resolves: 409 `code_set_unavailable`. A typed code outside the set: 400 `code_not_allowed`.
   - For a Fee, the timekeeper goes through `IInvoiceResolutionService` with a one-line input (the correction's `RederiveAsync` pattern). An unmatched timekeeper is not refused; its reason is stored.
   - Sequence = the highest stored sequence + 1.
   - Then the row, the owner (500 if unresolved), the creator stamp and the core-record guard.
3. **Rules.**
   - `RequestBasis.ForAddedLine`: load once, evaluate twice.
   - A new Procedure gap is 409 `procedure_gap`.
   - `PatchFindings` and `PatchPlan` run, and the added line gets `lineFindings` `line.reviewer_added`, plus `rules.not_checked` when it applies.
   - The status step: every stored, undecided line the patch touched takes `InvoiceCorrectionReevaluation.StatusOf`, as a conditional update. The added line is skipped (164a-3 open point 5), and so is a stored line with no extraction.
4. **Totals dry run and preflight.**
   - The totals dry run includes the added line as `ReviewerAdded` (EB-D-199). A refusal is 409 `totals_refused`.
   - `BillingDecision` uses code `add-line`, subject the line, `Human(caller, "invoice-reviewer")`, the reason and outcome `Added`. Its facts are ids and codes only.
   - `Preflight` runs with no I/O. A refusal is 400 `ebilling.decision.invalid`.
5. **None in R1** (EB-D-198 Q7).
6. **One transaction** (with `CancellationToken.None`):
   - the `CreateRequest` of the line;
   - the submission findings update (`IfRowVersionMatches`), which also sets `addLine = {decisionId, lineId, state: "committed"}`;
   - the status updates (`IfRowVersionMatches`);
   - a version-only invoice update (`IfRowVersionMatches`).

   A version mismatch is 409 `ebilling.line_add.changed`. Any other fault is 503 `unavailable`, and a repeat is safe because the row id is derived.

The 200 body is `{requestId, lineId, decisionId, status, alreadyApplied, totals}`, with `totals` null until 4b.

## The id contract (EB-D-198)

- `decisionId = IdOf("ebilling-add-line-{invoiceId}-{requestId}")`: the client's `requestId` is scoped by the invoice.
- `lineId = ExtractionProvenance.AddedLineIdFor(decisionId)`.
- `sprk_extractionconfidence` starts from `ExtractionProvenance.ForReviewerAddedLine(decisionId)`, so `IsReviewerAdded(row)` holds. The marker member is unchanged; the extra members are added beside it.

## 164a-3 open points

1. **As-billed codes are the typed codes** (D-117), set to the code set's canonical form.
2. **The extraction JSON** holds:
   - `fields.{Date, TimekeeperAsBilled, Hours, Rate, Amount, Narrative}.combined = 1` (only Date, Amount and Narrative for an Expense);
   - `coding` with status `Coded`, `recoded: true`, the code set, `needsReview`, confidence 1, and each code with `firmCode = code` and `mismatch: false`;
   - `dateMissing: false`;
   - for a Fee, `resolutionChange.after.{timekeeperReason, rateStatus, rateReason}`.
3. **Signal changes: the 4b seam (below).**
4. **Extracted, not copied.** `ReadLinesAsync`, `ReadCorrectionsAsync`, `CorrectionRow` and the new `VerifiedOf` moved into `InvoiceFindingsDiff` (commit 1). `InvoiceCorrectionReevaluation` calls them there. `ReadSubmissionAsync` and `SetAdminAttention` became `internal`. The correction's coding projection is `InvoiceCorrectionService.CodingLineOf`.
5. **The status step skips the added line.** It is created Flagged (R10).

## The 4b seam

- `PatchPlan` writes its plan changes into `pendingSignalChanges.rule`, exactly as a correction does. 4a leaves them there.
  - If 4b's Signals step does not reconcile them, the next correction's Signals step will.
  - 4b decides whether they move to `addLine.signals`, and must keep the two from being reconciled twice.
- `addLine` is a single member, so a second add on the same invoice overwrites it. 4b decides whether it becomes keyed by decision id once later steps read it.
- **A repeat with a different reason** is not caught in 4a: the row compare does not include the reason, which is not stored on the row. 4b's recorder hash (the same decision id with different content) catches it.
- **`alreadyApplied` returns without running steps 7 to 9.** 4b adds the resume: when `addLine.state` is `committed`, finish the later steps.

## Deviations

- **The extraction touched more than the stored-line and correction-row reads.**
  - Two re-evaluation helpers became internal: `ReadSubmissionAsync`, `SetAdminAttention`.
  - The correction's coding projection became `CodingLineOf`.
  - Both are visibility or extract-method changes with no behaviour change: 418 correction and diff tests pass on commit 1.
- **Timekeeper resolution goes through `IInvoiceResolutionService`**, as the brief prescribes. Its `TimekeeperMatcher` has an embedding-based stage for names that do not match exactly. This is the intake path, already used by the correction.
- **An untested assumption.** A version-only conditional `UpdateRequest` on the invoice (no columns) is assumed to be checked by Dataverse against the row version. Live writes were out of scope, so it was not verified. The unit test proves only the request shape and the conflict mapping.

## Tests

- **`InvoiceLineAddServiceTests`** (38): the cases this piece covers.
  - The happy paths:
    - the id contract;
    - the Expense and Fee row shapes;
    - the extraction JSON;
    - one transaction with conditional updates;
    - the version-only invoice update;
    - `addLine` and the `lineFindings`.
  - The repeat gives `alreadyApplied`, and other content gives `id_reused`. A version conflict gives `changed`.
  - **N3:** the three firm-gate states, with a strict recorder identity and no transaction. The re-run-pending `sprk_firmhold` value is refused with the re-run sentence.
  - **N4:** the six closed statuses.
  - **N5/N6:** 17 malformed requests are refused before any read or lock; `code_not_allowed`; `code_set_unavailable`.
  - **N9:** a new Procedure gap leaves the findings unchanged; an unapplied correction.
  - **N11:** an unresolved owner maps to HTTP 500.
  - The rules judge the added line.
- **`InvoiceLineReviewEndpointsAuthorizationContractTests`:** the Add route was added to `Routes()`, so it runs through these theories:
  - N1: unauthenticated; no right; a right on another invoice; a failed check;
  - N2: the step-0 refused and admitted kinds;
  - read alone is not enough;
  - the flag off;
  - not In Review.

  There are also two Add line cases: 400 before any read or decider lookup, and 403 for an unproven identity.
- **Seeds:** see the final report for the fail counts. They are re-run on demand by reverting the named line:
  - **S1:** remove the Add route's `AddFinanceAuthorizationFilter`.
  - **S2:** remove the `InvoiceFirmGate` check.
  - **S4:** remove `RequireInternalFinanceCaller` from the group.
- **Not covered here:**
  - N7, N8 (beyond the version conflict), N10, T1 to T8 and C1 to C4 depend on steps 7 to 9 and belong to 4b;
  - P1 and P2 belong to 4b;
  - G2, G4 and G5 belong to 4b, or to the task's other pieces.
