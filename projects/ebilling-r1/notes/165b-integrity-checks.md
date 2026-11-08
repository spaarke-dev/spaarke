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

## 6. 165b-3 part A — approval completion decides only from server-written sources

Task `tasks/165b3-approval-completion-and-status-record.poml`, part A. Rigor FULL. Branch `task/ebilling-165b3` from `work/ebilling-r1` at
`d415d37508`.

**Rule.** When an approve call finds the invoice already Approved (the completion path, `CompleteOrNoOpAsync`):

- **Complete** means a writer-created approve Decision Record exists for the approval's decision id: `sprk_actioncode` = `approve-invoice`,
  regarding the invoice, key `BillingDecisionKey.IdOf(sprk_invoice, invoiceId, DecisionIdFor(approvalId))`, `createdby` = the writer identity
  and `createdonbehalfby` empty. The portion's link counts only when it names that record. The portion's own columns never decide it.
- **Before completion writes the approve record** (no such record yet), the approval checks run again, under the same two per-invoice locks:
  the caller is still an eligible approver; the FR-20 hold, the firm gate, the Procedure hold, the unfinished corrections, I-2 and the 165b line
  origin all pass (one shared method, `CheckApprovableAsync`, which approve itself now calls); no Clean Fee or Expense line is undecided (approve
  accepts them in bulk before its claim, so a completion never has one); and the portion's approved amount equals the approved total
  `InvoiceTotalsCalculator` derives from the lines read now (`InvoiceTotalsService.ToRow`, the same mapping and rounding as the totals
  service).
- **On a refusal** nothing is written: 409 `ebilling.approve.completion_refused`, and one log line
  `Approval completion refusal {ReasonCode} on sprk_invoice {id}`, where the reason code is the failed check's code, or
  `ebilling.approve.completion.clean_undecided`, `ebilling.approve.completion.amount_mismatch`, or the calculator's refusal code. A read fault
  is 503 Unavailable, nothing written. The response text does not name the check.
- **Once the record exists**, a repeat finishes the remaining steps (portion status, Signals, link) without re-running the checks: the record
  is already the server's, and the writer answers it again under the same id and content, so no second record is written.

**Reuse (§11).** No new type, endpoint, DI registration or package. `InvoiceLineOrigin.AddWriterCreatedConditions` is now the one place for the
writer-identity rule (`createdby` and `createdonbehalfby`); the 165b add-line lookup and the new approve-record lookup both call it, and both
match the key with `InvoiceLineOrigin.ReviewIdOf`. The approve checks moved unchanged into `EligibleApproverAsync` and `CheckApprovableAsync`
(approve and completion call the same methods). One behaviour detail moved: approve now reads the caller's contact before the eligible set (both
are reads before any write; a fault in either is still Unavailable).

**Tests** (`InvoiceApprovalCompletionTests`, 13 cases):

- C1 must fire (6 cases): the invoice in the post-transition state with a failing check — the caller not eligible, the FR-20 hold, no firm, a
  Flagged line, a line of unknown origin, an undecided Clean line. Each: 409 `completion_refused`, no approve record, no write, the one log
  line. Control: an approve interrupted after its transition is completed by its approver's repeat with one record.
- C2 must fire: an approved amount that differs from the line-derived total, both in the post-transition state and after a real interruption
  before the record. Control: equal amounts complete.
- C3 must fire (2 cases): a portion that says Approved and links an approve record created by another identity, or on the writer's behalf by
  one, is not complete; the checks run (and refuse here). Control: the writer's own record makes it complete (a no-op).
- C4: a replay after the approval finished writes nothing; with the portion's link lost, the replay relinks the same record and writes no
  second one.

**Seeds.** With the completion re-check removed, the 9 must-fire cases of C1, C2 and C3 fail and the 4 controls pass. With "complete" decided
from the portion's link again, the 2 C3 cases fail and the other 11 pass.

**Fixture change.** `ApprovalWorld` now reads back a record the writer created with the id its create returned (the writer harness's `NewId`),
as the harness's own lookups do, so a reader that keys on the row id sees the record the approval linked.

**Dev data (read-only, 2026-11-07).** Approved, Sent to AP or Paid invoices: 2 (both Sent to AP). Each has exactly one approve record created by
the writer identity (`mi-ontology-writer-dev`), `createdonbehalfby` empty, with the derived key; each portion links it. Approvals in flight
(a decider on a Pending portion, or an Approved invoice with no approve record): 0. No live approval would be refused at completion.

**Escalation triggers.** Not fired. Multi-portion: approve refuses an invoice with other than one portion before the completion path is
reached. Override decides a held submission and never approves. The approver's repeat after an interrupted approve meets the amount equality
(the amount was frozen from the same lines).

**Known limits (part A).**

- K: the checks run once, under the approval locks, before the record is written. A change after that by a path that takes no approval lock is
  not seen by this completion.
- K: a legitimate path that changed an Approved invoice's lines or billed total would make completion refuse (fail closed). None is known: line
  decisions, Add line and adjustments refuse an invoice that is not In Review.
- K: the Decision Record writer's own "already recorded" lookup (`DecisionRecordWriter.cs`, ontology-r1; DI-144) filters `createdby` without
  `createdonbehalfby`. Completion's lookup applies both, and the writer is reached only after the completion checks pass. Not changed here.
- K: field security on the invoice status and the portion's columns is an owner schema question, out of scope.
- K: the D-72 Unreviewed rows are written by approve before its claim; completion does not write them again.

## 6.1 165b-3 part A review fix round

Branch `task/ebilling-165b3`. Wording stays neutral: approval completion decides only from server-written sources.

**F1 — a portion that says Approved on an invoice that is not Approved.** `CompleteOrNoOpAsync` now reads the writer-created approve records first.
When there are none and the invoice status is not Approved, the call is refused: 409 `ebilling.approve.completion_refused`, log reason
`ebilling.approve.completion.status_without_record`, nothing written, one log line with the reason code, table and row id. Before this, the portion
column alone made the call answer 200 `alreadyApproved`, and the invoice could then never be approved. The approval state (`GET .../approval`,
and the state returned with an approve) no longer reports Approved from the portion column alone: it reports Approved (with the approved amount,
decider and time) only when the invoice itself is Approved, Sent to AP or Paid; otherwise the status shows as Unknown and `canApprove` is false.
The portion's record link in the state is unchanged (it is shown for every decided status, including Rejected and Returned).

**F2 — the decision time.** Completion takes the decision time from the portion's `sprk_decidedon`, a user-writable column. `CheckCompletionAsync`
now refuses (409 `completion_refused`, log reason `ebilling.approve.completion.decided_on_invalid`, nothing written) when it is missing or later
than the service clock plus `LockDuration` (5 minutes, the skew tolerance between nodes). The clock is the service's `TimeProvider`. A time in
the past completes. A repeat that already has the writer's record is unchanged (the record's stored set is used, not the time).

**F3 — the C2 control with adjustments.** New control: a real interruption (the recorder's contact read faults after the transition) on an invoice
with one Reduced line and one Percent adjustment line (10%); the repeat completes and the portion's amount and the record carry 810 (billed 1,000,
less the 100 reduction, less 10% of the 900 fee base: D-42). `ApprovalWorld` now honours the requested `ColumnSet` for the line table (a line read
returns only the columns it asks for). That was cheap: no existing test changed. Other tables still return whole rows, as their readers were written
against.

**K3 — a cheap guard.** On the completion path where no writer-created approve record was found, an `AlreadyRecorded` answer from `RecordAsync` means
the record it names is not one the writer created under the completion's filter (`createdby` the writer, `createdonbehalfby` empty). It is refused:
409 `completion_refused`, log reason `ebilling.approve.completion.record_not_writer_created`; the record is not linked and no Signal is closed
against it. The portion's status may already have been written to Approved by that point (the portion step comes before the record step), so this
refusal is not "nothing written"; a repeat meets the same refusal. Test: a record whose `createdby` is the writer and `createdonbehalfby` is another
identity, which the writer's own lookup (DI-144, below) finds and the completion's lookup does not.

**K4 — one id per created row.** `ApprovalWorld` now answers each create with its own id (before, the writer harness returned one shared id for
every create). New C3 case: the portion links a different writer-created approve record (another invoice's) and this invoice has none of its own:
not complete, the completion checks run and refuse. The existing C3 test no longer re-ids its record (it keeps the id the portion links).

**Seeds (this round).** Without the F1 refusal, the 2 F1 must-fire cases fail (and the other 20 pass). Without the F2 refusal, the 2 F2 must-fire
cases fail. Without the K3 refusal, the K3 case fails. Each file was restored byte-identical (compared with `cmp`) before the gates ran.

**Known limits (added by this round).**

- K2: an approver removed from the core record's approver roles between the interrupted approve and their repeat. The invoice is Approved, the
  portion names that person as decider (status Pending or Approved) and no writer-created approve record exists. The repeat is refused at
  completion (eligibility is checked again, live), and no one else can complete another person's claim. *Manual remedy for an administrator.*
  State: invoice status Approved, portion `sprk_decidedby` set, no `sprk_decisionrecord` row with `sprk_actioncode` = `approve-invoice`,
  regarding that invoice, created by the writer identity. Finding it: the log line `Approval completion refusal ebilling.approver.not_eligible
  on sprk_invoice {id}`; or list Approved invoices whose portion has a decider and no linked approve record. What to do: (1) put the person back on
  the core record's approver roles and ask them to repeat the approve, which completes it (one record); (2) if they cannot return, take the case to
  the owner. R1 has no route that returns an Approved invoice to In Review, so any data repair is an owner decision under change control. Do not
  edit the portion's status, amount or record link, and do not create a Decision Record by hand: completion decides only from server-written
  sources, so either edit is refused or ignored.
- K1: the amount check compares the portion's approved amount with the total `InvoiceTotalsCalculator` derives from the lines read at completion.
  The lines and the invoice's billed total are not themselves verified until 165b-2 (stored totals equal the calculator over the lines) merges; the
  completion path will call that check then (one shared method, not a copy).
- K8: if the writer identity rotates (the ontology writer's application user changes), approve records created by the earlier identity stop counting
  as writer-created. A completion on an invoice approved under the old identity would then find no record and run its checks again; the
  F1 refusal applies to an invoice that is not Approved. Rotation needs a plan for the records of the earlier identity (owner/ontology-r1) before it
  happens.
- DI-144 (ontology-r1): `DecisionRecordWriter`'s own "already recorded" lookups filter `createdby` without `createdonbehalfby`. Part A does not change
  that file; the K3 guard covers the completion path. For the lead to pass to ontology-r1.
- K: the portion's record link in the approval state is the stored column as is (not checked against the writer's record).
