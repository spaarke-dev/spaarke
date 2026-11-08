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

## 6.2 165b-3 part B

Branch `task/ebilling-165b3` from `6e25f9194d` (part A and its fix round). Rigor FULL. Wording stays neutral: approval and AP export decide only
from server-written sources.

**Rule (EB-D-208).** Approval and AP export count an invoice as Approved, Sent to AP or Paid only when an approve Decision Record the writer identity
created exists for it: `sprk_actioncode` = `approve-invoice`, regarding the invoice, `createdby` = the BFF writer identity and `createdonbehalfby`
empty. Never `ownerid`, never a column a user can write. The writer identity is read app-only from `OntologyWriterDataverseClient.GetWriterUserIdAsync`
(cached WhoAmI), reached through `BillingDecisionRecorder.WriterUserIdAsync`. A fault reading it fails the caller's decision closed (503, or the read
throws); `Guid.Empty` is refused by the helper and never becomes a filter that reads as "no records".

**B0, the rule moved (ontology-r1 O2).** `InvoiceLineOrigin.AddWriterCreatedConditions` is removed. The rule now lives in one helper,
`Services/Signals/WriterCreatedRecord.cs`, next to `DecisionRecordWriter.CreatedByColumn`:

- `AddConditions(FilterExpression criteria, Guid writerUserId)`: the QueryExpression form, on a query's criteria.
- `AddConditions(LinkEntity link, Guid writerUserId)`: the same rule on a link's own criteria.
- `FetchXmlFilter(Guid writerUserId)`: the FetchXML form, a `<filter type="and">` for an `entity` or a `link-entity` element. It is the form 165b-4
  uses for the spend `exists` link.
- `CreatedOnBehalfByColumn`.

The allow-list holds one id today, the BFF writer identity, which callers pass in (O1: a `Guid writerUserId` parameter, no injected provider). Every
ebilling caller uses the helper: the 165b add-line lookup (`InvoiceLineOrigin`), the completion's approve-record lookup
(`InvoiceApprovalService.WriterApproveRecordsAsync`) and the new batched read below. No copy remains in `Services/Finance/**`.

**Services/Signals hunks for ontology-r1's review.** One new file, `Services/Signals/WriterCreatedRecord.cs` (pure, static, no DI registration).
No existing file under `Services/Signals/**` changed.

**Guard.** `tests/Spaarke.ArchTests/WriterCreatedRecordSingleSourceGuardTests.cs` is a source scan of `Services/Finance/**` and
`Services/Signals/**`. Outside the helper file it refuses a `createdby` or `createdonbehalfby` condition in three forms: built with
`AddCondition` / `new ConditionExpression`, built with `new XAttribute("attribute", ...)`, or spelled in a FetchXML string. It has must-fire and
must-not-fire controls, a check that the helper file exists, and a ratchet baseline for ontology's existing sites (DI-144, #2632):
`DecisionCommitService.cs` 1, `DecisionRecordWriter.cs` 3, `SignalReadService.cs` 1, `SignalReevaluationJob.cs` 1. A new site fails, and a count
that shrinks must be lowered. With part A's `InvoiceLineOrigin` restored, the guard fails on `InvoiceLineOrigin.cs:148,149`. The scan does not catch a
column name held in a differently named local, or an in-memory comparison of a row's `createdby` (`GateDecisionRecordSharing`, `RuleSetService`).

**B1, approval.**

- **The batched read.** `InvoiceApprovalService.InvoicesWithApproveRecordAsync(entities, invoiceIds, writerUserId, ct)` returns the invoices that have
  the record. It is ONE paged read (`sprk_regardinginvoice` In the ids, the action code, the helper's conditions) and is shared by approval and AP
  export.
- **The approval state** (`GET .../approval`) reports Approved, with its amount, decider and time, only when the portion says Approved, the invoice
  is Approved, Sent to AP or Paid, AND the record exists. Otherwise the status shows as Unknown. The read is made only when the portion and the
  invoice both say Approved. A fault throws, as every read there does.
- **Completion.** When an Approved invoice has no writer-created approve record, only its claimant's completion continues (part A's re-checks).
  Anyone else is refused and nothing is written. If another person's claim is open, the answer is 409 `ebilling.approval.claimed`, an existing code.
  If there is no claim, the answer is 409 `completion_refused` with the log reason `status_without_record`, part A's code. Before this, such a call
  answered 200 `alreadyApproved` and aligned the portion's status. That alignment now runs only when the record exists.
- **The decision time's lower bound (uac-r2).** At completion, a portion `sprk_decidedon` earlier than the invoice's own server-written `createdon`
  is refused (`decided_on_invalid`, part A's code). An invoice whose `createdon` cannot be read is refused too. `createdon` is read with the invoice,
  so no query is added. No line-decision record is read on the completion path, so the invoice's `createdon` is the bound.
- **Currency (uac-r2 note a).** `InvoiceTotalsCalculator` rounds every total to 2 decimals (USD only in R1); nothing handled currency precision. At
  completion, the amount comparison now refuses a currency outside {USD, EUR, GBP, CAD}, the 2-decimal codes among those intake accepts. JPY and any
  unknown code get 409 `completion_refused` with the log reason `ebilling.approve.completion.currency_unsupported`. No currency is the R1 default
  (USD).

**B2, AP export (lead, 2026-11-08: option 1).**

- **Candidate list.** An Approved invoice without the record is left out of the list.
- **Explicit request.** Requesting it in a batch gives the existing 400 `selection_refused`, with the per-invoice reason `approval_not_recorded`.
  Nothing is claimed. The request-level refusal stays as it is: one refused invoice refuses the whole request, as for every other reason, and there is
  never a silent partial export. A selection refused only for this reason gets its own sentence.
- **Re-check after the claim (step 7).** An invoice whose record no longer counts gives 409 `invoice_conflict` before any file or Sent to AP write,
  and the claims are released.
- **Log line.** Each invoice left out is logged as `AP export refusal ebilling.apexport.approval_not_recorded on sprk_invoice {id}`: the code, the
  table and the row id only.
- **Query count.** One batched read per check: one for the candidate list, and two per batch (the selection and the re-check), however many invoices.
- **Interrupted approve.** An invoice whose approve stopped after its status moved and before its record was written cannot reach Sent to AP. This
  closes the part A re-check's open item.
- **Writer identity.** `ApExportService` takes `BillingDecisionRecorder`, for the writer identity only; it writes no Decision Record.

**Tests.**

- **AP export** (`tests/integration/data-mutation/Ebilling/ApExportApprovalRecordTests.cs`, 7 cases):
  - R1 must fire: left out of the candidates, refused per invoice, nothing claimed, the log line.
  - R1 control: the writer's record exports, with one batched read per check.
  - R3 must fire (2 cases): created by another identity; created on the writer's behalf by one.
  - R3 control: the writer's own record counts.
  - The step-7 re-check.
  - The writer-id fault: 503 with nothing created or claimed, and the candidate list throws.
- **Approval** (`InvoiceApprovalCompletionTests`, 14 new cases):
  - B1 must fire, the state (2 cases): Approved, and Sent to AP.
  - B1 control, the state.
  - B1 must fire, another approver while a claim is open: 409 claimed, nothing written.
  - B1 must fire, no claim: status_without_record.
  - B1 control: another approver after a real approval gets a no-op.
  - R3 must fire on the state (2 cases), and its control.
  - F2 lower bound, must fire and control.
  - Currency (JPY) must fire, and its control (GBP).
  - The writer-id fault: completion Unavailable with nothing written, and the state read throws.
- **Changed test.** `InvoiceApprovalServiceTests` FR-30: another approver's call between the interruption and the claimant's repeat now expects
  409 claimed (not `alreadyApproved`) while no record exists.
- **Fixtures.** `ApprovalWorld` invoices carry `createdon`. `ApExportWorld` gives each invoice in a counted status the writer's approve record by
  default (`approveRecord: false` leaves it out), adds `AddApproveRecord`, and captures the export's log.

**Seeds** (each file restored byte-identical, compared with `cmp`):

- **The record requirement dropped at AP export** (`ApproveRecordedAsync` answers every invoice as recorded). R1 must-fire, both R3 must-fire cases
  and the re-check fail (4); the 3 others pass.
- **The record requirement dropped at approval** (the state's record test forced true; the non-claimant refusal removed). 9 fail: both B1 state
  cases, B1 claimed, B1 no-claim, both R3 state cases, and the 3 FR-30 cases. The controls pass.
- **Also checked.** With the lower bound and the currency check removed, those two must-fire cases fail. With part A's `InvoiceLineOrigin`
  restored, the guard fails.

**Dev data (read-only, 2026-11-08).** Approved, Sent to AP or Paid invoices: 2, both Sent to AP, one USD and one GBP. Each has exactly one approve
record created by the writer identity, with `createdonbehalfby` empty. No approve record on dev has `createdonbehalfby` set. Each portion's
`sprk_decidedon` is later than its invoice's `createdon`. Invoice currencies on dev are USD (5) and GBP (1). No live invoice is refused by part B.

**Escalation triggers.** Not fired:

- An approval in progress: the claimant's own completion still runs, and writes the record.
- Override: decides a held submission and never approves.
- Multi-portion: approve refuses an invoice with more than one portion.
- The allow-list needs only the BFF writer identity.

**Known limits (part B).**

- K: Paid has no record of its own in R1. It counts on the approve record. Mark Paid moves only a Sent to AP invoice, and (6.3, item 6) only one
  that has the writer-created approve record under its approval's key. D-103 counts Paid as Approved.
- K: a portion that says Approved on an In Review invoice with no writer-created record makes every approve answer 409 until an administrator
  acts. This fails closed and nothing is approved. Whoever can edit the portion's columns can therefore hold an approval. It is kept on purpose, so
  an edited portion reaches an administrator. The portion columns are part of the field-security owner question on the dev-deploy checklist.
- K (closed in 6.3, item 3): the "already in target" branch of approve answered `alreadyApproved` without reading the record. The branch is
  reached when the per-invoice lock was defeated OR when the status moved outside this call.
- K: the spend paths (`FinanceBaseValues`, `FinanceRollupService`, `FinancialCalculationToolHandler`) are 165b-4's and are not changed here. They
  will use `WriterCreatedRecord.FetchXmlFilter` / `AddConditions`.
- K: other readers of the counted statuses are not approval preconditions and are unchanged: `InvoiceContextService.StandingInvoiceStatuses` (the
  firm's disallow history) and the correction refusal in `InvoiceSubmissionPersister` (it refuses, so it fails closed).
- K (closed in 6.3, item 2): the approve route itself did not check the currency; approve and completion now run one check.
- K: the AP export client shows `approval_not_recorded` through its generic fallback ("Refused (approval_not_recorded).").
- K: a writer-identity rotation (part A K8) now also hides earlier approvals from AP export and the approval state, until a plan for the earlier
  identity's records exists.

**uac-r2 note b (check only).** A 503 on approve is shown as "try again" in the InvoiceReview client. The route answers `Unavailable` as 503
`ebilling.approval.unavailable`, with the detail "The approval could not be completed; nothing was changed. Try again." and no `failedStep`
(`Api/Finance/InvoiceApprovalEndpoints.cs:252`, through `InvoiceLineReviewEndpoints.Refused`). `decisionFailureMessage`
(`src/solutions/InvoiceReview/src/services/decisionApi.ts:344-356`) shows that detail with its code. `isDecisionIncomplete` (`:336-337`) is false
for it, because the code is not in `INCOMPLETE_CODES` and there is no `failedStep`, so no "saved in part" text is added. The wizard throws that
message (`components/decision/InvoiceDecisionWizard.tsx:159-165`). No finding.

**Gates.** See the task report: filtered unit tests, full ArchTests, BFF build and publish delta.

## 6.3 Part B fix round

Branch `task/ebilling-165b3` from `b81098c4b6`. Wording stays neutral: approval and AP export decide only from server-written sources.

**1. The batched record read matches the decision key (uac-r2 F1).**

- Before: `InvoiceApprovalService.InvoicesWithApproveRecordAsync` (the approval state, the AP export candidates, the selection and the re-check
  after the claim) counted any writer-created `approve-invoice` row regarding the invoice. Completion (`WriterApproveRecordsAsync`) also required
  the key.
- After: a row counts for invoice X only when `InvoiceLineOrigin.ReviewIdOf(row)` equals `BillingDecisionKey.IdOf(sprk_invoice, X,
  DecisionIdFor(portion of X))`, the same match completion makes, with the same helpers. The portion ids come from one batched read of
  `sprk_invoiceapproval` (`sprk_invoice` In the ids); the record read now also selects `sprk_steps`. Two reads per chunk, however many invoices,
  and the record read is skipped when no invoice of the chunk has a portion. Both reads are chunked at 200 (`FinanceBaseValues.ParentChunkSize`,
  item 7). An invoice with no portion does not count (fail closed). An invoice with more than one portion counts when the record matches the key
  of any of its portions (approve itself refuses such an invoice before it writes anything).
- Tests (must fire, a writer-created `approve-invoice` row regarding the invoice under another key): the state shows Unknown; the AP candidate
  list leaves it out and the selection gives 400 with `approval_not_recorded` (another invoice's key, an unrelated key, no key); the re-check after
  the claim gives 409 `invoice_conflict`; Mark Paid gives 409 (item 6). Also must fire: an invoice with the writer's record but no portion.
  Controls: the approval's own key counts (state Approved, candidate, exports).
- Query count: the R1 control still sees one `sprk_decisionrecord` read for the candidate list and two for an export (selection and re-check),
  plus one `sprk_invoiceapproval` read for the candidate list.

**2. Currency (review F1).**

- Before: completion refused any currency outside {USD, EUR, GBP, CAD} (so JPY, AUD and `$` were refused there), and approve did not check the
  currency at all.
- After: one check, `InvoiceApprovalService.CurrencyRefused`, in `CheckApprovableAsync`, which approve runs before its claim and completion runs
  again, so the two never disagree. The text is read through `BillingCurrency.Normalise` (D-119: `$`, `US$`, `USD$` are USD). A missing currency
  is the R1 default, USD (D-115), and passes. Refused: text that does not normalise to a 3-letter code (fail closed), and the ISO codes with more
  than 2 minor-unit decimals, one named set: BHD, KWD, JOD, OMR, TND, LYD, IQD (`InvoiceTotalsCalculator` rounds to 2 decimals). The refusal is
  409 `ebilling.approve.currency_unsupported` at approve; at completion it is 409 `completion_refused` with that code as the log reason. The code
  replaces `ebilling.approve.completion.currency_unsupported`.
- Tests: `$`, `AUD`, `JPY`, `usd` and a missing currency approve; `$`, `AUD`, `JPY` and a missing currency complete an interrupted approve.
  Must fire: `KWD`, `bhd`, `CA$` and `EURO` are refused at approve with nothing claimed or written; `KWD` is refused at completion.

**3. The "already in target" branch (review F1, low).**

- Before: when the transition answered "already in target", approve answered `alreadyApproved` without reading the record.
- After: it reads `WriterApproveRecordsAsync(invoiceId, decisionId)`. With the record it answers `alreadyApproved` naming the record; without it,
  `Incomplete` at step `transition`, so the claimant's repeat runs completion and its checks. The branch is reached when the per-invoice lock was
  defeated OR when the status moved outside this call. A read fault there is also `Incomplete` at `transition`.
- Tests: must fire, the status moved to Approved before the transition with no record: Incomplete, no record; the claimant's repeat completes
  with one record. Control: with the writer's record under the key, `alreadyApproved` names it.

**4. The guard's baseline (review F3).** Each entry of `WriterCreatedRecordSingleSourceGuardTests.Baseline` now carries its own reason (the method
and line of each site) and the citation `ADR-038 A1; EB-D-208; DI-144 (#2632)`. A1 is the amendment that makes ArchTests a KEEP path; the rule
that each allowlist entry carries a reason and an ADR citation is `tests/CLAUDE.md`'s. A new control checks every entry has a distinct reason and an
ADR citation. The remarks say the seventh DI-144 site (`GateDecisionRecordSharing.cs:521`, an in-memory comparison) is invisible to the scan, and
hold the maintenance procedure.

**5. The guard's patterns (review K2).** Added, each with a must-fire fixture: `AttributeName = <column>` (a `ConditionExpression` initializer), a
named `attributeName: <column>` argument, `LinkFromAttributeName = <column>`, and the Web API `_createdby_value` / `_createdonbehalfby_value`. The
must-not-fire fixture's line that did not compile (`LinkCriteria.Add(FetchXmlFilter(...))`, an `XElement` into a `FilterExpression`) now reads
`WriterCreatedRecord.AddConditions(link, writer)` and `linkElement.Add(WriterCreatedRecord.FetchXmlFilter(writer))`, and the fixture gains the
four new forms on `sprk_createdbyperson`. Re-check: the new patterns find no site in `Services/Finance/**` or `Services/Signals/**`, so the baseline
is unchanged (6 sites in 4 files).

**6. Mark Paid (review K1).**

- Before: `POST .../mark-paid` moved Sent to AP to Paid without the record.
- After: it first runs the export's batched check (`ApExportService.MarkPaidApprovalAsync`, item 1's read, with the key). Without the record the
  lifecycle is not called and nothing is written: a Sent to AP invoice is 409 `ebilling.apexport.approval_not_recorded`, logged as
  `Mark Paid refusal ebilling.apexport.approval_not_recorded on sprk_invoice {id}`; an invoice in any other status keeps the answer FR-42 gives it
  (409 `invoice.transition_not_allowed` with `currentStatus`, `invoice.status_unknown`, or the uniform 404 when it does not exist). A read fault is
  503 `ebilling.invoice.payment_unavailable`.
- Tests: must fire (no record, another key, another identity): 409, the lifecycle not read, nothing written. Control: the record under the key,
  200 and Paid. The 151a FR-42 tests pass unchanged.

**7. Small items.** The FR-30 test chooses its expectation from `step` (only `link` fails after the record), not from state read after the call.
The misplaced `<summary>` in `ApExportService.cs` is back on `FirmMissingStop`. The new `In` reads are chunked at 200.

**Seeds** (each file restored byte-identical, compared with `cmp`):

- The key match dropped from the batched read: 6 must-fire fail (AP key mismatch x3, the re-check, the state, Mark Paid another-key); the controls
  pass.
- The approve-time currency check removed: `KWD` reaches the claim; 5 fail (approve x4, completion x1).
- The record read removed from "already in target": its must-fire fails, the control passes.
- The Mark Paid check removed: its 3 must-fire fail; the FR-42 tests pass.

**Dev data (read-only, 2026-11-08).** Both counted invoices (Sent to AP: INV-2025-05166 USD, INV #2025023029 GBP) still count: each has one portion,
and its approve record (created by the writer identity, `createdonbehalfby` empty) carries exactly the key derived from that portion. Currency over
all 6 live invoices, normalised: USD 5, GBP 1. None is refused.

**Known limits (this round).**

- K: the approval state's `canApprove` does not reflect the currency check (approve refuses with 409 `currency_unsupported`).
- K: Mark Paid's status read on the refusal path mirrors the lifecycle's answers for a status other than Sent to AP; the lifecycle stays the only
  status writer.

## 6.4 Part B re-check and merge (2026-11-08)

uac-r2 closed F1 at `4ba88a6de1` and approved part B. A scoped Sonnet re-check of the fix diff found no fix-now item. Merged into work as `5ae5590b66`.

Known limits recorded from the re-check:

- **K1: time of check.** In Mark Paid, the keyed record check and the lifecycle transition are two calls, not one atomic step. Removing the record between them needs the writer identity.
- **K2: any-portion match.** The batched read matches at invoice level, so a record for one portion counts for the invoice. Approve refuses invoices with more than one portion (`PortionAmbiguous`), so this does not happen on a normal path.
- **K3: closed currency list.** The refused set is the seven 3-decimal ISO codes. CLF and UYW (4-decimal unit-of-account codes, not invoice currencies) are not listed. A future 3-decimal currency is a manual addition.
- **K4: guard upkeep.** The baseline reasons quote line numbers that will go stale. Every entry carries the same citation. `LinkToAttributeName` and positional `LinkEntity` arguments are not detected. Mark Paid's answers for statuses other than Sent to AP repeat the lifecycle's answers and could drift.
- **The re-check's F4 does not apply.** `MarkPaidAuthorizationTests` asserts `StatusReads == 0`. That counter sits at the lifecycle's own Dataverse boundary (`ApExportRoutesHost.LifecycleRecords`), and the lifecycle reads status before any write. So the assertion does prove the lifecycle was not reached.

## 7. 165b-2 — stored totals must match the lines at approval and AP export

Task `tasks/165b2-totals-and-pending-spend.poml`, item 3 only (item 4, pending spend, moved to 165b-4). Rigor FULL. Branch `task/ebilling-165b2` from
`work/ebilling-r1` at `c66b6933cf` (165b-3 merged). Wording stays neutral: approval and AP export decide only from server-written sources.

**Rule (EB-D-200 item 3).** An invoice's five stored totals (`sprk_feestotal`, `sprk_expensestotal`, `sprk_lineadjustmentstotal`,
`sprk_invoiceadjustmentstotal`, `sprk_approvedtotal`) must equal what `InvoiceTotalsCalculator` derives from its current lines with its stored billed
total (`sprk_totalamount`). The one computation is reused, never copied: `InvoiceTotalsService.StoredTotalsOf(invoice, lines)` runs
`InvoiceTotalsCalculator.Calculate` over the counted lines through `InvoiceTotalsService.ToRow` / `IsCounted`, the same mapping `RecalculateAsync` and
the 165b-3 completion use (D-42: approved = billed + added lines - line adjustments - invoice adjustments).

**The comparison.** They match when the calculation is not refused AND, for each of the five columns, `InvoiceTotalsCalculator.Round(stored) ==
calculated` (exact `decimal` equality; `Round` is the calculator's own rounding, 2 decimals, midpoint away from zero, now `internal` instead of
`private`). A missing stored total does not match. Decimal equality ignores scale, so a stored `885.0000` equals `885`.

- Why 2 decimals and not the currency's own: the five columns are Money with precision 2 (`docs/data-model/sprk_invoice.md`) and the calculator
  rounds every total to 2, so a total the server wrote is always exactly equal at 2 decimals. `BillingCurrency` holds no minor-unit table (it
  normalises the code only). Every currency approval accepts has 2 decimals or fewer: `CurrencyRefused` (through `BillingCurrency.Normalise`) refuses
  the 3-decimal codes before this check runs. Comparing a 0-decimal currency (JPY) at 0 decimals would accept a stored total that differs from the
  server's own value by up to half a unit, so the stricter 2-decimal equality is used for every currency. No legitimate difference exists, so the
  escalation trigger (a rounding or currency rule the calculator does not cover) did not fire.

**Approval (T1).** In `InvoiceApprovalService.CheckApprovableAsync`, last, after the 165b line-origin check: under both per-invoice locks, on the
line set approval already reads, with the invoice row approval already reads (the stored totals were added to `InvoiceColumns`; no extra query),
before any write (the Clean bulk accept included).

- A difference: 409 `ebilling.approve.totals_mismatch`, text "This invoice's stored totals do not match its lines, so it cannot be approved. Nothing
  was changed. An administrator must review the invoice." One log line `Approval refusal ebilling.approve.totals_mismatch on sprk_invoice {id}`.
- Lines the calculator refuses (for example a line with no cost type): the answer approve already gave for them, 409
  `LineDecisionCodes.TotalsRefused` with the calculator's code as the detail, now before any write. This closes the section 1.1 known limit ("refused
  by the totals calculator only after the Clean bulk-accept write"). Logged the same way with the calculator's code.
- Completion runs the same method (165b-3), so a completion whose stored totals differ is 409 `completion_refused` with the log reason
  `ebilling.approve.totals_mismatch`, no approve record. Completion's amount check now uses the calculation the check ran (one calculator run, not
  two); its own refused-calculation branch could no longer be reached and was removed. This closes 165b-3 part A known limit K1 (section 6.1).

**AP export (T2).** Left out the way 165b-3 leaves out an invoice without its approve record (lead brief: "the same way"):

- **Candidate list:** not offered, so the other candidates export without it.
- **Explicit request:** the per-invoice reason `totals_mismatch` in the existing 400 `selection_refused`; nothing is claimed. As decided for
  `approval_not_recorded` (lead, option 1), one refused invoice refuses the request; there is never a silent partial export. A selection refused only
  for this reason gets its own sentence.
- **Re-check after the claim (step 7):** the stored totals are re-read with the step-7 invoice read and compared with a fresh line read; a difference
  is 409 `ebilling.apexport.totals_mismatch` before any file or Sent to AP write, and the claims are released.
- **Log line:** `AP export refusal ebilling.apexport.totals_mismatch on sprk_invoice {id}`.
- **Query count:** one batched line read per check (an `In` on the invoice lookup, chunked at `FinanceBaseValues.ParentChunkSize`, paged, removed
  Adjustment lines excluded by `ExcludeInactiveAdjustments`): one for the candidate list, two per batch. Never one per invoice.

**Reuse (§11).** No new type file, endpoint, DI registration or package. New surface inside existing files:

- `InvoiceTotalsService.StoredTotalsOf` with its `StoredTotals` record and `StoredTotalsColumns`. (1) Existing: `RecalculateAsync` computes and
  writes; the 165b-3 completion ran the calculator inline; nothing compared stored with derived. (2) Extension: this is the extension, placed on the
  totals owner so approval and AP export share one comparison; it calls the calculator and copies none of its arithmetic. (3) Without it each caller
  would carry its own copy of the comparison (F2).
- `InvoiceTotalsService.InvoicesWithMatchingStoredTotalsAsync`. (1) Existing: `InvoiceTotalsService.ReadLinesAsync` (one invoice) and the export's
  `ValueReader.ReadLinesAsync` (one invoice, for the file). (2) Neither is batched; the line columns are now one list (`CalculatorLineColumns`) shared
  with `ReadLinesAsync`. (3) Without it AP export makes one line query per invoice (F3) or cannot check at all.
- `InvoiceApprovalCodes.TotalsMismatch`, `ApExportCodes.ReasonTotalsMismatch` and `ApExportCodes.TotalsMismatch`: the codes the POML names; without
  them the refusal has no stable code.
- `InvoiceApprovalService.CheckStoredTotals` and `ApExportService.TotalsMatchAsync`: private, the refusal and log line of each caller (the pattern of
  `CheckLineOriginAsync` and `ApproveRecordedAsync`).

**Tests.**

- **T1** (`tests/unit/Sprk.Bff.Api.Tests/Services/Finance/InvoiceApprovalTotalsTests.cs`, 12 cases). Must fire, each with the code, the text,
  `Writes == 0`, no transition, no record and the one log line: each of the five stored totals off by one cent (5); a line amount edited without a
  recalculation; a missing stored total; lines the calculator refuses (TotalsRefused before any write); completion with a stored total that differs.
  Controls: matching totals approve and freeze the calculator's amount (885, with a Reduced line, a Clean line and a 10% adjustment); the same
  amount at another scale matches; completion with matching totals completes with one record.
- **T2** (`tests/integration/data-mutation/Ebilling/ApExportTotalsTests.cs`, 10 cases). Must fire (8 rows): each of the five stored totals off by one
  cent, a line edited, lines the calculator refuses, a missing total. Each: not a candidate while the other invoice is, the log line, an explicit
  request refused per invoice with nothing written, and the other invoice then exports. The step-7 re-check (a line changed after the selection):
  409 `totals_mismatch`, no file, nothing sent, the claim released. Control: two matching invoices (one with a Reduced line and an adjustment) are
  candidates and export, with one batched line read for the candidate list.

**Seeds** (each file restored byte-identical, compared with `cmp`):

- **The check dropped at approval** (`CheckApprovableAsync` returns no refusal for the stored totals): the 9 T1 must-fire cases fail; the 3 controls pass.
- **The check dropped at AP export** (`TotalsMatchAsync` still reads the lines but answers every invoice as matching): the 9 T2 must-fire cases fail;
  the control passes.

**Fixture changes.** `ApprovalWorld` and `ApExportWorld` invoices now carry the totals the calculator derives from their lines
(`StoreTotalsFromLines`, run on creation and by `AddLine`, as the totals service runs after every line change), and the approval world's
`RecalculateAsync` stand-in stores all five totals (it stored only the approved total). `ApExportWorld.AddInvoice` stores the requested approved total
as the billed total (no line is decided, so approved = billed); its earlier fixed 1,000 / 60 / 40 did not derive from its lines. Its Adjustment lines
are Fixed adjustments whose value is their amount (the calculator refuses a line with no method). Updated to match: the FR-41 CSV row
(`900.00,0.00,0.00,900.00`), the 165b-3 C2 control and the two persister end-to-end tests (each stores the totals after setting its lines directly).

**Dev data (read-only, 2026-11-08).**

- Invoices: 6 (2 In Review, 2 Sent to AP, 2 Superseded); lines: 71. Every line is a Fee or Expense line, Active, and Clean, Flagged or Accepted; no
  Reduced or Disallowed line, no Adjustment line. For each invoice the stored fees and expenses equal the sums of its lines, both adjustment totals are
  0 and the approved total equals the billed total (the two invoices with no lines store 0 fees and expenses, and approved = billed).
- **Live invoices whose stored totals differ from their lines: 0. None would be refused at approval or AP export.**
- For 165b-4: held invoices now counted in pending spend: **0**. Pending spend counts In Review (`FinanceBaseValues.PendingInvoiceStatuses`); both In
  Review invoices have one linked submission, processing status Accepted, recorded outcome `Accepted` naming that invoice, so neither is held by the
  column state or the recorded outcome. (The own-submission derivation was not recomputed here; 165b-4 runs the real rule.)

**Gates.** `dotnet build src/server/api/Sprk.Bff.Api/`: 0 warnings, 0 errors. Full ArchTests: 2174 passed, 0 failed. Filtered unit tests (Finance,
ApExport, Approval, MarkPaid, Submission): 7474 passed, 0 failed, 4 skipped (7452 before the new tests). Publish (`dotnet publish -c Release`):
fresh master `57748ebf6d` 130.60 MB, this task's base `c66b6933cf` 131.47 MB, this branch 131.47 MB. This task's delta is +9.7 KB on
`Sprk.Bff.Api.dll` and about 0 in total; the +0.87 MB against master is the unmerged ebilling work already on `work/ebilling-r1`. No package added.

**Escalation triggers.** Not fired: no rounding or currency rule makes a server-written total differ from the calculator (the column precision and
the calculator are both 2 decimals), and no live invoice would be refused.

**Known limits.**

- K: the window after the step-7 re-check. A line changed out of band between the re-check and the file read (step 8) is not seen; the file reads
  the lines once. The same window exists for every step-7 check.
- K: an interrupted recalculation leaves stored totals that differ from the lines until the next line change recalculates. The trigger is any fault
  in the recalculation that follows a committed line decision, add-line or adjustment change (the totals read, the Adjustment line amounts write, or
  the invoice totals write, including its three failed attempts), not only a failed totals write: that call returns Incomplete at "totals".
  Approval and AP export refuse meanwhile (fail closed). There is no administrator recalculate tool; the remedy is to repeat the last line or
  adjustment action, which recalculates (the refusal text says so, fix round 1).
- K: the approval state's `canApprove` does not reflect the totals check (as for the currency check, section 6.3); approve answers 409.
- K: the clients show `totals_mismatch` (approval and AP export) through their generic fallback, as for `approval_not_recorded`.
- K: at completion, lines the calculator refuses are now logged with the reason `LineDecisionCodes.TotalsRefused` (the shared check's code), not the
  calculator's own code as in section 6; still 409 `completion_refused`, nothing written.
- K: Mark Paid does not run the totals check (the POML scopes approval and AP export). A Sent to AP invoice already passed it at export.- K: an Adjustment line's own stored `sprk_amount` is not compared. It is a line column; the totals derive from the line's method and value, and the
  invoice adjustments total, which is compared, shows a changed value.
- Interpretation for the lead: the POML says an invoice that fails AP export "is left out of the batch, not the whole batch failed"; the brief says
  "the same way" as 165b-3. This follows 165b-3: left out of the candidates, refused per invoice in an explicit request (option 1: no silent partial
  export), and a whole-batch 409 at the step-7 re-check (a change during the export). If the lead wants an explicit request to export the rest
  without the refused invoice, that changes the request-level rule for every reason.

### 7.1 Fix round 1 (165b-2, adversarial pass)

- **F4-1: the refusal names the real remedy.** Approval's `ebilling.approve.totals_mismatch` now says "The stored totals do not match the invoice lines.
  Repeat the last line or adjustment decision, then approve again."; AP export's per-invoice selection refusal says to repeat the last line or
  adjustment decision on the invoice and select it again. The earlier "an administrator must review" pointed at a tool that does not exist. The
  known limit above was corrected: any fault in the recalculation after a committed change is the trigger. (The step-7 re-check's 409 text, "An
  administrator must review it", is unchanged: outside this round's list; for the lead.)
- **F4-2: the export fixture has distinct adjustment totals.** In the FR-41 test, invoice A is billed 1000 with a Fee line of 1000 reduced to 940 (line
  adjustments 60) and a Fixed invoice adjustment of 40 (invoice adjustments 40), approved 900; the stored totals are those the calculator derives from
  the lines. The CSV row is `1000.00,60.00,40.00,900.00` again (the JSON fee line's billed is 1000). Seed: with the line-adjustment and invoice-adjustment
  columns swapped in the export mapping the test fails; restored, it passes (the 165b-2 earlier fixture, 0.00 / 0.00, could not show a swap).
- **K2: the export writes the re-checked values.** The step-7 re-check validated the freshly read rows, but the files are written from the rows read
  before the claim. It now also requires each invoice's fresh stored totals (the billed total and the five totals, `StoredTotalsColumns`) to equal the
  ones on the row the file is read from; otherwise the same 409 `ebilling.apexport.totals_mismatch`, claims released, one log line per changed
  invoice. No extra query (the fresh rows were already read). Tests: must fire (a line and the stored totals changed together after the claim:
  409, no file, nothing sent, claim released) and a control (the totals stored again unchanged after the claim: exports).
