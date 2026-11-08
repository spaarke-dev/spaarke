# 165b — integrity checks at approval, AP export and spend

Task 165 part b (EB-D-200). Rigor FULL. Branch `task/ebilling-165b` from `work/ebilling-r1` at `14fd6b7edb`.

Status: item 1 done on this branch. Items 2, 3 and 4 are not started: the brief is larger than one implementer run, and item 2 needs a
decision from the lead and the owner first (section 3). The proposed split is in section 4.

## 1. Item 1 — approval refuses a line of unknown origin

**Rule.** Approval decides a line's origin only from server-written sources. A Fee or Expense line on the invoice has a known origin when it is:

1. the invoice's own submission's intake line: `line.Id == InvoiceSubmissionPersister.LineIdFor(ownSubmission, sequence - 1)`, where the own
   submission is the one `InvoiceBlockedHold.ReadOwnSubmissionAsync` returns (165a); or
2. a line Add line created on this invoice (`ExtractionProvenance.AddedLineDecisionIdOf`: the reviewer marker, the line names this invoice, and
   `line.Id == AddedLineIdFor(invoiceId, decisionId)`) that is the subject of an `add-line` Decision Record created by the writer identity, whose
   key is `BillingDecisionKey.IdOf(sprk_billingevent, lineId, decisionId)` and whose facts name this invoice and this line.

The reviewer marker alone is not an exemption. Adjustment lines are not checked (their own service decides them).

Otherwise approval answers 409 `ebilling.approve.line_origin_unknown` before anything is written, and logs
`Approval refusal ebilling.approve.line_origin_unknown on sprk_billingevent {id}` once per line (the table and row id only). The refusal text:
"A line on this invoice has no known origin, so the invoice cannot be approved. An administrator must review the invoice's lines." A fault
reading the submission or the records is Unavailable (fail closed).

**Placement.** The check runs under both per-invoice locks, on the line set approval already reads, after the I-2 open-lines check and before
the first write.

**Why the record is server-written.** `sprk_decisionrecord` is a writer-only table (EB-D-204 / D-204): no person role holds Create, Write or
Delete on it (pinned by `FeatureRolePrivilegeGuardTests`), and the generic BFF record paths refuse it (`RecordTableRules`). The lookup also
filters `createdby` to the writer identity, as the Decision Record writer's own "already recorded" lookups do.

**Reuse (§11).** New file `Services/Finance/Submission/InvoiceLineOrigin.cs` (static; no DI, no endpoint, no package). It reuses
`InvoiceSubmissionPersister.LineIdFor`, `ExtractionProvenance.AddedLineIdFor` (now through `AddedLineDecisionIdOf`, which `IsReviewerAdded`
also uses), `BillingDecisionKey.IdOf`, `DecisionRecordWriter.CreatedByColumn` and `InvoiceReviewReadService.ReadPagedAsync`.
`InvoiceSubmissionRerun`'s own-line check (A1) now calls `InvoiceLineOrigin.IsIntakeLineOf` instead of keeping its copy of the rule.
`BillingDecisionRecorder.WriterUserIdAsync` exposes the writer's existing identity read.

**Dev data (read-only check, 2026-11-07).** All 71 lines in dev derive from their invoice's own submission. There are no `add-line` records and
no added lines. No live invoice would be refused.

**Tests** (`InvoiceApprovalLineOriginTests`, 10 cases):

- Must fire: a Fee or Expense line with neither origin; another submission's intake line; the marker and derived id without the record; a
  record not created by the writer identity; a record of another decision, or whose facts name another invoice; a line added on another
  invoice and moved here, even with that invoice's record. Each one checks the 409 code, the text, no writes, no transition, and the one log
  line.
- Fail closed: a fault reading the records is Unavailable, with no writes.
- Must not fire: intake Fee and Expense lines plus a recorded added line approve, and an Adjustment line is not checked; a line detached and
  re-attached to the same invoice keeps its origin (D-96).

**Seeds.** With the check removed, the 8 must-fire cases fail and the 2 must-not-fire cases pass. With the `createdby` filter removed, the
"not created by the writer identity" case fails.

**Fixture changes.** `ApprovalWorld` lines get their own submission's intake id by default (`ownSubmissionId` constructor parameter). Decision
Records the writer creates are read back with `createdby` = the writer identity, as Dataverse stores them. `RecordAddedLineAsync` writes an
`add-line` record through the real recorder. Updated to match: T7 (`InvoiceLineAddServiceTests.FixRound`) records its added line, and the two
persister end-to-end tests build the world from the persister's submission.

**Known limits.**

- K: the check runs once, under the approval locks, before the first write. A change to a line's invoice link after the check, by a path that
  takes no approval lock, is not seen by this approval.
- K: only the own submission counts (stricter than "a linked submission"), consistent with 165a. An invoice with no own submission linked is
  already refused by the 165a hold before this check runs.

## 1.1 Review fix round (item 1)

- **F3, Clean case.** `ALineOfUnknownOrigin_IsRefused` now also runs a stray Fee and a stray Expense line in status Clean. Approval refuses them
  with `Writes == 0` and one log line, so the check runs before the Clean bulk-accept write (`AcceptCleanHeldAsync`).
- **F3, the D-96 test.** The old test set `sprk_invoice` to null and straight back, which proved nothing. It now models the sequence on the
  world's rows: clear each line's `sprk_invoice`, move the invoice's core lookup and each line's core lookup (and the approval portion's core
  stamp) to a new core record with the same role assignments, re-attach the lines, approve, and expect success. The rule holds because it reads
  only the row id, the sequence, the reviewer marker and the invoice lookup, never a core.
- **K4.** The `add-line` record lookup also requires `createdonbehalfby` is null, next to the `createdby` = writer filter, so a row created on
  the writer's behalf by another identity does not count. New must-fire test: `createdby` = writer with `createdonbehalfby` set is refused.
  With the condition removed that test fails; with it, it passes. `DecisionRecordWriter.cs` (ontology-r1) is not changed: its own "already
  recorded" lookups filter on `createdby` alone and have the same gap. Known limit, for the lead to pass on to ontology-r1.
- **K6, message.** `InvoiceLineOrigin.UnknownOriginLinesAsync` now returns `UnknownLine(Id, AddedWithoutRecord)`. When a refused line passes
  `AddedLineDecisionIdOf` (marker, this invoice, derived id) but has no matching record, the refusal text adds: "If a line was added and the add
  did not finish, repeat the add." Every other case keeps the original text. The tests pin both texts.

**Known limits (added by this round).**

- K: the post-check window is unchanged (section 1: a change after the check, by a path that takes no approval lock, is not seen).
- K: the completion path's origin check (a bulk accept finished by `CompleteEarlierBulkAcceptAsync`, and the other completion routes) is in task
  165b-3's scope.
- K: a changed sequence fails closed. An intake line whose `sprk_linesequence` was changed no longer derives its id, so it is refused.
- K: a line with no cost type is refused by the totals calculator only after the Clean bulk-accept write. This ordering predates 165b; task
  165b-2's totals check before any write closes it.
- K: the Decision Record writer's own lookups (`DecisionRecordWriter.cs`, ontology-r1) filter on `createdby` without `createdonbehalfby`. Not changed here.

## 2. Ontology-owned files

Item 1 changes no file in `FinanceBaseValues` or `Services/Signals/**`. No hunks to review.

## 3. Item 2 — which record proves each status (needs a decision; not implemented)

The per-status table (which record proves Approved, Sent to AP and Paid; where it is written; whether dev data has it; its class) is held
privately and was sent to the lead. In short: dev data has a writer-created approval record for each invoice past Approved, so no live invoice
would be refused on that record. Not every status has a record of class (a) or (b) today, so the item-2 rule cannot be written as the brief
states it without a decision from the lead and the owner.

## 4. Proposed split of the rest of 165b

- **165b-2**, item 3 (stored totals equal `InvoiceTotalsCalculator` over the lines, at approval and AP export, 409 with a distinct code) and
  item 4 (pending spend excludes every invoice `InvoiceBlockedHold` holds, in both `FinanceBaseValues` and `FinanceRollupService`; FetchXML
  cannot read the findings JSON, so this needs an in-memory hold evaluation per candidate invoice, which touches an ontology-owned file).
- **165b-3**, item 2, after the decision in section 3.

## 5. Gates

See the task report: filtered unit tests, full ArchTests and the BFF build.
